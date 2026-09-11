// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Documents;

// A real DocumentPage consumed by the existing DocumentPageView/TextView wrapper.
// Borrows one immutable source generation; only the paginator disposes its lines.
internal sealed class PortableFlowDocumentPage : DocumentPage, IServiceProvider
{
    internal PortableFlowDocumentPaginator Owner { get; }
    internal PortableFlowDocumentLayout Layout { get; }
    internal int Number { get; }
    internal int FirstLine { get; }
    internal int EndLine { get; }
    internal bool IsDisposed { get; private set; }
    internal bool IsValid => !IsDisposed && Owner.LayoutValid && ReferenceEquals(Layout, Owner.Layout);
    internal PortableFlowDocumentTextView TextView { get; }
    internal TextSegment TextSegment => new(
        Owner.Document.TextContainer.CreatePointerAtOffset(Owner.PageStartOffset(Number), LogicalDirection.Forward),
        Owner.Document.TextContainer.CreatePointerAtOffset(Owner.PageStartOffset(Number + 1), LogicalDirection.Backward), true);
    internal UIElement RenderScope
    {
        get
        {
            Visual current = Visual;
            while (current != null && current is not UIElement) current = VisualTreeHelper.GetParent(current) as Visual;
            return current as UIElement;
        }
    }
    private readonly int[] _columnStarts;
    private readonly uint[] _columns;
    internal PortableFlowDocumentPage(PortableFlowDocumentPaginator owner, int number) : base(null)
    {
        Owner = owner; Number = number; Layout = owner.Layout;
        FirstLine = owner.PageStarts[number]; EndLine = owner.PageStarts[number + 1];
        var starts = new List<int>(); var columns = new List<uint>();
        for (int index = FirstLine; index < EndLine; ++index)
            if (index == FirstLine || owner.Positions[index].Column != owner.Positions[index - 1].Column)
            { starts.Add(index); columns.Add(owner.Positions[index].Column); }
        starts.Add(EndLine); _columnStarts = starts.ToArray(); _columns = columns.ToArray();
        SetSize(owner.ActualPageSize); SetBleedBox(new Rect(Size));
        SetContentBox(new(owner.Padding.Left, owner.Padding.Top,
            Size.Width - owner.Padding.Left - owner.Padding.Right, Size.Height - owner.Padding.Top - owner.Padding.Bottom));
        TextView = new(null, owner.Document, this);
        var visual = new PageVisual(this);
        SetVisual(visual);
        Draw(visual);
    }
    internal PortableDocumentLinePosition Position(int index)
    {
        var position = Owner.Positions[index];
        return new() { X = Owner.Padding.Left + position.Column * (Owner.ColumnWidth + Owner.ColumnGap) + Layout.Positions[index].X,
            Y = Owner.Padding.Top + position.Y };
    }
    internal double ColumnOffset(int index) => Owner.Positions[index].Column * (Owner.ColumnWidth + Owner.ColumnGap);
    internal bool Contains(ITextPointer position)
    {
        int start = Owner.PageStartOffset(Number), end = Owner.PageStartOffset(Number + 1);
        return position.Offset >= start && position.Offset <= end &&
            (position.Offset != start || Number == 0 || position.LogicalDirection == LogicalDirection.Forward) &&
            (position.Offset != end || Number + 1 == Owner.PageCount || position.LogicalDirection == LogicalDirection.Backward);
    }
    internal void PointLineRange(Point point, out int first, out int end)
    {
        int nearest = 0; double distance = double.PositiveInfinity;
        for (int i = 0; i < _columns.Length; ++i)
        {
            double x = Owner.Padding.Left + _columns[i] * (Owner.ColumnWidth + Owner.ColumnGap);
            double dx = point.X < x ? x - point.X : Math.Max(0, point.X - x - Owner.ColumnWidth);
            if (dx < distance) { distance = dx; nearest = i; }
        }
        first = _columnStarts[nearest]; end = _columnStarts[nearest + 1];
    }

