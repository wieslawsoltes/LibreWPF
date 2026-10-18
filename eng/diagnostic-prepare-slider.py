"""Prepare original Slider sources from qualified packages; never execute UI/input."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import time
import zipfile

INPUT_SHA = 'd18a86dc068fe3253db1ad54a11f14307bcc72cad62faacfe38379dcf107818c'
PROJECT_SHA = '98e72d7f9b4a8f7fd27edb03dfc06149cfb553aea98d3cf0a36efe59a833be74'


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def original_inputs(archive, manifest):
    require(sha(archive) == INPUT_SHA and manifest['archiveSha256'] == INPUT_SHA,
            'Original input archive changed')
    with zipfile.ZipFile(archive) as z:
        require(len(z.namelist()) == 19 and set(z.namelist()) == set(manifest['files']),
                'Original input inventory changed')
        result = {}
        for name, expected in manifest['files'].items():
            path = PurePosixPath(name)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in name and ':' not in name,
                    'Unsafe original input path')
            data = z.read(name)
            require(len(data) == expected['bytes'] and hashlib.sha256(data).hexdigest() == expected['sha256'],
                    'Original source changed: ' + name)
            result[name] = data
    upstream = json.loads(result['source-manifest.json'])
    require(upstream['commit'] == '811d01e95c8c929e68539d698d0a0609e94fd185'
            and len(upstream['files']) == 11, 'Original upstream inventory changed')
    for item in upstream['files']:
        data = result['original/' + item['path']]
        require(len(data) == item['bytes'] and hashlib.sha256(data).hexdigest() == item['sha256']
                and hashlib.sha1(f'blob {len(data)}\0'.encode() + data).hexdigest() == item['gitBlob'],
                'Original upstream blob changed')
    return result


def candidate_project(original, sdk, engine):
    require(hashlib.sha256(original).hexdigest() == PROJECT_SHA, 'Original project changed')
    require(all(re.fullmatch(r'[0-9A-Za-z.+-]+', v) for v in (sdk, engine)), 'Invalid selected package versions')
    text = original.decode('utf-8')
    before = ['LibreWPF.Sdk/0.1.0-preview.68', '<ProGpuPackageVersion>0.1.0-source.f1c83aea</ProGpuPackageVersion>']
    after = ['LibreWPF.Sdk/' + sdk, '<ProGpuPackageVersion>' + engine + '</ProGpuPackageVersion>']
    for old, new in zip(before, after):
        require(text.count(old) == 1, 'Ambiguous original project identity')
        text = text.replace(old, new)
    return text.encode('utf-8')


def validate_checks(checks, identity, expected):
    require(checks['headRevision'] == identity['head'] and checks.get('hasMore') is False
            and not checks.get('statuses'), 'Complete exact-head Code Review checks required')
    builds, docs = [], []
    for item in checks['checks']:
        require(item.get('head_sha') == identity['head'] and item.get('status') == 'completed'
                and item.get('conclusion') == 'success', 'Unsuccessful producer check')
        url = item.get('details_url', '')
        if f'/actions/runs/{identity["build"]}/job/' in url:
            builds.append(item)
        elif f'/actions/runs/{identity["docs"]}/job/' in url:
            docs.append(item)
        else:
            raise ValueError('Unexpected producer check run')
    require(len(builds) == len(expected) == 16 and {i['name'] for i in builds} == expected,
            'Every original whole-Build job is required')
    require(len(docs) == 1 and docs[0]['name'] == 'Verify release documentation', 'Exact Docs success required')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--producer', required=True, type=Path)
    parser.add_argument('--inputs', required=True, type=Path)
    args = parser.parse_args()
    require(os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF', 'Disposable repository runner required')
    repo = Path.cwd()
    spec = importlib.util.spec_from_file_location('slider_packages', repo/'eng/WindowsModalDesktop/candidate-packages.py')
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    package = helper.PACKAGE
    identity = package.read_json(args.producer/'identity.json')
    require(all(re.fullmatch('[0-9a-f]{40}', identity[k]) for k in ('head','progpu'))
            and all(type(identity[k]) is int and identity[k] > 0 for k in ('build','docs')), 'Exact producer identities required')
    checks = package.read_json(args.producer/'checks.json')['structuredContent']
    validate_checks(checks, identity, helper.CI.BOOTSTRAP_JOBS | package.BUILD_JOBS)
    artifact = package.read_json(args.producer/'artifact.json')
    archive = args.producer/'qualified-packages.zip'
    require(artifact['workflow_run']['id'] == identity['build']
            and artifact['workflow_run']['head_sha'] == identity['head'] and not artifact['expired']
            and artifact['name'] == 'librewpf-ci-packages-' + identity['head']
            and 0 < artifact['size_in_bytes'] <= 2*1024**3, 'Exact successful artifact required')
    require(archive.is_file() and not archive.is_symlink() and archive.stat().st_size == artifact['size_in_bytes']
            and sha(archive) == artifact['digest'].removeprefix('sha256:'), 'Qualified package archive changed')
    inputs = original_inputs(args.inputs/'original-slider-inputs.zip', package.read_json(args.inputs/'original-inputs.json'))
    root = Path(os.environ['RUNNER_TEMP'])/'original-slider-candidate'
    root.mkdir()
    record = dict(qualified=False, applicationExecuted=False, inputSent=False, candidateInputAuthorized=False,
                  producer=identity, artifact=artifact, sources={}, outputs={}, packages={}, compiled=False)
    receipt = root/'preparation.json'
    try:
        for name,data in inputs.items():
            target = root/name
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_bytes(data)
            record['sources'][name] = hashlib.sha256(data).hexdigest()
        feed = root/'feed'
        feed.mkdir()
        inventory,sdk,engine = package.stage_packages(archive, feed, identity['head'])
        record.update(stagedPackages=inventory, version=sdk, engineVersion=engine)
        backend = feed/inventory['ProGPU.Backend']['file']
        _,_,source = package.package_identity(backend)
        require(source is not None and source.get('commit') == identity['progpu'], 'Backend source mismatch')
        project = candidate_project(inputs['GallerySliderProbe.csproj'],sdk,engine)
        (root/'original-project.csproj.txt').write_bytes(inputs['GallerySliderProbe.csproj'])
        (root/'GallerySliderProbe.csproj').write_bytes(project)
        record['projectChange'] = dict(original=PROJECT_SHA, selected=hashlib.sha256(project).hexdigest(),
                                       allowed=['literal SDK version','literal engine package version'])
        helper.write_config(root/'NuGet.config',feed,root/'packages')
        package.seed_sdk(feed/inventory['LibreWPF.Sdk']['file'],root/'packages',sdk)
        environment = os.environ.copy()
        for key in list(environment):
            if key.startswith(('DOTNET_', 'MSBuildSDK', 'DYLD_', 'LD_LIBRARY_PATH', 'ProGpu', 'PROGPU', 'NUGET_', 'NuGetPackageRoot')):
                environment.pop(key)
        environment.update(DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
            DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',NUGET_PACKAGES=str(root/'packages'),
            NUGET_HTTP_CACHE_PATH=str(root/'http-cache'),DOTNET_CLI_HOME=str(root/'cli-home'),MSBUILDDISABLENODEREUSE='1')
        command = ['dotnet','build','GallerySliderProbe.csproj','-c','Release','-r','linux-arm64','-m:1','-nr:false',
                   '-p:UseSharedCompilation=false','-p:PlatformTarget=arm64','-p:AppendRuntimeIdentifierToOutputPath=false',
                   '-p:RestoreConfigFile='+str(root/'NuGet.config'),'-p:RestorePackagesPath='+str(root/'packages'),
                   '-p:RestoreFallbackFolders=','-p:RestoreAdditionalProjectSources=']
        record['command'] = command
        started = time.monotonic()
        with (root/'build.log').open('x') as log:
            result = subprocess.run(command,cwd=root,env=environment,stdout=log,stderr=subprocess.STDOUT,timeout=300)
        record.update(buildExitCode=result.returncode,buildSeconds=time.monotonic()-started)
        require(result.returncode == 0,'Original consumer compilation failed')
        out = root/'bin/Release/net10.0-windows'
        runtime = package.read_json(out/'GallerySliderProbe.runtimeconfig.json')
        require(runtime['runtimeOptions']['configProperties']['LibreWPF.RequestedRendererMode']=='NativeMilWgpu',
                'Original renderer changed')
        elf = (out/'GallerySliderProbe').read_bytes()
        require(elf[:4]==b'\x7fELF' and int.from_bytes(elf[18:20],'little')==183,'Original Linux ARM64 apphost required')
        selected = {name.lower():entry for name,entry in inventory.items()}
        for cached in sorted((root/'packages').rglob('*.nupkg')):
            name,version = cached.parent.parent.name,cached.parent.name
            record['packages'][name] = dict(version=version,sha256=sha(cached))
            if name.startswith(('progpu','librewpf','librewinforms')):
                require(version==selected[name]['version'] and sha(cached)==selected[name]['sha256'],'Substituted package: '+name)
        assets = {'PresentationCore.dll':('LibreWPF.Transport','lib/net10.0/PresentationCore.dll'),
            'PresentationFramework.dll':('LibreWPF.Transport','lib/net10.0/PresentationFramework.dll'),
            'ProGPU.Wpf.dll':('LibreWPF.ProGPU','lib/net10.0/ProGPU.Wpf.dll'),
            'libprogpu_native.so':('ProGPU.Backend.Native','runtimes/linux-arm64/native/libprogpu_native.so')}
        for name in ['libwgpu_native.so','LICENSE.MIT','LICENSE.APACHE','wgpu-LICENSE.MIT','wgpu-LICENSE.APACHE',
                     'webgpu-headers-LICENSE','wgpu-native-linux.json']:
            assets[name] = ('ProGPU.Backend','runtimes/linux-arm64/native/'+name)
        for name,(owner,member) in assets.items():
            with zipfile.ZipFile(feed/inventory[owner]['file']) as z:
                require(sha(out/name)==hashlib.sha256(z.read(member)).hexdigest(),'Package output differs: '+name)
        native = package.read_json(out/'wgpu-native-linux.json')
        require(native['producerCommit']==identity['progpu'] and native['rid']=='linux-arm64'
                and native['abiTestsPassed'] is True and native['extensionExported']=='progpuWgpuCreateInstanceXlibV1',
                'Native runtime provenance mismatch')
        for path in sorted(out.rglob('*')):
            if path.is_file():
                require(not path.is_symlink(),'Linked output')
                record['outputs'][str(path.relative_to(out))] = sha(path)
        for name,original_hash in record['sources'].items():
            if name != 'GallerySliderProbe.csproj':
                require(sha(root/name)==original_hash,'Original source changed during compilation')
        require((root/'GallerySliderProbe.csproj').read_bytes()==project,'Selected project changed during compilation')
        record.update(compiled=True,output=str(out),application=str(out/'GallerySliderProbe'))
    except BaseException as error:
        record['error'] = type(error).__name__ + ': ' + str(error)
        raise
    finally:
        receipt.write_text(json.dumps(record,indent=2)+'\n')


if __name__ == '__main__':
    main()
