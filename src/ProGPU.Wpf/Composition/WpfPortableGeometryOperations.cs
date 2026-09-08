using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ProGPU.Backend.Native;
using ProGPU.Vector;
using ProGPU.Wpf.Interop;
using VectorPath = ProGPU.Vector.PathGeometry;
using VectorFigure = ProGPU.Vector.PathFigure;
using VectorLine = ProGPU.Vector.LineSegment;
using VectorFill = ProGPU.Vector.FillRule;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed class WpfPortableGeometryOperations : IPortableGeometryOperations
{
    private static readonly WpfPortableGeometryOperations Default = new();
    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureGeometryOperations(Default);

    public PortableRect GetBounds(PortableGeometryOperand geometry, PortableMatrix3x2 worldTransform, bool skipHollows)
    {
        try
        {
            int budget = 1 << 20;
            Matrix4x4 transform = Matrix(worldTransform);
            VectorPath path = Resolve(geometry, 0, ref budget);
            if (!transform.IsIdentity) path = path.CreateTransformed(transform);
            if (!WpfPortablePathBoundsReader.TryGetMaterializedPathBounds(path, out var bounds, out bool hasPoints, skipHollows))
                throw new NotSupportedException("The transformed geometry has no valid exact bounds.");
            if (!hasPoints) return PortableRect.Empty;
            Check(bounds.X, bounds.Y); Check(bounds.X + bounds.Width, bounds.Y + bounds.Height);
            return new PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
        catch (BadGeometryNumberException) { return PortableRect.Empty; }
    }

    public bool FillContains(PortableGeometryOperand geometry, PortablePoint point, double tolerance, bool relativeTolerance)
    {
        try
        {
            Check(point.X, point.Y); Check(tolerance, 0, nativeRange: false);
            int budget = 1 << 20;
            VectorPath path = Resolve(geometry, 0, ref budget);
            if (path.IsCombined) throw new InvalidOperationException("Unresolved geometry operand.");
            var (_, segments) = PathAtlas.CompileFillPath(path, out _, out _, out _, out _);
            double left = double.PositiveInfinity, top = double.PositiveInfinity;
            double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
            IncludeBounds(path, ref left, ref top, ref right, ref bottom);
            float absolute = ResolveTolerance(tolerance, relativeTolerance,
                double.IsPositiveInfinity(left) ? 0 : Math.Max(right - left, bottom - top));
            return NativeGeometryUtilities.FillContains(
                MemoryMarshal.Cast<GpuPathSegment, NativePathSegment>(segments.AsSpan()),
                path.FillRule == VectorFill.EvenOdd ? NativeFillRule.EvenOdd : NativeFillRule.NonZero,
                new Vector2((float)point.X, (float)point.Y), absolute);
        }
        catch (BadGeometryNumberException) { return false; }
    }

    public PortableGeometryPath Combine(PortableGeometryOperand first, PortableGeometryOperand second,
        PortableGeometryCombineMode mode, PortableMatrix3x2 resultTransform, double tolerance, bool relativeTolerance)
    {
        if ((uint)mode > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        try
        {
            Check(tolerance, 0, nativeRange: false);
            Matrix4x4 transform = Matrix(resultTransform);
            int budget = 1 << 20;
            VectorPath a = Resolve(first, 0, ref budget), b = Resolve(second, 0, ref budget);
            if (!transform.IsIdentity) { a = a.CreateTransformed(transform); b = b.CreateTransformed(transform); }
            return ToPortable(CombinePaths(a, b, mode, tolerance, relativeTolerance));
        }
        catch (BadGeometryNumberException)
        {
            // WPF absorbs nonfinite geometry as an empty result. Missing native
            // code, unsupported finite ranges and other errors must still throw.
            return new PortableGeometryPath();
        }
    }

    private static VectorPath Resolve(PortableGeometryOperand node, int depth, ref int budget)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (depth > 64 || --budget < 0) throw new NotSupportedException("Geometry operation budget exceeded.");
        if (node.Kind == PortableGeometryOperandKind.Path)
        {
            PortableGeometryPath leaf = node.Path ?? throw new ArgumentException("Missing geometry path.");
            if (leaf.Kind != PortableGeometryPathKind.Path) throw new ArgumentException("Deferred leaf geometry is not allowed.");
            Validate(leaf, ref budget);
            WpfPortablePathGeometryConverter.TryConvert(leaf, Matrix4x4.Identity, out var path, out _);
            return path;
        }
        Matrix4x4 transform = Matrix(node.Transform);
        VectorPath result;
        if (node.Kind == PortableGeometryOperandKind.Group)
        {
            result = new VectorPath { FillRule = Fill(node.FillRule) };
            for (int index = 0; index < node.Children.Length; index++)
                result.Figures.AddRange(Resolve(node.Children[index], depth + 1, ref budget).Figures);
        }
        else if (node.Kind == PortableGeometryOperandKind.Combined && node.Children.Length == 2)
        {
            VectorPath a = Resolve(node.Children[0], depth + 1, ref budget);
            VectorPath b = Resolve(node.Children[1], depth + 1, ref budget);
            if (!transform.IsIdentity) { a = a.CreateTransformed(transform); b = b.CreateTransformed(transform); }
            // Nested CombinedGeometry materialization has WPF's standard tolerance.
            return ToVector(CombinePaths(a, b, node.CombineMode, 0.25, false));
        }
        else throw new ArgumentException("Invalid geometry operand structure.");
        return transform.IsIdentity ? result : result.CreateTransformed(transform);
    }

    private static NativeGeometryOutline CombinePaths(VectorPath a, VectorPath b,
        PortableGeometryCombineMode mode, double tolerance, bool relative)
    {
        // Both paths are materialized: CompileFillPath must never invoke its
        // deferred GPU boolean path. It preserves hollow/closed figure semantics.
        if (a.IsCombined || b.IsCombined) throw new InvalidOperationException("Unresolved geometry operand.");
        var (_, first) = PathAtlas.CompileFillPath(a, out _, out _, out _, out _);
        var (_, second) = PathAtlas.CompileFillPath(b, out _, out _, out _, out _);
        double left = double.PositiveInfinity, top = double.PositiveInfinity;
        double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        IncludeBounds(a, ref left, ref top, ref right, ref bottom);
        IncludeBounds(b, ref left, ref top, ref right, ref bottom);
        double extent = double.IsPositiveInfinity(left) ? 0 : Math.Max(right - left, bottom - top);
        float absolute = ResolveTolerance(tolerance, relative, extent);
        return NativeGeometryUtilities.Combine(
            MemoryMarshal.Cast<GpuPathSegment, NativePathSegment>(first.AsSpan()),
            a.FillRule == VectorFill.EvenOdd ? NativeFillRule.EvenOdd : NativeFillRule.NonZero,
            MemoryMarshal.Cast<GpuPathSegment, NativePathSegment>(second.AsSpan()),
            b.FillRule == VectorFill.EvenOdd ? NativeFillRule.EvenOdd : NativeFillRule.NonZero,
            (NativeMilGeometryCombineMode)mode, absolute);
    }

    internal static float ResolveTolerance(double tolerance, bool relative, double extent)
    {
        Check(tolerance, extent, nativeRange: false);
        // Source WPF's Combine uses the maximum axis of the union of tight
        // transformed operand bounds, with its double-relative numeric floor.
        double value = relative ? Math.Max(tolerance, 1e-12) * extent : Math.Max(tolerance, extent * 1e-12);
        Check(value, 0);
        return Math.Max((float)value, float.Epsilon);
    }

    private static void IncludeBounds(VectorPath path, ref double left, ref double top, ref double right, ref double bottom)
    {
        if (!WpfPortablePathBoundsReader.TryGetMaterializedPathBounds(path, out var bounds, out bool hasPoints))
            throw new NotSupportedException("The transformed geometry has no valid exact bounds.");
        if (!hasPoints) return;
        Check(bounds.X, bounds.Y); Check(bounds.X + bounds.Width, bounds.Y + bounds.Height);
        left = Math.Min(left, bounds.X); top = Math.Min(top, bounds.Y);
        right = Math.Max(right, bounds.X + bounds.Width); bottom = Math.Max(bottom, bounds.Y + bounds.Height);
    }

    private static VectorPath ToVector(NativeGeometryOutline outline)
    {
        var result = new VectorPath { FillRule = VectorFill.EvenOdd };
        for (int index = 0; index < outline.ContourCount; index++)
        {
            ReadOnlySpan<Vector2> points = outline.GetContour(index);
            var figure = new VectorFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            for (int point = 1; point < points.Length; point++) figure.Segments.Add(new VectorLine(points[point]));
            result.Figures.Add(figure);
        }
        return result;
    }

    private static PortableGeometryPath ToPortable(NativeGeometryOutline outline)
    {
        var figures = new PortablePathFigure[outline.ContourCount];
        for (int index = 0; index < figures.Length; index++)
        {
            ReadOnlySpan<Vector2> points = outline.GetContour(index);
            var segments = new PortablePathSegment[points.Length - 1];
            for (int point = 1; point < points.Length; point++)
                segments[point - 1] = PortablePathSegment.Line(Point(points[point]), false, true);
            figures[index] = new PortablePathFigure { StartPoint = Point(points[0]), IsClosed = true, IsFilled = true, Segments = segments };
        }
        return new PortableGeometryPath { FillRule = PortableFillRule.EvenOdd, Figures = figures };
    }

    private static PortablePoint Point(Vector2 point)
    {
        var value = Vector128.WidenLower(Vector128.Create(point.X, point.Y, 0f, 0f));
        return new PortablePoint(value[0], value[1]);
    }

    private static VectorFill Fill(PortableFillRule fill) => fill switch
    {
        PortableFillRule.Nonzero => VectorFill.Nonzero,
        PortableFillRule.EvenOdd => VectorFill.EvenOdd,
        _ => throw new ArgumentException("Invalid geometry fill rule.")
    };

    private static void Validate(PortableGeometryPath path, ref int budget)
    {
        _ = Fill(path.FillRule);
        _ = Matrix(path.Transform);
        for (int figureIndex = 0; figureIndex < path.Figures.Length; figureIndex++)
        {
            PortablePathFigure figure = path.Figures[figureIndex];
            Check(figure.StartPoint.X, figure.StartPoint.Y);
            budget -= figure.Segments.Length + 1;
            if (budget < 0) throw new NotSupportedException("Geometry segment budget exceeded.");
            for (int index = 0; index < figure.Segments.Length; index++)
            {
                PortablePathSegment segment = figure.Segments[index];
                if ((uint)segment.Kind > (uint)PortablePathSegmentKind.Arc) throw new ArgumentException("Invalid path segment kind.");
                Check(segment.Point1.X, segment.Point1.Y);
                Check(segment.Point2.X, segment.Point2.Y);
                Check(segment.Point3.X, segment.Point3.Y);
                Check(segment.Size.Width, segment.Size.Height);
                Check(segment.RotationAngle, 0);
            }
        }
    }

    private static Matrix4x4 Matrix(PortableMatrix3x2 value)
    {
        Check(value.M11, value.M12); Check(value.M21, value.M22); Check(value.OffsetX, value.OffsetY);
        return new Matrix4x4((float)value.M11, (float)value.M12, 0, 0,
            (float)value.M21, (float)value.M22, 0, 0, 0, 0, 1, 0,
            (float)value.OffsetX, (float)value.OffsetY, 0, 1);
    }

    private static void Check(double x, double y, bool nativeRange = true)
    {
        var magnitude = Vector128.Abs(Vector128.Create(x, y));
        if (!Vector128.LessThanOrEqualAll(magnitude, Vector128.Create(double.MaxValue))) throw new BadGeometryNumberException();
        if (nativeRange && Vector128.GreaterThanAny(magnitude, Vector128.Create((double)float.MaxValue)))
            throw new NotSupportedException("Geometry exceeds the native floating-point coordinate range.");
    }

    private sealed class BadGeometryNumberException : Exception;
}
