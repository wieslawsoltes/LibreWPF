using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Backend.Native;
using ProGPU.Vector;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public class WpfPortableGeometryOperationsTests
{
    [Fact]
    public void BoundsPreserveCurveExtremaHollowsAndBothTransforms()
    {
        var path = new PortableGeometryOperand { Path = new PortableGeometryPath
        {
            Transform = new PortableMatrix3x2(2, 0, 0, 3, 10, 20),
            Figures = [new PortablePathFigure
            {
                StartPoint = new(0, 0), IsFilled = true,
                Segments = [PortablePathSegment.CubicBezier(new(0, 100), new(100, 100), new(100, 0), false, true)]
            }, new PortablePathFigure { StartPoint = new(-10, -20), IsFilled = false }]
        }};
        var service = new WpfPortableGeometryOperations();
        var world = new PortableMatrix3x2(1, 0, 0, 1, 7, 8);
        var fill = service.GetBounds(path, world, true);
        Assert.False(fill.IsEmpty);
        Assert.Equal(17, fill.X); Assert.Equal(28, fill.Y);
        Assert.Equal(200, fill.Width); Assert.Equal(225, fill.Height);
        var all = service.GetBounds(path, world, false);
        Assert.Equal(-3, all.X); Assert.Equal(-32, all.Y);
        Assert.Equal(220, all.Width); Assert.Equal(285, all.Height);
    }

    [Fact]
    public void BoundsDistinguishEmptyFromSinglePointAndPreserveNonfinitePolicy()
    {
        var service = new WpfPortableGeometryOperations();
        Assert.True(service.GetBounds(new() { Path = new() }, PortableMatrix3x2.Identity, false).IsEmpty);
        var point = new PortableGeometryOperand { Path = new() { Figures = [new() { StartPoint = new(3, 4) }] } };
        var bounds = service.GetBounds(point, PortableMatrix3x2.Identity, false);
        Assert.False(bounds.IsEmpty); Assert.Equal(3, bounds.X); Assert.Equal(4, bounds.Y);
        Assert.Equal(0, bounds.Width); Assert.Equal(0, bounds.Height);
        Assert.False(service.FillContains(point, new(double.NaN, 0), 0.25, false));
        Assert.True(service.GetBounds(point, new(1, 0, 0, 1, double.NaN, 0), false).IsEmpty);
    }

    [Theory]
    [InlineData(0.01, true, 200, 2)]
    [InlineData(0.25, false, 200, 0.25)]
    [InlineData(0, true, 100, 1e-10)]
    [InlineData(-1, false, 100, 1e-10)]
    public void ToleranceUsesTransformedUnionExtent(double tolerance, bool relative, double extent, double expected)
    {
        Assert.Equal((float)expected, WpfPortableGeometryOperations.ResolveTolerance(tolerance, relative, extent));
    }

    [Fact]
    public void ToleranceBoundsUseCurveExtremaNotControlHull()
    {
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = Vector2.Zero, IsClosed = true };
        figure.Segments.Add(new CubicBezierSegment(new(0, 100), new(100, 100), new(100, 0)));
        path.Figures.Add(figure);
        var transformed = path.CreateTransformed(Matrix4x4.CreateScale(2, 3, 1));
        Assert.True(WpfPortablePathBoundsReader.TryGetMaterializedPathBounds(transformed, out var bounds, out bool hasPoints));
        Assert.True(hasPoints);
        Assert.Equal(200, bounds.Width);
        Assert.Equal(225, bounds.Height);
        Assert.Equal(2.25f, WpfPortableGeometryOperations.ResolveTolerance(0.01, true, Math.Max(bounds.Width, bounds.Height)));
    }

    [Fact]
    public void NativeCastingUsesTheExistingCanonicalSegmentLayout()
    {
        Assert.Equal(Unsafe.SizeOf<GpuPathSegment>(), Unsafe.SizeOf<NativePathSegment>());
        Assert.Equal(Marshal.OffsetOf<GpuPathSegment>(nameof(GpuPathSegment.SegmentType)),
            Marshal.OffsetOf<NativePathSegment>(nameof(NativePathSegment.Kind)));
        Assert.Equal(Marshal.OffsetOf<GpuPathSegment>(nameof(GpuPathSegment.Pad2)),
            Marshal.OffsetOf<NativePathSegment>(nameof(NativePathSegment.Pad2)));
    }

    [Fact]
    public void NonfiniteGeometryReturnsEmptyBeforeNativeLoading()
    {
        var bad = new PortableGeometryOperand
        {
            Path = new PortableGeometryPath
            {
                Figures = [new PortablePathFigure { StartPoint = new(double.NaN, 0) }]
            }
        };
        var result = new WpfPortableGeometryOperations().Combine(bad,
            new PortableGeometryOperand { Path = new PortableGeometryPath() },
            PortableGeometryCombineMode.Union, PortableMatrix3x2.Identity, 0.25, false);
        Assert.Empty(result.Figures);
    }

    [Fact]
    public void FiniteOutOfRangeGeometryFailsInsteadOfBecomingBoundsOrInfinity()
    {
        var bad = new PortableGeometryOperand
        {
            Path = new PortableGeometryPath { Figures = [new PortablePathFigure { StartPoint = new(double.MaxValue, 0) }] }
        };
        Assert.Throws<NotSupportedException>(() => new WpfPortableGeometryOperations().Combine(bad,
            new PortableGeometryOperand { Path = new PortableGeometryPath() },
            PortableGeometryCombineMode.Union, PortableMatrix3x2.Identity, 0.25, false));
    }
}
