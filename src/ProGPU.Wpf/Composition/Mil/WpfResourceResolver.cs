using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Text;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaMatrixTransform = System.Windows.Media.MatrixTransform;
using MediaPen = System.Windows.Media.Pen;
using MediaPenLineCap = System.Windows.Media.PenLineCap;
using MediaTransform = System.Windows.Media.Transform;
using PortableBrush = ProGPU.Wpf.Interop.PortableBrush;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableBrushKind = ProGPU.Wpf.Interop.PortableBrushKind;
using PortableColor = ProGPU.Wpf.Interop.PortableColor;
using PortableGradientColorInterpolationMode = ProGPU.Wpf.Interop.PortableGradientColorInterpolationMode;
using PortableGradientSpreadMethod = ProGPU.Wpf.Interop.PortableGradientSpreadMethod;
using PortableGradientStop = ProGPU.Wpf.Interop.PortableGradientStop;
using PortableFillRule = ProGPU.Wpf.Interop.PortableFillRule;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathKind = ProGPU.Wpf.Interop.PortableGeometryPathKind;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableGlyphRun = ProGPU.Wpf.Interop.PortableGlyphRun;
using PortableGlyphRunSource = ProGPU.Wpf.Interop.IPortableGlyphRunSource;
using PortableNativeGlyphRun = ProGPU.Wpf.Interop.PortableNativeGlyphRun;
using PortableNativeGlyphRunSource = ProGPU.Wpf.Interop.IPortableNativeGlyphRunSource;
using PortablePathSegment = ProGPU.Wpf.Interop.PortablePathSegment;
using PortablePathSegmentKind = ProGPU.Wpf.Interop.PortablePathSegmentKind;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableSweepDirection = ProGPU.Wpf.Interop.PortableSweepDirection;
using PortableBrushSource = ProGPU.Wpf.Interop.IPortableBrushSource;
using PortablePen = ProGPU.Wpf.Interop.PortablePen;
using PortablePenLineCap = ProGPU.Wpf.Interop.PortablePenLineCap;
using PortablePenLineJoin = ProGPU.Wpf.Interop.PortablePenLineJoin;
using PortablePenSource = ProGPU.Wpf.Interop.IPortablePenSource;
using PortableMatrix3x2 = ProGPU.Wpf.Interop.PortableMatrix3x2;
using PortableTransformMatrixSource = ProGPU.Wpf.Interop.IPortableTransformMatrixSource;
using PortableDashStyleSource = ProGPU.Wpf.Interop.IPortableDashStyleSource;
using MediaBrushMappingMode = System.Windows.Media.BrushMappingMode;
using MediaColorInterpolationMode = System.Windows.Media.ColorInterpolationMode;
using MediaGradientSpreadMethod = System.Windows.Media.GradientSpreadMethod;
using MediaGradientStop = System.Windows.Media.GradientStop;
using MediaGradientStopCollection = System.Windows.Media.GradientStopCollection;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaRadialGradientBrush = System.Windows.Media.RadialGradientBrush;
using WpfPoint = System.Windows.Point;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal enum ProGpuBrushMappingMode
{
    RelativeToBoundingBox,
    Absolute
}

internal readonly struct WpfNativeGlyphRun
{
    public WpfNativeGlyphRun(
        ushort[] glyphIndices,
        Vector2[] glyphPositions,
        TtfFont font,
        float fontSize,
        Vector2 position,
        Matrix4x4 transform,
        bool isBold,
        bool isItalic)
    {
        GlyphIndices = glyphIndices;
        GlyphPositions = glyphPositions;
        Font = font;
        FontSize = fontSize;
        Position = position;
        Transform = transform;
        IsBold = isBold;
        IsItalic = isItalic;

        if (TryCreateLocalBounds(glyphPositions, fontSize, position, out var localBounds))
        {
            HasBounds = true;
            LocalBounds = localBounds;
            TransformedBounds = TransformBounds(localBounds, transform);
        }
        else
        {
            HasBounds = false;
            LocalBounds = default;
            TransformedBounds = default;
        }
    }

    public ushort[] GlyphIndices { get; }

    public Vector2[] GlyphPositions { get; }

    public TtfFont Font { get; }

    public float FontSize { get; }

    public Vector2 Position { get; }

    public Matrix4x4 Transform { get; }

    public bool IsBold { get; }

    public bool IsItalic { get; }

    public bool HasBounds { get; }

    public WpfReplayRect LocalBounds { get; }

    public WpfReplayRect TransformedBounds { get; }

    private static bool TryCreateLocalBounds(
        Vector2[] glyphPositions,
        float fontSize,
        Vector2 position,
        out WpfReplayRect bounds)
    {
        bounds = default;
        if (!float.IsFinite(fontSize) ||
            fontSize <= 0f ||
            !float.IsFinite(position.X) ||
            !float.IsFinite(position.Y))
        {
            return false;
        }

        var minX = (double)position.X;
        var minY = position.Y - (double)fontSize;
        var maxX = (double)position.X;
        var maxY = (double)position.Y;

        if (glyphPositions.Length == 0)
        {
            maxX += fontSize;
        }
        else
        {
            var originX = position.X;
            var originY = position.Y;
            for (var i = 0; i < glyphPositions.Length; i++)
            {
                var glyphPosition = glyphPositions[i];
                if (!float.IsFinite(glyphPosition.X) || !float.IsFinite(glyphPosition.Y))
                {
                    return false;
                }

                var x = originX + (double)glyphPosition.X;
                var y = originY + (double)glyphPosition.Y;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y - fontSize);
                maxX = Math.Max(maxX, x + fontSize);
                maxY = Math.Max(maxY, y);
            }
        }

        var width = Math.Max(0, maxX - minX);
        var height = Math.Max(0, maxY - minY);
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bounds = new WpfReplayRect(minX, minY, width, height);
        return true;
    }

    private static WpfReplayRect TransformBounds(WpfReplayRect bounds, Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        var left = (float)bounds.X;
        var top = (float)bounds.Y;
        var right = (float)(bounds.X + bounds.Width);
        var bottom = (float)(bounds.Y + bounds.Height);

        var p0 = Vector2.Transform(new Vector2(left, top), transform);
        var p1 = Vector2.Transform(new Vector2(right, top), transform);
        var p2 = Vector2.Transform(new Vector2(right, bottom), transform);
        var p3 = Vector2.Transform(new Vector2(left, bottom), transform);

        var min = Vector2.Min(Vector2.Min(p0, p1), Vector2.Min(p2, p3));
        var max = Vector2.Max(Vector2.Max(p0, p1), Vector2.Max(p2, p3));
        if (!float.IsFinite(min.X) ||
            !float.IsFinite(min.Y) ||
            !float.IsFinite(max.X) ||
            !float.IsFinite(max.Y) ||
            max.X <= min.X ||
            max.Y <= min.Y)
        {
            return bounds;
        }

        return new WpfReplayRect(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    }
}

