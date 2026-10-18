#!/usr/bin/env python3
"""Package extraction/provenance controls; fixtures are not native qualification."""

import importlib.util
import hashlib
import json
import os
from pathlib import Path
import subprocess
import struct
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("release_native", ROOT / "eng/progpu-stage-release-native-packages.py")
STAGING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(STAGING)
VERSION = "0.1.0-preview.67"
COMMIT = "a" * 40
NOTICE = "runtimes/osx-arm64/native/licenses/edit-word-icu/LICENSE"


class ReleaseNativePackagesTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="release-native-packages-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.destination = self.root / "staged"

    def package(self, sdk=False, commit=COMMIT, version=VERSION, entries=None, package_id=None):
        package_id = package_id or ("ProGPU.Native.Sdk" if sdk else "ProGPU.Backend.Native")
        path = self.root / (package_id + ".nupkg")
        if entries is None:
            entry = "sdk/libprogpu_native_text.a" if sdk else "libprogpu_native.dylib"
            entries = [("runtimes/osx-arm64/native/" + entry, b"layout fixture"), (NOTICE, b"original notice")]
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(package_id + ".nuspec", f'''<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata><id>{package_id}</id><version>{version}</version>
              <repository url="https://github.com/wieslawsoltes/ProGPU" commit="{commit}"/></metadata></package>''')
            for name, data in entries:
                archive.writestr(name, data)
        return path

    def stage(self, native, sdk):
        return STAGING.stage(native, sdk, self.destination, VERSION, COMMIT)

    def test_complete_pair_preserves_payload_and_shared_extensionless_notice(self):
        self.assertEqual(self.stage(self.package(), self.package(sdk=True)), 3)
        self.assertEqual((self.destination / NOTICE).read_bytes(), b"original notice")
        self.assertEqual((self.destination / "runtimes/osx-arm64/native/sdk/libprogpu_native_text.a").read_bytes(), b"layout fixture")

    def test_missing_sdk_never_publishes_partial_runtime(self):
        with self.assertRaises(FileNotFoundError):
            self.stage(self.package(), self.root / "missing.nupkg")
        self.assertFalse(self.destination.exists())

    def test_mixed_commit_or_version_never_publishes(self):
        for changed in ({"commit": "b" * 40}, {"version": "0.1.0-preview.68"}):
            with self.subTest(changed=changed), self.assertRaisesRegex(ValueError, "provenance"):
                self.stage(self.package(), self.package(sdk=True, **changed))
            self.assertFalse(self.destination.exists())

    def test_different_shared_notice_is_not_overwritten(self):
        with self.assertRaisesRegex(ValueError, "payload collision"):
            self.stage(self.package(), self.package(sdk=True, entries=[(NOTICE, b"changed notice")]))
        self.assertFalse(self.destination.exists())

    def test_unsafe_path_never_escapes_staging(self):
        for name in ("runtimes/../../escaped", "runtimes/../escaped", "runtimes/x\\escaped"):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "unsafe"):
                self.stage(self.package(), self.package(sdk=True, entries=[(name, b"bad")]))
            self.assertFalse(self.destination.exists())
            self.assertFalse((self.root / "escaped").exists())

    def test_existing_destination_remains_untouched(self):
        self.destination.mkdir()
        original = self.destination / "original"
        original.write_bytes(b"existing")
        with self.assertRaisesRegex(ValueError, "already exist"):
            self.stage(self.package(), self.package(sdk=True))
        self.assertEqual(original.read_bytes(), b"existing")

    def test_optional_dx12_keeps_original_payload_bytes_in_separate_staging_root(self):
        prefix = "buildTransitive/runtimes/"
        package = self.package(package_id="ProGPU.Backend.Dx12", entries=[
            (prefix, b""), (prefix + "win-arm64/native/dxcompiler.dll", b"compiler fixture"),
            (prefix + "win-arm64/native/LICENSE-LLVM.txt", b"original compiler notice")])
        self.assertEqual(2, STAGING.stage_dx12(package, self.destination, VERSION, COMMIT))
        self.assertEqual(b"compiler fixture", (self.destination / "win-arm64/native/dxcompiler.dll").read_bytes())
        self.assertEqual(b"original compiler notice", (self.destination / "win-arm64/native/LICENSE-LLVM.txt").read_bytes())

    def test_dx12_prefix_cannot_hide_wrong_provenance_or_path_escape(self):
        for changed, message in (({"commit": "b" * 40}, "provenance"),
                                 ({"version": "another"}, "provenance"),
                                 ({"entries": [("buildTransitive/runtimes/../escaped", b"invalid")]}, "unsafe")):
            with self.subTest(changed=changed), self.assertRaisesRegex(ValueError, message):
                package = self.package(package_id="ProGPU.Backend.Dx12", **changed)
                STAGING.stage_dx12(package, self.destination, VERSION, COMMIT)
            self.assertFalse(self.destination.exists())
            self.assertFalse((self.root / "escaped").exists())

    def xlib_entries(self):
        entries = []
        for rid, machine in (("linux-x64", 62), ("linux-arm64", 183)):
            header = bytearray(64)
            header[:7] = b"\x7fELF\x02\x01\x01"
            struct.pack_into("<HH", header, 16, 3, machine)
            payload = {"libwgpu_native.so": bytes(header), "LICENSE.MIT": b"notice",
                       "LICENSE.APACHE": b"notice", "wgpu-LICENSE.MIT": b"notice",
                       "wgpu-LICENSE.APACHE": b"notice", "webgpu-headers-LICENSE": b"header notice"}
            receipt = {"schemaVersion": 1, "rid": rid, "producerCommit": COMMIT,
                       "abiTestsPassed": True, "extensionExported": "progpuWgpuCreateInstanceXlibV1",
                       "inputs": {"extension": "progpuWgpuCreateInstanceXlibV1",
                                  "backendAbi": "PROGPU_NATIVE_BACKEND_ABI_WGPU_NATIVE_2024_05"},
                       "files": {name: hashlib.sha256(data).hexdigest() for name, data in payload.items()}}
            payload["wgpu-native-linux.json"] = json.dumps(receipt).encode()
            entries.extend((f"runtimes/{rid}/native/{name}", data) for name, data in payload.items())
        return entries

    def test_xlib_preserves_both_architectures_and_extensionless_notices(self):
        entries = self.xlib_entries()
        package = self.package(package_id="ProGPU.Backend", entries=entries)
        self.assertEqual(14, STAGING.stage_xlib(package, self.destination, VERSION, COMMIT))
        for name, data in entries:
            self.assertEqual(data, (self.destination / name.removeprefix("runtimes/")).read_bytes())

    def test_xlib_rejects_partial_or_unexpected_inventory_before_publication(self):
        entries = self.xlib_entries()
        for changed in (entries[:-1], entries + [("runtimes/linux-x64/native/extra.so", b"unexpected")],
                        [(name.replace("webgpu-headers-LICENSE", "webgpu-headers-LICENSE/webgpu-headers-LICENSE"), data)
                         for name, data in entries]):
            with self.subTest(changed=len(changed)), self.assertRaisesRegex(ValueError, "inventories"):
                package = self.package(package_id="ProGPU.Backend", entries=changed)
                STAGING.stage_xlib(package, self.destination, VERSION, COMMIT)
            self.assertFalse(self.destination.exists())

    def test_xlib_rejects_changed_library_or_notice_before_publication(self):
        for suffix in ("libwgpu_native.so", "webgpu-headers-LICENSE"):
            entries = [(name, data + b"changed" if name.endswith(suffix) else data)
                       for name, data in self.xlib_entries()]
            package = self.package(package_id="ProGPU.Backend", entries=entries)
            with self.assertRaisesRegex(ValueError, "byte mismatch"):
                STAGING.stage_xlib(package, self.destination, VERSION, COMMIT)
            self.assertFalse(self.destination.exists())

    def test_xlib_rejects_changed_producer_or_dependency_receipt(self):
        for key, value in (("producerCommit", "b" * 40), ("rid", "linux-x64"),
                           ("abiTestsPassed", False), ("extensionExported", "different"),
                           ("inputs", {"extension": "different"})):
            entries = []
            for name, data in self.xlib_entries():
                if name == "runtimes/linux-arm64/native/wgpu-native-linux.json":
                    receipt = json.loads(data);receipt[key] = value;data = json.dumps(receipt).encode()
                entries.append((name, data))
            with self.subTest(key=key), self.assertRaises(ValueError):
                package = self.package(package_id="ProGPU.Backend", entries=entries)
                STAGING.stage_xlib(package, self.destination, VERSION, COMMIT)
            self.assertFalse(self.destination.exists())


