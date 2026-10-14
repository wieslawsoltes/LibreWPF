"""Failure-only native replay. Its result never qualifies the original idle run."""
import json
from pathlib import Path
import struct
import tempfile

import showcase_idle_crash as crash
from showcase_idle_output import MAX_LOG_BYTES


def should_replay(metadata):
    # Ordinary app failure is admitted only after the runner checked the same
    # prebuilt payload before/after and recorded the actual nonzero child exit.
    managed_failure = (type(metadata.get("childExitCode")) is int and metadata["childExitCode"] == 1
        and metadata.get("error") == "ContractError: Showcase idle child exited 1"
        and bool(metadata.get("payloadSha256Before"))
        and metadata["payloadSha256Before"] == metadata.get("payloadSha256After"))
    return (metadata.get("success") is False and metadata.get("timedOut") is False
            and (metadata.get("childExitCode") in (0xC0000005, 0xC0000409, 0xC0000602) or managed_failure)
            and metadata.get("crashEvidence", {}).get("captured") is False
            and not metadata.get("cleanupErrors"))


def machine(path):
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError("Not a PE executable")
        offset = struct.unpack_from("<I", header, 60)[0]
        if not 64 <= offset <= 1024 * 1024:
            raise ValueError("Unbounded PE header")
        stream.seek(offset)
        signature = stream.read(6)
        if len(signature) != 6 or signature[:4] != b"PE\0\0":
            raise ValueError("Invalid PE signature")
        result = struct.unpack_from("<H", signature, 4)[0]
        if result not in (0xAA64, 0x8664):
            raise ValueError("Only pure ARM64/x64 diagnostic executables are admitted")
        return result


def load_event(path, expected_machine):
    with path.open("rb") as stream:
        data = stream.read(4097)
    if len(data) > 4096:
        raise ValueError("Debugger receipt exceeds its bound")
    event = json.loads(data)
    integers = {"processId": 0xFFFFFFFF, "machine": 0xFFFF, "exitCode": 0xFFFFFFFF,
                "exceptionCode": 0xFFFFFFFF, "exceptionThreadId": 0xFFFFFFFF,
                "exceptionAddress": 0xFFFFFFFFFFFFFFFF, "dumpError": 0xFFFFFFFF, "loopError": 0xFFFFFFFF,
                "snapshotAfterMs": 60000, "snapshotElapsedMs": 110000, "snapshotError": 0xFFFFFFFF,
                "firstChanceAvThreadId": 0xFFFFFFFF, "firstChanceAvInstruction": 0xFFFFFFFFFFFFFFFF,
                "firstChanceAvAddress": 0xFFFFFFFFFFFFFFFF, "firstChanceAvOperation": 8,
                "firstChanceAvElapsedMs": 110000, "firstChanceAvDumpError": 0xFFFFFFFF}
    if (not isinstance(event, dict) or
            any(type(event.get(key)) is not int or not 0 <= event[key] <= limit for key, limit in integers.items()) or
            type(event.get("schemaVersion")) is not int or event["schemaVersion"] != 3 or event.get("diagnosticOnly") is not True
            or event.get("machine") != expected_machine
            or event["processId"] == 0 or type(event.get("captured")) is not bool
            or event.get("exited") is not True or event.get("loopError") != 0
            or (event["captured"] and (event["dumpError"] != 0 or event["exceptionCode"] == 0 or event["exceptionThreadId"] == 0))
            or type(event.get("snapshotAttempted")) is not bool
            or (event["snapshotAfterMs"] != 0 and event["snapshotAfterMs"] < 100)
            or (event["snapshotAttempted"] and (event["snapshotAfterMs"] == 0 or event["snapshotElapsedMs"] < event["snapshotAfterMs"]))
            or (not event["snapshotAttempted"] and (event["snapshotElapsedMs"] != 0 or event["snapshotError"] != 0))
            or type(event.get("firstChanceAvEnabled")) is not bool
            or type(event.get("firstChanceAvAttempted")) is not bool
            or (event["firstChanceAvAttempted"] and (not event["firstChanceAvEnabled"]
                or event["firstChanceAvThreadId"] == 0 or event["firstChanceAvInstruction"] == 0
                or event["firstChanceAvAddress"] <= 0x10000 or event["firstChanceAvOperation"] not in (0, 1, 8)))
            or (not event["firstChanceAvAttempted"] and any(event[key] != 0 for key in
                ("firstChanceAvThreadId", "firstChanceAvInstruction", "firstChanceAvAddress",
                 "firstChanceAvOperation", "firstChanceAvElapsedMs", "firstChanceAvDumpError")))):
        raise ValueError("Debugger did not report a completed, architecture-matched owned child")
    return event


