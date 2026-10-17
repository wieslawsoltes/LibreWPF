#!/usr/bin/env python3
"""Run the original approved bounded pair only on a disposable hosted Windows desktop."""
import argparse
import json
import os
from pathlib import Path
import runpy
import sys
import time

from prepare import inventory, require, save, sha, verify_sources, verify_staged_sources


def verify_compiler_captures(evidence, payload, expected):
    if expected is None:
        return
    require(expected == "Dxc", "Unsupported explicit modal compiler")
    library = Path(payload["directory"]) / "dxcompiler.dll"
    # Every original capture remains required. Read the actual source host's
    # device selection; a runtimeconfig request alone is not a loaded compiler.
    for phase in ("01-owner", "02-message", "03-dialog", "04-popup", "05-context", "06-combo", "07-tooltip"):
        capture = json.loads((evidence / "portable" / (phase + ".json")).read_text(encoding="utf-8"))
        windows = capture["source"]["windows"]
        require(windows, "No original windows in modal capture")
        for window in windows:
            value = window.get("compiler")
            require(isinstance(value, dict) and value.get("selected") == expected and
                    Path(value.get("library") or "").resolve(strict=True) == library.resolve(strict=True),
                    "Actual modal window did not select the packaged DXC compiler")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    require(os.name == "nt" and os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
            and os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF",
            "Native input is restricted to this repository's disposable hosted Windows runner")
    root = args.output.resolve(strict=True)
    preparation = json.loads((root / "preparation.json").read_text(encoding="utf-8"))
    manifest = json.loads((root / "public-inputs.json").read_text(encoding="utf-8"))
    require(preparation["prepared"] and not preparation["qualified"], "Prepared immutable inputs required")
    evidence = root / "runtime"
    require(not evidence.exists() and not (root / "controller-result.json").exists(), "Original run evidence already exists")

    def verify():
        require(sha(root / "public-inputs.json") == preparation["manifestSha256"], "Prepared package/source manifest changed")
        candidate = preparation["candidate"]
        verify_staged_sources(root, manifest, preparation["selectedSourceHashes"], candidate["sdk"] if candidate else None,
                              candidate["sdkSelection"].get("compiler") if candidate else None)
        for name, digest in preparation["hostedAutomation"].items():
            require(sha(root / "consumer" / name) == digest, "Prepared hosted controller changed")
        verify_sources(Path(preparation["helper"]), manifest["formsHelperSource"], manifest["helpers"])
        for payload in preparation["payloads"].values():
            require(inventory(Path(payload["directory"])) == payload["files"], "Prepared app payload changed")

    verify()
    command = [preparation["driver"], "--platform", "windows",
               "--reference-app", preparation["payloads"]["microsoft"]["app"],
               "--portable-app", preparation["payloads"]["portable"]["app"],
               "--forms-helper-checkout", preparation["helper"],
               "--forms-helper-commit", manifest["formsHelperSource"], "--output", str(evidence),
               "--hosted-windows-caption-activation", "--hosted-windows-shell-escape",
               "--hosted-windows-retirement-readiness", "--hosted-windows-start-activation"]
    source_changes = sorted(name for name, digest in manifest["source"].items()
                            if preparation["selectedSourceHashes"][name] != digest)
    receipt = dict(qualified=False, completed=False, argv=command, deadlineSecondsPerProcess=60,
                   maximumActionsPerProcess=64, scenarioSourcesUnchanged=True,
                   applicationFilesUnchanged=not source_changes, changedSourceFiles=source_changes,
                   observerRevision=preparation["observerRevision"],
                   packageSource=preparation["packageSource"],
                   candidate=preparation["candidate"],
                   compilerSelection=(preparation["candidate"]["sdkSelection"].get("compiler")
                                      if preparation["candidate"] else None),
                   nativeInputTrace=os.environ.get("PROGPU_WPF_TRACE_INPUT") == "1",
                   hostedAutomation=preparation["hostedAutomation"], automationCommit=preparation["automationCommit"],
                   startupInput="At most two verified shell Escapes at reference startup across the entire pair, with one verified Start activation after Search yields to Progman; then one PID/HTCAPTION-verified click per process; all inside original deadline/action budgets",
                   shellInputAuthorization="Owner approvals 2026-10-16/17: bounded hosted Search/Start Escape plus one verified Start activation after Search yields to the actual shell")
    save(root / "launch.json", receipt)
    previous = sys.argv
    started = time.monotonic()
    try:
        sys.argv = command
        runpy.run_path(command[0], run_name="__main__")
        for name in ["microsoft", "portable"]:
            result = json.loads((evidence / name / "result.json").read_text(encoding="utf-8"))
            require(result["completed"] and result["exitCode"] == 0 and not result["cleanupErrors"],
                    "Original paired workload did not complete")
        verify_compiler_captures(evidence, preparation["payloads"]["portable"], receipt["compilerSelection"])
        receipt["completed"] = True
    except BaseException as error:
        receipt["error"] = repr(error)
        raise
    finally:
        sys.argv = previous
        receipt["seconds"] = time.monotonic() - started
        try:
            verify()
            receipt["payloadsUnchanged"] = True
        finally:
            save(root / "controller-result.json", receipt)


if __name__ == "__main__":
    main()
