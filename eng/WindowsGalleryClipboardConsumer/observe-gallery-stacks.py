"""One bounded EventPipe observation of the exact private Gallery child."""
import argparse, ctypes as C, hashlib, json, os, subprocess, time
from ctypes import wintypes as W
from pathlib import Path
assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
    'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
    'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--tool',type=Path,required=True);a=p.parse_args()
out=a.root/'stack-observation';out.mkdir(exist_ok=False)
receipt={'diagnosticOnly':True,'qualified':False,'kind':'sampled managed thread time, not CPU attribution',
         'inputSent':False,'clipboardAccessed':False,'toolVersion':'10.0.750501'}
start=time.monotonic();k=C.WinDLL('kernel32',use_last_error=True)
def bind(name,result,*args):
    f=getattr(k,name);f.restype=result;f.argtypes=args;return f
bind('OpenProcess',W.HANDLE,W.DWORD,W.BOOL,W.DWORD);bind('CloseHandle',W.BOOL,W.HANDLE)
bind('WaitForSingleObject',W.DWORD,W.HANDLE,W.DWORD)
bind('QueryFullProcessImageNameW',W.BOOL,W.HANDLE,W.DWORD,W.LPWSTR,C.POINTER(W.DWORD))
bind('GetProcessTimes',W.BOOL,W.HANDLE,*[C.POINTER(W.FILETIME)]*4)
handle=None
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
    assert hashlib.sha256(expected.read_bytes()).hexdigest()==t['sha256']
    handle=k.OpenProcess(0x101000,False,t['pid'])
    if not handle:raise C.WinError(C.get_last_error())
    image=C.create_unicode_buffer(2048);size=W.DWORD(2048)
    if not k.QueryFullProcessImageNameW(handle,0,image,C.byref(size)):raise C.WinError(C.get_last_error())
    assert os.path.normcase(image.value)==os.path.normcase(str(expected.resolve())) and k.WaitForSingleObject(handle,0)==258
    receipt['before']=times()
    command=[str(a.tool),'collect','--process-id',str(t['pid']),'--duration','00:00:00:20','--buffersize','32',
             '--profile','dotnet-common,dotnet-sampled-thread-time',
             '--providers','Microsoft-Windows-DotNETRuntime:0x100003801D:5','--output',str(out/'gallery.nettrace')]
    with (out/'trace.log').open('xb') as log:
        tracer=subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT)
        try:receipt['traceExit']=tracer.wait(timeout=85)
        except subprocess.TimeoutExpired:
            tracer.kill();tracer.wait(timeout=10);raise RuntimeError('Observer deadline reached')
    receipt['after']=times();receipt['processExited']=k.WaitForSingleObject(handle,0)==0
    receipt['observedCpuSeconds']=sum(receipt['after'][n]-receipt['before'][n] for n in ['kernel100ns','user100ns'])/10000000
    trace=out/'gallery.nettrace'
    if trace.exists():
        with trace.open('rb') as f:receipt['traceSha256']=hashlib.file_digest(f,'sha256').hexdigest()
        receipt['traceBytes']=trace.stat().st_size
        with (out/'top-stacks.txt').open('xb') as log:
            report=subprocess.run([str(a.tool),'report',str(trace),'topN','--number','35','--inclusive','--verbose'],stdout=log,stderr=subprocess.STDOUT,timeout=40)
        receipt['reportExit']=report.returncode
except Exception as error:receipt['error']=str(error)
finally:
    if handle:k.CloseHandle(handle)
    receipt['elapsedSeconds']=time.monotonic()-start
    (out/'receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
