#!/usr/bin/env python3
"""Build the original pair with a versioned read-only observer correction."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile

HERE = Path(__file__).resolve().parent
OBSERVER = "samples/ProGPU.Wpf.ModalInteractionApp/ModalObservation.cs"
PROJECT = "samples/ProGPU.Wpf.ModalInteractionApp/ProGPU.Wpf.ModalInteractionApp.csproj"
DXC_PROPERTY = b'''    <ProGpuWpfDx12ShaderCompiler Condition="'$(RuntimeIdentifier)' == 'win-arm64' Or '$(RuntimeIdentifier)' == 'win-x64'">Dxc</ProGpuWpfDx12ShaderCompiler>\n'''


def require(value, message):
    if not value:
        raise RuntimeError(message)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def verify_sources(checkout, commit, files):
    actual = subprocess.check_output(["git", "-C", str(checkout), "rev-parse", "HEAD"], text=True).strip()
    require(actual == commit, "The original source checkout must retain its exact commit")
    for name, digest in files.items():
        path = checkout / name
        require(path.is_file() and not path.is_symlink(), "Original source file is unavailable: " + name)
        actual_digest = sha(path)
        require(actual_digest == digest,
                f"Original source bytes changed: {name}; expected {digest}, actual {actual_digest}")


def inventory(directory):
    files = sorted(path for path in directory.rglob("*") if path.is_file())
    require(0 < len(files) <= 1000, "Unexpected application payload inventory")
    return [{"path": path.relative_to(directory).as_posix(), "sha256": sha(path),
             "bytes": path.stat().st_size} for path in files]


def stage_sources(source, root, manifest, selected_observer):
    selected = dict(manifest["source"])
    for name, expected in manifest["source"].items():
        require(sha(source / name) == expected, "Original source changed before staging: " + name)
        for directory in (root / "original-source", root / "consumer"):
            target = directory / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source / name, target)
    # Preserve the original failed observer as evidence. Both applications use
    # this same explicit v2 observer. Application code and scenarios retain their
    # bytes; explicit candidate SDK/compiler selection is independently verified below.
    require(selected_observer.is_file() and not selected_observer.is_symlink(), "Selected observer is unavailable")
    shutil.copy2(selected_observer, root / "consumer" / OBSERVER)
    selected[OBSERVER] = sha(root / "consumer" / OBSERVER)
    return selected


def selected_project_bytes(root, manifest, sdk_version, compiler=None):
    require(re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?", sdk_version) is not None,
            "Selected SDK must retain a literal package version")
    original = (root / "original-source" / PROJECT).read_bytes()
    marker = ('<Project Sdk="LibreWPF.Sdk/' + manifest["version"] + '">').encode()
    require(original.startswith(marker) and original.count(marker) == 1,
            "Original SDK project selector is not the retained contract")
    selected = ('<Project Sdk="LibreWPF.Sdk/' + sdk_version + '">').encode()
    result = original.replace(marker, selected, 1)
    require(compiler in (None, "Dxc"), "Unsupported explicit modal compiler")
    if compiler:
        anchor = b'    <ProGpuWpfNativeMilHitTesting>true</ProGpuWpfNativeMilHitTesting>\n'
        require(result.count(anchor) == 1 and b'ProGpuWpfDx12ShaderCompiler' not in result,
                "Original native-MIL compiler selection anchor changed")
        result = result.replace(anchor, anchor + DXC_PROPERTY, 1)
    return result


def stage_sdk_selection(root, manifest, selected, sdk_version, compiler=None):
    project = root / "consumer" / PROJECT
    require(sha(project) == manifest["source"][PROJECT], "Original project changed before package selection")
    project.write_bytes(selected_project_bytes(root, manifest, sdk_version, compiler))
    selected[PROJECT] = sha(project)
    return dict(path=PROJECT, originalVersion=manifest["version"], selectedVersion=sdk_version,
                originalSha256=manifest["source"][PROJECT], selectedSha256=selected[PROJECT], compiler=compiler)


def verify_staged_sources(root, manifest, selected, sdk_version=None, compiler=None):
    require(compiler is None or sdk_version is not None, "Explicit compiler requires a qualified candidate")
    require(set(selected) == set(manifest["source"]), "Selected source inventory changed")
    for name, value in selected.items():
        if name == OBSERVER:
            continue
        expected = (hashlib.sha256(selected_project_bytes(root, manifest, sdk_version, compiler)).hexdigest()
                    if name == PROJECT and sdk_version is not None else manifest["source"][name])
        require(value == expected, "Only the explicit read-only observer and exact selected SDK reference/compiler may differ")
    for name, expected in manifest["source"].items():
        require(sha(root / "original-source" / name) == expected, "Preserved original source changed: " + name)
        require(sha(root / "consumer" / name) == selected[name], "Selected source changed: " + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--forms-helper-checkout", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    parser.add_argument("--candidate-inputs", type=Path)
    parser.add_argument("--dx12-compiler", choices=["Dxc"])
    args = parser.parse_args()
    require(args.dx12_compiler is None or args.candidate_inputs is not None,
            "Explicit DXC requires the complete qualified candidate package closure")
    require(os.name == "nt" and os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF",
            "Preparation is restricted to this repository's disposable Windows runner")
    manifest = json.loads((HERE / "public-inputs.json").read_text(encoding="utf-8"))
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    save(root / "public-inputs.json", manifest)
    try:
        source, helper = args.source.resolve(strict=True), args.forms_helper_checkout.resolve(strict=True)
        verify_sources(source, manifest["wpfSource"], manifest["source"])
        verify_sources(helper, manifest["formsHelperSource"], manifest["helpers"])
    except Exception as error:
        save(root / "preparation.json", dict(prepared=False, qualified=False, error=str(error)))
        raise
    consumer = root / "consumer"
    selected_sources = stage_sources(source, root, manifest, HERE.parents[1] / OBSERVER)
    automation = {}
    for original, name in [(HERE.parent / "progpu-wpf-modal-desktop.py", "eng/hosted-progpu-wpf-modal-desktop.py"),
                           (HERE.parent / "WindowsGalleryClipboardConsumer/run-windows.py", "eng/WindowsGalleryClipboardConsumer/run-windows.py"),
                           (HERE / "prepare-desktop.py", "eng/WindowsModalDesktop/prepare-desktop.py"),
                           (HERE / "shell-escape.py", "eng/WindowsModalDesktop/shell-escape.py"),
                           (HERE / "native-window-observation.py", "eng/WindowsModalDesktop/native-window-observation.py")]:
        target = consumer / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, target)
        automation[name] = sha(target)
    # This is the same isolated package-consumer configuration as the original
    # public preview.69 build. Candidate SDK/compiler selection is explicit below.
    (consumer / "Directory.Build.props").write_text(
        "<Project><PropertyGroup><ProGpuWpfSdkSampleTargetFramework>net10.0</ProGpuWpfSdkSampleTargetFramework></PropertyGroup></Project>",
        encoding="utf-8")
    save(consumer / "global.json", {"sdk": {"version": manifest["sdk"], "rollForward": "disable", "allowPrerelease": False}})
    (consumer / "NuGet.Config").write_text(
        '<configuration><packageSources><clear/><add key="NuGet" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>',
        encoding="utf-8")
    candidate = None
    candidate_module = None
    if args.candidate_inputs:
        spec = importlib.util.spec_from_file_location("modal_candidate", HERE / "candidate-packages.py")
        candidate_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(candidate_module)
        candidate = candidate_module.stage_producer(args.candidate_inputs.resolve(strict=True), root / "feed")
        actual = subprocess.check_output(["git", "-C", str(HERE), "rev-parse", "HEAD"], text=True).strip()
        require(candidate["identity"]["head"] == actual, "Candidate consumer automation must match the qualified producer")
        candidate["sdkSelection"] = stage_sdk_selection(root, manifest, selected_sources, candidate["sdk"], args.dx12_compiler)
    env = os.environ.copy()
    for name in list(env):
        if name.startswith(("DOTNET_", "MSBuildSDK", "NUGET_", "NuGetPackageRoot", "ProGpu", "PROGPU",
                            "LibreWpf", "LibreWPF", "LIBREWPF", "LibreWinForms", "LIBREWINFORMS")):
            env.pop(name)
    dotnet = args.dotnet.resolve(strict=True)
    env.update(DOTNET_ROOT=str(dotnet.parent), DOTNET_CLI_HOME=str(root / "cli-home"),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
               DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false", NUGET_PACKAGES=str(consumer / "artifacts/nuget/ProGPU.Wpf.ModalInteractionApp"),
               NUGET_HTTP_CACHE_PATH=str(root / "http-cache"), MSBUILDDISABLENODEREUSE="1")
    if candidate:
        packages = Path(env["NUGET_PACKAGES"])
        candidate_module.write_config(consumer / "NuGet.Config", root / "feed", packages)
        candidate_module.PACKAGE.seed_sdk(root / "feed" / candidate["inventory"]["LibreWPF.Sdk"]["file"], packages, candidate["sdk"])
    require(subprocess.check_output([str(dotnet), "--version"], cwd=consumer, env=env, text=True).strip() == manifest["sdk"],
            "Original exact consumer SDK is required")
    receipt = dict(prepared=False, qualified=False, dotnet=str(dotnet), source=manifest["wpfSource"],
                   helper=str(helper), helperCommit=manifest["formsHelperSource"], builds=[], payloads={},
                   packageSource="qualified-whole-ci" if candidate else "original-public-preview69",
                   candidate=candidate,
                   selectedSourceHashes=selected_sources,
                   observerRevision=dict(schema="wpf-modal-desktop-v2", originalSha256=manifest["source"][OBSERVER],
                                         selectedSha256=selected_sources[OBSERVER],
                                         reason="Original allocated client slot, source modal input policy and read-only actual host compiler identity"),
                   hostedAutomation=automation, automationCommit=subprocess.check_output(
                       ["git", "-C", str(HERE), "rev-parse", "HEAD"], text=True).strip())
    save(root / "preparation.json", receipt)
    (root / "runtime-inventory.txt").write_text(subprocess.check_output([str(dotnet), "--list-runtimes"], text=True), encoding="utf-8")
    for kind, name, tfm in [("microsoft", "ProGPU.Wpf.ModalInteractionApp.Windows", "net10.0-windows"),
                            ("portable", "ProGPU.Wpf.ModalInteractionApp", "net10.0")]:
        project = consumer / "samples" / name / (name + ".csproj")
        command = [str(dotnet), "build", str(project), "-c", "Release", "-r", manifest["runtimeIdentifier"],
                   "-p:UseSharedCompilation=false", "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
                   "-m:1", "--disable-build-servers", "--nologo", "-v:minimal"]
        if candidate and kind == "portable":
            command += ["-p:ProGpuPackageVersion=" + candidate["engine"],
                        "-p:RestoreFallbackFolders=", "-p:RestoreAdditionalProjectSources="]
        started = time.monotonic()
        log = root / (kind + "-build.log")
        with log.open("xb") as stream:
            result = subprocess.run(command, cwd=consumer, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=180)
        receipt["builds"].append(dict(kind=kind, command=command, exitCode=result.returncode, seconds=time.monotonic() - started))
        save(root / "preparation.json", receipt)
        require(result.returncode == 0, "Original build failed; preserve " + str(log))
        require(not any(code in log.read_text(encoding="utf-8-sig") for code in ("NU1900", "NU1901", "NU1902", "NU1903", "NU1904", "NU1905")),
                "Package audit is not clean")
        # Microsoft SDK includes RID in output; the portable SDK keeps its normal
        # package-consumer output path. Never relabel or replace runtime assets.
        directory = project.parent / "bin/Release" / tfm
        if kind == "microsoft":
            directory /= manifest["runtimeIdentifier"]
        app = directory / (name + ".exe")
        require(app.is_file(), "Expected original apphost output is missing")
        receipt["payloads"][kind] = dict(directory=str(directory), app=str(app), files=inventory(directory))
        if candidate and kind == "portable":
            receipt["candidateOutputHashes"] = candidate_module.verify_output(
                directory, root / "feed", candidate["inventory"], manifest["runtimeIdentifier"], args.dx12_compiler)
    restored = {}
    package_root = Path(env["NUGET_PACKAGES"])
    for package in sorted(package_root.glob("*/*/*.nupkg")):
        name, version = package.parent.parent.name, package.parent.name
        if not name.startswith(("progpu.", "librewpf.", "librewinforms.")):
            continue
        expected = (candidate["packages"] if candidate else manifest["packages"]).get(name)
        require(expected is not None and version == expected["version"] and sha(package) == expected["sha256"],
                "Restored product package differs from the selected verified producer: " + name)
        with zipfile.ZipFile(package) as archive:
            metadata = ET.fromstring(archive.read(next(x for x in archive.namelist() if x.endswith(".nuspec"))))
            repository = metadata.find("{*}metadata/{*}repository")
            require(repository is not None and repository.attrib == expected["repository"], "Package source identity changed")
        restored[name] = expected
    require({"librewpf.sdk", "librewpf.transport", "librewpf.progpu", "librewpf.interop", "progpu.backend",
             "progpu.backend.native", "progpu.scene.native"}.issubset(restored),
            "The original native-MIL product package closure is incomplete")
    verify_staged_sources(root, manifest, selected_sources, candidate["sdk"] if candidate else None, args.dx12_compiler)
    verify_sources(helper, manifest["formsHelperSource"], manifest["helpers"])
    receipt.update(prepared=True, packages=restored, manifestSha256=sha(root / "public-inputs.json"),
                   driver=str(consumer / "eng/hosted-progpu-wpf-modal-desktop.py"))
    save(root / "preparation.json", receipt)
    print("Prepared original applications with the same versioned observer and verified selected package closure; no UI executed.")


if __name__ == "__main__":
    main()
