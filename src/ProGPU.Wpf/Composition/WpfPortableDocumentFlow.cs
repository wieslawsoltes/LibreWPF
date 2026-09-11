using System.Runtime.InteropServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Typed zero-copy adapter only; block layout remains in ProGPU C++.</summary>
internal sealed class WpfPortableDocumentFlow : IPortableAnchoredDocumentFlow, IPortableMeasuredDocumentFlow
{
    private static readonly WpfPortableDocumentFlow Default = new();
    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureDocumentFlow(Default);

    public PortableDocumentExtent ArrangeWithContentMeasurement(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
        ReadOnlySpan<PortableDocumentLine> lines, ReadOnlySpan<PortableDocumentObject> objects,
        ReadOnlySpan<PortableDocumentRow> rows, ReadOnlySpan<double> columnWidths,
        ReadOnlySpan<PortableDocumentCell> cells, ReadOnlySpan<PortableDocumentPositionedParagraph> paragraphs,
        ReadOnlySpan<PortableDocumentLinePosition> localPositions, Span<PortableDocumentBox> boxes,
        Span<PortableDocumentLinePosition> positions, out double contentWidth)
    {
        var extent = NativeDocumentFlow.ArrangeWithContentMeasurement(
            MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentLine, NativeDocumentLine>(lines),
            MemoryMarshal.Cast<PortableDocumentObject, NativeDocumentObject>(objects),
            MemoryMarshal.Cast<PortableDocumentRow, NativeDocumentRow>(rows), columnWidths,
            MemoryMarshal.Cast<PortableDocumentCell, NativeDocumentCell>(cells),
            MemoryMarshal.Cast<PortableDocumentPositionedParagraph, NativeDocumentPositionedParagraph>(paragraphs),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(localPositions),
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(positions), out contentWidth);
        return new(extent.Width, extent.Height);
    }

    public PortableDocumentExtent ArrangeWithPositionedParagraphs(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
        ReadOnlySpan<PortableDocumentLine> lines, ReadOnlySpan<PortableDocumentObject> objects,
        ReadOnlySpan<PortableDocumentRow> rows, ReadOnlySpan<double> columnWidths,
        ReadOnlySpan<PortableDocumentCell> cells, ReadOnlySpan<PortableDocumentPositionedParagraph> paragraphs,
        ReadOnlySpan<PortableDocumentLinePosition> localPositions, Span<PortableDocumentBox> boxes,
        Span<PortableDocumentLinePosition> positions)
    {
        var extent = NativeDocumentFlow.ArrangeWithPositionedParagraphs(
            MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentLine, NativeDocumentLine>(lines),
            MemoryMarshal.Cast<PortableDocumentObject, NativeDocumentObject>(objects),
            MemoryMarshal.Cast<PortableDocumentRow, NativeDocumentRow>(rows), columnWidths,
            MemoryMarshal.Cast<PortableDocumentCell, NativeDocumentCell>(cells),
            MemoryMarshal.Cast<PortableDocumentPositionedParagraph, NativeDocumentPositionedParagraph>(paragraphs),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(localPositions),
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(positions));
        return new(extent.Width, extent.Height);
    }

    public void ResolveAnchorWidths(ReadOnlySpan<PortableDocumentAnchorWidthRequest> requests,
        Span<PortableDocumentAnchorWidthResult> results)
        => NativeDocumentFlow.ResolveAnchorWidths(
            MemoryMarshal.Cast<PortableDocumentAnchorWidthRequest, NativeDocumentAnchorWidthRequest>(requests),
            MemoryMarshal.Cast<PortableDocumentAnchorWidthResult, NativeDocumentAnchorWidthResult>(results));

    public void PlaceAnchors(ReadOnlySpan<PortableDocumentAnchorRequest> requests,
        ReadOnlySpan<PortableDocumentAnchorRectangle> exclusions, Span<PortableDocumentAnchorRectangle> results)
        => NativeDocumentFlow.PlaceAnchors(
            MemoryMarshal.Cast<PortableDocumentAnchorRequest, NativeDocumentAnchorRequest>(requests),
            MemoryMarshal.Cast<PortableDocumentAnchorRectangle, NativeDocumentAnchorRectangle>(exclusions),
            MemoryMarshal.Cast<PortableDocumentAnchorRectangle, NativeDocumentAnchorRectangle>(results));

    public PortableDocumentPagination Paginate(ReadOnlySpan<PortableDocumentFragmentLine> lines,
        double contentHeight, uint columns, Span<PortableDocumentFragmentPosition> positions)
    {
        var result = NativeDocumentFlow.Paginate(
            MemoryMarshal.Cast<PortableDocumentFragmentLine, NativeDocumentFragmentLine>(lines), contentHeight, columns,
            MemoryMarshal.Cast<PortableDocumentFragmentPosition, NativeDocumentFragmentPosition>(positions));
        return new(result.FragmentCount, result.PageCount);
    }

    public void ResolveWidths(ReadOnlySpan<PortableDocumentBlock> blocks, double width, Span<PortableDocumentBox> boxes)
        => NativeDocumentFlow.ResolveWidths(MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes));

    public void ResolveWidthsWithRows(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
        ReadOnlySpan<PortableDocumentRow> rows, ReadOnlySpan<double> columnWidths,
        ReadOnlySpan<PortableDocumentCell> cells, Span<PortableDocumentBox> boxes)
        => NativeDocumentFlow.ResolveWidthsWithRows(MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentRow, NativeDocumentRow>(rows), columnWidths,
            MemoryMarshal.Cast<PortableDocumentCell, NativeDocumentCell>(cells),
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes));

    public PortableDocumentExtent ArrangeWithRows(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
        ReadOnlySpan<PortableDocumentLine> lines, ReadOnlySpan<PortableDocumentObject> objects,
        ReadOnlySpan<PortableDocumentRow> rows, ReadOnlySpan<double> columnWidths,
        ReadOnlySpan<PortableDocumentCell> cells, Span<PortableDocumentBox> boxes,
        Span<PortableDocumentLinePosition> positions)
    {
        var extent = NativeDocumentFlow.ArrangeWithRows(MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentLine, NativeDocumentLine>(lines),
            MemoryMarshal.Cast<PortableDocumentObject, NativeDocumentObject>(objects),
            MemoryMarshal.Cast<PortableDocumentRow, NativeDocumentRow>(rows), columnWidths,
            MemoryMarshal.Cast<PortableDocumentCell, NativeDocumentCell>(cells),
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(positions));
        return new(extent.Width, extent.Height);
    }

    public PortableDocumentExtent ArrangeWithObjects(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
        ReadOnlySpan<PortableDocumentLine> lines, ReadOnlySpan<PortableDocumentObject> objects,
        Span<PortableDocumentBox> boxes, Span<PortableDocumentLinePosition> positions)
    {
        var extent = NativeDocumentFlow.ArrangeWithObjects(MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentLine, NativeDocumentLine>(lines),
            MemoryMarshal.Cast<PortableDocumentObject, NativeDocumentObject>(objects),
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(positions));
        return new(extent.Width, extent.Height);
    }

    public PortableDocumentExtent Arrange(ReadOnlySpan<PortableDocumentBlock> blocks, double width,
        ReadOnlySpan<PortableDocumentLine> lines, Span<PortableDocumentBox> boxes, Span<PortableDocumentLinePosition> positions)
    {
        var extent = NativeDocumentFlow.Arrange(MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentLine, NativeDocumentLine>(lines),
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes),
            MemoryMarshal.Cast<PortableDocumentLinePosition, NativeDocumentLinePosition>(positions));
        return new(extent.Width, extent.Height);
    }
}
