using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Backend;
using ProGPU.Scene;
using Xunit;
using ProGpuDrawingContext = ProGPU.Scene.DrawingContext;
using MediaDrawingContext = System.Windows.Media.DrawingContext;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfReplayToProGpuCommandTests
{
    [Fact]
    public void RetainedRepeatedImageScopesKeepIndependentOwnersAndLeaseRetirement()
    {
        var texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));
        var source = new FakeTextureLeaseSource(texture);
        var image = new FakePortableNativeMediaImageSource(source);
        var first = new ProGpuRetainedDrawingVisual();
        var second = new ProGpuRetainedDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 64, 64);
        foreach (var owner in new[] { first, second })
        {
            using var sink = new ProGpuRetainedCompositionCommandSink(frame, owner, context: null, viewport3DTextureCache: null);
            var repeated = (IWpfRepeatedImageCommandSink)sink;
            sink.PushBitmapScalingMode("NearestNeighbor");
            Assert.False(repeated.SupportsRepeatedLinearImages);
            Assert.False(repeated.TryDrawRepeatedImage(image, new System.Windows.Rect(0, 0, 16, 24), true, false));
            sink.Pop();
            Assert.True(repeated.SupportsRepeatedLinearImages);
            Assert.True(repeated.TryDrawRepeatedImage(image, new System.Windows.Rect(0, 0, 16, 24), true, false));
            Assert.True(repeated.TryDrawRepeatedImage(image, new System.Windows.Rect(16, 0, 16, 24), true, false));
            Assert.Equal(2, owner.Context.Commands.Count);
            Assert.Equal(1, owner.Context.RetainedResourceCount);
            Assert.All(owner.Context.Commands, command =>
            {
                Assert.Same(texture, command.Texture);
                Assert.Equal(TextureAddressMode.MirrorRepeat, command.TextureAddressModeU);
                Assert.Equal(TextureAddressMode.Repeat, command.TextureAddressModeV);
            });
        }
        Assert.Equal(2, source.AcquireCount);
        Assert.Equal(0, source.LeaseDisposeCount);
        first.Context.Clear();
        Assert.Equal(1, source.LeaseDisposeCount);
        Assert.Equal(2, second.Context.Commands.Count);
        second.Context.Clear();
        Assert.Equal(2, source.LeaseDisposeCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RepeatedImageCommandRetainsExactAddressingTransformAndOriginalTextureLease(bool mirrorX, bool mirrorY)
    {
        var texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));
        var textureSource = new FakeTextureLeaseSource(texture);
        var source = new FakePortableNativeMediaImageSource(textureSource);
        var context = new ProGpuDrawingContext();
        using (var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(context)))
        {
            sink.PushTransform(new MatrixTransform(1, 0, 0, 1, 8, 10));
            var repeated = (IWpfRepeatedImageCommandSink)sink;
            Assert.True(repeated.SupportsRepeatedLinearImages);
            Assert.True(repeated.TryDrawRepeatedImage(source, new System.Windows.Rect(-16, 0, 16, 24), mirrorX, mirrorY));
            sink.Pop();
        }
        var command = Assert.Single(context.Commands);
        Assert.Equal(RenderCommandType.DrawTexture, command.Type);
        Assert.Same(texture, command.Texture);
        Assert.Equal(TextureSamplingMode.Linear, command.TextureSamplingMode);
        Assert.Equal(mirrorX ? TextureAddressMode.MirrorRepeat : TextureAddressMode.Repeat, command.TextureAddressModeU);
        Assert.Equal(mirrorY ? TextureAddressMode.MirrorRepeat : TextureAddressMode.Repeat, command.TextureAddressModeV);
        Assert.Equal(-16, command.Rect.X);
        Assert.Equal(16, command.Rect.Width);
        Assert.Equal(24, command.Rect.Height);
        Assert.Equal(default, command.SrcRect);
        Assert.Equal(8, command.Transform.M41);
        Assert.Equal(10, command.Transform.M42);
        Assert.Equal(1, context.RetainedResourceCount);
        Assert.Equal(1, textureSource.AcquireCount);
        Assert.Equal(0, textureSource.LeaseDisposeCount);
        context.Clear();
        Assert.Equal(1, textureSource.LeaseDisposeCount);
    }

    [Theory]
    [InlineData("NearestNeighbor")]
    [InlineData("HighQuality")]
    [InlineData("Fant")]
    public void RepeatedImageDeclinesOtherSamplingBeforeLeaseOrPublicationAndRestoresMode(string mode)
    {
        var texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));
        var source = new FakeTextureLeaseSource(texture);
        var image = new FakePortableNativeMediaImageSource(source);
        var context = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(context));
        var repeated = (IWpfRepeatedImageCommandSink)sink;
        sink.PushBitmapScalingMode(mode);
        Assert.False(repeated.SupportsRepeatedLinearImages);
        Assert.False(repeated.TryDrawRepeatedImage(image, new System.Windows.Rect(0, 0, 16, 24), false, false));
        Assert.Empty(context.Commands);
        Assert.Equal(0, source.AcquireCount);
        Assert.Equal(0, context.RetainedResourceCount);
        sink.Pop();
        Assert.True(repeated.SupportsRepeatedLinearImages);
        Assert.True(repeated.TryDrawRepeatedImage(image, new System.Windows.Rect(0, 0, 16, 24), false, false));
        Assert.Single(context.Commands);
        context.Clear();
        Assert.Equal(1, source.LeaseDisposeCount);
    }
}
