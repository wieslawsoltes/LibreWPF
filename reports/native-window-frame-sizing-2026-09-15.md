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

The first local repack of the changed same-version SDK package exposed a
second package-layout issue: `LibreWPF.Sdk` contained two byte-identical
`README.md` ZIP entries, so the VM's exact isolated SDK extraction failed
before Showcase startup. Cleaning generated outputs did not remove the
duplication. SDK 11 preview pack included the readme through both its
`PackagingContent` and explicit `None` items. The first local correction
removed `None`, and the pack had one readme and no duplicate names. Hosted
SDK 11 RC1 then stopped at `NU5039`: on that toolset, `PackagingContent`
alone did not supply the NuGet readme named by `PackageReadmeFile`. The final
source uses one updated default `None` NuGet pack item and removes only this
SDK project's duplicate `PackagingContent` readme item; its Sdk/targets
content items and the source sample configuration remain unchanged. The
corrected package must pass both local ZIP membership and exact-head CI.
The local pack still briefly duplicated the readme after this source change:
its generated nuspec exposed a stale
`artifacts/packaging/Release/LibreWPF.Sdk/README.md` left by earlier
Arcade-content runs alongside the new project `None` item. That one generated
file was moved recoverably out of the staging directory. A fresh pack using
the exact hosted SDK 11 RC1 version then succeeded, with exactly one
`README.md`, required SDK targets, and no duplicate ZIP names. This local
toolset result does not waive the hosted exact-head bundle and VM rerun.
The repacked bundle completed the independent Windows 11 ARM64 package-only
gate. The SDK Showcase built with a native ARM64 apphost, exact package hashes
matched its WPF, ProGPU bridge, and `progpu_native.dll` outputs, and both
pre-display and visible `Application.Run` Showcase checks passed. The same
source-only text fixture compiled under stock Windows WPF and ProGPU native
MIL; both reported source size 430 × 300 DIPs, text width 211.333, five lines,
and starts `0,32,58,88,121`. At 192 DPI, stock WPF reported outer/client
860 × 600 / 834 × 529 pixels and ProGPU reported 860 × 601 / 834 × 530;
line heights were 93.100 and 93.105 DIPs. The gate's stock/native geometry
and text-layout tolerances passed. This is exact merged-ProGPU-source local
package evidence on ARM64, not hosted CI, x64, general live interaction,
resize/DPI transitions, or all-WPF text parity qualification.
It preceded the RC1 readme-pack correction; the corrected exact-head bundle
and hosted downstream package gates remain outstanding.

The local `ProGpuWpfWindowHostTests` regression group found one old source
assertion that required unconditional rendering immediately after every
framebuffer resize. It is aligned with the first-show pre-root guard while
still requiring synchronous resize rendering once a root is published.
Using the project's VSTest runner directly, all 229 window-host cases then
passed on macOS arm64. The build had existing compatibility/analyzer warnings
and zero errors. Source-shape coverage supplements, but does not replace, the
independent VM application result or hosted SDK/Windows gates.

Local full-SDK validation on a Retina macOS host stopped at the real XAML
mouse-binding case: the live source `Window.InputHitTest` selected its
`MouseBindingSurface` TextBlock, but the synthetic host event selected a
pointer-infrastructure Border and missed the routed command. The harness
created that event with `sender=null`, which ProGPU treats as already in
receiving source-root DIPs, yet its test helper had multiplied the point by
`TransformToDevice` first. At 2× it sent approximately `(420,53)` instead
of `(210,26)`. The fixture now sends the original root-local point; real
platform events retain their typed native desktop/device normalization.
A fresh real-XAML harness build under the same SDK 11 RC1 and runtime
version used by hosted CI had zero errors and its full runtime smoke passed
on macOS arm64. A proposed pre-forward managed-cache refresh was tested,
did not repair the mismatch, and was removed without committing it. This
fixture correction does not waive actual pointer/DPI application validation
or hosted exact-head CI.

