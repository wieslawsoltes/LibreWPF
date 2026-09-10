# Showcase ordinary line input — 2026-09-09

## Acceptance dependency

The package Showcase's `ShowcaseShapeLine` in `samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml`
has endpoints (16,98)/(154,76), thickness 4 and round caps. Its source MIL
`append_resolved_line_stroke` emits DRAW_GEOMETRY for an undashed line. The C++
input producer rejected that command family before reading its line geometry.
This is a source-backed application blocker, not a runtime reproduction.

## Implementation

ProGPU `abb8e4f2` connects ordinary nondegenerate geometry-line commands to the
existing canonical LineStroke hit payload. Endpoints, cap kinds, thickness,
local/outer transform, source owner and actual state clip are retained. Direction
metadata uses explicit NEON/SSE2 lanes and the existing intrinsic four-corner
placement; queries continue using the canonical GPU implementation.

The paired managed/native encoders also correct square-cap broad-phase pruning:
a diagonal square corner can extend farther than half the stroke width on one
axis. Conservative padding now includes that corner; it does not become the
hit shape or change the actual cap query. No WPF-local stroker, extra submission,
pixel readback, shader fork or source rendering change is introduced.

The source `progpu_native_mil.cpp` file is unchanged, so its coverage source digest
does not change. No public ABI/module signature changes. Existing module and
cross-platform qualification gates remain mandatory.

The native builder fixture covers all 16 cap pairs, signed owner identity,
nonidentity placement, clipping and direction data. Canonical scene 9813 encodes
the actual Showcase line. Matched managed fixtures cover those values and the
diagonal square-cap envelope. Degenerate/tiny lines at the canonical shader's
0.0001 threshold reject instead of borrowing its generic disk behavior; directed
point caps retain their separate unfinished contract.

Original in-repository provenance, primary engine references, semantic limits
and complexity are in [ProGPU's design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md).
The new line encoding is fixed work per line with no stroke-outline allocation.
No measured speed or output-parity claim is made before final qualification.

## Build-only evidence

The ProGPU Release test graph compiled with 0 warnings and 0 errors in 76.28s.
No tests ran. The clean isolated native build checkout was advanced to `abb8e4f2`,
excluding the unrelated four modified native files and deleted performance
artifacts in the main submodule. Both macOS ARM64 and x64 builds completed all
53 compile/link steps with exit 0, staging both providers and six SDK archives
per RID. Modules were OFF; test/sample targets compiled but did not execute.
Only the explicit `--build-only` lane was used. A fresh fetch leaves main at
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, already an ancestor of the feature
branch. ProGPU `abb8e4f2` is pushed to PR #139.

The final WPF Release graph compiled against `abb8e4f2` with 116 warnings and
0 errors in 28.82s. These warnings are recorded, not suppressed or treated as
green CI. Windows/Linux native payloads and the complete 23-package feed
remain at `da36a718` pending the final platform/package refresh. No applications,
VM graphics, verifiers, benchmarks or CI polling ran in this batch.

## Remaining core input work

This closes the identified producer rejection for the Showcase's ordinary line; it
does not enable partial native hit coverage in WPF hosts. Geometry kinds other
than lines, stroke batches, curves/arcs, directed caps/joins, geometry clips,
cache/effect families and native host query routing remain open. The Showcase's
closed stroked `ShowcaseShapePath` is the next concrete source path to trace through
the existing shared stroke preparation. Broader Direct2D/COM/Win2D expansion
remains deferred while application closure continues.
