# Native MIL Linux package input refresh — 2026-09-09

## Latest refresh and native package production: da36a718

Both Linux targets now use `da36a7188bb887b3935ac7929b03534b333658ef`, matching
the macOS and Windows native payloads. The clean isolated ProGPU checkout supplied
`artifacts/native-core-build.KvxVug/linux-native-source-da36a718.tar` using the
same committed path set described below. Its SHA-256 is
`19293f7e0ecc0214b16670931266ef13394880d68bd94f42552726623deb55df`.
There are no removed/renamed paths in this source delta, and unrelated working-tree
changes are excluded.

The stopped successful 2ab498be containers were preserved as local images
`progpu-native-build-cache:arm64-2ab498be` and
`progpu-native-build-cache:x64-2ab498be`. New containers
`progpu-native-linux-arm64-da36a718` and `progpu-native-linux-x64-da36a718` received
the archive without host mounts, privileged mode or GPU devices. They retained
the prior compiler/dependency caches, three build jobs, 4 CPU and 3500 MiB limits.
The source archive refreshed timestamps, so each compiled all 709 steps with
C++20 modules enabled. ARM64 exited 0 after approximately 80 seconds and x64
exited 0 after approximately 82 seconds. Native tests/samples compiled, not ran.

Both complete RID directories were copied into the host package staging root.
Fresh inventory confirmed successful terminal containers and no running container;
Colima then stopped gracefully. All old/new containers and caches are preserved,
and the Docker context remains `default`. No Parallels guest action occurred.

With all six desktop native RID sets at this commit, the clean ProGPU checkout
successfully built the native NuGet package using SDK 10.0.201:

```sh
/Users/wieslawsoltes/.dotnet/dotnet pack src/ProGPU.Backend.Native/ProGPU.Backend.Native.csproj \
  --no-restore -c Release -m:1 \
  -o /Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/packages-da36a718 \
  -p:Version=0.1.0-preview.55 -p:PackageVersion=0.1.0-preview.55 -v:minimal
```

Output: `ProGPU.Backend.Native.0.1.0-preview.55.nupkg`. The version matches the
current LibreWPF SDK lane's explicit ProGPU package version. The normal pack-time
required-RID/provider checks were enabled, with no runtime-validation bypass.
This is one locally produced, unqualified package, not the complete LibreWPF
package set, a public release, package-consumption proof or runtime/ABI parity.
Windows managed/IJW payloads and remaining package/application closure stay open;
tests, verifiers, renderer workloads, benchmarks and CI polling remain deferred.

The sections below preserve the earlier 2ab498be production provenance.

## Acceptance dependency

The package-mode Showcase needs complete platform inputs from the implementation
checkpoint before its native/managed startup can be qualified. The prior Linux
payloads used older native text/scene inputs than the current macOS builds. This
batch refreshes both Linux architectures to ProGPU
`2ab498be3589e151f3ae344139a48667782a2608`; it does not loosen package checks or
admit Windows startup.

## Reused isolated environment

The existing `progpu-native-build` Colima profile resumed successfully with
`--activate=false`. Its 4 CPU / 4 GiB VM and 12 GiB data disk, no SSH-agent
forwarding, disabled emulation and disabled application-port forwarding settings
were retained. The actual Lima configuration has only Colima's standard read-only
cache mount, not a home/workspace mount. Build containers have no mounts or GPU
devices. The default Docker context remains `default`; all build commands select
`--context colima-progpu-native-build` explicitly. Parallels guests were untouched.

The ARM64 build reuses the previously created local toolchain image
`progpu-native-build-toolchain:979f72ca`. The stopped successful x64 build was
preserved in the local image `progpu-native-build-cache:x64-87014829`, retaining
its target-specific CMake/dependency cache. Neither old container was deleted or
restarted with its old build command. New containers are:

- `progpu-native-linux-arm64-2ab498be`
- `progpu-native-linux-x64-2ab498be`

Each has a 4 CPU / 3500 MiB limit and three native compilation jobs. There was no
package-manager upgrade, new compiler installation or new base-image download.
The recorded compiler is Ubuntu Clang 18.1.3, with CMake 3.28.3, Ninja 1.11.1 and
the original .NET SDK 10.0.201 image. C++ module targets remain enabled.

## Source provenance and commands

The clean isolated ProGPU checkout supplied
`artifacts/native-core-build.KvxVug/linux-native-source-2ab498be.tar` through
`git archive`. As in the original Linux production run, it contains root build,
restore and package configuration, `eng`, native/backend sources, shared shader
and text/normalization inputs, and the referenced font projects. It excludes
unrelated dirty source/performance changes in the main ProGPU checkout.

The archived path set has no tracked deletions or renames between the earlier
source and this checkpoint; overlaying the complete committed path set therefore
does not retain removed source files. The archive was copied into the new
containers through Docker's archive input, not by mounting the repository.
Pinned wgpu-native, WebGPU/Dawn headers and Silk.NET native inputs are unchanged.

Within `/work/progpu`, ARM64 runs:

```sh
./eng/build-progpu-native.sh --build-only
```

The x64 container runs the existing real GNU cross-toolchain path:

```sh
./eng/build-progpu-native.sh --build-only --rid linux-x64
```

No target executable or emulator runs. The script restores build dependencies,
configures and compiles both providers, six SDK archives, native test/sample
executables and C++ module consumers. It stops before every runtime/verifier gate.

## Production results

- ARM64 completed all 709 build steps and exited 0.
- x64 completed all 709 build steps and exited 0.
- Both builds retained C++ module scanning/compilation, both provider libraries,
  all six SDK archives and native test/sample compilation. The committed source
  archive refreshed source timestamps; this was a full 709-step rebuild within
  retained build/dependency caches, not a fresh toolchain installation.
- Both completed containers' native RID directories were copied into the host
  staging root beside the macOS payloads:
  `artifacts/native-core-build.KvxVug/progpu/artifacts/progpu-native/package/runtimes`.
  All four Unix desktop architecture sets now use the same ProGPU source commit.
- Freshly fetched ProGPU main remains
  `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of that build commit.

No running container remained after production and copying. The profile stopped
gracefully and a fresh inventory confirmed Stopped; its images, containers,
source archives and caches remain available. The default Docker context is unchanged.

## Remaining requirements

This is native payload production, not a successful application or package-mode
test. Windows renderer and managed/IJW inputs are still missing; complete packages
must retain those requirements. The source startup/interaction/lifetime closure
queue and final architecture, ABI, rendering, GPU/SIMD, performance and exact-head
CI gates remain open. No tests, verifier scripts, applications, GPU workloads,
benchmarks or CI polling execute in this batch.

See the [macOS inputs at the same commit](native-mil-macos-payload-refresh-2026-09-09.md),
[original Linux build setup](native-mil-linux-payload-build-2026-09-09.md), and
[active delivery queue](../docs/native-mil-core-delivery.md).
