# Windows native-MIL SDK Showcase startup — 2026-09-14

## Acceptance action and provenance

The acceptance application is `ProGPU.Wpf.ShowcaseApp`. The action is to build
the unchanged SDK application with `ProGpuWpfRendererMode=NativeMilWgpu` and
native hit testing enabled, then run both its pre-display object checks and
displayed `Application.Run` self-test in the Parallels Windows 11 ARM64 VM.

The source is the merged LibreWPF integration branch at `c99d0311`, with
LibreWinForms `9e2924e5` and latest ProGPU `main` `ff8bcbf4`. The local
23-package feed was already produced from those commits. The guest built
Showcase in a private `C:\Temp\ProGpuWpfNativeWindowsFinal` artifacts and
NuGet tree, with zero build warnings or errors; it did not write outputs to
the shared source checkout. Its `progpu_native.dll` SHA-256 is
`59ac5c42cef19b62c835497907a1df6a3d48567239dbdfbae40ed087dbb1cdd5`,
identical to the `win-arm64` DLL in the exact ProGPU package.

The original package failed the pre-display `SystemCommands.MaximizeWindow`
assertion: the Window remained Normal. Source inspection showed that Windows
commands used the HWND post path when the portable activation had not yet
been created, despite frozen portable media. The source change now updates
the Window state before Show, without querying or posting to an HWND. The
guest's pre-display Showcase self-test passed with this rebuilt
`PresentationFramework.dll` overlaid into its disposable output.

The displayed run next failed in `WindowBackdropManager`: it handed the
ProGPU-owned portable handle to WPF's `DwmExtendFrameIntoClientArea`. The
source backdrop manager now avoids WPF HWND/DWM frame manipulation for
portable presentation, while leaving Windows MIL backdrops unchanged. The
ProGPU native host remains responsible for any future native backdrop effect.

The same run later rejected apparent unequal DPI during a framebuffer resize.
The captured frame was 3592×1875 pixels at logical 1796×938; Windows reported
uniform 2× content scale. X was exactly 2, while the one-row-short Y ratio was
1.9989339019189765. The host now uses the reported uniform content scale only
when each physical extent differs by at most one pixel and both ratios are
close to it. It retains the actual framebuffer and viewport dimensions, and
keeps larger or genuinely unequal-axis differences under the existing guard.
Eight focused render-surface geometry tests passed, including new one-pixel and
beyond-rounding cases.

With the rebuilt `PresentationFramework.dll`, `PresentationCore.dll`, and
`ProGPU.Wpf.dll` overlaid into the guest-only package output, the displayed
`Application.Run` self-test completed startup, system commands, storyboards,
resource controls, secondary window, editor, document, and shutdown, printing
`ProGPU WPF Showcase Application.Run validation succeeded.`

The first locally repacked transport still carried a stale Windows-managed
`PresentationCore.dll`, so that run did not test the follow-up source. A second
23-package closure replaced the transport's managed ARM64 payload with PR #126's
CI-built artifact. SHA-256 checks of `PresentationFramework.dll`,
`PresentationCore.dll`, `ProGPU.Wpf.dll`, and `progpu_native.dll` in the guest
output matched their exact NuGet members. That clean package built Showcase
without warnings or errors and passed its pre-display object self-test, but
displayed `Application.Run` failed in a DataGrid header: WPF mapped
`Segoe Fluent Icons, Segoe MDL2 Assets` at SemiBold to a physical face with
`BoldSimulation`, and `PortableTextLine` rejected that mapped face. This is a
reproducible clean-package text blocker, not an intermittent result.

The follow-up text change retains the mapped face and its style-simulation
flags in `GlyphRun`, whose portable exports already carry those flags to
ProGPU's managed and C++ native glyph renderers. It adds portable ink overhang
for the native simulated bold pass and italic shear without altering shaped
advances or caret positions. A focused Windows regression tests explicitly
simulated physical-face flag and ink propagation. The source mapper itself
still needs the displayed application gate. This change still requires a fresh
Windows-managed payload, clean package closure, displayed guest retest, and
visual comparison.

