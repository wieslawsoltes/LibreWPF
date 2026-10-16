"""Original source preservation and explicit observer revision controls."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("modal_prepare", Path(__file__).parents[1] / "WindowsModalDesktop/prepare.py")
PREPARE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREPARE)


class SourceStagingTests(unittest.TestCase):
    def stage(self, root):
        source = root / "source"
        files = {PREPARE.OBSERVER: b"original observer\n", "samples/App.cs": b"original application\n"}
        for name, data in files.items():
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        manifest = dict(source={name: hashlib.sha256(data).hexdigest() for name, data in files.items()})
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


if __name__ == "__main__":
    unittest.main()
