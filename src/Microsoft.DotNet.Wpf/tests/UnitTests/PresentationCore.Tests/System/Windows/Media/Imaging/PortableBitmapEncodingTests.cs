// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Media.Imaging;

// Run in separate processes with LIBREWPF_TEST_MEDIA_BACKEND=Portable and
// WindowsMil. Selection is frozen by the shared test module before WPF startup.
public sealed class PortableBitmapEncodingTests
{
    [Fact]
    public void IndexedFramePreservesOwnedPixelsPaletteDpiAndCloneState()
    {
        Color[] colors = [Colors.Black, Colors.Red, Colors.Green, Colors.White];
        var palette = new BitmapPalette(colors);
        colors[0] = Colors.Blue;
        Assert.Equal(Colors.Black, palette.Colors[0]);

        byte[] pixels = [0, 1, 99, 99, 2, 3, 99, 99];
        var source = new WriteableBitmap(BitmapSource.Create(
            2, 2, 144, 192, PixelFormats.Indexed8, palette, pixels, 4));
        var frame = BitmapFrame.Create(source);
        var clone = frame.Clone();
        var currentClone = frame.CloneCurrentValue();
        Assert.Equal(BitmapSource.UsesPortablePixelStorage, frame._managedPixelBuffer != null);
        if (BitmapSource.UsesPortablePixelStorage)
        {
            Assert.NotSame(source._managedPixelBuffer, frame._managedPixelBuffer);
            Assert.NotSame(frame._managedPixelBuffer, clone._managedPixelBuffer);
            Assert.NotSame(frame._managedPixelBuffer, currentClone._managedPixelBuffer);
            Assert.Same(palette, BitmapPalette.CreateFromBitmapSource(frame));
            var copiedPalette = new BitmapPalette(frame, 3);
            Assert.Equal(new[] { Colors.Black, Colors.Red, Colors.Green }, copiedPalette.Colors);
            Assert.Throws<PlatformNotSupportedException>(() => palette.InternalPalette);
            Assert.Throws<PlatformNotSupportedException>(() => BitmapPalette.CreateInternalPalette());

            // Portable frames own a snapshot, not the source's mutable storage.
            source.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 3 }, 1, 0);
        }
        else
        {
            Assert.False(palette.InternalPalette.IsInvalid);
        }

        foreach (BitmapSource copy in new BitmapSource[] { frame, clone, currentClone })
        {
            Assert.Equal(PixelFormats.Indexed8, copy.Format);
            Assert.Equal(144, copy.DpiX);
            Assert.Equal(192, copy.DpiY);
            Assert.Equal(palette.Colors, copy.Palette.Colors);
            byte[] output = new byte[4];
            copy.CopyPixels(output, 2, 0);
            Assert.Equal(new byte[] { 0, 1, 2, 3 }, output);
            copy.Freeze();
            Assert.True(copy.IsFrozen);
        }
    }

    [Fact]
    public void PackageImagingPathSavesAndDecodesIndexedBmp()
    {
        var palette = new BitmapPalette(new[] { Colors.Black, Colors.Red, Colors.Green, Colors.White });
        byte[] pixels = [0, 1, 2, 3];
        var source = BitmapSource.Create(2, 2, 144, 192, PixelFormats.Indexed8, palette, pixels, 2);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        byte[] encoded = stream.ToArray();
        Assert.Equal((byte)'B', encoded[0]);
        Assert.Equal((byte)'M', encoded[1]);
        long savedLength = stream.Length;
        Assert.Throws<InvalidOperationException>(() => encoder.Save(stream));
        Assert.Equal(savedLength, stream.Length);

        stream.Position = 0;
        var decoded = new BmpBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        stream.Dispose();
        Assert.Equal(BitmapSource.UsesPortablePixelStorage, decoded._managedPixelBuffer != null);
        Assert.Equal(2, decoded.PixelWidth);
        Assert.Equal(2, decoded.PixelHeight);
        Assert.InRange(Math.Abs(decoded.DpiX - 144), 0, 0.03);
        Assert.InRange(Math.Abs(decoded.DpiY - 192), 0, 0.03);
        Assert.Equal(PixelFormats.Indexed8, decoded.Format);
        byte[] actual = new byte[4];
        decoded.CopyPixels(actual, 2, 0);
        Assert.Equal(pixels, actual);
        for (int i = 0; i < palette.Colors.Count; i++)
        {
            Assert.Equal(palette.Colors[i], decoded.Palette.Colors[i]);
        }
    }

    [Fact]
    public void PortablePaletteAnalysisAndUnsupportedCodecAdmissionStayManaged()
    {
        if (!BitmapSource.UsesPortablePixelStorage)
        {
            return; // WIC palette ordering/quantization is a separate native contract.
        }

        byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 255];
        var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var palette = new BitmapPalette(source, 2);
        Assert.Equal(new[] { Color.FromRgb(3, 2, 1), Color.FromRgb(6, 5, 4) }, palette.Colors);
        Assert.Equal(4, BitmapPalettes.Gray4.Colors.Count);
        Assert.Equal(Color.FromRgb(85, 85, 85), BitmapPalettes.Gray4.Colors[1]);
        Assert.Equal(216, BitmapPalettes.WebPalette.Colors.Count);
        Assert.Throws<PlatformNotSupportedException>(() => BitmapPalettes.Gray4.InternalPalette);

        var missingPixels = new BitmapImage();
        Assert.Throws<PlatformNotSupportedException>(() => BitmapFrame.Create(missingPixels, null, null, null));
        Assert.Throws<PlatformNotSupportedException>(() => new BitmapPalette(missingPixels, 2));

        BitmapEncoder[] unsupported = [new PngBitmapEncoder(), new JpegBitmapEncoder(), new GifBitmapEncoder(), new TiffBitmapEncoder()];
        foreach (BitmapEncoder encoder in unsupported)
        {
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            Assert.Throws<PlatformNotSupportedException>(() => encoder.Save(stream));
            Assert.Equal(0, stream.Length);
            Assert.Throws<PlatformNotSupportedException>(() => encoder.CodecInfo);
        }
        Assert.Throws<PlatformNotSupportedException>(() => new BmpBitmapEncoder().CodecInfo);
    }
}
