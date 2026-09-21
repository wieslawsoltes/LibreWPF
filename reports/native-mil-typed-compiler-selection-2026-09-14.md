# Native MIL typed Windows compiler selection

Acceptance application: `ProGPU.Wpf.ShowcaseApp`. User action: startup, first
presentation and source point/region input. Blocking path: the common ProGPU
WebGPU device factory and native query execution on Windows.

ProGPU `45147156` connects immutable `WgpuDx12CompilerOptions` to the native
instance extension. Explicit DXC validates the actual loaded native DLL's build
manifest, exact ABI/revisions/feature/lock/hash and all DLL architectures before
instance creation. Explicit compiler library paths remain scoped to startup.
Missing/incorrect artifacts fail rather than silently using FXC. Automatic
selection remains unchanged. Shared surfaces inherit their owner's compiler;
external Dawn/browser devices cannot be reconfigured by this option.

The same instance/device supplies the managed renderer and C++ MIL renderer;
there is no WPF-local compiler, shader fork or CPU geometry fallback. A separate
explicit `ForceFallbackAdapter` option and native consumer `--software-adapter`
switch remove the diagnostic managed-assembly patch previously needed to test
WARP. Hardware remains the ordinary adapter preference. See ProGPU
`docs/native-windows-shader-compiler.md` for configuration, research, startup cost
and remaining package/runtime requirements.

## Verified results

- Backend and native consumer build with zero warnings/errors.
- All 79 focused compiler/context tests pass, including build-pin synchronization,
  architecture/hash/feature rejection and borrowed-provider restrictions.
- Complete rebuilt Metal consumer passes with unchanged automatic selection;
  loaded native library SHA256 is
  `5b5a8f55cdac3f8e4d0efb78a5447e26722e4a1bae2680197b53346c8f7d8300`.
- Full staged Windows ARM64 consumer passes with **current unpatched product
  assemblies**, explicit DXC and development WARP. This covers native ABI,
  document/inline contracts, cubic control hull, retained MIL pixels (38 resources,
  11 draws, 174080 coverage), point/repeated waits, participation, both region
  families and region-first/owner/generation isolation. Process 6896 exits 0.
- Backend DLL SHA256:
  `1beb68b66c7078f0b9aebaf8277694147f9712152f6e2457735d12ac4bb8b5cf`.
  Native DLL SHA256:
  `e696e6a9809f82d5baa5d45c3fcabbf11a19d7888e6c05a7cc663f3893337d52`.
  Feature dependency SHA256:
  `a0cdbedccc490377b94c4e7cf8506d65c85bbc0be1c6cf4d70d2a04c791806e3`.
- Point/bounds/ellipse submissions: 1318.047/2085.784/1292.745 ms. These are
  functional observations, not matched performance benchmarks.
- Real missing-manifest and missing-compiler controls both reject before adapter
  creation (no WARP module loaded), instead of entering an FXC fallback.

## Remaining runtime failures

The matched system-WARP current-product control passes rendering but still stops
after first point submission (1588.796 ms), before its readback. The actual system
WARP path was observed in that child. The Parallels hardware control selects DXC,
then fails shader pipeline creation with `0x80004005`; native invalid-pipeline
handling subsequently aborts. The controls script is terminal with exit 1,
aggregating those two failures. These are not live waits or successful gates.

No product compiler default, timeout, native query assertion, SDK admission,
dependency pin or ordered merge gate changes. Development WARP remains isolated,
testing-only and nonredistributable. Source-project publication and staged DLLs
are not final NuGet package or Showcase qualification. x64 remains required.

Next work: complete permitted DXC artifact packaging and current-package CI;
resolve system native-query execution and the adapter's shader-compiler limits,
then run the complete core source application gates before ordered merges.
Broader DirectX/Direct2D/Win2D expansion remains deferred, not declared finished.
