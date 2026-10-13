"""One read-only native-window observation on the disposable hosted CI desktop."""
import argparse, ctypes as C, hashlib, json, os, time
from ctypes import wintypes as W
from pathlib import Path

def main():
    p=argparse.ArgumentParser();p.add_argument('--runtime',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    assert os.name=='nt' and all(os.environ.get(k)==v for k,v in {
        'GITHUB_ACTIONS':'true','RUNNER_ENVIRONMENT':'github-hosted','RUNNER_OS':'Windows',
        'GITHUB_REPOSITORY':'wieslawsoltes/LibreWPF'}.items())
    assert not a.output.exists()
    start=time.monotonic();r={'diagnosticOnly':True,'qualified':False,'inputSent':False,'clipboardAccessed':False,
        'observerSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'workflowCommit':os.environ.get('GITHUB_SHA')}
    u=C.WinDLL('user32',use_last_error=True)
    def bind(lib,name,result,*args):
        f=getattr(lib,name);f.restype=result;f.argtypes=args;return f
    bind(u,'SetProcessDpiAwarenessContext',W.BOOL,W.HANDLE)
    assert u.SetProcessDpiAwarenessContext(W.HANDLE(-4))
    for name in ['IsWindow','IsWindowVisible','IsWindowEnabled','IsIconic','IsHungAppWindow']:
        bind(u,name,W.BOOL,W.HWND)
    for name in ['GetForegroundWindow','GetDesktopWindow','GetShellWindow']:bind(u,name,W.HWND)
    bind(u,'GetWindowThreadProcessId',W.DWORD,W.HWND,C.POINTER(W.DWORD))
    bind(u,'GetClassNameW',C.c_int,W.HWND,W.LPWSTR,C.c_int)
    bind(u,'GetWindowRect',W.BOOL,W.HWND,C.POINTER(W.RECT))
    bind(u,'ClientToScreen',W.BOOL,W.HWND,C.POINTER(W.POINT))
    bind(u,'GetWindowLongPtrW',C.c_ssize_t,W.HWND,C.c_int)
    bind(u,'WindowFromPoint',W.HWND,W.POINT)
    bind(u,'GetThreadDesktop',W.HANDLE,W.DWORD)
    bind(u,'OpenInputDesktop',W.HANDLE,W.DWORD,W.BOOL,W.DWORD)
    bind(u,'CloseDesktop',W.BOOL,W.HANDLE)
    bind(u,'GetUserObjectInformationW',W.BOOL,W.HANDLE,C.c_int,W.LPVOID,W.DWORD,C.POINTER(W.DWORD))
    bind(u,'SendMessageTimeoutW',C.c_ssize_t,W.HWND,W.UINT,C.c_size_t,C.c_ssize_t,W.UINT,W.UINT,C.POINTER(C.c_size_t))
    bind(u,'GetSystemMetrics',C.c_int,C.c_int)
    d=C.WinDLL('dwmapi',use_last_error=True);bind(d,'DwmGetWindowAttribute',C.c_int32,W.HWND,W.DWORD,W.LPVOID,W.DWORD)
    def desktop_name(handle):
        name=C.create_unicode_buffer(256);needed=W.DWORD()
        C.set_last_error(0)
        ok=bool(handle) and bool(u.GetUserObjectInformationW(handle,2,name,C.sizeof(name),C.byref(needed)))
        return {'handle':handle,'ok':ok,'name':name.value if ok else None,'error':C.get_last_error()}
    def window(hwnd):
        if not hwnd:return {'hwnd':hwnd}
        owner=W.DWORD();thread=u.GetWindowThreadProcessId(hwnd,C.byref(owner));name=C.create_unicode_buffer(256)
        u.GetClassNameW(hwnd,name,256);rect=W.RECT();got=bool(u.GetWindowRect(hwnd,C.byref(rect)))
        cloak=W.DWORD();hr=d.DwmGetWindowAttribute(hwnd,14,C.byref(cloak),C.sizeof(cloak))
        return {'hwnd':hwnd,'pid':owner.value,'thread':thread,'class':name.value,
                'visible':bool(u.IsWindowVisible(hwnd)),'enabled':bool(u.IsWindowEnabled(hwnd)),
                'iconic':bool(u.IsIconic(hwnd)),'hung':bool(u.IsHungAppWindow(hwnd)),
                'style':u.GetWindowLongPtrW(hwnd,-16),'extendedStyle':u.GetWindowLongPtrW(hwnd,-20),
                'rect':[rect.left,rect.top,rect.right,rect.bottom] if got else None,
                'cloaked':cloak.value if hr==0 else None,'cloakHresult':hr,
                'desktop':desktop_name(u.GetThreadDesktop(thread))}
    try:
        target=a.runtime/'activation-target.json'
        while not target.exists():
            if time.monotonic()-start>=60:raise RuntimeError('No original activation target within observation bound')
            time.sleep(0.025)
        assert target.stat().st_size<=65536
        t=json.loads(target.read_text());hwnd=t['hwnd'];r['target']=t
        owned=window(hwnd);assert owned['pid']==t['pid'] and owned['visible'] and u.IsWindow(hwnd)
        r['owned']=owned;r['foreground']=window(u.GetForegroundWindow());r['desktopWindow']=window(u.GetDesktopWindow());r['shellWindow']=window(u.GetShellWindow())
        C.set_last_error(0);input_desktop=u.OpenInputDesktop(0,False,1);r['openInputDesktopError']=C.get_last_error()
        if input_desktop:
            try:r['inputDesktop']=desktop_name(input_desktop)
            finally:assert u.CloseDesktop(input_desktop)
        r['virtualDesktop']=[u.GetSystemMetrics(i) for i in [76,77,78,79]]
        origin=W.POINT();assert u.ClientToScreen(hwnd,C.byref(origin));r['clientOrigin']=[origin.x,origin.y]
        left,top,right,bottom=owned['rect'];y=(top+origin.y)//2;r['captionProbes']=[]
        for fraction in [2,1,3]:
            x=left+(right-left)*fraction//4
            probe={'point':[x,y],'windowAtPoint':window(u.WindowFromPoint(W.POINT(x,y)))}
            if -32768<=x<=32767 and -32768<=y<=32767:
                hit=C.c_size_t();C.set_last_error(0)
                ok=u.SendMessageTimeoutW(hwnd,0x84,0,(x&65535)|((y&65535)<<16),0x22,200,C.byref(hit))
                probe.update(hitTestSucceeded=bool(ok),hitTestResult=hit.value if ok else None,hitTestError=C.get_last_error())
            r['captionProbes'].append(probe)
    except Exception as e:r['error']=str(e)
    finally:
        r['elapsed']=time.monotonic()-start
        with a.output.open('x') as f:json.dump(r,f,indent=2)

if __name__=='__main__':main()
