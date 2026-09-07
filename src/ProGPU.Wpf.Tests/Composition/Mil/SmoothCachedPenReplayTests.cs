using System;
using System.Buffers.Binary;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class SmoothCachedPenReplayTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 3)]
    [InlineData(false, true, 4)]
    [InlineData(true, true, 4)]
    public void TypedSmoothGeometrySharesFillAndStrokePath(bool ellipse, bool cachedFill, int route)
    {
        var source = new SourceVisual();
        var brush = new CachedBrush(new(source, HasRelativeTransform: true, RelativeTransform: new(0.5, 0, 0, 0.5, 0.25, 0.25)));
        var pen = new CachedPen(new(brush, 4, default, default, default, default, 10, default, 0));
        var commands = new global::ProGPU.Scene.DrawingContext();
        try
        {
            using var sink = new ProGpuCompositionCommandSink(commands);
            using var replay = new WpfObjectRenderDataDrawingContext(sink);
            object fill = cachedFill ? brush : Brushes.Blue;
            if (route == 0) replay.DrawGeometry(fill, pen, new SmoothGeometry(ellipse));
            else if (route == 1)
            {
                if (ellipse) replay.DrawEllipse(fill, pen, new Point(43, 11), 24d, 3d);
                else replay.DrawRoundedRectangle(fill, pen, new Rect(19, 8, 48, 6), 100d, 100d);
            }
            else if (route == 2)
            {
                Geometry geometry = ellipse ? new EllipseGeometry(new Point(20, 14), 12, 6)
                    : new RectangleGeometry(new Rect(8, 8, 24, 12), 100, 100);
                geometry.Transform = new MatrixTransform(2, 0, 0, 0.5, 3, 4);
                replay.DrawGeometry(fill, pen, geometry);
            }
            else if (route == 4)
            {
                var drawing = new SourceDrawing(new SmoothGeometry(ellipse), fill, pen);
                Assert.Equal(WpfDrawingReplayStatus.Applied, WpfDrawingReplay.Replay(drawing, sink));
            }
            else
            {
                var resources = new WpfMilResourceRegistry();
                resources.Register(1, fill); resources.Register(2, pen);
                byte[] record = new byte[8 + (ellipse ? 40 : 56)];
                BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), (int)(ellipse ? WpfMilCommandId.DrawEllipse : WpfMilCommandId.DrawRoundedRectangle));
                double[] values = ellipse ? [43, 11, 24, 3] : [19, 8, 48, 6, 100, 100];
                for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteDoubleLittleEndian(record.AsSpan(8 + i * 8), values[i]);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8 + values.Length * 8), 1);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(12 + values.Length * 8), 2);
                Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), new WpfMilRenderDataDecoder().Decode(record, sink, resources));
            }
            Assert.Equal(0, replay.Result.UnsupportedCount);
            var mask = Assert.Single(commands.Commands.Where(c => c.Type == global::ProGPU.Scene.RenderCommandType.PushOpacityMask));
            Assert.Equal(4, mask.Pen!.Thickness);
            Assert.InRange(Math.Abs(mask.Rect.X - 17), 0, 0.02);
            Assert.InRange(Math.Abs(mask.Rect.Y - 6), 0, 0.02);
            Assert.InRange(Math.Abs(mask.Rect.Right - 69), 0, 0.02);
            Assert.InRange(Math.Abs(mask.Rect.Bottom - 16), 0, 0.02);
            Assert.True(Assert.Single(mask.Path!.Figures).IsClosed);
            Assert.Same(commands.Commands[0].Path, mask.Path);
            var draw = commands.Commands.Last(c => c.Type == global::ProGPU.Scene.RenderCommandType.DrawVisual);
            Assert.InRange(Math.Abs(draw.Transform.M41 - 21.5), 0, 0.02);
            Assert.InRange(Math.Abs(draw.Transform.M42 - 5.5), 0, 0.02);
        }
        finally { commands.Clear(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedDashedPenDoesNotReportFillOnlyAsApplied(bool ellipse)
    {
        var source = new SourceVisual();
        var brush = new CachedBrush(new(source));
        var pen = new CachedPen(new(brush, 4, default, default, default, default, 10, new double[] { 1, 1 }, 0));
        var commands = new global::ProGPU.Scene.DrawingContext();
        try
        {
            using var sink = new ProGpuCompositionCommandSink(commands);
            var drawing = new SourceDrawing(new SmoothGeometry(ellipse), Brushes.Blue, pen);
            Assert.Equal(WpfDrawingReplayStatus.PartiallyApplied, WpfDrawingReplay.Replay(drawing, sink));
            Assert.Equal(global::ProGPU.Scene.RenderCommandType.DrawPath, Assert.Single(commands.Commands).Type);
        }
        finally { commands.Clear(); }
    }

    private sealed class CachedBrush(PortableBitmapCacheBrush state) : IPortableBitmapCacheBrushSource
    {
        public bool TryGetPortableBitmapCacheBrush(out PortableBitmapCacheBrush brush) { brush = state; return true; }
    }
    private sealed class SourceVisual : IPortableVisualStateSource, IPortableVisualBoundsSource,
        IPortableVisualChildrenSource, IPortableDrawingContentSource
    {
        private readonly SourceDrawing _drawing = new();
        public bool TryGetPortableVisualState(out PortableVisualState state) { state = new(); return true; }
        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new() { HasContentBounds = true, ContentBounds = new(0, 0, 30, 30),
                HasDescendantBounds = true, DescendantBounds = new(0, 0, 30, 30) };
            return true;
        }
        public bool TryGetPortableVisualChildCount(out int count) { count = 0; return true; }
        public bool TryGetPortableVisualChild(int index, out object? child) { child = null; return false; }
        public bool TryGetPortableDrawingContent(out object? content) { content = _drawing; return true; }
    }
    private sealed class SourceDrawing(object? geometry = null, object? brush = null, object? pen = null) : IPortableGeometryDrawingStateSource
    {
        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new() { HasGeometry = true, Geometry = geometry ?? new PortableRect(0, 0, 30, 30),
                HasBrush = true, Brush = brush ?? Brushes.Red, HasPen = pen != null, Pen = pen };
            return true;
        }
    }
    private sealed class CachedPen(PortablePenState state) : IPortablePenStateSource
    {
        public bool TryGetPortablePenState(out PortablePenState pen) { pen = state; return true; }
    }
    private sealed class SmoothGeometry(bool ellipse) : IPortablePrimitiveGeometrySource, IPortableGeometryPathSource
    {
        public bool TryGetPortablePrimitiveGeometry(out PortablePrimitiveGeometry geometry)
        {
            var matrix = new PortableMatrix3x2(2, 0, 0, 0.5, 3, 4);
            geometry = ellipse ? PortablePrimitiveGeometry.Ellipse(new(20, 14), 12, 6, matrix)
                : PortablePrimitiveGeometry.Rectangle(new(8, 8, 24, 12), 100, 100, matrix);
            return true;
        }
        public bool TryGetPortableGeometryPath(out PortableGeometryPath path) =>
            throw new InvalidOperationException("Smooth primitive replay must not request a packed path.");
    }
}
