namespace System.Windows.Media.ProGPU.Platform;

/// <summary>
/// Source WPF taskbar policy. Native operations must validate the exact live,
/// local, creating-thread window identities; this policy never selects windows.
/// </summary>
internal sealed class WpfWin32TaskbarPolicy
{
    internal const long AppWindow = 0x00040000;
    private readonly IWpfWin32TaskbarOperations _native;
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private nint _hiddenOwner;
    private bool _applying;
    private bool _retiring;
    private bool _released;

    internal WpfWin32TaskbarPolicy(IWpfWin32TaskbarOperations native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    internal void Apply(bool showInTaskbar)
    {
        VerifyThread();
        if (_released) throw new ObjectDisposedException(nameof(WpfWin32TaskbarPolicy));
        if (_applying || _retiring) throw new InvalidOperationException("Taskbar mutation cannot reenter.");
        _applying = true;
        try
        {
            WpfWin32TaskbarSnapshot original = _native.Read();
            // Original WPF creates its hidden owner even when a source Owner is
            // already present, and keeps that owner until native destruction.
            if (!showInTaskbar && _hiddenOwner == 0)
            {
                _hiddenOwner = _native.CreateHiddenOwner();
                if (_hiddenOwner == 0)
                    throw new InvalidOperationException("The hidden taskbar owner was not created.");
            }
            if (_hiddenOwner != 0) _native.ValidateHiddenOwner(_hiddenOwner);

            nint owner = original.Owner;
            if (showInTaskbar && owner == _hiddenOwner) owner = 0;
            else if (!showInTaskbar && owner == 0) owner = _hiddenOwner;
            long style = showInTaskbar ? original.ExtendedStyle | AppWindow
                : original.ExtendedStyle & ~AppWindow;
            if (owner == original.Owner && style == original.ExtendedStyle) return;

            try
            {
                // Match the source's SWP_NOACTIVATE hide/change/show sequence.
                // TOOLWINDOW and every unrelated chrome bit remain untouched.
                if (original.Visible) _native.SetVisibleWithoutActivation(false);
                if (owner != original.Owner) _native.SetOwner(owner);
                if (style != original.ExtendedStyle) _native.SetExtendedStyle(style);
                if (original.Visible) _native.SetVisibleWithoutActivation(true);
                if (_native.Read() != new WpfWin32TaskbarSnapshot(owner, style, original.Visible))
                    throw new InvalidOperationException("Native taskbar publication did not retain the requested state.");
            }
            catch (Exception failure)
            {
                // A failed candidate must not publish half an owner/style pair.
                // Keep the hidden owner retained even if rollback itself fails.
                try
                {
                    _native.SetOwner(original.Owner);
                    _native.SetExtendedStyle(original.ExtendedStyle);
                    _native.SetVisibleWithoutActivation(original.Visible);
                    if (_native.Read() != original)
                        throw new InvalidOperationException("Native taskbar rollback did not restore the original state.");
                }
                catch (Exception rollback)
                {
                    throw new AggregateException("Native taskbar publication and rollback failed.", failure, rollback);
                }
                throw;
            }
        }
        finally { _applying = false; }
    }

    // Called only by WpfNativeWindowRetirement's post-destruction dependency
    // release. The native adapter independently rejects a still-live child.
    internal void ReleaseAfterNativeWindow()
    {
        VerifyThread();
        if (_applying || _retiring) throw new InvalidOperationException("Taskbar retirement cannot reenter or interrupt publication.");
        if (_released) return;
        _retiring = true;
        try
        {
            _native.RequireChildDestroyed();
            if (_hiddenOwner != 0)
            {
                _native.ValidateHiddenOwner(_hiddenOwner);
                _native.DestroyHiddenOwner(_hiddenOwner);
                _hiddenOwner = 0;
            }
            _released = true;
        }
        finally { _retiring = false; }
    }

    private void VerifyThread()
    {
        if (_threadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Taskbar ownership belongs to its creating thread.");
    }
}

internal readonly record struct WpfWin32TaskbarSnapshot(nint Owner, long ExtendedStyle, bool Visible);

internal interface IWpfWin32TaskbarOperations
{
    WpfWin32TaskbarSnapshot Read();
    nint CreateHiddenOwner();
    void ValidateHiddenOwner(nint owner);
    void SetOwner(nint owner);
    void SetExtendedStyle(long style);
    void SetVisibleWithoutActivation(bool visible);
    void RequireChildDestroyed();
    void DestroyHiddenOwner(nint owner);
}
