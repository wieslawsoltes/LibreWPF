// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Media.TextFormatting;
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media;

// Actual source formatter/line/GlyphRun dispatch with recorded typed producers.
// No native shaping, ppem policy, raster pixels or Display parity is simulated.
[Collection("Sequential")]
public sealed class PortableDisplayTextSourceTests
{
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
    public void ExplicitHintingDoesNotAdmitOrdinaryDisplayOrCallIdeal()
    {
        var provider = new HintedOnlyProvider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        var source = new Source();
        Assert.Throws<PlatformNotSupportedException>(() => Format(formatter, source));
        Assert.Equal(0, provider.Calls);
    }

    [PortableMediaFact]
    public void DisplayRequestPreservesOriginalDoubleEmDeviceAndWholeSource()
    {
        var sentinel = new InvalidOperationException("captured original Display request");
        var provider = new DisplayProvider { FormatFailure = sentinel };
        var source = new Source { Text = "ab", PixelsPerDip = 1.5, Properties = new Properties(Math.BitIncrement(13d)) };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        Assert.Same(sentinel, Assert.Throws<InvalidOperationException>(() => Format(formatter, source)));
        Assert.Equal(source.Properties.FontRenderingEmSize, provider.Options.EmSize);
        Assert.Equal(source.Properties.FontRenderingEmSize, Assert.Single(provider.Styles).EmSize);
        Assert.Equal(1.5, provider.Options.PixelsPerDip);
        Assert.Equal("ab", provider.Text);
        Assert.NotEqual((double)provider.Request.FontSize, provider.Options.EmSize);
        Assert.Equal(PortableTextWrapping.WholeWord, provider.Request.Wrapping);
        Assert.Equal(1, provider.FormatCalls);
        Assert.Equal(0, provider.IdealCalls);
    }

    [PortableMediaFact]
    public void SourceDrawingAndInteractionUseTheSameDoubleDisplayMetrics()
    {
        var provider = new DisplayProvider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        using var line = Format(formatter, new Source());
        Assert.Equal(Advance, line.Width);
        Assert.Equal(LineHeight, line.Height);
        Assert.Equal(Baseline, line.Baseline);
        var run = Assert.Single(line.GetIndexedGlyphRuns()).GlyphRun;
        Assert.Equal(Advance, Assert.Single(run.AdvanceWidths));
        Assert.Equal(new Point(1d / 3, 2d / 3), Assert.Single(run.GlyphOffsets));
        Assert.Equal(new Point(0, Baseline), run.BaselineOrigin);
        double hitX = Math.BitIncrement(Advance);
        _ = line.GetCharacterHitFromDistance(hitX);
        Assert.Equal(hitX, provider.LastHit);
        Assert.Equal(Advance, line.GetDistanceFromCharacterHit(new CharacterHit(1, 0)));
        Assert.Equal(Advance, Assert.Single(line.GetTextBounds(0, 1)).Rectangle.Width);
        Assert.Throws<NotSupportedException>(() => run.GetDistanceFromCaretCharacterHit(new(0, 0)));
        Assert.Throws<NotSupportedException>(() => run.BuildGeometry());
        Assert.Equal(0, provider.IdealCalls);
        Assert.Equal(1, provider.BindCalls);
    }

    [PortableMediaFact]
    public void IntrinsicMeasurementUsesOriginalDoubleWidthsWithoutGlyphPublication()
    {
        var provider = new DisplayProvider();
        var source = new Source { Text = "ab" };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        var widths = formatter.FormatMinMaxParagraphWidth(source, 0, new ParagraphProperties(source.Properties));
        Assert.Equal(Advance, widths.MinWidth);
        Assert.Equal(Advance * 2, widths.MaxWidth);
        Assert.True(provider.Request.MeasureIntrinsicWidths);
        Assert.Equal(0, provider.BindCalls);
        Assert.True(Assert.Single(provider.Paragraphs).IsDisposed);
    }

    [PortableMediaFact]
    public void InitialIdentityOrMetricCoverageMismatchRetiresProducerBeforePublication()
    {
        foreach (string mutation in new[] { "dpi", "em", "text", "styles", "coverage" })
        {
            var provider = new DisplayProvider { InitialMutation = mutation };
            using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
            using var formatter = new TextFormatterImp(TextFormattingMode.Display);
            Assert.Throws<InvalidOperationException>(() => Format(formatter, new Source()));
            Assert.True(Assert.Single(provider.Paragraphs).IsDisposed);
            Assert.Equal(0, provider.RetainCalls);
            Assert.Equal(0, provider.BindCalls);
        }
    }

