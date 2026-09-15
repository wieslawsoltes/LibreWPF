# Native window frame sizing: source-to-host integration

The acceptance action is opening `ProGPU.Wpf.ShowcaseApp` and the shared native
text fixture as an ordinary, decorated top-level Window at 200% Windows DPI.
The fixture declares `Width="430" Height="300"`. In the Windows ARM64 VM,
stock WPF opened a 430 × 300 outer rectangle and a 417 × 264 client rectangle;
the prior ProGPU native MIL host opened 443 × 336 outer and 430 × 300 client.
That 13 × 36 surplus is a window frame applied *outside* an already
client-sized WPF outer request. The text line metrics alone could not reveal
this layout error.

The shared ProGPU frame contract reports native left/top/right/bottom as
desktop logical units. Win32 physical frame pixels are divided by the current
native content scale once; Cocoa/X11 GLFW frame metrics are already desktop
coordinates. `null` is unavailable; a known zero frame is valid for borderless
windows. No source path uses the portable presentation-source identity as an
HWND or invents title-bar dimensions.

LibreWPF now initializes its registered native host hidden before attaching
the Window root. It converts the Window's *outer* Width/Height to the host
*client* size after the native frame exists. The source Window measures and
arranges its child against the outer size minus the same frame. The portable
presentation source retains client size for surfaces and input, but adds the
frame exactly once when laying out the actual Window root; non-Window roots
and popups keep their previous client-sized layout. Reverse conversion during
SizeToContent measurement subtracts the frame exactly once.

The canonical `WindowsFormsIntegration` source gate requires LibreWPF and
LibreWinForms to consume an identical ProGPU commit. The first PR #141 CI run
rejected the new ProGPU pin before build because LibreWinForms still pinned
the preceding main commit. ProGPU PR #166 merged as
`3755428f3ad9c269a5e4a9bc91e0a4175f68f695`; LibreWinForms PR #35
passed its exact-head package/source gates and merged as
`4a1b17e819245a50adc92470c1096377e029b2f1`. This LibreWPF branch now
pins both merge commits, and the merged LibreWinForms source pins that same
ProGPU main commit. This is a dependency fix, not a relaxation of the
canonical gate.

The same-source Windows package-only text fixture now reports its actual
native outer/client rectangles, DPI, and declared Window size for both stock
WPF and native MIL. The gate compares the two rectangles (within one native
pixel), DPI, and source dimensions alongside text line metrics. In native
mode it reads the actual ProGPU Win32 host handle, never the portable
presentation-source identity; stock WPF uses its ordinary HWND. The earlier
443 × 336 oversized native window would fail this gate even though its text
line metrics passed.

The first exact-head SDK smoke with this source change compiled the real
PresentationCore/PresentationFramework and passed the source-built native MIL
host smoke, then failed in the real WPF XAML runtime harness before display.
Its trace showed `PortablePresentationSource.ApplyRootVisualLayout` invoking
Window's frame conversion while `TryCreateActivation` still had not returned
its activation to Window; `IsPortableWindowActive` was false and the old HWND
non-client helper was null. The registered normal-window path now creates the
source and native frame hidden, but defers the root visual attachment until
`Show`, after Window holds its portable activation. This uses the same
source-identity ordering as the existing hidden-window first-Show route.
The first Show applies the authoritative client size to that source before
attaching the root, so no placeholder outer size is measured. Local
ProGPU.Wpf compilation and all 81 focused activation tests pass; the final
merged-source head still needs its own exact-head CI result. The failed
smoke is not sizing qualification.

The next source-built XAML runtime smoke reached the real portable Window and
reported a 312-high macOS native *client* for its 340-high declared outer
Window. Its legacy harness still required a 340-high client and failed. The
harness now compares declared outer dimensions with the native client plus
the actual typed logical frame insets, instead of freezing pre-frame client
dimensions. This retains an exact window-sizing assertion across Cocoa,
Win32, and X11 without hard-coded title-bar heights.

