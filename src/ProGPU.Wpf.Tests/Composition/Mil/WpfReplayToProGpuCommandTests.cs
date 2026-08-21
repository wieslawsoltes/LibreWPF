using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Scene;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuDrawingContext = ProGPU.Scene.DrawingContext;
using ProGpuArcSegment = ProGPU.Vector.ArcSegment;
using ProGpuCubicBezierSegment = ProGPU.Vector.CubicBezierSegment;
using ProGpuLineSegment = ProGPU.Vector.LineSegment;
using ProGpuLinearGradientBrush = ProGPU.Vector.LinearGradientBrush;
using ProGpuPathFigure = ProGPU.Vector.PathFigure;
using ProGpuPathGeometry = ProGPU.Vector.PathGeometry;
using ProGpuQuadraticBezierSegment = ProGPU.Vector.QuadraticBezierSegment;
using ProGpuRadialGradientBrush = ProGPU.Vector.RadialGradientBrush;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfReplayToProGpuCommandTests
{
    [Fact]
    public void SmallValueStackReusesPooledStorageAfterReturningToEmpty()
    {
        var stack = new ProGpuCompositionCommandSink.SmallValueStack<int>();

        stack.Push(1);
        stack.Push(2);
        Assert.Equal(2, stack.Pop());
        Assert.Equal(1, stack.Pop());

        stack.Push(3);
        stack.Push(4);

        Assert.Equal(4, stack.Pop());
        Assert.Equal(3, stack.Pop());
        stack.Dispose();
    }

    [Fact]
    public void SmallValueStackPeekAtDepthReadsTopFirstWithoutPopping()
    {
        var stack = new ProGpuCompositionCommandSink.SmallValueStack<int>();

        stack.Push(1);
        Assert.Equal(1, stack.PeekAtDepth(0));

        stack.Push(2);
        stack.Push(3);
        stack.Push(4);
        stack.Push(5);

        Assert.Equal(5, stack.PeekAtDepth(0));
        Assert.Equal(4, stack.PeekAtDepth(1));
        Assert.Equal(3, stack.PeekAtDepth(2));
        Assert.Equal(2, stack.PeekAtDepth(3));
        Assert.Equal(1, stack.PeekAtDepth(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => stack.PeekAtDepth(5));

        Assert.Equal(5, stack.Pop());
        Assert.Equal(4, stack.Pop());
        Assert.Equal(3, stack.Pop());
        Assert.Equal(2, stack.Pop());
        Assert.Equal(1, stack.Pop());
        stack.Dispose();
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkEmitsDrawRectCommand()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 1),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(2, command.Rect.X);
        Assert.Equal(3, command.Rect.Y);
        Assert.Equal(40, command.Rect.Width);
        Assert.Equal(50, command.Rect.Height);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(command.Brush);
        Assert.Equal(2, nativeBrush.StartPoint.X);
        Assert.Equal(3, nativeBrush.StartPoint.Y);
        Assert.Equal(42, nativeBrush.EndPoint.X);
        Assert.Equal(53, nativeBrush.EndPoint.Y);
    }

    [Fact]
    public void TypedRectangleThroughProGpuSinkReusesCachedNativePen()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var pen = new Pen(Brushes.Black, 2);

        sink.DrawRectangle(null, pen, new System.Windows.Rect(0, 0, 10, 10));
        sink.DrawRectangle(null, pen, new System.Windows.Rect(20, 0, 10, 10));

        Assert.Equal(2, nativeContext.Commands.Count);
        Assert.Same(nativeContext.Commands[0].Pen, nativeContext.Commands[1].Pen);

        pen.Thickness = 3;
        sink.DrawRectangle(null, pen, new System.Windows.Rect(40, 0, 10, 10));

        Assert.Equal(3, nativeContext.Commands.Count);
        Assert.NotSame(nativeContext.Commands[0].Pen, nativeContext.Commands[2].Pen);
    }

    [Fact]
    public void ProGpuSinkSkipsNullMaterialPrimitiveCommands()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var nativePrimitiveSink = (IWpfNativePrimitiveCommandSink)sink;

        sink.DrawRectangle(null, null, new System.Windows.Rect(0, 0, 10, 10));
        sink.DrawRoundedRectangle(null, null, new System.Windows.Rect(20, 0, 10, 10), 2, 2);
        sink.DrawEllipse(null, null, new System.Windows.Point(40, 5), 5, 5);
        nativePrimitiveSink.DrawNativeRectangle(null, null, new WpfReplayRect(0, 20, 10, 10));
        nativePrimitiveSink.DrawNativeRoundedRectangle(null, null, new WpfReplayRect(20, 20, 10, 10), 2, 2);
        nativePrimitiveSink.DrawNativeEllipse(null, null, new WpfReplayPoint(40, 25), 5, 5);

        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkKeepsAbsoluteGradientMapping()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(10, 20),
            new FakePoint(30, 40),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            MappingMode = "Absolute"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(command.Brush);
        Assert.Equal(10, nativeBrush.StartPoint.X);
        Assert.Equal(20, nativeBrush.StartPoint.Y);
        Assert.Equal(30, nativeBrush.EndPoint.X);
        Assert.Equal(40, nativeBrush.EndPoint.Y);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkAppliesGradientTransforms()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25)),
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(command.Brush);
        Assert.Equal(2, nativeBrush.StartPoint.X);
        Assert.Equal(3, nativeBrush.StartPoint.Y);
        Assert.Equal(42, nativeBrush.EndPoint.X);
        Assert.Equal(3, nativeBrush.EndPoint.Y);
        Assert.Equal(1, nativeBrush.CoordinateTransform.M11);
        Assert.Equal(1, nativeBrush.CoordinateTransform.M22);
        Assert.Equal(-27, nativeBrush.CoordinateTransform.M41);
        Assert.Equal(-21.5f, nativeBrush.CoordinateTransform.M42);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkPreservesScRgbGradientColorInterpolationMode()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            ColorInterpolationMode = "ScRgbLinearInterpolation"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, sink.UnsupportedStateCount);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(nativeContext.Commands).Brush);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkDefaultsUnrecognizedPortableGradientColorInterpolationMode()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            ColorInterpolationMode = "FutureInterpolationMode"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, sink.UnsupportedStateCount);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(nativeContext.Commands).Brush);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.SRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkDoesNotCountSupportedManyStopGradient()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 0),
            CreateGradientStops(12));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, sink.UnsupportedStateCount);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(nativeContext.Commands).Brush);
        Assert.Equal(12, nativeBrush.Stops.Length);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkPreservesManyStopScRgbGradient()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 0),
            CreateGradientStops(9))
        {
            ColorInterpolationMode = "ScRgbLinearInterpolation"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, sink.UnsupportedStateCount);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(nativeContext.Commands).Brush);
        Assert.Equal(9, nativeBrush.Stops.Length);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
    }

    [Fact]
    public void ProGpuBrushAbiCarriesGradientStopTable()
    {
        Assert.Equal(256, Marshal.SizeOf<GpuBrush>());
        Assert.Equal(48, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.ColorInterpolationMode)).ToInt32());
        Assert.Equal(52, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.StopOffset)).ToInt32());
        Assert.Equal(64, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.Color0)).ToInt32());
        Assert.Equal(176, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.Color7)).ToInt32());
        Assert.Equal(192, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.Offsets)).ToInt32());
        Assert.Equal(208, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.Offsets1)).ToInt32());
        Assert.Equal(224, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.CoordinateTransform0)).ToInt32());
        Assert.Equal(240, Marshal.OffsetOf<GpuBrush>(nameof(GpuBrush.CoordinateTransform1)).ToInt32());
        Assert.Equal(32, Marshal.SizeOf<GpuGradientStop>());
        Assert.Equal(0, Marshal.OffsetOf<GpuGradientStop>(nameof(GpuGradientStop.Color)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<GpuGradientStop>(nameof(GpuGradientStop.Offset)).ToInt32());
        Assert.Contains("colorInterpolationMode", Shaders.VectorShader, StringComparison.Ordinal);
        Assert.Contains("stopOffset", Shaders.VectorShader, StringComparison.Ordinal);
        Assert.Contains("gradientStops", Shaders.VectorShader, StringComparison.Ordinal);
        Assert.Contains("coordinateTransform0", Shaders.VectorShader, StringComparison.Ordinal);
        Assert.Contains("sample_gradient_color", Shaders.VectorShader, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeEllipseThroughProGpuSinkMapsRelativeRadialGradient()
    {
        var brush = new FakeRadialGradientBrush(
            new FakePoint(0.5, 0.5),
            new FakePoint(0.25, 0.75),
            radiusX: 0.5,
            radiusY: 1,
            new FakeGradientStop(new FakeColor(255, 255, 255, 255), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 0), 1));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WritePoint(payload, 0, 20, 30);
        WriteDouble(payload, 16, 10);
        WriteDouble(payload, 24, 15);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawEllipse, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        var nativeBrush = Assert.IsType<ProGpuRadialGradientBrush>(command.Brush);
        Assert.Equal(20, nativeBrush.Center.X);
        Assert.Equal(30, nativeBrush.Center.Y);
        Assert.Equal(15, nativeBrush.GradientOrigin.X);
        Assert.Equal(37.5f, nativeBrush.GradientOrigin.Y);
        Assert.Equal(10, nativeBrush.RadiusX);
        Assert.Equal(30, nativeBrush.RadiusY);
    }

    [Fact]
    public void DecodeEllipseThroughProGpuSinkStoresAxisAlignedRadialGradientCoordinateTransform()
    {
        var brush = new FakeRadialGradientBrush(
            new FakePoint(0.5, 0.5),
            new FakePoint(0.25, 0.75),
            radiusX: 0.5,
            radiusY: 1,
            new FakeGradientStop(new FakeColor(255, 255, 255, 255), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 0), 1))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(2, 0, 0, 3, 5, 7))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WritePoint(payload, 0, 20, 30);
        WriteDouble(payload, 16, 10);
        WriteDouble(payload, 24, 15);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawEllipse, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        var nativeBrush = Assert.IsType<ProGpuRadialGradientBrush>(command.Brush);
        Assert.Equal(20, nativeBrush.Center.X);
        Assert.Equal(30, nativeBrush.Center.Y);
        Assert.Equal(15, nativeBrush.GradientOrigin.X);
        Assert.Equal(37.5f, nativeBrush.GradientOrigin.Y);
        Assert.Equal(10, nativeBrush.RadiusX);
        Assert.Equal(30, nativeBrush.RadiusY);
        Assert.Equal(0.5f, nativeBrush.CoordinateTransform.M11);
        Assert.Equal(1f / 3f, nativeBrush.CoordinateTransform.M22, precision: 6);
        Assert.Equal(-2.5f, nativeBrush.CoordinateTransform.M41);
        Assert.Equal(-7f / 3f, nativeBrush.CoordinateTransform.M42, precision: 6);
    }

    [Fact]
    public void DecodeEllipseThroughProGpuSinkStoresNonAxisAlignedRadialGradientCoordinateTransform()
    {
        var brush = new FakeRadialGradientBrush(
            new FakePoint(0.5, 0.5),
            new FakePoint(0.25, 0.75),
            radiusX: 0.5,
            radiusY: 1,
            new FakeGradientStop(new FakeColor(255, 255, 255, 255), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 0), 1))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0.25, 0, 1, 5, 7))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WritePoint(payload, 0, 20, 30);
        WriteDouble(payload, 16, 10);
        WriteDouble(payload, 24, 15);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawEllipse, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, sink.UnsupportedStateCount);
        var command = Assert.Single(nativeContext.Commands);
        var nativeBrush = Assert.IsType<ProGpuRadialGradientBrush>(command.Brush);
        Assert.Equal(20, nativeBrush.Center.X);
        Assert.Equal(30, nativeBrush.Center.Y);
        Assert.Equal(15, nativeBrush.GradientOrigin.X);
        Assert.Equal(37.5f, nativeBrush.GradientOrigin.Y);
        Assert.Equal(10, nativeBrush.RadiusX);
        Assert.Equal(30, nativeBrush.RadiusY);
        Assert.Equal(1, nativeBrush.CoordinateTransform.M11);
        Assert.Equal(-0.25f, nativeBrush.CoordinateTransform.M12);
        Assert.Equal(1, nativeBrush.CoordinateTransform.M22);
        Assert.Equal(-5, nativeBrush.CoordinateTransform.M41);
        Assert.Equal(-5.75f, nativeBrush.CoordinateTransform.M42);
    }

    [Fact]
    public void DecodeRectangleThroughProGpuSinkDropsNonInvertibleGradientTransform()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(0, 0, 0, 1, 0, 0))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[40];
        WriteRect(payload, 0, 2, 3, 40, 50);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(1, sink.UnsupportedStateCount);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(Assert.Single(nativeContext.Commands).Brush);
        Assert.Equal(Matrix4x4.Identity, nativeBrush.CoordinateTransform);
    }

    [Fact]
    public void DecodeTransformedRectangleThroughProGpuSinkStoresTransform()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, Brushes.Red });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 2, 3, 40, 50);
        WriteUInt32(rectanglePayload, 32, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(7, command.Transform.M41);
        Assert.Equal(9, command.Transform.M42);
    }

    [Fact]
    public void DecodeIdentityScopesThroughProGpuSinkDoNotEmitPushPopCommands()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0, 0));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, Brushes.Red });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var opacityPayload = new byte[8];
        WriteDouble(opacityPayload, 0, 1);
        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 2, 3, 40, 50);
        WriteUInt32(rectanglePayload, 32, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushOpacity, opacityPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushTransform, transformPayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(5, 5, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(Matrix4x4.Identity, command.Transform);
    }

    [Fact]
    public void NativeIdentityTransformThroughProGpuSinkDoesNotEmitPushPopCommands()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var nativeTransformSink = (IWpfNativeTransformCommandSink)sink;

        nativeTransformSink.PushNativeTransform(Matrix4x4.Identity);
        sink.Pop();

        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DecodeTransformedLineThroughProGpuSinkStoresTransform()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 6));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            transform,
            new Pen(Brushes.Black, 2)
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 1, 2);
        WritePoint(linePayload, 16, 30, 40);
        WriteUInt32(linePayload, 32, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawLine, linePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.Equal(1, command.Position.X);
        Assert.Equal(2, command.Position.Y);
        Assert.Equal(30, command.Position2.X);
        Assert.Equal(40, command.Position2.Y);
        Assert.Equal(5, command.Transform.M41);
        Assert.Equal(6, command.Transform.M42);
    }

    [Fact]
    public void DecodeSquareCappedLineThroughProGpuSinkPreservesNativeLineCapMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            2)
        {
            StartLineCap = "Square",
            EndLineCap = "Square"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 10, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.Equal(0, command.Position.X);
        Assert.Equal(10, command.Position2.X);
        Assert.NotNull(command.Pen);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Square, command.Pen!.StartLineCap);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Square, command.Pen.EndLineCap);
    }

    [Fact]
    public void DecodeTriangleCappedLineThroughProGpuSinkPreservesNativeLineCapMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            2)
        {
            StartLineCap = "Triangle",
            EndLineCap = "Triangle"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 10, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.NotNull(command.Pen);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Triangle, command.Pen!.StartLineCap);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Triangle, command.Pen.EndLineCap);
    }

    [Fact]
    public void DecodeLineThroughProGpuSinkPreservesWpfPenLineJoinAndMiterNativeMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            3)
        {
            LineJoin = "Bevel",
            MiterLimit = 4.5
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 10, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.NotNull(command.Pen);
        Assert.Equal(global::ProGPU.Vector.PenLineJoin.Bevel, command.Pen!.LineJoin);
        Assert.Equal(4.5f, command.Pen.MiterLimit);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkLowersLinePathAndPreservesWpfPenLineCapNativeMetadata()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false
        };
        figure.Segments.Add(new LineSegment(new Vector2(10, 0)));
        geometry.Figures.Add(figure);
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            2)
        {
            StartLineCap = "Square",
            EndLineCap = "Triangle",
            DashCap = "Round"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 4, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.NotNull(command.Pen);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Square, command.Pen!.StartLineCap);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Triangle, command.Pen.EndLineCap);
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Round, command.Pen.DashCap);
    }

    [Fact]
    public void DecodeDashedLineThroughProGpuSinkPreservesNativeDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 2.0, 2.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 10, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.Equal(0, command.Position.X);
        Assert.Equal(10, command.Position2.X);
        AssertNativeDashPattern(command.Pen, new[] { 2.0, 2.0 });
    }

    [Fact]
    public void DecodeDottedLineThroughProGpuSinkPreservesNativeDotDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            2)
        {
            DashStyle = new FakeDashStyle(new[] { 0.0, 2.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 10, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        AssertNativeDashPattern(command.Pen, new[] { 0.0, 2.0 });
    }

    [Fact]
    public void DecodeRoundDashCappedLineThroughProGpuSinkPreservesNativeDashCapMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            2)
        {
            DashStyle = new FakeDashStyle(new[] { 1.0, 1.0 }, 0),
            DashCap = "Round"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 6, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        AssertNativeDashPattern(command.Pen, new[] { 1.0, 1.0 });
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Round, command.Pen!.DashCap);
    }

    [Fact]
    public void DecodeTriangleDashCappedLineThroughProGpuSinkPreservesNativeDashCapMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            2)
        {
            DashStyle = new FakeDashStyle(new[] { 1.0, 1.0 }, 0),
            DashCap = "Triangle"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { pen });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 0, 0);
        WritePoint(linePayload, 16, 6, 0);
        WriteUInt32(linePayload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawLine, linePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        AssertNativeDashPattern(command.Pen, new[] { 1.0, 1.0 });
        Assert.Equal(global::ProGPU.Vector.PenLineCap.Triangle, command.Pen!.DashCap);
    }

    [Fact]
    public void DecodeFilledDashedRectangleThroughProGpuSinkPreservesNativeDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 2.0, 2.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 0, 0, 8, 4);
        WriteUInt32(rectanglePayload, 32, 1);
        WriteUInt32(rectanglePayload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.NotNull(command.Brush);
        AssertNativeDashPattern(command.Pen, new[] { 2.0, 2.0 });
    }

    [Fact]
    public void DecodeFilledDashedRoundedRectangleThroughProGpuSinkPreservesNativeDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 100.0, 1.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var roundedRectanglePayload = new byte[56];
        WriteRect(roundedRectanglePayload, 0, 0, 0, 10, 6);
        WriteDouble(roundedRectanglePayload, 32, 2);
        WriteDouble(roundedRectanglePayload, 40, 2);
        WriteUInt32(roundedRectanglePayload, 48, 1);
        WriteUInt32(roundedRectanglePayload, 52, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRoundedRectangle, roundedRectanglePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRoundedRect, command.Type);
        Assert.NotNull(command.Brush);
        AssertNativeDashPattern(command.Pen, new[] { 100.0, 1.0 });
    }

    [Fact]
    public void DecodeFilledDashedEllipseThroughProGpuSinkPreservesNativeDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 100.0, 1.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var ellipsePayload = new byte[40];
        WritePoint(ellipsePayload, 0, 5, 4);
        WriteDouble(ellipsePayload, 16, 5);
        WriteDouble(ellipsePayload, 24, 3);
        WriteUInt32(ellipsePayload, 32, 1);
        WriteUInt32(ellipsePayload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawEllipse, ellipsePayload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawEllipse, command.Type);
        Assert.NotNull(command.Brush);
        AssertNativeDashPattern(command.Pen, new[] { 100.0, 1.0 });
    }

    [Fact]
    public void DecodeFilledDashedGeometryThroughProGpuSinkPreservesNativePathDashMetadata()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(new Vector2(6, 0)));
        figure.Segments.Add(new QuadraticBezierSegment(new Vector2(9, 2), new Vector2(6, 4)));
        figure.Segments.Add(new BezierSegment(new Vector2(4, 6), new Vector2(2, 6), new Vector2(0, 4)));
        geometry.Figures.Add(figure);

        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 100.0, 1.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen,
            geometry
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);
        WriteUInt32(payload, 8, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Brush);
        Assert.NotNull(command.Path);
        Assert.Contains(command.Path!.Figures.SelectMany(figure => figure.Segments), segment => segment is ProGpuQuadraticBezierSegment);
        Assert.Contains(command.Path.Figures.SelectMany(figure => figure.Segments), segment => segment is ProGpuCubicBezierSegment);
        AssertNativeDashPattern(command.Pen, new[] { 100.0, 1.0 });
    }

    [Fact]
    public void RepeatedMediaGeometryThroughProGpuSinkReusesCachedNativePathWhenUnchanged()
    {
        var geometry = CreateArcPathGeometry();
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.DrawGeometry(Brushes.Blue, null, geometry);
        sink.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(2, nativeContext.Commands.Count);
        var firstCommand = nativeContext.Commands[0];
        var secondCommand = nativeContext.Commands[1];
        Assert.Equal(RenderCommandType.DrawPath, firstCommand.Type);
        Assert.Equal(RenderCommandType.DrawPath, secondCommand.Type);
        Assert.NotNull(firstCommand.Path);
        Assert.Same(firstCommand.Path, secondCommand.Path);
    }

    [Fact]
    public void MutableMediaGeometryThroughProGpuSinkRefreshesCachedNativePathWhenShapeChanges()
    {
        var geometry = CreateArcPathGeometry();
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.DrawGeometry(Brushes.Blue, null, geometry);
        geometry.Figures[0].Segments.Add(new LineSegment(new Point(50, 60), isStroked: true));
        sink.DrawGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(2, nativeContext.Commands.Count);
        var firstCommand = nativeContext.Commands[0];
        var secondCommand = nativeContext.Commands[1];
        Assert.Equal(RenderCommandType.DrawPath, firstCommand.Type);
        Assert.Equal(RenderCommandType.DrawPath, secondCommand.Type);
        Assert.NotNull(firstCommand.Path);
        Assert.NotSame(firstCommand.Path, secondCommand.Path);
        Assert.Equal(2, Assert.Single(secondCommand.Path!.Figures).Segments.Count);
    }

    [Fact]
    public void GeometryGroupThroughProGpuSinkUsesRuntimeChildrenCollectionAbi()
    {
        var geometry = new GeometryGroup();
        geometry.Children.Add(new RectangleGeometry(new System.Windows.Rect(2, 3, 40, 50)));
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.DrawGeometry(Brushes.Blue, null, geometry);

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Path);
    }

    [Fact]
    public void PortablePathThroughProGpuSinkReusesCachedNativePathWhenUnchanged()
    {
        var geometry = CreatePortableRectangleGeometry(new FakeRect(2, 3, 40, 50));
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.DrawNativeGeometry(Brushes.Blue, null, geometry);
        sink.DrawNativeGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(2, nativeContext.Commands.Count);
        var firstCommand = nativeContext.Commands[0];
        var secondCommand = nativeContext.Commands[1];
        Assert.Equal(RenderCommandType.DrawPath, firstCommand.Type);
        Assert.Equal(RenderCommandType.DrawPath, secondCommand.Type);
        Assert.NotNull(firstCommand.Path);
        Assert.Same(firstCommand.Path, secondCommand.Path);
    }

    [Fact]
    public void MutablePortablePathThroughProGpuSinkRefreshesCachedNativePathWhenShapeChanges()
    {
        var geometry = CreatePortableRectangleGeometry(new FakeRect(2, 3, 40, 50));
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.DrawNativeGeometry(Brushes.Blue, null, geometry);
        geometry.Figures[0].Segments =
        [
            .. geometry.Figures[0].Segments,
            PortablePathSegment.Line(new PortablePoint(2, 53), isSmoothJoin: false, isStroked: false)
        ];
        sink.DrawNativeGeometry(Brushes.Blue, null, geometry);

        Assert.Equal(2, nativeContext.Commands.Count);
        var firstCommand = nativeContext.Commands[0];
        var secondCommand = nativeContext.Commands[1];
        Assert.Equal(RenderCommandType.DrawPath, firstCommand.Type);
        Assert.Equal(RenderCommandType.DrawPath, secondCommand.Type);
        Assert.NotNull(firstCommand.Path);
        Assert.NotSame(firstCommand.Path, secondCommand.Path);
        Assert.Equal(4, Assert.Single(secondCommand.Path!.Figures).Segments.Count);
    }

    [Fact]
    public void DecodePortablePathThroughProGpuSinkMapsRelativeGradientFromNativePathBounds()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 1),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1));
        var geometry = new FakePortablePathGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.Nonzero,
            Bounds = new PortableRect(0, 0, 1, 1),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(3, 4),
                    IsClosed = false,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.QuadraticBezier(
                            new PortablePoint(53, 4),
                            new PortablePoint(103, 54),
                            isSmoothJoin: false,
                            isStroked: true)
                    ]
                }
            ]
        });
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            brush,
            geometry
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(command.Brush);
        Assert.Equal(3, nativeBrush.StartPoint.X);
        Assert.Equal(4, nativeBrush.StartPoint.Y);
        Assert.Equal(103, nativeBrush.EndPoint.X);
        Assert.Equal(54, nativeBrush.EndPoint.Y);
    }

    [Fact]
    public void DecodePortablePathThroughProGpuSinkMapsRelativeGradientFromExactScaledCurveBounds()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 1),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1));
        var geometry = new FakePortablePathGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.Nonzero,
            Bounds = new PortableRect(0, 0, 1, 1),
            Transform = new PortableMatrix3x2(2, 0, 0, 3, 5, 7),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(3, 4),
                    IsClosed = false,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.QuadraticBezier(
                            new PortablePoint(53, 104),
                            new PortablePoint(103, 4),
                            isSmoothJoin: false,
                            isStroked: true)
                    ]
                }
            ]
        });
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            brush,
            geometry
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(command.Brush);
        Assert.Equal(11, nativeBrush.StartPoint.X);
        Assert.Equal(19, nativeBrush.StartPoint.Y);
        Assert.Equal(211, nativeBrush.EndPoint.X);
        Assert.Equal(169, nativeBrush.EndPoint.Y);
    }

    [Fact]
    public void DecodeFilledDashedArcGeometryThroughProGpuSinkPreservesNativeArcDashMetadata()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(30, 0),
            Size = new Size(15, 15),
            RotationAngle = 0,
            IsLargeArc = false,
            SweepDirection = SweepDirection.Clockwise
        });
        geometry.Figures.Add(figure);

        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 100.0, 1.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen,
            geometry
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);
        WriteUInt32(payload, 8, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Brush);
        AssertNativeDashPattern(command.Pen, new[] { 100.0, 1.0 });
        var nativeFigure = Assert.Single(command.Path!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));
        Assert.Equal(ProGPU.Vector.SweepDirection.Clockwise, nativeArc.SweepDirection);
        Assert.Equal(15, nativeArc.Size.X);
        Assert.Equal(15, nativeArc.Size.Y);
    }

    [Fact]
    public void DecodeFilledDashedCombinedGeometryThroughProGpuSinkPreservesNativeCombinedPathDashMetadata()
    {
        var geometry = new FakeCombinedGeometry(
            "Union",
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)),
            new FakeRectangleGeometry(new FakeRect(30, 0, 20, 20)));
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 100.0, 1.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen,
            geometry
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);
        WriteUInt32(payload, 8, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.True(command.Path!.IsCombined);
        Assert.NotNull(command.Brush);
        AssertNativeDashPattern(command.Pen, new[] { 100.0, 1.0 });
    }

    [Fact]
    public void DecodeFilledDashedCombinedGeometryThroughProGpuSinkDashesResolvedBooleanPathWhenAvailable()
    {
        var geometry = new FakeCombinedGeometry(
            "Union",
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)),
            new FakeRectangleGeometry(new FakeRect(30, 0, 20, 20)));
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 0, 0, 0)),
            1)
        {
            DashStyle = new FakeDashStyle(new[] { 100.0, 1.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            Brushes.Blue,
            pen,
            geometry
        });
        var nativeContext = new ProGpuDrawingContext();
        var resolvedPath = new ProGpuPathGeometry();
        var resolvedFigure = new ProGpuPathFigure(new Vector2(0, 0), isClosed: true);
        resolvedFigure.Segments.Add(new ProGpuLineSegment(new Vector2(60, 0)));
        resolvedFigure.Segments.Add(new ProGpuLineSegment(new Vector2(60, 20)));
        resolvedFigure.Segments.Add(new ProGpuLineSegment(new Vector2(0, 20)));
        resolvedPath.Figures.Add(resolvedFigure);
        var resolverCalls = 0;
        using var sink = new ProGpuCompositionCommandSink(
            new MediaDrawingContext(nativeContext),
            context: null,
            viewport3DTextureCache: null,
            pathOperationResolver: path =>
            {
                resolverCalls++;
                Assert.True(path.IsCombined);
                return resolvedPath;
            });

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);
        WriteUInt32(payload, 8, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, resolverCalls);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.True(command.Path!.IsCombined);
        Assert.NotNull(command.Brush);
        AssertNativeDashPattern(command.Pen, new[] { 100.0, 1.0 });
        Assert.Equal(0, sink.UnsupportedStateCount);
    }

    [Fact]
    public void DecodeNullResourcePushesThroughProGpuSinkDoNotAffectNativeCommands()
    {
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.Red });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 0);
        var clipPayload = new byte[8];
        WriteUInt32(clipPayload, 0, 0);
        var opacityMaskPayload = new byte[24];
        WriteUInt32(opacityMaskPayload, 16, 0);
        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 2, 3, 40, 50);
        WriteUInt32(rectanglePayload, 32, 1);

        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushClip, clipPayload))
            .Concat(CreateRecord(WpfMilCommandId.PushOpacityMask, opacityMaskPayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(7, 7, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(Matrix4x4.Identity, command.Transform);
    }

    [Fact]
    public void DecodeDrawDrawingThroughProGpuSinkEmitsStateAndPathCommands()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9)),
            Opacity = 0.5
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushOpacity,
            RenderCommandType.DrawPath,
            RenderCommandType.PopOpacity
        }, nativeContext.Commands.Select(command => command.Type).ToArray());

        var drawPath = nativeContext.Commands[1];
        Assert.NotNull(drawPath.Path);
        Assert.NotNull(drawPath.Brush);
        Assert.Equal(7, drawPath.Transform.M41);
        Assert.Equal(9, drawPath.Transform.M42);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesPathArcSegments()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(30, 40),
            Size = new Size(10, 20),
            RotationAngle = 45,
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise
        });
        geometry.Figures.Add(figure);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        var nativePath = command.Path;
        Assert.NotNull(nativePath);
        var nativeFigure = Assert.Single(nativePath!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));
        Assert.Equal(30, nativeArc.Point.X);
        Assert.Equal(40, nativeArc.Point.Y);
        Assert.Equal(10, nativeArc.Size.X);
        Assert.Equal(20, nativeArc.Size.Y);
        Assert.Equal(45, nativeArc.RotationAngle);
        Assert.True(nativeArc.IsLargeArc);
        Assert.Equal(ProGPU.Vector.SweepDirection.Clockwise, nativeArc.SweepDirection);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesPathSegmentSmoothJoinMetadata()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false
        };
        figure.Segments.Add(new LineSegment(new Vector2(10, 0)) { IsSmoothJoin = true });
        figure.Segments.Add(new QuadraticBezierSegment(new Vector2(15, 5), new Vector2(20, 0)) { IsSmoothJoin = true });
        figure.Segments.Add(new BezierSegment(new Vector2(25, 5), new Vector2(30, 5), new Vector2(35, 0)) { IsSmoothJoin = true });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(45, 0),
            Size = new Size(4, 8),
            RotationAngle = 0,
            IsLargeArc = false,
            SweepDirection = SweepDirection.Clockwise,
            IsSmoothJoin = true
        });
        geometry.Figures.Add(figure);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        var nativeFigure = Assert.Single(command.Path!.Figures);
        Assert.All(nativeFigure.Segments, segment => Assert.True(segment.IsSmoothJoin));
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesPathFillRuleAndStrokeMetadata()
    {
        var geometry = new PathGeometry
        {
            FillRule = FillRule.EvenOdd
        };
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false
        };
        figure.Segments.Add(new LineSegment(new Vector2(10, 0)) { IsStroked = false });
        figure.Segments.Add(new QuadraticBezierSegment(new Vector2(15, 5), new Vector2(20, 0)) { IsStroked = false });
        figure.Segments.Add(new BezierSegment(new Vector2(25, 5), new Vector2(30, 5), new Vector2(35, 0)) { IsStroked = false });
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(45, 0),
            Size = new Size(4, 8),
            RotationAngle = 0,
            IsLargeArc = false,
            SweepDirection = SweepDirection.Clockwise,
            IsStroked = false
        });
        geometry.Figures.Add(figure);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(ProGPU.Vector.FillRule.EvenOdd, command.Path!.FillRule);
        var nativeFigure = Assert.Single(command.Path.Figures);
        Assert.All(nativeFigure.Segments, segment => Assert.False(segment.IsStroked));
    }

    [Theory]
    [InlineData("Union", 2)]
    [InlineData("Intersect", 1)]
    [InlineData("Xor", 3)]
    [InlineData("Exclude", 0)]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesCombinedGeometryOperation(
        string combineMode,
        int expectedPathOperation)
    {
        var geometry = new FakeCombinedGeometry(
            combineMode,
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)),
            new FakeRectangleGeometry(new FakeRect(10, 10, 20, 20)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Path);
        Assert.True(command.Path!.IsCombined);
        Assert.Equal(expectedPathOperation, command.Path.Op);
        Assert.NotNull(command.Path.PathA);
        Assert.NotNull(command.Path.PathB);
        Assert.Single(command.Path.PathA!.Figures);
        Assert.Single(command.Path.PathB!.Figures);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkAppliesCombinedGeometryTransform()
    {
        var geometry = new FakeCombinedGeometry(
            "Union",
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)),
            new FakeRectangleGeometry(new FakeRect(10, 10, 20, 20)))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.True(command.Path!.IsCombined);
        var figureA = Assert.Single(command.Path.PathA!.Figures);
        var figureB = Assert.Single(command.Path.PathB!.Figures);
        Assert.Equal(7, figureA.StartPoint.X);
        Assert.Equal(9, figureA.StartPoint.Y);
        Assert.Equal(17, figureB.StartPoint.X);
        Assert.Equal(19, figureB.StartPoint.Y);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkSkipsCombinedGeometryWithUnsupportedChild()
    {
        var geometry = new FakeCombinedGeometry(
            "Union",
            new object(),
            new FakeRectangleGeometry(new FakeRect(10, 10, 20, 20)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 1, 0), result);
        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesTranslatedPathArcSegments()
    {
        var matrix = Matrix.Identity;
        matrix.Translate(7, 9);
        var geometry = CreateArcPathGeometry();
        geometry.Transform = new MatrixTransform(matrix);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativePath = Assert.Single(nativeContext.Commands).Path;
        var nativeFigure = Assert.Single(nativePath!.Figures);
        Assert.Equal(7, nativeFigure.StartPoint.X);
        Assert.Equal(9, nativeFigure.StartPoint.Y);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));
        Assert.Equal(37, nativeArc.Point.X);
        Assert.Equal(49, nativeArc.Point.Y);
        Assert.Equal(10, nativeArc.Size.X);
        Assert.Equal(20, nativeArc.Size.Y);
        Assert.Equal(45, nativeArc.RotationAngle);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesUniformlyScaledPathArcSegments()
    {
        var matrix = Matrix.Identity;
        matrix.Scale(2, 2);
        var geometry = CreateArcPathGeometry();
        geometry.Transform = new MatrixTransform(matrix);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativePath = Assert.Single(nativeContext.Commands).Path;
        var nativeFigure = Assert.Single(nativePath!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));
        Assert.Equal(60, nativeArc.Point.X);
        Assert.Equal(80, nativeArc.Point.Y);
        Assert.Equal(20, nativeArc.Size.X);
        Assert.Equal(40, nativeArc.Size.Y);
        Assert.Equal(45, nativeArc.RotationAngle);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesAxisAlignedScaledPathArcSegments()
    {
        var matrix = Matrix.Identity;
        matrix.Scale(2, 3);
        var geometry = CreateArcPathGeometry(rotationAngle: 0);
        geometry.Transform = new MatrixTransform(matrix);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativePath = Assert.Single(nativeContext.Commands).Path;
        var nativeFigure = Assert.Single(nativePath!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));
        Assert.Equal(60, nativeArc.Point.X);
        Assert.Equal(120, nativeArc.Point.Y);
        Assert.Equal(20, nativeArc.Size.X);
        Assert.Equal(60, nativeArc.Size.Y);
        Assert.Equal(0, nativeArc.RotationAngle);
    }

    [Fact]
    public void DecodeDrawGeometryThroughProGpuSinkPreservesComplexTransformedPathArcSegments()
    {
        var matrix = new Matrix
        {
            M11 = 1,
            M12 = 0.35,
            M21 = 0.2,
            M22 = 1,
            OffsetX = 5,
            OffsetY = 7
        };
        var matrixTransform = new MatrixTransform(matrix);
        var geometry = CreateArcPathGeometry();
        geometry.Transform = matrixTransform;
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, geometry });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativePath = Assert.Single(nativeContext.Commands).Path;
        var nativeFigure = Assert.Single(nativePath!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));

        var expectedStart = Vector2.Transform(new Vector2(0, 0), matrixTransform.Value);
        var expectedEnd = Vector2.Transform(new Vector2(30, 40), matrixTransform.Value);
        Assert.Equal(expectedStart.X, nativeFigure.StartPoint.X, 4);
        Assert.Equal(expectedStart.Y, nativeFigure.StartPoint.Y, 4);
        Assert.Equal(expectedEnd.X, nativeArc.Point.X, 4);
        Assert.Equal(expectedEnd.Y, nativeArc.Point.Y, 4);
        Assert.True(nativeArc.Size.X > 0);
        Assert.True(nativeArc.Size.Y > 0);
        Assert.True(float.IsFinite(nativeArc.RotationAngle));
        Assert.Equal(ProGPU.Vector.SweepDirection.Clockwise, nativeArc.SweepDirection);
    }

    [Fact]
    public void DecodeTransformedClipThroughProGpuSinkPreservesPathArcSegments()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9));
        var clip = CreateArcPathGeometry();
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, clip });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var clipPayload = new byte[8];
        WriteUInt32(clipPayload, 0, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushClip, clipPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(4, 4, 0, 0), result);
        var clipCommand = nativeContext.Commands[0];
        Assert.Equal(RenderCommandType.PushGeometryClip, clipCommand.Type);
        var nativeFigure = Assert.Single(clipCommand.Path!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));
        Assert.Equal(37, nativeArc.Point.X);
        Assert.Equal(49, nativeArc.Point.Y);
    }

    [Fact]
    public void DecodeTransformedClipThroughProGpuSinkPreservesComplexTransformedPathArcSegments()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0.35, 0.2, 1, 5, 7));
        var transformMatrix = new Matrix4x4(
            1f, 0.35f, 0f, 0f,
            0.2f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            5f, 7f, 0f, 1f);
        var clip = CreateArcPathGeometry();
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, clip });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var clipPayload = new byte[8];
        WriteUInt32(clipPayload, 0, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushClip, clipPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(4, 4, 0, 0), result);
        var clipCommand = nativeContext.Commands[0];
        Assert.Equal(RenderCommandType.PushGeometryClip, clipCommand.Type);
        var nativeFigure = Assert.Single(clipCommand.Path!.Figures);
        var nativeArc = Assert.IsType<ProGPU.Vector.ArcSegment>(Assert.Single(nativeFigure.Segments));

        var expectedEnd = Vector2.Transform(new Vector2(30, 40), transformMatrix);
        Assert.Equal(expectedEnd.X, nativeArc.Point.X, 4);
        Assert.Equal(expectedEnd.Y, nativeArc.Point.Y, 4);
        Assert.True(nativeArc.Size.X > 0);
        Assert.True(nativeArc.Size.Y > 0);
        Assert.True(float.IsFinite(nativeArc.RotationAngle));
    }

    [Fact]
    public void DecodeRectangleClipThroughProGpuSinkEmitsNativeClipCommands()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9));
        var clip = new RectangleGeometry(new System.Windows.Rect(2, 3, 40, 50));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, clip });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushClip, pushPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(4, 4, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushClip,
            RenderCommandType.PopClip
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
        var clipCommand = nativeContext.Commands[0];
        Assert.Equal(2, clipCommand.Rect.X);
        Assert.Equal(3, clipCommand.Rect.Y);
        Assert.Equal(40, clipCommand.Rect.Width);
        Assert.Equal(50, clipCommand.Rect.Height);
        Assert.Equal(7, clipCommand.Transform.M41);
        Assert.Equal(9, clipCommand.Transform.M42);
    }

    [Fact]
    public void DecodeClipThroughProGpuSinkEmitsGeometryClipCommands()
    {
        var clip = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true
        };
        figure.Segments.Add(new LineSegment(new Vector2(40, 0)));
        figure.Segments.Add(new LineSegment(new Vector2(20, 30)));
        clip.Figures.Add(figure);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { clip });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushClip, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushGeometryClip,
            RenderCommandType.PopGeometryClip
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
        var nativePath = nativeContext.Commands[0].Path;
        Assert.NotNull(nativePath);
        var nativeFigure = Assert.Single(nativePath!.Figures);
        Assert.Equal(2, nativeFigure.Segments.Count);
    }

    [Fact]
    public void DecodeOpacityThroughProGpuSinkEmitsMatchingPushAndPopCommands()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var pushPayload = new byte[8];
        WriteDouble(pushPayload, 0, 0.25);
        var renderData = CreateRecord(WpfMilCommandId.PushOpacity, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(
            renderData,
            sink,
            WpfMilResourceRegistry.FromDependentResources(Array.Empty<object?>()));

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushOpacity,
            RenderCommandType.PopOpacity
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
        Assert.Equal(0.25f, nativeContext.Commands[0].FontSize);
    }

    [Fact]
    public void DecodeOpacityMaskThroughProGpuSinkEmitsMatchingPushAndPopCommands()
    {
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var pushPayload = new byte[24];
        WriteSingle(pushPayload, 0, 1);
        WriteSingle(pushPayload, 4, 2);
        WriteSingle(pushPayload, 8, 30);
        WriteSingle(pushPayload, 12, 40);
        WriteUInt32(pushPayload, 16, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushOpacityMask, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushOpacityMask,
            RenderCommandType.PopOpacityMask
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
        Assert.Equal(1, nativeContext.Commands[0].Rect.X);
        Assert.Equal(2, nativeContext.Commands[0].Rect.Y);
        Assert.Equal(30, nativeContext.Commands[0].Rect.Width);
        Assert.Equal(40, nativeContext.Commands[0].Rect.Height);
    }

    [Fact]
    public void DecodeTransformedOpacityMaskThroughProGpuSinkStoresMaskTransform()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, Brushes.White });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var maskPayload = new byte[24];
        WriteSingle(maskPayload, 0, 1);
        WriteSingle(maskPayload, 4, 2);
        WriteSingle(maskPayload, 8, 30);
        WriteSingle(maskPayload, 12, 40);
        WriteUInt32(maskPayload, 16, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushOpacityMask, maskPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(4, 4, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushOpacityMask,
            RenderCommandType.PopOpacityMask
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
        Assert.Equal(7, nativeContext.Commands[0].Transform.M41);
        Assert.Equal(9, nativeContext.Commands[0].Transform.M42);
    }

    [Fact]
    public void DecodeDrawDrawingOpacityMaskThroughProGpuSinkEmitsMaskScope()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Bounds = new FakeRect(1, 2, 30, 40),
            OpacityMask = new FakeSolidColorBrush(new FakeColor(128, 255, 255, 255))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[]
        {
            RenderCommandType.PushOpacityMask,
            RenderCommandType.DrawPath,
            RenderCommandType.PopOpacityMask
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
        Assert.NotNull(nativeContext.Commands[0].Brush);
        Assert.Equal(1, nativeContext.Commands[0].Rect.X);
        Assert.Equal(2, nativeContext.Commands[0].Rect.Y);
        Assert.NotNull(nativeContext.Commands[1].Path);
    }

    [Fact]
    public void DecodeTransformedImageThroughProGpuSinkStoresTextureTransform()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 11, 13));
        var imageSource = new FakeBitmapSource();
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform, imageSource });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var imagePayload = new byte[40];
        WriteRect(imagePayload, 0, 2, 3, 40, 50);
        WriteUInt32(imagePayload, 32, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawImage, imagePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawTexture, command.Type);
        Assert.Equal(2, command.Rect.X);
        Assert.Equal(3, command.Rect.Y);
        Assert.Equal(40, command.Rect.Width);
        Assert.Equal(50, command.Rect.Height);
        Assert.Equal(11, command.Transform.M41);
        Assert.Equal(13, command.Transform.M42);
    }

    [Fact]
    public void DrawImageWithSourceRectThroughProGpuSinkStoresTextureSourceRect()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.DrawImage(
            new FakeBitmapSource(),
            new System.Windows.Rect(2, 3, 40, 50),
            new System.Windows.Rect(7, 11, 13, 17));

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawTexture, command.Type);
        Assert.Equal(2, command.Rect.X);
        Assert.Equal(3, command.Rect.Y);
        Assert.Equal(40, command.Rect.Width);
        Assert.Equal(50, command.Rect.Height);
        Assert.Equal(7, command.SrcRect.X);
        Assert.Equal(11, command.SrcRect.Y);
        Assert.Equal(13, command.SrcRect.Width);
        Assert.Equal(17, command.SrcRect.Height);
    }

    [Fact]
    public void DrawImageWithNearestBitmapScalingThroughProGpuSinkStoresTextureSamplingMode()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.PushBitmapScalingMode("NearestNeighbor");
        sink.DrawImage(new FakeBitmapSource(), new System.Windows.Rect(2, 3, 40, 50));
        sink.Pop();

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawTexture, command.Type);
        Assert.Equal(TextureSamplingMode.Nearest, command.TextureSamplingMode);
    }

    [Fact]
    public void DrawImageWithLowQualityBitmapScalingThroughProGpuSinkStoresLinearTextureSamplingMode()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.PushBitmapScalingMode("LowQuality");
        sink.DrawImage(new FakeBitmapSource(), new System.Windows.Rect(2, 3, 40, 50));
        sink.Pop();

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawTexture, command.Type);
        Assert.Equal(TextureSamplingMode.Linear, command.TextureSamplingMode);
    }

    [Theory]
    [InlineData("HighQuality")]
    [InlineData("Fant")]
    [InlineData("2")]
    public void DrawImageWithHighQualityBitmapScalingThroughProGpuSinkStoresCubicTextureSamplingMode(string bitmapScalingMode)
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.PushBitmapScalingMode(bitmapScalingMode);
        sink.DrawImage(new FakeBitmapSource(), new System.Windows.Rect(2, 3, 40, 50));
        sink.Pop();

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawTexture, command.Type);
        Assert.Equal(TextureSamplingMode.Cubic, command.TextureSamplingMode);
    }

    [Fact]
    public void DrawRectangleWithAliasedEdgeModeThroughProGpuSinkStoresAndRestoresEdgeMode()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.PushEdgeMode("Aliased");
        sink.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(2, 3, 40, 50));
        sink.Pop();
        sink.DrawRectangle(Brushes.Black, null, new System.Windows.Rect(4, 5, 10, 20));

        Assert.Collection(
            nativeContext.Commands,
            first =>
            {
                Assert.Equal(RenderCommandType.DrawRect, first.Type);
                Assert.True(first.IsEdgeAliased);
            },
            second =>
            {
                Assert.Equal(RenderCommandType.DrawRect, second.Type);
                Assert.False(second.IsEdgeAliased);
            });
    }

    [Fact]
    public void DrawGeometryWithAliasedEdgeModeThroughProGpuSinkStoresEdgeMode()
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true
        };
        figure.Segments.Add(new LineSegment(new Vector2(10, 0)));
        figure.Segments.Add(new LineSegment(new Vector2(10, 10)));
        geometry.Figures.Add(figure);

        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        sink.PushEdgeMode("Aliased");
        sink.DrawGeometry(Brushes.Black, null, geometry);
        sink.Pop();

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawPath, command.Type);
        Assert.True(command.IsEdgeAliased);
    }

    [Fact]
    public void DecodeTransformedGlyphRunThroughProGpuSinkSkipsUnresolvedFontCommand()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 11, 13));
        var glyphRun = new GlyphRun(
            null!,
            16,
            new ushort[] { 7, 8 },
            new[] { new Vector2(0, 0), new Vector2(5, 0) })
        {
            Position = new Vector2(2, 3),
            Transform = Matrix4x4.CreateTranslation(17, 19, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            transform,
            Brushes.Black,
            glyphRun
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var glyphPayload = new byte[8];
        WriteUInt32(glyphPayload, 0, 2);
        WriteUInt32(glyphPayload, 4, 3);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawGlyphRun, glyphPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DrawGlyphRunThroughProGpuSinkPreservesStyleSimulationFlags()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var glyphRun = new GlyphRun(
            null!,
            16,
            new ushort[] { 7 },
            new[] { Vector2.Zero })
        {
            IsBold = true,
            IsItalic = true
        };

        sink.DrawGlyphRun(Brushes.Black, glyphRun);

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawGlyphRun, command.Type);
        Assert.Equal(new global::ProGPU.Scene.Rect(0, -16, 16, 16), command.Rect);
        Assert.True(command.IsBold);
        Assert.True(command.IsItalic);
    }

    [Fact]
    public void DrawNativeGlyphRunThroughProGpuSinkStoresCachedHitTestBounds()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var glyphRun = new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 7, 8 },
            GlyphPositions =
            [
                new PortablePoint(0, 0),
                new PortablePoint(9, 2)
            ],
            BaselineOrigin = new PortablePoint(3, 20),
            FontRenderingEmSize = 16,
            FontFamilyNames = new[] { "Arial" },
            HasTransform = true,
            Transform = new PortableMatrix3x2(1, 0, 0, 1, 5, 6)
        };

        ((IWpfNativePrimitiveCommandSink)sink).DrawNativeGlyphRun(Brushes.Black, glyphRun);

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawGlyphRun, command.Type);
        Assert.Equal(new global::ProGPU.Scene.Rect(3, 4, 25, 18), command.Rect);
        Assert.Equal(5, command.Transform.M41);
        Assert.Equal(6, command.Transform.M42);
    }

    [Fact]
    public void DrawTextThroughProGpuSinkStoresTransform()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var matrix = Matrix.Identity;
        matrix.Translate(5, 7);
        var formattedText = new FormattedText(
            "ProGPU",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial")),
            14,
            Brushes.Black);

        sink.PushTransform(new MatrixTransform(matrix));
        sink.DrawText(formattedText, new Point(2, 3));
        sink.Pop();

        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawText, command.Type);
        Assert.Equal("ProGPU", command.Text);
        Assert.Equal(14, command.FontSize);
        Assert.Equal(2, command.Position.X);
        Assert.Equal(5, command.Transform.M41);
        Assert.Equal(7, command.Transform.M42);
    }

    [Fact]
    public void DrawTextWithAliasedTextRenderingModeThroughProGpuSinkStoresAndRestoresMode()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var formattedText = new FormattedText(
            "ProGPU",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial")),
            14,
            Brushes.Black);
        var glyphRun = new GlyphRun(
            null!,
            16,
            new ushort[] { 7 },
            new[] { Vector2.Zero });

        sink.PushTextRenderingMode("Aliased");
        sink.DrawText(formattedText, new Point(2, 3));
        sink.Pop();
        sink.DrawGlyphRun(Brushes.Black, glyphRun);

        Assert.Collection(
            nativeContext.Commands,
            first =>
            {
                Assert.Equal(RenderCommandType.DrawText, first.Type);
                Assert.Equal(global::ProGPU.Scene.TextRenderingMode.Aliased, first.TextRenderingMode);
                Assert.True(first.IsTextAliased);
            },
            second =>
            {
                Assert.Equal(RenderCommandType.DrawGlyphRun, second.Type);
                Assert.Equal(global::ProGPU.Scene.TextRenderingMode.Grayscale, second.TextRenderingMode);
                Assert.False(second.IsTextAliased);
            });
    }

    [Fact]
    public void DrawTextWithClearTypeTextRenderingModeThroughProGpuSinkStoresAndRestoresMode()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var formattedText = new FormattedText(
            "ProGPU",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial")),
            14,
            Brushes.Black);
        var glyphRun = new GlyphRun(
            null!,
            16,
            new ushort[] { 7 },
            new[] { Vector2.Zero });

        sink.PushTextRenderingMode("ClearType");
        sink.DrawText(formattedText, new Point(2, 3));
        sink.Pop();
        sink.DrawGlyphRun(Brushes.Black, glyphRun);

        Assert.Collection(
            nativeContext.Commands,
            first =>
            {
                Assert.Equal(RenderCommandType.DrawText, first.Type);
                Assert.Equal(global::ProGPU.Scene.TextRenderingMode.ClearType, first.TextRenderingMode);
                Assert.False(first.IsTextAliased);
            },
            second =>
            {
                Assert.Equal(RenderCommandType.DrawGlyphRun, second.Type);
                Assert.Equal(global::ProGPU.Scene.TextRenderingMode.Grayscale, second.TextRenderingMode);
                Assert.False(second.IsTextAliased);
            });
    }

    [Fact]
    public void DrawTextWithAnimatedTextHintingModeThroughProGpuSinkStoresAndRestoresMode()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));
        var formattedText = new FormattedText(
            "ProGPU",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial")),
            14,
            Brushes.Black);
        var glyphRun = new GlyphRun(
            null!,
            16,
            new ushort[] { 7 },
            new[] { Vector2.Zero });

        sink.PushTextHintingMode("Animated");
        sink.DrawText(formattedText, new Point(2, 3));
        sink.Pop();
        sink.DrawGlyphRun(Brushes.Black, glyphRun);

        Assert.Collection(
            nativeContext.Commands,
            first =>
            {
                Assert.Equal(RenderCommandType.DrawText, first.Type);
                Assert.Equal(global::ProGPU.Scene.TextHintingMode.Animated, first.TextHintingMode);
            },
            second =>
            {
                Assert.Equal(RenderCommandType.DrawGlyphRun, second.Type);
                Assert.Equal(global::ProGPU.Scene.TextHintingMode.Auto, second.TextHintingMode);
            });
    }

    [Fact]
    public void DecodeGuidelineScopeThroughProGpuSinkDoesNotEmitRenderCommands()
    {
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var guidelinePayload = new byte[8];
        WriteDouble(guidelinePayload, 0, 12.5);
        var renderData = CreateRecord(WpfMilCommandId.PushGuidelineY1, guidelinePayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(
            renderData,
            sink,
            WpfMilResourceRegistry.FromDependentResources(Array.Empty<object?>()));

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DecodeGuidelineY1ThroughProGpuSinkPreservesNativeLineYCoordinate()
    {
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            new Pen(Brushes.Black, 2)
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var guidelinePayload = new byte[8];
        WriteDouble(guidelinePayload, 0, 12.25);
        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 1, 12.25);
        WritePoint(linePayload, 16, 30, 12.25);
        WriteUInt32(linePayload, 32, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushGuidelineY1, guidelinePayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawLine, linePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.Equal(1, command.Position.X);
        Assert.Equal(12.25f, command.Position.Y);
        Assert.Equal(30, command.Position2.X);
        Assert.Equal(12.25f, command.Position2.Y);
    }

    [Fact]
    public void DecodeGuidelineY2ThroughProGpuSinkPreservesNativeRectangleLeadingEdgeAndOffset()
    {
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.Red });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var guidelinePayload = new byte[16];
        WriteDouble(guidelinePayload, 0, 10.25);
        WriteDouble(guidelinePayload, 8, 5.5);
        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 2, 10.25, 40, 5.5);
        WriteUInt32(rectanglePayload, 32, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushGuidelineY2, guidelinePayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(2, command.Rect.X);
        Assert.Equal(10.25f, command.Rect.Y);
        Assert.Equal(40, command.Rect.Width);
        Assert.Equal(5.5f, command.Rect.Height);
    }

    [Fact]
    public void DecodeGuidelineSetThroughProGpuSinkPreservesNativeRectangleCoordinates()
    {
        var guidelineSet = new FakeGuidelineSet(new[] { 2.25, 42.25 }, new[] { 3.25, 53.25 });
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            guidelineSet,
            Brushes.Red
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var guidelinePayload = new byte[8];
        WriteUInt32(guidelinePayload, 0, 1);
        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 2.25, 3.25, 40, 50);
        WriteUInt32(rectanglePayload, 32, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushGuidelineSet, guidelinePayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawRect, command.Type);
        Assert.Equal(2.25f, command.Rect.X);
        Assert.Equal(3.25f, command.Rect.Y);
        Assert.Equal(40, command.Rect.Width);
        Assert.Equal(50, command.Rect.Height);
    }

    [Fact]
    public void DecodeGuidelineY1ThroughProGpuSinkDoesNotSnapRotatedLine()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(0, 1, -1, 0, 0, 0));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[]
        {
            transform,
            new Pen(Brushes.Black, 2)
        });
        var nativeContext = new ProGpuDrawingContext();
        using var sink = new ProGpuCompositionCommandSink(new MediaDrawingContext(nativeContext));

        var transformPayload = new byte[8];
        WriteUInt32(transformPayload, 0, 1);
        var guidelinePayload = new byte[8];
        WriteDouble(guidelinePayload, 0, 12.25);
        var linePayload = new byte[40];
        WritePoint(linePayload, 0, 1, 12.25);
        WritePoint(linePayload, 16, 30, 12.25);
        WriteUInt32(linePayload, 32, 2);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, transformPayload)
            .Concat(CreateRecord(WpfMilCommandId.PushGuidelineY1, guidelinePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawLine, linePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(5, 5, 0, 0), result);
        var command = Assert.Single(nativeContext.Commands);
        Assert.Equal(RenderCommandType.DrawLine, command.Type);
        Assert.Equal(12.25f, command.Position.Y);
        Assert.Equal(12.25f, command.Position2.Y);
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static PathGeometry CreateArcPathGeometry(float rotationAngle = 45)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = false
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(30, 40),
            Size = new Size(10, 20),
            RotationAngle = rotationAngle,
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise
        });
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void AssertNativeDashPattern(ProGPU.Vector.Pen? pen, double[] expectedDashes, double expectedOffset = 0.0)
    {
        Assert.NotNull(pen);
        Assert.True(pen!.HasDashPattern);
        Assert.Equal(expectedDashes, pen.DashArray);
        Assert.Equal(expectedOffset, pen.DashOffset);
    }

    private static void WriteRect(byte[] target, int offset, double x, double y, double width, double height)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
        WriteDouble(target, offset + 16, width);
        WriteDouble(target, offset + 24, height);
    }

    private static FakeGradientStop[] CreateGradientStops(int count)
    {
        var stops = new FakeGradientStop[count];
        for (var i = 0; i < count; i++)
        {
            var red = (byte)Math.Clamp(255 - i * 20, 0, 255);
            var green = (byte)Math.Clamp(i * 20, 0, 255);
            var blue = (byte)Math.Clamp(i * 30, 0, 255);
            var offset = count == 1 ? 0 : (double)i / (count - 1);
            stops[i] = new FakeGradientStop(new FakeColor(255, red, green, blue), offset);
        }

        return stops;
    }

    private static PortablePoint ToPortablePoint(FakePoint point)
    {
        return new PortablePoint(point.X, point.Y);
    }

    private static PortableColor ToPortableColor(FakeColor color)
    {
        return new PortableColor(color.A, color.R, color.G, color.B);
    }

    private static PortableGradientStop[] ToPortableGradientStops(FakeGradientStop[] stops)
    {
        var portableStops = new PortableGradientStop[stops.Length];
        for (var i = 0; i < stops.Length; i++)
        {
            portableStops[i] = new PortableGradientStop(ToPortableColor(stops[i].Color), stops[i].Offset);
        }

        return portableStops;
    }

    private static PortablePenLineCap ToPortablePenLineCap(object? value)
    {
        switch (value?.ToString())
        {
            case "Square":
                return PortablePenLineCap.Square;
            case "Round":
                return PortablePenLineCap.Round;
            case "Triangle":
                return PortablePenLineCap.Triangle;
            default:
                return PortablePenLineCap.Flat;
        }
    }

    private static PortablePenLineJoin ToPortablePenLineJoin(object? value)
    {
        switch (value?.ToString())
        {
            case "Bevel":
                return PortablePenLineJoin.Bevel;
            case "Round":
                return PortablePenLineJoin.Round;
            default:
                return PortablePenLineJoin.Miter;
        }
    }

    private static bool TryMapOptionalTransform(
        object? transformValue,
        out bool hasTransform,
        out PortableMatrix3x2 transform)
    {
        hasTransform = false;
        transform = PortableMatrix3x2.Identity;
        if (transformValue == null)
        {
            return true;
        }

        if (transformValue is IPortableTransformMatrixSource transformSource
            && transformSource.TryGetPortableTransformMatrix(out transform))
        {
            hasTransform = true;
            return true;
        }

        return false;
    }

    private static bool TryMapBrushMappingMode(string value, out PortableBrushMappingMode mappingMode)
    {
        switch (value)
        {
            case "RelativeToBoundingBox":
                mappingMode = PortableBrushMappingMode.RelativeToBoundingBox;
                return true;
            case "Absolute":
                mappingMode = PortableBrushMappingMode.Absolute;
                return true;
            default:
                mappingMode = PortableBrushMappingMode.RelativeToBoundingBox;
                return false;
        }
    }

    private static PortableGradientColorInterpolationMode ToPortableGradientColorInterpolationMode(string value)
    {
        return value == "ScRgbLinearInterpolation"
            ? PortableGradientColorInterpolationMode.ScRgbLinearInterpolation
            : PortableGradientColorInterpolationMode.SRgbLinearInterpolation;
    }

    private sealed class FakePen : IPortablePenSource
    {
        public FakePen(object brush, double thickness)
        {
            Brush = brush;
            Thickness = thickness;
        }

        public object Brush { get; }

        public double Thickness { get; }

        public object? DashStyle { get; init; }

        public object? StartLineCap { get; init; }

        public object? EndLineCap { get; init; }

        public object? DashCap { get; init; }

        public object? LineJoin { get; init; }

        public double MiterLimit { get; init; } = 10.0;

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = null!;
            if (Brush is not IPortableBrushSource brushSource
                || !brushSource.TryGetPortableBrush(out var portableBrush))
            {
                return false;
            }

            var dashArray = Array.Empty<double>();
            var dashOffset = 0.0;
            if (DashStyle is FakeDashStyle dashStyle)
            {
                dashArray = dashStyle.Dashes;
                dashOffset = dashStyle.Offset;
            }

            pen = new PortablePen(
                portableBrush,
                Thickness,
                ToPortablePenLineCap(StartLineCap),
                ToPortablePenLineCap(EndLineCap),
                ToPortablePenLineCap(DashCap),
                ToPortablePenLineJoin(LineJoin),
                MiterLimit,
                dashArray,
                dashOffset);
            return true;
        }
    }

    private sealed class FakeDashStyle
    {
        public FakeDashStyle(double[] dashes, double offset)
        {
            Dashes = dashes;
            Offset = offset;
        }

        public double[] Dashes { get; }

        public double Offset { get; }
    }

    private sealed class FakeGuidelineSet : IPortableGuidelineSetSource
    {
        private readonly PortableGuidelineSet _guidelineSet;

        public FakeGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            _guidelineSet = new PortableGuidelineSet(
                isFrozen: true,
                isDynamic: true,
                guidelinesX,
                guidelinesY);
        }

        public bool TryGetPortableGuidelineSet(out PortableGuidelineSet guidelineSet)
        {
            guidelineSet = _guidelineSet;
            return true;
        }
    }

    private static void WritePoint(byte[] target, int offset, double x, double y)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteSingle(byte[] target, int offset, float value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    }

    private static void WriteDouble(byte[] target, int offset, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
    }

    private sealed class FakeLinearGradientBrush : IPortableBrushSource
    {
        private readonly FakeGradientStop[] _stops;

        public FakeLinearGradientBrush(FakePoint startPoint, FakePoint endPoint, params FakeGradientStop[] stops)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
            _stops = stops;
            GradientStops = new FakeGradientStopCollection(stops);
        }

        public FakePoint StartPoint { get; }

        public FakePoint EndPoint { get; }

        public FakeGradientStopCollection GradientStops { get; }

        public string MappingMode { get; init; } = "RelativeToBoundingBox";

        public string ColorInterpolationMode { get; init; } = "SRgbLinearInterpolation";

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = null!;
            if (!TryMapBrushMappingMode(MappingMode, out var mappingMode)
                || !TryMapOptionalTransform(Transform, out var hasTransform, out var transform)
                || !TryMapOptionalTransform(RelativeTransform, out var hasRelativeTransform, out var relativeTransform))
            {
                return false;
            }

            brush = PortableBrush.LinearGradient(
                ToPortablePoint(StartPoint),
                ToPortablePoint(EndPoint),
                ToPortableGradientStops(_stops),
                mappingMode: mappingMode,
                colorInterpolationMode: ToPortableGradientColorInterpolationMode(ColorInterpolationMode),
                hasTransform: hasTransform,
                transform: transform,
                hasRelativeTransform: hasRelativeTransform,
                relativeTransform: relativeTransform);
            return true;
        }
    }

    private sealed class FakeRadialGradientBrush : IPortableBrushSource
    {
        private readonly FakeGradientStop[] _stops;

        public FakeRadialGradientBrush(FakePoint center, FakePoint gradientOrigin, double radiusX, double radiusY, params FakeGradientStop[] stops)
        {
            Center = center;
            GradientOrigin = gradientOrigin;
            RadiusX = radiusX;
            RadiusY = radiusY;
            _stops = stops;
            GradientStops = new FakeGradientStopCollection(stops);
        }

        public FakePoint Center { get; }

        public FakePoint GradientOrigin { get; }

        public double RadiusX { get; }

        public double RadiusY { get; }

        public FakeGradientStopCollection GradientStops { get; }

        public string MappingMode { get; init; } = "RelativeToBoundingBox";

        public string ColorInterpolationMode { get; init; } = "SRgbLinearInterpolation";

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = null!;
            if (!TryMapBrushMappingMode(MappingMode, out var mappingMode)
                || !TryMapOptionalTransform(Transform, out var hasTransform, out var transform)
                || !TryMapOptionalTransform(RelativeTransform, out var hasRelativeTransform, out var relativeTransform))
            {
                return false;
            }

            brush = PortableBrush.RadialGradient(
                ToPortablePoint(Center),
                ToPortablePoint(GradientOrigin),
                RadiusX,
                RadiusY,
                ToPortableGradientStops(_stops),
                mappingMode: mappingMode,
                colorInterpolationMode: ToPortableGradientColorInterpolationMode(ColorInterpolationMode),
                hasTransform: hasTransform,
                transform: transform,
                hasRelativeTransform: hasRelativeTransform,
                relativeTransform: relativeTransform);
            return true;
        }
    }

    private sealed class FakeGradientStopCollection
    {
        private readonly FakeGradientStop[] _items;

        public FakeGradientStopCollection(FakeGradientStop[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakeGradientStop this[int index] => _items[index];
    }

    private sealed class FakeGradientStop
    {
        public FakeGradientStop(FakeColor color, double offset)
        {
            Color = color;
            Offset = offset;
        }

        public FakeColor Color { get; }

        public double Offset { get; }
    }

    private sealed class FakeSolidColorBrush : IPortableBrushSource
    {
        public FakeSolidColorBrush(FakeColor color)
        {
            Color = color;
        }

        public FakeColor Color { get; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = PortableBrush.SolidColor(ToPortableColor(Color));
            return true;
        }
    }

    private sealed class FakeBitmapSource : System.Windows.Media.Imaging.BitmapSource
    {
        private static readonly GpuTexture s_texture = (GpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(GpuTexture));

        public override int PixelWidth => 1;

        public override int PixelHeight => 1;

        public override GpuTexture GpuTexture => s_texture;
    }

    private sealed class FakeGeometryDrawing : IPortableGeometryDrawingStateSource
    {
        public FakeGeometryDrawing(object? brush, object? pen, object? geometry)
        {
            Brush = brush;
            Pen = pen;
            Geometry = geometry;
        }

        public object? Brush { get; }

        public object? Pen { get; }

        public object? Geometry { get; }

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasBrush = Brush != null,
                Brush = Brush,
                HasPen = Pen != null,
                Pen = Pen,
                HasGeometry = Geometry != null,
                Geometry = Geometry
            };
            return true;
        }
    }

    private sealed class FakeDrawingGroup : IPortableDrawingGroupStateSource
    {
        private readonly object[] _children;

        public FakeDrawingGroup(params object[] children)
        {
            _children = children;
            Children = new FakeDrawingCollection(children);
        }

        public FakeDrawingCollection Children { get; }

        public object? Transform { get; init; }

        public double Opacity { get; init; } = 1;

        public object? Bounds { get; init; }

        public object? OpacityMask { get; init; }

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = new PortableDrawingGroupState
            {
                HasTransform = Transform != null,
                Transform = Transform,
                HasOpacity = true,
                Opacity = Opacity,
                HasOpacityMask = OpacityMask != null,
                OpacityMask = OpacityMask,
                Children = _children
            };

            if (Bounds is FakeRect bounds)
            {
                state.HasBounds = true;
                state.Bounds = new PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }

            return true;
        }
    }

    private sealed class FakeDrawingCollection
    {
        private readonly object[] _items;

        public FakeDrawingCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private static PortableGeometryPath CreatePortableRectangleGeometry(FakeRect rect)
    {
        return new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.Nonzero,
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(rect.X, rect.Y),
                    IsClosed = true,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.Line(new PortablePoint(rect.X + rect.Width, rect.Y), isSmoothJoin: false, isStroked: true),
                        PortablePathSegment.Line(new PortablePoint(rect.X + rect.Width, rect.Y + rect.Height), isSmoothJoin: false, isStroked: true),
                        PortablePathSegment.Line(new PortablePoint(rect.X, rect.Y + rect.Height), isSmoothJoin: false, isStroked: true)
                    ]
                }
            ]
        };
    }

    private static bool TryGetPortableGeometryPath(object? value, out PortableGeometryPath path)
    {
        if (value == null)
        {
            path = new PortableGeometryPath { Kind = PortableGeometryPathKind.Path };
            return true;
        }

        if (value is IPortableGeometryPathSource source)
        {
            return source.TryGetPortableGeometryPath(out path);
        }

        path = null!;
        return false;
    }

    private static int ToPortableCombineOperation(string geometryCombineMode)
    {
        return geometryCombineMode switch
        {
            "Exclude" => 0,
            "Intersect" => 1,
            "Xor" => 3,
            _ => 2
        };
    }

    private sealed class FakeRectangleGeometry : IPortableGeometryPathSource
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = CreatePortableRectangleGeometry(Rect);
            return true;
        }
    }

    private sealed class FakePortablePathGeometry(PortableGeometryPath path) : IPortableGeometryPathSource
    {
        public bool TryGetPortableGeometryPath(out PortableGeometryPath portablePath)
        {
            portablePath = path;
            return true;
        }
    }

    private sealed class FakeCombinedGeometry : IPortableGeometryPathSource
    {
        public FakeCombinedGeometry(string geometryCombineMode, object? geometry1, object? geometry2)
        {
            GeometryCombineMode = geometryCombineMode;
            Geometry1 = geometry1;
            Geometry2 = geometry2;
        }

        public string GeometryCombineMode { get; }

        public object? Geometry1 { get; }

        public object? Geometry2 { get; }

        public object? Transform { get; init; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = null!;
            if (!WpfReplayToProGpuCommandTests.TryGetPortableGeometryPath(Geometry1, out var pathA)
                || !WpfReplayToProGpuCommandTests.TryGetPortableGeometryPath(Geometry2, out var pathB))
            {
                return false;
            }

            path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Combined,
                PathA = pathA,
                PathB = pathB,
                CombineOperation = ToPortableCombineOperation(GeometryCombineMode)
            };

            if (TryMapOptionalTransform(Transform, out var hasTransform, out var transform) && hasTransform)
            {
                path.Transform = transform;
            }

            return true;
        }
    }

    private sealed class FakeMatrixTransform : IPortableTransformMatrixSource
    {
        public FakeMatrixTransform(FakeMatrix value)
        {
            Value = value;
        }

        public FakeMatrix Value { get; }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = new PortableMatrix3x2(
                Value.M11,
                Value.M12,
                Value.M21,
                Value.M22,
                Value.OffsetX,
                Value.OffsetY);
            return true;
        }
    }

    private readonly record struct FakeColor(byte A, byte R, byte G, byte B);

    private readonly record struct FakePoint(double X, double Y);

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private readonly record struct FakeMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY);
}
