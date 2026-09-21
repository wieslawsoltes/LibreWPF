# Native inline and anchored document dependency

## Current acceptance path and findings

This is an implementation design, not a completed feature or qualification.
The unchanged real XAML application's RichTextBox contains DocumentFigure,
DocumentFloater and InlineActionContainer. Required actions are first presentation,
text selection across and inside anchored content, inline Button interaction,
resize/reflow, source edit/undo and scrolling without losing document positions.
The source-table checkpoint is 8e13a7164; the native producer is 38b6a7a4.

PortableDocumentParagraphSource.RequireInline rejects all three source kinds.
Merely removing that guard cannot work: PortableTextLine.CreateCore admits
TextCharacters and structural runs but rejects TextEmbeddedObject. The neutral
PortableTextParagraphRequest has no object metrics. Native flow entry points
accept styles and tab options, but no measured inline items.

ProGPU already owns an authoritative managed counterpart:
src/ProGPU.Text/StyledTextLayout.cs, last changed by
cdeac8d230aa0754645239062c476240832e7ab3. Its StyledTextInlineBox represents an
actual U+FFFC source unit with width, ascent, descent and a caller tag.
ShapeParagraph keeps it separate from font shaping and preserves bidi level.
Line emission includes box ascent/descent and publishes a separate positioned
box. Reuse these original ProGPU contracts, not third-party implementation text.

The existing native integration points are
src/ProGPU.Native/src/Text/Interop/progpu_native_text_shaping_interop.cpp
(paragraph_layout_core, logical run segmentation, borrowed scratch and output export)
and src/ProGPU.Native/src/Text/progpu_native_text_layout.cpp (shared logical
wrapping and visual placement). The latter currently places baselines using
line_count times options.line_height. Supplying only object advances would
therefore admit overlapping tall objects. Metric propagation must be part of
the shared line-emission contract, not a WPF post-layout Y correction.

## Bounded implementation sequence

1. Extend the shared native paragraph, not a parallel WPF composer. Add a
   generated, fixed-layout inline metric record and additive batched paragraph
   entry point. Validate unique ordered source scalar positions, actual U+FFFC
   ownership, finite nonnegative width/ascent/descent, capacity and reserved
   fields before output publication. Preserve object-free entry behavior.
2. Split shaping runs at declared objects, retaining the full paragraph's
   Unicode bidi and break analysis. Object identity is not a font glyph; use
   explicit item metadata distinct from the tab and collapse sentinels.
   Preserve real cluster boundaries and do not send objects to fallback-font
   discovery, font atlases or glyph export.
3. Carry item ascent/descent into the common line writer, combining text and
   object metrics before baseline and next-line placement. Wrapping, tabs,
   justification, intrinsic widths, visual order and caret/selection must
   consume the same resolved items. Keep exact/minimum source line-height
   policies explicit. Do not grow all lines to the tallest object in a paragraph.
4. Expose immutable positioned-object results through the leased native
   paragraph snapshot and neutral typed service. WPF measures the actual source
   UIElement at the resolved available width, adapts TextEmbeddedObject metrics,
   and retains its real visual lifetime. Source maps keep element edges and
   embedded symbols; selection, caret, background and non-ink glyph filtering
   must use those original offsets.
5. Add anchored block placement/exclusion to shared native document flow.
   Figure/Floater retain their own source block subtrees and anchor positions;
   text flows around the accepted exclusion regions. Resolve width/height,
   alignment, offsets, margins, wrap direction and anchor reference frame from
   source policy. Do not stack anchors as ordinary paragraph children, inject
   their text into the parent string, or discard their source ranges.
   Content measurement and exclusion-dependent line fitting require a bounded
   native layout protocol; a single post-format rectangle cannot implement it.
6. Connect the unchanged application only after those producer/consumer
   contracts exist. Keep unsupported pagination and positioning combinations
   explicit until implemented, not hidden behind a successful inline fixture.

