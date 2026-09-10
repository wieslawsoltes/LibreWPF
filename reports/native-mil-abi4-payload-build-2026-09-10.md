# ABI 4 native package-input production

Acceptance application: the package-mode LibreWPF Showcase in native MIL mode.
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

## Windows ARM64 input completed; x64 build in progress

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
All 357 steps completed and the build-only entry point exited successfully.
Both WebGPU providers, the Direct2D COM DLL, all SDK libraries and configured
test/sample executables compiled; no resulting executable ran.

The complete ARM64 RID directory was copied into the isolated host worktree at
`artifacts/progpu-native/package/runtimes/win-arm64/native`. It contains three
DLLs and seven SDK libraries, including the Dawn import library. Source HEAD and
tracked cleanliness were checked before copying, and the host files were observed
afterwards. This is unqualified package-input staging, not Windows application
or Direct2D runtime parity evidence. The completed guest transcript is
`C:\pgpu-rebase-2a998c86\artifacts\rebase-build-win-arm64.log`.

The subsequent `-Rid win-x64 -Compiler MSVC -BuildOnly` invocation is active.
Configuration selected MSVC 19.51.36257 at
`C:\BuildTools2026\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\cl.exe`, and the
live full build advanced through step 60/357. Resume host execution session
`18036` rather than starting another build. Its transcript is
`C:\pgpu-rebase-2a998c86\artifacts\rebase-build-win-x64.log`.
The scoped local runner is
`artifacts/native-core-build.KvxVug/windows-rebase-build.ps1`; omit `-Prepare`
on an inspected retry. After successful x64 staging, the adjacent
`windows-rebase-stage.ps1 -Rid win-x64` copies the complete RID directory to the
host, rejecting an existing destination or missing successful staging record.
Neither runner weakens the production script's payload requirements.

## Current-source Windows managed preparation

The required package inputs include source-built `PresentationCore`,
`DirectWriteForwarder` and matching IJW hosts for Windows x86, x64 and ARM64.
Earlier `aa3bc9ad8` outputs are preserved, but are not substituted for current
source after the native/main integration. The acceptance action remains starting
the complete package-mode Showcase, not a reduced direct-host harness.

Preparation is active in a new guest checkout, `C:\lwpf-abi4-828fff634`.
The LibreWPF clone completed and its HEAD check accepted
`828fff63436d9f16a6ac397c538affa27ce8c9cf`; the pinned ProGPU submodule is now
being cloned. Its required HEAD is `2a998c86b669dda259a638470491440c1478a71f`.
The prior `C:\lwpf-eb8660165` source and all of its generated outputs remain
untouched. Only `.dotnet`, `.packages` and `.tools` build inputs are scheduled
for copying into the new checkout; no previous product assembly or native build
directory is reused. Copying and final clean-source checks have not completed.

Resume host preparation session `11927`; do not repeat `-Prepare` against the
partially populated directory. Its scoped runner is
`artifacts/native-core-build.KvxVug/windows-managed-abi4.ps1`. Only after successful
preparation and the current native x64 build completes, use its separate `-Build`
invocation. That calls the existing `eng/progpu-wpf-windows-managed-runtime.ps1`
with `-Rebuild`, all three RIDs, the pinned validated x64 SDK host, PowerShell 7
child processes and unchanged execution policy. No new managed build is running
at this checkpoint; no Windows managed payload or complete package is claimed.

## Linux input refresh

Further read-only inventory found the preserved `progpu-native-build` Colima
profile stopped, with the previous successful containers and toolchain images
intact. Resuming this named profile restored its Docker context. The context's
earlier absence was not loss of the build environment. The default Docker context
stays `default`; the VM retains 4 CPUs/4 GiB, no home/workspace mount, no forwarded
SSH agent and no application port forwarding. Its only host mount is Colima's
read-only cache. No old container or image was deleted or restarted.

A full Git archive of clean ProGPU `2a998c86` supplies a fresh
`/work/progpu-2a998c86` source directory; it is not overlaid onto older source.
Host archive: `artifacts/native-core-build.KvxVug/linux-native-source-2a998c86.tar`.
SHA-256: `9f90222d28c6496e57a4b06baeeecde8aac311a854f6764678f6ce40eeb1ddc5`.
The existing toolchain/dependency cache is reused separately. Actual tools are
Clang 18.1.3, CMake 3.28.3, Ninja 1.11.1 and .NET SDK 10.0.201. Containers retain
4 CPU/3500 MiB limits, three native build jobs, no mounts and no privileged mode.

Both containers completed the full 711-step C++20 module graph and exited 0:

| Container | Explicit build-only target | Elapsed container time |
| --- | --- | --- |
| `progpu-native-linux-arm64-2a998c86` | `--rid linux-arm64` | 2m 46s |
| `progpu-native-linux-x64-2a998c86` | `--rid linux-x64` | 2m 26s |

Each invokes `eng/build-progpu-native.sh --build-only` with the target above.
x64 uses the real GNU cross-toolchain; no target emulator or RID relabel is used.
Both providers, all six SDK archives and configured tests/samples compiled;
no resulting executable ran. Complete RID directories were copied after observed
successful staging to the clean host ProGPU worktree's
`artifacts/progpu-native/package/runtimes`. Both providers and six archives were
observed in each host directory. Docker retains each terminal container and log.
After confirming no running container remained, the dedicated Colima profile
stopped gracefully. Containers, toolchain caches and host payloads are preserved;
the default Docker context and Parallels VM state are unchanged by that shutdown.

The isolated LibreWPF `wpf-rebase` build worktree now selects committed source
`07d7233aa` and its exact ProGPU `2a998c86` gitlink. Its earlier local adapter
changes were first confirmed identical to committed files and preserved on local
branch `build/native-mil-rebase-compiled-checkpoint` (`c031aee79`); they were not
discarded. This prepares clean package source, not completed package production.

## Remaining delivery inputs and gates

Finish Windows x64 and copy its complete RID payload directory only after
successful staging. All four Unix RID directories and Windows ARM64 now carry
ABI 4 inputs from the same ProGPU source; Windows x64 is still incomplete.
Windows managed/IJW and complete SDK package inputs also need current-source
production through the existing explicit build-packages-only lane.

No tests, application/VM graphics, source/export/package verifiers, benchmarks
or CI polling ran. Automatic CI remains enabled. Runtime, image, SIMD/GPU,
native-Windows comparison, module/compiler matrices, performance and exact-head
PR CI qualification remain mandatory after core feature freeze. Core application
closure, including the documented native modality/popup dependencies, is still
open. These builds do not claim completion of broader DirectX/Direct2D/Win2D work.
