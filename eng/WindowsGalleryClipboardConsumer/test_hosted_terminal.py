import copy
import importlib.util
from pathlib import Path
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location('terminal', Path(__file__).with_name('hosted-terminal.py'))
terminal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(terminal)

ENVIRONMENT = {'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
               'RUNNER_OS': 'Windows', 'GITHUB_REPOSITORY': 'wieslawsoltes/LibreWPF'}
WINDOW = dict(hwnd=9, pid=123456, thread=4, title=r'C:\Windows\system32\wsl.exe',
              **{'class': 'CASCADIA_HOSTING_WINDOW_CLASS'}, visible=True, session=1,
              packageFamily='Microsoft.WindowsTerminal_8wekyb3d8bbwe',
              image=r'C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.0_arm64__8wekyb3d8bbwe\WindowsTerminal.exe')


class TerminalSetupTests(unittest.TestCase):
    def native(self):
        n = mock.Mock()
        n.context.return_value = dict(desktop='Default', session=1, system=r'C:\Windows\System32')
        n.foreground.side_effect = [9, 9, 9, 7]
        n.snapshot.return_value = copy.deepcopy(WINDOW)
        n.close.return_value = dict(succeeded=True, result=0, error=0)
        n.visible.return_value = False
        return n

    def run_case(self, native, record=None, environment=None, clock=None):
        record = {} if record is None else record
        terminal.close_startup(native, 7, 12, 60, record,
                               ENVIRONMENT if environment is None else environment,
                               clock=clock or (lambda: 10), sleep=mock.Mock())
        return record

    def test_one_normal_close_preserves_original_foreground_admission(self):
        n = self.native(); record = self.run_case(n)
        n.close.assert_called_once_with(9)
        self.assertEqual(1, record['closeRequests'])
        self.assertEqual(7, record['after'])
        self.assertFalse(record['inputSent'])
        self.assertFalse(record['activationRequested'])
        self.assertFalse(record['forcedTermination'])
        self.assertTrue(all(c[0] in ('context', 'foreground', 'snapshot', 'owned', 'idle', 'close', 'visible') for c in n.mock_calls))

    def test_no_change_when_original_application_keeps_foreground(self):
        n = self.native(); n.foreground.side_effect = [7]
        record = self.run_case(n)
        self.assertEqual(0, record['closeRequests'])
        n.context.assert_not_called(); n.close.assert_not_called()

    def test_every_host_boundary_is_required(self):
        for key in ENVIRONMENT:
            for value in (None, 'other'):
                n = self.native()
                with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                    self.run_case(n, environment=ENVIRONMENT | {key: value})
                self.assertEqual([], n.mock_calls)

    def test_original_window_process_and_package_identity_are_required(self):
        for key, value in [('hwnd', 7), ('pid', 12), ('pid', 0), ('visible', False), ('session', 2),
                           ('class', 'Other'), ('title', 'Windows Terminal'),
                           ('title', r'C:\Other\wsl.exe'), ('packageFamily', 'Other_8wekyb3d8bbwe'),
                           ('image', r'C:\Windows\other.exe')]:
            n = self.native(); n.snapshot.return_value[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError): self.run_case(n)
            n.close.assert_not_called()

    def test_replaced_window_or_foreground_is_never_closed(self):
        for replace_window in (False, True):
            n = self.native()
            if replace_window:
                n.snapshot.side_effect = [WINDOW, WINDOW | {'thread': 99}]
            else:
                n.foreground.side_effect = [9, 9, 10]
            with self.subTest(replace_window=replace_window), self.assertRaises(RuntimeError): self.run_case(n)
            n.close.assert_not_called()

    def test_normal_close_refusal_or_missing_foreground_return_is_not_retried(self):
        for refused in (False, True):
            n = self.native()
            if refused: n.close.return_value['succeeded'] = False
            else: n.foreground.side_effect = [9, 9, 9, 10]
            with self.subTest(refused=refused), self.assertRaises(RuntimeError): self.run_case(n)
            n.close.assert_called_once_with(9)

    def test_deadline_and_held_input_prevent_close(self):
        for expired in (False, True):
            n = self.native()
            if not expired: n.idle.side_effect = RuntimeError('held input')
            with self.subTest(expired=expired), self.assertRaises(RuntimeError):
                self.run_case(n, clock=lambda: 58 if expired else 10)
            n.close.assert_not_called()

    def test_close_wait_is_bounded_and_cannot_be_reentered(self):
        n = self.native(); n.visible.return_value = True
        record = {}
        with self.assertRaisesRegex(RuntimeError, 'within original budget'):
            self.run_case(n, record, clock=mock.Mock(side_effect=[10, 10, 10, 12]))
        n.close.assert_called_once_with(9)
        with self.assertRaisesRegex(RuntimeError, 'cannot be repeated'):
            self.run_case(n, record)
        n.close.assert_called_once_with(9)


if __name__ == '__main__': unittest.main()
