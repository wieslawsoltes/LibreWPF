"""Offline admission controls only; importing these modules does not open a desktop."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parent


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


prepare = load("gallery_prepare", "prepare.py")
probe = load("gallery_probe", "run-windows.py")
prepare_ci = load("gallery_prepare_ci", "prepare-ci.py")
display_setup = load("gallery_display_setup", "prepare-hosted-display.py")


class HostedDisplayTests(unittest.TestCase):
    def mode(self, width=1024, height=768, **changes):
        mode = display_setup.DevMode()
        mode.size = 220; mode.fields = display_setup.REQUIRED_FIELDS
        mode.width = width; mode.height = height; mode.bits = 32; mode.frequency = 60
        for key, value in changes.items():
            setattr(mode, key, value)
        return mode

    def test_original_offscreen_failure_stays_rejected(self):
        original = {"pid": 5636, "frame": 3, "client": [50, 50, 884, 749],
                    "image": [79.35616438356163, 577, 195.28767123287673, 198]}
        crop = probe.admitted_crop(original, 5636, (50, 50, 884, 749))
        self.assertEqual((79, 577, 275, 775), crop)
        with self.assertRaisesRegex(RuntimeError, "entire image capture"):
            probe.require_monitor_coverage(crop, [(0, 0, 1024, 768)])
        probe.require_monitor_coverage(crop, [(0, 0, 1280, 1024)])

    def test_smallest_actual_compatible_mode_selected(self):
        modes = [self.mode(1920, 1080), self.mode(1280, 1024), self.mode(1280, 960)]
        selected = display_setup.select_mode(self.mode(), modes, 96)
        self.assertIs(modes[1], selected)
        current = self.mode(1920, 1080)
        self.assertIs(current, display_setup.select_mode(current, modes, 96))

    def test_native_color_timing_orientation_and_position_are_preserved(self):
        for key, value in (("bits", 24), ("frequency", 59), ("flags", 2),
                           ("orientation", 1), ("fixedOutput", 1), ("x", 1), ("y", 1)):
            with self.subTest(key=key), self.assertRaisesRegex(RuntimeError, "No enumerated mode"):
                display_setup.select_mode(self.mode(), [self.mode(1920, 1080, **{key: value})], 96)

    def test_dpi_requires_real_larger_mode_without_scaling_original_pixels(self):
        with self.assertRaisesRegex(RuntimeError, "No enumerated mode"):
            display_setup.select_mode(self.mode(), [self.mode(1280, 1024)], 144)
        large = self.mode(1920, 1536)
        self.assertIs(large, display_setup.select_mode(self.mode(), [large], 144))

    def test_windows_abi_and_original_mode_round_trip(self):
        self.assertEqual(220, display_setup.C.sizeof(display_setup.DevMode))
        self.assertEqual(840, display_setup.C.sizeof(display_setup.DisplayDevice))
        self.assertEqual(72, display_setup.DevMode.fields.offset)
        self.assertEqual(172, display_setup.DevMode.width.offset)
        self.assertEqual(184, display_setup.DevMode.frequency.offset)
        original = bytes(self.mode()).hex()
        self.assertEqual(original, bytes(display_setup.decode_mode(original)).hex())
        for mode in (self.mode(driverExtra=4), self.mode(fields=0), self.mode(size=219)):
            with self.subTest(mode=bytes(mode).hex()), self.assertRaises(RuntimeError):
                display_setup.decode_mode(bytes(mode).hex())

    def test_display_setup_rejects_every_missing_or_foreign_host_boundary(self):
        environment = dict(display_setup.ENVIRONMENT)
        display_setup.require_hosted(environment)
        for key in environment:
            for value in (None, "another-value"):
                changed = dict(environment); changed[key] = value
                with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                    display_setup.require_hosted(changed)


class AdmissionTests(unittest.TestCase):
    def test_manual_clipboard_replacement_still_requires_explicit_selection(self):
        self.assertEqual("--allow-replace-clipboard", probe.clipboard_mode(True, False, {}))
        for allow, empty in ((False, False), (True, True)):
            with self.subTest(allow=allow, empty=empty), self.assertRaises(RuntimeError):
                probe.clipboard_mode(allow, empty, {})

    def test_empty_ci_mode_rejects_local_self_hosted_and_foreign_runners(self):
        environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
                       "RUNNER_OS": "Windows", "GITHUB_REPOSITORY": "wieslawsoltes/LibreWPF"}
        self.assertEqual("--require-empty-ci-clipboard", probe.clipboard_mode(False, True, environment))
        for key in environment:
            changed = dict(environment); changed.pop(key)
            with self.subTest(missing=key), self.assertRaises(RuntimeError):
                probe.clipboard_mode(False, True, changed)
        for key, value in (("RUNNER_ENVIRONMENT", "self-hosted"), ("RUNNER_OS", "Linux"),
                           ("GITHUB_REPOSITORY", "another/repository")):
            changed = dict(environment); changed[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                probe.clipboard_mode(False, True, changed)

    def snapshot(self):
        return {"pid": 12, "frame": 2, "client": [-500, 20, 400, 300], "image": [-480, 60, 198, 198],
                "gpuCompletion": {"frame": 2, "submission": 8, "deviceRecovery": 0}}

    def test_capture_requires_real_completion_receipt_for_same_frame(self):
        probe.require_gpu_completion(self.snapshot())
        original = self.snapshot(); original.pop("gpuCompletion")
        with self.assertRaisesRegex(RuntimeError, "completion receipt"):
            probe.require_gpu_completion(original)
        for value in (2.0, True, None):
            changed = self.snapshot(); changed["frame"] = value
            with self.subTest(sourceFrame=value), self.assertRaises(RuntimeError):
                probe.require_gpu_completion(changed)
        for key, values in {"frame": [0, 1, 3, True, None],
                            "submission": [0, -1, True, 2**64, None],
                            "deviceRecovery": [-1, True, 2**63, None]}.items():
            for value in values:
                changed = self.snapshot(); changed["gpuCompletion"][key] = value
                with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                    probe.require_gpu_completion(changed)

    def test_exact_native_client_and_negative_origin(self):
        self.assertEqual((-480, 60, -282, 258), probe.admitted_crop(self.snapshot(), 12, (-500, 20, 400, 300)))

    def test_foreign_process(self):
        with self.assertRaisesRegex(RuntimeError, "PID"):
            probe.admitted_crop(self.snapshot(), 13, (-500, 20, 400, 300))

    def test_no_frame(self):
        value = self.snapshot(); value["frame"] = 0
        with self.assertRaisesRegex(RuntimeError, "frame"):
            probe.admitted_crop(value, 12, (-500, 20, 400, 300))

    def test_no_dpi_ratio_repair(self):
        with self.assertRaisesRegex(RuntimeError, "mismatch"):
            probe.admitted_crop(self.snapshot(), 12, (-1000, 40, 800, 600))

    def test_partial_image_is_not_cropped_into_success(self):
        value = self.snapshot(); value["image"][0] = -501
        with self.assertRaisesRegex(RuntimeError, "entire"):
            probe.admitted_crop(value, 12, (-500, 20, 400, 300))

    def test_nonfinite_image(self):
        value = self.snapshot(); value["image"][0] = float("nan")
        with self.assertRaisesRegex(RuntimeError, "nonfinite"):
            probe.admitted_crop(value, 12, (-500, 20, 400, 300))

    def test_empty_image(self):
        value = self.snapshot(); value["image"][2] = 0
        with self.assertRaisesRegex(RuntimeError, "entire"):
            probe.admitted_crop(value, 12, (-500, 20, 400, 300))

    def test_physical_monitor_coverage_handles_negative_origin_and_adjoining_displays(self):
        probe.require_monitor_coverage((-480, 60, -282, 258), [(-500, 0, 0, 300)])
        probe.require_monitor_coverage((-100, 20, 100, 220), [(-500, 0, 0, 300), (0, 0, 500, 300)])

    def test_monitor_bounding_box_cannot_hide_a_gap_or_offscreen_pixels(self):
        for crop, monitors in (
            ((-100, 20, 100, 220), [(-500, 0, 0, 300), (1, 0, 500, 300)]),
            ((0, -1, 100, 220), [(0, 0, 500, 300)]),
            ((400, 20, 501, 220), [(0, 0, 500, 300)]),
        ):
            with self.subTest(crop=crop, monitors=monitors), self.assertRaisesRegex(RuntimeError, "entire image capture"):
                probe.require_monitor_coverage(crop, monitors)

    def test_empty_excessive_or_degenerate_monitor_inventory_rejected(self):
        for monitors in ([], [(0, 0, 100, 100)] * 17, [(0, 0, 0, 100)]):
            with self.subTest(monitors=monitors), self.assertRaises(RuntimeError):
                probe.require_monitor_coverage((0, 0, 50, 50), monitors)

    def test_original_manifest_includes_source_asset_and_license(self):
        value = json.loads((ROOT / "source-manifest.json").read_text())
        self.assertEqual("811d01e95c8c929e68539d698d0a0609e94fd185", value["commit"])
        files = {entry["path"]: entry for entry in value["files"]}
        self.assertEqual(12, len(files))
        self.assertIn("LICENSE", files)
        self.assertEqual("f1c9a6e709e0f7fcaf048f06074dbd7d2addcdc7576e82efe618632f433ee799",
                         files["Assets/ControlImages/Clipboard.png"]["sha256"])
        self.assertIn("Views/System/ClipboardPage.xaml.cs", files)


class ForegroundTests(unittest.TestCase):
    class NativeWindow:
        def __init__(self, foreground=(9, 7), accepted=True):
            self.foreground = iter(foreground)
            self.last_foreground = 9
            self.accepted = accepted
            self.owner = 12
            self.live = True
            self.visible = True
            self.iconic = False
            self.requests = []

        def IsWindow(self, hwnd): return self.live and hwnd == 7
        def IsWindowVisible(self, hwnd): return self.visible
        def IsIconic(self, hwnd): return self.iconic
        def GetWindowThreadProcessId(self, hwnd, owner):
            owner._obj.value = self.owner
            return 1
        def GetForegroundWindow(self):
            self.last_foreground = next(self.foreground, self.last_foreground)
            return self.last_foreground
        def SetForegroundWindow(self, hwnd):
            self.requests.append(hwnd)
            return self.accepted

    def desktop(self, **options):
        desktop = probe.Desktop.__new__(probe.Desktop)
        desktop.u = self.NativeWindow(**options)
        return desktop

    def prepare(self, desktop, record, snapshot=None, deadline=100):
        desktop.prepare_foreground(snapshot or {"hwnd": 7, "pid": 12}, 12, deadline, record)

    def host_activation(self):
        return {"pid": 12, "hwnd": 7, "before": 9, "requested": True,
                "accepted": True, "after": 7, "foregroundPid": 12, "requestCount": 1}

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_host_activation_never_adds_a_driver_request(self, _):
        desktop = self.desktop(foreground=(7, 7)); record = {}
        desktop.prepare_foreground({"hwnd": 7, "pid": 12}, 12, 100, record, self.host_activation())
        self.assertEqual([], desktop.u.requests)
        self.assertEqual(7, record["after"])

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_rejected_or_foreign_host_receipt_cannot_use_coincident_foreground(self, _):
        for key, value in (("pid", 13), ("hwnd", 8), ("accepted", False), ("after", 9),
                           ("foregroundPid", 13), ("requestCount", 2), ("requested", False)):
            receipt = self.host_activation(); receipt[key] = value
            desktop = self.desktop(foreground=(7, 7))
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                desktop.prepare_foreground({"hwnd": 7, "pid": 12}, 12, 100, {}, receipt)
            self.assertEqual([], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_foreground_lost_after_host_receipt_does_not_trigger_a_second_request(self, _):
        desktop = self.desktop(foreground=(9, 9))
        with self.assertRaisesRegex(RuntimeError, "lost foreground"):
            desktop.prepare_foreground({"hwnd": 7, "pid": 12}, 12, 100, {}, self.host_activation())
        self.assertEqual([], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_already_foreground_does_not_request_activation(self, _):
        desktop = self.desktop(foreground=(7, 7)); record = {}
        self.prepare(desktop, record)
        self.assertEqual([], desktop.u.requests)
        self.assertEqual(7, record["after"])
        self.assertFalse(record["requested"])

    @patch.object(probe.time, "sleep")
    @patch.object(probe.time, "monotonic", return_value=20)
    def test_one_request_waits_for_actual_cross_thread_foreground(self, _, sleep):
        desktop = self.desktop(foreground=(9, 9, 7)); record = {}
        self.prepare(desktop, record)
        self.assertEqual([7], desktop.u.requests)
        self.assertTrue(record["accepted"])
        self.assertEqual(7, record["after"])
        sleep.assert_called_once_with(0.025)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_foreign_dead_hidden_or_minimized_window_never_receives_request(self, _):
        for name, value in (("owner", 13), ("live", False), ("visible", False), ("iconic", True)):
            desktop = self.desktop(); setattr(desktop.u, name, value)
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                self.prepare(desktop, {})
            self.assertEqual([], desktop.u.requests)
        desktop = self.desktop()
        with self.assertRaisesRegex(RuntimeError, "snapshot PID"):
            self.prepare(desktop, {}, {"hwnd": 7, "pid": 13})
        self.assertEqual([], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_rejected_request_is_retained_and_never_retried(self, _):
        desktop = self.desktop(accepted=False); record = {}
        with self.assertRaisesRegex(RuntimeError, "rejected"):
            self.prepare(desktop, record)
        self.assertEqual([7], desktop.u.requests)
        self.assertFalse(record["accepted"])
        self.assertEqual(7, record["afterRejectedRequest"])
        # Even a coincident foreground change cannot turn a denied request into
        # success. Keep the original failure and its diagnostic readback.
        self.assertEqual(12, record["foregroundPidAfterRejectedRequest"])

    @patch.object(probe.time, "sleep")
    @patch.object(probe.time, "monotonic", side_effect=(20, 20, 20.5, 21))
    def test_accepted_request_without_actual_foreground_stays_failure(self, _, sleep):
        desktop = self.desktop(foreground=(9,)); record = {}
        with self.assertRaisesRegex(RuntimeError, "budget"):
            self.prepare(desktop, record)
        self.assertEqual([7], desktop.u.requests)
        self.assertEqual(9, record["after"])
        sleep.assert_called_once_with(0.025)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_ownership_change_after_accepted_request_fails(self, _):
        desktop = self.desktop()
        def change_owner(hwnd):
            desktop.u.requests.append(hwnd); desktop.u.owner = 13
            return True
        desktop.u.SetForegroundWindow = change_owner
        with self.assertRaisesRegex(RuntimeError, "PID mismatch"):
            self.prepare(desktop, {})
        self.assertEqual([7], desktop.u.requests)

    @patch.object(probe.time, "monotonic", return_value=98)
    def test_outer_deadline_cannot_be_extended_by_activation(self, _):
        desktop = self.desktop()
        with self.assertRaisesRegex(RuntimeError, "original60-second"):
            self.prepare(desktop, {})
        self.assertEqual([], desktop.u.requests)

    def test_later_capture_does_not_reactivate_a_background_window(self):
        desktop = self.desktop(foreground=(9,))
        with self.assertRaisesRegex(RuntimeError, "not foreground"):
            desktop.geometry(7, 12)
        self.assertEqual([], desktop.u.requests)


class CiCaptionTests(unittest.TestCase):
    environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
                   "RUNNER_OS": "Windows", "GITHUB_REPOSITORY": "wieslawsoltes/LibreWPF"}

    class NativeWindow(ForegroundTests.NativeWindow):
        def __init__(self):
            super().__init__(foreground=(9,))
            self.inputs = []
            self.hit = 2
            self.point_owner = 7
            self.held = False
            self.capture = False
            self.button_count = 2
            self.activate = True
            self.cursor = (0, 0)

        def GetGUIThreadInfo(self, thread, info):
            info._obj.hwndCapture = 8 if self.capture else 0
            return 1
        def GetAsyncKeyState(self, key): return 0x8000 if self.held else 0
        def GetWindowRect(self, hwnd, rect):
            rect._obj.left, rect._obj.top, rect._obj.right, rect._obj.bottom = -500, 0, 400, 930
            return 1
        def ClientToScreen(self, hwnd, point):
            point._obj.x, point._obj.y = -492, 40
            return 1
        def WindowFromPoint(self, point): return self.point_owner
        def SendMessageTimeoutW(self, hwnd, message, wparam, lparam, flags, timeout, result):
            result._obj.value = self.hit
            return 1
        def GetSystemMetrics(self, index): return {76: -500, 77: 0, 78: 1500, 79: 1000}[index]
        def GetCursorPos(self, point):
            point._obj.x, point._obj.y = self.cursor
            return 1
        def SendInput(self, count, values, size):
            array = probe.C.cast(values, probe.C.POINTER(probe.Input))
            self.inputs.append([array[i].mi.dwFlags for i in range(count)])
            if array[0].mi.dwFlags == 0xc001:
                self.cursor = (-500 + array[0].mi.dx * 1500 // 65536,
                               array[0].mi.dy * 1000 // 65536)
            if count == 2:
                if self.activate:
                    self.foreground = iter((7,))
                return self.button_count
            return count

    def desktop(self):
        desktop = probe.Desktop.__new__(probe.Desktop)
        desktop.u = self.NativeWindow()
        desktop.monitors = lambda crop: probe.require_monitor_coverage(crop, [(-500, 0, 1000, 1000)])
        return desktop

    def run_case(self, desktop, record=None, snapshot=None, environment=None):
        record = {} if record is None else record
        probe.Desktop.activate_ci_caption(desktop, snapshot or {"pid": 12, "hwnd": 7, "frame": 1}, 12, 100,
                                          record, self.environment if environment is None else environment)
        return record

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_one_caption_click_uses_exact_negative_physical_point_and_foreground_readback(self, _):
        desktop = self.desktop()
        record = self.run_case(desktop)
        self.assertEqual([[0xc001], [2, 4]], desktop.u.inputs)
        self.assertEqual([-50, 20], record["point"])
        self.assertEqual((-50, 20), desktop.u.cursor)
        self.assertEqual((1, 7), (record["clickCount"], record["after"]))
        self.assertEqual([], desktop.u.requests)

    def test_local_and_self_hosted_input_rejected_before_any_native_query(self):
        for environment in ({}, dict(self.environment, RUNNER_ENVIRONMENT="self-hosted")):
            with self.subTest(environment=environment), self.assertRaisesRegex(RuntimeError, "GitHub-hosted"):
                self.run_case(None, environment=environment)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_foreign_unpresented_hidden_and_dead_targets_never_receive_input(self, _):
        for snapshot in ({"pid": 13, "hwnd": 7, "frame": 1}, {"pid": 12, "hwnd": 7, "frame": 0}):
            desktop = self.desktop()
            with self.subTest(snapshot=snapshot), self.assertRaises(RuntimeError):
                self.run_case(desktop, snapshot=snapshot)
            self.assertEqual([], desktop.u.inputs)
        for name, value in (("owner", 13), ("live", False), ("visible", False), ("iconic", True)):
            desktop = self.desktop(); setattr(desktop.u, name, value)
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                self.run_case(desktop)
            self.assertEqual([], desktop.u.inputs)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_buttons_resize_client_obstruction_held_input_and_capture_rejected(self, _):
        for name, value in (("hit", 20), ("hit", 9), ("hit", 12), ("hit", 1),
                            ("point_owner", 8), ("held", True), ("capture", True)):
            desktop = self.desktop(); setattr(desktop.u, name, value)
            with self.subTest(name=name, value=value), self.assertRaises(RuntimeError):
                self.run_case(desktop)
            self.assertEqual([], desktop.u.inputs)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_caption_covered_after_move_never_receives_press(self, _):
        desktop = self.desktop()
        move = desktop.u.SendInput
        def obstruct(*args):
            result = move(*args)
            desktop.u.point_owner = 8
            return result
        desktop.u.SendInput = obstruct
        with self.assertRaisesRegex(RuntimeError, "caption ownership|caption.*changed"):
            self.run_case(desktop)
        self.assertEqual([[0xc001]], desktop.u.inputs)

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_partial_press_is_released_and_never_retried_or_accepted(self, _):
        desktop = self.desktop(); desktop.u.button_count = 1
        record = {}
        with self.assertRaisesRegex(RuntimeError, "not inserted completely"):
            self.run_case(desktop, record)
        self.assertEqual([[0xc001], [2, 4], [4]], desktop.u.inputs)
        self.assertEqual(1, record["partialPressReleaseInserted"])
        self.assertEqual(1, record["clickCount"])

    @patch.object(probe.time, "monotonic", return_value=20)
    def test_already_foreground_does_not_inject_or_request(self, _):
        desktop = self.desktop(); desktop.u.foreground = iter((7,))
        record = self.run_case(desktop)
        self.assertEqual([], desktop.u.inputs)
        self.assertEqual(0, record["clickCount"])

    @patch.object(probe.time, "monotonic", return_value=98)
    def test_expired_original_deadline_never_receives_input(self, _):
        desktop = self.desktop()
        with self.assertRaisesRegex(RuntimeError, "budget"):
            self.run_case(desktop)
        self.assertEqual([], desktop.u.inputs)

    @patch.object(probe.time, "sleep")
    @patch.object(probe.time, "monotonic", side_effect=(20, 20, 20, 20, 20, 21))
    def test_successfully_inserted_click_without_foreground_is_failure(self, _, sleep):
        desktop = self.desktop(); desktop.u.activate = False
        with self.assertRaisesRegex(RuntimeError, "did not activate"):
            self.run_case(desktop)
        self.assertEqual([[0xc001], [2, 4]], desktop.u.inputs)


class ProducerTests(unittest.TestCase):
    def test_original_ansi_log_is_captured_as_bytes_without_terminal_output(self):
        original = b"\xef\xbb\xbf2026-10-13T21:33:06Z \x1b[32moriginal build output\x1b[0m\r\n"
        with patch.object(prepare_ci.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, original, b"")) as run:
            self.assertEqual(original, prepare_ci.gh_job_log(37))
        command = run.call_args.args[0]
        self.assertEqual(["gh", "api", "--allow-escape-sequences",
                          "repos/wieslawsoltes/LibreWPF/actions/jobs/37/logs"], command)
        self.assertEqual(dict(check=True, capture_output=True, timeout=120), run.call_args.kwargs)

    def test_raw_upload_log_keeps_original_size_and_failure_gates(self):
        for raw in (b"", b"x" * (1024 * 1024 + 1)):
            with self.subTest(size=len(raw)), patch.object(prepare_ci.subprocess, "run",
                    return_value=subprocess.CompletedProcess([], 0, raw, b"")), self.assertRaisesRegex(ValueError, "bound"):
                prepare_ci.gh_job_log(37)
        with patch.object(prepare_ci.subprocess, "run", side_effect=subprocess.CalledProcessError(1, ["gh"])), \
                self.assertRaises(subprocess.CalledProcessError):
            prepare_ci.gh_job_log(37)
        with patch.object(prepare_ci.subprocess, "run") as run:
            for job_id in (0, -1, "37", True):
                with self.subTest(job_id=job_id), self.assertRaisesRegex(ValueError, "identity"):
                    prepare_ci.gh_job_log(job_id)
            run.assert_not_called()

    def test_private_build_disables_unrelated_first_run_side_effects(self):
        self.assertEqual({"DOTNET_GENERATE_ASPNET_CERTIFICATE": "false",
                          "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
                          "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}, prepare.BUILD_ENVIRONMENT)

    def test_explicit_rid_output_layout_matches_asset_verifier(self):
        for architecture in ("arm64", "x64"):
            command = prepare.build_command(None, Path("case.csproj"), architecture, Path("NuGet.config"), Path("packages"))
            self.assertEqual("win-" + architecture, command[command.index("-r") + 1])
            self.assertIn("-p:AppendRuntimeIdentifierToOutputPath=false", command)
            self.assertEqual(Path("bin/Release/net10.0-windows"), prepare.OUTPUT_RELATIVE)

    def receipts(self):
        head = "1" * 40
        success = {"status": "completed", "conclusion": "success"}
        build = dict(success, databaseId=1, name="LibreWPF Build", headSha=head,
                     jobs=[dict(success, name=name, steps=[dict(success, name="Upload CI package bundle")])
                           for name in sorted(prepare.BUILD_JOBS)])
        docs = dict(success, databaseId=2, name="LibreWPF Docs", headSha=head,
                    jobs=[dict(success, name="Verify release documentation")])
        artifact = {"name": "librewpf-ci-packages-" + head, "expired": False,
                    "workflow_run": {"id": 1, "head_sha": head}, "digest": "sha256:" + "2" * 64}
        return build, docs, artifact

    def validate(self, receipts):
        prepare.validate_producer(*receipts, "1" * 40, 1, 2)

    def test_whole_exact_producer_required(self):
        self.validate(self.receipts())

    def stale_upload(self):
        receipts = self.receipts()
        producer = next(j for j in receipts[0]["jobs"] if j["name"] == "SDK package and no-source-change smoke")
        producer.update(databaseId=37, steps=[{"name": "Upload CI package bundle", "status": "pending", "conclusion": None}])
        artifact = receipts[2]
        artifact.update(id=41, size_in_bytes=4096)
        job = {"id": 37, "run_id": 1, "head_sha": "1" * 40, "name": producer["name"],
               "workflow_name": "LibreWPF Build", "status": "completed", "conclusion": "success",
               "started_at": "2026-10-13T20:39:04Z", "completed_at": "2026-10-13T20:58:07Z",
               "steps": copy.deepcopy(producer["steps"])}
        name, digest = artifact["name"], artifact["digest"].removeprefix("sha256:")
        lines = ["##[group]Run actions/upload-artifact@v4", "with:", "  name: " + name,
                 "  overwrite: false", "##[endgroup]", "Beginning upload of artifact content to blob storage",
                 "SHA256 digest of uploaded artifact zip is " + digest,
                 f"Artifact {name}.zip successfully finalized. Artifact ID 41",
                 f"Artifact {name} has been successfully uploaded! Final size is 4096 bytes. Artifact ID is 41"]
        log = "\n".join("2026-10-13T20:57:58.1000000Z " + line for line in lines)
        return receipts, {"schema": "github-upload-log-v1", "job": job, "log": log}

    def test_stale_upload_requires_exact_original_action_evidence(self):
        receipts, evidence = self.stale_upload()
        with self.assertRaises(ValueError):
            self.validate(receipts)
        prepare.validate_producer(*receipts, "1" * 40, 1, 2, evidence)
        producer = next(j for j in receipts[0]["jobs"] if j["name"] == evidence["job"]["name"])
        self.assertEqual("pending", producer["steps"][0]["status"])
        self.assertIsNone(producer["steps"][0]["conclusion"])

    def test_upload_log_cannot_replace_a_failed_whole_build(self):
        receipts, evidence = self.stale_upload()
        receipts[0]["conclusion"] = "failure"
        with self.assertRaises(ValueError):
            prepare.validate_producer(*receipts, "1" * 40, 1, 2, evidence)

    def test_upload_log_cannot_replace_failed_skipped_or_duplicate_uploads(self):
        for location in ("run", "job"):
            for conclusion in ("failure", "cancelled", "skipped", "timed_out", "duplicate"):
                receipts, evidence = self.stale_upload()
                producer = next(j for j in receipts[0]["jobs"] if j["name"] == evidence["job"]["name"])
                owner = producer if location == "run" else evidence["job"]
                if conclusion == "duplicate": owner["steps"].append(copy.deepcopy(owner["steps"][0]))
                else: owner["steps"][0].update(status="completed", conclusion=conclusion)
                with self.subTest(location=location, conclusion=conclusion), self.assertRaises(ValueError):
                    prepare.validate_producer(*receipts, "1" * 40, 1, 2, evidence)

    def test_upload_log_rejects_other_job_run_head_or_interval(self):
        for field, value in (("id", 99), ("run_id", 99), ("head_sha", "3" * 40),
                             ("name", "Another job"), ("conclusion", "failure"),
                             ("completed_at", "2026-10-13T20:57:00Z")):
            receipts, evidence = self.stale_upload(); evidence["job"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                prepare.validate_producer(*receipts, "1" * 40, 1, 2, evidence)

    def test_upload_log_rejects_wrong_artifact_digest_size_or_id(self):
        for field, value in (("id", 42), ("size_in_bytes", 4097), ("digest", "sha256:" + "3" * 64)):
            receipts, evidence = self.stale_upload(); receipts[2][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                prepare.validate_producer(*receipts, "1" * 40, 1, 2, evidence)

    def test_upload_log_rejects_error_duplicate_or_unscoped_completion(self):
        for mode in ("error", "duplicate", "unscoped", "reordered", "overwrite", "missing"):
            receipts, evidence = self.stale_upload(); original = evidence["log"]
            if mode == "error": evidence["log"] += "\n2026-10-13T20:57:59Z ##[error]Upload failed"
            elif mode == "duplicate": evidence["log"] += "\n" + original
            elif mode == "unscoped": evidence["log"] = original.replace("Run actions/upload-artifact@v4", "Run echo diagnostics")
            elif mode == "reordered": evidence["log"] = "\n".join(original.splitlines()[:-3] + list(reversed(original.splitlines()[-3:])))
            elif mode == "overwrite": evidence["log"] = original.replace("overwrite: false", "overwrite: true")
            else: evidence["log"] = "\n".join(original.splitlines()[:-1])
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                prepare.validate_producer(*receipts, "1" * 40, 1, 2, evidence)

    def test_ci_requires_every_current_bootstrap_job_beside_original_inventory(self):
        receipts = self.receipts()
        with self.assertRaisesRegex(ValueError, "three current SDK bootstrap"):
            prepare_ci.validate_ci_producer(*receipts, "1" * 40, 1, 2)
        bootstrap = [dict(name=name, status="completed", conclusion="success")
                     for name in sorted(prepare_ci.BOOTSTRAP_JOBS)]
        receipts[0]["jobs"].extend(bootstrap)
        prepare_ci.validate_ci_producer(*receipts, "1" * 40, 1, 2)
        for name in prepare_ci.BOOTSTRAP_JOBS:
            incomplete = copy.deepcopy(receipts)
            incomplete[0]["jobs"] = [job for job in incomplete[0]["jobs"] if job["name"] != name]
            with self.subTest(missing=name), self.assertRaisesRegex(ValueError, "three current SDK bootstrap"):
                prepare_ci.validate_ci_producer(*incomplete, "1" * 40, 1, 2)

    def test_additional_jobs_must_also_succeed(self):
        receipts = self.receipts()
        receipts[0]["jobs"].append({"name": "Additional required gate", "status": "completed", "conclusion": "success"})
        self.validate(receipts)
        receipts[0]["jobs"][-1]["conclusion"] = "failure"
        with self.assertRaises(ValueError):
            self.validate(receipts)

    def test_green_producer_job_does_not_waive_failed_or_canceled_build(self):
        for conclusion in ("failure", "cancelled", "skipped", None):
            receipts = self.receipts(); receipts[0]["conclusion"] = conclusion
            with self.subTest(conclusion=conclusion), self.assertRaises(ValueError):
                self.validate(receipts)

    def test_missing_failed_and_duplicate_jobs_rejected(self):
        original = self.receipts()
        for mode in ("missing", "failed", "duplicate"):
            receipts = copy.deepcopy(original)
            if mode == "missing": receipts[0]["jobs"].pop()
            elif mode == "failed": receipts[0]["jobs"][0]["conclusion"] = "failure"
            else: receipts[0]["jobs"][0] = receipts[0]["jobs"][1]
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                self.validate(receipts)

    def test_live_docs_or_wrong_head_rejected(self):
        for field, value in (("status", "in_progress"), ("headSha", "3" * 40)):
            receipts = self.receipts(); receipts[1][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.validate(receipts)

    def test_other_producer_or_expired_artifact_rejected(self):
        for mode in ("other-run", "expired", "no-digest"):
            receipts = self.receipts()
            if mode == "other-run": receipts[2]["workflow_run"]["id"] = 3
            elif mode == "expired": receipts[2]["expired"] = True
            else: receipts[2].pop("digest")
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                self.validate(receipts)

    def test_upload_failure_rejected(self):
        receipts = self.receipts()
        producer = next(j for j in receipts[0]["jobs"] if j["name"] == "SDK package and no-source-change smoke")
        producer["steps"][0]["conclusion"] = "failure"
        with self.assertRaises(ValueError):
            self.validate(receipts)


if __name__ == "__main__":
    unittest.main()
