using System;
using System.Numerics;
using ProGPU.Wpf.Interop;
using VectorArcSegment = ProGPU.Vector.ArcSegment;
using VectorArcSegmentGeometry = ProGPU.Vector.ArcSegmentGeometry;
using VectorSweepDirection = ProGPU.Vector.SweepDirection;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortablePathBoundsReader
{
    public static bool TryGetLocalPathBounds(
        PortableGeometryPath geometry,
        out WpfReplayRect bounds)
    {
        return TryGetPathBoundsCore(geometry, out bounds);
    }

    public static bool TryGetPathBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (geometry.Transform.IsIdentity)
        {
            return TryGetPathBoundsCore(geometry, out bounds);
        }

        if (TryGetAxisAlignedTransform(geometry.Transform, out var transform)
            && TryGetPathBoundsCore(geometry, out var localBounds)
            && TryTransformAxisAlignedBounds(localBounds, transform, out bounds))
        {
            return true;
        }

        return WpfPortablePathGeometryConverter.TryGetNativePathBounds(geometry, out bounds);
    }

    private static bool TryGetPathBoundsCore(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        if (geometry.Kind == PortableGeometryPathKind.Combined)
        {
            return TryGetCombinedBounds(geometry, out bounds);
        }

        if (geometry.Kind != PortableGeometryPathKind.Path
            || geometry.Figures.Length == 0)
        {
            return false;
        }

        var hasPoint = false;
        var left = 0.0;
        var top = 0.0;
        var right = 0.0;
        var bottom = 0.0;

        var figures = geometry.Figures;
        for (var figureIndex = 0; figureIndex < figures.Length; figureIndex++)
        {
            var figure = figures[figureIndex];
            if (figure.Segments.Length == 0)
            {
                return false;
            }

            if (!TryIncludePoint(figure.StartPoint, ref hasPoint, ref left, ref top, ref right, ref bottom))
            {
                return false;
            }

            var current = figure.StartPoint;
            var segments = figure.Segments;
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                if (!segment.IsStroked && !figure.IsFilled)
                {
                    return false;
                }

                switch (segment.Kind)
                {
                    case PortablePathSegmentKind.Line:
                        if (!TryIncludePoint(segment.Point1, ref hasPoint, ref left, ref top, ref right, ref bottom))
                        {
                            return false;
                        }

                        current = segment.Point1;
                        break;

                    case PortablePathSegmentKind.QuadraticBezier:
                        if (!TryIncludeQuadraticBezier(
                                current,
                                segment.Point1,
                                segment.Point2,
                                ref hasPoint,
                                ref left,
                                ref top,
                                ref right,
                                ref bottom))
                        {
                            return false;
                        }

                        current = segment.Point2;
                        break;

                    case PortablePathSegmentKind.CubicBezier:
                        if (!TryIncludeCubicBezier(
                                current,
                                segment.Point1,
                                segment.Point2,
                                segment.Point3,
                                ref hasPoint,
                                ref left,
                                ref top,
                                ref right,
                                ref bottom))
                        {
                            return false;
                        }

                        current = segment.Point3;
                        break;

                    case PortablePathSegmentKind.Arc:
                        if (!TryIncludeArcSegment(
                                current,
                                segment,
                                ref hasPoint,
                                ref left,
                                ref top,
                                ref right,
                                ref bottom))
                        {
                            return false;
                        }

                        current = segment.Point1;
                        break;

                    default:
                        return false;
                }
            }
        }

        var width = right - left;
        var height = bottom - top;
        if (!hasPoint
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || (width == 0 && height == 0))
        {
            return false;
        }

        bounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool TryGetAxisAlignedTransform(PortableMatrix3x2 transform, out PortableMatrix3x2 axisAlignedTransform)
    {
        axisAlignedTransform = transform;
        return transform.M12 == 0.0
            && transform.M21 == 0.0
            && double.IsFinite(transform.M11)
            && double.IsFinite(transform.M22)
            && double.IsFinite(transform.OffsetX)
            && double.IsFinite(transform.OffsetY);
    }

    private static bool TryTransformAxisAlignedBounds(
        WpfReplayRect bounds,
        PortableMatrix3x2 transform,
        out WpfReplayRect transformedBounds)
    {
        transformedBounds = default;
        if (!IsUsableBounds(bounds))
        {
            return false;
        }

        var x0 = (bounds.X * transform.M11) + transform.OffsetX;
        var x1 = ((bounds.X + bounds.Width) * transform.M11) + transform.OffsetX;
        var y0 = (bounds.Y * transform.M22) + transform.OffsetY;
        var y1 = ((bounds.Y + bounds.Height) * transform.M22) + transform.OffsetY;
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
            || width < 0
            || height < 0
            || (width == 0 && height == 0))
        {
            return false;
        }

        transformedBounds = new WpfReplayRect(left, top, width, height);
        return true;
    }

    private static bool IsUsableBounds(WpfReplayRect bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width >= 0
            && bounds.Height >= 0
            && (bounds.Width != 0 || bounds.Height != 0);
    }

    private static bool TryGetCombinedBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        bounds = default;
        var boundsA = default(WpfReplayRect);
        var boundsB = default(WpfReplayRect);
        var hasA = geometry.PathA != null && TryGetPathBounds(geometry.PathA, out boundsA);
        var hasB = geometry.PathB != null && TryGetPathBounds(geometry.PathB, out boundsB);

        switch (geometry.CombineOperation)
        {
            case 1:
                if (!hasA || !hasB)
                {
                    return false;
                }

                return TryIntersectBounds(boundsA, boundsB, out bounds);

            case 0:
                if (hasA)
                {
                    bounds = boundsA;
                    return true;
                }

                return false;

            case 4:
                if (hasB)
                {
                    bounds = boundsB;
                    return true;
                }

                return false;

            default:
                if (hasA && hasB)
                {
                    bounds = UnionBounds(boundsA, boundsB);
                    return true;
                }

                if (hasA)
                {
                    bounds = boundsA;
                    return true;
                }

                if (hasB)
                {
                    bounds = boundsB;
                    return true;
                }

                return false;
        }
    }

    private static WpfReplayRect UnionBounds(WpfReplayRect left, WpfReplayRect right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Max(left.Y + left.Height, right.Y + right.Height);
        return new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static bool TryIntersectBounds(WpfReplayRect left, WpfReplayRect right, out WpfReplayRect bounds)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);
        if (x2 < x1 || y2 < y1)
        {
            bounds = default;
            return false;
        }

        bounds = new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
        return true;
    }

    private static bool TryIncludeArcSegment(
        PortablePoint start,
        PortablePathSegment segment,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (!TryCreateVector2(start, out var vectorStart)
            || !TryCreateVector2(segment.Point1, out var vectorEnd)
            || !TryCreateVector2(segment.Size, out var vectorSize)
            || !float.IsFinite((float)segment.RotationAngle))
        {
            return false;
        }

        var arc = new VectorArcSegment(
            vectorEnd,
            vectorSize,
            (float)segment.RotationAngle,
            segment.IsLargeArc,
            ToVectorSweepDirection(segment.SweepDirection),
            segment.IsSmoothJoin,
            segment.IsStroked);
        if (!VectorArcSegmentGeometry.TryGetArcBounds(vectorStart, arc, out var min, out var max))
        {
            return TryIncludePoint(segment.Point1, ref hasPoint, ref left, ref top, ref right, ref bottom);
        }

        return TryIncludePoint(new PortablePoint(min.X, min.Y), ref hasPoint, ref left, ref top, ref right, ref bottom)
            && TryIncludePoint(new PortablePoint(max.X, max.Y), ref hasPoint, ref left, ref top, ref right, ref bottom);
    }

    private static bool TryIncludeQuadraticBezier(
        PortablePoint start,
        PortablePoint control,
        PortablePoint end,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (!TryIncludePoint(end, ref hasPoint, ref left, ref top, ref right, ref bottom))
        {
            return false;
        }

        return TryIncludeQuadraticExtremum(
                start.X,
                control.X,
                end.X,
                start,
                control,
                end,
                ref hasPoint,
                ref left,
                ref top,
                ref right,
                ref bottom)
            && TryIncludeQuadraticExtremum(
                start.Y,
                control.Y,
                end.Y,
                start,
                control,
                end,
                ref hasPoint,
                ref left,
                ref top,
                ref right,
                ref bottom);
    }

    private static bool TryIncludeCubicBezier(
        PortablePoint start,
        PortablePoint control1,
        PortablePoint control2,
        PortablePoint end,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (!TryIncludePoint(end, ref hasPoint, ref left, ref top, ref right, ref bottom))
        {
            return false;
        }

        return TryIncludeCubicExtrema(
                start.X,
                control1.X,
                control2.X,
                end.X,
                start,
                control1,
                control2,
                end,
                ref hasPoint,
                ref left,
                ref top,
                ref right,
                ref bottom)
            && TryIncludeCubicExtrema(
                start.Y,
                control1.Y,
                control2.Y,
                end.Y,
                start,
                control1,
                control2,
                end,
                ref hasPoint,
                ref left,
                ref top,
                ref right,
                ref bottom);
    }

    private static bool TryIncludeQuadraticExtremum(
        double start,
        double control,
        double end,
        PortablePoint curveStart,
        PortablePoint curveControl,
        PortablePoint curveEnd,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        var denominator = start - (2 * control) + end;
        if (denominator == 0.0)
        {
            return true;
        }

        var t = (start - control) / denominator;
        if (t <= 0.0 || t >= 1.0)
        {
            return true;
        }

        return TryIncludePoint(
            EvaluateQuadratic(curveStart, curveControl, curveEnd, t),
            ref hasPoint,
            ref left,
            ref top,
            ref right,
            ref bottom);
    }

    private static bool TryIncludeCubicExtrema(
        double start,
        double control1,
        double control2,
        double end,
        PortablePoint curveStart,
        PortablePoint curveControl1,
        PortablePoint curveControl2,
        PortablePoint curveEnd,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        var a = -start + (3 * control1) - (3 * control2) + end;
        var b = 2 * (start - (2 * control1) + control2);
        var c = control1 - start;

        if (a == 0.0)
        {
            if (b == 0.0)
            {
                return true;
            }

            return TryIncludeCubicExtremum(-c / b, curveStart, curveControl1, curveControl2, curveEnd, ref hasPoint, ref left, ref top, ref right, ref bottom);
        }

        var discriminant = (b * b) - (4 * a * c);
        if (discriminant < 0.0)
        {
            return true;
        }

        var root = Math.Sqrt(discriminant);
        return TryIncludeCubicExtremum((-b + root) / (2 * a), curveStart, curveControl1, curveControl2, curveEnd, ref hasPoint, ref left, ref top, ref right, ref bottom)
            && TryIncludeCubicExtremum((-b - root) / (2 * a), curveStart, curveControl1, curveControl2, curveEnd, ref hasPoint, ref left, ref top, ref right, ref bottom);
    }

    private static bool TryIncludeCubicExtremum(
        double t,
        PortablePoint start,
        PortablePoint control1,
        PortablePoint control2,
        PortablePoint end,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (t <= 0.0 || t >= 1.0)
        {
            return true;
        }

        return TryIncludePoint(
            EvaluateCubic(start, control1, control2, end, t),
            ref hasPoint,
            ref left,
            ref top,
            ref right,
            ref bottom);
    }

    private static PortablePoint EvaluateQuadratic(PortablePoint start, PortablePoint control, PortablePoint end, double t)
    {
        var u = 1.0 - t;
        var x = (u * u * start.X) + (2 * u * t * control.X) + (t * t * end.X);
        var y = (u * u * start.Y) + (2 * u * t * control.Y) + (t * t * end.Y);
        return new PortablePoint(x, y);
    }

    private static PortablePoint EvaluateCubic(
        PortablePoint start,
        PortablePoint control1,
        PortablePoint control2,
        PortablePoint end,
        double t)
    {
        var u = 1.0 - t;
        var x = (u * u * u * start.X)
            + (3 * u * u * t * control1.X)
            + (3 * u * t * t * control2.X)
            + (t * t * t * end.X);
        var y = (u * u * u * start.Y)
            + (3 * u * u * t * control1.Y)
            + (3 * u * t * t * control2.Y)
            + (t * t * t * end.Y);
        return new PortablePoint(x, y);
    }

    private static bool TryCreateVector2(PortablePoint point, out Vector2 vector)
    {
        vector = new Vector2((float)point.X, (float)point.Y);
        return double.IsFinite(point.X)
            && double.IsFinite(point.Y)
            && float.IsFinite(vector.X)
            && float.IsFinite(vector.Y);
    }

    private static bool TryCreateVector2(PortableSize size, out Vector2 vector)
    {
        vector = new Vector2((float)size.Width, (float)size.Height);
        return double.IsFinite(size.Width)
            && double.IsFinite(size.Height)
            && float.IsFinite(vector.X)
            && float.IsFinite(vector.Y);
    }

    private static VectorSweepDirection ToVectorSweepDirection(PortableSweepDirection sweepDirection)
    {
        return sweepDirection == PortableSweepDirection.Clockwise
            ? VectorSweepDirection.Clockwise
            : VectorSweepDirection.Counterclockwise;
    }

    private static bool TryIncludePoint(
        PortablePoint point,
        ref bool hasPoint,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        if (!hasPoint)
        {
            left = point.X;
            top = point.Y;
            right = point.X;
            bottom = point.Y;
            hasPoint = true;
            return true;
        }

        left = Math.Min(left, point.X);
        top = Math.Min(top, point.Y);
        right = Math.Max(right, point.X);
        bottom = Math.Max(bottom, point.Y);
        return true;
    }
}
