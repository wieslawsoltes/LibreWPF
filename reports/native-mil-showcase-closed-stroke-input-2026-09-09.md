# Showcase closed stroked path input — 2026-09-09

## Acceptance dependency and implementation

The package Showcase's `ShowcaseShapePath` has the actual contour
`M 0,40 L 24,0 L 48,40 Z`, width 2 and miter joins, placed at 178/20.
Canonical MIL already lowers its pen to a closed polyline stroke batch; native
hit capture previously rejected that family. This was a source-backed blocker,
not an interaction reproduced at runtime.

ProGPU `fb0f3da4` connects closed solid normal-width polyline batches. It reuses
the existing flat LineStroke encoding and the native renderer's join triangles,
including the closing seam, WPF clipped-miter policy, bevel/round joins and the
same local-affine/world-conformal coordinate domains. All pieces retain one
source owner and exact state clips; canonical queries deduplicate owner IDs.
Triangle boundaries, not their envelopes or antialias padding, define input.
Rendering, shaders and public ABI/module interfaces are unchanged. There is no
WPF-local stroker, new GPU submission or pixel readback.

The closed form does not apply endpoint caps. Open/dashed/spline/device-width
batches, tiny/degenerate edges and remaining geometry families fail explicitly;
an unsupported later draw prevents publication of the whole index. This does
not enable incomplete native input in the host. Native point/all-owner/region
routing, geometry clips and effect/cache coverage remain core work.

Native scene 9814 has authored fixtures for all three joins, clipping,
anisotropic placement, caps, source identity and rejection after a supported
batch. Scene 9815 uses canonical MIL for the actual Showcase fill-plus-stroke path.
The managed fixture pairs the same contour and join counts with an independent
apex-miter oracle. These fixtures have not been executed.

Provenance, paired applicability, primary research and O(E) preparation/storage
costs are recorded in [ProGPU's design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md).
Existing intrinsic line metrics and corner placement are reused. The native
join helper's existing bounded scalar topology/math remains unchanged; this
batch does not claim full SIMD qualification or measured speed improvements.
`progpu_native_mil.cpp` itself is unchanged, so its ledger digest is unchanged.

## Build-only evidence

The ProGPU Release test graph compiled with **0 warnings, 0 errors, 33.83s**.
No tests ran. A fresh main fetch remains at
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of the feature branch.

The clean isolated native checkout at `fb0f3da4` excludes unrelated modified
native files and deleted performance artifacts from the primary submodule.
Both macOS ARM64 and x64 builds completed all 53 compile/link steps with exit 0,
including test/sample compilation, and staged unqualified native package payloads.
ProGPU `fb0f3da4` is pushed to PR #139.
Only the explicit `--build-only` mode was used; modules were OFF.

The WPF Release graph also compiled against this ProGPU commit with 116 warnings,
0 errors in 21.64s. The warnings are recorded, not suppressed or counted as green
CI. No WPF tests ran.

No applications, test executables, verifiers, VM graphics, benchmarks or CI
checks ran. Windows/Linux payloads and the complete development package feed
remain at the earlier `da36a718` checkpoint, not this commit. Final exact-head
platform/module/package/application/CI qualification remains mandatory.

## Next core action

The next concrete input blocker is the same Showcase's `ShowcaseBlurEffectBorder` and
`ShowcaseDropShadowEffectBorder` in the EventTrigger tab. Their source `Visual` input
path consumes `EffectMapping.Inverse`; native capture currently rejects their
PUSH_LAYER commands because only source-opacity layers are annotated. Trace the
built-in effects' actual mapping and preserve source geometry through native
effect composition before enabling the native index. Do not indiscriminately
accept custom effects, masks or cache layers. Exact geometry clips and native
host-query routing remain subsequent dependencies.
Do not begin general Direct2D/COM/Win2D expansion or
optional stroke-family refinements solely because those APIs remain incomplete.
