# LibreWPF Preview Release Workflow

The LibreWPF preview release uses the package list in `eng/progpu-preview-package-list.sh`.
That package set is what users need to consume the custom `LibreWPF.Sdk` and run normal WPF
projects on the ProGPU/Silk.NET platform.

## NuGet Packages

- `LibreWPF.Transport`
- `ProGPU.Backend`
- `ProGPU.Backend.Dawn`
- `ProGPU.Text.Shaping`
- `ProGPU.DirectX`
- `ProGPU.Transpiler`
- `ProGPU.Compute`
- `ProGPU.Vector`
- `ProGPU.Text`
- `ProGPU.Scene`
- `ProGPU.Layout`
- `ProGPU.Virtualization`
- `ProGPU.WinRT`
- `ProGPU.Media`
- `ProGPU.Media.Scene`
- `ProGPU.WinUI`
- `ProGPU.Avalonia`
- `ProGPU.SkiaSharp`
- `ProGPU.System.Drawing.Common`
- `LibreWPF.Interop`
- `LibreWPF.ProGPU`
- `LibreWPF.Sdk`

## Local Preview Build

```bash
PROGPU_WPF_DEV_PACKAGE_VERSION=0.1.0-preview.44 PROGPU_WPF_PROGPU_PACKAGE_VERSION=0.1.0-preview.54 ./eng/progpu-wpf-sdk-ci.sh
```

The SDK CI script stages ProGPU runtime packages, builds the managed WPF transport assemblies,
`LibreWPF.ProGPU`, and `LibreWPF.Sdk`, then audits the packages, writes the preview manifest,
creates a release bundle, verifies the bundle, and runs package-mode SDK smoke tests. Development
builds can pack ProGPU from the checked-out submodule. The release workflow instead downloads the
exact ProGPU release packages for the matching `v<version>` tag and verifies that tag points at the
checked-out ProGPU submodule commit.

## GitHub Actions

- `LibreWPF Build` runs the SDK package/no-source-change smoke on macOS with submodules checked out.
- `LibreWPF Docs` verifies that this document and README stay aligned with the preview package list.
- `LibreWPF Release` promotes the package bundle from a terminal-success `LibreWPF Build` run for the exact tagged commit, re-verifies its source/package provenance, runs the clean Windows AnyCPU package smoke, publishes to NuGet.org, and creates tag-driven GitHub Releases with generated release notes. It fails closed when the exact commit has no live qualified artifact.
- Manual `LibreWPF Release` dispatch remains the recovery path that rebuilds the full SDK gate for an explicitly selected immutable ref.

## NuGet Publishing

Publishing is gated by repository secret `NUGET_API_KEY`.

- Manual workflow runs publish only when the `publish` input is true.
- Tags named `librewpf-v*` publish after validation.
- ProGPU and `LibreWPF.Interop` are published first by the ProGPU release. LibreWPF then publishes only `LibreWPF.Transport`, `LibreWPF.ProGPU`, and `LibreWPF.Sdk`; the offline bundle carries the hash-identical ProGPU release packages without republishing them.
- Tag runs create the matching GitHub Release with `gh release create --generate-notes` and attach the preview packages, manifest, bundle, checksum, README, and NuGet.config.

## SDK Switch Contract

Existing WPF applications should be able to switch only the project SDK:

```xml
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.44">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

No application source or XAML changes should be required for normal WPF code. Windows-specific interop,
unsupported DirectX features, and native-hosting edge cases remain tracked in `reports/`.
