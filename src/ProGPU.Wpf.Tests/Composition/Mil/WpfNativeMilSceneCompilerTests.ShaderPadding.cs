using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ShaderPaddingTransportsOriginalFourDoublesWithoutChangingSourceBounds(int family)
    {
        var effect = PaddingEffect(family);
        var compiler = CreateCacheShaderCompiler();
        var receiver = PaddingReceiver(effect);
        using var zero = compiler.BuildBatch(receiver, 64, 64);
        byte[] original = (byte[])zero.Bytes.Clone();
        double[] padding = [Math.BitIncrement(2d), 3, 4, Math.BitDecrement(5d)];
        SetPadding(effect, padding);
        using var padded = compiler.BuildBatch(receiver, 64, 64);
        int offset = FindCommand(padded.Bytes, 0x70);
        for (int axis = 0; axis < 4; ++axis)
            Assert.Equal(BitConverter.DoubleToInt64Bits(padding[axis]),
                BitConverter.DoubleToInt64Bits(ReadDouble(padded.Bytes, offset + 12 + axis * 8)));
        var delta = WpfNativeMilCompilationSession.CreateDelta(zero, padded);
        Assert.False(delta.RequiresRebuild);
        Assert.Equal([0x70], ReadCommands(delta.Bytes));
        Assert.Equal(zero.DrawingImageBounds.ToArray(), padded.DrawingImageBounds.ToArray());
        Assert.Equal(zero.VisualCacheBounds.ToArray(), padded.VisualCacheBounds.ToArray());
        Assert.Equal(zero.BitmapCacheRasterPolicies.ToArray(), padded.BitmapCacheRasterPolicies.ToArray());
        Assert.Equal(original, zero.Bytes);
        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        SetPadding(effect, [negativeZero, negativeZero, negativeZero, negativeZero]);
        using var signedZero = compiler.BuildBatch(receiver, 64, 64);
        int signedOffset = FindCommand(signedZero.Bytes, 0x70);
        for (int axis = 0; axis < 4; ++axis)
            Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(
                ReadDouble(signedZero.Bytes, signedOffset + 12 + axis * 8)));
        SetPadding(effect, [0, 0, 0, 0]);
        using var reset = compiler.BuildBatch(receiver, 64, 64);
        Assert.Equal(original, reset.Bytes); // existing zero-padding overload bytes unchanged
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ShaderPaddingRejectsLaterInvalidAxisWithoutChangingEarlierCandidate(int axis)
    {
        var effect = PaddingEffect(1);
        SetPadding(effect, [2, 3, 4, 5]);
        var compiler = CreateCacheShaderCompiler();
        var first = PaddingReceiver(effect);
        using var previous = compiler.BuildBatch(first, 64, 64);
        byte[] original = (byte[])previous.Bytes.Clone();
        var bad = PaddingEffect(2);
        var root = new FakeVisual(null, null, first, PaddingReceiver(bad));
        foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue })
        {
            double[] padding = [2, 3, 4, 5];
            padding[axis] = value;
            SetPadding(bad, padding);
            Assert.Contains("padding", Assert.Throws<NotSupportedException>(() =>
                compiler.BuildBatch(root, 64, 64)).Message);
            Assert.Equal(original, previous.Bytes);
        }
        SetPadding(bad, [2, 3, 4, 5]);
        using var restored = compiler.BuildBatch(root, 64, 64);
        Assert.Equal(2, ReadCommands(restored.Bytes).Count(command => command == 0x70));
        Assert.Equal(original, previous.Bytes);
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative, 0)]
    [InlineData(NativeMilBackend.Dawn, 0)]
    [InlineData(NativeMilBackend.WgpuNative, 1)]
    [InlineData(NativeMilBackend.Dawn, 1)]
    [InlineData(NativeMilBackend.WgpuNative, 2)]
    [InlineData(NativeMilBackend.Dawn, 2)]
    public void ShaderPaddingSessionMutatesResetsAndRetainsOriginalGenerations(NativeMilBackend backend, int family)
    {
        var effect = PaddingEffect(family);
        var compiler = CreateCacheShaderCompiler();
        var receiver = PaddingReceiver(effect);
        using var session = new WpfNativeMilCompilationSession(backend, compiler);
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var original = session.CompileFrame(11961, 1, 0, 1);
        byte[] originalBytes = original.Scene.Stream.ToArray();
        SetPadding(effect, [2, 3, 4, 5]);
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);
        var padded = session.CompileFrame(11961, 2, 0, 2);
        byte[] paddedBytes = padded.Scene.Stream.ToArray();
        Assert.NotEqual(originalBytes, paddedBytes);
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);
        Assert.Equal(paddedBytes, session.CompileFrame(11961, 2, 0, 2).Scene.Stream.ToArray());
        for (int axis = 0; axis < 4; ++axis)
        {
            foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue })
            {
                double[] invalid = [2, 3, 4, 5];
                invalid[axis] = value;
                SetPadding(effect, invalid);
                Assert.Throws<NotSupportedException>(() => session.Update(receiver, 64, 64));
                Assert.True(session.IsInitialized);
                Assert.Equal(paddedBytes, session.CompileFrame(11961, 2, 0, 2).Scene.Stream.ToArray());
            }
        }
        SetPadding(effect, [0, 0, 0, 0]);
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);
        Assert.NotEmpty(session.CompileFrame(11961, 3, 0, 3).Scene.Stream);
        Assert.Equal(originalBytes, original.Scene.Stream.ToArray());
        Assert.Equal(paddedBytes, padded.Scene.Stream.ToArray());
    }

    private static void SetPadding(ShaderEffectSource effect, double[] padding)
    {
        effect.Padding = padding[0]; effect.PaddingBottom = padding[1];
        effect.PaddingLeft = padding[2]; effect.PaddingRight = padding[3];
    }

    private static ShaderEffectSource PaddingEffect(int family)
    {
        var effect = new ShaderEffectSource(new()) { Ddx = 7 };
        if (family == 1)
        {
            var drawing = new FakeGeometryDrawing(new FakeBrush(new(255, 0, 255, 0)), null,
                new FakePrimitiveGeometry(PortablePrimitiveGeometry.Rectangle(
                    new(10, 20, 8, 6), 0, 0, PortableMatrix3x2.Identity)));
            var image = new FakeDrawingImage(drawing);
            var brush = new ShaderImageBrush(image);
            effect.Samplers = [PortableShaderSampler.Image(0, image, PortableShaderSamplingMode.NearestNeighbor, brush)];
        }
        else if (family == 2)
        {
            var source = new SamplerVisual(new FakeRenderData(CreateRectangleRecord(1, 0),
                [new FakeBrush(new(255, 0, 0, 255))]));
            effect.Samplers = [new(0, new ShaderCacheBrush(new(source)), PortableShaderSamplingMode.NearestNeighbor)];
        }
        return effect;
    }

    private static CacheRasterVisual PaddingReceiver(ShaderEffectSource effect)
    {
        byte[] record = CreateRectangleRecord(1, 0);
        WriteDouble(record, 8, 16); WriteDouble(record, 16, 16);
        WriteDouble(record, 24, 20); WriteDouble(record, 32, 12);
        return new(new FakeRenderData(record, [new FakeBrush(new(255, 255, 255, 255))]),
            new() { HasEffect = true, Effect = effect }, new(16, 16, 20, 12));
    }
}
