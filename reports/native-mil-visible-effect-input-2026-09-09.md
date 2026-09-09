# Visible source effect input connection

## Acceptance action and blocking path

Application: the existing package MVP's `MvpBlurEffectBorder` and
`MvpDropShadowEffectBorder`, including their TextBlock descendants.
Action: point/region selection before and after child/content updates.
Source-backed blocker: managed `PrepareAndDrawEffect` indexed its padded source
and filtered textures with the root owner's ID, then captured only descendants.
The existing zero-opacity source traversal and native source-identity layer policy
did not connect that ordinary visible managed effect-root path.

## Implementation and paired applicability

ProGPU now captures typed source geometry before effect raster admission and
suspends hit-index writes for the subsequent preparation/composite, restoring the
flag in finally. Own content, point-only children, real clips and z-order come
from the pre-effect tree once. Source/shadow padding cannot become input and
empty ink can retain a source point descriptor. Unknown effect mappings and
unsupported cache/mask combinations fail through the existing typed policy.
Generic visuals, hit-testing-disabled rendering and offscreen-only preparation
remain unchanged; no WPF-local geometry or reflection was added.

C++ MIL already traverses source-owned drawing through identity-effect layers,
not the managed effect texture path. Its existing scene 9817 fixture now includes
the same point-only child, clipping, repeated capture, movement and content clear
as the managed actual-compositor fixture. The three variants preserve Gaussian
blur, zero-radius blur and DropShadow/source composition. LibreWPF adds a retained
sink fixture proving point/region geometry survives effect-bound normalization
once, with actual source owner IDs. The actual application is not reduced.

In-repository provenance is `GpuRenderCommandHitTestCache.SourceVisual.cs`,
`Compositor.ApplyAndDrawEffect` and native MIL's existing source-identity layer
compiler. [ProGPU's design record](../external/ProGPU/docs/native-mil-query-participation.md#managed-effect-composition-connection)
retains the existing cross-engine reference decisions. This changes typed
traversal/ownership, not raster algorithms, shader constants or CPU fallback
selection. Existing intrinsic transforms and GPU queries remain shared. No
performance or parity claim is made without final measurements.

## Build-only evidence

ProGPU implementation commit: `d5c98473`, following `7ad7d459` on the existing
#139 branch, which includes the last fetched main `102e39e5088b462624da6296ff70a43ed2c5d8b4`.
Unrelated native source edits and performance-artifact deletions were preserved.

- ProGPU Release fixture graph (SDK 10): success, zero warnings/errors,
  16.69 seconds.
- LibreWPF Release fixture graph (root SDK 11 preview): success, 116 existing
  compatibility/analyzer warnings, zero errors, 31.19 seconds. Warnings were not
  suppressed or counted as green CI.
- Clean detached native checkout `artifacts/native-core-build.KvxVug/progpu`
  at `d5c98473`: both macOS ARM64/x64 configured native build graphs succeeded.
  Each increment compiled and linked the extended MIL fixture in two steps;
  native product sources were unchanged from `7ad7d459`, whose complete graphs
  already compiled both providers. Header-mode Apple Clang C++20 configuration
  remains unchanged. No test executable was run and no artifact was restaged.

The source bridge implementation is unchanged in this batch; its new fixture
uses the actual retained effect/point sinks. Native product code already supplies
the paired source-identity policy, so no MIL decoder or generated ABI change is
required here. The shared reference algorithm was connected, not duplicated.

Tests, verifiers, applications, GPU/VM workloads, benchmarks and CI qualification
remain deferred until feature freeze. Automatic CI remains enabled. No payloads
or packages have been restaged; older artifacts cannot establish exact-head
qualification. Remaining application cache/mask coverage, final platform/package
production and all required PR gates are still open.
