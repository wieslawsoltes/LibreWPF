using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Scene;
using Xunit;
using Rect = System.Windows.Rect;
using SceneContext = ProGPU.Scene.DrawingContext;
using SceneRect = ProGPU.Scene.Rect;

namespace ProGPU.Wpf.Tests;

// Recording/ownership controls only. No preparation, texture, compositor or GPU
// is substituted by these fixtures; their recipes must never be prepared here.
[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuOwnedShaderEffectCommandSinkTests
{
    [Fact]
    public void OwnedEffectKeepsBeforeInsideAfterOrderAndParentScopes()
    {
        var parent = new SceneContext();
        using var sink = new ProGpuCompositionCommandSink(parent);
        var recipe = new RecordingRecipe();
        var bounds = Bounds();
        using var source = Source(recipe, bounds);
        Matrix4x4 transform = Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(7, 11, 0);
        sink.PushNativeTransform(transform);
        ((IWpfNativeClipCommandSink)sink).PushNativeClip(new WpfReplayRect(0, 0, 100, 100));
        sink.PushOpacity(.5);
        sink.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));

        Assert.True(Owned(sink).PushOwnedShaderEffect(source, bounds));
        var child = sink.NativeContext;
        Assert.NotSame(parent, child);
        sink.PushOpacity(.25);
        sink.DrawRectangle(Brushes.Green, null, new Rect(bounds.X, bounds.Y, 3, 4));
        sink.Pop();
        Assert.Equal(3, child.Commands.Count);
        Assert.Equal(RenderCommandType.PushOpacity, child.Commands[0].Type);
        Assert.Equal(RenderCommandType.DrawRect, child.Commands[1].Type);
        Assert.Equal(Matrix4x4.Identity, child.Commands[1].Transform);
        Assert.Equal((float)bounds.X, child.Commands[1].Rect.X);
        Assert.Equal((float)bounds.Y, child.Commands[1].Rect.Y);
        Assert.Equal(RenderCommandType.PopOpacity, child.Commands[2].Type);
        sink.Pop();
        Assert.Same(parent, sink.NativeContext);
        sink.DrawRectangle(Brushes.Blue, null, new Rect(9, 10, 3, 4));
        sink.Pop();
        sink.Pop();
        sink.Pop();

        Assert.Equal(7, parent.Commands.Count);
        Assert.Equal(RenderCommandType.PushClip, parent.Commands[0].Type);
        Assert.Equal(RenderCommandType.PushOpacity, parent.Commands[1].Type);
        Assert.Equal(RenderCommandType.DrawRect, parent.Commands[2].Type);
        Assert.Equal(RenderCommandType.DrawVisual, parent.Commands[3].Type);
        Assert.NotNull(parent.Commands[3].Visual);
        Assert.Equal(transform, parent.Commands[3].Transform);
        Assert.Equal(RenderCommandType.DrawRect, parent.Commands[4].Type);
        Assert.Equal(transform, parent.Commands[4].Transform);
        Assert.Equal(RenderCommandType.PopOpacity, parent.Commands[5].Type);
        Assert.Equal(RenderCommandType.PopClip, parent.Commands[6].Type);
        Assert.Equal(0, recipe.PrepareCount);
        Assert.Equal(0, recipe.DisposeCount);
        parent.Clear();
        Assert.Equal(1, recipe.DisposeCount);
    }

    [Fact]
    public void NestedEffectsApplyEachSavedTransformOnceAndRestoreLocalCommands()
    {
        var parent = new SceneContext();
        using var sink = new ProGpuCompositionCommandSink(parent);
        var outerRecipe = new RecordingRecipe();
        var innerRecipe = new RecordingRecipe();
        using var outer = Source(outerRecipe, Bounds());
        using var inner = Source(innerRecipe, Bounds());
        var parentTransform = Matrix4x4.CreateTranslation(13, 17, 0);
        var localTransform = Matrix4x4.CreateScale(2, 4, 1);
        sink.PushNativeTransform(parentTransform);
        Assert.True(Owned(sink).PushOwnedShaderEffect(outer, Bounds()));
        var outerCommands = sink.NativeContext;
        sink.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
        sink.PushNativeTransform(localTransform);
        Assert.True(Owned(sink).PushOwnedShaderEffect(inner, Bounds()));
        sink.DrawRectangle(Brushes.Green, null, new Rect(5, 6, 3, 4));
        Assert.Equal(Matrix4x4.Identity, Assert.Single(sink.NativeContext.Commands).Transform);
        sink.Pop();
        Assert.Same(outerCommands, sink.NativeContext);
        Assert.Equal(localTransform, outerCommands.Commands[1].Transform);
        sink.Pop();
        sink.DrawRectangle(Brushes.Blue, null, new Rect(7, 8, 3, 4));
        Assert.Equal(Matrix4x4.Identity, outerCommands.Commands[2].Transform);
        sink.Pop();
        sink.Pop();
        Assert.Equal(parentTransform, Assert.Single(parent.Commands).Transform);
        Assert.Equal(0, innerRecipe.DisposeCount);
        parent.Clear();
        Assert.Equal(1, innerRecipe.DisposeCount);
        Assert.Equal(1, outerRecipe.DisposeCount);
    }

    [Fact]
    public void InheritedGuidelinesKeepOriginalMappingWhileChildTransformIsLocal()
    {
        var parent = new SceneContext();
        using var sink = new ProGpuCompositionCommandSink(parent);
        using var source = Source(new RecordingRecipe(), Bounds());
        sink.PushNativeTransform(Matrix4x4.CreateScale(2, 2, 1) * Matrix4x4.CreateTranslation(.25f, .25f, 0));
        sink.PushGuidelineY1(.25);
        sink.DrawRectangle(Brushes.Red, null, new Rect(0, .25, 2, 2));
        Assert.Equal(.375f, parent.Commands[0].Rect.Y);
        Assert.True(Owned(sink).PushOwnedShaderEffect(source, Bounds()));
        sink.DrawRectangle(Brushes.Red, null, new Rect(0, .25, 2, 2));
        var child = Assert.Single(sink.NativeContext.Commands);
        Assert.Equal(.375f, child.Rect.Y);
        Assert.Equal(Matrix4x4.Identity, child.Transform);
        sink.Pop();
        sink.DrawRectangle(Brushes.Red, null, new Rect(0, .25, 2, 2));
        Assert.Equal(parent.Commands[0].Rect.Y, parent.Commands[2].Rect.Y);
        sink.Pop();
        sink.Pop();
        parent.Clear();
    }

    [Fact]
    public void PictureCloneKeepsRecipeAndChildLeasesAfterOriginalRecorderIsReleased()
    {
        var recorder = new GpuPictureRecorder();
        var parent = recorder.BeginRecording(new SceneRect(0, 0, 100, 100));
        using var sink = new ProGpuCompositionCommandSink(parent);
        var recipe = new RecordingRecipe();
        var dependency = new RecordingResource();
        using var source = Source(recipe, Bounds());
        Assert.True(Owned(sink).PushOwnedShaderEffect(source, Bounds()));
        sink.NativeContext.RetainResource(dependency);
        sink.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 1, 1));
        sink.Pop();
        using var picture = recorder.EndRecording();
        using var clone = picture.Clone();
        picture.Dispose();
        sink.Dispose();
        Assert.Equal(0, recipe.DisposeCount);
        Assert.Equal(0, dependency.DisposeCount);
        Assert.Equal(0, recipe.PrepareCount);
        clone.Dispose();
        Assert.Equal(1, recipe.DisposeCount);
        Assert.Equal(1, dependency.DisposeCount);
    }

    [Fact]
    public void RejectedBoundsAndTranslationDoNotAdoptSourceOrChangeParent()
    {
        var parent = new SceneContext();
        using var sink = new ProGpuCompositionCommandSink(parent);
        var recipe = new RecordingRecipe();
        var bounds = Bounds();
        using var source = Source(recipe, bounds);
        var roundedBounds = new WpfReplayRect((float)bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Assert.Equal((float)bounds.X, (float)roundedBounds.X);
        Assert.NotEqual(BitConverter.DoubleToInt64Bits(bounds.X), BitConverter.DoubleToInt64Bits(roundedBounds.X));
        Assert.False(Owned(sink).PushOwnedShaderEffect(source, roundedBounds));
        var translatedRecipe = new RecordingRecipe();
        using var translated = new OwnedShaderEffectSource(source.SourceCapture, Vector2.One, translatedRecipe);
        Assert.False(Owned(sink).PushOwnedShaderEffect(translated, bounds));
        Assert.Same(parent, sink.NativeContext);
        Assert.Empty(parent.Commands);
        Assert.Equal(0, recipe.DisposeCount);
        Assert.Equal(0, translatedRecipe.DisposeCount);
        source.Dispose();
        translated.Dispose();
        Assert.Equal(1, recipe.DisposeCount);
        Assert.Equal(1, translatedRecipe.DisposeCount);
    }

    [Fact]
    public void IncompleteNestedScopesAbortAllOwnersEvenWhenInnerCleanupFails()
    {
        var parent = new SceneContext();
        var sink = new ProGpuCompositionCommandSink(parent);
        var outerRecipe = new RecordingRecipe();
        var innerRecipe = new RecordingRecipe { DisposalError = new InvalidOperationException("inner cleanup") };
        var dependency = new RecordingResource();
        using var outer = Source(outerRecipe, Bounds());
        using var inner = Source(innerRecipe, Bounds());
        sink.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 1, 1));
        Assert.True(Owned(sink).PushOwnedShaderEffect(outer, Bounds()));
        sink.NativeContext.RetainResource(dependency);
        Assert.True(Owned(sink).PushOwnedShaderEffect(inner, Bounds()));
        sink.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 1, 1));
        Assert.Same(innerRecipe.DisposalError, Assert.Throws<InvalidOperationException>(() => sink.Dispose()));
        Assert.Equal(1, outerRecipe.DisposeCount);
        Assert.Equal(1, innerRecipe.DisposeCount);
        Assert.Equal(1, dependency.DisposeCount);
        Assert.Equal(RenderCommandType.DrawRect, Assert.Single(parent.Commands).Type);
        Assert.Throws<ObjectDisposedException>(() => sink.Pop());
        sink.Dispose();
        parent.Clear();
    }

    [Fact]
    public void MediaFallbackIsUnavailableOnlyWithinOwnedChild()
    {
        var parent = new SceneContext();
        var media = new System.Windows.Media.DrawingContext(parent);
        using var sink = new ProGpuCompositionCommandSink(media);
        using var source = Source(new RecordingRecipe(), Bounds());
        Assert.Same(media, sink.DrawingContext);
        Assert.True(Owned(sink).PushOwnedShaderEffect(source, Bounds()));
        Assert.Null(sink.DrawingContext);
        sink.PushTransform(new TranslateTransform(2, 3));
        sink.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 1, 1));
        Assert.Empty(parent.Commands);
        Assert.Equal(Matrix4x4.CreateTranslation(2, 3, 0), Assert.Single(sink.NativeContext.Commands).Transform);
        sink.Pop();
        sink.Pop();
        Assert.Same(media, sink.DrawingContext);
        Assert.Equal(RenderCommandType.DrawVisual, Assert.Single(parent.Commands).Type);
        parent.Clear();
    }

    private static IWpfOwnedShaderEffectCommandSink Owned(ProGpuCompositionCommandSink sink) => sink;
    private static WpfReplayRect Bounds() => new(10.000000000001, 20.25, 32, 24);
    private static OwnedShaderEffectSource Source(RecordingRecipe recipe, WpfReplayRect bounds) =>
        new(new ShaderEffectSourceCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height, 1, 2, 3, 4), Vector2.Zero, recipe);

    private sealed class RecordingRecipe : IShaderEffectPreparation
    {
        public int PrepareCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? DisposalError { get; init; }
        public OwnedShaderEffectParameters Prepare(ShaderEffectPreparationContext context)
        {
            PrepareCount++;
            throw new InvalidOperationException("Recording must not prepare a target or allocate a sampler.");
        }
        public void Dispose()
        {
            DisposeCount++;
            if (DisposalError != null) throw DisposalError;
        }
    }

    private sealed class RecordingResource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