public sealed class WpfResourceResolver :
    IWpfMilResourceResolver,
    IWpfDrawingResourceResolver,
    IWpfGuidelineSetResourceResolver,
    IWpfRawMilResourceResolver,
    IWpfImageSourceAdapter
{
    private readonly struct WpfMatrix2D
    {
        public WpfMatrix2D(
            double m11,
            double m12,
            double m21,
            double m22,
            double offsetX,
            double offsetY)
        {
            M11 = m11;
            M12 = m12;
            M21 = m21;
            M22 = m22;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public static WpfMatrix2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

        public double M11 { get; }

        public double M12 { get; }

        public double M21 { get; }

        public double M22 { get; }

        public double OffsetX { get; }

        public double OffsetY { get; }
    }

    private const int MaxSupportedGradientStops = 65536;
    private const int MaxCachedFontFilePaths = 256;
    // Retained glyph-run DTOs keep their resolved font alive. The path cache
    // should accelerate resolution without permanently retaining every custom
    // font file an application has ever previewed.
    private static readonly WpfBoundedWeakValueCache<string, TtfFont> s_fontFileCache =
        new(MaxCachedFontFilePaths, StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<MediaBrush, NativeSolidBrushCache> s_nativeSolidBrushCache = new();
    private static readonly ConditionalWeakTable<MediaLinearGradientBrush, NativeLinearGradientBrushCache> s_nativeLinearGradientBrushCache = new();
    private static readonly ConditionalWeakTable<MediaRadialGradientBrush, NativeRadialGradientBrushCache> s_nativeRadialGradientBrushCache = new();
    private static readonly ConditionalWeakTable<MediaPen, NativeSolidPenCache> s_nativeSolidPenCache = new();
    private static readonly ConditionalWeakTable<PortableNativeGlyphRun, NativePortableNativeGlyphRunCache> s_nativePortableNativeGlyphRunCache = new();
    private static readonly ConditionalWeakTable<PortableGlyphRun, NativePortableGlyphRunCache> s_nativePortableGlyphRunCache = new();

    private readonly IReadOnlyList<object?>? _dependentResources;
    private Dictionary<uint, object>? _resources;
    private Dictionary<uint, MediaBrush?>? _brushes;
    private Dictionary<uint, MediaPen?>? _pens;
    private Dictionary<uint, MediaGeometry?>? _geometries;
    private Dictionary<uint, MediaImageSource?>? _imageSources;
    private Dictionary<uint, MediaGlyphRun?>? _glyphRuns;
    private Dictionary<uint, MediaTransform?>? _transforms;
    private readonly IWpfImageSourceAdapter? _imageSourceAdapter;

    public WpfResourceResolver()
    {
    }

    public WpfResourceResolver(IWpfImageSourceAdapter? imageSourceAdapter)
    {
        _imageSourceAdapter = imageSourceAdapter;
    }

    private WpfResourceResolver(
        IReadOnlyList<object?> dependentResources,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        _dependentResources = dependentResources;
        _imageSourceAdapter = imageSourceAdapter;
    }

    public static WpfResourceResolver FromDependentResources(
        IReadOnlyList<object?> dependentResources,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(dependentResources);
        return new WpfResourceResolver(dependentResources, imageSourceAdapter);
    }

    public void Register(uint resourceToken, object resource)
    {
        if (resourceToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resourceToken), "WPF MIL dependent resource tokens are one-based.");
        }

        ArgumentNullException.ThrowIfNull(resource);
        (_resources ??= new Dictionary<uint, object>())[resourceToken] = resource;
    }

    public MediaBrush? ResolveBrush(uint resourceToken)
    {
        return Resolve(resourceToken, ref _brushes, AdaptBrush);
    }

    public MediaPen? ResolvePen(uint resourceToken)
    {
        return Resolve(resourceToken, ref _pens, AdaptPen);
    }

    public MediaGeometry? ResolveGeometry(uint resourceToken)
    {
        return Resolve(resourceToken, ref _geometries, AdaptGeometry);
    }

    public MediaImageSource? ResolveImageSource(uint resourceToken)
    {
        return Resolve(resourceToken, ref _imageSources, AdaptImageSource);
    }

    public MediaGlyphRun? ResolveGlyphRun(uint resourceToken)
    {
        return Resolve(resourceToken, ref _glyphRuns, AdaptGlyphRun);
    }

    public MediaTransform? ResolveTransform(uint resourceToken)
    {
        return Resolve(resourceToken, ref _transforms, AdaptTransform);
    }

    public object? ResolveGuidelineSet(uint resourceToken)
    {
        return TryResolveResource(resourceToken, out var resource) ? resource : null;
    }

    bool IWpfRawMilResourceResolver.TryResolveRawResource(uint resourceToken, out object resource)
    {
        if (TryResolveResource(resourceToken, out var resolved) && resolved != null)
        {
            resource = resolved;
            return true;
        }

        resource = null!;
        return false;
    }

    public bool TryReplayDrawing(uint resourceToken, IWpfCompositionCommandSink sink)
    {
        var status = ReplayDrawing(resourceToken, sink);
        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    public WpfDrawingReplayStatus ReplayDrawing(uint resourceToken, IWpfCompositionCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (!TryResolveResource(resourceToken, out var drawing) || drawing == null)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        return WpfDrawingReplay.Replay(drawing, sink, AdaptImageSource);
    }

    private T? Resolve<T>(
        uint resourceToken,
        ref Dictionary<uint, T?>? cache,
        Func<object, T?> adapter)
        where T : class
    {
        if (resourceToken == 0)
        {
            return null;
        }

        if (cache != null && cache.TryGetValue(resourceToken, out var cached))
        {
            return cached;
        }

        var resolved = TryResolveResource(resourceToken, out var resource) && resource != null
            ? adapter(resource)
            : null;

        cache ??= new Dictionary<uint, T?>();
        cache[resourceToken] = resolved;
        return resolved;
    }

    private bool TryResolveResource(uint resourceToken, out object? resource)
    {
        if (resourceToken == 0)
        {
            resource = null;
            return false;
        }

        if (_resources != null && _resources.TryGetValue(resourceToken, out var registeredResource))
        {
            resource = registeredResource;
            return true;
        }

        if (_dependentResources != null)
        {
            var index = resourceToken - 1;
            if (index < (uint)_dependentResources.Count)
            {
                resource = _dependentResources[(int)index];
                return resource != null;
            }
        }

        resource = null;
        return false;
    }

    public static MediaBrush? AdaptBrush(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is MediaBrush brush)
        {
            return brush;
        }

        if (resource is PortableBrushSource portableBrushSource)
        {
            return portableBrushSource.TryGetPortableBrush(out var portableBrush)
                ? AdaptPortableBrush(portableBrush)
                : null;
        }

        return null;
    }

    internal static global::ProGPU.Vector.Brush? AdaptNativeBrush(
        object? resource,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = 0;
        if (resource == null)
        {
            return null;
        }

        if (TryGetCachedNativeSolidBrush(resource, out var nativeSolidBrush))
        {
            return nativeSolidBrush;
        }

        if (TryGetCachedNativeGradientBrush(resource, bounds, out var nativeGradientBrush, out unsupportedStateCount))
        {
            return nativeGradientBrush;
        }

        if (resource is PortableBrushSource portableBrushSource)
        {
            return portableBrushSource.TryGetPortableBrush(out var portableBrush)
                ? AdaptNativePortableBrush(portableBrush, bounds, out unsupportedStateCount)
                : null;
        }

        return null;
    }

    internal static global::ProGPU.Vector.Pen? AdaptNativePen(
        object? resource,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = 0;
        if (resource == null)
        {
            return null;
        }

        if (TryGetCachedNativeSolidPen(resource, out var nativeSolidPen))
        {
            return nativeSolidPen;
        }

        if (resource is PortablePenSource portablePenSource)
        {
            return portablePenSource.TryGetPortablePen(out var portablePen)
                ? AdaptNativePortablePen(portablePen, bounds, out unsupportedStateCount)
                : null;
        }

        return null;
    }

    private static bool TryGetCachedNativeSolidBrush(
        object? resource,
        out global::ProGPU.Vector.SolidColorBrush nativeBrush)
    {
        if (resource is SolidColorBrush solidBrush)
        {
            nativeBrush = s_nativeSolidBrushCache
                .GetValue(solidBrush, static _ => new NativeSolidBrushCache())
                .GetOrCreate(solidBrush);
            return true;
        }

        nativeBrush = null!;
        return false;
    }

    private static bool TryGetCachedNativeGradientBrush(
        object resource,
        WpfReplayRect bounds,
        out global::ProGPU.Vector.Brush nativeBrush,
        out int unsupportedStateCount)
    {
        if (resource is MediaLinearGradientBrush linearGradientBrush &&
            s_nativeLinearGradientBrushCache
                .GetValue(linearGradientBrush, static _ => new NativeLinearGradientBrushCache())
                .TryGetOrCreate(linearGradientBrush, bounds, out nativeBrush, out unsupportedStateCount))
        {
            return true;
        }

        if (resource is MediaRadialGradientBrush radialGradientBrush &&
            s_nativeRadialGradientBrushCache
                .GetValue(radialGradientBrush, static _ => new NativeRadialGradientBrushCache())
                .TryGetOrCreate(radialGradientBrush, bounds, out nativeBrush, out unsupportedStateCount))
        {
            return true;
        }

        nativeBrush = null!;
        unsupportedStateCount = 0;
        return false;
    }

    private static bool TryGetCachedNativeSolidPen(
        object resource,
        out global::ProGPU.Vector.Pen nativePen)
    {
        if (resource is MediaPen pen &&
            TryGetCachedNativeSolidBrush(pen.Brush, out var nativeBrush))
        {
            return s_nativeSolidPenCache
                .GetValue(pen, static _ => new NativeSolidPenCache())
                .TryGetOrCreate(pen, nativeBrush, out nativePen);
        }

        nativePen = null!;
        return false;
    }

    private sealed class NativeSolidBrushCache
    {
        private Color _color;
        private double _opacity = double.NaN;
        private global::ProGPU.Vector.SolidColorBrush? _nativeBrush;

        public global::ProGPU.Vector.SolidColorBrush GetOrCreate(SolidColorBrush brush)
        {
            var color = brush.Color;
            var opacity = ClampOpacity(brush.Opacity);
            if (_nativeBrush != null &&
                color.Equals(_color) &&
                opacity.Equals(_opacity))
            {
                return _nativeBrush;
            }

            _color = color;
            _opacity = opacity;
            _nativeBrush = new global::ProGPU.Vector.SolidColorBrush(ToVectorColor(color, opacity));
            return _nativeBrush;
        }
    }

    private sealed class NativeLinearGradientBrushCache
    {
        private WpfPoint _startPoint;
        private WpfPoint _endPoint;
        private double _opacity = double.NaN;
        private MediaBrushMappingMode _mappingMode;
        private MediaGradientSpreadMethod _spreadMethod;
        private MediaColorInterpolationMode _colorInterpolationMode;
        private bool _hasTransform;
        private bool _hasRelativeTransform;
        private Matrix4x4 _transform;
        private Matrix4x4 _relativeTransform;
        private Color[]? _stopColors;
        private double[]? _stopOffsets;
        private int _unsupportedGradientStateCount;
        private global::ProGPU.Vector.LinearGradientBrush? _nativeBrush;

        public bool TryGetOrCreate(
            MediaLinearGradientBrush brush,
            WpfReplayRect bounds,
            out global::ProGPU.Vector.Brush nativeBrush,
            out int unsupportedStateCount)
        {
            nativeBrush = null!;
            unsupportedStateCount = 0;
            var stops = brush.GradientStops;
            if (stops == null ||
                stops.Count == 0 ||
                !TryReadOptionalBrushTransform(brush.Transform, out bool hasTransform, out Matrix4x4 transform) ||
                !TryReadOptionalBrushTransform(brush.RelativeTransform, out bool hasRelativeTransform, out Matrix4x4 relativeTransform))
            {
                return false;
            }

            var startPoint = brush.StartPoint;
            var endPoint = brush.EndPoint;
            var opacity = ClampOpacity(brush.Opacity);
            var mappingMode = brush.MappingMode;
            var spreadMethod = brush.SpreadMethod;
            var colorInterpolationMode = brush.ColorInterpolationMode;
            if (_nativeBrush == null ||
                !startPoint.Equals(_startPoint) ||
                !endPoint.Equals(_endPoint) ||
                !opacity.Equals(_opacity) ||
                mappingMode != _mappingMode ||
                spreadMethod != _spreadMethod ||
                colorInterpolationMode != _colorInterpolationMode ||
                hasTransform != _hasTransform ||
                hasRelativeTransform != _hasRelativeTransform ||
                !transform.Equals(_transform) ||
                !relativeTransform.Equals(_relativeTransform) ||
                !GradientStopsMatch(stops, _stopColors, _stopOffsets))
            {
                if (!TryCreateNativeGradientStops(stops, out var nativeStops, out var stopColors, out var stopOffsets, out bool stopsTruncated))
                {
                    return false;
                }

                _startPoint = startPoint;
                _endPoint = endPoint;
                _opacity = opacity;
                _mappingMode = mappingMode;
                _spreadMethod = spreadMethod;
                _colorInterpolationMode = colorInterpolationMode;
                _hasTransform = hasTransform;
                _hasRelativeTransform = hasRelativeTransform;
                _transform = transform;
                _relativeTransform = relativeTransform;
                _stopColors = stopColors;
                _stopOffsets = stopOffsets;
                _unsupportedGradientStateCount = CountUnsupportedGradientState(stopsTruncated, unsupportedColorInterpolationMode: false);
                _nativeBrush = new global::ProGPU.Vector.LinearGradientBrush(
                    new Vector2((float)startPoint.X, (float)startPoint.Y),
                    new Vector2((float)endPoint.X, (float)endPoint.Y),
                    nativeStops)
                {
                    Opacity = (float)opacity,
                    SpreadMethod = ToVectorGradientSpreadMethod(spreadMethod),
                    ColorInterpolationMode = ToVectorGradientColorInterpolationMode(colorInterpolationMode)
                };
            }

            nativeBrush = AdaptMappedNativeBrush(
                _nativeBrush,
                ToProGpuBrushMappingMode(_mappingMode),
                _hasTransform ? _transform : null,
                _hasRelativeTransform ? _relativeTransform : null,
                _unsupportedGradientStateCount,
                bounds,
                out unsupportedStateCount);
            return true;
        }
    }

    private sealed class NativeRadialGradientBrushCache
    {
        private WpfPoint _center;
        private WpfPoint _gradientOrigin;
        private double _radiusX = double.NaN;
        private double _radiusY = double.NaN;
        private double _opacity = double.NaN;
        private MediaBrushMappingMode _mappingMode;
        private MediaGradientSpreadMethod _spreadMethod;
        private MediaColorInterpolationMode _colorInterpolationMode;
        private bool _hasTransform;
        private bool _hasRelativeTransform;
        private Matrix4x4 _transform;
        private Matrix4x4 _relativeTransform;
        private Color[]? _stopColors;
        private double[]? _stopOffsets;
        private int _unsupportedGradientStateCount;
        private global::ProGPU.Vector.RadialGradientBrush? _nativeBrush;

        public bool TryGetOrCreate(
            MediaRadialGradientBrush brush,
            WpfReplayRect bounds,
            out global::ProGPU.Vector.Brush nativeBrush,
            out int unsupportedStateCount)
        {
            nativeBrush = null!;
            unsupportedStateCount = 0;
            var stops = brush.GradientStops;
            if (stops == null ||
                stops.Count == 0 ||
                !TryReadOptionalBrushTransform(brush.Transform, out bool hasTransform, out Matrix4x4 transform) ||
                !TryReadOptionalBrushTransform(brush.RelativeTransform, out bool hasRelativeTransform, out Matrix4x4 relativeTransform))
            {
                return false;
            }

            var center = brush.Center;
            var gradientOrigin = brush.GradientOrigin;
            var radiusX = brush.RadiusX;
            var radiusY = brush.RadiusY;
            var opacity = ClampOpacity(brush.Opacity);
            var mappingMode = brush.MappingMode;
            var spreadMethod = brush.SpreadMethod;
            var colorInterpolationMode = brush.ColorInterpolationMode;
            if (_nativeBrush == null ||
                !center.Equals(_center) ||
                !gradientOrigin.Equals(_gradientOrigin) ||
                !radiusX.Equals(_radiusX) ||
                !radiusY.Equals(_radiusY) ||
                !opacity.Equals(_opacity) ||
                mappingMode != _mappingMode ||
                spreadMethod != _spreadMethod ||
                colorInterpolationMode != _colorInterpolationMode ||
                hasTransform != _hasTransform ||
                hasRelativeTransform != _hasRelativeTransform ||
                !transform.Equals(_transform) ||
                !relativeTransform.Equals(_relativeTransform) ||
                !GradientStopsMatch(stops, _stopColors, _stopOffsets))
            {
                if (!TryCreateNativeGradientStops(stops, out var nativeStops, out var stopColors, out var stopOffsets, out bool stopsTruncated))
                {
                    return false;
                }

                _center = center;
                _gradientOrigin = gradientOrigin;
                _radiusX = radiusX;
                _radiusY = radiusY;
                _opacity = opacity;
                _mappingMode = mappingMode;
                _spreadMethod = spreadMethod;
                _colorInterpolationMode = colorInterpolationMode;
                _hasTransform = hasTransform;
                _hasRelativeTransform = hasRelativeTransform;
                _transform = transform;
                _relativeTransform = relativeTransform;
                _stopColors = stopColors;
                _stopOffsets = stopOffsets;
                _unsupportedGradientStateCount = CountUnsupportedGradientState(stopsTruncated, unsupportedColorInterpolationMode: false);
                _nativeBrush = new global::ProGPU.Vector.RadialGradientBrush(
                    new Vector2((float)center.X, (float)center.Y),
                    new Vector2((float)gradientOrigin.X, (float)gradientOrigin.Y),
                    (float)radiusX,
                    (float)radiusY,
                    nativeStops)
                {
                    Opacity = (float)opacity,
                    SpreadMethod = ToVectorGradientSpreadMethod(spreadMethod),
                    ColorInterpolationMode = ToVectorGradientColorInterpolationMode(colorInterpolationMode)
                };
            }

            nativeBrush = AdaptMappedNativeBrush(
                _nativeBrush,
                ToProGpuBrushMappingMode(_mappingMode),
                _hasTransform ? _transform : null,
                _hasRelativeTransform ? _relativeTransform : null,
                _unsupportedGradientStateCount,
                bounds,
                out unsupportedStateCount);
            return true;
        }
    }

    private sealed class NativeSolidPenCache
    {
        private global::ProGPU.Vector.SolidColorBrush? _brush;
        private double _thickness = double.NaN;
        private MediaPenLineCap _startLineCap;
        private MediaPenLineCap _endLineCap;
        private MediaPenLineCap _dashCap;
        private PenLineJoin _lineJoin;
        private double _miterLimit = double.NaN;
        private double[] _dashArray = Array.Empty<double>();
        private double _dashOffset = double.NaN;
        private global::ProGPU.Vector.Pen? _nativePen;

        public bool TryGetOrCreate(
            MediaPen pen,
            global::ProGPU.Vector.SolidColorBrush brush,
            out global::ProGPU.Vector.Pen nativePen)
        {
            nativePen = null!;
            if (!TryReadDashState(
                    pen.DashStyle,
                    out var dashStyleSource,
                    out var dashCount,
                    out var dashOffset))
            {
                return false;
            }

            var thickness = pen.Thickness;
            var startLineCap = pen.StartLineCap;
            var endLineCap = pen.EndLineCap;
            var dashCap = pen.DashCap;
            var lineJoin = pen.LineJoin;
            var miterLimit = ReadMiterLimit(pen.MiterLimit);
            if (_nativePen != null &&
                ReferenceEquals(brush, _brush) &&
                thickness.Equals(_thickness) &&
                startLineCap == _startLineCap &&
                endLineCap == _endLineCap &&
                dashCap == _dashCap &&
                lineJoin == _lineJoin &&
                miterLimit.Equals(_miterLimit) &&
                dashOffset.Equals(_dashOffset) &&
                DashArraysMatch(dashStyleSource, dashCount, _dashArray))
            {
                nativePen = _nativePen;
                return true;
            }

            var dashArray = CreateDashArray(dashStyleSource, dashCount);
            _brush = brush;
            _thickness = thickness;
            _startLineCap = startLineCap;
            _endLineCap = endLineCap;
            _dashCap = dashCap;
            _lineJoin = lineJoin;
            _miterLimit = miterLimit;
            _dashArray = dashArray;
            _dashOffset = dashOffset;
            _nativePen = new global::ProGPU.Vector.Pen(
                brush,
                (float)Math.Max(0, thickness),
                ToVectorLineJoin(lineJoin),
                (float)miterLimit,
                ToVectorLineCap(startLineCap),
                ToVectorLineCap(endLineCap),
                ToVectorLineCap(dashCap),
                dashArray,
                dashOffset);
            nativePen = _nativePen;
            return true;
        }

        private static bool TryReadDashState(
            DashStyle? dashStyle,
            out PortableDashStyleSource? dashStyleSource,
            out int dashCount,
            out double dashOffset)
        {
            dashStyleSource = null;
            dashCount = 0;
            dashOffset = 0.0;
            if (dashStyle == null)
            {
                return true;
            }

            if (dashStyle is not PortableDashStyleSource portableDashStyle)
            {
                return false;
            }

            dashStyleSource = portableDashStyle;
            dashCount = Math.Max(0, portableDashStyle.PortableDashCount);
            if (dashCount == 0)
            {
                return true;
            }

            var hasPositiveEntry = false;
            for (var i = 0; i < dashCount; i++)
            {
                var dash = portableDashStyle.GetPortableDash(i);
                if (!double.IsFinite(dash) || dash < 0.0)
                {
                    return false;
                }

                hasPositiveEntry |= dash > 0.0;
            }

            if (!hasPositiveEntry)
            {
                return false;
            }

            dashOffset = double.IsFinite(portableDashStyle.PortableDashOffset)
                ? portableDashStyle.PortableDashOffset
                : 0.0;
            return true;
        }

        private static double[] CreateDashArray(
            PortableDashStyleSource? dashStyleSource,
            int dashCount)
        {
            if (dashStyleSource == null || dashCount == 0)
            {
                return Array.Empty<double>();
            }

            var dashArray = new double[dashCount];
            for (var i = 0; i < dashArray.Length; i++)
            {
                dashArray[i] = dashStyleSource.GetPortableDash(i);
            }

            return dashArray;
        }

        private static bool DashArraysMatch(
            PortableDashStyleSource? dashStyleSource,
            int dashCount,
            double[] right)
        {
            if (dashCount != right.Length)
            {
                return false;
            }

            if (dashStyleSource == null)
            {
                return dashCount == 0;
            }

            for (var i = 0; i < dashCount; i++)
            {
                if (!dashStyleSource.GetPortableDash(i).Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static bool IsUsable(WpfReplayRect bounds)
    {
        return bounds.Width > 0
            && bounds.Height > 0
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height);
    }

    private static int CountUnsupportedGradientState(bool stopsTruncated, bool unsupportedColorInterpolationMode)
    {
        var count = stopsTruncated ? 1 : 0;
        if (unsupportedColorInterpolationMode)
        {
            count++;
        }

        return count;
    }

    public static MediaPen? AdaptPen(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is MediaPen pen)
        {
            return pen;
        }

        if (resource is PortablePenSource portablePenSource)
        {
            return portablePenSource.TryGetPortablePen(out var portablePen)
                ? AdaptPortablePen(portablePen)
                : null;
        }

        return null;
    }

    private static MediaBrush? AdaptPortableBrush(PortableBrush brush)
    {
        switch (brush.Kind)
        {
            case PortableBrushKind.SolidColor:
                return new SolidColorBrush(ToMediaColor(brush));

            case PortableBrushKind.LinearGradient:
                if (!TryCreatePortableLinearGradientMediaBrush(brush, out var linearBrush))
                {
                    return null;
                }

                return linearBrush;

            case PortableBrushKind.RadialGradient:
                if (!TryCreatePortableRadialGradientMediaBrush(brush, out var radialBrush))
                {
                    return null;
                }

                return radialBrush;

            default:
                return null;
        }
    }

    private static bool TryCreatePortableLinearGradientMediaBrush(
        PortableBrush brush,
        out MediaLinearGradientBrush mediaBrush)
    {
        mediaBrush = null!;
        if (!TryCreateMediaGradientStops(brush.GradientStops, out var stops))
        {
            return false;
        }

        mediaBrush = new MediaLinearGradientBrush(
            stops,
            new WpfPoint(brush.StartPoint.X, brush.StartPoint.Y),
            new WpfPoint(brush.EndPoint.X, brush.EndPoint.Y))
        {
            Opacity = ClampOpacity(brush.Opacity),
            MappingMode = ToMediaBrushMappingMode(brush.MappingMode),
            SpreadMethod = ToMediaGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToMediaColorInterpolationMode(brush.ColorInterpolationMode)
        };
        ApplyPortableBrushTransforms(brush, mediaBrush);
        return true;
    }

    private static bool TryCreatePortableRadialGradientMediaBrush(
        PortableBrush brush,
        out MediaRadialGradientBrush mediaBrush)
    {
        mediaBrush = null!;
        if (!TryCreateMediaGradientStops(brush.GradientStops, out var stops))
        {
            return false;
        }

        mediaBrush = new MediaRadialGradientBrush(stops)
        {
            Center = new WpfPoint(brush.Center.X, brush.Center.Y),
            GradientOrigin = new WpfPoint(brush.GradientOrigin.X, brush.GradientOrigin.Y),
            RadiusX = brush.RadiusX,
            RadiusY = brush.RadiusY,
            Opacity = ClampOpacity(brush.Opacity),
            MappingMode = ToMediaBrushMappingMode(brush.MappingMode),
            SpreadMethod = ToMediaGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToMediaColorInterpolationMode(brush.ColorInterpolationMode)
        };
        ApplyPortableBrushTransforms(brush, mediaBrush);
        return true;
    }

    private static bool TryCreateMediaGradientStops(
        PortableGradientStop[] portableStops,
        out MediaGradientStopCollection stops)
    {
        stops = null!;
        if (portableStops.Length == 0)
        {
            return false;
        }

        stops = new MediaGradientStopCollection(portableStops.Length);
        for (var i = 0; i < portableStops.Length; i++)
        {
            var stop = portableStops[i];
            stops.Add(new MediaGradientStop(
                ToMediaColor(stop.Color),
                stop.Offset));
        }

        return true;
    }

    private static void ApplyPortableBrushTransforms(PortableBrush source, MediaBrush target)
    {
        if (source.HasTransform
            && TryCreateMatrixTransform(ToWpfMatrix2D(source.Transform), out var transform)
            && transform != null)
        {
            target.Transform = transform;
        }

        if (source.HasRelativeTransform
            && TryCreateMatrixTransform(ToWpfMatrix2D(source.RelativeTransform), out var relativeTransform)
            && relativeTransform != null)
        {
            target.RelativeTransform = relativeTransform;
        }
    }

    internal static global::ProGPU.Vector.Brush? AdaptNativePortableBrush(
        PortableBrush brush,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = 0;
        switch (brush.Kind)
        {
            case PortableBrushKind.SolidColor:
                var color = ToMediaColor(brush);
                return new global::ProGPU.Vector.SolidColorBrush(
                    new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));

            case PortableBrushKind.LinearGradient:
                if (!TryCreatePortableLinearGradientBrush(brush, mapRelativeToBounds: false, default, out var linearBrush, out var linearStopsTruncated))
                {
                    return null;
                }

                return AdaptMappedNativeBrush(
                    linearBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(linearStopsTruncated, unsupportedColorInterpolationMode: false),
                    bounds,
                    out unsupportedStateCount);

            case PortableBrushKind.RadialGradient:
                if (!TryCreatePortableRadialGradientBrush(brush, mapRelativeToBounds: false, default, out var radialBrush, out var radialStopsTruncated))
                {
                    return null;
                }

                return AdaptMappedNativeBrush(
                    radialBrush,
                    ToProGpuBrushMappingMode(brush.MappingMode),
                    ToOptionalMatrix4x4(brush.HasTransform, brush.Transform),
                    ToOptionalMatrix4x4(brush.HasRelativeTransform, brush.RelativeTransform),
                    CountUnsupportedGradientState(radialStopsTruncated, unsupportedColorInterpolationMode: false),
                    bounds,
                    out unsupportedStateCount);

            default:
                unsupportedStateCount = 1;
                return null;
        }
    }

    private static global::ProGPU.Vector.Brush AdaptMappedNativeBrush(
        global::ProGPU.Vector.Brush brush,
        ProGpuBrushMappingMode mappingMode,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        int unsupportedGradientStateCount,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        unsupportedStateCount = unsupportedGradientStateCount;
        if (HasUnsupportedBrushTransformForBounds(brush, transform, relativeTransform, bounds))
        {
            unsupportedStateCount++;
        }

        return ToMappedNativeBrush(brush, mappingMode, transform, relativeTransform, bounds);
    }

    private static global::ProGPU.Vector.Brush ToMappedNativeBrush(
        global::ProGPU.Vector.Brush brush,
        ProGpuBrushMappingMode mappingMode,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        WpfReplayRect bounds)
    {
        bool hasUsableBounds = IsUsable(bounds);
        double x = bounds.X;
        double y = bounds.Y;
        double width = bounds.Width;
        double height = bounds.Height;

        if (mappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && !hasUsableBounds)
        {
            return brush;
        }

        bool hasTransform = TryGetEffectiveBrushTransform(
            transform,
            relativeTransform,
            x,
            y,
            width,
            height,
            hasUsableBounds,
            out Matrix4x4 effectiveTransform);
        if (mappingMode == ProGpuBrushMappingMode.Absolute && !hasTransform)
        {
            return brush;
        }

        bool hasCoordinateTransform = TryGetCoordinateBrushTransform(
            effectiveTransform,
            hasTransform,
            out Matrix4x4 coordinateTransform);

        return brush switch
        {
            global::ProGPU.Vector.LinearGradientBrush linear => new global::ProGPU.Vector.LinearGradientBrush(
                MapBrushPoint(linear.StartPoint, mappingMode, x, y, width, height, hasUsableBounds),
                MapBrushPoint(linear.EndPoint, mappingMode, x, y, width, height, hasUsableBounds),
                linear.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
            {
                Opacity = linear.Opacity,
                SpreadMethod = linear.SpreadMethod,
                ColorInterpolationMode = linear.ColorInterpolationMode,
                CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
            },
            global::ProGPU.Vector.RadialGradientBrush radial => CreateMappedRadialGradientBrush(
                radial,
                mappingMode,
                x,
                y,
                width,
                height,
                hasUsableBounds,
                coordinateTransform,
                hasCoordinateTransform),
            _ => brush
        };
    }

    private static bool TryReadOptionalBrushTransform(
        MediaTransform? transform,
        out bool hasTransform,
        out Matrix4x4 matrix)
    {
        hasTransform = false;
        matrix = Matrix4x4.Identity;
        if (transform == null || ReferenceEquals(transform, MediaTransform.Identity))
        {
            return true;
        }

        if (!TryAdaptTransformMatrix(transform, out matrix))
        {
            return false;
        }

        hasTransform = !IsIdentityMatrix(matrix);
        if (!hasTransform)
        {
            matrix = Matrix4x4.Identity;
        }

        return true;
    }

    private static bool TryCreateNativeGradientStops(
        MediaGradientStopCollection stops,
        out global::ProGPU.Vector.GradientStop[] nativeStops,
        out Color[] stopColors,
        out double[] stopOffsets,
        out bool truncated)
    {
        nativeStops = Array.Empty<global::ProGPU.Vector.GradientStop>();
        stopColors = Array.Empty<Color>();
        stopOffsets = Array.Empty<double>();
        truncated = false;
        if (stops.Count == 0)
        {
            return false;
        }

        truncated = stops.Count > MaxSupportedGradientStops;
        var count = truncated ? MaxSupportedGradientStops : stops.Count;
        nativeStops = new global::ProGPU.Vector.GradientStop[count];
        stopColors = new Color[count];
        stopOffsets = new double[count];
        for (var i = 0; i < count; i++)
        {
            var stop = stops[i];
            var color = stop.Color;
            var offset = stop.Offset;
            stopColors[i] = color;
            stopOffsets[i] = offset;
            nativeStops[i] = new global::ProGPU.Vector.GradientStop(
                ToVectorColor(color),
                (float)offset);
        }

        return true;
    }

    private static bool GradientStopsMatch(
        MediaGradientStopCollection stops,
        Color[]? colors,
        double[]? offsets)
    {
        if (colors == null || offsets == null)
        {
            return false;
        }

        var count = Math.Min(stops.Count, MaxSupportedGradientStops);
        if (colors.Length != count || offsets.Length != count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var stop = stops[i];
            if (!stop.Color.Equals(colors[i]) || !stop.Offset.Equals(offsets[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUnsupportedBrushTransformForBounds(
        global::ProGPU.Vector.Brush brush,
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        WpfReplayRect bounds)
    {
        return (brush is global::ProGPU.Vector.LinearGradientBrush || brush is global::ProGPU.Vector.RadialGradientBrush)
            && TryGetEffectiveBrushTransform(
                transform,
                relativeTransform,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                IsUsable(bounds),
                out Matrix4x4 effectiveTransform)
            && !TryCreateCoordinateBrushTransform(effectiveTransform, out _);
    }

    private static global::ProGPU.Vector.RadialGradientBrush CreateMappedRadialGradientBrush(
        global::ProGPU.Vector.RadialGradientBrush radial,
        ProGpuBrushMappingMode mappingMode,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds,
        Matrix4x4 coordinateTransform,
        bool hasCoordinateTransform)
    {
        var center = MapBrushPoint(radial.Center, mappingMode, x, y, width, height, hasUsableBounds);
        var gradientOrigin = MapBrushPoint(radial.GradientOrigin, mappingMode, x, y, width, height, hasUsableBounds);
        var radiusX = mappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusX * width)
            : radial.RadiusX;
        var radiusY = mappingMode == ProGpuBrushMappingMode.RelativeToBoundingBox && hasUsableBounds
            ? (float)(radial.RadiusY * height)
            : radial.RadiusY;

        return new global::ProGPU.Vector.RadialGradientBrush(
            center,
            gradientOrigin,
            radiusX,
            radiusY,
            radial.Stops ?? Array.Empty<global::ProGPU.Vector.GradientStop>())
        {
            Opacity = radial.Opacity,
            SpreadMethod = radial.SpreadMethod,
            ColorInterpolationMode = radial.ColorInterpolationMode,
            CoordinateTransform = hasCoordinateTransform ? coordinateTransform : Matrix4x4.Identity
        };
    }

    private static Vector2 MapBrushPoint(
        Vector2 point,
        ProGpuBrushMappingMode mappingMode,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds)
    {
        if (mappingMode != ProGpuBrushMappingMode.RelativeToBoundingBox || !hasUsableBounds)
        {
            return point;
        }

        return new Vector2(
            (float)(x + point.X * width),
            (float)(y + point.Y * height));
    }

    private static bool TryGetEffectiveBrushTransform(
        Matrix4x4? transform,
        Matrix4x4? relativeTransform,
        double x,
        double y,
        double width,
        double height,
        bool hasUsableBounds,
        out Matrix4x4 effectiveTransform)
    {
        effectiveTransform = Matrix4x4.Identity;
        bool hasTransform = false;

        if (relativeTransform.HasValue && hasUsableBounds)
        {
            effectiveTransform *= CreateRelativeBoundsBrushTransform(relativeTransform.Value, x, y, width, height);
            hasTransform = true;
        }

        if (transform.HasValue)
        {
            effectiveTransform *= transform.Value;
            hasTransform = true;
        }

        return hasTransform;
    }

    private static bool TryGetCoordinateBrushTransform(
        Matrix4x4 transform,
        bool hasTransform,
        out Matrix4x4 coordinateTransform)
    {
        coordinateTransform = Matrix4x4.Identity;
        return !hasTransform || TryCreateCoordinateBrushTransform(transform, out coordinateTransform);
    }

    private static bool TryCreateCoordinateBrushTransform(Matrix4x4 transform, out Matrix4x4 coordinateTransform)
    {
        coordinateTransform = Matrix4x4.Identity;
        return Is2DAffineBrushTransform(transform)
            && Matrix4x4.Invert(transform, out coordinateTransform)
            && Is2DAffineBrushTransform(coordinateTransform);
    }

    private static Matrix4x4 CreateRelativeBoundsBrushTransform(
        Matrix4x4 relativeTransform,
        double x,
        double y,
        double width,
        double height)
    {
        return Matrix4x4.CreateTranslation((float)-x, (float)-y, 0)
            * Matrix4x4.CreateScale((float)(1 / width), (float)(1 / height), 1)
            * relativeTransform
            * Matrix4x4.CreateScale((float)width, (float)height, 1)
            * Matrix4x4.CreateTranslation((float)x, (float)y, 0);
    }

    private static bool Is2DAffineBrushTransform(Matrix4x4 transform)
    {
        return NearlyZero(transform.M13)
            && NearlyZero(transform.M14)
            && NearlyZero(transform.M23)
            && NearlyZero(transform.M24)
            && NearlyZero(transform.M31)
            && NearlyZero(transform.M32)
            && NearlyEqual(transform.M33, 1)
            && NearlyZero(transform.M34)
            && NearlyZero(transform.M43)
            && NearlyEqual(transform.M44, 1);
    }

    private static bool NearlyZero(float value)
    {
        return MathF.Abs(value) <= 0.0001f;
    }

    private static bool TryCreatePortableLinearGradientBrush(
        PortableBrush brush,
        bool mapRelativeToBounds,
        WpfReplayRect bounds,
        out global::ProGPU.Vector.LinearGradientBrush nativeBrush,
        out bool stopsTruncated)
    {
        nativeBrush = null!;
        if (!TryConvertPortableGradientStops(brush.GradientStops, out var stops, out stopsTruncated))
        {
            return false;
        }

        nativeBrush = new global::ProGPU.Vector.LinearGradientBrush(
            MapBrushPoint(brush.StartPoint, brush.MappingMode, bounds, mapRelativeToBounds),
            MapBrushPoint(brush.EndPoint, brush.MappingMode, bounds, mapRelativeToBounds),
            stops)
        {
            Opacity = (float)ClampOpacity(brush.Opacity),
            SpreadMethod = ToVectorGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToVectorGradientColorInterpolationMode(brush.ColorInterpolationMode)
        };
        return true;
    }

    private static bool TryCreatePortableRadialGradientBrush(
        PortableBrush brush,
        bool mapRelativeToBounds,
        WpfReplayRect bounds,
        out global::ProGPU.Vector.RadialGradientBrush nativeBrush,
        out bool stopsTruncated)
    {
        nativeBrush = null!;
        if (!TryConvertPortableGradientStops(brush.GradientStops, out var stops, out stopsTruncated))
        {
            return false;
        }

        var hasUsableRelativeBounds = mapRelativeToBounds
            && brush.MappingMode == PortableBrushMappingMode.RelativeToBoundingBox
            && IsUsable(bounds);

        nativeBrush = new global::ProGPU.Vector.RadialGradientBrush(
            MapBrushPoint(brush.Center, brush.MappingMode, bounds, mapRelativeToBounds),
            MapBrushPoint(brush.GradientOrigin, brush.MappingMode, bounds, mapRelativeToBounds),
            hasUsableRelativeBounds ? (float)(brush.RadiusX * bounds.Width) : (float)brush.RadiusX,
            hasUsableRelativeBounds ? (float)(brush.RadiusY * bounds.Height) : (float)brush.RadiusY,
            stops)
        {
            Opacity = (float)ClampOpacity(brush.Opacity),
            SpreadMethod = ToVectorGradientSpreadMethod(brush.SpreadMethod),
            ColorInterpolationMode = ToVectorGradientColorInterpolationMode(brush.ColorInterpolationMode)
        };
        return true;
    }

    private static bool TryConvertPortableGradientStops(
        PortableGradientStop[] portableStops,
        out global::ProGPU.Vector.GradientStop[] stops,
        out bool truncated)
    {
        stops = Array.Empty<global::ProGPU.Vector.GradientStop>();
        truncated = false;
        if (portableStops.Length == 0)
        {
            return false;
        }

        truncated = portableStops.Length > MaxSupportedGradientStops;
        var count = truncated ? MaxSupportedGradientStops : portableStops.Length;
        stops = new global::ProGPU.Vector.GradientStop[count];
        for (var i = 0; i < count; i++)
        {
            var stop = portableStops[i];
            stops[i] = new global::ProGPU.Vector.GradientStop(
                ToVectorColor(stop.Color),
                (float)stop.Offset);
        }

        return true;
    }

    private static Vector2 MapBrushPoint(
        PortablePoint point,
        PortableBrushMappingMode mappingMode,
        WpfReplayRect bounds,
        bool mapRelativeToBounds)
    {
        if (!mapRelativeToBounds
            || mappingMode != PortableBrushMappingMode.RelativeToBoundingBox
            || !IsUsable(bounds))
        {
            return new Vector2((float)point.X, (float)point.Y);
        }

        return new Vector2(
            (float)(bounds.X + point.X * bounds.Width),
            (float)(bounds.Y + point.Y * bounds.Height));
    }

    private static ProGpuBrushMappingMode ToProGpuBrushMappingMode(PortableBrushMappingMode mappingMode)
    {
        return mappingMode == PortableBrushMappingMode.Absolute
            ? ProGpuBrushMappingMode.Absolute
            : ProGpuBrushMappingMode.RelativeToBoundingBox;
    }

    private static ProGpuBrushMappingMode ToProGpuBrushMappingMode(MediaBrushMappingMode mappingMode)
    {
        return mappingMode == MediaBrushMappingMode.Absolute
            ? ProGpuBrushMappingMode.Absolute
            : ProGpuBrushMappingMode.RelativeToBoundingBox;
    }

    private static global::ProGPU.Vector.GradientSpreadMethod ToVectorGradientSpreadMethod(
        PortableGradientSpreadMethod spreadMethod)
    {
        return spreadMethod switch
        {
            PortableGradientSpreadMethod.Reflect => global::ProGPU.Vector.GradientSpreadMethod.Reflect,
            PortableGradientSpreadMethod.Repeat => global::ProGPU.Vector.GradientSpreadMethod.Repeat,
            _ => global::ProGPU.Vector.GradientSpreadMethod.Pad
        };
    }

    private static global::ProGPU.Vector.GradientSpreadMethod ToVectorGradientSpreadMethod(
        MediaGradientSpreadMethod spreadMethod)
    {
        return spreadMethod switch
        {
            MediaGradientSpreadMethod.Reflect => global::ProGPU.Vector.GradientSpreadMethod.Reflect,
            MediaGradientSpreadMethod.Repeat => global::ProGPU.Vector.GradientSpreadMethod.Repeat,
            _ => global::ProGPU.Vector.GradientSpreadMethod.Pad
        };
    }

    private static global::ProGPU.Vector.GradientColorInterpolationMode ToVectorGradientColorInterpolationMode(
        PortableGradientColorInterpolationMode colorInterpolationMode)
    {
        return colorInterpolationMode == PortableGradientColorInterpolationMode.ScRgbLinearInterpolation
            ? global::ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation
            : global::ProGPU.Vector.GradientColorInterpolationMode.SRgbLinearInterpolation;
    }

    private static global::ProGPU.Vector.GradientColorInterpolationMode ToVectorGradientColorInterpolationMode(
        MediaColorInterpolationMode colorInterpolationMode)
    {
        return colorInterpolationMode == MediaColorInterpolationMode.ScRgbLinearInterpolation
            ? global::ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation
            : global::ProGPU.Vector.GradientColorInterpolationMode.SRgbLinearInterpolation;
    }

    private static Matrix4x4? ToOptionalMatrix4x4(bool hasMatrix, PortableMatrix3x2 matrix)
    {
        return hasMatrix ? ToMatrix4x4(ToWpfMatrix2D(matrix)) : null;
    }

    private static Vector4 ToVectorColor(PortableColor color)
    {
        return new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    private static Vector4 ToVectorColor(Color color)
    {
        return new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    private static Vector4 ToVectorColor(Color color, double opacity)
    {
        return new Vector4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            (float)(color.A / 255.0 * ClampOpacity(opacity)));
    }

    private static MediaPen? AdaptPortablePen(PortablePen pen)
    {
        var brush = AdaptPortableBrush(pen.Brush);
        if (brush == null)
        {
            return null;
        }

        var adaptedPen = new MediaPen(brush, pen.Thickness)
        {
            StartLineCap = ToMediaPenLineCap(pen.StartLineCap),
            EndLineCap = ToMediaPenLineCap(pen.EndLineCap),
            DashCap = ToMediaPenLineCap(pen.DashCap),
            LineJoin = ToMediaPenLineJoin(pen.LineJoin),
            MiterLimit = ReadMiterLimit(pen.MiterLimit)
        };

        if (TryUseSupportedDashArray(pen.DashArray, pen.Thickness, pen.DashOffset, out var dashArray, out var dashOffset))
        {
            adaptedPen.DashStyle = new DashStyle(dashArray, dashOffset);
        }

        return adaptedPen;
    }

    private static global::ProGPU.Vector.Pen? AdaptNativePortablePen(
        PortablePen pen,
        WpfReplayRect bounds,
        out int unsupportedStateCount)
    {
        var nativeBrush = AdaptNativePortableBrush(pen.Brush, bounds, out unsupportedStateCount);
        if (nativeBrush == null)
        {
            return null;
        }

        var dashArray = Array.Empty<double>();
        var dashOffset = 0.0;
        if (TryUseSupportedDashArray(pen.DashArray, pen.Thickness, pen.DashOffset, out var portableDashArray, out var portableDashOffset))
        {
            dashArray = portableDashArray;
            dashOffset = portableDashOffset;
        }

        return new global::ProGPU.Vector.Pen(
            nativeBrush,
            (float)Math.Max(0, pen.Thickness),
            ToVectorLineJoin(pen.LineJoin),
            (float)ReadMiterLimit(pen.MiterLimit),
            ToVectorLineCap(pen.StartLineCap),
            ToVectorLineCap(pen.EndLineCap),
            ToVectorLineCap(pen.DashCap),
            dashArray,
            dashOffset);
    }

    private static Color ToMediaColor(PortableBrush brush)
    {
        var color = brush.Color;
        return Color.FromArgb(
            ClampToByte(color.A * ClampOpacity(brush.Opacity)),
            color.R,
            color.G,
            color.B);
    }

    private static Color ToMediaColor(PortableColor color)
    {
        return Color.FromArgb(
            color.A,
            color.R,
            color.G,
            color.B);
    }

    private static MediaBrushMappingMode ToMediaBrushMappingMode(PortableBrushMappingMode mappingMode)
    {
        return mappingMode == PortableBrushMappingMode.Absolute
            ? MediaBrushMappingMode.Absolute
            : MediaBrushMappingMode.RelativeToBoundingBox;
    }

    private static MediaGradientSpreadMethod ToMediaGradientSpreadMethod(PortableGradientSpreadMethod spreadMethod)
    {
        return spreadMethod switch
        {
            PortableGradientSpreadMethod.Reflect => MediaGradientSpreadMethod.Reflect,
            PortableGradientSpreadMethod.Repeat => MediaGradientSpreadMethod.Repeat,
            _ => MediaGradientSpreadMethod.Pad
        };
    }

    private static MediaColorInterpolationMode ToMediaColorInterpolationMode(
        PortableGradientColorInterpolationMode colorInterpolationMode)
    {
        return colorInterpolationMode == PortableGradientColorInterpolationMode.ScRgbLinearInterpolation
            ? MediaColorInterpolationMode.ScRgbLinearInterpolation
            : MediaColorInterpolationMode.SRgbLinearInterpolation;
    }

    private static double ClampOpacity(double opacity)
    {
        return double.IsFinite(opacity) ? Math.Clamp(opacity, 0.0, 1.0) : 1.0;
    }

    private static MediaPenLineCap ToMediaPenLineCap(PortablePenLineCap lineCap)
    {
        return lineCap switch
        {
            PortablePenLineCap.Square => MediaPenLineCap.Square,
            PortablePenLineCap.Round => MediaPenLineCap.Round,
            PortablePenLineCap.Triangle => MediaPenLineCap.Triangle,
            _ => MediaPenLineCap.Flat
        };
    }

    private static PenLineJoin ToMediaPenLineJoin(PortablePenLineJoin lineJoin)
    {
        return lineJoin switch
        {
            PortablePenLineJoin.Bevel => PenLineJoin.Bevel,
            PortablePenLineJoin.Round => PenLineJoin.Round,
            _ => PenLineJoin.Miter
        };
    }

    private static global::ProGPU.Vector.PenLineCap ToVectorLineCap(PortablePenLineCap lineCap)
    {
        return lineCap switch
        {
            PortablePenLineCap.Square => global::ProGPU.Vector.PenLineCap.Square,
            PortablePenLineCap.Round => global::ProGPU.Vector.PenLineCap.Round,
            PortablePenLineCap.Triangle => global::ProGPU.Vector.PenLineCap.Triangle,
            _ => global::ProGPU.Vector.PenLineCap.Flat
        };
    }

    private static global::ProGPU.Vector.PenLineCap ToVectorLineCap(MediaPenLineCap lineCap)
    {
        return lineCap switch
        {
            MediaPenLineCap.Square => global::ProGPU.Vector.PenLineCap.Square,
            MediaPenLineCap.Round => global::ProGPU.Vector.PenLineCap.Round,
            MediaPenLineCap.Triangle => global::ProGPU.Vector.PenLineCap.Triangle,
            _ => global::ProGPU.Vector.PenLineCap.Flat
        };
    }

    private static global::ProGPU.Vector.PenLineJoin ToVectorLineJoin(PortablePenLineJoin lineJoin)
    {
        return lineJoin switch
        {
            PortablePenLineJoin.Bevel => global::ProGPU.Vector.PenLineJoin.Bevel,
            PortablePenLineJoin.Round => global::ProGPU.Vector.PenLineJoin.Round,
            _ => global::ProGPU.Vector.PenLineJoin.Miter
        };
    }

    private static global::ProGPU.Vector.PenLineJoin ToVectorLineJoin(PenLineJoin lineJoin)
    {
        return lineJoin switch
        {
            PenLineJoin.Bevel => global::ProGPU.Vector.PenLineJoin.Bevel,
            PenLineJoin.Round => global::ProGPU.Vector.PenLineJoin.Round,
            _ => global::ProGPU.Vector.PenLineJoin.Miter
        };
    }

    private static double ReadMiterLimit(double miterLimit)
    {
        if (!double.IsFinite(miterLimit))
        {
            return 10.0;
        }

        return Math.Max(1.0, miterLimit);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 0.0001;
    }

    private static bool TryUseSupportedDashArray(
        double[]? values,
        double thickness,
        double offset,
        out double[] dashArray,
        out double dashOffset)
    {
        dashArray = Array.Empty<double>();
        dashOffset = 0;

        if (thickness <= 0 || values == null || values.Length == 0)
        {
            return false;
        }

        var hasPositiveEntry = false;
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            if (!double.IsFinite(value) || value < 0)
            {
                return false;
            }

            hasPositiveEntry |= value > 0;
        }

        if (!hasPositiveEntry)
        {
            return false;
        }

        dashArray = values;
        dashOffset = double.IsFinite(offset) ? offset : 0.0;
        return true;
    }

    public MediaImageSource? AdaptImageSource(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is MediaImageSource imageSource)
        {
            return WpfBitmapSourceImageAdapter.CanProvideGpuTexture(imageSource)
                ? imageSource
                : _imageSourceAdapter?.AdaptImageSource(resource) ?? imageSource;
        }

        return _imageSourceAdapter?.AdaptImageSource(resource);
    }

    public static MediaTransform? AdaptTransform(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (!TryAdaptTransformMatrix2D(resource, out var matrix))
        {
            return null;
        }

        return TryCreateMatrixTransform(matrix, out var transform)
            ? transform
            : null;
    }

    internal static bool TryAdaptTransformMatrix(object? resource, out Matrix4x4 transform)
    {
        if (TryAdaptTransformMatrix2D(resource, out var matrix))
        {
            transform = ToMatrix4x4(matrix);
            return true;
        }

        transform = Matrix4x4.Identity;
        return false;
    }

    internal static bool TryCreateManagedMatrixTransform(
        Matrix4x4 matrix,
        out MediaTransform transform)
    {
        transform = null!;
        if (!TryReadMatrix4x4(matrix, out var matrix2D)
            || !TryCreateMatrixTransform(matrix2D, out var mediaTransform)
            || mediaTransform == null)
        {
            return false;
        }

        transform = mediaTransform;
        return true;
    }

    internal static bool IsIdentityMatrix(Matrix4x4 matrix)
    {
        return NearlyEqual(matrix.M11, 1)
            && NearlyEqual(matrix.M12, 0)
            && NearlyEqual(matrix.M13, 0)
            && NearlyEqual(matrix.M14, 0)
            && NearlyEqual(matrix.M21, 0)
            && NearlyEqual(matrix.M22, 1)
            && NearlyEqual(matrix.M23, 0)
            && NearlyEqual(matrix.M24, 0)
            && NearlyEqual(matrix.M31, 0)
            && NearlyEqual(matrix.M32, 0)
            && NearlyEqual(matrix.M33, 1)
            && NearlyEqual(matrix.M34, 0)
            && NearlyEqual(matrix.M41, 0)
            && NearlyEqual(matrix.M42, 0)
            && NearlyEqual(matrix.M43, 0)
            && NearlyEqual(matrix.M44, 1);
    }

    internal static bool TryAdaptNativeGlyphRun(object? resource, out WpfNativeGlyphRun glyphRun)
    {
        glyphRun = default;
        if (resource == null)
        {
            return false;
        }

        if (resource is WpfNativeGlyphRun nativeGlyphRun)
        {
            glyphRun = nativeGlyphRun;
            return true;
        }

        if (resource is PortableNativeGlyphRunSource nativeGlyphRunSource)
        {
            return nativeGlyphRunSource.TryGetPortableNativeGlyphRun(out var portableNativeGlyphRun)
                && TryAdaptPortableNativeGlyphRun(portableNativeGlyphRun, out glyphRun);
        }

        if (resource is PortableNativeGlyphRun nativeGlyphRunDto)
        {
            return TryAdaptPortableNativeGlyphRun(nativeGlyphRunDto, out glyphRun);
        }

        if (resource is PortableGlyphRunSource portableGlyphRunSource)
        {
            return portableGlyphRunSource.TryGetPortableGlyphRun(out var portableGlyphRun)
                && TryAdaptPortableNativeGlyphRun(portableGlyphRun, out glyphRun);
        }

        if (resource is PortableGlyphRun portableGlyphRunDto)
        {
            return TryAdaptPortableNativeGlyphRun(portableGlyphRunDto, out glyphRun);
        }

        return false;
    }

    public static MediaGlyphRun? AdaptGlyphRun(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is PortableNativeGlyphRunSource nativeGlyphRunSource)
        {
            return nativeGlyphRunSource.TryGetPortableNativeGlyphRun(out var nativeGlyphRun)
                ? AdaptPortableNativeGlyphRun(nativeGlyphRun)
                : null;
        }

        if (resource is PortableNativeGlyphRun nativeGlyphRunDto)
        {
            return AdaptPortableNativeGlyphRun(nativeGlyphRunDto);
        }

        if (resource is PortableGlyphRunSource portableGlyphRunSource)
        {
            return portableGlyphRunSource.TryGetPortableGlyphRun(out var portableGlyphRun)
                ? AdaptPortableGlyphRun(portableGlyphRun)
                : null;
        }

        if (resource is PortableGlyphRun portableGlyphRunDto)
        {
            return AdaptPortableGlyphRun(portableGlyphRunDto);
        }

        if (resource is MediaGlyphRun glyphRun)
        {
            return glyphRun;
        }

        return null;
    }

    private static bool TryAdaptPortableNativeGlyphRun(PortableNativeGlyphRun portableGlyphRun, out WpfNativeGlyphRun glyphRun)
    {
        return s_nativePortableNativeGlyphRunCache
            .GetValue(portableGlyphRun, static _ => new NativePortableNativeGlyphRunCache())
            .TryGetOrCreate(portableGlyphRun, out glyphRun);
    }

    private static bool TryAdaptPortableNativeGlyphRun(PortableGlyphRun portableGlyphRun, out WpfNativeGlyphRun glyphRun)
    {
        return s_nativePortableGlyphRunCache
            .GetValue(portableGlyphRun, static _ => new NativePortableGlyphRunCache())
            .TryGetOrCreate(portableGlyphRun, out glyphRun);
    }

    private sealed class NativePortableNativeGlyphRunCache
    {
        private bool _hasGlyphRun;
        private ushort[]? _glyphIndices;
        private Vector2[]? _glyphPositions;
        private Vector2 _baselineOrigin;
        private double _fontRenderingEmSize = double.NaN;
        private object? _nativeFont;
        private string? _fontUri;
        private string[]? _fontFamilyNames;
        private bool _isBold;
        private bool _isItalic;
        private bool _hasTransform;
        private Matrix4x4 _transform;
        private WpfNativeGlyphRun _glyphRun;

        public bool TryGetOrCreate(PortableNativeGlyphRun portableGlyphRun, out WpfNativeGlyphRun glyphRun)
        {
            if (_hasGlyphRun
                && ReferenceEquals(portableGlyphRun.GlyphIndices, _glyphIndices)
                && ReferenceEquals(portableGlyphRun.GlyphPositions, _glyphPositions)
                && portableGlyphRun.BaselineOrigin.Equals(_baselineOrigin)
                && portableGlyphRun.FontRenderingEmSize.Equals(_fontRenderingEmSize)
                && ReferenceEquals(portableGlyphRun.NativeFont, _nativeFont)
                && string.Equals(portableGlyphRun.FontUri, _fontUri, StringComparison.Ordinal)
                && ReferenceEquals(portableGlyphRun.FontFamilyNames, _fontFamilyNames)
                && portableGlyphRun.IsBold == _isBold
                && portableGlyphRun.IsItalic == _isItalic
                && portableGlyphRun.HasTransform == _hasTransform
                && (!portableGlyphRun.HasTransform || portableGlyphRun.Transform.Equals(_transform)))
            {
                glyphRun = _glyphRun;
                return true;
            }

            glyphRun = default;
            if (!TryValidatePortableNativeGlyphRun(portableGlyphRun, out var font))
            {
                return false;
            }

            var transform = Matrix4x4.Identity;
            if (portableGlyphRun.HasTransform)
            {
                if (!TryReadMatrix4x4(portableGlyphRun.Transform, out var matrix))
                {
                    return false;
                }

                transform = ToMatrix4x4(matrix);
            }

            glyphRun = new WpfNativeGlyphRun(
                portableGlyphRun.GlyphIndices,
                CreatePortableNativeGlyphPositions(portableGlyphRun),
                font,
                (float)portableGlyphRun.FontRenderingEmSize,
                portableGlyphRun.BaselineOrigin,
                transform,
                portableGlyphRun.IsBold,
                portableGlyphRun.IsItalic);

            _hasGlyphRun = true;
            _glyphIndices = portableGlyphRun.GlyphIndices;
            _glyphPositions = portableGlyphRun.GlyphPositions;
            _baselineOrigin = portableGlyphRun.BaselineOrigin;
            _fontRenderingEmSize = portableGlyphRun.FontRenderingEmSize;
            _nativeFont = portableGlyphRun.NativeFont;
            _fontUri = portableGlyphRun.FontUri;
            _fontFamilyNames = portableGlyphRun.FontFamilyNames;
            _isBold = portableGlyphRun.IsBold;
            _isItalic = portableGlyphRun.IsItalic;
            _hasTransform = portableGlyphRun.HasTransform;
            _transform = portableGlyphRun.Transform;
            _glyphRun = glyphRun;
            return true;
        }
    }

    private sealed class NativePortableGlyphRunCache
    {
        private bool _hasGlyphRun;
        private ushort[]? _glyphIndices;
        private PortablePoint[]? _glyphPositions;
        private double[]? _advanceWidths;
        private PortablePoint[]? _glyphOffsets;
        private PortablePoint _baselineOrigin;
        private double _fontRenderingEmSize = double.NaN;
        private object? _nativeFont;
        private string? _fontUri;
        private string[]? _fontFamilyNames;
        private bool _isBold;
        private bool _isItalic;
        private bool _hasTransform;
        private PortableMatrix3x2 _transform;
        private WpfNativeGlyphRun _glyphRun;

        public bool TryGetOrCreate(PortableGlyphRun portableGlyphRun, out WpfNativeGlyphRun glyphRun)
        {
            if (_hasGlyphRun
                && ReferenceEquals(portableGlyphRun.GlyphIndices, _glyphIndices)
                && ReferenceEquals(portableGlyphRun.GlyphPositions, _glyphPositions)
                && ReferenceEquals(portableGlyphRun.AdvanceWidths, _advanceWidths)
                && ReferenceEquals(portableGlyphRun.GlyphOffsets, _glyphOffsets)
                && PortablePointEquals(portableGlyphRun.BaselineOrigin, _baselineOrigin)
                && portableGlyphRun.FontRenderingEmSize.Equals(_fontRenderingEmSize)
                && ReferenceEquals(portableGlyphRun.NativeFont, _nativeFont)
                && string.Equals(portableGlyphRun.FontUri, _fontUri, StringComparison.Ordinal)
                && ReferenceEquals(portableGlyphRun.FontFamilyNames, _fontFamilyNames)
                && portableGlyphRun.IsBold == _isBold
                && portableGlyphRun.IsItalic == _isItalic
                && portableGlyphRun.HasTransform == _hasTransform
                && (!portableGlyphRun.HasTransform || PortableMatrixEquals(portableGlyphRun.Transform, _transform)))
            {
                glyphRun = _glyphRun;
                return true;
            }

            glyphRun = default;
            if (!TryValidatePortableGlyphRun(portableGlyphRun, out var font))
            {
                return false;
            }

            var transform = Matrix4x4.Identity;
            if (portableGlyphRun.HasTransform)
            {
                var matrix = ToWpfMatrix2D(portableGlyphRun.Transform);
                if (!TryUseFiniteMatrix(matrix, out matrix))
                {
                    return false;
                }

                transform = ToMatrix4x4(matrix);
            }

            glyphRun = new WpfNativeGlyphRun(
                portableGlyphRun.GlyphIndices,
                CreatePortableGlyphPositions(portableGlyphRun),
                font,
                (float)portableGlyphRun.FontRenderingEmSize,
                ToVector2(portableGlyphRun.BaselineOrigin),
                transform,
                portableGlyphRun.IsBold,
                portableGlyphRun.IsItalic);

            _hasGlyphRun = true;
            _glyphIndices = portableGlyphRun.GlyphIndices;
            _glyphPositions = portableGlyphRun.GlyphPositions;
            _advanceWidths = portableGlyphRun.AdvanceWidths;
            _glyphOffsets = portableGlyphRun.GlyphOffsets;
            _baselineOrigin = portableGlyphRun.BaselineOrigin;
            _fontRenderingEmSize = portableGlyphRun.FontRenderingEmSize;
            _nativeFont = portableGlyphRun.NativeFont;
            _fontUri = portableGlyphRun.FontUri;
            _fontFamilyNames = portableGlyphRun.FontFamilyNames;
            _isBold = portableGlyphRun.IsBold;
            _isItalic = portableGlyphRun.IsItalic;
            _hasTransform = portableGlyphRun.HasTransform;
            _transform = portableGlyphRun.Transform;
            _glyphRun = glyphRun;
            return true;
        }
    }

    private static MediaGlyphRun? AdaptPortableNativeGlyphRun(PortableNativeGlyphRun portableGlyphRun)
    {
        if (!TryValidatePortableNativeGlyphRun(portableGlyphRun, out var font))
        {
            return null;
        }

        var transform = Matrix4x4.Identity;
        if (portableGlyphRun.HasTransform)
        {
            if (!TryReadMatrix4x4(portableGlyphRun.Transform, out var matrix))
            {
                return null;
            }

            transform = ToMatrix4x4(matrix);
        }

        return new MediaGlyphRun(
            font,
            (float)portableGlyphRun.FontRenderingEmSize,
            portableGlyphRun.GlyphIndices,
            CreatePortableNativeGlyphPositions(portableGlyphRun))
        {
            Position = portableGlyphRun.BaselineOrigin,
            Transform = transform,
            IsBold = portableGlyphRun.IsBold,
            IsItalic = portableGlyphRun.IsItalic
        };
    }

    private static MediaGlyphRun? AdaptPortableGlyphRun(PortableGlyphRun portableGlyphRun)
    {
        if (!TryValidatePortableGlyphRun(portableGlyphRun, out var font))
        {
            return null;
        }

        var transform = Matrix4x4.Identity;
        if (portableGlyphRun.HasTransform)
        {
            var matrix = ToWpfMatrix2D(portableGlyphRun.Transform);
            if (!TryUseFiniteMatrix(matrix, out matrix))
            {
                return null;
            }

            transform = ToMatrix4x4(matrix);
        }

        return new MediaGlyphRun(
            font,
            (float)portableGlyphRun.FontRenderingEmSize,
            portableGlyphRun.GlyphIndices,
            CreatePortableGlyphPositions(portableGlyphRun))
        {
            Position = ToVector2(portableGlyphRun.BaselineOrigin),
            Transform = transform,
            IsBold = portableGlyphRun.IsBold,
            IsItalic = portableGlyphRun.IsItalic
        };
    }

    private static bool TryValidatePortableNativeGlyphRun(PortableNativeGlyphRun portableGlyphRun, out TtfFont font)
    {
        font = null!;
        if (portableGlyphRun.GlyphIndices.Length == 0
            || portableGlyphRun.GlyphPositions.Length < portableGlyphRun.GlyphIndices.Length
            || portableGlyphRun.FontRenderingEmSize <= 0
            || TryResolvePortableGlyphRunFont(portableGlyphRun) is not { } resolvedFont)
        {
            return false;
        }

        font = resolvedFont;
        return true;
    }

    private static bool TryValidatePortableGlyphRun(PortableGlyphRun portableGlyphRun, out TtfFont font)
    {
        font = null!;
        if (portableGlyphRun.GlyphIndices.Length == 0
            || portableGlyphRun.FontRenderingEmSize <= 0
            || TryResolvePortableGlyphRunFont(portableGlyphRun) is not { } resolvedFont)
        {
            return false;
        }

        font = resolvedFont;
        return true;
    }

    private static Vector2[] CreatePortableNativeGlyphPositions(PortableNativeGlyphRun portableGlyphRun)
    {
        var glyphCount = portableGlyphRun.GlyphIndices.Length;
        if (portableGlyphRun.GlyphPositions.Length == glyphCount)
        {
            return portableGlyphRun.GlyphPositions;
        }

        var positions = new Vector2[glyphCount];
        Array.Copy(portableGlyphRun.GlyphPositions, positions, glyphCount);
        return positions;
    }

    private static Vector2[] CreatePortableGlyphPositions(PortableGlyphRun portableGlyphRun)
    {
        var glyphCount = portableGlyphRun.GlyphIndices.Length;
        if (portableGlyphRun.GlyphPositions.Length >= glyphCount)
        {
            var positions = new Vector2[glyphCount];
            for (var i = 0; i < positions.Length; i++)
            {
                positions[i] = ToVector2(portableGlyphRun.GlyphPositions[i]);
            }

            return positions;
        }

        var computedPositions = new Vector2[glyphCount];
        double x = 0;
        for (var i = 0; i < computedPositions.Length; i++)
        {
            var offset = i < portableGlyphRun.GlyphOffsets.Length
                ? portableGlyphRun.GlyphOffsets[i]
                : new PortablePoint(0, 0);
            computedPositions[i] = new Vector2((float)(x + offset.X), (float)offset.Y);

            if (i < portableGlyphRun.AdvanceWidths.Length)
            {
                x += portableGlyphRun.AdvanceWidths[i];
            }
        }

        return computedPositions;
    }

    private static TtfFont? TryResolvePortableGlyphRunFont(PortableGlyphRun glyphRun)
    {
        var resolvedFont = TryResolvePortableGlyphRunFontCore(
            glyphRun.NativeFont,
            glyphRun.FontUri,
            glyphRun.FontFamilyNames);
        if (resolvedFont != null)
        {
            glyphRun.NativeFont = resolvedFont;
        }

        return resolvedFont;
    }

    private static TtfFont? TryResolvePortableGlyphRunFont(PortableNativeGlyphRun glyphRun)
    {
        var resolvedFont = TryResolvePortableGlyphRunFontCore(
            glyphRun.NativeFont,
            glyphRun.FontUri,
            glyphRun.FontFamilyNames);
        if (resolvedFont != null)
        {
            glyphRun.NativeFont = resolvedFont;
        }

        return resolvedFont;
    }

    private static TtfFont? TryResolvePortableGlyphRunFontCore(object? nativeFont, string? fontUri, string[] fontFamilyNames)
    {
        if (nativeFont is TtfFont font)
        {
            return font;
        }

        if (!string.IsNullOrWhiteSpace(fontUri)
            && TryResolveFontFileValue(fontUri) is { } fontFromUri)
        {
            return fontFromUri;
        }

        for (var i = 0; i < fontFamilyNames.Length; i++)
        {
            var familyName = fontFamilyNames[i];
            if (string.IsNullOrWhiteSpace(familyName))
            {
                continue;
            }

            var resolved = TryResolveFontFamily(familyName);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return TryResolveFontFamily("Arial");
    }

    public static MediaGeometry? AdaptGeometry(object? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is PortableGeometryPathSource portableGeometry)
        {
            return portableGeometry.TryGetPortableGeometryPath(out var portablePath)
                ? AdaptPortableGeometryPath(portablePath)
                : null;
        }

        if (resource is MediaGeometry geometry)
        {
            return geometry;
        }

        return null;
    }

    private static MediaGeometry? AdaptPortableGeometryPath(PortableGeometryPath portablePath)
    {
        MediaGeometry geometry;
        if (portablePath.Kind == PortableGeometryPathKind.Combined)
        {
            var geometryA = portablePath.PathA == null
                ? new PathGeometry()
                : AdaptPortableGeometryPath(portablePath.PathA);
            var geometryB = portablePath.PathB == null
                ? new PathGeometry()
                : AdaptPortableGeometryPath(portablePath.PathB);
            if (geometryA == null || geometryB == null)
            {
                return null;
            }

            geometry = CreateCombinedGeometry(geometryA, geometryB, portablePath.CombineOperation);
        }
        else
        {
            var portableFigures = portablePath.Figures;
            var figures = new PathFigureCollection(portableFigures.Length);

            for (var figureIndex = 0; figureIndex < portableFigures.Length; figureIndex++)
            {
                var portableFigure = portableFigures[figureIndex];
                var portableSegments = portableFigure.Segments;
                var segments = new PathSegmentCollection(portableSegments.Length);
                for (var segmentIndex = 0; segmentIndex < portableSegments.Length; segmentIndex++)
                {
                    var segment = portableSegments[segmentIndex];
                    segments.Add(CreatePortablePathSegment(segment));
                }

                var figure = new PathFigure
                {
                    Segments = segments,
                    StartPoint = ToPoint(portableFigure.StartPoint),
                    IsClosed = portableFigure.IsClosed,
                    IsFilled = portableFigure.IsFilled
                };

                figures.Add(figure);
            }

            geometry = new PathGeometry
            {
                Figures = figures,
                FillRule = ToMediaFillRule(portablePath.FillRule)
            };
        }

        return ApplyPortableGeometryTransform(portablePath, geometry);
    }

    private static MediaGeometry? ApplyPortableGeometryTransform(PortableGeometryPath portablePath, MediaGeometry geometry)
    {
        if (portablePath.Transform.IsIdentity)
        {
            return geometry;
        }

        var matrix = ToWpfMatrix2D(portablePath.Transform);
        if (!TryUseFiniteMatrix(matrix, out matrix)
            || !TryCreateMatrixTransform(matrix, out var transform)
            || transform == null)
        {
            return null;
        }

        geometry.Transform = transform;
        return geometry;
    }

    private static PathSegment CreatePortablePathSegment(PortablePathSegment segment)
    {
        switch (segment.Kind)
        {
            case PortablePathSegmentKind.Line:
                return new LineSegment(ToPoint(segment.Point1), segment.IsStroked)
                {
                    IsSmoothJoin = segment.IsSmoothJoin
                };
            case PortablePathSegmentKind.QuadraticBezier:
                return new QuadraticBezierSegment(ToPoint(segment.Point1), ToPoint(segment.Point2), segment.IsStroked)
                {
                    IsSmoothJoin = segment.IsSmoothJoin
                };
            case PortablePathSegmentKind.CubicBezier:
                return new BezierSegment(
                    ToPoint(segment.Point1),
                    ToPoint(segment.Point2),
                    ToPoint(segment.Point3),
                    segment.IsStroked)
                {
                    IsSmoothJoin = segment.IsSmoothJoin
                };
            case PortablePathSegmentKind.Arc:
                return new ArcSegment
                {
                    Point = ToPoint(segment.Point1),
                    Size = ToSize(segment.Size),
                    RotationAngle = segment.RotationAngle,
                    IsLargeArc = segment.IsLargeArc,
                    SweepDirection = ToMediaSweepDirection(segment.SweepDirection),
                    IsSmoothJoin = segment.IsSmoothJoin,
                    IsStroked = segment.IsStroked
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(segment));
        }
    }

    private static FillRule ToMediaFillRule(PortableFillRule fillRule)
    {
        return fillRule == PortableFillRule.EvenOdd
            ? FillRule.EvenOdd
            : FillRule.Nonzero;
    }

    private static SweepDirection ToMediaSweepDirection(PortableSweepDirection sweepDirection)
    {
        return sweepDirection == PortableSweepDirection.Clockwise
            ? SweepDirection.Clockwise
            : SweepDirection.Counterclockwise;
    }

    internal static PathGeometry CreateRectanglePath(Rect rectangle)
    {
        return CreateRectanglePath(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    internal static PathGeometry CreateRectanglePath(WpfReplayRect rectangle)
    {
        return CreateRectanglePath(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static PathGeometry CreateRectanglePath(double x, double y, double width, double height)
    {
        var segments = new PathSegmentCollection(capacity: 3)
        {
            new LineSegment(new Point(x + width, y), isStroked: true),
            new LineSegment(new Point(x + width, y + height), isStroked: true),
            new LineSegment(new Point(x, y + height), isStroked: true)
        };

        var figures = new PathFigureCollection(capacity: 1)
        {
            new PathFigure
            {
                Segments = segments,
                StartPoint = new Point(x, y),
                IsClosed = true,
                IsFilled = true
            }
        };

        var geometry = new PathGeometry
        {
            Figures = figures
        };

        return geometry;
    }

    private static MediaGeometry CreateCombinedGeometry(MediaGeometry geometry1, MediaGeometry geometry2, int pathOperation)
    {
        return new CombinedGeometry(ToGeometryCombineMode(pathOperation), geometry1, geometry2);
    }

    private static GeometryCombineMode ToGeometryCombineMode(int pathOperation)
    {
        return pathOperation switch
        {
            0 => GeometryCombineMode.Exclude,
            1 => GeometryCombineMode.Intersect,
            3 => GeometryCombineMode.Xor,
            _ => GeometryCombineMode.Union
        };
    }

    private static Vector2 ToVector2(PortablePoint point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static bool PortablePointEquals(PortablePoint left, PortablePoint right)
    {
        return left.X.Equals(right.X) && left.Y.Equals(right.Y);
    }

    private static bool PortableMatrixEquals(PortableMatrix3x2 left, PortableMatrix3x2 right)
    {
        return left.M11.Equals(right.M11)
            && left.M12.Equals(right.M12)
            && left.M21.Equals(right.M21)
            && left.M22.Equals(right.M22)
            && left.OffsetX.Equals(right.OffsetX)
            && left.OffsetY.Equals(right.OffsetY);
    }

    private static Point ToPoint(PortablePoint point)
    {
        return new Point(point.X, point.Y);
    }

    private static Size ToSize(PortableSize size)
    {
        return new Size(Math.Abs(size.Width), Math.Abs(size.Height));
    }

    private static bool TryAdaptTransformMatrix2D(object? resource, out WpfMatrix2D matrix)
    {
        if (resource == null)
        {
            matrix = default;
            return false;
        }

        if (resource is Matrix4x4 nativeMatrix)
        {
            return TryReadMatrix4x4(nativeMatrix, out matrix);
        }

        if (resource is PortableTransformMatrixSource portableTransform)
        {
            if (portableTransform.TryGetPortableTransformMatrix(out var portableMatrix))
            {
                return TryUseFiniteMatrix(ToWpfMatrix2D(portableMatrix), out matrix);
            }

            matrix = default;
            return false;
        }

        matrix = default;
        return false;
    }

    private static bool TryReadMatrix4x4(Matrix4x4 value, out WpfMatrix2D matrix)
    {
        if (!NearlyEqual(value.M13, 0)
            || !NearlyEqual(value.M14, 0)
            || !NearlyEqual(value.M23, 0)
            || !NearlyEqual(value.M24, 0)
            || !NearlyEqual(value.M31, 0)
            || !NearlyEqual(value.M32, 0)
            || !NearlyEqual(value.M33, 1)
            || !NearlyEqual(value.M34, 0)
            || !NearlyEqual(value.M43, 0)
            || !NearlyEqual(value.M44, 1))
        {
            matrix = default;
            return false;
        }

        return TryUseFiniteMatrix(
            new WpfMatrix2D(value.M11, value.M12, value.M21, value.M22, value.M41, value.M42),
            out matrix);
    }

    private static bool TryUseFiniteMatrix(WpfMatrix2D value, out WpfMatrix2D matrix)
    {
        matrix = value;
        return double.IsFinite(value.M11)
            && double.IsFinite(value.M12)
            && double.IsFinite(value.M21)
            && double.IsFinite(value.M22)
            && double.IsFinite(value.OffsetX)
            && double.IsFinite(value.OffsetY);
    }

    private static WpfMatrix2D ToWpfMatrix2D(PortableMatrix3x2 matrix)
    {
        return new WpfMatrix2D(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX,
            matrix.OffsetY);
    }

    private static Matrix4x4 ToMatrix4x4(WpfMatrix2D matrix)
    {
        return new Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0,
            0,
            (float)matrix.M21,
            (float)matrix.M22,
            0,
            0,
            0,
            0,
            1,
            0,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0,
            1);
    }

    private static bool TryCreateMatrixTransform(WpfMatrix2D matrix, out MediaTransform? transform)
    {
        transform = new MediaMatrixTransform(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX,
            matrix.OffsetY);
        return true;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return Math.Abs(left - right) < 0.0001f;
    }

    private static TtfFont? TryResolveFontFileValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && TryGetLocalFontPath(value, out var path)
            ? TryLoadFontFile(path)
            : null;
    }

    private static bool TryGetLocalFontPath(string value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return TryGetLocalFontPath(uri, out path);
        }

        path = value;
        return File.Exists(path);
    }

    private static bool TryGetLocalFontPath(Uri uri, out string path)
    {
        path = string.Empty;
        if (uri.IsAbsoluteUri)
        {
            if (!uri.IsFile)
            {
                return false;
            }

            path = uri.LocalPath;
            return File.Exists(path);
        }

        path = uri.OriginalString;
        return File.Exists(path);
    }

    private static TtfFont? TryLoadFontFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            return s_fontFileCache.GetOrAdd(
                fullPath,
                static fontPath => new TtfFont(fontPath));
        }
        catch (Exception ex) when (IsRecoverableFontLoadException(ex))
        {
            return null;
        }
    }

    private static bool IsRecoverableFontLoadException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or FormatException
            or InvalidDataException
            or KeyNotFoundException
            or IndexOutOfRangeException
            or OverflowException
            or NotSupportedException;
    }

    private static TtfFont? TryResolveFontFamily(string familyName)
    {
        try
        {
            return new FontFamily(familyName).NativeFont;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte ClampToByte(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value >= 255)
        {
            return 255;
        }

        return (byte)Math.Round(value);
    }
}
