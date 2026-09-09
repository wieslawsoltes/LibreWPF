// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;
using System.Windows.Media.TextFormatting;

namespace System.Windows.Media;

[Collection("Sequential")]
public class PortableTextLineTests
{
    [PortableMediaFact]
    public void CollapseRetainsSourceRangesAndIndependentStyledSymbolAfterProviderRemoval()
    {
        var properties = new Properties(new FontFamily(Path.Combine(AppContext.BaseDirectory,
            "LibreWPF", "Fonts", "Inter-Medium.ttf") + "#Inter"));
        var source = new Source { Text = "abc", Properties = properties };
        var provider = new CollapseProvider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var original = formatter.FormatLine(source, 0, 7,
            new ParagraphProperties(properties, false, false, TextWrapping.NoWrap), null, new TextRunCache());
        registration.Dispose();
        var signProperties = new Properties(properties.Typeface.FontFamily) { Size = 24 };
        using var collapsed = original.Collapse(new TextTrailingCharacterEllipsis(7, signProperties));
        Assert.True(collapsed.HasCollapsed);
        Assert.False(original.HasCollapsed);
        Assert.Equal(original.Length, collapsed.Length);
        Assert.Equal(original.Height, collapsed.Height);
        Assert.Equal(original.Baseline, collapsed.Baseline);
        Assert.Equal(7, collapsed.Width);
        Assert.Equal(1, collapsed.NewlineLength);
        var range = Assert.Single(collapsed.GetTextCollapsedRanges());
        Assert.Equal(1, range.TextSourceCharacterIndex);
        Assert.Equal(2, range.Length);
        Assert.Equal(8, range.Width);
        Assert.Equal(new CharacterHit(1, 2), collapsed.GetCharacterHitFromDistance(6));
        Assert.Equal(4, collapsed.GetDistanceFromCharacterHit(new CharacterHit(2, 0)));
        Assert.Equal(7, collapsed.GetDistanceFromCharacterHit(new CharacterHit(1, 2)));
        var bounds = Assert.Single(collapsed.GetTextBounds(1, 2));
        Assert.Equal(new Rect(4, 0, 3, collapsed.Height), bounds.Rectangle);
        var glyphs = collapsed.GetIndexedGlyphRuns().ToArray();
        Assert.Equal(2, glyphs.Length);
        Assert.Equal(24, glyphs[1].GlyphRun.FontRenderingEmSize);
        Assert.Equal(new Point(4, collapsed.Baseline), glyphs[1].GlyphRun.BaselineOrigin);
        Assert.Equal(1, glyphs[1].TextSourceCharacterIndex);
        Assert.Equal(2, glyphs[1].TextSourceLength);
        Assert.Equal(2, provider.FormatCalls); // Symbol uses the captured provider, not the current registry.
        original.Dispose();
        Assert.Single(collapsed.GetTextCollapsedRanges());
        using var expanded = collapsed.Collapse(new TextTrailingCharacterEllipsis(100, signProperties));
        Assert.False(expanded.HasCollapsed);
        Assert.Equal(12, expanded.Width);
        Assert.Equal(3, Assert.Single(expanded.GetIndexedGlyphRuns()).TextSourceLength);
        var visual = new DrawingVisual();
        using var drawing = visual.RenderOpen();
        collapsed.Draw(drawing, new Point(), InvertAxes.None);
    }

    // Typed adapter fixture only; native shaping/layout is covered separately.
    private sealed class CollapseProvider : IPortableTextFormatting
    {
        internal int FormatCalls { get; private set; }
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        {
            FormatCalls++;
            return new CollapseParagraph(request.Text.Length == 1, false);
        }
    }

