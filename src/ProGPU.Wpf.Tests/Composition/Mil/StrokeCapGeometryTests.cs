using System.Numerics;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class StrokeCapGeometryTests
{
    [Fact]
    public void CreateLineCapCreatesSquareStartCap()
    {
        var triangles = StrokeCapGeometry.CreateLineCap(
            PenLineCap.Square,
            thickness: 2,
            lineStart: new Vector2(0, 0),
            lineEnd: new Vector2(10, 0),
            isStart: true);

        Assert.Equal(2, triangles.Length);
        AssertClose(new Vector2(0, -1), triangles[0].P0);
        AssertClose(new Vector2(-1, -1), triangles[0].P1);
        AssertClose(new Vector2(-1, 1), triangles[0].P2);
        AssertClose(new Vector2(0, 1), triangles[1].P2);
    }

    [Fact]
    public void CreateLineCapCreatesTriangleEndCap()
    {
        var triangles = StrokeCapGeometry.CreateLineCap(
            PenLineCap.Triangle,
            thickness: 2,
            lineStart: new Vector2(0, 0),
            lineEnd: new Vector2(10, 0),
            isStart: false);

        var triangle = Assert.Single(triangles);
        AssertClose(new Vector2(10, -1), triangle.P0);
        AssertClose(new Vector2(11, 0), triangle.P1);
        AssertClose(new Vector2(10, 1), triangle.P2);
    }

    [Fact]
    public void CreateLineCapCreatesRoundEndCapFan()
    {
        var triangles = StrokeCapGeometry.CreateLineCap(
            PenLineCap.Round,
            thickness: 2,
            lineStart: new Vector2(0, 0),
            lineEnd: new Vector2(10, 0),
            isStart: false);

        Assert.Equal(8, triangles.Length);
        Assert.All(triangles, triangle => AssertClose(new Vector2(10, 0), triangle.P0));
        AssertClose(new Vector2(10, -1), triangles[0].P1);
        AssertClose(new Vector2(10, 1), triangles[^1].P2);
    }

    [Fact]
    public void CreateLineCapSkipsFlatCap()
    {
        var triangles = StrokeCapGeometry.CreateLineCap(
            PenLineCap.Flat,
            thickness: 2,
            lineStart: new Vector2(0, 0),
            lineEnd: new Vector2(10, 0),
            isStart: true);

        Assert.Empty(triangles);
    }

    [Fact]
    public void CreateDirectionalCapCreatesSquareStartCapFromCurveTangent()
    {
        var triangles = StrokeCapGeometry.CreateDirectionalCap(
            PenLineCap.Square,
            thickness: 2,
            center: new Vector2(5, 5),
            directionAlongPath: new Vector2(0, 10),
            isStart: true);

        Assert.Equal(2, triangles.Length);
        AssertClose(new Vector2(6, 5), triangles[0].P0);
        AssertClose(new Vector2(6, 4), triangles[0].P1);
        AssertClose(new Vector2(4, 4), triangles[0].P2);
        AssertClose(new Vector2(4, 5), triangles[1].P2);
    }

    [Fact]
    public void CreateDirectionalCapCreatesTriangleEndCapFromCurveTangent()
    {
        var triangles = StrokeCapGeometry.CreateDirectionalCap(
            PenLineCap.Triangle,
            thickness: 2,
            center: new Vector2(5, 5),
            directionAlongPath: new Vector2(0, 10),
            isStart: false);

        var triangle = Assert.Single(triangles);
        AssertClose(new Vector2(6, 5), triangle.P0);
        AssertClose(new Vector2(5, 6), triangle.P1);
        AssertClose(new Vector2(4, 5), triangle.P2);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual, float tolerance = 0.0001f)
    {
        Assert.True(
            Vector2.Distance(expected, actual) <= tolerance,
            $"Expected {expected}, got {actual}.");
    }
}
