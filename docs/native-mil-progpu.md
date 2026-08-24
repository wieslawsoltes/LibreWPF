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
latest `main`; the superproject records exact reviewed ProGPU commits.

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
  exact join/dash metadata and affine-expanded stroke bounds.
- Typed ellipse pen production for fill-only, stroke-only, and combined
  records. Solid ellipse outlines use ProGPU's exact full-ellipse analytic arc,
  preserve non-uniform radii, and publish affine-expanded stroke bounds;
  nonempty dashed ellipses fail closed pending phase-continuous curve dashing.
- Typed uniform rounded-rectangle pen production for fill-only, stroke-only,
  and combined records. Positive-radius solid outlines use ProGPU's exact
  analytic rounded-rectangle stroke with affine-expanded bounds; zero-radius
  records retain rectangle join/dash behavior, while nonempty curved dashes
  fail closed pending phase-continuous curve dashing.
- Typed retained `LineGeometry`, `RectangleGeometry`, and `EllipseGeometry`
  resources with nested `DrawGeometry` lowering. Optional geometry-local
  affine transforms compose with visual and drawing scopes; line pen semantics
  reuse ProGPU's stroke path while rectangle, rounded-rectangle, and ellipse
  resources reuse native analytic fill/stroke lowering. Animated fields,
  and non-uniform rounded radii fail closed.
- Typed retained general `PathGeometry` production from
  `IPortableGeometryPathSource`, using the shared exact local-path bounds
  reader and canonical WPF path/figure/fixed-segment records. Native fill-only
  lowering supports line, quadratic, and cubic contours, EvenOdd/Nonzero fill,
  implicit closure, and geometry-local affine transforms. Path arcs and
  meaningful path pens remain explicit fail-closed execution gaps.
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
  conversion, exact ellipse and uniform rounded-rectangle fill translation,
  balanced opacity/transform-scope translation, transform-resource identity
  reuse, typed `IPortablePenSource` solid/dashed line and rectangle-pen
  translation, typed solid ellipse and uniform rounded-rectangle pen
  translation, typed `IPortablePrimitiveGeometrySource` translation for exact
  line/rectangle/ellipse state, a typed single-line path fallback, and native
  target construction.
- `Compile(...)` selection of wgpu-native or Dawn without changing the existing
  managed portable renderer.
- Fail-closed behavior for unbalanced scopes, untyped or unavailable
  transforms, clips, effects, masks, guidelines, render options, dashed
  curved pens, non-uniform rounded rectangles, non-solid brushes, and all
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

The LibreWPF checkpoint passes its focused build and twenty-two native-producer
tests:
they check exact command order, framing, handle remapping, rectangle values,
ellipse and rounded-rectangle values, scRGB brush fields, canonical opacity-
scope translation, typed visual/nested matrix-resource reuse, null transform
scope parity, rejection of untyped transform shapes, unbalanced-scope
rejection and non-uniform-radius rejection.
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
fill rule, flags, and retained handle mapping. Untyped geometry shapes remain
rejected.

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

## Next parity gates

1. Generate packed protocol size/offset metadata from the checked-in WPF MCG
   model rather than manually extending command declarations.
2. Add transform animations and remaining transform resource kinds, dashed
   ellipse and rounded-rectangle pen draws, non-uniform rounded rectangles,
   path arcs/strokes, geometry groups/combined geometry, gradients, remaining
   push/pop state, clips, images, and
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
