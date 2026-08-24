using System.Buffers.Binary;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfNativeMilSceneCompilerTests
{
    [Fact]
    public void BuildBatchTranslatesTypedVisualRectangleAndSolidBrush()
    {
        var brush = new FakeBrush(new PortableColor(192, 128, 64, 32));
        var visual = new FakeVisual(
            new FakeRenderData(CreateRectangleRecord(1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 640, 480);
        List<int> commands = ReadCommands(result.Bytes);

        Assert.Equal(4U, result.TargetHandle);
        Assert.Equal(
            [0x07, 0x1a, 0x1b, 0x20, 0x07, 0x7e, 0x07, 0x18,
             0x22, 0x07, 0x34, 0x36, 0x35],
            commands);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;
        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x40, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
        Assert.Equal(2.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(6.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(30.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(40.0, ReadDouble(result.Bytes, nestedOffset + 32));

        int brushOffset = FindCommand(result.Bytes, 0x7e);
        Assert.Equal(2U, ReadUInt32(result.Bytes, brushOffset + 8));
        Assert.Equal(1.0, ReadDouble(result.Bytes, brushOffset + 12));
        Assert.Equal(SrgbToLinear(128), ReadSingle(result.Bytes, brushOffset + 20));
        Assert.Equal(SrgbToLinear(64), ReadSingle(result.Bytes, brushOffset + 24));
        Assert.Equal(SrgbToLinear(32), ReadSingle(result.Bytes, brushOffset + 28));
        Assert.Equal(192 / 255.0f, ReadSingle(result.Bytes, brushOffset + 32));
    }

    [Fact]
    public void BuildBatchFailsClosedForRectanglePen()
    {
        var brush = new FakeBrush(new PortableColor(255, 255, 0, 0));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRectangleRecord(1, 2),
                [brush, new object()]));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () =>
            {
                _ = new WpfNativeMilSceneCompiler().BuildBatch(visual, 32, 32);
            });

        Assert.Contains("rectangle pens", exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesBalancedOpacityScopes()
    {
        var brush = new FakeBrush(new PortableColor(255, 0, 128, 255));
        byte[] renderData = CreatePushOpacityRecord(0.5)
            .Concat(CreateRectangleRecord(1, 0))
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x4f, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0.5, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0x40, ReadInt32(result.Bytes, nestedOffset + 20));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 64));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 68));
    }

    [Fact]
    public void BuildBatchFailsClosedForUnbalancedOpacityScope()
    {
        var visual = new FakeVisual(
            new FakeRenderData(CreatePushOpacityRecord(0.5), []));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains("stack is unbalanced", exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesEllipseWithNativeBrushHandle()
    {
        var brush = new FakeBrush(new PortableColor(255, 0, 255, 64));
        var visual = new FakeVisual(
            new FakeRenderData(CreateEllipseRecord(1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x44, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(5.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(9.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(7.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(11.0, ReadDouble(result.Bytes, nestedOffset + 32));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesUniformRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 4, 1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(64, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x42, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(4.0, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(4.0, ReadDouble(result.Bytes, nestedOffset + 48));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 56));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 60));
    }

    [Fact]
    public void BuildBatchFailsClosedForNonUniformRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 6, 1, 0), [brush]));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => new WpfNativeMilSceneCompiler().BuildBatch(visual, 64, 64));

        Assert.Contains("non-uniform", exception.Message);
    }

    private static List<int> ReadCommands(byte[] batch)
    {
        var commands = new List<int>();
        int offset = 0;
        while (offset < batch.Length)
        {
            int itemSize = ReadInt32(batch, offset);
            Assert.True(itemSize >= 8);
            Assert.Equal(0, itemSize & 3);
            Assert.InRange(itemSize, 8, batch.Length - offset);
            commands.Add(ReadInt32(batch, offset + 4));
            offset += itemSize;
        }
        Assert.Equal(batch.Length, offset);
        return commands;
    }

    private static byte[] CreateRectangleRecord(uint brush, uint pen)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x40);
        WriteDouble(record, 8, 2);
        WriteDouble(record, 16, 6);
        WriteDouble(record, 24, 30);
        WriteDouble(record, 32, 40);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), pen);
        return record;
    }

    private static byte[] CreatePushOpacityRecord(double opacity)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x4f);
        WriteDouble(record, 8, opacity);
        return record;
    }

    private static byte[] CreateEllipseRecord(uint brush, uint pen)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x44);
        WriteDouble(record, 8, 5);
        WriteDouble(record, 16, 9);
        WriteDouble(record, 24, 7);
        WriteDouble(record, 32, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), pen);
        return record;
    }

    private static byte[] CreateRoundedRectangleRecord(
        double radiusX,
        double radiusY,
        uint brush,
        uint pen)
    {
        byte[] record = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x42);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 3);
        WriteDouble(record, 24, 20);
        WriteDouble(record, 32, 30);
        WriteDouble(record, 40, radiusX);
        WriteDouble(record, 48, radiusY);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(56), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(60), pen);
        return record;
    }

    private static byte[] CreatePopRecord()
    {
        byte[] record = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x56);
        return record;
    }

    private static void WriteDouble(byte[] bytes, int offset, double value) =>
        BinaryPrimitives.WriteInt64LittleEndian(
            bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));

    private static int FindCommand(byte[] batch, int command)
    {
        int offset = 0;
        while (offset < batch.Length)
        {
            if (ReadInt32(batch, offset + 4) == command)
            {
                return offset;
            }
            offset += ReadInt32(batch, offset);
        }
        throw new Xunit.Sdk.XunitException(
            $"MIL command 0x{command:x} was not found.");
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static float ReadSingle(byte[] bytes, int offset) =>
        BitConverter.UInt32BitsToSingle(ReadUInt32(bytes, offset));

    private static double ReadDouble(byte[] bytes, int offset) =>
        BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, 8)));

    private static float SrgbToLinear(byte component)
    {
        float value = component / 255.0f;
        return value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    private sealed class FakeVisual :
        IPortableVisualStateSource,
        IPortableVisualChildrenSource,
        IPortableDrawingContentSource
    {
        private readonly object? _content;

        internal FakeVisual(object? content)
        {
            _content = content;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            };
            return true;
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = 0;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = null;
            return false;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class FakeRenderData : IPortableRenderDataSource
    {
        private readonly PortableRenderDataSnapshot _snapshot;

        internal FakeRenderData(byte[] bytes, IReadOnlyList<object?> resources)
        {
            _snapshot = new PortableRenderDataSnapshot(bytes, resources);
        }

        public bool TryGetPortableRenderDataSnapshot(
            out PortableRenderDataSnapshot snapshot)
        {
            snapshot = _snapshot;
            return true;
        }
    }

    private sealed class FakeBrush : IPortableBrushSource
    {
        private readonly PortableBrush _brush;

        internal FakeBrush(PortableColor color)
        {
            _brush = PortableBrush.SolidColor(color);
        }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = _brush;
            return true;
        }
    }
}
