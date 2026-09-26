#!/usr/bin/env python3
"""Run a prebuilt, real Showcase native idle fixture without building or relaxing it."""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid


TIMEOUT_SECONDS = 120
CLEANUP_SECONDS = 5
MAX_RECEIPT_BYTES = 1024 * 1024
PHASES = ("initial", "scrolled", "native-resized", "restored")
ASSEMBLIES = (
    "ProGPU.Wpf.ShowcaseApp", "PresentationFramework", "PresentationCore",
    "ProGPU.Wpf", "ProGPU.Wpf.Interop",
)
CONFLICTING_MODES = (
    "PROGPU_WPF_SHOWCASE_VALIDATE", "PROGPU_WPF_SHOWCASE_RUN_VALIDATE",
    "PROGPU_WPF_SHOWCASE_LIVE_VALIDATE", "PROGPU_WPF_SHOWCASE_PERFORMANCE_VALIDATE",
)


class ContractError(ValueError):
    pass


class Interrupted(BaseException):
    def __init__(self, signum: int):
        self.signum = signum


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def number(value: object, name: str, *, minimum: float = 0, integer: bool = False) -> float | int:
    valid_type = type(value) is int if integer else type(value) in (int, float)
    require(valid_type, f"{name} must be {'an integer' if integer else 'numeric'}")
    try:
        valid = math.isfinite(value) and value >= minimum
    except OverflowError:
        valid = False
    require(valid, f"{name} must be finite and >= {minimum}")
    return value


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def load_receipt(path: Path) -> dict:
    require(path.is_file() and not path.is_symlink(), "Missing regular application receipt")
    with path.open("rb") as stream:
        encoded = stream.read(MAX_RECEIPT_BYTES + 1)
    require(len(encoded) <= MAX_RECEIPT_BYTES, "Application receipt exceeds size limit")
    result = json.loads(encoded, object_pairs_hook=reject_duplicate_keys,
                        parse_constant=lambda value: (_ for _ in ()).throw(ContractError(f"Invalid JSON number: {value}")))
    require(type(result) is dict, "Application receipt must be an object")
    return result


