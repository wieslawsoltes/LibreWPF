"""Require the separately recorded human approval and the exact reviewed scripts."""
import hashlib
import json
from pathlib import Path

ARCHIVE_SHA = '72af407f78992bac58fae546849188c1e3cce46e797bbb014de9a72245ade98c'
SCRIPT_NAMES = ('authorization.py', 'owned_overview.py', 'run-input.py', 'desktop-input.py', 'slider-native-input.py')


def require_approval(directory):
    directory = Path(directory)
    record = json.loads((directory/'authorization.json').read_text())
    if record.get('approved') is not True or not record.get('humanReply'):
        raise PermissionError('Explicit approval for this hosted candidate is pending')
    assert record['candidateSha256'] == ARCHIVE_SHA
    assert record['scope'] == 'hosted-linux-slider-281-and-owned-overview-once'
    assert set(record['scripts']) == set(SCRIPT_NAMES)
    for name in SCRIPT_NAMES:
        assert hashlib.sha256((directory/name).read_bytes()).hexdigest() == record['scripts'][name], name
    assert record['releaseObservation']['approved'] is True
    assert record['releaseObservation']['maxSeconds'] == .250
    assert record['releaseObservation']['maxReads'] == 26
    assert record['releaseObservation']['additionalInputActions'] == 0
    return record
