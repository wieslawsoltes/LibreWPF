# Native MIL Windows package input production — 2026-09-09

## Acceptance dependency

The package-mode MVP opening with explicitly selected native MIL requires Windows
native renderer and source-built managed/IJW payloads. This batch resumes the
Windows build environment and closes native MSVC compilation blockers. It does
not admit Windows package startup or claim application parity.

## Environment and source

The existing Windows 11 ARM64 Parallels guest
`9d6a85ae-607e-4508-bb6d-f79a2a0f0059` resumed gracefully and accepted guest commands.
There was no reset, saved-state discard, Tools upgrade or VM configuration change.
The installed C++ toolset is MSVC 19.44.35228.0 / 14.44.35207 at `C:\BuildTools`.
The VM remains running for build work.

Portable tools were staged in `C:\src\artifacts\librewpf-native-core-2ab498be`,
not installed globally. Official release asset digests matched the downloads:

- PowerShell 7.6.6 ARM64 ZIP:
  `bbde9dda31d148415eccb5fbe1638e6400a144187b006e5b3fd8ec2f39d781be`.
- MinGit 2.55.0.windows.5 ARM64 ZIP:
  `05843f9d6e60306c3ab886799e2c67200caab921571f10512df3493049179ddb`.

PowerShell 7 reports existing RemoteSigned policy. No policy override or persistent
PATH change was made. Windows PowerShell 5 remains Restricted. The separate WPF
managed build's legacy `build.cmd` wrapper explicitly requests Bypass; this batch
does not run that wrapper while the policy-exception question remains unresolved.

The guest `progpu` directory starts with the committed `2ab498be` source archive
used for Linux production, then receives only the native fixes committed as
ProGPU `da36a7188bb887b3935ac7929b03534b333658ef`. It excludes unrelated dirty scene
sources and performance artifact deletions in the working checkout. Freshly
fetched ProGPU main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor
of this branch. Pinned wgpu-native, WebGPU/Dawn headers and Silk.NET inputs remain
unchanged. A separate shallow LibreWPF clone was prepared at `C:\lwpf-eb8660165`;
managed Windows production has not started.

## Implementation and build result

The [ProGPU compiler-fix record](../external/ProGPU/docs/native-mil-windows-msvc-build-closure.md)
documents type-safe caret affinity comparisons, guarded picture-stream size
narrowing, Windows macro collisions and fixture-local naming/type corrections.
The caret C/C++ differential fixture was extended. MIL ledger generation changed
only its decoder digest, not the command inventory. No verifier ran.

Commands use the existing native entry point in its explicit build-only mode:

```powershell
./eng/build-progpu-native-windows.ps1 -Rid win-arm64 -Compiler MSVC -BuildOnly
./eng/build-progpu-native-windows.ps1 -Rid win-x64 -Compiler MSVC -BuildOnly
```

ARM64 completed successfully after the compiler corrections, retaining strict
C++20 `/W4 /WX`, both providers, Direct2D, all SDK archives and all test/sample
compilation. The final incremental pass completed 57 steps and exited 0; it is
not a claim of a clean 57-step total build. The original build graph has 356 steps.

The successful ARM64 staging contains:

- `progpu_native.dll`, `progpu_native_dawn.dll`, `progpu_native_direct2d.dll`.
- Seven SDK libraries: Dawn import, compression, hit testing, image, MIL,
  scene builder and text.

The complete ARM64 RID directory was copied back beside the Unix build inputs at
`artifacts/native-core-build.KvxVug/progpu/artifacts/progpu-native/package/runtimes/win-arm64`.
The x64 command completed all 356 steps successfully with the actual MSVC x64
toolchain and matching pinned x64 linker input. It produced the same three DLLs
and seven SDK libraries. Both Windows RIDs use the native source at da36a718;
no target test/sample executable ran, including no emulated x64 renderer workload.
The complete x64 RID directory was also copied into the same host staging root
under `win-x64`. Both Windows native build processes exited 0 and are finished.

## Remaining work and evidence limits

Finish Windows managed/IJW production and complete package
construction. Keep Windows SDK admission guarded until source/package integration
dependencies are implemented. Refresh Unix inputs from the final delivery source:
their existing `2ab498be` payloads precede the compiler-fix checkpoint.

This is implementation and compilation only. No tests, applications, renderer
workloads, output/export verifiers, benchmarks or CI polling ran. Final package
consumption, native/managed/native-Windows comparisons, GPU/SIMD, lifetime,
performance and exact-head CI remain mandatory. See the
[active delivery plan](../docs/native-mil-core-delivery.md).
