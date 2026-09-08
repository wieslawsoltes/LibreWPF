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

## Fixed core milestones

| Milestone | Required outcome | Current implementation evidence / open work |
| --- | --- | --- |
| 1. Native application pipeline | Source-built WPF state and updates reach canonical MIL, C++ scene compilation and WebGPU presentation; managed portable mode remains independently selectable. | `WpfNativeMilSceneCompiler`, `WpfNativeMilCompilationSession` and `RenderNativeMilFrame` exist. Close remaining producer/renderer failures encountered by the core applications; do not replace missing native rendering with silent managed replay. |
| 2. Essential desktop interaction | Windows, scrolling, editing, selection, menus, ComboBoxes, tooltips, popups, input, resize and DPI transitions render and respond through typed contracts. | Popup owner-surface MIL composition, native-window renderer inheritance and host window-region clipping are implemented, not runtime-qualified. Partial viewports and nonuniform DPI remain explicitly rejected. Popup platform/lifetime/input behavior and escape beyond owner-surface bounds remain open. |
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

## First implementation block: popup and presentation integration

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
blocking unsupported branch. Then switch to qualification and fixes only:

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

In particular, do not continue the general Windows Direct2D command-list bitmap,
image, glyph, mesh and blend expansion merely because its unsupported methods
are easy to enumerate. First prove whether that translator is used by a blocked
core path. The native MIL path is a separate consumer of ProGPU's shared renderer.

Completing this core milestone must be reported as core delivery, not as verified
completion of the entire original goal. Broader scope needs its own remaining
implementation and qualification evidence.
