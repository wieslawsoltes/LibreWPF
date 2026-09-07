using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using MediaBrush = System.Windows.Media.Brush;
using MediaEllipseGeometry = System.Windows.Media.EllipseGeometry;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaMatrixTransform = System.Windows.Media.MatrixTransform;
using MediaPen = System.Windows.Media.Pen;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;
using MediaTransform = System.Windows.Media.Transform;
using PortableAlignmentX = ProGPU.Wpf.Interop.PortableAlignmentX;
using PortableAlignmentY = ProGPU.Wpf.Interop.PortableAlignmentY;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableDrawingBoundsSource = ProGPU.Wpf.Interop.IPortableDrawingBoundsSource;
using PortableDrawingGroupChildrenSource = ProGPU.Wpf.Interop.IPortableDrawingGroupChildrenSource;
using PortableDrawingImageSource = ProGPU.Wpf.Interop.IPortableDrawingImageSource;
using PortableDrawingGroupState = ProGPU.Wpf.Interop.PortableDrawingGroupState;
using PortableDrawingGroupStateSource = ProGPU.Wpf.Interop.IPortableDrawingGroupStateSource;
using PortableGlyphRunDrawingState = ProGPU.Wpf.Interop.PortableGlyphRunDrawingState;
using PortableGlyphRunDrawingStateSource = ProGPU.Wpf.Interop.IPortableGlyphRunDrawingStateSource;
using PortableGeometryDrawingState = ProGPU.Wpf.Interop.PortableGeometryDrawingState;
using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortableImageDrawingState = ProGPU.Wpf.Interop.PortableImageDrawingState;
using PortableImageDrawingStateSource = ProGPU.Wpf.Interop.IPortableImageDrawingStateSource;
using PortableMatrix3x2 = ProGPU.Wpf.Interop.PortableMatrix3x2;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableStretch = ProGPU.Wpf.Interop.PortableStretch;
using PortableTileBrush = ProGPU.Wpf.Interop.PortableTileBrush;
using PortableTileBrushKind = ProGPU.Wpf.Interop.PortableTileBrushKind;
using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;
using PortableTileMode = ProGPU.Wpf.Interop.PortableTileMode;
using PortableVisualBounds = ProGPU.Wpf.Interop.PortableVisualBounds;
using PortableVisualBoundsSource = ProGPU.Wpf.Interop.IPortableVisualBoundsSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfDrawingReplay
{
    private const int MaxTileBrushReplayTiles = 1024;

    private enum SupportedTileMode
    {
        None,
        Tile,
        FlipX,
        FlipY,
        FlipXY
    }

    private enum SupportedStretch
    {
        None,
        Fill,
        Uniform,
        UniformToFill
    }

    private enum SupportedAlignmentX
    {
        Left,
        Center,
        Right
    }

    private enum SupportedAlignmentY
    {
        Top,
        Center,
        Bottom
    }

    private readonly record struct TileBrushReplayTile(Rect Bounds, int Column, int Row)
    {
        public bool FlipX(SupportedTileMode tileMode)
        {
            return (tileMode == SupportedTileMode.FlipX || tileMode == SupportedTileMode.FlipXY)
                && (Column & 1) != 0;
        }

        public bool FlipY(SupportedTileMode tileMode)
        {
            return (tileMode == SupportedTileMode.FlipY || tileMode == SupportedTileMode.FlipXY)
                && (Row & 1) != 0;
        }
    }

    private readonly record struct TileBrushReplayTiles(
        Rect Viewport,
        int StartColumn,
        int EndColumn,
        int StartRow,
        int EndRow)
    {
        public int Count
        {
            get
            {
                return (EndColumn - StartColumn + 1) * (EndRow - StartRow + 1);
            }
        }

        public TileBrushReplayTile GetAt(int index)
        {
            var columnCount = EndColumn - StartColumn + 1;
            var rowOffset = index / columnCount;
            var column = StartColumn + index - rowOffset * columnCount;
            var row = StartRow + rowOffset;

            return new TileBrushReplayTile(
                new Rect(
                    Viewport.X + column * Viewport.Width,
                    Viewport.Y + row * Viewport.Height,
                    Viewport.Width,
                    Viewport.Height),
                column,
                row);
        }
    }

    private readonly record struct TileBrushFillGeometry(
        object? Source,
        Rect Bounds,
        MediaGeometry? MediaGeometry,
        PortableGeometryPath? PortableGeometry,
        bool IsRectangle,
        WpfReplayPoint EllipseCenter = default,
        double EllipseRadiusX = 0,
        double EllipseRadiusY = 0);

    public static bool TryReplay(
        object? drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter = null)
    {
        var status = Replay(drawing, sink, imageSourceAdapter);
        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    public static WpfDrawingReplayStatus Replay(
        object? drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (drawing == null)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        using var graphNodeScope = WpfCaptureReplayGuard.Enter(drawing);
        if (drawing is PortableGeometryDrawingStateSource)
        {
            return TryReplayGeometryDrawing(drawing, sink, imageSourceAdapter);
        }

        if (drawing is PortableDrawingGroupStateSource)
        {
            return TryReplayDrawingGroup(drawing, sink, imageSourceAdapter);
        }

        if (drawing is PortableImageDrawingStateSource)
        {
            return TryReplayImageDrawing(drawing, sink, imageSourceAdapter);
        }

        if (drawing is PortableGlyphRunDrawingStateSource)
        {
            return TryReplayGlyphRunDrawing(drawing, sink);
        }

        return WpfDrawingReplayStatus.Unsupported;
    }

    internal static bool TryReplayDrawingImage(
        object? imageSource,
        Rect destinationBounds,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        ArgumentNullException.ThrowIfNull(sink);

        status = WpfDrawingReplayStatus.Skipped;
        if (imageSource is not PortableDrawingImageSource drawingImageSource)
        {
            return false;
        }

        if (!drawingImageSource.TryGetPortableDrawingImage(out var drawing)
            || drawing == null
            || !IsUsableRect(destinationBounds, out destinationBounds))
        {
            return true;
        }

        if (!TryGetDrawingBounds(drawing, imageSourceAdapter, out var sourceBounds))
        {
            status = WpfDrawingReplayStatus.Unsupported;
            return true;
        }

        var scaleX = destinationBounds.Width / sourceBounds.Width;
        var scaleY = destinationBounds.Height / sourceBounds.Height;
        var offsetX = destinationBounds.X - sourceBounds.X * scaleX;
        var offsetY = destinationBounds.Y - sourceBounds.Y * scaleY;
        var nativeScaleX = (float)scaleX;
        var nativeScaleY = (float)scaleY;
        var nativeOffsetX = (float)offsetX;
        var nativeOffsetY = (float)offsetY;
        if (!float.IsFinite(nativeScaleX)
            || !float.IsFinite(nativeScaleY)
            || !float.IsFinite(nativeOffsetX)
            || !float.IsFinite(nativeOffsetY))
        {
            status = WpfDrawingReplayStatus.Unsupported;
            return true;
        }

        PushRectangleClip(sink, destinationBounds);
        WpfPortableCommandSinkBridge.PushTransform(
            sink,
            new Matrix4x4(
                nativeScaleX, 0, 0, 0,
                0, nativeScaleY, 0, 0,
                0, 0, 1, 0,
                nativeOffsetX, nativeOffsetY, 0, 1));

        try
        {
            status = Replay(drawing, sink, imageSourceAdapter);
        }
        finally
        {
            sink.Pop();
            sink.Pop();
        }

        return true;
    }

    private static WpfDrawingReplayStatus TryReplayGeometryDrawing(
        object drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        var hasPortableGeometryDrawingState = TryGetPortableGeometryDrawingState(
            drawing,
            out var geometryDrawingState);
        if (!hasPortableGeometryDrawingState && drawing is PortableGeometryDrawingStateSource)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        if (!TryGetGeometryDrawingGeometry(drawing, hasPortableGeometryDrawingState, geometryDrawingState, out var geometryValue))
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        var hasBrush = TryGetGeometryDrawingBrush(
            drawing,
            hasPortableGeometryDrawingState,
            geometryDrawingState,
            out var brushValue);
        var hasPen = TryGetGeometryDrawingPen(
            drawing,
            hasPortableGeometryDrawingState,
            geometryDrawingState,
            out var penValue);

        var brush = WpfResourceResolver.AdaptBrush(brushValue);
        var pen = WpfResourceResolver.AdaptPen(penValue);
        var appliedAny = false;
        var unsupportedAny = hasPen && pen == null;

        if (hasBrush && brushValue is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource cacheBrush)
        {
            var cacheStatus = ReplayBitmapCacheBrushFill(cacheBrush, geometryValue, sink, imageSourceAdapter);
            bool penApplied = pen != null && TryDrawGeometryPen(geometryValue, pen, sink);
            if (pen != null && !penApplied) unsupportedAny = true;
            if (cacheStatus != WpfDrawingReplayStatus.Applied)
                return penApplied ? WpfDrawingReplayStatus.PartiallyApplied : cacheStatus;
            return unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied;
        }

        if (hasBrush
            && brushValue != null
            && IsTileBrush(brushValue)
            && TryReplayTileBrushFill(brushValue, geometryValue, sink, imageSourceAdapter, out var portableTileBrushStatus))
        {
            appliedAny = true;
            unsupportedAny |= portableTileBrushStatus == WpfDrawingReplayStatus.PartiallyApplied;
            if (pen != null)
            {
                if (TryDrawGeometryPen(geometryValue, pen, sink))
                {
                    appliedAny = true;
                }
                else
                {
                    unsupportedAny = true;
                }
            }

            return appliedAny
                ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
                : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
        }

        if (TryReplayNativePortableGeometryDrawing(
                geometryValue,
                brushValue,
                hasBrush,
                hasPen,
                brush,
                pen,
                sink,
                out var nativeStatus))
        {
            return nativeStatus;
        }

        if (TryReplayLineGeometryDrawing(
                geometryValue,
                brushValue,
                hasBrush,
                brush,
                pen,
                sink,
                out var lineStatus))
        {
            return lineStatus;
        }

        if (TryReplayPolylineGeometryDrawing(
                geometryValue,
                hasBrush,
                pen,
                sink,
                out var polylineStatus))
        {
            return polylineStatus;
        }

        if (TryReplayRectangleGeometryDrawing(
                geometryValue,
                brushValue,
                hasBrush,
                hasPen,
                brush,
                pen,
                sink,
                out var rectangleStatus))
        {
            return rectangleStatus;
        }

        if (TryReplayEllipseGeometryDrawing(
                geometryValue,
                brushValue,
                hasBrush,
                hasPen,
                brush,
                pen,
                sink,
                out var ellipseStatus))
        {
            return ellipseStatus;
        }

        if (WpfResourceResolver.AdaptGeometry(geometryValue) is not { } geometry)
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        if (!hasBrush)
        {
            if (pen != null)
            {
                DrawMediaGeometry(sink, null, pen, geometry);
                appliedAny = true;
            }
        }
        else if (IsTileBrush(brushValue)
            && TryReplayTileBrushFill(brushValue!, geometry, sink, imageSourceAdapter, out var tileBrushStatus))
        {
            appliedAny = true;
            unsupportedAny |= tileBrushStatus == WpfDrawingReplayStatus.PartiallyApplied;
            if (pen != null)
            {
                DrawMediaGeometry(sink, null, pen, geometry);
            }
        }
        else if (brush != null)
        {
            DrawMediaGeometry(sink, brush, pen, geometry);
            appliedAny = true;
        }
        else
        {
            unsupportedAny = true;
            if (pen != null)
            {
                DrawMediaGeometry(sink, null, pen, geometry);
                appliedAny = true;
            }
        }

        return appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
    }

    private static bool TryDrawGeometryPen(object? geometryValue, MediaPen pen, IWpfCompositionCommandSink sink)
    {
        if (TryGetDirectLineGeometry(geometryValue, out var startPoint, out var endPoint))
        {
            DrawLineGeometry(sink, pen, startPoint, endPoint);
            return true;
        }

        if (TryGetDirectPolylineGeometry(geometryValue, out var segments))
        {
            DrawPolylineGeometry(sink, pen, segments);
            return true;
        }

        if (TryGetDirectRectangleGeometry(geometryValue, out var rectangle, out var rectangleRadiusX, out var rectangleRadiusY))
        {
            DrawRectangleGeometry(sink, null, pen, rectangle, rectangleRadiusX, rectangleRadiusY);
            return true;
        }

        if (TryGetDirectRectangleStrokeGeometry(geometryValue, out var strokeRectangle))
        {
            DrawRectangleGeometry(sink, null, pen, strokeRectangle, 0, 0);
            return true;
        }

        if (TryGetDirectEllipseGeometry(geometryValue, out var center, out var radiusX, out var radiusY))
        {
            DrawEllipseGeometry(sink, null, pen, center, radiusX, radiusY);
            return true;
        }

        if (sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && TryGetPortableGeometryPath(geometryValue, out var portableGeometry)
            && nativeGeometrySink.DrawNativeGeometry(null, pen, portableGeometry))
        {
            return true;
        }

        if (WpfResourceResolver.AdaptGeometry(geometryValue) is not { } geometry)
        {
            return false;
        }

        DrawMediaGeometry(sink, null, pen, geometry);
        return true;
    }

    private static void DrawMediaGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        MediaGeometry geometry)
    {
        if (sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.DrawNativeGeometry(brush, pen, geometry))
        {
            return;
        }

        sink.DrawGeometry(brush, pen, geometry);
    }

    private static bool TryReplayLineGeometryDrawing(
        object? geometryValue,
        object? brushValue,
        bool hasBrush,
        MediaBrush? brush,
        MediaPen? pen,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (pen == null ||
            !TryGetDirectLineGeometry(geometryValue, out var startPoint, out var endPoint))
        {
            return false;
        }

        DrawLineGeometry(sink, pen, startPoint, endPoint);
        status = hasBrush && (brush == null || IsTileBrush(brushValue))
            ? WpfDrawingReplayStatus.PartiallyApplied
            : WpfDrawingReplayStatus.Applied;
        return true;
    }

    private static bool TryReplayPolylineGeometryDrawing(
        object? geometryValue,
        bool hasBrush,
        MediaPen? pen,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (hasBrush
            || pen == null
            || !TryGetDirectPolylineGeometry(geometryValue, out var segments))
        {
            return false;
        }

        DrawPolylineGeometry(sink, pen, segments);
        status = WpfDrawingReplayStatus.Applied;
        return true;
    }

    private static bool TryReplayEllipseGeometryDrawing(
        object? geometryValue,
        object? brushValue,
        bool hasBrush,
        bool hasPen,
        MediaBrush? brush,
        MediaPen? pen,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (!TryGetDirectEllipseGeometry(geometryValue, out var center, out var radiusX, out var radiusY))
        {
            return false;
        }

        var applied = false;
        var unsupported = hasPen && pen == null;
        if (!hasBrush)
        {
            if (pen != null)
            {
                DrawEllipseGeometry(sink, null, pen, center, radiusX, radiusY);
                applied = true;
            }
        }
        else if (IsTileBrush(brushValue))
        {
            unsupported = true;
            if (pen != null)
            {
                DrawEllipseGeometry(sink, null, pen, center, radiusX, radiusY);
                applied = true;
            }
        }
        else if (brush != null)
        {
            DrawEllipseGeometry(sink, brush, pen, center, radiusX, radiusY);
            applied = true;
        }
        else
        {
            unsupported = true;
            if (pen != null)
            {
                DrawEllipseGeometry(sink, null, pen, center, radiusX, radiusY);
                applied = true;
            }
        }

        status = applied
            ? unsupported ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupported ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
        return true;
    }

    private static bool TryReplayRectangleGeometryDrawing(
        object? geometryValue,
        object? brushValue,
        bool hasBrush,
        bool hasPen,
        MediaBrush? brush,
        MediaPen? pen,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (!TryGetDirectRectangleGeometry(geometryValue, out var rectangle, out var radiusX, out var radiusY))
        {
            if (hasBrush
                || pen == null
                || !TryGetDirectRectangleStrokeGeometry(geometryValue, out rectangle))
            {
                return false;
            }

            DrawRectangleGeometry(sink, null, pen, rectangle, 0, 0);
            status = WpfDrawingReplayStatus.Applied;
            return true;
        }

        var applied = false;
        var unsupported = hasPen && pen == null;
        if (!hasBrush)
        {
            if (pen != null)
            {
                DrawRectangleGeometry(sink, null, pen, rectangle, radiusX, radiusY);
                applied = true;
            }
        }
        else if (IsTileBrush(brushValue))
        {
            unsupported = true;
            if (pen != null)
            {
                DrawRectangleGeometry(sink, null, pen, rectangle, radiusX, radiusY);
                applied = true;
            }
        }
        else if (brush != null)
        {
            DrawRectangleGeometry(sink, brush, pen, rectangle, radiusX, radiusY);
            applied = true;
        }
        else
        {
            unsupported = true;
            if (pen != null)
            {
                DrawRectangleGeometry(sink, null, pen, rectangle, radiusX, radiusY);
                applied = true;
            }
        }

        status = applied
            ? unsupported ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupported ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
        return true;
    }

    private static void DrawLineGeometry(
        IWpfCompositionCommandSink sink,
        MediaPen pen,
        Point startPoint,
        Point endPoint)
    {
        if (sink is IWpfNativePrimitiveCommandSink nativePrimitiveSink)
        {
            nativePrimitiveSink.DrawNativeLine(
                pen,
                new WpfReplayPoint(startPoint.X, startPoint.Y),
                new WpfReplayPoint(endPoint.X, endPoint.Y));
            return;
        }

        sink.DrawLine(pen, startPoint, endPoint);
    }

    private static void DrawPolylineGeometry(
        IWpfCompositionCommandSink sink,
        MediaPen pen,
        IReadOnlyList<WpfReplayLineSegment> segments)
    {
        if (sink is IWpfNativePrimitiveCommandSink nativePrimitiveSink)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                nativePrimitiveSink.DrawNativeLine(pen, segment.StartPoint, segment.EndPoint);
            }

            return;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            sink.DrawLine(
                pen,
                new Point(segment.StartPoint.X, segment.StartPoint.Y),
                new Point(segment.EndPoint.X, segment.EndPoint.Y));
        }
    }

    private static void DrawRectangleGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        Rect rectangle,
        double radiusX,
        double radiusY)
    {
        if (sink is IWpfNativePrimitiveCommandSink nativePrimitiveSink)
        {
            if (radiusX > 0 || radiusY > 0)
            {
                nativePrimitiveSink.DrawNativeRoundedRectangle(brush, pen, ToReplayRect(rectangle), radiusX, radiusY);
            }
            else
            {
                nativePrimitiveSink.DrawNativeRectangle(brush, pen, ToReplayRect(rectangle));
            }

            return;
        }

        if (radiusX > 0 || radiusY > 0)
        {
            sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }
        else
        {
            sink.DrawRectangle(brush, pen, rectangle);
        }
    }

    private static void DrawEllipseGeometry(
        IWpfCompositionCommandSink sink,
        MediaBrush? brush,
        MediaPen? pen,
        Point center,
        double radiusX,
        double radiusY)
    {
        if (sink is IWpfNativePrimitiveCommandSink nativePrimitiveSink)
        {
            nativePrimitiveSink.DrawNativeEllipse(brush, pen, new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
            return;
        }

        sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
    }

    private static bool TryReplayNativePortableGeometryDrawing(
        object? geometryValue,
        object? brushValue,
        bool hasBrush,
        bool hasPen,
        MediaBrush? brush,
        MediaPen? pen,
        IWpfCompositionCommandSink sink,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (brushValue != null && IsTileBrush(brushValue))
        {
            return false;
        }

        if (sink is not IWpfNativeGeometryCommandSink nativeGeometrySink
            || !TryGetPortableGeometryPath(geometryValue, out var portableGeometry))
        {
            return false;
        }

        var unsupported = hasPen && pen == null;
        MediaBrush? nativeBrush = null;
        if (!hasBrush)
        {
            if (pen == null)
            {
                return false;
            }
        }
        else if (brush != null)
        {
            nativeBrush = brush;
        }
        else
        {
            status = pen != null
                ? WpfDrawingReplayStatus.PartiallyApplied
                : WpfDrawingReplayStatus.Unsupported;
            if (pen == null)
            {
                return true;
            }

            unsupported = true;
        }

        if (!nativeGeometrySink.DrawNativeGeometry(nativeBrush, pen, portableGeometry))
        {
            return false;
        }

        status = unsupported ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied;
        return true;
    }

    internal static bool TryReplayTileBrushFill(
        object brush,
        MediaGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        return TryReplayTileBrushFill(brush, (object?)geometry, sink, imageSourceAdapter, out status);
    }

    internal static bool TryReplayTileBrushEllipseFill(
        object brush,
        Point center,
        double radiusX,
        double radiusY,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (!double.IsFinite(center.X)
            || !double.IsFinite(center.Y)
            || !double.IsFinite(radiusX)
            || !double.IsFinite(radiusY)
            || radiusX <= 0
            || radiusY <= 0)
        {
            return false;
        }

        return TryReplayTileBrushFill(
            brush,
            new MediaEllipseGeometry(center, radiusX, radiusY),
            sink,
            imageSourceAdapter,
            out status);
    }

    internal static bool TryReplayTileBrushFill(
        object brush,
        object? geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        if (brush is PortableTileBrushSource portableSource)
        {
            return TryReplayPortableTileBrushFill(portableSource, geometry, sink, imageSourceAdapter, out status);
        }

        status = WpfDrawingReplayStatus.Skipped;
        return false;
    }

    internal static WpfDrawingReplayStatus ReplayBitmapCacheBrushFill(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        object? geometry, IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
        => ReplayBitmapCacheBrushFillCore(source, geometry, null, sink, imageSourceAdapter);

    internal static WpfDrawingReplayStatus ReplayBitmapCacheBrushRectangleFill(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source, Rect bounds,
        IWpfCompositionCommandSink sink, Func<object?, MediaImageSource?>? imageSourceAdapter)
        => ReplayBitmapCacheBrushFillCore(source, null,
            new TileBrushFillGeometry(null, bounds, null, null, true), sink, imageSourceAdapter);

    private static WpfDrawingReplayStatus ReplayBitmapCacheBrushFillCore(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        object? geometry, TileBrushFillGeometry? preparedFill, IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!source.TryGetPortableBitmapCacheBrush(out var brush)) return WpfDrawingReplayStatus.Unsupported;
        if (!double.IsFinite(brush.Opacity) || brush.Opacity < 0 || brush.Opacity > 1)
            return WpfDrawingReplayStatus.Unsupported;
        if (brush.InternalTarget == null || brush.Opacity == 0) return WpfDrawingReplayStatus.Applied;
        if (sink is not IWpfBitmapCacheBrushCommandSink cachedSink
            || sink is not IWpfNativeTransformCommandSink transformSink) return WpfDrawingReplayStatus.Unsupported;
        TileBrushFillGeometry fill;
        if (preparedFill is { } ready) fill = ready;
        else if (!TryGetTileBrushFillGeometry(geometry, out fill)) return WpfDrawingReplayStatus.Unsupported;
        if (fill.Bounds.IsEmpty || fill.Bounds.Width == 0 || fill.Bounds.Height == 0) return WpfDrawingReplayStatus.Applied;
        if (!global::ProGPU.Wpf.Interop.PortableBitmapCacheBrushPolicy.TryGetMapping(brush,
                new PortableRect(fill.Bounds.X, fill.Bounds.Y, fill.Bounds.Width, fill.Bounds.Height), out var mapping))
            return WpfDrawingReplayStatus.Unsupported;
        if (mapping.M11 * mapping.M22 - mapping.M12 * mapping.M21 == 0) return WpfDrawingReplayStatus.Applied;
        if (!PushTileBrushFillClip(sink, fill)) return WpfDrawingReplayStatus.Unsupported;
        int pops = 1;
        try
        {
            if (brush.Opacity != 1) { sink.PushOpacity(brush.Opacity); pops++; }
            if (!mapping.IsIdentity) { transformSink.PushNativeTransform(mapping); pops++; }
            cachedSink.DrawBitmapCacheBrushSource(source, imageSourceAdapter);
            return WpfDrawingReplayStatus.Applied;
        }
        finally { while (pops-- > 0) sink.Pop(); }
    }

    internal static bool TryReplaySourceBrushEllipseFill(object brush, Point center, double radiusX, double radiusY,
        IWpfCompositionCommandSink sink, Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        if (brush is not global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
            return TryReplayTileBrushEllipseFill(brush, center, radiusX, radiusY, sink, imageSourceAdapter, out status);
        status = WpfDrawingReplayStatus.Unsupported;
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(radiusX)
            || !double.IsFinite(radiusY) || radiusX < 0 || radiusY < 0) return true;
        if (radiusX == 0 || radiusY == 0) { status = WpfDrawingReplayStatus.Applied; return true; }
        var bounds = new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2);
        var fill = new TileBrushFillGeometry(null, bounds, null, null, false,
            new WpfReplayPoint(center.X, center.Y), radiusX, radiusY);
        status = ReplayBitmapCacheBrushFillCore(source, null, fill, sink, imageSourceAdapter);
        return true;
    }

    internal static bool IsSourceBrush(object? brush) => IsTileBrush(brush)
        || brush is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource;

    internal static bool TryReplaySourceBrushFill(object brush, object? geometry,
        IWpfCompositionCommandSink sink, Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        if (brush is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
        {
            status = ReplayBitmapCacheBrushFill(source, geometry, sink, imageSourceAdapter);
            return true;
        }
        return TryReplayTileBrushFill(brush, geometry, sink, imageSourceAdapter, out status);
    }

    private static bool TryReplayPortableTileBrushFill(
        PortableTileBrushSource portableSource,
        object? geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (!portableSource.TryGetPortableTileBrush(out var portableBrush)
            || !TryGetTileBrushFillGeometry(geometry, out var fillGeometry))
        {
            return false;
        }

        switch (portableBrush.Kind)
        {
            case PortableTileBrushKind.Image:
                if (portableBrush.Content is PortableDrawingImageSource drawingImageSource)
                {
                    if (!drawingImageSource.TryGetPortableDrawingImage(out var drawingImageContent)
                        || drawingImageContent == null)
                    {
                        return true;
                    }

                    _ = TryReplayPortableDrawingBrushFill(
                        portableBrush,
                        drawingImageContent,
                        fillGeometry,
                        sink,
                        imageSourceAdapter,
                        out status);
                    return true;
                }

                if (TryReplayPortableImageBrushFill(portableBrush, fillGeometry, sink, imageSourceAdapter))
                {
                    status = WpfDrawingReplayStatus.Applied;
                    return true;
                }

                return false;

            case PortableTileBrushKind.Drawing:
                return TryReplayPortableDrawingBrushFill(portableBrush, fillGeometry, sink, imageSourceAdapter, out status);

            case PortableTileBrushKind.Visual:
                return TryReplayPortableVisualBrushFill(portableBrush, fillGeometry, sink, imageSourceAdapter, out status);

            default:
                return false;
        }
    }

    private static bool TryReplayPortableImageBrushFill(
        PortableTileBrush brush,
        TileBrushFillGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        if (!TryGetOptionalBrushTransform(brush, out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || ResolveImageSource(brush.Content, imageSourceAdapter) is not { } imageSource
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var imageBounds)
            || !TryGetImageBrushSourceRect(brush, imageSource, out var sourceRect)
            || !TryGetImageStretchSourceBounds(stretch, sourceRect, imageSource, out var imageStretchSourceBounds)
            || !TryGetTileBounds(imageBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        if (!PushTileBrushFillClip(sink, geometry))
        {
            return false;
        }

        popCount++;

        if (brush.Opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(brush.Opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var tileCount = tileBounds.Count;
        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            var tile = tileBounds.GetAt(tileIndex);
            if (!TryGetStretchedTile(tile, imageStretchSourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                PushRectangleClip(sink, tile.Bounds);
                tilePopCount++;
            }

            if (TryCreateTileFlipTransform(stretchedTile, tileMode, out var tileTransform))
            {
                WpfPortableCommandSinkBridge.PushTransform(sink, tileTransform);
                tilePopCount++;
            }

            if (sourceRect.HasValue)
            {
                sink.DrawImage(imageSource, stretchedTile.Bounds, sourceRect.Value);
            }
            else
            {
                sink.DrawImage(imageSource, stretchedTile.Bounds);
            }

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return true;
    }

    private static bool TryReplayPortableDrawingBrushFill(
        PortableTileBrush brush,
        TileBrushFillGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        return TryReplayPortableDrawingBrushFill(
            brush,
            brush.Content,
            geometry,
            sink,
            imageSourceAdapter,
            out status);
    }

    private static bool TryReplayPortableDrawingBrushFill(
        PortableTileBrush brush,
        object? drawingValue,
        TileBrushFillGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        if (drawingValue == null
            || !TryGetOptionalBrushTransform(brush, out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetDrawingBounds(drawingValue, imageSourceAdapter, out var drawingBounds)
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var destinationBounds)
            || !TryGetTileBrushSourceBounds(brush, drawingBounds, out var sourceBounds, out var hasSourceClip)
            || !TryGetTileBounds(destinationBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        if (!PushTileBrushFillClip(sink, geometry))
        {
            return false;
        }

        popCount++;

        if (brush.Opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(brush.Opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var appliedAny = false;
        var unsupportedAny = false;
        var tileCount = tileBounds.Count;
        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            var tile = tileBounds.GetAt(tileIndex);
            if (!TryGetStretchedTile(tile, sourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip)
                || !TryCreateBoundsMappingTransform(sourceBounds, stretchedTile, tileMode, out var transform))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                PushRectangleClip(sink, tile.Bounds);
                tilePopCount++;
            }

            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            tilePopCount++;

            if (hasSourceClip)
            {
                PushRectangleClip(sink, sourceBounds);
                tilePopCount++;
            }

            var tileStatus = Replay(drawingValue, sink, imageSourceAdapter);
            appliedAny |= tileStatus == WpfDrawingReplayStatus.Applied
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= tileStatus == WpfDrawingReplayStatus.Unsupported
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        status = appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    private static bool TryReplayPortableVisualBrushFill(
        PortableTileBrush brush,
        TileBrushFillGeometry geometry,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out WpfDrawingReplayStatus status)
    {
        status = WpfDrawingReplayStatus.Skipped;
        var visualValue = brush.Content;
        if (!TryGetOptionalBrushTransform(brush, out var brushTransform)
            || !TryGetSupportedTileMode(brush, out var tileMode)
            || !TryGetSupportedStretch(brush, out var stretch)
            || !TryGetTileBrushAlignment(brush, out var alignmentX, out var alignmentY)
            || !IsUsableRect(geometry.Bounds, out var geometryBounds)
            || !TryGetOptionalRelativeBrushTransform(brush, geometryBounds, out var relativeTransform)
            || !TryGetVisualBounds(visualValue, out var visualBounds)
            || !TryGetTileBrushDestinationBounds(brush, geometryBounds, out var destinationBounds)
            || !TryGetTileBrushSourceBounds(brush, visualBounds, out var sourceBounds, out var hasSourceClip)
            || !TryGetTileBounds(destinationBounds, geometryBounds, tileMode, out var tileBounds))
        {
            return false;
        }

        var popCount = 0;
        if (!PushTileBrushFillClip(sink, geometry))
        {
            return false;
        }

        popCount++;

        if (brush.Opacity != 1)
        {
            sink.PushOpacity(Math.Clamp(brush.Opacity, 0, 1));
            popCount++;
        }

        if (relativeTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, relativeTransform);
            popCount++;
        }

        if (brushTransform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, brushTransform);
            popCount++;
        }

        var appliedAny = false;
        var unsupportedAny = false;
        WpfVisualTreeRenderer? visualBrushRenderer = null;
        IWpfImageSourceAdapter? visualBrushImageSourceAdapter = null;
        var visualBrushImageSourceAdapterInitialized = false;
        var tileCount = tileBounds.Count;
        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            var tile = tileBounds.GetAt(tileIndex);
            if (!TryGetStretchedTile(tile, sourceBounds, stretch, alignmentX, alignmentY, out var stretchedTile, out var needsTileClip)
                || !TryCreateBoundsMappingTransform(sourceBounds, stretchedTile, tileMode, out var transform))
            {
                continue;
            }

            var tilePopCount = 0;
            if (needsTileClip)
            {
                PushRectangleClip(sink, tile.Bounds);
                tilePopCount++;
            }

            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            tilePopCount++;

            if (hasSourceClip)
            {
                PushRectangleClip(sink, sourceBounds);
                tilePopCount++;
            }

            visualBrushRenderer ??= new WpfVisualTreeRenderer();
            if (!visualBrushImageSourceAdapterInitialized)
            {
                visualBrushImageSourceAdapter = CreateImageSourceAdapter(imageSourceAdapter);
                visualBrushImageSourceAdapterInitialized = true;
            }

            var result = visualBrushRenderer.ReplaySubtree(
                visualValue,
                sink,
                resources: null,
                imageSourceAdapter: visualBrushImageSourceAdapter);
            var tileStatus = ToDrawingReplayStatus(result);
            appliedAny |= tileStatus == WpfDrawingReplayStatus.Applied
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;
            unsupportedAny |= tileStatus == WpfDrawingReplayStatus.Unsupported
                || tileStatus == WpfDrawingReplayStatus.PartiallyApplied;

            for (var i = 0; i < tilePopCount; i++)
            {
                sink.Pop();
            }
        }

        status = appliedAny
            ? unsupportedAny ? WpfDrawingReplayStatus.PartiallyApplied : WpfDrawingReplayStatus.Applied
            : unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        return status == WpfDrawingReplayStatus.Applied
            || status == WpfDrawingReplayStatus.PartiallyApplied;
    }

    private static WpfDrawingReplayStatus TryReplayDrawingGroup(
        object drawingGroup,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        var hasPortableDrawingGroupState = TryGetPortableDrawingGroupState(
            drawingGroup,
            out var drawingGroupState);
        if (!hasPortableDrawingGroupState && drawingGroup is PortableDrawingGroupStateSource)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        var drawingGroupScopeBoundsInitialized = false;
        var drawingGroupScopeBoundsAvailable = false;
        var drawingGroupScopeBounds = default(Rect);

        bool TryGetDrawingGroupScopeBounds(out Rect bounds)
        {
            if (!drawingGroupScopeBoundsInitialized)
            {
                drawingGroupScopeBoundsAvailable =
                    TryGetDrawingGroupBounds(drawingGroup, hasPortableDrawingGroupState, drawingGroupState, out drawingGroupScopeBounds)
                    || TryInferDrawingGroupContentBounds(
                        drawingGroup,
                        hasPortableDrawingGroupState,
                        drawingGroupState,
                        imageSourceAdapter,
                        out drawingGroupScopeBounds);
                drawingGroupScopeBoundsInitialized = true;
            }

            bounds = drawingGroupScopeBounds;
            return drawingGroupScopeBoundsAvailable;
        }

        global::ProGPU.Scene.EffectBase? effect = null;
        Rect? effectBounds = null;
        var hasEffect = false;
        if (TryGetDrawingGroupEffect(drawingGroup, hasPortableDrawingGroupState, drawingGroupState, out var effectValue))
        {
            hasEffect = true;
            if (!WpfEffectMapper.TryCreateProGpuEffect(
                    effectValue,
                    out var proGpuEffect,
                    CreateImageSourceAdapter(imageSourceAdapter))
                || !TryGetDrawingGroupScopeBounds(out var resolvedEffectBounds))
            {
                return WpfDrawingReplayStatus.Unsupported;
            }

            effect = proGpuEffect;
            effectBounds = resolvedEffectBounds;
        }
        else if (TryGetDrawingGroupBitmapEffect(drawingGroup, hasPortableDrawingGroupState, drawingGroupState, out var bitmapEffect))
        {
            hasEffect = true;
            TryGetDrawingGroupBitmapEffectInput(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var bitmapEffectInput);
            if (!WpfEffectMapper.TryCreateProGpuPushEffect(
                    bitmapEffect,
                    bitmapEffectInput,
                    out var proGpuEffect,
                    CreateImageSourceAdapter(imageSourceAdapter))
                || !TryGetDrawingGroupScopeBounds(out var resolvedEffectBounds))
            {
                return WpfDrawingReplayStatus.Unsupported;
            }

            effect = proGpuEffect;
            effectBounds = resolvedEffectBounds;
        }
        else if (HasDrawingGroupBitmapEffectInput(drawingGroup, hasPortableDrawingGroupState, drawingGroupState))
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        MediaBrush? opacityMask = null;
        var opacityMaskBounds = default(Rect);
        var hasOpacityMask = false;
        if (TryGetDrawingGroupOpacityMask(drawingGroup, hasPortableDrawingGroupState, drawingGroupState, out var maskValue))
        {
            opacityMask = WpfResourceResolver.AdaptBrush(maskValue);
            if (opacityMask == null || !TryGetDrawingGroupScopeBounds(out opacityMaskBounds))
            {
                return WpfDrawingReplayStatus.Unsupported;
            }

            hasOpacityMask = true;
        }

        var popCount = 0;

        var hasTransform = TryGetDrawingGroupTransform(
            drawingGroup,
            hasPortableDrawingGroupState,
            drawingGroupState,
            out var transformValue);
        Matrix4x4 nativeTransform = default;
        MediaTransform? transform = null;
        var useNativeTransform = hasTransform
            && sink is IWpfNativeTransformCommandSink
            && WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out nativeTransform);
        if (hasTransform && !useNativeTransform)
        {
            transform = WpfResourceResolver.AdaptTransform(transformValue);
            if (transform == null)
            {
                return WpfDrawingReplayStatus.Unsupported;
            }
        }

        var hasClip = TryGetDrawingGroupClipGeometry(
            drawingGroup,
            hasPortableDrawingGroupState,
            drawingGroupState,
            out var clipValue);

        if (useNativeTransform)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, nativeTransform);
            popCount++;
        }
        else if (transform != null)
        {
            WpfPortableCommandSinkBridge.PushTransform(sink, transform);
            popCount++;
        }

        if (hasClip)
        {
            if (TryPushNativeDrawingGroupClip(sink, clipValue))
            {
                popCount++;
            }
            else
            {
                var clip = WpfResourceResolver.AdaptGeometry(clipValue);
                if (clip == null)
                {
                    PopPushedScopes(sink, popCount);
                    return WpfDrawingReplayStatus.Unsupported;
                }

                if (!TryPushNativeMediaGeometryClip(sink, clip))
                {
                    sink.PushClip(clip);
                }

                popCount++;
            }
        }

        if (TryGetDrawingGroupOpacity(drawingGroup, hasPortableDrawingGroupState, drawingGroupState, out var opacity)
            && opacity != 1)
        {
            sink.PushOpacity(opacity);
            popCount++;
        }

        if (hasOpacityMask)
        {
            WpfPortableCommandSinkBridge.PushOpacityMask(sink, opacityMask, ToReplayRect(opacityMaskBounds));
            popCount++;
        }

        var unsupportedGroupState = false;
        if (HasDrawingGroupCacheMode(drawingGroup, hasPortableDrawingGroupState, drawingGroupState))
        {
            if (TryGetDrawingGroupScopeBounds(out var cacheBounds)
                && WpfPortableCommandSinkBridge.TryPushDrawingCache(sink, ToReplayRect(cacheBounds)))
            {
                popCount++;
            }
            else
            {
                unsupportedGroupState = true;
            }
        }

        if (hasEffect)
        {
            if (!WpfPortableCommandSinkBridge.TryPushVisualEffect(sink, effect!, ToReplayRect(effectBounds)))
            {
                for (var i = 0; i < popCount; i++)
                {
                    sink.Pop();
                }

                return WpfDrawingReplayStatus.Unsupported;
            }

            popCount++;
        }

        if (TryGetDrawingGroupGuidelineSet(drawingGroup, hasPortableDrawingGroupState, drawingGroupState, out var guidelineSet))
        {
            sink.PushGuidelineSet(guidelineSet);
            popCount++;
        }

        var unsupportedRenderOptions = HasUnsupportedRenderOptionState(
            drawingGroup,
            hasPortableDrawingGroupState,
            drawingGroupState);
        if (TryGetDrawingGroupBitmapScalingMode(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var bitmapScalingMode)
            && WpfBitmapScalingModeMapper.HasExplicitValue(bitmapScalingMode))
        {
            if (WpfBitmapScalingModeMapper.IsSupported(bitmapScalingMode))
            {
                sink.PushBitmapScalingMode(bitmapScalingMode);
                popCount++;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        if (TryGetDrawingGroupEdgeMode(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var edgeMode)
            && WpfEdgeModeMapper.HasExplicitValue(edgeMode))
        {
            if (WpfEdgeModeMapper.IsSupported(edgeMode))
            {
                sink.PushEdgeMode(edgeMode);
                popCount++;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        var pushedTextRenderingMode = false;
        if (TryGetDrawingGroupTextRenderingMode(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var textRenderingMode)
            && WpfTextRenderingModeMapper.HasExplicitValue(textRenderingMode))
        {
            if (WpfTextRenderingModeMapper.IsSupported(textRenderingMode))
            {
                sink.PushTextRenderingMode(textRenderingMode);
                popCount++;
                pushedTextRenderingMode = true;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        if (!pushedTextRenderingMode
            && TryGetDrawingGroupClearTypeHint(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var clearTypeHint)
            && WpfTextRenderingModeMapper.HasExplicitClearTypeHint(clearTypeHint)
            && WpfTextRenderingModeMapper.TryMapClearTypeHintToTextRenderingMode(clearTypeHint, out var clearTypeMode))
        {
            sink.PushTextRenderingMode(clearTypeMode);
            popCount++;
        }

        if (TryGetDrawingGroupTextHintingMode(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var textHintingMode)
            && WpfTextRenderingModeMapper.HasExplicitTextHintingMode(textHintingMode))
        {
            if (WpfTextRenderingModeMapper.IsSupportedTextHintingMode(textHintingMode))
            {
                sink.PushTextHintingMode(textHintingMode);
                popCount++;
            }
            else
            {
                unsupportedRenderOptions = true;
            }
        }

        var appliedAny = false;
        var unsupportedAny = unsupportedGroupState || unsupportedRenderOptions;
        if (TryGetDrawingGroupChildren(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var childrenSource,
                out var children,
                out var childCount))
        {
            for (var i = 0; i < childCount; i++)
            {
                if (!TryGetDrawingGroupChild(childrenSource, children, i, out var child))
                {
                    continue;
                }

                var childStatus = Replay(child, sink, imageSourceAdapter);
                appliedAny |= childStatus == WpfDrawingReplayStatus.Applied
                    || childStatus == WpfDrawingReplayStatus.PartiallyApplied;
                unsupportedAny |= childStatus == WpfDrawingReplayStatus.Unsupported
                    || childStatus == WpfDrawingReplayStatus.PartiallyApplied;
            }
        }

        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }

        if (appliedAny && unsupportedAny)
        {
            return WpfDrawingReplayStatus.PartiallyApplied;
        }

        if (appliedAny)
        {
            return WpfDrawingReplayStatus.Applied;
        }

        return unsupportedAny ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
    }

    private static bool TryPushNativeDrawingGroupClip(
        IWpfCompositionCommandSink sink,
        object? clipValue)
    {
        if (!TryGetPortableGeometryPath(clipValue, out var nativeClip))
        {
            return false;
        }

        if (sink is IWpfNativeClipCommandSink nativeClipSink
            && TryGetRectangleClipBounds(nativeClip, out var clipBounds))
        {
            nativeClipSink.PushNativeClip(clipBounds);
            return true;
        }

        return sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.PushNativeGeometryClip(nativeClip);
    }

    private static bool TryGetRectangleClipBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        return WpfPortableRectangleClipReader.TryGetRectangleClipBounds(geometry, out bounds);
    }

    private static bool TryGetRectangleClipBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        return WpfMediaRectangleClipReader.TryGetRectangleClipBounds(geometry, out bounds);
    }

    private static bool TryPushNativeMediaGeometryClip(
        IWpfCompositionCommandSink sink,
        MediaGeometry clipGeometry)
    {
        if (sink is IWpfNativeClipCommandSink nativeClipSink
            && TryGetRectangleClipBounds(clipGeometry, out var clipBounds))
        {
            nativeClipSink.PushNativeClip(clipBounds);
            return true;
        }

        return sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.PushNativeGeometryClip(clipGeometry);
    }

    private static void PopPushedScopes(IWpfCompositionCommandSink sink, int popCount)
    {
        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }
    }

    private static void PushRectangleClip(IWpfCompositionCommandSink sink, Rect bounds)
    {
        if (sink is IWpfNativeClipCommandSink nativeClipSink)
        {
            nativeClipSink.PushNativeClip(ToReplayRect(bounds));
            return;
        }

        sink.PushClip(WpfResourceResolver.CreateRectanglePath(bounds));
    }

    private static bool TryGetTileBrushFillGeometry(object? geometry, out TileBrushFillGeometry fillGeometry)
    {
        if (TryGetPortableGeometryPath(geometry, out var portableGeometry)
            && TryGetPortableGeometryBounds(geometry, out var portableBounds)
            && IsUsableRect(portableBounds, out portableBounds))
        {
            fillGeometry = new TileBrushFillGeometry(geometry, portableBounds, null, portableGeometry, false);
            return true;
        }

        if (geometry is MediaGeometry mediaGeometry)
        {
            if (TryGetDirectPrimitiveGeometryBounds(mediaGeometry, out var mediaBounds))
            {
                if (IsUsableRect(mediaBounds, out mediaBounds))
                {
                    fillGeometry = new TileBrushFillGeometry(geometry, mediaBounds, mediaGeometry, null, false);
                    return true;
                }

                fillGeometry = default;
                return false;
            }

            if (WpfMediaGeometryBoundsReader.TryGetGeometryBounds(mediaGeometry, out var mediaGeometryBounds)
                && IsUsableRect(ToRect(mediaGeometryBounds), out mediaBounds))
            {
                fillGeometry = new TileBrushFillGeometry(geometry, mediaBounds, mediaGeometry, null, false);
                return true;
            }
        }

        if (geometry is Rect rect
            && IsUsableRect(rect, out rect))
        {
            fillGeometry = new TileBrushFillGeometry(geometry, rect, null, null, true);
            return true;
        }

        if (geometry is WpfReplayRect replayRect
            && IsUsableRect(new Rect(replayRect.X, replayRect.Y, replayRect.Width, replayRect.Height), out rect))
        {
            fillGeometry = new TileBrushFillGeometry(geometry, rect, null, null, true);
            return true;
        }

        if (geometry is PortableRect portableRect
            && TryReadPortableRect(portableRect, out rect)
            && IsUsableRect(rect, out rect))
        {
            fillGeometry = new TileBrushFillGeometry(geometry, rect, null, null, true);
            return true;
        }

        fillGeometry = default;
        return false;
    }

    private static bool PushTileBrushFillClip(IWpfCompositionCommandSink sink, TileBrushFillGeometry geometry)
    {
        if (geometry.EllipseRadiusX > 0)
            return sink is IWpfNativeGeometryCommandSink ellipseSink
                && ellipseSink.PushNativeEllipseClip(geometry.EllipseCenter, geometry.EllipseRadiusX, geometry.EllipseRadiusY);
        if (geometry.PortableGeometry != null)
        {
            if (sink is IWpfNativeClipCommandSink nativeClipSink
                && TryGetRectangleClipBounds(geometry.PortableGeometry, out var clipBounds))
            {
                nativeClipSink.PushNativeClip(clipBounds);
                return true;
            }

            if (sink is IWpfNativeGeometryCommandSink nativeGeometrySink
                && nativeGeometrySink.PushNativeGeometryClip(geometry.PortableGeometry))
            {
                return true;
            }
        }

        if (geometry.MediaGeometry != null
            && sink is IWpfNativeClipCommandSink mediaNativeClipSink
            && TryGetRectangleClipBounds(geometry.MediaGeometry, out var mediaClipBounds))
        {
            mediaNativeClipSink.PushNativeClip(mediaClipBounds);
            return true;
        }

        if (geometry.MediaGeometry != null
            && sink is IWpfNativeGeometryCommandSink mediaNativeGeometrySink
            && mediaNativeGeometrySink.PushNativeGeometryClip(geometry.MediaGeometry))
        {
            return true;
        }

        if (geometry.IsRectangle)
        {
            PushRectangleClip(sink, geometry.Bounds);
            return true;
        }

        var mediaGeometry = geometry.MediaGeometry ?? WpfResourceResolver.AdaptGeometry(geometry.Source);
        if (mediaGeometry == null)
        {
            return false;
        }

        sink.PushClip(mediaGeometry);
        return true;
    }

    private static WpfDrawingReplayStatus TryReplayImageDrawing(
        object drawing,
        IWpfCompositionCommandSink sink,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        var hasPortableImageDrawingState = TryGetPortableImageDrawingState(
            drawing,
            out var imageDrawingState);
        if (!hasPortableImageDrawingState && drawing is PortableImageDrawingStateSource)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        if (!TryGetImageDrawingImageSource(drawing, hasPortableImageDrawingState, imageDrawingState, out var imageValue)
            || !TryGetImageDrawingRect(drawing, hasPortableImageDrawingState, imageDrawingState, out var rectangle))
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        if (TryReplayDrawingImage(imageValue, rectangle, sink, imageSourceAdapter, out var drawingImageStatus))
        {
            return drawingImageStatus;
        }

        if (ResolveImageSource(imageValue, imageSourceAdapter) is not { } imageSource)
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        sink.DrawImage(imageSource, rectangle);
        return WpfDrawingReplayStatus.Applied;
    }

    private static MediaImageSource? ResolveImageSource(
        object? imageSource,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        return imageSourceAdapter?.Invoke(imageSource)
            ?? imageSource as MediaImageSource;
    }

    private static bool TryGetOptionalBrushTransform(
        PortableTileBrush brush,
        out MediaTransform? transform)
    {
        transform = null;
        if (!brush.HasTransform || brush.Transform.IsIdentity)
        {
            return true;
        }

        return TryCreateMatrixTransform(ToMatrix4x4(brush.Transform), out transform);
    }

    private static bool TryGetOptionalRelativeBrushTransform(
        PortableTileBrush brush,
        Rect fillBounds,
        out MediaTransform? transform)
    {
        transform = null;
        if (!brush.HasRelativeTransform || brush.RelativeTransform.IsIdentity)
        {
            return true;
        }

        return TryCreateRelativeBoundsTransform(ToMatrix4x4(brush.RelativeTransform), fillBounds, out transform);
    }

    private static System.Numerics.Matrix4x4 ToMatrix4x4(PortableMatrix3x2 matrix)
    {
        return new System.Numerics.Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0f,
            0f,
            (float)matrix.M21,
            (float)matrix.M22,
            0f,
            0f,
            0f,
            0f,
            1f,
            0f,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0f,
            1f);
    }

    private static bool TryCreateRelativeBoundsTransform(
        System.Numerics.Matrix4x4 relativeMatrix,
        Rect fillBounds,
        out MediaTransform? transform)
    {
        transform = null;
        if (!IsUsableRect(fillBounds, out fillBounds))
        {
            return false;
        }

        var boundsMatrix = System.Numerics.Matrix4x4.CreateTranslation((float)-fillBounds.X, (float)-fillBounds.Y, 0)
            * System.Numerics.Matrix4x4.CreateScale((float)(1 / fillBounds.Width), (float)(1 / fillBounds.Height), 1)
            * relativeMatrix
            * System.Numerics.Matrix4x4.CreateScale((float)fillBounds.Width, (float)fillBounds.Height, 1)
            * System.Numerics.Matrix4x4.CreateTranslation((float)fillBounds.X, (float)fillBounds.Y, 0);

        return TryCreateMatrixTransform(boundsMatrix, out transform);
    }

    private static bool TryCreateMatrixTransform(
        System.Numerics.Matrix4x4 matrix,
        out MediaTransform? transform)
    {
        transform = null;
        if (!NearlyEqual(matrix.M13, 0)
            || !NearlyEqual(matrix.M14, 0)
            || !NearlyEqual(matrix.M23, 0)
            || !NearlyEqual(matrix.M24, 0)
            || !NearlyEqual(matrix.M31, 0)
            || !NearlyEqual(matrix.M32, 0)
            || !NearlyEqual(matrix.M33, 1)
            || !NearlyEqual(matrix.M34, 0)
            || !NearlyEqual(matrix.M43, 0)
            || !NearlyEqual(matrix.M44, 1))
        {
            return false;
        }

        transform = new MediaMatrixTransform(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.M41,
            matrix.M42);
        return true;
    }

    private static bool TryGetSupportedTileMode(PortableTileBrush brush, out SupportedTileMode tileMode)
    {
        switch (brush.TileMode)
        {
            case PortableTileMode.None:
                tileMode = SupportedTileMode.None;
                return true;
            case PortableTileMode.Tile:
                tileMode = SupportedTileMode.Tile;
                return true;
            case PortableTileMode.FlipX:
                tileMode = SupportedTileMode.FlipX;
                return true;
            case PortableTileMode.FlipY:
                tileMode = SupportedTileMode.FlipY;
                return true;
            case PortableTileMode.FlipXY:
                tileMode = SupportedTileMode.FlipXY;
                return true;
            default:
                tileMode = SupportedTileMode.None;
                return false;
        }
    }

    private static bool TryGetSupportedStretch(PortableTileBrush brush, out SupportedStretch stretch)
    {
        switch (brush.Stretch)
        {
            case PortableStretch.None:
                stretch = SupportedStretch.None;
                return true;
            case PortableStretch.Fill:
                stretch = SupportedStretch.Fill;
                return true;
            case PortableStretch.Uniform:
                stretch = SupportedStretch.Uniform;
                return true;
            case PortableStretch.UniformToFill:
                stretch = SupportedStretch.UniformToFill;
                return true;
            default:
                stretch = SupportedStretch.Fill;
                return false;
        }
    }

    private static bool TryGetTileBrushAlignment(
        PortableTileBrush brush,
        out SupportedAlignmentX alignmentX,
        out SupportedAlignmentY alignmentY)
    {
        alignmentX = brush.AlignmentX switch
        {
            PortableAlignmentX.Left => SupportedAlignmentX.Left,
            PortableAlignmentX.Right => SupportedAlignmentX.Right,
            _ => SupportedAlignmentX.Center
        };

        alignmentY = brush.AlignmentY switch
        {
            PortableAlignmentY.Top => SupportedAlignmentY.Top,
            PortableAlignmentY.Bottom => SupportedAlignmentY.Bottom,
            _ => SupportedAlignmentY.Center
        };

        return true;
    }

    private static bool TryGetTileBrushDestinationBounds(
        PortableTileBrush brush,
        Rect fillBounds,
        out Rect destinationBounds)
    {
        destinationBounds = default;
        var viewport = ToRect(brush.Viewport);
        return IsUsableRect(viewport, out viewport)
            && TryGetViewportDestinationBounds(brush, fillBounds, viewport, out destinationBounds);
    }

    private static bool TryGetViewportDestinationBounds(
        PortableTileBrush brush,
        Rect fillBounds,
        Rect viewport,
        out Rect destinationBounds)
    {
        destinationBounds = default;

        if (brush.ViewportUnits == PortableBrushMappingMode.RelativeToBoundingBox)
        {
            return IsUsableRect(
                new Rect(
                    fillBounds.X + fillBounds.Width * viewport.X,
                    fillBounds.Y + fillBounds.Height * viewport.Y,
                    fillBounds.Width * viewport.Width,
                    fillBounds.Height * viewport.Height),
                out destinationBounds);
        }

        if (brush.ViewportUnits == PortableBrushMappingMode.Absolute)
        {
            return IsUsableRect(viewport, out destinationBounds);
        }

        return false;
    }

    private static bool TryGetTileBounds(
        Rect viewport,
        Rect fillBounds,
        SupportedTileMode tileMode,
        out TileBrushReplayTiles tileBounds)
    {
        tileBounds = default;
        if (!IsUsableRect(viewport, out viewport)
            || !IsUsableRect(fillBounds, out fillBounds))
        {
            return false;
        }

        if (tileMode == SupportedTileMode.None)
        {
            tileBounds = new TileBrushReplayTiles(viewport, 0, 0, 0, 0);
            return true;
        }

        var startX = (int)Math.Floor((fillBounds.X - viewport.X) / viewport.Width);
        var endX = (int)Math.Ceiling((fillBounds.X + fillBounds.Width - viewport.X) / viewport.Width) - 1;
        var startY = (int)Math.Floor((fillBounds.Y - viewport.Y) / viewport.Height);
        var endY = (int)Math.Ceiling((fillBounds.Y + fillBounds.Height - viewport.Y) / viewport.Height) - 1;

        var columnCount = endX - startX + 1;
        var rowCount = endY - startY + 1;
        if (columnCount <= 0
            || rowCount <= 0
            || columnCount > MaxTileBrushReplayTiles
            || rowCount > MaxTileBrushReplayTiles
            || columnCount * rowCount > MaxTileBrushReplayTiles)
        {
            return false;
        }

        tileBounds = new TileBrushReplayTiles(viewport, startX, endX, startY, endY);
        return true;
    }

    private static bool TryGetImageStretchSourceBounds(
        SupportedStretch stretch,
        Rect? sourceRect,
        MediaImageSource imageSource,
        out Rect sourceBounds)
    {
        sourceBounds = default;
        if (stretch == SupportedStretch.Fill)
        {
            return true;
        }

        if (sourceRect.HasValue)
        {
            return IsUsableRect(sourceRect.Value, out sourceBounds);
        }

        return TryGetImagePixelBounds(imageSource, out sourceBounds);
    }

    private static bool TryGetStretchedTile(
        TileBrushReplayTile tile,
        Rect sourceBounds,
        SupportedStretch stretch,
        SupportedAlignmentX alignmentX,
        SupportedAlignmentY alignmentY,
        out TileBrushReplayTile stretchedTile,
        out bool needsTileClip)
    {
        stretchedTile = tile;
        needsTileClip = false;

        if (stretch == SupportedStretch.Fill)
        {
            return true;
        }

        if (!IsUsableRect(sourceBounds, out sourceBounds)
            || !IsUsableRect(tile.Bounds, out var tileBounds))
        {
            return false;
        }

        var width = sourceBounds.Width;
        var height = sourceBounds.Height;
        if (stretch == SupportedStretch.Uniform || stretch == SupportedStretch.UniformToFill)
        {
            var scaleX = tileBounds.Width / sourceBounds.Width;
            var scaleY = tileBounds.Height / sourceBounds.Height;
            var scale = stretch == SupportedStretch.Uniform
                ? Math.Min(scaleX, scaleY)
                : Math.Max(scaleX, scaleY);
            width = sourceBounds.Width * scale;
            height = sourceBounds.Height * scale;
        }

        var x = alignmentX switch
        {
            SupportedAlignmentX.Left => tileBounds.X,
            SupportedAlignmentX.Right => tileBounds.X + tileBounds.Width - width,
            _ => tileBounds.X + (tileBounds.Width - width) / 2
        };
        var y = alignmentY switch
        {
            SupportedAlignmentY.Top => tileBounds.Y,
            SupportedAlignmentY.Bottom => tileBounds.Y + tileBounds.Height - height,
            _ => tileBounds.Y + (tileBounds.Height - height) / 2
        };

        var stretchedBounds = new Rect(x, y, width, height);
        if (!IsUsableRect(stretchedBounds, out stretchedBounds))
        {
            return false;
        }

        stretchedTile = new TileBrushReplayTile(stretchedBounds, tile.Column, tile.Row);
        needsTileClip = stretchedBounds.X < tileBounds.X
            || stretchedBounds.Y < tileBounds.Y
            || stretchedBounds.X + stretchedBounds.Width > tileBounds.X + tileBounds.Width
            || stretchedBounds.Y + stretchedBounds.Height > tileBounds.Y + tileBounds.Height;
        return true;
    }

    private static bool TryGetImageBrushSourceRect(
        PortableTileBrush brush,
        MediaImageSource imageSource,
        out Rect? sourceRect)
    {
        sourceRect = null;
        var viewbox = ToRect(brush.Viewbox);
        if (!IsUsableRect(viewbox, out viewbox))
        {
            return false;
        }

        if (brush.ViewboxUnits == PortableBrushMappingMode.RelativeToBoundingBox)
        {
            if (IsFullRelativeRect(viewbox))
            {
                return true;
            }

            if (!TryGetImagePixelBounds(imageSource, out var imageBounds))
            {
                return false;
            }

            return IsUsableRect(
                    new Rect(
                        imageBounds.X + imageBounds.Width * viewbox.X,
                        imageBounds.Y + imageBounds.Height * viewbox.Y,
                        imageBounds.Width * viewbox.Width,
                        imageBounds.Height * viewbox.Height),
                    out var relativeSourceRect)
                && AssignSourceRect(relativeSourceRect, out sourceRect);
        }

        return brush.ViewboxUnits == PortableBrushMappingMode.Absolute
            && AssignSourceRect(viewbox, out sourceRect);
    }

    private static bool TryGetImagePixelBounds(MediaImageSource imageSource, out Rect bounds)
    {
        if (imageSource is MediaBitmapSource bitmapSource
            && bitmapSource.PixelWidth > 0
            && bitmapSource.PixelHeight > 0)
        {
            bounds = new Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight);
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetTileBrushSourceBounds(
        PortableTileBrush brush,
        Rect contentBounds,
        out Rect sourceBounds,
        out bool hasSourceClip)
    {
        sourceBounds = contentBounds;
        hasSourceClip = false;

        var viewbox = ToRect(brush.Viewbox);
        if (!IsUsableRect(viewbox, out viewbox))
        {
            return false;
        }

        if (brush.ViewboxUnits == PortableBrushMappingMode.RelativeToBoundingBox)
        {
            if (IsFullRelativeRect(viewbox))
            {
                return true;
            }

            sourceBounds = new Rect(
                contentBounds.X + contentBounds.Width * viewbox.X,
                contentBounds.Y + contentBounds.Height * viewbox.Y,
                contentBounds.Width * viewbox.Width,
                contentBounds.Height * viewbox.Height);
            hasSourceClip = true;
            return IsUsableRect(sourceBounds, out sourceBounds);
        }

        if (brush.ViewboxUnits == PortableBrushMappingMode.Absolute)
        {
            sourceBounds = viewbox;
            hasSourceClip = !RectNearlyEqual(sourceBounds, contentBounds);
            return IsUsableRect(sourceBounds, out sourceBounds);
        }

        sourceBounds = default;
        hasSourceClip = false;
        return false;
    }

    private static bool AssignSourceRect(Rect value, out Rect? sourceRect)
    {
        sourceRect = value;
        return true;
    }

    private static Rect ToRect(PortableRect rect)
    {
        return rect.IsEmpty
            ? Rect.Empty
            : new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static Rect ToRect(WpfReplayRect rect)
    {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static bool IsFullRelativeRect(Rect rect)
    {
        return NearlyEqual(rect.X, 0)
            && NearlyEqual(rect.Y, 0)
            && NearlyEqual(rect.Width, 1)
            && NearlyEqual(rect.Height, 1);
    }

    private static bool RectNearlyEqual(Rect left, Rect right)
    {
        return NearlyEqual(left.X, right.X)
            && NearlyEqual(left.Y, right.Y)
            && NearlyEqual(left.Width, right.Width)
            && NearlyEqual(left.Height, right.Height);
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }

    private static bool TryCreateBoundsMappingTransform(
        Rect sourceBounds,
        TileBrushReplayTile tile,
        SupportedTileMode tileMode,
        out MediaMatrixTransform transform)
    {
        transform = null!;
        var destinationBounds = tile.Bounds;
        if (!IsUsableRect(sourceBounds, out sourceBounds)
            || !IsUsableRect(destinationBounds, out destinationBounds))
        {
            return false;
        }

        var flipX = tile.FlipX(tileMode);
        var flipY = tile.FlipY(tileMode);
        var scaleX = destinationBounds.Width / sourceBounds.Width * (flipX ? -1 : 1);
        var scaleY = destinationBounds.Height / sourceBounds.Height * (flipY ? -1 : 1);
        transform = new MediaMatrixTransform(
            scaleX,
            0,
            0,
            scaleY,
            (flipX ? destinationBounds.X + destinationBounds.Width : destinationBounds.X) - sourceBounds.X * scaleX,
            (flipY ? destinationBounds.Y + destinationBounds.Height : destinationBounds.Y) - sourceBounds.Y * scaleY);
        return true;
    }

    private static bool TryCreateTileFlipTransform(
        TileBrushReplayTile tile,
        SupportedTileMode tileMode,
        out MediaMatrixTransform transform)
    {
        transform = null!;
        var flipX = tile.FlipX(tileMode);
        var flipY = tile.FlipY(tileMode);
        if (!flipX && !flipY)
        {
            return false;
        }

        var bounds = tile.Bounds;
        transform = new MediaMatrixTransform(
            flipX ? -1 : 1,
            0,
            0,
            flipY ? -1 : 1,
            flipX ? bounds.X + bounds.X + bounds.Width : 0,
            flipY ? bounds.Y + bounds.Y + bounds.Height : 0);
        return true;
    }

    private static WpfDrawingReplayStatus TryReplayGlyphRunDrawing(object drawing, IWpfCompositionCommandSink sink)
    {
        var hasPortableGlyphRunDrawingState = TryGetPortableGlyphRunDrawingState(
            drawing,
            out var glyphRunDrawingState);
        if (!hasPortableGlyphRunDrawingState && drawing is PortableGlyphRunDrawingStateSource)
        {
            return WpfDrawingReplayStatus.Skipped;
        }

        if (!TryGetGlyphRunDrawingGlyphRun(
                drawing,
                hasPortableGlyphRunDrawingState,
                glyphRunDrawingState,
                out var glyphRunValue))
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        TryGetGlyphRunDrawingForegroundBrush(
            drawing,
            hasPortableGlyphRunDrawingState,
            glyphRunDrawingState,
            out var foregroundBrushValue);
        var foregroundBrush = WpfResourceResolver.AdaptBrush(foregroundBrushValue);
        if (sink is IWpfNativePrimitiveCommandSink nativeSink
            && WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRunValue, out var nativeGlyphRun))
        {
            nativeSink.DrawNativeGlyphRun(foregroundBrush, nativeGlyphRun);
            return WpfDrawingReplayStatus.Applied;
        }

        if (WpfResourceResolver.AdaptGlyphRun(glyphRunValue) is not { } glyphRun)
        {
            return WpfDrawingReplayStatus.Unsupported;
        }

        sink.DrawGlyphRun(foregroundBrush, glyphRun);
        return WpfDrawingReplayStatus.Applied;
    }

    private static bool HasUnsupportedRenderOptionState(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState)
    {
        if (TryGetDrawingGroupClearTypeHint(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var clearTypeHint)
            && WpfTextRenderingModeMapper.HasExplicitClearTypeHint(clearTypeHint)
            && !WpfTextRenderingModeMapper.IsSupportedClearTypeHint(clearTypeHint))
        {
            return true;
        }

        return TryGetDrawingGroupTextHintingMode(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var textHintingMode)
            && WpfTextRenderingModeMapper.HasExplicitTextHintingMode(textHintingMode)
            && !WpfTextRenderingModeMapper.IsSupportedTextHintingMode(textHintingMode);
    }

    private static bool TryGetPortableDrawingGroupState(
        object drawingGroup,
        out PortableDrawingGroupState? state)
    {
        if (drawingGroup is PortableDrawingGroupStateSource drawingGroupStateSource
            && drawingGroupStateSource.TryGetPortableDrawingGroupState(out var portableState))
        {
            state = portableState;
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryGetPortableGeometryDrawingState(
        object drawing,
        out PortableGeometryDrawingState? state)
    {
        if (drawing is PortableGeometryDrawingStateSource geometryDrawingStateSource
            && geometryDrawingStateSource.TryGetPortableGeometryDrawingState(out var portableState))
        {
            state = portableState;
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryGetPortableImageDrawingState(
        object drawing,
        out PortableImageDrawingState? state)
    {
        if (drawing is PortableImageDrawingStateSource imageDrawingStateSource
            && imageDrawingStateSource.TryGetPortableImageDrawingState(out var portableState))
        {
            state = portableState;
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryGetPortableGlyphRunDrawingState(
        object drawing,
        out PortableGlyphRunDrawingState? state)
    {
        if (drawing is PortableGlyphRunDrawingStateSource glyphRunDrawingStateSource
            && glyphRunDrawingStateSource.TryGetPortableGlyphRunDrawingState(out var portableState))
        {
            state = portableState;
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryGetGeometryDrawingGeometry(
        object drawing,
        bool hasPortableGeometryDrawingState,
        PortableGeometryDrawingState? geometryDrawingState,
        out object? geometry)
    {
        if (hasPortableGeometryDrawingState)
        {
            geometry = geometryDrawingState!.Geometry;
            return geometryDrawingState.HasGeometry && geometry != null;
        }

        geometry = null;
        return false;
    }

    private static bool TryGetGeometryDrawingBrush(
        object drawing,
        bool hasPortableGeometryDrawingState,
        PortableGeometryDrawingState? geometryDrawingState,
        out object? brush)
    {
        if (hasPortableGeometryDrawingState)
        {
            brush = geometryDrawingState!.Brush;
            return geometryDrawingState.HasBrush && brush != null;
        }

        brush = null;
        return false;
    }

    private static bool TryGetGeometryDrawingPen(
        object drawing,
        bool hasPortableGeometryDrawingState,
        PortableGeometryDrawingState? geometryDrawingState,
        out object? pen)
    {
        if (hasPortableGeometryDrawingState)
        {
            pen = geometryDrawingState!.Pen;
            return geometryDrawingState.HasPen && pen != null;
        }

        pen = null;
        return false;
    }

    private static bool TryGetPortableGeometryPath(object? geometry, out PortableGeometryPath portableGeometry)
    {
        portableGeometry = null!;
        return geometry is PortableGeometryPathSource portableGeometrySource
            && portableGeometrySource.TryGetPortableGeometryPath(out portableGeometry)
            && portableGeometry != null;
    }

    private static bool TryGetPortableGeometryBounds(object? geometry, out Rect bounds)
    {
        if (TryGetPortableGeometryPath(geometry, out var portableGeometry))
        {
            if (WpfPortableGeometryBoundsReader.TryGetGeometryBounds(portableGeometry, out var portableBounds)
                && IsUsableRect(ToRect(portableBounds), out bounds))
            {
                return true;
            }
        }

        return TryGetDirectPrimitiveGeometryBounds(geometry, out bounds);
    }

    private static bool TryGetDirectPrimitiveGeometryBounds(object? geometry, out Rect bounds)
    {
        if (TryGetDirectRectangleGeometry(geometry, out var rectangle, out _, out _)
            || TryGetDirectRectangleStrokeGeometry(geometry, out rectangle))
        {
            bounds = rectangle;
            return true;
        }

        if (TryGetDirectEllipseGeometry(geometry, out var center, out var radiusX, out var radiusY))
        {
            return IsUsableRect(
                new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2),
                out bounds);
        }

        if (TryGetDirectPolylineGeometry(geometry, out var segments))
        {
            return TryGetLineSegmentBounds(segments, out bounds);
        }

        return TryGetDirectLineGeometryBounds(geometry, out bounds);
    }

    private static bool TryGetDirectRectangleGeometryBounds(object? geometry, out Rect bounds)
    {
        if (geometry is Rect rect
            && IsUsableRect(rect, out bounds))
        {
            return true;
        }

        if (geometry is WpfReplayRect replayRect
            && IsUsableRect(new Rect(replayRect.X, replayRect.Y, replayRect.Width, replayRect.Height), out bounds))
        {
            return true;
        }

        if (geometry is PortableRect portableRect
            && TryReadPortableRect(portableRect, out bounds)
            && IsUsableRect(bounds, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetDirectLineGeometryBounds(object? geometry, out Rect bounds)
    {
        if (!TryGetDirectLineGeometry(geometry, out var startPoint, out var endPoint))
        {
            bounds = default;
            return false;
        }

        var x = Math.Min(startPoint.X, endPoint.X);
        var y = Math.Min(startPoint.Y, endPoint.Y);
        var width = Math.Abs(endPoint.X - startPoint.X);
        var height = Math.Abs(endPoint.Y - startPoint.Y);

        if (!double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || (width == 0 && height == 0))
        {
            bounds = default;
            return false;
        }

        bounds = new Rect(x, y, width, height);
        return true;
    }

    private static bool TryGetLineSegmentBounds(IReadOnlyList<WpfReplayLineSegment> segments, out Rect bounds)
    {
        bounds = default;
        if (segments.Count == 0)
        {
            return false;
        }

        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            IncludePoint(segment.StartPoint);
            IncludePoint(segment.EndPoint);
        }

        var width = right - left;
        var height = bottom - top;
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || (width == 0 && height == 0))
        {
            return false;
        }

        bounds = new Rect(left, top, width, height);
        return true;

        void IncludePoint(WpfReplayPoint point)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
    }

    private static bool TryGetDirectRectangleGeometry(
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

        if (TryGetDirectRectangleGeometryBounds(geometry, out rectangle))
        {
            radiusX = 0;
            radiusY = 0;
            return true;
        }

        radiusX = default;
        radiusY = default;
        return false;
    }

    private static bool TryGetDirectRectangleStrokeGeometry(
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

    private static bool TryGetDirectLineGeometry(
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

    private static bool TryGetDirectPolylineGeometry(
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

    private static bool TryGetDirectEllipseGeometry(
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

    private static bool TryGetImageDrawingImageSource(
        object drawing,
        bool hasPortableImageDrawingState,
        PortableImageDrawingState? imageDrawingState,
        out object? imageSource)
    {
        if (hasPortableImageDrawingState)
        {
            imageSource = imageDrawingState!.ImageSource;
            return imageDrawingState.HasImageSource && imageSource != null;
        }

        imageSource = null;
        return false;
    }

    private static bool TryGetImageDrawingRect(
        object drawing,
        bool hasPortableImageDrawingState,
        PortableImageDrawingState? imageDrawingState,
        out Rect rectangle)
    {
        if (hasPortableImageDrawingState)
        {
            return TryReadPortableRect(imageDrawingState!.Rect, out rectangle)
                && imageDrawingState.HasRect
                && IsUsableRect(rectangle, out rectangle);
        }

        rectangle = default;
        return false;
    }

    private static bool TryGetGlyphRunDrawingGlyphRun(
        object drawing,
        bool hasPortableGlyphRunDrawingState,
        PortableGlyphRunDrawingState? glyphRunDrawingState,
        out object? glyphRun)
    {
        if (hasPortableGlyphRunDrawingState)
        {
            glyphRun = glyphRunDrawingState!.GlyphRun;
            return glyphRunDrawingState.HasGlyphRun && glyphRun != null;
        }

        glyphRun = null;
        return false;
    }

    private static bool TryGetGlyphRunDrawingForegroundBrush(
        object drawing,
        bool hasPortableGlyphRunDrawingState,
        PortableGlyphRunDrawingState? glyphRunDrawingState,
        out object? foregroundBrush)
    {
        if (hasPortableGlyphRunDrawingState)
        {
            foregroundBrush = glyphRunDrawingState!.ForegroundBrush;
            return glyphRunDrawingState.HasForegroundBrush && foregroundBrush != null;
        }

        foregroundBrush = null;
        return false;
    }

    private static bool TryGetDrawingGroupBounds(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out Rect bounds)
    {
        if (hasPortableDrawingGroupState)
        {
            return TryReadPortableRect(drawingGroupState!.Bounds, out bounds)
                && drawingGroupState.HasBounds
                && IsUsableRect(bounds, out bounds);
        }

        bounds = default;
        return false;
    }

    private static bool TryGetDrawingGroupTransform(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? transform)
    {
        if (hasPortableDrawingGroupState)
        {
            transform = drawingGroupState!.Transform;
            return drawingGroupState.HasTransform && transform != null;
        }

        transform = null;
        return false;
    }

    private static bool TryGetDrawingGroupClipGeometry(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? clipGeometry)
    {
        if (hasPortableDrawingGroupState)
        {
            clipGeometry = drawingGroupState!.ClipGeometry;
            return drawingGroupState.HasClipGeometry && clipGeometry != null;
        }

        clipGeometry = null;
        return false;
    }

    private static bool TryGetDrawingGroupOpacity(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out double opacity)
    {
        if (hasPortableDrawingGroupState)
        {
            opacity = drawingGroupState!.Opacity;
            return drawingGroupState.HasOpacity;
        }

        opacity = 1;
        return false;
    }

    private static bool HasDrawingGroupOpacityMask(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState)
    {
        return TryGetDrawingGroupOpacityMask(
            drawingGroup,
            hasPortableDrawingGroupState,
            drawingGroupState,
            out _);
    }

    private static bool TryGetDrawingGroupOpacityMask(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? opacityMask)
    {
        if (hasPortableDrawingGroupState)
        {
            opacityMask = drawingGroupState!.OpacityMask;
            return drawingGroupState.HasOpacityMask && opacityMask != null;
        }

        opacityMask = null;
        return false;
    }

    private static bool TryGetDrawingGroupGuidelineSet(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? guidelineSet)
    {
        if (hasPortableDrawingGroupState)
        {
            guidelineSet = drawingGroupState!.GuidelineSet;
            return drawingGroupState.HasGuidelineSet && guidelineSet != null;
        }

        guidelineSet = null;
        return false;
    }

    private static bool TryGetDrawingGroupEffect(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? effect)
    {
        if (hasPortableDrawingGroupState)
        {
            effect = drawingGroupState!.Effect;
            return drawingGroupState.HasEffect && effect != null;
        }

        effect = null;
        return false;
    }

    private static bool TryGetDrawingGroupBitmapEffect(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? bitmapEffect)
    {
        if (hasPortableDrawingGroupState)
        {
            bitmapEffect = drawingGroupState!.BitmapEffect;
            return drawingGroupState.HasBitmapEffect && bitmapEffect != null;
        }

        bitmapEffect = null;
        return false;
    }

    private static bool HasDrawingGroupBitmapEffectInput(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState)
    {
        return TryGetDrawingGroupBitmapEffectInput(
            drawingGroup,
            hasPortableDrawingGroupState,
            drawingGroupState,
            out _);
    }

    private static bool TryGetDrawingGroupBitmapEffectInput(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? bitmapEffectInput)
    {
        if (hasPortableDrawingGroupState)
        {
            bitmapEffectInput = drawingGroupState!.BitmapEffectInput;
            return drawingGroupState.HasBitmapEffectInput && bitmapEffectInput != null;
        }

        bitmapEffectInput = null;
        return false;
    }

    private static bool HasDrawingGroupCacheMode(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState)
    {
        if (hasPortableDrawingGroupState)
        {
            var cacheMode = drawingGroupState!.CacheMode;
            return drawingGroupState.HasCacheMode && cacheMode != null;
        }

        return false;
    }

    private static bool TryGetDrawingGroupBitmapScalingMode(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? bitmapScalingMode)
    {
        if (hasPortableDrawingGroupState)
        {
            bitmapScalingMode = drawingGroupState!.BitmapScalingMode;
            return drawingGroupState.HasBitmapScalingMode && bitmapScalingMode != null;
        }

        bitmapScalingMode = null;
        return false;
    }

    private static bool TryGetDrawingGroupEdgeMode(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? edgeMode)
    {
        if (hasPortableDrawingGroupState)
        {
            edgeMode = drawingGroupState!.EdgeMode;
            return drawingGroupState.HasEdgeMode && edgeMode != null;
        }

        edgeMode = null;
        return false;
    }

    private static bool TryGetDrawingGroupClearTypeHint(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? clearTypeHint)
    {
        if (hasPortableDrawingGroupState)
        {
            clearTypeHint = drawingGroupState!.ClearTypeHint;
            return drawingGroupState.HasClearTypeHint && clearTypeHint != null;
        }

        clearTypeHint = null;
        return false;
    }

    private static bool TryGetDrawingGroupTextRenderingMode(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? textRenderingMode)
    {
        if (hasPortableDrawingGroupState)
        {
            textRenderingMode = drawingGroupState!.TextRenderingMode;
            return drawingGroupState.HasTextRenderingMode && textRenderingMode != null;
        }

        textRenderingMode = null;
        return false;
    }

    private static bool TryGetDrawingGroupTextHintingMode(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out object? textHintingMode)
    {
        if (hasPortableDrawingGroupState)
        {
            textHintingMode = drawingGroupState!.TextHintingMode;
            return drawingGroupState.HasTextHintingMode && textHintingMode != null;
        }

        textHintingMode = null;
        return false;
    }

    internal static bool TryGetDrawingBounds(
        object drawing,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect bounds)
    {
        using var graphBoundsScope = WpfCaptureReplayGuard.EnterBounds(drawing);
        if (drawing is PortableDrawingBoundsSource drawingBoundsSource)
        {
            if (drawingBoundsSource.TryGetPortableDrawingBounds(out var portableBounds)
                && TryReadPortableRect(portableBounds, out bounds)
                && IsUsableRect(bounds, out bounds))
            {
                return true;
            }

            bounds = default;
            return false;
        }

        if (drawing is PortableDrawingGroupStateSource)
        {
            var hasPortableDrawingGroupState = TryGetPortableDrawingGroupState(
                drawing,
                out var drawingGroupState);
            if (!hasPortableDrawingGroupState && drawing is PortableDrawingGroupStateSource)
            {
                bounds = default;
                return false;
            }

            var hasAuthoritativeBounds = TryGetDrawingGroupBounds(
                drawing,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out bounds);
            if (hasAuthoritativeBounds
                || TryInferDrawingGroupContentBounds(
                    drawing,
                    hasPortableDrawingGroupState,
                    drawingGroupState,
                    imageSourceAdapter,
                    out bounds))
            {
                if (!hasAuthoritativeBounds
                    && TryGetDrawingGroupTransform(
                        drawing,
                        hasPortableDrawingGroupState,
                        drawingGroupState,
                        out var transformValue))
                {
                    if (!WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out var transform))
                    {
                        bounds = default;
                        return false;
                    }

                    bounds = TransformBounds(bounds, transform);
                }

                return IsUsableRect(bounds, out bounds);
            }

            bounds = default;
            return false;
        }

        if (drawing is PortableGeometryDrawingStateSource)
        {
            var hasPortableGeometryDrawingState = TryGetPortableGeometryDrawingState(
                drawing,
                out var geometryDrawingState);
            if (!hasPortableGeometryDrawingState && drawing is PortableGeometryDrawingStateSource)
            {
                bounds = default;
                return false;
            }

            if (!TryGetGeometryDrawingGeometry(
                    drawing,
                    hasPortableGeometryDrawingState,
                    geometryDrawingState,
                    out var geometryValue))
            {
                bounds = default;
                return false;
            }

            if (TryGetPortableGeometryBounds(geometryValue, out bounds))
            {
                return true;
            }

            if (WpfResourceResolver.AdaptGeometry(geometryValue) is not { } geometry)
            {
                bounds = default;
                return false;
            }

            if (!WpfMediaGeometryBoundsReader.TryGetGeometryBounds(geometry, out var geometryBounds))
            {
                bounds = default;
                return false;
            }

            bounds = ToRect(geometryBounds);
            return true;
        }

        if (drawing is PortableImageDrawingStateSource)
        {
            var hasPortableImageDrawingState = TryGetPortableImageDrawingState(
                drawing,
                out var imageDrawingState);
            if (!hasPortableImageDrawingState && drawing is PortableImageDrawingStateSource)
            {
                bounds = default;
                return false;
            }

            if (!TryGetImageDrawingRect(
                    drawing,
                    hasPortableImageDrawingState,
                    imageDrawingState,
                    out var imageRect))
            {
                bounds = default;
                return false;
            }

            return IsUsableRect(imageRect, out bounds);
        }

        if (drawing is PortableGlyphRunDrawingStateSource)
        {
            var hasPortableGlyphRunDrawingState = TryGetPortableGlyphRunDrawingState(
                drawing,
                out var glyphRunDrawingState);
            if (!hasPortableGlyphRunDrawingState && drawing is PortableGlyphRunDrawingStateSource)
            {
                bounds = default;
                return false;
            }

            if (!TryGetGlyphRunDrawingGlyphRun(
                    drawing,
                    hasPortableGlyphRunDrawingState,
                    glyphRunDrawingState,
                    out var glyphRunValue))
            {
                bounds = default;
                return false;
            }

            if (WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRunValue, out var nativeGlyphRun))
            {
                return TryGetGlyphRunBounds(nativeGlyphRun, out bounds);
            }

            if (WpfResourceResolver.AdaptGlyphRun(glyphRunValue) is not { } glyphRun)
            {
                bounds = default;
                return false;
            }

            return TryGetGlyphRunBounds(glyphRun, out bounds);
        }

        bounds = default;
        return false;
    }

    internal static bool TryGetVisualBounds(object visual, out Rect bounds)
    {
        if (visual is PortableVisualBoundsSource boundsSource
            && boundsSource.TryGetPortableVisualBounds(out var portableBounds)
            && TryReadPortableVisualBounds(portableBounds, out bounds))
        {
            return true;
        }

        if (visual is PortableVisualLayoutStateSource layoutStateSource
            && layoutStateSource.TryGetPortableVisualLayoutState(out var layoutState)
            && TryReadPortableRenderSizeBounds(layoutState, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadPortableVisualBounds(PortableVisualBounds portableBounds, out Rect bounds)
    {
        if (portableBounds.HasDescendantBounds
            && TryReadPortableRect(portableBounds.DescendantBounds, out bounds)
            && IsUsableRect(bounds, out bounds))
        {
            return true;
        }

        if (portableBounds.HasContentBounds
            && TryReadPortableRect(portableBounds.ContentBounds, out bounds)
            && IsUsableRect(bounds, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadPortableRenderSizeBounds(PortableVisualLayoutState state, out Rect bounds)
    {
        if (state.HasRenderSize
            && IsUsableRect(new Rect(0, 0, state.RenderSize.Width, state.RenderSize.Height), out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static WpfDrawingReplayStatus ToDrawingReplayStatus(WpfVisualReplayResult result)
    {
        var applied = result.ContentCount > 0
            || result.RenderData.AppliedCount > 0;
        var unsupported = result.UnsupportedContentCount > 0
            || result.UnsupportedVisualStateCount > 0
            || result.RenderData.UnsupportedCount > 0;

        if (applied && unsupported)
        {
            return WpfDrawingReplayStatus.PartiallyApplied;
        }

        if (applied)
        {
            return WpfDrawingReplayStatus.Applied;
        }

        return unsupported ? WpfDrawingReplayStatus.Unsupported : WpfDrawingReplayStatus.Skipped;
    }

    private static IWpfImageSourceAdapter? CreateImageSourceAdapter(Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        return imageSourceAdapter == null ? null : new DelegateImageSourceAdapter(imageSourceAdapter);
    }

    private static bool TryInferDrawingGroupContentBounds(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        Func<object?, MediaImageSource?>? imageSourceAdapter,
        out Rect bounds)
    {
        var hasBounds = false;
        bounds = default;

        if (TryGetDrawingGroupChildren(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var childrenSource,
                out var children,
                out var childCount))
        {
            for (var i = 0; i < childCount; i++)
            {
                if (!TryGetDrawingGroupChild(childrenSource, children, i, out var child)
                    || !TryGetDrawingBounds(child, imageSourceAdapter, out var childBounds))
                {
                    continue;
                }

                bounds = hasBounds ? UnionBounds(bounds, childBounds) : childBounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            bounds = default;
            return false;
        }

        if (TryGetDrawingGroupClipGeometry(
                drawingGroup,
                hasPortableDrawingGroupState,
                drawingGroupState,
                out var clipValue)
            && TryGetDrawingGroupClipBounds(clipValue, out var clipBounds))
        {
            bounds = IntersectBounds(bounds, clipBounds);
        }

        return IsUsableRect(bounds, out bounds);
    }

    private static bool TryGetDrawingGroupClipBounds(object? clipValue, out Rect clipBounds)
    {
        if (TryGetPortableGeometryBounds(clipValue, out clipBounds))
        {
            return true;
        }

        if (WpfResourceResolver.AdaptGeometry(clipValue) is { } clipGeometry
            && WpfMediaGeometryBoundsReader.TryGetGeometryBounds(clipGeometry, out var geometryClipBounds)
            && IsUsableRect(ToRect(geometryClipBounds), out clipBounds))
        {
            return true;
        }

        clipBounds = default;
        return false;
    }

    private static bool TryGetGlyphRunBounds(MediaGlyphRun glyphRun, out Rect bounds)
    {
        bounds = default;

        if (glyphRun.FontSize <= 0)
        {
            return false;
        }

        var minX = glyphRun.Position.X;
        var minY = glyphRun.Position.Y - glyphRun.FontSize;
        var maxX = glyphRun.Position.X;
        var maxY = glyphRun.Position.Y;

        if (glyphRun.GlyphPositions.Length == 0)
        {
            maxX += glyphRun.FontSize;
        }
        else
        {
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
        }

        return IsUsableRect(TransformBounds(
            new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
            glyphRun.Transform),
            out bounds);
    }

    private static bool TryGetGlyphRunBounds(WpfNativeGlyphRun glyphRun, out Rect bounds)
    {
        bounds = default;
        if (glyphRun.HasBounds)
        {
            return IsUsableRect(ToRect(glyphRun.TransformedBounds), out bounds);
        }

        if (glyphRun.FontSize <= 0)
        {
            return false;
        }

        var minX = glyphRun.Position.X;
        var minY = glyphRun.Position.Y - glyphRun.FontSize;
        var maxX = glyphRun.Position.X;
        var maxY = glyphRun.Position.Y;

        if (glyphRun.GlyphPositions.Length == 0)
        {
            maxX += glyphRun.FontSize;
        }
        else
        {
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
        }

        return IsUsableRect(TransformBounds(
            new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
            glyphRun.Transform),
            out bounds);
    }

    private static Rect UnionBounds(Rect left, Rect right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Max(left.Y + left.Height, right.Y + right.Height);

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect IntersectBounds(Rect left, Rect right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);

        return x2 <= x1 || y2 <= y1
            ? Rect.Empty
            : new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private static Rect TransformBounds(Rect bounds, System.Numerics.Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        var p1 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)bounds.Y), transform);
        var p2 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)bounds.Y), transform);
        var p3 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)(bounds.Y + bounds.Height)), transform);
        var p4 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height)), transform);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool IsUsableRect(Rect rect, out Rect bounds)
    {
        bounds = rect;
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

    private static WpfReplayRect? ToReplayRect(Rect? bounds)
    {
        return bounds.HasValue ? ToReplayRect(bounds.Value) : null;
    }

    private static WpfReplayRect ToReplayRect(Rect bounds)
    {
        return new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool TryGetDrawingGroupChildren(
        object drawingGroup,
        bool hasPortableDrawingGroupState,
        PortableDrawingGroupState? drawingGroupState,
        out PortableDrawingGroupChildrenSource? childrenSource,
        out object[]? children,
        out int count)
    {
        if (drawingGroup is PortableDrawingGroupChildrenSource source
            && source.TryGetPortableDrawingGroupChildCount(out var sourceCount)
            && sourceCount > 0)
        {
            childrenSource = source;
            children = null;
            count = sourceCount;
            return true;
        }

        if (hasPortableDrawingGroupState)
        {
            children = drawingGroupState!.Children;
            if (children != null && children.Length > 0)
            {
                childrenSource = null;
                count = children.Length;
                return true;
            }
        }

        childrenSource = null;
        children = null;
        count = 0;
        return false;
    }

    private static bool TryGetDrawingGroupChild(
        PortableDrawingGroupChildrenSource? childrenSource,
        object[]? children,
        int index,
        out object child)
    {
        object? candidate;
        if (childrenSource != null)
        {
            if (!childrenSource.TryGetPortableDrawingGroupChild(index, out var sourceChild))
            {
                child = null!;
                return false;
            }

            candidate = sourceChild;
        }
        else if (children != null && (uint)index < (uint)children.Length)
        {
            candidate = children[index];
        }
        else
        {
            child = null!;
            return false;
        }

        if (candidate == null)
        {
            child = null!;
            return false;
        }

        child = candidate;
        return true;
    }

    private static bool TryReadPortableRect(PortableRect portableRect, out Rect rectangle)
    {
        if (portableRect.IsEmpty)
        {
            rectangle = default;
            return false;
        }

        rectangle = new Rect(portableRect.X, portableRect.Y, portableRect.Width, portableRect.Height);
        return true;
    }

    internal static bool IsTileBrush(object? brush)
    {
        return brush is PortableTileBrushSource;
    }

    private sealed class DelegateImageSourceAdapter : IWpfImageSourceAdapter
    {
        private readonly Func<object?, MediaImageSource?> _adapter;

        public DelegateImageSourceAdapter(Func<object?, MediaImageSource?> adapter)
        {
            _adapter = adapter;
        }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return _adapter(imageSource);
        }
    }
}
