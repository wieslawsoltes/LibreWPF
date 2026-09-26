#!/usr/bin/env python3
"""Offline runner controls only: synthetic receipts and tiny owned Python children.

These tests never launch Showcase and do not qualify rendering or idle cost.
"""

import copy
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock


SPEC = importlib.util.spec_from_file_location("showcase_idle_runner", Path(__file__).with_name("progpu-wpf-showcase-idle.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


def synthetic_receipt(hashes=None):
    hashes = hashes or {name: "a" * 64 for name in runner.ASSEMBLIES}
    phases = []
    for index, name in enumerate(runner.PHASES):
        resized = name == "native-resized"
        phases.append({
            "name": name, "stableIdentity": True,
            "logicalWidth": 900 if resized else 760, "logicalHeight": 640 if resized else 560,
            "pixelWidth": 1800 if resized else 1520, "pixelHeight": 1280 if resized else 1120,
            "dpiScale": 2.0, "scrollOffset": 40 if name in ("scrolled", "native-resized") else 0,
            "zeroClipWidth": 80, "zeroClipHeight": 0, "zeroClipIsEmpty": False,
            "deviceRecoveryCount": 0, "commandCount": 80, "drawCallCount": 40, "submissionCount": 1,
            "interval": {"framesBefore": 10 + index * 2, "framesAfter": 10 + index * 2,
                         "extraPresentations": 0, "wallMilliseconds": 2001.5,
                         "cpuMilliseconds": 12.5, "allocatedBytes": 12345,
                         "gen0Collections": 1, "gen1Collections": 0, "gen2Collections": 0},
        })
    return {"schemaVersion": 1, "success": True, "error": None, "uiRestored": True,
            "mode": "NativeMilWgpu", "sourceRoot": "Window", "phases": phases,
            "assemblyIdentities": [{"name": name, "mvid": "11111111-2222-3333-4444-555555555555", "sha256": hashes[name]}
                                   for name in runner.ASSEMBLIES]}


class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.hashes = {name: "a" * 64 for name in runner.ASSEMBLIES}

    def reject(self, mutate):
        receipt = synthetic_receipt()
        mutate(receipt)
        with self.assertRaises(runner.ContractError):
            runner.validate_receipt(receipt, self.hashes)

    def test_complete_receipt_and_unthresholded_process_metrics(self):
        receipt = synthetic_receipt()
        receipt["phases"][0]["interval"].update(cpuMilliseconds=100000, allocatedBytes=1000000000, gen2Collections=100)
        runner.validate_receipt(receipt, self.hashes)

    def test_all_phases_and_their_order_are_required(self):
        self.reject(lambda receipt: receipt["phases"].pop())
        self.reject(lambda receipt: receipt["phases"].reverse())
        self.reject(lambda receipt: receipt["phases"].__setitem__(1, copy.deepcopy(receipt["phases"][0])))

    def test_requires_actual_renderer_root_success_and_restoration(self):
        for key, value in (("schemaVersion", True), ("success", 1), ("success", False),
                           ("mode", "Managed"), ("sourceRoot", "Border"), ("uiRestored", False), ("error", "failure")):
            with self.subTest(key=key, value=value):
                self.reject(lambda receipt: receipt.__setitem__(key, value))
        self.reject(lambda receipt: receipt["phases"][0].__setitem__("stableIdentity", False))

    def test_exact_zero_and_genuine_interphase_presentation(self):
        for change in ({"framesAfter": 11, "extraPresentations": 1},
                       {"framesAfter": 11, "extraPresentations": 0}, {"framesBefore": 0},
                       {"extraPresentations": False}, {"framesAfter": 9}):
            with self.subTest(change=change):
                self.reject(lambda receipt: receipt["phases"][0]["interval"].update(change))
        self.reject(lambda receipt: receipt["phases"][1]["interval"].update(framesBefore=10, framesAfter=10))

    def test_short_nonfinite_negative_and_boolean_metrics_rejected(self):
        for key, values in {
            "wallMilliseconds": [1999.99, float("nan"), float("inf"), True],
            "cpuMilliseconds": [-1, float("inf"), False],
            "allocatedBytes": [-1, 0.5, True, 10 ** 1000],
            "gen0Collections": [-1, 0.5], "gen1Collections": [-1], "gen2Collections": [-1],
        }.items():
            for value in values:
                with self.subTest(key=key, value=value):
                    self.reject(lambda receipt: receipt["phases"][0]["interval"].__setitem__(key, value))

    def test_actual_geometry_clips_native_commands_and_recovery_required(self):
        for key, value in (("logicalWidth", 0), ("pixelHeight", True), ("dpiScale", 0),
                           ("dpiScale", float("nan")), ("scrollOffset", -1),
                           ("zeroClipWidth", 0), ("zeroClipHeight", 1), ("zeroClipIsEmpty", True),
                           ("commandCount", 0), ("drawCallCount", 0), ("submissionCount", 0)):
            with self.subTest(key=key, value=value):
                self.reject(lambda receipt: receipt["phases"][0].__setitem__(key, value))
        self.reject(lambda receipt: receipt["phases"][1].__setitem__("deviceRecoveryCount", 1))
        self.reject(lambda receipt: receipt["phases"][1].__setitem__("scrollOffset", 0))
        self.reject(lambda receipt: receipt["phases"][3].__setitem__("logicalHeight", 561))
        self.reject(lambda receipt: receipt["phases"][2].__setitem__("logicalWidth", 760))

    def test_source_identity_and_payload_hashes_are_mandatory(self):
        self.reject(lambda receipt: receipt["assemblyIdentities"].pop())
        for key, value in (("name", "System.Drawing.Common"), ("mvid", "not-a-guid"),
                           ("mvid", "00000000-0000-0000-0000-000000000000"), ("sha256", "b" * 64)):
            with self.subTest(key=key, value=value):
                self.reject(lambda receipt: receipt["assemblyIdentities"][0].__setitem__(key, value))
        self.reject(lambda receipt: receipt["assemblyIdentities"].__setitem__(1, receipt["assemblyIdentities"][0]))

    def test_json_duplicate_nonfinite_missing_oversized_and_symlink_receipts(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for index, content in enumerate((b'{"success":true,"success":false}', b'{"x":NaN}', b'[]', b' ' * (runner.MAX_RECEIPT_BYTES + 1))):
                path = root / str(index)
                path.write_bytes(content)
                with self.assertRaises(runner.ContractError):
                    runner.load_receipt(path)
            with self.assertRaises(runner.ContractError):
                runner.load_receipt(root / "missing")
            if os.name == "posix":
                link = root / "link"
                link.symlink_to(root / "0")
                with self.assertRaises(runner.ContractError):
                    runner.load_receipt(link)


class ProcessTests(unittest.TestCase):
    def test_real_child_failure_and_logs_are_preserved(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            outcome = {}
            code, timed_out = runner.run_child([sys.executable, "-c", "import sys; print('retained'); print('failure',file=sys.stderr); sys.exit(23)"],
                                              root, os.environ.copy(), root, outcome=outcome)
            self.assertEqual((23, False), (code, timed_out))
            self.assertEqual(23, outcome["childExitCode"])
            self.assertEqual("retained\n", (root / "stdout.log").read_text())
            self.assertEqual("failure\n", (root / "stderr.log").read_text())

    def test_timeout_retires_only_owned_child(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
            try:
                started = time.monotonic()
                code, timed_out = runner.run_child([sys.executable, "-c", "import time; time.sleep(30)"],
                                                  root, os.environ.copy(), root, timeout=0.15)
                self.assertTrue(timed_out)
                self.assertNotEqual(0, code)
                self.assertLess(time.monotonic() - started, 12)
                self.assertIsNone(unrelated.poll())
            finally:
                unrelated.kill()
                unrelated.wait(timeout=5)

    def test_existing_logs_are_never_overwritten(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "stdout.log").write_text("original evidence")
            with mock.patch.object(runner.subprocess, "Popen") as start:
                with self.assertRaises(FileExistsError):
                    runner.run_child([sys.executable], root, os.environ.copy(), root)
                start.assert_not_called()
            self.assertEqual("original evidence", (root / "stdout.log").read_text())

    @unittest.skipUnless(os.name == "posix", "POSIX process-group cancellation control")
    def test_interruption_cleans_owned_group_and_preserves_failure_status(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            outcome = {}
            def interrupt(signum, frame):
                raise runner.Interrupted(signum)

            previous = signal.signal(signal.SIGTERM, interrupt)
            timer = threading.Timer(0.15, lambda: os.kill(os.getpid(), signal.SIGTERM))
            timer.start()
            try:
                with self.assertRaises(runner.Interrupted):
                    runner.run_child([sys.executable, "-c", "import time; time.sleep(30)"],
                                     root, os.environ.copy(), root, outcome=outcome)
            finally:
                timer.cancel()
                timer.join(timeout=1)
                signal.signal(signal.SIGTERM, previous)
            self.assertIsNotNone(outcome["childExitCode"])
            self.assertNotEqual(0, outcome["childExitCode"])


class RunnerTests(unittest.TestCase):
    def exercise(self, mutate=None, child_code=0, corrupt_payload=False):
        with tempfile.TemporaryDirectory() as temporary:
            # The real runner resolves its app and evidence parent. Match that
            # filesystem identity on hosts where /var aliases /private/var.
            parent = Path(temporary).resolve()
            app_root = parent / "prebuilt"
            app_root.mkdir()
            for name in runner.ASSEMBLIES:
                (app_root / f"{name}.dll").write_bytes(f"synthetic offline payload {name}".encode())
            hashes = runner.payload_hashes(app_root)
            receipt = synthetic_receipt(hashes)
            if mutate:
                mutate(receipt)

            def fake_child(command, cwd, environment, directory, **kwargs):
                self.assertEqual(str(app_root), str(cwd))
                self.assertEqual("1", environment["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_VALIDATE"])
                path = Path(environment["PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_STATUS_PATH"])
                runner.write_json_new(path, receipt)
                if corrupt_payload:
                    (app_root / "PresentationCore.dll").write_bytes(b"changed after launch")
                kwargs["outcome"].update(childExitCode=child_code, timedOut=False, cleanupErrors=[])
                return child_code, False

            with mock.patch.dict(os.environ, {}, clear=True), mock.patch.object(runner, "run_child", side_effect=fake_child):
                result = runner.run(app_root / "ProGPU.Wpf.ShowcaseApp.dll", sys.executable, parent)
            runs = list(parent.glob("showcase-idle-*"))
            self.assertEqual(1, len(runs))
            self.assertTrue((runs[0] / "launch.json").is_file())
            self.assertTrue((runs[0] / "application-receipt.json").is_file())
            metadata = json.loads((runs[0] / "runner-receipt.json").read_text())
            self.assertEqual(result, metadata["runnerExitCode"])
            return result, metadata

    def test_synthetic_valid_receipt_is_accepted_without_app_execution(self):
        result, metadata = self.exercise()
        self.assertEqual(0, result)
        self.assertTrue(metadata["success"])
        self.assertEqual(120, metadata["timeoutSeconds"])

    def test_child_failure_is_not_replaced_by_success_receipt(self):
        result, metadata = self.exercise(child_code=23)
        self.assertEqual(23, result)
        self.assertFalse(metadata["success"])

    def test_zero_child_exit_does_not_hide_failed_receipt(self):
        result, metadata = self.exercise(lambda receipt: receipt.update(success=False, error="source remains dirty"))
        self.assertEqual(1, result)
        self.assertIn("source remains dirty", metadata["error"])

    def test_modified_payload_rejects_even_valid_receipt(self):
        result, metadata = self.exercise(corrupt_payload=True)
        self.assertEqual(1, result)
        self.assertIn("payload changed", metadata["error"])

    def test_json_output_never_overwrites_existing_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "evidence.json"
            runner.write_json_new(path, {"original": True})
            with self.assertRaises(FileExistsError):
                runner.write_json_new(path, {"replacement": True})
            self.assertEqual({"original": True}, json.loads(path.read_text()))

    def test_conflicting_validation_mode_fails_before_child_launch(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app = root / "ProGPU.Wpf.ShowcaseApp.dll"
            app.write_bytes(b"offline preflight input; never executed")
            for name in runner.CONFLICTING_MODES:
                with self.subTest(name=name), mock.patch.dict(os.environ, {name: "1"}, clear=True), \
                        mock.patch.object(runner, "run_child") as launch:
                    with self.assertRaises(runner.ContractError):
                        runner.run(app, sys.executable, root)
                    launch.assert_not_called()
            self.assertEqual([app], list(root.iterdir()))


if __name__ == "__main__":
    unittest.main()
