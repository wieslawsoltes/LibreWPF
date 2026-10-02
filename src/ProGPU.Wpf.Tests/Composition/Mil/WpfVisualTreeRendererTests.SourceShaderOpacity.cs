using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;
using SceneShaderEffect = ProGPU.Scene.WpfShaderEffect;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfVisualTreeRendererTests
{
    [Theory]
    [InlineData(0d)]
    [InlineData(0.5d)]
    public void SourceShaderCommandScopesCaptureOpacityAndMaskInsideEffect(double opacity)
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 61, 47, 19, 3 };
        const string constantShader = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return vec4<f32>(1.0, 0.0, 0.0, 1.0); }";
        var key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode, constantShader);
        try
        {
            var root = CreateSourceOpacityShaderVisual(bytecode, opacity);
            var sink = new TestSink { AcceptVisualEffects = true };

            var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[]
            {
                "PushNativeClip", "PushVisualEffect", "PushOpacity", "PushOpacityMask",
                "DrawRectangle", "Pop", "Pop", "Pop", "Pop"
            }, sink.Operations);
            Assert.Equal(opacity, Assert.Single(sink.Opacities));
            Assert.Same(Brushes.Transparent, Assert.Single(sink.OpacityMasks).OpacityMask);
            var effect = Assert.IsType<SceneShaderEffect>(Assert.Single(sink.VisualEffects));
            Assert.True(effect.CaptureSourceVisualOpacity);
            Assert.Equal(constantShader, effect.Parameters.ShaderSource);
            AssertReplayRect(12, 22, 20, 12, Assert.Single(sink.NativeClips));
            Assert.Equal(0, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(key);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceShaderRetainedStateKeepsZeroOpacityMaskClipAndCacheIdentity(bool cache)
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 61, 47, 19, 5 };
        const string shader = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return vec4<f32>(1.0); }";
        var key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode, shader);
        try
        {
            var root = CreateSourceOpacityShaderVisual(bytecode, 0, cache);
            var sink = new TestSink { AcceptRetainedVisualOwners = true };

            var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[]
            {
                "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop", "PopVisualOwner"
            }, sink.Operations);
            Assert.Same(root, Assert.Single(sink.VisualOwners));
            var state = Assert.Single(sink.RetainedVisualStates);
            Assert.True(Assert.IsType<SceneShaderEffect>(state.Effect).CaptureSourceVisualOpacity);
            Assert.Equal(0f, state.Opacity);
            Assert.Same(Brushes.Transparent, state.OpacityMask);
            Assert.Equal(cache, state.CacheAsLayer);
            Assert.Equal(new Vector2(10, 20), state.Offset);
            Assert.Equal(new Vector2(32, 16), state.Size);
            AssertReplayRect(10, 20, 32, 16, state.ContentBounds);
            AssertReplayRect(0, 0, 32, 16, state.OpacityMaskBounds);
            AssertReplayRect(2, 2, 20, 12, state.ClipBounds);
            Assert.Empty(sink.VisualEffects);
            Assert.Empty(sink.Opacities);
            Assert.Empty(sink.OpacityMasks);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(key);
        }
    }

    [Fact]
    public void SourceShaderRejectedCommandEffectKeepsBalancedOpacityScopesAndOneFailure()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 61, 47, 19, 7 };
        var key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }");
        try
        {
            var sink = new TestSink();
            var result = new WpfVisualTreeRenderer().ReplaySubtree(CreateSourceOpacityShaderVisual(bytecode, 0), sink);

            Assert.Equal(new[]
            {
                "PushNativeClip", "PushOpacity", "PushOpacityMask", "DrawRectangle", "Pop", "Pop", "Pop"
            }, sink.Operations);
            Assert.Empty(sink.VisualEffects);
            Assert.Equal(1, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(key);
        }
    }

    [Fact]
    public void NonShaderEffectRetainsOriginalOutputOpacityCommandOrdering()
    {
        var root = new FakePortableVisualStateDrawingVisual(CreateRenderData(Brushes.Green),
            CreateSourceOpacityState(new FakeBlurEffect(4), 0.5))
        {
            Bounds = new FakeRect(10, 20, 32, 16)
        };
        var sink = new TestSink { AcceptVisualEffects = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[]
        {
            "PushNativeClip", "PushOpacity", "PushOpacityMask", "PushVisualEffect",
            "DrawRectangle", "Pop", "Pop", "Pop", "Pop"
        }, sink.Operations);
        Assert.IsType<ProGPU.Scene.BlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    private static FakePortableVisualStateDrawingVisual CreateSourceOpacityShaderVisual(
        byte[] bytecode, double opacity, bool cache = false)
    {
        var source = new FakePortableShaderEffectSource(new PortableShaderEffect(
            effectTypeFullName: null, effectTypeName: null,
            pixelShader: new PortablePixelShader(null, null, bytecode, majorVersion: 3, minorVersion: 0),
            floatConstants: [],
            samplers: [PortableShaderSampler.ImplicitInput(0, PortableShaderSamplingMode.NearestNeighbor)],
            intConstantCount: 0, boolConstantCount: 0,
            paddingTop: 1, paddingBottom: 1, paddingLeft: 1, paddingRight: 1,
            ddxUvDdyUvRegisterIndex: -1));
        var state = CreateSourceOpacityState(source, opacity);
        state.HasCacheMode = cache;
        state.CacheMode = cache ? new object() : null;
        return new FakePortableVisualStateDrawingVisual(CreateRenderData(Brushes.Green), state)
        {
            Bounds = new FakeRect(10, 20, 32, 16)
        };
    }

    private static PortableVisualState CreateSourceOpacityState(object effect, double opacity) => new()
    {
        HasEffect = true, Effect = effect,
        HasOpacity = true, Opacity = opacity,
        HasOpacityMask = true, OpacityMask = Brushes.Transparent,
        HasClip = true, Clip = new PortableRectangleClipGeometry(12, 22, 20, 12)
    };
}
