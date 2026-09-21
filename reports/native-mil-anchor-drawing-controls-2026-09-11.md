# Retained anchor drawing and embedded controls

Acceptance remains the unchanged RealXamlCompilerHarness document through
RealApplicationRunHarness. The document-owned anchored children now participate
in the same retained drawing traversal as ordinary blocks, text and markers.

`PortableFlowDocumentVisual.DrawLayout` draws original child TextLines, block
backgrounds/borders and markers under one frozen translation built from the parent
native block position and anchor content origin. The anchor's own source box is
drawn with its original background, border and padding, excluding margins. Child
content is not copied into a substitute document, reformatted for drawing or
painted as a bitmap. Traversal depth is explicitly bounded and transform scopes
are balanced on failure.

Document layout also publishes anchored UI children through its existing source
host list, retaining actual container and UIElement identities. Their precomputed
document bounds add the parent block/content origin once to the child's own native
bounds. Existing visual attachment, stable-control reuse and desired-size
invalidation paths consume that list. No additional layout tree or per-frame
coordinate conversion is introduced.

The focused fixture adds a real Button in the anchored child, verifies its source
identity and exact document-local bounds, and records a source DrawingVisual
containing parent and child glyph draws. This is drawing/coordinate evidence, not
pixel comparison or a live child-control input test. Child text-query routing and
automatic reference discovery remain required before normal anchor admission;
the full application/package/platform gates remain open.

Validation: source build passed (0 errors, 1 existing warning, 36.89 seconds).
The first glyph assertion incorrectly equated TextLine count with glyph-run count:
the Button-only line is non-ink. The fixture now compares exact retained indexed
glyph-run count with recorded GlyphRunDrawing count and separately verifies the
real control bounds. Its final rebuild passed (0 errors/warnings, 7.54 seconds)
and the macOS ARM64 native-host smoke passed. This remains prepared-checkout
drawing/coordinate evidence, not exact-head package, pixel or other-OS qualification.