def collect_snapshot(raw, directory, event, expected_machine):
    report = {"kind": "live-process-MiniDumpNormal", "diagnosticOnly": True,
              "qualifiesIdle": False, "captured": False,
              "requestedAfterMs": event["snapshotAfterMs"], "elapsedMs": event["snapshotElapsedMs"],
              "attempted": event["snapshotAttempted"], "dumpError": event["snapshotError"]}
    if not event["snapshotAttempted"] or event["snapshotError"] != 0:
        return report
    validated = crash.validate_dump(raw, event["processId"], snapshot_machine=expected_machine)
    destination = directory / "native-stack.dmp"
    crash.publish_dump(raw, destination, event["processId"], validated, snapshot_machine=expected_machine)
    return report | validated | {"captured": True, "path": destination.name}


def correlate_exception(path, event):
    # The bounded normal-dump validator must have admitted this file first.
    validated = crash.validate_dump(path, event["processId"])
    with path.open("rb") as stream:
        header = stream.read(32)
        count, table = struct.unpack_from("<II", header, 8)
        stream.seek(table)
        entries = [struct.unpack("<III", stream.read(12)) for _ in range(count)]
        offset = next(offset for kind, _, offset in entries if kind == 6)
        stream.seek(offset)
        data = stream.read(32)
        thread, _, code, _, _, address = struct.unpack("<IIIIQQ", data)
    if (thread, code, address) != (event["exceptionThreadId"], event["exceptionCode"], event["exceptionAddress"]):
        raise ValueError("Minidump exception does not match the owned debug event")
    return validated


def collect_first_chance_av(raw, directory, event, expected_machine):
    report = {"kind": "first-chance-nonnull-AV-MiniDumpNormal", "diagnosticOnly": True,
              "qualifiesIdle": False, "provesUnhandledCrash": False, "captured": False,
              "enabled": event["firstChanceAvEnabled"], "attempted": event["firstChanceAvAttempted"],
              "dumpError": event["firstChanceAvDumpError"]}
    if not event["firstChanceAvAttempted"] or event["firstChanceAvDumpError"] != 0:
        return report
    mapped = event | {"exceptionCode": 0xC0000005, "exceptionThreadId": event["firstChanceAvThreadId"],
                      "exceptionAddress": event["firstChanceAvInstruction"]}
    validated = correlate_exception(raw, mapped)
    with raw.open("rb") as stream:
        header = stream.read(32)
        count, table = struct.unpack_from("<II", header, 8)
        stream.seek(table)
        entries = [struct.unpack("<III", stream.read(12)) for _ in range(count)]
        systems = [(length, offset) for kind, length, offset in entries if kind == 7]
        if len(systems) != 1 or systems[0][0] < 56 or systems[0][1] < table + count * 12:
            raise ValueError("First-chance dump requires its intrinsic architecture")
        stream.seek(systems[0][1])
        if struct.unpack("<H", stream.read(2))[0] != {0xAA64: 12, 0x8664: 9}[expected_machine]:
            raise ValueError("First-chance dump architecture differs from its owned child")
        offset = next(offset for kind, _, offset in entries if kind == 6)
        stream.seek(offset + 32)
        parameters, _, operation, address = struct.unpack("<IIQQ", stream.read(24))
        stream.seek(offset + 160)
        context_size, context_offset = struct.unpack("<II", stream.read(8))
        if (parameters, operation, address) != (2, event["firstChanceAvOperation"], event["firstChanceAvAddress"]):
            raise ValueError("First-chance access parameters differ from the actual debug event")
        if context_size == 0 or context_offset < table + count * 12 or context_offset + context_size > validated["bytes"]:
            raise ValueError("First-chance dump has no complete faulting thread context")
    destination = directory / "first-chance-av.dmp"
    crash.publish_dump(raw, destination, event["processId"], validated)
    return report | validated | {"captured": True, "path": destination.name,
        "threadId": event["firstChanceAvThreadId"], "instruction": event["firstChanceAvInstruction"],
        "accessAddress": event["firstChanceAvAddress"], "operation": event["firstChanceAvOperation"],
        "elapsedMs": event["firstChanceAvElapsedMs"]}


