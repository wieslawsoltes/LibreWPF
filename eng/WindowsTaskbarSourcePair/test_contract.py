import copy
import hashlib
import json
from pathlib import Path
import unittest
import compare
import source
import run

HERE = Path(__file__).resolve().parent


class Contract(unittest.TestCase):
    def setUp(self):
        self.reference = json.loads((HERE / 'reference-observations.json').read_text())

    def reject(self, mutate):
        candidate = copy.deepcopy(self.reference)
        mutate(candidate)
        with self.assertRaises((RuntimeError, KeyError)):
            compare.compare(self.reference, candidate)

    def test_original_reference_accepts(self):
        self.assertEqual(compare.compare(self.reference, self.reference)['matchedRecords'], 10)

    def test_source_only_observer_edits(self):
        original = (HERE / 'original.cs').read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
        actual, selected = source.select(original.replace(b'\r\n', b'\n'))
        self.assertEqual(actual, original)
        self.assertEqual(hashlib.sha256(actual).hexdigest(), source.ORIGINAL_WINDOWS_SHA)
        self.assertEqual(selected.count(b'NativeHandle(subject)'), 2)
        self.assertIn(b'TimeSpan.FromSeconds(20)', selected)
        with self.assertRaises(RuntimeError): source.select(original.replace(b'\r\n', b'\n') + b' ')

    def test_source_owner_mismatch(self):
        self.reject(lambda r: r['records'][4].update(sourceOwner=0))

    def test_chrome_change(self):
        def mutate(r):
            handle = r['records'][3]['native']['handle']
            r['records'][3]['native']['style'] = '0x0'
            for value in r['records'][3]['inventory']:
                if value['handle'] == handle: value['style'] = '0x0'
        self.reject(mutate)

    def test_hidden_owner_destroyed_too_early(self):
        def mutate(r):
            hidden = r['records'][1]['native']['owner']
            r['records'][2]['inventory'] = [v for v in r['records'][2]['inventory'] if v['handle'] != hidden]
        self.reject(mutate)

    def test_retirement_failure(self):
        self.reject(lambda r: r['records'][-1].update(originalStillExists=True))

    def test_surviving_child(self):
        self.reject(lambda r: r['finalInventory'].append(r['records'][0]['native']))

    def test_new_infrastructure(self):
        def mutate(r):
            extra = copy.deepcopy(r['records'][0]['inventory'][-1]); extra['handle'] = 999999999
            r['records'][4]['inventory'].append(extra)
        self.reject(mutate)

    def test_changed_infrastructure(self):
        self.reject(lambda r: r['records'][4]['inventory'][-1].update(enabled=True))

    def test_foreign_thread(self):
        self.reject(lambda r: r['records'][4]['inventory'][0].update(thread=123456789))

    def test_duplicate_inventory(self):
        self.reject(lambda r: r['records'][4]['inventory'].append(r['records'][4]['inventory'][0]))

    def test_original_failure(self):
        self.reject(lambda r: r.update(failure='Original timeout'))

    def test_diagnostic_scope(self):
        self.reject(lambda r: r.update(qualified=True))

    def test_missing_phase(self):
        self.reject(lambda r: r['records'].pop())

    def test_projects_keep_original_bounds_in_source(self):
        portable = run.project('portable', {'sdk': '0.1.0-ci.123'})
        self.assertIn('<ProGpuWpfDx12ShaderCompiler>Dxc', portable)
        self.assertIn('LIBREWPF_TASKBAR', portable)
        self.assertNotIn('LIBREWPF_TASKBAR', run.project('microsoft', {}))


if __name__ == '__main__': unittest.main()
