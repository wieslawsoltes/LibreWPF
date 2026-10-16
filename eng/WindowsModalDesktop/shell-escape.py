"""Owner-approved Escape guard for the disposable hosted reference startup only."""
import ntpath
import time

IMAGES = {
    "Search": r"SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\SearchHost.exe",
    "Start": r"SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe",
}


def require(value, message):
    if not value:
        raise RuntimeError(message)


def dismiss(native, original_sender, claim_original_action, deadline, record, environment, *,
            authorized=False, clock=time.monotonic, sleep=time.sleep):
    require(authorized, "Bounded hosted shell input requires explicit owner approval")
    require(all(environment.get(k) == v for k, v in {
        "GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted",
        "RUNNER_OS": "Windows", "GITHUB_REPOSITORY": "wieslawsoltes/LibreWPF"}.items()),
        "Only the disposable hosted Windows desktop is admitted")
    context = native.context()
    require(context["desktop"] == "Default" and context["session"] > 0,
            "Ordinary interactive desktop required")
    record.update(context=context, escapes=[], deadlineUnchanged=deadline)
    names = set()
    setup_deadline = min(deadline - 3, clock() + 3)
    record["setupDeadline"] = setup_deadline
    for _ in range(3):
        require(clock() < setup_deadline, "Bounded shell dismissal deadline expired")
        hwnd = native.foreground()
        if not hwnd:
            return
        window = native.snapshot(hwnd)
        name = window["title"]
        if name not in IMAGES:
            record["remainingForeground"] = window
            return  # No input; the original caption guards still decide admission.
        require(name not in names and len(names) < 2, "Repeated shell foreground; no retry permitted")
        require(window["class"] == "Windows.UI.Core.CoreWindow" and window["visible"]
                and window["pid"] > 0 and window["session"] == context["session"]
                and ntpath.normcase(window["image"]) ==
                    ntpath.normcase(ntpath.join(context["windowsRoot"], IMAGES[name])),
                "Unverified native shell input target")
        require(native.context() == context and native.foreground() == hwnd
                and native.snapshot(hwnd) == window,
                "Shell input identity changed before Escape")
        names.add(name)
        item = dict(window=window, key=0x1B, originalDeadline=deadline)
        record["escapes"].append(item)
        claim_original_action("hosted-shell-escape", nativeProof=item)
        # The unchanged Forms helper independently verifies foreground PID,
        # rejects held input and releases any partial key sequence.
        original_sender.key(window["pid"], 0x1B)
        until = min(setup_deadline, clock() + .5)
        while native.foreground() == hwnd and clock() < until:
            sleep(.01)
        item["foregroundAfter"] = native.foreground()
        require(item["foregroundAfter"] != hwnd, "Shell Escape did not yield foreground")
    raise RuntimeError("Bounded shell dismissal count exceeded")
