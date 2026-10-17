"""Original source preservation and explicit observer revision controls."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("modal_prepare", Path(__file__).parents[1] / "WindowsModalDesktop/prepare.py")
PREPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREPARE)
RUN_SPEC = importlib.util.spec_from_file_location('modal_run', Path(__file__).parents[1] / 'WindowsModalDesktop/run.py')
RUN = importlib.util.module_from_spec(RUN_SPEC)
sys.path.insert(0, str(Path(__file__).parents[1] / 'WindowsModalDesktop'))
try:
    RUN_SPEC.loader.exec_module(RUN)
finally:
    sys.path.pop(0)


class SourceStagingTests(unittest.TestCase):
    def stage(self, root):
        source = root / "source"
        files = {PREPARE.OBSERVER: b"original observer\n", "samples/App.cs": b"original application\n",
                 PREPARE.PROJECT: b'<Project Sdk="LibreWPF.Sdk/0.1.0-preview.69">\n  <PropertyGroup><UseWPF>true</UseWPF>\n    <ProGpuWpfNativeMilHitTesting>true</ProGpuWpfNativeMilHitTesting>\n  </PropertyGroup>\n</Project>\n'}
        for name, data in files.items():
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        manifest = dict(version="0.1.0-preview.69", source={name: hashlib.sha256(data).hexdigest() for name, data in files.items()})
        observer = root / "observer-v2.cs"
        observer.write_bytes(b"corrected observer\n")
        output = root / "output"
        selected = PREPARE.stage_sources(source, output, manifest, observer)
        return output, manifest, selected

    def test_both_original_and_selected_observers_remain_distinct_and_verified(self):
        with tempfile.TemporaryDirectory() as temporary:
            output, manifest, selected = self.stage(Path(temporary))
            PREPARE.verify_staged_sources(output, manifest, selected)
            self.assertNotEqual(manifest["source"][PREPARE.OBSERVER], selected[PREPARE.OBSERVER])
            self.assertEqual(b"original observer\n", (output / "original-source" / PREPARE.OBSERVER).read_bytes())
            self.assertEqual(b"corrected observer\n", (output / "consumer" / PREPARE.OBSERVER).read_bytes())

    def test_rehashed_application_change_cannot_be_disguised_as_observer_revision(self):
        with tempfile.TemporaryDirectory() as temporary:
            output, manifest, selected = self.stage(Path(temporary))
            path = output / "consumer/samples/App.cs"
            path.write_bytes(b"altered application")
            selected["samples/App.cs"] = PREPARE.sha(path)
            with self.assertRaisesRegex(RuntimeError, "Only the explicit read-only observer"):
                PREPARE.verify_staged_sources(output, manifest, selected)

    def test_mutation_of_either_observer_or_inventory_is_rejected(self):
        for changed in ("original-source", "consumer", "inventory"):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as temporary:
                output, manifest, selected = self.stage(Path(temporary))
                if changed == "inventory":
                    selected.pop("samples/App.cs")
                else:
                    (output / changed / PREPARE.OBSERVER).write_bytes(b"changed")
                with self.assertRaises(RuntimeError):
                    PREPARE.verify_staged_sources(output, manifest, selected)

    def test_candidate_changes_only_the_exact_sdk_selector_and_preserves_original(self):
        with tempfile.TemporaryDirectory() as temporary:
            output, manifest, selected = self.stage(Path(temporary))
            original = (output / "original-source" / PREPARE.PROJECT).read_bytes()
            receipt = PREPARE.stage_sdk_selection(output, manifest, selected, "0.1.0-preview.70")
            PREPARE.verify_staged_sources(output, manifest, selected, "0.1.0-preview.70")
            self.assertEqual(original, (output / "original-source" / PREPARE.PROJECT).read_bytes())
            self.assertEqual(original.replace(b"0.1.0-preview.69", b"0.1.0-preview.70"),
                             (output / "consumer" / PREPARE.PROJECT).read_bytes())
            self.assertEqual(("0.1.0-preview.69", "0.1.0-preview.70"),
                             (receipt["originalVersion"], receipt["selectedVersion"]))
            with self.assertRaises(RuntimeError):
                PREPARE.verify_staged_sources(output, manifest, selected)

    def test_rehashed_project_property_change_cannot_hide_in_sdk_selection(self):
        with tempfile.TemporaryDirectory() as temporary:
            output, manifest, selected = self.stage(Path(temporary))
            PREPARE.stage_sdk_selection(output, manifest, selected, "0.1.0-preview.70")
            project = output / "consumer" / PREPARE.PROJECT
            project.write_bytes(project.read_bytes().replace(b"true", b"false"))
            selected[PREPARE.PROJECT] = PREPARE.sha(project)
            with self.assertRaisesRegex(RuntimeError, "exact selected SDK reference"):
                PREPARE.verify_staged_sources(output, manifest, selected, "0.1.0-preview.70")

    def test_sdk_selection_rejects_property_expressions_and_malformed_versions(self):
        for version in ('$(Version)', '0.1.0-preview.70\" Other=\"true', '0.1.0/../../old', ''):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as temporary:
                output, manifest, selected = self.stage(Path(temporary))
                with self.assertRaisesRegex(RuntimeError, "literal package version"):
                    PREPARE.stage_sdk_selection(output, manifest, selected, version)

    def test_explicit_dxc_preserves_original_bytes_and_every_other_project_property(self):
        with tempfile.TemporaryDirectory() as temporary:
            output, manifest, selected = self.stage(Path(temporary))
            original = (output / 'original-source' / PREPARE.PROJECT).read_bytes()
            receipt = PREPARE.stage_sdk_selection(output, manifest, selected, '0.1.0-preview.70', 'Dxc')
            PREPARE.verify_staged_sources(output, manifest, selected, '0.1.0-preview.70', 'Dxc')
            self.assertEqual('Dxc', receipt['compiler'])
            self.assertEqual(original, (output / 'original-source' / PREPARE.PROJECT).read_bytes())
            project = output / 'consumer' / PREPARE.PROJECT
            self.assertEqual(original.replace(b'0.1.0-preview.69', b'0.1.0-preview.70'),
                             project.read_bytes().replace(PREPARE.DXC_PROPERTY, b''))
            with self.assertRaises(RuntimeError):
                PREPARE.verify_staged_sources(output, manifest, selected, '0.1.0-preview.70')
            project.write_bytes(project.read_bytes().replace(b'>true<', b'>false<'))
            selected[PREPARE.PROJECT] = PREPARE.sha(project)
            with self.assertRaises(RuntimeError):
                PREPARE.verify_staged_sources(output, manifest, selected, '0.1.0-preview.70', 'Dxc')

    def test_compiler_selection_rejects_unknown_and_public_baseline_changes(self):
        with tempfile.TemporaryDirectory() as temporary:
            output, manifest, selected = self.stage(Path(temporary))
            for value in ('Fxc', 'Automatic', 'dxc', '$(Compiler)'):
                with self.subTest(value=value), self.assertRaises(RuntimeError):
                    PREPARE.selected_project_bytes(output, manifest, '0.1.0-preview.70', value)
            with self.assertRaises(RuntimeError):
                PREPARE.verify_staged_sources(output, manifest, selected, compiler='Dxc')

    def test_sample_uses_exact_explicit_windows_only_compiler_property(self):
        project = Path(__file__).parents[2] / PREPARE.PROJECT
        self.assertEqual(1, project.read_bytes().count(PREPARE.DXC_PROPERTY))

    def test_all_seven_captures_require_actual_compiler_and_packaged_path(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve(); payload = dict(directory=str(root))
            library = root / 'dxcompiler.dll'; library.write_bytes(b'original package fixture')
            evidence = root / 'evidence'; (evidence / 'portable').mkdir(parents=True)
            capture = dict(source=dict(windows=[dict(compiler=dict(selected='Dxc', library=str(library)))]))
            phases = ('01-owner', '02-message', '03-dialog', '04-popup', '05-context', '06-combo', '07-tooltip')
            for phase in phases:
                (evidence / 'portable' / (phase + '.json')).write_text(json.dumps(capture))
            RUN.verify_compiler_captures(evidence, payload, 'Dxc')
            other = root / 'foreign.dll'; other.write_bytes(b'other')
            for phase in phases:
                path = evidence / 'portable' / (phase + '.json')
                original = path.read_bytes()
                for compiler in (None, dict(selected='Fxc', library=str(library)),
                                 dict(selected='Dxc', library=str(other))):
                    path.write_text(json.dumps(dict(source=dict(windows=[dict(compiler=compiler)]))))
                    with self.subTest(phase=phase, compiler=compiler), self.assertRaisesRegex(RuntimeError, 'Actual modal window'):
                        RUN.verify_compiler_captures(evidence, payload, 'Dxc')
                path.write_bytes(original)
            # The immutable automatic baseline has no new compiler requirement.
            RUN.verify_compiler_captures(root / 'absent', payload, None)


if __name__ == "__main__":
    unittest.main()
