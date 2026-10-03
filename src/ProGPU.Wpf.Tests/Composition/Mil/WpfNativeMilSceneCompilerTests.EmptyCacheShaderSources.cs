using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(false, 0)] // shared brush
    [InlineData(true, 0)]
    [InlineData(false, 1)] // shared drawing
    [InlineData(true, 1)]
    [InlineData(false, 2)] // shared visual
    [InlineData(true, 2)]
    public void CacheShaderEmptyClosureRetainsReusedOrdinarySourceEdges(bool shaderFirst, int sharedKind)
    {
        var descendant = new FakeVisual(null);
        var empty = new CacheRasterVisual(null, new(), PortableRect.Empty, descendant);
        var inner = new ShaderCacheBrush(new(empty));
        FakeRenderData content;
        if (sharedKind == 1)
        {
            var drawing = new FakeGeometryDrawing(inner, null,
                new FakePrimitiveGeometry(PortablePrimitiveGeometry.Rectangle(
                    new(1, 2, 30, 20), 0, 0, PortableMatrix3x2.Identity)));
            content = new(CreateDrawDrawingRecord(1), [drawing]);
        }
        else
        {
            content = new(CreateRectangleRecord(1, 0), [inner]);
        }
        var source = new FakeVisual(content);
        var ordinary = sharedKind == 2 ? source : new FakeVisual(content);
        var outer = new ShaderCacheBrush(new(source));
        var receiver = ShaderVisual(VisualSamplerEffect(outer));
        var root = new FakeVisual(null, null, shaderFirst ? [receiver, ordinary] : [ordinary, receiver]);
        using var batch = CreateCacheShaderCompiler().BuildBatch(root, 64, 64);

        var edge = Assert.Single(batch.EmptyCacheBrushSources.ToArray());
        var policy = Assert.Single(batch.BitmapCacheRasterPolicies.ToArray());
        Assert.NotEqual(policy.Handle, edge.BrushHandle);
        int[] brushPackets = CacheRasterCommandOffsets(batch.Bytes, 0x84);
        Assert.Equal(2, brushPackets.Length);
        int innerPacket = Assert.Single(brushPackets, offset => ReadUInt32(batch.Bytes, offset + 8) == edge.BrushHandle);
        int outerPacket = Assert.Single(brushPackets, offset => ReadUInt32(batch.Bytes, offset + 8) == policy.Handle);
        Assert.Equal(0U, ReadUInt32(batch.Bytes, innerPacket + 36)); // ordinary paint stays canonical empty
        Assert.NotEqual(0U, ReadUInt32(batch.Bytes, outerPacket + 36));
        Assert.Equal(edge.VisualHandle, Assert.Single(batch.EmptyVisualBrushSources.ToArray()));
        Assert.True(batch.VisualOwners.TryGetOwner(unchecked((int)edge.VisualHandle), out object? owner));
        Assert.Same(empty, owner);
        Assert.Contains(CacheRasterCommandOffsets(batch.Bytes, 0x1a), offset =>
            batch.VisualOwners.TryGetOwner(unchecked((int)ReadUInt32(batch.Bytes, offset + 8)), out object? childOwner) &&
            ReferenceEquals(descendant, childOwner));
        Assert.DoesNotContain(batch.VisualCacheBounds!, bounds => bounds.Handle == edge.VisualHandle);
        // The metadata owns the original empty dependency, not a fabricated
        // positive source rectangle or a cache policy granted to ordinary paint.
        using var snapshot = batch with { };
        Assert.Equal(batch.EmptyCacheBrushSources.ToArray(), snapshot.EmptyCacheBrushSources.ToArray());
        Assert.Equal(batch.EmptyVisualBrushSources.ToArray(), snapshot.EmptyVisualBrushSources.ToArray());
    }

    [Fact]
    public void CacheShaderDirectEmptyTargetIsNotAnOrdinaryNullPaintAlias()
    {
        var source = new SamplerVisual(null) { Empty = true };
        using var batch = CacheShaderBatch(VisualSamplerEffect(new ShaderCacheBrush(new(source))));
        int packet = FindCommand(batch.Bytes, 0x84);
        uint sourceHandle = ReadUInt32(batch.Bytes, packet + 36);
        Assert.NotEqual(0U, sourceHandle);
        Assert.Equal(sourceHandle, Assert.Single(batch.EmptyVisualBrushSources.ToArray()));
        Assert.Empty(batch.EmptyCacheBrushSources.ToArray());
        Assert.Equal(ReadUInt32(batch.Bytes, packet + 8),
            Assert.Single(batch.BitmapCacheRasterPolicies.ToArray()).Handle);
    }

    [Fact]
    public void CacheShaderEmptyClosureDoesNotGrantOrdinaryOnlyAdmission()
    {
        // The ordinary empty paint path historically skips this source. Only
        // a shader closure must retain and validate its hidden dependency graph.
        var invalid = new CacheRasterVisual(null,
            new() { HasVisibility = true, Visibility = PortableVisualVisibility.Hidden },
            PortableRect.Empty, new object());
        var inner = new ShaderCacheBrush(new(invalid));
        var source = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [inner]));
        using var ordinary = new WpfNativeMilSceneCompiler().BuildBatch(source, 64, 64);
        byte[] bytes = (byte[])ordinary.Bytes.Clone();
        Assert.Equal(0U, ReadUInt32(ordinary.Bytes, FindCommand(ordinary.Bytes, 0x84) + 36));
        Assert.Empty(ordinary.EmptyVisualBrushSources.ToArray());
        Assert.Empty(ordinary.EmptyCacheBrushSources.ToArray());
        Assert.Empty(ordinary.BitmapCacheRasterPolicies.ToArray());
        Assert.Equal(1, ordinary.VisualOwners.Count);

        var outer = new ShaderCacheBrush(new(source));
        Assert.Contains(nameof(IPortableVisualStateSource), Assert.Throws<InvalidOperationException>(() =>
            CacheShaderBatch(VisualSamplerEffect(outer))).Message);
        Assert.Equal(bytes, ordinary.Bytes);
        Assert.Equal(1, ordinary.VisualOwners.Count);
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative)]
    [InlineData(NativeMilBackend.Dawn)]
    public void CacheShaderNestedEmptyEdgeTransitionsRetainPreviousNativeGeneration(NativeMilBackend backend)
    {
        var empty = new SamplerVisual(null) { Empty = true };
        var inner = new ShaderCacheBrush(new(empty));
        var source = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [inner]));
        var outer = new ShaderCacheBrush(new(source));
        var receiver = ShaderVisual(VisualSamplerEffect(outer));
        var compiler = CreateCacheShaderCompiler();
        using var firstBatch = compiler.BuildBatch(receiver, 64, 64);
        var edge = Assert.Single(firstBatch.EmptyCacheBrushSources.ToArray());
        using var session = new WpfNativeMilCompilationSession(backend, compiler);
        Assert.True(session.Update(firstBatch).RecreatedChannel);
        var first = session.CompileFrame(11953, 1, 0, 1);
        byte[] firstBytes = first.Scene.Stream.ToArray();

        empty.Empty = false;
        empty.Content = new FakeRenderData(CreateRectangleRecord(1, 0), [new FakeBrush(new(255, 0, 0, 255))]);
        using var positiveBatch = compiler.BuildBatch(receiver, 64, 64);
        Assert.Empty(positiveBatch.EmptyCacheBrushSources.ToArray());
        Assert.Empty(positiveBatch.EmptyVisualBrushSources.ToArray());
        Assert.True(session.Update(positiveBatch).RecreatedChannel);
        var positive = session.CompileFrame(11953, 2, 0, 2);
        byte[] positiveBytes = positive.Scene.Stream.ToArray();
        Assert.Equal(edge, Assert.Single(firstBatch.EmptyCacheBrushSources.ToArray()));
        Assert.Equal(firstBytes, first.Scene.Stream.ToArray());

        inner.State = new(new FakeVisualWithoutBounds(new()));
        Assert.Throws<NotSupportedException>(() => session.Update(receiver, 64, 64));
        Assert.Equal(positiveBytes, session.CompileFrame(11953, 2, 0, 2).Scene.Stream.ToArray());
        inner.State = new(empty);
        empty.Content = null;
        empty.Empty = true;
        using var restoredBatch = compiler.BuildBatch(receiver, 64, 64);
        Assert.Equal(edge, Assert.Single(restoredBatch.EmptyCacheBrushSources.ToArray()));
        Assert.True(session.Update(restoredBatch).RecreatedChannel);
        var restored = session.CompileFrame(11953, 3, 0, 3);
        Assert.NotEmpty(restored.Scene.Stream);
        Assert.Equal(first.Scene.Metrics.RectangleCount, restored.Scene.Metrics.RectangleCount);
        Assert.Equal(firstBytes, first.Scene.Stream.ToArray());
        Assert.Equal(positiveBytes, positive.Scene.Stream.ToArray());
    }
}
