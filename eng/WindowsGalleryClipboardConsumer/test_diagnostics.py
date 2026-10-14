import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("diagnostic", Path(__file__).with_name("diagnose-windows.py"))
diagnostic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diagnostic)


class DiagnosticBoundaryTests(unittest.TestCase):
    def test_uniform_failure_keeps_original_exception_and_negative_evidence(self):
        error = RuntimeError("uniform image-region capture is not accepted")
        calls = []
        snapshot = {"pid": 12, "frame": 3}
        def original(desktop, source, pid, path):
            calls.append((desktop, source, pid, path))
            raise error
        def observe(desktop, source, pid, path):
            path.write_bytes(b"original captured failure bytes")
            return {"distinctRgbBytes": 1}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "pasted.bmp"
            with self.assertRaises(RuntimeError) as caught:
                diagnostic.observed_capture(original, observe)(None, snapshot, 12, path)
            self.assertIs(caught.exception, error)
            self.assertEqual(calls, [(None, snapshot, 12, path)])
            self.assertEqual(snapshot, {"pid": 12, "frame": 3})
            record = json.loads(path.with_name("pasted.diagnostic.json").read_text())
            self.assertFalse(record["qualified"])
            self.assertEqual(record["originalDeadlineSeconds"], 60)
            self.assertEqual(record["clientCapture"]["distinctRgbBytes"], 1)
            self.assertFalse(path.exists())

    def test_diagnostic_failure_never_substitutes_for_original_capture(self):
        expected = object()
        def original(*args): return expected
        def observe(*args): raise RuntimeError("observer unavailable")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "pasted.bmp"
            self.assertIs(diagnostic.observed_capture(original, observe)(None, {}, 12, path), expected)
            record = json.loads(path.with_name("pasted.diagnostic.json").read_text())
            self.assertEqual(record["diagnosticError"], "observer unavailable")
            self.assertFalse(record["qualified"])


if __name__ == "__main__":
    unittest.main()
