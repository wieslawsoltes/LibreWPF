# Rich-editor source decoration scopes

## Application dependency

The unchanged real Application.Run document includes Underline, Hyperlink, explicit
line breaks, Figure/Floater, embedded controls and block/table/list content.
After native word-space justification, its first rejection was decorated inline
elements in the linear portable editor. This batch connects those source scopes;
it does not claim full rich-document layout.

## Source ownership

TextBoxLine now emits the existing TextSpanModifier for decorated inline opening
edges and TextEndOfSegment for their closing edges. It uses the same source
decoration collection and foreground semantics as PortableDocumentParagraphSource
and the native-WPF paragraph source. Structural edges retain their original symbol
lengths and are not sent to shaping as fake characters.

Editor lines may be independently formatted after wrapping, scrolling or an
explicit line break. Those requests seed a managed TextLineBreak with the actual
already-open source ancestor modifier chain, outer to inner and bounded to 128
levels. Original element offsets and inside-out property evaluation are retained.
No native LineServices record, substitute paragraph composer or renderer fallback
is introduced. Existing native paragraph range geometry still owns underline
placement and source caret/selection coordinates.

PortableTextLine continues rejecting unsupported custom/animated/non-underline
decorations, pens and continuous mixed-font metric averaging. Directional scopes
and objects also retain explicit rejection. Figure/Floater derive from Inline,
so they now receive an explicit AnchoredBlock rejection rather than being admitted
as an ordinary span with linearly flattened children.

## Validation

The initial source-built native host run passes nested modifier opening/closing,
line-break scope preservation, plain-suffix cleanup and explicit Figure/Floater/
InlineUIContainer rejection. Added continuation coverage formats actual native
lines independently from each source run and checks retained underline geometry.
The complete native host gate passes with that continuation coverage, alongside
existing rendering, geometry, text and same-window device-recovery assertions.

The unchanged Application.Run gate builds and now fails at the actual Figure
contract: "Portable rich-text block and embedded-object layout is not implemented.
Source element: Figure." No document nodes or application assertions were removed.

Evidence logs in the validation worktree:

- artifacts/native-host-rich-decoration.log
- artifacts/native-host-rich-decoration-continuation.log
- artifacts/application-run-decoration-final-build.log
- artifacts/application-run-decoration-final-tests.log
- artifacts/wpf-rich-decoration-bridge-final-build.log
- artifacts/wpf-rich-decoration-bridge-final-tests.log

The complete final Release bridge suite passes 1,756/1,756 with no skips after the
independent-line seed follow-up (20 analyzer warnings, no build errors). Package startup, Windows/
Linux application qualification, anchored/embedded/block rich layout and green
exact-head CI remain delivery blockers. Passing structural scope tests is not
full rich-editor or native text parity.
