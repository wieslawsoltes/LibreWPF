// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Tests;

public sealed class PortableShaderEffectSourceTests
{
    [Fact]
    public void VisualSnapshotUsesActualRenderFieldNotInertAttachedBitmapScalingMode()
    {
        var visual = new SourceDrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        Assert.True(((IPortableVisualStateSource)visual).TryGetPortableVisualState(out var attachedOnly));
        Assert.False(attachedOnly.HasBitmapScalingMode);
        Assert.False(attachedOnly.HasPortableBitmapScalingMode);
        visual.SetActualBitmapScalingMode(BitmapScalingMode.LowQuality);
        Assert.True(((IPortableVisualStateSource)visual).TryGetPortableVisualState(out var actual));
        Assert.Equal(BitmapScalingMode.NearestNeighbor, RenderOptions.GetBitmapScalingMode(visual));
        Assert.True(actual.HasPortableBitmapScalingMode);
        Assert.Equal(PortableBitmapScalingMode.Linear, actual.PortableBitmapScalingMode);
    }

    [Fact]
    public void UiElementSnapshotRetainsItsActualBitmapScalingMetadataPropagation()
    {
        var element = new UIElement();
        RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
        Assert.True(((IPortableVisualStateSource)element).TryGetPortableVisualState(out var state));
        Assert.True(state.HasPortableBitmapScalingMode);
        Assert.Equal(PortableBitmapScalingMode.NearestNeighbor, state.PortableBitmapScalingMode);
    }

