using System.Collections.Generic;
using ProGPU.Backend;

namespace System.Windows.Media.ProGPU;

/// <summary>
/// Tracks the process-wide WebGPU render device - instance, adapter, device and queue - that
/// top-level window hosts borrow instead of each creating their own.
/// </summary>
/// <remarks>
/// Requesting an adapter enumerates every wgpu backend, GLES included, even where wgpu ends up
/// selecting another one. Enumerating the GLES backend makes its EGL context current, which
/// collides with the contexts other live instances hold and aborts the process from native code.
/// Keeping one instance for the process avoids that, and also stops every window from
/// duplicating an adapter, device, queue and device resource domain.
/// </remarks>
internal static class ProGpuWpfRenderDeviceSharing
{
    private static readonly object s_sync = new();
    private static readonly List<WgpuContext> s_deviceOwners = new();
    private static bool s_isContextDisposingHooked;

    internal static bool IsEnabled => ShouldShareRenderDevice(
        OperatingSystem.IsWindows(),
        string.Equals(
            Environment.GetEnvironmentVariable("PROGPU_WPF_DISABLE_RENDER_DEVICE_SHARING"),
            "1",
            StringComparison.Ordinal));

    internal static bool ShouldShareRenderDevice(bool isWindows, bool explicitlyDisabled)
    {
        // Windows presents through the D3D12 backend, where a per-window device is the shipped
        // and most exercised configuration. Every other platform can reach wgpu's GLES backend.
        return !isWindows && !explicitlyDisabled;
    }

    /// <summary>
    /// Returns a live context whose render device a new window can borrow, or <c>null</c> when
    /// the next window has to create the process render device itself.
    /// </summary>
    internal static WgpuContext? TryGetDeviceOwnerContext()
    {
        lock (s_sync)
        {
            PruneRetiredContexts();
            return s_deviceOwners.Count == 0 ? null : s_deviceOwners[0];
        }
    }

    /// <summary>
    /// Publishes a window context as a render device owner for later windows. The context that
    /// created the device and the contexts that borrowed it can all hand the same device on, so
    /// closing the first window does not force the next one onto a private device.
    /// </summary>
    internal static void RegisterDeviceOwnerContext(WgpuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (s_sync)
        {
            if (!s_isContextDisposingHooked)
            {
                WgpuContext.Disposing += RetireDeviceOwnerContext;
                s_isContextDisposingHooked = true;
            }

            PruneRetiredContexts();
            if (!s_deviceOwners.Contains(context))
            {
                s_deviceOwners.Add(context);
            }
        }
    }

    /// <summary>Drops a context that can no longer hand out its render device.</summary>
    internal static void RetireDeviceOwnerContext(WgpuContext? context)
    {
        if (context == null)
        {
            return;
        }

        lock (s_sync)
        {
            s_deviceOwners.Remove(context);
        }
    }

    private static void PruneRetiredContexts()
    {
        // Disposing fires before IsDisposed flips, so both are needed to keep the list live.
        for (int i = s_deviceOwners.Count - 1; i >= 0; i--)
        {
            if (s_deviceOwners[i].IsDisposed)
            {
                s_deviceOwners.RemoveAt(i);
            }
        }
    }
}
