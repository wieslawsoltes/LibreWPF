# LibreWPF native MIL on ProGPU

## Objective and ownership

LibreWPF is adding an opt-in native composition lane beside the current managed
portable renderer. ProGPU owns the backend-neutral retained resource graph,
canonical MIL decoder, semantic scene lowering, C ABI, WebGPU execution, Dawn
D3D12 execution, and future DirectX/DXGI interop facade. LibreWPF owns the
source-integrated producer, WPF lifecycle selection, package wiring, and parity
fixtures. This keeps the compositor reusable by WPF, WinUI, and Avalonia.

The active ProGPU work is tracked in draft
[ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139). Both this
superproject branch and the ProGPU submodule branch started from the fetched
latest `main`; the superproject records exact reviewed ProGPU commits. The
exact singular-affine qualification and its reproducible binary hashes are
pinned together with the subsequent degenerate point, ellipse, and rectangle
qualification at ProGPU documentation commit `37d052ce`.
The subsequent canonical static-transform, transform-animation, and native
gradient qualifications are pinned at ProGPU documentation commit `becbe01d`.
The canonical GeometryDrawing qualification is pinned at ProGPU documentation
commit `837b47e9`. The canonical DrawingGroup qualification is pinned at
ProGPU documentation commit `848763dc`. The canonical ImageDrawing
qualification is pinned at ProGPU documentation commit `5bbb7073`. The
canonical GlyphRun/GlyphRunDrawing qualification is pinned at ProGPU
documentation commit `834b318b`. The current submodule head is that exact
latest-`main`-integrated lineage plus the static-guideline implementation,
package, validation, and pinned Microsoft D3D12 sample-oracle checkpoints
described below.

## WPF protocol model

Canonical DUCE/MIL batches use the following little-endian framing:

```text
uint32 item_size_including_header
uint32 command_id
byte[] packed command fields and optional payload
byte[0..3] DWORD padding
```

`item_size` is at least eight, divisible by four, and contained in the batch.
WPF's open-source command model defines 141 IDs from `0x01` through `0x8d`:
61 transport/core/resource/visual/target commands, 25 nested render-data
commands, and 55 media/resource commands. `MilCmdRenderData` contains a byte
count followed by another framed command stream. Resource handles are 32-bit
and scoped to one channel.

ProGPU protocol-authority implementation `8839f00d` generates its public C++
command enum from WPF's checked-in MCG outputs. Complete-layout checkpoint
`4408a86c` adds all 116 packed structures from `wgx_commands.h` and all 25
records from `wgx_renderdata_commands.h`, exactly one layout for each of the
141 retail commands. The 108 explicit `Pack=1` layouts in `wgx_commands.cs`
serve as an independent overlap oracle for shared size and field offsets. The
neutral manifest records SHA-256 provenance for all four WPF inputs plus the
invalid/debug sentinels. The ProGPU standalone build checks manifest/header
agreement. `eng/progpu-wpf-sdk-ci.sh` additionally regenerates from this live
LibreWPF tree, so a WPF protocol change cannot silently leave the submodule's
decoder authority stale. ProGPU `d4a1f370` makes the complete retained Visual
update family plus DoubleResource and PointResource consume generated
constants, including variable guideline packets and child topology. Private
MCG packing bytes are captured and every fixed header must retain DWORD
framing. ProGPU `e93d8919` moves all active top-level and nested render-data
packet readers plus dependency discovery to the complete generated layouts.
No numeric `has_exact_size(view, ...)` or direct numeric
`read_at(view.packet, ...)` calls remain in the decoder; composite components
and the separately bounded path-figure mini-protocol remain intentional.

ProGPU producer-oracle checkpoint `563c031c` also parses all 25 packed nested
payload structs from this tree's `Generated/RenderData.cs`. Those managed
layouts agree with 24 native MCG declarations. Legacy `MILCMD_PUSH_EFFECT` is
the single source discrepancy: the managed writer emits `hEffect` and
`hEffectInput`, while `wgx_renderdata_commands.h` declares only the opcode.
Because the managed writer owns the bytes placed on the channel, its 12-byte
command view is authoritative. ProGPU `e510039d` requires that exact framing
in both execution and retained dependency traversal, then fails closed with
`unsupported_command`; the obsolete 4-byte interpretation is
`malformed_batch`. No reflection, object-shape probing, or managed effect
substitution is involved.

The complete Apple Silicon native/Metal gate passes at `e510039d`, including
all native tests, the bounded differential matrix, exact intrinsic-SIMD/scalar
glyph parity, and both Microsoft DirectX sample oracles. Clean detached
Windows ARM64 qualification rebuilt both generated-header consumers with MSVC
`/W4 /WX` and passed all 11 native/Dawn CTests. The Parallels D3D12 fastest,
raster-shader, intrinsic-SIMD, and scalar paths remain exact at glyph hash
`5B6EF4F70536C862`; forced compute retains its expected typed adapter rejection.
Triangle and texture oracle hashes remain
`AE1BC0A9B0623BACAB15BE1706FFA3E7FC15E33676A66F05C969C1B86A66FEA3` and
`591CC311F35E3C2612F529C3D4D7061FC93751A9B8614BF588A73599B0AA2790`.
Qualified DLL SHA-256 is
`5B140B2D5881C3847ECBD6D4E7F8B592DD54C24E2687915EDF30BCA4BC78796D`
for `progpu_native.dll` and
`7D7F35CFA5323D0BA6E61EA402788CBAE72EBA40D69FE5B3D05069C966AB56DB`
for `progpu_native_dawn.dll`. The live generator reports 143 commands and 141
complete packet layouts; the focused LibreWPF producer suite passes 73/73.

The exact generated-Visual pin `22bf5bf1` also passed a clean Windows ARM64
qualification in the Parallels VM. MSVC rebuilt the generated header and both
native modules under `/W4 /WX`; all 11 native/Dawn CTests passed, including the
MIL packet/layout suite. SHA-256 was
`FB4304088E87A3F07CA59A84B16FEDA21A4DDADBB9377028553740D51B30F290`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL. Live WPF-to-ProGPU regeneration remains in the
macOS/Linux SDK gate; the Windows lane validates the committed generated C++.

Clean detached `e93d8919` qualified the complete authority on Windows ARM64.
MSVC rebuilt all generated-header consumers and both native modules under
`/W4 /WX`; all 11 native/Dawn CTests passed. Qualified SHA-256 is
`7D4D5087CB7D81893CDE231BEDD22983A0C31323AE1EDF5A87FDDC415E758CB5`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL. The live LibreWPF drift gate reports
`143 commands, 141 complete packet layouts`.

ProGPU implementation `4e7d8f55` extends generated decoding through
MatrixResource and the complete retained 2D transform family: variable
TransformGroup children, translate, scale, skew, rotate, matrix, and all
animation-resource handles. It preserves the existing typed resource, finite
value, and graph-cycle validation. Apple Silicon passed the live generator
drift check and all 11 native/Dawn CTests; clean Windows ARM64 MSVC `/W4 /WX`
rebuilt both modules and passed the same 11 tests. Qualified SHA-256 is
`B514024B7F83A06C5F6FD2CDED7C9677255AD283076B3E61AA096DC633288E48`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL.

ProGPU `1b4ef706` moves SolidColorBrush, LinearGradientBrush,
RadialGradientBrush, DashStyle, and Pen packet decoding onto generated WPF MCG
layouts. Variable gradient-stop and dash-array payloads start at generated
fixed-header boundaries. Existing transform/animation resource validation,
mapping and spread modes, stop data, pen cap/join semantics, and finite-value
checks are unchanged. The live generator check and all 11 Apple Silicon tests
pass; clean Windows ARM64 MSVC `/W4 /WX` rebuilt both modules and passed all 11
tests. Qualified SHA-256 is
`163F49880179F85857ED4FB02C6F1CEB95C46158B407C87934568239C4FE9E5F`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL.

ProGPU `f2107a55` moves LineGeometry, RectangleGeometry, EllipseGeometry,
GeometryGroup, CombinedGeometry, and PathGeometry onto generated WPF MCG
layouts. The generated fixed-header size now defines variable group-child and
path-figure payload starts, while the nested path records remain separately
bounds checked. Existing geometry resource, transform, animation, cycle,
fill-rule, finite-value, and record-count validation is unchanged. The live
generator check and all 11 Apple Silicon tests pass; clean Windows ARM64 MSVC
`/W4 /WX` rebuilt both modules and passed the same suite. Qualified SHA-256 is
`853802988172C66820819B389E48305613A0488FEB3972C0F2C3BD61EB9CEDAC`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL.

ProGPU `9d489872` moves GeometryDrawing, GlyphRunDrawing, ImageDrawing,
DrawingImage, variable GuidelineSet/DrawingGroup payloads, and BitmapCache onto
generated WPF MCG layouts. Generated fixed-header boundaries now define the
guideline coordinate and drawing child-handle payload starts. Resource-type
dependencies, drawing cycle checks, opacity/render-option validation, retained
bounds preservation, child render-data synthesis, and bitmap-cache semantics
are unchanged. The generator check and all 11 Apple Silicon tests pass; clean
Windows ARM64 MSVC `/W4 /WX` rebuilt both modules and passed all 11 tests.
Qualified SHA-256 is
`096EE139F64DDB2D0FEC503424ECBFED98D97AEDCA29E9C9DD80ACF9FDF8FCE8`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL.

ProGPU effect commit `1ac97d67` migrates BlurEffect and DropShadowEffect,
including animation handles and rendering bias. Target commit `ee54c934`
migrates GenericTarget creation/root/clear/flags/invalidation plus the variable
RenderData payload boundary. Existing effect validation, unsupported-animation
policy, target/resource ownership, and nested command-byte preservation are
unchanged. Both commits pass the generator check and all 11 Apple Silicon
tests. Clean Windows ARM64 qualification at containing head `ee54c934` rebuilt
both modules under MSVC `/W4 /WX` and passed all 11 tests. Qualified SHA-256 is
`5B0F5505811EB938A9FDC097B330ECFBD4CFFA0CD7409E9BD1305798FAD35A94`
for `progpu_native.dll` and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDDBC`
for the wgpu-native runtime DLL.

The portable `RenderData` snapshot already uses the same framed nested records,
but its resource tokens are one-based indexes into a typed dependent-resource
array. LibreWPF's native producer therefore preserves command bytes and remaps
typed resource tokens to native channel handles. It never reads private WPF
fields or dispatches by type name.

## Implemented checkpoint

ProGPU currently provides:

- A C++20 zero-copy batch reader and transactional channel graph. A rejected
  batch cannot partially mutate live state.
- Generated stable command ID/packet-layout definitions and strict unknown,
  malformed, unsupported, handle, resource-type, graph, and capacity errors.
- Retained visual offsets, opacity, content, ordered child topology, generic
  targets, clear color/flags, opaque render data, solid-color brushes, and the
  complete static 2D transform resource family.
- Cycle, multiple-parent, and depth validation for the retained visual graph.
- Nested solid-brush `DrawRectangle`, `DrawEllipse`, and uniform-radius
  `DrawRoundedRectangle` decoding and lowering into ProGPU's pointer-free
  semantic scene stream with cumulative visual state and typed primitive
  metrics. Non-uniform rounded corners fail closed rather than being
  approximated.
- Balanced nested `PushOpacity`/`Pop` decoding, cumulative visual/scope opacity,
  typed semantic-state emission, and strict stack validation.
- Typed `MatrixTransform`, `TranslateTransform`, `ScaleTransform`,
  `SkewTransform`, `RotateTransform`, ordered retained `TransformGroup`,
  visual-transform, and nested `PushTransform` packet decoding. Leaf values use
  WPF float-matrix evaluation; groups resolve live child state in row-vector
  collection order for every visual, scope, geometry, boolean, and clip
  consumer. Typed `DoubleResource` and `MatrixResource` handles replace their
  corresponding base transform fields with live current values. Cycles,
  referenced-child/animation deletion, wrong-type animation handles, and
  unresolved nonzero resources fail closed. Handle zero remains WPF's balanced
  no-op transform scope.
- Typed solid `Pen` resources and nested `DrawLine` lowering through ProGPU's
  reusable geometry-stroke primitive, including all four WPF start/end cap
  kinds, affine stroke bounds, null-pen no-op semantics, line metrics, and
  transactional rejection of animation resources.
- Typed variable-size `DashStyle` resources for line pens, preserving
  thickness-relative intervals, offsets, dash caps, and ProGPU semantic-stroke
  execution across native backends.
- Typed retained linear/radial gradient brushes and `PointResource` current
  values, including WPF-relative bounds mapping, absolute and relative brush
  transforms, anisotropic/focal radial state, ScRGB/sRGB interpolation, all
  three spread modes, stable stop normalization, and dependency-protected live
  updates. Fills and common nondegenerate pen strokes reuse ProGPU's shared
  semantic vector-gradient shader in both wgpu-native and Dawn.
- Canonical retained `GeometryDrawing` resource `87` and nested `DrawDrawing`
  command `0x4a`. LibreWPF resolves only
  `IPortableGeometryDrawingStateSource`, emits typed brush, pen, and geometry
  dependencies once per compilation, and preserves null geometry as a no-op.
  ProGPU reuses the existing native `DrawGeometry` lowering and rejects
  wrong-type or prematurely deleted dependencies transactionally.
- Canonical retained `DrawingGroup` resource `91` with ordered nested drawings,
  typed transform, exact geometry clip, static opacity, and live
  `DoubleResource` opacity. LibreWPF consumes only
  `IPortableDrawingGroupStateSource` and `IPortableDrawingGroupChildrenSource`,
  preserves child order, rejects cycles, and fails closed for unimplemented
  mask, dynamic/multiple-guideline, effect/cache, and render-option state.
  ProGPU recursively
  compiles the retained group through the same native drawing and geometry
  paths while preserving parent semantic scopes and dependency lifetime.
- Canonical `GuidelineSet` resource `92` and command `0x8c` for the exact
  zero/one-static-coordinate-per-axis subset of WPF pixel snapping. LibreWPF
  reads only `IPortableGuidelineSetSource`, emits the canonical X/Y doubles,
  and rejects dynamic or multi-coordinate state. ProGPU transforms static
  guides into device space with WPF float evaluation and half-coordinate tie
  behavior, retains them as package-neutral semantic resource kind 17, and
  resolves the DPI-dependent uniform translation in the shared native state
  cursor. Rotated/sheared scopes use WPF's empty snapping-frame behavior.
- Canonical retained `ImageDrawing` resource `89` referencing BitmapSource
  handle `95`. LibreWPF consumes only `IPortableImageDrawingStateSource` and
  `IPortableBitmapSourcePixelsSource`, converts supported typed pixels once to
  compact straight-alpha RGBA8, and carries them in a pointer-free batch
  sideband. ProGPU binds those copied pixels to the retained handle, emits one
  shared semantic image resource/draw, and never transports WPF's process-local
  WIC pointer. Missing pixels, unsupported image kinds, and invalid dimensions
  fail closed.
- Canonical `GlyphRun` resource `42`, retained `GlyphRunDrawing` resource `88`,
  direct nested `DrawGlyphRun` command `0x49`, and retained `DrawDrawing`
  execution. LibreWPF consumes only cached `IPortableNativeGlyphRunSource` or
  `IPortableGlyphRunSource` state, keeps source-owned `Vector2` positions on the
  direct builder path, requires an identity glyph transform for this slice,
  and binds typed SFNT/TTC bytes plus face/style state before compilation.
  ProGPU zeroes the canonical process-local `IDWriteFont*`, retains copied font
  bytes through a typed sideband, shares identical font storage, decodes native
  TrueType outlines once per glyph-run/raster-size key, and routes direct and
  retained text through the same semantic renderer on wgpu-native and Dawn.
- Typed rectangle pen production for fill-only, stroke-only, and combined
  records. Native rectangle outlines use closed ProGPU semantic polylines with
  exact join/dash metadata and affine-expanded stroke bounds. Solid
  zero-width/height rectangles lower WPF's single outer widened figure to an
  exact vector fill: Miter and Bevel preserve the source bevel-offset formula,
  while Round emits analytic quarter arcs. One-axis dashed rectangles retain
  the four-point closed contour and WPF reversal joins. Fully collapsed sharp
  rectangles use the typed Round/Round point disk when the initial dash is
  visible and emit no draw for an initial gap. Degenerate fills stay empty.
- Typed ellipse pen production for fill-only, stroke-only, and combined
  records. Solid ellipse outlines use ProGPU's exact full-ellipse analytic arc,
  preserve non-uniform radii, and publish affine-expanded stroke bounds;
  one-axis ellipses use WPF's SmoothJoin-derived Round/Round capsule and point
  ellipses use the native point disk. Positive-area dashed ellipses reuse one
  closed analytic arc contour and the native curve-dash compiler; one-axis
  dashed ellipses retain four ordered collapsed quarter traversals, while a
  fully collapsed ellipse uses the visible-initial-dash point disk/initial-gap
  no-op rule. Degenerate fills stay empty.
- Typed rounded-rectangle pen production for fill-only, stroke-only, and
  combined records. Uniform positive radii use ProGPU's analytic primitive;
  positive independent X/Y radii use its exact elliptical vector path and
  connected-curve stroke. Positive-area records with either radius zero follow
  WPF's sharp-rectangle equivalence and retain rectangle join/dash behavior,
  while positive-area uniform and independent-X/Y curved dashes reuse the
  exact closed line/quarter-arc contour. Degenerate records with both radii
  positive retain WpfGfx's canonical 17-point alternating cubic/line contour,
  independently clamp X/Y radii, and reuse the native curve-dash compiler;
  point records reduce to the visible-initial-dash Round/Round disk or
  initial-gap no-op. Records with either radius zero, including asymmetric
  degenerates, normalize to the qualified sharp one-axis or point lane before
  widening and dashing.
- Typed retained `LineGeometry`, `RectangleGeometry`, and `EllipseGeometry`
  resources with nested `DrawGeometry` lowering. Optional geometry-local
  affine transforms compose with visual and drawing scopes; line pen semantics
  reuse ProGPU's stroke path while rectangle, rounded-rectangle, and ellipse
  resources reuse native analytic/vector fill and stroke lowering. Animated
  fields and degenerate zero-axis asymmetric rounded radii fail closed.
- Typed retained general `PathGeometry` production from
  `IPortableGeometryPathSource`, using the shared exact local-path bounds
  reader and canonical WPF path/figure/fixed-segment records. Native fill-only
  lowering supports line, quadratic, cubic, and endpoint-arc contours,
  EvenOdd/Nonzero fill, implicit closure, and geometry-local affine transforms.
  Arc math is shared with ProGPU's SVG glyph paths.
- Exact canonical `PushClip` production keeps retained, axis-aligned,
  non-rounded rectangles on semantic scissor state and lowers other fixed,
  path, group, and combined geometry to ordered semantic vector masks. ProGPU
  captures the transform active at push time and preserves analytic segments,
  fill ownership, and recursive boolean programs without broadening to bounds.
- Native retained path pens for line/polyline topology. ProGPU preserves true
  open/closed stroke contours independently from WPF's implicit fill closure,
  splits `IsStroked=false` geometry gaps into dash-capped runs, restarts dash
  phase per WPF `CDasher`, carries all pen and affine state into reusable native
  semantic polylines, and joins the solid closed-gap seam without a false cap.
  Solid line/quadratic/cubic/analytic-arc contours now lower to reusable ProGPU
  native geometry primitives with their geometry-local affine transform
  preserved. Joined and closed mixed curves compose exact native path-join
  records whose endpoint tangents come from the line, curve controls, or
  resolved analytic arc derivative. Open curves compose Square, Round, and
  Triangle start/end caps from the same tangent data; Flat caps remain
  implicit, and geometry gaps use the typed dash-cap value. `IsSmoothJoin` is
  retained on its incoming segment and forces only that endpoint's native join
  to Round, including the closing endpoint. A dashed open run crossing a
  closed figure's start remains one rotated semantic polyline, preserving dash
  phase and DashCap at both geometry-gap boundaries. Nondegenerate dashed
  line/quadratic/cubic/analytic-arc contours now use the native curve-dash
  compiler: thickness-scaled phase continues across segment boundaries,
  visible Bézier spans retain De Casteljau control points, visible arcs retain
  their analytic center/radii/rotation/sub-sweep, true open endpoints keep the
  source caps, internal endpoints use DashCap, SmoothJoin still forces Round,
  and first/final visible runs merge across a closed seam without coincident
  caps. Degenerate tangents adjacent to nondegenerate joins fail closed.
  Zero-length immediate/open path strokes compose their configured point-cap
  halves around WPF's horizontal shape-space direction; wholly degenerate
  closed contours force Round/Round caps and form an exact point disk. Finite
  nonzero dash patterns select the initial dash/gap by normalized offset, with
  exact-boundary offsets belonging to the preceding WPF interval; all-zero
  cycles remain fail closed.
- Canonical retained `GeometryGroup` packets in ProGPU's raw MIL backend,
  including variable child handles, fill rule, matrix transform, dependency
  deletion, cycle rejection, and transactional rollback. Identity-local path
  children aggregate into one semantic path batch so EvenOdd/Nonzero applies
  across overlapping child contours. Affine-transformed line/quadratic/cubic
  path children bake their points, implicit fill closure, and conservative
  bounds into that coordinate space exactly. Fixed rectangle and ellipse
  children join that batch, including geometry-local affine transforms and
  WPF-exact non-uniform rounded-rectangle radius clamping/cubic control points;
  line children contribute no fill. Nested groups lower recursively with
  WPF-order transform composition, bounded depth, transactional rollback, and
  the root group's fill rule applied across all descendant contours, matching
  WPF's `CShape::AddShapeData`/outer `SetFillMode` behavior. Nonzero groups keep
  a shared contour batch for cross-child winding cancellation; EvenOdd groups
  use an equivalent postfix XOR of child-inside predicates so raster work stays
  bounded by each leaf. Every pure left-fold XOR form from two leaves through
  the existing 32-child MIL ceiling with overlapping translated-equivalent
  simple leaf streams executes as ordered, phase-batched GPU raster masks
  followed by a packed XOR combine. Mixed postfix operations retain the typed
  fail-closed guard at semantic scene compilation before WebGPU device creation
  because those backend patterns are not yet qualified on the Parallels D3D12
  adapter. Nonsingular affine arc-bearing children remain analytic:
  ProGPU factors the transformed ellipse basis, preserves parameterization, and
  reverses sweep under reflection. Exact translations preserve the original arc
  fields bit-for-bit except for endpoints/center. Exact singular affine
  transforms produce empty fill and stroke coverage, matching WPF's
  zero-determinant area semantics. EvenOdd groups now preserve a
  CombinedGeometry child as its existing postfix predicate and XOR it with
  ordinary outer-fill contour leaves for both fills and vector clips; per-point
  guideline segments flow through the fill compiler. Nonzero groups containing
  boolean children and meaningful group pens remain fail closed pending exact
  winding/stroke composition.
- Canonical retained `CombinedGeometry` packets with optional transform,
  null-as-empty operands, dependency/cycle validation, and all four WPF combine
  operations. Two identity-local path operands lower to ProGPU's native postfix
  boolean program and retain each operand's fill rule. Fixed rectangle and
  ellipse operands share the group's exact contour lowerer, including
  geometry-local affine transforms, non-uniform rounded rectangles, and empty
  line leaves. Affine-transformed line/quadratic/cubic paths share the same
  exact point/bounds baking. A recursively lowered group can now be either
  boolean leaf while retaining that operand group's root fill rule. Combined
  operands recurse into the bounded geometry DAG, compose nested transforms
  into descendant leaves, and append arbitrary-depth postfix boolean trees with
  segment/node rollback and conservative descendant bounds. Nonsingular affine
  arc-bearing leaves use the same exact analytic factorization and sweep
  reversal as group children. Singular transformed operands become exact empty
  leaves. Stroked operands currently fail closed. Combined children inside an
  EvenOdd group retain their predicate and join the outer postfix program; they
  are never flattened into raw contours. Nonzero groups with boolean children
  fail closed because the signed winding required by WPF cannot be reconstructed
  from an inside predicate.
- An identical size-versioned C ABI exported by wgpu-native and provider-
  resolved Dawn modules, plus `NativeMilChannel` and typed scene metrics.
- `NativeMilBatchBuilder` and `NativeMilRenderDataBuilder` managed producers.
- Full export-allowlist protection; internal scene-builder C++ symbols remain
  hidden from the shared ABI.

LibreWPF currently provides:

- `WpfNativeMilSceneCompiler.BuildBatch(...)`, a reflection-free traversal of
  `IPortableVisualStateSource`, `IPortableVisualChildrenSource`,
  `IPortableDrawingContentSource`, `IPortableRenderDataSource`, and
  `IPortableBrushSource`, plus `IPortableTransformMatrixSource` for every
  transform value.
- Exact one-based render-data resource remapping, WPF sRGB-to-scRGB color
  conversion, exact ellipse and independent rounded-rectangle fill translation,
  balanced opacity/transform-scope translation, transform-resource identity
  reuse, typed `IPortablePenSource` solid/dashed line and rectangle-pen
  translation, typed solid ellipse and independent rounded-rectangle pen
  translation, typed `IPortablePrimitiveGeometrySource` translation for exact
  line/rectangle/ellipse state, a typed single-line path fallback, and native
  target construction. The portable bridge intentionally emits the matrix from
  `IPortableTransformMatrixSource` as `MatrixTransform`; it does not inspect
  WPF transform subtypes. ProGPU's additional canonical transform resources
  serve direct/source-built WPF MIL channels without weakening that typed seam.
- `Compile(...)` selection of wgpu-native or Dawn without changing the existing
  managed portable renderer.
- Fail-closed behavior for unbalanced scopes, untyped or unavailable
  transforms, clips, effects, masks, guidelines, render options, dashed
  curved pens, degenerate zero-axis asymmetric rounded rectangles, non-solid
  brushes, and all not-yet-implemented nested commands.
- SDK/package graph inclusion for `ProGPU.Backend.Native`; publication must be
  coordinated with the next ProGPU preview containing PR #139.

## Runtime lanes

| Lane | Status | Purpose |
| --- | --- | --- |
| Managed portable | Existing/default | Compatibility baseline and fallback |
| Native MIL + wgpu-native | Fixed primitive geometry slice | Shared WebGPU semantic compositor |
| Native MIL + Dawn | ARM64 ABI/build qualified | Provider-resolved Windows D3D12 validation |
| Native DirectX/DXGI facade | Planned | Measured D3D11/D3D12 interop parity |

No automatic fallback is allowed after native selection: missing typed state or
unsupported commands are observable errors. The application may explicitly
select the managed lane instead.

## Validation evidence

On the macOS ARM64 host, the ProGPU checkpoint passes:

- isolated CMake/Ninja MIL build and CTest;
- 10/10 full native tests;
- exact shared-library export allowlist;
- managed backend and package-consumer builds;
- live Metal rendering on Apple M3 Pro.

The LibreWPF checkpoint passes its focused build and twenty-five native-producer
tests:
they check exact command order, framing, handle remapping, rectangle values,
ellipse and rounded-rectangle values, scRGB brush fields, canonical opacity-
scope translation, typed visual/nested matrix-resource reuse, null transform
scope parity, rejection of untyped transform shapes, unbalanced-scope
rejection, positive non-uniform-radius emission, positive-area zero-axis
asymmetric emission, and degenerate zero-axis asymmetric rejection.
The added line cases verify exact pen/line packet offsets, solid-brush color
conversion, cap/join mapping, null-pen no-op preservation, exact dash packet
offset/interval production, filled and pen-only rectangle records, filled and
pen-only ellipse and rounded-rectangle records, invalid-dash rejection, and
rejection of untyped pen shapes. The geometry case verifies exact retained
line-geometry and `DrawGeometry` packet offsets plus geometry-local transform
identity without reflection. The primitive-geometry case additionally verifies
canonical rectangle/ellipse resource packet offsets and exact retained state;
the general-path case verifies exact local bounds (without double-applying the
geometry transform), canonical path/figure/line/quadratic/cubic record offsets,
fill rule, flags, and retained handle mapping. The arc case additionally checks
the canonical endpoint, radii, rotation, large-arc, sweep, padding, and segment
back-link fields. Untyped geometry shapes remain rejected.

The ProGPU checkpoint also passes the complete bounded Windows lane in the
Parallels integration guest: Windows 11 ARM64 build `26200.9168`, .NET SDK
`10.0.400` / runtime `10.0.11`, Visual Studio Build Tools `17.14.39`, ARM64
MSVC `19.44`, CMake `3.31.6`, Ninja `1.12.1`, and Parallels Display Adapter
WDDM driver `20.18.2641.57516` (2 GiB reported adapter memory). The exact gate
was:

```powershell
.\eng\build-progpu-native-windows.ps1 `
  -Rid win-arm64 `
  -Compiler MSVC `
  -Generator Ninja `
  -BenchmarkProfile Smoke
```

That gate built the wgpu-native and provider-resolved Dawn modules, passed all
11 native tests including MIL and Dawn ABI contracts, staged the `win-arm64`
package, and completed live D3D12 rendering/readback. The C++ sample executed
nine retained commands in five draws with 11,616 uploaded vertex bytes. The
managed native-host sample lowered 16 source commands to 13 C++ commands and
six draws, uploaded 27,464 vertex bytes plus 55,552 glyph coverage bytes, and
passed pre-render and post-render allocation/readback probes.

The adapter-specific typed glyph fallback was also exercised directly. The
Parallels D3D12 driver removes the device when its shared glyph compute shader
processes normal multi-glyph outlines, so ProGPU selects CPU R8 coverage atlas
rasterization in both native and managed compositors for that exact adapter
profile; other adapters retain GPU compute. Two- and sixteen-glyph retained
scenes then produced identical native/managed pixel hashes with zero differing
pixels and zero steady-frame managed allocations. In the 16-glyph diagnostic,
native submission was `0.5108 ms` versus `1.2558 ms` managed.

The dense mixed-picture gate is likewise explicit: 384 commands run through
the C++ renderer for eight synchronized frames at `0.2721 ms/frame`, while a
separate bounded differential scene had maximum delta 2/255, zero pixels over
3/255, and mean absolute delta `0.0000622`. This split is necessary because the
legacy managed renderer independently removes the Parallels D3D12 device on
the dense mixed scene; the C++ renderer does not. The remaining smoke matrix
passed group opacity, external/masked images, mixed semantic scenes,
mask/effect chains, vector clips, blur/drop-shadow, Overlay/ColorDodge, and
managed/C++ text shaping contracts.

Retained GPU hit-test readback remains marked `deferred-parallels-adapter`
because the Parallels blocking readback path stalls. This is a documented
adapter limitation, and neither the passing render gate nor Dawn ABI build is
being claimed as complete DirectX or MIL parity.

The affine-transform checkpoint was then requalified at ProGPU commit
`360a6f7e` with the same full Windows command. ARM64 MSVC compiled the new
matrix resource, visual-transform, and nested transform-scope code; all 11
native tests passed, including transformed semantic-state/bounds fixtures,
null no-op scopes, and transactional rejection cases. The live retained sample,
managed-host allocation probes, differential/effect/vector/text matrix, and
`win-arm64` package staging all completed. The follow-up native stress measured
`0.1244 ms/frame`, while the bounded differential again reported maximum delta
2/255 and zero pixels above 3/255. The Windows checkout was clean at the exact
qualified commit.

The typed solid-pen/`DrawLine` checkpoint was subsequently requalified at
ProGPU commit `dadb26a5` from a clean Windows checkout. Both wgpu-native and
provider-resolved Dawn modules linked for ARM64 and all 11 native tests passed,
including exact pen/line packet application, cap mapping, cap-aware affine
stroke bounds, line metrics, Dawn ABI compatibility, and transactional
animated/dashed-pen rejection. Live C++ and managed D3D12 rendering/readback,
managed post-build allocation probes, the full bounded parity matrix, and
`win-arm64` package staging completed. The mixed-picture differential remained
at maximum delta 2/255 with zero pixels over 3/255; exact Overlay and
ColorDodge hashes and all declared image, mask, effect, vector, and text
contracts remained qualified. The staged package contained both
`progpu_native.dll` and `progpu_native_dawn.dll`.

The retained-dash checkpoint was then qualified at exact ProGPU commit
`fca6c7a2` with a focused Windows integration gate over the renderer covered by
the preceding full matrix. ARM64 MSVC rebuilt both native modules and the MIL
test executable; the MIL and Dawn contract tests passed 2/2. The
project-reference package consumer built with zero warnings and ran live on
D3D12, compiling a typed dashed line through both native MIL channel exports
before completing renderer readback (`draws=1`, 16,384 pixels). The Windows
checkout remained clean at the qualified commit.

The typed rectangle-pen checkpoint was qualified at exact ProGPU commit
`89f0a838` with the same focused Windows ARM64 lane. Both native modules and
the MIL executable rebuilt under MSVC, MIL/Dawn tests passed 2/2, and the
updated project-reference package consumer built with zero warnings. Its live
D3D12 run compiled the dashed line and dashed fill-plus-stroke rectangle
through both MIL exports before completing readback (`draws=1`, 16,384
pixels). The checkout was clean at the qualified commit.

The typed solid-ellipse checkpoint was qualified at exact ProGPU commit
`f24d715f` from a clean Windows checkout. MIL/Dawn contracts passed 2/2, and
the package consumer compiled a fill-plus-solid-stroke ellipse through both
native exports before completing D3D12 readback (`draws=1`, 16,384 pixels).
During the gate, an isolated WebGPU probe identified backend-unspecified
managed instance creation as an ARM64 SYSTEM-session fault; ProGPU now supplies
wgpu-native's typed D3D12 instance extension on Windows. WebGPU-init-only,
render-only, and combined MIL/render processes then exited successfully, while
the independent C++ retained renderer completed nine commands, five draws,
11,616 uploaded vertex bytes, and readback on the Parallels D3D12 adapter.

The uniform rounded-rectangle pen checkpoint was qualified at exact ProGPU
commit `84cdcead` from a clean Windows checkout. ARM64 MSVC rebuilt both native
modules and the MIL executable, MIL/Dawn contracts passed 2/2, and the updated
project-reference package consumer built with zero warnings. It compiled a
pen-only rounded rectangle through both native exports before completing live
D3D12 rendering/readback (`draws=1`, 16,384 pixels). The independent C++
retained renderer also completed nine commands, five draws, 11,616 uploaded
vertex bytes, and readback on the Parallels D3D12 adapter.

The retained line-geometry checkpoint was qualified at exact ProGPU commit
`5c4757c0` from a clean Windows checkout. ARM64 MSVC rebuilt both native modules
and the MIL executable, MIL/Dawn contracts passed 2/2, and the updated
project-reference package consumer built with zero warnings. It compiled a
typed, transformed `LineGeometry`/`DrawGeometry` pen through both native
exports before completing live D3D12 rendering/readback (`draws=1`, 16,384
pixels). The independent C++ retained renderer also completed nine commands,
five draws, 11,616 uploaded vertex bytes, and readback on the Parallels D3D12
adapter.

The retained rectangle/ellipse-geometry checkpoint was qualified at exact
ProGPU commit `a1c0fd81` (feature implementation `bc6b5029`) from a clean
Windows checkout. ARM64 MSVC rebuilt both native modules and the MIL test
executable, and the MIL/Dawn contracts passed 2/2. The zero-warning
project-reference package consumer compiled transformed retained line,
uniform rounded-rectangle, and ellipse resources through both native MIL
exports before completing live D3D12 readback (`draws=1`, 16,384 pixels). The
independent C++ renderer again completed nine commands, five draws, 11,616
uploaded vertex bytes, and readback on the Parallels adapter. The gate required
the consumer's app-local native DLL hashes to match the freshly built modules
after detecting and replacing an older incremental-build artifact.

The retained general-path/arc implementation at exact ProGPU commit
`51550b6e` subsequently passed the complete Windows ARM64 MSVC gate. Strict
`/W4 /WX` caught and drove the correction of an implicit fill-rule enum
conversion before qualification. Both native modules linked, all 11 CTests
passed, and the live C++ and managed D3D12 samples completed rendering and
readback. The managed sample lowered 16 source commands to 13 native commands
and six draws, with 27,464 vertex-upload bytes and 55,552 coverage bytes. Its
eight-frame native stress measured `0.1235 ms/frame`; the bounded differential
remained at maximum delta 2/255 with zero pixels above 3/255 and mean
`0.0000622`. The retained path-atlas case rasterized 49 paths and stayed inside
its independent-edge contract (maximum delta 46/255, 1,048 pixels over
tolerance, mean `0.0171`). Image, mask, effect, vector, blend, text, and package
staging gates also completed.

At ProGPU package-consumer commit `a11ad9fd`, a zero-warning Windows build then
compiled a transformed retained path containing a quadratic segment and
rotated endpoint arc through both wgpu-native and Dawn MIL exports. After its
app-local DLL hashes were verified against the exact staged `51550b6e`
modules, it installed the wgpu-native semantic stream, rendered the retained
scene live on D3D12 (15 resources, two draws), and read back 16,384 pixels.
This closes the prior gap where package smoke compiled MIL but performed GPU
readback only from an unrelated immediate rectangle.

The geometry-group/combined-geometry checkpoint at exact ProGPU commit
`41af1e66` passed the complete Windows ARM64 MSVC gate from a clean checkout.
Both native modules rebuilt under strict warnings, all 11 fresh CTests passed,
and live C++/managed D3D12 rendering, readback, allocation probes, the bounded
differential, vector/image/mask/effect/text/blend contracts, and `win-arm64`
package staging all completed. The mixed differential remained at maximum
delta 2/255, zero pixels above 3/255, and mean `0.0000622`.

The updated package consumer built with zero warnings and exact SHA-256 matches
to both staged DLLs. It compiled two paths, an EvenOdd `GeometryGroup`, and an
Exclude `CombinedGeometry` through the wgpu-native and Dawn MIL exports, then
installed the wgpu-native semantic stream and completed live D3D12 readback
(17 resources, two draws, 16,384 pixels).

The retained line-path stroke checkpoint at exact ProGPU commit `70c88279`
passed the same complete Windows ARM64 MSVC gate. Both native modules rebuilt
under strict warnings and all 11 CTests passed, including closed/open topology,
gap dash caps, affine/dash/pen state, and curved/smooth/seam fail-closed cases.
Live C++ and managed D3D12 rendering/readback, allocation probes, path atlas,
image/mask/effect, semantic layer, text, and blend contracts all completed.
The mixed differential remained at maximum delta 2/255 with zero pixels above
3/255 and mean `0.0000622`; the eight-frame native diagnostic measured
`0.0902 ms/frame` on this VM.

The updated zero-warning package consumer copied app-local DLLs whose SHA-256
values exactly matched both freshly staged modules. It compiled the existing
path/group/combined graph plus a transformed dashed closed line path with a WPF
geometry gap through both native MIL exports, installed the wgpu-native stream,
and completed live D3D12 readback (18 resources, three draws, 16,384 pixels).

The fixed-child `GeometryGroup` checkpoint at exact ProGPU commit `18ccb55c`
passed the complete Windows ARM64 MSVC gate. Both native modules rebuilt under
`/W4 /WX`, all 11 CTests passed, and the independent C++ and managed hosts
completed live D3D12 rendering/readback plus the managed allocation probes.
The bounded mixed differential remained at maximum delta 2/255, zero pixels
above 3/255, and mean `0.0000622`; external and masked images, semantic
mask/effect layers, path atlas, blur/drop-shadow, text, Overlay, and ColorDodge
contracts also passed before fresh `win-arm64` package staging.

The zero-warning project-reference consumer then copied the exact staged
modules app-locally. SHA-256 was
`73fcc3871408d4642d6ace3817b30c36194e9938c36dd60f8e4d09325ec4495f`
for `progpu_native.dll` and
`709e59f97f484dc74dd5693f207dbbe96ba568d1f692b93c6df186e5d535c8c8`
for `progpu_native_dawn.dll`, with identical source/destination hashes. Both MIL
exports compiled the group containing transformed rounded-rectangle and ellipse
children; the wgpu-native stream then completed live D3D12 readback with 18
retained resources, three draws, and 16,384 pixels.

The shared fixed-operand `CombinedGeometry` checkpoint at exact ProGPU commit
`7d0fad61` passed the complete Windows ARM64 MSVC gate. The refactored shallow
fill lowerer compiled into both native modules under `/W4 /WX`, and all 11
CTests passed, including transformed fixed boolean leaves, non-uniform rounded
rectangles, preserved identity-local path operands, and the Dawn contract. Live
C++ and managed D3D12 rendering/readback, allocation probes, and the complete
bounded differential matrix passed. The mixed differential remained at maximum
delta 2/255, zero pixels above 3/255, and mean `0.0000622`; the eight-frame
native diagnostic measured `0.1618 ms/frame` before package staging.

The zero-warning project-reference consumer verified identical staged and
app-local SHA-256 values:
`288438736839fc4e673fe4dbd7a714eda8158df181c694d0efd3d92dadf1e984`
for `progpu_native.dll` and
`31b0fe54964b8163b4a1d132359e89de58367b31550020d924c681f6cc4732b6`
for `progpu_native_dawn.dll`. Both MIL exports compiled transformed fixed
rounded-rectangle and ellipse boolean operands; the wgpu-native stream then
completed live D3D12 readback with 18 resources, three draws, and 16,384 pixels.

The affine path-leaf checkpoint at exact ProGPU commit `9634af73` passed the
complete Windows ARM64 MSVC gate. Both modules rebuilt under `/W4 /WX`, and all
11 CTests passed with exact transformed line, quadratic, cubic, implicit fill
closure, conservative bounds, preserved identity-path operands, and
transformed-arc fail-closed coverage. Live C++/managed D3D12 rendering/readback,
allocation probes, and the complete differential matrix passed. The mixed
differential remained at maximum delta 2/255, zero pixels above 3/255, and mean
`0.0000622`; the eight-frame native diagnostic measured `0.0925 ms/frame`.

The zero-warning project-reference consumer verified identical staged/app-local
SHA-256 values:
`6493681ddc832c58b5d549a22cae070839268a7f66d41aae70c0c9450ba59f3f`
for `progpu_native.dll` and
`28a82155eedaa4c1b3c73b982f3c5e8f4e475687eef300946be8d6f1158d4379`
for `progpu_native_dawn.dll`. Both exports compiled a transformed
line/quadratic/cubic group leaf; live D3D12 then completed with 18 resources,
three draws, and 16,384 pixels.

The recursive `GeometryGroup` checkpoint at exact native implementation commit
`e0281b69` passed the complete Windows ARM64 MSVC gate. Both modules rebuilt
under `/W4 /WX`, all 11 CTests passed, and the MIL contract covered nested
group transform composition, outer-fill-rule ownership, groups as combined-
geometry boolean leaves, cycles, rollback, and transformed-arc fail-closed
behavior. Live C++/managed D3D12 rendering and readback, allocation probes,
text, path atlas, image/mask/effect, Overlay, ColorDodge, and the bounded
differential matrix passed. The mixed differential remained at maximum delta
2/255, zero pixels above 3/255, and mean `0.0000622`; the eight-frame native
diagnostic measured `0.1562 ms/frame` before package staging.

At package-consumer checkpoint `14603fa2`, the staged and app-local SHA-256
values matched exactly:
`e6e71dbca0b0e846de332c7bbade0362a9d19f2e4d16eef93aa73dce8640352e`
for `progpu_native.dll` and
`5a0bae5f610cfecf5a945b850a91251c725f97d7592903c78e9e2d24a5fcd79d`
for `progpu_native_dawn.dll`. Both exports compiled the 40-command,
17-resource recursive group seed, and live Parallels D3D12 readback completed
with 18 semantic resources, three draws, and 16,384 pixels.

The recursive `CombinedGeometry` checkpoint at exact ProGPU commit `8bf9a0c5`
(native implementation `6326cdf2`) passed the complete Windows ARM64 MSVC
gate. Both modules rebuilt under `/W4 /WX`, all 11 CTests passed, and the MIL
fixture verified a five-node postfix tree with exact leaf segment offsets/fill
rules, nested group/combined transform composition, operation order,
conservative bounds, and rollback. Live C++/managed D3D12 rendering/readback,
allocation probes, and the full differential matrix passed. Mixed parity
remained at maximum delta 2/255, zero pixels above 3/255, and mean `0.0000622`.

The zero-warning package consumer verified identical staged/app-local hashes:
`6ac27898f1f067854ac3e79bf415ecd41f9f79c3208a0d45618e0cf47047520d`
for `progpu_native.dll` and
`d98b7f7dd3a0315c5420ca5ca63f85354e9daec5bb8ede4468e097fd191dd906`
for `progpu_native_dawn.dll`. Both exports compiled the 42-command, 18-resource
recursive boolean seed; live Parallels D3D12 readback completed with 18
semantic resources, three draws, and 16,384 pixels.

The affine-arc recursive-geometry checkpoint at exact ProGPU commit `b9011c23`
passed the complete Windows ARM64 MSVC gate. Both native modules rebuilt under
`/W4 /WX`, all 11 CTests passed, and the MIL fixture covered reflected/sheared
arc sample equivalence, sweep reversal, singular-transform rejection, exact
translation preservation, multiple analytic group arcs, recursive boolean arc
leaves, outer fill ownership, and rollback. Live C++/managed D3D12 rendering,
allocation probes, text, images, mask/effect chains, Overlay, ColorDodge, and
the complete bounded differential matrix passed. Mixed parity stayed at maximum
delta 2/255, zero pixels above 3/255, and mean `0.0000622`; the 49-path atlas
retained its historical maximum 46/255, 1,048 pixels over tolerance, and mean
`0.017107928` contract. VM timing was noisy and is not used as qualification
evidence.

The zero-warning project-reference package consumer then copied the exact
staged DLLs and passed both a focused recursive-group arc scene and the broader
recursive group/boolean scene through the wgpu-native and Dawn exports. Each
completed live D3D12 readback with 18 semantic resources and three draws; their
coverage staging was 40,960 and 41,472 bytes respectively. SHA-256 was
`a94dab843f3f253e004e128e6ff9fc4160676691cc467cb0288a6071b0f37025`
for `progpu_native.dll` and
`a1f0c7067bd442b989708f4e7243927074a75e9419f8013d4cdef5d565b59807`
for `progpu_native_dawn.dll`.

At exact ProGPU safety commit `ef6091e9`, the close-translated-duplicate
EvenOdd-group diagnostic was converted from device removal to deterministic
fail-closed compilation. The resource update succeeds transactionally, then
`CompileScene` reports `unsupported_command` before WebGPU context/device
creation. Both modules rebuilt under `/W4 /WX`, all 11 CTests passed, and the
complete D3D12 differential gate passed again. The two supported package scenes
retained 18 resources, three draws, and 40,960/41,472 coverage bytes, while the
guarded diagnostic exited at the typed native MIL boundary without GPU
submission. Fresh staged SHA-256 values were
`5b403c179cc0aa9ae9395b2e486aa36d8574fce510b560acf4c744daba6a0a9b`
for `progpu_native.dll` and
`5a39d04f8dcccae29c093e63dd5e3d5c2effa3b4b68418763afb8f90ee2af856`
for `progpu_native_dawn.dll`. Exact rendering of that guarded overlap remains
an open parity item; non-overlapping equivalents and non-equivalent mixed
leaves retain normal analytic 8x8 GPU execution.

The isolated curved-stroke implementation at `e0a9d15f`, with MSVC portability
fix `42e05f29`, first passed the focused Windows ARM64 lane. ProGPU
`38245edd` then added exact joined and closed mixed curve composition, and
package checkpoint `3816050b` added a closed line/quadratic/cubic contour to
the retained seed. At that exact checkpoint both native modules rebuilt under
MSVC `/W4 /WX`, all 11 CTests passed, and the complete bounded D3D12 matrix
passed: independent native/managed readback, allocation probes, masks/effects,
text, path atlas, images, Overlay, ColorDodge, and the declared differential
scenes. The zero-warning package consumer compiled 46 commands and 20 channel
resources through both MIL exports; live Parallels D3D12 readback completed
with 20 semantic resources, three draws, and 41,472 coverage bytes. Exact
staged SHA-256 values were
`1c0e48225057db64eaf97eab5ba239b8be5c365525bc4b68bba58d5f906a7926`
for `progpu_native.dll` and
`efaad18f8ee89a1c53f0dc612e99371f9a3d24cbcfdf66b3129af5875ef1bb74`
for `progpu_native_dawn.dll`.

ProGPU cap implementation `4f5dcc20` then added Square, Round, and Triangle
open-curve caps as native path-cap primitives with exact endpoint tangents and
affine state. ARM64 MSVC rebuilt both modules under `/W4 /WX`, and the MIL and
Dawn contracts passed. Package checkpoint `48bea705` rendered Round/Triangle
caps on the retained analytic arc through both exports; the unchanged
46-command/20-channel-resource seed completed live Parallels D3D12 readback
with 20 semantic resources, three draws, and 41,472 coverage bytes. Exact
focused-build SHA-256 values were
`2afaa42721aa4ca9b6faa714755117d518abe23d47878613d6ea585b2dbdb164`
for `progpu_native.dll` and
`e64c515b74c131ae8e7b17eb86e4c301cbb4f07bc58d3bdb5b7644b441106309`
for `progpu_native_dawn.dll`; the complete differential matrix remains pinned
to joined-curve checkpoint `3816050b`.

ProGPU smooth-join implementation `1431509c` follows the source-built WPF
widener: `SegSmoothJoin` is captured after its incoming segment and passed as
`fRound` to the next `DoCorner`, overriding the pen join only at that endpoint.
Strict ARM64 MSVC rebuilt both modules and the MIL/Dawn contracts passed.
Package checkpoint `6868d909` marked one closed mixed-curve corner smooth;
both exports compiled it and live Parallels D3D12 readback retained 20 semantic
resources, three draws, and 41,472 coverage bytes. Exact focused-build SHA-256
values were
`b932861929989d6f847df95c0562a5629849507bace4e44dcdcc410b11e76237`
for `progpu_native.dll` and
`20806036f956a84b0b217329183f85ca8f130c7c4aba6fc4394705d6df6170e8`
for `progpu_native_dawn.dll`.

ProGPU exact rectangle-clip implementation `37f496f2` added canonical
`MILCMD_PUSH_CLIP`, transform-at-push capture, and nested target-space
intersection. Local native suites and the typed managed builder test passed;
ARM64 MSVC rebuilt both modules under `/W4 /WX`, and the MIL/Dawn contracts
passed. Package checkpoint `d22a94c9` compiled its 48 commands and 21 channel
resources through both exports. Live Parallels D3D12 readback completed with
21 semantic resources, three draws, and 41,472 coverage bytes. Exact
focused-build SHA-256 values were
`014999b22d86f2192ea56697dde3d5bc47a88991831a39636a4db26c29fccb69`
for `progpu_native.dll` and
`ba780db29da7fbedd7834768180b9d9976775532f3cf0c50c4d52cc94b56d0b7`
for `progpu_native_dawn.dll`.

ProGPU exact geometry-clip implementation `66d5f74b` then lowered retained
fixed, path, group, and combined geometry to the existing semantic vector-mask
resource. It preserves analytic line/quadratic/cubic/arc segments, recursive
group fill ownership, recursive combined-geometry boolean programs, nested
intersection order, and the transform active when WPF pushes the clip. Fixed
ellipses and rounded rectangles now use analytic quarter arcs instead of cubic
circle approximations. Clip scopes retain arena prefix counts rather than
copying segment vectors; malformed shapes fail closed and degenerate geometry
becomes an empty clip without using bounds as coverage. All ten local native
tests passed. Windows ARM64
MSVC rebuilt both modules under `/W4 /WX`, and all 11 native/Dawn CTests plus
the complete D3D12 sample and differential smoke matrix passed in Parallels.
Package checkpoint `a2502e36` nested an analytic path clip under the existing
rectangle fast-path clip; both exports compiled the unchanged 48-command and
21-channel-resource seed. Live D3D12 readback completed with 23 semantic
resources, three draws, and 41,472 coverage bytes. Exact staged SHA-256 values
were
`43e452fb73b6e103bc81ab56836c3e68d43a30b8bec7c8931df73ec8f5d05672`
for `progpu_native.dll` and
`9ca1765e660c8cc0d69c8c3eccba3d6971b9c4a05b04a5fc33975dee26e9c938`
for `progpu_native_dawn.dll`.

ProGPU dashed closed-gap implementation `c12e6d60` removed the obsolete seam
rejection for line-only closed figures. The decoder rotates the open stroked
run to the first edge after its gap, preserving one ordered polyline, dash
phase, intervals, and DashCap at both boundaries across the figure start.
Native tests assert the wrapped point sequence and typed pen state. All ten
local native tests passed; strict Windows ARM64 rebuilt both modules and the
MIL/Dawn contracts passed. Package checkpoint `0048f430` moved the existing
line geometry gap to force this seam. Both exports compiled the unchanged
48-command/21-channel-resource seed, and live D3D12 readback retained 23
semantic resources, three draws, and 41,472 coverage bytes. Exact focused
SHA-256 values were
`39a28937c25d977310597efb3c6e7f0ed9f077cd8617b2f95582d3cca58e0161`
for `progpu_native.dll` and
`daefb160737962ec81fb78238b44508a7c6a7235c8daf1bc129b0f5df2dda14a`
for `progpu_native_dawn.dll`.

ProGPU singular-affine implementation `f244dc2d` then closed the remaining
zero-determinant fill, stroke, and clip ambiguity. WPF's
`CShapeBase::GetArea` multiplies rectangle area by the absolute 2D determinant
and treats a degenerate general transform as no scannable workspace. The native
MIL compiler therefore lowers singularly transformed fixed, path, group, and
combined geometry to exact empty coverage instead of trying to invert or
factor an arc basis. Direct line strokes follow the same rule, and a singular
geometry clip becomes an exact empty clip. All ten local native tests passed.
Strict Windows ARM64 MSVC rebuilt both modules under `/W4 /WX`, and all 11
native/Dawn CTests passed in Parallels. Package checkpoint `7b91b21f` added a
typed singular `MatrixTransform` scope around direct and retained draws. Both
MIL exports compiled its 50-command, 22-channel-resource seed; live D3D12
readback retained 24 semantic resources, three visible draws, and 41,472
coverage bytes. Exact staged SHA-256 values were
`1dec50b6aef18b22f894739a9bff477a31bd0751cae1baabd9d3efc562212b65`
for `progpu_native.dll` and
`83ff9ae3133fbe9ecd789202f10ea5dfc483528f207c8ef3d34af05e45c038d9`
for `progpu_native_dawn.dll`.

ProGPU degenerate point-cap implementation `957adfdd` then matched WPF's
unstarted-widener behavior. Immediate zero-length lines and wholly degenerate
open path contours use a horizontal shape-space tangent and compose their typed
non-Flat cap halves; wholly degenerate closed contours force Round/Round. The
native hot path uses two fixed stack arrays, while nonempty dashed zero-length
strokes remain fail closed until their initial dash phase is represented
exactly. All ten local native tests passed. Strict Windows ARM64 MSVC rebuilt
both modules under `/W4 /WX`, and all 11 native/Dawn CTests passed in
Parallels. Package checkpoint `9d3d0033` added immediate and retained
open/closed degenerate strokes. Both MIL exports compiled its 52-command,
23-channel-resource seed; live D3D12 readback retained 27 semantic resources,
three draws, and 41,472 coverage bytes. Exact staged SHA-256 values were
`6afa15e6fff5a41e274674be9678d80f6bb88085078a0685eb3673d5e5467f4e`
for `progpu_native.dll` and
`560c4691baa714d366cc4817c853e41ae8d17a6140d74f06b3db5a505636d666`
for `progpu_native_dawn.dll`.

ProGPU degenerate dash-phase implementation `70b738b7` then applied WPF's
`CDashSequence::Initialize` rule to those point caps. Finite nonzero patterns
normalize positive/negative offsets over an effective even-length cycle,
repeat odd source lists, retain the preceding interval at an exact boundary,
emit caps only for an initial dash, and emit no draw for an initial gap.
Zero-total-length cycles remain fail closed. All ten local native tests passed.
Strict Windows ARM64 MSVC rebuilt both modules under `/W4 /WX`, and all 11
native/Dawn CTests passed in Parallels. Package checkpoint `61ed465d` moved the
retained degenerate path onto a boundary-offset dash resource and pen. Both MIL
exports compiled its 56-command, 25-channel-resource seed; live D3D12 readback
retained 27 semantic resources, three draws, and 41,472 coverage bytes. Exact
staged SHA-256 values were
`53d589f6580afd495e2bcb98d64c23c7acb1b450baf60027a5b7b371618774c3`
for `progpu_native.dll` and
`81a9450fc3af12677152fdb8777ab1ba346c1f5017e425858d476bd6e9076feb`
for `progpu_native_dawn.dll`.

ProGPU degenerate ellipse implementation `bbb4b2c2` then traced WPF's
`CFigureData::InitAsEllipse`: all four cubic segment types carry `SmoothJoin`,
so a zero X or Y radius is exactly one Round/Round capsule and two zero radii
form the existing point disk. Degenerate fills remain empty, immediate and
retained `EllipseGeometry` share the lowering, and geometry-local affine state
remains native. Nonempty dash patterns on a one-axis ellipse stay under the
curve-dash fail-closed gate. All ten local native tests passed. Strict Windows
ARM64 MSVC rebuilt both modules under `/W4 /WX`, and all 11 native/Dawn CTests
passed in Parallels. Package checkpoint `e909fd60` added immediate and retained
one-axis ellipses. Both exports compiled its 58-command, 26-channel-resource
seed; live D3D12 readback retained 29 semantic resources, three draws, and
41,472 coverage bytes. Exact staged SHA-256 values were
`8e235e440a980fcdf63c4770c33a2afbcd9f92a06667671daa33c7406e50457a`
for `progpu_native.dll` and
`2ecd3a808e9ee65d50cae7637e365d00820febb02a63067849ace0b73d54df58`
for `progpu_native_dawn.dll`.

ProGPU degenerate rectangle implementation `762887cb` then followed
`CRectangle::WidenToShape` and `CPlainPen::Get90DegreeBevelOffset` exactly.
WPF omits the inner stroke boundary when either original dimension cannot
contain the full pen width, leaving one outer figure. ProGPU now lowers that
figure as the exact four-/eight-edge Miter or Bevel path, or as a Round/source-
rounded path with four analytic elliptical quarter arcs and independent radius
clamps. Degenerate fills remain empty, geometry-local affine state remains on
the typed vector path, and nonempty dashed collapses remain fail closed.
Immediate and retained fixtures cover all public joins, line and point
collapses, rounded-source radii, transformed bounds, and the dash gate. All ten
local native tests passed. Strict Windows ARM64 MSVC rebuilt both modules under
`/W4 /WX`, and all 11 native/Dawn CTests passed in Parallels. Package checkpoint
`557c67fb` added immediate Round and rounded collapses plus retained transformed
rectangle geometry. Both exports compiled its 62-command, 28-channel-resource
seed; live D3D12 readback retained 32 semantic resources, issued six draws, and
staged 61,440 coverage bytes. Exact staged SHA-256 values were
`35610b8e6e6250d8d150e4a855e52a306f28af12dde286b41822baf5d5bab3eb`
for `progpu_native.dll` and
`7f3cf20154beb9c305de9b2477fbd6cb967292da61405afb35b2f46f936fa19a`
for `progpu_native_dawn.dll`.

ProGPU implementation `e17acda6` then removed the single-radius restriction
for positive independent rounded-rectangle radii. Immediate and retained
rectangles construct four exact elliptical quarter arcs plus four lines, keep
every WPF `SmoothJoin` as a native Round join, and preserve geometry-local
affine state. Fill uses the shared vector path batch and solid stroke reuses the
connected arc/line geometry lane identically through wgpu-native and Dawn.
LibreWPF checkpoint `1ba03dedc` removed the obsolete producer rejection and
now writes both typed radii into immediate and retained canonical packets;
zero-axis asymmetric cases still fail closed with an explicit typed error. All
ten local native tests and all 24 focused producer tests passed. Strict Windows
ARM64 MSVC rebuilt both modules under `/W4 /WX`, all 11 native/Dawn CTests
passed, and the project-reference package consumer built with zero warnings.
Package checkpoint `f7fef044` exercised a non-uniform immediate draw and a
non-uniform retained `RectangleGeometry` used directly and recursively. Both
exports compiled the unchanged 62-command, 28-channel-resource seed; live
D3D12 readback retained 34 semantic resources, issued ten draws, and staged
78,848 coverage bytes. Exact staged SHA-256 values were
`01dedafe1c059b043a422385f8d04085235d0f0b526be382fc8f3f97d2eb6641`
for `progpu_native.dll` and
`bcc551bf815c18ffb601d517f2c10be702fdf1e0b86a11cdd6b39b95c02b10a9`
for `progpu_native_dawn.dll`.

ProGPU native checkpoint `9a615714` next implemented WPF's
`CShape::AddRoundedRectangle` equivalence rule: with positive width and height,
either zero radius lowers immediate and retained records to the exact sharp
rectangle analytic fill and closed-polyline stroke. The degenerate zero-axis
asymmetric intersection remains fail closed because WPF sends it through the
general widener rather than the optimized rectangle path. LibreWPF producer
checkpoint `798ea56a4` now emits both typed radii for the supported immediate
and retained cases and keeps the unsupported intersection transactional with
an explicit error. All ten local native tests and all 25 focused producer tests
passed. Strict Windows ARM64 MSVC rebuilt both native modules under `/W4 /WX`,
all 11 native/Dawn CTests passed, and the project-reference package consumer
built with zero warnings. Package checkpoint `6a4f9f90` compiled an immediate
zero-axis draw and retained zero-axis `RectangleGeometry` through both exports
in a 64-command, 29-channel-resource seed. Live D3D12 readback retained 38
semantic resources, issued 11 draws, and staged 78,848 coverage bytes. Exact
qualified SHA-256 values were
`4c773f255b27ef00990ca52b89e428750a4108289de60ba5a50412b19c354d2f`
for `progpu_native.dll` and
`9b7434a0d2bea32861f2b3018078cff8dd183271da4ebffb9657aa4282b83476`
for `progpu_native_dawn.dll`; the complete native qualification is pinned at
ProGPU documentation commit `df9b0d0d`.

ProGPU implementation `f6f82b91` then added canonical static Translate, Scale,
Skew, Rotate, and variable-size ordered TransformGroup packets beside the
existing MatrixTransform path. The retained graph follows WPF float-matrix,
center, modulo-angle, and row-vector child-order semantics; live child updates
flow through nested groups without flattening. Native fixtures also cover
animation rollback, cycles, and referenced-child deletion. LibreWPF keeps its
reflection-free typed matrix producer unchanged because
`IPortableTransformMatrixSource` is the authoritative bridge contract; this
new slice accepts real canonical subtype/group resources within ProGPU itself.
All eight locally configured native suites passed. Strict Windows ARM64 MSVC
rebuilt both modules under `/W4 /WX`, all 11 native/Dawn CTests passed, and the
project-reference consumer built with zero warnings. Package checkpoint
`8bc860e4` compiled every new managed builder API through both exports in a
74-command, 34-channel-resource seed. Its identity-equivalent group preserved
the live D3D12 result at 38 semantic resources, 11 draws, and 78,848 coverage
bytes. Qualified SHA-256 values were
`301561a6f02de5a392b042f763134720a9a4b3d29f47b379c1018fc31c429d9c`
for `progpu_native.dll` and
`c3a800ba100508178a0d9f5837b07f9c6428a2bb616b1bb0d6a4708d0529da06`
for `progpu_native_dawn.dll`; the complete record is pinned at ProGPU
documentation commit `92092990`.

ProGPU implementation `04ae7747` next added canonical `DoubleResource` and
`MatrixResource` current-value packets for every transform animation field.
Native scene compilation resolves those typed dependencies on demand, so
scalar and matrix resource updates propagate through nested transform groups
without rewriting transform packets; a zero handle alone selects the packet's
base value. Wrong resource types, failed updates, and deletion of referenced
animation resources remain transactional. LibreWPF's portable producer still
publishes its typed current matrix snapshot rather than inspecting animation
or transform subtypes, while direct/source-built canonical WPF channels can use
the new native resource path. All eight locally configured native suites
passed. Following the latest-main merge, strict Windows ARM64 MSVC rebuilt both
complete modules under `/W4 /WX`, all 11 native/Dawn CTests passed, and the
project-reference consumer built with zero warnings. Package checkpoint
`d07ab05d` compiled `DoubleResource`, `MatrixResource`, and animated transform
handles through both exports in a 78-command, 36-channel-resource seed. Its
identity-equivalent current values preserved live D3D12 output at 38 semantic
resources, 11 draws, and 78,848 coverage bytes. Qualified SHA-256 values were
`a903edec8bb58e314e2738d64f8246ccc7a9f83e2d0c33755f3855ff043c233e`
for `progpu_native.dll` and
`e19d905e42d5030bf2aded0182fa1c8eb9bfc27f9a974cc3aa4d21b6507d33b0`
for `progpu_native_dawn.dll`; the complete record is pinned at ProGPU
documentation commit `856a4b98`.

ProGPU native implementation `1a937dbd` and managed builder checkpoint
`5d3b96f0` then added canonical `PointResource`, `LinearGradientBrush`, and
`RadialGradientBrush` packets. Native lowering resolves live point/double and
transform dependencies for every scene compilation; maps relative coordinates
and radii through the draw bounds; preserves focal anisotropic radial state;
implements Pad, Reflect, and Repeat; and normalizes unordered or out-of-range
stops stably. WPF's packet enums and linear ScRGB color payload are converted
explicitly into ProGPU's semantic gradient state instead of being cast across
the two contracts. Linear/radial fills and common nondegenerate pen strokes
share the existing ProGPU vector-gradient shader across wgpu-native and Dawn.

LibreWPF producer checkpoint `32234e172` consumes only
`IPortableBrushSource`/`PortableBrush` and now emits those canonical resources
for rectangle, ellipse, rounded-rectangle, and geometry fills plus typed pen
brushes. It preserves brush opacity, points/radii, stops, mapping,
interpolation, spread, and both typed transform snapshots without reflection.
Solid-brush transforms remain explicitly fail closed because canonical native
solid-brush transform resources are not implemented yet. The focused producer
suite passed 27/27.

All eight locally configured ProGPU native suites passed. Strict Windows ARM64
MSVC rebuilt both native modules under `/W4 /WX`; all 11 native/Dawn CTests
passed in the Parallels VM; the managed native-builder suite passed 6/6; and
the project-reference package consumer built with zero warnings. Focused
package gate `5db0910e` compiled a mixed solid/linear/radial scene through both
MIL exports using 15 commands and six channel resources, then installed and
rendered it on live D3D12. It produced five semantic resources, one batched
draw, zero coverage-staging bytes, a valid submission, nonblack readback, and
16,384 direct-render readback pixels. The unchanged dense path/boolean scene
still passes both export contracts but stalls intermittently in its live
Parallels render, so it is retained as a separately documented adapter issue
and is not used as gradient evidence. Exact qualified SHA-256 values were
`84f9ff3fcc3b1030fba0150891a92d176ea63d5cca7641af97d7f57d36f0cb54`
for `progpu_native.dll` and
`3779ab39f5d324f666eccc2452d0a21caf5ac5c2bea8d9eee2acede9fe8c6bf5`
for `progpu_native_dawn.dll`; the complete ProGPU record is pinned at
documentation commit `becbe01d`.

ProGPU implementation `43ef1cf5`, focused gate `64206983`, and documentation
checkpoint `837b47e9` next added canonical `GeometryDrawing` resource updates
and nested `DrawDrawing` replay. LibreWPF producer checkpoint `7a2f3bb2d`
consumes only `IPortableGeometryDrawingStateSource` and the existing typed
brush, pen, and geometry contracts; untyped drawing objects fail closed and no
reflection or private-state probes were added. The focused producer suite
passed 29/29.

All ten local ProGPU native CTests passed, the managed canonical-builder filter
passed 6/6, and the project-reference package consumer built with zero
warnings. After merging current ProGPU `main`, strict Windows ARM64 MSVC
rebuilt both native modules under `/W4 /WX` and all 11 native/Dawn CTests passed
in the Parallels VM. The focused 15-command, six-resource GeometryDrawing scene
compiled through both MIL exports and rendered on live D3D12 with three
semantic resources, one batched draw, zero coverage-staging bytes, a valid
submission, nonblack retained readback, and 16,384 direct-render pixels. Exact
qualified SHA-256 values were
`14636dca53dbecb0defd05a356642ac39cac9982d4ef918dc3d50e538cf99c3a`
for `progpu_native.dll` and
`5abd082989ae7df2b77cd727081f761d1211d5803d71cfd9102056f1a2d6034c`
for `progpu_native_dawn.dll`.

ProGPU implementation `49d448af`, focused gate `85f55ab2`, and documentation
checkpoint `848763dc` next added canonical `DrawingGroup` resource updates and
recursive retained replay. LibreWPF producer checkpoint `437f553d0` consumes
only `IPortableDrawingGroupStateSource` and
`IPortableDrawingGroupChildrenSource`; it recursively emits typed child
drawings, transform, clip, and opacity state without reflection, preserves an
empty group, rejects cycles, and fails closed for state that ProGPU does not
yet execute. The focused producer suite passed 31/31.

All ten local ProGPU native CTests passed, the managed canonical-builder filter
passed 7/7, and the project-reference package consumer built with zero
warnings. Strict Windows ARM64 MSVC rebuilt both native modules under `/W4
/WX`, and all 11 native/Dawn CTests passed in the Parallels VM. The focused
23-command, ten-resource DrawingGroup scene compiled through both MIL exports
and rendered on live D3D12 with four semantic resources, one batched draw, zero
coverage-staging bytes, a valid submission, nonblack retained readback, and
16,384 direct-render pixels. Exact qualified SHA-256 values were
`d20b7d78eff8905c7d1130c12980bbe2bc02a70337cbb4461c1279562fe624da`
for `progpu_native.dll` and
`e8c7dce855f34877abe3c211a7970235444402a37bfd940f0a4afbfea5f1a6a2`
for `progpu_native_dawn.dll`.

ProGPU implementation `6d99ced4`, focused gate `03acffe0`, expectation fix
`46175bf3`, and documentation checkpoint `5bbb7073` next added canonical
`ImageDrawing` retention plus the typed BitmapSource RGBA8 sideband. LibreWPF
producer checkpoint `2906bf396` consumes only portable image-drawing and bitmap
pixel contracts, reuses the existing typed format conversion, emits canonical
handles/packets, and supplies copied pointer-free pixels before native scene
compilation. Unsupported source kinds fail closed without reflection. The
focused producer suite passed 33/33.

All ten local ProGPU native CTests passed, the managed canonical-builder filter
passed 8/8, and the project-reference package consumer built with zero
warnings. Strict Windows ARM64 MSVC rebuilt both native modules under `/W4
/WX`, and all 11 native/Dawn CTests passed in the Parallels VM. The focused
12-command, five-resource ImageDrawing scene compiled through both MIL exports
and rendered on live D3D12 with two semantic resources, one image draw, zero
coverage-staging bytes, a valid submission, nonblack retained readback, and
16,384 direct-render pixels. Exact qualified SHA-256 values were
`d396e5bcc5b9093271878499fafabae9e0b1fb0e7db6fd9aac8379e14ea64749`
for `progpu_native.dll` and
`4fe6051479644bfe40019e5d45570f68c57aeaae5040096b2fc257fe60c405d5`
for `progpu_native_dawn.dll`.

ProGPU implementation `c8efc666`, transport optimization `6c762f2b`, focused
package gate `b21fd324`, fixture correction `fa8d6a33`, and documentation
checkpoint `834b318b` next added canonical GlyphRun/GlyphRunDrawing retention
and pointer-free SFNT font binding. LibreWPF producer checkpoint `9574b0acf`
consumes the existing typed native glyph DTO and drawing-state contracts,
reuses cached glyph index/`Vector2` position arrays and resolved `TtfFont`,
emits the exact canonical packet with a zero DirectWrite pointer, and supplies
the font bytes, face index, and style simulations before compilation. Untyped
glyphs, missing finite bounds, invalid font state, and nonidentity glyph
transforms fail closed without reflection. The focused producer suite passed
36/36.

All ten local ProGPU native CTests passed, the managed canonical-builder filter
passed 10/10, and the package consumer built with zero warnings. Strict Windows
ARM64 MSVC rebuilt both native modules under `/W4 /WX`; all 11 native/Dawn
CTests passed in the Parallels VM. The focused 14-command,
six-channel-resource glyph scene compiled direct and retained text through both
MIL exports and rendered on live D3D12 with three semantic resources, one
batched draw, 13,312 coverage-staging bytes, a valid submission, nonblack
retained readback, and 16,384 direct-render pixels. Exact qualified SHA-256
values were
`f75a6e979f52d5a606294cb1698c48efcb6a96b78e961f23820495af1697d510`
for `progpu_native.dll` and
`e95c7107f76ef1bb221b0784919fe5bd8f72ac8c004ef016db4794b8e7a5d399`
for `progpu_native_dawn.dll`. Sideways text, gradient/tile foreground brushes,
CFF/CFF2 and variable/color/bitmap glyphs, target-DPI-aware raster selection,
decorations, and incremental font registration remain explicit follow-up work.

ProGPU implementation `6071925d` and LibreWPF producer checkpoint
`0f0aabb13` next added canonical DrawingImage retention. LibreWPF recognizes
only `IPortableDrawingImageSource`, resolves its referenced drawing through the
existing typed drawing contracts, and obtains exact local content bounds
through the reflection-free `IPortableDrawingBoundsSource`/typed drawing-bounds
reader. It emits type `59` plus command `0x71`, supplies a neutral
`NativeMilRect` sideband before compilation, preserves an empty DrawingImage as
a no-op, and fails closed for incomplete or unbounded source content.

ProGPU recursively maps the retained vector drawing into ImageDrawing's
destination rectangle. Axis-preserving destination clips use semantic
scissors; arbitrary affine transforms use exact vector clip masks, so rotated
or sheared image destinations are not broadened to axis-aligned bounds. Native
tests cover missing/invalid bounds, dependency-protected deletion, mapping,
and affine clip masks. The LibreWPF producer tests passed 2/2, all ten local
native CTests passed, the canonical managed packet test passed, and the public
consumer built with zero warnings.

Strict Windows ARM64 MSVC rebuilt both native modules under `/W4 /WX`; all 11
native/Dawn CTests passed. The focused 19-command, eight-resource package scene
compiled through both exports and rendered on live D3D12 with four semantic
resources, one batched draw, zero coverage-staging bytes, a valid retained
readback, and 16,384 direct-render pixels. Qualified SHA-256 values are
`85ef5bb9c18505b97f11bf40302a8d93c50d3bd13b7afbd412fac55b7ba67cf1`
for `progpu_native.dll` and
`bae571f2a8d3cf707c92919613c8a5bece2f6e462b19c9bcd6167cd0ea66bc2c`
for `progpu_native_dawn.dll`. DrawingImage-backed ImageBrush tiling, animated
destination rectangles, incremental bounds updates, and effects/cache state
remain explicit gaps.

ProGPU checkpoint `ebe966b6` and LibreWPF producer checkpoint `2dc79a3ce`
then completed DrawingGroup bitmap-scaling propagation for retained images.
ProGPU maps canonical Unspecified/inherit, Linear/LowQuality,
Fant/HighQuality, and NearestNeighbor values onto its semantic samplers. That
checkpoint initially used Mitchell-Netravali cubic for Fant; ProGPU's later
Fant checkpoint corrects this to a dedicated bounded area-prefilter path
because WPF Fant is not a bicubic reconstruction kernel. Source-built WPF now
publishes the value as neutral `PortableBitmapScalingMode`; the native producer
requires that typed field and rejects the legacy object-only shape instead of
parsing enum names.

The portable interop and focused test builds completed with zero warnings, the
source-built PresentationCore build succeeded, two focused producer tests
passed, and all ten local native CTests passed. Strict Windows ARM64 MSVC
rebuilt both exports under `/W4 /WX`; all 11 native/Dawn CTests passed. The
DrawingImage public package scene remained green through both exports and live
D3D12 readback. Current qualified SHA-256 values are
`812312ae4d91c30a363f801985d2f881a6aa528709331f0985279756a5337790`
for `progpu_native.dll` and
`8cf312ffadac52d7109239de3fee4f25e34358bcc963e6f33965799fe3d9f607`
for `progpu_native_dawn.dll`.

ProGPU implementation `d4112930`, package checkpoint `59851d8c`, validator
fix `dab52e58`, and LibreWPF producer checkpoint `f67a22475` then added the
first exact canonical GuidelineSet slice. Source-built WPF state reaches the
producer only through `IPortableGuidelineSetSource`; no reflected collection,
property, type-name, or private-field fallback was added. Static empty sets
disable snapping, missing handles inherit, and a zero/one X/Y set is accepted.
Dynamic pairs and multiple guides fail closed until native piecewise path,
image, and glyph deformation is implemented.

The native semantic ABI remains 64 bytes: state flag bit 2 activates a typed
index into guideline resource kind 17. ProGPU evaluates WPF's scale/translate
device mapping and `CFloatFPU::OffsetToRounded` half-coordinate rule, then the
shared wgpu-native/Dawn state cursor applies the target-DPI offset to every
semantic draw family. Native tests cover canonical packet parsing, state
resources, runtime DPI adjustment, malformed inputs, and fail-closed
multi-guide state. Managed tests cover the zero-allocation builder contract;
41 focused LibreWPF producer tests cover packet output and rejection behavior.

The package consumer added `--mil-guideline-only` to JIT, NativeAOT, build,
release, and package-verification lanes. Its first Windows run found that the
scene validator's known-resource boundary still ended at kind 16 even though
the kind-17 parser was present; `dab52e58` fixed the boundary and added a
public transactional-validation regression. After the fix, all ten local
native CTests and all 11 strict Windows ARM64 native/Dawn CTests passed. Fresh
app-local DLLs compiled the 19-command/eight-resource scene through both MIL
exports and rendered on live D3D12 with five semantic resources, one draw,
zero coverage-staging bytes, nonblack retained readback, and 16,384 direct
pixels. Qualified SHA-256 values are
`9a76e7a16eb989cad3932e4d24e9e3ca1247069d8bd14114120cf073e038a270`
for `progpu_native.dll` and
`4a9e55ff26301d50138c7f02cd8be02645541ea29dd37081e2f787d2cc69c8b7`
for `progpu_native_dawn.dll`.

ProGPU implementation `f9f49b86`, package checkpoint `9ecc8a9b`, and
LibreWPF producer checkpoint `15fa518c2` next added the exact static solid
DrawingGroup opacity-mask subset. The producer reads only
`IPortableBrushSource`/`PortableBrush`, accepts a transform-free
SolidColorBrush, reuses its typed canonical brush handle, and emits that
handle in the DrawingGroup packet. No reflected brush properties, type-name
checks, or bridge-local mask shape were introduced.

ProGPU evaluates the spatially uniform mask alpha as
`brush.Opacity * brush.Color.A` and multiplies it into inherited group
opacity before recursively lowering children. This is exact across every
semantic draw family and remains live when the retained SolidColorBrush is
updated. Gradient masks are rejected as unsupported, missing/wrong resource
handles are invalid, and tile, animated, transformed, or otherwise spatially
varying masks fail closed until native group bounds and reusable mask
render-target/material resources are available.

The focused LibreWPF producer suite passed 43/43 tests, including typed solid
packet output and gradient rejection, and its Release build completed with
zero warnings and errors. The package consumer's
`--mil-drawing-group-only` lane now runs in JIT, NativeAOT, build, release,
and package verification. All ten local MIL CTests passed. Strict Windows
ARM64 MSVC rebuilt both exports under `/W4 /WX`; all 11 native/Dawn CTests
passed. Fresh app-local DLLs compiled the focused scene through both exports
and rendered on live D3D12 with four semantic resources, one draw, zero
coverage-staging bytes, nonblack retained readback, and 16,384 direct pixels.
Qualified SHA-256 values are
`3b5aa2a63c1335877e8ca49ecb37abcc705be1a9940a77fbf5f19150219f69c1`
for `progpu_native.dll` and
`341e01504aeb9380a33676704f1712cf25ee433f80554344bb080a3e0514be93`
for `progpu_native_dawn.dll`.

ProGPU implementation `55bf8628`, package checkpoint `3f5f72dc`, and
LibreWPF producer checkpoint `3a478c526` next added canonical DrawingGroup
`EdgeMode.Aliased`. Source-built WPF publishes a neutral
`PortableEdgeMode`; the native scene compiler requires that typed value when
legacy DrawingGroup state says an edge mode is present, maps it into the
canonical packet, and rejects object-only edge state. No type-name matching,
property probing, or reflection fallback was introduced.

ProGPU keeps Unspecified as inherited state and makes Aliased sticky through
nested drawing scopes. Existing shared-backend primitive and polyline flags
select aliased analytic rasterization, and vector paths use a one-sample grid
for fills, strokes, caps, and joins. Images and glyph text retain their own
sampling behavior, while exact clip masks remain antialiased geometry rather
than being reduced to aliased rectangle bounds.

Validation passed all eight configured local native suites, the zero-warning
ProGPU managed graph build, 44/44 focused LibreWPF producer tests, the
source-built PresentationCore build, the reflection audit, and the
project-reference package build. Strict Windows ARM64 MSVC rebuilt both
exports and all 11 native/Dawn CTests passed. Fresh app-local native, Dawn,
and wgpu-native DLLs compiled the upgraded `--mil-drawing-group-only` scene
through both exports; live D3D12 reported four semantic resources, one draw,
zero coverage-staging bytes, and 16,384 direct pixels.

ProGPU implementation `db057403`, package checkpoint `4af0b1c5`, and
LibreWPF producer checkpoint `053d7b5cb` then added the exact non-text subset
of canonical DrawingGroup `ClearTypeHint.Enabled`. Source-built WPF publishes
neutral `PortableClearTypeHint`; the native producer requires that typed
value, emits the existing canonical field, and rejects legacy object-only
state without reflection.

The boundary follows WPF's native implementation rather than treating the
flag as a generic antialiasing option: `PushRenderOptions` forwards Enabled to
the render target, and the software target consumes it only when deciding
whether an alpha surface may render a glyph run with ClearType. ProGPU carries
the hint as inherited native scope state, accepts vector and image subtrees
where it is an exact no-op, and returns `unsupported_command` when a nonempty
direct or retained glyph run is reached. The current shared glyph rasterizer
is grayscale, so this fail-closed boundary prevents false ClearType text
parity.

Validation passed all eight configured local native suites, the canonical
managed builder test, a zero-warning ProGPU graph build, 45/45 focused
LibreWPF producer tests, source-built PresentationCore, the reflection audit,
and the zero-warning project-reference consumer. Strict Windows ARM64 MSVC
rebuilt both exports; all 11 native/Dawn CTests passed. Fresh app-local DLLs
compiled the hinted vector scene through both exports, and live D3D12 reported
four semantic resources, one draw, zero coverage-staging bytes, and 16,384
direct pixels. True ClearType glyph rasterization remains explicit follow-up.

ProGPU implementation `7db3ddb9`, package checkpoint `0e1b4029`, and
LibreWPF producer checkpoint `6992d0b6f` then added canonical retained visual
render options through `MilCmdVisualSetRenderOptions` (`0x21`). Source-built
`Visual` publishes typed `PortableBitmapScalingMode`, `PortableEdgeMode`, and
`PortableClearTypeHint` fields in `PortableVisualState`; the reflection-free
compiler maps only those neutral values into the canonical WPF flag/payload
layout. Legacy object-only values fail closed.

The native visual graph retains and inherits those options through child
visuals and drawing scopes. Tests prove root-to-child aliased vector output,
nearest-neighbor ImageDrawing sampling through a nested DrawingGroup, and the
vector-only ClearType boundary. Unsupported CompositingMode,
TextRenderingMode, and TextHintingMode flags are rejected transactionally,
unknown flag bits are malformed, and real glyph content under visual
ClearType remains unsupported until shared native ClearType rasterization is
implemented.

Validation passed all eight configured local native suites, the canonical
managed builder test, a zero-warning ProGPU graph build, 48/48 focused
LibreWPF producer tests, source-built PresentationCore, the product reflection
audit, and the project-reference consumer build. Strict Windows ARM64 MSVC
rebuilt both exports and all 11 native/Dawn CTests passed. Fresh app-local
native, Dawn, and wgpu-native DLLs compiled the visual-to-DrawingGroup
inheritance scene through both exports; live D3D12 reported four semantic
resources, one draw, zero coverage-staging bytes, and 16,384 direct pixels.

ProGPU implementation `83f9febd` and LibreWPF producer checkpoint
`6a8fda700` next complete the text fields in canonical retained Visual command
`0x21`. `PortableVisualState` now carries neutral, typed
`PortableTextRenderingMode` and `PortableTextHintingMode` values. Source-built
`Visual` publishes them directly, and the reflection-free compiler maps them
to the exact WPF packet enums. Legacy object-only text-option state fails
closed. Canonical DrawingGroup does not contain corresponding text fields, so
DrawingGroup object-level text options remain explicitly unsupported.

The C++ scene compiler applies the inherited text values only to glyph draws.
Aliased, Grayscale, and ClearType select the existing shared ProGPU semantic
text styles used by both WebGPU/Dawn and DirectX. Auto plus
`ClearTypeHint.Enabled` selects ClearType, matching the managed ProGPU WPF
policy. Auto/Fixed hinting performs quarter-pixel X phase selection with
integer Y snapping through 24 px, integer X/Y snapping above 24 px, and leaves
rotated/sheared/reflected runs unsnapped; Animated always remains unsnapped.
Four retained outline records share each glyph's decoded SFNT path segments,
avoiding repeated outline decoding while preserving the managed compiler's
phase behavior.

Validation passed all ten local native CTests, the canonical managed packet
test, six focused LibreWPF producer/typed-contract cases, and the source-built
PresentationCore build. Package checkpoint `c7139459` adds the focused text
mode to source, package, release, and NativeAOT lanes. Strict Windows ARM64
MSVC rebuilt both exports and all 11 native/Dawn CTests passed. With fresh
native, Dawn, and wgpu-native DLLs copied app-local, the ClearType/Fixed scene
compiled through both MIL exports and live D3D12 rendered three semantic
resources, one draw, 53,248 coverage-staging bytes, and 16,384 direct pixels.
Qualified hashes are
`4703ddeaebf3ddea3ce7f503e935093e79cabb5bac5c3d26ff2890444f011fa2`
and
`9de7c391543e027410523b75dc8a394255ca1045e2359f9049f27ba387939a15`.

This is parity with ProGPU's current managed text mode and placement
implementation. It does not claim pixel identity with WPF's DirectWrite glyph
hinting or system display parameters, and CompositingMode remains a known
transactional unsupported state.

ProGPU implementation `f134b690`, package checkpoint `909d6ae8`, and
LibreWPF producer checkpoint `adcbbf5fd` next add canonical retained Visual
clipping. The typed compiler reads only `PortableVisualState.Clip` and
`ScrollableAreaClip`, resolves clip geometry through the existing portable
primitive/path contracts, emits `MilCmdVisualSetClip` (`0x1f`) and
`MilCmdVisualSetScrollableAreaClip` (`0x28`), and rejects untyped clip objects
without reflection.

The native compiler implements WPF's ordering for the currently exact subset.
It transforms the scroll rectangle in the parent scope, snaps it inward with
ceiling left/top and floor right/bottom, snaps the Visual offset through parent
device space, and then applies the regular Visual clip after the Visual offset
and transform. Axis-preserving plain RectangleGeometry becomes a shared
semantic scissor and intersects inherited clips. Rounded, rotated/sheared,
ellipse, and arbitrary path Visual clips fail closed instead of being widened
to rectangle bounds. Exact vector-mask clips and source-built layout-clip
production remain explicit follow-up work.

Validation passed all ten local native CTests, the canonical managed packet
test, two focused LibreWPF compiler/typed-contract tests, and the package
consumer build. The focused `--mil-visual-clip-only` gate is present in JIT,
NativeAOT, package verification, build, and release lanes. Strict Windows ARM64
MSVC rebuilt both exports and all 11 native/Dawn CTests passed. Fresh app-local
DLLs compiled the scene through both exports and live D3D12 rendered three
semantic resources, one draw, zero coverage bytes, and 16,384 direct pixels.
Qualified SHA-256 values are
`0261b5eda34a53db96526e7b27709b052619da561d468d5b131945ed475d54d8`
for `progpu_native.dll` and
`9068358ec8f291c261943eef95849c1eac78397bb0446b83d395e9ae5c330116`
for `progpu_native_dawn.dll`.

ProGPU implementation `070bed14`, package checkpoint `cfe13009`, and
LibreWPF producer checkpoint `11efafcca` next add the exact static solid subset
of canonical Visual alpha masks (`0x23`). LibreWPF consumes only the typed
`PortableVisualState.OpacityMask` and `IPortableBrushSource` contract, reuses
the existing canonical SolidColorBrush resource, and rejects gradient or
missing typed mask state without reflection.

ProGPU retains and protects the mask dependency and shares one uniform-alpha
resolver between Visual and DrawingGroup scopes. A transform-free,
nonanimated solid mask multiplies inherited opacity by
`Brush.Opacity * Color.A`; retained brush updates flow into the next scene
generation. Gradient, tile, transformed, animated, and spatial masks fail
closed pending a reusable ProGPU mask render target/material.

Validation passed all ten local native CTests, the canonical packet test, two
focused typed producer tests, and the zero-warning package consumer build. The
focused `--mil-visual-opacity-mask-only` gate runs in JIT, NativeAOT, package
verification, build, and release lanes. Strict Windows ARM64 MSVC rebuilt both
exports and all 11 native/Dawn CTests passed. Fresh app-local DLLs compiled the
scene through both exports and live D3D12 rendered three semantic resources,
one draw, zero coverage bytes, and 16,384 direct pixels. Qualified hashes are
`a76fe43b7e7a26b6ccaab71e80261e2704f0308c03c3e3a35abc4d80ff66038c`
for `progpu_native.dll` and
`ac396e3973a2bc5a851925dff0d97f3cf43ebaaaa7b332df797cfbc3946341cd`
for `progpu_native_dawn.dll`.

ProGPU implementation `31cd23ca`, package checkpoint `50710315`, and
LibreWPF producer checkpoint `1485782bf` next add canonical Visual guideline
collections (`0x27`). Source-built WPF already publishes cached typed
`double[]` X/Y snapshots in `PortableVisualState`; LibreWPF emits packed UInt16
counts plus float coordinates without reflection and rejects more than one
guide per axis until native piecewise deformation exists.

The native implementation shares the existing semantic GuidelineSet resource
and exact zero/one guide mapper across Visual and DrawingGroup. It preserves
WPF float conversion and scale/translate mapping, uses an empty snapping frame
for rotated/sheared scopes, and implements the Visual-specific boundary rule:
child Visual content never inherits its parent's guidelines. Native regression
coverage proves the root mapped values, the child reset, multi-guide fail-closed
behavior, clearing, and malformed padding rejection.

Validation passed all ten local native CTests, the canonical packet test, two
focused typed producer tests, and the zero-warning package consumer build. The
focused `--mil-visual-guideline-only` gate runs in JIT, NativeAOT, package
verification, build, and release lanes. Strict Windows ARM64 MSVC rebuilt both
exports and all 11 native/Dawn CTests passed. Fresh app-local DLLs compiled the
scene through both exports and live D3D12 rendered four semantic resources,
one draw, zero coverage bytes, and 16,384 direct pixels. Qualified hashes are
`36406b7138010c2c3b47e136a32efa62f07e148027640b68edafc0b67ea07318`
for `progpu_native.dll` and
`deaa21c42b156f0aa5f78bcb10593bfc53c650da69dab320cb625fdcd8a585be`
for `progpu_native_dawn.dll`.

ProGPU implementation `93929c07`, portable DTO checkpoint `7f02bd4a`, package
checkpoint `6702b9b7`, and LibreWPF producer checkpoint `b750e8af5` next add
the exact static retained Visual BlurEffect/DropShadowEffect subset. Source-
built WPF publishes Radius, KernelType, RenderingBias, direction, depth,
opacity, and color through `IPortableEffectSource`; LibreWPF emits canonical
`MilCmdVisualSetEffect` (`0x1d`), `MilCmdBlurEffect` (`0x6e`), or
`MilCmdDropShadowEffect` (`0x6f`) without reflection or host-specific effect
objects in the protocol.

The native compiler follows WPF milcore's Gaussian conversion: truncate the
logical radius, scale it by the smaller orthogonal transform row length,
truncate and cap the physical radius at 100, and use `radius / 3` as sigma.
DropShadow maps WPF's local
`(depth * cos(direction), -depth * sin(direction))` offset through the
normalized orthogonal transform and reuses ProGPU's shared blur,
shadow-composite, and source-composite passes. The retained stream and effect
descriptors are identical for wgpu-native/Dawn and DirectX.

The supported ordering is deliberately fail-closed. WPF evaluates a Visual
clip before its effect and applies opacity mask/opacity after the effect. Until
the shared semantic layer distinguishes an inflated effect-source clip from
the final composite clip, the compiler rejects an effect combined with an
active Visual clip, opacity mask, or non-unit opacity. Box blur, animated
effect fields, and sheared effective transforms are also unsupported. The
current full-target isolated layer is correct for this subset but leaves
dirty-region and intermediate-target tightening as performance work.

Validation passed all ten local native CTests, the canonical managed packet
test, 59 focused native-scene compiler tests, the typed effect-mapper case,
source-built WPF compilation, and the zero-warning project-reference consumer
build. The focused `--mil-visual-effect-only` gate is present in JIT,
NativeAOT, package verification, build, and release lanes. Strict Windows ARM64
MSVC rebuilt both exports and all 11 native/Dawn CTests passed. Fresh app-local
DLLs compiled the retained DropShadow scene through both exports and live
D3D12 rendered four semantic resources, two draws, zero coverage bytes, and
16,384 direct pixels. Qualified SHA-256 values are
`eb55945dff526f5535fd7c10795e2e0e91baea787aac6c165ab7cfea3fa4c4cf`
for `progpu_native.dll` and
`2c9c1f5fc1ee4f41b9361280d53a32201e3b4215c3cd70a0c0cf68c130766eda`
for `progpu_native_dawn.dll`.

## BitmapCache execution foundation

ProGPU checkpoint `a6394d47`, integrated with current ProGPU `main` at
`93a08f1b`, adds the first executable persistent cache primitive shared by the
wgpu-native and provider-resolved Dawn paths. Semantic layers may opt into
`PROGPU_NATIVE_SCENE_LAYER_CACHE_CONTENT` / managed
`NativeSceneLayerFlags.CacheContent`. The exact 64-byte layer ABI is unchanged:
the nonzero composite revision temporarily supplies stable owner identity and
the nonzero content revision supplies the subtree pixel version.

Cached pages use 16 owner-keyed slots separate from the 16 depth-indexed
temporary layer slots. Their texture and effect allocations participate in the
existing bounded aggregate layer budget. Cache lookup intentionally excludes
the whole-scene hash, so an unrelated sibling or outer-composite update can
rebuild the semantic bundle without redrawing the retained subtree. Content
revision, extent or texture-generation changes miss and redraw; duplicate
owners and backdrop caches fail closed; owner eviction and device recovery
invalidate completed output.

All ten portable native CTests and the managed zero-allocation builder contract
pass. The pinned WebScene/Dawn Metal hardware gate also passes on Apple M3 Pro,
including stable replay, unrelated-sibling retention, content-version redraw,
package-mode managed render/readback, and forced device-loss recreation. The
provider/Dawn revisions are
`02823bf8d2e56548b2780d6b92ae7065be1d8605` and
`710c33013c53ab2700d332c25ff51430251a8cc4`; native and managed capture SHA-256
values are
`14cba9013202f0405b43906255fcf89dc05d315a46f8fc0ad4d3d5680c265b9c` and
`cfd48921ecaf125032d11d36be132f7850d1060a90d1c71958211871becfbfac`.

This is deliberately not yet a WPF `BitmapCache` parity claim. Canonical
protocol/producer checkpoints `8217ea2d` and `b88c6e89` now decode the exact
12-byte `MilCmdVisualSetCacheMode` (`0x1e`) and 28-byte
`MilCmdBitmapCache` (`0x8d`) packets, retain type-94 cache and optional type-49
animation dependencies transactionally, and expose canonical managed builder
methods. ProGPU also publishes the package-neutral
`IPortableBitmapCacheSource`/`PortableBitmapCache` contract. Source-built
LibreWPF `BitmapCache` implements that contract from current property values,
and `WpfNativeMilSceneCompiler` emits the typed resource and Visual link without
reflection; untyped cache objects fail closed. The compiler also consumes the
existing typed `IPortableVisualBoundsSource` descendant bounds and binds them
through ProGPU's Visual-cache bounds sideband.

The local-space native subset now executes as an owner-keyed cached layer. Its
pixel revision walks typed Visual/render-data resource dependencies, preserving
the revision across an unrelated sibling update while invalidating for an
in-cache brush or animation update. Exact non-positive resolved scale suppresses
the subtree. Exact typed bounds become a zero-origin page sized by
RenderAtScale and frame DPI; missing/nonfinite/empty bounds fail closed rather
than allocating the full target. The additive
`PROGPU_NATIVE_SCENE_LAYER_CACHE_LOCAL_SPACE` flag keeps the exact 64-byte layer
ABI and uses a typed composite State resource to place and clip the cached quad.
Root offset, affine transform, opacity, render options, exact rectangle clip,
and one static guideline per axis are composite-only and no longer invalidate
pixels. Positive finite static or animated RenderAtScale values rerasterize at
the requested size. ProGPU commit `148cc5bb` also implements
WPF's SnapsToDevicePixels composite rule: exact local bounds are transformed
through outer placement, their world-space left/top are floored, and only the
cached-page composite receives the fractional correction. ProGPU commit
`bff32414` implements EnableClearType as the WPF cache raster-target policy:
false suppresses requested subpixel text to grayscale, while true permits the
existing descendant inherited or explicit ClearType mode without forcing it.
ProGPU commit `7eb17727` follows WPF `DrawCacheVisualTree` at the cache-root
boundary: root Visual state is applied to the retained bitmap composite, exact
root/ancestor rectangle clips become target-local composite scissors, static
guidelines adjust the composite transform, and empty clips suppress only the
composite. Snapping, clip, guideline, placement, and opacity updates retain the
page; EnableClearType, RenderAtScale, bounds, and descendant changes
rerasterize it. ProGPU `625a0961` adds composite-only NearestNeighbor sampling
without invalidating the page. ProGPU `a3d6b0fd` adds the first spatial
cache-root opacity-mask subset: canonical linear and radial gradient brushes
reuse the shared typed GPU brush-mask compositor without rerasterizing retained
content. Inherited mask composition, mask/effect or mask/guideline ordering,
multi-guideline deformation, nested/effect
ordering, and LibreWPF package gates remain open and fail closed where required.

The pinned provider/Dawn Metal gate passes the new lifecycle directly: first
render materializes a 24x18 page, an outer translation performs zero content
passes, and 0.5 RenderAtScale materializes a 12x9 page. The complete
package-mode managed Dawn render/readback and forced device-loss recovery also
pass at provider revision `02823bf8d2e56548b2780d6b92ae7065be1d8605` and
Dawn revision `710c33013c53ab2700d332c25ff51430251a8cc4`.
The post-raster regression changes only the local-page composite clip and
observes zero content passes on the next live Metal frame. All 12
provider-configured native CTests, the base export allowlist, package-mode
managed Dawn readback, and forced device-loss recovery pass with unchanged
capture hashes.

The live D3D12 gate for this checkpoint is complete. On 2026-08-25 the
Parallels Windows 11 ARM64 VM checked out clean ProGPU commit `dd3857a4`
(LibreWPF integration commit `ea9aaebb6`), rebuilt both native modules with
strict MSVC `/W4 /WX`, passed all 11 native/Dawn CTests and both export
contracts, and staged the win-arm64 package. The independent C++ and managed
samples selected `Parallels Display Adapter (WDDM)` with D3D12 and passed live
render/allocation/readback checks; the managed retained sample lowered 16
source commands to 13 native commands and six draws. The bounded differential
smoke matrix and managed/C++ text-shaping parity passed as well. Packaged DLL
SHA-256 values are
`D17701FB0669A241183AF064080A1FD1ADD29AE1B000A531CCE5E7307B2650C6`
(`progpu_native.dll`) and
`02414A74F7C6CB1A84F2846D5E5B701102E4812B5AEFCBA25688AE881592BD42`
(`progpu_native_dawn.dll`). This closes Windows qualification for the
preceding target-space subset. Strict Windows qualification for the new
local-space/RenderAtScale checkpoint remains separate.

The new local-space/RenderAtScale checkpoint subsequently passed that strict
gate on 2026-08-25. The Parallels Windows 11 ARM64 guest checked out clean
ProGPU commit `1a75a958` (native implementation `dee81dff`; LibreWPF tracking
commit `6d5db5652`), rebuilt both modules with MSVC `/W4 /WX`, passed all 11
native/Dawn CTests and both export contracts, and staged the win-arm64 package.
The independent C++ and managed samples selected the live
`Parallels Display Adapter (WDDM)` D3D12 adapter and completed retained render,
allocation, and readback checks. The expected Parallels-only retained GPU
hit-test deferral remained isolated to the optional probe. The bounded D3D12
differential smoke matrix and managed/C++ text-shaping parity passed; final
Overlay and ColorDodge scenes were pixel-exact. Packaged DLL SHA-256 values are
`FBC4EC3D71A1BB63CA2DE3A092C7F25D63747C47C40AF7FC9D19EA4A379FE5B4`
(`progpu_native.dll`) and
`ECC81DF8437FE0C4EC8BB18D9692E248048F04270471E04DC053BF7610E5B173`
(`progpu_native_dawn.dll`). This closes Windows DirectX qualification for the
current local-space cache subset; post-raster clip/mask/guideline ordering and
LibreWPF package-mode SDK coverage remain.

The combined native snapping/ClearType checkpoint subsequently passed the same
strict gate on 2026-08-25. The Parallels Windows 11 ARM64 guest checked out
clean ProGPU commit `bff32414` (LibreWPF tracking commit `da5f85ed6`), rebuilt
both native modules with MSVC `/W4 /WX`, passed all 11 native/Dawn CTests and
both export contracts, and staged the nine-file win-arm64 package. The
independent C++ and managed samples selected the live
`Parallels Display Adapter (WDDM)` D3D12 adapter and completed retained render,
allocation, and pixel-readback checks. The expected Parallels-only retained GPU
hit-test deferral remained isolated to the optional probe. The bounded D3D12
differential smoke matrix passed group-opacity, zero-copy image/mask, retained
semantic, mask/effect, path-atlas, image-effect, Overlay, ColorDodge, and
managed/C++ text-shaping contracts; Overlay was pixel-exact. Packaged DLL
SHA-256 values are
`768BE3DB0A8970334FE6B4574370CCC96E63A653C94B9ECBD769FAEAD3825891`
(`progpu_native.dll`) and
`FC95E25FF8E5313D6151F199E236D376E28C9FF7243AD0887F8FA360B89AA73E`
(`progpu_native_dawn.dll`). This closes Windows DirectX qualification for the
local-space, RenderAtScale, SnapsToDevicePixels, and EnableClearType cache
subset.

The subsequent `7eb17727` rectangle-clip and single-guideline composite
checkpoint passed its own strict Windows gate on 2026-08-26 from a clean
detached checkout. MSVC rebuilt the modified compiler, validator, executor,
and both native modules under `/W4 /WX`; all 11 native/Dawn CTests and both
export contracts passed. The independent C++ and managed samples used the live
`Parallels Display Adapter (WDDM)` D3D12 adapter and passed retained rendering,
allocation, and pixel-readback checks, while both managed builds completed
with zero warnings. The complete bounded differential matrix also passed,
including managed/C++ text shaping; group opacity, zero-copy image, Overlay,
and ColorDodge were pixel-exact. The staged nine-file win-arm64 package hashes
are
`B2258721E6AFA621ADB5AC6E284DBF392342288A5620B22156667EE357E7D710`
(`progpu_native.dll`) and
`73327D9C482EEE4F387789A9B2561220FD41C8659A4C781AF094CBFC8FB2C3E1`
(`progpu_native_dawn.dll`). Exact rectangle post-raster clips, one static
composite guideline per axis, and the cache-root raster/composite separation
are therefore qualified on DirectX as well as Metal/Dawn. Spatial masks,
multi-guideline behavior, nested-cache/effect ordering, and LibreWPF
package-mode SDK coverage remain.

ProGPU then merged latest `main` at `0e3c9452` and added exact cache-composite
NearestNeighbor sampling in `625a0961`. LibreWPF's existing reflection-free
typed `PortableVisualState.BitmapScalingMode` producer already emits the
canonical root Visual render option, so no WPF bridge callback or object-shape
adapter is required. The native MIL compiler maps only NearestNeighbor to the
additive local-cache-only `CACHE_NEAREST` layer flag; each retained page owns
linear and nearest bindings over the same texture, and a sampling-only change
keeps the content revision/page intact. C++, managed, and serialized-scene
validation reject the flag without a local cache. Fant sampling remains
fail closed at that historical checkpoint pending the shared reconstruction
path; the Fant checkpoint below supersedes that limitation. All 12
provider-configured native CTests passed, including a live Metal/Dawn switch to
nearest with zero
content passes; the managed zero-allocation builder regression and both export
allowlists also passed.

That exact checkpoint passed strict Windows ARM64/D3D12 qualification on
2026-08-26 from a clean detached checkout of ProGPU `625a0961`. ARM64 MSVC
rebuilt the modified MIL compiler, validators, retained-layer compositor, and
both native modules under `/W4 /WX`; all 11 native/Dawn CTests and both export
contracts passed. The independent C++ and managed samples selected the live
`Parallels Display Adapter (WDDM)` D3D12 adapter and completed retained render,
allocation, and pixel-readback checks. The managed sample and benchmark builds
were repeated serially with `-m:1 -nr:false` to remain inside the Parallels VM
memory envelope, completing with zero warnings and zero errors. The complete
bounded differential smoke matrix passed mixed-picture native stress and
bounded managed parity, group opacity, zero-copy image/mask, retained semantic,
mask/effect, path-atlas, image-effect, Overlay, ColorDodge, and managed/C++
text-shaping contracts. Group opacity, zero-copy image, Overlay, and ColorDodge
were pixel-exact. The staged win-arm64 package DLL SHA-256 values are
`8CFCBD3BFCC362611EC4A1DB0F17684838C2E1EA1DC30F3EA994B04C63709E2D`
(`progpu_native.dll`) and
`9BFB20223CCC046B2280B2B3A8F25E353C916FB001118B3DC5DC47C744968D5F`
(`progpu_native_dawn.dll`). Exact linear/NearestNeighbor retained-page
sampling is therefore qualified on DirectX as well as Metal/Dawn without
rerasterizing the page for a sampling-only change.

ProGPU spatial-mask implementation `a3d6b0fd`, dedicated live gate `7497ff59`,
and qualification documentation `5852fcaa`, pinned by this LibreWPF revision,
extend that local-cache layer without changing its 64-byte ABI. The native MIL
compiler resolves a cache-root canonical linear or radial opacity brush against
the exact typed Visual descendant bounds and records an existing
`LAYER_MASK_BRUSH` resource for the page composite. The mask receives the same
outer transform and SnapsToDevicePixels correction as the cached quad. Its
brush opacity, stops, animations, mapping mode, and typed transforms are
composite-only dependencies, so changing them preserves the content revision
and skips the next content pass. Transform-free solid masks continue to fold
into uniform layer opacity.

No LibreWPF reflection or compatibility adapter was added: source-built WPF's
existing typed `PortableVisualState.OpacityMask` and canonical MIL brush packet
producer already carry the required state. C++ validation, managed stream
validation, and the shared WebGPU/DirectX executor now accept a typed mask on a
local retained layer while continuing to reject local-cache effects. The local
gate passes all 8 portable native CTests, all 12 provider/Dawn CTests, both
export allowlists, and 3,823 managed tests. The live provider regression proves
that a mask-only opacity change performs zero content passes and one composite
pass. MIL regressions cover both linear and radial masks and explicitly reject
the unrepresented gradient-mask plus guideline ordering.

The dedicated backend-neutral live scene then passed on both Apple M3 Pro
Metal and the Parallels Display Adapter D3D12 backend. It rendered one 24x18
owner-keyed local page through a linear gradient mask, changed only mask
opacity from 1.0 to 0.5, observed `1/1` then `0/1` content/composite passes, and
produced identical sampled green-channel evidence `0/112 -> 56`. The clean
detached Windows checkout of exact ProGPU commit `7497ff59` also passed ARM64
MSVC `/W4 /WX`, all 11 native/Dawn CTests, both export contracts, zero-warning
managed builds, independent C++/managed D3D12 samples, the full bounded parity
matrix, and package staging. The staged DLL SHA-256 values are
`8B1C5FCD58EA5794D14C9F6E75F84B5BDFF890A3B8BAA9054B195D2BC6F63622`
(`progpu_native.dll`) and
`E6920A87784984ED82F1E172DD441B8909499DCA8CEC149B145C45B811236D89`
(`progpu_native_dawn.dll`). This qualifies composite-only linear spatial-mask
changes on DirectX and Metal; radial normalization remains covered by the MIL
regression.

ProGPU native Fant implementation `e027c942`, portable qualification update
`ac38938b`, and final documentation commit `9ff48063`, pinned by this LibreWPF
revision, correct WPF HighQuality/Fant without adding a WPF-side adapter.
Source-built WPF's existing reflection-free
`PortableVisualState.BitmapScalingMode` producer already emits the canonical
value. The C++ MIL compiler now maps it to the additive local-cache-only
`CACHE_FANT` flag, while typed immediate and retained images use canonical
`PROGPU_NATIVE_IMAGE_SAMPLING_FANT`. The separate ProGPU `CUBIC` value remains
Mitchell-Netravali.

The shared WebGPU/DirectX texture shader follows WPF's sqrt(2) prefilter
activation threshold and integrates one destination-pixel parallelogram with a
fixed stratified 4x4 footprint, including rotation and shear. This is a bounded
GPU approximation of WIC Fant, not a byte-exact WIC-output claim. Sampling-only
linear/nearest/Fant changes preserve the retained page content revision. C++,
managed, and serialized validators require local-cache state and reject
nearest-plus-Fant conflicts.

All 12 native/provider CTests, both export allowlists, and the focused managed
scene/image contracts pass locally. The Apple M3 Pro Metal gate keeps the page
at `passes=1/1 -> 0/1` and changes stripe red min/mean/max from `43/117/213` to
`106/130/149`. The clean detached Windows qualification at exact ProGPU commit
`ac38938b` passed strict ARM64 MSVC, all 11 native/Dawn CTests, both export
contracts, zero-warning managed builds, independent C++/managed D3D12 samples,
the complete bounded differential matrix, and package staging. On Parallels
D3D12 the same gate kept `passes=1/1 -> 0/1` and changed stripe evidence from
`0/63/255` to `64/135/191`. Staged DLL SHA-256 values are
`FACAE389AC4EC1A818004D3C881B301342BC22C1C3E3E145B5660E03715FFF65`
(`progpu_native.dll`) and
`A39DCD04927D02D7EDFB08E747AB08C7CF8FAEE620A45B52162CC1C58169C0FA`
(`progpu_native_dawn.dll`).

ProGPU analysis checkpoint `84b917a0` fixes the
multi-guideline boundary against WPF `CSnappingFrame` and
`CShapeClipperForFEB`. Zero/one guide remains a uniform transform offset;
multiple sorted static guides require a nearest-guide offset per transformed
point, with exact midpoint ties choosing the lower guide. Implementation commit
`1cd1e5dd`, followed by current-`main` merge head `d99acbc8`, adds the first
explicit local-cache-composite-only capability. It bounds counts to the WPF
UInt16 packet range, requires finite sorted axes, preserves negative-scale
ordering, snaps each of the four absolute retained-page vertices, and rejects
that State from ordinary SAVE, PUSH, and draw commands. The managed builder
writes directly into its caller-owned arena, with no reflection or large
temporary stack payload.

Native, managed, and MIL regressions cover malformed resources, midpoint
selection, mapped and negative-scale coordinates, ordinary-State rejection,
and cache content-revision stability. The live Apple M3 Pro Metal and Parallels
Display Adapter D3D12 gates both keep the page at `passes=1/1 -> 0/1` while
deforming its red extent from `[10,8]-[25,15]` to `[11,9]-[25,15]`, changing
red sum from `32640` to `26775`, with 23 changed pixels.

The clean detached Windows run at exact latest-main-integrated ProGPU commit
`d99acbc8` passed strict ARM64 MSVC `/W4 /WX`, all 11 native/Dawn CTests, both
export allowlists, zero-warning managed Release builds, independent C++ and
managed D3D12 allocation/readback samples, the complete bounded differential
smoke matrix, and nine-file package staging. Qualified win-arm64 SHA-256 values
are `F65DA33BFCE4242A869369052E4C52C3CDB67951988FFCB740E85173A74D2C75`
(`progpu_native.dll`) and
`E445C3DED9FC741EFECEDC4764A5AE84C120A4FECD15293058504C39ED8E400F`
(`progpu_native_dawn.dll`). ProGPU documentation commit `570b658e` records the
evidence. General path/primitive point deformation and spatial-mask plus
multi-guide ordering remain fail closed.

ProGPU implementation commit `b3b4f784`, followed by qualification/document
checkpoint `4c8525c8` pinned here, adds the bounded nested cache/effect ordering
slice without a managed WPF workaround. WPF `DrawCacheVisualTree` ignores the
cache root's own state but performs normal child walks, so the native MIL
compiler emits parent local cache, child effect layer, then child local cache.
Uniform child opacity lives on the isolated child-page composite and executes
before the outer Gaussian/drop-shadow effect. Uncached opacity/effect,
clip/effect, and spatial-mask/effect combinations remain typed fail-closed
gaps.

The parent cache content revision includes descendant placement and effect
generation, while the child cache content revision excludes its own outer
state. A child move or effect update therefore misses the parent but reuses
the child page; moving the parent root retains both. Native MIL tests assert
that nesting and revision split across child movement, parent movement, and
effect mutation.

The live Apple M3 Pro Metal and Parallels Display Adapter D3D12 gates produced
identical evidence. First/stable/child-moved frames executed `3 -> 0 -> 2`
content/effect-input passes and `2 -> 0 -> 2` effect passes. Stable pixels were
byte-identical; child movement changed 572 pixels, shifted the nonzero extent
from `[3,3]-[28,24]` to `[8,3]-[33,24]`, and preserved red sum 24,576. The
clean detached Windows run at exact ProGPU code commit `b3b4f784` passed ARM64
MSVC `/W4 /WX`, all 11 native/Dawn CTests, both export allowlists, two
zero-warning managed Release builds, both independent D3D12 samples, the full
bounded smoke matrix, and nine-file package staging. Qualified SHA-256 values
are `424D1A11F6D398D1AC1F206B2686345882143DEBE7D3140037FBBD0D7EF09EBA`
(`progpu_native.dll`) and
`A4BB52C578C71DCDBE3297F9CC7D1DEC4BD13D4046F600D1C6966AA60EC0FD2A`
(`progpu_native_dawn.dll`).

ProGPU implementation `bb550c79` and qualification documentation `c4609e14`,
pinned here, extend that ordering to one typed linear/radial cache-root spatial
opacity mask. The effect remains outer; uniform opacity and the brush mask stay
on the inner local-cache composite, so both execute once on the isolated page
before Gaussian blur or drop shadow. LibreWPF's existing typed visual/brush
state already emits the required canonical data; no bridge reflection,
callback, or managed rendering fallback was added.

MIL tests assert effect -> masked cache ordering, mask/opacity placement, and
unchanged cache content revision. The live Apple M3 Pro Metal and Parallels
D3D12 gates match exactly: first/stable/mask-changed content passes are
`2 -> 1 -> 1`, effect passes remain `2 -> 2 -> 2`, stable pixels are identical,
and halving only mask opacity changes 164 pixels, narrows extent
`[21,7]-[31,24] -> [22,7]-[30,24]`, and changes red sum `756 -> 372` while
retaining the source page.

The clean detached Windows run at exact code commit `bb550c79` passed ARM64
MSVC `/W4 /WX`, all 11 native/Dawn CTests, both export allowlists, two
zero-warning managed builds, both D3D12 samples, the complete bounded smoke
matrix, and nine-file staging. Qualified SHA-256 values are
`FFA0223D369BF89F48E4A9A271318BE7B057022899A3D8B8AA2532BDA44F3C30`
(`progpu_native.dll`) and
`7A98FA8A4A69E11886ED6879D430295BAD370F88D463B4E638847D1F8CBE6836`
(`progpu_native_dawn.dll`). Inherited/combined masks, mask plus guideline
ordering, arbitrary geometry clip/effect output regions, and inflated
effect-bound tightening remain open.

ProGPU implementation `234687b7` and qualification documentation `af85479b`,
pinned here, add final rectangle clipping after effect sampling without a
managed WPF workaround. The append-only `LAYER_COMPOSITE_STATE` flag reuses the
unchanged 64-byte layer record's `reserved0` field for a typed identity-
transform, unit-opacity, clip-only State on a materialized non-local layer.
The shared WebGPU/DirectX executor applies its scissor while restoring the
layer, after Gaussian blur or drop shadow has sampled the full isolated input.
Builders and serialized-scene validation reject local-cache, transformed,
masked, guideline-bearing, non-materialized, missing, and wrong-kind uses.

The native MIL compiler moves the combined current rectangle clip from the
ordinary draw State to the outer effect composite. When a local cache provides
the effect input, its inner composite State omits the clip while retaining its
uniform opacity and supported spatial mask. A zero-radius no-op blur with a
clip still emits a clip-only isolation layer, so it cannot silently drop state.
Uncached opacity/effect and arbitrary geometry clip/effect combinations remain
typed fail-closed gaps.

Native/managed builder and MIL tests cover uncached and cached ordering,
canonical validation, inner-cache clip omission, and the zero-radius edge. The
live Apple M3 Pro Metal and Parallels Display Adapter D3D12 gates match exactly:
content/effect-input passes are `2 -> 1 -> 1`, Gaussian passes are
`2 -> 2 -> 2` with later effect-cache hits, and the stable output is byte-
identical. Narrowing only the final clip changes 428 pixels and crops extent
`[6,4]-[33,27] -> [14,8]-[25,21]`; pixels inside the rectangle remain
byte-identical to the already blurred wide output and every outside pixel is
black, proving post-effect clipping.

The clean detached Windows run at exact ProGPU code commit `234687b7` passed
ARM64 MSVC `/W4 /WX`, all 11 native/Dawn CTests, both export allowlists, two
zero-warning managed Release builds, both D3D12 samples, the complete bounded
smoke matrix, and nine-file staging. Qualified SHA-256 values are
`86062D03035829A8E6B7DA8CC52EC63FB9E4F3BEA15A91C4C8530B5AFC89D952`
(`progpu_native.dll`) and
`CF01D087373FD1580EBE1A5B72BC2314CDCE2AEFA4FE02DBF782C88F3DB11C91`
(`progpu_native_dawn.dll`).

ProGPU implementation `ef811a7c` and qualification documentation `fdd2b82e`,
pinned here, bound temporary Visual effect isolation without a managed
rendering workaround or a new protocol packet. The existing
`set_visual_cache_bounds` symbol is retained for ABI compatibility, but its
typed payload is the source-built Visual descendant extent and now serves both
BitmapCache and effect planning. `WpfNativeMilSceneCompiler` publishes the
existing `IPortableVisualBoundsSource` snapshot for every cache or effect
Visual and fails closed when it is missing. This keeps the bridge reflection-
free and prevents real LibreWPF effects from silently allocating a full target;
older direct native consumers may omit the optional sideband and retain the
conservative effect behavior.

ProGPU transforms the descendant rectangle through the effective Visual state.
Blur expands it by WPF's resolved physical kernel radius; DropShadow unions the
source with the offset, inflated shadow; a zero-radius effect kept for final
clipping uses the exact source extent. The independent composite clip remains
outside those bounds so effect sampling is never truncated. Native MIL tests
cover unbounded compatibility plus exact blur, shadow, and zero-radius cases,
while LibreWPF tests cover both typed production and missing-bounds rejection.

The live Apple M3 Pro Metal `--semantic-bounded-effect` gate renders a full and
bounded Gaussian layer with byte-identical output. Allocation falls from
`96x64` to `28x24`, layer bytes from 24,576 to 2,688, and effect bytes from
73,728 to 8,064; both outputs have extent `[24,14]-[51,37]`, red sum 48,960,
and zero changed pixels. The Windows DirectX/D3D12 run produced the same
result.

The clean detached Windows run at exact ProGPU code commit `ef811a7c` passed
ARM64 MSVC `/W4 /WX`, all 11 native/Dawn CTests, both export allowlists, two
zero-warning managed Release builds, both D3D12 allocation/readback samples,
the complete bounded smoke matrix, and nine-file staging. The D3D12 bounded-
effect metrics and pixels matched Metal exactly. Qualified win-arm64 SHA-256
values are
`09B17325EFC71E90131AAA4538F883C4D3C9EAFFA3A54539BCE50E18FB07F47B`
(`progpu_native.dll`) and
`CE4A5E6E81F11DB499E8B160A550A14701F4D050EC80AC484C5CEEA57BA92F0A`
(`progpu_native_dawn.dll`).

The following checkpoint adds the uncached uniform-opacity-before-effect
subset primarily in ProGPU. For an effect Visual without inherited opacity,
the native MIL compiler emits the bounded outer effect followed by a bounded
inner `FORCE_ISOLATION` layer carrying the Visual's uniform alpha. It resets
draw/child opacity to one, so overlapping primitives are composed first and
attenuated once before Gaussian blur or drop shadow samples the result. A zero-
radius blur retains the opacity group, and the already separate rectangle clip
remains outside the effect. No protocol expansion or managed renderer fallback
is involved.

LibreWPF removes only its stale rejection of typed `HasOpacity + HasEffect`;
the existing portable Visual state and exact descendant-bounds sideband carry
all data. Inherited non-unit opacity and spatial masks still fail closed because
their owner boundaries cannot be moved across descendant effects. Native tests
cover nesting, bounds, clip placement, zero-radius retention, and inherited-
opacity rejection; all 63 focused LibreWPF compiler tests pass.

The Apple M3 Pro Metal `--semantic-uncached-opacity-effect` gate compares two
overlapping opaque rectangles under group opacity with a half-opacity union
reference and an incorrect per-primitive-alpha variant. Group/reference pixels
are byte-identical; the incorrect variant changes 420 pixels and raises the
overlap sample `128 -> 188`. The group executes `2/2/2` content/composite/effect
passes at extent `[5,5]-[46,30]`, red sum 65,536. The same gate is wired into
the Windows D3D12 qualification.

The implementation is ProGPU commit `a47d80b5`; qualification documentation is
commit `570cdf18`. From clean detached implementation commit `a47d80b5`, the
Parallels Display Adapter D3D12 gate produced the same `2/2/2`, sample,
changed-pixel, extent, and red-sum results as Metal. The complete Windows ARM64
MSVC `/W4 /WX` lane also passed 11/11 native/Dawn CTests, both export
allowlists, two zero-warning managed Release builds, independent C++ and
managed D3D12 samples, the bounded smoke matrix, and package staging. Qualified
SHA-256 values are
`07E97B185A066124719A2593CBE2AD7762B9FF00FEB406255B428FC7CF2BA85D`
(`progpu_native.dll`) and
`35744D6CAF0F8C7789D7DE0E7EFA0985529A27217C7F65613BD0889487D879B2`
(`progpu_native_dawn.dll`).

The typed effect-clip producer checkpoint advances ProGPU to `3403e841` and
enables the existing final-output clip path for source-built WPF. LibreWPF now
accepts `HasClip + HasEffect` only when the clip publishes an exact
`IPortablePrimitiveGeometrySource` rectangle with zero radii and an
axis-preserving local matrix. The typed `ScrollableAreaClip` rectangle is also
accepted. ProGPU repeats the authoritative check after composing ancestor
transforms, intersects both rectangles, and attaches the result outside the
effect so blur/drop-shadow input remains unclipped.

This follows WPF's own description of `ScrollableAreaClip` as a simple
world-space, pixel-aligned rectangle and its rule that rotation above the
Visual disables accelerated scrolling. Ellipse/path/rounded/sheared clips and
rotated ancestor scroll clips fail closed; neither side broadens them to an
AABB. Native regressions cover one-axis rounding, transformed scroll clips, and
combined effect clipping. The reflection-free LibreWPF suite covers typed
acceptance and inexact-shape rejection; all 65 focused compiler tests pass with
a zero-warning incremental Release build.

ProGPU qualification documentation is commit `21fe1aa0`. Exact implementation
commit `3403e841` passes all 10 local native CTests, the base export, the Metal
sample, and the Apple M3 Pro final-clip gate. A clean detached Parallels run
passes strict Windows ARM64 MSVC `/W4 /WX`, 11/11 native/Dawn CTests, both
exports, two zero-warning managed Release builds, independent C++ and managed
D3D12 samples, the full bounded smoke matrix, and package staging. Metal and
D3D12 report identical effect-clip evidence: content passes `2 -> 1 -> 1`,
effect passes `2 -> 2 -> 2`, 428 changed pixels, extent
`[6,4]-[33,27] -> [14,8]-[25,21]`, and red sum `48,960 -> 32,960`. Qualified
SHA-256 values are
`991F9301B71660FEF89DDA9A4D1E6400D01C92EFAD10B521D3C58BB12482D0F9`
(`progpu_native.dll`) and
`616B0650CF74D5D84FB45D908DB6285A82760B59E6A8D56313D827B6885038C7`
(`progpu_native_dawn.dll`).

The uncached spatial-mask-before-effect checkpoint advances ProGPU to
`3c22b004`. LibreWPF now accepts typed solid, linear-gradient, and radial-
gradient Visual opacity masks whenever a BitmapCache or effect supplies the
required isolation boundary; an ordinary uncached Visual without either still
rejects a spatial mask. The existing portable brush DTO and descendant-bounds
sideband carry all data without reflection or a managed renderer fallback.

For an uncached effect, ProGPU resolves the typed gradient to its reusable
semantic brush-mask resource and combines it with uniform alpha on the bounded
inner `FORCE_ISOLATION` layer. The outer blur/drop-shadow samples that completed
source. Cached Visuals retain the already-qualified local-page mask path, while
solid masks collapse to uniform alpha. Inherited mask/opacity ownership still
fails closed. Native MIL tests assert the uncached layer order/bounds/mask and
absence of cache flags; all 66 focused LibreWPF compiler tests pass.

The expanded Apple M3 Pro Metal `--semantic-uncached-opacity-effect` gate keeps
the previous opacity proof and adds mask-ordering evidence: `2/2/2`
content/composite/effect passes, red samples `36/217`, extent
`[7,5]-[47,30]`, and red sum 65,264. Reversing the mask to post-effect changes
666 pixels and yields `[10,10]-[41,25]`, red sum 56,038.

ProGPU qualification documentation is commit `ab6d9d06`. A clean detached
`3c22b004` Windows ARM64 MSVC `/W4 /WX` run passes 11/11 native/Dawn CTests,
both export allowlists, two zero-warning managed Release builds, independent
C++ and managed D3D12 allocation/readback, the complete bounded differential
smoke matrix, and nine-file package staging. The Parallels Display Adapter
D3D12 proof is identical to Metal: `2/2/2` passes, samples `36/217`, masked
extent `[7,5]-[47,30]` with red sum 65,264, and 666 wrong-order changed pixels
at `[10,10]-[41,25]` with red sum 56,038. Qualified SHA-256 values are
`F7B72CAF58C8B4675A3B26FBBC4B62D314F26737CFFC9DC625F1E2BF640A681C`
(`progpu_native.dll`) and
`6921A4037372B7A327370DA2035750FD48E791164BD2B5E0407E05F3A01C4A14`
(`progpu_native_dawn.dll`).

The inherited-opacity ownership checkpoint advances ProGPU to `a3affb9d`.
WPF pushes each Visual's non-unit opacity as that node's group boundary before
walking its children; a child Visual then owns its effect outside its own local
opacity/mask layer. LibreWPF now publishes exact typed descendant bounds for
every non-unit-opacity Visual, in addition to cache/effect owners, so native MIL
can retain the ancestor boundary instead of multiplying its alpha into child
draw state.

ProGPU emits a bounded outer `FORCE_ISOLATION` opacity layer for an uncached
ancestor, resets the isolated local alpha after cache planning, and compiles
the descendant effect and any child-local opacity/mask inside it. Missing typed
bounds fail closed on the LibreWPF producer. The provider retains its explicit
compatibility behavior for simple direct-native callers, but still rejects an
unresolved inherited-opacity/effect boundary. No reflection, managed rendering
fallback, new callback, ABI change, or DirectX-only path is involved.

All eight portable native CTests, the base export allowlist, a zero-warning
managed benchmark build, 68/68 focused LibreWPF compiler tests, and the Apple
M3 Pro Metal ownership gate pass. Metal executes `2/2/2`
content/composite/effect passes; correct ancestor ownership keeps
exclusive/overlap samples at `128/128`, extent `[4,4]-[41,31]`, red sum 67,186.
The deliberately flattened comparison reaches `128/189`, changes 392 pixels,
and produces `[5,5]-[41,30]`, red sum 74,382.

ProGPU qualification documentation is commit `4d74fc39`. A clean detached
`a3affb9d` Parallels run passes strict Windows ARM64 MSVC `/W4 /WX`, 11/11
native/Dawn CTests, both export allowlists, two zero-warning managed Release
builds, independent C++ and managed D3D12 samples, the complete bounded smoke
matrix, and nine-file package staging. D3D12 matches Metal exactly: `2/2/2`
passes, correct `128/128`, flattened `128/189`, 392 changed pixels, correct
extent/red sum `[4,4]-[41,31]`/67,186, and flattened
`[5,5]-[41,30]`/74,382. Qualified SHA-256 values are
`32B4876D3930276798732AF91C5D0C866A4A189FED22BEAF7C93016E6006B8C1`
(`progpu_native.dll`) and
`636748FE9C8E29EA5687625E5EF0B77E77017F62FFD463139B36E75162A13DC6`
(`progpu_native_dawn.dll`).

The inherited-opacity-mask checkpoint advances ProGPU to implementation commit
`9fb7c4aa` (qualification commit `faad4874`). LibreWPF now treats every typed
Visual opacity mask as a bounded isolation owner: it publishes exact
`IPortableVisualBoundsSource` descendant bounds through the existing sideband
and accepts solid, linear-gradient, or radial-gradient masks without requiring
a BitmapCache or effect on that same Visual. Missing typed bounds fail closed
before native submission.

Reusable ProGPU C++ emits one bounded outer `FORCE_ISOLATION` layer carrying
the ancestor Visual's local opacity and optional semantic brush-mask resource.
It resets the isolated local alpha before compiling descendants, keeping a
child effect and child-local opacity/mask inside the ancestor mask exactly as
WPF's per-node `PreSubgraph`/`PostSubgraph` stack requires. The native compiler
also rejects an unbounded spatial mask. This adds no reflection, callback,
managed rendering fallback, ABI extension, or DirectX-specific scene path.

All eight portable native CTests, the export allowlist, zero-warning benchmark
build, 70/70 focused LibreWPF compiler tests, and the Apple M3 Pro Metal gate
pass. The correct common ancestor mask executes `2/2/2`
content/composite/effect passes with red samples `60/200`, extent
`[6,4]-[41,31]`, and red sum 66,698. A deliberately flattened per-child mask
executes `3/3/2`, changes 420 pixels, and produces `[6,5]-[41,30]`, red sum
74,122.

A clean detached `9fb7c4aa` Parallels run produces identical D3D12 evidence
and passes strict Windows ARM64 MSVC `/W4 /WX`, 11/11 native/Dawn CTests, both
export allowlists, two zero-warning managed Release builds, independent C++
and managed D3D12 samples, the complete bounded differential smoke matrix, and
nine-file runtime/SDK staging. Qualified SHA-256 values are
`A4A917F47FBA3BA246BCE9D61C1160384C660F8D07D0BA06A02292BDFDAC0018`
(`progpu_native.dll`) and
`743FE185F4D4C900CA1B7F5B18AD85BEAAD47CEA592315AF22D81E625DF0393D`
(`progpu_native_dawn.dll`).

The nested-mask checkpoint advances ProGPU to exact test/qualification commit
`66592f2c` (DirectX qualification commit `36ceeb56`). The LibreWPF compiler test now
publishes two independent typed gradient-mask packets and exact bounds: a
parent horizontal mask and a child vertical mask owned inside the child's
effect. ProGPU preserves parent mask -> child effect -> child mask/local
opacity ordering, with distinct Visual-local mask bounds and resource
identity. The existing generalized layer planner required no reflection,
callback, managed fallback, ABI extension, or backend fork.

All eight portable native CTests, the zero-warning benchmark build, 70/70
focused LibreWPF compiler tests, and the Apple M3 Pro Metal differential pass.
The correct nested stack executes `3/3/2` content/composite/effect passes,
samples red `28/200`, and produces `[7,4]-[41,29]`, red sum 59,308. Flattening
the parent mask into descendants executes `4/4/2`, changes 348 pixels, samples
`29/200`, and produces `[6,5]-[41,28]`, red sum 63,032.

A clean detached `66592f2c` Parallels run produces identical D3D12 evidence
and passes strict Windows ARM64 MSVC `/W4 /WX`, 11/11 native/Dawn CTests, both
export allowlists, two zero-warning managed Release builds, independent C++
and managed D3D12 samples, the complete bounded differential smoke matrix, and
nine-file runtime/SDK staging. Qualified SHA-256 values are
`9BC233F2462CCA5CE5A9BA31A296BEF80E22D6982D5B706F9756D9F62EC6CB97`
(`progpu_native.dll`) and
`743FE185F4D4C900CA1B7F5B18AD85BEAAD47CEA592315AF22D81E625DF0393D`
(`progpu_native_dawn.dll`).

The nested cached-mask checkpoint advances ProGPU to exact test/qualification
commit `f8bd57b5` (DirectX qualification commit `a1d66d70`). LibreWPF now has
explicit two-cache/two-mask packet coverage: the parent cache owns a horizontal mask,
while an effect-owning cached child owns a vertical mask. The compiler emits
both typed BitmapCache packets, both typed mask packets, the effect, and exact
bounds without reflection or fallback.

ProGPU regression coverage proves the invalidation boundary: a root-mask-only
change preserves both cached content revisions, while a child-mask change
preserves child raster pixels but invalidates the root page containing that
child composite. The Apple M3 Pro Metal sequence reports content passes
`3 -> 0 -> 0 -> 2`, effect passes `2 -> 0 -> 0 -> 2`, and pixel changes
`0/379/161`. Extent/red sum moves from `[12,6]-[33,25]`/23,482 to
`[12,6]-[33,25]`/11,772 and `[12,6]-[33,24]`/11,266. All eight portable native
CTests, the zero-warning benchmark build, and 71/71 focused LibreWPF compiler
tests pass.

The clean detached Windows qualification at exact code commit `f8bd57b5`
passed ARM64 MSVC `/W4 /WX`, all 11 native/Dawn CTests, both export allowlists,
two zero-warning managed builds, independent native and managed D3D12 samples,
the complete bounded smoke lane, and nine-file runtime/SDK staging. D3D12
reproduced the Metal pass counts, pixel changes, extents, and red sums exactly.
Packaged SHA-256 values are
`3E5617D3A46F3B2F26A0F727796277A7A9C026C00188EE88BE1D21C320CF8483`
(`progpu_native.dll`) and
`743FE185F4D4C900CA1B7F5B18AD85BEAAD47CEA592315AF22D81E625DF0393D`
(`progpu_native_dawn.dll`).

The cache-root mask/guideline checkpoint advances ProGPU to implementation and
live-gate commit `9eb46b92` (DirectX qualification commit `ea10294b`). The previous
fail-closed check represented a real coordinate-frame gap: the retained page's
four composite vertices were deformed by WPF static guidelines, but the typed
gradient opacity-mask coverage was rasterized in the undeformed target frame.
The shared C++ executor now passes the local-cache composite State and semantic
state cursor into brush-mask preparation. It snaps the exact mask rectangle
through the same guideline set and derives its axis-aligned affine coverage
frame while leaving brush material coordinates in WPF's original target-space
frame. Rotation/shear continues to disable the guideline frame, and ordinary
per-draw masks remain unchanged. No ABI, callback, reflection, managed fallback,
or DirectX-only path is added.

LibreWPF's reflection-free compiler now has explicit packet coverage for one
typed BitmapCache, one linear-gradient opacity mask, X/Y static guidelines, and
the exact typed Visual bounds sideband. ProGPU's Apple M3 Pro Metal gate retains
the page across baseline/guided/independent-reference frames
(`1/1 -> 0/1 -> 0/1` content/composite passes), changes 40 pixels, moves the
masked extent from `[21,8]-[25,15]`/red 1,881 to
`[21,9]-[25,15]`/red 1,617, and matches the independently constructed affine
reference byte for byte (`referenceChanged=0`). Exact DirectX qualification on
2026-08-26 used a clean detached checkout of `9eb46b92`. ARM64 MSVC passed all
11 native/Dawn CTest cases under `/W4 /WX`, both export allowlists, two
zero-warning managed builds, independent native and managed D3D12
allocation/readback samples, the complete bounded smoke suite, and nine-file
staging. D3D12 reproduced the Metal gate exactly, including the same pass
sequence, extents, red sums, `changed=40`, and `referenceChanged=0`. The staged
base DLL was 2,001,920 bytes with SHA-256
`FF3EAAB807826914615FD98EEEC5EBACB6E783EB8E3A4061178D785CD5B95780`;
the Dawn DLL was 2,039,808 bytes with SHA-256
`1B181A7CF2692164C809D8799539A1FDB8839688C6C01B66AF11F326E39908D1`.

The follow-up cache-mask/guideline/effect checkpoint advances ProGPU to exact
gate commit `7889fa17` (DirectX qualification commit `0172af3b`) and LibreWPF packet
coverage to `9183fefff`. The typed compiler regression now emits one linear
gradient mask, one static guideline collection, one BitmapCache, and one
Gaussian BlurEffect for the same bounded Visual. ProGPU's native MIL regression
retains that guideline packet instead of clearing it before effect compilation
and proves the generated stack is outer effect -> local cache with brush mask
plus guideline composite State. The Apple M3 Pro Metal gate executes
`2/2/2 -> 1/2/2 -> 1/2/2` content/composite/effect passes, changes 69 pixels,
moves the blurred masked extent from `[19,6]-[27,17]`/red 1,876 to
`[19,7]-[27,17]`/red 1,617, and is byte-identical to the independently
constructed affine reference (`referenceChanged=0`). No reflection, managed
rendering fallback, ABI expansion, shader fork, or DirectX-only scene branch is
introduced. Exact DirectX qualification on 2026-08-26 used a clean detached
checkout of `7889fa17`. ARM64 MSVC rebuilt both base and Dawn modules under
`/W4 /WX`; all 11 native/Dawn CTest cases, both export allowlists, two
zero-warning managed builds, independent native and managed D3D12
allocation/readback samples, and nine-file staging passed. D3D12 reproduced
the Metal gate exactly, including `2/2/2 -> 1/2/2 -> 1/2/2`, both extents and
red sums, `changed=69`, and `referenceChanged=0`. A transient Parallels Tools
command-channel disconnect occurred later in the smoke tail; every remaining
prescribed semantic-layer-effect, text-shaping, vector-clip, image-effect,
Overlay, and ColorDodge command was rerun individually with unchanged script
arguments against the same binaries and passed. The guest ended clean at the
exact commit. The staged base DLL was 2,001,920 bytes with SHA-256
`AD812584A2F7E549755320A44CA76ED5C20DB5DAD1BD66006EB2D0C7B98F0C2D`;
the Dawn DLL was 2,039,808 bytes with SHA-256
`1B181A7CF2692164C809D8799539A1FDB8839688C6C01B66AF11F326E39908D1`.

The first ordinary static multi-guideline path checkpoint advances ProGPU to
implementation commit `80560d34` and contract documentation commit
`eae5f42a`; LibreWPF publishes the typed collections in `753102825`. Visual and
DrawingGroup `IPortableGuidelineSetSource` data may now carry multiple sorted
static coordinates without reflection or bridge-side deformation. Dynamic
leading/driven pairs remain rejected. ProGPU maps the collection through the
complete MIL scale/translate frame and emits the new append-only
`GUIDELINE_PER_POINT` resource mode, while the prior zero/one affine fast path
and cache-only multi-guide mode retain their existing contracts.

The initial execution subset was one non-boolean semantic path containing
line, quadratic, or cubic segments. Native C++ composes path and scope
transforms, snaps each start/control/end point in absolute target space with
WPF nearest-guide and lower-midpoint-tie behavior, rebases materialized target
coordinates, and publishes identity transform plus updated bounds. Arcs,
boolean paths, primitives, strokes, images, glyphs, meshes, points, 3D, and
dynamic pairs fail closed until their exact representations land. The public
managed/native builders also reject direct per-point use on non-path commands
or cache composites; scoped MIL state remains legal and the executor rejects
unsupported descendants before drawing.

All ten native CTests pass, the managed native-interop class passes 80/80 after
the allocation lane is warmed, the benchmark builds with zero warnings, and
LibreWPF's focused compiler suite passes 72/72 with only its 96 pre-existing
warnings. The Apple M3 Pro Metal differential compares a fractional four-line
path with an independently authored deformed reference: baseline red sum
37,536; guided/reference red sum 40,800; 48 changed pixels; and
`referenceChanged=0`. The Windows smoke script now includes the same gate;
exact D3D12 qualification completed on 2026-08-26 from clean detached ProGPU
commit `80560d340d6d12eb5e4f846cbcac61a53a482b24`. Strict ARM64 MSVC
`/W4 /WX`, all 11 native/Dawn CTests, both export allowlists, two zero-warning
managed builds, both D3D12 allocation/readback samples, the complete bounded
smoke profile, and staging passed. D3D12 reproduced Metal exactly: baseline
`[10,8]-[25,17]`/red 37,536, guided/reference `[10,8]-[25,17]`/red 40,800,
`changed=48`, and `referenceChanged=0`. Qualified DLL hashes are
`D1F0CF2A09D021523B3F42D43C7E1549CB5FD1DF5FCACEB0FBA3A07CF12FC34D`
for `progpu_native.dll` and
`DB359E0C6155530B87DFC7183E4BE071455964F84B9A3D1ED9DAE20A2AB7148F`
for `progpu_native_dawn.dll`.

The subsequent ProGPU GCC portability commits `c6080cb0` and `84b0258d`
explicitly type guideline and aliased-primitive ABI flag expressions as
`uint32_t`; there is no packet or pixel change. An exact Ubuntu ARM64 GCC 13.3
run compiled all 260 C++20 objects under
`-Wall -Wextra -Wpedantic -Werror`, passed 10/10 CTests and the export
allowlist, and completed the Vulkan llvmpipe retained sample with GPU hit-test
and readback. ProGPU documentation commit `e76a9e3c` records that compiler
checkpoint.

The current pinned ProGPU documentation revision `c796aa0e` records native
implementation/package commit `885fa670`, which extends the same algorithm to
multiple path records when their segment ranges are ordered and disjoint.
Shared, overlapping, or out-of-order ranges return `UNSUPPORTED` before GPU
submission, preventing one immutable segment slot from being snapped twice.
The common macOS/Linux and Windows scripts now render one line-only figure and
one mixed quadratic/cubic figure in a single path resource, compare with an
independently deformed reference, and exercise the shared-range negative case.
Apple M3 Pro Metal reports baseline `[10,8]-[35,25]`, red 37,536, green 11,542;
guided/reference `[10,8]-[35,26]`, red 40,800, green 13,045; `changed=76` and
`referenceChanged=0`. WPF lowers `ArcSegment` to one through four cubic Beziers
before its snapping task traverses the shape, so compatible analytic-arc
lowering remains a separate exact checkpoint. ProGPU also corrects the focused
DrawingGroup package fixture to its actual 26-command packet and makes package
failures report all actual/expected MIL metrics; all nine focused package
scenes pass locally through both native MIL exports and live Metal rendering.

The exact clean detached Windows 11 ARM64 qualification at `885fa670` passed
on 2026-08-26. MSVC rebuilt both native modules under `/W4 /WX`; all 11
native/Dawn CTests, both export allowlists, two zero-warning managed Release
builds, independent native and managed D3D12 allocation/readback samples, the
complete bounded differential smoke profile, and package staging passed. The
Parallels Display Adapter D3D12 result reproduced Metal exactly: baseline
`[10,8]-[35,25]`, red 37,536, green 11,542; guided/reference
`[10,8]-[35,26]`, red 40,800, green 13,045; `changed=76` and
`referenceChanged=0`. The guest remained clean. Qualified win-arm64 SHA-256
values are
`73D76B0211CDDDB46383359B4F9833DF551BC2E4123C9E09CFA646CD0AD63F1C`
for `progpu_native.dll` and
`450EBC621B482275377C15EB26FFD0CBF90679D8BE4B87152C3F23A055A326B9`
for `progpu_native_dawn.dll`.

## Microsoft DirectX sample oracle gate

The pinned ProGPU revision adds a cross-platform image gate based on
Microsoft's Windows-only `D3D12HelloTriangle` sample. ProGPU checks out DirectX
Graphics Samples commit `213dd4fd4918ea009dd8f35adee1aff1f2ecaba4` into an
ignored worktree, verifies selected upstream file hashes, applies only a small
capture patch to that generated checkout, and restores the sample's declared
`Microsoft.Direct3D.D3D12` 1.618.3 Agility SDK. No upstream sample source is
vendored into LibreWPF or ProGPU.

That NuGet package is useful as native-oracle infrastructure, not as a managed
Direct3D binding or a replacement renderer. It supplies Microsoft's native
headers and app-local D3D12 runtime for the sample process. ProGPU continues to
render through its shared typed Dawn/wgpu-native layer; its provider-selected
D3D12 runtime is recorded separately until host exports and runtime selection
are deliberately aligned and qualified.

Windows captures the native triangle through WARP and also renders the
equivalent ProGPU semantic scene through D3D12. macOS and Linux render the same
ProGPU scene through Metal and Vulkan rather than attempting to execute D3D12.
The aggregate GitHub Actions job compares all three ProGPU candidates against
the native frame by deterministic interior probes and bounded whole-image
differential, then publishes the PPM images and JSON evidence.

The 2026-08-26 Parallels ARM64 user-session capture, ProGPU D3D12 frame, and
Apple M3 Pro Metal frame are byte-identical at 1280x720. All use PPM SHA-256
`1269AE803032CC2BF6AD717E8491CC19BAF7F9FD5C6B233F8C0012D2DFA53933`;
maximum and mean channel differences are zero, no pixels change, and all probe
differences are zero. Hosted run `32957387184` then passed the independent
D3D12/Metal/Vulkan aggregate: Microsoft Basic Render Driver/D3D12, Apple
Paravirtual device/Metal, and llvmpipe LLVM 20.1.2/Vulkan all produced that
same PPM hash, maximum/mean difference 0, zero changed pixels, and four
zero-difference probes. The published aggregate artifact contains all four
PPMs, manifests, and comparison JSON. This is deterministic virtual/software
adapter evidence, not physical-GPU qualification. Parallels service sessions
cannot create the WARP presentation environment (`0x887A0022`), so the GUI
step must run with `prlctl exec
--current-user`. WARP is the reproducible Microsoft semantic reference, while
the existing ProGPU hardware D3D12 lane remains the adapter/backend
qualification; neither is mislabeled as common-runtime proof.

Replacement hosted build run `32959809523` repeated the byte-exact oracle at
`885fa670` and completed all 27 jobs. The corrected 26-command DrawingGroup
fixture passed the source-independent native NuGet job and every runnable
desktop JIT/NativeAOT package consumer, coupling the oracle result to a green
shipping package graph.

ProGPU implementation `a4ae5576` extends this gate with Microsoft's pinned
`D3D12HelloTexture` sample. The native WARP reference and ProGPU D3D12/Metal/
Vulkan candidates preserve the upstream 256x256 checkerboard, point sampling,
affine UV mapping, triangle boundary, clear color, and viewport. ProGPU uses a
typed nearest-sampled image resource plus edge-aliased cover triangles; this
validates the visible texture semantics without claiming a new arbitrary
textured-mesh command in the scene ABI. The 2026-08-26 Apple M3 Pro Metal,
Parallels Display Adapter D3D12, and native Microsoft ARM64/WARP captures are
byte-identical at 1280x720 with PPM SHA-256
`480B613A9F4FA0E799E46D310E7A3AB9F917B9B60CDA035A2E2718CBF2391397`.
The ProGPU RGBA readback SHA-256 is
`591CC311F35E3C2612F529C3D4D7061FC93751A9B8614BF588A73599B0AA2790`,
and explicit clear/black/white interior probes pass.

## GPU-first fallback and nested MIL scope checkpoint

The current ProGPU pin `b4dff69c` integrates the latest ProGPU `main` device-
recovery/windowing work with the configurable glyph execution policy. Product
default `Fastest` selects native WebGPU compute on qualified Metal/Vulkan
adapters and the exact retained raster-shader substitute on the known
incompatible Parallels D3D12 profile. Forced `compute`, `raster`, `simd`, and
`scalar` modes are available through the typed policy and
`PROGPU_COMPUTE_EXECUTION`; the raster path writes directly into the retained
R8 atlas with zero coverage staging, while CPU modes use managed runtime SIMD
and native NEON/SSE2 before the independent scalar oracle.
The native intrinsic loop now evaluates two adjacent pixels/16 horizontal
samples per crossing broadcast and quantizes 64-sample coverage with exact
integer arithmetic; forced Metal SIMD and scalar modes remain byte-identical.
The exact pushed implementation also rebuilds cleanly with ARM64 MSVC, passes
all 11 Windows native/Dawn tests, and retains zero-difference forced-NEON D3D12
output at hash `5B6EF4F70536C862`.

SIMD implementation `516eb3d7` also skips quadratic/cubic root solving when a
subpixel scanline lies outside the curve's conservative control-point Y hull.
The benchmark's new `--rerasterize-glyphs` mode changes content revision every
frame so results include fresh CPU coverage generation and upload instead of a
retained cache hit. Four alternating 30-frame Apple M3 Pro Release runs per
variant reduced median-of-run native submission p50 from 1.8217 ms to
1.3916 ms (-23.6%) and synchronized frame p50 from 3.6040 ms to 3.0045 ms
(-16.6%); p95 improved from 2.9429 to 2.3009 ms for submission and 5.1773 to
4.4856 ms end to end. All 240 measured frames remained exact at
`5B6EF4F70536C862`, and scalar plus x86_64 SSE2 compile gates pass.

Exact ProGPU head `644a8d89` rebuilt both libraries with ARM64 MSVC and passed
all 11 native/Dawn CTests in Windows. The zero-warning benchmark build ran the
full 42-glyph forced-NEON D3D12 gate with zero pixel difference, 247,808
staging bytes, and the same `5B6EF4F70536C862` hash. A bounded rerasterized
one-glyph A/B remained exact at `6C59592F05595EFE`; its 51–133 second process
startup variance makes it correctness evidence, not a Windows timing claim.
The qualified DLL hashes are recorded in the pinned ProGPU documentation.

SIMD follow-up `e6ab073e` precompiles quadratic/cubic control-point Y hulls
and Y-polynomial coefficients once per CPU-rerasterized frame, rather than on
all eight subpixel scanlines of every pixel row. It does not change the root
solver, crossing order, winding decisions, or scalar oracle. Four alternating
30-frame Apple M3 Pro runs reduced median-of-run native submission p50 from
1.1648 ms to 1.0533 ms (-9.6%) and synchronized-frame p50 from 2.7528 ms to
2.5981 ms (-5.6%); submission/frame p95 medians improved from
2.0873/4.3461 ms to 1.4839/4.0934 ms. All 240 measured frames retained exact
`5B6EF4F70536C862` output, all five execution-policy checks passed, and the
native/Dawn plus x86_64 SSE2 compile gates remained green.

Exact implementation head `405d139b` then passed the unmodified Windows ARM64
MSVC/D3D12 smoke gate. Both libraries rebuilt; all 11 CTests, native and
managed samples, Microsoft D3D12HelloTriangle, forced raster/NEON/scalar
pixel parity, typed pre-resource rejection of incompatible forced compute,
MIL guideline/arc deformation, retained cache/mask/effect/blend families,
text parity, bounded differential profiles, and package staging completed on
the Parallels adapter. SHA-256 is
`C690AED72C3C895778197808C8347656433D6A97DD178F5249A8B4D0C1B56756` for
`progpu_native.dll` and
`552E8CC9441B9A33E89B346758113B52DC13F7A3B1D11F80BF86A3AE90039637` for
`progpu_native_dawn.dll`.

SIMD checkpoint `bf20bd66` collects all eight Y-subscanline crossing spans for
one raster row before visiting X. A pixel pair now constructs its four
NEON/SSE2 sample vectors once, resets only integer winding accumulators between
subscanlines, accumulates the same 64 samples, and writes coverage directly.
Crossing order, strict comparisons, floating-point sample expressions,
quantization, and the scalar oracle remain unchanged. Four alternating
30-frame Apple M3 Pro A/B runs per variant reduced median submission/frame p50
from 1.0469/2.6249 ms to 1.0199/2.5889 ms at 1x DPI (-2.6%/-1.4%) and from
1.9498/3.5588 ms to 1.7884/3.3814 ms at 2x DPI (-8.3%/-5.0%). All 480 measured
baseline/candidate frames remained exact at `5B6EF4F70536C862` (1x) or
`706B261418EC5C3B` (2x). Both macOS libraries, all 11 native/Dawn tests, every
execution-policy route, and strict x86_64 SSE2 compilation pass.

The same implementation rebuilt both Windows ARM64 libraries under MSVC
`/W4 /WX`, passed all 11 CTests, and reproduced the full 42-glyph forced-NEON
D3D12 hash `5B6EF4F70536C862` with 247,808 staged coverage bytes. DLL SHA-256 is
`EE150A6E7EACF4B7E789C8EE9B0A0A91778D121AE107FCF7700BEC4C7FD588C5` and
`3FF479B331F6548938115C272FE53B03F4AC89872B565941AA0DD34DF75A9B35`.
The VM result is correctness evidence, not a Windows timing claim.

Two later SIMD candidates were intentionally not shipped. Four-pixel NEON
crossing batches and a packed-byte deferred horizontal reduction both stayed
byte-exact and kept all native tests green, but longer grouped measurements
regressed median submission p50 by roughly 3–5% at both 1x and 2x DPI. ProGPU
records the complete measurements and retains `bf20bd66` as the qualified
no-regression implementation.

The macOS Metal matrix produces exact managed/native hash
`5B6EF4F70536C862` in all modes. Ubuntu ARM64 GCC 13.3 compiled the full
260-object graph, passed 10/10 wgpu-native CTests and live Vulkan/llvmpipe
render/readback, and produced exact hash `1F9AE0BB0AC59113` in forced compute,
raster, SIMD, and scalar modes. The clean Windows ARM64 MSVC gate selects
raster automatically, reproduces the Metal hash, proves NEON SIMD parity,
bounds the scalar oracle to one glyph, and requires forced compute to throw its
typed pre-resource incompatibility without a WebGPU device error. The build
scripts now execute that matrix automatically and bind benchmark copies to the
actual custom native build directory, preventing stale-library qualification.

The same pin advances canonical nested MIL decoding. Static
`PushGuidelineSet` resolves its retained resource in the transform active at
the push, scopes it through the mixed save/layer stack, and keeps dynamic Y1/Y2
pairs fail closed until Stage 2 supplies WPF's animation clock and scheduling
state. Constant and animated `PushOpacity` scopes now use a full-target native
isolated layer, applying alpha once at `Pop` so overlapping children are not
attenuated independently. Animated scopes resolve their typed DoubleResource
on each compile without retransmitting render data. Canonical
`PushOpacityMask` now consumes its exact retained local bounds: static solid
masks fold to isolated group alpha, while linear/radial masks compile to the
existing backend-neutral GPU brush-mask resource and are applied once at
`Pop`. Gradient resource-only updates are observed by the unchanged retained
render-data stream. The integrated head passes the complete local native suite
and focused managed packet/policy tests. Retained `DrawingGroup` static or
animated opacity plus static solid opacity-mask alpha use the same isolated
group-composite rule, so overlapping drawing children retain source-over
coverage before the group alpha is applied. Source-built WPF can now bind exact
local DrawingGroup content bounds through the typed C/.NET sideband; retained
linear/radial group masks reuse the same backend-neutral GPU brush-mask
resource, preserve those bounds across canonical group updates, and fail closed
when bounds are absent. The focused package consumer exercises that gradient
path through both native exports and live Metal rendering.

LibreWPF now closes that producer seam. ProGPU's portable group DTO publishes
separate `HasLocalBounds`/`LocalBounds` state because the existing `Bounds`
value is post-transform. Source-built `DrawingGroup` computes the new value
with WPF's own `BoundsDrawingContextWalker`, applying the group clip and all
child drawing semantics while deliberately excluding only the group's own
transform. `WpfNativeMilSceneCompiler` carries those local bounds in its batch,
binds them with `NativeMilChannel.SetDrawingGroupBounds`, accepts typed
linear/radial group masks, and rejects spatial masks before native compilation
when exact local bounds are absent. This avoids transform double-application
and keeps the bridge reflection-free. The Release compiler/contract gate passes
74/74 focused tests; a source-built PresentationCore test separately asserts a
translated, clipped group reports local `(12,21,10,8)` and post-transform
`(42,61,10,8)` bounds.

Windows ARM64 qualification used exact LibreWPF `6a15f6dfc` and ProGPU
`20fc4299`. The Release producer graph built with zero errors (the 96 warnings
are the existing LibreWPF WinForms compatibility baseline), then passed all
74 focused native-MIL compiler and typed-contract tests. The ProGPU package
consumer rebuilt from project references with zero warnings and errors. Its
focused gradient DrawingGroup compiled through both wgpu-native and Dawn MIL
exports and rendered on the Parallels D3D12 adapter with five resources, two
draws, zero coverage staging, and a 16,384-pixel readback. Because `20fc4299`
changes only the managed portable DTO and documentation, the qualified native
implementation remains exact native commit `644a8d89`; DLL SHA-256 is
`A9BB8F281F27B332AAACAA0EC35B9E3B26E73D21E839470654D95CB89DDA6A39`
for `progpu_native.dll` and
`97CDBDD4F02442F2D9ACF966C1FF1660C64D7014E9A98FC767B3D9819CB561BF`
for `progpu_native_dawn.dll`.

The new source-built PresentationCore unit test is checked in, but its isolated
VM execution remains an infrastructure gate: the upstream WPF bootstrap fails
before product compilation while acquiring its pinned Strawberry Perl,
.NET Framework 4.8 reference, and D3D redist tools, then cannot load the
`FilterItem1ByItem2` custom task. This does not affect the independently built
LibreWPF producer or live D3D12 gates above; the full source-WPF test remains
required once the VM image carries the official WPF native tool bundle.

The source producer hot path avoids an unnecessary second drawing-tree walk for
the common identity, translation, and axis-aligned scale cases. It computes
local clipped child bounds once and derives post-transform bounds with WPF's
typed `Transform.TransformBounds` only when the matrix has zero cross-axis
terms. Rotation, skew, and other cross-axis transforms retain the original
full `Bounds` walker so a transformed local bounding box cannot broaden exact
content bounds. Source tests cover both the one-walk translated case and the
exact rotated fallback; the reflection-free contract audit enforces the
axis-preserving guard.

The exact `b36b241b` Windows checkpoint rebuilt both libraries with ARM64 MSVC,
passed all 11 native/Dawn CTests, matched both export allowlists, and built the
managed consumer with zero warnings. Its focused linear-gradient DrawingGroup
compiled through the wgpu-native and Dawn MIL channels, then rendered on the
Parallels D3D12 adapter with five semantic resources, two draws, zero coverage
staging, and a valid 16,384-pixel readback. SHA-256 was
`F3FB0D077BE494A6D067C1526C96C56A10A0981E8B9283D8574ABF52FEEBFD85`
for `progpu_native.dll` and
`F002C1FB564334FF21E6F1B18E2FADFD067A955103531A7E1E55B4CC361D6DC8`
for `progpu_native_dawn.dll`.

ProGPU implementation `a7dcd8de` and documentation checkpoint `59bffdb4`
complete the native scalar MIL value-resource family and the core animated
render-data primitives. Color, Rect, Size, Point3D, Vector3D, and Quaternion
resources now join the existing Double, Point, and Matrix decoders under the
generated WPF MCG layouts, exact type/size checks, finite-value validation, and
transactional rollback. DrawLineAnimate, DrawRectangleAnimate,
DrawRoundedRectangleAnimate, DrawEllipseAnimate, and DrawImageAnimate resolve
live typed dependencies during native semantic-scene compilation. Static
DrawImage and retained ImageDrawing share the pointer-free RGBA8 BitmapSource
sideband, and ImageDrawing now applies its live RectResource destination.

The complete Apple Silicon native/Metal gate passed. A clean detached Windows
ARM64 MSVC `/W4 /WX` build of exact implementation `a7dcd8de` passed all 11
native/Dawn CTests and the live Parallels D3D12 smoke matrix. Forced raster,
intrinsic SIMD, and scalar paths were exact; SIMD retained-glyph parity kept
hash `5B6EF4F70536C862`, while forced compute failed through the expected typed
adapter incompatibility before resource execution. Microsoft
D3D12HelloTriangle and D3D12HelloTexture produced the same hashes on D3D12 and
Metal. Qualified SHA-256 is
`CD33CEEE182F2A77403B96F4D23DF7FBB1A61AEFAD66C927D3282C4A461236C3`
for `progpu_native.dll`,
`50362916F0026C1B016A2496F89547B1814C4F3BCA2D414CCC6B39B2E12B84F6`
for `progpu_native_dawn.dll`, and
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDBC`
for wgpu-native. The canonical process-pointer BitmapSource packet,
BitmapInvalidate, direct DrawingImage image commands, D3DImage/shared-surface
synchronization, and planar/HDR media remain fail-closed typed-contract work.
The later typed MediaPlayer lane covers packed same-device external textures
without weakening those boundaries.

ProGPU framing checkpoint `d7538fe7` validates every nested RenderData packet
against the generated managed-producer size before dispatch. Unsupported but
correctly framed commands remain `unsupported_command`; a short or oversized
form of any of the 25 nested commands is `malformed_batch`. This prevents a
partially implemented handler from accidentally accepting a producer layout
that changed underneath it.

Direct DrawingImage replay follows at `6d96ceaa`. Static and animated DrawImage
records can consume a typed DrawingImage, resolve its canonical Drawing and
exact local-bounds sideband, clip to the destination, and compose an affine
source-to-destination mapping before retained vector replay. This shares the
ImageDrawing path, preserves empty images as no-ops, rejects cycles, and never
rasterizes through a bitmap or reflects over WPF objects. SolidColorBrush
checkpoint `67739b87` then resolves live DoubleResource opacity and
ColorResource color through one shared brush path used by analytic/path fills,
pens, glyphs, and uniform opacity masks. Retained revision and deletion graphs
include both resources.

Animated effect checkpoint `8836b8b1` resolves BlurEffect radius and all five
DropShadowEffect animation slots from typed DoubleResource/ColorResource
state. Effect-chain and cached-layer revisions incorporate those dependency
generations, so value-only updates change sigma, offset, color/alpha, and
inflated bounds without retransmitting the effect or Visual. Missing or
wrong-type references and deletion of a live dependency fail transactionally;
Box blur was still explicitly unsupported at that checkpoint.

SIMD checkpoint `09589773` improves the already qualified two-pixel CPU glyph
fallback without increasing vector pressure. NEON/SSE2 comparison masks are
applied directly with signed add/subtract, removing the direction broadcast
and four bitwise masks per crossing. Four alternating Apple M3 Pro runs per
variant, each with 120 rerasterized frames, improved median native-submission
p50 by 19.4% at 1x and 10.9% at 2x. All 960 frames remained exact at
`5B6EF4F70536C862` and `706B261418EC5C3B`; the complete local native suite and
strict x86_64 SSE2 compile pass. This improves only the configured intrinsic
fallback and does not alter the GPU-first default.

The follow-up empty-subscanline branch experiment was exact but rejected. Four
alternating 120-frame runs per variant kept identical current-scene hashes at
both DPI scales. It improved 2x median submission/frame p50 from
3.0416/6.5538 ms to 2.8003/6.1728 ms, but regressed 1x submission p50 from
1.6761 to 1.7931 ms (+7.0%) and frame p50 from 5.3414 to 5.3760 ms (+0.6%).
The source therefore retains unconditional subscanline vector reduction under
the documented cross-profile no-regression rule.

PushEffect checkpoint `2387fa4a` follows the current WPF native executor's
disabled legacy BitmapEffect behavior exactly. ProGPU validates the canonical
12-byte managed record, treats its two producer dependency indices as opaque,
and gives the balanced scope PushOpacity(1)/Pop stack semantics without adding
false native dependencies. Animated fixed-geometry checkpoint `45afbc3b` then
resolves every LineGeometry, RectangleGeometry, and EllipseGeometry animation
slot from typed PointResource, RectResource, and DoubleResource state. Direct,
retained, recursive, fill, clip, and cached consumers share the live resolver;
value-only updates change exact output and cache revisions, while wrong types,
invalid live dimensions, or deletion of a referenced value fail closed. All
eight local native suites and the generated 143-command/141-layout drift gate
pass at this checkpoint.

Animated stroke checkpoint `21221dff` resolves Pen thickness and DashStyle
offset through typed DoubleResource handles across immediate and retained
lines, paths, rectangles, rounded rectangles, ellipses, degenerate caps, and
group/combined-geometry stroke decisions. The hot path copies only compact Pen
state; dash arrays remain retained and allocation-free. Value-only updates
change exact stroke thickness, dash phase, bounds, and cached-layer revisions;
wrong types, dependency deletion, and negative live thickness fail closed. All
eight local native suites and the generated protocol drift gate pass.

Windows-gate checkpoint `edd98b71` captures the expected forced-compute
rejection through `System.Diagnostics.Process`, so Windows PowerShell 5 and
PowerShell 7 both validate the typed incompatibility, exit code, and absence of
unsafe WebGPU/device errors before continuing the same D3D12 smoke matrix.

The exact detached `edd98b71` rerun completed that full matrix on Parallels
D3D12: ARM64 MSVC `/W4 /WX`, 11/11 native/Dawn CTests, native and managed
allocation/readback samples, forced raster/NEON/scalar glyph parity, typed
forced-compute rejection, Microsoft triangle/texture oracles, all retained
cache/effect/mask/clip/text/blend profiles, and nine-file package staging.
SHA-256 is
`0E13CD164AB5449DA7FEFB44F7FE26DE76E2200B16EAC047BFBAA1589C5A3C07`
for `progpu_native.dll` and
`F58F610CF3513C275C59254510D646C3B7F2BA175B3927F6679ABC36067A8721`
for `progpu_native_dawn.dll`.

The following Box-blur checkpoint closes WPF `KernelType.Box` as a typed GPU
path. Canonical MIL kernel 1 resolves live animated radius state and emits the
reusable Box group-effect descriptor; Gaussian remains the default, and
unknown values fail closed. Uniform `2R + 1` weights execute in the existing
two-pass WebGPU compute resources with no CPU readback or product fallback.
The public native capability and C/C# factories make the same effect available
to WPF, WinUI, and Avalonia. The new `--group-box-blur` gate is exact against an
independent two-pass RGBA8 oracle at radius 2/1x on Apple M3 Pro Metal
(`22A8BEC63E7C7494`); at 2x it stays within 1/255 with zero pixels beyond
tolerance and mean absolute error 0.000455 byte/channel. The Windows smoke
script now gates this profile on D3D12 as well. LibreWPF's reflection-free
native scene compiler maps the source-built `PortableBlurKernel.Box` contract
to `NativeMilBlurKernelType.Box`; unknown kernel values remain fail-closed.
The focused compiler suite verifies the canonical packet field, so a real WPF
`BlurEffect.KernelType` selection reaches the ProGPU shader instead of stopping
at the bridge. ProGPU's portable managed compositor now exposes the same typed
`BlurKernelType.Box` selection and reuses its cached separable WebGPU pipelines,
uniform buffers, bind groups, and intermediate texture. LibreWPF's portable
effect mapper preserves the source-built Box selection on that path as well;
Gaussian remains the default, unsupported enum values fail closed, and neither
GPU route introduces a CPU readback fallback.

Exact Box checkpoint ProGPU `0866b919` / LibreWPF `8dabd9d84` completed the
full Windows ARM64 Parallels D3D12 lane: strict MSVC `/W4 /WX`, 11/11
native/Dawn CTests, native and managed samples, forced raster/NEON/scalar
parity, expected typed compute rejection, Microsoft D3D12 triangle/texture
oracles, the retained cache/effect/mask/clip/text/blend matrix, and package
staging. Box was byte-exact against its independent two-pass oracle at
`D77D5DC8AC370BCE`. DLL SHA-256 values are
`3A64CFDD974448B71F8BF645AFCBDE95DC10C64256F73D7CEF1E12776DB3DA20`
and `B77C8A4157D4432F8C74F6067DE2944F96C8ECE0FA16B4967B24D330326DD70A`.

Accepted SIMD follow-up `c5549ceb` stops computing a discarded second pixel
on odd-width glyph rows. The qualified paired loop remains a 16-sample
NEON/SSE2 kernel, while its final tail uses a dedicated 8-sample intrinsic
kernel with identical winding and integer quantization. Four alternating
120-frame runs per variant stayed byte-exact at `5B6EF4F70536C862` (1x) and
`706B261418EC5C3B` (2x). Median submission/frame p50 improved 3.9%/5.8% at 1x
and 5.9%/3.2% at 2x; all p95 comparisons also improved. Ten native tests, 84
focused managed interop tests, and strict x86_64 SSE2 compilation pass.

Subsequent conservative right-bound, scalar-offset, and native-vector-offset
SIMD candidates were all pixel-exact but rejected by the cross-profile
no-regression gate. The vector form improved submission latency at both DPIs,
yet eight-run 2x synchronized-frame p50/p95 regressed
5.6951/8.4109 -> 5.8623/8.5459 ms. Explicit baseline/candidate dylib hashes
were checked before every alternating process after a stale-copy harness issue
was detected, so only correctly staged evidence is retained.

An exact line-segment metadata candidate was rejected for the same reason. It
cached X/Y deltas without changing crossing arithmetic or edge decisions and
improved 2x submission/frame p50 from 1.7558/5.5705 to 1.7332/5.0904 ms, but
regressed 1x from 1.0949/5.1557 to 1.1324/5.3494 ms and worsened frame p95.
All eight 120-frame processes retained the managed/native hashes above. The
qualified implementation therefore avoids the added metadata traffic.

A subsequent crossing-layout candidate split positive and negative winding
positions into separate `float` arrays and compile-time-specialized their
NEON/SSE2 updates. It remained byte-exact at `5B6EF4F70536C862` (1x) and
`706B261418EC5C3B` (2x), but initial 120-frame submission/frame p50 regressed
1.0344/5.3215 -> 1.5310/5.9844 ms at 1x and
1.6587/5.0745 -> 2.5752/6.2036 ms at 2x, with worse p95 values. The candidate
was rejected immediately; the qualified interleaved `{x,direction}` crossing
layout remains unchanged.

Precomputing the eight row-local crossing `span` descriptors was also exact
and rejected. The extra stack-resident descriptors were intended to remove
repeated offset-based `subspan` construction from each pixel pair and odd
tail, but Apple M3 Pro Metal 120-frame gates moved submission/frame p50 from
`1.4922/5.5365` to `1.7465/5.2648` ms at 1x and from
`1.9650/6.1749` to `2.6905/6.3856` ms at 2x. Both runs retained hashes
`5B6EF4F70536C862` and `706B261418EC5C3B`; ProGPU `8db55a80` records the
negative evidence and keeps inline `subspan` construction as the qualified
form.

A first-reset branch candidate then improved p50 at both DPIs but regressed 2x
frame p95 by 1.6%, so it was rejected. Accepted ProGPU `deb50413` instead folds
the exact NEON 0-or-1 lane reduction and removes one vector add per pixel with
no new branch or floating-point change. Across eight alternating 120-frame
runs, submission/frame p50 improved 3.2%/4.9% at 1x and 5.3%/5.7% at 2x;
p95 improved in all four comparisons and both managed/native hashes remained
exact. The local ten-test native suite, strict x86_64 SSE2 syntax gate, and a
Windows 11 Parallels ARM64 MSVC/Ninja rebuild with all ten non-Dawn CTests pass.

An exact NEON absolute-value/unsigned-minimum coverage candidate was then
measured against that folded baseline and rejected. Eight alternating
120-frame runs per variant kept hashes `5B6EF4F70536C862` (1x) and
`706B261418EC5C3B` (2x) with zero channel difference. At 1x,
submission/frame p95 regressed 1.4299/7.2335 -> 1.5391/7.3918 ms; at 2x,
frame p50 regressed 5.6484 -> 5.8287 ms even though submission and frame p95
improved. The source retains the qualified compare/invert/shift flag formation
and folded lane reduction.

The following pixel-pair origin reassociation candidate was exact but also
rejected. It replaced the second pixel's independent add/subtract/multiply
chain with `first_glyph_x + inverse_scale`. Eight alternating 120-frame runs
per variant retained zero channel difference and hashes `5B6EF4F70536C862`
(1x) and `706B261418EC5C3B` (2x). Median submission p50 changed only
1.4806 -> 1.4765 ms and 2.2561 -> 2.2521 ms, while synchronized-frame p50
regressed 5.2713 -> 5.3665 ms at 1x and 6.1248 -> 6.1985 ms at 2x; 1x frame
p95 also worsened 8.0932 -> 8.1285 ms. The qualified implementation retains
the independently evaluated second origin and its established edge rounding.

A following branchless winding-direction candidate was exact but slower. It
replicated a normalized `+1`/`-1` direction and masked that delta into every
paired and odd-tail NEON/SSE2 accumulator. Eight alternating 120-frame runs
kept the same exact hashes, while median submission/frame p50 regressed
1.4165/5.2487 -> 1.4850/5.4465 ms at 1x and 1.7186/5.7809 ->
1.9296/6.0166 ms at 2x; every p95 median also worsened. The qualified kernel
therefore retains the lower-cost direction branch.

ProGPU `447ec566` records two additional exact but rejected candidates. Checked
32-bit row-crossing offsets improved the complete 2x median set but regressed
1x synchronized-frame p50 by 2.0%; hoisting only the row's base span generated
a byte-identical dylib because Clang already performs that optimization. A
paired `uint32x2_t` NEON total improved submission p50 by 3.0% at 1x and 5.3%
at 2x, yet regressed 1x frame p50 by 3.8% and 2x frame p95 by 2.2%. All 32
process reports per experiment retained exact hashes
`5B6EF4F70536C862`/`706B261418EC5C3B`; both source candidates were reverted
and the previously qualified folded reduction remains authoritative.

The next reflection-free render-data slice preserves canonical `PushClip` and
`PushOpacityMask` scopes in the native MIL stream. Geometry and mask resources
resolve only through typed portable contracts, opacity-mask bounds retain the
WPF single-precision layout, null-resource scopes become balanced identity
scopes, malformed packet sizes fail closed, and the existing `Pop` balance
validation remains authoritative. Canonical static `PushGuidelineSet` scopes
now resolve through `IPortableGuidelineSetSource` and native guideline
resources too; null sets are valid no-op scopes, while dynamic pairs remain
fail-closed until their MIL preprocessing contract is implemented. Canonical
legacy `PushEffect` follows milcore's own disabled-BitmapEffect behavior: its
managed-only handles remain opaque and it lowers to a balanced identity scope,
not an invented effect implementation. Static canonical `DrawImage` now uses
ProGPU's typed writer and the existing bitmap/drawing-image resource resolver;
finite destination rectangles and zero padding are preserved, null sources are
no-ops, and pixels still enter only through `IPortableBitmapSourcePixelsSource`
or the typed DrawingImage graph.

Canonical `PushOpacityAnimate` is the first render-data animation command to
cross the bridge without reflection. Generated source-built WPF animation-clock
resources publish live `Double`, `Point`, `Size`, and `Rect` values through
narrow ProGPU interop contracts and expose clock invalidation through
`IPortableInvalidationSource`; the code generator emits the same contracts so a
future MCG regeneration preserves the seam. The native compiler resolves the
double resource, emits canonical type-49 `DoubleResource` state, and retains
the animation handle in `PushOpacityAnimate`. Missing or untyped animation
resources and nonzero packet padding fail closed. The focused native-scene
compiler now also preserves the complete animated 2D draw family: line point
pairs, rectangle bounds, rounded-rectangle bounds/radii, ellipse center/radii,
and image destination bounds. Point, rectangle, and double resources are
deduplicated by source identity, emitted as canonical typed MIL resources, and
retained by each animated command handle. ProGPU owns the exact packet writers,
including type-50 `SizeResource` and type-52 `RectResource`; missing typed
values, malformed sizes, or nonzero reserved padding fail closed. The focused
native-scene compiler suite now passes 88/88 cases, while ProGPU's focused
native interop suite passes 87/87.

The first typed retained `Viewport3DVisual` slice now crosses the same native
MIL compiler without reflection or CPU projection. LibreWPF recognizes only
`IPortableViewport3DSceneSource`, creates the canonical type-40 visual, and
flattens its finite camera, viewport, light/material, model/normal matrices,
vertices, and indices into ProGPU's copied pointer-free sideband. ProGPU then
emits its reusable semantic 3D mesh draw; projection, viewport placement,
lighting, depth, and rasterization stay in the shared GPU backend for Metal,
D3D12, Vulkan, and browser WebGPU. The compiler preserves retained offset,
axis-preserving transform, opacity, exact rectangle/scroll clips, and exact
front/back material selection. Non-axis-preserving 2D transforms, arbitrary
geometry clips, opacity masks, effects, caches, and guidelines fail closed
until the native 3D compositor can reproduce them exactly. Focused coverage
validates the sideband, emitted rectangle/scroll-clip commands, and
representative unsupported-state boundaries.

ProGPU's new `--semantic-viewport3d` gate exercises the complete retained MIL
sideband, semantic compiler, shared WGSL pipeline, sub-viewport mapping, Metal
submission, and GPU readback. The first run found and fixed three backend gaps
that byte-level tests could not expose: wgpu-native-incompatible temporary
array indexing, invalid zero-initialized stencil comparison enums, and mesh
positions incorrectly consuming `NativePoint3D.Reserved` as homogeneous `w`.
With explicit line-corner selection, valid unused stencil state, and
`vec4(position.xyz, 1)`, Apple M3 Pro Metal executes a 0.75 axis scale,
`[8,6]` retained offset, 0.5 opacity, a local rectangle clip, and a world-space
scroll clip together. The viewport becomes `[32,21]-[80,57]`, the effective
clip is `[48,28.5]-[66.5,47.25]`, and all 291 colored pixels occupy
`[48,28]-[66,47]` with the expected half-red center sample. The same gate is
now part of macOS/Linux and Windows native integration scripts so D3D12 and
Vulkan must reproduce this transformed, clipped, composited placement rather
than merely accepting the scene bytes.

The same live gate now covers the lighting fields LibreWPF already lowers from
the typed WPF scene. ProGPU's shared `Native3D.wgsl` scales diffuse/specular by
`LightIntensity`, material ambient by `AmbientIntensity`, and uses each
material's `Shininess` as the specular exponent instead of hardcoding 24. With
realistic shading, 0.4 directional intensity, 0.2 ambient intensity, and 0.5
retained opacity, the Metal center sample is exactly `77/51/0/255`; a second
generation changes shininess from 1 to 256 and must produce a different GPU
image. The focused ProGPU native suite remains 10/10 and the native interop
suite passes 88/88.

Orthographic cameras now have explicit compiler and live coverage as well.
LibreWPF converts the typed WPF horizontal `Width` into
`Matrix4x4.CreateOrthographic(width, width / aspectRatio, near, far)` and the
focused compiler suite passes 89/89 with exact projection-matrix assertions.
ProGPU renders a fourth retained generation through that camera, requires its
readback to differ from the perspective frame, and observes 278 colored pixels
at `[48,28]-[66,47]` inside the same transformed viewport and clip.
The native boundary also rejects negative directional/ambient intensity and
nonpositive shininess before retaining the mesh; focused C++ coverage exercises
all three invalid cases, and the complete native suite remains 10/10.

Source-built `MatrixCamera` now crosses the same reflection-free boundary.
ProGPU's additive portable camera kind carries the complete WPF view and
projection matrices; the exporter folds `Camera.Transform` into the view matrix
using WPF's own typed camera implementation. Both the managed compositor bridge
and native MIL compiler preserve those matrices directly, derive camera position
from the inverse view for GPU specular lighting, and reject non-finite or singular
views rather than substituting a perspective camera. Focused managed/native
bridge coverage passes 108/108, and the real PresentationCore exporter suite
passes 3/3 on macOS.

WPF mesh texture coordinates are now preserved end to end as typed geometry
state rather than discarded. The source exporter copies the finite
`MeshGeometry3D.TextureCoordinates` prefix and pads missing trailing vertices
with WPF's canonical `(0,0)` coordinate. The managed ProGPU path feeds that
array to its existing textured-mesh vertex upload, while native MIL writes it
into the stable `progpu_native_scene_mesh_3d_vertex.texture_coordinate` field.
Extra WPF coordinates are ignored by vertex count, matching MIL. This does not
invent a brush fallback: it establishes the UV half of the next typed 3D brush
resource gate.

Mesh normals now follow WPF MIL's upload contract as well. Source-built WPF
normalizes every supplied normal, computes the complete face-normal set when
the collection is short, and then preserves the normalized supplied prefix;
zero normals remain zero and extra normals are ignored. Both ProGPU consumers
repeat finite/range validation and normalization for non-WPF typed producers.
The `System.Numerics.Vector3` divide in those hot loops is runtime-intrinsic
SIMD, while non-finite inputs fail closed instead of reaching a GPU buffer.

WPF material groups now cross the source boundary as an ordered typed layer
array. Source-built `MaterialGroup` recursively flattens diffuse, specular, and
emissive children in MIL order and snapshots each supported brush DTO plus its
material color, ambient color, and specular power. The two ProGPU consumers
expand solid-color layers into shared-geometry GPU passes: diffuse and
specular use realistic lighting, while emissive selects a per-mesh unlit
shader override. Geometry, normal, UV, and index storage is not duplicated.
The LibreWPF bridge maps diffuse and emissive linear/radial DTOs to
the reusable ProGPU Mesh3D material-brush path. ProGPU uploads finite stops from
reused scratch through `CollectionsMarshal.AsSpan(...)` and evaluates UV-space
coordinates, inverse affine transforms, spread, interpolation, brush opacity,
and stop alpha in WGSL. The live Metal gate renders distinct red/blue gradient
regions, and WinUI uses the same path instead of its former first-stop
approximation. Native MIL now encodes the same mapped ProGPU brush as one
canonical 256-byte `NativeSceneBrush` per mesh plus shared 32-byte gradient
stops. The camera payload prefix and 256-byte mesh ABI remain unchanged; an
optional versioned suffix references the shared brush table. Solid-only scenes
retain the camera-only compatibility path, while mixed scenes fill non-gradient
passes with an opaque-white multiplier. A dedicated native mesh flag now makes
the canonical brush multiply the existing specular-color vector for ordered
specular-gradient passes, preserving the 256-byte mesh ABI. Tile-brush
materials remain a typed fail-closed gap; no CPU texture staging or readback is
used. The managed 560-byte Mesh3D record now uses its previously reserved
`MaterialStopMetadata.z` lane for the equivalent typed
`MaterialBrushTarget3D`: zero preserves the established color target, while
one multiplies the sampled brush into the specular vector. LibreWPF maps typed
specular-gradient layers to that target with black diffuse RGB, preserved
material color/power, and no reflection or CPU texture conversion. Invalid
target/brush combinations fail closed.

That managed gradient checkpoint is now qualified on Windows D3D12 as well.
The exact pushed ProGPU `8eee2170` archive, hydrated only with the commit's
pinned `microsoft-ui-xaml` `generic.xaml`, built the complete test graph under
.NET SDK 10.0.400 with zero warnings and errors. The focused Mesh3D family
passed 18/18 in 4.6601 minutes, including typed linear/radial compilation,
live linear-gradient GPU readback, point/spot lights, planar surfaces, and
scratch reuse. A diagnostic rerun of the gradient readback selected
`Parallels Display Adapter (WDDM)`, backend `D3D12`, device type
`DiscreteGpu`, and passed without WebGPU validation/device errors. Metal and
D3D12 therefore execute the same reusable managed WGSL material path.
ProGPU checkpoint `318c0b0a` adds the native C++ equivalent: builder, validator,
stable retained hashing, GPU brush/stop buffers, the MIL copy sideband, managed
typed wrappers, and export allowlists. Its expanded Metal
`--semantic-viewport3d` gate preserves the original 291-pixel clip and observes
75 red-dominant plus 96 blue-dominant pixels from the native linear material.
All 11 CTests, generated-contract verification, export verification, and two
focused managed ABI tests pass. The exact source archive also passes ARM64
MSVC `/W4 /WX`, both export allowlists, and 11/11 native/Dawn CTests in the
Windows Parallels VM. Its live D3D12 gate reproduces the Metal evidence exactly
at 291 clipped pixels, 75 red-dominant pixels, and 96 blue-dominant pixels on
`Parallels Display Adapter (WDDM)` without WebGPU validation/device errors.
LibreWPF converts the already mapped typed ProGPU vector brush without
reflection; focused producer coverage preserves coordinates, opacity, spread,
scRGB interpolation, and stop offsets/colors. The native compiler preserves
the same layer with its specular flag, black diffuse RGB, material
color/exponent in the specular vector, and the canonical gradient sideband.
Apple M3 Pro Metal observes 64 red-dominant plus 85 blue-dominant pixels from
the specular-only generation inside the same 291-pixel clip, distinct from the
75/96 unlit-gradient evidence.

The framework-neutral managed route is now executable rather than a retained
record-only assertion. Its focused Metal gate renders the specular brush under
an explicit point light and observes 3,300 red-dominant plus 3,300
blue-dominant pixels, with maximum endpoint-channel deltas of 134. The
ordinary gradient test runs beside it to protect default-target compatibility;
the complete managed Mesh3D family passes 22/22 with a warning-free Release
build. The LibreWPF bridge/compiler selection passes 110/110 focused tests.

Exact ProGPU implementation `ed98df5d` is independently qualified in the
Windows 11 ARM64 Parallels guest from archive SHA-256
`0EAA66E17840D35DE955854F31C0D9398115D4D7473D451218B363071B68AC50`.
The archive's pinned `microsoft-ui-xaml` gitlink is `25d2cb1c`, and the hydrated
`generic.xaml` matched the current submodule at SHA-256
`4C4085838721C0AFCB1A9EE17591C0655CDDDADB26D330788E08BCD7F1AF8285`.

ProGPU `d51b289b` adds Win2D `CanvasBitmap.CreateFromColors(...)` and full or
subrectangle `SetPixelColors(...)` over an allocation-free-after-warmup pooled
conversion buffer. It preserves upstream Win2D's exact ARGB-to-BGRA swizzle;
it does not invent a premultiplication pass. The typed
`ProGpuCanvasCpuConversionMode` supports `Automatic`, forced
`IntrinsicSimd`, and `ScalarReference`. Automatic selects AVX2/Vector256,
portable Vector128 (including ARM64), or scalar for only the 1–3-pixel all-tail
case. Forced intrinsic fails closed without Vector128 hardware support, and
`CanvasDevice.LastPixelConversionPath` exposes the selected path.

The 11-test suite compares automatic and forced SIMD with the scalar oracle,
checks the four-byte WinRT ARGB layout, an 11-pixel bounded tail, destination
canaries, and small-buffer dispatch on macOS, Windows ARM64, and Linux ARM64.
The live checker uses asymmetric Color values and records Vector128 for its
four-pixel update plus ScalarReference for the 1x1 bounded tail. The exact
`16+2` hashes are Metal
`D72F667FCB6AC14B2C28A1C45001734C3B62B85B1816069521C9019985D1B39B`,
Parallels WDDM D3D12
`319939D4E5CC8544502BE837B04FDD8DD68D4F54ADB8D8AB83B49D86A4120122`,
and Ubuntu llvmpipe/Vulkan
`D2410112CF400C826A4855C134AE93E236932C879F690F93AA5B4422075B09C8`.
The new checker is exact; the full-frame differential remains two
Metal/D3D12 and 84 D3D12/Vulkan pixels at 1/255.

For 262,144 pixels, allocation-free scalar/Vector128 p50 is
`241.740/28.657 us` on Apple M3 Pro, `469.310/63.055 us` in the Windows ARM64
VM, and `237.920/29.545 us` in the Ubuntu ARM64 VM with identical checksums.
Apple ARM64 also records `1.742/0.240 us` for 256 pixels and
`2.601/0.320 us` for 4,096 pixels. The VM values qualify dispatch correctness,
not physical Windows performance. The exact Windows source archive SHA-256 is
`C8B1C7949EDE5BF18D85ED1B0E159E2C7B52056D4CA2721A4BDD493420B0477E`;
native C++ remains unchanged and reuses the qualified DLL/theme hashes above.

ProGPU `3dad29a9` completes the current Win2D bitmap-copy group. The whole-
bitmap, destination-offset, and source-subrectangle
`CopyPixelsFromBitmap(...)` overloads submit one typed same-device base-level
WebGPU texture-to-texture copy. A source texture lease covers submission and
the destination shares the byte/Color mutation guard. Active render-target
sessions, deferred destination leases, cross-device copies, and self-copy fail
closed. In particular, ProGPU does not use Win2D's cross-device system-memory
fallback because that would require a GPU readback and upload.

The live checker now executes all three copy shapes before its retained
image-brush draw and verifies both self-copy and lease-protected destination
rejection. macOS, Windows ARM64, and Linux ARM64 each pass 11/11 contracts and
a warning-free build. Their exact Metal, D3D12, and Vulkan hashes remain the
three `d51b289b` hashes above, proving the GPU-copy route preserves the exact
checker and the existing named differential. The exact Windows archive
SHA-256 is
`C545A591DBBE3FFBE274BF6D11DED211BCC5DA41CF34107E14E2A78A9434BD01`;
native C++/ABI and the qualified DLL/theme remain unchanged.
.NET SDK 10.0.400 rebuilt the complete managed graph with zero warnings and
errors; all 8 focused compilation, validation, ABI, ordinary-gradient, and
specular-gradient tests passed. Both live contexts selected
`Parallels Display Adapter (WDDM)`, backend `D3D12`, device type
`DiscreteGpu`. The specular readback reproduced 3,304 red-dominant plus 3,304
blue-dominant pixels and maximum channel deltas of 134 without a WebGPU
validation/device error. Native C++ sources did not change from the preceding
strict MSVC/D3D12 checkpoint.

The exact pushed ProGPU `fd455edf` checkpoint is independently qualified from
isolated archive SHA-256
`46B06076344DE8518622AD66F5C9BE129C5E6231FAB874066FE83BFFDB6E5201`
in Windows 11 ARM64 Parallels. MSVC 19.44 compiled both providers under
`/W4 /WX`; both export allowlists and all 11 CTests passed, and the managed
harness built warning-free. `Parallels Display Adapter (WDDM)` selected D3D12
as a discrete GPU and reproduced the 291-pixel clip plus 75/96 ordinary and
64/85 specular gradient evidence exactly. Qualified DLL SHA-256 values are
`635A68C0D9EDDD54230CC6CB8B37B6EDC8E994D6739AEA75FE006DDA44364EF5`
and `3DC03BD509449F560765FE9B9F73AEAD3DB4440D1CAF409D851834CBB847D722`
for the native and Dawn providers, respectively.

The same native face-mode addition closes the initial back-material gap.
LibreWPF maps each typed `PortableViewport3DMesh.IsBackFace` entry to an
exclusive ProGPU `FrontFace` or `BackFace` flag. ProGPU selects back or front
culling from its shared retained 3D pipeline family; zero remains the
source-compatible two-sided mode for non-WPF consumers. The Metal gate renders
front winding and reversed back winding in consecutive retained generations
and requires byte-identical clipped readbacks, while the focused compiler test
verifies that back-material identity survives the pointer-free sideband.

Source-built PresentationCore now has a focused executable test for the real
`Viewport3DVisual` exporter rather than only bridge-owned fake scene sources.
It constructs a `PerspectiveCamera`, transformed `ModelVisual3D`,
`GeometryModel3D`, source-built mesh collections, and distinct front/back
`DiffuseMaterial` values, then verifies the typed scene's viewport/camera,
computed normals, indices, composed model transform, material opacity/color,
geometry identity, face identity, and a `DirectionalLight` direction after its
accumulated source-built `Model3D`/`Visual3D` transform. The exporter now applies
that typed `Matrix3D` to the light before normalization instead of publishing
the untransformed local direction. A second case proves that an empty viewport
fails closed. Both cases pass in the portable macOS source-build lane.
The matching Windows 11 ARM64 Parallels attempt reached the source graph after
installing the pinned .NET 11 preview SDK into an isolated clone, but the guest
does not currently contain the Visual C++ targets required by
`DirectWriteForwarder`, the WPF native reference-tool payload, or a restored
`PresentationBuildTasks` asset graph. That host-toolchain gap is recorded
separately from the exporter result; no product assertion failed, and the
user’s active Windows checkout and Visual Studio installation were not changed.

The next typed lighting checkpoint stops collapsing WPF's complete light graph
into one directional plus one ambient value. `PortableViewport3DLight` now
preserves ambient, directional, point, and spot identity; linear color;
transformed position/direction; range; constant, linear, and quadratic
attenuation; and inner/outer cone angles. The real source-built exporter test
now traverses all four WPF light types and verifies transformed point/spot
state alongside the existing transformed directional state.

The native MIL route now compiles that array into ProGPU's bounded retained
light-buffer ABI. Each unchanged 256-byte mesh record addresses up to 16
80-byte ambient/directional/point/spot records through its former reserved
words; the auxiliary stream stores lights after vertices and indices and the
native page binds a sixth read-only WGSL storage buffer. The new versioned MIL
sideband entry point preserves the legacy zero-light call, and validation
rejects malformed kinds/ranges/slices, negative or all-zero attenuation, and
invalid spot-cone ordering before retention. Spot angles use the same clamp as
WPF MIL (outer `[0,180]`, inner no wider than outer), while point/spot specular
uses WPF's half-vector model. A zero light count retains the old directional /
ambient shader path exactly.

Focused bridge/compiler coverage passes 98/98, including multiple-light range,
spot-cone, point attenuation, and invalid attenuation cases; all 10 native
CTest executables and generated-contract/export allowlists also pass. The live
Apple Metal MIL gate executes ambient-plus-point and ambient-plus-spot retained
generations and reads center RGBA `91/85/0/255` and `103/78/0/255`, distinct
from the legacy `77/51/0/255` reference. The executable PresentationCore
exporter suite remains 2/2 on macOS.

The portable managed route now has the corresponding reusable ProGPU
implementation. `Viewport3DCompilationPayload` carries up to 16 typed lights;
the managed compositor reuses an 80-byte-record scratch array and per-viewport
GPU storage buffer, and both its solid/material and wireframe shaders execute
WPF-compatible ambient, directional, point, and spot lighting. Zero explicit
lights deliberately retains ProGPU's existing presentation-oriented PBR light
rig for non-WPF callers. LibreWPF now validates and maps the neutral typed DTOs
directly, including WPF cone clamping and attenuation rules, without reflection.
A real Metal headless render verifies red point-light and blue spot-light output,
while the bridge slice verifies all three lights survive into the managed
payload.

The same checkpoint now has bounded Windows DirectX evidence. An isolated
Windows 11 ARM64 snapshot rebuilt both ProGPU native modules under strict MSVC
warnings-as-errors; all 11 native/Dawn CTests passed, and both DLL export
tables contain the versioned Viewport3D light entry point. The live retained
MIL gate selected the
Parallels Display Adapter (WDDM) D3D12 backend and reproduced point RGBA
`91/85/0/255` plus spot RGBA `103/79/0/255`, within one blue code value of the
Metal reference. The ProGPU equivalents of Microsoft's D3D12HelloTriangle and
D3D12HelloTexture contracts also passed on D3D12 with SHA-256
`AE1BC0A9B0623BACAB15BE1706FFA3E7FC15E33676A66F05C969C1B86A66FEA3`
and `591CC311F35E3C2612F529C3D4D7061FC93751A9B8614BF588A73599B0AA2790`.
The broad standalone mixed-scene sample and managed headless Mesh3D test stall
on this Parallels driver after entering GPU submission, so neither is reported
as a pass; the bounded retained gate isolates and proves the new native light
route while physical/non-Parallels D3D12 coverage remains an open gate.

The WPF MCG `csp` tool also rebuilds cleanly and its unmodified `Resources.rsp`
regenerates all 378 resource outputs into an isolated temporary tree. The
generated Double, Point, Size, and Rect animation-clock resources retain their
typed portable value interfaces and methods, proving that ordinary source
regeneration preserves this bridge instead of overwriting it.

Current accepted ProGPU code checkpoint `23f6848d` was also rebuilt in the Windows 11
Parallels ARM64 guest with MSVC/Ninja after the DrawImage, typed animation, and
folded-NEON changes. All 10 non-Dawn native CTests passed; this focused
current-head gate supplements, but does not replace, the exact full D3D12 Box
checkpoint above.

That exact code checkpoint then completed the extended ARM64 MSVC/Ninja D3D12
smoke/package lane. Both providers built with zero warnings, all 11
native/Dawn CTests passed, native and managed allocation/readback samples
completed, and automatic raster, forced raster, forced SIMD, bounded scalar,
and typed forced-compute-rejection routes behaved as declared. SIMD retained
`5B6EF4F70536C862`; Box blur retained `D77D5DC8AC370BCE`. Both Microsoft D3D12
oracles and the complete retained cache/effect/mask/clip/text/blend matrix
passed, including byte-exact Overlay and ColorDodge. Staged SHA-256 values are
`9D2E6713B9CF8EE97B58B6ED8BB6B73A4C4DF19AED9C5AF5248C0DF522D45266`
and `51BA93113AB6CA6D76DE29BD5DE83C8397808C44EDD21F277244772779B353EC`.

The preceding exact `e510039d` Windows checkpoint completed the entire
Parallels D3D12 lane: strict ARM64 MSVC `/W4 /WX`, 11/11 native/Dawn CTests,
forced raster/NEON/scalar parity, expected pre-resource compute rejection,
Microsoft triangle/texture oracles, managed-picture, retained MIL effects,
masks, clips, text, blends, and package staging. Its DLL SHA-256 values are
`5B140B2D5881C3847ECBD6D4E7F8B592DD54C24E2687915EDF30BCA4BC78796D`
and `7D7F35CFA5323D0BA6E61EA402788CBAE72EBA40D69FE5B3D05069C966AB56DB`;
wgpu-native is
`9F73E41536B3BD96A0A44692EA65888C9DE004B19FBF5DE90489768667FBBDBC`.
The current full-D3D12 rerun above now covers the newer PushEffect, animation,
image-draw, Box, and SIMD checkpoints together.

ProGPU checkpoint `ba7b5d74f40d554a6267aeabe3807fe989260cc4`
closed the WPF gradient-normalization differential. Native MIL now applies the
same stable double-to-float ordering and strict relative
`10 * FLT_EPSILON` coincidence test as WPF, retains only the first/last colors
of internal coincident chains at one exact offset, and preserves WPF's distinct
Pad colors outside duplicate zero/one endpoint groups. The unchanged 256-byte
canonical brush carries those two extension colors in its existing inline
slots behind a validated flag, and the shared Vector, Hatch, and native 3D
shaders select them only outside the unit interval. The ordered normalization
pass remains scalar by design because each result depends on the preceding
normalized stop; this is documented as a non-SIMD resource-compilation path,
while the actual per-pixel work remains GPU shader execution.

The exact archive SHA-256
`9A22CC63BB972FD2549C937B88503F4284D8AB3A1874182A87BC9D1EE4376D01`
passed strict Windows 11 ARM64 MSVC/Ninja compilation, all 11 native/Dawn
CTests, a zero-warning managed build, and 8/8 focused managed gradient/3D
tests. The native sample produced identical Metal and Parallels D3D12 samples:
start Pad `250/133/20`, in-range start `0/255/4`, in-range end `0/255/253`,
and end Pad `184/51/245`. Provider SHA-256 values are
`F46B10C0B21D171D4AF1830F85D7499BF4BE4E43B550A53B3D27145340657EEB`
and
`B32E22C7BCF4A11F7BB64D60199670DEE3E9DDA0718FC006190A55069CDE27DF`.
The adjacent cap-only gradient-pen gap is closed at ProGPU implementation
`a124dcb9`. Zero-length lines and point-degenerate ellipses now pass their exact
WPF stroke-sizing bounds into the existing canonical GPU gradient path. One
checked native helper owns both brush mapping and cap draw bounds; asymmetric
Flat/Triangle caps retain their half-stroke X extent while round point ellipses
retain the full stroke square. This adds no ABI, CPU rasterization, readback, or
backend fork. The constant-size bounds calculation is dependency-driven scalar
control work rather than a lane-independent hot loop; all material sampling
remains in the shared GPU fragment shader.

Exact archive
`D38337F1AFB33F7E5C4DA9D6BC08D65AEBC544C4E9E5881CE2FD3BF56A672832`
passed strict Windows ARM64 compilation, 11/11 CTests, and the direct D3D12
sample with the same four pixels as Metal. The managed graph built with zero
warnings/errors; eight focused gradient tests passed in fresh GPU test hosts.
One initial combined host hit a `wgpuDevicePoll` access violation after the
native sample, while the exact crash-site test and every other group passed in
isolation, so the provider/VM process-lifetime event is recorded rather than
treated as a product result. Qualified provider hashes are
`8213074DAB22FBBAD630BEAF8BF87E09522B77730E7D92E5E33812BC9C68590D` and
`0E2C0667243F49475E81B23FF7E56999F7E4095D906B1A283637EB7CC148B47E`.
The superproject now tracks ProGPU documentation checkpoint `9deaefa9`.

ProGPU implementation `1c3bd210` and documentation checkpoint `fd7ac143`
next add exact CombinedGeometry children to EvenOdd GeometryGroup fills and
vector clips. Ordinary children retain the compact outer-fill contour leaf;
the boolean child retains its existing postfix subtree and each subsequent
nonempty child appends one XOR. Native regression coverage verifies the exact
five-node `leaf leaf difference leaf xor` program for both fill and clip and
requires the equivalent Nonzero group to fail closed. The compiler keeps the
32-child/63-node bounds, transactional segment/node rollback, and the existing
per-point guideline fill path. Its ordered `O(S + N)` graph walk is not an
independent-lane SIMD candidate; all pixel coverage remains on the shared GPU
path rasterizer without CPU readback or repacking.

Exact archive
`71443727B66A565CF9D270807976859460B29EFBCBB84511630748A830B2CD37`
passed the strict Windows ARM64 312-step dual-provider build, 11/11 CTests, and
the direct Parallels D3D12 sample with Metal's exact four Pad-gradient pixels.
The serial managed graph built in 4:13.66 with zero warnings/errors. The known
combined-host `wgpuDevicePoll` access violation recurred after the native
sample; fresh hosts passed the builder test, all five 2D gradient tests,
ordinary Mesh3D gradient, and specular Mesh3D gradient. The specular readback
retained 3,304 red-dominant and 3,304 blue-dominant pixels with maximum deltas
of 134. Qualified provider hashes are
`D00CEAB00E6E06C18E49D3952DB80A2593B53727BA23B67EB1914013E76AC828` and
`F17C61D361C9C5F51B19E4B602FA052C55A614895886B27DBDD5E8C7B6182FC5`.
ProGPU checkpoint `711e169f` additionally runs that exact five-node program in
the provider-resolved Metal vector-mask fixture. Live Apple M3 Pro readback
asserts surviving cyan coverage, a clear Difference hole, and a clear final-XOR
island; the complete configured native/provider matrix passes 12/12. The
standard hardware sample checkpoint `3bd6bb40` then adds the same isolated
boolean tile to the shared Metal/D3D12/Vulkan oracle. Metal and Parallels
D3D12 both produced inside `51/209/242`, Difference hole `5/6/10`, and XOR
island `5/6/10`, while preserving all four gradient pixels. Exact archive
`B8740F7C484A1B763253185C1DBC395D07A0016B4E691CB86A271F5ABAEEDF89`
passed the Windows 312-step dual-provider build and 11/11 CTests. Qualified
provider hashes are
`C5E90611B1BDB249DB940A11AC6F8C4C5816392FF14BE9A7D5A5246AAD177991`
and
`C29207284FDDC19E193A131651F7A70E10ECABF12D1BD9816A6954E3E6808655`.
The superproject now tracks ProGPU documentation checkpoint `ff930177`.

ProGPU implementation checkpoint `b97b99e3` closes transform-bearing
SolidColorBrush protocol parity without changing uniform-color rendering.
Absolute and relative transform handles are retained, type-checked,
deletion-protected, and included in dependency revisions, while realized
color/opacity remains transform-invariant exactly like WPF MIL. The same
checkpoint documents the append-only dynamic-guideline state machine and ABI
required for exact Start/Quiet/Animation/Landing/Flight behavior; dynamic
guidelines remain fail closed until DPI, monotonic time, stable request serial,
VisualBrush-use state, idempotent compile/copy, and scheduler feedback are all
present together.

The exact `b97b99e3` archive completed the full Windows 11 ARM64 Parallels
D3D12 smoke/package lane. MSVC 19.44 rebuilt the 312-step dual-provider graph
under `/W4 /WX`; all 11 native/Dawn CTests passed; automatic/forced raster and
forced NEON retained `5B6EF4F70536C862`; the bounded scalar oracle retained
`6C59592F05595EFE`; and forced compute failed at the typed pre-resource
boundary. Microsoft D3D12HelloTriangle and D3D12HelloTexture ProGPU oracles,
native mixed-picture stress, bounded managed/native parity, retained
cache/guideline/Viewport3D/effect/clip/text/blend families, and runtime package
staging all passed. The VM lacked PowerShell Core and Parallels Tools guest RPC,
so the repository script ran under Windows PowerShell 5.1 with `IsWindows`
defined only in that child process; no machine policy or installed software was
changed.

Two further SIMD scratch-layout candidates were measured and rejected rather
than merged. Retaining coverage/crossing/curve vectors improved 1x medians but
regressed 2x p50 by roughly eight percent. Replacing the reserved crossing
vector with an exactly bounded uninitialized arena removed append-capacity
checks and stayed byte-exact across 48 processes, but its uncontended 1x
extension regressed submission/frame p50 and p95; the combined frame p95 was
3.0% worse. ProGPU therefore keeps the qualified folded two-pixel NEON/SSE2
kernel and documents both negative results. The superproject now tracks ProGPU
documentation checkpoint `58b35ccb`.

ProGPU checkpoint `c585d26a` implements the first append-only dynamic-guideline
ABI/state foundation without changing the legacy build contract. Both native
providers now export a size-versioned stateful scene-build call carrying typed
target/scene/generation identity, actual X/Y DPI, monotonic nanoseconds, a
stable nonzero request serial, and VisualBrush-use context. Its typed result
reserves `NeedsMoreCycles` and next-due-time scheduler feedback and reports the
exact stream size. Unknown flags, reused serials with changed frame fields,
invalid DPI, nonzero reserved data, and undersized structures fail closed.

The native channel caches the immutable stream, metrics, and result for an
identical request, making managed size-query/copy idempotent before any dynamic
state is allowed to advance. Successful transactional batches and typed
bitmap/drawing/cache/Viewport3D/font sideband updates invalidate the cache;
failed mutations preserve it. The managed ProGPU API exposes the same typed
request/result and validates serial/byte-count consistency. The full macOS
dual-provider build passed 12/12 CTests, the managed backend built with zero
warnings, binary compatibility passed, both dylibs exposed old and new build
symbols, and the project-reference package consumer compiled byte-identical
legacy/stateful scenes through wgpu-native and Dawn. Dynamic GuidelineSet still
fails closed: the per-resource Start/Quiet/Animation/Landing/Flight phase state
and scheduler invalidation are the next implementation checkpoint.

ProGPU checkpoint `83759aa1` adds the shared semantic output required by that
phase state. An append-only explicit-offset flag extends guideline payloads
with one physical-device-pixel offset per existing sorted coordinate without
changing the resource header or static payloads. The typed builder and scene
validator enforce matching counts, finite sorted coordinates, finite offsets
within WPF's one-pixel driven range, and valid multi-guide modes. The common
semantic cursor applies those offsets with exactly one target-DPI conversion,
so WebGPU/Dawn and DirectX require no separate animation implementation. The
full native/provider matrix remains 12/12; MIL dynamic resources still fail
closed until their retained phase machine emits this representation.

ProGPU checkpoint `897aeb68` implements the retained dynamic-guideline phase
machine on the versioned stateful build path. Per-resource X/Y pairs now carry
WPF-compatible Start, Quiet, Animation, Landing, and Flight history. The native
compiler applies the 200 ms movement window, three-device-pixel jump
suppression, 0.05-device-pixel landing steps, VisualBrush suppression, and
rotation/shear Flight behavior, then emits the shared explicit-offset semantic
resource for both providers. Animated and landing builds return typed
`NeedsMoreCycles` feedback with a saturated 50 ms next-due timestamp.

Dynamic phase mutation is transactional: the channel copies its graph only
when dynamic guideline resources exist and commits their history only after a
complete scene build. An unsupported record after guideline evaluation cannot
consume a landing step, while an identical request serial returns the cached
bytes/result without advancing state twice. Focused tests cover phase changes,
scheduling, VisualBrush behavior, failed-build rollback, Flight recovery, and
large-jump suppression; all 12 configured native/provider tests, the managed
native-backend build, and the wgpu-native/Dawn package consumer remain green.
Nonuniform X/Y DPI and compact `PushGuidelineY1/Y2` lowering remain explicit
fail-closed gaps.

ProGPU documentation checkpoint `d1b585e3` records the next SIMD
qualification. A proposed NEON multiply-accumulate reduced to an already
rejected direction-mask form in Clang output. A distinct packed crossing load
remained pixel-exact but regressed forced-SIMD native submission p50 by 16.9%
at 1x and 26.5% at 2x in the preliminary 120-frame component gates, so it was
reverted without extending a decisively negative candidate. The qualified
folded two-pixel NEON/SSE2 kernel remains unchanged.

ProGPU checkpoint `b91f34c9` lowers compact `PushGuidelineY1` and
`PushGuidelineY2` render-data records through the same retained WPF phase
machine and explicit-offset semantic resource. Each render-data resource owns
its compact phase histories by stable packet offset; replacing its bytes
clears those histories, while unrelated resource updates retain them. The
stateful compiler detects the records before selecting its transactional graph
copy, and the legacy compiler rejects them before allocating or mutating phase
state. Focused coverage proves initial Y snapping, Animation feedback, the Y2
driven gap, render-data replacement reset, and legacy fail-closed behavior.
The regenerated serialized native build is incrementally clean, all 12 CTest
suites pass, the managed native backend builds without warnings, and the
wgpu-native/Dawn project-reference package consumer passes. Nonuniform X/Y DPI
and consumption of `NeedsMoreCycles` by LibreWPF's typed render scheduler are
the remaining dynamic-guideline integration gaps.

ProGPU checkpoint `de22cef2` makes scheduler timing reusable across UI hosts.
`NativeMilSceneBuildTiming` validates known flags and request/result identity,
then converts the absolute monotonic due time into an overflow-safe relative
`TimeSpan`, rounding upward so a phase is never advanced early. LibreWPF now
feeds that delay directly into `IWpfDelayedRenderScheduler`, upgrades any
coalesced MediaContext wake-only request into a real presentation request, and
wakes the native loop. Completed scenes schedule nothing; overdue work runs on
the next scheduler turn; mismatched serials and unknown flags fail closed.
Six focused host tests cover delayed presentation, completion, validation, and
existing MediaContext coalescing behavior. Runtime binding of the native MIL
compiler/session is still required before ordinary windows can produce this
feedback end to end.

ProGPU checkpoint `f9647a6d` closes the native nonuniform-DPI guideline gap.
The shared resolver now advances X phase state and converts coordinates with
`dpi_scale_x`, while Y independently uses `dpi_scale_y`; explicit offsets stay
in physical device pixels. Retained X pairs pass exact 1.25x/1.5y coverage and
compact Y1/Y2 pairs pass exact 1.25x/2.0y initial, Animation, and driven-gap
coverage. The full native/provider matrix remains 12/12 and the wgpu-native/
Dawn package consumer passes against the rebuilt libraries. Dynamic-guideline
work is now native-complete for the implemented command families; persistent
incremental LibreWPF compiler/session ownership remains the end-to-end runtime
integration requirement.

The LibreWPF typed batch translator now preserves
`PortableGuidelineSet.IsDynamic` in canonical `MilCmdGuidelineSet` packets
instead of rejecting the source-built WPF contract. Dynamic X/Y arrays remain
pair-validated by ProGPU's `NativeMilBatchBuilder`, and both drawing-group and
render-data `PushGuidelineSet` references retain the generated native handle.
Four focused tests verify the dynamic flag, exact pair byte counts and values,
scope handle wiring, and unchanged static single/multiple guideline output.

ProGPU checkpoint `2289e9ed` exposes typed compact-guideline production through
`NativeMilRenderDataBuilder.PushGuidelineY1/Y2`, with finite-value validation
and exact generated-command plus DWORD-size framing verified by the package
consumer. LibreWPF's native batch translator now recognizes the corresponding
16-byte and 24-byte source-built WPF records, writes them through those typed
APIs, and balances each scope with the ordinary canonical `Pop`. Focused
coverage verifies both coordinates, the driven offset, command sizes/IDs, and
stack framing without copying opaque input bytes.

LibreWPF now owns a typed `WpfNativeMilCompilationSession` across presentation
frames. It builds deterministic full producer snapshots, validates their DWORD
packet framing, and compares packet command/handle identity positionally so
repeated structural commands such as child insertion cannot collide. Stable
topology emits only changed mutable packets into the existing native channel;
unchanged frames issue no protocol batch. Changed resource creation, visual
creation, child topology, target-root identity, packet ordering, or target
handle builds a complete replacement channel and swaps it only after the full
batch plus typed sidebands succeed. A sideband failure after a transactional
delta marks the session unusable until a full replacement succeeds.

Stateful frame compilation reuses that retained channel and returns the exact
`NativeMilSceneBuildRequest` beside its semantic scene/result, allowing the
existing host continuation scheduler to validate serial identity and schedule
the native phase deadline. Six packet-differ tests cover unchanged snapshots,
a single mutable update, nested RenderData updates, structural rebuilds,
identity changes, and malformed framing. Direct compositor target binding and
an explicit runtime selector remain separate gates; the current managed
portable renderer is still the default.

The retained session also diffs typed sidebands before crossing the native
ABI. Bitmap metadata and RGBA8 bytes, glyph face/style metadata and SFNT bytes,
and drawing-image, drawing-group, and visual-cache bounds are compared by
value; unchanged payloads are not rebound or recopied. `AppliedSidebandCount`
reports how many bindings a session update actually applied. Sideband handle
count or ordering changes rebuild the transactional channel so removed or
replaced bindings cannot survive as stale native state. Viewport3D scenes are
still rebound on each dirty producer update until that contract publishes a
canonical revision or content hash; comparing struct padding or managed array
identity would risk skipping real scene changes. Focused tests cover typed
handle topology plus byte-exact bitmap and font equality.

ProGPU checkpoint `20a7438b` adds the reusable target boundary required by the
host. `NativeSceneExternalTarget` carries a host-owned WebGPU texture-view
identity and pixel dimensions into the existing provider-resolved
`NativeCompositor`; it transfers no texture ownership and performs no copy or
readback. The original `GpuTexture` overload delegates to the same frame
builder while retaining its stronger device/format/usage validation. The
project-reference package consumer renders the compiled MIL scene through
both paths and waits for a valid external-target submission.

LibreWPF exposes the explicit `ProGpuWpfRendererMode.NativeMilWgpu` option and
keeps `ManagedPortable` as the default. The native host owns one compilation
session and native compositor beside the existing window/context, applies
typed retained deltas only for dirty WPF state or resize, compiles every
stateful continuation with nonzero monotonic request identity, installs the
semantic stream, renders directly into the acquired swapchain view, presents,
releases both the view and acquired surface-texture reference on every terminal
path, and feeds `NeedsMoreCycles` back into the existing delayed scheduler. Public
last-update/frame metrics expose the selected path without reflection.

This first forced lane fails closed instead of silently mixing unsupported
managed callbacks, popup roots, window-region clips, partial viewports, or
nonuniform presentation DPI. Applications using portable PresentationFramework
activation can preserve the selector through `CreateHostOptions(window,
fallbackOptions)` and a typed host factory. Dawn surface ownership and those
explicit host features remain follow-up gates.

`eng/progpu-wpf-native-mil-host-smoke.sh` now validates that boundary through a
real source-built PresentationCore `DrawingVisual`, an owned native window and
swapchain, the stateful MIL compiler, and ProGPU's external-target compositor.
The gate waits for an actual presentation and requires a valid semantic scene,
a nonzero retained target, at least one native draw, and a nonzero GPU
submission before closing through the host dispatcher. Its load context shares
the single typed `ProGPU.Wpf.Interop` contract assembly; a duplicate contract
identity fails before the window is created. The 2026-08-27 Apple M3 Pro Metal
run presented one frame from three commands, three resources, one semantic
draw, and one submitted draw call.

The SDK gate controls this smoke with
`PROGPU_WPF_SDK_CI_NATIVE_MIL_HOST=auto|1|0`. `auto` runs when the current
ProGPU native library and a graphical session are available; `1` is the
required setting for qualification machines and fails closed when either is
missing. `PROGPU_NATIVE_BUILD_DIR` and `PROGPU_NATIVE_RUNTIME_DIR` select the
exact library under test. Run the same forced gate in the interactive Windows
Parallels user session after the ProGPU `win-arm64` native build to exercise the
live wgpu-native/D3D12 surface, and under X11/Wayland on Linux for the Vulkan
surface. Provider-specific adapter/readback evidence remains in ProGPU's native
Windows gate; this host smoke proves that LibreWPF reaches that provider through
the real window path.

ProGPU checkpoint `201a6c11` optimizes the managed glyph CPU fallback without
changing its scalar oracle or allocation profile. It uses fixed-width
`Vector256<T>`/`Vector128<T>` crossing evaluation, comparison-mask bit counts,
and a vectorized 16-byte coverage-normalization loop with a bounded scalar
tail. Odd and narrow widths (`1`, `15`, `16`, `17`, and `31`) are covered by
exact differential tests. On the Apple M3 Pro benchmark fixture, the median of
three process-level p50 results improved from `519.896 us/glyph` to
`293.924 us/glyph` (43.5%), while remaining byte-identical and retaining
`4,120 B/glyph`. The explicit scalar reference measured `49,097.540 us/glyph`;
that comparison also includes the already-qualified scanline-reuse algorithm,
so it is not presented as a SIMD-only speedup. ProGPU's C++ fallback remains on
its already-qualified NEON/SSE2 two-pixel kernels. Detailed benchmark method,
research sources, rerun commands, and rejected alternatives live in ProGPU's
`docs/GLYPH_CPU_FALLBACK_SIMD_RESEARCH.md`.

ProGPU checkpoints `2960fb39` and `ffb285af` continue that managed SIMD lane
by removing its remaining per-subscanline X traversal. The rasterizer now
collects all eight Y crossing spans first, builds each pixel's horizontal
Vector256 or Vector128 samples once, applies the eight winding spans, and
writes the exact quantized coverage byte directly. It keeps the scalar oracle,
one pooled crossing arena, stack-resident offsets, and the unchanged output
allocation; Vector128-only machines do not execute unsupported Vector256 setup.
All 19 focused differential tests pass. Eight alternating Apple ARM64
processes improved median p50/p95 from `218.649/227.590` to
`205.471/212.347 us/glyph` (6.0%/6.7%) with checksum 175 and
`4,120 B/glyph` throughout. The immutable final archive rebuilt with zero
warnings in Windows 11 ARM64 Parallels and three .NET 10.0.11 Vector128 runs
retained the checksum/allocation. Host and guest hashes matched
(`45BA556F...CD3FE0C` source, `C6A295B3...E1E242F` archive). Both available
Windows and Rosetta x64 runtimes reported `Vector256=False`, so actual
Vector256 execution remains an explicit x64 CI/hardware qualification gate.

ProGPU checkpoint `f8c6cc7e` removes the next measured managed glyph SIMD
cost. Every fixed-capacity Y-subscanline block now stores positive crossings
from the front and negative crossings from the back, so Vector128/Vector256
winding loops no longer load or branch on direction per crossing. Bounded
ref-plus-offset access also removes two transient span views per
pixel/subscanline, and the logical pooled crossing payload falls from eight to
four bytes per root. The scalar oracle, 8x8 grid, exact quantization, GPU-first
policy, and C++ fallback are unchanged. The differential suite expands to
opposed contours and empty glyphs and passes 21/21 on Apple, Windows ARM64,
and Ubuntu ARM64. Eight alternating Apple M3 Pro pairs improved median
p50/p95/p99 from `208.648/240.219/302.034` to
`174.606/222.808/262.180 us/glyph` with exact checksum and unchanged
`4,120 B/glyph`. The exact archive and pinned WinUI submodule content rebuilt
in Windows 11 ARM64, and three Ubuntu ARM64 runs retained the same output.
A self-contained Windows x64 publish reported `Vector256=True` and preserved
checksum 175 across three processes, closing functional 256-bit coverage;
Windows-on-ARM timing remains correctness evidence rather than a physical-x64
performance claim. ProGPU's glyph SIMD research log records the full paired
distribution, hashes, trace ownership, and rejected micro-optimizations.

ProGPU checkpoint `e07e1411` applies the same intrinsic requirement to the
shared managed PCM16 media hot path. Interleaved stereo gain/balance widens
signed samples into `Vector256<int>` or `Vector128<int>` lanes, performs exact
Q15 truncate-toward-zero scaling and saturation, narrows in place, and leaves
only a bounded scalar tail. Identity and zero blocks keep direct fast paths.
The differential test covers signed extrema, both channel offsets, vector
boundaries, zero/identity/asymmetric/saturating levels, and seeded full-range
data. On Apple M3 Pro, four alternating 48,000-frame Release runs measured a
median-of-run p50 of `25.537 us/block` for SIMD versus `150.519 us/block` for
the independent scalar oracle (5.89x throughput, 83.0% lower latency), with
exact output and zero measured allocation for both. ProGPU's
`--pcm16-simd [--scalar]` benchmark and
`docs/GPU_COMPUTE_FALLBACK_POLICY.md` preserve the rerun method and scope the
claim to the qualified ARM64 runtime.

ProGPU checkpoint `8a8ce383` extends that requirement to the shared Windows,
Linux, and Android native-export wide mixer. One typed SIMD implementation now
owns PCM16-to-Int64 Q15 accumulation and final saturating Int64-to-PCM16
conversion instead of three platform scalar copies. Differential coverage
includes mono/stereo patterns, vector tails, signed PCM extrema, wrapping
accumulator edges, and exact saturation boundaries. The complete 3,872-test
ProGPU assembly passes. Four alternating Apple M3 Pro runs over the product
1,024-frame block measured median-of-run p50 `2.027 us/block` SIMD versus
`6.139 us/block` scalar (3.03x throughput, 67.0% lower latency), with exact
accumulator/output/checksum results and zero allocation.

ProGPU checkpoint `e6236472` separately vectorizes that processed-float effect
lane for Windows, Linux, and Android without weakening its finite validation,
away-from-zero rounding, contribution clamp, or saturating Int64-add contract.
Valid float lanes widen to double through `Vector256` or `Vector128`; a vector
containing NaN/infinity resumes at its first lane through the scalar operation
so the exact exception and prior writes remain compatible. Vector-tail,
subnormal, signed-zero, midpoint, float-extrema, Int64-overflow, NaN partial-
write, mono/stereo, and allocation tests match the independent scalar oracle.
Four alternating Apple M3 Pro 1,024-frame runs measured median p50 `3.705
us/block` SIMD versus `8.064 us/block` scalar (2.18x, 54.1% lower latency).
The self-contained Windows 11 ARM64 Parallels x64-emulation lane reported
`Vector256=True` and measured `28.571` versus `38.003 us/block` (1.33x,
24.8% lower latency). Both platforms remained exact and allocation free;
ProGPU documentation checkpoint `e33a668a` records the complete measurements
and scopes the Windows result as emulated-x64 rather than physical-x64 evidence.

ProGPU checkpoints `2c7bf929` and `86855726` also remove the duplicated scalar
PCM16-to-float normalization loops from the Windows Media Foundation, Linux,
and Android decode paths. A shared allocation-free converter widens signed
samples through unrolled `Vector256` or `Vector128` lanes, applies the exact
power-of-two `1 / 32768` scale, and keeps only a bounded scalar tail. The
independent scalar oracle covers PCM extrema, vector boundaries and tails,
sentinel preservation, invalid ranges, exact output bits, and allocation
behavior; the complete ProGPU test assembly passes 3,877/3,877 tests. Three
fresh Apple M3 Pro 48,000-frame runs measured median-of-run p50 `10.451`
versus `33.191 us/block` (3.18x), with median p95 `27.255` versus `42.697` and
p99 `35.809` versus `47.037 us/block`. Four self-contained `win-x64` runs in
the Windows 11 ARM64 Parallels VM over the product 1,024-frame block reported
`Vector128=True`, `Vector256=True`, median p50 `1.492` versus `14.874
us/block` (9.97x), p95 `3.285` versus `23.408`, and p99 `5.406` versus
`30.666 us/block`. Both lanes remained bit exact and allocation free. The
Windows executable SHA-256 was
`95ECEAE96594EAE211491850692CD76FBDDC908800D69CCD1E59779A2E3B557F`;
documentation checkpoint `a50b57f8` retains the commands, raw runs, platform
scope, and caveat that this Windows result is x64 emulation rather than
physical-x64 evidence.

ProGPU checkpoint `e61970f7` applies the same rule to AVFoundation's real-time
non-interleaved float stereo mix tap. One shared allocation-free layout kernel
uses ARM64 `ST2`/`LD2` or SSE unpack/shuffle operations for the planar-to-
interleaved and interleaved-to-planar callback round trip, with a bounded
scalar tail. Mono remains a direct span copy; dependency-strided layouts above
two channels retain their scalar implementation. Exact vector-boundary/tail,
sentinel, length, round-trip, and allocation tests pass, the Apple project
builds with zero warnings, and the complete managed suite reports 3,878/3,878.
The first explicit ARM ZIP/UZP loop was measured and rejected because it was
slower than the scalar oracle; the native interleaved-memory form measured
median p50 `0.269` versus `1.241 us/block` (4.61x) over three Apple M3 Pro
1,024-frame runs. Four alternating self-contained Windows 11 ARM64 Parallels
x64-emulation runs measured median p50 `0.934` versus `1.369 us/block` (1.47x),
with exact output, zero allocation, and executable SHA-256
`E38F889B495687BDFFBE61747FAA51ED3C60446092613B28DA8CEC5E0E56EDD8`.
ProGPU's documentation retains p95/p99 values, the cold-host outlier, rerun
method, and physical-x64 qualification caveat.

ProGPU checkpoint `86f7ade8` removes the retained MIL compiler's curved-dash
fail-closed branch. The new C++ helper is a clean-room port of ProGPU's owned
managed `DashPattern`, `BezierSegmentGeometry`, `ArcSegmentGeometry`, and
`Compositor.TryCreateDashedStrokePath` algorithms. It uses the same 32-chord
Bézier and bounded 64-entry analytic-arc cumulative-length tables only to map
dash distances back to parameters; final line, quadratic, cubic, and arc spans
remain exact reusable ProGPU geometry primitives. Direct fixtures match the
managed quadratic/cubic/arc reference cases, odd-pattern duplication,
thickness scaling, negative phase normalization, and closed-seam SmoothJoin
merging. Retained-scene coverage verifies dashed arcs and mixed closed curves
emit native bodies and typed caps instead of `unsupported_command`. The local
Apple Clang native matrix passes 8/8 CTests and the docs verifier passes; PR
#139 independently passes the strict GCC and MSVC compiler-compatibility jobs,
macOS/Ubuntu build-and-test jobs, native/managed image parity, and Windows,
Linux, and macOS Avalonia native Dawn contracts. The Parallels Windows ARM64
MSVC 19.44 lane also compiles the exact changed MIL library and test sources
under `/W4 /WX`; its reused older staged build manifest reaches link only after
successful compilation, then reports an unrelated missing newer scene-builder
object, so the clean PR MSVC job remains the authoritative link/test result.

ProGPU checkpoint `eddae8b3` routes the remaining positive-area analytic shape
outlines into that compiler only when a nonempty dash is present. Ellipses use
one closed full-sweep analytic arc; uniform and independent-X/Y rounded
rectangles reuse the existing exact line/quarter-arc contour. Solid ellipse and
uniform rounded-rectangle draws keep their original analytic fast paths.
Native scene tests now require multiple retained arc/body primitives plus typed
DashCap records for both shape families. This checkpoint's collapsed one-axis
ellipse and degenerate rectangle boundary is superseded by `f308c676`; rounded
degenerate rectangles remain explicit fail-closed work. Follow-up
checkpoint `2450bdd0` reuses the caller's already resolved native brush index
when an analytic shape enters the curve-dash lane, avoiding a second gradient
brush insertion and redundant brush resolution without changing the solid
fast path.

ProGPU checkpoint `c5a5e7b6` removes the remaining per-visible-run container
cost from the native curve-dash compiler. Run ranges, exact segments, and join
flags now occupy three flat buffers shared by every dashed path in one render
stream; the transactionally copied MIL channel state does not retain scratch
storage. A dense 256-segment contract produces 64 runs, 192 segments, and 128
joins, then requires identical storage addresses and capacities across 32
recompilations. This makes high-water reuse a tested steady-state
no-allocation property rather than a benchmark inference. The full native
Apple Clang qualification rebuilt 96 targets, passed 10/10 CTests, verified the
exported-symbol allowlist, and completed Metal retained render/readback; exact
curve and scene parity fixtures remain unchanged.

ProGPU checkpoint `4d714cb3` adds exact pen execution for `GeometryGroup`
resources whose children are typed `PathGeometry` values. The implementation
follows the tracked WPF `CMilGeometryGroupDuce::GetShapeDataCore` and
`CDrawingContext::DrawGeometry` ordering: figures retain their separate
open/closed and fillable/stroked state, child/group/drawing transforms compose
in WPF order, one aggregate pen brush is reused, fill submits before stroke,
and dash phase restarts per figure. Native fixtures cover a filled child plus
an explicitly unfilled-but-stroked closed child, independent transforms, exact
solid line/quadratic/cubic bodies, and dashed bodies/triangle caps on both
children. Rectangle/ellipse, nested, and boolean group children remain fail
closed for a meaningful pen; their stroke contours are the next typed
extensions rather than candidates for fill-boundary approximation. All 8
native CTests and both documentation/package verifiers pass locally.

Follow-up ProGPU checkpoint `43f23999` admits `LineGeometry` children through
the same group pen contract. Direct and grouped lines now share one resolved
line helper for solid/dashed submission, caps, bounds, metrics, and brush-index
reuse. The fixture adds an independently transformed open line, requires its
exact solid native primitive, then requires its two-point dashed stroke,
interval payload, phase, and start/end caps alongside the dashed path children.
Collapsed fixed shapes plus nested and boolean group strokes remain the
explicit fail-closed boundary. The complete 8/8 native CTest set and
documentation verifiers pass after the extension.

ProGPU checkpoint `ba0ee5ff` completes the positive-area fixed-shape portion
of group pens. Direct and grouped plain rectangles, analytic ellipses, and
uniform/nonuniform rounded rectangles now share one typed fixed-shape helper,
including solid analytic fast paths and dashed exact-curve paths. The expanded
group scene requires a 20-segment fill and independently transformed solid and
dashed rectangle, ellipse, and nonuniform rounded-rectangle strokes while
retaining one aggregate pen-brush realization. Collapsed fixed shapes remain
on their specialized WPF widening rules, and nested/boolean group strokes still
fail closed. All 8 native CTests and documentation/package verifiers pass.

ProGPU checkpoint `23e6b925` extends the same pen contract recursively through
nested `GeometryGroup` resources. Bounds and execution use matched typed tree
walks with the existing 256-level depth bound; leaf, inner-group, root-group,
and drawing transforms compose in WPF order, singular nested branches produce
no coverage, dash phase still resets per figure, and one root pen brush is
shared. The fixture draws the same line directly at translation `(60,10)` and
through an inner group at `(260,25)`, requiring both transforms in solid and
dashed output. Collapsed shapes and boolean-boundary strokes remain fail
closed. All 8 native CTests and documentation/package verifiers pass.

ProGPU documentation checkpoint `ab6107e4` additionally qualifies both
`Vector256` product kernels with the self-contained `win-x64` benchmark in the
Windows 11 ARM64 Parallels integration VM. .NET 10.0.5 reported `arch=X64`,
`Vector256=True`; all runs retained exact scalar checksums and zero allocation.
Median-of-run p50 was `48.669` versus `171.175 us/block` (3.52x) for the
48,000-frame gain/balance path and `1.277` versus `4.877 us/block` (3.82x) for
the 1,024-frame wide mix. This is emulated-x64 correctness and relative
performance evidence, not a physical-x64 hardware claim.

ProGPU checkpoint `4dbd79ac` further reduces processed-float SIMD validation
overhead without changing its scalar oracle or failure boundary. The hot lane
tests the IEEE-754 exponent field directly and falls back at the first vector
containing infinity or NaN. Its differential corpus now includes 4,099 finite
raw bit patterns, adjacent positive/negative half-boundary values, alternating
Int64 saturation edges, and all supported level pairs. Six paired Apple M3 Pro
runs reduced median p50 from `1.926` to `1.858 us/block` (3.5%) and p95 from
`18.348` to `17.033 us/block` (7.2%), with exact checksum `-68911` and zero
allocation. Measured two-vector unrolling and forced helper inlining were
rejected because they did not improve the product workload. The exact
self-contained `win-x64` checkpoint then ran in the Windows 11 ARM64 Parallels
guest with .NET 10.0.11 and `Vector256=True`. Four alternating runs measured
median p50 `13.059` versus `23.917 us/block` for SIMD and the scalar oracle
(1.83x), with exact checksum and zero allocation. Guest p95 remained noisy and
is not used as a speed claim; this qualifies x64 emulation, not physical x64.

The native MIL checkpoints from `f2b0b579` through `96c0a2d2` also remove
sideband bounds from additional `DrawingImage` brush-mapping cases. Fixed
positive rectangles, rounded rectangles and ellipses; exact line, quadratic,
cubic and arc paths; transformed paths; and nested single-child geometry
groups now derive exact emitted fill bounds in C++. Multi-child groups,
stroked drawings and boolean cancellation remain fail closed until their exact
WPF bounds semantics are implemented. A clean archive of `96c0a2d2` compiled
all 136 focused target steps in the Windows 11 ARM64 Parallels VM with MSVC
`19.44` under `/W4 /WX`; `progpu_native_mil_tests` passed in 1.26 seconds.
Checkpoint `4dbd79ac` changes only managed PCM code and documentation, so its
native MIL sources are byte-identical to that Windows-qualified archive.

ProGPU checkpoints `1e764396` and `18e72815` first infer sideband-free
`DrawingImage` bounds through nested default-state `DrawingGroup` trees.
Native coverage uses two
independent rectangle drawings through two transformed group levels and then
requires a sheared update to return `unsupported_command`. The complete Apple
native suite passes 8/8. Exact changed-file hashes were rebuilt under Windows
ARM64 MSVC 19.44 with `/W4 /WX`; the focused native MIL test passed in 1.67
seconds after that compiler exposed and the follow-up removed a shadowed depth
name in the recursive lambda.

ProGPU implementation checkpoints `022a44cc` and `b83c1b5f` then match WPF's
authoritative `BoundsDrawingContextWalker` state rules: static
fixed/path/geometry-group clip bounds intersect each local child before union
and the group transform, so empty space between separately drawn children is
not added to the bounds.
Opacity, animated opacity, opacity masks, guidelines, edge mode, bitmap
sampling, and ClearType do not alter drawing bounds. Unsupported clip geometry
and non-axis-preserving group transforms remain fail closed. The differential
fixture combines all of those ignored states with a rectangle clip that
intersects only one of two separated children, verifies the resulting exact
DrawingImage mapping, and retains the shear rejection oracle. The Apple native
suite remains 8/8. Exact host/guest SHA-256 values matched at the final
implementation head (`3737C2B5...03DD` for the native source and
`6F60A9C7...BCCA` for its test), and Windows 11 ARM64 Parallels rebuilt the
native target under MSVC 19.44 `/W4 /WX`; the focused test passed in 1.12
seconds. ProGPU documentation checkpoint `d841466e` pins that qualification.

ProGPU implementation checkpoints `f5c3245d`, `81b5e0c4`, `dbec7d09`, and
`cc629948` provide exact sideband-free bounds for fixed GeometryDrawings with a
positive solid Pen.
The lane covers lines and positive-area rectangles, rounded rectangles, and
ellipses, then extends the same rule to zero-width/zero-height rectangles,
collapsed-axis ellipses, and point ellipses.
It reuses ProGPU's canonical live Pen resolver, cap-aware line bounds, and the
same positive-shape stroke-bounds helper used by native rendering, so a
thickness animation updates the image mapping without retransmitting the Pen or
Drawing. Fixed shapes require axis-preserving transforms, where transforming
the local stroke AABB remains exact; polygonal-cap lines gain broader affine
support in the checkpoint below. A missing DashStyle and an empty DashStyle
interval collection both select the solid lane; nonempty dashed and path/group
stroke cases fail closed. Native
coverage checks
square-cap mappings at two live thicknesses, positive and degenerate fixed
shape mappings, plus sheared-transform and nonempty-dashed-Pen rejection, then
updates that DashStyle to empty intervals and succeeds without retransmitting
the Pen or Drawing. Exact
host/guest hashes matched at the empty-DashStyle implementation head
(`680D3CBE...FBF7` native and `BD6A99C8...FECC` test); Windows ARM64 MSVC 19.44
`/W4 /WX` rebuilt the target and passed the focused test in 1.93 seconds.
ProGPU documentation checkpoint `e291d485` pins the result.

ProGPU checkpoint `34529979` begins general-affine solid line-stroke inference
for flat, square, and triangle caps by reducing actual strip and cap vertices
rather than transforming a broadened local AABB. Fixed shapes still require
axis-preserving transforms. Coverage verifies sheared square- and triangle-cap
mappings while retaining nonempty-dash
rejection and live empty-DashStyle success. Apple native tests pass 8/8. A clean
archive rebuilt all 136 focused target steps under Windows ARM64 MSVC
`19.44.35228.0` with `/W4 /WX`; focused CTest passed in 1.00 second and direct
execution returned zero. Host and guest hashes matched (`00917E30...3E64A2`
native source, `8EC1AC25...751CF6` test source), and the guest executable
SHA-256 was
`FE386E7FB3B93E0BE7125E8AD60B7005CBF6D2264C1A12FAA8A4EB2CC38A2051`.

ProGPU checkpoint `cd3e70c3` completes that cap lane and corrects transform
ordering against live Windows PresentationCore. WPF applies
`Geometry.Transform` to the line spine before Pen widening, but applies
`DrawingGroup.Transform` to the widened stroke; ProGPU now keeps those stages
separate. Round caps use WpfGfx's exact `ARC_AS_BEZIER` constant plus analytic
cubic extrema. For an 8-unit round-capped line under matrix
`[1,.25,.5,1,0,0]`, the Parallels WPF oracle returned
`15.999053955078125,18.499053955078125,28.00189208984375,13.00189208984375`
for `Geometry.Transform` and
`15.526884078979492,18.375919342041016,28.946229934692383,13.248161315917969`
for `DrawingGroup.Transform`; native tests lock down both mappings plus the
square and triangle variants. Apple native tests pass 8/8. A clean archive
rebuilt all 136 focused target steps under Windows ARM64 MSVC `19.44.35228.0`
with `/W4 /WX`; focused CTest passed in 1.62 seconds and direct execution
returned zero. Host and guest hashes matched (`C1A647F6...BA7E0F` native,
`554160BF...83E08D` test), and the guest executable SHA-256 was
`02BB1A776DBDC7A019D12BC362944B8253324334C97EF2B5796FFEB48F0AD2EE`.

ProGPU checkpoint `c16178cd` extends the same WPF ordering to positive-area,
non-rounded rectangle strokes. It maps the closed rectangular spine through
`Geometry.Transform`, widens normalized edges with exact in-limit miter or
bevel joins, and applies `DrawingGroup.Transform` afterward. For rectangle
`[20,10,30,15]`, thickness 8, and matrix `[1,.25,.5,1,0,0]`, live WPF bounds
were
`17.532926559448242,9.0101261138916016,52.434144973754883,34.479745864868164`
for `Geometry.Transform`, `19,10,49.5,32.5` for `DrawingGroup.Transform`, and
`21.422290802001953,11.119429588317871,44.655414581298828,30.261139869689941`
for the affine bevel join. Tests lock down all three mappings. WPF clipped
miters deliberately fail closed because the current native renderer reduces
an over-limit miter to a bevel; round joins, rounded rectangles, ellipses, and
dashed fixed shapes remain follow-up lanes. Apple native tests pass 10/10. A
clean Windows ARM64 archive rebuilt all 136 target steps with MSVC
`19.44.35228.0`; 161 Ninja flag lines carry `/W4 /WX`, focused CTest passed in
0.96 seconds, and direct execution returned zero. Host/guest hashes matched
(`D70BEB9B...267072` native, `83DB8A55...58449F` test,
`C7CD2AD9...A3A5F` archive), and the guest executable SHA-256 was
`D7F63F0EAEE4574872B88D20DD2E6E75C2DE71706D7094D342CC607434211CC8`.

ProGPU checkpoint `d1025caf`, finalized for strict MSVC/internal coverage at
`026ce1a7`, closes the clipped-miter gap in both native rendering and MIL
bounds. The shared tessellator implements WpfGfx
`CSimplePen::DoLimitedMiter`, derives the two limit-plane clip points from the
adjacent directions and radius, and emits a three-triangle fan; bounds consume
the same formula before the world transform. The live `MiterLimit=1` WPF
rectangle bounds
`20.276056289672852,10.497797012329102,46.94788932800293,31.50440788269043`
now map exactly. Apple native tests pass 10/10. A clean current archive built
153 MIL/internal steps under Windows ARM64 MSVC `19.44.35228.0`, with 161
`/W4 /WX` Ninja flag lines; both focused tests passed in 2.71 seconds and both
executables returned zero. Host/guest hashes matched (`924F4560...2F6FF6`
stroke header, `7E305BF5...B641C2` MIL, `0E2EC09A...FAB1E5` MIL test,
`10CD93E7...3D16D0` internal test, `0995611D...222AD` archive). Guest MIL and
internal executable SHA-256 values were `707D8EEA...6962D4` and
`4A36E7E6...0D1958`.

ProGPU checkpoint `269005a5` extends the exact affine rectangle lane to WPF
round joins. It mirrors WpfGfx `CSimplePen::RoundCorner` and
`GetBezierDistance`, including the 0.25 widening-tolerance refinement test,
the one/two-cubic split, and analytic cubic extrema after the group/world
transform. No transformed circle box or broadened local AABB is substituted.
For rectangle `[20,10,30,15]`, thickness 8, and matrix
`[1,.25,.5,1,0,0]`, live PresentationCore returned
`20.999963760376,10.9998416900635,45.5000743865967,30.5003185272217`
for `Geometry.Transform` and
`20.5268840789795,10.875919342041,46.4462299346924,30.748161315918`
for `DrawingGroup.Transform`; tests lock down both resulting native image
mappings. Apple native tests pass 8/8. The exact archive rebuilt 153
MIL/internal steps with Windows ARM64 MSVC `19.44.35228.0`; 161 Ninja flag
lines carry `/W4 /WX`, both focused CTests passed in 4.01 seconds, and both
executables returned zero. Host/guest hashes matched (`1CAB3180...F60569`
archive, `D6730AFD...91BDB` MIL source, `8581A4D5...FCD3F` MIL test); guest MIL
and internal executable hashes were `865BB142...99714` and
`287ECC0A...48117`.

ProGPU checkpoint `aadd184f` adds exact affine ellipse-stroke bounds for the
qualified non-refined profile. It reconstructs WPF's four float-quantized
`ARC_AS_BEZIER` cubics and mirrors `CBezierFlattener` hybrid forward
differencing at the 0.25 tolerance, using fixed-size arrays, before offsetting
the emitted tangents by the pen radius. `Geometry.Transform` is applied before
that widening and `DrawingGroup.Transform` afterward. For center `(20,30)`,
radii `(10,5)`, thickness 8, and matrix `[1,.25,.5,1,0,0]`, live WPF returned
`20.719608306884766,25.423517227172852,28.560783386230469,19.152963638305664`
for `Geometry.Transform` and
`20.239826202392578,25.299463272094727,29.520347595214844,19.40107536315918`
for `DrawingGroup.Transform`; native mapping tests lock down both. Profiles
that cross WpfGfx's thick-stroke refinement threshold still fail closed until
the extra `RoundTo` cubics are shared, and a regression test locks that
boundary. Apple native tests pass 8/8. A clean exact archive rebuilt 153
MIL/internal steps under Windows ARM64 MSVC `19.44.35228.0`; 161 Ninja flag
lines carry `/W4 /WX`, both focused tests passed in 1.57 seconds, and direct
executions returned zero. Host/guest hashes matched (`471096C4...D6234D`
archive, `F55BB225...ACFAF8` MIL source, `1BE90C6C...5D51BE` MIL test); guest
MIL/internal executable hashes were `BB97651B...85AAE6` and
`2B2E729A...8F8FE7`.

ProGPU checkpoint `7521787d` removes that thick-stroke boundary. Its
allocation-free fixed-array walker mirrors WpfGfx `CPen::AcceptCurvePoint`,
including the previous-tangent-to-chord and chord-to-current-tangent
`RoundTo` refinements, the intermediate chord offset pair, WpfGfx's very-flat
bevel endpoint, and the one/two-cubic round arcs with analytic transformed
extrema. With thickness 64, the same live WPF oracle returned
`-7.13843107223511,-2.55054593086243,84.2768588066101,75.101090669632`
for `Geometry.Transform` and
`-10.9766893386841,-3.54298257827759,91.9533739089966,77.0859665870667`
for `DrawingGroup.Transform`; the native tests lock both down alongside the
thickness-8 mappings. Apple native tests pass 8/8. The immutable archive
SHA-256 is `FA85410E...E3E01`; its exact sources rebuilt 153 MIL/internal
steps under Windows ARM64 MSVC `19.44.35228.0`, with 161 `/W4 /WX` Ninja flag
lines. Both focused CTests passed in 1.39 seconds and direct executions
returned zero. Host/guest source hashes matched (`8B71B473...77D4D` MIL and
`691DAD8A...CE95` MIL test); guest MIL/internal executable hashes were
`037D26AF...7BFBD` and `B5635F7D...6EE5`.

ProGPU checkpoint `e4d1d2c8` then brings the shared managed renderer onto the
same WPF clipped-miter rule through both the allocation-free span writer and
the allocating compatibility API. ProGPU checkpoint `2f55b1ba` below
subsequently scopes the rule to typed WPF/MIL consumers, preserving standard
join behavior for generic ProGPU, Svg.Skia, and downstream callers. The
original checkpoint fixed the four Unix native-renderer CI jobs without
relaxing their differential budgets: the formerly divergent
96-polyline Apple Metal frame is now byte exact (`max=0`, zero differing
pixels, identical `C67040E2A28F2507` hashes), with native and managed output
both using 3,408 vertices and 5,112 indices. The managed suite passes
3,880/3,880 and the Apple native suite passes 10/10.

ProGPU checkpoint `5a47e701` adds exact affine rounded-rectangle stroke
bounds. Its allocation-free contour walker consumes WPF's alternating smooth
cubic/line shape, widens after Geometry.Transform, applies DrawingGroup/world
state afterward, evaluates HFD flatness in device space, and derives the
`RoundTo` refinement threshold from the world transform's maximum singular
value. For rectangle `[20,10,30,15]`, radii `(5,3)`, thickness 8, and matrix
`[1,.25,.5,1,0,0]`, live WPF returned
`22.42738151550293,11.999236106872559,42.645235061645508,28.501526832580566`
for `Geometry.Transform` and
`21.880094528198242,11.876118659973145,43.739809036254883,28.747763633728027`
for `DrawingGroup.Transform`; both native image mappings are locked down.
Apple native tests pass 10/10 and 8/8 in the two configurations. The immutable
archive rebuilt 153 steps under Windows ARM64 MSVC `19.44.35228.0`; all 161
Ninja flag lines carry `/W4 /WX`, both focused CTests passed in 2.57 seconds,
and direct execution returned zero. Host/guest hashes matched
(`21C131E2...D8C4D` archive, `3D67CDD5...2B43C` MIL source,
`45ABD421...F6D0E` MIL test); guest MIL/internal executable hashes were
`F7E4E8F7...626C1` and `62A9AE08...F175`.

ProGPU checkpoint `f308c676` implements exact shared 180-degree reversal
joins and uses them to traverse collapsed MIL dash contours. Under WPF join
semantics, Miter and Bevel emit WPF's half-width three-triangle square; Round
emits the incoming semicircle. One-axis sharp rectangles retain their canonical four points and
one-axis ellipses retain the four ordered collapsed quarter traversals with
forced Round smooth joins. The semantic stroke resource continues to own dash
phase, DashCap, closed-seam merging, affine state, and backend-independent
execution, so this adds no WPF-only renderer or CPU readback. Fully collapsed
ellipses keep the typed visible-initial-dash point disk/initial-gap no-op rule;
fully collapsed sharp rectangles and rounded degenerate rectangles remain
fail closed. Live Windows PresentationCore probes cover all DashCap bounds,
the `2.0` boundary versus `2.01` gap transition, and each reversal join shape.
Apple passes 10/10 native CTests and 3,883 managed tests. Polyline Metal parity
is byte exact at `C67040E2A28F2507` with 3,408/5,112 vertices/indices on both
sides; dash parity retains matching 31,840/47,760 counts and the existing
one-channel raster-edge budget. The immutable archive SHA-256 is
`DD7E6B9D66305527E0F20F3445619F393943B00BEAABD4FEA88CD8450526491A`.
Windows ARM64 MSVC `19.44.35228.0` rebuilt 178 steps with 161 `/W4 /WX` flag
lines; focused MIL/internal CTests passed in 2.98 seconds and both executables
returned zero directly. Host/guest hashes matched for the six changed source
and test files; guest MIL/internal executable hashes were
`A5272CE9...FAAC` and `30295F83...D0D6`.

ProGPU checkpoint `2f55b1ba` exposes that qualification through the typed
`WpfJoinSemantics` policy. Generic managed join APIs and native polyline flags
retain standard renderer behavior, while explicit managed WPF APIs and every
MIL semantic stroke opt in to both clipped-miter and 180-degree reversal
geometry. The native semantic validator rejects this flag with hairline or
fixed-device strokes, so incompatible forced combinations fail closed. The
pinned Svg.Skia W3C gate returns to its reviewed inventory: native 530/533
with three skips and ProGPU 486/533 with 44 reviewed differences and three
skips; `animate-elem-35-t`, `painting-stroke-07-t`, and
`shapes-polyline-02-t` all pass individually. Apple passes 10/10 native CTests
and 3,885/3,885 managed tests. Generic polyline differential output is byte
exact at 3,360 vertices, 5,040 indices, and matching
`DE73D991697DAB3F` hashes. Dash differential topology matches at 31,776
vertices and 47,664 indices with no pixels outside the one-channel edge
tolerance (native `34DBC0EA94EF5BDB`, managed `D09D785B5B327753`). The
immutable archive SHA-256 is
`4420B6E1D842FDD4F2C9101FC7C438773FB53707A11060F2DD5A6F17EF8867D6`.
Its exact sources rebuilt 257 steps under Windows ARM64 MSVC
`19.44.35228.0`; all 10 CTests passed in 29.51 seconds and both focused
executables returned zero directly. Host/guest source hashes matched, and the
guest MIL/internal executable SHA-256 values were
`3082D4214B1B6147A8BD40B2D6B9A56D39A2B3FE929AADF071043A4E42DD56CC`
and `0328FBF9528582E54D7E90F4051290ACD1C7F419709DAE16BA3A0D87EE4CE872`.
LibreWPF advances its ProGPU submodule to this checkpoint.

ProGPU checkpoint `0f72b5f1` closes fully collapsed sharp-rectangle dashes by
reusing the typed degenerate-cap stroke path, including its finite dash phase
selection, and forcing WPF's Round/Round caps for a wholly degenerate closed
figure. A visible initial dash becomes one backend-independent point disk; an
initial gap becomes an exact no-op. Rounded degenerate rectangle dashes remain
fail closed. A live Windows 11 PresentationCore raster oracle with thickness 8
and dash array `[1,1]` returned the same 8-by-8 disk (60 covered pixels, alpha
sum 12,452) for every LineJoin and DashCap at offset `1.0`, and no covered
pixels at `1.01`. Apple passes all 10 native CTests. The immutable archive
SHA-256 is
`5BE5A14AA65021CA1D1273623169F766DBA834E968C9BEFF9A37BF0D96FBFFE3`.
Its exact sources rebuilt all 257 steps under Windows ARM64 MSVC
`19.44.35228.0`; all 10 CTests passed in 24.33 seconds and both focused
executables returned zero. Host/guest source hashes matched at
`D89307B4A78DB4BE457647F25C5C9DD1BC1305D3BE9535E444F1A0C693C3F90D`
and `D756F7E0138D44FAF012D34FF704A4A0EFCD6EAA03EF9AADDF8924C0BFC5C5AA`.
Guest MIL/internal executable SHA-256 values were
`7E907A8ADD470AEFA5904EB51FCCD697C648992BAE37E94283666E7A27FC07D4`
and `72633B0DB0A4B5A1908F6EB92AA8C0D469A3DB197A5EE09923B4712C60E7C1F3`.
LibreWPF advances its ProGPU submodule to this checkpoint.

ProGPU checkpoint `35edc9c6` closes dashed degenerate rounded rectangles when
both radii are positive. It independently clamps X/Y radii, recreates
WpfGfx's exact 17 float-point alternating cubic/line contour with
`ARC_AS_BEZIER`, and routes that contour through the existing typed curve-dash
compiler. Vertical, horizontal, asymmetric-radius, and point records retain
the shared dash phase, DashCap, smooth joins, exact cubic spans, affine state,
and DirectX/WebGPU execution; the point case reduces to the qualified
Round/Round disk or no-op. A live Windows PresentationCore oracle covered six
uniform/asymmetric vertical, horizontal, and point profiles across every
DashCap and seven offsets, locking phase-dependent bounds, alpha totals, and
pixel hashes. Apple passes all 10 native CTests. The immutable archive SHA-256
is `C9C4FD6BB74BF15EAB6CBD03408C36F23DF945C205B3A6FE038CE4520F62720D`.
Its exact sources rebuilt all 257 steps under Windows ARM64 MSVC
`19.44.35228.0`; all 10 CTests passed in 24.07 seconds and both focused
executables returned zero. Host/guest source hashes matched at
`667394D8B2BF70C10C14B9695144F4066EC6680A41F6B2B64E1C334EBD2AC2C0`
and `DAC859981EF978FCCDC1C7CEEF6E382F611DA2B23A0E01BF37E535B35AB89549`.
Guest MIL/internal executable SHA-256 values were
`DE5C145CA0529B82B292B43E558509476AE62C95C63819805115D0F77D0D37DD`
and `61ADE59E104E6D29FC4FDA04550FE2CFAE34C871455E3510E04ED07F606823C7`.
LibreWPF advances its ProGPU submodule to this checkpoint.

ProGPU checkpoint `649fe3a5` completes degenerate zero-radius normalization
using WpfGfx's explicit `CShape::AddRoundedRectangle` rule: if either radius is
zero, the record is a sharp rectangle before widening or dashing. Vertical and
horizontal one-axis records reuse the typed four-point semantic polyline and
WPF reversal joins; point visible/gap phases reuse the qualified Round/Round
disk decision. Coverage includes both asymmetric orientations, both point
phases, and the invalid brush-handle boundary. Apple passes all 10 native
CTests. The exact archive SHA-256 is
`E831663733B21EF2232F11F3225F27DDABDF1FF2198F6625DE157C4CD6C491BE`.
Against the fully qualified `35edc9c6` parent build, MSVC rebuilt the exact two
changed sources through the 7-step incremental graph; all 10 Windows ARM64
CTests passed in 7.53 seconds and both focused executables returned zero.
Host/guest source hashes matched at
`89D7E319A6E51F9AFBAA79DD21921D64645F7EF5B2F92C9BF1D1147801500858`
and `F16CB5EAA918C04A47BFB895A4682046D22BF195B56688A2428AA18746E7B63F`.
Guest MIL/internal executable SHA-256 values were
`F78174BC5DF1E31207F37BDD10AA56ED680EB965BA5E0B7F5A1107D97E666AED`
and `61ADE59E104E6D29FC4FDA04550FE2CFAE34C871455E3510E04ED07F606823C7`.
LibreWPF advances its ProGPU submodule to this checkpoint.

ProGPU checkpoint `30fcf084` removes the earlier group-level affine
restriction for supported fill leaves. The native walker now composes every
nested `DrawingGroup` transform into leaf geometry before calculating bounds,
transforms group clips into the same world space, intersects every transformed
child with the active clip, and only then unions the separately drawn results.
This mirrors WPF `BoundsDrawingContextWalker` ordering without broadening a
pre-transform union. Rotation and shear are exact for the supported fixed,
path, and single-child geometry-group fill lane; the solid fixed-line subset
has the broader cap-qualified affine support described above. Empty and singular
groups are valid empty draws. Coverage verifies the earlier axis-aligned
mapping, an exact sheared mapping, destination clipping, and empty singular and
childless results. The complete Apple native suite passes 8/8. A clean commit
archive rebuilt all 136 focused target steps in the Windows 11 ARM64 Parallels
guest with MSVC `19.44.35228.0` under `/W4 /WX`; the focused CTest passed in
0.79 seconds. Host and guest hashes matched (`AB4E6081...DFEE08` native source,
`61622AB8...B23767` test source), and the guest executable SHA-256 was
`DBDA27CD933D4D4A17B4FE70D55204A6481A16B9AA29DC8BF886974C3C82C6A4`.

ProGPU checkpoint `6a7652a9` adds nested `ImageDrawing` to that exact bounds
walk. In accordance with WPF `BoundsDrawingContextWalker.DrawImage`, a non-null
image source contributes its live static or animated destination rectangle;
the native walker applies the complete current group transform and active clip
without reading pixels or recursively inspecting image content. The fixture
wraps a vector-backed ImageDrawing in a sheared DrawingGroup and another
DrawingImage, verifies the derived general-affine mapping and destination clip,
then updates the ImageDrawing through a live `RectResource` and verifies the new
mapping without retransmitting either DrawingImage. Apple native tests pass
8/8. A clean archive rebuilt all 136 focused target steps under Windows ARM64
MSVC `19.44.35228.0` with `/W4 /WX`; focused CTest passed in 4.22 seconds and
direct execution returned zero. Host and guest hashes matched
(`6ACDD31B...DEEB` native source, `0B239F7E...D11C8` test source), and the guest
executable SHA-256 was
`08C83E4E428AD441321281AC701D05B28CF62B4D65B815B7F5ADA999E932BAAB`.

ProGPU checkpoint `14e870f5` adds exact `GlyphRunDrawing` bounds. Canonical WPF
already serializes `ComputeInkBoundingBox()` offset by `BaselineOrigin` as
`MilCmdGlyphRunCreate.ManagedBounds`, the same rectangle consumed by
`BoundsDrawingContextWalker.DrawGlyphRun`. ProGPU uses that typed packet field
directly instead of reconstructing metrics or inspecting font outlines for
bounds; null foreground brushes and empty managed ink boxes are valid empty
draws. Coverage renders the pointer-free SFNT glyph directly and through a
sheared DrawingGroup/DrawingImage, checks the complete affine mapping,
destination clip, and transformed glyph command bounds, and retains the
existing grayscale/ClearType/aliased text checks. Apple native tests pass 8/8.
A clean archive rebuilt all 136 focused target steps under Windows ARM64 MSVC
`19.44.35228.0` with `/W4 /WX`; focused CTest passed in 0.84 seconds and direct
execution returned zero. Host and guest hashes matched (`FA158FC6...BF398`
native source, `B2F34697...7CA6E` test source), and the guest executable
SHA-256 was
`48153630050BEDA01C79EDF0D9B4F7FE4EF5CBE881B89DD577820352D5E93604`.

The Windows host harness now accepts `PROGPU_WPF_REAL_ASSEMBLY_DIR` so a
deployment bundle can load one adjacent, source-built PresentationCore/
PresentationFramework graph instead of inferring repository artifact paths.
Its collectible load context resolves adjacent WPF dependencies first and
continues to share the single neutral interop contract assembly. The first
interactive Parallels D3D12 attempts exposed two cold-start starvation
boundaries. A dispatcher turn could consume callbacks posted by callbacks, and
both the owner loop and render callback drained that self-rescheduling WPF work
before the first native frame. `QueuedWpfDispatcherService` now processes only
the sequence snapshot present at turn entry and rejects nested processing of
the active turn. The native-MIL owner path compiles and presents its already
typed retained snapshot before servicing those callbacks. GLFW polling is also
temporarily nonblocking only inside the owner-driven native loop, closing the
separate empty-event lost-wakeup window. The loop retains its bounded 1 ms
active and 16 ms idle delays; externally pumped hosts retain their configured
event-driven behavior.

The 2026-08-28 Windows 11 ARM64 Parallels user-session gate selected
`Parallels Display Adapter (WDDM)` with backend `D3D12` and the fastest-policy
`RasterShader` glyph path. It acquired the swapchain texture, installed the
stateful semantic scene, submitted and presented before dispatcher/close
handling, then exited autonomously with `PASS`: one presented frame, three MIL
commands, three resources, one semantic draw, and one submitted draw call.
Focused dispatcher/host/source-contract coverage passed 188/188, the managed
test project built serially with no errors, and the real PresentationFramework
harness built with no warnings or errors.

ProGPU checkpoint `63d013e6` closes the typed DirectX-texture ownership boundary
needed by retained LibreWPF scenes. Eligible GPU-backed
`ProGpuDirectXTexture2D` resources now publish the shared
`IProGpuInvalidatingTextureSource` contract. The WPF recorder can retain the
same-device texture, transfer its reference-counted lease into a `GpuPicture`,
and compile it as a native external-image resource without readback, repacking,
upload, reflection, or a raw native-handle ownership contract. DirectX writes,
render/compute/copy completion, mip generation, writable unmap, and resize
publish retained invalidation. CPU-only, array, multisample, depth/stencil, and
non-shader-bindable resources fail closed.

The checkpoint passes ProGPU's full 3,875-test managed suite on Apple ARM64 and
the focused 3/3 Windows 11 ARM64 lease/invalidation/native-lowering gate from an
immutable archive. Detailed Windows diagnostics identify
`Parallels Display Adapter (WDDM)` and backend `D3D12`; the external-image test
passes in 480 ms on .NET runtime `10.0.11` with SDK `10.0.400`. This qualifies
the DirectX-to-native-MIL ownership and lowering seam on the integration VM,
not physical-adapter performance. LibreWPF now tracks the documented ProGPU
checkpoint so subsequent WPF image/effect work can consume the neutral lease
contract instead of adding a managed bridge-local workaround.

LibreWPF consumer checkpoint `8d466d211` carries that ownership contract
through retained WPF image replay. `IPortableNativeImageSource` payloads may
now publish either a direct `GpuTexture` or an `IProGpuTextureSource`, while
lease-capable payloads are retained by the ProGPU scene `DrawingContext` until
the recorded command list is cleared or disposed. A lease-capable source that
cannot produce a compatible lease fails closed instead of falling back to an
unowned raw texture. The invalidation tracker also traverses the typed native
image payload and subscribes directly to
`IProGpuInvalidatingTextureSource.TextureChanged`, releasing that subscription
when the retained tracker is disposed. This keeps owner disposal, resize, and
post-recording DirectX writes safe without reflection, CPU readback, or a
WPF-owned lifetime wrapper.

The focused consumer gate passes 4/4 tests, including retained lease disposal,
typed texture invalidation, LibreWinForms carrier ordering, and the package
graph contract. The complete `ProGPU.Wpf.Tests` assembly currently reports
1,448 passed and seven unrelated pre-existing source-shape failures in window
activation registration, shader review assertions, project inclusion,
render scheduling, and retained-branch-map assertions. Those baseline failures
are not counted as consumer-checkpoint regressions and remain visible rather
than being weakened or hidden by this change.

The package-mode gate now treats `ProGPU.Backend.Native` as a required runtime
package instead of allowing NuGet to resolve an older published managed
assembly beside current native binaries. The package list, SDK staging snapshot,
release manifest, README/release documentation, and package audit all include
the managed binding plus `progpu_native` and Dawn assets for `win-x64`,
`win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`. A source
pack with an incomplete RID set fails closed; release/package qualification
must consume the exact six-RID artifact produced from the tracked ProGPU
commit. This closes the stale-type failure that previously surfaced as a
`TypeLoadException` for `NativeMilBatchMetrics` only after the bundle consumer
started.

The exact-head production gate is qualified at ProGPU commit `6babe3f8` by
[workflow run 33140719250](https://github.com/wieslawsoltes/ProGPU/actions/runs/33140719250):
all 27 jobs passed and produced version `0.1.0-preview.1934.ci`. All 40 NuGet
packages were audited for that exact package version and repository commit;
`ProGPU.Backend.Native` contains native assets for all six required RIDs. The
same immutable artifact then passed the complete LibreWPF package-mode gate,
including generated MIL layout verification, native transport/host, real XAML
and `Application.Run`, Fluent, release bundle audit, bundle consumer, runtime
and external no-source-change harnesses, live geometry/input, the MVP renderer,
Toolkit/AvalonDock, paid Xceed, SciChart, and the focused package-graph guard.
The qualified bundle is
`librewpf-preview-0.1.0-preview.45.tar.gz`, SHA-256
`6b0faf9f9ba2f08f466a1a450f381cc65602cf6614f6f89fb238d43a7a8329d6`.
LibreWPF checkpoints `1c10d47a9` and `7b0a38248` make the exact ProGPU version
an explicit input to every packaged harness and separate serial build from
`dotnet run --no-build`, so the MSBuild concurrency flag can never leak into
application arguments. The tracked ProGPU submodule has since advanced to the
post-gate PCM16 normalization checkpoints above; the exact package evidence
intentionally identifies the earlier immutable commit that produced it.

The Windows managed payload and SDK transport fallback now pin the serviced
`Microsoft.WindowsDesktop.App.Runtime` `10.0.11` packages for x64, x86, and
ARM64. The immutable Windows qualification artifact contains all three RIDs;
the ARM64 Parallels DirectX lease/lowering tests pass on runtime `10.0.11` and
SDK `10.0.400`. Package values remain runtime/deployment inputs rather than
being recovered through reflection.

ProGPU's native CI also qualifies this checkpoint with strict GCC, MSVC, and
ClangCL builds. Portable engine flags no longer mix scoped enums with integer
bitmasks, and Windows ClangCL links its compiler-rt builtins explicitly so the
ARM64 and x64 native-MIL test executables resolve compiler-generated wide
integer helpers. Glyph fallback validation separates two contracts: dedicated
intrinsic-SIMD/scalar coverage tests remain byte exact, while independently
rendered native/managed final frames allow only the bounded GPU pipeline tie of
3/255 maximum per channel, zero pixels beyond that tolerance, and mean absolute
difference at most 0.001 byte/channel. On Microsoft Basic Render Driver the
forced raster and SIMD routes produced the same native frame hash and the same
bounded `2/255`, `0`-over-tolerance, `0.000109` mean result, proving that the
remaining tie is downstream GPU draw/sampling rather than CPU coverage
arithmetic.

ProGPU implementation checkpoint `d4ca87d9` and documentation checkpoint
`0e139ea2` close the canonical translated-equivalent two-leaf EvenOdd overlap.
At that exact checkpoint, the native compiler recognized only the
`leaf leaf xor` postfix form,
batched its two ordinary leaf records into ordered A/B GPU raster phases, and
combined their packed masks with the shared XOR compute kernel before the atlas
copy. Pending semantic work is flushed once and a fresh encoder is restored;
normal scenes retain the original single-submission path and do not allocate
the split buffers or bind groups. The path and vector-clip implementations
perform no CPU readback/repacking and no per-item submission. At that
checkpoint, larger or mixed translated-equivalent postfix programs remained
deterministically fail closed.

The exact implementation archive SHA-256 is
`CBA84443FA2EFC2AE74A6677370E9C6CF4E69729FEC5FEF15EC27E5EBEEB3DA2`.
Apple M3 Pro Metal passed 10/10 native CTests and the permanent XOR pixel gate.
The same source then rebuilt both providers under Windows ARM64 MSVC/Ninja in
the Parallels VM, passed 11/11 CTests, and reproduced cyan/clear/cyan pixels
`51/209/242`, `5/6/10`, `51/209/242` on the real D3D12 adapter. The complete
bounded Windows integration matrix passed managed/native readback, automatic
and forced raster-shader compute fallback, intrinsic-SIMD CPU and scalar
oracle modes, typed fail-closed forced native compute, DirectX HelloTriangle
and HelloTexture oracles, retained masks/effects/3D/text/images/caches, and the
final vector-clip/blur/effect/blend smoke profiles. Final provider SHA-256
values are
`894B9C4337FED2134245E0D59ED17E0A0BCBBE52988681453393D3462F48CA97`
and `BEC988C393985D52900A787F667501ADB4F7A3CB8ADC6A29EDF9497C7D7BFF4B`.
LibreWPF advances its ProGPU submodule to the qualified documentation
checkpoint.

ProGPU implementation checkpoint `db4ffef2` and documentation checkpoint
`e8ba9246` extend that split execution to the exact
`leaf leaf xor leaf xor` ternary form. The compiler emits three contiguous leaf
records; path fills and retained vector clips submit phase-batched A, B, and C
raster work, then the shared 32-byte combine record drives `A xor B xor C` in
the packed coverage shader. Phase-C buffers, bind groups, uploads, and a phase-C
submission exist only when a ternary program is present. Ordinary paths retain
the single-submission fast path, binary XOR retains its two leaf phases, and no
CPU readback, repacking, managed fallback, public ABI change, or per-item
submission is introduced. At that exact checkpoint, MIL tests accepted the
exact three-leaf program and proved that a four-leaf translated-equivalent
program still failed closed; the generalized checkpoint below supersedes that
temporary ceiling.

The exact source archive
`ProGPU-native-ternary-xor-db4ffef2.zip` has SHA-256
`70FFC3367638D5EDFA13DF9740578BFE808E57DED511625B08312C6B6B321807`.
Apple M3 Pro Metal passed 10/10 native CTests and the five-region permanent
sample as cyan/black/cyan/black/cyan. Hash-verified source rebuilt both native
providers under Windows ARM64 MSVC/Ninja, passed 11/11 CTests, and reproduced
the same pixels through `Parallels Display Adapter (WDDM)`. The expanded sample
executed eleven draws from eighteen retained commands and uploaded 12,960
vertex bytes. The complete bounded D3D12 integration matrix passed again,
including exact forced intrinsic-SIMD/scalar parity, typed fail-closed native
compute, and stable DirectX HelloTriangle/HelloTexture hashes. Final provider
SHA-256 values are
`C47649929661AC238ABD41CFCEA0486BE7F839AF0D6FD5E3023C4591F77AE020`
and `AE1B4271CB9D16296539170BA3C0191D45A149847A14E4B51C66F4B47A530A07`.
LibreWPF advances its ProGPU submodule to `e8ba9246`.

ProGPU implementation checkpoint `402ecfb9`, with MSVC portability checkpoint
`c5fb5244`, generalizes the fixed A/B/C resources to every pure
`leaf leaf xor ... leaf xor` program through the existing 32-child MIL ceiling.
The unchanged 32-byte combine record carries a source base, stride, and count;
path fills and vector clips use one phase-batched GPU mask resource per leaf
ordinal, and the shared WGSL kernel loops across the bounded source range.
Ordinary scenes keep their single-submission path without reserving the outer
split-program vectors. The path performs no CPU readback, CPU repacking,
managed fallback, public ABI change, or per-item submission. Mixed boolean
postfix programs remain typed fail closed.

Native coverage now includes binary, ternary, quaternary, mixed rejection, and
the full 32-leaf boundary. Apple M3 Pro Metal passed 10/10 CTests and rendered
the permanent four-leaf regions cyan/black/cyan/black/cyan/black/cyan. Windows
ARM64 strict MSVC/Ninja rebuilt both providers, passed 11/11 CTests, and
reproduced the binary, ternary, and quaternary sample through the real D3D12
Parallels adapter. The expanded sample executed thirteen draws from twenty-one
retained commands and uploaded 13,408 vertex bytes.

The immutable full source archive
`ProGPU-native-general-xor-c5fb5244.zip` has SHA-256
`D68DE4BDB753A1FCB3E7E2C6DF3DBF9C55D9BEE68FAD6F57BFD8FF43BDF2574E`.
The exact ten-file retry delta used after the shared-folder extraction stall,
`ProGPU-native-general-xor-delta-retry-c5fb5244.zip`, has SHA-256
`224E41DA07D2531FC93C5AD5DAF866FAC4F31A4DBFC3D54FC60E03FF61D2B538`;
all copied sources were hash-checked before the rebuild. The complete bounded
D3D12 integration matrix passed managed/native readback, fastest and forced
raster-shader paths, forced intrinsic-SIMD and scalar-reference CPU paths,
typed fail-closed native compute, stable DirectX HelloTriangle/HelloTexture
oracles, and all retained effect/mask/3D/text/image/cache smoke families. The
terminal ColorDodge result was pixel-exact with matching native/managed FNV-1a
`41DAE69420EE7C25`.

Final Windows SHA-256 values are
`07CB46633DE7AB2D872475CF2682D8AAA493D2CACDC707924948F625F0DDBA39`
for `progpu_native.dll`,
`E29F579504B66A974BD14786E4A6D9D4AACDDC1E30C07B9DA814196C6BBE7598`
for `progpu_native_dawn.dll`,
`372174B1FD90D370AB333301EAD0B0CC72895BE172E7AAAABD4D0C7C8BA3B5A6`
for the native sample,
`C9920BE3B258F55D9101F23B4EC610666D1D2D5E606D6E5B9A8E1F911D71D6EB`
for the MIL tests, and
`EF4FDFB21F49F9BAD8A078E03D219C60BF40A843A8CDFB529CD724AF18BD44FD`
for the internal tests. LibreWPF advances its ProGPU submodule to the resulting
documentation checkpoint.

ProGPU implementation checkpoint `73319afa` generalizes the phased route to
overlapping translated-equivalent leaves inside mixed postfix boolean programs.
Each leaf preserves all 64 supersamples in two packed words per pixel; one
shared GPU combine evaluator executes Difference, Intersect, Union, XOR, and
ReverseDifference in original program order before a single R8 average. Pure
left-fold XOR continues to phase. Safe non-overlapping mixed programs retain
the ordinary single-dispatch fast path. Work is batched by leaf ordinal, with
no CPU mask construction, readback, repacking, managed fallback, public ABI
change, or per-item submission.

Apple M3 Pro Metal passed all 10 configured CTests and the complete native
gate. The permanent sample requires exact mixed cyan/black/cyan pixels and
fractional XOR edge pixels `28/108/126`, overlap `5/6/10`, and
`28/108/126`. The immutable full and delta archives have SHA-256
`791BDEC1D4D18124A1AB6A55B866A6F4B4F502EEE1BF5B89E41F7CCEA7043E80`
and `7188E717842C04D4BC28708B65709FB17548FF5AE2DA3AEDAD56D42CDFC851BC`.

The exact full archive then completed Windows 11 ARM64 strict MSVC/Ninja and
live Parallels D3D12 qualification: 11/11 native/Dawn CTests, both zero-warning
managed builds, native/managed samples, automatic/raster/intrinsic-SIMD/scalar
policies, typed pre-resource forced-compute rejection, native stress, the
complete bounded differential matrix, and package staging. Overlay,
ColorDodge, group box blur, and the mixed boolean pixels were exact. Staged
SHA-256 values are
`11DBB21369E7BFB375650AEFFB2A0DD2F21626ED2250FC01F3F583F0D7688009`
for `progpu_native.dll` and
`FC6E3796EB62F435606AC8821D947262FACF7E751FDAC471D55FD2EE6AB2AC64`
for `progpu_native_dawn.dll`. The preserved 130,829-byte terminal evidence has
SHA-256
`F01C054984C9A24B241A203DAAF3390219E165769D524FEF36F3650476A071C5`.
This is correctness evidence on the virtual adapter, not a physical-D3D12
performance claim. LibreWPF advances its submodule to ProGPU documentation and
Win2D-planning checkpoint `fa3e9e4a`.

## Exact WPF Nonzero boolean winding

ProGPU checkpoint `a35e1d67` completes exact WPF Nonzero aggregation when
ordinary GeometryGroup contours contain or surround CombinedGeometry results.
The public 48-byte boolean-node layout is unchanged; appended winding-leaf,
winding-add, and winding-negate values preserve raw contour orientation until
the final Nonzero test. Boolean results normalize to `+1`, and only a reflected
containing GeometryGroup negates that contribution, matching the standalone
Windows WPF contour oracle.

The native path and retained-clip routes now default to the bounded inline
vector evaluator, matching the portable managed PathAtlas and avoiding
per-leaf intermediate storage. A typed forced compatibility option retains the
staged GPU shaders: analytic leaf rasterization records raw signed winding, the
postfix evaluator carries eight horizontal samples through two `vec4<i32>`
lanes per supersample row, and the coverage stage counts two predicate-mask
words before packing R8 atlas texels. Relative to the scalar-per-supersample
staging prototype, this reduces evaluator launches to one sixty-fourth and
result storage from 64 to two u32 words per texel. Neither route performs CPU
readback, CPU repacking, per-item submission, or managed fallback. Typed
fastest, forced-inline, and forced-staged modes report their resolved path and
invalid forced values fail closed.

The D3D12 reduction also separated WebGPU row pitch from copy placement: rows
remain 256-byte aligned, while every path, retained-clip, and glyph atlas copy
source is now 512-byte aligned. A source offset of 72,960 was valid for row
pitch but lost the Parallels D3D12 device; the next 512-byte boundary at 73,216
passed without a backend-specific path.

Apple M3 Pro Metal passed both 10/10 native CTest configurations, managed
shader coverage passed 20/20, and selected managed PathAtlas GPU cases passed
5/5. The exact committed archive then completed all 315 strict Windows ARM64
MSVC `/W4 /WX` build steps and 11/11 native/Dawn CTests. The live Parallels
WDDM D3D12 sample reproduced Metal at all six decisive probes: cancellation
and EvenOdd are `5,6,10`; positive islands and Nonzero are `51,209,242`. The
qualified source commit is `51d63ed2`, source archive SHA-256 is
`0a09a31491e115bf4794c0567e43e98013ecae91ed1d127a9717dd9365f9e9c2`,
and evidence-bundle SHA-256 is
`2459e7141471ab2101b885fe51b95a6b041e82e60ac1aaf093ddd75ee0b78aef`.

ProGPU checkpoint `cf0792aa` adds a matched high-precision rerasterization
benchmark and makes inline the qualified fastest/default path. Four alternating
Apple M3 Pro/Metal Release runs per mode measured median-of-run native p50/p95
at `3.1407/3.3726 ms` inline versus `7.7894/9.3647 ms` staged. Coverage staging
fell from `119,844,576` to `165,888` bytes (722.44-fold); both modes allocated
zero managed bytes per frame and matched all 518,400 pixels exactly with hash
`4026F1AF5062CEA5`. Time Profiler and Metal System Trace captures accompany the
ignored benchmark evidence.

The exact `cf0792aa` archive (SHA-256
`4606478e5e70db32d312186171d7816a60842335e862272ebfb143c971636e01`)
then completed all 315 strict Windows ARM64 MSVC `/W4 /WX` build steps and
11/11 native/Dawn CTests. The Parallels WDDM D3D12 sample retained all six exact
winding probes, and forced inline plus forced staged each matched the complete
managed frame with the same hash, reported the requested path, and completed
without device loss. The short VM p50 samples (`10.699 ms` inline,
`35.3319 ms` staged) are correctness evidence only, not a physical-D3D12
performance claim.

ProGPU checkpoint `2391367d` closes the browser-WGSL side of the same SIMD
winding work. WGSL logical `&&` and `||` accept scalar booleans only, while the
desktop shader compilers had accepted their use with four-lane boolean vectors.
The shared inline and staged signed-winding shaders now express difference,
intersection, union, and reverse difference with component-wise `select`
operations. The Emscripten/Chromium native browser contract passes with no
WebGPU validation errors, and 21 focused managed shader/boolean tests remain
green; no scalar fallback or browser-specific algorithm was introduced.

## Direct2D and Win2D boundary

`ProGPU.DirectX` implements the portable Direct3D-style facade, while the C++
backend now also owns a separate Windows-only genuine Direct2D COM provider.
ProGPU checkpoint `fa3e9e4a` classifies `d2d1.dll`,
`dwrite.dll`, `windowscodecs.dll`, and `Microsoft.Graphics.Canvas.dll` as
Windows-native graphics dependencies so the native resolver cannot impersonate
them. The same checkpoint pins exact Win2D and Win2D-Samples commits and adds a
source-verification gate for the native Direct2D/Direct3D contract plus
SimpleSample and ExampleGallery oracle sources.

The implementation has two tiers. ProGPU checkpoints `862077fe`, `4c716f4f`,
`7f7d6971`, `7c9fe63b`, `4133db10`, `42b43d6d`, `db43e5eb`,
`d9431558`, `ee84a0b3`, `2e8683ee`, and `f86481b5`
ship the first `ProGPU.Win2D` portable Canvas tier: `CanvasDevice`,
`CanvasBitmap`, `CanvasRenderTarget`, `CanvasCommandList`, `CanvasGeometry`,
`CanvasPathBuilder`, `CanvasActiveLayer`, and `CanvasDrawingSession` record a
source-compatible Win2D subset into immutable `GpuPicture` values, compile them
through `ProGPU.Scene.Native`, and submit them to the retained C++ renderer.
BGRA8-premultiplied targets, Win2D DPI rounding, transforms, solid primitive
draw/fill operations, default text, same-device bitmap drawing with source and
destination rectangles, opacity and qualified sampling, target-preserving
later sessions, retained command lists, explicit readback, and execution
diagnostics are implemented. Command lists retain immutable `GpuPicture`
chunks across `Flush()`, nest directly into the destination native scene, and
do not allocate an intermediate bitmap. Typed ownership clones and texture
leases preserve command-list and bitmap sources through deferred native
submission without staging copies. The ordinary render path does not use the
managed compositor, CPU readback, or CPU repacking.

The geometry slice adds rectangle, rounded-rectangle, ellipse, circle,
polygon, line, quadratic, cubic, and both Win2D arc-builder forms. Color
`DrawGeometry`/`FillGeometry` operations retain the shared ProGPU
`PathGeometry` directly. Scoped opacity layers use exact rectangle scissors or
native vector geometry clips; they enforce LIFO disposal and fail closed if a
scope would cross `Flush()` or drawing-session close. `CreatePath` consumes its
builder as Win2D does, arc rotation is converted from public radians to the
native vector degree contract, and no Direct2D COM emulation is introduced.

The locked Microsoft SimpleSample drawing body compiles against that package.
Live Apple M3 Pro Metal and Windows 11 ARM64 Parallels WDDM D3D12 runs produced
byte-identical frames with SHA-256
`92D04C71F9DF04983106F3BE3CBDEC1179CB2ACDB5B28A5A38667D2BF013B001`.
The frame qualifies full-opacity and half-opacity bitmap draws and disposes the
public source before destination-session close, proving deferred GPU lease
ownership.

The same frame records a command list in two chunks separated by `Flush()`,
draws it with an offset, and disposes the public command-list resource before
destination submission. The exact `7c9fe63b` source also passes the four
isolated source/DPI/fail-closed/signature contracts on macOS and Windows ARM64.
VM timings are correctness evidence only.

Exact ProGPU `4133db10` then passed five isolated source/signature contracts
on macOS and Windows ARM64 and rendered one retained quadratic/cubic path plus
circle and rectangle clips as `10+2` native draws. Metal produced SHA-256
`BE7227D7224576EC3C74963CD18CA9736FAC67657350CC739170E496AE28991A`;
Parallels WDDM D3D12 produced
`6FEC0F3EF3F628E18395542383E487C5D8CDA6FE0B49906299A6CDB9D19BE502`.
Only two antialiased curve-edge pixels differ, each by one channel level; all
solid interiors and both clip probes are exact. Checkpoint `42b43d6d` turns
that observation into a D3D12/Metal/Vulkan CI artifact gate with a named,
bounded differential contract instead of treating backend edge rounding as an
unexplained hash exception.

The same checkpoint ran on Ubuntu 24.04 ARM64 llvmpipe/Vulkan with SHA-256
`4443D80D541A386DEEEB6B35933550FE6FA437FDCC2ABA54BE8CA66E16877DF9`.
Against D3D12, Vulkan changes 42 antialiased-edge pixels and Metal changes two;
every changed channel is exactly 1/255, the Vulkan mean absolute channel
difference is `0.0003602431`, and the three-backend differential passes.

ProGPU `db43e5eb` removes the next ArcOptions source blocker with a mutable,
typed `CanvasStrokeStyle`. Start/end/dash caps, miter/bevel/round joins, miter
limit, standard and custom dash patterns, dash offset, fixed strokes, and
hairlines map to immutable ProGPU `Pen` realizations. Each style caches its last
brush/width/version realization and invalidates it on mutation; recorded
pictures retain the earlier immutable pen. Custom dash data is defensively
copied and takes precedence over the standard dash enum. `MiterOrBevel` fails
closed rather than silently becoming a different join.

The pinned ArcOptions-shaped draw overload now compiles, seven isolated tests
pass on macOS and Windows ARM64, and a dashed/capped line advances the live
frame to `11+2` native draws. Metal SHA-256 is
`0D9BB2695BF85767A0AFF3683392172D9A02EE1C17D5362C38EB060E848C69BB`,
Parallels D3D12 is
`CA50647DD915E8D42B4F5DD724BC96DE74383689157824186C52BF12D6B1577E`,
and Ubuntu Vulkan is
`AABC336A0F851925C70566E1CFFEC64BE943E29B41127CE7233386C930782FF2`.
The new stroke is exact across the three backends: the full-frame comparison
retains only the earlier 2-pixel Metal and 42-pixel Vulkan curve-edge
differences, all exactly 1/255.

ProGPU `d9431558` adds the pinned GeometryOperations creation subset:
`CreateGroup`, affine `Transform`, and `CombineWith` union, intersection, xor,
and exclusion. Combined geometry remains an immutable retained DAG evaluated
by the existing native vector-mask shaders. Identity-transformed operands are
referenced directly, and no synchronous path solver, GPU readback, CPU curve
flattening, or scalar fallback is used. Groups accept ordinary path geometries
and fail closed for combined entries until their `GeometryGroup` fill semantics
can be preserved without flattening.

The live exclusion test adds a rectangle-minus-circle fill (`12+2` native
draws) whose solid ring and transparent center are exact on all backends.
Metal SHA-256 is
`32F9926D292FB2A109268B42D5CC01B17EE7449EE69CEBC2CD7F2E14B24A063A`,
Parallels D3D12 is
`A48B37AE5DE4E77CE0FE8F69C0C7D4E9FCC93179CAA852B247D6C41B7072D9DD`,
and Ubuntu Vulkan is
`3191C015FF87F1FC4899DEFDFCBC5B518754B2908350E4BE47B81796C7D3C7E5`.
The three-backend differential still contains only the original two Metal and
42 Vulkan curve-edge pixels at exactly 1/255, proving the boolean fill itself
is backend-exact.

ProGPU `ee84a0b3` adds source-compatible `ICanvasBrush`, solid-color,
linear-gradient, and radial-gradient resources, including color and HDR stop
DTOs. Canvas primitive, geometry, text, and styled-stroke overloads consume the
typed brush directly. Mutable opacity, affine brush transforms, gradient
coordinates/radii/origin offset, and clamp/wrap/mirror spread are snapshotted
into immutable cached ProGPU brush realizations. Pen caching keys on that
realized brush identity and width, so the steady path allocates neither a new
brush nor pen, while mutation or public brush disposal cannot rewrite an
earlier retained picture. Device mismatches and missing typed brush contracts
fail closed. The qualified interpolation subset is premultiplied sRGB with
8-bit normalized precision; unsupported alpha, color-space, and precision
semantics are rejected rather than approximated.

The lock now hashes the official Win2D brush ABI and default-gradient source,
and the isolated source/signature/validation suite passes 9/9. The live native
frame adds one linear and one radial gradient and disposes both public brush
objects before destination-session submission, directly exercising native
brush-table and gradient-stop retention rather than a CPU pixel fallback.
It renders `13+2` native draws with SHA-256
`25829098701BE31CADAD8A3306D0AE4E66D50088891CD446A2B35A568108A295`
on Apple M3 Pro Metal,
`2B516B3243BEF0C59BD0428035B748E07E737679809B505F9FCF57AE3F74F005`
on the Parallels WDDM D3D12 adapter, and
`FAB68DBDD8997E364EBDA6833F8F825825945DE7230F110CABC4F653C0D91E46`
on Ubuntu llvmpipe/Vulkan. D3D12 versus Metal retains only the original two
1/255 curve-edge ties. Vulkan changes 84 pixels by 1/255 with mean absolute
channel difference `0.0005946181`; all exact gradient, boolean-fill, clip, and
solid probes pass the named three-backend gate. VM timing remains correctness
evidence only.

ProGPU `2e8683ee` adds source-compatible `CanvasImageBrush` over the same
retained texture lease used by `DrawImage`. Same-device `CanvasBitmap` and
`CanvasRenderTarget` sources support an optional DIP source rectangle,
independent clamp/wrap/mirror axes, opacity, and qualified nearest, linear,
multisample-linear, or cubic sampling. Positive axis-preserving
scale/translation lowers to one native external-image draw with extended
source coordinates, flat shader address metadata, and a hardware sampler. No
CPU tiling, pixel readback/repacking, or per-tile submissions are introduced.
Public source and brush disposal before drawing-session commit is safe because
the recorded picture owns the typed texture lease. Command-list/effect image
sources, rotation/skew/reflection, anisotropic sampling, and high-quality cubic
fail closed until their retained semantics are implemented.

The three-backend gate caught the first D3D12 run clamping extended UVs while
Metal repeated them. ProGPU `f86481b5` keeps the compatibility fallback on the
GPU: the texture shader normalizes repeat/mirror coordinates and addresses
cubic integer taps from the same flat mode while the sampler retains filtering
and tap behavior. The corrected `14+2` frame hashes are Metal
`09BA76F11AD8477D3D4852CE09B816FA84176DA8461DB5C974C2A8C6B6AC47F8`,
Parallels WDDM D3D12
`0D1EC07A46B5CCB9495C3BB30FFE20D78CE3AD7DD5CABE03BBE7B52DA7D088A9`,
and Ubuntu llvmpipe/Vulkan
`60BD4E94ED3BBBD99A34F6577CD1FA6EF7693263E040B304D4166F0227520C64`.
The entire image-brush region is exact. The full D3D12/Metal comparison keeps
only two earlier pixels at 1/255; Vulkan keeps 84 earlier pixels at 1/255 with
mean absolute channel difference `0.0005946181`. Windows passes 11/11 native
CTest executables and the isolated Win2D suite passes 10/10; Linux passes
10/10 native CTest executables. VM timing is correctness evidence only.

ProGPU `2196beaa` completes the next portable Canvas image group for bitmap
and command-list bounds plus cropped/destination-scaled command-list drawing.
`ICanvasImage.GetBounds(...)` is now typed on both bitmap and retained command
list resources. The shared `GpuPictureBounds` reader traverses immutable
commands without materializing the compatibility command array, composes
nested affine transforms, reuses clip/stroke/path bounds from the hit-test
lowering, and fails closed for malformed state, cycles, GPU/3D transforms, or
unknown commands. A cropped scale records one destination clip and affine
nested-picture transform; it does not allocate an intermediate bitmap, read or
upload pixels, or split primitives into separate submissions.

The exact `16+2` validation frame is Metal
`AFF6CBF059B5F2CDBF24243B1DA94E41F227A4E348FD0B76F07E9D1F239C5497`,
Parallels WDDM D3D12
`82592978570D34A2E5D110B95D963E051F01026184C23E0DF4703D7B6DEDA2B5`,
and Ubuntu llvmpipe/Vulkan
`59E132D93DDE652E0FE569162B248178F7EEA83806BA2CE0F3A7A81600B89617`.
The new region is exact across all three backends. The complete Metal/D3D12
frame changes only two pixels by 1/255; D3D12/Vulkan retains the previous 84
one-level antialiasing ties with mean absolute channel difference
`0.0005946181`. The Win2D contract suite passes 10/10 and retained-picture
bounds pass 4/4 on macOS, Windows ARM64, and Linux ARM64. Exact Windows and
Linux native builds pass 10/10 CTest suites. The Windows source archive and
rebuilt ARM64 DLL SHA-256 values are respectively
`7FCD5A09E672C61102066C60FEB0F9EDBEEE279521AF0251015F17AE3C5942EF` and
`39C0FD9F5B13CF277581C64096668CAF3673742719B55D6C6252AC9EB009262D`.

ProGPU `3390388e` adds the first portable Win2D bitmap-pixel creation/update
lane: the three `CanvasBitmap.CreateFromBytes(...)` overloads plus full and
subrectangle `SetPixelBytes(...)` for BGRA8-unorm premultiplied data. Uploads
flow from the caller array directly through the typed `GpuTexture` queue-write
APIs; no WIC/reflection shim, GPU readback, staging repack, or whole-buffer
scalar conversion is introduced. Oversized arrays retain Win2D's accepted
"at least required bytes" contract while only the required prefix reaches the
backend. Mutation fails closed once a retained draw owns a texture lease, and
render targets additionally reject mutation during an active drawing session,
preserving Win2D's immediate-draw observation despite ProGPU's deferred scene
submission.

The live gate creates the image-brush checker with `CreateFromBytes`, performs
one full upload and one 1x1 update, checks all four resulting sample cells, and
verifies that post-record mutation throws. macOS Metal, Parallels WDDM D3D12,
and Ubuntu llvmpipe/Vulkan retain the exact `16+2` frame hashes above. The
complete Metal/D3D12 differential remains two pixels at 1/255, and
D3D12/Vulkan remains 84 pixels at 1/255. The Win2D contract suite passes 10/10
on all three systems and all three benchmark builds are warning-free. The exact
Windows source archive SHA-256 is
`24FD8FC118952E4C51B857C01D476E06873472F43DEEBE46490C443510A98248`.
This managed-only change reuses the unchanged exact ARM64 native DLL SHA-256
`39C0FD9F5B13CF277581C64096668CAF3673742719B55D6C6252AC9EB009262D`;
the staged WinUI `generic.xaml` SHA-256 is
`4C4085838721C0AFCB1A9EE17591C0655CDDDADB26D330788E08BCD7F1AF8285`.

Windows may additionally run the real Win2D/Direct2D runtime into a
same-adapter shared BGRA8 DXGI allocation and import it through ProGPU's
existing Dawn keyed-mutex/shared-texture path without CPU readback; that native
interop adapter is implemented in `ProGPU.Direct2D`. The Microsoft Win2D binary remains
Windows-only, native COM resource wrapping fails closed off Windows, and
ProGPU will not publish a fake `d2d1.dll`. The portable package is source
compatible rather than binary compatible; bitmap file/buffer APIs,
geometry query/stroke/outline operations, command-list/effect image brushes, layer
opacity brushes, formatted text, effects, sprite batches, and XAML controls
remain incremental compatibility groups.
The support matrix, API mapping, delivery stages, and Windows/Metal/Vulkan
oracle gate are documented in
[`DIRECT2D_WIN2D_COMPATIBILITY.md`](../external/ProGPU/docs/DIRECT2D_WIN2D_COMPATIBILITY.md).

## Live MediaPlayer video checkpoint

ProGPU `f5f7988b` and the matching LibreWPF producer implement the last missing
canonical draw family from the current nested-render-data compiler audit:
`DrawVideo` and `DrawVideoAnimate`. Source-built `MediaPlayer` now publishes a
neutral `IPortableMediaPlayerSource` frame through
`PortableMediaPlayerSourceFactory.Attach(...)`; the attachment calls the
`Freezable` base write guard and deliberately does not create the Windows MIL
media state. Providers may also publish `IPortableInvalidationSource`, which is
delegated by `MediaPlayer` so retained replay sees new frames without private
field/event discovery.

Both LibreWPF rendering lanes consume the same contract:

- The current portable renderer resolves the live frame, applies a typed
  `IPortableRectAnimationValueSource` destination when present, retains the
  `IProGpuTextureLeaseSource` directly in the ProGPU drawing context, and emits
  `DrawTexture`. It does not adapt the frame into `ImageSource` or copy pixels.
- The native MIL compiler creates resource type 1, preserves the exact 48-byte
  video records, publishes width/height through ProGPU's pointer-free sideband,
  and records the lease source beside the compiled frame. The host acquires a
  context-qualified lease, binds deterministic external-image resource IDs
  `1..N` for the semantic-scene generation, installs the scene, and releases
  the prior frame leases only after the new table succeeds.

Forced or accidental CPU video fallback is not part of this path. An untyped
player, invalid dimensions, unavailable/cross-device texture, malformed
packet, or incompatible animation fails closed or is reported as a skipped
not-ready frame. The initial lane supports a single packed straight-alpha
RGBA/BGRA 8-bit texture; D3DImage synchronization, shared Direct2D surfaces,
NV12/P010 planes, HDR/color-space metadata, and protected content remain
explicit follow-up contracts.

The Apple Silicon checkpoint passes the ProGPU native MIL CTest, exact managed
packet test, 15 focused LibreWPF compiler/session/portable-video tests, and two
source-built `MediaPlayer` tests running on macOS without `wpfgfx`. Ubuntu
24.04 ARM64 builds exact ProGPU archive `bb2313ab` with GCC 13.3.0, passes the
native MIL CTest 1/1, exposes the new media-player external-image C export, and
produces `libprogpu_native.so` SHA-256
`17a2e5fd74de64a3697b98b41245a747c75850292573407346cda8671e7dba3a`.
Windows 11 ARM64 builds the same exact archive with MSVC 19.44.35228.0, passes
the native MIL CTest 1/1 in 3.61 seconds, exposes the same C export, and
produces `progpu_native.dll` SHA-256
`0eeb5e34086b753ac6abd93192c3def9aaec9559fb71cca053e33c7fdfbe258d`.

## Native MIL portable external ImageSource checkpoint

ProGPU `cfebce57` and LibreWPF `e4ac9d762` add a zero-copy native-MIL lane for
general typed image providers. When an `IPortableNativeImageSource` returns an
`IProGpuTextureLeaseSource`, the compiler creates canonical resource type 95,
records validated dimensions and the lease source, and calls ProGPU's new
bitmap external-image sideband instead of requesting portable CPU pixels. This
covers a `ProGPU.Win2D.CanvasBitmap` exposed through
`PortableNativeImageSourceFactory.Create(...)`, shared ProGPU textures, and the
consumer half of the synchronized D3DImage/Direct2D provider.

External bitmap and MediaPlayer lists remain individually handle-ordered. The
host merges them with two indices and no temporary combined collection,
acquires context-qualified leases, and binds resource IDs `1..N` in the same
global handle order used by the native C++ scene builder. Lease-table
replacement is transactional and the old leases are released only after the
new table succeeds. Cross-device, unavailable, untyped, invalid-size, and
non-lease native images fail closed; no CPU readback, pixel conversion, repack,
or upload is introduced.

The ProGPU native MIL CTest passes on Apple Silicon, the native managed backend
builds with zero warnings, the LibreWPF product/test assemblies build, and the
full compiler/session classes pass 114/114. Source-built PresentationCore,
including `PortableNativeImageSourceFactory`, builds with zero errors. Ubuntu
24.04 ARM64 rebuilds the exact tracked ProGPU delta through `4ece2969`, passes
the native MIL CTest 1/1 in 0.03 seconds, exposes the new export, and produces
`libprogpu_native.so` SHA-256
`c7633cc318977e69373c5d26d0bceed24de86d52bfe8b6506fe731ad14b24f54`.
Windows 11 ARM64 with MSVC 19.44.35228.0 passes the same CTest 1/1 in
2.50 seconds, exposes the export, and produces `progpu_native.dll` SHA-256
`fc627fff1240a9f06ae4e785101f9052b9dac8dbe600ae1a331d094087d79fdf`.

Native Microsoft Win2D binary and `ID2D1*` support remain Windows-only. ProGPU
`59045316` implements the first real producer in the separate
`progpu_native_direct2d` library: it creates genuine system
`ID2D1Factory1/2`, `ID2D1Device/1`, `ID2D1DeviceContext/1`, and
`ID2D1Bitmap/1` objects over a BGRA8-premultiplied D3D11 target with an NT
shared handle and keyed mutex. Its versioned descriptor includes adapter LUID,
DPI, dimensions, format, alpha mode, synchronization keys, software-adapter
state, and monotonic content version. COM pointers remain confined to its
Windows process-local header and never enter the portable MIL ABI.

The archived ABI v1 Windows 11 ARM64 MSVC `/W4 /WX` gate queries every advertised COM
interface, verifies multithread protection and bitmap target state, executes a
real Direct2D clear and rectangle fill, reopens the NT handle through
`ID3D11Device1`, and completes keyed-mutex handoff `0 -> 1 -> 2 -> 3`. CTest
passes 1/1 in 7.74 seconds and all eight exports are present. SHA-256 is
`f115ea21f43c218444a2d9fd9ebb622e073a5b3cafb52ec1745990e7984e498c`
for `progpu_native_direct2d.dll` and
`cab7f76311cd5115a0f8f84ee680115eb6481c6842eb45a85eea0633c08292fc`
for its test executable.

ABI v9 and the `ProGPU.Direct2D` package now bind the producer lifecycle to
Dawn's same-adapter import and the already-qualified D3DImage texture lease.
`ProGpuDirect2DSurface` validates the D3D12 context, adapter, BGRA8 premultiplied
format, NT handle, keyed mutex, dimensions, and DPI before importing the
allocation. `BeginDrawing()` transfers ownership from Dawn to a genuine
`ID2D1DeviceContext1`; session disposal performs native `EndDraw`, returns
ownership to Dawn, advances the content version, and invalidates the typed
texture source. Active deferred compositor leases fail closed. Both sides use
the Dawn-qualified zero mutex key; initialization is represented separately by
content version zero/nonzero. Ownership transitions occur outside the provider
state lock, avoiding a render-lock/provider-lock inversion.

The managed surface is reflection-free and AOT-safe through source-generated
`LibraryImport` and caller-owned `SafeHandle` COM references. It implements
`IProGpuContextTextureLeaseSource`; the reflection-free
`ProGpuDirect2DD3DImageSource` adapter publishes it through the neutral
`IPortableD3DImageSource` and `IPortableInvalidationSource` contracts consumed
by source-built `PortableD3DImageSourceFactory.Attach(...)`. The adapter fails
closed before the first successful draw and forwards `TextureChanged`; the
application retains ownership of the wrapped surface. This adds no bridge
copy, new MIL resource kind, COM pointer in a packet, readback, or repack. The
Windows build now stages `progpu_native_direct2d.dll` for x64/ARM64 and checks
the exact 27-export ABI. Its generic GUID-based `QueryInterface` seam lets
typed native/AOT callers request later genuine `ID2D1*` generations supported
by the installed Windows runtime, with explicit `E_NOINTERFACE` failure and no
emulated vtables.

ABI v5 adds the official Win2D `ICanvasFactoryNative::GetOrCreate` resource
wrappers. The registered factory wraps the provider's exact `ID2D1Device1` as
a real `Microsoft.Graphics.Canvas.CanvasDevice`, then wraps the exact target
`ID2D1Bitmap1` as a real `CanvasRenderTarget` at the surface DPI. The managed
`TryBeginMicrosoftWin2DProducerAccess(...)` scope transfers the keyed mutex
from Dawn without beginning a competing native Direct2D draw session. The
caller creates, uses, and disposes its real `CanvasDrawingSession` inside that
scope; disposing the outer scope returns ownership to Dawn, refreshes the
descriptor/content version, and publishes texture invalidation. Missing Win2D
package registration, missing apartment initialization, a cross-device
resource, an overlapping producer, or an active GPU lease fails closed through
typed HRESULT/state results. The provider does not search for or load the
Win2D DLL.

ABI v6 adds the reverse official
`ICanvasResourceWrapperNative::GetNativeResource` seam. Public typed methods
return caller-owned exact `ID2D1Device1` and `ID2D1Bitmap1` references from the
real CanvasDevice and CanvasRenderTarget while native code supplies the cached
CanvasDevice and target DPI. Canonical `IUnknown` identity comparison proves
both round trips return the original ProGPU provider resources without a
second device domain or copy.

ABI v7 adds the first non-surface device resource: a genuine
device-context-domain `ID2D1SolidColorBrush`. The reusable native C ABI wraps
it through Win2D's official `ICanvasFactoryNative::GetOrCreate`, reverse-
unwraps it through `ICanvasResourceWrapperNative::GetNativeResource`, and
preserves canonical COM identity. Public managed methods keep the generic raw
pointer seam internal, require the exact brush handle kinds, and use
`DangerousAddRef` around every borrowed `SafeHandle` pointer. This provides the
resource-family shape for gradients, images, geometry, text, command lists,
and effects without placing COM pointers in the portable MIL protocol.

ABI v8 implements that shape for gradients. A pinned blittable managed stop
span creates a genuine `ID2D1GradientStopCollection1` with explicit pre/post
color spaces, buffer precision, extend mode, and interpolation mode without an
intermediate array. Kind-checked collection handles then create genuine linear
and radial brushes with typed geometry, opacity, and affine transform state.
Both brush families use the generic native Win2D factory/resource-wrapper seam
but expose only kind-specific managed methods; each borrowed safe handle is
protected by `DangerousAddRef`.

ABI v9 adds genuine `ID2D1RectangleGeometry`,
`ID2D1RoundedRectangleGeometry`, `ID2D1EllipseGeometry`,
`ID2D1PathGeometry1`, and `ID2D1TransformedGeometry` creation plus union,
intersection, XOR, and exclusion through Direct2D's boolean geometry sink.
The batched blittable path ABI preserves filled/closed figures and line,
quadratic, cubic, and arc segments without per-segment P/Invoke or CPU
tessellation. The managed owner accepts both low-level spans and the same
neutral `PortablePrimitiveGeometry`/`PortableGeometryPath` DTOs already used
by LibreWPF retained replay. Kind-checked `CanvasGeometry` projection and
reverse unwrapping must preserve canonical `ID2D1Geometry` identity.

Exact ProGPU implementation commit `f751cd0b` was rebuilt in the Windows 11
ARM64 Parallels VM with MSVC 19.44 and Windows SDK 26100 under `/W4 /WX`. The
native regression exits zero and the exact 14-export `dumpbin` audit passes.
SHA-256 is
`d9224ee806635ba3086d299912bb7bd2d9cf52a7ef56451ae54656058e7175d8`
for `progpu_native_direct2d.dll` and
`0e8fc690ba5bd4a7a40d461d1691f8efd32dbef7338ae90a1635ccc5b0f2e02d`
for its test executable. That isolated native regression had no registered
Canvas/Win2D AppX package, so it also qualifies the explicit unavailable-
runtime behavior for both wrappers.

The real package-deployed path is now qualified from exact ProGPU source
`d201494a` in that Windows 11 ARM64 Parallels VM. A full-trust MSIX containing
official Microsoft Win2D 1.4.0 projects the returned native pointer through
`CanvasRenderTarget.FromAbi`, creates a genuine `CanvasDrawingSession`, and
draws into the ProGPU Direct2D/Dawn shared target. Validation-only pixel access
reports an exact transparent corner and center ARGB `(255,32,96,192)` after a
48x48 fill on a 64x64 target. Content version advances `0 -> 1`, native wrapping
returns `S_OK`, the three runtime types are the official
`Microsoft.Graphics.Canvas` types, and the adapter reports `Dawn D3D12`.
The optional Windows lane is enabled with
`PROGPU_RUN_REAL_WIN2D_INTEGRATION=1`; it requires a pre-provisioned signing
certificate thumbprint and never mutates trust stores. Full device-loss/domain
recreation and broader Win2D resource wrapping remain open parity gates.

Exact ProGPU `4f5e614f` was rebuilt in the Windows 11 ARM64 Parallels VM with
MSVC 19.44, Windows SDK 26100, and `/W4 /WX`. The native regression exits zero
and `dumpbin` matches all 18 allowed exports. SHA-256 is
`6c35ac88938fbdc483b6a932d1180a1fd041ead3097c4ef51bce2b31ad5e301c`
for `progpu_native_direct2d.dll` and
`edb201be9ab6f1783d679bcafd8872c3f5c1495bcc9b8738c3235b5177f44d42`
for its test executable. The signed official Microsoft Win2D 1.4.0 oracle
projects the result as the real
`Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush`, proves exact native
solid-brush identity, reads brush ARGB `(255,224,48,96)`, and draws through the
brush overload to the exact same center pixel while the corner remains
transparent. Device/bitmap identity, content version `0 -> 1`, and
`Dawn D3D12` also pass in that run.

Exact ProGPU `8e62b5e5` was rebuilt in the Windows 11 ARM64 Parallels VM with
MSVC 19.44, Windows SDK 26100, and `/W4 /WX`. The native regression exits zero
and `dumpbin` matches all 21 allowed exports. SHA-256 is
`c291eac6efc959acd39ba1bdea03d80e8e9025b001c145c13b4c174f003ffc96`
for `progpu_native_direct2d.dll` and
`712ba33d7cd121bb8a7d3c68585c3895c00ad5575e4cdc64971783857d2020a3`
for its test executable. The signed official Win2D 1.4.0 oracle projects real
`CanvasLinearGradientBrush` and `CanvasRadialGradientBrush` objects, proves
exact reverse native identities, validates two-stop and geometry metadata, and
draws exact solid/linear/radial samples `(255,224,48,96)`,
`(255,32,160,224)`, and `(255,64,192,96)` while the corner remains
transparent. Content advances `0 -> 1` on `Dawn D3D12`.

ABI v9 at exact ProGPU `0b96328e` was rebuilt in the same Windows 11 ARM64
Parallels guest with MSVC 19.44, Windows SDK 26100, and `/W4 /WX`. The native
regression exits zero and the exact 27-export audit passes. SHA-256 is
`83a67ee9007902ca477bada185ea99d298f879b8798b91aad18d4bf996eda29e`
for the DLL and
`eb9cdf5346e8f72ae49b2486051298a7bbce44bd83bde36b554dee50d7b8f0fa`
for its test executable; the source archive is
`3a3726ee61792a98558a02e2cb6a050340fbadf757b0908c5f1b318514f55f5b`.
The signed official Win2D 1.4.0 oracle built from exact app `3a058643`
projects a real `Microsoft.Graphics.Canvas.Geometry.CanvasGeometry`, proves
exact reverse native identity, and fills a boolean-exclude geometry. Its ring
sample is exact ARGB `(255,240,208,32)` while the excluded hole preserves the
solid sample `(255,224,48,96)`. Existing brush samples, transparent corner,
all native identities, content version `0 -> 1`, and `Dawn D3D12` remain
green. The package gate persists JSON before uninstall and records a
best-effort last completed stage for native-termination diagnosis.

Exact ProGPU ABI-v6 commit `1be881ca` was rebuilt in the same guest with MSVC
19.44 and Windows SDK 26100 under `/W4 /WX`. The native regression exits zero
and the exact 15-export audit passes. SHA-256 is
`160037e11339ec6ad38a3cc2bc121ca6da5ba73ad3fd25c29d9eb8d030a132d9`
for `progpu_native_direct2d.dll` and
`46884523bd6ba4700c8113ac9df2f09689b134d429327a07d9fcd083511159ec`
for its test executable. The packaged Win2D evidence adds
`NativeDeviceIdentityMatches=true` and `NativeBitmapIdentityMatches=true` while
retaining the exact pixel, `0 -> 1` content-version, and `Dawn D3D12` results.

The provider now also creates the genuine WinRT `IDirect3DDevice` required by
Win2D `CanvasDevice.CreateFromDirect3D11Device` from the surface's exact
`IDXGIDevice`. Its native regression unwraps the object through
`IDirect3DDxgiInterfaceAccess` and requires the original `ID3D11Device`
identity, preventing a second adapter/resource domain or cross-device copy.
The factory-native wrapper is preferred over constructing a CanvasDevice only
from that WinRT object because it preserves the provider's exact Direct2D
resource domain.

ABI v16 at ProGPU implementation checkpoint `6a87f320` extends the same
Windows-only native COM producer with a genuine shared `IDWriteFactory3`,
caller-owned `IDWriteTextFormat1` resources, and typed
`ID2D1RenderTarget::DrawText` submission on both shared-surface and
command-list transactions. Family, locale, and text enter through explicit
UTF-16 spans. Only the cold format-creation boundary builds the NUL-terminated
DirectWrite family and locale strings; the hot draw path consumes the pinned
caller span directly without provider-side text copies, readback, repacking,
reflection, or per-glyph interop. Device-independent Win2D wrapping uses a
null CanvasDevice and zero DPI, and reverse unwrapping requests the exact
`IDWriteTextFormat1` IID so a later signed oracle can prove canonical identity
without creating another Direct2D resource domain. Invalid descriptors,
unknown flags, wrong resource kinds, and calls outside an active draw fail
closed.

Two consecutive `/Brepro` builds in the Windows 11 ARM64 Parallels guest with
MSVC 19.44, Windows SDK 10.0.26100.0, and `/W4 /WX` produce identical
artifacts. The focused native regression exits zero after creating and
querying the DirectWrite resources and executing a real Direct2D text draw;
`dumpbin` matches the exact 47-export allowlist. SHA-256 is
`6BC503DBE9BB5506B709CA6D97D8B78F82F302BF33BCE4352B104722DA05FCDC`
for `progpu_native_direct2d.dll` and
`8C634D6EC4963786D87D5E87BEE5FBD83F6B843A8BCE535E0E9149CB806FCDC5`
for its native test executable. This qualifies the native COM path; official
Win2D `CanvasTextFormat` projection remains a separate signed-package gate and
is not inferred from native identity alone.

ProGPU `e6647ffd` advances the provider to ABI v17 with retained genuine
`IDWriteTextLayout4` resources and typed
`ID2D1RenderTarget::DrawTextLayout` submission for both shared-surface and
command-list transactions. Layout creation consumes one explicit UTF-16 span,
an existing typed format, and positive finite bounds. DirectWrite copies the
text into its retained layout during that synchronous call; ProGPU neither
retains the caller span nor creates another provider text buffer. The Win2D
factory path checks for a text layout before its inherited text-format
interface, supplies the surface's exact CanvasDevice as required by the pinned
Microsoft `CanvasTextLayout` implementation, and reverse-unwraps the exact
`IDWriteTextLayout4` identity. Invalid dimensions/origins/options, wrong
resource kinds, and drawing outside an active transaction fail closed. The
reflection-free managed owner exposes reusable layout creation/drawing and the
native export allowlist grows from 47 to exactly 49.

ProGPU `15249f32` advances the provider to ABI v18 with one typed,
pointer-free mutable range descriptor over those retained layouts. Selected
font size, numeric weight,
style, stretch, underline, strikethrough, and an optional genuine
`ID2D1Brush` drawing effect flow through one synchronous call. Both managed
and native validation reject unknown flags, empty or overflowing ranges,
malformed selected values, and non-brush effects without reflection, text
copies, CPU rendering, or per-character interop. The native regression reads
the applied DirectWrite state and canonical brush identity back. The official
Win2D gate now observes that state through `CanvasTextLayout`, mutates the same
layout back through Win2D, and draws it; execution of that signed package gate
remains pending explicit approval for its temporary test-certificate trust.
The native export allowlist grows from 49 to exactly 50.

ProGPU `4d36f331` advances the provider to ABI v19 with genuine
device-independent `IDWriteTypography`. A bounded pinned span of typed
OpenType name-tag/parameter pairs crosses managed/native once; DirectWrite
copies it into the owned typography object, which is then assigned to a
nonempty retained-layout UTF-16 range. The Win2D resource seam applies the
correct null-device/zero-DPI rule for official `CanvasTypography`, validates
the projected feature metadata, and reverse-unwraps the exact native identity.
Empty or oversized feature sets, zero tags, invalid ranges, and wrong COM
kinds fail closed. The allowlist grows from 50 to exactly 52 exports without
reflection, text readback, or per-feature managed/native calls. Signed package
execution remains a separate approval-gated oracle.

ProGPU `6dfdd082` advances the provider to ABI v20 with the genuine shaped
glyph resource boundary required by WPF/MIL replay. It resolves a typed system
family/weight/style/stretch match as `IDWriteFontFaceReference`, creates the
corresponding `IDWriteFontFace5`, and submits already-shaped glyph indices,
optional advances, and optional offsets directly through
`ID2D1DeviceContext::DrawGlyphRun` in either shared-surface or command-list
transactions. The spans stay caller-owned and pinned only for the synchronous
call; the provider performs no text reshaping, CPU raster fallback, pixel
readback, intermediate glyph arrays, or per-glyph native calls. Counts,
floating state, bidi level, COM kinds, and draw ownership fail closed. The
official Win2D seam wraps the same device-independent reference as
`CanvasFontFace`, reverse-unwraps its exact COM identity, obtains real glyph
indices through the projection, and feeds them into the native draw oracle.
The allowlist grows from 52 to exactly 55 exports. This remains parallel to,
not a replacement for, the portable cross-platform glyph DTO path.
GitHub Actions Build run `33326634929`, MSVC job `99297867722`, compiles and
links the provider plus native regression under the warning-as-error lane. The
focused `progpu_native_direct2d_tests` passes in 0.16 seconds, the complete
native suite passes 11/11, and the exact 55-symbol allowlist is accepted.

ProGPU `ef2cc90b` advances the provider to ABI v21 with a GPU-native color-font
path over the same shaped spans. Fastest/default queries
`ID2D1DeviceContext7::DrawGlyphRunWithColorSupport`, keeping current COLR
paint-tree, SVG, embedded bitmap, layered-color, and monochrome selection
inside Direct2D. Down-level Windows 10 falls back on-GPU through
`IDWriteFactory4::TranslateColorGlyphRun` and `ID2D1DeviceContext4` bitmap,
SVG, or outline draws. Only `DWRITE_E_NOCOLOR` selects monochrome semantics;
missing required COM support and other translation failures fail closed. No
font payload is decoded in managed code or copied through CPU pixel buffers.
The session returns a typed context7/translated-context4/no-color diagnostic,
and the allowlist grows from 55 to exactly 56 exports.
GitHub Actions Build run `33327156224`, MSVC job `99299265980`, compiles and
links the provider plus regression under the warning-as-error lane. The
focused `progpu_native_direct2d_tests` passes in 0.14 seconds, all 11 native
suites pass, and the exact 56-symbol allowlist is accepted.

The next ProGPU ABI v22 checkpoint adds genuine same-device
`ID2D1SvgDocument` resources through `ID2D1DeviceContext5`. Caller-owned UTF-8
XML is exposed by a bounded borrowed `IStream` only for the synchronous
Direct2D parse, avoiding both a retained pointer and an intermediate managed
array. Surface and command-list draws temporarily apply Win2D-compatible
viewport/origin state and restore it before return. Foreign resource domains,
invalid viewports, oversized input, wrong COM kinds, and inactive draws fail
closed. The official Win2D seam wraps and reverse-unwraps the exact resource as
`CanvasSvgDocument`, while portable LibreWPF SVG continues to use ProGPU's
backend-neutral retained vector path. The allowlist grows from 56 to exactly
58 exports.
GitHub Actions Build run `33328289063`, MSVC job `99302278126`, compiles and
links the warning-as-error provider and native regression. The focused
`progpu_native_direct2d_tests` passes in 0.49 seconds, all 11 native suites
pass, and the exact 58-symbol allowlist is accepted.

ProGPU ABI v23 then adds typed device-loss and resource-generation ownership.
Each genuine Direct2D/DirectWrite/Win2D COM safe handle inherits its surface's
nonzero monotonic generation. The native surface registers an
`ID3D11Device4` removal event when supported, polls it without blocking,
confirms the reason through `ID3D11Device::GetDeviceRemovedReason`, and retains
`DXGI_ERROR_DEVICE_REMOVED`, `DXGI_ERROR_DEVICE_RESET`, or
`D2DERR_RECREATE_TARGET` as terminal state. Managed loss invalidates the shared
safe-handle token, reports into the same Dawn `WgpuContext`, and raises one
typed notification requiring a new Dawn/Direct2D domain and rebuilt resources.
Cross-generation use and direct `QueryInterface` on a lost generation fail
closed. The allowlist grows from 58 to 59 exports. Deterministic regression
coverage includes invalid-state structs, initial non-lost state, registered
removal notification, and unique replacement generations; real adapter-loss
injection/recreation remains a separate destructive Windows VM gate.
Exact implementation `d67fe1bf` is Windows-qualified by GitHub Actions Build
run `33329548704`, dedicated MSVC job `99305585595`. The provider and regression
compile/link under the warning-as-error lane, the focused Direct2D test passes
in 0.15 seconds, all 11 configured native suites pass, and the exact 59-export
allowlist is accepted. The broader ClangCL job `99305585623` independently
passes the Direct2D test in 0.14 seconds and all 12 native suites before an
unrelated later Dawn readback loses Microsoft Basic Render Driver; that
software-D3D12 stability failure remains separate from ABI v23 evidence.

ProGPU ABI v24 adds typed genuine-`ID2D1Geometry` analysis for LibreWPF bounds
and hit-test parity. The Windows provider now exposes bounds, widened bounds,
fill/stroke point containment, geometry relation, area, length, and
point/unit-tangent sampling through eight fixed AOT-safe exports. Managed
geometry and optional `ID2D1StrokeStyle1` safe handles must belong to the same
monotonic resource generation and remain protected by `DangerousAddRef` for
the borrowed call. Invalid points, widths, lengths, affine transforms, and
flattening tolerances fail closed; output rectangles convert Direct2D's edge
layout to ProGPU's `x/y/width/height` form. This removes the need for reflected
WPF shapes or CPU tessellation on the Windows native path and preserves the
portable ProGPU geometry implementation on other backends. The allowlist grows
from 59 to exactly 67 exports. Deterministic native coverage uses a rectangle
oracle for all eight operations and invalid-output clearing. Simplify,
outline, widen, tessellate, and realization sinks remain the next typed slice.
Exact ProGPU implementation `13f6906b` is qualified by GitHub Actions Build
run `33330942215`: dedicated MSVC job `99309300180` passes the focused
Direct2D regression in 0.25 seconds and all 11 native suites under
warning-as-error, while ClangCL x64 job `99309300268` passes it in 0.14 seconds
and all 12 native suites before the unrelated later Dawn software-adapter loss.

ProGPU ABI v25 keeps those realization sinks internal and typed. Simplify,
outline, and widen materialize same-factory `ID2D1PathGeometry1` resources;
tessellation writes blittable triangles directly into a caller span and
returns the required count plus a typed insufficient-buffer result without
per-triangle allocation or managed callbacks. Filled and stroked
`ID2D1GeometryRealization` resources are created and drawn through
`ID2D1DeviceContext1` from both target and command-list producer sessions.
All geometry, stroke-style, realization, and brush handles remain kind- and
generation-checked. Invalid options, transforms, tolerances, widths, buffers,
and producer state fail closed. The allowlist grows from 67 to exactly 74
exports. Final ProGPU checkpoint `9dc74d09` passes the managed aggregate in
Build run `33332388195`, Ubuntu job `99313260684`. Dedicated MSVC job
`99313260762` compiles/links the 74-export DLL under warning-as-error, passes
the Direct2D regression in 0.14 seconds, and passes all 11 native suites.
Corrected native-identical commit `84ece34c` passes ClangCL x64 job
`99312705172` in 0.15 seconds and all 12 native suites; the final commit changes
only managed operation-label scope covered by the aggregate job.

ProGPU ABI v26 at checkpoints `d9982c73`/`f1b1ca18` adds the typed immediate Direct2D
vector-drawing surface needed by Windows MIL and native Win2D integration.
Both shared-target and `ID2D1CommandList` sessions expose transparent or
colored clear, affine transform get/set, and line, rectangle,
rounded-rectangle, ellipse, and arbitrary geometry fill/stroke operations.
Brushes, optional stroke styles, geometries, and optional opacity brushes are
kind- and generation-checked, and each borrowed safe handle is retained across
the native call. The C++ boundary repeats finite scalar validation and genuine
COM `QueryInterface` checks; operations require an active producer and defer
device-context errors to the existing tagged `EndDraw` transaction. There is
no reflection, managed callback, CPU command-array materialization, readback,
or portable-scene COM pointer. The exact allowlist grows from 74 to 86 exports.
The native regression records every operation, round-trips transform state,
composites the command list into the keyed-mutex target, and checks an exact
BGRA staging pixel. Portable managed contract coverage passes 5/5 with zero
warnings. Corrected ProGPU checkpoint `f1b1ca18` is qualified by Build run
`33333671491`, dedicated MSVC job `99316705077`: warning-as-error native
compile/link succeeds, the focused Direct2D pixel regression passes in 0.16
seconds, all 11 native suites pass, and the exact 86-export gate is accepted.
Clip APIs remain a follow-up because exact clip/layer cross-ordering requires
one unified LIFO draw-state stack rather than independent depth counters.

ProGPU ABI v27 at checkpoint `10ef4c1a` implements that exact draw-state
ordering and adds the next typed Win2D image slice. Axis-aligned clip scopes
and layers share one fixed-capacity, allocation-free LIFO stack; cross-kind
pop attempts fail closed without consuming the active scope, and producer
cleanup unwinds mixed scopes in exact reverse order. Shared-target and command-
list sessions expose typed `DrawBitmap` and `DrawImage` operations with optional
source/destination rectangles, target offsets, full finite 4x4 bitmap
perspective transforms, and the complete Direct2D interpolation and composite
mode enums. Bitmap and image handles remain kind- and generation-checked and
protected by `DangerousAddRef` across native calls. The allowlist grows from 86
to exactly 90 exports. Deterministic native coverage records clipped vector,
bitmap, and image commands into an `ID2D1CommandList`, verifies mixed layer/clip
mismatch behavior, composites through the keyed-mutex target, and retains the
exact BGRA staging-pixel oracle. Portable managed contracts pass 5/5 with zero
warnings. Windows MSVC compile, exact-export, and native pixel qualification
are qualified by Build run `33334553038`, dedicated MSVC job `99319045125`:
warning-as-error compile/link succeeds, the focused Direct2D regression passes
in 0.16 seconds, all 11 native suites pass in 1.05 seconds, and the successful
Windows build accepts the exact 90-export allowlist. The recorded ProGPU
documentation checkpoint is `8d9ad714`.

ProGPU ABI v28 at checkpoint `ac10d4af` adds typed native Win2D drawing-state
parity on the same genuine device context. Shared-target and command-list
sessions round-trip geometry antialiasing, text antialiasing, primitive blend,
DIP/pixel unit mode, two 64-bit tags, and DPI. Both managed and C++ boundaries
reject unknown enums and invalid DPI; `(0, 0)` preserves Direct2D's exact
reset-to-96-DPI behavior. Every operation requires the active typed producer,
allocates no command objects or callbacks, and leaves MIL/WebGPU free of COM
identity. The native regression restores all state before the existing clipped
bitmap/image and exact-BGRA oracle. The exact allowlist grows from 90 to 102
exports; managed contracts pass 5/5 with zero package warnings. Windows MSVC
qualification passes in ProGPU Build run `33335230522`, dedicated MSVC job
`99320851539`: warning-as-error compile/link succeeds, the focused Direct2D
regression passes in 0.17 seconds, all 11 native suites pass in 1.07 seconds,
and the exact 102-export gate is accepted.

ProGPU ABI v29 at checkpoint `2086632e` adds mutable native Direct2D brush
parity: typed common opacity/affine transform, solid color, linear endpoints,
and radial center/origin/radii set/query operations. Handles remain generation-
and kind-checked and protected during the C++ call; finite values, opacity, and
radii fail closed on both boundaries. No reflection, CPU pixels, command arrays,
or portable COM identity are introduced. The native regression restores the
solid brush before the exact-BGRA oracle. The exact allowlist grows from 102 to
110 exports; managed contracts pass 5/5 with zero warnings. Windows MSVC
qualification passes in ProGPU Build run `33336026310`, dedicated MSVC job
`99322989531`: warning-as-error compile/link succeeds, the focused Direct2D
regression passes in 0.14 seconds, all 11 native suites pass in 1.02 seconds,
and the exact 110-export gate is accepted.

ProGPU ABI v30 at checkpoints `96735d95`/`058f6f1f` adds typed live bitmap/image-brush
state required by native Win2D projections. Sampling, tiling, source rectangle,
and nullable bitmap/image bindings can be changed and queried; returned
interfaces are caller-owned and preserve canonical COM identity. Managed
generation/kind validation and safe-handle borrowing pair with independent C++
`QueryInterface` checks. The path performs no reflection, CPU pixel transfer,
repack, or portable MIL/WebGPU COM transport. Tests cover detach/rebind and
restore state before existing rendering gates. The exact allowlist grows from
110 to 118 exports; contracts pass 5/5 with zero warnings. Corrected checkpoint
`058f6f1f` is qualified by ProGPU Build run `33336912843`, dedicated MSVC job
`99325361848`: warning-as-error compile/link succeeds, the focused Direct2D
regression passes in 0.18 seconds, all 11 native suites pass in 1.33 seconds,
and the exact 118-export gate is accepted.

ProGPU ABI v31 at checkpoint `2d24157d` adds typed `ID2D1Bitmap1` metadata,
bounded caller-span upload, and same-generation GPU bitmap-to-bitmap copy. The
descriptor exposes pixel/DIP dimensions, DPI, format, alpha mode, and bitmap
options without a readback. Managed and native validation proves resource
kind/generation, pitch and byte extent, both copy rectangles, and canonical COM
identity; invalid or unsupported operations fail closed. The input span is
pinned only for the synchronous native upload, with no array materialization or
repack. Native coverage draws the mutated bitmap into the shared target and
checks distinct exact BGRA pixels for the memory update and GPU copy. The exact
allowlist grows from 118 to 121 exports; contracts pass 5/5 with zero warnings.
Immutable archive SHA-256
`CBEF4F7F71DE3B61B43CE0A1C2C14941B0589C6440C92F0CD7553FA4DBAE82E3`
is qualified in the Windows 11 ARM64 Parallels VM using MSVC 19.44 and Windows
SDK 10.0.26100.0: strict compile/link, the exact 121-export comparison, and the
focused live Direct2D regression all pass. The provider DLL SHA-256 is
`07751974494C643CF899F60988AED1335EC10BF493E26142099528D4041B7C1C`.
ProGPU Build run `33337753262`, x64 native job `99327677774`, independently
passes the Direct2D regression in 0.17 seconds and all 12 native suites in 1.14
seconds. Its later managed WebGPU sample lost the Microsoft Basic Render Driver
device after those gates completed. ProGPU qualification documentation is at
checkpoint `13475078`.

ProGPU ABI v32 at implementation `3f5078af`, with Windows oracle correction
`8e812820`, adds the first genuine Direct2D command-list ingestion boundary for
the C++ backend. `ID2D1CommandList::Stream` feeds an allocation-free internal
`ID2D1CommandSink1` that validates mixed clip/layer scope order and publishes a
64-byte pointer-free summary of state, clear, draw, fill, text, image, clip,
layer, and unsupported operation counts. Callback COM resources remain
Windows-local and are never retained or transported through MIL/WebGPU. Audit
mode reports unsupported classes; strict mode fails closed with `E_NOTIMPL` for
non-null text rendering parameters, GDI metafiles, meshes, and opacity masks.
This checkpoint is operation-set preflight, not yet resource conversion or
native scene emission. Managed builds have zero warnings, contracts pass 5/5,
and the ABI allowlist is exactly 122 exports. Incremental Windows 11 ARM64 MSVC
19.44/SDK 10.0.26100.0 compiles the full sink vtable under `/W4 /WX` and passes
the supported/fail-closed regression 1/1; provider SHA-256 is
`E2A0F827107450E5C6D0ED8C2CA3C8C20656F6A32C1A6361DB788C14117CD1D3`.
ProGPU documentation checkpoint is `a048cff9`; ClangCL COM exception-spec
portability fix `7a5feda4` is tracked by the submodule. Clean-checkout Build
run `33340281658` is pending.

ProGPU ABI v33 at implementation `bb4818bf` and documentation checkpoint
`28b4610b` moves from structural preflight to real scene emission. A strict
`ID2D1CommandSink1` translates finite transforms, source-over/DIPs state,
solid-color brushes, rectangle fills/default strokes, default flat-cap lines,
edge-antialias selection, and one leading clear into ProGPU's existing C++
semantic scene builder. The clear remains frame metadata. Unsupported state,
resources, scopes, or operations return `E_NOTIMPL` with a typed reason and
one-based callback index; no partial stream is accepted. This is an explicit
first subset, not a claim of complete arbitrary `ID2D1*` command parity.

The AOT-safe two-pass ABI reports the exact required size and writes directly
to caller-owned storage. COM pointers never enter the stream, and there is no
managed staging array, reflection, CPU pixel readback/repack, or raster
fallback. Linking the Windows provider to the backend-neutral scene builder
keeps the output reusable by ProGPU D3D12, Metal, Vulkan, and WebGPU rather
than creating a Windows-only renderer. Managed build is warning-free,
contracts pass 5/5, and the allowlist is exactly 123 exports. Incremental
Windows 11 ARM64 Parallels qualification uses MSVC 19.44/SDK 10.0.26100.0
under `/W4 /WX`, passes the live regression 1/1 in 3.35 seconds, decodes the
three-draw scene header, verifies typed rejection of DirectWrite rendering
parameters, and reports exactly 123 exports. Provider SHA-256 is
`0C552556B68BDB2F34B9B4ADA552B1DBBC2EB25A247483ED27710787CBF787D2`.
Clean-checkout ProGPU MSVC compatibility job `99339089791` at checkpoint
`b91df2da` passes; its longer Windows renderer jobs were superseded by ABI v34.

ProGPU ABI v34 at implementation `c4dca894` and documentation checkpoint
`e938bce7` adds exact nested aliased Direct2D axis-aligned clip translation.
Each push captures the active transform, converts the rectangle to target
space, intersects it with its parent, and emits native scene state plus balanced
save/restore commands. This preserves Direct2D push-time transform semantics
and gives WPF scroll/viewport clips one backend-neutral representation across
D3D12, Metal, Vulkan, and WebGPU. Depth is bounded by the native scene maximum
of 64 with an explicit capacity result.

Per-primitive antialiased clips remain fail-closed `E_NOTIMPL`: the current
native rectangle clip is an exact scissor, not a coverage mask. The Windows
oracle decodes the seven-command stream and verifies transformed outer state
`[3,5,37.5,22.5]` and nested intersection `[15.5,12.5,25,15]` exactly, then
proves that antialiased clip mode produces no partial scene. Managed build is
warning-free, contracts pass 5/5, and the allowlist remains 123 exports.
Incremental Windows 11 ARM64 Parallels MSVC 19.44/SDK 10.0.26100.0 compiles
under `/W4 /WX` and passes the live Direct2D regression 1/1. Provider SHA-256
is `9C38D9BFFC95D7453EDCA5F3D63B53C973C1E24F9DDA2EB3214477BF497464AE`;
clean-checkout ABI v34 CI qualification is pending.

ProGPU ABI v35 at implementation `226085da` and documentation checkpoint
`0b8d54f5` adds typed Direct2D linear and radial gradient brushes to native
scene translation. During `ID2D1CommandList::Stream`, the Windows adapter
queries `ID2D1LinearGradientBrush`, `ID2D1RadialGradientBrush`, and
`ID2D1GradientStopCollection1`, snapshots finite ordered stops into ProGPU's
backend-neutral brush table, and releases every COM reference before return.
Clamp, wrap, and mirror reuse the existing pad, repeat, and reflect shader
paths; no CPU rasterization, readback, or Windows-only retained resource is
introduced.

The translator stores Direct2D target-relative gradient coordinates as
`inverse(active draw transform) * inverse(brush transform)`. Its synchronous
COM-identity cache includes the active draw transform, so reuse under a new
transform cannot accidentally share the old material mapping. The admitted
color subset is sRGB-to-sRGB straight alpha, plus premultiplied interpolation
when all stops have uniform alpha and therefore the same mathematical result.
Varying-alpha premultiplied interpolation, other color spaces, and
non-invertible transforms fail closed with typed unsupported state and no
partial stream. Source buffer precision remains a raster quality dimension
for the cross-backend pixel gate rather than a CPU stop-quantization step.

The native oracle decodes six draws, two nested clips, four brushes, six
gradient stops, two distinct mappings for one reused brush, and a radial
origin. Its negative case proves the varying-alpha premultiplied boundary.
Managed AOT contracts pass 5/5 and the package builds warning-free. Windows 11
ARM64 Parallels with MSVC 19.44/SDK 10.0.26100.0 compiles provider and test
under `/W4 /WX`; the live regression passes 1/1 in 1.70 seconds (2.01 seconds
total under concurrent VM load), with exactly 123 exports. The incremental
qualification payload SHA-256 is
`B545679CDCC7C81A826A333D3975C8BB7E8ED977A58FFBFC0601D4431DAAA368`;
the resulting provider SHA-256 is
`E5651DF33F23EB909FF2AB42F2A4E3592CDE81E21B57B3ADABFF38F493FDC2ED`.
Clean-checkout ABI v35 CI qualification is pending.

ProGPU ABI v36 at implementation `e9788c5e` and documentation checkpoint
`c4e4ce34` adds genuine Direct2D `ID2D1Geometry` fill ingestion. A typed
`ID2D1SimplifiedGeometrySink` captures Direct2D's cubic-and-line contours,
excludes hollow figures, preserves open/closed topology and alternate/nonzero
fill rules, and emits at most 1,048,576 finite segments into ProGPU's existing
pointer-free path resource. The active Direct2D draw matrix remains the path
transform, and typed local/target geometry bounds become conservative scene
bounds. Every COM callback resource is released before return.

This is one-time device-independent scene compilation, not CPU rasterization.
The retained path executes through ProGPU's shared GPU path lane on D3D12,
Metal, Vulkan, and WebGPU. Per-primitive edges use the eight-sample quality
path. Aliased path edges and a non-null opacity brush fail closed until their
coverage/mask semantics are exact; stroked `DrawGeometry` remains a separate
slice so caps, joins, miter limits, and dashes are not silently reduced.

The Windows oracle decodes a transformed winding line/cubic figure, its
explicit closing edge, and the absence of hollow-figure segments; its
negative case proves aliased fill returns typed unsupported state with no
partial scene. Managed AOT contracts pass 5/5 and the package is warning-free.
Windows 11 ARM64 Parallels with MSVC 19.44/SDK 10.0.26100.0 compiles provider
and test under `/W4 /WX`; CTest passes 1/1 in 3.00 seconds (3.51 seconds
total). The 96 KiB payload SHA-256 is
`4BD4A70EE6575824BF33F37118434A185405F4BE3B484ADE2AE4B53374820F54`;
the unchanged 123-export provider SHA-256 is
`12467CF6BE48235928B396A76AD5AE0AAD15CAA3E1949AB8A4E9BA4323EB744A`.
Explicit matrix field assignments also close the ClangCL anonymous-union
warning found by the ABI v35 clean runner. ABI v36 clean qualification is
complete for this boundary: clean ProGPU Build run `33345291817`, Windows x64
job `99348168246`, compiled provider and test under ClangCL `/W4 /WX` and
passed all 12 native CTests, including Direct2D in 0.17 seconds. Its overall
red result occurred later when an unrelated managed renderer readback lost the
Microsoft Basic Render Driver. Clean MSVC compatibility job `99348168261`
passed independently.

ProGPU ABI v37 at implementation `163fa686` adds genuine Direct2D stroked
geometry ingestion. `ID2D1CommandSink::DrawGeometry` supplies the original
stroke width/style and active draw transform to `ID2D1Geometry::Widen`, so the
Windows platform resolves exact caps, joins, miters, custom dashes, and
`ID2D1StrokeStyle1` normal/fixed/hairline transform behavior. The resulting
filled outline is captured through the typed simplified-geometry sink and
retained as an identity-transformed, pointer-free ProGPU path with bounds from
`GetWidenedBounds`. Brush mapping still observes the active draw transform.

The conversion runs once while compiling the command list; D3D12, Metal,
Vulkan, and WebGPU replay the retained outline through the existing GPU path
rasterizer. It introduces no CPU pixel raster/readback/repacking path and
retains no COM object. Aliased edges, invalid typed inputs, unsupported
widening, and capacity overflow fail closed. The Windows native oracle compares
the translated stroke topology with a second genuine Direct2D `Widen` result
for a transformed custom dashed/beveled/capped fixed-transform stroke, then
decodes both fill and stroke path resources. Managed AOT contracts pass 5/5
and the package builds warning-free. The final 95,520-byte payload SHA-256 is
`304477EB0796599D9015E7652DF15AEA53A61A79B69B93CFBD52101F7CA41974`.
Windows 11 ARM64 Parallels with MSVC 19.44/SDK 10.0.26100.0 compiles under
`/W4 /WX`; focused CTest passes 1/1 in 40.29 seconds (78.46 seconds total under
concurrent guest load). No native export is added.

ProGPU ABI v38 at implementation `a308e7df` adds the first exact Direct2D
opacity-layer ingestion. Full-target `D2D1::InfiniteRect()` layers with finite
uniform opacity, no geometric/opacity mask, and
`D2D1_LAYER_OPTIONS1_NONE` compile to the existing backend-neutral isolated
layer. ProGPU therefore applies opacity once when compositing the grouped
content—including overlapping descendants—and owns the pooled GPU layer on
D3D12, Metal, Vulkan, and WebGPU. No `ID2D1Layer` COM identity, CPU pixel
fallback, readback, or repacking enters the retained scene.

The command sink now uses one bounded typed scope stack for clips and layers,
so Direct2D's valid nested pairs are retained and overlapping/wrong-order pops
fail with typed drawing state. Finite content bounds, geometric masks, opacity
brushes, `INITIALIZE_FROM_BACKGROUND`, and `IGNORE_ALPHA` remain fail-closed
until their distinct native bounds/mask/backdrop semantics are wired.

The Windows oracle decodes one 37.5% source-over layer containing two
overlapping rectangles inside an outer axis clip and requires exact
save/push/pop/restore order. A negative command list proves background
initialization produces no partial scene. Managed AOT contracts pass 5/5 and
the package builds warning-free. Windows 11 ARM64 Parallels with MSVC
19.44/SDK 10.0.26100.0 recompiles provider/test under `/W4 /WX`; the fresh
test exits zero. The final 97,082-byte payload SHA-256 is
`84A118A67091ED4DA4854B1B00A4AEB26F760073D22A729DFCE1B8460859C270`.
Provider SHA-256 is
`305C1D7D3BC72F0CFC016778721CC36D90FDC91ABE1F9FCDE5DA2A8C5CFEF121`;
all 123 exports exactly match the checked-in allowlist.

ProGPU ABI v39 at implementation `35a8fadc` extends grouped-opacity layers to
finite Direct2D `contentBounds` under axis-preserving active draw transforms.
The rectangle is transformed once at `PushLayer` into exact target-space
ProGPU layer bounds; later transform changes cannot move it. Scale,
translation, reflection, and their combinations are exact. Rotation and shear
remain fail-closed until the transformed region can flow through a native
geometry-mask/coverage resource instead of a broadened axis-aligned box.

The Windows oracle maps `[1,2,21,22]` through
`[2,0,0,0.5,7,9]` and decodes target bounds `[9,10,40,10]`, while retaining
37.5% grouped opacity and valid outer-clip nesting. Managed contracts pass 5/5
and AOT build is warning-free. Windows 11 ARM64 Parallels rebuilds provider
and test from deleted objects with MSVC 19.44/SDK 10.0.26100.0 `/W4 /WX`; the
fresh native executable exits zero. Payload SHA-256 is
`EDCD1850DABE2055AC05B6ACAC5583ADA8899C5A7806FC8A177551FF7D03B282`;
provider SHA-256 is
`C42A075E13706B42F7AA617CA437A194B20076BB538F5C2E91520A4F28BFE81E`,
with all 123 exports matching the allowlist.

ProGPU ABI v40 at implementation `21be13a9` extends the same Direct2D
command-list ingestion path with per-primitive geometric layer masks. Genuine
`ID2D1Geometry` masks are simplified once to filled line/cubic paths, then
serialized as pointer-free ProGPU vector-mask resources. The retained transform
is `maskTransform * activeDrawTransform`, matching Direct2D's rule that the
mask transform is relative to the world transform. Exact Direct2D target mask
bounds constrain full-target layers and intersect finite content bounds.

Execution stays in ProGPU's shared GPU path rasterizer and isolated-layer
compositor on D3D12, Metal, Vulkan, and WebGPU; no CPU pixel fallback,
readback, repacking, per-segment submission, or retained COM pointer is added.
Empty filled geometry becomes an empty layer. Aliased masks, opacity brushes,
background initialization, ignored alpha, non-finite transform composition,
and unsupported geometry fail closed rather than approximating coverage.

The Windows oracle independently obtains transformed bounds from the genuine
Direct2D geometry and decodes the layer reference, intersected bounds,
line/cubic topology, fill rule, eight-sample mask, and composed transform. Its
negative command list proves aliased masks emit zero partial scene bytes.
Managed AOT contracts pass 5/5 and build warning-free. Windows 11 ARM64
Parallels rebuilds provider/test from deleted objects under MSVC 19.44/SDK
10.0.26100.0 `/W4 /WX`; the fresh native executable exits zero. The
170,496-byte provider SHA-256 is
`21CB1B6F5DD483A6E6F1F3546D76C1EC158A22F042120AA8A503247CF58B4789`,
with all 123 exports matching the allowlist.

ProGPU ABI v41 at implementation `b84845fb` adds finite Direct2D
opacity-brush layers through the existing backend-neutral GPU brush-mask
resource. Genuine solid, linear-gradient, and radial-gradient brushes become
pointer-free material and stop data. Local content bounds plus the active draw
transform define mask coverage, and inverse draw/brush coordinates retain
Direct2D target-space mapping. The mapped alpha multiplies the isolated layer
at composition time, independently of uniform group opacity.

The R8 brush mask is generated and consumed on the D3D12, Metal, Vulkan, and
WebGPU paths without CPU pixels, readback, repacking, per-stop submission, or
retained COM pointers. Full-target opacity-brush layers remain fail-closed
until their content-derived bounds can be retained. Combined geometric and
opacity-brush masks remain fail-closed until ProGPU's executable composite-mask
resource is exposed through the native scene builder.

The Windows oracle decodes a transformed genuine two-stop Direct2D linear
brush, exact target/local bounds, active transform, 75% brush opacity, and
inverse draw/brush coordinates. Managed AOT contracts pass 5/5 and build with
zero warnings. Windows 11 ARM64 Parallels rebuilds provider/test from deleted
objects under MSVC 19.44/SDK 10.0.26100.0 `/W4 /WX`; the fresh native
executable exits zero. The 176,640-byte provider SHA-256 is
`50FD9745C40EE045B53F06D1CD089B48F20BABC502D48DB014BAD795A3466C7F`,
with all 123 exports matching the allowlist.

ProGPU ABI v42 at implementation `f56ebe75` combines finite Direct2D
geometric masks and opacity brushes through the reusable typed composite-mask
builder added at `1ce62657`. The pointer-free resource contains the brush
child, exact vector path/segments, and shared stops. ProGPU rasterizes both R8
children and multiplies them in `ClipCompose.wgsl` on D3D12, Metal, Vulkan, and
WebGPU without a CPU pixel fallback, readback, repacking, or retained COM data.

The native oracle requires a genuine transformed line/cubic geometry and
two-stop Direct2D linear brush to decode as two components, one brush, one
path, three segments, and two stops with exact content/mask bound intersection.
Managed AOT build is warning-free and contracts pass 5/5. After the Windows
VM's existing restart restored Guest Tools, the exact source archive SHA-256
`E01D2B571D8C11CCC41A3639DEBE5C4DB4B08CE571A60B0C4EE4802F80DEFBAC`
was extracted and confirmed as ABI v42. Windows 11 ARM64 Parallels rebuilt the
provider/test cleanly with MSVC 19.44/SDK 10.0.26100.0 `/W4 /WX`, and the
native executable exits zero. The 181,248-byte provider SHA-256 is
`D20084AFFC6C8FE39C2F10EBBBA565BB8CA0D6C0771B595A33C5527135F09698`.

ProGPU ABI v43 starts an explicit ProGPU-owned Direct2D COM facade. Native
callers create a typed scene recorder and acquire a caller-owned genuine
`ID2D1CommandSink1*` implemented by the C++ backend. Supported `BeginDraw`,
state, drawing, and `EndDraw` callbacks compile directly into the same
pointer-free semantic scene used by LibreWPF native MIL on D3D12, Metal,
Vulkan, and WebGPU. Canonical `IUnknown`/base/versioned sink identity is
preserved; the recorder retains its own reference, rejects serialization until
recording is complete, and retains no COM pointer in the scene.

This is an explicit compatibility factory rather than a replacement
`d2d1.dll`. Full ProGPU-owned factory, immutable geometry, device-context,
brush/bitmap, command-list, and effect vtables will be added in dependency
slices while unsupported callbacks continue to fail closed. The initial
native oracle issues clear and fill callbacks directly against the ProGPU COM
sink, performs required-size and write passes, and verifies the scene identity,
command/brush counts, and clear metadata without first creating a system
Direct2D command list.

The exact ABI v43 source archive SHA-256 is
`93F348B9C81F8D8211D24D9D0D145F620DD2EFBF9930D009B3826A8E46B4B05C`.
Windows 11 ARM64 Parallels rebuilds the provider/test cleanly with MSVC
19.44/SDK 10.0.26100.0 `/W4 /WX`; the native oracle exits zero. `dumpbin`
matches all 127 allowlisted exports exactly. The 183,296-byte provider hash is
`A6B2D9CFA4222846D91081F793BB3D6BAFC1F8C93854933DDD528BFE988D2533`,
and the test executable hash is
`08A3E37727EA14A579D6333E3E20914D15DE17F4F016AE10E6EC368F330A474D`.

ProGPU ABI v44 extends the explicit COM facade with a ProGPU-owned
`ID2D1Factory1` and immutable `ID2D1RectangleGeometry`. The factory preserves
canonical `IUnknown`/base-factory identity, publishes the standard
`ID2D1Multithread` contract, and returns `E_NOTIMPL` plus null outputs for
resource families outside this slice. Rectangle objects own the factory and
support retrieval, transformed bounds, fill containment, simplification,
tessellation, area, length, and point-at-length queries.

LibreWPF can pass the ProGPU geometry directly to the existing
`ID2D1CommandSink1::FillGeometry` recorder. Standard COM vtable calls lower it
to the same pointer-free ProGPU vector-path scene consumed by D3D12, Metal,
Vulkan, and WebGPU, without a system Direct2D geometry dependency, retained COM
pointer, CPU pixel fallback, or alternate renderer. Creation remains an
explicit ProGPU API; it does not replace `D2D1CreateFactory` or shadow
`d2d1.dll`. Focused managed contracts pass 5/5. The exact implementation
checkpoint is `123d2371`; its committed source archive SHA-256 is
`7F903F5B62FBA969359F8363E4E7C11495F9F76730CDBCADEAE4EA3AE021071A`.
Windows 11 ARM64 Parallels rebuilds it cleanly with MSVC 19.44/SDK
10.0.26100.0 `/W4 /WX`, and the native oracle exits zero. `dumpbin` matches all
128 allowlisted exports exactly. The 191,488-byte provider SHA-256 is
`3D90668C81E5113EF5A3C1B86EC13CC5B4B6E09B2C070F753CF5276AE8BCB033`;
the 111,104-byte test executable SHA-256 is
`7910843D99080398B21DDD8F383FBEBBCB99E662B76338800C97034844B4C722`.

ProGPU ABI v45 adds a mutable ProGPU-owned `ID2D1SolidColorBrush` to the
explicit compatibility-factory domain. The object preserves canonical
resource/brush/solid-brush COM identity and factory ownership, synchronizes
valid color/opacity/transform state, and rejects invalid creation with typed
status/HRESULT output. Invalid void-returning Direct2D mutations retain the
last valid state.

The direct COM oracle now records with only ProGPU-owned factory, rectangle,
solid brush, and command sink objects. LibreWPF therefore has a system-Direct2D
independent COM resource-to-scene path while the same pointer-free brush and
drawing commands continue to render through D3D12, Metal, Vulkan, and WebGPU.
Focused managed contracts pass 5/5. The exact implementation checkpoint is
`73b6ff5e`; its committed source archive SHA-256 is
`59A755509F2E3FF32B8A4C5FE5C32CB7C8752C10B2A02F84276393D2FC157DDA`.
Windows 11 ARM64 Parallels rebuilds it cleanly with MSVC 19.44/SDK
10.0.26100.0 `/W4 /WX`, and the native oracle exits zero. `dumpbin` matches all
129 allowlisted exports exactly. The 195,584-byte provider SHA-256 is
`4126FB918B4A577BB728BF1E0B27E35E388185841223BBAD4044FD80DEE836ED`;
the 113,664-byte test executable SHA-256 is
`5B6EC4E52D17BB185A3E513A22628CC9BF93AE98AF28AFFD90F2FC448DFEB45C`.

ProGPU ABI v46 adds ProGPU-owned `ID2D1PathGeometry1` and
`ID2D1GeometrySink` identities through both standard factory path-creation
vtables. The one-shot sink records line, cubic, quadratic, and arc segments,
fill mode, segment flags, and filled/hollow open/closed figures; successful
`Close` publishes an immutable path. Invalid or abandoned construction fails
closed, and the public segment count includes Direct2D's implicit closed-figure
edge while `Stream` retains the canonical `EndFigure(CLOSED)` vocabulary.

LibreWPF can pass that path directly to the existing ProGPU
`ID2D1CommandSink1::FillGeometry` recorder. Supported path queries cover
transformed bounds, simplification, ordinary non-overlapping fill
containment/area, length, point-at-length, and point-plus-segment data. Complex
self-intersection/overlap analysis, strokes/widening, tessellation, outline,
geometry compare, and boolean combination remain gated and return
`E_NOTIMPL`; no CPU raster fallback or system Direct2D delegation is hidden
behind those calls. The direct native oracle records the full
line/quadratic/cubic/arc vocabulary and differentially checks counts, bounds,
and flattened length against genuine Windows Direct2D before serializing the
same pointer-free path scene for D3D12, Metal, Vulkan, and WebGPU. Focused
managed contracts pass 5/5. The exact implementation checkpoint is
`3f42538c`; its committed source archive SHA-256 is
`32A3ECA03C6C721B505D40A6638A7D55E139C6132E65C296DFFFBD4D2A633EC3`.
Windows 11 ARM64 Parallels rebuilds it cleanly with MSVC 19.44/SDK
10.0.26100.0 `/W4 /WX`, and the native differential oracle exits zero.
`dumpbin` matches all 129 allowlisted exports exactly. The 225,280-byte
provider SHA-256 is
`681EC3239D4B235BDD0E024A9D3C1DCD5D0444F8F1ACD3CB6FE31F0DC8A6940B`;
the 118,272-byte test executable SHA-256 is
`1845C2C96B3B8AA0DA46D909384AB3D417AB607205EAB921C84AC626FB084586`.

## Native MIL canonical D3DImage checkpoint

ProGPU `72c9d794`/`20918afb` and this LibreWPF checkpoint add canonical
`TYPE_D3DIMAGE` (97), `MilCmdD3DImage`, and `MilCmdD3DImagePresent` replay.
Source-built `D3DImage` implements the neutral `IPortableD3DImageSource` and
forwards provider invalidation through `IPortableInvalidationSource`.
`PortableD3DImageSourceFactory.Attach(...)` is mutually exclusive with the
legacy process-local D3D9 back buffer and cloning retains the typed provider
without calling `wpfgfx`.

ProGPU's exact base-native export allowlist now pins both the canonical
BitmapSource and D3DImage external-image sidebands, so every native package
lane rejects a build that compiles these zero-copy paths without exporting
them.

The compiler emits WPF's exact 24-byte update and 16-byte present packets with
zero COM pointers and zero event handle, records dimensions/content version,
and binds an `IProGpuTextureLeaseSource` through the native sideband. Raw
process pointers and handles never enter the portable ABI. The host merges
BitmapSource, MediaPlayer, and D3DImage sources through three sorted indices,
acquires all context-qualified leases before replacing the old table, and
disposes a D3DImage lease only after submission ownership has moved forward.
This permits a Windows DXGI adapter to implement keyed-mutex/shared-fence
synchronization entirely in lease acquire/release while Metal/Vulkan/WebGPU
providers use the same consumer contract.

Apple Silicon validation passes the ProGPU native MIL CTest, the managed
canonical packet test, 117/117 LibreWPF compiler/session tests, three focused
D3DImage seam tests, and the source-built PresentationCore build with zero
errors. The exact ProGPU `1f1d921b` checkpoint then rebuilt and relinked on
Ubuntu 24.04 ARM64 and Windows 11 ARM64. Both guests pass the native MIL CTest
1/1 and expose
`progpu_native_mil_channel_set_d3d_image_external_image`. Ubuntu SHA-256 is
`21798600a4c5d4f4a58d6ea456b5919fa782164d4ebf0ab9f40f1949dcb0ea2e`
for `libprogpu_native.so` and
`c485873cf4d532ab956a44ed729a399486805ba83d815b624fe1c64c8844f3bb`
for the test executable. Windows SHA-256 is
`81f1078e89d9f9f8e4bfdcead25ebc8a84e3d6c425350c865217ff74cb50bd5d`
for `progpu_native.dll` and
`d94382db3f1087573615c91ff983cd2343b6144b68c4f3db160f7c59f0f8568f`
for the test executable. The genuine Windows `ID2D1Bitmap1`/DXGI producer is
implemented at ProGPU `59045316`; ABI v9 supplies typed Dawn lifecycle/lease
binding, real CanvasDevice/CanvasRenderTarget factory-native wrappers, exact
reverse device/bitmap identity, and the first exact
solid, linear-gradient, radial-gradient, and geometry resource round trips.
Microsoft Win2D device/target/brush/geometry wrapping now has a package-deployed success
oracle. The native DirectWrite text-format/draw path is ABI-v16 qualified and
ABI v17 implements retained text layouts, ABI v18 implements typed range
formatting, ABI v19 implements OpenType typography, and ABI v20 implements
system font-face references plus shaped glyph-run drawing with corresponding
official-Win2D oracle assertions. ABI v21 adds GPU-native color-font drawing
with a typed selected-path diagnostic. Signed package execution, remaining
typography/color-paint customization, complete
device-loss recreation, and remaining image/effect resource-family tests
remain required.

## Next parity gates

1. Implement the remaining 2D/3D resource, media, cache, effect, and nested
   render-data command families using the complete generated WPF MCG layouts.
2. Add remaining non-bitmap image sources, extend the package-qualified
   Microsoft Win2D device/target wrappers to broader resource families,
   complete the device-loss gate, and add remaining exact WPF-compatible arc
   lowering, and
   remaining multi-guideline draw-family deformation, general Visual
   effect/clip/mask/opacity ordering, remaining
   opacity-mask/effect/dynamic-guideline push/pop state,
DirectWrite/system-display text realization, destructive device-loss injection
and replacement-domain reconstruction, and the
   explicit advanced glyph/text gaps listed above.
3. Complete general multi-guideline geometry, transformed/nonorthogonal advanced effect
   bounds, and broader
   arbitrary-geometry clip/mask/effect gates on the now-executable local-space
   cache primitive.
4. Extend the retained compiler session with a canonical Viewport3D sideband
   revision/hash, stable cross-frame object handles, damage production, and
   producer-side incremental snapshot construction. The current deterministic
   snapshot differ retains the native channel, emits mutable packet deltas,
   and avoids unchanged bitmap/font/bounds sideband ABI calls and payload
   copies.
5. Extend the now-live native host lane with popup/window-region/viewport and
   nonuniform presentation support, a provider-resolved Dawn surface owner,
   package-mode sample validation, and pixel comparison against the managed
   portable lane.
6. Complete live provider-resolved Dawn rendering and the remaining adapter
   LUID, limits, resize, occlusion, DPI, lifetime, and non-Parallels retained
   hit-test evidence. The Dawn ARM64 build/ABI and wgpu-native D3D12 live lane
   are now qualified.
7. Implement only the measured D3D11/D3D12/DXGI/D3DCompiler compatibility
   surface required by SDK, SciChart, and interop consumers. Shared textures,
   fences, formats, row pitch, alpha mode, and device loss require explicit
   parity tests.
8. Run package-mode Toolkit/AvalonDock continuously and Xceed paid coverage
   only when the required license environment variables are present.

## Developer commands

```sh
cmake -S external/ProGPU/src/ProGPU.Native \
  -B external/ProGPU/artifacts/progpu-native-mil -G Ninja \
  -DPROGPU_NATIVE_BUILD_WGPU_TARGET=OFF \
  -DPROGPU_NATIVE_BUILD_SAMPLE=OFF \
  -DPROGPU_NATIVE_ENABLE_CPP_MODULES=OFF -DBUILD_TESTING=ON
cmake --build external/ProGPU/artifacts/progpu-native-mil \
  --target progpu_native_mil_tests
ctest --test-dir external/ProGPU/artifacts/progpu-native-mil \
  -R '^progpu_native_mil_tests$' --output-on-failure

dotnet build src/ProGPU.Wpf/ProGPU.Wpf.csproj -c Release \
  -p:ProGpuRuntimePackageVersion=0.1.0-preview.56
dotnet vstest \
  src/ProGPU.Wpf.Tests/bin/Release/net10.0/ProGPU.Wpf.Tests.dll \
  --Tests:ProGPU.Wpf.Tests.Composition.Mil.WpfNativeMilSceneCompilerTests

# Requires a current ProGPU native build and a graphical user session.
./eng/progpu-wpf-native-mil-host-smoke.sh
```

Use the ProGPU full qualification gate before publishing native changes:

```sh
cd external/ProGPU
PROGPU_NATIVE_SKIP_EXTENDED_INTEGRATION=1 ./eng/build-progpu-native.sh
```
