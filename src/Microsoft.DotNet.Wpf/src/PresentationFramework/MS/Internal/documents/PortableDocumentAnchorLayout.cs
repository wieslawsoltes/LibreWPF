// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MS.Internal.PtsHost;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Documents;

// Generation-owned original child layouts. Source policy is resolved once; native
// width policy is invoked twice for the whole paragraph, not once per anchor.
internal sealed class PortableDocumentAnchorLayout : IDisposable
{
    internal sealed record Entry(PortableDocumentAnchorSource Source, MbpInfo Box,
        PortableFlowDocumentLayout Layout, Size OuterSize);
    private IReadOnlyList<Entry> _entries;
    private PortableDocumentAnchorLayout(Entry[] entries) => _entries = Array.AsReadOnly(entries);
    internal IReadOnlyList<Entry> Entries => _entries ?? throw new ObjectDisposedException(nameof(PortableDocumentAnchorLayout));

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
        if (sources.Count == 0) return new(Array.Empty<Entry>());
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
            return new(entries);
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
