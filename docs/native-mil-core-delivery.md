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

## Core application closure checkpoints

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
The concrete remaining pen dependency is ordinary stroked content:
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
helpers are reused. Next: add the host/source query encoder preserving
figure/segment flags, and route the named DrawGeometry bounds/hit consumers.
The Windows SDK guard and final acceptance gates remain unchanged. See the
[stroke transport and remaining dependency](../external/ProGPU/docs/native-mil-geometry-utilities.md#stroke-query-prerequisite--not-yet-source-wpf-pen-admission).
Compile-only checkpoint: the strict AppleClang C++20 native query fixture target
is up to date, and the final ProGPU.Tests Release build succeeds with 0 warnings
and 0 errors. No fixtures, runtime gates or CI qualification were executed.
The point/endpoint follow-up also compiles and links the native geometry-utility,
Direct2D core and Direct2D compatibility fixture targets. Added cap-oracle and
public COM query fixtures remain unexecuted. This closes the native preparation
dependency, not the source-WPF consumer or final application acceptance gate.

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
