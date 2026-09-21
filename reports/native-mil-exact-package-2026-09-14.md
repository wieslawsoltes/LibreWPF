# Native MIL exact-package validation — 2026-09-14

## Final merged-source core package and text check

ProGPU `main` Build
[34864169048](https://github.com/wieslawsoltes/ProGPU/actions/runs/34864169048)
passed 43/43 jobs for `ff8bcbf46a6b77de25cd524407b4865eb13d9db0`.
ProGPU #163 passed all 45 PR checks. LibreWinForms #31 passed all six checks
and merged to `9e2924e5da58831783bcf3eb6732fd4c954ed526`.
LibreWPF #115 head `3928b97ad4704bd49a9a3f2ab506d073ef6f0ffb`
passed all seven hosted checks: canonical WinForms source, Windows managed
payload, native SDK package/app smoke, canonical SDK consumer, Windows AnyCPU
package launch, Linux Wayland/XWayland input and popup smoke, and docs.

The isolated macOS NativeMilWgpu `eng/progpu-wpf-sdk-ci.sh` run exited zero
with the exact merged ProGPU runtime and aligned LibreWinForms package closure.
It completed the native host, XAML/theme/application-lifetime, package/bundle,
Hello, Showcase, Toolkit, licensed Xceed, default SciChart, and focused graph
gates. Its Showcase apphost `libprogpu_native.dylib` and the staged Build
runtime both have SHA-256
`ea2d4c4364844d18d486ae5d702b669b0bc3298f4188eef8422aeeaaca1de083`.
The initial local command omitted the exact native build-directory setting;
the next omitted the ProGPU source package version and stopped at a mixed
WPF/WinForms NU1605 downgrade. Neither stopped run is qualification. The
corrected full command passed. Local canonical package preparation also
passed for both the PR-head and merged LibreWinForms pins; a repeated output
directory first had to be isolated from old generated package versions.

A direct 2× macOS window-only capture of the final packaged Showcase Themes
pane showed normal-sized glyph ink and these five whole-word lines:
`The button is styled through a`, `compiled ControlTemplate,`,
`TemplateBinding, named parts,`, `VisualStateManager states, and`,
`a property trigger.` The private local capture is
`/tmp/progpu-showcase-merged-themes-window-20260914.png`; no unrelated desktop
content is part of that window image. This closes the observed native ink-scale
and shared whitespace-wrap defects on the exact core package. It does not
qualify a full cross-platform text-layout/sizing matrix, commercial SciChart
binaries, arbitrary Direct2D/Win2D, or native Windows SDK MIL admission. Those
remain documented work outside this core package decision.

## Exact merged package and licensed macOS application pass

ProGPU `main` Build
[34843133821](https://github.com/wieslawsoltes/ProGPU/actions/runs/34843133821)
completed successfully with 43/43 jobs for merge commit
`5b99b640a583c9f1cb69fd17731e000ab632baec`. Its published native
runtime artifact was staged without a source-built substitution. The canonical
LibreWinForms source/package gate passed against `7164c7ed9b9b93175d0eec9e0cee2a28dcc61310`
and LibreWPF head `33d7b09f7c1edd3cbaeb2098bd2df5d62fa74f2a`; its
isolated package directory contained one current canonical generation.

The full local `eng/progpu-wpf-sdk-ci.sh` run in `NativeMilWgpu` mode with
licensed Xceed validation enabled exited zero. It passed the native host,
XAML, application-lifetime and Fluent runtime harnesses; package audit and
release-bundle verification; SDK switch, external and mixed-desktop package
consumers; packaged Hello, Showcase, Toolkit and paid Xceed live app gates at
2× macOS DPI; Showcase's 120-frame performance/memory gate; default SciChart
renderer/`Application.Run` checks; and the focused SDK graph guard. The
packaged Showcase `libprogpu_native.dylib` SHA-256 is
`e00023d9ef597ad52c2036b4f2d9906ab7679d3f89835a2485f9bd3c55257c5e`,
identical to the staged exact merged-main artifact. This validates the local
macOS package path after the native text DPI, Fluent palette and ideal-width
fixes. It does not establish same-source Windows/Linux text visual parity,
commercial SciChart binaries, or hosted LibreWPF PR #115 CI. At that earlier
checkpoint the PR remained draft pending the final gates and release decision.

## Shared paragraph whitespace-wrap defect

The earlier exact packaged Showcase Themes pane split `TemplateBinding` into
`TemplateBindin` / `g` and placed `compiled` alone despite enough width for
`compiled ControlTemplate,`. Independent ManagedPortable and NativeMilWgpu
window captures reproduced the same boundaries, ruling out native MIL glyph
ink scaling as the cause. The source adapter supplied a 211.333-DIP paragraph,
14-DIP font, and three style spans; native output cut at source indices
`31..40`, `40..71`, and `71..87`. A C++ reproduction with the captured font
and identical paragraph request produced those exact cuts. A shaping
`unsafe_to_break` flag on the glyph after a legal whitespace opportunity
caused the shared scanner to ignore the space and take an emergency
mid-word break. [ProGPU #163](https://github.com/wieslawsoltes/ProGPU/pull/163)
preserves the whitespace boundary while retaining unsafe non-space and
same-cluster protection. The reproduced request now has five whole-word
lines, and all 20 local native CTest suites pass. #163 merged to ProGPU
`main` at `ff8bcbf46a6b77de25cd524407b4865eb13d9db0`. Its macOS x64
CI rerun and all 45 PR checks subsequently passed. The final exact merged-main
package/app visual gate was then rerun and passed as recorded above;
the previous exact-package pass used ProGPU `5b99b640` and predates this fix.
LibreWinForms #31 passed all six CI checks and merged at
`9e2924e5da58831783bcf3eb6732fd4c954ed526`, carrying the ProGPU
`ff8bcbf4` pin. The local aligned canonical package preparation passed
against LibreWinForms `75264ab6` and ProGPU `ff8bcbf4`; the final WPF
dependency pin uses the merged LibreWinForms commit and was repacked above.
An isolated copy of the packaged Showcase output with only the locally built
PR #163 `libprogpu_native.dylib` substituted passed its source self-test,
`Application.Run` validation twice, and the full native live input/resize/
framework-theme/popup gate at 2× DPI. The first `Application.Run` attempt in
that copied directory did not complete and was stopped; a control run using
the original merged-main dylib passed, then two clean attempts using the new
dylib passed in 11 and 7 seconds. That transient is not counted as a pass or
reproduced regression. The overlay run demonstrates app ABI compatibility,
not a rebuilt exact artifact. A new opt-in initial-tab selector allowed the
same isolated output to open directly on Themes, avoiding unreliable desktop
click injection. A 2× macOS window capture with the PR #163 dylib shows
normal-sized native glyphs and five whole-word paragraph lines:
`The button is styled through a`, `compiled ControlTemplate,`,
`TemplateBinding, named parts,`, `VisualStateManager states, and`,
`a property trigger.` A managed-mode full-screen capture of the same output
shows the same text boundaries. The native and managed captures used separate
processes. This is visual source-overlay evidence for the specific defect,
not a full text sizing/layout matrix or exact merged-package qualification.

Additional 2× macOS window spot-checks used isolated copies of the existing
Toolkit and SciChart Debug app outputs with only the locally built PR #163
native library substituted. Toolkit's headings, control labels, document
tabs, PropertyGrid values, and status text rendered at normal apparent size;
SciChart's title, bridge labels, button and status text did likewise. These
were initial-window captures, not the final exact package or a Windows
side-by-side. Narrow Toolkit DateTimeUpDown/PropertyGrid fields visibly clip
content at the current window width; whether that is source control sizing or
portable text measurement remains unresolved until matched Windows captures
at the same logical size. SciChart's rendered chart surface is a separate
DirectX visual concern, not evidence about text layout. The broad app text
matrix therefore remains open despite the corrected Showcase paragraph.
The subsequent same-source Windows Toolkit build used a guest-only
`C:\Temp\ProGpuWpfToolkitParity-20260914` output/cache and completed with zero
warnings/errors. At the app's initial logical window size, its native WPF
PropertyGrid `Body` value also clips in the narrow pane, so that specific
clipping is not evidence of a ProGPU text measurement regression. The lower
DateTimeUpDown field was below the Windows viewport in the initial capture;
its parity remains unchecked. The test process closed normally and the VM was
returned to suspended state. A full normalized cross-platform glyph/field
matrix is still required before broader text-layout qualification.

A current-source Windows comparison is now available. In the Parallels
Windows 11 VM, the same Showcase source project built against the exact
`5b99b640` local SDK feed with all output, intermediate and NuGet cache paths
redirected to `C:\Temp\ProGpuWpfShowcaseParity-20260914`; the shared macOS
worktree artifacts were not written. The source `PresentationBuildTasks`
dependency and Showcase both built with zero warnings/errors. Native Windows
WPF displayed the Themes pane at the VM's high-DPI resolution with five
whole-word lines: `The button is styled through a`,
`compiled ControlTemplate,`, `TemplateBinding, named parts,`,
`VisualStateManager states, and a`, `property trigger.` The Windows font and
metrics differ slightly from the portable macOS capture, so exact glyph pixels
and the last line boundary are not asserted identical; no mid-word split
occurred. An older guest checkout was not used. The first Windows
object-graph self-test exposed two portable-only test assumptions:
an unshown native WPF Window has no HWND for `SystemCommands` to post to, and
`Clipboard.IsCurrent` must receive the original object placed on the clipboard,
not a later `GetDataObject` wrapper. The Showcase now checks system-command
state only when the native source is presented, while still checking portable
pre-host state; its clipboard test retains the original `DataObject`. The
same-source Windows Release rebuild then succeeded with zero warnings/errors
and `PROGPU_WPF_SHOWCASE_VALIDATE=1` passed. The macOS native source-overlay
self-test passed after those adjustments too. Guest builds stayed in its
private `C:\Temp` directory, and the Windows VM was returned to suspended
state. This closes the object-graph test discrepancy, not Windows native MIL
SDK package admission or the full text visual parity matrix.

The first hosted LibreWPF PR #115 SDK run reached Showcase's displayed
`Application.Run` validation after all preceding package, Hello and Showcase
object checks passed, then remained there until the 90-minute job cancellation.
Local phase tracing reproduced the stall inside `ValidateShowcaseSystemCommands`:
maximize, minimize and restore completed, but executing the displayed native
system menu waited for an interactive dismissal. The unattended run now checks
that command's binding and `CanExecute` without opening the menu; the unshown
object test retains execution coverage. Actual displayed system-menu behavior
still needs a separate interaction gate. A process-group deadline now limits
both Showcase `Application.Run` invocations to three minutes with an explicit
failure instead of letting the whole SDK job hang. The changed app rebuilt
cleanly, and isolated 2× macOS native and managed `Application.Run` validations
both completed successfully in about five seconds. Hosted CI must rerun on
the updated PR head before this blocker is called closed.
The same current-source Windows 11 Showcase `Application.Run` test also passed
after its presented native-WPF restore assertion was aligned with Windows'
maximize → minimize → restore behavior (restore returns to Maximized). The
portable source path still asserts Normal. The first guest retry returned a
Parallels Tools job-code error without starting the app; an unchanged second
attempt completed every validation phase. The guest was returned to suspended
state. This adds a same-source Windows app-lifetime check, not a Windows native
MIL SDK package run.

## Native text visual blocker and clean-package follow-up

The user identified widespread text sizing/spacing defects while the live
behavior gates were green. A direct 1656×1312 window capture of the packaged
Showcase on a 2× macOS display confirmed that native glyph ink was about half
size while WPF advances and control positions remained logical. The C++ MIL
compiler supplied `em / raster` to a shader that already divided atlas pixels
by frame DPI. [ProGPU #162](https://github.com/wieslawsoltes/ProGPU/pull/162)
supplies `em × dpi / raster`; its native high-DPI regression and all 20 local
CTest suites pass. The PR passed 45/45 checks and merged to `main` at
`5b99b640a583c9f1cb69fd17731e000ab632baec`. Replacing only the app's
native dylib visibly restored normal text size and spacing. This is local
visual evidence, not final exact merged package or Windows/Linux parity;
LibreWPF's final exact pin and package CI remain. Other text/layout defects
are not presumed fixed.

A separate, managed-only Showcase window capture at the same 2× display
shows normal glyph sizing too. It also reproduced missing visible text in the
initial Controls tab's DataGrid Name/Category columns while checkbox cells
and grid lines were present. The native capture had the same missing content.
An isolated live visual-tree trace found correct bound strings, nonzero
TextBlock measured/actual sizes and `IsVisible=true`, but inherited
`Foreground=#00FFFFFF` from the DataGrid's Fluent style. The app's normal
relative `/PresentationFramework.Fluent;component/Themes/Fluent.xaml` resource
URI was not recognized by `ThemeManager`'s absolute-pack-URI-only prefix
check, so its dynamic light/dark semantic colors were absent. Temporarily
selecting the explicit `Fluent.Light.xaml` resource in the app restored the
palette and visible DataGrid text/control chrome in a fresh managed capture;
that app-only experiment was reverted. The source fix now recognizes the
relative URI and retains automatic system/light/dark theme selection. Its
focused PresentationFramework test passes 4/4. Overlaying only the newly
compiled source PresentationFramework assembly into an isolated packaged
Showcase output restored the full Controls tab in both managed and native
window captures; both live input validations and the object self-test passed.
The new Showcase palette assertion fails against the old package payload and
passes with the compiled source assembly, providing a red/green package-output
regression check.
The native overlay also used the clean rebuilt ProGPU #162 dylib. This is
source-overlay evidence, not a rebuilt exact SDK package or same-source
Windows comparison. The two renderer captures used independent processes,
not overlapping app windows. Do not merge the LibreWPF application PR on the
strength of the DPI regression alone.

With both corrected WPF assemblies rebuilt in Release mode, the same
isolated native Showcase output passed its object self-test and complete live
input/resize/theme/popup validation again at 2× DPI. This closes the local
source-overlay regression check, not the exact merged-package gate.

The Toolkit initial native source-overlay run exposed a second text boundary:
`PortableTextLine.HasOverflowed` compared native glyph width against the
1/300-DIP-floored formatting width. For an actual 41.373046875-DIP advance,
the floored width was 41.37 while TextBlock's collapse target was the original
41.373046875. The false overflow requested an ellipsis; native Collapse
correctly returned the uncollapsed fitting line, tripping WPF's debug
assertion. Portable overflow now requires more than one ideal-unit excess,
retaining genuine narrow-line overflow. A focused fixture passes 1/1, and the
Toolkit initial native window renders with the source overlay. Its subsequent
debug-assembly live script reached a separate `#if DEBUG`
`ItemContainerGenerator` assertion during AvalonDock menu validation. Rebuilding
the corrected PresentationCore and PresentationFramework assemblies in Release
mode and overlaying those two assemblies into the same packaged Toolkit output
completed the entire native live script, including DataGrid 100k virtualization,
AvalonDock menus, floating windows and keyboard paths, at 2× DPI. This remains
a source-assembly/native-dylib overlay rather than a new exact SDK package;
the final merged-package gate is still required.
The complete portable TextLine fixture class also passes 23/23 after this
change.

The first exact-merge native SDK attempt passed the native host, XAML and
application-lifetime harnesses, then stopped in the Fluent theme harness.
Recognizing the relative Fluent URI now correctly selects system ThemeMode;
that transition replaces the merged dictionary with the active
palette-bearing dictionary. The harness still compared implicit control style
identity with the discarded requested dictionary. It now reads the live
merged dictionary after synchronization. The focused Release native Fluent
harness passes with the exact `5b99b640` runtime. This repairs the gate's
identity expectation; the full exact-package run must restart and pass.

The Windows 11 Parallels VM was inspected for a same-source native-WPF visual
baseline. Its existing `C:\GitHub\LibreWPF` checkout still contains the earlier
`ProGPU.Wpf.MvpApp` samples rather than the current Showcase source; comparing
those different applications would not qualify text parity. The current
isolated source tree is visible on the guest's shared `X:` drive, but no build
was run into that shared macOS worktree because it would cross-contaminate its
package artifacts. The VM was restored to its original suspended state.

A local native SDK run using the #161 source pin and a clean canonical
LibreWinForms package directory passed source host, package audit, Showcase,
and Toolkit gates, then the paid Xceed DataGrid reported the old bounded-atlas
failure. Its app output's native dylib SHA-256
`2626281f3742eaa7f8890fe84bccc50f351a68921d82a094a783260288b3c0f0`
matched the locally staged `external/ProGPU/artifacts/progpu-native/package`
binary. That staging binary was built at 09:58 local, before #161's C++ fix
at 12:20; the passing isolated overlay's dylib was built at 12:17. The
source-version package label had therefore hidden an old local native payload.
This run is invalid as #161 package qualification, not evidence of a new atlas
regression. The hosted gate must stage the successful exact-commit ProGPU Build
artifact, and the paid app must rerun with it. Paid license variables later
became unavailable to the current shell and launchctl; local rerun needs the
user to restore them without sharing secret values.

## Paid Xceed package gate follow-up

The macOS 26 native SDK run at LibreWPF head `a8c942360` passed packaged
Hello, Showcase and Toolkit before the licensed paid Xceed DataGrid stopped
its first native frame: the retained ProGPU path batch exceeded the 4096²
atlas. An isolated packaged-app output overlay, with no source-project
fallback, showed that enlarging the path atlas exposed a second ProGPU error:
per-point guideline snapping collapsed a filled path's X bound to one float
coordinate after earlier scene admission. Both fixes are in
[ProGPU #161](https://github.com/wieslawsoltes/ProGPU/pull/161), which passed
45/45 checks and merged to `main` at
`62b67e6cf34addff2e2bdc7ef959a3c50694939a`. LibreWPF has a local
submodule pin but has not yet qualified the merged package. The overlay
subsequently reached Xceed's virtual
DataGrid GPU input check. That check needed a real query for the newly changed
scene before requiring device-index residency; the source check now performs
one and retains its existing owner/count assertions. With these two ProGPU
fixes and that assertion ordering, the isolated paid Xceed native live gate
passes at 1180×760 logical, 2360×1520 pixels, 2× DPI, including full viewport,
large-scroll budget and GPU hit testing.

This is local overlay evidence, not final merged-package evidence.
[LibreWinForms #30](https://github.com/wieslawsoltes/LibreWinForms/pull/30)
passed 7/7 checks and merged as
`7164c7ed9b9b93175d0eec9e0cee2a28dcc61310`, with the same ProGPU #162
merge pin. LibreWPF must commit both exact merge pins; its current-head CI and
remaining SDK/platform gates must pass before #115 is marked ready or merged.

After the Xceed gate, the current SDK package output also built the default
`ProGPU.Wpf.SciChartApp` with zero errors; its renderer validation and real
`Application.Run` startup both exited successfully in `NativeMilWgpu` mode.
The focused SDK graph guard passed (1/1). This default chart application does
not enable the separate commercial SciChart binaries, whose native runtime
compatibility remains a documented broader DirectX integration gap.

The full local WPF test assembly initially reported 17 failures: 13 lacked a
native runtime on the test process path, two source guards still read the
retired LibreWinForms portable host, and two guards expected obsolete source
forms. With the ProGPU native build and stock WebGPU runtime staged, 1,786/1,790
passed. The live `src/LibreWPF.WinFormsCompat/WindowsFormsIntegration` host now
restores typed GPU image carriers and native direct owner-draw painting, retaining
pixel bitmap rendering only when those capabilities are unavailable. Its Release
project build succeeds. The guards now inspect that live host, the current
retained hit-query counter form, and actual floating-host diagnostics without
allowing private reflection. The final full assembly passes 1,790/1,790 on
macOS ARM64 with the local ProGPU #161 native runtime. This is source-test
evidence; the final SDK package and platform gates are still required.

The current-source SDK package-production command completed, but the optional
`LibreWPF.WinFormsCompat.WindowsFormsIntegration` assembly is not part of
`LibreWPF.Transport`. Its separate Release package probe contains
`lib/net10.0/WindowsFormsIntegration.dll` with SHA-256
`22a26af872d3c86dba7134cbed8ec201238d1fd1c045205fae2fdaae0ad76717`,
identical to the built implementation. That probe is not the canonical
LibreWinForms package lane or a release bundle. The canonical lane requires
LibreWinForms and LibreWPF to pin the same ProGPU commit. LibreWinForms #30
carries that matching update; its 7/7 green CI and merge are complete. The
final LibreWPF repin and package gate remain outstanding.

## Merged ProGPU and LibreWinForms handoff

ProGPU #139 merged as `86f2f766d1f8e6b4041fa184de0fe9d03ae2840f`.
Its own main-branch [Build 34824026135](https://github.com/wieslawsoltes/ProGPU/actions/runs/34824026135)
completed successfully and published native package artifact `10340618392`,
version `0.1.0-preview.3052.ci`, with nuspec repository commit matching the
merge commit. The exact NuGet consumer passes on macOS ARM64/Metal, Windows VM
x64/system WARP and Parallels D3D12, and Windows VM native ARM64/system WARP.
Each returns the final ABI 4, Dawn ABI 1, one-draw/16,384-pixel marker. This
does not substitute for a packaged LibreWPF application test. LibreWinForms #29
merged as `625befd5f7140343f80dbcf3185c23f15ca21318`; LibreWPF #115 pins
both merge commits.

The exact merged native dylib targets macOS 26. LibreWPF's hosted native SDK
gate therefore uses a macOS 26 runner and stages the restored source-closure
`libwgpu_native.dylib` separately from the ProGPU native package. Locally,
the source-built native MIL host, XAML, application lifetime and Fluent theme
harnesses pass on macOS 26 with the exact merged native runtime. The package-
mode Hello app and Showcase validation pass. Showcase Application.Run exposed
that its per-app restore cache was not also used as `NuGetPackageRoot` for
transitive target imports: the resulting output omitted the packaged
`LibreWPF.FluentSymbols.ttf`, then correctly rejected an unresolved null-shape
run for `Segoe Fluent Icons, Segoe MDL2 Assets`. Aligning both roots copies the
font byte-for-byte (SHA-256
`2b1cef154adcd63aa3538b76e46d41f4d284277a8722b938bfe8996fd4874bd4`)
and makes the packaged Showcase Application.Run check pass. The SDK now owns
that alignment and asserts the font is present before Showcase tests. The
remaining full package-mode and latest-head CI gates must still complete
before LibreWPF #115 is merge-ready.

## Current-head package handoff

ProGPU Build [34819727963](https://github.com/wieslawsoltes/ProGPU/actions/runs/34819727963)
completed successfully with 54/54 checks, including all native package
consumer jobs. [ProGPU #139](https://github.com/wieslawsoltes/ProGPU/pull/139)
merged to `main` as `86f2f766d1f8e6b4041fa184de0fe9d03ae2840f`, whose tree
is identical to tested PR head `54adc6a005119d40fc25615b3823844c21453690`.
The Build published `progpu-native-package` artifact `10338682752`, version
`0.1.0-preview.3051.ci`. Both inspected Backend and Native nuspecs identify
repository commit `54adc6a005119d40fc25615b3823844c21453690`; Native depends on
Backend and Dawn at the same exact version. Downloaded package SHA-256 values:

| Package | SHA-256 |
| --- | --- |
| ProGPU.Backend | `c19347bd04a262e244e2fcebf9011cae34af62b595412721d8e0f4d15a135552` |
| ProGPU.Backend.Native | `f3320496ee5a54401707060bad76925594027951b77541c601e525ecbdf8bea5` |
| ProGPU.Backend.Dawn | `45ff4ead3f9be3dfd38d35086bd7f400930cd75ad617ad4555e42afcb52ab965` |
| ProGPU.Backend.Dx12 | `8287589556f0767a4968015d3b2723fedc66adb5eb5a06d4ae2b5792a406154c` |

The unchanged full `ProGPU.Native.PackageConsumer` was published from this
clean ProGPU source with project references disabled, this downloaded NuGet
feed, version `3051`, and an isolated restore cache. Its assets have zero
project libraries. The exact-package macOS ARM64/Metal process, Windows VM
emulated x64/system-WARP and default-Parallels-adapter processes, and native
Windows ARM64/system-WARP and default-Parallels-adapter processes exited zero
with the final ABI 4, Dawn ABI 1,
one-draw/16,384-pixel smoke marker. Windows WARP used the system
`d3dcompiler_47.dll`, `D3D12Core.dll`, and `d3d10warp.dll`; no app-local WARP
or compiler override was supplied. The guest's `progpu_native.dll` SHA-256
`6449641b46d4568ffaf8c185e88366f30c2e52d97a683c44a23d6c893e818490`
matches the published package payload. The stock package `wgpu_native.dll`
hash is `4971fce5b4d93fc10b65d01cbcc57f9f35ad1bc479e654737974e4ad2e265be6`;
no source overlay was applied. Logs are under the external core-release staging
directory as `osx-arm64-package-3051-metal.log`,
`win-x64-package-3051-system-warp.log`,
`win-x64-package-3051-default-adapter.log`, and
`win-arm64-package-3051-system-warp.log`, and
`win-arm64-package-3051-default-adapter.log`. Native Windows ARM64 selected
`Parallels Display Adapter (WDDM)`/D3D12 on the default path. Its guest native
payload hashes match the exact package publication:
`progpu_native.dll` `d2a17d872fbe1c68de77fd14022632b187507dbce9f03170de5fb655a6bd5a9b`
and stock `wgpu_native.dll` `9f73e41536b3bd96a0a44692ea65888c9de004b19fbf5de90489768667fbbdbc`.

Separately, the current-source Windows x64 consumer passed both system WARP and
the default Parallels D3D12 adapter using the current CI runtime payloads. This
is useful integration evidence, not the NuGet package result above. Further
downstream SDK/application gates remain open. The ProGPU producer is qualified
and merged. LibreWPF's exact-runtime staging requires a successful Build for
the pinned merge commit `86f2f766`, rather than reusing the tree-identical PR
head's Build; main Build
[34824026135](https://github.com/wieslawsoltes/ProGPU/actions/runs/34824026135)
is running. Do not infer that LibreWinForms/LibreWPF package/application gates
are complete.

## Current-head Windows sample oracle follow-up

ProGPU `54adc6a005119d40fc25615b3823844c21453690` Build
`34819727963` published `progpu-directx-oracle-win-x64` (artifact
`10337999613`). It contains native Microsoft D3D12HelloTriangle and
D3D12HelloTexture captures from the pinned DirectX-Graphics-Samples commit
`213dd4fd4918ea009dd8f35adee1aff1f2ecaba4` (Agility `1.618.3`), together
with ProGPU's D3D12 frames. Running the repository's unchanged
`progpu-compare-directx-sample-oracle.py` on both pairs yields exact 1280×720
pixels: maximum, mean, every probe and channels/pixels over three all zero.
The outputs are retained under the external core-release diagnostic directory's
`progpu-directx-oracle-54adc6a0` folder. The exact-head hosted differential
artifact `progpu-directx-sample-differential` (`10338942628`) also passed:
both pinned 1280×720 Microsoft frames compare byte-identically with ProGPU's
D3D12, Metal and Vulkan candidates. These two sample scenes are not full
DirectX API or final LibreWPF application qualification.

## Aligned LibreWPF canonical package gate

ProGPU `main` merge `86f2f766` and LibreWinForms PR head `7c583b29` (retained
by its tree-identical merge `625befd5`) build the canonical WinForms, WPF
foundation and WindowsFormsIntegration source graph locally on macOS. The first
isolated run stopped before package production because ProGPU's `ACadSharp`
submodule was not initialized there; clean CI checks out submodules recursively.
After initialization, the run produced the actual Design and integration
assemblies and packages, then exposed a WPF package-entry verifier race: its
`unzip -Z1 | grep -Fxq` pipeline can return SIGPIPE 141 under `pipefail` even
when `lib/net10.0/System.Windows.Forms.Design.dll` is present. An unchanged
package reproduces that exit in a 100-attempt loop. The verifier now consumes
the complete zip entry listing before exact `grep` checks, still rejecting a
missing required entry. Bash syntax, the documentation verifier, present and
missing entry checks pass. A rerun in a fresh package output directory passes
the entire canonical source and package gate, ending with its explicit
LibreWinForms `7c583b29` / ProGPU `86f2f766` success marker. Its log is
`wpf-canonical-winforms-merged-progpu-pipefix-clean.log` in the external
core-release staging directory. This is a local source/package gate, not the
final SDK consumer or live application gate; final-head WPF CI remains required.

## Source and package identity

ProGPU source: `0ac6a5ff79d79e7a6e5a2e5b0488955cfb2256d7`, incorporating
main `cde81083be9533761e2fe5573adf5d27015e5324`. Exact Build:
[34808350830](https://github.com/wieslawsoltes/ProGPU/actions/runs/34808350830),
package version `0.1.0-preview.3047.ci`, artifact `progpu-native-package`
(ID `10334860867`). Both inspected Backend and Native nuspec repository commits
match that full source SHA; their dependencies select the same package version.

The original consumer project is built from a clean detached checkout of that
commit, with `ProGpuNativeUseProjectReference=false`. Its assets file contains
zero project libraries: Backend, Native and Dawn all resolve as versioned NuGet
packages. No diagnostic assembly/native-library overlays are used. This is
stronger than the earlier mixed-source diagnostic runs, but remains ProGPU
package evidence, not a complete LibreWPF SDK application/package qualification.

Package SHA-256:

| Package | SHA-256 |
| --- | --- |
| ProGPU.Backend.Native | `1270a7e2003d9fe58ef7170da355e8e72d9c5bc486d234230223b4ae391b116c` |
| ProGPU.Backend | `1dcf99871b8738d3f3d5ccd8094b01f98752336f624e92999e7ac441db12080e` |

Clean source, the downloaded feed, isolated NuGet cache and macOS publication
are under `/Volumes/1TB-macOS/progpu-core-release.xtwndj/`. Windows publications,
the bounded guest runner and stdout/stderr logs are under
`artifacts/native-release-qualification.9mQdMk/`. The external SSD avoids filling
the internal volume; no existing artifacts or unrelated work were deleted.

## Actual full package consumers

All publications are self-contained Release builds of
`tests/ProGPU.Native.PackageConsumer/ProGPU.Native.PackageConsumer.csproj` using:

```text
-p:ProGpuNativeUseProjectReference=false
-p:ProGpuNativePackageVersion=0.1.0-preview.3047.ci
-p:ProGpuNativePackageSource=<downloaded exact CI feed>
```

All four runs below execute the **full consumer**, not `--mil-only` or the
owner-query-only probe. They pass ABI checks, both MIL exports, native document
rows/inline/positioned paragraph contracts, cubic control-hull rendering,
retained MIL rendering (38 resources, 11 draws, 174,080 coverage bytes), pixel
readback, native memory inventory, point/list/region owner queries, 16 repeated
waits, participation policy, first-query bounds/ellipse ordering and owner/
generation isolation. Each process exits zero and ends with the package smoke
result: ABI 4, Dawn ABI 1, one final draw and 16,384 pixels. This fixture does
not represent all WPF controls or the full Direct2D/Win2D API surface.

| Process/platform | Actual selected adapter | Result |
| --- | --- | --- |
| Windows ARM64 VM, `--software-adapter` | Microsoft Basic Render Driver / D3D12 | Pass |
| Windows emulated x64 VM, `--software-adapter` | Microsoft Basic Render Driver / D3D12 | Pass |
| Windows ARM64 VM, no adapter override | Parallels Display Adapter (WDDM) / D3D12 | Pass |
| macOS ARM64, no adapter override | Apple M3 Pro / Metal | Pass |

Query and compiler preferences are `auto` in all Windows runs. Diagnostics
confirm actual system FXC and **OrderedStages selected from Automatic**, not an
explicit query-stage override. The system WARP module is
`C:\WINDOWS\SYSTEM32\d3d10warp.dll`, version `10.0.26100.9278`; system
`d3dcompiler_47.dll` is `10.0.26100.9444`. The stage rejects an app-local WARP
DLL, and no external compiler directory is supplied. The default Parallels
adapter separately selects the existing ExplicitShader image path and RasterShader
glyph path under Automatic/Fastest; no CPU query fallback is introduced.
The Mac keeps automatic single-pass queries and its normal native compute path.

Parallels 27.0.1 (58670), Windows `10.0.26200.9445`, installed matching Tools,
four vCPUs and 6 GiB RAM were rechecked. Existing PowerShell 7 runs the scoped
script under the existing LocalMachine RemoteSigned policy. No policy, VM
configuration, compiler, adapter default or system file is changed. Each run
copies only the exact publication to a fresh named guest directory, keeps the
owned child handle, and retains the 300-second bound. No child timed out.

Windows payload SHA-256:

| Payload | SHA-256 |
| --- | --- |
| ARM64 progpu_native.dll | `f9a94eb629640be3e22f6f989a5cc5f1023834357c22ce33bbadee1167ab39e4` |
| x64 progpu_native.dll | `0fa90664f532df54cf4480fdf6e831a67ebc541dddaa3dcc414e963f19d8afd0` |
| ProGPU.Backend.dll (both) | `06548e75f5d4e3adf046d6fead5214a0de263b04894593307612e7fceed72651` |
| ProGPU.Backend.Native.dll (both) | `d2e35048eb7736baf3e11d047b7b12ff2a5475653e8bb846a441502c507d8958` |
| ARM64 stock wgpu_native.dll | `9f73e41536b3bd96a0a44692ea65888c9de004b19fbf5de90489768667fbbdbc` |
| x64 stock wgpu_native.dll | `4971fce5b4d93fc10b65d01cbcc57f9f35ad1bc479e654737974e4ad2e265be6` |

## Current-head cross-platform oracle evidence

The exact Build's published differential JSON was downloaded and inspected:

- Microsoft **D3D12HelloTriangle** and **D3D12HelloTexture** compare the captured
  native Windows sample with the equivalent ProGPU D3D12, Metal and Vulkan scenes.
  Both 1280×720 fixtures are byte-identical on all three candidates: maximum,
  mean and every probe difference are zero. These are the two pinned sample
  contracts, not a claim that arbitrary Microsoft DirectX samples run unchanged
  on non-Windows systems.
- The portable Win2D Canvas fixture passes its existing bounds: Metal changes
  one pixel, Vulkan 81 pixels, maximum channel difference one in both cases.
- The portable Direct2D COM fixture passes: Metal changes 304 pixels, Vulkan
  165, maximum channel difference one and zero pixels over one. These are
  cross-backend scene comparisons, not complete Windows COM ABI/behavior parity.

Artifacts: `progpu-directx-sample-differential` (`10334825891`),
`progpu-win2d-canvas-differential` (`10334442195`),
`progpu-direct2d-webgpu-differential` (`10334054690`). The image-brush WPF
differential artifact (`10334257767`) is also retained. No tolerance or fixture
was changed during this qualification. Captures are under the external staging
directory's `oracles/` subdirectories.

## Remaining merge gates at this checkpoint

Current-head native producer builds, both Windows explicit/automatic ordered-query
parity jobs, the DX12 NativeAOT package consumers and macOS/Linux package consumers
have passed. Both general Windows package-consumer jobs subsequently hit their
15-minute job limit, leaving Build 3047 terminal canceled. Their logs show
successful assertions until cancellation, not a completed qualification. The
exact-runtime staging helper correctly rejected this Build and produced no
qualified staging directory.

ProGPU `a8afeab6f53c0b8bdd470cf9d2d9283f6c1bdf8d` now splits each Windows RID
into core, drawing, visual and guideline jobs, retaining all nine independent
JIT and NativeAOT cases and the original deadlines. Runtime code and assertions
are unchanged. Selector coverage, Bash syntax, ShellCheck, Actionlint and release
documentation checks pass locally. The new exact
[Build 34811802371](https://github.com/wieslawsoltes/ProGPU/actions/runs/34811802371)
must pass; the older package results above are historical evidence, not proof
for this new commit. See the
[ProGPU scheduling contract](https://github.com/wieslawsoltes/ProGPU/blob/a8afeab6f53c0b8bdd470cf9d2d9283f6c1bdf8d/docs/native-package-consumer-groups.md).
Do not restart live jobs, advance dependency pins, or merge from partial results.

After exact ProGPU qualification, align LibreWinForms and LibreWPF to the same
qualified ProGPU commit, produce the exact WPF Windows payload/SDK packages,
run the native application gates and require downstream CI to pass. The earlier
Showcase checkpoint report is still an explicitly diagnostic WPF assembly graph.
Preserve the ordered ProGPU → LibreWinForms → LibreWPF merges and all final
Release/performance/platform gates. Broader API work is deferred from this core
delivery, not declared completed; ActivityMonitor remains outside scope.
