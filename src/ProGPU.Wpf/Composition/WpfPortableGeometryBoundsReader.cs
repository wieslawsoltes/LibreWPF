using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortableGeometryBoundsReader
{
    public static bool TryGetLocalGeometryBounds(
        PortableGeometryPath geometry,
        out WpfReplayRect bounds)
    {
        return WpfPortablePathBoundsReader.TryGetLocalPathBounds(
            geometry,
            out bounds);
    }

    public static bool TryGetGeometryBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        if (WpfPortableRectangleClipReader.TryGetRectangleClipBounds(geometry, out bounds))
        {
            return true;
        }

        if (WpfPortablePathBoundsReader.TryGetPathBounds(geometry, out bounds))
        {
            return true;
        }

        if (WpfPortablePathGeometryConverter.TryGetNativePathBounds(geometry, out bounds))
        {
            return true;
        }

        if (!WpfPortableGeometryPathData.HasPathData(geometry))
        {
            bounds = FromPortableRect(geometry.Bounds);
            return IsUsableBounds(bounds);
        }

        bounds = default;
        return false;
    }

    private static WpfReplayRect FromPortableRect(PortableRect bounds)
    {
        return bounds.IsEmpty
            ? default
            : new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool IsUsableBounds(WpfReplayRect bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width >= 0
            && bounds.Height >= 0;
    }
}
