#!/usr/bin/env python3
"""Require the original application's outcomes, not diagnostic workflow success."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

SOURCES = {
    'Program.cs': '41311b4b6498d8d5f8f47bfbd8283a5fccd3b5799c65336e90386a6fc9de23b7',
    'GlyphComputeReadback.cs': 'd29fac45e01933379623095461aa890a2999f68c62c0b8f4333e96d25aa55167',
    'Fonts/Inter-Regular.ttf': '40d692fce188e4471e2b3cba937be967878f631ad3ebbbdcd587687c7ebe0c82',
    'Fonts/LICENSE.txt': '262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a',
}


def require(value, message):
    if not value:
        raise ValueError(message)


def verify(read):
    def receipt(name):
        return json.loads(read('ubuntu26-' + name + '.json'))

    preparation = receipt('multi-window-preparation')
    require(preparation.get('compiled') is True and preparation.get('buildExitCode') == 0,
            'Original application did not compile')
    require(preparation.get('applicationExecuted') is False and preparation.get('inputSent') is False,
            'Preparation executed an application or sent input')
    require(preparation.get('sources') == SOURCES, 'Original source inventory changed')
    outputs = preparation.get('outputs', {})
    require(outputs and all(re.fullmatch('[0-9a-f]{64}', v) for v in outputs.values()),
            'Complete compiled output hashes required')
    results = {}
    for case in ('headless-x11', 'mixed-wayland-x11'):
        result = receipt('multi-window-' + case)
        require(result.get('case') == case and result.get('inputSent') is False,
                case + ': changed case or input policy')
        require(result.get('command') == ['dotnet', preparation['application'], '--backend', 'gl'],
                case + ': original forced-GL command changed')
        require(result.get('phaseTimeoutSeconds') == 120 and result.get('outerTimeoutSeconds') == 600,
                case + ': original deadlines changed')
        require(result.get('outputsBefore') == outputs and result.get('outputsUnchanged') is True,
                case + ': compiled outputs changed')
        log = read('ubuntu26-multi-window-' + case + '.application.log')
        require(hashlib.sha256(log).hexdigest() == result.get('applicationLogSha256'),
                case + ': application log changed')
        require(result.get('passed') is True and result.get('exitCode') == 0
                and result.get('reportedSuccess') is True and not result.get('outerTimeout')
                and b'ProGPU WPF multi-window smoke succeeded.' in log,
                case + ': original application failed')
        results[case] = result
    session = receipt('desktop-session')
    require(session.get('completed') is True and session.get('applicationExecuted') is True
            and session.get('applicationPassed') is True and session.get('inputSent') is False,
            'Owned desktop did not complete the original application')
    require(not session.get('error') and not session.get('compositorForcedCleanup')
            and session.get('compositorExitCode') == 0,
            'Owned compositor did not exit normally')
    require(session.get('waylandSocketExists') is True, 'Owned Wayland socket missing')
    descendants = session.get('ownedDescendants', {})
    xwaylands = [args for args in descendants.values() if args and Path(args[0]).name == 'Xwayland']
    require(len(xwaylands) == 1, 'One actual owned Xwayland required')
    args = xwaylands[0]
    environment = results['mixed-wayland-x11']['environment']
    require(len(args) > 1 and re.fullmatch(r':\d+', args[1]) and '-auth' in args,
            'Owned Xwayland identity missing')
    require(environment.get('DISPLAY') == args[1]
            and environment.get('XAUTHORITY') == args[args.index('-auth') + 1]
            and environment.get('WAYLAND_DISPLAY') == 'progpu-owned-wayland'
            and environment.get('XDG_SESSION_TYPE') == 'wayland',
            'Application did not use the actual owned mixed session')
    return {case: result['elapsedSeconds'] for case, result in results.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence', type=Path)
    args = parser.parse_args()
    if args.evidence.is_dir():
        result = verify(lambda name: (args.evidence / name).read_bytes())
    else:
        with zipfile.ZipFile(args.evidence) as archive:
            require(len(archive.namelist()) == len(set(archive.namelist())), 'Duplicate evidence member')
            result = verify(archive.read)
    print(json.dumps({'originalApplicationPassed': True, 'elapsedSeconds': result}, indent=2))


if __name__ == '__main__':
    main()
