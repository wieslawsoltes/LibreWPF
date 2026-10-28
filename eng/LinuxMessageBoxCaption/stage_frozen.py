"""Restore exact reviewed compiled bytes; no build, application launch or native input."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import io
import json
import os
import stat
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parent

def require(condition, message):
    if not condition: raise ValueError(message)

def sha(path):
    with path.open("rb") as stream: return hashlib.file_digest(stream, "sha256").hexdigest()

def read_artifact(path, selected):
    artifact, frozen = selected["artifact"], selected["frozen"]
    require(path.is_file() and not path.is_symlink(), "Original archive missing or linked")
    require(path.stat().st_size == artifact["size_in_bytes"] and
            "sha256:" + sha(path) == artifact["digest"], "Original artifact bytes changed")
    with zipfile.ZipFile(path) as outer:
        require(len(outer.namelist()) == len(set(outer.namelist())), "Duplicate outer members")
        prefix = "LibreWPF/LibreWPF/artifacts/frozen-caption/"
        require(json.loads(outer.read(prefix+"frozen-consumer.json")) == frozen, "Frozen inventory changed")
        info = outer.getinfo(prefix+"compiled-consumer.zip")
        require(info.file_size == frozen["archive"]["bytes"] <= 128*1024*1024, "Frozen archive size changed")
        raw = outer.read(info)
        require(hashlib.sha256(raw).hexdigest() == frozen["archive"]["sha256"], "Frozen archive changed")
        original = outer.read(prefix+"preparation.json")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        expected = {"application/"+name for name in frozen["outputs"]} | {"preparation.json"}
        require(len(archive.namelist()) == len(expected) and set(archive.namelist()) == expected,
                "Complete original executable inventory required")
        require(archive.read("preparation.json") == original, "Original preparation receipt differs")
        require(sum(i.file_size for i in archive.infolist()) <= 1024**3, "Bounded compiled output required")
        for info in archive.infolist():
            relative = PurePosixPath(info.filename)
            require(not relative.is_absolute() and ".." not in relative.parts and
                    "\\" not in info.filename and not stat.S_ISLNK(info.external_attr >> 16),
                    "Unsafe compiled member")
        for name, digest in frozen["outputs"].items():
            require(hashlib.sha256(archive.read("application/"+name)).hexdigest() == digest,
                    "Compiled file changed: "+name)
        runtime = json.loads(archive.read("application/LinuxMessageBoxCaption.runtimeconfig.json"))
        require(runtime["runtimeOptions"]["configProperties"]["LibreWPF.RequestedRendererMode"] == frozen["renderer"],
                "Actual compiled renderer differs")
    record = json.loads(original)
    require(record["compiled"] is True and record["buildExitCode"] == 0 and
            record["applicationExecuted"] is False and record["inputSent"] is False,
            "Successful unexecuted compilation receipt required")
    require(record["outputs"] == frozen["outputs"] and record["sources"] == frozen["sources"] and
            record["nativeOutputs"] == frozen["nativeOutputs"] and record["renderer"] == frozen["renderer"] and
            record["producerHead"] == frozen["producerHead"], "Frozen producer identity differs")
    return raw, original

def restore(path, selected, destination, evidence):
    raw, original = read_artifact(path, selected)
    require(not destination.exists() and not destination.is_symlink(), "Fresh private output required")
    require(not (evidence/"ubuntu26-caption-preparation.json").exists(), "Never overwrite a preparation receipt")
    destination.mkdir(parents=True)
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        for name in selected["frozen"]["outputs"]:
            target = destination/name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read("application/"+name))
    observed = {str(p.relative_to(destination)):sha(p) for p in destination.rglob("*") if p.is_file()}
    require(observed == selected["frozen"]["outputs"], "Restored output differs")
    before = json.loads(original)
    after = dict(before, output=str(destination), application=str(destination/"LinuxMessageBoxCaption.dll"))
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence/"caption-original-preparation.json").write_bytes(original)
    (evidence/"ubuntu26-caption-preparation.json").write_text(json.dumps(after,indent=2)+"\n")
    mapping = dict(applicationExecuted=False, inputSent=False, artifact=selected["artifact"],
                   originalReceiptSha256=hashlib.sha256(original).hexdigest(),
                   before={k:before[k] for k in ("output","application")},
                   after={k:after[k] for k in ("output","application")}, outputsUnchanged=True)
    (evidence/"caption-relocation.json").write_text(json.dumps(mapping,indent=2)+"\n")
    return after

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--renderer", choices=("NativeMilWgpu","ManagedPortable"), required=True)
    args=parser.parse_args()
    require(os.environ.get("RUNNER_ENVIRONMENT")=="github-hosted" and
            os.environ.get("GITHUB_REPOSITORY")=="wieslawsoltes/LibreWPF", "Disposable hosted repository required")
    require(os.environ.get("GITHUB_RUN_ATTEMPT")=="1", "No unchanged retry")
    config=json.loads((ROOT/"consumers.json").read_text())
    selected=config["renderers"][args.renderer]
    require(selected["artifact"]["workflow_run"]["head_sha"]==config["buildHead"] and
            selected["artifact"]["workflow_run"]["id"]==config["buildRun"] and
            selected["frozen"]["producerHead"]==config["producerHead"], "Exact reviewed producer required")
    directory=Path(os.environ["RUNNER_TEMP"])/"caption-frozen"
    require(not directory.exists(), "Fresh owned stage required")
    directory.mkdir()
    archive=directory/"original-artifact.zip"
    with archive.open("xb") as output:
        result=subprocess.run(["gh","api","repos/wieslawsoltes/LibreWPF/actions/artifacts/"
                               +str(selected["artifact"]["id"])+"/zip"],stdout=output,timeout=180)
    require(result.returncode==0,"Original artifact download failed")
    receipt=restore(archive,selected,directory/"application",Path("artifacts"))
    with open(os.environ["GITHUB_ENV"],"a") as env:
        env.write("CAPTION_APP="+receipt["application"]+"\nCAPTION_RENDERER="+args.renderer+"\n")

if __name__=="__main__": main()

