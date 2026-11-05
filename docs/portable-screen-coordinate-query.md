# Reading the portable screen-coordinate contract

`PointToScreen` and `PointFromScreen` retain their existing coordinate behavior.
A portable source maps client DIPs through its host-published
`PortableDesktopTransform`: desktop coordinates equal client coordinates times
the independent X/Y desktop scales, plus the desktop origin. The origin is not
multiplied by those scales. `CompositionTarget.TransformToDevice` describes
framebuffer DPI separately and must not be applied again to an already converted
desktop point.

On the reported Retina host, a desktop scale of one and framebuffer scale of two
therefore still give `(240, 210)` for a client point `(40, 60)` at desktop origin
`(200, 150)`. Other portable hosts may publish different desktop scales; neither
OS identity nor a blanket assumption that portable screen coordinates are logical
units describes the contract.

Callers can now inspect the actual live source without casting an internal type:

```csharp
PresentationSource source = PresentationSource.FromVisual(child);
if (source != null &&
    PortablePresentationSourceHost.TryGetDesktopTransform(source, out var desktop))
{
    Point anchor = child.PointToScreen(new Point(0, 0));
    // anchor is already in desktop units. Do not multiply it by framebuffer DPI.

    // Convert a desktop drag offset to this source's client-DIP offset:
    var clientOffset = desktop.DesktopVectorToClient(
        new ProGPU.Wpf.Interop.PortablePoint(desktopDeltaX, desktopDeltaY));
}
```

The query resolves the same owned portable source used by `PointUtil`, including
the public `HwndSource` facade returned by `FromVisual`. It reads one immutable
snapshot on the source dispatcher thread. Read again after moving the source or
changing its desktop geometry; an older returned value does not update itself.
The transform maps source client coordinates, not arbitrary child visual-local
coordinates. Keep using `PointToScreen` for a transformed child's absolute point.

Null arguments throw `ArgumentNullException`; foreign-thread calls retain the
source's dispatcher-access exception. Disposed sources, retired facades, original
native HWND sources and unknown custom sources return false with an invalid
default output. False means the portable mapping is unavailable, not that the
coordinates use physical pixels. A custom source's advertised host interface is
not adopted when the existing conversion path does not consume it. Native HWND
sources retain their existing Windows screen-coordinate route.

Source controls cover the public facade, translated independent X/Y scales,
framebuffer changes, source movement, immutable returned values, custom sources,
missing/disposed/retiring identities and foreign threads. A separate Windows
control requires the actual hidden native HWND source to remain unavailable and
keeps its existing `PointToScreen` result. These controls do not replace AvalonDock
desktop drag acceptance on scaled displays or final package qualification.

The existing Linux and macOS source CI lanes select all 24 font-discovery and
coordinate controls through `eng/progpu-wpf-catalog-desktop-source.sh`, reusing
their already-built source assembly. Each selection requires its exact minimum
inventory, no skips and the existing 60-second bound, with retained TRX results.
All 24 passed locally on macOS ARM64. The Windows managed-runtime lane opts into
the separate native query using WindowsMil media, an explicitly verified x64
source/native closure and a 60-second owned-process deadline. Its test verifies
the loaded source assembly and native MIL path; compilation and local portable
results do not count as Windows execution.

This API addresses public detection in
[LibreWPF #299](https://github.com/wieslawsoltes/LibreWPF/issues/299); it does not
introduce a DPI conversion change.
