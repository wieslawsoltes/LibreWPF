#!/usr/bin/env python3
"""Actual WPF desktop input evidence; authored controls never imply qualification."""

import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import uuid


LIMIT = 256 * 1024


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def regular(path, directory=False):
    value = path.lstat()
    require(not stat.S_ISLNK(value.st_mode) and not (getattr(value, "st_file_attributes", 0) & 0x400),
            "Evidence/helpers cannot be links or reparse points")
    require(stat.S_ISDIR(value.st_mode) if directory else stat.S_ISREG(value.st_mode), "Unexpected path type")
    require(path.is_absolute() and path.resolve(strict=True) == path, "Expected exact canonical absolute path")
    return value


def parse(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "Duplicate observation field")
            result[key] = value
        return result
    def reject(value):
        raise RuntimeError("Nonfinite observation: " + value)
    require(len(raw) <= LIMIT, "Observation exceeds 256 KiB")
    return json.loads(raw, object_pairs_hook=unique, parse_constant=reject)


def write(path, value):
    raw = json.dumps(value, indent=2, allow_nan=False).encode()
    require(len(raw) <= LIMIT, "Receipt exceeds 256 KiB")
    with path.open("xb") as stream:
        stream.write(raw)


def rectangle(value):
    require(isinstance(value, dict) and set(value) == {"x", "y", "width", "height"}, "Invalid exact rectangle")
    require(all(type(item) in (int, float) and math.isfinite(item) for item in value.values()) and
            value["width"] > 0 and value["height"] > 0 and
            all(abs(item) <= 1_000_000 for item in value.values()), "Invalid rectangle extent")
    return value


def contains(outer, inner):
    rectangle(outer)
    rectangle(inner)
    return (outer["x"] <= inner["x"] and outer["y"] <= inner["y"] and
            outer["x"] + outer["width"] >= inner["x"] + inner["width"] and
            outer["y"] + outer["height"] >= inner["y"] + inner["height"])


def overlaps(a, b):
    return max(a["x"], b["x"]) < min(a["x"] + a["width"], b["x"] + b["width"]) and \
        max(a["y"], b["y"]) < min(a["y"] + a["height"], b["y"] + b["height"])


def observation(directory, pid, run):
    regular(directory, True)
    paths = list(directory.glob("snapshot-????????.json"))
    require(len(paths) <= 650, "Source observation budget exceeded")
    if not paths:
        raise FileNotFoundError("Source has not published a snapshot")
    path = max(paths)
    require(re.fullmatch(r"snapshot-[0-9]{8}\.json", path.name) is not None and regular(path).st_size <= LIMIT,
            "Invalid source snapshot name/size")
    raw = path.read_bytes()
    state = parse(raw)
    require(state.get("schema") == "wpf-modal-desktop-v2" and state.get("qualified") is False and
            type(state.get("pid")) is int and state["pid"] == pid and state.get("run") == run and
            state.get("title") == f"WpfModalInteractionApp [{run}]" and
            type(state.get("sequence")) is int and 0 < state["sequence"] <= 650 and
            path.name == f"snapshot-{state['sequence']:08d}.json", "Source identity/sequence mismatch")
    regular(path)
    return state, dict(path=str(path), sha256=hashlib.sha256(raw).hexdigest())


def stable(state):
    result = {key: value for key, value in state.items() if key not in ("sequence", "elapsedMs")}
    # Presentation continues normally. A newer frame is not a geometry change.
    result["windows"] = [{**window, "surfaces": [{key: value for key, value in surface.items()
        if key != "presentedFrames"} for surface in window["surfaces"]]} for window in state["windows"]]
    return result


def owner_input(state):
    return dict(text=state["ownerText"], selection=state["comboSelection"],
                counts={key: value for key, value in state["counts"].items()
                        if key.startswith(("owner-pointer", "owner-key", "guard-", "editor-", "message-", "dialog-open-request"))})


def presented(state):
    ready = True
    for window in state["windows"]:
        for surface in window["surfaces"]:
            frames = surface["presentedFrames"]
            require(type(frames) is int and frames >= 0, "Invalid actual presented host frame count")
            ready = ready and frames > 0
    return ready


def exposed_guard(owner, windows, bounds, identity):
    rectangle(bounds)
    require(contains(owner["client"], bounds) and all(not overlaps(bounds, item["bounds"])
            for item in windows if identity(item) != identity(owner)), "Owner guard is covered by an owned window")


