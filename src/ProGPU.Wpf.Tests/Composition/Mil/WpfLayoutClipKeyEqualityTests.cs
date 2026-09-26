using System;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfLayoutClipKeyEqualityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualPrimitiveValuesHaveEqualHashes(bool useNaN)
    {
        double value = useNaN ? double.NaN : 0.0;
        var first = WpfLayoutClipKey.Capture(new PrimitiveSource(value));
        var second = WpfLayoutClipKey.Capture(new PrimitiveSource(useNaN ? -double.NaN : -0.0));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualOwnedPathValuesHaveEqualHashes(bool combined)
    {
        var first = WpfLayoutClipKey.Capture(new PathSource(CreatePath(combined)));
        var second = WpfLayoutClipKey.Capture(new PathSource(CreatePath(combined)));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void CallerMutationCannotChangeRetainedKeyOrItsHash()
    {
        var path = CreatePath(true);
        var source = new PathSource(path);
        var original = WpfLayoutClipKey.Capture(source);
        int originalHash = original.GetHashCode();

        path.PathB!.Figures[0].Segments[0] = PortablePathSegment.Line(new PortablePoint(5, 6), false, true);
        var changed = WpfLayoutClipKey.Capture(source, original);

        Assert.NotEqual(original, changed);
        Assert.Equal(originalHash, original.GetHashCode());
        Assert.Equal(WpfLayoutClipKey.Capture(new PathSource(CreatePath(true))), original);
        Assert.Equal(changed, WpfLayoutClipKey.Capture(new PathSource(path), changed));
    }

    [Fact]
    public void PathSignedZeroAndNaNFollowDoubleEqualityForHashing()
    {
        var firstPath = CreatePath(false);
        var secondPath = CreatePath(false);
        firstPath.Transform = new PortableMatrix3x2(1, 0, double.NaN, 1, 0, 0);
        secondPath.Transform = new PortableMatrix3x2(1, -0.0, -double.NaN, 1, -0.0, 0);

        var first = WpfLayoutClipKey.Capture(new PathSource(firstPath));
        var second = WpfLayoutClipKey.Capture(new PathSource(secondPath));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    private static PortableGeometryPath CreatePath(bool combined)
    {
        if (combined)
        {
            return new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Combined,
                CombineOperation = 1,
                PathA = CreatePath(false),
                PathB = CreatePath(false)
            };
        }

        return new PortableGeometryPath
        {
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(1, 2),
                    IsClosed = true,
                    Segments = [PortablePathSegment.Line(new PortablePoint(3, 4), false, true)]
                }
            ]
        };
    }

    private sealed class PrimitiveSource(double value) : IPortablePrimitiveGeometrySource
    {
        public bool TryGetPortablePrimitiveGeometry(out PortablePrimitiveGeometry geometry)
        {
            geometry = PortablePrimitiveGeometry.Rectangle(
                new PortableRect(value, 0, 10, 20), 2, 3, PortableMatrix3x2.Identity);
            return true;
        }
    }

    private sealed class PathSource(PortableGeometryPath path) : IPortableGeometryPathSource
    {
        public bool TryGetPortableGeometryPath(out PortableGeometryPath value)
        {
            value = path;
            return true;
        }
    }
}
