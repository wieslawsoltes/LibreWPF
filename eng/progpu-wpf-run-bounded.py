#!/usr/bin/env python3
"""Run a package validation command with a bounded process-group lifetime."""

import os
import signal
import subprocess
import sys


def main() -> int:
    if len(sys.argv) < 4:
        print("usage: progpu-wpf-run-bounded.py SECONDS LABEL COMMAND [ARGS...]", file=sys.stderr)
        return 2

    seconds = int(sys.argv[1])
    label = sys.argv[2]
    command = sys.argv[3:]
    if seconds <= 0:
        print("validation timeout must be positive", file=sys.stderr)
        return 2

    process = subprocess.Popen(command, start_new_session=os.name == "posix")
    try:
        return process.wait(timeout=seconds)
    except subprocess.TimeoutExpired:
        print(f"{label} exceeded {seconds} seconds.", file=sys.stderr, flush=True)
        if os.name == "posix":
            os.killpg(process.pid, signal.SIGTERM)
        else:
            process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            if os.name == "posix":
                os.killpg(process.pid, signal.SIGKILL)
            else:
                process.kill()
            process.wait()
        return 124


if __name__ == "__main__":
    raise SystemExit(main())
