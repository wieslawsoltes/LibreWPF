#!/usr/bin/env python3
"""Execute the canonical workflow staging step with a recording helper, offline."""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/progpu-wpf-sdk.yml"


class CanonicalNativeStagingTests(unittest.TestCase):
    def staging_step(self):
        workflow = WORKFLOW.read_text()
        canonical = workflow.split("\n  canonical-winforms-integration:\n", 1)[1].split("\n  portable-source-contracts:\n", 1)[0]
        name = "      - name: Stage exact native drawing package dependency\n"
        self.assertLess(canonical.index(name), canonical.index("      - name: Build canonical WinForms and WindowsFormsIntegration\n"))
        step = canonical.split(name, 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("GH_TOKEN: ${{ github.token }}", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())

    def run_step(self, helper_status=0, git_status=0):
        with tempfile.TemporaryDirectory(prefix="wpf-canonical-staging-") as temporary:
            root = Path(temporary)
            (root / "eng").mkdir()
            helper = root / "eng/progpu-stage-ci-native-runtimes.sh"
            # This stub records arguments only; it cannot create native payloads
            # and is never used by package production or the real staging helper.
            helper.write_text('#!/bin/bash\nprintf "%s\\n" "$@" > "$STAGING_RECEIPT"\nexit "$STAGING_STATUS"\n')
            helper.chmod(0o755)
            receipt = root / "arguments"
            environment = dict(os.environ, GITHUB_WORKSPACE=str(root), STAGING_RECEIPT=str(receipt),
                               STAGING_STATUS=str(helper_status), GIT_STATUS=str(git_status))
            script = '''git() {
  test "$*" = "-C external/ProGPU rev-parse HEAD" || return 91
  test "$GIT_STATUS" = 0 || return "$GIT_STATUS"
  printf '%s\\n' aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
}
'''+ self.staging_step()
            result = subprocess.run(["bash", "-c", script], cwd=root, env=environment, capture_output=True, text=True)
            return result.returncode, receipt.read_text().splitlines() if receipt.exists() else [], str(root)

    def test_stages_exact_checkout_into_its_native_package_root(self):
        status, arguments, root = self.run_step()
        self.assertEqual(status, 0)
        self.assertEqual(arguments, ["a" * 40, root + "/external/ProGPU/artifacts/progpu-native/package", root + "/external/ProGPU/artifacts/progpu-dx12/package", root + "/external/ProGPU/artifacts/progpu-xlib/package"])

    def test_failed_producer_staging_fails_the_step(self):
        status, arguments, _ = self.run_step(helper_status=23)
        self.assertEqual(status, 23)
        self.assertEqual(arguments[0], "a" * 40)

    def test_unresolved_source_identity_never_invokes_staging(self):
        status, arguments, _ = self.run_step(git_status=27)
        self.assertEqual(status, 27)
        self.assertEqual(arguments, [])


class ReleaseNativeStagingTests(unittest.TestCase):
    workflow = ROOT / ".github/workflows/progpu-wpf-release.yml"

    def native_step(self):
        workflow = self.workflow.read_text()
        names = ["Stage exact ProGPU release packages",
                 "Stage native runtimes from exact ProGPU release package",
                 "Build canonical LibreWinForms package closure",
                 "Align canonical package feed with exact ProGPU release",
                 "Build, pack, and smoke SDK"]
        positions = [workflow.index("      - name: " + name + "\n") for name in names]
        self.assertEqual(positions, sorted(positions),
                         "canonical drawing packages require native payloads before they can be produced")
        step = workflow.split("      - name: " + names[1] + "\n", 1)[1].split("\n      - name:", 1)[0]
        self.assertNotIn("continue-on-error", step)
        return "\n".join(line[10:] for line in step.split("        run: |\n", 1)[1].splitlines())

    def test_release_production_order(self):
        self.native_step()

    def test_absent_native_payload_cannot_reach_canonical_packaging(self):
        for metadata_only_archive in (False, True):
            with self.subTest(metadata_only_archive=metadata_only_archive):
                with tempfile.TemporaryDirectory(prefix="wpf-release-native-") as temporary:
                    root = Path(temporary)
                    helper = root / "eng/progpu-stage-release-native-packages.py"
                    helper.parent.mkdir()
                    helper.write_bytes((ROOT / "eng/progpu-stage-release-native-packages.py").read_bytes())
                    packages = root / "packages"
                    packages.mkdir()
                    if metadata_only_archive:
                        # Deliberately contains no runtime bytes: this is a
                        # rejection control, never a staged native substitute.
                        with zipfile.ZipFile(packages / "ProGPU.Backend.Native.0.1.0-test.nupkg", "w") as archive:
                            archive.writestr("ProGPU.Backend.Native.nuspec", "<package/>")
                    environment = dict(os.environ, GITHUB_WORKSPACE=str(root),
                                       PROGPU_WPF_PREPACKAGED_PROGPU_DIR=str(packages),
                                       PROGPU_WPF_PROGPU_PACKAGE_VERSION="0.1.0-test",
                                       PROGPU_WPF_EXPECTED_PROGPU_PACKAGE_COMMIT="a" * 40)
                    result = subprocess.run(["bash", "-c", self.native_step()], cwd=root,
                                            env=environment, capture_output=True, text=True)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("ProGPU.Backend.Native", result.stderr)
                    self.assertFalse((root / "external/ProGPU/artifacts/progpu-native/package").exists())
                    self.assertFalse((root / "external/ProGPU/artifacts/progpu-native/runtime").exists())


class ExactProducerRunSelectionTests(unittest.TestCase):
    """Exercise the real selector offline; never provide or fabricate runtimes."""

    @staticmethod
    def run_record(number, status, conclusion=None, sha="a" * 40):
        return {"id": number, "head_sha": sha, "status": status, "conclusion": conclusion,
                "created_at": f"2026-10-11T00:00:{number:02d}Z", "html_url": f"https://example.invalid/{number}"}

    def select_run(self, responses):
        with tempfile.TemporaryDirectory(prefix="wpf-exact-build-selection-") as temporary:
            root = Path(temporary)
            (root / "runs.json").write_text(json.dumps(responses))
            gh = root / "gh"
            gh.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
root = pathlib.Path(os.environ["SELECTION_FIXTURE"])
endpoint = sys.argv[2]
with (root / "requests").open("a") as log:
    log.write(endpoint + "\\n")
if "/workflows/build.yml/runs?" in endpoint:
    counter = root / "query-count"
    index = int(counter.read_text()) if counter.exists() else 0
    responses = json.loads((root / "runs.json").read_text())
    counter.write_text(str(index + 1))
    if index >= len(responses):
        sys.exit(93)
    print(json.dumps({"workflow_runs": responses[index]}))
elif "/artifacts?" in endpoint:
    # Stop at the real missing-artifact guard after observing the selected run.
    # This control cannot stage a native library or qualify any package.
    pass
else:
    sys.exit(94)
''')
            gh.chmod(0o755)
            sleeper = root / "sleep"
            sleeper.write_text("#!/bin/sh\nexit 0\n")
            sleeper.chmod(0o755)
            environment = dict(os.environ, PATH=str(root) + os.pathsep + os.environ["PATH"],
                               SELECTION_FIXTURE=str(root), PROGPU_CI_POLL_SECONDS="1", PROGPU_CI_TIMEOUT_SECONDS="5")
            result = subprocess.run(["bash", str(ROOT / "eng/progpu-stage-ci-native-runtimes.sh"),
                                     "a" * 40, str(root / "runtimes")], env=environment,
                                    capture_output=True, text=True, timeout=10)
            self.assertNotEqual(result.returncode, 0, "No control contains a real native artifact")
            self.assertFalse((root / "runtimes").exists())
            requests = (root / "requests").read_text().splitlines()
            return requests, result.stdout + result.stderr

    def test_pending_duplicate_reuses_complete_success_for_only_the_exact_commit(self):
        for state in ("queued", "in_progress"):
            with self.subTest(state=state):
                requests, output = self.select_run([[self.run_record(1, "completed", "success"),
                    self.run_record(2, state), self.run_record(3, "completed", "success", "b" * 40)]])
                self.assertIn("/runs/1/artifacts?", requests[-1])
                self.assertIn("has no live progpu-native-package artifact", output)

    def test_newest_complete_success_wins(self):
        requests, _ = self.select_run([[self.run_record(1, "completed", "success"),
                                       self.run_record(2, "completed", "success")]])
        self.assertIn("/runs/2/artifacts?", requests[-1])

    def test_newest_completed_failure_cannot_fall_back_to_older_success(self):
        for conclusion in ("failure", "cancelled", "timed_out", "skipped", None):
            with self.subTest(conclusion=conclusion):
                requests, output = self.select_run([[self.run_record(1, "completed", "success"),
                                                    self.run_record(2, "completed", conclusion)]])
                self.assertEqual(len(requests), 1)
                self.assertIn("completed with", output)

    def test_other_commit_or_partial_success_cannot_supply_a_producer(self):
        pending = [self.run_record(1, "in_progress", "success"),
                   self.run_record(2, "completed", "success", "b" * 40)]
        requests, output = self.select_run([pending, [self.run_record(1, "completed", "failure")]])
        self.assertEqual(len(requests), 2)
        self.assertTrue(all("/workflows/build.yml/runs?" in request for request in requests))
        self.assertIn("Waiting for exact ProGPU Build", output)

    def test_no_qualified_prior_build_waits_for_whole_success(self):
        prior = self.run_record(1, "completed", "failure")
        requests, output = self.select_run([[prior, self.run_record(2, "in_progress")],
                                            [prior, self.run_record(2, "completed", "success")]])
        self.assertEqual(len(requests), 3)
        self.assertIn("/runs/2/artifacts?", requests[-1])
        self.assertIn("Waiting for exact ProGPU Build", output)

    def test_pending_retry_cannot_reuse_success_before_a_newer_failure(self):
        prior = [self.run_record(1, "completed", "success"), self.run_record(2, "completed", "failure")]
        requests, output = self.select_run([prior + [self.run_record(3, "in_progress")],
                                            prior + [self.run_record(3, "completed", "failure")]])
        self.assertEqual(len(requests), 2)
        self.assertTrue(all("/workflows/build.yml/runs?" in request for request in requests))
        self.assertIn("Waiting for exact ProGPU Build", output)


if __name__ == "__main__":
    unittest.main()
