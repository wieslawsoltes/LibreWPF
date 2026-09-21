// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Controls.Primitives;
using MS.Internal.Text;
using ProGPU.Wpf.Interop;

namespace System.Windows.Controls;

[Collection("Sequential")]
public sealed class RichTextBoxTests
{
    private sealed class PortableMediaFactAttribute : FactAttribute
    {
        public PortableMediaFactAttribute([CallerFilePath] string? sourceFilePath = null,
            [CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before WPF construction; do not switch a frozen Windows-MIL process.";
        }
    }

    [Fact]
    public void CreateRenderScope_UsesFrozenMediaOwnershipRatherThanOperatingSystem()
    {
        RichTextBox richTextBox = new();

        FrameworkElement renderScope = GetRenderScope(richTextBox);

        if (PortableWpfRuntime.GetMediaBackendAndFreeze() == PortableWpfMediaBackend.WindowsMil)
        {
            Assert.Equal("MS.Internal.Documents.FlowDocumentView", renderScope.GetType().FullName);
        }
        else
        {
            Assert.Equal("System.Windows.Controls.TextBoxView", renderScope.GetType().FullName);
            Assert.Same(
                renderScope,
                ((IServiceProvider)renderScope).GetService(typeof(ITextView)));
        }
    }

    [PortableMediaFact]
    public void PortableRenderScope_FormatsRichRunsAndParagraphs()
    {
        FlowDocument document = new();
        document.Blocks.Add(new Paragraph(new Bold(new Run("First"))));
        document.Blocks.Add(new Paragraph(new Italic(new Run("Second"))));
        RichTextBox richTextBox = new(document);
        FrameworkElement renderScope = GetRenderScope(richTextBox);

        renderScope.Measure(new Size(300, double.PositiveInfinity));
        renderScope.Arrange(new Rect(0, 0, 300, renderScope.DesiredSize.Height));

        Assert.True(renderScope.DesiredSize.Width > 0);
        Assert.True(renderScope.DesiredSize.Height > 0);
        Assert.Equal(2, VisualTreeHelper.GetChildrenCount(renderScope));
    }

    private static FrameworkElement GetRenderScope(RichTextBox richTextBox)
    {
        return richTextBox.CreateRenderScope();
    }

    [PortableMediaFact]
    public void PortableEditorSendsActualStyledContentToRegisteredProvider()
    {
        var paragraph = new Paragraph(new Run("Editable plain text"));
        paragraph.Inlines.Add(new Bold(new Run("bold text")));
        var editor = new RichTextBox(new FlowDocument(paragraph));
        var provider = new CaptureProvider();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(provider);
        var view = Assert.IsType<TextBoxView>(GetRenderScope(editor));
        Assert.Same(provider.Failure, Assert.Throws<InvalidOperationException>(() =>
            view.Measure(new Size(300, double.PositiveInfinity))));
        Assert.Equal("Editable plain textbold text", provider.Request.Text.ToString());
        Assert.True(provider.Request.Styles.Length >= 2);
        Assert.Equal(0, provider.Request.Styles.Span[0].Start);
        var last = provider.Request.Styles.Span[^1];
        Assert.Equal(provider.Request.Text.Length, last.Start + last.Length);
    }

    [PortableMediaFact]
    public void PortableEditorKeepsSourceEdgesAndStyledRunProperties()
    {
        var first = new Run("abc") { Foreground = Brushes.Red };
        var second = new Run("def") { Foreground = Brushes.Blue };
        var paragraph = new Paragraph(first);
        paragraph.Inlines.Add(new Bold(second));
        var editor = new RichTextBox(new FlowDocument(paragraph));
        using var line = new TextBoxLine(Assert.IsType<TextBoxView>(GetRenderScope(editor)));
        int offset = 0;
        var runs = new List<TextCharacters>();
        for (;;)
        {
            TextRun run = line.GetTextRun(offset);
            if (run is TextEndOfParagraph) break;
            if (run is TextCharacters characters) runs.Add(characters);
            else Assert.IsType<TextHidden>(run);
            offset += run.Length;
        }
        Assert.Equal(((ITextBoxViewHost)editor).TextContainer.SymbolCount, offset);
        Assert.Equal(2, runs.Count);
        Assert.Same(Brushes.Red, runs[0].Properties.ForegroundBrush);
        Assert.Same(Brushes.Blue, runs[1].Properties.ForegroundBrush);
        Assert.Equal(FontWeights.Bold, runs[1].Properties.Typeface.Weight);
    }

