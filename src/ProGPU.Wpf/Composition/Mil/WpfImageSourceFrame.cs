using System.Windows;
using System.Windows.Media.Imaging;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>Original source DIPs and the adapted image's independent texel axes.</summary>
internal readonly record struct WpfImageSourceFrame(Rect Bounds, double TexelsPerDipX, double TexelsPerDipY)
{
    internal static bool HasTypedMetrics(object source) => source is IPortableBitmapSourceMetricsSource
        or IPortableNativeImageSource or IPortableBitmapSourcePixelsSource;

    internal Rect ToTexels(Rect bounds) => new(bounds.X * TexelsPerDipX, bounds.Y * TexelsPerDipY,
        bounds.Width * TexelsPerDipX, bounds.Height * TexelsPerDipY);

    internal static bool TryMapViewbox(PortableTileBrush brush, Rect sourceBounds, out Rect bounds)
    {
        bounds = default;
        PortableRect box = brush.Viewbox;
        if (box.IsEmpty || !double.IsFinite(box.X) || !double.IsFinite(box.Y) ||
            !double.IsFinite(box.Width) || box.Width <= 0 || !double.IsFinite(box.Height) || box.Height <= 0)
            return false;
        if (brush.ViewboxUnits == PortableBrushMappingMode.Absolute)
            bounds = new(box.X, box.Y, box.Width, box.Height);
        else if (brush.ViewboxUnits == PortableBrushMappingMode.RelativeToBoundingBox)
            bounds = new(sourceBounds.X + box.X * sourceBounds.Width, sourceBounds.Y + box.Y * sourceBounds.Height,
                box.Width * sourceBounds.Width, box.Height * sourceBounds.Height);
        else return false;
        return double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
            double.IsFinite(bounds.Width) && bounds.Width > 0 && double.IsFinite(bounds.Height) && bounds.Height > 0;
    }

    internal static bool TryRead(object source, ImageSource? adapted, out WpfImageSourceFrame frame)
    {
        frame = default;
        PortableBitmapSourceMetrics metrics;
        if (source is IPortableBitmapSourceMetricsSource metricsSource)
        {
            if (!metricsSource.TryGetPortableBitmapSourceMetrics(out metrics)) return false;
        }
        else if (source is IPortableNativeImageSource native)
            metrics = new(native.PixelWidth, native.PixelHeight, native.DpiX, native.DpiY);
        else if (source is IPortableBitmapSourcePixelsSource pixelsSource)
        {
            // Compatibility for older typed providers only. Source BitmapSource
            // publishes cheap metrics above, so reading its frame never copies pixels.
            if (!pixelsSource.TryGetPortableBitmapSourcePixels(out var pixels)) return false;
            metrics = new(pixels.Width, pixels.Height, pixels.DpiX, pixels.DpiY);
        }
        else if (adapted is BitmapSource legacy)
            metrics = new(legacy.PixelWidth, legacy.PixelHeight, 96, 96);
        else
            return false;

        if (metrics.PixelWidth <= 0 || metrics.PixelHeight <= 0 ||
            !double.IsFinite(metrics.DpiX) || metrics.DpiX <= 0 ||
            !double.IsFinite(metrics.DpiY) || metrics.DpiY <= 0) return false;
        // Original ImageSource.PixelsToDIPs narrows DPI and performs the ratio
        // and multiplication in float before returning double. More precise
        // double arithmetic would change the original source coordinate frame.
        float dpiX = (float)metrics.DpiX, dpiY = (float)metrics.DpiY;
        if (!float.IsFinite(dpiX) || dpiX <= 0 || !float.IsFinite(dpiY) || dpiY <= 0) return false;
        double width = metrics.PixelWidth * (96.0f / dpiX);
        double height = metrics.PixelHeight * (96.0f / dpiY);
        int pixelWidth = adapted is BitmapSource bitmap ? bitmap.PixelWidth : metrics.PixelWidth;
        int pixelHeight = adapted is BitmapSource bitmapHeight ? bitmapHeight.PixelHeight : metrics.PixelHeight;
        double scaleX = pixelWidth / width, scaleY = pixelHeight / height;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0 ||
            !double.IsFinite(scaleX) || scaleX <= 0 || !double.IsFinite(scaleY) || scaleY <= 0) return false;
        frame = new(new Rect(0, 0, width, height), scaleX, scaleY);
        return true;
    }
}
