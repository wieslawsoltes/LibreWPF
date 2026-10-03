using System;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfVisualTreeRendererTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyCacheShaderValidatesActualAttachedSourceAndRejectsRefill(int boundsKind)
    {
        var target = EmptyShaderCaptureVisual();
        target.Bounds = boundsKind switch
        {
            1 => new Rect(7, 9, 0, 12),
            2 => new Rect(7, 9, 12, 0),
            _ => Rect.Empty
        };
        var parent = new FakePortableVisualStateVisual(new PortableVisualState());
        parent.Children.Add(target);
        var cache = new CaptureCache(new(2, true, false));
        var source = new CaptureBrush(new(target, cache, Opacity: 0.5));

        WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source);
        Assert.Same(target, parent.Children[0]);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);

        target.Bounds = new Rect(7, 9, 12, 6);
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
        target.Bounds = Rect.Empty;
        WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source);
        Assert.Same(target, parent.Children[0]);
    }

    [Fact]
    public void EmptyCacheShaderRequiresAuthoritativeBoundsAndNonnullTypedTarget()
    {
        var missingBounds = new FakePortableVisualStateDrawingVisual(null, new PortableVisualState());
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(missingBounds))));
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(null))));
        var missingState = new FakeDrawingVisual(null) { Bounds = Rect.Empty };
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(missingState))));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void EmptyCacheShaderValidatesHiddenChildrenEvenWithZeroCacheScale(double scale)
    {
        var root = EmptyShaderCaptureVisual();
        var child = new FakePortableVisualStateDrawingVisual(null, new PortableVisualState
        {
            HasVisibility = true, Visibility = PortableVisualVisibility.Hidden,
            HasEffect = true, Effect = new object()
        }) { Bounds = Rect.Empty };
        root.Children.Add(child);
        var source = new CaptureBrush(new(root, new CaptureCache(new(scale, false, false))));
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
        Assert.True(child.TryGetPortableVisualState(out var unchanged));
        Assert.Equal(PortableVisualVisibility.Hidden, unchanged.Visibility);

        // The hidden-validation flag must not leak into ordinary replay.
        var sink = new TestSink();
        var replay = new WpfVisualTreeRenderer().ReplayBitmapCacheBrushSource(root, sink);
        Assert.Equal(0, replay.UnsupportedVisualStateCount);
        Assert.Empty(sink.VisualEffects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCacheShaderRejectsHiddenCyclesAndRepeatedParents(bool repeatedParent)
    {
        var root = EmptyShaderCaptureVisual();
        var hidden = new FakePortableVisualStateDrawingVisual(null, new PortableVisualState
        {
            HasVisibility = true, Visibility = PortableVisualVisibility.Collapsed
        }) { Bounds = Rect.Empty };
        root.Children.Add(hidden);
        if (repeatedParent)
        {
            var shared = EmptyShaderCaptureVisual();
            root.Children.Add(shared);
            hidden.Children.Add(shared);
        }
        else
        {
            hidden.Children.Add(root);
        }
        Assert.Throws<InvalidOperationException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(root))));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
        WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(new CaptureBrush(new(EmptyShaderCaptureVisual())));
    }

    [Fact]
    public void EmptyCacheShaderPreservesOuterRootExclusionsButRejectsDescendantState()
    {
        var state = new PortableVisualState
        {
            HasOffset = true, Offset = new PortablePoint(500, 400),
            HasTransform = true, Transform = new object(),
            HasClip = true, Clip = new object(),
            HasOpacity = true, Opacity = 0.01,
            HasOpacityMask = true, OpacityMask = new object(),
            HasEffect = true, Effect = new object()
        };
        var root = new FakePortableVisualStateDrawingVisual(null, state) { Bounds = Rect.Empty };
        var source = new CaptureBrush(new(root));
        WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source);
        var child = new FakePortableVisualStateDrawingVisual(null, new PortableVisualState
        {
            HasEffect = true, Effect = state.Effect
        }) { Bounds = Rect.Empty };
        root.Children.Add(child);
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source));
        Assert.True(root.TryGetPortableVisualState(out var after));
        Assert.Same(state, after);
        Assert.Equal(new PortablePoint(500, 400), after.Offset);
        Assert.Equal(0.01, after.Opacity);
        Assert.Same(state.Effect, after.Effect);
    }

    [Fact]
    public void EmptyCacheShaderRejectsInvalidSelectedCacheWithoutDefaulting()
    {
        var target = EmptyShaderCaptureVisual();
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(target, new object()))));
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(target, new CaptureCache(new(double.NaN, false, false))))));
        var targetWithInvalidCache = new FakePortableVisualStateDrawingVisual(null, new PortableVisualState
        {
            HasCacheMode = true, CacheMode = new object()
        }) { Bounds = Rect.Empty };
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(targetWithInvalidCache))));
        // Explicit valid selection is authoritative over the excluded root cache.
        WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(new CaptureBrush(new(targetWithInvalidCache,
            new CaptureCache(new(1, false, false)))));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void EmptyCacheShaderRejectsInvalidBrushOpacity(double opacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(EmptyShaderCaptureVisual(), Opacity: opacity))));
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    [Fact]
    public void EmptyCacheShaderRejectsHidden3DWithoutRequestingSceneOrDevice()
    {
        var root = EmptyShaderCaptureVisual();
        var viewport = new EmptyShaderViewport { Bounds = Rect.Empty };
        root.Children.Add(viewport);
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(
            new CaptureBrush(new(root))));
        Assert.Equal(0, viewport.SceneReads);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    [Fact]
    public void OrdinaryEmptyCacheCaptureKeepsItsExistingSkipAfterShaderValidationFailure()
    {
        var root = EmptyShaderCaptureVisual();
        root.Children.Add(new FakeDrawingVisual(null)); // No typed visual state.
        var source = new CaptureBrush(new(root));
        using (var ordinary = WpfBitmapCacheBrushCapture.Create(source))
            Assert.Equal(0, ordinary.Picture.CommandCount);
        Assert.Throws<NotSupportedException>(() => WpfBitmapCacheBrushCapture.ValidateEmptyShaderSource(source));
        using var restored = WpfBitmapCacheBrushCapture.Create(source);
        Assert.Equal(0, restored.Picture.CommandCount);
        Assert.Same(root, restored.Brush.InternalTarget);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
    }

    private static FakePortableVisualStateDrawingVisual EmptyShaderCaptureVisual() =>
        new(null, new PortableVisualState()) { Bounds = Rect.Empty };

    private sealed class EmptyShaderViewport : FakeVisual, IPortableVisualStateSource, IPortableViewport3DSceneSource
    {
        public int SceneReads { get; private set; }
        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = new PortableVisualState { HasVisibility = true, Visibility = PortableVisualVisibility.Hidden };
            return true;
        }
        public bool TryGetPortableViewport3DScene(out PortableViewport3DScene scene)
        {
            SceneReads++;
            scene = default!;
            return false;
        }
    }
}
