#!/usr/bin/env python3
"""Evaluate WPF's producer import boundary; fixtures are not native qualification."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
DOTNET = "dotnet"


class WindowsSourceAssetsTests(unittest.TestCase):
    def evaluate(self, *, rid="win-x64", output="Exe", test=False, capability=True,
                 staged=True, explicit=False, missing_receipt=False, suffix="", runtime_alias=False):
        with tempfile.TemporaryDirectory(prefix="wpf-windows-source-assets-") as temporary:
            root = Path(temporary)
            source = root / "source"
            targets = source / "src/ProGPU.Backend/buildTransitive"
            targets.mkdir(parents=True)
            # Own tiny target fixtures test delegation exactly once, not a copy of
            # another repository's runtime-selection implementation.
            producer = ET.Element("Project")
            if capability:
                ET.SubElement(producer, "Import", Project="ProGPU.Backend.Windows.targets")
                windows = ET.Element("Project")
                group = ET.SubElement(windows, "ItemGroup")
                for selected in ("win-x64", "win-arm64"):
                    ET.SubElement(group, "Content", Include=f"$(ProGpuWgpuWindowsSourceRuntimeRoot)/{selected}/native/*",
                                  Condition=f"'$(RuntimeIdentifier)' == '' Or '$(RuntimeIdentifier)' == '{selected}'",
                                  Link=f"runtimes/{selected}/native/%(Filename)%(Extension)",
                                  CopyToOutputDirectory="PreserveNewest", CopyToPublishDirectory="PreserveNewest")
                ET.ElementTree(windows).write(targets / "ProGPU.Backend.Windows.targets", encoding="unicode")
            ET.ElementTree(producer).write(targets / "ProGPU.Backend.targets", encoding="unicode")
            payload = root / "explicit" if explicit else source / "artifacts/progpu-wgpu-windows/package"
            if staged:
                for selected in ("win-x64", "win-arm64"):
                    folder = payload / selected / "native"
                    folder.mkdir(parents=True)
                    (folder / "wgpu_native.dll").write_bytes(b"asset selection fixture; never execute")
                    if not missing_receipt: (folder / "wgpu-native-windows.json").write_text("fixture receipt")
            document = ET.Element("Project")
            group = ET.SubElement(document, "PropertyGroup")
            for name, value in {"OutputType": output, "IsTestProject": str(test).lower(), "RuntimeIdentifier": rid,
                                "ProGpuSourceRoot": str(source) + suffix}.items():
                ET.SubElement(group, name).text = value
            if explicit: ET.SubElement(group, "ProGpuWgpuWindowsRuntimeRoot" if runtime_alias else "ProGpuWgpuWindowsSourceRuntimeRoot").text = str(payload)
            for _ in range(2):
                ET.SubElement(document, "Import", Project=str(ROOT / "eng/LibreWPF.XlibRuntime.targets"))
            project = root / "control.proj"
            ET.ElementTree(document).write(project, encoding="unicode")
            result = subprocess.run([DOTNET, "msbuild", str(project), "-nologo", "-verbosity:quiet",
                                     "-target:ValidateLibreWpfOwnedWindowsSourceRuntime", "-getItem:Content"],
                                    capture_output=True, text=True, timeout=30, cwd=root)
            if result.returncode:
                return result.returncode, result.stdout + result.stderr
            items = json.loads(result.stdout)["Items"]["Content"]
            return 0, [(item["Link"], Path(item["FullPath"]).read_bytes()) for item in items]

    def test_exact_selected_architecture_imports_once_with_global_root_shapes(self):
        for rid in ("win-x64", "win-arm64"):
            for suffix in ("", "/"):
                for explicit in (False, True):
                    with self.subTest(rid=rid, suffix=suffix, explicit=explicit):
                        code, items = self.evaluate(rid=rid, suffix=suffix, explicit=explicit)
                        self.assertEqual(code, 0, items)
                        self.assertEqual({name for name, _ in items}, {f"runtimes/{rid}/native/wgpu_native.dll", f"runtimes/{rid}/native/wgpu-native-windows.json"})
                        self.assertEqual(len(items), 2)

    def test_shared_runtime_root_alias_preserves_explicit_selection(self):
        code, items = self.evaluate(explicit=True, runtime_alias=True)
        self.assertEqual(code, 0, items)
        self.assertEqual(len(items), 2)

    def test_new_producer_missing_payload_or_receipt_fails_before_build(self):
        for options in ({"staged": False}, {"missing_receipt": True}):
            with self.subTest(options=options):
                code, output = self.evaluate(**options)
                self.assertNotEqual(code, 0, output)
                self.assertTrue("staged Windows runtime" in output or "producer receipt" in output, output)

    def test_old_qualified_producer_and_plain_libraries_remain_unchanged(self):
        for options in ({"capability": False, "staged": False}, {"output": "Library", "staged": False}):
            with self.subTest(options=options):
                code, items = self.evaluate(**options)
                self.assertEqual(code, 0, items)
                self.assertEqual(items, [])

    def test_test_library_is_an_executable_boundary(self):
        code, items = self.evaluate(output="Library", test=True)
        self.assertEqual(code, 0, items)
        self.assertEqual(len(items), 2)

    def test_raw_native_artifact_keeps_one_owner_and_private_assets(self):
        names = ("wgpu_native.dll", "wgpu-native-build.json", "wgpu-native-windows.json", "LICENSE.MIT",
                 "LICENSE.APACHE", "wgpu-LICENSE.MIT", "wgpu-LICENSE.APACHE", "webgpu-headers-LICENSE")
        for capability in (False, True):
            for rid in ("win-x64", "win-arm64"):
                with self.subTest(capability=capability, rid=rid), tempfile.TemporaryDirectory() as temporary:
                    source = Path(temporary)
                    marker = source / "src/ProGPU.Backend/buildTransitive/ProGPU.Backend.Windows.targets"
                    if capability:
                        marker.parent.mkdir(parents=True)
                        marker.write_text("<Project/>")
                    native = source / f"artifacts/progpu-native/package/runtimes/{rid}/native"
                    native.mkdir(parents=True)
                    for name in names: (native / name).write_bytes(b"raw source fixture")
                    (native / "progpu_native.dll").write_bytes(b"private renderer fixture")
                    (native / "private").mkdir()
                    (native / "private/wgpu_native.dll").write_bytes(b"unrelated private fixture")
                    (native / "sdk").mkdir()
                    (native / "sdk/example.lib").write_bytes(b"SDK fixture")
                    document = ET.Element("Project")
                    group = ET.SubElement(document, "PropertyGroup")
                    for name, value in {"OutputType": "Exe", "RuntimeIdentifier": rid,
                                        "LibreWpfUseSourceNativeRuntime": "true", "ProGpuSourceRoot": str(source)}.items():
                        ET.SubElement(group, name).text = value
                    for _ in range(2): ET.SubElement(document, "Import", Project=str(ROOT / "eng/LibreWPF.NativeRuntime.targets"))
                    project = source / "control.proj"
                    ET.ElementTree(document).write(project, encoding="unicode")
                    result = subprocess.run([DOTNET, "msbuild", str(project), "-nologo", "-verbosity:quiet", "-getItem:Content"],
                                            capture_output=True, text=True, timeout=30, cwd=source)
                    self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                    items = json.loads(result.stdout)["Items"]["Content"]
                    actual = [item["Link"].replace("\\", "/") for item in items]
                    expected = {"progpu_native.dll", "private/wgpu_native.dll"} | (set() if capability else set(names))
                    self.assertEqual(set(actual), expected)
                    self.assertEqual(len(actual), len(expected))

    def test_nonwindows_rid_does_not_select_windows_assets(self):
        code, items = self.evaluate(rid="linux-x64")
        self.assertEqual(code, 0, items)
        self.assertEqual(items, [])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--dotnet", default="dotnet")
    args, remaining = parser.parse_known_args()
    DOTNET = args.dotnet
    unittest.main(argv=[__file__, *remaining])
