using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuSourceEffectCaptureFrameTests
{
    [Fact]
    public void CommandEffectScopeKeepsSourceDescriptorAndActualRecordedTranslation()
    {
        var root = new Scene.ContainerVisual();
        var frame = CreateFrame(root);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, null, null);
        var bounds = SourceBounds();
        var effect = SourceEffect(bounds);
        Assert.True(sink.PushNativeVisualEffect(effect, bounds));
        sink.DrawRectangle(Brushes.Red, null, new Rect(bounds.X, bounds.Y, 3, 4));
        sink.Pop();
        sink.DrawRectangle(Brushes.Blue, null, new Rect(1, 2, 3, 4));

        var retained = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(root.Children));
        var nested = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retained.Children));
        Assert.Same(effect, nested.Effect);
        Assert.Equal(new Vector2((float)-bounds.X, (float)-bounds.Y), nested.EffectSourceTranslation);
        Assert.Equal(new Vector2((float)bounds.X, (float)bounds.Y), nested.Offset);
        Assert.Equal(new Vector2((float)bounds.Width, (float)bounds.Height), nested.Size);
        AssertOriginalBounds(effect, bounds);
        var command = Assert.Single(nested.Context.Commands);
        Assert.Equal((float)-bounds.X, command.Transform.M41);
        Assert.Equal((float)-bounds.Y, command.Transform.M42);
        var following = Assert.Single(retained.Context.Commands);
        Assert.Equal(0f, following.Transform.M41);
        Assert.Equal(0f, following.Transform.M42);
        Assert.Null(retained.EffectSourceTranslation);
    }

    [Fact]
    public void CommandEffectRejectsMissingOrFloatEquivalentWrongBoundsWithoutPublishingScope()
    {
        var root = new Scene.ContainerVisual();
        using var sink = new ProGpuRetainedCompositionCommandSink(CreateFrame(root), null, null);
        var bounds = SourceBounds();
        var effect = SourceEffect(bounds);
        var rounded = new WpfReplayRect((float)bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Assert.Equal((float)bounds.X, (float)rounded.X);
        Assert.NotEqual(BitConverter.DoubleToInt64Bits(bounds.X), BitConverter.DoubleToInt64Bits(rounded.X));
        Assert.False(sink.PushNativeVisualEffect(effect, null));
        Assert.False(sink.PushNativeVisualEffect(effect, rounded));
        var retained = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(root.Children));
        Assert.Empty(retained.Children);
        Assert.True(sink.PushNativeVisualEffect(effect, bounds));
        sink.Pop();
        Assert.Single(retained.Children);
    }

    [Fact]
    public void RetainedOwnerKeepsSourceRebaseAndFinalClipsThenClearsItForLegacyAndReset()
    {
        var root = new Scene.ContainerVisual();
        using var sink = new ProGpuRetainedCompositionCommandSink(CreateFrame(root), null, null);
        var bounds = SourceBounds();
        var effect = SourceEffect(bounds);
        Assert.True(sink.PushVisualOwner(new object()));
        sink.ApplyVisualState(State(bounds, effect));
        var retained = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(root.Children));
        var owner = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retained.Children));
        Assert.Same(effect, owner.Effect);
        Assert.Equal(new Vector2((float)-bounds.X, (float)-bounds.Y), owner.EffectSourceTranslation);
        Assert.Equal(new Scene.Rect(2, 3, 10, 11), owner.ClipBounds);
        Assert.Equal(new Scene.Rect(0, 1, 20, 21), owner.OuterClipBounds);
        AssertOriginalBounds(effect, bounds);

        var legacy = new Scene.WpfShaderEffect(new Scene.WpfShaderEffectParams()) { Padding = 2.75f };
        sink.ApplyVisualState(State(bounds, legacy));
        Assert.Same(legacy, owner.Effect);
        Assert.Null(owner.EffectSourceTranslation);
        Assert.Null(legacy.SourceCapture);
        Assert.Equal(2.75f, legacy.Padding);
        sink.ApplyVisualState(State(bounds, effect));
        Assert.NotNull(owner.EffectSourceTranslation);
        sink.ApplyVisualState(State(bounds, null));
        Assert.Null(owner.Effect);
        Assert.Null(owner.EffectSourceTranslation);
        sink.PopVisualOwner();
    }

    [Fact]
    public void RetainedSourceFrameMismatchPreservesPreviouslyPublishedState()
    {
        var root = new Scene.ContainerVisual();
        using var sink = new ProGpuRetainedCompositionCommandSink(CreateFrame(root), null, null);
        var bounds = SourceBounds();
        var effect = SourceEffect(bounds);
        Assert.True(sink.PushVisualOwner(new object()));
        sink.ApplyVisualState(State(bounds, effect));
        var retained = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(root.Children));
        var owner = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retained.Children));
        var translation = owner.EffectSourceTranslation;
        var clip = owner.ClipBounds;
        var outerClip = owner.OuterClipBounds;
        var different = new WpfReplayRect((float)bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var invalid = new WpfRetainedVisualState(new Vector2(100, 200), Matrix4x4.CreateScale(2), 0,
            null, new Vector2(1, 1), effect, contentBounds: different, isVisible: false);
        Assert.Throws<InvalidOperationException>(() => sink.ApplyVisualState(invalid));
        Assert.Same(effect, owner.Effect);
        Assert.Equal(translation, owner.EffectSourceTranslation);
        Assert.Equal(clip, owner.ClipBounds);
        Assert.Equal(outerClip, owner.OuterClipBounds);
        Assert.True(owner.IsVisible);
        Assert.Equal(1f, owner.Opacity);
        Assert.Equal(Matrix4x4.Identity, owner.Transform);
        Assert.Equal(new Vector2((float)bounds.X, (float)bounds.Y), owner.Offset);
        sink.PopVisualOwner();
    }

    private static WpfReplayRect SourceBounds() => new(Math.BitIncrement(10d), Math.BitDecrement(20d),
        Math.BitIncrement(30d), Math.BitDecrement(40d));

    private static Scene.WpfShaderEffect SourceEffect(WpfReplayRect bounds) =>
        new(new Scene.WpfShaderEffectParams())
        {
            SourceCapture = new Scene.ShaderEffectSourceCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                0.25, 0.5, 0.75, 1.25),
            CaptureSourceVisualOpacity = true
        };

    private static WpfRetainedVisualState State(WpfReplayRect bounds, Scene.EffectBase? effect) =>
        new(new Vector2((float)bounds.X, (float)bounds.Y), Matrix4x4.Identity, 1,
            new WpfReplayRect(2, 3, 10, 11), new Vector2((float)bounds.Width, (float)bounds.Height), effect,
            contentBounds: bounds, outerClipBounds: new WpfReplayRect(0, 1, 20, 21));

    private static ProGpuWpfDrawingFrame CreateFrame(Scene.ContainerVisual retained) =>
        new(new Scene.ContainerVisual(), retained, new Scene.DrawingVisual(), 200, 100);

    private static void AssertOriginalBounds(Scene.WpfShaderEffect effect, WpfReplayRect bounds)
    {
        Assert.True(effect.SourceCapture.HasValue);
        var source = effect.SourceCapture.Value;
        Assert.Equal(BitConverter.DoubleToInt64Bits(bounds.X), BitConverter.DoubleToInt64Bits(source.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(bounds.Y), BitConverter.DoubleToInt64Bits(source.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(bounds.Width), BitConverter.DoubleToInt64Bits(source.Width));
        Assert.Equal(BitConverter.DoubleToInt64Bits(bounds.Height), BitConverter.DoubleToInt64Bits(source.Height));
    }
}
