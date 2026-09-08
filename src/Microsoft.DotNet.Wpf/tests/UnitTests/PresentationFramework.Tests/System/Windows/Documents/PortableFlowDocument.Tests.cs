// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Documents;
using ProGPU.Wpf.Interop;

namespace System.Windows.Documents;

[Collection("Sequential")]
public sealed class PortableFlowDocumentTests
{
    private sealed class PortableMediaFactAttribute : FactAttribute
    {
        public PortableMediaFactAttribute([CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before WPF construction.";
        }
    }

    [PortableMediaFact]
    public void ParagraphSourceRetainsOriginalEdgesScopesAndSurrogateBoundaries()
    {
        var run = new Run(new string('a', 4095) + "\U0001F600" + "tail");
        var link = new Hyperlink(run);
        var paragraph = new Paragraph(link);
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("next"));
        var document = new FlowDocument(paragraph);
        var source = new PortableDocumentParagraphSource(paragraph, 1);
        Assert.Equal(paragraph.ElementStart.Offset, source.Start);
        Assert.Equal(paragraph.ElementEnd.Offset, source.End);
        Assert.IsType<TextHidden>(source.GetTextRun(source.Start));
        Assert.IsAssignableFrom<TextModifier>(source.GetTextRun(link.ElementStart.Offset));
        Assert.IsType<TextEndOfSegment>(source.GetTextRun(link.ContentEnd.Offset));
        TextCharacters first = Assert.IsType<TextCharacters>(source.GetTextRun(run.ContentStart.Offset));
        Assert.Equal(4095, first.Length);
        TextCharacters next = Assert.IsType<TextCharacters>(source.GetTextRun(run.ContentStart.Offset + first.Length));
        Assert.Equal(6, next.Length);
        Assert.Equal(4096, source.GetPrecedingText(run.ContentEnd.Offset).Length);
        Assert.Equal(0, source.GetPrecedingText(source.Start).Length);
        Assert.IsType<TextEndOfParagraph>(source.GetTextRun(source.End - 1));
        Assert.Same(document, paragraph.Parent);
        Assert.Same(link, run.Parent);
    }

    [PortableMediaFact]
    public void SourceLayoutPublishesActualParagraphRangesAndBlockPolicy()
    {
        var text = new TextProvider(); var flow = new FlowProvider();
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var heading = new Paragraph(new Run("heading")) { FontSize = 24, Margin = new Thickness(2, 3, 4, 5) };
        var body = new Paragraph(new Run("body")) { FontSize = 12 };
        var section = new Section(body) { Padding = new Thickness(6) };
        var document = new FlowDocument(heading) { PagePadding = new Thickness(8) };
        document.Blocks.Add(section);
        using var layout = PortableFlowDocumentLayout.Create(document, 300, 1, TextFormattingMode.Ideal);
        Assert.Equal(new[] { "heading", "body" }, text.Texts);
        Assert.Equal(4, layout.Blocks.Count);
        Assert.Same(heading, layout.Lines[0].Paragraph);
        Assert.Same(body, layout.Lines[1].Paragraph);
        Assert.Equal(heading.ElementStart.Offset, layout.Lines[0].Start);
        Assert.Equal(heading.ElementEnd.Offset, layout.Lines[0].Start + layout.Lines[0].Line.Length);
        Assert.Equal(body.ElementEnd.Offset, layout.Lines[1].Start + layout.Lines[1].Line.Length);
        Assert.Equal(8, flow.Blocks[0].InsetLeft);
        Assert.Equal(3, flow.Blocks[1].MarginTop);
        Assert.Equal(6, flow.Blocks[2].InsetLeft);
        Assert.Equal(2U, flow.Blocks[3].ParentIndex);
        Assert.Equal(1U, flow.Blocks[1].LineCount);
        Assert.Equal(0U, flow.Blocks[2].LineCount);
        Assert.Equal(layout.Lines[1].Advance, flow.Lines[1].Height);
        Assert.Equal(110, layout.Positions[1].Y);
        Assert.Equal(new Size(300, 250), layout.Size);
    }

    [PortableMediaFact]
    public void ListMarkerRetainsSourceNumberingAndSeparateGeneratedContent()
    {
        var text = new TextProvider(); var flow = new FlowProvider();
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var paragraph = new Paragraph(new Run("item"));
        var item = new ListItem(paragraph);
        var list = new List(item) { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 3,
            MarkerOffset = 4, Padding = new Thickness(20, 0, 0, 0) };
        var document = new FlowDocument(list);
        using var layout = PortableFlowDocumentLayout.Create(document, 300, 1, TextFormattingMode.Ideal);
        Assert.Equal(new[] { "item", "3." }, text.Texts);
        var marker = Assert.Single(layout.Markers);
        Assert.Same(item, marker.Item);
        Assert.Equal(0, marker.TargetLine);
        Assert.Equal(-4, marker.Offset);
        Assert.Equal(20, flow.Blocks[1].InsetLeft);
        Assert.Equal(1U, flow.Blocks[2].ParentIndex);
        Assert.Equal(2U, flow.Blocks[3].ParentIndex);
        Assert.Single(layout.Lines);
        Assert.Same(paragraph, layout.Lines[0].Paragraph);
    }

