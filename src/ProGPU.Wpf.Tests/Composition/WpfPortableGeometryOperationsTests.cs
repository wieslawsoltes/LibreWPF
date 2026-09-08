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
