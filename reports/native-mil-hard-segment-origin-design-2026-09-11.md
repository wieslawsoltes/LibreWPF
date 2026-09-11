# Excluded hard-segment origins

Acceptance remains the unchanged RealXamlCompilerHarness through
RealApplicationRunHarness. The required action is wrapping and navigating text
before and after an explicit LineBreak beside real Figure/Floater content.

## Confirmed blocking chain

At ProGPU e333972a, `try_layout_excluded_logical_shaped_text` initializes its
double `top` to zero. `progpu_native_text_exclusion_options` is a 16-byte record
containing size, attempt budget and two reserved uints; there is no origin field.
The shared shaping interop invokes that fitter without a segment origin. Its
placement tops and result height therefore belong to a segment starting at zero.

Source `PortableDocumentParagraphSource.GetExclusions` rejects a new initial
formatting request after Start. Wrapped continuations correctly retain the prior
native paragraph; a hard break starts another native paragraph. Independently,
`PortableFlowDocumentLayout.FormatParagraph` records only the first native
paragraph's extent with `recordedExtent`. Merely removing the source rejection
would overlap later segments and understate the containing document block height.

## Connected implementation contract

1. Add an explicit native segment-origin entry point/options contract without
   repurposing old reserved fields or changing existing zero-origin calls. Carry
   a finite nonnegative double origin through requirements and layout. Reject
   origins whose float band cannot represent a positive next line height.
2. Start the shared native fit at that origin against unchanged paragraph-local
   exclusions. Preserve double tops, native baseline/caret float frames, attempt
   bounds and native measurement. Publish the absolute bottom separately from
   the origin; do not subtract and re-add rounded coordinates in the source.
3. Carry the optional capability through generated ABI records, both managed
   providers, retained snapshots and the neutral source provider. Ordinary
   zero-origin formatting stays compatible. Missing capability fails explicitly.
4. The source paragraph formatter supplies the previous native segment's bottom
   only after its last wrapped fragment. Preserve cached continuations and source
   modifier scopes; never advance the origin for each same-row fragment.
5. Accumulate the containing block's native bottom and maximum content width
   across hard segments. Retain each segment's actual fragment frame. Physical
   caret movement stays generation-local and crosses hard segments through the
   source viewer's block/line ownership, not reused native caret indices.

Required evidence: two hard segments across a central exclusion; a break after a
cleared gap; wrapped first segment; exclusion ending inside the second segment;
source offsets/affinities around LineBreak; Up/Down across the segment boundary;
following-block advancement; invalid origin/budget atomicity; both providers.
Empty consecutive hard segments also require native empty-row placement, which
the current excluded snapshot rejects. Do not hide that case with an ordinary
paragraph or fabricated glyph.

This is a verified dependency/design checkpoint, not an implemented origin API.
Automatic anchor collection/ownership, full application qualification and the
final package/platform/CI gates remain required.
