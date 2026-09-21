// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace System.Windows.Media
{
    internal interface IRenderDataDrawingContextSink
    {
        void DrawLine(Pen pen, Point point0, Point point1);

        void DrawLine(Pen pen, Point point0, AnimationClock point0Animations, Point point1, AnimationClock point1Animations);

        void DrawRectangle(Brush brush, Pen pen, Rect rectangle);

        void DrawRectangle(Brush brush, Pen pen, Rect rectangle, AnimationClock rectangleAnimations);

        void DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX, double radiusY);

        void DrawRoundedRectangle(
            Brush brush,
            Pen pen,
            Rect rectangle,
            AnimationClock rectangleAnimations,
            double radiusX,
            AnimationClock radiusXAnimations,
            double radiusY,
            AnimationClock radiusYAnimations);

        void DrawEllipse(Brush brush, Pen pen, Point center, double radiusX, double radiusY);

        void DrawEllipse(
            Brush brush,
            Pen pen,
            Point center,
            AnimationClock centerAnimations,
            double radiusX,
            AnimationClock radiusXAnimations,
            double radiusY,
            AnimationClock radiusYAnimations);

        void DrawGeometry(Brush brush, Pen pen, Geometry geometry);

        void DrawImage(ImageSource imageSource, Rect rectangle);

        void DrawImage(ImageSource imageSource, Rect rectangle, AnimationClock rectangleAnimations);

        void DrawGlyphRun(Brush foregroundBrush, GlyphRun glyphRun);

        void DrawDrawing(Drawing drawing);

        void DrawVideo(MediaPlayer player, Rect rectangle);

        void DrawVideo(MediaPlayer player, Rect rectangle, AnimationClock rectangleAnimations);

        void PushClip(Geometry clipGeometry);

        void PushOpacityMask(Brush opacityMask);

        void PushOpacity(double opacity);

        void PushOpacity(double opacity, AnimationClock opacityAnimations);

        void PushTransform(Transform transform);

        void PushGuidelineSet(GuidelineSet guidelines);

        void PushGuidelineY1(double coordinate);

        void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate);

        void PushEffect(BitmapEffect effect, BitmapEffectInput effectInput);

        void Pop();

        void Close();
    }
}
