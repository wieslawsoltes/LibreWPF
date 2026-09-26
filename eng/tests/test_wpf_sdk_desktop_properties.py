#!/usr/bin/env python3
"""Execute the real SDK imports without restoring packages or requiring Windows."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


REPO_ROOT = Path(__file__).resolve().parents[2]
SDK_ROOT = REPO_ROOT / "packaging" / "ProGPU.Wpf.Sdk"
DOTNET = shutil.which("dotnet")


class DesktopPropertyTests(unittest.TestCase):
    def probe(self, wpf="", forms="", framework="net10.0", portable=True,
              markup=True, global_flags=False, transitive=False):
        with tempfile.TemporaryDirectory(prefix="librewpf-desktop-properties-") as directory:
            root = Path(directory)
            (root / "App.xaml").write_text(
                '<Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />',
                encoding="utf-8")
            (root / "View.xaml").write_text(
                '<Page xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />',
                encoding="utf-8")
            project_flags = "" if global_flags else (
                f"<UseWPF>{wpf}</UseWPF><UseWindowsForms>{forms}</UseWindowsForms>")
            transitive_item = ('<TransitiveFrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />'
                               if transitive else "")
            project = root / "Consumer.csproj"
            project.write_text(f"""<Project>
  <PropertyGroup>
    <ProGpuWpfUseCurrentRuntimeIdentifier>false</ProGpuWpfUseCurrentRuntimeIdentifier>
    <ProGpuWpfUsePortableFrameworkReferences>{str(portable).lower()}</ProGpuWpfUsePortableFrameworkReferences>
  </PropertyGroup>
  <Import Project="{escape(str(SDK_ROOT / 'Sdk' / 'Sdk.props'))}" />
  <PropertyGroup>
    <TargetFramework>{framework}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <ProGpuWpfUseWpfMarkup>{str(markup).lower()}</ProGpuWpfUseWpfMarkup>
    <ProGpuWpfEnablePortableBootstrap>false</ProGpuWpfEnablePortableBootstrap>
    {project_flags}
  </PropertyGroup>
  <ItemGroup>
    <IntentAtEvaluation Include="WPF" Condition="'$(UseWPF)' == 'true'" />
    <IntentAtEvaluation Include="Forms" Condition="'$(UseWindowsForms)' == 'true'" />
    {transitive_item}
  </ItemGroup>
  <Import Project="{escape(str(SDK_ROOT / 'Sdk' / 'Sdk.targets'))}" />
  <Target Name="Probe" DependsOnTargets="_CheckForInvalidWindowsDesktopTargetingConfiguration;_CheckForTransitiveWindowsDesktopDependencies">
    <ItemGroup>
      <IntentAtExecution Include="WPF" Condition="'$(UseWPF)' == 'true'" />
      <IntentAtExecution Include="Forms" Condition="'$(UseWindowsForms)' == 'true'" />
    </ItemGroup>
  </Target>
