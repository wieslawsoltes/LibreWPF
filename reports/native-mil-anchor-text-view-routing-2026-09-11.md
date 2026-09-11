# Retained child text-view routing

Acceptance: select and inspect original Figure/Floater text in the unchanged
RealXamlCompilerHarness document through RealApplicationRunHarness.

The existing PortableFlowDocumentTextView now creates generation-cached child
views over the actual owned child layouts. No second paragraph composer or text
copy is introduced. Each child borrows its parent validity and checks both parent
layout identity and child disposal. Its source range remains the original anchor
content; pointer normalization is clamped to that range.

Point queries select child content in reverse drawing order, then reuse the same
line/table/object lookup. Child scroll conversion includes the native parent
block/content origin exactly once. Caret rectangles, caret-boundary checks and
line-range queries route by original source offset to the child view. Selection
rectangles omit hidden child source ranges from parent text and append actual
child rectangles translated into the parent document frame. Existing viewport
clipping remains in the outer tight-geometry operation.

Source-offset child selection is binary over retained source order. Point queries
scan retained child bounds; view objects are created only for a new layout
generation, not for every point query. Selection retains its normal output-list
allocation. No native readback, synthetic layout rectangles for text, or managed
reshaping is added.

This connection is not complete anchor interaction: cross-boundary caret/line
navigation, indexed glyph export and automatic source frame/convergence admission
remain required. Anchored-viewer runtime checks must use the real normal application
path; compilation and existing ordinary viewer regressions do not qualify the new
branches. Package/platform/CI gates remain open.

Validation: prepared source build passed (0 errors, 1 existing warning, 35.85
seconds). The macOS ARM64 source-host regression passed, including ordinary rich
document viewer hit/navigation/scrolling and retained anchor drawing. The new
anchored child-view branches are compiled but not runtime-qualified by that run.
