using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ProGPU.Backend;

namespace System.Windows.Media.ProGPU.Platform;

[SupportedOSPlatform("windows")]
internal sealed class Win32WpfTaskbarOperations : IWpfWin32TaskbarOperations, IWpfWin32TaskbarIconOperations
{
    private const int OwnerIndex = -8, StyleIndex = -16, ExtendedStyleIndex = -20;
    private readonly nint _window;
    private readonly uint _thread = GetCurrentThreadId();
    private readonly string _hiddenTitle = "LibreWPF taskbar owner " + Guid.NewGuid().ToString("N");
    private readonly WpfWin32TaskbarIcons _icons;
    private nint _liveHiddenOwner;

    internal Win32WpfTaskbarOperations(NativeWindowHandle window)
    {
        if (window.Kind != NativeWindowKind.Win32 || !window.IsValid || window.Display != 0)
            throw new ArgumentException("An actual Win32 source window is required.", nameof(window));
        _window = window.Handle;
        ValidateLocalTopLevel(_window);
        _icons = new(this);
    }

    public WpfWin32TaskbarSnapshot Read()
    {
        ValidateLocalTopLevel(_window);
        return new(ReadLong(_window, OwnerIndex), unchecked((uint)ReadLong(_window, ExtendedStyleIndex)),
            IsWindowVisible(_window));
    }

