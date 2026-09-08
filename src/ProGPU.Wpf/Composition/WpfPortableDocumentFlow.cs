using System.Runtime.InteropServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Typed zero-copy adapter only; block layout remains in ProGPU C++.</summary>
internal sealed class WpfPortableDocumentFlow : IPortableDocumentFlow
{
    private static readonly WpfPortableDocumentFlow Default = new();
    internal static void EnsureRegistered() => PortableWpfServiceRegistry.EnsureDocumentFlow(Default);

    public void ResolveWidths(ReadOnlySpan<PortableDocumentBlock> blocks, double width, Span<PortableDocumentBox> boxes)
        => NativeDocumentFlow.ResolveWidths(MemoryMarshal.Cast<PortableDocumentBlock, NativeDocumentBlock>(blocks), width,
            MemoryMarshal.Cast<PortableDocumentBox, NativeDocumentBox>(boxes));

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
