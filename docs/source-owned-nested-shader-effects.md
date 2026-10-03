# Owned nested source ShaderEffect recordings

This source layer is a child of LibreWPF #250 at
`d107503dbd33ab873b9beb66888c39379095be7a`. It requires the coordinated owned
recording producer [ProGPU #352](https://github.com/wieslawsoltes/ProGPU/pull/352)
at `87ccd171a0ca1e28677677e2bc3b2f2b63fb777a` and a rebuilt source graph.
Qualified ProGPU and LibreWinForms gitlinks are unchanged. Older pinned binaries
are not claimed to implement these additive APIs.

## Ordered source capture

Actual bounded source ShaderEffects reached while recording VisualBrush,
ImageBrush/DrawingImage or BitmapCacheBrush content use a purpose-specific owned
effect scope. The scope records unre-based child commands, snapshots the original
double source capture and shader parameters, then emits one
`DrawingContext.DrawOwnedShaderEffect` at its original command position.
It does not append a retained child after the surrounding commands. Parent
transform and geometry clips remain outside the effect; source visual opacity
and opacity mask remain inside its implicit input. Unrelated legacy BitmapEffect
and ordinary scalar effect APIs retain their existing admission.

The original effect owner remains an identity, not a mutable late-render callback.
Source samplers snapshot the complete owned graph during recording. A separate
discarded ownership walk visits hidden dependencies under the existing recursion
guard before visible content is retained. No source visibility is changed.
Nested ordinary BitmapCacheBrush paints retain a captured picture generation;
ordinary consumers outside owned shader recordings keep their existing live
cache-source invalidation behavior.

## Actual late target

The producer calls `IShaderEffectPreparation.Prepare` only after the actual
offscreen target exists. That immutable context supplies its compositor, device
identity, projection, normalized viewport, semantic DPI and shared capture frame.
The source never forwards its outer window frame into a nested capture and adds
no local pixel, UV, output-coverage or projection algorithm.

ImageBrush and VisualBrush samplers paint the recorded content into the shared
frame's complete physical extent, using the existing physical-size DPI-1 brush
realization. A recursively nested effect receives that real target through the
producer again. BitmapCacheBrush samplers instead use their retained actual
primary-display/device policy and the shared raw-cache frame producer; brush
opacity and transforms are not reapplied to raw cache pixels.

## Immutable resources and failures

Source bitmap samplers take an actual GPU copy of the exact selected texture
before publication, retaining raw format, alpha mode and owning context. This
requires a live, single-sample 2D texture with the existing CopySrc capability.
An arbitrary mutable texture lease is not an immutable pixel generation; there
is no CPU readback, guessed provider identity or fallback for an uncopyable
texture. Original typed ImageBrush DIP/texel metadata is snapshotted separately
and is not queried again during late realization.

Each preparation returns a fresh owned parameter generation. The producer clones
sampler leases, and temporary owners retire before publication. Reusing the
source recording for another target does not mutate a previously published
parameter or sampler texture. Discarding an incomplete scope releases its owned
child commands and source recipe. Failed recipe/texture candidates attempt all
owned cleanup while preserving the original error. Shutdown/finalizer behavior
comes from the existing shared owned-resource retirement contract.

The existing source family, missing-bound, source-cycle, device, 4096 sampler
dimension and unsupported content gates remain explicit. DrawingBrush samplers,
live video/external/3D sources and arbitrary shader replacement policies are not
admitted by this connection. The deferred recording route rejects video before
its frame callback; an ordinary live video lease does not prove immutable bytes.
Ordinary cache/video paths outside the typed recording route remain unchanged.

## Validation boundary

Source controls are authored separately from the producer's owned texture,
ordered recording and two-target controls. They are not execution evidence.
No builds, tests, syntax checks, verifiers, probes, GPU/UI/VM execution or CI
dispatch were performed. Final combined producer/source validation, actual
original-Windows rendering and platform application qualification remain pending.
