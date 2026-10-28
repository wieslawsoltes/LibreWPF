#!/usr/bin/env python3
"""Download the reviewed Actions artifact on a disposable runner; no CI discovery."""
import hashlib
import json
import os
from pathlib import Path
import subprocess

root = Path("eng/CaptionQualifiedProducer")
artifact = json.loads((root / "artifact.json").read_text())
head = "4ab76a8a111a1b176d66bcd332f2d65e1c7dabca"
assert os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
assert os.environ.get("GITHUB_REPOSITORY") == "wieslawsoltes/LibreWPF"
assert artifact["name"] == "librewpf-ci-packages-" + head
assert artifact["workflow_run"]["head_sha"] == head
assert artifact["workflow_run"]["id"] == 40683771914 and not artifact["expired"]
assert type(artifact["id"]) is int and artifact["id"] > 0
assert 0 < artifact["size_in_bytes"] <= 2 * 1024**3
archive = root / "qualified-packages.zip"
assert not archive.exists() and not archive.is_symlink()
Path("artifacts").mkdir(exist_ok=True)
record = dict(artifact=artifact, complete=False)
try:
    with archive.open("xb") as output:
        result = subprocess.run(["gh", "api", "repos/wieslawsoltes/LibreWPF/actions/artifacts/"
                                 + str(artifact["id"]) + "/zip"], stdout=output, timeout=180)
    record["exitCode"] = result.returncode
    assert result.returncode == 0, "Original artifact download failed"
    record["bytes"] = archive.stat().st_size
    with archive.open("rb") as stream:
        record["sha256"] = hashlib.file_digest(stream, "sha256").hexdigest()
    assert record["bytes"] == artifact["size_in_bytes"], "Original artifact byte count differs"
    assert artifact["digest"] == "sha256:" + record["sha256"], "Original artifact hash differs"
    record["complete"] = True
except BaseException as error:
    record["error"] = type(error).__name__ + ": " + str(error)
    raise
finally:
    Path("artifacts/caption-artifact-download.json").write_text(json.dumps(record, indent=2) + "\n")

