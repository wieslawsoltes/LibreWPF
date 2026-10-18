"""Original no-input multi-window process; preserves120s phase/600s outer bounds."""
import hashlib,json,os,pathlib,resource,signal,subprocess,time

def run_case(environment,case):
    root=pathlib.Path.cwd(); preparation=json.loads((root/'artifacts/ubuntu26-multi-window-preparation.json').read_text())
    assert preparation['compiled'] and not preparation['applicationExecuted']
    app=pathlib.Path(os.environ['ORIGINAL_MULTI_WINDOW_APP']);assert str(app)==preparation['application']
    def inventory():return {str(p.relative_to(app.parent)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(app.parent.rglob('*')) if p.is_file()}
    assert inventory()==preparation['outputs'],'Output inventory changed before execution'
    env=environment.copy()
    for key in list(env):
        if key.startswith(('PROGPU','ProGpu')) or key in ('EGL_PLATFORM','LIBGL_ALWAYS_SOFTWARE','GALLIUM_DRIVER','__EGL_VENDOR_LIBRARY_FILENAMES','WGPU_BACKEND'):env.pop(key)
    # Core dump storage is disabled; original stderr, exit status and all output hashes remain.
    resource.setrlimit(resource.RLIMIT_CORE,(0,0))
    command=['dotnet',str(app),'--backend','gl'];started=time.monotonic()
    record=dict(case=case,qualified=False,inputSent=False,command=command,outputsBefore=preparation['outputs'],environment={k:env.get(k) for k in ('DISPLAY','WAYLAND_DISPLAY','XDG_RUNTIME_DIR','XAUTHORITY','XDG_SESSION_TYPE')},phaseTimeoutSeconds=120,outerTimeoutSeconds=600)
    logpath=root/('artifacts/ubuntu26-multi-window-'+case+'.application.log')
    child=None
    try:
        with logpath.open('w') as log:
            child=subprocess.Popen(command,cwd=app.parent,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            record['pid']=child.pid
            try:child.wait(timeout=600)
            except subprocess.TimeoutExpired:
                record['outerTimeout']=True;os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=5)
        text=logpath.read_text(errors='replace')
        record.update(exitCode=child.returncode,reportedSuccess='ProGPU WPF multi-window smoke succeeded.' in text,applicationLogSha256=hashlib.sha256(logpath.read_bytes()).hexdigest())
        record['outputsUnchanged']=inventory()==preparation['outputs']
        record['passed']=child.returncode==0 and record['reportedSuccess'] and record['outputsUnchanged']
    finally:
        if child is not None and child.poll() is None:os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=5)
        record['elapsedSeconds']=time.monotonic()-started
        (root/('artifacts/ubuntu26-multi-window-'+case+'.json')).write_text(json.dumps(record,indent=2)+'\n')
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
                run_case(env,'headless-x11')
        finally:
            os.close(readfd)
            if writefd is not None:os.close(writefd)
            if server is not None and server.poll() is None:
                server.terminate()
                try:server.wait(timeout=3)
                except subprocess.TimeoutExpired:os.killpg(server.pid,signal.SIGKILL);server.wait(timeout=3)
