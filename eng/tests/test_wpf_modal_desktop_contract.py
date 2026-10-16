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

    def test_ordinary_initial_activation_keeps_the_existing_provider_call(self):
        desktop = mock.Mock()
        session = DRIVER.Session(desktop, None, Path('/unused'), 'run', False, 60)
        owner = {'hwnd': 7}
        session.activate(owner)
        desktop.activate.assert_called_once_with(owner)
        self.assertEqual(0, session.actions)

    def test_hosted_activation_preserves_deadline_and_counts_the_caption_action(self):
        process = mock.Mock(pid=12)
        process.poll.return_value = None
        desktop = mock.Mock()
        desktop.window_identity.side_effect = lambda window: window['hwnd']
        module = mock.Mock()
        def activate(hwnd, pid, deadline, record, environment):
            self.assertEqual((7, 12, 60), (hwnd, pid, deadline))
            record.update(clickCount=1, after=7)
        module.Desktop.return_value.activate_ci_window.side_effect = activate
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for name in ('stdout.txt', 'stderr.txt'): (directory/name).touch()
            session = DRIVER.Session(desktop, process, directory, 'run', False, 60, True)
            session.owner_identity = 7
            with mock.patch.object(DRIVER.time, 'monotonic', return_value=20), \
                 mock.patch.object(DRIVER.importlib.util, 'spec_from_file_location', return_value=mock.Mock()), \
                 mock.patch.object(DRIVER.importlib.util, 'module_from_spec', return_value=module):
                session.activate({'hwnd': 7})
                self.assertEqual(1, session.actions)
                self.assertEqual(60, session.deadline)
                module.Desktop.assert_called_once_with(adopt_physical_coordinates=True)
                self.assertEqual('hosted-caption', json.loads((directory/'input-001.json').read_text())['kind'])
                with self.assertRaisesRegex(RuntimeError, 'startup-only'):
                    session.activate({'hwnd': 7})
                module.Desktop.return_value.activate_ci_window.assert_called_once()
                desktop.activate.assert_not_called()

    def test_hosted_activation_rejects_replaced_owner_before_loading_native_input(self):
        session = DRIVER.Session(mock.Mock(), None, Path('/unused'), 'run', True, 60, True)
        session.owner_identity = 7
        session.desktop.window_identity.return_value = 8
        with mock.patch.object(session, 'remaining'), \
             mock.patch.object(DRIVER.importlib.util, 'spec_from_file_location') as load:
            with self.assertRaisesRegex(RuntimeError, 'identity changed'):
                session.activate({'hwnd': 8})
            load.assert_not_called()

    def test_shell_escape_keeps_fresh_original_owner_events_and_rejects_leaked_keys(self):
        for leaked in (False, True):
            with self.subTest(leaked=leaked), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                process = mock.Mock(pid=12)
                session = DRIVER.Session(mock.Mock(), process, directory, 'run', False, 60, True, True)
                session.state = dict(ownerText='owner', comboSelection=0, counts={'owner-key': 0},
                                     phase='owner', sequence=1, windows=[])
                latest = copy.deepcopy(session.state); latest['sequence'] = 10
                first = copy.deepcopy(latest); first['sequence'] = 11
                if leaked: first['counts']['owner-key'] = 1
                second = copy.deepcopy(first); second['sequence'] = 12
                native, guard = mock.Mock(), mock.Mock()
                def dismiss(observer, sender, claim, deadline, record, environment, authorized):
                    self.assertTrue(authorized)
                    self.assertIs(sender, session.desktop)
                    self.assertEqual(60, deadline)
                    record.update(escapes=[{'key': 27}], setupDeadline=23)
                    claim('hosted-shell-escape', nativeProof={'key': 27})
                guard.dismiss.side_effect = dismiss
                with mock.patch.object(session, 'remaining', return_value=40), \
                     mock.patch.object(session, 'native', return_value=[]), \
                     mock.patch.object(session, 'match') as match, \
                     mock.patch.object(DRIVER, 'observation', side_effect=[(latest, {}), (first, {}), (second, {})]), \
                     mock.patch.object(DRIVER.time, 'monotonic', return_value=20), \
                     mock.patch.object(DRIVER.time, 'sleep'), \
                     mock.patch.object(DRIVER.importlib.util, 'spec_from_file_location', return_value=mock.Mock()), \
                     mock.patch.object(DRIVER.importlib.util, 'module_from_spec', side_effect=[native, guard]):
                    if leaked:
                        with self.assertRaisesRegex(RuntimeError, 'leaked input'): session.dismiss_hosted_shell()
                        match.assert_not_called()
                    else:
                        session.dismiss_hosted_shell()
                        self.assertEqual(12, session.state['sequence'])
                        match.assert_called_once()
                    self.assertEqual(1, session.actions)
                    self.assertEqual(60, session.deadline)
                receipt = json.loads((directory/'startup-shell.json').read_text())
                self.assertFalse(receipt['qualified'])
                self.assertEqual(leaked, 'error' in receipt)

    def test_shell_escape_is_not_admitted_to_portable_or_later_actions(self):
        for portable, actions in ((True, 0), (False, 1)):
            session = DRIVER.Session(mock.Mock(), None, Path('/unused'), 'run', portable, 60, True, True)
            session.actions = actions
            with self.subTest(portable=portable, actions=actions), \
                 mock.patch.object(DRIVER.importlib.util, 'spec_from_file_location') as load:
                with self.assertRaisesRegex(RuntimeError, 'first reference'): session.dismiss_hosted_shell()
                load.assert_not_called()

    def test_retirement_diagnostic_cannot_replace_the_original_failed_assertion(self):
        with tempfile.TemporaryDirectory() as temporary:
            session = mock.Mock(directory=Path(temporary), state={'phase': 'owner'},
                                source_receipt={}, hosted_caption=True, deadline=60)
            session.native.side_effect = [[{'hwnd': 1}, {'hwnd': 2}], [{'hwnd': 1}]]
            with mock.patch.object(DRIVER.time, 'monotonic', side_effect=[20, 20.1, 20.1, 21.1]), \
                 mock.patch.object(DRIVER.time, 'sleep'):
                with self.assertRaisesRegex(RuntimeError, 'remain visible'):
                    DRIVER.check_native_retirement(session)
            receipt = json.loads((session.directory/'native-retirement.json').read_text())
            self.assertFalse(receipt['originalAssertionPassed'])
            self.assertEqual(2, len(receipt['nativeWindows']))
            self.assertEqual(1, len(receipt['observations'][0]['nativeWindows']))

    def test_paired_windows_runs_initialize_dpi_once_but_keep_distinct_sessions(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            app = root/'app.exe'; app.write_bytes(b'pure test app identity')
            args = mock.Mock(platform='windows', macos_helper=None,
                             hosted_windows_caption_activation=True, hosted_windows_shell_escape=True)
            observer = mock.Mock(spec=['windows', 'key', 'pointer'])
            first, second = mock.Mock(pid=11, returncode=0), mock.Mock(pid=22, returncode=0)
            first.poll.return_value = second.poll.return_value = 0
            sessions = []
            def scenario(session):
                sessions.append(session)
                session.actions = 22
            with mock.patch.object(DRIVER.subprocess, 'Popen', side_effect=[first, second]), \
                 mock.patch.object(DRIVER, 'make_desktop', return_value=observer) as factory, \
                 mock.patch.object(DRIVER, 'digest', return_value='test-hash'), \
                 mock.patch.object(DRIVER, 'scenario', side_effect=scenario), \
                 mock.patch.object(DRIVER.time, 'monotonic', side_effect=[10, 30]):
                shared = DRIVER.run_one(args, None, {}, app, root/'microsoft', False)
                DRIVER.run_one(args, None, {}, app, root/'portable', True, shared)
            factory.assert_called_once()
            self.assertEqual([11, 22], [s.process.pid for s in sessions])
            self.assertEqual([70, 90], [s.deadline for s in sessions])
            self.assertEqual([True, False], [s.hosted_shell for s in sessions])
            self.assertIs(sessions[0].desktop, sessions[1].desktop)
            self.assertNotEqual(sessions[0].run, sessions[1].run)
            self.assertTrue(all(json.loads((root/n/'result.json').read_text())['completed']
                                for n in ('microsoft', 'portable')))

    def test_source_native_frame_failure_records_exact_inputs_without_fitting(self):
        for changed in ({'width': 101}, {'x': 8}, {'width': 100.0000000001}):
            with self.subTest(changed=changed), tempfile.TemporaryDirectory() as temporary:
                desktop = mock.Mock()
                desktop.window_identity.side_effect = lambda window: window['hwnd']
                session = DRIVER.Session(desktop, None, Path(temporary), 'run', True, 60)
                frame = dict(x=0, y=0, width=100, height=100)
                surface = dict(visible=True, nativeHandle=1, sourceHandle=2, client=frame, presentedFrames=1)
                state = dict(title='owner', windows=[dict(title='owner', surfaces=[surface])])
                windows = [dict(hwnd=1, title='owner', client=frame | changed)]
                with mock.patch.object(DRIVER.sys, 'platform', 'win32'), \
                     self.assertRaisesRegex(RuntimeError, 'no fitted bounds'):
                    session.match(state, windows)
                receipt = json.loads((session.directory/'source-native-mismatch.json').read_text())
                self.assertEqual(frame, receipt['mismatchedSurface']['client'])
                self.assertEqual(frame | changed, receipt['matchedNativeWindows'][0]['client'])
                self.assertEqual(windows, receipt['nativeWindows'])


if __name__ == "__main__":
    unittest.main()
