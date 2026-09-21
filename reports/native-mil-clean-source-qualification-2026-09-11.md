# Clean-source application qualification

The prepared WPF checkout now selects commit
`87939694636d4883507b4cfc5ed641517c2773b6`, with LibreWinForms
`fcbd4e5f64ea2206e4c58a4077797e46170c0198` and both ProGPU references at
`b54db16546e8392d44f13443e6792861978cc3c5`. Its tracked/untracked status was
clean before building. Previous mirrored work, including one stale Showcase
sample edit, remains recoverable in the prepared checkout's named stash.
The main checkout's dirty physical submodules were not modified.

The Release application harness rebuilt successfully in 1:45.39 with zero errors
and two warnings: CS0067 on DisplayMetricsChanged and IDE0031 on the portable
activation service null check. Both native provider targets in the clean ProGPU
source checkout were also checked by CMake, which reported no work required.

The rebuilt `ProGPU.Wpf.RealApplicationRunHarness` then reported
`Real WPF Application.Run smoke succeeded` and exited zero on macOS arm64.
This qualifies this source/recording-host run, not package-mode startup or other
platforms. It does not supersede required native-host/input/image gates.

Exact-head Linux ARM64 runtime artifact 10181954651 from ProGPU run 34553573493
is downloaded with provenance into `artifacts/native-exact-b54db165.dBPf7B`.
Both native providers and SDK archives are present. Other RID payloads are still
required before the package-production guard can be satisfied.

## Native-host gate on the same revision

The real PresentationFramework harness rebuilt with zero warnings/errors in
8.71 seconds. The existing `eng/progpu-wpf-native-mil-host-smoke.sh` then exited
zero using that newly built harness and the checked local native providers.
Only its redundant build was skipped; neither executable gate was skipped.

The retention run passed viewport mutation/resize/disposal, image/DPI and
DrawingImage clear/refill, text collapse/justification/exclusions/inline content,
WriteableBitmap, Toolkit header collapse, rich-document source ownership and
Figure/Floater/excluded-line connections. The host run passed geometry selection,
presented a frame, and rebuilt its target after device loss while retaining the
existing native window. It reported 23 commands, 20 resources, 9 draws and five
submitted draw calls.

These are the assertions of this gate, not full input, platform or DirectX parity.
The package/runtime payload prerequisite remains open.

## Windows VM prerequisite inspection

The existing Windows 11 VM is running and Tools-backed guest commands succeed.
Parallels Desktop is 27.0.1; guest Tools report outdated 26.4.1. No installation,
VM configuration, lifecycle or policy change was made. No dotnet/MSBuild process
was found at inspection. Installed SDKs include 10.0.201, 10.0.400 and 10.0.401.

PowerShell 7 is absent from PATH but the existing task-local executable
`C:\src\artifacts\librewpf-native-core-2ab498be\pwsh\pwsh.exe` runs and reports
7.6.6. Its effective policy is RemoteSigned; Windows PowerShell independently
reports Restricted. Neither was modified or overridden. Use the verified full
PowerShell 7 path for the existing Windows managed payload script, subject to
its required clean x64 SDK-host checks. This is environment readiness only,
not Windows native MIL or DirectX parity evidence.
