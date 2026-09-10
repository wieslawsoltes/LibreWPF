# LibreWPF SciChart App

This is the first executable SciChart-focused SDK smoke lane. It uses the normal `LibreWPF.Sdk` project shape and renders Visual Xccelerator-style 2D and 3D chart primitives through the reusable `ProGPU.DirectX` SciChart bridge into a WPF `WriteableBitmap`.

The 2D panel exercises native line, mountain, column, financial, shaped heatmap, and colored sprite marker paths. The line-strip and colored marker paths now use a `ColoredVertex` bridge matching real SciChart's public `SciChart.Data.ColoredVertex` shape; markers map SciChart-style `DrawColoredSprites(...)` start/count semantics to ProGPU-owned instanced textured quad draws with active clipping and per-marker ARGB tinting.

The 3D panel exercises native surface-mesh, waterfall/slice, point-cloud, line-strip, XYZ-data-series ribbon, and triangle-strip draw paths so future SciChart3D axes, grids, surface/waterfall charts, and line-series adapters can land in `ProGPU.DirectX` instead of managed WPF bitmap fallbacks.

By default the sample stays license-free and validates the ProGPU-side render context. Set `PROGPU_WPF_SCICHART_REAL_PACKAGES=1` or build with `-p:ProGpuWpfUseRealSciChartPackages=true` to reference the commercial `SciChart` and `SciChart3D` packages directly. The opt-in lane sets `SciChart.Core.NativeDllLoader.DependenciesPathRoot` to `PROGPU_WPF_SCICHART_NATIVE_DEPENDENCIES_ROOT` or a writable temp directory, reads `SCICHART_RUNTIME_LICENSE_KEY` at startup, calls `SciChartSurface.SetRuntimeLicenseKey(...)`, uses real SciChart 2D/3D data-series APIs, and attempts to create real chart controls beside the ProGPU bridge output.

The real-package lane currently targets `SciChart` and `SciChart3D` version `9.0.0.29196`. Official SciChart WPF docs describe the WPF product as Windows/WPF-oriented, and the install docs call out `SciChart.Charting3D.dll` as a mixed .NET/C++ DLL that requires the Visual C++ 2013 runtime. Local package inspection confirms the NuGet payload has `lib/net462` and `lib/net6.0-windows7.0` assemblies only, with no `runtimes/osx-*` native assets. The restored assemblies reference Windows native payload names such as `AbtLicensingNative.dll`, `VXccelEngine3D.dll`, `d3d9.dll`, `d3d11.dll`, `dxgi.dll`, and `D3DCOMPILER_47.dll`. On macOS this is reported as a native runtime gap, not as a missing package reference. The durable fix is a ProGPU-hosted native/ABI compatibility facade for those SciChart licensing/Visual Xccelerator/Direct3D entry points, backed by `ProGPU.DirectX` and WebGPU. The real-package sample now passes compile-time SciChart anchor types to `ProGpuDirectXNativeDependencyInspector` and `ProGpuDirectXNativeResolver`, keeping assembly-handle native loader state centralized in `ProGPU.DirectX` rather than the sample. The inspector, compatibility planner, and ABI planner report native dependencies from the actual SciChart assemblies, classify them as ProGPU native facade work, host OS Win32 abstraction work, managed assembly hints, or unknown modules, and list exact native exports that the facade must implement next. It also uses `ProGpuDirectXNativeFacadeSourceEmitter` to summarize how many native exports can be scaffolded as NativeAOT `UnmanagedCallersOnly` stubs before those stubs are routed to real ProGPU/OS implementations. It installs `ProGpuDirectXNativeResolver` before `SciChartSurface.SetRuntimeLicenseKey(...)`; without `PROGPU_DIRECTX_NATIVE_FACADE_PATH` or `PROGPU_DIRECTX_NATIVE_FACADE_MODULES` pointing at a future native facade, the resolver returns zero and records `facade not configured`/load failures so unsupported native entry points remain explicit instead of being hidden by managed fallback code.

Run from the repository root:

```bash
./eng/run-progpu-wpf-scichart.sh
```

Run non-interactive renderer validation:

```bash
PROGPU_WPF_SCICHART_VALIDATE=1 ./eng/run-progpu-wpf-scichart.sh
```

Run `Application.Run` validation:

```bash
PROGPU_WPF_SCICHART_RUN_VALIDATE=1 ./eng/run-progpu-wpf-scichart.sh
```

Rebuild local SDK packages before running:

```bash
PROGPU_WPF_SCICHART_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-scichart.sh
```

Build and run with real SciChart packages:

```bash
PROGPU_WPF_SCICHART_REAL_PACKAGES=1 ./eng/run-progpu-wpf-scichart.sh
```
