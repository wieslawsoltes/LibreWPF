#!/usr/bin/env python3
"""Run the original approved bounded pair only on a disposable hosted Windows desktop."""
import argparse
import json
import os
from pathlib import Path
import runpy
import sys
import time

from prepare import inventory, require, save, sha, verify_sources


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
        for name, digest in manifest["source"].items():
            require(sha(root / "consumer" / name) == digest, "Original application/driver changed")
        verify_sources(Path(preparation["helper"]), manifest["formsHelperSource"], manifest["helpers"])
        for payload in preparation["payloads"].values():
            require(inventory(Path(payload["directory"])) == payload["files"], "Prepared app payload changed")

    verify()
    command = [preparation["driver"], "--platform", "windows",
               "--reference-app", preparation["payloads"]["microsoft"]["app"],
               "--portable-app", preparation["payloads"]["portable"]["app"],
               "--forms-helper-checkout", preparation["helper"],
               "--forms-helper-commit", manifest["formsHelperSource"], "--output", str(evidence)]
    receipt = dict(qualified=False, completed=False, argv=command, deadlineSecondsPerProcess=60,
                   maximumActionsPerProcess=64, sourceFilesUnchanged=True)
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
