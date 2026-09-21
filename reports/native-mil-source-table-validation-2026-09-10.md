# Native source-table connection

## Application and implementation

Acceptance is the real source-built RichTextBox using a fixed-column Table,
original paragraph/cell ownership, native rendering and input. ProGPU
38b6a7a4 owns width resolution and row/cell placement; WPF exports actual
Table/RowGroup/Row/Cell nodes, cached source column indices, absolute column
widths, spans and spacing through the existing typed batched adapter.
Paragraphs are formatted only after native cell constraints are available.
The renderer does not clone the document or calculate a second table layout.

Retained navigation annotates the same source block forest. Horizontal rows
select cells by X; ordinary containers select vertical content by Y. Up/down
leaves the whole row instead of entering its neighboring cell and retains X
when entering another row. Original source line order is preserved even when
its Y coordinates are nonmonotonic. Backward line affinity remains within its
actual paragraph; outside-object insertion affinity is retained independently.
Viewport translation occurs once for point, caret and selection queries.

Changed-layout navigation construction is linear in source blocks. Queries
binary-search direct children and paragraph items; no per-query allocation,
text sorting or new CPU rendering fallback is introduced. Existing native
column validation uses ProGPU's SIMD implementation. Ordered source-tree
bookkeeping is not an independent shaping or placement algorithm.

## Evidence and limits

The Release bridge suite passes **1,764/1,764**, zero skips, including the
source-row routing audit. Log: artifacts/wpf-source-table-bridge-tests.log in
the prepared WPF worktree. Source inspection is not runtime qualification.

The source-built macOS ARM64 native host fixture passes with two rows and four
cells, unequal paragraph counts, actual 80/120-DIP columns, cell padding,
selection, cell point hits and vertical navigation in both directions.
It checks backward affinity at a cell start, real scrolling with caret/point
coordinates, width-property invalidation and reflow, edit/undo with the original
source undo system's restored controls and table, native MIL export, detachment,
and explicit rejection of unimplemented object/table pagination.
Logs: artifacts/native-table-scroll-fixture.log and
artifacts/native-table-source-checkpoint.log. The final checkpoint also passes
after preserving outside-object backward affinity, including retention and
same-window device recovery.

Column reflow here means changing the actual TableColumn.Width property.
Mouse-driven table-border resizing, automatic/star/intrinsic columns, row spans,
RTL tables, empty-cell insertion, row/cell fragmentation and full table command
or accessibility coverage remain unimplemented or unqualified. Nested rows and
column spans are supported by the native producer, but this source fixture does
not establish their complete application behavior or Windows pixel parity.

The unchanged full Application.Run build succeeds, but execution still rejects
inline/anchored paragraph content in PortableDocumentParagraphSource.RequireInline.
Its Figure occurs before the table's paragraphs are formatted: passing table
admission does not prove that the full application rendered its table.
Logs: artifacts/application-run-native-table-build.log and
artifacts/application-run-native-table-tests.log. No document node or assertion
was removed to reach this result.

Next is the actual Figure/Floater/InlineUIContainer source path, using shared
ProGPU contracts. Final exact-head package, platform, Windows VM and application
qualification remain required. No renderer fallback, admission gate, tolerance,
or package provenance requirement changed. Broader DirectX/Direct2D/Win2D work
remains deferred in the delivery sequence, not declared complete.
