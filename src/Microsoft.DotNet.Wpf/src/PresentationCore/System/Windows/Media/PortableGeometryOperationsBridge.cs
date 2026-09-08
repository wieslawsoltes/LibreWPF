// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;
using System.Windows.Media.Composition;

namespace System.Windows.Media;

internal static class PortableGeometryOperationsBridge
{
    internal static bool IsPortable => PortableWpfRuntime.GetMediaBackendAndFreeze() == PortableWpfMediaBackend.Portable;

    private static IPortableGeometryOperations Service => PortableWpfServiceRegistry.TryGetGeometryOperations(out var service)
        ? service : throw new PlatformNotSupportedException("No typed ProGPU geometry operations provider is registered.");

    internal static Rect GetBounds(PortableGeometryOperand operand, Matrix worldMatrix, bool skipHollows)
    {
        PortableRect bounds = Service.GetBounds(operand, Matrix(worldMatrix), skipHollows);
        return bounds.IsEmpty ? Rect.Empty : new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    internal static bool FillContains(Geometry geometry, Point point, double tolerance, ToleranceType type)
    {
        if (type != ToleranceType.Absolute && type != ToleranceType.Relative)
            throw new ArgumentException("Invalid geometry tolerance policy.");
        return Service.FillContains(Export(geometry, 0), new PortablePoint(point.X, point.Y),
            tolerance, type == ToleranceType.Relative);
    }

    internal static PortableGeometryOperand ExportPathData(Geometry.PathGeometryData data)
    {
        var matrix = CompositionResourceManager.MilMatrix3x2DToMatrix(ref data.Matrix);
        var context = new PathStreamGeometryContext(data.FillRule, new MatrixTransform(matrix));
        PathGeometry.ParsePathGeometryData(data, context);
        return Export(context.GetPathGeometry(), 0);
    }

    // Source MIL primitive polygon transport: one figure, line/cubic records.
    // This is decoding only; ProGPU owns transforms, extrema and containment.
    internal static unsafe PortableGeometryOperand ExportPolygon(Point* points, uint pointCount,
        byte* types, uint segmentCount, Matrix geometryMatrix)
    {
        if (pointCount == 0 || points == null || (segmentCount != 0 && types == null) ||
            pointCount > 1 << 20 || segmentCount > 1 << 20)
            throw new ArgumentException("Invalid primitive geometry transport.");
        var segments = new PortablePathSegment[segmentCount];
        uint pointIndex = 1;
        for (int index = 0; index < segments.Length; index++)
        {
            byte flags = types[index];
            bool smooth = (flags & (byte)MILCoreSegFlags.SegSmoothJoin) != 0;
            bool stroked = (flags & (byte)MILCoreSegFlags.SegIsAGap) == 0;
            byte kind = (byte)(flags & (byte)MILCoreSegFlags.SegTypeMask);
            uint consumed = kind == (byte)MILCoreSegFlags.SegTypeLine ? 1U :
                kind == (byte)MILCoreSegFlags.SegTypeBezier ? 3U :
                throw new ArgumentException("Invalid primitive segment kind.");
            if (consumed > pointCount - pointIndex) throw new ArgumentException("Truncated primitive segment.");
            Point p = points[pointIndex];
            segments[index] = consumed == 1
                ? PortablePathSegment.Line(new PortablePoint(p.X, p.Y), smooth, stroked)
                : PortablePathSegment.CubicBezier(new PortablePoint(p.X, p.Y),
                    new PortablePoint(points[pointIndex + 1].X, points[pointIndex + 1].Y),
                    new PortablePoint(points[pointIndex + 2].X, points[pointIndex + 2].Y), smooth, stroked);
            pointIndex += consumed;
        }
        if (pointIndex != pointCount) throw new ArgumentException("Unexpected primitive point count.");
        return new PortableGeometryOperand { Path = new PortableGeometryPath
        {
            Transform = Matrix(geometryMatrix),
            Figures = [new PortablePathFigure
            {
                StartPoint = new PortablePoint(points[0].X, points[0].Y), IsFilled = true,
                IsClosed = segmentCount != 0 && (types[0] & (byte)MILCoreSegFlags.SegClosed) != 0,
                Segments = segments
            }]
        }};
    }

    private static PortableMatrix3x2 Matrix(Matrix value) =>
        new(value.M11, value.M12, value.M21, value.M22, value.OffsetX, value.OffsetY);

    internal static PathGeometry Combine(Geometry first, Geometry second, GeometryCombineMode mode,
        Transform transform, double tolerance, ToleranceType toleranceType)
    {
        if (!PortableWpfServiceRegistry.TryGetGeometryOperations(out var service))
            throw new PlatformNotSupportedException("No typed ProGPU geometry operations provider is registered.");
        if ((uint)mode > (uint)GeometryCombineMode.Exclude ||
            (toleranceType != ToleranceType.Absolute && toleranceType != ToleranceType.Relative))
            throw new ArgumentException("Invalid geometry combination policy.");
        PortableGeometryPath result = service.Combine(Export(first, 0), Export(second, 0),
            (PortableGeometryCombineMode)mode, PortableGeometryPathExporter.ToPortableMatrix(transform),
            tolerance, toleranceType == ToleranceType.Relative);
        if (result == null || result.Kind != PortableGeometryPathKind.Path ||
            result.FillRule is not PortableFillRule.EvenOdd and not PortableFillRule.Nonzero)
            throw new InvalidOperationException("The geometry provider did not return a materialized path.");
        var figures = new PathFigureCollection();
        for (int figureIndex = 0; figureIndex < result.Figures.Length; figureIndex++)
        {
            PortablePathFigure source = result.Figures[figureIndex];
            var segments = new PathSegmentCollection();
            for (int segmentIndex = 0; segmentIndex < source.Segments.Length; segmentIndex++)
            {
                PortablePathSegment segment = source.Segments[segmentIndex];
                if (segment.Kind != PortablePathSegmentKind.Line)
                    throw new InvalidOperationException("The geometry provider returned a non-polygonal boundary.");
                segments.Add(new LineSegment(new Point(segment.Point1.X, segment.Point1.Y), segment.IsStroked)
                { IsSmoothJoin = segment.IsSmoothJoin });
            }
            figures.Add(new PathFigure(new Point(source.StartPoint.X, source.StartPoint.Y), segments, source.IsClosed)
            { IsFilled = source.IsFilled });
        }
        PortableMatrix3x2 matrix = result.Transform;
        return new PathGeometry(figures,
            result.FillRule == PortableFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero,
            new MatrixTransform(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY));
    }

    internal static PortableGeometryOperand Export(Geometry geometry, int depth)
    {
        if (depth > 64) throw new NotSupportedException("Geometry operation nesting exceeds 64 levels.");
        if (geometry is CombinedGeometry combined)
            return new PortableGeometryOperand
            {
                Kind = PortableGeometryOperandKind.Combined,
                CombineMode = (PortableGeometryCombineMode)combined.GeometryCombineMode,
                Transform = PortableGeometryPathExporter.ToPortableMatrix(combined.Transform),
                Children = [Export(combined.Geometry1, depth + 1), Export(combined.Geometry2, depth + 1)]
            };
        if (geometry is GeometryGroup group)
        {
            var children = new PortableGeometryOperand[group.Children?.Count ?? 0];
            for (int index = 0; index < children.Length; index++) children[index] = Export(group.Children[index], depth + 1);
            return new PortableGeometryOperand
            {
                Kind = PortableGeometryOperandKind.Group,
                FillRule = group.FillRule == FillRule.EvenOdd ? PortableFillRule.EvenOdd : PortableFillRule.Nonzero,
                Transform = PortableGeometryPathExporter.ToPortableMatrix(group.Transform),
                Children = children
            };
        }
        // Do not request geometry.Bounds: combined bounds reenter Combine and
        // ordinary Windows bounds may use legacy MIL before transport admission.
        return new PortableGeometryOperand
        {
            Path = geometry == null ? new PortableGeometryPath() :
                PortableGeometryPathExporter.FromPathGeometry(geometry.GetAsPathGeometry(), Rect.Empty)
        };
    }
}
