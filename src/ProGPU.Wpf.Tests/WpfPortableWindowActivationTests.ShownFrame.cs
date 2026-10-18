using System;
using System.Windows.Media.ProGPU;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class WpfPortableWindowActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShownNativeFramePreservesSourceOuterSize(bool sizeToContent)
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow
        {
            Width = sizeToContent ? double.NaN : 980,
            Height = sizeToContent ? double.NaN : 640,
            ActualWidth = 980, ActualHeight = 640
        };
        var source = new FakePortablePresentationSource();
        Assert.True(WpfPortableWindowActivation.TryAttach(host, window, source, out var activation));
        activation!.SynchronizeShownWindowClientSize(null);
        Assert.Equal(640, host.Height);
        Assert.Equal(0, source.ClientSizeChangeCount);

        activation.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(0, 39, 0, 0));
        Assert.Equal(980, host.Width);
        Assert.Equal(601, host.Height);
        Assert.Equal(980, source.ClientWidth);
        Assert.Equal(601, source.ClientHeight);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.Equal(980, window.ActualWidth);
        Assert.Equal(640, window.ActualHeight);
        Assert.Equal(sizeToContent ? double.NaN : 640, window.Height);

        activation.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(0, 39, 0, 0));
        Assert.Equal(1, source.ClientSizeChangeCount);
    }

    [Fact]
    public void ShownNativeFrameUsesBothAxesAndPreservesUnavailableDimension()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions { Width = 700 });
        var window = new FakeWindow { Height = 640 };
        var source = new FakePortablePresentationSource();
        Assert.True(WpfPortableWindowActivation.TryAttach(host, window, source, out var activation));
        activation!.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(2, 35, 3, 4));
        Assert.Equal(700, host.Width);
        Assert.Equal(601, host.Height);
        window.Width = 980;
        activation.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(2, 35, 3, 4));
        Assert.Equal(975, host.Width);
        Assert.Equal(601, host.Height);
    }

    [Fact]
    public void ShownEmptyOrInvalidFrameDoesNotInventDecorations()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow { Width = 980, Height = 640 };
        var source = new FakePortablePresentationSource();
        Assert.True(WpfPortableWindowActivation.TryAttach(host, window, source, out var activation));
        activation!.SynchronizeShownWindowClientSize(PortableWindowFrameInsets.Empty);
        activation.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(0, -39, 0, 0));
        activation.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(0, double.NaN, 0, 0));
        Assert.Equal(980, host.Width);
        Assert.Equal(640, host.Height);
        Assert.Equal(0, source.ClientSizeChangeCount);
    }

    [Theory]
    [InlineData(ProGpuWpfWindowState.Maximized)]
    [InlineData(ProGpuWpfWindowState.Minimized)]
    public void ShownFrameDoesNotResizeNonNormalState(ProGpuWpfWindowState state)
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow { Width = 980, Height = 640 };
        var source = new FakePortablePresentationSource();
        Assert.True(WpfPortableWindowActivation.TryAttach(host, window, source, out var activation));
        host.SetWindowState(state);
        activation!.SynchronizeShownWindowClientSize(new PortableWindowFrameInsets(0, 39, 0, 0));
        Assert.Equal(640, host.Height);
        Assert.Equal(0, source.ClientSizeChangeCount);
    }
}