class CiNativePackageSelectionTests(unittest.TestCase):
    """Run the actual CI selection block with an argument-only extraction stub."""

    def select(self, versions=(VERSION,), status=0, dx12=False, xlib=False):
        with tempfile.TemporaryDirectory(prefix="ci-native-selection-") as temporary:
            root = Path(temporary)
            artifacts = root / "artifact"
            artifacts.mkdir()
            # Empty selection markers, never native payloads or valid packages.
            for version in versions:
                (artifacts / f"ProGPU.Backend.Native.{version}.nupkg").touch()
            receipt = root / "arguments"
            source = (ROOT / "eng/progpu-stage-ci-native-runtimes.sh").read_text()
            block = source[source.index("native_packages=()\n"):source.index("required_runtimes=(\n")]
            environment = dict(os.environ, artifact_contents=str(artifacts),
                               runtime_stage=str(root / "staged"), script_dir=str(ROOT / "eng"),
                               progpu_commit=COMMIT, RECEIPT=str(receipt), STAGE_STATUS=str(status))
            if dx12:
                environment["dx12_stage"] = str(root / "compiler")
            if xlib:
                environment["xlib_stage"] = str(root / "xlib")
            script = '''set -euo pipefail
python3() { printf '%s\\n' "$@" > "$RECEIPT"; return "$STAGE_STATUS"; }
''' + block
            result = subprocess.run(["bash", "-c", script], env=environment, capture_output=True, text=True)
            return result.returncode, receipt.read_text().splitlines() if receipt.exists() else [], root

    def test_exact_artifact_pair_uses_one_version_and_original_commit(self):
        status, arguments, root = self.select()
        self.assertEqual(status, 0)
        self.assertEqual(arguments, [str(ROOT / "eng/progpu-stage-release-native-packages.py"),
                         str(root / f"artifact/ProGPU.Backend.Native.{VERSION}.nupkg"),
                         str(root / f"artifact/ProGPU.Native.Sdk.{VERSION}.nupkg"),
                         str(root / "staged"), "--version", VERSION, "--commit", COMMIT])

    def test_missing_or_ambiguous_native_artifact_never_calls_extraction(self):
        for versions in ((), (VERSION, "0.1.0-preview.68")):
            with self.subTest(versions=versions):
                status, arguments, _ = self.select(versions)
                self.assertNotEqual(status, 0)
                self.assertEqual(arguments, [])

    def test_optional_dx12_uses_the_same_successful_producer_version(self):
        status, arguments, root = self.select(dx12=True)
        self.assertEqual(0, status)
        self.assertEqual(["--dx12-package", str(root / f"artifact/ProGPU.Backend.Dx12.{VERSION}.nupkg"),
                          "--dx12-destination", str(root / "compiler")], arguments[-4:])

    def test_failed_pair_extraction_stops_ci_staging(self):
        status, _, _ = self.select(status=23)
        self.assertEqual(status, 23)

    def test_optional_xlib_uses_the_same_successful_producer_version(self):
        status, arguments, root = self.select(dx12=True, xlib=True)
        self.assertEqual(0, status)
        self.assertEqual(["--xlib-package", str(root / f"artifact/ProGPU.Backend.{VERSION}.nupkg"),
                          "--xlib-destination", str(root / "xlib")], arguments[-4:])


if __name__ == "__main__":
    unittest.main()
