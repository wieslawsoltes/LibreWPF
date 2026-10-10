using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(0)] // default
    [InlineData(1)] // target cache
    [InlineData(2)] // explicitly selected cache
    public void CacheShaderSamplerKeepsActualTargetAndSelectedCache(int selection)
    {
        var cache = new FakeBitmapCache(new(2, true, false));
        var target = new FakeVisual(null, selection == 1
            ? new PortableVisualState { HasCacheMode = true, CacheMode = cache } : null);
        var brush = new ShaderCacheBrush(new(target, selection == 2 ? cache : null));
        using var batch = CacheShaderBatch(VisualSamplerEffect(brush));
        int packet = FindCommand(batch.Bytes, 0x84);
        uint targetHandle = ReadUInt32(batch.Bytes, packet + 36);
        Assert.True(batch.VisualOwners.TryGetOwner(unchecked((int)targetHandle), out object? source));
        Assert.Same(target, source);
        Assert.Equal(1, brush.Reads);
        Assert.Equal(1, ReadCommands(batch.Bytes).Count(value => value == 0x84));
        Assert.DoesNotContain(0x83, ReadCommands(batch.Bytes));
        Assert.Empty(batch.BitmapSources!);
        Assert.Empty(batch.EmptyVisualBrushSources.ToArray());
        Assert.Equal(selection == 2,
            ReadUInt32(batch.Bytes, packet + 32) != 0U);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20),
            Assert.Single(batch.VisualCacheBounds!, bounds => bounds.Handle == targetHandle).Bounds);
    }

    [Fact]
    public void CacheShaderNullKeepsActualBrushPacketAndDoesNotCreateSourceVisual()
    {
        var brush = new ShaderCacheBrush(new(null));
        using var batch = CacheShaderBatch(VisualSamplerEffect(brush));
        int packet = FindCommand(batch.Bytes, 0x84);
        Assert.Equal(0U, ReadUInt32(batch.Bytes, packet + 36));
        Assert.Equal(1, batch.VisualOwners.Count); // receiver only
        Assert.Single(batch.VisualCacheBounds!); // receiver's frame only
        Assert.Empty(batch.EmptyVisualBrushSources.ToArray());
        Assert.Contains(0x70, ReadCommands(batch.Bytes));
    }

    [Fact]
    public void CacheShaderRejectsMissingBoundsWithoutChangingPreviousBatch()
    {
        var brush = new ShaderCacheBrush(new(new FakeVisual(null)));
        var effect = VisualSamplerEffect(brush);
        using var original = CacheShaderBatch(effect);
        byte[] bytes = (byte[])original.Bytes.Clone();
        brush.State = new(new FakeVisualWithoutBounds(new()));
        Assert.Contains("original source bounds", Assert.Throws<NotSupportedException>(() => CacheShaderBatch(effect)).Message);
        Assert.Equal(bytes, original.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheShaderRejectsReplacedTargetOrCacheDuringOneCandidate(bool replaceCache)
    {
        var target = new FakeVisual(null);
        var brush = new ShaderCacheBrush(new(target, new FakeBitmapCache(new(1, false, false))));
        var later = VisualSamplerEffect(brush);
        later.AfterCapture = () => brush.State = replaceCache
            ? brush.State with { BitmapCache = new FakeBitmapCache(new(2, false, false)) }
            : brush.State with { InternalTarget = new FakeVisual(null) };
        Assert.Contains("changed during native batch capture", Assert.Throws<InvalidOperationException>(() =>
            CreateCacheShaderCompiler().BuildBatch(new FakeVisual(null, null,
                ShaderVisual(VisualSamplerEffect(brush)), ShaderVisual(later)), 64, 64)).Message);
    }

    [Fact]
    public void CacheShaderSelfReferenceCannotPublishAnOwnedTarget()
    {
        var brush = new ShaderCacheBrush(new(null));
        var receiver = ShaderVisual(VisualSamplerEffect(brush));
        brush.State = new(receiver);
        Assert.Contains("cycle", Assert.Throws<InvalidOperationException>(() =>
            CreateCacheShaderCompiler().BuildBatch(receiver, 64, 64)).Message);
    }

    [Fact]
    public void CacheShaderDependenciesIncludeOriginalTargetAndCache()
    {
        var target = new FakeVisual(null);
        var cache = new FakeBitmapCache(new(1, false, false));
        var brush = new ShaderCacheBrush(new(target, cache));
        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(VisualSamplerEffect(brush));
        Assert.Contains(brush, dependencies);
        Assert.Contains(target, dependencies);
        Assert.Contains(cache, dependencies);
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative)]
    [InlineData(NativeMilBackend.Dawn)]
    public void CacheShaderSessionRetainsRejectedGenerationAndReattachesSameTarget(NativeMilBackend backend)
    {
        var target = new SamplerVisual(new FakeRenderData(CreateRectangleRecord(1, 0),
            [new FakeBrush(new(255, 255, 0, 0))]));
        var brush = new ShaderCacheBrush(new(target));
        var receiver = ShaderVisual(VisualSamplerEffect(brush));
        using var session = new WpfNativeMilCompilationSession(backend, CreateCacheShaderCompiler());
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var initial = session.CompileFrame(11951, 1, 0, 1);
        byte[] original = initial.Scene.Stream.ToArray();
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);
        brush.State = new(new FakeVisualWithoutBounds(new()));
        Assert.Throws<NotSupportedException>(() => session.Update(receiver, 64, 64));
        Assert.Equal(original, session.CompileFrame(11951, 1, 0, 1).Scene.Stream.ToArray());
        brush.State = new(null);
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var disconnected = session.CompileFrame(11951, 2, 0, 2);
        byte[] noSource = disconnected.Scene.Stream.ToArray();
        brush.State = new(target);
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var restored = session.CompileFrame(11951, 3, 0, 3);
        Assert.NotEmpty(restored.Scene.Stream);
        Assert.Equal(initial.Scene.Metrics.RectangleCount, restored.Scene.Metrics.RectangleCount);
        // A replacement channel owns new native cache identities. Do not
        // require its internal picture identifiers to equal the retired page.
        Assert.Equal(original, initial.Scene.Stream.ToArray());
        Assert.Equal(noSource, disconnected.Scene.Stream.ToArray());
    }

    // These are explicit synthetic source/device-policy fixtures, not host
    // limit discovery or a default granted to unrelated shader tests.
    private static WpfNativeMilSceneCompiler CreateCacheShaderCompiler(ulong revision = 1) =>
        new(() => new PortableBitmapCacheRasterPolicy(1, 1, 4096, 4096, revision));

    private static WpfNativeMilBatch CacheShaderBatch(ShaderEffectSource effect) =>
        CreateCacheShaderCompiler().BuildBatch(ShaderVisual(effect), 64, 64);

    private sealed class ShaderCacheBrush(PortableBitmapCacheBrush state) : IPortableBitmapCacheBrushSource,
        IPortableBrushSource
    {
        internal PortableBitmapCacheBrush State = state;
        internal int Reads;
        public bool TryGetPortableBitmapCacheBrush(out PortableBitmapCacheBrush brush)
        { ++Reads; brush = State; return true; }
        public bool TryGetPortableBrush(out PortableBrush brush) =>
            throw new InvalidOperationException("The original cache sampler cannot use ordinary solid/gradient fallback.");
    }
}
