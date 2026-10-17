using System.Runtime.ExceptionServices;

namespace System.Windows.Media.ProGPU.Platform;

// A provider may replace/destroy its HWND icons before the hidden owner dies.
// Own copies, and release only handles no longer installed in the hidden HWND.
internal sealed class WpfWin32TaskbarIcons(IWpfWin32TaskbarIconOperations native)
{
    private readonly HashSet<nint> _owned = [];

    internal void Synchronize(nint owner)
    {
        WpfWin32IconPair original = native.ReadTargetIcons(owner);
        Exception? failure = null;
        try
        {
            WpfWin32IconPair source = native.ReadSourceIcons();
            var candidate = new WpfWin32IconPair(Copy(source.Large), Copy(source.Small));
            try
            {
                native.WriteTargetIcons(owner, candidate);
                if (native.ReadTargetIcons(owner) != candidate)
                    throw new InvalidOperationException("The hidden taskbar owner did not retain its icon pair.");
            }
            catch (Exception publication)
            {
                try
                {
                    native.WriteTargetIcons(owner, original);
                    if (native.ReadTargetIcons(owner) != original)
                        throw new InvalidOperationException("Hidden taskbar icon rollback did not restore the original pair.");
                }
                catch (Exception rollback)
                {
                    throw new AggregateException("Hidden taskbar icon publication and rollback failed.", publication, rollback);
                }
                throw;
            }
        }
        catch (Exception error) { failure = error; }

        try
        {
            // Query actual bindings even after partial publication/rollback.
            // If that query fails, retain every copy for native retirement.
            ReleaseExcept(native.ReadTargetIcons(owner));
        }
        catch (Exception cleanup)
        {
            if (failure != null) throw new AggregateException("Hidden taskbar icon update and cleanup failed.", failure, cleanup);
            throw;
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // The policy calls this only after child AND hidden HWND destruction.
    internal void ReleaseAfterWindows() => ReleaseExcept(default);

    private nint Copy(nint icon)
    {
        if (icon == 0) return 0;
        nint copy = native.CopyIcon(icon);
        if (copy == 0) throw new InvalidOperationException("The native icon copy was not created.");
        _owned.Add(copy);
        return copy;
    }

    private void ReleaseExcept(WpfWin32IconPair retained)
    {
        foreach (nint icon in _owned.ToArray())
        {
            if (icon == retained.Large || icon == retained.Small) continue;
            native.DestroyIcon(icon);
            _owned.Remove(icon); // A failed release remains owned for retry.
        }
    }
}

internal readonly record struct WpfWin32IconPair(nint Large, nint Small);

internal interface IWpfWin32TaskbarIconOperations
{
    WpfWin32IconPair ReadSourceIcons();
    WpfWin32IconPair ReadTargetIcons(nint owner);
    void WriteTargetIcons(nint owner, WpfWin32IconPair icons);
    nint CopyIcon(nint icon);
    void DestroyIcon(nint icon);
}
