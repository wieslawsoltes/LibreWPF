using System;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfVisualTreeRendererTests
{
    [Theory]
    [InlineData(0)] // zero brush opacity
    [InlineData(1)] // zero fill extent
    [InlineData(2)] // exactly singular brush mapping
    [InlineData(3)] // opacity mask's zero-opacity shortcut
    [InlineData(4)] // glyph paint's zero-opacity shortcut
    [InlineData(5)] // degenerate ellipse
    public void NestedCacheShaderProofPrecedesNoPaintShortcuts(int route)
    {
        var target = InvalidNestedShaderCacheTarget();
        var descriptor = new PortableBitmapCacheBrush(target,
            Opacity: route is 0 or 3 or 4 ? 0 : 1,
            HasTransform: route == 2, Transform: new(0, 0, 0, 1, 0, 0));
        var brush = new CaptureBrush(descriptor);
        var commands = new global::ProGPU.Scene.DrawingContext();
        try
        {
            using var sink = new ProGpuCompositionCommandSink(commands);
            WpfDrawingReplayStatus Replay()
            {
                if (route == 3)
                {
                    bool pushed = ((IWpfBitmapCacheBrushCommandSink)sink).PushBitmapCacheBrushOpacityMask(
                        brush, new(0, 0, 8, 8), null);
                    if (pushed) sink.Pop();
                    return pushed ? WpfDrawingReplayStatus.Applied : WpfDrawingReplayStatus.Unsupported;
                }
                if (route == 4)
                    return ((IWpfBitmapCacheBrushCommandSink)sink).DrawBitmapCacheBrushGlyphRun(
                        brush, new object(), null)
                        ? WpfDrawingReplayStatus.Applied : WpfDrawingReplayStatus.Unsupported;
                if (route == 5)
                {
                    Assert.True(WpfDrawingReplay.TryReplaySourceBrushEllipseFill(brush,
                        new(4, 4), 0, 4, sink, null, out var status));
                    return status;
                }
                return WpfDrawingReplay.ReplayBitmapCacheBrushRectangleFill(brush,
                    new(0, 0, route == 1 ? 0 : 8, 8), sink, null);
            }

            Assert.Equal(WpfDrawingReplayStatus.Applied, Replay()); // existing ordinary no-paint behavior
            int count = commands.Commands.Count;
            using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true))
                Assert.Throws<NotSupportedException>(() => Replay());
            Assert.Equal(count, commands.Commands.Count); // no skipped paint was re-recorded
            Assert.False(WpfCaptureReplayGuard.IsActive);
            Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
            Assert.Equal(WpfDrawingReplayStatus.Applied, Replay());
        }
        finally { commands.Clear(); }
    }

    [Fact]
    public void NestedCacheShaderProofDoesNotTrustAnOrdinaryEmptyCachedPictureLease()
    {
        var target = InvalidNestedShaderCacheTarget();
        var inner = new CaptureBrush(new(target));
        // Actual ordinary source lookup captured no commands for empty bounds.
        // Keep its lease alive to exercise the reused entry, not cold creation.
        using var ordinary = WpfBitmapCacheBrushSourceLookup.Acquire(inner, null, null, null);
        Assert.Equal(0.0f, ordinary.Picture.Bounds.Width);
        var source = new FakePortableVisualStateDrawingVisual(
            new FakeGeometryDrawing(new PortableRect(0, 0, 8, 8), inner), new())
        { Bounds = new Rect(0, 0, 8, 8) };
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.CreateShaderSource(
            new CaptureBrush(new(source)), null, null, null));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
        using var stillOwned = WpfBitmapCacheBrushSourceLookup.Acquire(inner, null, null, null);
        Assert.Same(ordinary.Picture, stillOwned.Picture);

        target.Children.Clear();
        using var restored = WpfBitmapCacheBrushCapture.CreateShaderSource(
            new CaptureBrush(new(source)), null, null, null);
        Assert.Same(source, restored.Brush.InternalTarget);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    [Fact]
    public void NestedCacheShaderProofUsesTheAlreadyCapturedBrushDescriptor()
    {
        var source = new CaptureCacheSource(new(EmptyShaderCaptureVisual(), Opacity: 0));
        var sink = new NullVisualScopeSink();
        using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true))
            Assert.Equal(WpfDrawingReplayStatus.Applied,
                WpfDrawingReplay.ReplayBitmapCacheBrushRectangleFill(source,
                    new Rect(0, 0, 8, 8), sink, null));
        Assert.Equal(1, source.Reads);
        Assert.Equal(new[] { "PushSourceRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.VisualCacheBounds);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedCacheShaderProofValidatesSelectedPolicyEvenForNullOrZeroOpacity(bool nullTarget)
    {
        var brush = new CaptureBrush(new(nullTarget ? null : EmptyShaderCaptureVisual(),
            new CaptureCache(new(double.NaN, false, false)), Opacity: 0));
        var sink = new NullVisualScopeSink();
        using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true))
            Assert.Throws<NotSupportedException>(() => WpfDrawingReplay.ReplayBitmapCacheBrushRectangleFill(
                brush, new Rect(0, 0, 8, 8), sink, null));
        Assert.Empty(sink.Operations);
        Assert.Equal(WpfDrawingReplayStatus.Applied,
            WpfDrawingReplay.ReplayBitmapCacheBrushRectangleFill(brush, new Rect(0, 0, 8, 8), sink, null));
        Assert.Equal(new[] { "PushSourceRectangle", "Pop" }, sink.Operations);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    [Fact]
    public void NestedCacheShaderProofRejectsAnOpacityZeroSourceCycleAndRestoresGuard()
    {
        var inner = new EventCaptureBrush();
        var target = new FakePortableVisualStateDrawingVisual(
            new FakeGeometryDrawing(new PortableRect(0, 0, 8, 8), inner), new())
        { Bounds = new Rect(0, 0, 8, 8) };
        inner.Value = new(target, Opacity: 0);
        var outer = new CaptureBrush(new(target));
        Assert.Throws<InvalidOperationException>(() => WpfBitmapCacheBrushCapture.CreateShaderSource(
            outer, null, null, null));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);

        // The same source still follows its ordinary opacity-zero path.
        using var ordinary = WpfBitmapCacheBrushCapture.Create(outer);
        Assert.Same(target, ordinary.Brush.InternalTarget);
        inner.Value = new(EmptyShaderCaptureVisual(), Opacity: 0);
        using var restored = WpfBitmapCacheBrushCapture.CreateShaderSource(outer, null, null, null);
        Assert.Same(target, restored.Brush.InternalTarget);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    private static FakePortableVisualStateDrawingVisual InvalidNestedShaderCacheTarget()
    {
        var target = EmptyShaderCaptureVisual();
        target.Children.Add(new FakePortableVisualStateDrawingVisual(null, new()
        {
            HasVisibility = true, Visibility = PortableVisualVisibility.Hidden,
            HasEffect = true, Effect = new object()
        }) { Bounds = Rect.Empty });
        return target;
    }
}
