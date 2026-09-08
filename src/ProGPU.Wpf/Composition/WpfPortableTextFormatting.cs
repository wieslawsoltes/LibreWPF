using System.Buffers;
using System.Runtime.CompilerServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using ProGPU.Text;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed class WpfPortableTextFormatting : IPortableTextFormatting
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
        if (!request.Styles.IsEmpty) return FormatStyled(in request, in options, font);
        var features = new NativeTextFeature[request.Features.Length];
        for (int i = 0; i < features.Length; i++) features[i] = new(request.Features.Span[i].Tag, request.Features.Span[i].Value);
        return new Paragraph(NativeTextParagraphSnapshot.Create(font.Context, request.Text.Span,
            request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight, in options, features,
            incrementalTab: request.IncrementalTab, tabOrigin: request.TabOrigin), [font.RenderFont]);
    }

    private Paragraph FormatStyled(in PortableTextParagraphRequest request, in NativeTextParagraphOptions options, FontState primary)
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
        return new Paragraph(NativeTextParagraphSnapshot.Create(context, request.Text.Span,
            request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight,
            in options, features, styles, request.IncrementalTab, request.TabOrigin), fonts.ToArray());
    }

    private sealed class Paragraph : IPortableTextParagraph
    {
        private readonly NativeTextParagraphSnapshot _native;
        private readonly (int Start, int Count)[] _boxes;
        private readonly (int Start, int Count)[] _carets;
        private readonly int[][] _logicalCarets;
        public ReadOnlyMemory<PortableTextGlyph> Glyphs { get; }
        public ReadOnlyMemory<PortableTextLineInfo> Lines { get; }
        private readonly TtfFont[] _fonts;
        public object NativeFont => _fonts[0];
        public object GetNativeFont(uint fontIndex) => _fonts[checked((int)fontIndex)];

        internal Paragraph(NativeTextParagraphSnapshot native, TtfFont[] fonts)
        {
            _fonts = fonts;
            _native = native;
            var glyphs = new PortableTextGlyph[native.Glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                var g = native.Glyphs.Span[i];
                if (g.FontIndex >= fonts.Length) throw new NotSupportedException("The source font map must include native fallback faces.");
                glyphs[i] = new(g.GlyphId, g.Cluster, native.ClusterEnds.Span[i], g.X, g.Y, g.AdvanceX,
                    native.BidiLevels.Span[i], g.FontIndex, g.GlyphId == NativeTextParagraphSnapshot.TabGlyphId);
            }
            Glyphs = glyphs;
            var lines = new PortableTextLineInfo[native.Lines.Length];
            _boxes = new (int, int)[lines.Length];
            _carets = new (int, int)[lines.Length];
            _logicalCarets = new int[lines.Length][];
            int box = 0, caret = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = native.Lines.Span[i];
                lines[i] = new(checked((int)l.GlyphStart), checked((int)l.GlyphCount),
                    l.InputStart, l.InputEnd, l.Width, l.BaselineY, l.Height);
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
