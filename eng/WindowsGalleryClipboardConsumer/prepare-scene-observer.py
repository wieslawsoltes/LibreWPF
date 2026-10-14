"""Compile an isolated diagnostic observer, without touching the qualified consumer."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--dotnet", required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir()
    versions = [line.split()[0] for line in subprocess.check_output(
        [str(args.dotnet), "--list-sdks"], text=True).splitlines()]
    version = max((v for v in versions if v.startswith("10.") and "-" not in v),
                  key=lambda v: tuple(map(int, v.split("."))))
    (args.output / "global.json").write_text(json.dumps({"sdk": {"version": version, "rollForward": "disable"}}))
    (args.output / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    source = Path(__file__).with_name("SceneObserver.cs")
    shutil.copyfile(source, args.output / source.name)
    project = args.output / "GallerySceneObserver.csproj"
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
</PropertyGroup><ItemGroup><Compile Include="SceneObserver.cs" /></ItemGroup></Project>''')
    with (args.output / "build.log").open("xb") as log:
        subprocess.run([str(args.dotnet), "build", str(project), "-c", "Release", "-m:1",
            "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
            "-p:UseSharedCompilation=false", "--disable-build-servers"], cwd=args.output,
            stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
    dll = args.output / "bin/Release/net10.0/GallerySceneObserver.dll"
    receipt = {"diagnosticOnly": True, "qualified": False, "sdk": version,
               "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
               "dll": str(dll), "dllSha256": hashlib.sha256(dll.read_bytes()).hexdigest()}
    (args.output / "preparation.json").write_text(json.dumps(receipt, indent=2))
    print(json.dumps(receipt))


if __name__ == "__main__":
    main()
