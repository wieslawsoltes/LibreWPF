// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Globalization;
using System.Linq;
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;
using System.Windows.Media.TextFormatting;

namespace System.Windows.Media;

[Collection("Sequential")]
public class PortableTextLineTests
{
    [Fact]
    public void HiddenDocumentEdgesPreserveSourceRangesAcrossWrappedContinuation()
    {
        var p = new Properties();
        var source = new DocumentSource([new TextHidden(2), new TextCharacters("a", p), new TextHidden(3),
            new TextCharacters("bc", p), new TextHidden(1)]);
        var provider = new Provider { Mixed = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var first = PortableTextLine.Create(DocumentSettings(formatter, source, p), 0, 800, 1);
        Assert.Equal("abc", provider.Text);
        Assert.Equal(6, first.Length);
        Assert.Equal(3, first.DependentLength);
        Assert.Equal(6, first.GetTextRunSpans().Sum(run => run.Length));
        var glyph = Assert.Single(first.GetIndexedGlyphRuns());
        Assert.Equal(2, glyph.TextSourceCharacterIndex);
        Assert.Equal(1, glyph.TextSourceLength);
        Assert.Equal("a", new string(glyph.GlyphRun.Characters.ToArray()));
        Assert.Empty(first.GetTextBounds(0, 2));
        Assert.Empty(first.GetTextBounds(3, 3));
        Assert.Equal(new CharacterHit(2, 1), first.GetCharacterHitFromDistance(3));
        Assert.Equal(4, first.GetDistanceFromCharacterHit(new(2, 1)));
        Assert.Equal(4, first.GetDistanceFromCharacterHit(new(4, 0)));
        Assert.Equal(new CharacterHit(6, 0), first.GetNextCaretCharacterHit(new(2, 0)));
        Assert.Equal(new CharacterHit(2, 0), first.GetPreviousCaretCharacterHit(new(6, 0)));
        using var original = first.GetTextLineBreak();
        using var continuation = original.Clone();
        original.Dispose(); first.Dispose();
        using var second = PortableTextLine.Create(DocumentSettings(formatter, source, p, continuation), 6, 800, 1);
        Assert.Equal(4, second.Length);
        Assert.Equal(1, second.NewlineLength);
        Assert.Equal(2, second.TrailingWhitespaceLength); // Hidden close edge plus paragraph terminator.
        glyph = Assert.Single(second.GetIndexedGlyphRuns());
        Assert.Equal(6, glyph.TextSourceCharacterIndex);
        Assert.Equal(2, glyph.TextSourceLength);
        Assert.Empty(second.GetTextBounds(8, 1));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void WhitespaceOnlyContinuationDoesNotCountPreviousLinesHiddenEdges()
    {
        var p = new Properties();
        var source = new DocumentSource([new TextHidden(2), new TextCharacters("a", p), new TextHidden(3),
            new TextCharacters("  ", p), new TextHidden(1)]);
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new Provider { Mixed = true });
        using var formatter = new TextFormatterImp();
        using var first = PortableTextLine.Create(DocumentSettings(formatter, source, p), 0, 800, 1);
        using var continuation = first.GetTextLineBreak();
        using var second = PortableTextLine.Create(DocumentSettings(formatter, source, p, continuation), 6, 800, 1);
        Assert.Equal(4, second.Length);
        Assert.Equal(4, second.TrailingWhitespaceLength);
        Assert.Equal(0, second.Width);
    }

    [Fact]
    public void NestedPropertyModifiersApplyInsideOutAndRestoreTheirParent()
    {
        var p = new Properties();
        var calls = new List<string>();
        var outer = new Modifier(properties => { calls.Add("outer"); return properties; });
        var inner = new Modifier(properties => { calls.Add("inner"); return new Properties { Size = 24 }; });
        var source = new DocumentSource([outer, inner, new TextCharacters("a", p), new TextEndOfSegment(1),
            new TextCharacters("bc", p), new TextEndOfSegment(1)]);
        var provider = new Provider { Mixed = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var line = PortableTextLine.Create(DocumentSettings(formatter, source, p), 0, 800, 1);
        Assert.Equal(new[] { "inner", "outer", "outer" }, calls);
        Assert.Equal(24, provider.Styles.Span[0].FontSize);
        Assert.Equal(12, provider.Styles.Span[1].FontSize);
        Assert.Equal(24, Assert.Single(line.GetIndexedGlyphRuns()).GlyphRun.FontRenderingEmSize);
    }

    [Fact]
    public void ModifierScopeSurvivesWrappedAndExplicitLineBreaks()
    {
        var p = new Properties();
        var modifier = new Modifier(properties => new Properties { Size = 24 });
        var source = new DocumentSource([modifier, new TextCharacters("abc", p), new TextEndOfLine(1),
            new TextCharacters("abc", p), new TextEndOfSegment(1)]);
        var provider = new Provider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var first = PortableTextLine.Create(DocumentSettings(formatter, source, p), 0, 800, 1);
        using var wrap = first.GetTextLineBreak();
        using var second = PortableTextLine.Create(DocumentSettings(formatter, source, p, wrap), 3, 800, 1);
        Assert.Equal(2, second.Length);
        using var hardBreak = second.GetTextLineBreak();
        using var clone = hardBreak.Clone();
        hardBreak.Dispose(); second.Dispose(); first.Dispose();
        using var third = PortableTextLine.Create(DocumentSettings(formatter, source, p, clone), 5, 800, 1);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(24, provider.Styles.Span[0].FontSize);
        Assert.Equal(5, Assert.Single(third.GetIndexedGlyphRuns()).TextSourceCharacterIndex);
    }

    [Fact]
    public void HiddenOnlyParagraphConsumesSourceWithoutInventingTextOrInk()
    {
        var p = new Properties();
        var source = new DocumentSource([new TextHidden(4)]);
        var provider = new Provider { Empty = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var line = PortableTextLine.Create(DocumentSettings(formatter, source, p), 0, 800, 1);
        Assert.Equal("", provider.Text);
        Assert.Equal(5, line.Length);
        Assert.Equal(0, line.Width);
        Assert.Empty(line.GetIndexedGlyphRuns());
        Assert.Empty(line.GetTextBounds(0, 4));
        Assert.Null(line.GetTextLineBreak());
    }

    [Fact]
    public void DirectionalOrUnbalancedModifiersRemainExplicitFailures()
    {
        var p = new Properties();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new Provider());
        using var formatter = new TextFormatterImp();
        foreach (TextRun run in new TextRun[] { new Modifier(properties => properties, true), new TextEndOfSegment(1) })
            Assert.Throws<PlatformNotSupportedException>(() => PortableTextLine.Create(
                DocumentSettings(formatter, new DocumentSource([run, new TextCharacters("abc", p)]), p), 0, 800, 1));
    }

    private static FormatSettings DocumentSettings(TextFormatterImp formatter, TextSource source,
        TextRunProperties properties, TextLineBreak? previous = null) =>
        new(formatter, source, new TextRunCacheImp(), new ParaProp(formatter, new ParagraphProperties(properties, false), false),
            previous, true, TextFormattingMode.Ideal, false);

    private sealed class DocumentSource(TextRun[] runs) : TextSource
    {
        public override TextRun GetTextRun(int index)
        {
            foreach (var run in runs)
            {
                if (index == 0) return run;
                index -= run.Length;
                if (index < 0) throw new InvalidOperationException("Fixture requested inside a source run.");
            }
            return new TextEndOfParagraph(1);
        }
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(0, new(CultureInfo.InvariantCulture, new CharacterBufferRange()));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    private sealed class Modifier(Func<TextRunProperties, TextRunProperties> modify, bool directional = false) : TextModifier
    {
        public override int Length => 1;
        public override TextRunProperties Properties => null!;
        public override TextRunProperties ModifyProperties(TextRunProperties properties) => modify(properties);
        public override bool HasDirectionalEmbedding => directional;
        public override FlowDirection FlowDirection => FlowDirection.RightToLeft;
    }

    [Fact]
    public void IncrementalTabKeepsCaretSelectionAndWidthWithoutAnInkGlyph()
    {
        var provider = new Provider { Tabs = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var source = new Source { Text = "a\tb" };
        using var line = PortableTextLine.Create(Settings(formatter, source), 0, 800, 1);
        Assert.Equal("a\tb", provider.Text);
        Assert.Equal(32, provider.IncrementalTab);
        Assert.Equal(38, line.WidthIncludingTrailingWhitespace);
        Assert.Equal(2, line.GetIndexedGlyphRuns().Count());
        Assert.All(line.GetIndexedGlyphRuns(), run => Assert.DoesNotContain('\t', run.GlyphRun.Characters));
        Assert.Equal(new CharacterHit(1, 1), line.GetCharacterHitFromDistance(20));
        Assert.Equal(32, line.GetDistanceFromCharacterHit(new(1, 1)));
        var selection = Assert.Single(line.GetTextBounds(1, 1)).Rectangle;
        Assert.Equal(8, selection.X);
        Assert.Equal(24, selection.Width);
        Assert.Equal(new CharacterHit(2, 0), line.GetNextCaretCharacterHit(new(1, 0)));
    }

    [Fact]
    public void CompositeFontRangesPreserveMappingScaleThroughWrappedGlyphRuns()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "Inter-Medium.ttf");
        var face = new GlyphTypeface(new Uri(path));
        string target = path + "#" + face.FamilyNames.Values.First();
        var composite = new FontFamily();
        composite.FamilyMaps.Add(new FontFamilyMap { Unicode = "0061", Target = target, Scale = .75 });
        composite.FamilyMaps.Add(new FontFamilyMap { Unicode = "0062-0063", Target = target, Scale = 1.5 });
        var source = new Source { Properties = new Properties(composite) };
        Assert.False(source.Properties.Typeface.TryGetGlyphTypeface(out _));
        var provider = new Provider { Mixed = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var first = PortableTextLine.Create(Settings(formatter, source), 0, 800, 1);
        Assert.Equal(2, provider.Styles.Length);
        Assert.Equal(9, provider.Styles.Span[0].FontSize);
        Assert.Equal(18, provider.Styles.Span[1].FontSize);
        Assert.Equal(9, Assert.Single(first.GetIndexedGlyphRuns()).GlyphRun.FontRenderingEmSize);
        using var continuation = first.GetTextLineBreak();
        using var second = PortableTextLine.Create(Settings(formatter, source, continuation), 1, 800, 1);
        var glyphs = Assert.Single(second.GetIndexedGlyphRuns());
        Assert.Equal(18, glyphs.GlyphRun.FontRenderingEmSize);
        Assert.Equal(1, glyphs.TextSourceCharacterIndex);
        Assert.Equal(2, glyphs.TextSourceLength);
        Assert.Equal(new ushort[] { 0, 1 }, glyphs.GlyphRun.ClusterMap);
    }

    [Fact]
    public void SourceAdapterKeepsClustersAndClonedContinuationAfterLineDisposal()
    {
        var provider = new Provider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var source = new Source();
        using var line = PortableTextLine.Create(Settings(formatter, source), 0, TextFormatterImp.RealToIdeal(80), 1);
        Assert.Equal("abc", provider.Text);
        Assert.Equal(2, line.Length);
        Assert.Equal(0, line.NewlineLength);
        Assert.Equal(new CharacterHit(0, 2), line.GetCharacterHitFromDistance(7));
        Assert.Equal(new CharacterHit(2, 0), line.GetNextCaretCharacterHit(new(0, 0)));
        Assert.Equal(8, line.GetDistanceFromCharacterHit(new(0, 2)));
        Assert.Equal(8, Assert.Single(line.GetTextBounds(0, 2)).Rectangle.Width);
        var glyph = Assert.Single(line.GetIndexedGlyphRuns()).GlyphRun;
        Assert.Equal(1, glyph.BidiLevel);
        Assert.Equal(new ushort[] { 0, 0 }, glyph.ClusterMap);
        Assert.True(((IPortableNativeGlyphRunSource)glyph).TryGetPortableNativeGlyphRun(out var native));
        Assert.Equal(System.Numerics.Vector2.Zero, Assert.Single(native.GlyphPositions));
        Assert.Same(provider.NativeFont, native.NativeFont);
        Assert.True(((IPortableGlyphRunSource)glyph).TryGetPortableGlyphRun(out var portable));
        Assert.Same(provider.NativeFont, portable.NativeFont);
        Assert.Equal(0, Assert.Single(portable.GlyphPositions).X);
        using var original = line.GetTextLineBreak();
        using var continuation = original.Clone();
        original.Dispose(); line.Dispose();
        using var next = PortableTextLine.Create(Settings(formatter, source, continuation), 2, TextFormatterImp.RealToIdeal(80), 1);
        Assert.Equal(2, next.Length);
        Assert.Equal(1, next.NewlineLength);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void StyledLinesPreserveRunMetricsBrushesAndExactRenderFaces()
    {
        var provider = new Provider { Mixed = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var source = new Source { Mixed = true, AutoHeight = true };
        using var first = PortableTextLine.Create(Settings(formatter, source), 0, 800, 1);
        Assert.Equal(2, provider.Styles.Length);
        Assert.Equal(12, provider.Styles.Span[0].FontSize);
        Assert.Equal(24, provider.Styles.Span[1].FontSize);
        Assert.Equal(1, provider.Styles.Span[1].Start);
        using var continuation = first.GetTextLineBreak();
        using var second = PortableTextLine.Create(Settings(formatter, source, continuation), 1, 800, 1);
        Assert.True(second.Height > first.Height);
        var run = Assert.Single(second.GetIndexedGlyphRuns()).GlyphRun;
        Assert.Equal(24, run.FontRenderingEmSize);
        Assert.True(((IPortableNativeGlyphRunSource)run).TryGetPortableNativeGlyphRun(out var native));
        Assert.Same(provider.SecondNativeFont, native.NativeFont);
        Assert.Equal(second.Height, Assert.Single(second.GetTextBounds(1, 2)).Rectangle.Height);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) second.Draw(context, new Point(), InvertAxes.None);
        static IEnumerable<Drawing> Leaves(Drawing drawing)
        {
            if (drawing is DrawingGroup group)
                foreach (Drawing child in group.Children) foreach (Drawing leaf in Leaves(child)) yield return leaf;
            else yield return drawing;
        }
        Assert.Same(Brushes.Red, Assert.Single(Leaves(visual.Drawing).OfType<GlyphRunDrawing>()).ForegroundBrush);
        Assert.Same(Brushes.Blue, Assert.Single(Leaves(visual.Drawing).OfType<GeometryDrawing>()).Brush);
        Assert.Equal(1, provider.Calls);
    }

    private static FormatSettings Settings(TextFormatterImp formatter, Source source, TextLineBreak? previous = null) =>
        new(formatter, source, new TextRunCacheImp(), new ParaProp(formatter, new ParagraphProperties(source.Properties, source.AutoHeight), false),
            previous, true, TextFormattingMode.Ideal, false);

    private sealed class Source : TextSource
    {
        internal Properties Properties { get; init; } = new();
        internal bool Mixed { get; init; }
        internal bool AutoHeight { get; init; }
        internal string Text { get; init; } = "abc";
        public override TextRun GetTextRun(int index) => index >= Text.Length ? new TextEndOfParagraph(1) :
            new TextCharacters(Text, index, Mixed && index == 0 ? 1 : Text.Length - index,
                Mixed && index != 0 ? new Properties { Size = 24 } : Properties);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(limit, new(CultureInfo.InvariantCulture, new CharacterBufferRange(Text, 0, Math.Min(limit, Text.Length))));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    private sealed class Properties : TextRunProperties
    {
        private readonly Typeface _face;
        internal double Size { get; init; } = 12;
        internal Properties(FontFamily? family = null)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "LibreWPF.FluentSymbols.ttf");
            var glyph = new GlyphTypeface(new Uri(path));
            _face = new Typeface(family ?? new FontFamily(path + "#" + glyph.FamilyNames.Values.First()), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        }
        public override Typeface Typeface => _face;
        public override double FontRenderingEmSize => Size;
        public override double FontHintingEmSize => Size;
        public override TextDecorationCollection TextDecorations => null!;
        public override Brush ForegroundBrush => Size == 24 ? Brushes.Red : Brushes.Black;
        public override Brush BackgroundBrush => Size == 24 ? Brushes.Blue : null!;
        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
        public override TextEffectCollection TextEffects => null!;
    }

    private sealed class ParagraphProperties(TextRunProperties properties, bool autoHeight) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.RightToLeft;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => autoHeight ? 0 : 20;
        public override double DefaultIncrementalTab => 32;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => TextWrapping.Wrap;
        public override TextMarkerProperties TextMarkerProperties => null!;
        public override double Indent => 0;
    }

    // A typed source contract fixture, not native shaping/parity evidence.
    private sealed class Provider : IPortableTextFormatting, IPortableTextParagraph
    {
        internal bool Mixed { get; init; }
        internal bool Tabs { get; init; }
        internal bool Empty { get; init; }
        internal float IncrementalTab { get; private set; }
        internal ReadOnlyMemory<PortableTextStyle> Styles { get; private set; }
        public object NativeFont { get; } = new();
        public object SecondNativeFont { get; } = new();
        public object GetNativeFont(uint fontIndex) => fontIndex == 0 ? NativeFont : SecondNativeFont;
        internal int Calls { get; private set; }
        internal string? Text { get; private set; }
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        { Calls++; Text = request.Text.ToString(); Styles = request.Styles; IncrementalTab = request.IncrementalTab; Assert.False(request.Font.Data.IsEmpty); return this; }
        public ReadOnlyMemory<PortableTextGlyph> Glyphs => Empty ? ReadOnlyMemory<PortableTextGlyph>.Empty : Tabs ? new PortableTextGlyph[]
        { new(0, 0, 1, 0, 0, 8, 0), new(uint.MaxValue, 1, 2, 8, 0, 24, 0, IsTab: true), new(0, 2, 3, 32, 0, 6, 0) } : Mixed ? new PortableTextGlyph[]
        { new(0, 0, 1, 0, 0, 4, 0), new(0, 1, 2, 0, 20, 6, 0, 1), new(0, 2, 3, 6, 20, 6, 0, 1) } : new PortableTextGlyph[]
        { new(0, 0, 2, 0, 0, 8, 1), new(0, 2, 3, 0, 20, 6, 1) };
        public ReadOnlyMemory<PortableTextLineInfo> Lines => Empty ? new PortableTextLineInfo[] { new(0, 0, 0, 0, 0, 0, 20) } : Tabs ? new PortableTextLineInfo[]
        { new(0, 3, 0, 3, 38, 0, 20) } : Mixed ? new PortableTextLineInfo[]
        { new(0, 1, 0, 1, 4, 0, 20), new(1, 2, 1, 3, 12, 20, 20) } : new PortableTextLineInfo[]
        { new(0, 1, 0, 2, 8, 0, 20), new(1, 1, 2, 3, 6, 20, 20) };
        public PortableTextHit HitTest(int lineIndex, float distance) => new(lineIndex == 0 ? Mixed ? 1 : 2 : 3, true);
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => Tabs ? TabDistance(hit.Position) :
            Mixed ? lineIndex == 0 ? hit.Position * 4 : (hit.Position - 1) * 6 :
            hit.Position == (lineIndex == 0 ? 0 : 2) ? 0 : lineIndex == 0 ? 8 : 6;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => Empty ? 0 : Tabs ? Math.Clamp(position + (previous ? -1 : 1), 0, 3) :
            Mixed ? lineIndex == 0 ? previous ? 0 : 1 : previous ? 1 : 3 : lineIndex == 0 ? previous ? 0 : 2 : previous ? 2 : 3;
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        { rectangles[0] = Tabs ? new(TabDistance(start), 0, TabDistance(end) - TabDistance(start), 20) : new(0, 0, lineIndex == 0 ? 8 : 6, 20); return 1; }
        private static float TabDistance(int position) => position switch { 0 => 0, 1 => 8, 2 => 32, _ => 38 };
    }
}
