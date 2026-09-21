// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    internal sealed class ObjectRenderDataDrawingContextSink :
        IRenderDataDrawingContextSink,
        IPortableNativeDrawingContextSource,
        IPortableNativeDrawingContextStateSource
    {
        private readonly IPortableRenderDataDrawingContextSink _sink;

        internal ObjectRenderDataDrawingContextSink(IPortableRenderDataDrawingContextSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        bool IPortableNativeDrawingContextSource.TryGetPortableNativeDrawingContext(out object nativeDrawingContext)
        {
            if (_sink is IPortableNativeDrawingContextSource nativeDrawingContextSource)
            {
                return nativeDrawingContextSource.TryGetPortableNativeDrawingContext(out nativeDrawingContext);
            }

            nativeDrawingContext = null;
            return false;
        }

        bool IPortableNativeDrawingContextStateSource.TryGetPortableNativeDrawingContextState(
            out PortableNativeDrawingContextState state)
        {
            if (_sink is IPortableNativeDrawingContextStateSource nativeDrawingContextStateSource)
            {
                return nativeDrawingContextStateSource.TryGetPortableNativeDrawingContextState(out state);
            }

            if (_sink is IPortableNativeDrawingContextSource nativeDrawingContextSource
                && nativeDrawingContextSource.TryGetPortableNativeDrawingContext(out object nativeDrawingContext))
            {
                state = new PortableNativeDrawingContextState(
                    nativeDrawingContext,
                    System.Numerics.Matrix4x4.Identity);
                return true;
            }

            state = default;
            return false;
        }

        void IRenderDataDrawingContextSink.DrawLine(Pen pen, Point point0, Point point1)
        {
            _sink.DrawLine(pen, ToPortablePoint(point0), ToPortablePoint(point1));
        }

        void IRenderDataDrawingContextSink.DrawLine(Pen pen, Point point0, AnimationClock point0Animations, Point point1, AnimationClock point1Animations)
        {
            _sink.DrawLine(pen, ToPortablePoint(point0), point0Animations, ToPortablePoint(point1), point1Animations);
        }

        void IRenderDataDrawingContextSink.DrawRectangle(Brush brush, Pen pen, Rect rectangle)
        {
            _sink.DrawRectangle(brush, pen, ToPortableRect(rectangle));
        }

        void IRenderDataDrawingContextSink.DrawRectangle(Brush brush, Pen pen, Rect rectangle, AnimationClock rectangleAnimations)
        {
            _sink.DrawRectangle(brush, pen, ToPortableRect(rectangle), rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX, double radiusY)
        {
            _sink.DrawRoundedRectangle(brush, pen, ToPortableRect(rectangle), radiusX, radiusY);
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
            _sink.DrawRoundedRectangle(brush, pen, ToPortableRect(rectangle), rectangleAnimations, radiusX, radiusXAnimations, radiusY, radiusYAnimations);
        }

        void IRenderDataDrawingContextSink.DrawEllipse(Brush brush, Pen pen, Point center, double radiusX, double radiusY)
        {
            _sink.DrawEllipse(brush, pen, ToPortablePoint(center), radiusX, radiusY);
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
            _sink.DrawEllipse(brush, pen, ToPortablePoint(center), centerAnimations, radiusX, radiusXAnimations, radiusY, radiusYAnimations);
        }

        void IRenderDataDrawingContextSink.DrawGeometry(Brush brush, Pen pen, Geometry geometry)
        {
            _sink.DrawGeometry(brush, pen, geometry);
        }

        void IRenderDataDrawingContextSink.DrawImage(ImageSource imageSource, Rect rectangle)
        {
            _sink.DrawImage(imageSource, ToPortableRect(rectangle));
        }

        void IRenderDataDrawingContextSink.DrawImage(ImageSource imageSource, Rect rectangle, AnimationClock rectangleAnimations)
        {
            _sink.DrawImage(imageSource, ToPortableRect(rectangle), rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.DrawGlyphRun(Brush foregroundBrush, GlyphRun glyphRun)
        {
            _sink.DrawGlyphRun(foregroundBrush, glyphRun);
        }

        void IRenderDataDrawingContextSink.DrawDrawing(Drawing drawing)
        {
            _sink.DrawDrawing(drawing);
        }

        void IRenderDataDrawingContextSink.DrawVideo(MediaPlayer player, Rect rectangle)
        {
            _sink.DrawVideo(player, ToPortableRect(rectangle));
        }

        void IRenderDataDrawingContextSink.DrawVideo(MediaPlayer player, Rect rectangle, AnimationClock rectangleAnimations)
        {
            _sink.DrawVideo(player, ToPortableRect(rectangle), rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.PushClip(Geometry clipGeometry)
        {
            _sink.PushClip(clipGeometry);
        }

        void IRenderDataDrawingContextSink.PushOpacityMask(Brush opacityMask)
        {
            _sink.PushOpacityMask(opacityMask);
        }

        void IRenderDataDrawingContextSink.PushOpacity(double opacity)
        {
            _sink.PushOpacity(opacity);
        }

        void IRenderDataDrawingContextSink.PushOpacity(double opacity, AnimationClock opacityAnimations)
        {
            _sink.PushOpacity(opacity, opacityAnimations);
        }

        void IRenderDataDrawingContextSink.PushTransform(Transform transform)
        {
            _sink.PushTransform(transform);
        }

        void IRenderDataDrawingContextSink.PushGuidelineSet(GuidelineSet guidelines)
        {
            _sink.PushGuidelineSet(guidelines);
        }

        void IRenderDataDrawingContextSink.PushGuidelineY1(double coordinate)
        {
            _sink.PushGuidelineY1(coordinate);
        }

        void IRenderDataDrawingContextSink.PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            _sink.PushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
        }

        [Obsolete(MS.Internal.Media.VisualTreeUtils.BitmapEffectObsoleteMessage)]
        void IRenderDataDrawingContextSink.PushEffect(BitmapEffect effect, BitmapEffectInput effectInput)
        {
            _sink.PushEffect(effect, effectInput);
        }

        void IRenderDataDrawingContextSink.Pop()
        {
            _sink.Pop();
        }

        void IRenderDataDrawingContextSink.Close()
        {
            _sink.Close();
        }

        private static PortablePoint ToPortablePoint(Point point)
        {
            return new PortablePoint(point.X, point.Y);
        }

        private static PortableRect ToPortableRect(Rect rect)
        {
            return rect.IsEmpty
                ? PortableRect.Empty
                : new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }
}
