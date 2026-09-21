# Retained source input under opacity culling — 2026-09-09

## Acceptance and bounded outcome

The package Showcase opacity animations and Toolkit/AvalonDock pointer/selection path
require source geometry to remain hittable through zero visual opacity. Source
inspection found separate early returns in ProGPU's `CompileVisualTreeCore` and
`AddVisualHitTestBoundsSubtree`, before the already connected command policy.
This batch connects those returns for typed source visuals, without rendering
invisible content or using visual Size as substitute input geometry.

The previous command-opacity checkpoint was committed/pushed as LibreWPF
`1ae73cc35`, consuming ProGPU `7e7c84e3`. Its fresh WPF incremental Release build
completed with 0 warnings/errors in 3.47s after the old process handle was missing.

## Implementation and applicability

ProGPU `78cc0130` owns `ISourceGeometryHitTestCommands` and the shared builder's hit-only
visual/picture traversal. LibreWPF's retained visual exposes its existing context
through the typed interface; no reflection, WPF-local hit algorithm, OnRender,
second recording or raster target is added. The enabled compositor captures this
geometry when source opacity culls rendering. Ordinary untyped visuals retain
their old opacity policy; disabled and suspended hit-index construction stays off.

The traversal preserves visible child order, command owner IDs, source and picture
transforms, explicit opacity-command policy, and local/outer/composite clips.
Axis-preserving rectangles use the existing bounds clip; rotated rectangles use
real four-edge paths. Geometry-clip failure cannot become bounds containment.
Logical images retain destination coverage independently of internal render
commands, including unowned synthetic scopes and enclosing image scopes.
Embedded visuals join existing compositor version tracking through a cached
observer, so their mutation invalidates input even without a parent-tree link.

Unsupported effects, cache layers, masks, draw commands and missing glyph ink
bounds remain explicit. A failed capture faults index publication until Clear;
the accumulated prefix is not a valid partial index. Command scopes cannot pop
an enclosing visual clip or remain open. A shared 256-level recursion limit
bounds malformed visual/picture graphs. No generic primitive encoder or query
shader is replaced, and its other coverage limits remain relevant.

Native MIL already retains zero-alpha source geometry via source-geometry state
and source-opacity layer policy; its canonical scene 9811 and builder 9810
fixtures are the paired native evidence to execute at qualification. There is no
corresponding C++ managed-visual early return to modify in this batch. Native
source, C ABI, shader, coverage digest and staged binaries are unchanged.

Original code provenance, primary engine references, decisions and costs are
recorded in [ProGPU's design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md).
The tree/scope scheduling is dependency-bound. Existing SIMD geometry placement
and GPU query policies are retained. The existing embedded-version identity scan
is worst-case quadratic in distinct embedded visuals; no speed claim is made.

## Authored coverage and build-only evidence

`SourceVisualHitTestTests` covers real geometry/owners, nested clips, pictures,
logical images, visibility, empty updates, malformed scopes, unsupported capture,
reset and bounded cycles. Compositor fixtures cover zero raster vertices/OnRender
calls, generic/disabled behavior and embedded-child invalidation. LibreWPF has a
product retained-visual fixture asserting that the actual context supplies input.
Tests have not run. Source review corrected a clip-count expectation: an exact
axis-aligned clip uses clamped primitive bounds, not path-segment payloads.

Intermediate ProGPU graphs compiled with 0 warnings/errors (11.31s and 31.09s);
the WPF graph compiled with 116 warnings and 0 errors in 29.25s. A later compile
found use of Peek on the hit builder's narrower stack API. The final implementation
instead requests strict clip admission inside the shared encoder, which also
prevents an inherited path from hiding a failed inner clip. The final ProGPU
Release graph compiled with 0 warnings and 0 errors in 11.84s. The final WPF
Release graph compiled with 116 warnings and 0 errors in 28.97s against ProGPU
`78cc0130`. The warnings are reported, not suppressed or treated as green CI.

A fresh fetch leaves ProGPU main at
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, already an ancestor of the working
feature branch. Only this batch's seven ProGPU paths were staged; unrelated
native edits and deleted performance artifacts were preserved outside the commit.

All builds/restores remain serialized. No tests, verifiers, apps, VM graphics,
benchmarks or CI polling ran. Existing staged macOS native inputs remain
`62d89553`; Windows/Linux payloads and the complete 23-package feed remain
`da36a718`. These builds are not exact-head package or runtime qualification.

## Next core work

Continue the application's remaining native hit coverage (stroke/clip/cache/
effect families) and native host query routing. This connection does not enable
the optional native index in WPF hosts or finish application closure. Complete
required application branches, then feature-freeze, refresh all final platform
packages and run the full preserved qualification/CI gates. Broader Direct2D/
COM/Win2D expansion remains deferred, not completed.
