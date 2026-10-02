# Source ShaderEffect input opacity

The typed WPF adapter marks an adapted `WpfShaderEffect` with the immutable
`CaptureSourceVisualOpacity` contract. Original source visual opacity and its
opacity mask participate in the implicit input before shader evaluation. A
constant-output shader can therefore produce visible output when its own source
opacity or mask alpha is zero. A zero-opacity ancestor remains an outer scope.
Geometry clipping is separate and remains outside the effect.

This is not a default change for arbitrary ProGPU Scene effects. The shared
implementation is ProGPU PR294 (`fix/source-shader-opacity-order`, implementation
`c6442aa02`, authored controls `1b7019c8b`), stacked on the native source-opacity
work in PR290. It owns the input capture, cache identity, zero-alpha early-out
handling and output scope; WPF does not add another compositor.

## Both source routes

- `WpfEffectMapper` marks only the typed source ShaderEffect adaptation. Existing
  replacement resolution and sampler validation remain unchanged.
- Merged retained replay keeps opacity, mask, original mask bounds, cache state,
  clip and effect on the same owner. `ProGpuRetainedCompositionCommandSink`
  retains that topology, and the shared marked effect consumes the root alpha.
- Command-scope replay pushes the marked effect after geometry clips but before
  opacity/mask scopes. Thus the existing effect wrapper captures those child
  scopes. It resolves the source effect once and records a rejected push once,
  without adding an unmatched pop. Blur, shadow and other effects keep their
  existing output-opacity ordering.

No source replay early-out based on visual opacity precedes these two paths;
zero-alpha source content still reaches the shared effect capture. Hidden source
visuals remain hidden. Shared generic shader defaults and public source render
mode/native capability admission are not widened.

## Authored controls and pending qualification

Six new actual-adapter cases cover command opacity zero/one-half with a zero-alpha
mask and constant shader, retained cached/uncached zero-alpha state with nonzero
content origin and separate normalized clip/mask frames, rejected effect push
scope balance, and unchanged blur ordering. The existing typed shader test also
asserts the source marker. The shared producer adds paired-provider pixel controls
for constant output, gradient masks, zero-alpha ancestors, and cache behavior.

These controls are authored, not executed. Per the implementation-first request,
no build, test, verifier or CI workflow was run for this child. It starts from
LibreWPF PR238 exact `e271942424727b42e1045467c097c44b88324193`, preserving its
modal/source work. ProGPU and Forms gitlinks, package versions, and qualified
runtime selection remain unchanged. The final integrated producer union and
matching source build/pixels are required before updating the producer pin or
claiming application parity.
