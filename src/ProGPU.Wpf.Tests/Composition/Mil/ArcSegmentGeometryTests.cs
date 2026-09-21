using System;
using System.Numerics;
using ProGPU.Vector;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class ArcSegmentGeometryTests
{
    [Fact]
    public void FlattenArcIncludesStartEndAndIntermediatePoints()
    {
        var start = new Vector2(0, 0);
        var arc = new ArcSegment(
            new Vector2(10, 0),
            new Vector2(5, 5),
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise);

        var points = ArcSegmentGeometry.FlattenArc(start, arc);

        Assert.True(points.Length > 2);
        AssertClose(start, points[0]);
        AssertClose(arc.Point, points[^1]);
    }

    [Fact]
    public void FlattenArcFallsBackToLineForZeroRadius()
    {
        var start = new Vector2(2, 3);
        var arc = new ArcSegment(
            new Vector2(12, 8),
            Vector2.Zero,
            rotationAngle: 45,
            isLargeArc: true,
            SweepDirection.Counterclockwise);

        var points = ArcSegmentGeometry.FlattenArc(start, arc);

        Assert.Equal(2, points.Length);
        Assert.Equal(1, ArcSegmentGeometry.CountFlattenedSegments(start, arc));
        AssertClose(start, points[0]);
        AssertClose(arc.Point, points[1]);
    }

    [Fact]
    public void FlattenArcHonorsSweepDirection()
    {
        var start = new Vector2(0, 0);
        var end = new Vector2(10, 0);
        var clockwiseArc = new ArcSegment(end, new Vector2(5, 5), 0, isLargeArc: false, SweepDirection.Clockwise);
        var counterclockwiseArc = new ArcSegment(end, new Vector2(5, 5), 0, isLargeArc: false, SweepDirection.Counterclockwise);

        var clockwisePoints = ArcSegmentGeometry.FlattenArc(start, clockwiseArc);
        var counterclockwisePoints = ArcSegmentGeometry.FlattenArc(start, counterclockwiseArc);

        Assert.True(clockwisePoints[1].Y < 0.0f, $"Expected clockwise arc to bend upward, got {clockwisePoints[1]}.");
        Assert.True(counterclockwisePoints[1].Y > 0.0f, $"Expected counterclockwise arc to bend downward, got {counterclockwisePoints[1]}.");
    }

    [Fact]
    public void TryGetArcCenterScalesTooSmallRadii()
    {
        var result = ArcSegmentGeometry.TryGetArcCenter(
            new Vector2(0, 0),
            new Vector2(20, 0),
            new Vector2(5, 5),
            rotationAngleDegrees: 0,
            isLargeArc: false,
            SweepDirection.Clockwise,
            out _,
            out _,
            out _,
            out float radiusX,
            out float radiusY);

        Assert.True(result);
        AssertClose(10.0f, radiusX);
        AssertClose(10.0f, radiusY);
    }

    [Fact]
    public void TryTransformArcSegmentPreservesArcThroughShear()
    {
        var start = new Vector2(0, 0);
        var arc = new ArcSegment(
            new Vector2(30, 40),
            new Vector2(10, 20),
            rotationAngle: 45,
            isLargeArc: true,
            SweepDirection.Clockwise);
        var transform = new Matrix4x4(
            1f, 0.35f, 0f, 0f,
            0.2f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            5f, 7f, 0f, 1f);

        var result = ArcSegmentGeometry.TryTransformArcSegment(
            start,
            arc,
            transform,
            out var transformedStart,
            out var transformedArc);

        Assert.True(result);
        AssertClose(Vector2.Transform(start, transform), transformedStart);
        AssertClose(Vector2.Transform(arc.Point, transform), transformedArc.Point);
        Assert.True(transformedArc.Size.X > 0.0f);
        Assert.True(transformedArc.Size.Y > 0.0f);
        Assert.True(float.IsFinite(transformedArc.RotationAngle));
        Assert.True(transformedArc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, transformedArc.SweepDirection);
    }

    [Fact]
    public void TryTransformArcSegmentFlipsSweepForReflection()
    {
        var start = new Vector2(0, 0);
        var arc = new ArcSegment(
            new Vector2(10, 0),
            new Vector2(5, 5),
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise);
        var transform = Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f);

        var result = ArcSegmentGeometry.TryTransformArcSegment(
            start,
            arc,
            transform,
            out var transformedStart,
            out var transformedArc);

        Assert.True(result);
        AssertClose(Vector2.Transform(start, transform), transformedStart);
        AssertClose(Vector2.Transform(arc.Point, transform), transformedArc.Point);
        Assert.Equal(SweepDirection.Counterclockwise, transformedArc.SweepDirection);
        AssertClose(5.0f, transformedArc.Size.X);
        AssertClose(5.0f, transformedArc.Size.Y);
    }

    [Fact]
    public void CompilePathPreservesValidArcRecord()
    {
        var path = new PathGeometry();
        var figure = new PathFigure(new Vector2(0, 0));
        figure.Segments.Add(new ArcSegment(
            new Vector2(10, 0),
            new Vector2(5, 5),
            rotationAngle: 0,
            isLargeArc: false,
            SweepDirection.Clockwise));
        path.Figures.Add(figure);

        var (_, segments) = PathOpGeometrySolver.CompilePath(path, out _, out _, out _, out _);

        var segment = Assert.Single(segments);
        Assert.Equal(3u, segment.SegmentType);
        AssertClose(new Vector2(0, 0), segment.P0);
        AssertClose(new Vector2(10, 0), segment.P1);
        Assert.True(float.IsFinite(segment.P2.X));
        Assert.True(float.IsFinite(segment.P2.Y));
        AssertClose(new Vector2(5, 5), segment.P3);
        Assert.True(float.IsFinite(BitConverter.UInt32BitsToSingle(segment.Pad0)));
        Assert.True(float.IsFinite(BitConverter.UInt32BitsToSingle(segment.Pad1)));
        Assert.True(float.IsFinite(BitConverter.UInt32BitsToSingle(segment.Pad2)));
    }

    [Fact]
    public void CompilePathFallsBackToLineForZeroRadiusArc()
    {
        var path = new PathGeometry();
        var figure = new PathFigure(new Vector2(2, 3));
        figure.Segments.Add(new ArcSegment(
            new Vector2(12, 8),
            Vector2.Zero,
            rotationAngle: 45,
            isLargeArc: true,
            SweepDirection.Counterclockwise));
        path.Figures.Add(figure);

        var (_, segments) = PathOpGeometrySolver.CompilePath(
            path,
            out float minX,
            out float minY,
            out float maxX,
            out float maxY);

        var segment = Assert.Single(segments);
        Assert.Equal(0u, segment.SegmentType);
        AssertClose(new Vector2(2, 3), segment.P0);
        AssertClose(new Vector2(12, 8), segment.P1);
        AssertClose(2.0f, minX);
        AssertClose(3.0f, minY);
        AssertClose(12.0f, maxX);
        AssertClose(8.0f, maxY);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual, float tolerance = 0.0001f)
    {
        Assert.True(
            Vector2.Distance(expected, actual) <= tolerance,
            $"Expected {expected}, got {actual}.");
    }

    private static void AssertClose(float expected, float actual, float tolerance = 0.0001f)
    {
        Assert.True(
            MathF.Abs(expected - actual) <= tolerance,
            $"Expected {expected}, got {actual}.");
    }
}