def validate_receipt(receipt: dict, hashes: dict[str, str]) -> None:
    require(type(receipt.get("schemaVersion")) is int and receipt["schemaVersion"] == 1, "Unsupported receipt schema")
    require(receipt.get("success") is True, f"Application rejected idle validation: {receipt.get('error')}")
    require(receipt.get("error") in (None, ""), "Successful receipt contains an error")
    require(receipt.get("mode") == "NativeMilWgpu", "Actual native MIL mode is required")
    require(receipt.get("sourceRoot") == "Window", "Actual Window source root is required")
    require(receipt.get("uiRestored") is True, "Source UI restoration did not complete")
    identities = receipt.get("assemblyIdentities")
    require(type(identities) is list and len(identities) == len(ASSEMBLIES), "Expected five source assembly identities")
    names = set()
    for identity in identities:
        require(type(identity) is dict, "Invalid assembly identity")
        name = identity.get("name")
        require(type(name) is str and name in ASSEMBLIES and name not in names, "Unknown or duplicate source assembly")
        names.add(name)
        mvid = identity.get("mvid")
        require(type(mvid) is str and re.fullmatch(r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", mvid) is not None,
                f"Invalid MVID for {name}")
        require(uuid.UUID(mvid).int != 0, f"Empty MVID for {name}")
        digest = identity.get("sha256")
        require(type(digest) is str and re.fullmatch(r"[0-9a-fA-F]{64}", digest) is not None,
                f"Invalid SHA256 for {name}")
        require(digest.lower() == hashes.get(name), f"Loaded {name} does not match the prebuilt payload")

    phases = receipt.get("phases")
    require(type(phases) is list and len(phases) == len(PHASES), "All four idle phases are mandatory")
    geometry_keys = ("logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "dpiScale")
    previous_frames = None
    recovery = None
    for expected, phase in zip(PHASES, phases):
        require(type(phase) is dict and phase.get("name") == expected, "Missing, duplicate or reordered idle phase")
        require(phase.get("stableIdentity") is True, f"{expected}: source/native identity changed")
        interval = phase.get("interval")
        require(type(interval) is dict, f"{expected}: missing interval")
        first = number(interval.get("framesBefore"), "framesBefore", minimum=1, integer=True)
        last = number(interval.get("framesAfter"), "framesAfter", minimum=1, integer=True)
        extra = number(interval.get("extraPresentations"), "extraPresentations", integer=True)
        require(last - first == extra == 0, f"{expected}: static source presented additional frames")
        if previous_frames is not None:
            require(first > previous_frames, f"{expected}: transition did not publish a genuine new frame")
        previous_frames = last
        number(interval.get("wallMilliseconds"), "wallMilliseconds", minimum=2000)
        number(interval.get("cpuMilliseconds"), "cpuMilliseconds")
        for key in ("allocatedBytes", "gen0Collections", "gen1Collections", "gen2Collections"):
            number(interval.get(key), key, integer=True)
        for key in geometry_keys:
            number(phase.get(key), key, minimum=1 if key != "dpiScale" else sys.float_info.min,
                   integer=key != "dpiScale")
        number(phase.get("scrollOffset"), "scrollOffset")
        number(phase.get("zeroClipWidth"), "zeroClipWidth")
        number(phase.get("zeroClipHeight"), "zeroClipHeight")
        require(phase["zeroClipWidth"] == 80 and phase["zeroClipHeight"] == 0 and phase.get("zeroClipIsEmpty") is False,
                f"{expected}: zero-height source clip is missing or Empty")
        current_recovery = number(phase.get("deviceRecoveryCount"), "deviceRecoveryCount", integer=True)
        require(recovery is None or recovery == current_recovery, "Device recovery crossed the measured phases")
        recovery = current_recovery
        for key in ("commandCount", "drawCallCount", "submissionCount"):
            number(phase.get(key), key, minimum=1, integer=True)

    initial, scrolled, resized, restored = phases
    geometry = lambda phase: tuple(phase[key] for key in geometry_keys)
    require(geometry(initial) == geometry(scrolled) == geometry(restored), "Scroll/restore changed the original surface geometry")
    require(resized["logicalWidth"] > initial["logicalWidth"] and resized["logicalHeight"] > initial["logicalHeight"],
            "Native resize did not grow the actual client extent")
    require(initial["scrollOffset"] == restored["scrollOffset"] == 0 and scrolled["scrollOffset"] > 0,
            "Source scroll and restoration were not observed")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def payload_hashes(directory: Path) -> dict[str, str]:
    result = {}
    for name in ASSEMBLIES:
        path = directory / f"{name}.dll"
        require(path.is_file() and not path.is_symlink(), f"Missing regular prebuilt source payload: {path}")
        result[name] = sha256(path)
    return result


class WindowsJob:
    """Own child descendants through a kernel job, never a broad process-name kill."""

    def __init__(self):
        from ctypes import wintypes

        class BasicLimits(ctypes.Structure):
            _fields_ = [("process_time", ctypes.c_longlong), ("job_time", ctypes.c_longlong),
                        ("flags", wintypes.DWORD), ("min_working_set", ctypes.c_size_t),
                        ("max_working_set", ctypes.c_size_t), ("active_processes", wintypes.DWORD),
                        ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD), ("scheduling", wintypes.DWORD)]

        class IoCounters(ctypes.Structure):
            _fields_ = [(name, ctypes.c_ulonglong) for name in ("read_ops", "write_ops", "other_ops", "read_bytes", "write_bytes", "other_bytes")]

        class ExtendedLimits(ctypes.Structure):
            _fields_ = [("basic", BasicLimits), ("io", IoCounters), ("process_memory", ctypes.c_size_t),
                        ("job_memory", ctypes.c_size_t), ("peak_process_memory", ctypes.c_size_t), ("peak_job_memory", ctypes.c_size_t)]

        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        self.kernel.CreateJobObjectW.restype = wintypes.HANDLE
        self.kernel.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        self.kernel.SetInformationJobObject.restype = wintypes.BOOL
        self.kernel.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        self.kernel.AssignProcessToJobObject.restype = wintypes.BOOL
        self.kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        self.kernel.CloseHandle.restype = wintypes.BOOL
        self.handle = self.kernel.CreateJobObjectW(None, None)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = ExtendedLimits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.kernel.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            error = ctypes.WinError(ctypes.get_last_error())
            self.close()
            raise error

    def assign(self, process: subprocess.Popen) -> None:
        if not self.kernel.AssignProcessToJobObject(self.handle, int(process._handle)):
            raise ctypes.WinError(ctypes.get_last_error())

    def close(self) -> None:
        if self.handle:
            handle, self.handle = self.handle, None
            if not self.kernel.CloseHandle(handle):
                raise ctypes.WinError(ctypes.get_last_error())


def signal_owned_group(process: subprocess.Popen, signum: int) -> None:
    try:
        os.killpg(process.pid, signum)
    except ProcessLookupError:
        pass


def run_child(command: list[str], cwd: Path, environment: dict[str, str], directory: Path,
              timeout: float = TIMEOUT_SECONDS, outcome: dict | None = None) -> tuple[int, bool]:
    process = None
    job = None
    timed_out = False
    outcome = {} if outcome is None else outcome
    cleanup_errors = []
    deadline = time.monotonic() + timeout
    with (directory / "stdout.log").open("xb") as stdout, (directory / "stderr.log").open("xb") as stderr:
        try:
            if os.name == "nt":
                job = WindowsJob()
            process = subprocess.Popen(command, cwd=cwd, env=environment, stdin=subprocess.DEVNULL,
                                       stdout=stdout, stderr=stderr, start_new_session=os.name == "posix")
            if job is not None:
                job.assign(process)
            try:
                process.wait(timeout=max(0, deadline - time.monotonic()))
            except subprocess.TimeoutExpired:
                timed_out = True
        finally:
            outcome["timedOut"] = timed_out
            if job is not None:
                try:
                    job.close()
                except OSError as error:
                    cleanup_errors.append(str(error))
            if process is not None:
                try:
                    if os.name == "posix":
                        signal_owned_group(process, signal.SIGTERM)
                    elif process.poll() is None:
                        process.terminate()
                except OSError as error:
                    cleanup_errors.append(str(error))
                try:
                    try:
                        process.wait(timeout=CLEANUP_SECONDS)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=CLEANUP_SECONDS)
                except (OSError, subprocess.TimeoutExpired) as error:
                    cleanup_errors.append(str(error))
                finally:
                    if os.name == "posix":
                        # Retire descendants even when their group leader has exited.
                        try:
                            signal_owned_group(process, signal.SIGKILL)
                        except OSError as error:
                            cleanup_errors.append(str(error))
                    outcome["childExitCode"] = process.poll()
            outcome["cleanupErrors"] = cleanup_errors
    require(process.returncode is not None, "Owned child did not terminate")
    return process.returncode, timed_out


