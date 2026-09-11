# Source fragment integration boundary

Acceptance remains the unchanged RealXamlCompilerHarness document through
RealApplicationRunHarness: first presentation, wrapping beside Figure/Floater,
selection across and inside their original child paragraphs, caret movement,
resize/reflow and scrolling. This report records an implementation dependency
found after source subtree measurement, not completed source anchor support.

## Verified current paths

`PortableTextLine.CreateCore` currently calls only ordinary `Format` or
`FormatInline`. Its continuations retain that paragraph and expose one native
line index at a time. The optional excluded provider is therefore not reachable
from source TextFormatter yet.

`WpfPortableTextFormatting.Paragraph` deliberately retains paragraph-local glyph,
caret and selection X coordinates. For excluded paragraphs its line Y comes from
the explicit fragment top, not a height prefix. Multiple fragments can share one
row. `PortableTextLine` currently adds Start to native glyph, object, background,
underline, selection and caret coordinates; hit lookup subtracts Start. Setting
Start to a fragment's Left without adjusting this common native origin would add
the exclusion offset twice. A fix restricted to glyph drawing would disagree
with input and decoration geometry.

`PortableFlowDocumentLayout.FormatParagraph` currently appends every TextLine's
advance to the ordinary native block-flow line array. That contract prefix-sums
line heights. Feeding same-row excluded fragments into it would stack them and
lose vertical clearance. The existing text view reads Layout.Positions for both
drawing-aligned distance lookup and line rectangles, so a view-local Y correction
would create a second geometry authority.

## Required connected implementation

1. Give source formatting an explicit immutable exclusion request for its actual
   paragraph generation. Resolve it before the initial provider call, retain it
   through continuations and require `IPortableExcludedTextFormatting`. Do not
   silently ignore it during unsupported intrinsic/trimming paths.
2. Expose the native fragment's row, top and interval through the source TextLine.
   Keep TextLine.Start/Width semantics distinct from the native paragraph origin.
   Apply the same origin to glyphs, objects, backgrounds, underlines, selection,
   caret distances and hit lookup exactly once.
3. Extend shared native document placement to consume explicit paragraph-local
   fragment positions/extents, rather than prefix-summing fragment heights.
   Preserve source order even when physical X differs; no WPF-local line composer
   or post-layout stacking correction may replace that contract.
4. Let the retained source layout publish one position map for rendering and
   interaction. Native fragment caret navigation must own physical same-row and
   cross-row movement. Anchor child queries retain original TextPointers and
   their independently formatted subtree, not flattened parent character offsets.
5. Connect actual source anchor collection, width remeasurement, native placement
   and wrap-side exclusion production only once these consumers agree. Keep
   `RequireInline` rejection until that connection exists. Removing it earlier
   would feed child block edges into the parent TextSource as if ordinary inlines.

Focused acceptance must include two fragments on one row, a cleared vertical gap,
left/right anchors, source offsets before/after an anchor, RTL text in the admitted
LTR block flow, inline objects beside exclusions, selection/caret round trips and
reflow invalidation. Existing provider/native fixtures prove some prerequisites
but do not prove this source/application integration. Package/platform and final
CI gates remain separate and required.
