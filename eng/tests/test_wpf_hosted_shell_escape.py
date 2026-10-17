"""Pure admission controls: no native libraries, process launch or injected input."""
import copy
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location("shell_escape", Path(__file__).parents[1] / "WindowsModalDesktop/shell-escape.py")
GUARD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GUARD)
ENV = dict(GITHUB_ACTIONS="true", RUNNER_ENVIRONMENT="github-hosted", RUNNER_OS="Windows",
           GITHUB_REPOSITORY="wieslawsoltes/LibreWPF")


class ShellEscapeTests(unittest.TestCase):
    class Native:
        def __init__(self):
            self.items = [dict(hwnd=i + 1, pid=100 + i, title=title,
                               **{"class": "Windows.UI.Core.CoreWindow"}, visible=True, session=2,
                               image=GUARD.ntpath.join(r"C:\Windows", image))
                          for i, (title, image) in enumerate(GUARD.IMAGES.items())]
            self.items.append(dict(hwnd=9, pid=99, title="Original application"))
            self.actions, self.keys, self.ticks = [], [], 0
            self.refuse = self.repeat = self.replace = False
            self.reads = 0
        def clock(self): return self.ticks
        def sleep(self, amount): self.ticks += amount
        def context(self): return dict(desktop="Default", windowsRoot=r"C:\Windows", session=2, shell=88)
        def foreground(self): return self.items[0]["hwnd"]
        def snapshot(self, hwnd):
            self.reads += 1
            result = copy.deepcopy(next(w for w in self.items if w["hwnd"] == hwnd))
            if self.replace and self.reads == 2: result["pid"] += 1
            return result
        def claim(self, kind, **details): self.actions.append((kind, details))
        def key(self, pid, code):
            assert len(self.actions) == len(self.keys) + 1, "Original action budget must be reserved first"
            self.keys.append((pid, code))
            if not self.refuse:
                item = self.items.pop(0)
                if self.repeat: self.items.insert(1, dict(item, hwnd=44))

    def run_guard(self, native, record, authorized=True, environment=None, deadline=60):
        GUARD.dismiss(native, native, native.claim, deadline, record, ENV if environment is None else environment,
                      authorized=authorized, clock=native.clock, sleep=native.sleep)

    def test_two_original_escapes_consume_actions_and_never_target_the_application(self):
        native, record = self.Native(), {}
        self.run_guard(native, record)
        self.assertEqual([(100, 27), (101, 27)], native.keys)
        self.assertEqual(2, len(native.actions))
        self.assertEqual(60, record["deadlineUnchanged"])
        self.assertEqual(3, record["setupDeadline"])
        self.assertEqual("Original application", record["remainingForeground"]["title"])

    def test_unapproved_or_nonhosted_calls_do_not_observe_or_send_input(self):
        for kwargs in (dict(authorized=False), dict(environment=ENV | {"RUNNER_ENVIRONMENT": "self-hosted"}),
                       dict(environment=ENV | {"GITHUB_REPOSITORY": "another/repository"})):
            native = self.Native()
            with self.subTest(kwargs=kwargs), self.assertRaises(RuntimeError): self.run_guard(native, {}, **kwargs)
            self.assertEqual((0, [], []), (native.reads, native.actions, native.keys))

    def test_wrong_shell_image_class_session_visibility_or_pid_sends_no_input(self):
        for change in ({"image": r"C:\Users\runneradmin\SearchHost.exe"}, {"class": "application"},
                       {"session": 3}, {"visible": False}, {"pid": 0}):
            native = self.Native(); native.items[0].update(change)
            with self.subTest(change=change), self.assertRaisesRegex(RuntimeError, "Unverified"):
                self.run_guard(native, {})
            self.assertEqual([], native.keys)

    def test_identity_replacement_before_escape_is_rejected(self):
        native = self.Native(); native.replace = True
        with self.assertRaisesRegex(RuntimeError, "identity changed"): self.run_guard(native, {})
        self.assertEqual([], native.keys)

    def test_refused_escape_is_never_retried(self):
        native, record = self.Native(), {}; native.refuse = True
        with self.assertRaisesRegex(RuntimeError, "did not yield"): self.run_guard(native, record)
        self.assertEqual([(100, 27)], native.keys)
        self.assertLess(native.ticks, .52)
        self.assertEqual(1, len(record["escapes"]))

    def test_reappeared_shell_does_not_receive_a_second_escape(self):
        native = self.Native(); native.repeat = True
        with self.assertRaisesRegex(RuntimeError, "Repeated"): self.run_guard(native, {})
        self.assertEqual([(100, 27), (101, 27)], native.keys)

    def test_expired_original_deadline_does_not_restart_or_send_input(self):
        native = self.Native(); native.ticks = 58
        with self.assertRaisesRegex(RuntimeError, "deadline expired"): self.run_guard(native, {}, deadline=60)
        self.assertEqual([], native.keys)

    def test_unknown_foreground_is_left_to_the_original_caption_admission(self):
        native = self.Native(); native.items[0]["title"] = "Other application"
        record = {}; self.run_guard(native, record)
        self.assertEqual([], native.keys)
        self.assertEqual("Other application", record["remainingForeground"]["title"])


if __name__ == "__main__":
    unittest.main()
