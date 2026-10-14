"""Offline replay admission and optional real Windows owned-child controls."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import types
import unittest
from unittest import mock
import uuid

import showcase_idle_debugger as diagnostic
import showcase_idle_crash as crash


def event():
    return dict(schemaVersion=3, diagnosticOnly=True, processId=42, machine=0xAA64,
                exitCode=0xC0000005, exited=True, exceptionCode=0xC0000005,
                exceptionThreadId=7, exceptionAddress=0x12345678, captured=True,
                dumpError=0, loopError=0, snapshotAfterMs=0, snapshotAttempted=False,
                snapshotElapsedMs=0, snapshotError=0, firstChanceAvEnabled=False,
                firstChanceAvAttempted=False, firstChanceAvThreadId=0,
                firstChanceAvInstruction=0, firstChanceAvAddress=0, firstChanceAvOperation=0,
                firstChanceAvElapsedMs=0, firstChanceAvDumpError=0)


class Controls(unittest.TestCase):
    def test_only_failed_native_crash_without_original_dump_replays(self):
        original = dict(success=False, timedOut=False, childExitCode=0xC0000005,
                        crashEvidence={"captured": False}, cleanupErrors=[])
        self.assertTrue(diagnostic.should_replay(original))
        for changes in (dict(success=True), dict(timedOut=True), dict(childExitCode=0),
                        dict(childExitCode=1), dict(childExitCode=124),
                        dict(crashEvidence={"captured": True}), dict(cleanupErrors=["failed"])):
            with self.subTest(changes=changes):
                self.assertFalse(diagnostic.should_replay(original | changes))
        self.assertEqual(original["childExitCode"], 0xC0000005)

    def test_missing_metadata_never_launches(self):
        self.assertFalse(diagnostic.should_replay({}))

    def test_managed_failure_replay_requires_unchanged_payload_and_actual_exit(self):
        original = dict(success=False, timedOut=False, childExitCode=1, cleanupErrors=[],
                        error="ContractError: Showcase idle child exited 1",
                        crashEvidence={"captured": False}, payloadSha256Before={"source": "a"},
                        payloadSha256After={"source": "a"})
        self.assertTrue(diagnostic.should_replay(original))
        for changes in (dict(success=True), dict(timedOut=True), dict(childExitCode=0),
                        dict(childExitCode=True), dict(childExitCode=124), dict(error="payload changed"),
                        dict(payloadSha256Before={}), dict(payloadSha256After={"source": "b"}),
                        dict(crashEvidence={"captured": True}), dict(cleanupErrors=["failed"])):
            with self.subTest(changes=changes):
                self.assertFalse(diagnostic.should_replay(original | changes))

    def test_replay_enables_trace_only_in_fresh_environment_with_bounded_logs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app = root / "ProGPU.Wpf.ShowcaseApp.exe"
            debugger = root / "ShowcaseNativeDebugger.exe"
            hashes = {"source": "a"}
            environment = {"original": "unchanged", "PROGPU_NATIVE_TRACE_COMPUTE": "0"}
            captured = {}
            def child(command, cwd, env, directory, **kwargs):
                self.assertEqual("1", env["PROGPU_WPF_TRACE_NATIVE_LOOP"])
                self.assertEqual("1", env["PROGPU_NATIVE_TRACE_SCENE_ENCODE"])
                self.assertEqual("1", env["PROGPU_NATIVE_TRACE_VECTOR_CLIP"])
                self.assertEqual("1", env["PROGPU_NATIVE_TRACE_COMPUTE"])
                self.assertEqual(root, cwd)
                self.assertEqual(120, kwargs["timeout"])
                self.assertEqual(8 * 1024 * 1024, kwargs["log_limit_bytes"])
                self.assertEqual(directory, Path(env["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_PHASE_PATH"]).parent)
                self.assertEqual("--snapshot-after-ms=40000", command[-2])
                self.assertNotEqual(directory, Path(command[-1]).parent)
                kwargs["outcome"].update(childProcessId=42, cleanupErrors=[])
                return 1, False
            def write(path, value):
                captured.update(value)
            with mock.patch.object(diagnostic, "machine", return_value=0xAA64), \
                 mock.patch.object(Path, "resolve", return_value=debugger), \
                 mock.patch.object(crash, "digest", return_value="digest"), \
                 mock.patch.object(crash, "WindowsRegistry"), mock.patch.object(crash, "Capture") as capture, \
                 mock.patch.object(diagnostic, "load_event", return_value=event() | {
                     "processId": 43, "exitCode": 1, "captured": False, "exceptionCode": 0,
                     "snapshotAfterMs": 40000}):
                capture.return_value.prepare.return_value = root / "unique.exe"
                capture.return_value.collect.return_value = {"captured": False}
                capture.return_value.close.return_value = []
                result = diagnostic.replay(app, debugger, root, environment, child, write, lambda _: hashes, hashes, 1)
                self.assertFalse(result["qualifiesIdle"])
                self.assertIsNone(result["error"])
                self.assertEqual(hashes, captured["payloadSha256After"])
                self.assertTrue(captured["nativeSceneEncodeTrace"])
                self.assertTrue(captured["nativeVectorClipTrace"])
                self.assertTrue(captured["nativeComputeTraceRequested"])
                self.assertFalse(captured["stackSnapshot"]["captured"])
                self.assertEqual({"original": "unchanged", "PROGPU_NATIVE_TRACE_COMPUTE": "0"}, environment)
                capture.return_value.close.assert_called_once()

    def test_native_crash_replay_omits_trace_and_timed_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app, debugger = root / "ProGPU.Wpf.ShowcaseApp.exe", root / "ShowcaseNativeDebugger.exe"
            hashes = {"source": "a"}
            names = ("PROGPU_WPF_TRACE_NATIVE_LOOP", "PROGPU_NATIVE_TRACE_SCENE_ENCODE",
                     "PROGPU_NATIVE_TRACE_VECTOR_CLIP", "PROGPU_NATIVE_TRACE_COMPUTE")
            environment = {name: "1" for name in names}
            captured = {}
            def child(command, cwd, env, directory, **kwargs):
                self.assertTrue(all(env[name] == "0" for name in names))
                self.assertEqual("--first-chance-av", command[-2])
                self.assertEqual("first-chance-av.dmp", Path(command[-1]).name)
                self.assertNotEqual(directory, Path(command[-1]).parent)
                self.assertFalse(any("snapshot-after" in arg for arg in command))
                self.assertEqual(120, kwargs["timeout"])
                kwargs["outcome"].update(childProcessId=42, cleanupErrors=[])
                return 0, False
            with mock.patch.object(diagnostic, "machine", return_value=0xAA64), \
                 mock.patch.object(Path, "resolve", return_value=debugger), \
                 mock.patch.object(crash, "digest", return_value="digest"), \
                 mock.patch.object(crash, "WindowsRegistry"), mock.patch.object(crash, "Capture") as capture, \
                 mock.patch.object(diagnostic, "load_event", return_value=event() | {
                     "processId": 43, "exitCode": 0, "captured": False, "exceptionCode": 0,
                     "firstChanceAvEnabled": True}):
                capture.return_value.prepare.return_value = root / "unique.exe"
                capture.return_value.collect.return_value = {"captured": False}
                capture.return_value.close.return_value = []
                result = diagnostic.replay(app, debugger, root, environment, child,
                    lambda _, value: captured.update(value), lambda _: hashes, hashes, 0xC0000005)
                self.assertIsNone(result["error"])
                self.assertFalse(result["qualifiesIdle"])
                self.assertFalse(captured["nativeLoopTrace"])
                self.assertFalse(captured["firstChanceAccessViolation"]["provesUnhandledCrash"])
                self.assertEqual({name: "1" for name in names}, environment)

    def test_pe_machine_is_read_not_inferred_from_name(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "arm64.exe"
            header = bytearray(70)
            header[:2] = b"MZ"
            struct.pack_into("<I", header, 60, 64)
            header[64:68] = b"PE\0\0"
            for kind in (0x8664, 0xAA64):
                struct.pack_into("<H", header, 68, kind)
                path.write_bytes(header)
                self.assertEqual(diagnostic.machine(path), kind)
            for kind in (0x14C, 0xA641, 0xA64E, 0):
                struct.pack_into("<H", header, 68, kind)
                path.write_bytes(header)
                with self.assertRaises(ValueError): diagnostic.machine(path)

    def test_pe_rejects_missing_signature_and_unbounded_offset(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "bad.exe"
            for data in (b"MZ", b"xx" + bytes(62), b"MZ" + bytes(58) + struct.pack("<I", 0xFFFFFFFF)):
                path.write_bytes(data)
                with self.assertRaises(ValueError): diagnostic.machine(path)

    def test_debugger_receipt_validates_identity_architecture_and_capture(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "event.json"
            path.write_text(json.dumps(event()), encoding="utf-8")
            self.assertEqual(diagnostic.load_event(path, 0xAA64), event())
            for change in (dict(diagnosticOnly=False), dict(machine=0x8664), dict(processId=True),
                           dict(processId=0), dict(exitCode=-1), dict(exited=False),
                           dict(loopError=5), dict(loopError=1460), dict(exited=1),
                           dict(captured="true"), dict(exceptionThreadId=0),
                           dict(exceptionCode=0), dict(dumpError=5), dict(schemaVersion=True),
                           dict(snapshotAfterMs=True), dict(snapshotAfterMs=99), dict(snapshotAfterMs=60001),
                           dict(snapshotAttempted=1), dict(snapshotAttempted=True),
                           dict(snapshotElapsedMs=1), dict(snapshotError=5),
                           dict(snapshotAfterMs=40000, snapshotAttempted=True, snapshotElapsedMs=39999),
                           dict(firstChanceAvEnabled=1), dict(firstChanceAvAttempted=1),
                           dict(firstChanceAvAttempted=True), dict(firstChanceAvThreadId=1),
                           dict(firstChanceAvAddress=0x10001), dict(firstChanceAvOperation=8),
                           dict(firstChanceAvElapsedMs=1), dict(firstChanceAvDumpError=5)):
                path.write_text(json.dumps(event() | change), encoding="utf-8")
                with self.subTest(change=change), self.assertRaises(ValueError):
                    diagnostic.load_event(path, 0xAA64)
            path.write_text(" " * 4097, encoding="utf-8")
            with self.assertRaises(ValueError): diagnostic.load_event(path, 0xAA64)

    def test_live_snapshot_is_not_crash_evidence_and_requires_complete_native_threads(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "raw.dmp"
            # Structural transport control only, not an executed Windows dump.
            start = 32 + 4 * 12
            identity = struct.pack("<IIIIII", 24, 1, 42, 0, 0, 0)
            system = struct.pack("<H", 12) + bytes(54)
            thread = bytearray(52)
            struct.pack_into("<II", thread, 0, 1, 7)
            context_offset = start + 24 + 56 + 52 + 112
            struct.pack_into("<II", thread, 44, 16, context_offset)
            module = struct.pack("<I", 1) + bytes(108)
            data = (struct.pack("<IIIIIIQ", 0x504D444D, 0xA793, 4, 32, 0, 0, 0)
                    + struct.pack("<III", 15, 24, start) + struct.pack("<III", 7, 56, start + 24)
                    + struct.pack("<III", 3, 52, start + 80) + struct.pack("<III", 4, 112, start + 132)
                    + identity + system + thread + module + bytes(16))
            path.write_bytes(data)
            snapshot_event = event() | dict(snapshotAfterMs=100, snapshotAttempted=True,
                snapshotElapsedMs=103, captured=False, exceptionCode=0, exceptionThreadId=0, exitCode=17)
            with self.assertRaises(ValueError): crash.validate_dump(path, 42)
            report = diagnostic.collect_snapshot(path, root, snapshot_event, 0xAA64)
            self.assertTrue(report["captured"])
            self.assertFalse(report["qualifiesIdle"])
            self.assertEqual(data, (root / "native-stack.dmp").read_bytes())
            with self.assertRaises(FileExistsError): diagnostic.collect_snapshot(path, root, snapshot_event, 0xAA64)
            for pid, machine in ((43, 0xAA64), (42, 0x8664), (42, 0xA641)):
                with self.subTest(pid=pid, machine=machine), self.assertRaises(ValueError):
                    crash.validate_dump(path, pid, snapshot_machine=machine)
            for offset, value in ((32, 6), (start + 80, 0), (start + 80, 4097),
                                  (start + 124, 0), (start + 128, len(data)), (start + 132, 0)):
                invalid = bytearray(data)
                struct.pack_into("<I", invalid, offset, value)
                path.write_bytes(invalid)
                with self.subTest(offset=offset, value=value), self.assertRaises(ValueError):
                    crash.validate_dump(path, 42, snapshot_machine=0xAA64)
            for changes in (dict(snapshotAttempted=False), dict(snapshotError=5)):
                with mock.patch.object(crash, "validate_dump") as validate:
                    self.assertFalse(diagnostic.collect_snapshot(path, root, snapshot_event | changes, 0xAA64)["captured"])
                    validate.assert_not_called()

    def test_exit_event_requires_continuation_and_actual_process_termination(self):
        source = (Path(__file__).parent / "native" / "showcase-native-debugger.cpp").read_text()
        event_start = source.index("case EXIT_PROCESS_DEBUG_EVENT:")
        continuation = source.index("if (!ContinueDebugEvent(", event_start)
        wait = source.index("WaitForSingleObject(process.value, remaining)", continuation)
        confirmed = source.index("else exited = true;", wait)
        receipt = source.index('const std::string json =', confirmed)
        self.assertIn("exit_event_seen = true;", source[event_start:continuation])
        self.assertNotIn("exited = true;", source[event_start:wait])
        self.assertIn("if (exit_event_seen && loop_error == 0)", source[continuation:wait])
        self.assertIn("now < deadline ? static_cast<DWORD>(deadline - now) : 0", source[continuation:wait])
        self.assertIn("wait == WAIT_OBJECT_0", source[wait:confirmed])
        self.assertIn("GetExitCodeProcess(process.value, &actual_exit_code)", source[wait:confirmed])
        self.assertIn("actual_exit_code != exit_code", source[wait:confirmed])
        self.assertIn("wait == WAIT_TIMEOUT ? ERROR_TIMEOUT", source[confirmed:receipt])
        self.assertIn("wait == WAIT_FAILED ? GetLastError()", source[confirmed:receipt])
        self.assertEqual(source.count("GetTickCount64() + child_deadline_ms"), 1)
        self.assertNotIn("INFINITE", source[continuation:receipt])

    def test_exception_stream_must_match_actual_event(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "normal.dmp"
            # Minimal bounded normal-dump transport, not an executed process.
            data = bytearray(struct.pack("<IIIIIIQ", 0x504D444D, 0xA793, 2, 32, 0, 0, 0)
                + struct.pack("<III", 6, 168, 56) + struct.pack("<III", 15, 24, 224)
                + bytes(168) + struct.pack("<IIIIII", 24, 1, 42, 0, 0, 0))
            struct.pack_into("<I", data, 56, 7)
            struct.pack_into("<I", data, 64, 0xC0000005)
            struct.pack_into("<Q", data, 80, 0x12345678)
            path.write_bytes(data)
            diagnostic.correlate_exception(path, event())
            for changes in (dict(processId=43), dict(exceptionThreadId=8),
                            dict(exceptionCode=0xC0000409), dict(exceptionAddress=0x12345679)):
                with self.subTest(changes=changes), self.assertRaises(ValueError):
                    diagnostic.correlate_exception(path, event() | changes)

    def test_original_receipt_precedes_replay_and_original_failure_survives(self):
        spec = importlib.util.spec_from_file_location("idle_runner_debug_control", Path(__file__).with_name("progpu-wpf-showcase-idle.py"))
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            app = root / "ProGPU.Wpf.ShowcaseApp.exe"
            app.write_bytes(b"test-only")
            debugger = root / "ShowcaseNativeDebugger.exe"
            original_code = 0xC0000005
            fail_replay = True
            def child(*args, **kwargs):
                self.assertNotIn("PROGPU_WPF_TRACE_NATIVE_LOOP", args[2])
                self.assertNotIn("PROGPU_NATIVE_TRACE_SCENE_ENCODE", args[2])
                self.assertNotIn("PROGPU_NATIVE_TRACE_VECTOR_CLIP", args[2])
                self.assertNotIn("PROGPU_NATIVE_TRACE_COMPUTE", args[2])
                kwargs["outcome"].update(childProcessId=42, childExitCode=original_code, timedOut=False, cleanupErrors=[])
                return original_code, False
            def finish(capture, metadata, code):
                metadata["crashEvidence"] = {"captured": False}
                return code
            def replay(app_arg, debugger_arg, directory, *args):
                receipt = json.loads((directory / "runner-receipt.json").read_text())
                self.assertFalse(receipt["success"])
                self.assertEqual(receipt["runnerExitCode"], original_code)
                if fail_replay:
                    raise RuntimeError("diagnostic failure must not replace original")
                return {"diagnosticOnly": True, "qualifiesIdle": False, "captured": True}
            with mock.patch.object(runner, "os", types.SimpleNamespace(name="nt", environ=os.environ)), \
                 mock.patch.dict(os.environ, {"GITHUB_ACTIONS": "true"}, clear=True), \
                 mock.patch.object(runner, "payload_hashes", return_value={"source": "a"}), \
                 mock.patch.object(diagnostic, "machine", return_value=0xAA64), \
                 mock.patch.object(crash, "WindowsRegistry"), mock.patch.object(crash, "Capture") as capture, \
                 mock.patch.object(runner, "run_child", side_effect=child), \
                 mock.patch.object(runner, "finish_crash_capture", side_effect=finish), \
                 mock.patch.object(runner, "collect_failure_events"), \
                 mock.patch.object(diagnostic, "replay", side_effect=replay) as replay_call:
                capture.return_value.prepare.return_value = app
                for original_code in (0xC0000005, 1):
                    with self.subTest(original_code=original_code):
                        replay_call.reset_mock()
                        fail_replay = True
                        self.assertEqual(runner.run(app, None, root, True, debugger), original_code)
                        replay_call.assert_called_once()
                        fail_replay = False
                        self.assertEqual(runner.run(app, None, root, True, debugger), original_code)
                        self.assertEqual(replay_call.call_count, 2)

    def test_first_chance_dump_correlates_access_and_can_end_successfully(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "raw.dmp"
            # Authored transport controls; executed SEH recovery is tested below
            # on both Windows architectures with the real native helper.
            data = bytearray(348)
            struct.pack_into("<IIIIIIQ", data, 0, 0x504D444D, 0xA793, 3, 32, 0, 0, 0)
            for offset, kind, length, location in ((32, 6, 168, 68), (44, 7, 56, 236), (56, 15, 24, 292)):
                struct.pack_into("<III", data, offset, kind, length, location)
            struct.pack_into("<IIIIQQIIQQ", data, 68, 7, 0, 0xC0000005, 0, 0, 0x12345678, 2, 0, 1, 0x34567000)
            struct.pack_into("<II", data, 228, 32, 316)
            struct.pack_into("<H", data, 236, 12)
            struct.pack_into("<IIIIII", data, 292, 24, 1, 42, 0, 0, 0)
            path.write_bytes(data)
            value = event() | dict(exitCode=0, captured=False, exceptionCode=0,
                exceptionThreadId=0, exceptionAddress=0, firstChanceAvEnabled=True,
                firstChanceAvAttempted=True, firstChanceAvThreadId=7,
                firstChanceAvInstruction=0x12345678, firstChanceAvAddress=0x34567000,
                firstChanceAvOperation=1, firstChanceAvElapsedMs=53)
            receipt = root / "event.json"
            receipt.write_text(json.dumps(value))
            self.assertEqual(value, diagnostic.load_event(receipt, 0xAA64))
            report = diagnostic.collect_first_chance_av(path, root, value, 0xAA64)
            self.assertTrue(report["captured"])
            self.assertFalse(report["provesUnhandledCrash"])
            self.assertEqual(0, value["exitCode"])
            self.assertEqual(data, (root / "first-chance-av.dmp").read_bytes())
            for changes in (dict(firstChanceAvThreadId=8), dict(firstChanceAvInstruction=1),
                            dict(firstChanceAvAddress=0x34567001), dict(firstChanceAvOperation=8)):
                with self.subTest(changes=changes), self.assertRaises(ValueError):
                    diagnostic.collect_first_chance_av(path, root, value | changes, 0xAA64)
            for offset, fmt, number in ((236, "<H", 9), (100, "<I", 1), (228, "<I", 0), (232, "<I", 347)):
                bad = bytearray(data)
                struct.pack_into(fmt, bad, offset, number)
                path.write_bytes(bad)
                with self.subTest(offset=offset), self.assertRaises(ValueError):
                    diagnostic.collect_first_chance_av(path, root, value, 0xAA64)
            for changes in (dict(firstChanceAvEnabled=False), dict(firstChanceAvThreadId=0),
                            dict(firstChanceAvInstruction=0), dict(firstChanceAvAddress=0x10000),
                            dict(firstChanceAvOperation=2), dict(firstChanceAvElapsedMs=110001)):
                receipt.write_text(json.dumps(value | changes))
                with self.subTest(changes=changes), self.assertRaises(ValueError):
                    diagnostic.load_event(receipt, 0xAA64)


def native_controls(directory, architecture):
    if os.name != "nt": raise RuntimeError("Native controls require Windows")
    helper = directory / "ShowcaseNativeDebugger.exe"
    fixture = directory / "ShowcaseDebuggerFixture.exe"
    expected = {"arm64": 0xAA64, "x64": 0x8664}[architecture]
    if diagnostic.machine(helper) != expected or diagnostic.machine(fixture) != expected:
        raise RuntimeError("Native helper/fixture PE architecture mismatch")
    subprocess.run([str(helper), "--test-callbacks"], timeout=5, check=True)
    print(f"PASS native {architecture} debugger: callback contracts", flush=True)
    for mode, expected_exit, captures in (("handled", 0, False), ("exit", 17, False),
                                        ("access-violation", 0xC0000005, True), ("live-stack", 17, False),
                                        ("handled-nonnull-av", 0, False), ("unhandled-nonnull-av", 0xC0000005, True)):
        with tempfile.TemporaryDirectory(prefix="showcase-debugger-control-") as temp:
            root = Path(temp)
            app = root / f"ShowcaseIdle-{uuid.uuid4().hex}.exe"
            app.write_bytes(fixture.read_bytes())
            raw = root / "raw"
            raw.mkdir()
            receipt = root / "event.json"
            command = [str(helper), str(app), str(raw), str(receipt)]
            if mode == "live-stack":
                snapshot_raw = root / "stack-raw"
                snapshot_raw.mkdir()
                command += ["--snapshot-after-ms=100", str(snapshot_raw / "native-stack.dmp")]
            else:
                first_raw = root / "first-av-raw"
                first_raw.mkdir()
                command += ["--first-chance-av", str(first_raw / "first-chance-av.dmp")]
            result = subprocess.run(command,
                env=os.environ | {"SHOWCASE_DEBUGGER_FIXTURE": mode}, timeout=20, capture_output=True)
            # No sleep, retry, or dump-validation work before testing image release.
            app.unlink()
            value = diagnostic.load_event(receipt, expected)
            print(f"Native {architecture} {mode}: {json.dumps(value, sort_keys=True)}", flush=True)
            if result.returncode != expected_exit or value["exitCode"] != expected_exit or value["captured"] != captures:
                raise RuntimeError(f"Native {mode} mismatch: exit={result.returncode}; {value}")
            dumps = list(raw.iterdir())
            if captures:
                if len(dumps) != 1: raise RuntimeError("Expected exactly one owned exception dump")
                report = diagnostic.correlate_exception(dumps[0], value)
                print(f"Validated native {architecture} dump: {json.dumps(report, sort_keys=True)}", flush=True)
            elif dumps or value["exceptionCode"] != 0:
                raise RuntimeError("Handled/ordinary exit created false crash evidence")
            if mode == "live-stack":
                report = diagnostic.collect_snapshot(snapshot_raw / "native-stack.dmp", root, value, expected)
                if not report["captured"]: raise RuntimeError("Owned live thread was not captured")
                print(f"Validated native {architecture} live stack: {json.dumps(report, sort_keys=True)}", flush=True)
            else:
                report = diagnostic.collect_first_chance_av(first_raw / "first-chance-av.dmp", root, value, expected)
                if report["captured"] != mode.endswith("nonnull-av") or report["provesUnhandledCrash"]:
                    raise RuntimeError("First-chance evidence changed exception handling or misidentified a crash")
                print(f"Validated native {architecture} first chance: {json.dumps(report, sort_keys=True)}", flush=True)
            print(f"PASS native {architecture} debugger: {mode}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-directory", type=Path)
    parser.add_argument("--architecture", choices=("x64", "arm64"))
    args = parser.parse_args()
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Controls))
    if not result.wasSuccessful(): raise SystemExit(1)
    if args.native_directory:
        if not args.architecture: parser.error("--architecture is required for native controls")
        native_controls(args.native_directory.resolve(strict=True), args.architecture)
