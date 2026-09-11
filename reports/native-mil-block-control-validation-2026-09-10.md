# Native document block-control connection

## Acceptance and implementation

The acceptance case is a real source RichTextBox using the shared native
FlowDocumentView: show a BlockUIContainer between formatted paragraphs, resize
its child, select and edit the document, undo, and replace the document.

ProGPU e1981183 includes the additive ArrangeWithObjects contract introduced in
08a23ba7. WPF measures the actual source child at the native width constraint
and passes its desired size in borrowed typed spans. C++ resolves its content
box together with paragraph lines, insets and adjoining margins. No fake text
line, cloned UI tree or WPF-local block-placement algorithm is introduced.
LibreWinForms a611be5a pins the same ProGPU commit.

The drawing visual borrows the child without changing its logical source owner.
Stable children retain their visual parent during reflow; source order changes
use the existing visual collection move operation. Desired-size changes invalidate
the live text view before the next layout generation. Deletion, failed drawing
attachment and document detachment release borrowed visual children.

Object source symbols remain separate from shaped clusters. A lazy mixed-item
index refers to retained TextLines or real block controls. Point positions,
caret and vertical movement, selection rectangles and line ranges use the
native placements and original TextContainer offsets. Ordinary paragraph-only
documents do not allocate the mixed-item index. Index construction is linear;
point/source lookup is logarithmic, and object selection scans only the overlapping
range after a binary search. Source UI Measure/Arrange is not a new CPU renderer
or compute fallback; bulk placement stays in ProGPU's native flow implementation.

## Source undo ownership

The first fixture incorrectly expected the deleted UIElement instance to return.
The existing source TextTreeDeleteContentUndoUnit saves an embedded element as
XAML and deserializes it on undo. The renderer must borrow the restored source
child, not keep the deleted instance attached or create its own replacement.

The fixture now checks the restored source Button type, content and height,
its actual BlockUIContainer.Child identity, original TextContainer ownership,
new drawing attachment and continued detachment of the deleted control. The
restored document also compiles a complete native MIL hit index. No source undo
implementation, placeholder behavior or serialization policy was changed.

## Evidence and limits

The source-built macOS ARM64 native host gate passes, including retention,
paragraph/section/list/block-control layout, source point positions, caret and
selection, native MIL drawing/input compilation, desired-size reflow, source edit,
undo, detachment and same-window device recovery. The final fixture also verifies
that pagination explicitly rejects the object without attaching it. Log:
artifacts/native-host-block-objects-final.log (zero build errors, one warning).

The unchanged full Application.Run harness builds, then explicitly rejects:
"Portable document tables require native column, row and cell layout."
Logs: artifacts/application-run-block-objects-build.log and
artifacts/application-run-block-objects-tests.log. No acceptance document nodes
or assertions were removed. Tables, Figure/Floater and InlineUIContainer remain
core application blockers; this source fixture does not qualify all control
events, focus, scrolling or package-mode application interaction.

Pagination rejects block controls before line-only fragmentation, preserving the
missing object fragmentation/page-visual ownership contract. Object-leading list
markers require a baseline contract and remain explicitly rejected. Neither this
connection nor a compiled native index admits those missing behaviors.

The Release bridge suite passes **1,762/1,762**, zero skips, including typed
native object placement and invalid-argument admission. Build: zero errors,
117 analyzer/dependency warnings. Logs: artifacts/wpf-block-objects-bridge-build.log
and artifacts/wpf-block-objects-bridge-tests.log. All-RID exact-head
CI, package-mode applications, Windows VM comparisons and remaining platform
qualification are still required before the ordered PR merges.
