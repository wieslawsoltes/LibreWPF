"""Prepare only the three observed Windows startup overlays on a disposable runner.

Normal WM_CLOSE only; no input synthesis, termination, settings or clipboard changes.
The application and its original acceptance deadline have not started.
"""
import argparse
import ctypes as C
from ctypes import wintypes as W
import hashlib
import json
import ntpath
import os
from pathlib import Path
import time

EXPECTED = {
    "Microsoft account": r"System32\WWAHost.exe",
    "Search": r"SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\SearchHost.exe",
    "Start": r"SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe",
}

def require(value, message):
    if not value:
        raise RuntimeError(message)

def admitted(window, windows_root, session):
    require(window["title"] in EXPECTED and window["class"] == "Windows.UI.Core.CoreWindow"
            and window["visible"] and window["pid"] > 0 and window["session"] == session,
            "Unidentified or noninteractive startup overlay")
    expected = ntpath.join(windows_root, EXPECTED[window["title"]])
    require(ntpath.normcase(window["image"]) == ntpath.normcase(expected),
            "Startup overlay image is not the exact Windows system image")

def prepare(native, record, clock=time.monotonic, sleep=time.sleep):
    deadline = clock() + 8
    record["context"] = context = native.context()
    require(context["desktop"] == "Default" and context["session"] > 0,
            "Ordinary interactive desktop required")
    closed = set()
    record["observations"], record["closes"] = [], []
    for _ in range(4):
        require(clock() < deadline, "Hosted startup setup deadline expired")
        windows, foreground = native.windows(), native.foreground()
        require(len(windows) <= 128, "Native window inventory exceeds bound")
        record["observations"].append(dict(foreground=foreground, windows=windows))
        # Explorer's OOBE owner has the same title as the real account CoreWindow.
        # It is retained as evidence, never selected as a close target.
        proxies = [w for w in windows if w["title"] == "Microsoft account" and w["class"] == "Shell_OOBEProxy"]
        for proxy in proxies:
            require(proxy["session"] == context["session"] and proxy["pid"] > 0
                    and ntpath.normcase(proxy["image"]) == ntpath.normcase(ntpath.join(context["windowsRoot"], "explorer.exe")),
                    "Unidentified account proxy")
        known = [w for w in windows if w["title"] in EXPECTED and w not in proxies]
        require(len({w["title"] for w in known}) == len(known), "Duplicate startup overlay identity")
        for window in known:
            admitted(window, context["windowsRoot"], context["session"])
        require(not foreground or foreground == context["shell"]
                or any(w["hwnd"] == foreground for w in known),
                "Unidentified foreground window; no close request sent")
        if not known:
            record["completed"] = True
            return
        window = next((w for name in EXPECTED for w in known if w["title"] == name), None)
        require(window["title"] not in closed, "Closed startup overlay reappeared; no retry permitted")
        require(native.snapshot(window["hwnd"]) == window, "Startup overlay identity changed before close")
        require(native.foreground() == foreground, "Foreground identity changed before close")
        closed.add(window["title"])
        receipt = dict(window=window, message=0x10)
        record["closes"].append(receipt)
        receipt["message"] = native.close(window["hwnd"])
        until = min(deadline, clock() + 2)
        while native.visible(window["hwnd"]) and clock() < until:
            sleep(.025)
        receipt["closed"] = not native.visible(window["hwnd"])
        require(receipt["closed"], "Startup overlay declined normal close")
    raise RuntimeError("Hosted startup close budget exceeded")

