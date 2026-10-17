import copy
import ctypes
from types import SimpleNamespace
import unittest
from test_wpf_hosted_shell_escape import GUARD, ENV
from test_wpf_hosted_desktop_setup import SETUP
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
        def cloak(self, hwnd): return dict(hresult=0, flags=0)
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

class CloakCompletionTests(unittest.TestCase):
    class Native(ExtensionTests.Native):
        def __init__(self):
            super().__init__()
            self.sent_start = False
            self.flags, self.hresult, self.replace_after, self.expire_after = 2, 0, False, False
            self.cloak_reads = 0
        def key(self, pid, code):
            if pid != 101:
                return super().key(pid, code)
            self.keys.append((pid, code))
            self.sent_start = True
        def cloak(self, hwnd):
            if not self.sent_start: return super().cloak(hwnd)
            self.cloak_reads += 1
            if self.expire_after: self.ticks = 3
            if self.replace_after: self.items[0]['pid'] += 1
            return dict(hresult=self.hresult, flags=self.flags if self.hresult == 0 else None)

    run_guard = ExtensionTests.run_guard

    def test_hidden_start_can_retain_foreground_without_another_action(self):
        n = self.Native(); record = self.run_guard(n)
        self.assertEqual([(100, 27), (101, 27)], n.keys)
        self.assertEqual([2], n.activations)
        self.assertEqual(3, len(n.actions))
        self.assertEqual(2, record['escapes'][-1]['foregroundAfter'])
        self.assertEqual(2, n.cloak_reads)
        self.assertEqual(3, record['setupDeadline'])
        self.assertLess(n.ticks, .5)

    def test_visible_start_still_fails_the_original_half_second_bound(self):
        n = self.Native(); n.flags = 0
        with self.assertRaisesRegex(RuntimeError, 'did not yield'): self.run_guard(n)
        self.assertEqual(2, len(n.keys))
        self.assertLess(n.ticks, .52)

    def test_cloak_completion_requires_two_consecutive_shell_hidden_observations(self):
        n = self.Native()
        values = iter((0, 2, 0, 2, 2))
        n.cloak = lambda hwnd: dict(hresult=0, flags=next(values))
        record = self.run_guard(n)
        self.assertEqual([2, 0, 2, 2], [v['cloak']['flags'] for v in record['escapes'][-1]['cloakAfter']])
        self.assertEqual(2, len(n.keys))

    def test_failed_or_other_cloak_flags_never_prove_shell_dismissal(self):
        for result, flags in ((-1, None), (0, 1), (0, 3), (0, 4), (0, 8)):
            n = self.Native(); n.hresult, n.flags = result, flags
            with self.subTest(result=result, flags=flags), self.assertRaisesRegex(RuntimeError, 'unproven'):
                self.run_guard(n)
            self.assertEqual(1, n.cloak_reads)
            self.assertEqual(2, len(n.keys))

    def test_replacement_and_deadline_expiry_do_not_pass_cloak_completion(self):
        for flag in ('replace_after', 'expire_after'):
            n = self.Native(); setattr(n, flag, True)
            with self.subTest(flag=flag), self.assertRaises(RuntimeError): self.run_guard(n)
            self.assertEqual(2, len(n.keys))
            self.assertEqual([2], n.activations)

    def test_already_hidden_start_receives_no_escape(self):
        n = self.Native(); n.cloak = lambda hwnd: dict(hresult=0, flags=2)
        with self.assertRaisesRegex(RuntimeError, 'uncloaked before Escape'): self.run_guard(n)
        self.assertEqual([(100, 27)], n.keys)


class CompletionTests(unittest.TestCase):
    run_guard = ExtensionTests.run_guard
    class PendingNative(ExtensionTests.Native):
        def __init__(self, pending=0, delay=.05):
            super().__init__()
            self.pending, self.delay = pending, delay
            self.started = None
            self.changed_during_wait = False
        def activate(self, hwnd):
            accepted = super().activate(hwnd)
            self.started = self.ticks
            return accepted
        def foreground(self):
            if self.started is not None and self.ticks - self.started < self.delay:
                return self.pending
            return super().foreground()
        def sleep(self, amount):
            super().sleep(amount)
            if self.changed_during_wait:
                next(w for w in self.items if w['title'] == 'Start')['pid'] += 1

    def test_actual_ctypes_null_completion_uses_the_original_bounded_observation(self):
        n = self.PendingNative()
        raw = n.foreground
        boundary = object.__new__(SETUP.Desktop)
        boundary.u = SimpleNamespace(GetForegroundWindow=ctypes.CFUNCTYPE(ctypes.c_void_p)(raw))
        n.foreground = boundary.foreground
        record = self.run_guard(n)
        self.assertEqual([2], n.activations)
        self.assertEqual([(100, 27), (101, 27)], n.keys)
        self.assertEqual(3, len(n.actions))
        observations = record['startActivations'][0]['completionObservations']
        self.assertEqual(0, observations[0]['foreground'])
        self.assertEqual(2, observations[-1]['foreground'])
        self.assertLess(n.ticks, 1)
        self.assertEqual(3, record['setupDeadline'])

    def test_accepted_pending_activation_keeps_one_request_and_original_actions(self):
        for pending in (0, 88):
            n = self.PendingNative(pending)
            record = self.run_guard(n)
            self.assertEqual([2], n.activations)
            self.assertEqual([(100, 27), (101, 27)], n.keys)
            self.assertEqual(3, len(n.actions))
            self.assertEqual(3, record['setupDeadline'])
            observations = record['startActivations'][0]['completionObservations']
            self.assertEqual(pending, observations[0]['foreground'])
            self.assertEqual(2, observations[-1]['foreground'])
            self.assertLess(n.ticks, 1)

    def test_pending_activation_never_sends_escape_on_expiry(self):
        n = self.PendingNative(delay=2)
        with self.assertRaisesRegex(RuntimeError, 'deadline changed'): self.run_guard(n)
        self.assertEqual([2], n.activations)
        self.assertEqual([(100, 27)], n.keys)
        self.assertLess(n.ticks, 1.02)

    def test_completion_cannot_extend_original_setup_deadline(self):
        n = self.PendingNative(delay=2)
        activate = n.activate
        def late_activate(hwnd):
            n.ticks = 2.8
            return activate(hwnd)
        n.activate = late_activate
        record = {}
        with self.assertRaisesRegex(RuntimeError, 'deadline changed'):
            GUARD.dismiss(n, n, n.claim, 60, record, ENV, authorized=True,
                          allow_background_start=True, clock=n.clock, sleep=n.sleep)
        self.assertEqual(3, record['setupDeadline'])
        self.assertEqual(3, record['startActivations'][0]['completionDeadline'])
        self.assertEqual([2], n.activations)
        self.assertEqual([(100, 27)], n.keys)
        self.assertLess(n.ticks, 3.02)

    def test_pending_identity_change_stops_before_input(self):
        n = self.PendingNative(); n.changed_during_wait = True
        with self.assertRaisesRegex(RuntimeError, 'identity or deadline'): self.run_guard(n)
        self.assertEqual([2], n.activations)
        self.assertEqual([(100, 27)], n.keys)

    def test_pending_unknown_foreground_stops_immediately(self):
        n = self.PendingNative(pending=9876)
        with self.assertRaisesRegex(RuntimeError, 'unexpected foreground'): self.run_guard(n)
        self.assertEqual([2], n.activations)
        self.assertEqual([(100, 27)], n.keys)
        self.assertEqual(0, n.ticks)

if __name__ == '__main__': unittest.main()
