# LibreWPF native MIL core delivery

## Scope decision — 2026-09-08

The user requested a focus on core major features and finishing. The immediate
delivery milestone is an end-to-end usable LibreWPF application rendered by
ProGPU C++ MIL, alongside the existing managed portable mode. This is a delivery
sequence, not a claim that the broader DirectX/Direct2D/Win2D goal is finished.

Do not open another general compatibility family or spend successive batches on
isolated geometry edge cases unless it blocks one of the core acceptance paths
below. Finish a vertical application path before expanding API breadth. New
findings go into the appropriate core blocker or deferred expansion list; they
do not automatically expand the release checklist.

## Active completion queue

Work through these application-level batches in order. This queue, not the
historical checkpoint suggestions below, determines the next work. Update the
current row with concrete remaining blockers rather than starting another
subsystem roadmap. None of these rows is runtime-qualified yet.

| Order | Implementation batch | Exit to the next batch |
| --- | --- | --- |
| 1 — startup connected, qualification pending | Native package activation: source-built harness and complete package MVP, including Windows x64/ARM64. Popup ownership, pre-host services, frozen media/input selection, typed text/document/geometry/bitmap routes and native host factory registration are connected. The SDK now enters those portable paths on Windows for explicit native selection, rejecting unsupported process architectures or failed registration. Native desktop payloads use da36a718; corrected Windows transport builds and all 23 selected SDK packages are produced. Isolated package-mode MVP compilation succeeds; source/application runtime behavior remains unqualified. | Continue concrete acceptance actions below. Any newly identified startup/source-media blocker reopens this row; package compilation is not first-frame or Windows runtime proof. |
| 2 — current | Application closure: use the same MVP, Toolkit/AvalonDock, license-controlled Xceed and existing SciChart gate. Current concrete input blocker: native MIL has opt-in index emission for analytic primitives/plain path fills, image destination quads and source-bounded glyph runs, but lacks complete application hit coverage; host owner queries still use the managed index. Immutable source-owner snapshots and compositor/scene-qualified query tokens are connected. Complete remaining stroke/clip/cache/effect coverage and native host routing before enabling the option. Finish required text/selection, scroll/clip, popup/input, resize/DPI, content/effect/cache updates and close/reopen/device-loss ownership. Reuse existing implementations; fix concrete missing connections. | Each required action has an implemented path and authored regression coverage. Record any known blocking branch against that action; do not reopen already connected subsystems for optional refinements. |
| 3 | Feature freeze, final qualification and delivery: build complete platform artifacts and packages, execute the existing cross-platform/Windows comparison and application gates, fix failures, and bring both PRs' required CI to green at the delivery commits. | Record exact-head package consumption and required gate results, with explicit failures or environment/license limitations. Only then report the core release delivered. |

The native hit-index producer checkpoint and its build-only artifact versions are
recorded in [the producer report](../reports/native-mil-hit-index-producer-2026-09-09.md).
The image connection also retains DrawingImage destination input independently
of flattened render contents through paired native/managed logical scopes,
including authoritative empty drawings. This does not close remaining outer
stroke/clip/cache/effect coverage or select native host queries.
The [source opacity checkpoint](../reports/native-mil-source-opacity-input-2026-09-09.md)
connects native ordinary/animated drawing opacity and unmasked visual groups,
plus paired managed command policy. The
[retained source connection](../reports/native-mil-retained-source-input-2026-09-09.md)
now preserves input for opacity-culled typed visuals without rendering or
size-based hit substitutes. Its embedded-source invalidation and strict clip
handling are implemented, not runtime-qualified. Remaining native coverage and
query routing stay current; input-neutral source opacity does not bypass effects,
spatial masks or caches.
This does not refresh the complete package feed or qualify host input. The
following package-production paragraphs retain their earlier provenance.

