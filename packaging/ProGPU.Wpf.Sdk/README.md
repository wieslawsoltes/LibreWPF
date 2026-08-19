# LibreWPF.Sdk

`LibreWPF.Sdk` is the custom MSBuild SDK surface for running WPF applications on the ProGPU/Silk.NET platform. It is intended to let existing WPF applications move from the WindowsDesktop SDK to the portable LibreWPF platform by changing the project SDK while preserving normal WPF XAML, BAML, resource, theme, and code-behind behavior.

This initial package skeleton layers on the existing WindowsDesktop SDK so WPF markup compilation remains owned by the real `PresentationBuildTasks` implementation. It then selects the portable ProGPU/Silk.NET platform and redirects WPF framework references through either package references or local artifact roots while the port is still source-built.

Package mode is the intended delivery path. It references the ported managed WPF bundle through `ProGpuWpfManagedPackageId`/`ProGpuWpfManagedPackageVersion`, references the ProGPU runtime packages, injects the non-Windows portable activation bootstrap, and copies resolved managed and native runtime assets to the application output. Local-artifact mode remains available for source-tree validation by setting `ProGpuWpfManagedReferenceRoot` and `ProGpuReferenceRoot`.

For mutable development package versions such as `0.1.0-preview.42`, the SDK clears known WPF and ProGPU runtime assemblies from the app output before recopying package assets. This prevents an incremental app rebuild from launching stale bridge/compositor DLLs after a local package refresh while preserving normal incremental copy behavior for stable package versions. Set `ProGpuWpfClearMutablePackageOutputs=false` to disable this development safeguard.

The SDK owns the package dependency closure. `LibreWPF.Transport` supplies the real managed WPF assembly identities and runtime payload, while `LibreWPF.ProGPU` is the adapter/runtime bridge package and does not publish dependencies on the ProGPU shim `PresentationCore` package.

Existing WPF application projects should keep their normal WPF project shape and switch only the project SDK, whether the original project used `Microsoft.NET.Sdk.WindowsDesktop` or the newer `Microsoft.NET.Sdk` plus `UseWPF=true`. The SDK treats `UseWPF=true` as the app's markup intent, keeps the normal `net*-windows` target-framework shape, and internally redirects framework references to the portable WPF transport and ProGPU/Silk.NET package graph.

Windows, macOS, and Linux are supported runtime targets. A Windows RID restores the same platform-independent `LibreWPF.Transport` payload as the other hosts; no `runtime.win-*` LibreWPF companion package is required or published.

The SDK also supplies the WPF markup compiler defaults and portable runtime-framework default needed by the current build lane, so applications do not need ProGPU-specific item includes, PresentationBuildTasks compatibility properties, or runtime-version pins.

```xml
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.42">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

The current repo MVP validation is intentionally apphost-based, because that is how users run a built SDK-switched WPF application. From the repository root:

```bash
./eng/run-progpu-wpf-hello.sh
./eng/run-progpu-wpf-mvp.sh
```

For a fast validation pass that exercises the external no-source-change SDK smoke, the SDK-switch smoke apphost live geometry probe, Hello and MVP `Application.Run` apphost self-tests, and both Hello/MVP live ProGPU/Silk.NET apphost geometry probes:

```bash
./eng/progpu-wpf-mvp-quickcheck.sh
```

The quickcheck expects the local `0.1.0-preview.42` LibreWPF package feed and its ProGPU `0.1.0-preview.53` runtime dependencies to be current. Use the full SDK CI gate when package contents need to be rebuilt from source:

```bash
./eng/progpu-wpf-sdk-ci.sh
```
