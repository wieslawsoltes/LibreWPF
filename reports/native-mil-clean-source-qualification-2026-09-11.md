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
