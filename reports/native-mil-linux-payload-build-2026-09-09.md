# Linux native MIL payload production — 2026-09-09

## Acceptance dependency

The package-mode LibreWPF Showcase cannot consume a complete fresh native package until
all required platform payloads exist. This batch addresses the Linux ARM64/x64
build inputs without touching the suspended Parallels guests or running renderer
qualification. It does not close Windows SDK admission or application startup.

## Isolated build environment

Read-only inventory found Colima 0.10.1 and Lima installed, with no instances and
no live Docker daemon. A dedicated profile was created:

```sh
colima start progpu-native-build --template=false --vm-type=vz --arch=aarch64 \
  --cpus=4 --memory=4 --root-disk=20 --disk=12 --runtime=docker --mount=none \
  --activate=false --ssh-config=false --ssh-agent=false --binfmt=false \
  --port-forwarder=none
```

The profile has 4 CPUs, 4 GiB RAM, a 20 GiB root disk and 12 GiB data disk. The
existing Docker default context remained active. Commands explicitly select
`--context colima-progpu-native-build`; containers are non-privileged with no
host mounts, forwarded SSH agent, exposed application ports or GPU devices.
Parallels VM state, configuration and external storage were not changed or retried.
Startup printed missing-current-directory warnings because the host checkout is
deliberately not mounted; the VM and Docker runtime nevertheless started normally.

The official build image is `mcr.microsoft.com/dotnet/sdk:10.0.201-noble`, resolved
to `sha256:127d7d4d601ae26b8e04c54efb37e9ce8766931bded0ee59fcd799afd21d6850`.
Inside the ARM64 container, Ubuntu packages supply `clang`, `clang-tools`, `cmake`,
`ninja-build`, `python3`, `git` and `g++-x86-64-linux-gnu`. Actual versions printed
by the build: .NET 10.0.201, Clang 18.1.3, CMake 3.28.3 and Ninja 1.11.1. Each
container is limited to 4 CPUs/3500 MiB; native compilation uses three jobs.

## Source and dependency provenance

The clean native checkout at `artifacts/native-core-build.KvxVug/progpu` supplied
a Git archive of ProGPU `979f72ca727b5bdd537b80d78c76df02f04783ae`. The archive
contains the root build/restore configuration, `eng`, native and backend sources,
shared shader inputs, normalization data and font assets used by the native graph.
It excludes unrelated dirty native edits and performance artifact deletions in
the main ProGPU worktree. It was copied through Docker's archive input, not a
host home/workspace mount. Only the existing native dependency project is restored.

Pinned inputs remain wgpu-native `33133da4ec5a0174cb21539ef2d3346f75200411`, its
WebGPU headers `aef5e428a1fdab2ea770581ae7c95d8779984e0a`, Dawn headers
`01addc4ba8a2915a061b7095a6768b512071ab96` and Silk.NET's native package 2.23.0.
No renderer algorithm, dependency version, scalar/GPU fallback or ABI changed.
The fetched ProGPU main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4` and is
included in the feature branch.

## ARM64 production

Container `progpu-native-linux-arm64` ran:

```sh
./eng/build-progpu-native.sh --build-only
```

Restore, pinned dependency preparation, CMake configuration and all 709 fresh
Ninja build steps completed with exit 0. C++20 module scanning/compilation was
enabled by the available Clang toolchain. Both renderer libraries, all six native
SDK archives, native tests and samples compiled. No resulting executable ran.

The staged `linux-arm64` directory was copied to:

`artifacts/native-core-build.KvxVug/progpu/artifacts/progpu-native/package/runtimes/linux-arm64/native`

It contains `libprogpu_native.so`, `libprogpu_native_dawn.so` and the compression,
hit-testing, image, MIL, scene-builder and text SDK archives.

## Explicit x64 production support

ProGPU `870148298c5825ecf35919cd89b15a4e532ac124` adds
`--build-only --rid linux-x64|linux-arm64` to the existing Unix script. Explicit
targets require Linux/Clang and real target GNU toolchain dependencies, select a
target-specific CMake directory, and keep compiler triple/processor, wgpu linker
input and package RID aligned. No uname emulation, compiler link-probe bypass or
target executable emulator is used. Normal CI/release invocations remain unchanged.

The completed ARM64 container was preserved as the local toolchain image
`progpu-native-build-toolchain:979f72ca`. Container `progpu-native-linux-x64` uses
that image and the committed updated script, with unchanged native/compiler inputs:

```sh
./eng/build-progpu-native.sh --build-only --rid linux-x64
```

This is a real x86_64-linux-gnu cross-build from ARM64, not an ARM64 binary relabel.
Its source provenance is the 979f72ca native archive plus the 87014829 build script;
it is not represented as a full checkout of a later delivery commit.

All 709 fresh x64 Ninja steps completed with exit 0, including module targets,
both providers, all six SDK archives and native test/sample executables. The
staged directory was copied beside ARM64 under
`artifacts/native-core-build.KvxVug/progpu/artifacts/progpu-native/package/runtimes/linux-x64/native`.
Build-output sizes are 3,837,272 bytes for libprogpu_native.so and 3,876,064 bytes
for libprogpu_native_dawn.so; corresponding ARM64 files are 3,687,736 and
3,686,144 bytes. No target executable, shader, test or benchmark ran.

The added source-contract fixture and complete ProGPU.Tests graph compiled using
the WPF workspace SDK with 0 warnings and 0 errors:

```sh
./.dotnet/dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj \
  --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Fixture execution, negative command-line cases and runtime behavior are deferred
to final qualification. This build-orchestration change applies to C++ payload
production only; managed portable rendering and shared render algorithms are
unchanged. Source provenance is the existing ProGPU build script, not foreign
implementation code.

## Qualification limits

These are unqualified build inputs, not installable LibreWPF packages, successful
Linux application evidence, SIMD output evidence or GPU performance measurements.
No tests, verifier scripts, applications, export/ABI audits, benchmarks or CI
polling ran. Windows native/managed payload production, remaining core platform
connections, complete packages and exact-head final qualification remain open.
Build-only support does not relax any mandatory package or release gate.

After both containers exited successfully and no container remained running, the
dedicated Colima profile was stopped gracefully. Its toolchain image, stopped
containers, source/dependency cache and build directories remain available for
reuse; no image, container or VM data was deleted. Use the named profile/context
explicitly when resuming future build work. The host staging directories also
remain available independently of the stopped VM. The default Docker context is
unchanged, and no Parallels guest was touched in this batch.