    [PortableMediaFact]
    public void RetainCannotRelabelOriginalDisplayIdentity()
    {
        foreach (string mutation in new[] { "dpi", "em", "text", "styles" })
        {
            var provider = new DisplayProvider { RetainMutation = mutation };
            using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
            using var formatter = new TextFormatterImp(TextFormattingMode.Display);
            Assert.Throws<InvalidOperationException>(() => Format(formatter, new Source()));
            Assert.Equal(2, provider.Paragraphs.Count);
            Assert.All(provider.Paragraphs, paragraph => Assert.True(paragraph.IsDisposed));
            Assert.Equal(0, provider.BindCalls);
        }
    }

    [PortableMediaFact]
    public void ContinuationUsesOriginalDoubleReflowAfterLineAndProviderRemoval()
    {
        var provider = new DisplayProvider();
        var source = new Source { Text = "ab" };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        using var first = Format(formatter, source, width: 10);
        using var original = first.GetTextLineBreak();
        using var continuation = original.Clone();
        first.PixelsPerDip = 2; // Public mutable metadata cannot change retained identity.
        original.Dispose(); first.Dispose(); registration.Dispose();
        source.ForbidReads = true;
        using var second = Format(formatter, source, 1, 20, continuation);
        Assert.Equal(1, provider.FormatCalls);
        Assert.Equal(1, provider.ReflowCalls);
        Assert.Equal(1, provider.ReflowStart);
        Assert.Equal(20d, provider.ReflowWidth);
        Assert.Equal("ab", provider.Text);
        Assert.Equal(Advance, second.Width);
        Assert.Equal(1.5, second.PixelsPerDip);
    }

    [PortableMediaFact]
    public void ReflowIdentityFailureRetiresReturnedProducerAndKeepsBreakUsable()
    {
        var provider = new DisplayProvider();
        var source = new Source { Text = "ab" };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        using var first = Format(formatter, source, width: 10);
        using var continuation = first.GetTextLineBreak();
        provider.ReflowMutation = "dpi";
        Assert.Throws<InvalidOperationException>(() => Format(formatter, source, 1, 20, continuation));
        Assert.True(provider.Paragraphs[^1].IsDisposed);
        provider.ReflowMutation = null;
        source.ForbidReads = true;
        using var second = Format(formatter, source, 1, 20, continuation);
        Assert.Equal(2, provider.ReflowCalls);
        Assert.Equal(1, provider.FormatCalls);
        Assert.Equal(Advance, second.Width);
    }

    [PortableMediaFact]
    public void ChangedContinuationDeviceRejectsBeforeNativeReflow()
    {
        var provider = new DisplayProvider();
        var source = new Source { Text = "ab" };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        using var first = Format(formatter, source, width: 10);
        using var continuation = first.GetTextLineBreak();
        source.PixelsPerDip = 2;
        Assert.Throws<PlatformNotSupportedException>(() => Format(formatter, source, 1, 20, continuation));
        Assert.Equal(0, provider.ReflowCalls);
    }

    [PortableMediaFact]
    public void UnsupportedTextAndCollapseNeverFallBackToIdealOrFormatASymbol()
    {
        foreach (string text in new[] { "", "a\t" })
        {
            var provider = new DisplayProvider();
            using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
            using var formatter = new TextFormatterImp(TextFormattingMode.Display);
            Assert.Throws<PlatformNotSupportedException>(() => Format(formatter, new Source { Text = text }));
            Assert.Equal(0, provider.FormatCalls);
            Assert.Equal(0, provider.IdealCalls);
        }
        var collapseProvider = new DisplayProvider();
        using var collapseRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(collapseProvider);
        using var collapseFormatter = new TextFormatterImp(TextFormattingMode.Display);
        var source = new Source();
        using var line = Format(collapseFormatter, source);
        Assert.Throws<PlatformNotSupportedException>(() => line.Collapse(new TextTrailingCharacterEllipsis(1, source.Properties)));
        Assert.Equal(1, collapseProvider.FormatCalls);
        Assert.Equal(0, collapseProvider.IdealCalls);
    }

