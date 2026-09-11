// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.PtsHost;
using MS.Internal.Text;
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;
using DocumentList = System.Windows.Documents.List;
using Section = System.Windows.Documents.Section;

namespace MS.Internal.Documents;

// Immutable source layout generation. WPF owns properties, text positions and
// TextLines; ProGPU owns paragraph shaping and all block-placement arithmetic.
internal sealed class PortableFlowDocumentLayout : IDisposable
{
    internal sealed record BlockEntry(TextElement Element, MbpInfo Box, DocumentList MarkerList, int MarkerIndex);
    internal sealed record LineEntry(Paragraph Paragraph, TextLine Line, int Start, int BlockIndex, double Advance,
        PortableTextFragment? Fragment = null);
    internal sealed record MarkerEntry(ListItem Item, TextLine Line, int TargetLine, double Offset);
    internal sealed record ObjectEntry(BlockUIContainer Container, UIElement Child, int BlockIndex,
        int Start, int End, int ContentStart, int ContentEnd);
    internal readonly record struct FlowItem(int LineIndex, int ObjectIndex);
    internal sealed record HostedChild(TextElement Owner, UIElement Child, int BlockIndex, int LineIndex, Rect LineBounds);
    private readonly List<HostedChild> _hostedChildren = new();
    private readonly Dictionary<TextElement, int> _hostedOwners = new();
    internal IReadOnlyList<HostedChild> HostedChildren => _hostedChildren;
    private readonly record struct TablePolicy(int ColumnStart, int ColumnCount, double Spacing);

    private readonly List<BlockEntry> _entries = new();
    private readonly List<PortableDocumentBlock> _blocks = new();
    private readonly List<LineEntry> _lines = new();
    private readonly List<ObjectEntry> _objects = new();
    private List<FlowItem> _items;
    private readonly List<PortableDocumentRow> _rows = new();
    private readonly List<PortableDocumentCell> _cells = new();
    private readonly List<PortableDocumentPositionedParagraph> _positionedParagraphs = new();
    internal bool HasPositionedParagraphs => _positionedParagraphs.Count != 0;
    private readonly List<double> _columns = new();
    private int[] _firstItems, _childStarts, _children, _siblingSlots;
    internal bool HasTables => _rows.Count != 0;
    internal IReadOnlyList<PortableDocumentRow> Rows => _rows;
    internal IReadOnlyList<PortableDocumentCell> Cells => _cells;
    internal IReadOnlyList<ObjectEntry> Objects => _objects;
    internal IReadOnlyList<FlowItem> Items => _items ?? (IReadOnlyList<FlowItem>)Array.Empty<FlowItem>();
    private readonly List<MarkerEntry> _markers = new();
    private bool _disposed;
    internal IReadOnlyList<BlockEntry> Blocks => _entries;
    internal IReadOnlyList<LineEntry> Lines => _lines;
    internal IReadOnlyList<MarkerEntry> Markers => _markers;
    internal ReadOnlySpan<PortableDocumentBlock> BlockDescriptors => CollectionsMarshal.AsSpan(_blocks);
    internal PortableDocumentBox[] Boxes { get; private set; }
    internal PortableDocumentLinePosition[] Positions { get; private set; }
    internal Size Size { get; private set; }
    internal double? MeasuredContentWidth { get; private set; }

    internal Rect HostedChildBounds(int index)
    {
        var child = _hostedChildren[index];
        if (child.LineIndex < 0)
        {
            var box = Boxes[child.BlockIndex];
            return new(box.X, box.Y, box.Width, box.Height);
        }
        var position = Positions[child.LineIndex];
        Rect bounds = child.LineBounds;
        bounds.Offset(position.X, position.Y);
        return bounds;
    }

    internal bool TryGetHostedChildBounds(TextElement owner, out Rect bounds)
    {
        if (_hostedOwners.TryGetValue(owner, out int index)) { bounds = HostedChildBounds(index); return true; }
        bounds = Rect.Empty; return false;
    }

