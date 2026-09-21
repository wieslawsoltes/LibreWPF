# Source excluded hard segments

Acceptance remains the unchanged document application through Application.Run:
explicit source LineBreak content must continue beside the same anchor exclusions.

The WPF provider implements ProGPU's optional IPortableSegmentedTextFormatting
capability through CreateWithExclusionsAt. All native styled-face paths receive
the same origin; original exclusion calls retain the zero-origin entry point.
Source PortableTextLine requests require this capability for a nonzero origin.
They do not shift glyphs, inline objects, caret stops or selection rectangles.

PortableDocumentParagraphSource owns the current hard segment's source start and
immutable origin request. The document formatter advances that request only when
the native paragraph's last wrapped fragment has been consumed. Modifier-scope
continuations remain on the existing TextLineBreak path. Native absolute tops and
the maximum content width/final bottom are retained across segments in the single
positioned paragraph descriptor used by document placement.

The source fixture adds an actual LineBreak and text Run to the original paragraph,
then checks the next segment's native origin and following paragraph advancement.
Empty excluded segments remain explicitly unsupported; this does not admit
automatic Figure/Floater collection, wrap-side policy or anchor child ownership.
Full viewer interaction, unchanged application and package/platform gates remain.

Dependency graph: ProGPU 9449ef8026e9542884758cdf50aca476e194e19b and canonical
LibreWinForms f4e1a558a3d441cef6aec0e11aead3933ae414c8. Prepared source compilation
uses those ProGPU sources with the locally rebuilt native payload; it is not
exact-head packaged artifact qualification.

The prepared Release source build passed in 1:42.41 with zero errors and two
existing warnings (CS0067 and IDE0031). The macOS native MIL host smoke passed,
including the real source hard-segment/following-block checks and existing source
document, input and same-window device-recovery regressions. This is focused host
evidence, not unchanged application, empty-segment or packaged runtime qualification.
