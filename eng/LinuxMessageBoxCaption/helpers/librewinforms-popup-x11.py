#!/usr/bin/env python3
"""Real X11/XWayland popup evidence using the unchanged shared fourteen-phase scenario."""

import argparse
from contextlib import contextmanager
import functools
import importlib.metadata
import importlib.util
import json
from pathlib import Path
import signal
import struct
import sys
import tempfile
import time
import uuid


SPEC = importlib.util.spec_from_file_location("popup_desktop", Path(__file__).with_name("librewinforms-popup-desktop.py"))
SHARED = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SHARED)
require = SHARED.require


@contextmanager
def protocol_timeout(seconds):
    """Bound an owned protocol operation, including a stalled server connection."""
    require(seconds > 0, "Original X11 scenario deadline expired")
    started = time.monotonic()
    previous_handler = signal.getsignal(signal.SIGALRM)
    previous_timer = signal.getitimer(signal.ITIMER_REAL)

    def expired(_number, _frame):
        raise TimeoutError("X11 protocol operation exceeded its bounded deadline")

    signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, min(seconds, previous_timer[0]) if previous_timer[0] else seconds)
    try:
        yield
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous_handler)
        if previous_timer[0]:
            signal.setitimer(signal.ITIMER_REAL, max(0.000001, previous_timer[0] - (time.monotonic() - started)), previous_timer[1])


def bounded(method):
    @functools.wraps(method)
    def invoke(self, *args, **kwargs):
        with protocol_timeout(min(3, self.deadline - time.monotonic())):
            return method(self, *args, **kwargs)
    return invoke


def image_layout(screen, info):
    formats = [f for f in info.pixmap_formats if f.depth == screen.root_depth]
    visuals = [v for depth in screen.allowed_depths for v in depth.visuals if v.visual_id == screen.root_visual]
    require(len(formats) == len(visuals) == 1, "Ambiguous root image format/visual")
    pixel, visual = formats[0], visuals[0]
    layout = dict(depth=screen.root_depth, bitsPerPixel=pixel.bits_per_pixel, scanlinePad=pixel.scanline_pad,
                  byteOrder=info.image_byte_order, visual=screen.root_visual, visualClass=visual.visual_class,
                  redMask=visual.red_mask, greenMask=visual.green_mask, blueMask=visual.blue_mask)
    require((layout["depth"], layout["bitsPerPixel"], layout["scanlinePad"], layout["byteOrder"], layout["visualClass"],
             layout["redMask"], layout["greenMask"], layout["blueMask"]) ==
            (24, 32, 32, 0, 4, 0xFF0000, 0xFF00, 0xFF),
            "Unsupported actual root pixel format; no conversion or inferred RGBA is permitted")
    return layout


def write_bmp(destination, width, height, data):
    require(0 < width <= 4096 and 0 < height <= 4096 and width * height <= 4_194_304,
            "Capture exceeds the shared 16 MiB pixel budget")
    require(len(data) == width * height * 4, "Incomplete native image stride/length")
    with destination.open("xb") as stream:
        stream.write(struct.pack("<2sIHHI", b"BM", 54 + len(data), 0, 0, 54))
        stream.write(struct.pack("<IiiHHIIiiII", 40, width, -height, 1, 32, 0, len(data), 0, 0, 0, 0))
        stream.write(data)


