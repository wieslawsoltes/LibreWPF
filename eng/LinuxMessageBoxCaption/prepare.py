#!/usr/bin/env python3
"""Build the explicit Linux MessageBox caption observer from one qualified archive.

Producer checks/metadata are retained connector responses, not fetched here.
This prepares an application only; the original controllers own its execution.
"""
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
import zipfile


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--producer', type=Path, required=True)
    parser.add_argument('--renderer', choices=('NativeMilWgpu','ManagedPortable'), required=True)
    parser.add_argument('--head', required=True)
    parser.add_argument('--build', type=int, required=True)
    parser.add_argument('--docs', type=int, required=True)
    parser.add_argument('--progpu-head', required=True)
    args = parser.parse_args()
    repo = Path.cwd()
    helper = module('candidate_packages', repo / 'eng/WindowsModalDesktop/candidate-packages.py')
    package = helper.PACKAGE
    require, sha = package.require, package.sha
    require(os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF',
            'Only the disposable hosted repository runner may prepare this workload')
    require(all(re.fullmatch('[0-9a-f]{40}', value) for value in (args.head, args.progpu_head)),
            'Exact qualified source identities required')
    checks = package.read_json(args.producer / 'checks.json')['structuredContent']
    require(checks['headRevision'] == args.head and checks.get('hasMore') is False
            and not checks.get('statuses'), 'Complete exact-head checks required')
    expected = helper.CI.BOOTSTRAP_JOBS | package.BUILD_JOBS
    build_checks, docs_checks = [], []
    for check in checks['checks']:
        require(check.get('head_sha') == args.head and package.succeeded(check),
                'Unsuccessful or changed-head producer check')
        url = check.get('details_url', '')
        if f'/actions/runs/{args.build}/job/' in url:
            build_checks.append(check)
        elif f'/actions/runs/{args.docs}/job/' in url:
            docs_checks.append(check)
        else:
            raise ValueError('Unexpected producer check run')
    require(len(build_checks) == len(expected) and {c['name'] for c in build_checks} == expected,
            'All sixteen original whole-Build jobs required')
    require(len(docs_checks) == 1 and docs_checks[0]['name'] == 'Verify release documentation',
            'Exact successful Docs check required')
    artifact = package.read_json(args.producer / 'artifact.json')
    archive = args.producer / 'qualified-packages.zip'
    require(artifact['workflow_run']['head_sha'] == args.head
            and artifact['workflow_run']['id'] == args.build
            and artifact['name'] == 'librewpf-ci-packages-' + args.head
            and not artifact['expired'] and 0 < artifact['size_in_bytes'] <= 2 * 1024**3,
            'Exact whole-producer artifact identity required')
    require(archive.is_file() and not archive.is_symlink()
            and archive.stat().st_size == artifact['size_in_bytes']
            and sha(archive) == artifact['digest'].removeprefix('sha256:'),
            'Original package archive changed')
    root = Path(os.environ['RUNNER_TEMP']) / 'messagebox-caption'
    root.mkdir()
    record = dict(qualified=False, inputSent=False, applicationExecuted=False,
                  renderer=args.renderer, producerHead=args.head, producerBuild=args.build, producerDocs=args.docs,
                  artifact=artifact, sources={}, packages={}, outputs={})
    receipt = repo / 'artifacts/ubuntu26-caption-preparation.json'
    require(not receipt.exists(), 'Never overwrite earlier preparation evidence')
    try:
        feed = root / 'feed'
        feed.mkdir()
        inventory, sdk, engine = package.stage_packages(archive, feed, args.head)
        record.update(version=sdk, engineVersion=engine, stagedPackages=inventory)
        backend = feed / inventory['ProGPU.Backend']['file']
        _, _, source = package.package_identity(backend)
        require(source is not None and source.get('commit') == args.progpu_head,
                'Backend package is not the qualified source dependency')
        source_root = repo / 'eng/LinuxMessageBoxCaption/app'
        original_sources = package.read_json(repo / 'eng/LinuxMessageBoxCaption/sources.json')
        require(set(original_sources) == {f.name for f in source_root.iterdir() if f.is_file()},
                'Original application inventory changed')
        for name, expected_hash in original_sources.items():
            source = source_root / name
            require(sha(source) == expected_hash, 'Original source changed: ' + name)
            if name.endswith(('.cs', '.xaml')):
                shutil.copyfile(source, root / name)
                record['sources'][name] = expected_hash
        record['originalSourceInventory'] = original_sources
        (root / 'global.json').write_text('{"sdk":{"version":"10.0.201","rollForward":"disable"}}')
        helper.write_config(root / 'NuGet.config', feed, root / 'packages')
        package.seed_sdk(feed / inventory['LibreWPF.Sdk']['file'], root / 'packages', sdk)
        project = (source_root / 'LinuxMessageBoxCaption.csproj').read_text()
        require(project.count('LibreWPF.Sdk/0.1.0-preview.70') == 1
                and project.count('$(ProGpuWpfSdkSampleTargetFramework)') == 1,
                'Original project wrapper changed')
        project = project.replace('LibreWPF.Sdk/0.1.0-preview.70', 'LibreWPF.Sdk/' + sdk)
        project = project.replace('$(ProGpuWpfSdkSampleTargetFramework)', 'net10.0-windows')
        (root / 'LinuxMessageBoxCaption.csproj').write_text(project)
        (root / 'Directory.Build.props').write_text('<Project><PropertyGroup><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild><NoWarn>$(NoWarn);IDE0073</NoWarn></PropertyGroup></Project>')
        record['buildWrapper'] = {name: sha(root / name) for name in ('LinuxMessageBoxCaption.csproj', 'Directory.Build.props', 'global.json', 'NuGet.config')}
        environment = os.environ.copy()
        for key in list(environment):
            if key.startswith(('PROGPU', 'ProGpu', 'NUGET_', 'MSBuildSDK')):
                environment.pop(key)
        environment.update(NUGET_PACKAGES=str(root / 'packages'), NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'),
                           DOTNET_CLI_HOME=str(root / 'cli-home'), MSBUILDDISABLENODEREUSE='1')
        out = root / 'application'
        command = ['dotnet', 'build', 'LinuxMessageBoxCaption.csproj', '-c', 'Debug', '-r', 'linux-arm64',
                   '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaptionRenderer=' + args.renderer,
                   '-p:RestorePackagesPath=' + str(root / 'packages'), '-o', str(out)]
        record['command'] = command
        started = time.monotonic()
        with (root / 'build.log').open('w') as log:
            result = subprocess.run(command, cwd=root, env=environment, stdout=log,
                                    stderr=subprocess.STDOUT, timeout=600)
        record.update(buildExitCode=result.returncode, buildSeconds=time.monotonic() - started)
        require(result.returncode == 0, 'Original consumer build failed')
        expected_packages = {name.lower(): entry for name, entry in inventory.items()}
        for cached in sorted((root / 'packages').rglob('*.nupkg')):
            name, version = cached.parent.parent.name, cached.parent.name
            record['packages'][name] = dict(version=version, sha256=sha(cached))
            if name.startswith(('progpu', 'librewpf', 'librewinforms')):
                expected_entry = expected_packages[name]
                require(version == expected_entry['version'] and sha(cached) == expected_entry['sha256'],
                        'Consumer substituted a producer package: ' + name)
        with zipfile.ZipFile(backend) as payload:
            native = 'runtimes/linux-arm64/native/'
            members = [member for member in payload.namelist()
                       if member.startswith(native) and not member.endswith('/')]
            expected_native = {'libwgpu_native.so', 'LICENSE.MIT', 'LICENSE.APACHE',
                               'wgpu-LICENSE.MIT', 'wgpu-LICENSE.APACHE',
                               'webgpu-headers-LICENSE', 'wgpu-native-linux.json'}
            require(len(members) == 7 and set(members) == {native + name for name in expected_native},
                    'Complete original Linux runtime inventory required')
            native_receipt = json.loads(payload.read(native + 'wgpu-native-linux.json'))
            require(native_receipt['producerCommit'] == args.progpu_head
                    and native_receipt['rid'] == 'linux-arm64'
                    and native_receipt['abiTestsPassed'] is True
                    and native_receipt['extensionExported'] == 'progpuWgpuCreateInstanceXlibV1',
                    'Linux dependency receipt differs from the qualified producer')
            record['nativeOutputs'] = {}
            for member in members:
                target = out / Path(member).name
                expected_hash = hashlib.sha256(payload.read(member)).hexdigest()
                require(target.is_file() and sha(target) == expected_hash,
                        'Original Linux runtime output changed: ' + member)
                record['nativeOutputs'][target.name] = expected_hash
        require((out / 'LinuxMessageBoxCaption.dll').is_file(), 'Expected application output absent')
        for path in sorted(out.rglob('*')):
            if path.is_file():
                require(not path.is_symlink(), 'Linked application output')
                record['outputs'][str(path.relative_to(out))] = sha(path)
        for name, expected_hash in record['sources'].items():
            require(sha(root / name) == expected_hash, 'Original source changed during build')
        record.update(compiled=True, output=str(out), application=str(out / 'LinuxMessageBoxCaption.dll'))
        with open(os.environ['GITHUB_ENV'], 'a') as stream:
            stream.write('CAPTION_APP=' + record['application'] + '\n')
    except BaseException as error:
        record['error'] = type(error).__name__ + ': ' + str(error)
        raise
    finally:
        receipt.write_text(json.dumps(record, indent=2) + '\n')


if __name__ == '__main__':
    main()
