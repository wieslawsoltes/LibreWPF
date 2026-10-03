# Known-empty DrawingImage shader ownership

This child of source #246 connects the paired native
`NativeMilChannel.SetDrawingImageEmptySource(imageHandle, drawingHandle)` contract.
It does not reinterpret an unavailable drawing descriptor or synthesize bounds.
The compiler first resolves the real original drawing with its complete typed
graph. A successful authoritative empty-bounds descriptor still emits the existing
canonical DrawingImage drawing handle zero, preserving ordinary no-ink painting.
Only a shader source closure publishes the separate ownership edge to that exact
initialized drawing. An absent Drawing has no edge.

Empty image wrappers and their nested cache/image dependencies propagate through
reused brushes, drawings and visuals in either source traversal order. The owned
batch copies the complete edge list; retained sessions compare it as topology and
apply it after graph initialization and nested empty-Visual/cache witnesses. A
changed edge uses a candidate channel. Failed candidate metadata leaves the prior
channel and previously compiled generation owned. Positive source bounds keep
their existing canonical drawing and bounds sideband; refill or disconnection
does not leave an old empty-source witness attached.

The managed shader validation pass also visits known-empty DrawingImage content
through actual drawing replay before skipping its paint. This is the existing
discarded, scoped ownership recording, not an extra raster, source mutation, or
substitute native proof. Actual nested cache policy, unsupported descendants and
cycles cannot hide behind empty image bounds. Ordinary replay remains unchanged
outside that explicit validation pass.

Authored controls retain the earlier ordinary no-op checks and replace the
previous explicit unsupported combination with a real image-to-drawing-to-cache
edge assertion. Additional controls cover both traversal orders, ImageBrush and
DrawImage wrapper reuse, nested wrappers, genuine null, all three admitted shader
source families, hidden invalid descendants, source cycles, and both native
retained-session backends across empty/refill/replacement/disconnection. Malformed
ownership sidebands exercise failed candidate preservation independently of the
canonical packet delta. Managed controls cover both image routes, hidden nested
sources, invalid selected cache policy, genuine null and actual cycles.

These are authored controls only: no build, test, syntax check, verifier, GPU/UI,
probe, VM or CI execution occurred. The new producer API is intentionally not in
the held qualified gitlink. Coordinated producer qualification, exact source
rebuild and final application/provider/package validation remain required.
