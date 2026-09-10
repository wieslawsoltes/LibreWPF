# Native FlowDocument inline controls

## Acceptance and implementation

Acceptance application: unchanged ProGPU.Wpf.ShowcaseApp rich document, including
its InlineActionContainer Button. This batch connects the source paragraph and
document visual ownership path; it does not implement Figure/Floater exclusions.

PortableDocumentParagraphSource exports the actual InlineUIContainer child as a
one-symbol TextEmbeddedObject. Its measurement adapter uses the native resolved
paragraph width and source baseline. ProGPU's existing measured paragraph owns
wrapping, metrics, placement and interaction; no second text composer is added.

PortableFlowDocumentLayout retains source-owned TextRunBounds for inline children
and native block boxes for block controls in one hosted-child collection. Inline
objects remain on the source text navigation path. PortableFlowDocumentVisual
borrows actual children and keeps parents stable across reflow. IContentHost
returns actual child content bounds, distinct from line-height selection bounds.
The existing desired-size invalidation and source edit/undo ownership are shared.
Pagination explicitly rejects all hosted controls until page ownership exists.

## Evidence and limitations

Source build passes with zero errors and one existing source warning. The initial
build caught a missing explicit Compile entry for the new adapter; the project
file now includes it. The expanded native-host gate passes. Its real
RichTextBox fixture covers original inline ownership, measured placement, source
selection/caret boundaries, content rectangles, size invalidation, edit/removal,
undo reconstruction, document detachment and explicit pagination rejection.
Existing block/table/list and native scene/input compilation checks remain.
The existing compiled bridge suite passes 1764/1764 with no skips against this
updated source tree; the source harness above is rebuilt for the new code.
Documentation verification and git diff whitespace checks pass.

Local logs in the prepared source worktree:

- artifacts/document-inline-build.log
- artifacts/document-inline-native-host.log
- artifacts/document-inline-bridge-tests.log
- artifacts/document-inline-docs.log

Both checks use development native libraries with the unchanged native producer
implementation; they are not exact-head package provenance.

This is not full application, package, cross-platform, performance or CI
qualification. Figure/Floater native exclusion layout and remaining release
gates are still required. ProGPU e574a911 and LibreWinForms 82d595d3 remain pinned;
this batch only adapts the already implemented shared native inline contract.
