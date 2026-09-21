# Windows native-MIL text layout matrix

Date: 2026-09-21. Acceptance application:
`ProGPU.Wpf.TextLayoutParityApp`.

## Purpose

The earlier Windows gate compared one wrapped paragraph. The application now
compiles one source fixture with both the stock Microsoft WPF SDK and the
LibreWPF SDK and covers six independent text contracts:

- wrapped composite-font text;
- mixed font sizes, weight, and style in one paragraph;
- `WrapWithOverflow` and an over-wide token;
- tabs, trailing spaces, and an explicit newline;
- explicit `LineHeight` with `BlockLineHeight`; and
- mixed right-to-left and left-to-right source text.

Each case reports actual and desired size, real line-start positions obtained
through `TextPointer.GetLineStartPosition`, line-start caret geometry, the
number of insertion positions, the final source offset, and the final caret.
The final caret is resolved from the last backward insertion position rather
than assuming that the `ContentEnd` element boundary is itself a caret. The
first guest run caught that distinction in stock WPF and prevented the oracle
from treating an element edge as a rendering failure.

## Windows ARM64 result

The comparison ran in the Windows 11 ARM64 Parallels VM on Windows
`10.0.26200.9457`, .NET SDK `10.0.401`, and a 192-DPI desktop. LibreWPF was
built from this branch over merged `main` `42f4ba6e0`; ProGPU was exact merged
`main` `910571ae18c5b8ec7f1fe41908fd8aa2304844e0`. The source-built app used
the exact merged native package payload:

- `progpu_native.dll`:
  `19E92448928F791116376E72DB5F3039890C6834044CD36AF68FF2244C747804`;
- `progpu_native_dawn.dll`:
  `6D0400F4846F3B771A1E43397C3ECCF511FC7C6369341E7C811A9D35CC526940`;
- `progpu_native_direct2d.dll`:
  `E78EC9A7F766DFEB5AFE202365D38E6CEA8CF64070047AA696F18C1B4B59C061`.

The authoritative run consumed the immutable green CI package bundle from
LibreWPF run `35617462820`. The package-only gate verified
`TEXT_RENDERER NativeMilWgpu` before accepting any ProGPU measurement. The
stock process used an outer window of `1560x1440` pixels and client area
`1534x1369`; the ProGPU process used `1560x1441` and `1534x1370`. Both
declared a source size of `780x720` DIPs. The one-pixel native-frame
difference is inside the gate tolerance and does not affect the text cases.

### Stock WPF

| Case | Actual size (DIP) | Lines | Source line starts | Positions / end | Final caret (X, Y, H) |
| --- | ---: | ---: | --- | ---: | ---: |
| wrapped-composite | 300.000 x 74.480 | 4 | 0, 41, 88, 130 | 138 / 138 | 44.710, 55.860, 18.620 |
| mixed-runs | 300.000 x 47.883 | 2 | 0, 54 | 80 / 88 | 219.947, 29.263, 18.620 |
| overflow-token | 233.057 x 55.860 | 3 | 0, 8, 49 | 59 / 59 | 59.850, 37.240, 18.620 |
| tabs-whitespace | 300.000 x 37.240 | 2 | 0, 24 | 35 / 35 | 101.967, 18.620, 18.620 |
| explicit-line-height | 260.000 x 75.000 | 3 | 0, 43, 81 | 103 / 103 | 144.003, 50.000, 25.000 |
| bidirectional | 300.000 x 37.240 | 2 | 0, 43 | 63 / 63 | -0.003, 18.620, 18.620 |

### ProGPU native MIL

