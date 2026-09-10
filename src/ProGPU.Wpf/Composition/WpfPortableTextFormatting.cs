using System.Buffers;
using System.Runtime.CompilerServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using ProGPU.Text;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed class WpfPortableTextFormatting : IPortableExcludedTextFormatting
{
    private static readonly WpfPortableTextFormatting Default = new();
    private sealed class FontState(PortableTextFont source)
    {
        internal TtfFont RenderFont { get; } = new(source.Data.ToArray(), checked((int)source.FaceIndex));
        private readonly Lazy<NativeTextShapingContext> _context = new(() => new(source.Data.Span, source.FaceIndex));
        internal NativeTextShapingContext Context => _context.Value;
    }
    private readonly ConditionalWeakTable<PortableTextFont, FontState> _fonts = new();
    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureTextFormatting(Default);

    public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        => FormatCore(in request, null, null);

    public IPortableInlineTextParagraph FormatInline(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> styleMetrics, ReadOnlySpan<PortableTextInlineObject> inlineObjects)
        => FormatMeasured(in request, styleMetrics, inlineObjects);

    public IPortableExcludedTextParagraph FormatExcluded(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> styleMetrics, ReadOnlySpan<PortableTextInlineObject> inlineObjects,
        in PortableTextExclusionOptions options, ReadOnlySpan<PortableTextExclusion> exclusions)
    {
        var rectangles = new NativeTextExclusionRectangle[exclusions.Length];
        for (int i = 0; i < rectangles.Length; i++)
        {
            var r = exclusions[i];
            rectangles[i] = new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        }
        return (ExcludedParagraph)FormatMeasured(in request, styleMetrics, inlineObjects,
            new NativeTextExclusionOptions { MaximumAttempts = options.MaximumAttempts }, rectangles);
    }

    private IPortableInlineTextParagraph FormatMeasured(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> styleMetrics, ReadOnlySpan<PortableTextInlineObject> inlineObjects,
        NativeTextExclusionOptions? exclusionOptions = null, ReadOnlySpan<NativeTextExclusionRectangle> exclusions = default)
    {
        if (styleMetrics.Length != request.Styles.Length || (!request.Text.IsEmpty && request.Styles.IsEmpty))
            throw new ArgumentException("Inline paragraphs require explicit styles and matching source metrics.");
        var metrics = new NativeTextStyleMetrics[styleMetrics.Length];
        for (int i = 0; i < metrics.Length; i++)
            metrics[i] = new() { Ascent = styleMetrics[i].Ascent, Descent = styleMetrics[i].Descent };
        var objects = new NativeTextParagraphInlineObject[inlineObjects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            var item = inlineObjects[i];
            objects[i] = new(item.Position, item.Width, item.Ascent, item.Descent);
        }
        return (InlineParagraph)FormatCore(in request, null, null, true, metrics, objects, exclusionOptions, exclusions);
    }

    private Paragraph FormatCore(in PortableTextParagraphRequest request, Paragraph? original, PortableTextCollapseRequest? collapse,
        bool inline = false, ReadOnlySpan<NativeTextStyleMetrics> metrics = default,
        ReadOnlySpan<NativeTextParagraphInlineObject> objects = default,
        NativeTextExclusionOptions? exclusionOptions = null, ReadOnlySpan<NativeTextExclusionRectangle> exclusions = default)
    {
        if (request.Font == null || request.Font.UnitsPerEm == 0 || !float.IsFinite(request.FontSize) || request.FontSize <= 0)
            throw new ArgumentException("A real source face and positive em size are required.");
        var font = _fonts.GetValue(request.Font, static font => new(font));
        var options = new NativeTextParagraphOptions(request.FontSize / request.Font.UnitsPerEm,
            request.MaximumWidth, request.LineHeight, Alignment: request.Alignment switch
            {
                PortableTextAlignment.Left => NativeTextAlignment.Left,
                PortableTextAlignment.Right => NativeTextAlignment.Right,
                PortableTextAlignment.Center => NativeTextAlignment.Center,
                PortableTextAlignment.Justify => NativeTextAlignment.Justify,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            });
        if (!request.Styles.IsEmpty) return FormatStyled(in request, in options, font, original, collapse, inline, metrics, objects, exclusionOptions, exclusions);
        var features = new NativeTextFeature[request.Features.Length];
        for (int i = 0; i < features.Length; i++) features[i] = new(request.Features.Span[i].Tag, request.Features.Span[i].Value);
        return CreateParagraph(request, CreateNative(font.Context, request, options, features, [], original, collapse, inline, metrics, objects, exclusionOptions, exclusions), [font.RenderFont]);
    }

    private Paragraph FormatStyled(in PortableTextParagraphRequest request, in NativeTextParagraphOptions options, FontState primary,
        Paragraph? original, PortableTextCollapseRequest? collapse, bool inline,
        ReadOnlySpan<NativeTextStyleMetrics> metrics, ReadOnlySpan<NativeTextParagraphInlineObject> objects,
        NativeTextExclusionOptions? exclusionOptions, ReadOnlySpan<NativeTextExclusionRectangle> exclusions)
    {
        // Size/brush/feature changes on one face reuse its retained plans. Multiple
        // explicit faces use an isolated temporary context so they cannot alter
        // the cached primary context's uniform fallback behavior.
        bool oneFace = true;
        foreach (var style in request.Styles.Span) oneFace &= ReferenceEquals(style.Font, request.Font);
        using var ownedContext = oneFace ? null : new NativeTextShapingContext(request.Font.Data.Span, request.Font.FaceIndex);
        var context = ownedContext ?? primary.Context;
        var indices = new Dictionary<PortableTextFont, uint> { [request.Font] = 0 };
        var fonts = new List<TtfFont> { primary.RenderFont };
        var styles = new NativeTextParagraphStyle[request.Styles.Length];
        int featureCount = 0;
        foreach (var style in request.Styles.Span) featureCount = checked(featureCount + style.Features.Length);
        var features = new NativeTextFeature[featureCount];
        int feature = 0;
        for (int i = 0; i < styles.Length; i++)
        {
            var style = request.Styles.Span[i];
            if (style.Font == null || style.Font.UnitsPerEm == 0 || !float.IsFinite(style.FontSize) || style.FontSize <= 0)
                throw new ArgumentException("Each style requires a real source face and positive em size.");
            if (!indices.TryGetValue(style.Font, out uint index))
            {
                var status = context.AddFallbackFont(style.Font.Data.Span, out index, style.Font.FaceIndex);
                if (status != NativeRendererStatus.Success || index != fonts.Count)
                    throw new InvalidOperationException($"Native styled font registration failed: {status}.");
                indices.Add(style.Font, index);
                fonts.Add(_fonts.GetValue(style.Font, static f => new(f)).RenderFont);
            }
            styles[i] = new(style.Start, style.Length, index, style.FontSize / style.Font.UnitsPerEm,
                (uint)feature, (uint)style.Features.Length, style.Language);
            foreach (var value in style.Features.Span) features[feature++] = new(value.Tag, value.Value);
        }
        return CreateParagraph(request, CreateNative(context, request, options, features, styles, original, collapse, inline, metrics, objects, exclusionOptions, exclusions), fonts.ToArray());
    }

    private Paragraph CreateParagraph(PortableTextParagraphRequest request, NativeTextParagraphSnapshot native, TtfFont[] fonts)
        => native.FragmentLayout.HasValue ? new ExcludedParagraph(this, request, native, fonts) :
            native.HasMeasuredLines ? new InlineParagraph(this, request, native, fonts) : new Paragraph(this, request, native, fonts);

    private static NativeTextParagraphSnapshot CreateNative(NativeTextShapingContext context, PortableTextParagraphRequest request,
        NativeTextParagraphOptions options, NativeTextFeature[] features, NativeTextParagraphStyle[] styles,
        Paragraph? original, PortableTextCollapseRequest? collapse, bool inline,
        ReadOnlySpan<NativeTextStyleMetrics> metrics, ReadOnlySpan<NativeTextParagraphInlineObject> objects,
        NativeTextExclusionOptions? exclusionOptions, ReadOnlySpan<NativeTextExclusionRectangle> exclusions)
    {
        if (exclusionOptions is { } exclusion)
            return NativeTextParagraphSnapshot.CreateWithExclusions(context, request.Text.Span,
                request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight,
                in options, styles, metrics, objects, in exclusion, exclusions, features,
                request.IncrementalTab, request.TabOrigin, request.MeasureIntrinsicWidths, ConvertWrapping(request.Wrapping));
        if (inline)
            return NativeTextParagraphSnapshot.CreateWithInlineObjects(context, request.Text.Span,
                request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight,
                in options, styles, metrics, objects, features, request.IncrementalTab, request.TabOrigin,
                request.MeasureIntrinsicWidths, ConvertWrapping(request.Wrapping));
        if (collapse is { } c)
            return NativeTextParagraphSnapshot.CreateCollapsed(context, request.Text.Span,
                request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight, options, original!._native,
                new(c.LineIndex, c.Width, c.SymbolWidth, c.Trimming switch
                {
                    PortableTextTrimming.Character => NativeTextTrimming.CharacterEllipsis,
                    PortableTextTrimming.Word => NativeTextTrimming.WordEllipsis,
                    _ => throw new ArgumentOutOfRangeException(nameof(collapse))
                }), features, styles, request.IncrementalTab, request.TabOrigin, ConvertWrapping(request.Wrapping));
        return NativeTextParagraphSnapshot.Create(context, request.Text.Span,
            request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight,
            in options, features, styles, request.IncrementalTab, request.TabOrigin, request.MeasureIntrinsicWidths,
            ConvertWrapping(request.Wrapping));
    }

    private static NativeTextWrapping ConvertWrapping(PortableTextWrapping wrapping) => wrapping switch
    {
        PortableTextWrapping.Emergency => NativeTextWrapping.Emergency,
        PortableTextWrapping.WholeWord => NativeTextWrapping.WholeWord,
        _ => throw new ArgumentOutOfRangeException(nameof(wrapping))
    };

    private class InlineParagraph : Paragraph, IPortableInlineTextParagraph
    {
        public ReadOnlyMemory<PortableTextInlineObjectPlacement> InlineObjects { get; }
        public float GetBaselineOffset(int lineIndex) => _native.Lines.Span[lineIndex].BaselineY - Lines.Span[lineIndex].Y;

        internal InlineParagraph(WpfPortableTextFormatting owner, PortableTextParagraphRequest request,
            NativeTextParagraphSnapshot native, TtfFont[] fonts) : base(owner, request, native, fonts)
        {
            var placements = new PortableTextInlineObjectPlacement[native.InlineObjects.Length];
            for (int i = 0; i < placements.Length; i++)
            {
                var p = native.InlineObjects.Span[i];
                placements[i] = new(p.InputPosition, p.GlyphIndex, p.LineIndex, p.X, p.Y, p.Width, p.Height);
            }
            InlineObjects = placements;
        }
    }

    private sealed class ExcludedParagraph : InlineParagraph, IPortableExcludedTextParagraph
    {
        private readonly sbyte _paragraphLevel;
        public ReadOnlyMemory<PortableTextFragment> Fragments { get; }
        public ReadOnlyMemory<PortableTextCaretStop> Carets { get; }
        public double ContentWidth => _native.FragmentLayout!.Value.ContentWidth;
        public double ContentHeight => _native.FragmentLayout!.Value.ContentHeight;
        public double MeasuredWidth => _native.FragmentLayout!.Value.MeasuredWidth;

        internal ExcludedParagraph(WpfPortableTextFormatting owner, PortableTextParagraphRequest request,
            NativeTextParagraphSnapshot native, TtfFont[] fonts) : base(owner, request, native, fonts)
        {
            _paragraphLevel = (sbyte)(request.RightToLeft ? 1 : 0);
            var fragments = new PortableTextFragment[native.Fragments.Length];
            for (int i = 0; i < fragments.Length; i++)
            {
                var f = native.Fragments.Span[i];
                fragments[i] = new(checked((int)f.RowIndex), f.Left, f.Top, f.Width);
            }
            Fragments = fragments;
            var carets = new PortableTextCaretStop[native.Carets.Length];
            for (int i = 0; i < carets.Length; i++)
            {
                var c = native.Carets.Span[i];
                carets[i] = new(c.InputPosition, c.Trailing != 0, checked((int)c.LineIndex), c.X, c.Y, c.Height, c.BidiLevel);
            }
            Carets = carets;
        }

        public int MoveCaret(int caretIndex, PortableTextCaretMovement direction, float preferredX)
        {
            var movement = direction switch
            {
                PortableTextCaretMovement.Left => NativeTextCaretMovement.Left,
                PortableTextCaretMovement.Right => NativeTextCaretMovement.Right,
                PortableTextCaretMovement.Up => NativeTextCaretMovement.Up,
                PortableTextCaretMovement.Down => NativeTextCaretMovement.Down,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
            var status = _native.MoveFragmentCaret(checked((uint)caretIndex), movement, _paragraphLevel, preferredX, out uint next);
            if (status != NativeRendererStatus.Success) throw new InvalidOperationException($"Native fragment navigation failed: {status}.");
            return checked((int)next);
        }
    }

    private class Paragraph : IPortableTextParagraph
    {
        internal readonly NativeTextParagraphSnapshot _native;
        private readonly WpfPortableTextFormatting _owner;
        private readonly PortableTextParagraphRequest _request;
        private sealed record CollapseCache(PortableTextCollapseRequest Key, Paragraph Paragraph);
        private CollapseCache? _collapseCache;
        public PortableTextCollapsedRange? CollapsedRange => _native.CollapsedRange is { } c ?
            new(c.LineIndex, c.Start, c.End, c.SymbolGlyphIndex) : null;
        public IPortableTextParagraph Collapse(in PortableTextCollapseRequest request)
        {
            if (_native.HasMeasuredLines) throw new NotSupportedException("Measured paragraph collapse requires an explicit sign-metric contract.");
            if (CollapsedRange != null) throw new InvalidOperationException("Collapse the original paragraph, not a collapsed view.");
            var cached = _collapseCache;
            if (cached?.Key == request) return cached.Paragraph;
            var result = _owner.FormatCore(in _request, this, request);
            _collapseCache = new(request, result);
            return result;
        }
        private readonly (int Start, int Count)[] _boxes;
        private readonly (int Start, int Count)[] _carets;
        private readonly int[][] _logicalCarets;
        public ReadOnlyMemory<PortableTextGlyph> Glyphs { get; }
        public ReadOnlyMemory<PortableTextLineInfo> Lines { get; }
        private readonly TtfFont[] _fonts;
        public PortableTextIntrinsicWidths? IntrinsicWidths => _native.IntrinsicWidths is { } widths ?
            new PortableTextIntrinsicWidths(widths.Minimum, widths.Maximum) : null;
        public object NativeFont => _fonts[0];
        public object GetNativeFont(uint fontIndex) => _fonts[checked((int)fontIndex)];

        internal Paragraph(WpfPortableTextFormatting owner, PortableTextParagraphRequest request, NativeTextParagraphSnapshot native, TtfFont[] fonts)
        {
            _owner = owner; _request = request;
            _fonts = fonts;
            _native = native;
            var glyphs = new PortableTextGlyph[native.Glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                var g = native.Glyphs.Span[i];
                bool isObject = native.HasMeasuredLines && g.GlyphId == NativeTextParagraphSnapshot.InlineObjectGlyphId;
                if (!isObject && g.FontIndex >= fonts.Length) throw new NotSupportedException("The source font map must include native fallback faces.");
                glyphs[i] = new(g.GlyphId, g.Cluster, native.ClusterEnds.Span[i], g.X, g.Y, g.AdvanceX,
                    native.BidiLevels.Span[i], g.FontIndex, g.GlyphId == NativeTextParagraphSnapshot.TabGlyphId,
                    native.CollapsedRange?.SymbolGlyphIndex == i) { IsInlineObject = isObject };
            }
            Glyphs = glyphs;
            var lines = new PortableTextLineInfo[native.Lines.Length];
            _boxes = new (int, int)[lines.Length];
            _carets = new (int, int)[lines.Length];
            _logicalCarets = new int[lines.Length][];
            int box = 0, caret = 0;
            double lineTop = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = native.Lines.Span[i];
                lines[i] = new(checked((int)l.GlyphStart), checked((int)l.GlyphCount),
                    l.InputStart, l.InputEnd, l.Width, native.FragmentLayout.HasValue ? (float)native.Fragments.Span[i].Top :
                        native.HasMeasuredLines ? (float)lineTop : l.BaselineY, l.Height);
                if (!native.FragmentLayout.HasValue) lineTop += l.Height;
                int firstBox = box, firstCaret = caret;
                while (box < native.Boxes.Length && native.Boxes.Span[box].LineIndex == i) box++;
                while (caret < native.Carets.Length && native.Carets.Span[caret].LineIndex == i) caret++;
                _boxes[i] = (firstBox, box - firstBox); _carets[i] = (firstCaret, caret - firstCaret);
                var boundaries = new SortedSet<int> { l.InputStart, l.InputEnd };
                for (int j = firstCaret; j < caret; j++) boundaries.Add(native.Carets.Span[j].InputPosition);
                _logicalCarets[i] = new int[boundaries.Count];
                boundaries.CopyTo(_logicalCarets[i]);
            }
            Lines = lines;
        }

        public PortableTextHit HitTest(int lineIndex, float distance)
        {
            var range = _boxes[lineIndex];
            if (range.Count == 0) return new(Lines.Span[lineIndex].InputStart, false);
            Check(NativeTextInteractionInterop.HitTest(_native.Boxes.Span.Slice(range.Start, range.Count),
                distance, Lines.Span[lineIndex].Y, out var result));
            return new(result.InputPosition, result.Trailing != 0);
        }

        public float GetCaretDistance(int lineIndex, PortableTextHit hit)
        {
            var range = _carets[lineIndex];
            if (range.Count == 0) return 0;
            Check(NativeTextInteractionInterop.GetCaret(_native.Carets.Span.Slice(range.Start, range.Count),
                hit.Position, hit.Trailing, out var result));
            return result.X;
        }

        public int GetNextLogicalCaret(int lineIndex, int position, bool previous)
        {
            var values = _logicalCarets[lineIndex];
            int index = Array.BinarySearch(values, position);
            index = index >= 0 ? index + (previous ? -1 : 1) : ~index - (previous ? 1 : 0);
            return values[Math.Clamp(index, 0, values.Length - 1)];
        }

        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        {
            var range = _boxes[lineIndex];
            var output = ArrayPool<NativeTextRectangle>.Shared.Rent(rectangles.Length);
            try
            {
                Check(NativeTextInteractionInterop.GetSelection(_native.Boxes.Span.Slice(range.Start, range.Count),
                    start, end, output.AsSpan(0, rectangles.Length), out uint written));
                for (int i = 0; i < written; i++)
                {
                    var r = output[i]; rectangles[i] = new(r.X, r.Y - Lines.Span[lineIndex].Y, r.Width, r.Height);
                }
                return checked((int)written);
            }
            finally { ArrayPool<NativeTextRectangle>.Shared.Return(output); }
        }

        private static void Check(NativeRendererStatus status)
        {
            if (status != NativeRendererStatus.Success) throw new InvalidOperationException($"Native editor query failed: {status}.");
        }
    }
}