Both portable renderer modes share source formatting and the native text
provider. Native wgpu and Dawn must consume the same implementation. The
existing managed StyledTextLayout supplies differential semantics for common
inline boxes; do not regress its richer existing paragraph/tab features.
No Direct2D/Win2D API expansion is required to make this WPF source path work.

## Research and decisions

Reviewed public contracts and architecture on 2026-09-10:

- [DirectWrite inline objects](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/nn-dwrite-idwriteinlineobject)
  separate metrics, break conditions, overhang and application drawing.
  Adopt that separation through batched neutral values; reject per-item COM
  callbacks across the managed/native boundary.
- [Win2D inline assignment](https://microsoft.github.io/Win2D/WinUI2/html/M_Microsoft_Graphics_Canvas_Text_CanvasTextLayout_SetInlineObject.htm)
  associates an object with text ranges and permits reuse at multiple positions.
  Keep source placement identity separate from reusable object identity.
- [SkParagraph builder](https://skia.googlesource.com/skia/+/refs/heads/main/modules/skparagraph/include/ParagraphBuilder.h)
  exposes placeholders separately from styled text. Adopt distinct measured
  items, not generated whitespace; no upstream implementation is copied.
- [Parley inline boxes](https://docs.rs/parley/latest/parley/struct.InlineBox.html)
  distinguish in-flow/out-of-flow boxes and retain caller identity. Preserve
  that conceptual distinction for inline controls versus anchored blocks;
  ProGPU source indices remain its existing UTF convention, not Rust byte offsets.
- [Vello](https://github.com/linebender/vello/blob/main/README.md) and
  [Parley](https://github.com/linebender/parley/blob/main/doc/concept.md)
  are separate rendering/layout references. Keep retained native layout reusable
  independently of GPU resources; do not introduce a second renderer for objects.
- [WebRender architecture](https://firefox-source-docs.mozilla.org/gfx/RenderingOverview.html)
  separates layout, retained display lists, viewport culling and GPU work.
  Keep scrolling a retained transform and object drawing in existing source
  scenes, not repeated shaping or eager object rasterization.
- [HarfBuzz clusters](https://harfbuzz.github.io/working-with-harfbuzz-clusters.html)
  describe source/glyph correspondence through shaping. Preserve true clusters
  around objects and reject guesses based on codepoint or glyph counts.
- [WPF flow documents](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/flow-document-overview)
  define the source inline and anchored content model; source ownership is
  retained rather than replaced with a different control/document tree.

These sources inform architecture only. ProGPU's own managed implementation
is the code provenance for applicable ports. No new atlas, fallback-font,
variable-font, DPI, hinting or device-loss policy is proposed: preserve the
existing leased contexts, physical faces, retained glyph runs, demand uploads
and device recovery. Do not initialize GPU resources merely to measure a box.

## Correctness and performance acceptance

Native cases must include text/object/text, object-only and zero-size content,
mixed font metrics, tall ascent/descent, consecutive objects, exact-fit/overflow
wrapping, hard breaks, tabs, bidi, justification, intrinsic widths, collapse and
both source affinities. Invalid source positions and undersized output buffers
must not publish partial successful results. Pair the managed/native common
cases and include header/module, generated ABI and both provider builds.

Source cases retain actual controls and source TextPointers through hit testing,
vertical movement, selection, resize, desired-size invalidation, deletion,
undo and detachment. Anchored tests require surrounding text to reflow and
inside-anchor text to remain independently selectable. Run the unchanged
application and exact-head Windows/Linux/macOS package gates afterward.

Reuse bounded paragraph scratch and one object batch per changed paragraph;
no per-object P/Invoke or per-frame measurement. Independent metric validation
and coordinate lanes require intrinsic SIMD. Ordered break selection, prefix
placement and source topology remain dependency-bound CPU work, not GPU
fallbacks. GPU render/compute policy and retained uploads stay unchanged.
Measure final cold layout, reflow, stable scrolling, allocations and boundary
costs; no performance claim follows from this design.
