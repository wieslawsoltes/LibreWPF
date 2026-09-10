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

namespace System.Windows.Media.ProGPU.Composition;

public sealed class ProGpuCompositionCommandSink :
    IWpfCompositionCommandSink,
    IWpfViewport3DCommandSink,
    IWpfCompositionCommandSinkDiagnostics,
    IWpfNativeTransformCommandSink,
    IWpfNativePrimitiveCommandSink,
    IWpfNativeClipCommandSink,
    IWpfNativeGeometryCommandSink,
    IWpfHitTestOwnerScopeCommandSink,
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

        point0 = SnapGuideline(point0);
        point1 = SnapGuideline(point1);
        var bounds = new Rect(point0, point1);
        if (pen == null || ToNativePen(pen, bounds) is not { } nativePen)
        {
            return;
        }

        AddNativeLine(nativePen, point0, point1, pen.StartLineCap, pen.EndLineCap);
    }

    private void AddNativeLine(
        VectorPen pen,
        Point point0,
        Point point1,
        MediaPenLineCap startLineCap = MediaPenLineCap.Flat,
        MediaPenLineCap endLineCap = MediaPenLineCap.Flat)
    {
        var originalPoint0 = point0;
        var originalPoint1 = point1;
        ApplySquareLineCaps(pen, ref point0, ref point1, startLineCap, endLineCap);

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawLine,
            Pen = pen,
            Position = new Vector2((float)point0.X, (float)point0.Y),
            Position2 = new Vector2((float)point1.X, (float)point1.Y),
            Transform = _transformStack.Peek(),
            IsEdgeAliased = _edgeModeStack.Peek()
        });

        AddRoundLineCap(pen, originalPoint0, startLineCap);
        AddRoundLineCap(pen, originalPoint1, endLineCap);
        AddTriangleLineCap(pen, originalPoint0, originalPoint1, startLineCap, isStart: true);
        AddTriangleLineCap(pen, originalPoint0, originalPoint1, endLineCap, isStart: false);
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

    private void AddRoundLineCap(VectorPen pen, Point point, MediaPenLineCap lineCap)
    {
        if (lineCap != MediaPenLineCap.Round || pen.Thickness <= TransformEpsilon)
        {
            return;
        }

        var radius = pen.Thickness / 2;
        AddNativeEllipse(pen.Brush, null, point, radius, radius);
    }

    private void AddTriangleLineCap(
        VectorPen pen,
        Point point0,
        Point point1,
        MediaPenLineCap lineCap,
        bool isStart)
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
        rectangle = SnapGuidelines(rectangle);
        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);

        AddNativeRect(nativeBrush, nativePen, rectangle);
    }

    private void AddNativeRect(VectorBrush? brush, VectorPen? pen, Rect rectangle)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRect,
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
        rectangle = SnapGuidelines(rectangle);
        var nativeBrush = ToNativeBrush(brush, rectangle);
        var nativePen = ToNativePen(pen, rectangle);

        AddNativeRoundedRect(nativeBrush, nativePen, rectangle, radiusX, radiusY);
    }

    private void AddNativeRoundedRect(VectorBrush? brush, VectorPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawRoundedRect,
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
        var bounds = new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2);
        bounds = SnapGuidelines(bounds);
        center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        radiusX = bounds.Width / 2;
        radiusY = bounds.Height / 2;
        var nativeBrush = ToNativeBrush(brush, bounds);
        var nativePen = ToNativePen(pen, bounds);

        AddNativeEllipse(nativeBrush, nativePen, center, radiusX, radiusY);
    }

    private void AddNativeEllipse(VectorBrush? brush, VectorPen? pen, Point center, double radiusX, double radiusY)
    {
        if (brush == null && pen == null)
        {
            return;
        }

        AddNativeCommand(new global::ProGPU.Scene.RenderCommand
        {
            Type = global::ProGPU.Scene.RenderCommandType.DrawEllipse,
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
        if (brush == null && pen == null)
        {
            return false;
        }

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
        if (brush == null && pen == null)
        {
            return false;
        }

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

        if (WpfBitmapSourceImageAdapter.TryGetGpuTexture(imageSource, out var texture))
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

        if (WpfBitmapSourceImageAdapter.TryGetGpuTexture(imageSource, out var texture))
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

    public void PushOpacity(double opacity)
    {
        ThrowIfClosed();
        if (IsIdentityOpacity(opacity))
        {
            _pushStack.Push(PushKind.NoOp);
            return;
        }

        NativeContext.PushOpacity((float)opacity);
        _pushStack.Push(PushKind.Opacity);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        ThrowIfClosed();

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

        if (WpfBitmapSourceImageAdapter.TryGetGpuTexture(imageSource, out var texture))
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

        if (WpfBitmapSourceImageAdapter.TryGetGpuTexture(imageSource, out var texture))
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
        return ScalePenThicknessToDeviceSpace(nativePen);
    }

    /// <summary>
    /// Converts an adapted pen's thickness out of local space, matching the contract ProGPU's
    /// render commands are built on.
    /// </summary>
    /// <remarks>
    /// A render command carries its geometry in local space plus a Transform, but its
    /// Pen.Thickness is expected to already be in device space: the compositor transforms the
    /// geometry and then expands the stroke by the thickness verbatim (Compositor.CompileLineCommand),
    /// and divides the stroke scale back out on the few paths that need local units. ProGPU's own
    /// WPF DrawingContext upholds this by multiplying by the current stroke scale before emitting a
    /// command. This sink did not, so every stroke under a scaling transform kept its unscaled
    /// width - most visibly, a Border with a non-uniform BorderThickness inside a Viewbox filled
    /// solid once its edges grew wider than the shrunken box. The adapted pen can be a shared
    /// cached instance, so return a copy instead of mutating it.
    /// </remarks>
    private VectorPen? ScalePenThicknessToDeviceSpace(VectorPen? pen)
    {
        if (pen == null)
        {
            return null;
        }

        var strokeScale = global::ProGPU.Vector.TransformMetrics.GetStrokeScale(_transformStack.Peek());
        if (!float.IsFinite(strokeScale) || strokeScale <= 0f ||
            Math.Abs(strokeScale - 1f) <= TransformEpsilon)
        {
            return pen;
        }

        return new VectorPen(
            pen.Brush,
            pen.Thickness * strokeScale,
            pen.LineJoin,
            pen.MiterLimit,
            pen.StartLineCap,
            pen.EndLineCap,
            pen.DashCap,
            pen.DashArray,
            pen.DashOffset);
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
