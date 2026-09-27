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
    return dict(schemaVersion=1, diagnosticOnly=True, processId=42, machine=0xAA64,
                exitCode=0xC0000005, exited=True, exceptionCode=0xC0000005,
                exceptionThreadId=7, exceptionAddress=0x12345678, captured=True,
                dumpError=0, loopError=0)


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
                           dict(exceptionCode=0), dict(dumpError=5), dict(schemaVersion=True)):
                path.write_text(json.dumps(event() | change), encoding="utf-8")
                with self.subTest(change=change), self.assertRaises(ValueError):
                    diagnostic.load_event(path, 0xAA64)
            path.write_text(" " * 4097, encoding="utf-8")
            with self.assertRaises(ValueError): diagnostic.load_event(path, 0xAA64)

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
                 mock.patch.object(runner, "payload_hashes", return_value={}), \
                 mock.patch.object(diagnostic, "machine", return_value=0xAA64), \
                 mock.patch.object(crash, "WindowsRegistry"), mock.patch.object(crash, "Capture") as capture, \
                 mock.patch.object(runner, "run_child", side_effect=child), \
                 mock.patch.object(runner, "finish_crash_capture", side_effect=finish), \
                 mock.patch.object(runner, "collect_failure_events"), \
                 mock.patch.object(diagnostic, "replay", side_effect=replay) as replay_call:
                capture.return_value.prepare.return_value = app
                self.assertEqual(runner.run(app, None, root, True, debugger), original_code)
                replay_call.assert_called_once()
                fail_replay = False
                self.assertEqual(runner.run(app, None, root, True, debugger), original_code)
                self.assertEqual(replay_call.call_count, 2)


def native_controls(directory, architecture):
    if os.name != "nt": raise RuntimeError("Native controls require Windows")
    helper = directory / "ShowcaseNativeDebugger.exe"
    fixture = directory / "ShowcaseDebuggerFixture.exe"
    expected = {"arm64": 0xAA64, "x64": 0x8664}[architecture]
    if diagnostic.machine(helper) != expected or diagnostic.machine(fixture) != expected:
        raise RuntimeError("Native helper/fixture PE architecture mismatch")
    subprocess.run([str(helper), "--test-callbacks"], timeout=5, check=True)
    print(f"PASS native {architecture} debugger: callback contracts", flush=True)
    for mode, expected_exit, captures in (("handled", 0, False), ("exit", 17, False), ("access-violation", 0xC0000005, True)):
        with tempfile.TemporaryDirectory(prefix="showcase-debugger-control-") as temp:
            root = Path(temp)
            app = root / f"ShowcaseIdle-{uuid.uuid4().hex}.exe"
            app.write_bytes(fixture.read_bytes())
            raw = root / "raw"
            raw.mkdir()
            receipt = root / "event.json"
            result = subprocess.run([str(helper), str(app), str(raw), str(receipt)],
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
