// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ProGPU.Wpf.Interop;
using Vector2 = System.Numerics.Vector2;

namespace MS.Internal.TextFormatting;

/// <summary>Source-owned WPF line semantics over the typed ProGPU paragraph service.</summary>
internal sealed class PortableTextLine : TextLine
{
    private static readonly ConditionalWeakTable<GlyphTypeface, PortableTextFont> Fonts = new();
    internal sealed record Continuation(PortableTextLine Owner, int LineIndex, int NextSourceIndex);
    private readonly IPortableTextParagraph _paragraph;
    private readonly string _text;
    private readonly TextRunProperties _properties;
    private readonly GlyphTypeface _face;
    private readonly int _paragraphStart, _lineIndex, _newlines;
    private readonly double _paragraphWidth, _indent, _baseline, _height;
    private readonly bool _rightToLeft;
    private readonly List<IndexedGlyphRun> _glyphRuns = new();
    private readonly List<TextSpan<TextRun>> _runs;
    private readonly Rect _ink;
    private readonly int _trailing;
    private readonly double _width;
    private bool _disposed;
    private PortableTextLineInfo Info => _paragraph.Lines.Span[_lineIndex];
    private int First => _paragraphStart + Info.InputStart;
    private int End => _paragraphStart + Info.InputEnd;

    internal static TextLine Create(FormatSettings settings, int first, int idealWidth, double pixelsPerDip)
    {
        if (!PortableWpfServiceRegistry.TryGetTextFormatting(out var service)) return null;
        double width = settings.Formatter.IdealToReal(idealWidth, pixelsPerDip);
        if (settings.PreviousLineBreak?.PortableContinuation is Continuation next)
        {
            if (next.NextSourceIndex != first || next.Owner._paragraphWidth != width)
                throw Unsupported("changed continuation width or source index");
            return new PortableTextLine(next.Owner, next.LineIndex);
        }
        var pap = settings.Pap;
        if (settings.IsSideways || settings.TextFormattingMode != TextFormattingMode.Ideal || pap.TextMarkerProperties != null ||
            (pap.TextDecorations?.Count ?? 0) != 0 || pap.Justify ||
            pap.Tabs?.Count > 0 || (pap.Wrap && !pap.EmergencyWrap))
            throw Unsupported("display hinting, sideways text, markers, decorations, justification, custom tabs or WrapWithOverflow");

        var builder = new StringBuilder();
        var runs = new List<TextSpan<TextRun>>();
        TextRunProperties properties = null;
        int cp = first, newlines = 0;
        while (true)
        {
            var range = settings.FetchTextRun(cp, first, out TextRun run, out int length);
            if (length <= 0 || builder.Length > (1 << 20) - length)
                throw Unsupported("invalid run length or paragraph input budget");
            if (run is TextEndOfLine)
            {
                newlines = length; runs.Add(new(length, run)); break;
            }
            if (run is not TextCharacters) throw Unsupported("document objects, modifiers or hidden runs");
            var p = run.Properties;
            if (p == null || (p.TextDecorations?.Count ?? 0) != 0 || (p.TextEffects?.Count ?? 0) != 0 ||
                p.BaselineAlignment != BaselineAlignment.Baseline)
                throw Unsupported("run decorations, effects, baseline changes, custom typography or number substitution");
            var digits = new DigitState();
            digits.SetTextRunProperties(p);
            if (digits.DigitCulture != null || digits.Contextual) throw Unsupported("digit substitution");
            if (properties != null && (!Equals(properties.Typeface, p.Typeface) ||
                properties.FontRenderingEmSize != p.FontRenderingEmSize || !Equals(properties.CultureInfo, p.CultureInfo) ||
                !Equals(properties.TypographyProperties, p.TypographyProperties) ||
                !Equals(properties.ForegroundBrush, p.ForegroundBrush) || !Equals(properties.BackgroundBrush, p.BackgroundBrush)))
                throw Unsupported("mixed styled runs");
            properties ??= p;
            int start = builder.Length;
            range.CharacterBuffer.AppendToStringBuilder(builder, range.OffsetToFirstChar, length);
            int used = length;
            for (int i = start; i < builder.Length; i++)
            {
                char c = builder[i];
                if (c == '\t') throw Unsupported("tab expansion");
                if (c is '\r' or '\n' or '\u2028' or '\u2029')
                {
                    newlines = c == '\r' && i + 1 < builder.Length && builder[i + 1] == '\n' ? 2 : 1;
                    used = i - start + newlines;
                    builder.Length = i;
                    break;
                }
            }
            runs.Add(new(used, run)); cp += used;
            if (newlines != 0) break;
        }
        properties ??= pap.DefaultTextRunProperties;
        if (!properties.Typeface.TryGetGlyphTypeface(out GlyphTypeface face))
            throw Unsupported("composite-font source resolution");
        string text = builder.ToString();
        var font = Fonts.GetValue(face, static source =>
        {
            using Stream stream = source.GetFontStream();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return new PortableTextFont(bytes.ToArray(), checked((uint)source.FaceIndex), source.DesignEmHeight);
        });
        double indent = settings.Formatter.IdealToReal(settings.TextIndent + pap.ParagraphIndent, pixelsPerDip);
        double height = pap.LineHeight > 0 ? settings.Formatter.IdealToReal(pap.LineHeight, pixelsPerDip) :
            properties.Typeface.LineSpacing(properties.FontRenderingEmSize, 1, pixelsPerDip, settings.TextFormattingMode);
        double baseline = properties.Typeface.Baseline(properties.FontRenderingEmSize, 1, pixelsPerDip, settings.TextFormattingMode);
        var request = new PortableTextParagraphRequest(text.AsMemory(), font, (float)properties.FontRenderingEmSize,
            (float)height, pap.Wrap && width > 0 ? (float)Math.Max(float.Epsilon, width - indent) : 0,
            pap.RightToLeft, PortableTextAlignment.Left, Features(properties.TypographyProperties));
        var paragraph = service.Format(in request);
        if (paragraph.Lines.Length == 0) throw new InvalidOperationException("The text provider returned no line.");
        return new PortableTextLine(paragraph, text, properties, face, first, 0, newlines,
            width, indent, baseline, height, pap.RightToLeft, runs, pixelsPerDip, pap.Align);
    }

