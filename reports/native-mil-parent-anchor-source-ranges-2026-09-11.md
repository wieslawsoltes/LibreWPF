# Parent anchor source ranges

The acceptance document contains sibling Figure/Floater content inside a parent
Paragraph. Parent shaping must not concatenate those child paragraphs into its
own text. The actual children must remain retained for separate drawing and
document interaction.

The source paragraph adapter now accepts an explicit `PortableDocumentAnchorLayout`
lease. That batch validates its original Paragraph and TextContainer generation;
disposed or changed generations cannot supply source ranges. Binary lookup selects
only recorded original anchor ranges. Parent GetTextRun emits a TextHidden for the
remaining source-symbol range; it never visits child text or manufactures glyphs.
GetPrecedingText skips the same ranges before querying source text, so shaping
context cannot accidentally come from a child paragraph. The actual child lines
remain owned by the batch. Ordinary callers without this explicit ownership still
reject AnchoredBlock, including at its opening source edge.

Range lookup is O(log A) without allocation for A anchors; the source text run
uses the existing hidden-symbol map. Source parent/child ownership, not renderer
type names or a second composer, determines what is shaped. This does not admit
the parent application path: placement/exclusion production and drawing/text-view
routing must consume the batch before normal layout passes it to this adapter.

The focused fixture formats the real parent containing two original anchors. It
checks the full hidden symbol range, preceding text outside both children, native
shaping text containing only the parent prefix, and a TextLine length retaining
all original source symbols. Existing child ownership/sizing checks remain paired.

Validation: source build passed (0 errors, 1 existing warning, 31.48 seconds);
final fixture rebuild passed (0 errors/warnings, 7.38 seconds). The macOS ARM64
native-host smoke passed the new native parent shaping, preceding-text and
unowned-anchor rejection checks, paired child sizing and existing device recovery.
Prepared-checkout local evidence is not exact-head package/platform qualification.
