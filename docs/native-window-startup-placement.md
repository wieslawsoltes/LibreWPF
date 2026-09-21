# Native window startup placement

The source-built `Window` exports `WindowStartupLocation` through the typed
`PortableWindowState` snapshot. `WpfPortableWindowActivation` applies it after
the portable host's initial client size and owner have been synchronized, before
the first native show. Later `Hide`/`Show` cycles preserve the user's moved
position. `Manual` remains controlled by source `Left`/`Top`; maximized and
minimized startup states take precedence over centering.
The optional typed `IPortableWindowLocationSink` returns accepted native
desktop positions to the source `Window`, so `Left`/`Top` and
`LocationChanged` do not depend on an HWND-only move handler.

The shared ProGPU `PortableWindowStartupPlacement` contract performs the
work-area center and owner-center clamp in desktop coordinates. The WPF host
selects the owner's monitor using ProGPU's existing nearest/overlap monitor
selection. An unowned `CenterScreen` window reads the pointer through ProGPU's
typed `NativeDesktopPointer` provider and selects the containing or nearest
monitor; when that capability is unavailable it falls back to the primary
monitor. The source and native host use the same unscaled desktop origins:
framebuffer DPI is not multiplied into these positions. An unavailable monitor
inventory or unresolved owner does not fabricate a placement.

This implements the primary-monitor route for the top-left startup behavior
seen in `ProGPU.Wpf.TextLayoutParityApp`, and routes
`ProGPU.Wpf.ShowcaseApp`'s owned About window through `CenterOwner`.
Neither application path is qualified by compilation alone.
The pointer provider uses Win32 screen coordinates, Cocoa screen points mapped
to ProGPU's top-left convention, or an isolated X11 root-pointer query. The WPF
monitor service resolves the same explicit/default X11-versus-Wayland preference
as host creation before selecting the provider. Wayland reports no global point,
so its documented primary-monitor fallback remains explicit.

It is not yet exact native-WPF placement parity:

- Host width/height currently describe the client rather than the complete
  decorated top-level frame. Native frame extents and size-to-content changes
  need a post-layout/pre-show placement contract before exact pixel comparison.
- Wayland may reject global desktop positioning. Such a rejection must remain
  visible in platform qualification, not be counted as a centered window.

Compilation gate: build `external/ProGPU/src/ProGPU.Backend`,
`external/ProGPU/src/ProGPU.Tests`, `src/ProGPU.Wpf`, and
`src/ProGPU.Wpf.Tests`. Focused coordinate mapping, explicit-platform dispatch,
monitor selection, owner precedence, and deferred-first-show regression cases
are authored in the ProGPU and LibreWPF test projects. The focused provider cases
pass 8/8 and the combined WPF activation/platform-selection cases pass 104/104.
Compare the Showcase and text-layout windows against Windows WPF in the final
integrated qualification phase defined by `native-mil-core-delivery.md`.
Canonical WinForms source integration requires the same ProGPU pin in
LibreWinForms. [LibreWinForms #34](https://github.com/wieslawsoltes/LibreWinForms/pull/34)
merged as `c5f459c7b078eee47b2c32065ecac0899d87de0a` and pins ProGPU merge
`21c60978539ca7893f04491249f08ad11141e475`; LibreWPF pins those exact
merge commits and retains its own exact-ProGPU-pin gate.
