using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Animation.Tests;

public class AnimationClockResourceTests
{
    [Fact]
    public void GeneratedResourcesPublishPortableBaseValuesWithoutAClock()
    {
        var doubleSource = Assert.IsAssignableFrom<
            IPortableDoubleAnimationValueSource>(
                new DoubleAnimationClockResource(0.25, null));
        Assert.True(doubleSource.TryGetPortableDoubleAnimationValue(
            out double doubleValue));
        Assert.Equal(0.25, doubleValue);

        var pointSource = Assert.IsAssignableFrom<
            IPortablePointAnimationValueSource>(
                new PointAnimationClockResource(new Point(1, 2), null));
        Assert.True(pointSource.TryGetPortablePointAnimationValue(
            out PortablePoint pointValue));
        Assert.Equal(1, pointValue.X);
        Assert.Equal(2, pointValue.Y);

        var sizeSource = Assert.IsAssignableFrom<
            IPortableSizeAnimationValueSource>(
                new SizeAnimationClockResource(new Size(3, 4), null));
        Assert.True(sizeSource.TryGetPortableSizeAnimationValue(
            out PortableSize sizeValue));
        Assert.Equal(3, sizeValue.Width);
        Assert.Equal(4, sizeValue.Height);

        var rectSource = Assert.IsAssignableFrom<
            IPortableRectAnimationValueSource>(
                new RectAnimationClockResource(new Rect(5, 6, 7, 8), null));
        Assert.True(rectSource.TryGetPortableRectAnimationValue(
            out PortableRect rectValue));
        Assert.Equal(5, rectValue.X);
        Assert.Equal(6, rectValue.Y);
        Assert.Equal(7, rectValue.Width);
        Assert.Equal(8, rectValue.Height);
    }

    [Fact]
    public void AnimationResourcePublishesTypedClockInvalidationSubscription()
    {
        AnimationClock clock = new DoubleAnimation(0.25, 0.75,
            new Duration(TimeSpan.FromSeconds(1))).CreateClock();
        var resource = new DoubleAnimationClockResource(0.5, clock);
        var invalidationSource = Assert.IsAssignableFrom<
            IPortableInvalidationSource>(resource);

        Assert.True(invalidationSource.TrySubscribeInvalidated(
            (_, _) => { },
            out IDisposable subscription));

        subscription.Dispose();
    }
}
