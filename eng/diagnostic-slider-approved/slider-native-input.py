"""Bounded XTEST Slider diagnostic. Execute input only after explicit user approval."""
from pathlib import Path
import argparse
import ctypes as C
import ctypes.util
import hashlib
import json
import math
import os
import signal
import time

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'run2-native-input'
P = C.POINTER
D, W, I, U = C.c_void_p, C.c_ulong, C.c_int, C.c_uint


class Attributes(C.Structure):
    _fields_ = [('x', I), ('y', I), ('width', I), ('height', I), ('border_width', I),
                ('depth', I), ('visual', D), ('root', W), ('class_', I),
                ('bit_gravity', I), ('win_gravity', I), ('backing_store', I),
                ('backing_planes', W), ('backing_pixel', W), ('save_under', I),
                ('colormap', W), ('map_installed', I), ('map_state', I),
                ('all_event_masks', C.c_long), ('your_event_mask', C.c_long),
                ('do_not_propagate_mask', C.c_long), ('override_redirect', I), ('screen', D)]


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


"""Prepared read-only post-release observation; no input operations."""
import time


def observe_released_button(verify, point, record, *, clock=time.monotonic, sleep=time.sleep):
    deadline = clock() + .250
    record.update(limitSeconds=.250, observations=[], completed=False)
    for index in range(26):
        if clock() >= deadline:
            raise TimeoutError('Original left release not observed within 250ms')
        # The caller rechecks every original process/window/focus/geometry/key/
        # pointer guard. Only our already-issued release may still be pending.
        state = verify(point=point, release_pending=True)
        record['observations'].append(state)
        released = state['mask'] & ~(2 | 16)
        if index == 0:
            record['originalImmediateGuardPassed'] = released == 0
        if released not in (0, 256):
            raise AssertionError('Unexpected held button or modifier')
        if clock() >= deadline:
            raise TimeoutError('Original left release not observed within 250ms')
        if released == 0:
            record['completed'] = True
            return state
        sleep(min(.010, max(0, deadline-clock())))
    raise TimeoutError('Post-release read-only observation inventory exhausted')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--execute-approved-input', action='store_true')
    args = parser.parse_args()
    runner = json.loads((OUT / 'runner.json').read_text())
    ready = json.loads((OUT / 'ready.json').read_text())
    assert ready['nativeKind'] == 'X11' and ready['pid'] == runner['pid']
    assert ready['frame'] > 0 and len(ready['sliders']) == 4
    pid, window = runner['pid'], ready['nativeHandle']
    stage = Path(runner['stage']).resolve()
    assert stage.name.startswith('progpu-slider-52-') and stage.parent == Path('/tmp')
    manifest = json.loads((ROOT / 'payload-manifest.json').read_text())
    assert len(manifest) == 281 and runner['deadlineSeconds'] == 180
    assert all(digest(stage / item['path']) == item['sha256'] for item in manifest)
    assert Path(f'/proc/{pid}/exe').resolve() == stage / 'GallerySliderProbe'
    process_stat = Path(f'/proc/{pid}/stat').read_text().split(') ', 1)[1].split()[19]
    started = float(runner['startedMonotonic'])
    os.environ.update(json.loads((ROOT / 'guest-session.json').read_text()))
    x = C.CDLL(ctypes.util.find_library('X11'))
    t = C.CDLL(ctypes.util.find_library('Xtst'))

    def api(lib, name, result, *arguments):
        fn = getattr(lib, name); fn.restype = result; fn.argtypes = list(arguments)
        return fn

    api(x, 'XOpenDisplay', D, C.c_char_p)
    api(x, 'XDefaultRootWindow', W, D)
    api(x, 'XCloseDisplay', I, D)
    api(x, 'XSync', I, D, I)
    api(x, 'XFree', I, D)
    api(x, 'XInternAtom', W, D, C.c_char_p, I)
    api(x, 'XGetWindowAttributes', I, D, W, P(Attributes))
    api(x, 'XGetWindowProperty', I, D, W, W, C.c_long, C.c_long, I, W,
        P(W), P(I), P(W), P(W), P(P(C.c_ubyte)))
    api(x, 'XTranslateCoordinates', I, D, W, W, I, I, P(I), P(I), P(W))
    api(x, 'XQueryPointer', I, D, W, P(W), P(W), P(I), P(I), P(I), P(I), P(U))
    api(x, 'XGetInputFocus', I, D, P(W), P(I))
    api(x, 'XQueryKeymap', I, D, P(C.c_char))
    api(t, 'XTestQueryExtension', I, D, P(I), P(I), P(I), P(I))
    api(t, 'XTestFakeMotionEvent', I, D, I, I, I, W)
    api(t, 'XTestFakeButtonEvent', I, D, U, I, W)
    display = x.XOpenDisplay(None)
    assert display, 'Actual X11 session is unavailable'
    native_root = x.XDefaultRootWindow(display)
    errors, journal, held = [], [], False
    callback_type = C.CFUNCTYPE(I, D, D)
    error_callback = callback_type(lambda d, e: (errors.append('X11 protocol error'), 0)[1])
    api(x, 'XSetErrorHandler', D, callback_type)(error_callback)
    destination = OUT / ('native-input.json' if args.execute_approved_input else 'native-inspection.json')
    if args.execute_approved_input:
        (OUT / 'native-input-started.json').open('x').write(json.dumps({'utc': time.time(), 'pid': pid, 'window': window}))
    result = {'qualified': False, 'inputRequested': args.execute_approved_input,
              'pid': pid, 'window': window, 'journal': journal, 'deadlineSeconds': 180,
              'inputReserveEndsAtSeconds': 160, 'inputMethod': 'XTEST', 'completed': False}

    def sync():
        x.XSync(display, 0)
        assert not errors, errors

    def scalar_property(owner, name, expected_type):
        atom = x.XInternAtom(display, name.encode(), 1)
        assert atom, name
        actual, fmt, count, remaining, data = W(), I(), W(), W(), P(C.c_ubyte)()
        try:
            assert x.XGetWindowProperty(display, owner, atom, 0, 1, 0, expected_type,
                C.byref(actual), C.byref(fmt), C.byref(count), C.byref(remaining), C.byref(data)) == 0
            assert actual.value == expected_type and fmt.value == 32 and count.value == 1 and remaining.value == 0
            return C.cast(data, P(W))[0]
        finally:
            if data: x.XFree(data)

    def geometry(owner):
        attr = Attributes()
        assert x.XGetWindowAttributes(display, owner, C.byref(attr)) and attr.map_state == 2
        px, py, child = I(), I(), W()
        assert x.XTranslateCoordinates(display, owner, native_root, 0, 0, C.byref(px), C.byref(py), C.byref(child))
        sync()
        return [px.value, py.value, attr.width, attr.height]

    def pointer(owner):
        rr, child, rx, ry, wx, wy, mask = W(), W(), I(), I(), I(), I(), U()
        assert x.XQueryPointer(display, owner, C.byref(rr), C.byref(child), C.byref(rx), C.byref(ry),
                              C.byref(wx), C.byref(wy), C.byref(mask))
        sync()
        assert rr.value == native_root
        return [rx.value, ry.value], mask.value, child.value

    def verify(pressed=False, point=None, release_pending=False):
        assert time.monotonic() - started < 160, 'Original app deadline reserve reached'
        assert Path(f'/proc/{pid}/exe').resolve() == stage / 'GallerySliderProbe'
        assert Path(f'/proc/{pid}/stat').read_text().split(') ', 1)[1].split()[19] == process_stat
        assert scalar_property(window, '_NET_WM_PID', 6) == pid
        assert scalar_property(native_root, '_NET_ACTIVE_WINDOW', 33) == window, 'Owned window is not active'
        focus, revert = W(), I()
        x.XGetInputFocus(display, C.byref(focus), C.byref(revert))
        assert focus.value == window, 'Owned window does not hold native focus'
        assert geometry(window) == original_geometry, 'Native client geometry changed'
        keys = (C.c_char * 32)(); x.XQueryKeymap(display, keys); sync()
        assert bytes(keys) == bytes(32), 'A physical key is held'
        actual, mask, child = pointer(native_root)
        assert not (pressed and release_pending)
        allowed_masks = (0, 256) if release_pending else ((256 if pressed else 0),)
        assert mask & ~(2 | 16) in allowed_masks, 'Unexpected held button or modifier'
        if point is not None:
            assert actual == list(point), 'Actual pointer does not match requested native position'
            seen, current = set(), child
            while current and current != window:
                assert current not in seen and len(seen) < 16
                seen.add(current); _, _, current = pointer(current)
            assert current == window, 'Pointer does not target the owned client'
        return {'pointer': actual, 'mask': mask, 'geometry': original_geometry}

    def motion(point, pressed=False):
        verify(pressed=pressed)
        assert t.XTestFakeMotionEvent(display, -1, point[0], point[1], 0)
        sync()
        # This is the gesture's original pacing, within the unchanged controller budget.
        time.sleep(0.05)
        state = verify(pressed=pressed, point=point)
        journal.append({'kind': 'motion', 'point': point, 'seconds': time.monotonic() - started, **state})

    def on_alarm(signum, frame):
        raise TimeoutError('Bounded native input diagnostic exceeded 15 seconds')

    try:
        extension = [I() for _ in range(4)]
        assert t.XTestQueryExtension(display, *(C.byref(v) for v in extension)), 'XTEST unavailable'
        original_geometry = geometry(window)
        root_geometry = geometry(native_root)
        gx, gy, gw, gh = original_geometry
        assert 0 <= gx and 0 <= gy and gx + gw <= root_geometry[2] and gy + gh <= root_geometry[3]
        result.update(nativeGeometry=original_geometry, rootGeometry=root_geometry, xtestVersion=[v.value for v in extension[2:]])
        paths = []
        for index, slider in enumerate(ready['sliders']):
            assert slider['thumb'] is not None and not slider['dragging'] and not slider['captured']
            thumb, bounds = slider['thumb'], slider['slider']
            ax, ay = thumb['topLeft']['x'], thumb['topLeft']['y']
            bx, by = thumb['bottomRight']['x'], thumb['bottomRight']['y']
            start = [round((ax + bx) / 2), round((ay + by) / 2)]
            target = [round((bounds['topLeft']['x'] + bounds['bottomRight']['x']) / 2), start[1]] if index < 3 else [start[0], round((bounds['topLeft']['y'] + bounds['bottomRight']['y']) / 2)]
            assert ax < start[0] < bx and ay < start[1] < by
            points = [[round(start[0] + (target[0] - start[0]) * n / 12), round(start[1] + (target[1] - start[1]) * n / 12)] for n in range(13)]
            assert all(math.isfinite(v) for p in points for v in p)
            assert all(gx + 2 <= px < gx + gw - 2 and gy + 2 <= py < gy + gh - 2 for px, py in points)
            paths.append({'index': index, 'name': slider['name'], 'points': points})
        result['paths'] = paths
        result['initialState'] = verify()
        if args.execute_approved_input:
            # Keep raw captures separate: host capture precedes this invocation and follows it.
            assert (OUT / 'before-input.png').is_file(), 'Original host capture is required'
            result['beforeCaptureSha256'] = digest(OUT / 'before-input.png')
            signal.signal(signal.SIGALRM, on_alarm); signal.alarm(15)
            for path in paths:
                motion(path['points'][0])
                assert t.XTestFakeButtonEvent(display, 1, 1, 0)
                held = True; sync(); verify(pressed=True, point=path['points'][0])
                journal.append({'kind': 'button-down', 'index': path['index'], 'seconds': time.monotonic() - started})
                for point in path['points'][1:]: motion(point, pressed=True)
                assert t.XTestFakeButtonEvent(display, 1, 0, 0)
                sync(); held = False
                release_record = {'index': path['index']}
                result.setdefault('releaseObservations', []).append(release_record)
                observe_released_button(verify, path['points'][-1], release_record)
                journal.append({'kind': 'button-up', 'index': path['index'], 'seconds': time.monotonic() - started})
                time.sleep(0.15)
            signal.alarm(0)
        result['completed'] = True
    except BaseException as error:
        result['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        signal.alarm(0)
        if held:
            # Only release the left button this process actually pressed; never synthesize other edges.
            result['cleanupButtonReleaseRequested'] = bool(t.XTestFakeButtonEvent(display, 1, 0, 0))
            x.XSync(display, 0)
        result['seconds'] = time.monotonic() - started
        result['x11Errors'] = errors
        result['payloadUnchanged'] = all(digest(stage / item['path']) == item['sha256'] for item in manifest)
        destination.write_text(json.dumps(result, indent=2) + '\n')
        x.XCloseDisplay(display)
        print(json.dumps({k: v for k, v in result.items() if k not in ('journal', 'paths')}))


if __name__ == '__main__':
    main()