    private PortableTextLine(PortableTextLine owner, int index) : this(owner._paragraph, owner._text,
        owner._properties, owner._face, owner._paragraphStart, index, owner._newlines,
        owner._paragraphWidth, owner._indent, owner._baseline, owner._height, owner._rightToLeft,
        owner._runs, owner.PixelsPerDip, owner._alignment) { }

    private static PortableTextFeature[] Features(TextRunTypographyProperties p)
    {
        if (p == null) return [];
        if (p.Variants != FontVariants.Normal || p.Capitals != FontCapitals.Normal || p.Fraction != FontFraction.Normal ||
            p.NumeralStyle != FontNumeralStyle.Normal || p.NumeralAlignment != FontNumeralAlignment.Normal ||
            p.EastAsianWidths != FontEastAsianWidths.Normal || p.EastAsianLanguage != FontEastAsianLanguage.Normal ||
            p.StandardSwashes != 0 || p.ContextualSwashes != 0 || p.StylisticAlternates != 0 || p.AnnotationAlternates != 0)
            throw Unsupported("enumerated typography alternates");
        static PortableTextFeature F(string tag, bool enabled) => new(
            (uint)tag[0] << 24 | (uint)tag[1] << 16 | (uint)tag[2] << 8 | tag[3], enabled ? 1U : 0U);
        return [F("liga", p.StandardLigatures), F("clig", p.ContextualLigatures), F("dlig", p.DiscretionaryLigatures),
            F("hlig", p.HistoricalLigatures), F("calt", p.ContextualAlternates), F("hist", p.HistoricalForms),
            F("kern", p.Kerning), F("cpsp", p.CapitalSpacing), F("case", p.CaseSensitiveForms),
            F("ss01", p.StylisticSet1), F("ss02", p.StylisticSet2), F("ss03", p.StylisticSet3), F("ss04", p.StylisticSet4),
            F("ss05", p.StylisticSet5), F("ss06", p.StylisticSet6), F("ss07", p.StylisticSet7), F("ss08", p.StylisticSet8),
            F("ss09", p.StylisticSet9), F("ss10", p.StylisticSet10), F("ss11", p.StylisticSet11), F("ss12", p.StylisticSet12),
            F("ss13", p.StylisticSet13), F("ss14", p.StylisticSet14), F("ss15", p.StylisticSet15), F("ss16", p.StylisticSet16),
            F("ss17", p.StylisticSet17), F("ss18", p.StylisticSet18), F("ss19", p.StylisticSet19), F("ss20", p.StylisticSet20),
            F("zero", p.SlashedZero), F("mgrk", p.MathematicalGreek), F("expt", p.EastAsianExpertForms)];
    }

