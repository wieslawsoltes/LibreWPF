# ABI 4 native package-input production

Acceptance application: the package-mode LibreWPF MVP in native MIL mode.
User action: start the application using the new managed/native ABI together.
Blocking package path: old platform payloads predate the latest-main rebase and
cannot be mixed with the ABI 4 managed contract. Bounded outcome: compile and
stage complete native platform inputs before final package/application execution.
This does not change core scope or qualify application startup.

## Source and completed macOS inputs

Source is clean ProGPU `2a998c86b669dda259a638470491440c1478a71f`, based on merged
main `73cda9a5`. Builds use the isolated `progpu-rebase` linked worktree under
`artifacts/native-core-build.KvxVug`; the original dirty checkout is untouched.

Both explicit build-only commands completed successfully:

```sh
eng/build-progpu-native.sh --build-only --rid osx-x64
eng/build-progpu-native.sh --build-only --rid osx-arm64
```

Apple Clang 21 compiled strict C++20 with warnings as errors, using the supported
header compatibility mode. x64 completed all 351 fresh build steps. ARM64 reused
the previous full build, then completed 41 incremental steps and staging. Both
retained wgpu-native and Dawn, six SDK static libraries, and all configured
test/sample compilation. Neither target's executables ran.

Staging roots, relative to the clean ProGPU worktree:

- `artifacts/progpu-native/package/runtimes/osx-x64/native`
- `artifacts/progpu-native/package/runtimes/osx-arm64/native`

Each contains both provider dylibs and the compression, hit-testing, image,
MIL, scene-builder and text SDK archives. These are unqualified build inputs,
not an updated published feed or final package-consumption result.

## Windows build in progress

The Parallels CLI skill guided VM state/Tools inspection and graceful startup of
the existing suspended Windows VM `9d6a85ae-607e-4508-bb6d-f79a2a0f0059`.
No reset, saved-state discard, Tools update, reboot or execution-policy override
was performed. Existing Tools accepted guest execution after startup.

Windows tar could not extract the repository's browser `main.js` symlink.
The incomplete `C:\pgpu-2a998c86` directory is preserved and was not built.
A fresh Git checkout at `C:\pgpu-rebase-2a998c86` has the exact commit above and
was checked for tracked modifications before build. Git's ordinary Windows
symlink representation is used; no renderer source or required target was omitted.

PowerShell 7 invokes the existing entry point:

```powershell
eng/build-progpu-native-windows.ps1 -Rid win-arm64 -Compiler MSVC -BuildOnly
```

Dependencies restored with .NET SDK 10.0.401. Configuration selected the actual
ARM64 compiler at `C:\BuildTools2026\VC\Tools\MSVC\14.51.36231\bin\Hostarm64\arm64\cl.exe`
(MSVC 19.51.36257), matching ARM64 wgpu input, and the full 357-step graph.
At this checkpoint compilation is still active; `cmake`, `ninja` and six `cl`
processes were observed, and the live output advanced through step 89. This is
not a successful Windows build or a staged Windows payload claim.

Resume the existing build rather than start a duplicate. The host execution
session is `68235`; the guest transcript is
`C:\pgpu-rebase-2a998c86\artifacts\rebase-build-win-arm64.log`.
The scoped local runner is
`artifacts/native-core-build.KvxVug/windows-rebase-build.ps1`; omit `-Prepare`
on an inspected retry or for the subsequent `-Rid win-x64` build.

## Remaining delivery inputs and gates

Finish Windows ARM64, then x64, and copy complete RID payload directories only
after successful staging. The historical Linux Docker context
`colima-progpu-native-build` is no longer present; fresh Linux build preparation
must inspect current infrastructure instead of assuming the old container exists.
Windows managed/IJW and complete SDK package inputs also need current-source
production through the existing explicit build-packages-only lane.

No tests, application/VM graphics, source/export/package verifiers, benchmarks
or CI polling ran. Automatic CI remains enabled. Runtime, image, SIMD/GPU,
native-Windows comparison, module/compiler matrices, performance and exact-head
PR CI qualification remain mandatory after core feature freeze. Core application
closure, including the documented native modality/popup dependencies, is still
open. These builds do not claim completion of broader DirectX/Direct2D/Win2D work.
