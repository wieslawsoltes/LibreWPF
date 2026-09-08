// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace MS.Internal.Documents;

// Real source drawing/content host. It is neither a PTS PageVisual nor a
// synthetic WPF root; native MIL consumes its normal typed source render data.
internal sealed class PortableFlowDocumentVisual(FlowDocumentView owner) : DrawingVisual, IContentHost
{
    private PortableFlowDocumentLayout _drawn;
    internal void Draw(PortableFlowDocumentLayout layout)
    {
        if (ReferenceEquals(layout, _drawn)) return;
        using (DrawingContext drawing = RenderOpen())
        {
            drawing.DrawRectangle(owner.Document.Background ?? Brushes.Transparent, null, new Rect(layout.Size));
            for (int index = 1; index < layout.Blocks.Count; ++index)
                DrawBlock(drawing, layout.Blocks[index], layout.Boxes[index]);
            for (int index = 0; index < layout.Lines.Count; ++index)
            {
                var position = layout.Positions[index];
                layout.Lines[index].Line.Draw(drawing, new Point(position.X, position.Y), InvertAxes.None);
            }
            foreach (var marker in layout.Markers)
            {
                var target = layout.Lines[marker.TargetLine]; var position = layout.Positions[marker.TargetLine];
                marker.Line.Draw(drawing, new Point(position.X + target.Line.Start + marker.Offset - marker.Line.Width,
                    position.Y + target.Line.Baseline - marker.Line.Baseline), InvertAxes.None);
            }
        }
        _drawn = layout;
    }

    internal static void DrawBlock(DrawingContext drawing, PortableFlowDocumentLayout.BlockEntry entry,
        ProGPU.Wpf.Interop.PortableDocumentBox content)
    {
        Thickness border = entry.Box.Border, padding = entry.Box.Padding;
        Rect outer = new(content.X - padding.Left - border.Left, content.Y - padding.Top - border.Top,
            content.Width + padding.Left + padding.Right + border.Left + border.Right,
            content.Height + padding.Top + padding.Bottom + border.Top + border.Bottom);
        if (entry.Element.Background != null) drawing.DrawRectangle(entry.Element.Background, null, outer);
        if (entry.Box.BorderBrush != null && border != new Thickness())
        {
            var ring = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var context = ring.Open())
            {
                PortableFlowDocumentTextView.AddRectangle(context, outer);
                Rect inner = new(outer.X + border.Left, outer.Y + border.Top,
                    Math.Max(0, outer.Width - border.Left - border.Right), Math.Max(0, outer.Height - border.Top - border.Bottom));
                if (inner.Width > 0 && inner.Height > 0) PortableFlowDocumentTextView.AddRectangle(context, inner);
            }
            ring.Freeze();
            drawing.DrawGeometry(entry.Box.BorderBrush, null, ring);
        }
    }

    internal void Clear()
    {
        using (RenderOpen()) { }
        _drawn = null;
    }

    IInputElement IContentHost.InputHitTest(Point point)
    {
        if (owner.Document == null || !owner.PortableTextView.IsValid) return null;
        // Visual-local points already include the inverse scroll translation.
        ITextPointer hit = owner.PortableTextView.GetTextPositionFromPoint(point - owner.PortableScrollOffset, false);
        return hit == null ? null : hit.CreateStaticPointer().Parent as IInputElement;
    }

    ReadOnlyCollection<Rect> IContentHost.GetRectangles(ContentElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (owner.Document == null) return ReadOnlyCollection<Rect>.Empty;
        if (ReferenceEquals(child, owner.Document))
            return owner.PortableLayoutValid ? new(new[] { new Rect(owner.PortableLayout.Size) }) : ReadOnlyCollection<Rect>.Empty;
        if (child is not TextElement element || !ReferenceEquals(element.TextContainer, owner.Document.TextContainer))
            throw new ArgumentException("Content does not belong to this document.", nameof(child));
        if (!owner.PortableTextView.IsValid) return ReadOnlyCollection<Rect>.Empty;
        return owner.PortableTextView.GetDocumentRectangles(element.ContentStart, element.ContentEnd, false);
    }

    IEnumerator<IInputElement> IContentHost.HostedElements => EnumerateHostedElements().GetEnumerator();
    private IEnumerable<IInputElement> EnumerateHostedElements()
    {
        var document = owner.Document;
        if (document == null) yield break;
        ITextPointer position = document.TextContainer.Start.CreatePointer();
        while (position.CompareTo(document.TextContainer.End) < 0)
        {
            if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart &&
                position.GetAdjacentElement(LogicalDirection.Forward) is IInputElement element) yield return element;
            if (!position.MoveToNextContextPosition(LogicalDirection.Forward)) yield break;
        }
    }

    void IContentHost.OnChildDesiredSizeChanged(UIElement child)
        => throw new PlatformNotSupportedException("Portable document embedded UI elements require the inline/block object contract.");
}
