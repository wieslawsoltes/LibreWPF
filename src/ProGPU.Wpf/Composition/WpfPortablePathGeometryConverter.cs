using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using ProGPU.Wpf.Interop;
using VectorArcSegment = ProGPU.Vector.ArcSegment;
using VectorCubicBezierSegment = ProGPU.Vector.CubicBezierSegment;
using VectorFillRule = ProGPU.Vector.FillRule;
using VectorLineSegment = ProGPU.Vector.LineSegment;
using VectorPathFigure = ProGPU.Vector.PathFigure;
using VectorPathGeometry = ProGPU.Vector.PathGeometry;
using VectorQuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;
using VectorSweepDirection = ProGPU.Vector.SweepDirection;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortablePathGeometryConverter
{
    private const ulong PortablePathKeyOffset = 1469598103934665603UL;
    private const ulong PortablePathKeyPrime = 1099511628211UL;
    private static readonly ConditionalWeakTable<PortableGeometryPath, PortablePathConversionCache> s_pathCache = new();

    public static bool TryConvert(
        PortableGeometryPath portablePath,
        Matrix4x4 transform,
        out VectorPathGeometry path,
        out WpfReplayRect bounds)
    {
        var hasCacheKey = TryReadPortablePathKey(portablePath, transform, out var cacheKey);
        if (hasCacheKey
            && s_pathCache.TryGetValue(portablePath, out var cache)
            && cache.TryGet(cacheKey, out path, out bounds))
        {
            return true;
        }

        path = Convert(portablePath);
        if (!transform.IsIdentity)
        {
            path = path.CreateTransformed(transform);
        }

        bounds = GetBoundsOrEmpty(path);
        if (hasCacheKey)
        {
            s_pathCache.GetOrCreateValue(portablePath).Set(cacheKey, path, bounds);
        }

        return true;
    }

    public static bool TryGetNativePathBounds(PortableGeometryPath portablePath, out WpfReplayRect bounds)
    {
        if (!TryConvert(portablePath, Matrix4x4.Identity, out _, out bounds)
            || !IsUsableBounds(bounds))
        {
            bounds = default;
            return false;
        }

        return true;
    }

    public static WpfReplayRect GetBoundsOrEmpty(VectorPathGeometry path)
    {
        if (!path.TryGetBounds(out var min, out var max))
        {
            return WpfReplayRect.Empty;
        }

        return new WpfReplayRect(
            min.X,
            min.Y,
            Math.Max(0.0, max.X - min.X),
            Math.Max(0.0, max.Y - min.Y));
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

    private static VectorPathGeometry Convert(PortableGeometryPath portablePath)
    {
        VectorPathGeometry path;
        if (portablePath.Kind == PortableGeometryPathKind.Combined)
        {
            path = new VectorPathGeometry
            {
                IsCombined = true,
                PathA = portablePath.PathA != null
                    ? Convert(portablePath.PathA)
                    : new VectorPathGeometry(),
                PathB = portablePath.PathB != null
                    ? Convert(portablePath.PathB)
                    : new VectorPathGeometry(),
                Op = portablePath.CombineOperation,
                FillRule = ToNativeFillRule(portablePath.FillRule)
            };
        }
        else
        {
            path = new VectorPathGeometry
            {
                FillRule = ToNativeFillRule(portablePath.FillRule)
            };

            var portableFigures = portablePath.Figures;
            for (var figureIndex = 0; figureIndex < portableFigures.Length; figureIndex++)
            {
                var portableFigure = portableFigures[figureIndex];
                var figure = new VectorPathFigure
                {
                    StartPoint = ToVector2(portableFigure.StartPoint),
                    IsClosed = portableFigure.IsClosed,
                    IsFilled = portableFigure.IsFilled
                };

                var segments = portableFigure.Segments;
                for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                {
                    var segment = segments[segmentIndex];
                    AddPortableSegment(figure, segment);
                }

                path.Figures.Add(figure);
            }
        }

        var localTransform = ToMatrix4x4(portablePath.Transform);
        return localTransform.IsIdentity
            ? path
            : path.CreateTransformed(localTransform);
    }

    private static void AddPortableSegment(VectorPathFigure figure, PortablePathSegment segment)
    {
        switch (segment.Kind)
        {
            case PortablePathSegmentKind.Line:
                figure.Segments.Add(new VectorLineSegment(
                    ToVector2(segment.Point1),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.QuadraticBezier:
                figure.Segments.Add(new VectorQuadraticBezierSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Point2),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.CubicBezier:
                figure.Segments.Add(new VectorCubicBezierSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Point2),
                    ToVector2(segment.Point3),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
            case PortablePathSegmentKind.Arc:
                figure.Segments.Add(new VectorArcSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Size),
                    (float)segment.RotationAngle,
                    segment.IsLargeArc,
                    ToNativeSweepDirection(segment.SweepDirection),
                    segment.IsSmoothJoin,
                    segment.IsStroked));
                break;
        }
    }

    private static VectorFillRule ToNativeFillRule(PortableFillRule fillRule)
    {
        return fillRule == PortableFillRule.Nonzero
            ? VectorFillRule.Nonzero
            : VectorFillRule.EvenOdd;
    }

    private static VectorSweepDirection ToNativeSweepDirection(PortableSweepDirection sweepDirection)
    {
        return sweepDirection == PortableSweepDirection.Clockwise
            ? VectorSweepDirection.Clockwise
            : VectorSweepDirection.Counterclockwise;
    }

    private static Matrix4x4 ToMatrix4x4(PortableMatrix3x2 matrix)
    {
        return new Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0.0f,
            0.0f,
            (float)matrix.M21,
            (float)matrix.M22,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            1.0f,
            0.0f,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0.0f,
            1.0f);
    }

    private static Vector2 ToVector2(PortablePoint point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2 ToVector2(PortableSize size)
    {
        return new Vector2((float)size.Width, (float)size.Height);
    }

    private static bool TryReadPortablePathKey(
        PortableGeometryPath portablePath,
        Matrix4x4 transform,
        out PortablePathCacheKey key)
    {
        var hash = PortablePathKeyOffset;
        var figureCount = 0;
        var segmentCount = 0;
        var pathCount = 0;
        AddMatrixHash(ref hash, transform);
        if (!AddPortablePathKey(portablePath, ref hash, ref figureCount, ref segmentCount, ref pathCount, depth: 0))
        {
            key = default;
            return false;
        }

        key = new PortablePathCacheKey(hash, figureCount, segmentCount, pathCount);
        return true;
    }

    private static bool AddPortablePathKey(
        PortableGeometryPath portablePath,
        ref ulong hash,
        ref int figureCount,
        ref int segmentCount,
        ref int pathCount,
        int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        pathCount++;
        AddHash(ref hash, (int)portablePath.Kind);
        AddHash(ref hash, (int)portablePath.FillRule);
        AddPortableMatrixHash(ref hash, portablePath.Transform);

        if (portablePath.Kind == PortableGeometryPathKind.Combined)
        {
            AddHash(ref hash, portablePath.CombineOperation);
            if (!AddOptionalPortablePathKey(portablePath.PathA, ref hash, ref figureCount, ref segmentCount, ref pathCount, depth + 1))
            {
                return false;
            }

            return AddOptionalPortablePathKey(portablePath.PathB, ref hash, ref figureCount, ref segmentCount, ref pathCount, depth + 1);
        }

        var portableFigures = portablePath.Figures;
        if (portableFigures == null)
        {
            return false;
        }

        figureCount += portableFigures.Length;
        AddHash(ref hash, portableFigures.Length);
        for (var figureIndex = 0; figureIndex < portableFigures.Length; figureIndex++)
        {
            var portableFigure = portableFigures[figureIndex];
            if (portableFigure == null || portableFigure.Segments == null)
            {
                return false;
            }

            AddPortablePointHash(ref hash, portableFigure.StartPoint);
            AddHash(ref hash, portableFigure.IsClosed ? 1 : 0);
            AddHash(ref hash, portableFigure.IsFilled ? 1 : 0);

            var segments = portableFigure.Segments;
            segmentCount += segments.Length;
            AddHash(ref hash, segments.Length);
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                AddPortableSegmentHash(ref hash, segments[segmentIndex]);
            }
        }

        return true;
    }

    private static bool AddOptionalPortablePathKey(
        PortableGeometryPath? portablePath,
        ref ulong hash,
        ref int figureCount,
        ref int segmentCount,
        ref int pathCount,
        int depth)
    {
        if (portablePath == null)
        {
            AddHash(ref hash, 0);
            return true;
        }

        return AddPortablePathKey(portablePath, ref hash, ref figureCount, ref segmentCount, ref pathCount, depth);
    }

    private static void AddPortableSegmentHash(ref ulong hash, PortablePathSegment segment)
    {
        AddHash(ref hash, (int)segment.Kind);
        AddPortablePointHash(ref hash, segment.Point1);
        AddPortablePointHash(ref hash, segment.Point2);
        AddPortablePointHash(ref hash, segment.Point3);
        AddPortableSizeHash(ref hash, segment.Size);
        AddHash(ref hash, segment.RotationAngle);
        AddHash(ref hash, segment.IsLargeArc ? 1 : 0);
        AddHash(ref hash, (int)segment.SweepDirection);
        AddHash(ref hash, segment.IsSmoothJoin ? 1 : 0);
        AddHash(ref hash, segment.IsStroked ? 1 : 0);
    }

    private static void AddPortablePointHash(ref ulong hash, PortablePoint point)
    {
        AddHash(ref hash, point.X);
        AddHash(ref hash, point.Y);
    }

    private static void AddPortableSizeHash(ref ulong hash, PortableSize size)
    {
        AddHash(ref hash, size.Width);
        AddHash(ref hash, size.Height);
    }

    private static void AddPortableMatrixHash(ref ulong hash, PortableMatrix3x2 matrix)
    {
        AddHash(ref hash, matrix.M11);
        AddHash(ref hash, matrix.M12);
        AddHash(ref hash, matrix.M21);
        AddHash(ref hash, matrix.M22);
        AddHash(ref hash, matrix.OffsetX);
        AddHash(ref hash, matrix.OffsetY);
    }

    private static void AddMatrixHash(ref ulong hash, Matrix4x4 matrix)
    {
        AddHash(ref hash, matrix.M11);
        AddHash(ref hash, matrix.M12);
        AddHash(ref hash, matrix.M13);
        AddHash(ref hash, matrix.M14);
        AddHash(ref hash, matrix.M21);
        AddHash(ref hash, matrix.M22);
        AddHash(ref hash, matrix.M23);
        AddHash(ref hash, matrix.M24);
        AddHash(ref hash, matrix.M31);
        AddHash(ref hash, matrix.M32);
        AddHash(ref hash, matrix.M33);
        AddHash(ref hash, matrix.M34);
        AddHash(ref hash, matrix.M41);
        AddHash(ref hash, matrix.M42);
        AddHash(ref hash, matrix.M43);
        AddHash(ref hash, matrix.M44);
    }

    private static void AddHash(ref ulong hash, double value)
    {
        AddHash(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
    }

    private static void AddHash(ref ulong hash, float value)
    {
        AddHash(ref hash, BitConverter.SingleToUInt32Bits(value));
    }

    private static void AddHash(ref ulong hash, int value)
    {
        AddHash(ref hash, unchecked((uint)value));
    }

    private static void AddHash(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= PortablePathKeyPrime;
    }

    private readonly record struct PortablePathCacheKey(
        ulong Hash,
        int FigureCount,
        int SegmentCount,
        int PathCount);

    private sealed class PortablePathConversionCache
    {
        private bool _hasPath;
        private PortablePathCacheKey _key;
        private VectorPathGeometry? _path;
        private WpfReplayRect _bounds;

        public bool TryGet(PortablePathCacheKey key, out VectorPathGeometry path, out WpfReplayRect bounds)
        {
            if (_hasPath && _key == key)
            {
                path = _path!;
                bounds = _bounds;
                return true;
            }

            path = null!;
            bounds = default;
            return false;
        }

        public void Set(PortablePathCacheKey key, VectorPathGeometry path, WpfReplayRect bounds)
        {
            _key = key;
            _path = path;
            _bounds = bounds;
            _hasPath = true;
        }
    }
}
