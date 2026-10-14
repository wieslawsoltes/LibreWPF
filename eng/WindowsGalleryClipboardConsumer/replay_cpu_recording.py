"""A named CPU recording around one unchanged, bounded offscreen replay."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

_state = None


def _invoke(arguments, name, timeout):
    with (_state['root'] / name).open('xb') as log:
        return subprocess.run([_state['wpr'], *arguments, '-instancename', _state['instance']],
                              stdout=log, stderr=subprocess.STDOUT, timeout=timeout).returncode


def _save():
    (_state['root'] / 'cpu-recording.json').write_text(
        json.dumps({k: v for k, v in _state.items() if k != 'root'}, indent=2) + '\n')


def record_start(root):
    global _state
    assert _state is None and os.name == 'nt'
    assert os.environ.get('GITHUB_ACTIONS') == 'true'
    assert os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
    assert os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF'
    assert root.name == 'scene-replay' and (root / 'preparation.json').is_file()
    _state = {'root': root, 'diagnosticOnly': True, 'qualified': False,
              'inputInjected': False, 'ordinaryGalleryLaunched': False,
              'instance': 'SceneReplay-' + os.environ['GITHUB_RUN_ID'] + '-' + os.environ['GITHUB_RUN_ATTEMPT'],
              'wpr': str(Path(os.environ['SystemRoot']) / 'System32/wpr.exe'),
              'recording': False, 'deadlineSeconds': 60, 'launched': False}
    _state['startExit'] = _invoke(['-start', 'CPU.Verbose'], 'wpr-start.log', 15)
    _save()
    assert _state['startExit'] == 0, 'Named CPU recording failed before replay launch'
    _state['recording'] = True
    _save()


def recorded_run(command, *, env, cwd, stdout, stderr, timeout):
    assert _state and _state['recording'] and not _state['launched']
    assert timeout == 60 and command[1] == 'exec'
    assert sum(Path(x).name == 'SceneReplay.dll' for x in command) == 1
    assert Path(cwd).resolve() == (_state['root'] / 'runtime').resolve()
    app = Path(command[0]).resolve(strict=True)
    _state.update(executable=str(app), executableSha256=hashlib.sha256(app.read_bytes()).hexdigest(),
                  launchUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    started = time.monotonic()
    process = subprocess.Popen(command, env=env, cwd=cwd, stdout=stdout, stderr=stderr)
    _state.update(launched=True, pid=process.pid)
    _save()
    try:
        code = process.wait(timeout=max(0, timeout - (time.monotonic() - started)))
        _state.update(exitCode=code, timedOut=False)
        return subprocess.CompletedProcess(command, code)
    except subprocess.TimeoutExpired:
        _state['timedOut'] = True
        process.kill()
        _state['retirementExit'] = process.wait(timeout=5)
        raise
    finally:
        _state['processSeconds'] = time.monotonic() - started
        _state['endUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        _save()


def record_stop():
    if not _state or not _state['recording']:
        return
    trace = _state['root'] / 'scene-cpu.etl'
    try:
        _state['stopExit'] = _invoke(['-stop', str(trace), '-skipPdbGen'], 'wpr-stop.log', 60)
        assert _state['stopExit'] == 0, 'CPU recording did not finalize'
        _state['recording'] = False
        assert 0 < trace.stat().st_size < 1024 ** 3
        with trace.open('rb') as stream:
            _state['traceSha256'] = hashlib.file_digest(stream, 'sha256').hexdigest()
        _state['traceBytes'] = trace.stat().st_size
    finally:
        if _state['recording']:
            _state['cancelExit'] = _invoke(['-cancel'], 'wpr-cancel.log', 15)
        _save()
