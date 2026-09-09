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
not deleted. The corrected script installed the fresh x64 SDK. The dotnet-engine
retry compiled PresentationBuildTasks for net472 and net10.0 with 0 warnings and
0 errors in 1:21.36. PresentationCore then failed during restore in
DirectWriteForwarder.vcxproj with MSB4278: the dotnet engine cannot resolve
`$(VCTargetsPath)\Microsoft.Cpp.Default.props`. That child failure propagated
(0 warnings, 1 error, 7.40 seconds); the build is terminal, not still running.
No completed PresentationCore/DirectWriteForwarder payload is claimed.

## Compatible Visual Studio prerequisite

`eng/WpfArcadeSdk/tools/Wpf.Cpp.props` requires v145 and Windows SDK
10.0.26100.0. The next build needs compatible Visual Studio Build Tools, not
source retargeting to v143 or a bypass of the missing C++ import. Preflight found
approximately 102 GB free on guest C:, no active build/installer process and no
`C:\BuildTools2026` directory. The existing `C:\BuildTools` VS 2022 is preserved.

The fixed VS 2026 Build Tools 18.10.0 bootstrapper came from Microsoft's
[release history](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-history).
Host and guest SHA-256 both matched
`e21cd5dc076844266c9a9bc23eb3573fcca92a14ed5da088dc1a62f796b03171`.
Guest Authenticode status was Valid, signer Microsoft Corporation, product
version 18.10.0 and file version 18.10.12201.205.

The scoped request uses `--installPath C:\BuildTools2026 --passive --norestart
--wait` with these documented component IDs:

- Microsoft.VisualStudio.Workload.VCTools
- Microsoft.VisualStudio.Component.VC.Tools.x86.x64
- Microsoft.VisualStudio.Component.VC.Tools.ARM64
- Microsoft.VisualStudio.Component.VC.CLI.Support
- Microsoft.VisualStudio.Component.Windows11SDK.26100
- Microsoft.Net.Component.4.7.2.TargetingPack
- Microsoft.Net.Component.4.8.SDK

