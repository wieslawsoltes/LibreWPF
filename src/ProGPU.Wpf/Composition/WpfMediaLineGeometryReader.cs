using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaLineGeometry = System.Windows.Media.LineGeometry;
using MediaLineSegment = System.Windows.Media.LineSegment;
using MediaPathGeometry = System.Windows.Media.PathGeometry;
using MediaPathSegmentCollection = System.Windows.Media.PathSegmentCollection;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfMediaLineGeometryReader
{
    private static readonly ConditionalWeakTable<MediaGeometry, GeometryPrimitiveCache> s_primitiveCache = new();

    public static bool TryGetLinePoints(
        MediaGeometry geometry,
        out Point startPoint,
        out Point endPoint)
    {
        if (s_primitiveCache.TryGetValue(geometry, out var cache)
            && cache.TryGetLinePoints(geometry, out startPoint, out endPoint))
        {
            return true;
        }

        if (!TryComputeLinePoints(geometry, out var primitive, out startPoint, out endPoint))
        {
            return false;
        }

        s_primitiveCache.GetOrCreateValue(geometry).SetLinePoints(primitive);
        return true;
    }

    private static bool TryComputeLinePoints(
        MediaGeometry geometry,
        out LinePrimitive primitive,
        out Point startPoint,
        out Point endPoint)
    {
        if (!TryReadLinePrimitiveFingerprint(geometry, out var fingerprint)
            || !TryTransformPoint(fingerprint.StartPoint, fingerprint.Transform, out startPoint)
            || !TryTransformPoint(fingerprint.EndPoint, fingerprint.Transform, out endPoint))
        {
            primitive = default;
            startPoint = default;
            endPoint = default;
            return false;
        }

        primitive = new LinePrimitive(
            fingerprint.StartPoint,
            fingerprint.EndPoint,
            fingerprint.Transform,
            new WpfReplayLineSegment(
                new WpfReplayPoint(startPoint.X, startPoint.Y),
                new WpfReplayPoint(endPoint.X, endPoint.Y)));
        return true;
    }

    private static bool TryReadLinePrimitiveFingerprint(
        MediaGeometry geometry,
        out LinePrimitiveFingerprint fingerprint)
    {
        if (!TryGetGeometryTransform(geometry, out var transform))
        {
            fingerprint = default;
            return false;
        }

        if (geometry is MediaLineGeometry lineGeometry
            && IsUsablePoint(lineGeometry.StartPoint, out var startPoint)
            && IsUsablePoint(lineGeometry.EndPoint, out var endPoint))
        {
            fingerprint = new LinePrimitiveFingerprint(startPoint, endPoint, transform);
            return true;
        }

        if (geometry is MediaPathGeometry pathGeometry)
        {
            return TryReadPathLinePrimitiveFingerprint(pathGeometry, transform, out fingerprint);
        }

        fingerprint = default;
        return false;
    }

    public static bool TryGetPolylineSegments(
        MediaGeometry geometry,
        out IReadOnlyList<WpfReplayLineSegment> segments)
    {
        if (s_primitiveCache.TryGetValue(geometry, out var cache)
            && cache.TryGetPolylineSegments(geometry, out segments))
        {
            return true;
        }

        if (!TryGetGeometryTransform(geometry, out var transform)
            || geometry is not MediaPathGeometry pathGeometry)
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        if (!TryGetPathPolylineSegments(pathGeometry, transform, out var primitive, out segments))
        {
            return false;
        }

        s_primitiveCache.GetOrCreateValue(geometry).SetPolylineSegments(primitive);

        return true;
    }

    private static bool TryReadPathLinePrimitiveFingerprint(
        MediaPathGeometry pathGeometry,
        Matrix4x4 transform,
        out LinePrimitiveFingerprint fingerprint)
    {
        if (pathGeometry.Figures.Count != 1)
        {
            fingerprint = default;
            return false;
        }

        var figure = pathGeometry.Figures[0];
        if (figure.IsClosed || figure.Segments.Count != 1)
        {
            fingerprint = default;
            return false;
        }

        if (figure.Segments[0] is MediaLineSegment lineSegment
            && lineSegment.IsStroked
            && IsUsablePoint(figure.StartPoint, out var startPoint)
            && IsUsablePoint(lineSegment.Point, out var endPoint))
        {
            fingerprint = new LinePrimitiveFingerprint(startPoint, endPoint, transform);
            return true;
        }

        fingerprint = default;
        return false;
    }

    private static bool TryGetPathPolylineSegments(
        MediaPathGeometry pathGeometry,
        Matrix4x4 transform,
        out PolylinePrimitive primitive,
        out IReadOnlyList<WpfReplayLineSegment> segments)
    {
        primitive = default;
        if (pathGeometry.Figures.Count != 1)
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        var figure = pathGeometry.Figures[0];
        var segmentCount = figure.Segments.Count;
        if (segmentCount < 2)
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        if (figure.IsClosed
            && WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(pathGeometry, out _))
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        if (!IsUsablePoint(figure.StartPoint, out var startPoint)
            || !TryReadPolylineSegmentPoints(figure.Segments, out var rawSegmentPoints))
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        var closesToStart = figure.IsClosed && SamePoint(rawSegmentPoints[^1], startPoint);
        var lineSegments = new WpfReplayLineSegment[segmentCount + (figure.IsClosed && !closesToStart ? 1 : 0)];
        if (!TryTransformPoint(startPoint, transform, out var transformedStartPoint))
        {
            segments = Array.Empty<WpfReplayLineSegment>();
            return false;
        }

        var currentPoint = transformedStartPoint;
        var writtenSegmentCount = 0;
        for (var i = 0; i < segmentCount; i++)
        {
            if (!TryTransformPoint(rawSegmentPoints[i], transform, out var nextPoint))
            {
                segments = Array.Empty<WpfReplayLineSegment>();
                return false;
            }

            lineSegments[writtenSegmentCount++] = new WpfReplayLineSegment(
                new WpfReplayPoint(currentPoint.X, currentPoint.Y),
                new WpfReplayPoint(nextPoint.X, nextPoint.Y));
            currentPoint = nextPoint;
        }

        if (figure.IsClosed && !closesToStart)
        {
            lineSegments[writtenSegmentCount++] = new WpfReplayLineSegment(
                new WpfReplayPoint(currentPoint.X, currentPoint.Y),
                new WpfReplayPoint(transformedStartPoint.X, transformedStartPoint.Y));
        }

        primitive = new PolylinePrimitive(startPoint, rawSegmentPoints, figure.IsClosed, transform, lineSegments);
        segments = lineSegments;
        return true;
    }

    private static bool TryReadPolylineSegmentPoints(
        MediaPathSegmentCollection segments,
        out Point[] points)
    {
        var segmentCount = segments.Count;
        points = new Point[segmentCount];
        for (var i = 0; i < segmentCount; i++)
        {
            if (segments[i] is not MediaLineSegment lineSegment
                || !lineSegment.IsStroked
                || !IsUsablePoint(lineSegment.Point, out var point))
            {
                points = Array.Empty<Point>();
                return false;
            }

            points[i] = point;
        }

        return true;
    }

    private static bool TryGetGeometryTransform(MediaGeometry geometry, out Matrix4x4 transform)
    {
        var transformValue = geometry.Transform;
        if (transformValue == null)
        {
            transform = Matrix4x4.Identity;
            return true;
        }

        return WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform);
    }

    private static bool IsUsablePoint(Point point, out Point usablePoint)
    {
        usablePoint = point;
        return double.IsFinite(point.X)
            && double.IsFinite(point.Y);
    }

    private static bool TryTransformPoint(Point point, Matrix4x4 transform, out Point transformedPoint)
    {
        var x = (point.X * transform.M11) + (point.Y * transform.M21) + transform.M41;
        var y = (point.X * transform.M12) + (point.Y * transform.M22) + transform.M42;
        transformedPoint = new Point(x, y);
        return double.IsFinite(x)
            && double.IsFinite(y);
    }

    private static bool SamePoint(Point left, Point right)
    {
        return left.X == right.X
            && left.Y == right.Y;
    }

    private static bool SamePoint(WpfReplayPoint left, Point right)
    {
        return left.X == right.X
            && left.Y == right.Y;
    }

    private static bool SameTransform(Matrix4x4 left, Matrix4x4 right)
    {
        return left.Equals(right);
    }

    private sealed class GeometryPrimitiveCache
    {
        private bool _hasLineSegment;
        private Point _lineStartPoint;
        private Point _lineEndPoint;
        private Matrix4x4 _lineTransform;
        private WpfReplayLineSegment _lineSegment;
        private Point _polylineStartPoint;
        private Point[]? _polylineSegmentPoints;
        private bool _polylineIsClosed;
        private Matrix4x4 _polylineTransform;
        private WpfReplayLineSegment[]? _polylineSegments;

        public void SetLinePoints(LinePrimitive primitive)
        {
            _hasLineSegment = true;
            _lineStartPoint = primitive.StartPoint;
            _lineEndPoint = primitive.EndPoint;
            _lineTransform = primitive.Transform;
            _lineSegment = primitive.Segment;
        }

        public void SetPolylineSegments(PolylinePrimitive primitive)
        {
            _polylineStartPoint = primitive.StartPoint;
            _polylineSegmentPoints = primitive.SegmentPoints;
            _polylineIsClosed = primitive.IsClosed;
            _polylineTransform = primitive.Transform;
            _polylineSegments = primitive.Segments;
        }

        public bool TryGetLinePoints(
            MediaGeometry geometry,
            out Point startPoint,
            out Point endPoint)
        {
            if (!_hasLineSegment
                || !TryValidateLinePoints(geometry, this))
            {
                startPoint = default;
                endPoint = default;
                return false;
            }

            startPoint = new Point(_lineSegment.StartPoint.X, _lineSegment.StartPoint.Y);
            endPoint = new Point(_lineSegment.EndPoint.X, _lineSegment.EndPoint.Y);
            return true;
        }

        public bool TryGetPolylineSegments(
            MediaGeometry geometry,
            out IReadOnlyList<WpfReplayLineSegment> segments)
        {
            var cached = _polylineSegments;
            var cachedPoints = _polylineSegmentPoints;
            if (cached == null
                || cachedPoints == null
                || !TryValidatePolylineSegments(geometry, this, cached, cachedPoints))
            {
                segments = Array.Empty<WpfReplayLineSegment>();
                return false;
            }

            segments = cached;
            return true;
        }

        private static bool TryValidateLinePoints(
            MediaGeometry geometry,
            GeometryPrimitiveCache cache)
        {
            if (!TryReadLinePrimitiveFingerprint(geometry, out var fingerprint))
            {
                return false;
            }

            return SamePoint(cache._lineStartPoint, fingerprint.StartPoint)
                && SamePoint(cache._lineEndPoint, fingerprint.EndPoint)
                && SameTransform(cache._lineTransform, fingerprint.Transform);
        }

        private static bool TryValidatePolylineSegments(
            MediaGeometry geometry,
            GeometryPrimitiveCache cache,
            WpfReplayLineSegment[] cached,
            Point[] cachedPoints)
        {
            if (!TryGetGeometryTransform(geometry, out var transform)
                || geometry is not MediaPathGeometry pathGeometry
                || pathGeometry.Figures.Count != 1
                || !SameTransform(cache._polylineTransform, transform))
            {
                return false;
            }

            var figure = pathGeometry.Figures[0];
            var segmentCount = figure.Segments.Count;
            if (segmentCount < 2
                || segmentCount != cachedPoints.Length
                || figure.IsClosed != cache._polylineIsClosed)
            {
                return false;
            }

            if (figure.IsClosed
                && WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(pathGeometry, out _))
            {
                return false;
            }

            if (!IsUsablePoint(figure.StartPoint, out var startPoint)
                || !SamePoint(cache._polylineStartPoint, startPoint))
            {
                return false;
            }

            for (var i = 0; i < segmentCount; i++)
            {
                if (figure.Segments[i] is not MediaLineSegment lineSegment
                    || !lineSegment.IsStroked
                    || !IsUsablePoint(lineSegment.Point, out var nextPoint)
                    || !SamePoint(cachedPoints[i], nextPoint))
                {
                    return false;
                }
            }

            var expectedSegmentCount = segmentCount
                + (figure.IsClosed && !SamePoint(cachedPoints[^1], startPoint) ? 1 : 0);
            return expectedSegmentCount == cached.Length;
        }
    }

    private readonly record struct LinePrimitive(
        Point StartPoint,
        Point EndPoint,
        Matrix4x4 Transform,
        WpfReplayLineSegment Segment);

    private readonly record struct LinePrimitiveFingerprint(
        Point StartPoint,
        Point EndPoint,
        Matrix4x4 Transform);

    private readonly record struct PolylinePrimitive(
        Point StartPoint,
        Point[] SegmentPoints,
        bool IsClosed,
        Matrix4x4 Transform,
        WpfReplayLineSegment[] Segments);
}

internal readonly record struct WpfReplayLineSegment(
    WpfReplayPoint StartPoint,
    WpfReplayPoint EndPoint);
