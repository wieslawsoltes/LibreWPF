# Showcase scrolling: query-specific source coverage

## Acceptance path and source evidence

Application: existing `ProGPU.Wpf.ShowcaseApp`.
Action: expand the selector section, wheel over `SelectorScrollViewer`, then use
point and geometry selection in the same presented native scene.

`MainWindow.xaml.cs:TryRaiseLiveMouseWheel` queries the arranged viewport center
and requires the ScrollViewer or one of its source descendants. Its wrapping
TextBlocks implement source `HitTestCore(PointHitTestParameters)` using their
arranged rectangle, including blank space. They do not override geometry queries
with that rectangle. Source `UIElement.PromoteInputHit` already invokes the real
`IContentHost` to resolve inline content after a visual candidate is found.

The ordinary `ScrollContentPresenter` already places children using negative
scroll offsets and publishes a viewport layout clip. `UIElement` installs that
effective clip as `VisualClip`; the native compiler already serializes the clip
and visual offset. No additional ScrollableAreaClip snapping subsystem is justified
by this action. The concrete missing connection is custom source point coverage,
not an absent generic scroll offset or a missing content-host promotion step.

These are source-backed observations, not reproduced runtime results.

## Implemented prerequisite

ProGPU commit `f41bb3da` adds `PointOnly` and `RegionOnly` primitive flags, admission
checks and one shared GPU query filter for managed WebGPU and both C++ providers.
Neither flag preserves existing behavior; conflicting/unknown flags fail closed.
The existing 128-byte record, geometry, clip and ownership contracts are unchanged.
Flags participate in retained index identity. There is no new CPU fallback,
numeric loop, native crossing or performance claim. Documentation and agents rules
record the source dependency, paired applicability and remaining connection.

The ProGPU branch includes fetched `main` at
`102e39e5088b462624da6296ff70a43ed2c5d8b4`. Its existing PR is #139; LibreWPF's
integration PR is #115. Unrelated performance-artifact deletions and four native
source edits were preserved and excluded from staging/native compilation.

## Build-only evidence

- ProGPU managed Release fixture graph, SDK 10: success, zero warnings/errors,
  36.83 seconds.
- Native package-consumer project-reference Release build, root SDK 11 preview:
  success, zero warnings/errors, 7.55 seconds. The first invocation used the wrong
  project-reference property and failed restore with NU1010; the corrected command
  used `-p:ProGpuNativeUseProjectReference=true`. This is not package consumption.
- Clean detached ProGPU checkout at `artifacts/native-core-build.KvxVug/progpu`
  at `f41bb3da`: macOS ARM64 and x64 each completed all 304 compile/link steps,
  exit code zero. Both configured native providers and the configured test/sample
  targets compiled. The build regenerated the embedded shared hit-query shader;
  embedding is not runtime shader validation. These builds use the established
  Apple Clang C++20 header configuration with modules off; final module/compiler
  matrices remain required.

Managed GPU tests, native scene validation/hash fixtures, native package-consumer
queries and Dawn provider queries were authored. No tests, shader validators,
applications, GPU workloads, VM graphics, benchmarks or CI checks were executed.
The separately configured Dawn runtime-provider fixture is not compiled by the
two current native CMake graphs. Final qualification remains mandatory.

Direct CMake builds do not restage payloads. The existing macOS staging remains
at `e814ca9c`; Windows/Linux staging and the complete 23-package feed remain at
`da36a718`. They must be refreshed before using this contract. No package parity,
application input completion, default admission or merge readiness is claimed.

## Next bounded implementation

Continue this same source TextBlock dependency: publish an authoritative typed
point rectangle, carry it in batched native MIL metadata and managed retained
snapshots, and emit point-only source coverage with region-only drawing coverage.
Preserve empty text, glyph overhang, source bounds pruning, transforms, clips,
child order, content-host promotion and layout invalidation. Add paired fixtures
to the existing source-built harness before admitting this application action.
Do not broaden this into generic arranged-size hit substitution or a new renderer.
