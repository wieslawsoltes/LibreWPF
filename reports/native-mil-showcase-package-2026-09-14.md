# Native Showcase package qualification — 2026-09-14

## Current-source macOS diagnostic follow-up

After the Toolkit floating-input correction, the native-mode ShowcaseApp was
rebuilt in the same identified mixed source graph with zero warnings/errors.
`showcase-diagnostic-current-source.log` passes its complete live input gate:
TextBox editing and selection, commands and controls, all seven framework
themes, separate popup surfaces, keyboard navigation, clipped scroll input,
Thumb capture/drag/release and native host resize. The run exits zero with the
required success marker.

`showcase-diagnostic-current-source-performance.log` repeats that live gate and
passes its real native 120-frame performance and memory check. It reports 132→252
sampled presentations in 2,822.664 ms, 2,292 commands/118 draws, 3,985,151.6
managed allocated bytes per frame, host CPU p50/p95/p99 14.541/30.148/35.689 ms,
and completed native-owned logical GPU bytes 9,287,580→9,287,580. Submitted
bytes were 9,287,580→27,726,924 with a sampled 126,070,092-byte peak and 57
pending batches; the completed checkpoint had zero pending batches. These are
single-run observations, not a comparative claim. Logical ownership excludes
swapchain/driver residency. The mixed graph contains current source assemblies
and native libraries over a prior SDK package; final exact-head packages,
Windows/Linux application runs and matched performance remain open.

## Genuine native timing follow-up

The host now publishes an atomic typed native timing/frame snapshot, including
the real device-recovery count. The legacy managed performance and memory APIs
reject native mode. All 214 focused host tests and the actual source/native
device-recovery gate pass. The diagnostic Showcase still completes live input,
then explicitly rejects incomplete native performance qualification instead of
reporting idle managed zeros. Its native snapshot records frame 65, CPU 22.773
ms, compilation 17.754 ms, submission 1.850 ms and 2,292 commands/118 draws.
Native C++ memory accounting and the native report consumer remain required.
See [the complete diagnostic contract and remaining work](../docs/native-mil-performance-diagnostics.md).

Hosted ProGPU `27d13562` Windows x64 package CI passes ordered queries but fails
the default system-FXC ellipse query: one summary hit, zero returned list records.
Exact-result checks, default selection and dependency pin gates remain unchanged.

## Glyph raster retention and performance diagnostics

The latest diagnostic overlay completes **all live input actions and the
120-frame presentation loop**. This supersedes the earlier per-stage blockers
below, but does not qualify final packages or native performance counters.

A Metal System Trace isolated native glyph coverage passes around 375 ms,
with a maximum of 785.335 ms. Host traces showed surface acquisition around
360–390 ms. The improved failure message captured presented 91→91, skipped
0→0, 740.183 ms waiting, 2,292 native commands and 118 draws. The existing
300×2 ms polling deadline is unchanged.

ProGPU `5a4cc291` now retains exact owned outline/segment bytes separately
from positioned instances. Unchanged raster content survives scene placement/
paint changes; new instances still upload. DPI, raster scale, subpixel phase,
atlas generation, invalid input and abandoned encoder ownership remain checked.
The existing shared intrinsic byte comparison is reused. Managed GlyphAtlas
already separates these identities; its algorithm and all fallback defaults are
unchanged. Both native providers build, all 19 CTests pass and each of five
execution modes passes 11 exact fresh-raster pixel comparisons plus the existing
managed/native differential. See ProGPU `docs/native-glyph-raster-retention.md`.

The post-fix run completed text editing/selection, controls and bindings,
toolbar, seven framework themes, separate popup surfaces, navigation, wheel,
clipped point/region queries, content replacement and Thumb capture/move/release.
It then presented 120 measured frames in 3,187.306 ms, reporting 497,321,536
managed allocated bytes (4,144,346.13/frame). This single Debug-app/Release-native
overlay observation is not a comparative performance claim.

**Open diagnostic contract:** `ProGpuWpfDiagnostics.TryGetPerformanceSnapshot`
uses the idle managed compositor's timings/draw/cache counters for native mode.
The memory snapshot likewise does not inventory the complete native engine.
The report's zero timings, zero draws and zero tracked GPU bytes must not be
accepted as native qualification. Add genuine typed native measurements before
the matched final Release Instruments, memory and package/platform acceptance.

At WPF `f1538b46c`, canonical WinForms and Windows managed CI pass; SDK staging
still fails on the old pinned ProGPU build, so downstream launch jobs skip.
LibreWinForms PR #29 remains green. No dependency pins, draft states or merges
were advanced by this diagnostic fix. ActivityMonitor remains excluded.

## Initial package snapshot

The isolated `artifacts/native-source-qualification.PmByoe/wpf` snapshot contains
WPF `1ee08c876`, ProGPU `960dfbfb` and LibreWinForms `f268f73c` source. Physical
dirty submodules and indexed dependency pins were not modified.

## Completed evidence

The fresh real source native host passed retained viewport/image updates,
native text/inline controls, document editing/undo/layout, geometry selection and
device recovery. It presented a native frame with 23 commands, 20 resources and
five submitted draws. This is macOS source-host evidence, not Windows/package
application admission.

