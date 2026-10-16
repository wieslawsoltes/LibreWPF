#!/usr/bin/env python3
"""Build the unchanged Microsoft/public-package pair without launching either app."""
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

HERE = Path(__file__).resolve().parent


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
        require(path.is_file() and not path.is_symlink() and sha(path) == digest,
                "Original source bytes changed: " + name)


def inventory(directory):
    files = sorted(path for path in directory.rglob("*") if path.is_file())
    require(0 < len(files) <= 1000, "Unexpected application payload inventory")
    return [{"path": path.relative_to(directory).as_posix(), "sha256": sha(path),
             "bytes": path.stat().st_size} for path in files]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--forms-helper-checkout", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", type=Path, required=True)
    args = parser.parse_args()
    require(os.name == "nt" and os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF",
            "Preparation is restricted to this repository's disposable Windows runner")
    manifest = json.loads((HERE / "public-inputs.json").read_text(encoding="utf-8"))
    source, helper = args.source.resolve(strict=True), args.forms_helper_checkout.resolve(strict=True)
    verify_sources(source, manifest["wpfSource"], manifest["source"])
    verify_sources(helper, manifest["formsHelperSource"], manifest["helpers"])
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    save(root / "public-inputs.json", manifest)
    consumer = root / "consumer"
    for name in manifest["source"]:
        target = consumer / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / name, target)
    # This is the same isolated package-consumer configuration as the original
    # public preview.69 build. Application/project/observer files stay unchanged.
    (consumer / "Directory.Build.props").write_text(
        "<Project><PropertyGroup><ProGpuWpfSdkSampleTargetFramework>net10.0</ProGpuWpfSdkSampleTargetFramework></PropertyGroup></Project>",
        encoding="utf-8")
    save(consumer / "global.json", {"sdk": {"version": manifest["sdk"], "rollForward": "disable", "allowPrerelease": False}})
    (consumer / "NuGet.Config").write_text(
        '<configuration><packageSources><clear/><add key="NuGet" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>',
        encoding="utf-8")
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
    require(subprocess.check_output([str(dotnet), "--version"], cwd=consumer, env=env, text=True).strip() == manifest["sdk"],
            "Original exact consumer SDK is required")
    receipt = dict(prepared=False, qualified=False, dotnet=str(dotnet), source=manifest["wpfSource"],
                   helper=str(helper), helperCommit=manifest["formsHelperSource"], builds=[], payloads={})
    save(root / "preparation.json", receipt)
    (root / "runtime-inventory.txt").write_text(subprocess.check_output([str(dotnet), "--list-runtimes"], text=True), encoding="utf-8")
    for kind, name, tfm in [("microsoft", "ProGPU.Wpf.ModalInteractionApp.Windows", "net10.0-windows"),
                            ("portable", "ProGPU.Wpf.ModalInteractionApp", "net10.0")]:
        project = consumer / "samples" / name / (name + ".csproj")
        command = [str(dotnet), "build", str(project), "-c", "Release", "-r", manifest["runtimeIdentifier"],
                   "-p:UseSharedCompilation=false", "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
                   "-m:1", "--disable-build-servers", "--nologo", "-v:minimal"]
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
    restored = {}
    package_root = Path(env["NUGET_PACKAGES"])
    for package in sorted(package_root.glob("*/*/*.nupkg")):
        name, version = package.parent.parent.name, package.parent.name
        if not name.startswith(("progpu.", "librewpf.", "librewinforms.")):
            continue
        expected = manifest["packages"].get(name)
        require(expected is not None and version == expected["version"] and sha(package) == expected["sha256"],
                "Restored product package differs from the verified published release: " + name)
        with zipfile.ZipFile(package) as archive:
            metadata = ET.fromstring(archive.read(next(x for x in archive.namelist() if x.endswith(".nuspec"))))
            repository = metadata.find("{*}metadata/{*}repository")
            require(repository is not None and repository.attrib == expected["repository"], "Package source identity changed")
        restored[name] = expected
    require({"librewpf.sdk", "librewpf.transport", "librewpf.progpu", "librewpf.interop", "progpu.backend",
             "progpu.backend.native", "progpu.scene.native"}.issubset(restored),
            "The original native-MIL product package closure is incomplete")
    for name, digest in manifest["source"].items():
        require(sha(consumer / name) == digest, "Original application/driver source changed during build")
    verify_sources(helper, manifest["formsHelperSource"], manifest["helpers"])
    receipt.update(prepared=True, packages=restored, manifestSha256=sha(root / "public-inputs.json"),
                   driver=str(consumer / "eng/progpu-wpf-modal-desktop.py"))
    save(root / "preparation.json", receipt)
    print("Prepared the unchanged paired application and verified public package closure; no UI executed.")


if __name__ == "__main__":
    main()
