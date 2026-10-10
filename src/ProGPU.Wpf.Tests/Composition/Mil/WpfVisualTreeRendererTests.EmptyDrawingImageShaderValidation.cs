using System;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfVisualTreeRendererTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmptyDrawingImageShaderProofKeepsNestedSourceValidation(bool imageBrush, bool invalidCache)
    {
        var target = invalidCache ? EmptyShaderCaptureVisual() : InvalidNestedShaderCacheTarget();
        var nested = new CaptureBrush(new(target,
            invalidCache ? new CaptureCache(new(double.NaN, false, false)) : null, Opacity: 0));
        var drawing = new EmptyImageProofDrawing(nested);
        var image = new EmptyImageProofSource(drawing);
        var tile = new EmptyImageProofBrush(image);
        var sink = new NullVisualScopeSink();
        WpfDrawingReplayStatus Replay()
        {
            WpfDrawingReplayStatus status;
            bool recognized = imageBrush
                ? WpfDrawingReplay.TryReplayTileBrushFill(tile, (object)new PortableRect(0, 0, 8, 8), sink, null, out status)
                : WpfDrawingReplay.TryReplayDrawingImage(image, new Rect(0, 0, 8, 8), sink, null, out status);
            Assert.True(recognized);
            return status;
        }

        Assert.Equal(WpfDrawingReplayStatus.Skipped, Replay());
        int reads = drawing.Reads;
        using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true))
            Assert.Throws<NotSupportedException>(() => Replay());
        Assert.True(drawing.Reads > reads); // actual original drawing was visited
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, Replay()); // no ordinary admission change
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyDrawingImageShaderProofKeepsNullDistinctAndRejectsActualCycle(bool imageBrush)
    {
        var image = new EmptyImageProofSource(null);
        var tile = new EmptyImageProofBrush(image);
        var sink = new NullVisualScopeSink();
        void Replay()
        {
            if (imageBrush)
                Assert.True(WpfDrawingReplay.TryReplayTileBrushFill(tile,
                    (object)new PortableRect(0, 0, 8, 8), sink, null, out _));
            else
                Assert.True(WpfDrawingReplay.TryReplayDrawingImage(image,
                    new Rect(0, 0, 8, 8), sink, null, out _));
        }
        using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true)) Replay();
        image.Drawing = new EmptyImageProofCycleDrawing(image);
        using (WpfCaptureReplayGuard.Begin(validateHiddenSources: true))
            Assert.Throws<InvalidOperationException>(Replay);
        Assert.False(WpfCaptureReplayGuard.IsActive);
        Assert.False(WpfCaptureReplayGuard.ValidateHiddenSources);
        Replay(); // known empty ordinary painting still skips the source graph
    }

    private sealed class EmptyImageProofSource(object? drawing) : IPortableDrawingImageSource
    {
        internal object? Drawing = drawing;
        public bool TryGetPortableDrawingImage(out object? value) { value = Drawing; return value is not null; }
    }

    private sealed class EmptyImageProofBrush(object image) : IPortableTileBrushSource
    {
        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = new(PortableTileBrushKind.Image, image, 1, new(0, 0, 1, 1), new(0, 0, 1, 1),
                PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
                PortableTileMode.None, PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
                false, PortableMatrix3x2.Identity, false, PortableMatrix3x2.Identity);
            return true;
        }
    }

    private sealed class EmptyImageProofDrawing(object brush) : IPortableGeometryDrawingStateSource, IPortableDrawingBoundsSource
    {
        internal int Reads;
        public bool TryGetPortableDrawingBounds(out PortableRect bounds) { bounds = PortableRect.Empty; return true; }
        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            ++Reads;
            state = new() { HasGeometry = true, Geometry = new PortableRect(0, 0, 0, 8), HasBrush = true, Brush = brush };
            return true;
        }
    }

    private sealed class EmptyImageProofCycleDrawing(object image) : IPortableImageDrawingStateSource, IPortableDrawingBoundsSource
    {
        public bool TryGetPortableDrawingBounds(out PortableRect bounds) { bounds = PortableRect.Empty; return true; }
        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = new() { HasImageSource = true, ImageSource = image, HasRect = true, Rect = new(0, 0, 8, 8) };
            return true;
        }
    }
}
