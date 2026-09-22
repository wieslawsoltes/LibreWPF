# Source digit substitution qualification — 2026-09-22

## Source contract evidence

The source integration at LibreWPF `eb35550a6` pins ProGPU `b61d01c5` and
connects the original WPF number policy to native context/grapheme resolution
before source physical-font selection. Its `PresentationCore.Tests` assembly
builds in Release. All 53 `PortableTextLineTests` pass on macOS ARM64.

The same source test payload was copied into an isolated Windows 11 ARM64
Parallels guest directory. SHA-256 comparisons verified all 280 original files;
the test assembly hash is
`2decf4d2cafae34367fbdcba2be3897d2778ba1852aa01aed05c8475a76d7871`.
The guest used .NET 10.0.12 ARM64. Installed WindowsDesktop 10.0.12 native
prerequisites were added only to this isolated payload, without changing the
managed assemblies, system runtime, VM configuration, or package caches.

The first invocation used the default Windows MIL selection: 37 tests passed
and 16 existing portable-media facts skipped. Microsoft.Testing.Platform exited
9 because the required minimum of 53 executed tests was not met. That run is
not a passing portable test gate.

A fresh process selected the repository's existing test bootstrap through
`LIBREWPF_TEST_MEDIA_BACKEND=Portable`. The command was the direct test
executable, not VSTest discovery:

```text
dotnet PresentationCore.Tests.dll --filter-class System.Windows.Media.PortableTextLineTests --minimum-expected-tests 53 --no-progress --report-trx --results-directory <isolated-results>
```

This run passed **53/53**, with zero failures or skips and exit code 0. The
source contracts cover original text/indices, mixed number policies, mapped
physical faces/em scales, contextual seeds, hard breaks, combining clusters,
supplementary replacement digits, source continuations, and explicit unsupported
number-symbol/alternate-character cases.

Local evidence is retained under
`artifacts/windows-digit-source.r95nW9/`: the portable TRX and console log,
the full original-payload SHA-256 CSV, installed native-prerequisite inventory,
and the separate default-Windows-MIL result.

## Actual native provider regression

The real WPF adapter test compares ASCII source `123` with forced Arabic digit
substitution against direct Arabic scalar input, using the repository's
Traditional Arabic physical font. On ProGPU `b61d01c5`, glyph identities,
advances, and positions match, but exported bidi levels incorrectly come from
the original ASCII input (0 rather than 2). The equality test remains strict.
This was a native snapshot metadata defect, not permission to weaken caret or
source-cluster assertions. ProGPU `e584e6803` corrects the shared metadata
resolver using the same substituted scratch scalars as shaping. With that
native library and rebuilt managed bridge, the strict real adapter regression
passes, along with context/grapheme forwarding and both source/package graph
guards (4/4). ProGPU's native package consumer separately passes nine
ordinary/continued/inline metadata comparisons against direct Arabic input.
The managed-only follow-up `e29b782b4` explicitly pins the three bidi out
records for each native call, including ordinary requirements/resolution.

The first complete WPF bridge run executed 1,834 cases: 1,831 passed and three Dawn
cases failed because the local search path lacked `progpu_native_dawn`. This
was a missing test prerequisite, not a passing whole-suite result. Building the
exact shared Dawn provider from the same source and rerunning with both native
providers produced **1,834/1,834 passes**, with no failures or skips. The bridge
was rebuilt against `e29b782b4`; its native implementation is unchanged from
`e584e6803`. The full Dawn build, contract test, and exported-symbol check also
pass. This is component/contract evidence, not final package application proof.

## Application and release boundary

The shared Windows text-layout application now adds `national-digits` and
`contextual-digits` to its six existing cases. Both SDK projects stage the
same repository `trado.ttf`; the original strings retain ASCII digits. The
contextual case crosses Latin/Arabic strong characters and a hard break under
RTL paragraph direction. The existing package gate requires all eight cases
and retains every original insertion-position, line-start, extent, and window
geometry comparison against stock Windows WPF.

The stock Windows ARM64 project builds with zero warnings/errors and exits
successfully with all eight reports. Its output font SHA-256 is
`db83f74a512357dc3885ccf53c68a864bd929a1d2d60201234fced8dee624c7c`.
The observed new reference cases are:

| Case | Actual size (DIP) | Lines / source starts | Insertion count / final offset | Final caret X, Y, height (DIP) |
| --- | --- | --- | --- | --- |
| `national-digits` | 240 × 57.6 | 2 / 0, 23 | 33 / 33 | 116.213, 28.8, 28.8 |
| `contextual-digits` | 240 × 57.6 | 2 / 0, 16 | 30 / 30 | 112.083, 28.8, 28.8 |

These are stock-WPF oracle measurements, not ProGPU comparison passes.
The eight-case report, 541 insertion-position rows, stock runtime configuration,
source/output SHA-256 manifests, and clean build log are retained in
`artifacts/windows-stock-eight-case.iE1bv0/`. Parallels screenshots were
incomplete/mostly black; they are not rendered-pixel qualification.

These source tests use a typed test provider and do not prove actual C++
shaping, rendered pixels, or final package startup. The native adapter test,
which now passes, does not replace the expanded Windows application comparison,
immutable dependency alignment,
cross-platform package qualification, and required CI remain separate gates.
Number symbols and source alternate-character fallback remain explicit gaps;
this record does not claim full number-formatting or text parity.

The prerequisite source-preservation correction, LibreWPF PR #152 at
`176939285`, passed all ten PR checks and merged as `788f33ba0`. The digit
integration is rebased onto that exact merge with no content changes. This
closes the missing-provider false-success fix; it does not qualify the new
digit package graph. ProGPU #180 remains the pending native dependency.
