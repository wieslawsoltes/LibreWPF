namespace ProGPU.Wpf.Interop;

public interface IPortableWindowStateSource
{
    bool TryGetPortableWindowState(out PortableWindowState state);
}

/// <summary>
/// Source-owned top-level window state updated from a native host move.
/// Coordinates are desktop logical positions, not framebuffer pixels.
/// </summary>
public interface IPortableWindowLocationSink
{
    void OnPortableWindowLocationChanged(double left, double top);
}

public sealed class PortableWindowState
{
    public bool HasTitle { get; set; }

    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="Icon"/> contains the window icon source.
    /// </summary>
    public bool HasIcon { get; set; }

    /// <summary>
    /// Gets or sets the framework-owned window icon source.
    /// </summary>
    /// <remarks>
    /// The interop assembly deliberately keeps this value opaque so portable hosts can
    /// adapt the source without taking a dependency on a particular UI framework.
    /// </remarks>
    public object? Icon { get; set; }

    public bool HasWidth { get; set; }

    public double Width { get; set; }

    public bool HasHeight { get; set; }

    public double Height { get; set; }

    public bool HasActualWidth { get; set; }

    public double ActualWidth { get; set; }

    public bool HasActualHeight { get; set; }

    public double ActualHeight { get; set; }

    public bool HasLeft { get; set; }

    public double Left { get; set; }

    public bool HasTop { get; set; }

    public double Top { get; set; }

    /// <summary>
    /// Whether <see cref="StartupLocation"/> carries the source window's first-show policy.
    /// </summary>
    public bool HasStartupLocation { get; set; }

    /// <summary>
    /// Source startup location: 0 = manual, 1 = center screen, 2 = center owner.
    /// Hosts apply this only before the first native show, after final client sizing.
    /// </summary>
    public int StartupLocation { get; set; }

    public bool HasWindowState { get; set; }

    public int WindowState { get; set; }

    public bool HasTopmost { get; set; }

    public bool Topmost { get; set; }

    public bool HasResizeMode { get; set; }

    public int ResizeMode { get; set; }

    public bool HasWindowStyle { get; set; }

    public int WindowStyle { get; set; }

    public bool HasShowInTaskbar { get; set; }

    public bool ShowInTaskbar { get; set; }

    public bool HasShowActivated { get; set; }

    public bool ShowActivated { get; set; }

    public bool HasAllowsTransparency { get; set; }

    public bool AllowsTransparency { get; set; }

    public bool HasOwner { get; set; }

    public object? Owner { get; set; }
}
