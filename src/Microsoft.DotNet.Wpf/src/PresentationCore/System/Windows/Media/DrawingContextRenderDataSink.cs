// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    internal sealed class DrawingContextRenderDataSink :
        IRenderDataDrawingContextSink,
        IPortableNativeDrawingContextSource,
        IPortableNativeDrawingContextStateSource
    {
        private readonly DrawingContext _drawingContext;

        internal DrawingContextRenderDataSink(DrawingContext drawingContext)
        {
            _drawingContext = drawingContext ?? throw new ArgumentNullException(nameof(drawingContext));
        }

        bool IPortableNativeDrawingContextSource.TryGetPortableNativeDrawingContext(out object nativeDrawingContext)
        {
            return ((IPortableNativeDrawingContextSource)_drawingContext)
                .TryGetPortableNativeDrawingContext(out nativeDrawingContext);
        }

        bool IPortableNativeDrawingContextStateSource.TryGetPortableNativeDrawingContextState(
            out PortableNativeDrawingContextState state)
        {
            return ((IPortableNativeDrawingContextStateSource)_drawingContext)
                .TryGetPortableNativeDrawingContextState(out state);
        }

        void IRenderDataDrawingContextSink.DrawLine(Pen pen, Point point0, Point point1)
        {
            _drawingContext.DrawLine(pen, point0, point1);
        }

        void IRenderDataDrawingContextSink.DrawLine(Pen pen, Point point0, AnimationClock point0Animations, Point point1, AnimationClock point1Animations)
        {
            _drawingContext.DrawLine(pen, point0, point0Animations, point1, point1Animations);
        }

        void IRenderDataDrawingContextSink.DrawRectangle(Brush brush, Pen pen, Rect rectangle)
        {
            _drawingContext.DrawRectangle(brush, pen, rectangle);
        }

        void IRenderDataDrawingContextSink.DrawRectangle(Brush brush, Pen pen, Rect rectangle, AnimationClock rectangleAnimations)
        {
            _drawingContext.DrawRectangle(brush, pen, rectangle, rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX, double radiusY)
        {
            _drawingContext.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
        }

        void IRenderDataDrawingContextSink.DrawRoundedRectangle(
            Brush brush,
            Pen pen,
            Rect rectangle,
            AnimationClock rectangleAnimations,
            double radiusX,
            AnimationClock radiusXAnimations,
            double radiusY,
            AnimationClock radiusYAnimations)
        {
            _drawingContext.DrawRoundedRectangle(brush, pen, rectangle, rectangleAnimations, radiusX, radiusXAnimations, radiusY, radiusYAnimations);
        }

        void IRenderDataDrawingContextSink.DrawEllipse(Brush brush, Pen pen, Point center, double radiusX, double radiusY)
        {
            _drawingContext.DrawEllipse(brush, pen, center, radiusX, radiusY);
        }

        void IRenderDataDrawingContextSink.DrawEllipse(
            Brush brush,
            Pen pen,
            Point center,
            AnimationClock centerAnimations,
            double radiusX,
            AnimationClock radiusXAnimations,
            double radiusY,
            AnimationClock radiusYAnimations)
        {
            _drawingContext.DrawEllipse(brush, pen, center, centerAnimations, radiusX, radiusXAnimations, radiusY, radiusYAnimations);
        }

        void IRenderDataDrawingContextSink.DrawGeometry(Brush brush, Pen pen, Geometry geometry)
        {
            _drawingContext.DrawGeometry(brush, pen, geometry);
        }

        void IRenderDataDrawingContextSink.DrawImage(ImageSource imageSource, Rect rectangle)
        {
            _drawingContext.DrawImage(imageSource, rectangle);
        }

        void IRenderDataDrawingContextSink.DrawImage(ImageSource imageSource, Rect rectangle, AnimationClock rectangleAnimations)
        {
            _drawingContext.DrawImage(imageSource, rectangle, rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.DrawGlyphRun(Brush foregroundBrush, GlyphRun glyphRun)
        {
            _drawingContext.DrawGlyphRun(foregroundBrush, glyphRun);
        }

        void IRenderDataDrawingContextSink.DrawDrawing(Drawing drawing)
        {
            _drawingContext.DrawDrawing(drawing);
        }

        void IRenderDataDrawingContextSink.DrawVideo(MediaPlayer player, Rect rectangle)
        {
            _drawingContext.DrawVideo(player, rectangle);
        }

        void IRenderDataDrawingContextSink.DrawVideo(MediaPlayer player, Rect rectangle, AnimationClock rectangleAnimations)
        {
            _drawingContext.DrawVideo(player, rectangle, rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.PushClip(Geometry clipGeometry)
        {
            _drawingContext.PushClip(clipGeometry);
        }

        void IRenderDataDrawingContextSink.PushOpacityMask(Brush opacityMask)
        {
            _drawingContext.PushOpacityMask(opacityMask);
        }

        void IRenderDataDrawingContextSink.PushOpacity(double opacity)
        {
            _drawingContext.PushOpacity(opacity);
        }

        void IRenderDataDrawingContextSink.PushOpacity(double opacity, AnimationClock opacityAnimations)
        {
            _drawingContext.PushOpacity(opacity, opacityAnimations);
        }

        void IRenderDataDrawingContextSink.PushTransform(Transform transform)
        {
            _drawingContext.PushTransform(transform);
        }

        void IRenderDataDrawingContextSink.PushGuidelineSet(GuidelineSet guidelines)
        {
            _drawingContext.PushGuidelineSet(guidelines);
        }

        void IRenderDataDrawingContextSink.PushGuidelineY1(double coordinate)
        {
            _drawingContext.PushGuidelineY1(coordinate);
        }

        void IRenderDataDrawingContextSink.PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            _drawingContext.PushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
        }

        [Obsolete(MS.Internal.Media.VisualTreeUtils.BitmapEffectObsoleteMessage)]
        void IRenderDataDrawingContextSink.PushEffect(BitmapEffect effect, BitmapEffectInput effectInput)
        {
            _drawingContext.PushEffect(effect, effectInput);
        }

        void IRenderDataDrawingContextSink.Pop()
        {
            _drawingContext.Pop();
        }

        void IRenderDataDrawingContextSink.Close()
        {
            _drawingContext.Close();
        }
    }
}
