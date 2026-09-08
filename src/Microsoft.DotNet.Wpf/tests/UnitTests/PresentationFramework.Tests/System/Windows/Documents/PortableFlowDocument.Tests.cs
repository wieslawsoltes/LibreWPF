// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Documents;
using ProGPU.Wpf.Interop;
using WpfDrawing = System.Windows.Media.Drawing;

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
    public void ViewerSharesLiveLayoutForDrawingContentHitSelectionAndScrolling()
    {
        var text = new TextProvider(); var flow = new FlowProvider();
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var first = new Run("first"); var second = new Run("second");
        var link = new Hyperlink(second);
        var document = new FlowDocument(new Paragraph(first));
        document.Blocks.Add(new Paragraph(link));
        var viewer = new FlowDocumentView { Document = document };
        var scroll = (IScrollInfo)viewer; scroll.ScrollOwner = new ScrollViewer();
        var textView = Assert.IsAssignableFrom<ITextView>(((IServiceProvider)viewer).GetService(typeof(ITextView)));
        document.TextContainer.TextView = textView;
        LayoutViewer(viewer);
        Assert.True(textView.IsValid);
        Assert.False(document.StructuralCache.HasPtsContext());
        var visual = Assert.IsType<PortableFlowDocumentVisual>(VisualTreeHelper.GetChild(viewer, 0));
        var content = (IContentHost)visual;
        Rect firstCaret = textView.GetRectangleFromTextPosition(first.ContentStart);
        Rect secondCaret = textView.GetRectangleFromTextPosition(second.ContentStart);
        Assert.Equal(10, firstCaret.Y); Assert.Equal(110, secondCaret.Y);
        Assert.Equal(250, scroll.ExtentHeight); Assert.Equal(60, scroll.ViewportHeight);
        Assert.Same(first, content.InputHitTest(new(6, firstCaret.Y + 1)));
        Assert.Contains(Drawings(visual.Drawing), drawing => drawing is GlyphRunDrawing);
        Assert.True(textView.IsAtCaretUnitBoundary(second.ContentEnd.GetFrozenPointer(LogicalDirection.Backward)));
        var generation = viewer.PortableLayout;
        scroll.SetVerticalOffset(100);
        Assert.False(textView.IsValid);
        viewer.Arrange(new Rect(0, 0, 300, 60));
        Assert.True(textView.IsValid);
        Assert.Same(generation, viewer.PortableLayout);
        Assert.Equal(new Vector(0, -100), VisualTreeHelper.GetOffset(visual));
        Assert.Equal(new Rect(0, 100, 300, 60), Assert.IsType<RectangleGeometry>(visual.Clip).Rect);
        Assert.Equal(10, textView.GetRectangleFromTextPosition(second.ContentStart).Y);
        Assert.Same(second, ((TextPointer)textView.GetTextPositionFromPoint(new(6, 11), false)).Parent);
        Assert.Same(second, content.InputHitTest(new(6, 111))); // Content-host space is unscrolled.
        var rectangles = content.GetRectangles(link);
        Assert.NotEmpty(rectangles); Assert.Equal(110, rectangles[0].Y);
        Geometry selection = textView.GetTightBoundingGeometryFromTextPositions(second.ContentStart, second.ContentEnd);
        Assert.True(((IPortableGeometryPathSource)selection).TryGetPortableGeometryPath(out var selectionPath));
        Assert.Equal(10, Assert.Single(selectionPath.Figures).StartPoint.Y);
        Assert.Single(content.GetRectangles(document));
        var hosted = new System.Collections.Generic.List<IInputElement>();
        using (var elements = content.HostedElements) while (elements.MoveNext()) hosted.Add(elements.Current);
        Assert.Contains(link, hosted);
        viewer.Document = null!;
        Assert.False(textView.IsValid);
        Assert.Equal(0, VisualTreeHelper.GetChildrenCount(viewer));
        Assert.Null(content.InputHitTest(new(6, 111)));
        Assert.Empty(content.GetRectangles(link));
        Assert.Empty(generation.Lines);
    }

    [PortableMediaFact]
    public void ViewerUpdatesEditsAndSupportsLinePageAndBringIntoViewNavigation()
    {
        var text = new TextProvider(); var flow = new FlowProvider();
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var first = new Run("one"); var second = new Run("two");
        var document = new FlowDocument(new Paragraph(first)); document.Blocks.Add(new Paragraph(second));
        var viewer = new FlowDocumentView { Document = document };
        var scroll = (IScrollInfo)viewer; scroll.ScrollOwner = new ScrollViewer();
        var textView = Assert.IsAssignableFrom<ITextView>(((IServiceProvider)viewer).GetService(typeof(ITextView)));
        document.TextContainer.TextView = textView;
        LayoutViewer(viewer);
        var generation = viewer.PortableLayout;
        var next = textView.GetPositionAtNextLine(first.ContentStart, 5, 1, out double x, out int moved);
        Assert.Equal(1, moved); Assert.Equal(5, x);
        Assert.Same(second.Parent, ((TextPointer)next).Paragraph);
        next = textView.GetPositionAtNextLine(first.ContentStart, 5, 0, out _, out moved);
        Assert.Equal(0, moved); Assert.Equal(first.ContentStart.Offset, next.Offset);
        next = textView.GetPositionAtNextPage(first.ContentStart, new Point(5, 10), 2, out _, out int pages);
        Assert.Equal(2, pages); Assert.Same(second.Parent, ((TextPointer)next).Paragraph);
        bool completed = false;
        textView.BringPositionIntoViewCompleted += (_, args) => completed = !args.Cancelled && args.Error is null;
        textView.BringPositionIntoViewAsync(second.ContentStart, null!);
        Assert.True(completed);
        Assert.True(scroll.VerticalOffset > 0);
        viewer.Arrange(new Rect(0, 0, 300, 60));
        Assert.Same(generation, viewer.PortableLayout);
        first.Text = "changed";
        Assert.False(textView.IsValid);
        LayoutViewer(viewer);
        Assert.NotSame(generation, viewer.PortableLayout);
        Assert.Empty(generation.Lines);
        Assert.Contains("changed", text.Texts);
        viewer.SuspendLayout();
        Assert.False(textView.IsValid);
        Assert.Equal(0.5, VisualTreeHelper.GetOpacity(Assert.IsAssignableFrom<Visual>(VisualTreeHelper.GetChild(viewer, 0))));
        viewer.ResumeLayout(); LayoutViewer(viewer);
        Assert.True(textView.IsValid);
        viewer.Document = null!;
    }

    [PortableMediaFact]
    public void PaginatorPublishesRealPagesWithSourceOwnedColumnInteractionAndLifetime()
    {
        var text = new TextProvider(); var flow = new FlowProvider {
            PaginatedPositions = [new() { Page = 0, Column = 0, Y = 10 },
                new() { Page = 0, Column = 1, Y = 4 }, new() { Page = 1, Column = 0, Y = 5 }] };
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var first = new Run("one"); var second = new Run("two"); var third = new Run("three");
        var link = new Hyperlink(second);
        var document = new FlowDocument(new Paragraph(first)) { PagePadding = new Thickness(8),
            ColumnWidth = 120, ColumnGap = 12, IsColumnWidthFlexible = false };
        document.Blocks.Add(new Paragraph(link)); document.Blocks.Add(new Paragraph(third));
        var paginator = Assert.IsType<PortableFlowDocumentPaginator>(((IDocumentPaginatorSource)document).DocumentPaginator);
        paginator.PageSize = new Size(300, 200);
        var page = Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(0));
        Assert.True(paginator.IsPageCountValid); Assert.Equal(2, paginator.PageCount);
        Assert.Equal(2U, flow.PaginationColumns); Assert.Equal(184, flow.PaginationHeight);
        Assert.Equal(120, paginator.ColumnWidth); Assert.Equal(new Size(300, 200), page.Size);
        Assert.False(document.StructuralCache.HasPtsContext());
        Assert.Same(document, ((IServiceProvider)paginator).GetService(typeof(ITextContainer)) is TextContainer container ? container.Parent : null);
        var view = Assert.IsAssignableFrom<ITextView>(((IServiceProvider)page).GetService(typeof(ITextView)));
        Assert.True(view.IsValid); Assert.True(view.Contains(first.ContentStart)); Assert.True(view.Contains(second.ContentStart));
        Assert.False(view.Contains(third.ContentStart));
        Rect firstCaret = view.GetRectangleFromTextPosition(first.ContentStart);
        Rect secondCaret = view.GetRectangleFromTextPosition(second.ContentStart);
        Assert.Equal(13, firstCaret.X); Assert.Equal(18, firstCaret.Y);
        Assert.Equal(145, secondCaret.X); Assert.Equal(12, secondCaret.Y);
        var hit = Assert.IsType<TextPointer>(view.GetTextPositionFromPoint(new(146, 13), false));
        Assert.Same(second, hit.Parent);
        var content = Assert.IsAssignableFrom<IContentHost>(page.Visual);
        Assert.Same(second, content.InputHitTest(new(146, 13)));
        Assert.Equal(145, Assert.Single(content.GetRectangles(link)).X);
        var next = view.GetPositionAtNextLine(first.ContentStart, firstCaret.X, 1, out double x, out int moved);
        Assert.Equal(1, moved); Assert.Equal(secondCaret.X, x); Assert.Same(second.Parent, ((TextPointer)next).Paragraph);
        Assert.Equal(1, paginator.GetPageNumber(third.ContentStart));
        Assert.Same(DocumentPage.Missing, paginator.GetPage(2));
        var secondPage = Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(1));
        Assert.True(secondPage.TextView.Contains(third.ContentStart));
        Assert.Equal(13, secondPage.TextView.GetRectangleFromTextPosition(third.ContentStart).Y);
        var boundary = document.TextContainer.CreatePointerAtOffset(page.Layout.Lines[2].Start, LogicalDirection.Forward);
        Assert.Equal(1, paginator.GetPageNumber(boundary));
        Assert.Equal(0, paginator.GetPageNumber(boundary.GetFrozenPointer(LogicalDirection.Backward)));
        var generation = page.Layout;
        page.Dispose(); Assert.False(view.IsValid); Assert.NotEmpty(generation.Lines);
        Assert.NotSame(page, paginator.GetPage(0));
        first.Text = "edited";
        Assert.False(paginator.IsPageCountValid); Assert.False(secondPage.IsValid); Assert.Empty(generation.Lines);
        Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(0));
        Assert.Contains("edited", text.Texts);
        _ = document.PortableBottomlessFormatter;
        Assert.False(paginator.IsPageCountValid);
        Assert.Throws<InvalidOperationException>(() => paginator.GetPage(0));
    }

    [PortableMediaFact]
    public void PaginatorPreservesBreakPoliciesAndRejectsInvalidProviderPages()
    {
        var text = new TextProvider(); var flow = new FlowProvider {
            PaginatedPositions = [new() { Page = 0, Column = 0, Y = 10 }, new() { Page = 1, Column = 0, Y = 0 }] };
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(text);
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var first = new Paragraph(new Run("one")) { KeepWithNext = true };
        var second = new Paragraph(new Run("two"));
        var section = new Section(second) { BreakPageBefore = true };
        var document = new FlowDocument(first); document.Blocks.Add(section);
        var paginator = Assert.IsType<PortableFlowDocumentPaginator>(((IDocumentPaginatorSource)document).DocumentPaginator);
        paginator.ComputePageCount();
        Assert.Equal(0U, flow.FragmentLines[1].AllowBreakBefore);
        Assert.Equal(1U, flow.FragmentLines[1].ForcePageBefore);
        var old = Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(0));
        second.MinOrphanLines = 2;
        Assert.Throws<PlatformNotSupportedException>(() => paginator.GetPage(0));
        Assert.False(old.IsValid); Assert.False(paginator.IsPageCountValid);
        second.MinOrphanLines = 0;
        flow.PaginatedPositions[1] = new() { Page = 1, Column = 1024, Y = 0 };
        Assert.Throws<InvalidOperationException>(() => paginator.GetPage(0));
        Assert.False(paginator.IsPageCountValid); Assert.Equal(0, paginator.PageCount);
        Assert.False(document.StructuralCache.HasPtsContext());
        ((IFlowDocumentFormatter)paginator).Suspend();
    }

    [PortableMediaFact]
    public void ContinuationPageHostsOriginalOpenContentAncestors()
    {
        var flow = new FlowProvider { PaginatedPositions = [new() { Y = 10 }, new() { Page = 1 }] };
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(new TextProvider());
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var second = new Run("continued");
        var link = new Hyperlink(new Run("first"));
        link.Inlines.Add(new LineBreak()); link.Inlines.Add(second);
        var paragraph = new Paragraph(link);
        var document = new FlowDocument(paragraph);
        var paginator = Assert.IsType<PortableFlowDocumentPaginator>(((IDocumentPaginatorSource)document).DocumentPaginator);
        var page = Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(1));
        var elements = new System.Collections.Generic.List<IInputElement>();
        using (var enumerator = ((IContentHost)page.Visual).HostedElements)
            while (enumerator.MoveNext()) elements.Add(enumerator.Current);
        Assert.Contains(paragraph, elements); Assert.Contains(link, elements); Assert.Contains(second, elements);
        Assert.Equal(elements.IndexOf(link), elements.LastIndexOf(link));
        Assert.True(page.TextView.Contains(second.ContentStart));
        ((IFlowDocumentFormatter)paginator).Suspend();
    }

    [PortableMediaFact]
    public void PaginatorReservesBlockInsetsAndRejectsUnrepresentedDecorationFragments()
    {
        var flow = new FlowProvider { PaginatedPositions = [new() { Y = 20 }, new() { Y = 120 }] };
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(new TextProvider());
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var section = new Section(new Paragraph(new Run("first"))) {
            Padding = new Thickness(0, 20, 0, 7), Background = Brushes.Gold };
        section.Blocks.Add(new Paragraph(new Run("second")));
        var document = new FlowDocument(section) { PagePadding = new Thickness(), ColumnWidth = 300 };
        var paginator = Assert.IsType<PortableFlowDocumentPaginator>(((IDocumentPaginatorSource)document).DocumentPaginator);
        paginator.PageSize = new Size(300, 400);
        var page = Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(0));
        Assert.Equal(20, flow.FragmentLines[0].LeadingSpace);
        Assert.Equal(flow.Lines[1].Height + 7, flow.FragmentLines[1].Height);
        Assert.Equal(120, page.Position(1).Y);

        flow.PaginatedPositions = [new() { Y = 20 }, new() { Page = 1, Y = 0 }];
        paginator.PageSize = new Size(300, 401);
        Assert.Throws<PlatformNotSupportedException>(() => paginator.GetPage(0));
        Assert.False(page.IsValid); Assert.False(paginator.IsPageCountValid);
        section.Background = null;
        Assert.IsType<PortableFlowDocumentPage>(paginator.GetPage(1));

        // Both painting and interaction require one translation per fragment.
        flow.PaginatedPositions = [new() { Y = 20 }, new() { Y = 121 }];
        paginator.PageSize = new Size(300, 402);
        Assert.Throws<InvalidOperationException>(() => paginator.GetPage(0));
        flow.PaginatedPositions = [new() { Y = 20 }, new() { Page = 1, Y = 403 - flow.Lines[1].Height }];
        paginator.PageSize = new Size(300, 403);
        Assert.Throws<InvalidOperationException>(() => paginator.GetPage(0));
        Assert.False(document.StructuralCache.HasPtsContext());
        ((IFlowDocumentFormatter)paginator).Suspend();
    }

    [PortableMediaFact]
    public void DocumentPageViewConsumesPortablePageTextViewWithoutPts()
    {
        var flow = new FlowProvider { PaginatedPositions = [new() { Y = 10 }] };
        using var textRegistration = PortableWpfServiceRegistry.RegisterTextFormatting(new TextProvider());
        using var flowRegistration = PortableWpfServiceRegistry.RegisterDocumentFlow(flow);
        var run = new Run("page wrapper");
        var document = new FlowDocument(new Paragraph(run)) { PagePadding = new Thickness(8) };
        var paginator = Assert.IsType<PortableFlowDocumentPaginator>(((IDocumentPaginatorSource)document).DocumentPaginator);
        paginator.PageSize = new Size(300, 200);
        Assert.True(paginator.IsBackgroundPaginationEnabled);
        using var host = new DocumentPageView { DocumentPaginator = paginator, Stretch = Stretch.None };
        var view = Assert.IsAssignableFrom<ITextView>(((IServiceProvider)host).GetService(typeof(ITextView)));
        host.Measure(new Size(300, 200)); host.Arrange(new Rect(0, 0, 300, 200));
        Assert.True(view.IsValid);
        Assert.IsType<PortableFlowDocumentPage>(host.DocumentPage);
        Assert.False(document.StructuralCache.HasPtsContext());
        Assert.NotEmpty(view.TextSegments);
        Assert.True(view.Contains(run.ContentStart));
        Rect caret = view.GetRectangleFromTextPosition(run.ContentStart);
        Assert.Equal(13, caret.X); Assert.Equal(18, caret.Y);
        Assert.Same(run, Assert.IsType<TextPointer>(view.GetTextPositionFromPoint(new(14, 19), false)).Parent);
        host.DocumentPaginator = null!;
        Assert.False(view.IsValid);
        ((IFlowDocumentFormatter)paginator).Suspend();
    }

    private static void LayoutViewer(FlowDocumentView viewer)
    {
        viewer.Measure(new Size(300, 60));
        viewer.Arrange(new Rect(0, 0, 300, 60));
    }

    private static IEnumerable<WpfDrawing> Drawings(WpfDrawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (WpfDrawing child in group.Children)
                foreach (WpfDrawing descendant in Drawings(child)) yield return descendant;
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
        internal PortableDocumentFragmentPosition[] PaginatedPositions { get; set; } = [];
        internal PortableDocumentFragmentLine[] FragmentLines { get; private set; } = [];
        internal uint PaginationColumns { get; private set; }
        internal double PaginationHeight { get; private set; }
        public PortableDocumentPagination Paginate(ReadOnlySpan<PortableDocumentFragmentLine> lines,
            double height, uint columns, Span<PortableDocumentFragmentPosition> positions)
        {
            // Prescribed geometry tests source consumption, not native fitting.
            FragmentLines = lines.ToArray(); PaginationColumns = columns; PaginationHeight = height;
            if (PaginatedPositions.Length != lines.Length) throw new InvalidOperationException("Fixture line count mismatch.");
            PaginatedPositions.CopyTo(positions);
            uint fragments = 0;
            for (int i = 0; i < positions.Length; ++i)
                if (i == 0 || positions[i].Page != positions[i - 1].Page || positions[i].Column != positions[i - 1].Column) ++fragments;
            return new(fragments, lines.IsEmpty ? 0 : PaginatedPositions[^1].Page + 1);
        }
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
            // Prescribe content envelopes around the fixture's fixed line grid;
            // this is not a second implementation of native block placement.
            for (int i = 0; i < blocks.Length; ++i)
            {
                int first = (int)blocks[i].LineStart;
                int end = blocks[i].SubtreeEnd < blocks.Length ? (int)blocks[(int)blocks[i].SubtreeEnd].LineStart : lines.Length;
                if (first < end)
                {
                    boxes[i].Y = positions[first].Y;
                    boxes[i].Height = positions[end - 1].Y + lines[end - 1].Height - boxes[i].Y;
                }
            }
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
