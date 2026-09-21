using System;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortableRectangleClipReader
{
    public static bool TryGetRectangleClipBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (!geometry.Transform.IsIdentity
            || geometry.Kind != PortableGeometryPathKind.Path
            || geometry.Figures.Length != 1)
        {
            return false;
        }

        var figure = geometry.Figures[0];
        if (!figure.IsClosed || !figure.IsFilled)
        {
            return false;
        }

        var segmentCount = figure.Segments.Length;
        if (segmentCount is not (3 or 4))
        {
            return false;
        }

        var point0 = figure.StartPoint;
        var segment0 = figure.Segments[0];
        var segment1 = figure.Segments[1];
        var segment2 = figure.Segments[2];
        if (segment0.Kind != PortablePathSegmentKind.Line
            || segment1.Kind != PortablePathSegmentKind.Line
            || segment2.Kind != PortablePathSegmentKind.Line)
        {
            return false;
        }

        var point1 = segment0.Point1;
        var point2 = segment1.Point1;
        var point3 = segment2.Point1;
        if (segmentCount == 4)
        {
            var segment = figure.Segments[3];
            if (segment.Kind != PortablePathSegmentKind.Line
                || !NearlyEqual(segment.Point1.X, point0.X)
                || !NearlyEqual(segment.Point1.Y, point0.Y))
            {
                return false;
            }
        }

        return TryCreateRectangleClipFromPolygon(point0, point1, point2, point3, out bounds);
    }

    private static bool TryCreateRectangleClipFromPolygon(
        PortablePoint point0,
        PortablePoint point1,
        PortablePoint point2,
        PortablePoint point3,
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
        PortablePoint point,
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

    private static bool IsAxisAlignedRectangleEdge(PortablePoint point, PortablePoint next)
    {
        var sameX = NearlyEqual(point.X, next.X);
        var sameY = NearlyEqual(point.Y, next.Y);
        return sameX != sameY;
    }

    private static bool NearlyEqual(double left, double right)
    {
        return Math.Abs(left - right) <= 0.000001;
    }
}
