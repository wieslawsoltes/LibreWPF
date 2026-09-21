// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32.SafeHandles;
using MS.Internal;
using System.Windows.Media;

namespace System.Windows.Media.Imaging
{
    #region IconBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Icon (Bitmap) Decoder.
    /// </summary>
    public sealed class IconBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private IconBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a IconBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public IconBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatIco)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public IconBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatIco)
        {
        }

        internal IconBitmapDecoder(
            BitmapFrame portableFrame,
            Uri baseUri,
            Uri uri,
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(true)
        {
            InitializePortableFrames(baseUri, uri, stream, createOptions, cacheOption, portableFrame);
        }

        /// <summary>
        /// Internal Constructor
        /// </summary>
        internal IconBitmapDecoder(
            SafeMILHandle decoderHandle,
            BitmapDecoder decoder,
            Uri baseUri,
            Uri uri,
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            bool insertInDecoderCache,
            bool originalWritable,
            Stream uriStream,
            UnmanagedMemoryStream unmanagedMemoryStream,
            SafeFileHandle safeFilehandle
            ) : base(decoderHandle, decoder, baseUri, uri, stream, createOptions, cacheOption, insertInDecoderCache, originalWritable, uriStream, unmanagedMemoryStream, safeFilehandle)
        {
        }

        public override BitmapPalette Palette
        {
            get
            {
                if (InternalDecoder == null && _frames != null && _frames.Count > 0)
                {
                    return _frames[0].Palette;
                }

                return base.Palette;
            }
        }

        public override ReadOnlyCollection<ColorContext> ColorContexts
        {
            get
            {
                if (InternalDecoder == null)
                {
                    return null;
                }

                return base.ColorContexts;
            }
        }

        public override BitmapSource Thumbnail
        {
            get
            {
                return InternalDecoder == null ? null : base.Thumbnail;
            }
        }

        public override BitmapMetadata Metadata
        {
            get
            {
                return InternalDecoder == null ? null : base.Metadata;
            }
        }

        public override BitmapSource Preview
        {
            get
            {
                return InternalDecoder == null ? null : base.Preview;
            }
        }

        internal static bool TryCreatePortableFrame(
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            out BitmapFrame frame)
        {
            frame = null;

            if (stream == null || !stream.CanSeek)
            {
                return false;
            }

            long startPosition = stream.Position;

            try
            {
                Span<byte> header = stackalloc byte[6];
                if (!TryReadExactly(stream, header))
                {
                    stream.Position = startPosition;
                    return false;
                }

                ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0, 2));
                ushort iconType = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2, 2));
                ushort count = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
                if (reserved != 0 || iconType != 1 || count == 0)
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (count > 1024)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                List<IconDirectoryEntry> entries = new List<IconDirectoryEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    entries.Add(ReadDirectoryEntry(stream));
                }

                entries.Sort(CompareDirectoryEntries);
                foreach (IconDirectoryEntry entry in entries)
                {
                    if (entry.BytesInResource == 0)
                    {
                        continue;
                    }

                    if (!TrySeekToImage(stream, startPosition, entry, out long imagePosition))
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    stream.Position = imagePosition;
                    byte[] imageBytes = new byte[checked((int)entry.BytesInResource)];
                    ReadExactly(stream, imageBytes);

                    if (IsPngSignature(imageBytes))
                    {
                        using MemoryStream imageStream = new MemoryStream(imageBytes, writable: false);
                        if (!PngBitmapDecoder.TryCreatePortableFrame(imageStream, createOptions, cacheOption, out frame))
                        {
                            throw new FileFormatException(null, SR.Image_CantDealWithStream);
                        }
                    }
                    else if (!TryCreateDibFrame(imageBytes, out frame))
                    {
                        continue;
                    }

                    if (!frame.IsFrozen && frame.CanFreeze)
                    {
                        frame.Freeze();
                    }

                    return true;
                }

                throw new NotSupportedException("Portable ICO decoding currently supports PNG-backed and BI_RGB DIB icon images.");
            }
            catch
            {
                stream.Position = startPosition;
                throw;
            }
        }

        internal static bool TryCreatePortableFrameFromUri(
            Uri uri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            out BitmapFrame frame)
        {
            return TryCreatePortableFrameFromUri(uri, createOptions, cacheOption, null, out frame);
        }

        internal static bool TryCreatePortableFrameFromUri(
            Uri uri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            System.Net.Cache.RequestCachePolicy uriCachePolicy,
            out BitmapFrame frame)
        {
            frame = null;

            if (!BitmapDecoder.TryOpenPortableUriStream(uri, uriCachePolicy, out Stream stream))
            {
                return false;
            }

            using (stream)
            {
                return TryCreatePortableFrame(stream, createOptions, cacheOption, out frame);
            }
        }

        private static bool TryCreateDibFrame(byte[] imageBytes, out BitmapFrame frame)
        {
            frame = null;

            if (imageBytes.Length < 40)
            {
                return false;
            }

            ReadOnlySpan<byte> data = imageBytes;
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4));
            if (headerSize < 40 || headerSize > imageBytes.Length)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
            int iconBitmapHeight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
            ushort planes = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2));
            ushort bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14, 2));
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));
            int pixelsPerMeterX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24, 4));
            int pixelsPerMeterY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28, 4));
            uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32, 4));

            if (width <= 0 ||
                iconBitmapHeight <= 0 ||
                iconBitmapHeight % 2 != 0 ||
                planes != 1 ||
                compression != 0)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            int height = iconBitmapHeight / 2;
            int colorCount = GetDibColorCount(bitsPerPixel, colorsUsed);
            int paletteOffset = checked((int)headerSize);
            int xorOffset = checked(paletteOffset + colorCount * 4);
            int xorStride = checked(((width * bitsPerPixel) + 31) / 32 * 4);
            int maskStride = checked((width + 31) / 32 * 4);
            int maskOffset = checked(xorOffset + xorStride * height);
            int requiredLength = checked(maskOffset + maskStride * height);
            if (requiredLength > imageBytes.Length)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            if (bitsPerPixel != 1 &&
                bitsPerPixel != 4 &&
                bitsPerPixel != 8 &&
                bitsPerPixel != 24 &&
                bitsPerPixel != 32)
            {
                throw new NotSupportedException($"Portable ICO DIB decoding does not support {bitsPerPixel}-bit images.");
            }

            bool hasSourceAlpha = bitsPerPixel == 32 && HasAnyAlphaByte(imageBytes, xorOffset, xorStride, width, height);
            int targetStride = checked(width * 4);
            byte[] pixels = new byte[checked(targetStride * height)];

            for (int fileRow = 0; fileRow < height; fileRow++)
            {
                int targetY = height - 1 - fileRow;
                int sourceRow = xorOffset + fileRow * xorStride;
                int maskRow = maskOffset + fileRow * maskStride;
                int targetRow = targetY * targetStride;

                for (int x = 0; x < width; x++)
                {
                    int targetOffset = targetRow + x * 4;
                    WriteDibPixel(imageBytes, paletteOffset, colorCount, sourceRow, maskRow, x, bitsPerPixel, hasSourceAlpha, pixels, targetOffset);
                }
            }

            BitmapSource source = BitmapSource.Create(
                width,
                height,
                PixelsPerMeterToDpi(pixelsPerMeterX),
                PixelsPerMeterToDpi(pixelsPerMeterY),
                PixelFormats.Bgra32,
                null,
                pixels,
                targetStride);

            frame = BitmapFrame.Create(source);
            return true;
        }

        private static int GetDibColorCount(ushort bitsPerPixel, uint colorsUsed)
        {
            if (bitsPerPixel > 8)
            {
                return 0;
            }

            int maximum = 1 << bitsPerPixel;
            return colorsUsed == 0 ? maximum : checked((int)Math.Min(colorsUsed, (uint)maximum));
        }

        private static bool HasAnyAlphaByte(byte[] imageBytes, int xorOffset, int xorStride, int width, int height)
        {
            for (int row = 0; row < height; row++)
            {
                int rowOffset = xorOffset + row * xorStride;
                for (int x = 0; x < width; x++)
                {
                    if (imageBytes[rowOffset + x * 4 + 3] != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void WriteDibPixel(
            byte[] imageBytes,
            int paletteOffset,
            int colorCount,
            int sourceRow,
            int maskRow,
            int x,
            ushort bitsPerPixel,
            bool hasSourceAlpha,
            byte[] pixels,
            int targetOffset)
        {
            byte b;
            byte g;
            byte r;
            byte a = 255;

            switch (bitsPerPixel)
            {
                case 1:
                case 4:
                case 8:
                    int paletteIndex = ReadPackedIndex(imageBytes, sourceRow, x, bitsPerPixel);
                    if (paletteIndex >= colorCount)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    int paletteEntry = paletteOffset + paletteIndex * 4;
                    b = imageBytes[paletteEntry];
                    g = imageBytes[paletteEntry + 1];
                    r = imageBytes[paletteEntry + 2];
                    break;
                case 24:
                    int bgrOffset = sourceRow + x * 3;
                    b = imageBytes[bgrOffset];
                    g = imageBytes[bgrOffset + 1];
                    r = imageBytes[bgrOffset + 2];
                    break;
                case 32:
                    int bgraOffset = sourceRow + x * 4;
                    b = imageBytes[bgraOffset];
                    g = imageBytes[bgraOffset + 1];
                    r = imageBytes[bgraOffset + 2];
                    a = hasSourceAlpha ? imageBytes[bgraOffset + 3] : (byte)255;
                    break;
                default:
                    throw new NotSupportedException($"Portable ICO DIB decoding does not support {bitsPerPixel}-bit images.");
            }

            if (IsMaskBitSet(imageBytes, maskRow, x))
            {
                a = 0;
            }

            pixels[targetOffset + 0] = b;
            pixels[targetOffset + 1] = g;
            pixels[targetOffset + 2] = r;
            pixels[targetOffset + 3] = a;
        }

        private static int ReadPackedIndex(byte[] imageBytes, int sourceRow, int x, ushort bitsPerPixel)
        {
            int bitOffset = x * bitsPerPixel;
            byte value = imageBytes[sourceRow + bitOffset / 8];

            switch (bitsPerPixel)
            {
                case 1:
                    return (value >> (7 - bitOffset % 8)) & 0x01;
                case 4:
                    return bitOffset % 8 == 0 ? (value >> 4) & 0x0F : value & 0x0F;
                case 8:
                    return value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(bitsPerPixel));
            }
        }

        private static bool IsMaskBitSet(byte[] imageBytes, int maskRow, int x)
        {
            return (imageBytes[maskRow + x / 8] & (0x80 >> (x % 8))) != 0;
        }

        private static double PixelsPerMeterToDpi(int pixelsPerMeter)
        {
            return pixelsPerMeter > 0 ? pixelsPerMeter / 39.37007874015748 : 96.0;
        }

        private static IconDirectoryEntry ReadDirectoryEntry(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[16];
            ReadExactly(stream, buffer);

            int width = buffer[0] == 0 ? 256 : buffer[0];
            int height = buffer[1] == 0 ? 256 : buffer[1];
            return new IconDirectoryEntry(
                width,
                height,
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4)));
        }

        private static int CompareDirectoryEntries(IconDirectoryEntry left, IconDirectoryEntry right)
        {
            int areaComparison = (right.Width * right.Height).CompareTo(left.Width * left.Height);
            if (areaComparison != 0)
            {
                return areaComparison;
            }

            return right.BitCount.CompareTo(left.BitCount);
        }

        private static bool TrySeekToImage(Stream stream, long startPosition, IconDirectoryEntry entry, out long imagePosition)
        {
            imagePosition = checked(startPosition + entry.ImageOffset);
            long imageEnd = checked(imagePosition + entry.BytesInResource);
            if (imagePosition < startPosition || imageEnd < imagePosition)
            {
                return false;
            }

            try
            {
                if (imageEnd > stream.Length)
                {
                    return false;
                }
            }
            catch (NotSupportedException)
            {
            }

            stream.Position = imagePosition;
            return true;
        }

        private static bool IsPngSignature(ReadOnlySpan<byte> signature)
        {
            return signature.Length >= 8 &&
                signature[0] == 0x89 &&
                signature[1] == (byte)'P' &&
                signature[2] == (byte)'N' &&
                signature[3] == (byte)'G' &&
                signature[4] == 0x0D &&
                signature[5] == 0x0A &&
                signature[6] == 0x1A &&
                signature[7] == 0x0A;
        }

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            if (!TryReadExactly(stream, buffer))
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }
        }

        private static bool TryReadExactly(Stream stream, Span<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(offset));
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        private readonly struct IconDirectoryEntry
        {
            public IconDirectoryEntry(int width, int height, ushort bitCount, uint bytesInResource, uint imageOffset)
            {
                Width = width;
                Height = height;
                BitCount = bitCount;
                BytesInResource = bytesInResource;
                ImageOffset = imageOffset;
            }

            public int Width { get; }

            public int Height { get; }

            public ushort BitCount { get; }

            public uint BytesInResource { get; }

            public uint ImageOffset { get; }
        }

        #region Internal Abstract

        /// Need to implement this to derive from the "sealed" object
        internal override void SealObject()
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    #endregion
}
