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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--producer-tools', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    args = parser.parse_args()
    if os.name != 'nt' or any(os.environ.get(k) != v for k, v in {
        'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
        'GITHUB_REPOSITORY': 'wieslawsoltes/LibreWPF'}.items()):
        raise RuntimeError('Exact disposable hosted Windows runner required')
    root, tools = args.prepared.resolve(strict=True), args.producer_tools.resolve(strict=True)
    preparation = json.loads((root / 'preparation.json').read_text())
    if (preparation['candidate']['identity']['head'] != '356d1a22c0217c10ee3cfdee88db52c46ded58e2' or
            preparation['candidate']['sdkSelection']['compiler'] != 'Dxc' or
            os.environ.get('PROGPU_WPF_TRACE_INPUT') != '0'):
        raise RuntimeError('Exact retained qualified356 DXC failure required')
    output = args.output.resolve(); output.mkdir(exist_ok=False)
    hook = prepare_observer(output / 'observer', args.dotnet)
    evidence = output / 'evidence'; evidence.mkdir()
    app = Path(preparation['payloads']['portable']['app']).resolve(strict=True)
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
        (output / 'diagnostic.json').write_text(json.dumps(record, indent=2)+'\n')


if __name__ == '__main__': main()
