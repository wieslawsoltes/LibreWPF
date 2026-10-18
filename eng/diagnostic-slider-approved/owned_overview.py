"""One authorized public-property change on the disposable owned GNOME shell."""
import re
import time


def close_owned_overview(command, child, *, approved, clock=time.monotonic, sleep=time.sleep):
    if approved is not True:
        raise PermissionError('Hosted overview/input proposal has not been approved')
    deadline = clock() + 5
    record = {'setAttempts': 0, 'observations': [], 'completed': False}

    def call(args):
        remaining = deadline - clock()
        if remaining <= 0:
            raise TimeoutError('Owned overview startup exceeded five seconds')
        if child.poll() is not None:
            raise RuntimeError('Owned compositor exited')
        result = command(args, timeout=remaining)
        if result.returncode:
            raise RuntimeError('Owned overview D-Bus request failed: ' + result.stdout)
        return result.stdout.strip()

    bus = ['gdbus', 'call', '--session', '--dest', 'org.freedesktop.DBus',
           '--object-path', '/org/freedesktop/DBus', '--method']
    # Pin the unique connection, so a later well-known-name replacement cannot
    # redirect our single Set to another shell.
    owner = call(bus + ['org.freedesktop.DBus.GetNameOwner', 'org.gnome.Shell'])
    match = re.fullmatch(r"\('(:[0-9]+\.[0-9]+)',\)", owner)
    if not match:
        raise RuntimeError('Invalid shell unique bus owner')
    owner = match[1]
    def verify():
        identity = call(bus + ['org.freedesktop.DBus.GetConnectionUnixProcessID', owner])
        if identity != '(uint32 ' + str(child.pid) + ',)':
            raise RuntimeError('Shell bus connection is not the owned compositor')
    verify()
    base = ['gdbus', 'call', '--session', '--dest', owner,
            '--object-path', '/org/gnome/Shell', '--method']
    def read():
        verify()
        result = call(base + ['org.freedesktop.DBus.Properties.Get', 'org.gnome.Shell', 'OverviewActive'])
        if result not in ('(<true>,)', '(<false>,)'):
            raise RuntimeError('Overview state is not an exact boolean')
        record['observations'].append(result)
        return result == '(<true>,)'
    record['uniqueOwner'] = owner
    if read():
        verify()
        record['setAttempts'] = 1
        # Public setter only; no Eval, unsafe-mode or shell policy modification.
        result = call(base + ['org.freedesktop.DBus.Properties.Set', 'org.gnome.Shell', 'OverviewActive', '<false>'])
        if result != '()':
            raise RuntimeError('Overview Set returned an unexpected result')
        while read():
            sleep(min(.05, max(0, deadline-clock())))
    record['completed'] = True
    return record
