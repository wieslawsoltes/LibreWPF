"""Pure controls for the separate read-only foreground diagnostic."""
import importlib.util
from pathlib import Path
import sys
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('foreground_diagnostic',
    Path(__file__).parents[1] / 'WindowsModalDesktop/observe-foreground.py')
DRIVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DRIVER)


class ForegroundDiagnosticTests(unittest.TestCase):
    class Stop:
        def __init__(self, count): self.count = count
        def is_set(self): return self.count <= 0
        def wait(self, seconds):
            assert seconds == .05
            self.count -= 1

    def test_preserves_foreign_transitions_and_coalesces_only_equal_records(self):
        native = mock.Mock()
        rows = [{'hwnd': 1, 'pid': 10, 'after': 1},
                {'hwnd': 1, 'pid': 10, 'after': 1},
                {'hwnd': 2, 'pid': 20, 'after': 2},
                {'hwnd': 2, 'pid': 20, 'after': 3}]
        native.snapshot.side_effect = rows
        record = {'changes': []}
        DRIVER.observe(native, self.Stop(4), record, lambda: 0)
        self.assertEqual([rows[0], rows[2], rows[3]], [r['native'] for r in record['changes']])
        self.assertEqual([mock.call.snapshot()] * 4, native.mock_calls)
        self.assertNotIn('observerError', record)

    def test_change_budget_retains_original_records_and_stops(self):
        native = mock.Mock()
        native.snapshot.side_effect = [{'hwnd': i} for i in range(300)]
        record = {'changes': []}
        DRIVER.observe(native, self.Stop(300), record, lambda: 0)
        self.assertEqual(256, len(record['changes']))
        self.assertEqual({'hwnd': 255}, record['changes'][-1]['native'])
        self.assertIn('budget exceeded', record['observerError'])
        self.assertEqual(257, native.snapshot.call_count)

    def test_elapsed_bound_stops_before_native_read(self):
        native = mock.Mock()
        clock = mock.Mock(side_effect=[0, 150])
        record = {'changes': []}
        DRIVER.observe(native, self.Stop(2), record, clock)
        native.snapshot.assert_not_called()
        self.assertIn('deadline expired', record['observerError'])

    def test_native_failure_does_not_discard_preceding_evidence(self):
        native = mock.Mock()
        native.snapshot.side_effect = [{'hwnd': 3}, RuntimeError('native failure')]
        record = {'changes': []}
        DRIVER.observe(native, self.Stop(3), record, lambda: 0)
        self.assertEqual([{'seconds': 0, 'native': {'hwnd': 3}}], record['changes'])
        self.assertIn('native failure', record['observerError'])

    def test_original_driver_failure_and_arguments_survive_without_overlay(self):
        argv, path = sys.argv, sys.path[:]
        driver = Path('/original/eng/WindowsModalDesktop/run.py')
        output = Path('/case')
        def run(name, run_name):
            self.assertEqual(str(driver), name)
            self.assertEqual('__main__', run_name)
            self.assertEqual([str(driver), '--output', str(output)], sys.argv)
            self.assertEqual(str(driver.parent), sys.path[0])
            raise RuntimeError('original failure')
        with mock.patch.object(DRIVER.runpy, 'run_path', side_effect=run) as original:
            with self.assertRaisesRegex(RuntimeError, '^original failure$'):
                DRIVER.invoke_original(driver, output)
            original.assert_called_once()
        self.assertIs(argv, sys.argv)
        self.assertEqual(path, sys.path)

    def test_workflow_preserves_exact_producer_and_names_diagnostic_separately(self):
        workflow = (Path(__file__).parents[2] / '.github/workflows/progpu-wpf-modal-desktop.yml').read_text()
        self.assertIn('DIAGNOSTIC ONLY — unchanged original modal pair', workflow)
        self.assertIn('ref: ${{ inputs.producer_head }}\n          path: automation', workflow)
        self.assertIn('automation/eng/WindowsModalDesktop/candidate-packages.py', workflow)
        self.assertIn("PROGPU_WPF_TRACE_INPUT: '0'", workflow)
        self.assertIn('if: ${{ !inputs.foreground_diagnostic }}', workflow)
        self.assertIn('if: ${{ inputs.foreground_diagnostic }}', workflow)
        self.assertIn('foreground_diagnostic:', workflow)
        self.assertNotIn('ref: ${{ inputs.producer_head }}\n          path: diagnostics', workflow)
        self.assertIn('modal-foreground-diagnostic.json', workflow)


if __name__ == '__main__':
    unittest.main()
