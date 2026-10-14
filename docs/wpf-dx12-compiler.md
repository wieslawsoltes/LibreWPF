# Explicit Windows compiler selection

The original Gallery clipboard page spends substantial startup time compiling
its D3D12 shaders with the pinned backend's automatic FXC policy. Applications
can now select the existing ProGPU DXC implementation through the WPF host and
SDK. Automatic compiler and adapter selection remain unchanged.

For a package-mode native-MIL WPF executable, set an explicit Windows RID and:

```xml
<ProGpuWpfRendererMode>NativeMilWgpu</ProGpuWpfRendererMode>
<RuntimeIdentifier>win-arm64</RuntimeIdentifier>
<ProGpuWpfDx12ShaderCompiler>Dxc</ProGpuWpfDx12ShaderCompiler>
```

`win-x64` is also accepted. `Automatic` preserves the existing policy and `Fxc`
explicitly requests FXC. Invalid values, unsupported RIDs, library projects,
disabled bootstrap and unsupported renderer/reference modes fail the build.
DXC adds `ProGPU.Backend.Dx12` at the exact `ProGpuPackageVersion`, including
central package management. Core initialization verifies the actual loaded
native library, its manifest and compiler architecture. No DLL replacement,
automatic fallback, environment mutation or ABI change is introduced.

Direct hosts use `ProGpuWpfWindowOptions.Dx12CompilerOptions`. The immutable
descriptor passes through source activation and both managed-portable and native
MIL target creation. Shared windows/popups inherit the live owner's descriptor
when unspecified, including its compiler directory. Recovery resolves the current
owner. Explicit conflicting requests reach the existing core rejection path.
The SDK setting targets native MIL because Windows managed-portable SDK startup
uses Windows MIL; the direct managed host API retains its separate contract.

The package bundle includes the optional runtime. Source package production
extracts its original compiler/runtime/notices from the same whole-successful
ProGPU Build used for native/SDK payloads, then uses the existing exact-source
package version. Compiler staging has its own destination. Release preparation
retains the exact immutable release package. Package cache and system libraries
are never patched.

## Evidence and acceptance

[Fixed comparison 38631278791](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38631278791)
uses byte-verified public preview.69 packages from qualified ProGPU
`aef75180bcdc3b937d004b6b949e0e91724c7981`, the original two complete Gallery
scenes, Microsoft Basic Render Driver, 884 by 861 BGRA targets and DPI 1.
The ordered fresh-process plan was stock/FXC, packaged/FXC, packaged/DXC,
packaged/DXC, packaged/FXC. Total seconds were 45.766, 45.860, 18.062, 18.141,
43.593. The actual compiler/module hashes, every original RGB pixel and original
60/55-second diagnostic bounds passed. Command counts, uploads and submissions
were unchanged. First submission took 21.49–22.95 seconds with FXC and
1.27–1.30 seconds with DXC. This is offscreen evidence, not application acceptance.

The original Gallery consumer now requests DXC through the real SDK and verifies
its packaged DLL/manifests and actual selected compiler. Its controller returns
to the original 60-second bound, with 57 seconds for the host and the unchanged
55-second foreground bound. Original sources, both clipboard actions, full image
comparison, GPU completion, both desktop captures and cleanup remain required.
The previous approved 90/87-second FXC exception remains historical evidence.
Whole LibreWPF Build/Docs and this full application acceptance are pending at
this implementation checkpoint; #117 remains open until those gates pass.

## Design references and applicability

Primary references were checked on 14 October 2026. Only the existing ProGPU
compiler contract is transported; no third-party implementation is copied.

- [Vello renderer options](https://docs.rs/vello/latest/vello/struct.RendererOptions.html)
  make shader initialization an explicit renderer-creation choice. Adopt that
  ownership boundary; do not change worker counts or add another pipeline cache.
- [Direct2D performance guidance](https://learn.microsoft.com/en-us/windows/win32/direct2d/improving-direct2d-performance)
  informs preserving device-bound resource reuse. Existing device generations,
  lazy pipelines, batching and demand-driven uploads remain authoritative.
- [WebRender overview](https://firefox-source-docs.mozilla.org/gfx/RenderingOverview.html)
  separates layout, retained scenes and visible frame preparation. Keep those
  boundaries, culling and clip ownership independent of compiler selection.
- [SkParagraph](https://github.com/google/skia/blob/main/modules/skparagraph/include/Paragraph.h),
  [Parley layout](https://docs.rs/parley/latest/parley/struct.Layout.html) and
  [HarfBuzz shape plans](https://harfbuzz.github.io/harfbuzz-hb-shape-plan.html)
  keep layout/shaping reusable. This change neither reshapes text nor changes
  font fallback, variations, DPI, hinting or glyph/path/texture cache identity.
- [DirectWrite hit testing](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nf-dwrite-idwritetextlayout-hittestpoint)
  and [Win2D geometry](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Geometry_CanvasGeometry.htm)
  inform keeping source interaction and geometry separate from raster policy.

The WPF option copies are O(1) startup work. Existing core manifest/hash checks
remain initialization-only; there is no per-frame IO, new submission, CPU pixel
path or native crossing. Both renderers receive the same owned device options,
so the C++ MIL renderer requires no parallel compiler implementation. macOS,
browser, Dawn and other backend compiler policies remain unchanged.