| Case | Actual size (DIP) | Lines | Source line starts | Positions / end | Final caret (X, Y, H) |
| --- | ---: | ---: | --- | ---: | ---: |
| wrapped-composite | 300.000 x 74.484 | 4 | 0, 41, 81, 121 | 138 / 138 | 101.787, 55.863, 18.621 |
| mixed-runs | 300.000 x 47.883 | 2 | 0, 45 | 80 / 88 | 262.493, 29.262, 18.621 |
| overflow-token | 233.064 x 55.863 | 3 | 0, 8, 49 | 59 / 59 | 59.855, 37.242, 18.621 |
| tabs-whitespace | 300.000 x 37.242 | 2 | 0, 24 | 35 / 35 | 101.965, 18.621, 18.621 |
| explicit-line-height | 260.000 x 75.000 | 3 | 0, 43, 81 | 103 / 103 | 144.013, 50.000, 25.000 |
| bidirectional | 300.000 x 37.242 | 2 | 0, 43 | 63 / 63 | 122.896, 18.621, 18.621 |

The exact backend run therefore **fails** the gate. Composite-font and
mixed-run paragraphs choose different wrap boundaries; the final caret follows
those different boundaries. Mixed-run line-start rectangles use the whole
line metric envelope instead of the source run metric, explicit line-height
line-start rectangles use the 25-DIP line box instead of the centered
18.620-DIP run box, and the final RTL caret resolves to the opposite visual
edge. `WrapWithOverflow`, tabs, whitespace/newline positions, overall explicit
line-height size, source insertion counts, and ordinary line heights already
agree within expected float quantization.

The package audit matched `PresentationCore.dll`
`20255F1FD971E1BC8C2C1599FD639C53291C6CEFEAD493796D94CE184C64B2E2`,
`PresentationFramework.dll`
`8561673FF1C071DDCA533E9FB6E10079E039B04A4EF09884ACA797AA8FFFE8A0`,
`ProGPU.Wpf.dll`
`B5E11053ECFA742232D2E71A932D1DA3A3CCED8387378361AA7982B3668A26E0`,
and `progpu_native.dll`
`19E92448928F791116376E72DB5F3039890C6834044CD36AF68FF2244C747804`.

## Corrective implementation and focused verification

The failed package run isolated two independent causes. ProGPU C++ counted a
legal trailing break-space against the line measure even when the visible word
still fit, while WPF's line service retains that whitespace on the line and
reports it only through `WidthIncludingTrailingWhitespace`. ProGPU commit
`c685d6ed` now tracks visible width independently while preserving the full
line width and source range. With the exact Windows Segoe UI fixture, the
native line ranges changed from `0..40`, `40..80`, `80..120`, `120..137` to
`0..40`, `40..87`, `87..129`, `129..137`; after WPF element boundaries are
included these are the stock line starts `0, 41, 88, 130`.

LibreWPF now retains each source `TextRun` with its physical native style,
uses that run's real baseline and height for `TextRunBounds`, preserves the
default-face baseline ratio for explicit line heights, and maps a hidden final
RTL source edge to the paragraph's physical trailing side. The focused
`PortableTextLineTests` suite passes 26/26. The new native trailing-whitespace
regression passes on macOS ARM64 and in the Windows 11 ARM64 VM after a strict
MSVC native build; the exact Segoe UI shaping probe also reports the corrected
ranges above.

These focused results verify the causes and their implementations. They do not
replace the package-only application gate: a new immutable package bundle
containing both changes must still produce `TEXT_RENDERER NativeMilWgpu` and
pass the complete matrix before this slice is qualified.

## Gate and qualification boundary

`eng/progpu-wpf-windows-native-mil-showcase.ps1` now parses every named case,
requires the complete matrix, compares exact line starts/source counts, and
applies bounded tolerances to sizes and caret geometry. Both Windows x64 and
ARM64 package jobs execute this gate.

The recorded package result remains diagnostic rather than passing
qualification evidence. It proves that the package-only path reached
NativeMilWgpu and identifies the source-layout and caret defects without
allowing a Windows-MIL fallback to pass. The corrective implementation is now
focused-test complete, with the new package run pending. Glyph raster-pixel
identity, additional composite fallback faces, trimming/collapse, rich
document/editor behavior, and macOS/Linux parity remain separate qualification
boundaries after this matrix passes.
