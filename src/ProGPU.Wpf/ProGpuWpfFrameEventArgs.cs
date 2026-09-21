using System;
using MediaDrawingContext = System.Windows.Media.DrawingContext;

namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfFrameEventArgs : EventArgs
{
    public ProGpuWpfFrameEventArgs(
        MediaDrawingContext? drawingContext,
        uint pixelWidth,
        uint pixelHeight,
        double deltaSeconds,
        double dpiScale,
        ProGpuWpfDrawingFrame? drawingFrame = null)
    {
        DrawingContext = drawingContext;
        DrawingFrame = drawingFrame;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DeltaSeconds = deltaSeconds;
        DpiScale = dpiScale;
    }

    public MediaDrawingContext? DrawingContext { get; }

    public ProGpuWpfDrawingFrame? DrawingFrame { get; }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }

    public double DeltaSeconds { get; }

    public double DpiScale { get; }
}
