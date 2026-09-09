# Native MIL core package payload build — 2026-09-09

Status: **compilation and partial payload production only; not qualified**.

## Acceptance dependency

Application: the existing package-mode `ProGPU.Wpf.MvpApp`.
Action: restore/build its explicitly selected native SDK renderer and start the
source-built application. The package feed requires the complete native runtime
and Windows managed payloads; the previous checkpoint found neither staged.
This batch builds the available platform outputs without executing validation.

## Source and dependency ownership

- WPF starting commit: `27099e0876f8533168627bd98c9b019340012a66`.
- Initial clean ProGPU commit: `95f9049232b0cc37ad23fb170efdccc75797be50`.
- Corrected ProGPU commit: `95504c8d0717de8b417b500f45ba4454bd18b900`.
- Latest fetched ProGPU main: `102e39e5088b462624da6296ff70a43ed2c5d8b4`, included.
- wgpu-native source: `33133da4ec5a0174cb21539ef2d3346f75200411`.
- Its WebGPU headers: `aef5e428a1fdab2ea770581ae7c95d8779984e0a`.
- Dawn WebGPU headers: `01addc4ba8a2915a061b7095a6768b512071ab96`.
- Native wgpu libraries: restored `Silk.NET.WebGPU.Native.WGPU` 2.23.0,
  architecture-specific macOS assets, copied to build-local runtime directories.

A detached ProGPU worktree under `artifacts/native-core-build.KvxVug/progpu`
excludes the main submodule's unrelated semantic-state changes and deleted
performance artifacts. It was advanced to the corrected commit before successful
builds and remained clean. No third-party implementation source was copied into
ProGPU; pinned dependency headers remain build inputs only.

## Build blocker corrected in ProGPU

The first ARM64 build failed in the Dawn variant of
`progpu_native_semantic_picture_mask_resources.cpp`: a retained picture seed copy
called raw `wgpuQueueSubmit`, which is unavailable in the provider-resolved ABI.
The fix uses existing `progpu_native_engine::submit` and removes the duplicate
local submission-counter increment. This retains the backend dispatch, latest
completion identity and retirement path used by other native GPU work.

See [the ProGPU contract and provenance](../external/ProGPU/docs/native-mil-picture-copy-submission.md).
The managed renderer needs no change for this C++-specific call-site failure.
An authored managed source-contract fixture protects submission/release ordering;
it compiled in ProGPU.Tests with 0 warnings/0 errors, but was not executed.

## Produced artifacts

Both complete CMake `all` builds succeeded, including the native renderer, Dawn
adapter, six SDK static libraries, sample and configured test executables. No
sample or test executable ran. The compiler was Apple Clang 21.0.0
(`clang-2100.0.123.102`), CMake 4.1.0, Ninja, Release/C++20, with the existing native
warning-as-error policy intact. Apple Clang uses the existing header compatibility
profile; LLVM named-module qualification is still separate.

| RID | Build directory | Runtime payload |
| --- | --- | --- |
| osx-arm64 | `artifacts/native-core-build.KvxVug/build-osx-arm64` | `libprogpu_native.dylib`, `libprogpu_native_dawn.dylib`, six SDK archives |
| osx-x64 | `artifacts/native-core-build.KvxVug/build-osx-x64` | Same payload, cross-compiled for Intel macOS |

Unqualified staging root: `artifacts/native-core-build.KvxVug/package`, using
`runtimes/<rid>/native` and its `sdk` subdirectory. The archives are compression,
hit_testing, image, mil, text and scene_builder. These are local build artifacts,
not checked-in binaries or a release manifest. They are only two of six required
desktop RIDs, so they do not constitute a complete packable native package.

The direct CMake configuration retained `PROGPU_NATIVE_BUILD_SAMPLE=ON` and
`BUILD_TESTING=ON`; it built the fixtures instead of deleting them from the graph.
For each RID, configuration used the clean `src/ProGPU.Native` source directory,
the pinned combined wgpu include directory, its matching native library and the
pinned Dawn header directory, plus `CMAKE_OSX_ARCHITECTURES=arm64` or `x86_64`.
Build command: `cmake --build <build-directory> --config Release --parallel 4`.
The normal native build scripts were not invoked because they execute CTest,
samples and additional qualification after compilation.

## Windows environment interruption

The Parallels skill was used to discover and inspect the existing Windows 11 VM.
Parallels 27.0.1 (58670) reported it suspended, with installed but outdated Tools.
A single supported resume operation failed with a critical error and the VM
returned to suspended. A read-only listing of its bundle on `/Volumes/1TB-macOS`
also stalled; the agent terminated only its own listing command. This does not
establish the root cause. No reset, state deletion, disk repair, Tools upgrade,
snapshot restoration or guest command succeeded or changed the VM contents.
The existing Ubuntu VM is also suspended on that volume; it was not started.
The user was asked to make the VM/storage available without discarding saved state.

## Remaining work

Build Windows x64/ARM64 native and x86/x64/ARM64 managed payloads when the guest
is available, then Linux x64/ARM64 native payloads through the existing platform
build contracts. Preserve the complete package requirements; do not fill missing
RIDs with release binaries from another source commit or bypass admission.
Then produce the fresh package feed and compile the unchanged native-selected MVP.
Windows SDK activation, application integration and the final qualification phase
remain open. No CTest, runtime/image/VM/GPU workload, export/import verifier,
benchmark, package audit or CI polling ran in this batch.
