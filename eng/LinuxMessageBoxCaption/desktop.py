#!/usr/bin/env python3
"""Pending approved caption input in one disposable owned GNOME session."""
import hashlib, json, os, pathlib, re, signal, subprocess, tempfile, time
output = pathlib.Path('artifacts/ubuntu26-toolkit-desktop-session.json')
assert not output.exists()
record = dict(qualified=False, applicationExecuted=False, inputSent=False, observations=[], commands=[])
child = None
temporary = tempfile.TemporaryDirectory(prefix="progpu-owned-desktop-")
root = pathlib.Path(temporary.name)
started = time.monotonic()
def command(args, timeout=5):
    result = subprocess.run(args, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    item = dict(arguments=args, exitCode=result.returncode, output=result.stdout)
    record['commands'].append(item)
    return result
try:
    proposal = pathlib.Path(__file__).with_name('decision.txt')
    assert os.environ.get('LIBREWPF_CAPTION_INPUT_APPROVAL_SHA256') == hashlib.sha256(proposal.read_bytes()).hexdigest(), 'Exact final proposal approval required'
    assert os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF'
    assert os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
    environment = {key: value for key, value in os.environ.items()
                   if not re.search(r"TOKEN|SECRET|PASSWORD|LICENSE|CREDENTIAL|API_KEY|ACCESS_KEY", key, re.I)}
    for key in ('DISPLAY', 'WAYLAND_DISPLAY', 'XAUTHORITY'):
        environment.pop(key, None)
    for key, name in [('XDG_RUNTIME_DIR', 'runtime'), ('XDG_CONFIG_HOME', 'config'),
                      ('XDG_CACHE_HOME', 'cache'), ('XDG_DATA_HOME', 'data')]:
        directory = root / name
        directory.mkdir(mode=0o700)
        environment[key] = str(directory)
    environment['XDG_SESSION_TYPE'] = 'wayland'
    # A private keyfile backend confines this test preference to the fresh session.
    # Showing all enabled buttons makes a NoResize regression visible.
    environment['GSETTINGS_BACKEND'] = 'keyfile'
    record['captionPreference'] = []
    for action in (['gsettings', 'set', 'org.gnome.desktop.wm.preferences', 'button-layout', ':minimize,maximize,close'],
                   ['gsettings', 'get', 'org.gnome.desktop.wm.preferences', 'button-layout']):
        setting = subprocess.run(action, env=environment, text=True, stdout=subprocess.PIPE,
                                 stderr=subprocess.STDOUT, timeout=5)
        record['captionPreference'].append(dict(arguments=action, exitCode=setting.returncode, output=setting.stdout))
        if setting.returncode: raise RuntimeError('Owned caption preference setup failed')
    if setting.stdout.strip() != "':minimize,maximize,close'":
        raise RuntimeError('Owned caption preference was not retained')

    before = sorted(str(p) for p in pathlib.Path('/tmp/.X11-unix').glob('*'))
    args = ['gnome-shell', '--headless', '--virtual-monitor=1280x1024', '--wayland-display=progpu-owned-wayland']
    record.update(arguments=args, x11SocketsBefore=before)
    with (root/'compositor.log').open('w+') as log:
        child = subprocess.Popen(args, env=environment, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        record['compositorPid'] = child.pid
        until = time.monotonic()+30
        while time.monotonic() < until:
            if child.poll() is not None:
                raise RuntimeError('Owned compositor exited: '+str(child.returncode))
            observed = command(['gdbus', 'call', '--session', '--dest', 'org.gnome.Mutter.DisplayConfig',
                                '--object-path', '/org/gnome/Mutter/DisplayConfig', '--method',
                                'org.gnome.Mutter.DisplayConfig.GetCurrentState'], timeout=min(5, max(.001, until-time.monotonic())))
            if observed.returncode == 0:
                record['displayConfig'] = observed.stdout
                break
            time.sleep(.25)
        else:
            raise TimeoutError('Owned compositor readiness exceeded30seconds')
        identity = command(['gdbus', 'call', '--session', '--dest', 'org.freedesktop.DBus',
                            '--object-path', '/org/freedesktop/DBus', '--method',
                            'org.freedesktop.DBus.GetConnectionUnixProcessID', 'org.gnome.Mutter.DisplayConfig'])
        assert identity.returncode == 0 and re.fullmatch(r'\(uint32 '+str(child.pid)+r',\)\s*', identity.stdout)
        record['waylandSocketExists'] = (root/'runtime/progpu-owned-wayland').exists()
        record['x11SocketsAfter'] = sorted(str(p) for p in pathlib.Path('/tmp/.X11-unix').glob('*'))
        # Observe only descendants of this owned compositor; no unrelated process environment.
        descendants = {child.pid}
        details = {}
        for _ in range(4):
            for entry in pathlib.Path('/proc').iterdir():
                if not entry.name.isdigit() or int(entry.name) in descendants:
                    continue
                try:
                    status = (entry/'status').read_text()
                    parent = int(next(line.split(':',1)[1] for line in status.splitlines() if line.startswith('PPid:')))
                    if parent in descendants:
                        pid=int(entry.name);descendants.add(pid)
                        details[pid] = (entry/'cmdline').read_bytes().decode(errors='replace').split('\0')
                except (FileNotFoundError,ProcessLookupError,PermissionError):
                    pass
        record['ownedDescendants'] = details
        xwaylands=[(pid,args) for pid,args in details.items() if args and pathlib.Path(args[0]).name=='Xwayland']
        assert len(xwaylands)==1,'Require exactly one owned Xwayland'
        xpid,xargs=xwaylands[0]
        assert pathlib.Path('/proc',str(xpid)).exists() and re.fullmatch(r':\d+',xargs[1])
        auth=pathlib.Path(xargs[xargs.index('-auth')+1]).resolve()
        assert auth.is_relative_to((root/'runtime').resolve()) and auth.is_file()
        environment.update(DISPLAY=xargs[1],WAYLAND_DISPLAY='progpu-owned-wayland',XAUTHORITY=str(auth))
        from owned_overview import close_owned_overview
        log.flush()
        record['overview'] = close_owned_overview(command, child, approved=True, read_startup=lambda: (root/'compositor.log').read_bytes())
        import importlib.util
        spec=importlib.util.spec_from_file_location('original_multi_window',pathlib.Path(__file__).with_name('executor.py'))
        runner=importlib.util.module_from_spec(spec);spec.loader.exec_module(runner)
        record['applicationExecuted']=True
        result=runner.run_case(environment,'mixed-wayland-x11')
        record['applicationPassed']=result['passed']
        record['completed'] = True
        child.terminate()
        try: child.wait(timeout=3)
        except subprocess.TimeoutExpired:
            record['compositorForcedCleanup'] = True
            os.killpg(child.pid, signal.SIGKILL)
            child.wait(timeout=3)
        record['compositorExitCode'] = child.returncode
        log.flush(); log.seek(0); record['compositorOutput'] = log.read(262144)
except BaseException as error:
    record.update(completed=False,error=type(error).__name__+': '+str(error))
    raise
finally:
    native_result = pathlib.Path('artifacts/caption-input/result.json')
    if native_result.is_file():
        actual = json.loads(native_result.read_text())
        record['inputSent'] = actual.get('nativeEventAttempts', 0) > 0
        record['nativeResult'] = {'path': str(native_result), 'sha256': hashlib.sha256(native_result.read_bytes()).hexdigest(), 'completed': actual.get('completed', False)}
    if child is not None and child.poll() is None:
        os.killpg(child.pid, signal.SIGKILL)
        child.wait(timeout=3)
        record['compositorForcedCleanup'] = True
    if (root/'compositor.log').exists():
        record['compositorOutput'] = (root/'compositor.log').read_text(errors='replace')[:262144]
    record['elapsedSeconds'] = time.monotonic()-started
    output.write_text(json.dumps(record,indent=2)+'\n')
    temporary.cleanup()

if not record.get('applicationPassed'):raise SystemExit('Linux caption observation failed')
