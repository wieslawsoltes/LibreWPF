#!/usr/bin/env python3
"""Invoke the real build function without launching a compiler or Windows app."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")


@unittest.skipUnless(PWSH, "PowerShell 7 is required for the actual invocation contract")
class WindowsManagedRuntimeBuildTests(unittest.TestCase):
    def invoke(self, serialized=False, rebuild=False, failure=False):
        script = r"""
param([string]$Path, [string]$Serialized, [string]$RebuildValue, [string]$Fail)
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$tree = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'The production PowerShell script does not parse.' }
$function = $tree.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-WpfProjectBuild'}, $true)
Invoke-Expression $function.Extent.Text
function Capture-Build {
    # Native command invocation expands array-valued arguments; mirror that
    # expansion while keeping this compiler substitute inside PowerShell.
    $script:Captured = @($args | ForEach-Object { $_ })
    $global:LASTEXITCODE = if ($Fail -eq 'True') { 23 } else { 0 }
}
$buildPowerShell = 'Capture-Build'
$buildCommand = 'owned-build.ps1'
$Configuration = 'Release'; $MSBuildEngine = 'vs'; $perlCommand = 'owned-perl'
$NativeToolsOnMachine = $false
$Rebuild = $RebuildValue -eq 'True'
$caught = $null
try { Invoke-WpfProjectBuild 'owned-test.csproj' 'x64' 'win-x64' 'owned-ijwhost.dll' -SerializeBuild:($Serialized -eq 'True') }
catch { $caught = $_.Exception.Message }
@{ arguments = @($script:Captured); caught = $caught } | ConvertTo-Json -Depth 4 -Compress
"""
        result = subprocess.run([PWSH, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                                 "& {" + script + "} '" + str(ROOT / "eng/progpu-wpf-windows-managed-runtime.ps1").replace("'", "''")
                                 + f"' '{serialized}' '{rebuild}' '{failure}'"],
                                text=True, capture_output=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def test_native_test_graph_serializes_both_msbuild_schedulers(self):
        result = self.invoke(serialized=True)
        self.assertIsNone(result["caught"])
        args = result["arguments"]
        self.assertEqual(args.count("/m:1"), 1)
        self.assertEqual(args.count("/p:BuildInParallel=false"), 1)
        for original in ("-restore", "-build", "/p:RuntimeIdentifier=win-x64", "/p:IjwHostSourcePath=owned-ijwhost.dll"):
            self.assertIn(original, args)

    def test_original_production_invocation_keeps_existing_scheduling(self):
        result = self.invoke()
        self.assertIsNone(result["caught"])
        self.assertNotIn("/m:1", result["arguments"])
        self.assertNotIn("/p:BuildInParallel=false", result["arguments"])

    def test_serialized_rebuild_and_failed_compiler_are_not_suppressed(self):
        result = self.invoke(serialized=True, rebuild=True, failure=True)
        self.assertIn("-rebuild", result["arguments"])
        self.assertNotIn("-build", result["arguments"])
        self.assertEqual(result["caught"], "Building owned-test.csproj for x64 failed.")

    def test_catalog_compile_scope_matches_actual_implementation_and_keeps_native_query(self):
        host = os.environ.get("PROGPU_WPF_SOURCE_EVALUATION_DOTNET") or shutil.which("dotnet")
        self.assertIsNotNone(host, "A configured .NET SDK is required for source item evaluation")
        projects = {
            "product": "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj",
            "tests": "src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationCore.Tests/PresentationCore.Tests.csproj",
        }
        portable_tests = {"PortableFontDataTests.cs", "PortableFontCollectionTests.cs", "PortableSystemFontDiscoveryTests.cs"}
        for platform in ("Unix", "Windows_NT"):
            items = {}
            for kind, project in projects.items():
                result = subprocess.run([host, "msbuild", str(ROOT / project), "-nologo", "-getItem:Compile",
                                         "-p:Configuration=Release", "-p:Platform=x64", f"-p:OS={platform}"],
                                        cwd=ROOT, text=True, capture_output=True, timeout=30)
                self.assertEqual(result.returncode, 0, result.stderr + result.stdout[-2000:])
                values = json.loads(result.stdout[result.stdout.index("{"):])["Items"]["Compile"]
                items[kind] = {v["Identity"].replace("\\", "/").rsplit("/", 1)[-1] for v in values}
            self.assertEqual("PortableTextInterface.cs" in items["product"], platform != "Windows_NT")
            self.assertEqual(items["tests"] & portable_tests, portable_tests if platform != "Windows_NT" else set())
            self.assertIn("PortableDesktopTransformQueryTests.cs", items["tests"])


if __name__ == "__main__":
    unittest.main()
