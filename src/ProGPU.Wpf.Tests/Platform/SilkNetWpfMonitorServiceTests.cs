using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfMonitorServiceTests
{
    [Fact]
    public void GetMonitorsConfiguresPlatformBeforeQueryingSilk()
    {
        bool configured = false;
        var service = new SilkNetWpfMonitorService(
            () =>
            {
                Assert.True(configured);
                return Array.Empty<IMonitor>();
            },
            () =>
            {
                Assert.True(configured);
                return null;
            },
            getDpiScale: null,
            getWorkArea: null,
            configureBeforeMonitorQuery: () => configured = true);

        Assert.Empty(service.GetMonitors());
    }

    [Fact]
    public void GetMonitorsMapsSilkMonitorBounds()
    {
        var primary = new FakeMonitor(
            "Primary",
            0,
            new Rectangle<int>(10, 20, 1920, 1080),
            new VideoMode(new Vector2D<int>(1920, 1080), 60));
        var secondary = new FakeMonitor(
            "Secondary",
            1,
            new Rectangle<int>(1930, 20, 1280, 720),
            new VideoMode(new Vector2D<int>(1280, 720), 60));
        var service = new SilkNetWpfMonitorService(
            () => new IMonitor[] { primary, secondary },
            () => primary);

        var monitors = service.GetMonitors();

        Assert.Equal(2, monitors.Count);
        Assert.Equal(new WpfMonitorInfo("Primary", 10, 20, 1920, 1080, 1.0, true), monitors[0]);
        Assert.Equal(new WpfMonitorInfo("Secondary", 1930, 20, 1280, 720, 1.0, false), monitors[1]);
    }

    [Fact]
    public void GetMonitorsFallsBackToVideoModeResolutionWhenBoundsSizeIsUnavailable()
    {
        var monitor = new FakeMonitor(
            "Headless",
            0,
            new Rectangle<int>(0, 0, 0, 0),
            new VideoMode(new Vector2D<int>(1024, 768), 60));
        var service = new SilkNetWpfMonitorService(
            () => new IMonitor[] { monitor },
            () => monitor);

        var mapped = Assert.Single(service.GetMonitors());

        Assert.Equal(1024, mapped.Width);
        Assert.Equal(768, mapped.Height);
        Assert.True(mapped.IsPrimary);
    }

    [Fact]
    public void GetMonitorsDerivesDpiScaleFromVideoModeResolutionAndBounds()
    {
        var monitor = new FakeMonitor(
            "HiDpi",
            0,
            new Rectangle<int>(0, 0, 1920, 1080),
            new VideoMode(new Vector2D<int>(3840, 2160), 60));
        var service = new SilkNetWpfMonitorService(
            () => new IMonitor[] { monitor },
            () => monitor);

        var mapped = Assert.Single(service.GetMonitors());

        Assert.Equal(2.0, mapped.DpiScale);
    }

    [Fact]
    public void ToMonitorInfoUsesTypedDpiScaleProvider()
    {
        var monitor = new FakeMonitor(
            "Scaled",
            0,
            new Rectangle<int>(0, 0, 1920, 1080),
            new VideoMode(new Vector2D<int>(1920, 1080), 60));

        var mapped = SilkNetWpfMonitorService.ToMonitorInfo(monitor, monitor, _ => 1.75);

        Assert.Equal(1.75, mapped.DpiScale);
        Assert.False(mapped.UsesLogicalCoordinates);
    }

    [Fact]
    public void GetMonitorsUsesTypedDpiScaleProvider()
    {
        var monitor = new FakeMonitor(
            "Dpi",
            0,
            new Rectangle<int>(0, 0, 1920, 1080),
            new VideoMode(new Vector2D<int>(1920, 1080), 60));
        var service = new SilkNetWpfMonitorService(
            () => new IMonitor[] { monitor },
            () => monitor,
            _ => 1.75);

        var mapped = Assert.Single(service.GetMonitors());

        Assert.Equal(1.75, mapped.DpiScale);
    }

    [Fact]
    public void ToMonitorInfoFallsBackToResolutionRatioWhenReflectedScaleIsInvalid()
    {
        var monitor = new FakeMonitor(
            "InvalidScale",
            0,
            new Rectangle<int>(0, 0, 1920, 1080),
            new VideoMode(new Vector2D<int>(3840, 2160), 60));

        var mapped = SilkNetWpfMonitorService.ToMonitorInfo(monitor, monitor, _ => 0);

        Assert.Equal(2.0, mapped.DpiScale);
        Assert.True(mapped.UsesLogicalCoordinates);
    }

    [Fact]
    public void ToMonitorInfoUsesTypedWorkAreaProvider()
    {
        var monitor = new FakeMonitor(
            "WorkArea",
            0,
            new Rectangle<int>(0, 0, 3840, 2160),
            new VideoMode(new Vector2D<int>(3840, 2160), 60));

        var mapped = SilkNetWpfMonitorService.ToMonitorInfo(
            monitor,
            monitor,
            _ => 2.0,
            _ => new Rectangle<int>(0, 48, 3840, 2112));

        Assert.Equal(0, mapped.WorkAreaX);
        Assert.Equal(48, mapped.WorkAreaY);
        Assert.Equal(3840, mapped.WorkAreaWidth);
        Assert.Equal(2112, mapped.WorkAreaHeight);
        Assert.False(mapped.UsesLogicalCoordinates);
    }

    private sealed class FakeMonitor : IMonitor
    {
        public FakeMonitor(string name, int index, Rectangle<int> bounds, VideoMode videoMode)
        {
            Name = name;
            Index = index;
            Bounds = bounds;
            VideoMode = videoMode;
        }

        public string Name { get; }

        public int Index { get; }

        public Rectangle<int> Bounds { get; }

        public VideoMode VideoMode { get; }

        public float Gamma { get; set; } = 1.0f;

        public IEnumerable<VideoMode> GetAllVideoModes()
        {
            return new[] { VideoMode };
        }

        public IWindow CreateWindow(WindowOptions opts)
        {
            throw new NotSupportedException();
        }
    }
}
