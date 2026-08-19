using ProGPU.Wpf.Interop;
using Silk.NET.GLFW;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortableDisplayMetricsSourceTests
{
    [Fact]
    public void PhysicalMonitorCoordinatesAreConvertedToLogicalUnits()
    {
        var monitor = new WpfMonitorInfo("Primary", 0, 0, 3840, 2160, 2.0, true)
        {
            WorkAreaY = 48,
            WorkAreaWidth = 3840,
            WorkAreaHeight = 2112,
        };
        var source = CreateSource(monitor);

        Assert.True(source.TryGetDisplayMetrics(out PortableDisplayMetrics metrics));

        AssertRect(metrics.PrimaryScreen, 0, 0, 1920, 1080);
        AssertRect(metrics.PrimaryWorkArea, 0, 24, 1920, 1056);
        AssertRect(metrics.VirtualScreen, 0, 0, 1920, 1080);
    }

    [Fact]
    public void AlreadyLogicalMonitorCoordinatesAreNotScaledAgain()
    {
        var monitor = new WpfMonitorInfo("Primary", 0, 0, 1920, 1080, 2.0, true)
        {
            UsesLogicalCoordinates = true,
        };
        var source = CreateSource(monitor);

        Assert.True(source.TryGetDisplayMetrics(out PortableDisplayMetrics metrics));

        AssertRect(metrics.PrimaryScreen, 0, 0, 1920, 1080);
    }

    [Fact]
    public void VirtualScreenUnionsLogicalMonitorBounds()
    {
        var primary = new WpfMonitorInfo("Primary", 0, 0, 3840, 2160, 2.0, true);
        var secondary = new WpfMonitorInfo("Secondary", -1920, 0, 1920, 1080, 1.0, false);
        var source = CreateSource(primary, secondary);

        Assert.True(source.TryGetDisplayMetrics(out PortableDisplayMetrics metrics));

        AssertRect(metrics.VirtualScreen, -1920, 0, 3840, 1080);
    }

    [Fact]
    public void RecoverablePlatformQueryFailureReturnsFalse()
    {
        var source = new WpfPortableDisplayMetricsSource(
            () => new ThrowingMonitorService(new InvalidCastException("Simulated native platform callback conflict.")));

        Assert.False(source.TryGetDisplayMetrics(out PortableDisplayMetrics metrics));
        Assert.Equal(default, metrics);
    }

    [Fact]
    public void GlfwInitializationFailureReturnsFalse()
    {
        var source = new WpfPortableDisplayMetricsSource(
            () => new ThrowingMonitorService(new GlfwException("Simulated display connection failure.")));

        Assert.False(source.TryGetDisplayMetrics(out PortableDisplayMetrics metrics));
        Assert.Equal(default, metrics);
    }

    private static WpfPortableDisplayMetricsSource CreateSource(params WpfMonitorInfo[] monitors)
    {
        var service = new TestMonitorService(monitors);
        return new WpfPortableDisplayMetricsSource(() => service);
    }

    private static void AssertRect(
        PortableRect actual,
        double x,
        double y,
        double width,
        double height)
    {
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }

    private sealed class TestMonitorService(IReadOnlyList<WpfMonitorInfo> monitors) : IWpfMonitorService
    {
        public IReadOnlyList<WpfMonitorInfo> GetMonitors() => monitors;
    }

    private sealed class ThrowingMonitorService(Exception exception) : IWpfMonitorService
    {
        public IReadOnlyList<WpfMonitorInfo> GetMonitors()
        {
            throw exception;
        }
    }
}
