using ProGPU.Backend;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

/// <summary>One source host's exact native window and renderer retirement lease.</summary>
internal sealed class WpfNativeWindowRetirement
{
    private readonly Action _releaseResources;
    private readonly Func<IWindow, bool> _tryDispose;
    private readonly Action? _releaseNativeDependencies;
    private bool _resourcesReleased;
    private bool _nativeWindowReleased;
    private bool _attempting;

    internal WpfNativeWindowRetirement(IWindow window, int threadId, Action releaseResources,
        Func<IWindow, bool>? tryDispose = null, Action? releaseNativeDependencies = null)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        _releaseResources = releaseResources ?? throw new ArgumentNullException(nameof(releaseResources));
        ThreadId = threadId;
        _tryDispose = tryDispose ?? NativeWindowLifetime.TryDispose;
        _releaseNativeDependencies = releaseNativeDependencies;
    }

    internal IWindow Window { get; }
    internal int ThreadId { get; }
    internal bool IsComplete { get; private set; }

    internal bool TryComplete()
    {
        if (Environment.CurrentManagedThreadId != ThreadId)
            throw new InvalidOperationException("Native window retirement belongs to its creating thread.");
        if (IsComplete || _attempting)
            return IsComplete;

        _attempting = true;
        try
        {
            if (!_resourcesReleased)
            {
                _releaseResources();
                _resourcesReleased = true;
            }

            // A false result (or exception) retains this exact provider and its
            // cleanup owner. Never infer native destruction from Dispose return.
            if (!_nativeWindowReleased)
            {
                if (!_tryDispose(Window)) return false;
                _nativeWindowReleased = true;
            }

            // Destroying a hidden top-level owner can also destroy its owned
            // HWNDs. Such dependencies outlive actual native child retirement,
            // not merely the source Close callback or renderer disposal. A failed
            // dependency release retains this lease without disposing Window again.
            _releaseNativeDependencies?.Invoke();
            IsComplete = true;
            return true;
        }
        finally
        {
            _attempting = false;
        }
    }
}
