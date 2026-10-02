# Original source transform resources

Built-in WPF transforms export their current original primitive parameters through
`IPortableTransformSource`; TransformGroup captures ordered original child
identities. Export does not call a group's aggregate `Value`, reduce an angle,
narrow a scalar or evaluate a child. The existing matrix interface is unchanged
for existing managed consumers. Unknown original resource kinds fail closed.

The native compiler now emits actual Matrix/Translate/Scale/Rotate/Skew/Group
packets. Shared children are captured once and referenced at every original
position; groups retain order and empty identity groups. Cycle/depth/child and
resource budgets fail before the private batch is returned. An advertised but
failed original-resource contract never falls back to an aggregate matrix.
Independent external matrix-only providers retain their previous route.

This is transport, not permission to infer original named-leaf float arithmetic.
The paired producer retains explicit Rotate/Skew ShaderEffect witness gates
until their primitive construction contract is established. Generic old native
transform behavior is unchanged. Compilation-session deltas and channel atomic
graph publication remain authoritative; no default renderer or runtime pin is
changed. Authored controls will run only on the final qualified union.

Five compiler/session controls cover shared-child order/single capture, retained
primitive delta and structural reorder, cycle/typed failure with successful later
capture, late nonfinite/depth rejection, and empty groups. Two actual source
controls cover every built-in primitive, original angle/double bits, mutation,
group-list ownership, shared child identity and frozen cloning. Existing
matrix-only controls remain unchanged. No validation was executed.
