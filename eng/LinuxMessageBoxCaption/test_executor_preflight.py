import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import executor

class PreflightTests(unittest.TestCase):
    def test_missing_or_wrong_approval_cannot_launch_or_load_native_input(self):
        for approval in ('', '0'*64):
            with self.subTest(approval=approval), mock.patch.dict(os.environ, {
                'RUNNER_ENVIRONMENT':'github-hosted','GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF',
                'LIBREWPF_CAPTION_INPUT_APPROVAL_SHA256':approval}, clear=True), \
                mock.patch.object(executor.subprocess,'Popen') as launch, \
                mock.patch.object(executor.importlib.util,'spec_from_file_location') as load:
                with self.assertRaisesRegex(ValueError,'explicit approval'):
                    executor.run_case({},'preflight-only')
                launch.assert_not_called();load.assert_not_called()
    def test_non_hosted_environment_cannot_launch(self):
        with mock.patch.dict(os.environ,{},clear=True),mock.patch.object(executor.subprocess,'Popen') as launch:
            with self.assertRaisesRegex(ValueError,'Disposable owned'):
                executor.run_case({},'preflight-only')
            launch.assert_not_called()
    def test_duplicate_and_nonfinite_source_fields_reject(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'source.json'
            for raw in ('{"pid":1,"pid":2}','{"elapsedSeconds":NaN}','{"value":Infinity}'):
                with self.subTest(raw=raw):
                    path.write_text(raw)
                    with self.assertRaises(ValueError):executor.read_source(path)
    def test_source_symlink_and_oversized_record_reject(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'source.json';path.write_text('{}')
            alias=Path(root)/'alias.json';alias.symlink_to(path)
            with self.assertRaises(ValueError):executor.read_source(alias)
            path.write_text(' '*65537)
            with self.assertRaises(ValueError):executor.read_source(path)

if __name__=='__main__':unittest.main()
