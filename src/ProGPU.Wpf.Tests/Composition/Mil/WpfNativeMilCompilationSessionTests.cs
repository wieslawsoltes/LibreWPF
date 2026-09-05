using System.Buffers.Binary;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend;
using ProGPU.Backend.Native;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfNativeMilCompilationSessionTests
{
    [Fact]
    public void ViewportSnapshotsOwnEachAppliedPayloadAcrossProducerMutations()
    {
        var first = new NativeMilViewport3DScene(default, new(0, 0, 100, 100),
            new NativeSceneMesh3D[1], new NativeSceneMesh3DVertex[3], [0, 1, 2], []);
        var second = first with { Indices = [2, 1, 0] };
        var batch = new WpfNativeMilBatch([], 1, Viewport3DScenes:
            [new(2, first), new(3, second)]);
        NativeMilViewport3DSnapshot[] snapshots =
            WpfNativeMilCompilationSession.CaptureViewportSnapshots(batch);
        Assert.Equal(2, snapshots.Length);
        Assert.True(snapshots[0].Matches(first));
        Assert.True(snapshots[1].Matches(second));
        first.Indices[0] = 2;
        Assert.False(snapshots[0].Matches(first));
        Assert.True(snapshots[1].Matches(second));
        NativeMilViewport3DSnapshot[] replacement =
            WpfNativeMilCompilationSession.CaptureViewportSnapshots(batch);
        Assert.True(replacement[0].Matches(first));
        Assert.False(snapshots[0].Matches(first));
        Assert.Empty(WpfNativeMilCompilationSession.CaptureViewportSnapshots(
            new WpfNativeMilBatch([], 1)));
    }

    [Fact]
    public void CreateDeltaSkipsIdenticalBatchesWithoutAllocatingCommands()
    {
        WpfNativeMilBatch batch = CreateVisualBatch(2, 3);

        NativeMilBatchDelta delta =
            WpfNativeMilCompilationSession.CreateDelta(batch, batch);

        Assert.False(delta.RequiresRebuild);
        Assert.Empty(delta.Bytes);
    }

    [Fact]
    public void CreateDeltaEmitsOnlyChangedMutablePacket()
    {
        WpfNativeMilBatch previous = CreateVisualBatch(2, 3);
        WpfNativeMilBatch current = CreateVisualBatch(7, 11);

        NativeMilBatchDelta delta =
            WpfNativeMilCompilationSession.CreateDelta(previous, current);

        Assert.False(delta.RequiresRebuild);
        Assert.Equal(28, delta.Bytes.Length);
        Assert.Equal(0x1bU, ReadUInt32(delta.Bytes, 4));
        Assert.Equal(1U, ReadUInt32(delta.Bytes, 8));
        Assert.Equal(7, ReadDouble(delta.Bytes, 12));
        Assert.Equal(11, ReadDouble(delta.Bytes, 20));
    }

    [Fact]
    public void CreateDeltaKeepsChangedRenderDataOnRetainedChannel()
    {
        WpfNativeMilBatch previous = CreateRenderDataBatch(4);
        WpfNativeMilBatch current = CreateRenderDataBatch(9);

        NativeMilBatchDelta delta =
            WpfNativeMilCompilationSession.CreateDelta(previous, current);

        Assert.False(delta.RequiresRebuild);
        Assert.Single(ReadCommands(delta.Bytes));
        Assert.Equal(0x18U, ReadUInt32(delta.Bytes, 4));
        Assert.Equal(2U, ReadUInt32(delta.Bytes, 8));
    }

    [Fact]
    public void CreateDeltaRebuildsForChangedChildTopology()
    {
        WpfNativeMilBatch previous = CreateChildBatch(2);
        WpfNativeMilBatch current = CreateChildBatch(3);

        NativeMilBatchDelta delta =
            WpfNativeMilCompilationSession.CreateDelta(previous, current);

        Assert.True(delta.RequiresRebuild);
        Assert.Empty(delta.Bytes);
    }

    [Fact]
    public void CreateDeltaRebuildsWhenPacketIdentityChanges()
    {
        WpfNativeMilBatch previous = CreateVisualBatch(2, 3);
        var builder = new NativeMilBatchBuilder();
        builder.CreateResource(1, NativeMilResourceType.Visual);
        builder.CreateVisual(1);
        builder.SetVisualOpacity(1, 0.5);
        var current = new WpfNativeMilBatch(builder.ToArray(), 1);

        NativeMilBatchDelta delta =
            WpfNativeMilCompilationSession.CreateDelta(previous, current);

        Assert.True(delta.RequiresRebuild);
    }

    [Fact]
    public void CreateDeltaRejectsMalformedPacketBeforeMutation()
    {
        WpfNativeMilBatch previous = CreateVisualBatch(2, 3);
        var current = new WpfNativeMilBatch([12, 0, 0, 0], 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => WpfNativeMilCompilationSession.CreateDelta(
                previous, current));

        Assert.Contains("truncated packet header", exception.Message);
    }

    [Fact]
    public void SidebandTopologyUsesTypedHandleOrder()
    {
        var previous = new WpfNativeMilBatch(
            [],
            1,
            BitmapSources:
            [new WpfNativeMilBitmapSource(2, 1, 1, 4, [1, 2, 3, 4])],
            DrawingImageBounds:
            [new WpfNativeMilDrawingImageBounds(
                3, new NativeMilRect(1, 2, 3, 4))],
            MediaPlayerSources:
            [new WpfNativeMilMediaPlayerSource(
                4, 64, 32, 1, new FakeTextureSource())],
            BitmapExternalImageSources:
            [new WpfNativeMilBitmapExternalImageSource(
                5, 32, 16, new FakeTextureSource())],
            D3DImageSources:
            [new WpfNativeMilD3DImageSource(
                6, 128, 64, 1, new FakeTextureSource())]);
        var sameTopology = previous with
        {
            BitmapSources =
            [new WpfNativeMilBitmapSource(2, 1, 1, 4, [4, 3, 2, 1])],
            DrawingImageBounds =
            [new WpfNativeMilDrawingImageBounds(
                3, new NativeMilRect(5, 6, 7, 8))]
        };
        var changedHandle = sameTopology with
        {
            MediaPlayerSources =
            [new WpfNativeMilMediaPlayerSource(
                5, 64, 32, 2, new FakeTextureSource())]
        };

        Assert.True(WpfNativeMilCompilationSession.HasStableSidebandTopology(
            previous, sameTopology));
        Assert.False(WpfNativeMilCompilationSession.HasStableSidebandTopology(
            previous, changedHandle));
    }

    [Fact]
    public void ExternalBitmapSidebandEqualityUsesStableDescriptor()
    {
        var previous = new WpfNativeMilBitmapExternalImageSource(
            2, 64, 32, new FakeTextureSource());
        var replacementSource = new WpfNativeMilBitmapExternalImageSource(
            2, 64, 32, new FakeTextureSource());
        var resized = replacementSource with { Height = 33 };

        Assert.True(WpfNativeMilCompilationSession.SidebandEquals(
            previous, replacementSource));
        Assert.False(WpfNativeMilCompilationSession.SidebandEquals(
            previous, resized));
    }

    [Fact]
    public void MediaPlayerSidebandEqualityUsesStableExternalDescriptor()
    {
        var previous = new WpfNativeMilMediaPlayerSource(
            2, 64, 32, 1, new FakeTextureSource());
        var newerFrame = new WpfNativeMilMediaPlayerSource(
            2, 64, 32, 9, new FakeTextureSource());
        var resized = newerFrame with { Width = 65 };

        Assert.True(WpfNativeMilCompilationSession.SidebandEquals(
            previous, newerFrame));
        Assert.False(WpfNativeMilCompilationSession.SidebandEquals(
            previous, resized));
    }

    [Fact]
    public void D3DImageSidebandEqualityIncludesPresentVersion()
    {
        var previous = new WpfNativeMilD3DImageSource(
            2, 64, 32, 1, new FakeTextureSource());
        var replacementSource = new WpfNativeMilD3DImageSource(
            2, 64, 32, 1, new FakeTextureSource());
        var presented = replacementSource with { ContentVersion = 2 };

        Assert.True(WpfNativeMilCompilationSession.SidebandEquals(
            previous, replacementSource));
        Assert.False(WpfNativeMilCompilationSession.SidebandEquals(
            previous, presented));
    }

    [Fact]
    public void BitmapSidebandEqualityComparesPixelContent()
    {
        var previous = new WpfNativeMilBitmapSource(
            2, 1, 1, 4, [1, 2, 3, 4]);
        var same = new WpfNativeMilBitmapSource(
            2, 1, 1, 4, [1, 2, 3, 4]);
        var changed = new WpfNativeMilBitmapSource(
            2, 1, 1, 4, [1, 2, 3, 5]);

        Assert.True(WpfNativeMilCompilationSession.SidebandEquals(
            previous, same));
        Assert.False(WpfNativeMilCompilationSession.SidebandEquals(
            previous, changed));
    }

    [Fact]
    public void FontSidebandEqualityComparesSfntContentAndFace()
    {
        var previous = new WpfNativeMilGlyphRunFont(
            2, 0, NativeMilGlyphStyleSimulations.None,
            new byte[] { 1, 2, 3 });
        var same = new WpfNativeMilGlyphRunFont(
            2, 0, NativeMilGlyphStyleSimulations.None,
            new byte[] { 1, 2, 3 });
        var changed = new WpfNativeMilGlyphRunFont(
            2, 1, NativeMilGlyphStyleSimulations.None,
            new byte[] { 1, 2, 3 });

        Assert.True(WpfNativeMilCompilationSession.SidebandEquals(
            previous, same));
        Assert.False(WpfNativeMilCompilationSession.SidebandEquals(
            previous, changed));
    }

    private static WpfNativeMilBatch CreateVisualBatch(double x, double y)
    {
        var builder = new NativeMilBatchBuilder();
        builder.CreateResource(1, NativeMilResourceType.Visual);
        builder.CreateVisual(1);
        builder.SetVisualOffset(1, x, y);
        return new WpfNativeMilBatch(builder.ToArray(), 1);
    }

    private static WpfNativeMilBatch CreateRenderDataBatch(
        double guideline)
    {
        var renderData = new NativeMilRenderDataBuilder();
        renderData.PushGuidelineY1(guideline);
        renderData.Pop();
        var builder = new NativeMilBatchBuilder();
        builder.CreateResource(2, NativeMilResourceType.RenderData);
        builder.SetRenderData(2, renderData);
        return new WpfNativeMilBatch(builder.ToArray(), 2);
    }

    private static WpfNativeMilBatch CreateChildBatch(uint childHandle)
    {
        var builder = new NativeMilBatchBuilder();
        builder.CreateResource(1, NativeMilResourceType.Visual);
        builder.CreateVisual(1);
        builder.CreateResource(childHandle, NativeMilResourceType.Visual);
        builder.CreateVisual(childHandle);
        builder.InsertVisualChild(1, childHandle, 0);
        return new WpfNativeMilBatch(builder.ToArray(), 1);
    }

    private static uint[] ReadCommands(byte[] bytes)
    {
        var commands = new List<uint>();
        for (int offset = 0; offset < bytes.Length;)
        {
            int size = checked((int)ReadUInt32(bytes, offset));
            commands.Add(ReadUInt32(bytes, offset + 4));
            offset += size;
        }
        return commands.ToArray();
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static double ReadDouble(byte[] bytes, int offset) =>
        BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(
                bytes.AsSpan(offset, 8)));

    private sealed class FakeTextureSource : IProGpuTextureLeaseSource
    {
        public bool TryGetGpuTexture(out GpuTexture texture)
        {
            texture = null!;
            return false;
        }

        public bool TryAcquireGpuTextureLease(out IProGpuTextureLease lease)
        {
            lease = null!;
            return false;
        }
    }
}