    private void AddHostedChild(HostedChild child)
    {
        _hostedOwners.Add(child.Owner, _hostedChildren.Count);
        _hostedChildren.Add(child);
    }

    internal static PortableFlowDocumentLayout Create(FlowDocument document, double pageWidth, double pixelsPerDip,
        TextFormattingMode formattingMode, Thickness? pagePadding = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return CreateCore(document, document.Blocks, pageWidth, pixelsPerDip, formattingMode, pagePadding, false);
    }

    // Measure original anchored blocks with the same native document/paragraph
    // services. Explicit requests are source-owned and scoped to actual paragraphs.
    internal static PortableFlowDocumentLayout CreateWithExclusions(FlowDocument document, double pageWidth,
        double pixelsPerDip, TextFormattingMode formattingMode,
        IReadOnlyDictionary<Paragraph, PortableTextExclusionRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(requests);
        var snapshot = new Dictionary<Paragraph, PortableTextExclusionRequest>(requests);
        foreach (var pair in snapshot)
            if (pair.Value == null || !ReferenceEquals(pair.Key.TextContainer, document.TextContainer))
                throw new InvalidOperationException("Excluded paragraphs must retain their actual source document.");
        return CreateCore(document, document.Blocks, pageWidth, pixelsPerDip, formattingMode, null, false, snapshot);
    }

    // Measure original anchored blocks with the same native document/paragraph
    // services. The parent owns anchor margins/insets and placement; this local
    // generation owns only child TextLines and their original document offsets.
    internal static PortableFlowDocumentLayout CreateAnchored(FlowDocument document, AnchoredBlock anchor,
        double contentWidth, double pixelsPerDip, TextFormattingMode formattingMode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(anchor);
        if (!ReferenceEquals(anchor.TextContainer, document.TextContainer))
            throw new InvalidOperationException("Anchored content must belong to the original source document.");
        return CreateCore(document, anchor.Blocks, contentWidth, pixelsPerDip, formattingMode, new Thickness(0), true);
    }