    [PortableMediaFact]
    public void BindingRetainCannotDropTheOriginalDoubleDeviceIdentity()
    {
        var provider = new DisplayProvider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        using var line = Format(formatter, new Source());
        var run = Assert.Single(line.GetIndexedGlyphRuns()).GlyphRun;
        provider.BindingRetainMismatch = true;
        Assert.Throws<InvalidOperationException>(() => ((IPortableHintedGlyphRunSource)run).TryAcquirePortableHintedGlyphRun(out _));
        Assert.True(provider.Bindings[^1].IsDisposed);
        provider.BindingRetainMismatch = false;
        Assert.True(((IPortableHintedGlyphRunSource)run).TryAcquirePortableHintedGlyphRun(out var retained));
        using (retained) Assert.Equal(1.5, Assert.IsAssignableFrom<IPortableDisplayGlyphRunBinding>(retained).SourcePixelsPerDip);
    }

    [PortableMediaFact]
    public void InvalidBindingFrameFailsBeforeInkReadAndRetiresTheBinding()
    {
        var provider = new DisplayProvider { BindingFrameMismatch = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        Assert.Throws<ArgumentException>(() => Format(formatter, new Source()));
        Assert.True(Assert.Single(provider.Bindings).IsDisposed);
        Assert.Equal(0, provider.InkReads);
    }

    [PortableMediaFact]
    public void ChangedMetricsCopyCannotReplaceParagraphAdvances()
    {
        var provider = new DisplayProvider { CopiedAdvanceMismatch = true };
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        using var formatter = new TextFormatterImp(TextFormattingMode.Display);
        Assert.Throws<InvalidOperationException>(() => Format(formatter, new Source()));
        Assert.Equal(0, provider.BindCalls);
        Assert.All(provider.Paragraphs, paragraph => Assert.True(paragraph.IsDisposed));
    }

    private const double Advance = 7 / 1.5, Baseline = 13 / 1.5, LineHeight = 20 / 1.5;
    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "Inter-Medium.ttf");
    private static TextLine Format(TextFormatterImp formatter, Source source, int first = 0, double width = 100,
        TextLineBreak? previous = null) => formatter.FormatLine(source, first, width,
            new ParagraphProperties(source.Properties), previous, new TextRunCache());

