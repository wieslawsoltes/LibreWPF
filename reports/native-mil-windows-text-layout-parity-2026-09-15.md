# Windows native-MIL text layout and final-caret parity

Date: 2026-09-15. Acceptance application: `ProGPU.Wpf.TextLayoutParityApp`.
The user action is asking the last `TextPointer` in a wrapped, styled `TextBlock`
for its character rectangle. Both app projects compile the same XAML and code:
the LibreWPF SDK selects ProGPU C++ native MIL, while the `.Windows` project
uses the stock Microsoft WPF SDK as the same-machine reference.

## Failure and source contract

The exact merged LibreWPF #131 Windows package ran the text fixture on the
Windows 11 ARM64 Parallels desktop and failed fast in
`MS.Internal.Text.Line.GetBoundsFromPosition`. The last insertion pointer was
source offset 138. Instrumentation showed its native paragraph line had
`First=121`, `End=139`, `Length=19`, `NewlineLength=1`, and its requested
source position 138 mapped to the same shaped-text index on both sides
(`137:137`). This is a hidden closing formatting symbol before the paragraph
terminator. It has a caret but no ink or selection rectangle.

`PortableTextLine.GetTextBounds(138, 1)` correctly returned no selection
geometry. The source `Line.GetBoundsFromTextPosition` nevertheless demanded
exactly one `TextBounds` for this caret query and called `FailFast`. Changing
`GetTextBounds` to paint a hidden-edge rectangle would conflate selection and
caret contracts. Instead, `PortableTextLine.TryGetNonInkCaretBounds` provides
the zero-width caret rectangle only to that point-query path. Ordinary text
still uses its real run bounds, and hidden source edges still return empty
selection bounds. `Line` retains its existing trailing-space X shift.

The focused source `PresentationCore.Tests` portable-media class passes 24/24
on the guest with no skips. The source `PresentationCore` Release build had no
warnings or errors; `PresentationFramework` Release built with zero errors.

## Same-source Windows comparison

The package payload came from LibreWPF #131's passing PR-head Build
[34895232305](https://github.com/wieslawsoltes/LibreWPF/actions/runs/34895232305),
whose tree matches merged commit `a89e8d233`. It pins ProGPU `main`
`c36cf91d96cd669faf3f355294f5d33fed2dcc52`, still current when checked
for this run. The guest package-built fixture's `progpu_native.dll` SHA-256
was `c8ca49a88a3a8e8a69769c5c17d738013f282b8083357eb606e42ed4628e810b`,
and its `PresentationCore.dll` was
`94de60be5a824bd0641fbba0964069e768bdbe253ae160258d1a52e13e2b0cc8`,
matching the exact package members. For the repaired run, only the managed
`PresentationCore.dll` and `PresentationFramework.dll` were overlaid in a
private guest copy; their source-build hashes were respectively
`bb10fdbe520340e2893df62710b01713f03a99d4b0b0df764f7fbe071fd9df01`
and `d06e879067e7daf51e6b88d28fc77bcf81cf6ae83298a4c2a89d21c52005295e`.
The ProGPU native DLL was unchanged. This is source-overlay validation, **not
yet a newly packaged or hosted-CI result for the repair**.

Both apps ran under the signed-in guest user with
`PROGPU_WPF_TEXT_LAYOUT_REPORT=1` and
`PROGPU_WPF_TEXT_LAYOUT_EXIT_AFTER_REPORT=1`:

| Renderer | Width (DIP) | Height (DIP) | Lines | Line tops (DIP) | Source line starts |
| --- | ---: | ---: | ---: | --- | --- |
| Stock Windows WPF | 211.333 | 93.100 | 5 | 0.000, 18.620, 37.240, 55.860, 74.480 | 0, 32, 58, 88, 121 |
| ProGPU native MIL with source repair | 211.333 | 93.105 | 5 | 0.000, 18.621, 37.242, 55.863, 74.484 | 0, 32, 58, 88, 121 |

The line breaks match exactly; the largest observed line-top difference is
0.004 DIP. Live guest captures show the same five whole-word lines, including
`ControlTemplate`, `TemplateBinding`, and `VisualStateManager` without a
split word. The captured native-MIL window starts at a different desktop
position despite the shared `WindowStartupLocation`, which is a separate
windowing observation, not text-layout parity evidence. Neither these two
captures nor this narrow fixture qualify the full font, editor, document,
platform, or DirectX parity matrix.

## Repeatable gate and remaining admission

`eng/progpu-wpf-windows-native-mil-showcase.ps1` now builds both fixture
projects after its exact-package x64 Showcase check. It verifies the ProGPU
app's package-member hashes, runs both displayed apps to completion, rejects
missing final-caret geometry, requires identical source line starts, and
compares width, height, font size, and line tops within bounded DIP tolerances.
The CI `windows-native-mil-showcase` job invokes this script on native Windows
x64. Its new behavior must pass on the PR's exact package commit before this
repair is merge-ready. The Parallels ARM64 source-overlay result does not
substitute for that hosted gate.

The broader goal still retains its separate Windows SDK admission,
macOS/Linux visual text matrix, rich editor/document coverage, DirectX and
Direct2D/Win2D expansion, and full native-MIL qualification requirements.
