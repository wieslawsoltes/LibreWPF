using System;
using System.Numerics;
using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using MediaFormattedText = System.Windows.Media.FormattedText;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableMediaPlayerFrame = ProGPU.Wpf.Interop.PortableMediaPlayerFrame;

namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfCompositionCommandSink : IDisposable
{
    MediaDrawingContext? DrawingContext { get; }

    void DrawLine(MediaPen? pen, Point point0, Point point1);

    void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle);

    void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY);

    void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY);

    void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry);

    void DrawImage(MediaImageSource imageSource, Rect rectangle);

    void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
    {
        DrawImage(imageSource, rectangle);
    }

    void DrawText(MediaFormattedText formattedText, Point origin);

    void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun);

    void PushClip(MediaGeometry clipGeometry);

    void PushOpacity(double opacity);

    void PushOpacityMask(MediaBrush? opacityMask, Rect bounds);

    void PushTransform(MediaTransform transform);

    void PushNoOpScope()
    {
    }

    void PushGuidelineSet()
    {
    }

    void PushGuidelineSet(object? guidelines)
    {
        PushGuidelineSet();
    }

    void PushGuidelineY1(double coordinate)
    {
    }

    void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
    }

    void PushBitmapScalingMode(object? bitmapScalingMode)
    {
        PushNoOpScope();
    }

    void PushEdgeMode(object? edgeMode)
    {
        PushNoOpScope();
    }

    void PushTextRenderingMode(object? textRenderingMode)
    {
        PushNoOpScope();
    }

    void PushTextHintingMode(object? textHintingMode)
    {
        PushNoOpScope();
    }

    void Pop();

    void Close();
}

internal interface IWpfNativeTransformCommandSink
{
    void PushNativeTransform(Matrix4x4 transform);
}

internal interface IWpfNativePrimitiveCommandSink
{
    void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1);

    void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle);

    void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY);

    void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY);

    void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle);

    void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle);

    void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRun);

    void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds);
}

internal interface IWpfNativeVideoCommandSink
{
    bool DrawNativeVideo(
        PortableMediaPlayerFrame frame,
        WpfReplayRect rectangle);
}

internal interface IWpfNativeClipCommandSink
{
    void PushNativeClip(WpfReplayRect bounds);
}

internal interface IWpfNativeGeometryCommandSink
{
    bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry);

    bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry) => false;

    bool PushNativeGeometryClip(PortableGeometryPath clipGeometry);

    bool PushNativeGeometryClip(MediaGeometry clipGeometry) => false;

    bool PushNativeEllipseClip(WpfReplayPoint center, double radiusX, double radiusY) => false;

    bool PushNativeRoundedRectangleClip(WpfReplayRect bounds, double radiusX, double radiusY) => false;
}

internal interface IWpfHitTestOwnerScopeCommandSink
{
    bool PushHitTestOwner(object sourceVisual);

    void PopHitTestOwner();
}

internal interface IWpfProGpuSceneDrawingContextSource
{
    bool TryGetProGpuSceneDrawingContext(out global::ProGPU.Scene.DrawingContext? drawingContext);

    bool TryGetProGpuSceneDrawingContextState(
        out global::ProGPU.Scene.DrawingContext? drawingContext,
        out Matrix4x4 transform);
}

internal interface IWpfBitmapCacheBrushCommandSink
{
    void DrawBitmapCacheBrushSource(global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        Func<object?, MediaImageSource?>? imageSourceAdapter);
}
