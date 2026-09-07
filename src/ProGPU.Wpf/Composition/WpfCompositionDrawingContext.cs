using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using MediaTransform = System.Windows.Media.Transform;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableMediaPlayerSource = ProGPU.Wpf.Interop.IPortableMediaPlayerSource;
using PortableRectAnimationValueSource = ProGPU.Wpf.Interop.IPortableRectAnimationValueSource;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfCompositionDrawingContext : IWpfGeneratedRenderDataDrawingContext, IDisposable
{
    private readonly IWpfCompositionCommandSink _sink;
    private readonly Func<object?, MediaImageSource?>? _imageSourceAdapter;
    private int _stackDepth;
    private int _operationCount;
    private int _appliedCount;
    private int _unsupportedCount;
    private bool _isClosed;

    public WpfCompositionDrawingContext(
        IWpfCompositionCommandSink sink,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _imageSourceAdapter = imageSourceAdapter == null ? null : imageSourceAdapter.AdaptImageSource;
    }

    public MediaDrawingContext? DrawingContext => _sink.DrawingContext;

    public int StackDepth => _stackDepth;

    public WpfCompositionDrawingContextResult Result => new(
        _operationCount,
        _appliedCount,
        _unsupportedCount);

    public void DrawLine(MediaPen? pen, Point point0, Point point1)
    {
        ThrowIfClosed();
        if (pen == null)
        {
            return;
        }

        RegisterRetainedDependencies(pen);
        _sink.DrawLine(pen, point0, point1);
        CountApplied();
    }

    public void DrawLine(
        MediaPen? pen,
        Point point0,
        object? point0Animations,
        Point point1,
        object? point1Animations)
    {
        ThrowIfClosed();
        if (pen == null)
        {
            return;
        }

        RegisterRetainedDependencies(pen);
        _sink.DrawLine(pen, point0, point1);
        CountApplied();
        CountUnsupportedStateIfAny(point0Animations, point1Animations);
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        if (brush != null && TryReplayTileBrushRectangle(brush, pen, rectangle))
        {
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawRectangle(brush, pen, rectangle);
        CountApplied();
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawRectangle(brush, pen, rectangle);
        CountApplied();
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        CountApplied();
    }

    public void DrawRoundedRectangle(
        MediaBrush? brush,
        MediaPen? pen,
        Rect rectangle,
        object? rectangleAnimations,
        double radiusX,
        object? radiusXAnimations,
        double radiusY,
        object? radiusYAnimations)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        CountApplied();
        CountUnsupportedStateIfAny(rectangleAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (brush == null && pen == null)
        {
            return;
        }

        if (brush != null
            && WpfDrawingReplay.IsSourceBrush(brush)
            && WpfDrawingReplay.TryReplaySourceBrushEllipseFill(
                brush,
                center,
                radiusX,
                radiusY,
                _sink,
                _imageSourceAdapter,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen);
            if (pen != null)
            {
                DrawEllipsePenAfterTileBrush(pen, center, radiusX, radiusY);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
        CountApplied();
    }

    public void DrawEllipse(
        MediaBrush? brush,
        MediaPen? pen,
        Point center,
        object? centerAnimations,
        double radiusX,
        object? radiusXAnimations,
        double radiusY,
        object? radiusYAnimations)
    {
        DrawEllipse(brush, pen, center, radiusX, radiusY);
        CountUnsupportedStateIfAny(centerAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry? geometry)
    {
        ThrowIfClosed();
        if ((brush == null && pen == null) || geometry == null)
        {
            return;
        }

        if (brush != null && TryReplayTileBrushGeometry(brush, pen, geometry))
        {
            return;
        }

        if (TryDrawPrimitiveLineGeometry(brush, pen, geometry))
        {
            return;
        }

        if (TryDrawPrimitivePolylineGeometry(brush, pen, geometry))
        {
            return;
        }

        if (TryDrawPrimitiveRectangleGeometry(brush, pen, geometry))
        {
            return;
        }

        if (TryDrawPrimitiveEllipseGeometry(brush, pen, geometry))
        {
            return;
        }

        if (TryDrawNativePortableGeometry(brush, pen, geometry))
        {
            return;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        _sink.DrawGeometry(brush, pen, geometry);
        CountApplied();
    }

    public void DrawImage(MediaImageSource? imageSource, Rect rectangle)
    {
        ThrowIfClosed();
        if (imageSource == null)
        {
            return;
        }

        if (WpfDrawingReplay.TryReplayDrawingImage(
            imageSource,
            rectangle,
            _sink,
            _imageSourceAdapter,
            out var drawingImageStatus))
        {
            RegisterRetainedDependencies(imageSource);
            CountDrawingReplayStatus(drawingImageStatus);
            return;
        }

        RegisterRetainedDependencies(imageSource);
        _sink.DrawImage(imageSource, rectangle);
        CountApplied();
    }

    public void DrawImage(MediaImageSource? imageSource, Rect rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (imageSource == null)
        {
            return;
        }

        if (WpfDrawingReplay.TryReplayDrawingImage(
            imageSource,
            rectangle,
            _sink,
            _imageSourceAdapter,
            out var drawingImageStatus))
        {
            RegisterRetainedDependencies(imageSource);
            CountDrawingReplayStatus(drawingImageStatus);
            CountUnsupportedStateIfAny(rectangleAnimations);
            return;
        }

        RegisterRetainedDependencies(imageSource);
        _sink.DrawImage(imageSource, rectangle);
        CountApplied();
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawText(MediaFormattedText formattedText, Point origin)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(formattedText);
        RegisterRetainedDependencies(formattedText);
        _sink.DrawText(formattedText, origin);
        CountApplied();
    }

    public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun? glyphRun)
    {
        ThrowIfClosed();
        if (foregroundBrush == null || glyphRun == null)
        {
            return;
        }

        RegisterRetainedDependencies(foregroundBrush, glyphRun);
        _sink.DrawGlyphRun(foregroundBrush, glyphRun);
        CountApplied();
    }

    public WpfDrawingReplayStatus DrawDrawing(object? drawing, IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        Func<object?, MediaImageSource?>? adapter = imageSourceAdapter == null
            ? null
            : imageSourceAdapter.AdaptImageSource;

        return DrawDrawing(drawing, adapter);
    }

    public WpfDrawingReplayStatus DrawDrawing(
        object? drawing,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();

        var status = WpfDrawingReplay.Replay(drawing, _sink, imageSourceAdapter);
        if (status is WpfDrawingReplayStatus.Applied or WpfDrawingReplayStatus.PartiallyApplied)
        {
            RegisterRetainedDependencies(drawing);
        }

        CountDrawingReplayStatus(status);
        return status;
    }

    void IWpfGeneratedRenderDataDrawingContext.DrawDrawing(object? drawing)
    {
        DrawDrawing(drawing, (IWpfImageSourceAdapter?)null);
    }

    public void DrawVideo(object? player, Rect rectangle)
    {
        ThrowIfClosed();
        if (player == null)
        {
            return;
        }

        DrawPortableVideo(player, new WpfReplayRect(
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height));
    }

    public void DrawVideo(object? player, Rect rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (player == null)
        {
            return;
        }

        WpfReplayRect replayRectangle = new(
            rectangle.X,
            rectangle.Y,
            rectangle.Width,
            rectangle.Height);
        bool animationResolved = false;
        if (rectangleAnimations is PortableRectAnimationValueSource animation &&
            animation.TryGetPortableRectAnimationValue(out var animatedRectangle))
        {
            animationResolved = true;
            replayRectangle = new WpfReplayRect(
                animatedRectangle.X,
                animatedRectangle.Y,
                animatedRectangle.Width,
                animatedRectangle.Height);
        }
        bool unsupportedAnimation = rectangleAnimations != null &&
            !animationResolved;
        bool hasTypedFrame = DrawPortableVideo(player, replayRectangle);
        if (hasTypedFrame && unsupportedAnimation)
        {
            CountUnsupportedStateIfAny(rectangleAnimations);
        }
    }

    private bool DrawPortableVideo(object player, WpfReplayRect rectangle)
    {
        if (player is not PortableMediaPlayerSource source ||
            !source.TryGetPortableMediaPlayerFrame(out var frame))
        {
            CountUnsupported();
            return false;
        }
        if (_sink is not IWpfNativeVideoCommandSink videoSink ||
            !videoSink.DrawNativeVideo(frame, rectangle))
        {
            CountUnsupported();
            return true;
        }

        RegisterRetainedDependencies(player, frame.NativeImage);
        CountApplied();
        return true;
    }

    public void PushClip(MediaGeometry? clipGeometry)
    {
        ThrowIfClosed();
        if (clipGeometry == null)
        {
            _sink.PushNoOpScope();
        }
        else if (TryPushPrimitiveRectangleClip(clipGeometry))
        {
            RegisterRetainedDependencies(clipGeometry);
        }
        else if (TryPushNativeMediaGeometryClip(clipGeometry))
        {
            RegisterRetainedDependencies(clipGeometry);
        }
        else
        {
            RegisterRetainedDependencies(clipGeometry);
            _sink.PushClip(clipGeometry);
        }

        _stackDepth++;
        CountApplied();
    }

    private bool TryPushPrimitiveRectangleClip(MediaGeometry clipGeometry)
    {
        if (_sink is IWpfNativeClipCommandSink nativeClipSink
            && WpfMediaRectangleClipReader.TryGetRectangleClipBounds(clipGeometry, out var clipBounds))
        {
            nativeClipSink.PushNativeClip(clipBounds);
            return true;
        }

        return false;
    }

    private bool TryPushNativeMediaGeometryClip(MediaGeometry clipGeometry)
    {
        return _sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.PushNativeGeometryClip(clipGeometry);
    }

    public void PushOpacity(double opacity)
    {
        ThrowIfClosed();
        _sink.PushOpacity(opacity);
        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(double opacity, object? opacityAnimations)
    {
        ThrowIfClosed();
        _sink.PushOpacity(opacity);
        _stackDepth++;
        CountApplied();
        CountUnsupportedStateIfAny(opacityAnimations);
    }

    public void PushOpacityMask(MediaBrush? opacityMask)
    {
        PushOpacityMask(opacityMask, Rect.Empty);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        ThrowIfClosed();
        if (opacityMask == null)
        {
            _sink.PushNoOpScope();
        }
        else
        {
            RegisterRetainedDependencies(opacityMask);
            _sink.PushOpacityMask(opacityMask, bounds);
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushTransform(MediaTransform? transform)
    {
        ThrowIfClosed();
        if (transform == null)
        {
            _sink.PushNoOpScope();
        }
        else
        {
            RegisterRetainedDependencies(transform);
            WpfPortableCommandSinkBridge.PushTransform(_sink, transform);
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineSet()
    {
        PushGuidelineSet(guidelines: null);
    }

    public void PushGuidelineSet(object? guidelines)
    {
        ThrowIfClosed();

        RegisterRetainedDependencies(guidelines);
        if (WpfGuidelineSetReader.TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY))
        {
            if (guidelinesX.Length == 0 && guidelinesY.Length == 2)
            {
                _sink.PushGuidelineY2(guidelinesY[0], guidelinesY[1] - guidelinesY[0]);
            }
            else if (guidelinesX.Length == 0 && guidelinesY.Length == 1)
            {
                _sink.PushGuidelineY1(guidelinesY[0]);
            }
            else
            {
                _sink.PushGuidelineSet(guidelines);
            }
        }
        else
        {
            _sink.PushGuidelineSet();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineY1(double coordinate)
    {
        ThrowIfClosed();
        _sink.PushGuidelineY1(coordinate);
        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        ThrowIfClosed();
        _sink.PushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
        _stackDepth++;
        CountApplied();
    }

    public void PushEffect(object? effect, object? effectInput)
    {
        ThrowIfClosed();

        if (WpfEffectMapper.TryCreateProGpuPushEffect(effect, effectInput, out var proGpuEffect)
            && _sink is IWpfVisualEffectCommandSink effectSink
            && effectSink.PushVisualEffect(proGpuEffect))
        {
            RegisterRetainedDependencies(effect, effectInput);
            _stackDepth++;
            CountApplied();
            return;
        }

        _sink.PushNoOpScope();
        _stackDepth++;
        CountPartiallyApplied();
    }

    public void Pop()
    {
        ThrowIfClosed();

        if (_stackDepth <= 0)
        {
            throw new InvalidOperationException("Cannot pop more drawing-context scopes than were pushed.");
        }

        _sink.Pop();
        _stackDepth--;
        CountApplied();
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        while (_stackDepth > 0)
        {
            _sink.Pop();
            _stackDepth--;
            CountApplied();
        }

        _sink.Close();
        _isClosed = true;
    }

    public void Dispose()
    {
        Close();
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_isClosed, this);
    }

    private void CountApplied()
    {
        _operationCount++;
        _appliedCount++;
    }

    private void CountUnsupported()
    {
        _operationCount++;
        _unsupportedCount++;
    }

    private void CountPartiallyApplied()
    {
        _operationCount++;
        _appliedCount++;
        _unsupportedCount++;
    }

    private void CountDrawingReplayStatus(WpfDrawingReplayStatus status)
    {
        switch (status)
        {
            case WpfDrawingReplayStatus.Applied:
                CountApplied();
                break;
            case WpfDrawingReplayStatus.PartiallyApplied:
                CountPartiallyApplied();
                break;
            case WpfDrawingReplayStatus.Unsupported:
                CountUnsupported();
                break;
        }
    }

    private bool TryReplayTileBrushRectangle(MediaBrush brush, MediaPen? pen, Rect rectangle)
    {
        if (!WpfDrawingReplay.IsSourceBrush(brush))
        {
            return false;
        }

        if (WpfDrawingReplay.TryReplaySourceBrushFill(
                brush,
                rectangle,
                _sink,
                _imageSourceAdapter,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen);
            if (pen != null)
            {
                DrawRectanglePenAfterTileBrush(pen, rectangle);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return true;
        }

        return false;
    }

    private bool TryReplayTileBrushGeometry(MediaBrush brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (!WpfDrawingReplay.IsSourceBrush(brush))
        {
            return false;
        }

        if (WpfDrawingReplay.TryReplaySourceBrushFill(
                brush,
                geometry,
                _sink,
                _imageSourceAdapter,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            if (pen != null)
            {
                DrawGeometryPenAfterTileBrush(pen, geometry);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return true;
        }

        return false;
    }

    private void DrawRectanglePenAfterTileBrush(MediaPen pen, Rect rectangle)
    {
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeRectangle(null, pen, ToReplayRect(rectangle));
            return;
        }

        _sink.DrawRectangle(null, pen, rectangle);
    }

    private void DrawGeometryPenAfterTileBrush(MediaPen pen, MediaGeometry geometry)
    {
        if (_sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.DrawNativeGeometry(null, pen, geometry))
        {
            return;
        }

        _sink.DrawGeometry(null, pen, geometry);
    }

    private void DrawEllipsePenAfterTileBrush(MediaPen pen, Point center, double radiusX, double radiusY)
    {
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeEllipse(null, pen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
            return;
        }

        _sink.DrawEllipse(null, pen, center, radiusX, radiusY);
    }

    private bool TryDrawPrimitiveLineGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (pen == null ||
            !TryGetPrimitiveLineGeometry(geometry, out var startPoint, out var endPoint))
        {
            return false;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeLine(
                pen,
                new WpfReplayPoint(startPoint.X, startPoint.Y),
                new WpfReplayPoint(endPoint.X, endPoint.Y));
        }
        else
        {
            _sink.DrawLine(pen, startPoint, endPoint);
        }

        CountApplied();
        return true;
    }

    private bool TryDrawPrimitiveRectangleGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (!TryGetPrimitiveRectangleGeometry(geometry, out var rectangle, out var radiusX, out var radiusY))
        {
            if (brush != null
                || pen == null
                || !TryGetPrimitiveRectangleStrokeGeometry(geometry, out rectangle))
            {
                return false;
            }

            radiusX = 0;
            radiusY = 0;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            var replayRectangle = ToReplayRect(rectangle);
            if (radiusX > 0 || radiusY > 0)
            {
                nativeSink.DrawNativeRoundedRectangle(brush, pen, replayRectangle, radiusX, radiusY);
            }
            else
            {
                nativeSink.DrawNativeRectangle(brush, pen, replayRectangle);
            }
        }
        else if (radiusX > 0 || radiusY > 0)
        {
            _sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }
        else
        {
            _sink.DrawRectangle(brush, pen, rectangle);
        }

        CountApplied();
        return true;
    }

    private bool TryDrawPrimitivePolylineGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (brush != null
            || pen == null
            || !WpfMediaLineGeometryReader.TryGetPolylineSegments(geometry, out var segments))
        {
            return false;
        }

        RegisterRetainedDependencies(pen, geometry);
        DrawPolylineSegments(pen, segments);
        CountApplied();
        return true;
    }

    private bool TryDrawPrimitiveEllipseGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (!TryGetPrimitiveEllipseGeometry(geometry, out var center, out var radiusX, out var radiusY))
        {
            return false;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeEllipse(brush, pen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
        }
        else
        {
            _sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
        }

        CountApplied();
        return true;
    }

    private bool TryDrawNativePortableGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
        {
            return false;
        }

        if (_sink is not IWpfNativeGeometryCommandSink nativeGeometrySink)
        {
            return false;
        }

        if (TryGetPortableGeometryPath(geometry, out var portableGeometry)
            && nativeGeometrySink.DrawNativeGeometry(brush, pen, portableGeometry))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            CountApplied();
            return true;
        }

        if (!nativeGeometrySink.DrawNativeGeometry(brush, pen, geometry))
        {
            return false;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        CountApplied();
        return true;
    }

    private void DrawPolylineSegments(MediaPen pen, IReadOnlyList<WpfReplayLineSegment> segments)
    {
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                nativeSink.DrawNativeLine(pen, segment.StartPoint, segment.EndPoint);
            }

            return;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            _sink.DrawLine(
                pen,
                new Point(segment.StartPoint.X, segment.StartPoint.Y),
                new Point(segment.EndPoint.X, segment.EndPoint.Y));
        }
    }

    private static bool TryGetPrimitiveLineGeometry(
        MediaGeometry geometry,
        out Point startPoint,
        out Point endPoint)
    {
        return WpfMediaLineGeometryReader.TryGetLinePoints(geometry, out startPoint, out endPoint);
    }

    private static bool TryGetPrimitiveRectangleGeometry(
        MediaGeometry geometry,
        out Rect rectangle,
        out double radiusX,
        out double radiusY)
    {
        if (geometry is MediaRectangleGeometry rectangleGeometry
            && HasIdentityGeometryTransform(rectangleGeometry)
            && IsUsableRect(rectangleGeometry.Rect, out rectangle)
            && IsUsableRadius(rectangleGeometry.RadiusX, out radiusX)
            && IsUsableRadius(rectangleGeometry.RadiusY, out radiusY))
        {
            return true;
        }

        if (WpfMediaRectangleClipReader.TryGetRectangleClipBounds(geometry, out var rectangleBounds))
        {
            rectangle = ToRect(rectangleBounds);
            radiusX = 0;
            radiusY = 0;
            return true;
        }

        rectangle = default;
        radiusX = default;
        radiusY = default;
        return false;
    }

    private static bool TryGetPrimitiveRectangleStrokeGeometry(
        MediaGeometry geometry,
        out Rect rectangle)
    {
        if (WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(geometry, out var rectangleBounds))
        {
            rectangle = ToRect(rectangleBounds);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryGetPrimitiveEllipseGeometry(
        MediaGeometry geometry,
        out Point center,
        out double radiusX,
        out double radiusY)
    {
        return WpfMediaEllipseGeometryReader.TryGetEllipseGeometry(geometry, out center, out radiusX, out radiusY);
    }

    private static bool TryGetPortableGeometryPath(MediaGeometry geometry, out PortableGeometryPath portableGeometry)
    {
        portableGeometry = null!;
        return geometry is PortableGeometryPathSource portableGeometrySource
            && portableGeometrySource.TryGetPortableGeometryPath(out portableGeometry)
            && portableGeometry != null;
    }

    private static bool HasIdentityGeometryTransform(MediaGeometry geometry)
    {
        var transform = geometry.Transform;
        return transform == null
            || (WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
                && WpfResourceResolver.IsIdentityMatrix(matrix));
    }

    private static bool IsUsableRect(Rect rect, out Rect rectangle)
    {
        rectangle = rect;
        return !rect.IsEmpty
            && double.IsFinite(rect.X)
            && double.IsFinite(rect.Y)
            && double.IsFinite(rect.Width)
            && double.IsFinite(rect.Height)
            && rect.Width > 0
            && rect.Height > 0;
    }

    private static bool IsUsableRadius(double radius, out double usableRadius)
    {
        usableRadius = radius;
        return double.IsFinite(radius) && radius >= 0;
    }

    private static WpfReplayRect ToReplayRect(Rect rectangle)
    {
        return new WpfReplayRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private static Rect ToRect(WpfReplayRect rectangle)
    {
        return new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    private void CountUnsupportedStateIfAny(object? state)
    {
        if (state != null)
        {
            _unsupportedCount++;
        }
    }

    private void CountUnsupportedStateIfAny(object? first, object? second)
    {
        CountUnsupportedStateIfAny(first);
        CountUnsupportedStateIfAny(second);
    }

    private void CountUnsupportedStateIfAny(object? first, object? second, object? third)
    {
        CountUnsupportedStateIfAny(first);
        CountUnsupportedStateIfAny(second);
        CountUnsupportedStateIfAny(third);
    }

    private void RegisterRetainedDependencies(object? dependency)
    {
        WpfRetainedVisualDependencyRegistrar.Register(_sink, dependency);
    }

    private void RegisterRetainedDependencies(object? first, object? second)
    {
        RegisterRetainedDependencies(first);
        RegisterRetainedDependencies(second);
    }

    private void RegisterRetainedDependencies(object? first, object? second, object? third)
    {
        RegisterRetainedDependencies(first);
        RegisterRetainedDependencies(second);
        RegisterRetainedDependencies(third);
    }

}

public readonly record struct WpfCompositionDrawingContextResult(
    int OperationCount,
    int AppliedCount,
    int UnsupportedCount);
