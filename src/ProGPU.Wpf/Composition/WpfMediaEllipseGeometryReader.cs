using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaEllipseGeometry = System.Windows.Media.EllipseGeometry;
using MediaGeometry = System.Windows.Media.Geometry;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfMediaEllipseGeometryReader
{
    public static bool TryGetEllipseGeometry(
        MediaGeometry geometry,
        out Point center,
        out double radiusX,
        out double radiusY)
    {
        if (geometry is not MediaEllipseGeometry ellipseGeometry
            || !TryGetAxisPreservingGeometryTransform(geometry, out var transform)
            || !IsUsablePoint(ellipseGeometry.Center, out var localCenter)
            || !IsPositiveRadius(ellipseGeometry.RadiusX, out var localRadiusX)
            || !IsPositiveRadius(ellipseGeometry.RadiusY, out var localRadiusY)
            || !TryTransformPoint(localCenter, transform, out center)
            || !TryTransformRadii(localRadiusX, localRadiusY, transform, out radiusX, out radiusY))
        {
            center = default;
            radiusX = default;
            radiusY = default;
            return false;
        }

        return true;
    }

    public static bool TryGetEllipseBounds(MediaGeometry geometry, out WpfReplayRect bounds)
    {
        if (TryGetEllipseGeometry(geometry, out var center, out var radiusX, out var radiusY))
        {
            bounds = new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2);
            return IsUsableBounds(bounds);
        }

        bounds = default;
        return false;
    }

    private static bool TryGetAxisPreservingGeometryTransform(MediaGeometry geometry, out Matrix4x4 transform)
    {
        var transformValue = geometry.Transform;
        if (transformValue == null)
        {
            transform = Matrix4x4.Identity;
            return true;
        }

        if (!WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
        {
            return false;
        }

        return IsAxisPreservingTransform(transform);
    }

    private static bool IsAxisPreservingTransform(Matrix4x4 transform)
    {
        return IsFinite2D(transform)
            && ((NearlyZero(transform.M12) && NearlyZero(transform.M21))
                || (NearlyZero(transform.M11) && NearlyZero(transform.M22)));
    }

    private static bool TryTransformPoint(Point point, Matrix4x4 transform, out Point transformedPoint)
    {
        var x = (point.X * transform.M11) + (point.Y * transform.M21) + transform.M41;
        var y = (point.X * transform.M12) + (point.Y * transform.M22) + transform.M42;
        transformedPoint = new Point(x, y);
        return double.IsFinite(x)
            && double.IsFinite(y);
    }

    private static bool TryTransformRadii(
        double radiusX,
        double radiusY,
        Matrix4x4 transform,
        out double transformedRadiusX,
        out double transformedRadiusY)
    {
        if (NearlyZero(transform.M12) && NearlyZero(transform.M21))
        {
            transformedRadiusX = Math.Abs(radiusX * transform.M11);
            transformedRadiusY = Math.Abs(radiusY * transform.M22);
        }
        else if (NearlyZero(transform.M11) && NearlyZero(transform.M22))
        {
            transformedRadiusX = Math.Abs(radiusY * transform.M21);
            transformedRadiusY = Math.Abs(radiusX * transform.M12);
        }
        else
        {
            transformedRadiusX = default;
            transformedRadiusY = default;
            return false;
        }

        return double.IsFinite(transformedRadiusX)
            && double.IsFinite(transformedRadiusY)
            && transformedRadiusX > 0
            && transformedRadiusY > 0;
    }

    private static bool IsUsablePoint(Point point, out Point usablePoint)
    {
        usablePoint = point;
        return double.IsFinite(point.X)
            && double.IsFinite(point.Y);
    }

    private static bool IsPositiveRadius(double radius, out double usableRadius)
    {
        usableRadius = radius;
        return double.IsFinite(radius)
            && radius > 0;
    }

    private static bool IsUsableBounds(WpfReplayRect bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static bool IsFinite2D(Matrix4x4 transform)
    {
        return float.IsFinite(transform.M11)
            && float.IsFinite(transform.M12)
            && float.IsFinite(transform.M21)
            && float.IsFinite(transform.M22)
            && float.IsFinite(transform.M41)
            && float.IsFinite(transform.M42);
    }

    private static bool NearlyZero(float value)
    {
        return Math.Abs(value) <= 0.000001f;
    }
}
