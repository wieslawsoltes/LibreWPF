// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MS.Internal.PtsHost;
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Documents;

// Generation-owned original child layouts. Source policy is resolved once; native
// width policy is invoked twice for the whole paragraph, not once per anchor.
internal sealed class PortableDocumentAnchorLayout : IDisposable
{
    internal sealed record Entry(PortableDocumentAnchorSource Source, MbpInfo Box,
        PortableFlowDocumentLayout Layout, Size OuterSize);
    internal sealed record Placement(Entry Child, Rect OuterBounds, Point ContentOrigin);
    internal sealed record PlacedBatch(IReadOnlyList<Placement> Children, PortableTextExclusionRequest Exclusions);
    private IReadOnlyList<Entry> _entries;
    private readonly Paragraph _paragraph;
    private readonly uint _generation;
    private readonly IPortableAnchoredDocumentFlow _flow;
    private PortableDocumentAnchorLayout(Paragraph paragraph, uint generation, Entry[] entries, IPortableAnchoredDocumentFlow flow)
    { _paragraph = paragraph; _generation = generation; _entries = Array.AsReadOnly(entries); _flow = flow; }
    internal IReadOnlyList<Entry> Entries => _entries ?? throw new ObjectDisposedException(nameof(PortableDocumentAnchorLayout));

    internal void ValidateFor(Paragraph paragraph)
    {
        paragraph.Dispatcher.VerifyAccess();
        _ = Entries;
        if (!ReferenceEquals(paragraph, _paragraph) || paragraph.TextContainer.Generation != _generation)
            throw new InvalidOperationException("Anchored content does not belong to the current paragraph generation.");
    }

