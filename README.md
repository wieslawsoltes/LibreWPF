# LibreWPF ProGPU Port

[![Telegram Community](https://img.shields.io/badge/Telegram-Community-26A5E4?logo=telegram&logoColor=white)](https://t.me/+HblJUymBc544ODY0)

This branch ports WPF onto the ProGPU/Silk.NET platform while reusing as much managed WPF code as possible. The public package brand is LibreWPF, with the custom SDK package `LibreWPF.Sdk`, so an existing WPF app can switch the project SDK and keep normal WPF source and XAML unchanged.

Current focus areas:

- Reuse WPF managed code for application model, dependency properties, layout, controls, data binding, documents, XAML, resources, themes, and the XAML compiler.
- Replace Windows-only MIL/D3D rendering with ProGPU WebGPU composition, shaders, DirectX-compatible shims, GPU hit testing, and Silk.NET windowing/input.
- Package the runtime as a preview SDK and NuGet set that can be consumed from a local feed or NuGet.org.
- Keep third-party validation active through basic WPF apps, Xceed Toolkit/AvalonDock, Xceed paid Toolkit/DataGrid, SciChart MVP, ProGPU Avalonia package smoke, and no-source-change SDK smoke tests.

The maintained cross-platform priorities, compatibility policy, and ecosystem
status are tracked in the [LibreWPF cross-platform roadmap](roadmap.md). The
historical upstream Microsoft WPF roadmap remains below that LibreWPF section.

### Linux windowing behavior

LibreWPF selects X11 when it runs in a Wayland desktop session that also exposes
an XWayland `DISPLAY`. This preserves WPF desktop-coordinate behavior for
`Window.Left`/`Top`, `Window.DragMove`, docking windows, and native popup
placement. Set `PROGPU_WPF_LINUX_WINDOWING=wayland` to opt into native Wayland;
native Wayland intentionally uses owner-composited popups because GLFW cannot
position popup toplevels.

Libraries that need to choose a docking or floating-window strategy can call
`ProGpuWpfDiagnostics.TryGetWindowingCapabilities(...)`. The returned typed
snapshot reports the actual backend and whether global positioning, interactive
move, native popup windows, and owner-composited popups are available. Native
Wayland reports global positioning and interactive movement as unsupported
instead of silently pretending that an ignored absolute move succeeded.

## Upstream WPF Synchronization

The current LibreWPF development line includes `dotnet/wpf` through upstream commit
[`1131ae499da9687fcd7c5b25cea7ac37f5885c61`](https://github.com/dotnet/wpf/commit/1131ae499da9687fcd7c5b25cea7ac37f5885c61)
(`Source code updates from dotnet/dotnet (#11770)`, 2026-07-10). This pinned commit is the
auditable upstream baseline for the next preview; later upstream commits are not implied.

Upstream updates are integrated periodically on `progpu-rendering-port` as explicit merge/sync
commits. Each update must preserve the portable typed contracts, pass the full LibreWPF SDK and
package-mode application gates, and update the baseline recorded here. Published preview tags remain
immutable, so a consumer can combine the LibreWPF tag commit with this recorded upstream baseline to
identify the exact managed WPF source lineage.

## Getting Started: Switch From WPF To LibreWPF

LibreWPF is packaged as an MSBuild SDK so normal WPF apps can move to the ProGPU/Silk.NET platform through the project file first. Keep your application code, XAML, resources, and existing package references unchanged unless the app uses Windows-only interop or unsupported native graphics APIs.

1. Start from an existing SDK-style WPF project and keep a clean commit of the working WPF version.

2. Make sure the project targets the supported preview TFM:

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
```

`LibreWPF.Sdk` sets `EnableWindowsTargeting` when needed, so cross-platform builds can still use the Windows-shaped WPF API surface while running through the portable ProGPU host.

Windows is a supported LibreWPF runtime target alongside macOS and Linux. The `net10.0-windows` target framework preserves the WPF API contract; it does not select the Windows-only MIL renderer. Windows RIDs consume the same portable `LibreWPF.Transport` package and ProGPU/Silk.NET runtime graph, without RID-split `runtime.win-*` LibreWPF companion packages.

3. Change only the project SDK.

Before:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

After:

```xml
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.42">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Older projects that still use `Microsoft.NET.Sdk.WindowsDesktop` should make the same SDK change and keep the existing WPF properties.

4. Keep existing app dependencies in place. For example, a Toolkit app only changes the SDK line:

```xml
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.42">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Extended.Wpf.Toolkit" Version="5.1.2" />
  </ItemGroup>
</Project>
```

5. Restore and run the app normally:

```bash
dotnet restore
dotnet run
```

6. Treat Windows-only interop, direct Win32 calls, `D3DImage`, raw DirectX use, custom HWND hosting, and unsupported native graphics APIs as the first compatibility review points. Normal WPF managed code, XAML, bindings, controls, resources, and themes should remain source-compatible as the port fills out.

## NuGet Packages

The preview package set is defined in `eng/progpu-preview-package-list.sh` and validated by the release workflow.
Tag releases promote and re-verify the exact package artifact produced by the full `LibreWPF Build`
gate for the tagged commit, then repeat the clean Windows AnyCPU package smoke before publication.
This avoids compiling the same WPF graph twice without removing any qualification step; manual
release dispatch remains the full-rebuild recovery path.

### LibreWPF Packages

| Package | NuGet | Purpose |
| --- | --- | --- |
| `LibreWPF.Sdk` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Sdk.svg)](https://www.nuget.org/packages/LibreWPF.Sdk) | Custom MSBuild SDK that redirects WPF apps to the ProGPU/Silk.NET platform. |
| `LibreWPF.ProGPU` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.ProGPU.svg)](https://www.nuget.org/packages/LibreWPF.ProGPU) | WPF-to-ProGPU host, retained/source replay bridge, Silk.NET input/windowing, and compositor adapter. |
| `LibreWPF.Transport` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Transport.svg)](https://www.nuget.org/packages/LibreWPF.Transport) | Ported managed WPF transport assemblies, refs, themes, XAML build tasks, and runtime metadata. |
| `LibreWPF.Interop` | [![NuGet](https://img.shields.io/nuget/vpre/LibreWPF.Interop.svg)](https://www.nuget.org/packages/LibreWPF.Interop) | Shared WPF interop contracts consumed by the WPF bridge and ProGPU runtime. |

### ProGPU Packages

| Package | NuGet | Purpose |
| --- | --- | --- |
| `ProGPU.Backend` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Backend.svg)](https://www.nuget.org/packages/ProGPU.Backend) | WebGPU device, swapchain, Silk.NET windowing, and platform backend services. |
| `ProGPU.Backend.Dawn` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Backend.Dawn.svg)](https://www.nuget.org/packages/ProGPU.Backend.Dawn) | Dawn native backend assets used by package-mode presentation hosts. |
| `ProGPU.Text.Shaping` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Text.Shaping.svg)](https://www.nuget.org/packages/ProGPU.Text.Shaping) | AOT-safe OpenType shaping contracts and execution used by the text renderer. |
| `ProGPU.DirectX` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.DirectX.svg)](https://www.nuget.org/packages/ProGPU.DirectX) | DirectX-compatible facade for SciChart and future D3D-style interop on ProGPU/WebGPU. |
| `ProGPU.Transpiler` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Transpiler.svg)](https://www.nuget.org/packages/ProGPU.Transpiler) | Shader/source transformation helpers used by generated GPU pipelines. |
| `ProGPU.Compute` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Compute.svg)](https://www.nuget.org/packages/ProGPU.Compute) | Compute pipeline helpers for GPU effects, indexes, and acceleration structures. |
| `ProGPU.Vector` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Vector.svg)](https://www.nuget.org/packages/ProGPU.Vector) | Vector paths, geometry, brushes, pens, and rasterization data models. |
| `ProGPU.Text` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Text.svg)](https://www.nuget.org/packages/ProGPU.Text) | Text layout, glyph metrics, and GPU-ready text rendering helpers. |
| `ProGPU.Scene` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Scene.svg)](https://www.nuget.org/packages/ProGPU.Scene) | Scene graph, compositor commands, retained visuals, effects, and presentation primitives. |
| `ProGPU.Layout` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Layout.svg)](https://www.nuget.org/packages/ProGPU.Layout) | Measure/arrange layout substrate shared by ProGPU UI adapters. |
| `ProGPU.Virtualization` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Virtualization.svg)](https://www.nuget.org/packages/ProGPU.Virtualization) | Virtualization helpers for large retained visual and item surfaces. |
| `ProGPU.WinRT` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.WinRT.svg)](https://www.nuget.org/packages/ProGPU.WinRT) | Typed WinRT-compatible value and geometry contracts shared by ProGPU surfaces. |
| `ProGPU.Media` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Media.svg)](https://www.nuget.org/packages/ProGPU.Media) | Provider-neutral media state, timing, track, cue, and presentation contracts. |
| `ProGPU.Media.Scene` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Media.Scene.svg)](https://www.nuget.org/packages/ProGPU.Media.Scene) | Typed media-to-scene composition and GPU surface presentation. |
| `ProGPU.WinUI` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.WinUI.svg)](https://www.nuget.org/packages/ProGPU.WinUI) | WinUI-shaped controls and app model implemented on ProGPU. |
| `ProGPU.Avalonia` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.Avalonia.svg)](https://www.nuget.org/packages/ProGPU.Avalonia) | Avalonia integration and compositor backend adapter used by package smoke validation. |
| `ProGPU.SkiaSharp` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.SkiaSharp.svg)](https://www.nuget.org/packages/ProGPU.SkiaSharp) | ProGPU-backed portable SkiaSharp compatibility shim used by drawing and imaging adapters. |
| `ProGPU.System.Drawing.Common` | [![NuGet](https://img.shields.io/nuget/vpre/ProGPU.System.Drawing.Common.svg)](https://www.nuget.org/packages/ProGPU.System.Drawing.Common) | ProGPU-backed portable System.Drawing.Common compatibility shim for LibreWinForms and GDI-style callers. |

## Build And Release

```bash
PROGPU_WPF_DEV_PACKAGE_VERSION=0.1.0-preview.42 PROGPU_WPF_PROGPU_PACKAGE_VERSION=0.1.0-preview.52 ./eng/progpu-wpf-sdk-ci.sh
```

The SDK CI script stages ProGPU runtime packages, builds managed WPF transport assemblies, `LibreWPF.ProGPU`, and `LibreWPF.Sdk`, then audits the packages, writes the preview manifest, creates and verifies the release bundle, and runs package-mode SDK smoke tests. Public releases consume the hash-identical packages from the matching ProGPU GitHub release instead of repacking or republishing them.

For a faster source-development loop, use the same qualified managed, theme,
and harness project sets through the validation graph:

```bash
for target in \
  RestoreManagedTransport BuildManagedTransport \
  RestoreThemes BuildThemes \
  RestoreHarnesses BuildHarnesses
do
  ./.dotnet/dotnet msbuild eng/ProGPU.Wpf.ValidationGraphs.proj \
    -target:${target} -property:Configuration=Debug -verbosity:minimal
done
```

This preserves the normal compilers, analyzers, project ordering, all seven
themes, Ribbon, and all four real-WPF harness builds. Restore and build remain
separate processes so generated package properties are reevaluated correctly;
independent theme and harness leaves build in parallel. On the current Apple
arm64 development host, a warm 13-project sequence fell from about 45.3 seconds
to 9.5 seconds. This is the inner loop only: the complete
`eng/progpu-wpf-sdk-ci.sh` package, provenance, third-party, runtime, and live
GPU gates remain required before a release.

GitHub workflows:

- `LibreWPF Build` runs the SDK package/no-source-change smoke on macOS.
- `LibreWPF Docs` verifies README and release docs against the preview package list.
- `LibreWPF Release` builds preview packages/bundle artifacts and can publish to NuGet.org with `NUGET_API_KEY`.

See [docs/progpu-wpf-release.md](docs/progpu-wpf-release.md) and the ongoing porting reports in [reports/](reports/).

## Original Upstream README

# Windows Presentation Foundation (WPF)
[![.NET Foundation](https://img.shields.io/badge/.NET%20Foundation-blueviolet.svg)](https://www.dotnetfoundation.org/)
[![Build Status](https://dnceng.visualstudio.com/public/_apis/build/status/dotnet/wpf/dotnet-wpf%20CI)](https://dnceng.visualstudio.com/public/_build/latest?definitionId=270)
[![codecov](https://codecov.io/gh/dotnet/wpf/branch/main/graph/badge.svg?flag=production)](https://codecov.io/gh/dotnet/wpf)
[![MIT License](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)

Windows Presentation Foundation (WPF) is a UI framework for building Windows desktop applications. 

WPF supports a broad set of application development features, including an application model, resources, controls, graphics, layout, data binding and documents. WPF uses the Extensible Application Markup Language (XAML) to provide a declarative model for application programming.

WPF's rendering is vector-based, which enables applications to look great on high DPI monitors, as they can be infinitely scaled. WPF also includes a flexible hosting model, which makes it straightforward to host a video in a button, for example.

Visual Studio's designer, as well as Visual Studio Blend, make it easy to build WPF applications, with drag-and-drop and/or direct editing of XAML markup.

As of .NET 6.0, WPF supports ARM64. 

See the [WPF Roadmap](roadmap.md) to learn about project priorities, status and ship dates.

[WinForms](https://github.com/dotnet/winforms) is another UI framework for building Windows desktop applications that is supported on .NET (7.0.x/6.0.x). WPF and WinForms applications only run on Windows. They are part of the `Microsoft.NET.Sdk.WindowsDesktop` SDK. You are recommended to use the most recent version of [Visual Studio](https://visualstudio.microsoft.com/downloads/) to develop WPF and WinForms applications for .NET.  

To build the WPF repo and contribute features and fixes for .NET 8.0, [Visual Studio 2022 Preview](https://visualstudio.microsoft.com/vs/preview/) is required.

## Getting started

* [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0), [.NET 7.0 SDK](https://dotnet.microsoft.com/download/dotnet/7.0), [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
* [.NET Preview SDKs](https://github.com/dotnet/dotnet/blob/main/docs/builds-table.md)
* [Getting started instructions](Documentation/getting-started.md)
* [Contributing guide](Documentation/contributing.md)
* [Migrating .NET Framework WPF Apps to .NET Core](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/migration/)

## Status

- We are currently developing WPF for .NET 10. 

See the [WPF roadmap](roadmap.md) to learn about the schedule for specific WPF components.

Test published at [separate repo](https://github.com/dotnet/wpf-test) Tests and have limited coverage at this time. We will add more tests, however, it will be a progressive process.

The Visual Studio WPF designer is now available as part of Visual Studio 2019. 

## How to Engage, Contribute and Provide Feedback

Some of the best ways to contribute are to try things out, file bugs, join in design conversations, and fix issues.

* This repo defines [contributing guidelines](Documentation/contributing.md) and also follows the more general [.NET Core contributing guide](https://github.com/dotnet/runtime/blob/main/CONTRIBUTING.md).
* If you have a question or have found a bug, [file an issue](https://github.com/dotnet/wpf/issues/new).
* Use [daily builds](Documentation/getting-started.md#installation) if you want to contribute and stay up to date with the team.

### .NET Framework issues

Issues with .NET Framework, including WPF, should be filed on [VS developer community](https://developercommunity.visualstudio.com/spaces/61/index.html), 
or [Product Support](https://support.microsoft.com/en-us/contactus?ws=support).
They should not be filed on this repo.

## Relationship to .NET Framework

This code base is a fork of the WPF code in the .NET Framework. .NET Core 3.0 was released with a goal of WPF having parity with the .NET Framework version. Over time, the two implementations may diverge.

The [Update on .NET Core 3.0 and .NET Framework 4.8](https://devblogs.microsoft.com/dotnet/update-on-net-core-3-0-and-net-framework-4-8/) provides a good description of the forward-looking differences between .NET Core and .NET Framework.

This [update](https://devblogs.microsoft.com/dotnet/net-core-is-the-future-of-net/) states how going forward .NET Core is the future of .NET. and .NET Framework 4.8 will be the last major version of .NET Framework.


## Code of Conduct

This project uses the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct) to define expected conduct in our community. Instances of abusive, harassing, or otherwise unacceptable behavior may be reported by contacting a project maintainer at conduct@dotnetfoundation.org.

## Reporting security issues and security bugs

Security issues and bugs should be reported privately, via email, to the Microsoft Security Response Center (MSRC) <secure@microsoft.com>. You should receive a response within 24 hours. If for some reason you do not, please follow up via email to ensure we received your original message. Further information, including the MSRC PGP key, can be found in the [Security TechCenter](https://www.microsoft.com/msrc/faqs-report-an-issue).

Also see info about related [Microsoft .NET Core and ASP.NET Core Bug Bounty Program](https://www.microsoft.com/msrc/bounty-dot-net-core).

## License

.NET Core (including the WPF repo) is licensed under the [MIT license](LICENSE.TXT).

## .NET Foundation

.NET Core WPF is a [.NET Foundation](https://www.dotnetfoundation.org/projects) project.

See the [.NET home repo](https://github.com/Microsoft/dotnet) to find other .NET-related projects.
