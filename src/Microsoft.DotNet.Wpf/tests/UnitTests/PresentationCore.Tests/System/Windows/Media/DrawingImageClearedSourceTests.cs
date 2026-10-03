using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Tests;

// Exercises the actual source exporter, independently of the shader recipe's
// typed fixture. These observations do not qualify shader pixels or GPU state.
public sealed class DrawingImageClearedSourceTests
{
    [Fact]
    public void SameImageExportsAbsentDrawingAndOriginalRefilledDrawing()
    {
        var image = new DrawingImage();
        var source = Assert.IsAssignableFrom<IPortableDrawingImageSource>(image);
        var drawing = new GeometryDrawing(Brushes.Lime, null,
            new RectangleGeometry(new Rect(10, 20, 8, 6)));

        Assert.False(source.TryGetPortableDrawingImage(out var content));
        Assert.Null(content);
        Assert.Equal(0, image.Width);
        Assert.Equal(0, image.Height);

        image.Drawing = drawing;
        Assert.True(source.TryGetPortableDrawingImage(out content));
        Assert.Same(drawing, content);
        Assert.Equal(8, image.Width);
        Assert.Equal(6, image.Height);

        image.Drawing = null;
        Assert.False(source.TryGetPortableDrawingImage(out content));
        Assert.Null(content);
        Assert.Equal(0, image.Width);
        Assert.Equal(0, image.Height);

        image.Drawing = drawing;
        Assert.True(source.TryGetPortableDrawingImage(out content));
        Assert.Same(drawing, content);
        Assert.True(((IPortableDrawingBoundsSource)drawing).TryGetPortableDrawingBounds(out var bounds));
        Assert.Equal(new PortableRect(10, 20, 8, 6), bounds);
    }

    [Fact]
    public void ClearingActualGroupKeepsPresentDrawingWithKnownEmptyBounds()
    {
        var group = new DrawingGroup();
        var drawing = new GeometryDrawing(Brushes.Lime, null,
            new RectangleGeometry(new Rect(10, 20, 8, 6)));
        var image = new DrawingImage(group);
        var source = (IPortableDrawingImageSource)image;
        var boundsSource = (IPortableDrawingBoundsSource)group;

        Assert.True(source.TryGetPortableDrawingImage(out var content));
        Assert.Same(group, content);
        Assert.True(boundsSource.TryGetPortableDrawingBounds(out var bounds));
        Assert.True(bounds.IsEmpty);

        group.Children.Add(drawing);
        Assert.True(source.TryGetPortableDrawingImage(out content));
        Assert.Same(group, content);
        Assert.True(boundsSource.TryGetPortableDrawingBounds(out bounds));
        Assert.Equal(new PortableRect(10, 20, 8, 6), bounds);

        group.Children.Clear();
        Assert.True(source.TryGetPortableDrawingImage(out content));
        Assert.Same(group, content);
        Assert.True(boundsSource.TryGetPortableDrawingBounds(out bounds));
        Assert.True(bounds.IsEmpty);
        Assert.Equal(0, image.Width);
        Assert.Equal(0, image.Height);

        image.Drawing = null;
        Assert.False(source.TryGetPortableDrawingImage(out content));
        Assert.Null(content);
        image.Drawing = group;
        group.Children.Add(drawing);
        Assert.True(source.TryGetPortableDrawingImage(out content));
        Assert.Same(group, content);
        Assert.Same(drawing, Assert.Single(group.Children));
        Assert.True(boundsSource.TryGetPortableDrawingBounds(out bounds));
        Assert.Equal(new PortableRect(10, 20, 8, 6), bounds);
    }
}
