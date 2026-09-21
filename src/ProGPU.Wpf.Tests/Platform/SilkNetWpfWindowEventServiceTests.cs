using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfWindowEventServiceTests
{
    [Theory]
    [InlineData(true, WpfWindowEventKind.Activated)]
    [InlineData(false, WpfWindowEventKind.Deactivated)]
    public void CreateFocusChangedEventMapsFocusToActivation(bool isFocused, WpfWindowEventKind expected)
    {
        var args = SilkNetWpfWindowEventService.CreateFocusChangedEvent(isFocused);

        Assert.Equal(expected, args.Kind);
        Assert.Empty(args.Files);
    }

    [Fact]
    public void CreateFileDropEventStoresDroppedFiles()
    {
        var args = SilkNetWpfWindowEventService.CreateFileDropEvent(new[] { "/tmp/a.txt", "/tmp/b.txt" });

        Assert.Equal(WpfWindowEventKind.FilesDropped, args.Kind);
        Assert.Equal(new[] { "/tmp/a.txt", "/tmp/b.txt" }, args.Files);
    }

    [Fact]
    public void CreateFileDropEventHandlesNullFileList()
    {
        var args = SilkNetWpfWindowEventService.CreateFileDropEvent(null);

        Assert.Equal(WpfWindowEventKind.FilesDropped, args.Kind);
        Assert.Empty(args.Files);
    }

    [Fact]
    public void CreateWindowShownEventMapsToShown()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowShownEvent();

        Assert.Equal(WpfWindowEventKind.Shown, args.Kind);
    }

    [Fact]
    public void CreateWindowHiddenEventMapsToHidden()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowHiddenEvent();

        Assert.Equal(WpfWindowEventKind.Hidden, args.Kind);
    }

    [Fact]
    public void CreateWindowPositionChangingEventStoresCoordinates()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowPositionChangingEvent(-12, 34);

        Assert.Equal(WpfWindowEventKind.WindowPositionChanging, args.Kind);
        Assert.Equal(-12, args.Left);
        Assert.Equal(34, args.Top);
        Assert.Null(args.Width);
        Assert.Null(args.Height);
    }

    [Fact]
    public void CreateWindowPositionChangedEventStoresCoordinates()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowPositionChangedEvent(120, -45);

        Assert.Equal(WpfWindowEventKind.WindowPositionChanged, args.Kind);
        Assert.Equal(120, args.Left);
        Assert.Equal(-45, args.Top);
        Assert.Null(args.Width);
        Assert.Null(args.Height);
    }

    [Fact]
    public void CreateWindowSizeChangedEventStoresSize()
    {
        var args = SilkNetWpfWindowEventService.CreateWindowSizeChangedEvent(800, 600);

        Assert.Equal(WpfWindowEventKind.WindowSizeChanged, args.Kind);
        Assert.Null(args.Left);
        Assert.Null(args.Top);
        Assert.Equal(800, args.Width);
        Assert.Equal(600, args.Height);
    }

    [Fact]
    public void CreateNonClientMouseMoveEventStoresHitTestAndScreenCoordinates()
    {
        var args = SilkNetWpfWindowEventService.CreateNonClientMouseMoveEvent(2, -7, 42);

        Assert.Equal(WpfWindowEventKind.NonClientMouseMove, args.Kind);
        Assert.Equal(WpfMouseButton.None, args.Button);
        Assert.Equal(2, args.HitTestCode);
        Assert.Equal(-7, args.ScreenX);
        Assert.Equal(42, args.ScreenY);
    }

    [Fact]
    public void CreateNonClientMouseButtonEventStoresButtonHitTestAndScreenCoordinates()
    {
        var args = SilkNetWpfWindowEventService.CreateNonClientMouseButtonEvent(
            WpfWindowEventKind.NonClientMouseDown,
            WpfMouseButton.Right,
            2,
            300,
            -8);

        Assert.Equal(WpfWindowEventKind.NonClientMouseDown, args.Kind);
        Assert.Equal(WpfMouseButton.Right, args.Button);
        Assert.Equal(2, args.HitTestCode);
        Assert.Equal(300, args.ScreenX);
        Assert.Equal(-8, args.ScreenY);
    }

    [Fact]
    public void CreateNonClientMouseButtonEventRejectsNonButtonKinds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SilkNetWpfWindowEventService.CreateNonClientMouseButtonEvent(
                WpfWindowEventKind.NonClientMouseMove,
                WpfMouseButton.Left,
                2,
                0,
                0));
    }
}
