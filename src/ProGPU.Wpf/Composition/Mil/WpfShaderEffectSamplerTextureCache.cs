using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using ProGPU.Backend;
using ProGPU.Scene;
using Silk.NET.WebGPU;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaImageSource = System.Windows.Media.ImageSource;
using ProGpuCompositor = ProGPU.Scene.Compositor;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
using ProGpuRect = ProGPU.Scene.Rect;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableGeometryDrawingState = ProGPU.Wpf.Interop.PortableGeometryDrawingState;
using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableTileBrush = ProGPU.Wpf.Interop.PortableTileBrush;
using PortableTileBrushKind = ProGPU.Wpf.Interop.PortableTileBrushKind;
using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;
using PortableDrawingImageSource = ProGPU.Wpf.Interop.IPortableDrawingImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal sealed class WpfShaderEffectSamplerTextureCache : IDisposable
{
    private const int MaxSamplerTextureDimension = 4096;

    private readonly WgpuContext _context;
    private readonly ProGpuCompositor _compositor;
    private readonly WpfViewport3DTextureCache _viewport3DTextureCache;
    // Shader brushes can be created transiently by templates, animations, and
    // effects. The retained scene owns the resulting texture while it is in
    // use, so this adapter must not independently keep every source brush alive
    // until the entire composition target is cleared.
    private readonly ConditionalWeakTable<object, TextureEntry> _entries = new();
    private readonly ConditionalWeakTable<object, ConditionalWeakTable<object, TextureEntry>> _effectEntries = new();
    private readonly ConditionalWeakTable<object, RawCacheEntry> _rawCacheEntries = new();
    private readonly Func<global::ProGPU.Wpf.Interop.PortableBitmapCacheRasterPolicy>? _getCacheRasterPolicy;
    private bool _isDisposed;

    public WpfShaderEffectSamplerTextureCache(
        WgpuContext context,
        ProGpuCompositor compositor,
        WpfViewport3DTextureCache viewport3DTextureCache,
        Func<global::ProGPU.Wpf.Interop.PortableBitmapCacheRasterPolicy>? getCacheRasterPolicy = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _viewport3DTextureCache = viewport3DTextureCache ?? throw new ArgumentNullException(nameof(viewport3DTextureCache));
        _getCacheRasterPolicy = getCacheRasterPolicy;
    }

    internal bool HasRawCacheSamplers => _rawCacheEntries.Any();

    internal WpfShaderRecordingImageSourceAdapter CreateRecordingAdapter(IWpfImageSourceAdapter? inner) =>
        new(inner, _context, _viewport3DTextureCache, _getCacheRasterPolicy);

    internal bool HasSourceTargetFrame(WpfShaderEffectTargetFrame frame) =>
        _effectEntries.All(owner => owner.Value.All(entry =>
            entry.Value.SourceTargetFrame is not { } captured || captured == frame));

    internal bool HasRawCachePolicy(global::ProGPU.Wpf.Interop.PortableBitmapCacheRasterPolicy policy) =>
        _rawCacheEntries.All(entry => entry.Value.Policy.Equals(policy));

    public bool TryCreateSampler(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfShaderEffectSampler sampler)
        => TryCreateSamplerCore(brush, registerIndex, samplingMode, imageSourceAdapter, null, null, out sampler);

    internal bool TryCreateSampler(object? brush, int registerIndex, TextureSamplingMode samplingMode,
        IWpfImageSourceAdapter? imageSourceAdapter, WpfShaderEffectSamplerFrame request, float dpiScale,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (request.SourceCapture.HasValue || request.Owner is null || !EffectCaptureFrame.TryCreate(request.ContentBounds, request.Padding,
                dpiScale, out var frame)) return false;
        return TryCreateSamplerCore(brush, registerIndex, samplingMode, imageSourceAdapter,
            request.Owner, frame, out sampler);
    }

    internal bool TryCreateSourceSampler(object? brush, int registerIndex, TextureSamplingMode samplingMode,
        IWpfImageSourceAdapter? imageSourceAdapter, WpfShaderEffectSamplerFrame request, float dpiScale,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (request.Owner is null || request.SourceCapture is not { } source ||
            request.TargetFrame is not { } targetFrame || !targetFrame.TryGetPixelsPerUnit(out var pixelsPerUnit) ||
            !EffectCaptureFrame.TryCreateSource(source, Vector2.Zero, pixelsPerUnit, dpiScale, out var frame)) return false;
        return TryCreateSamplerCore(brush, registerIndex, samplingMode, imageSourceAdapter,
            request.Owner, frame, out sampler, targetFrame);
    }

    private bool TryCreateSamplerCore(object? brush, int registerIndex, TextureSamplingMode samplingMode,
        IWpfImageSourceAdapter? imageSourceAdapter, object? effectOwner, EffectCaptureFrame? effectFrame,
        out WpfShaderEffectSampler sampler, WpfShaderEffectTargetFrame? sourceTargetFrame = null)
    {
        ThrowIfDisposed();
        sampler = null!;

        if (brush is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource cacheSource)
        {
            if (!cacheSource.TryGetPortableBitmapCacheBrush(out var cache) ||
                !global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryResolve(cache, out _) ||
                (cache.InternalTarget is { } target && !TryGetCacheSourceBounds(target, out _)))
                return false;
            if (_getCacheRasterPolicy is null)
                throw new NotSupportedException("Raw cache samplers require the actual primary display and owned device policy.");
            var policy = _getCacheRasterPolicy();
            if (!policy.IsValid) throw new NotSupportedException("The owned cache raster policy is unavailable.");
            using var capture = WpfBitmapCacheBrushCapture.CreateShaderSource(new CapturedBitmapCacheBrush(cache),
                _context, _viewport3DTextureCache, imageSourceAdapter);
            var bounds = capture.Bounds;
            if (!CacheSamplerRasterFrame.TryCreate(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                capture.CachePolicy.RenderAtScale, policy.PrimaryDpiScaleX, policy.PrimaryDpiScaleY,
                policy.MaximumTextureWidth, policy.MaximumTextureHeight, out var rasterFrame))
                throw new NotSupportedException("The original cache raster frame is outside the shared renderer contract.");
            _rawCacheEntries.TryGetValue(brush, out var previous);
            ulong revision = checked((previous?.Raster.SourceRevision ?? 0) + 1);
            CacheSamplerRaster raster = _compositor.CaptureCacheSampler(capture.Picture, rasterFrame,
                brush, revision, capture.CachePolicy.EnableClearType);
            WpfShaderEffectSampler candidate;
            try
            {
                candidate = WpfShaderEffectSampler.FromCacheRaster(registerIndex, raster, samplingMode);
            }
            catch (Exception failure)
            {
                try { raster.Dispose(); }
                catch (Exception cleanup) { try { failure.Data["CacheRasterCleanupFailure"] = cleanup; } catch { } }
                throw;
            }
            try
            {
                if (previous is null) _rawCacheEntries.Add(brush, new RawCacheEntry(raster, policy));
                else previous.Replace(raster, policy);
            }
            catch (Exception failure)
            {
                try { candidate.Dispose(); }
                catch (Exception cleanup) { try { failure.Data["CacheSamplerCleanupFailure"] = cleanup; } catch { } }
                try { raster.Dispose(); }
                catch (Exception cleanup) { try { failure.Data["CacheRasterCleanupFailure"] = cleanup; } catch { } }
                throw;
            }
            sampler = candidate;
            return true;
        }

        MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return imageSourceAdapter?.AdaptImageSource(imageSource);
        }

        if (brush is not PortableTileBrushSource tileSource || !tileSource.TryGetPortableTileBrush(out var tile) ||
            tile.Kind is not (PortableTileBrushKind.Image or PortableTileBrushKind.Visual or PortableTileBrushKind.Drawing)) return false;
        Rect textureBounds;
        uint pixelWidth, pixelHeight;
        bool requiresEffectFrame = tile.Kind is PortableTileBrushKind.Image or PortableTileBrushKind.Visual;
        if (requiresEffectFrame)
        {
            if (effectOwner is null || effectFrame is not { } frame ||
                !TryGetEffectTextureBounds(frame, out textureBounds, out pixelWidth, out pixelHeight)) return false;
        }
        else if (!TryGetPortableBrushSourceBounds(tile, AdaptImageSource, out var sourceBounds) ||
            !TryCreateTextureBounds(sourceBounds, out textureBounds, out pixelWidth, out pixelHeight)) return false;

        var entry = GetOrCreateEntry(brush, pixelWidth, pixelHeight,
            requiresEffectFrame ? effectOwner : null);
        if (!RenderBrushToTexture(new CapturedTileBrush(tile), textureBounds, entry.Texture, imageSourceAdapter,
                allowSkipped: tile.Kind == PortableTileBrushKind.Visual && tile.Content is null))
        {
            return false;
        }

        entry.SourceTargetFrame = sourceTargetFrame;

        sampler = new WpfShaderEffectSampler(registerIndex, entry.Texture, samplingMode);
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();

        foreach (var entry in _entries)
        {
            entry.Value.Dispose();
        }

        _entries.Clear();
        foreach (var owner in _effectEntries)
            foreach (var entry in owner.Value) entry.Value.Dispose();
        _effectEntries.Clear();
        foreach (var entry in _rawCacheEntries) entry.Value.Dispose();
        _rawCacheEntries.Clear();
    }

    internal void GetMemoryDiagnostics(out int textureCount, out ulong textureBytes)
    {
        ThrowIfDisposed();

        textureCount = 0;
        textureBytes = 0;
        foreach (var entry in _entries)
        {
            GpuTexture texture = entry.Value.Texture;
            if (texture.IsDisposed)
            {
                continue;
            }

            textureCount++;
            textureBytes += (ulong)texture.Width * texture.Height * 4UL;
        }
        foreach (var owner in _effectEntries)
            foreach (var entry in owner.Value)
            {
                GpuTexture texture = entry.Value.Texture;
                if (texture.IsDisposed) continue;
                textureCount++;
                textureBytes += (ulong)texture.Width * texture.Height * 4UL;
            }
        foreach (var entry in _rawCacheEntries)
        {
            GpuTexture texture = entry.Value.Raster.Texture;
            if (texture.IsDisposed) continue;
            textureCount++;
            textureBytes += (ulong)texture.Width * texture.Height * 4UL;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            entry.Value.Dispose();
        }

        _entries.Clear();
        foreach (var owner in _effectEntries)
            foreach (var entry in owner.Value) entry.Value.Dispose();
        _effectEntries.Clear();
        foreach (var entry in _rawCacheEntries) entry.Value.Dispose();
        _rawCacheEntries.Clear();
        _isDisposed = true;
    }

    private TextureEntry GetOrCreateEntry(object brush, uint pixelWidth, uint pixelHeight, object? effectOwner)
    {
        var entries = effectOwner is null ? _entries : _effectEntries.GetValue(effectOwner, static _ => new());
        if (!entries.TryGetValue(brush, out var entry))
        {
            entry = new TextureEntry(_context, pixelWidth, pixelHeight);
            entries.Add(brush, entry);
            return entry;
        }

        entry.EnsureSize(pixelWidth, pixelHeight);
        return entry;
    }

    private bool RenderBrushToTexture(
        object brush,
        Rect textureBounds,
        GpuTexture texture,
        IWpfImageSourceAdapter? imageSourceAdapter, bool allowSkipped)
    {
        var visual = new ProGpuDrawingVisual
        {
            Size = new Vector2(texture.Width, texture.Height)
        };

        Exception? failure = null;
        try
        {
            using var drawingContext = new MediaDrawingContext(visual.Context);
            using var sink = new ProGpuCompositionCommandSink(
                drawingContext,
                _context,
                _viewport3DTextureCache);

            var drawing = new ShaderSamplerGeometryDrawing(textureBounds, brush);
            using var recordingAdapter = CreateRecordingAdapter(imageSourceAdapter);
            var replayStatus = WpfDrawingReplay.Replay(
                drawing,
                sink,
                recordingAdapter.AdaptImageSource);

            if (replayStatus != WpfDrawingReplayStatus.Applied &&
                !(allowSkipped && replayStatus == WpfDrawingReplayStatus.Skipped))
                return false;

            visual.ClipBounds = new ProGpuRect(0, 0, texture.Width, texture.Height);
            _compositor.RenderOffscreen(
                visual,
                texture.Width,
                texture.Height,
                texture,
                padding: 0f,
                dpiScale: 1f,
                includeRootTransform: false,
                includeRootVisualState: false);
            return true;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            // The compositor retains actual submission resources. This
            // temporary display list must release its new source/recipe leases
            // deterministically after realization, including a failed capture.
            try { visual.Context.Clear(); }
            catch (Exception cleanup) when (failure is not null)
            { try { failure.Data["ShaderSamplerRecordingCleanupFailure"] = cleanup; } catch { } }
        }
    }

    // One source snapshot owns both frame selection and replay. Reentrant
    // readers cannot replace its Visual or mapping between those operations.
    private sealed class CapturedTileBrush(PortableTileBrush brush) : PortableTileBrushSource
    {
        public bool TryGetPortableTileBrush(out PortableTileBrush value) { value = brush; return true; }
    }

    private sealed class CapturedBitmapCacheBrush(global::ProGPU.Wpf.Interop.PortableBitmapCacheBrush brush)
        : global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource
    {
        public bool TryGetPortableBitmapCacheBrush(out global::ProGPU.Wpf.Interop.PortableBitmapCacheBrush value)
        { value = brush; return true; }
    }

    private static bool TryGetCacheSourceBounds(object target, out bool empty)
    {
        empty = false;
        if (target is not global::ProGPU.Wpf.Interop.IPortableVisualBoundsSource source ||
            !source.TryGetPortableVisualBounds(out var bounds) ||
            (!bounds.HasDescendantBounds && !bounds.HasContentBounds)) return false;
        var rect = bounds.HasDescendantBounds ? bounds.DescendantBounds : bounds.ContentBounds;
        if (rect.IsEmpty) { empty = true; return true; }
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) || rect.Width < 0 || rect.Height < 0)
            return false;
        empty = rect.Width == 0 || rect.Height == 0;
        return true;
    }

    internal static bool TryGetBrushSourceBounds(object brush, out Rect bounds)
    {
        return TryGetBrushSourceBounds(brush, null, out bounds);
    }

    private static bool TryGetBrushSourceBounds(
        object brush,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect bounds)
    {
        if (brush is PortableTileBrushSource portableSource
            && portableSource.TryGetPortableTileBrush(out var portableBrush)
            && TryGetPortableBrushSourceBounds(portableBrush, imageSourceAdapter, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetPortableBrushSourceBounds(
        PortableTileBrush brush,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect bounds)
    {
        if (brush.Content is null)
        {
            // An absolute viewbox is mapping, not proof of live source bounds.
            bounds = default;
            return false;
        }
        if (brush.Kind == PortableTileBrushKind.Image)
        {
            Rect imageBounds;
            if (brush.Content is PortableDrawingImageSource drawingImage)
            {
                if (!drawingImage.TryGetPortableDrawingImage(out var drawing) || drawing is null ||
                    !WpfDrawingReplay.TryGetDrawingBounds(drawing, imageSourceAdapter, out imageBounds))
                { bounds = default; return false; }
            }
            else
            {
                MediaImageSource? adapted = imageSourceAdapter?.Invoke(brush.Content) ?? brush.Content as MediaImageSource;
                if (!WpfImageSourceFrame.TryRead(brush.Content, adapted, out var frame))
                { bounds = default; return false; }
                imageBounds = frame.Bounds;
            }
            return WpfImageSourceFrame.TryMapViewbox(brush, imageBounds, out bounds);
        }

        if (TryGetAbsoluteViewbox(brush, out bounds))
        {
            return true;
        }

        switch (brush.Kind)
        {
            case PortableTileBrushKind.Drawing:
                if (WpfDrawingReplay.TryGetDrawingBounds(brush.Content, imageSourceAdapter, out var drawingBounds))
                {
                    if (TryGetRelativeViewbox(brush, drawingBounds, out bounds))
                    {
                        return true;
                    }

                    bounds = drawingBounds;
                    return true;
                }

                break;

            case PortableTileBrushKind.Visual:
                if (TryGetSamplerVisualBounds(brush.Content, out var visualBounds))
                {
                    if (TryGetRelativeViewbox(brush, visualBounds, out bounds))
                    {
                        return true;
                    }

                    bounds = visualBounds;
                    return true;
                }

                break;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetAbsoluteViewbox(PortableTileBrush brush, out Rect viewbox)
    {
        viewbox = default;
        if (brush.ViewboxUnits != PortableBrushMappingMode.Absolute)
        {
            return false;
        }

        viewbox = ToRect(brush.Viewbox);
        return IsUsableBounds(viewbox);
    }

    private static bool TryGetRelativeViewbox(PortableTileBrush brush, Rect sourceBounds, out Rect viewbox)
    {
        viewbox = default;
        if (brush.ViewboxUnits != PortableBrushMappingMode.RelativeToBoundingBox
            || !IsUsableBounds(sourceBounds))
        {
            return false;
        }

        var relativeViewbox = ToRect(brush.Viewbox);
        if (!IsUsableBounds(relativeViewbox))
        {
            return false;
        }

        viewbox = new Rect(
            sourceBounds.X + relativeViewbox.X * sourceBounds.Width,
            sourceBounds.Y + relativeViewbox.Y * sourceBounds.Height,
            relativeViewbox.Width * sourceBounds.Width,
            relativeViewbox.Height * sourceBounds.Height);
        return IsUsableBounds(viewbox);
    }

    private static Rect ToRect(PortableRect rect)
    {
        return rect.IsEmpty
            ? Rect.Empty
            : new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static bool TryGetSamplerVisualBounds(object visual, out Rect bounds)
    {
        return WpfDrawingReplay.TryGetVisualBounds(visual, out bounds);
    }

    private static bool TryCreateTextureBounds(
        Rect sourceBounds,
        out Rect textureBounds,
        out uint pixelWidth,
        out uint pixelHeight)
    {
        textureBounds = default;
        pixelWidth = 0;
        pixelHeight = 0;

        if (!IsUsableBounds(sourceBounds))
        {
            return false;
        }

        pixelWidth = ClampTextureDimension(sourceBounds.Width);
        pixelHeight = ClampTextureDimension(sourceBounds.Height);
        textureBounds = new Rect(0, 0, pixelWidth, pixelHeight);
        return true;
    }

    private static uint ClampTextureDimension(double value)
    {
        return (uint)Math.Clamp((int)Math.Ceiling(value), 1, MaxSamplerTextureDimension);
    }

    internal static bool TryGetEffectTextureBounds(EffectCaptureFrame frame,
        out Rect textureBounds, out uint pixelWidth, out uint pixelHeight)
    {
        textureBounds = default;
        pixelWidth = pixelHeight = 0;
        if (frame.PixelWidth is 0 or > MaxSamplerTextureDimension ||
            frame.PixelHeight is 0 or > MaxSamplerTextureDimension) return false;
        pixelWidth = frame.PixelWidth;
        pixelHeight = frame.PixelHeight;
        // Original shader ImageBrush realization is identity mapped across the
        // complete physical implicit input, not the intrinsic image/viewbox.
        textureBounds = new Rect(0, 0, pixelWidth, pixelHeight);
        return true;
    }

    private static bool IsUsableBounds(Rect bounds)
    {
        return !bounds.IsEmpty
            && bounds.Width > 0
            && bounds.Height > 0
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private sealed class TextureEntry : IDisposable
    {
        private readonly WgpuContext _context;

        public TextureEntry(WgpuContext context, uint width, uint height)
        {
            _context = context;
            Texture = CreateTexture(width, height);
        }

        public GpuTexture Texture { get; private set; }

        public WpfShaderEffectTargetFrame? SourceTargetFrame { get; set; }

        public void EnsureSize(uint width, uint height)
        {
            if (Texture.Width == width && Texture.Height == height)
            {
                return;
            }

            GpuTexture replacement = CreateTexture(width, height);
            GpuTexture previous = Texture;
            Texture = replacement;
            previous.Dispose(); // existing GpuTexture/context submission retirement
        }

        public void Dispose()
        {
            Texture.Dispose();
        }

        private GpuTexture CreateTexture(uint width, uint height)
        {
            return new GpuTexture(
                _context,
                width,
                height,
                TextureFormat.Rgba8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                "WPF ShaderEffect Brush Sampler Texture");
        }
    }

    private sealed class RawCacheEntry(CacheSamplerRaster raster,
        global::ProGPU.Wpf.Interop.PortableBitmapCacheRasterPolicy policy) : IDisposable
    {
        internal CacheSamplerRaster Raster { get; private set; } = raster;
        internal global::ProGPU.Wpf.Interop.PortableBitmapCacheRasterPolicy Policy { get; private set; } = policy;
        internal void Replace(CacheSamplerRaster replacement,
            global::ProGPU.Wpf.Interop.PortableBitmapCacheRasterPolicy replacementPolicy)
        {
            CacheSamplerRaster previous = Raster;
            // Enqueue before publication: a failed enqueue leaves the prior
            // owner intact, and source cleanup callbacks cannot reenter midway
            // through candidate replacement. The existing render/context drain
            // releases this owner; retained parameters have independent leases.
            previous.Texture.Context.QueueExternalTextureOwnerDisposal(previous);
            Raster = replacement; Policy = replacementPolicy;
        }
        public void Dispose() => Raster.Dispose();
    }

    private sealed class ShaderSamplerGeometryDrawing : PortableGeometryDrawingStateSource
    {
        public ShaderSamplerGeometryDrawing(Rect geometryBounds, object brush)
        {
            GeometryBounds = geometryBounds;
            Brush = brush;
        }

        public Rect GeometryBounds { get; }

        public object Brush { get; }

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasGeometry = true,
                Geometry = GeometryBounds,
                HasBrush = true,
                Brush = Brush
            };
            return true;
        }
    }
}

internal sealed class WpfShaderEffectSamplerImageSourceAdapter :
    IWpfImageSourceAdapter,
    IWpfShaderEffectSamplerBrushAdapter,
    IWpfShaderRecordingAdapterSource
{
    private readonly IWpfImageSourceAdapter? _inner;
    private readonly WpfShaderEffectSamplerTextureCache _samplerTextureCache;

    internal float DpiScale { get; }

    internal WpfShaderEffectTargetFrame? TargetFrame { get; }

    internal bool UsesCache(WpfShaderEffectSamplerTextureCache cache) => ReferenceEquals(_samplerTextureCache, cache);

    internal IWpfImageSourceAdapter? SourceAdapter => _inner;

    public WpfShaderRecordingImageSourceAdapter CreateShaderRecordingAdapter() =>
        _samplerTextureCache.CreateRecordingAdapter(_inner);

    public WpfShaderEffectSamplerImageSourceAdapter(
        IWpfImageSourceAdapter? inner,
        WpfShaderEffectSamplerTextureCache samplerTextureCache,
        float dpiScale = 1f)
    {
        if (!float.IsFinite(dpiScale) || dpiScale <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScale));
        _inner = inner;
        _samplerTextureCache = samplerTextureCache ?? throw new ArgumentNullException(nameof(samplerTextureCache));
        DpiScale = dpiScale;
    }

    public WpfShaderEffectSamplerImageSourceAdapter(
        IWpfImageSourceAdapter? inner,
        WpfShaderEffectSamplerTextureCache samplerTextureCache,
        float dpiScale,
        WpfShaderEffectTargetFrame targetFrame) : this(inner, samplerTextureCache, dpiScale)
    {
        if (!targetFrame.TryGetPixelsPerUnit(out _)) throw new ArgumentOutOfRangeException(nameof(targetFrame));
        TargetFrame = targetFrame;
    }

    public MediaImageSource? AdaptImageSource(object? imageSource)
    {
        return _inner?.AdaptImageSource(imageSource);
    }

    public bool TryAdaptShaderEffectSamplerBrush(
        object? brush, int registerIndex, TextureSamplingMode samplingMode,
        WpfShaderEffectSamplerFrame frame, out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (frame.SourceCapture.HasValue) return false;
        frame = frame with { DpiScale = DpiScale };
        if (_inner is IWpfShaderEffectSamplerBrushAdapter innerSamplerAdapter &&
            innerSamplerAdapter.TryAdaptShaderEffectSamplerBrush(brush, registerIndex, samplingMode, frame, out sampler))
            return true;
        return _samplerTextureCache.TryCreateSampler(brush, registerIndex, samplingMode, this, frame, DpiScale, out sampler);
    }

    public bool TryAdaptSourceShaderEffectSamplerBrush(
        object? brush, int registerIndex, TextureSamplingMode samplingMode,
        WpfShaderEffectSamplerFrame frame, out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (frame.Owner is null || frame.SourceCapture is not { IsValid: true } || TargetFrame is not { } targetFrame) return false;
        frame = frame with { DpiScale = DpiScale, TargetFrame = targetFrame };
        if (_inner is IWpfShaderEffectSamplerBrushAdapter innerSamplerAdapter &&
            innerSamplerAdapter.TryAdaptSourceShaderEffectSamplerBrush(brush, registerIndex, samplingMode, frame, out sampler))
            return true;
        return _samplerTextureCache.TryCreateSourceSampler(brush, registerIndex, samplingMode, this, frame, DpiScale, out sampler);
    }

    public bool TryAdaptShaderEffectSamplerBrush(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler)
    {
        if (_inner is IWpfShaderEffectSamplerBrushAdapter innerSamplerAdapter
            && innerSamplerAdapter.TryAdaptShaderEffectSamplerBrush(
                brush,
                registerIndex,
                samplingMode,
                out sampler))
        {
            return true;
        }

        return _samplerTextureCache.TryCreateSampler(
            brush,
            registerIndex,
            samplingMode,
            this,
            out sampler);
    }
}