On the final merged-source CI head, the corrected XAML harness and three real
portable application-lifetime scenarios passed. The subsequent Fluent theme
runtime harness independently retained the same old 340-client-height
expectation and stopped the SDK gate. Its presented-host assertion now uses
the identical actual client-plus-frame relation. This is a harness update for
the implemented outer-size contract, not a skipped runtime or a change in
theme rendering behavior.

ProGPU backend contract tests passed (12/12) and the ProGPU.Wpf
portable host build completed with zero errors on macOS. Windows source
compilation passed on the prior PR head, but Windows x64/ARM64 stock-vs-native
rectangle validation, live
Showcase interaction, resize/DPI/chrome/SizeToContent transitions, and CI are
still required before this branch qualifies or merges. Existing native SDK
admission remains separate.

On the independent Parallels Windows 11 ARM64 guest (build 26200.9457,
Parallels Tools 27.0.1), the merged-source branch built and ran the focused
`WpfPortableWindowActivationTests` under installed .NET SDK 10.0.401:
81 passed, 0 failed, 0 skipped. This checks source activation and frame
callbacks on Windows; it does not substitute for package-only outer/client
rectangle comparison or native MIL application interaction. The guest uses its
existing shared drive for source access, with no VM configuration change.

The later exact-head canonical WinForms CI lane failed twice at the same
package restore with NuGet `MSB4181`, after all ProGPU drawing dependencies
were packed. Its generated version ended in bare WPF commit token `04060464`:
an all-numeric prerelease identifier with a leading zero, invalid under
SemVer. Earlier otherwise-identical heads with alphanumeric commit tokens
passed the lane. Canonical CI package versions now label both source identities
as alphanumeric `winforms<commit>` and `wpf<commit>` tokens, preserving exact
source provenance while avoiding numeric-leading-zero restore rejection.
The pinned local .NET 11 restore reproduced `MSB4181` with the bare
`0.1.0-canonical.4a1b17e8.04060464` version and completed successfully with
`0.1.0-canonical.winforms4a1b17e8.wpf04060464` under the same project and
properties.
The changed head still requires its own canonical and SDK result; this is not
license to skip package closure or release validation.

The subsequent exact-head CI build passed canonical WinForms integration,
Windows managed-payload packaging, the real XAML and Fluent theme runtimes,
three application-lifetime scenarios, and the package audit. The SDK switch
runtime harness then stopped at `TargetParameterCountException` before its
window checks: its reflective test registrar still supplied 22 arguments to
`PortableWindowActivationService.Register` after the typed frame-insets
capability became argument 23. The harness now supplies an explicit absent
frame-insets callback in its fake host. The product's typed registration and
all preceding live-host gates remain unchanged; the corrected SDK switch
and downstream CI lanes still require an exact-head run.

The independent Windows ARM64 package-only gate initially stopped at NuGet
`NU1301` because the unchanged Showcase sample's checked-in `NuGet.config`
requires a repo-local `artifacts/packages/Release/NonShipping` directory,
which is absent in this clean checkout. Passing an external exact package
directory via `RestoreAdditionalProjectSources` does not remove that invalid
source. The VM gate now clones the sample's configuration to its private test
directory, points only that local-feed entry at the supplied package bundle,
and keeps its package cache private. It does not rewrite the sample or restore
from an undeclared source. The first rerun restored the unchanged Showcase
successfully; native application and stock-Windows geometry results are still
pending.

That VM rerun built the ARM64 SDK Showcase, confirmed the package hashes for
`PresentationCore.dll`, `PresentationFramework.dll`, `ProGPU.Wpf.dll`, and
`progpu_native.dll`, and passed its pre-display validation. Its displayed
Application.Run case then reached a genuine native MIL first-show race:
Win32 delivered a synchronous framebuffer-resize callback while the source
was attaching the WPF root, before the bridge could mirror that root into the
native host. Native MIL correctly rejected a null typed root. The host now
updates resize geometry and queues the frame but defers only that premature
callback render; subsequent frames still require the typed source root and
native input index. The corrected package must be rebuilt and rerun on the
VM; this source fix alone does not qualify the displayed case or the Windows
geometry comparison.
