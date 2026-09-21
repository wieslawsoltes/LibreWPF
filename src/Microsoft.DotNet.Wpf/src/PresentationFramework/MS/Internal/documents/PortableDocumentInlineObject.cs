// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace MS.Internal.Documents;

// Source measurement adapter only. Native text owns placement; the document's
// retained content visual borrows the actual child without changing its owner.
internal sealed class PortableDocumentInlineObject(InlineUIContainer container, UIElement child,
    TextRunProperties properties, double paragraphWidth) : TextEmbeddedObject
{
    internal InlineUIContainer Container { get; } = container;
    internal UIElement Child { get; } = child;
    private TextEmbeddedObjectMetrics _metrics;
    public override CharacterBufferReference CharacterBufferReference => new(string.Empty, 0);
    public override int Length => 1;
    public override TextRunProperties Properties => properties;
    public override bool HasFixedSize => true;
    public override LineBreakCondition BreakBefore => LineBreakCondition.BreakDesired;
    public override LineBreakCondition BreakAfter => LineBreakCondition.BreakDesired;

    public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
    {
        if (!double.IsFinite(paragraphWidth) || paragraphWidth <= 0)
            throw new PlatformNotSupportedException("Document inline controls require a resolved positive native paragraph width.");
        if (!ReferenceEquals(Container.Child, Child))
            throw new InvalidOperationException("The source inline child changed while formatting.");
        // As for TextBlock inline children, measure against the whole resolved
        // paragraph constraint, not the remaining width at one line position.
        Child.Measure(new Size(paragraphWidth, double.PositiveInfinity));
        Size desired = Child.DesiredSize;
        double baseline = (double)Child.GetValue(TextBlock.BaselineOffsetProperty);
        if (double.IsNaN(baseline)) baseline = desired.Height;
        return _metrics = new(desired.Width, desired.Height, baseline);
    }

    public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways)
        => Child.IsArrangeValid && _metrics != null ? new(0, -_metrics.Baseline, _metrics.Width, _metrics.Height) : Rect.Empty;

    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
    {
        // The actual UIElement draws through PortableFlowDocumentVisual.Children.
    }
}
