"""Unchanged Toolkit source-driven validation; original 180-second process limit."""
import hashlib,json,os,pathlib,resource,signal,subprocess,time

def validate_log(text):
    import re
    success = re.findall(r'^ProGPU WPF Toolkit live input validation succeeded: (.*)$', text, re.M)
    details = re.findall(r'^ProGPU WPF Toolkit live input validation details: (.*)$', text, re.M)
    adapters = re.findall(r"\[WGPUCONTEXT\] Adapter '[^\n]*?', backend=(\w+), requestedBackend=(\w+),", text)
    geometry = re.search(r'logical ([0-9]+)x([0-9]+), pixels ([0-9]+)x([0-9]+), viewport ([0-9]+)x([0-9]+)@([0-9]+),([0-9]+), dpi ([0-9]+(?:\.[0-9]+)?)', success[0]) if len(success)==1 else None
    geometry_ok = False
    if geometry:
        lw,lh,pw,ph,vw,vh,vx,vy = map(int,geometry.groups()[:8])
        geometry_ok = lw>0 and lh>0 and pw>=lw and ph>=lh and (vw,vh,vx,vy)==(pw,ph,0,0)
    details_ok = len(details)==1 and all(s in details[0] for s in ('floating document window', 'anchorable hide/show', 'auto-hide side groups', 'layout serialization updated'))
    backend_ok = bool(adapters) and all(actual in ('OpenGL','OpenGles') and requested=='OpenGL' for actual,requested in adapters)
    return dict(passed=geometry_ok and details_ok and backend_ok,geometryPassed=geometry_ok,detailsPassed=details_ok,backendPassed=backend_ok,adapters=adapters)

def run_case(environment,case):
    root=pathlib.Path.cwd(); preparation=json.loads((root/'artifacts/ubuntu26-toolkit-preparation.json').read_text())
    assert preparation['compiled'] and not preparation['applicationExecuted']
    app=pathlib.Path(os.environ['ORIGINAL_TOOLKIT_APP']);assert str(app)==preparation['application']
    def inventory():return {str(p.relative_to(app.parent)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(app.parent.rglob('*')) if p.is_file()}
    assert inventory()==preparation['outputs'],'Output inventory changed before execution'
    env=environment.copy()
    for key in list(env):
        if key.startswith(('PROGPU','ProGpu')) or key in ('EGL_PLATFORM','LIBGL_ALWAYS_SOFTWARE','GALLIUM_DRIVER','__EGL_VENDOR_LIBRARY_FILENAMES','WGPU_BACKEND'):env.pop(key)
    # Core dump storage is disabled; original stderr, exit status and all output hashes remain.
    resource.setrlimit(resource.RLIMIT_CORE,(0,0))
    env.update(PROGPU_WGPU_BACKEND='gl', PROGPU_BACKEND_DIAGNOSTICS='1', PROGPU_WPF_TOOLKIT_LIVE_VALIDATE='1', PROGPU_WPF_TOOLKIT_LIVE_VALIDATE_STATUS_PATH=str(root/('artifacts/ubuntu26-toolkit-'+case+'.status.txt')))
    command=['dotnet',str(app)];started=time.monotonic()
    record=dict(case=case,qualified=False,inputSent=False,command=command,outputsBefore=preparation['outputs'],environment={k:env.get(k) for k in ('DISPLAY','WAYLAND_DISPLAY','XDG_RUNTIME_DIR','XAUTHORITY','XDG_SESSION_TYPE')},outerTimeoutSeconds=180,sourceDrivenActions=True,nativeInputSent=False)
    logpath=root/('artifacts/ubuntu26-toolkit-'+case+'.application.log')
    child=None
    try:
        with logpath.open('w') as log:
            child=subprocess.Popen(command,cwd=app.parent,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            record['pid']=child.pid
            try:child.wait(timeout=max(.001,180-(time.monotonic()-started)))
            except subprocess.TimeoutExpired:
                record['outerTimeout']=True;os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=5)
        text=logpath.read_text(errors='replace')
        record.update(exitCode=child.returncode,reportedSuccess='ProGPU WPF Toolkit live input validation succeeded:' in text,applicationLogSha256=hashlib.sha256(logpath.read_bytes()).hexdigest())
        record['outputsUnchanged']=inventory()==preparation['outputs']
        record['originalAssertions']=validate_log(text)
        record['passed']=child.returncode==0 and record['originalAssertions']['passed'] and record['outputsUnchanged'] and not record.get('outerTimeout')
    finally:
        if child is not None and child.poll() is None:os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=5)
        record['elapsedSeconds']=time.monotonic()-started
        (root/('artifacts/ubuntu26-toolkit-'+case+'.json')).write_text(json.dumps(record,indent=2)+'\n')
    return record

if __name__=='__main__':
    import select,tempfile
    assert os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted'
    env=os.environ.copy()
    for key in ('DISPLAY','WAYLAND_DISPLAY','XAUTHORITY'):env.pop(key,None)
    with tempfile.TemporaryDirectory(prefix='original-headless-') as temporary:
        os.chmod(temporary,0o700);env.update(XDG_RUNTIME_DIR=temporary,XDG_SESSION_TYPE='x11')
        readfd,writefd=os.pipe();server=None
        try:
            with open('artifacts/ubuntu26-headless-server.log','w') as log:
                server=subprocess.Popen(['Xvfb','-displayfd',str(writefd),'-screen','0','1920x1080x24','+extension','RANDR','+extension','GLX','+extension','XTEST'],pass_fds=(writefd,),stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
                os.close(writefd);writefd=None
                assert select.select([readfd],[],[],10)[0],'Xvfb readiness timeout'
                value=os.read(readfd,32).decode().strip();assert value.isdigit()
                env['DISPLAY']=':'+value
                result=run_case(env,'headless-x11')
        finally:
            os.close(readfd)
            if writefd is not None:os.close(writefd)
            if server is not None and server.poll() is None:
                server.terminate()
                try:server.wait(timeout=3)
                except subprocess.TimeoutExpired:os.killpg(server.pid,signal.SIGKILL);server.wait(timeout=3)

    if not result['passed']:raise SystemExit('Original Toolkit assertions, backend selection or process result failed')
