"""Temporarily fit the original 900-DIP Gallery window on a disposable CI desktop.

Enumerate real driver modes, change once before the app starts, and restore the
recorded mode afterward. This never operates on a local or self-hosted desktop.
"""
import argparse
import ctypes as C
from ctypes import wintypes as W
import hashlib
import json
import math
import os
from pathlib import Path
import time

U16, U32, I32 = C.c_uint16, C.c_uint32, C.c_int32


class DevMode(C.Structure):
    # DEVMODEW's display union; explicit Windows widths also allow offline tests.
    _fields_ = [("deviceName", U16 * 32), ("specVersion", U16), ("driverVersion", U16),
                ("size", U16), ("driverExtra", U16), ("fields", U32),
                ("x", I32), ("y", I32), ("orientation", U32), ("fixedOutput", U32),
                ("color", U16), ("duplex", U16), ("yResolution", U16),
                ("ttOption", U16), ("collate", U16), ("formName", U16 * 32),
                ("logPixels", U16), ("bits", U32), ("width", U32), ("height", U32),
                ("flags", U32), ("frequency", U32), ("icmMethod", U32),
                ("icmIntent", U32), ("mediaType", U32), ("ditherType", U32),
                ("reserved1", U32), ("reserved2", U32),
                ("panningWidth", U32), ("panningHeight", U32)]


class DisplayDevice(C.Structure):
    _fields_ = [("size", U32), ("name", U16 * 32), ("description", U16 * 128),
                ("flags", U32), ("id", U16 * 128), ("key", U16 * 128)]


MODE_KEYS = ("x", "y", "width", "height", "bits", "frequency", "flags", "orientation", "fixedOutput")
PRESERVED_KEYS = tuple(k for k in MODE_KEYS if k not in ("width", "height"))
REQUIRED_FIELDS = 0x40000 | 0x80000 | 0x100000 | 0x200000 | 0x400000
ENVIRONMENT = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
               "RUNNER_OS": "Windows", "GITHUB_REPOSITORY": "wieslawsoltes/LibreWPF"}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def require_hosted(environment):
    require(all(environment.get(k) == v for k, v in ENVIRONMENT.items()),
            "Requires the disposable GitHub-hosted LibreWPF Windows desktop")


def values(mode):
    return {k: getattr(mode, k) for k in MODE_KEYS}


def select_mode(current, modes, dpi):
    require(96 <= dpi <= 240, "Unsupported actual system DPI; DPI is never changed")
    minimum = (math.ceil(1280 * dpi / 96), math.ceil(1024 * dpi / 96))
    if current.width >= minimum[0] and current.height >= minimum[1]:
        return current
    candidates = [m for m in modes if m.width >= minimum[0] and m.height >= minimum[1]
                  and m.width <= 4096 and m.height <= 4096
                  and all(getattr(m, k) == getattr(current, k) for k in PRESERVED_KEYS)]
    require(candidates, "No enumerated mode fits the original Gallery window without changing display policy")
    return min(candidates, key=lambda m: (m.width * m.height, m.width, m.height))


def decode_mode(encoded):
    raw = bytes.fromhex(encoded)
    require(len(raw) == 220, "Invalid original DEVMODEW size")
    result = DevMode.from_buffer_copy(raw)
    require(result.size == 220 and result.driverExtra == 0, "Unsupported private display mode")
    require(result.fields & REQUIRED_FIELDS == REQUIRED_FIELDS, "Incomplete native display mode")
    return result


def text(array):
    return bytes(array).decode("utf-16-le").split("\0", 1)[0]


