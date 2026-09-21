# Source positioned paragraph consumption

`PortableFlowDocumentLayout.CreateWithExclusions` snapshots requests keyed by
original source Paragraphs, verifies TextContainer ownership, and requires the
explicit positioned document provider. Unconsumed requests are rejected. The
ordinary document and anchored subtree entry points retain their existing paths.

Formatted source lines retain the provider's fragment descriptor. Their native
paragraph extent is published once per block and actual fragment tops are passed
to ProGPU's shared positioned block/row arranger. Local X remains zero because
TextLine.Start/native-origin mapping already owns the fragment's horizontal
interval. This preserves one coordinate map instead of adding an interval twice.
Following blocks use the native paragraph height, not the sum of fragment heights.
The source still owns original Paragraph/TextLine objects and document offsets.

This path does not implement a source block composer. It copies retained native
metadata in O(L+P) work per changed generation and uses one existing batched native
arrangement call. Ordinary lines have zero local offsets. Unsupported source
line-stacking adjustments reject before publication rather than changing native
fragment row heights. A request for another hard segment still requires its real
resolved segment origin.

The real-source fixture uses an excluded Paragraph with a full-width leading
clearance followed by a central exclusion, plus a following ordinary Paragraph.
It checks a shared first-row Y, the right fragment's single X offset, and the
following paragraph's origin from the retained native content height. This is a
source layout connection, not automatic Figure/Floater collection or admission.
Viewer row-aware hit testing/navigation, parent/child selection, hard-line origins,
source anchor policy and full application/package/platform qualification remain
required before enabling the unchanged application's anchored content.

The initial source build found a missing internal namespace import, which was
corrected. The rebuild succeeded with zero errors and one existing IDE0031
warning (45.11 seconds). The native-host smoke passed the extended source-layout
fixture and existing text/document/geometry/device-recovery checks. Its final
presentation compiled 23 commands/20 resources/9 draws and submitted five draw
calls. These are local source-built checks, not full unchanged Application.Run
or exact-head published package qualification.
