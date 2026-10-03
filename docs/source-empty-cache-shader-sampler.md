# Owned raw BitmapCacheBrush shader samplers

This source correction follows #245 and preserves the held empty-source ancestry.
It replaces the earlier full-receiving-frame brush-paint assumption with the
dedicated original BitmapCacheBrush sampler contract: sample the actual selected
cache texture directly in normalized coordinates. Brush opacity and relative or
absolute transforms do not paint this shader input. Ordinary cache painting keeps
its existing mapping, source-hit and empty-source behavior.

The producer dependencies are ProGPU #345 (native raw cache raster) and #346
(shared Scene owned raster, lifetime and nested ownership witness). These APIs are
not present in the currently qualified source pin. A coordinated qualified producer
update and source rebuild are mandatory before validation; no old-binary fallback
or pin substitution is introduced here.

## Actual policy and frame ownership

Windows exports the actual source system-DPI float policy through DpiUtil and the
window registrar. Non-Windows uses an explicitly typed primary-monitor content
scale provider on its creating thread, not the receiving window scale and not a
Windows DPI claim. Source snapshots retain actual provider/primary identities and
revisions locally. The rendering owner supplies actual device texture limits via
NativeCompositor or WgpuContext. Missing, lost, uninitialized or mismatched
capabilities fail; no 96-DPI or default-device-limit substitution exists.

The compiler captures one coherent policy per batch. Each shader cache brush has
a purpose-specific MIL handle and raster-policy sideband; an ordinary paint using
the same original brush never acquires that sampler authorization. Retained session
comparison includes the policy and exact empty ownership edges, so a revision,
source topology or empty/positive transition cannot silently reuse stale metadata.

Managed capture validates the typed source graph including hidden and empty
descendants, then records actual visible cache content while excluding only the
cache root's documented outer state. The root scroll clip is excluded; descendant
scroll clips remain source content. Capture owns and disposes discarded recordings
on failure. Original source bounds and selected double scale flow into shared
Scene CacheSamplerRasterFrame; WPF does not implement its own round-out, clamping
or UV math. Primary display policy is independent of effect/window DPI.

Genuine null, zero-scale and known-empty sources yield a real owned transparent
1x1 raster after the applicable source ownership checks. Nonnull empty sources
retain their actual Visual identity, topology and explicit empty-bounds witness;
they are never rewritten to a fake positive rectangle or a null shader source.

## Nested ordinary empty sources

An ordinary empty cache brush inside the retained shader source still paints no
cache page. Its canonical paint target remains zero, but the separate
SetBitmapCacheBrushEmptySource edge connects that exact brush handle to its actual
initialized, explicitly empty Visual. Native ownership/revision/deletion/cycle
validation therefore sees the real dependency. Empty bounds are applied before
the edge. Reused source brushes, drawings and visuals propagate dependencies in
either traversal order. Ordinary-only empty painting does not acquire this edge
or broader cache allocation admission.

One combination remains explicitly unsupported: a known-empty DrawingImage whose
canonical drawing handle is zero but whose discarded drawing contains nested
cache dependencies. The source rejects that shader closure rather than claiming
orphan serialized nodes prove ownership. Ordinary empty DrawingImage painting is
unchanged. A separate paired native DrawingImage ownership witness is required to
complete this combination; it is not implemented or qualified by this draft.

## Generation lifetime and authored controls

Managed raw cache replacements create fresh owned rasters. Parameters acquire
FromCacheRaster leases; recorded pictures, clones and compiled consumers own
additional leases. A failed candidate releases its leases and preserves the
previous generation. Replacing a cache entry transfers the earlier owner to its
existing context retirement queue, without invoking cleanup callbacks halfway
through publication. The shared Scene producer owns deterministic context shutdown
and finalizer-safe retirement, not WPF-local GPU handles.

Authored source controls cover explicit policy absence/invalidity, null/empty/
positive sources, same-brush ordinary/shader ordering, snapshot identity, root
exclusions versus descendant state, retained session transitions and real nested
empty dependency edges. Managed forwarding controls now require no receiving frame
for the raw cache family; ImageBrush and VisualBrush retain their separate frame
contracts. Existing ordinary paint/hit controls are preserved.

No builds, tests, syntax checks, verifiers, probes, GPU/UI execution or CI runs were
performed. Qualified submodule pins remain unchanged. Final producer/source union,
both renderer/package paths, original Windows differential and actual application
qualification remain required; this draft is authored implementation only.
