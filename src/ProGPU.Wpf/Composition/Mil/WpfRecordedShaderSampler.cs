using System;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using SceneRect = ProGPU.Scene.Rect;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>
/// Captures source data now; realizes its texture only against the actual
/// producer-owned target later. Prepare never asks an original Visual, Brush or
/// Drawing for new state and never borrows the receiving window's frame.
/// </summary>
internal abstract class WpfRecordedShaderSampler(int register, TextureSamplingMode sampling) : IDisposable
{
    protected int Register { get; } = register;
    protected TextureSamplingMode Sampling { get; } = sampling;
    internal abstract WpfShaderEffectSampler Prepare(ShaderEffectPreparationContext context);
    public abstract void Dispose();

    internal sealed class Image(int register, TextureSamplingMode sampling, WpfOwnedShaderImage image)
        : WpfRecordedShaderSampler(register, sampling)
    {
        internal override WpfShaderEffectSampler Prepare(ShaderEffectPreparationContext context)
        {
            if (!ReferenceEquals(image.GpuTexture.Context, context.Compositor.Context))
                throw new InvalidOperationException("The recorded shader image belongs to another device.");
            return WpfShaderEffectSampler.FromOwnedTexture(Register, image.Owner, Sampling);
        }
        public override void Dispose() => image.Dispose();
    }

    internal sealed class Cache(int register, TextureSamplingMode sampling, object sourceIdentity,
        WpfBitmapCacheBrushCapture capture, PortableBitmapCacheRasterPolicy policy,
        WgpuContext device, object deviceIdentity) : WpfRecordedShaderSampler(register, sampling)
    {
        internal override WpfShaderEffectSampler Prepare(ShaderEffectPreparationContext context)
        {
            if (!ReferenceEquals(device, context.Compositor.Context) ||
                !ReferenceEquals(deviceIdentity, context.DeviceIdentity))
                throw new InvalidOperationException("The source cache recording belongs to another device generation.");
            var b = capture.Bounds;
            if (!CacheSamplerRasterFrame.TryCreate(b.X, b.Y, b.Width, b.Height,
                capture.CachePolicy.RenderAtScale, policy.PrimaryDpiScaleX, policy.PrimaryDpiScaleY,
                policy.MaximumTextureWidth, policy.MaximumTextureHeight, out var frame))
                throw new NotSupportedException("The retained source cache raster frame is unavailable.");
            using var raster = context.Compositor.CaptureCacheSampler(capture.Picture, frame,
                sourceIdentity, policy.SourceRevision, capture.CachePolicy.EnableClearType);
            return WpfShaderEffectSampler.FromCacheRaster(Register, raster, Sampling);
        }
        public override void Dispose() => capture.Dispose();
    }

