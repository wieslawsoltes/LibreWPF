# Native MIL number-symbol Windows parity

## Stock WPF oracle

The shared `ProGPU.Wpf.TextLayoutParityApp` fixture uses the bundled
`trado.ttf`, a 24-DIP `ar-SA` `NativeNational` TextBlock, and original ASCII
source `12,345.67% 89,012.34%`. Both stock WPF and NativeMilWgpu ran in the
Windows 11 ARM64 Parallels VM at 192 DPI with a real Win32 host. The stock
case ends at X=209.913 DIP; preview.64 NativeMilWgpu ends at X=222.258 DIP.
Each native ASCII percent is 17.262 DIP instead of the stock Arabic-percent
glyph's 11.087 DIP. Stock glyph runs use glyph 266 for `%`, glyph 16 for `,`,
and glyph 18 for `.`; preview.64 native uses 9, 208, and 16 respectively.

Windows `ar-SA` exposes `PercentSymbol` as U+066A U+061C. The one-scalar
`DigitMap` returns the original `%` for this two-code-unit value, so the WPF
portable style published `Percent=0`. ProGPU's packaged native shaper was
independently checked on Windows with the same font: an explicit U+066A and
`Percent=U+066A` have identical glyph/source metadata. The native ABI was not
the failing boundary.

## Source policy and gate

The portable source style now carries WPF's resolved substitution method.
For `NativeNational`, it maps the known U+066A U+061C percent value to the
stock one-ink-glyph U+066A result, while retaining source grouping and decimal
punctuation. Other methods retain their existing symbol policy. The source
text, UTF-16 offsets, physical font, and original-source bidi policy remain
unchanged. This is not admission of arbitrary multi-scalar number symbols:
the U+061C case is tied to the observed Windows `NativeNational` output, and
other unsupported symbol sequences still need explicit contracts.

The Windows native-MIL Showcase gate now includes this case and compares the
flattened glyph IDs in addition to its existing line/caret metrics. The
fixture reports the IDs from each renderer's retained WPF drawing, so a
same-width but wrong punctuation glyph cannot pass.

Validation so far: source `PresentationCore` builds; the complete
`PortableTextLineTests` class passes (59 tests) on macOS; both stock and
preview.64 portable fixture builds and run on Windows; the isolated ProGPU
package test passes. The published preview.64 portable fixture still fails
the new glyph/caret comparison by design. A new package graph and a full
Windows gate run are required before calling this parity case qualified.

Traditional-method grouping/decimal glyph fallback, other cultures' symbol
strings, and bidi cases where a culture mark has semantic effect remain
separate, unqualified contracts. This evidence does not close the full native
MIL or DirectX parity goal.