def write_json_new(path: Path, value: dict) -> None:
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def run(app: Path, dotnet: str | None, evidence_parent: Path) -> int:
    app = app.resolve(strict=True)
    require(app.is_file() and app.name in ("ProGPU.Wpf.ShowcaseApp", "ProGPU.Wpf.ShowcaseApp.exe", "ProGPU.Wpf.ShowcaseApp.dll"),
            "--app must be the prebuilt genuine Showcase apphost or DLL")
    evidence_parent = evidence_parent.resolve(strict=True)
    require(evidence_parent.is_dir(), "Evidence parent must already exist")
    for name in CONFLICTING_MODES:
        require(os.environ.get(name) != "1", f"Conflicting Showcase mode: {name}")
    before = payload_hashes(app.parent)
    app_hash = sha256(app)
    if app.suffix.lower() == ".dll":
        executable = shutil.which(dotnet or "dotnet")
        require(executable is not None, "A .NET host is required for a prebuilt DLL")
        command = [executable, str(app)]
    else:
        require(dotnet is None, "--dotnet applies only to a prebuilt DLL")
        command = [str(app)]
    directory = Path(tempfile.mkdtemp(prefix="showcase-idle-", dir=evidence_parent))
    status_path = directory / "application-receipt.json"
    environment = os.environ.copy()
    environment["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_VALIDATE"] = "1"
    environment["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_STATUS_PATH"] = str(status_path)
    metadata = {"schemaVersion": 1, "command": command, "workingDirectory": str(app.parent),
                "timeoutSeconds": TIMEOUT_SECONDS, "appSha256": app_hash, "payloadSha256Before": before,
                "success": False, "childExitCode": None, "timedOut": False}
    write_json_new(directory / "launch.json", metadata)
    print(f"Showcase idle evidence: {directory}", flush=True)
    exit_code = 1
    try:
        child_code, timed_out = run_child(command, app.parent, environment, directory, outcome=metadata)
        metadata.update(childExitCode=child_code, timedOut=timed_out)
        exit_code = 124 if timed_out else child_code if child_code >= 0 else 128 - child_code
        after = payload_hashes(app.parent)
        metadata["payloadSha256After"] = after
        require(before == after and app_hash == sha256(app), "Prebuilt payload changed while the child ran")
        require(not timed_out, f"Showcase idle child exceeded {TIMEOUT_SECONDS} seconds")
        require(child_code == 0, f"Showcase idle child exited {child_code}")
        require(not metadata.get("cleanupErrors"), f"Owned-child cleanup failed: {metadata.get('cleanupErrors')}")
        validate_receipt(load_receipt(status_path), before)
        metadata["success"] = True
    except Interrupted as interrupted:
        exit_code = 128 + interrupted.signum
        metadata["error"] = f"Runner interrupted by signal {interrupted.signum}"
    except Exception as error:
        recorded_child = metadata.get("childExitCode")
        if metadata.get("timedOut"):
            exit_code = 124
        elif recorded_child is not None and recorded_child != 0:
            exit_code = recorded_child if recorded_child > 0 else 128 - recorded_child
        if exit_code == 0:
            exit_code = 1
        metadata["error"] = f"{type(error).__name__}: {error}"
    metadata["runnerExitCode"] = exit_code
    try:
        write_json_new(directory / "runner-receipt.json", metadata)
    except OSError as error:
        print(f"Could not retain runner receipt: {error}", file=sys.stderr)
        return exit_code or 1
    print(f"Showcase passive idle {'passed' if metadata['success'] else 'failed'}; evidence retained at {directory}")
    return exit_code


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", required=True, type=Path, help="Prebuilt Showcase apphost or DLL; never builds")
    parser.add_argument("--dotnet", help=".NET host for DLL input only")
    parser.add_argument("--evidence-parent", required=True, type=Path, help="Existing directory for a fresh evidence child")
    args = parser.parse_args()
    handlers = {}
    try:
        for signum in (signal.SIGINT, signal.SIGTERM):
            handlers[signum] = signal.signal(signum, lambda received, frame: (_ for _ in ()).throw(Interrupted(received)))
        return run(args.app, args.dotnet, args.evidence_parent)
    except Interrupted as interrupted:
        return 128 + interrupted.signum
    except (OSError, ContractError) as error:
        print(f"Showcase idle preflight failed: {error}", file=sys.stderr)
        return 1
    finally:
        for signum, handler in handlers.items():
            signal.signal(signum, handler)


if __name__ == "__main__":
    raise SystemExit(main())
