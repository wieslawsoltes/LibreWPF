"""Retain actual popup scenes beside the unchanged qualified pair; never acceptance."""
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()


@contextmanager
def portable_hook(app, digest, observer, evidence, records):
    original = subprocess.Popen
    def launch(*args, **kwargs):
        command = args[0] if args else kwargs.get('args')
        if isinstance(command, (list, tuple)) and command and str(command[0]) == str(app):
            if records or sha(app) != digest:
                raise RuntimeError('Portable diagnostic requires one exact unchanged original child')
            environment = dict(os.environ if kwargs.get('env') is None else kwargs['env'])
            if environment.get('DOTNET_STARTUP_HOOKS'):
                raise RuntimeError('Unexpected preexisting startup hook')
            environment.update(DOTNET_STARTUP_HOOKS=str(observer),
                               PROGPU_MODAL_POPUP_OBSERVER_ROOT=str(evidence),
                               PROGPU_MODAL_POPUP_OBSERVER_APP_SHA256=digest)
            kwargs = dict(kwargs, env=environment)
            records.append(dict(app=str(app), appSha256=digest, observerSha256=sha(observer)))
        return original(*args, **kwargs)
    subprocess.Popen = launch
    try: yield
    finally: subprocess.Popen = original


def prepare_observer(output, dotnet):
    output.mkdir(exist_ok=False)
    source = Path(__file__).with_name('PopupSceneObserver.cs')
    shutil.copyfile(source, output / source.name)
    (output / 'global.json').write_text(json.dumps({'sdk': {'version': '10.0.201', 'rollForward': 'disable'}}))
    (output / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    project = output / 'PopupSceneObserver.csproj'
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
</PropertyGroup><ItemGroup><Compile Include="PopupSceneObserver.cs" /></ItemGroup></Project>''')
    with (output / 'build.log').open('xb') as log:
        subprocess.run([str(dotnet), 'build', str(project), '-c', 'Release', '-m:1',
                        '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false',
                        '-p:UseSharedCompilation=false', '--disable-build-servers'],
                       cwd=output, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
    dll = output / 'bin/Release/net10.0/PopupSceneObserver.dll'
    (output / 'preparation.json').write_text(json.dumps(dict(diagnosticOnly=True, qualified=False,
        sdk='10.0.201', sourceSha256=sha(source), dllSha256=sha(dll)), indent=2))
    return dll


def replay_original_scene(output, evidence, dotnet, app):
    """Isolated offscreen replay after the original pair; never modifies its result."""
    scenes = [p for p in evidence.glob('*.bin') if sha(p) ==
              '74c2500ba093488403782be426fb24c63856b2beb89f46f3a5c8037e969bd117']
    if not scenes:
        return dict(skipped='The exact original popup scene was not observed')
    root = output / 'offscreen-replay'; root.mkdir()
    source = Path(__file__).with_name('PopupSceneReplay.cs')
    shutil.copyfile(source, root / source.name)
    (root / 'global.json').write_text(json.dumps({'sdk': {'version': '10.0.201', 'rollForward': 'disable'}}))
    (root / 'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    props = ET.SubElement(project, 'PropertyGroup')
    for key, value in dict(OutputType='Exe', TargetFramework='net10.0', ImplicitUsings='enable',
                           Nullable='enable', TreatWarningsAsErrors='true', EnableDefaultCompileItems='false').items():
        ET.SubElement(props, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(items, 'Compile', Include=source.name)
    for name in ('ProGPU.Backend', 'ProGPU.Backend.Native', 'ProGPU.Backend.Dawn',
                 'Silk.NET.Core', 'Silk.NET.WebGPU', 'Silk.NET.Windowing.Common', 'Silk.NET.Maths'):
        reference = ET.SubElement(items, 'Reference', Include=name)
        ET.SubElement(reference, 'HintPath').text = str(app.parent / (name + '.dll'))
        ET.SubElement(reference, 'Private').text = 'false'
    project_path = root / 'Replay.csproj'; ET.ElementTree(project).write(project_path)
    with (root / 'build.log').open('xb') as log:
        subprocess.run([str(dotnet), 'build', str(project_path), '-c', 'Release', '-m:1',
                        '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false',
                        '-p:UseSharedCompilation=false', '--disable-build-servers'], cwd=root,
                       stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
    runtime = root / 'runtime'
    original = {p.relative_to(app.parent).as_posix(): sha(p) for p in app.parent.rglob('*') if p.is_file()}
    if not 0 < len(original) <= 1000:
        raise RuntimeError('Original replay payload inventory exceeds bound')
    record = dict(diagnosticOnly=True, qualified=False, sceneSha256=sha(scenes[0]),
                  sourceSha256=sha(source), originalPayload=original, deadlineSeconds=30)
    try:
        shutil.copytree(app.parent, runtime)
        for name, digest in original.items():
            if sha(runtime / name) != digest: raise RuntimeError('Replay payload bytes changed')
        replay = runtime / 'Replay.dll'
        if replay.exists(): raise RuntimeError('Replay would replace an original payload')
        shutil.copyfile(root / 'bin/Release/net10.0/Replay.dll', replay)
        with (root / 'run.log').open('xb') as log:
            process = subprocess.run([str(dotnet), 'exec', '--runtimeconfig',
                str(runtime / (app.stem + '.runtimeconfig.json')), '--depsfile',
                str(runtime / (app.stem + '.deps.json')), str(replay), str(scenes[0]),
                str(root / 'evidence')], cwd=runtime, stdout=log, stderr=subprocess.STDOUT, timeout=30)
        record['exitCode'] = process.returncode
        for name, digest in original.items():
            if sha(app.parent / name) != digest or sha(runtime / name) != digest:
                raise RuntimeError('Original/replay payload changed during diagnostic')
        return record
    finally:
        (root / 'receipt.json').write_text(json.dumps(record, indent=2))
        if runtime.exists(): shutil.rmtree(runtime)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--producer-tools', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--replay-only', action='store_true')
    args = parser.parse_args()
    if os.name != 'nt' or any(os.environ.get(k) != v for k, v in {
        'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
        'GITHUB_REPOSITORY': 'wieslawsoltes/LibreWPF'}.items()):
        raise RuntimeError('Exact disposable hosted Windows runner required')
    root, tools = args.prepared.resolve(strict=True), args.producer_tools.resolve(strict=True)
    preparation = json.loads((root / 'preparation.json').read_text())
    if (preparation['candidate']['identity']['head'] != '023e2f9ace74c4b3da8191311551adbbd8698a78' or
            preparation['candidate']['sdkSelection']['compiler'] != 'Dxc' or
            os.environ.get('PROGPU_WPF_TRACE_INPUT') != '0'):
        raise RuntimeError('Exact producer with approved retirement readiness and hosted Start activation required')
    output = args.output.resolve(); output.mkdir(exist_ok=False)
    app = Path(preparation['payloads']['portable']['app']).resolve(strict=True)
    if args.replay_only:
        evidence = output / 'evidence'; evidence.mkdir()
        source = Path(__file__).with_name('original-popup-39022755735.bin')
        if sha(source) != '74c2500ba093488403782be426fb24c63856b2beb89f46f3a5c8037e969bd117':
            raise RuntimeError('Retained original popup scene changed')
        shutil.copyfile(source, evidence / source.name)
        record = dict(diagnosticOnly=True, qualified=False, originalControllerExecuted=False,
                      applicationLaunched=False, nativeInputUsed=False,
                      producer=preparation['candidate']['identity'])
        try:
            record['offscreenReplay'] = replay_original_scene(output, evidence, args.dotnet, app)
            if record['offscreenReplay'].get('exitCode') != 0:
                raise RuntimeError('Original scene replay failed; retain its original output')
        finally:
            (output / 'diagnostic.json').write_text(json.dumps(record, indent=2)+'\n')
        return
    hook = prepare_observer(output / 'observer', args.dotnet)
    evidence = output / 'evidence'; evidence.mkdir()
    records = []
    record = dict(diagnosticOnly=True, qualified=False, producer=preparation['candidate']['identity'],
                  originalDeadlineSecondsPerProcess=60, originalMaximumActionsPerProcess=64,
                  applicationBytesUnchanged=True, controllerUnchanged=True)
    argv, path = sys.argv, sys.path[:]
    try:
        sys.path.insert(0, str(tools))
        sys.argv = [str(tools / 'run.py'), '--output', str(root)]
        with portable_hook(app, sha(app), hook, evidence, records):
            runpy.run_path(str(tools / 'run.py'), run_name='__main__')
        record['originalControllerCompleted'] = True
    except BaseException as error:
        record['originalControllerError'] = repr(error)
        raise
    finally:
        sys.argv, sys.path[:] = argv, path
        record['children'] = records
        if records:
            try: record['offscreenReplay'] = replay_original_scene(output, evidence, args.dotnet, app)
            except BaseException as error: record['offscreenReplayError'] = repr(error)
        (output / 'diagnostic.json').write_text(json.dumps(record, indent=2)+'\n')


if __name__ == '__main__': main()
