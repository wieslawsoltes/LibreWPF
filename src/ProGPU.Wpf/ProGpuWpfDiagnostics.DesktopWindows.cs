using global::ProGPU.Backend;

namespace System.Windows.Media.ProGPU;

public static partial class ProGpuWpfDiagnostics
{
    /// <summary>Read-only source/native identity for desktop acceptance, not input admission.</summary>
    public readonly record struct DesktopWindowSnapshot(
        object RootVisual, nint SourceHandle, NativeWindowHandle NativeWindow,
        bool IsVisible, bool InputEnabled, long PresentedFrameCount,
        ProGpuWpfRendererMode RendererMode, bool NativeMilHitTestingEnabled,
        NativeWindowGeometrySnapshot? NativeGeometry);

    /// <summary>
    /// Reads existing live surfaces on their creating thread. Never creates,
    /// pumps, shows, focuses, presents or reconstructs a missing native surface.
    /// An unavailable visible popup makes the entire observation unavailable.
    /// </summary>
    public static bool TryGetDesktopWindowSnapshots(object window, out DesktopWindowSnapshot[] snapshots)
    {
        snapshots = Array.Empty<DesktopWindowSnapshot>();
        return WpfPortableWindowActivation.TryGetActiveHost(window, out var host) && host != null &&
            host.TryGetDesktopWindowSnapshots(out snapshots);
    }
}
