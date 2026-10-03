using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(0)] // null target
    [InlineData(1)] // authoritative empty target
    [InlineData(2)] // positive target
    public void CacheShaderRequiresExplicitRasterPolicyForEveryTargetKind(int targetKind)
    {
        var brush = new ShaderCacheBrush(new(CacheRasterTarget(targetKind)));
        Assert.Contains("raster policy", Assert.Throws<NotSupportedException>(() =>
            ShaderBatch(VisualSamplerEffect(brush))).Message);

        using var batch = CacheShaderBatch(VisualSamplerEffect(brush));
        var raster = Assert.Single(batch.BitmapCacheRasterPolicies.ToArray());
        int packet = FindCommand(batch.Bytes, 0x84);
        Assert.Equal(ReadUInt32(batch.Bytes, packet + 8), raster.Handle);
        Assert.Equal(new PortableBitmapCacheRasterPolicy(1, 1, 4096, 4096, 1), raster.Policy);
        uint sourceHandle = ReadUInt32(batch.Bytes, packet + 36);
        if (targetKind == 0)
        {
            Assert.Equal(0U, sourceHandle);
        }
        else
        {
            Assert.NotEqual(0U, sourceHandle);
            Assert.True(batch.VisualOwners.TryGetOwner(unchecked((int)sourceHandle), out object? source));
            Assert.Same(brush.State.InternalTarget, source);
        }
    }

    [Fact]
    public void CacheShaderCapturesOnePolicyAndRetainsIndependentBatchSnapshots()
    {
        var firstPolicy = new PortableBitmapCacheRasterPolicy(1.25f, 2, 2048, 1024, 7);
        var secondPolicy = new PortableBitmapCacheRasterPolicy(2, 1.5f, 4096, 8192, 8);
        PortableBitmapCacheRasterPolicy current = firstPolicy;
        int calls = 0;
        var compiler = new WpfNativeMilSceneCompiler(() => { ++calls; return current; });
        var source = new FakeVisual(null);
        var first = new ShaderCacheBrush(new(source));
        var second = new ShaderCacheBrush(new(source));
        var laterEffect = VisualSamplerEffect(second);
        laterEffect.AfterCapture = () => current = secondPolicy;
        var root = new FakeVisual(null, null,
            ShaderVisual(VisualSamplerEffect(first)), ShaderVisual(laterEffect));

        using var before = compiler.BuildBatch(root, 64, 64);
        byte[] bytes = (byte[])before.Bytes.Clone();
        var oldPolicies = before.BitmapCacheRasterPolicies.ToArray();
        using var snapshot = before with { };
        Assert.Equal(1, calls);
        Assert.Equal(2, oldPolicies.Length);
        Assert.NotEqual(oldPolicies[0].Handle, oldPolicies[1].Handle);
        Assert.All(oldPolicies, entry => Assert.Equal(firstPolicy, entry.Policy));

        using var after = compiler.BuildBatch(root, 64, 64);
        Assert.Equal(2, calls);
        Assert.All(after.BitmapCacheRasterPolicies.ToArray(), entry => Assert.Equal(secondPolicy, entry.Policy));
        // Policy changes are explicit sidebands, not rewritten source packets.
        Assert.Equal(bytes, after.Bytes);
        Assert.Equal(bytes, before.Bytes);
        Assert.Equal(oldPolicies, before.BitmapCacheRasterPolicies.ToArray());
        Assert.Equal(oldPolicies, snapshot.BitmapCacheRasterPolicies.ToArray());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void CacheShaderAndOrdinaryPaintKeepPurposeSpecificHandlesInEitherOrder(
        bool shaderFirst, int targetKind)
    {
        var source = CacheRasterTarget(targetKind);
        var cache = new FakeBitmapCache(new(2, true, false));
        var brush = new ShaderCacheBrush(new(source, cache, 0.25,
            true, new(1, 0, 0, 1, 4, 5), true, new(2, 0, 0, 3, 0, 0)));
        var ordinary = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [brush]));
        var shader = ShaderVisual(VisualSamplerEffect(brush));
        var root = new FakeVisual(null, null, shaderFirst ? [shader, ordinary] : [ordinary, shader]);
        using var batch = CreateCacheShaderCompiler().BuildBatch(root, 64, 64);

        int[] packets = CacheRasterCommandOffsets(batch.Bytes, 0x84);
        Assert.Equal(2, packets.Length);
        var policy = Assert.Single(batch.BitmapCacheRasterPolicies.ToArray());
        int shaderPacket = Assert.Single(packets, offset => ReadUInt32(batch.Bytes, offset + 8) == policy.Handle);
        int ordinaryPacket = Assert.Single(packets, offset => ReadUInt32(batch.Bytes, offset + 8) != policy.Handle);
        Assert.Equal(shaderFirst, shaderPacket < ordinaryPacket);
        int effectPacket = FindCommand(batch.Bytes, 0x70);
        Assert.Equal(0U, ReadUInt32(batch.Bytes, effectPacket + 52)); // no float registers
        Assert.Equal(0U, ReadUInt32(batch.Bytes, effectPacket + 56));
        Assert.Equal(policy.Handle, ReadUInt32(batch.Bytes, effectPacket + 92));
        foreach (int packet in packets)
        {
            // Raw sampler capture ignores these paint properties; the source
            // packets still retain them for ordinary paint using the same brush.
            Assert.Equal(0.25, ReadDouble(batch.Bytes, packet + 12));
            Assert.NotEqual(0U, ReadUInt32(batch.Bytes, packet + 24));
            Assert.NotEqual(0U, ReadUInt32(batch.Bytes, packet + 28));
            Assert.NotEqual(0U, ReadUInt32(batch.Bytes, packet + 32));
        }
        uint shaderSource = ReadUInt32(batch.Bytes, shaderPacket + 36);
        uint ordinarySource = ReadUInt32(batch.Bytes, ordinaryPacket + 36);
        if (targetKind == 0)
        {
            Assert.Equal(0U, shaderSource);
            Assert.Equal(0U, ordinarySource);
        }
        else
        {
            Assert.NotEqual(0U, shaderSource);
            Assert.True(batch.VisualOwners.TryGetOwner(unchecked((int)shaderSource), out object? owner));
            Assert.Same(source, owner);
            Assert.Equal(targetKind == 1 ? 0U : shaderSource, ordinarySource);
        }
    }

    [Fact]
    public void CacheShaderPolicyIsIndependentOfReceiverAndBrushPaintState()
    {
        var policy = new PortableBitmapCacheRasterPolicy(1.5f, 2, 1024, 2048, 11);
        var compiler = new WpfNativeMilSceneCompiler(() => policy);
        var cache = new FakeBitmapCache(new(0.5, false, true));
        var target = new FakeVisual(null);
        var brush = new ShaderCacheBrush(new(target, cache));
        var effect = VisualSamplerEffect(brush);
        var receiver = new CacheRasterVisual(new FakeRenderData(CreateRectangleRecord(1, 0),
            [new FakeBrush(new(255, 255, 255, 255))]),
            new PortableVisualState { HasEffect = true, Effect = effect }, new(1, 2, 30, 20));
        using var before = compiler.BuildBatch(receiver, 64, 64);
        var policies = before.BitmapCacheRasterPolicies.ToArray();

        receiver.Bounds = new(8, 10, 48, 12);
        brush.State = brush.State with
        {
            Opacity = 0,
            HasTransform = true, Transform = new(2, 0, 0, 3, 40, -8),
            HasRelativeTransform = true, RelativeTransform = new(0.5, 0, 0, 0.25, 0.125, 0.5)
        };
        using var after = compiler.BuildBatch(receiver, 128, 96);
        Assert.Equal(policy, Assert.Single(policies).Policy);
        Assert.Equal(policy, Assert.Single(after.BitmapCacheRasterPolicies.ToArray()).Policy);
        Assert.Equal(policies, before.BitmapCacheRasterPolicies.ToArray());
        Assert.Contains(after.VisualCacheBounds!, entry => entry.Bounds == new NativeMilRect(8, 10, 48, 12));
        int packet = FindCommand(after.Bytes, 0x84);
        Assert.Equal(0.0, ReadDouble(after.Bytes, packet + 12));
        Assert.NotEqual(0U, ReadUInt32(after.Bytes, packet + 24));
        Assert.NotEqual(0U, ReadUInt32(after.Bytes, packet + 28));
        uint targetHandle = ReadUInt32(after.Bytes, packet + 36);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20),
            Assert.Single(after.VisualCacheBounds!, entry => entry.Handle == targetHandle).Bounds);
    }

    [Fact]
    public void CacheShaderSourceRetainsSixRootPropertiesAndDescendantScrollClip()
    {
        var child = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0),
            [new FakeBrush(new(255, 255, 0, 0))]), new PortableVisualState
            {
                HasScrollableAreaClip = true, ScrollableAreaClip = new(3, 4, 8, 6)
            });
        var target = new FakeVisual(null, new PortableVisualState
        {
            HasOffset = true, Offset = new(100, 200),
            HasTransform = true, Transform = new FakeTransform(new(2, 0, 0, 3, 7, 9)),
            HasOpacity = true, Opacity = 0,
            HasOpacityMask = true, OpacityMask = new FakeBrush(new(0, 0, 0, 0)),
            HasEffect = true, Effect = new FakeEffect(PortableEffect.Blur(2)),
            HasClip = true, Clip = new FakePrimitiveGeometry(PortablePrimitiveGeometry.Rectangle(
                new(90, 90, 1, 1), 0, 0, PortableMatrix3x2.Identity))
        }, child);
        var brush = new ShaderCacheBrush(new(target));
        using var batch = CacheShaderBatch(VisualSamplerEffect(brush));
        uint sourceHandle = ReadUInt32(batch.Bytes, FindCommand(batch.Bytes, 0x84) + 36);
        // Do not erase the six root properties from shared source identity.
        // The native cache-root boundary, not source serialization, excludes
        // them. Descendants retain their ordinary scroll-clip state.
        foreach (int command in new[] { 0x1b, 0x1c, 0x1d, 0x1f, 0x20, 0x23 })
            Assert.Single(CacheRasterCommandOffsets(batch.Bytes, command),
                offset => ReadUInt32(batch.Bytes, offset + 8) == sourceHandle);
        int scroll = Assert.Single(CacheRasterCommandOffsets(batch.Bytes, 0x28));
        uint childHandle = ReadUInt32(batch.Bytes, scroll + 8);
        Assert.NotEqual(sourceHandle, childHandle);
        Assert.True(batch.VisualOwners.TryGetOwner(unchecked((int)childHandle), out object? owner));
        Assert.Same(child, owner);
        Assert.Equal(3.0, ReadDouble(batch.Bytes, scroll + 12));
        Assert.Equal(4.0, ReadDouble(batch.Bytes, scroll + 20));
        Assert.Equal(8.0, ReadDouble(batch.Bytes, scroll + 28));
        Assert.Equal(6.0, ReadDouble(batch.Bytes, scroll + 36));
        Assert.Equal(1U, ReadUInt32(batch.Bytes, scroll + 44));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void CacheShaderRejectsInvalidExplicitPolicyWithoutChangingPublishedBatch(int invalidField)
    {
        var valid = new PortableBitmapCacheRasterPolicy(1, 1, 4096, 4096, 1);
        PortableBitmapCacheRasterPolicy selected = valid;
        var compiler = new WpfNativeMilSceneCompiler(() => selected);
        var receiver = ShaderVisual(VisualSamplerEffect(new ShaderCacheBrush(new(null))));
        using var before = compiler.BuildBatch(receiver, 64, 64);
        byte[] bytes = (byte[])before.Bytes.Clone();
        var policies = before.BitmapCacheRasterPolicies.ToArray();
        selected = invalidField switch
        {
            0 => valid with { PrimaryDpiScaleX = float.NaN },
            1 => valid with { PrimaryDpiScaleY = float.PositiveInfinity },
            2 => valid with { PrimaryDpiScaleX = 0 },
            3 => valid with { PrimaryDpiScaleY = -1 },
            4 => valid with { MaximumTextureWidth = 0 },
            5 => valid with { MaximumTextureHeight = 0 },
            _ => valid with { SourceRevision = 0 }
        };
        Assert.Contains("raster policy", Assert.Throws<NotSupportedException>(() =>
            compiler.BuildBatch(receiver, 64, 64)).Message);
        Assert.Equal(bytes, before.Bytes);
        Assert.Equal(policies, before.BitmapCacheRasterPolicies.ToArray());
        selected = valid;
        using var restored = compiler.BuildBatch(receiver, 64, 64);
        Assert.Equal(bytes, restored.Bytes);
        Assert.Equal(policies, restored.BitmapCacheRasterPolicies.ToArray());
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative)]
    [InlineData(NativeMilBackend.Dawn)]
    public void CacheShaderSessionKeepsEmptyPositivePolicyAndFailureGenerationsOwned(NativeMilBackend backend)
    {
        var policy = new PortableBitmapCacheRasterPolicy(1, 1, 4096, 4096, 1);
        bool failPolicy = false;
        var policyFailure = new InvalidOperationException("fixture policy capture failed");
        var compiler = new WpfNativeMilSceneCompiler(() => failPolicy ? throw policyFailure : policy);
        var target = new SamplerVisual(null) { Empty = true };
        var brush = new ShaderCacheBrush(new(target));
        var receiver = ShaderVisual(VisualSamplerEffect(brush));
        using var session = new WpfNativeMilCompilationSession(backend, compiler);
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        Assert.True(session.HasCacheRasterSamplers);
        Assert.True(session.HasCacheRasterPolicy(policy));
        var empty = session.CompileFrame(11952, 1, 0, 1);
        byte[] emptyBytes = empty.Scene.Stream.ToArray();
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);

        target.Empty = false;
        target.Content = new FakeRenderData(CreateRectangleRecord(1, 0), [new FakeBrush(new(255, 255, 0, 0))]);
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var positive = session.CompileFrame(11952, 2, 0, 2);
        byte[] positiveBytes = positive.Scene.Stream.ToArray();
        Assert.True(session.HasCacheRasterPolicy(policy));
        Assert.Equal(emptyBytes, empty.Scene.Stream.ToArray());

        brush.State = new(new FakeVisualWithoutBounds(new()));
        Assert.Throws<NotSupportedException>(() => session.Update(receiver, 64, 64));
        Assert.Equal(positiveBytes, session.CompileFrame(11952, 2, 0, 2).Scene.Stream.ToArray());
        brush.State = new(target);
        failPolicy = true;
        Assert.Same(policyFailure, Assert.Throws<InvalidOperationException>(() => session.Update(receiver, 64, 64)));
        Assert.Equal(positiveBytes, session.CompileFrame(11952, 2, 0, 2).Scene.Stream.ToArray());
        failPolicy = false;

        var priorPolicy = policy;
        policy = policy with { PrimaryDpiScaleX = 2, PrimaryDpiScaleY = 1.5f, SourceRevision = 2 };
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        Assert.True(session.HasCacheRasterPolicy(policy));
        Assert.False(session.HasCacheRasterPolicy(priorPolicy));
        var reconfigured = session.CompileFrame(11952, 3, 0, 3);
        byte[] reconfiguredBytes = reconfigured.Scene.Stream.ToArray();
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);

        policy = policy with { MaximumTextureHeight = 0 };
        Assert.Throws<NotSupportedException>(() => session.Update(receiver, 64, 64));
        Assert.Equal(reconfiguredBytes, session.CompileFrame(11952, 3, 0, 3).Scene.Stream.ToArray());
        policy = policy with { MaximumTextureHeight = 4096 };
        target.Content = null;
        target.Empty = true;
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var restored = session.CompileFrame(11952, 4, 0, 4);
        Assert.NotEmpty(restored.Scene.Stream);
        Assert.Equal(empty.Scene.Metrics.RectangleCount, restored.Scene.Metrics.RectangleCount);
        Assert.Equal(emptyBytes, empty.Scene.Stream.ToArray());
        Assert.Equal(positiveBytes, positive.Scene.Stream.ToArray());
        Assert.Equal(reconfiguredBytes, reconfigured.Scene.Stream.ToArray());
    }

    private static object? CacheRasterTarget(int kind) => kind switch
    {
        0 => null,
        1 => new SamplerVisual(null) { Empty = true },
        2 => new FakeVisual(null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static int[] CacheRasterCommandOffsets(byte[] bytes, int command)
    {
        var offsets = new List<int>();
        for (int offset = 0; offset < bytes.Length; offset += ReadInt32(bytes, offset))
            if (ReadInt32(bytes, offset + 4) == command) offsets.Add(offset);
        return offsets.ToArray();
    }

    private sealed class CacheRasterVisual(object? content, PortableVisualState state, PortableRect bounds,
        params object[] children)
        : FakeVisual(content, state, children), IPortableVisualBoundsSource
    {
        internal PortableRect Bounds = bounds;
        bool IPortableVisualBoundsSource.TryGetPortableVisualBounds(out PortableVisualBounds value)
        {
            value = new() { HasDescendantBounds = true, DescendantBounds = Bounds };
            return true;
        }
    }
}