    private sealed class CollapseParagraph(bool symbol, bool collapsed) : IPortableTextParagraph
    {
        public PortableTextCollapsedRange? CollapsedRange => collapsed ? new(0, 1, 3, 1) : null;
        public IPortableTextParagraph Collapse(in PortableTextCollapseRequest request)
        {
            Assert.Equal(0, request.LineIndex); Assert.Equal(7, request.Width); Assert.Equal(3, request.SymbolWidth);
            Assert.Equal(PortableTextTrimming.Character, request.Trimming);
            return new CollapseParagraph(false, true);
        }
        public ReadOnlyMemory<PortableTextGlyph> Glyphs => symbol ? new PortableTextGlyph[] { new(0, 0, 1, 0, 0, 3, 0) } :
            collapsed ? new PortableTextGlyph[] { new(0, 0, 1, 0, 0, 4, 0), new(0, 1, 3, 4, 0, 3, 0, IsCollapseSymbol: true) } :
            new PortableTextGlyph[] { new(0, 0, 1, 0, 0, 4, 0), new(0, 1, 2, 4, 0, 4, 0), new(0, 2, 3, 8, 0, 4, 0) };
        public ReadOnlyMemory<PortableTextLineInfo> Lines => new PortableTextLineInfo[] { new(0, Glyphs.Length, 0,
            symbol ? 1 : 3, symbol ? 3 : collapsed ? 7 : 12, 0, 20) };
        public PortableTextHit HitTest(int lineIndex, float distance) => new(3, true);
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => hit.Position == 3 ? 7 : hit.Position * 4;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => previous ? position > 1 ? 1 : 0 : position < 1 ? 1 : 3;
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        {
            rectangles[0] = symbol ? new(0, 0, 3, 20) : collapsed && start >= 1 ? new(4, 0, 3, 20) : new(0, 0, 4, 20);
            return 1;
        }
    }

    // A Windows-MIL test process must not switch its frozen resource domain.
    // The explicit-portable Windows host/application gate covers that platform.
    private sealed class PortableMediaFactAttribute : FactAttribute
    {
        public PortableMediaFactAttribute([CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires a process initialized with portable media before WPF construction.";
        }
    }

    [PortableMediaFact]
    public void PublicFormatterUsesProviderForSimpleLatinAndRetainsContinuationAfterUnregister()
    {
        var source = new Source { Properties = new Properties(new FontFamily(
            Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "Inter-Medium.ttf") + "#Inter")) };
        var properties = new ParagraphProperties(source.Properties, false, false);
        var provider = new Provider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var first = formatter.FormatLine(source, 0, 8.0 / 3, properties, null, new TextRunCache());
        Assert.IsType<PortableTextLine>(first);
        Assert.Equal("abc", provider.Text);
        Assert.Equal(8, first.Width);
        using var original = first.GetTextLineBreak();
        using var continuation = original.Clone();
        original.Dispose(); first.Dispose(); registration.Dispose();
        using var second = formatter.FormatLine(source, 2, 8.0 / 3, properties, continuation, new TextRunCache());
        Assert.IsType<PortableTextLine>(second);
        Assert.Equal(6, second.Width);
        Assert.Equal(1, provider.Calls);
        Assert.False(TextFormatterImp.IsNativeLineServicesAvailable);
    }

