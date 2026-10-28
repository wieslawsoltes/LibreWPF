"""No input: observe one owned programmatic application, preserve each original frame."""
import hashlib,importlib.util,json,os,signal,struct,subprocess,time,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parent
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def run_case(environment,case,readiness_deadline):
    assert os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted' and os.environ.get('GITHUB_REPOSITORY')=='wieslawsoltes/LibreWPF'
    assert os.environ.get('GITHUB_RUN_ATTEMPT')=='1'
    receipt=json.loads(Path('artifacts/paint-tree-build/receipt.json').read_text());app=Path(receipt['application']);renderer=os.environ['CAPTION_RENDERER'];assert receipt['renderer']==renderer
    def inventory():return {str(f.relative_to(app.parent)):digest(f) for f in sorted(app.parent.rglob('*')) if f.is_file()}
    assert inventory()==receipt['outputs']
    for name,sha in json.loads((ROOT/'helper-manifest.json').read_text())['files'].items():assert digest(ROOT/'helpers'/name)==sha
    spec=importlib.util.spec_from_file_location('paint_x11',ROOT/'helpers/librewinforms-popup-x11.py');helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
    out=Path('artifacts/paint-tree').resolve();out.mkdir();run=uuid.uuid4().hex;child=desktop=None
    env=environment.copy()
    for key in list(env):
        if key.startswith(('PROGPU','ProGpu')) or key in ('EGL_PLATFORM','LIBGL_ALWAYS_SOFTWARE','GALLIUM_DRIVER','WGPU_BACKEND'):env.pop(key)
    env.update(PROGPU_WGPU_BACKEND='gl',PROGPU_BACKEND_DIAGNOSTICS='1')
    old={key:os.environ.get(key) for key in ('DISPLAY','XAUTHORITY')};started=time.monotonic();start=None;deadline=None
    result=dict(qualified=False,completed=False,passed=False,applicationExecuted=False,inputEventsSent=0,nativeEventAttempts=0,renderer=renderer,run=run,captures=[],cleanupErrors=[],forcedCleanup=False)
    def remaining():
        assert deadline is not None
        left=deadline-time.monotonic()
        if left<=0:raise TimeoutError('Original60second process limit')
        return left
    try:
        for key in old:os.environ[key]=env[key]
        # The immutable helper's original contract preflights before launch.
        # Keep all readiness inside the existing desktop's absolute30s budget.
        readiness_left=readiness_deadline-time.monotonic()
        if readiness_left<=0:raise TimeoutError('Original30second desktop startup limit')
        with helper.protocol_timeout(min(10,readiness_left)):desktop=helper.X11Desktop()
        if time.monotonic()>=readiness_deadline:raise TimeoutError('Original30second desktop startup limit')
        result['protocolReadinessSeconds']=time.monotonic()-started
        (out/'x11-preflight.json').write_text(json.dumps(desktop.provenance,indent=2)+'\n')
        with (out/'application.log').open('w') as log:
            start=time.monotonic();deadline=start+60;desktop.deadline=deadline
            child=subprocess.Popen(['dotnet',str(app),str(out/'app'),run,renderer],env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            result['pid']=child.pid;result['applicationExecuted']=True
            for index in range(4):
                for stage in range(2):
                    f=out/'app'/f'snapshot-{index}-{stage}.json'
                    while not f.exists():
                        remaining()
                        if child.poll() is not None:raise RuntimeError('Application exited before snapshot')
                        time.sleep(.02)
                    assert f.stat().st_size<4*1024*1024
                    state=json.loads(f.read_text());assert state['pid']==child.pid and state['run']==run and state['renderer']==renderer and state['index']==index and state['stage']==stage and state['elapsedSeconds']<55
                    with helper.protocol_timeout(min(3,remaining())):windows=desktop.windows(child.pid)
                    (out/f'native-{index}-{stage}.json').write_text(json.dumps(dict(source=state,nativeWindows=windows),indent=2)+'\n')
                    owned=[w for w in windows if w['xid']==state['nativeHandle'] and w['title']==state['title']];assert len(owned)==1
                    window=owned[0];assert window['pid']==child.pid and window['client']['width']==state['geometry']['PixelWidth'] and window['client']['height']==state['geometry']['PixelHeight']
                    image=out/f'{index}-{stage}.png';r=subprocess.run(['import','-window',str(window['xid']),str(image)],env=env,capture_output=True,timeout=min(5,remaining()));assert r.returncode==0 and image.stat().st_size<8*1024*1024
                    data=image.read_bytes();assert data[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',data[16:24])==(window['client']['width'],window['client']['height'])
                    result['captures'].append(dict(name=image.name,sha256=digest(image),sourceSha256=digest(f),frames=state['frames'],sourceSeconds=state['elapsedSeconds']))
                    (out/'app'/f'capture-{index}-{stage}.ack').write_text(run)
            result['exitCode']=child.wait(timeout=remaining());assert result['exitCode']==0
        complete=json.loads((out/'app/complete.json').read_text());assert complete['completed'] and complete['inputEventsSent']==0 and complete['pid']==child.pid and complete['elapsedSeconds']<55
        result['sourceCompletion']=complete;result['outputsUnchanged']=inventory()==receipt['outputs'];assert result['outputsUnchanged'];result.update(passed=True,completed=True)
    except BaseException as error:result['error']=repr(error);raise
    finally:
        if child is not None and child.poll() is None:
            result['forcedCleanup']=True
            try:os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=3)
            except BaseException as error:result['cleanupErrors'].append(repr(error))
        if desktop is not None:
            try:desktop.close()
            except BaseException as error:result['cleanupErrors'].append(repr(error))
        for key,value in old.items():
            if value is None:os.environ.pop(key,None)
            else:os.environ[key]=value
        if result['cleanupErrors']:result.update(passed=False,completed=False)
        result['elapsedSeconds']=None if start is None else time.monotonic()-start;result['totalElapsedSeconds']=time.monotonic()-started;(out/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    return result
