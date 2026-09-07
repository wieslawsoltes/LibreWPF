using System;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using SceneRect = ProGPU.Scene.Rect;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>
/// An owned managed source recording for a typed BitmapCacheBrush. Capture
/// excludes outer root state; brush mapping/opacity and cache policy remain
/// separate metadata for the consumer. Dispose after transferring picture leases.
/// </summary>
public sealed class WpfBitmapCacheBrushCapture : IDisposable
{
    private WpfBitmapCacheBrushCapture(
        GpuPicture picture, PortableRect bounds,
        PortableBitmapCacheBrush brush, PortableBitmapCache cachePolicy)
    {
        Picture = picture;
        Bounds = bounds;
        Brush = brush;
        CachePolicy = cachePolicy;
    }

    public GpuPicture Picture { get; }
    public PortableRect Bounds { get; }
    public PortableBitmapCacheBrush Brush { get; }
    public PortableBitmapCache CachePolicy { get; }

    /// <summary>
    /// Creates a ProGPU-owned cached source with independent picture leases.
    /// The caller retains it across consumers and owns its lifetime. Brush
    /// transforms, opacity and shape coverage must still be applied by replay.
    /// </summary>
    public CachedPicture CreateCachedPicture() => new(Picture,
        new SceneRect((float)Bounds.X, (float)Bounds.Y, (float)Bounds.Width, (float)Bounds.Height),
        (float)CachePolicy.RenderAtScale, CachePolicy.EnableClearType);

    /// <summary>Updates an existing shared source after typed invalidation.</summary>
    public void UpdateCachedPicture(CachedPicture cachedPicture)
    {
        ArgumentNullException.ThrowIfNull(cachedPicture);
        cachedPicture.Update(Picture,
            new SceneRect((float)Bounds.X, (float)Bounds.Y, (float)Bounds.Width, (float)Bounds.Height),
            (float)CachePolicy.RenderAtScale, CachePolicy.EnableClearType);
    }

    public static WpfBitmapCacheBrushCapture Create(IPortableBitmapCacheBrushSource source) =>
        Create(source, null, null, null);

    internal static WpfBitmapCacheBrushCapture Create(
        IPortableBitmapCacheBrushSource source,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewportCache,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.TryGetPortableBitmapCacheBrush(out var brush))
            throw new NotSupportedException("The typed BitmapCacheBrush descriptor is unavailable.");
        if (!double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(source), "Cache-brush opacity must be finite and in [0, 1].");

        if (!PortableBitmapCacheBrushPolicy.TryResolve(brush, out var policy) ||
            !float.IsFinite((float)policy.RenderAtScale) ||
            (policy.RenderAtScale > 0 && (float)policy.RenderAtScale == 0))
            throw new NotSupportedException("The typed BitmapCache policy is unavailable or outside the managed renderer domain.");
        PortableRect bounds = default;
        if (brush.InternalTarget is object target)
        {
            if (target is not IPortableVisualStateSource visualSource ||
                !visualSource.TryGetPortableVisualState(out _))
                throw new NotSupportedException("Cache capture requires typed root visual state.");
            if (target is not IPortableVisualBoundsSource boundsSource ||
                !boundsSource.TryGetPortableVisualBounds(out var snapshot) ||
                (!snapshot.HasDescendantBounds && !snapshot.HasContentBounds))
                throw new NotSupportedException("Cache capture requires typed source bounds.");
            PortableRect rect = snapshot.HasDescendantBounds ? snapshot.DescendantBounds : snapshot.ContentBounds;
            if (!rect.IsEmpty)
            {
                if (rect.Width < 0 || rect.Height < 0 ||
                    !double.IsFinite(rect.X) || !double.IsFinite(rect.Y) ||
                    !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) ||
                    !float.IsFinite((float)rect.X) || !float.IsFinite((float)rect.Y) ||
                    !float.IsFinite((float)rect.Width) || !float.IsFinite((float)rect.Height) ||
                    !float.IsFinite((float)(rect.X + rect.Width)) || !float.IsFinite((float)(rect.Y + rect.Height)))
                    throw new NotSupportedException("Cache source bounds exceed the managed renderer coordinate domain.");
                bounds = new(rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        var recorder = new GpuPictureRecorder();
        var commands = recorder.BeginRecording(new SceneRect(
            (float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height));
        try
        {
            if (brush.InternalTarget != null && bounds.Width > 0 && bounds.Height > 0 && policy.RenderAtScale > 0)
            {
                using var sink = new ProGpuCompositionCommandSink(commands, context, viewportCache);
                var result = new WpfVisualTreeRenderer().ReplayBitmapCacheBrushSource(
                    brush.InternalTarget, sink, imageSourceAdapter);
                if (result.UnsupportedContentCount != 0 || result.UnsupportedVisualStateCount != 0 ||
                    result.RenderData.UnsupportedCount != 0)
                    throw new NotSupportedException("The cached visual source contains unsupported managed replay content or state.");
            }
            return new WpfBitmapCacheBrushCapture(recorder.EndRecording(), bounds, brush, policy);
        }
        finally
        {
            commands.Clear();
        }
    }

    public void Dispose() => Picture.Dispose();
}
