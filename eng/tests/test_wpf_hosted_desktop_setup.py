"""Verify that hosted desktop setup never closes an unverified or repeated owner."""
import copy
import ctypes
import importlib.util
from pathlib import Path
from types import SimpleNamespace
import unittest

SPEC = importlib.util.spec_from_file_location("modal_setup", Path(__file__).parents[1] / "WindowsModalDesktop/prepare-desktop.py")
SETUP = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(SETUP)

class DesktopSetupTests(unittest.TestCase):
    def test_actual_ctypes_hwnd_return_keeps_null_zero_and_live_handle_identity(self):
        native = object.__new__(SETUP.Desktop)
        for value in (0, 1, 0x100000001):
            with self.subTest(value=value):
                callback = ctypes.CFUNCTYPE(ctypes.c_void_p)(lambda: value)
                self.assertEqual(None if value == 0 else value, callback())
                native.u = SimpleNamespace(GetForegroundWindow=callback)
                self.assertEqual(value, native.foreground())
                self.assertIs(type(native.foreground()), int)

    class Native:
        def __init__(self):
            self.items = [
                dict(hwnd=i+1, pid=100+i, thread=200+i, title=title,
                     **{"class": "Windows.UI.Core.CoreWindow"}, visible=True, session=2,
                     image=SETUP.ntpath.join(r"C:\Windows", image))
                for i, (title, image) in enumerate(SETUP.EXPECTED.items())]
            self.sent = []
            self.foreign = False
            self.replace = False
            self.refuse = False
            self.reappear = False
            self.ticks = 0
        def clock(self): return self.ticks
        def sleep(self, amount): self.ticks += amount
        def context(self): return dict(desktop="Default", windowsRoot=r"C:\Windows", session=2, shell=9)
        def windows(self): return copy.deepcopy(self.items)
        def foreground(self): return 666 if self.foreign else self.items[0]["hwnd"] if self.items else 9
        def snapshot(self, hwnd):
            item = copy.deepcopy(next(w for w in self.items if w["hwnd"] == hwnd))
            if self.replace: item["pid"] += 1
            return item
        def close(self, hwnd):
            self.sent.append(hwnd)
            if not self.refuse:
                self.items = [w for w in self.items if w["hwnd"] != hwnd]
                if self.reappear:
                    item = dict(hwnd=88, pid=888, thread=999, title="Microsoft account",
                                **{"class": "Windows.UI.Core.CoreWindow"}, visible=True, session=2,
                                image=r"C:\Windows\System32\WWAHost.exe")
                    self.items.append(item)
            return dict(succeeded=True, result=0, error=0)
        def visible(self, hwnd): return any(w["hwnd"] == hwnd for w in self.items)

    def run_setup(self, native, record):
        SETUP.prepare(native, record, native.clock, native.sleep)

    def test_only_account_closes_and_shell_overlays_remain_observed_without_input(self):
        native, record = self.Native(), {}
        self.run_setup(native, record)
        self.assertEqual([1], native.sent)
        self.assertEqual(["Search", "Start"], [w["title"] for w in native.items])
        self.assertTrue(record["completed"])
        self.assertEqual(2, len(record["observations"]))
        self.assertTrue(all(c["closed"] for c in record["closes"]))

    def test_ordinary_shell_is_already_ready_without_close(self):
        native, record = self.Native(), {}; native.items = []
        self.run_setup(native, record)
        self.assertEqual([], native.sent)
        self.assertTrue(record["completed"])

    def test_explorer_account_proxy_is_retained_but_never_closed(self):
        native, record = self.Native(), {}
        proxy = dict(native.items[0], hwnd=99, **{"class": "Shell_OOBEProxy"}, image=r"C:\Windows\explorer.exe")
        native.items.append(proxy)
        original = native.close
        def close(hwnd):
            result = original(hwnd)
            if hwnd == 1: native.items = [w for w in native.items if w["hwnd"] != 99]
            return result
        native.close = close
        self.run_setup(native, record)
        self.assertEqual([1], native.sent)
        self.assertIn(proxy, record["observations"][0]["windows"])
        native = self.Native(); native.items.append(dict(proxy, image=r"C:\Users\runneradmin\explorer.exe"))
        with self.assertRaisesRegex(RuntimeError, "proxy"): self.run_setup(native, {})
        self.assertEqual([], native.sent)

    def test_wrong_process_image_session_class_or_duplicate_fails_before_any_close(self):
        changes = ({"image": r"C:\Users\runneradmin\SearchHost.exe"}, {"session": 3},
                   {"class": "ordinary app"}, {"pid": 0}, {"visible": False})
        for change in changes:
            native = self.Native(); native.items[1].update(change)
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                self.run_setup(native, {})
            self.assertEqual([], native.sent)
        native = self.Native(); native.items.append(dict(native.items[1], hwnd=77))
        with self.assertRaisesRegex(RuntimeError, "Duplicate"): self.run_setup(native, {})
        self.assertEqual([], native.sent)

    def test_unknown_foreground_and_replaced_owner_never_receive_close(self):
        for attribute in ("foreign", "replace"):
            native = self.Native(); setattr(native, attribute, True)
            with self.subTest(attribute=attribute), self.assertRaises(RuntimeError):
                self.run_setup(native, {})
            self.assertEqual([], native.sent)

    def test_reappeared_overlay_is_not_retried(self):
        native, record = self.Native(), {}; native.reappear = True
        with self.assertRaisesRegex(RuntimeError, "reappeared"):
            self.run_setup(native, record)
        self.assertEqual([1], native.sent)
        self.assertNotIn("completed", record)

    def test_refused_close_keeps_original_failure_and_bound(self):
        native, record = self.Native(), {}; native.refuse = True
        with self.assertRaisesRegex(RuntimeError, "declined"):
            self.run_setup(native, record)
        self.assertEqual([1], native.sent)
        self.assertFalse(record["closes"][0]["closed"])
        self.assertLess(native.ticks, 2.1)
        self.assertNotIn("completed", record)

    def test_native_z_order_retains_core_windows_and_rejects_cycles_or_destroyed_handles(self):
        class Windows:
            next_handle = {1: 2, 2: 0}
            dead = None
            def GetTopWindow(self, parent): return 1
            def GetWindow(self, hwnd, command):
                if command != 2: raise AssertionError("GW_HWNDNEXT required")
                return self.next_handle[hwnd]
            def IsWindow(self, hwnd): return hwnd != self.dead
            def IsWindowVisible(self, hwnd): return True
            def FindWindowExW(self, parent, previous, native_class, title): return 0
            def GetForegroundWindow(self): return 1
        desktop = SETUP.Desktop.__new__(SETUP.Desktop)
        desktop.u = Windows()
        desktop.snapshot = lambda hwnd: {"hwnd": hwnd, "class": "Windows.UI.Core.CoreWindow" if hwnd == 1 else "Shell_OOBEProxy"}
        self.assertEqual([1, 2], [w["hwnd"] for w in desktop.windows()])
        desktop.u.next_handle = {1: 2, 2: 1}
        with self.assertRaisesRegex(RuntimeError, "cycle"): desktop.windows()
        desktop.u.next_handle = {1: 2, 2: 0}; desktop.u.dead = 2
        with self.assertRaisesRegex(RuntimeError, "changed"): desktop.windows()

    def test_actual_foreground_and_exact_native_lookup_do_not_depend_on_desktop_app_enumeration(self):
        class Windows:
            def GetTopWindow(self, parent): return 0
            def IsWindow(self, hwnd): return True
            def IsWindowVisible(self, hwnd): return True
            def FindWindowExW(self, parent, previous, native_class, title):
                if native_class != "Windows.UI.Core.CoreWindow": raise AssertionError("Exact class required")
                return 8 if not previous and title == "Start" else 0
            def GetForegroundWindow(self): return 9
        desktop = SETUP.Desktop.__new__(SETUP.Desktop); desktop.u = Windows()
        desktop.snapshot = lambda hwnd: {"hwnd": hwnd}
        self.assertEqual([8, 9], [w["hwnd"] for w in desktop.windows()])

if __name__ == "__main__": unittest.main()