    internal sealed class Tile(int register, TextureSamplingMode sampling, PortableTileBrush brush,
        WpfDrawingReplay.RecordedTileContent? content, WpfOwnedShaderImage? image,
        WgpuContext device, object deviceIdentity, WpfImageSourceFrame? imageFrame) : WpfRecordedShaderSampler(register, sampling)
    {
        internal override WpfShaderEffectSampler Prepare(ShaderEffectPreparationContext context)
        {
            if (!ReferenceEquals(device, context.Compositor.Context) ||
                !ReferenceEquals(deviceIdentity, context.DeviceIdentity))
                throw new InvalidOperationException("The source brush recording belongs to another device generation.");
            uint width = context.CaptureFrame.PixelWidth, height = context.CaptureFrame.PixelHeight;
            if (width == 0 || height == 0 || width > 4096 || height > 4096)
                throw new NotSupportedException("The actual nested shader sampler exceeds the existing capture budget.");
            var visual = new global::ProGPU.Scene.DrawingVisual { Size = new Vector2(width, height),
                ClipBounds = new SceneRect(0, 0, width, height) };
            ProGpuCompositionCommandSink? sink = null;
            GpuTexture? texture = null;
            OwnedShaderEffectTexture? owner = null;
            WpfShaderEffectSampler? candidate = null;
            Exception? failure = null;
            try
            {
                sink = new ProGpuCompositionCommandSink(visual.Context, device, null);
                texture = new GpuTexture(device, width, height, TextureFormat.Rgba8Unorm,
                    TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                    "Owned nested source shader sampler");
                var status = WpfDrawingReplay.ReplayRecordedShaderTile(brush, content, image,
                    new Rect(0, 0, width, height), sink, imageFrame);
                if (status is not (WpfDrawingReplayStatus.Applied or WpfDrawingReplayStatus.Skipped) ||
                    sink.UnsupportedStateCount != 0)
                    throw new NotSupportedException("The recorded shader brush mapping is unsupported.");
                context.Compositor.RenderOffscreen(visual, width, height, texture, padding: 0,
                    dpiScale: 1, includeRootTransform: false, includeRootVisualState: false);
                owner = new OwnedShaderEffectTexture(texture);
                candidate = WpfShaderEffectSampler.FromOwnedTexture(Register, owner, Sampling);
            }
            catch (Exception error) { failure = error; }
            WpfShaderRecordingCleanup.Dispose(sink, ref failure);
            try { visual.Context.Clear(); }
            catch (Exception error)
            {
                if (failure is null) failure = error;
                else try { failure.Data["NestedSamplerCommandCleanupFailure"] = error; } catch { }
            }
            WpfShaderRecordingCleanup.Dispose(owner, ref failure);
            if (owner is null) WpfShaderRecordingCleanup.Dispose(texture, ref failure);
            if (failure is not null)
            {
                WpfShaderRecordingCleanup.Dispose(candidate, ref failure);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return candidate!;
        }
        public override void Dispose()
        {
            Exception? failure = null;
            WpfShaderRecordingCleanup.Dispose(content, ref failure);
            WpfShaderRecordingCleanup.Dispose(image, ref failure);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    // A discarded complete-ownership walk must never realize GPU resources.
    // Such a recipe cannot escape into a successfully published preparation.
    internal sealed class Proof : WpfRecordedShaderSampler
    {
        internal Proof() : base(0, TextureSamplingMode.Linear) { }
        internal override WpfShaderEffectSampler Prepare(ShaderEffectPreparationContext context) =>
            throw new InvalidOperationException("An ownership-only shader proof cannot be rendered.");
        public override void Dispose() { }
    }
}

internal sealed class WpfShaderRecordingImageSourceAdapter : IWpfImageSourceAdapter,
    IWpfShaderRecordingAdapterSource, IDisposable
{
    private readonly IWpfImageSourceAdapter? _inner;
    private readonly WgpuContext _context;
    private readonly WpfViewport3DTextureCache? _viewportCache;
    private readonly Func<PortableBitmapCacheRasterPolicy>? _cachePolicy;
    private readonly Dictionary<object, WpfOwnedShaderImage> _images = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    internal WpfShaderRecordingImageSourceAdapter(IWpfImageSourceAdapter? inner, WgpuContext context,
        WpfViewport3DTextureCache? viewportCache, Func<PortableBitmapCacheRasterPolicy>? cachePolicy)
    {
        _inner = inner; _context = context; _viewportCache = viewportCache; _cachePolicy = cachePolicy;
    }

    public WpfShaderRecordingImageSourceAdapter CreateShaderRecordingAdapter() =>
        new(_inner, _context, _viewportCache, _cachePolicy);

    public bool RecordsOwnedShaderImages => true;

    public ImageSource? AdaptImageSource(object? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (value is null) return null;
        if (_images.TryGetValue(value, out var image)) return image;
        var adapted = _inner?.AdaptImageSource(value) ?? value as ImageSource;
        if (adapted is null || WpfCaptureReplayGuard.ValidateHiddenSources) return adapted;
        var snapshot = WpfOwnedShaderImage.Capture(adapted, _context);
        try { _images.Add(value, snapshot); }
        catch { snapshot.Dispose(); throw; }
        return snapshot;
    }

    internal WpfRecordedShaderSampler CaptureSampler(PortableShaderSampler sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mode = sampler.SamplingMode == PortableShaderSamplingMode.NearestNeighbor
            ? TextureSamplingMode.Nearest : TextureSamplingMode.Linear;
        if (sampler.Kind == PortableShaderSamplerKind.ImageSource && sampler.Brush is null)
        {
            var image = _inner?.AdaptImageSource(sampler.ImageSource) ?? sampler.ImageSource as ImageSource;
            if (image is null) throw new NotSupportedException("The original shader image is unavailable.");
            if (WpfCaptureReplayGuard.ValidateHiddenSources)
            {
                if (!WpfBitmapSourceImageAdapter.TryGetGpuTexture(image, out _))
                    throw new NotSupportedException("The original shader image has no actual texture.");
                return new WpfRecordedShaderSampler.Proof();
            }
            return new WpfRecordedShaderSampler.Image(sampler.RegisterIndex, mode,
                WpfOwnedShaderImage.Capture(image, _context));
        }
        if (sampler.Brush is IPortableBitmapCacheBrushSource cache)
        {
            if (_cachePolicy is null) throw new NotSupportedException("The raw cache policy is unavailable.");
            var policy = _cachePolicy();
            if (!policy.IsValid) throw new NotSupportedException("The raw cache policy is invalid.");
            var capture = WpfBitmapCacheBrushCapture.CreateShaderSource(cache, _context, _viewportCache, this);
            return new WpfRecordedShaderSampler.Cache(sampler.RegisterIndex, mode, sampler.Brush,
                capture, policy, _context, _context.DeviceIdentity);
        }
        if (sampler.Brush is not IPortableTileBrushSource source || !source.TryGetPortableTileBrush(out var tile) ||
            tile.Kind is not (PortableTileBrushKind.Image or PortableTileBrushKind.Visual))
            throw new NotSupportedException("The original shader sampler family is unsupported.");
        WpfDrawingReplay.RecordedTileContent? content = null;
        WpfOwnedShaderImage? bitmap = null;
        WpfImageSourceFrame? imageFrame = null;
        try
        {
            if (tile.Kind == PortableTileBrushKind.Visual || tile.Content is IPortableDrawingImageSource)
                content = RecordContent(tile);
            else
            {
                var image = _inner?.AdaptImageSource(tile.Content) ?? tile.Content as ImageSource;
                if (image is null) throw new NotSupportedException("The original ImageBrush image is unavailable.");
                if (WpfImageSourceFrame.HasTypedMetrics(tile.Content!))
                {
                    if (!WpfImageSourceFrame.TryRead(tile.Content!, image, out var frame))
                        throw new NotSupportedException("The original ImageBrush source metrics are unavailable.");
                    imageFrame = frame;
                }
                if (WpfCaptureReplayGuard.ValidateHiddenSources)
                {
                    if (!WpfBitmapSourceImageAdapter.TryGetGpuTexture(image, out _))
                        throw new NotSupportedException("The original ImageBrush has no actual texture.");
                    return new WpfRecordedShaderSampler.Proof();
                }
                bitmap = WpfOwnedShaderImage.Capture(image, _context);
            }
            return new WpfRecordedShaderSampler.Tile(sampler.RegisterIndex, mode, tile,
                content, bitmap, _context, _context.DeviceIdentity, imageFrame);
        }
        catch (Exception failure)
        {
            Exception? preserved = failure;
            WpfShaderRecordingCleanup.Dispose(content, ref preserved);
            WpfShaderRecordingCleanup.Dispose(bitmap, ref preserved);
            throw;
        }
    }

    private WpfDrawingReplay.RecordedTileContent RecordContent(PortableTileBrush tile)
    {
        if (WpfCaptureReplayGuard.ValidateHiddenSources) return RecordContentCore(tile);
        WpfDrawingReplay.RecordedTileContent proof;
        using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true))
            proof = RecordContentCore(tile);
        using (proof)
        {
            var recording = RecordContentCore(tile);
            proof.TryGetBounds(out var before, out var beforeEmpty);
            recording.TryGetBounds(out var after, out var afterEmpty);
            if (before != after || beforeEmpty != afterEmpty)
            {
                recording.Dispose();
                throw new InvalidOperationException("The original sampler source frame changed during owned recording.");
            }
            return recording;
        }
    }

    private WpfDrawingReplay.RecordedTileContent RecordContentCore(PortableTileBrush tile)
    {
        using var capture = WpfCaptureReplayGuard.Begin();
        object? source = tile.Content;
        bool visual = tile.Kind == PortableTileBrushKind.Visual;
        Rect bounds = Rect.Empty;
        bool empty = true;
        if (!visual && source is IPortableDrawingImageSource image)
        {
            if (!image.TryGetPortableDrawingImage(out var drawing) && drawing is not null)
                throw new NotSupportedException("The original DrawingImage source returned inconsistent content.");
            // The original exporter returns false with null when Drawing is
            // cleared. This is known empty, unlike an unavailable bounds result
            // for a drawing that is actually present. Getter errors propagate.
            source = drawing;
        }
        if (source is not null)
        {
            if (visual)
            {
                if (source is not IPortableVisualBoundsSource typed ||
                    !typed.TryGetPortableVisualBounds(out var value) ||
                    (!value.HasDescendantBounds && !value.HasContentBounds))
                    throw new NotSupportedException("The actual recorded Visual bounds are unavailable.");
                var b = value.HasDescendantBounds ? value.DescendantBounds : value.ContentBounds;
                empty = b.IsEmpty;
                if (!empty) bounds = new Rect(b.X, b.Y, b.Width, b.Height);
            }
            else if (!WpfDrawingReplay.TryGetDrawingBounds(source, AdaptImageSource, out bounds, out empty))
                throw new NotSupportedException("The actual recorded DrawingImage bounds are unavailable.");
        }
        if (!empty && (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0))
            throw new NotSupportedException("The actual recorded source bounds are outside the existing domain.");
        var recorder = new GpuPictureRecorder();
        var commands = recorder.BeginRecording(empty ? default :
            new SceneRect((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height));
        try
        {
            if (source is not null)
            {
                using var sink = new ProGpuCompositionCommandSink(commands, _context, _viewportCache);
                if (visual)
                {
                    var result = new WpfVisualTreeRenderer().ReplaySubtree(source, sink,
                        resources: null, imageSourceAdapter: this);
                    if (result.UnsupportedContentCount != 0 || result.UnsupportedVisualStateCount != 0 ||
                        result.RenderData.UnsupportedCount != 0)
                        throw new NotSupportedException("The recorded shader Visual has unsupported owned content.");
                }
                else if (WpfDrawingReplay.Replay(source, sink, AdaptImageSource) is
                    WpfDrawingReplayStatus.Unsupported or WpfDrawingReplayStatus.PartiallyApplied)
                    throw new NotSupportedException("The recorded shader DrawingImage has unsupported owned content.");
                if (sink.UnsupportedStateCount != 0)
                    throw new NotSupportedException("The original shader source contains unsupported recording commands.");
            }
            return new WpfDrawingReplay.RecordedTileContent(recorder.EndRecording(), bounds, empty);
        }
        finally { commands.Clear(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Exception? first = null;
        foreach (var image in _images.Values)
        {
            try { image.Dispose(); }
            catch (Exception failure) { first ??= failure; }
        }
        _images.Clear();
        if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }
}
