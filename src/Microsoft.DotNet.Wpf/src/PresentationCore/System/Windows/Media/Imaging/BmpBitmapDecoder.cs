// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using MS.Internal;
using Microsoft.Win32.SafeHandles;
using System.Windows.Media;

namespace System.Windows.Media.Imaging
{
    #region BmpBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Bmp (Bitmap) Decoder.
    /// </summary>
    public sealed class BmpBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private BmpBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a BmpBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public BmpBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatBmp)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public BmpBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatBmp)
        {
        }

        internal BmpBitmapDecoder(
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
        internal BmpBitmapDecoder(
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
                Span<byte> fileHeader = stackalloc byte[14];
                if (!TryReadExactly(stream, fileHeader))
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (fileHeader[0] != (byte)'B' || fileHeader[1] != (byte)'M')
                {
                    stream.Position = startPosition;
                    return false;
                }

                uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(fileHeader.Slice(10, 4));
                uint dibHeaderSize = ReadUInt32(stream);
                if (dibHeaderSize < 40)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                int width = ReadInt32(stream);
                int signedHeight = ReadInt32(stream);
                ushort planes = ReadUInt16(stream);
                ushort bitsPerPixel = ReadUInt16(stream);
                uint compression = ReadUInt32(stream);
                _ = ReadUInt32(stream);
                int pixelsPerMeterX = ReadInt32(stream);
                int pixelsPerMeterY = ReadInt32(stream);
                uint colorsUsed = ReadUInt32(stream);
                _ = ReadUInt32(stream);

                if (width <= 0 || signedHeight == 0 || planes != 1 || compression != 0)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                long extraHeaderBytes = dibHeaderSize - 40;
                if (extraHeaderBytes > 0)
                {
                    stream.Seek(extraHeaderBytes, SeekOrigin.Current);
                }

                BitmapPalette palette = null;
                List<Color> paletteColors = null;
                if (bitsPerPixel <= 8)
                {
                    paletteColors = ReadColorTable(stream, startPosition, pixelOffset, bitsPerPixel, colorsUsed);
                }

                PixelFormat pixelFormat = GetPixelFormat(bitsPerPixel, paletteColors, out bool usesPalette);
                if (usesPalette)
                {
                    palette = new BitmapPalette(paletteColors);
                }

                int height = Math.Abs(signedHeight);
                bool topDown = signedHeight < 0;
                int sourceStride = checked(((width * bitsPerPixel) + 31) / 32 * 4);
                int targetStride = checked(((width * pixelFormat.BitsPerPixel) + 7) / 8);
                byte[] pixels = new byte[checked(targetStride * height)];
                byte[] sourceRow = new byte[sourceStride];

                stream.Position = startPosition + pixelOffset;
                for (int fileRow = 0; fileRow < height; fileRow++)
                {
                    ReadExactly(stream, sourceRow);
                    int targetRow = topDown ? fileRow : height - 1 - fileRow;
                    Buffer.BlockCopy(sourceRow, 0, pixels, targetRow * targetStride, targetStride);
                }

                BitmapSource source = BitmapSource.Create(
                    width,
                    height,
                    PixelsPerMeterToDpi(pixelsPerMeterX),
                    PixelsPerMeterToDpi(pixelsPerMeterY),
                    pixelFormat,
                    palette,
                    pixels,
                    targetStride);

                frame = BitmapFrame.Create(source);
                if (!frame.IsFrozen && frame.CanFreeze)
                {
                    frame.Freeze();
                }

                return true;
            }
            catch
            {
                stream.Position = startPosition;
                throw;
            }
        }

        private static PixelFormat GetPixelFormat(ushort bitsPerPixel, List<Color> paletteColors, out bool usesPalette)
        {
            usesPalette = false;

            switch (bitsPerPixel)
            {
                case 1:
                    if (IsGeneratedGrayPalette(paletteColors, bitsPerPixel))
                    {
                        return PixelFormats.BlackWhite;
                    }

                    usesPalette = true;
                    return PixelFormats.Indexed1;
                case 4:
                    if (IsGeneratedGrayPalette(paletteColors, bitsPerPixel))
                    {
                        return PixelFormats.Gray4;
                    }

                    usesPalette = true;
                    return PixelFormats.Indexed4;
                case 8:
                    if (IsGeneratedGrayPalette(paletteColors, bitsPerPixel))
                    {
                        return PixelFormats.Gray8;
                    }

                    usesPalette = true;
                    return PixelFormats.Indexed8;
                case 24:
                    return PixelFormats.Bgr24;
                case 32:
                    return PixelFormats.Bgra32;
                default:
                    throw new NotSupportedException($"Portable BMP decoding does not support {bitsPerPixel}-bit BI_RGB bitmaps.");
            }
        }

        private static List<Color> ReadColorTable(Stream stream, long startPosition, uint pixelOffset, ushort bitsPerPixel, uint colorsUsed)
        {
            int maximumColorCount = 1 << bitsPerPixel;
            int colorCount = colorsUsed == 0 ? maximumColorCount : checked((int)Math.Min(colorsUsed, (uint)maximumColorCount));
            long colorTableBytes = (startPosition + pixelOffset) - stream.Position;
            if (colorCount <= 0 || colorTableBytes < checked(colorCount * 4L))
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            List<Color> colors = new List<Color>(colorCount);
            Span<byte> entry = stackalloc byte[4];
            for (int i = 0; i < colorCount; i++)
            {
                ReadExactly(stream, entry);
                colors.Add(Color.FromRgb(entry[2], entry[1], entry[0]));
            }

            return colors;
        }

        private static bool IsGeneratedGrayPalette(List<Color> paletteColors, ushort bitsPerPixel)
        {
            if (paletteColors == null)
            {
                return false;
            }

            int colorCount = 1 << bitsPerPixel;
            if (paletteColors.Count != colorCount)
            {
                return false;
            }

            int divisor = colorCount - 1;
            for (int i = 0; i < colorCount; i++)
            {
                byte expected = divisor == 0 ? (byte)0 : (byte)((i * 255) / divisor);
                Color color = paletteColors[i];
                if (color.R != expected || color.G != expected || color.B != expected)
                {
                    return false;
                }
            }

            return true;
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

        private static double PixelsPerMeterToDpi(int pixelsPerMeter)
        {
            return pixelsPerMeter > 0 ? pixelsPerMeter / 39.37007874015748 : 96.0;
        }

        private static ushort ReadUInt16(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        }

        private static uint ReadUInt32(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        }

        private static int ReadInt32(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
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

        private static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            if (!TryReadExactly(stream, buffer))
            {
                throw new EndOfStreamException();
            }
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
