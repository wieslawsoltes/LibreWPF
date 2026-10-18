"""Download the exact reviewed compilation artifact, without querying CI."""
from pathlib import Path
import hashlib
import os
import subprocess

assert os.environ.get('RUNNER_ENVIRONMENT') == 'github-hosted'
assert os.environ.get('GITHUB_REPOSITORY') == 'wieslawsoltes/LibreWPF'
path = Path('eng/diagnostic-slider-candidate.zip')
with path.open('xb') as stream:
    subprocess.run(['gh', 'api', 'repos/wieslawsoltes/LibreWPF/actions/artifacts/12126438636/zip'],
                   stdout=stream, check=True, timeout=300)
assert path.stat().st_size == 71223843
assert hashlib.sha256(path.read_bytes()).hexdigest() == '72af407f78992bac58fae546849188c1e3cce46e797bbb014de9a72245ade98c'
print('Exact original built Slider artifact verified.')
