using ProGPU.Backend;
using ProGPU.Scene;

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

    public bool TransparentFramebuffer { get; set; }

    /// <summary>
    /// Selects the WPF scene compiler and compositor lane. The established
    /// managed portable renderer remains the compatibility default.
    /// </summary>
    public ProGpuWpfRendererMode RendererMode { get; set; } =
        ProGpuWpfRendererMode.ManagedPortable;

    internal bool EnablePortablePopupService { get; set; } = true;

    internal WgpuContext? SharedRenderDeviceContext { get; set; }

    internal CompositorOptions? CompositorOptions { get; set; }

    internal bool IncludePortablePopupRootsInWpfReplay { get; set; }

    internal bool NativePointerCoordinatesAreOwnerRelative { get; set; }

    public ProGpuWpfWindowBorder WindowBorder { get; set; } = ProGpuWpfWindowBorder.Resizable;

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
