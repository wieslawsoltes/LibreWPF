using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Composition;

namespace System.Windows.Media.ProGPU;

/// <summary>Initializes native-mode source services before constructing source-built WPF objects.</summary>
public static class ProGpuWpfNativeMediaServices
{
    /// <summary>
    /// Selects portable media and installs lazy text/geometry/document defaults. Idempotent;
    /// preserves explicit service overrides. Creates no window, device, font context,
    /// native renderer or surface. A conflicting frozen media choice fails before
    /// service registration. SDK platform admission remains a separate requirement.
    /// </summary>
    public static void Initialize()
    {
        PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.Portable);
        WpfPortableGeometryOperations.EnsureRegistered();
        WpfPortableTextFormatting.EnsureRegistered();
        WpfPortableDocumentFlow.EnsureRegistered();
    }
}
