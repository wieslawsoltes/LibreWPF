#!/usr/bin/env python3
"""Retain an exact whole-CI producer for the unchanged original modal consumer."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("modal_gallery_producer", HERE.parent / "WindowsGalleryClipboardConsumer/prepare-ci.py")
CI = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CI)
PACKAGE = CI.prepare


def load_producer(directory):
    identity = PACKAGE.read_json(directory / "identity.json")
    head, build_id, docs_id = identity["head"], identity["build"], identity["docs"]
    PACKAGE.require(re.fullmatch(r"[0-9a-f]{40}", head) and type(build_id) is int and build_id > 0
                    and type(docs_id) is int and docs_id > 0, "Exact candidate identity required")
    build, docs, artifact = (PACKAGE.read_json(directory / name) for name in ("build.json", "docs.json", "artifact.json"))
    CI.validate_ci_producer(build, docs, artifact, head, build_id, docs_id)
    archive = directory / "qualified-packages.zip"
    PACKAGE.require(archive.is_file() and not archive.is_symlink()
                    and archive.stat().st_size == artifact["size_in_bytes"]
                    and PACKAGE.sha(archive) == artifact["digest"].removeprefix("sha256:"),
                    "Candidate archive differs from the original successful producer")
    return identity, archive


def stage_producer(directory, feed):
    identity, archive = load_producer(directory)
    feed.mkdir()
    inventory, sdk, engine = PACKAGE.stage_packages(archive, feed, identity["head"])
    expected = {}
    for name, entry in inventory.items():
        package = feed / entry["file"]
        _, version, repository = PACKAGE.package_identity(package)
        PACKAGE.require(repository is not None, "Candidate source identity missing: " + name)
        expected[name.lower()] = dict(version=version, sha256=entry["sha256"], repository=repository.attrib)
    return dict(identity=identity, packages=expected, inventory=inventory, sdk=sdk, engine=engine,
                archiveSha256=PACKAGE.sha(archive))


def write_config(path, feed, packages):
    config = ET.Element("configuration")
    settings = ET.SubElement(config, "config")
    ET.SubElement(settings, "add", key="globalPackagesFolder", value=str(packages))
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="qualified", value=str(feed))
    ET.SubElement(sources, "add", key="NuGet", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(config, "packageSourceMapping")
    local = ET.SubElement(mapping, "packageSource", key="qualified")
    for pattern in ("ProGPU.*", "LibreWPF.*", "LibreWinForms.*"):
        ET.SubElement(local, "package", pattern=pattern)
    ET.SubElement(ET.SubElement(mapping, "packageSource", key="NuGet"), "package", pattern="*")
    path.write_bytes(ET.tostring(config, encoding="utf-8", xml_declaration=True))


def verify_output(directory, feed, inventory, rid, compiler=None):
    PACKAGE.require(rid == "win-arm64", "Original modal runtime identity required")
    PACKAGE.require(compiler in (None, "Dxc"), "Unsupported explicit modal compiler")
    assets = {
        "PresentationCore.dll": ("LibreWPF.Transport", f"runtimes/{rid}/lib/net10.0/PresentationCore.dll"),
        "PresentationFramework.dll": ("LibreWPF.Transport", "lib/net10.0/PresentationFramework.dll"),
        "WindowsBase.dll": ("LibreWPF.Transport", "lib/net10.0/WindowsBase.dll"),
        "System.Private.Windows.Core.dll": ("LibreWPF.Transport", "lib/net10.0/System.Private.Windows.Core.dll"),
        "PresentationNative_cor3.dll": ("LibreWPF.Transport", f"runtimes/{rid}/native/PresentationNative_cor3.dll"),
        "ProGPU.Wpf.dll": ("LibreWPF.ProGPU", "lib/net10.0/ProGPU.Wpf.dll"),
        "ProGPU.Wpf.Interop.dll": ("LibreWPF.Interop", "lib/net10.0/ProGPU.Wpf.Interop.dll"),
        "ProGPU.Backend.dll": ("ProGPU.Backend", "lib/net10.0/ProGPU.Backend.dll"),
        "progpu_native.dll": ("ProGPU.Backend.Native", f"runtimes/{rid}/native/progpu_native.dll"),
    }
    if compiler:
        for name in ("wgpu_native.dll", "dxcompiler.dll", "dxil.dll", "wgpu-native-build.json",
                     "dxc-compiler-package.json", "progpu-dx12-runtime.json"):
            assets[name] = ("ProGPU.Backend.Dx12", f"buildTransitive/runtimes/{rid}/native/{name}")
        runtime = PACKAGE.read_json(directory / "ProGPU.Wpf.ModalInteractionApp.runtimeconfig.json")
        options = runtime["runtimeOptions"]["configProperties"]
        PACKAGE.require(options.get("LibreWPF.RequestedRendererMode") == "NativeMilWgpu" and
                        options.get("LibreWPF.RequestedDx12ShaderCompiler") == compiler,
                        "Modal consumer did not request native MIL with the explicit packaged compiler")
    hashes = {}
    for name, (package, member) in assets.items():
        target = directory / name
        PACKAGE.require(target.is_file() and not target.is_symlink(), "Missing candidate output: " + name)
        with zipfile.ZipFile(feed / inventory[package]["file"]) as archive:
            with archive.open(member) as stream:
                expected = hashlib.file_digest(stream, "sha256").hexdigest()
        actual = PACKAGE.sha(target)
        PACKAGE.require(actual == expected, "Consumer substituted qualified package asset: " + name)
        hashes[name] = actual
    return hashes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--head", required=True)
    parser.add_argument("--build", type=int, required=True)
    parser.add_argument("--docs", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    PACKAGE.require(os.name == "nt" and os.environ.get("GITHUB_ACTIONS") == "true"
                    and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
                    and os.environ.get("GITHUB_REPOSITORY") == CI.REPOSITORY,
                    "Candidate fetch is restricted to this repository's disposable hosted Windows runner")
    PACKAGE.require(re.fullmatch(r"[0-9a-f]{40}", args.head) and args.build > 0 and args.docs > 0,
                    "Exact candidate head and both run IDs required")
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=HERE, text=True).strip()
    PACKAGE.require(actual == args.head, "Automation must match the exact qualified candidate head")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)

    def retain(name, value):
        (output / name).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    retain("identity.json", dict(head=args.head, build=args.build, docs=args.docs))
    fields = "databaseId,name,headSha,status,conclusion,jobs"
    build = CI.gh_json("run", "view", str(args.build), "--repo", CI.REPOSITORY, "--json", fields)
    docs = CI.gh_json("run", "view", str(args.docs), "--repo", CI.REPOSITORY, "--json", fields)
    retain("build.json", build)
    retain("docs.json", docs)
    listing = CI.gh_json("api", f"repos/{CI.REPOSITORY}/actions/runs/{args.build}/artifacts?per_page=100")
    retain("artifact-list.json", listing)
    PACKAGE.require(listing.get("total_count") == len(listing.get("artifacts", [])), "Complete artifact inventory required")
    selected = [a for a in listing["artifacts"] if a["name"] == "librewpf-ci-packages-" + args.head]
    PACKAGE.require(len(selected) == 1, "One exact successful package producer required")
    artifact = CI.gh_json("api", f"repos/{CI.REPOSITORY}/actions/artifacts/{selected[0]['id']}")
    retain("artifact.json", artifact)
    # A stale upload timeline fails explicitly here. Never retry or substitute a
    # different run or infer success merely from an existing package archive.
    CI.validate_ci_producer(build, docs, artifact, args.head, args.build, args.docs)
    PACKAGE.require(0 < artifact["size_in_bytes"] <= 2 * 1024**3, "Candidate archive exceeds bound")
    with (output / "qualified-packages.zip").open("xb") as stream:
        subprocess.run(["gh", "api", f"repos/{CI.REPOSITORY}/actions/artifacts/{artifact['id']}/zip"],
                       stdout=stream, check=True, timeout=300)
    load_producer(output)
    print("Retained exact successful whole-CI package producer; no application input executed.")


if __name__ == "__main__":
    main()