def observed_front_to_owner(owner, pid, record):
    rows = record["frontToOwner"]
    require(isinstance(rows, list) and 0 < len(rows) <= 4096 and
            len({row["id"] for row in rows}) == len(rows), "Invalid original native Z-order inventory")
    last = rows[-1]
    require(last["id"] == owner["hwnd"] and last["pid"] == pid and
            all(last.get(key) == owner[key] for key in ("title", "bounds", "client")),
            "Native owner changed during Z-order observation")
    return [dict(row, hwnd=row["id"]) for row in rows]


def initial_windows(owner, pid, record):
    rows = record["windows"]
    require(0 < len(rows) <= 32 and len({row["hwnd"] for row in rows}) == len(rows),
            "Invalid initial native inventory")
    require(sum(row["hwnd"] == owner["hwnd"] for row in rows) == 1, "Initial native owner is missing")
    for row in rows:
        require(row["pid"] == pid, "Foreign initial native identity")
        if row["hwnd"] != owner["hwnd"]:
            # Original Windows observations identify these as transparent system
            # shadow surfaces. Preserve each complete record, never discard it.
            require(row["windowClass"] == "SysShadow" and row["title"] == "" and
                    row["owner"] == 0 and row["style"] == 0x94000000 and
                    row["extendedStyle"] == 0x800a0, "Unclassified initial native window")
    front = observed_front_to_owner(owner, pid, record)
    require(not any(row["hwnd"] in {item["hwnd"] for item in front[:-1]} for row in rows if row["hwnd"] != owner["hwnd"]),
            "Initial shadow is not behind its original owner")
    return sorted(rows, key=lambda row: row["hwnd"])


def unique_options(argv):
    seen = set()
    for value in argv:
        if value.startswith("--"):
            option = value.split("=", 1)[0]
            require(option not in seen, "Duplicate desktop option: " + option)
            seen.add(option)


def load_helper(checkout, commit):
    regular(checkout, True)
    require(re.fullmatch(r"[0-9a-f]{40}", commit) is not None, "An immutable Forms helper commit is required")
    actual = subprocess.run(["git", "-C", str(checkout), "rev-parse", "HEAD"], check=True,
                            stdout=subprocess.PIPE, timeout=10).stdout.decode().strip()
    require(actual == commit, "Helper checkout is not the exact requested commit")
    paths = sorted((checkout / "eng").glob("librewinforms-popup-*.py")) + [checkout / "eng/PopupDesktopNative.swift"]
    require(4 <= len(paths) <= 16, "Unexpected shared helper inventory")
    hashes = {}
    for path in paths:
        require(regular(path).st_size <= 512 * 1024, "Helper source budget exceeded")
        original = subprocess.run(["git", "-C", str(checkout), "show", f"{commit}:{path.relative_to(checkout).as_posix()}"],
                                  check=True, stdout=subprocess.PIPE, timeout=10).stdout
        require(path.read_bytes() == original, "Mutable/uncommitted helper source is not admitted")
        hashes[path.name] = digest(path)
    def module(name):
        path = checkout / "eng" / (name + ".py")
        require(path.name in hashes, "Missing immutable helper module")
        spec = importlib.util.spec_from_file_location(name.replace("-", "_"), path)
        result = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(result)
        return result
    return module, dict(commit=commit, checkout=str(checkout), sources=hashes)


