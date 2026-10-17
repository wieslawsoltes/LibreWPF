#!/usr/bin/env python3
"""Build and observe the original taskbar source pair on disposable Windows."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time
import zipfile
import source
import compare


def require(value, message):
    if not value: raise RuntimeError(message)


def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def project(kind, candidate):
    portable = kind == 'portable'
    sdk = 'LibreWPF.Sdk/' + candidate['sdk'] if portable else 'Microsoft.NET.Sdk'
    tfm = 'net10.0' if portable else 'net10.0-windows'
    extra = '''<ProGpuWpfRendererMode>NativeMilWgpu</ProGpuWpfRendererMode>
<ProGpuWpfNativeMilHitTesting>true</ProGpuWpfNativeMilHitTesting>
<ProGpuWpfDx12ShaderCompiler>Dxc</ProGpuWpfDx12ShaderCompiler>
<DefineConstants>$(DefineConstants);LIBREWPF_TASKBAR</DefineConstants>''' if portable else ''
    return f'''<Project Sdk="{sdk}"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>{tfm}</TargetFramework><UseWPF>true</UseWPF>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<RuntimeIdentifier>win-arm64</RuntimeIdentifier><SelfContained>false</SelfContained>{extra}
</PropertyGroup></Project>'''


def parse_result(path):
    text = path.read_text(encoding='utf-8-sig')
    # Runtime diagnostics may precede stdout JSON. Require exactly one complete
    # result ending at EOF, rather than accepting a stale or partial transcript.
    marker = '{\n  "diagnosticOnly": true,'
    require(text.count(marker) == 1, 'One complete original result required')
    return json.loads(text[text.index(marker):])


def verify_compilers(result, directory, expected_hash):
    hosts = result.get('hostCompilers', {})
    require(len(hosts) == 3, 'Exact three source host compiler observations required')
    expected_hosts = {str(result['records'][0]['native']['handle']), str(result['records'][4]['sourceOwner']),
                      str(result['records'][6]['sourceOwner'])}
    require(set(hosts) == expected_hosts, 'Compiler witnesses differ from original source windows')
    for value in hosts.values():
        library = Path(value['library']).resolve(strict=True)
        require(value['selected'] == 'Dxc' and library == (directory / 'dxcompiler.dll').resolve(strict=True)
                and sha(library) == expected_hash, 'Actual host substituted its qualified DXC library')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--producer-tools', type=Path, required=True)
    parser.add_argument('--candidate-inputs', type=Path, required=True)
    parser.add_argument('--original-checkout', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    args = parser.parse_args()
    require(os.name == 'nt' and os.environ.get('GITHUB_ACTIONS') == 'true' and
            os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted' and
            os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF', 'Disposable hosted Windows required')
    root = args.output.resolve()
    root.mkdir(parents=True, exist_ok=False)
    receipt = dict(diagnosticOnly=True, qualified=False, prepared=False, builds=[], runs=[])
    save(root / 'receipt.json', receipt)
    try:
        original_checkout = args.original_checkout.resolve(strict=True)
        require(subprocess.check_output(['git', '-C', str(original_checkout), 'rev-parse', 'HEAD'], text=True).strip()
                == source.ORIGINAL_COMMIT, 'Original source commit changed')
        original, selected = source.select((original_checkout / source.ORIGINAL_PATH).read_bytes())
        (root / 'original.cs').write_bytes(original)
        (root / 'selected.cs').write_bytes(selected)
        receipt['source'] = dict(commit=source.ORIGINAL_COMMIT, originalSha256=sha(root / 'original.cs'),
                                 selectedSha256=sha(root / 'selected.cs'), observerOnly=True)
        tools = args.producer_tools.resolve(strict=True)
        spec = importlib.util.spec_from_file_location('taskbar_candidate', tools / 'eng/WindowsModalDesktop/candidate-packages.py')
        candidate_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(candidate_module)
        candidate = candidate_module.stage_producer(args.candidate_inputs.resolve(strict=True), root / 'feed')
        require(subprocess.check_output(['git', '-C', str(tools), 'rev-parse', 'HEAD'], text=True).strip()
                == candidate['identity']['head'], 'Package verifier is not the exact qualified producer')
        receipt['candidate'] = candidate
        packages = root / 'packages'
        candidate_module.write_config(root / 'NuGet.Config', root / 'feed', packages)
        candidate_module.PACKAGE.seed_sdk(root / 'feed' / candidate['inventory']['LibreWPF.Sdk']['file'], packages, candidate['sdk'])
        save(root / 'global.json', dict(sdk=dict(version='10.0.201', rollForward='disable', allowPrerelease=False)))
        env = os.environ.copy()
        for name in list(env):
            if name.startswith(('DOTNET_', 'MSBuildSDK', 'NUGET_', 'NuGetPackageRoot', 'ProGpu', 'PROGPU',
                                'LibreWpf', 'LibreWPF', 'LIBREWPF', 'LibreWinForms', 'LIBREWINFORMS')): env.pop(name)
        dotnet = args.dotnet.resolve(strict=True)
        env.update(DOTNET_ROOT=str(dotnet.parent), DOTNET_CLI_HOME=str(root / 'cli-home'),
                   DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                   DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false', NUGET_PACKAGES=str(packages),
                   NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), MSBUILDDISABLENODEREUSE='1')
        require(subprocess.check_output([str(dotnet), '--version'], cwd=root, env=env, text=True).strip() == '10.0.201',
                'Original exact consumer SDK required')
        payloads = {}
        for kind in ('microsoft', 'portable'):
            folder = root / kind
            folder.mkdir()
            (folder / 'Program.cs').write_bytes(selected)
            (folder / 'TaskbarPair.csproj').write_text(project(kind, candidate), encoding='utf-8')
            command = [str(dotnet), 'build', str(folder / 'TaskbarPair.csproj'), '-c', 'Release', '-r', 'win-arm64',
                       '-p:UseSharedCompilation=false', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all',
                       '-p:RestoreFallbackFolders=', '-p:RestoreAdditionalProjectSources=', '-m:1', '--disable-build-servers',
                       '--nologo', '-v:minimal']
            if kind == 'portable': command += ['-p:ProGpuPackageVersion=' + candidate['engine']]
            start = time.monotonic()
            with (root / (kind + '-build.log')).open('xb') as log:
                result = subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=180)
            receipt['builds'].append(dict(kind=kind, exitCode=result.returncode, seconds=time.monotonic()-start, command=command))
            save(root / 'receipt.json', receipt)
            require(result.returncode == 0, 'Source pair build failed: ' + kind)
            require(not any(code in (root / (kind + '-build.log')).read_text(encoding='utf-8-sig')
                            for code in ('NU1900','NU1901','NU1902','NU1903','NU1904','NU1905')), 'Package audit failed')
            directory = folder / ('bin/Release/net10.0' if kind == 'portable' else 'bin/Release/net10.0-windows/win-arm64')
            require((directory / 'TaskbarPair.exe').is_file(), 'Actual apphost missing')
            payloads[kind] = directory
        directory = payloads['portable']
        hashes = candidate_module.verify_output(directory, root / 'feed', candidate['inventory'], 'win-arm64')
        with zipfile.ZipFile(root / 'feed' / candidate['inventory']['ProGPU.Backend.Dx12']['file']) as archive:
            for name in ('wgpu_native.dll','dxcompiler.dll','dxil.dll','wgpu-native-build.json','dxc-compiler-package.json','progpu-dx12-runtime.json'):
                expected = hashlib.sha256(archive.read('buildTransitive/runtimes/win-arm64/native/' + name)).hexdigest()
                require(sha(directory / name) == expected, 'Qualified DXC payload changed: ' + name)
                hashes[name] = expected
        options = json.loads((directory / 'TaskbarPair.runtimeconfig.json').read_text())['runtimeOptions']['configProperties']
        require(options.get('LibreWPF.RequestedRendererMode') == 'NativeMilWgpu' and
                options.get('LibreWPF.RequestedDx12ShaderCompiler') == 'Dxc', 'Explicit candidate compiler configuration missing')
        restored = {}
        for package in sorted(packages.glob('*/*/*.nupkg')):
            name = package.parent.parent.name
            if not name.startswith(('progpu.','librewpf.','librewinforms.')): continue
            expected = candidate['packages'].get(name)
            require(expected and package.parent.name == expected['version'] and sha(package) == expected['sha256'],
                    'Restored package changed: ' + name)
            _, _, repository = candidate_module.PACKAGE.package_identity(package)
            require(repository is not None and repository.attrib == expected['repository'], 'Restored package source changed')
            restored[name] = expected
        require({'librewpf.sdk','librewpf.transport','librewpf.progpu','librewpf.interop','progpu.backend',
                 'progpu.backend.native','progpu.backend.dx12','progpu.scene.native'}.issubset(restored), 'Incomplete source/native package closure')
        for kind in payloads:
            require((root / kind / 'Program.cs').read_bytes() == selected, 'Source observer changed during build')
        receipt.update(prepared=True, packages=restored, payloadHashes=hashes)
        save(root / 'receipt.json', receipt)
        results = {}
        for kind, directory in payloads.items():
            start = time.monotonic()
            with (root / (kind + '-stdout.log')).open('xb') as stdout, (root / (kind + '-stderr.log')).open('xb') as stderr:
                result = subprocess.run([str(directory / 'TaskbarPair.exe')], cwd=directory, env=env,
                                        stdout=stdout, stderr=stderr, timeout=30)
            receipt['runs'].append(dict(kind=kind, exitCode=result.returncode, seconds=time.monotonic()-start,
                                       processDeadlineSeconds=30, originalInternalDeadlineSeconds=20))
            save(root / 'receipt.json', receipt)
            results[kind] = parse_result(root / (kind + '-stdout.log'))
            save(root / (kind + '-observations.json'), results[kind])
            require(result.returncode == 0, 'Original source scenario failed: ' + kind)
            compare.normalize(results[kind])
            if kind == 'portable': verify_compilers(results[kind], directory, hashes['dxcompiler.dll'])
        receipt['comparison'] = compare.compare(results['microsoft'], results['portable'])
        save(root / 'receipt.json', receipt)
        print('Original source taskbar pair matched; modal input and pixel qualification remain independent.')
    except Exception as error:
        receipt['failure'] = repr(error)
        save(root / 'receipt.json', receipt)
        raise


if __name__ == '__main__': main()
