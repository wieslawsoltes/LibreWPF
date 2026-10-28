#!/usr/bin/env python3
"""Exercise the source CI selector, including exact counts and failure propagation."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class SourceSelectorTests(unittest.TestCase):
    def run_helper(self, fail_at=0, assembly=True):
        with tempfile.TemporaryDirectory(prefix="wpf-catalog-desktop-selector-") as temporary:
            root = Path(temporary)
            script = root / "eng" / "gate.sh"
            script.parent.mkdir()
            script.write_bytes((ROOT / "eng/progpu-wpf-catalog-desktop-source.sh").read_bytes())
            if assembly:
                target = root / "artifacts/bin/PresentationCore.Tests/Release/net10.0-windows/PresentationCore.Tests.dll"
                target.parent.mkdir(parents=True)
                target.touch()
            log = root / "calls.jsonl"
            host = root / "owned-test-host"
            host.write_text("#!/usr/bin/env python3\nimport json,os,sys\nfrom pathlib import Path\np=Path(os.environ['SELECTOR_CALLS'])\nn=len(p.read_text().splitlines())+1 if p.exists() else 1\nwith p.open('a') as f: f.write(json.dumps({'args':sys.argv[1:], 'backend':os.environ.get('LIBREWPF_TEST_MEDIA_BACKEND')})+'\\n')\nsys.exit(23 if n==int(os.environ['SELECTOR_FAIL_AT']) else 0)\n")
            host.chmod(0o700)
            environment = dict(os.environ, PROGPU_WPF_CATALOG_DESKTOP_DOTNET=str(host),
                               SELECTOR_CALLS=str(log), SELECTOR_FAIL_AT=str(fail_at),
                               LIBREWPF_TEST_MEDIA_BACKEND="WindowsMil")
            environment.pop("CONFIGURATION", None)
            result = subprocess.run(["bash", str(script)], env=environment, capture_output=True, text=True, timeout=10)
            calls = [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []
            return result, calls

    def test_complete_inventory_has_exact_counts_bounds_and_receipts(self):
        result, calls = self.run_helper()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(calls), 6)
        self.assertEqual([call["args"][call["args"].index("--minimum-expected-tests") + 1] for call in calls],
                         ["4", "3", "3", "7", "4", "3"])
        for call in calls:
            args = call["args"]
            self.assertEqual(call["backend"], "Portable")
            self.assertEqual(args[args.index("--fail-skips") + 1], "on")
            self.assertEqual(args[args.index("--timeout") + 1], "60s")
            self.assertIn("--report-trx", args)
            self.assertIn("--results-directory", args)
            self.assertNotIn("NativeDesktopTransformQueryTests", " ".join(args))
            self.assertNotIn("build", args)
        self.assertIn("PortableSystemFontDiscoveryTests", " ".join(calls[0]["args"]))
        self.assertIn("PortableDesktopTransformQueryTests", " ".join(calls[3]["args"]))

    def test_failed_control_stops_remaining_calls_and_preserves_exit_code(self):
        result, calls = self.run_helper(fail_at=2)
        self.assertEqual(result.returncode, 23)
        self.assertEqual(len(calls), 2)

    def test_missing_source_assembly_fails_without_building_or_running(self):
        result, calls = self.run_helper(assembly=False)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(calls, [])


if __name__ == "__main__":
    unittest.main()
