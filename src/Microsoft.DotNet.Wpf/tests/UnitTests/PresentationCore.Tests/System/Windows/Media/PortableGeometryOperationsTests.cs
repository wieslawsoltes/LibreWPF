// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media;

[Collection("Sequential")]
public class PortableGeometryOperationsTests
{
    [Fact]
    public void PathDataBoundsPreserveFillTransformAndSkipHollows()
    {
        var service = new GeometryService();
        using var registration = PortableWpfServiceRegistry.RegisterGeometryOperations(service);
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd, Transform = new TranslateTransform(10, 20) };
        geometry.Figures.Add(new PathFigure(new Point(1, 2), [new LineSegment(new Point(3, 4), false)], false)
        { IsFilled = false });
        var operand = PortableGeometryOperationsBridge.ExportPathData(geometry.GetPathGeometryData());
        var result = PortableGeometryOperationsBridge.GetBounds(operand, new Matrix(2, 0, 0, 3, 7, 8), true);
        operand.Path!.FillRule.Should().Be(PortableFillRule.EvenOdd);
        operand.Path.Transform.OffsetX.Should().Be(10);
        operand.Path.Transform.OffsetY.Should().Be(20);
        operand.Path.Figures[0].IsFilled.Should().BeFalse();
        operand.Path.Figures[0].Segments[0].IsStroked.Should().BeFalse();
        service.SkipHollows.Should().BeTrue();
        service.Transform.M11.Should().Be(2);
        result.Should().Be(new Rect(11, 12, 13, 14));
    }

    [Fact]
    public void FillQueryForwardsPointAndToleranceWithoutBoundsQueries()
    {
        var service = new GeometryService();
        using var registration = PortableWpfServiceRegistry.RegisterGeometryOperations(service);
        PortableGeometryOperationsBridge.FillContains(new RectangleGeometry(new Rect(0, 0, 10, 10)),
            new Point(8, 9), 0.125, ToleranceType.Relative).Should().BeTrue();
        service.Point.X.Should().Be(8); service.Point.Y.Should().Be(9);
        service.Tolerance.Should().Be(0.125); service.Relative.Should().BeTrue();
        service.Calls.Should().Be(1);
    }

    [Fact]
    public unsafe void PrimitiveDecoderPreservesCubicsFlagsAndRejectsTruncatedTransport()
    {
        Point* points = stackalloc Point[] { new(1, 2), new(3, 4), new(5, 6), new(7, 8) };
        byte* types = stackalloc byte[] { 2 | 4 | 8 | 16 | 32 };
        var operand = PortableGeometryOperationsBridge.ExportPolygon(points, 4, types, 1, Matrix.Identity);
        var figure = operand.Path!.Figures[0];
        figure.IsClosed.Should().BeTrue(); figure.IsFilled.Should().BeTrue();
        figure.Segments[0].Kind.Should().Be(PortablePathSegmentKind.CubicBezier);
        figure.Segments[0].IsSmoothJoin.Should().BeTrue(); figure.Segments[0].IsStroked.Should().BeFalse();
        figure.Segments[0].Point3.X.Should().Be(7);
        bool rejected = false;
        try { PortableGeometryOperationsBridge.ExportPolygon(points, 3, types, 1, Matrix.Identity); }
        catch (ArgumentException) { rejected = true; }
        rejected.Should().BeTrue();
    }

    [Fact]
    public void NestedGroupExportDoesNotAskForCombinedBoundsOrExecuteTheProvider()
    {
        var service = new GeometryService();
        using var registration = PortableWpfServiceRegistry.RegisterGeometryOperations(service);
        var combined = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, 20, 20)),
            new EllipseGeometry(new Point(10, 10), 5, 5))
        { Transform = new TranslateTransform(2, 3) };
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd, Transform = new ScaleTransform(2, 3) };
        group.Children.Add(combined);
        PortableGeometryOperand snapshot = PortableGeometryOperationsBridge.Export(group, 0);
        snapshot.Kind.Should().Be(PortableGeometryOperandKind.Group);
        snapshot.FillRule.Should().Be(PortableFillRule.EvenOdd);
        snapshot.Transform.M11.Should().Be(2);
        snapshot.Transform.M22.Should().Be(3);
        snapshot.Children[0].Kind.Should().Be(PortableGeometryOperandKind.Combined);
        snapshot.Children[0].CombineMode.Should().Be(PortableGeometryCombineMode.Exclude);
        snapshot.Children[0].Transform.OffsetX.Should().Be(2);
        snapshot.Children[0].Children[0].Path!.Bounds.IsEmpty.Should().BeTrue();
        snapshot.Children[0].Children[1].Path!.Bounds.IsEmpty.Should().BeTrue();
        service.Calls.Should().Be(0);
    }

    [Fact]
    public void TypedCombinePreservesPolicyAndReturnsActualContourInsteadOfBounds()
    {
        var service = new GeometryService();
        using var registration = PortableWpfServiceRegistry.RegisterGeometryOperations(service);
        var result = PortableGeometryOperationsBridge.Combine(
            new RectangleGeometry(new Rect(0, 0, 10, 10)),
            new RectangleGeometry(new Rect(5, 5, 10, 10)),
            GeometryCombineMode.Intersect, new TranslateTransform(7, 8), 0.01, ToleranceType.Relative);
        service.Calls.Should().Be(1);
        service.Mode.Should().Be(PortableGeometryCombineMode.Intersect);
        service.Transform.OffsetX.Should().Be(7);
        service.Transform.OffsetY.Should().Be(8);
        service.Tolerance.Should().Be(0.01);
        service.Relative.Should().BeTrue();
        result.FillRule.Should().Be(FillRule.EvenOdd);
        result.Figures.Count.Should().Be(1);
        result.Figures[0].Segments.Count.Should().Be(2);
        ((LineSegment)result.Figures[0].Segments[0]).Point.Should().Be(new Point(10, 0));
        ((LineSegment)result.Figures[0].Segments[1]).Point.Should().Be(new Point(0, 10));
        result.Figures[0].IsClosed.Should().BeTrue();
    }

    [Fact]
    public void MissingProviderFailsInsteadOfReintroducingTheRectangleApproximation()
    {
        using var registration = PortableWpfServiceRegistry.RegisterGeometryOperations(new GeometryService());
        registration.Dispose();
        Action combine = () => PortableGeometryOperationsBridge.Combine(new PathGeometry(), new PathGeometry(),
            GeometryCombineMode.Union, null!, 0.25, ToleranceType.Absolute);
        combine.Should().Throw<PlatformNotSupportedException>();
    }

    private sealed class GeometryService : IPortableGeometryOperations
    {
        public int Calls;
        public PortableGeometryCombineMode Mode;
        public PortableMatrix3x2 Transform;
        public double Tolerance;
        public bool Relative;
        public bool SkipHollows;
        public PortablePoint Point;
        public PortableRect GetBounds(PortableGeometryOperand geometry, PortableMatrix3x2 transform, bool skipHollows)
        {
            Calls++; Transform = transform; SkipHollows = skipHollows;
            return new PortableRect(11, 12, 13, 14);
        }
        public bool FillContains(PortableGeometryOperand geometry, PortablePoint point, double tolerance, bool relative)
        {
            Calls++; Point = point; Tolerance = tolerance; Relative = relative;
            return true;
        }
        public PortableGeometryPath Combine(PortableGeometryOperand first, PortableGeometryOperand second,
            PortableGeometryCombineMode mode, PortableMatrix3x2 transform, double tolerance, bool relative)
        {
            Calls++;
            Mode = mode; Transform = transform; Tolerance = tolerance; Relative = relative;
            return new PortableGeometryPath
            {
                FillRule = PortableFillRule.EvenOdd,
                Figures = [new PortablePathFigure
                {
                    StartPoint = new(0, 0), IsClosed = true,
                    Segments = [PortablePathSegment.Line(new(10, 0), false, true), PortablePathSegment.Line(new(0, 10), false, true)]
                }]
            };
        }
    }
}