    [PortableMediaFact]
    public void PublicFormatterDoesNotHideProviderFailureOrNullParagraph()
    {
        var source = new Source();
        var properties = new ParagraphProperties(source.Properties, false);
        var failure = new InvalidOperationException("Fixture provider failure");
        using var formatter = new TextFormatterImp();
        using (PortableWpfServiceRegistry.RegisterTextFormatting(new Provider { Failure = failure }))
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                formatter.FormatLine(source, 0, 30, properties, null, new TextRunCache())));
        using (PortableWpfServiceRegistry.RegisterTextFormatting(new Provider { NullParagraph = true }))
            Assert.Contains("no paragraph", Assert.Throws<InvalidOperationException>(() =>
                formatter.FormatLine(source, 0, 30, properties, null, new TextRunCache())).Message);
    }

    [PortableMediaFact]
    public void PortableFormattingRejectsMissingMeasurementAndOptimalLineServicesOperations()
    {
        var source = new Source();
        var properties = new ParagraphProperties(source.Properties, false);
        var provider = new Provider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        Assert.Contains("intrinsic", Assert.Throws<PlatformNotSupportedException>(() =>
            formatter.FormatMinMaxParagraphWidth(source, 0, properties)).Message);
        Assert.Throws<PlatformNotSupportedException>(() =>
            formatter.RecreateLine(source, 0, 2, 30, properties, null, new TextRunCache()));
        Assert.Throws<PlatformNotSupportedException>(() =>
            formatter.CreateParagraphCache(source, 0, 30, properties, null, new TextRunCache()));
        Assert.Throws<PlatformNotSupportedException>(() => formatter.AcquireContext(new object(), IntPtr.Zero));
        Assert.Equal(1, provider.Calls);
    }

    [PortableMediaFact]
    public void IntrinsicMeasurementUsesProviderMetricsNotFormattedLineWidth()
    {
        var source = new Source();
        var properties = new ParagraphProperties(source.Properties, false);
        var provider = new Provider { IntrinsicWidths = new(5, 20) };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var widths = formatter.FormatMinMaxParagraphWidth(source, 0, properties);
        Assert.Equal(5, widths.MinWidth);
        Assert.Equal(20, widths.MaxWidth);
        Assert.True(provider.MeasureIntrinsicWidths);
        Assert.Equal(1, provider.Calls);
    }

    [PortableMediaFact]
    public void WrapWithOverflowSelectsWholeWordNativePolicy()
    {
        var source = new Source();
        var provider = new Provider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        using var line = formatter.FormatLine(source, 0, 1,
            new ParagraphProperties(source.Properties, false, wrapping: TextWrapping.WrapWithOverflow), null, new TextRunCache());
        Assert.IsType<PortableTextLine>(line);
        Assert.Equal(PortableTextWrapping.WholeWord, provider.Wrapping);
        Assert.Equal(1, provider.Calls);
    }

    [PortableMediaFact]
    public void IntrinsicMeasurementVisitsHardLinesAndPreservesPropertyScopes()
    {
        var p = new Properties();
        var source = new DocumentSource([new Modifier(properties => new Properties { Size = 24 }),
            new TextCharacters("abc", p), new TextEndOfLine(1), new TextCharacters("abc", p), new TextEndOfSegment(1)]);
        var provider = new Provider { IntrinsicWidths = new(5, 20) };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var widths = formatter.FormatMinMaxParagraphWidth(source, 0, new ParagraphProperties(p, false));
        Assert.Equal(5, widths.MinWidth);
        Assert.Equal(20, widths.MaxWidth);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(24, provider.Styles.Span[0].FontSize);
    }

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
        Assert.True(first.IsAtCaretCharacterHit(new(2, 1), 0));
        Assert.True(first.IsAtCaretCharacterHit(new(4, 0), 0)); // Equivalent hidden formatting edge.
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
        Assert.True(line.IsAtCaretCharacterHit(new(0, 2), 0));
        Assert.True(line.IsAtCaretCharacterHit(new(2, 0), 0));
        Assert.False(line.IsAtCaretCharacterHit(new(1, 0), 0));
        Assert.False(line.IsAtCaretCharacterHit(new(0, 1), 0)); // Inside one native cluster.
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

    [PortableMediaFact]
    public void NativeRangeUnderlinePreservesSourceBrushFontMetricsInkAndContinuation()
    {
        var provider = new Provider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var source = new Source { Properties = new Properties(new FontFamily(
            Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "Inter-Medium.ttf") + "#Inter"))
            { Decorations = TextDecorations.Underline } };
        using var first = PortableTextLine.Create(Settings(formatter, source), 0, 800, 1);
        var underline = Assert.Single(DrawLeaves(first).OfType<GeometryDrawing>());
        var glyph = Assert.Single(first.GetIndexedGlyphRuns()).GlyphRun;
        var bounds = underline.Geometry.Bounds;
        Assert.Null(underline.Pen);
        Assert.Same(source.Properties.ForegroundBrush, underline.Brush);
        Assert.Equal(8, bounds.Width);
        Assert.Equal(glyph.GlyphTypeface.UnderlineThickness * glyph.FontRenderingEmSize, bounds.Height, 6);
        Assert.Equal(first.Baseline - glyph.GlyphTypeface.UnderlinePosition * glyph.FontRenderingEmSize,
            bounds.Top + bounds.Height / 2, 6);
        Rect expectedInk = glyph.ComputeInkBoundingBox();
        if (!expectedInk.IsEmpty) expectedInk.Offset(glyph.BaselineOrigin.X, glyph.BaselineOrigin.Y);
        expectedInk.Union(bounds);
        Assert.Equal(expectedInk.Height, first.Extent, 6);
        using var continuation = first.GetTextLineBreak();
        first.Dispose();
        using var second = PortableTextLine.Create(Settings(formatter, source, continuation), 2, 800, 1);
        Assert.Equal(6, Assert.Single(DrawLeaves(second).OfType<GeometryDrawing>()).Geometry.Bounds.Width);
        Assert.Equal(1, provider.Calls);
    }

    [PortableMediaFact]
    public void UnderlineUsesNativeTabRangeButDoesNotDrawTrailingWhitespace()
    {
        var provider = new Provider { Tabs = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var properties = new Properties(new FontFamily(Path.Combine(AppContext.BaseDirectory,
            "LibreWPF", "Fonts", "Inter-Medium.ttf") + "#Inter")) { Decorations = TextDecorations.Underline };
        using var line = PortableTextLine.Create(Settings(formatter, new Source { Text = "a\tb", Properties = properties }), 0, 800, 1);
        Assert.Equal(38, Assert.Single(DrawLeaves(line).OfType<GeometryDrawing>()).Geometry.Bounds.Width);
        using var spaces = PortableTextLine.Create(Settings(formatter, new Source { Text = " \t ", Properties = properties }), 0, 800, 1);
        Assert.Empty(DrawLeaves(spaces).OfType<GeometryDrawing>());
    }

    [PortableMediaFact]
    public void ContinuousUnderlineRejectsUnimplementedMixedMetricAveraging()
    {
        var provider = new Provider { MixedOneLine = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp();
        var family = new FontFamily(Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "Inter-Medium.ttf") + "#Inter");
        var source = new Source
        {
            Mixed = true,
            Properties = new Properties(family) { Decorations = TextDecorations.Underline },
            FollowingProperties = new Properties(family) { Size = 24, Decorations = TextDecorations.Underline }
        };
        Assert.Contains("averaging", Assert.Throws<PlatformNotSupportedException>(() =>
            PortableTextLine.Create(Settings(formatter, source), 0, 800, 1)).Message);
    }

    [PortableMediaFact]
    public void UnsupportedDecorationShapesRemainExplicit()
    {
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new Provider());
        using var formatter = new TextFormatterImp();
        TextDecoration[] decorations =
        [
            new() { Location = TextDecorationLocation.Strikethrough },
            new() { Pen = new Pen(Brushes.Red, 2) },
            new() { PenOffset = 1 },
            new() { PenThicknessUnit = TextDecorationUnit.Pixel }
        ];
        foreach (var decoration in decorations)
        {
            var source = new Source { Properties = new Properties { Decorations = new() { decoration } } };
            Assert.Contains("decorations", Assert.Throws<PlatformNotSupportedException>(() =>
                PortableTextLine.Create(Settings(formatter, source), 0, 800, 1)).Message);
        }
    }

    private static IEnumerable<Drawing> DrawLeaves(TextLine line)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) line.Draw(context, new Point(), InvertAxes.None);
        return Leaves(visual.Drawing);
        static IEnumerable<Drawing> Leaves(Drawing drawing)
        {
            if (drawing is DrawingGroup group)
                foreach (Drawing child in group.Children) foreach (Drawing leaf in Leaves(child)) yield return leaf;
            else yield return drawing;
        }
    }

    private sealed class Source : TextSource
    {
        internal Properties Properties { get; init; } = new();
        internal Properties? FollowingProperties { get; init; }
        internal bool Mixed { get; init; }
        internal bool AutoHeight { get; init; }
        internal string Text { get; init; } = "abc";
        public override TextRun GetTextRun(int index) => index >= Text.Length ? new TextEndOfParagraph(1) :
            new TextCharacters(Text, index, Mixed && index == 0 ? 1 : Text.Length - index,
                Mixed && index != 0 ? FollowingProperties ?? new Properties { Size = 24 } : Properties);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(limit, new(CultureInfo.InvariantCulture, new CharacterBufferRange(Text, 0, Math.Min(limit, Text.Length))));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    private sealed class Properties : TextRunProperties
    {
        private readonly Typeface _face;
        internal TextDecorationCollection? Decorations { get; init; }
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
        public override TextDecorationCollection TextDecorations => Decorations!;
        public override Brush ForegroundBrush => Size == 24 ? Brushes.Red : Brushes.Black;
        public override Brush BackgroundBrush => Size == 24 ? Brushes.Blue : null!;
        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
        public override TextEffectCollection TextEffects => null!;
    }

    private sealed class ParagraphProperties(TextRunProperties properties, bool autoHeight, bool rightToLeft = true,
        TextWrapping wrapping = TextWrapping.Wrap) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => autoHeight ? 0 : 20;
        public override double DefaultIncrementalTab => 32;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => wrapping;
        public override TextMarkerProperties TextMarkerProperties => null!;
        public override double Indent => 0;
    }

    // A typed source contract fixture, not native shaping/parity evidence.
    private sealed class Provider : IPortableTextFormatting, IPortableTextParagraph
    {
        public PortableTextIntrinsicWidths? IntrinsicWidths { get; init; }
        internal bool MeasureIntrinsicWidths { get; private set; }
        internal PortableTextWrapping Wrapping { get; private set; }
        internal Exception? Failure { get; init; }
        internal bool NullParagraph { get; init; }
        internal bool Mixed { get; init; }
        internal bool Tabs { get; init; }
        internal bool MixedOneLine { get; init; }
        internal bool Empty { get; init; }
        internal float IncrementalTab { get; private set; }
        internal ReadOnlyMemory<PortableTextStyle> Styles { get; private set; }
        public object NativeFont { get; } = new();
        public object SecondNativeFont { get; } = new();
        public object GetNativeFont(uint fontIndex) => fontIndex == 0 ? NativeFont : SecondNativeFont;
        internal int Calls { get; private set; }
        internal string? Text { get; private set; }
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        {
            Calls++;
            MeasureIntrinsicWidths = request.MeasureIntrinsicWidths;
            Wrapping = request.Wrapping;
            if (Failure != null) throw Failure;
            if (NullParagraph) return null!;
            Text = request.Text.ToString(); Styles = request.Styles; IncrementalTab = request.IncrementalTab;
            Assert.False(request.Font.Data.IsEmpty); return this;
        }
        public ReadOnlyMemory<PortableTextGlyph> Glyphs => Empty ? ReadOnlyMemory<PortableTextGlyph>.Empty : Tabs ? new PortableTextGlyph[]
        { new(0, 0, 1, 0, 0, 8, 0), new(uint.MaxValue, 1, 2, 8, 0, 24, 0, IsTab: true), new(0, 2, 3, 32, 0, 6, 0) } : MixedOneLine ? new PortableTextGlyph[]
        { new(0, 0, 1, 0, 0, 4, 0), new(0, 1, 2, 4, 0, 6, 0, 1), new(0, 2, 3, 10, 0, 6, 0, 1) } : Mixed ? new PortableTextGlyph[]
        { new(0, 0, 1, 0, 0, 4, 0), new(0, 1, 2, 0, 20, 6, 0, 1), new(0, 2, 3, 6, 20, 6, 0, 1) } : new PortableTextGlyph[]
        { new(0, 0, 2, 0, 0, 8, 1), new(0, 2, 3, 0, 20, 6, 1) };
        public ReadOnlyMemory<PortableTextLineInfo> Lines => Empty ? new PortableTextLineInfo[] { new(0, 0, 0, 0, 0, 0, 20) } : Tabs ? new PortableTextLineInfo[]
        { new(0, 3, 0, 3, 38, 0, 20) } : MixedOneLine ? new PortableTextLineInfo[]
        { new(0, 3, 0, 3, 16, 0, 20) } : Mixed ? new PortableTextLineInfo[]
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
