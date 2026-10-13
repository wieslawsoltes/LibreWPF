"""One exact-input observation with ordinary heap policy; no acceptance claim."""
import importlib.util,json,os,subprocess,sys,tempfile,time,winreg
from pathlib import Path

assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
    'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted',
    'RUNNER_OS':'Windows','GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
repo=Path.cwd();helper_repo=repo/'diagnostic-tools';root=repo/'artifacts/glyph-heap-diagnostic';root.mkdir(exist_ok=True)
sys.path.insert(0,str(helper_repo/'eng'))
spec=importlib.util.spec_from_file_location('original_idle',helper_repo/'eng/progpu-wpf-showcase-idle.py')
idle=importlib.util.module_from_spec(spec);spec.loader.exec_module(idle)
import showcase_idle_crash as crash
import showcase_idle_debugger as debugger
app=Path((root/'app-path.txt').read_text(encoding='utf-8-sig').strip()).resolve(strict=True)
helper=Path(os.environ['RUNNER_TEMP'])/'showcase-native-debugger/ShowcaseNativeDebugger.exe'
gflags=Path(os.environ['HEAP_GFLAGS']).resolve(strict=True)
assert helper.is_file() and debugger.machine(app)==debugger.machine(helper)==0xAA64
assert app.name=='ProGPU.Wpf.ShowcaseApp.exe'
out=root/'run';out.mkdir(exist_ok=False)
before=idle.payload_hashes(app.parent)
receipt={'diagnosticOnly':True,'qualified':False,'originalLimitSeconds':120,
    'debuggerChildLimitSeconds':110,'payloadSha256Before':before,
    'appSha256':crash.digest(app),'debuggerSha256':crash.digest(helper),
    'gflagsSha256':crash.digest(gflags),'configuration':'ordinary allocator; no heap-policy mutation',
    'source':'f9507dc67920d9e72af64f35c8455b59e70ed87f'}
expected=json.loads((Path(__file__).resolve().parent/'expected.json').read_text())
recovery=dict(expected=expected,actualAppSha256=crash.digest(app),actualPayloadSha256=before,diagnosticOnly=True,qualified=False)
recovery['exactOriginalAppAndFivePayloads']=recovery['actualAppSha256']==expected['appSha256'] and before==expected['payloadSha256Before']
(root/'exact-input-recovery.json').write_text(json.dumps(recovery,indent=2))
assert recovery['exactOriginalAppAndFivePayloads'],'Rebuilt inputs differ from the failed original; no app execution permitted'
receipt['exactOriginalInputsRecovered']=True
capture=crash.Capture(app,out,crash.WindowsRegistry())
image=None;key=None;key_owned=False;enabled=False
ifeo=r'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'+'\\'
code=1
def run_tool(arguments,name):
    result=subprocess.run([str(gflags),*arguments],capture_output=True,text=True,timeout=15)
    (out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
    if result.returncode:raise RuntimeError('GFlags '+name+' failed: '+str(result.returncode))
    return result.stdout+result.stderr
try:
    image=capture.prepare();key=ifeo+image.name
    # A unique owned image must have no IFEO override before execution.
    try:
        handle=winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,key,0,winreg.KEY_READ|winreg.KEY_WOW64_64KEY)
    except FileNotFoundError:
        receipt['ownedImageIfeoAbsent']=True
    else:
        winreg.CloseKey(handle)
        raise RuntimeError('Unexpected heap/debug policy on the unique owned image')
    receipt['imageName']=image.name
    first=Path(tempfile.mkdtemp(prefix='showcase-original-heap-av-'))/'first-chance-av.dmp'
    event_path=out/'debug-event.json'
    env=os.environ.copy()
    for name in idle.CONFLICTING_MODES:env.pop(name,None)
    assert env.get('PROGPU_NATIVE_TRACE_COMPUTE')!='1'
    env['PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_VALIDATE']='1'
    env['PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_STATUS_PATH']=str(out/'application-receipt.json')
    env['PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_PHASE_PATH']=str(out/'application-phases.jsonl')
    code,timed=idle.run_child([str(helper),str(image),str(capture.dump_directory),str(event_path),
        '--first-chance-av',str(first)],app.parent,env,out,timeout=120,outcome=receipt)
    receipt.update(exitCode=code,timedOut=timed)
    if timed:raise RuntimeError('Original diagnostic deadline exceeded')
    event=debugger.load_event(event_path,0xAA64);receipt['nativeEvent']=event
    assert event['processId']!=receipt['childProcessId'] and event['exitCode']==code
    assert event['snapshotAfterMs']==0 and event['firstChanceAvEnabled']
    receipt['firstChance']=debugger.collect_first_chance_av(first,out,event,0xAA64)
    receipt['unhandled']=capture.collect(event['processId'],event['exitCode'],False)
    if event['captured']:
        debugger.correlate_exception(capture.dump_directory/f"{image.name}.{event['processId']}.dmp",event)
    if code==0:
        idle.validate_receipt(idle.load_receipt(out/'application-receipt.json'),before)
        receipt['originalApplicationAssertionsPassed']=True
except Exception as e:
    receipt['error']=type(e).__name__+': '+str(e);code=1
finally:
    receipt['cleanupErrors']=[]
    receipt['cleanupErrors']+=capture.close()
    receipt['payloadSha256After']=idle.payload_hashes(app.parent)
    receipt['payloadUnchanged']=before==receipt['payloadSha256After'] and crash.digest(app)==receipt['appSha256']
    (out/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in receipt.items() if not k.startswith('payloadSha256')},indent=2))
raise SystemExit(0 if code==0 and receipt.get('originalApplicationAssertionsPassed') and receipt['payloadUnchanged'] and not receipt['cleanupErrors'] else 1)