    [PortableMediaFact]
    public void FormatterRetainsGenerationUntilSourceChangeAndInvalidatesOnFailure()
    {
        var text = new TextProvider(); var flow = new FlowProvider();
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var run = new Run("before"); var document = new FlowDocument(new Paragraph(run));
        var formatter = document.PortableBottomlessFormatter;
        int invalidations = 0; formatter.ContentInvalidated += (_, _) => ++invalidations;
        formatter.Format(new(300, 200), 1, TextFormattingMode.Ideal); formatter.OnArranged();
        var first = formatter.Layout;
        Assert.True(((IFlowDocumentFormatter)formatter).IsLayoutDataValid);
        Assert.False(document.StructuralCache.HasPtsContext());
        formatter.Format(new(300, 100), 1, TextFormattingMode.Ideal);
        Assert.Same(first, formatter.Layout);
        run.Text = "after";
        Assert.True(invalidations > 0);
        Assert.False(((IFlowDocumentFormatter)formatter).IsLayoutDataValid);
        formatter.Format(new(300, 100), 1, TextFormattingMode.Ideal);
        Assert.NotSame(first, formatter.Layout);
        Assert.Empty(first.Lines);
        document.FontSize = 32;
        flow.Fail = true;
        Assert.Throws<InvalidOperationException>(() => formatter.Format(new(300, 100), 1, TextFormattingMode.Ideal));
        Assert.False(document.StructuralCache.IsFormattingInProgress);
        Assert.False(((IFlowDocumentFormatter)formatter).IsLayoutDataValid);
        flow.Fail = false;
        formatter.Format(new(300, 100), 1, TextFormattingMode.Ideal); formatter.OnArranged();
        Assert.True(((IFlowDocumentFormatter)formatter).IsLayoutDataValid);
        ((IFlowDocumentFormatter)formatter).Suspend();
        Assert.Null(formatter.Layout);
    }

    [PortableMediaFact]
    public void RejectedStructuresAndCaughtReentrantMutationsDoNotBecomeEmptyLayout()
    {
        var text = new TextProvider(); var flow = new FlowProvider();
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var document = new FlowDocument(new BlockUIContainer(new Button()));
        Assert.Throws<PlatformNotSupportedException>(() => document.PortableBottomlessFormatter.Format(new(300, 200), 1, TextFormattingMode.Ideal));
        document.Blocks.Clear(); document.Blocks.Add(new Paragraph(new Run("stable")));
        flow.DuringWidth = () => Assert.Throws<InvalidOperationException>(() => document.FontSize = 30);
        Assert.Throws<InvalidOperationException>(() => document.PortableBottomlessFormatter.Format(new(300, 200), 1, TextFormattingMode.Ideal));
        Assert.False(document.StructuralCache.IsFormattingInProgress);
        Assert.Null(document.PortableBottomlessFormatter.Layout);
    }

    // Deterministic transport fixtures, not native layout/shaping or rendering oracles.
    private sealed class FlowProvider : IPortableDocumentFlow
    {
        internal PortableDocumentBlock[] Blocks { get; private set; } = [];
        internal PortableDocumentLine[] Lines { get; private set; } = [];
        internal bool Fail { get; set; }
        internal Action? DuringWidth { get; set; }
        public void ResolveWidths(ReadOnlySpan<PortableDocumentBlock> blocks, double width, Span<PortableDocumentBox> boxes)
        {
            DuringWidth?.Invoke();
            if (Fail) throw new InvalidOperationException("fixture failure");
            boxes.Fill(new() { Width = 200 });
        }
        public PortableDocumentExtent Arrange(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
            ReadOnlySpan<PortableDocumentLine> lines, Span<PortableDocumentBox> boxes, Span<PortableDocumentLinePosition> positions)
        {
            Blocks = blocks.ToArray(); Lines = lines.ToArray();
            for (int i = 0; i < positions.Length; ++i) positions[i] = new() { X = 5, Y = 10 + i * 100 };
            return new(width, 250);
        }
    }

    private sealed class TextProvider : IPortableTextFormatting
    {
        internal System.Collections.Generic.List<string> Texts { get; } = new();
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        {
            Texts.Add(request.Text.ToString());
            return new ParagraphOutput(request);
        }
    }
    private sealed class ParagraphOutput : IPortableTextParagraph
    {
        internal ParagraphOutput(in PortableTextParagraphRequest request)
        {
            var glyphs = new PortableTextGlyph[request.Text.Length];
            for (int i = 0; i < glyphs.Length; ++i) glyphs[i] = new(0, i, i + 1, i * 5, 0, 5, 0);
            Glyphs = glyphs;
            Lines = new PortableTextLineInfo[] { new(0, glyphs.Length, 0, glyphs.Length, glyphs.Length * 5, 0, request.LineHeight) };
        }
        public ReadOnlyMemory<PortableTextGlyph> Glyphs { get; }
        public ReadOnlyMemory<PortableTextLineInfo> Lines { get; }
        public PortableTextHit HitTest(int lineIndex, float distance) => new(Math.Clamp((int)(distance / 5), 0, Glyphs.Length), false);
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => hit.Position * 5;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => Math.Clamp(position + (previous ? -1 : 1), 0, Glyphs.Length);
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        {
            if (end <= start) return 0;
            rectangles[0] = new(start * 5, 0, (end - start) * 5, Lines.Span[0].Height);
            return 1;
        }
    }
}
