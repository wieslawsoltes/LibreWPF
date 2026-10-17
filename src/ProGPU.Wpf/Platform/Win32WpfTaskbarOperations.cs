using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ProGPU.Backend;

namespace System.Windows.Media.ProGPU.Platform;

[SupportedOSPlatform("windows")]
internal sealed class Win32WpfTaskbarOperations : IWpfWin32TaskbarOperations
{
    private const int OwnerIndex = -8, StyleIndex = -16, ExtendedStyleIndex = -20;
    private readonly nint _window;
    private readonly uint _thread = GetCurrentThreadId();
    private readonly string _hiddenTitle = "LibreWPF taskbar owner " + Guid.NewGuid().ToString("N");

    internal Win32WpfTaskbarOperations(NativeWindowHandle window)
    {
        if (window.Kind != NativeWindowKind.Win32 || !window.IsValid || window.Display != 0)
            throw new ArgumentException("An actual Win32 source window is required.", nameof(window));
        _window = window.Handle;
        ValidateLocalTopLevel(_window);
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
        // WS_OVERLAPPEDWINDOW without WS_VISIBLE, matching the original source.
        // The system STATIC class owns its native procedure; no managed callback
        // can be collected while this retained hidden HWND remains alive.
        nint owner = CreateWindowExW(0, "STATIC", _hiddenTitle, 0x00cf0000,
            int.MinValue, int.MinValue, int.MinValue, int.MinValue,
            0, 0, GetModuleHandleW(null), 0); // Original CW_USEDEFAULT geometry.
        if (owner == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return owner; // Publish ownership before any further fallible operation.
    }

    public void ValidateHiddenOwner(nint owner)
    {
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
