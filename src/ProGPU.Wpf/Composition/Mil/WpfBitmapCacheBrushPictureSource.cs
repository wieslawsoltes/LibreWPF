using System;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using SceneRect = ProGPU.Scene.Rect;

namespace System.Windows.Media.ProGPU.Composition.Mil;

// Host-only subscription/recording adapter. ProGPU owns the retained source,
// deferred recapture, independent leases, GPU cache and rendering lifetime.
internal sealed class WpfBitmapCacheBrushPictureSource : ICachedPictureSource
{
    private readonly IPortableBitmapCacheBrushSource _source;
    private readonly global::ProGPU.Backend.WgpuContext? _context;
    private readonly WpfViewport3DTextureCache? _viewportCache;
    private readonly IWpfImageSourceAdapter? _imageSourceAdapter;
    private readonly WpfVisualInvalidationTracker _tracker = new();
    private bool _disposed;

    internal WpfBitmapCacheBrushPictureSource(
        IPortableBitmapCacheBrushSource source,
        global::ProGPU.Backend.WgpuContext? context = null,
        WpfViewport3DTextureCache? viewportCache = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _context = context;
        _viewportCache = viewportCache;
        _imageSourceAdapter = imageSourceAdapter;
        try
        {
            _tracker.Attach(source);
            _tracker.Invalidated += OnInvalidated;
        }
        catch
        {
            _tracker.Dispose();
            throw;
        }
    }

    public event EventHandler? Invalidated;

    private void OnInvalidated(object? sender, EventArgs args) => Invalidated?.Invoke(this, args);

    public CachedPictureSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Refresh subscriptions before recording and clear the coalesced dirty
        // flag so an event raised during capture is observable by ProGPU.
        _tracker.ConsumeDirty();
        var capture = WpfBitmapCacheBrushCapture.Create(_source, _context, _viewportCache, _imageSourceAdapter);
        try
        {
            // Transfer this newly owned recording directly to ProGPU; do not
            // clone leases merely to wrap the host capture result.
            return new CachedPictureSnapshot(capture.Picture,
                new SceneRect((float)capture.Bounds.X, (float)capture.Bounds.Y,
                    (float)capture.Bounds.Width, (float)capture.Bounds.Height),
                (float)capture.CachePolicy.RenderAtScale, capture.CachePolicy.EnableClearType);
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.Invalidated -= OnInvalidated;
        _tracker.Dispose();
        Invalidated = null;
    }
}
