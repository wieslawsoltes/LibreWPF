# Source-owned TextBlock point coverage

## Acceptance action

Continue the existing Showcase SelectorScrollViewer wheel/selection action and the
source-built native host's inline TextBlock. Source TextBlock point hit testing
accepts its arranged rectangle, including blank space; geometry-region selection
uses actual drawing. UIElement already retains source broad-phase bounds and real
IContentHost promotion. ScrollContentPresenter already supplies the offset and
viewport clip. No source-local renderer, fake visual or text composer is needed.
This trace is source-backed; no application was executed in this batch.

## Implementation

- Source TextBlock publishes `IPortablePointHitRegionSource`. No type probing or
  generic layout-rectangle fallback is added.
- Native MIL compilation collects sorted generated rectangle records. Incremental
  sessions compare owned bytes and replace the full native snapshot in one call
  when changed, including clearing it. Layout-only input changes need no painted
  MIL command. The native channel validates transactionally and retains ownership.
- ProGPU's native builder emits point-only coverage and region-only own drawing,
  restoring the normal query policy before children. It reuses actual transforms,
  clipping and shared GPU primitives. Empty text and overhanging drawing remain
  distinct. Managed retained replay carries matching logical scopes.
- Managed compositor source-command visuals no longer add generic Size input
  rectangles. Actual command owner IDs and typed descendant source capture replace
  that stand-in. This does not complete root cached/effect texture input coverage.
- Authored fixtures cover native/managed flags, empty content, overhang, clipping,
  child ownership, snapshot lifetime, malformed input and layout update/clear.
  The existing source-built host checks a blank-space point hit separately from
  a region query over the same space; it still uses the complete application tree.

Design, source provenance, complexity and shared-backend applicability are in
[ProGPU's contract](../external/ProGPU/docs/native-mil-query-participation.md#source-connection--2026-09-09).
This is ownership/query metadata, not a new compute-heavy CPU fallback. Existing
SIMD preprocessing and canonical GPU query algorithms remain shared. No measured
performance or runtime parity claim follows from compilation.

## Build-only checkpoint

ProGPU implementation commit: `7ad7d459`, pushed to the existing #139 branch.
The branch includes the freshly fetched `main` at
`102e39e5088b462624da6296ff70a43ed2c5d8b4`. Unrelated performance-artifact deletions
and four native source edits remain excluded from this commit and native builds.

A first managed ProGPU Release fixture build succeeded with zero warnings/errors.
The initial WPF fixture build
exposed use of an internal snapshot helper; it now uses the public picture recorder.
The subsequent fixture compile exposed an incorrect explicit rectangle namespace;
the public recorder's declared parameter now supplies its type.

Final compile-only results, with serialized .NET restore/build graphs:

- `ProGPU.Tests`, SDK 10 Release: success, zero warnings/errors, 68.40 seconds.
- `ProGPU.Wpf.Tests`, root SDK 11 preview Release: success, 20 existing xUnit
  analyzer warnings, zero errors, 12.06 seconds.
- `ProGPU.Wpf.RealPresentationFrameworkHarness`, root SDK 11 preview Release:
  success, four existing linked SfntFontFace style warnings, zero errors,
  91.24 seconds. This compiles the actual source TextBlock seam.
- Clean detached `artifacts/native-core-build.KvxVug/progpu` at `7ad7d459`:
  macOS ARM64 and x64 each completed all 303 compile/link steps, exit zero.
  Both native providers and configured test/sample targets compiled through
  direct `cmake --build ... --parallel 4`. The established Apple Clang C++20
  header configuration has modules off; the authored import-based fixture still
  requires its final module gate. These builds do not stage payloads or execute
  their linked test programs.

macOS staging still uses `e814ca9c`; Windows/Linux staging and the complete
23-package feed still use `da36a718`. They are not exact-head runtime evidence
for this new API. Final Windows/Linux/module/compiler/package gates remain open.

The generated native contract and MIL coverage ledger are refreshed. Tests,
verifiers, shader/GPU/app/VM execution, benchmarks and CI qualification are deferred
until feature freeze, as requested. Automatic CI is not disabled. Both PRs remain
unqualified. No final package consumption or default native-input admission is
claimed. Broader Direct2D/COM/Win2D work remains explicitly deferred.

## Remaining work

This source connection is implemented and compiled, not runtime-qualified. Continue
the same core application's remaining input dependencies. The Showcase's actual
`ShowcaseDropShadowEffectBorder` and `ShowcaseBlurEffectBorder` identify the next bounded
effect-input path: source own-content/descendant coverage must survive offscreen
composition without hittable effect padding. Root managed effect texture coverage
remains distinct from the source descendant connection in this batch. Required
cache/mask combinations remain open; do not substitute optional API expansion.
Final platform binaries/packages must be refreshed before execution:
older staged payloads do not expose the new point-region API. Close the remaining
application actions before switching to the fixed final qualification gates.
