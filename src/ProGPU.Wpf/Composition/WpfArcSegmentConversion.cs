using System;
using System.Collections.Generic;
using System.Numerics;

namespace System.Windows.Media.ProGPU.Composition;

internal readonly struct WpfCubicBezierSegmentData
{
    public WpfCubicBezierSegmentData(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 point)
    {
        ControlPoint1 = controlPoint1;
        ControlPoint2 = controlPoint2;
        Point = point;
    }

    public Vector2 ControlPoint1 { get; }

    public Vector2 ControlPoint2 { get; }

    public Vector2 Point { get; }
}

internal static class WpfArcSegmentConversion
{
    private const float TransformEpsilon = 0.0001f;

    public static bool TryAppendTransformedCubics(
        ICollection<WpfCubicBezierSegmentData> target,
        Vector2 startPoint,
        Vector2 endPoint,
        Vector2 size,
        float rotationAngle,
        bool isLargeArc,
        SweepDirection sweepDirection,
        Matrix4x4 transform)
    {
        if (NearlyEqual(startPoint.X, endPoint.X) && NearlyEqual(startPoint.Y, endPoint.Y))
        {
            return true;
        }

        var rx = MathF.Abs(size.X);
        var ry = MathF.Abs(size.Y);
        if (rx <= TransformEpsilon || ry <= TransformEpsilon)
        {
            return false;
        }

        var phi = rotationAngle * MathF.PI / 180f;
        var cosPhi = MathF.Cos(phi);
        var sinPhi = MathF.Sin(phi);
        var dx = (startPoint.X - endPoint.X) / 2f;
        var dy = (startPoint.Y - endPoint.Y) / 2f;
        var x1Prime = cosPhi * dx + sinPhi * dy;
        var y1Prime = -sinPhi * dx + cosPhi * dy;

        var rxSq = rx * rx;
        var rySq = ry * ry;
        var x1PrimeSq = x1Prime * x1Prime;
        var y1PrimeSq = y1Prime * y1Prime;
        var radiusCheck = x1PrimeSq / rxSq + y1PrimeSq / rySq;
        if (radiusCheck > 1f)
        {
            var scale = MathF.Sqrt(radiusCheck);
            rx *= scale;
            ry *= scale;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        var denominator = rxSq * y1PrimeSq + rySq * x1PrimeSq;
        if (denominator <= TransformEpsilon)
        {
            return false;
        }

        var numerator = rxSq * rySq - rxSq * y1PrimeSq - rySq * x1PrimeSq;
        var sign = isLargeArc == (sweepDirection == SweepDirection.Clockwise) ? -1f : 1f;
        var coefficient = sign * MathF.Sqrt(MathF.Max(0f, numerator / denominator));
        var cxPrime = coefficient * rx * y1Prime / ry;
        var cyPrime = -coefficient * ry * x1Prime / rx;
        var centerX = cosPhi * cxPrime - sinPhi * cyPrime + (startPoint.X + endPoint.X) / 2f;
        var centerY = sinPhi * cxPrime + cosPhi * cyPrime + (startPoint.Y + endPoint.Y) / 2f;

        var startVector = new Vector2((x1Prime - cxPrime) / rx, (y1Prime - cyPrime) / ry);
        var endVector = new Vector2((-x1Prime - cxPrime) / rx, (-y1Prime - cyPrime) / ry);
        var theta1 = CalculateVectorAngle(new Vector2(1f, 0f), startVector);
        var deltaTheta = CalculateVectorAngle(startVector, endVector);

        if (sweepDirection != SweepDirection.Clockwise && deltaTheta > 0f)
        {
            deltaTheta -= MathF.PI * 2f;
        }
        else if (sweepDirection == SweepDirection.Clockwise && deltaTheta < 0f)
        {
            deltaTheta += MathF.PI * 2f;
        }

        var segmentCount = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(deltaTheta) / (MathF.PI / 2f)));
        var segmentDelta = deltaTheta / segmentCount;

        for (var i = 0; i < segmentCount; i++)
        {
            var nextTheta = theta1 + segmentDelta;
            var alpha = 4f / 3f * MathF.Tan((nextTheta - theta1) / 4f);

            var cosTheta1 = MathF.Cos(theta1);
            var sinTheta1 = MathF.Sin(theta1);
            var cosTheta2 = MathF.Cos(nextTheta);
            var sinTheta2 = MathF.Sin(nextTheta);

            var control1 = TransformArcPoint(
                centerX,
                centerY,
                rx,
                ry,
                cosPhi,
                sinPhi,
                new Vector2(cosTheta1 - alpha * sinTheta1, sinTheta1 + alpha * cosTheta1),
                transform);
            var control2 = TransformArcPoint(
                centerX,
                centerY,
                rx,
                ry,
                cosPhi,
                sinPhi,
                new Vector2(cosTheta2 + alpha * sinTheta2, sinTheta2 - alpha * cosTheta2),
                transform);
            var point = i == segmentCount - 1
                ? Vector2.Transform(endPoint, transform)
                : TransformArcPoint(
                    centerX,
                    centerY,
                    rx,
                    ry,
                    cosPhi,
                    sinPhi,
                    new Vector2(cosTheta2, sinTheta2),
                    transform);

            target.Add(new WpfCubicBezierSegmentData(control1, control2, point));
            theta1 = nextTheta;
        }

        return true;
    }

    private static Vector2 TransformArcPoint(
        float centerX,
        float centerY,
        float rx,
        float ry,
        float cosPhi,
        float sinPhi,
        Vector2 unitPoint,
        Matrix4x4 transform)
    {
        var scaledX = rx * unitPoint.X;
        var scaledY = ry * unitPoint.Y;
        var point = new Vector2(
            centerX + cosPhi * scaledX - sinPhi * scaledY,
            centerY + sinPhi * scaledX + cosPhi * scaledY);

        return Vector2.Transform(point, transform);
    }

    private static float CalculateVectorAngle(Vector2 from, Vector2 to)
    {
        var lengthProduct = from.Length() * to.Length();
        if (lengthProduct <= TransformEpsilon)
        {
            return 0f;
        }

        var ratio = Vector2.Dot(from, to) / lengthProduct;
        if (ratio < -1f)
        {
            ratio = -1f;
        }
        else if (ratio > 1f)
        {
            ratio = 1f;
        }

        var angle = MathF.Acos(ratio);
        return from.X * to.Y - from.Y * to.X < 0f ? -angle : angle;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) <= TransformEpsilon;
    }
}