    // Only the parent's text stream skips these ranges. Their real content stays
    // in the owned child layouts and must be routed there for drawing/input.
    internal bool TryGetSourceRange(int position, out int start, out int end)
    {
        ValidateFor(_paragraph);
        int low = 0, high = _entries.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_entries[middle].Source.Start <= position) low = middle + 1;
            else high = middle;
        }
        if (low > 0 && position < _entries[low - 1].Source.End)
        {
            var source = _entries[low - 1].Source;
            start = source.Start; end = source.End; return true;
        }
        start = end = 0; return false;
    }

    // Reference frames are paragraph-local and source-resolved in anchor order.
    // In particular, the caller supplies a Floater's actual line top; this method
    // must not substitute the Figure ParagraphTop default or a viewport guess.
    internal PlacedBatch Place(IReadOnlyList<PortableDocumentAnchorRectangle> frames, uint maximumAttempts)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ValidateFor(_paragraph);
        if (frames.Count != _entries.Count) throw new ArgumentException("Each source anchor requires one reference frame.", nameof(frames));
        if (maximumAttempts == 0 || maximumAttempts > 1U << 20)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        var requests = new PortableDocumentAnchorRequest[_entries.Count];
        var results = new PortableDocumentAnchorRectangle[_entries.Count];
        for (int i = 0; i < requests.Length; ++i)
        {
            var child = _entries[i]; var frame = frames[i];
            PortableDocumentAnchorAlignment alignment;
            bool delay = true;
            if (child.Source.Anchor is Figure figure)
            {
                if (figure.WrapDirection != WrapDirection.Both || !figure.Height.IsAuto ||
                    figure.HorizontalOffset != 0 || figure.VerticalOffset != 0)
                    throw new PlatformNotSupportedException("Figure wrap sides, fixed height and offsets require their source placement policy.");
                alignment = figure.HorizontalAnchor switch
                {
                    FigureHorizontalAnchor.ColumnLeft or FigureHorizontalAnchor.ContentLeft or FigureHorizontalAnchor.PageLeft => PortableDocumentAnchorAlignment.Left,
                    FigureHorizontalAnchor.ColumnCenter or FigureHorizontalAnchor.ContentCenter or FigureHorizontalAnchor.PageCenter => PortableDocumentAnchorAlignment.Center,
                    FigureHorizontalAnchor.ColumnRight or FigureHorizontalAnchor.ContentRight or FigureHorizontalAnchor.PageRight => PortableDocumentAnchorAlignment.Right,
                    _ => throw new PlatformNotSupportedException("Unknown Figure horizontal reference.")
                };
                delay = figure.CanDelayPlacement;
            }
            else if (child.Source.Anchor is Floater floater)
                alignment = floater.HorizontalAlignment switch
                {
                    HorizontalAlignment.Left or HorizontalAlignment.Stretch => PortableDocumentAnchorAlignment.Left,
                    HorizontalAlignment.Center => PortableDocumentAnchorAlignment.Center,
                    HorizontalAlignment.Right => PortableDocumentAnchorAlignment.Right,
                    _ => throw new PlatformNotSupportedException("Unknown Floater horizontal alignment.")
                };
            else throw new PlatformNotSupportedException("Unknown source anchor.");
            requests[i] = new() { Left = frame.Left, Top = frame.Top, Right = frame.Right, Bottom = frame.Bottom,
                Width = (float)child.OuterSize.Width, Height = (float)child.OuterSize.Height,
                Alignment = alignment, AllowDelay = delay ? 1U : 0U, MaximumAttempts = maximumAttempts };
        }
        _flow.PlaceAnchors(requests, ReadOnlySpan<PortableDocumentAnchorRectangle>.Empty, results);
        ValidateFor(_paragraph);
        var placements = new Placement[results.Length];
        var exclusions = new PortableTextExclusion[results.Length];
        for (int i = 0; i < results.Length; ++i)
        {
            var rectangle = results[i]; var child = _entries[i]; var box = child.Box;
            placements[i] = new(child, new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top),
                new(rectangle.Left + box.Margin.Left + box.Border.Left + box.Padding.Left,
                    rectangle.Top + box.Margin.Top + box.Border.Top + box.Padding.Top));
            exclusions[i] = new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        }
        return new(Array.AsReadOnly(placements), new(new PortableTextExclusionOptions(maximumAttempts), exclusions));
    }

    internal static PortableDocumentAnchorLayout Create(FlowDocument document, Paragraph paragraph,
        double availableWidth, double pixelsPerDip, TextFormattingMode formattingMode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);
        paragraph.Dispatcher.VerifyAccess();
        if (!ReferenceEquals(paragraph.TextContainer, document.TextContainer))
            throw new InvalidOperationException("Anchors must retain the original source document.");
        if (!double.IsFinite(availableWidth) || availableWidth <= 0 || availableWidth > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        if (PortableWpfRuntime.GetMediaBackendAndFreeze() != PortableWpfMediaBackend.Portable ||
            !PortableWpfServiceRegistry.TryGetDocumentFlow(out var flow) || flow is not IPortableAnchoredDocumentFlow sizing)
            throw new PlatformNotSupportedException("Automatic anchors require the native document sizing service.");
        uint generation = document.TextContainer.Generation;
        var sources = PortableDocumentAnchorSource.Collect(paragraph);
        if (sources.Count == 0) return new(paragraph, generation, Array.Empty<Entry>(), sizing);
        var requests = new PortableDocumentAnchorWidthRequest[sources.Count];
        var widths = new PortableDocumentAnchorWidthResult[sources.Count];
        var boxes = new MbpInfo[sources.Count];
        var layouts = new PortableFlowDocumentLayout[sources.Count];
        // Validate every policy before formatting any child. Non-auto FigureLength
        // needs its parent's actual reference frames, not this local width.
        for (int i = 0; i < sources.Count; ++i)
        {
            AnchoredBlock anchor = sources[i].Anchor;
            if (anchor is Figure figure && !figure.Width.IsAuto ||
                anchor is Floater floater && !double.IsNaN(floater.Width))
                throw new PlatformNotSupportedException("Non-auto anchor widths require source reference-frame resolution.");
            var box = boxes[i] = MbpInfo.FromElement(anchor, pixelsPerDip);
            double horizontal = box.Margin.Left + box.Border.Left + box.Padding.Left +
                box.Margin.Right + box.Border.Right + box.Padding.Right;
            requests[i] = new() { AvailableWidth = (float)availableWidth, HorizontalInsets = (float)horizontal,
                Mode = anchor is Floater { HorizontalAlignment: HorizontalAlignment.Stretch }
                    ? PortableDocumentAnchorWidthMode.Fill : PortableDocumentAnchorWidthMode.FitContent };
        }
        sizing.ResolveAnchorWidths(requests, widths);
        try
        {
            for (int i = 0; i < sources.Count; ++i)
            {
                layouts[i] = PortableFlowDocumentLayout.CreateAnchored(document, sources[i].Anchor,
                    widths[i].ContentWidth, pixelsPerDip, formattingMode);
                requests[i].MeasuredWidth = (float)layouts[i].MeasuredContentWidth.Value;
                requests[i].HasMeasurement = 1;
            }
            sizing.ResolveAnchorWidths(requests, widths);
            for (int i = 0; i < sources.Count; ++i)
                if (widths[i].RequiresRemeasure != 0)
                {
                    var previous = layouts[i];
                    layouts[i] = PortableFlowDocumentLayout.CreateAnchored(document, sources[i].Anchor,
                        widths[i].ContentWidth, pixelsPerDip, formattingMode);
                    previous.Dispose();
                }
            if (generation != document.TextContainer.Generation)
                throw new InvalidOperationException("Source changed during anchored child measurement.");
            var entries = new Entry[sources.Count];
            for (int i = 0; i < entries.Length; ++i)
            {
                var box = boxes[i];
                double vertical = box.Margin.Top + box.Border.Top + box.Padding.Top +
                    box.Margin.Bottom + box.Border.Bottom + box.Padding.Bottom;
                entries[i] = new(sources[i], box, layouts[i], new(widths[i].OuterWidth, layouts[i].Size.Height + vertical));
            }
            return new(paragraph, generation, entries, sizing);
        }
        catch
        {
            foreach (var layout in layouts) layout?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_entries == null) return;
        var entries = _entries;
        _entries = null;
        foreach (var entry in entries) entry.Layout.Dispose();
    }
}