See Microsoft's [component catalog](https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=visualstudio)
and [installer parameters](https://learn.microsoft.com/en-us/visualstudio/install/use-command-line-parameters-to-install-visual-studio?view=visualstudio).
No existing-instance update, forced reboot or security-policy change was requested.

The Parallels launch returned immediately; this was not installation completion.
A fresh inventory found bootstrapper PID 10980 and consent PID 12060. A guest
screenshot confirmed the Visual Studio Installer UAC prompt with verified
publisher Microsoft Corporation. At that checkpoint installation awaited user
approval; no approval was automated and the target directory was absent. Do not
start a duplicate installer from the launcher exit code alone.

## Resumed Windows build after toolchain installation

On goal resumption, fresh guest inventory found no installer/consent or build
process. VS 2026 Build Tools was registered at `C:\BuildTools2026`, instance
`200f1eec`, installation version `18.10.12201.205`, complete and launchable with
no reboot required. The existing VS 2022 instance remains installed. A component-
filtered vswhere query matched MSBuild, C++/CLI, ARM64 and x86/x64 tools in the
new instance. Direct MSBuild version output was `18.10.1.42706`; the installed
MSVC directory is `14.51.36231`. No UAC approval or reboot was automated.

The existing dedicated WPF guest checkout resumed its current managed-runtime
script with the default Visual Studio engine, existing x64 SDK and unchanged
RID/payload requirements. Its IJW packs and Arcade toolset restored, but the
build stopped in Tools.proj before source compilation: the MSBuild .NET Runtime
Task Host could not be found. The terminal result was 0 warnings, 1 error in
26.24 seconds, propagated through the PresentationBuildTasks child-build check.
The binlog is `C:\lwpf-eb8660165\artifacts\log\Release\x86\Build.binlog`.
Native-tool bootstrap still reports the previously documented mismatch
between tool-name executable checks and actual extracted package layouts; no
fake executable or bootstrap bypass was introduced. Completion and final payload
staging were not claimed for this retry.

### .NET task-host discovery correction

Read-only project evaluation under VS 18.10 resolved the pinned SDK and its
NetCoreRoot, but returned an empty `DOTNET_HOST_PATH`. The SDK contains
MSBuild.exe, MSBuild.dll and its runtime configuration; searching for a separate
TaskHost-named binary was not the right prerequisite check.
MSBuild's [task factory implementation](https://github.com/dotnet/msbuild/blob/main/src/Build/Instance/TaskFactories/AssemblyTaskFactory.cs)
requires both the .NET executable and SDK root to populate .NET task-host
parameters. The [task-host launcher](https://github.com/dotnet/msbuild/blob/main/src/Build/BackEnd/Components/Communications/NodeProviderOutOfProcTaskHost.cs)
raises the observed error when those parameters lack the SDK location.

The package entry now sets `DOTNET_HOST_PATH` to the same PE-validated x64
dotnet.exe selected by Initialize-BuildSdk. It does not pass a directory as the
executable, change the SDK, suppress task execution or alter the VS/C++ engine.
The existing source-contract fixture now asserts this executable mapping and
rejects a root-directory-only assignment. Fixture execution remains deferred.
The corrected retry passed Arcade toolset setup and entered source project
restore. It then failed in ProcessFrameworkReferences with MSB4018 because
Microsoft.Deployment.DotNet.Releases 2.0.0.0 could not load (0 warnings, 1 error,
24.12 seconds). The build is terminal; managed payloads remain incomplete.

The assembly is present in the pinned SDK, including its net472 task directory.
However, VS 2026 MSBuild.exe.config explicitly binds that version to
`SdkResolvers/Microsoft.DotNet.MSBuildSdkResolver/`, which is absent in the
C++-only Build Tools instance. This is an installation prerequisite, not a
missing application reference. The installed VS catalog identifies
`Microsoft.NetCore.Component.SDK` as depending on
`Microsoft.Net.Core.SDK.MSBuildExtensions`. A scoped bootstrapper modification
was requested for that official component in `C:\BuildTools2026`, using
`--passive --norestart --wait`. No existing SDK was removed, binding config
edited, resolver assembly copied or UAC approval automated. The repository
continues to select its pinned x64 SDK independently of the installed component.
The initial launcher returned immediately; completion must be established from
installer state and actual resolver files, not that exit code.

The component installation completed at guest time 13:44:18. The setup log
records Completed install and release of its singleton lock; fresh process
inventory found no installer/bootstrapper remaining. The official resolver
directory and Microsoft.Deployment.DotNet.Releases.dll now exist, and a
component-filtered vswhere query matches the complete VS 2026 instance without
a reboot requirement. The unchanged corrected package script has resumed.

This retry compiled PresentationBuildTasks for both net472/net10.0 (2 warnings,
0 errors, 59.44 seconds), then restored PresentationCore's project graph and
compiled/linked the x86 DirectWriteForwarder C++/CLI library. The x86
PresentationCore graph then completed with 26 warnings, 0 errors in 3:56.24.
x64 completed with 26 warnings, 0 errors in 4:02.90; ARM64 completed with
26 warnings, 0 errors in 3:50.55. The script exited successfully and staged
PresentationCore, DirectWriteForwarder and IJW for all three Windows RIDs.
These outputs are compiler-host-warning artifacts, not a qualified package set.

The two build-task warnings are CS8034 analyzer load failures, also seen in
subsequent source projects. Root Directory.Build.props unconditionally requests
the .NET Framework-hosted compiler, and guest process inspection confirmed the
framework compiler toolset was used. The pinned SDK's BeforeCommon.targets
supports selecting the .NET compiler through DOTNET_HOST_PATH when that request
is false. The package lane now passes
`/p:BuildWithNetFrameworkHostedCompiler=false` to each Arcade child, with an
authored contract assertion. This leaves Visual Studio/C++/CLI and all analyzer
inputs enabled; the active older invocation is not modified in place. A fresh
build is required to establish the result of this compiler-host correction.

Read-only PE header inspection confirms both failing SDK analyzers are AMD64
managed-native images (196-byte managed native headers, not IL-only). The
framework toolset csc.exe is an I386 IL-only image. The correction therefore
keeps the SDK and its analyzer architecture together in the validated x64 .NET
host, independently of the x86/x64/ARM64 output target.

The package script also exposes `-Rebuild`, forwarding restore plus Arcade's
existing Rebuild action instead of its incremental Build action. This is needed
for the compiler-host retry so already-produced assemblies do not hide skipped
analyzer execution. The default remains incremental; no tests, broad output
deletion or platform/payload exclusions are added. Contract assertions cover
both action mappings; execution remains deferred.

The first three-RID run is terminal (exit 0). All nine required DLLs were present
in its staging directory. Before the corrected invocation, that directory was
moved to `artifacts/windows-managed-runtime-framework-host-preserved`, and the
three platform Build.binlog files were copied into it. These generated artifacts
remain recoverable and are not selected for the final package. The guest script
was then updated from the working tree and invoked with `-Rebuild`.

The final source-contract project, including compiler selection and both rebuild
action mappings, compiled with 116 warnings, 0 errors in 13.52 seconds. No tests
executed. The local build exited before the guest rebuild started, preserving
serialized .NET build scheduling.

The updated ProGPU.Wpf.Tests project compiled locally with the pinned SDK using
`dotnet build --no-restore -c Release -m:1 -p:UseSharedCompilation=false`:
116 warnings, 0 errors, 38.87 seconds. This compiles the new contract assertion;
no test method executed and the warnings remain visible rather than suppressed.

### Rebuild project-reference graph correction

Acceptance path: package-mode MVP startup on Windows. The bounded outcome is
compiling the real Windows managed/IJW transport inputs required by
LibreWPF.Transport; no renderer fallback or SDK admission change is involved.

The compiler-host-corrected rebuild is terminal (exit 1). PresentationBuildTasks
compiled both targets with 0 warnings and 0 errors in 48.63 seconds, preserving
analyzer inputs. The x86 PresentationCore graph then failed with 0 warnings and
121 errors in 1:46.19 while compiling WindowsBase-ref: System.Xaml markup types
were missing. No corrected Windows RID payload was staged.

The failed x86 Build.binlog is preserved on the host under
`artifacts/msbuild-taskhost-source.1avQj5/x86-rebuild-reference-failure.binlog`.
A read-only replay shows EnsureWpfProjectReference removing System.Xaml-ref from
both configured project-reference lists in WindowsBase-ref (node 3, context 135).
The final compiler command contains no System.Xaml reference. The source project
still declares its real System.Xaml-ref ProjectReference. The diagnostic reader
uses forward-compatible replay and reports recoverable unknown-record notices;
these specific removal events and the compiler command, corroborated by the
target source, establish the fault without treating the log as a complete audit.

The target previously filtered configured references against only the list of
available named WPF implementation projects. Rebuild's Clean phase had already
populated those lists, so ordinary projects outside that list were discarded.
The original ProjectReference list already performed the correct second filter:
only unmatched paths also known to WpfProjectPath become package substitutions.
All configured removals now use that same known-missing set. Reference projects,
ProGPU dependencies and their metadata remain intact; missing known WPF projects
still follow the existing transport-package substitution. No binary HintPath,
extra whitelist entry, disabled analyzer or change to eng/common is introduced.

New source-contract fixtures cover all four removal lists, ordered known-missing
selection, the real reference-assembly dependency and retained substitution.
Execution remains deferred; a corrected full Windows rebuild is still required.

The updated fixture project compiled with 116 warnings and 0 errors in 12.94
seconds. No test method executed. After that local build exited, the corrected
target was copied to the dedicated guest checkout with source/destination hash
agreement required, and the existing package entry was invoked with `-Rebuild`.
Completion is not inferred from dispatch.

At source correction `aa3bc9ad8`, the retry rebuilt both PresentationBuildTasks
targets with 0 warnings/errors in 41.37 seconds. The x86 PresentationCore graph
then completed with 0 warnings/errors in 2:26.50, including the previously
removed System.Xaml-ref, WindowsBase-ref and ProGPU dependencies. x64 completed
with 0 warnings/errors in 2:34.07. ARM64 completed with 0 warnings/errors in
2:44.46. The wrapper exited 0 and staged all three corrected Windows RID inputs.
The compiler/analyzer-host and configured-reference failures are closed at the
build level. Package production and runtime qualification are separate results.

All nine required DLLs and the available PDBs were exported to
`artifacts/native-core-build.KvxVug/windows-managed-runtime-aa3bc9ad8`, together
with all three final Build.binlog files. The export checks every required path
before copying and requires source/destination SHA-256 agreement for every
payload file. The earlier warning-bearing outputs remain preserved separately.
The host's ordinary LibreWPF.Transport pack now selects this exact directory via
LibreWpfWindowsManagedPayloadDir; no default artifact directory was overwritten.

No tests, renderer workloads, apps,
verifiers, benchmarks or CI polling are part of this batch. Windows SDK admission
and final exact-head package/application/CI gates remain mandatory.
