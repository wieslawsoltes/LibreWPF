using System;
using System.Collections.Generic;
using ProGPU.Wpf.Interop;
using Silk.NET.GLFW;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

internal sealed class WpfPortableDisplayMetricsSource : IPortableDisplayMetricsSource
{
    private readonly Func<IWpfMonitorService> _getMonitorService;

    internal WpfPortableDisplayMetricsSource(Func<IWpfMonitorService> getMonitorService)
    {
        _getMonitorService = getMonitorService ?? throw new ArgumentNullException(nameof(getMonitorService));
    }

    public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;

    public event EventHandler? DisplayMetricsChanged;

    public bool TryGetDisplayMetrics(out PortableDisplayMetrics metrics)
    {
        metrics = default;
        IReadOnlyList<WpfMonitorInfo> monitors;
        try
        {
            monitors = _getMonitorService().GetMonitors();
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (Exception exception) when (IsRecoverableMonitorQueryException(exception))
        {
            return false;
        }

        if (monitors.Count == 0)
        {
            return false;
        }

        WpfMonitorInfo primary = monitors[0];
        for (int index = 0; index < monitors.Count; index++)
        {
            if (monitors[index].IsPrimary)
            {
                primary = monitors[index];
                break;
            }
        }

        PortableRect primaryScreen = ToLogicalRect(
            primary.X,
            primary.Y,
            primary.Width,
            primary.Height,
            primary.DpiScale,
            primary.UsesLogicalCoordinates);
        PortableRect primaryWorkArea = ToLogicalRect(
            primary.WorkAreaX,
            primary.WorkAreaY,
            primary.WorkAreaWidth,
            primary.WorkAreaHeight,
            primary.DpiScale,
            primary.UsesLogicalCoordinates);

        if (!IsUsable(primaryScreen))
        {
            return false;
        }

        if (!IsUsable(primaryWorkArea))
        {
            primaryWorkArea = primaryScreen;
        }

        PortableRect virtualScreen = primaryScreen;
        for (int index = 0; index < monitors.Count; index++)
        {
            WpfMonitorInfo monitor = monitors[index];
            PortableRect screen = ToLogicalRect(
                monitor.X,
                monitor.Y,
                monitor.Width,
                monitor.Height,
                monitor.DpiScale,
                monitor.UsesLogicalCoordinates);
            if (IsUsable(screen))
            {
                virtualScreen = Union(virtualScreen, screen);
            }
        }

        metrics = new PortableDisplayMetrics(primaryScreen, primaryWorkArea, virtualScreen);
        return true;
    }

    internal static PortableRect ToLogicalRect(
        int x,
        int y,
        int width,
        int height,
        double dpiScale,
        bool usesLogicalCoordinates)
    {
        double scale = usesLogicalCoordinates || !IsUsableScale(dpiScale) ? 1.0 : dpiScale;
        return new PortableRect(x / scale, y / scale, width / scale, height / scale);
    }

    internal static PortableRect Union(PortableRect left, PortableRect right)
    {
        double x = Math.Min(left.X, right.X);
        double y = Math.Min(left.Y, right.Y);
        double rightEdge = Math.Max(left.X + left.Width, right.X + right.Width);
        double bottomEdge = Math.Max(left.Y + left.Height, right.Y + right.Height);
        return new PortableRect(x, y, rightEdge - x, bottomEdge - y);
    }

    private static bool IsUsable(PortableRect rect)
    {
        return !rect.IsEmpty
            && double.IsFinite(rect.X)
            && double.IsFinite(rect.Y)
            && double.IsFinite(rect.Width)
            && double.IsFinite(rect.Height)
            && rect.Width > 0
            && rect.Height > 0;
    }

    private static bool IsUsableScale(double scale)
    {
        return double.IsFinite(scale) && scale > 0 && scale <= 8;
    }

    private static bool IsRecoverableMonitorQueryException(Exception exception)
    {
        return exception is DllNotFoundException
            or EntryPointNotFoundException
            or GlfwException
            or InvalidCastException
            or InvalidOperationException
            or TypeInitializationException;
    }
}
