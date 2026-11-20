using System.Reflection;
using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class SilkNetWpfWindowEventServiceTests
{
    [Fact]
    public void ResizeThatMovedTheFrameRaisesPositionEventsBeforeSize()
    {
        var window = DispatchProxy.Create<IWindow, WindowEventProbe>();
        var probe = (WindowEventProbe)(object)window;
        probe.Position = new Vector2D<int>(100, 100);
        var service = new SilkNetWpfWindowEventService();
        var kinds = new List<WpfWindowEventKind>();
        var positions = new List<(int? Left, int? Top)>();
        service.WindowEventReceived += (_, e) =>
        {
            kinds.Add(e.Kind);
            if (e.Kind == WpfWindowEventKind.WindowPositionChanged) positions.Add((e.Left, e.Top));
        };

        using var subscription = service.Attach(window);
        probe.Position = new Vector2D<int>(0, 33);
        probe.Resize!(new Vector2D<int>(1728, 1010));

        Assert.Equal(
            new[]
            {
                WpfWindowEventKind.WindowPositionChanging,
                WpfWindowEventKind.WindowPositionChanged,
                WpfWindowEventKind.WindowSizeChanged,
            },
            kinds);
        Assert.Equal((0, 33), Assert.Single(positions));
    }

    [Fact]
    public void ResizeWithoutMovingOrAfterMoveEventRaisesOnlySize()
    {
        var window = DispatchProxy.Create<IWindow, WindowEventProbe>();
        var probe = (WindowEventProbe)(object)window;
        probe.Position = new Vector2D<int>(100, 100);
        var service = new SilkNetWpfWindowEventService();
        var kinds = new List<WpfWindowEventKind>();
        service.WindowEventReceived += (_, e) => kinds.Add(e.Kind);

        using var subscription = service.Attach(window);
        probe.Resize!(new Vector2D<int>(900, 600));
        Assert.Equal(new[] { WpfWindowEventKind.WindowSizeChanged }, kinds);

        kinds.Clear();
        probe.Position = new Vector2D<int>(40, 50);
        probe.Move!(probe.Position);
        probe.Resize!(new Vector2D<int>(901, 600));
        Assert.Equal(
            new[]
            {
                WpfWindowEventKind.WindowPositionChanging,
                WpfWindowEventKind.WindowPositionChanged,
                WpfWindowEventKind.WindowSizeChanged,
            },
            kinds);
    }

    public class WindowEventProbe : DispatchProxy
    {
        internal Vector2D<int> Position;
        internal Action<Vector2D<int>>? Move, Resize;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case "get_Position": return Position;
                case "add_Move": Move = (Action<Vector2D<int>>?)arguments![0]; return null;
                case "add_Resize": Resize = (Action<Vector2D<int>>?)arguments![0]; return null;
                case "remove_Move": Move = null; return null;
                case "remove_Resize": Resize = null; return null;
                default:
                    if (method.Name.StartsWith("add_", StringComparison.Ordinal) ||
                        method.Name.StartsWith("remove_", StringComparison.Ordinal)) return null;
                    throw new InvalidOperationException("Unexpected window access: " + method.Name);
            }
        }
    }
}
