#!/usr/bin/env python3
"""Authored pure receipt/admission controls; importing does not load a desktop provider."""

import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock


SPEC = importlib.util.spec_from_file_location("wpf_modal_desktop", Path(__file__).parents[1] / "progpu-wpf-modal-desktop.py")
DRIVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DRIVER)


class DesktopReceiptTests(unittest.TestCase):
    def test_duplicate_and_nonfinite_fields_are_not_receipts(self):
        for raw in (b'{"pid":1,"pid":1}', b'{"x":NaN}', b'{"x":Infinity}', b'{"x":-Infinity}'):
            with self.subTest(raw=raw), self.assertRaises(RuntimeError):
                DRIVER.parse(raw)

    def test_observation_budget_is_preserved(self):
        with self.assertRaises(RuntimeError):
            DRIVER.parse(b" " * (DRIVER.LIMIT + 1))

    def test_duplicate_options_are_rejected_before_launch(self):
        for argv in (("--platform", "windows", "--platform=x11"),
                     ("--portable-app=a", "--portable-app", "b")):
            with self.subTest(argv=argv), self.assertRaises(RuntimeError):
                DRIVER.unique_options(argv)
        DRIVER.unique_options(["--platform", "windows", "--portable-app", "a"])

    def test_rectangle_rejects_boolean_nonfinite_and_empty(self):
        for change in ({"x": True}, {"y": float("inf")}, {"width": 0}, {"height": -1}):
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                DRIVER.rectangle(dict(x=0, y=0, width=20, height=10) | change)

    def test_snapshot_pid_run_title_sequence_are_exact(self):
        run = "a" * 32
        original = dict(schema="wpf-modal-desktop-v1", qualified=False, pid=17, run=run,
                        title=f"WpfModalInteractionApp [{run}]", sequence=1)
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary).resolve()
            path = directory / "snapshot-00000001.json"
            for change in ({"pid": 18}, {"run": "b" * 32}, {"title": "another"},
                           {"sequence": 2}, {"qualified": True}):
                path.write_text(json.dumps(original | change))
                with self.subTest(change=change), self.assertRaises(RuntimeError):
                    DRIVER.observation(directory, 17, run)
            path.write_text(json.dumps(original))
            state, receipt = DRIVER.observation(directory, 17, run)
            self.assertEqual(state, original)
            self.assertEqual(receipt["sha256"], DRIVER.digest(path))

    def test_receipt_never_overwrites_prior_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary).resolve() / "receipt.json"
            DRIVER.write(path, {"qualified": False})
            with self.assertRaises(FileExistsError):
                DRIVER.write(path, {"qualified": True})
            self.assertEqual(DRIVER.parse(path.read_bytes()), {"qualified": False})

    def test_owner_invariance_keeps_real_input_but_not_dialog_counts(self):
        state = dict(ownerText="owner", comboSelection=0,
                     counts={"owner-pointer": 2, "owner-key": 1, "guard-action": 0,
                             "message-open-request": 1, "dialog-pointer": 3})
        baseline = DRIVER.owner_input(state)
        changed = copy.deepcopy(state)
        changed["counts"]["dialog-pointer"] += 1
        self.assertEqual(baseline, DRIVER.owner_input(changed))
        for key in ("owner-pointer", "owner-key", "guard-action", "editor-text"):
            changed = copy.deepcopy(state)
            changed["counts"][key] = changed["counts"].get(key, 0) + 1
            self.assertNotEqual(baseline, DRIVER.owner_input(changed))
        changed = copy.deepcopy(state)
        changed["ownerText"] = "changed"
        self.assertNotEqual(baseline, DRIVER.owner_input(changed))

    def test_guard_requires_actual_exposed_owner_client(self):
        owner = dict(id=1, client=dict(x=0, y=0, width=100, height=100), bounds=dict(x=0, y=0, width=100, height=100))
        child = dict(id=2, bounds=dict(x=40, y=20, width=60, height=60))
        identity = lambda value: value["id"]
        DRIVER.exposed_guard(owner, [owner, child], dict(x=1, y=85, width=20, height=10), identity)
        for target in (dict(x=50, y=30, width=10, height=10), dict(x=-1, y=85, width=20, height=10)):
            with self.subTest(target=target), self.assertRaises(RuntimeError):
                DRIVER.exposed_guard(owner, [owner, child], target, identity)

    def test_frames_may_advance_but_geometry_may_not(self):
        state = dict(sequence=1, elapsedMs=1, windows=[dict(surfaces=[dict(presentedFrames=4, nativeHandle=5, client={"x": 2})])])
        newer = copy.deepcopy(state)
        newer["sequence"] = 2
        newer["elapsedMs"] = 2
        newer["windows"][0]["surfaces"][0]["presentedFrames"] = 5
        self.assertEqual(DRIVER.stable(state), DRIVER.stable(newer))
        newer["windows"][0]["surfaces"][0]["client"]["x"] = 3
        self.assertNotEqual(DRIVER.stable(state), DRIVER.stable(newer))

    def test_original_absolute_deadline_not_restarted_by_native_setup(self):
        process = mock.Mock()
        process.poll.return_value = None
        session = DRIVER.Session(None, process, Path("/unused"), "run", True, 60)
        with mock.patch.object(DRIVER.time, "monotonic", return_value=61), self.assertRaisesRegex(RuntimeError, "60-second"):
            session.remaining()

    def test_wrong_helper_head_is_rejected_before_import(self):
        with tempfile.TemporaryDirectory() as temporary:
            with mock.patch.object(DRIVER.subprocess, "run", return_value=mock.Mock(stdout=b"b" * 40 + b"\n")):
                with self.assertRaisesRegex(RuntimeError, "exact requested commit"):
                    DRIVER.load_helper(Path(temporary).resolve(), "a" * 40)


if __name__ == "__main__":
    unittest.main()
