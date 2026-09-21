# Paragraph anchor sizing and ownership

Acceptance remains the unchanged RealXamlCompilerHarness document through
RealApplicationRunHarness. Its sibling Figure/Floater anchors need one ordered
retained generation before parent placement/exclusions can consume their sizes.

`PortableDocumentAnchorLayout.Create` now collects original paragraph anchors,
validates every automatic width policy, resolves initial native widths in one
batch, formats original child BlockCollections, then resolves measured widths in
one second batch. Only children requiring remeasurement are reformatted. It owns
every resulting child layout and the actual source box policy, identity and symbol
range. Failure disposes all created layouts; disposal clears public generation
access and is idempotent. A source container generation change during child
measurement prevents publication. The parent formatter must still supply its
existing property/content invalidation and formatting guard.

This replaces the isolated `CreateAutoSizedAnchored` helper rather than introducing
a second width policy. Native width crossings are exactly two per nonempty
paragraph batch, independent of anchor count. Source collection/storage is O(A+I)
for A anchors and I visited inline elements; child shaping retains its existing
native layout cost. Object ownership is dependency-bound source work, not a CPU
fallback for a data-parallel renderer; native width SIMD stays unchanged.

The source fixture now measures a mixed pair of original Figure/Floater siblings
in every sizing case and checks ordered identity, actual child Paragraph ownership,
effective source alignment, explicit Stretch/Left and separate content/outer size.

Validation: prepared source build passed (0 errors, 1 existing warning, 31.62
seconds). The macOS ARM64 native-host smoke passed all mixed-anchor sizing and
source ownership checks, existing excluded TextLines, and device recovery. The
prepared source graph still carries mirrored changes and older physical dependency
checkouts; this is not exact-head package or Windows/Linux qualification.

Parent anchor admission remains closed. Next connections are source-position
reference resolution, native placement and wrap rectangles, parent shaping without
duplicating child text, and drawing/text-view routing into the same owned children.
Floater's reference cannot be guessed from Figure's ParagraphTop default. Fixed
and relative Figure sizes, empty/exhausted anchors and final platform/package
qualification remain explicit work, not successful omissions.