WPF Build `34795305358` produced all three Windows managed payloads and passed
canonical WinForms source integration at `1ee08c876`. Exact artifacts:
`10329208818` (Windows runtime) and `10329698235` (canonical package closure).
Canonical packages identify `f268f73c`/`1ee08c87`, with their separately pinned
ProGPU `e56d45c5` graph; they are not the newer renderer package graph.

ProGPU Build `34793857889` at `4b6d9cbe` completed both full DX12 JIT/NativeAOT
consumer jobs, x64 and ARM64. Native package artifact `10328964352` and portable
package artifact `10329508340` carry `0.1.0-preview.3036.ci`. These precede the
private query-stack change, so final-head qualification is still required.

Using that exact portable package feed and the current WPF Windows payload,
`progpu-wpf-sdk-ci.sh --build-packages-only` completed all three WPF packages.
The native-mode Showcase package consumer built with zero warnings/errors.

## Actual application blockers

The package-only Showcase live probe reached a presented frame, geometry,
windowing and resize checks, then failed during TextBox interaction while
serializing `CaretElement` guidelines. Source Y `[0, double.MaxValue / 2]`
overflows the protocol's float coordinate. ProGPU's static-guideline fix retains
infinite anchors, zero remote-anchor displacement and NaN rejection, without
changing WPF adorner layout, source input or filtering visual types.

Both native providers and all 19 native tests pass; all 124 linked native
interop tests and generated contracts pass. A diagnostic rebuilt-library overlay
gets past that failure and reaches a separately surfaced popup target, where
native scene compilation returns `UnsupportedCommand`. The temporary diagnostic
bridge exception instrumentation was removed and its original package assembly
restored. Rebuilt ProGPU overlays remain identified as diagnostic artifacts,
not qualifying package output.

Next: isolate that popup operation, rebuild exact packages, finish actual
Showcase/Toolkit/SciChart and platform gates, then merge dependencies in order.
No PR draft state, pins or merge admission changed. ActivityMonitor is excluded;
general Direct2D/COM/Win2D expansion remains deferred.

### Popup input follow-up

ProGPU `27d13562` fixes the actual popup vector-rectangle/client-rectangle
intersection and preserves declared rectangular geometry masks on source
blur/shadow layers. No shadow allocation bounds, alpha-mask pixels or managed
input fallback are used. Both native providers compile; all 19 native CTest
suites pass, including 24 state/effect clip variants and canonical MIL effect
fixtures. Generated contracts and documentation checks pass.

The rebuilt diagnostic-overlay Showcase advances through text selection,
control mouse input, mouse bindings, discrete controls, toolbar, framework
themes, popup surfaces and keyboard navigation. Stage logs distinguish completed
predecessors from the current wheel/capture stage. The overall 180-second gate
still times out; a native process sample shows a pending GPU hit-query map wait.
This is not successful final-package or full application qualification.

At WPF `37042271d`, canonical WinForms and Windows managed production pass, but
SDK smoke fails while staging native dependencies: indexed ProGPU `e56d45c5`
requires Build `34763887821`, which failed. Keep this exact-commit check; after
final ProGPU qualification, update dependent pins and rerun the whole SDK lane.
Do not substitute unrelated artifacts or waive the failed dependency run.
LibreWinForms PR #29 remains green; ProGPU `27d13562` CI is queued/running.

### Transparent ScrollViewer source input

Per-attempt diagnostics identified the timeout as repeated unsuccessful wheel
targeting, not a proven stuck GPU submission. The actual ScrollViewer center
resolved to the Border behind it. Native query tracing confirmed successive
ordered point queries completed against 140 primitives/13 nodes; that temporary
native tracing was removed and both libraries rebuilt cleanly.

`ScrollViewer.HitTestCore` explicitly owns its full ActualWidth/ActualHeight
rectangle even with a null background. The source class now exports precisely
that policy through the existing `IPortablePointHitRegionSource` contract. Both
portable renderer modes already consume this typed point-only scope. Region
input remains drawing-based, and the ScrollContentPresenter retains descendant
clipping; no painted background or renderer-local type filter is added.

The source regression compares the metadata after 160/80-DIP arrangements with
the actual protected point-hit policy on a background-free ScrollViewer. Its
unattached fixture invokes that policy directly: public InputHitTest additionally
requires visible source admission, exercised separately by the live application.
The complete source host/retention/device-recovery script passes with this change.
The full source build has zero errors and two existing warnings; the final
harness-only build has zero warnings/errors.

With the rebuilt PresentationFramework diagnostic overlay, Showcase completes
wheel routing, real scroll movement, visible/clipped point queries, both bounds
queries, content replacement and Thumb capture/move/release. It then fails the
required performance gate with `Expected the requested Showcase performance
frame to be presented`. Thus input completion is concrete progress, not a passing
whole gate. Finish requested-frame scheduling/performance diagnostics, rebuild
the exact final package graph, then run platform/CI gates before ordered merges.

## Separate Windows diagnostic

Moving scalar query traversal state to invocation-private storage in addition
to the private array did not repair stock single-pass bounds execution:
the isolated VM process still exited `0xC0000005`. It was not applied to product
source. The baseline shader was restored and forcibly re-embedded in both
providers (restored source timestamp alone initially missed that rebuild).
Source SHA-256 remains
`1dbf5e9bab657323460d680e5417b6aa1f29b23656efca210520b1c4a23b3f4b`.
The superseded pre-fix Build `34794383097` was cancelled to release runners;
current-head and required package-producing runs were retained.
