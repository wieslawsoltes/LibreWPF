"""Bounded compiler comparison; never Gallery/application qualification."""
from pathlib import Path
import datetime
import hashlib
import json
import os
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile

SOURCE = Path(__file__).resolve().parent
VERSION = '0.1.0-preview.69'
HEAD = 'aef75180bcdc3b937d004b6b949e0e91724c7981'
SCENES = ['f9fb7c73acb86d730add0e812b82219658811829e63f45bdc3ca1066b0cc773c',
          '4c54ccf28fd236ff7b1193e4680f6cf49a804a3e3ce64a3e74df373e21a2e8ed']
RGB = ['f30a1a5872001308c611389e7f2382dfe551aafe62adeb798ea6388a5cee3cbb',
       'f7b87f03257fc37daf345505794f852c6f69ac2fe93ecd51b673dce29c93226d']

def sha(path):
    with path.open('rb') as file:
        return hashlib.file_digest(file, 'sha256').hexdigest()

def save(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n')

def main():
    assert os.name == 'nt'
    assert all(os.environ.get(k) == v for k, v in {
        'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
        'GITHUB_REPOSITORY': 'wieslawsoltes/LibreWPF', 'RUNNER_OS': 'Windows',
        'RUNNER_ARCH': 'ARM64'}.items())
    root = Path(os.environ['RUNNER_TEMP']) / 'gallery-compiler-comparison'
    root.mkdir(exist_ok=False)
    evidence = root / 'evidence'; evidence.mkdir()
    producer = json.loads((SOURCE / 'qualified-producer.json').read_text())
    assert producer['head'] == HEAD and producer['build'] == 38549555561
    assert producer['wholeCIQualified'] and producer['status'] == 'completed' and producer['conclusion'] == 'success'
    assert len(producer['jobs']) == 59 and len({j['id'] for j in producer['jobs']}) == 59
    assert all(j['status'] == 'completed' and j['conclusion'] == 'success' for j in producer['jobs'])
    shutil.copy2(SOURCE / 'qualified-producer.json', evidence)
    shutil.copy2(SOURCE / 'qualified-public-packages.json', evidence)
    public = {p['id'].lower(): p for p in json.loads((SOURCE / 'qualified-public-packages.json').read_text())['packages']}

    def api(path, name):
        data = subprocess.check_output(['gh', 'api', 'repos/wieslawsoltes/LibreWPF/' + path], timeout=60)
        assert len(data) <= 1024 * 1024
        (evidence / name).write_bytes(data)
        return json.loads(data)

    meta = api('actions/artifacts/11861089232', 'original-artifact.json')
    assert meta['workflow_run']['id'] == 38489878685 and not meta['expired']
    assert meta['size_in_bytes'] == 256582
    archive_hash = 'b0d00231a42e473ca22368312f079d87fb9bd492ef700fd09c9f9e68dc8469f2'
    assert meta['digest'] == 'sha256:' + archive_hash
    archive = root / 'original.zip'
    with archive.open('xb') as file:
        subprocess.run(['gh', 'api', 'repos/wieslawsoltes/LibreWPF/actions/artifacts/11861089232/zip'],
                       stdout=file, timeout=60, check=True)
    assert archive.stat().st_size == meta['size_in_bytes'] and sha(archive) == archive_hash
    scenes = []
    with zipfile.ZipFile(archive) as zipped:
        for i, digest in enumerate(SCENES, 1):
            name = f'runtime/scene-frame-{i}'
            info = zipped.getinfo(name + '.bin'); assert info.file_size < 32 * 1024 * 1024
            target = root / f'scene-frame-{i}.bin'; target.write_bytes(zipped.read(info))
            assert sha(target) == digest
            observed = json.loads(zipped.read(name + '.json'))
            assert observed['bitmapExternalImages'] == 0
            assert observed['adapter'] == 'Microsoft Basic Render Driver'
            assert observed['request']['DpiScaleX'] == observed['request']['DpiScaleY'] == 1
            save(evidence / f'original-frame-{i}.json', observed); scenes.append(target)

    env = dict(os.environ)
    for key in list(env):
        if key.startswith(('PROGPU_', 'DOTNET_', 'NUGET_', 'MSBuildSDK', 'MSBUILD')):
            env.pop(key)
    packages = root / 'packages'
    env.update(NUGET_PACKAGES=str(packages), DOTNET_CLI_HOME=str(root / 'cli'),
               NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
               MSBUILDDISABLENODEREUSE='1')
    dotnet = shutil.which('dotnet'); assert dotnet
    (evidence / 'dotnet-info.txt').write_bytes(subprocess.check_output([dotnet, '--info'], env=env, timeout=30))
    inventory = {}
    runtimes = {}
    for variant in ['stock', 'feature']:
        work = root / variant; work.mkdir()
        save(work / 'global.json', {'sdk': {'version': '10.0.201', 'rollForward': 'disable'}})
        (work / 'NuGet.Config').write_text('<configuration><packageSources><clear />'
            '<add key="NuGet" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>')
        shutil.copy2(SOURCE / 'SceneReplay.cs', work)
        project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
        props = ET.SubElement(project, 'PropertyGroup')
        for k, v in {'OutputType': 'Exe', 'TargetFramework': 'net10.0', 'RuntimeIdentifier': 'win-arm64',
                     'ImplicitUsings': 'enable', 'Nullable': 'enable', 'UseAppHost': 'false',
                     'SelfContained': 'false', 'EnableDefaultCompileItems': 'false',
                     'ManagePackageVersionsCentrally': 'false'}.items():
            ET.SubElement(props, k).text = v
        items = ET.SubElement(project, 'ItemGroup'); ET.SubElement(items, 'Compile', Include='SceneReplay.cs')
        ET.SubElement(items, 'PackageReference', Include='ProGPU.Backend.Native', Version=VERSION)
        if variant == 'feature':
            ET.SubElement(items, 'PackageReference', Include='ProGPU.Backend.Dx12', Version=VERSION)
        path = work / 'SceneReplay.csproj'; ET.ElementTree(project).write(path, encoding='utf-8', xml_declaration=True)
        shutil.copy2(path, evidence / f'{variant}.csproj')
        with (evidence / f'{variant}-build.log').open('xb') as log:
            subprocess.run([dotnet, 'build', str(path), '-c', 'Release', '-m:1', '-nr:false',
                '-p:UseSharedCompilation=false', '-p:ImportDirectoryBuildProps=false',
                '-p:ImportDirectoryBuildTargets=false'], cwd=work, env=env, stdout=log,
                stderr=subprocess.STDOUT, timeout=180, check=True)
        assets = json.loads((work / 'obj/project.assets.json').read_text())
        for name, lib in assets['libraries'].items():
            package_id, version = name.split('/')
            if not package_id.startswith('ProGPU.'):
                continue
            expected = public[package_id.lower()]
            assert version == VERSION == expected['version'] and expected['repository']['commit'] == HEAD
            nupkg = packages / lib['path'] / f'{package_id.lower()}.{version}.nupkg'
            assert nupkg.stat().st_size == expected['publicPackage']['bytes']
            assert sha(nupkg) == expected['publicPackage']['sha256']
        runtime = work / 'bin/Release/net10.0/win-arm64'; runtimes[variant] = runtime
        inventory[variant] = {str(p.relative_to(runtime)): sha(p) for p in runtime.rglob('*') if p.is_file()}
        for name in ['SceneReplay.deps.json', 'SceneReplay.runtimeconfig.json']:
            shutil.copy2(runtime / name, evidence / f'{variant}-{name}')
        shutil.copy2(work / 'obj/project.assets.json', evidence / f'{variant}-project.assets.json')
    for name in ['ProGPU.Backend.dll', 'ProGPU.Backend.Native.dll', 'progpu_native.dll']:
        assert inventory['stock'][name] == inventory['feature'][name]
    save(evidence / 'runtime-inventories.json', inventory)
    plan = [('stock', 'Fxc'), ('feature', 'Fxc'), ('feature', 'Dxc'), ('feature', 'Dxc'), ('feature', 'Fxc')]
    receipt = {'diagnosticOnly': True, 'qualified': False, 'applicationLaunched': False, 'inputInjected': False,
        'at': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'producer': HEAD, 'packageVersion': VERSION,
        'applicationLimitSeconds': 60, 'originalInternalLimitSeconds': 55, 'fixedPlan': plan,
        'sourceSha256': sha(SOURCE / 'SceneReplay.cs'), 'originalScenes': SCENES, 'expectedRgb': RGB, 'cases': []}
    save(evidence / 'receipt.json', receipt)
    for index, (variant, compiler) in enumerate(plan):
        output = evidence / f'{index}-{variant}-{compiler}'; output.mkdir()
        runtime = runtimes[variant]
        case_env = dict(env, PROGPU_DX12_SHADER_COMPILER=compiler.lower(), GALLERY_EXPECTED_COMPILER=compiler)
        start = time.monotonic(); row = {'index': index, 'runtime': variant, 'compiler': compiler}
        with (output / 'runtime.log').open('xb') as log:
            try:
                completed = subprocess.run([dotnet, str(runtime / 'SceneReplay.dll'), *(str(p) for p in scenes), str(output)],
                    cwd=runtime, env=case_env, stdout=log, stderr=subprocess.STDOUT, timeout=60)
                row.update(exitCode=completed.returncode, timedOut=False)
            except subprocess.TimeoutExpired:
                row.update(exitCode=None, timedOut=True)
        row['seconds'] = time.monotonic() - start
        row['originalRgbMatch'] = []
        for i, expected in enumerate(RGB, 1):
            pixel = output / f'frame-{i}-pixels.json'
            row['originalRgbMatch'].append(pixel.is_file() and json.loads(pixel.read_text())['rgbSha256'].lower() == expected)
        row['runtimeUnchanged'] = all(sha(runtime / name) == digest for name, digest in inventory[variant].items())
        module_receipt = output / 'loaded-modules.json'
        row['loadedModulesMatch'] = False
        if module_receipt.is_file():
            modules = {m['ModuleName'].lower(): m for m in json.loads(module_receipt.read_text())}
            required = ['wgpu_native.dll', 'progpu_native.dll']
            if compiler == 'Dxc':
                required += ['dxcompiler.dll', 'dxil.dll']
            row['loadedModulesMatch'] = all(name in modules and
                modules[name]['sha256'].lower() == inventory[variant][name] and
                Path(modules[name]['FileName']).resolve() == (runtime / name).resolve() for name in required)
        receipt['cases'].append(row); save(evidence / 'receipt.json', receipt)
        print(json.dumps(row), flush=True)
    receipt['allOriginalPixelsMatch'] = all(all(c['originalRgbMatch']) for c in receipt['cases'])
    receipt['allOriginalBoundsPass'] = all(c['exitCode'] == 0 and not c['timedOut'] and
        c['runtimeUnchanged'] and c['loadedModulesMatch'] for c in receipt['cases'])
    save(evidence / 'receipt.json', receipt)
    return 0 if receipt['allOriginalPixelsMatch'] and receipt['allOriginalBoundsPass'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
