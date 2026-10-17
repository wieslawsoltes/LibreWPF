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
            authorized=False, allow_background_start=False, clock=time.monotonic, sleep=time.sleep):
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
    record["startActivations"] = []
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
            # The extension is opt-in and admits only the exact observed failure:
            # Search was dismissed, actual shell owns foreground, visible Start
            # remains. No arbitrary foreground/application is activated.
            if not (allow_background_start and names == {"Search"}
                    and hwnd == context["shell"] and window["class"] == "Progman"
                    and window["visible"] and window["pid"] > 0
                    and window["session"] == context["session"]
                    and not record["startActivations"]):
                return
            windows = native.windows()
            require(len(windows) <= 128, "Native window inventory exceeds bound")
            starts = [item for item in windows if item["title"] == "Start"]
            require(len(starts) == 1, "Exactly one visible Start window is required")
            candidate = starts[0]
            require(candidate["class"] == "Windows.UI.Core.CoreWindow" and candidate["visible"]
                    and candidate["pid"] > 0 and candidate["session"] == context["session"]
                    and ntpath.normcase(candidate["image"]) ==
                        ntpath.normcase(ntpath.join(context["windowsRoot"], IMAGES["Start"])),
                    "Unverified background Start target")
            require(clock() < setup_deadline and native.context() == context
                    and native.foreground() == hwnd and native.snapshot(hwnd) == window
                    and native.snapshot(candidate["hwnd"]) == candidate,
                    "Start activation identity or deadline changed")
            activation = dict(window=candidate, foregroundBefore=window)
            record["startActivations"].append(activation)
            claim_original_action("hosted-start-activation", nativeProof=activation)
            activation["accepted"] = native.activate(candidate["hwnd"])
            require(activation["accepted"], "Start activation rejected; no retry permitted")
            # An accepted cross-thread request may briefly leave foreground zero.
            # Observe its completion once inside the existing setup budget. This
            # never repeats activation, changes policy or sends input while pending.
            completion_deadline = min(setup_deadline, clock() + 1)
            activation["completionDeadline"] = completion_deadline
            activation["completionObservations"] = []
            while True:
                foreground = native.foreground()
                observation = dict(foreground=foreground, observedAt=clock(),
                                   context=native.context(), window=native.snapshot(candidate["hwnd"]))
                activation["completionObservations"].append(observation)
                require(len(activation["completionObservations"]) <= 101
                        and observation["observedAt"] < completion_deadline
                        and observation["context"] == context and observation["window"] == candidate,
                        "Start activation completion identity or deadline changed; no retry permitted")
                if foreground == candidate["hwnd"]:
                    break
                require(foreground == 0 or foreground == hwnd and native.snapshot(hwnd) == window,
                        "Start activation yielded to an unexpected foreground; no retry permitted")
                sleep(.01)
            hwnd, window, name = candidate["hwnd"], candidate, "Start"
        require(name not in names and len(names) < 2, "Repeated shell foreground; no retry permitted")
        require(window["class"] == "Windows.UI.Core.CoreWindow" and window["visible"]
                and window["pid"] > 0 and window["session"] == context["session"]
                and ntpath.normcase(window["image"]) ==
                    ntpath.normcase(ntpath.join(context["windowsRoot"], IMAGES[name])),
                "Unverified native shell input target")
        require(clock() < setup_deadline and native.context() == context
                and native.foreground() == hwnd and native.snapshot(hwnd) == window,
                "Shell input identity changed before Escape")
        names.add(name)
        item = dict(window=window, key=0x1B, originalDeadline=deadline)
        record["escapes"].append(item)
        observe_start_cloak = name == "Start" and allow_background_start and bool(record["startActivations"])
        if observe_start_cloak:
            item["cloakBefore"] = native.cloak(hwnd)
            require(item["cloakBefore"] == dict(hresult=0, flags=0),
                    "Activated Start must be proven uncloaked before Escape")
            require(clock() < setup_deadline and native.context() == context
                    and native.foreground() == hwnd and native.snapshot(hwnd) == window,
                    "Start identity or deadline changed during cloak preflight")
        claim_original_action("hosted-shell-escape", nativeProof=item)
        # The unchanged Forms helper independently verifies foreground PID,
        # rejects held input and releases any partial key sequence.
        original_sender.key(window["pid"], 0x1B)
        until = min(setup_deadline, clock() + .5)
        cloaked_observations = 0
        while native.foreground() == hwnd and clock() < until:
            if observe_start_cloak:
                observation = dict(observedAt=clock(), cloak=native.cloak(hwnd),
                                   window=native.snapshot(hwnd), context=native.context(),
                                   foreground=native.foreground())
                item.setdefault("cloakAfter", []).append(observation)
                require(observation["window"] == window and observation["context"] == context,
                        "Start identity changed after Escape")
                require(observation["cloak"]["hresult"] == 0 and observation["cloak"]["flags"] in (0, 2),
                        "Start shell-cloak completion is unavailable or unproven")
                cloaked_observations = (cloaked_observations + 1
                    if observation["cloak"]["flags"] == 2 and observation["foreground"] == hwnd else 0)
                if cloaked_observations == 2:
                    require(clock() < until and native.foreground() == hwnd,
                            "Start cloak completion exceeded original observation deadline")
                    item["foregroundAfter"] = hwnd
                    item["completion"] = "same Start hidden by shell in two native observations"
                    return
            sleep(.01)
        item["foregroundAfter"] = native.foreground()
        require(item["foregroundAfter"] != hwnd, "Shell Escape did not yield foreground")
    raise RuntimeError("Bounded shell dismissal count exceeded")
