using System.Buffers.Binary;
using System.Collections.Generic;
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

public sealed class WpfRenderDataBridgeTests
{
    [Fact]
    public void ExtractUsesSourceTrimmedPortableRenderDataBytesAndDependentResources()
    {
        var brush = Brushes.Green;
        var record = CreateRectangleRecord(1, 0);
        var pooledBuffer = record.Concat(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }).ToArray();
        var renderData = new FakeRenderData(pooledBuffer, record.Length, new FakeDependentResources(brush));

        var snapshot = WpfRenderDataBridge.Extract(renderData);

        Assert.Equal(record, snapshot.RenderData);
        Assert.NotSame(pooledBuffer, snapshot.RenderData);
        Assert.Same(renderData.LastSnapshot!.RenderData, snapshot.RenderData);
        Assert.Same(renderData.LastSnapshot.DependentResources, snapshot.DependentResources);
        Assert.Equal(new object?[] { brush }, snapshot.DependentResources);
    }

    [Fact]
    public void ExtractUsesPortableRenderDataSnapshotWithoutBridgeCopies()
    {
        var brush = Brushes.Green;
        var record = CreateRectangleRecord(1, 0);
        object?[] dependentResources = [brush];
        var renderData = new TypedPortableRenderDataSource(record, dependentResources);

        var snapshot = WpfRenderDataBridge.Extract(renderData);

        Assert.Same(record, snapshot.RenderData);
        Assert.Same(dependentResources, snapshot.DependentResources);
        Assert.Equal(new object?[] { brush }, snapshot.DependentResources);
        Assert.Equal(1, renderData.TypedSnapshotCount);
    }

    [Fact]
    public void ExtractUsesSharedEmptyDependentResources()
    {
        var record = CreateRectangleRecord(0, 0);
        var renderData = new TypedPortableRenderDataSource(record, Array.Empty<object?>());

        var snapshot = WpfRenderDataBridge.Extract(renderData);

        Assert.Equal(record, snapshot.RenderData);
        Assert.Same(Array.Empty<object?>(), snapshot.DependentResources);
    }

    [Fact]
    public void ExtractRejectsNonPortableRenderDataFieldShape()
    {
        var brush = Brushes.Green;
        var record = CreateRectangleRecord(1, 0);
        var renderData = new LegacyRenderDataFieldShape(record, record.Length, new FakeDependentResources(brush));

        var exception = Assert.Throws<InvalidOperationException>(
            () => WpfRenderDataBridge.Extract(renderData));

        Assert.Contains("portable WPF RenderData source contract", exception.Message);
    }

    [Fact]
    public void ReplayDecodesExtractedRenderDataWithOneBasedResources()
    {
        var brush = Brushes.Red;
        var pen = new Pen(Brushes.Black, 3);
        var record = CreateRectangleRecord(1, 2);
        var renderData = new FakeRenderData(record, record.Length, new FakeDependentResources(brush, pen));
        var sink = new TestSink();

        var result = new WpfRenderDataBridge().Replay(renderData, sink);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Single(sink.DrawRectangles);
        Assert.Same(brush, sink.DrawRectangles[0].Brush);
        Assert.Same(pen, sink.DrawRectangles[0].Pen);
        Assert.Equal(new Rect(1, 2, 30, 40), sink.DrawRectangles[0].Rectangle);
    }

    [Fact]
    public void ReplayUsesImageSourceAdapterForDefaultReflectionResolver()
    {
        var source = new FakeBitmapSource();
        var adapter = new FakeImageSourceAdapter();
        var record = CreateImageRecord(1);
        var renderData = new FakeRenderData(record, record.Length, new FakeDependentResources(source));
        var sink = new TestSink();

        var result = new WpfRenderDataBridge().Replay(
            renderData,
            sink,
            imageSourceAdapter: adapter);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(source, adapter.LastImageSource);
        var image = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, image.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), image.Rectangle);
    }

    [Fact]
    public void ReplayUsesExplicitImageSourceAdapterWithSuppliedResourceResolver()
    {
        var source = new FakeImageSource();
        var adapter = new FakeImageSourceAdapter();
        var record = CreateImageRecord(1);
        var renderData = new FakeRenderData(record, record.Length, new FakeDependentResources(source));
        var resources = WpfResourceResolver.FromDependentResources(new object?[] { source });
        var sink = new TestSink();

        var result = new WpfRenderDataBridge().Replay(
            renderData,
            sink,
            resources,
            adapter);

        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result);
        Assert.Same(source, adapter.LastImageSource);
        var image = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, image.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), image.Rectangle);
    }

    private static byte[] CreateRectangleRecord(uint brushToken, uint penToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, brushToken);
        WriteUInt32(payload, 36, penToken);
        return CreateRecord(WpfMilCommandId.DrawRectangle, payload);
    }

    private static byte[] CreateImageRecord(uint imageSourceToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, imageSourceToken);
        return CreateRecord(WpfMilCommandId.DrawImage, payload);
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

        public PortableRenderDataSnapshot? LastSnapshot { get; private set; }

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            snapshot = new PortableRenderDataSnapshot(
                _buffer.AsSpan(0, _curOffset).ToArray(),
                _dependentResources.Items);
            LastSnapshot = snapshot;
            return true;
        }
    }

    private sealed class LegacyRenderDataFieldShape
    {
        private readonly byte[] _buffer;
        private readonly int _curOffset;
        private readonly FakeDependentResources _dependentResources;

        public LegacyRenderDataFieldShape(byte[] buffer, int curOffset, FakeDependentResources dependentResources)
        {
            _buffer = buffer;
            _curOffset = curOffset;
            _dependentResources = dependentResources;
        }
    }

    private sealed class TypedPortableRenderDataSource : IPortableRenderDataSource
    {
        private readonly byte[] _renderData;
        private readonly IReadOnlyList<object?> _dependentResources;

        public TypedPortableRenderDataSource(byte[] renderData, IReadOnlyList<object?> dependentResources)
        {
            _renderData = renderData;
            _dependentResources = dependentResources;
        }

        public int TypedSnapshotCount { get; private set; }

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            TypedSnapshotCount++;
            snapshot = new PortableRenderDataSnapshot(_renderData, _dependentResources);
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

    private sealed class FakeBitmapSource
    {
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class FakeImageSourceAdapter : IWpfImageSourceAdapter
    {
        public MediaImageSource AdaptedImageSource { get; } = new FakeImageSource();

        public object? LastImageSource { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            LastImageSource = imageSource;
            return AdaptedImageSource;
        }
    }

    private sealed class TestSink : IWpfCompositionCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
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
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Images.Add((imageSource, rectangle));
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
        }

        public void PushOpacity(double opacity)
        {
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
        }

        public void PushTransform(MediaTransform transform)
        {
        }

        public void Pop()
        {
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}
