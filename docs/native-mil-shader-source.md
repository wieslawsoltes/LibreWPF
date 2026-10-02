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
adapted texel crops, including resized adapters. For typed `TileMode.None`,
Viewbox defines mapping rather than a source crop: the full image is mapped and
clipped against the original viewport and fill geometry. This follows original
`imagebrush.cpp::CalculateSourceClip` and the actual Windows reference's 50-row
Stretch=None overflow, not the earlier incorrect 20-row cropped expectation.
The existing sampler compositor
captures ImageBrush over the receiving effect's complete zero-origin physical
implicit-input extent with identity mapping, not its intrinsic bitmap/viewbox
extent. The shared Scene `EffectCaptureFrame` computes that extent from actual
effect content bounds, padding and current host DPI using the compositor's exact
arithmetic. An extent over the existing 4096 limit rejects rather than shrinking
the effect frame. Source bitmap DPI only owns source-DIP crop/stretch arithmetic.
Typed
invalid metadata rejects before rendering. Legacy image-only DTOs and untyped
shim image frames retain their prior route. Older typed pixel-only providers may
use their existing pixel descriptor to obtain metrics; updated source BitmapSource
never copies pixels merely to answer a frame query. DrawingImage sampler bounds
reuse original drawing geometry and origin. No renderer is replaced.

Complete-source repeated bitmap tiles now carry Scene's original texture
Repeat/Mirror U/V addressing through an optional command-sink capability. The
actual sink admits only its current Linear mode; Nearest/Fant, untyped metrics,
cropped/padded tiles, non-axis brush transforms and legacy sinks retain their
ordinary image route. Full source/viewbox and stretched viewport equality are
exact, with original DIP and adapted texel frames kept separate. Tile enumeration
retains its existing budget and alternating mirror transforms; positive-axis
brush scopes map paint bounds back into their actual captured float frame before
enumerating, so translations retain negative indices and cover the original
paint clip. Integer range checks reject unrepresentable tile indices before
publishing drawing scopes. No enlarged clamped texture or second compositor is
introduced. The same owned texture lease remains retained by the original drawing
context until its existing clear/retirement boundary, including retained sinks.
The native pairing is ProGPU PR 271, stacked on inherited-option PR 270. Original
Windows repeated Linear references support its first three cases; mirror pixel
qualification, actual managed/native provider execution and the source graph
remain pending. Source-only fixtures cover every address pair, source DPI/adapted
texels, translated negative phases, range rejection, unchanged general routes,
mode restoration and exact command/lease lifetime. They are authored, not run.
Source DIP extent preserves `ImageSource.PixelsToDIPs`' original float DPI ratio
and multiplication before promotion to double. Unrepresentable axes reject;
this path does not invent the original helper's degenerate-resolution fallback.

Complete ImageBrush adapters require a receiving source owner and content frame;
legacy adapters are not silently called without them. Visual and DrawingGroup
replay retain that frame through the existing resource resolver. The sampler
cache is weakly keyed by receiving owner and original brush, so distinct owners
cannot overwrite each other's captured texture. Same-owner resize creates its
replacement before retiring the old texture through existing GpuTexture/context
deferred disposal. Host DPI changes invalidate full source replay even when a
narrower source change was already pending. Existing image-only and DrawingBrush/
VisualBrush routes remain separate.

Capture bitmap filtering is not inferred from the shader sampler's own mode.
Original Windows reference PR 268 currently exposes a filtering difference:
nearest sampling on the effect visual does not itself prove nearest secondary
bitmap realization. Its initial bare DrawingVisual attached-DP controls do not
set the original serialized bitmap-scaling field. The source Visual snapshot
already reads that field; UIElement propagates its DP metadata, while DrawingGroup
serializes the DP directly. Actual-source controls retain all three distinctions.
Genuinely paired render-state propagation and provider pixel parity remain pending;
this source change does not force nearest to match a native-only fixture or claim
the existing managed default is a complete original-state policy.

The focused bridge fixtures cover source packet identity, dense holes, sampler
state, snapshot immutability, retained deltas, cancellation by failed capture,
live invalidation and both native provider sessions. Actual PresentationCore
tests cover original PixelShader and ShaderEffect exports, not shim substitutes.
They run in the existing source-contract CI job using the already built source
test assembly. These tests are authored pending the matching ProGPU producer;
no local renderer or source-graph execution is claimed.

Dependency: ProGPU typed shader transport `94ed4ebae4981ff63b30cb15c07fe904cae3accd`
and optional original bitmap metrics `e60a2dd37`, plus shared capture-frame
implementation `54d51c0fd603a6a8236405379a73c9fd1f460581`
on the native shader implementation from PR 258. Do not publish/pin this source
change until the matching whole successful producer and source/package gates
qualify. Ordinary effect capability advertisement is unchanged. Hosted provider
pixels, actual ShowcaseApp actions, Windows/application parity and package closure
remain separate gates.
