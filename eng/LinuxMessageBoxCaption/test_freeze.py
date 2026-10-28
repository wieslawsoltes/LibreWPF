import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from freeze import freeze

class FreezeTests(unittest.TestCase):
    def fixture(self, root):
        output = root / "application"
        output.mkdir()
        (output / "fixture.bin").write_bytes(b"metadata-only fixture; not an executable")
        (output / "LinuxMessageBoxCaption.runtimeconfig.json").write_text(json.dumps({"runtimeOptions":{"configProperties":{"LibreWPF.RequestedRendererMode":"NativeMilWgpu"}}}))
        record = dict(compiled=True, buildExitCode=0, applicationExecuted=False, inputSent=False,
                      renderer="NativeMilWgpu", output=str(output), producerHead="a"*40,
                      sources={"fixture.cs":"b"*64}, nativeOutputs={},
                      outputs={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in output.iterdir()})
        receipt = root / "preparation.json"
        receipt.write_text(json.dumps(record))
        return output, record, receipt
    def test_exact_output_freezes_without_execution(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw); _,_,receipt=self.fixture(root)
            result=freeze(receipt, root/"retained")
            self.assertFalse(result["inputSent"])
            self.assertFalse(result["applicationExecuted"])
            self.assertTrue((root/"retained/compiled-consumer.zip").is_file())
    def test_changed_or_added_output_rejected(self):
        for mutation in ("changed","added"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as raw:
                root=Path(raw); output,_,receipt=self.fixture(root)
                (output/("fixture.bin" if mutation=="changed" else "extra.bin")).write_bytes(b"changed")
                with self.assertRaisesRegex(ValueError,"inventory"): freeze(receipt,root/"retained")
                self.assertFalse((root/"retained").exists())
    def test_linked_output_rejected(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw); output,_,receipt=self.fixture(root)
            (output/"alias.bin").symlink_to(output/"fixture.bin")
            with self.assertRaisesRegex(ValueError,"Linked"): freeze(receipt,root/"retained")
    def test_failed_or_executed_receipt_rejected(self):
        for field,value in (("compiled",False),("buildExitCode",1),("inputSent",True),("applicationExecuted",True)):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as raw:
                root=Path(raw); _,record,receipt=self.fixture(root)
                record[field]=value;receipt.write_text(json.dumps(record))
                with self.assertRaises(ValueError): freeze(receipt,root/"retained")
    def test_existing_retained_evidence_rejected(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw); _,_,receipt=self.fixture(root)
            destination=root/"retained";destination.mkdir()
            with self.assertRaisesRegex(ValueError,"overwrite"): freeze(receipt,destination)

    def test_wrong_renderer_rejected_even_when_output_hashes_match(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw); _,record,receipt=self.fixture(root)
            record['renderer']='ManagedPortable';receipt.write_text(json.dumps(record))
            with self.assertRaisesRegex(ValueError,'renderer'): freeze(receipt,root/'retained')

if __name__=="__main__": unittest.main()

