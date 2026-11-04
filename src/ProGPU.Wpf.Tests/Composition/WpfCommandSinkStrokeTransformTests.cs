using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Scene;
using ProGPU.Scene.Native;
using Xunit;
using SceneDrawingContext = ProGPU.Scene.DrawingContext;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfCommandSinkStrokeTransformTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void ScaledPrimitivesKeepSourcePenPolicyAndReuseItsSnapshot(bool typed, int shape)
    {
        var commands = new SceneDrawingContext();
        try
        {
            using var sink = new ProGpuCompositionCommandSink(commands);
            var source = new Pen(Brushes.Black, 8)
            {
                LineJoin = PenLineJoin.Bevel,
                MiterLimit = 3,
                DashStyle = new DashStyle([2, 3], 0.5)
            };
            var transform = Matrix4x4.CreateScale(0.25f, 0.5f, 1f);
            sink.PushNativeTransform(transform);
            Draw(sink, source, typed, shape);
            sink.Pop();
            Draw(sink, source, typed, shape);

            Assert.Equal(2, commands.Commands.Count);
            var scaled = commands.Commands[0];
            var unscaled = commands.Commands[1];
            Assert.True(scaled.IsPenThicknessLocal);
            Assert.True(unscaled.IsPenThicknessLocal);
            Assert.Equal(transform, scaled.Transform);
            Assert.Equal(Matrix4x4.Identity, unscaled.Transform);
            Assert.Same(scaled.Pen, unscaled.Pen);
            Assert.Equal(8f, scaled.Pen!.Thickness);
            Assert.True(scaled.Pen.UseWpfJoinSemantics);
            Assert.Equal(ProGPU.Vector.PenLineJoin.Bevel, scaled.Pen.LineJoin);
            Assert.Equal(3f, scaled.Pen.MiterLimit);
            Assert.Equal(new double[] { 2, 3 }, scaled.Pen.DashArray);
            Assert.Equal(0.5, scaled.Pen.DashOffset);
            Assert.Equal(8, source.Thickness);
            Assert.Equal(0, sink.UnsupportedStateCount);
        }
        finally { commands.Clear(); }
    }

    [Theory]
    [InlineData(0.25f, 0.5f)]
    [InlineData(2f, 3f)]
    [InlineData(-0.5f, 2f)]
    public void RetainedLineBoundsScaleTheOriginalWidthExactlyOnce(float scaleX, float scaleY)
    {
        var recorder = new GpuPictureRecorder();
        var context = recorder.BeginRecording(new ProGPU.Scene.Rect(-100, -100, 400, 400));
        using (var sink = new ProGpuCompositionCommandSink(context))
        {
            sink.PushNativeTransform(Matrix4x4.CreateScale(scaleX, scaleY, 1f));
            ((IWpfNativePrimitiveCommandSink)sink).DrawNativeLine(
                new Pen(Brushes.Black, 8), new(8, 20), new(40, 20));
            sink.Pop();
        }
        using var picture = recorder.EndRecording();
        using var clone = picture.Clone();
        Assert.True(GpuPictureBounds.TryGetBounds(clone, out var bounds));
        // GpuPictureBounds retains a conservative half-width envelope around
        // the line, including its endpoints. Scale that existing envelope.
        Assert.Equal(MathF.Min(4 * scaleX, 44 * scaleX), bounds.X);
        Assert.Equal(16 * scaleY, bounds.Y);
        Assert.Equal(40 * MathF.Abs(scaleX), bounds.Width);
        Assert.Equal(8 * scaleY, bounds.Height);
        Assert.True(GpuPictureNativeSceneCompiler.TryCompile(clone, 308, 1,
            out var compiled, out var failure), failure.ToString());
        using (compiled)
        {
            Assert.NotNull(compiled);
            Assert.NotEmpty(compiled.Stream.ToArray());
        }
    }

    [Theory]
    [InlineData(PenLineCap.Square)]
    [InlineData(PenLineCap.Round)]
    [InlineData(PenLineCap.Triangle)]
    public void ShimLineCapsKeepTheirOriginalLocalGeometry(PenLineCap cap)
    {
        var commands = new SceneDrawingContext();
        try
        {
            using var sink = new ProGpuCompositionCommandSink(commands);
            sink.PushNativeTransform(Matrix4x4.CreateScale(0.25f, 0.5f, 1f));
            sink.DrawLine(new Pen(Brushes.Black, 8) { StartLineCap = cap, EndLineCap = cap },
                new Point(8, 20), new Point(40, 20));
            sink.Pop();
            var line = commands.Commands[0];
            Assert.True(line.IsPenThicknessLocal);
            Assert.Equal(8f, line.Pen!.Thickness);
            if (cap == PenLineCap.Square)
            {
                Assert.Equal(new Vector2(4, 20), line.Position);
                Assert.Equal(new Vector2(44, 20), line.Position2);
            }
            else
            {
                Assert.Equal(new Vector2(8, 20), line.Position);
                Assert.Equal(new Vector2(40, 20), line.Position2);
                Assert.Equal(3, commands.Commands.Count);
                if (cap == PenLineCap.Round)
                {
                    Assert.Equal(4f, commands.Commands[1].RadiusX);
                    Assert.Equal(4f, commands.Commands[1].RadiusY);
                }
                else
                {
                    Assert.Equal(new Vector2(4, 20), commands.Commands[1].Position2);
                    Assert.Equal(new Vector2(44, 20), commands.Commands[2].Position2);
                }
            }
        }
        finally { commands.Clear(); }
    }

    private static void Draw(ProGpuCompositionCommandSink sink, Pen pen, bool typed, int shape)
    {
        if (typed)
        {
            var primitives = (IWpfNativePrimitiveCommandSink)sink;
            switch (shape)
            {
                case 0: primitives.DrawNativeLine(pen, new(8, 20), new(40, 20)); break;
                case 1: primitives.DrawNativeRectangle(null, pen, new(8, 20, 32, 16)); break;
                case 2: primitives.DrawNativeRoundedRectangle(null, pen, new(8, 20, 32, 16), 3, 3); break;
                case 3: primitives.DrawNativeEllipse(null, pen, new(24, 28), 16, 8); break;
            }
            return;
        }
        switch (shape)
        {
            case 0: sink.DrawLine(pen, new(8, 20), new(40, 20)); break;
            case 1: sink.DrawRectangle(null, pen, new(8, 20, 32, 16)); break;
            case 2: sink.DrawRoundedRectangle(null, pen, new(8, 20, 32, 16), 3, 3); break;
            case 3: sink.DrawEllipse(null, pen, new(24, 28), 16, 8); break;
            case 4: sink.DrawGeometry(null, pen, new RectangleGeometry(new(8, 20, 32, 16))); break;
        }
    }
}
