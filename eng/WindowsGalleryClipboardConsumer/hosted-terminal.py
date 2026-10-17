"""One normal close for the observed disposable runner's WSL startup terminal.

No input, activation, process termination, retry, settings or clipboard changes.
The original foreground/capture checks still decide acceptance after this guard.
"""
import ctypes as C
from ctypes import wintypes as W
import ntpath
import os
import time


def require(value, message):
    if not value:
        raise RuntimeError(message)


def close_startup(native, target, pid, deadline, record, environment, clock=time.monotonic, sleep=time.sleep):
    require(all(environment.get(k) == v for k, v in {
        'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
        'RUNNER_OS': 'Windows', 'GITHUB_REPOSITORY': 'wieslawsoltes/LibreWPF'}.items()),
        'Terminal setup requires the disposable LibreWPF Windows runner')
    require(not record, 'Terminal startup close cannot be repeated')
    record.update(closeRequests=0, inputSent=False, activationRequested=False, forcedTermination=False)
    native.owned(target, pid)
    if native.foreground() == target:
        record['result'] = 'original application still foreground'
        return
    require(clock() < deadline - 3, 'Original deadline exhausted before terminal setup')
    context = native.context()
    record['context'] = context
    require(context['desktop'] == 'Default' and context['session'] > 0, 'Ordinary interactive desktop required')
    window = native.snapshot(native.foreground())
    record['window'] = window
    require(window['hwnd'] != target and window['pid'] not in (pid, os.getpid()) and window['pid'] > 0
            and window['visible'] and window['session'] == context['session']
            and window['class'] == 'CASCADIA_HOSTING_WINDOW_CLASS'
            and ntpath.normcase(window['title']) == ntpath.normcase(ntpath.join(context['system'], 'wsl.exe'))
            and window['packageFamily'] == 'Microsoft.WindowsTerminal_8wekyb3d8bbwe'
            and ntpath.basename(window['image']).lower() == 'windowsterminal.exe',
            'Foreground is not the identified packaged Windows WSL startup terminal')
    native.idle()
    native.owned(target, pid)
    require(native.foreground() == window['hwnd'] and native.snapshot(window['hwnd']) == window,
            'Terminal identity changed before normal close')
    require(clock() < deadline - 3, 'Original deadline exhausted before normal close')
    record['closeRequests'] = 1
    record['message'] = native.close(window['hwnd'])
    require(record['message']['succeeded'], 'Terminal declined normal close request')
    until = min(deadline - 2, clock() + 1)
    while native.visible(window['hwnd']):
        require(clock() < until, 'Terminal did not close within original budget')
        sleep(.025)
    native.owned(target, pid)
    record['after'] = native.foreground()
    require(record['after'] == target, 'Original application did not naturally regain foreground')
    record['result'] = 'identified startup terminal accepted one normal close'


class Native:
    def __init__(self, desktop):
        self.desktop = desktop
        self.u = desktop.u
        self.k = C.WinDLL('kernel32', use_last_error=True)
        def bind(lib, name, result, *args):
            fn = getattr(lib, name); fn.restype = result; fn.argtypes = args
        bind(self.k, 'OpenProcess', W.HANDLE, W.DWORD, W.BOOL, W.DWORD)
        bind(self.k, 'CloseHandle', W.BOOL, W.HANDLE)
        bind(self.k, 'QueryFullProcessImageNameW', W.BOOL, W.HANDLE, W.DWORD, W.LPWSTR, C.POINTER(W.DWORD))
        bind(self.k, 'GetPackageFamilyName', W.LONG, W.HANDLE, C.POINTER(W.UINT), W.LPWSTR)
        bind(self.k, 'ProcessIdToSessionId', W.BOOL, W.DWORD, C.POINTER(W.DWORD))
        bind(self.k, 'GetSystemDirectoryW', W.UINT, W.LPWSTR, W.UINT)
        bind(self.u, 'OpenInputDesktop', W.HANDLE, W.DWORD, W.BOOL, W.DWORD)
        bind(self.u, 'CloseDesktop', W.BOOL, W.HANDLE)
        bind(self.u, 'GetUserObjectInformationW', W.BOOL, W.HANDLE, C.c_int, W.LPVOID, W.DWORD, C.POINTER(W.DWORD))
        bind(self.u, 'SendMessageTimeoutW', C.c_ssize_t, W.HWND, W.UINT, C.c_size_t, C.c_ssize_t, W.UINT, W.UINT, C.POINTER(C.c_size_t))

    def session(self, pid):
        result = W.DWORD()
        require(self.k.ProcessIdToSessionId(pid, C.byref(result)), 'Process session unavailable')
        return result.value

    def context(self):
        handle = self.u.OpenInputDesktop(0, False, 1)
        require(handle, 'Input desktop unavailable')
        try:
            name, needed, system = C.create_unicode_buffer(256), W.DWORD(), C.create_unicode_buffer(1024)
            require(self.u.GetUserObjectInformationW(handle, 2, name, C.sizeof(name), C.byref(needed)), 'Desktop name unavailable')
            require(0 < self.k.GetSystemDirectoryW(system, 1024) < 1024, 'System directory unavailable')
            return dict(desktop=name.value, system=system.value, session=self.session(os.getpid()))
        finally:
            require(self.u.CloseDesktop(handle), 'Desktop handle cleanup failed')

    def snapshot(self, hwnd):
        value = self.desktop.window_observation(hwnd)
        handle = self.k.OpenProcess(0x1000, False, value['pid'])
        require(handle, 'Foreground process identity unavailable')
        try:
            image, size = C.create_unicode_buffer(32768), W.DWORD(32768)
            require(self.k.QueryFullProcessImageNameW(handle, 0, image, C.byref(size)), 'Process image unavailable')
            family, length = C.create_unicode_buffer(256), W.UINT(256)
            require(self.k.GetPackageFamilyName(handle, C.byref(length), family) == 0, 'Packaged terminal identity unavailable')
            return dict(hwnd=hwnd, pid=value['pid'], thread=value['thread'], title=value['title'],
                        **{'class': value['nativeClass']}, image=image.value, packageFamily=family.value,
                        visible=self.visible(hwnd), session=self.session(value['pid']))
        finally:
            require(self.k.CloseHandle(handle), 'Process identity handle cleanup failed')

    def foreground(self): return self.u.GetForegroundWindow()
    def visible(self, hwnd): return bool(self.u.IsWindow(hwnd) and self.u.IsWindowVisible(hwnd))
    def owned(self, hwnd, pid): self.desktop.owned_window(hwnd, pid)
    def idle(self): self.desktop.require_idle_input()

    def close(self, hwnd):
        result = C.c_size_t(); C.set_last_error(0)
        sent = self.u.SendMessageTimeoutW(hwnd, 0x10, 0, 0, 0x22, 500, C.byref(result))
        return dict(succeeded=bool(sent), result=result.value, error=C.get_last_error())
