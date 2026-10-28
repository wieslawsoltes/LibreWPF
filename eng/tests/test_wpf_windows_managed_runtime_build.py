#!/usr/bin/env python3
"""Invoke the real build function without launching a compiler or Windows app."""
import json
import base64
import os
from pathlib import Path
import shutil
import subprocess
import struct
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")


@unittest.skipUnless(PWSH, "PowerShell 7 is required for the actual invocation contract")
class WindowsManagedRuntimeBuildTests(unittest.TestCase):
    def invoke_closure(self, fail):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, copied, evidence = (root / name for name in ("source", "copied", "evidence"))
            for path in (source, copied, evidence): path.mkdir()
            def pe(machine):
                image = bytearray(70);image[:2] = b"MZ"
                struct.pack_into("<I", image, 60, 64);struct.pack_into("<IH", image, 64, 0x4550, machine)
                return image
            for name in ("core.dll", "first.dll", "second.dll", "third.dll"):
                data = b"managed source" if name == "core.dll" else pe(0x8664)
                (source / name).write_bytes(data);(copied / name).write_bytes(data)
            if fail:
                (copied / "core.dll").write_bytes(b"wrong copied source")
                for path in (source / "first.dll", copied / "first.dll"): path.write_bytes(pe(0xaa64))
                (copied / "second.dll").unlink()
                (source / "third.dll").unlink()
            script = r"""
$ErrorActionPreference = 'Stop'; $tokens = $null; $errors = $null
$tree = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot 'eng/progpu-wpf-windows-managed-runtime.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'The production PowerShell script does not parse.' }
foreach ($name in @('Test-X64DotNetHost', 'Assert-WpfSourceTestClosure')) {
    $function = $tree.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name}, $true)
    Invoke-Expression $function.Extent.Text
}
$expected = @('core.dll','first.dll','second.dll','third.dll') | ForEach-Object { Join-Path $root "source/$_" }
try { Assert-WpfSourceTestClosure $expected $expected[1..3] (Join-Path $root 'copied') (Join-Path $root 'evidence'); @{error=$null} | ConvertTo-Json }
catch { @{error=$_.Exception.Message} | ConvertTo-Json }
"""
            command = "$repoRoot = '" + str(ROOT).replace("'", "''") + "'; $root = '" + str(root).replace("'", "''") + "';\n" + script
            result = subprocess.run([PWSH, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
                                    cwd=ROOT, text=True, capture_output=True, timeout=15)
            self.assertEqual(result.returncode, 0, result.stderr)
            return json.loads(result.stdout), json.loads((evidence / "x64-closure.json").read_text())

    def test_closure_guard_records_every_mismatch_before_rejecting(self):
        result, receipt = self.invoke_closure(fail=True)
        self.assertIn("does not match", result["error"])
        self.assertFalse(receipt["passed"])
        self.assertEqual(len(receipt["files"]), 4)
        self.assertTrue(all(entry["mismatches"] for entry in receipt["files"]))
        self.assertIn("hashes differ", receipt["files"][0]["mismatches"][0])
        self.assertFalse(receipt["files"][1]["expectedX64"])
        self.assertIsNone(receipt["files"][2]["actualSha256"])
        self.assertIsNone(receipt["files"][3]["expectedSha256"])

    def test_closure_guard_requires_exact_hashes_and_native_architecture(self):
        result, receipt = self.invoke_closure(fail=False)
        self.assertIsNone(result["error"])
        self.assertTrue(receipt["passed"])
        self.assertEqual(len(receipt["files"]), 4)
        for entry in receipt["files"]:
            self.assertEqual(entry["expectedSha256"], entry["actualSha256"])
            self.assertFalse(entry["mismatches"])

    def invoke_core_evaluation(self, synthetic=None):
        host = os.environ.get("PROGPU_WPF_SOURCE_EVALUATION_DOTNET") or shutil.which("dotnet")
        self.assertIsNotNone(host, "A configured .NET SDK is required for source item evaluation")
        setup = "$env:DOTNET_HOST_PATH = '" + host.replace("'", "''") + "'"
        if synthetic is not None:
            encoded = base64.b64encode(json.dumps(synthetic).encode()).decode()
            setup = "function Read-TestMetadata { $global:LASTEXITCODE = 0; [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')) }; $env:DOTNET_HOST_PATH = 'Read-TestMetadata'"
        script = r"""
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$tree = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $repoRoot 'eng/progpu-wpf-windows-managed-runtime.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'The production PowerShell script does not parse.' }
$function = $tree.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-WpfSourceTestCoreBuild'}, $true)
Invoke-Expression $function.Extent.Text
$Configuration = 'Release'
try { @{ result = (Get-WpfSourceTestCoreBuild); error = $null } | ConvertTo-Json -Depth 8 -Compress }
catch { @{ result = $null; error = $_.Exception.Message } | ConvertTo-Json -Compress }
"""
        command = "$repoRoot = '" + str(ROOT).replace("'", "''") + "'; " + setup + "\n" + script
        result = subprocess.run([PWSH, "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
                                cwd=ROOT, text=True, capture_output=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def test_actual_reference_output_is_distinct_from_explicit_rid_package_output(self):
        actual = self.invoke_core_evaluation()
        self.assertIsNone(actual["error"])
        properties = actual["result"]["Properties"]
        self.assertEqual(properties["PlatformTarget"], "x64")
        self.assertEqual(properties["RuntimeIdentifier"], "")
        self.assertEqual(Path(properties["TargetPath"]), ROOT / "artifacts/bin/PresentationCore/x64/Release/net10.0/PresentationCore.dll")
        host = os.environ.get("PROGPU_WPF_SOURCE_EVALUATION_DOTNET") or shutil.which("dotnet")
        explicit = subprocess.run([host, "msbuild", "src/Microsoft.DotNet.Wpf/src/PresentationCore/PresentationCore.csproj",
                                   "-nologo", "-getProperty:TargetPath", "-p:Configuration=Release", "-p:Platform=x64", "-p:RuntimeIdentifier=win-x64"],
                                  cwd=ROOT, text=True, capture_output=True, timeout=30)
        self.assertEqual(explicit.returncode, 0, explicit.stderr)
        self.assertEqual(Path(explicit.stdout.strip()), Path(properties["TargetPath"]).parent / "win-x64/PresentationCore.dll")

    def test_reference_output_rejects_changed_platform_or_rid_contract(self):
        baseline = {"Properties": {"TargetPath": "owned/PresentationCore.dll", "PlatformTarget": "x64", "RuntimeIdentifier": "", "TargetFramework": "net10.0"},
                    "Items": {"_ThisProjectBuildMetadata": [{"IsRidAgnostic": "true"}]}}
        for key, value in (("PlatformTarget", "arm64"), ("RuntimeIdentifier", "win-x64"), ("TargetPath", "")):
            candidate = json.loads(json.dumps(baseline));candidate["Properties"][key] = value
            self.assertIn("RID-agnostic x64", self.invoke_core_evaluation(candidate)["error"])
        baseline["Items"]["_ThisProjectBuildMetadata"][0]["IsRidAgnostic"] = "false"
        self.assertIn("RID-agnostic x64", self.invoke_core_evaluation(baseline)["error"])

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
