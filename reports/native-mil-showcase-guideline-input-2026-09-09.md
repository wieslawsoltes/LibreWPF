# Showcase guideline/input connection — 2026-09-09

## Acceptance action and bounded blocker

Continue the existing Showcase's text/Hyperlink underline and control-chrome input.
The source point/geometry drawing-context walkers keep guideline scopes balanced
without changing input coordinates. Native MIL's index producer rejected guideline
state despite original geometry remaining available; ordinary managed recorder
primitives could instead expose coordinates already changed by snapping. This
is source-backed implementation evidence, not a reproduced runtime failure.

## Implemented

ProGPU `06f8d541` admits guideline state only in explicit native source-geometry
capture, preserving original geometry/transforms and raster guideline resources.
Logical image input retains the same policy. Generic rendered-visibility capture
continues rejecting guideline state instead of pretending to compute snapped hits.

The WPF ordinary primitive sink publishes ProGPU source-geometry metadata for
snapped lines, rectangles, rounded rectangles and ellipses. Nonflat line caps
also preserve the original spine before raster cap extension; auxiliary cap draws
do not become duplicate source input. Typed native primitive ingress is already
unsnapped and stays unchanged. The shared ProGPU hit builder owns all geometry
lowering. No WPF-local hit algorithm, reflection or per-item native call was added.

Compact retained picture snapshots/clones retain optional indexed metadata.
Invalid annotations cannot suppress scope commands or publish a partial index.
Raster bounds explicitly ignore source overrides. External SKPicture archives
reject this currently unencoded metadata, rather than losing it on serialization.
Existing source image scopes remain authoritative over their internal commands.

Authored paired fixtures cover native static/explicit-offset resources, native
canonical MIL Y1/Y2 commands, transformed source bounds, image scopes, retained
managed snapshots/raster bounds, each ordinary line cap and invalid annotations.
Detailed provenance, complexity, applicability and remaining gaps are in
[ProGPU's design record](../external/ProGPU/docs/native-mil-guideline-input.md).

## Compilation checkpoint

- Fresh ProGPU main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an
  ancestor of the feature branch. ProGPU `06f8d541` is pushed to PR #139.
- Final ProGPU Release test-project build: zero warnings/errors, 32.83 seconds.
  The earlier post-bounds-change build also succeeded (8.96 seconds).
- WPF Release graph: final incremental build zero errors, 20 existing xUnit
  analyzer warnings, 8.58 seconds. Earlier attempts exposed shim equality-operator
  and test Rect-namespace compilation errors; both were corrected. Warnings
  were not suppressed, and incremental warning counts are not a clean-CI claim.
- Clean isolated C++ builds at `06f8d541` completed 53 ARM64 and 17 x64
  compile/link steps, including both providers and MIL fixtures. These were
  `cmake --build ... --parallel 4` invocations only, not test execution.
  Modules remain OFF in these macOS build trees; module qualification is pending.

The clean native checkout excludes the unrelated four modified native files and
deleted performance artifacts in the working submodule. Direct CMake compilation
does not restage package inputs: staged macOS payloads retain their earlier
`e814ca9c` provenance; Windows/Linux payloads and the complete development package
feed remain at `da36a718`. Do not relabel those packages as current-head output.

No tests, apps, GPU/VM workloads, verifiers, benchmarks or CI checks executed.
Final package/platform/module, renderer/headless, Svg.Skia exact-difference,
application/Windows comparison, lifetime/performance and both PR CI gates remain.

## Remaining core work

Native host owner queries are still disabled. Finish required exact geometry
clip/spatial-mask/cache coverage and the async-native/synchronous-source query
connection. Cached-brush snapping and ScrollableAreaClip's source frame require
their own concrete application trace; this change does not admit them implicitly.
Then close the remaining acceptance actions and freeze for final qualification.
This is not full guideline/input parity, feature freeze or merge readiness.
