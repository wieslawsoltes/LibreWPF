// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Tests;

public sealed class PortableBitmapCacheBrushTests
{
    [Fact]
    public void SnapshotUsesHostWrapperInsteadOfPublicTarget()
    {
        var target = new DrawingVisual();
        var brush = new BitmapCacheBrush(target) { AutoWrapTarget = true };
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var state));
        Assert.Same(brush.InternalTarget, state.InternalTarget);
        Assert.NotSame(target, state.InternalTarget);
        Assert.Same(target, brush.Target);
        brush.AutoWrapTarget = false;
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out state));
        Assert.Same(target, state.InternalTarget);
    }

    [Fact]
    public void SnapshotPreservesResolvedTargetCacheAndNullTarget()
    {
        var target = new DrawingVisual();
        var cache = new BitmapCache(2);
        var brush = new BitmapCacheBrush(target) { BitmapCache = cache };
        var source = (IPortableBitmapCacheBrushSource)brush;
        Assert.True(source.TryGetPortableBitmapCacheBrush(out var state));
        Assert.Same(target, state.InternalTarget);
        Assert.Same(cache, state.BitmapCache);
        Assert.Equal(1, state.Opacity);
        Assert.False(state.HasTransform);
        Assert.False(state.HasRelativeTransform);
        brush.Target = null;
        Assert.True(source.TryGetPortableBitmapCacheBrush(out state));
        Assert.Null(state.InternalTarget);
        Assert.Same(cache, state.BitmapCache);
    }
}
