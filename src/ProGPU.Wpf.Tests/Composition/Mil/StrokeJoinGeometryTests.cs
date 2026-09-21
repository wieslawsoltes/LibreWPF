using System.Numerics;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class StrokeJoinGeometryTests
{
    [Fact]
    public void CreateLineJoinCreatesBevelOuterCornerTriangle()
    {
        var triangles = StrokeJoinGeometry.CreateLineJoin(
            PenLineJoin.Bevel,
            thickness: 4,
            miterLimit: 10,
            previousPoint: new Vector2(0, 0),
            joinPoint: new Vector2(10, 0),
            nextPoint: new Vector2(10, 10));

        var triangle = Assert.Single(triangles);
        AssertClose(new Vector2(10, -2), triangle.P0);
        AssertClose(new Vector2(10, 0), triangle.P1);
        AssertClose(new Vector2(12, 0), triangle.P2);
    }

    [Fact]
    public void CreateLineJoinCreatesMiterOuterCornerTrianglesWithinLimit()
    {
        var triangles = StrokeJoinGeometry.CreateLineJoin(
            PenLineJoin.Miter,
            thickness: 4,
            miterLimit: 10,
            previousPoint: new Vector2(0, 0),
            joinPoint: new Vector2(10, 0),
            nextPoint: new Vector2(10, 10));

        Assert.Equal(2, triangles.Length);
        AssertClose(new Vector2(10, -2), triangles[0].P0);
        AssertClose(new Vector2(10, 0), triangles[0].P1);
        AssertClose(new Vector2(12, 0), triangles[0].P2);
        AssertClose(new Vector2(10, -2), triangles[1].P0);
        AssertClose(new Vector2(12, -2), triangles[1].P1);
        AssertClose(new Vector2(12, 0), triangles[1].P2);
    }

    [Fact]
    public void CreateLineJoinFallsBackToBevelWhenMiterLimitIsExceeded()
    {
        var triangles = StrokeJoinGeometry.CreateLineJoin(
            PenLineJoin.Miter,
            thickness: 4,
            miterLimit: 1,
            previousPoint: new Vector2(0, 0),
            joinPoint: new Vector2(10, 0),
            nextPoint: new Vector2(10, 10));

        var triangle = Assert.Single(triangles);
        AssertClose(new Vector2(10, -2), triangle.P0);
        AssertClose(new Vector2(10, 0), triangle.P1);
        AssertClose(new Vector2(12, 0), triangle.P2);
    }

    [Fact]
    public void CreateLineJoinCreatesRoundOuterCornerFan()
    {
        var triangles = StrokeJoinGeometry.CreateLineJoin(
            PenLineJoin.Round,
            thickness: 4,
            miterLimit: 10,
            previousPoint: new Vector2(0, 0),
            joinPoint: new Vector2(10, 0),
            nextPoint: new Vector2(10, 10));

        Assert.Equal(4, triangles.Length);
        Assert.All(triangles, triangle => AssertClose(new Vector2(10, 0), triangle.P0));
        AssertClose(new Vector2(10, -2), triangles[0].P1);
        AssertClose(new Vector2(12, 0), triangles[^1].P2);
    }

    [Fact]
    public void CreateLineJoinSuppressesHardJoinForSmoothJoinSegment()
    {
        var triangles = StrokeJoinGeometry.CreateLineJoin(
            PenLineJoin.Miter,
            thickness: 4,
            miterLimit: 10,
            previousPoint: new Vector2(0, 0),
            joinPoint: new Vector2(10, 0),
            nextPoint: new Vector2(10, 10),
            isSmoothJoin: true);

        Assert.Empty(triangles);
    }

    [Fact]
    public void CreateDirectionalJoinCreatesMiterFillFromCurveTangents()
    {
        var triangles = StrokeJoinGeometry.CreateDirectionalJoin(
            PenLineJoin.Miter,
            thickness: 4,
            miterLimit: 10,
            joinPoint: new Vector2(10, 0),
            incomingDirection: new Vector2(6, 0),
            outgoingDirection: new Vector2(0, 8));

        Assert.Equal(2, triangles.Length);
        AssertClose(new Vector2(10, -2), triangles[0].P0);
        AssertClose(new Vector2(10, 0), triangles[0].P1);
        AssertClose(new Vector2(12, 0), triangles[0].P2);
        AssertClose(new Vector2(10, -2), triangles[1].P0);
        AssertClose(new Vector2(12, -2), triangles[1].P1);
        AssertClose(new Vector2(12, 0), triangles[1].P2);
    }

    [Fact]
    public void CreateDirectionalJoinSuppressesSmoothJoin()
    {
        var triangles = StrokeJoinGeometry.CreateDirectionalJoin(
            PenLineJoin.Bevel,
            thickness: 4,
            miterLimit: 10,
            joinPoint: new Vector2(10, 0),
            incomingDirection: new Vector2(1, 0),
            outgoingDirection: new Vector2(0, 1),
            isSmoothJoin: true);

        Assert.Empty(triangles);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual, float tolerance = 0.0001f)
    {
        Assert.True(
            Vector2.Distance(expected, actual) <= tolerance,
            $"Expected {expected}, got {actual}.");
    }
}