def replay(app, debugger, evidence_parent, environment, run_child, write_json, payload_hashes, expected_hashes,
           original_exit_code):
    if original_exit_code not in (1, 0xC0000005, 0xC0000409, 0xC0000602) or type(original_exit_code) is not int:
        raise ValueError("Replay requires the original admitted child failure")
    trace = original_exit_code == 1
    debugger = debugger.resolve(strict=True)
    if debugger.name != "ShowcaseNativeDebugger.exe" or machine(debugger) != machine(app):
        raise ValueError("Debugger must have the original apphost's native architecture")
    directory = Path(tempfile.mkdtemp(prefix="native-debugger-replay-", dir=evidence_parent))
    report = {"schemaVersion": 1, "diagnosticOnly": True, "qualifiesIdle": False,
              "debuggerSha256": crash.digest(debugger), "timeoutSeconds": 120, "captured": False,
              "originalExitCode": original_exit_code, "nativeLoopTrace": trace, "nativeSceneEncodeTrace": trace,
              "nativeVectorClipTrace": trace, "nativeComputeTraceRequested": trace,
              "logTailLimitBytesPerStream": MAX_LOG_BYTES}
    capture = crash.Capture(app, directory, crash.WindowsRegistry())
    # Keep raw/partial stack snapshots out of uploaded evidence and separate
    # from the strict exception-only WER directory. Publish only validated bytes.
    snapshot_root = Path(tempfile.mkdtemp(prefix="showcase-live-stack-"))
    snapshot_path = snapshot_root / "native-stack.dmp"
    first_av_root = Path(tempfile.mkdtemp(prefix="showcase-first-chance-av-"))
    first_av_path = first_av_root / "first-chance-av.dmp"
    try:
        report["payloadSha256Before"] = payload_hashes(app.parent)
        if report["payloadSha256Before"] != expected_hashes:
            raise ValueError("Original payload changed before diagnostic replay")
        image = capture.prepare()
        env = environment.copy()
        # Native crashes retain original scheduling without the heavy tracing
        # or timed live dump. Ordinary managed failures retain that diagnosis.
        for name in ("PROGPU_WPF_TRACE_NATIVE_LOOP", "PROGPU_NATIVE_TRACE_SCENE_ENCODE",
                     "PROGPU_NATIVE_TRACE_VECTOR_CLIP", "PROGPU_NATIVE_TRACE_COMPUTE"):
            env[name] = "1" if trace else "0"
        env["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_STATUS_PATH"] = str(directory / "application-receipt.json")
        env["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_PHASE_PATH"] = str(directory / "application-phases.jsonl")
        event_path = directory / "native-debugger-event.json"
        options = ["--snapshot-after-ms=40000", str(snapshot_path)] if trace else ["--first-chance-av", str(first_av_path)]
        code, timed_out = run_child([str(debugger), str(image), str(capture.dump_directory), str(event_path), *options],
                                   app.parent, env, directory, timeout=120, outcome=report,
                                   log_limit_bytes=MAX_LOG_BYTES)
        report.update(debuggerExitCode=code, timedOut=timed_out)
        if timed_out:
            raise ValueError("Diagnostic replay exceeded its separate bound")
        if report.get("cleanupErrors"):
            raise ValueError("Diagnostic replay child/log cleanup failed")
        event = load_event(event_path, machine(app))
        if event["processId"] == report["childProcessId"] or event["exitCode"] != code:
            raise ValueError("Debugger/helper child identities or exit status disagree")
        report["nativeEvent"] = event
        if event["snapshotAfterMs"] != (40000 if trace else 0) or event["firstChanceAvEnabled"] != (not trace):
            raise ValueError("Debugger snapshot schedule differs from the requested diagnostic")
        report["stackSnapshot"] = collect_snapshot(snapshot_path, directory, event, machine(app))
        report["firstChanceAccessViolation"] = collect_first_chance_av(first_av_path, directory, event, machine(app))
        correlated = None
        if event["captured"]:
            correlated = correlate_exception(capture.dump_directory / f"{image.name}.{event['processId']}.dmp", event)
        report["crashEvidence"] = capture.collect(event["processId"], event["exitCode"], False)
        report["crashEvidence"]["kind"] = "Windows-debug-event-MiniDumpNormal"
        if bool(event["captured"]) != bool(report["crashEvidence"]["captured"]):
            raise ValueError("Native dump receipt and independently validated dump disagree")
        if correlated is not None and any(report["crashEvidence"].get(key) != value for key, value in correlated.items()):
            raise ValueError("Published dump changed after exception correlation")
        report["captured"] = report["crashEvidence"]["captured"]
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        try:
            report["payloadSha256After"] = payload_hashes(app.parent)
            if report["payloadSha256After"] != expected_hashes:
                report["error"] = "Original payload changed during diagnostic replay"
        except Exception as error:
            report["error"] = f"Diagnostic payload identity unavailable: {type(error).__name__}: {error}"
        report["captureCleanupErrors"] = capture.close()
    write_json(directory / "diagnostic-receipt.json", report)
    return {"diagnosticOnly": True, "qualifiesIdle": False, "path": str(directory),
            "captured": report["captured"], "error": report.get("error")}
