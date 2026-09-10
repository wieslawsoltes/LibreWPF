# LibreWPF Preview Release Workflow

The LibreWPF preview release uses the package list in `eng/progpu-preview-package-list.sh`.
That package set is what users need to consume the custom `LibreWPF.Sdk` and run normal WPF
projects on the ProGPU/Silk.NET platform.

## NuGet Packages

- `LibreWPF.Transport`
- `ProGPU.Backend`
- `ProGPU.Backend.Native`
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
PROGPU_WPF_DEV_PACKAGE_VERSION=0.1.0-preview.45 PROGPU_WPF_PROGPU_PACKAGE_VERSION=0.1.0-preview.55 ./eng/progpu-wpf-sdk-ci.sh
```

The SDK CI script stages ProGPU runtime packages, builds the managed WPF transport assemblies,
`LibreWPF.ProGPU`, and `LibreWPF.Sdk`, then audits the packages, writes the preview manifest,
creates a release bundle, verifies the bundle, and runs package-mode SDK smoke tests. Development
builds can pack ProGPU from the checked-out submodule. The release workflow instead downloads the
exact ProGPU release packages for the matching `v<version>` tag and verifies that tag points at the
checked-out ProGPU submodule commit.

### Package production before qualification

During implementation, package production can stop before running applications or
qualification scripts:

```bash
PROGPU_WPF_SERIAL_BUILD=1 ./eng/progpu-wpf-sdk-ci.sh --build-packages-only
./.dotnet/dotnet build samples/ProGPU.Wpf.ShowcaseApp/ProGPU.Wpf.ShowcaseApp.csproj -m:1 -p:ProGpuWpfRendererMode=NativeMilWgpu
```

This uses the same package builders, managed transport/theme graph and harness
compilation as the full gate. It does not execute the protocol verifier, Avalonia
consumer smoke, WPF harnesses, native host, package audits, release verifiers,
applications or tests. It produces no release manifest or release bundle, and a
successful exit means **package production only**, not qualified application
startup or renderer parity. The flag is command-line-only; the no-argument CI and
release workflows retain all existing gates, including Toolkit/AvalonDock, the
license-controlled Xceed lane and SciChart.

All normal pack-time payload requirements remain mandatory: stage the ProGPU
native runtimes/adapters for every required RID and the source-built Windows
managed/native payloads before packing. Missing payloads still fail packaging;
do not set runtime-validation bypass properties or substitute old artifacts to
claim exact-head packages. Prebuilt ProGPU packages can use the existing
`PROGPU_WPF_PREPACKAGED_PROGPU_DIR` input, subject to final provenance qualification.

Native payload preparation has its own explicit build-only modes:
`external/ProGPU/eng/build-progpu-native.sh --build-only` on a matching macOS/Linux
host, and `eng/build-progpu-native-windows.ps1 -Rid <win-x64|win-arm64> -BuildOnly`
inside a Windows ProGPU checkout. These compile both providers and the required
SDK payloads without running the native qualification scripts or executables.
On Linux, `--build-only --rid linux-x64` or `--build-only --rid linux-arm64`
selects an explicit target using Clang plus its target GNU toolchain, with a
separate default build directory and target-matched wgpu input/staging label.
Cross-compilation is not target runtime qualification and requires no emulator.
They do not produce Windows managed transport/IJW payloads or qualify the staged
files. Use ProGPU's required SDK when running from its checkout, and retain the
full package gate after freeze. See the
[native payload build contract](../external/ProGPU/docs/native-mil-build-only-payloads.md).

Produce Windows managed/IJW inputs in a Windows checkout using PowerShell 7:

```powershell
./eng/progpu-wpf-windows-managed-runtime.ps1
```

This restores/builds PresentationCore and DirectWriteForwarder for win-x86,
win-x64 and win-arm64 and stages their matching IJW hosts. It uses the repository's
pinned SDK, installing it locally when missing. SDK installation clears Arcade's
runtime-only default rather than passing the unsupported `-Runtime sdk` option.
The SDK build host is x64 even on ARM64 Windows, matching Arcade's pinned x64
runtime restoration into that root; all three output architectures remain.
An incompatible existing repository SDK host is rejected before installation.
Use a clean checkout or preserve/move that generated SDK directory explicitly;
do not overlay a different host architecture. Visual Studio MSBuild must meet
the minimum recorded in the selected SDK's bundled MSBuild information.
The current pinned SDK requires MSBuild 18.6 or newer; the source C++ props require
Visual Studio 2026's v145 toolset and Windows SDK 10.0.26100.0. Install C++/CLI
support and x86/x64/ARM64 compiler targets as well as the managed build tools.
The dotnet engine alone cannot build DirectWriteForwarder's Visual Studio C++
project; compiling PresentationBuildTasks does not satisfy that prerequisite.
Each Arcade restore/build runs in a child of the current PowerShell installation,
without requesting an execution-policy override. The existing host policy must
permit the trusted scripts; script rejection remains an explicit failure.
No user/machine policy change or legacy wrapper fallback is performed. This is
package input production, not Windows native SDK admission or application testing.

The script rebuilds its configured local package output and transport staging
directory just as the full gate does. Use a dedicated checkout/feed for isolated
development; packages from a dirty checkout are not exact-commit release evidence.
Do not run the Showcase launcher to obtain build-only behavior: it launches the app and
its automatic package rebuild uses the full SDK gate. After feature freeze, run
the normal no-argument gate on the delivery commits and record its full results.

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
<Project Sdk="LibreWPF.Sdk/0.1.0-preview.45">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

No application source or XAML changes should be required for normal WPF code. Windows-specific interop,
unsupported DirectX features, and native-hosting edge cases remain tracked in `reports/`.
