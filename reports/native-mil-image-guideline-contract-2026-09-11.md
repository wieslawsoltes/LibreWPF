# Native image guideline first-frame blocker

Acceptance: the external SDK application opens and presents its first frame with
NativeMilWgpu and the native owner index enabled. With ProGPU `a939a049`, local
diagnostic payload substitution reaches image command 5179, kind 19, resource
3057. It still fails the generic per-point draw-family guard. This is not an
exact-package or successful application result.

## Source evidence

The source path is `CDrawingContext::DrawImage` → `DrawBitmap` →
`FillShapeWithBitmap` in
`src/Microsoft.DotNet.Wpf/src/WpfGfx/core/uce/drawingcontext.cpp`.
`DrawBitmap` constructs the destination shape and the source-bitmap-to-local
affine mapping independently. `FillShapeWithBitmap` supplies that mapping to
the bitmap brush and submits the destination shape as a path.

`CHwSurfaceRenderTarget::FillPathWithBrush` in
`src/Microsoft.DotNet.Wpf/src/WpfGfx/core/hw/hwsurfrt.cpp` applies guidelines to
the shape before applying the brush, while retaining the original brush mapping.
The observable contract is separate snapped coverage and source sampling, not
scaling the bitmap to the snapped destination envelope. This trace records
contract behavior only; no source renderer code is imported into ProGPU.

## Consequences for the implementation

The current native semantic image compiler emits four vertices (six for patches)
with UVs assigned from the original source rectangle. Simply snapping those
vertices while retaining corner UVs stretches the bitmap and is not the source
contract. Computing UVs from the original inverse affine at new vertices avoids
that stretch, but does not by itself establish correct coverage for arbitrarily
deformed or collapsed quads, antialiasing, or source-edge clipping.

Use the existing canonical path coverage machinery for the snapped destination
and keep the existing image sampler/material in its original frame. Any isolated
coverage must include the same guidelines and presentation mapping exactly once,
retain inherited clips/masks, and have sufficient target bounds for snapped
edges. The native owner index must retain the original source destination, not
the coverage allocation or mask-internal geometry. D3DImage/external images and
bitmap images share `append_bitmap_source`; DrawingImage retains its distinct
source-owned destination annotation and vector lowering.

Required regression cases include opposite edge offsets, source cropping,
fractional DPI, transform/localization, collapsed coverage, inherited clips,
unchanged source input and a following draw after scope restoration. Compare
pixels against native Windows before claiming image parity. Keep the existing
draw-family rejection until the execution path and its coverage are connected;
do not accept the command merely because its four vertices can be moved.

## Merge status

The current ProGPU Windows x64, Windows ARM64 and MSVC jobs were observed running
on the new checkpoint; no failing checks were reported at this inspection.
Running checks are not passes. Exact-head packages, final application/platform
qualification and ordered ProGPU → LibreWinForms → LibreWPF merges remain open.
