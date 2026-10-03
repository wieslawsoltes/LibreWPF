using System.Numerics;
using ProGPU.Scene;
using ProGPU.Vector;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfRetainedSourceOpacityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedProductVisualOptsIntoRasterOnlyMasksWithoutLosingSourceClips(bool pictureMask)
    {
        var root = new ProGpuRetainedDrawingVisual { Opacity = 0 };
        var masked = new ProGpuRetainedDrawingVisual
        {
            HitTestId = 1, ClipBounds = new Rect(10, 12, 20, 18),
            OpacityMaskBounds = new Rect(100, 100, 1, 1)
        };
        var recorder = new GpuPictureRecorder();
        var maskContext = recorder.BeginRecording(new Rect(100, 100, 1, 1));
        maskContext.DrawRectangle(new SolidColorBrush(Vector4.Zero), null, new Rect(100, 100, 1, 1));
        using var picture = recorder.EndRecording();
        if (pictureMask) masked.OpacityMaskPicture = picture;
        else masked.OpacityMask = new SolidColorBrush(Vector4.Zero);
        masked.Context.DrawRectangle(new SolidColorBrush(Vector4.One), null, new Rect(8, 10, 32, 24));
        var child = new ProGpuRetainedDrawingVisual { HitTestId = 7 };
        child.Context.DrawRectangle(new SolidColorBrush(Vector4.One), null, new Rect(20, 20, 16, 16));
        var sibling = new ProGpuRetainedDrawingVisual { HitTestId = 9 };
        sibling.Context.DrawRectangle(new SolidColorBrush(Vector4.One), null, new Rect(8, 10, 32, 24));
        masked.AddChild(child); root.AddChild(masked); root.AddChild(sibling);
        Assert.True(masked.SourceOpacityMaskPreservesHitGeometry);
        Assert.Same(masked.Context, ((ISourceGeometryHitTestCommands)masked).SourceHitTestCommands);
        using var capture = new GpuRenderCommandHitTestCacheBuilder();
        for (int phase = 0; phase < 4; phase++)
        {
            if (phase == 1) root.Opacity = 1;
            if (phase == 2) masked.IsVisible = false;
            if (phase == 3) masked.IsVisible = true;
            capture.Clear();
            capture.AddSourceVisual(root, Matrix4x4.Identity);
            var hits = capture.BuildIndex().Primitives;
            Assert.Equal(phase == 2 ? 1 : 3, hits.Count);
            if (phase != 2)
            {
                Assert.Equal(1, hits[0].Id);
                Assert.Equal(new Vector2(10, 12), hits[0].BoundsMin);
                Assert.Equal(new Vector2(30, 30), hits[0].BoundsMax);
                Assert.Equal(7, hits[1].Id);
                Assert.Equal(new Vector2(20, 20), hits[1].BoundsMin);
                Assert.Equal(new Vector2(30, 30), hits[1].BoundsMax);
            }
            Assert.Equal(9, hits[^1].Id);
            Assert.Equal(new Vector2(8, 10), hits[^1].BoundsMin);
            Assert.Equal(new Vector2(40, 34), hits[^1].BoundsMax);
            Assert.Equal(0u, hits[^1].ClipSegmentCount);
        }
        if (pictureMask) Assert.Same(picture, masked.OpacityMaskPicture);
        else Assert.NotNull(masked.OpacityMask);
    }

    [Fact]
    public void RetainedProductVisualPublishesItsActualCommandsAtZeroOpacity()
    {
        var visual = new ProGpuRetainedDrawingVisual
        {
            Opacity = 0, HitTestId = 431, Size = new Vector2(1000)
        };
        visual.Context.DrawRectangle(new SolidColorBrush(Vector4.One), null, new Rect(10, 20, 30, 40));
        var source = Assert.IsAssignableFrom<ISourceGeometryHitTestCommands>(visual);
        Assert.Same(visual.Context, source.SourceHitTestCommands);
        using var capture = new GpuRenderCommandHitTestCacheBuilder();
        capture.AddSourceVisual(visual, Matrix4x4.Identity);
        var primitive = Assert.Single(capture.BuildIndex().Primitives);
        Assert.Equal(431, primitive.Id);
        Assert.Equal(new Vector2(10, 20), primitive.BoundsMin);
        Assert.Equal(new Vector2(40, 60), primitive.BoundsMax);
        Assert.Equal(0f, visual.Opacity);
    }
}
