# Native MIL SDK package prerequisites — 2026-09-09

## Acceptance path

The existing package-mode LibreWPF MVP needs the exact ProGPU dependency set used
by `eng/progpu-wpf-sdk-ci.sh`, then source-built WPF transport, themes and bridge
packages. This batch produces those ProGPU package inputs and compiles the WPF
build graph without running the deferred application/qualification phase.

## ProGPU package production

The isolated clean checkout at
`artifacts/native-core-build.KvxVug/progpu` remains at
`da36a7188bb887b3935ac7929b03534b333658ef`. All six desktop native RID inputs use
that implementation checkpoint, as recorded in the Linux/macOS/Windows build
reports. Unrelated dirty ProGPU source and performance artifacts are excluded.

The native package was already produced. The other nineteen projects listed by
the SDK gate were packed serially with SDK 10.0.201, Release, `-m:1`, and
`Version`, `PackageVersion`, `ProGpuRuntimePackageVersion` set to
`0.1.0-preview.55`, matching the existing SDK lane. The loop stopped on any
failure; it completed with exit 0. No full ProGPU pack-script invocation was used:
that entry also executes package/consumer verifiers reserved for final qualification.
Normal project build and pack-time requirements remained enabled.

All twenty `.nupkg` outputs are in
`artifacts/native-core-build.KvxVug/packages-da36a718`:

- ProGPU.Backend, ProGPU.Backend.Native, ProGPU.Backend.Dawn
- ProGPU.Text.Shaping, ProGPU.DirectX, ProGPU.Transpiler, ProGPU.Compute
- ProGPU.Vector, ProGPU.Text, ProGPU.Scene, ProGPU.Layout, ProGPU.Virtualization
- ProGPU.WinRT, ProGPU.Media, ProGPU.Media.Scene, ProGPU.WinUI, ProGPU.Avalonia
- ProGPU.SkiaSharp, ProGPU.System.Drawing.Common, LibreWPF.Interop

This is the ProGPU-side list selected by the existing LibreWPF gate, not every
package in the broader ProGPU catalog. A directory inventory confirms production,
not transitive dependency completeness or isolated-consumer qualification. No
package was uploaded to a public feed or installed into a consumer cache.

## WPF compilation

The host WPF checkout at `873768501` uses its pinned SDK
11.0.100-preview.5.26302.115 to run these existing graph targets serially:

```sh
for build_target in RestoreManagedTransport BuildManagedTransport RestoreThemes BuildThemes RestoreHarnesses BuildHarnesses; do
  ./.dotnet/dotnet msbuild eng/ProGPU.Wpf.ValidationGraphs.proj \
    "-target:$build_target" -property:Configuration=Release \
    -m:1 -p:UseSharedCompilation=false -verbosity:minimal
done
```

The shell uses `set -e`; restore and build are separate invocations, preserving
reevaluation and shared-output serialization. These are the existing SDK build
targets, not harness execution. The generated staging directory is reused, so
this is compilation evidence rather than a clean release-payload audit.

All six restore/build target invocations completed successfully; the serialized
shell exited 0. The graph compiled the thirteen managed transport projects,
eight theme/Ribbon projects and all four real harnesses (XAML compiler, XAML
runtime, Application.Run and themes). The referenced ProGPU.Wpf bridge also
compiled. Existing IDE0305/IDE0051 warnings in linked font source and CS0067 for
the unused display-metrics event were reported; no compiler error occurred.
No harness executable was launched. This graph consumes source/project references,
not the newly packed NuGet files, so it is not package-consumption evidence.

The portable macOS WPF build cannot supply Windows
PresentationCore/DirectWriteForwarder/IJW files. Their
staging directory remains absent; the Windows installer approval prerequisite,
Windows SDK admission and the complete LibreWPF package set remain open.

No tests, renderer workloads, application launches, package/ABI/export verifiers,
benchmarks or CI polling are part of this batch. All final gates remain mandatory.

## Bridge and SDK package production

