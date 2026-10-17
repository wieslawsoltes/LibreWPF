import copy
import unittest
from test_wpf_hosted_shell_escape import GUARD, ENV
import test_wpf_hosted_shell_escape as original

class ExtensionTests(unittest.TestCase):
    class Native(original.ShellEscapeTests.Native):
        def __init__(self):
            super().__init__()
            self.items.insert(1, dict(hwnd=88, pid=900, title='Program Manager',
                                     **{'class': 'Progman'}, visible=True, session=2))
            self.activations = []
            self.activation_refused = self.activation_changed = self.expire_activation = False
            self.duplicate = False
        def windows(self):
            result = copy.deepcopy(self.items)
            if self.duplicate: result.append(copy.deepcopy(next(w for w in result if w['title'] == 'Start')))
            return result
        def activate(self, hwnd):
            self.activations.append(hwnd)
            if self.activation_refused: return False
            target = next(w for w in self.items if w['hwnd'] == hwnd)
            self.items.remove(target); self.items.insert(0, target)
            if self.activation_changed: target['pid'] += 1
            if self.expire_activation: self.ticks = 3
            return True
        def key(self, pid, code):
            assert len([x for x in self.actions if x[0] == 'hosted-shell-escape']) == len(self.keys) + 1
            self.keys.append((pid, code)); self.items.pop(0)

    def run_guard(self, native, enabled=True):
        record = {}
        GUARD.dismiss(native, native, native.claim, 60, record, ENV, authorized=True,
                      allow_background_start=enabled, clock=native.clock, sleep=native.sleep)
        return record

    def test_original_scope_still_does_not_activate_background_start(self):
        n = self.Native(); record = self.run_guard(n, False)
        self.assertEqual([], n.activations)
        self.assertEqual([(100, 27)], n.keys)
        self.assertEqual('Program Manager', record['remainingForeground']['title'])

    def test_exact_failure_gets_one_verified_start_activation_and_two_escapes(self):
        n = self.Native(); record = self.run_guard(n)
        self.assertEqual([2], n.activations)
        self.assertEqual([(100, 27), (101, 27)], n.keys)
        self.assertEqual(['hosted-shell-escape', 'hosted-start-activation', 'hosted-shell-escape'], [a[0] for a in n.actions])
        self.assertEqual(60, record['deadlineUnchanged'])
        self.assertEqual(3, record['setupDeadline'])
        self.assertEqual(1, len(record['startActivations']))

    def test_background_activation_never_targets_other_foreground(self):
        for change in ({'hwnd': 87}, {'class': 'AnotherClass'}, {'session': 3}, {'pid': 0}, {'visible': False}):
            n = self.Native(); n.items[1].update(change)
            with self.subTest(change=change):
                self.run_guard(n)
                self.assertEqual([], n.activations)
                self.assertEqual([(100, 27)], n.keys)

    def test_missing_successful_search_escape_prevents_activation(self):
        n = self.Native(); n.items.pop(0)
        self.run_guard(n)
        self.assertEqual([], n.activations)
        self.assertEqual([], n.keys)

    def test_unverified_start_receives_no_activation_or_escape(self):
        for change in ({'image': r'C:\Users\StartMenuExperienceHost.exe'}, {'class': 'AnotherClass'},
                       {'pid': 0}, {'session': 3}, {'visible': False}):
            n = self.Native(); n.items[2].update(change)
            with self.subTest(change=change), self.assertRaisesRegex(RuntimeError, 'Unverified background'):
                self.run_guard(n)
            self.assertEqual([], n.activations)
            self.assertEqual([(100, 27)], n.keys)

    def test_duplicate_start_fails_before_activation(self):
        n = self.Native(); n.duplicate = True
        with self.assertRaisesRegex(RuntimeError, 'Exactly one'): self.run_guard(n)
        self.assertEqual([], n.activations)

    def test_refused_changed_or_expired_activation_never_sends_start_escape_or_retries(self):
        for flag in ('activation_refused', 'activation_changed', 'expire_activation'):
            n = self.Native(); setattr(n, flag, True)
            with self.subTest(flag=flag), self.assertRaisesRegex(RuntimeError, 'no retry'):
                self.run_guard(n)
            self.assertEqual([2], n.activations)
            self.assertEqual([(100, 27)], n.keys)

    def test_changed_foreground_before_activation_fails_without_mutation(self):
        n = self.Native()
        original = n.windows
        def change():
            result = original()
            n.items[0]['hwnd'] = 87
            return result
        n.windows = change
        with self.assertRaisesRegex(RuntimeError, 'identity or deadline'): self.run_guard(n)
        self.assertEqual([], n.activations)

if __name__ == '__main__': unittest.main()
