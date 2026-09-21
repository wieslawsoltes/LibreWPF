// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Media
{
    public interface IPortableRenderDataDrawingContextSink
    {
        void DrawLine(object pen, object point0, object point1);

        void DrawLine(object pen, object point0, object point0Animations, object point1, object point1Animations);

        void DrawRectangle(object brush, object pen, object rectangle);

        void DrawRectangle(object brush, object pen, object rectangle, object rectangleAnimations);

        void DrawRoundedRectangle(object brush, object pen, object rectangle, object radiusX, object radiusY);

        void DrawRoundedRectangle(
            object brush,
            object pen,
            object rectangle,
            object rectangleAnimations,
            object radiusX,
            object radiusXAnimations,
            object radiusY,
            object radiusYAnimations);

        void DrawEllipse(object brush, object pen, object center, object radiusX, object radiusY);

        void DrawEllipse(
            object brush,
            object pen,
            object center,
            object centerAnimations,
            object radiusX,
            object radiusXAnimations,
            object radiusY,
            object radiusYAnimations);

        void DrawGeometry(object brush, object pen, object geometry);

        void DrawImage(object imageSource, object rectangle);

        void DrawImage(object imageSource, object rectangle, object rectangleAnimations);

        void DrawGlyphRun(object foregroundBrush, object glyphRun);

        void DrawDrawing(object drawing);

        void DrawVideo(object player, object rectangle);

        void DrawVideo(object player, object rectangle, object rectangleAnimations);

        void PushClip(object clipGeometry);

        void PushOpacityMask(object opacityMask);

        void PushOpacity(object opacity);

        void PushOpacity(object opacity, object opacityAnimations);

        void PushTransform(object transform);

        void PushGuidelineSet(object guidelines);

        void PushGuidelineY1(object coordinate);

        void PushGuidelineY2(object leadingCoordinate, object offsetToDrivenCoordinate);

        void PushEffect(object effect, object effectInput);

        void Pop();

        void Close();
    }
}
