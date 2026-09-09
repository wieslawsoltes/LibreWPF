# LibreWPF.Sdk

`LibreWPF.Sdk` is the custom MSBuild SDK surface for running WPF applications on the ProGPU/Silk.NET platform. It is intended to let existing WPF applications move from the WindowsDesktop SDK to the portable LibreWPF platform by changing the project SDK while preserving normal WPF XAML, BAML, resource, theme, and code-behind behavior.

This initial package skeleton layers on the existing WindowsDesktop SDK so WPF markup compilation remains owned by the real `PresentationBuildTasks` implementation. It then selects the portable ProGPU/Silk.NET platform and redirects WPF framework references through either package references or local artifact roots while the port is still source-built.

Package mode is the intended delivery path. It references the ported managed WPF bundle through `ProGpuWpfManagedPackageId`/`ProGpuWpfManagedPackageVersion`, references the ProGPU runtime packages, injects portable activation on non-Windows hosts and for explicit native MIL selection on Windows, and copies resolved managed and native runtime assets to the application output. Local-artifact mode remains available for source-tree validation by setting `ProGpuWpfManagedReferenceRoot` and `ProGpuReferenceRoot`.

For mutable development package versions such as `0.1.0-preview.45`, the SDK clears known WPF and ProGPU runtime assemblies from the app output before recopying package assets. This prevents an incremental app rebuild from launching stale bridge/compositor DLLs after a local package refresh while preserving normal incremental copy behavior for stable package versions. Set `ProGpuWpfClearMutablePackageOutputs=false` to disable this development safeguard.

The SDK owns the package dependency closure. `LibreWPF.Transport` supplies the real managed WPF assembly identities and runtime payload, while `LibreWPF.ProGPU` is the adapter/runtime bridge package and does not publish dependencies on the ProGPU shim `PresentationCore` package.

Existing WPF application projects should keep their normal WPF project shape and switch only the project SDK, whether the original project used `Microsoft.NET.Sdk.WindowsDesktop` or the newer `Microsoft.NET.Sdk` plus `UseWPF=true`. The SDK treats `UseWPF=true` as the app's markup intent, keeps the normal `net*-windows` target-framework shape, and internally redirects framework references to the portable WPF transport and ProGPU/Silk.NET package graph.

Windows, macOS, and Linux are supported runtime targets. A Windows RID restores the same platform-independent `LibreWPF.Transport` payload as the other hosts; no `runtime.win-*` LibreWPF companion package is required or published.

The SDK also supplies the WPF markup compiler defaults and portable runtime-framework default needed by the current build lane, so applications do not need ProGPU-specific item includes, PresentationBuildTasks compatibility properties, or runtime-version pins.

```xml
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.45">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

## Renderer selection

`ProGpuWpfRendererMode` selects the SDK-created window renderer at build time:
`ManagedPortable` is the unchanged default; `NativeMilWgpu` selects canonical MIL
compilation and ProGPU C++ rendering through the typed host factory. Application
XAML/code-behind does not need a custom host or bootstrap:

```xml
<ProGpuWpfRendererMode>NativeMilWgpu</ProGpuWpfRendererMode>
<ProGpuWpfNativeMilHitTesting>true</ProGpuWpfNativeMilHitTesting>
```

For the current native input development lane, use
`dotnet build -p:ProGpuWpfRendererMode=NativeMilWgpu -p:ProGpuWpfNativeMilHitTesting=true`.
Rebuild with `ManagedPortable` to return to the established portable renderer.
This is independent of `ProGpuWpfRenderingBackend=ProGPU`; it does not select a
different compute/SIMD fallback policy. The executable runtime configuration
records `LibreWPF.RequestedRendererMode` for diagnostics, not as proof of the
active renderer. Editing that record at runtime does not change the compiled
bootstrap. Unknown values fail the build; native
executables cannot disable portable references/bootstrap and silently use another
renderer. Failure to register typed source-built activation is a startup error.

Native SDK activation is wired for macOS/Linux/Windows x64 and ARM64 desktop
processes, **not runtime-qualified**.
It requires the matching `ProGPU.Backend.Native` package and its RID-native assets,
and inherits the current native host's full-surface/uniform-DPI restrictions.
Native selection rejects other process architectures before WPF initialization;
in particular, Windows x86 transport assets do not supply a native MIL backend.
On Windows too, the bootstrap selects frozen portable media and lazy typed
providers before source startup, then registers the explicit native host factory.
Source window/input/media ownership and popup desktop/DPI routing use the shared
portable contracts. Missing host registration or native resources fail rather
than silently selecting Windows MIL or managed replay. This connects application
startup but does not establish complete API coverage or application fidelity.
The direct native host harness remains a separate Windows integration path, not
proof that package-mode application activation works there. The default Windows
SDK bootstrap behavior remains unchanged. Windows runtime, popup/input/mixed-DPI
and package application qualification remain required for core delivery.

The final SDK qualification matrix uses the same applications in both modes:

```bash
./eng/progpu-wpf-sdk-ci.sh
PROGPU_WPF_SDK_CI_RENDERER_MODE=NativeMilWgpu ./eng/progpu-wpf-sdk-ci.sh
```

The native lane also requires the direct native host/recovery gate. It retains
Toolkit/AvalonDock, license-controlled paid Xceed and the existing SDK coverage;
unsupported required paths are failures, not permission to skip them. These
commands are qualification work, not an assertion that either lane has passed.

The current repo MVP validation is intentionally apphost-based, because that is how users run a built SDK-switched WPF application. From the repository root:

```bash
./eng/run-progpu-wpf-hello.sh
./eng/run-progpu-wpf-mvp.sh
```

For a fast validation pass that exercises the external no-source-change SDK smoke, the SDK-switch smoke apphost live geometry probe, Hello and MVP `Application.Run` apphost self-tests, and both Hello/MVP live ProGPU/Silk.NET apphost geometry probes:

```bash
./eng/progpu-wpf-mvp-quickcheck.sh
```

The quickcheck expects the local `0.1.0-preview.45` LibreWPF package feed and its ProGPU `0.1.0-preview.55` runtime dependencies to be current. Use the full SDK CI gate when package contents need to be rebuilt from source:

```bash
./eng/progpu-wpf-sdk-ci.sh
```

## Native input qualification

For the native-MIL development lane, select `ProGpuWpfRendererMode=NativeMilWgpu`
and `ProGpuWpfNativeMilHitTesting=true`. The latter requests a complete native
GPU input index; unsupported application coverage fails explicitly. Native host
input without admission does not borrow the managed renderer's index.

The normal SDK default remains managed portable. Native input admission is still
opt-in while core coverage is completed; the native SDK qualification gate enables
it mandatorily. Use freshly built, matching LibreWPF/ProGPU packages: older native
payloads do not export the completion and metadata APIs used by this lane.
