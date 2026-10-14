#!/usr/bin/env python3
"""Fetch explicit successful producer inputs, then invoke the original preparer."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent
REPOSITORY = "wieslawsoltes/LibreWPF"
spec = importlib.util.spec_from_file_location("gallery_prepare", ROOT / "prepare.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)
BOOTSTRAP_JOBS = {
    "SDK bootstrap ownership (ubuntu-24.04)",
    "SDK bootstrap ownership (windows-2025)",
    "SDK bootstrap ownership (macos-15)",
}


def validate_ci_producer(build, docs, artifact, head, build_id, docs_id, upload_evidence=None):
    prepare.validate_producer(build, docs, artifact, head, build_id, docs_id, upload_evidence)
    prepare.require(BOOTSTRAP_JOBS.issubset({job.get("name") for job in build["jobs"]}),
                    "All three current SDK bootstrap jobs are required beside the original thirteen Build jobs")


def gh_json(*arguments):
    result = subprocess.run(["gh", *arguments], check=True, capture_output=True, timeout=120)
    prepare.require(len(result.stdout) <= 2 * 1024**2, "GitHub receipt exceeds bound")
    return json.loads(result.stdout)


def gh_job_log(job_id):
    prepare.require(type(job_id) is int and job_id > 0, "Explicit producer job identity required")
    # Current gh rejects ANSI-bearing logs even with redirected stdout. Keep the
    # original bytes in a pipe for evidence; never render them in the terminal.
    result = subprocess.run(["gh", "api", "--allow-escape-sequences",
                             f"repos/{REPOSITORY}/actions/jobs/{job_id}/logs"],
                            check=True, capture_output=True, timeout=120)
    prepare.require(0 < len(result.stdout) <= 1024 * 1024, "original upload log exceeds bound")
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--head", required=True)
    parser.add_argument("--build", required=True, type=int)
    parser.add_argument("--docs", required=True, type=int)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--dotnet", required=True, type=Path)
    args = parser.parse_args()
    prepare.require(os.name == "nt" and os.environ.get("GITHUB_ACTIONS") == "true"
                    and os.environ.get("RUNNER_ENVIRONMENT") == "github-hosted"
                    and os.environ.get("GITHUB_REPOSITORY") == REPOSITORY,
                    "This fetcher is restricted to the repository's disposable Windows CI runner")
    prepare.require(re.fullmatch(r"[0-9a-f]{40}", args.head) and args.build > 0 and args.docs > 0,
                    "Explicit producer head and run IDs required")
    checked_out = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    prepare.require(checked_out == args.head, "Acceptance source must match the exact qualified producer")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    inputs = output / "inputs"
    inputs.mkdir()

    def retain(name, data):
        path = inputs / name
        path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        return path

    fields = "databaseId,name,headSha,status,conclusion,jobs"
    build = gh_json("run", "view", str(args.build), "--repo", REPOSITORY, "--json", fields)
    docs = gh_json("run", "view", str(args.docs), "--repo", REPOSITORY, "--json", fields)
    build_path = retain("build.json", build)
    docs_path = retain("docs.json", docs)
    listing = gh_json("api", f"repos/{REPOSITORY}/actions/runs/{args.build}/artifacts?per_page=100")
    retain("artifact-list.json", listing)
    prepare.require(listing.get("total_count") == len(listing.get("artifacts", [])), "Incomplete artifact listing")
    selected = [a for a in listing["artifacts"] if a["name"] == "librewpf-ci-packages-" + args.head]
    prepare.require(len(selected) == 1, "One exact producer package archive required")
    artifact = gh_json("api", f"repos/{REPOSITORY}/actions/artifacts/{selected[0]['id']}")
    artifact_path = retain("artifact.json", artifact)
    producers = [j for j in build.get("jobs", []) if j.get("name") == "SDK package and no-source-change smoke"]
    prepare.require(len(producers) == 1, "one exact package producer job required")
    producer = producers[0]
    uploads = [s for s in producer.get("steps", []) if s.get("name") == "Upload CI package bundle"]
    upload_evidence = None
    upload_path = None
    if not (len(uploads) == 1 and prepare.succeeded(uploads[0])):
        job_id = producer.get("databaseId")
        prepare.require(type(job_id) is int and job_id > 0, "explicit producer job identity required")
        job = gh_json("api", f"repos/{REPOSITORY}/actions/jobs/{job_id}")
        retain("producer-job.json", job)
        raw_log = gh_job_log(job_id)
        (inputs / "producer-job.log").write_bytes(raw_log)
        upload_evidence = {"schema": "github-upload-log-v1", "job": job,
                           "log": raw_log.decode("utf-8-sig")}
        upload_path = retain("upload-evidence.json", upload_evidence)
    validate_ci_producer(build, docs, artifact, args.head, args.build, args.docs, upload_evidence)
    prepare.require(0 < artifact["size_in_bytes"] <= 2 * 1024**3, "Producer archive exceeds bound")
    archive = output / "qualified-packages.zip"
    with archive.open("xb") as stream:
        subprocess.run(["gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{artifact['id']}/zip"],
                       stdout=stream, check=True, timeout=300)
    prepare.require(archive.stat().st_size == artifact["size_in_bytes"]
                    and prepare.sha(archive) == artifact["digest"].removeprefix("sha256:"),
                    "Downloaded package archive differs from original producer digest")

    manifest = prepare.read_json(ROOT / "source-manifest.json")
    original = output / "original"
    original.mkdir()
    for entry in manifest["files"]:
        target = original / entry["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        url = f"https://raw.githubusercontent.com/{manifest['repository']}/{manifest['commit']}/" + urllib.parse.quote(entry["upstream"], safe="/")
        with urllib.request.urlopen(url, timeout=30) as response:
            data = response.read(entry["bytes"] + 1)
        prepare.require(len(data) == entry["bytes"], "Original source size mismatch")
        target.write_bytes(data)
    prepare.validate_sources(original)
    command = [sys.executable, str(ROOT / "prepare.py"),
               "--original-source", str(original), "--build-receipt", str(build_path),
               "--docs-receipt", str(docs_path), "--artifact-receipt", str(artifact_path),
               "--package-archive", str(archive), "--expected-head", args.head,
               "--expected-build", str(args.build), "--expected-docs", str(args.docs),
               "--architecture", "arm64", "--output", str(output / "consumer"),
               "--build-only", "--dotnet", str(args.dotnet.resolve())]
    if upload_path is not None:
        command.extend(["--upload-evidence", str(upload_path)])
    subprocess.run(command, check=True)


if __name__ == "__main__":
    main()
