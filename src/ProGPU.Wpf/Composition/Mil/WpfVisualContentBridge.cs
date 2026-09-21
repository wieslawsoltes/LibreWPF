using System;
using System.Windows.Media.ProGPU.Composition;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualContentBridge
{
    private readonly WpfRenderDataBridge _renderDataBridge;

    public WpfVisualContentBridge()
        : this(new WpfRenderDataBridge())
    {
    }

    public WpfVisualContentBridge(WpfRenderDataBridge renderDataBridge)
    {
        _renderDataBridge = renderDataBridge ?? throw new ArgumentNullException(nameof(renderDataBridge));
    }

    public static object? ExtractContent(object drawingVisual)
    {
        ArgumentNullException.ThrowIfNull(drawingVisual);

        if (TryExtractContent(drawingVisual, out var content))
        {
            return content;
        }

        throw new InvalidOperationException(
            "The supplied object does not implement the portable WPF visual content source contract.");
    }

    public static bool TryExtractContent(object drawingVisual, out object? content)
    {
        ArgumentNullException.ThrowIfNull(drawingVisual);

        if (drawingVisual is PortableDrawingContentSource drawingContentSource
            && drawingContentSource.TryGetPortableDrawingContent(out content))
        {
            return true;
        }

        content = null;
        return false;
    }

    public WpfMilDecodeResult ReplayContent(
        object drawingVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var content = ExtractContent(drawingVisual);
        if (content == null)
        {
            return default;
        }

        if (content is not PortableRenderDataSource)
        {
            throw new NotSupportedException(
                "The supplied WPF visual content is not supported by the ProGPU RenderData replay bridge.");
        }

        return _renderDataBridge.Replay(content, sink, resources, imageSourceAdapter);
    }
}