    private sealed class Source : TextSource
    {
        internal Source() => PixelsPerDip = 1.5;
        internal string Text { get; init; } = "a";
        internal Properties Properties { get; init; } = new(12);
        internal bool ForbidReads;
        public override TextRun GetTextRun(int index) => ForbidReads ? throw new InvalidOperationException("Continuation re-fetched source") :
            index >= Text.Length ? new TextEndOfParagraph(1) : new TextCharacters(Text, index, Text.Length - index, Properties);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int limit) =>
            new(0, new(CultureInfo.InvariantCulture, new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
    }

    private sealed class Properties(double size) : TextRunProperties
    {
        public override Typeface Typeface { get; } = new(new FontFamily(FontPath + "#Inter"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public override double FontRenderingEmSize => size;
        public override double FontHintingEmSize => size;
        public override TextDecorationCollection TextDecorations => null!;
        public override Brush ForegroundBrush => Brushes.Black;
        public override Brush BackgroundBrush => null!;
        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
        public override TextEffectCollection TextEffects => null!;
    }

    private sealed class ParagraphProperties(TextRunProperties properties) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => TextWrapping.WrapWithOverflow;
        public override TextMarkerProperties TextMarkerProperties => null!;
        public override double Indent => 0;
    }

    private sealed class HintedOnlyProvider : IPortableTextFormatting, IPortableHintedTextFormatting
    {
        internal int Calls;
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request) { Calls++; throw new InvalidOperationException("Ideal fallback"); }
        public IPortableHintedTextParagraph FormatHinted(in PortableTextParagraphRequest request,
            ReadOnlySpan<PortableTextStyleMetrics> metrics, ReadOnlySpan<PortableTextHintingStyle> devices,
            in PortableHintedTextOptions options, ReadOnlySpan<int> coordinates = default, ReadOnlySpan<short> normalized = default)
        { Calls++; throw new InvalidOperationException("Explicit hinting is not Display"); }
    }

    private sealed class DisplayProvider : IPortableDisplayTextFormatting
    {
        internal int IdealCalls, FormatCalls, RetainCalls, ReflowCalls, ReflowStart, BindCalls, InkReads;
        internal double LastHit, ReflowWidth;
        internal bool BindingRetainMismatch, BindingFrameMismatch, CopiedAdvanceMismatch;
        internal string? InitialMutation, RetainMutation, ReflowMutation;
        internal Exception? FormatFailure;
        internal string Text = "";
        internal PortableDisplayTextOptions Options;
        internal PortableDisplayTextStyle[] Styles = [];
        internal PortableTextParagraphRequest Request;
        internal readonly List<DisplayParagraph> Paragraphs = new();
        internal readonly List<DisplayBinding> Bindings = new();
        internal readonly ushort Glyph = new GlyphTypeface(new Uri(FontPath)).CharacterToGlyphMap['a'];
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        { IdealCalls++; throw new InvalidOperationException("Display called Ideal"); }
        public IPortableDisplayTextParagraph FormatDisplay(in PortableTextParagraphRequest request,
            ReadOnlySpan<PortableDisplayTextStyle> styles, in PortableDisplayTextOptions options)
        {
            FormatCalls++; Text = request.Text.ToString(); Styles = styles.ToArray(); Options = options; Request = request;
            if (FormatFailure != null) throw FormatFailure;
            return new DisplayParagraph(this, 0, InitialMutation);
        }
    }

    private sealed class DisplayParagraph : IPortableDisplayTextParagraph
    {
        internal readonly DisplayProvider Provider;
        private readonly int _start;
        private readonly string? _mutation;
        private readonly PortableTextGlyph[] _glyphs;
        private readonly PortableTextLineInfo[] _lines;
        internal DisplayParagraph(DisplayProvider provider, int start, string? mutation = null)
        {
            Provider = provider; _start = start; _mutation = mutation;
            int count = provider.Text.Length - start;
            _glyphs = Enumerable.Range(start, count).Select(index => new PortableTextGlyph(provider.Glyph,
                index, index + 1, -123, -234, -345, 0)).ToArray(); // Legacy geometry is deliberately unusable.
            _lines = Enumerable.Range(0, count).Select(index => new PortableTextLineInfo(index, 1,
                start + index, start + index + 1, -456, -567, -678)).ToArray();
            DisplayGlyphMetrics = Enumerable.Range(0, count).Select(index => new PortableDisplayTextGlyphMetrics(0, Baseline, Advance)).ToArray();
            DisplayLineMetrics = Enumerable.Range(0, count).Select(index => new PortableDisplayTextLineMetrics(
                Advance, index * LineHeight, LineHeight, Baseline, index * LineHeight + Baseline)).ToArray();
            provider.Paragraphs.Add(this);
        }
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
        public double SourceEmSize => Provider.Options.EmSize + (_mutation == "em" ? 1 : 0);
        public double SourcePixelsPerDip => Provider.Options.PixelsPerDip + (_mutation == "dpi" ? 1 : 0);
        public float DpiScale => (float)Provider.Options.PixelsPerDip;
        public ReadOnlyMemory<char> SourceText => (_mutation == "text" ? "changed" : Provider.Text).AsMemory();
        public ReadOnlyMemory<PortableDisplayTextStyle> SourceStyles => _mutation == "styles" ? [] : Provider.Styles;
        public ReadOnlyMemory<PortableDisplayTextGlyphMetrics> DisplayGlyphMetrics { get; }
        private readonly ReadOnlyMemory<PortableDisplayTextLineMetrics> _displayLines;
        public ReadOnlyMemory<PortableDisplayTextLineMetrics> DisplayLineMetrics
        { get => _mutation == "coverage" ? [] : _displayLines; private init => _displayLines = value; }
        public PortableDisplayTextIntrinsicWidths? DisplayIntrinsicWidths => new(Advance, Advance * Provider.Text.Length);
        public ReadOnlyMemory<PortableTextGlyph> Glyphs => _glyphs;
        public ReadOnlyMemory<PortableTextLineInfo> Lines => _lines;
        ReadOnlyMemory<PortableHintedTextGlyph> IPortableHintedTextParagraph.Glyphs => throw new InvalidOperationException("Legacy glyph metrics");
        ReadOnlyMemory<PortableHintedTextLine> IPortableHintedTextParagraph.Lines => throw new InvalidOperationException("Legacy line frames");
        public ReadOnlyMemory<PortableHintedTextClusterBox> Boxes => throw new InvalidOperationException("Legacy cluster boxes");
        public ReadOnlyMemory<PortableHintedTextCaret> Carets => throw new InvalidOperationException("Legacy carets");
        public PortableTextHit HitTest(int line, float x) => throw new InvalidOperationException("Narrowed hit input");
        public float GetCaretDistance(int line, PortableTextHit hit) => throw new InvalidOperationException("Narrowed caret output");
        public PortableTextHit HitTestDisplay(int line, double x) { Provider.LastHit = x; return new(_lines[line].InputEnd, false); }
        public double GetDisplayCaretDistance(int line, PortableTextHit hit) => hit.Position == _lines[line].InputStart ? 0 : Advance;
        public int GetNextLogicalCaret(int line, int position, bool previous) => Math.Clamp(position + (previous ? -1 : 1), _lines[line].InputStart, _lines[line].InputEnd);
        public int GetSelection(int line, int start, int end, Span<PortableRect> rectangles)
        { rectangles[0] = new(0, 0, Advance, LineHeight); return 1; }
        public IPortableHintedTextParagraph Retain()
        { Provider.RetainCalls++; return new DisplayParagraph(Provider, _start, Provider.RetainMutation); }
        public IPortableDisplayTextParagraph ReflowDisplay(int start, double width)
        {
            Provider.ReflowCalls++; Provider.ReflowStart = start; Provider.ReflowWidth = width;
            return new DisplayParagraph(Provider, start, Provider.ReflowMutation);
        }
        public IPortableHintedTextGlyphRun AcquireGlyphRun(ReadOnlySpan<int> indices) => new DisplayRun(this, indices.ToArray());
    }

    private sealed class DisplayRun(DisplayParagraph paragraph, int[] indices) : IPortableHintedTextGlyphRun, IPortableDisplayGlyphRunBindingFactory
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
        public ReadOnlyMemory<int> PositionedGlyphIndices => indices;
        public IPortableHintedTextGlyphRun Retain() => new DisplayRun(paragraph, indices.ToArray());
        public IPortableHintedTextParagraph AcquireParagraph() => paragraph.Retain();
        public void CopyDisplaySourceMetrics(double em, double dpi, Span<double> advances, Span<PortablePoint> offsets)
        {
            Assert.Equal(paragraph.SourceEmSize, em); Assert.Equal(paragraph.SourcePixelsPerDip, dpi);
            for (int index = 0; index < indices.Length; index++)
            { advances[index] = paragraph.Provider.CopiedAdvanceMismatch ? (float)Advance : Advance; offsets[index] = new(1d / 3, 2d / 3); }
        }
        public IPortableDisplayGlyphRunBinding BindDisplayGlyphRun(PortableTextFont font, double em, double dpi,
            PortablePoint baseline, ReadOnlySpan<double> advances, ReadOnlySpan<PortablePoint> offsets)
        {
            paragraph.Provider.BindCalls++;
            Assert.Equal(paragraph.SourceEmSize, em); Assert.Equal(paragraph.SourcePixelsPerDip, dpi);
            Assert.Equal(Advance, advances[0]); Assert.Equal(1d / 3, offsets[0].X);
            return new DisplayBinding(paragraph.Provider, em, dpi, baseline);
        }
    }

