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

The stock and ProGPU processes both used an outer window of `1560x1440`
pixels, client area `1534x1369`, and declared source size `780x720` DIPs.

| Case | Actual size (DIP) | Lines | Source line starts | Positions / end | Final caret (X, Y, H) |
| --- | ---: | ---: | --- | ---: | ---: |
| wrapped-composite | 300.000 x 74.480 | 4 | 0, 41, 88, 130 | 138 / 138 | 44.710, 55.860, 18.620 |
| mixed-runs | 300.000 x 47.883 | 2 | 0, 54 | 80 / 88 | 219.947, 29.263, 18.620 |
| overflow-token | 233.057 x 55.860 | 3 | 0, 8, 49 | 59 / 59 | 59.850, 37.240, 18.620 |
| tabs-whitespace | 300.000 x 37.240 | 2 | 0, 24 | 35 / 35 | 101.967, 18.620, 18.620 |
| explicit-line-height | 260.000 x 75.000 | 3 | 0, 43, 81 | 103 / 103 | 144.003, 50.000, 25.000 |
| bidirectional | 300.000 x 37.240 | 2 | 0, 43 | 63 / 63 | -0.003, 18.620, 18.620 |

Every reported value was identical between stock WPF and ProGPU native MIL,
including mixed-run line starts, the overflow token's expected 233.057-DIP
actual width under a 220-DIP layout constraint, tab/newline source positions,
explicit-line-height caret height, and the RTL final-caret coordinate. Both
processes exited zero. The source-built LibreWPF application DLL hash was
`06F7A2B9B9F9E558BE1A708F67909E37CC821D0EB39E92ACB563873566F28D9E`;
its `PresentationCore.dll` and `PresentationFramework.dll` hashes were
`FD7A55A5EA9B6851B247DBC3635897934F1AD5C7B6E5961BC212F9AA9445A407`
and `F79B65D3BA462042D7FA13CE85A2B1E540A50112936D4CFF24C360FA1703DAC9`.

## Gate and qualification boundary

`eng/progpu-wpf-windows-native-mil-showcase.ps1` now parses every named case,
requires the complete matrix, compares exact line starts/source counts, and
applies bounded tolerances to sizes and caret geometry. Both Windows x64 and
ARM64 package jobs execute this gate.

This proves source layout and caret geometry for the listed cases on Windows;
it does not prove glyph raster-pixel identity, every composite fallback face,
trimming/collapse, rich document/editor behavior, or macOS/Linux parity. A
rendered-pixel comparison is the next diagnostic for reports where text looks
wrongly sized even though WPF layout values agree.
