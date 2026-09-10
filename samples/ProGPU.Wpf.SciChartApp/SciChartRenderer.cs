using System;
using System.Numerics;
using ProGPU.Backend;
using ProGPU.DirectX;

namespace ProGPU.Wpf.SciChartApp;

internal sealed record SciChartShowcaseRenderResult(
    int Width,
    int Height,
    byte[] Pbgra32Pixels,
    int TwoDimensionalDraws,
    int ThreeDimensionalDraws,
    ulong SubmittedDrawCount,
    ulong SubmittedClearCount,
    string BackendSummary);

internal static class SciChartRenderer
{
    private const int Chart2DWidth = 500;
    private const int ChartHeight = 300;
    private const int Chart3DWidth = 300;
    private const int Gap = 18;
    private const int Margin = 18;

    internal static SciChartShowcaseRenderResult Render()
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(
            wgpu,
            new ProGpuDirectXDeviceOptions
            {
                Label = "ProGPU WPF SciChart Showcase",
                MinimumFeatureLevel = DxFeatureLevel.Direct3D9_3,
                RequireGpuBackedResources = true,
                EnableValidation = true
            });

        using var context2D = new ProGpuDirectXSciChartRenderContext2D(
            device,
            Chart2DWidth,
            ChartHeight,
            DxResourceFormat.R8G8B8A8Unorm);
        Render2D(context2D);
        var pixels2D = context2D.ReadTargetPixels();

        using var context3D = new ProGpuDirectXSciChartRenderContext3D(
            device,
            Chart3DWidth,
            ChartHeight,
            DxResourceFormat.R8G8B8A8Unorm);
        Render3D(context3D);
        var pixels3D = context3D.ReadTargetPixels();

        var width = checked(Margin + Chart2DWidth + Gap + Chart3DWidth + Margin);
        var height = checked(Margin + ChartHeight + Margin);
        var output = CreateBackground(width, height, b: 0x1C, g: 0x17, r: 0x11);
        CopyRgbaToPbgra(pixels2D, Chart2DWidth, ChartHeight, output, width, Margin, Margin);
        CopyRgbaToPbgra(pixels3D, Chart3DWidth, ChartHeight, output, width, Margin + Chart2DWidth + Gap, Margin);

