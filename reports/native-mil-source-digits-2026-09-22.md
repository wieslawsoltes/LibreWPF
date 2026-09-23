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

## Pause checkpoint

Work stopped at the user's request after the following additional checks. These
are source-build diagnostics, not completion of the goal or package qualification.

- The real macOS ARM64 PresentationFramework harness rebuilt at LibreWPF
  `99dfbe2c0` / ProGPU `e29b782b4` with zero errors and two existing warnings.
  `eng/progpu-wpf-native-mil-host-smoke.sh` then exited zero: retention, text and
  document interaction, native presentation, device recovery, and native memory
  completion checks passed. The single instrumented frame is not a performance
  benchmark. Logs remain in `/tmp/librewpf-text-review.6OsyqD/` as
  `native-source-harness-build.log` and `native-source-harness-tests.log`.
- ProGPU Build run `35694517582`, Ubuntu job `106638345331`, reported 3,808
  allocated bytes instead of zero in
  `EmptyClipStateCyclesAllocateNothingAfterWarmup` (4,636 passed, one failed,
  seven skipped). The unchanged assertion passed in five fresh local Release
  processes and its entire 11-test class with `DOTNET_TieredCompilation=0`.
  Those macOS results do not explain or qualify the Linux failure. An attempted
  job rerun while the workflow was still active was refused; no successful rerun
  is claimed. Local evidence remains in ProGPU's
  `artifacts/skia-clip-allocation-diagnostic/`.
- The private Windows ARM64 eight-case source diagnostic compiled successfully
  in 6.75 seconds (zero errors, one existing CS1591 warning). SHA-256 checks
  matched 883 staged inputs. Its application host is ARM64 and runtime
  configuration requests `NativeMilWgpu`; the unchanged application used an
  isolated SDK and `LocalArtifacts` references. It was **not run** and no native
  payload was staged. Managed metadata inspection found five absent assemblies:
  `System.Private.Windows.Core`, `System.Printing`, `Accessibility`,
  `System.Drawing.Common`, and `WebGpuSharp`. Resolve the actual dependency
  closure before startup; do not treat compilation as runtime success.
  Evidence remains under `artifacts/windows-native-eight-case.Jj9fbZ/`, including
  build output, input/output hashes, runtime configuration, dependency metadata,
  and `missing-managed-references.csv`. The host-built PresentationCore uses
  PortableTextInterface, not the Windows-produced C++/CLI package graph.

Resume with the unresolved Ubuntu allocation gate, exact-head native payload and
Windows application comparison, then aligned immutable ProGPU/LibreWinForms/WPF
dependencies and final package CI. Broader deferred API parity remains outside
this text checkpoint. No additional validation or merges were started on pause.

## 2026-09-23 Windows ARM64 source diagnostic

ProGPU #180 merged as `5b0a20c6` after its unchanged Ubuntu allocation test
passed on rerun and the dependent Windows checks completed. The isolated Windows
ARM64 eight-case app was then supplied with hash-verified `e29b782b4` native
DLLs, existing managed dependencies and the ARM64 WPF native helper. It selected
and reported the live `NativeMilWgpu` host, exited zero and emitted all eight
cases with insertion positions. This is a mixed source diagnostic: the
host-built PresentationCore uses PortableTextInterface and the supplemental
WPF helper/dependency assemblies come from published preview.45 and the
installed Windows runtime. It cannot qualify the immutable preview.63 package.

The shared comparison rules pass the six previously covered cases. They reject
both new digit cases:

| Case | Largest insertion X difference | Insertion metric failures | Final caret X, stock / native |
| --- | ---: | ---: | ---: |
| `national-digits` | 128.798 DIP | 26 | 116.213 / 110.203 DIP |
| `contextual-digits` | 152.163 DIP | 6 | 112.083 / 0.000 DIP |

