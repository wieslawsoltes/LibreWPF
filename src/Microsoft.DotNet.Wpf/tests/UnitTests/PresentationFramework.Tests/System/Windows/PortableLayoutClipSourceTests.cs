// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableLayoutClipSourceTests
{
    // These are source-producer contracts. The independent bridge suite consumes
    // the same typed values through its public invalidation tracker; this source
    // project must not import the bridge's different WPF implementation assemblies.
    [PortableLayoutClipTheory]
    [InlineData(false, "positive")]
    [InlineData(true, "positive")]
    [InlineData(false, "zero-width")]
    [InlineData(true, "zero-width")]
    [InlineData(false, "zero-height")]
    [InlineData(true, "zero-height")]
    [InlineData(false, "zero-both")]
    [InlineData(true, "zero-both")]
    [InlineData(false, "constrained-margin")]
    [InlineData(true, "constrained-margin")]
    [InlineData(false, "constrained-margin-rtl")]
    [InlineData(true, "constrained-margin-rtl")]
    public void ArrangedSourcePublishesFreshLayoutClipsWithStableTypedValues(bool border, string scenario)
    {
        RunInUiApartment(() =>
        {
            FrameworkElement element = CreateElement(border);
            var slot = new Size(80, 60);
            var renderSize = new Size(80, 60);
            var localRect = new Rect(0, 0, 80, 60);
            var bounds = localRect;
            PortableMatrix3x2 transform = PortableMatrix3x2.Identity;
            switch (scenario)
            {
                case "positive":
                    break;
                case "zero-width":
                    element.Width = 0;
                    slot = renderSize = new Size(0, 60);
                    localRect = bounds = new Rect(0, 0, 0, 60);
                    break;
                case "zero-height":
                    element.Height = 0;
                    slot = renderSize = new Size(80, 0);
                    localRect = bounds = new Rect(0, 0, 80, 0);
                    break;
                case "zero-both":
                    element.Width = element.Height = 0;
                    slot = renderSize = new Size(0, 0);
                    localRect = bounds = new Rect(0, 0, 0, 0);
                    break;
                case "constrained-margin":
                case "constrained-margin-rtl":
                    element.Margin = new Thickness(3, 5, 7, 11);
                    slot = new Size(50, 40);
                    // The 80x60 ink is right/bottom-aligned in a 40x24 client
                    // slot. Its local clip starts at (40,36), not at the margin.
                    localRect = bounds = new Rect(40, 36, 40, 24);
                    if (scenario == "constrained-margin-rtl")
                    {
                        element.FlowDirection = FlowDirection.RightToLeft;
                        transform = new PortableMatrix3x2(-1, 0, 0, 1, 80, 0);
                        bounds = new Rect(0, 36, 40, 24);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }

            Arrange(element, slot);
            AssertStableFreshClips(element, renderSize, localRect, bounds, transform);
        });
    }

    [PortableLayoutClipTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualResizeMarginAndFlowDirectionChangesPublishNewClipValues(bool border)
    {
        RunInUiApartment(() =>
        {
            FrameworkElement element = CreateElement(border);
            Arrange(element, new Size(80, 60));
            ClipValue original = AssertStableFreshClips(element, new Size(80, 60),
                new Rect(0, 0, 80, 60), new Rect(0, 0, 80, 60), PortableMatrix3x2.Identity);

            element.Width = 120;
            element.Height = 90;
            Arrange(element, new Size(120, 90));
            ClipValue resized = AssertStableFreshClips(element, new Size(120, 90),
                new Rect(0, 0, 120, 90), new Rect(0, 0, 120, 90), PortableMatrix3x2.Identity);
            Assert.NotEqual(original, resized);

            element.Margin = new Thickness(3, 5, 7, 11);
            Arrange(element, new Size(80, 60));
            ClipValue constrained = AssertStableFreshClips(element, new Size(120, 90),
                new Rect(50, 46, 70, 44), new Rect(50, 46, 70, 44), PortableMatrix3x2.Identity);
            Assert.NotEqual(resized, constrained);

            element.FlowDirection = FlowDirection.RightToLeft;
            Arrange(element, new Size(80, 60));
            ClipValue mirrored = AssertStableFreshClips(element, new Size(120, 90),
                new Rect(50, 46, 70, 44), new Rect(0, 46, 70, 44),
                new PortableMatrix3x2(-1, 0, 0, 1, 120, 0));
            Assert.NotEqual(constrained, mirrored);

            // Captures contain only immutable values, never retained mutable
            // source DTO/figure/segment arrays that could change this baseline.
            Assert.Equal(new PortableRect(0, 0, 80, 60), original.Primitive.Rect);
            Assert.Equal(new PortablePoint(80, 0), original.First.Point1);
            Assert.Equal(PortableMatrix3x2.Identity, original.Transform);
        });
    }

    [PortableLayoutClipTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipToBoundsTogglePreservesActualClipPresence(bool border)
    {
        RunInUiApartment(() =>
        {
            FrameworkElement element = CreateElement(border);
            element.ClipToBounds = false;
            Arrange(element, new Size(80, 60));
            AssertNoClip(element);

            element.ClipToBounds = true;
            Arrange(element, new Size(80, 60));
            AssertStableFreshClips(element, new Size(80, 60),
                new Rect(0, 0, 80, 60), new Rect(0, 0, 80, 60), PortableMatrix3x2.Identity);

            element.ClipToBounds = false;
            Arrange(element, new Size(80, 60));
            AssertNoClip(element);
        });
    }

    private static FrameworkElement CreateElement(bool border)
    {
        Assert.Equal(PortableWpfMediaBackend.Portable, PortableWpfRuntime.ConfiguredMediaBackend);
        FrameworkElement element = border ? new Border() : new FrameworkElement();
        element.Width = 80;
        element.Height = 60;
        element.ClipToBounds = true;
        element.HorizontalAlignment = HorizontalAlignment.Right;
        element.VerticalAlignment = VerticalAlignment.Bottom;
        return element;
    }

    private static void Arrange(FrameworkElement element, Size slot)
    {
        element.Measure(slot);
        element.Arrange(new Rect(new Point(), slot));
        Assert.True(element.IsMeasureValid);
        Assert.True(element.IsArrangeValid);
    }

    private static ClipValue AssertStableFreshClips(FrameworkElement element, Size renderSize,
        Rect localRect, Rect bounds, PortableMatrix3x2 transform)
    {
        ClipValue baseline = ReadClip(element, renderSize, localRect, bounds, transform, out Geometry first);
        Geometry previous = first;
        for (int poll = 0; poll < 8; poll++)
        {
            ClipValue current = ReadClip(element, renderSize, localRect, bounds, transform, out Geometry geometry);
            Assert.NotSame(first, geometry);
            Assert.NotSame(previous, geometry);
            Assert.Equal(baseline, current);
            previous = geometry;
        }
        return baseline;
    }

    private static ClipValue ReadClip(FrameworkElement element, Size renderSize,
        Rect localRect, Rect bounds, PortableMatrix3x2 transform, out Geometry geometry)
    {
        Assert.True(((IPortableVisualLayoutStateSource)element).TryGetPortableVisualLayoutState(out var state));
        Assert.True(state.HasRenderSize);
        Assert.Equal(new PortableSize(renderSize.Width, renderSize.Height), state.RenderSize);
        Assert.Equal(renderSize, element.RenderSize);
        Assert.True(state.HasClipToBounds);
        Assert.True(state.ClipToBounds);
        Assert.True(state.HasLayoutClip);
        geometry = Assert.IsType<RectangleGeometry>(state.LayoutClip);

        Assert.True(((IPortablePrimitiveGeometrySource)geometry).TryGetPortablePrimitiveGeometry(out var primitive));
        Assert.Equal(PortablePrimitiveGeometryKind.Rectangle, primitive.Kind);
        Assert.Equal(ToPortableRect(localRect), primitive.Rect);
        Assert.False(primitive.Rect.IsEmpty); // Zero extents are not the Empty sentinel.
        Assert.Equal(0, primitive.RadiusX);
        Assert.Equal(0, primitive.RadiusY);
        Assert.Equal(transform, primitive.Transform);

        Assert.True(((IPortableGeometryPathSource)geometry).TryGetPortableGeometryPath(out var path));
        Assert.Equal(PortableGeometryPathKind.Path, path.Kind);
        Assert.Equal(PortableFillRule.EvenOdd, path.FillRule);
        Assert.Equal(ToPortableRect(bounds), path.Bounds);
        Assert.Equal(transform, path.Transform);
        Assert.Null(path.PathA);
        Assert.Null(path.PathB);
        Assert.Equal(0, path.CombineOperation);
        PortablePathFigure figure = Assert.Single(path.Figures);
        Assert.Equal(new PortablePoint(localRect.Left, localRect.Top), figure.StartPoint);
        Assert.True(figure.IsClosed);
        Assert.True(figure.IsFilled);
        Assert.Equal(3, figure.Segments.Length);
        Assert.Equal(PortablePathSegment.Line(new PortablePoint(localRect.Right, localRect.Top), false, true), figure.Segments[0]);
        Assert.Equal(PortablePathSegment.Line(new PortablePoint(localRect.Right, localRect.Bottom), false, true), figure.Segments[1]);
        Assert.Equal(PortablePathSegment.Line(new PortablePoint(localRect.Left, localRect.Bottom), false, true), figure.Segments[2]);

        return new ClipValue(primitive, path.Kind, path.FillRule, path.Transform, path.Bounds,
            figure.StartPoint, figure.IsClosed, figure.IsFilled,
            figure.Segments[0], figure.Segments[1], figure.Segments[2]);
    }

    private static void AssertNoClip(FrameworkElement element)
    {
        for (int poll = 0; poll < 8; poll++)
        {
            Assert.True(((IPortableVisualLayoutStateSource)element).TryGetPortableVisualLayoutState(out var state));
            Assert.True(state.HasRenderSize);
            Assert.Equal(new PortableSize(80, 60), state.RenderSize);
            Assert.True(state.HasClipToBounds);
            Assert.False(state.ClipToBounds);
            Assert.False(state.HasLayoutClip);
            Assert.Null(state.LayoutClip);
        }
    }

    private static PortableRect ToPortableRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private readonly record struct ClipValue(
        PortablePrimitiveGeometry Primitive, PortableGeometryPathKind Kind, PortableFillRule FillRule,
        PortableMatrix3x2 Transform, PortableRect Bounds, PortablePoint Start, bool Closed, bool Filled,
        PortablePathSegment First, PortablePathSegment Second, PortablePathSegment Third);

    private sealed class PortableLayoutClipTheoryAttribute : TheoryAttribute
    {
        public PortableLayoutClipTheoryAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before source layout initialization, including on Windows.";
        }
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Layout clip source fixture timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