The following hosted SDK 11 RC1 run passed canonical WinForms integration,
Windows managed payload, Linux smoke, SDK package production, real XAML,
Fluent theme, mixed desktop, and SDK-switch validation, then stopped in the
external unchanged-SDK app. Its SizeToContent fixture expected a 74-DIP
`Window.ActualHeight` for 74-DIP content while the newly correct outer Window
measured 102 DIPs with a 28-DIP native frame. The fixture now checks the
host's content/client width and height and the Window's outer size as host
client plus the actual logical native frame, both on first show and live
content resize. The generated external harness source compiles under the
pinned SDK 11 RC1 with zero errors. The exact local ProGPU source packages
and freshly repacked LibreWPF bridge/transport/SDK were then staged in an
isolated default feed so its strict package-vs-rebuilt-DLL hash guard passed.
That local external-app run exposed one more stale fixture in the generated
DefaultItemsApp: its XAML declares a 260 × 140 DIP outer Window, but the
render-surface geometry reports the native client, which is shorter by the
actual Cocoa frame inset. The live probe now requires the declared outer
size and checks each client dimension plus the typed native frame insets
against `Window.ActualWidth`/`ActualHeight`; its physical-pixel coverage and
full-viewport checks are unchanged. After adding the exact ProGPU and WebGPU
native dylib directories to the test-owned library search path, the pinned
SDK 11 RC1 external harness built with zero errors and its complete local
runtime smoke reported `ProGPU WPF external SDK smoke succeeded.` This is a
macOS arm64 generated-app result, not hosted exact-head CI or the corrected
bundle's final Windows VM rerun; those gates remain required.

The superseded hosted SDK 11 RC1 run for `35abacf6f` confirmed the same
DefaultItemsApp failure after its earlier gates passed: the old 140-DIP
logical-height assertion stopped the external live app. The new exact head
`232792e83` is running CI. A local full SDK sequence against the exact
merged ProGPU package closure initially stopped during real XAML native
library loading because the test's `DYLD_LIBRARY_PATH` referenced the
SDK-switch NuGet output directory that the full script deliberately cleans.
Binding `PROGPU_NATIVE_RUNTIME_DIR` to the stable Silk WebGPU 2.23.0 package
cache made that development-only path pass; package consumers still resolve
their own native assets. The full sequence then passed the generated external
SDK app and reached Hello's live geometry check. Hello still compared its
360-DIP declared outer height with the 332-DIP Cocoa client height. Its
source-owned live check now requires the declared 520 × 360 outer size and
the actual typed frame relation for both client dimensions, retaining
physical-pixel/viewport and interactive input checks. The corrected Hello
apphost passed at 2× DPI: 520 × 332 client, 1040 × 664 pixels, full viewport,
TextBox focus/edit/backspace/binding and button click.

The same outer/client assumption was present in the Showcase and Toolkit
initial live source checks and their launchers. Those checks now validate
the declared outer size against the live client plus typed native frame,
with positive client dimensions and unchanged pixel/viewport checks in the
launchers. Showcase and Toolkit both built under SDK 11 RC1 with zero errors.
The corrected Showcase apphost passed its full macOS 2× live path at an
initial 760 × 532 client: native resize, editor input, commands, themes,
popups, scrolling and pointer/caret interaction. Toolkit progressed through
its 980 × 612 initial client geometry and extensive AvalonDock interaction
steps, then crashed during a floated editor's native position callback in
`WindowChromeWorker._HandleWindowPosChanged`. That is a separate source
ownership/HWND-path blocker, not frame-geometry validation. Full local SDK,
hosted exact-head CI and final VM package rerun remain open.

The floated-editor crash was traced to Xceed AvalonDock's separately
compiled `Microsoft.Windows.Shell.WindowChromeWorker` in
`Xceed.Wpf.AvalonDock.dll`; LibreWPF's source worker is
`System.Windows.Shell.WindowChromeWorker` in PresentationFramework. The
vendor handler always consumes `WM_WINDOWPOSCHANGED` as a real Win32
`WINDOWPOS` pointer, then updates a Win32 system menu/rounding region.
The portable HwndSource facade had delivered synthetic WINDOWPOSCHANGING
and WINDOWPOSCHANGED hooks with `lParam=0`, which is not that protocol.
The portable activation bridge now publishes its typed source client
origin/location and only packed pointer-free `WM_MOVE`/`WM_SIZE` facade
notifications; actual native Windows WPF continues receiving real HWND
WINDOWPOS messages. This is a source-policy correction, not a vendor-type
filter or emulated user32 call. LibreWPF's own chrome worker also selects
registered portable windowing or frozen portable media even after a source
facade appears and guards native hook dispatch during a handoff.
After repacking the exact local closure, Toolkit's full macOS 2× live
application path passed, including floating/redocking steps, with initial
980 × 612 client geometry. `WpfPortableWindowActivationTests` passed 81/81;
the focused native-platform source guard and pointer-free geometry ingress
tests passed 2/2. The test project built with zero errors and existing
analyzer warnings. Hosted exact-head CI and Windows package/runtime gates
remain outstanding; this local vendor ingress success does not qualify all
AvalonDock layout or native chrome parity.
