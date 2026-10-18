"""Inspect the exact built Slider candidate without sending native input."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import re
import signal
import subprocess
import tempfile
import time
import zipfile

ARCHIVE_SHA = '72af407f78992bac58fae546849188c1e3cce46e797bbb014de9a72245ade98c'
INPUT_SHA = 'd18a86dc068fe3253db1ad54a11f14307bcc72cad62faacfe38379dcf107818c'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def validate_archive(path):
    assert path.stat().st_size == 71223843 and digest(path.read_bytes()) == ARCHIVE_SHA
    with zipfile.ZipFile(path) as archive:
        receipt = json.loads(archive.read('preparation.json'))
        assert receipt['compiled'] and not any(receipt[key] for key in
            ('qualified', 'applicationExecuted', 'inputSent', 'candidateInputAuthorized'))
        assert receipt['producer']['head'] == 'a255b4cccd5ca1872472cdbf635c4340d31def24'
        prefix = 'bin/Release/net10.0-windows/'
        files = {name[len(prefix):]: archive.read(name) for name in archive.namelist()
                 if name.startswith(prefix) and not name.endswith('/')}
        assert len(files) == 281 and {name: digest(data) for name, data in files.items()} == receipt['outputs']
        for name in files:
            part = PurePosixPath(name)
            assert not part.is_absolute() and '..' not in part.parts and '\\' not in name and ':' not in name
        return receipt, files


def inspection_source(archive):
    assert digest(archive.read_bytes()) == INPUT_SHA
    with zipfile.ZipFile(archive) as source:
        original = source.read('input-delivery-20261013/slider-native-input.py').decode()
    # The only inspector change selects the actual reviewed candidate inventory.
    # It is invoked without --execute-approved-input; every original input guard remains.
    before = 'assert len(manifest) == 267 and runner[\'deadlineSeconds\'] == 180'
    assert original.count(before) == 1
    return original, original.replace(before, before.replace('267', '281'))


def run_case(environment, case):
    assert os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
    assert os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF'
    root = Path.cwd()
    receipt, files = validate_archive(root/'eng/diagnostic-slider-candidate.zip')
    original, inspector = inspection_source(root/'eng/diagnostic-slider-inputs/original-slider-inputs.zip')
    output = root/'artifacts/slider-readiness'
    output.mkdir()
    evidence = output/'run2-native-input'
    evidence.mkdir()
    (output/'input-delivery-20261013').mkdir()
    inspect_path = output/'input-delivery-20261013/slider-native-input.py'
    inspect_path.write_text(inspector)
    manifest = [{'path': name, 'sha256': digest(data)} for name, data in sorted(files.items())]
    (output/'payload-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    desktop = {key: environment[key] for key in
        ('DISPLAY', 'WAYLAND_DISPLAY', 'XDG_RUNTIME_DIR', 'XAUTHORITY', 'XDG_SESSION_TYPE')}
    (output/'guest-session.json').write_text(json.dumps(desktop, indent=2)+'\n')
    record = dict(qualified=False, case=case, inputSent=False, inputSynthesizedByRunner=False,
        deadlineSeconds=180, applicationReserveSeconds=170, inputReserveEndsAtSeconds=160,
        archiveSha256=ARCHIVE_SHA, producer=receipt['producer'], manifestFiles=281,
        originalInspectorSha256=digest(original.encode()), inspectorSha256=digest(inspector.encode()),
        inspectorChanges=['Actual reviewed inventory count:267 to281'], desktop=desktop)
    runner = evidence/'runner.json'
    child = None
    started = time.monotonic()

    def save():
        runner.write_text(json.dumps(record, indent=2)+'\n')

    def remaining(limit=180):
        value = limit-(time.monotonic()-started)
        if value <= 0:
            raise TimeoutError('Original controller deadline exhausted')
        return value

    with tempfile.TemporaryDirectory(prefix='progpu-slider-52-', dir='/tmp') as temporary:
        stage = Path(temporary)
        for name, data in files.items():
            path = stage/name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        (stage/'GallerySliderProbe').chmod(0o700)

        def unchanged():
            return {str(p.relative_to(stage)): digest(p.read_bytes()) for p in stage.rglob('*')
                    if p.is_file()} == receipt['outputs']

        env = environment.copy()
        for key in list(env):
            if key.startswith(('PROGPU', 'ProGpu', 'DOTNET_', 'NUGET_', 'LD_LIBRARY_PATH')):
                env.pop(key)
        try:
            started = time.monotonic()
            with (evidence/'stdout.log').open('x') as stdout, (evidence/'stderr.log').open('x') as stderr:
                child = subprocess.Popen([str(stage/'GallerySliderProbe'), str(evidence)], cwd=stage,
                    env=env, stdout=stdout, stderr=stderr, start_new_session=True)
                record.update(stage=str(stage), pid=child.pid, startedUtc=time.time(), startedMonotonic=started)
                save()
                while not (evidence/'ready.json').is_file():
                    assert child.poll() is None, 'Original application exited before readiness'
                    time.sleep(min(.1, remaining(160)))
                ready = json.loads((evidence/'ready.json').read_text())
                assert ready['pid'] == child.pid and ready['nativeKind'] == 'X11'
                assert ready['frame'] > 0 and len(ready['sliders']) == 4
                assert unchanged()
                with (evidence/'inspection.log').open('x') as log:
                    inspected = subprocess.run(['python3', str(inspect_path)], cwd=output, env=env,
                        stdout=log, stderr=subprocess.STDOUT, timeout=min(20, remaining(160)))
                record['inspectionExitCode'] = inspected.returncode
                # Retain independent read-only focus/overview observations even
                # when the original inspector rejects. They cannot turn that
                # rejection into successful input admission.
                observations = []
                for arguments in (
                    ['xprop', '-root', '_NET_ACTIVE_WINDOW', '_NET_CLIENT_LIST_STACKING'],
                    ['xprop', '-id', str(ready['nativeHandle']), '_NET_WM_PID', 'WM_CLASS', 'WM_HINTS', '_NET_WM_STATE'],
                    ['gdbus', 'call', '--session', '--dest', 'org.gnome.Shell',
                     '--object-path', '/org/gnome/Shell', '--method',
                     'org.freedesktop.DBus.Properties.Get', 'org.gnome.Shell', 'OverviewActive']):
                    observed = subprocess.run(arguments, env=env, text=True, stdout=subprocess.PIPE,
                        stderr=subprocess.STDOUT, timeout=min(5, remaining(160)))
                    observations.append(dict(command=arguments, exitCode=observed.returncode, output=observed.stdout))
                (evidence/'native-window-observation.json').write_text(json.dumps(observations, indent=2)+'\n')
                assert Path(f'/proc/{child.pid}/exe').resolve() == stage/'GallerySliderProbe'
                assert observations[1]['exitCode'] == 0 and re.search(
                    r'^_NET_WM_PID\(CARDINAL\) = '+str(child.pid)+r'\s*$', observations[1]['output'], re.M)
                with (evidence/'capture.log').open('x') as log:
                    subprocess.run(['import', '-window', str(ready['nativeHandle']), str(evidence/'before-input.png')],
                        env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=min(10, remaining(160)))
                capture = evidence/'before-input.png'
                assert capture.read_bytes().startswith(b'\x89PNG\r\n\x1a\n')
                record.update(captureSha256=digest(capture.read_bytes()),
                    captureMethod='Read-only ImageMagick X11 owned-client capture; no host/VM capture substitution claim')
                assert inspected.returncode == 0, 'Original read-only native ownership/geometry guards failed'
                inspection = json.loads((evidence/'native-inspection.json').read_text())
                assert inspection['completed'] and not inspection['inputRequested'] and not inspection['journal']
                assert inspection['payloadUnchanged']
                (evidence/'stop.request').write_text('No-input readiness complete.\n')
                child.wait(timeout=remaining())
                record.update(exitCode=child.returncode, payloadUnchanged=unchanged())
                events = [json.loads(line) for line in (evidence/'events.jsonl').read_text().splitlines()]
                assert events[-1]['kind'] == 'exit' and events[-1]['data']['result'] == 0
                assert events[-1]['data']['ready'] and not events[-1]['data']['timedOut']
                assert not any(e['kind'] in ('drag-started', 'drag-delta', 'drag-completed', 'value-changed') for e in events)
                record['passed'] = child.returncode == 0 and record['payloadUnchanged']
        except BaseException as error:
            record.update(passed=False, error=type(error).__name__+': '+str(error))
            raise
        finally:
            if child is not None and child.poll() is None:
                (evidence/'stop.request').write_text('No-input readiness cleanup.\n')
                try:
                    child.wait(timeout=max(.001, 180-(time.monotonic()-started)))
                except subprocess.TimeoutExpired:
                    record['forcedCleanup'] = True
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait(timeout=5)
            record.update(seconds=time.monotonic()-started,
                exitCode=child.returncode if child is not None else None, payloadUnchanged=unchanged())
            save()
    record['temporaryPayloadRemoved'] = not stage.exists()
    save()
    return record
