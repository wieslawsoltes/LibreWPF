using System;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProGPU.Backend;
using ProGPU.DirectX;

namespace ProGPU.Wpf.SciChartApp;

internal sealed record SciChart3DBridgeSnapshot(
    int Width,
    int Height,
    byte[] Pbgra32Pixels,
    int DrawCount,
    ulong SubmittedDrawCount,
    int BrightPixelCount);

internal static class SciChart3DBridgeSnapshotRenderer
{
    private const int Width = 320;
    private const int Height = 220;

    internal static ProGpuDirectXSciChartXyzPoint3D[] CreateSamplePoints(int sampleCount = 96)
    {
        if (sampleCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "SciChart 3D sample data requires at least two points.");
        }

        var points = new ProGpuDirectXSciChartXyzPoint3D[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var amount = sampleCount == 1 ? 0d : i / (double)(sampleCount - 1);
            var angle = amount * Math.PI * 2.0;
            points[i] = new ProGpuDirectXSciChartXyzPoint3D(
                Math.Cos(angle) * 4.0,
                Math.Sin(angle * 3.0),
                Math.Sin(angle) * 4.0,
                LerpColorArgb(0xFF42C6FF, 0xFFFFD166, (float)amount));
        }

        return points;
    }

    internal static SciChart3DBridgeSnapshot Render(ReadOnlySpan<ProGpuDirectXSciChartXyzPoint3D> points)
    {
        using var wgpu = new WgpuContext();
        wgpu.Initialize(null);
        using var device = ProGpuDirectXDevice.FromContext(
            wgpu,
            new ProGpuDirectXDeviceOptions
            {
                Label = "ProGPU WPF SciChart 3D XYZ Bridge",
                MinimumFeatureLevel = DxFeatureLevel.Direct3D9_3,
                RequireGpuBackedResources = true,
                EnableValidation = true
            });

        using var context = new ProGpuDirectXSciChartRenderContext3D(
            device,
            Width,
            Height,
            DxResourceFormat.R8G8B8A8Unorm);
        context.BeginFrame();
        context.Clear(new DxColor(0.025f, 0.04f, 0.07f, 1f));
        context.SetClipRect(new DxRect(8, 8, Width - 16, Height - 16));

        var worldViewProjection =
            Matrix4x4.CreateScale(0.62f, 0.62f, 0.18f) *
            Matrix4x4.CreateRotationX(-0.58f) *
            Matrix4x4.CreateRotationY(0.44f) *
            Matrix4x4.CreateTranslation(0f, 0f, 0.55f);
        var options = new ProGpuDirectXSciChartXyzSeries3DOptions
        {
            ColorArgb = 0xFF42C6FF,
            Normal = new Vector3(0f, 0f, 1f)
        };
        float[] waterfallHeights =
        [
            -0.65f, -0.48f, -0.36f, -0.42f, -0.57f, -0.70f,
            -0.44f, -0.22f, -0.08f, -0.18f, -0.32f, -0.50f,
            -0.26f, -0.04f,  0.12f,  0.04f, -0.10f, -0.30f
        ];

        context.DrawWaterfallDataSeries(
            waterfallHeights,
            columns: 6,
            rows: 3,
            worldViewProjection,
            new ProGpuDirectXSciChartWaterfall3DOptions
            {
                YRange = new ProGpuDirectXSciChartDoubleRange(-0.75d, 0.22d),
                BaseY = -0.9f,
                LowColorArgb = 0xFF1B6CA8,
                HighColorArgb = 0xFFFFD166,
                Normal = new Vector3(0f, 0f, 1f)
            },
            new Vector3(0.25f, 0.45f, 1f),
            DxCullMode.None);
        context.DrawXyzDataSeriesLineStrip(
            points,
            worldViewProjection,
            options,
            new Vector3(0.25f, 0.45f, 1f));
        context.DrawXyzDataSeriesRibbon(
            points,
            worldViewProjection,
            halfThickness: 0.035f,
            options,
            new Vector3(0.25f, 0.45f, 1f),
            DxCullMode.None);
        context.DrawXyzDataSeriesPointCloud(
            points,
            worldViewProjection,
            options with { ColorArgb = 0xFFFFF4B8 },
            new Vector3(0.25f, 0.45f, 1f));
        context.Flush();

        var pixels = ConvertRgbaToPbgra(context.ReadTargetPixels(), Width, Height);
        return new SciChart3DBridgeSnapshot(
            Width,
            Height,
            pixels,
            context.LineDraws.Count + context.TriangleStripDraws.Count + context.PointCloudDraws.Count + context.WaterfallDraws.Count,
            context.ImmediateContext.SubmittedDrawCount,
            CountBrightPixels(pixels));
    }

    internal static void Validate(SciChart3DBridgeSnapshot snapshot)
    {
        if (snapshot.Width <= 0 || snapshot.Height <= 0)
        {
            throw new InvalidOperationException("SciChart 3D XYZ bridge produced an empty bitmap.");
        }

        if (snapshot.Pbgra32Pixels.Length != checked(snapshot.Width * snapshot.Height * 4))
        {
            throw new InvalidOperationException("SciChart 3D XYZ bridge produced a bitmap with an invalid stride.");
        }

        if (snapshot.DrawCount < 4 || snapshot.SubmittedDrawCount < 4)
        {
            throw new InvalidOperationException("Expected SciChart 3D bridge to submit native waterfall, line, ribbon, and point draws.");
        }

        if (snapshot.BrightPixelCount < 40)
        {
            throw new InvalidOperationException(
                $"Expected visible SciChart 3D XYZ bridge pixels, but found only {snapshot.BrightPixelCount} bright pixels.");
        }
    }

    internal static BitmapSource CreateBitmap(SciChart3DBridgeSnapshot snapshot)
    {
        var bitmap = new WriteableBitmap(
            snapshot.Width,
            snapshot.Height,
            96,
            96,
            PixelFormats.Pbgra32,
            null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, snapshot.Width, snapshot.Height),
            snapshot.Pbgra32Pixels,
            checked(snapshot.Width * 4),
            0);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] ConvertRgbaToPbgra(byte[] sourceRgba, int width, int height)
    {
        var destination = new byte[checked(width * height * 4)];
        for (var i = 0; i < destination.Length; i += 4)
        {
            destination[i] = sourceRgba[i + 2];
            destination[i + 1] = sourceRgba[i + 1];
            destination[i + 2] = sourceRgba[i];
            destination[i + 3] = sourceRgba[i + 3];
        }

        return destination;
    }

    private static int CountBrightPixels(ReadOnlySpan<byte> pbgra32Pixels)
    {
        var count = 0;
        for (var i = 0; i < pbgra32Pixels.Length; i += 4)
        {
            var b = pbgra32Pixels[i];
            var g = pbgra32Pixels[i + 1];
            var r = pbgra32Pixels[i + 2];
            var a = pbgra32Pixels[i + 3];
            if (a > 200 && (r > 80 || g > 80 || b > 80))
            {
                count++;
            }
        }

        return count;
    }

    private static uint LerpColorArgb(uint startArgb, uint endArgb, float amount)
    {
        static byte LerpChannel(uint startArgb, uint endArgb, int shift, float amount)
        {
            var start = (int)((startArgb >> shift) & 0xFF);
            var end = (int)((endArgb >> shift) & 0xFF);
            return (byte)Math.Clamp((int)MathF.Round(start + ((end - start) * amount)), 0, 255);
        }

        return ((uint)LerpChannel(startArgb, endArgb, 24, amount) << 24) |
            ((uint)LerpChannel(startArgb, endArgb, 16, amount) << 16) |
            ((uint)LerpChannel(startArgb, endArgb, 8, amount) << 8) |
            LerpChannel(startArgb, endArgb, 0, amount);
    }
}
