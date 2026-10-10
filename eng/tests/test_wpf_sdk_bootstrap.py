#!/usr/bin/env python3
"""Run SDK evaluation and initializer isolation controls; these are not UI qualification."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[2]
SDK = REPO / "packaging/ProGPU.Wpf.Sdk"
DOTNET = shutil.which("dotnet")


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def run(root, *args):
    result = subprocess.run([DOTNET, *args], cwd=root, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
    if result.returncode:
        raise AssertionError(f"{args}:\n{result.stdout}")
    return result.stdout


def project(root, name, code, references=(), items="", constants="", executable=False):
    references = "".join(f'<ProjectReference Include="../{ref}/{ref}.csproj" />' for ref in references)
    write(root / name / (name + ".csproj"), f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>{"Exe" if executable else "Library"}</OutputType>
    <DefineConstants>{constants}</DefineConstants><ImplicitUsings>enable</ImplicitUsings>
    <UseAppHost>false</UseAppHost><NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>{references}{items}</ItemGroup>
</Project>''')
    write(root / name / "Code.cs", code)


class BootstrapTests(unittest.TestCase):
    def test_sdk_selects_only_requested_desktop_startup(self):
        cases = (
            ("wpf", "true", "false", False, "true", "Exe", True, True),
            ("forms", "false", "true", False, "true", "WinExe", True, False),
            ("mixed", "true", "true", False, "true", "Exe", True, True),
            ("default-wpf-sdk", "", "", False, "true", "Exe", True, True),
            ("headless", "false", "false", False, "true", "Exe", False, False),
            ("web", "", "", True, "true", "Exe", False, False),
            ("web-forms", "false", "true", True, "true", "Exe", False, False),
            ("web-mixed", "true", "true", True, "true", "Exe", False, False),
            ("opt-out", "true", "true", False, "false", "Exe", False, False),
            ("library", "true", "true", False, "true", "Library", False, False),
        )
        with tempfile.TemporaryDirectory(prefix="librewpf-bootstrap-policy-") as temporary:
            root = Path(temporary).resolve()
            for name, wpf, forms, web, enabled, output, included, activates_wpf in cases:
                with self.subTest(name=name):
                    web_props = '<Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk.Web" />' if web else ""
                    web_targets = '<Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk.Web" />' if web else ""
                    write(root / "Policy.csproj", f'''<Project>
  {web_props}
  <Import Project="{escape(str(SDK / 'Sdk/Sdk.props'))}" />
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework><OutputType>{output}</OutputType>
    <UseWPF>{wpf}</UseWPF><UseWindowsForms>{forms}</UseWindowsForms>
    <ProGpuWpfEnablePortableBootstrap>{enabled}</ProGpuWpfEnablePortableBootstrap>
  </PropertyGroup>
  {web_targets}
  <Import Project="{escape(str(SDK / 'Sdk/Sdk.targets'))}" />
</Project>''')
                    output = run(root, "msbuild", "Policy.csproj", "-nologo",
                                 "-getItem:Compile", "-getProperty:DefineConstants")
                    # Combined SDK imports may print duplicate-import warnings
                    # before the structured evaluation result.
                    result = json.loads(output[output.index("{"):])
                    sources = [item["Identity"] for item in result["Items"]["Compile"]]
                    self.assertEqual(included, any(path.endswith("ProGPU.Wpf.Sdk.PortableBootstrap.cs") for path in sources))
                    constants = result["Properties"]["DefineConstants"].split(";")
                    self.assertEqual(activates_wpf, "PROGPU_WPF_BOOTSTRAP_WPF" in constants)

    def test_referenced_executable_does_not_start_or_load_desktop_services(self):
        # Deliberate test doubles live only in this temporary compiler fixture.
        # Removing their DLLs proves actual module/JIT behavior, not UI parity.
        with tempfile.TemporaryDirectory(prefix="librewpf-bootstrap-runtime-") as temporary:
            root = Path(temporary).resolve()
            write(root / "NuGet.Config", '<configuration><packageSources><clear /></packageSources></configuration>')
            project(root, "TestDesktopTypes", '''namespace System.Windows {
public class Application { }
public class Clipboard { }
}
namespace System.Windows.Forms.Integration {
public class WindowsFormsHost {
 public static void EnableWindowsFormsInterop() => Console.WriteLine("INTEROP");
}}
''')
            startup_source = REPO / "src/ProGPU.Wpf/ProGpuWpfStartupOptions.cs"
            project(root, "TestStartupBridge", '''namespace System.Windows.Media.ProGPU;
public enum ProGpuWpfRendererMode { NativeMilWgpu }
public class ProGpuWpfWindowOptions {
 public ProGpuWpfRendererMode RendererMode { get; set; }
 public bool EnableNativeModalSessions { get; set; }
 public bool EnableNativeMilHitTesting { get; set; }
}
public class ProGpuWpfWindowHost { public ProGpuWpfWindowHost(object options) { } }
public static class ProGpuWpfNativeMediaServices {
 public static void Initialize() => Console.WriteLine("NATIVE MEDIA");
}
public static class WpfPortableWindowActivation {
 public static object CreateHostOptions(object window, ProGpuWpfWindowOptions options) => options;
 public static bool TryRegisterPresentationFrameworkActivation(Func<object, ProGpuWpfWindowHost> create = null) {
  Console.WriteLine("WPF ACTIVATION"); return true;
 }
 public static void TryRegisterPresentationCoreClipboardService() => Console.WriteLine("WPF CLIPBOARD");
}
''', ("TestDesktopTypes",), f'<Compile Include="{escape(str(startup_source))}" />')
            project(root, "TestFormsRegistration", '''namespace LibreWinForms.ProGPU;
public readonly record struct ProGpuStartupOptions {
 public bool EnableNativeModalSessions => false;
 public static ProGpuStartupOptions ParseArguments(ReadOnlySpan<string> arguments) => default;
}
public static class ProGpuPlatform {
 public static void Register() => Console.WriteLine("FORMS");
 public static void Register(bool enableNativeModalSessions) => throw new Exception("Unexpected modal request");
}
''')
            bootstrap = f'<Compile Include="{escape(str(SDK / "targets/ProGPU.Wpf.Sdk.PortableBootstrap.cs"))}" />'
            for mode in ("managed", "native", "forms", "mixed"):
                with self.subTest(mode=mode):
                    constants = []
                    references = []
                    if mode != "forms":
                        constants.append("PROGPU_WPF_BOOTSTRAP_WPF")
                        references.extend(("TestStartupBridge", "TestDesktopTypes"))
                    if mode == "native":
                        constants.append("PROGPU_WPF_NATIVE_MIL")
                    if mode in ("forms", "mixed"):
                        constants.extend(("PROGPU_WPF_USE_LIBREWINFORMS", "PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS"))
                        references.append("TestFormsRegistration")
                    app = "App_" + mode
                    host = "Host_" + mode
                    project(root, app, '''public static class Api {
 public static int Value() => 42;
 public static void Main() => Console.WriteLine("ENTRY");
}''', references, bootstrap, ";".join(constants), True)
                    project(root, host, 'Console.WriteLine(Api.Value());', (app,), executable=True)
                    run(root, "build", f"{host}/{host}.csproj", "-c", "Release", "-v:q", "-nodeReuse:false")
                    app_output = root / app / "bin/Release/net10.0"
                    expected = []
                    if mode == "native":
                        expected.append("NATIVE MEDIA")
                    if mode in ("forms", "mixed"):
                        expected.append("FORMS")
                    if mode == "mixed":
                        expected.append("INTEROP")
                    if mode != "forms" and (os.name != "nt" or mode == "native"):
                        expected.extend(("WPF ACTIVATION", "WPF CLIPBOARD"))
                    expected.append("ENTRY")
                    self.assertEqual(expected, run(root, str(app_output / (app + ".dll"))).splitlines())
                    host_output = root / host / "bin/Release/net10.0"
                    for dependency in host_output.glob("Test*.dll"):
                        dependency.unlink()
                    self.assertEqual("42", run(root, str(host_output / (host + ".dll"))).strip())
                    if os.name == "nt" and mode == "managed":
                        (app_output / "TestDesktopTypes.dll").unlink()
                        self.assertEqual("ENTRY", run(root, str(app_output / (app + ".dll"))).strip())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=DOTNET)
    parser.add_argument("--sdk-root", type=Path, default=SDK)
    args, remaining = parser.parse_known_args()
    DOTNET, SDK = args.dotnet, args.sdk_root.resolve()
    if not DOTNET:
        parser.error("dotnet is required")
    unittest.main(argv=[__file__, *remaining])