    private sealed class DisplayBinding : IPortableDisplayGlyphRunBinding
    {
        private readonly DisplayProvider _provider;
        private readonly PortablePoint _baseline;
        internal DisplayBinding(DisplayProvider provider, double em, double dpi, PortablePoint baseline)
        { _provider = provider; SourceEmSize = em; SourcePixelsPerDip = dpi; _baseline = baseline; provider.Bindings.Add(this); }
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
        public double SourceEmSize { get; }
        public double SourcePixelsPerDip { get; }
        public float FontRenderingEmSize => (float)SourceEmSize;
        public float DpiScale => (float)SourcePixelsPerDip;
        public sbyte BidiLevel => 0;
        public Vector2 Origin => default;
        public PortableHintedGlyphSourceFrame SourceFrame => throw new InvalidOperationException("Legacy source frame");
        public PortableDisplayGlyphSourceFrame DisplaySourceFrame => new(0, Baseline,
            new(_baseline.X, _baseline.Y + (_provider.BindingFrameMismatch ? 1 : 0)));
        public ReadOnlyMemory<ushort> GlyphIndices => new[] { _provider.Glyph };
        public ReadOnlyMemory<Vector2> GlyphPositions => new[] { Vector2.Zero };
        public PortableRect InkBounds { get { _provider.InkReads++; return PortableRect.Empty; } }
        public PortableRect BaselineRelativeInkBounds => PortableRect.Empty;
        public IPortableHintedGlyphRunBinding Retain() => new DisplayBinding(_provider, SourceEmSize,
            SourcePixelsPerDip + (_provider.BindingRetainMismatch ? 1 : 0), _baseline);
        public IPortableHintedTextGlyphRun AcquireGlyphRun() => throw new NotSupportedException("Recorded source binding has no native lease");
    }
}
