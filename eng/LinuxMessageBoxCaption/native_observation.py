"""Retain the unchanged native inventory before the exact source/native guard."""
import json
from contract import validate_native

def validate_recorded_native(state, windows, path):
    # This does not query, move, resize or fit a native window. The caller passes
    # the same single inventory which the guard checks, including on failure.
    record = dict(pid=state["pid"], run=state["run"], sourceSequence=state["sequence"],
                  source=state, nativeWindows=windows)
    with path.open("x") as stream:
        json.dump(record, stream, indent=2, allow_nan=False)
        stream.write("\n")
    return validate_native(state, windows)
