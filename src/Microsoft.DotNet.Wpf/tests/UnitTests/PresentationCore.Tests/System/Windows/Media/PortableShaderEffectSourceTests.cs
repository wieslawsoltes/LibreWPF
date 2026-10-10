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
    public void PaddingPreservesOriginalDrawingImageOrCacheSamplerAndFourDoubleBits(bool cacheSampler)
    {
        var rectangle = new Rect(10, 20, 8, 6);
        var drawing = new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(rectangle));
        var group = new DrawingGroup();
        group.Children.Add(drawing);
        var image = new DrawingImage(group);
        var target = new DrawingVisual();
        using (DrawingContext context = target.RenderOpen()) context.DrawDrawing(group);
        var cache = new BitmapCache(2);
        Brush brush = cacheSampler
            ? new BitmapCacheBrush(target) { BitmapCache = cache, AutoWrapTarget = false }
            : new ImageBrush(image);
        var effect = new SourceEffect { Input = brush };
        var source = (IPortableShaderEffectSource)effect;
        double[] padding = [Math.BitIncrement(2d), 3, 4, Math.BitDecrement(5d)];
        for (int axis = 0; axis < 4; ++axis) effect.SetPadding(axis, padding[axis]);
        Assert.True(source.TryGetPortableShaderEffect(out var captured));
        AssertPaddingBits(padding, captured);
        var sampler = Assert.Single(captured.Samplers);
        Assert.Same(brush, sampler.Brush);
        Assert.Equal(cacheSampler ? PortableShaderSamplerKind.Brush : PortableShaderSamplerKind.ImageSource, sampler.Kind);
        Assert.Equal(3, sampler.RegisterIndex);
        Assert.Equal(PortableShaderSamplingMode.NearestNeighbor, sampler.SamplingMode);
        if (cacheSampler)
        {
            Assert.Null(sampler.ImageSource);
            Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var cacheState));
            Assert.Same(target, cacheState.InternalTarget);
            Assert.Same(cache, cacheState.BitmapCache);
        }
        else Assert.Same(image, sampler.ImageSource);
        Assert.Same(group, image.Drawing);
        Assert.Same(drawing, Assert.Single(group.Children));
        Assert.Equal(rectangle, image.Bounds);
        Assert.True(((IPortableVisualBoundsSource)target).TryGetPortableVisualBounds(out var bounds));
        Assert.Equal(new PortableRect(10, 20, 8, 6), bounds.DescendantBounds);

        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        for (int axis = 0; axis < 4; ++axis) effect.SetPadding(axis, negativeZero);
        Assert.True(source.TryGetPortableShaderEffect(out var signedZero));
        AssertPaddingBits([negativeZero, negativeZero, negativeZero, negativeZero], signedZero);
        for (int axis = 0; axis < 4; ++axis) effect.SetPadding(axis, 0);
        Assert.True(source.TryGetPortableShaderEffect(out var reset));
        AssertPaddingBits([0, 0, 0, 0], reset);
        AssertPaddingBits(padding, captured);
        AssertPaddingBits([negativeZero, negativeZero, negativeZero, negativeZero], signedZero);
        Assert.Same(brush, Assert.Single(reset.Samplers).Brush);
        Assert.Equal(rectangle, image.Bounds);
        Assert.Same(effect.OriginalShader, reset.PixelShader!.Source);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PaddingRejectsEachInvalidSourceAxisWithoutChangingCapturedGeneration(int axis)
    {
        var effect = new SourceEffect();
        var source = (IPortableShaderEffectSource)effect;
        double[] original = [2, 3, 4, 5];
        for (int i = 0; i < 4; ++i) effect.SetPadding(i, original[i]);
        Assert.True(source.TryGetPortableShaderEffect(out var captured));
        Assert.Throws<ArgumentOutOfRangeException>(() => effect.SetPadding(axis, -1));
        Assert.True(source.TryGetPortableShaderEffect(out var afterSetterRejection));
        AssertPaddingBits(original, afterSetterRejection);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.MaxValue })
        {
            effect.SetPadding(axis, invalid); // protected source setter permits these values
            Assert.False(source.TryGetPortableShaderEffect(out _));
            AssertPaddingBits(original, captured);
            effect.SetPadding(axis, original[axis]);
            Assert.True(source.TryGetPortableShaderEffect(out var restored));
            AssertPaddingBits(original, restored);
        }
        effect.SetPadding(axis, Math.BitDecrement((double)float.MaxValue));
        Assert.True(source.TryGetPortableShaderEffect(out var finiteBoundary));
        double[] expected = (double[])original.Clone();
        expected[axis] = Math.BitDecrement((double)float.MaxValue);
        AssertPaddingBits(expected, finiteBoundary);
        AssertPaddingBits(original, captured);
    }

    private static void AssertPaddingBits(double[] expected, PortableShaderEffect effect)
    {
        double[] actual = [effect.PaddingTop, effect.PaddingBottom, effect.PaddingLeft, effect.PaddingRight];
        for (int axis = 0; axis < 4; ++axis)
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected[axis]), BitConverter.DoubleToInt64Bits(actual[axis]));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CacheShaderSamplerRetainsActualInternalTargetAndSelectedCache(bool wrap)
    {
        var target = new DrawingVisual();
        var cache = new BitmapCache(2);
        var brush = new BitmapCacheBrush(target) { AutoWrapTarget = wrap, BitmapCache = cache };
        var effect = new SourceEffect { Input = brush };
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var shader));
        var sampler = Assert.Single(shader.Samplers);
        Assert.Equal(PortableShaderSamplerKind.Brush, sampler.Kind);
        Assert.Equal(3, sampler.RegisterIndex);
        Assert.Same(brush, sampler.Brush);
        Assert.Null(sampler.ImageSource);
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var captured));
        Assert.Same(brush.InternalTarget, captured.InternalTarget);
        Assert.Same(cache, captured.BitmapCache);
        Assert.Equal(1, captured.Opacity);
        Assert.False(captured.HasTransform);
        Assert.False(captured.HasRelativeTransform);
        if (wrap)
        {
            Assert.NotSame(target, captured.InternalTarget);
            var wrapper = Assert.IsAssignableFrom<IPortableVisualChildrenSource>(captured.InternalTarget);
            Assert.True(wrapper.TryGetPortableVisualChildCount(out int count));
            Assert.Equal(1, count);
            Assert.True(wrapper.TryGetPortableVisualChild(0, out object child));
            Assert.Same(target, child);
        }
        else Assert.Same(target, captured.InternalTarget);
    }

    [Fact]
    public void CacheShaderTargetBoundsIgnoreOuterRootStateButPreserveDescendantClip()
    {
        var child = new CacheSourceDrawingVisual();
        using (DrawingContext context = child.RenderOpen())
            context.DrawRectangle(Brushes.Red, null, new Rect(10, 20, 8, 6));
        var root = new CacheSourceContainerVisual();
        root.Children.Add(child);
        root.SetOuterState();
        var brush = new BitmapCacheBrush(root);
        var effect = new SourceEffect { Input = brush };
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var shader));
        Assert.Same(brush, Assert.Single(shader.Samplers).Brush);
        Assert.True(((IPortableVisualBoundsSource)root).TryGetPortableVisualBounds(out var before));
        Assert.Equal(new PortableRect(10, 20, 8, 6), before.DescendantBounds);
        child.SetClip(new Rect(12, 21, 2, 3));
        Assert.True(((IPortableVisualBoundsSource)root).TryGetPortableVisualBounds(out var after));
        Assert.Equal(new PortableRect(12, 21, 2, 3), after.DescendantBounds);
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var captured));
        Assert.Same(root, captured.InternalTarget);
    }

    [Fact]
    public void CacheShaderNullTargetAndCacheMutationRetainOriginalBrush()
    {
        var target = new DrawingVisual();
        var first = new BitmapCache(1);
        var second = new BitmapCache(2);
        var brush = new BitmapCacheBrush(target) { BitmapCache = first };
        var effect = new SourceEffect { Input = brush };
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var before));
        brush.Target = null;
        brush.BitmapCache = second;
        Assert.True(((IPortableShaderEffectSource)effect).TryGetPortableShaderEffect(out var shader));
        Assert.Same(brush, Assert.Single(shader.Samplers).Brush);
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var after));
        Assert.Null(after.InternalTarget);
        Assert.Same(second, after.BitmapCache);
        Assert.Same(target, before.InternalTarget);
        Assert.Same(first, before.BitmapCache);
        brush.Target = target;
        Assert.True(((IPortableBitmapCacheBrushSource)brush).TryGetPortableBitmapCacheBrush(out var restored));
        Assert.Same(target, restored.InternalTarget);
    }

    private sealed class CacheSourceDrawingVisual : DrawingVisual
    {
        internal void SetClip(Rect rectangle) => VisualClip = new RectangleGeometry(rectangle);
    }

    private sealed class CacheSourceContainerVisual : ContainerVisual
    {
        internal void SetOuterState()
        {
            VisualOffset = new Vector(100, 200);
            VisualTransform = new ScaleTransform(2, 3);
            VisualClip = new RectangleGeometry(new Rect(0, 0, 1, 1));
            VisualOpacity = 0;
            VisualOpacityMask = Brushes.Transparent;
            VisualEffect = new BlurEffect { Radius = 4 };
        }
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
        internal void SetPadding(int axis, double value)
        {
            switch (axis)
            {
                case 0: PaddingTop = value; break;
                case 1: PaddingBottom = value; break;
                case 2: PaddingLeft = value; break;
                case 3: PaddingRight = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(axis));
            }
        }
        protected override Freezable CreateInstanceCore() => new SourceEffect();
    }
}
