using System.Runtime.CompilerServices;
using System.Windows;
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
    [InlineData(false)]
    [InlineData(true)]
    public void CacheSamplerUsesRawSourceAdapterWithoutReceivingOwnerFrameEvenWhenDisconnected(bool disconnected)
    {
        byte[] bytecode = [0, 3, 0, 0, 41, 43, 47, 53];
        string key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "original_raw_cache_brush_sampler");
        // A forwarding-only control: this texture is never created or rendered.
        var texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));
        var brush = new CaptureCacheSource(new(disconnected ? null :
            new FakeDrawingVisual(CreateRenderData(Brushes.Red)) { Bounds = new Rect(10, 20, 8, 6) },
            new CaptureCache(new(2, true, false)), Opacity: 0.5));
        var adapter = new FakeShaderSamplerBrushAdapter(texture);
        var source = new FakePortableShaderEffectSource(new PortableShaderEffect(null, null,
            new PortablePixelShader(null, null, bytecode, 3, 0), [],
            [new PortableShaderSampler(2, brush, PortableShaderSamplingMode.NearestNeighbor)],
            0, 0, 0, 0, 0, 0, -1));
        var first = new FakePortableVisualStateVisual(CreatePortableEffectState(source)) { Bounds = new Rect(8, 10, 32, 24) };
        var second = new FakePortableVisualStateVisual(CreatePortableEffectState(source)) { Bounds = new Rect(3, 4, 64, 48) };
        try
        {
            var renderer = new WpfVisualTreeRenderer();
            foreach (var owner in new[] { first, second })
            {
                var sink = new TestSink { AcceptVisualEffects = true };
                var result = renderer.ReplaySubtree(owner, sink, imageSourceAdapter: adapter);
                Assert.Equal(0, result.UnsupportedVisualStateCount);
                Assert.Same(brush, adapter.LastSamplerBrush);
                Assert.Equal(2, adapter.LastSamplerRegisterIndex);
                Assert.Equal(Scene.TextureSamplingMode.Nearest, adapter.LastSamplerMode);
                var effect = Assert.IsType<Scene.WpfShaderEffect>(Assert.Single(sink.VisualEffects));
                Assert.Same(texture, Assert.Single(effect.Parameters.Samplers).Texture);
            }
            Assert.Empty(adapter.Frames);
            var imageOnly = new FakeImageSourceAdapter(null);
            var rejected = new TestSink { AcceptVisualEffects = true };
            Assert.Equal(1, renderer.ReplaySubtree(first, rejected,
                imageSourceAdapter: imageOnly).UnsupportedVisualStateCount);
            Assert.Empty(rejected.VisualEffects);
            Assert.Null(imageOnly.LastImageSource);
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Fact]
    public void DisconnectedCachePaintRetainsSourceRectangleWithoutPaintingOrCachePage()
    {
        var sink = new NullVisualScopeSink();
        var source = new CaptureCacheSource(new(null, new CaptureCache(new(2, true, false)), Opacity: 0.5));
        var result = WpfDrawingReplay.ReplayBitmapCacheBrushRectangleFill(source,
            new Rect(8, 10, 32, 24), sink, null);
        Assert.Equal(WpfDrawingReplayStatus.Applied, result);
        Assert.Equal(new WpfReplayRect(8, 10, 32, 24), Assert.Single(sink.SourceRectangles));
        Assert.Equal(new[] { "PushSourceRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.DrawRectangles);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.VisualCacheBounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheSamplerWithoutActualRasterPolicyRejectsBeforeTextureCreation(bool disconnected)
    {
        // No policy/device/compositor/cache tables exist. Missing actual primary
        // display/device policy must fail before reaching any GPU/cache work;
        // receiving-frame absence is not the reason for rejecting a raw sampler.
        var cache = (WpfShaderEffectSamplerTextureCache)RuntimeHelpers.GetUninitializedObject(
            typeof(WpfShaderEffectSamplerTextureCache));
        var brush = new CaptureCacheSource(new(disconnected ? null :
            new FakeDrawingVisual(CreateRenderData(Brushes.Red)) { Bounds = new Rect(10, 20, 8, 6) },
            new CaptureCache(new(2, true, false)), Opacity: 0.5));
        Assert.Throws<NotSupportedException>(() => cache.TryCreateSampler(brush, 2,
            Scene.TextureSamplingMode.Nearest, null, out _));
        Assert.Equal(1, brush.Reads);
    }

    private sealed class CaptureCacheSource(PortableBitmapCacheBrush descriptor) : IPortableBitmapCacheBrushSource
    {
        public int Reads { get; private set; }
        public bool TryGetPortableBitmapCacheBrush(out PortableBitmapCacheBrush value)
        {
            Reads++;
            value = descriptor;
            return true;
        }
    }
}
