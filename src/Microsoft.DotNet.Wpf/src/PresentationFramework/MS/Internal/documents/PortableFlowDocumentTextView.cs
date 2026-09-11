// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.TextFormatting;

namespace MS.Internal.Documents;

// Interaction borrows the same live TextLines that the document visual draws.
// No text copy, nominal glyph positions or independently formatted hit-test tree.
internal sealed class PortableFlowDocumentTextView(FlowDocumentView owner, FlowDocument document,
    PortableFlowDocumentPage page = null) : TextViewBase
{
    private readonly PortableFlowDocumentTextView _parentView;
    private readonly PortableFlowDocumentLayout _parentLayout, _childLayout;
    private readonly PortableDocumentAnchorSource _childSource;
    private readonly Vector _childOrigin;
    private sealed record AnchorView(PortableDocumentAnchorLayout.Placement Placement, Vector Origin,
        PortableFlowDocumentTextView View);
    private PortableFlowDocumentLayout _anchorViewLayout;
    private IReadOnlyList<AnchorView> _anchorViews = Array.Empty<AnchorView>();

    private PortableFlowDocumentTextView(FlowDocumentView owner, FlowDocument document,
        PortableFlowDocumentTextView parent, PortableFlowDocumentLayout parentLayout,
        PortableDocumentAnchorLayout.Placement placement, Vector origin) : this(owner, document)
    {
        _parentView = parent; _parentLayout = parentLayout; _childLayout = placement.Child.Layout;
        _childSource = placement.Child.Source; _childOrigin = origin;
    }

    private PortableFlowDocumentLayout Layout => _childLayout ?? (page == null ? owner.PortableLayout : page.Layout);
    private int FirstLine => page?.FirstLine ?? 0;
    private int EndLine => page?.EndLine ?? Layout.Lines.Count;
    private Vector ScrollOffset => _parentView != null ? _parentView.ScrollOffset - _childOrigin :
        page == null ? owner.PortableScrollOffset : new Vector();
    private Size ViewportSize => page == null ? owner.RenderSize : page.Size;
    private ProGPU.Wpf.Interop.PortableDocumentLinePosition Position(int index) => page == null ? Layout.Positions[index] : page.Position(index);
    internal override UIElement RenderScope => page == null ? owner : page.RenderScope;
    internal override ITextContainer TextContainer => document.TextContainer;
    internal override bool IsValid => _parentView != null ? _parentView.IsValid &&
        ReferenceEquals(_parentLayout, _parentView.Layout) && !_childLayout.IsDisposed :
        page == null ? ReferenceEquals(owner.Document, document) && owner.PortableLayoutValid &&
        owner.IsMeasureValid && owner.IsArrangeValid : page.IsValid;
    internal override ReadOnlyCollection<TextSegment> TextSegments => IsValid
        ? new(new[] { _childSource != null ? new TextSegment(_childSource.Anchor.ContentStart, _childSource.Anchor.ContentEnd, true) :
            page == null ? new TextSegment(TextContainer.Start, TextContainer.End, true) : page.TextSegment })
        : ReadOnlyCollection<TextSegment>.Empty;

    internal void PublishUpdate() => OnUpdated(EventArgs.Empty);
    internal override bool Validate()
    {
        if (page != null) return IsValid;
        if (ReferenceEquals(owner.Document, document) && (!owner.IsMeasureValid || !owner.IsArrangeValid)) owner.UpdateLayout();
        return IsValid;
    }

    internal override bool Contains(ITextPointer position) => IsValid && position != null &&
        ReferenceEquals(position.TextContainer, TextContainer) && position.Offset >= TextContainer.Start.Offset &&
        position.Offset <= TextContainer.End.Offset && (page == null || page.Contains(position)) &&
        (_childSource == null || position.Offset >= _childSource.Anchor.ContentStart.Offset && position.Offset <= _childSource.Anchor.ContentEnd.Offset);

    private IReadOnlyList<AnchorView> AnchorViews
    {
        get
        {
            var layout = Layout;
            if (page != null) return Array.Empty<AnchorView>();
            if (ReferenceEquals(layout, _anchorViewLayout)) return _anchorViews;
            var views = new List<AnchorView>();
            foreach (var paragraph in layout.Anchors)
            {
                var box = layout.Boxes[paragraph.BlockIndex];
                foreach (var placement in paragraph.Placement.Children)
                {
                    Vector origin = new(box.X + placement.ContentOrigin.X, box.Y + placement.ContentOrigin.Y);
                    views.Add(new(placement, origin, new(owner, document, this, layout, placement, origin)));
                }
            }
            _anchorViews = views; _anchorViewLayout = layout;
            return views;
        }
    }

    private AnchorView ChildAt(ITextPointer position)
    {
        var children = AnchorViews;
        int low = 0, high = children.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (children[middle].Placement.Child.Source.Start < position.Offset) low = middle + 1;
            else high = middle;
        }
        return low > 0 && position.Offset < children[low - 1].Placement.Child.Source.End ? children[low - 1] : null;
    }

    private void RequireValid()
    {
        if (!IsValid) throw new InvalidOperationException(SR.TextViewInvalidLayout);
    }
    private void RequirePosition(ITextPointer position)
    {
        RequireValid();
        if (!Contains(position)) throw new ArgumentException("Position belongs to another document.", nameof(position));
    }

    // Native positions are Y-ordered only within a vertical flow. Tables select
    // their source cell first, then the original line range inside that cell.
    internal override ITextPointer GetTextPositionFromPoint(Point point, bool snapToText)
    {
        RequireValid();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return null;
        var children = AnchorViews;
        Point local = point + ScrollOffset;
        for (int i = children.Count - 1; i >= 0; --i)
        {
            var child = children[i];
            if (new Rect((Point)child.Origin, child.Placement.Child.Layout.Size).Contains(local))
                return child.View.GetTextPositionFromPoint(point, snapToText);
        }
        point += ScrollOffset;
        if (page == null && Layout.HasTables)
        {
            int block = SelectNavigationLeaf(0, point, 0);
            return GetFlowPositionFromPoint(point, snapToText, Layout.FirstItem(block), Layout.EndItem(block));
        }
        if (page == null && Layout.Objects.Count != 0)
            return GetFlowPositionFromPoint(point, snapToText);
        if (FirstLine == EndLine) return snapToText ? TextContainer.Start.GetFrozenPointer(LogicalDirection.Forward) : null;
        int first = FirstLine, limit = EndLine;
        page?.PointLineRange(point, out first, out limit);
        int low = first, high = limit;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Position(middle).Y <= point.Y) low = middle + 1; else high = middle;
        }
        int index = Math.Max(first, low - 1);
        double bottom = Position(index).Y + Layout.Lines[index].Advance;
        if (point.Y > bottom && index + 1 < limit && point.Y - bottom > Position(index + 1).Y - point.Y) ++index;
        index = SelectRowFragment(index, first, limit, point.X, false);
        var entry = Layout.Lines[index]; var origin = Position(index);
        if (!snapToText && (point.Y < origin.Y || point.Y >= origin.Y + entry.Advance ||
            point.X < origin.X + entry.Line.Start || point.X > origin.X + entry.Line.Start + entry.Line.WidthIncludingTrailingWhitespace)) return null;
        return PositionFromDistance(index, point.X - origin.X);
    }

    private ITextPointer PositionFromDistance(int index, double distance)
    {
        var entry = Layout.Lines[index];
        CharacterHit hit = entry.Line.GetCharacterHitFromDistance(distance);
        int offset = Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, entry.Start, ContentEnd(index));
        return Pointer(offset, hit.TrailingLength > 0 ? LogicalDirection.Backward : LogicalDirection.Forward);
    }

    // Items reference either an original TextLine or an actual block control.
    // Native placement owns both rectangles; object metrics never masquerade as
    // shaped text or acquire glyph clusters. Source order also orders block Y.
    private Rect ObjectRect(int index)
    {
        var box = Layout.Boxes[Layout.Objects[index].BlockIndex];
        return new(box.X, box.Y, box.Width, box.Height);
    }

    private Rect ItemRect(int index)
    {
        var item = Layout.Items[index];
        if (item.ObjectIndex >= 0) return ObjectRect(item.ObjectIndex);
        var line = Layout.Lines[item.LineIndex]; var point = Layout.Positions[item.LineIndex];
        return new(point.X + line.Line.Start, point.Y, line.Line.WidthIncludingTrailingWhitespace, line.Advance);
    }

    private int ItemStart(int index)
    {
        var item = Layout.Items[index];
        return item.ObjectIndex >= 0 ? Layout.Objects[item.ObjectIndex].Start : Layout.Lines[item.LineIndex].Start;
    }

    private int FindItem(ITextPointer position)
    {
        int low = 0, high = Layout.Items.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (ItemStart(middle) <= position.Offset) low = middle + 1; else high = middle;
        }
        int index = Math.Max(0, low - 1);
        if (index > 0 && ItemStart(index) == position.Offset && position.LogicalDirection == LogicalDirection.Backward)
        {
            var previous = Layout.Items[index - 1]; var current = Layout.Items[index];
            // An object's ElementStart is its outside edge; preserve the
            // preceding insertion side there. A text line's start can refer
            // backward only to its own wrapped paragraph, not another cell.
            if (current.ObjectIndex >= 0 ||
                (previous.LineIndex >= 0 && current.LineIndex >= 0 &&
                    ReferenceEquals(Layout.Lines[previous.LineIndex].Paragraph, Layout.Lines[current.LineIndex].Paragraph) &&
                    Layout.Lines[previous.LineIndex].Start + Layout.Lines[previous.LineIndex].Line.Length == position.Offset)) --index;
        }
        return index;
    }

    private int ObjectAt(ITextPointer position)
        => page == null && Layout.Objects.Count != 0 ? Layout.Items[FindItem(position)].ObjectIndex : -1;

    private ITextPointer PositionFromItemX(int index, double x)
    {
        var item = Layout.Items[index];
        if (item.ObjectIndex < 0) return PositionFromDistance(item.LineIndex, x - Layout.Positions[item.LineIndex].X);
        var embedded = Layout.Objects[item.ObjectIndex]; var box = ObjectRect(item.ObjectIndex);
        bool trailing = x > box.X + box.Width / 2;
        return Pointer(trailing ? embedded.ContentEnd : embedded.ContentStart,
            trailing ? LogicalDirection.Backward : LogicalDirection.Forward);
    }

    private ITextPointer GetFlowPositionFromPoint(Point point, bool snapToText, int first = 0, int end = -1)
    {
        if (end < 0) end = Layout.Items.Count;
        if (first == end) return snapToText ? TextContainer.Start.GetFrozenPointer(LogicalDirection.Forward) : null;
        int low = first, high = end;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (ItemRect(middle).Y <= point.Y) low = middle + 1; else high = middle;
        }
        int index = Math.Max(first, low - 1);
        Rect box = ItemRect(index);
        if (point.Y > box.Bottom && index + 1 < end &&
            point.Y - box.Bottom > ItemRect(index + 1).Y - point.Y) box = ItemRect(++index);
        index = SelectRowFragment(index, first, end, point.X, true);
        box = ItemRect(index);
        if (!snapToText && (point.Y < box.Top || point.Y >= box.Bottom || point.X < box.Left || point.X > box.Right)) return null;
        return PositionFromItemX(index, point.X);
    }

    // Source order is not physical X order (in particular for RTL fragments).
    // Keep the selected native row, then choose its closest retained ink span.
    // This is a query over the drawing map, not a second paragraph layout.
    private int SelectRowFragment(int index, int first, int end, double x, bool items)
    {
        if (!Layout.HasPositionedParagraphs) return index;
        Rect Bounds(int candidate)
        {
            if (items) return ItemRect(candidate);
            var line = Layout.Lines[candidate]; var origin = Position(candidate);
            return new(origin.X + line.Line.Start, origin.Y,
                line.Line.WidthIncludingTrailingWhitespace, line.Advance);
        }
        double top = Bounds(index).Top;
        int start = index;
        while (start > first && Bounds(start - 1).Top == top) --start;
        int selected = index;
        double nearest = double.PositiveInfinity;
        for (int candidate = start; candidate < end; ++candidate)
        {
            Rect box = Bounds(candidate);
            if (box.Top != top) break;
            double distance = Math.Max(box.Left - x, Math.Max(0, x - box.Right));
            if (distance < nearest)
            {
                selected = candidate;
                nearest = distance;
            }
        }
        return selected;
    }

    // Only source hierarchy and native boxes participate here. Horizontal rows
    // select a cell by X; all other ancestors select vertical source content.
    // boundary chooses the first/last vertical child for up/down entry.
    private int SelectNavigationLeaf(int block, Point point, int boundary)
    {
        while (true)
        {
            ReadOnlySpan<int> children = Layout.NavigationChildren(block);
            if (children.IsEmpty) return block;
            bool horizontal = Layout.IsHorizontalRow(block);
            int selected;
            if (!horizontal && boundary != 0) selected = boundary > 0 ? 0 : children.Length - 1;
            else
            {
                double coordinate = horizontal ? point.X : point.Y;
                int low = 0, high = children.Length;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    var box = Layout.Boxes[children[middle]];
                    if ((horizontal ? box.X : box.Y) <= coordinate) low = middle + 1; else high = middle;
                }
                selected = Math.Max(0, low - 1);
                if (selected + 1 < children.Length)
                {
                    var current = Layout.Boxes[children[selected]]; var next = Layout.Boxes[children[selected + 1]];
                    double bottom = horizontal ? current.X + current.Width : current.Y + current.Height;
                    double nextStart = horizontal ? next.X : next.Y;
                    if (coordinate > bottom && coordinate - bottom > nextStart - coordinate) ++selected;
                }
            }
            block = children[selected];
        }
    }

    private bool TryNextNavigationItem(int current, int direction, double x, out int target)
    {
        int block = Layout.ItemBlock(current);
        target = current + direction;
        if (target >= Layout.FirstItem(block) && target < Layout.EndItem(block)) return true;
        while (Layout.ParentBlock(block) is int parent && parent >= 0)
        {
            // Do not move down into the adjacent horizontal cell. Leave the
            // whole row, then enter the next vertical subtree at retained X.
            if (!Layout.IsHorizontalRow(parent))
            {
                var siblings = Layout.NavigationChildren(parent);
                int sibling = Layout.SiblingIndex(block) + direction;
                if (sibling >= 0 && sibling < siblings.Length)
                {
                    int leaf = SelectNavigationLeaf(siblings[sibling], new(x, 0), direction);
                    target = direction > 0 ? Layout.FirstItem(leaf) : Layout.EndItem(leaf) - 1;
                    return true;
                }
            }
            block = parent;
        }
        target = current;
        return false;
    }

    private ITextPointer AdjacentItemBoundary(ITextPointer position, LogicalDirection direction)
    {
        int current = FindItem(position), target = current + (direction == LogicalDirection.Forward ? 1 : -1);
        if (target < 0 || target >= Layout.Items.Count) return position;
        var item = Layout.Items[target];
        int offset = item.ObjectIndex >= 0
            ? direction == LogicalDirection.Forward ? Layout.Objects[item.ObjectIndex].ContentStart : Layout.Objects[item.ObjectIndex].ContentEnd
            : direction == LogicalDirection.Forward ? Layout.Lines[item.LineIndex].Start : ContentEnd(item.LineIndex);
        return Pointer(offset, direction);
    }

    private ITextPointer Pointer(int offset, LogicalDirection direction)
    {
        var pointer = TextContainer.CreatePointerAtOffset(Math.Clamp(offset,
            _childSource?.Anchor.ContentStart.Offset ?? TextContainer.Start.Offset,
            _childSource?.Anchor.ContentEnd.Offset ?? TextContainer.End.Offset), direction);
        pointer = pointer.GetInsertionPosition(direction);
        pointer.Freeze();
        return pointer;
    }

    private int ContentEnd(int index) => Layout.Lines[index].Start + Layout.Lines[index].Line.Length - Layout.Lines[index].Line.NewlineLength;
    private int FindLine(ITextPointer position) => FindLine(position.Offset, position.LogicalDirection);
    private int FindLine(int sourceOffset, LogicalDirection direction)
    {
        int low = FirstLine, high = EndLine;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Layout.Lines[middle].Start <= sourceOffset) low = middle + 1; else high = middle;
        }
        int index = Math.Max(FirstLine, low - 1);
        if (index > FirstLine && Layout.Lines[index].Start == sourceOffset && direction == LogicalDirection.Backward &&
            ReferenceEquals(Layout.Lines[index - 1].Paragraph, Layout.Lines[index].Paragraph) &&
            Layout.Lines[index - 1].Start + Layout.Lines[index - 1].Line.Length == sourceOffset) --index;
        else if (index + 1 < EndLine && sourceOffset >= Layout.Lines[index].Start + Layout.Lines[index].Line.Length &&
            direction == LogicalDirection.Forward) ++index;
        return index;
    }

    private CharacterHit Hit(int index, ITextPointer position)
    {
        var entry = Layout.Lines[index];
        int offset = Math.Clamp(position.Offset, entry.Start, ContentEnd(index));
        if (position.LogicalDirection == LogicalDirection.Backward && offset > entry.Start)
        {
            CharacterHit previous = entry.Line.GetPreviousCaretCharacterHit(new(offset, 0));
            int previousOffset = previous.FirstCharacterIndex + previous.TrailingLength;
            if (previousOffset >= entry.Start && previousOffset < offset) return new(previousOffset, offset - previousOffset);
        }
        return new(offset, 0);
    }

    internal override Rect GetRawRectangleFromTextPosition(ITextPointer position, out Transform transform)
    {
        RequirePosition(position);
        if (ChildAt(position) is { } child) return child.View.GetRawRectangleFromTextPosition(position, out transform);
        Vector offset = ScrollOffset;
        transform = offset == new Vector() ? Transform.Identity : new TranslateTransform(-offset.X, -offset.Y);
        int objectIndex = ObjectAt(position);
        if (objectIndex >= 0)
        {
            var embedded = Layout.Objects[objectIndex]; var box = ObjectRect(objectIndex);
            return new(position.Offset <= embedded.ContentStart ? box.Left : box.Right, box.Top, 0, box.Height);
        }
        if (FirstLine == EndLine) return Rect.Empty;
        int index = FindLine(position);
        var entry = Layout.Lines[index]; var origin = Position(index);
        return new(origin.X + entry.Line.GetDistanceFromCharacterHit(Hit(index, position)), origin.Y, 0, entry.Line.Height);
    }

    internal ReadOnlyCollection<Rect> GetDocumentRectangles(ITextPointer start, ITextPointer end, bool paragraphBreaks)
    {
        RequireRange(start, end);
        if (end.Offset < start.Offset) throw new ArgumentException("A document range must be ordered.");
        var result = new List<Rect>();
        if (start.Offset == end.Offset) return result.AsReadOnly();
        int first = FirstLine == EndLine ? 0 : FindLine(start), last = FirstLine == EndLine ? -1 : FindLine(end);
        for (int index = first; index <= last; ++index)
        {
            var entry = Layout.Lines[index]; var origin = Position(index);
            int from = Math.Max(start.Offset, entry.Start), to = Math.Min(end.Offset, ContentEnd(index));
            void AddOwnRange(int rangeStart, int rangeEnd)
            {
                if (rangeEnd <= rangeStart) return;
                foreach (TextBounds bounds in entry.Line.GetTextBounds(rangeStart, rangeEnd - rangeStart))
                {
                    Rect rectangle = bounds.Rectangle; rectangle.Offset(origin.X, origin.Y); result.Add(rectangle);
                }
            }
            int cursor = from;
            foreach (var child in AnchorViews)
            {
                var source = child.Placement.Child.Source;
                if (source.End <= cursor) continue;
                if (source.Start >= to) break;
                AddOwnRange(cursor, Math.Min(to, source.Start));
                cursor = Math.Max(cursor, source.End);
                if (cursor >= to) break;
            }
            AddOwnRange(cursor, to);
            if (paragraphBreaks && entry.Line.NewlineLength > 0 && end.Offset > ContentEnd(index) &&
                start.Offset < entry.Start + entry.Line.Length)
            {
                double x = origin.X + entry.Line.GetDistanceFromCharacterHit(new(ContentEnd(index), 0));
                result.Add(new(x, origin.Y, entry.Paragraph.FontSize * CaretElement.c_endOfParaMagicMultiplier, entry.Advance));
            }
        }
        if (page == null)
        {
            int low = 0, high = Layout.Objects.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (Layout.Objects[middle].ContentEnd <= start.Offset) low = middle + 1; else high = middle;
            }
            for (int index = low; index < Layout.Objects.Count && Layout.Objects[index].ContentStart < end.Offset; ++index)
            {
                var embedded = Layout.Objects[index];
                if (embedded.ContentStart < end.Offset && embedded.ContentEnd > start.Offset)
                    result.Add(ObjectRect(index));
            }
        }
        foreach (var child in AnchorViews)
        {
            var source = child.Placement.Child.Source;
            if (source.Start >= end.Offset || source.End <= start.Offset) continue;
            foreach (Rect rectangle in child.View.GetDocumentRectangles(start, end, paragraphBreaks))
            {
                Rect translated = rectangle; translated.Offset(child.Origin); result.Add(translated);
            }
        }
        return result.AsReadOnly();
    }

    internal override Geometry GetTightBoundingGeometryFromTextPositions(ITextPointer startPosition, ITextPointer endPosition)
    {
        var rectangles = GetDocumentRectangles(startPosition, endPosition, true);
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        Vector offset = ScrollOffset;
        Rect viewport = new(ViewportSize);
        using (var context = geometry.Open())
            foreach (Rect rectangle in rectangles)
            {
                Rect visible = rectangle; visible.Offset(-offset.X, -offset.Y); visible.Intersect(viewport);
                if (visible.IsEmpty || visible.Width == 0 || visible.Height == 0) continue;
                AddRectangle(context, visible);
            }
        geometry.Freeze();
        return geometry;
    }

    internal static void AddRectangle(StreamGeometryContext context, Rect rectangle)
    {
        context.BeginFigure(rectangle.TopLeft, true, true);
        context.LineTo(rectangle.TopRight, true, false);
        context.LineTo(rectangle.BottomRight, true, false);
        context.LineTo(rectangle.BottomLeft, true, false);
    }

    internal override ITextPointer GetPositionAtNextLine(ITextPointer position, double suggestedX, int count,
        out double newSuggestedX, out int linesMoved)
    {
        RequirePosition(position);
        // Source TextDocumentView keeps vertical moves within the selected
        // floating element, including an exhausted move at its first/last row.
        // Child ScrollOffset already accounts for the content-origin X once.
        if (ChildAt(position) is { } child)
            return child.View.GetPositionAtNextLine(position, suggestedX, count, out newSuggestedX, out linesMoved);
        newSuggestedX = suggestedX; linesMoved = 0;
        if (page == null && Layout.HasPositionedParagraphs)
        {
            if (count == 0) return position.GetFrozenPointer(position.LogicalDirection);
            if (double.IsNaN(newSuggestedX)) newSuggestedX = GetRectangleFromTextPosition(position).X;
            if (!double.IsFinite(newSuggestedX)) throw new ArgumentOutOfRangeException(nameof(suggestedX));
            double x = newSuggestedX + ScrollOffset.X;
            bool items = Layout.HasTables || Layout.Objects.Count != 0;
            int limit = items ? Layout.Items.Count : EndLine;
            int current = items ? FindItem(position) : FindLine(position);
            int direction = Math.Sign(count);
            long remaining = Math.Min(Math.Abs((long)count), limit);
            while (remaining-- > 0)
            {
                int lineIndex = items ? Layout.Items[current].LineIndex : current;
                if (lineIndex >= 0 && Layout.Lines[lineIndex].Line is PortableTextLine line && line.Fragment != null)
                {
                    CharacterHit hit = Hit(lineIndex, position);
                    if (line.TryMoveFragmentCaret(hit.FirstCharacterIndex + hit.TrailingLength,
                        hit.TrailingLength != 0, direction > 0, x - Position(lineIndex).X,
                        out int offset, out bool trailing, out int delta) && delta != 0)
                    {
                        int targetLine = lineIndex + delta;
                        if (targetLine < FirstLine || targetLine >= EndLine ||
                            !ReferenceEquals(Layout.Lines[targetLine].Paragraph, Layout.Lines[lineIndex].Paragraph))
                            throw new InvalidOperationException("Native fragment movement left its source paragraph.");
                        current += delta; // A paragraph's retained fragments are contiguous items.
                        position = Pointer(offset, trailing ? LogicalDirection.Backward : LogicalDirection.Forward);
                        linesMoved += direction;
                        continue;
                    }
                }
                // At a native paragraph boundary, leave its entire physical row.
                // Table hierarchy still owns transitions out of a source cell.
                int next = current;
                double top = items ? ItemRect(current).Top : Position(current).Y;
                bool found = false;
                while (true)
                {
                    int candidate;
                    if (Layout.HasTables)
                    {
                        if (!TryNextNavigationItem(next, direction, x, out candidate)) break;
                    }
                    else
                    {
                        candidate = next + direction;
                        if (candidate < 0 || candidate >= limit) break;
                    }
                    next = candidate;
                    if ((items ? ItemRect(next).Top : Position(next).Y) != top) { found = true; break; }
                }
                if (!found) break;
                int first = 0, end = limit;
                if (Layout.HasTables)
                {
                    int block = Layout.ItemBlock(next);
                    first = Layout.FirstItem(block); end = Layout.EndItem(block);
                }
                current = SelectRowFragment(next, first, end, x, items);
                position = items ? PositionFromItemX(current, x) : PositionFromDistance(current, x - Position(current).X);
                linesMoved += direction;
            }
            return position.GetFrozenPointer(position.LogicalDirection);
        }
        if (page == null && Layout.HasTables)
        {
            if (count == 0) return position.GetFrozenPointer(position.LogicalDirection);
            if (double.IsNaN(newSuggestedX)) newSuggestedX = GetRectangleFromTextPosition(position).X;
            if (!double.IsFinite(newSuggestedX)) throw new ArgumentOutOfRangeException(nameof(suggestedX));
            int current = FindItem(position), direction = Math.Sign(count);
            long remaining = Math.Min(Math.Abs((long)count), Layout.Items.Count);
            while (remaining-- > 0 && TryNextNavigationItem(current, direction, newSuggestedX + ScrollOffset.X, out int targetItem))
            {
                current = targetItem; linesMoved += direction;
            }
            return linesMoved == 0 ? position.GetFrozenPointer(position.LogicalDirection)
                : PositionFromItemX(current, newSuggestedX + ScrollOffset.X);
        }
        if (page == null && Layout.Objects.Count != 0)
        {
            int current = FindItem(position);
            int targetItem = (int)Math.Clamp((long)current + count, 0, Layout.Items.Count - 1);
            linesMoved = targetItem - current;
            if (double.IsNaN(newSuggestedX)) newSuggestedX = GetRectangleFromTextPosition(position).X;
            return PositionFromItemX(targetItem, newSuggestedX + ScrollOffset.X);
        }
        if (FirstLine == EndLine) return position;
        int index = FindLine(position);
        int target = (int)Math.Clamp((long)index + count, FirstLine, EndLine - 1);
        linesMoved = target - index;
        if (linesMoved == 0) return position.GetFrozenPointer(position.LogicalDirection);
        if (double.IsNaN(suggestedX)) return Pointer(Layout.Lines[target].Start, LogicalDirection.Forward);
        if (!double.IsFinite(newSuggestedX)) throw new ArgumentOutOfRangeException(nameof(suggestedX));
        if (page != null) newSuggestedX += page.ColumnOffset(target) - page.ColumnOffset(index);
        return PositionFromDistance(target, newSuggestedX + ScrollOffset.X - Position(target).X);
    }

    internal override ITextPointer GetPositionAtNextPage(ITextPointer position, Point suggestedOffset, int count,
        out Point newSuggestedOffset, out int pagesMoved)
    {
        RequirePosition(position);
        newSuggestedOffset = suggestedOffset; pagesMoved = 0;
        if (page != null) return position; // MultiPageTextView owns cross-page navigation.
        var scroll = (IScrollInfo)owner;
        if (count == 0 || Layout.Lines.Count == 0 || scroll.ScrollOwner == null || scroll.ViewportHeight <= 0) return position;
        Rect caret = GetRectangleFromTextPosition(position);
        double x = double.IsNaN(suggestedOffset.X) ? caret.X : suggestedOffset.X;
        double y = double.IsNaN(suggestedOffset.Y) ? caret.Y : suggestedOffset.Y;
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(suggestedOffset));
        double documentY = y + scroll.VerticalOffset;
        double targetY = Math.Clamp(documentY + count * scroll.ViewportHeight, 0, Layout.Size.Height);
        var target = GetTextPositionFromPoint(new(x, targetY - scroll.VerticalOffset), true);
        if (target.CompareTo(position) == 0) return position;
        pagesMoved = (int)(Math.Sign(count) * Math.Min(Math.Abs((long)count), Math.Ceiling(Math.Abs(targetY - documentY) / scroll.ViewportHeight)));
        newSuggestedOffset = GetRectangleFromTextPosition(target).TopLeft;
        return target;
    }

    internal override bool IsAtCaretUnitBoundary(ITextPointer position)
    {
        RequirePosition(position);
        if (ChildAt(position) is { } child) return child.View.IsAtCaretUnitBoundary(position);
        int objectIndex = ObjectAt(position);
        if (objectIndex >= 0)
            return position.Offset == Layout.Objects[objectIndex].ContentStart || position.Offset == Layout.Objects[objectIndex].ContentEnd;
        if (FirstLine == EndLine) return position.Offset == TextContainer.Start.Offset;
        int index = FindLine(position);
        if (position.Offset < Layout.Lines[index].Start || position.Offset > ContentEnd(index)) return false;
        return Layout.Lines[index].Line.IsAtCaretCharacterHit(Hit(index, position), Layout.Lines[index].Start);
    }

    internal override ITextPointer GetNextCaretUnitPosition(ITextPointer position, LogicalDirection direction)
        => Move(position, direction, false);
    internal override ITextPointer GetBackspaceCaretUnitPosition(ITextPointer position) => Move(position, LogicalDirection.Backward, true);
    private ITextPointer Move(ITextPointer position, LogicalDirection direction, bool backspace)
    {
        RequirePosition(position);
        if (direction is not LogicalDirection.Forward and not LogicalDirection.Backward) throw new ArgumentOutOfRangeException(nameof(direction));
        if (ChildAt(position) is { } child)
        {
            var moved = child.View.Move(position, direction, backspace);
            if (moved.Offset != position.Offset) return moved;
            var source = child.Placement.Child.Source;
            return Pointer(direction == LogicalDirection.Forward ? source.End : source.Start, direction);
        }
        // Probe only anchor topology before generic object/paragraph fallback.
        // Never substitute source insertion steps for ordinary shaped text.
        if (AnchorViews.Count != 0 && position.GetNextInsertionPosition(direction) is { } adjacent &&
            ChildAt(adjacent) is { } adjacentChild)
            return adjacentChild.View.Pointer(direction == LogicalDirection.Forward
                ? adjacentChild.Placement.Child.Source.Anchor.ContentStart.Offset
                : adjacentChild.Placement.Child.Source.Anchor.ContentEnd.Offset, direction);
        int objectIndex = ObjectAt(position);
        if (objectIndex >= 0)
        {
            var embedded = Layout.Objects[objectIndex];
            if (direction == LogicalDirection.Forward && position.Offset < embedded.ContentEnd)
                return Pointer(embedded.ContentEnd, direction);
            if (direction == LogicalDirection.Backward && position.Offset > embedded.ContentStart)
                return Pointer(embedded.ContentStart, direction);
            return AdjacentItemBoundary(position, direction);
        }
        if (FirstLine == EndLine) return position;
        int index = FindLine(position);
        var entry = Layout.Lines[index];
        CharacterHit hit = new(Math.Clamp(position.Offset, entry.Start, ContentEnd(index)), 0);
        CharacterHit next = backspace ? entry.Line.GetBackspaceCaretCharacterHit(hit) : direction == LogicalDirection.Forward
            ? entry.Line.GetNextCaretCharacterHit(hit) : entry.Line.GetPreviousCaretCharacterHit(hit);
        int offset = next.FirstCharacterIndex + next.TrailingLength;
        if (page == null && Layout.Objects.Count != 0 &&
            (direction == LogicalDirection.Forward ? offset <= position.Offset : offset >= position.Offset))
            return AdjacentItemBoundary(position, direction);
        if (direction == LogicalDirection.Forward && offset <= position.Offset && index + 1 < EndLine)
            offset = Layout.Lines[index + 1].Start;
        else if (direction == LogicalDirection.Backward && offset >= position.Offset && index > FirstLine)
            offset = ContentEnd(index - 1);
        var candidate = Pointer(offset, direction);
        var anchors = AnchorViews;
        for (int i = 0; i < anchors.Count; ++i)
        {
            var anchor = anchors[direction == LogicalDirection.Forward ? i : anchors.Count - 1 - i];
            var source = anchor.Placement.Child.Source;
            if (direction == LogicalDirection.Forward
                ? source.Start >= position.Offset && source.Start < candidate.Offset
                : source.End <= position.Offset && source.End > candidate.Offset)
                return anchor.View.Pointer(direction == LogicalDirection.Forward
                    ? source.Anchor.ContentStart.Offset : source.Anchor.ContentEnd.Offset, direction);
        }
        return candidate;
    }

    internal override TextSegment GetLineRange(ITextPointer position)
    {
        RequirePosition(position);
        if (ChildAt(position) is { } child) return child.View.GetLineRange(position);
        int objectIndex = ObjectAt(position);
        if (objectIndex >= 0)
        {
            var embedded = Layout.Objects[objectIndex];
            return new(TextContainer.CreatePointerAtOffset(embedded.ContentStart, LogicalDirection.Forward),
                TextContainer.CreatePointerAtOffset(embedded.ContentEnd, LogicalDirection.Backward), true);
        }
        if (FirstLine == EndLine) return new(TextContainer.Start, TextContainer.End, true);
        int index = FindLine(position);
        return new(TextContainer.CreatePointerAtOffset(Layout.Lines[index].Start, LogicalDirection.Forward),
            TextContainer.CreatePointerAtOffset(ContentEnd(index), LogicalDirection.Backward), true);
    }

    internal override ReadOnlyCollection<GlyphRun> GetGlyphRuns(ITextPointer start, ITextPointer end)
    {
        RequireRange(start, end);
        if (end.Offset < start.Offset) throw new ArgumentException("A document range must be ordered.");
        var result = new List<GlyphRun>();
        CollectGlyphRuns(start.Offset, end.Offset, result, AnchorViews.Count == 0 ? null : new HashSet<GlyphRun>());
        return result.AsReadOnly();
    }

    private void CollectGlyphRuns(int start, int end, List<GlyphRun> result, HashSet<GlyphRun> seen)
    {
        if (end <= start) return;
        void AddOwn(int from, int to)
        {
            if (to <= from || FirstLine == EndLine) return;
            for (int index = FindLine(from, LogicalDirection.Forward); index <= FindLine(to, LogicalDirection.Backward); ++index)
                foreach (IndexedGlyphRun run in Layout.Lines[index].Line.GetIndexedGlyphRuns())
                    if (run.TextSourceCharacterIndex < to && run.TextSourceCharacterIndex + run.TextSourceLength > from &&
                        (seen == null || seen.Add(run.GlyphRun)))
                        result.Add(run.GlyphRun);
        }
        int cursor = start;
        foreach (var child in AnchorViews)
        {
            var source = child.Placement.Child.Source;
            if (source.End <= cursor) continue;
            if (source.Start >= end) break;
            AddOwn(cursor, Math.Min(end, source.Start));
            child.View.CollectGlyphRuns(Math.Max(cursor, source.Start), Math.Min(end, source.End), result, seen);
            cursor = Math.Max(cursor, source.End);
            if (cursor >= end) break;
        }
        AddOwn(cursor, end);
    }

    internal override void BringPositionIntoViewAsync(ITextPointer position, object userState)
    {
        RequirePosition(position);
        if (page != null) { base.BringPositionIntoViewAsync(position, userState); return; }
        BringRectIntoViewMinimally(this, GetRectangleFromTextPosition(position));
        // Scrolling invalidates arrange; do not query stale geometry again while
        // reporting completion of this already-resolved source-position request.
        OnBringPositionIntoViewCompleted(new(position, true, null, false, userState));
    }

    private void RequireRange(ITextPointer start, ITextPointer end)
    {
        RequireValid();
        if (start == null || end == null || !ReferenceEquals(start.TextContainer, TextContainer) ||
            !ReferenceEquals(end.TextContainer, TextContainer))
            throw new ArgumentException("Range belongs to another document.");
        if (end.Offset < start.Offset) throw new ArgumentException("A document range must be ordered.");
    }
}
