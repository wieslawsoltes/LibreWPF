# Original anchored subtree measurement

Acceptance is the unchanged RealXamlCompilerHarness document's first presentation
through RealApplicationRunHarness. The new source entry point is a prerequisite
for formatting its Figure/Floater children; parent paragraph admission remains
closed pending wrapping and interaction ownership.

`PortableFlowDocumentLayout.CreateAnchored` accepts the original document and
AnchoredBlock, verifies shared TextContainer ownership, and passes the anchor's
real BlockCollection through the same native width/arrangement and TextFormatter
pipeline used for the document. No FlowDocument clone, substitute TextBlock or
flattened child string is created. The child generation owns its TextLines and
retains original Paragraph objects and document symbol offsets. Disposal affects
only that generation. The explicit anchored provider is required before layout.

The local child root has no document page padding: the parent retains anchor
margins, padding/border, reference frame and placement. Existing block policies,
lists, tables, real inline/block UI children and unsupported-contract checks stay
in the shared pipeline. Positive content width is still required; zero width is
not converted into unbounded formatting. The returned Size uses existing native
document extent semantics (at least the constraint width), not an intrinsic
shrink-to-content measurement. Completing auto-width policy must retain that
distinction rather than treating Size.Width as a measured child ink width.

The focused source fixture creates actual Figure and Floater child paragraphs,
formats each at two widths, checks reflow and original source ranges, disposes one
generation while retaining another, and rejects a different document owner.
This does not yet test parent-paragraph wrapping, child hit testing through the
viewer, source edit invalidation, native input selection or full application
startup. Those remain required along with the platform/package/CI gates.

## Local evidence

The source-built Release harness compiled with zero errors and one existing
IDE0031 warning (92.40 seconds). The native MIL host smoke passed, including the
new original Figure/Floater subtree checks and all existing rich-document,
inline-control, text, geometry-selection and same-window device-recovery checks.
Its final presentation compiled 23 commands/20 resources/9 draws and submitted
five draw calls. The smoke used the freshly built harness and prepared ProGPU
native libraries; it is not unchanged full Application.Run or published package
qualification. The selected source parent still rejects AnchoredBlock.
