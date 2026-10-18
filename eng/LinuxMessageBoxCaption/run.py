"""Read actual owned X11 captions; no activation, pointer or keyboard calls."""
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time
import uuid


def value(text, name, count=None):
    match = re.search(r'^' + re.escape(name) + r'\([^\n]*?\) = ([^\n]+)$', text, re.M)
    if not match:
        raise ValueError('Missing original native property: ' + name)
    result = [int(part.strip(), 0) for part in match[1].split(',')]
    if count is not None and len(result) != count:
        raise ValueError('Unexpected native property extent: ' + name)
    return result


def validate_chrome(properties, pid):
    if value(properties, '_NET_WM_PID', 1) != [pid]:
        raise ValueError('Foreign client PID')
    hints = value(properties, '_MOTIF_WM_HINTS', 5)
    flags, functions, decorations = hints[:3]
    if flags & 3 != 3 or functions & (1 | 8 | 16) or not functions & 32:
        raise ValueError('Original Motif functions do not explicitly exclude minimize/maximize')
    if decorations & (1 | 32 | 64) or not decorations & 8:
        raise ValueError('Original Motif decorations do not explicitly retain title without minimize/maximize')
    return hints


def run_case(environment, case):
    if os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted' or os.environ.get('GITHUB_REPOSITORY') != 'wieslawsoltes/LibreWPF':
        raise RuntimeError('Disposable owned repository runner required')
    root = Path.cwd()
    preparation = json.loads((root / 'artifacts/ubuntu26-caption-preparation.json').read_text())
    app = Path(os.environ['CAPTION_APP'])
    if not preparation['compiled'] or str(app) != preparation['application']:
        raise ValueError('Qualified consumer preparation missing')
    def inventory():
        return {str(p.relative_to(app.parent)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(app.parent.rglob('*')) if p.is_file()}
    if inventory() != preparation['outputs']:
        raise ValueError('Prepared application bytes changed')
    env = environment.copy()
    for key in list(env):
        if key.startswith(('PROGPU', 'ProGpu')) or key in ('EGL_PLATFORM', 'LIBGL_ALWAYS_SOFTWARE', 'GALLIUM_DRIVER', 'WGPU_BACKEND'):
            env.pop(key)
    env.update(PROGPU_WGPU_BACKEND='gl', PROGPU_BACKEND_DIAGNOSTICS='1')
    output = root / 'artifacts/caption-observation'
    if output.exists():
        raise ValueError('Fresh caption evidence required')
    run = uuid.uuid4().hex
    record = dict(qualified=False, inputSent=False, activatedWindows=False, case=case,
                  processDeadlineSeconds=60, sourceDeadlineSeconds=55, observations=[], commands=[])
    started = time.monotonic()
    child = None
    def remaining():
        left = 60 - (time.monotonic() - started)
        if left <= 0: raise TimeoutError('60-second process bound exhausted')
        return left
    def command(args, limit=5):
        result = subprocess.run(args, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                timeout=min(limit, remaining()))
        record['commands'].append(dict(arguments=args, exitCode=result.returncode, output=result.stdout))
        if result.returncode: raise RuntimeError('Read-only native command failed: ' + args[0])
        return result.stdout
    try:
        with (root / 'artifacts/ubuntu26-caption.application.log').open('x') as log:
            child = subprocess.Popen(['dotnet', str(app), str(output), run], cwd=app.parent, env=env,
                                     stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            record['pid'] = child.pid
            for index in range(4):
                ready_path = output / f'ready-{index}.json'
                while not ready_path.is_file():
                    if child.poll() is not None: raise RuntimeError('Application exited before source caption readiness')
                    time.sleep(min(.05, remaining()))
                ready = json.loads(ready_path.read_text())
                if ready['pid'] != child.pid or ready['run'] != run or ready['index'] != index or ready['presentedFrames'] <= 0:
                    raise ValueError('Source/native identity changed')
                handle = ready['nativeHandle']
                properties = command(['xprop', '-id', str(handle), '_NET_WM_PID', '_MOTIF_WM_HINTS',
                                      '_NET_FRAME_EXTENTS', '_NET_WM_ALLOWED_ACTIONS', 'WM_TRANSIENT_FOR', '_NET_WM_NAME'])
                try:
                    hints = validate_chrome(properties, child.pid)
                except ValueError as admission_error:
                    # Keep the original rejection. An owned-client image explains
                    # source-drawn chrome; it never substitutes for a native frame.
                    record['captionAdmissionFailure'] = str(admission_error)
                    try:
                        if value(properties, '_NET_WM_PID', 1) != [child.pid]:
                            raise ValueError('Foreign rejected client PID')
                        target = output / f'{index}-rejected-owned-client.png'
                        command(['import', '-window', str(handle), str(target)], limit=10)
                        data = target.read_bytes()
                        if not data.startswith(b'\x89PNG\r\n\x1a\n') or len(data) > 8 * 1024 * 1024:
                            raise ValueError('Invalid rejected client capture')
                        after = command(['xprop', '-id', str(handle), '_NET_WM_PID'])
                        if value(after, '_NET_WM_PID', 1) != [child.pid] or child.poll() is not None:
                            raise ValueError('Rejected client ownership changed during capture')
                        record['rejectedOwnedClient'] = dict(source=ready, properties=properties,
                            kind='client-only-diagnostic', qualified=False, file=target.name,
                            bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
                    except Exception as capture_error:
                        record['rejectedClientCaptureError'] = type(capture_error).__name__ + ': ' + str(capture_error)
                    raise
                transient = re.search(r'WM_TRANSIENT_FOR\(WINDOW\): window id # (0x[0-9a-fA-F]+)', properties)
                if not transient or int(transient[1], 16) != ready['ownerHandle']:
                    raise ValueError('Actual transient owner differs from original source owner')
                tree = command(['xwininfo', '-id', str(handle), '-tree'])
                parent = re.search(r'Parent window id: (0x[0-9a-fA-F]+)', tree)
                root_window = re.search(r'Root window id: (0x[0-9a-fA-F]+)', tree)
                if not parent or not root_window or parent[1] == root_window[1]:
                    raise ValueError('Actual separate native decoration frame is unavailable')
                frame = int(parent[1], 16)
                frame_tree = command(['xwininfo', '-id', str(frame), '-tree'])
                if hex(handle).lower() not in frame_tree.lower():
                    raise ValueError('Frame does not contain original client')
                frames = []
                for kind, window in [('client', handle), ('frame', frame)]:
                    target = output / f'{index}-{kind}.png'
                    command(['import', '-window', str(window), str(target)], limit=10)
                    data = target.read_bytes()
                    if not data.startswith(b'\x89PNG\r\n\x1a\n') or len(data) > 8 * 1024 * 1024:
                        raise ValueError('Invalid original window capture')
                    frames.append(dict(kind=kind, window=window, file=target.name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
                if child.poll() is not None:
                    raise RuntimeError('Application retired during capture')
                after = command(['xprop', '-id', str(handle), '_NET_WM_PID', '_MOTIF_WM_HINTS'])
                if validate_chrome(after, child.pid) != hints:
                    raise ValueError('Native caption contract changed during capture')
                if index == 0:
                    owner = ready['ownerHandle']
                    if ready['ownerPresentedFrames'] <= 0: raise ValueError('Owner has no presented frame')
                    owner_properties = command(['xprop', '-id', str(owner), '_NET_WM_PID', '_MOTIF_WM_HINTS'])
                    if value(owner_properties, '_NET_WM_PID', 1) != [child.pid]:
                        raise ValueError('Foreign positive-control owner')
                    owner_hints = value(owner_properties, '_MOTIF_WM_HINTS', 5)
                    if owner_hints[0] & 3 != 3 or owner_hints[1] & 24 != 24 or owner_hints[2] & 96 != 96:
                        raise ValueError('Source owner does not provide enabled minimize/maximize positive control')
                    owner_tree = command(['xwininfo', '-id', str(owner), '-tree'])
                    owner_parent = re.search(r'Parent window id: (0x[0-9a-fA-F]+)', owner_tree)
                    if not owner_parent or owner_parent[1] == root_window[1]:
                        raise ValueError('Actual positive-control caption frame is missing')
                    owner_target = output / 'owner-frame-positive-control.png'
                    command(['import', '-window', str(int(owner_parent[1], 16)), str(owner_target)], limit=10)
                    record['ownerPositiveControl'] = dict(properties=owner_properties, tree=owner_tree,
                        image=owner_target.name, sha256=hashlib.sha256(owner_target.read_bytes()).hexdigest())
                record['observations'].append(dict(source=ready, properties=properties, tree=tree,
                                                   frameTree=frame_tree, captures=frames))
                pending = output / f'capture-{index}.pending'
                pending.write_text(run)
                pending.rename(output / f'capture-{index}.ack')
            child.wait(timeout=remaining())
            record['exitCode'] = child.returncode
            completion = json.loads((output / 'complete.json').read_text())
            record['sourceCompletion'] = completion
            record['outputsUnchanged'] = inventory() == preparation['outputs']
            if child.returncode != 0 or not completion['completed'] or len(completion['cases']) != 4 or not record['outputsUnchanged']:
                raise RuntimeError('Source completion or exact package output identity failed')
            record['passed'] = True
    except BaseException as error:
        record.update(passed=False, error=type(error).__name__ + ': ' + str(error))
        raise
    finally:
        if child is not None and child.poll() is None:
            os.killpg(child.pid, signal.SIGKILL); child.wait(timeout=5)
            record['forcedCleanup'] = True
        record['elapsedSeconds'] = time.monotonic() - started
        (root / 'artifacts/ubuntu26-caption-result.json').write_text(json.dumps(record, indent=2))
    return record