class Desktop:
    def __init__(self):
        self.u = C.WinDLL("user32", use_last_error=True)
        self.k = C.WinDLL("kernel32", use_last_error=True)
        def bind(lib, name, result, *args):
            fn = getattr(lib, name); fn.restype = result; fn.argtypes = args
        bind(self.u, "GetForegroundWindow", W.HWND)
        bind(self.u, "GetShellWindow", W.HWND)
        bind(self.u, "GetTopWindow", W.HWND, W.HWND)
        bind(self.u, "GetWindow", W.HWND, W.HWND, W.UINT)
        for name in ("IsWindow", "IsWindowVisible"):
            bind(self.u, name, W.BOOL, W.HWND)
        bind(self.u, "GetWindowThreadProcessId", W.DWORD, W.HWND, C.POINTER(W.DWORD))
        for name in ("GetClassNameW", "GetWindowTextW"):
            bind(self.u, name, C.c_int, W.HWND, W.LPWSTR, C.c_int)
        bind(self.u, "OpenInputDesktop", W.HANDLE, W.DWORD, W.BOOL, W.DWORD)
        bind(self.u, "CloseDesktop", W.BOOL, W.HANDLE)
        bind(self.u, "GetUserObjectInformationW", W.BOOL, W.HANDLE, C.c_int, W.LPVOID, W.DWORD, C.POINTER(W.DWORD))
        bind(self.u, "SendMessageTimeoutW", C.c_ssize_t, W.HWND, W.UINT, C.c_size_t, C.c_ssize_t, W.UINT, W.UINT, C.POINTER(C.c_size_t))
        bind(self.k, "OpenProcess", W.HANDLE, W.DWORD, W.BOOL, W.DWORD)
        bind(self.k, "CloseHandle", W.BOOL, W.HANDLE)
        bind(self.k, "QueryFullProcessImageNameW", W.BOOL, W.HANDLE, W.DWORD, W.LPWSTR, C.POINTER(W.DWORD))
        bind(self.k, "ProcessIdToSessionId", W.BOOL, W.DWORD, C.POINTER(W.DWORD))
        bind(self.k, "GetWindowsDirectoryW", W.UINT, W.LPWSTR, W.UINT)

    def session(self, pid):
        result = W.DWORD()
        require(self.k.ProcessIdToSessionId(pid, C.byref(result)), "Native process session unavailable")
        return result.value

    def context(self):
        desktop = self.u.OpenInputDesktop(0, False, 1)
        require(desktop, "Input desktop unavailable")
        try:
            name, needed = C.create_unicode_buffer(256), W.DWORD()
            require(self.u.GetUserObjectInformationW(desktop, 2, name, C.sizeof(name), C.byref(needed)), "Desktop identity unavailable")
            root = C.create_unicode_buffer(1024)
            require(0 < self.k.GetWindowsDirectoryW(root, 1024) < 1024, "Windows root unavailable")
            shell = self.u.GetShellWindow()
            shell_pid = W.DWORD()
            require(shell and self.u.GetWindowThreadProcessId(shell, C.byref(shell_pid)), "Shell identity unavailable")
            session = self.session(os.getpid())
            require(self.session(shell_pid.value) == session, "Shell belongs to another session")
            return dict(desktop=name.value, windowsRoot=root.value, session=session, shell=shell)
        finally:
            require(self.u.CloseDesktop(desktop), "Desktop handle cleanup failed")

    def snapshot(self, hwnd):
        pid = W.DWORD()
        thread = self.u.GetWindowThreadProcessId(hwnd, C.byref(pid))
        title, name = C.create_unicode_buffer(256), C.create_unicode_buffer(256)
        self.u.GetWindowTextW(hwnd, title, 256); self.u.GetClassNameW(hwnd, name, 256)
        result = dict(hwnd=hwnd, pid=pid.value, thread=thread, title=title.value,
                      **{"class": name.value}, visible=self.visible(hwnd),
                      session=self.session(pid.value) if pid.value else None)
        if title.value in EXPECTED:
            handle = self.k.OpenProcess(0x1000, False, pid.value)
            require(handle, "Startup process identity unavailable")
            try:
                image, size = C.create_unicode_buffer(1024), W.DWORD(1024)
                require(self.k.QueryFullProcessImageNameW(handle, 0, image, C.byref(size)), "Startup image unavailable")
                result["image"] = image.value
            finally:
                require(self.k.CloseHandle(handle), "Process identity handle cleanup failed")
        return result

    def windows(self):
        # EnumWindows omits the observed UWP CoreWindows on modern Windows.
        # Follow the actual desktop Z order with cycle/count and live-handle
        # guards, matching the existing original input helper's inventory route.
        result, seen = [], set()
        hwnd = self.u.GetTopWindow(None)
        while hwnd:
            require(hwnd not in seen and len(seen) < 512, "Native window inventory cycle or bound")
            seen.add(hwnd)
            require(self.u.IsWindow(hwnd), "Native window changed during inventory")
            if self.visible(hwnd):
                require(len(result) < 128, "Native window inventory exceeds bound")
                result.append(self.snapshot(hwnd))
            hwnd = self.u.GetWindow(hwnd, 2)  # GW_HWNDNEXT; no inferred window IDs.
        return result

    def foreground(self):
        return self.u.GetForegroundWindow()

    def visible(self, hwnd):
        return bool(self.u.IsWindow(hwnd) and self.u.IsWindowVisible(hwnd))

    def close(self, hwnd):
        result = C.c_size_t(); C.set_last_error(0)
        sent = self.u.SendMessageTimeoutW(hwnd, 0x10, 0, 0, 0x22, 1000, C.byref(result))
        return dict(succeeded=bool(sent), result=result.value, error=C.get_last_error())

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    require(os.name == "nt" and all(os.environ.get(k) == v for k, v in {
        "GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "RUNNER_OS": "Windows",
        "GITHUB_REPOSITORY": "wieslawsoltes/LibreWPF"}.items()), "Only the disposable hosted Windows runner is admitted")
    require(not args.output.exists(), "Original desktop setup evidence already exists")
    record = dict(qualified=False, completed=False, inputSent=False, forcedTermination=False,
                  settingsChanged=False, clipboardAccessed=False, maximumCloseRequests=3,
                  sourceSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    start = time.monotonic()
    try:
        prepare(Desktop(), record)
    except BaseException as error:
        record["error"] = str(error)
        raise
    finally:
        record["elapsedSeconds"] = time.monotonic() - start
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x") as output:
            json.dump(record, output, indent=2)

if __name__ == "__main__":
    main()

