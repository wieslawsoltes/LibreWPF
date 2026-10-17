#!/usr/bin/env python3
"""One input-free console-host startup diagnostic; never application acceptance."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import time
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--producer-tools', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if (os.name != 'nt' or os.environ.get('GITHUB_ACTIONS') != 'true' or
            os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted' or
            os.environ.get('GITHUB_REPOSITORY') != 'wieslawsoltes/LibreWPF'):
        raise RuntimeError('Requires the disposable hosted Windows runner')
    spec = importlib.util.spec_from_file_location('qualified_modal_prepare', args.producer_tools / 'prepare.py')
    prepare = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(prepare)
    root = args.prepared.resolve(strict=True)
    receipt = json.loads((root / 'preparation.json').read_text(encoding='utf-8'))
    manifest = json.loads((root / 'public-inputs.json').read_text(encoding='utf-8'))
    candidate = receipt['candidate']
    prepare.require(receipt['prepared'] and candidate['identity']['head'] ==
                    '5a4c4d310c17df9cd7da0d33b072b90e951de0cf' and
                    candidate['sdkSelection']['compiler'] == 'Dxc', 'Exact failed candidate required')
    prepare.verify_staged_sources(root, manifest, receipt['selectedSourceHashes'], candidate['sdk'], 'Dxc')
    payload = receipt['payloads']['portable']
    directory = Path(payload['directory'])
    prepare.require(prepare.inventory(directory) == payload['files'], 'Prepared payload changed')
    output = args.output.resolve()
    output.mkdir(exist_ok=False)
    app = Path(payload['app'])
    # Console dotnet exposes startup exceptions hidden by a WinExe apphost.
    # This deliberately different host never qualifies the original application.
    command = [receipt['dotnet'], str(app.with_suffix('.dll')), '--evidence-directory',
               str(output / 'app'), '--run-id', uuid.uuid4().hex]
    result = dict(diagnosticOnly=True, qualified=False, inputSent=False, timeoutSeconds=60,
                  producer=candidate['identity'], command=command, originalAppHost=str(app),
                  originalAppHostSha256=prepare.sha(app), consoleHostSha256=prepare.sha(Path(receipt['dotnet'])))
    process = None
    started = time.monotonic()
    try:
        with (output / 'stdout.txt').open('xb') as stdout, (output / 'stderr.txt').open('xb') as stderr:
            process = subprocess.Popen(command, stdout=stdout, stderr=stderr)
            result['pid'] = process.pid
            while process.poll() is None:
                if time.monotonic() - started >= 60:
                    result['timedOut'] = True
                    break
                if any((output / name).stat().st_size > 1024 * 1024 for name in ('stdout.txt', 'stderr.txt')):
                    result['outputLimitExceeded'] = True
                    break
                time.sleep(.05)
    finally:
        if process is not None:
            if process.poll() is None:
                process.kill()
            result['exitCode'] = process.wait(timeout=5)
        result['elapsedSeconds'] = time.monotonic() - started
        result['payloadUnchanged'] = prepare.inventory(directory) == payload['files']
        (output / 'diagnostic.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    prepare.require(result['payloadUnchanged'], 'Original payload changed during diagnosis')


if __name__ == '__main__':
    main()