    private readonly TextAlignment _alignment;
    private PortableTextLine(IPortableTextParagraph paragraph, string text, TextRunProperties properties,
        GlyphTypeface face, int paragraphStart, int lineIndex, int newlines, double width, double indent,
        double baseline, double height, bool rtl, List<TextSpan<TextRun>> runs, double pixelsPerDip, TextAlignment alignment)
        : base(pixelsPerDip)
    {
        _paragraph = paragraph; _text = text; _properties = properties; _face = face;
        _paragraphStart = paragraphStart; _lineIndex = lineIndex; _newlines = newlines;
        _paragraphWidth = width; _indent = indent; _baseline = baseline; _height = height;
        _rightToLeft = rtl; _runs = runs; _alignment = alignment;
        int visibleEnd = Info.InputEnd;
        while (visibleEnd > Info.InputStart && char.IsWhiteSpace(text[visibleEnd - 1])) visibleEnd--;
        _trailing = Info.InputEnd - visibleEnd;
        double trailingWidth = 0;
        foreach (var glyph in paragraph.Glyphs.Span.Slice(Info.GlyphStart, Info.GlyphCount))
            if (glyph.Cluster >= visibleEnd) trailingWidth += glyph.Advance;
        _width = Math.Max(0, Info.Width - trailingWidth);
        _ink = CreateGlyphRuns();
    }