class Display:
    def __init__(self):
        require(os.name == "nt", "Windows only")
        require(C.sizeof(DevMode) == 220 and C.sizeof(DisplayDevice) == 840, "Windows ABI size mismatch")
        self.u = C.WinDLL("user32", use_last_error=True)
        self.k = C.WinDLL("kernel32", use_last_error=True)
        def bind(lib, name, result, *args):
            fn = getattr(lib, name); fn.restype = result; fn.argtypes = args
        bind(self.u, "SetProcessDpiAwarenessContext", W.BOOL, C.c_void_p)
        bind(self.u, "GetDpiForSystem", U32)
        bind(self.u, "GetSystemMetrics", C.c_int, C.c_int)
        bind(self.u, "EnumDisplayDevicesW", W.BOOL, W.LPCWSTR, U32, C.POINTER(DisplayDevice), U32)
        bind(self.u, "EnumDisplaySettingsExW", W.BOOL, W.LPCWSTR, U32, C.POINTER(DevMode), U32)
        bind(self.u, "ChangeDisplaySettingsExW", I32, W.LPCWSTR, C.POINTER(DevMode), W.HWND, U32, W.LPVOID)
        bind(self.u, "SystemParametersInfoW", W.BOOL, U32, U32, W.LPVOID, U32)
        bind(self.u, "OpenInputDesktop", W.HANDLE, U32, W.BOOL, U32)
        bind(self.u, "CloseDesktop", W.BOOL, W.HANDLE)
        bind(self.u, "GetUserObjectInformationW", W.BOOL, W.HANDLE, C.c_int, W.LPVOID, U32, C.POINTER(U32))
        bind(self.u, "GetShellWindow", W.HWND)
        bind(self.u, "GetWindowThreadProcessId", U32, W.HWND, C.POINTER(U32))
        bind(self.k, "ProcessIdToSessionId", W.BOOL, U32, C.POINTER(U32))
        require(self.u.SetProcessDpiAwarenessContext(C.c_void_p(-4)), "Cannot read actual physical desktop geometry")
        desktop = self.u.OpenInputDesktop(0, False, 1)
        require(desktop, "No interactive input desktop")
        try:
            name = C.create_unicode_buffer(256); needed = U32()
            require(self.u.GetUserObjectInformationW(desktop, 2, name, C.sizeof(name), C.byref(needed)),
                    "Cannot identify input desktop")
            require(name.value == "Default", "Only the ordinary Default desktop is admitted")
        finally:
            self.u.CloseDesktop(desktop)
        pid = U32(); own_session = U32(); shell_session = U32()
        require(self.u.GetWindowThreadProcessId(self.u.GetShellWindow(), C.byref(pid)), "No desktop shell")
        require(self.k.ProcessIdToSessionId(os.getpid(), C.byref(own_session))
                and self.k.ProcessIdToSessionId(pid, C.byref(shell_session))
                and own_session.value > 0 and own_session.value == shell_session.value,
                "Observer and shell must share the interactive session")
        self.session = own_session.value
        devices = []
        for index in range(64):
            device = DisplayDevice(); device.size = C.sizeof(device)
            if not self.u.EnumDisplayDevicesW(None, index, C.byref(device), 0):
                break
            if device.flags & 1:
                devices.append(device)
        else:
            raise RuntimeError("Display inventory exceeds bound")
        require(len(devices) == 1 and devices[0].flags & 4 and self.u.GetSystemMetrics(80) == 1,
                "Only one attached primary physical display is admitted")
        self.identity = {k: text(getattr(devices[0], k)) for k in ("name", "description", "id", "key")}
        self.identity["flags"] = devices[0].flags
        self.name = self.identity["name"]

    def mode(self, index=0xffffffff):
        mode = DevMode(); mode.size = C.sizeof(mode)
        if not self.u.EnumDisplaySettingsExW(self.name, index, C.byref(mode), 0):
            return None
        return decode_mode(bytes(mode).hex())

    def modes(self):
        modes = []
        for index in range(1024):
            mode = self.mode(index)
            if mode is None:
                return modes
            modes.append(mode)
        raise RuntimeError("Display mode inventory exceeds bound")

    def snapshot(self):
        current = self.mode(); require(current is not None, "No current display mode")
        work = W.RECT()
        require(self.u.SystemParametersInfoW(0x30, 0, C.byref(work), 0), "No physical work area")
        bounds = [self.u.GetSystemMetrics(i) for i in (76, 77, 78, 79)]
        require(bounds == [current.x, current.y, current.width, current.height],
                "Current mode and actual physical desktop differ")
        return {"mode": values(current), "dpi": self.u.GetDpiForSystem(), "bounds": bounds,
                "workArea": [work.left, work.top, work.right, work.bottom], "modeBytes": bytes(current).hex()}

    def change(self, mode, flags):
        result = self.u.ChangeDisplaySettingsExW(self.name, C.byref(mode), None, flags, None)
        require(result == 0, f"Native display change flags={flags} failed: {result}")


def save(path, record):
    temporary = path.with_name(path.name + ".tmp")
    with temporary.open("x") as stream:
        json.dump(record, stream, indent=2)
    temporary.replace(path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("prepare", "restore"))
    parser.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args()
    require_hosted(os.environ)
    identity = {k: os.environ.get(k) for k in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_JOB", "COMPUTERNAME")}
    require(all(identity.values()), "Missing runner identity")
    source = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    path = args.receipt if args.action == "prepare" else args.receipt.with_suffix(".restore.json")
    if args.action == "restore" and not args.receipt.exists():
        return  # Preparation never ran; no display mutation to restore.
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("x") as stream:
        json.dump({"action": args.action, "started": True}, stream)
    record = {"action": args.action, "runner": identity, "sourceSha256": source,
              "qualified": False, "registryChanged": False, "changeRequested": False}
    start = time.monotonic()
    try:
        display = Display()
        record.update(device=display.identity, session=display.session, before=display.snapshot())
        current = decode_mode(record["before"]["modeBytes"])
        if args.action == "prepare":
            modes = display.modes()
            record["enumeratedModes"] = [values(m) for m in modes]
            selected = select_mode(current, modes, record["before"]["dpi"])
            record["selected"] = values(selected)
        else:
            original = json.loads(args.receipt.read_text())
            require(original.get("runner") == identity and original.get("sourceSha256") == source,
                    "Restoration receipt belongs to another runner or source")
            require(original.get("device") == display.identity and original.get("session") == display.session,
                    "Display or session changed before restoration")
            selected = decode_mode(original["before"]["modeBytes"])
            require(values(current) in (values(selected), original.get("selected")),
                    "A foreign display mode replaced the owned mode; refusing restoration")
        if values(current) != values(selected):
            display.change(selected, 2)  # CDS_TEST: reject unsupported modes before mutation.
            record["changeRequested"] = True
            save(path, record)  # Persist the original exact mode before the single dynamic change.
            display.change(selected, 0)  # No CDS_UPDATEREGISTRY or unsafe-mode flags.
        record["after"] = after = display.snapshot()
        require(after["mode"] == values(selected), "Native display readback differs from selected mode")
        expected_dpi = record["before"]["dpi"] if args.action == "prepare" else original["before"]["dpi"]
        require(after["dpi"] == expected_dpi, "System DPI changed")
        if args.action == "prepare":
            left, top, right, bottom = after["workArea"]
            minimum = math.ceil(960 * expected_dpi / 96)
            require(right - left >= minimum and bottom - top >= minimum,
                    "Physical work area cannot contain the unchanged Gallery window")
        record["success"] = True
    except Exception as error:
        record["error"] = str(error)
        raise
    finally:
        record["elapsedSeconds"] = time.monotonic() - start
        save(path, record)


if __name__ == "__main__":
    main()
