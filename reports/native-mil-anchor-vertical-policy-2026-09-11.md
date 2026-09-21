# Source floating-element vertical policy

The authoritative source `TextDocumentView.GetPositionAtNextLineInFloatingElements`
searches only the selected floating element and deliberately does not visit sibling
paragraphs when its lines are exhausted. `positionFound` remains true even when an
inner normalized position is unavailable. The outer routine therefore retains
unconsumed line count rather than moving into a different floating element.

Portable text views now select the actual child by original source position and
delegate GetPositionAtNextLine to that retained view. Child ScrollOffset already
includes the parent/content-origin conversion, so suggested X stays in the shared
viewport frame. Partial/zero linesMoved at the child boundary is the source policy,
not an unimplemented global Y-order transition. Earlier checkpoint references to
required cross-anchor Up/Down traversal are superseded by this source evidence.

Automatic-layout trace: TextParagraph.GetAttachedObjects selects FigureParagraph
only when CurrentFormatContext.FinitePage is true. Bottomless Figure content goes
through FloaterParagraph, including its horizontal alignment and wrap policy.
FloaterParagraph always requests delay on no progress and maps stretch Floater to
no side wrapping. Therefore automatic scrolling frame discovery must be selected
explicitly by the source bottomless formatter, not applied indiscriminately to the
paginator or inferred from Figure.VerticalAnchor's metadata default.

Relevant repository sources:

- TextDocumentView.cs, GetPositionAtNextLineInFloatingElements and its column wrapper.
- TextParagraph.cs, GetAttachedObjects.
- FloaterParagraph.cs, GetFloaterProperties, HorizontalAlignment and WrapDirection.

The acceptance application remains unchanged RealXamlCompilerHarness through
RealApplicationRunHarness. Automatic bottomless anchor-line discovery/convergence,
finite-page policies, actual anchored viewer behavior and final platform/package
qualification remain open. This change does not claim full editing or PageUp/Down
parity from the Up/Down source contract.

Validation: prepared source build passed (0 errors, 1 existing warning, 34.29
seconds). Existing macOS ARM64 source-host/viewer regressions passed. The new
anchored vertical delegation remains runtime-unqualified until automatic source
layout enables the real application path.
