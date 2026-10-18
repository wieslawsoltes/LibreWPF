#!/usr/bin/env python3
"""Disposable hosted environment and owned desktop inventory; no product application or input."""
import json, os, pathlib, platform, subprocess, time
root = pathlib.Path("artifacts")
root.mkdir(exist_ok=True)
target = root / "ubuntu26-desktop-environment.json"
assert not target.exists()
record = dict(qualified=False, applicationExecuted=False, inputSent=False,
              localVMsUsed=False, run=os.environ.get("GITHUB_RUN_ID"),
              sourceHead=os.environ.get("GITHUB_SHA"), architecture=platform.machine(),
              kernel=platform.release(), osRelease=pathlib.Path("/etc/os-release").read_text(),
              commands=[])
started = time.monotonic()
def invoke(args, timeout):
    item = dict(arguments=args, timeoutSeconds=timeout)
    record["commands"].append(item)
    result = subprocess.run(args, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, timeout=timeout)
    item.update(exitCode=result.returncode, output=result.stdout)
    if result.returncode: raise RuntimeError("Environment command failed: " + args[0])
try:
    assert os.environ.get("GITHUB_ACTIONS") == "true"
    assert os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
    assert os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF"
    assert platform.machine() == "aarch64"
    assert 'VERSION_ID="26.04"' in record["osRelease"]
    invoke(["sudo", "apt-get", "update"], 180)
    invoke(["sudo", "env", "DEBIAN_FRONTEND=noninteractive", "apt-get", "install", "-y",
            "--no-install-recommends", "gnome-shell", "mutter", "xwayland", "xvfb",
            "mesa-vulkan-drivers", "dbus-x11", "libgl1", "libegl1", "libgles2", "libx11-dev", "clang-18", "libclang-18-dev", "libx11-6", "libxcursor1", "libxinerama1", "libxi6", "libxrandr2", "libfontconfig1"], 420)
    invoke(["dpkg-query", "-W", "gnome-shell", "mutter", "xwayland", "xvfb", "mesa-vulkan-drivers"], 10)
    invoke(["gnome-shell", "--version"], 10)
    invoke(["gnome-shell", "--help"], 10)
    invoke(["mutter", "--version"], 10)
    invoke(["mutter", "--help"], 10)
    record["completed"] = True
except BaseException as error:
    record.update(completed=False, error=type(error).__name__ + ": " + str(error))
    raise
finally:
    record["elapsedSeconds"] = time.monotonic() - started
    target.write_text(json.dumps(record, indent=2) + "\n")
