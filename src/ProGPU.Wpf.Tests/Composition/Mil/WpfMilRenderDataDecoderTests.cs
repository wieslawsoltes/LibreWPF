using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfMilRenderDataDecoderTests
{
    [Fact]
    public void DecodeDrawRectangleResolvesBrushAndPen()
    {
        var brush = Brushes.Red;
        var pen = new Pen(Brushes.Black, 2);
        var resolver = new TestResolver
        {
            Brush = brush,
            Pen = pen
        };
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
        Assert.Same(brush, sink.DrawRectangles[0].Brush);
        Assert.Same(pen, sink.DrawRectangles[0].Pen);
        Assert.Equal(1, sink.DrawRectangles[0].Rectangle.X);
        Assert.Equal(2, sink.DrawRectangles[0].Rectangle.Y);
        Assert.Equal(30, sink.DrawRectangles[0].Rectangle.Width);
        Assert.Equal(40, sink.DrawRectangles[0].Rectangle.Height);
    }

    [Fact]
    public void DecodeNativeDrawGeometryUsesPortableRawGeometryWithoutManagedResolution()
    {
        var brush = Brushes.Red;
        var portableGeometry = CreatePortableRectangleGeometry(1, 2, 30, 40);
        var resolver = new TestResolver { Brush = brush };
        resolver.RawResources[2] = new FakePortableGeometry(portableGeometry);
        var sink = new NativeTestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 0);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, resolver.ResolveGeometryCallCount);
        var draw = Assert.Single(sink.NativeDrawGeometries);
        Assert.Same(brush, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Same(portableGeometry, draw.Geometry);
    }

    [Fact]
    public void DecodeTypedDrawGeometryUsesPortableRawGeometryWithoutManagedResolution()
    {
        var brush = Brushes.Blue;
        var portableGeometry = CreatePortableRectangleGeometry(11, 12, 130, 140);
        var resolver = new TestResolver { Brush = brush };
        resolver.RawResources[2] = new FakePortableGeometry(portableGeometry);
        var sink = new TypedNativeGeometryTestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 0);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, resolver.ResolveGeometryCallCount);
        var draw = Assert.Single(sink.NativeDrawGeometries);
        Assert.Same(brush, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Same(portableGeometry, draw.Geometry);
    }

    [Fact]
    public void DecodeDrawVideoUsesTypedLiveFrameAndAnimatedRectangle()
    {
        var nativeImage = new object();
        var resolver = new TestResolver();
        resolver.RawResources[1] = new FakeMediaPlayer(
            new PortableMediaPlayerFrame(64, 32, 7, nativeImage));
        resolver.RawResources[2] = new FakeRectAnimation(
            new PortableRect(11, 12, 13, 14));
        var sink = new VideoTestSink();

        var staticPayload = new byte[40];
        WriteRect(staticPayload, 0, 1, 2, 30, 40);
        WriteUInt32(staticPayload, 32, 1);
        var animatedPayload = new byte[40];
        WriteRect(animatedPayload, 0, 3, 4, 50, 60);
        WriteUInt32(animatedPayload, 32, 1);
        WriteUInt32(animatedPayload, 36, 2);

        WpfMilDecodeResult result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawVideo, staticPayload)
                .Concat(CreateRecord(
                    WpfMilCommandId.DrawVideoAnimate,
                    animatedPayload))
                .ToArray(),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result);
        Assert.Collection(
            sink.Videos,
            video =>
            {
                Assert.Equal(7UL, video.Frame.ContentVersion);
                Assert.Same(nativeImage, video.Frame.NativeImage);
                Assert.Equal(new WpfReplayRect(1, 2, 30, 40), video.Rectangle);
            },
            video => Assert.Equal(
                new WpfReplayRect(11, 12, 13, 14),
                video.Rectangle));
    }

    [Fact]
    public void DecodeNativeDrawImageReplaysTypedDrawingImageAsVectorGeometry()
    {
        var portableGeometry = CreatePortableRectangleGeometry(10, 20, 20, 10);
        var drawing = new FakeGeometryDrawing(
            Brushes.Blue,
            new FakePortableGeometry(portableGeometry));
        var resolver = new TestResolver();
        resolver.RawResources[2] = new FakeDrawingImage(drawing);
        var sink = new NativeTestSink();

        var payload = new byte[40];
        WriteRect(payload, 0, 100, 200, 80, 60);
        WriteUInt32(payload, 32, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawImage, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(new WpfReplayRect(100, 200, 80, 60), Assert.Single(sink.NativeClipBounds));
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(4, transform.M11);
        Assert.Equal(6, transform.M22);
        Assert.Equal(60, transform.M41);
        Assert.Equal(80, transform.M42);
        var draw = Assert.Single(sink.NativeDrawGeometries);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Same(portableGeometry, draw.Geometry);
        Assert.Empty(sink.Images);
        Assert.Equal(2, sink.PopCount);
    }

    [Fact]
    public void DecodeNativeDrawGeometryUsesLocalRectanglePrimitiveWithoutGenericGeometryFallback()
    {
        var brush = Brushes.Red;
        var pen = new Pen(Brushes.Black, 2);
        var geometry = new RectangleGeometry(new Rect(1, 2, 30, 40));
        var resolver = new TestResolver
        {
            Brush = brush,
            Pen = pen,
            Geometry = geometry
        };
        var sink = new NativeTestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);
        WriteUInt32(payload, 8, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(1, resolver.ResolveGeometryCallCount);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.NativeRectangles);
        Assert.Same(brush, draw.Brush);
        Assert.Same(pen, draw.Pen);
        Assert.Equal(1, draw.Rectangle.X);
        Assert.Equal(2, draw.Rectangle.Y);
        Assert.Equal(30, draw.Rectangle.Width);
        Assert.Equal(40, draw.Rectangle.Height);
    }

    [Fact]
    public void DecodeNativeDrawGeometryUsesLocalMediaGeometryWithoutGenericGeometryFallback()
    {
        var brush = Brushes.Red;
        var pen = new Pen(Brushes.Black, 2);
        var geometry = CreateCurvedPathGeometry(new Rect(1, 2, 30, 40));
        var resolver = new TestResolver
        {
            Brush = brush,
            Pen = pen,
            Geometry = geometry
        };
        var sink = new NativeTestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 2);
        WriteUInt32(payload, 8, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(1, resolver.ResolveGeometryCallCount);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.NativeDrawGeometries);
        Assert.Empty(sink.NativeRectangles);
        var draw = Assert.Single(sink.NativeMediaDrawGeometries);
        Assert.Same(brush, draw.Brush);
        Assert.Same(pen, draw.Pen);
        Assert.Same(geometry, draw.Geometry);
    }

    [Fact]
    public void DecodeNativeDrawGeometryReplaysImageBrushInsideEllipseClip()
    {
        var imageSource = new FakeImageSource();
        var adaptedImageSource = new FakeImageSource();
        var imageSourceAdapter = new RecordingImageSourceAdapter(adaptedImageSource);
        var brush = new FakeMediaImageBrush(imageSource);
        var geometry = new EllipseGeometry(new Point(16, 16), 16, 16);
        var resolver = new TestResolver
        {
            Brush = brush,
            Geometry = geometry
        };
        resolver.RawResources[2] = geometry;
        var sink = new NativeTestSink();

        var payload = new byte[16];
        WriteUInt32(payload, 0, 1);
        WriteUInt32(payload, 4, 0);
        WriteUInt32(payload, 8, 2);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawGeometry, payload),
            sink,
            resolver,
            imageSourceAdapter);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Equal(0, resolver.ResolveGeometryCallCount);
        Assert.Same(imageSource, Assert.Single(imageSourceAdapter.Sources));
        Assert.Same(adaptedImageSource, Assert.Single(sink.Images));
        Assert.Equal(
            1,
            sink.NativeGeometryClips.Count
            + sink.NativeMediaGeometryClips.Count
            + sink.NativeClipBounds.Count);
        Assert.Empty(sink.NativeMediaDrawGeometries);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodePortableRectangleClipUsesNativeClipWithoutManagedResolution()
    {
        var portableGeometry = CreatePortableRectangleGeometry(5, 6, 70, 80);
        var resolver = new TestResolver();
        resolver.RawResources[3] = new FakePortableGeometry(portableGeometry);
        var sink = new NativeTestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.PushClip, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(0, resolver.ResolveGeometryCallCount);
        var clip = Assert.Single(sink.NativeClipBounds);
        Assert.Equal(5, clip.X);
        Assert.Equal(6, clip.Y);
        Assert.Equal(70, clip.Width);
        Assert.Equal(80, clip.Height);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodeRoundedRectangleClipUsesNativeMediaGeometryClipWithoutBroadNativeClip()
    {
        var geometry = new RectangleGeometry(new Rect(5, 6, 70, 80))
        {
            RadiusX = 4,
            RadiusY = 6
        };
        var resolver = new TestResolver { Geometry = geometry };
        var sink = new NativeTestSink();

        var payload = new byte[8];
        WriteUInt32(payload, 0, 3);

        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.PushClip, payload),
            sink,
            resolver);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(1, resolver.ResolveGeometryCallCount);
        Assert.Equal(0, sink.ClipCount);
        Assert.Empty(sink.NativeClipBounds);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Same(geometry, Assert.Single(sink.NativeMediaGeometryClips));
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodeSkipsPopForUnresolvedPush()
    {
        var pushClipPayload = new byte[8];
        WriteUInt32(pushClipPayload, 0, 99);

        var renderData = CreateRecord(WpfMilCommandId.PushClip, pushClipPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(2, 0, 2, 0), result);
        Assert.Equal(0, sink.PopCount);
    }

    [Fact]
    public void DecodeUnwindsAppliedPushesAtEndOfRenderData()
    {
        var opacityPayload = new byte[8];
        WriteDouble(opacityPayload, 0, 0.5);

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.PushOpacity, opacityPayload),
            sink,
            new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 1), result);
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodeNullResourcePushesAsNoOpScopes()
    {
        var pushClipPayload = new byte[8];
        WriteUInt32(pushClipPayload, 0, 0);
        var pushOpacityMaskPayload = new byte[24];
        WriteUInt32(pushOpacityMaskPayload, 16, 0);
        var pushTransformPayload = new byte[8];
        WriteUInt32(pushTransformPayload, 0, 0);

        var renderData = CreateRecord(WpfMilCommandId.PushClip, pushClipPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushOpacityMask, pushOpacityMaskPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushTransform, pushTransformPayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(6, 6, 0, 0), result);
        Assert.Equal(3, sink.NoOpScopeCount);
        Assert.Equal(3, sink.PopCount);
    }

    [Fact]
    public void DecodeGuidelinePushesAsNoOpScopes()
    {
        var guidelineSetPayload = new byte[8];
        WriteUInt32(guidelineSetPayload, 0, 1);
        var guidelineY1Payload = new byte[8];
        WriteDouble(guidelineY1Payload, 0, 12.5);
        var guidelineY2Payload = new byte[16];
        WriteDouble(guidelineY2Payload, 0, 20.5);
        WriteDouble(guidelineY2Payload, 8, 3.25);

        var renderData = CreateRecord(WpfMilCommandId.PushGuidelineSet, guidelineSetPayload)
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushGuidelineY1, guidelineY1Payload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .Concat(CreateRecord(WpfMilCommandId.PushGuidelineY2, guidelineY2Payload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(6, 6, 0, 0), result);
        Assert.Equal(1, sink.GuidelineSetCount);
        Assert.Equal(new[] { 12.5 }, sink.GuidelineY1Coordinates);
        var guidelineY2 = Assert.Single(sink.GuidelineY2Coordinates);
        Assert.Equal(20.5, guidelineY2.LeadingCoordinate);
        Assert.Equal(3.25, guidelineY2.OffsetToDrivenCoordinate);
        Assert.Equal(3, sink.PopCount);
    }

    [Fact]
    public void DecodeAnimatedRecordsReplayBaseValuesAndCountAnimationHandlesAsUnsupportedState()
    {
        var imageSource = new FakeImageSource();
        var resolver = new TestResolver { ImageSource = imageSource };
        var linePayload = new byte[48];
        WritePoint(linePayload, 0, 1, 2);
        WritePoint(linePayload, 16, 3, 4);
        WriteUInt32(linePayload, 36, 10);
        WriteUInt32(linePayload, 40, 11);
        var rectanglePayload = new byte[48];
        WriteRect(rectanglePayload, 0, 5, 6, 7, 8);
        WriteUInt32(rectanglePayload, 40, 12);
        var roundedRectanglePayload = new byte[72];
        WriteRect(roundedRectanglePayload, 0, 9, 10, 11, 12);
        WriteDouble(roundedRectanglePayload, 32, 2);
        WriteDouble(roundedRectanglePayload, 40, 3);
        WriteUInt32(roundedRectanglePayload, 56, 13);
        WriteUInt32(roundedRectanglePayload, 64, 14);
        var ellipsePayload = new byte[56];
        WritePoint(ellipsePayload, 0, 13, 14);
        WriteDouble(ellipsePayload, 16, 15);
        WriteDouble(ellipsePayload, 24, 16);
        WriteUInt32(ellipsePayload, 44, 15);
        WriteUInt32(ellipsePayload, 48, 16);
        var imagePayload = new byte[40];
        WriteRect(imagePayload, 0, 17, 18, 19, 20);
        WriteUInt32(imagePayload, 32, 1);
        WriteUInt32(imagePayload, 36, 17);
        var opacityPayload = new byte[16];
        WriteDouble(opacityPayload, 0, 0.5);
        WriteUInt32(opacityPayload, 8, 18);

        var renderData = CreateRecord(WpfMilCommandId.DrawLineAnimate, linePayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangleAnimate, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawRoundedRectangleAnimate, roundedRectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawEllipseAnimate, ellipsePayload))
            .Concat(CreateRecord(WpfMilCommandId.DrawImageAnimate, imagePayload))
            .Concat(CreateRecord(WpfMilCommandId.PushOpacityAnimate, opacityPayload))
            .ToArray();

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(renderData, sink, resolver);

        Assert.Equal(new WpfMilDecodeResult(6, 6, 0, 10), result);
        Assert.Equal(1, sink.LineCount);
        Assert.Single(sink.DrawRectangles);
        Assert.Equal(1, sink.RoundedRectangleCount);
        Assert.Equal(1, sink.EllipseCount);
        Assert.Same(imageSource, Assert.Single(sink.Images));
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(1, sink.PopCount);
    }

    [Fact]
    public void DecodeAnimatedRecordsWithZeroHandlesDoNotCountUnsupportedState()
    {
        var rectanglePayload = new byte[48];
        WriteRect(rectanglePayload, 0, 5, 6, 7, 8);
        WriteUInt32(rectanglePayload, 40, 0);

        var sink = new TestSink();
        var result = new WpfMilRenderDataDecoder().Decode(
            CreateRecord(WpfMilCommandId.DrawRectangleAnimate, rectanglePayload),
            sink,
            new TestResolver());

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
    }

    [Fact]
    public void ResourceRegistryUsesOneBasedDependentResourceTokens()
    {
        var brush = Brushes.Blue;
        var pen = new Pen(Brushes.Black, 1);
        var overrideBrush = Brushes.Green;
        var guidelineSet = new object();
        var registry = WpfMilResourceRegistry.FromDependentResources(new object?[] { brush, null, pen });

        Assert.Same(brush, registry.ResolveBrush(1));
        Assert.Null(registry.ResolveBrush(2));
        Assert.Same(pen, registry.ResolvePen(3));
        Assert.Null(registry.ResolveBrush(0));
        Assert.Null(registry.ResolvePen(1));

        registry.Register(1, overrideBrush);
        registry.Register(4, guidelineSet);

        Assert.Same(overrideBrush, registry.ResolveBrush(1));
        Assert.Same(guidelineSet, registry.ResolveGuidelineSet(4));
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static void WriteRect(byte[] target, int offset, double x, double y, double width, double height)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
        WriteDouble(target, offset + 16, width);
        WriteDouble(target, offset + 24, height);
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

    private static void WriteDouble(byte[] target, int offset, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
    }

    private static PortableGeometryPath CreatePortableRectangleGeometry(double x, double y, double width, double height)
    {
        return new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.Nonzero,
            Bounds = new PortableRect(x, y, width, height),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(x, y),
                    IsClosed = true,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.Line(new PortablePoint(x + width, y), isSmoothJoin: false, isStroked: true),
                        PortablePathSegment.Line(new PortablePoint(x + width, y + height), isSmoothJoin: false, isStroked: true),
                        PortablePathSegment.Line(new PortablePoint(x, y + height), isSmoothJoin: false, isStroked: true)
                    ]
                }
            ]
        };
    }

    private static PathGeometry CreateCurvedPathGeometry(Rect bounds)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(bounds.X, bounds.Y + bounds.Height),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new BezierSegment(
            new Point(bounds.X + bounds.Width * 0.25, bounds.Y),
            new Point(bounds.X + bounds.Width * 0.75, bounds.Y),
            new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height),
            isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Y + bounds.Height), isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private sealed class TestResolver : IWpfMilResourceResolver, IWpfRawMilResourceResolver
    {
        public Dictionary<uint, object> RawResources { get; } = new();

        public MediaBrush? Brush { get; init; }

        public MediaPen? Pen { get; init; }

        public MediaGeometry? Geometry { get; init; }

        public MediaImageSource? ImageSource { get; init; }

        public int ResolveGeometryCallCount { get; private set; }

        public MediaBrush? ResolveBrush(uint resourceToken) => Brush;

        public MediaPen? ResolvePen(uint resourceToken) => Pen;

        public MediaGeometry? ResolveGeometry(uint resourceToken)
        {
            ResolveGeometryCallCount++;
            return Geometry;
        }

        public MediaImageSource? ResolveImageSource(uint resourceToken) => ImageSource;

        public MediaGlyphRun? ResolveGlyphRun(uint resourceToken) => null;

        public MediaTransform? ResolveTransform(uint resourceToken) => null;

        public bool TryResolveRawResource(uint resourceToken, out object resource)
        {
            return RawResources.TryGetValue(resourceToken, out resource!);
        }
    }

    private sealed class FakeMediaPlayer(PortableMediaPlayerFrame frame) :
        IPortableMediaPlayerSource
    {
        public bool TryGetPortableMediaPlayerFrame(
            out PortableMediaPlayerFrame value)
        {
            value = frame;
            return true;
        }
    }

    private sealed class FakeRectAnimation(PortableRect value) :
        IPortableRectAnimationValueSource
    {
        public bool TryGetPortableRectAnimationValue(out PortableRect result)
        {
            result = value;
            return true;
        }
    }

    private sealed class VideoTestSink : TestSink, IWpfNativeVideoCommandSink
    {
        public List<(PortableMediaPlayerFrame Frame, WpfReplayRect Rectangle)>
            Videos { get; } = new();

        public bool DrawNativeVideo(
            PortableMediaPlayerFrame frame,
            WpfReplayRect rectangle)
        {
            Videos.Add((frame, rectangle));
            return true;
        }
    }

    private class TestSink : IWpfCompositionCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> DrawGeometries { get; } = new();

        public List<MediaImageSource> Images { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<double> GuidelineY1Coordinates { get; } = new();

        public List<(double LeadingCoordinate, double OffsetToDrivenCoordinate)> GuidelineY2Coordinates { get; } = new();

        public int GuidelineSetCount { get; private set; }

        public int LineCount { get; private set; }

        public int RoundedRectangleCount { get; private set; }

        public int EllipseCount { get; private set; }

        public int ClipCount { get; private set; }

        public int NoOpScopeCount { get; private set; }

        public int PopCount { get; private set; }

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            LineCount++;
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            DrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            RoundedRectangleCount++;
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            EllipseCount++;
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            DrawGeometries.Add((brush, pen, geometry));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Images.Add(imageSource);
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            ClipCount++;
        }

        public void PushOpacity(double opacity)
        {
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
        }

        public void PushTransform(MediaTransform transform)
        {
        }

        public void PushNoOpScope()
        {
            NoOpScopeCount++;
        }

        public void PushGuidelineSet()
        {
            GuidelineSetCount++;
        }

        public void PushGuidelineY1(double coordinate)
        {
            GuidelineY1Coordinates.Add(coordinate);
        }

        public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            GuidelineY2Coordinates.Add((leadingCoordinate, offsetToDrivenCoordinate));
        }

        public void Pop()
        {
            PopCount++;
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TypedNativeGeometryTestSink :
        TestSink,
        IWpfNativeGeometryCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, PortableGeometryPath Geometry)> NativeDrawGeometries { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> NativeMediaDrawGeometries { get; } = new();

        public List<PortableGeometryPath> NativeGeometryClips { get; } = new();

        public List<MediaGeometry> NativeMediaGeometryClips { get; } = new();

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
        {
            NativeDrawGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            NativeMediaDrawGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
        {
            NativeGeometryClips.Add(clipGeometry);
            return true;
        }

        public bool PushNativeGeometryClip(MediaGeometry clipGeometry)
        {
            NativeMediaGeometryClips.Add(clipGeometry);
            return true;
        }
    }

    private sealed class NativeTestSink :
        TestSink,
        IWpfNativeTransformCommandSink,
        IWpfNativePrimitiveCommandSink,
        IWpfNativeGeometryCommandSink,
        IWpfNativeClipCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, PortableGeometryPath Geometry)> NativeDrawGeometries { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> NativeMediaDrawGeometries { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayRect Rectangle)> NativeRectangles { get; } = new();

        public List<WpfReplayRect> NativeClipBounds { get; } = new();

        public List<System.Numerics.Matrix4x4> NativeTransforms { get; } = new();

        public List<PortableGeometryPath> NativeGeometryClips { get; } = new();

        public List<MediaGeometry> NativeMediaGeometryClips { get; } = new();

        public void PushNativeTransform(System.Numerics.Matrix4x4 transform)
        {
            NativeTransforms.Add(transform);
        }

        public void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
        {
        }

        public void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
        {
            NativeRectangles.Add((brush, pen, rectangle));
        }

        public void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
        {
        }

        public void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
        {
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
        {
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
        {
        }

        public void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRun)
        {
        }

        public void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
        {
        }

        public void PushNativeClip(WpfReplayRect bounds)
        {
            NativeClipBounds.Add(bounds);
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
        {
            NativeDrawGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            NativeMediaDrawGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
        {
            NativeGeometryClips.Add(clipGeometry);
            return true;
        }

        public bool PushNativeGeometryClip(MediaGeometry clipGeometry)
        {
            NativeMediaGeometryClips.Add(clipGeometry);
            return true;
        }
    }

    private sealed class FakePortableGeometry(PortableGeometryPath path) : IPortableGeometryPathSource
    {
        public bool TryGetPortableGeometryPath(out PortableGeometryPath portablePath)
        {
            portablePath = path;
            return true;
        }
    }

    private sealed class FakeGeometryDrawing(
        object? brush,
        object? geometry) : IPortableGeometryDrawingStateSource
    {
        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasBrush = brush != null,
                Brush = brush,
                HasGeometry = geometry != null,
                Geometry = geometry
            };
            return true;
        }
    }

    private sealed class FakeDrawingImage(object? drawing) : IPortableDrawingImageSource
    {
        public bool TryGetPortableDrawingImage(out object? portableDrawing)
        {
            portableDrawing = drawing;
            return portableDrawing != null;
        }
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class RecordingImageSourceAdapter(MediaImageSource adaptedImageSource) : IWpfImageSourceAdapter
    {
        public List<object?> Sources { get; } = new();

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            Sources.Add(imageSource);
            return adaptedImageSource;
        }
    }

    private sealed class FakeMediaImageBrush(object imageSource) : MediaBrush, IPortableTileBrushSource
    {
        public override global::ProGPU.Vector.Brush ToNative()
        {
            return Brushes.Red.ToNative();
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = new PortableTileBrush(
                PortableTileBrushKind.Image,
                imageSource,
                opacity: 1,
                viewport: new PortableRect(0, 0, 1, 1),
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
            return true;
        }
    }
}
