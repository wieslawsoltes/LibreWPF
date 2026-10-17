using ProGPU.Scene;
using ProGPU.Backend;

namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfWindowOptions
{
    public string Title { get; set; } = "WPF ProGPU Host";

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 800;

    public int? Left { get; set; }

    public int? Top { get; set; }

    public bool VSync { get; set; }

    public bool IsEventDriven { get; set; } = true;

    public bool IsVisible { get; set; } = true;

    public bool Topmost { get; set; }

    public bool ShowActivated { get; set; } = true;

    // Source-owned Win32 policy. Null leaves generic hosts unchanged; other
    // providers keep their existing independent taskbar/dock behavior.
    internal bool? Win32ShowInTaskbar { get; set; }

    public bool TransparentFramebuffer { get; set; }

    /// <summary>
    /// Selects the WPF scene compiler and compositor lane. The established
    /// managed portable renderer remains the compatibility default.
    /// </summary>
    public ProGpuWpfRendererMode RendererMode { get; set; } =
        ProGpuWpfRendererMode.ManagedPortable;

    /// <summary>
    /// Selects the native WebGPU backend before creating a render device. Null
    /// inherits a shared device's configuration, or the ProGPU startup environment
    /// for a new device. Explicit choices must match an existing shared device;
    /// they never select another renderer or silently fall back to another backend.
    /// </summary>
    public WgpuNativeBackendOptions? NativeBackendOptions { get; set; }

    /// <summary>
    /// Selects the compiler before creating an owned Windows D3D12 device.
    /// Null inherits the live shared owner's configuration, or the core startup
    /// environment for a new device. Explicit DXC requires its verified runtime
    /// package; shared devices retain the core compiler compatibility checks.
    /// </summary>
    public WgpuDx12CompilerOptions? Dx12CompilerOptions { get; set; }

    /// <summary>
    /// Requires native MIL to emit a complete GPU input index and uses that
    /// index for host point/region callbacks. Startup-only, explicit admission
    /// while application coverage is being completed; unsupported content fails
    /// compilation. Native mode never substitutes the managed input index.
    /// </summary>
    public bool EnableNativeMilHitTesting { get; set; }

    /// <summary>
    /// Explicitly requests the shared native modal event session for source
    /// ShowDialog. Unsupported providers reject; no ordinary poll fallback is
    /// selected. Automatic admission awaits paired source-host qualification.
    /// </summary>
    public bool EnableNativeModalSessions { get; set; }

    internal bool EnablePortablePopupService { get; set; } = true;

    // Resolve the live owner at target creation, including after device loss.
    internal ProGpuWpfWindowHost? SharedRenderDeviceOwner { get; set; }

    internal CompositorOptions? CompositorOptions { get; set; }

    internal bool IncludePortablePopupRootsInWpfReplay { get; set; }

    internal bool NativePointerCoordinatesAreOwnerRelative { get; set; }

    internal bool IsPopupSurface { get; set; }

    public ProGpuWpfWindowBorder WindowBorder { get; set; } = ProGpuWpfWindowBorder.Resizable;

    /// <summary>Requested minimize capability; native admission is platform-specific.</summary>
    public bool CanMinimize { get; set; } = true;

    /// <summary>Requested maximize capability; native admission is platform-specific.</summary>
    public bool CanMaximize { get; set; } = true;

    public ProGpuWpfWindowState WindowState { get; set; } = ProGpuWpfWindowState.Normal;
}

public enum ProGpuWpfRendererMode
{
    ManagedPortable,
    NativeMilWgpu
}

public enum ProGpuWpfWindowState
{
    Normal,
    Minimized,
    Maximized
}

public enum ProGpuWpfWindowBorder
{
    Resizable,
    Fixed,
    Hidden,
    HiddenResizable
}
