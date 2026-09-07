using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPortableRenderDataSink = System.Windows.Media.IPortableRenderDataDrawingContextSink;
using MediaPen = System.Windows.Media.Pen;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using MediaTransform = System.Windows.Media.Transform;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableDrawingImageSource = ProGPU.Wpf.Interop.IPortableDrawingImageSource;
using PortableNativeDrawingContextSource = ProGPU.Wpf.Interop.IPortableNativeDrawingContextSource;
using PortableNativeDrawingContextState = ProGPU.Wpf.Interop.PortableNativeDrawingContextState;
using PortableNativeDrawingContextStateSource = ProGPU.Wpf.Interop.IPortableNativeDrawingContextStateSource;
using PortableMediaPlayerSource = ProGPU.Wpf.Interop.IPortableMediaPlayerSource;
using PortableRectAnimationValueSource = ProGPU.Wpf.Interop.IPortableRectAnimationValueSource;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfObjectRenderDataDrawingContext :
    MediaPortableRenderDataSink,
    PortableNativeDrawingContextSource,
    PortableNativeDrawingContextStateSource,
    IDisposable
{
    private readonly IWpfCompositionCommandSink _sink;
    private readonly WpfResourceResolver _resources;
    private readonly IWpfImageSourceAdapter? _imageSourceAdapter;
    private int _stackDepth;
    private int _operationCount;
    private int _appliedCount;
    private int _unsupportedCount;
    private bool _isClosed;

    public WpfObjectRenderDataDrawingContext(
        IWpfCompositionCommandSink sink,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _imageSourceAdapter = imageSourceAdapter;
        _resources = new WpfResourceResolver(imageSourceAdapter);
    }

    public int StackDepth => _stackDepth;

    public WpfCompositionDrawingContextResult Result => new(
        _operationCount,
        _appliedCount,
        _unsupportedCount);

    bool PortableNativeDrawingContextSource.TryGetPortableNativeDrawingContext(out object? nativeDrawingContext)
    {
        if (((PortableNativeDrawingContextStateSource)this).TryGetPortableNativeDrawingContextState(out var state))
        {
            nativeDrawingContext = state.NativeDrawingContext;
            return true;
        }

        nativeDrawingContext = null;
        return false;
    }

    bool PortableNativeDrawingContextStateSource.TryGetPortableNativeDrawingContextState(
        out PortableNativeDrawingContextState state)
    {
        if (_sink is IWpfProGpuSceneDrawingContextSource nativeDrawingContextSource
            && nativeDrawingContextSource.TryGetProGpuSceneDrawingContextState(
                out var sceneDrawingContext,
                out var transform)
            && sceneDrawingContext != null)
        {
            state = new PortableNativeDrawingContextState(sceneDrawingContext, transform);
            return true;
        }

        state = default;
        return false;
    }

    public void DrawLine(object? pen, object? point0, object? point1)
    {
        ThrowIfClosed();
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (mediaPen == null)
        {
            CountUnsupportedIfPresent(pen);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeLine(pen, point0, point1, mediaPen, nativeSink);
            return;
        }

        DrawLineTypedFallback(pen, point0, point1, mediaPen);
    }

    private void DrawNativeLine(
        object? pen,
        object? point0,
        object? point1,
        MediaPen mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayPoint(point0, out var replayPoint0) || !TryReadReplayPoint(point1, out var replayPoint1))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(pen);
        nativeSink.DrawNativeLine(mediaPen, replayPoint0, replayPoint1);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawLineTypedFallback(object? pen, object? point0, object? point1, MediaPen mediaPen)
    {
        if (!TryReadPoint(point0, out var mediaPoint0) || !TryReadPoint(point1, out var mediaPoint1))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(pen);
        _sink.DrawLine(mediaPen, mediaPoint0, mediaPoint1);
        CountApplied();
    }

    public void DrawLine(object? pen, object? point0, object? point0Animations, object? point1, object? point1Animations)
    {
        DrawLine(pen, point0, point1);
        CountUnsupportedStateIfAny(point0Animations, point1Animations);
    }

    public void DrawRectangle(object? brush, object? pen, object? rectangle)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (TryReplayTileBrushRectangle(brush, pen, rectangle, mediaPen))
        {
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeRectangle(brush, pen, rectangle, mediaBrush, mediaPen, nativeSink);
            return;
        }

        DrawRectangleTypedFallback(brush, pen, rectangle, mediaBrush, mediaPen);
    }

    private void DrawNativeRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayRect(rectangle, out var replayRectangle))
        {
            CountUnsupported();
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen);
            nativeSink.DrawNativeRectangle(mediaBrush, mediaPen, replayRectangle);
            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen);
            nativeSink.DrawNativeRectangle(null, mediaPen, replayRectangle);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return;
        }

        CountUnsupportedIfPresent(brush, pen);
    }

    private bool TryReplayTileBrushRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        MediaPen? mediaPen)
    {
        if (brush == null
            || !WpfDrawingReplay.IsSourceBrush(brush)
            || !TryReadRect(rectangle, out var mediaRectangle)
            || !WpfDrawingReplay.TryReplaySourceBrushFill(
                brush,
                mediaRectangle,
                _sink,
                _resources.AdaptImageSource,
                out var brushReplayStatus))
        {
            return false;
        }

        RegisterRetainedDependencies(brush, pen);
        if (mediaPen != null)
        {
            DrawRectanglePenAfterTileBrush(mediaPen, mediaRectangle);
        }

        CountDrawingReplayStatus(brushReplayStatus);
        return true;
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawRectangleTypedFallback(
        object? brush,
        object? pen,
        object? rectangle,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (!TryReadRect(rectangle, out var mediaRectangle))
        {
            CountUnsupported();
            return;
        }

        if (brush != null
            && WpfDrawingReplay.IsTileBrush(brush)
            && WpfDrawingReplay.TryReplayTileBrushFill(
                brush,
                mediaRectangle,
                _sink,
                _resources.AdaptImageSource,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen);
            if (mediaPen != null)
            {
                DrawRectanglePenAfterTileBrush(mediaPen, mediaRectangle);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen);
            _sink.DrawRectangle(mediaBrush, mediaPen, mediaRectangle);
            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen);
            _sink.DrawRectangle(null, mediaPen, mediaRectangle);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return;
        }

        CountUnsupportedIfPresent(brush, pen);
    }

    public void DrawRectangle(object? brush, object? pen, object? rectangle, object? rectangleAnimations)
    {
        DrawRectangle(brush, pen, rectangle);
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawRoundedRectangle(object? brush, object? pen, object? rectangle, object? radiusX, object? radiusY)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (mediaBrush == null && mediaPen == null)
        {
            CountUnsupportedIfPresent(brush, pen);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeRoundedRectangle(brush, pen, rectangle, radiusX, radiusY, mediaBrush, mediaPen, nativeSink);
            return;
        }

        DrawRoundedRectangleTypedFallback(brush, pen, rectangle, radiusX, radiusY, mediaBrush, mediaPen);
    }

    private void DrawNativeRoundedRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayRect(rectangle, out var replayRectangle)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        nativeSink.DrawNativeRoundedRectangle(mediaBrush, mediaPen, replayRectangle, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawRoundedRectangleTypedFallback(
        object? brush,
        object? pen,
        object? rectangle,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (!TryReadRect(rectangle, out var mediaRectangle)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawRoundedRectangle(mediaBrush, mediaPen, mediaRectangle, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    public void DrawRoundedRectangle(
        object? brush,
        object? pen,
        object? rectangle,
        object? rectangleAnimations,
        object? radiusX,
        object? radiusXAnimations,
        object? radiusY,
        object? radiusYAnimations)
    {
        DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        CountUnsupportedStateIfAny(rectangleAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawEllipse(object? brush, object? pen, object? center, object? radiusX, object? radiusY)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (brush != null
            && WpfDrawingReplay.IsTileBrush(brush)
            && TryReadPoint(center, out var tileCenter)
            && TryReadDouble(radiusX, out var tileRadiusX)
            && TryReadDouble(radiusY, out var tileRadiusY)
            && WpfDrawingReplay.TryReplayTileBrushEllipseFill(
                brush,
                tileCenter,
                tileRadiusX,
                tileRadiusY,
                _sink,
                _resources.AdaptImageSource,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen);
            if (mediaPen != null)
            {
                DrawEllipsePenAfterTileBrush(mediaPen, tileCenter, tileRadiusX, tileRadiusY);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return;
        }

        if (mediaBrush == null && mediaPen == null)
        {
            CountUnsupportedIfPresent(brush, pen);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeEllipse(brush, pen, center, radiusX, radiusY, mediaBrush, mediaPen, nativeSink);
            return;
        }

        DrawEllipseTypedFallback(brush, pen, center, radiusX, radiusY, mediaBrush, mediaPen);
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

    private void DrawNativeEllipse(
        object? brush,
        object? pen,
        object? center,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayPoint(center, out var replayCenter)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        nativeSink.DrawNativeEllipse(mediaBrush, mediaPen, replayCenter, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawEllipseTypedFallback(
        object? brush,
        object? pen,
        object? center,
        object? radiusX,
        object? radiusY,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (!TryReadPoint(center, out var mediaCenter)
            || !TryReadDouble(radiusX, out var mediaRadiusX)
            || !TryReadDouble(radiusY, out var mediaRadiusY))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(brush, pen);
        _sink.DrawEllipse(mediaBrush, mediaPen, mediaCenter, mediaRadiusX, mediaRadiusY);
        CountApplied();
    }

    public void DrawEllipse(
        object? brush,
        object? pen,
        object? center,
        object? centerAnimations,
        object? radiusX,
        object? radiusXAnimations,
        object? radiusY,
        object? radiusYAnimations)
    {
        DrawEllipse(brush, pen, center, radiusX, radiusY);
        CountUnsupportedStateIfAny(centerAnimations, radiusXAnimations, radiusYAnimations);
    }

    public void DrawGeometry(object? brush, object? pen, object? geometry)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        MediaPen? mediaPen = WpfResourceResolver.AdaptPen(pen);
        if (brush != null
            && WpfDrawingReplay.IsSourceBrush(brush)
            && WpfDrawingReplay.TryReplaySourceBrushFill(
                brush,
                geometry,
                _sink,
                _resources.AdaptImageSource,
                out var portableBrushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            var replayStatus = portableBrushReplayStatus;
            if (mediaPen != null
                && !TryDrawNativePortableGeometryPen(geometry, mediaPen)
                && !TryDrawPrimitiveLineGeometryPen(geometry, mediaPen)
                && !TryDrawPrimitivePolylineGeometryPen(geometry, mediaPen)
                && !TryDrawPrimitiveRectangleGeometryPen(geometry, mediaPen)
                && !TryDrawPrimitiveEllipseGeometryPen(geometry, mediaPen))
            {
                if (WpfResourceResolver.AdaptGeometry(geometry) is { } penGeometry)
                {
                    DrawMediaGeometry(null, mediaPen, penGeometry);
                }
                else
                {
                    replayStatus = WpfDrawingReplayStatus.PartiallyApplied;
                }
            }

            CountDrawingReplayStatus(replayStatus);
            return;
        }

        if (TryDrawPrimitiveLineGeometry(brush, pen, geometry, mediaPen))
        {
            return;
        }

        if (TryDrawPrimitivePolylineGeometry(brush, pen, geometry, mediaPen))
        {
            return;
        }

        if (TryDrawPrimitiveRectangleGeometry(brush, pen, geometry, mediaBrush, mediaPen))
        {
            return;
        }

        if (TryDrawPrimitiveEllipseGeometry(brush, pen, geometry, mediaBrush, mediaPen))
        {
            return;
        }

        if (TryDrawNativePortableGeometry(brush, pen, geometry, mediaBrush, mediaPen))
        {
            return;
        }

        MediaGeometry? mediaGeometry = WpfResourceResolver.AdaptGeometry(geometry);
        if (mediaGeometry == null)
        {
            CountUnsupportedIfPresent(brush, pen, geometry);
            return;
        }

        if (brush != null
            && WpfDrawingReplay.IsTileBrush(brush)
            && WpfDrawingReplay.TryReplayTileBrushFill(
                brush,
                mediaGeometry,
                _sink,
                _resources.AdaptImageSource,
                out var brushReplayStatus))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            if (mediaPen != null)
            {
                DrawMediaGeometry(null, mediaPen, mediaGeometry);
            }

            CountDrawingReplayStatus(brushReplayStatus);
            return;
        }

        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            DrawMediaGeometry(mediaBrush, mediaPen, mediaGeometry);
            CountApplied();
            return;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            DrawMediaGeometry(null, mediaPen, mediaGeometry);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return;
        }

        CountUnsupportedIfPresent(brush, pen, geometry);
    }

    private bool TryDrawNativePortableGeometryPen(object? geometry, MediaPen pen)
    {
        return _sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && ((TryGetPortableGeometryPath(geometry, out var portableGeometry)
                    && nativeGeometrySink.DrawNativeGeometry(null, pen, portableGeometry))
                || (geometry is MediaGeometry mediaGeometry
                    && nativeGeometrySink.DrawNativeGeometry(null, pen, mediaGeometry)));
    }

    private void DrawMediaGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        if (_sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.DrawNativeGeometry(brush, pen, geometry))
        {
            return;
        }

        _sink.DrawGeometry(brush, pen, geometry);
    }

    private bool TryDrawPrimitiveLineGeometryPen(object? geometry, MediaPen pen)
    {
        if (!TryReadLineGeometry(geometry, out var startPoint, out var endPoint))
        {
            return false;
        }

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

        return true;
    }

    private bool TryDrawPrimitivePolylineGeometryPen(object? geometry, MediaPen pen)
    {
        if (!TryReadPolylineGeometry(geometry, out var segments))
        {
            return false;
        }

        DrawPolylineSegments(pen, segments);
        return true;
    }

    private bool TryDrawPrimitiveLineGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaPen? mediaPen)
    {
        if (mediaPen == null ||
            !TryReadLineGeometry(geometry, out var startPoint, out var endPoint))
        {
            return false;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeLine(
                mediaPen,
                new WpfReplayPoint(startPoint.X, startPoint.Y),
                new WpfReplayPoint(endPoint.X, endPoint.Y));
        }
        else
        {
            _sink.DrawLine(mediaPen, startPoint, endPoint);
        }

        if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
        {
            CountPartiallyApplied();
        }
        else
        {
            CountApplied();
        }

        return true;
    }

    private bool TryDrawPrimitivePolylineGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaPen? mediaPen)
    {
        if (brush != null
            || mediaPen == null
            || !TryReadPolylineGeometry(geometry, out var segments))
        {
            return false;
        }

        RegisterRetainedDependencies(pen, geometry);
        DrawPolylineSegments(mediaPen, segments);
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

    private bool TryDrawPrimitiveRectangleGeometryPen(object? geometry, MediaPen pen)
    {
        if (!TryReadRectangleGeometry(geometry, out var rectangle, out var radiusX, out var radiusY))
        {
            if (!TryReadRectangleStrokeGeometry(geometry, out rectangle))
            {
                return false;
            }

            radiusX = 0;
            radiusY = 0;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativePrimitiveRectangle(nativeSink, null, pen, ToReplayRect(rectangle), radiusX, radiusY);
        }
        else
        {
            DrawPrimitiveRectangle(null, pen, rectangle, radiusX, radiusY);
        }

        return true;
    }

    private bool TryDrawPrimitiveEllipseGeometryPen(object? geometry, MediaPen pen)
    {
        if (!TryReadEllipseGeometry(geometry, out var center, out var radiusX, out var radiusY))
        {
            return false;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.DrawNativeEllipse(null, pen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
        }
        else
        {
            _sink.DrawEllipse(null, pen, center, radiusX, radiusY);
        }

        return true;
    }

    private bool TryDrawPrimitiveRectangleGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (!TryReadRectangleGeometry(geometry, out var rectangle, out var radiusX, out var radiusY))
        {
            if (brush != null
                || mediaPen == null
                || !TryReadRectangleStrokeGeometry(geometry, out rectangle))
            {
                return false;
            }

            radiusX = 0;
            radiusY = 0;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            return TryDrawNativeRectangleGeometry(brush, pen, geometry, mediaBrush, mediaPen, ToReplayRect(rectangle), radiusX, radiusY, nativeSink);
        }

        return TryDrawRectangleGeometryFallback(brush, pen, geometry, mediaBrush, mediaPen, rectangle, radiusX, radiusY);
    }

    private bool TryDrawNativeRectangleGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        WpfReplayRect rectangle,
        double radiusX,
        double radiusY,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            DrawNativePrimitiveRectangle(nativeSink, mediaBrush, mediaPen, rectangle, radiusX, radiusY);
            CountApplied();
            return true;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            DrawNativePrimitiveRectangle(nativeSink, null, mediaPen, rectangle, radiusX, radiusY);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return true;
        }

        return false;
    }

    private bool TryDrawRectangleGeometryFallback(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        Rect rectangle,
        double radiusX,
        double radiusY)
    {
        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            DrawPrimitiveRectangle(mediaBrush, mediaPen, rectangle, radiusX, radiusY);
            CountApplied();
            return true;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            DrawPrimitiveRectangle(null, mediaPen, rectangle, radiusX, radiusY);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return true;
        }

        return false;
    }

    private static void DrawNativePrimitiveRectangle(
        IWpfNativePrimitiveCommandSink nativeSink,
        MediaBrush? brush,
        MediaPen? pen,
        WpfReplayRect rectangle,
        double radiusX,
        double radiusY)
    {
        if (radiusX > 0 || radiusY > 0)
        {
            nativeSink.DrawNativeRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }
        else
        {
            nativeSink.DrawNativeRectangle(brush, pen, rectangle);
        }
    }

    private void DrawPrimitiveRectangle(
        MediaBrush? brush,
        MediaPen? pen,
        Rect rectangle,
        double radiusX,
        double radiusY)
    {
        if (radiusX > 0 || radiusY > 0)
        {
            _sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }
        else
        {
            _sink.DrawRectangle(brush, pen, rectangle);
        }
    }

    private bool TryDrawPrimitiveEllipseGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (!TryReadEllipseGeometry(geometry, out var center, out var radiusX, out var radiusY))
        {
            return false;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            return TryDrawNativeEllipseGeometry(brush, pen, geometry, mediaBrush, mediaPen, center, radiusX, radiusY, nativeSink);
        }

        return TryDrawEllipseGeometryFallback(brush, pen, geometry, mediaBrush, mediaPen, center, radiusX, radiusY);
    }

    private bool TryDrawNativeEllipseGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        Point center,
        double radiusX,
        double radiusY,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            nativeSink.DrawNativeEllipse(mediaBrush, mediaPen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
            CountApplied();
            return true;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            nativeSink.DrawNativeEllipse(null, mediaPen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return true;
        }

        return false;
    }

    private bool TryDrawEllipseGeometryFallback(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen,
        Point center,
        double radiusX,
        double radiusY)
    {
        if (mediaBrush != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            _sink.DrawEllipse(mediaBrush, mediaPen, center, radiusX, radiusY);
            CountApplied();
            return true;
        }

        if (mediaPen != null)
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            _sink.DrawEllipse(null, mediaPen, center, radiusX, radiusY);
            if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
            {
                CountPartiallyApplied();
            }
            else
            {
                CountApplied();
            }

            return true;
        }

        return false;
    }

    private bool TryDrawNativePortableGeometry(
        object? brush,
        object? pen,
        object? geometry,
        MediaBrush? mediaBrush,
        MediaPen? mediaPen)
    {
        if (brush != null && WpfDrawingReplay.IsTileBrush(brush))
        {
            return false;
        }

        if (mediaBrush == null && mediaPen == null)
        {
            return false;
        }

        if (_sink is not IWpfNativeGeometryCommandSink nativeGeometrySink)
        {
            return false;
        }

        if (TryGetPortableGeometryPath(geometry, out var portableGeometry)
            && nativeGeometrySink.DrawNativeGeometry(mediaBrush, mediaPen, portableGeometry))
        {
            RegisterRetainedDependencies(brush, pen, geometry);
            CountApplied();
            return true;
        }

        if (geometry is not MediaGeometry mediaGeometry
            || !nativeGeometrySink.DrawNativeGeometry(mediaBrush, mediaPen, mediaGeometry))
        {
            return false;
        }

        RegisterRetainedDependencies(brush, pen, geometry);
        CountApplied();
        return true;
    }

    public void DrawImage(object? imageSource, object? rectangle)
    {
        ThrowIfClosed();
        if (imageSource is PortableDrawingImageSource)
        {
            if (!TryReadRect(rectangle, out var drawingImageRectangle))
            {
                CountUnsupported();
                return;
            }

            WpfDrawingReplay.TryReplayDrawingImage(
                imageSource,
                drawingImageRectangle,
                _sink,
                _resources.AdaptImageSource,
                out var drawingImageStatus);
            RegisterRetainedDependencies(imageSource);
            CountDrawingReplayStatus(drawingImageStatus);
            return;
        }

        MediaImageSource? mediaImageSource = _resources.AdaptImageSource(imageSource);
        if (mediaImageSource == null)
        {
            CountUnsupportedIfPresent(imageSource);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeImage(imageSource, rectangle, mediaImageSource, nativeSink);
            return;
        }

        DrawImageTypedFallback(imageSource, rectangle, mediaImageSource);
    }

    private void DrawNativeImage(
        object? imageSource,
        object? rectangle,
        MediaImageSource mediaImageSource,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!TryReadReplayRect(rectangle, out var replayRectangle))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(imageSource);
        nativeSink.DrawNativeImage(mediaImageSource, replayRectangle);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawImageTypedFallback(object? imageSource, object? rectangle, MediaImageSource mediaImageSource)
    {
        if (!TryReadRect(rectangle, out var mediaRectangle))
        {
            CountUnsupported();
            return;
        }

        RegisterRetainedDependencies(imageSource);
        _sink.DrawImage(mediaImageSource, mediaRectangle);
        CountApplied();
    }

    public void DrawImage(object? imageSource, object? rectangle, object? rectangleAnimations)
    {
        DrawImage(imageSource, rectangle);
        CountUnsupportedStateIfAny(rectangleAnimations);
    }

    public void DrawGlyphRun(object? foregroundBrush, object? glyphRun)
    {
        ThrowIfClosed();
        MediaBrush? mediaBrush = WpfResourceResolver.AdaptBrush(foregroundBrush);
        if (mediaBrush == null || glyphRun == null)
        {
            CountUnsupportedIfPresent(foregroundBrush, glyphRun);
            return;
        }

        if (_sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            DrawNativeGlyphRun(foregroundBrush, glyphRun, mediaBrush, nativeSink);
            return;
        }

        DrawGlyphRunTypedFallback(foregroundBrush, glyphRun, mediaBrush);
    }

    private void DrawNativeGlyphRun(
        object? foregroundBrush,
        object glyphRun,
        MediaBrush mediaBrush,
        IWpfNativePrimitiveCommandSink nativeSink)
    {
        if (!WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var nativeGlyphRun))
        {
            CountUnsupportedIfPresent(foregroundBrush, glyphRun);
            return;
        }

        RegisterRetainedDependencies(foregroundBrush, glyphRun);
        nativeSink.DrawNativeGlyphRun(mediaBrush, nativeGlyphRun);
        CountApplied();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DrawGlyphRunTypedFallback(object? foregroundBrush, object glyphRun, MediaBrush mediaBrush)
    {
        MediaGlyphRun? mediaGlyphRun = WpfResourceResolver.AdaptGlyphRun(glyphRun);
        if (mediaGlyphRun == null)
        {
            CountUnsupportedIfPresent(foregroundBrush, glyphRun);
            return;
        }

        RegisterRetainedDependencies(foregroundBrush, glyphRun);
        _sink.DrawGlyphRun(mediaBrush, mediaGlyphRun);
        CountApplied();
    }

    public void DrawDrawing(object? drawing)
    {
        ThrowIfClosed();
        var status = WpfDrawingReplay.Replay(drawing, _sink, _resources.AdaptImageSource);
        if (status is WpfDrawingReplayStatus.Applied or WpfDrawingReplayStatus.PartiallyApplied)
        {
            RegisterRetainedDependencies(drawing);
        }

        CountDrawingReplayStatus(status);
    }

    public void DrawVideo(object? player, object? rectangle)
    {
        ThrowIfClosed();
        if (player == null)
        {
            return;
        }
        if (!TryReadReplayRect(rectangle, out var replayRectangle))
        {
            CountUnsupported();
            return;
        }

        DrawPortableVideo(player, replayRectangle);
    }

    public void DrawVideo(object? player, object? rectangle, object? rectangleAnimations)
    {
        ThrowIfClosed();
        if (player == null)
        {
            return;
        }
        if (!TryReadReplayRect(rectangle, out var replayRectangle))
        {
            CountUnsupported();
            return;
        }
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

    public void PushClip(object? clipGeometry)
    {
        ThrowIfClosed();
        if (clipGeometry == null)
        {
            _sink.PushNoOpScope();
        }
        else if (!TryPushPrimitiveRectangleClip(clipGeometry)
            && !TryPushNativePortableClip(clipGeometry))
        {
            if (WpfResourceResolver.AdaptGeometry(clipGeometry) is { } mediaGeometry)
            {
                RegisterRetainedDependencies(clipGeometry);
                if (!TryPushNativeMediaGeometryClip(mediaGeometry))
                {
                    _sink.PushClip(mediaGeometry);
                }
            }
            else
            {
                _sink.PushNoOpScope();
                CountUnsupported();
            }
        }

        _stackDepth++;
        CountApplied();
    }

    private bool TryPushNativePortableClip(object? clipGeometry)
    {
        if (!TryGetPortableGeometryPath(clipGeometry, out var portableGeometry))
        {
            return false;
        }

        if (_sink is IWpfNativeClipCommandSink nativeClipSink
            && WpfPortableRectangleClipReader.TryGetRectangleClipBounds(portableGeometry, out var clipBounds))
        {
            RegisterRetainedDependencies(clipGeometry);
            nativeClipSink.PushNativeClip(clipBounds);
            return true;
        }

        if (_sink is not IWpfNativeGeometryCommandSink nativeGeometrySink
            || !nativeGeometrySink.PushNativeGeometryClip(portableGeometry))
        {
            return false;
        }

        RegisterRetainedDependencies(clipGeometry);
        return true;
    }

    private bool TryPushNativeMediaGeometryClip(MediaGeometry clipGeometry)
    {
        return _sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.PushNativeGeometryClip(clipGeometry);
    }

    private bool TryPushPrimitiveRectangleClip(object? clipGeometry)
    {
        if (_sink is not IWpfNativeClipCommandSink nativeClipSink
            || !TryReadRectangleGeometry(clipGeometry, out var rectangle, out var radiusX, out var radiusY)
            || radiusX != 0
            || radiusY != 0
            || !IsUsableRect(rectangle, out rectangle))
        {
            return false;
        }

        RegisterRetainedDependencies(clipGeometry);
        nativeClipSink.PushNativeClip(ToReplayRect(rectangle));
        return true;
    }

    public void PushOpacityMask(object? opacityMask)
    {
        ThrowIfClosed();
        if (opacityMask == null)
        {
            _sink.PushNoOpScope();
        }
        else if (WpfResourceResolver.AdaptBrush(opacityMask) is { } mediaOpacityMask)
        {
            RegisterRetainedDependencies(opacityMask);
            _sink.PushOpacityMask(mediaOpacityMask, Rect.Empty);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(object? opacity)
    {
        ThrowIfClosed();
        if (TryReadDouble(opacity, out var mediaOpacity))
        {
            _sink.PushOpacity(mediaOpacity);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushOpacity(object? opacity, object? opacityAnimations)
    {
        PushOpacity(opacity);
        CountUnsupportedStateIfAny(opacityAnimations);
    }

    public void PushTransform(object? transform)
    {
        ThrowIfClosed();
        if (transform == null)
        {
            _sink.PushNoOpScope();
        }
        else if (_sink is IWpfNativeTransformCommandSink nativeTransformSink
            && WpfResourceResolver.TryAdaptTransformMatrix(transform, out var nativeTransform))
        {
            RegisterRetainedDependencies(transform);
            nativeTransformSink.PushNativeTransform(nativeTransform);
        }
        else if (WpfResourceResolver.AdaptTransform(transform) is { } mediaTransform)
        {
            RegisterRetainedDependencies(transform);
            _sink.PushTransform(mediaTransform);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
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

    public void PushGuidelineY1(object? coordinate)
    {
        ThrowIfClosed();
        if (TryReadDouble(coordinate, out var mediaCoordinate))
        {
            _sink.PushGuidelineY1(mediaCoordinate);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushGuidelineY2(object? leadingCoordinate, object? offsetToDrivenCoordinate)
    {
        ThrowIfClosed();
        if (TryReadDouble(leadingCoordinate, out var mediaLeadingCoordinate)
            && TryReadDouble(offsetToDrivenCoordinate, out var mediaOffsetToDrivenCoordinate))
        {
            _sink.PushGuidelineY2(mediaLeadingCoordinate, mediaOffsetToDrivenCoordinate);
        }
        else
        {
            _sink.PushNoOpScope();
            CountUnsupported();
        }

        _stackDepth++;
        CountApplied();
    }

    public void PushEffect(object? effect, object? effectInput)
    {
        ThrowIfClosed();

        if (WpfEffectMapper.TryCreateProGpuPushEffect(effect, effectInput, out var proGpuEffect, _imageSourceAdapter)
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

    private void CountUnsupportedIfPresent(object? state)
    {
        if (state != null)
        {
            CountUnsupported();
        }
    }

    private void CountUnsupportedIfPresent(object? first, object? second)
    {
        if (first != null || second != null)
        {
            CountUnsupported();
        }
    }

    private void CountUnsupportedIfPresent(object? first, object? second, object? third)
    {
        if (first != null || second != null || third != null)
        {
            CountUnsupported();
        }
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

    private static bool TryReadPoint(object? pointValue, out Point point)
    {
        if (pointValue is Point mediaPoint)
        {
            point = mediaPoint;
            return true;
        }

        if (pointValue is PortablePoint portablePoint)
        {
            point = new Point(portablePoint.X, portablePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadReplayPoint(object? pointValue, out WpfReplayPoint point)
    {
        if (pointValue is WpfReplayPoint replayPoint)
        {
            point = replayPoint;
            return true;
        }

        if (pointValue is Point mediaPoint)
        {
            point = new WpfReplayPoint(mediaPoint.X, mediaPoint.Y);
            return true;
        }

        if (pointValue is PortablePoint portablePoint)
        {
            point = new WpfReplayPoint(portablePoint.X, portablePoint.Y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadRect(object? rectValue, out Rect rectangle)
    {
        if (rectValue is WpfReplayRect replayRect)
        {
            rectangle = new Rect(replayRect.X, replayRect.Y, replayRect.Width, replayRect.Height);
            return true;
        }

        if (rectValue is Rect mediaRect)
        {
            rectangle = mediaRect;
            return true;
        }

        if (rectValue is PortableRect portableRect && !portableRect.IsEmpty)
        {
            rectangle = new Rect(portableRect.X, portableRect.Y, portableRect.Width, portableRect.Height);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryGetPortableGeometryPath(object? geometry, out PortableGeometryPath portableGeometry)
    {
        portableGeometry = null!;
        return geometry is PortableGeometryPathSource portableGeometrySource
            && portableGeometrySource.TryGetPortableGeometryPath(out portableGeometry)
            && portableGeometry != null;
    }

    private static bool TryReadLineGeometry(
        object? geometry,
        out Point startPoint,
        out Point endPoint)
    {
        if (geometry is MediaGeometry mediaGeometry)
        {
            return WpfMediaLineGeometryReader.TryGetLinePoints(mediaGeometry, out startPoint, out endPoint);
        }

        startPoint = default;
        endPoint = default;
        return false;
    }

    private static bool TryReadPolylineGeometry(
        object? geometry,
        out IReadOnlyList<WpfReplayLineSegment> segments)
    {
        if (geometry is MediaGeometry mediaGeometry)
        {
            return WpfMediaLineGeometryReader.TryGetPolylineSegments(mediaGeometry, out segments);
        }

        segments = Array.Empty<WpfReplayLineSegment>();
        return false;
    }

    private static bool TryReadRectangleStrokeGeometry(
        object? geometry,
        out Rect rectangle)
    {
        if (geometry is MediaGeometry mediaGeometry
            && WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(mediaGeometry, out var rectangleBounds))
        {
            rectangle = ToRect(rectangleBounds);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryReadRectangleGeometry(
        object? geometry,
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

        if (geometry is MediaGeometry mediaGeometry
            && WpfMediaRectangleClipReader.TryGetRectangleClipBounds(mediaGeometry, out var rectangleBounds))
        {
            rectangle = ToRect(rectangleBounds);
            radiusX = 0;
            radiusY = 0;
            return true;
        }

        if (TryReadRect(geometry, out rectangle))
        {
            radiusX = 0;
            radiusY = 0;
            return true;
        }

        radiusX = default;
        radiusY = default;
        return false;
    }

    private static bool TryReadEllipseGeometry(
        object? geometry,
        out Point center,
        out double radiusX,
        out double radiusY)
    {
        if (geometry is MediaGeometry mediaGeometry)
        {
            return WpfMediaEllipseGeometryReader.TryGetEllipseGeometry(mediaGeometry, out center, out radiusX, out radiusY);
        }

        center = default;
        radiusX = default;
        radiusY = default;
        return false;
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

    private static bool TryReadReplayRect(object? rectValue, out WpfReplayRect rectangle)
    {
        if (rectValue is WpfReplayRect replayRect)
        {
            rectangle = replayRect;
            return true;
        }

        if (rectValue is Rect mediaRect && !mediaRect.IsEmpty)
        {
            rectangle = new WpfReplayRect(mediaRect.X, mediaRect.Y, mediaRect.Width, mediaRect.Height);
            return true;
        }

        if (rectValue is PortableRect portableRect && !portableRect.IsEmpty)
        {
            rectangle = new WpfReplayRect(portableRect.X, portableRect.Y, portableRect.Width, portableRect.Height);
            return true;
        }

        rectangle = default;
        return false;
    }

    private static bool TryReadDouble(object? value, out double result)
    {
        switch (value)
        {
            case double doubleValue:
                result = doubleValue;
                return true;
            case float floatValue:
                result = floatValue;
                return true;
            case int intValue:
                result = intValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case short shortValue:
                result = shortValue;
                return true;
            case ushort ushortValue:
                result = ushortValue;
                return true;
            case byte byteValue:
                result = byteValue;
                return true;
            case sbyte sbyteValue:
                result = sbyteValue;
                return true;
            case long longValue when longValue >= -9007199254740992L && longValue <= 9007199254740992L:
                result = longValue;
                return true;
            case ulong ulongValue when ulongValue <= 9007199254740992UL:
                result = ulongValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
