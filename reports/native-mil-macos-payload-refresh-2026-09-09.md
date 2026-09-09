# Native MIL macOS package inputs — 2026-09-09

## Acceptance dependency and implementation

The existing package-mode MVP must launch with an explicitly selected native
renderer. Complete package production still requires Windows native and managed
payloads: ProGPU.Backend.Native checks all supported renderer RIDs and the WPF
transport requires source-built Windows PresentationCore/DirectWriteForwarder/IJW
assets. The Windows managed staging directory is absent. No package requirement
was bypassed and no incomplete package was labeled deliverable.

The available macOS native inputs were at different checkpoints and the Unix
build-only script only accepted explicit Linux RIDs. ProGPU `2ab498be` extends
that existing original target-selection path to osx-arm64/osx-x64. It pairs the
Apple compiler architecture with the matching pinned wgpu dylib and staging RID,
keeps default build and mutable runtime directories target-specific, and rejects
host/target OS mismatches before restore. The no-switch CI/release path retains
all qualification gates. No foreign engine implementation, new renderer, shader,
SIMD algorithm or dependency version was introduced.

The existing clean native checkout was advanced from 0502e7de to the committed
ProGPU `2ab498be3589e151f3ae344139a48667782a2608`:

`artifacts/native-core-build.KvxVug/progpu`

Unrelated native semantic-state edits and performance-artifact deletions in the
main ProGPU checkout were preserved, not incorporated into these build inputs.

## Build-only production

Both builds use the installed .NET 10.0.201 for restore, Apple Clang 21.0.0,
CMake 4.1.0 and Ninja 1.13.1. The existing strict C++20 header-compatibility caches
keep C++ modules OFF on this toolchain. Native test/sample targets still compile;
none execute. Both provider libraries and all six SDK archives are required before
staging. Existing caches are reused, not presented as new clean full builds.

From the isolated ProGPU checkout, the ARM64 command is:

```sh
env PATH="/Users/wieslawsoltes/.dotnet:$PATH" \
  PROGPU_NATIVE_WGPU_SOURCE=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/wgpu-native \
  PROGPU_NATIVE_BUILD_DIR=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/build-osx-arm64 \
  PROGPU_NATIVE_INCLUDE_DIR=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/include \
  PROGPU_NATIVE_RUNTIME_DIR=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/runtime \
  PROGPU_NATIVE_DAWN_HEADER_SOURCE=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/dawn-headers \
  PROGPU_NATIVE_CXX_COMPILER=/usr/bin/clang++ PROGPU_NATIVE_ENABLE_CPP_MODULES=OFF \
  PROGPU_NATIVE_BUILD_JOBS=4 ./eng/build-progpu-native.sh --build-only --rid osx-arm64
```

The x64 command uses `--rid osx-x64`, `build-osx-x64` and `runtime-osx-x64`;
all other inputs are identical. No Rosetta process or target executable runs.
Pinned dependencies remain wgpu-native 33133da4, WebGPU headers aef5e428,
Dawn headers 01addc4b and Silk.NET native package 2.23.0.

- ARM64: successful exit 0, 62 incremental compile/link steps, staged both native
  provider libraries and the six required SDK archives.
- x64: successful exit 0, 193 incremental compile/link steps, staged both native
  provider libraries and the six required SDK archives into the same package root
  under its separate osx-x64 RID.
- ProGPU.Tests: forced restore followed by serialized standalone SDK build,
  0 warnings, 0 errors, 36.68 seconds. Source-contract fixtures were authored and
  compiled, not executed.

Output is under the isolated checkout's
`artifacts/progpu-native/package/runtimes/<rid>/native`, with SDK archives in
its `sdk` subdirectory. Older Linux sets remain distinct and unqualified.

## Remaining requirements

These are unqualified package inputs, not installable LibreWPF packages or proof
of application startup/rendering. Windows native/managed payload production,
remaining application integration and exact-head Linux refresh remain open.
The suspended Windows Parallels VM and its saved state were not touched or retried.
No tests, renderer applications, GPU workloads, verifiers, benchmarks or CI checks
ran. Final architecture/ABI, output, input, lifetime, performance and package-mode
qualification remain mandatory after feature freeze.

See the [ProGPU build contract and compiler-setting provenance](../external/ProGPU/docs/native-mil-build-only-payloads.md)
and the [core delivery queue](../docs/native-mil-core-delivery.md).
