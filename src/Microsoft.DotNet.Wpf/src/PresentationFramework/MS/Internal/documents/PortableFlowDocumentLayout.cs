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
using ProGPU.Wpf.Interop;
using DocumentList = System.Windows.Documents.List;
using Section = System.Windows.Documents.Section;

namespace MS.Internal.Documents;

// Immutable source layout generation. WPF owns properties, text positions and
// TextLines; ProGPU owns paragraph shaping and all block-placement arithmetic.
internal sealed class PortableFlowDocumentLayout : IDisposable
{
    internal sealed record BlockEntry(TextElement Element, MbpInfo Box, DocumentList MarkerList, int MarkerIndex);
    internal sealed record LineEntry(Paragraph Paragraph, TextLine Line, int Start, int BlockIndex, double Advance);
    internal sealed record MarkerEntry(ListItem Item, TextLine Line, int TargetLine, double Offset);

    private readonly List<BlockEntry> _entries = new();
    private readonly List<PortableDocumentBlock> _blocks = new();
    private readonly List<LineEntry> _lines = new();
    private readonly List<MarkerEntry> _markers = new();
    private bool _disposed;
    internal IReadOnlyList<BlockEntry> Blocks => _entries;
    internal IReadOnlyList<LineEntry> Lines => _lines;
    internal IReadOnlyList<MarkerEntry> Markers => _markers;
    internal PortableDocumentBox[] Boxes { get; private set; }
    internal PortableDocumentLinePosition[] Positions { get; private set; }
    internal Size Size { get; private set; }

    internal static PortableFlowDocumentLayout Create(FlowDocument document, double pageWidth, double pixelsPerDip,
        TextFormattingMode formattingMode)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (PortableWpfRuntime.GetMediaBackendAndFreeze() != PortableWpfMediaBackend.Portable)
            throw new InvalidOperationException("Portable document layout requires portable media ownership.");
        if (!double.IsFinite(pageWidth) || pageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pageWidth));
        if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerDip));
        if (!PortableWpfServiceRegistry.TryGetDocumentFlow(out var flow) ||
            !PortableWpfServiceRegistry.TryGetTextFormatting(out _))
            throw new PlatformNotSupportedException("Portable FlowDocument requires registered native document and text services before layout.");
        if (document.FlowDirection != FlowDirection.LeftToRight)
            throw new PlatformNotSupportedException("Portable RTL document block ordering is not implemented.");
        var layout = new PortableFlowDocumentLayout();
        try
        {
            Thickness padding = document.ComputePageMargin();
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
            foreach (Block block in document.Blocks) layout.AddBlock(block, 0, 1, pixelsPerDip);
            layout.CloseSubtree(0);
            layout.Boxes = new PortableDocumentBox[layout._blocks.Count];
            flow.ResolveWidths(CollectionsMarshal.AsSpan(layout._blocks), pageWidth, layout.Boxes);
            TextFormatter formatter = TextFormatter.FromCurrentDispatcher(formattingMode);
            var metrics = new List<PortableDocumentLine>();
            for (int i = 0; i < layout._entries.Count; ++i)
            {
                PortableDocumentBlock descriptor = layout._blocks[i];
                descriptor.LineStart = checked((uint)layout._lines.Count);
                if (layout._entries[i].Element is Paragraph paragraph)
                {
                    if (paragraph.TextIndent != 0)
                        throw new PlatformNotSupportedException("Portable document first-line indentation requires native per-line width constraints.");
                    if (paragraph.IsHyphenationEnabled)
                        throw new PlatformNotSupportedException("Portable document hyphenation requires the native discretionary-break contract.");
                    if (layout.Boxes[i].Width <= 0)
                        throw new PlatformNotSupportedException("Exhausted document width requires zero-width wrapping, not unbounded paragraph formatting.");
                    layout.FormatParagraph(document, paragraph, i, pixelsPerDip, formatter, metrics);
                }
                descriptor.LineCount = checked((uint)layout._lines.Count - descriptor.LineStart);
                layout._blocks[i] = descriptor;
            }
            layout.Positions = new PortableDocumentLinePosition[layout._lines.Count];
            PortableDocumentExtent extent = flow.Arrange(CollectionsMarshal.AsSpan(layout._blocks), pageWidth,
                CollectionsMarshal.AsSpan(metrics), layout.Boxes, layout.Positions);
            layout.Size = new(extent.Width, extent.Height);
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
        DocumentList markerList = null, int markerIndex = 0)
    {
        if (depth >= 128 || _entries.Count >= 1 << 20)
            throw new PlatformNotSupportedException("Portable document block budget exceeded.");
        if (element is not Paragraph and not Section and not DocumentList and not ListItem)
            throw new PlatformNotSupportedException("Portable document tables and embedded blocks require their native layout contracts.");
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

    private void FormatParagraph(FlowDocument document, Paragraph paragraph, int blockIndex, double pixelsPerDip,
        TextFormatter formatter, List<PortableDocumentLine> metrics)
    {
        var source = new PortableDocumentParagraphSource(paragraph, pixelsPerDip);
        var properties = new LineProperties(paragraph, document,
            new TextProperties(paragraph, paragraph.StaticElementStart, false, false, pixelsPerDip), null);
        var cache = new TextRunCache();
        TextLineBreak continuation = null;
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
                    metrics.Add(new() { Width = line.Start + line.WidthIncludingTrailingWhitespace, Height = advance });
                    _lines.Add(new(paragraph, line, position, blockIndex, advance));
                }
                catch { line.Dispose(); throw; }
                TextLineBreak next = line.GetTextLineBreak();
                continuation?.Dispose(); continuation = next;
                position += line.Length;
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
    }
}
