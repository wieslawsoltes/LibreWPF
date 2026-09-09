# Cached source input: managed connection and native dependency

## Acceptance action

The core application sequence requires cached content to update after its first
frame and retain correct selection after movement. The existing retained
`PushVisualCache` path and `RetainedSinkPropagatesSourceOwnerHitTestIdToCacheScopes`
fixture identify the concrete source connection: typed cached visuals retained
owner IDs, but their source hit traversal rejected caching and compositor replay
could index the cache texture rectangle instead. This is a source-backed finding;
the package MVP and third-party applications have not been run for this change.

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
