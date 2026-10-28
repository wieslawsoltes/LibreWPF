import copy
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from stage_frozen import read_artifact, restore

class FrozenTests(unittest.TestCase):
    def fixture(self, root, unsafe=False, renderer="NativeMilWgpu"):
        outputs = {"../outside.bin" if unsafe else "LinuxMessageBoxCaption.dll": b"metadata-only inert fixture",
                   "LinuxMessageBoxCaption.runtimeconfig.json": json.dumps({"runtimeOptions":{"configProperties":{
                       "LibreWPF.RequestedRendererMode":renderer}}}).encode()}
        hashes={n:hashlib.sha256(v).hexdigest() for n,v in outputs.items()}
        prep=dict(compiled=True,buildExitCode=0,applicationExecuted=False,inputSent=False,
                  producerHead="a"*40,renderer="NativeMilWgpu",sources={"App.xaml.cs":"b"*64},
                  nativeOutputs={},outputs=hashes,output="/original/application",
                  application="/original/application/LinuxMessageBoxCaption.dll")
        original=json.dumps(prep).encode()
        memory=io.BytesIO()
        with zipfile.ZipFile(memory,"w") as z:
            for n,v in outputs.items():z.writestr("application/"+n,v)
            z.writestr("preparation.json",original)
        raw=memory.getvalue()
        frozen=dict(producerHead=prep["producerHead"],renderer=prep["renderer"],sources=prep["sources"],
                    nativeOutputs={},outputs=hashes,archive=dict(bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest()))
        path=root/"original.zip"
        with zipfile.ZipFile(path,"w") as z:
            prefix="LibreWPF/LibreWPF/artifacts/frozen-caption/"
            z.writestr(prefix+"frozen-consumer.json",json.dumps(frozen))
            z.writestr(prefix+"compiled-consumer.zip",raw)
            z.writestr(prefix+"preparation.json",original)
        selected=dict(frozen=frozen,artifact=dict(size_in_bytes=path.stat().st_size,
                        digest="sha256:"+hashlib.sha256(path.read_bytes()).hexdigest()))
        return path,selected,prep,outputs

    def test_restore_changes_only_two_receipt_paths(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw);path,selected,before,files=self.fixture(root)
            after=restore(path,selected,root/"output",root/"evidence")
            self.assertEqual({k:v for k,v in after.items() if k not in ("application","output")},
                             {k:v for k,v in before.items() if k not in ("application","output")})
            for name,data in files.items():self.assertEqual((root/"output"/name).read_bytes(),data)
            self.assertEqual(json.loads((root/"evidence/caption-original-preparation.json").read_text()),before)
            self.assertFalse(after["inputSent"]);self.assertFalse(after["applicationExecuted"])

    def test_changed_outer_archive_rejects_before_publication(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw);path,selected,_,_=self.fixture(root)
            with path.open("ab") as f:f.write(b"changed")
            with self.assertRaisesRegex(ValueError,"bytes changed"):restore(path,selected,root/"output",root/"evidence")
            self.assertFalse((root/"output").exists())

    def test_unsafe_path_rejected_even_with_consistent_hashes(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw);path,selected,_,_=self.fixture(root,unsafe=True)
            with self.assertRaisesRegex(ValueError,"Unsafe"):restore(path,selected,root/"output",root/"evidence")
            self.assertFalse((root/"outside.bin").exists())

    def test_wrong_compiled_renderer_rejected(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw);path,selected,_,_=self.fixture(root,renderer="ManagedPortable")
            with self.assertRaisesRegex(ValueError,"renderer differs"):read_artifact(path,selected)

    def test_existing_output_is_preserved(self):
        with tempfile.TemporaryDirectory() as raw:
            root=Path(raw);path,selected,_,_=self.fixture(root)
            output=root/"output";output.mkdir();(output/"original").write_bytes(b"retained")
            with self.assertRaisesRegex(ValueError,"Fresh"):restore(path,selected,output,root/"evidence")
            self.assertEqual((output/"original").read_bytes(),b"retained")

if __name__=="__main__":unittest.main()

