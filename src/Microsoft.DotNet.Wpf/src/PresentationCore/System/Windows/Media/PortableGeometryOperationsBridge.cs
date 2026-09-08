// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media;

internal static class PortableGeometryOperationsBridge
{
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
