#!/usr/bin/env python3
"""Stage original runtime/SDK payloads from one exact ProGPU release pair."""

import argparse
import hashlib
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def digest(stream):
    value = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        value.update(block)
    return value.digest()


def stage(native, sdk, destination, version, commit):
    return stage_payloads(((native, "ProGPU.Backend.Native", "runtimes/", ""),
                           (sdk, "ProGPU.Native.Sdk", "runtimes/", "")), destination, version, commit)


def stage_dx12(package, destination, version, commit):
    return stage_payloads(((package, "ProGPU.Backend.Dx12", "buildTransitive/runtimes/",
                            "buildTransitive/runtimes/"),), destination, version, commit)


def stage_payloads(packages, destination, version, commit):
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Expected an exact ProGPU commit")
    destination = Path(destination)
    if destination.exists() or destination.is_symlink():
        raise ValueError("Native staging destination must not already exist")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="native-release-", dir=destination.parent) as temporary:
        staged = Path(temporary) / "package"
        staged.mkdir()
        published = {}
        for path, package_id, prefix, remove_prefix in packages:
            with zipfile.ZipFile(path) as archive:
                specs = [item for item in archive.infolist() if item.filename.endswith(".nuspec")]
                if len(specs) != 1 or specs[0].file_size > 1024 * 1024:
                    raise ValueError(f"{package_id}: expected one bounded nuspec")
                metadata = ET.fromstring(archive.read(specs[0])).find("{*}metadata")
                repository = None if metadata is None else metadata.find("{*}repository")
                if (metadata is None or metadata.findtext("{*}id") != package_id
                        or metadata.findtext("{*}version") != version or repository is None
                        or repository.get("commit") != commit
                        or repository.get("url", "").removesuffix(".git").rstrip("/")
                        != "https://github.com/wieslawsoltes/ProGPU"):
                    raise ValueError(f"{package_id}: release provenance mismatch")
                members = set()
                for item in archive.infolist():
                    name = item.filename
                    if not name.startswith(prefix):
                        continue
                    if item.is_dir() and name == prefix:
                        continue
                    name = name.removeprefix(remove_prefix) if remove_prefix else name
                    parts = name.rstrip("/").split("/")
                    kind = stat.S_IFMT(item.external_attr >> 16)
                    if (any(part in ("", ".", "..") for part in parts) or "\\" in name
                            or ":" in name or PurePosixPath(name).is_absolute()
                            or kind not in (0, stat.S_IFREG, stat.S_IFDIR)):
                        raise ValueError(f"{package_id}: unsafe runtime entry {name}")
                    if item.is_dir():
                        continue
                    key = name.casefold()
                    if key in members:
                        raise ValueError(f"{package_id}: duplicate runtime entry {name}")
                    members.add(key)
                    output = staged.joinpath(*parts)
                    if key in published:
                        if published[key] != name:
                            raise ValueError(f"Runtime path case collision: {name}")
                        with archive.open(item) as incoming, output.open("rb") as prior:
                            if digest(incoming) != digest(prior):
                                raise ValueError(f"Runtime payload collision: {name}")
                    else:
                        output.parent.mkdir(parents=True, exist_ok=True)
                        with archive.open(item) as incoming, output.open("xb") as target:
                            shutil.copyfileobj(incoming, target, 1024 * 1024)
                        published[key] = name
                if not members:
                    raise ValueError(f"{package_id}: missing runtime payload")
        staged.rename(destination)
    return len(published)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("native", type=Path)
    parser.add_argument("sdk", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--dx12-package", type=Path)
    parser.add_argument("--dx12-destination", type=Path)
    args = parser.parse_args()
    if (args.dx12_package is None) != (args.dx12_destination is None):
        parser.error("DX12 staging requires both its exact package and destination")
    count = stage(args.native, args.sdk, args.destination, args.version, args.commit)
    print(f"Staged {count} original native runtime/SDK/license files at {args.commit}.")
    if args.dx12_package is not None:
        count = stage_dx12(args.dx12_package, args.dx12_destination, args.version, args.commit)
        print(f"Staged {count} original DX12 runtime/compiler/license files at {args.commit}.")


if __name__ == "__main__":
    main()
