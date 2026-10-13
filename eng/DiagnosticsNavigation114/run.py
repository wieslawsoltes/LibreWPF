"""Original navigation sequence; user-approved native input, hosted Windows only."""
import argparse
import ctypes as C
import datetime
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import time
import traceback

ROOT = Path(__file__).resolve().parent


class StaleObservation(RuntimeError):
    """Wait for a fresh source observation; never inject input using this state."""


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def read(path):
    with path.open('rb') as stream:
        data = stream.read(1024 * 1024 + 1)
    require(len(data) <= 1024 * 1024, 'Observer receipt exceeds bound')
    return json.loads(data)


def hosted():
    require(os.name == 'nt' and os.environ.get('GITHUB_ACTIONS') == 'true'
            and os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
            and os.environ.get('RUNNER_OS') == 'Windows'
            and os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF',
            'Native input is restricted to the disposable repository Windows runner')


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--producer', type=Path, required=True)
    p.add_argument('--case', type=Path, required=True)
    p.add_argument('--native-input-authorized', action='store_true')
    p.add_argument('--caption', type=Path)
    p.add_argument('--remaining', type=float)
    a = p.parse_args()
    require(a.native_input_authorized, 'Explicit native input authorization required')
    hosted()
    if a.caption:
        # The existing hosted-only activation helper runs in its own DPI context.
        # clipboard_mode validates the CI environment; no clipboard API is called.
        require(a.remaining is not None and 3 < a.remaining <= 170, 'Original remaining input budget required')
        source = a.producer / 'eng/WindowsGalleryClipboardConsumer/run-windows.py'
        helper = module('qualified_caption', source)
        snapshot = read(a.caption)
        record = dict(clipboardAccessed=False, helperSha256=hashlib.sha256(source.read_bytes()).hexdigest())
        try:
            helper.Desktop().activate_ci_caption(snapshot, snapshot['pid'], time.monotonic() + a.remaining, record, os.environ)
        finally:
            a.caption.with_suffix('.result.json').write_text(json.dumps(record, indent=2))
        return

    preparation = read(a.case / 'inputs/preparation.json')
    require(preparation.get('compiled') is True and not preparation.get('error'), 'Complete original preparation required')
    app = Path(preparation['output'])
    require(hashlib.sha256((ROOT / 'original-desktop.py').read_bytes()).hexdigest() == '83e2a53e880b39c73f82d26c93d0dd79f769c61908054817cf838f1c1ae7e9bc', 'Original native helper changed')
    for entry in preparation['files']:
        path = app / entry['path']
        require(path.stat().st_size == entry['bytes'] and hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256'], 'Compiled input changed: ' + entry['path'])
    native = module('original_native', ROOT / 'original-desktop.py')
    desktop = native.WindowsDesktop()
    out = a.case / 'runtime'; out.mkdir(exist_ok=False)
    result = dict(qualified=False, clipboardAccessed=False, sequenceCompleted=False, deadlineSeconds=180,
                  inputDeadlineSeconds=170, producer=preparation['head'], actions=[])
    started = time.monotonic(); deadline = started + 180; input_deadline = started + 170
    process = None
    with (out / 'stdout.log').open('xb') as stdout, (out / 'stderr.log').open('xb') as stderr:
        try:
            exe = app / 'GalleryNavigationApp.exe'
            process = subprocess.Popen([str(exe), str(out)], cwd=app, stdout=stdout, stderr=stderr,
                                       creationflags=subprocess.CREATE_NO_WINDOW)
            result.update(pid=process.pid, path=str(exe), appSha256=hashlib.sha256(exe.read_bytes()).hexdigest(),
                          startedAt=datetime.datetime.now(datetime.timezone.utc).isoformat())
            (out / 'launch.json').write_text(json.dumps(result, indent=2))

            def state():
                require(process.poll() is None, 'Original process ended before sequence completion')
                value = read(out / 'state.json')
                require(value['pid'] == process.pid, 'Original observer PID mismatch')
                if time.time() - (out / 'state.json').stat().st_mtime >= 2:
                    raise StaleObservation('Observer is stale; no input')
                return value

            def wait_for(predicate, label):
                while time.monotonic() < input_deadline - 2:
                    try:
                        value = state()
                        matches = [w for w in value['windows'] if w['presented'] and predicate(w)]
                        if len(matches) == 1:
                            (out / (label + '-state.json')).write_text(json.dumps(value, indent=2))
                            return matches[0]
                        require(len(matches) <= 1, 'Ambiguous original source window')
                    except (FileNotFoundError, json.JSONDecodeError, StaleObservation):
                        pass
                    time.sleep(.1)
                raise TimeoutError('Original input budget expired waiting for ' + label)

            def select(window, predicate):
                choices = [c for c in window['controls'] if c['enabled'] and predicate(c)]
                require(len(choices) == 1, 'Expected exactly one observed original control')
                return choices[0]

            def activate(window, label):
                snapshot = out / (label + '-activation.json')
                snapshot.write_text(json.dumps(dict(pid=process.pid, hwnd=window['hwnd'], frame=window['frames'])))
                require(time.monotonic() < input_deadline - 5, 'Original input budget exhausted before activation')
                subprocess.run([sys.executable, str(Path(__file__).resolve()), '--native-input-authorized',
                                '--producer', str(a.producer), '--case', str(a.case), '--caption', str(snapshot),
                                '--remaining', str(input_deadline - time.monotonic())],
                               check=True, timeout=min(5, input_deadline - time.monotonic()),
                               creationflags=subprocess.CREATE_NO_WINDOW)

            def capture(label):
                result['actions'].append(dict(label=label, capture=desktop.screenshot(process.pid, desktop.windows(process.pid), out / (label + '.bmp'))))

            def click(window, selector, label):
                activate(window, label)
                def ready(w):
                    if w['hwnd'] != window['hwnd']:
                        return False
                    require(w['contentType'] == window['contentType'] and w['source'] == window['source'],
                            'Original navigation content changed before input')
                    choices = [c for c in w['controls'] if c['enabled'] and selector(c)]
                    require(len(choices) <= 1, 'Ambiguous original input target')
                    return len(choices) == 1
                # Original navigation metadata precedes the source command's
                # enabled-state update. Wait for the observed enabled target;
                # never send input to a disabled or missing control.
                observed = wait_for(ready, label + '-ready')
                control = select(observed, selector)
                rect = control['rect']
                require(len(rect) == 4 and all(math.isfinite(x) for x in rect) and rect[2] > 0 and rect[3] > 0,
                        'Original source target geometry unavailable')
                owners = [w for w in desktop.windows(process.pid) if w['hwnd'] == window['hwnd'] and w['enabled']]
                require(len(owners) == 1, 'Original native owner unavailable')
                owner = owners[0]
                measured = dict(id=owner['hwnd'], pid=process.pid, title=owner['title'], bounds=owner['bounds'], client=owner['client'])
                target = dict(x=int(rect[0] + rect[2]/2), y=int(rect[1] + rect[3]/2), width=1, height=1)
                record = dict(label=label, control=control, owner=owner,
                              beforeMove=native.exposed_owner_target(process.pid, measured, target, desktop.owner_obstruction_inventory(measured)))
                capture(label + '-before')
                desktop.pointer(process.pid, target)
                desktop.foreground(process.pid)
                require(not any(desktop.user.GetAsyncKeyState(k) & 0x8000 for k in (1, 2, 4, 5, 6, 16, 17, 18, 91, 92)), 'Held input prevents owned click')
                point = native.Point()
                require(desktop.user.GetCursorPos(C.byref(point)) and (point.x, point.y) == (target['x'], target['y']), 'Pointer changed before click')
                require(desktop.pid(desktop.user.WindowFromPoint(point)) == process.pid, 'Pointer ownership changed before click')
                record['beforeClick'] = native.exposed_owner_target(process.pid, measured, target, desktop.owner_obstruction_inventory(measured))
                require(process.poll() is None and time.monotonic() < input_deadline, 'Original input deadline or lifetime ended')
                down = native.Input(0, native.InputUnion(mouse=native.MouseInput(0, 0, 0, 2, 0, 0)))
                up = native.Input(0, native.InputUnion(mouse=native.MouseInput(0, 0, 0, 4, 0, 0)))
                require(desktop.user.SendInput(1, C.byref(down), C.sizeof(down)) == 1, 'Owned mouse down rejected')
                pressed = time.monotonic()
                try:
                    time.sleep(.050)
                finally:
                    require(desktop.user.SendInput(1, C.byref(up), C.sizeof(up)) == 1, 'Owned mouse up rejected')
                record.update(requestedHoldSeconds=.050, actualHoldSeconds=time.monotonic()-pressed,
                              elapsedSeconds=time.monotonic()-started)
                (out / (label + '-input.json')).write_text(json.dumps(record, indent=2))
                result['actions'].append(record)
                return observed

            root = wait_for(lambda w: w['title'] == 'Original Gallery navigation acceptance', 'root')
            click(root, lambda c: c['type'] == 'Button' and c['name'] == 'OpenNavigationWindow', 'open-navigation')
            page1 = wait_for(lambda w: w['type'] == 'NavigationWindow' and w['contentType'] == 'WPFGallery.Views.Page1', 'page1')
            # The immutable original Page1 has one enabled complex-content
            # TextBlock enclosing its sole Hyperlink. Its public Text value is
            # empty in the observed source snapshot; retain the actual measured
            # element rectangle rather than inventing a plaintext text value.
            # The other enabled TextBlock is the explicit "This is Page 1" label.
            require(sum(c['type'] == 'TextBlock' and c['enabled'] for c in page1['controls']) == 2,
                    'Original Page1 inline-container inventory differs')
            click(page1, lambda c: c['type'] == 'TextBlock' and c['text'] == '', 'page1-link')
            page2 = wait_for(lambda w: w['type'] == 'NavigationWindow' and w['contentType'] == 'WPFGallery.Views.Page2' and w['canGoBack'] is True, 'page2')
            select(page2, lambda c: c['type'] == 'TextBlock' and c['text'] == 'This is Page 2')
            click(page2, lambda c: c['type'] == 'TextBlock' and c['text'] == '\ue72b', 'journal-back')
            back = wait_for(lambda w: w['type'] == 'NavigationWindow' and w['contentType'] == 'WPFGallery.Views.Page1' and w['canGoForward'] is True, 'back-page1')
            forward_origin = click(back, lambda c: c['type'] == 'TextBlock' and c['text'] == '\ue72a', 'journal-forward')
            final = wait_for(lambda w: w['type'] == 'NavigationWindow' and w['contentType'] == 'WPFGallery.Views.Page2' and w['canGoBack'] is True, 'forward-page2')
            select(final, lambda c: c['type'] == 'TextBlock' and c['text'] == 'This is Page 2')
            # Content/source can publish before the original host presents the
            # navigation frame. Retain both observations and wait for the real
            # presentation counter to advance after the forward transition.
            # Do not request another render, inject input, or extend the deadline.
            final_presented = wait_for(lambda w: w['hwnd'] == final['hwnd']
                                       and w['contentType'] == final['contentType']
                                       and w['source'] == final['source']
                                       and w['canGoBack'] is True
                                       and w['frames'] > forward_origin['frames']
                                       and any(c['type'] == 'TextBlock' and c['text'] == '\ue72b' and c['enabled'] for c in w['controls'])
                                       and any(c['type'] == 'TextBlock' and c['text'] == '\ue72a' and not c['enabled'] for c in w['controls']), 'forward-page2-presented')
            select(final_presented, lambda c: c['type'] == 'TextBlock' and c['text'] == 'This is Page 2')
            capture('forward-page2')
            result['sequenceCompleted'] = True
        except BaseException as error:
            result['error'] = str(error)
            result['traceback'] = traceback.format_exc()
        finally:
            if process is not None:
                if process.poll() is None:
                    (out / 'finish').write_text('Original observer normal cleanup\n')
                try:
                    result['exitCode'] = process.wait(timeout=max(.001, deadline - time.monotonic()))
                    result['deadlineExceeded'] = False
                except subprocess.TimeoutExpired:
                    result['deadlineExceeded'] = True
                    process.kill(); result['exitCode'] = process.wait(timeout=3)
                result['ownedProcessEnded'] = process.poll() is not None
            result['elapsedSeconds'] = time.monotonic()-started
            (out / 'result.json').write_text(json.dumps(result, indent=2))
    print(json.dumps({k: v for k, v in result.items() if k != 'actions'}))
    require(result['sequenceCompleted'] and result.get('exitCode') == 0 and not result.get('deadlineExceeded') and not result.get('error'),
            'Original navigation sequence did not complete; preserve all evidence')


if __name__ == '__main__':
    main()
