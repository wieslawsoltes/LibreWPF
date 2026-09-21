using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfGeneratedRenderDataDrawingContext
{
    void DrawLine(MediaPen? pen, Point point0, Point point1);

    void DrawLine(MediaPen? pen, Point point0, object? point0Animations, Point point1, object? point1Animations);

    void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle);

    void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, object? rectangleAnimations);

    void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY);

    void DrawRoundedRectangle(
        MediaBrush? brush,
        MediaPen? pen,
        Rect rectangle,
        object? rectangleAnimations,
        double radiusX,
        object? radiusXAnimations,
        double radiusY,
        object? radiusYAnimations);

    void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY);

    void DrawEllipse(
        MediaBrush? brush,
        MediaPen? pen,
        Point center,
        object? centerAnimations,
        double radiusX,
        object? radiusXAnimations,
        double radiusY,
        object? radiusYAnimations);

    void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry? geometry);

    void DrawImage(MediaImageSource? imageSource, Rect rectangle);

    void DrawImage(MediaImageSource? imageSource, Rect rectangle, object? rectangleAnimations);

    void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun? glyphRun);

    void DrawDrawing(object? drawing);

    void DrawVideo(object? player, Rect rectangle);

    void DrawVideo(object? player, Rect rectangle, object? rectangleAnimations);

    void PushClip(MediaGeometry? clipGeometry);

    void PushOpacityMask(MediaBrush? opacityMask);

    void PushOpacity(double opacity);

    void PushOpacity(double opacity, object? opacityAnimations);

    void PushTransform(MediaTransform? transform);

    void PushGuidelineSet(object? guidelines);

    void PushGuidelineY1(double coordinate);

    void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate);

    void PushEffect(object? effect, object? effectInput);

    void Pop();
}
