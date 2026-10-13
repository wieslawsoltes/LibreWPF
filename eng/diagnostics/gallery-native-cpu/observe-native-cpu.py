"""One bounded named WPR CPU recording of the exact private hosted Gallery child."""
import argparse, ctypes as C, hashlib, json, os, subprocess, time
from ctypes import wintypes as W
from pathlib import Path
assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
    'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
    'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--reader',type=Path,required=True);a=p.parse_args()
out=a.root/'native-cpu-observation';out.mkdir(exist_ok=False)
receipt={'diagnosticOnly':True,'qualified':False,'kind':'At most 60-second native CPU recording; separate diagnostic, original application60-second deadline unchanged',
         'inputSent':False,'clipboardAccessed':False,'traceEventVersion':'3.1.23'}
start=time.monotonic();k=C.WinDLL('kernel32',use_last_error=True)
def bind(name,result,*args):
    f=getattr(k,name);f.restype=result;f.argtypes=args;return f
bind('OpenProcess',W.HANDLE,W.DWORD,W.BOOL,W.DWORD);bind('CloseHandle',W.BOOL,W.HANDLE)
bind('WaitForSingleObject',W.DWORD,W.HANDLE,W.DWORD)
bind('QueryFullProcessImageNameW',W.BOOL,W.HANDLE,W.DWORD,W.LPWSTR,C.POINTER(W.DWORD))
bind('GetProcessTimes',W.BOOL,W.HANDLE,*[C.POINTER(W.FILETIME)]*4)
handle=None; recording=False
instance="Gallery-"+os.environ["GITHUB_RUN_ID"]+"-"+os.environ["GITHUB_RUN_ATTEMPT"]
wpr=str(Path(os.environ["SystemRoot"])/"System32/wpr.exe")
def invoke(arguments,name,timeout):
    with (out/name).open("xb") as log:
        return subprocess.run([wpr,*arguments,"-instancename",instance],stdout=log,stderr=subprocess.STDOUT,timeout=timeout).returncode
def times():
    values=[W.FILETIME() for _ in range(4)]
    if not k.GetProcessTimes(handle,*[C.byref(v) for v in values]):raise C.WinError(C.get_last_error())
    return {name:((v.dwHighDateTime<<32)|v.dwLowDateTime) for name,v in zip(['creation100ns','exit100ns','kernel100ns','user100ns'],values)}
try:
    target=a.root/'runtime/diagnostic-child.json'
    while not target.exists():
        if time.monotonic()-start>15:raise RuntimeError('No owned child within start bound')
        time.sleep(.025)
    assert target.stat().st_size<8192;t=json.loads(target.read_text());receipt['target']=t
    expected=a.root/'consumer/bin/Release/net10.0-windows/GalleryClipboardApp.exe'
    assert Path(t['app']).resolve()==expected.resolve()
    assert hashlib.sha256(expected.read_bytes()).hexdigest()==t['sha256']=='e7e9b9595630e971900feb2c056021c3b523e90e72321cb474125b1e86ce8f38'
    handle=k.OpenProcess(0x101000,False,t['pid'])
    if not handle:raise C.WinError(C.get_last_error())
    image=C.create_unicode_buffer(2048);size=W.DWORD(2048)
    if not k.QueryFullProcessImageNameW(handle,0,image,C.byref(size)):raise C.WinError(C.get_last_error())
    assert os.path.normcase(image.value)==os.path.normcase(str(expected.resolve())) and k.WaitForSingleObject(handle,0)==258
    receipt['before']=times()
    receipt['instance']=instance
    receipt['startExit']=invoke(['-start','CPU.Verbose'],'wpr-start.log',15)
    assert receipt['startExit']==0,'Named CPU recording did not start'
    recording=True; capture_start=time.monotonic();receipt["processObservation"]=[];last_sample=-1
    while time.monotonic()-capture_start<60 and k.WaitForSingleObject(handle,0)==258:
        second=int(time.monotonic()-capture_start)
        if second!=last_sample:
            receipt['processObservation'].append({'recordSeconds':time.monotonic()-capture_start,'processTimes':times(),
                'existingReceipts':[n for n in ['activation-target.json','activation.json','pasted.json','retained.json','host-result.json','receipt.json'] if (a.root/'runtime'/n).is_file()]})
            last_sample=second
        time.sleep(.05)
    receipt['recordSeconds']=time.monotonic()-capture_start
    receipt['after']=times();receipt['processExited']=k.WaitForSingleObject(handle,0)==0
    receipt['observedCpuSeconds']=sum(receipt['after'][n]-receipt['before'][n] for n in ['kernel100ns','user100ns'])/10000000
    receipt['statusExit']=invoke(['-status'],'wpr-status.log',10)
    trace=out/'gallery.etl'
    receipt['stopExit']=invoke(['-stop',str(trace),'-skipPdbGen'],'wpr-stop.log',60)
    assert receipt['stopExit']==0,'CPU recording did not finalize'
    recording=False
    with trace.open('rb') as f:receipt['traceSha256']=hashlib.file_digest(f,'sha256').hexdigest()
    receipt['traceBytes']=trace.stat().st_size
    assert 0<trace.stat().st_size<1024**3,'Unexpected trace size'
    with (out/'reader.log').open('xb') as log:
        report=subprocess.run(['dotnet',str(a.reader),str(trace),str(t['pid']),str(out/'cpu-summary.json')],stdout=log,stderr=subprocess.STDOUT,timeout=90)
    receipt['reportExit']=report.returncode
except Exception as error:receipt['error']=str(error)
finally:
    if recording:
        try:receipt['cancelExit']=invoke(['-cancel'],'wpr-cancel.log',15)
        except Exception as error:receipt['cancelError']=str(error)
    if handle:k.CloseHandle(handle)
    receipt['elapsedSeconds']=time.monotonic()-start
    (out/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
