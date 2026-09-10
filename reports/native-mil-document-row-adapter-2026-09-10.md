# Native document row adapter

## Implemented dependency

ProGPU 38b6a7a4 extends its shared document-flow passes with fixed column tracks,
horizontal rows, column-spanning cells, nested rows, cell spacing and tallest-cell
height measurement. Source blocks, original text-line order and measured objects
remain in the existing forest. Cell contents are not flattened into fake text.

The WPF typed adapter now forwards ResolveWidthsWithRows and ArrangeWithRows
using borrowed spans and layout-matched native records. There is no per-cell
native crossing or WPF column/row placement algorithm. ProGPU validates
independent column metrics with NEON/SSE2, computes shared prefixes once and
retains O(blocks + lines + columns + rows + cells) changed-layout work.
No unchanged-frame renderer work or new CPU raster fallback is introduced.

ProGPU's complete native build and all 20 CTest suites pass locally. Its
document contract tests pass 9/9, generated records/exports verify, the standalone
ASan/UBSan document-flow tests pass, and the full default consumer runs successfully
with both native providers through the existing project-reference development lane.
These are component results, not all-RID packages or Windows table pixel parity.
See its docs/native-mil-document-rows-2026-09-10.md for the native geometry contract.

## Required source connection

The unchanged Application.Run acceptance table has 80/120-DIP columns and two
paragraph cells. Source Table admission is deliberately unchanged by this adapter
commit. PortableFlowDocumentLayout still rejects tables: typed service availability
does not make the current Y-ordered text-view lookup table-aware.

The next implementation batch must:

1. Export actual Table/RowGroup/Row/Cell source structure and fixed column policy,
   format original paragraphs at native cell widths, and draw the same generation.
2. Retain per-cell source ranges and use them for point positions and vertical
   navigation. Native line order is source order; it is not globally increasing Y.
3. Preserve selection, caret, edit/undo and source invalidation across cells,
   while keeping pagination and missing layout contracts explicit.

Automatic/intrinsic column widths, row spans, RTL tables and row/cell fragmentation
remain unimplemented. Figure/Floater, inline controls, remaining application
contracts and final package/platform/Windows VM/CI qualification remain open too.
No application assertion, fallback, native input admission or package gate changed.

## Adapter verification

The bridge regression uses the actual 80/120 column widths with unequal line
counts/heights. It checks native constraints, shared cell height, overflowing
extent, distinct X positions and deliberately nonmonotonic source-ordered Y.
No source table application qualification follows from this adapter test.

The Release bridge suite passes **1,763/1,763**, zero skips. Build: zero errors,
117 analyzer/dependency warnings. Logs: artifacts/wpf-native-rows-bridge-build.log
and artifacts/wpf-native-rows-bridge-tests.log. The aligned source pins are
ProGPU 38b6a7a4 and LibreWinForms b7a4eefb; all-RID exact-head CI remains required.
The complete source-built native host and same-window device-recovery gate also
pass against the new producer (artifacts/native-host-document-rows.log), including
the existing rich paragraph/list/block-control actions. Source tables remain
explicitly rejected until their retained interaction path is connected.
