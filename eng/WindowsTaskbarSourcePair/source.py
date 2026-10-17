"""Read-only native identity observation for the original taskbar scenarios."""
import hashlib

ORIGINAL_GIT_SHA = '02aa891b89856fdd6d27215281f00c9b88825088c0e965cec37f3cf958fddc2b'
ORIGINAL_WINDOWS_SHA = '052bb795e5c0d1ad35b20d135b0b61eff7903436d23f767d1462e5337b5195af'
ORIGINAL_COMMIT = '12739b51f9505b40b2c74e40cb6bac0fba179c07'
ORIGINAL_PATH = 'eng/WindowsModalDesktop/TaskbarOwnershipDiagnostic.cs'
HELPER = '''    private static readonly Dictionary<long, object> HostCompilers = [];
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern nint GetClassLongPtrW(nint window, int index);
    private static object NativeDetails(nint window)
    {
        var name = new System.Text.StringBuilder(256);
        var title = new System.Text.StringBuilder(256);
        if (GetClassNameW(window, name, name.Capacity) == 0 || !GetWindowRect(window, out var rectangle))
            throw new InvalidOperationException("Original native window metadata unavailable.");
        GetWindowTextW(window, title, title.Capacity);
        return new { nativeClass = name.ToString(), title = title.ToString(),
            rectangle = new[] { rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom },
            classStyle = $"0x{GetClassLongPtrW(window, -26).ToInt64():X}" };
    }

    private static nint NativeHandle(Window window)
    {
#if LIBREWPF_TASKBAR
        if (!global::System.Windows.Media.ProGPU.ProGpuWpfDiagnostics.TryGetWindowHost(window, out var host) ||
            host == null || !ReferenceEquals(host.WpfRootVisual, window))
            throw new InvalidOperationException("The original source has no exact live host.");
        var handle = host.NativeWindowHandle;
        if (!global::System.Windows.Media.ProGPU.ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(window, out var snapshots))
            throw new InvalidOperationException("Actual source/native snapshot is unavailable.");
        var primary = snapshots.Single(value => ReferenceEquals(value.RootVisual, window));
        if (!handle.IsValid || handle.Kind != global::ProGPU.Backend.NativeWindowKind.Win32 ||
            (handle.Display != 0 && GetWindowLongPtrW(handle.Handle, -6) != handle.Display) ||
            primary.RendererMode != global::System.Windows.Media.ProGPU.ProGpuWpfRendererMode.NativeMilWgpu ||
            primary.NativeWindow != handle || !primary.NativeMilHitTestingEnabled || host.CompositionTarget?.Context is not { } context ||
            context.SelectedDx12ShaderCompiler.ToString() != "Dxc")
            throw new InvalidOperationException("Actual native MIL/DXC source identity is unavailable.");
        if (!HostCompilers.ContainsKey((long)handle.Handle) && HostCompilers.Count == 3)
            throw new InvalidOperationException("Original three-window compiler inventory exceeded.");
        HostCompilers[(long)handle.Handle] = new { selected = context.SelectedDx12ShaderCompiler.ToString(),
            library = context.Dx12CompilerLibraryPath };
        return handle.Handle;
#else
        return new WindowInteropHelper(window).Handle;
#endif
    }

'''.replace('\n', '\r\n').encode()


def require(value, message):
    if not value: raise RuntimeError(message)


def select(original_git):
    require(hashlib.sha256(original_git).hexdigest() == ORIGINAL_GIT_SHA, 'Original taskbar source changed')
    original = original_git.replace(b'\n', b'\r\n')
    require(hashlib.sha256(original).hexdigest() == ORIGINAL_WINDOWS_SHA, 'Original Windows source bytes differ')
    selected = original
    replacements = [
        (b'            visible = IsWindowVisible(handle), enabled = IsWindowEnabled(handle) };',
         b'            visible = IsWindowVisible(handle), enabled = IsWindowEnabled(handle), nativeDetails = NativeDetails(handle) };', 1),
        (b'new WindowInteropHelper(subject).Handle', b'NativeHandle(subject)', 2),
        (b'new WindowInteropHelper(subject.Owner).Handle', b'NativeHandle(subject.Owner)', 1),
        (b'            records = Records, finalInventory = Inventory(), failure = failure?.ToString()',
         b'            records = Records, hostCompilers = HostCompilers, finalInventory = Inventory(), failure = failure?.ToString()', 1),
        (b'    private static Window MakeWindow', HELPER + b'    private static Window MakeWindow', 1),
    ]
    for before, after, count in replacements:
        require(selected.count(before) == count, 'Original observer anchor changed')
        selected = selected.replace(before, after)
    # Reverse only the declared observer edits; all source actions and bounds
    # must reconstruct the exact original successful Windows reference bytes.
    restored = selected
    for before, after, count in reversed(replacements):
        require(restored.count(after) == count, 'Selected observer inventory changed')
        restored = restored.replace(after, before)
    require(restored == original, 'Original taskbar scenarios or deadlines changed')
    return original, selected
