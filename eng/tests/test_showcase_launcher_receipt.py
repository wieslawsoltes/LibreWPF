#!/usr/bin/env python3
"""Offline launcher I/O contracts; these fixtures do not qualify native UI."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
RECEIPT = "ProGPU WPF Showcase external native drag source press/release received."


class ShowcaseLauncherReceiptTests(unittest.TestCase):
    def run_launcher(self, receipt, requested=True):
        with tempfile.TemporaryDirectory(prefix="showcase-launcher-receipt.") as temporary:
            repo = Path(temporary)
            (repo / "eng").mkdir()
            launcher = repo / "eng/run-progpu-wpf-showcase.sh"
            shutil.copyfile(ROOT / "eng/run-progpu-wpf-showcase.sh", launcher)
            packages = repo / "packages"
            packages.mkdir()
            (packages / "LibreWPF.Sdk.0.1.0-preview.70.nupkg").touch()
            app = repo / "artifacts/bin/ProGPU.Wpf.ShowcaseApp/Debug/net10.0-windows/ProGPU.Wpf.ShowcaseApp"
            app.parent.mkdir(parents=True)
            app.write_text(
                '#!/bin/bash\nset -eu\n'
                'if [[ -n "$FIXTURE_RECEIPT" ]]; then printf "%s\\n" "$FIXTURE_RECEIPT"; fi\n'
                'printf "%s\\n" "ProGPU WPF Showcase live input validation succeeded: '
                'logical 760x560, pixels 760x560, viewport 760x560@0,0, dpi 1." '
                '> "$PROGPU_WPF_SHOWCASE_LIVE_VALIDATE_STATUS_PATH"\n'
            )
            app.chmod(0o755)
            env = {key: value for key, value in os.environ.items() if not key.startswith("PROGPU_WPF_")}
            env.update(PROGPU_WPF_SHOWCASE_SKIP_BUILD="1", PROGPU_WPF_SHOWCASE_LIVE_VALIDATE="1",
                       PROGPU_WPF_SHOWCASE_LIVE_VALIDATE_TIMEOUT_SECONDS="5",
                       PROGPU_WPF_PACKAGE_OUTPUT=str(packages), FIXTURE_RECEIPT=receipt)
            if requested:
                env["PROGPU_WPF_SHOWCASE_NATIVE_DRAG_STATUS_PATH"] = str(repo / "drag-status")
            return subprocess.run(["bash", str(launcher)], env=env, text=True,
                                  capture_output=True, timeout=10)

    def test_real_private_log_receipt_is_forwarded_to_the_outer_gate(self):
        result = self.run_launcher(RECEIPT)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(RECEIPT, result.stdout.splitlines())

    def test_missing_receipt_rejects_otherwise_successful_geometry(self):
        result = self.run_launcher("")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Expected the Showcase apphost source press/release receipt.", result.stderr)

    def test_partial_or_embedded_receipt_is_not_accepted(self):
        for receipt in (RECEIPT[:-1], "not observed: " + RECEIPT):
            with self.subTest(receipt=receipt):
                self.assertNotEqual(self.run_launcher(receipt).returncode, 0)

    def test_ordinary_probe_does_not_require_an_unrequested_drag(self):
        result = self.run_launcher("", requested=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn(RECEIPT, result.stdout)


if __name__ == "__main__":
    unittest.main()
