"""Read-only owned HWND roles and Z order; never filters acceptance or sends input."""
import ctypes as C


def pointer():
    class Point(C.Structure):
        _fields_ = [("x", C.c_int32), ("y", C.c_int32)]
    user = C.WinDLL("user32", use_last_error=True)
    user.GetCursorPos.restype = C.c_int32
    user.GetCursorPos.argtypes = [C.POINTER(Point)]
    value = Point()
    if not user.GetCursorPos(C.byref(value)):
        raise RuntimeError("Actual native cursor observation unavailable")
    return dict(x=value.x, y=value.y)


def observe(desktop, pid, owner, windows):
    user = desktop.user
    user.GetClassNameW.restype = C.c_int32
    user.GetClassNameW.argtypes = [C.c_void_p, C.c_wchar_p, C.c_int32]
    user.GetWindowLongPtrW.restype = C.c_ssize_t
    user.GetWindowLongPtrW.argtypes = [C.c_void_p, C.c_int32]
    rows = []
    for window in windows:
        hwnd = window["hwnd"]
        if desktop.pid(hwnd) != pid or not user.IsWindowVisible(hwnd):
            raise RuntimeError("Observed HWND ownership or visibility changed")
        name = C.create_unicode_buffer(256)
        if not user.GetClassNameW(hwnd, name, len(name)):
            raise RuntimeError("Observed HWND class unavailable")
        rows.append(dict(window, pid=pid, windowClass=name.value,
                         owner=int(user.GetWindow(hwnd, 4) or 0),
                         style=user.GetWindowLongPtrW(hwnd, -16) & 0xffffffff,
                         extendedStyle=user.GetWindowLongPtrW(hwnd, -20) & 0xffffffff))
    native_owner = dict(id=owner["hwnd"], pid=pid, title=owner["title"],
                        bounds=owner["bounds"], client=owner["client"])
    return dict(qualified=False, windows=rows,
                frontToOwner=desktop.owner_obstruction_inventory(native_owner))
