# Native MIL hit-index producer connection — 2026-09-09

## Latest application connection: DrawingImage logical input

Acceptance remains Toolkit/AvalonDock click and selection. Source inspection
identified that native DrawingImage DrawImage lowering flattens vector content;
indexing those render commands could incorrectly make an ellipse or sparse
drawing, rather than the image destination, the input region. Source WPF's
HitTestDrawingContextWalker.DrawImage declares rectangle geometry independently
of image pixels. This was a source-backed blocker, not a reproduced runtime bug.

ProGPU `2f351eb3` adds source-declared logical rectangle metadata to a balanced
native save/restore scope. Canonical MIL uses it around DrawingImage lowering,
including authoritative empty images. Source state/owner selects the rectangle;
internal transforms, clips and rendering cannot replace it. The scope annotates
existing retained commands rather than adding a fake draw or GPU submission.
Sparse metadata and constant-time restore pairing reuse builder storage; the
existing SIMD placement and canonical hit shader are unchanged. Outer unsupported
mask/guideline contracts remain explicit.

ProGPU `bd197edb` connects the matching managed retained metadata and hit-cache
consumer. LibreWPF's typed product sinks mark the existing image destination clip;
both compact and general snapshots retain its flag. The cache emits one image
rectangle and excludes internal content while preserving outer clip/opacity and
subsequent source ownership. Empty image scopes and direct compositor clip calls
share this policy; unclosed scopes cannot publish an index. Missing source
descriptors remain unsupported. Bounds/diagnostic/native-WPF sinks retain their
ordinary behavior. Follow-up field placement groups the new metadata with existing
boolean fields; no performance improvement is claimed without final measurement.

Authored native fixtures cover source rectangle values, nested/empty scopes,
copied metadata, owner/state isolation, reset, invalid/unbalanced input and
canonical ellipse DrawingImage clearing. Managed fixtures cover compact/general
snapshot retention, internal clips/opacity, empty scopes and product WPF replay.
The shared source provenance/research decisions are in the
[ProGPU design record](../external/ProGPU/docs/native-mil-hit-test-ownership.md).
Agent guidance now preserves these contracts in both repositories.

