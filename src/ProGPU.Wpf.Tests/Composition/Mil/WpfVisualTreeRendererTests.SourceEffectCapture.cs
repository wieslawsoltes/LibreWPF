using System;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfVisualTreeRendererTests
{
    [Fact]
    public void TypedShaderMapperKeepsOriginalDoubleEdgesInImplicitAndSamplerDescriptors()
    {
        byte[] bytecode = [0, 3, 0, 0, 163, 167, 173, 179];
        string key = RegisterSourceCaptureShader(bytecode);
        var bounds = new WpfReplayRect(Math.BitIncrement(16d), Math.BitDecrement(8d),
            Math.BitIncrement(32d), Math.BitDecrement(24d));
        double[] padding = [Math.BitIncrement(0.25d), 0.5d, Math.BitDecrement(0.75d), 1.25d];
        var expected = new Scene.ShaderEffectSourceCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            padding[0], padding[1], padding[2], padding[3]);
        var adapter = new SourceCaptureMetadataAdapter();
        object image = new(), brush = new(), firstOwner = new(), secondOwner = new();
        try
        {
            foreach (object owner in new[] { firstOwner, secondOwner })
            {
                var source = new FakePortableShaderEffectSource(PaddingDescriptor(bytecode,
                    [PortableShaderSampler.ImplicitInput(0, PortableShaderSamplingMode.NearestNeighbor),
                     PortableShaderSampler.Image(2, image, PortableShaderSamplingMode.Bilinear, brush)], padding));
                Assert.True(WpfEffectMapper.TryCreateProGpuEffect(source, out var mapped, adapter, bounds, owner));
                var effect = Assert.IsType<Scene.WpfShaderEffect>(mapped);
                Assert.True(effect.SourceCapture.HasValue);
                AssertSourceCaptureBits(expected, effect.SourceCapture.Value);
                Assert.True(adapter.Frame.SourceCapture.HasValue);
                AssertSourceCaptureBits(expected, adapter.Frame.SourceCapture.Value);
                Assert.Same(owner, adapter.Frame.Owner);
                Assert.Same(brush, adapter.Brush);
                Assert.Equal(2, adapter.Register);
                Assert.Equal(Scene.TextureSamplingMode.Linear, adapter.Mode);
                Assert.Equal(0, effect.Parameters.SourceTextureRegisterIndex);
                Assert.Equal(Scene.TextureSamplingMode.Nearest, effect.Parameters.SamplingMode);
                Assert.Equal(0f, effect.Padding);
                Assert.Equal(default, adapter.Frame.ContentBounds);
                Assert.Equal(0f, adapter.Frame.Padding);
                Assert.Single(effect.Parameters.Samplers).Dispose();
            }
            Assert.Equal(2, adapter.SourceCalls);
            Assert.Equal(0, adapter.LegacyCalls);
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void TypedShaderWithoutActualOwnerAndBoundsRejectsBeforeSamplerDependencies(bool hasOwner, bool hasBounds)
    {
        byte[] bytecode = [0, 3, 0, 0, 181, 191, 193, 197];
        string key = RegisterSourceCaptureShader(bytecode);
        var trap = new PaddingDependencyTrap();
        try
        {
            foreach (bool implicitOnly in new[] { true, false })
            {
                PortableShaderSampler sampler = implicitOnly
                    ? PortableShaderSampler.ImplicitInput(0, PortableShaderSamplingMode.NearestNeighbor)
                    : PortableShaderSampler.Image(1, trap, PortableShaderSamplingMode.Bilinear, trap);
                var source = new FakePortableShaderEffectSource(PaddingDescriptor(bytecode, [sampler], [0, 0, 0, 0]));
                Assert.False(WpfEffectMapper.TryCreateProGpuEffect(source, out var mapped, trap,
                    hasBounds ? new WpfReplayRect(8, 10, 32, 24) : null, hasOwner ? new object() : null));
                Assert.Null(mapped);
                Assert.Equal(0, trap.Reads);
            }
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Fact]
    public void TypedShaderSourceFrameDoesNotFallBackToLegacySamplerAdapter()
    {
        byte[] bytecode = [0, 3, 0, 0, 199, 211, 223, 227];
        string key = RegisterSourceCaptureShader(bytecode);
        var legacy = new LegacyCaptureMetadataAdapter();
        try
        {
            var source = new FakePortableShaderEffectSource(PaddingDescriptor(bytecode,
                [PortableShaderSampler.Image(1, new object(), PortableShaderSamplingMode.NearestNeighbor, new object())],
                [0.25, 0.5, 0.75, 1]));
            Assert.False(WpfEffectMapper.TryCreateProGpuEffect(source, out var mapped, legacy,
                new WpfReplayRect(8, 10, 32, 24), new object()));
            Assert.Null(mapped);
            Assert.Equal(0, legacy.LegacyCalls);
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Fact]
    public void ReceivingSourceAdapterForwardsHostDpiAndExactDescriptorWithoutScalarReconstruction()
    {
        var inner = new SourceCaptureMetadataAdapter();
        // Forwarding-only: the accepting adapter prevents any cache/device use.
        var unusedCache = (WpfShaderEffectSamplerTextureCache)RuntimeHelpers.GetUninitializedObject(
            typeof(WpfShaderEffectSamplerTextureCache));
        var receiving = new WpfShaderEffectSamplerImageSourceAdapter(inner, unusedCache, 1.75f);
        object owner = new(), brush = new();
        var source = new Scene.ShaderEffectSourceCapture(Math.BitIncrement(16d), 8, 32, 24,
            BitConverter.Int64BitsToDouble(long.MinValue), 0.5, Math.BitIncrement(0.75d), 1.25);
        var request = WpfShaderEffectSamplerFrame.FromSource(owner, source);

        Assert.True(receiving.TryAdaptSourceShaderEffectSamplerBrush(brush, 3, Scene.TextureSamplingMode.Nearest,
            request, out var sampler));
        Assert.Equal(1.75f, inner.Frame.DpiScale);
        Assert.Equal(1f, request.DpiScale);
        Assert.Same(owner, inner.Frame.Owner);
        AssertSourceCaptureBits(source, inner.Frame.SourceCapture!.Value);
        Assert.Equal(default, inner.Frame.ContentBounds);
        Assert.Equal(0f, inner.Frame.Padding);
        Assert.Equal(0, inner.LegacyCalls);
        sampler.Dispose();

        Assert.False(receiving.TryAdaptShaderEffectSamplerBrush(brush, 3, Scene.TextureSamplingMode.Nearest,
            request, out _));
        Assert.False(receiving.TryAdaptSourceShaderEffectSamplerBrush(brush, 3, Scene.TextureSamplingMode.Nearest,
            new WpfShaderEffectSamplerFrame(owner, new Scene.Rect(16, 8, 32, 24), 1.25f), out _));
        Assert.Equal(1, inner.SourceCalls);
    }

    private static string RegisterSourceCaptureShader(byte[] bytecode) =>
        WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }");

    [Fact]
    public void ResourceResolverForwardsOnlyExplicitSourceSamplerCapability()
    {
        var inner = new SourceCaptureMetadataAdapter();
        var resolver = new WpfResourceResolver(imageSourceAdapter: inner);
        object owner = new(), brush = new();
        var source = new Scene.ShaderEffectSourceCapture(Math.BitIncrement(8d), 10, 32, 24,
            0.25, 0.5, 0.75, 1.25);
        var request = WpfShaderEffectSamplerFrame.FromSource(owner, source) with { DpiScale = 1.75f };
        Assert.True(resolver.TryAdaptSourceShaderEffectSamplerBrush(brush, 4, Scene.TextureSamplingMode.Linear,
            request, out var sampler));
        AssertSourceCaptureBits(source, inner.Frame.SourceCapture!.Value);
        Assert.Equal(1.75f, inner.Frame.DpiScale);
        Assert.Same(owner, inner.Frame.Owner);
        Assert.Same(brush, inner.Brush);
        Assert.Equal(4, inner.Register);
        Assert.Equal(Scene.TextureSamplingMode.Linear, inner.Mode);
        sampler.Dispose();
        Assert.False(resolver.TryAdaptShaderEffectSamplerBrush(brush, 4, Scene.TextureSamplingMode.Linear,
            request, out _));
        Assert.Equal(0, inner.LegacyCalls);

        var legacy = new LegacyCaptureMetadataAdapter();
        var legacyResolver = new WpfResourceResolver(imageSourceAdapter: legacy);
        Assert.False(legacyResolver.TryAdaptSourceShaderEffectSamplerBrush(brush, 4, Scene.TextureSamplingMode.Linear,
            request, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(0, legacy.LegacyCalls);
    }

    private static void AssertSourceCaptureBits(Scene.ShaderEffectSourceCapture expected,
        Scene.ShaderEffectSourceCapture actual)
    {
        double[] left = [expected.X, expected.Y, expected.Width, expected.Height,
            expected.PaddingTop, expected.PaddingBottom, expected.PaddingLeft, expected.PaddingRight];
        double[] right = [actual.X, actual.Y, actual.Width, actual.Height,
            actual.PaddingTop, actual.PaddingBottom, actual.PaddingLeft, actual.PaddingRight];
        for (int index = 0; index < left.Length; index++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(left[index]), BitConverter.DoubleToInt64Bits(right[index]));
    }

    private class LegacyCaptureMetadataAdapter : IWpfImageSourceAdapter, IWpfShaderEffectSamplerBrushAdapter
    {
        public int LegacyCalls { get; private set; }
        public ImageSource? AdaptImageSource(object? value)
        {
            LegacyCalls++;
            throw new InvalidOperationException("Source capture must not use a legacy image adapter.");
        }
        public bool TryAdaptShaderEffectSamplerBrush(object? brush, int registerIndex, Scene.TextureSamplingMode mode,
            out Scene.WpfShaderEffectSampler sampler)
        {
            LegacyCalls++;
            throw new InvalidOperationException("Source capture must not use an unframed adapter.");
        }
        public bool TryAdaptShaderEffectSamplerBrush(object? brush, int registerIndex, Scene.TextureSamplingMode mode,
            WpfShaderEffectSamplerFrame frame, out Scene.WpfShaderEffectSampler sampler)
        {
            LegacyCalls++;
            throw new InvalidOperationException("Source capture must not use a scalar-framed adapter.");
        }
    }

    private sealed class SourceCaptureMetadataAdapter : LegacyCaptureMetadataAdapter, IWpfShaderEffectSamplerBrushAdapter
    {
        public int SourceCalls { get; private set; }
        public WpfShaderEffectSamplerFrame Frame { get; private set; }
        public object? Brush { get; private set; }
        public int Register { get; private set; }
        public Scene.TextureSamplingMode Mode { get; private set; }
        public bool TryAdaptSourceShaderEffectSamplerBrush(object? brush, int registerIndex, Scene.TextureSamplingMode mode,
            WpfShaderEffectSamplerFrame frame, out Scene.WpfShaderEffectSampler sampler)
        {
            SourceCalls++;
            Frame = frame;
            Brush = brush;
            Register = registerIndex;
            Mode = mode;
            // Metadata-only forwarding control, not a real texture capture.
            sampler = new(registerIndex, null, mode);
            return true;
        }
    }
}
