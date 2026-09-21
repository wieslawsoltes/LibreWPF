# Source TextLine exclusion connection

The source formatter now accepts an explicit immutable exclusion request through
`IPortableExcludedTextSource`. `PortableDocumentParagraphSource` has an optional
request constructor; ordinary sources retain their previous formatting path.
The first source segment requires the registered excluded provider and a bounded
width. Native paragraph continuations retain the existing provider output rather
than requerying source exclusions or reconstructing fragment layout.

`PortableTextLine` publishes native fragment row/top/interval and content extents
for the document consumer. TextLine.Start includes the fragment interval and
source alignment; a separate native origin subtracts the already-native interval
offset. GlyphRuns, real inline objects, background/underline rectangles, selection,
caret distances and hit lookup share that origin. This avoids adding the interval
X twice while preserving ordinary paragraph behavior. Native line Y/baseline
normalization remains unchanged and uses the retained provider frame.

The internal source request copies its exclusion memory once, before formatting;
no per-line callback, native re-shaping implementation or source layout algorithm
is added. Provider output must retain one frame per fragment. Intrinsic-width
requests with exclusions and a later hard-segment request without a resolved
segment origin reject explicitly; neither silently formats an ordinary paragraph.
Empty excluded text and native collapse/height-fit constraints remain explicit.

The focused real-source fixture formats one Paragraph through the actual
TextFormatter, mutates the original exclusion input after request construction,
and checks two same-row fragment Start values, source caret distances/round trips
and selection. It does not remove parent AnchoredBlock rejection. Document
placement still must consume fragment positions, and full anchor source ownership,
hard-line origins, wrapping and viewer interaction remain required before the
unchanged application can be qualified.

The first build evaluated before the new fixture file existed and failed to
resolve its call from Program. Rebuilding with all files present succeeded:
zero errors, one existing IDE0031 warning, 62.40 seconds. The native-host smoke
then passed the source excluded-text fixture plus all existing source text,
document, geometry and same-window device-recovery checks. It presented with
23 commands/20 resources/9 draws and five submitted draw calls. This is local
source-built fixture evidence, not full unchanged Application.Run or exact-head
published package/platform qualification.
