// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Documents;

namespace System.Windows.Controls;

public sealed class TextBlockTests
{
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
}
