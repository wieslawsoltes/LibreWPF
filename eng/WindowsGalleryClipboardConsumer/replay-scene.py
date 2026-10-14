"""Separate bounded Windows replay of the two original failed Gallery scenes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent
REPO = "wieslawsoltes/LibreWPF"
HEAD = "ce5dccd151e4e141239f4dc545a3b1c09bd19157"
RUN = 38489878685
ARTIFACT = 11861089232
ARCHIVE_SHA = "b0d00231a42e473ca22368312f079d87fb9bd492ef700fd09c9f9e68dc8469f2"
SCENES = ["f9fb7c73acb86d730add0e812b82219658811829e63f45bdc3ca1066b0cc773c",
          "4c54ccf28fd236ff7b1193e4680f6cf49a804a3e3ce64a3e74df373e21a2e8ed"]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case-root", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    args = parser.parse_args()
    require(os.name == "nt" and os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and os.environ.get("GITHUB_REPOSITORY") == REPO, "Disposable Windows CI only")
    root = args.case_root.resolve() / "scene-replay"
    root.mkdir()
    consumer = args.case_root.resolve() / "consumer"
    prepared = json.loads((consumer / "preparation.json").read_text())
    require(prepared["head"] == HEAD and prepared["build"] == 38480990649
            and prepared["docs"] == 38480990595 and prepared["compiled"], "Exact whole-qualified producer required")
    original = consumer / "bin/Release/net10.0-windows"
    for name, digest in prepared["output"]["sha256"].items():
        require(sha(original / name) == digest, "Original prepared output changed")

    def api(path, name):
        result = subprocess.run(["gh", "api", f"repos/{REPO}/{path}"], check=True,
                                capture_output=True, timeout=60)
        require(len(result.stdout) <= 1024 * 1024, "Receipt bound")
        (root / name).write_bytes(result.stdout)
        return json.loads(result.stdout)

    run = api(f"actions/runs/{RUN}", "source-run.json")
    require(run["id"] == RUN and run["status"] == "completed" and run["conclusion"] == "failure"
            and run["head_sha"] == "8ce4beea6067e258d56d36990cc87a2fe5bb27b6"
            and run["name"] == "Diagnostic only — Gallery " + HEAD, "Exact original diagnostic required")
    artifact = api(f"actions/artifacts/{ARTIFACT}", "source-artifact.json")
    require(artifact["workflow_run"]["id"] == RUN and not artifact["expired"]
            and artifact["size_in_bytes"] == 256582 and artifact["digest"] == "sha256:" + ARCHIVE_SHA,
            "Original diagnostic artifact identity changed")
    archive = root / "original.zip"
    with archive.open("xb") as stream:
        subprocess.run(["gh", "api", f"repos/{REPO}/actions/artifacts/{ARTIFACT}/zip"],
                       stdout=stream, check=True, timeout=60)
    require(archive.stat().st_size == 256582 and sha(archive) == ARCHIVE_SHA, "Original archive digest changed")
    scenes = []
    with zipfile.ZipFile(archive) as zip_file:
        for i, digest in enumerate(SCENES, 1):
            name = f"runtime/scene-frame-{i}"
            info = zip_file.getinfo(name + ".bin")
            require(0 < info.file_size <= 32 * 1024 * 1024, "Original scene bound")
            path = root / f"scene-frame-{i}.bin"
            path.write_bytes(zip_file.read(info))
            require(sha(path) == digest, "Original scene digest changed")
            metadata = json.loads(zip_file.read(name + ".json"))
            require(metadata["bitmapExternalImages"] == 0 and metadata["adapter"] == "Microsoft Basic Render Driver"
                    and metadata["request"]["DpiScaleX"] == metadata["request"]["DpiScaleY"] == 1,
                    "Original capture has external resources or unsupported presentation")
            (root / f"scene-frame-{i}.json").write_text(json.dumps(metadata, indent=2))
            scenes.append(path)
    build = root / "build"
    build.mkdir()
    versions = subprocess.check_output([str(args.dotnet), "--list-sdks"], text=True).splitlines()
    sdk = max((v.split()[0] for v in versions if v.startswith("10.") and "-" not in v.split()[0]),
              key=lambda v: tuple(map(int, v.split("."))))
    (build / "global.json").write_text(json.dumps({"sdk": {"version": sdk, "rollForward": "disable"}}))
    (build / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    shutil.copyfile(ROOT / "SceneReplay.cs", build / "SceneReplay.cs")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for name, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "ImplicitUsings": "enable",
                        "Nullable": "enable", "EnableDefaultCompileItems": "false", "UseAppHost": "false"}.items():
        ET.SubElement(properties, name).text = value
    items = ET.SubElement(project, "ItemGroup")
    ET.SubElement(items, "Compile", Include="SceneReplay.cs")
    references = [original / n for n in ("ProGPU.Backend.dll", "ProGPU.Backend.Native.dll", "ProGPU.Backend.Dawn.dll")]
    references += sorted(original.glob("Silk.NET.*.dll"))
    require(len(references) >= 5 and all(p.is_file() for p in references), "Original typed dependencies unavailable")
    for path in references:
        reference = ET.SubElement(items, "Reference", Include=path.stem)
        ET.SubElement(reference, "HintPath").text = str(path)
        ET.SubElement(reference, "Private").text = "false"
    project_path = build / "SceneReplay.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    with (root / "build.log").open("xb") as log:
        subprocess.run([str(args.dotnet), "build", str(project_path), "-c", "Release", "-m:1",
                        "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                        "-p:UseSharedCompilation=false", "--disable-build-servers"], cwd=build,
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
    files = [p for p in original.rglob("*") if p.is_file()]
    require(len(files) <= 2000 and sum(p.stat().st_size for p in files) <= 1024**3
            and all(not p.is_symlink() for p in original.rglob("*")), "Original output inventory bound")
    manifest = {p.relative_to(original).as_posix(): sha(p) for p in files}
    runtime = root / "runtime"
    shutil.copytree(original, runtime)
    require(all(sha(runtime / n) == digest for n, digest in manifest.items()), "Isolated copy changed bytes")
    dll = runtime / "SceneReplay.dll"
    require(not dll.exists(), "Replay assembly collision")
    shutil.copyfile(build / "bin/Release/net10.0/SceneReplay.dll", dll)
    evidence = root / "evidence"
    evidence.mkdir()
    environment = os.environ.copy()
    require(not environment.get("DOTNET_STARTUP_HOOKS"), "Unexpected startup hook")
    environment.update(PROGPU_NATIVE_TRACE_COMPUTE="1", PROGPU_NATIVE_TRACE_SCENE_ENCODE="1",
                       PROGPU_NATIVE_TRACE_VECTOR_CLIP="1")
    command = [str(args.dotnet), "exec", "--runtimeconfig", str(runtime / "GalleryClipboardApp.runtimeconfig.json"),
               "--depsfile", str(runtime / "GalleryClipboardApp.deps.json"), str(dll),
               *(str(p) for p in scenes), str(evidence)]
    receipt = {"diagnosticOnly": True, "qualified": False, "producerHead": HEAD,
               "sourceRun": RUN, "sceneSha256": SCENES, "originalInventory": manifest,
               "observerSha256": sha(ROOT / "SceneReplay.cs"), "dllSha256": sha(dll), "sdk": sdk,
               "applicationLaunched": False, "inputInjected": False, "deadlineSeconds": 60}
    (root / "preparation.json").write_text(json.dumps(receipt, indent=2))
    started = time.monotonic()
    with (root / "runtime.log").open("xb") as log:
        try:
            result = subprocess.run(command, env=environment, cwd=runtime, stdout=log,
                                    stderr=subprocess.STDOUT, timeout=60)
            receipt.update(exitCode=result.returncode, timedOut=False)
        except subprocess.TimeoutExpired:
            receipt.update(exitCode=None, timedOut=True)
    receipt["elapsedSeconds"] = time.monotonic() - started
    receipt["originalFilesUnchanged"] = all(sha(original / n) == digest for n, digest in manifest.items())
    (root / "receipt.json").write_text(json.dumps(receipt, indent=2))
    require(receipt["originalFilesUnchanged"], "Original application files changed")
    print(json.dumps({k: v for k, v in receipt.items() if k != "originalInventory"}))
    return 1 if receipt["timedOut"] or receipt["exitCode"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
