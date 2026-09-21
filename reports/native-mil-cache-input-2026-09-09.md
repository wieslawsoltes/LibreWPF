# Cached source input: managed connection and native dependency

## Acceptance action

The core application sequence requires cached content to update after its first
frame and retain correct selection after movement. The existing retained
`PushVisualCache` path and `RetainedSinkPropagatesSourceOwnerHitTestIdToCacheScopes`
fixture identify the concrete source connection: typed cached visuals retained
owner IDs, but their source hit traversal rejected caching and compositor replay
could index the cache texture rectangle instead. This is a source-backed finding;
the package Showcase and third-party applications have not been run for this change.

## Implementation

ProGPU `b08e007e` connects optional `CacheAsLayer` visuals to the shared
pre-composite source capture. Original source transforms/clips/commands own input,
independently of cache scaling, pixel snapping and raster suppression at scale
zero. Raster preparation and final composition suspend hit writes with finally
restoration. Existing effect capture shares the helper; no bridge-local geometry
algorithm, texture readback or CPU fallback was introduced.

Regression fixtures are authored for scales 0/1/2, unchanged raster reuse with
index rebuilding, fractional movement, content replacement/clear, clip bounds,
sibling state restoration and cached Blur/DropShadow roots with point-only
children. LibreWPF's actual retained cache-sink fixture now captures the index
and checks drawing bounds (5,6)-(15,17), not the 70-by-80 cache bounds.

The paired native work remains required, not non-applicable or complete:
`add_visual_cache_layer` records content in a cache-local frame, optionally snaps
the final composite, and skips content at scale zero. The next implementation
must retain an unsnapped source frame through the existing builder input capture,
including clip-frame ownership and input-only zero-scale behavior. Indexed native
caches remain rejected until connected. Required cached-picture refresh sources,
spatial masks and undeclared effect mappings remain explicit as well.

