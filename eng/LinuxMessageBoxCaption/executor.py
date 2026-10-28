"""Pending owned-hosted X11 caption executor. Never imported by the original observer.

Execution requires the final reviewed proposal SHA via explicit opt-in. Preparation,
package/source admission and the disposable GNOME-session wrapper remain required.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import struct
import subprocess
import time
import uuid
from native_observation import validate_recorded_native
from contract import SCENARIOS, inside, interior, rect, require, validate_completion, validate_native, validate_snapshot

ROOT = Path(__file__).resolve().parent

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def read_source(path):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 65536, "Bounded owned snapshot required")
    def unique(pairs):
        out = {}
        for key, value in pairs:
            require(key not in out, "Duplicate observation field")
            out[key] = value
        return out
    def invalid(value): raise ValueError("Nonfinite observation token: " + value)
    return json.loads(path.read_text(), object_pairs_hook=unique, parse_constant=invalid)

def run_case(environment, case):
    require(os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted" and
            os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF", "Disposable owned repository runner required")
    proposal = ROOT / "decision.txt"
    require(proposal.is_file() and os.environ.get("LIBREWPF_CAPTION_INPUT_APPROVAL_SHA256") == digest(proposal),
            "The exact final caption-input proposal requires explicit approval before execution")
    renderer = os.environ.get("CAPTION_RENDERER", "NativeMilWgpu")
    require(renderer in ("NativeMilWgpu", "ManagedPortable"), "Explicit renderer required")
    preparation = read_source(Path("artifacts/ubuntu26-caption-preparation.json"))
    require(preparation.get("renderer") == renderer,"Prepared renderer differs from the explicit run selection")
    app = Path(os.environ["CAPTION_APP"]).resolve(strict=True)
    require(preparation.get("compiled") is True and preparation["application"] == str(app), "Qualified candidate preparation missing")
    sources = json.loads((ROOT / "sources.json").read_text())
    require(preparation["sources"] == {name: sha for name, sha in sources.items() if name.endswith((".cs", ".xaml"))},
            "Prepared source differs from this reviewed input observer")
    def inventory():
        values = {}
        for path in sorted(app.parent.rglob("*")):
            if path.is_file():
                require(not path.is_symlink(), "Linked application payload")
                values[str(path.relative_to(app.parent))] = digest(path)
        return values
    require(inventory() == preparation["outputs"], "Prepared application bytes changed")
    helper_manifest = json.loads((ROOT / "helper-manifest.json").read_text())
    for name, sha in helper_manifest["files"].items():
        require(digest(ROOT / "helpers" / name) == sha, "Immutable native helper changed")
    spec = importlib.util.spec_from_file_location("caption_x11", ROOT / "helpers/librewinforms-popup-x11.py")
    helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    output = Path("artifacts/caption-input").resolve()
    require(not output.exists(), "Fresh evidence required")
    output.mkdir()
    app_output = output / "app"
    run = uuid.uuid4().hex
    env = environment.copy()
    for key in list(env):
        if key.startswith(("PROGPU", "ProGpu")) or key in ("EGL_PLATFORM", "LIBGL_ALWAYS_SOFTWARE", "GALLIUM_DRIVER", "WGPU_BACKEND"):
            env.pop(key)
    env.update(PROGPU_WGPU_BACKEND="gl", PROGPU_BACKEND_DIAGNOSTICS="1")
    record = dict(qualified=False, completed=False, case=case, renderer=renderer, run=run,
                  processDeadlineSeconds=60, sourceDeadlineSeconds=55, maximumNativeEvents=64,
                  inputDeadlineSeconds=15, actions=[], captures=[], forcedCleanup=False)
    child = desktop = None
    state = None
    owner_identity = None
    dialog_identities = {}
    sequence = 0
    native_sequence = 0
    events = 0
    input_seconds = 0.0
    started = time.monotonic()
    deadline = started + 60
    old_display = {key: os.environ.get(key) for key in ("DISPLAY", "XAUTHORITY")}

    def remaining():
        value = deadline-time.monotonic()
        require(value > 0, "Original absolute60-second process deadline exhausted")
        if child is not None: require(child.poll() is None, "Owned source application exited early")
        log = output / "application.log"
        if log.exists(): require(log.stat().st_size <= 8*1024*1024, "Original application log budget exceeded")
        return value

    def observe_native():
        nonlocal native_sequence
        native_sequence += 1
        path = output / ("native-%06d.json" % native_sequence)
        windows = desktop.windows(child.pid)
        return validate_recorded_native(state, windows, path)

    def observe(predicate, unchanged_input=None):
        nonlocal state, sequence, owner_identity
        matches = 0
        while remaining() > 0:
            path = app_output / "latest.json"
            if not path.exists(): time.sleep(min(.02, remaining())); continue
            current = read_source(path)
            if current["sequence"] <= sequence: time.sleep(min(.02, remaining())); continue
            validate_snapshot(current, child.pid, run, renderer, sequence)
            handle = current["owner"]["nativeHandle"]
            if owner_identity is None: owner_identity = handle
            require(handle == owner_identity, "Original native owner was replaced")
            if current["dialog"]:
                handle = current["dialog"]["surface"]["nativeHandle"]
                previous = dialog_identities.setdefault(current["index"], handle)
                require(handle == previous, "Original native dialog was replaced")
            sequence = current["sequence"]
            if unchanged_input is not None:
                require(current["ownerInput"] == unchanged_input, "Disabled original owner accepted native input")
            state = current
            matches = matches+1 if predicate(current) else 0
            if matches >= 2:
                observe_native()
                return state

    def focus(handle):
        desktop.foreground(child.pid)
        require(desktop.scalar(desktop.root, "_NET_ACTIVE_WINDOW", desktop.atom_types.WINDOW) == handle,
                "Exact owned window is not active")
        require(desktop.display.get_input_focus().focus.id == handle, "Exact original client does not have keyboard focus")

    def target(window, bounds):
        observe_native()
        value = interior(bounds)
        owner, front = desktop.owner_obstruction_inventory(child.pid, window)
        helper.SHARED.exposed_owner_target(child.pid, owner, value, front)
        return value

    def native_window(handle):
        matches = [w for w in desktop.windows(child.pid) if w["xid"] == handle]
        require(len(matches) == 1, "Expected exact owned native client")
        return matches[0]

    def input_action(name, count, callback):
        nonlocal events, input_seconds
        remaining()
        require(events + count + 4 <= 64 and input_seconds < 15, "Original native action/time budget exhausted")
        events += count
        action = dict(name=name, eventAttempts=count, elapsedSeconds=time.monotonic()-started, beforeSequence=sequence)
        record["actions"].append(action)
        before = time.monotonic()
        try:
            with helper.protocol_timeout(min(3, remaining(), 15-input_seconds)):
                callback()
            action["completed"] = True
        finally:
            input_seconds += time.monotonic()-before
            action["totalInputSeconds"] = input_seconds

    def click(surface, bounds, label):
        focus(state["dialog"]["surface"]["nativeHandle"] if state["dialog"] else state["owner"]["nativeHandle"])
        value = target(native_window(surface["nativeHandle"]), bounds)
        input_action(label,3,lambda: desktop.pointer(child.pid,value,"left"))

    def key(label):
        focus(state["dialog"]["surface"]["nativeHandle"])
        input_action(label,2,lambda: desktop.key(child.pid,0x1B))

    def capture(label):
        remaining()
        surface = state["dialog"]["surface"] if state["dialog"] else state["owner"]
        observe_native()
        handle = surface["nativeHandle"]
        path = output / (label+".png")
        require(not path.exists(), "Never replace an original capture")
        result = subprocess.run(["import","-window",str(handle),str(path)],env=env,capture_output=True,timeout=min(5,remaining()))
        require(result.returncode == 0 and path.is_file() and path.stat().st_size <= 8*1024*1024,"Owned client capture failed")
        data=path.read_bytes();require(data[:8]==b"\x89PNG\r\n\x1a\n","Original PNG required")
        a=rect(surface["client"])
        require(struct.unpack(">II",data[16:24]) == (a[2]-a[0],a[3]-a[1]),"Original captured client extent changed")
        validate_native(state,desktop.windows(child.pid))
        native_contract = None
        if state["dialog"]:
            hints = list(desktop.property(desktop.window(handle), "_MOTIF_WM_HINTS", desktop.atoms["_MOTIF_WM_HINTS"], 32, True))
            extents = list(desktop.property(desktop.window(handle), "_NET_FRAME_EXTENTS", desktop.atom_types.CARDINAL, 32, True))
            require(len(hints)==5 and hints[0]&3==3 and not hints[1]&(1|8|16) and hints[2]==0,
                    "Actual source caption native chrome contract changed")
            require(bool(hints[1]&32) is state["dialog"]["closeEnabled"] and extents==[0,0,0,0],
                    "Actual native Close policy or borderless extents disagree")
            native_contract = dict(motif=hints,frameExtents=extents)
        record["captures"].append(dict(name=path.name,sha256=digest(path),window=handle,sequence=sequence,
                                      source=state,nativeContract=native_contract))

    def drag():
        dialog=state["dialog"];handle=dialog["surface"]["nativeHandle"]
        focus(handle)
        bounds=target(native_window(handle),dialog["dragTarget"])
        x,y=bounds["x"]+bounds["width"]//2,bounds["y"]+bounds["height"]//2
        require(0 <= x+48 < desktop.screen.width_in_pixels and 0 <= y+32 < desktop.screen.height_in_pixels,"Drag exits original screen")
        def gesture():
            pressed=False
            try:
                desktop.held()
                desktop.display.xtest_fake_input(desktop.X.MotionNotify,root=desktop.root,x=x,y=y);desktop.sync()
                p=desktop.held()
                require((p.root_x,p.root_y)==(x,y) and desktop.point_owner(x,y,True)==child.pid,"Original drag point changed")
                focus(handle)
                desktop.display.xtest_fake_input(desktop.X.ButtonPress,1);pressed=True;desktop.sync()
                time.sleep(.1)  # One fixed hold, never a retry or another input sequence.
                require(desktop.pid(desktop.window(handle))==child.pid,"Owned drag window retired")
                desktop.display.xtest_fake_input(desktop.X.MotionNotify,root=desktop.root,x=x+48,y=y+32);desktop.sync()
                time.sleep(.1)
                desktop.display.xtest_fake_input(desktop.X.ButtonRelease,1);pressed=False;desktop.sync()
            finally:
                if pressed:
                    record["dragCleanupReleaseAttempted"]=True
                    desktop.display.xtest_fake_input(desktop.X.ButtonRelease,1);desktop.sync()
        input_action("caption-drag-48x32",4,gesture)

    try:
        for name in old_display:
            require(name in env, "Owned X11 display/auth missing")
            os.environ[name]=env[name]
        with (output/"application.log").open("x") as log:
            child=subprocess.Popen(["dotnet",str(app),str(app_output),run,renderer],cwd=app.parent,env=env,
                                   stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            record["pid"]=child.pid
            desktop=helper.X11Desktop();desktop.deadline=deadline
            for name in ("_MOTIF_WM_HINTS", "_NET_FRAME_EXTENTS"):
                desktop.atoms[name]=desktop.display.intern_atom(name,only_if_exists=True)
                require(desktop.atoms[name],"Original native caption property unavailable: "+name)
            observe(lambda s:s["phase"]=="owner" and s["index"]==-1)
            require(state["ownerInput"]==dict(clicks=0,down=0,up=0,keys=0),"Original owner positive-control baseline changed")
            capture("initial-owner")
            for index, (buttons,response,actions) in enumerate(SCENARIOS):
                focus(state["owner"]["nativeHandle"])
                click(state["owner"],state["ownerTarget"],"owner-positive-"+str(index))
                observe(lambda s:s["phase"]=="modal" and s["index"]==index)
                require(state["ownerInput"]["clicks"]==index+1,"Original owner positive click absent")
                baseline=state["ownerInput"].copy();dialog_id=state["dialog"]["surface"]["nativeHandle"]
                capture(str(index)+"-original-caption")
                owner=native_window(state["owner"]["nativeHandle"])
                blocked=interior(state["ownerTarget"])
                input_action("blocked-owner-"+str(index),3,lambda: desktop.blocked_owner_pointer(child.pid,owner,blocked))
                observe(lambda s:s["phase"]=="modal" and s["index"]==index,baseline)
                if index==0:
                    before=rect(state["dialog"]["surface"]["client"])
                    drag()
                    expected=[before[0]+48,before[1]+32,before[2]+48,before[3]+32]
                    observe(lambda s:s["phase"]=="modal" and rect(s["dialog"]["surface"]["client"])==expected,baseline)
                    capture("0-after-drag")
                if index in (0,3):click(state["dialog"]["surface"],state["dialog"]["closeTarget"],"enabled-close")
                elif index==1:key("escape-cancel")
                else:
                    click(state["dialog"]["surface"],state["dialog"]["closeTarget"],"disabled-close")
                    observe(lambda s:s["phase"]=="modal" and s["index"]==2,baseline)
                    key("escape-rejected")
                    observe(lambda s:s["phase"]=="modal" and s["index"]==2,baseline)
                    choices=[v for v in state["dialog"]["responseTargets"] if v["content"]=="_No"]
                    require(len(choices)==1,"Actual No response is unavailable")
                    click(state["dialog"]["surface"],choices[0]["bounds"],"no-response")
                observe(lambda s:s["phase"]=="owner" and s["index"]==index,baseline)
                require(not any(w["xid"]==dialog_id for w in desktop.windows(child.pid)),"Original visible dialog did not retire")
                focus(state["owner"]["nativeHandle"])
                capture(str(index)+"-restored-owner")
            click(state["owner"],state["ownerTarget"],"final-owner-positive")
            child.wait(timeout=max(.001,deadline-time.monotonic()))
            require(time.monotonic() <= deadline,"Original absolute process deadline exhausted at exit")
            completion=read_source(app_output/"complete.json")
            require(child.returncode==0,"Original source process exit failed")
            validate_completion(completion,child.pid,run)
            require(inventory()==preparation["outputs"],"Prepared application outputs changed during execution")
            record.update(completed=True,passed=True,exitCode=child.returncode,sourceCompletion=completion,outputsUnchanged=True)
    except BaseException as error:
        record["error"]=type(error).__name__+": "+str(error)
        if child is not None:record["exitCodeAtFailure"]=child.poll()
        raise
    finally:
        cleanup_errors=[]
        try:
            if child is not None and child.poll() is None:
                os.killpg(child.pid,signal.SIGKILL);child.wait(timeout=5);record["forcedCleanup"]=True
        except BaseException as error:cleanup_errors.append("owned process: "+str(error))
        try:
            if desktop is not None:desktop.close()
        except BaseException as error:cleanup_errors.append("owned protocol connection: "+str(error))
        for name,value in old_display.items():
            if value is None:os.environ.pop(name,None)
            else:os.environ[name]=value
        record.update(elapsedSeconds=time.monotonic()-started,nativeEventAttempts=events,totalInputSeconds=input_seconds,
                      cleanupErrors=cleanup_errors)
        if cleanup_errors:record.update(completed=False,passed=False)
        (output/"result.json").write_text(json.dumps(record,indent=2)+"\n")
    return record
