# Native MIL source opacity/input connection — 2026-09-09

## Application dependency

Acceptance remains the package MVP and Toolkit/AvalonDock interaction path.
The MVP's template/animation cases change visual opacity, and source visual-state
command replay pushes that value through the product sink. Native MIL previously
rejected all layers when emitting its optional input index, including ordinary
drawing opacity and unmasked visual opacity isolation. The source WPF point and
region drawing walkers retain geometry input through zero/fractional opacity;
render alpha is not input visibility. This is a source-backed blocker, not a
runtime reproduction or qualification result.

## Connected implementation

ProGPU `62d89553` adds explicit source-opacity layer metadata and a source-geometry
state-opacity policy to the reusable C++ builder. MIL selects them only for its
requested input index. Ordinary/animated drawing opacity and unmasked visual
opacity groups keep their real render alpha and bounded isolation; input walks
the enclosed geometry under the actual source owner, transform and clip. Bounds
used to size an opacity intermediate do not become hit geometry or replace clips.
Effects, spatial masks, non-SrcOver blends and cache layers remain separate,
explicit unsupported input contracts. Generic native default filtering is intact.

The paired managed `PushOpacity(opacity, affectsHitTesting: false)` carries source
policy through compact scalar-state and general retained snapshots. The hit
cache saves/restores its input opacity independently from rendering opacity.
LibreWPF's product composition sink selects this policy; other ProGPU callers
keep their prior behavior. No shader, C ABI/stream layout, raster alpha, input
owner map or native host selection was changed. C++ static SDK clients rebuild
for the optional typed parameters; header and module exports share the API.

Native fixtures cover zero/fractional nested groups, saved-state clips/transforms,
owner changes, preserved stream alpha, reset, generic policy and blend/layer
rejection. Canonical fixtures cover regular/animated opacity plus zero-opacity
visual parents, with and without typed isolation bounds. Managed fixtures cover
snapshot variants, actual clips, zero opacity, generic policy and product WPF
command replay. The source MIL byte-capture fixture now preserves zero, fractional
and full alpha. All are authored for final execution, not claimed passing tests.

The [ProGPU design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md)
records original in-repository provenance and primary engine references. Existing
SIMD placement and canonical GPU queries are reused. New work is dependent stack/
owner metadata: O(C) traversal, O(L) sparse native annotations for L source layers,
bounded depth, geometric growth and capacity reuse. No CPU pixels, readback,
per-item submission or measured speed claim is introduced.

## Remaining application blocker

Managed retained visual traversal independently rejects opacity-zero nodes before
command replay. This does not follow from the now-connected command policy and
must be closed through source-owned retained input semantics, not a bounds-only
subtree fallback. Native host queries still use the managed index; do not enable
partial native coverage or mark the core application/input batch complete.
Masks/effects/cache coverage, native host query routing, final platform package
refresh, runtime/image/lifetime/performance qualification and exact-head PR CI
remain open. Broader Direct2D/COM/Win2D expansion stays deferred.

## Build-only evidence

The native clean build checkout was advanced to `62d89553`, excluding the four
unrelated modified native files and deleted performance artifacts in the working
submodule. A fresh fetch confirms ProGPU main remains
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, already an ancestor. The required MIL
coverage source digest was regenerated; verifier execution remains deferred.

Both macOS ARM64 and x64 builds at `62d89553` completed all 75 compile/link steps
each with exit 0 and staged both native providers and six SDK archives per RID.
Modules were OFF; test/sample targets compiled but did not execute. The module
consumer remains for the final module-enabled lane. ProGPU `7e7c84e3` only corrects
the documented names of the remaining retained opacity rejection methods.
The ProGPU managed test graph at `7e7c84e3` compiled with 0 warnings and 0 errors
in 79.37s. The earlier WPF build handle became unavailable before its terminal
result could be recovered; no success is inferred from that handle. A fresh
serialized Release build of `ProGPU.Wpf.Tests` completed with 0 warnings and
0 errors in 3.47s. This was incremental compilation, not test execution.
Only the explicit native
`--build-only` entry and serialized managed builds are used before freeze.
No tests, applications, graphics/VM comparisons, benchmarks or CI polling run in
this phase. Existing Windows/Linux staged payloads and the complete package feed
retain their earlier `da36a718` provenance until the final refresh.
