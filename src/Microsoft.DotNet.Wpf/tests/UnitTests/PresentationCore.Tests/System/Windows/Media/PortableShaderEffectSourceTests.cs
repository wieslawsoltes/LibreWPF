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
