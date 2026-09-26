// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfLayoutClipKeyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void FreshEqualPrimitiveClipsDoNotRequestAnotherDirtyPass(int sample)
    {
        var root = new LayoutRoot(() => new PrimitiveSource(Primitive(sample)));
        using var observation = new TrackerObservation(root);

        observation.AssertIdle(32);

        Assert.True(root.ReadCount >= 32);
        Assert.Equal(0, observation.InvalidatedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FreshEqualPathGraphsDoNotRequestAnotherDirtyPass(int sample)
    {
        var root = new LayoutRoot(() => new PathSource(sample switch
        {
            0 => Path(),
            1 => CombinedPath(),
            _ => new PortableGeometryPath()
        }));
        using var observation = new TrackerObservation(root);

        observation.AssertIdle(32);

        Assert.True(root.ReadCount >= 32);
        Assert.Equal(0, observation.InvalidatedCount);
    }

    public static IEnumerable<object[]> PrimitiveChanges()
    {
        var identity = PortableMatrix3x2.Identity;
        var start = new PortablePoint(1, 2);
        var end = new PortablePoint(7, 8);
        var line = PortablePrimitiveGeometry.Line(start, end, identity);
        yield return [line, PortablePrimitiveGeometry.Line(new(2, 2), end, identity)];
        yield return [line, PortablePrimitiveGeometry.Line(new(1, 3), end, identity)];
        yield return [line, PortablePrimitiveGeometry.Line(start, new(8, 8), identity)];
        yield return [line, PortablePrimitiveGeometry.Line(start, new(7, 9), identity)];
        var rectangle = Rectangle(new(3, 4, 30, 40));
        yield return [rectangle, Rectangle(new(4, 4, 30, 40))];
        yield return [rectangle, Rectangle(new(3, 5, 30, 40))];
        yield return [rectangle, Rectangle(new(3, 4, 31, 40))];
        yield return [rectangle, Rectangle(new(3, 4, 30, 41))];
        yield return [rectangle, PortablePrimitiveGeometry.Rectangle(new(3, 4, 30, 40), 2, 0, identity)];
        yield return [rectangle, PortablePrimitiveGeometry.Rectangle(new(3, 4, 30, 40), 0, 2, identity)];
        var ellipse = PortablePrimitiveGeometry.Ellipse(start, 3, 4, identity);
        yield return [ellipse, PortablePrimitiveGeometry.Ellipse(new(2, 2), 3, 4, identity)];
        yield return [ellipse, PortablePrimitiveGeometry.Ellipse(new(1, 3), 3, 4, identity)];
        yield return [ellipse, PortablePrimitiveGeometry.Ellipse(start, 4, 4, identity)];
        yield return [ellipse, PortablePrimitiveGeometry.Ellipse(start, 3, 5, identity)];
        yield return [line, ellipse];
        yield return [Rectangle(PortableRect.Empty), Rectangle(new(0, 0, 0, 0))];
        yield return [Rectangle(new(0, 0, 0, 0)), Rectangle(PortableRect.Empty)];
        yield return [Rectangle(new(0, 0, 0, 40)), Rectangle(new(0, 0, 1, 40))];
        yield return [Rectangle(new(0, 0, 40, 0)), Rectangle(new(0, 0, 40, 1))];
    }

    [Theory]
    [MemberData(nameof(PrimitiveChanges))]
    public void RealPrimitiveChangesInvalidateTheLayoutOwner(
        PortablePrimitiveGeometry before, PortablePrimitiveGeometry after)
    {
        var clip = new PrimitiveSource(before);
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));

        clip.Value = after;

        observation.AssertOneChange();
    }

    public static IEnumerable<object[]> PrimitiveTransformChanges()
    {
        for (int kind = 0; kind < 3; kind++)
        {
            for (int component = 0; component < 6; component++)
            {
                yield return [kind, component];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PrimitiveTransformChanges))]
    public void EveryPrimitiveAffineComponentParticipatesInInvalidation(int kind, int component)
    {
        var clip = new PrimitiveSource(WithTransform(kind, PortableMatrix3x2.Identity));
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));
        double[] values = [1, 0, 0, 1, 0, 0];
        values[component] += 0.25;

        clip.Value = WithTransform(kind, new(values[0], values[1], values[2], values[3], values[4], values[5]));

        observation.AssertOneChange();
    }

    [Theory]
    [InlineData("fill-rule")]
    [InlineData("transform")]
    [InlineData("bounds")]
    [InlineData("figure-start")]
    [InlineData("figure-closed")]
    [InlineData("figure-filled")]
    [InlineData("figure-count")]
    [InlineData("segment-count")]
    [InlineData("line-point")]
    [InlineData("segment-kind")]
    [InlineData("smooth-join")]
    [InlineData("stroked")]
    [InlineData("quadratic-control")]
    [InlineData("quadratic-end")]
    [InlineData("cubic-first-control")]
    [InlineData("cubic-second-control")]
    [InlineData("cubic-end")]
    [InlineData("arc-end")]
    [InlineData("arc-width")]
    [InlineData("arc-height")]
    [InlineData("arc-rotation")]
    [InlineData("arc-large")]
    [InlineData("arc-sweep")]
    public void MutatingCachedPathStorageInvalidatesTheLayoutOwner(string change)
    {
        var path = Path();
        var clip = new PathSource(path);
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));

        MutatePath(path, change);

        observation.AssertOneChange();
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("first-operand")]
    [InlineData("second-operand")]
    [InlineData("operand-order")]
    [InlineData("fill-rule")]
    [InlineData("transform")]
    public void CombinedGeometryChangesInvalidateWithoutReplacingItsProvider(string change)
    {
        var path = CombinedPath();
        var clip = new PathSource(path);
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));

        switch (change)
        {
            case "operation": path.CombineOperation = 1; break;
            case "first-operand": path.PathA!.Figures[0].IsFilled = false; break;
            case "second-operand": path.PathB!.Figures[0].IsClosed = false; break;
            case "operand-order": (path.PathA, path.PathB) = (path.PathB, path.PathA); break;
            case "fill-rule": path.FillRule = PortableFillRule.EvenOdd; break;
            case "transform": path.Transform = new(1, 0, 0, 1, 3, 4); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }

        observation.AssertOneChange();
    }

    [Theory]
    [InlineData("unknown-object")]
    [InlineData("failed-primitive")]
    [InlineData("failed-path")]
    [InlineData("unknown-path-kind")]
    [InlineData("null-figures")]
    [InlineData("null-segments")]
    [InlineData("cycle")]
    [InlineData("over-depth")]
    [InlineData("over-budget")]
    public void UnsupportedDescriptorsRetainReferenceIdentity(string failure)
    {
        object clip = Unsupported(failure);
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));
        observation.AssertIdle(8);

        clip = Unsupported(failure);

        observation.AssertOneChange();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameProviderTransitionsBetweenKnownAndUnavailableInvalidate(bool primitive)
    {
        var primitiveSource = new PrimitiveSource(Primitive(1));
        var pathSource = new PathSource(Path());
        object clip = primitive ? primitiveSource : pathSource;
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));

        primitiveSource.Available = pathSource.Available = false;
        observation.AssertOneChange();

        primitiveSource.Available = pathSource.Available = true;
        observation.AssertOneChange();
        Assert.Equal(2, observation.InvalidatedCount);
    }

    [Theory]
    [InlineData("unknown-path-kind")]
    [InlineData("null-figures")]
    [InlineData("null-segments")]
    [InlineData("cycle")]
    public void SamePathTransitionsBetweenSupportedAndInvalidInvalidate(string failure)
    {
        var path = Path();
        var figures = path.Figures;
        var segments = figures[0].Segments;
        var clip = new PathSource(path);
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));

        InvalidatePath(path, failure);
        observation.AssertOneChange();

        path.Kind = PortableGeometryPathKind.Path;
        path.Figures = figures;
        figures[0].Segments = segments;
        path.PathA = path.PathB = null;
        observation.AssertOneChange();
        Assert.Equal(2, observation.InvalidatedCount);
    }

    [Fact]
    public void SignedZeroDoesNotCreateAnArtificialPrimitiveChange()
    {
        var clip = new PrimitiveSource(Rectangle(new(0, 0, 0, 0)));
        using var observation = new TrackerObservation(new LayoutRoot(() => clip));

        clip.Value = PortablePrimitiveGeometry.Rectangle(
            new(-0.0, -0.0, -0.0, -0.0), -0.0, -0.0, new(1, -0.0, -0.0, 1, -0.0, -0.0));

        observation.AssertIdle(8);
        Assert.Equal(0, observation.InvalidatedCount);
    }

    [Fact]
    public void SupportedPrimitiveDoesNotExportAnUnusedPath()
    {
        using var observation = new TrackerObservation(new LayoutRoot(() => new PrimitiveFirstSource()));

        observation.AssertIdle(16);

        Assert.Equal(0, observation.InvalidatedCount);
    }

    [Fact]
    public void LayoutClipPresenceAndNullTransitionsRemainObservable()
    {
        object? clip = null;
        var root = new LayoutRoot(() => clip);
        using var observation = new TrackerObservation(root);
        clip = new PrimitiveSource(Primitive(1));
        observation.AssertOneChange();
        clip = null;
        observation.AssertOneChange();
        root.HasLayoutClip = false;
        observation.AssertOneChange();
        clip = new PrimitiveSource(Primitive(2));
        observation.AssertIdle(8);
        root.HasLayoutClip = true;
        observation.AssertOneChange();
        Assert.Equal(4, observation.InvalidatedCount);
    }

    private static PortablePrimitiveGeometry Primitive(int sample) => sample switch
    {
        0 => WithTransform(0, PortableMatrix3x2.Identity),
        1 => Rectangle(new(3, 4, 30, 40)),
        2 => PortablePrimitiveGeometry.Rectangle(new(3, 4, 30, 40), 2, 3, PortableMatrix3x2.Identity),
        3 => WithTransform(2, PortableMatrix3x2.Identity),
        4 => Rectangle(new(0, 0, 0, 40)),
        5 => Rectangle(new(0, 0, 40, 0)),
        6 => Rectangle(new(0, 0, 0, 0)),
        7 => Rectangle(PortableRect.Empty),
        8 => WithTransform(1, new(1.25, 0.5, -0.25, 0.75, 3, -4)),
        9 => PortablePrimitiveGeometry.Line(new(3, 4), new(3, 4), PortableMatrix3x2.Identity),
        _ => throw new ArgumentOutOfRangeException(nameof(sample))
    };

    private static PortablePrimitiveGeometry Rectangle(PortableRect bounds) =>
        PortablePrimitiveGeometry.Rectangle(bounds, 0, 0, PortableMatrix3x2.Identity);

    private static PortablePrimitiveGeometry WithTransform(int kind, PortableMatrix3x2 transform) => kind switch
    {
        0 => PortablePrimitiveGeometry.Line(new(1, 2), new(7, 8), transform),
        1 => PortablePrimitiveGeometry.Rectangle(new(3, 4, 30, 40), 2, 3, transform),
        2 => PortablePrimitiveGeometry.Ellipse(new(5, 6), 3, 4, transform),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static PortableGeometryPath Path() => new()
    {
        Kind = PortableGeometryPathKind.Path,
        FillRule = PortableFillRule.Nonzero,
        Bounds = new(1, 2, 30, 40),
        Figures =
        [
            new PortablePathFigure
            {
                StartPoint = new(1, 2),
                IsClosed = true,
                IsFilled = true,
                Segments =
                [
                    PortablePathSegment.Line(new(10, 2), false, true),
                    PortablePathSegment.QuadraticBezier(new(12, 3), new(14, 5), false, true),
                    PortablePathSegment.CubicBezier(new(16, 7), new(18, 9), new(20, 11), false, true),
                    PortablePathSegment.Arc(new(1, 2), new(4, 5), 15, false, PortableSweepDirection.Clockwise, false, true)
                ]
            }
        ]
    };

    private static PortableGeometryPath CombinedPath()
    {
        var second = Path();
        second.Transform = new(1, 0, 0, 1, 20, 0);
        return new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Combined,
            PathA = Path(),
            PathB = second,
            CombineOperation = 2
        };
    }

    private static void MutatePath(PortableGeometryPath path, string change)
    {
        PortablePathFigure figure = path.Figures[0];
        PortablePathSegment[] segments = figure.Segments;
        switch (change)
        {
            case "fill-rule": path.FillRule = PortableFillRule.EvenOdd; break;
            case "transform": path.Transform = new(1, 0.25, 0, 1, 3, 4); break;
            case "bounds": path.Bounds = new(1, 2, 31, 40); break;
            case "figure-start": figure.StartPoint = new(2, 3); break;
            case "figure-closed": figure.IsClosed = false; break;
            case "figure-filled": figure.IsFilled = false; break;
            case "figure-count": path.Figures = [figure, new PortablePathFigure { StartPoint = new(50, 60) }]; break;
            case "segment-count": figure.Segments = [segments[0], segments[1], segments[2]]; break;
            case "line-point": segments[0] = PortablePathSegment.Line(new(11, 2), false, true); break;
            case "segment-kind": segments[0] = PortablePathSegment.QuadraticBezier(new(5, 1), new(10, 2), false, true); break;
            case "smooth-join": segments[0] = PortablePathSegment.Line(new(10, 2), true, true); break;
            case "stroked": segments[0] = PortablePathSegment.Line(new(10, 2), false, false); break;
            case "quadratic-control": segments[1] = PortablePathSegment.QuadraticBezier(new(13, 3), new(14, 5), false, true); break;
            case "quadratic-end": segments[1] = PortablePathSegment.QuadraticBezier(new(12, 3), new(15, 5), false, true); break;
            case "cubic-first-control": segments[2] = PortablePathSegment.CubicBezier(new(17, 7), new(18, 9), new(20, 11), false, true); break;
            case "cubic-second-control": segments[2] = PortablePathSegment.CubicBezier(new(16, 7), new(19, 9), new(20, 11), false, true); break;
            case "cubic-end": segments[2] = PortablePathSegment.CubicBezier(new(16, 7), new(18, 9), new(21, 11), false, true); break;
            default:
                segments[3] = PortablePathSegment.Arc(
                    change == "arc-end" ? new(2, 2) : new(1, 2),
                    new(change == "arc-width" ? 5 : 4, change == "arc-height" ? 6 : 5),
                    change == "arc-rotation" ? 16 : 15,
                    change == "arc-large",
                    change == "arc-sweep" ? PortableSweepDirection.Counterclockwise : PortableSweepDirection.Clockwise,
                    false, true);
                Assert.StartsWith("arc-", change);
                break;
        }
    }

    private static object Unsupported(string failure)
    {
        if (failure == "unknown-object") return new EqualButUnknownClip();
        if (failure == "failed-primitive") return new PrimitiveSource(Primitive(1)) { Available = false };
        if (failure == "failed-path") return new PathSource(Path()) { Available = false };
        var path = Path();
        if (failure == "over-depth")
        {
            for (int level = 0; level < 64; level++)
            {
                path = new PortableGeometryPath
                {
                    Kind = PortableGeometryPathKind.Combined,
                    PathA = path,
                    PathB = new PortableGeometryPath()
                };
            }
        }
        else if (failure == "over-budget")
        {
            path.Figures = new PortablePathFigure[65_537];
            Array.Fill(path.Figures, new PortablePathFigure());
        }
        else
        {
            InvalidatePath(path, failure);
        }
        return new PathSource(path);
    }

    private static void InvalidatePath(PortableGeometryPath path, string failure)
    {
        switch (failure)
        {
            case "unknown-path-kind": path.Kind = (PortableGeometryPathKind)99; break;
            case "null-figures": path.Figures = null!; break;
            case "null-segments": path.Figures[0].Segments = null!; break;
            case "cycle":
                path.Kind = PortableGeometryPathKind.Combined;
                path.Figures = Array.Empty<PortablePathFigure>();
                path.PathA = path;
                path.PathB = Path();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(failure));
        }
    }

    private sealed class LayoutRoot(Func<object?> clipFactory) : IPortableVisualLayoutStateSource
    {
        public int ReadCount { get; private set; }
        public bool HasLayoutClip { get; set; } = true;

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            ReadCount++;
            state = new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new(80, 60),
                HasLayoutClip = HasLayoutClip,
                LayoutClip = clipFactory()
            };
            return true;
        }
    }

    private sealed class PrimitiveSource(PortablePrimitiveGeometry value) : IPortablePrimitiveGeometrySource
    {
        public PortablePrimitiveGeometry Value { get; set; } = value;
        public bool Available { get; set; } = true;
        public bool TryGetPortablePrimitiveGeometry(out PortablePrimitiveGeometry geometry)
        {
            geometry = Value;
            return Available;
        }
    }

    private sealed class PathSource(PortableGeometryPath value) : IPortableGeometryPathSource
    {
        public bool Available { get; set; } = true;
        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = value;
            return Available;
        }
    }

    private sealed class PrimitiveFirstSource : IPortablePrimitiveGeometrySource, IPortableGeometryPathSource
    {
        public bool TryGetPortablePrimitiveGeometry(out PortablePrimitiveGeometry geometry)
        {
            geometry = Primitive(1);
            return true;
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path) =>
            throw new InvalidOperationException("A supported primitive must not export a path.");
    }

    private sealed class EqualButUnknownClip
    {
        public override bool Equals(object? obj) => obj is EqualButUnknownClip;
        public override int GetHashCode() => 0;
    }

    private sealed class TrackerObservation : IDisposable
    {
        private readonly LayoutRoot _root;
        private readonly WpfVisualInvalidationTracker _tracker = new();

        public TrackerObservation(LayoutRoot root)
        {
            _root = root;
            _tracker.Attach(root);
            Assert.True(_tracker.ConsumeDirty());
            _tracker.Invalidated += (_, _) => InvalidatedCount++;
            AssertIdle(2);
        }

        public int InvalidatedCount { get; private set; }

        public void AssertIdle(int polls)
        {
            int events = InvalidatedCount;
            for (int i = 0; i < polls; i++)
            {
                Assert.False(_tracker.DetectVersionChanges());
                Assert.False(_tracker.IsDirty);
                Assert.Null(_tracker.LastDirtySource);
                Assert.Empty(_tracker.DirtySources);
                // This is the public dirty-pass admission, not a rendered-frame counter.
                Assert.False(_tracker.ConsumeDirty());
            }
            Assert.Equal(events, InvalidatedCount);
        }

        public void AssertOneChange()
        {
            int events = InvalidatedCount;
            Assert.True(_tracker.DetectVersionChanges());
            Assert.True(_tracker.IsDirty);
            Assert.Same(_root, _tracker.LastDirtySource);
            Assert.Same(_root, Assert.Single(_tracker.DirtySources));
            Assert.Equal(events + 1, InvalidatedCount);
            Assert.True(_tracker.DetectVersionChanges());
            Assert.Equal(events + 1, InvalidatedCount);
            Assert.True(_tracker.ConsumeDirty());
            AssertIdle(4);
        }

        public void Dispose() => _tracker.Dispose();
    }
}
