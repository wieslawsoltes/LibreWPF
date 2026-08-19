// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Windows.Documents;
using System.Windows.Media;

namespace System.Windows.Controls;

public sealed class RichTextBoxTests
{
    [Fact]
    public void CreateRenderScope_UsesPortableTextViewWhenPtsIsUnavailable()
    {
        RichTextBox richTextBox = new();

        FrameworkElement renderScope = GetRenderScope(richTextBox);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("MS.Internal.Documents.FlowDocumentView", renderScope.GetType().FullName);
        }
        else
        {
            Assert.Equal("System.Windows.Controls.TextBoxView", renderScope.GetType().FullName);
            Type textViewType = typeof(RichTextBox).Assembly.GetType("System.Windows.Documents.ITextView", throwOnError: true)!;
            Assert.Same(
                renderScope,
                ((IServiceProvider)renderScope).GetService(textViewType));
        }
    }

    [Fact]
    public void PortableRenderScope_FormatsRichRunsAndParagraphs()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

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
        MethodInfo createRenderScope = typeof(RichTextBox).GetMethod(
            "CreateRenderScope",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (FrameworkElement)createRenderScope.Invoke(richTextBox, parameters: null)!;
    }
}
