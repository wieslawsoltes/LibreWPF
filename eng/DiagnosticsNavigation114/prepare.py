"""Diagnostic-only original navigation consumer from a complete qualified producer."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--producer', type=Path, required=True)
    p.add_argument('--head', required=True)
    p.add_argument('--build', type=int, required=True)
    p.add_argument('--docs', type=int, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--dotnet', type=Path, required=True)
    a = p.parse_args()
    producer = a.producer.resolve()
    ci = module('original_prepare_ci', producer / 'eng/WindowsGalleryClipboardConsumer/prepare-ci.py')
    original = ci.prepare
    require = original.require
    require(os.name == 'nt' and os.environ.get('GITHUB_ACTIONS') == 'true'
            and os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('GITHUB_REPOSITORY') == ci.REPOSITORY,
            'Only the disposable repository Windows runner is admitted')
    require(subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=producer, text=True).strip() == a.head,
            'Qualified producer checkout differs')
    require(original.sha(ROOT / 'GalleryNavigationApp.cs') == '3e23d181d9c4b75c5bd43a7029a3d603dff6e52240222cac0ae112e062414ae7',
            'Original read-only observer changed')
    require(original.sha(ROOT / 'original-desktop.py') == '83e2a53e880b39c73f82d26c93d0dd79f769c61908054817cf838f1c1ae7e9bc',
            'Original Win32 input/capture helper changed')
    out = a.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    inputs = out / 'inputs'; inputs.mkdir()
    receipt = dict(head=a.head, build=a.build, docs=a.docs, qualified=False, clipboardAccessed=False)
    def retain(name, value):
        (inputs / name).write_text(json.dumps(value, indent=2), encoding='utf-8')
    try:
        fields = 'databaseId,name,headSha,status,conclusion,jobs'
        build = ci.gh_json('run', 'view', str(a.build), '--repo', ci.REPOSITORY, '--json', fields)
        docs = ci.gh_json('run', 'view', str(a.docs), '--repo', ci.REPOSITORY, '--json', fields)
        retain('producer-build.json', build); retain('producer-docs.json', docs)
        listing = ci.gh_json('api', f'repos/{ci.REPOSITORY}/actions/runs/{a.build}/artifacts?per_page=100')
        retain('artifact-list.json', listing)
        require(listing['total_count'] == len(listing['artifacts']), 'Complete artifact listing required')
        chosen = [x for x in listing['artifacts'] if x['name'] == 'librewpf-ci-packages-' + a.head]
        require(len(chosen) == 1, 'One exact producer archive required')
        artifact = ci.gh_json('api', f'repos/{ci.REPOSITORY}/actions/artifacts/{chosen[0]["id"]}')
        retain('producer-artifact.json', artifact)
        ci.validate_ci_producer(build, docs, artifact, a.head, a.build, a.docs)
        require(0 < artifact['size_in_bytes'] <= 2 * 1024**3, 'Archive size bound')
        archive = out / 'qualified-packages.zip'
        with archive.open('xb') as stream:
            subprocess.run(['gh', 'api', f'repos/{ci.REPOSITORY}/actions/artifacts/{artifact["id"]}/zip'],
                           stdout=stream, check=True, timeout=300)
        require(archive.stat().st_size == artifact['size_in_bytes']
                and original.sha(archive) == artifact['digest'].removeprefix('sha256:'), 'Producer archive digest mismatch')
        receipt['archiveSha256'] = original.sha(archive)
        feed, cache, app = out / 'feed', out / 'packages', out / 'consumer'
        feed.mkdir(); cache.mkdir(); app.mkdir()
        inventory, sdk, engine = original.stage_packages(archive, feed, a.head)
        receipt['packages'] = inventory
        original.seed_sdk(feed / inventory['LibreWPF.Sdk']['file'], cache, sdk)
        manifest = original.read_json(ROOT / 'source-manifest.json')
        require(manifest['repository'] == 'microsoft/WPF-Samples'
                and manifest['commit'] == '811d01e95c8c929e68539d698d0a0609e94fd185'
                and len(manifest['files']) == 15, 'Original source inventory differs')
        for entry in manifest['files']:
            rel = Path(entry['path'])
            require(not rel.is_absolute() and '..' not in rel.parts, 'Unsafe original path')
            upstream = entry['path'] if entry['path'] == 'LICENSE' else 'Sample Applications/WPFGallery/' + entry['path']
            url = f'https://raw.githubusercontent.com/{manifest["repository"]}/{manifest["commit"]}/' + urllib.parse.quote(upstream, safe='/')
            with urllib.request.urlopen(url, timeout=30) as stream:
                data = stream.read(entry['bytes'] + 1)
            require(len(data) == entry['bytes'] and hashlib.sha256(data).hexdigest() == entry['sha256']
                    and hashlib.sha1(f'blob {len(data)}\0'.encode() + data).hexdigest() == entry['gitBlobSha'],
                    'Original upstream bytes differ: ' + entry['path'])
            dest = app / 'original' / rel
            dest.parent.mkdir(parents=True, exist_ok=True); dest.write_bytes(data)
        shutil.copyfile(ROOT / 'source-manifest.json', inputs / 'source-manifest.json')
        shutil.copyfile(ROOT / 'GalleryNavigationApp.cs', app / 'GalleryNavigationApp.cs')
        project = app / 'GalleryNavigationApp.csproj'
        project.write_text((ROOT / 'GalleryNavigationApp.csproj.template').read_text()
                           .replace('@SDK_VERSION@', sdk).replace('@ENGINE_VERSION@', engine))
        compiler = original.read_json(producer / 'global.json')['sdk']['version']
        (app / 'global.json').write_text(json.dumps({'sdk': {'version': compiler, 'rollForward': 'disable', 'allowPrerelease': True}}))
        config = ET.parse(producer / 'samples/ProGPU.Wpf.ShowcaseApp/NuGet.config')
        config.find("./config/add[@key='globalPackagesFolder']").set('value', str(cache))
        config.find("./packageSources/add[@key='ProGPUWpfLocalArtifacts']").set('value', str(feed))
        mapping = ET.SubElement(config.getroot(), 'packageSourceMapping')
        for source in config.findall('./packageSources/add'):
            item = ET.SubElement(mapping, 'packageSource', key=source.get('key'))
            for pattern in (('LibreWPF*', 'LibreWinForms*', 'ProGPU*') if source.get('key') == 'ProGPUWpfLocalArtifacts' else ('*',)):
                ET.SubElement(item, 'package', pattern=pattern)
        config.write(app / 'NuGet.config', encoding='utf-8', xml_declaration=True)
        env = os.environ.copy(); env.update(original.BUILD_ENVIRONMENT)
        for key, folder in [('NUGET_PACKAGES', 'packages'), ('DOTNET_CLI_HOME', 'cli-home'), ('TEMP', 'temp'), ('TMP', 'temp'), ('TMPDIR', 'temp')]:
            directory = out / folder; directory.mkdir(exist_ok=True); env[key] = str(directory)
        command = original.build_command(a.dotnet, project, 'arm64', app / 'NuGet.config', cache)
        receipt['command'] = command
        with (inputs / 'build.log').open('xb') as log:
            built = subprocess.run(command, cwd=app, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=300)
        receipt['buildExitCode'] = built.returncode
        require(built.returncode == 0, 'Original navigation consumer compilation failed; see retained build.log')
        output = app / original.OUTPUT_RELATIVE
        checks = {'PresentationCore.dll': ('LibreWPF.Transport', 'runtimes/win-arm64/lib/net10.0/PresentationCore.dll'),
                  'PresentationFramework.dll': ('LibreWPF.Transport', 'lib/net10.0/PresentationFramework.dll'),
                  'WindowsBase.dll': ('LibreWPF.Transport', 'lib/net10.0/WindowsBase.dll'),
                  'System.Private.Windows.Core.dll': ('LibreWPF.Transport', 'lib/net10.0/System.Private.Windows.Core.dll'),
                  'PresentationNative_cor3.dll': ('LibreWPF.Transport', 'runtimes/win-arm64/native/PresentationNative_cor3.dll'),
                  'ProGPU.Wpf.dll': ('LibreWPF.ProGPU', 'lib/net10.0/ProGPU.Wpf.dll'),
                  'ProGPU.Wpf.Interop.dll': ('LibreWPF.Interop', 'lib/net10.0/ProGPU.Wpf.Interop.dll'),
                  'progpu_native.dll': ('ProGPU.Backend.Native', 'runtimes/win-arm64/native/progpu_native.dll')}
        for name, (package, member) in checks.items():
            with zipfile.ZipFile(feed / inventory[package]['file']) as z, z.open(member) as stream:
                require(original.sha(output / name) == hashlib.file_digest(stream, 'sha256').hexdigest(), 'Substituted package asset: ' + name)
        exe = output / 'GalleryNavigationApp.exe'
        data = exe.read_bytes(); offset = int.from_bytes(data[60:64], 'little')
        require(data[:2] == b'MZ' and data[offset:offset+6] == b'PE\0\0\x64\xaa', 'Original apphost must be native Windows ARM64')
        runtime = original.read_json(output / 'GalleryNavigationApp.runtimeconfig.json')
        require(runtime['runtimeOptions']['configProperties']['LibreWPF.RequestedRendererMode'] == 'NativeMilWgpu', 'Original native MIL mode required')
        receipt['output'] = str(output)
        receipt['files'] = [{'path': str(p.relative_to(output)), 'bytes': p.stat().st_size, 'sha256': original.sha(p)}
                            for p in sorted(output.rglob('*')) if p.is_file()]
        require(0 < len(receipt['files']) <= 1024, 'Compiled payload inventory bound')
        receipt['compiled'] = True
    except BaseException as error:
        receipt['error'] = str(error)
        raise
    finally:
        (inputs / 'preparation.json').write_text(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    main()