    [Fact]
    public void DrawingGroupSnapshotRetainsItsSerializedAttachedBitmapScalingMode()
    {
        var drawing = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);
        Assert.True(((IPortableDrawingGroupStateSource)drawing).TryGetPortableDrawingGroupState(out var state));
        Assert.True(state.HasPortableBitmapScalingMode);
        Assert.Equal(PortableBitmapScalingMode.NearestNeighbor, state.PortableBitmapScalingMode);
    }

    private sealed class SourceDrawingVisual : DrawingVisual
    {
        internal void SetActualBitmapScalingMode(BitmapScalingMode mode) => VisualBitmapScalingMode = mode;
    }

    [Fact]
    public void BitmapMetricsSnapshotRetainsOriginalPixelSizeAndBothDpiAxes()
    {
        var image = BitmapSource.Create(2, 1, 192, 144, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8);
        Assert.True(((IPortableBitmapSourceMetricsSource)image).TryGetPortableBitmapSourceMetrics(out var metrics));
        Assert.Equal(new PortableBitmapSourceMetrics(2, 1, 192, 144), metrics);
    }

    [Theory]
    [InlineData(ShaderRenderMode.Auto, PortableShaderRenderMode.Auto)]
    [InlineData(ShaderRenderMode.SoftwareOnly, PortableShaderRenderMode.SoftwareOnly)]
    [InlineData(ShaderRenderMode.HardwareOnly, PortableShaderRenderMode.HardwareOnly)]
    public void PixelShaderSnapshotOwnsBytesAndOriginalExecutionIntent(ShaderRenderMode mode, PortableShaderRenderMode portable)
    {
        var pixel = CreateShader();
        pixel.ShaderRenderMode = mode;
        var source = (IPortablePixelShaderSource)pixel;
        Assert.True(source.TryGetPortablePixelShader(out var captured));
        Assert.Same(pixel, captured.Source);
        Assert.Equal(portable, captured.RenderMode);
        Assert.Equal(2, captured.MajorVersion);
        Assert.Equal(new byte[] { 0, 2, 255, 255, 255, 255, 0, 0 }, captured.Bytecode);
        using var stream = new MemoryStream(new byte[] { 0, 3, 255, 255, 255, 255, 0, 0 });
        pixel.SetStreamSource(stream);
        Assert.True(source.TryGetPortablePixelShader(out var updated));
        Assert.Equal(3, updated.MajorVersion);
        Assert.Equal(2, captured.Bytecode[1]);
        captured.Bytecode[0] = 42;
        Assert.True(source.TryGetPortablePixelShader(out var independent));
        Assert.Equal(0, independent.Bytecode[0]);
    }

    [Fact]
    public void ShaderEffectRetainsOriginalImageBrushAndDenseZeroRegisterHoles()
    {
        var image = new DrawingImage();
        var brush = new ImageBrush(image) { Opacity = 0.25, TileMode = TileMode.FlipX,
            Viewport = new Rect(0, 0, 0.5, 1), Transform = new TranslateTransform(3, 4) };
        var effect = new SourceEffect { Input = brush, Amount = 0.75 };
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var captured));
        var sampler = Assert.Single(captured.Samplers);
        Assert.Equal(PortableShaderSamplerKind.ImageSource, sampler.Kind);
        Assert.Same(brush, sampler.Brush);
        Assert.Same(image, sampler.ImageSource);
        Assert.Equal(PortableShaderSamplingMode.NearestNeighbor, sampler.SamplingMode);
        Assert.Equal(3, sampler.RegisterIndex);
        Assert.Equal(12, captured.FloatConstants.Length);
        Assert.All(captured.FloatConstants.Take(8), value => Assert.Equal(0, value));
        Assert.Equal(0.75f, captured.FloatConstants[8]);
        Assert.Same(effect.OriginalShader, captured.PixelShader!.Source);
        effect.Amount = 0.125;
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var updated));
        Assert.Equal(0.125f, updated.FloatConstants[8]);
        Assert.Equal(0.75f, captured.FloatConstants[8]);
    }

    [Fact]
    public void ImplicitInputAndPaddingRemainExplicitSourceState()
    {
        var effect = new SourceEffect();
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var state));
        Assert.Equal(PortableShaderSamplerKind.ImplicitInput, Assert.Single(state.Samplers).Kind);
        effect.SetTopPadding(2);
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out state));
        Assert.Equal(2, state.PaddingTop);
        effect.SetTopPadding(double.NaN);
        Assert.False(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out _));
        effect.SetTopPadding(double.PositiveInfinity);
        Assert.False(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisualSamplerKeepsActualSourceIdentityBoundsChildrenAndMapping(bool absolute)
    {
        var child = new DrawingVisual();
        using (DrawingContext context = child.RenderOpen())
            context.DrawRectangle(Brushes.Red, null, new Rect(10, 20, 8, 6));
        var visual = new ContainerVisual();
        visual.Children.Add(child);
        var transform = new TranslateTransform(3, 4);
        var brush = new VisualBrush(visual) { Opacity = 0.25, Transform = transform,
            Viewport = new Rect(0, 0, 0.5, 1), Viewbox = new Rect(10, 20, 8, 6),
            ViewboxUnits = absolute ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox,
            TileMode = TileMode.FlipX };
        var effect = new SourceEffect { Input = brush };
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var captured));
        var sampler = Assert.Single(captured.Samplers);
        Assert.Equal(PortableShaderSamplerKind.Brush, sampler.Kind);
        Assert.Same(brush, sampler.Brush);
        Assert.Null(sampler.ImageSource);
        Assert.Equal(3, sampler.RegisterIndex);
        Assert.Equal(PortableShaderSamplingMode.NearestNeighbor, sampler.SamplingMode);
        Assert.True(((IPortableTileBrushSource)brush).TryGetPortableTileBrush(out var tile));
        Assert.Same(visual, tile.Content);
        Assert.Equal(PortableTileBrushKind.Visual, tile.Kind);
        Assert.Equal(0.25, tile.Opacity);
        Assert.Equal(new PortableMatrix3x2(1, 0, 0, 1, 3, 4), tile.Transform);
        Assert.Equal(PortableTileMode.FlipX, tile.TileMode);
        Assert.Equal(absolute ? PortableBrushMappingMode.Absolute : PortableBrushMappingMode.RelativeToBoundingBox, tile.ViewboxUnits);
        Assert.True(((IPortableVisualChildrenSource)visual).TryGetPortableVisualChildCount(out int count));
        Assert.Equal(1, count);
        Assert.True(((IPortableVisualChildrenSource)visual).TryGetPortableVisualChild(0, out object ownedChild));
        Assert.Same(child, ownedChild);
        Assert.True(((IPortableVisualBoundsSource)visual).TryGetPortableVisualBounds(out var bounds));
        Assert.Equal(new PortableRect(10, 20, 8, 6), bounds.DescendantBounds);
        transform.X = -7;
        Assert.True(((IPortableTileBrushSource)brush).TryGetPortableTileBrush(out var changed));
        Assert.Equal(new PortableMatrix3x2(1, 0, 0, 1, -7, 4), changed.Transform);
        Assert.Equal(new PortableMatrix3x2(1, 0, 0, 1, 3, 4), tile.Transform);
    }

    [Fact]
    public void VisualSamplerDisconnectAndReattachNeverBecomeImplicitInputOrInventedVisual()
    {
        var first = new DrawingVisual();
        var second = new DrawingVisual();
        var brush = new VisualBrush(first);
        var effect = new SourceEffect { Input = brush };
        Assert.True(((IPortableTileBrushSource)brush).TryGetPortableTileBrush(out var original));
        brush.Visual = null;
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var disconnected));
        Assert.Equal(PortableShaderSamplerKind.Brush, Assert.Single(disconnected.Samplers).Kind);
        Assert.Same(brush, disconnected.Samplers[0].Brush);
        Assert.True(((IPortableTileBrushSource)brush).TryGetPortableTileBrush(out var empty));
        Assert.Equal(PortableTileBrushKind.Visual, empty.Kind);
        Assert.Null(empty.Content);
        brush.Visual = second;
        Assert.True(((IPortableTileBrushSource)brush).TryGetPortableTileBrush(out var replacement));
        Assert.Same(second, replacement.Content);
        Assert.Same(first, original.Content);
        Assert.Null(empty.Content);
        Assert.True(((IPortableVisualBoundsSource)second).TryGetPortableVisualBounds(out var bounds));
        Assert.True(bounds.HasDescendantBounds);
        Assert.True(bounds.DescendantBounds.IsEmpty); // initialized empty is not null
    }

    [Fact]
    public void VisualSamplerTracksSameSourceDrawingReplacementAndChildRetirement()
    {
        var child = new DrawingVisual();
        var root = new ContainerVisual();
        root.Children.Add(child);
        var brush = new VisualBrush(root);
        using (DrawingContext context = child.RenderOpen())
            context.DrawRectangle(Brushes.Red, null, new Rect(10, 20, 8, 6));
        Assert.True(((IPortableVisualBoundsSource)root).TryGetPortableVisualBounds(out var before));
        using (DrawingContext context = child.RenderOpen())
            context.DrawRectangle(Brushes.Blue, null, new Rect(-6, 9, 8, 6));
        Assert.True(((IPortableVisualBoundsSource)root).TryGetPortableVisualBounds(out var after));
        Assert.Equal(new PortableRect(10, 20, 8, 6), before.DescendantBounds);
        Assert.Equal(new PortableRect(-6, 9, 8, 6), after.DescendantBounds);
        root.Children.Clear();
        Assert.True(((IPortableTileBrushSource)brush).TryGetPortableTileBrush(out var tile));
        Assert.Same(root, tile.Content);
        Assert.True(((IPortableVisualBoundsSource)root).TryGetPortableVisualBounds(out var empty));
        Assert.True(empty.DescendantBounds.IsEmpty);
    }

    [Fact]
    public void DrawingBrushRemainsAnIllegalOriginalShaderSampler()
    {
        var effect = new SourceEffect();
        Assert.Throws<ArgumentException>(() => effect.Input = new DrawingBrush());
    }

    private static PixelShader CreateShader()
    {
        var shader = new PixelShader();
        using var stream = new MemoryStream(new byte[] { 0, 2, 255, 255, 255, 255, 0, 0 });
        shader.SetStreamSource(stream);
        return shader;
    }

    private sealed class SourceEffect : ShaderEffect
    {
        public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(
            nameof(Input), typeof(SourceEffect), 3, SamplingMode.NearestNeighbor);
        public static readonly DependencyProperty AmountProperty = DependencyProperty.Register(
            nameof(Amount), typeof(double), typeof(SourceEffect), new PropertyMetadata(0.0, PixelShaderConstantCallback(2)));

        internal SourceEffect()
        {
            PixelShader = CreateShader();
            UpdateShaderValue(InputProperty);
            UpdateShaderValue(AmountProperty);
        }
        public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
        public double Amount { get => (double)GetValue(AmountProperty); set => SetValue(AmountProperty, value); }
        internal PixelShader OriginalShader => PixelShader;
        internal void SetTopPadding(double value) => PaddingTop = value;
        protected override Freezable CreateInstanceCore() => new SourceEffect();
    }
}
