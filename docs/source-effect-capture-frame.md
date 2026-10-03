# Shared source ShaderEffect capture frame

The subsequent [receiving target mapping](source-effect-target-mapping.md) layer
connects actual per-axis projection/viewport inputs to this source descriptor.
The scalar frame description below records the #249 boundary; its legacy APIs
remain intact while the actual source capture selects the paired vector path.

The managed source adapter now retains one immutable Scene
`ShaderEffectSourceCapture` per receiving effect generation: original double
X/Y/Width/Height and original double Top/Bottom/Left/Right padding. This same value
is published on the mapped `WpfShaderEffect.SourceCapture` and the source sampler
request. It is not reconstructed from a float `Visual.Size`, a symmetric maximum
padding, a bitmap's size or rounded offscreen allocation.

The actual source visual and DrawingGroup paths already supply their receiving
owner and content bounds. Both are now required before replacement lookup or
sampler callbacks. Missing, partial or invalid source metadata rejects rather
than selecting a scalar approximation. The original direct `DrawingContext`
`PushEffect` API accepts legacy `BitmapEffect`, not `ShaderEffect`; that unrelated
emulation path stays unchanged.

## Capture and ownership

`WpfShaderEffectSamplerFrame.FromSource` retains the exact descriptor and original
owner. The existing scalar request constructor remains available, but its fields
are not authoritative for a typed source request. The additive
`TryAdaptSourceShaderEffectSamplerBrush` capability defaults to false. A provider
that implements only the old scalar/framed adapter cannot accept a new request
by accidentally reading rounded scalar shadows. The real source image adapter
stamps the actual receiving DPI, preserves the descriptor and invokes the shared
Scene `EffectCaptureFrame.TryCreateSource` before allocating the sampler target.
Ordinary scalar sampler calls still use their original helper.

ImageBrush and VisualBrush capture their complete shared effect frame. The raw
BitmapCacheBrush route remains independently sized by its actual cache-raster
policy and does not consume receiver padding. Existing texture/context ownership,
source leases, transparent-source handling, brush-family rules, unsupported
accounting and the 4096 sampler dimension rejection stay unchanged.

## Original bounds versus retained-local coordinates

Both WPF retained effect forms already normalize source content by a float
translation of its original double origin. Merged retained state uses
`PushRetainedVisualStateContentTransform`; command effect scopes use
`PushVisualScope`. The corresponding real sink now publishes this same translation
as `Visual.EffectSourceTranslation`, after comparing all four original bound
fields against the effect descriptor with exact-bit equality. A mismatch rejects
before visible state or child scope publication. Removal or replacement by a
legacy effect clears the metadata.

Shared Scene code narrows source origins and original double endpoints
independently, inflates the four float edges without a maximum or preliminary
padding ceiling, computes the extent, and applies the recorded rebase only to
the resulting local origin. The WPF layer contains no duplicate edge, extent,
UV or device-rounding algorithm. Sampler capture and implicit-input capture share
the same source descriptor and producer frame function.

This source path does not write `EffectRasterPadding`. The producer preserves
that explicit override and all old scalar APIs separately. The new managed frame
retains its existing logical minimum and physical-extent arithmetic; it is not
the native MIL outward pixel-origin/final-quad contract. Fractional/affine native
or original-Windows pixel parity is not inferred from transmitting four edges.

## Delivery boundary

This is a child of LibreWPF #248 `e30560184fcec0486057b8287c04aef6f49087f0`.
It requires the coordinated Scene producer ProGPU #350,
`4b0ee09d09d0d3514cd1c73d927aaeca381ffdb4`, from
`feat/asymmetric-effect-capture-frame` (initial API `2bfe7ecb1`, source-domain
validation `430565c68ca7c4eaa3144d1c75edb63557d92f70`) and a rebuilt source graph.
Qualified ProGPU/LibreWinForms pins remain unchanged. The older pinned binary is
not claimed to implement these additive contracts.

Eleven new source configurations cover original-double mapper/request identity,
missing owner or bounds, explicit adapter admission, actual DPI forwarding,
nested resource forwarding, actual retained-sink rebase, final clips,
legacy/reset cleanup and atomic rejection of float-equivalent but
original-double-different bounds. These controls are authored separately from
the producer's pure frame and GPU controls. The prior nine padding configurations
and existing image/visual sampler inputs remain; their obsolete maximum-padding
expectations now require exact source-frame identity. Metadata-only forwarding
fixtures do not claim texture capture or GPU execution.

No build, test, syntax check, verifier, probe, GPU/UI/VM execution or CI was run
for this branch. Final producer/source/package and actual platform qualification
remains pending.