        return new SciChartShowcaseRenderResult(
            width,
            height,
            output,
            context2D.LineBatchDraws.Count + context2D.MountainBatchDraws.Count + context2D.ColumnBatchDraws.Count +
                context2D.ColoredSpriteDraws.Count + context2D.FinancialBatchDraws.Count +
                context2D.VerticalPixelsDraws.Count +
                context2D.ShapedHeatmapDraws.Count + context2D.PrimitiveDraws.Count,
            context3D.PointCloudDraws.Count + context3D.LineDraws.Count + context3D.MeshDraws.Count +
                context3D.TriangleStripDraws.Count + context3D.SurfaceMeshDraws.Count + context3D.WaterfallDraws.Count,
            context2D.ImmediateContext.SubmittedDrawCount + context3D.ImmediateContext.SubmittedDrawCount,
            context2D.ImmediateContext.SubmittedClearCount + context3D.ImmediateContext.SubmittedClearCount,
            $"Backend: GPU-backed DirectX shim, clears {context2D.ImmediateContext.SubmittedClearCount + context3D.ImmediateContext.SubmittedClearCount}");
    }

    private static void Render2D(ProGpuDirectXSciChartRenderContext2D context)
    {
        context.BeginFrame();
        context.Clear(new DxColor(0.03f, 0.045f, 0.07f, 1f));
        context.SetClipRect(new DxRect(10, 10, Chart2DWidth - 20, ChartHeight - 20));
        context.FillRectangle(
            0xFF162233,
            new ProGpuDirectXSciChartPoint(12, 12),
            new ProGpuDirectXSciChartPoint(Chart2DWidth - 12, ChartHeight - 12));
        context.FillRectangle(
            0xFF0E1520,
            new ProGpuDirectXSciChartPoint(46, 34),
            new ProGpuDirectXSciChartPoint(284, 254));
        context.FillRectangle(
            0xFF0D1B22,
            new ProGpuDirectXSciChartPoint(314, 34),
            new ProGpuDirectXSciChartPoint(468, 148));

        var gridPen = context.CreatePen(0x446D7890, 1f, false);
        for (int i = 0; i <= 5; i++)
        {
            float y = 54 + i * 36;
            context.DrawLine(gridPen, new ProGpuDirectXSciChartPoint(46, y), new ProGpuDirectXSciChartPoint(284, y));
        }

        var mountain = new ProGpuDirectXSciChartBandVertex[18];
        var line = new ProGpuDirectXSciChartColoredVertex[mountain.Length];
        for (int i = 0; i < mountain.Length; i++)
        {
            float x = 54 + i * 13.2f;
            float signal = MathF.Sin(i * 0.58f) * 34f + MathF.Cos(i * 0.24f) * 18f;
            float y = 150 - signal;
            mountain[i] = new ProGpuDirectXSciChartBandVertex(x, y, 238);
            line[i] = new ProGpuDirectXSciChartColoredVertex(x, y, 0, 0xFF42C6FF);
        }

        context.DrawMountainBatch(
            mountain,
            mountain.Length,
            context.CreatePen(0xFF39A9F4, 2f),
            context.CreateLinearGradientBrush(0x9335C2FF, 0x182087FF, 90),
            isDigital: false,
            default);
        context.DrawLineStrip(
            context.CreatePen(0xFFEDEFF7, 2.25f),
            line,
            startIndex: 0,
            count: line.Length,
            transform: default);
        using var markerSprite = context.CreateSprite(7, 7);
        markerSprite.SetData(CreateMarkerSprite(7));
        ProGpuDirectXSciChartColoredVertex[] markers =
        [
            new(line[1].X, line[1].Y, 0, 0xFF23B87D),
            new(line[5].X, line[5].Y, 0, 0xFFFFD166),
            new(line[9].X, line[9].Y, 0, 0xFFE15759),
            new(line[13].X, line[13].Y, 0, 0xFF42C6FF),
            new(line[16].X, line[16].Y, 0, 0xFFB18CFF)
        ];
        context.DrawColoredSprites(
            markerSprite,
            markers,
            startIndex: 0,
            count: markers.Length,
            transform: default,
            centeredAmount: 0.5f,
            filtering: ProGpuDirectXSciChartTextureFiltering.Point);

        ProGpuDirectXSciChartColumnVertex[] columns =
        [
            new(74, 236, 18, -70, 0xFF23B87D, 0xFF94F0CE),
            new(112, 236, 18, -96, 0xFF23B87D, 0xFF94F0CE),
            new(150, 236, 18, -52, 0xFFE15759, 0xFFFFB2B4),
            new(188, 236, 18, -118, 0xFF23B87D, 0xFF94F0CE),
            new(226, 236, 18, -82, 0xFF23B87D, 0xFF94F0CE)
        ];
        context.DrawColumnsBatch(columns, columns.Length, default);

        ProGpuDirectXSciChartOhlcCandleVertex[] candles =
        [
            new(326, 118, 60, 132, 82, 0xFF23B87D, 0xFF8BF2D3),
            new(360, 84, 66, 132, 120, 0xFFE15759, 0xFFFFB2B4),
            new(394, 124, 76, 140, 92, 0xFF23B87D, 0xFF8BF2D3),
            new(428, 92, 58, 126, 74, 0xFF23B87D, 0xFF8BF2D3)
        ];
        context.DrawCandlesBatch(candles, candles.Length, width: 18, default);
        DrawVerticalPixelStrips(context);
        using var heightsTexture = context.CreateTexture(8, 8, ProGpuDirectXSciChartTextureFormat.Float32);
        using var gradientTexture = context.CreateTexture(5, 1);
        DrawHeatmap(context, heightsTexture, gradientTexture);
        context.Flush();
    }

    private static int[] CreateMarkerSprite(int size)
    {
        var pixels = new int[checked(size * size)];
        var center = (size - 1) * 0.5f;
        var radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                pixels[(y * size) + x] = (dx * dx) + (dy * dy) <= radius * radius
                    ? unchecked((int)0xFFFFFFFF)
                    : 0;
            }
        }

        return pixels;
    }

    private static void DrawVerticalPixelStrips(ProGpuDirectXSciChartRenderContext2D context)
    {
        int[] gradientPixels =
        [
            unchecked((int)0xFF243A8F),
            unchecked((int)0xFF1668C7),
            unchecked((int)0xFF20B486),
            unchecked((int)0xFFFFD166),
            unchecked((int)0xFFE15759),
            unchecked((int)0xFFFF9F1C),
            unchecked((int)0xFFED6A5A),
            unchecked((int)0xFFB24C63)
        ];
        context.DrawPixelsVertically(
            xLeft: 292,
            xRight: 304,
            yStartBottom: 254,
            yEndTop: 34,
            gradientPixels,
            opacity: 0.92d,
            yAxisIsFlipped: false);

        int[] yCoordinates = [34, 68, 102, 136, 176, 214, 254];
        int[] runColors =
        [
            unchecked((int)0xFF42C6FF),
            unchecked((int)0xFF23B87D),
            unchecked((int)0xFFFFD166),
            unchecked((int)0xFFE15759),
            unchecked((int)0xFFB18CFF),
            unchecked((int)0xFF5AC8FA)
        ];
        context.DrawPixelsVertically(
            xLeft: 306,
            xRight: 312,
            yCoordinates,
            runColors,
            opacity: 0.86d,
            isUniform: false,
            yAxisIsFlipped: true);
    }

    private static void DrawHeatmap(
        ProGpuDirectXSciChartRenderContext2D context,
        ProGpuDirectXSciChartTexture2D heightsTexture,
        ProGpuDirectXSciChartTexture2D gradientTexture)
    {
        var heights = new float[64];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                heights[y * 8 + x] = 0.5f + 0.5f * MathF.Sin((x * 0.65f) + (y * 0.35f));
            }
        }

        heightsTexture.SetFloatData(heights);
        gradientTexture.SetData(
        [
            unchecked((int)0xFF243A8F),
            unchecked((int)0xFF1668C7),
            unchecked((int)0xFF20B486),
            unchecked((int)0xFFFFD166),
            unchecked((int)0xFFE15759)
        ]);

        ProGpuDirectXSciChartTextureVertex[] heatmapQuad =
        [
            new(314, 166, 0, 0, 0xFFFFFFFF),
            new(468, 166, 1, 0, 0xFFFFFFFF),
            new(468, 254, 1, 1, 0xFFFFFFFF),
            new(314, 166, 0, 0, 0xFFFFFFFF),
            new(468, 254, 1, 1, 0xFFFFFFFF),
            new(314, 254, 0, 1, 0xFFFFFFFF)
        ];
        context.DrawShapedHeatmap(
            heatmapQuad,
            startIndex: 0,
            count: heatmapQuad.Length,
            colorMapMin: 0,
            colorMapMax: 1,
            heightsTexture,
            gradientTexture,
            ProGpuDirectXSciChartTextureFiltering.Linear);
    }

    private static void Render3D(ProGpuDirectXSciChartRenderContext3D context)
    {
        context.BeginFrame();
        context.Clear(new DxColor(0.025f, 0.04f, 0.07f, 1f));
        context.SetClipRect(new DxRect(8, 8, Chart3DWidth - 16, ChartHeight - 16));

        const int columns = 18;
        const int rows = 18;
        var heights = new float[columns * rows];
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                float normalizedX = (x / (float)(columns - 1) - 0.5f) * 2f;
                float normalizedZ = (z / (float)(rows - 1) - 0.5f) * 2f;
                heights[z * columns + x] =
                    0.38f * MathF.Sin(normalizedX * MathF.PI * 1.7f) * MathF.Cos(normalizedZ * MathF.PI * 1.4f);
            }
        }

        var worldViewProjection =
            Matrix4x4.CreateScale(0.78f, 0.72f, 1f) *
            Matrix4x4.CreateRotationX(-0.55f) *
            Matrix4x4.CreateRotationY(0.38f);
        context.DrawSurfaceMesh(
            heights,
            columns,
            rows,
            worldViewProjection,
            xRange: new Vector2(-0.92f, 0.92f),
            zRange: new Vector2(-0.42f, 0.58f),
            lowColorArgb: 0xFF1668C7,
            highColorArgb: 0xFFFFD166,
            lightDirection: new Vector3(-0.3f, -0.6f, -1f),
            cullMode: DxCullMode.None);

        const int waterfallColumns = 14;
        const int waterfallRows = 6;
        var waterfallHeights = new float[waterfallColumns * waterfallRows];
        for (int row = 0; row < waterfallRows; row++)
        {
            for (int column = 0; column < waterfallColumns; column++)
            {
                float normalizedX = (column / (float)(waterfallColumns - 1) - 0.5f) * 2f;
                waterfallHeights[(row * waterfallColumns) + column] =
                    -0.58f + (0.11f * row) + (0.24f * MathF.Sin((normalizedX * MathF.PI * 1.35f) + (row * 0.42f)));
            }
        }

        context.DrawWaterfallDataSeries(
            waterfallHeights,
            waterfallColumns,
            waterfallRows,
            worldViewProjection,
            new ProGpuDirectXSciChartWaterfall3DOptions
            {
                YRange = new ProGpuDirectXSciChartDoubleRange(-0.84d, 0.34d),
                BaseY = -0.86f,
                LowColorArgb = 0xFF1B6CA8,
                HighColorArgb = 0xFFFFD166,
                Normal = new Vector3(0f, 0f, 1f)
            },
            new Vector3(0.18f, 0.38f, 1f),
            DxCullMode.None);

        var xyzSeries = SciChart3DBridgeSnapshotRenderer.CreateSamplePoints();
        var xyzOptions = new ProGpuDirectXSciChartXyzSeries3DOptions
        {
            ColorArgb = 0xFF78DCE8,
            Normal = new Vector3(0, 0, 1)
        };
        context.DrawXyzDataSeriesLineStrip(
            xyzSeries,
            worldViewProjection,
            xyzOptions,
            new Vector3(0, 0, 1));
        context.DrawXyzDataSeriesRibbon(
            xyzSeries,
            worldViewProjection,
            halfThickness: 0.028f,
            xyzOptions with { ColorArgb = 0xCC78DCE8 },
            new Vector3(0, 0, 1),
            DxCullMode.None);

        ProGpuDirectXSciChartVertex3D[] strip =
        [
            new(-0.76f, -0.62f, -0.05f, 0, 0, 1, 0x8896F2D7),
            new(-0.30f, -0.58f,  0.02f, 0, 0, 1, 0x8896F2D7),
            new(-0.68f, -0.32f,  0.10f, 0, 0, 1, 0x8896F2D7),
            new(-0.18f, -0.28f,  0.18f, 0, 0, 1, 0x8896F2D7)
        ];
        context.DrawTriangleStrip(strip, worldViewProjection, new Vector3(0, 0, -1), DxCullMode.None);

        context.DrawXyzDataSeriesPointCloud(
            xyzSeries,
            worldViewProjection,
            xyzOptions with { ColorArgb = 0xFFFFF4B8 },
            new Vector3(0, 0, 1));
        context.Flush();
    }

    private static byte[] CreateBackground(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = 0xFF;
        }

        return pixels;
    }

    private static void CopyRgbaToPbgra(
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        byte[] destinationPbgra,
        int destinationWidth,
        int destinationX,
        int destinationY)
    {
        for (int y = 0; y < sourceHeight; y++)
        {
            for (int x = 0; x < sourceWidth; x++)
            {
                int source = checked(((y * sourceWidth) + x) * 4);
                int destination = checked((((destinationY + y) * destinationWidth) + destinationX + x) * 4);
                destinationPbgra[destination] = sourceRgba[source + 2];
                destinationPbgra[destination + 1] = sourceRgba[source + 1];
                destinationPbgra[destination + 2] = sourceRgba[source];
                destinationPbgra[destination + 3] = sourceRgba[source + 3];
            }
        }
    }
}