class Session:
    def __init__(self, desktop, process, directory, run, portable, deadline, hosted_caption=False, hosted_shell=False, retirement_readiness=False, hosted_start=False):
        self.desktop, self.process, self.directory, self.run = desktop, process, directory, run
        self.portable = portable
        self.deadline = deadline
        self.state = self.source_receipt = None
        self.phase = "startup"
        self.image_bytes = 0
        self.actions = 0
        self.owner_identity = None
        self.blocked_owner_baseline = None
        self.hosted_caption = hosted_caption
        self.hosted_shell = hosted_shell
        self.hosted_start = hosted_start
        self.retirement_readiness = retirement_readiness
        self.activation_attempted = False
        self.initial_native_windows = None

    def activate(self, owner):
        if not self.hosted_caption:
            self.desktop.activate(owner)
            return
        self.remaining()
        # Only initial owner activation changes. Every original foreground,
        # obstruction, source-identity and scenario assertion remains in force.
        require(self.phase == "startup" and self.actions == 0 and not self.activation_attempted,
                "Hosted caption activation is startup-only and may not be retried")
        require(self.identity(owner) == self.owner_identity, "Activation owner identity changed")
        self.activation_attempted = True
        if self.hosted_shell:
            self.dismiss_hosted_shell()
        path = Path(__file__).parent / "WindowsGalleryClipboardConsumer/run-windows.py"
        spec = importlib.util.spec_from_file_location("modal_hosted_caption", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        record = dict(qualified=False, helperSha256=digest(path))
        try:
            module.Desktop(adopt_physical_coordinates=True).activate_ci_window(
                owner["hwnd"], self.process.pid, self.deadline, record, os.environ)
        except BaseException as error:
            record["error"] = str(error)[:4096]
            raise
        finally:
            write(self.directory / "startup-activation.json", record)
        if record["clickCount"]:
            self.action("hosted-caption", nativeProof=record)

    def dismiss_hosted_shell(self):
        require(not self.portable and self.phase == "startup" and self.actions == 0,
                "Shell Escape is restricted to the first reference startup")
        record = dict(qualified=False, sourceBefore=owner_input(self.state), helpers={})
        try:
            modules = []
            for name in ("prepare-desktop", "shell-escape"):
                path = Path(__file__).parent / "WindowsModalDesktop" / (name + ".py")
                record["helpers"][name] = digest(path)
                spec = importlib.util.spec_from_file_location(name.replace("-", "_"), path)
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                modules.append(module)
            modules[1].dismiss(modules[0].Desktop(), self.desktop, self.action,
                               self.deadline, record, os.environ, authorized=self.hosted_shell,
                               allow_background_start=self.hosted_start)
            if record["escapes"]:
                # Require original source observations published after input;
                # already queued snapshots cannot prove absence of leaked keys.
                latest, _ = observation(self.directory / "app", self.process.pid, self.run)
                first = latest["sequence"]
                while True:
                    self.remaining()
                    require(time.monotonic() < record["setupDeadline"], "Bounded shell source verification expired")
                    state, receipt = observation(self.directory / "app", self.process.pid, self.run)
                    require(owner_input(state) == record["sourceBefore"],
                            "Hosted shell Escape leaked input to the original owner")
                    if (state["phase"] == "owner" and latest["sequence"] > first
                            and state["sequence"] > latest["sequence"] and stable(state) == stable(latest)):
                        self.match(state, self.native())
                        self.state, self.source_receipt = state, receipt
                        break
                    latest = state
                    time.sleep(.025)
                record["sourceAfter"] = owner_input(self.state)
                require(record["sourceBefore"] == record["sourceAfter"],
                        "Hosted shell Escape leaked input to the original owner")
        except BaseException as error:
            record["error"] = str(error)[:4096]
            raise
        finally:
            write(self.directory / "startup-shell.json", record)

    def remaining(self):
        value = self.deadline - time.monotonic()
        require(value > 0, "Original 60-second desktop deadline expired")
        require(self.process.poll() is None, "Owned application exited before the scenario completed")
        for name in ("stdout.txt", "stderr.txt"):
            require((self.directory / name).stat().st_size <= 8 * 1024 * 1024, "Application log budget exceeded")
        return value

    def record_failure(self, result):
        # The scenario has already failed. Preserve its error and natural child
        # status before cleanup can replace the latter with a termination code.
        # This never sends input, retries a scenario, or changes its deadline.
        try:
            result["exitCodeAtFailure"] = self.process.poll()
            result["physicalActionsAtFailure"] = self.actions
            if self.hosted_caption:
                path = Path(__file__).parent / "WindowsModalDesktop/native-window-observation.py"
                spec = importlib.util.spec_from_file_location("modal_failure_foreground", path)
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                result["foregroundAfterFailure"] = module.foreground(self.desktop)
                result["failureObserverSha256"] = digest(path)
        except BaseException as error:
            result["failureObservationError"] = str(error)[:4096]

    def native(self):
        windows = self.desktop.windows(self.process.pid)
        require(0 < len(windows) <= 32, "Owned native window inventory unavailable/over budget")
        for value in windows:
            rectangle(value["bounds"])
            if value.get("client") is not None:
                rectangle(value["client"])
        return windows

    def identity(self, window):
        return self.desktop.window_identity(window)

    def match(self, state, windows):
        owners = [window for window in windows if window["title"] == state["title"]]
        require(len(owners) == 1, "Expected one exact PID/title owner")
        owner = owners[0]
        if self.owner_identity is None:
            self.owner_identity = self.identity(owner)
        require(self.owner_identity == self.identity(owner), "Owner native identity was replaced")
        source_handles, native_handles = set(), set()
        for source in state["windows"]:
            for index, surface in enumerate(source["surfaces"]):
                require(surface["visible"] is True, "Source published a nonvisible native surface")
                handle = surface["nativeHandle"]
                source_handle = surface["sourceHandle"]
                require(type(handle) is int and handle != 0 and type(source_handle) is int and source_handle != 0 and
                        handle not in native_handles and source_handle not in source_handles, "Aliased source/native identity")
                native_handles.add(handle)
                source_handles.add(source_handle)
                matches = [value for value in windows if self.identity(value) == handle]
                if sys.platform == "darwin":
                    geometry = surface["nativeGeometry"]
                    require(isinstance(geometry, dict) and geometry["contentView"] != 0, "Missing actual Cocoa view geometry")
                    matches = [value for value in windows if self.identity(value) == geometry["windowNumber"]]
                frame_matches = len(matches) == 1 and matches[0]["client"] == rectangle(surface["client"])
                if not frame_matches:
                    write(self.directory / "source-native-mismatch.json", dict(qualified=False,
                          source=state, sourceReceipt=self.source_receipt, nativeWindows=windows,
                          mismatchedSurface=surface, matchedNativeWindows=matches))
                require(frame_matches, "Original source/native client frames differ; no fitted bounds are permitted")
                if index == 0:
                    require(matches[0]["title"] == source["title"], "Source/native title identity differs")
                if self.portable:
                    require(type(surface["presentedFrames"]) is int and surface["presentedFrames"] > 0,
                            "Visible source lacks an actual presented host frame")
        if self.blocked_owner_baseline is not None and state["phase"] in ("message", "dialog"):
            require(owner_input(state) == self.blocked_owner_baseline, "Modal owner accepted physical input")
        return owner

    def wait(self, predicate):
        previous = None
        while self.remaining() > 0:
            try:
                state, receipt = observation(self.directory / "app", self.process.pid, self.run)
            except FileNotFoundError:
                time.sleep(0.05)
                continue
            # Native visibility/layout may precede the first presentation. Both
            # stable observations must carry actual frames before capture/input;
            # waiting consumes the same original absolute process deadline.
            ready = predicate(state) and (not self.portable or presented(state))
            if ready and previous is not None and state["sequence"] > previous["sequence"] and stable(state) == stable(previous):
                self.state, self.source_receipt = state, receipt
                windows = self.native()
                self.match(state, windows)
                return windows
            previous = state if ready else None
            time.sleep(0.05)

    def action(self, kind, **detail):
        self.remaining()
        self.actions += 1
        require(self.actions <= 64, "Physical input budget exceeded")
        write(self.directory / f"input-{self.actions:03d}.json", dict(kind=kind, qualified=False,
              pid=self.process.pid, phase=self.phase, observation=self.source_receipt, **detail))

    def point(self, target, click="left"):
        self.remaining()
        windows = self.native()
        self.match(self.state, windows)
        bounds = rectangle(self.state["targets"][target])
        require(any(value.get("client") is not None and contains(value["client"], bounds) for value in windows),
                "Input target has no exact native client owner")
        self.desktop.pointer(self.process.pid, bounds, click)
        native_cursor = None
        if self.hosted_caption:
            path = Path(__file__).parent / "WindowsModalDesktop/native-window-observation.py"
            spec = importlib.util.spec_from_file_location("modal_cursor_observation", path)
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            native_cursor = module.pointer()
        self.action("pointer", target=target, rectangle=bounds, click=click, nativeCursorAfter=native_cursor)
        if native_cursor is not None:
            require(native_cursor == dict(x=bounds["x"] + bounds["width"] // 2,
                                          y=bounds["y"] + bounds["height"] // 2),
                    "Actual native cursor differs from the requested source target")

    def key(self, code):
        self.remaining()
        self.desktop.key(self.process.pid, code)
        self.action("key", code=code)

    def blocked_owner_attempt(self, modal_title):
        windows = self.native()
        owner = self.match(self.state, windows)
        children = [value for value in windows if value["title"] == modal_title]
        require(len(children) == 1 and self.identity(children[0]) != self.identity(owner), "Missing exact modal child identity")
        bounds = rectangle(self.state["targets"]["guard"])
        obstruction_windows = windows
        if self.hosted_caption:
            record = self.observe_native_windows(owner, windows)
            record.update(source=self.source_receipt, target=bounds)
            write(self.directory / (self.state["phase"] + "-native-inventory.json"), record)
            obstruction_windows = observed_front_to_owner(owner, self.process.pid, record)
        exposed_guard(owner, obstruction_windows, bounds, self.identity)
        source_owner = next(window for window in self.state["windows"] if window["title"] == self.state["title"])
        policy = source_owner["surfaces"][0]["inputEnabled"] if self.portable else owner.get("enabled")
        require(policy is False, "Modal owner input policy did not disable")
        if sys.platform == "win32":
            require(owner.get("enabled") is False, "Native modal owner remains enabled")
        self.blocked_owner_baseline = owner_input(self.state)
        require(hasattr(self.desktop, "blocked_owner_pointer"), "Shared native helper lacks independently unobstructed blocked-owner input")
        proof = self.desktop.blocked_owner_pointer(self.process.pid, owner, bounds)
        require(isinstance(proof, dict) and proof.get("qualified") is False, "Missing bounded native input evidence")
        self.action("blocked-owner-pointer", nativeProof=proof, target=bounds,
                    ownerIdentity=self.identity(owner), childIdentity=self.identity(children[0]))
        self.wait(lambda state: state["phase"] == self.state["phase"])

    def observe_native_windows(self, owner, windows):
        self.remaining()
        path = Path(__file__).parent / "WindowsModalDesktop/native-window-observation.py"
        spec = importlib.util.spec_from_file_location("modal_native_observation", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        record = module.observe(self.desktop, self.process.pid, owner, windows)
        record["helperSha256"] = digest(path)
        require(self.native() == windows, "Native inventory changed during role observation")
        self.remaining()
        return record

    def capture(self, phase, predicate):
        self.phase = phase
        windows = self.wait(predicate)
        self.desktop.foreground(self.process.pid)
        require(self.image_bytes + 16 * 1024 * 1024 + 54 <= 128 * 1024 * 1024, "Screenshot budget exceeded")
        path = self.directory / (phase + ".bmp")
        image = self.desktop.screenshot(self.process.pid, windows, path)
        self.image_bytes += regular(path).st_size
        require(self.image_bytes <= 128 * 1024 * 1024, "Screenshot budget exceeded")
        self.remaining()
        require(self.native() == windows, "Native identities changed during capture")
        state, receipt = observation(self.directory / "app", self.process.pid, self.run)
        unchanged = stable(state) == stable(self.state)
        if not unchanged:
            self.record_capture_change(state, receipt, windows, image)
        require(unchanged, "Source state changed during capture")
        write(self.directory / (phase + ".json"), dict(qualified=False, pid=self.process.pid,
              source=self.state, sourceReceipt=self.source_receipt, nativeWindows=windows, image=image))
        if phase == "01-owner" and self.hosted_caption:
            require(self.initial_native_windows is None and len(self.state["windows"]) == 1 and
                    len(self.state["windows"][0]["surfaces"]) == 1, "Initial source inventory is not one owner")
            owner = self.match(self.state, windows)
            record = self.observe_native_windows(owner, windows)
            self.initial_native_windows = initial_windows(owner, self.process.pid, record)
            write(self.directory / "initial-native-inventory.json", record)

    def record_capture_change(self, state, receipt, windows, image):
        # Freeze the original failed comparison before observing anything else.
        # No new input, settling, recapture or reinterpretation can pass it.
        record = dict(qualified=False, originalSourceStateUnchanged=False,
                      sourceBefore=self.state, sourceBeforeReceipt=self.source_receipt,
                      sourceAfter=state, sourceAfterReceipt=receipt,
                      nativeWindows=windows, image=image)
        try:
            self.remaining()
            if self.hosted_caption:
                path = Path(__file__).parent / "WindowsModalDesktop/native-window-observation.py"
                spec = importlib.util.spec_from_file_location("modal_failed_capture_observation", path)
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                record["foregroundAfterFailure"] = module.foreground(self.desktop)
                record["helperSha256"] = digest(path)
            self.remaining()
        except BaseException as error:
            record["diagnosticError"] = str(error)[:4096]
        try:
            write(self.directory / (self.phase + "-capture-failure.json"), record)
        except BaseException:
            # A secondary diagnostic failure cannot replace the original error.
            pass


def await_exact_retirement(read_inventory, expected, remaining, clock=None, sleep=None):
    """Observe up to one second, inside the original process deadline, without input."""
    clock = clock or time.monotonic
    sleep = sleep or time.sleep
    started = clock()
    observations = []
    stable = 0
    while True:
        budget = remaining()  # The original absolute deadline stays authoritative.
        if budget <= 0:
            raise TimeoutError('Original process deadline expired')
        elapsed = clock() - started
        if observations and elapsed >= 1:
            return dict(ready=False, observations=observations)
        current = read_inventory()
        remaining()
        if clock() - started >= 1:
            observations.append(dict(elapsedSeconds=clock() - started, inventory=current))
            return dict(ready=False, observations=observations)
        observations.append(dict(elapsedSeconds=elapsed, inventory=current))
        stable = stable + 1 if current == expected else 0
        if stable == 2:
            return dict(ready=True, observations=observations)
        wait = min(.05, budget, 1 - (clock() - started))
        if wait <= 0:
            return dict(ready=False, observations=observations)
        sleep(wait)


def check_native_retirement(session):
    windows = session.native()
    single_window = len(windows) == 1
    passed = single_window
    record = dict(qualified=False, originalSingleWindowAssertionPassed=single_window,
                  source=session.state, sourceReceipt=session.source_receipt,
                  nativeWindows=windows, observations=[])
    if session.hosted_caption and session.initial_native_windows is not None:
        owner = session.match(session.state, windows)
        current = session.observe_native_windows(owner, windows)
        record.update(initialNativeWindows=session.initial_native_windows, finalNativeInventory=current)
        passed = sorted(current["windows"], key=lambda row: row["hwnd"]) == session.initial_native_windows
    record["originalAssertionPassed"] = single_window
    record["restorationAssertionPassed"] = passed
    if getattr(session, "retirement_readiness", False) is True:
        write(session.directory / "native-retirement-initial.json", record)
        require(session.hosted_caption and session.initial_native_windows is not None,
                "Retirement readiness requires the original hosted native baseline")
        # The user-approved readiness rule preserves both instantaneous results.
        # It uses only exact native observations inside the original deadline.
        expected = session.initial_native_windows
        baseline_owner = next(row for row in expected if row["hwnd"] == owner["hwnd"])
        def inventory():
            native = session.native()
            current_owner = session.match(session.state, native)
            observed = session.observe_native_windows(current_owner, native)
            rows = sorted(observed["windows"], key=lambda row: row["hwnd"])
            require(sum(row["hwnd"] == baseline_owner["hwnd"] for row in rows) == 1 and
                    next(row for row in rows if row["hwnd"] == baseline_owner["hwnd"]) == baseline_owner,
                    "Original native owner identity changed during retirement readiness")
            return rows
        try:
            record["boundedReadiness"] = await_exact_retirement(inventory, expected, session.remaining)
            passed = record["boundedReadiness"]["ready"]
        except BaseException as error:
            record["readinessError"] = str(error)[:4096]
            write(session.directory / "native-retirement.json", record)
            raise
    elif not passed and session.hosted_caption:
        # Observe only after freezing the original failed assertion. Later
        # disappearance can diagnose deferred retirement, never turn it green.
        until = min(session.deadline, time.monotonic() + 1)
        try:
            while time.monotonic() < until:
                session.remaining()
                record["observations"].append(dict(at=time.monotonic(), nativeWindows=session.native()))
                time.sleep(.05)
        except BaseException as error:
            record["diagnosticError"] = str(error)[:4096]
    write(session.directory / "native-retirement.json", record)
    require(passed, "Dismissed popup or modal native windows remain visible")


def scenario(session):
    count = lambda state, key: state["counts"].get(key, 0)
    windows = session.wait(lambda state: state["phase"] == "owner" and bool(state["windows"]))
    session.activate(next(value for value in windows if value["title"] == session.state["title"]))
    session.capture("01-owner", lambda state: state["windows"][0]["active"])
    for action, suffix in (("message", " MessageBox"), ("dialog", " Dialog")):
        session.point(action)
        session.wait(lambda state: state["phase"] == action and count(state, action + "-open-request") == 1)
        session.blocked_owner_attempt(session.state["title"] + suffix)
        session.capture("02-message" if action == "message" else "03-dialog", lambda state: state["phase"] == action)
        if action == "message":
            session.key(0x0D)
        else:
            session.point("dialog-editor")
            session.wait(lambda state: state["focus"] == "DialogEditor")
            session.key(0x28)
            session.point("dialog-accept")
        session.wait(lambda state: state["phase"] == "owner" and count(state, action + "-returned") == 1 and
                     state["focus"] == state["priorFocus"])
        session.blocked_owner_baseline = None
        if action == "message":
            require(session.state["messageResult"] == "OK", "Original MessageBox result differs")
        else:
            require(session.state["dialogResult"] is True and count(session.state, "dialog-closing") == 1 and
                    count(session.state, "dialog-closed") == 1, "ShowDialog close/return count differs")
        owner = session.match(session.state, session.native())
        native_enabled = owner.get("enabled")
        source_owner = next(window for window in session.state["windows"] if window["title"] == session.state["title"])
        require(source_owner["surfaces"][0]["inputEnabled"] is True if session.portable else native_enabled is True,
                "Modal owner input was not restored")
        if sys.platform == "win32":
            require(native_enabled is True, "Native modal owner input was not restored")
        before = count(session.state, "guard-action")
        session.point("guard")
        session.wait(lambda state: count(state, "guard-action") == before + 1)
    session.point("popup")
    session.capture("04-popup", lambda state: state["popupOpen"] and count(state, "popup-opened") == 1)
    session.point("guard")
    session.wait(lambda state: not state["popupOpen"] and count(state, "popup-closed") == 1)
    session.point("editor", "right")
    session.capture("05-context", lambda state: state["contextOpen"] and count(state, "context-opened") == 1)
    session.key(0x1B)
    session.wait(lambda state: not state["contextOpen"] and count(state, "context-closed") == 1)
    session.point("combo")
    session.wait(lambda state: state["comboOpen"])
    session.key(0x28)
    session.key(0x0D)
    session.capture("06-combo", lambda state: not state["comboOpen"] and state["comboSelection"] == 1)
    session.point("tooltip", None)
    session.capture("07-tooltip", lambda state: state["tooltipOpen"] and count(state, "tooltip-opening") == 1)
    session.point("guard", None)
    session.wait(lambda state: not state["tooltipOpen"] and not state["popupOpen"] and not state["contextOpen"])
    check_native_retirement(session)
    session.point("finish")
    remaining = max(0.001, session.deadline - time.monotonic())
    require(session.process.wait(timeout=remaining) == 0, "Actual owner shutdown failed")


def make_desktop(platform, module, helper, output, read_state):
    if platform == "windows":
        return module("librewinforms-popup-desktop").WindowsDesktop()
    if platform == "x11":
        return module("librewinforms-popup-x11").X11Desktop()
    mac = module("librewinforms-popup-macos")
    class WpfMacDesktop(mac.MacDesktop):
        def windows(self, pid):
            inventory = self.call("inventory", pid=pid)["windows"]
            state = read_state()
            mapped = {}
            native_ids, views = set(), set()
            for window in state["windows"]:
                for surface in window["surfaces"]:
                    geometry = surface["nativeGeometry"]
                    require(surface["nativeKind"] == "Cocoa" and isinstance(geometry, dict), "Missing typed Cocoa source geometry")
                    number, native, view = geometry["windowNumber"], surface["nativeHandle"], geometry["contentView"]
                    require(number not in mapped and native not in native_ids and view not in views and view != 0,
                            "Aliased Cocoa window/view identity")
                    native_ids.add(native)
                    views.add(view)
                    require(surface["client"] == geometry["content"], "Source/native point frame differs; no inferred scaling")
                    mapped[number] = (surface, geometry)
            result = []
            for item in inventory:
                require(item["pid"] == pid, "Foreign native inventory entry")
                pair = mapped.get(item["windowNumber"])
                require(pair is not None and item["frameBounds"] == pair[1]["frame"], "Unmatched native/source Cocoa frame")
                result.append(dict(pid=pid, windowNumber=item["windowNumber"], title=item["title"], bounds=item["frameBounds"],
                    client=pair[1]["content"], nativeClient=pair[1]["content"], nativeToSourceScale=1,
                    sourceName="main" if item["title"] else "popup", clientGeometryVerified=True))
            require(len(result) == len(mapped), "Missing independent CG window identity")
            return sorted(result, key=lambda item: item["windowNumber"])
    require(helper is not None, "macOS requires the explicitly built native input/capture helper")
    return WpfMacDesktop(helper, output)


def run_one(args, module, shared, app, output, portable, windows_desktop=None):
    require(windows_desktop is None or args.platform == "windows", "Only the stateless Windows observer can be shared")
    regular(app)
    output.mkdir()
    run = uuid.uuid4().hex
    argv = [str(app), "--evidence-directory", str(output / "app"), "--run-id", run]
    if args.platform == "macos":
        argv.append("--libre-native-modal-sessions")
    result = dict(schema="wpf-modal-desktop-result-v1", qualified=False, binarySourceIdentityVerified=False,
                  app=str(app), appSha256=digest(app),
                  argv=argv, helpers=shared, sourceSha256=digest(Path(__file__).parents[1] / "samples/ProGPU.Wpf.ModalInteractionApp/App.xaml.cs"))
    desktop = windows_desktop
    process = session = failure = None
    cleanup_errors = []
    try:
        with (output / "stdout.txt").open("xb") as stdout, (output / "stderr.txt").open("xb") as stderr:
            deadline = time.monotonic() + 60
            process = subprocess.Popen(argv, stdout=stdout, stderr=stderr)
            result["pid"] = process.pid
            if desktop is None:
                desktop = make_desktop(args.platform, module, args.macos_helper, output,
                                       lambda: observation(output / "app", process.pid, run)[0])
            result["nativeHelper"] = getattr(desktop, "provenance", None)
            session = Session(desktop, process, output, run, portable, deadline,
                              args.hosted_windows_caption_activation,
                              args.hosted_windows_shell_escape and not portable,
                              getattr(args, "hosted_windows_retirement_readiness", False) is True,
                              getattr(args, "hosted_windows_start_activation", False) is True and not portable)
            if args.platform == "macos":
                desktop.deadline = session.deadline
            scenario(session)
            result.update(completed=True, exitCode=process.returncode, physicalActions=session.actions)
    except BaseException as error:
        failure = error
        result.update(completed=False, error=str(error)[:4096], phase=session.phase if session else "startup")
        if session is not None:
            session.record_failure(result)
    finally:
        try:
            if process is not None and process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
        except BaseException as error:
            cleanup_errors.append("owned process: " + str(error)[:2048])
        try:
            if desktop is not None and hasattr(desktop, "close"):
                desktop.close()
        except BaseException as error:
            cleanup_errors.append("native observer: " + str(error)[:2048])
        result["cleanupErrors"] = cleanup_errors
        if process is not None:
            result["exitCodeAfterCleanup"] = process.returncode
        if cleanup_errors:
            result["completed"] = False
        try:
            write(output / "result.json", result)
        except BaseException as error:
            if failure is None:
                failure = error
    if failure is not None:
        raise failure
    require(not cleanup_errors, "Desktop cleanup failed: " + "; ".join(cleanup_errors))
    return desktop


def main():
    unique_options(sys.argv[1:])
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--platform", choices=("windows", "macos", "x11"), required=True)
    parser.add_argument("--portable-app", type=Path, required=True)
    parser.add_argument("--reference-app", type=Path)
    parser.add_argument("--forms-helper-checkout", type=Path, required=True)
    parser.add_argument("--forms-helper-commit", required=True)
    parser.add_argument("--macos-helper", type=Path)
    parser.add_argument("--hosted-windows-caption-activation", action="store_true")
    parser.add_argument("--hosted-windows-shell-escape", action="store_true")
    parser.add_argument("--hosted-windows-start-activation", action="store_true")
    parser.add_argument("--hosted-windows-retirement-readiness", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    require((args.platform == "windows" and sys.platform == "win32") or
            (args.platform == "macos" and sys.platform == "darwin") or
            (args.platform == "x11" and sys.platform.startswith("linux")), "Unsupported platform selection")
    require((args.reference_app is not None) == (args.platform == "windows"), "Windows requires an original Microsoft pair only")
    require(not args.hosted_windows_caption_activation or (args.platform == "windows" and
            os.environ.get("GITHUB_ACTIONS") == "true" and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and os.environ.get("RUNNER_OS") == "Windows" and os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF"),
            "Caption activation is restricted to this repository's disposable hosted Windows desktop")
    require(not args.hosted_windows_shell_escape or args.hosted_windows_caption_activation,
            "Approved shell Escape requires the original hosted caption admission")
    require(not args.hosted_windows_start_activation or args.hosted_windows_shell_escape,
            "Approved Start activation requires the original bounded hosted shell guard")
    require(not args.hosted_windows_retirement_readiness or args.hosted_windows_caption_activation,
            "Retirement readiness requires the original hosted caption admission")
    regular(args.output.parent, True)
    require(args.output.is_absolute() and not args.output.exists(), "Output must be a fresh absolute directory")
    module, shared = load_helper(args.forms_helper_checkout, args.forms_helper_commit)
    args.output.mkdir()
    windows_desktop = None
    if args.reference_app is not None:
        # SetProcessDpiAwarenessContext belongs to this controller process, not
        # each child application. The immutable helper holds only native API
        # bindings; each run still owns its own PID, source state and deadline.
        windows_desktop = run_one(args, module, shared, args.reference_app, args.output / "microsoft", False)
    run_one(args, module, shared, args.portable_app, args.output / "portable", True, windows_desktop)


if __name__ == "__main__":
    main()
