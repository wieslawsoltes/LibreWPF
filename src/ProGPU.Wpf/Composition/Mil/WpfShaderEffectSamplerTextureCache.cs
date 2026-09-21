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
    private bool _isDisposed;

    public WpfShaderEffectSamplerTextureCache(
        WgpuContext context,
        ProGpuCompositor compositor,
        WpfViewport3DTextureCache viewport3DTextureCache)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _viewport3DTextureCache = viewport3DTextureCache ?? throw new ArgumentNullException(nameof(viewport3DTextureCache));
    }

    public bool TryCreateSampler(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfShaderEffectSampler sampler)
    {
        ThrowIfDisposed();
        sampler = null!;

        MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return imageSourceAdapter?.AdaptImageSource(imageSource);
        }

        if (brush == null
            || !IsSupportedShaderSamplerBrush(brush)
            || !TryGetBrushSourceBounds(brush, AdaptImageSource, out var sourceBounds)
            || !TryCreateTextureBounds(sourceBounds, out var textureBounds, out var pixelWidth, out var pixelHeight))
        {
            return false;
        }

        var entry = GetOrCreateEntry(brush, pixelWidth, pixelHeight);
        if (!RenderBrushToTexture(brush, textureBounds, entry.Texture, imageSourceAdapter))
        {
            return false;
        }

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
        _isDisposed = true;
    }

    private TextureEntry GetOrCreateEntry(object brush, uint pixelWidth, uint pixelHeight)
    {
        if (!_entries.TryGetValue(brush, out var entry))
        {
            entry = new TextureEntry(_context, pixelWidth, pixelHeight);
            _entries.Add(brush, entry);
            return entry;
        }

        entry.EnsureSize(pixelWidth, pixelHeight);
        return entry;
    }

    private bool RenderBrushToTexture(
        object brush,
        Rect textureBounds,
        GpuTexture texture,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        var visual = new ProGpuDrawingVisual
        {
            Size = new Vector2(texture.Width, texture.Height)
        };

        using var drawingContext = new MediaDrawingContext(visual.Context);
        using var sink = new ProGpuCompositionCommandSink(
            drawingContext,
            _context,
            _viewport3DTextureCache);

        var drawing = new ShaderSamplerGeometryDrawing(textureBounds, brush);
        MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return imageSourceAdapter?.AdaptImageSource(imageSource);
        }

        var replayStatus = WpfDrawingReplay.Replay(
            drawing,
            sink,
            AdaptImageSource);

        if (replayStatus != WpfDrawingReplayStatus.Applied)
        {
            return false;
        }

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

    private static bool IsSupportedShaderSamplerBrush(object brush)
    {
        return brush is PortableTileBrushSource portableSource
            && portableSource.TryGetPortableTileBrush(out var portableBrush)
            && (portableBrush.Kind == PortableTileBrushKind.Drawing
                || portableBrush.Kind == PortableTileBrushKind.Visual);
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

        public void EnsureSize(uint width, uint height)
        {
            if (Texture.Width == width && Texture.Height == height)
            {
                return;
            }

            Texture.Dispose();
            Texture = CreateTexture(width, height);
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
    IWpfShaderEffectSamplerBrushAdapter
{
    private readonly IWpfImageSourceAdapter? _inner;
    private readonly WpfShaderEffectSamplerTextureCache _samplerTextureCache;

    public WpfShaderEffectSamplerImageSourceAdapter(
        IWpfImageSourceAdapter? inner,
        WpfShaderEffectSamplerTextureCache samplerTextureCache)
    {
        _inner = inner;
        _samplerTextureCache = samplerTextureCache ?? throw new ArgumentNullException(nameof(samplerTextureCache));
    }

    public MediaImageSource? AdaptImageSource(object? imageSource)
    {
        return _inner?.AdaptImageSource(imageSource);
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
