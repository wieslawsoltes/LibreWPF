"""Offline package provenance controls; no desktop, download or runtime qualification."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

SPEC = importlib.util.spec_from_file_location("modal_candidate", Path(__file__).parents[1] / "WindowsModalDesktop/candidate-packages.py")
CANDIDATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CANDIDATE)
HEAD = "1" * 40
VERSION = "0.1.0-preview.69"
ENGINE = "0.1.0-source.22222222"


class CandidatePackageTests(unittest.TestCase):
    def producer(self, root, mutate=None, sdk_version=VERSION):
        success = dict(status="completed", conclusion="success")
        jobs = CANDIDATE.PACKAGE.BUILD_JOBS | CANDIDATE.CI.BOOTSTRAP_JOBS
        build = dict(success, databaseId=1, name="LibreWPF Build", headSha=HEAD,
                     jobs=[dict(success, name=name, steps=[dict(success, name="Upload CI package bundle")])
                           for name in sorted(jobs)])
        docs = dict(success, databaseId=2, name="LibreWPF Docs", headSha=HEAD,
                    jobs=[dict(success, name="Verify release documentation")])
        archive = root / "qualified-packages.zip"
        names = ("LibreWPF.Sdk", "LibreWPF.Transport", "LibreWPF.ProGPU", "LibreWPF.Interop",
                 "ProGPU.Backend", "ProGPU.Backend.Native", "ProGPU.Backend.Dx12")
        with zipfile.ZipFile(archive, "w") as bundle:
            for name in names:
                version = sdk_version if name in names[:3] else ENGINE
                data = io.BytesIO()
                with zipfile.ZipFile(data, "w") as package:
                    package.writestr(name + ".nuspec", '<package><metadata><id>' + name + '</id><version>' + version +
                                     '</version><repository type="git" commit="' + HEAD + '"/></metadata></package>')
                bundle.writestr(name + ".nupkg", data.getvalue())
        artifact = dict(name="librewpf-ci-packages-" + HEAD, expired=False,
                        workflow_run=dict(id=1, head_sha=HEAD), size_in_bytes=archive.stat().st_size,
                        digest="sha256:" + hashlib.sha256(archive.read_bytes()).hexdigest())
        receipts = dict(identity=dict(head=HEAD, build=1, docs=2), build=build, docs=docs, artifact=artifact)
        if mutate:
            mutate(receipts)
        for name, value in receipts.items():
            (root / (name + ".json")).write_text(json.dumps(value))
        return receipts

    def test_exact_whole_producer_stages_actual_sdk_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.producer(root)
            value = CANDIDATE.stage_producer(root, root / "feed")
            self.assertEqual(dict(head=HEAD, build=1, docs=2), value["identity"])
            self.assertEqual((VERSION, ENGINE), (value["sdk"], value["engine"]))
            self.assertEqual(7, len(value["packages"]))
            self.assertEqual(HEAD, value["packages"]["librewpf.progpu"]["repository"]["commit"])

    def test_failed_incomplete_or_other_producer_cannot_stage(self):
        changes = [lambda r: r["build"].update(conclusion="failure"),
                   lambda r: r["docs"].update(status="in_progress"),
                   lambda r: r["docs"].update(headSha="3" * 40),
                   lambda r: r["build"]["jobs"].pop(),
                   lambda r: r["build"]["jobs"].append(copy.deepcopy(r["build"]["jobs"][0])),
                   lambda r: r["artifact"].update(expired=True),
                   lambda r: r["artifact"]["workflow_run"].update(id=3),
                   lambda r: r["identity"].update(build=True)]
        for number, change in enumerate(changes):
            with self.subTest(number=number), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.producer(root, change)
                with self.assertRaises(ValueError):
                    CANDIDATE.stage_producer(root, root / "feed")
                self.assertFalse((root / "feed").exists())

    def test_pending_upload_does_not_infer_success_from_archive(self):
        def pending(receipts):
            job = next(j for j in receipts["build"]["jobs"] if j["name"] == "SDK package and no-source-change smoke")
            job["steps"][0].update(status="pending", conclusion=None)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.producer(root, pending)
            with self.assertRaisesRegex(ValueError, "upload.*evidence"):
                CANDIDATE.load_producer(root)

    def test_every_bootstrap_job_required(self):
        for name in CANDIDATE.CI.BOOTSTRAP_JOBS:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.producer(root, lambda r: r["build"].update(jobs=[j for j in r["build"]["jobs"] if j["name"] != name]))
                with self.assertRaisesRegex(ValueError, "three current SDK bootstrap"):
                    CANDIDATE.load_producer(root)

    def test_changed_archive_size_or_hash_rejected(self):
        for mode in ("size", "hash"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.producer(root)
                archive = root / "qualified-packages.zip"
                data = archive.read_bytes()
                archive.write_bytes(data + b"changed" if mode == "size" else bytes([data[0] ^ 1]) + data[1:])
                with self.assertRaisesRegex(ValueError, "archive differs"):
                    CANDIDATE.load_producer(root)

    def test_new_sdk_version_is_never_relabeled_as_the_old_package(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.producer(root, sdk_version="0.1.0-preview.70")
            value = CANDIDATE.stage_producer(root, root / "feed")
            self.assertEqual("0.1.0-preview.70", value["sdk"])
            self.assertEqual("0.1.0-preview.70", value["packages"]["librewpf.sdk"]["version"])

    def test_private_restore_maps_all_product_families_to_qualified_feed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            config = root / "NuGet.Config"
            CANDIDATE.write_config(config, root / "feed", root / "packages")
            value = ET.parse(config).getroot()
            self.assertEqual(str(root / "packages"), value.find("config/add").get("value"))
            sources = value.find("packageSources")
            self.assertEqual("clear", sources[0].tag)
            self.assertEqual({"qualified", "NuGet"}, {s.get("key") for s in sources.findall("add")})
            mapped = value.find("packageSourceMapping/packageSource[@key='qualified']")
            self.assertEqual({"ProGPU.*", "LibreWPF.*", "LibreWinForms.*"}, {p.get("pattern") for p in mapped})

    def test_output_requires_actual_package_bytes_for_every_selected_assembly(self):
        assets = {
            "LibreWPF.Transport": ["runtimes/win-arm64/lib/net10.0/PresentationCore.dll", "lib/net10.0/PresentationFramework.dll",
                                   "lib/net10.0/WindowsBase.dll", "lib/net10.0/System.Private.Windows.Core.dll",
                                   "runtimes/win-arm64/native/PresentationNative_cor3.dll"],
            "LibreWPF.ProGPU": ["lib/net10.0/ProGPU.Wpf.dll"],
            "LibreWPF.Interop": ["lib/net10.0/ProGPU.Wpf.Interop.dll"],
            "ProGPU.Backend": ["lib/net10.0/ProGPU.Backend.dll"],
            "ProGPU.Backend.Native": ["runtimes/win-arm64/native/progpu_native.dll"],
        }
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); feed = root / "feed"; output = root / "output"
            feed.mkdir(); output.mkdir(); inventory = {}
            for name, members in assets.items():
                filename = name + ".nupkg"
                inventory[name] = dict(file=filename)
                with zipfile.ZipFile(feed / filename, "w") as archive:
                    for member in members:
                        data = (name + ":" + member).encode()
                        archive.writestr(member, data)
                        (output / Path(member).name).write_bytes(data)
            self.assertEqual(9, len(CANDIDATE.verify_output(output, feed, inventory, "win-arm64")))
            for path in output.iterdir():
                original = path.read_bytes()
                path.write_bytes(b"substituted old output")
                with self.subTest(asset=path.name), self.assertRaisesRegex(ValueError, "substituted"):
                    CANDIDATE.verify_output(output, feed, inventory, "win-arm64")
                path.write_bytes(original)

    def test_dxc_outputs_require_all_compiler_bytes_and_explicit_runtime_request(self):
        assets = {
            'LibreWPF.Transport': ['runtimes/win-arm64/lib/net10.0/PresentationCore.dll',
                'lib/net10.0/PresentationFramework.dll', 'lib/net10.0/WindowsBase.dll',
                'lib/net10.0/System.Private.Windows.Core.dll', 'runtimes/win-arm64/native/PresentationNative_cor3.dll'],
            'LibreWPF.ProGPU': ['lib/net10.0/ProGPU.Wpf.dll'],
            'LibreWPF.Interop': ['lib/net10.0/ProGPU.Wpf.Interop.dll'],
            'ProGPU.Backend': ['lib/net10.0/ProGPU.Backend.dll'],
            'ProGPU.Backend.Native': ['runtimes/win-arm64/native/progpu_native.dll'],
            'ProGPU.Backend.Dx12': ['buildTransitive/runtimes/win-arm64/native/' + name for name in
                ('wgpu_native.dll', 'dxcompiler.dll', 'dxil.dll', 'wgpu-native-build.json',
                 'dxc-compiler-package.json', 'progpu-dx12-runtime.json')],
        }
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); feed = root / 'feed'; output = root / 'output'
            feed.mkdir(); output.mkdir(); inventory = {}
            for package, members in assets.items():
                inventory[package] = dict(file=package + '.nupkg')
                with zipfile.ZipFile(feed / (package + '.nupkg'), 'w') as archive:
                    for member in members:
                        raw = (package + ':' + member).encode()
                        archive.writestr(member, raw)
                        (output / Path(member).name).write_bytes(raw)
            runtime = output / 'ProGPU.Wpf.ModalInteractionApp.runtimeconfig.json'
            config = dict(runtimeOptions=dict(configProperties={
                'LibreWPF.RequestedRendererMode': 'NativeMilWgpu', 'LibreWPF.RequestedDx12ShaderCompiler': 'Dxc'}))
            runtime.write_text(json.dumps(config))
            self.assertEqual(15, len(CANDIDATE.verify_output(output, feed, inventory, 'win-arm64', 'Dxc')))
            for member in assets['ProGPU.Backend.Dx12']:
                path = output / Path(member).name
                original = path.read_bytes(); path.write_bytes(b'substituted compiler')
                with self.subTest(member=member), self.assertRaisesRegex(ValueError, 'substituted'):
                    CANDIDATE.verify_output(output, feed, inventory, 'win-arm64', 'Dxc')
                path.write_bytes(original)
            for property_name in config['runtimeOptions']['configProperties']:
                changed = copy.deepcopy(config)
                changed['runtimeOptions']['configProperties'].pop(property_name)
                runtime.write_text(json.dumps(changed))
                with self.subTest(property_name=property_name), self.assertRaisesRegex(ValueError, 'did not request'):
                    CANDIDATE.verify_output(output, feed, inventory, 'win-arm64', 'Dxc')


if __name__ == "__main__":
    unittest.main()
