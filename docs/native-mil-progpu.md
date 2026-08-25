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
package, and validation checkpoints described below.

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

The portable `RenderData` snapshot already uses the same framed nested records,
but its resource tokens are one-based indexes into a typed dependent-resource
array. LibreWPF's native producer therefore preserves command bytes and remaps
typed resource tokens to native channel handles. It never reads private WPF
fields or dispatches by type name.

## Implemented checkpoint

ProGPU currently provides:

- A C++20 zero-copy batch reader and transactional channel graph. A rejected
  batch cannot partially mutate live state.
- Complete stable command ID definitions and strict unknown, malformed,
  unsupported, handle, resource-type, graph, and capacity errors.
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
  while Round emits analytic quarter arcs. Degenerate fills stay empty and
  nonempty collapsed dashes fail closed.
- Typed ellipse pen production for fill-only, stroke-only, and combined
  records. Solid ellipse outlines use ProGPU's exact full-ellipse analytic arc,
  preserve non-uniform radii, and publish affine-expanded stroke bounds;
  one-axis ellipses use WPF's SmoothJoin-derived Round/Round capsule and point
  ellipses use the native point disk. Degenerate fills stay empty. Nonempty
  dashed non-point ellipses fail closed pending phase-continuous curve dashing.
- Typed rounded-rectangle pen production for fill-only, stroke-only, and
  combined records. Uniform positive radii use ProGPU's analytic primitive;
  positive independent X/Y radii use its exact elliptical vector path and
  connected-curve stroke. Positive-area records with either radius zero follow
  WPF's sharp-rectangle equivalence and retain rectangle join/dash behavior,
  while nonempty curved dashes fail closed pending phase-continuous curve
  dashing. Degenerate uniform and positive-radius records use the same outer
  widened path with WPF's independently clamped X/Y radii; degenerate
  zero-axis asymmetric records remain fail closed.
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
  phase and DashCap at both geometry-gap boundaries. Dashed curves/smooth
  joins and degenerate tangents adjacent to nondegenerate joins fail closed.
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
  bounded by each leaf. Overlapping nonzero translated-equivalent leaf streams
  currently fail closed at semantic scene compilation, before WebGPU device
  creation, because that exact backend pattern is unsafe on the Parallels D3D12
  adapter. Nonsingular affine arc-bearing children remain analytic:
  ProGPU factors the transformed ellipse basis, preserves parameterization, and
  reverses sweep under reflection. Exact translations preserve the original arc
  fields bit-for-bit except for endpoints/center. Exact singular affine
  transforms produce empty fill and stroke coverage, matching WPF's
  zero-determinant area semantics. Combined children and meaningful group pens
  remain fail closed pending exact contour/stroke composition.
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
  leaves. Stroked operands currently fail closed; combined children inside
  groups also fail closed
  because flattening a boolean result into raw outer-fill contours would change
  WPF semantics.
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
Fant/HighQuality, and NearestNeighbor values onto its shared linear,
Mitchell-Netravali cubic, and nearest semantic samplers. Source-built WPF now
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
ABI and uses a typed transform-only State resource to place the cached quad.
Root offset, affine transform, and opacity are composite-only and no longer
invalidate pixels. Positive finite static or animated RenderAtScale values
rerasterize at the requested size. SnapsToDevicePixels, EnableClearType, and
root composite clip/mask/guideline state remain fail closed; those policies,
nested ordering, and LibreWPF package gates remain open.

The pinned provider/Dawn Metal gate passes the new lifecycle directly: first
render materializes a 24x18 page, an outer translation performs zero content
passes, and 0.5 RenderAtScale materializes a 12x9 page. The complete
package-mode managed Dawn render/readback and forced device-loss recovery also
pass at provider revision `02823bf8d2e56548b2780d6b92ae7065be1d8605` and
Dawn revision `710c33013c53ab2700d332c25ff51430251a8cc4`.

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

## Next parity gates

1. Generate packed protocol size/offset metadata from the checked-in WPF MCG
   model rather than manually extending command declarations.
2. Add dashed ellipse and rounded-rectangle pen draws, curve dashes, exact
   degenerate zero-axis asymmetric rounded-rectangle widening, exact
   translated-equivalent EvenOdd overlap execution, exact combined children
   inside groups, WPF epsilon-near-coincident gradient-stop normalization,
   duplicate-endpoint Pad outside-color distinction, cap-only degenerate
   gradient pen strokes, ImageDrawing rect animations and non-bitmap image
   sources, dynamic/multiple-guideline piecewise deformation, general Visual
   effect/clip/mask/opacity ordering, animated and Box effects, remaining
   push/pop state, DirectWrite/system-display text realization, and the
   explicit advanced glyph/text gaps listed above.
3. Complete the remaining WPF BitmapCache pixel snapping, ClearType,
   post-raster clip/mask/guideline, nested-ordering, and effects gates on the
   now-executable local-space cache primitive.
4. Cache other stable native handles/generations across frames and emit
   incremental resource updates plus damage instead of rebuilding the initial
   scene batch.
5. Bind compiled semantic streams directly to `NativeCompositor` targets and
   expose an explicit LibreWPF runtime selector.
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
```

Use the ProGPU full qualification gate before publishing native changes:

```sh
cd external/ProGPU
PROGPU_NATIVE_SKIP_EXTENDED_INTEGRATION=1 ./eng/build-progpu-native.sh
```
