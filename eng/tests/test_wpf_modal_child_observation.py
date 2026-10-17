import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location('pair_observer', Path(__file__).resolve().parents[1] / 'WindowsModalDesktop/diagnose-pair.py')
PAIR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PAIR)


class ChildObservationTests(unittest.TestCase):
    def test_keeps_the_exact_process_command_arguments_and_return_object(self):
        child = Mock(pid=42)
        command = ['owned.exe', '--original-argument']
        with patch.object(subprocess, 'Popen', return_value=child) as original:
            rows = []
            with PAIR.observe_children({'owned.exe'}, rows):
                actual = subprocess.Popen(command, cwd='original', stdout=123)
            self.assertIs(actual, child)
            self.assertIs(subprocess.Popen, original)
            original.assert_called_once_with(command, cwd='original', stdout=123)
            self.assertIs(original.call_args.args[0], command)
            self.assertIs(rows[0]['process'], child)
            self.assertEqual(rows[0]['pid'], 42)
            child.assert_not_called()
            self.assertEqual(child.method_calls, [])

    def test_restores_launch_after_original_controller_failure(self):
        failure = RuntimeError('original controller failure')
        original = subprocess.Popen
        with self.assertRaises(RuntimeError) as seen:
            with PAIR.observe_children({'owned.exe'}, []):
                raise failure
        self.assertIs(seen.exception, failure)
        self.assertIs(subprocess.Popen, original)

    def test_launch_error_is_preserved_and_unrelated_process_is_not_recorded(self):
        rows = []
        with patch.object(subprocess, 'Popen', side_effect=OSError('original launch failure')) as original:
            with self.assertRaisesRegex(OSError, 'original launch failure'):
                with PAIR.observe_children({'owned.exe'}, rows):
                    subprocess.Popen(['owned.exe'])
            self.assertIs(subprocess.Popen, original)
        self.assertEqual(rows, [])
        with patch.object(subprocess, 'Popen', return_value=Mock(pid=43)):
            with PAIR.observe_children({'owned.exe'}, rows):
                subprocess.Popen(['unrelated.exe'])
        self.assertEqual(rows, [])


if __name__ == '__main__':
    unittest.main()
