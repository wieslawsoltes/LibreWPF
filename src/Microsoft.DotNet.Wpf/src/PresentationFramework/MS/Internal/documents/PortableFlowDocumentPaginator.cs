// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MS.Internal.PtsHost;
using MS.Internal.Text;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Documents;

// Source document/page policy and lifetime only. ProGPU owns paragraph shaping,
// block layout and page/column fitting. No PTS context, cloned document or fake root.
internal sealed class PortableFlowDocumentPaginator : DynamicDocumentPaginator,
    IServiceProvider, IFlowDocumentFormatter
{
    private readonly FlowDocument document;
    private Size _pageSize = new(816, 1056);
    private bool _dirty = true, _suspended, _background;
    private DispatcherOperation _pending;
    private PortableFlowDocumentPage[] _pages = Array.Empty<PortableFlowDocumentPage>();
    internal PortableFlowDocumentPaginator(FlowDocument document)
    {
        this.document = document;
        _background = true;
        Schedule();
    }
    internal FlowDocument Document => document;
    internal PortableFlowDocumentLayout Layout { get; private set; }
    internal PortableDocumentFragmentPosition[] Positions { get; private set; }
    internal int[] PageStarts { get; private set; }
    internal Size ActualPageSize { get; private set; }
    internal Thickness Padding { get; private set; }
    internal double ColumnWidth { get; private set; }
    internal double ColumnGap { get; private set; }
    internal int Columns { get; private set; }
    internal bool LayoutValid => !_dirty && !_suspended && Layout != null &&
        !document.StructuralCache.IsFormattingInProgress && !document.StructuralCache.IsContentChangeInProgress;
    public override IDocumentPaginatorSource Source => document;
    public override bool IsPageCountValid => LayoutValid;
    public override int PageCount => _pages.Length;
    public override Size PageSize
    {
        get => _pageSize;
        set
        {
            document.Dispatcher.VerifyAccess();
            if (double.IsNaN(value.Width)) value.Width = 816;
            if (double.IsNaN(value.Height)) value.Height = 1056;
            if (!double.IsFinite(value.Width) || value.Width <= 0 || !double.IsFinite(value.Height) || value.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_pageSize != value) { _pageSize = value; Invalidate(); }
        }
    }
    public override bool IsBackgroundPaginationEnabled
    {
        get => _background;
        set { document.Dispatcher.VerifyAccess(); _background = value; Schedule(); }
    }

    public override DocumentPage GetPage(int pageNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);
        EnsureLayout();
        if (pageNumber >= _pages.Length) return DocumentPage.Missing;
        var page = _pages[pageNumber];
        if (page == null || page.IsDisposed) _pages[pageNumber] = page = new(this, pageNumber);
        return page;
    }
    public override void ComputePageCount() => EnsureLayout();
    public override int GetPageNumber(ContentPosition contentPosition)
    {
        EnsureLayout();
        if (contentPosition is not TextPointer pointer || !ReferenceEquals(pointer.TextContainer, document.TextContainer))
            throw new ArgumentException("Position belongs to another document.", nameof(contentPosition));
        int low = 0, high = _pages.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            int offset = PageStartOffset(middle);
            if (offset < pointer.Offset || (offset == pointer.Offset && pointer.LogicalDirection == LogicalDirection.Forward)) low = middle + 1;
            else high = middle;
        }
        return Math.Max(0, low - 1);
    }
    public override ContentPosition GetPagePosition(DocumentPage page)
    {
        document.Dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(page);
        return page is PortableFlowDocumentPage source && ReferenceEquals(source.Owner, this) && source.IsValid
            ? document.TextContainer.CreatePointerAtOffset(PageStartOffset(source.Number), LogicalDirection.Forward)
            : ContentPosition.Missing;
    }
    public override ContentPosition GetObjectPosition(object value)
    {
        document.Dispatcher.VerifyAccess(); ArgumentNullException.ThrowIfNull(value);
        return document.GetObjectPosition(value);
    }
    internal int PageStartOffset(int page) => page == 0 ? document.TextContainer.Start.Offset :
        page >= _pages.Length ? document.TextContainer.End.Offset : Layout.Lines[PageStarts[page]].Start;
    object IServiceProvider.GetService(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type == typeof(ITextContainer) || type == typeof(TextContainer) ? document.TextContainer : null;
    }

    private void EnsureLayout()
    {
        document.Dispatcher.VerifyAccess();
        if (_suspended) throw new InvalidOperationException("This paginator no longer owns the document formatter.");
        if (document.StructuralCache.IsFormattingInProgress) throw new InvalidOperationException(SR.FlowDocumentFormattingReentrancy);
        if (document.StructuralCache.IsContentChangeInProgress) throw new InvalidOperationException(SR.TextContainerChangingReentrancyInvalid);
        if (LayoutValid) return;
        if (!PortableWpfServiceRegistry.TryGetDocumentFlow(out var flow))
            throw new PlatformNotSupportedException("A native document provider is required before pagination.");
        PortableFlowDocumentLayout next = null;
        PortableDocumentFragmentPosition[] placed;
        PortableFlowDocumentPage[] pages;
        int[] pageStarts;
        int pageCount;
        using (document.Dispatcher.DisableProcessing())
        using (document.StructuralCache.BeginPortableFormat())
        {
            try
            {
                ActualPageSize = FlowDocumentPaginator.ComputePageSize(document, _pageSize);
                Padding = FitPadding(document.ComputePageMargin(), ActualPageSize);
                double width = ActualPageSize.Width - Padding.Left - Padding.Right;
                double height = ActualPageSize.Height - Padding.Top - Padding.Bottom;
                if (width <= 0 || height <= 0) throw new PlatformNotSupportedException("Page padding leaves no formatting area.");
                var columns = new ColumnPropertiesGroup(document);
                double lineHeight = DynamicPropertyReader.GetLineHeightValue(document);
                Columns = PtsHelper.CalculateColumnCount(columns, lineHeight, width, document.FontSize, document.FontFamily, true);
                PtsHelper.GetColumnMetrics(columns, width, document.FontSize, document.FontFamily, true, Columns,
                    ref lineHeight, out double columnWidth, out double free, out double gap);
                ColumnWidth = columnWidth + (columns.IsColumnWidthFlexible ? free / Columns : 0);
                ColumnGap = gap;
                next = PortableFlowDocumentLayout.Create(document, ColumnWidth, document.PixelsPerDip,
                    TextOptions.GetTextFormattingMode(document), new Thickness());
                if (next.HostedChildren.Count != 0)
                    throw new PlatformNotSupportedException("Paginated controls require native object fragmentation and page ownership.");
                if (next.HasTables)
                    throw new PlatformNotSupportedException("Paginated tables require native row/cell fragmentation and page ownership.");
                var input = CreateFragmentLines(next);
                placed = new PortableDocumentFragmentPosition[input.Length];
                var result = flow.Paginate(input, height, (uint)Columns, placed);
                // An empty source document has one real blank page, no synthetic text line.
                pageCount = Math.Max(1, checked((int)result.PageCount));
                if (pageCount > Math.Max(1, input.Length)) throw new InvalidOperationException("Document provider returned an invalid page count.");
                ValidatePositions(next, input, placed, pageCount, result.FragmentCount, height);
                ValidateDecoratedFragments(next, placed);
                pages = new PortableFlowDocumentPage[pageCount];
                pageStarts = new int[pageCount + 1];
                int cursor = 0;
                for (int page = 0; page < pageCount; ++page)
                {
                    pageStarts[page] = cursor;
                    while (cursor < placed.Length && placed[cursor].Page == page) ++cursor;
                }
                pageStarts[pageCount] = cursor;
                if (cursor != placed.Length) throw new InvalidOperationException("Document provider returned unordered page assignments.");
                document.StructuralCache.DetectInvalidOperation();
            }
            catch { next?.Dispose(); throw; }
        }
        ReleasePages(); Layout?.Dispose();
        Layout = next; Positions = placed;
        _pages = pages; PageStarts = pageStarts;
        _dirty = false;
        document.StructuralCache.ClearUpdateInfo(false);
        OnPaginationProgress(new(0, pageCount)); OnPaginationCompleted(EventArgs.Empty);
    }

    private static Thickness FitPadding(Thickness padding, Size size)
    {
        if (padding.Left + padding.Right > size.Width)
        { double scale = size.Width / (padding.Left + padding.Right); padding.Left *= scale; padding.Right *= scale; }
        if (padding.Top + padding.Bottom > size.Height)
        { double scale = size.Height / (padding.Top + padding.Bottom); padding.Top *= scale; padding.Bottom *= scale; }
        return padding;
    }

    private void ValidatePositions(PortableFlowDocumentLayout layout, PortableDocumentFragmentLine[] input,
        PortableDocumentFragmentPosition[] positions, int pages, uint expectedFragments, double height)
    {
        uint fragments = 0;
        for (int i = 0; i < positions.Length; ++i)
        {
            var current = positions[i];
            if (current.Page >= pages || current.Column >= Columns || !double.IsFinite(current.Y) || current.Y < 0 ||
                current.Y + input[i].Height > height || (i == 0 && (current.Page != 0 || current.Column != 0)))
                throw new InvalidOperationException("Document provider returned invalid page geometry.");
            bool first = i == 0 || current.Page != positions[i - 1].Page || current.Column != positions[i - 1].Column;
            if (first)
            {
                ++fragments;
                if (current.Y < input[i].LeadingSpace)
                    throw new InvalidOperationException("Document provider omitted leading block insets.");
            }
            if (i == 0) continue;
            var previous = positions[i - 1];
            if (current.Page < previous.Page || current.Page > previous.Page + 1 ||
                (current.Page == previous.Page && (current.Column < previous.Column ||
                    (current.Column == previous.Column && current.Y < previous.Y + layout.Lines[i - 1].Advance))))
                throw new InvalidOperationException("Document provider returned unordered page geometry.");
            // The fragment visual translates retained source drawing once.
            // Its interaction positions must retain the same interior spacing.
            if (!first && !DoubleUtil.AreClose(current.Y - previous.Y,
                layout.Positions[i].Y - layout.Positions[i - 1].Y))
                throw new InvalidOperationException("Document provider changed retained fragment spacing.");
        }
        if (fragments != expectedFragments)
            throw new InvalidOperationException("Document provider returned an invalid fragment count.");
        if (positions.Length > 0 && positions[^1].Page != pages - 1)
            throw new InvalidOperationException("Document provider omitted a declared page.");
    }

    private static PortableDocumentFragmentLine[] CreateFragmentLines(PortableFlowDocumentLayout layout)
    {
        var lines = layout.Lines; var result = new PortableDocumentFragmentLine[lines.Count];
        for (int i = 0; i < lines.Count; ++i)
        {
            var entry = lines[i]; var paragraph = entry.Paragraph;
            if (paragraph.MinOrphanLines != 0 || paragraph.MinWidowLines != 0)
                throw new PlatformNotSupportedException("Fragment-relative widow/orphan constraints require the extended native break contract.");
            bool first = i == 0 || !ReferenceEquals(lines[i - 1].Paragraph, paragraph);
            bool allowed = first ? i == 0 || !lines[i - 1].Paragraph.KeepWithNext : !paragraph.KeepTogether;
            result[i] = new() { AllowBreakBefore = allowed ? 1U : 0U, Height = entry.Advance,
                LeadingSpace = i == 0 ? layout.Positions[i].Y : 0 };
        }
        // Ancestor break-before properties apply at their first actual source line.
        var blocks = layout.BlockDescriptors;
        for (int i = 1; i < blocks.Length; ++i)
        {
            int line = checked((int)blocks[i].LineStart);
            int end = blocks[i].SubtreeEnd < blocks.Length ? checked((int)blocks[(int)blocks[i].SubtreeEnd].LineStart) : lines.Count;
            if (line >= end) continue;
            // Native boxes already resolve nested content placement. Preserve
            // their border/padding extents at first/last source lines, without
            // inventing glyphs or re-running block layout in the source adapter.
            var box = layout.Boxes[i];
            result[line].LeadingSpace = Math.Max(result[line].LeadingSpace,
                layout.Positions[line].Y - (box.Y - blocks[i].InsetTop));
            result[end - 1].Height = Math.Max(result[end - 1].Height,
                box.Y + box.Height + blocks[i].InsetBottom - layout.Positions[end - 1].Y);
            if (layout.Blocks[i].Element is Block block)
            {
                if (block.BreakPageBefore) { result[line].ForcePageBefore = 1; result[line].ForceColumnBefore = 0; }
                else if (block.BreakColumnBefore && result[line].ForcePageBefore == 0) result[line].ForceColumnBefore = 1;
            }
        }
        for (int i = 0; i < result.Length; ++i)
            result[i].SpaceBefore = i == 0 ? layout.Positions[i].Y :
                Math.Max(0, layout.Positions[i].Y - layout.Positions[i - 1].Y - result[i - 1].Height);
        return result;
    }

    private static void ValidateDecoratedFragments(PortableFlowDocumentLayout layout, PortableDocumentFragmentPosition[] positions)
    {
        var blocks = layout.BlockDescriptors;
        for (int i = 1; i < blocks.Length; ++i)
        {
            var entry = layout.Blocks[i];
            if (entry.Element.Background == null && (entry.Box.BorderBrush == null || entry.Box.Border == new Thickness())) continue;
            int first = checked((int)blocks[i].LineStart);
            int end = blocks[i].SubtreeEnd < blocks.Length ? checked((int)blocks[(int)blocks[i].SubtreeEnd].LineStart) : layout.Lines.Count;
            if (first >= end)
                throw new PlatformNotSupportedException("An empty decorated block requires a native box-only page fragment.");
            if (positions[first].Page != positions[end - 1].Page || positions[first].Column != positions[end - 1].Column)
                throw new PlatformNotSupportedException("Decorated blocks crossing page/column boundaries require the fragmented box-decoration contract.");
        }
    }

    private void ReleasePages()
    {
        foreach (var page in _pages) page?.Dispose();
        _pages = Array.Empty<PortableFlowDocumentPage>();
    }
    private void Invalidate()
    {
        _dirty = true;
        ReleasePages(); Layout?.Dispose(); Layout = null;
        OnPagesChanged(new(0, int.MaxValue)); Schedule();
    }
    private void Schedule()
    {
        if (!_background || _suspended) { _pending?.Abort(); _pending = null; return; }
        if (_pending != null || !_dirty) return;
        _pending = document.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        { _pending = null; if (_background && !_suspended && _dirty) EnsureLayout(); }));
    }
    void IFlowDocumentFormatter.OnContentInvalidated(bool affectsLayout) => Invalidate();
    void IFlowDocumentFormatter.OnContentInvalidated(bool affectsLayout, ITextPointer start, ITextPointer end) => Invalidate();
    void IFlowDocumentFormatter.Suspend() { _suspended = true; _background = false; Schedule(); Invalidate(); }
    bool IFlowDocumentFormatter.IsLayoutDataValid => LayoutValid;
}