The Windows 11 ARM64 guest ran the `PresentationCore.Tests` portable-media
`PortableTextLineTests` class with the source-built `PresentationCore.dll` and
packaged `PresentationNative_cor3.dll`: 24/24 tests passed, including the new
synthetic bold/italic glyph-run flag and ink test. A displayed source-overlay
Showcase retry initially aborted at `wgpuSurfaceConfigure` with `Invalid
surface`. This was a test-harness launch error: Parallels `exec` without
`--current-user` ran as `SYSTEM`, outside the signed-in desktop session. With
`--current-user`, the same source-overlay binary displayed and completed the
full `Application.Run` self-test, including the DataGrid, secondary window,
editor, and document. Future graphical guest gates must use the signed-in
user session; neither a process exit code of zero nor startup-only messages
count as success.

The follow-up branch then produced a fresh 23-package development closure at
`artifacts/packages/WindowsNativeFont` using the CI-built Windows managed
payload and exact ProGPU `ff8bcbf4` packages. The transport's `win-arm64`
`PresentationCore.dll` SHA-256 is
`aab9db901251fe9d0a4d78afff09515a522ecfc9447b034bd9bfe82149a867de`,
identical to that CI payload. The Windows VM was subsequently resumed, and
the unchanged Showcase was built against that package feed in a private guest
artifacts tree. Both its pre-display check and displayed `Application.Run`
self-test passed without source overlays.

For exact PR #126 head `adfbcd39f`, a second private 23-package closure at
`artifacts/packages/WindowsNativeCurrentPr126` used the current-head Windows
managed payload from CI run `34881136168`, canonical WinForms packages and
ProGPU `ff8bcbf4`. The guest copied only these packages to
`C:\Temp\ProGpuWpfNativeCurrentPr126\packages`, built the repository's
`PresentationBuildTasks` and then the unchanged SDK Showcase with
`ProGpuWpfRendererMode=NativeMilWgpu` and native hit testing enabled. Both
builds finished with zero warnings and zero errors. The guest's output hashes
matched the exact package members:

| Guest output | SHA-256 |
| --- | --- |
| `PresentationCore.dll` | `bb96bc93e7a0fd95b76eab203d58dca7ed07076872cd19eaa2269b5232ffdf39` |
| `PresentationFramework.dll` | `ec6a09ef13730a2ee26f0d2ffd1ab9804fbe4a442af6a446d9379cd78a309854` |
| `ProGPU.Wpf.dll` | `126c429586347f9a0151a1ad16a4b74570151080fb7d86b2dbc5b8495faaa720` |
| `progpu_native.dll` | `59ac5c42cef19b62c835497907a1df6a3d48567239dbdfbae40ed087dbb1cdd5` |

The `PresentationCore.dll` hash also matches the current-head CI `win-arm64`
payload. The package-only pre-display check printed `ProGPU WPF Showcase
validation succeeded.` The displayed check, launched with Parallels
`--current-user` into the signed-in desktop session, reached startup, resource
controls, system commands, storyboards, the secondary window, editor and
document, then printed `ProGPU WPF Showcase Application.Run validation
succeeded.` Both processes exited zero. No guest output DLL was overlaid from
the source build. Graphical Parallels tests must keep `--current-user`; running
as `SYSTEM` previously produced an invalid WebGPU surface.

## Qualification boundary

The current-head Windows ARM64 package-only Showcase startup and displayed
`Application.Run` gates now pass. The earlier first-package synthetic-font
failure is historical, fixed and covered by the clean rerun; it must not be
counted as a current failure. This is functional self-test evidence, not a
pixel-quality comparison: inspect visible text/layout at Windows DPI, run the
same exact-package application gate on x64, complete required native-host/SDK
smoke and CI at the delivery commit before Windows native SDK admission. The
larger native text, modal/popup, DirectX/Direct2D and platform parity
requirements remain open. No default renderer or unsupported guard for true
anisotropic presentation was enabled.