Build-only checkpoint: the clean isolated native checkout at `2f351eb3` completed
all 75 compile/link steps on each macOS architecture, staging both providers and
six SDK archives per RID. Both commands exited 0 with modules OFF; test/sample
targets compiled, not executed. The separate module consumer remains authored
for the final module-enabled gate. Unrelated working-tree native edits and deleted
performance artifacts were excluded. Fresh origin/main remains
`102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of the feature branch.

The managed ProGPU graph initially compiled with 0 warnings/errors in 79.18s.
The WPF graph exposed an accessibility error in the new sink method; it now uses
an explicit internal interface implementation. That WPF retry compiled with
116 warnings and 0 errors in 29.17s; warnings concern existing WinForms shim,
unused display event and xUnit analyzer sites, not a warning-clean qualification.
The final ProGPU graph at code commit `72cc82fd` compiled with 0 warnings/errors
in 78.10s. Documentation follow-up `85f87239` clarifies the source getter contract.
The final WPF graph against ProGPU `85f87239` compiled with 116 warnings and
0 errors in 29.52s, including the null-Drawing fixture and final typed sink code.
No test/verifier, app, GPU/VM graphics, benchmark or
CI polling ran. macOS staging is `2f351eb3`; Linux/Windows staging and the complete
23-package development feed retain their earlier `da36a718` provenance. Full
native host input, feature freeze, final package consumption and PR CI are still
open; this checkpoint does not enable partial native host coverage.

The source getter audit distinguishes `TryGetPortableDrawingImage` returning
false for Drawing == null from `IPortableDrawingBoundsSource` returning false
for unavailable bounds. The former retains image rectangle input; unavailable
bounds or a true drawing result carrying null remain explicit failures. Product
fixtures include both an empty drawing object and a null Drawing property.

## Latest connection: images and source-ink text

ProGPU `22aa3d5d` continues the same Toolkit/AvalonDock click/selection dependency.
The native producer now records each image destination quad (including coalesced
and explicitly transformed patches) and the actual source ink rectangle of a
glyph run. It reuses canonical image payload parsing and the existing SIMD
rectangle placement; no pixel sampling, readback or per-quad submission is added.
Image patch gaps must remain non-hittable. Native index coverage is still opt-in:
layers/cache/effects, remaining stroke/clip families and host query routing are
not closed by this checkpoint.

Source inspection confirmed WPF point and region walkers use `ComputeInkBoundingBox`
plus baseline for glyph hits, and the destination rectangle for image hits.
Native MIL now passes its source `ManagedBounds` separately from transformed
render/culling bounds. Optional ink metadata is copied synchronously into sparse
builder storage only for owned text commands, grows geometrically, and clears
on reset. It neither enlarges every command nor retains caller pointers.
Missing ink metadata rejects index production; authoritative empty ink emits no
hit. Culling fallbacks, hinted atlas padding and font-size guesses are not used
for text input.

LibreWPF native source capture now requires `HasInkBounds` instead of accepting
legacy estimated size bounds. The existing source-built GlyphRun publishes the
typed ink contract; an older descriptor must supply it or fail explicitly.
The fixture for typed GlyphRunDrawing supplies real contract metadata, and a new
fixture rejects a glyph descriptor without it. The nonidentity-transform case
still supplies ink so it tests its intended independent rejection.

The managed ProGPU hit cache now also retains per-patch destination rectangles
and their local transforms. Provided glyph command bounds use precise rectangle
coverage. Its legacy raw-text/position-only estimates remain explicit unqualified
behavior, not a native fallback. The paired native/managed fixtures include
rotated image patches and gaps, owner boundaries, source ink overhangs, copied
metadata, missing metadata rejection, and canonical MIL text with the existing
Inter SFNT fixture. The public C++ module consumer covers the optional ink
parameter. No third-party implementation text was copied; the shared algorithm
and behavioral source references are in the ProGPU design record.

Both macOS build-only targets at `22aa3d5d` completed 75 steps each, and the
managed ProGPU graph compiled with 0 warnings/errors in 76.52s. The earlier WPF
build handle was no longer available on continuation; its result is not claimed.
The DrawingImage checkpoint above supersedes these macOS inputs and rebuilds the
WPF graph. Older production checkpoints below retain their original provenance.

## Acceptance dependency

Acceptance application: existing Toolkit/AvalonDock. Actions: clicking docked
content, menus/popups and selection. Source-backed blocker: native presentation
uses C++ MIL scenes while retained host owner queries use the managed compositor.
The previous owner-snapshot checkpoint supplied identity, not index production.

ProGPU `0a2acfd4` adds opt-in native MIL index generation for analytic primitives
and plain path fills through the existing reusable C++ builder. No acceptance
application or final gate was reduced. Full application hit coverage and host
query routing remain blocking; this is not a native-input completion claim.
Follow-up `a4100cc8` adds C/C++ flag agreement and cached-visual rejection fixtures.
Both commits are pushed to [ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139).

## Implementation and provenance

MIL records visual handles around their own content, independently of resource
and draw IDs. Sparse builder-owned boundaries preserve signed IDs, exclude
unowned draws and prevent image merging across owners. The encoder retains
analytic/local path data, affine inverses and exact rectangular clip edges, then
uses the existing C++ quadtree/index serializer. It converts semantic fill-rule
numbering to the canonical hit shader's numbering explicitly. SIMD four-corner
placement uses NEON/SSE2; metadata/tree traversal is dependency-bound.

Unsupported draw/state/layer/cache families reject the requested index without
publishing partial coverage. Normal rendering remains unchanged and the LibreWPF
host does not yet select the new option. No per-draw native calls, managed object
pointers, reflection, new shader variant, CPU pixel fallback or readback was added.

ProGPU's [design/provenance record](../external/ProGPU/docs/native-mil-hit-test-ownership.md)
documents original in-repository algorithm sources, primary research references,
costs, paired managed/native applicability and remaining work. Native producer
fixtures cover owners, clips/transforms, fill-rule conversion, reset, unsupported
layers and canonical MIL parent/child resource reuse. A managed factory fixture
uses matched values; the module consumer exercises the public C++ API.

## Build-only evidence

The clean isolated checkout at
`artifacts/native-core-build.KvxVug/progpu` was advanced from `da36a718` to
`0a2acfd4`. Unrelated semantic-render/state edits and deleted performance artifacts
in the working ProGPU checkout were preserved and not incorporated. Fresh fetch
confirmed main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of
the feature branch. The MIL coverage digest was regenerated; verifier execution
remains deferred under the user-directed implementation-first phase.

The existing macOS ARM64 command in the
[payload build record](native-mil-macos-payload-refresh-2026-09-09.md) completed
all 79 incremental compile/link steps with exit 0. It uses `--build-only`,
strict C++20, Apple Clang, modules OFF, the existing target-specific cache and
pinned provider dependencies. Both renderer libraries and six SDK archives were
staged; tests/samples compiled but did not execute. Module-consumer execution and
compilation on a module-enabled toolchain remain in final qualification.

The x64 build at `0a2acfd4` also completed all 79 steps. After advancing the clean
checkout to final `a4100cc8`, both architectures completed another 53 incremental
steps each, including the updated native fixtures, and staged both providers and
six SDK archives. Both commands exited 0. The standalone ProGPU.Tests Release
graph compiled at `0a2acfd4` with 0 warnings/errors in 74.10 seconds; the follow-up
changes only native fixture coverage and a complexity comment.

The managed command was:

```sh
/Users/wieslawsoltes/.dotnet/dotnet build src/ProGPU.Tests/ProGPU.Tests.csproj \
  -c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -v:quiet
```

All build processes are terminal. The two macOS staged RIDs now use `a4100cc8`;
Linux and Windows inputs still use `da36a718`. Existing development NuGet feeds
remain at their documented earlier source versions; staging a native payload
does not produce or qualify a refreshed LibreWPF package feed. No tests, apps,
GPU workloads, benchmarks, VM graphics or CI polling ran. Final Windows/Linux,
module-enabled, package-consumer, runtime and CI qualification remains mandatory.

## Next core work

Complete the producer's text/image and remaining stroke/clip families, preserve
coverage independently of cache/effect shortcuts, and connect point/all-owner/
region queries to the presented snapshot without stale asynchronous results or
managed replay. Keep those changes on the same Toolkit/Showcase acceptance path.
Then freeze and run final platform, package, image/input/lifetime/performance and
exact-head CI gates. Broader Direct2D/Win2D work stays explicitly deferred.
