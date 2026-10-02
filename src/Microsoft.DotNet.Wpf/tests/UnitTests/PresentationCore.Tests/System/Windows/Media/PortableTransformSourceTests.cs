using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Tests;

public sealed class PortableTransformSourceTests
{
    private static PortableTransform Capture(Transform source)
    {
        Assert.True(((IPortableTransformSource)source).TryGetPortableTransform(out var result));
        return result;
    }

    [Fact]
    public void ActualPrimitiveResourcesPublishCurrentDoublesWithoutCallingAggregateValue()
    {
        var rotation = new RotateTransform(double.BitIncrement(360000000.25), -0.0, 4);
        var r = Assert.IsType<PortableRotateTransform>(Capture(rotation));
        Assert.Equal(BitConverter.DoubleToInt64Bits(rotation.Angle), BitConverter.DoubleToInt64Bits(r.Angle));
        Assert.Equal(BitConverter.DoubleToInt64Bits(rotation.CenterX), BitConverter.DoubleToInt64Bits(r.CenterX));
        Assert.Equal(4, r.CenterY);
        rotation.Angle = 17;
        Assert.NotEqual(17, r.Angle); Assert.Equal(17, Assert.IsType<PortableRotateTransform>(Capture(rotation)).Angle);
        Assert.Equal(new PortableScaleTransform(1.25, -.5, 3, 4), Capture(new ScaleTransform(1.25, -.5, 3, 4)));
        Assert.Equal(new PortableSkewTransform(721.25, -45, 3, 4), Capture(new SkewTransform(721.25, -45, 3, 4)));
        Assert.Equal(new PortableTranslateTransform(.25, -.5), Capture(new TranslateTransform(.25, -.5)));
        var matrix = Assert.IsType<PortableMatrixTransform>(Capture(new MatrixTransform(1, .25, -.5, 2, 3, 4)));
        Assert.Equal(.25, matrix.Matrix.M12); Assert.Equal(-.5, matrix.Matrix.M21);
    }

    [Fact]
    public void ActualGroupSnapshotOwnsOrderButRetainsSourceIdentityAndCurrentChildState()
    {
        var scale = new ScaleTransform(2, 3); var rotate = new RotateTransform(17);
        var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(rotate); group.Children.Add(scale);
        var old = Assert.IsType<PortableTransformGroup>(Capture(group));
        group.Children.Clear(); scale.ScaleX = 4;
        Assert.Equal(3, old.Children.Count); Assert.Same(scale, old.Children[0]);
        Assert.Same(rotate, old.Children[1]); Assert.Same(scale, old.Children[2]);
        Assert.Equal(4, Assert.IsType<PortableScaleTransform>(Capture((Transform)old.Children[0])).ScaleX);
        Assert.Empty(Assert.IsType<PortableTransformGroup>(Capture(group)).Children);
        var clone = group.Clone(); clone.Children.Add(rotate); clone.Freeze();
        Assert.Same(rotate, Assert.IsType<PortableTransformGroup>(Capture(clone)).Children[0]);
    }
}
