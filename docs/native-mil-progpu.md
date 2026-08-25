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
The subsequent positive non-uniform rounded-rectangle qualification is pinned
at ProGPU documentation commit `5c7b1924`.

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
  targets, clear color/flags, opaque render data, solid-color brushes, and
  retained affine matrix-transform resources.
- Cycle, multiple-parent, and depth validation for the retained visual graph.
- Nested solid-brush `DrawRectangle`, `DrawEllipse`, and uniform-radius
  `DrawRoundedRectangle` decoding and lowering into ProGPU's pointer-free
  semantic scene stream with cumulative visual state and typed primitive
  metrics. Non-uniform rounded corners fail closed rather than being
  approximated.
- Balanced nested `PushOpacity`/`Pop` decoding, cumulative visual/scope opacity,
  typed semantic-state emission, and strict stack validation.
- Typed `MatrixTransform`, visual-transform, and nested `PushTransform` packet
  decoding. Matrices compose in WPF row-vector order across local visual
  transforms, visual offsets, ancestors, and drawing scopes; transformed draw
  bounds use all four corners. Handle zero is preserved as WPF's balanced
  no-op transform scope, while animation handles and unresolved nonzero
  resources fail closed.
- Typed solid `Pen` resources and nested `DrawLine` lowering through ProGPU's
  reusable geometry-stroke primitive, including all four WPF start/end cap
  kinds, affine stroke bounds, null-pen no-op semantics, line metrics, and
  transactional rejection of animation resources.
- Typed variable-size `DashStyle` resources for line pens, preserving
  thickness-relative intervals, offsets, dash caps, and ProGPU semantic-stroke
  execution across native backends.
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
  connected-curve stroke. Zero-radius records retain rectangle join/dash
  behavior, while nonempty curved dashes fail closed pending phase-continuous
  curve dashing. Degenerate solid records use the same outer widened path with
  WPF's independently clamped X/Y radii.
- Typed retained `LineGeometry`, `RectangleGeometry`, and `EllipseGeometry`
  resources with nested `DrawGeometry` lowering. Optional geometry-local
  affine transforms compose with visual and drawing scopes; line pen semantics
  reuse ProGPU's stroke path while rectangle, rounded-rectangle, and ellipse
  resources reuse native analytic/vector fill and stroke lowering. Animated
  fields and zero-axis asymmetric rounded radii fail closed.
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
  target construction.
- `Compile(...)` selection of wgpu-native or Dawn without changing the existing
  managed portable renderer.
- Fail-closed behavior for unbalanced scopes, untyped or unavailable
  transforms, clips, effects, masks, guidelines, render options, dashed
  curved pens, zero-axis asymmetric rounded rectangles, non-solid brushes, and all
  not-yet-implemented nested commands.
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

The LibreWPF checkpoint passes its focused build and twenty-four native-producer
tests:
they check exact command order, framing, handle remapping, rectangle values,
ellipse and rounded-rectangle values, scRGB brush fields, canonical opacity-
scope translation, typed visual/nested matrix-resource reuse, null transform
scope parity, rejection of untyped transform shapes, unbalanced-scope
rejection, positive non-uniform-radius emission, and zero-axis asymmetric
radius rejection.
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

## Next parity gates

1. Generate packed protocol size/offset metadata from the checked-in WPF MCG
   model rather than manually extending command declarations.
2. Add transform animations and remaining transform resource kinds, dashed
   ellipse and rounded-rectangle pen draws, curve dashes, exact
   translated-equivalent EvenOdd overlap execution, exact
   combined children inside groups, gradients, remaining
   push/pop state,
   images, and
   glyph runs.
3. Cache stable native handles/generations across frames and emit incremental
   resource updates plus damage instead of rebuilding the initial scene batch.
4. Bind compiled semantic streams directly to `NativeCompositor` targets and
   expose an explicit LibreWPF runtime selector.
5. Complete live provider-resolved Dawn rendering and the remaining adapter
   LUID, limits, resize, occlusion, DPI, lifetime, and non-Parallels retained
   hit-test evidence. The Dawn ARM64 build/ABI and wgpu-native D3D12 live lane
   are now qualified.
6. Implement only the measured D3D11/D3D12/DXGI/D3DCompiler compatibility
   surface required by SDK, SciChart, and interop consumers. Shared textures,
   fences, formats, row pitch, alpha mode, and device loss require explicit
   parity tests.
7. Run package-mode Toolkit/AvalonDock continuously and Xceed paid coverage
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
