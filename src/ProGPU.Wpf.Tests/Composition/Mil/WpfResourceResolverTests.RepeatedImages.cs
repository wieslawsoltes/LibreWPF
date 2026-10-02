using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfResourceResolverTests
{
    [Theory]
    [InlineData(false, PortableTileMode.Tile, false, false, 0)]
    [InlineData(true, PortableTileMode.Tile, false, false, 0)]
    [InlineData(false, PortableTileMode.FlipX, true, false, 2)]
    [InlineData(true, PortableTileMode.FlipX, true, false, 2)]
    [InlineData(false, PortableTileMode.FlipY, false, true, 2)]
    [InlineData(true, PortableTileMode.FlipY, false, true, 2)]
    [InlineData(false, PortableTileMode.FlipXY, true, true, 3)]
    [InlineData(true, PortableTileMode.FlipXY, true, true, 3)]
    public void CompleteRepeatedImageUsesOriginalTexelsAndKeepsMirrorPhase(bool absolute,
        PortableTileMode mode, bool mirrorX, bool mirrorY, int transforms)
    {
        var image = new ImageMetricsSource(); // 400x200 pixels, 200x50 source DIPs.
        var adapter = new FakeImageSourceAdapter(); // Independently adapted to 200x100 texels.
        var brush = RepeatedImageBrush(image, mode, absolute);
        var sink = new RepeatedImageSink();
        Assert.True(WpfDrawingReplay.TryReplayTileBrushFill(new FakePortableTileBrushSource(brush),
            new RectangleGeometry(new Rect(0, 0, 32, 48)), sink, adapter.AdaptImageSource, out var status));
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.SourceImages);
        Assert.Equal(4, sink.RepeatedImages.Count);
        Assert.All(sink.RepeatedImages, draw =>
        {
            Assert.Same(adapter.AdaptedImageSource, draw.Image);
            Assert.Equal(new Size(16, 24), draw.Bounds.Size);
            Assert.Equal(mirrorX, draw.MirrorX);
            Assert.Equal(mirrorY, draw.MirrorY);
        });
        Assert.Same(image, adapter.LastImageSource);
        Assert.Equal(0.25, Assert.Single(sink.Opacities));
        Assert.Single(sink.Clips);
        Assert.Equal(transforms, sink.Transforms.Count);
        Assert.Equal(2 + transforms, sink.PopCount);
    }

    [Theory]
    [InlineData(8, -16, 3, 2)]
    [InlineData(-8, 0, 3, 1)]
    [InlineData(48, -48, 2, 1)]
    public void RepeatedTranslatedImageCoversOriginalPaintAndNegativeMirrorIndices(
        double translation, double firstTile, int tileCount, int mirroredTiles)
    {
        var brush = RepeatedImageBrush(new ImageMetricsSource(), PortableTileMode.FlipX,
            transform: new PortableMatrix3x2(1, 0, 0, 1, translation, 0));
        var sink = new RepeatedImageSink();
        var adapter = new FakeImageSourceAdapter();
        Assert.True(WpfDrawingReplay.TryReplayTileBrushFill(new FakePortableTileBrushSource(brush),
            new RectangleGeometry(new Rect(0, 0, 32, 24)), sink, adapter.AdaptImageSource, out _));
        Assert.Equal(tileCount, sink.RepeatedImages.Count);
        Assert.Equal(firstTile, sink.RepeatedImages[0].Bounds.X);
        Assert.Equal(firstTile + (tileCount - 1) * 16, sink.RepeatedImages[^1].Bounds.X);
        Assert.Equal(new Rect(0, 0, 32, 24), Assert.IsType<RectangleGeometry>(Assert.Single(sink.Clips)).Rect);
        Assert.Equal(1 + mirroredTiles, sink.Transforms.Count);
        Assert.Equal(3 + mirroredTiles, sink.PopCount);
        Assert.Empty(sink.Images);
    }

    [Theory]
    [InlineData(1e-30, 0)]
    [InlineData(1, 1e30)]
    public void UnrepresentableRepeatedTileRangeRejectsBeforePublishingScopes(double scale, double translation)
    {
        var brush = RepeatedImageBrush(new ImageMetricsSource(), PortableTileMode.Tile,
            transform: new PortableMatrix3x2(scale, 0, 0, 1, translation, 0));
        var sink = new RepeatedImageSink();
        Assert.False(WpfDrawingReplay.TryReplayTileBrushFill(new FakePortableTileBrushSource(brush),
            new RectangleGeometry(new Rect(0, 0, 32, 24)), sink, new FakeImageSourceAdapter().AdaptImageSource, out _));
        Assert.Empty(sink.Operations);
        Assert.Empty(sink.Images);
        Assert.Empty(sink.RepeatedImages);
    }

    [Theory]
    [InlineData(0)] // Cropped source.
    [InlineData(1)] // Uniform letterbox.
    [InlineData(2)] // Non-repeated source.
    [InlineData(3)] // Non-axis brush mapping.
    [InlineData(4)] // Untyped source metrics.
    [InlineData(5)] // Sink's current mode does not admit Linear repetition.
    [InlineData(6)] // An unavailable texture publishes no addressed command.
    public void RepeatedImageKeepsGeneralAndUnavailablePaths(int control)
    {
        var brush = RepeatedImageBrush(control == 4 ? new object() : new ImageMetricsSource(),
            control == 2 ? PortableTileMode.None : PortableTileMode.Tile,
            transform: control == 3 ? new PortableMatrix3x2(1, 0.25, 0, 1, 0, 0) : null,
            cropped: control == 0, stretch: control == 1 ? PortableStretch.Uniform : PortableStretch.Fill);
        var sink = new RepeatedImageSink { Supported = control != 5, Accept = control != 6 };
        Assert.True(WpfDrawingReplay.TryReplayTileBrushFill(new FakePortableTileBrushSource(brush),
            new RectangleGeometry(new Rect(0, 0, 32, 48)), sink, new FakeImageSourceAdapter().AdaptImageSource, out _));
        Assert.Empty(sink.RepeatedImages);
        Assert.Equal(control == 2 ? 1 : 4, sink.Images.Count);
        Assert.Equal(control == 6 ? 4 : 0, sink.Attempts);
        Assert.Equal(0.25, Assert.Single(sink.Opacities));
        Assert.Equal(sink.Operations.FindAll(operation => operation.StartsWith("Push", StringComparison.Ordinal)).Count,
            sink.PopCount);
    }

    private static PortableTileBrush RepeatedImageBrush(object image, PortableTileMode mode, bool absolute = false,
        PortableMatrix3x2? transform = null, bool cropped = false, PortableStretch stretch = PortableStretch.Fill) =>
        new(PortableTileBrushKind.Image, image, 0.25, new(0, 0, 16, 24),
            cropped ? new(0.25, 0.2, 0.5, 0.4) : absolute ? new(0, 0, 200, 50) : new(0, 0, 1, 1),
            PortableBrushMappingMode.Absolute, absolute ? PortableBrushMappingMode.Absolute : PortableBrushMappingMode.RelativeToBoundingBox,
            mode, stretch, PortableAlignmentX.Center, PortableAlignmentY.Center,
            transform.HasValue, transform.GetValueOrDefault(), false, default);

    private sealed class RepeatedImageSink : TestSink, IWpfRepeatedImageCommandSink
    {
        internal bool Supported { get; init; } = true;
        internal bool Accept { get; init; } = true;
        internal int Attempts { get; private set; }
        internal List<(ImageSource Image, Rect Bounds, bool MirrorX, bool MirrorY)> RepeatedImages { get; } = [];
        public bool SupportsRepeatedLinearImages => Supported;
        public bool TryDrawRepeatedImage(ImageSource imageSource, Rect rectangle, bool mirrorX, bool mirrorY)
        {
            Attempts++;
            if (!Accept) return false;
            RepeatedImages.Add((imageSource, rectangle, mirrorX, mirrorY));
            return true;
        }
    }
}
