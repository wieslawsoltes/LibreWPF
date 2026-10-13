"""Close one identified disposable-runner startup window before Gallery starts.

No credentials, clipboard, input synthesis, forced termination or settings changes.
The normal WM_CLOSE request may be refused, in which case setup fails closed.
"""
import argparse, ctypes as C, hashlib, json, os, time
from ctypes import wintypes as W
from pathlib import Path

def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    if os.name!='nt' or any(os.environ.get(k)!=v for k,v in {
        'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
        'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items()):
        raise SystemExit('Requires the disposable GitHub-hosted LibreWPF Windows desktop')
    if a.output.exists():raise SystemExit('Refusing to replace an earlier receipt')
    r={'diagnosticOnly':True,'qualified':False,'inputSent':False,'clipboardAccessed':False,
       'forcedTermination':False,'settingsChanged':False,'closeRequests':0,
       'sourceSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    u=C.WinDLL('user32',use_last_error=True);k=C.WinDLL('kernel32',use_last_error=True)
    def bind(lib,name,result,*args):
        f=getattr(lib,name);f.restype=result;f.argtypes=args;return f
    bind(u,'GetForegroundWindow',W.HWND);bind(u,'GetShellWindow',W.HWND)
    bind(u,'IsWindow',W.BOOL,W.HWND);bind(u,'IsWindowVisible',W.BOOL,W.HWND)
    bind(u,'GetWindowThreadProcessId',W.DWORD,W.HWND,C.POINTER(W.DWORD))
    bind(u,'GetClassNameW',C.c_int,W.HWND,W.LPWSTR,C.c_int)
    bind(u,'GetWindowTextW',C.c_int,W.HWND,W.LPWSTR,C.c_int)
    bind(u,'OpenInputDesktop',W.HANDLE,W.DWORD,W.BOOL,W.DWORD);bind(u,'CloseDesktop',W.BOOL,W.HANDLE)
    bind(u,'GetUserObjectInformationW',W.BOOL,W.HANDLE,C.c_int,W.LPVOID,W.DWORD,C.POINTER(W.DWORD))
    bind(u,'SendMessageTimeoutW',C.c_ssize_t,W.HWND,W.UINT,C.c_size_t,C.c_ssize_t,W.UINT,W.UINT,C.POINTER(C.c_size_t))
    bind(k,'OpenProcess',W.HANDLE,W.DWORD,W.BOOL,W.DWORD);bind(k,'CloseHandle',W.BOOL,W.HANDLE)
    bind(k,'QueryFullProcessImageNameW',W.BOOL,W.HANDLE,W.DWORD,W.LPWSTR,C.POINTER(W.DWORD))
    bind(k,'ProcessIdToSessionId',W.BOOL,W.DWORD,C.POINTER(W.DWORD))
    bind(k,'GetSystemDirectoryW',W.UINT,W.LPWSTR,W.UINT)
    def session(pid):
        value=W.DWORD()
        if not k.ProcessIdToSessionId(pid,C.byref(value)):raise C.WinError(C.get_last_error())
        return value.value
    def snapshot(hwnd):
        pid=W.DWORD();thread=u.GetWindowThreadProcessId(hwnd,C.byref(pid))
        title=C.create_unicode_buffer(256);name=C.create_unicode_buffer(256)
        u.GetWindowTextW(hwnd,title,256);u.GetClassNameW(hwnd,name,256)
        return {'hwnd':hwnd,'pid':pid.value,'thread':thread,'class':name.value,'title':title.value,
                'visible':bool(u.IsWindowVisible(hwnd)),'session':session(pid.value) if pid.value else None}
    start=time.monotonic();handle=None;desktop=None
    try:
        desktop=u.OpenInputDesktop(0,False,1)
        if not desktop:raise C.WinError(C.get_last_error())
        name=C.create_unicode_buffer(256);needed=W.DWORD()
        if not u.GetUserObjectInformationW(desktop,2,name,C.sizeof(name),C.byref(needed)):raise C.WinError(C.get_last_error())
        if name.value!='Default':raise RuntimeError('Only the ordinary Default input desktop is admitted')
        r['inputDesktop']=name.value;r['before']=before=snapshot(u.GetForegroundWindow())
        r['shell']=shell=snapshot(u.GetShellWindow());r['callerSession']=session(os.getpid())
        if not r['callerSession'] or before['session']!=r['callerSession'] or shell['session']!=r['callerSession']:
            raise RuntimeError('Foreground, shell and observer must share the interactive session')
        if before['class']=='Progman' and before['hwnd']==shell['hwnd']:
            r['result']='ordinary shell already foreground';return
        if before['class']!='Windows.UI.Core.CoreWindow' or before['title']!='Microsoft account' or not before['visible']:
            raise RuntimeError('Unidentified foreground window; no close request sent')
        handle=k.OpenProcess(0x1000,False,before['pid'])
        if not handle:raise C.WinError(C.get_last_error())
        image=C.create_unicode_buffer(1024);size=W.DWORD(1024)
        if not k.QueryFullProcessImageNameW(handle,0,image,C.byref(size)):raise C.WinError(C.get_last_error())
        system=C.create_unicode_buffer(1024);length=k.GetSystemDirectoryW(system,1024)
        if not 0<length<1024:raise RuntimeError('Invalid native system directory')
        r['image']=image.value
        if os.path.normcase(image.value)!=os.path.normcase(str(Path(system.value)/'WWAHost.exe')):
            raise RuntimeError('Foreground is not the exact Windows WWAHost image')
        if snapshot(u.GetForegroundWindow())!=before:raise RuntimeError('Foreground identity changed before close')
        result=C.c_size_t();r['closeRequests']=1;C.set_last_error(0)
        sent=u.SendMessageTimeoutW(before['hwnd'],0x10,0,0,0x22,1000,C.byref(result))
        r['messageSucceeded']=bool(sent);r['messageResult']=result.value;r['messageError']=C.get_last_error()
        deadline=time.monotonic()+2
        while u.IsWindow(before['hwnd']) and u.IsWindowVisible(before['hwnd']) and time.monotonic()<deadline:time.sleep(.025)
        r['windowClosed']=not u.IsWindow(before['hwnd']) or not u.IsWindowVisible(before['hwnd'])
        r['after']=snapshot(u.GetForegroundWindow())
        if not r['windowClosed']:raise RuntimeError('The startup window declined normal close')
        r['result']='identified startup window accepted normal close'
    except Exception as e:
        r['error']=str(e);raise
    finally:
        if handle:k.CloseHandle(handle)
        if desktop:u.CloseDesktop(desktop)
        r['elapsedSeconds']=time.monotonic()-start
        a.output.parent.mkdir(parents=True,exist_ok=True)
        with a.output.open('x') as f:json.dump(r,f,indent=2)

if __name__=='__main__':main()