The [MVP line-input connection](../reports/native-mil-mvp-line-input-2026-09-09.md)
adds ordinary geometry-line strokes to the native producer and fixes diagonal
square-cap broad-phase bounds in both encoders. It traces the real `MvpShapeLine`,
not a reduced application. Other geometry kinds, stroke batches (including the
MVP's closed stroked path), caches/effects and native host routing remain open.
Authored fixtures and build-only artifacts do not qualify those interactions.

Package input refresh: macOS, Linux and Windows ARM64/x64 native payloads now
share ProGPU da36a718 in the same staging root. Both Linux refresh builds completed
all 709 compile/module/link steps. ProGPU.Backend.Native.0.1.0-preview.55.nupkg
was then produced successfully with normal required-RID/provider checks enabled.
Windows managed/IJW production and the selected LibreWPF package set are now also
complete, as recorded below; package consumption remains unqualified.
Do not bypass Windows transport checks or claim package/runtime qualification
from this build-only result. See the
[macOS build record](../reports/native-mil-macos-payload-refresh-2026-09-09.md) and
[Linux refresh record](../reports/native-mil-linux-payload-refresh-2026-09-09.md).

The twenty ProGPU-side packages selected by the existing SDK gate are now produced
at da36a718 using the lane's preview.55 version. The source-built portable WPF
transport, eight theme/Ribbon projects, bridge and four real harnesses also compile.
These source/project-reference builds did not consume or execute the new packages.
LibreWPF.ProGPU and LibreWPF.Sdk preview.45 packages also build successfully, giving
22 of the 23 explicitly selected SDK packages at that checkpoint. The corrected
Windows x86/x64/ARM64 inputs now also produced LibreWPF.Transport preview.45,
completing the selected 23-package development feed. This is not a release bundle,
isolated package-consumption result or runtime qualification; see the
[SDK build record](../reports/native-mil-sdk-package-build-2026-09-09.md).

Windows production has resumed in the existing Parallels guest. ProGPU da36a718
closes strict MSVC compilation failures in native MIL/text and test fixtures;
both ARM64 and x64 builds completed, each staging three DLLs/seven SDK libraries.
The x64 build completed all 356 steps without further fixes.
Windows SDK activation is connected in the follow-up below; managed/IJW and
package production also completed. Both macOS architectures use da36a718, completing 62
incremental compile/link steps each and staging both providers/six SDK archives.
Both Linux inputs have also been refreshed. This is build-only progress, not
runtime qualification; see the
[Windows build record](../reports/native-mil-windows-payload-build-2026-09-09.md).

Windows managed production now uses the current PowerShell 7 host without a
wrapper-imposed execution-policy override and correctly installs the pinned SDK
without an invalid runtime selector. The SDK and IJW packs restored, but the
installed VS 2022 MSBuild cannot load a required SDK build-task interface.
The alternate engine exposed mixed ARM64-host/x64-runtime restoration; the package
entry now requires an x64 SDK build host while preserving all output RIDs and
rejecting incompatible existing SDK roots. The corrected x64 SDK compiled
PresentationBuildTasks; PresentationCore then stopped at missing Visual Studio
C++ targets under the dotnet engine. The pinned SDK requires MSBuild 18.6 or newer
and source C++ requires v145. VS 2026 Build Tools is now installed alongside VS
2022, with MSBuild 18.10 and the required C++/CLI target tools. The first default
Visual Studio retry stopped before source compilation because .NET task hosting
had no `DOTNET_HOST_PATH`. The package entry now publishes the validated x64
host executable as well as the SDK directory. That retry passed toolset setup,
then exposed the missing Visual Studio .NET SDK resolver: MSBuild's assembly
binding points to its absent directory. Installation of the official .NET SDK
component completed for the existing VS 2026 instance; the resolver files are
present. The resumed build staged PresentationCore/DirectWriteForwarder/IJW for
all three Windows RIDs, but reported analyzer-host load warnings. Those outputs
and logs are preserved separately. The corrected full `-Rebuild` compiled both
PresentationBuildTasks targets with zero warnings/errors, then failed in the
x86 WindowsBase reference assembly: project-reference filtering removed its
System.Xaml-ref dependency after Clean. The target now removes only known WPF
projects absent locally, preserving ordinary configured references and their
metadata. The corrected rebuild now completed for x86, x64 and ARM64 with zero
warnings/errors in each graph and staged all required Windows managed/IJW inputs.
This closes the build blockers, not package consumption or runtime qualification.
The pinned SDK and payload requirements remain unchanged. Corrected package
inputs are exported and packed. See the
[managed build-entry record](../reports/native-mil-windows-managed-build-entry-2026-09-09.md).

Windows native SDK startup now enters the existing typed activation path for
x64/ARM64 processes instead of rejecting Windows or returning before registration.
Native media/provider selection precedes source startup; failed registration and
unsupported process architectures still throw. Ordinary managed Windows startup
is unchanged. The source-backed prerequisite trace covers frozen media/input,
hidden-source ownership, common media, popup desktop/DPI/input units and native
first-frame compilation. The full isolated package MVP compiles, but no app was
launched. Its existing final gates now assert frozen portable media and an actual
native session frame for compiled native mode. Continue application closure,
not general compatibility expansion. See the
[Windows SDK connection record](../reports/native-mil-windows-sdk-activation-2026-09-09.md).

The Toolkit top-header CharacterEllipsis connection is now implemented through
typed native collapsed views and source symbol/range/interaction mapping, with
native and source-header fixtures authored. This removes the identified blanket
source rejection, not the need for application/pixel/performance qualification.
Continue the startup/package and concrete application closure queue above; do not
expand unrelated trimming API families before freeze. See the
[connection checkpoint](../reports/native-mil-toolkit-text-collapse-connection-2026-09-09.md).

Portable Application.Run now follows source shutdown policy across native polling
host retirement. It can hand off to a remaining window or wait for posted work
without windows under explicit lifetime; existing-host entry preserves visibility
without Show/activation or dialog hints. The existing application harness includes
three authored shutdown-mode scenarios in the SDK gate. This is a source-backed
lifetime connection, not visible multi-window or package qualification. See the
[application lifetime checkpoint](../reports/native-mil-application-lifetime-2026-09-09.md).

The MVP's no-focus F10/Alt menu-entry fallback now shares a typed active-source
resolver with default access keys. It no longer calls user32 unconditionally or
chooses the first registered portable root. Actual source activation, visibility,
dispatcher, disposal and modal admission select the scope; authored regressions
are not runtime qualification. See the
[menu-entry checkpoint](../reports/native-mil-active-menu-scope-2026-09-09.md).

The MVP editor caret now selects actual portable source ownership before Win32
caret calls. ProGPU owns an optional hidden native caret mirror for the real
Windows host; source drawing and caret semantics remain authoritative. Source
and native GUI fixtures are authored, with execution deferred. This closes the
identified unowned SetCaretPos route, not Windows package admission or native
accessibility/IME qualification. See the
[caret checkpoint](../reports/native-mil-native-caret-2026-09-09.md).

Editor drag admission and context-menu client clipping now select by source
ownership on Windows as well as Unix. Accepted drops request actual owner-host
activation through the existing popup owner chain; denied moves report None.
Source fixtures cover admission, activation rejection/lifetime and client-DIP
clipping. This is an implementation connection, not end-to-end drag transport or
visible menu qualification. See the
[editor drop checkpoint](../reports/native-mil-editor-drop-routing-2026-09-09.md).
Portable DragDrop.DoDragDrop still declines outgoing source drags. End-to-end
editor-to-editor text dragging therefore has a known implementation gap; do not
treat the ingress connection or its authored fixtures as source-drag completion.
The default Silk.NET service only supplies file drops; OS text-drag transport is
also still an implementation requirement for external text dragging.

Native Cocoa popup ownership now uses ProGPU's checked hidden-window parent
configuration. A selected native popup is disposed when owner setup rejects or
throws on every platform, rather than proceeding unowned. The main MVP menus and
ComboBoxes still require visible qualification; this is not genuine NSPanel
modal-popup hosting or automatic AppKit session admission. See the
[popup ownership checkpoint](../reports/native-mil-cocoa-popup-ownership-2026-09-09.md).

Retained drawing-image clear/refill now distinguishes known empty drawing bounds
from unavailable metadata. Source DrawingGroup identity and dependencies survive
empty content; native MIL uses its existing null-drawing image contract and
managed image/tile replay skips only authoritative emptiness. The existing native
host harness includes real source-built image and image-brush clear/refill
fixtures, authored and compiled but not executed. This is content-update closure,
not offscreen RenderTargetBitmap support or runtime/package qualification. See the
[image update checkpoint](../reports/native-mil-empty-drawing-images-2026-09-09.md).

The `Visual` geometry-hit consumer through `PathGeometry.HitTestWithPathGeometry`
is now connected to ProGPU's shared native fill-relation query under portable
media selection. The existing native host harness includes region selection
inside/outside an actual clip; execution is deferred. This closes the identified
source import route, not Windows SDK admission. Continue the package MVP's startup
and common resource path. Path-length animation or neighboring APIs enter this
queue only if a required application action uses them; do not exhaustively scan
unrelated API families before feature freeze.

General DirectX/Direct2D/COM/Win2D completeness, advanced presentation and
nonblocking optimizations remain in the broader backlog. They cannot delay this
core release solely because they are unfinished. Existing acceptance gates,
reflection-free contracts, GPU-first/SIMD requirements and explicit unsupported
behavior remain mandatory. Documentation records closure and evidence; a new
plan or another compiled fixture alone does not close an application batch.

## Finish-first rule — renewed scope decision

The four milestones below are the immediate finish line. API coverage counts,
additional compatibility families and completion of every presentation consumer
are not separate prerequisites for that finish line. This rule supersedes the
next-step suggestions in the historical checkpoints below.

Before starting or continuing an implementation batch, identify the existing
acceptance application, user action, blocking source path and bounded outcome.
Source inspection is sufficient to identify an implementation blocker while
runtime validation is deferred; label it as source-backed, not reproduced.
If no core dependency can be identified, park the work in the broader backlog.
Do not spend consecutive batches improving a subsystem without closing an
application-level blocker or explaining the remaining dependency.

The next implementation pass is application integration: trace the native host
harness and package-mode MVP from startup through first frame, content update,
popup, resize and shutdown; close their known unsupported/lifetime branches.
Then apply the same closure to Toolkit/AvalonDock and the license-controlled
paid Xceed path. Preserve the existing SDK gate, including its SciChart coverage;
this scope decision does not authorize deleting tests or reducing that gate.

Full-surface presentation and equal X/Y DPI are the existing native host profile,
not a claim of runtime qualification. Ordinary desktop resize, DPI transitions,
window chrome and popup placement remain core requirements. Trace their actual
platform geometry before deciding whether partial viewports or unequal-axis
mapping are needed. If they are required for those ordinary actions, implement
the necessary shared presentation behavior; otherwise keep advanced mappings
explicitly unsupported and defer their remaining consumers. Do not force a
contrived host configuration, silently change scaling, or omit an application
feature merely to fit the existing profile. In particular, do not begin the
advanced 3D presentation rewrite solely to remove the global presentation guard.

Preserve current implementation work, including unfinished changes, without
equating it with delivered functionality. Correctness, reflection-free contracts,
GPU-first/SIMD policy and resource ownership remain mandatory for core code.
After core implementation closure, freeze feature development and run the final
qualification phase below; only qualification failures reopen core code work.

## Fixed core milestones

| Milestone | Required outcome | Current implementation evidence / open work |
| --- | --- | --- |
| 1. Native application pipeline | Source-built WPF state and updates reach canonical MIL, C++ scene compilation and WebGPU presentation; managed portable mode remains independently selectable. | `WpfNativeMilSceneCompiler`, `WpfNativeMilCompilationSession` and `RenderNativeMilFrame` exist. Close remaining producer/renderer failures encountered by the core applications; do not replace missing native rendering with silent managed replay. |
| 2. Essential desktop interaction | Windows, scrolling, editing, selection, menus, ComboBoxes, tooltips, popups, input, resize and DPI transitions render and respond through typed contracts. | Popup ownership/lifetime/input routing and actual-surface placement selection are implemented, not runtime-qualified. Mixed-DPI coordinate transport, interactive capture and application fidelity remain open. Partial viewports and nonuniform DPI remain explicitly rejected. |
| 3. Essential rendering and lifetime | Core application text, bitmap/vector content, brushes, clipping, opacity, common effects and caches update correctly; device/surface loss and resource release do not leave stale content or invalid handles. | Many implementations exist, but final output/lifetime qualification is absent. Fix missing shared algorithms in ProGPU, not WPF-local renderers. Retained update/handle stability and external image leases require review in the full application path. |
| 4. Deliverable and qualification | Installable LibreWPF packages run representative applications on macOS/Linux and Windows Parallels; native/managed/native-Windows comparisons and exact-head required CI pass. | Packaging and host/SDK gates exist. New changes are not qualified. Full renderer and Windows builds, final runtime/image/lifetime/performance runs and CI remain mandatory. |

## Acceptance application paths

Use the existing source-built native host harness, package-mode MVP application,
Toolkit/AvalonDock application and paid Xceed application when licenses are
available. Extend these paths to exercise the core milestones; do not create a
special stripped-down application and treat its success as product completion.

The interactive path must include opening/closing a popup, changing text and
selection, scrolling clipped content, resizing and moving between DPI scales,
and closing/reopening windows. Rendering must include ordinary text and images,
filled/stroked geometry, nested clip/opacity, Blur/DropShadow and cached content
that changes after its first frame. Retained replay must not show stale content
or silently omit unsupported commands.

DirectX work is on the critical path only when needed by these applications or
their explicitly selected interop samples. Preserve texture/fence/device-domain,
format, alpha, pitch and device-loss correctness. Keep configured GPU/SIMD
fallback semantics; performance defaults may only claim speed after measurement.

## Historical core application closure checkpoints

These checkpoints preserve implementation provenance and the state at each
commit. Their next-step text is historical, may be superseded by later changes,
and must not be treated as an additional active backlog. Use the active completion
queue above for current priorities.

Current macOS package inputs (2026-09-09): ProGPU 2ab498be adds explicit build-only
osx-arm64/osx-x64 target selection with matched compiler architecture, pinned
linker input and staging RID. The clean isolated checkout completed both builds
(62 ARM64 and 193 x64 incremental steps), requiring both providers and all six
SDK archives before staging. Source-contract fixtures compile with 0 warnings
and errors. No tests, apps or CI checks ran. Windows native/managed payloads,
current-commit Linux refresh, complete packages and all runtime qualification
remain open; complete package gates and Windows admission were not weakened.
See the [build input record](../reports/native-mil-macos-payload-refresh-2026-09-09.md).

X11 About-dialog native-state connection (2026-09-09): ProGPU 91104e9f owns a typed,
thread-bound EWMH modal-hint lease, preserving preexisting/unrelated state and
queued-removal/reopen ownership. LibreWPF acquires it for the actual X11 dialog
and releases it before source focus completion, native Hide and destruction.
Unsupported hint admission is explicit. Source ShowPortableDialog now hides the
real Window after host admission/pump failure, retaining its identity for retry
instead of leaving it visibly modeless after an exception. Both renderers share
this connection. This is advisory WM state, not full Linux native input gating;
Wayland, Cocoa native popup admission, Windows package startup and final visible
application/WM qualification remain open. See the
[X11 hint contract](../external/ProGPU/docs/native-mil-x11-modal-hint.md) and
[implementation record](../reports/native-mil-x11-dialog-hint-2026-09-09.md).

Linux payload production (2026-09-09): a dedicated no-host-mount Colima build
profile supplied Ubuntu ARM64 without changing the suspended Parallels guests or
active Docker context. Both Linux ARM64 and x64 completed 709 native build steps,
including both renderers, all SDK archives and test/sample/module compilation.
ProGPU 87014829 adds explicit build-only Linux RID selection with aligned Clang
target, target GNU linker/standard-library dependencies, pinned wgpu input and
staging RID, plus isolated build directories. No x64 emulation or link-probe
bypass was used. The authored source-contract graph compiled with 0 warnings and
0 errors; tests and all runtime qualification remain deferred. These Linux inputs
close the missing build-host/payload obstacle, not Linux application modality,
full package production or Windows SDK admission. See the
[production and source-provenance record](../reports/native-mil-linux-payload-build-2026-09-09.md).

Native build-only payload production (2026-09-09): ProGPU now has explicit Unix
--build-only and Windows -BuildOnly modes. Both providers and required native SDK
files compile/stage before returning, while normal no-switch qualification gates
remain intact. Unix build-only shares pinned Dawn header preparation with the
existing verifier rather than executing that verifier to obtain a library.
The clean ProGPU 979f72ca checkout completed a macOS ARM64 run, staging two renderer
libraries and six SDK archives. Contract fixtures compile with 0 warnings/0 errors
but have not run. A repeated graceful Windows VM resume failed with a critical
error and returned to suspended; saved state and VM configuration were preserved.
Windows/Linux payloads, Windows managed transport and complete packages remain
open. See the [production record](../reports/native-mil-build-only-production-2026-09-09.md).

Source dialog completion (2026-09-09): the MVP About-dialog Hide/Close path now
transfers source input/focus cleanup through captured typed ReleaseDialog and
ProGPU ReleaseAfterNative contracts. Source scopes remain blocked until native
completion and unwind inside-out, including out-of-order native completions.
The original focus snapshot is restored only after source gate publication and
its existing synchronization/native-activation/source-identity checks. Managed
ShowDialog finally no longer disposes pending scopes early. Cancellation stays
modal; repeated release/completion is idempotent, and reopening the same dialog
before completion fails explicitly. Both renderer modes share this connection.
Native Cocoa popup admission still blocks automatic AppKit sessions; this source
connection is not modal runtime qualification or package/Windows admission.
See the [source completion contract](../external/ProGPU/docs/native-mil-dialog-lifetime.md#native-completion-before-source-input-and-focus-restoration).
Build commands, authored coverage and qualification limits are recorded in the
[implementation report](../reports/native-mil-source-dialog-completion-2026-09-09.md).

Deferred native dialog Hide (2026-09-09): ProGPU now provides window-scoped native
modal release completion after actual End and identity cleanup, including nested
sessions and callback unwinding. LibreWPF host Hide consumes that completion,
coalesces pending requests, and checks current visibility/disposal and new leases
before changing native visibility. A later Show supersedes the deferred Hide.
End failure retains the host and faults the coordinator rather than retrying a
possibly consumed native token; callback failures do not skip other ready cleanup.
This closes the identified host-Hide ordering branch for an explicitly owned
native session, not automatic About-dialog modality. Source gate-release/focus
restoration and genuine Cocoa native popup admission remain open, as do Linux
modality, package payload completion and Windows SDK admission. Authored lifecycle
and host-source fixtures compile but have not run. See the
[deferred completion contract](../external/ProGPU/docs/native-mil-cocoa-modal-session.md#deferred-native-hide-completion).
Final compile-only results: backend fixtures 0 warnings/0 errors; WPF bridge
fixtures 116/0. See the [implementation and build record](../reports/native-mil-deferred-dialog-hide-2026-09-09.md).

Native package payload build (2026-09-09): clean macOS ARM64 and Intel C++ builds
now produce both wgpu-native and Dawn renderer libraries plus the required SDK
archives. The first Dawn build exposed a raw queue-submit call in retained picture
seed copying; ProGPU `95504c8d` routes it through the existing engine submission,
completion-identity and retirement path. Complete native targets and the authored
ProGPU source-contract fixture compile; no tests or renderer workloads executed.
Artifacts remain an isolated, unqualified two-RID staging set, not a full package.
Windows Parallels resume failed with a critical error, and its bundle listing
stalled; saved state was preserved and user assistance requested. The Windows and
Linux payloads remain missing, so fresh package production/MVP consumption and
Windows SDK admission are still open. See the
[build evidence and remaining prerequisites](../reports/native-mil-core-payload-build-2026-09-09.md).

Package production/qualification separation (2026-09-09): the package-mode MVP's
automatic rebuild invokes the full SDK gate, including application execution,
even when the launcher's individual validation flags are disabled. The SDK script
now accepts an explicit command-line-only `--build-packages-only` option. It shares
the real pack functions and transport/theme/harness compilation, skips the two
early protocol/Avalonia qualification calls, and exits after packing before native
host, application, test, audit, manifest and release-bundle work. The no-argument
SDK/CI/release path retains its gates and ordering. The build-only dotnet wrapper
rejects non-build commands, and authored graph coverage protects the separation
and unchanged workflow invocations. No runtime/payload admission is weakened.
See [package production](progpu-wpf-release.md#package-production-before-qualification).

The complete Release managed transport, themes and four-harness graphs restored
and compiled in separate serialized processes: transport 4 warnings/0 errors,
themes 0/0 and harnesses 1/0. This closes the missing restore-assets obstacle for
the source Application.Run harness, not its execution or package-mode startup.
The focused bridge/graph fixture project also compiled (116 warnings/0 errors);
the new build-only contract fixture was authored and compiled, not executed.
The native runtime staging directory under ProGPU and the WPF Windows managed
payload directory are absent in this checkout; full fresh package production
still needs those platform build outputs. Do not substitute release-version or
dirty native binaries and report exact-head package evidence. No package-production
script, fixture, verifier, application, VM/GPU workload, benchmark or CI gate was
executed in this batch. Native/package admission and final qualification remain
open. Latest fetched ProGPU main is included; unrelated submodule edits remain
untouched.

Cocoa About-dialog native-session prerequisite: ProGPU now owns an incremental
AppKit modal session, retained native host identity, nested polling and deferred
End after native callbacks. WPF's two native-poll sites respect that session and
defer native window destruction while retained. This is not enabled automatically
by ShowDialog: existing GLFW-native popups still need real modal admission, and
source release/focus restoration must follow actual native End, including deferred
End. Local monitors miss native tracking loops and are not a full substitute.
Keep these concrete application dependencies open; do not use this API/compiled
fixture as macOS modal parity, package startup or Windows SDK admission evidence.
See the [Cocoa session contract](../external/ProGPU/docs/native-mil-cocoa-modal-session.md).
Compile-only: ProGPU.Tests 0 warnings/0 errors, final bridge fixtures 0/0 and source
RealPresentationFrameworkHarness 4/0. Corrected initial fixture internal access
with a signed backend friend contract; existing platform fixtures now use the
actual assembly instead of duplicated source. No fixtures, verifiers, applications,
VM/GPU workloads, benchmarks or CI checks ran. Automatic Cocoa modality, native
popup admission, source deferred-End restoration and Linux modality remain open;
these compilations do not change the package-mode or Windows admission gates.

Win32 modal gate/source restoration connection: the MVP About dialog now publishes
native input admission for source-owned windows and separately surfaced popups,
including windows created during the dialog. ProGPU retains an independent input
gate alongside latest application-enabled intent; controller refresh cannot erase
it. Weak registrations and transition snapshots preserve lifetime under callback
creation/removal; failed entry rolls back, failed exit reports unsynchronized
gates and still notifies survivors. Accepted Hide/Close releases gates before
native destruction, with owned nested windows unwound first; canceled close keeps
modality. Actual previous activation/source focus is captured before blocking and
restored only after gate/host admission, without fake IsActive or stale captures.
Failed gate release cannot skip accepted dialog host cleanup, and its diagnostics
prevent speculative activation while predecessor input state is uncertain.
Registered Win32 surfaces are created hidden until their input policy is applied.
Both renderers share this host connection. Cocoa/Linux native suppression, other
UI threads/custom native windows and startup-placement/interaction qualification
remain open. Windows package admission stays guarded. Authored fixtures include
real hidden Win32 enabled-state assertions, source restoration and Hide/Close
ordering; no fixtures, applications, VM/GPU, benchmark or CI runs have executed.
See the [input contract](../external/ProGPU/docs/native-mil-dialog-lifetime.md#win32-input-gates-and-source-focus-restoration).
Compile-only checkpoint (2026-09-09): final ProGPU.Tests 0 warnings/0 errors,
bridge fixtures 0/0, source PresentationFramework fixtures 2/0, and source
RealPresentationFrameworkHarness 0/0. The earlier unobserved bridge/harness
processes were confirmed absent before compiling again. Callback-created native
surfaces and active-then-hidden restoration have authored coverage, not executed
results. Latest fetched ProGPU main is included; unrelated submodule work remains
excluded. No package feed/restore bypass was introduced. The concrete remaining
About-dialog implementation blocker is native input suppression on Cocoa/Linux;
the source policy and checked Win32 integration do not close that requirement.

Native dialog owner connection: the MVP About dialog's Window.Owner now resolves
its live ProGPU host and applies shared native top-level ownership before Show.
The child initializes hidden when needed; missing/disposed owners or unsupported
native ownership reject explicitly instead of displaying an unowned dialog.
Live source owner changes admit the typed callback before updating OwnedWindows,
and raw WindowInteropHelper owner handles reject before WPF HWND/hidden-owner
access. ProGPU's shared controller checks creating-thread/platform/display and
owner-chain identity. Win32 checks local top-level native chains and actual writes;
Cocoa validates before detaching the previous owner; X11 flushes its existing
transient hint. Both renderers share this host path. Native interaction is not
qualified: OS input suppression, previous activation/focus restoration, startup
placement across monitors and Wayland ownership remain explicit. These are still
the same dialog/application integration batch, not general window customization.
Fixtures cover policy rejection/cycles, real source collection preservation,
live-host resolution and rejection before native Show. No tests, verifiers,
applications, VM/GPU workloads, benchmarks or CI qualification have run.
See the [owner contract](../external/ProGPU/docs/native-mil-dialog-lifetime.md#native-top-level-owner-connection).
Compile-only: ProGPU.Tests 0 warnings/0 errors, source PresentationFramework
fixtures 2/0, final bridge fixtures 21/0, and source RealPresentationFrameworkHarness
0/0. Fixed test-only WindowCollection
assertions after the initial source compile failure. Latest fetched ProGPU main
is included; unrelated native semantic-state/performance changes are preserved.
An additional RealApplicationRunHarness no-restore build stopped at NETSDK1004
because its project.assets.json is absent; no restore/feed bypass was used. That
SDK-run harness remains separate from the source PresentationFramework harness.

Shared source modal-input connection: the MVP About dialog now enters ProGPU's
thread-bound PortableModalInputScope before Show and restores nested scopes on
exit. Host receipt and queued dispatch reject inactive owners before input or
activation hooks; source input also checks focus and the actual captured-mouse
route. Blocked drag/drop returns no effect, native close callbacks are canceled
without preventing explicit source Close, and geometry/render/lifecycle callbacks
continue. Entry deactivates blocked input providers and releases capture/focus.
Popup admission uses its actual owner presentation source, including nested and
separately surfaced roots, with allocation-free cycle detection and explicit
missing/disposed-owner rejection. Application IsEnabled values remain untouched.
Source/host filtering is connected, not runtime-qualified native modality: OS
nonclient suppression, native owner configuration, other-UI-thread coordination
where required and previous activation/focus restoration remain open. Authored
policy/host/source fixtures cover nesting, ownership, queue entry, failure cleanup,
enabled intent and capture release; actual popup/OS interaction is still a final
gate. Both renderers share the policy. Continue these concrete dialog integration
blockers, not general windowing API expansion. See the [scope contract](../external/ProGPU/docs/native-mil-dialog-lifetime.md#shared-source-input-admission).
Compile-only: ProGPU.Tests 0 warnings/0 errors, source PresentationFramework
fixtures 6/0 on the final resumed build (2/0 previously), final bridge fixtures 20/0 (116/0 initially) and source application
harness 0/0. Fixed one test-only ambiguous xUnit overload after the initial ProGPU
build failure. No fixture, verifier, application/native-input/VM/GPU workload,
benchmark or CI qualification ran. The package-mode MVP still awaits fresh package
production; these source builds do not repair or bypass the missing local feed.
Latest fetched ProGPU main is included and unrelated submodule edits are preserved.

Native modal-input prerequisite: source inspection of the shared ProGPU enable
path found that Win32 EnableWindow's previous-state result was treated as success,
and an unrelated shadow refresh could mask rejected/unsupported enabled-state
handling. The shared controller now reports enabled-state admission alone; Win32
checks local host-thread/process ownership and the actual post-callback state.
Source-generated integer-BOOL bindings replace the old enable import. Authored
policy fixtures cover state changes/idempotence, rejection, destruction and
callback failures. This corrects a prerequisite for the MVP dialog's remaining
other-window input blocker, not a completed WPF modal-input coordinator. Cocoa
currently updates native buttons only, and X11/Wayland input suppression remains
incomplete. Keep native ownership, nested input restriction and activation
restoration in the same core dialog batch; do not report them complete or expand
unrelated window customization. See the [admission contract](../external/ProGPU/docs/native-mil-dialog-lifetime.md#native-enabled-state-admission-prerequisite).
Compile-only: ProGPU.Tests 0 warnings/0 errors and source-built WPF application
harness 5 warnings/0 errors. No fixture, verifier, native input/application/VM/GPU
workload, benchmark or CI qualification ran. Latest fetched ProGPU main is included;
unrelated native semantic-state and performance-artifact changes are preserved.
The next core integration must share one effective modal restriction across host
ingress, queued dispatch, popup ownership and source captured-mouse routing, while
preserving application-owned enabled values. Existing WinUI modal owner code
assigns IsEnabled directly and must not be copied as the WPF product contract.

Portable dialog lifetime connection: the MVP About dialog now admits a separate
typed `RunDialog` capability before Show. Source ShowHelper no longer enters a WPF
HWND dispatcher modal frame for an active portable window on Windows. The host's
existing event/render loop borrows the source continuation and ends that invocation
on Hide without closing/disposal or changing the ordinary application lifetime.
Canceled DialogResult closes reset the result, allowing the same value to close
again later. Premature host return and missing capability fail explicitly; source
modal notifications and host pump cleanup remain finally-balanced. Both renderers
share the same host path. Authored source/interop fixtures and the extended existing
MVP dialog gate cover Hide/reuse, cancellation and failure cleanup; they are not
executed runtime evidence. Native owner-window configuration, other-window input
disabling/nested modality and activation restoration are still concrete dialog
integration blockers, not completed by this loop fix. Windows SDK admission stays
guarded. See the [contract and qualification scope](../external/ProGPU/docs/native-mil-dialog-lifetime.md).
Compile-only: ProGPU.Tests 0 warnings/0 errors, final PresentationFramework source
fixtures 2/0 (6/0 initially), bridge fixtures 116/0 and source application harness
0/0. The package-mode MVP build did **not** compile: no-restore failed with
NETSDK1004 for missing assets; ordinary restore then failed with NU1301 because
`artifacts/packages/Release/NonShipping` does not exist. The live MVP gate extension
is authored, not package-compiled or executed. Fresh package production/consumption
remains mandatory; no feed bypass or old-package success substitutes for it.
ProGPU `62867381` contains the typed contract and latest fetched main; unrelated
submodule changes remain untouched. No tests/verifiers, applications, VM/GPU
workloads, benchmarks or CI qualification ran.

Package imaging ownership connection: the existing external SDK application's
`ValidateManagedImagingObjects` constructs custom/predefined palettes, derives a
palette from owned BGRA/indexed pixels, wraps the bitmap in `BitmapFrame` and saves
BMP. Source inspection found those consumers still selected WIC by OS on Windows
after portable BitmapSource construction. Palette selection now follows frozen
media ownership, and source/frame adapters adopt actual owned pixels before any
native handle read. Missing portable frame/analysis pixels fail explicitly.
Portable BMP saving uses the existing serializer on every OS; other containers
and WIC codec-info/palette-handle requests reject before native codec activation.
Windows-MIL storage/codec routes remain separate. This is source-WPF ownership
plumbing, not a new ProGPU rendering algorithm or a codec rewrite. Existing
first-distinct-color palette analysis, predefined palette generation and BMP
serialization remain unqualified for WIC quantization, full codec fidelity and
SIMD/performance parity; no new scalar kernel or speed claim is introduced.
Authored source fixtures cover indexed palette/frame ownership, independent
clones, DPI, freeze, a BMP round trip and unsupported codec rejection. Separate
portable and Windows-MIL test processes are required. Source graph guards also
cover admission before WIC. No tests or application validation have run for this
checkpoint; Windows package admission remains guarded. Continue the same package
startup/common-resource queue, not a general image-codec expansion.
Compile-only: PresentationCore source fixtures 4 warnings/0 errors, bridge fixtures
116 warnings/0 errors and the source-built application harness 0 warnings/0 errors.
An unnecessary test import was removed after the initial analyzer failure.
Latest fetched ProGPU main is already included in feature head `133f8134`;
unrelated ProGPU changes remain untouched. No source verifier, test, application,
VM/GPU workload, benchmark or CI qualification was executed.

Cocoa system-menu connection: the MVP Window / Show system menu action now
resolves its real NSWindow and uses the shared ProGPU AppKit native-action menu.
Minimize/Zoom/Close retain native button policy and delegate close cancellation;
this is a macOS adaptation, not arbitrary Win32 menu customization or Windows
Move/Size/fullscreen parity. Main-thread admission and application-window identity
precede owner access. Window/view/delegate leases survive synchronous tracking,
and the reverse callback only records a known item for its scoped target. Actual
action dispatch follows owner/identity/capability revalidation after tracking.
Native menus, targets, autoreleased objects and owner leases are released; no
WPF rendering workaround, generic recovery or source-local menu was introduced.
Desktop placement uses the current primary screen and real AppKit window/view
conversion without framebuffer scaling, with arm64/x64 native struct-return
bindings. Collectible callback providers and rejected platform capabilities fail
explicitly. Both renderer modes share this provider. Wayland and Windows SDK
admission remain open, and menu display/interaction is not runtime-qualified.
See the [native-action contract and qualification record](../external/ProGPU/docs/native-mil-system-menu.md#cocoa-native-action-menu).
Policy/selection/lease/coordinate/ABI fixtures are authored but not executed.
Compile-only: final ProGPU.Tests 0 warnings/0 errors, bridge fixtures 21/0
(116/0 initially), and the source-built application harness 0/0 (4/0 initially).
No tests, source verifiers, applications, VM/GPU workloads, benchmarks or CI
qualification ran. This closes the identified provider connection, not the
package application acceptance batch; final actual-app qualification remains.
ProGPU `133f8134` is pushed and includes latest fetched main. Continue the active
native package-startup queue; this checkpoint does not make general platform menu
expansion a prerequisite for the Windows application path.

Linux/X11 system-menu connection: the same MVP Window / Show system menu action
now resolves the actual native Display/XID in the Silk.NET host adapter and calls
ProGPU's shared provider. The provider requires a WM-managed client and the menu
extension advertised on its actual root, preserves signed desktop coordinates,
and resolves the owning client's real XInput pointer on a temporary connection.
XI2 negotiation never changes the host's input protocol. Temporary connections
and Xlib property buffers are released; malformed/oversized capability metadata
and missing XInput fail explicitly. The native-long event/property layout supports
ILP32/LP64 instead of assuming C long is always 64 bits. Request acceptance is
asynchronous and is not menu-display evidence. Cocoa/Wayland providers, unsupported
X11 environments and Windows SDK admission remain open. Both renderer modes use
this platform path; no WPF-local menu renderer or C++ rendering fork was introduced.
See the [protocol and qualification record](../external/ProGPU/docs/native-mil-system-menu.md#x11-window-manager-request).
Policy/payload/cleanup/ABI fixtures are authored but not executed. Compile-only:
ProGPU.Tests 0 warnings/0 errors, bridge fixtures 116/0 and the source-built
application harness 4/0. No fixture, verifier,
application, VM/GPU, benchmark or CI qualification ran. ProGPU `141621a3` is pushed
and includes latest fetched main; this does not close the package application
acceptance batch.

Windows system-menu connection: the MVP Window / Show system menu action now
uses a typed optional activation callback before source HWND/DPI handling.
ProGPU owns the new native provider: local same-thread top-level owner admission,
existing menu lifetime, desktop coordinates, system alignment, modal selection
and one posted command after owner/menu revalidation. Both renderers share it.
Missing/rejected capabilities fail explicitly; unrelated host exceptions propagate.
Native WPF's original menu path remains separate. Authored source and shared
policy fixtures cover ownership, optional registration, coordinates, cancellation,
errors and reentrancy; Windows admission fixtures use real child/foreign-thread
HWNDs. These are not executed menu-display evidence. Cocoa/X11/Wayland system-menu
providers are still missing and the native Windows SDK guard remains in place.
See the [design and qualification record](../external/ProGPU/docs/native-mil-system-menu.md).
Compile-only: ProGPU.Tests 0 warnings/0 errors, bridge fixtures 116/0 and source
PresentationFramework fixtures 6/0; the source-built application harness builds
with 5 warnings/0 errors. ProGPU `dab85b76` is pushed and includes latest fetched
main. No fixture, verifier, application, VM/GPU,
benchmark or CI workload ran. No runtime validation is implied by this connection.

Portable Window menu state commands: the MVP's maximize/minimize/restore/close
path now selects an active portable Window before Win32 handle access on every
OS. It uses the existing source WindowState/Close implementation and typed host
callbacks, preserving source state, cancelable Closing and one close/dispose.
Native WPF HWND windows keep their posted asynchronous system commands. Authored
fixtures exercise a real source Window with a deliberately non-HWND portable
handle, all three state transitions and canceled/accepted close; the separate
Windows-MIL fixture observes all four posted commands through a real HwndSource
hook. They require independent portable/native test-process lanes and have not
been executed. This host/source routing fix requires no new ProGPU algorithm.
Portable ShowSystemMenu remains a distinct missing host capability: the current
source implementation is still OS-selected and must be connected before claiming
that MVP action or Windows SDK admission complete. Do not count the existing
non-Windows no-op as successful menu display. Compile-only: the final source
PresentationFramework fixture graph builds with 2 warnings/0 errors (6 warnings
on its initial source dependency rebuild). Warnings are the existing test-package
compatibility and nullable diagnostics; no fixture, verifier, application, VM/GPU,
benchmark or CI workload ran. Latest fetched ProGPU main remains included in the
unchanged ProGPU feature head. Windows native SDK activation remains guarded.

Windows popup service connection (MVP/Toolkit ComboBox and menu open, nested menu
movement, two-window ownership and close): ProGPU root hosts now register with
the existing typed popup router on Windows as well as macOS/Linux. Native popup
child hosts retain their explicit opt-out. A supplied source identity cannot be
overridden by a colliding opaque handle while the router probes other windows;
legacy handle-only lookup remains available only when no source was supplied.
Factory-created popup sources are now disposed by their bridge on teardown and
failed setup, while main-window bindings remain borrowed. Native setup exceptions
propagate after cleanup; only an explicit null factory result selects the existing
owner-surface path. These are host adapter/ownership fixes, not a new compositor,
native algorithm or API family, and need no paired ProGPU implementation change.
Authored fixtures cover both renderer registrations, nested movement, foreign
owner rejection, disposal/unregistration, child-host opt-out and native creation/
input-handler failure cleanup. Compile-only: the final bridge fixture graph builds
with 21 warnings/0 errors (116 warnings on the first dependency rebuild), and the
source-built PresentationFramework application harness builds with 4 warnings/
0 errors. These include existing analyzer/source warnings. No fixture, verifier,
application, VM/GPU, benchmark or CI workload ran. Latest fetched ProGPU main is
already included by the existing feature branch; its unrelated worktree changes
remain untouched.
This closes the identified popup registration and creation-lifetime blockers, not
Windows SDK admission. The next application integration work remains startup and
common source-media routing before removing that guard; no optional API expansion
is added to the queue.

Windows portable input ownership: the MVP/Toolkit editor-focus, modifier-key,
typing and mouse-selection path exposed OS-selected Win32 devices even after
portable media selection. InputManager now freezes that selection before device
construction and uses portable key/button state on Windows as well. WPF TSF does
not re-promote host-delivered keys, enable its message pump for portable focus or
attach source editor stores lacking WPF-owned HWND composition geometry. Existing
committed-character delivery stays on the typed input report pipeline; native
Windows WPF keeps its devices and TSF path. Nondefault automatic IME preferences
are explicitly unsupported pending host composition policy; this is not full IME,
candidate-window or reconversion support. The Windows native SDK guard remains.
Authored device/input/editor fixtures await final execution. The final source
fixture graphs compile with 9 warnings/0 errors (PresentationCore) and 7 warnings/
0 errors (PresentationFramework); the source host compiles with 1 warning/0 errors.
These include existing source/package warnings, not real-device or TSF qualification.
A shared test-only module initializer accepts
`LIBREWPF_TEST_MEDIA_BACKEND=Portable` or `WindowsMil` before test source objects
are constructed. Final Windows qualification must run both separate process lanes;
skipped portable fixtures in the native-Windows default lane are not evidence.
This does not bypass the package admission guard or reset product ownership. See the
[input ownership record](../external/ProGPU/docs/native-mil-startup-selection.md#input-device-ownership-connection).

Source paginated-viewer connection: the MVP's real FlowDocument paginator now
selects portable media before PTS, shares source page/column policy and passes
actual TextLine advances and source break constraints to ProGPU C++ pagination.
Real DocumentPages feed the existing DocumentPageView and shared ITextView with
original document positions, page affinities, column-local caret/selection/hits,
source content ancestors and list/brush ownership. Block insets are reserved;
native fragment positions must preserve the drawing/interaction translation.
Page disposal releases its drawing only; edits, page-size changes and formatter
replacement invalidate and dispose the owned layout generation. Background work
coalesces a whole generation on the source dispatcher, not an incremental worker.
Nonzero widow/orphan policy, cross-fragment block decorations and empty decorated
blocks remain explicitly unsupported, alongside existing indentation/hyphenation,
embedded-object/table and RTL limitations. This closes the unconditional paginator
import route, not native document fidelity or Windows package admission.
Source fixtures are authored, including actual DocumentPageView consumption.
The source PresentationFramework fixture graph compiles with 3 warnings/0 errors
(two package compatibility warnings and an existing nullable warning); no fixture
was executed. The source-built host harness compiles with 1 warning/0 errors.
Application/VM/GPU, benchmarks and CI remain deferred. See the
[source pagination record](../external/ProGPU/docs/native-mil-document-flow.md#source-paginated-viewer-connection).
Return to the acceptance application's startup/resource path and Windows admission
dependencies next; optional document policy breadth is not a new prerequisite.

Native pagination prerequisite (historical): the MVP page viewer was still a known PTS consumer.
ProGPU now supplies generated native records and a typed zero-copy service for
sequential page/column fitting over actual line advances and source-admitted
breaks. Forced page/column boundaries, leading-space replacement and failure
atomicity are implemented; indivisible non-fitting ranges fail explicitly.
This does not switch the source paginator. Connect source column/page policy,
keep/widow/orphan constraints, page visuals/fragmented decorations and page-local
document interaction next; do not open another pagination utility family first.
Native fixtures compile; ProGPU managed fixtures build with 0 warnings/errors
and LibreWPF adapter fixtures with 116 warnings/0 errors. None is executed.
ProGPU's existing managed/native
symbol-cmap support and the installed macOS Wingdings face mean symbol markers
are an availability/integration qualification item, not proof of a missing cmap
implementation; Linux font availability remains unqualified. See the
[pagination record](../external/ProGPU/docs/native-mil-document-flow.md#sequential-native-pagination-prerequisite).

Source scroll-view checkpoint: the MVP's actual `FlowDocumentView` now consumes
the live portable formatter before PTS access on every OS. Its real source
DrawingVisual/IContentHost draws paragraphs, markers, backgrounds and border
rings; ITextView shares original document pointers and retained TextLines for
selection, caret/content hit tests, navigation and bring-into-view. Scroll offsets
move retained drawing without reformatting; document-local content rectangles and
viewport interaction geometry remain distinct. Edits, suspension and document
replacement invalidate interaction and release owned generations. Portable caret
boundary checks now accept equivalent trailing affinities through native logical
boundaries without accepting shaped-cluster interiors. Lazy text defaults are
shared by direct managed and native hosts. No new WPF-local composer or native
algorithm copy was introduced. Required symbol fonts, paginated consumers and
Windows SDK admission remain open; viewport page navigation is not pagination.
Source fixtures compile (PresentationFramework: 3 warnings/0 errors;
PresentationCore: 5 warnings/0 errors; source-built host: 1 warning/0 errors).
Tests, apps, VM/GPU, benchmarks and CI
remain deferred. See the [connection record](../external/ProGPU/docs/native-mil-document-flow.md#source-scroll-view-connection).

Source document formatter checkpoint (historical; viewer connection above supersedes
its next step): `PortableFlowDocumentLayout` now sends
source Paragraph/Section/List/ListItem properties and real TextLines to ProGPU's
native block service. Original document objects, UTF-16/source-edge positions,
scoped properties, source line advances and separate numbered markers are retained.
`PortableFlowDocumentFormatter` owns source invalidation, generation reuse,
reentrancy detection and disposal without a PTS context. Its typed source getter
requires frozen portable media. Source fixtures are authored for those boundaries;
they do not qualify native typography or document rendering. **FlowDocumentView
is not switched yet**: visual/IContentHost, ITextView and scroll integration are
the next required connection. Symbol-marker fonts, pagination, zero-width wrapping,
indentation/hyphenation and Windows SDK admission remain explicit dependencies.
Do not spend another batch on optional formatter APIs before the actual viewer
consumer. See the [source checkpoint](../external/ProGPU/docs/native-mil-document-flow.md#source-formatter-checkpoint--not-viewer-activation).
Compile-only: source PresentationFramework builds with 0 warnings/errors;
its source fixture graph builds with 3 warnings/0 errors incrementally and
7 warnings/0 errors when the source graph rebuilds. No fixture, verifier,
application, VM/GPU, benchmark or CI workload was executed.

Native document placement prerequisite: ProGPU now resolves nested source block
widths and arranges actual formatted lines with positive margin collapse, insets
and overflow. The fixed C ABI has generated double-precision records, a zero-copy
typed LibreWPF adapter and lazy registration before native SDK media construction.
Fixtures cover spacing, invalid topology/ranges/aliasing, preserved failure outputs,
scalar metric/prefix oracles and neutral/native field layouts. The strict C++
fixture target and managed adapter fixtures compile; no tests or runtime/CI
qualification ran. Source `FlowDocumentView` still needs its actual consumer:
original document extraction, paragraph/list rendering, source text-view/content
ownership, scrolling and invalidation. Pagination remains open. This native layer
is not viewer completion and does not remove the Windows package admission guard.
Continue that consumer, not additional block-layout API breadth.
See the [implementation record](../external/ProGPU/docs/native-mil-document-flow.md).
Compilation: ProGPU fixtures 0 warnings/errors; LibreWPF adapter fixture graph
116 warnings/0 errors on its first build and 1 warning/0 errors incrementally.
ProGPU commit `3dc0c223` contains the latest fetched `main`; this checkpoint does
not assert that the required CI or installed native package exports are qualified.

Standard source run underlines: the MVP FlowDocument viewer's Hyperlink exposed
a required decoration rejection in PortableTextLine. Standard underlines now
consume native paragraph range geometry, actual font metrics and source brushes,
including wrapped lines, interior tabs, trailing-whitespace exclusion and ink
bounds. Source replay keeps paired baseline/top-edge guidelines and native MIL
rectangle commands. The native host now contains a real Hyperlink and asserts
underline coverage/guidelines in its typed render stream. This is a prerequisite,
not complete document-viewer layout. Custom/paragraph/animated decorations,
other locations, mixed-font underline metric averaging and the RichTextBox
structural decoration scope adapter remain explicit gaps.
See the [implementation record](../external/ProGPU/docs/native-mil-standard-underlines.md).
Compile-only: source PresentationCore fixtures build with 0 errors (4 warnings
incrementally, 8 when the source graph rebuilt). The ink fixture handles an empty
missing-glyph outline without treating it as an offsettable rectangle;
the native host harness builds with 1 warning/0 errors. No tests, verifiers, apps,
VM/GPU, benchmarks or CI qualification ran. Return to FlowDocument block/view
integration, not optional decoration expansion, after this required connection.

Mixed-size editor line metrics: `TextBoxView` now retains each formatted line's
source advance and accumulated top instead of reusing the last formatted line's
height for the entire document. Drawing, caret/selection geometry, point lookup,
scroll visibility and incremental edits consume the same retained prefix map.
The viewport bottom is exclusive, including an exact line boundary. Inline
selection drawing now uses source symbol offsets, preserving hidden document
edges, rather than flattened character offsets. The prefix rebuild is ordered
and allocation-free over existing records; it is not a new paragraph composer,
does not introduce scalar shaping and makes no performance claim. Its dependent
prefix accumulation cannot be treated as independent SIMD lanes.

Fixtures cover different-sized paragraphs, font-size mutation, visual/caret/extent
agreement, point and selection bounds, exact viewport edges and styled source
selection offsets. Their deterministic provider is a source-consumer fixture,
not a native shaper or evidence of GPU rendering. Full document/page/paragraph
layout, native decoration/inline-object contracts and application qualification
remain open; do not substitute this editor cache for the MVP's document viewers.
Compile-only: the source PresentationFramework fixture project builds with
2 warnings and 0 errors. No fixture, verifier, application, VM/GPU, benchmark or
CI workload was executed; these are authored contracts awaiting final qualification.

Portable rich-editor admission: the MVP `EditorRichTextBox` now selects its
source-owned `TextBoxView` by frozen media ownership, including Windows portable
mode. Windows MIL still selects PTS. The editor sends actual styled runs through
the registered paragraph provider, preserves source element positions, and rejects
embedded objects, unsupported block structures, directional scopes and decorations
instead of hiding their semantics. Registered providers no longer receive silently
left-aligned justified text. Spelling-highlight properties retain each rich run's
face/brush, and bounded source text copies do not split a UTF-16 surrogate pair.
Source regression fixtures compile (2 warnings, 0 errors); they were not executed.
No application, VM/GPU, verification, benchmark or CI workload ran.

This is an editor routing connection, not complete rich-document layout. The source
trace also identified `TextBoxView`'s uniform-height line cache, paragraph/page
layout limitations and the independent `FlowDocumentView` empty portable measure
path. The MVP's document viewers include headings, a hyperlink and lists; their
PTS replacement remains required. Do not remove the Windows SDK admission guard
or report this editor connection as document-viewer or package-mode qualification.

Native intrinsic measurement and WrapWithOverflow: ProGPU's shared C++ logical
paragraph pipeline now supplies min/max widths and explicit whole-word wrapping.
The actual source-backed startup blocker was FormattedText's default
WrapWithOverflow policy, which the provider rejected. Source MinWidth now requests
real native measurements instead of substituting a formatted-line width. Hard-line
scopes/indentation, shaped clusters, mixed font scales and tab grids are preserved.
The existing pre-host FormattedText harness and package App require valid MinWidth.
This connects two core text dependencies; it does not close optimal paragraphs,
trimming/display mode, Windows admission, full application closure or final gates.
See [implementation and qualification boundaries](../external/ProGPU/docs/native-mil-intrinsic-text.md).
Compile-only: native text/C ABI targets link; ProGPU fixtures 0/0, source
PresentationCore fixtures 8/0, bridge fixtures 116/0 and native host harness 0/0
(warnings/errors). The package constructor's native checks await rebuilt packages.
Tests, verifiers, applications, VM/GPU workloads, benchmarks and CI polling were
not run. The previously observed CI failures are not fixed by this text batch.

Provider-first source text dispatch: TextFormatter now selects by frozen media
ownership. Portable simple/complex lines use the registered native paragraph before
the old simple path; Windows-MIL mode retains native services. Classification table
and control-string initialization follow that same selection. Immutable wrapped
continuations survive provider unregister and preceding-line/break disposal.
Provider failure/null output is not an empty line. Context acquisition, optimal
paragraph caches, forced-break reconstruction and intrinsic min/max measurement
reject explicitly where contracts are missing. Full-line width is not a valid
intrinsic minimum. These are implementation connections and exposed gaps, not
Windows admission or application parity. Continue core constructor/first-layout
dependencies, specifically intrinsic measurement if requested by the acceptance
app; do not expand general DirectWrite or typography API coverage.
See the [source dispatch record](../external/ProGPU/docs/native-mil-startup-services.md#source-formatter-dispatch-connection).
Compile-only checkpoint: PresentationCore fixtures 8 warnings/0 errors; bridge
fixtures 116 warnings/0 errors. The new fixture-attribute source-information warning
was fixed before the final build. No tests, verifiers, applications, VM/GPU workloads,
benchmarks or CI polling ran. Latest fetched ProGPU main is included. Windows SDK
admission and final qualification remain open.

Native package pre-host services: the SDK now calls `ProGpuWpfNativeMediaServices.Initialize`
before source-module/LibreWinForms initialization. Native hosts share that path,
which selects portable media and installs lazy text/geometry defaults without
creating a device. ProGPU keeps defaults separate from explicit overrides, so
temporary override disposal cannot erase readiness. The existing native host
constructs styled text before its host; the SDK smoke App has native constructor-time
text/geometry assertions. Windows admission is still guarded, and Windows
TextFormatter LineServices selection remains OS-based. Continue that source/package
routing dependency; this does not prove full startup or application parity.
See the [startup implementation record](../external/ProGPU/docs/native-mil-startup-services.md).
Compile-only: ProGPU fixtures 0 warnings/0 errors, bridge fixtures 116 warnings/0
errors, native host harness 4 warnings/0 errors. Native SDK App constructor checks
await package rebuild; no tests, verifiers, apps, VM/GPU, benchmarks or CI ran.

Document inline connection: source `TextHidden` positions now stay in document
lengths, glyph indices, caret/selection and continuation metadata without entering
shaping text. ProGPU owns the reusable source-position map; WPF uses its existing
typed property-modifier scopes and preserves open scopes across explicit line
breaks. The native host now contains a real nested Run/Span TextBlock with positive
width and native font-binding assertions. Regressions are authored, not executed.
Directional embedding, decorations and embedded objects still block broader
document content; startup/Windows SDK admission and all application qualification
remain open. See the [source integration record](../external/ProGPU/docs/native-mil-text-source-integration.md).
After this ordinary inline connection, return to native package startup and its
Windows admission dependencies in batch 1. Do not expand directional/decorated
text merely to complete a text API inventory; tie the next change to a required
startup or acceptance-application action.
Compile-only evidence: ProGPU fixtures 0 warnings/0 errors, source PresentationCore
fixtures 8 warnings/0 errors, native host harness 1 warning/0 errors. No fixture,
verifier, runtime, VM/GPU, performance or CI validation was executed in this batch.

Incremental editor tabs: default tab-separated input now has a native measurement
path rather than hitting the source tab-expansion rejection. The shared C++
composer resolves the source interval/indent grid before wrapping, retains advances
through bidi reordering and publishes non-ink tab clusters. WPF retains tab-only
selection/caret/background extents and does not send the tab sentinel to a glyph
atlas. The existing host case includes a tab in styled composite-family bidi text;
native/source regressions are authored. Custom stop collections/leaders and tab
trimming remain explicit gaps. Continue required document/editor connections,
not general API expansion; the full core app and Windows admission remain open.
See the [tab implementation/qualification record](../external/ProGPU/docs/native-mil-text-source-integration.md).
Compile-only checkpoint: native text targets, ProGPU fixtures, source PresentationCore
fixtures, bridge fixtures and the source-host harness build with zero errors.
Source and bridge graphs report one and 116 warnings respectively; no tests or
runtime/CI gates were executed. The next identified document action blocker is
`ComplexLine` producing `TextHidden`/`TextSpanModifier` for ordinary inline element
edges while `PortableTextLine.Create` rejects them. Close that required source
connection within the existing application queue, not optional custom-tab breadth.

Source composite/fallback connection: the native host's styled bidi text now uses
WPF's composite UI family. The adapter consumes typed font ranges from the existing
source GlyphingCache/TypefaceMap without calling the DirectWrite itemizer; mapped
physical face identities and composite em scales reach the existing ProGPU native
paragraph and actual source GlyphRuns. Source Typeface continues to own line
metrics. Bundled-font fallback, cache reuse, culture/scale and UTF-16 range fixtures
are authored, not executed. This closes the identified first-physical-face-only
source branch; null-shape, device-font and synthetic style contracts remain
explicitly unsupported. Tabs/document objects and the other required editor
connections remain next, followed by startup admission and application closure.
The native algorithms/ABI are reused unchanged; do not port WPF font-family policy
into ProGPU or reopen unrelated API families. See the
[source integration record](../external/ProGPU/docs/native-mil-text-source-integration.md).
Compile-only checkpoint: source PresentationCore fixtures 4 warnings/0 errors;
source-host harness 1/0; bridge fixtures 115/0. No tests, source verifiers,
app/VM/GPU workloads, benchmarks or CI qualification ran. Native algorithms/ABI
are unchanged; ProGPU main is current and unrelated worktree changes preserved.

Styled editor connection: the MVP/source-host action of changing font size,
physical face or brushes no longer hits the source mixed-typography rejection.
ProGPU's existing C++ paragraph now accepts explicit face/feature/scale domains;
source WPF carries their actual glyph face annotations, source clusters, brushes
and participating per-line ascent/descent. Wrapping and bidi ordering use native
floating-point scaled metrics; NEON/SSE2 cover independent metric lanes. Single-
face styles reuse leased plans; multi-face paragraph contexts are isolated and
disposed after owned output. This is implemented connection work, not executed
editor parity. Existing native host text now changes size/foreground inline;
native and source regressions are authored for the final gate. Composite/fallback
font resolution, tabs and document objects remain the next concrete text blockers.
Do not reopen general Direct2D/Win2D or advanced-presentation breadth for this slice.
See the [styled integration and research record](../external/ProGPU/docs/native-mil-text-source-integration.md).
Windows SDK admission remains guarded. Testing, verification, app/VM/GPU work,
benchmarks and CI qualification remain deferred to core feature freeze.
Compile-only evidence for this connection: ProGPU fixtures 0 warnings/0 errors;
final bridge fixtures 21/0; final source PresentationCore fixtures 8/0; final
source-host harness 4/0. Native text and shaping-showcase targets compile with
strict AppleClang C++20. These are compilation results, not passes of fixture
bodies or application gates. ProGPU implementation `50c98e1c` is pushed to PR #139
and tracked here; latest fetched main is included and unrelated changes preserved.

Source text adapter checkpoint: `PortableTextLine` now connects the registered
native paragraph provider to source WPF glyph replay, clusters, hit/selection,
logical caret navigation and cloned wrapped-line continuation. ProGPU owns the
snapshot over its existing C++ pipeline, UTF-16 intrinsic expansion, bidi/cluster
metadata and native interaction buffers; source WPF owns actual GlyphRun and font
identity/state. The existing native host harness now draws mixed-direction text
with a combining mark and requires positive width/native font bindings. This is
an authored application gate addition, not runtime evidence. The initial adapter
accepts one typography domain; mixed styles, composite/fallback fonts, tabs,
document objects, trimming and other enumerated gaps remain explicit in the
native text integration record. The old simple path and Windows LineServices are
unchanged; an active provider failure cannot become an empty line. Next work must
close these remaining MVP/Toolkit editor connections, not expand unrelated APIs.
Compile-only evidence: ProGPU fixtures completed with zero warnings/errors;
bridge fixtures with 116 warnings and zero errors; the native host harness with
five warnings and zero errors; final source fixtures with eight warnings and zero errors.
no authored fixture, source verifier, app/VM/GPU run, benchmark or CI gate has been
executed. The native C++ algorithms/ABI are unchanged in this adapter batch.
Latest fetched ProGPU main is contained and unrelated worktree changes are preserved.

Editable-text interaction connection: ProGPU's existing native cluster/caret,
hit-test and selection algorithms now have generated C/.NET contracts and borrowed
span entry points. C++ and C ABI callers share the original implementation without
per-glyph repacking. Source adapters must supply actual cluster ends and resolved
bidi levels per positioned glyph, and consume successful output counts. Native
interaction and text fixture targets compile; the managed backend and fixture
project compile with zero warnings/errors. Fixtures are authored, not executed.
The WPF TextLine adapter and styled-run metadata producer remain open: this API
does not replace the empty portable paragraph or close Windows SDK admission.
Finish that existing editor connection next, not another text subsystem roadmap.
No tests, verifiers, application/VM/GPU runs, benchmarks or CI qualification ran.

Retained native text prerequisite: the source empty-paragraph fallback identified
in the SDK startup trace requires a real WPF TextLine adapter. ProGPU already has
a C++ bidi/OpenType/fallback/positioned-paragraph pipeline, so no duplicate composer
was added. Its managed retained context now holds an exclusive pointer-use scope
across every native call. This closes concurrent plan mutation and dispose-during-
call hazards before a reusable source-font cache can retain those contexts.
Nested/reentrant disposal, exactly-once release and concurrent owner fixtures are
authored. Source styled-run/font/cluster/caret integration is still required; the
empty WPF fallback has not been replaced by this ownership prerequisite.
See the [native text integration record](../external/ProGPU/docs/native-mil-text-source-integration.md).
Compile-only checkpoint: native managed backend and the ProGPU fixture project
completed with zero warnings/errors, including the final source-guard rebuild.
No fixture, native workload, application/VM/GPU, benchmark or CI execution ran.
The native header change is synchronization documentation only; C++ shaping and
paragraph algorithms are unchanged. Latest fetched ProGPU main is contained.

SDK attached window-chrome connection: `ValidateWindowChrome` in the existing
package SDK gate attaches/replaces/removes chrome. `WindowChromeWorker` formerly
selected its WPF HWND/HwndSource hook path by OS, despite a ProGPU-owned source
on Windows. It now selects the existing portable path by active ownership,
registered activation, or frozen portable media policy, before source creation
as well as afterwards. Typed window state selects borderless custom chrome and
restores the original style on removal; native-WPF mode retains its hook path.
Source fixtures cover attachment before/after hidden source creation, live chrome
updates, border callbacks and removal. The existing SDK app assertion now checks
typed host style after attach/remove. No new rendering/native algorithm applies.
Windows startup remains guarded pending remaining application integration.
Compile-only checkpoint: source-framework fixtures completed with two warnings
and zero errors on the final rebuild; bridge fixtures with 20 warnings and zero
errors; SDK harness with zero warnings/errors after restoring its missing assets
file. Building the SDK harness compiles the generator, not its generated package
application; that application assertion remains authored-only. No tests, source
verifiers, graphical/VM/GPU runs, benchmarks or CI qualification ran. ProGPU
contains the latest fetched main; unrelated worktree changes were preserved.

The startup trace also found `SimpleTextLine.CreatePortableFallback` can construct
an empty paragraph for unsupported text. Do not enable a Windows portable-text
route by replacing OS checks indiscriminately: native LineServices is a separate
dependency from MIL rendering, and the empty portable result is not text parity.
Track required complex-text/document cases against the existing MVP editor gate;
this batch neither changes text services nor claims those cases complete.

Decoder-backed SDK image connection: the existing package SDK gate loads
`Assets/ExternalImage.png` through XAML Image/ImageBrush and BitmapImage pack URI
paths. Source decoder constructors and format discovery now use the frozen media
choice on Windows too. Unsupported portable formats fail before WIC activation;
BitmapImage cache and decoded-source adoption preserve owned portable pixels
without OS gating. Windows-MIL mode retains native decoding. Existing decoder
algorithms, pack/HTTP stream acquisition, transforms and renderer pixel consumers
are unchanged. The source-built native host now draws the SDK PNG after OnLoad
stream disposal; authored source fixtures cover generic/specific decoder selection,
unknown-format rejection and cached URI pixel ownership. These fixtures are not
runtime evidence. Windows admission is still guarded. Async/nonseekable downloading,
color profiles and full codec parity remain unqualified; no SIMD speed claim is
made for existing codec algorithms. See the ProGPU memory-bitmap record.
Compile-only checkpoint: the native host harness completed with five warnings
and zero errors, source PresentationCore fixtures with four warnings and zero
errors, and bridge fixtures with 115 warnings and zero errors. No fixtures, graphical
applications, VM comparisons, source verifiers, benchmarks or CI checks ran.

Memory-bitmap connection (SciChart MVP chart-to-Image display and SDK memory
images): source WriteableBitmap construction/copy and CachedBitmap memory
construction now select portable storage by frozen media policy, not OS.
Cached sources consume existing managed pixels directly and decode-failure
replacement follows the same policy. WriteableBitmap storage uses bitmap-owned
pinned arrays, avoiding orphaned per-lock GCHandle pins while preserving nested
lock/dirty publication and freeze behavior. The existing native host gate now
draws a source-built bitmap and requires its exact RGBA sideband plus source DPI.
Source fixtures cover copies/clones, locks, collection/relock address stability
and freezing. Both renderers consume existing typed pixel contracts; no ProGPU
renderer algorithm or ABI changes apply. See the
[ownership and qualification record](../external/ProGPU/docs/native-mil-memory-bitmaps.md).
This closes the named constructor/MIL-storage route, not all imaging APIs or
Windows SDK admission. Decoder-backed images and shared-worker locked-image
publication remain explicitly unqualified; do not count these authored fixtures
as runtime evidence.
Compile-only checkpoint: source-built native host harness completed with five
warnings and zero errors; PresentationCore fixtures with four warnings and zero
errors; bridge fixtures with zero warnings/errors. No tests, source verifiers,
application/GPU/VM runs, benchmarks or CI qualification were executed. ProGPU
contains the latest fetched `origin/main`; this batch adds its shared ownership
documentation/rules, while the implementation belongs in source-built WPF.

Geometry-region selection connection: portable `PathGeometry.HitTestWithPathGeometry`
now exports bounds-free operands to the typed provider before its WPF graphics-DLL
import. ProGPU shares its original Direct2D comparison body between the COM entry
and a bounded synchronous C query; both renderer modes use the same provider.
Holes/cancellation, containment direction, transforms and first-operand relative
tolerance remain explicit. The existing host harness now exercises public geometry
comparison and clipped `VisualTreeHelper.HitTest`; it owns the host with `using`
so a rejected pre-run query cannot leak it. Native, managed, bridge and source
fixtures are authored, not executed. See the ProGPU
[contract and original-code provenance](../external/ProGPU/docs/native-mil-geometry-utilities.md#filled-relation-connection--core-selection-traversal).
No Windows SDK guard or qualification gate is removed by this connection.
Compile-only checkpoint: ProGPU `4c29b833` is pushed and contains latest fetched
`origin/main`. Strict AppleClang C++20 geometry-utility and Direct2D compatibility
targets compile/link. Release ProGPU.Tests builds with 0 warnings/0 errors;
bridge fixtures 116/0; source-built application harness 4/0; source
PresentationCore fixtures 4/0, including the existing NU1701 package warning.
No fixture, source verifier, application, VM/GPU/image, benchmark or CI
qualification was executed. These counts are not evidence of runtime parity.

Windows native popup connection (MVP/Toolkit ComboBox/menu open and click):
ProGPU now owns `NativePopupWindow.TryConfigureOwner` and a Win32 implementation
using its existing native window accessors. It validates local same-thread
top-level windows, configures a hidden owned popup with nonactivating/tool-window
styles, preserves unrelated style bits, and refreshes its frame without showing,
moving, activating or promoting it to topmost. A static native subclass returns
MA_NOACTIVATE without eating clicks, forwards other messages, and removes itself
on destruction. Configuration failures restore attributes where possible; the
caller must destroy any rejected hidden popup.

LibreWPF's decoration service now calls that shared typed API, and portable native
popup selection admits Windows. This affects portable owners only; native WPF
HWND routing is unchanged. Hidden Windows popup setup now disposes and fails
explicitly on rejected or throwing configuration before Show. Both renderer modes
use this host/platform path; no C++ scene, shader, wire or fallback algorithm needs
a paired change. Fixtures cover style preservation, every apply-stage failure,
invalid/foreign/child/visible windows and actual hidden HWND ownership plus click
activation messages on Windows. Fixture execution is deferred, not passed.

This closes the specific missing native-owner route, not the Windows SDK gate.
Next is the source-built package activation/media-startup path from the module
initializer through first frame and common resource construction; identify any
remaining native-Windows MIL entry before enabling explicit native SDK activation.
Keep SDK/sample/image/VM/CI qualification deferred until implementation freeze,
then mandatory. See ProGPU `docs/native-mil-popup-placement.md` for public Win32
references, original implementation provenance and native callback lifetime.
Compile-only checkpoint: ProGPU.Tests 0 warnings/0 errors; bridge fixtures 21/0
on the final rebuild; source-built application harness 5/0. The initial parallel
bridge build also succeeded with 117 warnings, including one shared-output copy
retry; subsequent overlapping-graph builds were serialized. No tests, source
verifiers, applications, VM/GPU workloads, benchmarks or CI qualification ran.

The initial startup trace confirms `MediaSystem.Startup` already branches on the
frozen portable media choice before `MilVersionCheck`. The next concrete utility
trace is `Visual` geometry-hit traversal (`FillContainsWithDetail`) through
`PathGeometry.HitTestWithPathGeometry`, which still directly enters legacy
`MilUtility_PathGeometryHitTestPathGeometry`. Separately, path animations enter
`GetPointAtFractionLength` and its legacy utility. Determine their actual import
routing and connect required source operations through shared ProGPU algorithms;
do not enable the SDK by assuming the new popup route also closes those calls.


Native-popup framebuffer ownership (MVP/Toolkit menu open while its owner moves
between monitors): source inspection confirmed two owner-DPI writes into a
separately surfaced popup, in `WpfPortablePopupBridge.TrySetOwnerGeometry` and
`WpfPortableNativePopupHost.SetDeviceScale`. The native adapter now exposes
`SetOwnerTransportScale`, updating only legacy position decoding. The bridge
updates source framebuffer DPI only for owner-surface popups. Native popups retain
their one-time creation seed until their own host resolves surface geometry, and
subsequent owner changes cannot overwrite it. Existing parent-first single-move
fixtures now also assert independent parent/child framebuffer scales remain
unchanged; a real-adapter hidden-construction fixture covers the second write
path. Both renderer modes share this host-only implementation; no ProGPU scene,
shader, GPU fallback, native C ABI or renderer algorithm changes apply.

The next source-backed Windows application blocker is explicit: native portable
popups are rejected by `WpfPortableNativePopupHost.ShouldUseNativePopup`, and
`SilkNetWpfWindowDecorationService.TryConfigurePopupOwner` implements Cocoa/X11
but no Win32 owner route. The ProGPU backend already owns Win32 window-parent and
style primitives in `Win32NativeWindowPlatform`; use shared typed platform support
to connect a nonactivating owned native popup before admitting the Windows SDK.
Do not simply remove the SDK guard or silently constrain that application path
to an owner surface. The completed framebuffer-write fix is not a monitor/input
runtime qualification claim. Final tests, VM/image comparisons and CI are deferred.
Compile-only checkpoint: bridge fixtures 116 warnings/0 errors initially and
20/0 on the final rebuild; source-built application harness 4/0. No fixture or
application execution. Latest ProGPU `origin/main` is contained in the branch.


Host/popup desktop connection (MVP/Toolkit ComboBox/menu open, move and click):
the stock host now publishes desktop scale from its actual native client-size
policy, via ProGPU's `FromWindowCoordinates` and the optional typed source seam.
Native pointer events use the same desktop-vector inverse, independently of
framebuffer DPI; compatibility/diagnostic input retains its existing route.
The popup bridge now decodes legacy device transport to desktop offsets before
mapping to owner DIPs, so overlay placement and local input share one frame.
Owner moves/scale changes map those offsets forward and keep coherent parent-first
publication. Native diagnostic queries use the popup source's own desktop frame;
independently surfaced popup scales are not replaced with their owner's scale.
Both renderer modes share these platform/source changes; no native renderer,
shader, fallback or C ABI algorithm changes apply. Regression fixtures are authored
for fractional/unequal desktop scales, negative origins, independent framebuffer
changes, movement and pointer mapping; execution is deferred.

This closes the identified host/overlay/input unit mismatch, not full mixed-monitor
qualification. The remaining application dependency is native-popup framebuffer
ownership across independent monitor changes: owner DPI propagation still enters
the popup's native host/source, while the surface can subsequently resolve its own
DPI. Trace and close that lifetime/update ordering before Windows SDK admission;
do not keep expanding placement math in isolation. Windows admission, final app
fidelity, VM/image comparisons, package production and exact-head CI remain open.
Compile-only checkpoint: ProGPU.Tests 0 warnings/0 errors, bridge fixtures 21/0
(final source-guard rebuild 20/0), source-built application harness 4/0.
No test execution or CI qualification.


Popup extent/limit connection (MVP/Toolkit menus and ComboBoxes at desktop edges):
source `Popup.UpdatePosition` previously used framebuffer DPI for root-size
nudging even on a logical-unit desktop. It now uses the ProGPU desktop-vector
contract, alongside child-interest points, absolute-placement offsets and
desired-size restrictions. Screen constraints operate in desktop units and return
client-DIP sizes; legacy HWND sources keep their device-transform path.
ProGPU owns intrinsic forward/inverse vector arithmetic without origin translation
or inverse-reciprocal overflow. Both renderer modes consume the same source helper,
not separate rendering algorithms. See the [connection and authored fixtures](../external/ProGPU/docs/native-mil-popup-placement.md#popup-extent-and-limit-connection).
Host publication and bridge-local overlay/input conversion remain the next coupled
dependency. Automatic mapping, Windows admission and final gates remain unchanged.
Build-infrastructure follow-up: space became available again, but the clean source
test restore exposed NU1605: PresentationCore.Tests pins System.Formats.Nrbf
10.0.11 while System.Private.Windows.Core.TestUtilities
11.0.0-preview.7.26359.117 requires its matching 11.0 preview. The test project now
uses `ProGpuWpfTestFormatsNrbfVersion`, capturing the upstream NRBF pin before the
portable product pins apply. Product NRBF stays at 10.0.11; only the source test
dependency is aligned with its utility package. A normal restore/build now succeeds
for PresentationCore.Tests (9 warnings/0 errors), closing the earlier disk/restore
compile blocker. NU1701 remains visible and runtime compatibility remains a final
qualification item. No downgrade warning was suppressed. The source-contract
fixture protects the separation between test and product pins.
Compile-only results: ProGPU.Tests 0 warnings/0 errors; the source-built
application harness 4/0; PresentationCore.Tests 9/0 after normal restore;
PresentationFramework.Tests 3/0 after normal restore; final bridge fixtures 116/0.
An earlier bridge rebuild reported 115 warnings; this is not a warning-cleanup
claim. These builds close the
source-consumer compilation gap, not application/runtime acceptance. Existing
NU1701 and other warnings remain visible. No tests, source verifiers, graphical
applications, VM/image workloads, benchmarks or CI qualification were executed.

Client-to-desktop source prerequisite (same MVP/Toolkit popup placement action):
ProGPU now owns an immutable validated desktop transform and optional typed source
capability. Source-built WPF point-to/from-screen conversion, including portable
HwndSource ownership, uses its shared intrinsic forward/inverse mapping. Legacy
origin changes retain desktop scale; framebuffer DPI changes stay independent.
Both renderers share this source integration, with no new native render algorithm.
The stock host's automatic scale policy is deliberately unchanged pending the
coupled popup migration: child-interest points, size restrictions, relative
offsets, owner-surface placement and input must use the same units as the anchor.
See [contract, provenance, public specification and unexecuted fixtures](../external/ProGPU/docs/native-mil-popup-placement.md#client-to-desktop-source-contract-prerequisite).
This is a connected source capability, not full host/popup admission or runtime
qualification. Continue the named popup consumers and host publication next.
Compile checkpoint: ProGPU.Tests Release succeeds with 0 warnings/0 errors.
The source PresentationCore.Tests build stops with three MSB3491 generated-file
write errors because the host disk is full (104 MiB available when inspected).
That build is incomplete and must be retried after space is restored; the source
and bridge fixtures and the full harness are not qualified at this checkpoint.
No tests, source verifiers, VM/image workloads, benchmarks or CI qualification ran.

Popup DPI publication checkpoint (MVP/Toolkit ComboBoxes and nested menus):
`UpdatePortablePresentationSourceDpiScale` previously propagated the new owner
device origin while popups still had their old scale, then updated scales in a
second pass. For an owner at desktop X=100 and a popup at X=140, a 1-to-2 scale
change could publish a native move to X=240 before correcting it to X=140.
Nested popup propagation repeated those intermediate moves. This is source-backed
ordering evidence, not a reproduced desktop run.

The host now sends owner origin and scale through one typed popup geometry
update, parent before child. Bridge device state is assigned together; only
the final source origin and one native position are published, with the native
scale updated before position conversion. The fallback for unpositioned owners
and legacy handle-only requests preserves the request origin across scale
changes; already updated popups are no-ops in that pass. Ordinary owner movement
continues to update positions without republishing scale.

This is host/source integration shared by both renderer modes, not a rendering
algorithm change: no new ProGPU C++, shader, managed renderer or geometry algorithm
is needed. The existing device-coordinate transport ABI and monitor coordinates
are unchanged. Regression fixtures capture every source origin/native position
for nested surfaces, positive/negative/zero origins, 1/2/1.5/1 scale transitions,
repeated notifications, native and managed host selections, and unpositioned or
legacy owners. They are authored for final qualification, not executed here.
Mixed-monitor coordinate projection, independently changing popup DPI, actual
capture/input fidelity and Windows package admission remain open. Continue those
application integration blockers; do not expand the general Direct2D surface.
The next bounded trace is the same ComboBox/menu placement action through
source `PointUtil.ClientToScreen`, `Popup.ToPortableScreenDevicePoint` and
`WpfPortableNativePopupHost.SetPosition`, compared with the host's existing
`ResolveLogicalClientDimension` content-scale handling. Portable client-to-screen
previously added the source origin without a separate client-to-desktop scale;
the source prerequisite above adds that capability, with host publication pending.
Resolve that coordinate contract before treating scaled desktop placement as
implemented or removing a Windows admission guard.
Compile-only results: the final bridge/fixture Release build succeeds with
21 warnings and 0 errors; the source-built PresentationFramework host harness
succeeds with 4 warnings and 0 errors. An earlier bridge rebuild reported 116
warnings; incremental counts do not establish warning cleanup. No fixtures,
source verifiers, graphical/VM workloads, benchmarks or CI qualification ran.

Fill-query connection checkpoint (MVP/Toolkit layout clips and pointer input):
portable unstroked bounds now route by frozen media backend through the typed
geometry provider, including generic groups, serialized paths and primitive MIL
line/cubic transport. Curve extrema and hollow filtering replace control-hull
bounds on those routes. Fill point queries use the shared ProGPU C++ Direct2D
core through one synchronous span call, with intrinsic double edge metrics;
source LineGeometry retains its no-filled-area result. Contributing-pen bounds
and stroke hit tests are not redirected into fill algorithms. The Windows SDK
guard remains, pending the remaining media utilities and application admission.
See the [contract, provenance and unexecuted fixtures](../external/ProGPU/docs/native-mil-geometry-utilities.md#fill-bounds-and-point-queries--core-layoutinput-connection).
This is an implementation connection, not runtime, image or performance proof.
The next pen dependency at that checkpoint was ordinary stroked content:
`BoundsDrawingContextWalker.DrawGeometry` passes the pen to `GetBoundsInternal`,
while its clip pushes pass null pens and use the newly connected route. Close
the contributing-pen path through shared ProGPU stroke coverage before Windows
admission; do not replace it with fill-bound inflation or expand unrelated APIs.
Final compile-only checkpoint: ProGPU `7d8ef97b` is pushed to PR #139; native
geometry fixture/shared Direct2D core compile under strict AppleClang C++20.
Release managed builds: ProGPU tests 0 warnings/0 errors, bridge fixtures 20/0,
source PresentationCore fixtures 8/0, source-built application host harness 5/0.
An earlier bridge rebuild reported 116 warnings; the final incremental count is
not a warning-cleanup claim. No tests, source verifiers, application/GPU/VM runs,
image comparisons, benchmarks or CI qualification were executed in this batch.
Both PRs remain open; unrelated native presentation edits and deleted performance
artifacts were preserved outside these commits.

Stroke-query backend checkpoint: ProGPU now has a batched device-independent
C ABI and managed wrapper for whole-figure stroke bounds/hits, retaining source
gaps, incoming smooth joins, pen/dash state and post-widen transforms. It shares
the existing native segment emitter and Direct2D stroker; native/managed dash
validation uses intrinsic lanes. This is a prerequisite, **not closure of the
application pen path**. The follow-up closes constant-edge loss in shared native
preparation: point caps, dash-phase visibility, source endpoint eligibility,
zero-distance gaps and incoming joins now survive. Original ProGPU cap/transform
helpers are reused. The subsequent connection below adds the host/source query
encoder and routes the named DrawGeometry bounds/hit consumers.
The Windows SDK guard and final acceptance gates remain unchanged. See the
[stroke transport and remaining dependency](../external/ProGPU/docs/native-mil-geometry-utilities.md#stroke-query-prerequisite--not-yet-source-wpf-pen-admission).
Compile-only checkpoint: the strict AppleClang C++20 native query fixture target
is up to date, and the final ProGPU.Tests Release build succeeds with 0 warnings
and 0 errors. No fixtures, runtime gates or CI qualification were executed.
The point/endpoint follow-up also compiles and links the native geometry-utility,
Direct2D core and Direct2D compatibility fixture targets. Added cap-oracle and
public COM query fixtures remain unexecuted. This closes the native preparation
dependency, not the source-WPF consumer or final application acceptance gate.

Source-WPF pen connection checkpoint: the shared typed provider now implements
render bounds and stroke containment using the new complete-figure ProGPU query
encoder. `Geometry`, serialized path/primitive transport and Rectangle/Ellipse/Line
instance overrides select it by frozen portable media policy. Source pen state
retains brush identity, thickness, caps, joins, miter and dash data; dash narrowing
uses SIMD. Geometry-local transforms precede native widening and world transforms
follow it. Bounds union actual fill and emitted stroke coverage, rather than
inflating fill rectangles. Both renderer modes use this same provider.
See [connection contracts and authored fixtures](../external/ProGPU/docs/native-mil-geometry-utilities.md#source-wpf-pen-query-connection--implementation-only).
This closes the named source-level DrawGeometry bounds/point-hit dependency, not
runtime application acceptance. Continue the application startup/interaction
trace for remaining core blockers; do not use this connection as a reason to
expand geometry or Direct2D API breadth. Windows SDK admission and every final
package, VM, image, lifetime, performance and CI gate remain required.
Compile-only results for this connection: ProGPU.Tests 0 warnings/0 errors;
ProGPU.Wpf.Tests 116/0; source PresentationCore.Tests 8/0; the existing source-built
PresentationFramework host harness 0/0. Fixtures were authored but not executed.
No native-module loading, graphical application, VM, benchmark or CI qualification
was performed; warning counts do not establish cleanup or runtime compatibility.

SDK activation checkpoint: the package bootstrap previously always selected
managed portable hosts, so the native direct-host harness did not establish a
native path for the MVP/Toolkit applications. `ProGpuWpfRendererMode=NativeMilWgpu`
now selects the existing typed native host factory in the SDK bootstrap; the
managed default is unchanged. The SDK gate accepts a renderer-mode lane for the
same package applications and requires the direct native host gate for native
selection. Invalid configuration and missing activation fail explicitly.

**Known core blocker:** the Windows package path still needs a complete source-built
backend selection. Typed window activation and render-wakeup registration now
accept Windows hosts. MIL transport startup now uses the shared frozen selection
described below; media-resource utilities and popup/interop creation remain open.
The native SDK selector continues to report this
limitation instead of silently using Windows MIL. Do not count the direct Windows
drawing harness as completion of this application path. macOS/Linux native SDK
selection is wired, not runtime-qualified. See the SDK README's renderer-selection
contract and the bounded checkpoint below.

Windows activation prerequisite checkpoint:

- Acceptance application/action: package-mode MVP and Toolkit/AvalonDock startup,
  first window, hide/show, activation, title updates, redraw and close.
- Source-backed blocker: `PortableWindowActivationService` rejected Windows even
  after explicit typed registration; `Window.Activate`, `DragMove` and icon updates
  also selected by OS, and `PortableMediaContextRenderService` returned an empty
  registration on Windows.
- Implementation: explicit host registration now enables portable windowing on
  every platform, existing portable window identity selects those operations,
  and real callback registrations deliver render invalidation/delays. Ordinary
  unregistered Windows WPF retains its existing native path. A registered factory
  that rejects a window throws before native HWND creation; an active portable
  window without a host run-loop callback also throws instead of changing loops.
  The portable application loop no longer creates the Windows parking window.
  `Window.Activate()` reports the host request result and no longer fabricates
  activation after rejection; actual host events own `IsActive` changes.
- Boundaries: this is window-service plumbing for **both** ProGPU renderer modes,
  not a renderer algorithm change. No ProGPU C++/shader/scene implementation needs
  a paired change. Existing ProGPU-owned typed callback contracts are reused;
  source-built WPF owns the integration. No reflected adapters or new CPU/GPU
  fallback are introduced. Registrations are startup/lifetime configuration, not
  permission to switch a live application between Windows MIL and ProGPU.
- Authored regressions cover explicit registration/clear, window lifecycle and
  reuse, rejected activation, missing run-loop callbacks, delayed invalidation,
  and independent render-wakeup registration disposal. These service fixtures
  are not image or real-device proof. Runtime execution remains deferred.
  PresentationFramework grants its signed unit-test assembly internal access,
  matching the existing PresentationCore test arrangement; no public test seam
  or reflected invocation is added.

Historical next dependency (transport selection is implemented below): select the portable MIL
transport **before** source-built `MediaContext`/`MediaSystem` initialization and
composition locking, then route ordinary Toolkit popup/interop-handle creation
by portable source ownership. `MediaContext` calls `MediaSystem.Startup`, which
still chooses MIL transport using `s_isWindows`; `CompositionEngineLock` in
`Common/Graphics/exports.cs` still selects native MIL locking by OS. `Popup` has
separate OS guards, and `WindowInteropHelper.EnsureHandle()` directly calls
`Window.CreateSourceWindow(false)`, bypassing the portable show-time factory.
Its hidden-window creation/ownership semantics must be preserved by that route.
Do not replace every Windows API check mechanically: native platform services
may remain Windows-specific, but portable source handles must never enter the
Windows MIL/HWND renderer. The direct-host harness bypasses package startup and
therefore cannot close this dependency. Keep the explicit Windows SDK guard until
the complete required route is connected.

Media transport checkpoint: the package MVP/Toolkit startup path now has the
ProGPU-owned `PortableWpfRuntime` choice before media initialization. First use
freezes transport identity across dispatchers; a late backend switch throws.
`MediaSystem`, composition locks and the MIL notification window consume it;
direct synchronous/asynchronous Windows channel requests fail closed in portable
mode. The native host harness and SDK native bootstrap select portable transport
before loading/initializing source-built WPF. This does not remove the SDK guard.
See [contract, provenance, references and qualification limits](../external/ProGPU/docs/native-mil-startup-selection.md).

Next core dependency: connect ordinary media-resource helpers (for example
`Geometry.GetProGpuPolygonBounds` versus the Windows MIL utility and
`PathGeometry.InternalCombine`), hidden interop-handle creation and popup ownership
to the selected portable backend. The initial transport choice is implemented,
not proof that the whole application no longer calls legacy MIL. Preserve the
existing ordinary managed/native renderer algorithms rather than adding reduced
Windows-only substitutes. Required Windows package admission remains pending.

Hidden-source checkpoint (core MVP/Toolkit startup, interop hooks, show/close):
`WindowInteropHelper.EnsureHandle()` now reaches the same ownership decision as
`Show` before any Windows HWND/MIL source creation. The explicit ProGPU-owned
`CreateHidden` callback creates a hidden host/source with no WPF root attached;
the first `Show` attaches the existing root to that source. Source initialization
publishes the handle before its one-time event, repeated queries reuse it, and
close-before-show releases ownership. Missing/rejected hidden factories and zero
handles fail closed, with cleanup for rejected owned activations. A custom host
factory returning null no longer silently selects a default host.

The portable handle remains the existing typed presentation-source identity,
**not a general-purpose HWND**. Native user32 calls require an explicit platform
adapter; this checkpoint does not make arbitrary third-party P/Invoke portable.
The Windows SDK guard stays in place. Popup creation/placement/capture still needs
ownership routing, and all of this implementation remains runtime-unqualified.
See the [hidden-source contract](../external/ProGPU/docs/native-mil-hidden-window-source.md).

Hidden-source Release compilation checkpoint (macOS, final incremental builds):
ProGPU tests 0 warnings/0 errors; source-built WPF host harness 0/0;
PresentationFramework lifecycle tests 2/0; WPF bridge tests 20/0. Earlier builds
in this batch reported 5 and 116 warnings while rebuilding dependencies; the
incremental totals are not warning-cleanup claims. Regression bodies, source
assertions and native hidden windows were not executed. ProGPU was refreshed
from `origin/main` with zero missing commits; shared contract commit `97fd4b3e`
is on PR #139. Runtime, VM, image, lifetime, benchmark and CI qualification are
still deferred, and the existing SDK acceptance gates remain intact.

Media-resource source finding: `PathGeometry.InternalCombineManaged` currently
returns a bounding rectangle, not a boolean path result. `FrameworkElement` uses
`Geometry.Combine` for transformed layout clips; `CaretElement` uses it for
selection unions/intersections. This is a named core dependency, not deferred
API breadth. Do not simply enable that approximation on Windows or replace
arbitrary clips with bounding boxes. Reuse ProGPU's owned geometry algorithms
for the synchronous source-WPF contract; its existing portable C++ Direct2D
geometry core is an implementation candidate, not yet a connected WPF utility.
Keep the next geometry batch bounded to these real application callers.

Popup ownership checkpoint (MVP/Toolkit ComboBox, menu and tooltip open/close):

- Source-backed blocker: `Popup.BuildWindow` and its placement, resize, visibility,
  capture, automation and destroy branches used the OS instead of the owner/source.
  On Windows this could feed portable identities into HWND/MIL operations. Host
  rejection elsewhere silently created an unhosted `PortablePresentationSource`.
- Implementation: the owner and frozen transport select creation; the resulting
  source identity selects all subsequent portable branches. A missing, rejected,
  disposed, wrong-kind or zero-handle host result is not published. Accepted invalid
  results are returned to their host for cleanup. Creation failure drops cached
  owner/service references. Destroy uses the captured source after clearing the
  helper reference and removes host ownership even after prior source disposal;
  local source disposal is attempted even if the host or root detachment throws.
  Portable HwndSource wrappers use their typed portable owner for client sizing.
- Related input consumers: ComboBox and Popup capture restoration, MenuBase capture
  and focus restoration, Menu system-menu routing, and tooltip deactivation now
  share `PopupControlService.UsesNativeWindowing`/`HasNativeMouseCapture`. Portable
  sources skip user32 focus/capture checks even on Windows; native WPF keeps them.
  Portable placement avoids native HWND-recreation/monitor-origin DPI heuristics.
- Applicability/provenance: original source-built WPF host integration, shared by
  both ProGPU renderer modes. Existing ProGPU-owned popup host/scene algorithms,
  callbacks, native/managed renderers, shaders and geometry are unchanged; there
  is no matching C++ algorithm to alter for these source-WPF OS guards. Owner
  resolution walks the existing visual/placement chain in O(H) time for height H;
  ordinary source-policy checks are allocation-free O(1). This is control/lifetime
  work, not a new compute or scalar fallback. The shared startup/ownership research
  documented in the ProGPU contract remains applicable; no rendering speed claim.
- Authored fixtures: real portable presentation sources with a typed fake host
  cover create/size/place/show/hide/hit-test/destroy, already-disposed sources,
  host rejection and invalid source results, and host-destroy exceptions. The
  existing helper is internal for signed unit-test access, not a public test API
  or reflected probe. Source assertions cover related control routing. These are
  not acceptance applications or runtime/parity evidence; execution is deferred.

This closes the identified ownership-routing implementation gap, **not complete
popup fidelity or Windows SDK admission**. At that checkpoint, portable screen bounds still
constrain placement to the owner client even for separately surfaced popups;
monitor work-area/edge escape and mouse-cursor geometry/coordinate utilities need
their typed platform route. Existing portable animation/system-menu limitations
are unchanged and remain explicit. Final qualification must exercise nested
menus, ComboBox/tooltip open/close, capture restoration, owner close/reopen,
cross-monitor DPI/placement and resource lifetime in the real package apps.
Do not enable the Windows SDK merely because these branches compile.

Popup checkpoint compilation: final Release PresentationFramework tests compile
with 2 warnings/0 errors (the earlier dependency rebuild reported 6/0), WPF bridge
tests with 116/0, and the source-built host harness with 0/0. These totals include
existing dependency/analyzer warnings; no warning-cleanup claim is made. All
commands used the repository SDK with `--no-restore -m:1 -nr:false -v:q`.
No test bodies, source verifiers, VM/GPU workloads, image/lifetime/benchmark runs
or CI qualification were executed. Existing SDK gates are unchanged. ProGPU
`origin/main` was refreshed with zero commits missing; this WPF-specific slice
does not change the submodule commit or pending unrelated native work.

Popup placement checkpoint (MVP/Toolkit menus, ComboBoxes and tooltips at an
owner-window edge): ProGPU now owns a typed actual-surface bounds query and
streaming monitor selection. The bridge reports owner-surface bounds only for
popups without a native host, including before first Show. Native hosts use
selected screen/work-area limits instead of owner-client limits. Source WPF
retains its menu/tooltip work-area preference and fails explicitly on unavailable
or invalid bounds. Queries cannot fall through to another window's registrar.
See the [contract and qualification limits](../external/ProGPU/docs/native-mil-popup-placement.md).

This closes the source-backed owner-only placement constraint, not mixed-DPI
coordinate projection, cursor geometry or interactive popup fidelity. Both
renderer modes share this host seam; no new native/managed rendering algorithm
or fallback was introduced. The Windows SDK admission guard remains. The next
core implementation dependency is the source-WPF geometry utility route used by
transformed layout clips and editing selection, already identified above;
do not resume general Direct2D API expansion.

Placement compilation checkpoint: ProGPU tests 0 warnings/0 errors; WPF bridge
tests 116/0; PresentationFramework tests 6/0; real source-built WPF host harness
5/0 (Release on macOS, repository SDK, `--no-restore -m:1 -nr:false -v:q`).
Warnings include existing dependency/analyzer diagnostics, not a cleanup claim.
ProGPU contract commit `52aafb89` is pushed to PR #139; the superproject tracks
it. No tests, source verifiers, runtime/GPU/VM work, images, benchmarks or CI
qualification were run. All existing qualification gates remain required.

Geometry utility prerequisite checkpoint (MVP/Toolkit transformed layout clips
and editing selection): ProGPU now exposes the existing C++ Direct2D boolean
boundary algorithm through a device-independent main-backend C API and managed
`NativeGeometryUtilities` wrapper. It accepts canonical paths, independent fill
rules and absolute tolerance, returning actual closed contours with one owned
snapshot. No GPU/window/system COM activation, bounds substitute or new boolean
algorithm is introduced. The shared core's intrinsic kernels remain authoritative.

At that prerequisite checkpoint, the source-WPF bounds-only implementation had
not yet been replaced. The next
bounded outcome is its typed adapter and startup registration, preserving nested
geometry, transforms, relative tolerance and invalid-number behavior for both
renderer modes. This native prerequisite cannot by itself admit Windows SDK
applications. See the [contract/provenance and remaining connection](../external/ProGPU/docs/native-mil-geometry-utilities.md).

The device-independent C++ conformance executable and managed ProGPU test project
compile in Release; managed builds report 0 warnings/0 errors and the strict
AppleClang native build reports no diagnostics. Test bodies, verifiers,
runtime/GPU/VM/image/lifetime/benchmark work and CI were not run. The CTest gate is
registered for final qualification. Full backend shared libraries and actual
managed/native P/Invoke still require qualification. ProGPU `origin/main` was
refreshed with zero commits missing; unrelated native edits remain preserved.
ProGPU implementation commit `93120e82` is pushed to PR #139 and tracked by the
superproject. The utility fixture is included by the existing unfiltered CTest
commands in the Unix and Windows native build scripts; those commands have not
been executed during this implementation-first phase.

Geometry connection checkpoint: source-built `PathGeometry.InternalCombine` now
selects a typed `IPortableGeometryOperations` provider using frozen media backend
identity. Its bounds-only implementation is removed. A separate bounds-free
operand export preserves groups and nested combinations without asking for
CombinedGeometry.Bounds. The host adapter uses existing ProGPU path conversion,
affine/arc handling and fill compilation, then the C++ actual-boundary utility;
returned contours become real source-WPF PathGeometry. Default provider installation
occurs in host construction and activation bootstrap without native/GPU work.
Explicit provider registrations remain authoritative. Both renderer modes now
require the matching native CPU geometry binary for these operations.

Source-backed contract handling includes group fill versus union semantics,
operand/result transforms, absolute/relative tolerance, nonfinite input and
explicit unsupported finite-range failures. This is not runtime/image parity.
The Windows SDK guard remains: other source geometry bounds/hit-test utilities
still have OS-selected legacy MIL routes. Finish those required core consumers
next, then qualify transformed clips/editing in real applications; do not expand
general Direct2D API breadth. See the updated ProGPU geometry utility document
for exact provenance, startup/dependency boundaries and authored fixtures.

Connection compilation checkpoint: final Release builds report ProGPU tests
0 warnings/0 errors, WPF bridge tests 116/0, PresentationCore tests 8/0 and the
real source-built WPF host harness 4/0. Earlier compilation defects in the new
adapter's namespace, widening call and empty-bounds signaling were corrected;
these final totals are not test passes or warning-cleanup claims. Exact
transformed curve extrema now feed relative tolerance instead of control hulls,
with a scaled-cubic regression authored. ProGPU contract commit `83bc51ad` is
pushed to PR #139 and tracked here. No test bodies, source verifiers, GPU/VM/image,
lifetime, benchmark or CI qualification were executed. All acceptance gates and
the existing Windows SDK guard remain intact.

Transport-selection compilation checkpoint: final Release ProGPU tests compile
with 0 warnings/0 errors, the source-built WPF host harness with 4/0, WPF tests
with 115/0 and SDK smoke harness with 0/0. Both actual conditional SDK bootstrap
branches compile with 0/0 against updated interop/source-built WPF assemblies.
Tests, native dependency observation, runtime/VM/image/benchmark work and CI
qualification remain deferred; these are compilation results only.

Compilation-only evidence for this prerequisite (Release, macOS host):

| Project | Compiler result |
| --- | --- |
| `ProGPU.Wpf.RealPresentationFrameworkHarness` including source-built WPF | 5 warnings, 0 errors |
| `PresentationCore.Tests` including the new wakeup fixtures | 4 warnings, 0 errors |
| `PresentationFramework.Tests` including the new lifecycle fixtures | 6 warnings, 0 errors |
| `ProGPU.Wpf.Tests` including updated source-contract assertions | 115 warnings, 0 errors |

The initial PresentationFramework test compile rejected internal access; the
signed friend-assembly declaration above fixes that compile failure. Builds use
the repository dotnet SDK and `-c Release -m:1 -nr:false -v:q`; no tests, source
verifiers, package runtime, VM, image, benchmark or CI qualification were run.
Warnings remain visible, including the existing test-utility package compatibility
warning. ProGPU `origin/main` was refreshed with zero commits missing from the
feature branch; this prerequisite does not change its native implementation or
the submodule commit. Existing unfinished ProGPU work remains untouched.

Compilation checkpoint: both conditional SDK bootstrap branches compile against
the source-built WPF/ProGPU assemblies, and the updated external SDK gate harness
compiles. These compilation-only checks do not pack the SDK, run its initializer,
validate MSBuild error cases or execute an application. Exact-package two-mode
build/runtime behavior remains in the final qualification phase.

Surface recovery: source inspection of the native host harness/MVP presentation
path found that both renderer modes discarded failed acquisition requests and
kept stale surface configuration. The shared ProGPU recovery policy now handles
Timeout/Outdated/Lost, and both host paths schedule a presentation-bearing retry,
retain correct frame counters and reject null acquired textures. Null views never
reach either renderer. Device loss and out-of-memory fail explicitly; automatic
device/resource recreation remains open. See
[surface recovery contract and qualification](../external/ProGPU/docs/native-mil-surface-recovery.md).
Regression fixtures are authored; runtime/VM/CI qualification remains deferred.

Device recreation checkpoint (supersedes the preceding open implementation item):
both host modes now retire a lost target outside the active frame and reconstruct
its renderer/session on a fresh device, preserving source-built WPF roots and
native windows. Popups resolve their owner's current device instead of a captured
context. A typed recreation event lets application-owned resources renew before
the first replacement frame; old image leases cannot cross device domains. The
native host gate now includes an authored first-frame/loss/recovery case. Device
recreation is implemented, not runtime-qualified; popup cohorts, external producers,
mid-submission loss and failure/close paths remain final qualification requirements.

## Implementation history: popup and presentation integration

These checkpoints record completed implementation batches and remaining subsystem
limitations. Their suggested next steps are historical, not an instruction to
finish every advanced mapping consumer before returning to the applications.
Use the finish-first rule above to admit work to the current core queue.

Popup checkpoint: canonical owner-surface composition, local clipping, change
revision and separate native-window renderer inheritance are implemented. See
[contract and qualification limits](../external/ProGPU/docs/native-mil-popup-composition.md).
This supersedes the blanket popup rejection below, but does not close the core
desktop milestone. Next implementation focus is presentation region/viewport/DPI
support plus any popup platform gap that prevents the acceptance applications.

Region checkpoint: immutable typed region snapshots and canonical outer-minus-hole
clipping are implemented above the application and owner-surface popup roots. See
[region contract and deferred qualification](../external/ProGPU/docs/native-mil-window-regions.md).
The existing native compiler flattens rectangle exclusions into one nonzero leaf;
this does not need a new shader or a per-hole boolean program. Next is the shared
native presentation contract for partial viewports and independent X/Y DPI.

Presentation contract checkpoint: ProGPU now has the opt-in generated native/managed
viewport-plus-per-axis-DPI descriptor and strict submission validation. Advanced
mapping execution is **partially integrated**, not enabled; the native backend rejects it and
host guards remain. The ordered consumer checklist is in
[native scene presentation](../external/ProGPU/docs/native-scene-presentation.md).

Native state/domain checkpoint: per-axis guideline snapping and viewport-aware
layer/scissor domains now consume that record, including zero-origin local-cache
pages and scope restoration. Geometry localization, glyph/path rasterization,
masks/effects and cache keys still need integration; no viewport/DPI guard has
been removed and this is not qualified presentation support.

Geometry/cache checkpoint: native draw-family preflight/preparation and target
scissors now use the current layer mapping; affected compiled-page, upload and
render-bundle identities include viewport and independent DPI. The main native
semantic render-execution source compiles against pinned WebGPU headers. Layer
composites, masks/effects, 3D and remaining raster/identity consumers still block
advanced presentation enablement; runtime parity is unproven.

Layer/effect checkpoint: cached and tiled layer composites now map through the
parent presentation, blur/shadow preflight and dispatch share per-axis physical
parameters, and layer/effect output content keys include presentation changes.
The semantic renderer, layer-resource execution, state and authored internal
fixtures compile; the portable MIL target builds. Masks/pictures, raster detail,
3D and damage/clear consumers still block removing the host/backend guards.
This is core rendering work, not a new Direct2D/Win2D expansion or a qualification
result. Finish these existing blockers before opening another feature family.

Mask checkpoint: analytic/rounded, bitmap coverage, vector clips, brush/geometry
and non-picture composite masks now receive the current presentation mapping.
Shared affine mapping and authored inverse-UV fixtures compile with the affected
native renderer sources. Picture-backed masks remain explicitly pending; no
viewport/DPI guard is removed. Continue with nested picture raster/sampling,
remaining raster/3D and damage/clear consumers, then the core application/lifetime
closure and final qualification phase.

Nested-picture checkpoint: source-owned raster axes and inherited target-space
viewports are resolved before child texture creation; standalone picture-mask UVs
include parent crop/DPI and logical guideline deformation. The affected sources
and authored child-frame fixtures compile. Composite picture masks still bypass
child sampling uniforms/opacity in their existing direct-texel composition path;
close that shared GPU composition gap next. Renderer/host presentation guards and
all final qualification requirements remain in place.

Sampled-composition checkpoint: composite picture/vector/brush/geometry masks now
consume each child's actual sampling uniforms and opacity through a common shader
function shared with managed/native texture rendering. The existing composition
passes remain GPU-only, the new pipeline is lazy, and child uniform lifetime is
retained through encoded work. The picture-specific gate is removed; the overall
presentation guard remains for the other raster/3D/damage consumers. Native
compilation and the managed ProGPU build succeed; shader execution and image,
lifetime, package and CI qualification remain deferred.

Source-backed blockers are in
`src/ProGPU.Wpf/ProGpuWpfWindowHost.cs:ValidateNativeMilHostConfiguration` and
`src/ProGPU.Wpf/WpfPortableNativePopupHost.cs`:

1. Separate popup roots already owned by another native surface from roots that
   need owner-surface composition. The parent must not reject a correctly owned
   native child window simply because its popup bridge exists.
2. Propagate the explicitly selected renderer mode to popup hosts; do not leave
   an implicitly managed popup attached to a native-MIL owner and claim a wholly
   native application path.
3. For owner-surface popups, emit typed placement/clip/visual state into the
   native scene with correct stacking, invalidation and input coordinates. Do
   not cast private WPF objects or manufacture reflected visual shapes.
4. Preserve window-region clipping, viewport origin/extent and independent DPI
   in shared native presentation contracts. Remove an unsupported guard only
   when the corresponding rendering and ownership behavior is implemented.

Native popup use varies by platform. Current code selects transient native
windows on Cocoa/X11, retains a different Windows popup path, and cannot use
ordinary positionable popup windows on Wayland. Do not qualify one path as all
platform support or drop the owner-surface path from this milestone.

## Feature freeze and final validation

Continue implementation and compilation first, as requested. Author regressions
with code, but do not run tests, source verifiers, VM/image workloads, benchmarks
or CI qualification until the feature phase ends. Do not disable automatic CI.

Feature freeze occurs when the core paths have implementations with no known
blocking unsupported branch in their required configurations. Deferred API or
advanced-presentation branches may remain explicitly unsupported; a branch used
by an acceptance action may not. Then switch to qualification and fixes only:

- Build the complete native renderer and Windows provider, not only the portable
  C++ fixture library. Build managed consumers against those exact artifacts.
- Run `eng/progpu-wpf-native-mil-host-smoke.sh` and the package-mode
  `eng/progpu-wpf-sdk-ci.sh` with `PROGPU_WPF_SDK_CI_NATIVE_MIL_HOST=1` so missing
  runtime/display state cannot become an automatic skipped native gate.
- Preserve Toolkit/AvalonDock coverage and license-controlled paid Xceed
  coverage. Never put license values into source or reports.
- Run relevant existing Microsoft DirectX sample gates and Windows
  native-versus-ProGPU comparisons alongside macOS/Linux comparisons. A sample
  skipped for a missing required API is an open item, not a pass.
- Run regression, reflection/generated-contract, intrinsic/scalar and GPU-path
  differentials, image quality, device/lifetime and performance gates on final
  binaries. Retain exact artifacts and explain differences.
- Fix required PR CI to green at the exact delivery commits and record package
  consumption evidence. Historical counts do not qualify a new commit.

No fixed ETA or percentage is justified by compilation alone. Track the four
milestones and their actual blockers; estimate delivery after the core feature
freeze and first integrated qualification results.

## Deferred expansion — still part of the broader requested goal

General ID2D1/Win2D API completion; arbitrary COM-heavy application portability;
DirectX features unrelated to the core applications; additional shader families,
advanced typography/effects/codecs/3D and nonblocking geometry corner cases remain
recorded requirements. Work already implemented is preserved. Bring an item back
onto the critical path only with a concrete core application dependency.

Partial-viewport/unequal-axis presentation consumers, their 3D projection work and
viewport-only clear/copy behavior follow the same admission rule. Their existing
contract and partial implementation remain documented in ProGPU; they are neither
declared complete nor automatically allowed to delay the core application pass.

In particular, do not continue the general Windows Direct2D command-list bitmap,
image, glyph, mesh and blend expansion merely because its unsupported methods
are easy to enumerate. First prove whether that translator is used by a blocked
core path. The native MIL path is a separate consumer of ProGPU's shared renderer.

Completing this core milestone must be reported as core delivery, not as verified
completion of the entire original goal. Broader scope needs its own remaining
implementation and qualification evidence.
