// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Windows.Media.Imaging;

[Collection("WriteableBitmapTests")]
public sealed class WriteableBitmapTests
{
    [Fact]
    public void PixelStorageFollowsMediaSelectionAndClonesRemainIndependent()
    {
        byte[] initial = [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255];
        BitmapSource source = BitmapSource.Create(2, 2, 144, 192, PixelFormats.Pbgra32, null, initial, 8);
        Assert.Equal(BitmapSource.UsesPortablePixelStorage, source._managedPixelBuffer != null);
        var bitmap = new WriteableBitmap(source);
        var clone = bitmap.Clone();
        var currentClone = bitmap.CloneCurrentValue();
        Assert.Equal(BitmapSource.UsesPortablePixelStorage, bitmap._managedPixelBuffer != null);
        Assert.Equal(BitmapSource.UsesPortablePixelStorage, clone._managedPixelBuffer != null);
        Assert.NotSame(bitmap._managedPixelBuffer ?? (object)bitmap, clone._managedPixelBuffer ?? (object)clone);
        bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 20, 21, 22, 255 }, 4, 0);
        foreach (BitmapSource unchanged in new BitmapSource[] { source, clone, currentClone })
        {
            byte[] copy = new byte[16];
            unchanged.CopyPixels(copy, 8, 0);
            Assert.Equal(initial, copy);
            Assert.Equal(144, unchanged.DpiX); Assert.Equal(192, unchanged.DpiY);
        }
        clone.Freeze();
        Assert.True(clone.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => clone.WritePixels(new Int32Rect(0, 0, 1, 1), initial, 8, 0));
    }

    [Fact]
    public void NestedLocksKeepTheAddressStableAndPublishOneDirtyNotification()
    {
        var bitmap = new WriteableBitmap(2, 2, 144, 192, PixelFormats.Pbgra32, null);
        int changed = 0;
        bitmap.Changed += (_, _) => changed++;
        bitmap.Lock();
        IntPtr address = bitmap.BackBuffer;
        try
        {
            bitmap.Lock();
            try
            {
                Assert.NotEqual(IntPtr.Zero, address);
                Marshal.WriteInt32(address, unchecked((int)0xff030201));
                bitmap.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
                Assert.False(bitmap.CanFreeze);
                GC.Collect();
                Assert.Equal(address, bitmap.BackBuffer);
                Assert.Equal(unchecked((int)0xff030201), Marshal.ReadInt32(bitmap.BackBuffer));
            }
            finally { bitmap.Unlock(); }
            Assert.Equal(0, changed);
        }
        finally { bitmap.Unlock(); }
        Assert.Equal(1, changed);
        bitmap.Lock();
        try { Assert.Equal(address, bitmap.BackBuffer); }
        finally { bitmap.Unlock(); }
        Assert.Equal(1, changed); // An unmodified lock does not publish a frame.
        byte[] copy = new byte[16];
        bitmap.CopyPixels(copy, 8, 0);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, copy[..4]);
        bitmap.Freeze();
        Assert.True(bitmap.IsFrozen);
    }

    // Under 2GB back-buffer (4 channels)
    [InlineData(128, 128, 96.0, 96.0)]
    [InlineData(256, 512, 96.0, 96.0)]
    [InlineData(256, 256, 120.0, 120.0)]
    [InlineData(512, 256, 120.0, 120.0)]
    [InlineData(10_000, 10_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(20_000, 20_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    // Over 2GB back-buffer (4 channels) -- NOTE: These tests shall not be run on x86 without PAE
    [InlineData(25_000, 25_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(30_000, 30_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(32_000, 32_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [Theory]
    public void Constructor_CreationSucceeds_HasCorrectParameters(int width, int height, double dpiX, double dpiY)
    {
        WriteableBitmap writeableBitmap = new(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

        // Assert
        Assert.Equal(width, writeableBitmap.PixelWidth);
        Assert.Equal(height, writeableBitmap.PixelHeight);

        Assert.Equal(dpiX, writeableBitmap.DpiX);
        Assert.Equal(dpiY, writeableBitmap.DpiY);

        Assert.Equal(PixelFormats.Pbgra32, writeableBitmap.Format);
    }

    // Under 2GB back-buffer (4 channels)
    [InlineData(2_000, 2_000, 96.0, 96.0)]
    [InlineData(4_000, 4_000, 120, 120)]
    [InlineData(10_000, 10_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(20_000, 20_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    // Over 2GB back-buffer (4 channels) -- NOTE: These tests shall not be run on x86 without PAE
    [InlineData(25_000, 25_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(32_000, 32_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [Theory]
    public void WritePixels_SmallRect_Safe_Succeeds(int width, int height, double dpiX, double dpiY)
    {
        const int tileSize = 500;
        const int channels = 4;

        // Create 1000x1000 rectangle with 4 channels, fill the rectangle with teal color
        byte[] smallRect = GC.AllocateUninitializedArray<byte>(tileSize * tileSize * channels);
        MemoryMarshal.Cast<byte, uint>(smallRect.AsSpan()).Fill(0xFF00E6FF);

        WriteableBitmap writeableBitmap = new(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

        // Top-Left
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize),
                                    smallRect, tileSize * channels, 0, 0);

        // Top-Right
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize),
                                    smallRect, tileSize * channels, width - tileSize, 0);

        // Middle Rect
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize),
                                    smallRect, tileSize * channels, (width - tileSize) / 2, (height - tileSize) / 2);

        // Bottom-Left
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize),
                                    smallRect, tileSize * channels, 0, height - tileSize);

        // Bottom-Right
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize),
                                    smallRect, tileSize * channels, width - tileSize, height - tileSize);
    }

    // Under 2GB back-buffer (4 channels)
    [InlineData(2_000, 2_000, 96.0, 96.0)]
    [InlineData(4_000, 4_000, 120, 120)]
    [InlineData(10_000, 10_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(20_000, 20_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    // Over 2GB back-buffer (4 channels) -- NOTE: These tests shall not be run on x86 without PAE
    [InlineData(25_000, 25_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(32_000, 32_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [Theory]
    public unsafe void WritePixels_SmallRect_Unsafe_Succeeds(int width, int height, double dpiX, double dpiY)
    {
        const int tileSize = 500;
        const int channels = 4;

        // Create 1000x1000 rectangle with 4 channels, fill the rectangle with teal color
        Span<byte> smallRect = GC.AllocateUninitializedArray<byte>(tileSize * tileSize * channels, pinned: true);
        MemoryMarshal.Cast<byte, uint>(smallRect).Fill(0xFF00E6FF);

        WriteableBitmap writeableBitmap = new(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

        // Top-Left
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize), smallRect.AsNativePointer(),
                                    smallRect.Length, tileSize * channels, 0, 0);

        // Top-Right
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize), smallRect.AsNativePointer(),
                                    smallRect.Length, tileSize * channels, width - tileSize, 0);

        // Middle Rect
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize), smallRect.AsNativePointer(),
                                    smallRect.Length, tileSize * channels, (width - tileSize) / 2, (height - tileSize) / 2);

        // Bottom-Left
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize), smallRect.AsNativePointer(),
                                    smallRect.Length, tileSize * channels, 0, height - tileSize);

        // Bottom-Right
        writeableBitmap.WritePixels(new Int32Rect(0, 0, tileSize, tileSize), smallRect.AsNativePointer(),
                                    smallRect.Length, tileSize * channels, width - tileSize, height - tileSize);
    }

    // Under 2GB back-buffer (4 channels)
    [InlineData(512, 512, 96.0, 96.0)]
    [InlineData(4_000, 4_000, 120, 120)]
    [InlineData(10_000, 10_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(20_000, 20_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    // Over 2GB back-buffer (4 channels) -- NOTE: These tests shall not be run on x86 without PAE
    [InlineData(25_000, 25_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(32_000, 32_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [Theory]
    public void WritePixels_FullRect_Safe_Succeeds(int width, int height, double dpiX, double dpiY)
    {
        const int channels = 4;

        // Create same-sized rectangle with 4 channels, fill the rectangle with teal color
        // NOTE: We use uint[] over byte[] to avoid Array.MaxLength limit for single-dims on 2GB+ bitmaps
        uint[] bigRect = GC.AllocateUninitializedArray<uint>(width * height);
        Array.Fill(bigRect, 0xFF00E6FF);

        WriteableBitmap writeableBitmap = new(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

        // Paint the full rect teal
        writeableBitmap.WritePixels(new Int32Rect(0, 0, width, height), bigRect, width * channels, 0, 0);
    }

    // Under 2GB back-buffer (4 channels)
    [InlineData(512, 512, 96.0, 96.0)]
    [InlineData(4_000, 4_000, 120, 120)]
    [InlineData(10_000, 10_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(20_000, 20_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    // Over 2GB back-buffer (4 channels) -- NOTE: These tests shall not be run on x86 without PAE
    [InlineData(25_000, 25_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(32_000, 32_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [Theory]
    public unsafe void WritePixels_FullRect_Unsafe_Succeeds(int width, int height, double dpiX, double dpiY)
    {
        const int channels = 4;

        // Create same-sized rectangle with 4 channels, fill the rectangle with teal color
        // NOTE: We use uint[] over byte[] to avoid Array.MaxLength limit for single-dims on 2GB+ bitmaps
        Span<uint> bigRect = GC.AllocateUninitializedArray<uint>(width * height, pinned: true);
        bigRect.Fill(0xFF00E6FF);

        WriteableBitmap writeableBitmap = new(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

        // Paint the full rect teal
        writeableBitmap.WritePixels(new Int32Rect(0, 0, width, height), bigRect.AsNativePointer(),
                                    bigRect.Length * channels, width * channels, 0, 0);
    }

    // Under 2GB back-buffer (4 channels)
    [InlineData(128, 128, 96.0, 96.0)]
    [InlineData(256, 512, 96.0, 96.0)]
    [InlineData(256, 256, 120.0, 120.0)]
    [InlineData(512, 256, 120.0, 120.0)]
    [InlineData(10_000, 10_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(20_000, 20_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    // Over 2GB back-buffer (4 channels) -- NOTE: These tests shall not be run on x86 without PAE
    [InlineData(25_000, 25_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [InlineData(32_000, 32_000, 96.0, 96.0, Skip = "Disabled to reduce working set")]
    [Theory]
    public void Clone_CopyPixels_Succeeds(int width, int height, double dpiX, double dpiY)
    {
        WriteableBitmap writeableBitmap = new(width, height, dpiX, dpiY, PixelFormats.Pbgra32, null);

        // Invoke bitmap copy
        BitmapSource bitmapSource = writeableBitmap.Clone();

        // Must succeed
        Assert.NotNull(bitmapSource);
    }
}

public static unsafe class SpanExtensions
{
    /// <summary>Retrieves the data pointer of the underlying <see cref="Span{T}"/> data reference.</summary>
    /// <param name="span">The <see cref="Span{T}"/> to retrieve a pointer to.</param>
    /// <returns>A <see cref="nuint"/> pointer of the underlying data reference.</returns>
    /// <remarks>The pointer reference is not pinned, use only on <see langword="fixed"/> buffers or <see langword="stackalloc"/> pointers.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint AsNativePointer<T>(this Span<T> span) => (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));

}
