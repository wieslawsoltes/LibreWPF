using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathKind = ProGPU.Wpf.Interop.PortableGeometryPathKind;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfPortableGeometryPathData
{
    public static bool HasPathData(PortableGeometryPath geometry)
    {
        if (geometry.Kind == PortableGeometryPathKind.Path)
        {
            return geometry.Figures.Length > 0;
        }

        if (geometry.Kind == PortableGeometryPathKind.Combined)
        {
            return (geometry.PathA != null && HasPathData(geometry.PathA))
                || (geometry.PathB != null && HasPathData(geometry.PathB));
        }

        return false;
    }
}
