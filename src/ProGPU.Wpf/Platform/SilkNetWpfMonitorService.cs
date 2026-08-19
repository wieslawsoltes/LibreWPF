using System;
using System.Collections.Generic;
using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfMonitorService : IWpfMonitorService
{
    private readonly Func<IEnumerable<IMonitor>> _getMonitors;
    private readonly Func<IMonitor?> _getMainMonitor;
    private readonly Func<IMonitor, double?>? _getDpiScale;
    private readonly Func<IMonitor, Rectangle<int>?>? _getWorkArea;
    private readonly Action _configureBeforeMonitorQuery;

    public SilkNetWpfMonitorService()
        : this(
            GetDefaultMonitors,
            GetDefaultMainMonitor,
            TryGetGlfwMonitorContentScale,
            TryGetGlfwMonitorWorkArea,
            static () => SilkNetGlfwPlatformSelector.ConfigureBeforeFirstGlfwUse())
    {
    }

    public SilkNetWpfMonitorService(IWindowPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);

        _getMonitors = platform.GetMonitors;
        _getMainMonitor = platform.GetMainMonitor;
        _getDpiScale = TryGetGlfwMonitorContentScale;
        _getWorkArea = TryGetGlfwMonitorWorkArea;
        _configureBeforeMonitorQuery = static () => { };
    }

    public SilkNetWpfMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor,
        Func<IMonitor, double?>? getDpiScale = null,
        Func<IMonitor, Rectangle<int>?>? getWorkArea = null)
        : this(getMonitors, getMainMonitor, getDpiScale, getWorkArea, static () => { })
    {
    }

    internal SilkNetWpfMonitorService(
        Func<IEnumerable<IMonitor>> getMonitors,
        Func<IMonitor?> getMainMonitor,
        Func<IMonitor, double?>? getDpiScale,
        Func<IMonitor, Rectangle<int>?>? getWorkArea,
        Action configureBeforeMonitorQuery)
    {
        _getMonitors = getMonitors ?? throw new ArgumentNullException(nameof(getMonitors));
        _getMainMonitor = getMainMonitor ?? throw new ArgumentNullException(nameof(getMainMonitor));
        _getDpiScale = getDpiScale;
        _getWorkArea = getWorkArea;
        _configureBeforeMonitorQuery = configureBeforeMonitorQuery
            ?? throw new ArgumentNullException(nameof(configureBeforeMonitorQuery));
    }

    public IReadOnlyList<WpfMonitorInfo> GetMonitors()
    {
        _configureBeforeMonitorQuery();
        var mainMonitor = _getMainMonitor();
        var monitors = _getMonitors();
        var mapped = monitors is ICollection<IMonitor> monitorCollection
            ? new List<WpfMonitorInfo>(monitorCollection.Count)
            : new List<WpfMonitorInfo>();

        foreach (var monitor in monitors)
        {
            mapped.Add(ToMonitorInfo(monitor, mainMonitor, _getDpiScale, _getWorkArea));
        }

        return mapped;
    }

    public static WpfMonitorInfo ToMonitorInfo(IMonitor monitor, IMonitor? mainMonitor)
    {
        return ToMonitorInfo(monitor, mainMonitor, getDpiScale: null);
    }

    public static WpfMonitorInfo ToMonitorInfo(
        IMonitor monitor,
        IMonitor? mainMonitor,
        Func<IMonitor, double?>? getDpiScale,
        Func<IMonitor, Rectangle<int>?>? getWorkArea = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var bounds = monitor.Bounds;
        var width = bounds.Size.X;
        var height = bounds.Size.Y;

        if ((width <= 0 || height <= 0) && monitor.VideoMode.Resolution is Vector2D<int> resolution)
        {
            width = resolution.X;
            height = resolution.Y;
        }

        var workArea = getWorkArea?.Invoke(monitor) ?? bounds;
        bool usesLogicalCoordinates = MonitorBoundsAreLogical(monitor, width, height);

        return new WpfMonitorInfo(
            monitor.Name,
            bounds.Origin.X,
            bounds.Origin.Y,
            Math.Max(0, width),
            Math.Max(0, height),
            ResolveDpiScale(monitor, width, height, getDpiScale?.Invoke(monitor)),
            IsPrimary: ReferenceEquals(monitor, mainMonitor) || monitor.Index == mainMonitor?.Index)
        {
            WorkAreaX = workArea.Origin.X,
            WorkAreaY = workArea.Origin.Y,
            WorkAreaWidth = Math.Max(0, workArea.Size.X),
            WorkAreaHeight = Math.Max(0, workArea.Size.Y),
            UsesLogicalCoordinates = usesLogicalCoordinates,
        };
    }

    internal static bool MonitorBoundsAreLogical(IMonitor monitor, int boundsWidth, int boundsHeight)
    {
        return boundsWidth > 0
            && boundsHeight > 0
            && monitor.VideoMode.Resolution is Vector2D<int> resolution
            && resolution.X > 0
            && resolution.Y > 0
            && (resolution.X != boundsWidth || resolution.Y != boundsHeight);
    }

    internal static double ResolveDpiScale(IMonitor monitor, int boundsWidth, int boundsHeight)
    {
        return ResolveDpiScale(monitor, boundsWidth, boundsHeight, explicitScale: null);
    }

    internal static double ResolveDpiScale(
        IMonitor monitor,
        int boundsWidth,
        int boundsHeight,
        double? explicitScale)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        if (explicitScale is double scale && IsUsableScale(scale))
        {
            return NormalizeScale(scale);
        }

        if (boundsWidth > 0
            && boundsHeight > 0
            && monitor.VideoMode.Resolution is Vector2D<int> resolution
            && resolution.X > 0
            && resolution.Y > 0)
        {
            var scaleX = resolution.X / (double)boundsWidth;
            var scaleY = resolution.Y / (double)boundsHeight;
            if (IsUsableScale(scaleX) && IsUsableScale(scaleY))
            {
                return NormalizeScale((scaleX + scaleY) / 2);
            }
        }

        return 1.0;
    }

    private static bool IsUsableScale(double scale)
    {
        return !double.IsNaN(scale)
            && !double.IsInfinity(scale)
            && scale > 0
            && scale <= 8;
    }

    private static double NormalizeScale(double scale)
    {
        return Math.Round(scale, 4, MidpointRounding.AwayFromZero);
    }

    private static IEnumerable<IMonitor> GetDefaultMonitors()
    {
        return GetDefaultPlatform().GetMonitors();
    }

    private static IMonitor? GetDefaultMainMonitor()
    {
        return GetDefaultPlatform().GetMainMonitor();
    }

    private static IWindowPlatform GetDefaultPlatform()
    {
        try
        {
            return Window.GetWindowPlatform(false)
                ?? throw new PlatformNotSupportedException("Silk.NET did not return a window platform for monitor enumeration.");
        }
        catch (Exception exception)
        {
            throw new PlatformNotSupportedException("Silk.NET monitor enumeration is not available on this platform.", exception);
        }
    }

    private static unsafe double? TryGetGlfwMonitorContentScale(IMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        try
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            Silk.NET.GLFW.Monitor** nativeMonitors = glfw.GetMonitors(out int monitorCount);
            if (nativeMonitors == null || monitor.Index < 0 || monitor.Index >= monitorCount)
            {
                return null;
            }

            glfw.GetMonitorContentScale(nativeMonitors[monitor.Index], out float scaleX, out float scaleY);
            return SilkNetGlfwDpiService.TryNormalizeContentScale(scaleX, scaleY, out WpfDeviceScale scale)
                ? scale.Average
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (GlfwException)
        {
            return null;
        }
    }

    private static unsafe Rectangle<int>? TryGetGlfwMonitorWorkArea(IMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        try
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            Silk.NET.GLFW.Monitor** nativeMonitors = glfw.GetMonitors(out int monitorCount);
            if (nativeMonitors == null || monitor.Index < 0 || monitor.Index >= monitorCount)
            {
                return null;
            }

            glfw.GetMonitorWorkarea(
                nativeMonitors[monitor.Index],
                out int x,
                out int y,
                out int width,
                out int height);
            return width > 0 && height > 0
                ? new Rectangle<int>(x, y, width, height)
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (GlfwException)
        {
            return null;
        }
    }
}
