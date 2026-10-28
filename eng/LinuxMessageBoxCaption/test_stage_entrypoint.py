"""Exercise actual reviewed manifest selection without download or application work."""
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import stage_frozen

class StageEntryPointTests(unittest.TestCase):
    def test_actual_reviewed_manifest_reaches_each_exact_frozen_consumer(self):
        expected={'NativeMilWgpu':12817604575,'ManagedPortable':12817928501}
        for renderer,artifact in expected.items():
            with self.subTest(renderer=renderer), tempfile.TemporaryDirectory() as tmp:
                directory=Path(tmp);environment=directory/'github-env';seen=[]
                def download(args,stdout,timeout):
                    self.assertEqual(args,['gh','api','repos/wieslawsoltes/LibreWPF/actions/artifacts/'+str(artifact)+'/zip'])
                    self.assertEqual(timeout,180);stdout.write(b'inert download marker')
                    return type('Result',(),{'returncode':0})()
                def restore(path,selected,destination,evidence):
                    self.assertEqual(path.read_bytes(),b'inert download marker')
                    self.assertEqual(selected['artifact']['id'],artifact)
                    self.assertEqual(selected['artifact']['workflow_run']['id'],40694128166)
                    self.assertEqual(selected['artifact']['workflow_run']['head_sha'],'5bbd13c709537b89a07b1b22ca84433f7ba795d2')
                    self.assertEqual(selected['frozen']['producerHead'],'4ab76a8a111a1b176d66bcd332f2d65e1c7dabca')
                    self.assertEqual(selected['frozen']['renderer'],renderer)
                    self.assertEqual(len(selected['frozen']['outputs']),253)
                    self.assertEqual(destination,directory/'caption-frozen/application')
                    seen.append(artifact)
                    return {'application':str(destination/'LinuxMessageBoxCaption.dll')}
                with mock.patch.dict(os.environ,{'RUNNER_ENVIRONMENT':'github-hosted','GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF','GITHUB_RUN_ATTEMPT':'1','RUNNER_TEMP':tmp,'GITHUB_ENV':str(environment)}), \
                     mock.patch('sys.argv',['stage_frozen.py','--renderer',renderer]), \
                     mock.patch.object(stage_frozen.subprocess,'run',side_effect=download), \
                     mock.patch.object(stage_frozen,'restore',side_effect=restore):
                    stage_frozen.main()
                self.assertEqual(seen,[artifact])
                self.assertEqual(environment.read_text(),'CAPTION_APP='+str(directory/'caption-frozen/application/LinuxMessageBoxCaption.dll')+'\nCAPTION_RENDERER='+renderer+'\n')

if __name__=='__main__':unittest.main()
