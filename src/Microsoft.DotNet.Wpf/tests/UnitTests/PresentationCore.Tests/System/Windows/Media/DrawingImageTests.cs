using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Tests;

public sealed class DrawingImageTests
{
    [Fact]
    public void PortableDrawingImageSourcePublishesSourceBuiltDrawingWithoutCopying()
    {
        var drawing = new GeometryDrawing(
            Brushes.Blue,
            null,
            new RectangleGeometry(new Rect(10, 20, 30, 40)));
        var image = new DrawingImage(drawing);

        var source = Assert.IsAssignableFrom<IPortableDrawingImageSource>(image);
        Assert.True(source.TryGetPortableDrawingImage(out var portableDrawing));
        Assert.Same(drawing, portableDrawing);
    }

    [Fact]
    public void EmptyDrawingImageFailsClosedWithoutSyntheticContent()
    {
        var source = Assert.IsAssignableFrom<IPortableDrawingImageSource>(new DrawingImage());

        Assert.False(source.TryGetPortableDrawingImage(out var portableDrawing));
        Assert.Null(portableDrawing);
    }

    [Fact]
    public void PortableDrawingBoundsIncludeGeometryPenExtents()
    {
        var drawing = new GeometryDrawing(
            null,
            new Pen(Brushes.Black, 4),
            new RectangleGeometry(new Rect(10, 20, 20, 10)));

        var source = Assert.IsAssignableFrom<IPortableDrawingBoundsSource>(drawing);

        Assert.True(source.TryGetPortableDrawingBounds(out var bounds));
        Assert.Equal(drawing.Bounds.X, bounds.X);
        Assert.Equal(drawing.Bounds.Y, bounds.Y);
        Assert.Equal(drawing.Bounds.Width, bounds.Width);
        Assert.Equal(drawing.Bounds.Height, bounds.Height);
        Assert.Equal(new Rect(8, 18, 24, 14), drawing.Bounds);
    }

    [Fact]
    public void PortableDrawingBoundsIncludeDrawingGroupTransformExactlyOnce()
    {
        var drawing = new DrawingGroup
        {
            Transform = new TranslateTransform(30, 40)
        };
        drawing.Children.Add(new GeometryDrawing(
            Brushes.Blue,
            null,
            new RectangleGeometry(new Rect(10, 20, 20, 10))));

        var source = Assert.IsAssignableFrom<IPortableDrawingBoundsSource>(drawing);

        Assert.True(source.TryGetPortableDrawingBounds(out var bounds));
        Assert.Equal(new Rect(40, 60, 20, 10), drawing.Bounds);
        Assert.Equal(drawing.Bounds.X, bounds.X);
        Assert.Equal(drawing.Bounds.Y, bounds.Y);
        Assert.Equal(drawing.Bounds.Width, bounds.Width);
        Assert.Equal(drawing.Bounds.Height, bounds.Height);
    }

    [Fact]
    public void PortableDrawingGroupStateSeparatesLocalAndPostTransformBounds()
    {
        var drawing = new DrawingGroup
        {
            Transform = new TranslateTransform(30, 40),
            ClipGeometry = new RectangleGeometry(
                new Rect(12, 21, 10, 8))
        };
        drawing.Children.Add(new GeometryDrawing(
            Brushes.Blue,
            null,
            new RectangleGeometry(new Rect(10, 20, 20, 10))));

        var source = Assert.IsAssignableFrom<IPortableDrawingGroupStateSource>(
            drawing);

        Assert.True(source.TryGetPortableDrawingGroupState(out var state));
        Assert.True(state.HasBounds);
        Assert.Equal(new PortableRect(42, 61, 10, 8), state.Bounds);
        Assert.True(state.HasLocalBounds);
        Assert.Equal(new PortableRect(12, 21, 10, 8), state.LocalBounds);
    }
}
