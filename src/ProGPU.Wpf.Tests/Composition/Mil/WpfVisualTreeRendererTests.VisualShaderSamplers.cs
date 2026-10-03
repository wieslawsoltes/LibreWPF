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
    public void VisualSamplerRequiresActualReceivingOwnerFrameEvenWhenDisconnected(bool disconnected)
    {
        byte[] bytecode = [0, 3, 0, 0, 23, 27, 31, 35];
        string key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "original_visual_brush_frames");
        // Pure forwarding control, not a texture-creation/rendering assertion.
        var texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));
        var brush = new SourceVisualTile(disconnected ? null :
            new FakeDrawingVisual(CreateRenderData(Brushes.Red)) { Bounds = new(10, 20, 8, 6) });
        var adapter = new FakeShaderSamplerBrushAdapter(texture);
        var source = new FakePortableShaderEffectSource(new PortableShaderEffect(null, null,
            new PortablePixelShader(null, null, bytecode, 3, 0), [],
            [new PortableShaderSampler(2, brush, PortableShaderSamplingMode.Bilinear)],
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
                var effect = Assert.IsType<Scene.WpfShaderEffect>(Assert.Single(sink.VisualEffects));
                Assert.Same(texture, Assert.Single(effect.Parameters.Samplers).Texture);
            }
            Assert.Equal(2, adapter.Frames.Count);
            Assert.Same(first, adapter.Frames[0].Owner);
            Assert.Same(second, adapter.Frames[1].Owner);
            Assert.Equal(new Scene.Rect(8, 10, 32, 24), adapter.Frames[0].ContentBounds);
            Assert.Equal(new Scene.Rect(3, 4, 64, 48), adapter.Frames[1].ContentBounds);
            var rejected = new TestSink { AcceptVisualEffects = true };
            Assert.Equal(1, renderer.ReplaySubtree(first, rejected,
                imageSourceAdapter: new FakeImageSourceAdapter(null)).UnsupportedVisualStateCount);
            Assert.Empty(rejected.VisualEffects);
        }
        finally { WpfShaderEffectRegistry.Unregister(key); }
    }

    [Fact]
    public void DisconnectedVisualPaintRetainsSourceHitScopeWithoutDrawingCommands()
    {
        var sink = new NullVisualScopeSink();
        Assert.True(WpfDrawingReplay.TryReplayTileBrushFill(new SourceVisualTile(null),
            new RectangleGeometry(new Rect(8, 10, 32, 24)), sink, null, out var status));
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
        Assert.Equal(new WpfReplayRect(8, 10, 32, 24), Assert.Single(sink.SourceRectangles));
        Assert.Equal(new[] { "PushSourceRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.DrawRectangles);
        Assert.Empty(sink.DrawGeometries);
    }

    private sealed class SourceVisualTile(object? visual) : IPortableTileBrushSource
    {
        public bool TryGetPortableTileBrush(out PortableTileBrush tile)
        {
            tile = PortableTileBrush.Visual(visual, 1, new(0, 0, 1, 1), new(0, 0, 1, 1),
                PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
                PortableTileMode.None, PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
                false, default, false, default);
            return true;
        }
    }

    private sealed class NullVisualScopeSink : TestSink, IWpfSourceRectangleHitTestScopeCommandSink
    {
        internal List<WpfReplayRect> SourceRectangles { get; } = [];
        public void PushSourceRectangleHitTestScope(WpfReplayRect rectangle)
        {
            SourceRectangles.Add(rectangle);
            Operations.Add("PushSourceRectangle");
        }
    }
}
