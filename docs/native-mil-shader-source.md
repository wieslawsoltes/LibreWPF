# Original ShaderEffect source transport

Source `ShaderEffect` on a retained visual now reaches native MIL through
`IPortableShaderEffectSource`, original `PixelShader` bytecode, explicit execution
intent and the existing bounded visual-effect frame. This path never consults
the managed WGSL replacement registry. Native ProGPU owns instruction admission,
shader translation, compilation and rendering.

The source descriptor retains the live `PixelShader` for dependency subscription
and per-batch handle identity, while each packet owns the captured original bytes.
Sharing a source with conflicting snapshots in one batch rejects atomically.
Dense source float registers include zero holes; all are explicitly transmitted.
Retained sessions compare owned packet bytes, so bytecode, mode, constants and
sampler changes generate real resource updates rather than stale DTO identity hits.
Failed source capture never reaches the active channel.

Only explicit Auto/HardwareOnly intent, float constants, zero padding, and one
implicit-input or original ImageBrush sampler are admitted by this source seam.
SoftwareOnly, absent/unknown intent, raw-image-only sampler DTOs, arbitrary brush
samplers, multiple/missing samplers, integer/Boolean constants and nonzero padding
remain rejected. Instruction/register combinations remain subject to native
validation; this does not claim all shader-model bytecode is implemented.
The original `compileSoftwareShader` MIL flag is retained, not interpreted as
permission to run a CPU renderer.

ImageBrush export preserves its actual source identity, opacity, image, mapping,
tile mode and transforms through the ordinary native brush compiler. The managed
replacement path likewise requires its existing brush adapter when the complete
source brush is available; the older image-only DTO still uses its legacy route.
Source dependency traversal observes both the original brush and PixelShader.

Original BitmapSource exports metadata-only pixel dimensions and both source DPI
axes. Shared ImageBrush replay keeps source DIP stretch/placement separate from
adapted texel crops, including resized adapters. The existing sampler compositor
captures ImageBrush with independent physical axes and the existing 4096 extent
cap; it does not shrink high-DPI source texels into a 96-DPI assumption. Typed
invalid metadata rejects before rendering. Legacy image-only DTOs and untyped
shim image frames retain their prior route. Older typed pixel-only providers may
use their existing pixel descriptor to obtain metrics; updated source BitmapSource
never copies pixels merely to answer a frame query. DrawingImage sampler bounds
reuse original drawing geometry and origin. No renderer or cache owner is replaced.
Source DIP extent preserves `ImageSource.PixelsToDIPs`' original float DPI ratio
and multiplication before promotion to double. Unrepresentable axes reject;
this path does not invent the original helper's degenerate-resolution fallback.

The focused bridge fixtures cover source packet identity, dense holes, sampler
state, snapshot immutability, retained deltas, cancellation by failed capture,
live invalidation and both native provider sessions. Actual PresentationCore
tests cover original PixelShader and ShaderEffect exports, not shim substitutes.
They run in the existing source-contract CI job using the already built source
test assembly. These tests are authored pending the matching ProGPU producer;
no local renderer or source-graph execution is claimed.

Dependency: ProGPU typed shader transport `94ed4ebae4981ff63b30cb15c07fe904cae3accd`
and optional original bitmap metrics `e60a2dd37`
on the native shader implementation from PR 258. Do not publish/pin this source
change until the matching whole successful producer and source/package gates
qualify. Ordinary effect capability advertisement is unchanged. Hosted provider
pixels, actual ShowcaseApp actions, Windows/application parity and package closure
remain separate gates.
