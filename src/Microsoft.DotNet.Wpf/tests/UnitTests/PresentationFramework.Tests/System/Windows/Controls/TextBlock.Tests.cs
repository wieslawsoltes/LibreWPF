// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Documents;
using ProGPU.Wpf.Interop;

namespace System.Windows.Controls;

public sealed class TextBlockTests
{
    [Fact]
    public void PortableRtlFinalInsertionUsesPhysicalParagraphEdge()
    {
        using var registration = PortableWpfServiceRegistry.RegisterTextFormatting(new TextProvider());
        TextBlock textBlock = new()
        {
            Text = "English 1234 — שלום עולם — العربية 5678 — mixed direction text",
            FlowDirection = FlowDirection.RightToLeft,
            TextWrapping = TextWrapping.Wrap,
            Width = 300,
        };

        textBlock.Measure(new Size(300, double.PositiveInfinity));
        textBlock.Arrange(new Rect(textBlock.DesiredSize));

        TextPointer finalPosition = textBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)!;
        Rect finalCaret = finalPosition.GetCharacterRect(LogicalDirection.Forward);
        TextPointer previousPosition = finalPosition.GetNextInsertionPosition(LogicalDirection.Backward)!;
        Rect previousCaret = previousPosition.GetCharacterRect(LogicalDirection.Forward);

        Assert.Equal(0, finalCaret.X);
        Assert.Equal(5, previousCaret.X);
    }

    [Fact]
    public void GetRectangles_MultilineContent_UsesPrecedingLineHeights()
    {
        TextBlock textBlock = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Width = 200,
        };
        Hyperlink firstLink = new(new Run("First")) { FontSize = 48 };
        Hyperlink secondLink = new(new Run("Second")) { FontSize = 12 };
        Hyperlink thirdLink = new(new Run("Third")) { FontSize = 12 };
        textBlock.Inlines.Add(firstLink);
        textBlock.Inlines.Add(new LineBreak());
        textBlock.Inlines.Add(secondLink);
        textBlock.Inlines.Add(new LineBreak());
        textBlock.Inlines.Add(thirdLink);

        textBlock.Measure(new Size(200, double.PositiveInfinity));
        textBlock.Arrange(new Rect(textBlock.DesiredSize));

        IContentHost contentHost = (IContentHost)textBlock;
        Rect first = Assert.Single(contentHost.GetRectangles(firstLink));
        Rect second = Assert.Single(contentHost.GetRectangles(secondLink));
        Rect third = Assert.Single(contentHost.GetRectangles(thirdLink));

        Assert.True(second.Top >= first.Bottom);
        Assert.True(third.Top >= second.Bottom);
    }

    private sealed class TextProvider : IPortableTextFormatting
    {
        public IPortableTextParagraph Format(in PortableTextParagraphRequest request) => new Paragraph(request);
    }

    private sealed class Paragraph : IPortableTextParagraph
    {
        internal Paragraph(in PortableTextParagraphRequest request)
        {
            var glyphs = new PortableTextGlyph[request.Text.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                glyphs[i] = new(0, i, i + 1, i * 5, 0, 5, 0);
            }

            Glyphs = glyphs;
            Lines = new PortableTextLineInfo[]
            {
                new(0, glyphs.Length, 0, glyphs.Length, glyphs.Length * 5, 0, request.LineHeight),
            };
        }

        public ReadOnlyMemory<PortableTextGlyph> Glyphs { get; }
        public ReadOnlyMemory<PortableTextLineInfo> Lines { get; }
        public PortableTextHit HitTest(int lineIndex, float distance) => new(Math.Clamp((int)(distance / 5), 0, Glyphs.Length), false);
        public float GetCaretDistance(int lineIndex, PortableTextHit hit) => hit.Position * 5;
        public int GetNextLogicalCaret(int lineIndex, int position, bool previous) => Math.Clamp(position + (previous ? -1 : 1), 0, Glyphs.Length);
        public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
        {
            if (end <= start)
            {
                return 0;
            }

            rectangles[0] = new(start * 5, 0, (end - start) * 5, Lines.Span[0].Height);
            return 1;
        }
    }
}
