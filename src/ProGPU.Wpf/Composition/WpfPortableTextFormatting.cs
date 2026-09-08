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
        internal NativeTextShapingContext Context { get; } = new(source.Data.Span, source.FaceIndex);
    }
    private readonly ConditionalWeakTable<PortableTextFont, FontState> _fonts = new();
    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureTextFormatting(Default);

    public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
    {
        if (request.Font.UnitsPerEm == 0 || !float.IsFinite(request.FontSize) || request.FontSize <= 0)
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
        var features = new NativeTextFeature[request.Features.Length];
        for (int i = 0; i < features.Length; i++) features[i] = new(request.Features.Span[i].Tag, request.Features.Span[i].Value);
        return new Paragraph(NativeTextParagraphSnapshot.Create(font.Context, request.Text.Span,
            request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight, in options, features), font.RenderFont);
    }

    private sealed class Paragraph : IPortableTextParagraph
    {
        private readonly NativeTextParagraphSnapshot _native;
        private readonly (int Start, int Count)[] _boxes;
        private readonly (int Start, int Count)[] _carets;
        private readonly int[][] _logicalCarets;
        public ReadOnlyMemory<PortableTextGlyph> Glyphs { get; }
        public ReadOnlyMemory<PortableTextLineInfo> Lines { get; }
        public object NativeFont { get; }

        internal Paragraph(NativeTextParagraphSnapshot native, TtfFont renderFont)
        {
            NativeFont = renderFont;
            _native = native;
            var glyphs = new PortableTextGlyph[native.Glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                var g = native.Glyphs.Span[i];
                if (g.FontIndex != 0) throw new NotSupportedException("The source font map must include native fallback faces.");
                glyphs[i] = new(g.GlyphId, g.Cluster, native.ClusterEnds.Span[i], g.X, g.Y, g.AdvanceX, native.BidiLevels.Span[i]);
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