    private static PortableFlowDocumentLayout CreateCore(FlowDocument document, BlockCollection sourceBlocks,
        double pageWidth, double pixelsPerDip, TextFormattingMode formattingMode, Thickness? pagePadding,
        bool requiresAnchoredFlow, IReadOnlyDictionary<Paragraph, PortableTextExclusionRequest> exclusions = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (PortableWpfRuntime.GetMediaBackendAndFreeze() != PortableWpfMediaBackend.Portable)
            throw new InvalidOperationException("Portable document layout requires portable media ownership.");
        if (!double.IsFinite(pageWidth) || pageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pageWidth));
        if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        if (!PortableWpfServiceRegistry.TryGetDocumentFlow(out var flow) ||
            !PortableWpfServiceRegistry.TryGetTextFormatting(out _))
            throw new PlatformNotSupportedException("Portable FlowDocument requires registered native document and text services before layout.");
        if (requiresAnchoredFlow && flow is not IPortableAnchoredDocumentFlow)
            throw new PlatformNotSupportedException("Anchored document measurement requires the explicit native anchor capability.");
        if (requiresAnchoredFlow && flow is not IPortableMeasuredDocumentFlow)
            throw new PlatformNotSupportedException("Anchored document measurement requires native content extent measurement.");
        if (exclusions is { Count: > 0 } && flow is not IPortablePositionedDocumentFlow)
            throw new PlatformNotSupportedException("Excluded source paragraphs require native positioned document arrangement.");
        if (document.FlowDirection != FlowDirection.LeftToRight)
            throw new PlatformNotSupportedException("Portable RTL document block ordering is not implemented.");
        var layout = new PortableFlowDocumentLayout();
        try
        {
            Thickness padding = pagePadding ?? document.ComputePageMargin();
            // PagePadding policy is source-owned. Proportional reduction is the
            // documented FlowDocument behavior when padding exhausts page width.
            double horizontal = padding.Left + padding.Right;
            if (horizontal > pageWidth)
            {
                double scale = pageWidth / horizontal;
                padding.Left *= scale; padding.Right *= scale;
            }
            layout._entries.Add(new(null, null, null, 0));
            layout._blocks.Add(new() { ParentIndex = PortableDocumentBlock.NoParent,
                InsetLeft = padding.Left, InsetTop = padding.Top, InsetRight = padding.Right, InsetBottom = padding.Bottom });
            foreach (Block block in sourceBlocks) layout.AddBlock(block, 0, 1, pixelsPerDip);
            layout.CloseSubtree(0);
            layout.Boxes = new PortableDocumentBox[layout._blocks.Count];
            if (layout.HasTables)
            {
                layout._items = new();
                layout._firstItems = new int[layout._blocks.Count + 1];
                flow.ResolveWidthsWithRows(CollectionsMarshal.AsSpan(layout._blocks), pageWidth,
                    CollectionsMarshal.AsSpan(layout._rows), CollectionsMarshal.AsSpan(layout._columns),
                    CollectionsMarshal.AsSpan(layout._cells), layout.Boxes);
            }
            else flow.ResolveWidths(CollectionsMarshal.AsSpan(layout._blocks), pageWidth, layout.Boxes);
            TextFormatter formatter = TextFormatter.FromCurrentDispatcher(formattingMode);
            var metrics = new List<PortableDocumentLine>();
            var objects = new List<PortableDocumentObject>();
            int consumedExclusions = 0;
            for (int i = 0; i < layout._entries.Count; ++i)
            {
                PortableDocumentBlock descriptor = layout._blocks[i];
                if (layout.HasTables) layout._firstItems[i] = layout._items.Count;
                descriptor.LineStart = checked((uint)layout._lines.Count);
                if (layout._entries[i].Element is TableCell && layout.Boxes[i].Width <= 0)
                    throw new PlatformNotSupportedException("Exhausted table cell width requires the zero-width content contract.");
                if (layout._entries[i].Element is Paragraph paragraph)
                {
                    if (paragraph.TextIndent != 0)
                        throw new PlatformNotSupportedException("Portable document first-line indentation requires native per-line width constraints.");
                    if (paragraph.IsHyphenationEnabled)
                        throw new PlatformNotSupportedException("Portable document hyphenation requires the native discretionary-break contract.");
                    if (layout.Boxes[i].Width <= 0)
                        throw new PlatformNotSupportedException("Exhausted document width requires zero-width wrapping, not unbounded paragraph formatting.");
                    PortableTextExclusionRequest request = null;
                    if (exclusions != null && exclusions.TryGetValue(paragraph, out request)) ++consumedExclusions;
                    layout.FormatParagraph(document, paragraph, i, pixelsPerDip, formatter, metrics, request);
                }
                else if (layout._entries[i].Element is BlockUIContainer container && container.Child is UIElement child)
                {
                    // Measure the original control at the native constraint. Its
                    // UI tree and source symbol are borrowed, never flattened into
                    // a text line or cloned into a substitute document.
                    child.Measure(new Size(layout.Boxes[i].Width, double.PositiveInfinity));
                    Size desired = child.DesiredSize;
                    objects.Add(new() { BlockIndex = checked((uint)i), Width = desired.Width, Height = desired.Height });
                    if (layout._items == null)
                    {
                        layout._items = new(layout._lines.Count + 1);
                        for (int lineIndex = 0; lineIndex < layout._lines.Count; ++lineIndex)
                            layout._items.Add(new(lineIndex, -1));
                    }
                    layout._items.Add(new(-1, layout._objects.Count));
                    layout._objects.Add(new(container, child, i, container.ElementStart.Offset, container.ElementEnd.Offset,
                        container.ContentStart.Offset, container.ContentEnd.Offset));
                    layout.AddHostedChild(new(container, child, i, -1, Rect.Empty));
                }
                descriptor.LineCount = checked((uint)layout._lines.Count - descriptor.LineStart);
                layout._blocks[i] = descriptor;
            }
            layout.Positions = new PortableDocumentLinePosition[layout._lines.Count];
            if (consumedExclusions != (exclusions?.Count ?? 0))
                throw new InvalidOperationException("An exclusion request did not target a formatted source paragraph.");
            PortableDocumentExtent extent;
            if (layout.HasPositionedParagraphs || requiresAnchoredFlow)
            {
                var local = layout.HasPositionedParagraphs
                    ? new PortableDocumentLinePosition[layout._lines.Count]
                    : Array.Empty<PortableDocumentLinePosition>();
                for (int i = 0; i < local.Length; ++i)
                    if (layout._lines[i].Fragment is { } fragment)
                        local[i].Y = fragment.Top; // X already belongs to TextLine.Start/native origin.
                if (requiresAnchoredFlow)
                {
                    extent = ((IPortableMeasuredDocumentFlow)flow).ArrangeWithContentMeasurement(
                        CollectionsMarshal.AsSpan(layout._blocks), pageWidth,
                        CollectionsMarshal.AsSpan(metrics), CollectionsMarshal.AsSpan(objects),
                        CollectionsMarshal.AsSpan(layout._rows), CollectionsMarshal.AsSpan(layout._columns),
                        CollectionsMarshal.AsSpan(layout._cells), CollectionsMarshal.AsSpan(layout._positionedParagraphs),
                        local, layout.Boxes, layout.Positions, out double contentWidth);
                    layout.MeasuredContentWidth = contentWidth;
                }
                else extent = ((IPortablePositionedDocumentFlow)flow).ArrangeWithPositionedParagraphs(
                    CollectionsMarshal.AsSpan(layout._blocks), pageWidth,
                    CollectionsMarshal.AsSpan(metrics), CollectionsMarshal.AsSpan(objects),
                    CollectionsMarshal.AsSpan(layout._rows), CollectionsMarshal.AsSpan(layout._columns),
                    CollectionsMarshal.AsSpan(layout._cells), CollectionsMarshal.AsSpan(layout._positionedParagraphs),
                    local, layout.Boxes, layout.Positions);
            }
            else extent = layout.HasTables
                ? flow.ArrangeWithRows(CollectionsMarshal.AsSpan(layout._blocks), pageWidth,
                    CollectionsMarshal.AsSpan(metrics), CollectionsMarshal.AsSpan(objects),
                    CollectionsMarshal.AsSpan(layout._rows), CollectionsMarshal.AsSpan(layout._columns),
                    CollectionsMarshal.AsSpan(layout._cells), layout.Boxes, layout.Positions)
                : flow.ArrangeWithObjects(CollectionsMarshal.AsSpan(layout._blocks), pageWidth,
                    CollectionsMarshal.AsSpan(metrics), CollectionsMarshal.AsSpan(objects), layout.Boxes, layout.Positions);
            layout.Size = new(extent.Width, extent.Height);
            if (layout.HasTables)
            {
                layout._firstItems[^1] = layout._items.Count;
                layout.BuildNavigation();
            }
            layout.FormatMarkers(document, pixelsPerDip, formatter);
            return layout;
        }
        catch
        {
            layout.Dispose();
            throw;
        }
    }

    private void AddBlock(TextElement element, int parent, int depth, double pixelsPerDip,
        DocumentList markerList = null, int markerIndex = 0, TablePolicy? tablePolicy = null, int nativeRow = -1)
    {
        if (depth >= 128 || _entries.Count >= 1 << 20)
            throw new PlatformNotSupportedException("Portable document block budget exceeded.");
        if (element is not Paragraph and not Section and not DocumentList and not ListItem and not BlockUIContainer
            and not Table and not TableRowGroup and not TableRow and not TableCell)
            throw new PlatformNotSupportedException("This portable document block requires its native layout contract.");
        if ((FlowDirection)element.GetValue(Block.FlowDirectionProperty) != FlowDirection.LeftToRight)
            throw new PlatformNotSupportedException("Portable RTL document block ordering is not implemented.");
        MbpInfo box = MbpInfo.FromElement(element, pixelsPerDip);
        int index = _entries.Count;
        _entries.Add(new(element, box, markerList, markerIndex));
        _blocks.Add(new() { ParentIndex = checked((uint)parent),
            MarginLeft = box.Margin.Left, MarginTop = box.Margin.Top,
            MarginRight = box.Margin.Right, MarginBottom = box.Margin.Bottom,
            InsetLeft = box.Padding.Left + box.Border.Left, InsetTop = box.Padding.Top + box.Border.Top,
            InsetRight = box.Padding.Right + box.Border.Right, InsetBottom = box.Padding.Bottom + box.Border.Bottom });
        switch (element)
        {
            case Table table:
                if (table.Columns.Count == 0 || table.Columns.Count < table.ColumnCount)
                    throw new PlatformNotSupportedException("Portable automatic table columns require native intrinsic sizing.");
                int columnStart = _columns.Count, firstRow = _rows.Count;
                if (table.Columns.Count > (1 << 20) - columnStart)
                    throw new PlatformNotSupportedException("Portable table column budget exceeded.");
                foreach (TableColumn column in table.Columns)
                {
                    if (!column.Width.IsAbsolute)
                        throw new PlatformNotSupportedException("Portable automatic/star table columns require native intrinsic sizing.");
                    _columns.Add(column.Width.Value);
                }
                var policy = new TablePolicy(columnStart, table.Columns.Count, table.CellSpacing);
                foreach (TableRowGroup group in table.RowGroups)
                    AddBlock(group, index, depth + 1, pixelsPerDip, tablePolicy: policy);
                if (_rows.Count == firstRow)
                    throw new PlatformNotSupportedException("Empty portable tables require a source insertion-row contract.");
                break;
            case TableRowGroup group:
                foreach (TableRow row in group.Rows)
                    AddBlock(row, index, depth + 1, pixelsPerDip, tablePolicy: tablePolicy);
                break;
            case TableRow row:
                if (tablePolicy is not TablePolicy rowPolicy || row.Cells.Count == 0)
                    throw new PlatformNotSupportedException("A portable table row requires actual source cells and column policy.");
                int rowIndex = _rows.Count;
                _rows.Add(new() { BlockIndex = checked((uint)index),
                    ColumnStart = checked((uint)rowPolicy.ColumnStart), ColumnCount = checked((uint)rowPolicy.ColumnCount),
                    CellSpacing = rowPolicy.Spacing });
                foreach (TableCell cell in row.Cells)
                    AddBlock(cell, index, depth + 1, pixelsPerDip, nativeRow: rowIndex);
                break;
            case TableCell cell:
                if (nativeRow < 0 || cell.RowSpan != 1)
                    throw new PlatformNotSupportedException("Portable row-spanning cells require native span-height constraints.");
                _cells.Add(new() { BlockIndex = checked((uint)index), RowIndex = checked((uint)nativeRow),
                    ColumnStart = checked((uint)cell.ColumnIndex), ColumnCount = checked((uint)cell.ColumnSpan) });
                foreach (Block child in cell.Blocks) AddBlock(child, index, depth + 1, pixelsPerDip);
                break;
            case Section section:
                foreach (Block child in section.Blocks) AddBlock(child, index, depth + 1, pixelsPerDip);
                break;
            case ListItem item:
                foreach (Block child in item.Blocks) AddBlock(child, index, depth + 1, pixelsPerDip);
                break;
            case DocumentList list:
                long ordinal = list.StartIndex;
                foreach (ListItem child in list.ListItems)
                    AddBlock(child, index, depth + 1, pixelsPerDip, list, checked((int)ordinal++));
                break;
        }
        CloseSubtree(index);
    }

    private void CloseSubtree(int index)
    {
        var block = _blocks[index]; block.SubtreeEnd = checked((uint)_blocks.Count); _blocks[index] = block;
    }

    // Navigation annotates this same source forest; it never computes layout
    // or sorts source text into a second document. Empty structural blocks have
    // no caret item. Native row boxes choose X; other containers choose Y.
    private void BuildNavigation()
    {
        int count = _blocks.Count;
        _childStarts = new int[count + 1];
        _siblingSlots = new int[count];
        Array.Fill(_siblingSlots, -1);
        foreach (var cell in _cells)
            if (FirstItem((int)cell.BlockIndex) == EndItem((int)cell.BlockIndex))
                throw new PlatformNotSupportedException("Empty portable cells require their source caret/insertion contract.");
        for (int i = 1; i < count; ++i)
            if (FirstItem(i) != EndItem(i)) ++_childStarts[checked((int)_blocks[i].ParentIndex) + 1];
        for (int i = 1; i <= count; ++i) _childStarts[i] += _childStarts[i - 1];
        _children = new int[_childStarts[^1]];
        int[] next = (int[])_childStarts.Clone();
        for (int i = 1; i < count; ++i)
            if (FirstItem(i) != EndItem(i))
            {
                int slot = next[checked((int)_blocks[i].ParentIndex)]++;
                _children[slot] = i; _siblingSlots[i] = slot;
            }
    }

    internal int FirstItem(int block) => _firstItems[block];
    internal int EndItem(int block) => _firstItems[checked((int)_blocks[block].SubtreeEnd)];
    internal int ItemBlock(int item) => _items[item].ObjectIndex >= 0
        ? _objects[_items[item].ObjectIndex].BlockIndex : _lines[_items[item].LineIndex].BlockIndex;
    internal int ParentBlock(int block) => _blocks[block].ParentIndex == PortableDocumentBlock.NoParent
        ? -1 : checked((int)_blocks[block].ParentIndex);
    internal bool IsHorizontalRow(int block) => _entries[block].Element is TableRow;
    internal ReadOnlySpan<int> NavigationChildren(int block)
        => _children.AsSpan(_childStarts[block], _childStarts[block + 1] - _childStarts[block]);
    internal int SiblingIndex(int block) => _siblingSlots[block] - _childStarts[ParentBlock(block)];

    private void FormatParagraph(FlowDocument document, Paragraph paragraph, int blockIndex, double pixelsPerDip,
        TextFormatter formatter, List<PortableDocumentLine> metrics, PortableTextExclusionRequest exclusions = null)
    {
        var source = new PortableDocumentParagraphSource(paragraph, pixelsPerDip, Boxes[blockIndex].Width, exclusions);
        var properties = new LineProperties(paragraph, document,
            new TextProperties(paragraph, paragraph.StaticElementStart, false, false, pixelsPerDip), null);
        var cache = new TextRunCache();
        TextLineBreak continuation = null;
        int extentIndex = -1;
        try
        {
            int position = source.Start;
            while (position < source.End)
            {
                if (_lines.Count >= 1 << 20) throw new PlatformNotSupportedException("Portable document line budget exceeded.");
                TextLine line = formatter.FormatLine(source, position, Boxes[blockIndex].Width,
                    position == source.Start ? properties.FirstLineProps : properties, continuation, cache);
                try
                {
                    if (line.Length <= 0 || line.Length > source.End - position)
                        throw new InvalidOperationException("Formatted paragraph line did not preserve its source range.");
                    double advance = properties.CalcLineAdvance(line.Height);
                    PortableTextFragment? fragment = (line as PortableTextLine)?.Fragment;
                    if (exclusions != null && !fragment.HasValue)
                        throw new InvalidOperationException("Excluded source formatting lost its fragment frame.");
                    if (fragment.HasValue)
                    {
                        if (advance != line.Height)
                            throw new PlatformNotSupportedException("Excluded source row stacking requires matching native line-height policy.");
                        var nativeLine = (PortableTextLine)line;
                        if (extentIndex < 0)
                        {
                            extentIndex = _positionedParagraphs.Count;
                            _positionedParagraphs.Add(new() { BlockIndex = checked((uint)blockIndex),
                                Width = Math.Max(Boxes[blockIndex].Width, nativeLine.FragmentContentWidth),
                                Height = nativeLine.FragmentContentHeight });
                        }
                        else
                        {
                            var extent = _positionedParagraphs[extentIndex];
                            extent.Width = Math.Max(extent.Width, nativeLine.FragmentContentWidth);
                            extent.Height = Math.Max(extent.Height, nativeLine.FragmentContentHeight);
                            _positionedParagraphs[extentIndex] = extent;
                        }
                    }
                    if (source.HasInlineObjects)
                        foreach (TextBounds bounds in line.GetTextBounds(position, line.Length))
                            if (bounds.TextRunBounds != null)
                                foreach (TextRunBounds run in bounds.TextRunBounds)
                                    if (run.TextRun is PortableDocumentInlineObject embedded)
                                        AddHostedChild(new(embedded.Container, embedded.Child, blockIndex, _lines.Count, run.Rectangle));
                    metrics.Add(new() { Width = line.Start + line.WidthIncludingTrailingWhitespace, Height = advance });
                    _items?.Add(new(_lines.Count, -1));
                    _lines.Add(new(paragraph, line, position, blockIndex, advance, fragment));
                }
                catch { line.Dispose(); throw; }
                TextLineBreak next = line.GetTextLineBreak();
                continuation?.Dispose(); continuation = next;
                position += line.Length;
                if (exclusions != null && position < source.End && line is PortableTextLine { IsLastFragment: true } segment)
                    source.AdvanceExcludedSegment(position, segment.FragmentContentHeight);
            }
        }
        finally { continuation?.Dispose(); }
    }

    private void FormatMarkers(FlowDocument document, double pixelsPerDip, TextFormatter formatter)
    {
        for (int index = 0; index < _entries.Count; ++index)
        {
            var entry = _entries[index];
            if (entry.MarkerList == null || entry.MarkerList.MarkerStyle == TextMarkerStyle.None) continue;
            int first = checked((int)_blocks[index].LineStart);
            int subtreeEnd = checked((int)_blocks[index].SubtreeEnd);
            int end = subtreeEnd < _blocks.Count ? checked((int)_blocks[subtreeEnd].LineStart) : _lines.Count;
            if (first == end)
                throw new PlatformNotSupportedException("An empty list item requires a native marker-only line contract.");
            int low = 0, high = _objects.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_objects[middle].BlockIndex <= index) low = middle + 1; else high = middle;
            }
            if (low < _objects.Count && _objects[low].BlockIndex < _lines[first].BlockIndex)
                throw new PlatformNotSupportedException("A list marker before a block object requires an explicit object baseline contract.");
            Paragraph paragraph = _lines[first].Paragraph;
            var properties = new LineProperties(paragraph, document,
                new TextProperties(paragraph, paragraph.StaticElementStart, false, false, pixelsPerDip), null);
            TextMarkerProperties marker = new MarkerProperties(entry.MarkerList, entry.MarkerIndex).GetTextMarkerProperties(properties);
            if (entry.MarkerList.MarkerStyle is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box)
            {
                Typeface markerTypeface = marker.TextSource.GetTextRun(0).Properties.Typeface;
                if (!markerTypeface.TryGetGlyphTypeface(out GlyphTypeface face) || !face.Symbol)
                    throw new PlatformNotSupportedException("A source symbol-list marker requires its actual symbol face; ordinary-font fallback is not marker coverage.");
            }
            // Generated marker content is separate from document source positions.
            // Reuse WPF's marker source and actual font, never a substituted shape.
            TextLine line = formatter.FormatLine(marker.TextSource, 0, 0, new MarkerLineProperties(properties), null);
            try { _markers.Add(new((ListItem)entry.Element, line, first, marker.Offset)); }
            catch { line.Dispose(); throw; }
        }
    }

    private sealed class MarkerLineProperties(LineProperties source) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => source.FlowDirection;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => source.DefaultTextRunProperties;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties TextMarkerProperties => null;
        public override double Indent => 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var marker in _markers) marker.Line.Dispose();
        foreach (var line in _lines) line.Line.Dispose();
        _markers.Clear(); _lines.Clear(); _entries.Clear(); _blocks.Clear();
        _objects.Clear(); _items?.Clear();
        _hostedChildren.Clear(); _hostedOwners.Clear();
        _rows.Clear(); _cells.Clear(); _columns.Clear();
        _firstItems = _childStarts = _children = _siblingSlots = null;
    }
}
