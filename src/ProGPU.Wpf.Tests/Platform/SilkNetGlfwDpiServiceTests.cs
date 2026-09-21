using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetGlfwDpiServiceTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.5)]
    [InlineData(1.3333333, 1.6666667)]
    public void TryNormalizeContentScaleAcceptsFinitePositiveScales(double scaleX, double scaleY)
    {
        bool normalized = SilkNetGlfwDpiService.TryNormalizeContentScale(scaleX, scaleY, out WpfDeviceScale scale);

        Assert.True(normalized);
        Assert.Equal(Math.Round(scaleX, 4), scale.X);
        Assert.Equal(Math.Round(scaleY, 4), scale.Y);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(9.0, 1.0)]
    public void TryNormalizeContentScaleRejectsInvalidScales(double scaleX, double scaleY)
    {
        Assert.False(SilkNetGlfwDpiService.TryNormalizeContentScale(scaleX, scaleY, out _));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void UsesMonitorScaledWindowCoordinatesMatchesGlfwBackendBehavior(
        bool hintsConfigured,
        bool hasX11Window,
        bool hasWin32Window,
        bool expected)
    {
        Assert.Equal(
            expected,
            SilkNetGlfwDpiService.UsesMonitorScaledWindowCoordinates(
                hintsConfigured,
                hasX11Window,
                hasWin32Window));
    }
}
