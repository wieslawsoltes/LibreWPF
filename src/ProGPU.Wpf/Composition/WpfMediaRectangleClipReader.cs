using System;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaLineSegment = System.Windows.Media.LineSegment;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaRectangleGeometry = System.Windows.Media.RectangleGeometry;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfMediaRectangleClipReader
{
    public static bool TryGetRectangleClipBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!TryGetAxisAlignedGeometryTransform(geometry, out var transform))
        {
            return false;
        }

        if (geometry is MediaRectangleGeometry rectangleGeometry)
        {
            return rectangleGeometry.RadiusX == 0
                && rectangleGeometry.RadiusY == 0
                && TryCreateUsableRect(rectangleGeometry.Rect, out bounds)
                && TryTransformAxisAlignedBounds(bounds, transform, out bounds);
        }

        return geometry is MediaPathGeometry pathGeometry
            && TryGetRectanglePathBounds(
                pathGeometry,
                requireFilled: true,
                requireStrokedSegments: false,
                out bounds)
            && TryTransformAxisAlignedBounds(bounds, transform, out bounds);
    }

    public static bool TryGetRectangleStrokeBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!TryGetAxisAlignedGeometryTransform(geometry, out var transform))
        {
            return false;
        }

        return geometry is MediaPathGeometry pathGeometry
            && TryGetRectanglePathBounds(
                pathGeometry,
                requireFilled: false,
                requireStrokedSegments: true,
                out bounds)
            && TryTransformAxisAlignedBounds(bounds, transform, out bounds);
    }

    private static bool TryGetAxisAlignedGeometryTransform(MediaGeometry geometry, out System.Numerics.Matrix4x4 transform)
    {
        var transformValue = geometry.Transform;
        if (transformValue == null)
        {
            transform = System.Numerics.Matrix4x4.Identity;
            return true;
        }

        if (!WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
        {
            return false;
        }

        return IsAxisAlignedTransform(transform);
    }

    private static bool IsAxisAlignedTransform(System.Numerics.Matrix4x4 transform)
    {
        return transform.M12 == 0.0f
            && transform.M21 == 0.0f
            && float.IsFinite(transform.M11)
            && float.IsFinite(transform.M22)
            && float.IsFinite(transform.M41)
            && float.IsFinite(transform.M42);
    }

    private static bool TryTransformAxisAlignedBounds(
        WpfReplayRect bounds,
        System.Numerics.Matrix4x4 transform,
        out WpfReplayRect transformedBounds)
    {
        transformedBounds = default;
        var x0 = (bounds.X * transform.M11) + transform.M41;
        var x1 = ((bounds.X + bounds.Width) * transform.M11) + transform.M41;
        var y0 = (bounds.Y * transform.M22) + transform.M42;
        var y1 = ((bounds.Y + bounds.Height) * transform.M22) + transform.M42;
        var left = Math.Min(x0, x1);
        var top = Math.Min(y0, y1);
        var right = Math.Max(x0, x1);
        var bottom = Math.Max(y0, y1);
        var width = right - left;
        var height = bottom - top;
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        transformedBounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool TryGetRectanglePathBounds(
        MediaPathGeometry pathGeometry,
        bool requireFilled,
        bool requireStrokedSegments,
        out WpfReplayRect bounds)
    {
        bounds = default;
        if (pathGeometry.Figures.Count != 1)
        {
            return false;
        }

        var figure = pathGeometry.Figures[0];
        if (!figure.IsClosed || (requireFilled && !figure.IsFilled))
        {
            return false;
        }

        var segmentCount = figure.Segments.Count;
        if (segmentCount is not (3 or 4))
        {
            return false;
        }

        var point0 = figure.StartPoint;
        if (figure.Segments[0] is not MediaLineSegment lineSegment0
            || (requireStrokedSegments && !lineSegment0.IsStroked)
            || figure.Segments[1] is not MediaLineSegment lineSegment1
            || (requireStrokedSegments && !lineSegment1.IsStroked)
            || figure.Segments[2] is not MediaLineSegment lineSegment2
            || (requireStrokedSegments && !lineSegment2.IsStroked))
        {
            return false;
        }

        var point1 = lineSegment0.Point;
        var point2 = lineSegment1.Point;
        var point3 = lineSegment2.Point;
        if (segmentCount == 4)
        {
            if (figure.Segments[3] is not MediaLineSegment closingSegment
                || (requireStrokedSegments && !closingSegment.IsStroked)
                || !NearlyEqual(closingSegment.Point.X, point0.X)
                || !NearlyEqual(closingSegment.Point.Y, point0.Y))
            {
                return false;
            }
        }

        return TryCreateRectangleFromPolygon(point0, point1, point2, point3, out bounds);
    }

    private static bool TryCreateRectangleFromPolygon(
        Point point0,
        Point point1,
        Point point2,
        Point point3,
        out WpfReplayRect bounds)
    {
        bounds = default;
        var left = Math.Min(Math.Min(point0.X, point1.X), Math.Min(point2.X, point3.X));
        var top = Math.Min(Math.Min(point0.Y, point1.Y), Math.Min(point2.Y, point3.Y));
        var right = Math.Max(Math.Max(point0.X, point1.X), Math.Max(point2.X, point3.X));
        var bottom = Math.Max(Math.Max(point0.Y, point1.Y), Math.Max(point2.Y, point3.Y));

        var width = right - left;
        var height = bottom - top;
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        var hasTopLeft = false;
        var hasTopRight = false;
        var hasBottomRight = false;
        var hasBottomLeft = false;
        if (!TryMarkRectangleCorner(point0, left, top, right, bottom, ref hasTopLeft, ref hasTopRight, ref hasBottomRight, ref hasBottomLeft)
            || !TryMarkRectangleCorner(point1, left, top, right, bottom, ref hasTopLeft, ref hasTopRight, ref hasBottomRight, ref hasBottomLeft)
            || !TryMarkRectangleCorner(point2, left, top, right, bottom, ref hasTopLeft, ref hasTopRight, ref hasBottomRight, ref hasBottomLeft)
            || !TryMarkRectangleCorner(point3, left, top, right, bottom, ref hasTopLeft, ref hasTopRight, ref hasBottomRight, ref hasBottomLeft)
            || !IsAxisAlignedRectangleEdge(point0, point1)
            || !IsAxisAlignedRectangleEdge(point1, point2)
            || !IsAxisAlignedRectangleEdge(point2, point3)
            || !IsAxisAlignedRectangleEdge(point3, point0))
        {
            return false;
        }

        if (hasTopLeft && hasTopRight && hasBottomRight && hasBottomLeft)
        {
            bounds = new WpfReplayRect(left, top, width, height);
            return true;
        }

        return false;
    }

    private static bool TryMarkRectangleCorner(
        Point point,
        double left,
        double top,
        double right,
        double bottom,
        ref bool hasTopLeft,
        ref bool hasTopRight,
        ref bool hasBottomRight,
        ref bool hasBottomLeft)
    {
        var isLeft = NearlyEqual(point.X, left);
        var isRight = NearlyEqual(point.X, right);
        var isTop = NearlyEqual(point.Y, top);
        var isBottom = NearlyEqual(point.Y, bottom);
        var isOnVerticalEdge = isLeft || isRight;
        var isOnHorizontalEdge = isTop || isBottom;
        if (!isOnVerticalEdge || !isOnHorizontalEdge)
        {
            return false;
        }

        if (isLeft && isTop)
        {
            if (hasTopLeft)
            {
                return false;
            }

            hasTopLeft = true;
            return true;
        }

        if (isRight && isTop)
        {
            if (hasTopRight)
            {
                return false;
            }

            hasTopRight = true;
            return true;
        }

        if (isRight && isBottom)
        {
            if (hasBottomRight)
            {
                return false;
            }

            hasBottomRight = true;
            return true;
        }

        if (isLeft && isBottom)
        {
            if (hasBottomLeft)
            {
                return false;
            }

            hasBottomLeft = true;
            return true;
        }

        return false;
    }

    private static bool IsAxisAlignedRectangleEdge(Point point, Point next)
    {
        var sameX = NearlyEqual(point.X, next.X);
        var sameY = NearlyEqual(point.Y, next.Y);
        return sameX != sameY;
    }

    private static bool TryCreateUsableRect(Rect rect, out WpfReplayRect bounds)
    {
        bounds = new WpfReplayRect(rect.X, rect.Y, rect.Width, rect.Height);
        return !rect.IsEmpty
            && double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }
}
