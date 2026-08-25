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
    public void BuildBatchTranslatesTypedRectanglePen()
    {
        var brush = new FakeBrush(new PortableColor(255, 255, 0, 0));
        var pen = new FakePen(
            new PortableColor(255, 0, 0, 255),
            2,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Round,
            PortablePenLineJoin.Bevel,
            8,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRectangleRecord(1, 2),
                [brush, pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 32, 32);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 44));
        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(4U, ReadUInt32(result.Bytes, penOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 28));
        Assert.Equal(1U, ReadUInt32(result.Bytes, penOffset + 48));
    }

    [Fact]
    public void BuildBatchPreservesPenOnlyRectangle()
    {
        var pen = new FakePen(
            new PortableColor(255, 0, 255, 0),
            1,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            [2, 1]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateRectangleRecord(0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 32, 32);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesTypedLineGeometryWithTransform()
    {
        var pen = new FakePen(
            new PortableColor(255, 0, 128, 255),
            2,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Round,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            []);
        var geometry = new FakeGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            Transform = new PortableMatrix3x2(2, 0, 0, 3, 11, 13),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(1, 2),
                    IsClosed = false,
                    IsFilled = false,
                    Segments =
                    [
                        PortablePathSegment.Line(
                            new PortablePoint(5, 8),
                            isSmoothJoin: false,
                            isStroked: true)
                    ]
                }
            ]
        });
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(0, 1, 2),
                [pen, geometry]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(24, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x46, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 12));
        Assert.Equal(5U, ReadUInt32(result.Bytes, nestedOffset + 16));

        int geometryOffset = FindCommand(result.Bytes, 0x78);
        Assert.Equal(5U, ReadUInt32(result.Bytes, geometryOffset + 8));
        Assert.Equal(1.0, ReadDouble(result.Bytes, geometryOffset + 12));
        Assert.Equal(2.0, ReadDouble(result.Bytes, geometryOffset + 20));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 28));
        Assert.Equal(8.0, ReadDouble(result.Bytes, geometryOffset + 36));
        Assert.Equal(4U, ReadUInt32(result.Bytes, geometryOffset + 44));
    }

    [Fact]
    public void BuildBatchRejectsUntypedLineGeometry()
    {
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(0, 0, 1),
                [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortablePrimitiveGeometrySource),
            exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedRectangleAndEllipseGeometry()
    {
        var rectangle = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                2,
                new PortableMatrix3x2(2, 0, 0, 3, 11, 13)));
        var ellipse = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Ellipse(
                new PortablePoint(8, 9),
                6,
                7,
                PortableMatrix3x2.Identity));
        byte[] renderData = CreateDrawGeometryRecord(0, 0, 1)
            .Concat(CreateDrawGeometryRecord(0, 0, 2))
            .ToArray();
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [rectangle, ellipse]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int rectangleOffset = FindCommand(result.Bytes, 0x79);
        Assert.Equal(3U, ReadUInt32(result.Bytes, rectangleOffset + 8));
        Assert.Equal(0.0, ReadDouble(result.Bytes, rectangleOffset + 12));
        Assert.Equal(2.0, ReadDouble(result.Bytes, rectangleOffset + 20));
        Assert.Equal(2.0, ReadDouble(result.Bytes, rectangleOffset + 28));
        Assert.Equal(3.0, ReadDouble(result.Bytes, rectangleOffset + 36));
        Assert.Equal(20.0, ReadDouble(result.Bytes, rectangleOffset + 44));
        Assert.Equal(12.0, ReadDouble(result.Bytes, rectangleOffset + 52));
        Assert.Equal(2U, ReadUInt32(result.Bytes, rectangleOffset + 60));

        int ellipseOffset = FindCommand(result.Bytes, 0x7a);
        Assert.Equal(4U, ReadUInt32(result.Bytes, ellipseOffset + 8));
        Assert.Equal(6.0, ReadDouble(result.Bytes, ellipseOffset + 12));
        Assert.Equal(7.0, ReadDouble(result.Bytes, ellipseOffset + 20));
        Assert.Equal(8.0, ReadDouble(result.Bytes, ellipseOffset + 28));
        Assert.Equal(9.0, ReadDouble(result.Bytes, ellipseOffset + 36));
        Assert.Equal(0U, ReadUInt32(result.Bytes, ellipseOffset + 44));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchTranslatesTypedGeneralPathGeometry()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 192));
        var geometry = new FakeGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.EvenOdd,
            Transform = new PortableMatrix3x2(2, 0, 0, 3, 11, 13),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(1, 2),
                    IsClosed = true,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.Line(
                            new PortablePoint(9, 2),
                            isSmoothJoin: false,
                            isStroked: true),
                        PortablePathSegment.QuadraticBezier(
                            new PortablePoint(9, 8),
                            new PortablePoint(5, 8),
                            isSmoothJoin: true,
                            isStroked: true),
                        PortablePathSegment.CubicBezier(
                            new PortablePoint(3, 8),
                            new PortablePoint(1, 6),
                            new PortablePoint(1, 2),
                            isSmoothJoin: true,
                            isStroked: true)
                    ]
                }
            ]
        });
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(1, 0, 2),
                [brush, geometry]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int geometryOffset = FindCommand(result.Bytes, 0x7d);
        Assert.Equal(4U, ReadUInt32(result.Bytes, geometryOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 16));
        Assert.Equal(232U, ReadUInt32(result.Bytes, geometryOffset + 20));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 28));
        Assert.Equal(1.0, ReadDouble(result.Bytes, geometryOffset + 32));
        Assert.Equal(2.0, ReadDouble(result.Bytes, geometryOffset + 40));
        Assert.Equal(9.0, ReadDouble(result.Bytes, geometryOffset + 48));
        Assert.Equal(8.0, ReadDouble(result.Bytes, geometryOffset + 56));
        Assert.Equal(14U, ReadUInt32(result.Bytes, geometryOffset + 76));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 80));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 112));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 144));
        Assert.Equal(2U, ReadUInt32(result.Bytes, geometryOffset + 192));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 12));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 16));
    }

    [Fact]
    public void BuildBatchTranslatesTypedPathArcRecord()
    {
        var brush = new FakeBrush(new PortableColor(255, 192, 96, 32));
        var geometry = new FakeGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.Nonzero,
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(0, 5),
                    IsClosed = true,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.Arc(
                            new PortablePoint(10, 5),
                            new PortableSize(5, 5),
                            rotationAngle: 30,
                            isLargeArc: false,
                            sweepDirection:
                                PortableSweepDirection.Clockwise,
                            isSmoothJoin: true,
                            isStroked: true),
                        PortablePathSegment.Line(
                            new PortablePoint(0, 5),
                            isSmoothJoin: false,
                            isStroked: true)
                    ]
                }
            ]
        });
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(1, 0, 2),
                [brush, geometry]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int geometryOffset = FindCommand(result.Bytes, 0x7d);
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 12));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 16));
        Assert.Equal(184U, ReadUInt32(result.Bytes, geometryOffset + 20));
        Assert.Equal(4U, ReadUInt32(result.Bytes, geometryOffset + 112));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 120));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 124));
        Assert.Equal(10.0, ReadDouble(result.Bytes, geometryOffset + 128));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 136));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 144));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 152));
        Assert.Equal(30.0, ReadDouble(result.Bytes, geometryOffset + 160));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 168));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 172));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 176));
        Assert.Equal(64U, ReadUInt32(result.Bytes, geometryOffset + 184));
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
    public void BuildBatchPreservesPenOnlyEllipse()
    {
        var pen = new FakePen(
            new PortableColor(255, 64, 128, 255),
            3,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateEllipseRecord(0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 44));
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
    public void BuildBatchPreservesPenOnlyRoundedRectangle()
    {
        var pen = new FakePen(
            new PortableColor(255, 64, 128, 255),
            2,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Round,
            10,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 4, 0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 56));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 60));
    }

    [Fact]
    public void BuildBatchTranslatesNonUniformRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 6, 1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(4.0, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(6.0, ReadDouble(result.Bytes, nestedOffset + 48));
    }

    [Fact]
    public void BuildBatchTranslatesZeroAxisAsymmetricRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(0, 6, 1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0.0, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(6.0, ReadDouble(result.Bytes, nestedOffset + 48));
    }

    [Fact]
    public void BuildBatchFailsClosedForDegenerateZeroAxisAsymmetricRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        byte[] record = CreateRoundedRectangleRecord(0, 6, 1, 0);
        WriteDouble(record, 24, 0);
        var visual = new FakeVisual(new FakeRenderData(record, [brush]));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => new WpfNativeMilSceneCompiler().BuildBatch(visual, 64, 64));

        Assert.Contains("degenerate zero-axis asymmetric", exception.Message);
    }

    [Fact]
    public void BuildBatchReusesTypedMatrixForVisualAndNestedTransform()
    {
        var transform = new FakeTransform(
            new PortableMatrix3x2(2, 0.5, -0.25, 3, 11, 13));
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        byte[] renderData = CreatePushTransformRecord(1)
            .Concat(CreateRectangleRecord(2, 0))
            .Concat(CreatePopRecord())
            .ToArray();
        var state = new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(5, 7),
            HasTransform = true,
            Transform = transform,
            HasOpacity = true,
            Opacity = 0.75
        };
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [transform, brush]),
            state);

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        Assert.Equal(5U, result.TargetHandle);
        Assert.Equal(1, ReadCommands(result.Bytes).Count(
            static command => command == 0x77));

        int matrixOffset = FindCommand(result.Bytes, 0x77);
        Assert.Equal(64, ReadInt32(result.Bytes, matrixOffset));
        Assert.Equal(2U, ReadUInt32(result.Bytes, matrixOffset + 8));
        Assert.Equal(2.0, ReadDouble(result.Bytes, matrixOffset + 12));
        Assert.Equal(0.5, ReadDouble(result.Bytes, matrixOffset + 20));
        Assert.Equal(-0.25, ReadDouble(result.Bytes, matrixOffset + 28));
        Assert.Equal(3.0, ReadDouble(result.Bytes, matrixOffset + 36));
        Assert.Equal(11.0, ReadDouble(result.Bytes, matrixOffset + 44));
        Assert.Equal(13.0, ReadDouble(result.Bytes, matrixOffset + 52));
        Assert.Equal(0U, ReadUInt32(result.Bytes, matrixOffset + 60));

        int visualTransformOffset = FindCommand(result.Bytes, 0x1c);
        Assert.Equal(1U, ReadUInt32(result.Bytes, visualTransformOffset + 8));
        Assert.Equal(2U, ReadUInt32(result.Bytes, visualTransformOffset + 12));

        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;
        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 12));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 56));
    }

    [Fact]
    public void BuildBatchPreservesNullTransformAsBalancedNoOpScope()
    {
        byte[] renderData = CreatePushTransformRecord(0)
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 16, 16);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 20));
    }

    [Fact]
    public void BuildBatchRejectsTransformWithoutTypedPortableContract()
    {
        var state = new PortableVisualState
        {
            HasTransform = true,
            Transform = new object()
        };
        var visual = new FakeVisual(content: null, state: state);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(visual, 16, 16));

        Assert.Contains(nameof(IPortableTransformMatrixSource), exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedSolidPenLine()
    {
        var pen = new FakePen(
            new PortableColor(255, 32, 96, 192),
            2.5,
            PortablePenLineCap.Square,
            PortablePenLineCap.Round,
            PortablePenLineCap.Triangle,
            PortablePenLineJoin.Bevel,
            7,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        Assert.Equal(5U, result.TargetHandle);

        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(56, ReadInt32(result.Bytes, penOffset));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 8));
        Assert.Equal(2.5, ReadDouble(result.Bytes, penOffset + 12));
        Assert.Equal(7.0, ReadDouble(result.Bytes, penOffset + 20));
        Assert.Equal(2U, ReadUInt32(result.Bytes, penOffset + 28));
        Assert.Equal(0U, ReadUInt32(result.Bytes, penOffset + 32));
        Assert.Equal(1U, ReadUInt32(result.Bytes, penOffset + 36));
        Assert.Equal(2U, ReadUInt32(result.Bytes, penOffset + 40));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 44));
        Assert.Equal(1U, ReadUInt32(result.Bytes, penOffset + 48));
        Assert.Equal(0U, ReadUInt32(result.Bytes, penOffset + 52));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x3e, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(1.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(2.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(5.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(8.0, ReadDouble(result.Bytes, nestedOffset + 32));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchPreservesNullPenLineAsNoOpCommand()
    {
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(0), []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 16, 16);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0x3e, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchTranslatesTypedDashedLinePen()
    {
        var pen = new FakePen(
            new PortableColor(255, 255, 255, 255),
            1,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            [2, 1]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 16, 16);

        int dashOffset = FindCommand(result.Bytes, 0x85);
        Assert.Equal(44, ReadInt32(result.Bytes, dashOffset));
        Assert.Equal(3U, ReadUInt32(result.Bytes, dashOffset + 8));
        Assert.Equal(0.0, ReadDouble(result.Bytes, dashOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, dashOffset + 20));
        Assert.Equal(16U, ReadUInt32(result.Bytes, dashOffset + 24));
        Assert.Equal(2.0, ReadDouble(result.Bytes, dashOffset + 28));
        Assert.Equal(1.0, ReadDouble(result.Bytes, dashOffset + 36));

        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(4U, ReadUInt32(result.Bytes, penOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 52));
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchRejectsNegativeDashInterval()
    {
        var pen = new FakePen(
            new PortableColor(255, 255, 255, 255),
            1,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            [2, -1]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [pen]));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(visual, 16, 16));
    }

    [Fact]
    public void BuildBatchRejectsLinePenWithoutTypedPortableContract()
    {
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(visual, 16, 16));

        Assert.Contains(nameof(IPortablePenSource), exception.Message);
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

    private static byte[] CreateLineRecord(uint pen)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x3e);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 2);
        WriteDouble(record, 24, 5);
        WriteDouble(record, 32, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), pen);
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

    private static byte[] CreatePushTransformRecord(uint transform)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x51);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), transform);
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

    private static byte[] CreateDrawGeometryRecord(
        uint brush,
        uint pen,
        uint geometry)
    {
        byte[] record = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x46);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), pen);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), geometry);
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
        private readonly PortableVisualState _state;

        internal FakeVisual(
            object? content,
            PortableVisualState? state = null)
        {
            _content = content;
            _state = state ?? new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            };
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
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

    private sealed class FakeTransform : IPortableTransformMatrixSource
    {
        private readonly PortableMatrix3x2 _matrix;

        internal FakeTransform(PortableMatrix3x2 matrix)
        {
            _matrix = matrix;
        }

        public bool TryGetPortableTransformMatrix(
            out PortableMatrix3x2 matrix)
        {
            matrix = _matrix;
            return true;
        }
    }

    private sealed class FakePen : IPortablePenSource
    {
        private readonly PortablePen _pen;

        internal FakePen(
            PortableColor color,
            double thickness,
            PortablePenLineCap startLineCap,
            PortablePenLineCap endLineCap,
            PortablePenLineCap dashCap,
            PortablePenLineJoin lineJoin,
            double miterLimit,
            double[] dashArray)
        {
            _pen = new PortablePen(
                PortableBrush.SolidColor(color),
                thickness,
                startLineCap,
                endLineCap,
                dashCap,
                lineJoin,
                miterLimit,
                dashArray,
                dashOffset: 0);
        }

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = _pen;
            return true;
        }
    }

    private sealed class FakeGeometry : IPortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        internal FakeGeometry(PortableGeometryPath path)
        {
            _path = path;
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class FakePrimitiveGeometry :
        IPortablePrimitiveGeometrySource
    {
        private readonly PortablePrimitiveGeometry _geometry;

        internal FakePrimitiveGeometry(PortablePrimitiveGeometry geometry)
        {
            _geometry = geometry;
        }

        public bool TryGetPortablePrimitiveGeometry(
            out PortablePrimitiveGeometry geometry)
        {
            geometry = _geometry;
            return true;
        }
    }
}
