using System.Numerics;
using ProGPU.Scene;
using ProGPU.Vector;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfRetainedSourceOpacityTests
{
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
