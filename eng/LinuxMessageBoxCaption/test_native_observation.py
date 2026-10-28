import copy
import json
from pathlib import Path
import tempfile
import unittest
from native_observation import validate_recorded_native
from test_contract import sample, box

class NativeObservationTests(unittest.TestCase):
    def owner(self):
        state=sample();state.update(phase='owner',dialog=None)
        state['owner'].update(title='owner',sourceEnabled=True,inputAllowed=True,
                              outerVisualWidth=760,outerVisualHeight=520,
                              clientWidth=760,clientHeight=481,client=box(80,80,760,481))
        native=[dict(xid=1,pid=123,title='owner',client=dict(x=80,y=80,width=760,height=481))]
        return state,native

    def test_actual_client_extent_does_not_fit_native_into_outer_window(self):
        state,native=self.owner();before=copy.deepcopy((state,native))
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'native.json'
            validate_recorded_native(state,native,path)
            observed=json.loads(path.read_text())
            self.assertEqual(observed['source']['owner']['outerVisualHeight'],520)
            self.assertEqual(observed['nativeWindows'],native)
        self.assertEqual((state,native),before)

    def test_original_outer_extent_failure_retains_both_original_values(self):
        state,native=self.owner();state['owner']['client']=box(80,80,760,520)
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'native.json'
            with self.assertRaisesRegex(ValueError,'client mismatch'):
                validate_recorded_native(state,native,path)
            observed=json.loads(path.read_text())
            self.assertEqual(observed['source'],state)
            self.assertEqual(observed['nativeWindows'],native)
            self.assertEqual(observed['sourceSequence'],state['sequence'])

    def test_source_origin_difference_still_rejects_without_fitting(self):
        state,native=self.owner();native[0]['client']['y']=119
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'native.json'
            with self.assertRaisesRegex(ValueError,'client mismatch'):
                validate_recorded_native(state,native,path)
            self.assertEqual(json.loads(path.read_text())['nativeWindows'],native)

    def test_prior_native_evidence_cannot_be_overwritten(self):
        state,native=self.owner()
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'native.json';path.write_bytes(b'original')
            with self.assertRaises(FileExistsError):validate_recorded_native(state,native,path)
            self.assertEqual(path.read_bytes(),b'original')

if __name__=='__main__':unittest.main()
