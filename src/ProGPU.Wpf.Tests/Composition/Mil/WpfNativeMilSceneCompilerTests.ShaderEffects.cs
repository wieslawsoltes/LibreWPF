using System.Buffers.Binary;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(PortableShaderRenderMode.Auto, PortableShaderSamplingMode.Auto, 0U)]
    [InlineData(PortableShaderRenderMode.HardwareOnly, PortableShaderSamplingMode.NearestNeighbor, 1U)]
    [InlineData(PortableShaderRenderMode.Auto, PortableShaderSamplingMode.Bilinear, 2U)]
    public void ShaderBatchPreservesOriginalBytesIntentDenseHolesAndSampler(
        PortableShaderRenderMode mode, PortableShaderSamplingMode sampling, uint wireSampling)
    {
        var pixel = new ShaderSource { Mode = mode };
        var effect = new ShaderEffectSource(pixel)
        {
            Constants = [0, 0, 0, 0, 0.25f, -0.5f, 2, 1],
            Samplers = [PortableShaderSampler.ImplicitInput(3, sampling)], Ddx = 7
        };
        using var batch = ShaderBatch(effect);
        int shader = FindCommand(batch.Bytes, 0x6c);
        Assert.Equal((uint)mode, ReadUInt32(batch.Bytes, shader + 12));
        Assert.Equal((uint)pixel.Bytes.Length, ReadUInt32(batch.Bytes, shader + 16));
        Assert.Equal(1U, ReadUInt32(batch.Bytes, shader + 20));
        Assert.Equal(pixel.Bytes, batch.Bytes.AsSpan(shader + 24, pixel.Bytes.Length).ToArray());
        int packet = FindCommand(batch.Bytes, 0x70);
        Assert.Equal(7U, ReadUInt32(batch.Bytes, packet + 48));
        Assert.Equal(4U, ReadUInt32(batch.Bytes, packet + 52));
        Assert.Equal(32U, ReadUInt32(batch.Bytes, packet + 56));
        Assert.Equal(0, ReadUInt16(batch.Bytes, packet + 84));
        Assert.Equal(1, ReadUInt16(batch.Bytes, packet + 86));
        for (int i = 0; i < effect.Constants.Length; ++i)
            Assert.Equal(effect.Constants[i], ReadSingle(batch.Bytes, packet + 88 + 4 * i));
        Assert.Equal(3U, ReadUInt32(batch.Bytes, packet + 120));
        Assert.Equal(wireSampling, ReadUInt32(batch.Bytes, packet + 124));
        Assert.Contains(0x6d, ReadCommands(batch.Bytes));
        Assert.Single(batch.VisualCacheBounds!);
    }

    [Fact]
    public void ShaderImageSamplerKeepsBrushMappingOpacityTransformsAndPixels()
    {
        var image = new FakeBitmapSource(new(2, 1, 96, 96, 8, PortablePixelDataFormat.Bgra32,
            [255, 0, 0, 255, 0, 255, 0, 255]));
        var brush = new ShaderImageBrush(image);
        var effect = new ShaderEffectSource(new())
        { Samplers = [PortableShaderSampler.Image(0, image, PortableShaderSamplingMode.Bilinear, brush)] };
        using var before = ShaderBatch(effect);
        Assert.Single(before.BitmapSources!);
        int imagePacket = FindCommand(before.Bytes, 0x81);
        Assert.Equal(0.5, ReadDouble(before.Bytes, imagePacket + 12));
        Assert.Equal(0.5, ReadDouble(before.Bytes, imagePacket + 36));
        Assert.NotEqual(0U, ReadUInt32(before.Bytes, imagePacket + 104));
        Assert.Contains(0x77, ReadCommands(before.Bytes));
        byte[] snapshot = (byte[])before.Bytes.Clone();
        brush.Opacity = 0.25;
        using var after = ShaderBatch(effect);
        var delta = WpfNativeMilCompilationSession.CreateDelta(before, after);
        Assert.False(delta.RequiresRebuild);
        Assert.Equal([0x81], ReadCommands(delta.Bytes));
        Assert.Equal(snapshot, before.Bytes);
    }

    [Fact]
    public void ShaderMutationsUseOwnedPacketDeltasAndSharedSourceIdentity()
    {
        var pixel = new ShaderSource();
        var effect = new ShaderEffectSource(pixel) { Constants = [1, 0, 0, 1] };
        using var before = ShaderBatch(effect);
        byte[] immutable = (byte[])before.Bytes.Clone();
        effect.Constants[0] = 0.5f;
        using var constants = ShaderBatch(effect);
        Assert.Equal([0x70], ReadCommands(WpfNativeMilCompilationSession.CreateDelta(before, constants).Bytes));
        pixel.Mode = PortableShaderRenderMode.HardwareOnly;
        using var mode = ShaderBatch(effect);
        Assert.Equal([0x6c], ReadCommands(WpfNativeMilCompilationSession.CreateDelta(constants, mode).Bytes));
        pixel.Bytes[^8] ^= 1; // raw transport must capture changed bytes, not a URI or DTO reference.
        using var changed = ShaderBatch(effect);
        Assert.Equal([0x6c], ReadCommands(WpfNativeMilCompilationSession.CreateDelta(mode, changed).Bytes));
        Assert.Equal(immutable, before.Bytes);
        var secondEffect = new ShaderEffectSource(pixel);
        using var shared = new WpfNativeMilSceneCompiler().BuildBatch(new FakeVisual(null, null,
            ShaderVisual(effect), ShaderVisual(secondEffect)), 64, 64);
        Assert.Equal(1, ReadCommands(shared.Bytes).Count(command => command == 0x6c));
        Assert.Equal(2, ReadCommands(shared.Bytes).Count(command => command == 0x70));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    public void ShaderUnsupportedSourceCannotPublishOrChangePreviousBatch(int invalid)
    {
        var pixel = new ShaderSource();
        var effect = new ShaderEffectSource(pixel);
        using var previous = ShaderBatch(effect);
        byte[] snapshot = (byte[])previous.Bytes.Clone();
        switch (invalid)
        {
            case 0: pixel.Mode = null; break;
            case 1: pixel.Mode = PortableShaderRenderMode.SoftwareOnly; break;
            case 2: pixel.Mode = (PortableShaderRenderMode)99; break;
            case 3: pixel.PublishSource = false; break;
            case 4: effect.IntCount = 1; break;
            case 5: effect.BoolCount = 1; break;
            case 6: effect.Padding = 1; break;
            case 7: effect.Samplers = []; break;
            case 8: effect.Samplers = [effect.Samplers[0], effect.Samplers[0]]; break;
            case 9: effect.Samplers = [PortableShaderSampler.Image(0, new object(), PortableShaderSamplingMode.Auto)]; break;
            case 10: effect.Samplers = [new(0, new FakeBrush(new(255, 1, 2, 3)), PortableShaderSamplingMode.Auto)]; break;
            case 11: effect.Samplers = [PortableShaderSampler.ImplicitInput(0, (PortableShaderSamplingMode)99)]; break;
            case 12: effect.Constants = [1, 2, 3]; break;
            case 13: pixel.Major = 3; break;
        }
        if (invalid == 3)
            Assert.Throws<InvalidOperationException>(() => ShaderBatch(effect));
        else
            Assert.Throws<NotSupportedException>(() => ShaderBatch(effect));
        Assert.Equal(snapshot, previous.Bytes);
    }

    [Fact]
    public void ConflictingSnapshotsOfOneLiveShaderRejectTheEntireBatch()
    {
        var pixel = new ShaderSource();
        var first = new ShaderEffectSource(pixel)
        { AfterCapture = () => pixel.Mode = PortableShaderRenderMode.HardwareOnly };
        var second = new ShaderEffectSource(pixel);
        Assert.Contains("changed during native batch capture", Assert.Throws<InvalidOperationException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(new FakeVisual(null, null,
                ShaderVisual(first), ShaderVisual(second)), 64, 64)).Message);
    }

    [Fact]
    public void ShaderDependenciesObserveLivePixelShaderAndOriginalImageBrush()
    {
        var pixel = new ShaderSource();
        var image = new FakeBitmapSource(new(1, 1, 96, 96, 4, PortablePixelDataFormat.Bgra32, [255, 0, 0, 255]));
        var brush = new ShaderImageBrush(image);
        var effect = new ShaderEffectSource(pixel)
        { Samplers = [PortableShaderSampler.Image(0, image, PortableShaderSamplingMode.Auto, brush)] };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(ShaderVisual(effect));
        Assert.True(tracker.ConsumeDirty());
        pixel.Invalidate();
        Assert.Contains(pixel, tracker.DirtySources);
        Assert.True(tracker.ConsumeDirty());
        brush.Invalidate();
        Assert.Contains(brush, tracker.DirtySources);
        Assert.True(tracker.ConsumeDirty());
        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(effect);
        Assert.Contains(pixel, dependencies);
        Assert.Contains(brush, dependencies);
        Assert.Contains(image, dependencies);
        Assert.DoesNotContain(dependencies, value => value is PortablePixelShader);
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative)]
    [InlineData(NativeMilBackend.Dawn)]
    public void ShaderSessionRetainsGenerationAfterRejectedSourceAndSupportsUpdates(NativeMilBackend backend)
    {
        var pixel = new ShaderSource();
        var effect = new ShaderEffectSource(pixel);
        using var session = new WpfNativeMilCompilationSession(backend);
        Assert.True(session.Update(ShaderVisual(effect), 64, 64).RecreatedChannel);
        var first = session.CompileFrame(9871, 1, 0, 1);
        byte[] retained = first.Scene.Stream.ToArray();
        Assert.False(session.Update(ShaderVisual(effect), 64, 64).RecreatedChannel);
        pixel.Mode = PortableShaderRenderMode.SoftwareOnly;
        Assert.Throws<NotSupportedException>(() => session.Update(ShaderVisual(effect), 64, 64));
        Assert.True(session.IsInitialized);
        Assert.Equal(retained, session.CompileFrame(9871, 1, 0, 1).Scene.Stream.ToArray());
        pixel.Mode = PortableShaderRenderMode.HardwareOnly;
        Assert.False(session.Update(ShaderVisual(effect), 64, 64).RecreatedChannel);
        Assert.NotEmpty(session.CompileFrame(9871, 2, 1, 2).Scene.Stream);
        Assert.Equal(retained, first.Scene.Stream.ToArray());
    }

    private static FakeVisual ShaderVisual(ShaderEffectSource effect)
    {
        byte[] content = CreateRectangleRecord(1, 0);
        // The retained effect frame must describe the actual drawable content.
        WriteDouble(content, 8, 1);
        WriteDouble(content, 16, 2);
        WriteDouble(content, 24, 30);
        WriteDouble(content, 32, 20);
        return new(new FakeRenderData(content, [new FakeBrush(new(255, 100, 150, 200))]),
            new PortableVisualState { HasEffect = true, Effect = effect });
    }

    private static WpfNativeMilBatch ShaderBatch(ShaderEffectSource effect) =>
        new WpfNativeMilSceneCompiler().BuildBatch(ShaderVisual(effect), 64, 64);

    private sealed class ShaderSource : IPortablePixelShaderSource, IPortableInvalidationSource
    {
        internal byte[] Bytes = ShaderBytes();
        internal PortableShaderRenderMode? Mode = PortableShaderRenderMode.Auto;
        internal short Major = 2;
        internal bool PublishSource = true;
        private event EventHandler? Invalidated;
        internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);
        public bool TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)
        { Invalidated += handler; subscription = new PortableInvalidationSubscription(() => Invalidated -= handler); return true; }
        public bool TryGetPortablePixelShader(out PortablePixelShader shader)
        { shader = new(null, null, Bytes, Major, 0) { RenderMode = Mode, Source = PublishSource ? this : null }; return true; }
    }

    private sealed class ShaderEffectSource(ShaderSource pixel) : IPortableShaderEffectSource
    {
        internal float[] Constants = [];
        internal PortableShaderSampler[] Samplers = [PortableShaderSampler.ImplicitInput(0, PortableShaderSamplingMode.Auto)];
        internal uint IntCount, BoolCount;
        internal double Padding;
        internal int Ddx = -1;
        internal Action? AfterCapture;
        public bool TryGetPortableShaderEffect(out PortableShaderEffect effect)
        {
            pixel.TryGetPortablePixelShader(out var shader);
            effect = new(null, null, shader, Constants, Samplers, IntCount, BoolCount, Padding, 0, 0, 0, Ddx);
            AfterCapture?.Invoke();
            return true;
        }
    }

    private sealed class ShaderImageBrush(object image) : IPortableTileBrushSource, IPortableInvalidationSource
    {
        internal double Opacity = 0.5;
        private event EventHandler? Invalidated;
        internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);
        public bool TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)
        { Invalidated += handler; subscription = new PortableInvalidationSubscription(() => Invalidated -= handler); return true; }
        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = new(PortableTileBrushKind.Image, image, Opacity, new(0, 0, 0.5, 1), new(0, 0, 1, 1),
                PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
                PortableTileMode.Tile, PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
                true, new(1, 0, 0, 1, 7, 8), false, default);
            return true;
        }
    }

    private static byte[] ShaderBytes()
    {
        uint[] words = [0xffff0200, 0x0200001f, 0x80000000, 0xb0030000,
            0x0200001f, 0x90000000, 0xa00f0800, 0x03000042, 0x800f0000, 0xb0e40000,
            0xa0e40800, 0x02000001, 0x800f0800, 0x80e40000, 0x0000ffff];
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; ++i) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 * i), words[i]);
        return bytes;
    }
}
