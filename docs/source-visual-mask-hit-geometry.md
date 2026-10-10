# Retained source opacity-mask input

`ProGpuRetainedDrawingVisual` now publishes
`SourceOpacityMaskPreservesHitGeometry = true` alongside its existing
`ISourceGeometryHitTestCommands` retained-command contract. Original WPF `Visual`
point and geometry traversal does not consult visual opacity-mask brushes; it
does retain actual geometry/scroll clips, child transform inverses and effect
mapping. The opt-in belongs to this real source adapter, not every Scene visual.

This pairs with ProGPU's additive default-false interface capability and shared
hit-only traversal. A visible alpha-zero ancestor can retain masked descendant
geometry without rendering it. Source owners, exact local/outer clip scopes,
singular-transform omission, visibility and reappearance stay on the existing
path. Raster masks and their resource ownership are not mutated or sampled for
input. Unknown effect mappings and required cache sources remain rejected.

Original contract evidence is immutable source `381194e`,
`PresentationCore/System/Windows/Media/Visual.cs`, `HitTestPoint` (2035–2208) and
`HitTestGeometry` (2271–2418). No foreign implementation was copied or adapted.
The existing retained sink already maps typed source visual masks and bounds in
`ApplyVisualState`; it now declares their input-only semantics explicitly.

Authored actual-adapter controls retain brush/picture mask identities, source
command identity, own/child/sibling owners, the real clip and hidden/restored
subtrees while source alpha changes. The paired ProGPU change owns generic
negative controls, compositor early-out controls, native MIL scene 9842 and the
independent original-Windows point/region reference.

No build, test, syntax check, verifier, probe, CI, GPU/UI or VM execution was run.
The qualified ProGPU gitlink is unchanged. This extra public property can compile
against the old pinned interface but does not promise that an already compiled
adapter implements a newly added interface slot. Final producer qualification,
exact dependency staging and rebuilding the actual source adapter against that
producer are mandatory before the capability is admitted. This is source wiring,
not desktop or complete mask/effect/cache qualification.
