# Toolkit header collapse: native/source connection

## Acceptance path and scope

The unchanged `samples/ProGPU.Wpf.ToolkitApp/MainWindow.xaml` top header requests
CharacterEllipsis. Narrowing its window reaches source `Line` drawing, width and
interaction calls into `PortableTextLine.Collapse`. This batch replaces that
path's blanket overflowing-line rejection with a typed native collapsed view.
The dependency is source-backed; the application has not been run in this batch.

ProGPU implementation: `0502e7de`; documentation: `d60f8648` and `c6f8012c`.
Latest fetched ProGPU main is `102e39e5088b462624da6296ff70a43ed2c5d8b4`, already
an ancestor of this feature branch. LibreWPF integrates those commits through its
submodule and source adapter. Both existing PRs remain open delivery work:
[ProGPU #139](https://github.com/wieslawsoltes/ProGPU/pull/139) and
[LibreWPF #115](https://github.com/wieslawsoltes/LibreWPF/pull/115).

## Implementation

- ProGPU's new collapsed-flow C ABI keeps original wrapping width separate from
  final-line collapse width. Zero is a real collapse constraint. The shared native
  forward tab/scale and shaping-safe scan handles character/word prefixes,
  oversized clusters and sign-only output; RTL signs precede retained content.
- The immutable native snapshot maps retained glyph identities to original source
  cluster ends/bidi metadata. The sign is a distinct hidden-range interaction
  item, not the last visible glyph. Earlier wrapped lines remain unchanged.
- Neutral interop carries collapse intent, hidden source range and sign identity.
  The WPF provider keeps the original request and leased font-context domain,
  publishing one immutable cache entry for a repeated collapse request.
- Source WPF formats the actual TextCharacters symbol with its physical faces,
  sizes and brushes through the captured provider, even after registration is
  removed. The placeholder is never rendered. Symbol baseline, bounds, background
  and supported decorations follow source glyph runs; indexed exports retain
  original hidden-source association.
- Original line metrics, source length, newline and continuation state remain
  intact. Collapsed caret/selection uses native range geometry. Re-collapse and
  expansion remain usable after the original TextLine wrapper is disposed.
  Ordinary indexed glyph exports still return retained storage.

Both portable renderer modes use this shared service, not separate composers.
The [ProGPU design record](../external/ProGPU/docs/native-mil-text-collapse.md)
records primary-source research, original in-repository provenance and ownership.
No product reflection, CPU pixel readback or per-glyph interop was introduced.

## Authored qualification coverage — not executed

Native fixtures cover earlier-line stability, LTR/RTL sign placement and source
identity, explicit zero width, oversized single clusters and invalid constraints.
The module consumer fixture records the new public C++ option's legacy default.
Source fixtures cover styled symbol metrics, source ranges, hidden-range hit and
selection, provider removal, wrapper disposal, expansion and glyph export.

The existing native host harness now includes real native styled/tabbed paragraph
collapse for LTR/RTL, wrapped/unwrapped input, character/word trimming, zero width,
cache reuse, interaction and original-snapshot immutability. A supplementary real
source TextBlock uses the literal Toolkit header text and narrow/wide/narrow layout
before native MIL glyph export. It does not replace the full package-mode Toolkit
gate or prove pixel fidelity. Its public reflection is diagnostic-only for the
harness's real/shim side-by-side loading; replace it with direct binding when that
dual-load harness retires. Product formatting and rendering stay typed.

## Compilation evidence

The clean detached ProGPU `0502e7de` macOS ARM64 strict C++20 header configuration
completed 132 compile/link steps (exit 0) for both native providers and text tests:

```sh
cmake --build artifacts/native-core-build.KvxVug/build-osx-arm64 --target progpu_native_text_tests progpu_native progpu_native_dawn --parallel 3
```

The final source/bridge harness compilation completed with 0 warnings and 0 errors
(5.88 seconds):

```sh
./.dotnet/dotnet build src/ProGPU.Wpf.RealPresentationFrameworkHarness/ProGPU.Wpf.RealPresentationFrameworkHarness.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

Earlier build iterations exposed and corrected the fixture's missing direct text
project reference and a signed/unsigned native batch-width mismatch. These were
compilation findings, not runtime failures. Fixtures use the existing Inter font
assets copied into the harness output, not a new system-font assumption.

The final source `PresentationCore.Tests` compilation completed with 8 warnings
and 0 errors (32.58 seconds); this compiles the authored source collapse fixture:

```sh
./.dotnet/dotnet build src/Microsoft.DotNet.Wpf/tests/UnitTests/PresentationCore.Tests/PresentationCore.Tests.csproj --no-restore -m:1 -v:quiet '-clp:ErrorsOnly;Summary'
```

No tests, verifiers, applications, VM/GPU workloads, benchmarks or CI polling ran.
Module and other platform builds remain pending. This focused native build does
not refresh or qualify staged packages. Unrelated ProGPU scene/internal-test edits
and performance-artifact deletions were preserved and excluded from commits.

## Remaining limits and next delivery work

The shaping-safe retained-prefix policy does not reshape at an unsafe contextual
cut; all-script Windows character-ellipsis parity is unproven. Height-driven or
AlwaysCollapsible behavior for a width-fitting line, non-text/multiline symbols,
custom tab stops/leaders and other unsupported source text contracts remain
explicit. Cache misses re-run native shaping/layout and O(G) metadata/interaction
construction; multi-face contexts and source symbol wrappers still allocate.
Ordered topology/prefix stages are dependency-bound CPU work; existing intrinsic
metric and UTF paths remain. No speed or allocation-free reflow claim is made.

Continue the [core delivery queue](../docs/native-mil-core-delivery.md): native
package/startup completion and concrete acceptance-application blockers, then
feature freeze and full runtime/image/lifetime/performance/CI qualification.
Windows payload production and SDK admission remain open; the suspended Parallels
VM obstacle is unchanged. This connection is not package delivery, merge readiness
or completion of the broader DirectX/Direct2D/COM/Win2D goal.