Both digit cases retain two lines, source starts and total dimensions; those
equalities do not satisfy caret or insertion parity. The outer/client windows
also differ by one pixel in height, within the existing one-pixel gate tolerance.
Evidence is retained under `artifacts/windows-native-text-e29-arm64/`: stock and
native reports, exact native DLLs, staging hashes and the comparison script.
The native text source and/or source interaction mapping must be corrected, then
the clean Windows package gate must compare all eight cases and 541 positions.

ProGPU `9774cff2` adds an explicit source-bidi digit policy: WPF preserves
original ASCII digit bidi levels while shaping the culture-specific digit
glyphs. LibreWPF selects that policy for substituted source runs. The native
C++ regression, targeted ProGPU managed tests and 112 LibreWPF source/adapter
tests pass on macOS. This change is not yet Windows-oracle or package-qualified;
the same eight-case gate must be rerun on the exact final build before release.

## 2026-09-23 shared-font oracle correction

The first eight-case application comparison above did **not** use the staged
Traditional Arabic font despite both projects copying it. A stock-WPF
`GlyphRun.GlyphTypeface.FontUri` probe showed `SEGOEUI.TTF` for both digit
cases. `new FontFamily(fontFileUri, "#Traditional Arabic")` resolves that bare
family reference against Windows Fonts; it does not select the URI file. Thus
the 116.213/112.083 caret references and the corresponding insertion deltas
are invalid as an oracle for the intended bundled-font fixture. They remain
historical diagnostic output, not release parity failures.

The shared fixture now puts the absolute file URI into the family reference:
`file:///.../trado.ttf#Traditional Arabic`. A fresh stock-WPF ARM64 glyph probe
verified `GlyphTypeface.FontUri` is the staged `TRADO.TTF` and that substituted
digits use the same `257..265,256` glyph sequence as the ProGPU formatter on
that font. A fresh stock-WPF eight-case application built from the corrected
shared source and exited zero with all eight reports. Its corrected references
are:

| Case | Actual size (DIP) | Lines / source starts | Final caret X, Y, height (DIP) |
| --- | --- | --- | --- |
| `national-digits` | 240 × 71.740 | 2 / 0, 23 | 110.867, 35.870, 35.870 |
| `contextual-digits` | 240 × 71.740 | 2 / 0, 16 | 105.907, 35.870, 35.870 |

The corrected stock report is retained in
`artifacts/windows-native-text-977-arm64/corrected-stock-output.log`.
This is still only the stock side: the corrected shared fixture must run in an
exact ProGPU/LibreWPF package graph and pass the complete insertion-position
comparison before release. Do not compare the corrected stock report against
the old Segoe-based portable report or claim parity from matching glyph IDs.

The corrected stock contextual case has a final insertion X of 105.907 DIP,
not the paragraph's left edge. The WPF portable line had forced terminal RTL
non-ink carets to that left edge (x=0) even when an LTR digit run ended the
paragraph. The retained native paragraph exposes the run's physical caret at
33.258 DIP within a 139.160-DIP line; WPF's logical mirror places it at
105.902 DIP. The source adapter now consumes that native affinity instead of
forcing x=0. A new typed source regression verifies this path, and all 54
`PortableTextLineTests` pass locally. A real WPF/ProGPU adapter regression using
the bundled font also matches the corrected stock final X within 0.1 DIP.
These component tests do not substitute for the full eight-case exact-package
Windows comparison.

## Immutable preview.63 source alignment

ProGPU PR #182 merged as `36b388685` and its annotated
`v0.1.0-preview.63` tag points to that same main commit. LibreWPF's release
workflow requires its ProGPU submodule to equal the tag commit exactly; the
earlier `9774cff2` text commit is an ancestor but does not satisfy that
provenance check. The WPF submodule is therefore advanced to `36b388685`.
LibreWinForms PR #44 carries the same ProGPU pin; LibreWPF pins its focused
`06dcdd6dd` Forms source commit while that PR qualifies. The final package
gates must run again on these exact pointers before publishing LibreWPF.
