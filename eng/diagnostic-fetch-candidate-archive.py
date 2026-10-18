#!/usr/bin/env python3
"""Download only the artifact named by retained, independently reviewed metadata."""
import hashlib
import json
import os
from pathlib import Path
import subprocess


def main():
    assert os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF'
    assert os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
    producer = Path('eng/diagnostic-producer')
    metadata = json.loads((producer / 'artifact.json').read_text())
    artifact_id = metadata['id']
    assert type(artifact_id) is int and artifact_id > 0 and metadata['expired'] is False
    assert 0 < metadata['size_in_bytes'] <= 2 * 1024**3
    destination = producer / 'qualified-packages.zip'
    with destination.open('xb') as stream:
        subprocess.run(['gh', 'api', f'repos/wieslawsoltes/LibreWPF/actions/artifacts/{artifact_id}/zip'],
                       stdout=stream, check=True, timeout=300)
    with destination.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    assert destination.stat().st_size == metadata['size_in_bytes']
    assert digest == metadata['digest'].removeprefix('sha256:')
    print('Original artifact digest verified; no CI status or logs queried.')


if __name__ == '__main__':
    main()