    public nint CreateHiddenOwner()
    {
        ValidateLocalTopLevel(_window);
        if (_liveHiddenOwner != 0) throw new InvalidOperationException("A hidden taskbar owner already exists.");
        // WS_OVERLAPPEDWINDOW without WS_VISIBLE, matching the original source.
        // The system STATIC class owns its native procedure; no managed callback
        // can be collected while this retained hidden HWND remains alive.
        nint owner = CreateWindowExW(0, "STATIC", _hiddenTitle, 0x00cf0000,
            int.MinValue, int.MinValue, int.MinValue, int.MinValue,
            0, 0, GetModuleHandleW(null), 0); // Original CW_USEDEFAULT geometry.
        if (owner == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _liveHiddenOwner = owner;
        return owner; // Publish ownership before any further fallible operation.
    }

    public void ValidateHiddenOwner(nint owner)
    {
        if (owner == 0 || owner != _liveHiddenOwner)
            throw new InvalidOperationException("The hidden taskbar owner is not owned by this host.");
        ValidateLocalTopLevel(owner);
        var title = new StringBuilder(128);
        var className = new StringBuilder(32);
        if (GetWindowTextW(owner, title, title.Capacity) != _hiddenTitle.Length ||
            title.ToString() != _hiddenTitle || GetClassNameW(owner, className, className.Capacity) == 0 ||
            !string.Equals(className.ToString(), "Static", StringComparison.OrdinalIgnoreCase) || IsWindowVisible(owner))
            throw new InvalidOperationException("The retained hidden taskbar owner identity changed.");
    }

    public void SetOwner(nint owner)
    {
        ValidateLocalTopLevel(_window);
        nint ancestor = owner;
        for (int depth = 0; ancestor != 0; depth++)
        {
            if (ancestor == _window || depth == 1024)
                throw new InvalidOperationException("Native taskbar ownership is cyclic or exceeds its bound.");
            ValidateLocalTopLevel(ancestor);
            ancestor = ReadLong(ancestor, OwnerIndex);
        }
        // Windows rejects the unowned -> unowned GWLP_HWNDPARENT write with
        // ERROR_INVALID_WINDOW_HANDLE. Preserve idempotent native state, also
        // when a failed candidate rolls back an already-restored owner.
        if (ReadLong(_window, OwnerIndex) == owner) return;
        WriteLong(_window, OwnerIndex, owner);
    }

    public void SetExtendedStyle(long style)
    {
        ValidateLocalTopLevel(_window);
        long current = unchecked((uint)ReadLong(_window, ExtendedStyleIndex));
        if (((current ^ style) & ~WpfWin32TaskbarPolicy.AppWindow) != 0)
            throw new InvalidOperationException("Taskbar policy cannot replace unrelated native chrome.");
        WriteLong(_window, ExtendedStyleIndex, unchecked((nint)(uint)style));
    }

    public void SetVisibleWithoutActivation(bool visible)
    {
        ValidateLocalTopLevel(_window);
        // NOMOVE | NOSIZE | NOZORDER | NOACTIVATE | NOSENDCHANGING, then SHOW/HIDE.
        if (!SetWindowPos(_window, 0, 0, 0, 0, 0, 0x0417u | (visible ? 0x0040u : 0x0080u)))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public void RequireChildDestroyed()
    {
        VerifyThread();
        // Fail closed even if the numeric handle was reused. The provider's
        // completed retirement is required independently by the host lease.
        if (IsWindow(_window))
            throw new InvalidOperationException("The native child must retire before its hidden owner.");
    }

    public void DestroyHiddenOwner(nint owner)
    {
        RequireChildDestroyed();
        ValidateHiddenOwner(owner);
        if (!DestroyWindow(owner)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _liveHiddenOwner = 0;
    }

    public void SynchronizeHiddenOwnerIcons(nint owner)
    {
        ValidateLocalTopLevel(_window);
        ValidateHiddenOwner(owner);
        _icons.Synchronize(owner);
    }

    public void ReleaseIconCopiesAfterWindows()
    {
        RequireChildDestroyed();
        if (_liveHiddenOwner != 0)
            throw new InvalidOperationException("The hidden owner still borrows its retained icon copies.");
        _icons.ReleaseAfterWindows();
    }

    WpfWin32IconPair IWpfWin32TaskbarIconOperations.ReadSourceIcons()
    {
        ValidateLocalTopLevel(_window);
        WpfWin32IconPair icons = ReadIcons(_window);
        // WM_GETICON returns zero for a class-provided default. Copy that
        // actual source class icon, rather than selecting the STATIC default.
        return new(icons.Large != 0 ? icons.Large : ReadClassIcon(_window, -14),
            icons.Small != 0 ? icons.Small : ReadClassIcon(_window, -34));
    }

    WpfWin32IconPair IWpfWin32TaskbarIconOperations.ReadTargetIcons(nint owner)
    {
        ValidateHiddenOwner(owner);
        return ReadIcons(owner);
    }

    void IWpfWin32TaskbarIconOperations.WriteTargetIcons(nint owner, WpfWin32IconPair icons)
    {
        ValidateHiddenOwner(owner);
        SendMessageW(owner, 0x0080, 1, icons.Large); // WM_SETICON / ICON_BIG
        ValidateHiddenOwner(owner);
        SendMessageW(owner, 0x0080, 0, icons.Small); // ICON_SMALL
    }

    nint IWpfWin32TaskbarIconOperations.CopyIcon(nint icon)
    {
        VerifyThread();
        nint copy = CopyIcon(icon);
        if (copy == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return copy;
    }

    void IWpfWin32TaskbarIconOperations.DestroyIcon(nint icon)
    {
        VerifyThread();
        if (!DestroyIcon(icon)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    private static WpfWin32IconPair ReadIcons(nint window) =>
        new(SendMessageW(window, 0x007f, 1, 0), SendMessageW(window, 0x007f, 0, 0)); // WM_GETICON

    private static nint ReadClassIcon(nint window, int index)
    {
        Marshal.SetLastPInvokeError(0);
        nint icon = IntPtr.Size == 8 ? GetClassLongPtrW(window, index) : unchecked((nint)GetClassLongW(window, index));
        int error = Marshal.GetLastPInvokeError();
        if (icon == 0 && error != 0) throw new Win32Exception(error);
        return icon;
    }

    private void VerifyThread()
    {
        if (GetCurrentThreadId() != _thread)
            throw new InvalidOperationException("Native taskbar ownership belongs to its creating thread.");
    }

    private void ValidateLocalTopLevel(nint window)
    {
        VerifyThread();
        if (!IsWindow(window) || GetWindowThreadProcessId(window, out uint pid) != _thread ||
            pid != (uint)Environment.ProcessId || (ReadLong(window, StyleIndex).ToInt64() & 0x40000000L) != 0)
            throw new InvalidOperationException("A live local top-level window on the creating thread is required.");
    }

    private static nint ReadLong(nint window, int index)
    {
        Marshal.SetLastPInvokeError(0);
        nint result = IntPtr.Size == 8 ? GetWindowLongPtrW(window, index) : GetWindowLongW(window, index);
        int error = Marshal.GetLastPInvokeError();
        if (result == 0 && error != 0) throw new Win32Exception(error);
        return result;
    }

    private static void WriteLong(nint window, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = IntPtr.Size == 8 ? SetWindowLongPtrW(window, index, value)
            : SetWindowLongW(window, index, unchecked((int)value));
        int error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetClassLongPtrW(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetClassLongW(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLongW(nint window, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
}
