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

    internal bool EnablePortablePopupService { get; set; } = true;

    internal WgpuContext? SharedRenderDeviceContext { get; set; }

    internal CompositorOptions? CompositorOptions { get; set; }

    internal bool IncludePortablePopupRootsInWpfReplay { get; set; }

    internal bool NativePointerCoordinatesAreOwnerRelative { get; set; }

    internal bool IsPopupSurface { get; set; }

    public ProGpuWpfWindowBorder WindowBorder { get; set; } = ProGpuWpfWindowBorder.Resizable;

    public ProGpuWpfWindowState WindowState { get; set; } = ProGpuWpfWindowState.Normal;
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
