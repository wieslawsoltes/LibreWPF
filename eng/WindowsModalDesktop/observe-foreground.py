#!/usr/bin/env python3
"""Diagnostic only: observe foreground changes beside the unchanged qualified pair."""
import argparse
import ctypes as C
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import threading
import time


def require(value, message):
    if not value:
        raise RuntimeError(message)


class Native:
    def __init__(self):
        self.user = C.WinDLL('user32', use_last_error=True)
        self.dwm = C.WinDLL('dwmapi', use_last_error=True)
        self.dwm.DwmGetWindowAttribute.restype = C.c_int32
        self.dwm.DwmGetWindowAttribute.argtypes = [C.c_void_p, C.c_uint32, C.c_void_p, C.c_uint32]
        for name, result, args in (
            ('GetForegroundWindow', C.c_void_p, []),
            ('GetWindowThreadProcessId', C.c_uint32, [C.c_void_p, C.POINTER(C.c_uint32)]),
            ('GetWindowTextW', C.c_int32, [C.c_void_p, C.c_wchar_p, C.c_int32]),
            ('GetClassNameW', C.c_int32, [C.c_void_p, C.c_wchar_p, C.c_int32]),
            ('IsWindowVisible', C.c_int32, [C.c_void_p]),
            ('FindWindowExW', C.c_void_p, [C.c_void_p, C.c_void_p, C.c_wchar_p, C.c_wchar_p]),
        ):
            function = getattr(self.user, name)
            function.restype, function.argtypes = result, args

    def cloak(self, hwnd):
        value = C.c_uint32()
        result = self.dwm.DwmGetWindowAttribute(hwnd, 14, C.byref(value), C.sizeof(value))
        # A failed native read is unavailable evidence, never proof of no cloak.
        return dict(hresult=result, flags=value.value if result >= 0 else None)

    def window(self, hwnd):
        pid, thread = C.c_uint32(), 0
        title, name = C.create_unicode_buffer(512), C.create_unicode_buffer(256)
        if hwnd:
            thread = self.user.GetWindowThreadProcessId(hwnd, C.byref(pid))
            self.user.GetWindowTextW(hwnd, title, len(title))
            self.user.GetClassNameW(hwnd, name, len(name))
        return dict(hwnd=hwnd, pid=pid.value, thread=thread, title=title.value,
                    windowClass=name.value, visible=bool(hwnd and self.user.IsWindowVisible(hwnd)),
                    cloak=self.cloak(hwnd) if hwnd else None)

    def snapshot(self):
        hwnd = int(self.user.GetForegroundWindow() or 0)
        result = self.window(hwnd)
        shells = {}
        for title in ('Search', 'Start'):
            handle = int(self.user.FindWindowExW(None, None, 'Windows.UI.Core.CoreWindow', title) or 0)
            shells[title] = self.window(handle)
        result.update(shells=shells, after=int(self.user.GetForegroundWindow() or 0))
        return result


def observe(native, stop, record, clock=time.monotonic):
    start = clock()
    previous = None
    try:
        while not stop.is_set():
            require(clock() - start < 150, 'Read-only observation deadline expired')
            value = native.snapshot()
            if value != previous:
                require(len(record['changes']) < 256, 'Foreground change budget exceeded')
                record['changes'].append(dict(seconds=clock() - start, native=value))
                previous = value
            stop.wait(.05)
    except BaseException as error:
        record['observerError'] = repr(error)[:4096]


def invoke_original(driver, output):
    previous_argv, previous_path = sys.argv, sys.path[:]
    try:
        sys.path.insert(0, str(driver.parent))
        sys.argv = [str(driver), '--output', str(output)]
        runpy.run_path(str(driver), run_name='__main__')
    finally:
        sys.argv, sys.path[:] = previous_argv, previous_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--automation-root', type=Path, required=True)
    parser.add_argument('--producer-head', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    require(os.name == 'nt' and os.environ.get('GITHUB_ACTIONS') == 'true'
            and os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF',
            'Read-only diagnostic requires this repository\'s disposable Windows runner')
    require(re.fullmatch('[0-9a-f]{40}', args.producer_head), 'Exact producer commit required')
    root = args.automation_root.resolve(strict=True)
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    require(head == args.producer_head, 'Original automation must match qualified producer')
    subprocess.run(['git', 'diff', '--exit-code', 'HEAD', '--'], cwd=root, check=True)
    driver = root / 'eng/WindowsModalDesktop/run.py'
    require(driver.is_file() and not driver.is_symlink(), 'Original driver required')
    require(args.evidence.is_absolute() and not args.evidence.exists(), 'Fresh diagnostic evidence required')
    record = dict(qualified=False, diagnosticOnly=True, producerHead=head,
                  observerCommit=os.environ.get('GITHUB_SHA'),
                  observerSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                  originalDriverSha256=hashlib.sha256(driver.read_bytes()).hexdigest(),
                  startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  observerLimitSeconds=150, originalPerProcessSeconds=60,
                  maximumOriginalActionsPerProcess=64, changes=[])
    stop = threading.Event()
    thread = threading.Thread(target=observe, args=(Native(), stop, record), daemon=True)
    thread.start()
    try:
        invoke_original(driver, args.output)
        record['originalControllerCompleted'] = True
    except BaseException as error:
        record['originalControllerError'] = repr(error)[:4096]
        raise
    finally:
        stop.set()
        thread.join(1)
        record['observerStopped'] = not thread.is_alive()
        try:
            raw = json.dumps(record, indent=2).encode()
            require(len(raw) <= 512 * 1024, 'Diagnostic receipt exceeds bound')
            with args.evidence.open('xb') as stream:
                stream.write(raw)
        except BaseException:
            if 'originalControllerError' not in record:
                raise
            print('Could not save foreground diagnostic; original controller failure is preserved.', file=sys.stderr)
    require(record['observerStopped'] and 'observerError' not in record, 'Read-only observer failed')


if __name__ == '__main__':
    main()
