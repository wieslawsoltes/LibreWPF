#!/usr/bin/env python3
"""Retain a compiled caption consumer; never execute it or load its native code."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def freeze(receipt, destination):
    record = json.loads(receipt.read_text())
    if record.get("compiled") is not True or record.get("buildExitCode") != 0:
        raise ValueError("Only a successful original compilation can be frozen")
    if record.get("applicationExecuted") is not False or record.get("inputSent") is not False:
        raise ValueError("Compilation receipt must explicitly record no execution/input")
    if record.get("renderer") not in ("NativeMilWgpu", "ManagedPortable"):
        raise ValueError("Unknown renderer")
    directory = Path(record["output"])
    if not directory.is_dir() or directory.is_symlink():
        raise ValueError("Output directory is missing or linked")
    paths = sorted(directory.rglob("*"))
    if any(path.is_symlink() for path in paths):
        raise ValueError("Linked output is not admitted")
    observed = {str(path.relative_to(directory)): sha(path) for path in paths if path.is_file()}
    if not observed or observed != record["outputs"]:
        raise ValueError("Compiled output inventory differs from preparation")
    runtime = json.loads((directory / "LinuxMessageBoxCaption.runtimeconfig.json").read_text())
    if runtime["runtimeOptions"]["configProperties"].get("LibreWPF.RequestedRendererMode") != record["renderer"]:
        raise ValueError("Compiled consumer renderer differs from requested renderer")
    if destination.exists() or destination.is_symlink():
        raise ValueError("Never overwrite retained preparation")
    destination.mkdir(parents=True)
    archive = destination / "compiled-consumer.zip"
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as output:
        for name in sorted(observed):
            output.write(directory / name, "application/" + name)
        output.write(receipt, "preparation.json")
    with zipfile.ZipFile(archive) as output:
        expected = {"application/" + name for name in observed} | {"preparation.json"}
        if len(output.namelist()) != len(expected) or set(output.namelist()) != expected:
            raise ValueError("Frozen archive inventory changed")
        for name, digest in observed.items():
            if hashlib.sha256(output.read("application/" + name)).hexdigest() != digest:
                raise ValueError("Frozen bytes differ: " + name)
    if observed != {str(path.relative_to(directory)): sha(path) for path in directory.rglob("*") if path.is_file()}:
        raise ValueError("Output changed while freezing")
    shutil.copyfile(receipt, destination / "preparation.json")
    build_log = directory.parent / "build.log"
    if build_log.is_file():
        shutil.copyfile(build_log, destination / "build.log")
    result = dict(schema=1, compiled=True, applicationExecuted=False, inputSent=False,
                  producerHead=record["producerHead"], renderer=record["renderer"],
                  archive=dict(name=archive.name, bytes=archive.stat().st_size, sha256=sha(archive)),
                  sources=record["sources"], outputs=observed, nativeOutputs=record["nativeOutputs"])
    (destination / "frozen-consumer.json").write_text(json.dumps(result, indent=2) + "\n")
    return result

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(freeze(args.receipt, args.destination)["archive"]))