</Project>""", encoding="utf-8")
            command = [DOTNET, "msbuild", str(project), "-nologo", "-nodeReuse:false",
                       "-t:Probe",
                       "-getProperty:UseWPF,UseWindowsForms,PrepareResourcesDependsOn,ImportWindowsDesktopTargets",
                       "-getItem:FrameworkReference,ApplicationDefinition,Page,Using,IntentAtEvaluation,IntentAtExecution"]
            if global_flags:
                command.extend([f"-p:UseWPF={wpf}", f"-p:UseWindowsForms={forms}"])
            environment = os.environ.copy()
            for key in list(environment):
                if key.lower() in ("usewpf", "usewindowsforms", "importwindowsdesktoptargets"):
                    del environment[key]
            result = subprocess.run(command, cwd=root, env=environment, text=True,
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
            # Failed targets also emit -getItem JSON. Do not manufacture empty
            # item lists when testing native/foreign-framework rejection.
            start = result.stdout.find("{")
            self.assertGreaterEqual(start, 0, result.stdout)
            data = json.loads(result.stdout[start:])
            return result.returncode, result.stdout, data

    @staticmethod
    def identities(data, item):
        return [value["Identity"] for value in data["Items"][item]]

    def test_portable_project_and_global_flags_survive_both_msbuild_phases(self):
        for framework in ("net10.0", "net10.0-windows"):
            for wpf, forms in (("", ""), ("false", "false"), ("true", "false"),
                               ("false", "true"), ("true", "true")):
                for global_flags in (False, True):
                    with self.subTest(framework=framework, wpf=wpf, forms=forms, global_flags=global_flags):
                        code, output, data = self.probe(wpf, forms, framework, global_flags=global_flags)
                        self.assertEqual(0, code, output)
                        self.assertEqual(wpf, data["Properties"]["UseWPF"])
                        self.assertEqual(forms, data["Properties"]["UseWindowsForms"])
                        expected = [label for label, value in (("WPF", wpf), ("Forms", forms)) if value == "true"]
                        self.assertEqual(expected, self.identities(data, "IntentAtEvaluation"))
                        self.assertEqual(expected, self.identities(data, "IntentAtExecution"))
                        self.assertEqual(["Microsoft.NETCore.App"], self.identities(data, "FrameworkReference"))
                        self.assertEqual(["App.xaml"], self.identities(data, "ApplicationDefinition"))
                        self.assertEqual(["View.xaml"], self.identities(data, "Page"))
                        self.assertIn("MarkupCompilePass1;", data["Properties"]["PrepareResourcesDependsOn"])
                        self.assertIn("MarkupCompilePass2ForMainAssembly;", data["Properties"]["PrepareResourcesDependsOn"])
                        usings = self.identities(data, "Using")
                        self.assertEqual(int(forms == "true"), usings.count("System.Windows.Forms"))
                        self.assertEqual(int(forms == "true"), usings.count("System.Drawing"))

    def test_explicit_markup_opt_out_preserves_public_intent(self):
        code, output, data = self.probe("true", "true", markup=False)
        self.assertEqual(0, code, output)
        self.assertEqual(["WPF", "Forms"], self.identities(data, "IntentAtExecution"))
        self.assertEqual([], self.identities(data, "ApplicationDefinition"))
        self.assertEqual([], self.identities(data, "Page"))
        self.assertEqual(["Microsoft.NETCore.App"], self.identities(data, "FrameworkReference"))

    def test_native_opt_out_keeps_original_framework_selection(self):
        for wpf, forms, desktop in (("true", "false", "Microsoft.WindowsDesktop.App.WPF"),
                                    ("false", "true", "Microsoft.WindowsDesktop.App.WindowsForms"),
                                    ("true", "true", "Microsoft.WindowsDesktop.App")):
            with self.subTest(wpf=wpf, forms=forms):
                code, output, data = self.probe(wpf, forms, "net10.0-windows", portable=False)
                self.assertEqual(0, code, output)
                self.assertEqual(["Microsoft.NETCore.App", desktop], self.identities(data, "FrameworkReference"))
                self.assertEqual("true", data["Properties"]["ImportWindowsDesktopTargets"])

    def test_native_opt_out_still_rejects_nonwindows_tfm(self):
        code, output, data = self.probe("true", "false", portable=False)
        self.assertNotEqual(0, code, output)
        self.assertIn("NETSDK1136", output)
        self.assertIn("Microsoft.WindowsDesktop.App.WPF", self.identities(data, "FrameworkReference"))

    def test_portable_mode_does_not_disable_foreign_framework_validation(self):
        code, output, _ = self.probe("true", "true", transitive=True)
        self.assertNotEqual(0, code, output)
        self.assertIn("NETSDK1136", output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=DOTNET)
    parser.add_argument("--sdk-root", type=Path, default=SDK_ROOT)
    options, remaining = parser.parse_known_args()
    DOTNET = options.dotnet
    SDK_ROOT = options.sdk_root.resolve()
    if not DOTNET:
        parser.error("dotnet is required")
    unittest.main(argv=[__file__, *remaining])
