"""Compare original source windows and retain provider infrastructure separately.

This is a new diagnostic, not a replacement for the original modal acceptance.
Every enumerated window remains in the raw evidence. Provider infrastructure must
be identical to its own initial inventory until the controlled windows close;
its implementation-specific styles are not claimed as cross-provider parity.
"""
PHASES = ['unowned-default', 'unowned-false', 'unowned-true-again', 'unowned-false-again',
          'owned-A-false', 'owned-A-true', 'owned-B-false', 'owner-cleared-false', 'owner-cleared-true', 'subject-closed']


def require(value, message):
    if not value: raise RuntimeError(message)


def normalize(result):
    require(result.get('diagnosticOnly') is True and result.get('qualified') is False and result.get('failure') is None,
            'Original source run failed or changed its scope')
    records = result.get('records', [])
    require([r['phase'] for r in records] == PHASES, 'Original ten-record inventory changed')
    identities = {0: 'none', records[0]['native']['handle']: 'subject', records[1]['native']['owner']: 'hidden',
                  records[4]['sourceOwner']: 'owner-A', records[6]['sourceOwner']: 'owner-B'}
    require(len(identities) == 5 and all(type(h) is int and h >= 0 for h in identities), 'Original native roles are not distinct')
    process, thread = records[0]['native']['process'], records[0]['native']['thread']
    require(type(process) is int and process > 0 and type(thread) is int and thread > 0, 'Original process/thread identity missing')

    def inventory(values):
        require(isinstance(values, list) and len(values) <= 32, 'Native inventory exceeds original bound')
        require(len({v['handle'] for v in values}) == len(values), 'Duplicate native inventory entry')
        for value in values:
            require(type(value['handle']) is int and value['handle'] > 0 and
                    value['process'] == process and value['thread'] == thread,
                    'Native ownership left the original process/thread')
        return {v['handle']: v for v in values}

    baseline = inventory(records[0]['inventory'])
    infrastructure = {h: v for h, v in baseline.items() if h not in identities}
    require(all(v['visible'] is False for v in infrastructure.values()), 'Unidentified visible source window')

    def native(value):
        require(value['handle'] in identities and value['handle'] != 0 and value['owner'] in identities,
                'Unknown controlled native window or owner')
        return {k: identities[value[k]] if k in ('handle', 'owner') else value[k]
                for k in ('handle', 'owner', 'style', 'extendedStyle', 'appWindow', 'toolWindow', 'visible', 'enabled')}

    normalized = []
    for index, record in enumerate(records):
        current = inventory(record['inventory'])
        require({h: v for h, v in current.items() if h not in identities} == infrastructure,
                'Provider infrastructure changed during the original transitions')
        controlled = [native(v) for h, v in current.items() if h in identities]
        expected = {'subject', 'owner-A', 'owner-B'} if index == 0 else (
            {'owner-A', 'owner-B'} if index == 9 else {'subject', 'owner-A', 'owner-B', 'hidden'})
        require({v['handle'] for v in controlled} == expected, 'Controlled source/hidden-owner lifetime changed')
        value = dict(phase=record['phase'], inventory=sorted(controlled, key=lambda x: x['handle']))
        if 'native' in record:
            require(record['sourceOwner'] in identities, 'Unknown source owner identity')
            value.update(native=native(record['native']), sourceOwner=identities[record['sourceOwner']],
                         sourceShowInTaskbar=record['sourceShowInTaskbar'], sourceVisible=record['sourceVisible'])
            require(record['native'] == current[record['native']['handle']], 'Subject native observations disagree')
        else:
            require(record['originalHandle'] == records[0]['native']['handle'] and record['originalStillExists'] is False,
                    'Original native child did not retire')
        normalized.append(value)
    final = inventory(result['finalInventory'])
    require(all(h in infrastructure and v == infrastructure[h] for h, v in final.items()),
            'Controlled windows survived cleanup or provider infrastructure changed')
    return normalized


def compare(reference, candidate):
    expected, actual = normalize(reference), normalize(candidate)
    require(actual == expected, 'Original controlled source/native taskbar observations differ from Microsoft WPF')
    return dict(diagnosticOnly=True, qualified=False, matchedRecords=len(expected),
                scope='Original three source windows and retained taskbar owner, with separate stable infrastructure inventories; not modal-input or pixel acceptance')
