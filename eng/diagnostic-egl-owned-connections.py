"""Independent EGL lifetime observation of owned Xlib connections; no input."""
import ctypes as C,json,os,pathlib,time

def observe():
    # Executed in a separate child with this verified owned desktop environment.
    started=time.monotonic();record={'qualified':False,'inputSent':False,'contexts':[],'cleanup':[]}
    x=C.CDLL('libX11.so.6');e=C.CDLL('libEGL.so.1');g=C.CDLL('libGLESv2.so.2')
    def bind(lib,name,result,args):
        f=getattr(lib,name);f.restype=result;f.argtypes=args;return f
    ptr=C.c_void_p;integer=C.c_int;uint=C.c_uint;char=C.c_char_p
    open_display=bind(x,'XOpenDisplay',ptr,[char]);close_display=bind(x,'XCloseDisplay',integer,[ptr])
    display_string=bind(x,'XDisplayString',char,[ptr]);screen_number=bind(x,'XDefaultScreen',integer,[ptr])
    query=bind(e,'eglQueryString',char,[ptr,integer]);error=bind(e,'eglGetError',integer,[])
    platform_display=bind(e,'eglGetPlatformDisplay',ptr,[uint,ptr,C.POINTER(C.c_ssize_t)])
    initialize=bind(e,'eglInitialize',uint,[ptr,C.POINTER(integer),C.POINTER(integer)])
    terminate=bind(e,'eglTerminate',uint,[ptr]);bind_api=bind(e,'eglBindAPI',uint,[uint])
    choose=bind(e,'eglChooseConfig',uint,[ptr,C.POINTER(integer),C.POINTER(ptr),integer,C.POINTER(integer)])
    create_context=bind(e,'eglCreateContext',ptr,[ptr,ptr,ptr,C.POINTER(integer)])
    destroy_context=bind(e,'eglDestroyContext',uint,[ptr,ptr])
    create_pbuffer=bind(e,'eglCreatePbufferSurface',ptr,[ptr,ptr,C.POINTER(integer)])
    destroy_surface=bind(e,'eglDestroySurface',uint,[ptr,ptr])
    make_current=bind(e,'eglMakeCurrent',uint,[ptr,ptr,ptr,ptr])
    gl_string=bind(g,'glGetString',char,[uint]);gl_error=bind(g,'glGetError',uint,[])
    source=None;owners=[]
    def require(result,label):
        if not result:raise RuntimeError(label+' EGL error '+hex(error()))
        return result
    def cleanup(owner):
        if owner.get('retired'):return
        d=owner.get('egl')
        steps={}
        if owner.get('initialized'):
            steps['unmake']=int(make_current(d,None,None,None))
            if owner.get('context'):steps['context']=int(destroy_context(d,owner['context']))
            if owner.get('pbuffer'):steps['pbuffer']=int(destroy_surface(d,owner['pbuffer']))
            steps['terminate']=int(terminate(d))
        if owner.get('native'):steps['closeXlib']=int(close_display(owner['native']))
        owner['retired']=True;record['cleanup'].append(steps)
        assert all(v==1 for k,v in steps.items() if k!='closeXlib') and steps.get('closeXlib',0)==0,steps
    try:
        assert os.environ.get('RUNNER_ENVIRONMENT')=='github-hosted'
        assert os.environ.get('GITHUB_REPOSITORY')=='wieslawsoltes/LibreWPF'
        assert os.environ.get('DISPLAY') and os.environ.get('WAYLAND_DISPLAY') and os.environ.get('XAUTHORITY')
        source=open_display(None);assert source,'Could not open verified owned Xwayland'
        name=display_string(source);assert name
        screen=screen_number(source)
        attributes=(C.c_ssize_t*3)(0x31D6,screen,0x3038)
        extensions=query(None,0x3055);assert extensions
        record.update(clientExtensions=extensions.decode(),sourceName=name.decode(),screen=screen)
        borrowed=require(platform_display(0x31D5,source,attributes),'source display query')
        value=query(borrowed,0x3054);record['sourceEglBefore']=value.decode() if value else None;record['sourceEglBeforeError']=hex(error())
        assert record['sourceEglBefore'] is None,'Reference source unexpectedly initialized'
        for i in range(2):
            native=open_display(name);assert native
            owner={'native':native};owners.append(owner)
            d=require(platform_display(0x31D5,native,attributes),'private display query');owner['egl']=d
            assert d!=borrowed and all(old['egl']!=d for old in owners[:-1]),'Private display alias'
            major,minor=integer(),integer();require(initialize(d,C.byref(major),C.byref(minor)),'initialize');owner['initialized']=True
            require(bind_api(0x30A0),'bind GLES')
            config=ptr();count=integer();options=(integer*5)(0x3033,1,0x3040,0x40,0x3038)
            require(choose(d,options,C.byref(config),1,C.byref(count)),'choose configuration');assert count.value==1
            context=require(create_context(d,config,None,(integer*3)(0x3098,3,0x3038)),'create context');owner['context']=context
            pbuffer=require(create_pbuffer(d,config,(integer*5)(0x3057,1,0x3056,1,0x3038)),'create pbuffer');owner['pbuffer']=pbuffer
            require(make_current(d,pbuffer,pbuffer,context),'make current')
            version=gl_string(0x1F02);assert version and gl_error()==0
            record['contexts'].append({'eglVersion':[major.value,minor.value],'glVersion':version.decode(),'distinctFromSource':True,'distinctFromOtherOwner':True})
            require(make_current(d,None,None,None),'unmake current')
        cleanup(owners[0])
        survivor=owners[1]
        require(make_current(survivor['egl'],survivor['pbuffer'],survivor['pbuffer'],survivor['context']),'surviving context current')
        version=gl_string(0x1F02);assert version and version.decode()==record['contexts'][1]['glVersion'] and gl_error()==0
        record['survivorAfterOtherTermination']=True
        require(make_current(survivor['egl'],None,None,None),'survivor unmake current')
        cleanup(survivor)
        value=query(borrowed,0x3054);record['sourceEglAfter']=value.decode() if value else None;record['sourceEglAfterError']=hex(error())
        assert record['sourceEglAfter'] is None and record['sourceEglAfterError']==record['sourceEglBeforeError']
        record['passed']=True
    except BaseException as failure:
        record.update(passed=False,error=type(failure).__name__+': '+str(failure));raise
    finally:
        for owner in reversed(owners):
            try:cleanup(owner)
            except BaseException as failure:record.setdefault('cleanupErrors',[]).append(str(failure))
        if source:close_display(source)
        record['elapsedSeconds']=time.monotonic()-started
        pathlib.Path('artifacts/ubuntu26-egl-owned-connections.json').write_text(json.dumps(record,indent=2)+'\n')
    return record
if __name__=='__main__':observe()
