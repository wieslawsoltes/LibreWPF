# Native MIL drawing-mask source input — 2026-09-09

## Acceptance path and source evidence

Application: the existing source-built
`src/ProGPU.Wpf.RealPresentationFrameworkHarness/Program.cs` DrawingVisual path.
Action: preserve geometric point/region selection of content recorded inside
PushOpacityMask, including after content replacement. The full MVP XAML/code
search did not identify an explicit OpacityMask; this checkpoint does not invent
one or claim a reproduced MVP failure.

Blocking paths: native MIL `append_render_stream` emitted an unannotated mask
layer rejected by complete native input indexing; managed source-only command
capture rejected Push/PopOpacityMask even though its direct command encoder
already retained the enclosed geometric input. Source WPF's point and geometry
drawing-context walkers explicitly implement the mask as an input no-op with a
balanced stack placeholder. Actual source clips remain distinct.

## Implementation

ProGPU commit `a7619f7c` connects both paths. C++ uses the explicit source alpha-mask
layer annotation, preserving all raster resources/commands and the existing
geometric clip state. Managed typed source traversal checks balanced local mask
scopes without inspecting mask brushes/bounds. The follow-up `a3c30233` authors
negative coverage for attempts to reclassify a source geometry clip as an alpha
mask. Generic unannotated layers still reject complete index publication.

Paired regressions cover nested transparent/solid masks, actual rectangle clips,
smaller mask bounds, following unmasked content, replacement and clearing. Native
coverage includes gradients and unchanged raster layers. Managed malformed scope
coverage verifies failure without partial index publication. A module consumer
references the public C++ enum. No wire/shader changes or new scalar kernel.

Source WPF/bridge production code is unchanged: its existing typed render-data
seam already transports these commands. No source-local opacity-mask renderer,
reflection adapter, pixel readback or bounds-only input fallback was added.

See the paired [ProGPU design and research record](../external/ProGPU/docs/native-mil-drawing-mask-input.md).

## Build evidence and qualification boundaries

- The first managed fixture compilation found two int/uint assertion mismatches;
  these were corrected. SDK 10.0.201 `dotnet build src/ProGPU.Tests/ProGPU.Tests.csproj
  -c Release --nologo -v quiet` then succeeded: 0 warnings, 0 errors, 10.30 seconds.
- Clean immutable native checkout `a7619f7c`: complete configured macOS ARM64 and
  x64 CMake builds each completed all 41 incremental compile/link steps, including
  wgpu-native and Dawn libraries plus native MIL/test/sample targets.
- Final fixture commit `a3c30233` then rebuilt both immutable native graphs:
  2 compile/link steps per architecture, both successful. Managed production/test
  sources are unchanged from the successful managed build above.
- These are header-mode AppleClang builds. C++20 module, Windows/Linux, exact-head
  package and full runtime qualification remain mandatory in the final phase.
- MIL coverage ledger regeneration was performed after the implementation edit.
  Test execution, source verifiers, application/VM/GPU workloads, image comparisons,
  performance/lifetime measurements and CI qualification were not run. Automatic
  CI remains enabled. No new native artifacts were staged or packages refreshed.
- Unrelated native scene edits and performance-artifact deletions in the ProGPU
  worktree were preserved and excluded from commits and clean native builds.
- ProGPU main was fetched and remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`,
  included by the feature branch. Both implementation commits are pushed to
  [ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139); this report and
  the updated submodule pointer belong to
  [LibreWPF PR #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).

## Remaining core queue

This closes the identified drawing-scope admission branch in code, not complete
application selection or runtime parity. Visual-level opacity masks combined with
caches/effects, remaining exact clip combinations and required cached-picture
sources remain explicit unsupported contracts. Before treating another such
branch as a core blocker, identify its actual acceptance application/action.
Continue the existing full application integration queue, not generic mask/API
expansion; feature freeze and all final qualification gates remain pending.
