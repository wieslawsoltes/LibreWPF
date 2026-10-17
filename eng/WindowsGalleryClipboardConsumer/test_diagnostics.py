import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("diagnostic", Path(__file__).with_name("diagnose-windows.py"))
diagnostic = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diagnostic)


class DiagnosticBoundaryTests(unittest.TestCase):
    def test_extra_window_reads_preserve_original_result_and_bound(self):
        for unavailable in (False, True):
            with self.subTest(unavailable=unavailable), tempfile.TemporaryDirectory() as directory:
                value = {'hwnd': 7, 'pid': 10, 'title': 'original'}
                original = mock.Mock(return_value=value)
                observer = mock.Mock(return_value={'cloakFlags': 2})
                if unavailable: observer.side_effect = OSError('unavailable')
                wrapped = diagnostic.observed_window(original, Path(directory), observer)
                for _ in range(65): self.assertIs(value, wrapped(None, 7))
                self.assertEqual(65, original.call_count)
                self.assertEqual(64, observer.call_count)
                self.assertEqual(64, len(list(Path(directory).glob('*.json'))))
                self.assertEqual({'hwnd': 7, 'pid': 10, 'title': 'original'}, value)

    def test_extra_window_reads_do_not_hide_original_failure(self):
        original_error = RuntimeError('original identity failure')
        original = mock.Mock(side_effect=original_error)
        observer = mock.Mock()
        with self.assertRaises(RuntimeError) as seen:
            diagnostic.observed_window(original, Path('/unused'), observer)(None, 7)
        self.assertIs(original_error, seen.exception)
        observer.assert_not_called()

    def test_foreground_failure_is_observed_once_without_replacing_error(self):
        for unavailable in (False, True):
            with self.subTest(unavailable=unavailable), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / 'foreground.json'
                original_error = RuntimeError('owned window lost foreground after host activation')
                original = mock.Mock(side_effect=original_error)
                desktop = mock.Mock()
                desktop.u.GetForegroundWindow.return_value = 9
                desktop.window_observation.return_value = {'hwnd': 9, 'pid': 23, 'title': 'Search'}
                if unavailable:
                    desktop.window_observation.side_effect = OSError('unavailable')
                snapshot, record, activation = {'hwnd': 7}, {}, {'accepted': True}
                with self.assertRaises(RuntimeError) as seen:
                    diagnostic.observed_foreground(original, path)(desktop, snapshot, 12, 60, record, activation)
                self.assertIs(original_error, seen.exception)
                original.assert_called_once_with(desktop, snapshot, 12, 60, record, activation)
                self.assertEqual({}, record)
                self.assertTrue(all(call[0] in ('u.GetForegroundWindow', 'window_observation') for call in desktop.mock_calls))
                if not unavailable:
                    receipt = json.loads(path.read_text())
                    self.assertFalse(receipt['qualified'])
                    self.assertEqual(60, receipt['originalDeadline'])
                    self.assertEqual(23, receipt['foreground']['pid'])

    def test_successful_foreground_setup_has_no_diagnostic_calls(self):
        desktop = mock.Mock()
        expected = object()
        original = mock.Mock(return_value=expected)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'foreground.json'
            self.assertIs(expected, diagnostic.observed_foreground(original, path)(desktop, {}, 12, 60, {}))
            self.assertFalse(path.exists())
            self.assertEqual([], desktop.mock_calls)

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
