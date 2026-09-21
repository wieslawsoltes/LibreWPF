# Positioned paragraph provider connection

`WpfPortableDocumentFlow` now implements `IPortablePositionedDocumentFlow` through
the existing native backend. Sequential spans are cast without copying and passed
to the shared ProGPU block/row arranger. WPF adds no fragment offsets, height
prefixes, alternate row policy or source layout algorithm.

The focused fixture compares the paragraph descriptor's size and every field
offset with the generated native record. It verifies reverse-X fragments on one
row, a vertical clearance gap, source block insets and a following ordinary
paragraph. Invalid extents preserve all prior outputs; short local-position spans
are rejected before native access. Native producer tests separately cover the
same representation inside a table cell.

The source graph advances to ProGPU
`e333972a8abd5db50ab124116dbd6ec245cea348` and LibreWinForms
`ddcecf644e89a6f9b999619cfa3e87b814f0f48a`, with matching nested ProGPU pin.
The Release bridge-test build succeeded with zero errors and 117 existing
graph/analyzer warnings. This is local project-reference evidence, not package
qualification.

All five focused WPF document-flow tests passed under VSTest on macOS ARM64,
with zero skips, against the prepared native libraries. This includes existing
ordinary row/object and anchor-provider cases alongside the new positioned case.

The unchanged application's source TextFormatter still needs an explicit
exclusion request and fragment-aware drawing/input origins. Its document layout
must then consume these explicit local positions instead of the ordinary prefix
path. Anchor source ownership, parent wrapping and child interaction remain
required; the adapter alone does not remove AnchoredBlock rejection or qualify
full Application.Run, Windows/Linux/macOS packages or PR merges.