internal static class SciChartShowcaseSelfTest
{
    internal static void Validate(SciChartShowcaseRenderResult result)
    {
        if (result.Width <= 0 || result.Height <= 0)
        {
            throw new InvalidOperationException("SciChart Showcase renderer produced an empty bitmap.");
        }

        if (result.Pbgra32Pixels.Length != checked(result.Width * result.Height * 4))
        {
            throw new InvalidOperationException("SciChart Showcase renderer produced a bitmap with an invalid stride.");
        }

        if (result.TwoDimensionalDraws < 8 || result.ThreeDimensionalDraws < 6)
        {
            throw new InvalidOperationException("Expected both 2D and 3D SciChart bridge draws to be recorded.");
        }

        if (result.SubmittedDrawCount < 8 || result.SubmittedClearCount < 2)
        {
            throw new InvalidOperationException("Expected SciChart Showcase renderer to submit GPU-backed draw and clear work.");
        }

        int nonBackgroundPixels = 0;
        for (int i = 0; i < result.Pbgra32Pixels.Length; i += 4)
        {
            byte b = result.Pbgra32Pixels[i];
            byte g = result.Pbgra32Pixels[i + 1];
            byte r = result.Pbgra32Pixels[i + 2];
            byte a = result.Pbgra32Pixels[i + 3];
            if (a > 200 && (r > 80 || g > 80 || b > 80))
            {
                nonBackgroundPixels++;
            }
        }

        if (nonBackgroundPixels < 900)
        {
            throw new InvalidOperationException(
                $"Expected visible SciChart Showcase chart pixels, but found only {nonBackgroundPixels} bright pixels.");
        }
    }
}