    private void Draw(PageVisual visual)
    {
        using var drawing = visual.RenderOpen();
        drawing.DrawRectangle(Owner.Document.Background ?? Brushes.Transparent, null, new Rect(Size));
        var blocks = Layout.BlockDescriptors;
        for (int fragment = 0; fragment < _columns.Length; ++fragment)
        {
            int first = _columnStarts[fragment], end = _columnStarts[fragment + 1];
            var origin = Position(first); var source = Layout.Positions[first];
            var clip = new RectangleGeometry(new(Owner.Padding.Left + _columns[fragment] * (Owner.ColumnWidth + Owner.ColumnGap),
                ContentBox.Top, Owner.ColumnWidth, ContentBox.Height)); clip.Freeze();
            drawing.PushClip(clip);
            var transform = new TranslateTransform(origin.X - source.X, origin.Y - source.Y); transform.Freeze();
            drawing.PushTransform(transform);
            // Collect only this fragment's source paragraphs and ancestors.
            // Preorder index sorting preserves parent-before-child paint order.
            var visited = new HashSet<int>(); var visibleBlocks = new List<int>();
            int lastBlock = -1;
            for (int line = first; line < end; ++line)
            {
                int index = Layout.Lines[line].BlockIndex;
                if (index == lastBlock) continue;
                lastBlock = index;
                while (index != 0 && visited.Add(index))
                { visibleBlocks.Add(index); index = checked((int)blocks[index].ParentIndex); }
            }
            visibleBlocks.Sort();
            foreach (int index in visibleBlocks) PortableFlowDocumentVisual.DrawBlock(drawing, Layout.Blocks[index], Layout.Boxes[index]);
            for (int index = first; index < end; ++index)
            {
                var p = Layout.Positions[index];
                Layout.Lines[index].Line.Draw(drawing, new Point(p.X, p.Y), InvertAxes.None);
            }
            int low = 0, high = Layout.Markers.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (Layout.Markers[middle].TargetLine < first) low = middle + 1; else high = middle;
            }
            for (int index = low; index < Layout.Markers.Count && Layout.Markers[index].TargetLine < end; ++index)
            {
                var marker = Layout.Markers[index];
                var target = Layout.Lines[marker.TargetLine]; var p = Layout.Positions[marker.TargetLine];
                marker.Line.Draw(drawing, new Point(p.X + target.Line.Start + marker.Offset - marker.Line.Width,
                    p.Y + target.Line.Baseline - marker.Line.Baseline), InvertAxes.None);
            }
            drawing.Pop(); drawing.Pop();
        }
        if (Owner.Document.ColumnRuleBrush != null && Owner.Document.ColumnRuleWidth > 0)
            for (int column = 1; column < Owner.Columns; ++column)
            {
                double x = Owner.Padding.Left + column * (Owner.ColumnWidth + Owner.ColumnGap) - Owner.ColumnGap * 0.5;
                drawing.DrawRectangle(Owner.Document.ColumnRuleBrush, null,
                    new Rect(x - Owner.Document.ColumnRuleWidth * 0.5, ContentBox.Top, Owner.Document.ColumnRuleWidth, ContentBox.Height));
            }
    }
    object IServiceProvider.GetService(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type == typeof(ITextView) ? TextView : null;
    }
    public override void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (Visual is DrawingVisual drawing) using (drawing.RenderOpen()) { }
        base.Dispose();
    }

    private sealed class PageVisual(PortableFlowDocumentPage page) : DrawingVisual, IContentHost
    {
        IInputElement IContentHost.InputHitTest(Point point)
        {
            if (!page.IsValid) return null;
            return page.TextView.GetTextPositionFromPoint(point, false)?.CreateStaticPointer().Parent as IInputElement;
        }
        ReadOnlyCollection<Rect> IContentHost.GetRectangles(ContentElement child)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!page.IsValid) return ReadOnlyCollection<Rect>.Empty;
            if (ReferenceEquals(child, page.Owner.Document)) return new(new[] { page.ContentBox });
            if (child is not TextElement element || !ReferenceEquals(element.TextContainer, page.Owner.Document.TextContainer))
                throw new ArgumentException("Content belongs to another document.", nameof(child));
            return page.TextView.GetDocumentRectangles(element.ContentStart, element.ContentEnd, false);
        }
        IEnumerator<IInputElement> IContentHost.HostedElements => Elements().GetEnumerator();
        private IEnumerable<IInputElement> Elements()
        {
            if (!page.IsValid) yield break;
            var segment = page.TextSegment;
            var position = segment.Start.CreatePointer();
            // A continuation can begin inside a Run/Hyperlink/Paragraph. Its
            // already-open source ancestors are hosted here too, even though
            // their ElementStart edges belong to an earlier page.
            var ancestors = new Stack<IInputElement>();
            for (DependencyObject parent = position.CreateStaticPointer().Parent; parent is TextElement element; parent = element.Parent)
                ancestors.Push(element);
            while (ancestors.Count != 0) yield return ancestors.Pop();
            while (position.CompareTo(segment.End) < 0)
            {
                if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                    position.GetAdjacentElement(LogicalDirection.Forward) is IInputElement element) yield return element;
                if (!position.MoveToNextContextPosition(LogicalDirection.Forward)) yield break;
            }
        }
        void IContentHost.OnChildDesiredSizeChanged(UIElement child)
            => throw new PlatformNotSupportedException("Paginated embedded objects require their native metrics contract.");
    }
}
