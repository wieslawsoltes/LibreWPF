using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaArcSegment = System.Windows.Media.ArcSegment;
using MediaBezierSegment = System.Windows.Media.BezierSegment;
using MediaCombinedGeometry = System.Windows.Media.CombinedGeometry;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaEllipseGeometry = System.Windows.Media.EllipseGeometry;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGeometryGroup = System.Windows.Media.GeometryGroup;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaLineGeometry = System.Windows.Media.LineGeometry;
using MediaLineSegment = System.Windows.Media.LineSegment;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaPathSegment = System.Windows.Media.PathSegment;
using MediaQuadraticBezierSegment = System.Windows.Media.QuadraticBezierSegment;
using MediaPen = System.Windows.Media.Pen;
using MediaPenLineCap = System.Windows.Media.PenLineCap;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaTransform = System.Windows.Media.Transform;
using VectorLineSegment = ProGPU.Vector.LineSegment;
using VectorPen = ProGPU.Vector.Pen;
using VectorPathGeometry = ProGPU.Vector.PathGeometry;
using VectorBrush = ProGPU.Vector.Brush;
using VectorPenLineCap = ProGPU.Vector.PenLineCap;
using VectorSolidColorBrush = ProGPU.Vector.SolidColorBrush;
using NativePathGeometrySource = ProGPU.Scene.INativePathGeometrySource;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableMediaPlayerFrame = ProGPU.Wpf.Interop.PortableMediaPlayerFrame;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class ProGpuCompositionCommandSink :
    IWpfCompositionCommandSink,
    IWpfViewport3DCommandSink,
    IWpfCompositionCommandSinkDiagnostics,
    IWpfNativeTransformCommandSink,
    IWpfNativePrimitiveCommandSink,
    IWpfNativeVideoCommandSink,
    IWpfNativeClipCommandSink,
    IWpfNativeGeometryCommandSink,
    IWpfHitTestOwnerScopeCommandSink,
    IWpfImageHitTestScopeCommandSink,
    IWpfSourceRectangleHitTestScopeCommandSink,
    IWpfPointHitRegionCommandSink,
    IWpfBitmapCacheBrushCommandSink,
    IWpfProGpuSceneDrawingContextSource
{
    private const float TransformEpsilon = 0.0001f;
    private const ulong NativeGeometryPathKeyOffset = 1469598103934665603UL;
    private const ulong NativeGeometryPathKeyPrime = 1099511628211UL;
    private static readonly ConditionalWeakTable<MediaGeometry, NativeGeometryPathCache> s_nativeGeometryPathCache = new();

    private enum PushKind
    {
        DrawingContext,
        Clip,
        GeometryClip,
        Guideline,
        NoOp,
        Opacity,
        OpacityMask,
        Transform,
        BitmapScalingMode,
        EdgeMode,
        TextRenderingMode,
        TextHintingMode
    }

    private SmallValueStack<PushKind> _pushStack;
    private SmallValueStack<int> _hitTestOwnerStack;
    private SmallValueStack<GuidelineState> _guidelineStack;
    private SmallValueStack<Matrix4x4> _transformStack;
    private SmallValueStack<global::ProGPU.Scene.TextureSamplingMode> _bitmapScalingModeStack;
    private SmallValueStack<bool> _edgeModeStack;
    private SmallValueStack<global::ProGPU.Scene.TextRenderingMode> _textRenderingModeStack;
    private SmallValueStack<global::ProGPU.Scene.TextHintingMode> _textHintingModeStack;
    private readonly global::ProGPU.Backend.WgpuContext? _context;
    private readonly WpfViewport3DTextureCache? _viewport3DTextureCache;
    private readonly Func<VectorPathGeometry, VectorPathGeometry?>? _pathOperationResolver;
    private readonly MediaDrawingContext? _drawingContext;
    private readonly WpfGpuHitTestOwnerMap? _hitTestOwnerMap;
    private int _activeHitTestId;
    private bool _isClosed;

    public ProGpuCompositionCommandSink(MediaDrawingContext drawingContext)
        : this(drawingContext, context: null, viewport3DTextureCache: null)
    {
    }

    internal ProGpuCompositionCommandSink(
        MediaDrawingContext drawingContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver = null,
        int hitTestId = 0,
        WpfGpuHitTestOwnerMap? hitTestOwnerMap = null)
        : this(
            drawingContext?.NativeContext ?? throw new ArgumentNullException(nameof(drawingContext)),
            context,
            viewport3DTextureCache,
            pathOperationResolver,
            drawingContext,
            hitTestId,
            hitTestOwnerMap)
    {
    }

    public ProGpuCompositionCommandSink(global::ProGPU.Scene.DrawingContext nativeContext)
        : this(nativeContext, context: null, viewport3DTextureCache: null)
    {
    }

    internal ProGpuCompositionCommandSink(
        global::ProGPU.Scene.DrawingContext nativeContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver = null,
        int hitTestId = 0,
        WpfGpuHitTestOwnerMap? hitTestOwnerMap = null)
        : this(nativeContext, context, viewport3DTextureCache, pathOperationResolver, drawingContext: null, hitTestId, hitTestOwnerMap)
    {
    }

    private ProGpuCompositionCommandSink(
        global::ProGPU.Scene.DrawingContext nativeContext,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        Func<VectorPathGeometry, VectorPathGeometry?>? pathOperationResolver,
        MediaDrawingContext? drawingContext,
        int hitTestId,
        WpfGpuHitTestOwnerMap? hitTestOwnerMap)
    {
        NativeContext = nativeContext ?? throw new ArgumentNullException(nameof(nativeContext));
        _drawingContext = drawingContext;
        _context = context;
        _viewport3DTextureCache = viewport3DTextureCache;
        _pathOperationResolver = pathOperationResolver;
        _activeHitTestId = hitTestId;
        _hitTestOwnerMap = hitTestOwnerMap;
        _transformStack.Push(Matrix4x4.Identity);
        _bitmapScalingModeStack.Push(global::ProGPU.Scene.TextureSamplingMode.Linear);
        _edgeModeStack.Push(false);
        _textRenderingModeStack.Push(global::ProGPU.Scene.TextRenderingMode.Grayscale);
        _textHintingModeStack.Push(global::ProGPU.Scene.TextHintingMode.Auto);
    }

    public MediaDrawingContext? DrawingContext => _drawingContext;

    internal global::ProGPU.Scene.DrawingContext NativeContext { get; }

    bool IWpfProGpuSceneDrawingContextSource.TryGetProGpuSceneDrawingContext(
        out global::ProGPU.Scene.DrawingContext? drawingContext)
    {
        return ((IWpfProGpuSceneDrawingContextSource)this)
            .TryGetProGpuSceneDrawingContextState(out drawingContext, out _);
    }

    bool IWpfProGpuSceneDrawingContextSource.TryGetProGpuSceneDrawingContextState(
        out global::ProGPU.Scene.DrawingContext? drawingContext,
        out Matrix4x4 transform)
    {
        if (_isClosed)
        {
            drawingContext = null;
            transform = Matrix4x4.Identity;
            return false;
        }

        drawingContext = NativeContext;
        transform = _transformStack.Peek();
        return true;
    }

    private void AddNativeCommand(global::ProGPU.Scene.RenderCommand command)
    {
        command.HitTestId = _activeHitTestId;
        NativeContext.Commands.Add(command);
    }

    void IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushSource(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        using var lease = WpfBitmapCacheBrushSourceLookup.Acquire(source, _context, _viewport3DTextureCache, imageSourceAdapter);
        NativeContext.DrawCachedPicture(lease, _transformStack.Peek());
    }

    bool IWpfBitmapCacheBrushCommandSink.PushBitmapCacheBrushOpacityMask(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source, WpfReplayRect bounds,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        if (!source.TryGetPortableBitmapCacheBrush(out var brush)
            || !double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1) return false;
        // An empty source is a transparent mask, not an absent/no-op mask.
        if (brush.InternalTarget == null || brush.Opacity == 0)
        {
            PushOpacity(0);
            return true;
        }
        if (!global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryGetMapping(brush,
                new global::ProGPU.Wpf.Interop.PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), out var mapping)) return false;
        var nativeBounds = ToNativeRect(bounds);
        if (!float.IsFinite(nativeBounds.Right) || !float.IsFinite(nativeBounds.Bottom)
            || nativeBounds.Width <= 0 || nativeBounds.Height <= 0) return false;
        if (mapping.M11 * mapping.M22 - mapping.M12 * mapping.M21 == 0)
        {
            PushOpacity(0);
            return true;
        }
        using var lease = WpfBitmapCacheBrushSourceLookup.Acquire(source, _context, _viewport3DTextureCache, imageSourceAdapter);
        NativeContext.PushCachedPictureOpacityMask(lease, nativeBounds, mapping, (float)brush.Opacity, _transformStack.Peek());
        _pushStack.Push(PushKind.OpacityMask);
        return true;
    }

    bool IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushGlyphRun(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source, object glyphRunResource,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        if (!source.TryGetPortableBitmapCacheBrush(out var brush)
            || !double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1) return false;
        if (brush.InternalTarget == null || brush.Opacity == 0) return true;
        if (!WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRunResource, out var run) || !run.HasInkBounds) return false;
        var ink = run.InkBounds;
        if (ink.IsEmpty || ink.Width == 0 || ink.Height == 0) return true;
        if (!global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryGetMapping(brush, ink, out var mapping)) return false;
        if (mapping.M11 * mapping.M22 - mapping.M12 * mapping.M21 == 0) return true;
        var bounds = ToNativeRect(new WpfReplayRect(ink.X, ink.Y, ink.Width, ink.Height));
        if (!float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) || bounds.Width <= 0 || bounds.Height <= 0) return false;
        var recorder = new global::ProGPU.Scene.GpuPictureRecorder();
        var coverageCommands = recorder.BeginRecording(bounds);
        global::ProGPU.Scene.GpuPicture coverage;
        try
        {
            coverageCommands.DrawGlyphRun(run.GlyphIndices, run.GlyphPositions, run.Font, run.FontSize,
                new VectorSolidColorBrush(Vector4.One), run.Position, isBold: run.IsBold, isItalic: run.IsItalic,
                textRenderingMode: _textRenderingModeStack.Peek() == global::ProGPU.Scene.TextRenderingMode.Aliased
                    ? global::ProGPU.Scene.TextRenderingMode.Aliased : global::ProGPU.Scene.TextRenderingMode.Grayscale,
                textHintingMode: _textHintingModeStack.Peek());
            coverage = recorder.EndRecording();
        }
        finally { coverageCommands.Clear(); }
        using (coverage)
        using (var lease = WpfBitmapCacheBrushSourceLookup.Acquire(source, _context, _viewport3DTextureCache, imageSourceAdapter))
            NativeContext.DrawCachedPictureWithCoverage(lease, coverage, bounds, mapping,
                (float)brush.Opacity, run.Transform * _transformStack.Peek());
        return true;
    }

    bool IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushLine(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        in global::ProGPU.Wpf.Interop.PortablePenState pen, WpfReplayPoint start, WpfReplayPoint end,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        if (!source.TryGetPortableBitmapCacheBrush(out var brush)
            || !double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1) return false;
        if (brush.InternalTarget == null || brush.Opacity == 0) return true;
        if (!WpfResourceResolver.TryAdaptNativeStrokePen(pen, out var nativePen)) return false;
        var first = SnapGuideline(new Point(start.X, start.Y));
        var last = SnapGuideline(new Point(end.X, end.Y));
        if (!global::ProGPU.Scene.StrokeCoverageGeometry.TryPrepareLine(
                new Vector2((float)first.X, (float)first.Y), new Vector2((float)last.X, (float)last.Y),
                nativePen, out var path, out var coveragePen, out var bounds, out var fillCoverage)) return false;
        if (bounds.Width == 0 || bounds.Height == 0) return true;
        var ink = new global::ProGPU.Wpf.Interop.PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (!global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryGetMapping(brush, ink, out var mapping)) return false;
        if (mapping.M11 * mapping.M22 - mapping.M12 * mapping.M21 == 0) return true;
        using var lease = WpfBitmapCacheBrushSourceLookup.Acquire(source, _context, _viewport3DTextureCache, imageSourceAdapter);
        if (fillCoverage != null)
            NativeContext.DrawCachedPictureFillCoverage(lease, fillCoverage, bounds, mapping,
                (float)brush.Opacity, _transformStack.Peek(), _edgeModeStack.Peek());
        else
            NativeContext.DrawCachedPictureStroke(lease, path, coveragePen, bounds, mapping,
                (float)brush.Opacity, _transformStack.Peek(), _edgeModeStack.Peek());
        return true;
    }

    bool IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushRectangleStroke(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        in global::ProGPU.Wpf.Interop.PortablePenState pen, WpfReplayRect rectangle,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        if (!source.TryGetPortableBitmapCacheBrush(out var brush)
            || !double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1) return false;
        if (brush.InternalTarget == null || brush.Opacity == 0) return true;
        if (!WpfResourceResolver.TryAdaptNativeStrokePen(pen, out var nativePen)) return false;
        var snapped = SnapGuidelines(new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
        if (!global::ProGPU.Scene.StrokeCoverageGeometry.TryPrepareRectangle(ToNativeRect(snapped),
                Matrix3x2.Identity, nativePen, out var path, out var coveragePen, out var bounds)) return false;
        if (bounds.Width == 0 || bounds.Height == 0) return true;
        var ink = new global::ProGPU.Wpf.Interop.PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (!global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryGetMapping(brush, ink, out var mapping)) return false;
        if (mapping.M11 * mapping.M22 - mapping.M12 * mapping.M21 == 0) return true;
        using var lease = WpfBitmapCacheBrushSourceLookup.Acquire(source, _context, _viewport3DTextureCache, imageSourceAdapter);
        NativeContext.DrawCachedPictureStroke(lease, path, coveragePen, bounds, mapping,
            (float)brush.Opacity, _transformStack.Peek(), _edgeModeStack.Peek());
        return true;
    }

    WpfDrawingReplayStatus IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushPrimitiveGeometry(object? fill,
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        in global::ProGPU.Wpf.Interop.PortablePenState pen,
        in global::ProGPU.Wpf.Interop.PortablePrimitiveGeometry geometry,
        Func<object?, MediaImageSource?>? imageSourceAdapter, bool snapShape)
    {
        ThrowIfClosed();
        if (snapShape)
        {
            var shape = geometry;
            if (geometry.Kind == global::ProGPU.Wpf.Interop.PortablePrimitiveGeometryKind.Ellipse)
            {
                double rx = geometry.RadiusX, ry = geometry.RadiusY;
                if (!double.IsFinite(rx * 2) || !double.IsFinite(ry * 2) || rx <= 0 || ry <= 0
                    || !double.IsFinite(geometry.Point1.X - rx) || !double.IsFinite(geometry.Point1.Y - ry))
                    return WpfDrawingReplayStatus.Unsupported;
                var bounds = SnapGuidelines(new Rect(geometry.Point1.X - rx, geometry.Point1.Y - ry, rx * 2, ry * 2));
                shape = global::ProGPU.Wpf.Interop.PortablePrimitiveGeometry.Ellipse(
                    new(bounds.X + bounds.Width * 0.5, bounds.Y + bounds.Height * 0.5), bounds.Width * 0.5, bounds.Height * 0.5, geometry.Transform);
            }
            else if (geometry.Kind == global::ProGPU.Wpf.Interop.PortablePrimitiveGeometryKind.Rectangle)
            {
                if (!double.IsFinite(geometry.Rect.Width) || !double.IsFinite(geometry.Rect.Height)
                    || !double.IsFinite(geometry.Rect.X) || !double.IsFinite(geometry.Rect.Y)
                    || geometry.Rect.Width <= 0 || geometry.Rect.Height <= 0) return WpfDrawingReplayStatus.Unsupported;
                var bounds = SnapGuidelines(new Rect(geometry.Rect.X, geometry.Rect.Y, geometry.Rect.Width, geometry.Rect.Height));
                shape = global::ProGPU.Wpf.Interop.PortablePrimitiveGeometry.Rectangle(
                    new(bounds.X, bounds.Y, bounds.Width, bounds.Height), geometry.RadiusX, geometry.RadiusY, geometry.Transform);
            }
            return ((IWpfBitmapCacheBrushCommandSink)this).DrawBitmapCacheBrushPrimitiveGeometry(fill, source, pen, shape, imageSourceAdapter);
        }
        if (geometry.Kind == global::ProGPU.Wpf.Interop.PortablePrimitiveGeometryKind.Ellipse
            || geometry.RadiusX != 0 || geometry.RadiusY != 0)
            return DrawBitmapCacheBrushSmoothGeometry(fill, source, pen, geometry, imageSourceAdapter);
        Span<global::ProGPU.Wpf.Interop.PortablePoint> corners = stackalloc global::ProGPU.Wpf.Interop.PortablePoint[4];
        if (!geometry.TryWriteTransformedRectangleCorners(corners)) return WpfDrawingReplayStatus.Unsupported;
        Span<Vector2> points = stackalloc Vector2[4];
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            points[i] = new((float)corners[i].X, (float)corners[i].Y);
            if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y)) return WpfDrawingReplayStatus.Unsupported;
            minimum = Vector2.Min(minimum, points[i]);
            maximum = Vector2.Max(maximum, points[i]);
        }
        var extent = maximum - minimum;
        if (!float.IsFinite(extent.X) || !float.IsFinite(extent.Y)) return WpfDrawingReplayStatus.Unsupported;
        VectorPathGeometry path = null!;
        VectorPen coveragePen = null!;
        global::ProGPU.Scene.Rect strokeBounds = default;
        bool prepared = WpfResourceResolver.TryAdaptNativeStrokePen(pen, out var nativePen)
            && global::ProGPU.Scene.StrokeCoverageGeometry.TryPrepareConvexQuadrilateral(points, nativePen,
                out path, out coveragePen, out strokeBounds);
        // Even a rejected stroke must not erase valid fill. Success shares the
        // same immutable spine between fill coverage and the stroke mask.
        path ??= global::ProGPU.Scene.RenderCommandGeometryCache.CreatePolylinePath(points, isClosed: true);
        return DrawPreparedBitmapCacheGeometry(fill, source, path, coveragePen, strokeBounds,
            new(minimum.X, minimum.Y, extent.X, extent.Y), prepared, imageSourceAdapter);
    }

    private WpfDrawingReplayStatus DrawBitmapCacheBrushSmoothGeometry(object? fill,
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        in global::ProGPU.Wpf.Interop.PortablePenState pen,
        in global::ProGPU.Wpf.Interop.PortablePrimitiveGeometry geometry,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        // Only metadata conversion belongs in the host; curve preparation,
        // authoritative stroke bounds and SIMD arithmetic stay in ProGPU.
        var matrix = geometry.Transform;
        var affine = new Matrix3x2((float)matrix.M11, (float)matrix.M12, (float)matrix.M21,
            (float)matrix.M22, (float)matrix.OffsetX, (float)matrix.OffsetY);
        var descriptor = geometry;
        bool Prepare(VectorPen value, out VectorPathGeometry path, out VectorPen coverage, out global::ProGPU.Scene.Rect ink)
        {
            if (descriptor.Kind == global::ProGPU.Wpf.Interop.PortablePrimitiveGeometryKind.Ellipse)
                return global::ProGPU.Scene.StrokeCoverageGeometry.TryPrepareEllipse(
                    new((float)descriptor.Point1.X, (float)descriptor.Point1.Y), (float)descriptor.RadiusX,
                    (float)descriptor.RadiusY, affine, value, out path, out coverage, out ink);
            if (descriptor.Kind == global::ProGPU.Wpf.Interop.PortablePrimitiveGeometryKind.Rectangle)
                return global::ProGPU.Scene.StrokeCoverageGeometry.TryPrepareRoundedRectangle(
                    new((float)descriptor.Rect.X, (float)descriptor.Rect.Y, (float)descriptor.Rect.Width, (float)descriptor.Rect.Height),
                    (float)descriptor.RadiusX, (float)descriptor.RadiusY, affine, value, out path, out coverage, out ink);
            path = null!; coverage = null!; ink = default;
            return false;
        }
        VectorPathGeometry path = null!;
        VectorPen coveragePen = null!;
        global::ProGPU.Scene.Rect strokeBounds = default;
        bool prepared = WpfResourceResolver.TryAdaptNativeStrokePen(pen, out var nativePen)
            && Prepare(nativePen, out path, out coveragePen, out strokeBounds);
        // A rejected pen may still have a valid fill. Zero-width preparation
        // requests only an owned analytic spine, never a substitute stroke.
        if (!prepared && (fill == null || !Prepare(new VectorPen(new VectorSolidColorBrush(Vector4.One), 0),
                out path, out _, out _))) return WpfDrawingReplayStatus.Unsupported;
        if (!path.TryGetBounds(out var minimum, out var maximum)) return WpfDrawingReplayStatus.Unsupported;
        var extent = maximum - minimum;
        if (!float.IsFinite(extent.X) || !float.IsFinite(extent.Y)) return WpfDrawingReplayStatus.Unsupported;
        return DrawPreparedBitmapCacheGeometry(fill, source, path, coveragePen, strokeBounds,
            new(minimum.X, minimum.Y, extent.X, extent.Y), prepared, imageSourceAdapter);
    }

    WpfDrawingReplayStatus IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushPathGeometry(object? fill,
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        in global::ProGPU.Wpf.Interop.PortablePenState pen, MediaGeometry geometry,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        if (!TryConvertGeometryToNativePath(geometry, Matrix4x4.Identity, out var path, out _))
            return WpfDrawingReplayStatus.Unsupported;
        return ((IWpfBitmapCacheBrushCommandSink)this).DrawBitmapCacheBrushPathGeometry(fill, source, pen, path, imageSourceAdapter);
    }

    WpfDrawingReplayStatus IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushPathGeometry(object? fill,
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        in global::ProGPU.Wpf.Interop.PortablePenState pen, VectorPathGeometry geometry,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        VectorPathGeometry strokePath = null!;
        VectorPathGeometry? fillCoverage = null;
        VectorPen coveragePen = null!;
        global::ProGPU.Scene.Rect strokeBounds = default;
        bool prepared = WpfResourceResolver.TryAdaptNativeStrokePen(pen, out var nativePen)
            && global::ProGPU.Scene.StrokeCoverageGeometry.TryPrepareLinearPath(geometry, nativePen,
                out strokePath, out coveragePen, out strokeBounds, out fillCoverage);
        // Fill follows the original path, not a gap-split stroke-only contour.
        WpfReplayRect fillBounds = default;
        if (fill != null)
        {
            if (!geometry.TryGetBounds(out var minimum, out var maximum)) return WpfDrawingReplayStatus.Unsupported;
            var extent = maximum - minimum;
            if (!float.IsFinite(minimum.X) || !float.IsFinite(minimum.Y)
                || !float.IsFinite(extent.X) || !float.IsFinite(extent.Y)) return WpfDrawingReplayStatus.Unsupported;
            fillBounds = new(minimum.X, minimum.Y, extent.X, extent.Y);
        }
        return DrawPreparedBitmapCacheGeometry(fill, source, geometry, coveragePen, strokeBounds,
            fillBounds, prepared, imageSourceAdapter, strokePath, fillCoverage);
    }

    private WpfDrawingReplayStatus DrawPreparedBitmapCacheGeometry(object? fill,
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        VectorPathGeometry path, VectorPen coveragePen, global::ProGPU.Scene.Rect strokeBounds,
        WpfReplayRect fillBounds, bool prepared, Func<object?, MediaImageSource?>? imageSourceAdapter,
        VectorPathGeometry? strokePath = null, VectorPathGeometry? fillCoverage = null)
    {
        bool fillApplied = fill == null, partialFill = false;
        if (fill != null)
        {
            if (WpfDrawingReplay.IsSourceBrush(fill))
            {
                if (WpfDrawingReplay.TryReplaySourceBrushFill(fill, path, this, imageSourceAdapter, out var fillStatus))
                {
                    fillApplied = fillStatus == WpfDrawingReplayStatus.Applied;
                    partialFill = fillStatus == WpfDrawingReplayStatus.PartiallyApplied;
                }
            }
            else if (WpfResourceResolver.AdaptBrush(fill) is { } mediaFill)
            {
                int unsupportedBefore = UnsupportedStateCount;
                var fillBrush = ToNativeBrush(mediaFill, fillBounds);
                if (fillBrush != null)
                {
                    AddNativePath(fillBrush, null, path);
                    fillApplied = UnsupportedStateCount == unsupportedBefore;
                    partialFill = !fillApplied;
                }
            }
        }
        bool strokeApplied = prepared && TryDrawPreparedBitmapCacheStroke(source, strokePath ?? path, coveragePen,
            strokeBounds, imageSourceAdapter, fillCoverage);
        return fillApplied && strokeApplied ? WpfDrawingReplayStatus.Applied
            : strokeApplied || partialFill || (fill != null && fillApplied)
                ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Unsupported;
    }

    private bool TryDrawPreparedBitmapCacheStroke(global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        VectorPathGeometry path, VectorPen pen, global::ProGPU.Scene.Rect bounds,
        Func<object?, MediaImageSource?>? imageSourceAdapter, VectorPathGeometry? fillCoverage = null)
    {
        if (!source.TryGetPortableBitmapCacheBrush(out var brush)
            || !double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1) return false;
        if (brush.InternalTarget == null || brush.Opacity == 0 || bounds.Width == 0 || bounds.Height == 0) return true;
        var ink = new global::ProGPU.Wpf.Interop.PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (!global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryGetMapping(brush, ink, out var mapping)) return false;
        if (mapping.M11 * mapping.M22 - mapping.M12 * mapping.M21 == 0) return true;
        using var lease = WpfBitmapCacheBrushSourceLookup.Acquire(source, _context, _viewport3DTextureCache, imageSourceAdapter);
        if (fillCoverage != null)
            NativeContext.DrawCachedPictureFillCoverage(lease, fillCoverage, bounds, mapping,
                (float)brush.Opacity, _transformStack.Peek(), _edgeModeStack.Peek());
        else
            NativeContext.DrawCachedPictureStroke(lease, path, pen, bounds, mapping,
                (float)brush.Opacity, _transformStack.Peek(), _edgeModeStack.Peek());
        return true;
    }

    bool IWpfHitTestOwnerScopeCommandSink.PushHitTestOwner(object sourceVisual)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(sourceVisual);
        if (_hitTestOwnerMap == null)
        {
            return false;
        }

        _hitTestOwnerStack.Push(_activeHitTestId);
        _activeHitTestId = _hitTestOwnerMap.GetOrCreateId(sourceVisual);
        return true;
    }

    void IWpfHitTestOwnerScopeCommandSink.PopHitTestOwner()
    {
        ThrowIfClosed();
        if (_hitTestOwnerStack.Count == 0)
        {
            throw new InvalidOperationException("There is no WPF hit-test owner scope to pop.");
        }

        _activeHitTestId = _hitTestOwnerStack.Pop();
    }

    public int UnsupportedStateCount { get; private set; }

    public bool DrawViewport3D(object viewportVisual)
    {
        ThrowIfClosed();

        if (_context == null || _viewport3DTextureCache == null)
        {
            return false;
        }

        if (!WpfViewport3DSceneBridge.TryCreateReplayData(
                viewportVisual,
                _viewport3DTextureCache,
                out var replayData)
            || replayData.Payload.ColorTexture == null
            || replayData.Payload.MsaaColorTexture == null
            || replayData.Payload.DepthTexture == null)
        {
            return false;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawExtension,
            ExtensionId = global::ProGPU.Scene.CompositorBuiltInExtensions.Mesh3D,
            UseGpuTransforms = true,
            CameraView = replayData.View,
            Transform = replayData.Projection,
            DataParam = replayData.Payload
        });

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
            Texture = replayData.Payload.ColorTexture,
            Rect = replayData.Viewport,
            Transform = _transformStack.Peek(),
            TextureSamplingMode = _bitmapScalingModeStack.Peek()
        });

        return true;
    }

    public void DrawLine(MediaPen? pen, Point point0, Point point1)
    {
        ThrowIfClosed();

        if (WpfResourceResolver.TryGetBitmapCachePen(pen, out var state, out var source))
        {
            if (!((IWpfBitmapCacheBrushCommandSink)this).DrawBitmapCacheBrushLine(source, state,
                    new WpfReplayPoint(point0.X, point0.Y), new WpfReplayPoint(point1.X, point1.Y), null))
                UnsupportedStateCount++;
            return;
        }

        var sourcePoint0 = point0;
        var sourcePoint1 = point1;
        point0 = SnapGuideline(point0);
        point1 = SnapGuideline(point1);
        var bounds = new Rect(point0, point1);
        if (pen == null || ToNativePen(pen, bounds) is not { } nativePen)
        {
            return;
        }

        var sourceGeometry = sourcePoint0.X != point0.X || sourcePoint0.Y != point0.Y ||
            sourcePoint1.X != point1.X || sourcePoint1.Y != point1.Y ||
            pen.StartLineCap != MediaPenLineCap.Flat || pen.EndLineCap != MediaPenLineCap.Flat
            ? new global::ProGPU.Scene.SourceHitTestGeometry(global::ProGPU.Scene.SourceHitTestGeometryKind.Line,
                new Vector4((float)sourcePoint0.X, (float)sourcePoint0.Y, (float)sourcePoint1.X, (float)sourcePoint1.Y))
            : default;
        AddNativeLine(nativePen, point0, point1, pen.StartLineCap, pen.EndLineCap, sourceGeometry);
    }

    private void AddNativeLine(
        VectorPen pen,
        Point point0,
        Point point1,
        MediaPenLineCap startLineCap = MediaPenLineCap.Flat,
        MediaPenLineCap endLineCap = MediaPenLineCap.Flat,
        global::ProGPU.Scene.SourceHitTestGeometry sourceHitGeometry = default)
    {
        var originalPoint0 = point0;
        var originalPoint1 = point1;
        ApplySquareLineCaps(pen, ref point0, ref point1, startLineCap, endLineCap);

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawLine,
            SourceHitGeometry = sourceHitGeometry,
            Pen = pen,
            Position = new Vector2((float)point0.X, (float)point0.Y),
            Position2 = new Vector2((float)point1.X, (float)point1.Y),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });

        var capHitGeometry = sourceHitGeometry.Kind != global::ProGPU.Scene.SourceHitTestGeometryKind.None
            ? global::ProGPU.Scene.SourceHitTestGeometry.Excluded : default;
        AddRoundLineCap(pen, originalPoint0, startLineCap, capHitGeometry);
        AddRoundLineCap(pen, originalPoint1, endLineCap, capHitGeometry);
        AddTriangleLineCap(pen, originalPoint0, originalPoint1, startLineCap, isStart: true, capHitGeometry);
        AddTriangleLineCap(pen, originalPoint0, originalPoint1, endLineCap, isStart: false, capHitGeometry);
    }

    private static void ApplySquareLineCaps(
        VectorPen pen,
        ref Point point0,
        ref Point point1,
        MediaPenLineCap startLineCap,
        MediaPenLineCap endLineCap)
    {
        if (startLineCap != MediaPenLineCap.Square && endLineCap != MediaPenLineCap.Square)
        {
            return;
        }

        var start = new Vector2((float)point0.X, (float)point0.Y);
        var end = new Vector2((float)point1.X, (float)point1.Y);
        var delta = end - start;
        var length = delta.Length();
        if (length <= TransformEpsilon)
        {
            return;
        }

        var extension = delta / length * (pen.Thickness / 2);
        if (startLineCap == MediaPenLineCap.Square)
        {
            start -= extension;
            point0 = new Point(start.X, start.Y);
        }

        if (endLineCap == MediaPenLineCap.Square)
        {
            end += extension;
            point1 = new Point(end.X, end.Y);
        }
    }

    private void AddRoundLineCap(VectorPen pen, Point point, MediaPenLineCap lineCap,
        global::ProGPU.Scene.SourceHitTestGeometry sourceHitGeometry)
    {
        if (lineCap != MediaPenLineCap.Round || pen.Thickness <= TransformEpsilon)
        {
            return;
        }

        var radius = pen.Thickness / 2;
        AddNativeEllipse(pen.Brush, null, point, radius, radius, sourceHitGeometry);
    }

    private void AddTriangleLineCap(
        VectorPen pen,
        Point point0,
        Point point1,
        MediaPenLineCap lineCap,
        bool isStart,
        global::ProGPU.Scene.SourceHitTestGeometry sourceHitGeometry)
    {
        if (lineCap != MediaPenLineCap.Triangle || pen.Thickness <= TransformEpsilon)
        {
            return;
        }

        var start = new Vector2((float)point0.X, (float)point0.Y);
        var end = new Vector2((float)point1.X, (float)point1.Y);
        var delta = end - start;
        var length = delta.Length();
        if (length <= TransformEpsilon)
        {
            return;
        }

        var direction = delta / length;
        var radius = pen.Thickness / 2;
        var perpendicular = new Vector2(-direction.Y, direction.X) * radius;
        var center = isStart ? start : end;
        var outward = isStart ? -direction : direction;

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.FillTriangle,
            SourceHitGeometry = sourceHitGeometry,
            Brush = pen.Brush,
            Position = center - perpendicular,
            Position2 = center + outward * radius,
            Position3 = center + perpendicular,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenRectangle(brush, pen,
            new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), this, null, out var cachedStatus))
        {
            if (cachedStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return;
        }
        var sourceRectangle = rectangle;
        rectangle = SnapGuidelines(rectangle);
        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);

        AddNativeRect(nativeBrush, nativePen, rectangle,
            SourceRectangleHitGeometry(sourceRectangle, rectangle, global::ProGPU.Scene.SourceHitTestGeometryKind.Rectangle));
    }

    private static global::ProGPU.Scene.SourceHitTestGeometry SourceRectangleHitGeometry(
        Rect source, Rect raster, global::ProGPU.Scene.SourceHitTestGeometryKind kind) =>
        source.X == raster.X && source.Y == raster.Y && source.Width == raster.Width && source.Height == raster.Height
        ? default : new(kind, new Vector4((float)source.X, (float)source.Y, (float)source.Width, (float)source.Height));

    private void AddNativeRect(VectorBrush? brush, VectorPen? pen, Rect rectangle,
        global::ProGPU.Scene.SourceHitTestGeometry sourceHitGeometry = default)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRect,
            SourceHitGeometry = sourceHitGeometry,
            Brush = brush,
            Pen = pen,
            Rect = ToNativeRect(rectangle),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenRoundedRectangle(brush, pen,
            new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), radiusX, radiusY, this, null, out var cachedStatus))
        {
            if (cachedStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return;
        }
        var sourceRectangle = rectangle;
        rectangle = SnapGuidelines(rectangle);
        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);

        AddNativeRoundedRect(nativeBrush, nativePen, rectangle, radiusX, radiusY,
            SourceRectangleHitGeometry(sourceRectangle, rectangle, global::ProGPU.Scene.SourceHitTestGeometryKind.RoundedRectangle));
    }

    private void AddNativeRoundedRect(VectorBrush? brush, VectorPen? pen, Rect rectangle, double radiusX, double radiusY,
        global::ProGPU.Scene.SourceHitTestGeometry sourceHitGeometry = default)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRoundedRect,
            SourceHitGeometry = sourceHitGeometry,
            Brush = brush,
            Pen = pen,
            Rect = ToNativeRect(rectangle),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenEllipse(brush, pen, new(center.X, center.Y), radiusX, radiusY,
            this, null, out var cachedStatus))
        {
            if (cachedStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return;
        }
        var bounds = new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2);
        var sourceCenter = center;
        var sourceRadiusX = radiusX;
        var sourceRadiusY = radiusY;
        bounds = SnapGuidelines(bounds);
        center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        radiusX = bounds.Width / 2;
        radiusY = bounds.Height / 2;
        var nativeBrush = ToNativeBrush(brush, bounds);
        var nativePen = ToNativePen(pen, bounds);

        var sourceGeometry = sourceCenter.X != center.X || sourceCenter.Y != center.Y ||
            sourceRadiusX != radiusX || sourceRadiusY != radiusY
            ? new global::ProGPU.Scene.SourceHitTestGeometry(global::ProGPU.Scene.SourceHitTestGeometryKind.Ellipse,
                new Vector4((float)sourceCenter.X, (float)sourceCenter.Y, (float)sourceRadiusX, (float)sourceRadiusY))
            : default;
        AddNativeEllipse(nativeBrush, nativePen, center, radiusX, radiusY, sourceGeometry);
    }

    private void AddNativeEllipse(VectorBrush? brush, VectorPen? pen, Point center, double radiusX, double radiusY,
        global::ProGPU.Scene.SourceHitTestGeometry sourceHitGeometry = default)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawEllipse,
            SourceHitGeometry = sourceHitGeometry,
            Brush = brush,
            Pen = pen,
            Position2 = new Vector2((float)center.X, (float)center.Y),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        ThrowIfClosed();

        if (DrawNativeGeometry(brush, pen, geometry))
        {
            return;
        }

        if (_drawingContext != null)
        {
            _drawingContext.DrawGeometry(brush, pen, geometry);
        }
        else
        {
            UnsupportedStateCount++;
        }
    }

    public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenGeometry(brush, pen, geometry, this, null, out var cachedLineStatus))
        {
            if (cachedLineStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return true;
        }
        if (brush == null && pen == null)
        {
            return false;
        }

        if (WpfResourceResolver.TryGetBitmapCachePen(pen, out _, out _)) UnsupportedStateCount++;

        if (TryConvertGeometryToNativePath(geometry, Matrix4x4.Identity, out var path, out var bounds))
        {
            var nativeBrush = ToNativeBrush(brush, bounds);
            var nativePen = ToNativePen(pen, bounds);

            AddNativePath(nativeBrush, nativePen, path);
            return true;
        }

        return false;
    }

    public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenGeometry(brush, pen, geometry, this, null, out var cachedLineStatus))
        {
            if (cachedLineStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return true;
        }
        if (brush == null && pen == null)
        {
            return false;
        }

        if (WpfResourceResolver.TryGetBitmapCachePen(pen, out _, out _)) UnsupportedStateCount++;

        if (!TryConvertPortableGeometryPath(geometry, Matrix4x4.Identity, out var path, out var bounds)
            || (!path.IsCombined && path.Figures.Count == 0))
        {
            return false;
        }

        var nativeBrush = ToNativeBrush(brush, bounds);
        var nativePen = ToNativePen(pen, bounds);
        AddNativePath(nativeBrush, nativePen, path);
        return true;
    }

    private void AddNativePath(VectorBrush? brush, VectorPen? pen, VectorPathGeometry path)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawPath,
            Brush = brush,
            Pen = pen,
            Path = path,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle)
    {
        ThrowIfClosed();

        if (WpfBitmapSourceImageAdapter.TryRetainGpuTexture(
                imageSource,
                NativeContext,
                _context,
                out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
            return;
        }

        if (_drawingContext != null)
        {
            _drawingContext.DrawImage(imageSource, rectangle);
        }
        else
        {
            UnsupportedStateCount++;
        }
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
    {
        ThrowIfClosed();

        if (WpfBitmapSourceImageAdapter.TryRetainGpuTexture(
                imageSource,
                NativeContext,
                _context,
                out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                SrcRect = ToNativeRect(sourceRectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
        }
    }

    bool IWpfNativeVideoCommandSink.DrawNativeVideo(
        PortableMediaPlayerFrame frame,
        WpfReplayRect rectangle)
    {
        ThrowIfClosed();
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
            frame.NativeImage is not global::ProGPU.Backend.IProGpuTextureLeaseSource source)
        {
            return false;
        }

        global::ProGPU.Backend.GpuTexture texture;
        try
        {
            bool retained = _context != null
                ? NativeContext.TryRetainTexture(source, _context, out texture)
                : NativeContext.TryRetainTexture(source, out texture);
            if (!retained)
            {
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
            Texture = texture,
            Rect = ToNativeRect(rectangle),
            Transform = _transformStack.Peek(),
            TextureSamplingMode = _bitmapScalingModeStack.Peek()
        });
        return true;
    }

    public void DrawText(MediaFormattedText formattedText, Point origin)
    {
        ThrowIfClosed();

        if (formattedText == null || formattedText.Font == null)
        {
            return;
        }

        var textBounds = new WpfReplayRect(origin.X, origin.Y, formattedText.Width, formattedText.Height);
        var nativeBrush = formattedText.Foreground == null
            ? new VectorSolidColorBrush(Vector4.One)
            : WpfResourceResolver.AdaptNativeBrush(formattedText.Foreground, textBounds, out _)
                ?? new VectorSolidColorBrush(Vector4.One);
        var position = new Vector2(
            (float)origin.X,
            (float)(origin.Y + formattedText.Height * 0.8));

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawText,
            Text = formattedText.Text,
            Font = formattedText.Font,
            FontSize = (float)formattedText.FontSize,
            Brush = nativeBrush,
            Position = position,
            Transform = _transformStack.Peek(),
            TextRenderingMode = _textRenderingModeStack.Peek(),
            TextHintingMode = _textHintingModeStack.Peek()
        });
    }

    public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
    {
        ThrowIfClosed();
        if (foregroundBrush is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
        {
            if (!((IWpfBitmapCacheBrushCommandSink)this).DrawBitmapCacheBrushGlyphRun(source, glyphRun, null))
                UnsupportedStateCount++;
            return;
        }

        if (foregroundBrush == null || glyphRun == null)
        {
            return;
        }

        var glyphBounds = CreateGlyphRunBounds(glyphRun);
        var nativeBrush = ToNativeGlyphRunBrush(foregroundBrush, glyphBounds) ?? new VectorSolidColorBrush(Vector4.One);
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawGlyphRun,
            GlyphIndices = glyphRun.GlyphIndices,
            GlyphPositions = glyphRun.GlyphPositions,
            Font = glyphRun.Font,
            FontSize = glyphRun.FontSize,
            Brush = nativeBrush,
            Rect = ToNativeRect(glyphBounds),
            Position = glyphRun.Position,
            Transform = glyphRun.Transform * _transformStack.Peek(),
            IsBold = glyphRun.IsBold,
            IsItalic = glyphRun.IsItalic,
            TextRenderingMode = _textRenderingModeStack.Peek(),
            TextHintingMode = _textHintingModeStack.Peek()
        });
    }

    public void PushClip(MediaGeometry clipGeometry)
    {
        ThrowIfClosed();

        if (TryConvertGeometryToNativePath(clipGeometry, _transformStack.Peek(), out var path, out _))
        {
            NativeContext.PushGeometryClip(path);
            _pushStack.Push(PushKind.GeometryClip);
            return;
        }

        if (_drawingContext != null)
        {
            _drawingContext.PushClip(clipGeometry);
            _pushStack.Push(PushKind.DrawingContext);
        }
        else
        {
            UnsupportedStateCount++;
            _pushStack.Push(PushKind.NoOp);
        }
    }

    public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
    {
        ThrowIfClosed();
        if (!TryConvertPortableGeometryPath(clipGeometry, _transformStack.Peek(), out var path, out _)
            || (!path.IsCombined && path.Figures.Count == 0))
        {
            return false;
        }

        NativeContext.PushGeometryClip(path);
        _pushStack.Push(PushKind.GeometryClip);
        return true;
    }

    public bool PushNativeGeometryClip(MediaGeometry clipGeometry)
    {
        ThrowIfClosed();
        if (!TryConvertGeometryToNativePath(clipGeometry, _transformStack.Peek(), out var path, out _))
        {
            return false;
        }

        NativeContext.PushGeometryClip(path);
        _pushStack.Push(PushKind.GeometryClip);
        return true;
    }

    public bool PushNativeGeometryClip(VectorPathGeometry clipGeometry)
    {
        ThrowIfClosed();
        if (clipGeometry == null || !clipGeometry.TryGetBounds(out var minimum, out var maximum)
            || !float.IsFinite(minimum.X) || !float.IsFinite(minimum.Y)
            || !float.IsFinite(maximum.X) || !float.IsFinite(maximum.Y)) return false;
        NativeContext.PushGeometryClip(clipGeometry, _transformStack.Peek());
        _pushStack.Push(PushKind.GeometryClip);
        return true;
    }

    bool IWpfNativeGeometryCommandSink.PushNativeEllipseClip(WpfReplayPoint center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (!float.IsFinite((float)center.X) || !float.IsFinite((float)center.Y)
            || !float.IsFinite((float)radiusX) || !float.IsFinite((float)radiusY)
            || radiusX <= 0 || radiusY <= 0 || (float)radiusX == 0 || (float)radiusY == 0
            || !float.IsFinite((float)(center.X + radiusX)) || !float.IsFinite((float)(center.X - radiusX))
            || !float.IsFinite((float)(center.Y + radiusY)) || !float.IsFinite((float)(center.Y - radiusY))) return false;
        NativeContext.PushEllipseClip(new Vector2((float)center.X, (float)center.Y),
            (float)radiusX, (float)radiusY, _transformStack.Peek());
        _pushStack.Push(PushKind.GeometryClip);
        return true;
    }

    bool IWpfNativeGeometryCommandSink.PushNativeRoundedRectangleClip(WpfReplayRect bounds, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (!double.IsFinite(radiusX) || !double.IsFinite(radiusY) || radiusX < 0 || radiusY < 0
            || !float.IsFinite((float)bounds.X) || !float.IsFinite((float)bounds.Y)
            || !float.IsFinite((float)bounds.Width) || !float.IsFinite((float)bounds.Height)
            || (float)bounds.Width <= 0 || (float)bounds.Height <= 0
            || !float.IsFinite((float)(bounds.X + bounds.Width))
            || !float.IsFinite((float)(bounds.Y + bounds.Height))) return false;
        // Clamp before narrowing; large finite radii remain valid WPF inputs.
        float rx = (float)Math.Min(radiusX, bounds.Width * 0.5);
        float ry = (float)Math.Min(radiusY, bounds.Height * 0.5);
        if ((radiusX > 0 && rx == 0) || (radiusY > 0 && ry == 0)) return false;
        NativeContext.PushRoundedRectangleClip(ToNativeRect(bounds), rx, ry, _transformStack.Peek());
        _pushStack.Push(PushKind.GeometryClip);
        return true;
    }

    void IWpfNativeClipCommandSink.PushNativeClip(WpfReplayRect bounds)
    {
        ThrowIfClosed();
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushClip,
            Rect = ToNativeRect(bounds),
            Transform = _transformStack.Peek()
        });
        _pushStack.Push(PushKind.Clip);
    }

    void IWpfImageHitTestScopeCommandSink.PushImageHitTestScope(WpfReplayRect destination)
        => ((IWpfSourceRectangleHitTestScopeCommandSink)this).PushSourceRectangleHitTestScope(destination);

    void IWpfSourceRectangleHitTestScopeCommandSink.PushSourceRectangleHitTestScope(WpfReplayRect destination)
    {
        ThrowIfClosed();
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushClip,
            Rect = ToNativeRect(destination),
            Transform = _transformStack.Peek(),
            IsImageHitTestScope = true
        });
        _pushStack.Push(PushKind.Clip);
    }

    void IWpfPointHitRegionCommandSink.PushPointHitRegion(WpfReplayRect rectangle, bool isEmpty)
    {
        ThrowIfClosed();
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushOpacity,
            FontSize = 1f,
            IsSourceOpacityScope = true,
            Transform = _transformStack.Peek(),
            SourceHitGeometry = new(isEmpty ? global::ProGPU.Scene.SourceHitTestGeometryKind.PointEmptyBegin :
                global::ProGPU.Scene.SourceHitTestGeometryKind.PointRectangleBegin,
                isEmpty ? default : new Vector4((float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height))
        });
    }

    void IWpfPointHitRegionCommandSink.PopPointHitRegion()
    {
        ThrowIfClosed();
        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PopOpacity,
            SourceHitGeometry = new(global::ProGPU.Scene.SourceHitTestGeometryKind.PointRectangleEnd, default)
        });
    }

    public void PushOpacity(double opacity)
    {
        ThrowIfClosed();
        if (IsIdentityOpacity(opacity))
        {
            _pushStack.Push(PushKind.NoOp);
            return;
        }

        NativeContext.PushOpacity((float)opacity, affectsHitTesting: false);
        _pushStack.Push(PushKind.Opacity);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        ThrowIfClosed();

        if (opacityMask is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
        {
            if (!((IWpfBitmapCacheBrushCommandSink)this).PushBitmapCacheBrushOpacityMask(source,
                    new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height), null))
                throw new NotSupportedException("The cached opacity mask requires finite mapping bounds and typed source state.");
            return;
        }

        if (opacityMask == null)
        {
            PushNoOpScope();
            return;
        }

        var nativeBounds = new global::ProGPU.Scene.Rect(
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height);

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushOpacityMask,
            Brush = AdaptNativeBrush(opacityMask, bounds, count => UnsupportedStateCount += count),
            Rect = nativeBounds,
            Transform = _transformStack.Peek()
        });
        _pushStack.Push(PushKind.OpacityMask);
    }

    public void PushTransform(MediaTransform transform)
    {
        ThrowIfClosed();
        var hasNativeTransform = WpfResourceResolver.TryAdaptTransformMatrix(transform, out var adaptedTransform);
        if (hasNativeTransform && IsIdentityTransform(adaptedTransform))
        {
            _pushStack.Push(PushKind.NoOp);
            return;
        }

        var nativeTransform = hasNativeTransform ? adaptedTransform : Matrix4x4.Identity;
        _transformStack.Push(nativeTransform * _transformStack.Peek());
        _drawingContext?.PushTransform(transform);
        _pushStack.Push(PushKind.Transform);
    }

    public void PushNativeTransform(Matrix4x4 transform)
    {
        ThrowIfClosed();
        if (IsIdentityTransform(transform))
        {
            _pushStack.Push(PushKind.NoOp);
            return;
        }

        _transformStack.Push(transform * _transformStack.Peek());
        _pushStack.Push(PushKind.Transform);
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
    {
        ThrowIfClosed();

        if (WpfResourceResolver.TryGetBitmapCachePen(pen, out var state, out var source))
        {
            if (!((IWpfBitmapCacheBrushCommandSink)this).DrawBitmapCacheBrushLine(source, state, point0, point1, null))
                UnsupportedStateCount++;
            return;
        }

        var nativePen = ToNativePen(pen, CreateLineBounds(point0, point1));
        if (nativePen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawLine,
            Pen = nativePen,
            Position = new Vector2((float)point0.X, (float)point0.Y),
            Position2 = new Vector2((float)point1.X, (float)point1.Y),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenRectangle(brush, pen, rectangle, this, null, out var cachedStatus))
        {
            if (cachedStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return;
        }

        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);
        if (nativeBrush == null && nativePen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRect,
            Brush = nativeBrush,
            Pen = nativePen,
            Rect = ToNativeRect(rectangle),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenRoundedRectangle(brush, pen, rectangle, radiusX, radiusY,
            this, null, out var cachedStatus))
        {
            if (cachedStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return;
        }

        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);
        if (nativeBrush == null && nativePen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRoundedRect,
            Brush = nativeBrush,
            Pen = nativePen,
            Rect = ToNativeRect(rectangle),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (WpfDrawingReplay.TryReplayBitmapCachePenEllipse(brush, pen, center, radiusX, radiusY,
            this, null, out var cachedStatus))
        {
            if (cachedStatus != WpfDrawingReplayStatus.Applied) UnsupportedStateCount++;
            return;
        }

        var bounds = new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2);
        var nativeBrush = ToNativeBrush(brush, bounds);
        var nativePen = ToNativePen(pen, bounds);
        if (nativeBrush == null && nativePen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawEllipse,
            Brush = nativeBrush,
            Pen = nativePen,
            Position2 = new Vector2((float)center.X, (float)center.Y),
            RadiusX = (float)radiusX,
            RadiusY = (float)radiusY,
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
    {
        ThrowIfClosed();

        if (WpfBitmapSourceImageAdapter.TryRetainGpuTexture(
                imageSource,
                NativeContext,
                _context,
                out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
            return;
        }

        UnsupportedStateCount++;
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
    {
        ThrowIfClosed();

        if (WpfBitmapSourceImageAdapter.TryRetainGpuTexture(
                imageSource,
                NativeContext,
                _context,
                out var texture))
        {
            AddNativeCommand(new global::ProGPU.Scene.RenderCommand
            {
                Type = global::ProGPU.Scene.RenderCommandType.DrawTexture,
                Texture = texture,
                Rect = ToNativeRect(rectangle),
                SrcRect = ToNativeRect(sourceRectangle),
                Transform = _transformStack.Peek(),
                TextureSamplingMode = _bitmapScalingModeStack.Peek()
            });
            return;
        }

        UnsupportedStateCount++;
    }

    void IWpfNativePrimitiveCommandSink.DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRunResource)
    {
        ThrowIfClosed();
        if (foregroundBrush is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
        {
            if (!((IWpfBitmapCacheBrushCommandSink)this).DrawBitmapCacheBrushGlyphRun(source, glyphRunResource, null))
                UnsupportedStateCount++;
            return;
        }

        if (foregroundBrush == null
            || !WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRunResource, out var glyphRun))
        {
            return;
        }

        var nativeBrush = ToNativeGlyphRunBrush(foregroundBrush, glyphRun);
        if (nativeBrush == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawGlyphRun,
            GlyphIndices = glyphRun.GlyphIndices,
            GlyphPositions = glyphRun.GlyphPositions,
            Font = glyphRun.Font,
            FontSize = glyphRun.FontSize,
            Brush = nativeBrush,
            Rect = glyphRun.HasBounds ? ToNativeRect(glyphRun.LocalBounds) : default,
            Position = glyphRun.Position,
            Transform = glyphRun.Transform * _transformStack.Peek(),
            IsBold = glyphRun.IsBold,
            IsItalic = glyphRun.IsItalic,
            TextRenderingMode = _textRenderingModeStack.Peek(),
            TextHintingMode = _textHintingModeStack.Peek()
        });
    }

    void IWpfNativePrimitiveCommandSink.PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
    {
        ThrowIfClosed();

        if (opacityMask is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
        {
            if (!((IWpfBitmapCacheBrushCommandSink)this).PushBitmapCacheBrushOpacityMask(source, bounds, null))
                throw new NotSupportedException("The cached opacity mask requires finite mapping bounds and typed source state.");
            return;
        }

        if (opacityMask == null)
        {
            PushNoOpScope();
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.PushOpacityMask,
            Brush = ToNativeBrush(opacityMask, bounds),
            Rect = ToNativeRect(bounds),
            Transform = _transformStack.Peek()
        });
        _pushStack.Push(PushKind.OpacityMask);
    }

    public void PushNoOpScope()
    {
        ThrowIfClosed();
        _pushStack.Push(PushKind.NoOp);
    }

    public void PushGuidelineSet()
    {
        PushNoOpScope();
    }

    public void PushGuidelineSet(object? guidelines)
    {
        ThrowIfClosed();

        if (WpfGuidelineSetReader.TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY))
        {
            _guidelineStack.Push(GuidelineState.FromGuidelineSet(guidelinesX, guidelinesY));
            _pushStack.Push(PushKind.Guideline);
            return;
        }

        _pushStack.Push(PushKind.NoOp);
    }

    public void PushGuidelineY1(double coordinate)
    {
        ThrowIfClosed();
        _guidelineStack.Push(GuidelineState.FromGuidelineY1(coordinate));
        _pushStack.Push(PushKind.Guideline);
    }

    public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        ThrowIfClosed();
        _guidelineStack.Push(GuidelineState.FromGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate));
        _pushStack.Push(PushKind.Guideline);
    }

    public void PushBitmapScalingMode(object? bitmapScalingMode)
    {
        ThrowIfClosed();

        if (WpfBitmapScalingModeMapper.TryMapToTextureSamplingMode(bitmapScalingMode, out var samplingMode))
        {
            _bitmapScalingModeStack.Push(samplingMode);
            _pushStack.Push(PushKind.BitmapScalingMode);
            return;
        }

        if (bitmapScalingMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void PushEdgeMode(object? edgeMode)
    {
        ThrowIfClosed();

        if (WpfEdgeModeMapper.TryMapToAliased(edgeMode, out var isAliased))
        {
            _edgeModeStack.Push(isAliased);
            _pushStack.Push(PushKind.EdgeMode);
            return;
        }

        if (edgeMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void PushTextRenderingMode(object? textRenderingMode)
    {
        ThrowIfClosed();

        if (WpfTextRenderingModeMapper.TryMapToTextRenderingMode(textRenderingMode, out var mode))
        {
            _textRenderingModeStack.Push(mode);
            _pushStack.Push(PushKind.TextRenderingMode);
            return;
        }

        if (textRenderingMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void PushTextHintingMode(object? textHintingMode)
    {
        ThrowIfClosed();

        if (WpfTextRenderingModeMapper.TryMapToTextHintingMode(textHintingMode, out var mode))
        {
            _textHintingModeStack.Push(mode);
            _pushStack.Push(PushKind.TextHintingMode);
            return;
        }

        if (textHintingMode != null)
        {
            UnsupportedStateCount++;
        }

        PushNoOpScope();
    }

    public void Pop()
    {
        ThrowIfClosed();

        if (_pushStack.Count == 0)
        {
            if (_drawingContext != null)
            {
                PopDrawingContext(_drawingContext);
            }

            return;
        }

        var pushKind = _pushStack.Pop();
        if (pushKind == PushKind.Clip)
        {
            NativeContext.PopClip();
            return;
        }

        if (pushKind == PushKind.GeometryClip)
        {
            NativeContext.PopGeometryClip();
            return;
        }

        if (pushKind == PushKind.OpacityMask)
        {
            NativeContext.PopOpacityMask();
            return;
        }

        if (pushKind == PushKind.Opacity)
        {
            NativeContext.PopOpacity();
            return;
        }

        if (pushKind == PushKind.NoOp)
        {
            return;
        }

        if (pushKind == PushKind.Guideline)
        {
            if (_guidelineStack.Count > 0)
            {
                _guidelineStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.BitmapScalingMode)
        {
            if (_bitmapScalingModeStack.Count > 1)
            {
                _bitmapScalingModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.EdgeMode)
        {
            if (_edgeModeStack.Count > 1)
            {
                _edgeModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.TextRenderingMode)
        {
            if (_textRenderingModeStack.Count > 1)
            {
                _textRenderingModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.TextHintingMode)
        {
            if (_textHintingModeStack.Count > 1)
            {
                _textHintingModeStack.Pop();
            }

            return;
        }

        if (pushKind == PushKind.Transform && _transformStack.Count > 1)
        {
            _transformStack.Pop();
        }

        if (_drawingContext != null)
        {
            PopDrawingContext(_drawingContext);
        }
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        if (_drawingContext != null)
        {
            CloseDrawingContext(_drawingContext);
        }

        _pushStack.Dispose();
        _hitTestOwnerStack.Dispose();
        _guidelineStack.Dispose();
        _transformStack.Dispose();
        _bitmapScalingModeStack.Dispose();
        _edgeModeStack.Dispose();
        _textRenderingModeStack.Dispose();
        _textHintingModeStack.Dispose();
        _isClosed = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopDrawingContext(MediaDrawingContext drawingContext)
    {
        drawingContext.Pop();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CloseDrawingContext(MediaDrawingContext drawingContext)
    {
        drawingContext.Close();
    }

    public void Dispose()
    {
        Close();
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new ObjectDisposedException(nameof(ProGpuCompositionCommandSink));
        }
    }

    internal struct SmallValueStack<T> : IDisposable
    {
        private const int InitialArrayCapacity = 4;

        private T _first;
        private T[]? _items;
        private int _count;

        public readonly int Count => _count;

        public void Push(T item)
        {
            if (_count == 0)
            {
                _first = item;
                if (_items != null)
                {
                    _items[0] = item;
                }

                _count = 1;
                return;
            }

            var items = EnsureArray(_count + 1);
            items[_count] = item;
            _count++;
        }

        public T Pop()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("Cannot pop an empty stack.");
            }

            _count--;
            if (_items != null)
            {
                var item = _items[_count];
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    _items[_count] = default!;
                    if (_count == 0)
                    {
                        _first = default!;
                    }
                }

                return item;
            }

            var first = _first;
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _first = default!;
            }

            return first;
        }

        public readonly T Peek()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("Cannot peek an empty stack.");
            }

            return _items != null
                ? _items[_count - 1]
                : _first;
        }

        public readonly T PeekAtDepth(int depth)
        {
            if ((uint)depth >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(depth));
            }

            return _items != null
                ? _items[_count - depth - 1]
                : _first;
        }

        public readonly Enumerator GetEnumerator()
        {
            return new Enumerator(_first, _items, _count);
        }

        public void Dispose()
        {
            if (_items != null)
            {
                ArrayPool<T>.Shared.Return(
                    _items,
                    RuntimeHelpers.IsReferenceOrContainsReferences<T>());
                _items = null;
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _first = default!;
            }

            _count = 0;
        }

        private T[] EnsureArray(int capacity)
        {
            var items = _items;
            if (items == null)
            {
                items = ArrayPool<T>.Shared.Rent(Math.Max(InitialArrayCapacity, capacity));
                items[0] = _first;
                _items = items;
                return items;
            }

            if (capacity <= items.Length)
            {
                return items;
            }

            var larger = ArrayPool<T>.Shared.Rent(Math.Max(capacity, items.Length * 2));
            Array.Copy(items, larger, _count);
            ArrayPool<T>.Shared.Return(
                items,
                RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _items = larger;
            return larger;
        }

        public struct Enumerator
        {
            private readonly T _first;
            private readonly T[]? _items;
            private int _index;

            internal Enumerator(T first, T[]? items, int count)
            {
                _first = first;
                _items = items;
                _index = count;
                Current = default!;
            }

            public T Current { get; private set; }

            public bool MoveNext()
            {
                if (_index == 0)
                {
                    return false;
                }

                _index--;
                Current = _items != null
                    ? _items[_index]
                    : _first;
                return true;
            }
        }
    }

    private static global::ProGPU.Scene.Rect ToNativeRect(Rect rectangle)
    {
        return new global::ProGPU.Scene.Rect(
            (float)rectangle.X,
            (float)rectangle.Y,
            (float)rectangle.Width,
            (float)rectangle.Height);
    }

    private static global::ProGPU.Scene.Rect ToNativeRect(WpfReplayRect rectangle)
    {
        return new global::ProGPU.Scene.Rect(
            (float)rectangle.X,
            (float)rectangle.Y,
            (float)rectangle.Width,
            (float)rectangle.Height);
    }

    private VectorBrush? ToNativeBrush(MediaBrush? brush, WpfReplayRect bounds)
    {
        if (brush == null)
        {
            return null;
        }

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(brush, bounds, out var unsupportedStateCount);
        UnsupportedStateCount += unsupportedStateCount;
        return nativeBrush;
    }

    private VectorPen? ToNativePen(MediaPen? pen, WpfReplayRect bounds)
    {
        if (pen == null)
        {
            return null;
        }

        var nativePen = WpfResourceResolver.AdaptNativePen(pen, bounds, out var unsupportedStateCount);
        UnsupportedStateCount += unsupportedStateCount;
        return nativePen;
    }

    private VectorBrush? ToNativeGlyphRunBrush(MediaBrush foregroundBrush, in WpfNativeGlyphRun glyphRun)
    {
        if (foregroundBrush is MediaSolidColorBrush)
        {
            return ToNativeBrush(foregroundBrush, default(WpfReplayRect));
        }

        if (glyphRun.HasBounds)
        {
            return ToNativeBrush(foregroundBrush, glyphRun.LocalBounds);
        }

        return ToNativeBrush(foregroundBrush, CreateGlyphRunBounds(glyphRun));
    }

    private VectorBrush? ToNativeGlyphRunBrush(MediaBrush foregroundBrush, WpfReplayRect glyphBounds)
    {
        return foregroundBrush is MediaSolidColorBrush
            ? ToNativeBrush(foregroundBrush, default(WpfReplayRect))
            : ToNativeBrush(foregroundBrush, glyphBounds);
    }

    private static WpfReplayRect CreateLineBounds(WpfReplayPoint point0, WpfReplayPoint point1)
    {
        var x1 = Math.Min(point0.X, point1.X);
        var y1 = Math.Min(point0.Y, point1.Y);
        var x2 = Math.Max(point0.X, point1.X);
        var y2 = Math.Max(point0.Y, point1.Y);
        return new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect CreateGlyphRunBounds(MediaGlyphRun glyphRun)
    {
        if (glyphRun.GlyphPositions.Length == 0)
        {
            return new WpfReplayRect(glyphRun.Position.X, glyphRun.Position.Y - glyphRun.FontSize, glyphRun.FontSize, glyphRun.FontSize);
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var originX = glyphRun.Position.X;
        var originY = glyphRun.Position.Y;
        var fontSize = glyphRun.FontSize;
        var glyphPositions = glyphRun.GlyphPositions;
        for (var i = 0; i < glyphPositions.Length; i++)
        {
            var position = glyphPositions[i];
            var x = originX + position.X;
            var y = originY + position.Y;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y - fontSize);
            maxX = Math.Max(maxX, x + fontSize);
            maxY = Math.Max(maxY, y);
        }

        return new WpfReplayRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private static WpfReplayRect CreateGlyphRunBounds(WpfNativeGlyphRun glyphRun)
    {
        if (glyphRun.HasBounds)
        {
            return glyphRun.LocalBounds;
        }

        if (glyphRun.GlyphPositions.Length == 0)
        {
            return new WpfReplayRect(glyphRun.Position.X, glyphRun.Position.Y - glyphRun.FontSize, glyphRun.FontSize, glyphRun.FontSize);
        }

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var originX = glyphRun.Position.X;
        var originY = glyphRun.Position.Y;
        var fontSize = glyphRun.FontSize;
        var glyphPositions = glyphRun.GlyphPositions;
        for (var i = 0; i < glyphPositions.Length; i++)
        {
            var position = glyphPositions[i];
            var x = originX + position.X;
            var y = originY + position.Y;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y - fontSize);
            maxX = Math.Max(maxX, x + fontSize);
            maxY = Math.Max(maxY, y);
        }

        return new WpfReplayRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private Point SnapGuideline(Point point)
    {
        var x = TrySnapGuidelineX(point.X, out var snappedX) ? snappedX : point.X;
        var y = TrySnapGuidelineY(point.Y, out var snappedY) ? snappedY : point.Y;
        return x == point.X && y == point.Y ? point : new Point(x, y);
    }

    private Rect SnapGuidelines(Rect rectangle)
    {
        var left = rectangle.X;
        var right = rectangle.X + rectangle.Width;
        var top = rectangle.Y;
        var bottom = rectangle.Y + rectangle.Height;
        var snappedLeft = TrySnapGuidelineX(left, out var newLeft) ? newLeft : left;
        var snappedRight = TrySnapGuidelineX(right, out var newRight) ? newRight : right;
        var snappedTop = TrySnapGuidelineY(top, out var newTop) ? newTop : top;
        var snappedBottom = TrySnapGuidelineY(bottom, out var newBottom) ? newBottom : bottom;

        if (snappedLeft == left && snappedRight == right && snappedTop == top && snappedBottom == bottom)
        {
            return rectangle;
        }

        return new Rect(
            snappedLeft,
            snappedTop,
            Math.Max(0, snappedRight - snappedLeft),
            Math.Max(0, snappedBottom - snappedTop));
    }

    private bool TrySnapGuidelineX(double x, out double snappedX)
    {
        snappedX = x;
        if (_guidelineStack.Count == 0
            || !TryGetAxisAlignedMapping(
                _transformStack.Peek(),
                out var scaleX,
                out var translateX,
                out _,
                out _))
        {
            return false;
        }

        var guidelineCount = _guidelineStack.Count;
        for (var depth = 0; depth < guidelineCount; depth++)
        {
            var guideline = _guidelineStack.PeekAtDepth(depth);
            if (guideline.TrySnapX(x, scaleX, translateX, out snappedX))
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySnapGuidelineY(double y, out double snappedY)
    {
        snappedY = y;
        if (_guidelineStack.Count == 0
            || !TryGetAxisAlignedMapping(
                _transformStack.Peek(),
                out _,
                out _,
                out var scaleY,
                out var translateY))
        {
            return false;
        }

        var guidelineCount = _guidelineStack.Count;
        for (var depth = 0; depth < guidelineCount; depth++)
        {
            var guideline = _guidelineStack.PeekAtDepth(depth);
            if (guideline.TrySnapY(y, scaleY, translateY, out snappedY))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetAxisAlignedMapping(
        Matrix4x4 transform,
        out double scaleX,
        out double translateX,
        out double scaleY,
        out double translateY)
    {
        scaleX = transform.M11;
        translateX = transform.M41;
        scaleY = transform.M22;
        translateY = transform.M42;

        return !AreClose(scaleX, 0)
            && !AreClose(scaleY, 0)
            && double.IsFinite(scaleX)
            && double.IsFinite(translateX)
            && double.IsFinite(scaleY)
            && double.IsFinite(translateY)
            && AreClose(transform.M12, 0)
            && AreClose(transform.M21, 0)
            && AreClose(transform.M13, 0)
            && AreClose(transform.M14, 0)
            && AreClose(transform.M23, 0)
            && AreClose(transform.M24, 0)
            && AreClose(transform.M31, 0)
            && AreClose(transform.M32, 0)
            && AreClose(transform.M34, 0)
            && AreClose(transform.M43, 0)
            && AreClose(transform.M33, 1)
            && AreClose(transform.M44, 1);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= TransformEpsilon;
    }

    private static bool AreClose(double left, double right, double epsilon)
    {
        return Math.Abs(left - right) <= epsilon;
    }

    private static bool IsIdentityOpacity(double opacity)
    {
        return double.IsFinite(opacity) && AreClose(opacity, 1);
    }

    private static bool IsIdentityTransform(Matrix4x4 transform)
    {
        return AreClose(transform.M11, 1)
            && AreClose(transform.M12, 0)
            && AreClose(transform.M13, 0)
            && AreClose(transform.M14, 0)
            && AreClose(transform.M21, 0)
            && AreClose(transform.M22, 1)
            && AreClose(transform.M23, 0)
            && AreClose(transform.M24, 0)
            && AreClose(transform.M31, 0)
            && AreClose(transform.M32, 0)
            && AreClose(transform.M33, 1)
            && AreClose(transform.M34, 0)
            && AreClose(transform.M41, 0)
            && AreClose(transform.M42, 0)
            && AreClose(transform.M43, 0)
            && AreClose(transform.M44, 1);
    }

    internal static VectorBrush? AdaptNativeBrush(
        MediaBrush? brush,
        Rect bounds,
        Action<int>? reportUnsupportedState = null)
    {
        if (brush == null)
        {
            return null;
        }

        var replayBounds = new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            replayBounds,
            out int unsupportedStateCount);
        reportUnsupportedState?.Invoke(unsupportedStateCount);
        return nativeBrush;
    }

    private VectorBrush? ToNativeBrush(MediaBrush? brush, Rect bounds)
    {
        if (brush == null)
        {
            return null;
        }

        return AdaptNativeBrush(brush, bounds, count => UnsupportedStateCount += count);
    }

    private readonly struct GuidelineState
    {
        private readonly double[] _guidelinesX;
        private readonly double[] _guidelinesY;
        private readonly bool _preserveDrivenYOffset;
        private readonly double _leadingY;
        private readonly double _offsetToDrivenY;
        private readonly byte _inlineYCount;
        private readonly double _inlineY0;
        private readonly double _inlineY1;

        private GuidelineState(
            double[] guidelinesX,
            double[] guidelinesY,
            bool preserveDrivenYOffset,
            double leadingY,
            double offsetToDrivenY,
            byte inlineYCount = 0,
            double inlineY0 = 0,
            double inlineY1 = 0)
        {
            _guidelinesX = guidelinesX;
            _guidelinesY = guidelinesY;
            _preserveDrivenYOffset = preserveDrivenYOffset;
            _leadingY = leadingY;
            _offsetToDrivenY = offsetToDrivenY;
            _inlineYCount = inlineYCount;
            _inlineY0 = inlineY0;
            _inlineY1 = inlineY1;
        }

        public static GuidelineState FromGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            return new GuidelineState(guidelinesX, guidelinesY, preserveDrivenYOffset: false, leadingY: 0, offsetToDrivenY: 0);
        }

        public static GuidelineState FromGuidelineY1(double coordinate)
        {
            return new GuidelineState(
                Array.Empty<double>(),
                Array.Empty<double>(),
                preserveDrivenYOffset: false,
                leadingY: 0,
                offsetToDrivenY: 0,
                inlineYCount: 1,
                inlineY0: coordinate);
        }

        public static GuidelineState FromGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            return new GuidelineState(
                Array.Empty<double>(),
                Array.Empty<double>(),
                preserveDrivenYOffset: true,
                leadingCoordinate,
                offsetToDrivenCoordinate);
        }

        public bool TrySnapX(double x, double scaleX, double translateX, out double snappedX)
        {
            return TrySnapCoordinate(_guidelinesX, x, scaleX, translateX, out snappedX);
        }

        public bool TrySnapY(double y, double scaleY, double translateY, out double snappedY)
        {
            if (_preserveDrivenYOffset)
            {
                if (AreClose(y, _leadingY))
                {
                    snappedY = SnapCoordinate(_leadingY, scaleY, translateY);
                    return true;
                }

                var drivenCoordinate = _leadingY + _offsetToDrivenY;
                if (AreClose(y, drivenCoordinate))
                {
                    var snappedLeading = SnapCoordinate(_leadingY, scaleY, translateY);
                    snappedY = drivenCoordinate + snappedLeading - _leadingY;
                    return true;
                }

                snappedY = y;
                return false;
            }

            if (_inlineYCount != 0)
            {
                return TrySnapInlineY(y, scaleY, translateY, out snappedY);
            }

            return TrySnapCoordinate(_guidelinesY, y, scaleY, translateY, out snappedY);
        }

        private bool TrySnapInlineY(double y, double scaleY, double translateY, out double snappedY)
        {
            if (AreClose(y, _inlineY0))
            {
                snappedY = SnapCoordinate(_inlineY0, scaleY, translateY);
                return true;
            }

            if (_inlineYCount > 1 && AreClose(y, _inlineY1))
            {
                snappedY = SnapCoordinate(_inlineY1, scaleY, translateY);
                return true;
            }

            snappedY = y;
            return false;
        }

        private static bool TrySnapCoordinate(
            double[] guidelines,
            double coordinate,
            double scale,
            double translate,
            out double snappedCoordinate)
        {
            for (var i = 0; i < guidelines.Length; i++)
            {
                var guideline = guidelines[i];
                if (AreClose(coordinate, guideline))
                {
                    snappedCoordinate = SnapCoordinate(guideline, scale, translate);
                    return true;
                }
            }

            snappedCoordinate = coordinate;
            return false;
        }

        private static double SnapCoordinate(double coordinate, double scale, double translate)
        {
            var deviceCoordinate = coordinate * scale + translate;
            var snappedDeviceCoordinate = Math.Round(deviceCoordinate, MidpointRounding.AwayFromZero);
            return (snappedDeviceCoordinate - translate) / scale;
        }
    }

    private VectorPen? ToNativePen(MediaPen? pen, Rect bounds)
    {
        if (pen == null)
        {
            return null;
        }

        var replayBounds = new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        return ToNativePen(pen, replayBounds);
    }

    private static VectorPenLineCap ToNativeLineCap(MediaPenLineCap lineCap)
    {
        return lineCap switch
        {
            MediaPenLineCap.Square => VectorPenLineCap.Square,
            MediaPenLineCap.Round => VectorPenLineCap.Round,
            MediaPenLineCap.Triangle => VectorPenLineCap.Triangle,
            _ => VectorPenLineCap.Flat
        };
    }

    private static VectorPen WithLineCaps(VectorPen pen, MediaPenLineCap startLineCap, MediaPenLineCap endLineCap)
    {
        return new VectorPen(
            pen.Brush,
            pen.Thickness,
            pen.LineJoin,
            pen.MiterLimit,
            ToNativeLineCap(startLineCap),
            ToNativeLineCap(endLineCap),
            pen.DashCap);
    }

    private static bool TryConvertGeometryToNativePath(
        MediaGeometry geometry,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        out WpfReplayRect bounds,
        bool allowEmpty = false)
    {
        if (TryGetCachedNativeGeometryPath(geometry, transform, out path, out bounds))
        {
            return allowEmpty || path.IsCombined || path.Figures.Count > 0;
        }

        return TryConvertGeometryToNativePathCore(geometry, transform, out path, out bounds, allowEmpty);
    }

    private static bool TryConvertGeometryToNativePathCore(
        MediaGeometry geometry,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        out WpfReplayRect bounds,
        bool allowEmpty)
    {
        if (geometry is PortableGeometryPathSource portableGeometry
            && portableGeometry.TryGetPortableGeometryPath(out var portablePath)
            && TryConvertPortableGeometryPath(portablePath, transform, out path, out bounds))
        {
            return allowEmpty || path.IsCombined || path.Figures.Count > 0;
        }

        if (geometry is NativePathGeometrySource nativePathSource
            && nativePathSource.TryGetPathGeometry(out path, out var nativeTransform))
        {
            var combinedTransform = nativeTransform * transform;
            if (!combinedTransform.IsIdentity)
            {
                path = path.CreateTransformed(combinedTransform);
            }

            bounds = WpfPortablePathGeometryConverter.GetBoundsOrEmpty(path);
            return allowEmpty || path.IsCombined || path.Figures.Count > 0;
        }

        path = new VectorPathGeometry();
        bounds = WpfReplayRect.Empty;
        return false;
    }

    private static bool TryConvertPortableGeometryPath(
        PortableGeometryPath portablePath,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        out WpfReplayRect bounds)
    {
        if (!WpfPortablePathGeometryConverter.TryConvert(portablePath, transform, out path, out bounds))
        {
            return false;
        }

        if (transform.IsIdentity
            && WpfPortablePathBoundsReader.TryGetPathBounds(portablePath, out var portableBounds))
        {
            bounds = portableBounds;
        }

        return true;
    }

    private static bool TryGetCachedNativeGeometryPath(
        MediaGeometry geometry,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        out WpfReplayRect bounds)
    {
        if (!transform.IsIdentity || !TryReadNativeGeometryPathKey(geometry, out var key))
        {
            path = null!;
            bounds = default;
            return false;
        }

        return s_nativeGeometryPathCache.GetOrCreateValue(geometry).TryGetOrCreate(geometry, key, out path, out bounds);
    }

    private static bool TryReadNativeGeometryPathKey(MediaGeometry geometry, out NativeGeometryPathKey key)
    {
        var hash = NativeGeometryPathKeyOffset;
        var figureCount = 0;
        var segmentCount = 0;
        var geometryCount = 0;
        if (!AddNativeGeometryPathKey(geometry, ref hash, ref figureCount, ref segmentCount, ref geometryCount, depth: 0))
        {
            key = default;
            return false;
        }

        key = new NativeGeometryPathKey(hash, figureCount, segmentCount, geometryCount);
        return true;
    }

    private static bool AddNativeGeometryPathKey(
        MediaGeometry geometry,
        ref ulong hash,
        ref int figureCount,
        ref int segmentCount,
        ref int geometryCount,
        int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        geometryCount++;
        if (!AddGeometryTransformKey(geometry.Transform, ref hash))
        {
            return false;
        }

        switch (geometry)
        {
            case MediaPathGeometry pathGeometry:
                AddHash(ref hash, 1);
                return AddNativePathGeometryKey(pathGeometry, ref hash, ref figureCount, ref segmentCount);
            case MediaLineGeometry lineGeometry:
                AddHash(ref hash, 2);
                AddPointHash(ref hash, lineGeometry.StartPoint);
                AddPointHash(ref hash, lineGeometry.EndPoint);
                figureCount++;
                segmentCount++;
                return true;
            case MediaRectangleGeometry rectangleGeometry:
                AddHash(ref hash, 3);
                AddRectHash(ref hash, rectangleGeometry.Rect);
                AddHash(ref hash, rectangleGeometry.RadiusX);
                AddHash(ref hash, rectangleGeometry.RadiusY);
                figureCount++;
                segmentCount += 4;
                return true;
            case MediaEllipseGeometry ellipseGeometry:
                AddHash(ref hash, 4);
                AddPointHash(ref hash, ellipseGeometry.Center);
                AddHash(ref hash, ellipseGeometry.RadiusX);
                AddHash(ref hash, ellipseGeometry.RadiusY);
                figureCount++;
                segmentCount += 4;
                return true;
            case MediaGeometryGroup:
                // The lightweight compile-time shim and real WPF expose different
                // Children return types. Avoid binding to that getter and use the
                // existing uncached portable conversion path for geometry groups.
                return false;
            case MediaCombinedGeometry combinedGeometry:
                AddHash(ref hash, 6);
                AddHash(ref hash, (int)combinedGeometry.GeometryCombineMode);
                if (!AddOptionalNativeGeometryPathKey(combinedGeometry.Geometry1, ref hash, ref figureCount, ref segmentCount, ref geometryCount, depth + 1))
                {
                    return false;
                }

                return AddOptionalNativeGeometryPathKey(combinedGeometry.Geometry2, ref hash, ref figureCount, ref segmentCount, ref geometryCount, depth + 1);
            default:
                return false;
        }
    }

    private static bool AddOptionalNativeGeometryPathKey(
        MediaGeometry? geometry,
        ref ulong hash,
        ref int figureCount,
        ref int segmentCount,
        ref int geometryCount,
        int depth)
    {
        if (geometry == null)
        {
            AddHash(ref hash, 0);
            return true;
        }

        return AddNativeGeometryPathKey(geometry, ref hash, ref figureCount, ref segmentCount, ref geometryCount, depth);
    }

    private static bool AddNativePathGeometryKey(
        MediaPathGeometry pathGeometry,
        ref ulong hash,
        ref int figureCount,
        ref int segmentCount)
    {
        AddHash(ref hash, (int)pathGeometry.FillRule);
        var figures = pathGeometry.Figures;
        if (figures == null)
        {
            return false;
        }

        figureCount += figures.Count;
        AddHash(ref hash, figures.Count);
        for (var figureIndex = 0; figureIndex < figures.Count; figureIndex++)
        {
            var figure = figures[figureIndex];
            if (figure == null || figure.Segments == null)
            {
                return false;
            }

            AddPointHash(ref hash, figure.StartPoint);
            AddHash(ref hash, figure.IsClosed ? 1 : 0);
            AddHash(ref hash, figure.IsFilled ? 1 : 0);

            var segments = figure.Segments;
            segmentCount += segments.Count;
            AddHash(ref hash, segments.Count);
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                if (!AddNativePathSegmentKey(segments[segmentIndex], ref hash))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool AddNativePathSegmentKey(object? segment, ref ulong hash)
    {
        switch (segment)
        {
            case MediaLineSegment lineSegment:
                AddHash(ref hash, 1);
                AddPointHash(ref hash, lineSegment.Point);
                AddPathSegmentFlagsHash(ref hash, lineSegment);
                return true;
            case MediaQuadraticBezierSegment quadraticBezierSegment:
                AddHash(ref hash, 2);
                AddPointHash(ref hash, quadraticBezierSegment.Point1);
                AddPointHash(ref hash, quadraticBezierSegment.Point2);
                AddPathSegmentFlagsHash(ref hash, quadraticBezierSegment);
                return true;
            case MediaBezierSegment bezierSegment:
                AddHash(ref hash, 3);
                AddPointHash(ref hash, bezierSegment.Point1);
                AddPointHash(ref hash, bezierSegment.Point2);
                AddPointHash(ref hash, bezierSegment.Point3);
                AddPathSegmentFlagsHash(ref hash, bezierSegment);
                return true;
            case MediaArcSegment arcSegment:
                AddHash(ref hash, 4);
                AddPointHash(ref hash, arcSegment.Point);
                AddSizeHash(ref hash, arcSegment.Size);
                AddHash(ref hash, arcSegment.RotationAngle);
                AddHash(ref hash, arcSegment.IsLargeArc ? 1 : 0);
                AddHash(ref hash, (int)arcSegment.SweepDirection);
                AddPathSegmentFlagsHash(ref hash, arcSegment);
                return true;
            default:
                return false;
        }
    }

    private static bool AddGeometryTransformKey(MediaTransform? transform, ref ulong hash)
    {
        if (transform == null)
        {
            AddMatrixHash(ref hash, Matrix4x4.Identity);
            return true;
        }

        if (!WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix))
        {
            return false;
        }

        AddMatrixHash(ref hash, matrix);
        return true;
    }

    private static void AddPathSegmentFlagsHash(ref ulong hash, MediaPathSegment segment)
    {
        AddHash(ref hash, segment.IsSmoothJoin ? 1 : 0);
        AddHash(ref hash, segment.IsStroked ? 1 : 0);
    }

    private static void AddPointHash(ref ulong hash, Point point)
    {
        AddHash(ref hash, point.X);
        AddHash(ref hash, point.Y);
    }

    private static void AddSizeHash(ref ulong hash, Size size)
    {
        AddHash(ref hash, size.Width);
        AddHash(ref hash, size.Height);
    }

    private static void AddRectHash(ref ulong hash, Rect rect)
    {
        AddHash(ref hash, rect.X);
        AddHash(ref hash, rect.Y);
        AddHash(ref hash, rect.Width);
        AddHash(ref hash, rect.Height);
    }

    private static void AddMatrixHash(ref ulong hash, Matrix4x4 matrix)
    {
        AddHash(ref hash, matrix.M11);
        AddHash(ref hash, matrix.M12);
        AddHash(ref hash, matrix.M13);
        AddHash(ref hash, matrix.M14);
        AddHash(ref hash, matrix.M21);
        AddHash(ref hash, matrix.M22);
        AddHash(ref hash, matrix.M23);
        AddHash(ref hash, matrix.M24);
        AddHash(ref hash, matrix.M31);
        AddHash(ref hash, matrix.M32);
        AddHash(ref hash, matrix.M33);
        AddHash(ref hash, matrix.M34);
        AddHash(ref hash, matrix.M41);
        AddHash(ref hash, matrix.M42);
        AddHash(ref hash, matrix.M43);
        AddHash(ref hash, matrix.M44);
    }

    private static void AddHash(ref ulong hash, double value)
    {
        AddHash(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
    }

    private static void AddHash(ref ulong hash, float value)
    {
        AddHash(ref hash, BitConverter.SingleToUInt32Bits(value));
    }

    private static void AddHash(ref ulong hash, int value)
    {
        AddHash(ref hash, unchecked((uint)value));
    }

    private static void AddHash(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= NativeGeometryPathKeyPrime;
    }

    private readonly record struct NativeGeometryPathKey(
        ulong Hash,
        int FigureCount,
        int SegmentCount,
        int GeometryCount);

    private sealed class NativeGeometryPathCache
    {
        private bool _hasPath;
        private NativeGeometryPathKey _key;
        private VectorPathGeometry? _path;
        private WpfReplayRect _bounds;

        public bool TryGetOrCreate(MediaGeometry geometry, NativeGeometryPathKey key, out VectorPathGeometry path, out WpfReplayRect bounds)
        {
            if (_hasPath && _key == key)
            {
                path = _path!;
                bounds = _bounds;
                return true;
            }

            if (!TryConvertGeometryToNativePathCore(geometry, Matrix4x4.Identity, out path, out bounds, allowEmpty: true))
            {
                path = null!;
                bounds = default;
                return false;
            }

            _key = key;
            _path = path;
            _bounds = bounds;
            _hasPath = true;
            return true;
        }
    }

}
