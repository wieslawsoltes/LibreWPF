"""Pure preflight for the pending caption-input run. This module sends no input."""
import math

SCENARIOS = (
    ("OK", "OK", ("owner-blocked-click", "caption-drag", "close-click")),
    ("OKCancel", "Cancel", ("owner-blocked-click", "escape")),
    ("YesNo", "No", ("owner-blocked-click", "disabled-close-click", "escape-rejected", "no-click")),
    ("YesNoCancel", "Cancel", ("owner-blocked-click", "close-click")),
)

def require(condition, message):
    if not condition:
        raise ValueError(message)

def rect(value):
    require(isinstance(value, dict) and set(value) == {"left", "top", "right", "bottom"}, "Exact endpoint rectangle required")
    values = [value[name] for name in ("left", "top", "right", "bottom")]
    require(all(type(x) in (int, float) and math.isfinite(x) for x in values), "Finite original endpoint coordinates required")
    require(values[2] > values[0] and values[3] > values[1], "Positive target required")
    return values

def interior(value):
    left, top, right, bottom = rect(value)
    # Conservative integer input interior; never modifies source/native frame assertions.
    x, y = math.ceil(left), math.ceil(top)
    end_x, end_y = math.floor(right), math.floor(bottom)
    require(end_x > x and end_y > y, "No proven integer interior")
    return dict(x=x, y=y, width=end_x-x, height=end_y-y)

def inside(outer, inner):
    a, b = rect(outer), rect(inner)
    return a[0] <= b[0] and a[1] <= b[1] and a[2] >= b[2] and a[3] >= b[3]

def validate_snapshot(state, pid, run, renderer, previous_sequence=0):
    require(state["schema"] == "caption-native-input-observer-v1", "Unknown source observer")
    require(type(state["pid"]) is int and state["pid"] == pid and state["run"] == run, "Foreign source process/run")
    require(type(state["sequence"]) is int and state["sequence"] > previous_sequence, "Stale source observation")
    require(type(state["elapsedSeconds"]) in (int, float) and 0 <= state["elapsedSeconds"] <= 55, "Original source deadline")
    require(state["phase"] in ("owner", "modal") and type(state["index"]) is int and -1 <= state["index"] <= 3,
            "Unknown original source phase")
    require(set(state["ownerInput"]) == {"clicks", "down", "up", "keys"} and
            all(type(x) is int and 0 <= x <= 64 for x in state["ownerInput"].values()), "Invalid owner input counters")
    surfaces = [state["owner"]]
    dialog = state["dialog"]
    if state["phase"] == "modal":
        require(state["index"] >= 0 and isinstance(dialog, dict), "Missing original modal child")
        require(dialog["buttons"] == SCENARIOS[state["index"]][0], "Changed response family")
        require(dialog["closeEnabled"] is (state["index"] != 2), "Changed Close response policy")
        require(state["owner"]["inputAllowed"] is False and dialog["surface"]["inputAllowed"] is True,
                "Modal source input admission is not exclusive")
        surfaces.append(dialog["surface"])
        for target in [dialog["caption"], dialog["dragTarget"], dialog["closeTarget"]]:
            require(inside(dialog["surface"]["client"], target), "Caption input target leaves actual source client")
        for target in dialog["responseTargets"]:
            require(inside(dialog["surface"]["client"], target["bounds"]), "Response target leaves source client")
    else:
        require(dialog is None and state["owner"]["inputAllowed"] is True and state["owner"]["sourceEnabled"] is True,
                "Restored owner is not admitted")
    ids = set()
    for surface in surfaces:
        require(type(surface["nativeHandle"]) is int and surface["nativeHandle"] > 0 and surface["nativeHandle"] not in ids,
                "Missing/aliased native window")
        ids.add(surface["nativeHandle"])
        require(surface["renderer"] == renderer and surface["presentedFrames"] > 0, "Unproven renderer/frame")
        require(all(type(surface[k]) in (int, float) and math.isfinite(surface[k]) and surface[k] > 0
                    for k in ("dpiX", "dpiY", "clientWidth", "clientHeight")), "Invalid actual source geometry")
        rect(surface["client"])
    require(inside(state["owner"]["client"], state["ownerTarget"]), "Owner guard leaves source client")
    return state

def validate_native(state, windows):
    surfaces = [state["owner"]] + ([state["dialog"]["surface"]] if state["dialog"] else [])
    for source in surfaces:
        matching = [w for w in windows if w["xid"] == source["nativeHandle"] and w["pid"] == state["pid"]]
        require(len(matching) == 1, "Actual native source identity is absent/ambiguous")
        native = matching[0]
        require(native["title"] == source["title"], "Native title identity changed")
        if state["dialog"] and source is state["dialog"]["surface"]:
            require(native.get("transientFor") == state["owner"]["nativeHandle"], "Actual native transient owner changed")
        a = rect(source["client"]); c = native["client"]
        require([c["x"], c["y"], c["x"] + c["width"], c["y"] + c["height"]] == a,
                "Original source/native client mismatch; no fitted bounds")
    return surfaces

def native_plan():
    # Five real owner clicks: initial positive control and one after every dismissal.
    events = 5 * 3 + 4 * 3 + 4 + 2 * 3 + 2 * 2 + 3 + 3
    require(events == 47 and events + 4 <= 64, "Input reserve no longer fits original ceiling")
    return dict(schema="caption-input-plan-v1", processSeconds=60, sourceSeconds=55,
                maximumNativeEvents=64, plannedNativeEvents=events, cleanupReserve=4,
                totalInputSeconds=15, dragPhysicalDelta=[48, 32], scenarios=SCENARIOS,
                executable=False, approvalRequired=True,
                restriction="No input is sent by this preflight. The application-specific Linux input extension must be approved before an executor is enabled.")

def validate_completion(value, pid, run):
    require(value["pid"] == pid and value["run"] == run and value["completed"] is True and value["ownerClicks"] == 5,
            "Original source completion identity failed")
    elapsed=value["elapsedSeconds"]
    require(type(elapsed) in (int,float) and math.isfinite(elapsed) and 0 <= elapsed <= 55,
            "Original source completion deadline failed")
    require([(c["buttons"],c["result"]) for c in value["cases"]] == [(a,b) for a,b,_ in SCENARIOS],
            "Original four responses changed")