class X11Desktop:
    # These are the shared scenario's Windows-independent *semantic* keys.
    KEYS = {0x79: "F10", 0x28: "Down", 0x1B: "Escape", 0x12: "Alt_L", 0x0D: "Return"}

    def __init__(self):
        require(sys.platform.startswith("linux"), "This native X11 driver requires Linux")
        self.dependencies = {}
        for name, expected in (("python-xlib", "0.33"), ("six", "1.17.0")):
            require(importlib.metadata.version(name) == expected, f"Install the exact task-owned dependency: {name}=={expected}")
            self.dependencies[name] = expected
        from Xlib import X, Xatom, XK, display, protocol
        self.X, self.atom_types, self.XK, self.protocol = X, Xatom, XK, protocol
        self.deadline = time.monotonic() + 10
        self.display = None
        try:
            with protocol_timeout(10):
                self.display = display.Display()
                self.screen = self.display.screen()
                self.root = self.screen.root
                self.errors = []
                # This is a Python protocol-connection handler, not XSetErrorHandler
                # or a process-global native Xlib callback.
                self.display.set_error_handler(lambda error, request: self.errors.append(str(error)))
                require(self.display.has_extension("XTEST"), "Actual X11 connection has no XTEST extension")
                version = self.display.xtest_get_version(2, 2)
                require((version.major_version, version.minor_version) >= (2, 2), "XTEST 2.2 is required")
                self.atoms = {name: self.display.intern_atom(name, only_if_exists=True) for name in (
                    "_NET_SUPPORTED", "_NET_SUPPORTING_WM_CHECK", "_NET_ACTIVE_WINDOW", "_NET_WM_PID",
                    "_NET_WM_NAME", "UTF8_STRING", "WM_TRANSIENT_FOR", "_NET_WM_WINDOW_TYPE")}
                require(all(self.atoms.values()), "Required EWMH/ICCCM atoms are unavailable")
                wm = self.scalar(self.root, "_NET_SUPPORTING_WM_CHECK", Xatom.WINDOW)
                require(wm and self.scalar(self.window(wm), "_NET_SUPPORTING_WM_CHECK", Xatom.WINDOW) == wm,
                        "No live EWMH window-manager identity")
                supported = self.property(self.root, "_NET_SUPPORTED", Xatom.ATOM, 32, required=True)
                require(self.atoms["_NET_ACTIVE_WINDOW"] in supported, "Window manager does not support activation requests")
                self.layout = image_layout(self.screen, self.display.display.info)
                self.provenance = dict(transport="X11", serverVendor=self.display.display.info.vendor,
                                       serverRelease=self.display.display.info.release_number,
                                       rootXid=self.root.id, wmXid=wm, xtest=[version.major_version, version.minor_version],
                                       dependencies=self.dependencies, imageFormat=self.layout,
                                       captureProvider="X11 root GetImage", compositorCaptureVerified=False,
                                       usableWindowPixelsVerified=False, nativeWaylandQualified=False, qualified=False)
                self.sync()
        except BaseException:
            try:
                self.close()
            except Exception as cleanup_error:
                print(f"Additional owned X11 cleanup failure: {cleanup_error}", file=sys.stderr)
            raise

    def close(self):
        if self.display is not None:
            connection = self.display
            self.display = None
            try:
                with protocol_timeout(1):
                    connection.close()
            finally:
                # The pinned protocol close flushes first. A broken/stalled
                # connection must still release this exact owned socket.
                connection.display.socket.close()

    def sync(self):
        self.display.sync()
        require(not self.errors, f"X11 protocol error: {self.errors[:4]}")

    def window(self, xid):
        require(isinstance(xid, int) and xid > 1, "Missing actual X11 window identity")
        return self.display.create_resource_object("window", xid)

    def property(self, window, name, kind, bits, required=False):
        value = window.get_property(self.atoms[name], kind, 0, 1024)
        if value is None:
            require(not required, f"Missing required X11 property: {name}")
            return None
        require(value.property_type == kind and value.format == bits and value.bytes_after == 0,
                f"Malformed or over-budget X11 property: {name}")
        return value.value

    def scalar(self, window, name, kind):
        values = self.property(window, name, kind, 32)
        if values is None:
            return None
        require(len(values) == 1, f"Expected a single X11 property value: {name}")
        return int(values[0])

    def pid(self, window, ancestors=False):
        for _ in range(64):
            if not window or window == self.root or isinstance(window, int):
                return None
            pid = self.scalar(window, "_NET_WM_PID", self.atom_types.CARDINAL)
            if pid is not None or not ancestors:
                return pid
            window = window.query_tree().parent
        raise RuntimeError("Native ownership ancestry budget exceeded")

    def outer_bounds(self, window, client, override_redirect):
        # Reparenting frames belong to the WM, not the application PID. Resolve
        # them through the actual client ancestry, never decoration constants.
        outer, seen = window, set()
        for _ in range(64):
            require(outer.id not in seen, "Cycle in native frame ancestry")
            seen.add(outer.id)
            tree = outer.query_tree()
            require(tree.root == self.root, "Native frame is on another X11 screen")
            if tree.parent == self.root:
                break
            require(tree.parent and not isinstance(tree.parent, int), "Missing native frame parent")
            outer = tree.parent
        else:
            raise RuntimeError("Native frame ancestry budget exceeded")
        geometry = outer.get_geometry()
        origin = self.root.translate_coords(outer, 0, 0)
        require(origin.same_screen and geometry.root == self.root and
                outer.get_attributes().map_state == self.X.IsViewable,
                "Native outer frame is not viewable on the admitted screen")
        border = geometry.border_width
        bounds = dict(x=origin.x - border, y=origin.y - border,
                      width=geometry.width + 2 * border, height=geometry.height + 2 * border)
        require(bounds["width"] > 0 and bounds["height"] > 0 and
                bounds["x"] <= client["x"] and bounds["y"] <= client["y"] and
                bounds["x"] + bounds["width"] >= client["x"] + client["width"] and
                bounds["y"] + bounds["height"] >= client["y"] + client["height"],
                "Actual outer frame does not contain its observed client")
        reparented = outer.id != window.id
        return dict(bounds=bounds, outerFrameXid=outer.id,
                    boundsSource="ancestor-frame" if reparented else "owned-x-window",
                    chromeGeometryVerified=reparented or bool(override_redirect))

    @bounded
    def windows(self, pid):
        pending = [(window, 0) for window in self.root.query_tree().children]
        result, seen = [], set()
        while pending:
            window, depth = pending.pop()
            require(window.id not in seen and len(seen) < 4096 and depth < 64, "X11 window-tree budget exceeded")
            seen.add(window.id)
            attrs = window.get_attributes()
            if attrs.map_state != self.X.IsViewable:
                continue
            pending.extend((child, depth + 1) for child in window.query_tree().children)
            if self.pid(window) != pid:
                continue
            geometry = window.get_geometry()
            origin = self.root.translate_coords(window, 0, 0)
            require(origin.same_screen and geometry.root == self.root, "Owned window is not on the admitted X11 screen")
            title = self.property(window, "_NET_WM_NAME", self.atoms["UTF8_STRING"], 8)
            title = bytes(title).decode("utf-8", errors="strict") if title is not None else ""
            owner = self.scalar(window, "WM_TRANSIENT_FOR", self.atom_types.WINDOW)
            if attrs.override_redirect:
                require(owner and self.pid(self.window(owner)) == pid,
                        "Override-redirect popup lacks a live same-process transient owner")
            client = dict(x=origin.x, y=origin.y, width=geometry.width, height=geometry.height)
            result.append(dict(xid=window.id, pid=pid, title=title, client=client,
                               **self.outer_bounds(window, client, attrs.override_redirect),
                               transientFor=owner, overrideRedirect=bool(attrs.override_redirect),
                               windowTypes=list(self.property(window, "_NET_WM_WINDOW_TYPE", self.atom_types.ATOM, 32) or [])))
        return sorted(result, key=lambda item: item["xid"])

    @staticmethod
    def window_identity(window):
        return window["xid"]

    def select_observations(self, observations):
        # X11's native PID/client inventory already includes both real forms.
        # Source files are selected and validated by the shared modal session.
        pass

    def window_input_enabled(self, window, state):
        value = state.get("modal", {}).get("inputEnabled")
        require(type(value) is bool, "Missing exact typed source modal input observation")
        return value  # Source policy; X11 has no Win32 IsWindowEnabled equivalent.

    @bounded
    def foreground(self, pid):
        active = self.scalar(self.root, "_NET_ACTIVE_WINDOW", self.atom_types.WINDOW)
        require(active and self.pid(self.window(active)) == pid, "Foreground belongs to another process; no input permitted")
        require(self.pid(self.display.get_input_focus().focus, ancestors=True) == pid,
                "Actual X11 keyboard focus belongs to another process; no input permitted")

    @bounded
    def activate(self, window):
        target = self.window(window["xid"])
        require(self.pid(target) == window["pid"] and target.get_attributes().map_state == self.X.IsViewable,
                "Owned activation target changed")
        event = self.protocol.event.ClientMessage(window=target, client_type=self.atoms["_NET_ACTIVE_WINDOW"],
                                                  data=(32, [1, self.X.CurrentTime, 0, 0, 0]))
        self.root.send_event(event, event_mask=self.X.SubstructureRedirectMask | self.X.SubstructureNotifyMask)
        self.sync()  # A WM request, never XSetInputFocus or a source Focus call.

    def held(self, requested=0):
        require(list(self.display.get_pointer_mapping())[:3] == [1, 2, 3],
                "Nonstandard physical button mapping is not admitted by this scenario")
        keys = self.display.query_keymap()
        modifiers = {key for group in self.display.get_modifier_mapping() for key in group if key}
        require(not any(keys[key // 8] & (1 << (key % 8)) for key in modifiers | ({requested} if requested else set())),
                "Requested physical key or modifier is held; no injected release is permitted")
        pointer = self.root.query_pointer()
        require(pointer.same_screen and not (pointer.mask & 0x1F00), "Physical pointer button held or pointer is on another screen")
        return pointer

    def point_owner(self, x, y, actual_pointer=False):
        window = self.root
        for _ in range(64):
            position = window.query_pointer() if actual_pointer else window.translate_coords(self.root, x, y)
            require(position.same_screen, "Pointer coordinate is on another screen")
            child = position.child
            if not child:
                return self.pid(window, ancestors=True)
            window = child
        raise RuntimeError("Pointer ownership ancestry budget exceeded")

    def pair(self, down, up, detail):
        queued = False
        try:
            self.display.xtest_fake_input(down, detail)
            queued = True
            self.display.xtest_fake_input(up, detail)
            self.sync()
        except BaseException:
            # Release only our just-queued physical key/button, never modifiers
            # held by the user. Failure remains failure even if cleanup succeeds.
            if queued:
                try:
                    with protocol_timeout(1):
                        self.display.xtest_fake_input(up, detail)
                        self.display.sync()
                except BaseException:
                    pass
            raise

    @bounded
    def key(self, pid, key):
        self.foreground(pid)
        require(key in self.KEYS, "Unsupported shared-scenario key")
        code = self.display.keysym_to_keycode(self.XK.string_to_keysym(self.KEYS[key]))
        require(code > 0, "Required physical keysym has no current server keycode")
        self.held(code)
        self.pair(self.X.KeyPress, self.X.KeyRelease, code)

    @bounded
    def pointer(self, pid, rect, click=None):
        self.foreground(pid)
        self.held()
        require(click in (None, "left", "right"), "Unsupported pointer button")
        require(rect and rect["width"] > 0 and rect["height"] > 0, "Missing observed target geometry")
        x, y = rect["x"] + rect["width"] // 2, rect["y"] + rect["height"] // 2
        require(0 <= x < self.screen.width_in_pixels and 0 <= y < self.screen.height_in_pixels,
                "Observed point is outside the admitted X11 root")
        require(self.point_owner(x, y) == pid, "Observed point is covered by another process")
        self.display.xtest_fake_input(self.X.MotionNotify, root=self.root, x=x, y=y)
        self.sync()
        self.foreground(pid)
        pointer = self.held()
        require((pointer.root_x, pointer.root_y) == (x, y) and self.point_owner(x, y, actual_pointer=True) == pid,
                "Actual pointer target differs from the admitted source point")
        if click:
            self.pair(self.X.ButtonPress, self.X.ButtonRelease, 1 if click == "left" else 3)

    def owner_obstruction_inventory(self, pid, owner):
        fresh = [w for w in self.windows(pid) if w["xid"] == owner["xid"]]
        require(fresh == [owner], "Actual X11 owner identity/client/frame changed")
        expected = dict(id=owner["outerFrameXid"], pid=pid, title=owner["title"], bounds=owner["bounds"], client=owner["client"])
        children = self.root.query_tree().children
        require(len(children) <= 4096, "X11 obstruction inventory budget exceeded")
        result = []
        # XQueryTree orders root children bottom-to-top. Include WM frames,
        # owned popups and foreign windows; no same-PID obstruction exemption.
        for frame in reversed(children):
            if frame.get_attributes().map_state != self.X.IsViewable:
                continue
            geometry = frame.get_geometry(); origin = self.root.translate_coords(frame, 0, 0)
            require(origin.same_screen and geometry.root == self.root, "Obstruction is on an unknown X11 screen")
            border = geometry.border_width
            bounds = dict(x=origin.x - border, y=origin.y - border,
                          width=geometry.width + 2 * border, height=geometry.height + 2 * border)
            if frame.id == expected["id"]:
                require(bounds == owner["bounds"], "Owner frame changed during Z-order observation")
                result.append(expected)
                return expected, result
            result.append(dict(id=frame.id, pid=self.pid(frame) or 0, bounds=bounds))
        raise RuntimeError("Actual X11 owner frame is absent from Z-order")

    @bounded
    def blocked_owner_pointer(self, pid, owner_window, target_rectangle):
        self.foreground(pid); self.held()
        owner, inventory = self.owner_obstruction_inventory(pid, owner_window)
        before = SHARED.exposed_owner_target(pid, owner, target_rectangle, inventory)
        require(len(json.dumps(before).encode()) <= 64 * 1024, "Owner obstruction receipt exceeds budget")
        x, y = before["point"]
        require(type(x) is int and type(y) is int and 0 <= x < self.screen.width_in_pixels and 0 <= y < self.screen.height_in_pixels,
                "Owner point is outside admitted physical X11 coordinates")
        require(self.point_owner(x, y) == pid, "Owner point hit belongs to another process")
        self.display.xtest_fake_input(self.X.MotionNotify, root=self.root, x=x, y=y)
        self.sync(); self.foreground(pid)
        pointer = self.held()
        require((pointer.root_x, pointer.root_y) == (x, y) and self.point_owner(x, y, actual_pointer=True) == pid,
                "Actual exposed owner pointer changed")
        owner, inventory = self.owner_obstruction_inventory(pid, owner_window)
        after = SHARED.exposed_owner_target(pid, owner, target_rectangle, inventory)
        require(len(json.dumps(after).encode()) <= 64 * 1024, "Owner obstruction receipt exceeds budget")
        self.pair(self.X.ButtonPress, self.X.ButtonRelease, 1)
        return dict(policy="exposed-owner-native-z-order-v1", before=before, beforeClick=after, qualified=False)

    @bounded
    def screenshot(self, pid, windows, destination):
        self.foreground(pid)
        require(windows and all(w["pid"] == pid for w in windows), "Capture requires actual owned windows")
        rectangles = [w["bounds"] for w in windows]
        x, y = min(r["x"] for r in rectangles), min(r["y"] for r in rectangles)
        width = max(r["x"] + r["width"] for r in rectangles) - x
        height = max(r["y"] + r["height"] for r in rectangles) - y
        require(0 <= x and 0 <= y and x + width <= self.screen.width_in_pixels and y + height <= self.screen.height_in_pixels,
                "Owned capture rectangle extends outside the actual X11 root")
        require(0 < width <= 4096 and 0 < height <= 4096 and width * height <= 4_194_304, "Capture exceeds shared pixel budget")
        capture = self.root.get_image(x, y, width, height, self.X.ZPixmap, 0xFFFFFFFF)
        require(capture.depth == self.layout["depth"] and capture.visual == self.layout["visual"], "Native capture format changed")
        self.foreground(pid)
        write_bmp(destination, width, height, capture.data)
        return dict(x=x, y=y, width=width, height=height, stride=width * 4,
                    format=self.layout, captureProvider="X11 root GetImage", compositorCaptureVerified=False,
                    fullChromeGeometryVerified=all(w["chromeGeometryVerified"] for w in windows),
                    usableWindowPixelsVerified=False, sha256=SHARED.digest(destination))


def check_preparation(prepared, portable):
    manifest = json.loads((prepared / "preparation.json").read_text())
    require(manifest.get("schema") == "popup-interaction-preparation-v1", "Unknown source preparation receipt")
    require(manifest["sourceSha256"] == SHARED.digest(Path(__file__).parent / "PopupInteractionApp/Program.cs") ==
            SHARED.digest(prepared / "Portable/Program.cs"), "Prepared source differs from the shared scenario")
    require(manifest["startupSourceSha256"] == SHARED.digest(Path(__file__).parent / "PopupInteractionApp/PopupInteractionStartup.cs") ==
            SHARED.digest(prepared / "Portable/PopupInteractionStartup.cs"), "Prepared startup differs from the shared scenario")
    expected = (prepared / "Portable/bin/Release/net11.0/PopupInteractionApp").resolve(strict=True)
    require(portable == expected, "Executable is not the explicit prepared Linux consumer output")
    with portable.open("rb") as stream:
        require(stream.read(4) == b"\x7fELF", "The native Linux apphost must be ELF")
    return manifest


def cancel(signum, _frame):
    # Route launcher cancellation through shared run_case's owned-child finally.
    raise SystemExit(128 + signum)


def main():
    signal.signal(signal.SIGTERM, cancel)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portable-app", type=Path, required=True)
    parser.add_argument("--prepared-root", type=Path, required=True)
    parser.add_argument("--evidence-parent", type=Path, required=True)
    parser.add_argument("--modal", action="store_true", help="Separate real ShowDialog scenario; no Cocoa startup override")
    args = parser.parse_args()
    app = args.portable_app.resolve(strict=True)
    require(args.evidence_parent.is_dir(), "Evidence parent must already exist")
    preparation = check_preparation(args.prepared_root.resolve(strict=True), app)
    root = Path(tempfile.mkdtemp(prefix="popup-x11-interaction-", dir=args.evidence_parent.resolve()))
    desktop = None
    launch_attempted = False
    original_error = None
    try:
        with (root / "preparation.json").open("x") as stream:
            json.dump(preparation, stream, indent=2)
        print(f"Unqualified real X11 evidence: {root}", flush=True)
        desktop = X11Desktop()  # All dependency/protocol preflight occurs before launch.
        with (root / "x11.json").open("x") as stream:
            json.dump(desktop.provenance, stream, indent=2)
        desktop.deadline = time.monotonic() + 60
        launch_attempted = True
        if args.modal:
            complete = SHARED.run_case(desktop, app, root, "portable", uuid.uuid4().hex, modal=True)
        else:
            complete = SHARED.run_case(desktop, app, root, "portable", uuid.uuid4().hex)
        print("Raw phases captured; independent Windows comparison remains required." if complete else "Incomplete evidence; inspect retained receipt.")
        return 0 if complete else 1
    except BaseException as error:
        original_error = error
        with (root / "driver-failure.json").open("x") as stream:
            json.dump(dict(error=f"{type(error).__name__}: {error}"[:2048], qualified=False,
                           appNotLaunched=not launch_attempted,
                           status="driver-failed" if launch_attempted else "preflight-failed"), stream, indent=2)
        raise
    finally:
        try:
            if desktop is not None:
                desktop.close()
        except Exception as cleanup_error:
            if root is not None:
                with (root / "connection-cleanup-failure.json").open("x") as stream:
                    json.dump(dict(error=str(cleanup_error), qualified=False), stream, indent=2)
            if original_error is None:
                raise


if __name__ == "__main__":
    raise SystemExit(main())
