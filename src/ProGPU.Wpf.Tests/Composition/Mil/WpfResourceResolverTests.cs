using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using Xunit;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using ProGpuBlurEffect = ProGPU.Scene.BlurEffect;
using ProGpuEffectBase = ProGPU.Scene.EffectBase;
using ProGpuLinearGradientBrush = ProGPU.Vector.LinearGradientBrush;
using ProGpuRadialGradientBrush = ProGPU.Vector.RadialGradientBrush;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfResourceResolverTests
{
    [Fact]
    public void NativeGlyphInkBoundsRemainAuthoritativeAndInvalidateAdapterCache()
    {
        var value = new PortableNativeGlyphRun
        {
            GlyphIndices = [3], GlyphPositions = [Vector2.Zero], FontRenderingEmSize = 12,
            FontFamilyNames = ["Arial"], HasInkBounds = true, InkBounds = new PortableRect(-2, -10, 16, 14)
        };
        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(value, out var first));
        Assert.True(first.HasInkBounds);
        Assert.Equal(-2, first.InkBounds.X);
        Assert.Equal(14, first.InkBounds.Height);
        value.InkBounds = PortableRect.Empty;
        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(value, out var empty));
        Assert.True(empty.HasInkBounds);
        Assert.True(empty.InkBounds.IsEmpty);
        value.HasInkBounds = false;
        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(value, out var missing));
        Assert.False(missing.HasInkBounds);
    }

    [Fact]
    public void FromDependentResourcesIndexesReadOnlyListWithoutEnumerating()
    {
        var brush = Brushes.Green;
        var pen = new MediaPen(Brushes.Black, 2);
        var resources = new ThrowingEnumerableResourceList(brush, pen);

        var resolver = WpfResourceResolver.FromDependentResources(resources);

        Assert.Same(brush, resolver.ResolveBrush(1));
        Assert.Same(pen, resolver.ResolvePen(2));
        Assert.Null(resolver.ResolveBrush(3));
        Assert.Equal(0, resources.EnumerationCount);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableBrushAndPenFixtures()
    {
        var brush = new FakeSolidColorBrush(new FakeColor(128, 10, 20, 30), opacity: 0.5);
        var pen = new FakePen(new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)), 4);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush, pen });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
        var adaptedBrush = Assert.IsType<SolidColorBrush>(sink.DrawRectangles[0].Brush);
        Assert.Equal(64, adaptedBrush.Color.A);
        Assert.Equal(10, adaptedBrush.Color.R);
        Assert.Equal(20, adaptedBrush.Color.G);
        Assert.Equal(30, adaptedBrush.Color.B);
        Assert.NotNull(sink.DrawRectangles[0].Pen);
        Assert.Equal(4, sink.DrawRectangles[0].Pen!.Thickness);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableSolidBrushAndPen()
    {
        var brush = new FakePortableBrush(
            PortableBrush.SolidColor(new PortableColor(128, 10, 20, 30), opacity: 0.5));
        var pen = new FakePortablePen(
            PortableBrush.SolidColor(new PortableColor(255, 1, 2, 3)),
            thickness: 4,
            startLineCap: PortablePenLineCap.Square,
            endLineCap: PortablePenLineCap.Round,
            dashCap: PortablePenLineCap.Round,
            lineJoin: PortablePenLineJoin.Bevel,
            miterLimit: 2.0,
            dashArray: new[] { 2.0, 3.0 },
            dashOffset: 1.5);
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush, pen });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
        var adaptedBrush = Assert.IsType<SolidColorBrush>(sink.DrawRectangles[0].Brush);
        Assert.Equal(64, adaptedBrush.Color.A);
        Assert.Equal(10, adaptedBrush.Color.R);
        Assert.Equal(20, adaptedBrush.Color.G);
        Assert.Equal(30, adaptedBrush.Color.B);
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.Equal(4, adaptedPen.Thickness);
        Assert.Equal(PenLineCap.Square, adaptedPen.StartLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.EndLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.DashCap);
        Assert.Equal(PenLineJoin.Bevel, adaptedPen.LineJoin);
        Assert.Equal(2.0, adaptedPen.MiterLimit);
        Assert.NotNull(adaptedPen.DashStyle);
        Assert.Equal(new[] { 2.0, 3.0 }, adaptedPen.DashStyle!.Dashes);
        Assert.Equal(1.5, adaptedPen.DashStyle.Offset);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableLinearGradientBrush()
    {
        var brush = new FakePortableBrush(
            PortableBrush.LinearGradient(
                new PortablePoint(0, 0),
                new PortablePoint(1, 1),
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                    new PortableGradientStop(new PortableColor(128, 0, 0, 255), 1)
                },
                opacity: 0.75,
                spreadMethod: PortableGradientSpreadMethod.Repeat,
                colorInterpolationMode: PortableGradientColorInterpolationMode.ScRgbLinearInterpolation));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(
            ToNative(Assert.Single(sink.DrawRectangles).Brush!, new WpfReplayRect(1, 2, 30, 40)));
        Assert.Equal(1, nativeBrush.StartPoint.X);
        Assert.Equal(2, nativeBrush.StartPoint.Y);
        Assert.Equal(31, nativeBrush.EndPoint.X);
        Assert.Equal(42, nativeBrush.EndPoint.Y);
        Assert.Equal(0.75f, nativeBrush.Opacity);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Repeat, nativeBrush.SpreadMethod);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
        Assert.Equal(2, nativeBrush.Stops.Length);
        Assert.Equal(0f, nativeBrush.Stops[0].Offset);
        Assert.Equal(1f, nativeBrush.Stops[1].Offset);
        Assert.Equal(1f, nativeBrush.Stops[0].Color.X);
        Assert.Equal(0.5f, nativeBrush.Stops[1].Color.W, precision: 2);
    }

    [Fact]
    public void AdaptNativeBrushMapsPortableRelativeRadialGradient()
    {
        var brush = new FakePortableBrush(
            PortableBrush.RadialGradient(
                new PortablePoint(0.5, 0.5),
                new PortablePoint(0.25, 0.75),
                radiusX: 0.25,
                radiusY: 0.5,
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 0, 255, 0), 0),
                    new PortableGradientStop(new PortableColor(255, 0, 0, 0), 1)
                },
                spreadMethod: PortableGradientSpreadMethod.Reflect));

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(10, 20, 100, 50),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        var radialBrush = Assert.IsType<ProGpuRadialGradientBrush>(nativeBrush);
        Assert.Equal(60, radialBrush.Center.X);
        Assert.Equal(45, radialBrush.Center.Y);
        Assert.Equal(35, radialBrush.GradientOrigin.X);
        Assert.Equal(57.5f, radialBrush.GradientOrigin.Y);
        Assert.Equal(25, radialBrush.RadiusX);
        Assert.Equal(25, radialBrush.RadiusY);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Reflect, radialBrush.SpreadMethod);
        Assert.Equal(2, radialBrush.Stops.Length);
    }

    [Fact]
    public void AdaptNativeBrushAppliesPortableLinearGradientTransform()
    {
        var brush = new FakePortableBrush(
            PortableBrush.LinearGradient(
                new PortablePoint(0, 0),
                new PortablePoint(10, 0),
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                    new PortableGradientStop(new PortableColor(255, 0, 0, 255), 1)
                },
                mappingMode: PortableBrushMappingMode.Absolute,
                hasTransform: true,
                transform: new PortableMatrix3x2(1, 0, 0, 1, 5, 7)));

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(-5, linearBrush.CoordinateTransform.M41);
        Assert.Equal(-7, linearBrush.CoordinateTransform.M42);
    }

    [Fact]
    public void AdaptNativeBrushCountsNonInvertiblePortableLinearGradientTransform()
    {
        var brush = new FakePortableBrush(
            PortableBrush.LinearGradient(
                new PortablePoint(0, 0),
                new PortablePoint(10, 0),
                new[]
                {
                    new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                    new PortableGradientStop(new PortableColor(255, 0, 0, 255), 1)
                },
                mappingMode: PortableBrushMappingMode.Absolute,
                hasTransform: true,
                transform: new PortableMatrix3x2(0, 0, 0, 1, 0, 0)));

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(1, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(Matrix4x4.Identity, linearBrush.CoordinateTransform);
    }

    [Fact]
    public void AdaptNativeBrushAppliesPortableFixtureBrushTransform()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(10, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            MappingMode = "Absolute",
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7))
        };

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(-5, linearBrush.CoordinateTransform.M41);
        Assert.Equal(-7, linearBrush.CoordinateTransform.M42);
    }

    [Fact]
    public void AdaptNativeBrushCountsNonInvertiblePortableFixtureBrushTransform()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(10, 0),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 255), 1))
        {
            MappingMode = "Absolute",
            Transform = new FakeMatrixTransform(new FakeMatrix(0, 0, 0, 1, 0, 0))
        };

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 100, 100),
            out var unsupportedStateCount);

        Assert.Equal(1, unsupportedStateCount);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(Matrix4x4.Identity, linearBrush.CoordinateTransform);
    }

    [Fact]
    public void AdaptNativePenUsesPortableSolidBrushAndPen()
    {
        var pen = new FakePortablePen(
            PortableBrush.SolidColor(new PortableColor(128, 10, 20, 30), opacity: 0.5),
            thickness: 4,
            startLineCap: PortablePenLineCap.Square,
            endLineCap: PortablePenLineCap.Round,
            dashCap: PortablePenLineCap.Triangle,
            lineJoin: PortablePenLineJoin.Bevel,
            miterLimit: 2.0,
            dashArray: new[] { 2.0, 3.0 },
            dashOffset: 1.5);

        var nativePen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.NotNull(nativePen);
        Assert.Equal(4, nativePen!.Thickness);
        Assert.Equal(ProGPU.Vector.PenLineCap.Square, nativePen.StartLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Round, nativePen.EndLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Triangle, nativePen.DashCap);
        Assert.Equal(ProGPU.Vector.PenLineJoin.Bevel, nativePen.LineJoin);
        Assert.Equal(2.0f, nativePen.MiterLimit);
        Assert.Equal(new[] { 2.0, 3.0 }, nativePen.DashArray);
        Assert.Equal(1.5, nativePen.DashOffset);
        var nativeBrush = Assert.IsType<ProGPU.Vector.SolidColorBrush>(nativePen.Brush);
        Assert.Equal(10 / 255f, nativeBrush.Color.X, precision: 6);
        Assert.Equal(20 / 255f, nativeBrush.Color.Y, precision: 6);
        Assert.Equal(30 / 255f, nativeBrush.Color.Z, precision: 6);
        Assert.Equal(64 / 255f, nativeBrush.Color.W, precision: 6);
    }

    [Fact]
    public void AdaptNativeBrushCachesSolidColorBrushUntilStateChanges()
    {
        var brush = new SolidColorBrush(Color.FromArgb(128, 10, 20, 30))
        {
            Opacity = 0.5
        };

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);
        var cachedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(100, 100, 20, 20),
            out var cachedUnsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, cachedUnsupportedStateCount);
        Assert.Same(nativeBrush, cachedBrush);
        var solidBrush = Assert.IsType<ProGPU.Vector.SolidColorBrush>(nativeBrush);
        Assert.Equal(10 / 255f, solidBrush.Color.X, precision: 6);
        Assert.Equal(20 / 255f, solidBrush.Color.Y, precision: 6);
        Assert.Equal(30 / 255f, solidBrush.Color.Z, precision: 6);
        Assert.Equal(64 / 255f, solidBrush.Color.W, precision: 6);

        brush.Color = Color.FromArgb(255, 40, 50, 60);
        brush.Opacity = 1.0;
        var refreshedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var refreshedUnsupportedStateCount);

        Assert.Equal(0, refreshedUnsupportedStateCount);
        Assert.NotSame(nativeBrush, refreshedBrush);
        var refreshedSolidBrush = Assert.IsType<ProGPU.Vector.SolidColorBrush>(refreshedBrush);
        Assert.Equal(40 / 255f, refreshedSolidBrush.Color.X, precision: 6);
        Assert.Equal(1f, refreshedSolidBrush.Color.W, precision: 6);
    }

    [Fact]
    public void AdaptNativeBrushCachesAbsoluteLinearGradientBrushUntilStateChanges()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(1, 2),
            EndPoint = new Point(9, 10),
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Repeat,
            ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation,
            Opacity = 0.75
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 10, 20, 30), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(128, 40, 50, 60), 1));

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);
        var cachedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(100, 100, 20, 20),
            out var cachedUnsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, cachedUnsupportedStateCount);
        Assert.Same(nativeBrush, cachedBrush);
        var linearBrush = Assert.IsType<ProGpuLinearGradientBrush>(nativeBrush);
        Assert.Equal(1, linearBrush.StartPoint.X);
        Assert.Equal(10, linearBrush.EndPoint.Y);
        Assert.Equal(0.75f, linearBrush.Opacity, precision: 6);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Repeat, linearBrush.SpreadMethod);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, linearBrush.ColorInterpolationMode);
        Assert.Equal(2, linearBrush.Stops.Length);
        Assert.Equal(10 / 255f, linearBrush.Stops[0].Color.X, precision: 6);
        Assert.Equal(128 / 255f, linearBrush.Stops[1].Color.W, precision: 6);

        brush.GradientStops[0].Color = Color.FromArgb(255, 70, 80, 90);
        var refreshedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var refreshedUnsupportedStateCount);

        Assert.Equal(0, refreshedUnsupportedStateCount);
        Assert.NotSame(nativeBrush, refreshedBrush);
        var refreshedLinearBrush = Assert.IsType<ProGpuLinearGradientBrush>(refreshedBrush);
        Assert.Equal(70 / 255f, refreshedLinearBrush.Stops[0].Color.X, precision: 6);
    }

    [Fact]
    public void AdaptNativeBrushCachesRelativeLinearGradientStopsAcrossMappedBounds()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        brush.GradientStops.Add(new GradientStop(Colors.Red, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var firstBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(10, 20, 100, 50),
            out var firstUnsupportedStateCount);
        var secondBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(30, 40, 200, 60),
            out var secondUnsupportedStateCount);

        Assert.Equal(0, firstUnsupportedStateCount);
        Assert.Equal(0, secondUnsupportedStateCount);
        var firstLinearBrush = Assert.IsType<ProGpuLinearGradientBrush>(firstBrush);
        var secondLinearBrush = Assert.IsType<ProGpuLinearGradientBrush>(secondBrush);
        Assert.NotSame(firstLinearBrush, secondLinearBrush);
        Assert.Same(firstLinearBrush.Stops, secondLinearBrush.Stops);
        Assert.Equal(10, firstLinearBrush.StartPoint.X);
        Assert.Equal(20, firstLinearBrush.StartPoint.Y);
        Assert.Equal(230, secondLinearBrush.EndPoint.X);
        Assert.Equal(100, secondLinearBrush.EndPoint.Y);

        brush.GradientStops[1].Offset = 0.5;
        var refreshedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(10, 20, 100, 50),
            out var refreshedUnsupportedStateCount);

        Assert.Equal(0, refreshedUnsupportedStateCount);
        var refreshedLinearBrush = Assert.IsType<ProGpuLinearGradientBrush>(refreshedBrush);
        Assert.NotSame(firstLinearBrush.Stops, refreshedLinearBrush.Stops);
        Assert.Equal(0.5f, refreshedLinearBrush.Stops[1].Offset, precision: 6);
    }

    [Fact]
    public void AdaptNativeBrushCachesAbsoluteRadialGradientBrushUntilStateChanges()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(4, 5),
            GradientOrigin = new Point(6, 7),
            RadiusX = 8,
            RadiusY = 9,
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Reflect
        };
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1));

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);
        var cachedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(100, 100, 20, 20),
            out var cachedUnsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, cachedUnsupportedStateCount);
        Assert.Same(nativeBrush, cachedBrush);
        var radialBrush = Assert.IsType<ProGpuRadialGradientBrush>(nativeBrush);
        Assert.Equal(4, radialBrush.Center.X);
        Assert.Equal(7, radialBrush.GradientOrigin.Y);
        Assert.Equal(8, radialBrush.RadiusX);
        Assert.Equal(9, radialBrush.RadiusY);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Reflect, radialBrush.SpreadMethod);

        brush.RadiusX = 10;
        var refreshedBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var refreshedUnsupportedStateCount);

        Assert.Equal(0, refreshedUnsupportedStateCount);
        Assert.NotSame(nativeBrush, refreshedBrush);
        Assert.Equal(10, Assert.IsType<ProGpuRadialGradientBrush>(refreshedBrush).RadiusX);
    }

    [Fact]
    public void AdaptNativePenCachesSimpleSolidPenUntilStateChanges()
    {
        var brush = new SolidColorBrush(Color.FromArgb(255, 1, 2, 3));
        var pen = new MediaPen(brush, 2)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Triangle,
            LineJoin = PenLineJoin.Bevel,
            MiterLimit = 3
        };

        var nativePen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);
        var cachedPen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(100, 100, 20, 20),
            out var cachedUnsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, cachedUnsupportedStateCount);
        Assert.Same(nativePen, cachedPen);
        Assert.NotNull(nativePen);
        Assert.Equal(2, nativePen!.Thickness);
        Assert.Equal(ProGPU.Vector.PenLineCap.Square, nativePen.StartLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Round, nativePen.EndLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Triangle, nativePen.DashCap);
        Assert.Equal(ProGPU.Vector.PenLineJoin.Bevel, nativePen.LineJoin);
        Assert.Equal(3, nativePen.MiterLimit);

        pen.Thickness = 4;
        var refreshedPen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var refreshedUnsupportedStateCount);

        Assert.Equal(0, refreshedUnsupportedStateCount);
        Assert.NotSame(nativePen, refreshedPen);
        Assert.Equal(4, refreshedPen!.Thickness);

        brush.Color = Color.FromArgb(255, 10, 20, 30);
        var brushRefreshedPen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var brushRefreshedUnsupportedStateCount);

        Assert.Equal(0, brushRefreshedUnsupportedStateCount);
        Assert.NotSame(refreshedPen, brushRefreshedPen);
        var nativeBrush = Assert.IsType<ProGPU.Vector.SolidColorBrush>(brushRefreshedPen!.Brush);
        Assert.Equal(10 / 255f, nativeBrush.Color.X, precision: 6);
    }

    [Fact]
    public void AdaptNativePenCachesDashedSolidPenUntilDashStateChanges()
    {
        var brush = new SolidColorBrush(Color.FromArgb(255, 1, 2, 3));
        var dashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0.5);
        var pen = new MediaPen(brush, 3)
        {
            DashStyle = dashStyle,
            DashCap = PenLineCap.Round
        };

        var nativePen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);
        var cachedPen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(100, 100, 20, 20),
            out var cachedUnsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, cachedUnsupportedStateCount);
        Assert.Same(nativePen, cachedPen);
        Assert.NotNull(nativePen);
        Assert.Equal(3, nativePen!.Thickness);
        Assert.Equal(ProGPU.Vector.PenLineCap.Round, nativePen.DashCap);
        Assert.Equal(new[] { 1.0, 2.0 }, nativePen.DashArray);
        Assert.Equal(0.5, nativePen.DashOffset);

        dashStyle.Offset = 1.25;
        var offsetRefreshedPen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var offsetUnsupportedStateCount);

        Assert.Equal(0, offsetUnsupportedStateCount);
        Assert.NotSame(nativePen, offsetRefreshedPen);
        Assert.Equal(new[] { 1.0, 2.0 }, offsetRefreshedPen!.DashArray);
        Assert.Equal(1.25, offsetRefreshedPen.DashOffset);

        dashStyle.Dashes = new[] { 1.0, 3.0 };
        var dashRefreshedPen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var dashUnsupportedStateCount);

        Assert.Equal(0, dashUnsupportedStateCount);
        Assert.NotSame(offsetRefreshedPen, dashRefreshedPen);
        Assert.Equal(new[] { 1.0, 3.0 }, dashRefreshedPen!.DashArray);
        Assert.Equal(1.25, dashRefreshedPen.DashOffset);
    }

    [Fact]
    public void PortableBrushSourceAbsenceDoesNotFallBackToReflectedBrushShape()
    {
        var brush = new FakeUnavailablePortableSolidColorBrush();

        var mediaBrush = WpfResourceResolver.AdaptBrush(brush);
        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);

        Assert.Null(mediaBrush);
        Assert.Null(nativeBrush);
        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, brush.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void PortablePenSourceAbsenceDoesNotFallBackToReflectedPenShape()
    {
        var pen = new FakeUnavailablePortablePen();

        var mediaPen = WpfResourceResolver.AdaptPen(pen);
        var nativePen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(0, 0, 10, 10),
            out var unsupportedStateCount);

        Assert.Null(mediaPen);
        Assert.Null(nativePen);
        Assert.Equal(0, unsupportedStateCount);
        Assert.Equal(0, pen.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void AdaptNativeBrushDoesNotInvokeShimOnlyMediaBrushToNative()
    {
        var brush = new DirectNativeBrush();

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(1, 2, 30, 40),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Null(nativeBrush);
        Assert.Equal(0, brush.BoundsCallCount);
        Assert.Equal(0, brush.ParameterlessCallCount);
    }

    [Fact]
    public void AdaptBrushReturnsTypedLinearGradientShimWithPortableTransformContract()
    {
        var portableBrush = PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 1),
            new[]
            {
                new PortableGradientStop(new PortableColor(255, 255, 0, 0), 0),
                new PortableGradientStop(new PortableColor(255, 0, 0, 255), 1)
            },
            opacity: 0.5,
            mappingMode: PortableBrushMappingMode.Absolute,
            spreadMethod: PortableGradientSpreadMethod.Repeat,
            colorInterpolationMode: PortableGradientColorInterpolationMode.ScRgbLinearInterpolation,
            hasTransform: true,
            transform: new PortableMatrix3x2(1, 0, 0, 1, 5, 7),
            hasRelativeTransform: true,
            relativeTransform: new PortableMatrix3x2(2, 0, 0, 3, 0, 0));
        var mediaBrush = Assert.IsType<LinearGradientBrush>(
            WpfResourceResolver.AdaptBrush(new FakePortableBrush(portableBrush)));
        var portableSource = Assert.IsAssignableFrom<IPortableBrushSource>(mediaBrush);

        Assert.True(portableSource.TryGetPortableBrush(out var roundTrip));
        Assert.Equal(PortableBrushKind.LinearGradient, roundTrip.Kind);
        Assert.Equal(PortableBrushMappingMode.Absolute, roundTrip.MappingMode);
        Assert.Equal(PortableGradientSpreadMethod.Repeat, roundTrip.SpreadMethod);
        Assert.Equal(PortableGradientColorInterpolationMode.ScRgbLinearInterpolation, roundTrip.ColorInterpolationMode);
        Assert.Equal(0.5, roundTrip.Opacity);
        Assert.True(roundTrip.HasTransform);
        Assert.Equal(5, roundTrip.Transform.OffsetX);
        Assert.Equal(7, roundTrip.Transform.OffsetY);
        Assert.True(roundTrip.HasRelativeTransform);
        Assert.Equal(2, roundTrip.RelativeTransform.M11);
        Assert.Equal(3, roundTrip.RelativeTransform.M22);
        Assert.Equal(2, roundTrip.GradientStops.Length);
    }

    [Fact]
    public void AdaptNativeBrushDoesNotInvokeDuckTypedToNativeMethods()
    {
        var brush = new DuckTypedNativeBrush();

        var nativeBrush = WpfResourceResolver.AdaptNativeBrush(
            brush,
            new WpfReplayRect(1, 2, 30, 40),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Null(nativeBrush);
        Assert.Equal(0, brush.ToNativeCallCount);
    }

    [Fact]
    public void AdaptNativePenDoesNotInvokeDuckTypedToNativeMethod()
    {
        var pen = new DuckTypedNativePen();

        var nativePen = WpfResourceResolver.AdaptNativePen(
            pen,
            new WpfReplayRect(1, 2, 30, 40),
            out var unsupportedStateCount);

        Assert.Equal(0, unsupportedStateCount);
        Assert.Null(nativePen);
        Assert.Equal(0, pen.ToNativeCallCount);
    }

    [Fact]
    public void DecodeRectanglePreservesPositivePortablePenDashStyleMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            DashStyle = new FakeDashStyle(new[] { 2.0, 3.0 }, 1.5)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.NotNull(adaptedPen.DashStyle);
        Assert.Equal(new[] { 2.0, 3.0 }, adaptedPen.DashStyle!.Dashes);
        Assert.Equal(1.5, adaptedPen.DashStyle.Offset);
    }

    [Fact]
    public void DecodeRectanglePreservesZeroLengthPortablePenDotDashMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            DashStyle = new FakeDashStyle(new[] { 0.0, 2.0 }, 0)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.NotNull(adaptedPen.DashStyle);
        Assert.Equal(new[] { 0.0, 2.0 }, adaptedPen.DashStyle!.Dashes);
    }

    [Fact]
    public void DecodeRectanglePreservesPortablePenLineCapMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            StartLineCap = "Square",
            EndLineCap = "Round",
            DashCap = "Round"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.Equal(PenLineCap.Square, adaptedPen.StartLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.EndLineCap);
        Assert.Equal(PenLineCap.Round, adaptedPen.DashCap);
    }

    [Fact]
    public void DecodeRectanglePreservesPortablePenLineJoinAndMiterMetadata()
    {
        var pen = new FakePen(
            new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)),
            4)
        {
            LineJoin = "Round",
            MiterLimit = 3.5
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.Red, pen });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        WriteUInt32(payload, 36, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedPen = Assert.IsType<MediaPen>(sink.DrawRectangles[0].Pen);
        Assert.Equal(PenLineJoin.Round, adaptedPen.LineJoin);
        Assert.Equal(3.5, adaptedPen.MiterLimit);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableLinearGradientBrushFixture()
    {
        var brush = new FakeLinearGradientBrush(
            new FakePoint(0, 0),
            new FakePoint(1, 1),
            new FakeGradientStop(new FakeColor(255, 255, 0, 0), 0),
            new FakeGradientStop(new FakeColor(128, 0, 0, 255), 1))
        {
            Opacity = 0.75,
            SpreadMethod = "Repeat",
            ColorInterpolationMode = "ScRgbLinearInterpolation"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativeBrush = Assert.IsType<ProGpuLinearGradientBrush>(
            ToNative(Assert.Single(sink.DrawRectangles).Brush!, new WpfReplayRect(1, 2, 30, 40)));
        Assert.Equal(1, nativeBrush.StartPoint.X);
        Assert.Equal(2, nativeBrush.StartPoint.Y);
        Assert.Equal(31, nativeBrush.EndPoint.X);
        Assert.Equal(42, nativeBrush.EndPoint.Y);
        Assert.Equal(0.75f, nativeBrush.Opacity);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Repeat, nativeBrush.SpreadMethod);
        Assert.Equal(ProGPU.Vector.GradientColorInterpolationMode.ScRgbLinearInterpolation, nativeBrush.ColorInterpolationMode);
        Assert.Equal(2, nativeBrush.Stops.Length);
        Assert.Equal(0f, nativeBrush.Stops[0].Offset);
        Assert.Equal(1f, nativeBrush.Stops[1].Offset);
        Assert.Equal(1f, nativeBrush.Stops[0].Color.X);
        Assert.Equal(0.5f, nativeBrush.Stops[1].Color.W, precision: 2);
    }

    [Fact]
    public void DecodeRectangleAdaptsPortableRadialGradientBrushFixture()
    {
        var brush = new FakeRadialGradientBrush(
            new FakePoint(0.5, 0.5),
            new FakePoint(0.25, 0.75),
            radiusX: 0.25,
            radiusY: 0.5,
            new FakeGradientStop(new FakeColor(255, 0, 255, 0), 0),
            new FakeGradientStop(new FakeColor(255, 0, 0, 0), 1))
        {
            SpreadMethod = "Reflect"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush });
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangle, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var nativeBrush = Assert.IsType<ProGpuRadialGradientBrush>(
            ToNative(Assert.Single(sink.DrawRectangles).Brush!, new WpfReplayRect(1, 2, 30, 40)));
        Assert.Equal(16, nativeBrush.Center.X);
        Assert.Equal(22, nativeBrush.Center.Y);
        Assert.Equal(8.5f, nativeBrush.GradientOrigin.X);
        Assert.Equal(32, nativeBrush.GradientOrigin.Y);
        Assert.Equal(7.5f, nativeBrush.RadiusX);
        Assert.Equal(20, nativeBrush.RadiusY);
        Assert.Equal(20, nativeBrush.Radius);
        Assert.Equal(ProGPU.Vector.GradientSpreadMethod.Reflect, nativeBrush.SpreadMethod);
        Assert.Equal(2, nativeBrush.Stops.Length);
    }

    [Fact]
    public void DecodePushTransformAdaptsPortableMatrixTransformContract()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 2, 3, 4, 10, 20));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform });
        var sink = new TestSink();

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        var adaptedTransform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, adaptedTransform.Matrix.M11);
        Assert.Equal(2, adaptedTransform.Matrix.M12);
        Assert.Equal(3, adaptedTransform.Matrix.M21);
        Assert.Equal(4, adaptedTransform.Matrix.M22);
        Assert.Equal(10, adaptedTransform.Matrix.OffsetX);
        Assert.Equal(20, adaptedTransform.Matrix.OffsetY);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodePushTransformAdaptsPortableTransformMatrixSource()
    {
        var transform = new FakePortableTransform(new PortableMatrix3x2(1, 2, 3, 4, 10, 20));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform });
        var sink = new TestSink();

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        var adaptedTransform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, adaptedTransform.Matrix.M11);
        Assert.Equal(2, adaptedTransform.Matrix.M12);
        Assert.Equal(3, adaptedTransform.Matrix.M21);
        Assert.Equal(4, adaptedTransform.Matrix.M22);
        Assert.Equal(10, adaptedTransform.Matrix.OffsetX);
        Assert.Equal(20, adaptedTransform.Matrix.OffsetY);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void PortableTransformSourceAbsenceDoesNotFallBackToReflectedTransformShape()
    {
        var transform = new FakeUnavailablePortableMatrixTransform(new FakeMatrix(1, 2, 3, 4, 10, 20));

        var mediaTransform = WpfResourceResolver.AdaptTransform(transform);
        var hasNativeMatrix = WpfResourceResolver.TryAdaptTransformMatrix(transform, out _);

        Assert.Null(mediaTransform);
        Assert.False(hasNativeMatrix);
        Assert.Equal(0, transform.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void DecodePushTransformRejectsReflectedMatrixShapeWithoutPortableContract()
    {
        var transform = new FakeReflectedMatrixTransform(new FakeMatrix(1, 0, 0, 1, 6, 7));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { transform });
        var sink = new TestSink();

        var pushPayload = new byte[8];
        WriteUInt32(pushPayload, 0, 1);
        var renderData = CreateRecord(WpfMilCommandId.PushTransform, pushPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 0, 2, 0), result);
        Assert.Empty(sink.Transforms);
        Assert.Equal(0, sink.PopCount);
        Assert.Equal(0, transform.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void DecodeGeometryAdaptsPortableRectangleGeometry()
    {
        var brush = new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60));
        var geometry = new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush, geometry });
        var sink = new TestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.DrawRectangles);
        Assert.NotNull(draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new Rect(5, 6, 70, 80), draw.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysPortableGeometryDrawing()
    {
        var drawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 100, 110, 120)),
            null,
            new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var replayed = Assert.Single(sink.DrawGeometries);
        Assert.IsType<SolidColorBrush>(replayed.Brush);
        Assert.IsType<PathGeometry>(replayed.Geometry);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushGeometryDrawing()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewport = new FakeRect(0.25, 0.125, 0.5, 0.5),
                Opacity = 0.5
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(35, 30, 50, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysPortableImageTileBrushWithoutReflectedTypeName()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var tileBrush = new PortableTileBrush(
            PortableTileBrushKind.Image,
            imageSource,
            opacity: 0.5,
            viewport: new PortableRect(0.25, 0.125, 0.5, 0.5),
            viewbox: new PortableRect(0, 0, 1, 1),
            viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            tileMode: PortableTileMode.None,
            stretch: PortableStretch.Fill,
            alignmentX: PortableAlignmentX.Center,
            alignmentY: PortableAlignmentY.Center,
            hasTransform: false,
            transform: PortableMatrix3x2.Identity,
            hasRelativeTransform: false,
            relativeTransform: PortableMatrix3x2.Identity);
        var drawing = new FakeGeometryDrawing(
            new FakePortableTileBrushSource(tileBrush),
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(35, 30, 50, 40), replayed.Rectangle);
    }

    [Fact]
    public void PortableTileBrushSourceAbsenceDoesNotFallBackToReflectedImageBrushShape()
    {
        var brush = new FakeUnavailablePortableImageBrush(new FakeBitmapSource());
        var sink = new TestSink();

        var replayed = WpfDrawingReplay.TryReplayTileBrushFill(
            brush,
            new RectangleGeometry(new Rect(0, 0, 10, 10)),
            sink,
            imageSourceAdapter: _ => new FakeImageSource(),
            out var status);

        Assert.False(replayed);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
        Assert.Empty(sink.Operations);
        Assert.Equal(0, brush.ReflectedPropertyProbeCount);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithAbsoluteViewport()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewport = new FakeRect(12, 24, 30, 40),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(12, 24, 30, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithTransform()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(7, transform.Matrix.OffsetX);
        Assert.Equal(9, transform.Matrix.OffsetY);
        Assert.Equal(new Rect(10, 20, 100, 80), Assert.Single(sink.Images).Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushUniformStretch()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Stretch = "Uniform"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(0, 25, 100, 50), Assert.Single(sink.Images).Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushUniformStretchWithLeftBottomAlignment()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Stretch = "Uniform",
                AlignmentX = "Left",
                AlignmentY = "Bottom"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(0, 50, 100, 50), Assert.Single(sink.Images).Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithRelativeViewbox()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewbox = new FakeRect(0.25, 0.2, 0.5, 0.4)
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.SourceImages);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(10, 20, 100, 80), replayed.Rectangle);
        Assert.Equal(new Rect(50, 20, 100, 40), replayed.SourceRectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushWithAbsoluteViewbox()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                Viewbox = new FakeRect(12, 16, 32, 40),
                ViewboxUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        var replayed = Assert.Single(sink.SourceImages);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(10, 20, 100, 80), replayed.Rectangle);
        Assert.Equal(new Rect(12, 16, 32, 40), replayed.SourceRectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushTileMode()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushFlipXTiling()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "FlipX",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
        var flipTransform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(-1, flipTransform.Matrix.M11);
        Assert.Equal(1, flipTransform.Matrix.M22);
        Assert.Equal(30, flipTransform.Matrix.OffsetX);
        Assert.Equal(0, flipTransform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushTileModeWithTransform()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.OffsetX);
        Assert.Equal(4, transform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageBrushTileModeWithRelativeTransform()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10), new Rect(20, 0, 10, 10) }, sink.Images.Select(image => image.Rectangle).ToArray());
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, transform.Matrix.M11);
        Assert.Equal(1, transform.Matrix.M22);
        Assert.Equal(12.5, transform.Matrix.OffsetX);
        Assert.Equal(2.5, transform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingCountsUnsupportedImageBrushUnadaptableRelativeTransformButReplaysStroke()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeGeometryDrawing(
            new FakeImageBrush(imageSource)
            {
                TileMode = "FlipX",
                RelativeTransform = new object()
            },
            new FakePen(new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3)), 2),
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Null(imageAdapter.LastImageSource);
        Assert.Empty(sink.Images);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        var replayed = Assert.Single(sink.DrawGeometries);
        Assert.Null(replayed.Brush);
        Assert.NotNull(replayed.Pen);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushGeometryDrawing()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Viewport = new FakeRect(0.25, 0.125, 0.5, 0.5),
                Opacity = 0.5
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(5, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(35, transform.Matrix.OffsetX);
        Assert.Equal(30, transform.Matrix.OffsetY);
        var replayed = Assert.Single(sink.DrawGeometries);
        Assert.IsType<SolidColorBrush>(replayed.Brush);
        Assert.Null(replayed.Pen);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushWithAbsoluteViewport()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Viewport = new FakeRect(12, 24, 30, 40),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(12, transform.Matrix.OffsetX);
        Assert.Equal(24, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushUniformToFillStretch()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Stretch = "UniformToFill"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-50, transform.Matrix.OffsetX);
        Assert.Equal(0, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushUniformToFillStretchWithRightBottomAlignment()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 50)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Stretch = "UniformToFill",
                AlignmentX = "Right",
                AlignmentY = "Bottom"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-100, transform.Matrix.OffsetX);
        Assert.Equal(0, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushWithAbsoluteViewbox()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Viewbox = new FakeRect(25, 20, 50, 40),
                ViewboxUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushClip", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-40, transform.Matrix.OffsetX);
        Assert.Equal(-20, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushTileMode()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(
            new[]
            {
                "PushClip",
                "PushTransform",
                "DrawGeometry",
                "Pop",
                "PushTransform",
                "DrawGeometry",
                "Pop",
                "PushTransform",
                "DrawGeometry",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
        Assert.Equal(3, sink.DrawGeometries.Count);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushFlipXYTiling()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "FlipXY",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(4, sink.DrawGeometries.Count);
        var matrices = sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix).ToArray();
        Assert.Equal(new[] { 1d, -1d, 1d, -1d }, matrices.Select(matrix => matrix.M11).ToArray());
        Assert.Equal(new[] { 1d, 1d, -1d, -1d }, matrices.Select(matrix => matrix.M22).ToArray());
        Assert.Equal(new[] { 0d, 20d, 0d, 20d }, matrices.Select(matrix => matrix.OffsetX).ToArray());
        Assert.Equal(new[] { 0d, 0d, 20d, 20d }, matrices.Select(matrix => matrix.OffsetY).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushTileModeWithTransform()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawGeometries.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushTileModeWithRelativeTransform()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawGeometries.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var relativeTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(12.5, relativeTransform.Matrix.OffsetX);
        Assert.Equal(2.5, relativeTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingBrushWithTransform()
    {
        var nestedDrawing = new FakeGeometryDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)));
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedDrawing)
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushTransform", "DrawGeometry", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        var contentTransform = Assert.IsType<MatrixTransform>(sink.Transforms[1]);
        Assert.Equal(10, contentTransform.Matrix.M11);
        Assert.Equal(8, contentTransform.Matrix.M22);
        Assert.Equal(10, contentTransform.Matrix.OffsetX);
        Assert.Equal(20, contentTransform.Matrix.OffsetY);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingCountsPartiallyReplayedDrawingBrushContentAsUnsupported()
    {
        var nestedGroup = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 40, 50, 60)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10))),
            new object());
        var drawing = new FakeGeometryDrawing(
            new FakeDrawingBrush(nestedGroup),
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Single(sink.Transforms);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushGeometryDrawing()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Viewport = new FakeRect(0.25, 0.125, 0.5, 0.5),
                Opacity = 0.5
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushOpacity", "PushTransform", "DrawRectangle", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(5, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(35, transform.Matrix.OffsetX);
        Assert.Equal(30, transform.Matrix.OffsetY);
        var replayed = Assert.Single(sink.DrawRectangles);
        Assert.Same(Brushes.Green, replayed.Brush);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushWithAbsoluteViewport()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Viewport = new FakeRect(12, 24, 30, 40),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.M11);
        Assert.Equal(4, transform.Matrix.M22);
        Assert.Equal(12, transform.Matrix.OffsetX);
        Assert.Equal(24, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushNoneStretch()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Stretch = "None"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, transform.Matrix.M11);
        Assert.Equal(1, transform.Matrix.M22);
        Assert.Equal(45, transform.Matrix.OffsetX);
        Assert.Equal(35, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushNoneStretchWithRightBottomAlignment()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Stretch = "None",
                AlignmentX = "Right",
                AlignmentY = "Bottom"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(1, transform.Matrix.M11);
        Assert.Equal(1, transform.Matrix.M22);
        Assert.Equal(90, transform.Matrix.OffsetX);
        Assert.Equal(70, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushWithRelativeViewbox()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 100, 100)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Viewbox = new FakeRect(0.25, 0.2, 0.5, 0.4)
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushClip", "DrawRectangle", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Clips.Count);
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(2, transform.Matrix.M11);
        Assert.Equal(2, transform.Matrix.M22);
        Assert.Equal(-40, transform.Matrix.OffsetX);
        Assert.Equal(-20, transform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushTileMode()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(
            new[]
            {
                "PushClip",
                "PushTransform",
                "DrawRectangle",
                "Pop",
                "PushTransform",
                "DrawRectangle",
                "Pop",
                "PushTransform",
                "DrawRectangle",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
        Assert.Equal(3, sink.DrawRectangles.Count);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushFlipXYTiling()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "FlipXY",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute"
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(4, sink.DrawRectangles.Count);
        var matrices = sink.Transforms.Select(transform => ((MatrixTransform)transform).Matrix).ToArray();
        Assert.Equal(new[] { 1d, -1d, 1d, -1d }, matrices.Select(matrix => matrix.M11).ToArray());
        Assert.Equal(new[] { 1d, 1d, -1d, -1d }, matrices.Select(matrix => matrix.M22).ToArray());
        Assert.Equal(new[] { 0d, 20d, 0d, 20d }, matrices.Select(matrix => matrix.OffsetX).ToArray());
        Assert.Equal(new[] { 0d, 0d, 20d, 20d }, matrices.Select(matrix => matrix.OffsetY).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushTileModeWithTransform()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawRectangles.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushTileModeWithRelativeTransform()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                TileMode = "Tile",
                Viewport = new FakeRect(0, 0, 10, 10),
                ViewportUnits = "Absolute",
                RelativeTransform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 0.5, 0.25))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(0, 0, 25, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(3, sink.DrawRectangles.Count);
        Assert.Equal(4, sink.Transforms.Count);
        var relativeTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(12.5, relativeTransform.Matrix.OffsetX);
        Assert.Equal(2.5, relativeTransform.Matrix.OffsetY);
        Assert.Equal(new[] { 0d, 10d, 20d }, sink.Transforms.Skip(1).Select(transform => ((MatrixTransform)transform).Matrix.OffsetX).ToArray());
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedVisualBrushWithTransform()
    {
        var visual = new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green))
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual)
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9))
            },
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "PushTransform", "DrawRectangle", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(2, sink.Transforms.Count);
        var brushTransform = Assert.IsType<MatrixTransform>(sink.Transforms[0]);
        Assert.Equal(7, brushTransform.Matrix.OffsetX);
        Assert.Equal(9, brushTransform.Matrix.OffsetY);
        var contentTransform = Assert.IsType<MatrixTransform>(sink.Transforms[1]);
        Assert.Equal(10, contentTransform.Matrix.M11);
        Assert.Equal(8, contentTransform.Matrix.M22);
        Assert.Equal(10, contentTransform.Matrix.OffsetX);
        Assert.Equal(20, contentTransform.Matrix.OffsetY);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingCountsPartiallyReplayedVisualBrushContentAsUnsupported()
    {
        var visual = new FakeDrawingVisual(new object())
        {
            Bounds = new FakeRect(0, 0, 10, 10)
        };
        visual.Children.Add(new FakeDrawingVisual(CreateRectangleRenderData(Brushes.Green)));
        var drawing = new FakeGeometryDrawing(
            new FakeVisualBrush(visual),
            null,
            new FakeRectangleGeometry(new FakeRect(10, 20, 100, 80)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "PushClip", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.Clips);
        Assert.Single(sink.Transforms);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingGroupStateAndChildren()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            Opacity = 0.5
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTransform", "PushOpacity", "DrawGeometry", "Pop", "Pop" }, sink.Operations);
        Assert.Single(sink.DrawGeometries);
        Assert.Equal(0.5, Assert.Single(sink.Opacities));
        var transform = Assert.IsType<MatrixTransform>(Assert.Single(sink.Transforms));
        Assert.Equal(3, transform.Matrix.OffsetX);
        Assert.Equal(4, transform.Matrix.OffsetY);
    }

    [Fact]
    public void DecodeDrawDrawingCountsPartiallyReplayedDrawingGroupAsAppliedAndUnsupported()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))),
            new object());
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Single(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedDrawingGroupOpacityMask()
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
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushOpacityMask", "DrawGeometry", "Pop" }, sink.Operations);
        var replayedMask = Assert.Single(sink.OpacityMasks);
        Assert.IsType<SolidColorBrush>(replayedMask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 30, 40), replayedMask.Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingInfersDrawingGroupOpacityMaskBoundsFromChildren()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))),
            new FakeImageDrawing(new FakeBitmapSource(), new FakeRect(20, 30, 40, 50)))
        {
            OpacityMask = new FakeSolidColorBrush(new FakeColor(128, 255, 255, 255))
        };
        var imageAdapter = new FakeImageSourceAdapter();
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushOpacityMask", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(2, 3, 58, 77), Assert.Single(sink.OpacityMasks).Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingIntersectsInferredDrawingGroupOpacityMaskBoundsWithClip()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 100, 100))))
        {
            ClipGeometry = new FakeRectangleGeometry(new FakeRect(10, 20, 30, 40)),
            OpacityMask = new FakeSolidColorBrush(new FakeColor(128, 255, 255, 255))
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new Rect(10, 20, 30, 40), Assert.Single(sink.OpacityMasks).Bounds);
    }

    [Fact]
    public void DecodeDrawDrawingPushesNativeDrawingGroupCacheWhenSinkSupportsDrawingCaches()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))))
        {
            Bounds = new FakeRect(1, 2, 30, 40),
            CacheMode = new object()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink { AcceptDrawingCaches = true };

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushDrawingCache", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(1, 2, 30, 40), Assert.Single(sink.DrawingCacheBounds));
    }

    [Fact]
    public void DecodeDrawDrawingReportsUnsupportedDrawingGroupCacheAsPartialWhenSinkCannotCache()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            CacheMode = new object()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.DrawingCacheBounds);
    }

    [Fact]
    public void DecodeDrawDrawingPassesWpfShapedDrawingGroupGuidelineSetToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            GuidelineSet = new object()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawGeometry", "Pop" }, sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupBitmapScalingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapScalingMode = "NearestNeighbor"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "NearestNeighbor" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesHighQualityDrawingGroupBitmapScalingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapScalingMode = "HighQuality"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "HighQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingReportsUnsupportedDrawingGroupBitmapScalingModeAsPartial()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapScalingMode = "Supersampled"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.BitmapScalingModes);
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupEdgeModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            EdgeMode = "Aliased"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushEdgeMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupTextRenderingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextRenderingMode = "Aliased"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextRenderingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Aliased" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesDrawingGroupClearTypeTextRenderingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextRenderingMode = "ClearType"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextRenderingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesDrawingGroupClearTypeHintToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            ClearTypeHint = "Enabled"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextRenderingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingPassesSupportedDrawingGroupTextHintingModeToSink()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextHintingMode = "Animated"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushTextHintingMode", "DrawGeometry", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Animated" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
    }

    [Fact]
    public void DecodeDrawDrawingCountsUnknownDrawingGroupTextHintingModeAsPartial()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            TextHintingMode = "Display"
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.TextHintingModes);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupWhenTransformCannotBeAdapted()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Transform = new object()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupWhenClipCannotBeAdapted()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            ClipGeometry = new object()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupWithUnsupportedEffectInput()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            BitmapEffectInput = new object()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeDrawDrawingPushesNativeDrawingGroupEffectWhenSinkSupportsVisualEffects()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))))
        {
            Bounds = new FakeRect(1, 2, 30, 40),
            Effect = new FakeBlurEffect(8)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink { AcceptVisualEffects = true };

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushVisualEffect", "DrawGeometry", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(8, effect.BlurRadius);
        Assert.Equal(new Rect(1, 2, 30, 40), Assert.Single(sink.VisualEffectBounds));
    }

    [Fact]
    public void DecodeDrawDrawingPushesNativeDrawingGroupBitmapEffectWhenEmulationIsSupported()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))))
        {
            BitmapEffect = new FakeBlurBitmapEffect(6),
            BitmapEffectInput = new FakeContextBitmapEffectInput()
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink { AcceptVisualEffects = true };

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new[] { "PushVisualEffect", "DrawGeometry", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(6, effect.BlurRadius);
        Assert.Equal(new Rect(2, 3, 10, 20), Assert.Single(sink.VisualEffectBounds));
    }

    [Fact]
    public void DecodeDrawDrawingSkipsDrawingGroupEffectWhenSinkCannotApplyVisualEffects()
    {
        var group = new FakeDrawingGroup(
            new FakeGeometryDrawing(
                new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30)),
                null,
                new FakeRectangleGeometry(new FakeRect(0, 0, 10, 20))))
        {
            Effect = new FakeBlurEffect(4)
        };
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { group });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 0, 1), result);
        Assert.Empty(sink.VisualEffects);
        Assert.Empty(sink.DrawGeometries);
    }

    [Fact]
    public void DecodeDrawDrawingSkipsMissingDrawingResourceToken()
    {
        var resolver = WpfResourceResolver.FromDependentResources(Array.Empty<object?>());
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 0, 1, 0), result);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void DecodeImageUsesInjectedImageSourceAdapter()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { imageSource }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawImage, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeImageUsesInjectedImageSourceAdapterForMediaImageWithoutProGpuTexture()
    {
        var imageSource = new FakeImageSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { imageSource }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawImage, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), replayed.Rectangle);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysWpfShapedImageDrawingWithInjectedImageSourceAdapter()
    {
        var imageSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter();
        var drawing = new FakeImageDrawing(imageSource, new FakeRect(3, 4, 50, 60));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing }, imageAdapter);
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(imageSource, imageAdapter.LastImageSource);
        var replayed = Assert.Single(sink.Images);
        Assert.Same(imageAdapter.AdaptedImageSource, replayed.ImageSource);
        Assert.Equal(new Rect(3, 4, 50, 60), replayed.Rectangle);
    }

    [Fact]
    public void DecodeGlyphRunAdaptsPortableGlyphRunWithoutReflectionFallback()
    {
        var brush = new FakeSolidColorBrush(new FakeColor(255, 10, 20, 30));
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 3, 4 },
            AdvanceWidths = new[] { 5.5, 6.5 },
            GlyphOffsets = [new PortablePoint(0, 0), new PortablePoint(1.5, -2)],
            BaselineOrigin = new PortablePoint(10, 20),
            FontRenderingEmSize = 12.5,
            FontFamilyNames = new[] { "Arial" }
        });
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { brush, glyphRun });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGlyphRun, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var replayed = Assert.Single(sink.GlyphRuns);
        Assert.IsType<SolidColorBrush>(replayed.ForegroundBrush);
        Assert.Equal(new ushort[] { 3, 4 }, replayed.GlyphRun.GlyphIndices);
        Assert.Equal(12.5f, replayed.GlyphRun.FontSize);
        Assert.Equal(10, replayed.GlyphRun.Position.X);
        Assert.Equal(20, replayed.GlyphRun.Position.Y);
        Assert.Equal(0, replayed.GlyphRun.GlyphPositions[0].X);
        Assert.Equal(0, replayed.GlyphRun.GlyphPositions[0].Y);
        Assert.Equal(7, replayed.GlyphRun.GlyphPositions[1].X);
        Assert.Equal(-2, replayed.GlyphRun.GlyphPositions[1].Y);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void DecodeGlyphRunPrefersPortableFontUri()
    {
        if (!TryFindLoadableFontPathDifferentFromFallback(out var fontPath, out var expectedFont))
        {
            return;
        }

        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 3 },
            AdvanceWidths = new[] { 5.0 },
            BaselineOrigin = new PortablePoint(0, 0),
            FontRenderingEmSize = 12,
            FontUri = fontPath,
            FontFamilyNames = new[] { "ProGpuMissingFamilyNameForFontUriTest" }
        });

        var adapted = WpfResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.Equal(expectedFont.UnitsPerEm, adapted.Font.UnitsPerEm);
        Assert.Equal(expectedFont.Ascender, adapted.Font.Ascender);
        Assert.Equal(expectedFont.Descender, adapted.Font.Descender);
        Assert.Equal(expectedFont.NumGlyphs, adapted.Font.NumGlyphs);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void DecodeGlyphRunPreservesPortableStyleSimulations()
    {
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 3 },
            AdvanceWidths = new[] { 5.0 },
            BaselineOrigin = new PortablePoint(0, 0),
            FontRenderingEmSize = 12,
            FontFamilyNames = new[] { "Arial" },
            IsBold = true,
            IsItalic = true
        });

        var adapted = WpfResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.True(adapted.IsBold);
        Assert.True(adapted.IsItalic);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Theory]
    [InlineData("Calibri")]
    [InlineData("Segoe UI")]
    [InlineData("Segoe UI, Arial")]
    public void AdaptGlyphRunResolvesCommonWpfFontFamiliesThroughPortableFallbacks(string familyName)
    {
        var glyphRun = new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 3 },
            AdvanceWidths = new[] { 5.0 },
            BaselineOrigin = new PortablePoint(0, 0),
            FontRenderingEmSize = 12,
            FontFamilyNames = new[] { familyName }
        };

        var adapted = WpfResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        var cachedFont = Assert.IsType<TtfFont>(glyphRun.NativeFont);
        Assert.Same(cachedFont, adapted.Font);
    }

    [Fact]
    public void AdaptGlyphRunAdaptsPortableGlyphRunWithoutTypeNameShape()
    {
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 3, 4 },
            GlyphPositions =
            [
                new PortablePoint(2, 3),
                new PortablePoint(7, -1)
            ],
            BaselineOrigin = new PortablePoint(10, 20),
            FontRenderingEmSize = 12.5,
            FontFamilyNames = new[] { "Arial" },
            IsBold = true,
            IsItalic = true
        });

        var adapted = WpfResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.Equal(new ushort[] { 3, 4 }, adapted.GlyphIndices);
        Assert.Equal(12.5f, adapted.FontSize);
        Assert.Equal(new Vector2(10, 20), adapted.Position);
        Assert.Equal(new Vector2(2, 3), adapted.GlyphPositions[0]);
        Assert.Equal(new Vector2(7, -1), adapted.GlyphPositions[1]);
        Assert.True(adapted.IsBold);
        Assert.True(adapted.IsItalic);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void AdaptNativeGlyphRunAdaptsPortableGlyphRunWithoutReflection()
    {
        var glyphRun = new FakePortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 5 },
            AdvanceWidths = new[] { 9.0 },
            GlyphOffsets = [new PortablePoint(1, 2)],
            BaselineOrigin = new PortablePoint(3, 4),
            FontRenderingEmSize = 14,
            FontFamilyNames = new[] { "Arial" },
            HasTransform = true,
            Transform = new PortableMatrix3x2(1, 0, 0, 1, 6, 7)
        });

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var adapted));
        Assert.Equal(new ushort[] { 5 }, adapted.GlyphIndices);
        Assert.Equal(14f, adapted.FontSize);
        Assert.Equal(new Vector2(3, 4), adapted.Position);
        Assert.Equal(new Vector2(1, 2), adapted.GlyphPositions[0]);
        Assert.Equal(6, adapted.Transform.M41);
        Assert.Equal(7, adapted.Transform.M42);
        Assert.True(adapted.HasBounds);
        Assert.Equal(3, adapted.LocalBounds.X);
        Assert.Equal(-10, adapted.LocalBounds.Y);
        Assert.Equal(15, adapted.LocalBounds.Width);
        Assert.Equal(16, adapted.LocalBounds.Height);
        Assert.Equal(9, adapted.TransformedBounds.X);
        Assert.Equal(-3, adapted.TransformedBounds.Y);
        Assert.Equal(15, adapted.TransformedBounds.Width);
        Assert.Equal(16, adapted.TransformedBounds.Height);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void AdaptNativeGlyphRunReusesCachedPortableGlyphRunPositions()
    {
        var glyphRun = new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 5, 6 },
            AdvanceWidths = new[] { 9.0, 4.0 },
            GlyphOffsets = [new PortablePoint(1, 2), new PortablePoint(3, 4)],
            BaselineOrigin = new PortablePoint(3, 4),
            FontRenderingEmSize = 14,
            FontFamilyNames = new[] { "Arial" }
        };

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var first));
        Assert.Equal(new Vector2(1, 2), first.GlyphPositions[0]);
        Assert.Equal(new Vector2(12, 4), first.GlyphPositions[1]);

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var second));
        Assert.Same(first.GlyphPositions, second.GlyphPositions);
        Assert.Same(first.Font, second.Font);

        glyphRun.AdvanceWidths = new[] { 17.0, 4.0 };

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var rebuilt));
        Assert.NotSame(first.GlyphPositions, rebuilt.GlyphPositions);
        Assert.Equal(new Vector2(20, 4), rebuilt.GlyphPositions[1]);
    }

    [Fact]
    public void AdaptNativeGlyphRunPrefersPortableNativeGlyphRunWithoutPortableRoundTrip()
    {
        var positions = new[]
        {
            new Vector2(2, 3),
            new Vector2(7, -1)
        };
        var glyphRun = new FakePortableNativeGlyphRunSource(new PortableNativeGlyphRun
        {
            GlyphIndices = new ushort[] { 3, 4 },
            GlyphPositions = positions,
            BaselineOrigin = new Vector2(10, 20),
            FontRenderingEmSize = 12.5,
            FontFamilyNames = new[] { "Arial" },
            HasTransform = true,
            Transform = Matrix4x4.CreateTranslation(6, 7, 0),
            IsBold = true,
            IsItalic = true
        });

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var adapted));
        Assert.Equal(new ushort[] { 3, 4 }, adapted.GlyphIndices);
        Assert.Same(positions, adapted.GlyphPositions);
        Assert.Equal(12.5f, adapted.FontSize);
        Assert.Equal(new Vector2(10, 20), adapted.Position);
        Assert.Equal(6, adapted.Transform.M41);
        Assert.Equal(7, adapted.Transform.M42);
        Assert.True(adapted.IsBold);
        Assert.True(adapted.IsItalic);
        Assert.Equal(1, glyphRun.PortableNativeGlyphRunProbeCount);
        Assert.Equal(0, glyphRun.PortableGlyphRunProbeCount);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void AdaptNativeGlyphRunReusesCachedPortableNativeGlyphRunWrapper()
    {
        var sourcePositions = new[]
        {
            new Vector2(2, 3),
            new Vector2(7, -1),
            new Vector2(11, 13)
        };
        var glyphRun = new PortableNativeGlyphRun
        {
            GlyphIndices = new ushort[] { 3, 4 },
            GlyphPositions = sourcePositions,
            BaselineOrigin = new Vector2(10, 20),
            FontRenderingEmSize = 12.5,
            FontFamilyNames = new[] { "Arial" }
        };

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var first));
        Assert.NotSame(sourcePositions, first.GlyphPositions);
        Assert.Equal(2, first.GlyphPositions.Length);

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var second));
        Assert.Same(first.GlyphPositions, second.GlyphPositions);
        Assert.Same(first.Font, second.Font);

        glyphRun.GlyphPositions = [new Vector2(19, 23), new Vector2(29, 31)];

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var rebuilt));
        Assert.NotSame(first.GlyphPositions, rebuilt.GlyphPositions);
        Assert.Equal(new Vector2(19, 23), rebuilt.GlyphPositions[0]);
    }

    [Fact]
    public void AdaptNativeGlyphRunAcceptsAlreadyAdaptedNativeGlyphRunWithoutSourceProbe()
    {
        var positions = new[]
        {
            new Vector2(2, 3),
            new Vector2(7, -1)
        };
        var glyphRun = new FakePortableNativeGlyphRunSource(new PortableNativeGlyphRun
        {
            GlyphIndices = new ushort[] { 3, 4 },
            GlyphPositions = positions,
            BaselineOrigin = new Vector2(10, 20),
            FontRenderingEmSize = 12.5,
            FontFamilyNames = new[] { "Arial" },
            HasTransform = true,
            Transform = Matrix4x4.CreateTranslation(6, 7, 0),
            IsBold = true,
            IsItalic = true
        });

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var adapted));
        object boxedAdaptedGlyphRun = adapted;

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(boxedAdaptedGlyphRun, out var reused));
        Assert.Equal(1, glyphRun.PortableNativeGlyphRunProbeCount);
        Assert.Equal(0, glyphRun.PortableGlyphRunProbeCount);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
        Assert.Equal(new ushort[] { 3, 4 }, reused.GlyphIndices);
        Assert.Same(positions, reused.GlyphPositions);
        Assert.Equal(12.5f, reused.FontSize);
        Assert.Equal(new Vector2(10, 20), reused.Position);
        Assert.Equal(6, reused.Transform.M41);
        Assert.Equal(7, reused.Transform.M42);
        Assert.True(reused.IsBold);
        Assert.True(reused.IsItalic);
    }

    [Fact]
    public void AdaptGlyphRunAdaptsPortableNativeGlyphRunDtoWithoutPositionCopy()
    {
        var positions = new[]
        {
            new Vector2(1, 2),
            new Vector2(5, 6)
        };
        var glyphRun = new PortableNativeGlyphRun
        {
            GlyphIndices = new ushort[] { 8, 9 },
            GlyphPositions = positions,
            BaselineOrigin = new Vector2(3, 4),
            FontRenderingEmSize = 18,
            FontFamilyNames = new[] { "Arial" }
        };

        var adapted = WpfResourceResolver.AdaptGlyphRun(glyphRun);

        Assert.NotNull(adapted);
        Assert.Equal(new ushort[] { 8, 9 }, adapted.GlyphIndices);
        Assert.Same(positions, adapted.GlyphPositions);
        Assert.Equal(new Vector2(3, 4), adapted.Position);
    }

    [Fact]
    public void AdaptNativeGlyphRunCachesPortableNativeGlyphRunFont()
    {
        var glyphRun = new PortableNativeGlyphRun
        {
            GlyphIndices = new ushort[] { 11 },
            GlyphPositions = [new Vector2(2, 3)],
            BaselineOrigin = new Vector2(5, 7),
            FontRenderingEmSize = 16,
            FontFamilyNames = new[] { "Arial" }
        };

        Assert.Null(glyphRun.NativeFont);
        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var first));
        var cachedFont = Assert.IsType<TtfFont>(glyphRun.NativeFont);
        Assert.Same(cachedFont, first.Font);

        glyphRun.FontUri = "/missing/fonts/ProGPU-Missing-Font.ttf";
        glyphRun.FontFamilyNames = new[] { "ProGPU Missing Font After Cache" };

        Assert.True(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var second));
        Assert.Same(cachedFont, second.Font);
    }

    [Fact]
    public void AdaptGlyphRunCachesPortableGlyphRunFont()
    {
        var glyphRun = new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 12 },
            AdvanceWidths = new[] { 8.0 },
            GlyphOffsets = [new PortablePoint(1, 2)],
            BaselineOrigin = new PortablePoint(5, 7),
            FontRenderingEmSize = 14,
            FontFamilyNames = new[] { "Arial" }
        };

        Assert.Null(glyphRun.NativeFont);
        var first = WpfResourceResolver.AdaptGlyphRun(glyphRun);
        Assert.NotNull(first);
        var cachedFont = Assert.IsType<TtfFont>(glyphRun.NativeFont);
        Assert.Same(cachedFont, first.Font);

        glyphRun.FontUri = "/missing/fonts/ProGPU-Missing-Font.ttf";
        glyphRun.FontFamilyNames = new[] { "ProGPU Missing Font After Cache" };

        var second = WpfResourceResolver.AdaptGlyphRun(glyphRun);
        Assert.NotNull(second);
        Assert.Same(cachedFont, second.Font);
    }

    [Fact]
    public void AdaptGlyphRunSkipsUnavailablePortableGlyphRunWithoutReflectionFallback()
    {
        var glyphRun = new UnavailablePortableGlyphRun();

        Assert.Null(WpfResourceResolver.AdaptGlyphRun(glyphRun));
        Assert.False(WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out _));
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void DecodeDrawDrawingReplaysPortableGlyphRunDrawing()
    {
        var drawing = new FakeGlyphRunDrawing(
            new FakeSolidColorBrush(new FakeColor(255, 80, 90, 100)),
            new FakePortableGlyphRunSource(new PortableGlyphRun
            {
                GlyphIndices = new ushort[] { 7 },
                AdvanceWidths = new[] { 8.0 },
                BaselineOrigin = new PortablePoint(1, 2),
                FontRenderingEmSize = 14,
                FontFamilyNames = new[] { "Arial" }
            }));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { drawing });
        var sink = new TestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 1);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawDrawing, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var replayed = Assert.Single(sink.GlyphRuns);
        Assert.IsType<SolidColorBrush>(replayed.ForegroundBrush);
        Assert.Equal(new ushort[] { 7 }, replayed.GlyphRun.GlyphIndices);
        Assert.Equal(1, replayed.GlyphRun.Position.X);
        Assert.Equal(2, replayed.GlyphRun.Position.Y);
    }

    [Fact]
    public void DecodeGeometryAdaptsPortableLineGeometry()
    {
        var geometry = new FakeLineGeometry(new FakePoint(1, 2), new FakePoint(31, 42));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { null, geometry });
        var sink = new TestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        var adaptedGeometry = Assert.IsType<PathGeometry>(Assert.Single(sink.DrawGeometries).Geometry);
        Assert.Equal(new Rect(1, 2, 30, 40), adaptedGeometry.Bounds);
        Assert.False(adaptedGeometry.Figures[0].IsClosed);
        Assert.False(adaptedGeometry.Figures[0].IsFilled);
    }

    [Fact]
    public void AdaptGeometryAdaptsPortableEllipseGeometry()
    {
        var geometry = new FakeEllipseGeometry(new FakePoint(10, 20), 3, 4);

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfResourceResolver.AdaptGeometry(geometry));

        Assert.Equal(new Rect(7, 16, 6, 8), adaptedGeometry.Bounds);
        Assert.Single(adaptedGeometry.Figures);
        Assert.Equal(4, adaptedGeometry.Figures[0].Segments.Count);
        Assert.True(adaptedGeometry.Figures[0].IsClosed);
    }

    [Fact]
    public void AdaptGeometryAdaptsPortableGeometryGroup()
    {
        var group = new FakeGeometryGroup(
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10)),
            new FakeLineGeometry(new FakePoint(20, 5), new FakePoint(30, 5)))
        {
            FillRule = FakeFillRule.Nonzero
        };

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfResourceResolver.AdaptGeometry(group));

        Assert.Equal(FillRule.Nonzero, adaptedGeometry.FillRule);
        Assert.Equal(2, adaptedGeometry.Figures.Count);
        Assert.Equal(new Rect(0, 0, 30, 10), adaptedGeometry.Bounds);
    }

    [Fact]
    public void AdaptGeometryAdaptsPortableGeometryPathWithoutTypeNameShape()
    {
        var source = new FakePortableGeometryPathSource(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.EvenOdd,
            Transform = new PortableMatrix3x2(1, 0, 0, 1, 3, 4),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(0, 0),
                    IsClosed = true,
                    IsFilled = false,
                    Segments =
                    [
                        PortablePathSegment.Line(new PortablePoint(10, 0), isSmoothJoin: true, isStroked: false),
                        PortablePathSegment.Arc(
                            new PortablePoint(20, 10),
                            new PortableSize(5, 6),
                            rotationAngle: 30,
                            isLargeArc: true,
                            PortableSweepDirection.Clockwise,
                            isSmoothJoin: true,
                            isStroked: true)
                    ]
                }
            ]
        });

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfResourceResolver.AdaptGeometry(source));

        Assert.Equal(FillRule.EvenOdd, adaptedGeometry.FillRule);
        var transform = Assert.IsType<MatrixTransform>(adaptedGeometry.Transform);
        Assert.Equal(3, transform.Matrix.OffsetX);
        Assert.Equal(4, transform.Matrix.OffsetY);

        var figure = Assert.Single(adaptedGeometry.Figures);
        Assert.True(figure.IsClosed);
        Assert.False(figure.IsFilled);
        Assert.Equal(new Point(0, 0), figure.StartPoint);

        var line = Assert.IsType<LineSegment>(figure.Segments[0]);
        Assert.True(line.IsSmoothJoin);
        Assert.False(line.IsStroked);
        Assert.Equal(new Point(10, 0), line.Point);

        var arc = Assert.IsType<ArcSegment>(figure.Segments[1]);
        Assert.True(arc.IsSmoothJoin);
        Assert.True(arc.IsStroked);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
        Assert.Equal(30, arc.RotationAngle);
        Assert.Equal(new Size(5, 6), arc.Size);
    }

    [Fact]
    public void AdaptGeometrySkipsUnavailablePortableGeometryPathWithoutReflectionFallback()
    {
        var geometry = new UnavailablePortableLineGeometry();

        Assert.Null(WpfResourceResolver.AdaptGeometry(geometry));
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void DecodeGeometryPreservesCombinedGeometryInsidePortableGeometryGroup()
    {
        var group = new FakeGeometryGroup(
            new FakeCombinedGeometry(
                "Intersect",
                new FakeRectangleGeometry(new FakeRect(0, 0, 20, 20)),
                new FakeRectangleGeometry(new FakeRect(10, 10, 20, 20))),
            new FakeRectangleGeometry(new FakeRect(40, 0, 10, 10)));
        var resolver = WpfResourceResolver.FromDependentResources(new object?[] { Brushes.White, group });
        var nativeContext = new ProGPU.Scene.DrawingContext();
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
        Assert.Equal(ProGPU.Scene.RenderCommandType.DrawPath, command.Type);
        Assert.NotNull(command.Path);
        Assert.True(command.Path!.IsCombined);
        Assert.Equal(2, command.Path.Op);
        Assert.NotNull(command.Path.PathA);
        Assert.NotNull(command.Path.PathB);
        Assert.True(command.Path.PathA!.IsCombined);
        Assert.Equal(1, command.Path.PathA.Op);
        Assert.Single(command.Path.PathA.PathA!.Figures);
        Assert.Single(command.Path.PathA.PathB!.Figures);
        Assert.Single(command.Path.PathB!.Figures);
    }

    [Fact]
    public void AdaptGeometryAppliesChildTransformsInsidePortableGeometryGroup()
    {
        var group = new FakeGeometryGroup(
            new FakeRectangleGeometry(new FakeRect(0, 0, 10, 10))
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7))
            },
            new FakeLineGeometry(new FakePoint(20, 5), new FakePoint(30, 5))
            {
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 2, 3))
            });

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfResourceResolver.AdaptGeometry(group));

        Assert.Equal(new Rect(5, 7, 27, 10), adaptedGeometry.Bounds);
        Assert.Equal(new Point(5, 7), adaptedGeometry.Figures[0].StartPoint);
        Assert.Equal(new Point(22, 8), adaptedGeometry.Figures[1].StartPoint);
        var line = Assert.IsType<LineSegment>(Assert.Single(adaptedGeometry.Figures[1].Segments));
        Assert.Equal(new Point(32, 8), line.Point);
    }

    [Fact]
    public void AdaptGeometryPreservesTransformedArcsInsidePortableGeometryGroup()
    {
        var childTransform = new FakeMatrixTransform(new FakeMatrix(1, 0.35, 0.2, 1, 5, 7));
        var transformMatrix = new Matrix4x4(
            1f, 0.35f, 0f, 0f,
            0.2f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            5f, 7f, 0f, 1f);
        var group = new FakeGeometryGroup(
            new FakePathGeometry(
                new FakePathFigure(
                    new FakePoint(0, 0),
                    isClosed: false,
                    isFilled: true,
                    new FakeArcSegment(new FakePoint(30, 40), new FakeSize(10, 20))
                    {
                        RotationAngle = 45,
                        IsLargeArc = true,
                        SweepDirection = FakeSweepDirection.Clockwise
                    }))
            {
                Transform = childTransform
            });

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfResourceResolver.AdaptGeometry(group));

        var figure = Assert.Single(adaptedGeometry.Figures);
        var arc = Assert.IsType<ArcSegment>(Assert.Single(figure.Segments));

        var expectedStart = Vector2.Transform(new Vector2(0, 0), transformMatrix);
        var expectedEnd = Vector2.Transform(new Vector2(30, 40), transformMatrix);
        Assert.Equal(expectedStart.X, figure.StartPoint.X, 4);
        Assert.Equal(expectedStart.Y, figure.StartPoint.Y, 4);
        Assert.Equal(expectedEnd.X, arc.Point.X, 4);
        Assert.Equal(expectedEnd.Y, arc.Point.Y, 4);
        Assert.True(arc.Size.Width > 0);
        Assert.True(arc.Size.Height > 0);
        Assert.True(double.IsFinite(arc.RotationAngle));
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }

    [Fact]
    public void AdaptGeometryAdaptsPortablePathGeometryFiguresAndSegments()
    {
        var geometry = new FakePathGeometry(
            new FakePathFigure(
                new FakePoint(0, 0),
                isClosed: true,
                isFilled: false,
                new FakeLineSegment(new FakePoint(10, 0)) { IsSmoothJoin = true },
                new FakePolyLineSegment(new FakePointCollection(new FakePoint(20, 0), new FakePoint(30, 0))) { IsSmoothJoin = true },
                new FakeQuadraticBezierSegment(new FakePoint(35, 5), new FakePoint(40, 0)) { IsSmoothJoin = true },
                new FakePolyQuadraticBezierSegment(new FakePointCollection(new FakePoint(45, 5), new FakePoint(50, 0))) { IsSmoothJoin = true },
                new FakeBezierSegment(new FakePoint(55, 5), new FakePoint(60, 5), new FakePoint(65, 0)) { IsSmoothJoin = true },
                new FakePolyBezierSegment(new FakePointCollection(new FakePoint(70, 5), new FakePoint(75, 5), new FakePoint(80, 0))) { IsSmoothJoin = true },
                new FakeArcSegment(new FakePoint(100, 100), new FakeSize(3, 4))
                {
                    RotationAngle = 45,
                    IsLargeArc = true,
                    SweepDirection = FakeSweepDirection.Clockwise,
                    IsSmoothJoin = true
                }))
        {
            FillRule = FakeFillRule.Nonzero
        };

        var adaptedGeometry = Assert.IsType<PathGeometry>(WpfResourceResolver.AdaptGeometry(geometry));

        Assert.Equal(FillRule.Nonzero, adaptedGeometry.FillRule);
        var figure = Assert.Single(adaptedGeometry.Figures);
        Assert.True(figure.IsClosed);
        Assert.False(figure.IsFilled);
        Assert.Equal(8, figure.Segments.Count);
        Assert.IsType<LineSegment>(figure.Segments[0]);
        Assert.IsType<LineSegment>(figure.Segments[1]);
        Assert.IsType<LineSegment>(figure.Segments[2]);
        Assert.IsType<QuadraticBezierSegment>(figure.Segments[3]);
        Assert.IsType<QuadraticBezierSegment>(figure.Segments[4]);
        Assert.IsType<BezierSegment>(figure.Segments[5]);
        Assert.IsType<BezierSegment>(figure.Segments[6]);
        Assert.All(figure.Segments, segment => Assert.True(segment.IsSmoothJoin));
        var arc = Assert.IsType<ArcSegment>(figure.Segments[7]);
        Assert.Equal(100f, arc.Point.X);
        Assert.Equal(100f, arc.Point.Y);
        Assert.Equal(3, arc.Size.Width);
        Assert.Equal(4, arc.Size.Height);
        Assert.Equal(45, arc.RotationAngle);
        Assert.True(arc.IsLargeArc);
        Assert.Equal(SweepDirection.Clockwise, arc.SweepDirection);
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static FakeRenderData CreateRectangleRenderData(MediaBrush brush)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, 1);
        var record = CreateRecord(WpfMilCommandId.DrawRectangle, payload);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(brush));
    }

    private static global::ProGPU.Vector.Brush? ToNative(MediaBrush brush, WpfReplayRect bounds)
    {
        return WpfResourceResolver.AdaptNativeBrush(brush, bounds, out _);
    }

    private static void WriteRect(byte[] target, int offset, double x, double y, double width, double height)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
        WriteDouble(target, offset + 16, width);
        WriteDouble(target, offset + 24, height);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteDouble(byte[] target, int offset, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
    }

    private sealed class DirectNativeBrush : MediaBrush
    {
        public DirectNativeBrush()
        {
            NativeBrush = new ProGPU.Vector.SolidColorBrush(Vector4.One);
        }

        public ProGPU.Vector.Brush NativeBrush { get; }

        public int ParameterlessCallCount { get; private set; }

        public int BoundsCallCount { get; private set; }

        public Rect LastBounds { get; private set; }

        public override ProGPU.Vector.Brush ToNative()
        {
            ParameterlessCallCount++;
            return NativeBrush;
        }

        public override ProGPU.Vector.Brush ToNative(Rect targetBounds)
        {
            BoundsCallCount++;
            LastBounds = targetBounds;
            return NativeBrush;
        }
    }

    private sealed class DuckTypedNativeBrush
    {
        public int ToNativeCallCount { get; private set; }

        public ProGPU.Vector.Brush ToNative()
        {
            ToNativeCallCount++;
            return new ProGPU.Vector.SolidColorBrush(Vector4.One);
        }

        public ProGPU.Vector.Brush ToNative(WpfReplayRect bounds)
        {
            ToNativeCallCount++;
            return new ProGPU.Vector.SolidColorBrush(Vector4.One);
        }
    }

    private sealed class DuckTypedNativePen
    {
        public int ToNativeCallCount { get; private set; }

        public ProGPU.Vector.Pen ToNative()
        {
            ToNativeCallCount++;
            return new ProGPU.Vector.Pen(
                new ProGPU.Vector.SolidColorBrush(Vector4.One),
                1);
        }
    }

    private sealed class FakeSolidColorBrush : IPortableBrushSource
    {
        public FakeSolidColorBrush(FakeColor color, double opacity = 1)
        {
            Color = color;
            Opacity = opacity;
        }

        public FakeColor Color { get; }

        public double Opacity { get; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = PortableBrush.SolidColor(ToPortableColor(Color), Opacity);
            return true;
        }
    }

    private readonly record struct FakeColor(byte A, byte R, byte G, byte B);

    private sealed class FakePortableBrush : IPortableBrushSource
    {
        private readonly PortableBrush _brush;

        public FakePortableBrush(PortableBrush brush)
        {
            _brush = brush;
        }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = _brush;
            return true;
        }
    }

    private sealed class FakeUnavailablePortableSolidColorBrush : IPortableBrushSource
    {
        public int ReflectedPropertyProbeCount { get; private set; }

        public FakeColor Color
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return new FakeColor(255, 1, 2, 3);
            }
        }

        public double Opacity
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return 1.0;
            }
        }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = null!;
            return false;
        }
    }

    private sealed class FakePortablePen : IPortablePenSource
    {
        private readonly PortablePen _pen;

        public FakePortablePen(
            PortableBrush brush,
            double thickness,
            PortablePenLineCap startLineCap = PortablePenLineCap.Flat,
            PortablePenLineCap endLineCap = PortablePenLineCap.Flat,
            PortablePenLineCap dashCap = PortablePenLineCap.Flat,
            PortablePenLineJoin lineJoin = PortablePenLineJoin.Miter,
            double miterLimit = 10.0,
            double[]? dashArray = null,
            double dashOffset = 0.0)
        {
            _pen = new PortablePen(
                brush,
                thickness,
                startLineCap,
                endLineCap,
                dashCap,
                lineJoin,
                miterLimit,
                dashArray,
                dashOffset);
        }

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = _pen;
            return true;
        }
    }

    private sealed class FakeUnavailablePortablePen : IPortablePenSource
    {
        public int ReflectedPropertyProbeCount { get; private set; }

        public object Brush
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return new FakeSolidColorBrush(new FakeColor(255, 1, 2, 3));
            }
        }

        public double Thickness
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return 2.0;
            }
        }

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = null!;
            return false;
        }
    }

    private sealed class FakePortableTileBrushSource : IPortableTileBrushSource
    {
        private readonly PortableTileBrush _brush;

        public FakePortableTileBrushSource(PortableTileBrush brush)
        {
            _brush = brush;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = _brush;
            return true;
        }
    }

    private sealed class FakeUnavailablePortableImageBrush : IPortableTileBrushSource
    {
        private readonly object? _imageSource;

        public FakeUnavailablePortableImageBrush(object? imageSource)
        {
            _imageSource = imageSource;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public object? ImageSource => Probe(_imageSource);

        public FakeRect? Viewport => Probe<FakeRect?>(null);

        public FakeRect? Viewbox => Probe<FakeRect?>(null);

        public string TileMode => Probe("None");

        public string Stretch => Probe("Fill");

        public string ViewportUnits => Probe("RelativeToBoundingBox");

        public string ViewboxUnits => Probe("RelativeToBoundingBox");

        public double Opacity => Probe(1.0);

        public object? Transform => Probe<object?>(null);

        public object? RelativeTransform => Probe<object?>(null);

        public string AlignmentX => Probe("Center");

        public string AlignmentY => Probe("Center");

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = null!;
            return false;
        }

        private T Probe<T>(T value)
        {
            ReflectedPropertyProbeCount++;
            return value;
        }
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

        public double Opacity { get; init; } = 1;

        public string SpreadMethod { get; init; } = "Pad";

        public string ColorInterpolationMode { get; init; } = "SRgbLinearInterpolation";

        public string MappingMode { get; init; } = "RelativeToBoundingBox";

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = null!;
            if (!TryMapBrushMappingMode(MappingMode, out var mappingMode)
                || !TryMapGradientSpreadMethod(SpreadMethod, out var spreadMethod)
                || !TryMapGradientColorInterpolationMode(ColorInterpolationMode, out var colorInterpolationMode)
                || !TryMapOptionalTransform(Transform, out var hasTransform, out var transform)
                || !TryMapOptionalTransform(RelativeTransform, out var hasRelativeTransform, out var relativeTransform))
            {
                return false;
            }

            brush = PortableBrush.LinearGradient(
                ToPortablePoint(StartPoint),
                ToPortablePoint(EndPoint),
                ToPortableGradientStops(_stops),
                Opacity,
                mappingMode,
                spreadMethod,
                colorInterpolationMode,
                hasTransform,
                transform,
                hasRelativeTransform,
                relativeTransform);
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

        public double Opacity { get; init; } = 1;

        public string SpreadMethod { get; init; } = "Pad";

        public string ColorInterpolationMode { get; init; } = "SRgbLinearInterpolation";

        public string MappingMode { get; init; } = "RelativeToBoundingBox";

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = null!;
            if (!TryMapBrushMappingMode(MappingMode, out var mappingMode)
                || !TryMapGradientSpreadMethod(SpreadMethod, out var spreadMethod)
                || !TryMapGradientColorInterpolationMode(ColorInterpolationMode, out var colorInterpolationMode)
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
                Opacity,
                mappingMode,
                spreadMethod,
                colorInterpolationMode,
                hasTransform,
                transform,
                hasRelativeTransform,
                relativeTransform);
            return true;
        }
    }

    private static bool TryCreatePortableTileBrush(
        PortableTileBrushKind kind,
        object? content,
        double opacity,
        FakeRect? viewport,
        FakeRect? viewbox,
        string viewportUnits,
        string viewboxUnits,
        string tileMode,
        string stretch,
        string alignmentX,
        string alignmentY,
        object? transformValue,
        object? relativeTransformValue,
        out PortableTileBrush brush)
    {
        brush = null!;
        if (content == null
            || !TryMapBrushMappingMode(viewportUnits, out var portableViewportUnits)
            || !TryMapBrushMappingMode(viewboxUnits, out var portableViewboxUnits)
            || !TryMapTileMode(tileMode, out var portableTileMode)
            || !TryMapStretch(stretch, out var portableStretch)
            || !TryMapAlignmentX(alignmentX, out var portableAlignmentX)
            || !TryMapAlignmentY(alignmentY, out var portableAlignmentY)
            || !TryMapOptionalTransform(transformValue, out var hasTransform, out var transform)
            || !TryMapOptionalTransform(relativeTransformValue, out var hasRelativeTransform, out var relativeTransform))
        {
            return false;
        }

        brush = new PortableTileBrush(
            kind,
            content,
            opacity,
            ToPortableRect(viewport ?? new FakeRect(0, 0, 1, 1)),
            ToPortableRect(viewbox ?? new FakeRect(0, 0, 1, 1)),
            portableViewportUnits,
            portableViewboxUnits,
            portableTileMode,
            portableStretch,
            portableAlignmentX,
            portableAlignmentY,
            hasTransform,
            transform,
            hasRelativeTransform,
            relativeTransform);
        return true;
    }

    private static PortableRect ToPortableRect(FakeRect rect)
    {
        return new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
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

    private static bool TryMapBrushMappingMode(string value, out PortableBrushMappingMode mode)
    {
        switch (value)
        {
            case "RelativeToBoundingBox":
                mode = PortableBrushMappingMode.RelativeToBoundingBox;
                return true;
            case "Absolute":
                mode = PortableBrushMappingMode.Absolute;
                return true;
            default:
                mode = PortableBrushMappingMode.RelativeToBoundingBox;
                return false;
        }
    }

    private static bool TryMapGradientSpreadMethod(string value, out PortableGradientSpreadMethod spreadMethod)
    {
        switch (value)
        {
            case "Pad":
                spreadMethod = PortableGradientSpreadMethod.Pad;
                return true;
            case "Reflect":
                spreadMethod = PortableGradientSpreadMethod.Reflect;
                return true;
            case "Repeat":
                spreadMethod = PortableGradientSpreadMethod.Repeat;
                return true;
            default:
                spreadMethod = PortableGradientSpreadMethod.Pad;
                return false;
        }
    }

    private static bool TryMapGradientColorInterpolationMode(
        string value,
        out PortableGradientColorInterpolationMode colorInterpolationMode)
    {
        switch (value)
        {
            case "SRgbLinearInterpolation":
                colorInterpolationMode = PortableGradientColorInterpolationMode.SRgbLinearInterpolation;
                return true;
            case "ScRgbLinearInterpolation":
                colorInterpolationMode = PortableGradientColorInterpolationMode.ScRgbLinearInterpolation;
                return true;
            default:
                colorInterpolationMode = PortableGradientColorInterpolationMode.SRgbLinearInterpolation;
                return false;
        }
    }

    private static bool TryMapTileMode(string value, out PortableTileMode mode)
    {
        switch (value)
        {
            case "None":
                mode = PortableTileMode.None;
                return true;
            case "Tile":
                mode = PortableTileMode.Tile;
                return true;
            case "FlipX":
                mode = PortableTileMode.FlipX;
                return true;
            case "FlipY":
                mode = PortableTileMode.FlipY;
                return true;
            case "FlipXY":
                mode = PortableTileMode.FlipXY;
                return true;
            default:
                mode = PortableTileMode.None;
                return false;
        }
    }

    private static bool TryMapStretch(string value, out PortableStretch stretch)
    {
        switch (value)
        {
            case "None":
                stretch = PortableStretch.None;
                return true;
            case "Fill":
                stretch = PortableStretch.Fill;
                return true;
            case "Uniform":
                stretch = PortableStretch.Uniform;
                return true;
            case "UniformToFill":
                stretch = PortableStretch.UniformToFill;
                return true;
            default:
                stretch = PortableStretch.Fill;
                return false;
        }
    }

    private static bool TryMapAlignmentX(string value, out PortableAlignmentX alignment)
    {
        switch (value)
        {
            case "Left":
                alignment = PortableAlignmentX.Left;
                return true;
            case "Center":
                alignment = PortableAlignmentX.Center;
                return true;
            case "Right":
                alignment = PortableAlignmentX.Right;
                return true;
            default:
                alignment = PortableAlignmentX.Center;
                return false;
        }
    }

    private static bool TryMapAlignmentY(string value, out PortableAlignmentY alignment)
    {
        switch (value)
        {
            case "Top":
                alignment = PortableAlignmentY.Top;
                return true;
            case "Center":
                alignment = PortableAlignmentY.Center;
                return true;
            case "Bottom":
                alignment = PortableAlignmentY.Bottom;
                return true;
            default:
                alignment = PortableAlignmentY.Center;
                return false;
        }
    }

    private sealed class FakeImageBrush : IPortableTileBrushSource
    {
        public FakeImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }

        public FakeRect? Viewport { get; init; }

        public FakeRect? Viewbox { get; init; }

        public string TileMode { get; init; } = "None";

        public string Stretch { get; init; } = "Fill";

        public string ViewportUnits { get; init; } = "RelativeToBoundingBox";

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public string AlignmentX { get; init; } = "Center";

        public string AlignmentY { get; init; } = "Center";

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(
                PortableTileBrushKind.Image,
                ImageSource,
                Opacity,
                Viewport,
                Viewbox,
                ViewportUnits,
                ViewboxUnits,
                TileMode,
                Stretch,
                AlignmentX,
                AlignmentY,
                Transform,
                RelativeTransform,
                out brush);
        }
    }

    private sealed class FakeDrawingBrush : IPortableTileBrushSource
    {
        public FakeDrawingBrush(object? drawing)
        {
            Drawing = drawing;
        }

        public object? Drawing { get; }

        public FakeRect? Viewport { get; init; }

        public FakeRect? Viewbox { get; init; }

        public string TileMode { get; init; } = "None";

        public string Stretch { get; init; } = "Fill";

        public string ViewportUnits { get; init; } = "RelativeToBoundingBox";

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public string AlignmentX { get; init; } = "Center";

        public string AlignmentY { get; init; } = "Center";

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(
                PortableTileBrushKind.Drawing,
                Drawing,
                Opacity,
                Viewport,
                Viewbox,
                ViewportUnits,
                ViewboxUnits,
                TileMode,
                Stretch,
                AlignmentX,
                AlignmentY,
                Transform,
                RelativeTransform,
                out brush);
        }
    }

    private sealed class FakeVisualBrush : IPortableTileBrushSource
    {
        public FakeVisualBrush(object? visual)
        {
            Visual = visual;
        }

        public object? Visual { get; }

        public FakeRect? Viewport { get; init; }

        public FakeRect? Viewbox { get; init; }

        public string TileMode { get; init; } = "None";

        public string Stretch { get; init; } = "Fill";

        public string ViewportUnits { get; init; } = "RelativeToBoundingBox";

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? RelativeTransform { get; init; }

        public string AlignmentX { get; init; } = "Center";

        public string AlignmentY { get; init; } = "Center";

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            return TryCreatePortableTileBrush(
                PortableTileBrushKind.Visual,
                Visual,
                Opacity,
                Viewport,
                Viewbox,
                ViewportUnits,
                ViewboxUnits,
                TileMode,
                Stretch,
                AlignmentX,
                AlignmentY,
                Transform,
                RelativeTransform,
                out brush);
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

    private sealed class FakeReflectedMatrixTransform
    {
        private readonly FakeMatrix _value;

        public FakeReflectedMatrixTransform(FakeMatrix value)
        {
            _value = value;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public FakeMatrix Value
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return _value;
            }
        }
    }

    private sealed class FakePortableTransform : IPortableTransformMatrixSource
    {
        private readonly PortableMatrix3x2 _matrix;

        public FakePortableTransform(PortableMatrix3x2 matrix)
        {
            _matrix = matrix;
        }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = _matrix;
            return true;
        }
    }

    private sealed class FakeUnavailablePortableMatrixTransform : IPortableTransformMatrixSource
    {
        private readonly FakeMatrix _value;

        public FakeUnavailablePortableMatrixTransform(FakeMatrix value)
        {
            _value = value;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public FakeMatrix Value
        {
            get
            {
                ReflectedPropertyProbeCount++;
                return _value;
            }
        }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = default;
            return false;
        }
    }

    private readonly record struct FakeMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY);

    private static PortableSize ToPortableSize(FakeSize size)
    {
        return new PortableSize(size.Width, size.Height);
    }

    private static PortableGeometryPath CreatePortablePath(
        FakeFillRule fillRule,
        object? transform,
        params PortablePathFigure[] figures)
    {
        var path = new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = ToPortableFillRule(fillRule),
            Figures = figures
        };

        if (TryMapOptionalTransform(transform, out var hasTransform, out var portableTransform) && hasTransform)
        {
            path.Transform = portableTransform;
        }

        return path;
    }

    private static PortablePathFigure CreatePortableFigure(
        FakePoint startPoint,
        bool isClosed,
        bool isFilled,
        params PortablePathSegment[] segments)
    {
        return new PortablePathFigure
        {
            StartPoint = ToPortablePoint(startPoint),
            IsClosed = isClosed,
            IsFilled = isFilled,
            Segments = segments
        };
    }

    private static PortableGeometryPath CreatePortableRectangleGeometry(FakeRect rect, object? transform)
    {
        return CreatePortablePath(
            FakeFillRule.Nonzero,
            transform,
            CreatePortableFigure(
                new FakePoint(rect.X, rect.Y),
                isClosed: true,
                isFilled: true,
                PortablePathSegment.Line(new PortablePoint(rect.X + rect.Width, rect.Y), isSmoothJoin: false, isStroked: true),
                PortablePathSegment.Line(new PortablePoint(rect.X + rect.Width, rect.Y + rect.Height), isSmoothJoin: false, isStroked: true),
                PortablePathSegment.Line(new PortablePoint(rect.X, rect.Y + rect.Height), isSmoothJoin: false, isStroked: true)));
    }

    private static PortableGeometryPath CreatePortableLineGeometry(FakePoint startPoint, FakePoint endPoint, object? transform)
    {
        return CreatePortablePath(
            FakeFillRule.Nonzero,
            transform,
            CreatePortableFigure(
                startPoint,
                isClosed: false,
                isFilled: false,
                PortablePathSegment.Line(ToPortablePoint(endPoint), isSmoothJoin: false, isStroked: true)));
    }

    private static PortableGeometryPath CreatePortableEllipseGeometry(FakePoint center, double radiusX, double radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            return CreatePortablePath(FakeFillRule.Nonzero, transform: null);
        }

        const double kappa = 0.5522847498307936;
        var cx = center.X;
        var cy = center.Y;
        var rx = radiusX;
        var ry = radiusY;
        var ox = rx * kappa;
        var oy = ry * kappa;

        return CreatePortablePath(
            FakeFillRule.Nonzero,
            transform: null,
            CreatePortableFigure(
                new FakePoint(cx + rx, cy),
                isClosed: true,
                isFilled: true,
                PortablePathSegment.CubicBezier(
                    new PortablePoint(cx + rx, cy + oy),
                    new PortablePoint(cx + ox, cy + ry),
                    new PortablePoint(cx, cy + ry),
                    isSmoothJoin: false,
                    isStroked: true),
                PortablePathSegment.CubicBezier(
                    new PortablePoint(cx - ox, cy + ry),
                    new PortablePoint(cx - rx, cy + oy),
                    new PortablePoint(cx - rx, cy),
                    isSmoothJoin: false,
                    isStroked: true),
                PortablePathSegment.CubicBezier(
                    new PortablePoint(cx - rx, cy - oy),
                    new PortablePoint(cx - ox, cy - ry),
                    new PortablePoint(cx, cy - ry),
                    isSmoothJoin: false,
                    isStroked: true),
                PortablePathSegment.CubicBezier(
                    new PortablePoint(cx + ox, cy - ry),
                    new PortablePoint(cx + rx, cy - oy),
                    new PortablePoint(cx + rx, cy),
                    isSmoothJoin: false,
                    isStroked: true)));
    }

    private static bool TryGetPortableGeometryPath(object? value, out PortableGeometryPath path)
    {
        if (value == null)
        {
            path = CreatePortablePath(FakeFillRule.Nonzero, transform: null);
            return true;
        }

        if (value is IPortableGeometryPathSource source)
        {
            return source.TryGetPortableGeometryPath(out path);
        }

        path = null!;
        return false;
    }

    private static PortableGeometryPath FoldPortableGeometryChildren(IReadOnlyList<PortableGeometryPath> children)
    {
        var combined = children[0];
        for (var i = 1; i < children.Count; i++)
        {
            combined = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Combined,
                PathA = combined,
                PathB = children[i],
                CombineOperation = 2
            };
        }

        return combined;
    }

    private static bool TryFlattenPortablePath(PortableGeometryPath path, List<PortablePathFigure> figures)
    {
        if (path.Kind != PortableGeometryPathKind.Path)
        {
            return false;
        }

        foreach (var figure in path.Figures)
        {
            figures.Add(TransformPortableFigure(figure, path.Transform));
        }

        return true;
    }

    private static PortablePathFigure TransformPortableFigure(PortablePathFigure figure, PortableMatrix3x2 transform)
    {
        if (transform.IsIdentity)
        {
            return figure;
        }

        var matrix = ToMatrix4x4(transform);
        var sourceCurrentPoint = ToVector2(figure.StartPoint);
        var segments = new List<PortablePathSegment>(figure.Segments.Length);
        foreach (var segment in figure.Segments)
        {
            switch (segment.Kind)
            {
                case PortablePathSegmentKind.Line:
                    segments.Add(PortablePathSegment.Line(
                        TransformPoint(segment.Point1, matrix),
                        segment.IsSmoothJoin,
                        segment.IsStroked));
                    sourceCurrentPoint = ToVector2(segment.Point1);
                    break;
                case PortablePathSegmentKind.QuadraticBezier:
                    segments.Add(PortablePathSegment.QuadraticBezier(
                        TransformPoint(segment.Point1, matrix),
                        TransformPoint(segment.Point2, matrix),
                        segment.IsSmoothJoin,
                        segment.IsStroked));
                    sourceCurrentPoint = ToVector2(segment.Point2);
                    break;
                case PortablePathSegmentKind.CubicBezier:
                    segments.Add(PortablePathSegment.CubicBezier(
                        TransformPoint(segment.Point1, matrix),
                        TransformPoint(segment.Point2, matrix),
                        TransformPoint(segment.Point3, matrix),
                        segment.IsSmoothJoin,
                        segment.IsStroked));
                    sourceCurrentPoint = ToVector2(segment.Point3);
                    break;
                case PortablePathSegmentKind.Arc:
                    if (TryTransformPortableArcSegment(sourceCurrentPoint, segment, matrix, out var transformedArc))
                    {
                        segments.Add(transformedArc);
                    }
                    else
                    {
                        segments.Add(PortablePathSegment.Line(
                            TransformPoint(segment.Point1, matrix),
                            segment.IsSmoothJoin,
                            segment.IsStroked));
                    }

                    sourceCurrentPoint = ToVector2(segment.Point1);
                    break;
            }
        }

        return new PortablePathFigure
        {
            StartPoint = TransformPoint(figure.StartPoint, matrix),
            IsClosed = figure.IsClosed,
            IsFilled = figure.IsFilled,
            Segments = segments.ToArray()
        };
    }

    private static bool TryTransformPortableArcSegment(
        Vector2 startPoint,
        PortablePathSegment segment,
        Matrix4x4 transform,
        out PortablePathSegment transformedSegment)
    {
        transformedSegment = default;
        if (!global::ProGPU.Vector.ArcSegmentGeometry.TryTransformArcSegment(
                startPoint,
                new global::ProGPU.Vector.ArcSegment(
                    ToVector2(segment.Point1),
                    ToVector2(segment.Size),
                    (float)segment.RotationAngle,
                    segment.IsLargeArc,
                    (global::ProGPU.Vector.SweepDirection)(int)segment.SweepDirection,
                    segment.IsSmoothJoin),
                transform,
                out _,
                out var vectorArc))
        {
            return false;
        }

        transformedSegment = PortablePathSegment.Arc(
            new PortablePoint(vectorArc.Point.X, vectorArc.Point.Y),
            new PortableSize(vectorArc.Size.X, vectorArc.Size.Y),
            vectorArc.RotationAngle,
            vectorArc.IsLargeArc,
            (PortableSweepDirection)(int)vectorArc.SweepDirection,
            vectorArc.IsSmoothJoin,
            segment.IsStroked);
        return true;
    }

    private static Matrix4x4 ToMatrix4x4(PortableMatrix3x2 matrix)
    {
        return new Matrix4x4(
            (float)matrix.M11,
            (float)matrix.M12,
            0,
            0,
            (float)matrix.M21,
            (float)matrix.M22,
            0,
            0,
            0,
            0,
            1,
            0,
            (float)matrix.OffsetX,
            (float)matrix.OffsetY,
            0,
            1);
    }

    private static PortablePoint TransformPoint(PortablePoint point, Matrix4x4 matrix)
    {
        var transformed = Vector2.Transform(ToVector2(point), matrix);
        return new PortablePoint(transformed.X, transformed.Y);
    }

    private static Vector2 ToVector2(PortablePoint point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static Vector2 ToVector2(PortableSize size)
    {
        return new Vector2((float)size.Width, (float)size.Height);
    }

    private static PortableFillRule ToPortableFillRule(FakeFillRule fillRule)
    {
        return fillRule == FakeFillRule.EvenOdd
            ? PortableFillRule.EvenOdd
            : PortableFillRule.Nonzero;
    }

    private static PortableSweepDirection ToPortableSweepDirection(FakeSweepDirection sweepDirection)
    {
        return sweepDirection == FakeSweepDirection.Clockwise
            ? PortableSweepDirection.Clockwise
            : PortableSweepDirection.Counterclockwise;
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

    private static IEnumerable<PortablePathSegment> ToPortableSegments(object segment)
    {
        switch (segment)
        {
            case FakeLineSegment line:
                yield return PortablePathSegment.Line(
                    ToPortablePoint(line.Point),
                    line.IsSmoothJoin,
                    isStroked: true);
                break;
            case FakePolyLineSegment polyLine:
                for (var i = 0; i < polyLine.Points.Count; i++)
                {
                    yield return PortablePathSegment.Line(
                        ToPortablePoint(polyLine.Points[i]),
                        polyLine.IsSmoothJoin,
                        isStroked: true);
                }

                break;
            case FakeQuadraticBezierSegment quadratic:
                yield return PortablePathSegment.QuadraticBezier(
                    ToPortablePoint(quadratic.Point1),
                    ToPortablePoint(quadratic.Point2),
                    quadratic.IsSmoothJoin,
                    isStroked: true);
                break;
            case FakePolyQuadraticBezierSegment polyQuadratic:
                for (var i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
                {
                    yield return PortablePathSegment.QuadraticBezier(
                        ToPortablePoint(polyQuadratic.Points[i]),
                        ToPortablePoint(polyQuadratic.Points[i + 1]),
                        polyQuadratic.IsSmoothJoin,
                        isStroked: true);
                }

                break;
            case FakeBezierSegment cubic:
                yield return PortablePathSegment.CubicBezier(
                    ToPortablePoint(cubic.Point1),
                    ToPortablePoint(cubic.Point2),
                    ToPortablePoint(cubic.Point3),
                    cubic.IsSmoothJoin,
                    isStroked: true);
                break;
            case FakePolyBezierSegment polyCubic:
                for (var i = 0; i + 2 < polyCubic.Points.Count; i += 3)
                {
                    yield return PortablePathSegment.CubicBezier(
                        ToPortablePoint(polyCubic.Points[i]),
                        ToPortablePoint(polyCubic.Points[i + 1]),
                        ToPortablePoint(polyCubic.Points[i + 2]),
                        polyCubic.IsSmoothJoin,
                        isStroked: true);
                }

                break;
            case FakeArcSegment arc:
                yield return PortablePathSegment.Arc(
                    ToPortablePoint(arc.Point),
                    ToPortableSize(arc.Size),
                    arc.RotationAngle,
                    arc.IsLargeArc,
                    ToPortableSweepDirection(arc.SweepDirection),
                    arc.IsSmoothJoin,
                    isStroked: true);
                break;
        }
    }

    private sealed class FakeRectangleGeometry : IPortableGeometryPathSource
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public object? Transform { get; init; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = CreatePortableRectangleGeometry(Rect, Transform);
            return true;
        }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private sealed class FakeLineGeometry : IPortableGeometryPathSource
    {
        public FakeLineGeometry(FakePoint startPoint, FakePoint endPoint)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
        }

        public FakePoint StartPoint { get; }

        public FakePoint EndPoint { get; }

        public object? Transform { get; init; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = CreatePortableLineGeometry(StartPoint, EndPoint, Transform);
            return true;
        }
    }

    private sealed class FakePortableGeometryPathSource : IPortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public FakePortableGeometryPathSource(PortableGeometryPath path)
        {
            _path = path;
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class UnavailablePortableLineGeometry : IPortableGeometryPathSource
    {
        public int ReflectedGeometryProbeCount { get; private set; }

        public object? StartPoint => ThrowReflectedGeometryProbe();

        public object? EndPoint => ThrowReflectedGeometryProbe();

        public object? Transform => ThrowReflectedGeometryProbe();

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = null!;
            return false;
        }

        private object? ThrowReflectedGeometryProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGeometryProbeCount++;
            throw new InvalidOperationException($"Reflected geometry property '{propertyName}' should not be read.");
        }
    }

    private sealed class FakeEllipseGeometry : IPortableGeometryPathSource
    {
        public FakeEllipseGeometry(FakePoint center, double radiusX, double radiusY)
        {
            Center = center;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        public FakePoint Center { get; }

        public double RadiusX { get; }

        public double RadiusY { get; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = CreatePortableEllipseGeometry(Center, RadiusX, RadiusY);
            return true;
        }
    }

    private sealed class FakeGeometryGroup : IPortableGeometryPathSource
    {
        private readonly object[] _children;

        public FakeGeometryGroup(params object[] children)
        {
            _children = children;
            Children = new FakeGeometryCollection(children);
        }

        public FakeGeometryCollection Children { get; }

        public FakeFillRule FillRule { get; init; } = FakeFillRule.EvenOdd;

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = null!;
            var childPaths = new List<PortableGeometryPath>(_children.Length);
            var flattenedFigures = new List<PortablePathFigure>();
            var canFlatten = true;
            foreach (var child in _children)
            {
                if (!WpfResourceResolverTests.TryGetPortableGeometryPath(child, out var childPath))
                {
                    continue;
                }

                childPaths.Add(childPath);
                if (canFlatten)
                {
                    canFlatten = TryFlattenPortablePath(childPath, flattenedFigures);
                }
            }

            if (childPaths.Count == 0)
            {
                return false;
            }

            path = canFlatten
                ? new PortableGeometryPath
                {
                    Kind = PortableGeometryPathKind.Path,
                    FillRule = ToPortableFillRule(FillRule),
                    Figures = flattenedFigures.ToArray()
                }
                : FoldPortableGeometryChildren(childPaths);
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
            if (!WpfResourceResolverTests.TryGetPortableGeometryPath(Geometry1, out var pathA)
                || !WpfResourceResolverTests.TryGetPortableGeometryPath(Geometry2, out var pathB))
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

            if (TryMapOptionalTransform(Transform, out var hasTransform, out var portableTransform) && hasTransform)
            {
                path.Transform = portableTransform;
            }

            return true;
        }
    }

    private sealed class FakeGeometryCollection
    {
        private readonly object[] _items;

        public FakeGeometryCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private readonly record struct FakePoint(double X, double Y);

    private sealed class FakePointCollection
    {
        private readonly FakePoint[] _items;

        public FakePointCollection(params FakePoint[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakePoint this[int index] => _items[index];
    }

    private enum FakeFillRule
    {
        EvenOdd,
        Nonzero
    }

    private enum FakeSweepDirection
    {
        Counterclockwise,
        Clockwise
    }

    private sealed class FakePathGeometry : IPortableGeometryPathSource
    {
        public FakePathGeometry(params FakePathFigure[] figures)
        {
            Figures = new FakePathFigureCollection(figures);
        }

        public FakePathFigureCollection Figures { get; }

        public FakeFillRule FillRule { get; init; } = FakeFillRule.EvenOdd;

        public object? Transform { get; init; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            var figures = new PortablePathFigure[Figures.Count];
            for (var i = 0; i < figures.Length; i++)
            {
                figures[i] = Figures[i].ToPortableFigure();
            }

            path = CreatePortablePath(FillRule, Transform, figures);
            return true;
        }
    }

    private sealed class FakePathFigureCollection
    {
        private readonly FakePathFigure[] _items;

        public FakePathFigureCollection(FakePathFigure[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakePathFigure this[int index] => _items[index];
    }

    private sealed class FakePathFigure
    {
        public FakePathFigure(FakePoint startPoint, bool isClosed, bool isFilled, params object[] segments)
        {
            StartPoint = startPoint;
            IsClosed = isClosed;
            IsFilled = isFilled;
            Segments = new FakePathSegmentCollection(segments);
        }

        public FakePoint StartPoint { get; }

        public bool IsClosed { get; }

        public bool IsFilled { get; }

        public FakePathSegmentCollection Segments { get; }

        public PortablePathFigure ToPortableFigure()
        {
            var segments = new List<PortablePathSegment>();
            for (var i = 0; i < Segments.Count; i++)
            {
                foreach (var segment in ToPortableSegments(Segments[i]))
                {
                    segments.Add(segment);
                }
            }

            return CreatePortableFigure(StartPoint, IsClosed, IsFilled, segments.ToArray());
        }
    }

    private sealed class FakePathSegmentCollection
    {
        private readonly object[] _items;

        public FakePathSegmentCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private sealed class FakeLineSegment
    {
        public FakeLineSegment(FakePoint point)
        {
            Point = point;
        }

        public FakePoint Point { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakePolyLineSegment
    {
        public FakePolyLineSegment(FakePointCollection points)
        {
            Points = points;
        }

        public FakePointCollection Points { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakeQuadraticBezierSegment
    {
        public FakeQuadraticBezierSegment(FakePoint point1, FakePoint point2)
        {
            Point1 = point1;
            Point2 = point2;
        }

        public FakePoint Point1 { get; }

        public FakePoint Point2 { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakePolyQuadraticBezierSegment
    {
        public FakePolyQuadraticBezierSegment(FakePointCollection points)
        {
            Points = points;
        }

        public FakePointCollection Points { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakeBezierSegment
    {
        public FakeBezierSegment(FakePoint point1, FakePoint point2, FakePoint point3)
        {
            Point1 = point1;
            Point2 = point2;
            Point3 = point3;
        }

        public FakePoint Point1 { get; }

        public FakePoint Point2 { get; }

        public FakePoint Point3 { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakePolyBezierSegment
    {
        public FakePolyBezierSegment(FakePointCollection points)
        {
            Points = points;
        }

        public FakePointCollection Points { get; }

        public bool IsSmoothJoin { get; init; }
    }

    private sealed class FakeArcSegment
    {
        public FakeArcSegment(FakePoint point, FakeSize size)
        {
            Point = point;
            Size = size;
        }

        public FakePoint Point { get; }

        public FakeSize Size { get; }

        public double RotationAngle { get; init; }

        public bool IsLargeArc { get; init; }

        public FakeSweepDirection SweepDirection { get; init; } = FakeSweepDirection.Counterclockwise;

        public bool IsSmoothJoin { get; init; }
    }

    private readonly record struct FakeSize(double Width, double Height);

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

        public object? ClipGeometry { get; init; }

        public double Opacity { get; init; } = 1;

        public object? Bounds { get; init; }

        public object? OpacityMask { get; init; }

        public object? GuidelineSet { get; init; }

        public object? EdgeMode { get; init; }

        public object? BitmapScalingMode { get; init; }

        public object? ClearTypeHint { get; init; }

        public object? TextRenderingMode { get; init; }

        public object? TextHintingMode { get; init; }

        public object? BitmapEffect { get; init; }

        public object? BitmapEffectInput { get; init; }

        public object? Effect { get; init; }

        public object? CacheMode { get; init; }

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = new PortableDrawingGroupState
            {
                HasTransform = Transform != null,
                Transform = Transform,
                HasClipGeometry = ClipGeometry != null,
                ClipGeometry = ClipGeometry,
                HasOpacity = true,
                Opacity = Opacity,
                HasOpacityMask = OpacityMask != null,
                OpacityMask = OpacityMask,
                HasGuidelineSet = GuidelineSet != null,
                GuidelineSet = GuidelineSet,
                HasEdgeMode = EdgeMode != null,
                EdgeMode = EdgeMode,
                HasBitmapScalingMode = BitmapScalingMode != null,
                BitmapScalingMode = BitmapScalingMode,
                HasClearTypeHint = ClearTypeHint != null,
                ClearTypeHint = ClearTypeHint,
                HasTextRenderingMode = TextRenderingMode != null,
                TextRenderingMode = TextRenderingMode,
                HasTextHintingMode = TextHintingMode != null,
                TextHintingMode = TextHintingMode,
                HasBitmapEffect = BitmapEffect != null,
                BitmapEffect = BitmapEffect,
                HasBitmapEffectInput = BitmapEffectInput != null,
                BitmapEffectInput = BitmapEffectInput,
                HasEffect = Effect != null,
                Effect = Effect,
                HasCacheMode = CacheMode != null,
                CacheMode = CacheMode,
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

    private sealed class FakeBlurEffect : IPortableEffectSource
    {
        public FakeBlurEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.Blur(Radius);
            return true;
        }
    }

    private sealed class FakeBlurBitmapEffect : IPortableEffectSource
    {
        public FakeBlurBitmapEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.Blur(Radius);
            return true;
        }
    }

    private sealed class FakeContextBitmapEffectInput : IPortableBitmapEffectInputSource
    {
        public bool TryGetPortableBitmapEffectInput(out PortableBitmapEffectInput input)
        {
            input = new PortableBitmapEffectInput(
                usesContextInput: true,
                hasDefaultAreaToApplyEffect: true);
            return true;
        }
    }

    private sealed class FakeImageDrawing : IPortableImageDrawingStateSource
    {
        public FakeImageDrawing(object? imageSource, FakeRect rect)
        {
            ImageSource = imageSource;
            Rect = rect;
        }

        public object? ImageSource { get; }

        public FakeRect Rect { get; }

        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = new PortableImageDrawingState
            {
                HasImageSource = ImageSource != null,
                ImageSource = ImageSource,
                HasRect = true,
                Rect = new PortableRect(Rect.X, Rect.Y, Rect.Width, Rect.Height)
            };
            return true;
        }
    }

    private sealed class FakeDrawingVisual : IPortableDrawingContentSource, IPortableVisualChildrenSource, IPortableVisualBoundsSource
    {
        private readonly object? _content;

        public FakeDrawingVisual(object? content)
        {
            _content = content;
        }

        public FakeVisualCollection Children { get; } = new();

        public object? Bounds { get; init; }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = Children.Count;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            if (index < 0 || index >= Children.Count)
            {
                child = null;
                return false;
            }

            child = Children[index];
            return true;
        }

        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new PortableVisualBounds();
            if (Bounds is not FakeRect rect)
            {
                return true;
            }

            var portableBounds = new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
            bounds.HasContentBounds = true;
            bounds.ContentBounds = portableBounds;
            bounds.HasDescendantBounds = true;
            bounds.DescendantBounds = portableBounds;
            return true;
        }
    }

    private sealed class FakeVisualCollection
    {
        private readonly List<object> _children = new();

        public int Count => _children.Count;

        public object this[int index] => _children[index];

        public void Add(object child)
        {
            _children.Add(child);
        }
    }

    private sealed class FakeRenderData : IPortableRenderDataSource
    {
        private readonly byte[] _buffer;
        private readonly int _curOffset;
        private readonly FakeDependentResources _dependentResources;

        public FakeRenderData(byte[] buffer, int curOffset, FakeDependentResources dependentResources)
        {
            _buffer = buffer;
            _curOffset = curOffset;
            _dependentResources = dependentResources;
        }

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            snapshot = new PortableRenderDataSnapshot(
                _buffer.AsSpan(0, _curOffset).ToArray(),
                _dependentResources.Items);
            return true;
        }
    }

    private sealed class FakeDependentResources
    {
        private readonly object?[] _items;

        public FakeDependentResources(params object?[] items)
        {
            _items = items;
        }

        public IReadOnlyList<object?> Items => _items;

        public int Count => _items.Length;

        public object? this[int index] => _items[index];
    }

    private sealed class ThrowingEnumerableResourceList : IReadOnlyList<object?>
    {
        private readonly object?[] _items;

        public ThrowingEnumerableResourceList(params object?[] items)
        {
            _items = items;
        }

        public int EnumerationCount { get; private set; }

        public int Count => _items.Length;

        public object? this[int index] => _items[index];

        public IEnumerator<object?> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Dependent resources should be resolved by token index.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class FakeBitmapSource
    {
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class FakeAdaptedBitmapSource : MediaBitmapSource
    {
        public override int PixelWidth => 200;

        public override int PixelHeight => 100;

        public override global::ProGPU.Backend.GpuTexture GpuTexture => null!;
    }

    private sealed class FakeImageSourceAdapter : IWpfImageSourceAdapter
    {
        public MediaImageSource AdaptedImageSource { get; } = new FakeAdaptedBitmapSource();

        public object? LastImageSource { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            LastImageSource = imageSource;
            return AdaptedImageSource;
        }
    }

    private sealed class FakeGlyphRunDrawing : IPortableGlyphRunDrawingStateSource
    {
        public FakeGlyphRunDrawing(object? foregroundBrush, object? glyphRun)
        {
            ForegroundBrush = foregroundBrush;
            GlyphRun = glyphRun;
        }

        public object? ForegroundBrush { get; }

        public object? GlyphRun { get; }

        public bool TryGetPortableGlyphRunDrawingState(out PortableGlyphRunDrawingState state)
        {
            state = new PortableGlyphRunDrawingState
            {
                HasForegroundBrush = ForegroundBrush != null,
                ForegroundBrush = ForegroundBrush,
                HasGlyphRun = GlyphRun != null,
                GlyphRun = GlyphRun
            };
            return true;
        }
    }

    private static bool TryFindLoadableFontPathDifferentFromFallback(out string fontPath, out TtfFont expectedFont)
    {
        fontPath = string.Empty;
        expectedFont = null!;

        TtfFont? fallbackFont = null;
        try
        {
            fallbackFont = new FontFamily("Arial").NativeFont;
        }
        catch (Exception ex) when (IsRecoverableFontLoadException(ex))
        {
        }

        foreach (var fontInfo in FontApi.GetSystemFonts())
        {
            if (string.IsNullOrWhiteSpace(fontInfo.FilePath) || !File.Exists(fontInfo.FilePath))
            {
                continue;
            }

            try
            {
                var font = new TtfFont(fontInfo.FilePath);
                if (fallbackFont != null && !HasDifferentFontMetrics(font, fallbackFont))
                {
                    continue;
                }

                fontPath = fontInfo.FilePath;
                expectedFont = font;
                return true;
            }
            catch (Exception ex) when (IsRecoverableFontLoadException(ex))
            {
            }
        }

        return false;
    }

    private static bool HasDifferentFontMetrics(TtfFont left, TtfFont right)
    {
        return left.UnitsPerEm != right.UnitsPerEm
            || left.Ascender != right.Ascender
            || left.Descender != right.Descender
            || left.NumGlyphs != right.NumGlyphs;
    }

    private static bool IsRecoverableFontLoadException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or FormatException
            or InvalidDataException
            or KeyNotFoundException
            or IndexOutOfRangeException
            or OverflowException
            or NotSupportedException;
    }

    private sealed class FakePortableGlyphRunSource : IPortableGlyphRunSource
    {
        private readonly PortableGlyphRun _glyphRun;

        public FakePortableGlyphRunSource(PortableGlyphRun glyphRun)
        {
            _glyphRun = glyphRun;
        }

        public int ReflectedGlyphRunProbeCount { get; private set; }

        public object? GlyphIndices => ThrowReflectedGlyphRunProbe();

        public object? AdvanceWidths => ThrowReflectedGlyphRunProbe();

        public object? GlyphOffsets => ThrowReflectedGlyphRunProbe();

        public object? BaselineOrigin => ThrowReflectedGlyphRunProbe();

        public object? FontRenderingEmSize => ThrowReflectedGlyphRunProbe();

        public object? GlyphTypeface => ThrowReflectedGlyphRunProbe();

        public object? Font => ThrowReflectedGlyphRunProbe();

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            glyphRun = _glyphRun;
            return true;
        }

        private object? ThrowReflectedGlyphRunProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGlyphRunProbeCount++;
            throw new InvalidOperationException($"Reflected glyph-run property '{propertyName}' should not be read.");
        }
    }

    private sealed class FakePortableNativeGlyphRunSource : IPortableNativeGlyphRunSource, IPortableGlyphRunSource
    {
        private readonly PortableNativeGlyphRun _glyphRun;

        public FakePortableNativeGlyphRunSource(PortableNativeGlyphRun glyphRun)
        {
            _glyphRun = glyphRun;
        }

        public int PortableGlyphRunProbeCount { get; private set; }

        public int PortableNativeGlyphRunProbeCount { get; private set; }

        public int ReflectedGlyphRunProbeCount { get; private set; }

        public object? GlyphIndices => ThrowReflectedGlyphRunProbe();

        public object? AdvanceWidths => ThrowReflectedGlyphRunProbe();

        public object? GlyphOffsets => ThrowReflectedGlyphRunProbe();

        public object? BaselineOrigin => ThrowReflectedGlyphRunProbe();

        public object? FontRenderingEmSize => ThrowReflectedGlyphRunProbe();

        public object? GlyphTypeface => ThrowReflectedGlyphRunProbe();

        public object? Font => ThrowReflectedGlyphRunProbe();

        public bool TryGetPortableNativeGlyphRun(out PortableNativeGlyphRun glyphRun)
        {
            PortableNativeGlyphRunProbeCount++;
            glyphRun = _glyphRun;
            return true;
        }

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            PortableGlyphRunProbeCount++;
            glyphRun = null!;
            return false;
        }

        private object? ThrowReflectedGlyphRunProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGlyphRunProbeCount++;
            throw new InvalidOperationException($"Reflected glyph-run property '{propertyName}' should not be read.");
        }
    }

    private sealed class UnavailablePortableGlyphRun : IPortableGlyphRunSource
    {
        public int ReflectedGlyphRunProbeCount { get; private set; }

        public object? GlyphIndices => ThrowReflectedGlyphRunProbe();

        public object? AdvanceWidths => ThrowReflectedGlyphRunProbe();

        public object? GlyphOffsets => ThrowReflectedGlyphRunProbe();

        public object? BaselineOrigin => ThrowReflectedGlyphRunProbe();

        public object? FontRenderingEmSize => ThrowReflectedGlyphRunProbe();

        public object? GlyphTypeface => ThrowReflectedGlyphRunProbe();

        public object? Font => ThrowReflectedGlyphRunProbe();

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            glyphRun = null!;
            return false;
        }

        private object? ThrowReflectedGlyphRunProbe([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            ReflectedGlyphRunProbeCount++;
            throw new InvalidOperationException($"Reflected glyph-run property '{propertyName}' should not be read.");
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

    private sealed class TestSink : IWpfCompositionCommandSink, IWpfVisualEffectCommandSink, IWpfDrawingCacheCommandSink
    {
        public List<string> Operations { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> DrawGeometries { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle, Rect SourceRectangle)> SourceImages { get; } = new();

        public List<MediaGeometry> Clips { get; } = new();

        public List<(MediaBrush? ForegroundBrush, MediaGlyphRun GlyphRun)> GlyphRuns { get; } = new();

        public List<(MediaBrush? OpacityMask, Rect Bounds)> OpacityMasks { get; } = new();

        public List<MediaTransform> Transforms { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<object?> BitmapScalingModes { get; } = new();

        public List<object?> EdgeModes { get; } = new();

        public List<object?> TextRenderingModes { get; } = new();

        public List<object?> TextHintingModes { get; } = new();

        public List<ProGpuEffectBase> VisualEffects { get; } = new();

        public List<Rect?> VisualEffectBounds { get; } = new();

        public List<Rect?> DrawingCacheBounds { get; } = new();

        public bool AcceptVisualEffects { get; init; }

        public bool AcceptDrawingCaches { get; init; }

        public int PopCount { get; private set; }

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            Operations.Add("DrawRectangle");
            DrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            Operations.Add("DrawGeometry");
            DrawGeometries.Add((brush, pen, geometry));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Images.Add((imageSource, rectangle));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
        {
            Images.Add((imageSource, rectangle));
            SourceImages.Add((imageSource, rectangle, sourceRectangle));
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            GlyphRuns.Add((foregroundBrush, glyphRun));
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            Operations.Add("PushClip");
            Clips.Add(clipGeometry);
        }

        public void PushOpacity(double opacity)
        {
            Operations.Add("PushOpacity");
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            Operations.Add("PushOpacityMask");
            OpacityMasks.Add((opacityMask, bounds));
        }

        public void PushTransform(MediaTransform transform)
        {
            Operations.Add("PushTransform");
            Transforms.Add(transform);
        }

        public void PushGuidelineSet()
        {
            Operations.Add("PushGuidelineSet");
        }

        public void PushGuidelineSet(object? guidelines)
        {
            Operations.Add("PushGuidelineSetObject");
            Assert.NotNull(guidelines);
        }

        public void PushBitmapScalingMode(object? bitmapScalingMode)
        {
            Operations.Add("PushBitmapScalingMode");
            BitmapScalingModes.Add(bitmapScalingMode);
        }

        public void PushEdgeMode(object? edgeMode)
        {
            Operations.Add("PushEdgeMode");
            EdgeModes.Add(edgeMode);
        }

        public void PushTextRenderingMode(object? textRenderingMode)
        {
            Operations.Add("PushTextRenderingMode");
            TextRenderingModes.Add(textRenderingMode);
        }

        public void PushTextHintingMode(object? textHintingMode)
        {
            Operations.Add("PushTextHintingMode");
            TextHintingModes.Add(textHintingMode);
        }

        public void Pop()
        {
            Operations.Add("Pop");
            PopCount++;
        }

        public bool PushVisualEffect(ProGpuEffectBase effect)
        {
            return PushVisualEffect(effect, bounds: null);
        }

        public bool PushVisualEffect(ProGpuEffectBase effect, Rect? bounds)
        {
            if (!AcceptVisualEffects)
            {
                return false;
            }

            Operations.Add("PushVisualEffect");
            VisualEffects.Add(effect);
            VisualEffectBounds.Add(bounds);
            return true;
        }

        public bool PushDrawingCache(Rect? bounds = null)
        {
            if (!AcceptDrawingCaches)
            {
                return false;
            }

            Operations.Add("PushDrawingCache");
            DrawingCacheBounds.Add(bounds);
            return true;
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
