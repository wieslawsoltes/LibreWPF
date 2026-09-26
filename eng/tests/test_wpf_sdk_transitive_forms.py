#!/usr/bin/env python3
"""Exercise real SDK filtering and optionally fresh project/NuGet consumers."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile


REPO = Path(__file__).resolve().parents[2]
FORMS = "Microsoft.WindowsDesktop.App.WindowsForms"
DESKTOP = "Microsoft.WindowsDesktop.App"
PACKAGE = "LibreWpf.FormsDependency.ContractProbe"
VERSION = "1.0.0-contract." + uuid.uuid4().hex
OPTIONS = None
WORK = None


def write(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


def run(args, cwd, log_name):
    environment = os.environ.copy()
    environment["NUGET_PACKAGES"] = str(OPTIONS.packages_root)
    result = subprocess.run(
        [OPTIONS.dotnet, *args], cwd=cwd, env=environment,
        text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300,
    )
    write(WORK / (log_name + ".log"), result.stdout)
    if result.returncode:
        raise AssertionError(f"{log_name} failed ({result.returncode}):\n{result.stdout}")
    return result.stdout


def imports(body):
    root = escape(str(OPTIONS.sdk_root), {'"': "&quot;"})
    return f'''<Project>
  <Import Project="{root}/Sdk/Sdk.props" />
  {body}
  <Import Project="{root}/Sdk/Sdk.targets" />
</Project>
'''


class FrameworkPolicyTests(unittest.TestCase):
    def test_only_owned_portable_forms_replaces_forms_framework(self):
        cases = (
            ("canonical", "true", "true", "Package", "true", False),
            ("legacy", "true", "true", "Package", "false", False),
            ("forms-opt-out", "true", "false", "Package", "true", True),
            ("local-artifacts", "true", "true", "LocalArtifacts", "true", True),
            ("local-opt-out", "true", "false", "LocalArtifacts", "true", True),
            ("native", "false", "true", "Package", "true", True),
            ("native-opt-out", "false", "false", "Package", "true", True),
        )
        for name, portable, forms, mode, libre, keep_forms in cases:
            with self.subTest(name=name):
                root = WORK / name
                body = f'''<PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>false</UseWPF><UseWindowsForms>true</UseWindowsForms>
    <ProGpuWpfUsePortableFrameworkReferences>{portable}</ProGpuWpfUsePortableFrameworkReferences>
    <ProGpuWpfUsePortableWinFormsCompat>{forms}</ProGpuWpfUsePortableWinFormsCompat>
    <ProGpuWpfUseLibreWinForms>{libre}</ProGpuWpfUseLibreWinForms>
    <ProGpuWpfReferenceMode>{mode}</ProGpuWpfReferenceMode>
  </PropertyGroup>
  <ItemGroup>
    <TransitiveFrameworkReference Include="{FORMS};{DESKTOP};{DESKTOP}.WPF;Microsoft.AspNetCore.App" />
  </ItemGroup>'''
                write(root / "Policy.csproj", imports(body))
                output = run([
                    "msbuild", "Policy.csproj", "-nologo",
                    "-t:_ProGpuWpfSdkRemoveTransitiveWindowsDesktopFrameworkReferences",
                    "-getItem:TransitiveFrameworkReference",
                ], root, name)
                items = json.loads(output)["Items"]["TransitiveFrameworkReference"]
                identities = [item["Identity"] for item in items]
                expected = ["Microsoft.AspNetCore.App"]
                if keep_forms:
                    expected.append(FORMS)
                if portable == "false":
                    expected.extend([DESKTOP, DESKTOP + ".WPF"])
                self.assertCountEqual(expected, identities)


class DependencyConsumerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = WORK / "consumers"
        cls.feed = cls.root / "feed"
        cls.feed.mkdir(parents=True)
        config = ET.parse(OPTIONS.nuget_config)
        sources = config.getroot().find("packageSources")
        if sources is None:
            raise AssertionError("NuGet.config must declare packageSources")
        ET.SubElement(sources, "add", {"key": "FormsDependencyContract", "value": str(cls.feed)})
        # The caller's sources can be relative to its configuration file.
        for source in sources.findall("add"):
            value = source.get("value", "")
            if "://" not in value and not Path(value).is_absolute():
                source.set("value", str((OPTIONS.nuget_config.parent / value).resolve()))
        cls.config = cls.root / "NuGet.config"
        config.write(cls.config, encoding="utf-8", xml_declaration=True)
        cls.library = cls.root / "FormsLibrary"
        write(cls.library / "FormsLibrary.csproj", f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>false</UseWPF><UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <PackageId>{PACKAGE}</PackageId><Version>{VERSION}</Version>
  </PropertyGroup>
</Project>
''')
        write(cls.library / "Probe.cs", '''namespace FormsLibrary;
public static class Probe
{
    public static System.Windows.Forms.Button CreateButton() => new() { Text = "Forms dependency" };
}
''')
        run([
            "pack", "FormsLibrary/FormsLibrary.csproj", "-c", "Release",
            "-o", str(cls.feed), "-p:RestoreConfigFile=" + str(cls.config),
        ], cls.root, "pack-forms-library")
        with zipfile.ZipFile(cls.feed / (PACKAGE + "." + VERSION + ".nupkg")) as package:
            nuspec = ET.fromstring(package.read(PACKAGE + ".nuspec"))
        references = [item.get("name") for item in nuspec.iter() if item.tag.endswith("}frameworkReference")]
        if references != [FORMS]:
            raise AssertionError(f"The real child package must carry only Forms: {references}")

    def test_project_and_package_dependencies_launch_without_windows_desktop(self):
        for route in ("project", "package"):
            for use_wpf in ("true", "false"):
                name = f"{route}-wpf-{use_wpf}"
                with self.subTest(name=name):
                    root = self.root / name
                    reference = (
                        '<ProjectReference Include="../FormsLibrary/FormsLibrary.csproj" />'
                        if route == "project" else
                        f'<PackageReference Include="{PACKAGE}" Version="{VERSION}" />'
                    )
                    body = f'''<PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>{use_wpf}</UseWPF><UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup><ItemGroup>{reference}</ItemGroup>'''
                    write(root / "Consumer.csproj", imports(body))
                    write(root / "Program.cs", '''using System;
using LibreWinForms.Platform;
if (!LibrePlatform.IsRegistered) throw new InvalidOperationException("Missing portable bootstrap");
using (var button = FormsLibrary.Probe.CreateButton())
{
    if (button.Text != "Forms dependency") throw new InvalidOperationException("Lost dependency behavior");
    if (button.GetType() != typeof(System.Windows.Forms.Button)) throw new InvalidOperationException("Wrong Forms type");
}
LibrePlatform.Current.Dispose();
Console.WriteLine("Forms-only dependency contract passed");
''')
                    properties = ["-p:" + value for value in OPTIONS.property]
                    run([
                        "build", "Consumer.csproj", "-c", "Release", "--force",
                        "-p:RestoreConfigFile=" + str(self.config), *properties,
                    ], root, name + "-build")
                    assets = json.loads((root / "obj/project.assets.json").read_text())
                    identity = PACKAGE + "/" + VERSION
                    self.assertTrue(identity in assets["libraries"], "Fresh restore must actually include the child")
                    self.assertEqual(route, assets["libraries"][identity]["type"])
                    if route == "project":
                        frameworks = assets["project"]["restore"]["frameworks"].values()
                        self.assertTrue(any(fw.get("projectReferences") for fw in frameworks))
                    else:
                        targets = assets["targets"].values()
                        self.assertTrue(any(FORMS in target.get(identity, {}).get("frameworkReferences", []) for target in targets))
                        package_name = f"{PACKAGE}.{VERSION}.nupkg"
                        cached = OPTIONS.packages_root / PACKAGE.lower() / VERSION / package_name.lower()
                        self.assertEqual((self.feed / package_name).read_bytes(), cached.read_bytes(), "Child package must be the exact fresh artifact")
                    outputs = list((root / "bin/Release").rglob("Consumer.runtimeconfig.json"))
                    self.assertEqual(1, len(outputs))
                    options = json.loads(outputs[0].read_text())["runtimeOptions"]
                    frameworks = options.get("frameworks", [options.get("framework", {})])
                    self.assertEqual(["Microsoft.NETCore.App"], [fw["name"] for fw in frameworks])
                    output = run([str(outputs[0].with_name("Consumer.dll"))], root, name + "-launch")
                    self.assertIn("Forms-only dependency contract passed", output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    parser.add_argument("--sdk-root", type=Path, default=REPO / "packaging/ProGPU.Wpf.Sdk")
    parser.add_argument("--output-dir", type=Path)
    parser.add_argument("--packages-root", type=Path)
    parser.add_argument("--nuget-config", type=Path)
    parser.add_argument("--package-smoke", action="store_true")
    parser.add_argument("--property", action="append", default=[])
    OPTIONS = parser.parse_args()
    if not OPTIONS.dotnet:
        parser.error("A dotnet host is required")
    if OPTIONS.package_smoke and not OPTIONS.nuget_config:
        parser.error("--package-smoke requires --nuget-config")
    # Resolve /tmp -> /private/tmp before creating any MSBuild graph; mixing
    # logical and physical project identities can silently lose project edges.
    WORK = (OPTIONS.output_dir or Path(tempfile.mkdtemp(prefix="librewpf-transitive-forms-"))).resolve()
    WORK.mkdir(parents=True, exist_ok=True)
    if any(WORK.iterdir()):
        parser.error("--output-dir must be empty; each run requires fresh restore graphs")
    OPTIONS.sdk_root = OPTIONS.sdk_root.resolve()
    OPTIONS.packages_root = (OPTIONS.packages_root or WORK / "packages").resolve()
    if OPTIONS.nuget_config:
        OPTIONS.nuget_config = OPTIONS.nuget_config.resolve()
    print(f"Transitive Forms evidence: {WORK}", flush=True)
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(FrameworkPolicyTests)
    if OPTIONS.package_smoke:
        suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(DependencyConsumerTests))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
