#!/usr/bin/env python3
"""Observe child exits around the exact qualified modal pair; never acceptance."""
import argparse
from contextlib import contextmanager
import datetime
import importlib.util
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys


@contextmanager
def observe_children(applications, records):
    original = subprocess.Popen

    def launch(*args, **kwargs):
        command = args[0] if args else kwargs.get('args')
        process = original(*args, **kwargs)
        if isinstance(command, (list, tuple)) and command and str(command[0]) in applications:
            records.append(dict(app=str(command[0]), pid=process.pid, process=process,
                                start=datetime.datetime.now(datetime.timezone.utc).isoformat()))
        return process

    subprocess.Popen = launch
    try:
        yield
    finally:
        subprocess.Popen = original


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
    root = args.prepared.resolve(strict=True)
    tools = args.producer_tools.resolve(strict=True)
    preparation = json.loads((root / 'preparation.json').read_text(encoding='utf-8'))
    if (preparation['candidate']['identity']['head'] != '5a4c4d310c17df9cd7da0d33b072b90e951de0cf' or
            preparation['candidate']['sdkSelection']['compiler'] != 'Dxc' or
            os.environ.get('PROGPU_WPF_TRACE_INPUT') != '0'):
        raise RuntimeError('Exact failed qualified DXC pair with tracing disabled required')
    applications = {value['app'] for value in preparation['payloads'].values()}
    if len(applications) != 2:
        raise RuntimeError('Original Microsoft and portable application identities required')
    output = args.output.resolve()
    output.mkdir(exist_ok=False)
    records = []
    result = dict(diagnosticOnly=True, qualified=False, producer=preparation['candidate']['identity'],
                  originalDeadlineSecondsPerProcess=60, originalMaximumActionsPerProcess=64,
                  controllerUnchanged=True, processObservationOnly=True)
    old_argv, old_path = sys.argv, sys.path[:]
    try:
        sys.path.insert(0, str(tools))
        sys.argv = [str(tools / 'run.py'), '--output', str(root)]
        with observe_children(applications, records):
            runpy.run_path(str(tools / 'run.py'), run_name='__main__')
        result['originalControllerCompleted'] = True
    except BaseException as error:
        result['originalControllerError'] = repr(error)
        raise
    finally:
        sys.argv, sys.path[:] = old_argv, old_path
        observed = []
        for item in records:
            process = item['process']
            observed.append({key: value for key, value in item.items() if key != 'process'} |
                            {'exitCode': process.poll()})
        result['children'] = observed
        (output / 'diagnostic.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
        spec = importlib.util.spec_from_file_location('startup_event_reader', Path(__file__).with_name('diagnose-startup.py'))
        startup = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(startup)
        for index, item in enumerate(observed):
            directory = output / str(index)
            directory.mkdir()
            startup.collect_events(directory, item['start'], Path(item['app']).name)


if __name__ == '__main__':
    main()
