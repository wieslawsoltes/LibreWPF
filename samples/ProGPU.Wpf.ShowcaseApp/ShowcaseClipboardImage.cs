// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProGPU.Wpf.ShowcaseApp;

// Compiled unchanged by the actual Showcase package consumer and source tests.
// CF_BITMAP carries opaque RGB, not an alpha/DPI-preserving interchange format.
internal static class ShowcaseClipboardImage
{
    internal static BitmapSource CreateSource(bool indexed = false)
    {
        if (indexed)
        {
            BitmapPalette palette = new([System.Windows.Media.Color.FromRgb(209, 83, 17), System.Windows.Media.Color.FromRgb(5, 47, 231)]);
            return BitmapSource.Create(3, 2, 144, 192, PixelFormats.Indexed8, palette,
                new byte[] { 0, 1, 1, 99, 1, 0, 0, 99 }, 4);
        }

        // Three pixels plus padding per row, unequal channels, asymmetric rows.
        return BitmapSource.Create(3, 2, 144, 192, PixelFormats.Bgra32, null,
            new byte[]
            {
                17, 83, 209, 255, 231, 47, 5, 255, 231, 47, 5, 255, 99, 99, 99, 99,
                231, 47, 5, 255, 17, 83, 209, 255, 17, 83, 209, 255, 99, 99, 99, 99
            }, 16);
    }

    internal static void Verify(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth != 3 || bitmap.PixelHeight != 2
            || (bitmap.Format != PixelFormats.Bgr32 && bitmap.Format != PixelFormats.Bgra32))
            throw new System.InvalidOperationException("The clipboard did not return the expected opaque 3×2 bitmap.");

        byte[] pixels = new byte[24];
        bitmap.CopyPixels(pixels, 12, 0);
        int[] indices = [0, 1, 1, 1, 0, 0];
        for (int i = 0; i < indices.Length; i++)
        {
            bool first = indices[i] == 0;
            if (pixels[i * 4] != (first ? 17 : 231)
                || pixels[i * 4 + 1] != (first ? 83 : 47)
                || pixels[i * 4 + 2] != (first ? 209 : 5))
                throw new System.InvalidOperationException($"Clipboard RGB/orientation mismatch at pixel {i}.");
        }
    }

    internal static BitmapSource ExerciseLifetime(bool indexed)
    {
        if (!System.OperatingSystem.IsWindows())
            throw new System.PlatformNotSupportedException("This gate requires the real Windows OLE clipboard.");

        try
        {
            WriteableBitmap source = new(CreateSource(indexed));
            Clipboard.SetImage(source); // SetImage flushes the real OLE object.
            source.WritePixels(new Int32Rect(0, 0, 3, 2), new byte[indexed ? 6 : 24], indexed ? 3 : 12, 0);
            if (!Clipboard.ContainsImage())
                throw new System.InvalidOperationException("CF_BITMAP was not published by the source DataObject.");
            BitmapSource copy = Clipboard.GetImage()
                ?? throw new System.InvalidOperationException("The Windows OLE clipboard did not return its bitmap.");
            Verify(copy);
            Clipboard.Clear();
            Verify(copy); // OLE source/medium lifetime has ended.
            copy.Freeze();
            Clipboard.SetImage(copy);
            Clipboard.Flush();
            BitmapSource republished = Clipboard.GetImage()
                ?? throw new System.InvalidOperationException("Republishing the imported bitmap failed.");
            Verify(republished);
            return republished;
        }
        finally
        {
            Clipboard.Clear();
        }
    }
}
