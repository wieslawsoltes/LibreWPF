using System;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfVisualTreeRendererTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidRawShaderPaddingRejectsBeforeAnySamplerDependency(int axis)
    {
        byte[] bytecode = [0, 3, 0, 0, 101, 103, 107, 109];
        string key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "shader_padding_preflight");
        var dependency = new PaddingDependencyTrap();
        PortableShaderSampler[][] samplerCases =
        [
            [PortableShaderSampler.ImplicitInput(0, PortableShaderSamplingMode.NearestNeighbor)],
            [PortableShaderSampler.Image(1, dependency, PortableShaderSamplingMode.NearestNeighbor)],
            [new PortableShaderSampler(1, dependency, PortableShaderSamplingMode.NearestNeighbor)],
            [PortableShaderSampler.Image(1, dependency, PortableShaderSamplingMode.NearestNeighbor, dependency)],
        ];
        try
        {
            // The tiny negative double would narrow to negative zero. It must
            // still reject from the original value, before narrowing/clamping.
            foreach (double invalid in new[] { -1d, -double.Epsilon, double.NaN,
                double.PositiveInfinity, double.NegativeInfinity, double.MaxValue })
            {
                double[] padding = [0.25, 0.5, 0.75, 1];
                padding[axis] = invalid;
                foreach (PortableShaderSampler[] samplers in samplerCases)
                {
                    var descriptor = PaddingDescriptor(bytecode, samplers, padding);
                    var source = new FakePortableShaderEffectSource(descriptor);
                    // Root producer must retain raw DTO values; this is not a
                    // test-only reflection bypass of an old sanitizer.
                    double[] retained = [descriptor.PaddingTop, descriptor.PaddingBottom,
                        descriptor.PaddingLeft, descriptor.PaddingRight];
                    Assert.Equal(BitConverter.DoubleToInt64Bits(invalid), BitConverter.DoubleToInt64Bits(retained[axis]));
                    Assert.False(WpfEffectMapper.TryCreateProGpuEffect(source, out var mapped,
                        dependency, new WpfReplayRect(8, 10, 32, 24), source));
                    Assert.Null(mapped);
                    Assert.False(WpfEffectMapper.TryCreateProGpuPushEffect(source, null, out mapped,
                        dependency, new WpfReplayRect(8, 10, 32, 24), source));
                    Assert.Null(mapped);
                    Assert.Equal(0, dependency.Reads);
                }
            }
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ValidAsymmetricPaddingUsesExactSourceFrameWithoutScalarMaximum(int maximumAxis)
    {
        byte[] bytecode = [0, 3, 0, 0, 113, 127, 131, 137];
        string key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "shader_padding_source_frame");
        // Existing forwarding-only seam: never create, render or dispose this
        // uninitialized borrowed texture as if it were a real device resource.
        var texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));
        var adapter = new FakeShaderSamplerBrushAdapter(texture);
        object image = new(), brush = new(), owner = new();
        double[] padding = [0.25, 0.5, 0.75, 1];
        padding[maximumAxis] = 2.75;
        var descriptor = PaddingDescriptor(bytecode,
            [PortableShaderSampler.Image(2, image, PortableShaderSamplingMode.Bilinear, brush)], padding);
        try
        {
            Assert.True(WpfEffectMapper.TryCreateProGpuEffect(new FakePortableShaderEffectSource(descriptor),
                out var mapped, adapter, new WpfReplayRect(8, 10, 32, 24), owner));
            var effect = Assert.IsType<Scene.WpfShaderEffect>(mapped);
            Assert.Equal(0f, effect.Padding);
            Assert.True(effect.CaptureSourceVisualOpacity);
            var frame = Assert.Single(adapter.Frames);
            Assert.Same(owner, frame.Owner);
            var expected = new Scene.ShaderEffectSourceCapture(8, 10, 32, 24,
                padding[0], padding[1], padding[2], padding[3]);
            Assert.Equal(expected, effect.SourceCapture);
            Assert.Equal(expected, frame.SourceCapture);
            Assert.Equal(default, frame.ContentBounds);
            Assert.Equal(0f, frame.Padding);
            Assert.Same(brush, adapter.LastSamplerBrush);
            var sampler = Assert.Single(effect.Parameters.Samplers);
            Assert.Same(texture, sampler.Texture);
            Assert.Equal(Scene.TextureSamplingMode.Linear, adapter.LastSamplerMode);
            sampler.Dispose();
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Fact]
    public void NegativeZeroAndFiniteFloatBoundaryRemainValidMapperMetadata()
    {
        byte[] bytecode = [0, 3, 0, 0, 139, 149, 151, 157];
        string key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "shader_padding_finite_boundary");
        try
        {
            double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
            foreach (double valid in new[] { negativeZero, (double)float.MaxValue })
            {
                var descriptor = PaddingDescriptor(bytecode,
                    [PortableShaderSampler.ImplicitInput(0, PortableShaderSamplingMode.NearestNeighbor)],
                    [valid, valid, valid, valid]);
                Assert.Equal(BitConverter.DoubleToInt64Bits(valid), BitConverter.DoubleToInt64Bits(descriptor.PaddingTop));
                Assert.True(WpfEffectMapper.TryCreateProGpuEffect(new FakePortableShaderEffectSource(descriptor),
                    out var mapped, effectBounds: new WpfReplayRect(8, 10, 32, 24), effectOwner: new object()));
                var effect = Assert.IsType<Scene.WpfShaderEffect>(mapped);
                Assert.Equal(0f, effect.Padding);
                Assert.True(effect.SourceCapture.HasValue);
                Assert.Equal(BitConverter.DoubleToInt64Bits(valid),
                    BitConverter.DoubleToInt64Bits(effect.SourceCapture.Value.PaddingTop));
                Assert.Empty(effect.Parameters.Samplers);
            }
            // Mapping metadata does not prove that an enormous capture can be
            // allocated. Device/raster admission remains in the existing path.
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    private static PortableShaderEffect PaddingDescriptor(byte[] bytecode,
        PortableShaderSampler[] samplers, double[] padding) =>
        new(null, null, new PortablePixelShader(null, null, bytecode, 3, 0), [], samplers,
            0, 0, padding[0], padding[1], padding[2], padding[3], -1);

    private sealed class PaddingDependencyTrap : IWpfImageSourceAdapter,
        IWpfShaderEffectSamplerBrushAdapter, IPortableTileBrushSource
    {
        internal int Reads { get; private set; }
        private InvalidOperationException UnexpectedRead()
        {
            Reads++;
            return new InvalidOperationException("Invalid shader padding reached a sampler dependency.");
        }
        public ImageSource? AdaptImageSource(object? imageSource) => throw UnexpectedRead();
        public bool TryGetPortableTileBrush(out PortableTileBrush brush) => throw UnexpectedRead();
        public bool TryAdaptSourceShaderEffectSamplerBrush(object? brush, int registerIndex,
            Scene.TextureSamplingMode samplingMode, WpfShaderEffectSamplerFrame frame,
            out Scene.WpfShaderEffectSampler sampler) => throw UnexpectedRead();
        public bool TryAdaptShaderEffectSamplerBrush(object? brush, int registerIndex,
            Scene.TextureSamplingMode samplingMode, out Scene.WpfShaderEffectSampler sampler) => throw UnexpectedRead();
        public bool TryAdaptShaderEffectSamplerBrush(object? brush, int registerIndex,
            Scene.TextureSamplingMode samplingMode, WpfShaderEffectSamplerFrame frame,
            out Scene.WpfShaderEffectSampler sampler) => throw UnexpectedRead();
    }
}
