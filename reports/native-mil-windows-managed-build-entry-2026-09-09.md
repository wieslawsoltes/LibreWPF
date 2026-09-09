# Windows managed package build entry — 2026-09-09

## Acceptance dependency

The package-mode MVP requires Windows PresentationCore, DirectWriteForwarder and
matching IJW hosts before complete package production and native SDK admission.
The native ARM64/x64 inputs were produced in the preceding batch; those do not
replace the source-built managed runtime inputs.

This batch works in the existing Windows ARM64 Parallels guest. The isolated
LibreWPF clone at `C:\lwpf-eb8660165` was confirmed clean, fast-forwarded to
`142b5c90b`, and initialized only its ProGPU submodule at
`da36a7188bb887b3935ac7929b03534b333658ef`. Only the changed package build script
is overlaid for compilation. The unrelated host ProGPU changes remain excluded.

## Implementation

`eng/progpu-wpf-windows-managed-runtime.ps1` now explicitly requires Windows and
PowerShell 7. It invokes the existing `eng/common/build.ps1` in a child of the
current PowerShell installation with `-NoProfile -NonInteractive -File`, explicit
`-restore -build`, preserved build options, and checked child exit status. It no
longer launches `build.cmd`, whose Windows PowerShell invocation requests an
execution-policy override. All three managed RIDs, pinned tool restoration,
Visual Studio MSBuild default, runtime routing and payload requirements remain.

The current PowerShell 7.6.6 guest reports RemoteSigned at LocalMachine and
Undefined at every other scope; Windows PowerShell 5 remains Restricted. No
policy or persistent PATH setting was changed. Scripts must be permitted by the
existing policy, and rejection is not retried through a less restrictive mode.
The earlier policy-exception question is unnecessary for this new entry path.
Arcade-owned files and the general repository build wrapper remain untouched.

Actual package-build startup then exposed a separate bootstrap bug. The old
`-runtime sdk` argument was forwarded through Arcade to the downloaded official
installer, which rejected `sdk` as an invalid runtime selector before downloading
the SDK. The package entry now passes an empty runtime to Arcade so it omits the
runtime selector upstream and installs the pinned SDK. It does not substitute a
different SDK or alter `global.json`.

The public contracts consulted are Microsoft's
[PowerShell executable arguments](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_pwsh?view=powershell-7.6)
and [.NET installation script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).
The implementation derives from LibreWPF's existing package script and Arcade
entry signatures, not copied external implementation. This is Windows package
build orchestration, with no renderer, shader, CPU kernel or managed/native
behavior change. GPU-first/SIMD and reflection-free product requirements remain.

## Compilation and qualification boundary

The source-contract fixture in `WindowsAnyCpuSmokeContractTests` now covers the
PowerShell host/child selection, restore/build flags, unchanged boolean warning
policy, failure propagation, SDK-selection argument and absence of execution
policy mutation or legacy wrapper fallback. The bridge test project compiled
after the initial child-process change with 116 warnings and 0 errors in 23.08 s.
The fixtures have not executed; compilation is not argument-binding or package
runtime qualification.

The corrected Windows build passed the former installer-argument failure and
installed the pinned SDK 11.0.100-preview.5.26302.115, restored the three IJW host
packs and entered the child Arcade build. The final fixture source, including
the installer guard, compiled with 20 warnings and 0 errors in 9.64 s.

The default Visual Studio build then failed with MSB4062: the SDK's
`AllowEmptyTelemetry` task requires `Microsoft.Build.Framework.IMultiThreadableTask`,
which the installed Visual Studio 2022 Build Tools 17.14.39 MSBuild cannot load.
The child exit was propagated and no managed payload was reported staged.
Native-tool bootstrap also reported missing tool-named executables; inspection
confirmed the Strawberry Perl archive was extracted with its real
`portableshell.bat`/`perl` layout. No fake executable shim, compiler version
downgrade or bootstrap bypass was added. The existing explicit
`-MSBuildEngine dotnet` option was attempted separately with the same SDK and all
payload requirements, not selected as a new default. It reached build-task
compilation, then failed to launch csc. A direct SDK-host diagnostic showed the
cause: Arcade installed its configured x64 runtime into the ARM64 SDK root;
the ARM64 host then rejected the newer x64 hostfxr with HRESULT 0x800700C1.

The package script now selects an x64 SDK host explicitly for this build lane,
including ARM64 Windows hosts. A bounded PE-header read checks local/global SDK
host candidates before selection and the final host before use. An existing
non-x64 repository host is rejected before installation rather than overwritten.
The runtime and output target architectures remain distinct: x86, x64 and ARM64
payloads are still required. This is consistent with Arcade's existing x64/x86
tool-runtime layout, not a native renderer architecture change. The final fixture
source including architecture guards compiled with 20 warnings and 0 errors in
9.29 s, without fixture execution.

The selected SDK metadata records a minimum MSBuild version of 18.6.0 and bundled
version 18.8.0. The installed VS 2022 17.14.39 does not meet that minimum. Do not
lower SDK requirements or replace the pinned SDK to hide that prerequisite.

After confirming no dotnet/MSBuild process remained, the generated mixed SDK
directory was preserved as `C:\lwpf-eb8660165\.dotnet-arm64-mixed-preserved`,
not deleted. The corrected script is now preparing a fresh x64 SDK at the normal
repository path and retrying the explicit dotnet-engine build. No completed
PresentationCore/DirectWriteForwarder payload is claimed at this checkpoint.

No tests, renderer workloads, apps,
verifiers, benchmarks or CI polling are part of this batch. Windows SDK admission
and final exact-head package/application/CI gates remain mandatory.
