# Source ShaderEffect receiving target mapping

This source connection follows the exact four-edge descriptor in
[source-effect-capture-frame.md](source-effect-capture-frame.md). The receiving
window already owns logical dimensions, physical target dimensions and the actual
viewport before WPF replay. It now retains those inputs together in
`WpfShaderEffectTargetFrame`, before any sampler callback. It does not read the
compositor's previous projection or infer either axis from scalar DPI.

The shared Scene `EffectCaptureFrame.TryResolveSourcePixelsPerUnit` uses the
same projection/normalized-viewport arithmetic as the compositor. WPF forwards
its result to the additive vector `TryCreateSource` overload along with the
independently retained actual semantic DPI. Source padding/bounds remain the
original `ShaderEffectSourceCapture`. No source-local extent, projection, UV,
outward-rounding or affine-scale algorithm is added.

## Actual sampler capture

ImageBrush and VisualBrush sampler realization remains an identity paint over
the complete zero-origin physical implicit-input extent. The shared frame's
`PixelWidth`/`PixelHeight` determine that target; the implicit subtree's
source-to-raster transform is not applied to the sampler brush again. The
existing physical-size offscreen pass remains DPI 1 for this brush realization.
Shader output geometry and UV subrectangle are producer responsibilities.

Raw BitmapCacheBrush sampler textures still use their separate actual primary
display/device raster policy. Neither receiving target mapping nor source
padding replaces that policy. Ordinary effects and legacy scalar-framed sampler
overloads retain their existing behavior.

## Replay and retained identity

The window host supplies the same dimensions/viewport later passed to actual
presentation. Explicit pixel-coordinate `ReplayVisualSubtree` overloads provide
their own actual pixel-coordinate frame. An arbitrary sink without a target does
not acquire a source mapping from a default scalar DPI. Existing tracked replay
that receives this target's already-prepared adapter preserves its immutable
mapping instead of wrapping it with an unbound default frame.

Adapter identity includes the complete target snapshot, not merely scalar DPI.
A changed viewport, logical extent or target size invalidates the whole source
replay, including when the reported DPI is unchanged. Successful cached source
sampler entries retain their target frame. Explicit target callers cannot render
those entries under a different frame without replay. Clearing source recordings
clears the existing sampler entries and their mapping metadata.

Nested effects inside current brush/cache recordings remain explicitly
unsupported by their existing non-effect-scope recording sink. This change does
not forward an outer host mapping into a nested offscreen target or claim to
implement that separate contract. It does not alter original source ownership,
brush-family admission, source geometry hits, final clips or resource retirement.

## Delivery

The source branch is a child of LibreWPF #249
`e9683966cfec976491c9ea8c3c02bc8d5e0dd179`. It requires the coordinated Scene
source-raster-frame producer successor of ProGPU #350 and a rebuilt source graph.
Its frozen API checkpoint is `e9f7f0524c3713755b8960d06397e186575012a0`
on `feat/source-effect-raster-frame`.
The qualified ProGPU and LibreWinForms gitlinks are unchanged; their older binaries
are not claimed to provide the additive resolver/vector frame contract.

Seven new controls cover actual host input wiring, target-owned adapter
reuse/invalidation, exact descriptor/resolver forwarding, absent or invalid
mapping rejection, independent asymmetric mapping and an actual device-owning
sampler cache's physical extent/identity/clear path. The existing eleven source
frame controls remain, with the receiving-adapter control now supplying an
actual target frame. Metadata forwarding does not claim GPU execution; the two
new device-owning controls are likewise authored but not run.

Controls are authored only. No builds, tests, syntax checks, verifiers, probes,
GPU/UI/VM execution or CI dispatch occurred. Actual source/platform rendering and
native/managed parity remain final validation requirements.
