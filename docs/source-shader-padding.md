# Original ShaderEffect padding transport

The original source `ShaderEffect` retains four protected double padding values.
The native source compiler now passes those same values through the existing
`NativeMilShaderPadding` overload instead of rejecting every nonzero value.
The original double bits, including negative zero and values not exactly
representable as float, survive the source snapshot and MIL packet unchanged.
Float conversion is used only to reject an unsupported nonfinite capture domain.

The actual source exporter rejects nonfinite, negative or float-overflow padding
before collecting shader dependencies. The native compiler repeats this preflight
for independently supplied typed descriptors before resolving their shader or
sampler. Negative source setters retain their original exception behavior;
NaN/infinity and finite double overflow remain original source values but cannot
publish a portable capture. A failed candidate never updates an active channel.

This connects existing producer behavior, not new frame arithmetic. The shared
native implementation narrows the original local edges/padding and owns capture
inflation, source transform history, final-device sample placement, clipping and
UV/derivative normalization. The source bridge neither inflates visual or
DrawingImage bounds nor adjusts shader constants, UVs or source transforms.
All existing native frame restrictions, one-sampler admission, original brush
ownership, float-constant and execution-intent rules remain authoritative.

Implicit input, original ImageBrush/DrawingImage and BitmapCacheBrush retain
their existing distinct captures. In particular, a raw BitmapCacheBrush sampler
still owns its independently sized cache raster and primary-display/device
policy; effect padding does not resize it to the receiving input/output frame.
Known-empty graph sidebands, selected cache ownership and null versus empty
source distinctions are unchanged.

## Managed replacement boundary

The existing managed WGSL replacement path uses one float `MaxPadding` for its
effect and receiving-frame capture. That is not a four-edge asymmetric source
frame contract. This change only rejects invalid raw padding before replacement
or sampler callbacks; it does not change valid-input managed capture arithmetic,
claim asymmetric parity, or add local bounds corrections. A genuinely paired
managed four-edge frame remains separate implementation work.

## Authored controls and dependencies

Thirteen compiler/session configurations cover three source families, exact
asymmetric double bits, signed zero, unchanged source bounds/cache policy,
zero-padding byte restoration, per-axis invalid values and retained generations
for both native backend identities. Existing unsupported-source inventory is
retained: its former nonzero-padding negative now uses finite-double/float
overflow. Six actual PresentationCore source configurations exercise original
DrawingImage/cache brush identity, source bounds, mutation/reset, all invalid
axes, original negative-setter rejection and immutable exported snapshots.
The source selector minimum rises from 18 to 24 with its original 60-second
deadline and fail-skips policy unchanged. Nine mapper configurations separately
guard each invalid DTO axis before sampler dependencies, retain the existing
managed maximum-padding frame on each maximum axis, and preserve valid signed
zero/finite boundary metadata. They do not render pixels or establish an
asymmetric managed frame.

The neutral producer must preserve the four raw double values rather than
sanitize invalid values or negative zero. That paired correction is ProGPU #348
`45bbcf69982052b4b478ee574a90d48d4412c813`, authored as a child of the current
ownership producer #347 (`dd2f1415ac1ee2551ea83321b6581cc52e6fb145`). The existing
native padding API/capture implementation is part of that producer ancestry.
The original Windows padding corpus is not duplicated in this source change.
Its software integer-origin and hardware pixel-center UV distinction remains
explicit; transport tests do not qualify either rendering path.

All controls above are authored and unexecuted. No build, test, syntax check,
verifier, probe, VM or CI run was performed for this branch. It starts from
LibreWPF #247 `bae8f10ede7b0a166e470fad59d722748f85f381`; the qualified ProGPU
`48a49afeb993214c0c40c6908e9896ef0b5dec97` and LibreWinForms
`4badfec8dea1466a908f05e828c5461c422e136a` pins remain unchanged. A coherent
qualified producer, rebuilt source graph and final original/both-provider/UI
validation remain necessary; this source branch is not old-binary compatibility
or application qualification.
