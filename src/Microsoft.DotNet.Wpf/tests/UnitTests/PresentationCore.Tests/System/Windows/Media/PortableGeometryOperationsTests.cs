// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media;

[Collection("Sequential")]
public class PortableGeometryOperationsTests
{
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
