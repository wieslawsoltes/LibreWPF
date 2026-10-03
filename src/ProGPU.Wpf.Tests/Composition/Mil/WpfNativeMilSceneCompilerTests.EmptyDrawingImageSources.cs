using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void EmptyDrawingImageClosureRetainsReusedWrappersInEitherOrder(
        bool shaderFirst, bool imageBrush, bool nestedImage)
    {
        var target = new SamplerVisual(null) { Empty = true };
        var inner = new ShaderCacheBrush(new(target));
        object image = new FakeDrawingImage(new EmptyCacheImageDrawing(inner));
        if (nestedImage) image = new FakeDrawingImage(new EmptyOwnedImageDrawing(image));
        var content = imageBrush
            ? new FakeRenderData(CreateRectangleRecord(1, 0), [new ShaderImageBrush(image)])
            : new FakeRenderData(CreateDrawImageRecord(1, 2, 30, 20, 1), [image]);
        var source = new FakeVisual(content);
        var ordinary = new FakeVisual(content);
        var receiver = ShaderVisual(VisualSamplerEffect(new ShaderCacheBrush(new(source))));
        using var batch = CreateCacheShaderCompiler().BuildBatch(
            new FakeVisual(null, null, shaderFirst ? [receiver, ordinary] : [ordinary, receiver]), 64, 64);

        Assert.Equal(nestedImage ? 2 : 1, batch.EmptyDrawingImageSources.Length);
        Assert.Single(batch.EmptyCacheBrushSources.ToArray());
        Assert.Single(batch.EmptyVisualBrushSources.ToArray());
        Assert.Empty(batch.DrawingImageBounds!);
        int[] packets = CacheRasterCommandOffsets(batch.Bytes, 0x71);
        Assert.Equal(batch.EmptyDrawingImageSources.Length, packets.Length);
        foreach (var edge in batch.EmptyDrawingImageSources.Span)
        {
            int packet = Assert.Single(packets, value => ReadUInt32(batch.Bytes, value + 8) == edge.ImageHandle);
            Assert.Equal(0U, ReadUInt32(batch.Bytes, packet + 12));
            Assert.NotEqual(0U, edge.DrawingHandle);
            Assert.Contains(CacheRasterCommandOffsets(batch.Bytes, 0x07), value =>
                ReadUInt32(batch.Bytes, value + 8) == edge.DrawingHandle);
        }
        if (nestedImage)
        {
            int wrapper = FindCommand(batch.Bytes, 0x89);
            uint nested = ReadUInt32(batch.Bytes, wrapper + 44);
            Assert.Contains(batch.EmptyDrawingImageSources.ToArray(), edge => edge.ImageHandle == nested);
            Assert.Contains(batch.EmptyDrawingImageSources.ToArray(), edge =>
                edge.DrawingHandle == ReadUInt32(batch.Bytes, wrapper + 8));
        }
        using var copied = batch with { };
        Assert.Equal(batch.EmptyDrawingImageSources.ToArray(), copied.EmptyDrawingImageSources.ToArray());
    }

    [Theory]
    [InlineData(0)] // original ImageBrush sampler
    [InlineData(1)] // original VisualBrush source
    [InlineData(2)] // original BitmapCacheBrush source
    public void EmptyDrawingImageWitnessDoesNotDependOnHavingNestedCacheBrushes(int family)
    {
        var drawing = new FakeGeometryDrawing(null, null, null, PortableRect.Empty);
        var image = new FakeDrawingImage(drawing);
        var source = new FakeVisual(new FakeRenderData(CreateDrawImageRecord(1, 2, 30, 20, 1), [image]));
        ShaderEffectSource effect;
        if (family == 0)
        {
            var brush = new ShaderImageBrush(image);
            effect = new(new()) { Samplers = [PortableShaderSampler.Image(0, image,
                PortableShaderSamplingMode.NearestNeighbor, brush)] };
        }
        else effect = VisualSamplerEffect(family == 1
            ? new VisualSamplerBrush(source) : new ShaderCacheBrush(new(source)));
        using var batch = CreateCacheShaderCompiler().BuildBatch(ShaderVisual(effect), 64, 64);
        Assert.Single(batch.EmptyDrawingImageSources.ToArray());
        Assert.Empty(batch.EmptyCacheBrushSources.ToArray());
        Assert.Empty(batch.DrawingImageBounds!);
    }

    [Fact]
    public void NullDrawingImageRemainsDistinctFromRetainedKnownEmptyDrawing()
    {
        var image = new MutableOwnedDrawingImage(null);
        var source = new FakeVisual(new FakeRenderData(CreateDrawImageRecord(1, 2, 30, 20, 1), [image]));
        var receiver = ShaderVisual(VisualSamplerEffect(new ShaderCacheBrush(new(source))));
        var compiler = CreateCacheShaderCompiler();
        using var absent = compiler.BuildBatch(receiver, 64, 64);
        Assert.Empty(absent.EmptyDrawingImageSources.ToArray());
        image.Drawing = new FakeGeometryDrawing(null, null, null, PortableRect.Empty);
        using var empty = compiler.BuildBatch(receiver, 64, 64);
        var edge = Assert.Single(empty.EmptyDrawingImageSources.ToArray());
        Assert.NotEqual(0U, edge.DrawingHandle);
        Assert.Equal(0U, ReadUInt32(empty.Bytes, FindCommand(empty.Bytes, 0x71) + 12));
        using var ordinary = new WpfNativeMilSceneCompiler().BuildBatch(source, 64, 64);
        Assert.Empty(ordinary.EmptyDrawingImageSources.ToArray());
        Assert.Empty(ordinary.DrawingImageBounds!);
        Assert.Equal(0U, ReadUInt32(ordinary.Bytes, FindCommand(ordinary.Bytes, 0x71) + 12));
    }

    [Fact]
    public void EmptyDrawingImageDoesNotEraseActualDrawingCycle()
    {
        var image = new MutableOwnedDrawingImage(null);
        image.Drawing = new EmptyOwnedImageDrawing(image);
        var source = new FakeVisual(new FakeRenderData(CreateDrawImageRecord(1, 2, 30, 20, 1), [image]));
        Assert.Contains("cycle", Assert.Throws<InvalidOperationException>(() =>
            CacheShaderBatch(VisualSamplerEffect(new ShaderCacheBrush(new(source))))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyDrawingImageStillValidatesHiddenNestedSource(bool ordinaryFirst)
    {
        var invalid = new CacheRasterVisual(null,
            new() { HasVisibility = true, Visibility = PortableVisualVisibility.Hidden },
            PortableRect.Empty, new object());
        var image = new FakeDrawingImage(new EmptyCacheImageDrawing(new ShaderCacheBrush(new(invalid))));
        var source = new FakeVisual(new FakeRenderData(CreateDrawImageRecord(1, 2, 30, 20, 1), [image]));
        using var ordinary = new WpfNativeMilSceneCompiler().BuildBatch(source, 64, 64);
        byte[] bytes = (byte[])ordinary.Bytes.Clone();
        var receiver = ShaderVisual(VisualSamplerEffect(new ShaderCacheBrush(new(source))));
        var root = ordinaryFirst ? new FakeVisual(null, null, source, receiver) : receiver;
        Assert.Contains(nameof(IPortableVisualStateSource), Assert.Throws<InvalidOperationException>(() =>
            CreateCacheShaderCompiler().BuildBatch(root, 64, 64)).Message);
        Assert.Equal(bytes, ordinary.Bytes);
        Assert.Empty(ordinary.EmptyDrawingImageSources.ToArray());
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative)]
    [InlineData(NativeMilBackend.Dawn)]
    public void EmptyDrawingImageSessionOwnsTransitionsAndRejectsFailedWitnessAtomically(NativeMilBackend backend)
    {
        var firstDrawing = new FakeGeometryDrawing(null, null, null, PortableRect.Empty);
        var image = new MutableOwnedDrawingImage(firstDrawing);
        var source = new FakeVisual(new FakeRenderData(CreateDrawImageRecord(1, 2, 30, 20, 1), [image]));
        var receiver = ShaderVisual(VisualSamplerEffect(new ShaderCacheBrush(new(source))));
        var compiler = CreateCacheShaderCompiler();
        using var session = new WpfNativeMilCompilationSession(backend, compiler);
        using var initial = compiler.BuildBatch(receiver, 64, 64);
        var edge = Assert.Single(initial.EmptyDrawingImageSources.ToArray());
        Assert.True(session.Update(initial).RecreatedChannel);
        var retained = session.CompileFrame(11957, 1, 0, 1);
        byte[] bytes = retained.Scene.Stream.ToArray();
        Assert.False(session.Update(initial).RecreatedChannel);

        // Same bytes with a changed source edge must use a candidate channel.
        // This references an initialized wrong-family resource, not a missing
        // handle accepted as empty. Failed sidebands cannot replace the old owner.
        using var invalid = initial with
        {
            EmptyDrawingImageSources = new WpfNativeMilEmptyDrawingImageSource[]
                { new(edge.ImageHandle, initial.TargetHandle) }
        };
        Assert.True(WpfNativeMilCompilationSession.CreateDelta(initial, invalid).RequiresRebuild);
        Assert.ThrowsAny<Exception>(() => session.Update(invalid));
        Assert.Equal(bytes, session.CompileFrame(11957, 1, 0, 1).Scene.Stream.ToArray());

        image.Drawing = new FakeGeometryDrawing(new FakeBrush(new(255, 0, 255, 0)), null,
            new FakePrimitiveGeometry(PortablePrimitiveGeometry.Rectangle(
                new(1, 2, 30, 20), 0, 0, PortableMatrix3x2.Identity)));
        using var positive = compiler.BuildBatch(receiver, 64, 64);
        Assert.Empty(positive.EmptyDrawingImageSources.ToArray());
        Assert.Single(positive.DrawingImageBounds!);
        Assert.NotEqual(0U, ReadUInt32(positive.Bytes, FindCommand(positive.Bytes, 0x71) + 12));
        Assert.True(session.Update(positive).RecreatedChannel);
        var filled = session.CompileFrame(11957, 2, 0, 2);
        byte[] filledBytes = filled.Scene.Stream.ToArray();

        image.Drawing = new object();
        Assert.Throws<InvalidOperationException>(() => session.Update(receiver, 64, 64));
        Assert.Equal(filledBytes, session.CompileFrame(11957, 2, 0, 2).Scene.Stream.ToArray());
        image.Drawing = null;
        using var disconnected = compiler.BuildBatch(receiver, 64, 64);
        Assert.Empty(disconnected.EmptyDrawingImageSources.ToArray());
        Assert.True(session.Update(disconnected).RecreatedChannel);
        image.Drawing = firstDrawing;
        using var restored = compiler.BuildBatch(receiver, 64, 64);
        Assert.Equal(edge, Assert.Single(restored.EmptyDrawingImageSources.ToArray()));
        Assert.True(session.Update(restored).RecreatedChannel);
        Assert.NotEmpty(session.CompileFrame(11957, 3, 0, 3).Scene.Stream);
        Assert.Equal(bytes, retained.Scene.Stream.ToArray());
        Assert.Equal(filledBytes, filled.Scene.Stream.ToArray());
    }

    private sealed class MutableOwnedDrawingImage(object? drawing) : IPortableDrawingImageSource
    {
        internal object? Drawing = drawing;
        public bool TryGetPortableDrawingImage(out object? value) { value = Drawing; return value is not null; }
    }

    private sealed class EmptyOwnedImageDrawing(object image) : IPortableImageDrawingStateSource, IPortableDrawingBoundsSource
    {
        public bool TryGetPortableDrawingBounds(out PortableRect bounds) { bounds = PortableRect.Empty; return true; }
        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = new() { HasImageSource = true, ImageSource = image, HasRect = true, Rect = new(0, 0, 0, 0) };
            return true;
        }
    }
}
