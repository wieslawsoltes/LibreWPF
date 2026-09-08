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
        internal Properties Properties { get; } = new();
        internal bool Mixed { get; init; }
        internal bool AutoHeight { get; init; }
        public override TextRun GetTextRun(int index) => index >= 3 ? new TextEndOfParagraph(1) :
            new TextCharacters("abc", index, Mixed && index == 0 ? 1 : 3 - index,
                Mixed && index != 0 ? new Properties { Size = 24 } : Properties);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(limit, new(CultureInfo.InvariantCulture, new CharacterBufferRange("abc", 0, Math.Min(limit, 3))));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    private sealed class Properties : TextRunProperties
    {
        private readonly Typeface _face;
        internal double Size { get; init; } = 12;
        internal Properties()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "LibreWPF.FluentSymbols.ttf");
            var glyph = new GlyphTypeface(new Uri(path));
            _face = new Typeface(new FontFamily(path + "#" + glyph.FamilyNames.Values.First()), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
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
        internal ReadOnlyMemory<PortableTextStyle> Styles { get; private set; }
        public object NativeFont { get; } = new();
        public object SecondNativeFont { get; } = new();
        public object GetNativeFont(uint fontIndex) => fontIndex == 0 ? NativeFont : SecondNativeFont;
        internal int Calls { get; private set; }
        internal string? Text { get; private set; }
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        { Calls++; Text = request.Text.ToString(); Styles = request.Styles; Assert.False(request.Font.Data.IsEmpty); return this; }
        public ReadOnlyMemory<PortableTextGlyph> Glyphs => Mixed ? new PortableTextGlyph[]
        { new(0, 0, 1, 0, 0, 4, 0), new(0, 1, 2, 0, 20, 6, 0, 1), new(0, 2, 3, 6, 20, 6, 0, 1) } : new PortableTextGlyph[]
        { new(0, 0, 2, 0, 0, 8, 1), new(0, 2, 3, 0, 20, 6, 1) };
        public ReadOnlyMemory<PortableTextLineInfo> Lines => Mixed ? new PortableTextLineInfo[]
        { new(0, 1, 0, 1, 4, 0, 20), new(1, 2, 1, 3, 12, 20, 20) } : new PortableTextLineInfo[]
        { new(0, 1, 0, 2, 8, 0, 20), new(1, 1, 2, 3, 6, 20, 20) };
        public PortableTextHit HitTest(int lineIndex, float distance) => new(lineIndex == 0 ? 2 : 3, true);
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => hit.Position == (lineIndex == 0 ? 0 : 2) ? 0 : lineIndex == 0 ? 8 : 6;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => lineIndex == 0 ? previous ? 0 : 2 : previous ? 2 : 3;
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        { rectangles[0] = new(0, 0, lineIndex == 0 ? 8 : 6, 20); return 1; }
    }
}
