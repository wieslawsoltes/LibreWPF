#!/usr/bin/env python3
"""Original Microsoft scenarios followed by bounded native startup diagnosis only."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import showcase_idle_crash as crash
import showcase_idle_debugger as debugger


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--producer-tools', type=Path, required=True)
    parser.add_argument('--debugger', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if (os.name != 'nt' or os.environ.get('GITHUB_ACTIONS') != 'true' or
            os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted' or
            os.environ.get('RUNNER_OS') != 'Windows' or
            os.environ.get('GITHUB_REPOSITORY') != 'wieslawsoltes/LibreWPF' or
            os.environ.get('PROGPU_WPF_TRACE_INPUT') != '0'):
        raise RuntimeError('Requires this repository\'s disposable hosted Windows runner, tracing disabled')
    root = args.prepared.resolve(strict=True)
    prepare = load('qualified_native_modal_prepare', args.producer_tools / 'prepare.py')
    prepared = json.loads((root / 'preparation.json').read_text(encoding='utf-8'))
    manifest = json.loads((root / 'public-inputs.json').read_text(encoding='utf-8'))
    candidate = prepared['candidate']
    prepare.require(prepared['prepared'] and not prepared['qualified'] and
                    candidate['identity']['head'] == '5a4c4d310c17df9cd7da0d33b072b90e951de0cf' and
                    candidate['sdkSelection']['compiler'] == 'Dxc', 'Exact failed qualified producer required')

    def verify():
        prepare.require(prepare.sha(root / 'public-inputs.json') == prepared['manifestSha256'], 'Manifest changed')
        prepare.verify_staged_sources(root, manifest, prepared['selectedSourceHashes'], candidate['sdk'], 'Dxc')
        for name, digest in prepared['hostedAutomation'].items():
            prepare.require(prepare.sha(root / 'consumer' / name) == digest, 'Original controller changed')
        prepare.verify_sources(Path(prepared['helper']), manifest['formsHelperSource'], manifest['helpers'])
        for payload in prepared['payloads'].values():
            prepare.require(prepare.inventory(Path(payload['directory'])) == payload['files'], 'Original payload changed')

    verify()
    output = args.output.resolve()
    output.mkdir(exist_ok=False)
    helper = args.debugger.resolve(strict=True)
    app = Path(prepared['payloads']['portable']['app'])
    expected = debugger.machine(app)
    prepare.require(debugger.machine(helper) == expected, 'Debugger architecture mismatch')
    original = app.read_bytes()
    prepare.require(original.count(b'ProGPU.Wpf.ModalInteractionApp.dll\0') == 1, 'Original apphost binding required')
    image = app.with_name('ModalStartup-' + uuid.uuid4().hex + '.exe')
    image_created = False
    result = dict(diagnosticOnly=True, qualified=False, producer=candidate['identity'],
                  originalAppHost=str(app), originalAppHostSha256=prepare.sha(app),
                  nativeDebuggerSha256=prepare.sha(helper), timeoutSeconds=60,
                  portableInputSent=False, referenceOriginalMaximumActions=64,
                  referenceOriginalDeadlineSeconds=60, uniqueImage=str(image))
    try:
        controller = load('qualified_native_modal_controller', prepared['driver'])
        options = SimpleNamespace(platform='windows', macos_helper=None,
                                  hosted_windows_caption_activation=True, hosted_windows_shell_escape=True)
        module, shared = controller.load_helper(Path(prepared['helper']), manifest['formsHelperSource'])
        controller.run_one(options, module, shared, Path(prepared['payloads']['microsoft']['app']),
                           output / 'microsoft', False)
        result['originalReferenceCompleted'] = True
        verify()
        with image.open('xb') as stream:
            image_created = True
            stream.write(original)
        prepare.require(prepare.sha(image) == result['originalAppHostSha256'], 'Copied apphost changed')
        # Raw dumps are never uploaded. Only a bounded dump correlated with this
        # owned child's native exception may enter the diagnostic artifact.
        with tempfile.TemporaryDirectory(prefix='modal-native-raw-') as temporary:
            raw = Path(temporary) / 'crash'
            raw.mkdir()
            # Preserve an earlier real access violation, including low addresses, if native
            # handling later turns it into a return-authentication fail-fast.
            # This existing debugger mode does not handle or suppress the fault.
            first_av = Path(temporary) / 'first-av'
            first_av.mkdir(exist_ok=False)
            event_path = output / 'native-event.json'
            command = [str(helper), str(image), str(raw), str(event_path),
                       '--modal-evidence-directory', str(output / 'app'),
                       '--modal-run-id', uuid.uuid4().hex,
                       '--first-chance-av-including-low', str(first_av / 'first-chance-av.dmp')]
            result['command'] = command
            tracing = {'PROGPU_WPF_TRACE_NATIVE_LOOP': '1', 'PROGPU_NATIVE_TRACE_SCENE_ENCODE': '1',
                       'PROGPU_NATIVE_TRACE_COMPUTE': '1'}
            result['diagnosticTrace'] = tracing
            with (output / 'stdout.txt').open('xb') as stdout, (output / 'stderr.txt').open('xb') as stderr:
                process = subprocess.run(command, stdout=stdout, stderr=stderr, timeout=70,
                                         env=os.environ | tracing)
            result['debuggerExitCode'] = process.returncode
            event = debugger.load_event(event_path, expected)
            result['nativeEvent'] = event
            if event['captured']:
                source = raw / f"{image.name}.{event['processId']}.dmp"
                validated = debugger.correlate_exception(source, event)
                crash.publish_dump(source, output / 'native-crash.dmp', event['processId'], validated)
                result['validatedCrash'] = validated
            result['firstChanceAccessViolation'] = debugger.collect_first_chance_av(
                first_av / 'first-chance-av.dmp', output, event, expected)
    except BaseException as error:
        result['error'] = repr(error)
        raise
    finally:
        try:
            if image_created:
                image.unlink()
            verify()
            result['payloadsUnchanged'] = True
        finally:
            (output / 'diagnostic.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
