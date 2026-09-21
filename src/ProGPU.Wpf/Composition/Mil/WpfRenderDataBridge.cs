using System;
using System.Collections.Generic;
using System.Windows.Media.ProGPU.Composition;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;
using PortableRenderDataSnapshot = ProGPU.Wpf.Interop.PortableRenderDataSnapshot;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfRenderDataBridge
{
    private readonly WpfMilRenderDataDecoder _decoder;

    public WpfRenderDataBridge()
        : this(new WpfMilRenderDataDecoder())
    {
    }

    public WpfRenderDataBridge(WpfMilRenderDataDecoder decoder)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    public static WpfRenderDataSnapshot Extract(object renderData)
    {
        ArgumentNullException.ThrowIfNull(renderData);

        if (renderData is PortableRenderDataSource portableSource
            && portableSource.TryGetPortableRenderDataSnapshot(out var portableSnapshot))
        {
            return CreateSnapshot(portableSnapshot);
        }

        throw new InvalidOperationException(
            "The supplied object does not implement the portable WPF RenderData source contract.");
    }

    public WpfMilDecodeResult Replay(
        object renderData,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var snapshot = Extract(renderData);
        var decoderImageSourceAdapter = resources == null ? null : imageSourceAdapter;
        resources ??= WpfResourceResolver.FromDependentResources(
            snapshot.DependentResources,
            imageSourceAdapter);
        return _decoder.Decode(snapshot.RenderData, sink, resources, decoderImageSourceAdapter);
    }

    public WpfMilDecodeResult Replay(
        WpfRenderDataSnapshot snapshot,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sink);

        var decoderImageSourceAdapter = resources == null ? null : imageSourceAdapter;
        resources ??= WpfResourceResolver.FromDependentResources(
            snapshot.DependentResources,
            imageSourceAdapter);
        return _decoder.Decode(snapshot.RenderData, sink, resources, decoderImageSourceAdapter);
    }

    private static WpfRenderDataSnapshot CreateSnapshot(PortableRenderDataSnapshot portableSnapshot)
    {
        ArgumentNullException.ThrowIfNull(portableSnapshot);
        ArgumentNullException.ThrowIfNull(portableSnapshot.RenderData);
        ArgumentNullException.ThrowIfNull(portableSnapshot.DependentResources);

        var renderData = portableSnapshot.RenderData.Length == 0
            ? Array.Empty<byte>()
            : portableSnapshot.RenderData;
        var dependentResources = portableSnapshot.DependentResources.Count == 0
            ? Array.Empty<object?>()
            : portableSnapshot.DependentResources;

        return new WpfRenderDataSnapshot(renderData, dependentResources);
    }

}

public sealed class WpfRenderDataSnapshot
{
    public WpfRenderDataSnapshot(byte[] renderData, IReadOnlyList<object?> dependentResources)
    {
        RenderData = renderData ?? throw new ArgumentNullException(nameof(renderData));
        DependentResources = dependentResources ?? throw new ArgumentNullException(nameof(dependentResources));
    }

    public byte[] RenderData { get; }

    public IReadOnlyList<object?> DependentResources { get; }
}
