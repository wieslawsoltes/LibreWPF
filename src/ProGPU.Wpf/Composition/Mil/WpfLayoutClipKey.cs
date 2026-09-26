using System;
using System.Runtime.CompilerServices;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

// Layout getters may return a fresh geometry on every read. Capture source values,
// not that temporary identity. Unknown/unavailable descriptors keep identity semantics.
internal readonly struct WpfLayoutClipKey : IEquatable<WpfLayoutClipKey>
{
    private readonly object? _reference;
    private readonly PortablePrimitiveGeometry _primitive;
    private readonly PathSnapshot? _path;
    private readonly bool _hasPrimitive;

    private WpfLayoutClipKey(object? reference)
    {
        _reference = reference;
    }

    private WpfLayoutClipKey(PortablePrimitiveGeometry primitive)
    {
        _primitive = primitive;
        _hasPrimitive = true;
    }

    private WpfLayoutClipKey(PathSnapshot path)
    {
        _path = path;
    }

    internal static WpfLayoutClipKey Capture(object? clip, WpfLayoutClipKey previous = default)
    {
        if (clip is IPortablePrimitiveGeometrySource primitiveSource
            && primitiveSource.TryGetPortablePrimitiveGeometry(out var primitive)
            && primitive.Kind is PortablePrimitiveGeometryKind.Line
                or PortablePrimitiveGeometryKind.Rectangle or PortablePrimitiveGeometryKind.Ellipse)
        {
            return new WpfLayoutClipKey(primitive);
        }

        if (clip is IPortableGeometryPathSource pathSource
            && pathSource.TryGetPortableGeometryPath(out var path)
            && PathSnapshot.IsSupported(path))
        {
            // Portable paths, figures and their arrays are mutable caller storage.
            // Reuse only our private copy, and only after comparing its actual values.
            if (previous._path is { } retained && retained.Matches(path))
            {
                return previous;
            }

            return new WpfLayoutClipKey(new PathSnapshot(path));
        }

        return new WpfLayoutClipKey(clip);
    }

    public bool Equals(WpfLayoutClipKey other)
    {
        if (_hasPrimitive != other._hasPrimitive)
        {
            return false;
        }

        if (_hasPrimitive)
        {
            return PrimitiveEquals(_primitive, other._primitive);
        }

        if (_path != null || other._path != null)
        {
            return _path != null && other._path != null
                && (ReferenceEquals(_path, other._path) || _path.Matches(other._path.Value));
        }

        return ReferenceEquals(_reference, other._reference);
    }

    public override bool Equals(object? obj) => obj is WpfLayoutClipKey other && Equals(other);

    public override int GetHashCode()
    {
        if (_hasPrimitive)
        {
            var hash = new HashCode();
            hash.Add(_primitive.Kind);
            AddPoint(ref hash, _primitive.Point1);
            AddPoint(ref hash, _primitive.Point2);
            AddRect(ref hash, _primitive.Rect);
            hash.Add(_primitive.RadiusX);
            hash.Add(_primitive.RadiusY);
            AddMatrix(ref hash, _primitive.Transform);
            return hash.ToHashCode();
        }

        return _path?.CachedHashCode ?? (_reference == null ? 0 : RuntimeHelpers.GetHashCode(_reference));
    }

    private static bool PrimitiveEquals(PortablePrimitiveGeometry left, PortablePrimitiveGeometry right) =>
        left.Kind == right.Kind
        && PointEquals(left.Point1, right.Point1)
        && PointEquals(left.Point2, right.Point2)
        && RectEquals(left.Rect, right.Rect)
        && left.RadiusX.Equals(right.RadiusX)
        && left.RadiusY.Equals(right.RadiusY)
        && MatrixEquals(left.Transform, right.Transform);

    private static bool PointEquals(PortablePoint left, PortablePoint right) =>
        left.X.Equals(right.X) && left.Y.Equals(right.Y);

    private static bool RectEquals(PortableRect left, PortableRect right) =>
        left.IsEmpty == right.IsEmpty && left.X.Equals(right.X) && left.Y.Equals(right.Y)
        && left.Width.Equals(right.Width) && left.Height.Equals(right.Height);

    private static bool MatrixEquals(PortableMatrix3x2 left, PortableMatrix3x2 right) =>
        left.M11.Equals(right.M11) && left.M12.Equals(right.M12)
        && left.M21.Equals(right.M21) && left.M22.Equals(right.M22)
        && left.OffsetX.Equals(right.OffsetX) && left.OffsetY.Equals(right.OffsetY);

    private static void AddPoint(ref HashCode hash, PortablePoint point)
    {
        hash.Add(point.X);
        hash.Add(point.Y);
    }

    private static void AddRect(ref HashCode hash, PortableRect rect)
    {
        hash.Add(rect.IsEmpty);
        hash.Add(rect.X);
        hash.Add(rect.Y);
        hash.Add(rect.Width);
        hash.Add(rect.Height);
    }

    private static void AddMatrix(ref HashCode hash, PortableMatrix3x2 matrix)
    {
        hash.Add(matrix.M11);
        hash.Add(matrix.M12);
        hash.Add(matrix.M21);
        hash.Add(matrix.M22);
        hash.Add(matrix.OffsetX);
        hash.Add(matrix.OffsetY);
    }

    private sealed class PathSnapshot
    {
        // These are snapshot limits, not rendering admission limits. Oversized,
        // cyclic or future-schema inputs retain the original reference policy.
        private const int MaximumDepth = 64;
        private const int MaximumItems = 65536;

        internal readonly PortableGeometryPath Value;
        internal readonly int CachedHashCode;

        internal PathSnapshot(PortableGeometryPath path)
        {
            Value = Copy(path);
            var hash = new HashCode();
            AddPath(ref hash, Value);
            CachedHashCode = hash.ToHashCode();
        }

        internal bool Matches(PortableGeometryPath path) => PathEquals(Value, path);

        internal static bool IsSupported(PortableGeometryPath? path)
        {
            int remaining = MaximumItems;
            return IsSupported(path, 0, ref remaining);
        }

        private static bool IsSupported(PortableGeometryPath? path, int depth, ref int remaining)
        {
            if (path == null || depth >= MaximumDepth || --remaining < 0
                || path.FillRule is not (PortableFillRule.EvenOdd or PortableFillRule.Nonzero)
                || path.Figures == null)
            {
                return false;
            }

            if (path.Kind == PortableGeometryPathKind.Combined)
            {
                return path.Figures.Length == 0 && path.CombineOperation is >= 0 and <= 3
                    && IsSupported(path.PathA, depth + 1, ref remaining)
                    && IsSupported(path.PathB, depth + 1, ref remaining);
            }

            if (path.Kind != PortableGeometryPathKind.Path || path.PathA != null || path.PathB != null
                || path.Figures.Length > remaining)
            {
                return false;
            }

            var figures = path.Figures;
            for (int i = 0; i < figures.Length; i++)
            {
                var figure = figures[i];
                if (--remaining < 0 || figure == null || figure.Segments == null
                    || figure.Segments.Length > remaining)
                {
                    return false;
                }

                var segments = figure.Segments;
                remaining -= segments.Length;
                for (int j = 0; j < segments.Length; j++)
                {
                    var segment = segments[j];
                    if (segment.Kind is not (PortablePathSegmentKind.Line or PortablePathSegmentKind.QuadraticBezier
                            or PortablePathSegmentKind.CubicBezier or PortablePathSegmentKind.Arc)
                        || segment.SweepDirection is not (PortableSweepDirection.Clockwise or PortableSweepDirection.Counterclockwise))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static PortableGeometryPath Copy(PortableGeometryPath path)
        {
            var figures = path.Figures;
            var copy = new PortableGeometryPath
            {
                Kind = path.Kind,
                FillRule = path.FillRule,
                Transform = path.Transform,
                Bounds = path.Bounds,
                CombineOperation = path.CombineOperation,
                Figures = figures.Length == 0 ? Array.Empty<PortablePathFigure>() : new PortablePathFigure[figures.Length],
                PathA = path.PathA == null ? null : Copy(path.PathA),
                PathB = path.PathB == null ? null : Copy(path.PathB)
            };
            for (int i = 0; i < figures.Length; i++)
            {
                var figure = figures[i];
                copy.Figures[i] = new PortablePathFigure
                {
                    StartPoint = figure.StartPoint,
                    IsClosed = figure.IsClosed,
                    IsFilled = figure.IsFilled,
                    Segments = figure.Segments.Length == 0
                        ? Array.Empty<PortablePathSegment>() : (PortablePathSegment[])figure.Segments.Clone()
                };
            }

            return copy;
        }

        private static bool PathEquals(PortableGeometryPath left, PortableGeometryPath right)
        {
            if (left.Kind != right.Kind || left.FillRule != right.FillRule
                || !MatrixEquals(left.Transform, right.Transform) || !RectEquals(left.Bounds, right.Bounds)
                || left.CombineOperation != right.CombineOperation || left.Figures.Length != right.Figures.Length)
            {
                return false;
            }

            if (left.Kind == PortableGeometryPathKind.Combined)
            {
                return PathEquals(left.PathA!, right.PathA!) && PathEquals(left.PathB!, right.PathB!);
            }

            var leftFigures = left.Figures;
            var rightFigures = right.Figures;
            for (int i = 0; i < leftFigures.Length; i++)
            {
                var first = leftFigures[i];
                var second = rightFigures[i];
                if (!PointEquals(first.StartPoint, second.StartPoint) || first.IsClosed != second.IsClosed
                    || first.IsFilled != second.IsFilled || first.Segments.Length != second.Segments.Length)
                {
                    return false;
                }

                var firstSegments = first.Segments;
                var secondSegments = second.Segments;
                for (int j = 0; j < firstSegments.Length; j++)
                {
                    if (!SegmentEquals(firstSegments[j], secondSegments[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SegmentEquals(PortablePathSegment left, PortablePathSegment right) =>
            left.Kind == right.Kind && PointEquals(left.Point1, right.Point1)
            && PointEquals(left.Point2, right.Point2) && PointEquals(left.Point3, right.Point3)
            && left.Size.Width.Equals(right.Size.Width) && left.Size.Height.Equals(right.Size.Height)
            && left.RotationAngle.Equals(right.RotationAngle) && left.IsLargeArc == right.IsLargeArc
            && left.SweepDirection == right.SweepDirection && left.IsSmoothJoin == right.IsSmoothJoin
            && left.IsStroked == right.IsStroked;

        private static void AddPath(ref HashCode hash, PortableGeometryPath path)
        {
            hash.Add(path.Kind);
            hash.Add(path.FillRule);
            AddMatrix(ref hash, path.Transform);
            AddRect(ref hash, path.Bounds);
            hash.Add(path.CombineOperation);
            var figures = path.Figures;
            hash.Add(figures.Length);
            if (path.Kind == PortableGeometryPathKind.Combined)
            {
                AddPath(ref hash, path.PathA!);
                AddPath(ref hash, path.PathB!);
            }

            for (int i = 0; i < figures.Length; i++)
            {
                var figure = figures[i];
                AddPoint(ref hash, figure.StartPoint);
                hash.Add(figure.IsClosed);
                hash.Add(figure.IsFilled);
                var segments = figure.Segments;
                hash.Add(segments.Length);
                for (int j = 0; j < segments.Length; j++)
                {
                    var segment = segments[j];
                    hash.Add(segment.Kind);
                    AddPoint(ref hash, segment.Point1);
                    AddPoint(ref hash, segment.Point2);
                    AddPoint(ref hash, segment.Point3);
                    hash.Add(segment.Size.Width);
                    hash.Add(segment.Size.Height);
                    hash.Add(segment.RotationAngle);
                    hash.Add(segment.IsLargeArc);
                    hash.Add(segment.SweepDirection);
                    hash.Add(segment.IsSmoothJoin);
                    hash.Add(segment.IsStroked);
                }
            }
        }
    }
}