At WPF source checkpoint `617251a57`, both remaining host-buildable packages were
produced successfully into the same `packages-da36a718` feed:

- `LibreWPF.ProGPU.0.1.0-preview.45.nupkg`
- `LibreWPF.Sdk.0.1.0-preview.45.nupkg`

Each uses the normal `dotnet pack` project entry, Release, `-m:1`,
`UseSharedCompilation=false`, `Version`/`PackageVersion=0.1.0-preview.45` and
`ProGpuRuntimePackageVersion=0.1.0-preview.55`. SDK production rebuilt
PresentationBuildTasks for net10.0 and net472. Both commands exited 0; the bridge
reported its existing unused-event CS0067 warning. No payload/admission guards
were changed.

Restore used a task-local cache under
`artifacts/native-core-build.KvxVug/nuget-package-build.UFN4t0/packages` and an
adjacent temporary NuGet.config, leaving global package caches intact. The first
attempt failed with NU1100 because an explicitly selected configuration does not
inherit the root's feed definitions. The corrected configuration includes the
repository's existing external feeds and audit source, plus local-feed-only
mapping for `ProGPU.*`/`LibreWPF.*`. The successful retry retained these mappings;
no external source was allowed to supply those development package IDs.

The normal bridge graph still selects its project references during this pack
invocation; source/compiler output confirms those projects were built. Source
mapping and a fresh cache therefore do not establish isolated ProGPU package
consumption. Keep the task-local cache because generated restore assets reference
it; a later normal restore may replace those generated assets.

The feed now contains 22 of the 23 packages explicitly selected by the SDK gate.
`LibreWPF.Transport` is still missing: its normal pack requires the separately
built Windows PresentationCore/DirectWriteForwarder/IJW assets. Do not substitute
the portable macOS transport or an older package. No complete release bundle,
dependency-closure audit, SDK application success or renderer parity is claimed.

## Windows inputs and complete selected package production

The Windows compiler-host and rebuild-reference corrections now produced all
three Windows managed/IJW RID inputs with zero warnings/errors in each graph.
The dedicated guest checkout uses the previously recorded source snapshot plus
the package wrapper and project-reference correction from `aa3bc9ad8`; ProGPU
remains da36a718. This is not a claim of a clean exact-delivery-head checkout.
Payloads and final binlogs are exported to
`artifacts/native-core-build.KvxVug/windows-managed-runtime-aa3bc9ad8` with
source/destination payload hash agreement. See the
[Windows managed build record](native-mil-windows-managed-build-entry-2026-09-09.md).

The host then successfully produced
`LibreWPF.Transport.0.1.0-preview.45.nupkg` into the same `packages-da36a718` feed:

```sh
./.dotnet/dotnet pack \
  packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj \
  -c Release -m:1 -p:UseSharedCompilation=false -v:minimal \
  -o /Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/packages-da36a718 \
  -p:Version=0.1.0-preview.45 -p:PackageVersion=0.1.0-preview.45 \
  -p:ProGpuRuntimePackageVersion=0.1.0-preview.55 \
  -p:LibreWpfWindowsManagedPayloadDir=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/windows-managed-runtime-aa3bc9ad8 \
  -p:RestoreConfigFile=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/nuget-package-build.UFN4t0/NuGet.config \
  -p:RestorePackagesPath=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/nuget-package-build.UFN4t0/packages
```

The command exited 0 and reported successful package creation. Existing required
managed/native input and pack-time checks remained enabled. No package contents
were substituted, no Windows SDK admission guard removed and no qualification
command was invoked. Local packing began only after the guest rebuild exited.

All 23 packages selected by the current SDK lane have now been produced. This
closes their development-production blocker, not a clean exact-head release,
package dependency/ABI audit, isolated consumer success, runtime parity or CI
qualification. The feed contains checkpoints built during implementation; final
qualification must regenerate and consume the delivery artifacts. No public
NuGet upload occurred. Next work is the existing package MVP's source-media and
Windows activation closure, then application closure and feature freeze.