    private Rect CreateGlyphRuns()
    {
        Rect ink = Rect.Empty;
        var glyphs = _paragraph.Glyphs.Span;
        int end = Info.GlyphStart + Info.GlyphCount;
        for (int first = Info.GlyphStart; first < end;)
        {
            int stop = first + 1;
            sbyte level = glyphs[first].BidiLevel;
            while (stop < end && glyphs[stop].BidiLevel == level)
            {
                var previous = glyphs[stop - 1]; var next = glyphs[stop];
                if (previous.Cluster != next.Cluster && ((level & 1) == 0 ?
                    previous.ClusterEnd != next.Cluster : next.ClusterEnd != previous.Cluster)) break;
                stop++;
            }
            var indices = new int[stop - first];
            for (int i = 0; i < indices.Length; i++) indices[i] = first + i;
            if ((level & 1) != 0)
                Array.Sort(indices, (a, b) =>
                {
                    int compare = _paragraph.Glyphs.Span[a].Cluster.CompareTo(_paragraph.Glyphs.Span[b].Cluster);
                    return compare == 0 ? a.CompareTo(b) : compare;
                });
            int cpStart = int.MaxValue, cpEnd = 0;
            foreach (int index in indices) { cpStart = Math.Min(cpStart, glyphs[index].Cluster); cpEnd = Math.Max(cpEnd, glyphs[index].ClusterEnd); }
            var ids = new ushort[indices.Length]; var advances = new double[indices.Length];
            var offsets = new Point[indices.Length]; var positions = new Vector2[indices.Length];
            var clusters = new ushort[cpEnd - cpStart]; var carets = new bool[clusters.Length + 1];
            double advance = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                var g = glyphs[indices[i]];
                ids[i] = checked((ushort)g.GlyphId); advances[i] = g.Advance;
                positions[i] = new(g.X, g.Y - Info.Y);
                double offset = (level & 1) == 0 ? g.X - advance :
                    -advance - _face.AdvanceWidths[ids[i]] * _properties.FontRenderingEmSize - g.X;
                offsets[i] = new(offset, -(g.Y - Info.Y));
                advance += g.Advance;
                if (i == 0 || g.Cluster != glyphs[indices[i - 1]].Cluster)
                {
                    clusters.AsSpan(g.Cluster - cpStart, g.ClusterEnd - g.Cluster).Fill(checked((ushort)i));
                    carets[g.Cluster - cpStart] = true; carets[g.ClusterEnd - cpStart] = true;
                }
            }
            var run = new GlyphRun(_face, level, false, _properties.FontRenderingEmSize, (float)PixelsPerDip,
                ids, new Point(Start, Baseline), advances, offsets, _text.AsSpan(cpStart, cpEnd - cpStart).ToArray(),
                null, clusters, carets, XmlLanguage.GetLanguage(_properties.CultureInfo.IetfLanguageTag));
            run.InitializePortableGlyphPositions(positions, _paragraph.NativeFont);
            _glyphRuns.Add(new(_paragraphStart + cpStart, cpEnd - cpStart, run));
            Rect bounds = run.ComputeInkBoundingBox();
            if (!bounds.IsEmpty) { bounds.Offset(run.BaselineOrigin.X, run.BaselineOrigin.Y); ink.Union(bounds); }
            first = stop;
        }
        return ink;
    }

    public override void Dispose() => _disposed = true;
    private void CheckAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static PlatformNotSupportedException Unsupported(string detail) => new("Native WPF text integration does not yet preserve " + detail + ".");
    public override void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion)
    {
        CheckAlive();
        ArgumentNullException.ThrowIfNull(drawingContext);
        var antiInversion = TextFormatterImp.CreateAntiInversionTransform(inversion, _paragraphWidth, _height);
        if (antiInversion != null) drawingContext.PushTransform(antiInversion);
        drawingContext.PushTransform(new TranslateTransform(origin.X, origin.Y));
        try
        {
            if (_properties.BackgroundBrush != null) drawingContext.DrawRectangle(_properties.BackgroundBrush, null, new Rect(Start, 0, WidthIncludingTrailingWhitespace, Height));
            foreach (var run in _glyphRuns) drawingContext.DrawGlyphRun(_properties.ForegroundBrush, run.GlyphRun);
        }
        finally { drawingContext.Pop(); if (antiInversion != null) drawingContext.Pop(); }
    }

    public override TextLine Collapse(params TextCollapsingProperties[] properties)
    { CheckAlive(); if (properties.Length == 0 || Width <= properties[0].Width) return this; throw Unsupported("source collapsing symbols"); }
    public override IList<TextCollapsedRange> GetTextCollapsedRanges() => null;
    public override CharacterHit GetCharacterHitFromDistance(double distance)
    {
        CheckAlive(); var hit = _paragraph.HitTest(_lineIndex, (float)(distance - Start));
        if (!hit.Trailing) return new(_paragraphStart + hit.Position, 0);
        int before = _paragraph.GetNextLogicalCaret(_lineIndex, hit.Position, true);
        return new(_paragraphStart + before, hit.Position - before);
    }
    public override double GetDistanceFromCharacterHit(CharacterHit hit)
    { CheckAlive(); return Start + _paragraph.GetCaretDistance(_lineIndex, new(hit.FirstCharacterIndex + hit.TrailingLength - _paragraphStart, hit.TrailingLength != 0)); }
    public override CharacterHit GetNextCaretCharacterHit(CharacterHit hit) => Move(hit, false);
    public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit hit) => Move(hit, true);
    public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit hit) => Move(hit, true);
    private CharacterHit Move(CharacterHit hit, bool previous)
    {
        CheckAlive(); int position = hit.FirstCharacterIndex + hit.TrailingLength;
        if (position >= End && !previous) return new(First + Length, 0);
        return new(_paragraphStart + _paragraph.GetNextLogicalCaret(_lineIndex, position - _paragraphStart, previous), 0);
    }
    public override IList<TextBounds> GetTextBounds(int first, int length)
    {
        CheckAlive();
        if (length < 0) { first += length; length = -length; }
        int start = Math.Clamp(first, First, End) - _paragraphStart;
        int end = Math.Clamp(checked(first + length), First, End) - _paragraphStart;
        var rectangles = new PortableRect[Math.Max(1, Info.GlyphCount)];
        var result = new List<TextBounds>();
        foreach (var run in _glyphRuns)
        {
            int from = Math.Max(start, run.TextSourceCharacterIndex - _paragraphStart);
            int to = Math.Min(end, run.TextSourceCharacterIndex + run.TextSourceLength - _paragraphStart);
            if (from >= to) continue;
            int count = _paragraph.GetSelection(_lineIndex, from, to, rectangles);
            for (int i = 0; i < count; i++)
            {
                var r = rectangles[i]; result.Add(new(new Rect(Start + r.X, r.Y, r.Width, r.Height),
                    (run.GlyphRun.BidiLevel & 1) != 0 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, null));
            }
        }
        return result;
    }
    public override IList<TextSpan<TextRun>> GetTextRunSpans()
    {
        var result = new List<TextSpan<TextRun>>(); int cp = _paragraphStart;
        foreach (var run in _runs)
        {
            int overlap = Math.Min(cp + run.Length, First + Length) - Math.Max(cp, First);
            if (overlap > 0) result.Add(new(overlap, run.Value)); cp += run.Length;
        }
        return result;
    }
    public override IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns() => _glyphRuns;
    public override TextLineBreak GetTextLineBreak() => _lineIndex + 1 < _paragraph.Lines.Length ?
        new TextLineBreak(null, IntPtr.Zero) { PortableContinuation = new(this, _lineIndex + 1, End) } : null;
    public override bool HasOverflowed => _paragraphWidth > 0 && Start + Width > _paragraphWidth;
    public override bool HasCollapsed => false;
    public override int Length => Info.InputEnd - Info.InputStart + NewlineLength;
    public override int NewlineLength => _lineIndex + 1 == _paragraph.Lines.Length ? _newlines : 0;
    public override int TrailingWhitespaceLength => _trailing + NewlineLength;
    public override int DependentLength => _text.Length - Info.InputEnd;
    public override double Start => _indent + (_paragraphWidth <= 0 ? 0 : _alignment switch
    { TextAlignment.Right => _paragraphWidth - _indent - _width, TextAlignment.Center => (_paragraphWidth - _indent - _width) / 2, _ => 0 });
    public override double Width => _width;
    public override double WidthIncludingTrailingWhitespace => Info.Width;
    public override double Height => _height;
    public override double TextHeight => _height;
    public override double Baseline => _baseline;
    public override double TextBaseline => _baseline;
    public override double MarkerBaseline => _baseline;
    public override double MarkerHeight => _height;
    public override double Extent => _ink.IsEmpty ? 0 : _ink.Height;
    public override double OverhangLeading => _ink.IsEmpty ? 0 : Start - _ink.Left;
    public override double OverhangTrailing => _ink.IsEmpty ? 0 : _ink.Right - Start - Width;
    public override double OverhangAfter => _ink.IsEmpty ? 0 : _ink.Bottom - Height;
}
