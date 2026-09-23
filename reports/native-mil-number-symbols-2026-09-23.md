# Native number-symbol source connection — 2026-09-23

## Change and ownership

LibreWPF retains the original WPF `%`, `,`, and `.` source characters, source
indices, and caret ownership while applying the active `DigitState` culture
policy. The source adapter selects the actual physical face through
`GlyphingCache.GetPortableFontRuns`, checks the preferred mapped symbol in that
face, and uses WPF's defined alternate only when the same face contains it.
The typed paragraph style then carries three optional scalar replacements into
ProGPU's C++ scratch shaping pass. Native bidi can continue to resolve from
original source scalars when the source-bidi flag is set. The managed and native
style records are now 44 bytes and require ProGPU native ABI 5 as a matched pair.

This is a source-to-native text contract, not a replacement for WPF's entire
number formatting, text service, or MIL protocol. Multi-scalar culture symbols
remain unchanged by `DigitMap`; digit alternate-character fallback and a stock
Windows WPF visual/interaction comparison remain open.

## Executed checks

- The ProGPU generated native contract and verifier pass after the ABI change.
  Native text shaping and interop CTests pass, including symbol glyph identity,
  contextual activation, source-bidi distinction, and invalid-scalar rejection.
- The ProGPU focused managed native interop and paragraph-snapshot tests pass:
  147 passed, zero failed. The ABI-5 project-reference package consumer builds
  with zero warnings/errors. Its text-only run passes nine ordinary, continued,
  and inline metadata layouts.
- The full project-reference package-consumer smoke passes on macOS ARM64 with
  both rebuilt ABI-5 native providers and an Apple M3 Pro Metal adapter. It
  exercises native MIL rendering and native owner queries as well as the text
  contracts. This is not a packed NuGet consumer result.
- LibreWPF `PresentationCore.Tests` passes all 55 portable text-line cases on
  macOS ARM64. An isolated test payload in a Windows 11 ARM64 Parallels guest,
  selected with `LIBREWPF_TEST_MEDIA_BACKEND=Portable`, also passes 55/55 using
  the guest's .NET 10.0.12 and WindowsDesktop native prerequisites. This is a
  source-test result, not a Windows native ABI-5 application or package result.
- The real LibreWPF ProGPU text bridge passes its five focused cases on macOS,
  including direct glyph comparison for all three number symbols.

## Remaining admission

The coordinated ProGPU and LibreWPF commits, PR checks, packed ABI-5 artifacts,
Windows native provider build/run, stock-WPF number-symbol comparison, and
final application/package qualification remain required. The ABI-5 change must
not be combined with ABI-4 managed or native packages. No DirectX, Direct2D,
complete MIL, or final release parity is inferred from these checks.