    [PortableMediaFact]
    public void PortableEditorRunAllocationBoundaryDoesNotSplitSurrogatePair()
    {
        var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run(new string('a', 4095) + "\U0001F600"))));
        using var line = new TextBoxLine(Assert.IsType<TextBoxView>(GetRenderScope(editor)));
        int offset = 0;
        while (line.GetTextRun(offset) is TextHidden hidden) offset += hidden.Length;
        Assert.Equal(4095, Assert.IsType<TextCharacters>(line.GetTextRun(offset)).Length);
        Assert.Equal(2, Assert.IsType<TextCharacters>(line.GetTextRun(offset + 4095)).Length);
    }

    [PortableMediaFact]
    public void PortableEditorRejectsObjectsDecorationsAndDirectionalScopes()
    {
        Inline[] unsupported =
        [
            new InlineUIContainer(new Button()),
            new Underline(new Run("decorated")),
            new Span(new Run("directional")) { FlowDirection = FlowDirection.RightToLeft }
        ];
        foreach (Inline inline in unsupported)
        {
            var editor = new RichTextBox(new FlowDocument(new Paragraph(inline)));
            using var line = new TextBoxLine(Assert.IsType<TextBoxView>(GetRenderScope(editor)));
            Assert.Throws<PlatformNotSupportedException>(() =>
            {
                int offset = 0;
                while (line.GetTextRun(offset) is TextRun run && run is not TextEndOfParagraph)
                    offset += run.Length;
            });
        }
    }

    [PortableMediaFact]
    public void ProviderBackedEditorDoesNotSuppressJustification()
    {
        var editor = new TextBox { Text = "first second", TextAlignment = TextAlignment.Justify };
        var properties = new LineProperties(editor, editor, new TextProperties(editor, false), null!);
        using var line = new TextBoxLine(new TextBoxView(editor));
        using var formatter = TextFormatter.Create();
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new CaptureProvider());
        Assert.Throws<PlatformNotSupportedException>(() =>
            line.Format(0, 100, 100, properties, new TextRunCache(), formatter));
        Assert.False(properties.IgnoreTextAlignment);
        Assert.Equal(TextAlignment.Justify, properties.TextAlignment);
    }

    private sealed class CaptureProvider : IPortableTextFormatting
    {
        internal InvalidOperationException Failure { get; } = new("Stop after typed paragraph submission.");
        internal PortableTextParagraphRequest Request { get; private set; }
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request)
        {
            Request = request;
            throw Failure;
        }
    }

    [PortableMediaFact]
    public void MixedSizeEditorLinesSharePlacementCaretSelectionAndEditMetrics()
    {
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new LayoutProvider());
        var first = new Run("small") { FontSize = 12 };
        var second = new Run("large") { FontSize = 40 };
        var third = new Run("last") { FontSize = 18 };
        var document = new FlowDocument(new Paragraph(first));
        document.Blocks.Add(new Paragraph(second));
        document.Blocks.Add(new Paragraph(third));
        var editor = new RichTextBox(document);
        var view = Assert.IsType<TextBoxView>(GetRenderScope(editor));
        Layout(view);

        var textView = (ITextView)view;
        Rect firstCaret = textView.GetRectangleFromTextPosition(first.ContentStart);
        Rect secondCaret = textView.GetRectangleFromTextPosition(second.ContentStart);
        Rect thirdCaret = textView.GetRectangleFromTextPosition(third.ContentStart);
        Assert.True(secondCaret.Height > firstCaret.Height * 2);
        Assert.Equal(firstCaret.Bottom, secondCaret.Top, 5);
        Assert.Equal(secondCaret.Bottom, thirdCaret.Top, 5);
        Assert.Equal(thirdCaret.Bottom, view.DesiredSize.Height, 5);
        Assert.Equal(secondCaret.Top, VisualTreeHelper.GetOffset((Visual)VisualTreeHelper.GetChild(view, 1)).Y, 5);
        Assert.Equal(thirdCaret.Top, VisualTreeHelper.GetOffset((Visual)VisualTreeHelper.GetChild(view, 2)).Y, 5);
        var hit = textView.GetTextPositionFromPoint(new Point(1, secondCaret.Top + secondCaret.Height / 2), true);
        Assert.Same(second.Parent, ((TextPointer)hit).Paragraph);
        Geometry selection = textView.GetTightBoundingGeometryFromTextPositions(third.ContentStart, third.ContentEnd);
        Assert.Equal(thirdCaret.Top, selection.Bounds.Top, 5);
        Assert.Equal(thirdCaret.Bottom, selection.Bounds.Bottom, 5);

        second.FontSize = 24;
        Layout(view);
        Rect changedSecondCaret = textView.GetRectangleFromTextPosition(second.ContentStart);
        Rect changedThirdCaret = textView.GetRectangleFromTextPosition(third.ContentStart);
        Assert.True(changedThirdCaret.Top < thirdCaret.Top);
        Assert.Equal(changedSecondCaret.Bottom, changedThirdCaret.Top, 5);
        Assert.Equal(changedThirdCaret.Bottom, view.DesiredSize.Height, 5);
        Assert.Equal(changedThirdCaret.Top, VisualTreeHelper.GetOffset((Visual)VisualTreeHelper.GetChild(view, 2)).Y, 5);
    }

    [PortableMediaFact]
    public void InlineSelectionDrawingUsesDocumentSymbolsNotFlattenedCharacterOffsets()
    {
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new LayoutProvider());
        var first = new Run("first");
        var second = new Run("second");
        var document = new FlowDocument(new Paragraph(new Bold(first)));
        document.Blocks.Add(new Paragraph(second));
        var editor = new RichTextBox(document)
        {
            IsInactiveSelectionHighlightEnabled = true,
            SelectionBrush = Brushes.Magenta
        };
        var view = Assert.IsType<TextBoxView>(GetRenderScope(editor));
        if (!((ITextView)view).RendersOwnSelection) return; // Separate process switch owns adorner rendering.
        editor.Selection.Select(second.ContentStart, second.ContentEnd);
        Layout(view);
        var visual = Assert.IsAssignableFrom<DrawingVisual>(VisualTreeHelper.GetChild(view, 1));
        var selection = Assert.IsType<GeometryDrawing>(visual.Drawing.Children[0]);
        Assert.Equal(6 * 5, selection.Geometry.Bounds.Width, 5);
        Assert.Equal(0, selection.Geometry.Bounds.Left, 5);
    }

    [PortableMediaFact]
    public void MixedSizeEditorViewportUsesExclusiveLineBottomAndActualHeight()
    {
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new LayoutProvider());
        var first = new Run("small") { FontSize = 12 };
        var second = new Run("large") { FontSize = 40 };
        var third = new Run("last") { FontSize = 18 };
        var document = new FlowDocument(new Paragraph(first));
        document.Blocks.Add(new Paragraph(second));
        document.Blocks.Add(new Paragraph(third));
        var editor = new RichTextBox(document);
        var view = Assert.IsType<TextBoxView>(GetRenderScope(editor));
        Layout(view);
        var textView = (ITextView)view;
        Rect secondCaret = textView.GetRectangleFromTextPosition(second.ContentStart);
        var scroll = (IScrollInfo)view;
        scroll.ScrollOwner = new ScrollViewer();
        scroll.CanVerticallyScroll = true;
        view.Arrange(new Rect(0, 0, 300, secondCaret.Height));
        scroll.SetVerticalOffset(secondCaret.Top);
        view.Arrange(new Rect(0, 0, 300, secondCaret.Height));
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(view));
        Assert.Equal(0, VisualTreeHelper.GetOffset((Visual)VisualTreeHelper.GetChild(view, 0)).Y, 5);
        Assert.Equal(0, textView.GetRectangleFromTextPosition(second.ContentStart).Top, 5);
    }

    private static void Layout(TextBoxView view)
    {
        view.Measure(new Size(300, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, 300, view.DesiredSize.Height));
    }

    // Deterministic fixture output exercises source line-cache consumers only.
    // It is not a shaper or evidence of native rendering/paragraph qualification.
    private sealed class LayoutProvider : IPortableTextFormatting
    {
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request) => new LayoutParagraph(request);
    }

    private sealed class LayoutParagraph : IPortableTextParagraph
    {
        internal LayoutParagraph(in PortableTextParagraphRequest request)
        {
            var glyphs = new PortableTextGlyph[request.Text.Length];
            for (int i = 0; i < glyphs.Length; i++) glyphs[i] = new(0, i, i + 1, i * 5, 0, 5, 0);
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
