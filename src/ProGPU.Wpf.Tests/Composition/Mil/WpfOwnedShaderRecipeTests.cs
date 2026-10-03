using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using Xunit;
using SceneDrawingVisual = ProGPU.Scene.DrawingVisual;
using SceneRect = ProGPU.Scene.Rect;

namespace ProGPU.Wpf.Tests.Composition.Mil;

// Typed source-adapter controls with an actual headless rendering owner. These
// do not substitute for the separately built original PresentationCore oracle.
public sealed class WpfOwnedShaderRecipeTests
{
    private const string SampleShader = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return wpf_sample_register(1u, uv) * wpf_constant(0u); }";
    private const string ConstantShader = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return wpf_constant(0u); }";
    private static readonly WpfReplayRect CaptureBounds = new(0, 0, 8, 8);

    [Theory]
    [InlineData(0)] // VisualBrush
    [InlineData(1)] // ImageBrush over DrawingImage
    [InlineData(2)] // BitmapCacheBrush, with the effect below its excluded root
    public void RecordedNestedEffectNeverRequeriesMutatedSourceOrConstants(int family)
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        using var innerShader = new ShaderRegistration(11, ConstantShader);
        using var outerShader = new ShaderRegistration(12, SampleShader);
        var reads = new SourceReads();
        var inner = new ShaderSource(reads, Descriptor(innerShader, [0, 1, 0, 1]));
        var drawing = new GeometrySource(reads) { Brush = Brushes.Blue };
        var group = new GroupSource(reads, drawing, inner);
        var visual = new VisualSource(reads, group);
        object brush = family switch
        {
            0 => new TileSource(reads, Tile(PortableTileBrushKind.Visual, visual)),
            1 => new TileSource(reads, Tile(PortableTileBrushKind.Image, new DrawingImageSource(reads, group))),
            _ => new CacheSource(reads, new PortableBitmapCacheBrush(visual))
        };
        var outer = new ShaderSource(reads, Descriptor(outerShader, [1, 1, 1, 1],
            new PortableShaderSampler(1, brush, PortableShaderSamplingMode.NearestNeighbor)));
        Assert.True(target.Context.TryGetCacheRasterLimits(out uint maxWidth, out uint maxHeight));
        // Explicit caller policy, not a claim about this machine's primary DPI.
        var policy = new PortableBitmapCacheRasterPolicy(1, 1, maxWidth, maxHeight, 1);
        using var adapter = new WpfShaderRecordingImageSourceAdapter(null, target.Context,
            target.Viewport3DTextureCache, () => { reads.Read(); return policy; });
        Assert.True(WpfEffectMapper.TryCreateOwnedShaderEffect(outer, visual, CaptureBounds, adapter, out var source));
        using (source)
        {
            int capturedReads = reads.Count;
            Assert.True(capturedReads > 0);
            // Change the actual DTO arrays as well as the live source graph.
            // A late descriptor read or aliased constants array is observable.
            inner.Value.FloatConstants[1] = 0;
            inner.Value.FloatConstants[2] = 1;
            outer.Value.FloatConstants[1] = 0;
            outer.Value.Samplers[0] = new PortableShaderSampler(-1, brush, PortableShaderSamplingMode.Bilinear);
            drawing.Brush = Brushes.Red;
            group.State.Children = [];
            group.State.HasEffect = false;
            visual.Content = null;
            reads.Forbidden = true;

            AssertGreenAtColdWarmAndNewTarget(target, source);
            Assert.Equal(capturedReads, reads.Count);
        }
    }

    [Fact]
    public void ImageBrushOwnsPixelsAndOriginalDipMetricsBeforeLatePreparation()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        using var shader = new ShaderRegistration(13, SampleShader);
        using var texture = new GpuTexture(target.Context, 4, 2, TextureFormat.Rgba8Unorm,
            TextureUsage.CopyDst | TextureUsage.CopySrc | TextureUsage.TextureBinding,
            "Original shader bitmap pixels");
        byte[] pixels =
        [
            255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255,
            255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255
        ];
        texture.WritePixels<byte>(pixels);
        var reads = new SourceReads();
        var image = new MetricsBitmap(reads, texture);
        // Actual 4x2 texels at 192 DPI occupy 2x1 DIPs. The right 1x1 DIP
        // selects only green; assuming 96 DPI instead selects a red texel.
        var brush = new TileSource(reads, Tile(PortableTileBrushKind.Image, image,
            new PortableRect(1, 0, 1, 1), PortableBrushMappingMode.Absolute));
        var effect = new ShaderSource(reads, Descriptor(shader, [1, 1, 1, 1],
            new PortableShaderSampler(1, brush, PortableShaderSamplingMode.NearestNeighbor)));
        using var adapter = new WpfShaderRecordingImageSourceAdapter(null, target.Context, null, null);
        Assert.True(WpfEffectMapper.TryCreateOwnedShaderEffect(effect, image, CaptureBounds, adapter, out var source));
        using (source)
        {
            int capturedReads = reads.Count;
            Assert.True(image.MetricReads > 0);
            image.Metrics = new PortableBitmapSourceMetrics(4, 2, 96, 96);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 255;
            }
            texture.WritePixels<byte>(pixels);
            reads.Forbidden = true;
            AssertGreenAtColdWarmAndNewTarget(target, source);
            Assert.Equal(capturedReads, reads.Count);
        }
    }

    [Theory]
    [InlineData(0)] // negative register
    [InlineData(1)] // out-of-range register
    [InlineData(2)] // duplicate explicit register
    [InlineData(3)] // collision with default implicit register zero
    public void InvalidCompleteRegisterListRejectsBeforeRecordingAdapterOrSamplerReads(int kind)
    {
        using var shader = new ShaderRegistration(14, SampleShader);
        var reads = new SourceReads { Forbidden = true };
        var brush = new TileSource(reads, Tile(PortableTileBrushKind.Visual, new object()));
        int rejectedRegister = kind switch { 0 => -1, 1 => int.MaxValue, 2 => 1, _ => 0 };
        var descriptor = Descriptor(shader, [1, 1, 1, 1],
            new PortableShaderSampler(1, brush, PortableShaderSamplingMode.NearestNeighbor),
            new PortableShaderSampler(rejectedRegister, brush, PortableShaderSamplingMode.NearestNeighbor));
        var adapter = new ForbiddenRecordingAdapter();
        Assert.False(WpfEffectMapper.TryCreateOwnedShaderEffect(new ShaderSource(new SourceReads(), descriptor),
            new object(), CaptureBounds, adapter, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(0, adapter.Calls);
        Assert.Equal(0, reads.Count);
    }

    [Fact]
    public void HiddenVisualCycleRejectsCandidateWithoutChangingExistingParentCommands()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        using var shader = new ShaderRegistration(15, SampleShader);
        var reads = new SourceReads();
        var root = new VisualSource(reads, new GeometrySource(reads));
        var hidden = new VisualSource(reads, null)
        {
            State = new PortableVisualState { HasVisibility = true, Visibility = PortableVisualVisibility.Hidden }
        };
        root.Children.Add(hidden);
        hidden.Children.Add(root);
        var brush = new TileSource(reads, Tile(PortableTileBrushKind.Visual, root));
        var effect = new ShaderSource(reads, Descriptor(shader, [1, 1, 1, 1],
            new PortableShaderSampler(1, brush, PortableShaderSamplingMode.NearestNeighbor)));
        using var adapter = new WpfShaderRecordingImageSourceAdapter(null, target.Context, null, null);
        var parent = new global::ProGPU.Scene.DrawingContext();
        parent.DrawRectangle(new global::ProGPU.Scene.SolidColorBrush(Vector4.One), null, new SceneRect(2, 3, 4, 5));
        var before = Assert.Single(parent.Commands);
        int retainedBefore = parent.RetainedResourceCount;
        try
        {
            OwnedShaderEffectSource? rejected = null;
            Assert.Throws<InvalidOperationException>(() => WpfEffectMapper.TryCreateOwnedShaderEffect(
                effect, root, CaptureBounds, adapter, out rejected));
            Assert.Null(rejected);
            Assert.Equal(before, Assert.Single(parent.Commands));
            Assert.Equal(retainedBefore, parent.RetainedResourceCount);
            Assert.False(WpfCaptureReplayGuard.IsActive);
            Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);

            hidden.Children.Clear();
            Assert.True(WpfEffectMapper.TryCreateOwnedShaderEffect(effect, root, CaptureBounds, adapter, out var restored));
            using (restored)
            using (var content = EmptyContent())
                parent.DrawOwnedShaderEffect(content, restored);
            Assert.Equal(2, parent.Commands.Count);
            Assert.Equal(before, parent.Commands[0]);
            Assert.Equal(RenderCommandType.DrawVisual, parent.Commands[1].Type);
        }
        finally { parent.Clear(); }
    }

    [Fact]
    public void FailedDrawingImageReadIsNotSuccessfulNullDrawing()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        using var shader = new ShaderRegistration(16, SampleShader);
        var reads = new SourceReads();
        var image = new DrawingImageSource(reads, null) { Available = false };
        var brush = new TileSource(reads, Tile(PortableTileBrushKind.Image, image));
        var effect = new ShaderSource(reads, Descriptor(shader, [1, 1, 1, 1],
            new PortableShaderSampler(1, brush, PortableShaderSamplingMode.NearestNeighbor)));
        using var adapter = new WpfShaderRecordingImageSourceAdapter(null, target.Context, null, null);
        OwnedShaderEffectSource? rejected = null;
        Assert.Throws<NotSupportedException>(() => WpfEffectMapper.TryCreateOwnedShaderEffect(
            effect, image, CaptureBounds, adapter, out rejected));
        Assert.Null(rejected);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        image.Available = true;
        Assert.True(WpfEffectMapper.TryCreateOwnedShaderEffect(effect, image, CaptureBounds, adapter, out var empty));
        using (empty)
            Assert.Equal(new ShaderEffectSourceCapture(0, 0, 8, 8, 0, 0, 0, 0), empty.SourceCapture);
    }

    private static void AssertGreenAtColdWarmAndNewTarget(ProGpuWpfCompositionTarget target, OwnedShaderEffectSource source)
    {
        using var content = EmptyContent();
        var visual = new SceneDrawingVisual { Size = new Vector2(8) };
        visual.Context.DrawOwnedShaderEffect(content, source);
        // Drop caller references before preparation; the exact recorded command
        // now owns both the content and the complete source recipe generation.
        source.Dispose();
        content.Dispose();
        try
        {
            for (int pass = 0; pass < 3; pass++)
            {
                uint side = pass == 2 ? 16U : 8U;
                using var output = new GpuTexture(target.Context, side, side, TextureFormat.Rgba8Unorm,
                    TextureUsage.RenderAttachment | TextureUsage.CopySrc, "Owned source recipe output");
                target.Compositor.RenderOffscreen(visual, 8, 8, output, padding: 0,
                    dpiScale: pass == 2 ? 2 : 1, clearColor: Vector4.Zero);
                byte[] pixels = output.ReadPixels();
                Assert.Equal(checked((int)(side * side * 4)), pixels.Length);
                for (int pixel = 0; pixel < pixels.Length; pixel += 4)
                {
                    Assert.Equal((byte)0, pixels[pixel]);
                    Assert.Equal((byte)255, pixels[pixel + 1]);
                    Assert.Equal((byte)0, pixels[pixel + 2]);
                    Assert.Equal((byte)255, pixels[pixel + 3]);
                }
            }
        }
        finally { visual.Context.Clear(); }
    }

    private static GpuPicture EmptyContent()
    {
        var recorder = new GpuPictureRecorder();
        var context = recorder.BeginRecording(new SceneRect(0, 0, 8, 8));
        try { return recorder.EndRecording(); }
        finally { context.Clear(); }
    }

    private static PortableShaderEffect Descriptor(ShaderRegistration shader, float[] constants,
        params PortableShaderSampler[] samplers) => new(null, null,
            new PortablePixelShader(null, null, shader.Bytecode, 3, 0), constants, samplers,
            0, 0, 0, 0, 0, 0, -1);

    private static PortableTileBrush Tile(PortableTileBrushKind kind, object content,
        PortableRect? viewbox = null,
        PortableBrushMappingMode units = PortableBrushMappingMode.RelativeToBoundingBox) =>
        new(kind, content, 1, new PortableRect(0, 0, 1, 1), viewbox ?? new PortableRect(0, 0, 1, 1),
            PortableBrushMappingMode.RelativeToBoundingBox, units, PortableTileMode.None,
            PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
            false, default, false, default);

    private sealed class ShaderRegistration : IDisposable
    {
        private readonly string _key;
        internal byte[] Bytecode { get; }
        internal ShaderRegistration(byte identity, string source)
        {
            Bytecode = [0, 3, 0, 0, 197, 211, 223, identity];
            _key = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(Bytecode, source,
                shaderKey: "owned_source_recipe_" + identity);
        }
        public void Dispose() => WpfShaderEffectRegistry.Unregister(_key);
    }

    private sealed class SourceReads
    {
        internal bool Forbidden { get; set; }
        internal int Count { get; private set; }
        internal void Read()
        {
            if (Forbidden) throw new InvalidOperationException("Late preparation queried mutable source state.");
            Count++;
        }
    }

    private sealed class ShaderSource(SourceReads reads, PortableShaderEffect value) : IPortableShaderEffectSource
    {
        internal PortableShaderEffect Value { get; } = value;
        public bool TryGetPortableShaderEffect(out PortableShaderEffect effect)
        { reads.Read(); effect = Value; return true; }
    }

    private sealed class TileSource(SourceReads reads, PortableTileBrush value) : IPortableTileBrushSource
    {
        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        { reads.Read(); brush = value; return true; }
    }

    private sealed class CacheSource(SourceReads reads, PortableBitmapCacheBrush value) : IPortableBitmapCacheBrushSource
    {
        public bool TryGetPortableBitmapCacheBrush(out PortableBitmapCacheBrush brush)
        { reads.Read(); brush = value; return true; }
    }

    private sealed class DrawingImageSource(SourceReads reads, object? drawing) : IPortableDrawingImageSource
    {
        internal bool Available { get; set; } = true;
        public bool TryGetPortableDrawingImage(out object? value)
        { reads.Read(); value = drawing; return Available; }
    }

    private sealed class GeometrySource(SourceReads reads) : IPortableGeometryDrawingStateSource, IPortableDrawingBoundsSource
    {
        internal object Brush { get; set; } = Brushes.Red;
        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            reads.Read();
            state = new() { HasGeometry = true, Geometry = new PortableRect(0, 0, 8, 8), HasBrush = true, Brush = Brush };
            return true;
        }
        public bool TryGetPortableDrawingBounds(out PortableRect bounds)
        { reads.Read(); bounds = new(0, 0, 8, 8); return true; }
    }

    private sealed class GroupSource(SourceReads reads, object drawing, object effect)
        : IPortableDrawingGroupStateSource, IPortableDrawingBoundsSource
    {
        internal PortableDrawingGroupState State { get; } = new()
        {
            HasBounds = true, Bounds = new(0, 0, 8, 8), HasLocalBounds = true, LocalBounds = new(0, 0, 8, 8),
            HasEffect = true, Effect = effect, Children = [drawing]
        };
        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        { reads.Read(); state = State; return true; }
        public bool TryGetPortableDrawingBounds(out PortableRect bounds)
        { reads.Read(); bounds = State.Bounds; return true; }
    }

    private sealed class VisualSource(SourceReads reads, object? content) : IPortableVisualStateSource,
        IPortableVisualBoundsSource, IPortableVisualChildrenSource, IPortableDrawingContentSource
    {
        internal object? Content { get; set; } = content;
        internal PortableVisualState State { get; set; } = new();
        internal List<object> Children { get; } = [];
        public bool TryGetPortableVisualState(out PortableVisualState state)
        { reads.Read(); state = State; return true; }
        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            reads.Read();
            bounds = new() { HasContentBounds = true, ContentBounds = new(0, 0, 8, 8),
                HasDescendantBounds = true, DescendantBounds = new(0, 0, 8, 8) };
            return true;
        }
        public bool TryGetPortableVisualChildCount(out int count)
        { reads.Read(); count = Children.Count; return true; }
        public bool TryGetPortableVisualChild(int index, out object? child)
        { reads.Read(); child = Children[index]; return true; }
        public bool TryGetPortableDrawingContent(out object? drawing)
        { reads.Read(); drawing = Content; return true; }
    }

    private sealed class MetricsBitmap(SourceReads reads, GpuTexture texture)
        : BitmapSource, IPortableBitmapSourceMetricsSource
    {
        internal PortableBitmapSourceMetrics Metrics { get; set; } = new(4, 2, 192, 192);
        internal int MetricReads { get; private set; }
        public override int PixelWidth => 4;
        public override int PixelHeight => 2;
        public override GpuTexture GpuTexture { get { reads.Read(); return texture; } }
        public bool TryGetPortableBitmapSourceMetrics(out PortableBitmapSourceMetrics metrics)
        { reads.Read(); MetricReads++; metrics = Metrics; return true; }
    }

    private sealed class ForbiddenRecordingAdapter : IWpfImageSourceAdapter, IWpfShaderRecordingAdapterSource
    {
        internal int Calls { get; private set; }
        public ImageSource? AdaptImageSource(object? value) => throw new InvalidOperationException("Unexpected sampler lookup.");
        public WpfShaderRecordingImageSourceAdapter CreateShaderRecordingAdapter()
        { Calls++; throw new InvalidOperationException("Invalid registers reached recording allocation."); }
    }
}
