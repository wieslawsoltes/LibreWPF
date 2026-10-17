#!/usr/bin/env python3
"""Observe the unchanged acceptance driver. This run never qualifies a release."""
import argparse
import ctypes as C
from ctypes import wintypes as W
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import sys
import time


def capture_client(desktop, snapshot, pid, path, raster_operation=0x00CC0020):
    """Keep actual client pixels, including uniform failures, without source pixels."""
    hwnd = snapshot["hwnd"]
    desktop.owned_window(hwnd, pid)
    if snapshot["pid"] != pid or snapshot["frame"] <= 0:
        raise RuntimeError("Diagnostic requires the original owned presented frame")
    client = desktop.geometry(hwnd, pid)
    x, y, width, height = client
    if not (0 < width * height <= 2_000_000 and width > 0 and height > 0):
        raise RuntimeError("Diagnostic client capture exceeds the fixed bound")
    crop = (x, y, x + width, y + height)
    monitors = desktop.monitors(crop)
    before = desktop.unobstructed(hwnd, crop)
    dc = desktop.u.GetDC(0)
    if not dc:
        raise RuntimeError("Diagnostic screen DC unavailable")
    memory = bitmap = old = None
    try:
        memory = desktop.g.CreateCompatibleDC(dc)
        bitmap = desktop.g.CreateCompatibleBitmap(dc, width, height)
        if not memory or not bitmap:
            raise RuntimeError("Diagnostic capture storage unavailable")
        old = desktop.g.SelectObject(memory, bitmap)
        if not old or old == C.c_void_p(-1).value:
            old = None
            raise RuntimeError("Diagnostic bitmap selection failed")
        copy_started = time.monotonic()
        if not desktop.g.BitBlt(memory, 0, 0, width, height, dc, x, y, raster_operation):
            raise RuntimeError("Diagnostic desktop BitBlt failed")
        copy_seconds = time.monotonic() - copy_started
        if not desktop.g.SelectObject(memory, old):
            raise RuntimeError("Diagnostic bitmap deselection failed")
        old = None
        header = struct.pack("<IiiHHIIiiII", 40, width, -height, 1, 32, 0,
                             width * height * 4, 0, 0, 0, 0)
        info = C.create_string_buffer(header)
        pixels = C.create_string_buffer(width * height * 4)
        read_started = time.monotonic()
        if desktop.g.GetDIBits(dc, bitmap, 0, height, pixels, info, 0) != height:
            raise RuntimeError("Diagnostic desktop pixels incomplete")
        read_seconds = time.monotonic() - read_started
        if desktop.geometry(hwnd, pid) != client or desktop.monitors(crop) != monitors:
            raise RuntimeError("Diagnostic desktop geometry changed during capture")
        after = desktop.unobstructed(hwnd, crop)
        raw = pixels.raw
        data = struct.pack("<2sIHHI", b"BM", 54 + len(raw), 0, 0, 54) + header + raw
        with path.open("xb") as output:
            output.write(data)
        rgb = bytearray(width * height * 3)
        rgb[0::3], rgb[1::3], rgb[2::3] = raw[0::4], raw[1::4], raw[2::4]
        return {"client": client, "crop": crop, "monitors": monitors,
                "rasterOperation": raster_operation, "bitBltSeconds": copy_seconds,
                "getDIBitsSeconds": read_seconds,
                "zOrderBefore": before, "zOrderAfter": after,
                "firstBgr": list(raw[:3]), "minimumRgbByte": min(rgb),
                "maximumRgbByte": max(rgb), "distinctRgbBytes": len(set(rgb)),
                "rgbSha256": hashlib.sha256(rgb).hexdigest(),
                "fileSha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}
    finally:
        failures = []
        if old and not desktop.g.SelectObject(memory, old): failures.append("deselect")
        if bitmap and not desktop.g.DeleteObject(bitmap): failures.append("bitmap release")
        if memory and not desktop.g.DeleteDC(memory): failures.append("DC release")
        if not desktop.u.ReleaseDC(0, dc): failures.append("screen DC release")
        if failures:
            error = RuntimeError("Diagnostic cleanup: " + ", ".join(failures))
            if sys.exception() is not None: sys.exception().add_note(str(error))
            else: raise error


def capture_details(desktop, snapshot, pid, path):
    # Read-only diagnostics distinguish a blank client from an excluded layered
    # presentation. Neither result replaces the original SRCCOPY capture.
    desktop.owned_window(snapshot["hwnd"], pid)
    hwnd = snapshot["hwnd"]
    get_style = desktop.u.GetWindowLongPtrW
    get_style.restype, get_style.argtypes = C.c_ssize_t, [W.HWND, C.c_int]
    get_affinity = desktop.u.GetWindowDisplayAffinity
    get_affinity.restype, get_affinity.argtypes = W.BOOL, [W.HWND, C.POINTER(W.DWORD)]
    affinity = W.DWORD()
    affinity_read = bool(get_affinity(hwnd, C.byref(affinity)))
    metadata = {"style": get_style(hwnd, -16) & 0xffffffff,
                "extendedStyle": get_style(hwnd, -20) & 0xffffffff,
                "displayAffinityRead": affinity_read,
                "displayAffinity": affinity.value if affinity_read else None}
    result = capture_client(desktop, snapshot, pid, path)
    result["window"] = metadata
    try:
        result["includingLayeredWindows"] = capture_client(
            desktop, snapshot, pid, path.with_name(path.stem + ".captureblt.bmp"), 0x40CC0020)
    except Exception as error:
        result["includingLayeredWindowsError"] = str(error)
    return result


def observed_capture(original, observer=capture_details):
    def capture(desktop, snapshot, pid, path):
        record = {"qualified": False, "diagnosticOnly": True, "snapshot": snapshot,
                  "originalDeadlineSeconds": 60, "startedMonotonic": time.monotonic()}
        try:
            record["clientCapture"] = observer(
                desktop, snapshot, pid, path.with_name(path.stem + ".diagnostic-client.bmp"))
        except Exception as error:
            record["diagnosticError"] = str(error)
        record["elapsedSeconds"] = time.monotonic() - record["startedMonotonic"]
        try:
            with path.with_name(path.stem + ".diagnostic.json").open("x") as output:
                json.dump(record, output, indent=2)
        except Exception as error:
            print("Diagnostic receipt failure: " + str(error), file=sys.stderr)
        # Preserve the original capture, assertions, exception and return value.
        return original(desktop, snapshot, pid, path)
    return capture


def observed_foreground(original, path):
    def prepare(desktop, snapshot, pid, deadline, record, host_activation=None):
        try:
            return original(desktop, snapshot, pid, deadline, record, host_activation)
        except BaseException:
            # Preserve the exact original failure before owned cleanup changes
            # the foreground. No activation, input, retry or extra wait.
            evidence = {"qualified": False, "diagnosticOnly": True,
                        "originalDeadline": deadline, "pid": pid, "target": snapshot["hwnd"]}
            try:
                evidence["foreground"] = desktop.window_observation(desktop.u.GetForegroundWindow())
                evidence["foregroundAfterObservation"] = desktop.u.GetForegroundWindow()
                with path.open("x") as output:
                    json.dump(evidence, output, indent=2)
            except Exception as error:
                print("Foreground diagnostic unavailable: " + str(error), file=sys.stderr)
            raise
    return prepare


def native_window_details(desktop, hwnd):
    bounds = W.RECT()
    exists = bool(desktop.u.IsWindow(hwnd))
    rect_read = exists and bool(desktop.u.GetWindowRect(hwnd, C.byref(bounds)))
    dwm = C.WinDLL('dwmapi', use_last_error=True)
    query = dwm.DwmGetWindowAttribute
    query.restype, query.argtypes = C.c_int32, [W.HWND, W.DWORD, W.LPVOID, W.DWORD]
    cloak = W.DWORD()
    result = query(hwnd, 14, C.byref(cloak), C.sizeof(cloak))
    return dict(exists=exists, visible=bool(exists and desktop.u.IsWindowVisible(hwnd)),
                rectangle=[bounds.left, bounds.top, bounds.right, bounds.bottom] if rect_read else None,
                cloakHresult=result, cloakFlags=cloak.value if result >= 0 else None)


def observed_window(original, directory, observer=native_window_details):
    count = 0
    def observe(desktop, hwnd):
        nonlocal count
        value = original(desktop, hwnd)
        # Keep the original helper's result and exception semantics. These extra
        # native reads identify a possible closing terminal/compositor overlay;
        # they never send input, wait for readiness or replace capture pixels.
        if count < 64:
            count += 1
            record = dict(qualified=False, diagnosticOnly=True, observedAt=time.monotonic(),
                          original=value)
            try:
                record['native'] = observer(desktop, hwnd)
            except Exception as error:
                record['observerError'] = str(error)
            try:
                with (directory / f'window-{count:03d}.diagnostic.json').open('x') as output:
                    json.dump(record, output, indent=2)
            except Exception as error:
                print('Window diagnostic receipt failure: ' + str(error), file=sys.stderr)
        return value
    return observe


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--driver", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--scene-observer", type=Path)
    args, original_arguments = parser.parse_known_args()
    driver = args.driver.resolve(strict=True)
    receipt = json.loads(driver.with_name("preparation.json").read_text())
    digest = hashlib.sha256(driver.read_bytes()).hexdigest()
    if driver.name != "run-windows.py" or digest != receipt["inputs"]["run-windows.py"]:
        raise RuntimeError("Original prepared driver identity mismatch")
    spec = importlib.util.spec_from_file_location("original_gallery_driver", driver)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.Desktop.capture = observed_capture(module.Desktop.capture)
    module.Desktop.window_observation = observed_window(module.Desktop.window_observation, args.output)
    module.Desktop.prepare_foreground = observed_foreground(
        module.Desktop.prepare_foreground, args.output / "foreground-failure.diagnostic.json")
    trace_environment = {"PROGPU_WPF_TRACE_NATIVE_LOOP": "1", "PROGPU_WPF_TRACE_RENDER_SURFACE": "1"}
    if args.scene_observer:
        observer = args.scene_observer.resolve(strict=True)
        observer_receipt = json.loads(observer.parents[3].joinpath("preparation.json").read_text())
        observer_sha = hashlib.sha256(observer.read_bytes()).hexdigest()
        if not observer_receipt.get("diagnosticOnly") or observer_sha != observer_receipt["dllSha256"]:
            raise RuntimeError("Prepared scene observer identity mismatch")
        existing = os.environ.get("DOTNET_STARTUP_HOOKS", "")
        trace_environment.update({
            "DOTNET_STARTUP_HOOKS": existing + (os.pathsep if existing else "") + str(observer),
            "PROGPU_GALLERY_SCENE_OBSERVER_ROOT": str(args.output.resolve()),
            "PROGPU_GALLERY_SCENE_OBSERVER_APP_SHA256": receipt["output"]["sha256"]["GalleryClipboardApp.exe"],
            "PROGPU_NATIVE_TRACE_PICTURE_MASK": "1",
        })
    os.environ.update(trace_environment)
    sys.argv = [str(driver), "--output", str(args.output), *original_arguments]
    record = {"qualified": False, "diagnosticOnly": True, "originalDriverSha256": digest,
              "observerSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              "producer": receipt["head"], "originalDeadlineSeconds": 60,
              "diagnosticEnvironment": trace_environment}
    print(json.dumps(record))
    try:
        code = module.main()
        record["originalExitCode"] = code
        return code
    finally:
        if args.output.is_dir():
            with (args.output / "diagnostic-run.json").open("x") as output:
                json.dump(record, output, indent=2)


if __name__ == "__main__":
    raise SystemExit(main())