See [ProGPU's contract and design record](../external/ProGPU/docs/native-mil-cache-input.md)
for provenance, cross-engine references, complexity and native requirements. This
is one staged implementation batch in the linked existing PRs, not approval to
merge one-sided cache parity or declare core application closure.

## Build-only evidence

- ProGPU Release fixture graph, SDK 10: success, 0 warnings, 0 errors, 44.06 s.
- LibreWPF Release fixture graph, root SDK 11 preview: success, 116 existing
  compatibility/analyzer warnings, 0 errors, 26.66 s. No warnings were suppressed.
- Fetched ProGPU main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an
  ancestor of the working branch. Existing unrelated native edits and performance
  artifact deletions were preserved and excluded from the commit.

Native product/fixtures were not changed or built in this managed connection.
No native artifacts or packages were restaged. Tests, verifiers, applications,
GPU/VM workloads, benchmarks and CI qualification remain deferred until feature
freeze; automatic CI remains enabled. Final exact-head platform packages, paired
native fixtures and all existing SDK/PR gates remain outstanding.

## Native positive-scale implementation follow-up

ProGPU `278ef660` connects positive-scale native cache input through copied
`source_local_cache` builder metadata. MIL records its raster-to-parent input
transform before cache pixel snapping; neither the raster stream nor cache
content/composite revisions are replaced. The producer maps actual command states,
point/image scopes, glyph bounds and clip controls to source coordinates. Nested
frames and source clips restore independently; frame-qualified clip caches cannot
reuse another cached layer's transformed segments. `4251cac8` keeps the frame
table lazy so ordinary noncached indices acquire no extra table allocation.

Independent affine rows use NEON/SSE2 with the existing multiply/add ordering,
and corners/clip controls reuse the existing intrinsic transform. The scalar
composition oracle appears only in fixtures. There is no new CPU fallback,
source-local geometry implementation, query-time scene traversal or pixel readback.

Paired canonical native fixtures now cover scales 1/2, fractional placement,
movement, source clipping and content replacement/clear; cached Blur/DropShadow
variants retain point-only children. The nested builder fixture covers source
frame composition, frame-qualified rectangle clip reuse, sibling restoration,
invalid mapping rejection and reset. Managed nested source traversal and the
import-based consumer receive matched authored coverage.

The earlier blanket native rejection is superseded for this positive-scale
branch only. Zero-scale native caches still need input-only command retention;
cache-boundary source masks and non-axis rectangle clipping within a cached frame
remain explicit failures, not silent empty results. Full cache parity, actual
application interaction and feature freeze remain incomplete.

Build-only results for `278ef660`:

- Clean detached native checkout: macOS ARM64 and x64 both completed all 41
  configured incremental compile/link steps, including MIL, builder, native
  wgpu/Dawn providers, and configured test/sample binaries.
- Managed ProGPU Release fixture graph (SDK 10): success, zero warnings/errors,
  49.75 seconds. No fixture body executed.
- Native headers use the existing Apple Clang C++20 configuration. Modules remain
  off in these local graphs; import fixtures and full compiler/platform matrix
  still require final qualification. No native payload/package was restaged.
- The MIL coverage ledger was regenerated through the existing generator.
  Contract verifiers, runtime tests and PR CI qualification remain deferred.

Final `4251cac8` builds succeeded for both macOS ARM64 and x64: each completed
16 incremental compile/link steps, including both provider libraries and configured
fixtures/samples. No executable was run and no payload was restaged. Unrelated
native worktree changes and performance artifact deletions remain untouched.

## Zero-scale source connection

ProGPU `b9f08004` closes the zero-scale branch through a balanced input-only
builder scope. Native MIL retains the original source coordinate frame and
uses its existing command traversal, including effect layers and descendants.
The hit producer reads those retained commands before serialization. Stream
measurement and writing exclude the same complete command ranges; nested ranges
are not counted twice. Visible siblings retain their own IDs and command payloads.
Restoring positive scale resumes the normal cache path from current source data.

The resource table remains owned and index-stable, including CPU snapshots shared
with visible commands. This does not promise zero CPU storage/validation cost.
The excluded draw/effect/cache commands are absent from the command-driven raster
pipeline, without a tiny cache texture, second MIL decoder, duplicate source
composer or CPU pixel fallback. Scope filtering is dependent metadata work;
the existing intrinsic geometry mapping and canonical GPU queries are unchanged.

Authored native/managed fixtures now include scale zero, source movement and
content replacement/clear followed by scale-one restoration. Native zero-scale
fixtures require only the visible sibling's draw, and zero-scale effect-root
fixtures require no serialized commands for that whole root while retaining its
source input. Builder fixtures cover nested suppression, visible sibling input,
stream-size agreement, raster stack-depth metrics, reset and unbalanced-scope
rejection. The module consumer exercises point-only input inside an input-only
scope. These are authored fixtures, not executed results.

Build-only evidence at `b9f08004`:

- Clean detached macOS ARM64 and x64 native graphs each completed 41 incremental
  compile/link steps, including both wgpu-native/Dawn providers and configured
  fixture/sample binaries. No executable ran and no payload was restaged.
- Managed ProGPU Release fixture graph, SDK 10: success, 0 warnings, 0 errors,
  46.44 seconds. WPF bridge product code is unchanged; no WPF-local input adapter
  or new C ABI declaration is needed for this native builder connection.
- MIL coverage metadata was regenerated with the existing generator. Freshly
  fetched ProGPU main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`, included
  by the feature branch. Unrelated native edits/deletions remain untouched.

Native boundary masks, required cached-picture sources and remaining exact clip
combinations stay explicit. Full application/parity qualification, module/compiler
matrix, exact-head payload/package production, GPU/SIMD/lifetime/performance
checks and both PRs' required CI remain open. No tests, verifiers, application/VM
workloads or CI qualification were run before feature freeze; automatic CI remains
enabled.
