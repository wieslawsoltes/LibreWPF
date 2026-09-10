# Original anchor source inventory

Acceptance path: the unchanged document application's Figure/Floater declarations,
including anchored elements inside styled Spans. Before width/placement requests,
the source needs ordered anchor identities and their actual document symbol ranges.

`PortableDocumentAnchorSource.Collect` walks the owning Paragraph's inline tree,
retains original Paragraph/AnchoredBlock references and ElementStart/ElementEnd
offsets, and stops at each anchor instead of visiting its child BlockCollection.
Span nesting is bounded at 256 and total inline visits at one million. Foreign,
overlapping or out-of-paragraph symbol ranges fail explicitly. Results are read-only
generation-local metadata; they do not own or clone source document nodes.

Complexity is O(I) inline visits with O(A + D) metadata/stack for A anchors and
nesting depth D. Child block text belongs to the existing independently formatted
anchored subtree. This is source topology, not a layout algorithm or SIMD fallback.

The actual source Figure/Floater fixture now nests each anchor in a Span and
checks identity, exact source ranges and absence of child-paragraph flattening
before the existing narrow/wide subtree measurement checks.

This inventory is a prerequisite, not automatic anchor admission: native sizing,
placement/wrap policy, retained child drawing and TextPointer query ownership must
consume it together. Parent TextSource still rejects AnchoredBlock. Full unchanged
application, viewer, package/platform and CI qualification remain required.

The initial build exposed a collision with an internal Span type; the collector
now explicitly selects System.Windows.Documents.Span. The corrected Release
source build passed in 39.41 seconds with zero errors and one existing IDE0031
warning. The macOS native-host smoke passed, including both nested-span inventory
checks and retained child subtree measurement. This is focused source evidence,
not automatic anchored application layout.
