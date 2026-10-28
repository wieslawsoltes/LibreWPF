#!/usr/bin/env python3
"""Package extraction/provenance controls; fixtures are not native qualification."""

import importlib.util
import hashlib
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
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

    def test_xlib_inventory_uses_archive_names_on_each_host_path_flavor(self):
        # Keep real file/hash/ELF validation; vary only pathlib's relative-name
        # representation, including Windows behavior on a non-Windows test host.
        entries = self.xlib_entries()
        for name, data in entries:
            output = self.destination / name.removeprefix("runtimes/")
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(data)

        class InventoryPath:
            def __init__(self, path, flavor):
                self.path, self.flavor = path, flavor

            def __truediv__(self, child):
                return self.path / child

            def rglob(self, pattern):
                return (InventoryPath(path, self.flavor) for path in self.path.rglob(pattern))

            def is_file(self):
                return self.path.is_file()

            def relative_to(self, root):
                return self.flavor(*self.path.relative_to(root.path).parts)

        for flavor in (PurePosixPath, PureWindowsPath):
            with self.subTest(flavor=flavor.__name__):
                staged = InventoryPath(self.destination, flavor)
                STAGING.validate_xlib(staged, COMMIT)
                extra = self.destination / "linux-x64/native/unexpected.so"
                extra.write_bytes(b"unexpected")
                with self.assertRaisesRegex(ValueError, "inventories"):
                    STAGING.validate_xlib(staged, COMMIT)
                extra.unlink()
                notice = self.destination / "linux-arm64/native/webgpu-headers-LICENSE"
                original = notice.read_bytes()
                notice.write_bytes(b"changed")
                with self.assertRaisesRegex(ValueError, "byte mismatch"):
                    STAGING.validate_xlib(staged, COMMIT)
                notice.unlink()
                with self.assertRaisesRegex(ValueError, "inventories"):
                    STAGING.validate_xlib(staged, COMMIT)
                notice.write_bytes(original)

    def test_xlib_selects_only_linux_from_combined_backend_package(self):
        package = self.package(package_id="ProGPU.Backend", entries=self.xlib_entries() +
                               [("runtimes/win-x64/native/wgpu_native.dll", b"independent Windows payload")])
        self.assertEqual(14, STAGING.stage_xlib(package, self.destination, VERSION, COMMIT))
        self.assertFalse((self.destination / "win-x64").exists())

    def producer(self, capability=True):
        producer = self.root / "producer"
        producer.mkdir()
        script = producer / "eng/progpu-wgpu-windows-runtime.py"
        if capability:
            script.parent.mkdir()
            script.write_text("""# Fixture verifier: tests delegation, not native payload qualification.
import argparse
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--rid');p.add_argument('--verify',type=Path);a=p.parse_args()
names={'wgpu_native.dll','wgpu-native-build.json','wgpu-native-windows.json','LICENSE.MIT','LICENSE.APACHE','wgpu-LICENSE.MIT','wgpu-LICENSE.APACHE','webgpu-headers-LICENSE'}
assert {x.name for x in a.verify.iterdir()} == names
for name in names: assert (a.verify/name).read_bytes() == ('original/'+a.rid+'/'+name).encode()
""")
        (producer / "marker").write_text("owned source fixture")
        def git(*args):
            return subprocess.check_output(["git", "-C", str(producer), *args], text=True, stderr=subprocess.DEVNULL).strip()
        git("init");git("add", ".")
        git("-c", "user.name=Control", "-c", "user.email=control@example.invalid", "commit", "-m", "source fixture")
        return producer, git("rev-parse", "HEAD")

    def windows_entries(self):
        names = ("wgpu_native.dll", "wgpu-native-build.json", "wgpu-native-windows.json", "LICENSE.MIT",
                 "LICENSE.APACHE", "wgpu-LICENSE.MIT", "wgpu-LICENSE.APACHE", "webgpu-headers-LICENSE")
        return [(f"runtimes/{rid}/native/{name}", f"original/{rid}/{name}".encode())
                for rid in ("win-x64", "win-arm64") for name in names]

    def test_windows_delegates_both_exact_architectures_to_committed_producer(self):
        producer, commit = self.producer()
        entries = self.windows_entries()
        package = self.package(package_id="ProGPU.Backend", commit=commit, entries=entries + self.xlib_entries())
        self.assertEqual(16, STAGING.stage_windows(package, self.destination, VERSION, commit, producer))
        for name, data in entries:
            self.assertEqual(data, (self.destination / name.removeprefix("runtimes/")).read_bytes())
        self.assertFalse((self.destination / "linux-x64").exists())

    def test_windows_rejects_verifier_failure_before_publication(self):
        producer, commit = self.producer()
        for entries in (self.windows_entries()[:-1],
                        [(name, b"changed" if name.endswith("win-arm64/native/wgpu_native.dll") else data)
                         for name, data in self.windows_entries()]):
            package = self.package(package_id="ProGPU.Backend", commit=commit, entries=entries)
            with self.assertRaises(subprocess.CalledProcessError):
                STAGING.stage_windows(package, self.destination, VERSION, commit, producer)
            self.assertFalse(self.destination.exists())

    def test_windows_missing_or_modified_new_verifier_is_not_legacy_fallback(self):
        producer, commit = self.producer()
        package = self.package(package_id="ProGPU.Backend", commit=commit, entries=self.windows_entries())
        with self.assertRaisesRegex(ValueError, "source must match"):
            STAGING.stage_windows(package, self.destination, VERSION, COMMIT, producer)
        self.assertFalse(self.destination.exists())
        script = producer / "eng/progpu-wgpu-windows-runtime.py"
        original = script.read_bytes()
        for content in (None, original + b"# local changes\n"):
            if content is None: script.unlink()
            else: script.write_bytes(content)
            with self.assertRaisesRegex(ValueError, "local changes"):
                STAGING.stage_windows(package, self.destination, VERSION, commit, producer)
            self.assertFalse(self.destination.exists())

    def test_legacy_source_has_no_windows_stage_and_still_requires_exact_package(self):
        producer, commit = self.producer(capability=False)
        package = self.package(package_id="ProGPU.Backend", commit=commit, entries=self.xlib_entries())
        self.assertEqual(0, STAGING.stage_windows(package, self.destination, VERSION, commit, producer))
        self.assertFalse(self.destination.exists())
        package = self.package(package_id="ProGPU.Backend", commit=COMMIT, entries=self.xlib_entries())
        with self.assertRaisesRegex(ValueError, "provenance"):
            STAGING.stage_windows(package, self.destination, VERSION, commit, producer)
        package = self.package(package_id="ProGPU.Backend", commit=commit, entries=self.windows_entries())
        with self.assertRaisesRegex(ValueError, "no matching producer"):
            STAGING.stage_windows(package, self.destination, VERSION, commit, producer)
        package = self.package(package_id="ProGPU.Backend", commit=commit, entries=self.xlib_entries())
        self.destination.mkdir()
        (self.destination / "stale.dll").write_bytes(b"preserve caller artifact")
        with self.assertRaisesRegex(ValueError, "already exist"):
            STAGING.stage_windows(package, self.destination, VERSION, commit, producer)
        self.assertEqual((self.destination / "stale.dll").read_bytes(), b"preserve caller artifact")

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

    def select(self, versions=(VERSION,), status=0, dx12=False, xlib=False, windows=False):
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
            if windows:
                environment["windows_stage"] = str(root / "windows")
                environment["producer_root"] = str(root / "producer")
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

    def test_optional_windows_uses_same_backend_version_and_exact_source_root(self):
        status, arguments, root = self.select(windows=True)
        self.assertEqual(0, status)
        self.assertEqual(["--windows-package", str(root / f"artifact/ProGPU.Backend.{VERSION}.nupkg"),
                          "--windows-destination", str(root / "windows"), "--producer-root", str(root / "producer")], arguments[-6:])

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
