# WPF text-layout comparison fixture

`ProGPU.Wpf.TextLayoutParityApp` uses the LibreWPF SDK and ProGPU native MIL.
The sibling `ProGPU.Wpf.TextLayoutParityApp.Windows` project links the same
`App.xaml`, `MainWindow.xaml`, and code-behind but uses the stock Windows WPF
SDK. Keep the content shared: the native Windows renderer is the same-machine
reference for line breaks, size, and caret geometry.

Set `PROGPU_WPF_TEXT_LAYOUT_REPORT=1` to query every insertion position,
including the last one, and print one `TEXT_CASE` metrics line for each
matrix case. The shared matrix covers wrapped composite-font text, mixed
run sizes/styles/weights, `WrapWithOverflow`, tabs and retained whitespace,
explicit block line height, bidirectional text, forced native-national digits,
and contextual digits across Latin/Arabic strong characters and a hard break.
Both SDK projects stage the same repository Traditional Arabic font for the
digit cases; their source strings retain ASCII digits. Add
`PROGPU_WPF_TEXT_LAYOUT_EXIT_AFTER_REPORT=1` for unattended validation. An
invalid final caret rectangle in any case writes `TEXT_LAYOUT_ERROR` and
exits nonzero. The gate compares source line starts, actual and desired size,
line tops/heights, and final-caret geometry for every named case.
The package gate also enables `PROGPU_WPF_TEXT_LAYOUT_DETAIL=1`, records every
real insertion-position rectangle, and compares its X, Y, and height against
the stock Windows WPF process by source offset. This prevents matching line
counts or endpoints from hiding an interior source-layout defect.
When compiled for native MIL, the fixture also requires a live ProGPU window
host and prints `TEXT_RENDERER NativeMilWgpu`. The package gate checks that
marker and the SDK's runtime selection record before comparing metrics; a
Windows-MIL fallback is not native text evidence. The Windows x64 and ARM64
package gates in
`eng/progpu-wpf-windows-native-mil-showcase.ps1` build and compare both
variants; see `reports/native-mil-windows-text-layout-parity-2026-09-15.md`
for its first guest result and
`reports/native-mil-text-runtime-admission-2026-09-15.md` for the subsequent
runtime-admission correction. The expanded six-case Windows ARM64 result is
recorded in
`reports/native-mil-windows-text-layout-matrix-2026-09-21.md`; its explicit
qualification boundary keeps rendered-pixel and broader text contracts open.
The new eight-case matrix still requires exact-package Windows execution;
the earlier six-case result does not qualify number substitution.
