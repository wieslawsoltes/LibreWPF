// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Tests;

public class PortableMediaPlayerSourceTests
{
    [Fact]
    public void AttachPublishesTypedFrameAndInvalidation()
    {
        var provider = new Provider();
        var player = new MediaPlayer();
        PortableMediaPlayerSourceFactory.Attach(player, provider);

        var frameSource = Assert.IsAssignableFrom<IPortableMediaPlayerSource>(
            player);
        Assert.True(frameSource.TryGetPortableMediaPlayerFrame(out var frame));
        Assert.Equal(64, frame.PixelWidth);
        Assert.Equal(32, frame.PixelHeight);
        Assert.Equal(7UL, frame.ContentVersion);
        Assert.Same(provider.NativeImage, frame.NativeImage);

        int invalidationCount = 0;
        var invalidation = Assert.IsAssignableFrom<IPortableInvalidationSource>(
            player);
        Assert.True(invalidation.TrySubscribeInvalidated(
            (_, _) => invalidationCount++,
            out IDisposable subscription));
        provider.Invalidate();
        Assert.Equal(1, invalidationCount);
        subscription.Dispose();
        provider.Invalidate();
        Assert.Equal(1, invalidationCount);
    }

    [Fact]
    public void DetachRemovesPortableFrameWithoutOpeningNativeMedia()
    {
        var player = new MediaPlayer();
        PortableMediaPlayerSourceFactory.Attach(player, new Provider());
        PortableMediaPlayerSourceFactory.Detach(player);

        Assert.False(((IPortableMediaPlayerSource)player)
            .TryGetPortableMediaPlayerFrame(out _));
        Assert.False(((IPortableInvalidationSource)player)
            .TrySubscribeInvalidated((_, _) => { }, out _));
    }

    private sealed class Provider :
        IPortableMediaPlayerSource,
        IPortableInvalidationSource
    {
        private event EventHandler? Invalidated;

        internal object NativeImage { get; } = new();

        public bool TryGetPortableMediaPlayerFrame(
            out PortableMediaPlayerFrame frame)
        {
            frame = new PortableMediaPlayerFrame(64, 32, 7, NativeImage);
            return true;
        }

        public bool TrySubscribeInvalidated(
            EventHandler handler,
            out IDisposable subscription)
        {
            Invalidated += handler;
            subscription = new PortableInvalidationSubscription(
                () => Invalidated -= handler);
            return true;
        }

        internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
