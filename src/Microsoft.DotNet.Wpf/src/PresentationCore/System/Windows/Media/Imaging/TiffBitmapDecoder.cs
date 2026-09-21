// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.IO;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using Microsoft.Win32.SafeHandles;
using MS.Internal;

namespace System.Windows.Media.Imaging
{
    #region TiffBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Tiff (Bitmap) Decoder.
    /// </summary>
    public sealed class TiffBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private TiffBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a TiffBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public TiffBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatTiff)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public TiffBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatTiff)
        {
        }

        internal TiffBitmapDecoder(
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

        internal TiffBitmapDecoder(
            ReadOnlyCollection<BitmapFrame> portableFrames,
            Uri baseUri,
            Uri uri,
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(true)
        {
            InitializePortableFrames(baseUri, uri, stream, createOptions, cacheOption, portableFrames);
        }

        /// <summary>
        /// Internal Constructor
        /// </summary>
        internal TiffBitmapDecoder(
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
                return InternalDecoder == null ? null : base.Palette;
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
            if (!TryCreatePortableFrames(stream, createOptions, cacheOption, out ReadOnlyCollection<BitmapFrame> frames))
            {
                return false;
            }

            frame = frames[0];
            return true;
        }

        internal static bool TryCreatePortableFrames(
            Stream stream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            out ReadOnlyCollection<BitmapFrame> frames)
        {
            frames = null;

            if (stream == null || !stream.CanSeek)
            {
                return false;
            }

            long startPosition = stream.Position;

            try
            {
                Span<byte> header = stackalloc byte[8];
                if (!TryReadExactly(stream, header))
                {
                    stream.Position = startPosition;
                    return false;
                }

                bool littleEndian;
                if (header[0] == (byte)'I' && header[1] == (byte)'I')
                {
                    littleEndian = true;
                }
                else if (header[0] == (byte)'M' && header[1] == (byte)'M')
                {
                    littleEndian = false;
                }
                else
                {
                    stream.Position = startPosition;
                    return false;
                }

                ushort marker = ReadUInt16(header.Slice(2, 2), littleEndian);
                if (marker != 42)
                {
                    stream.Position = startPosition;
                    return false;
                }

                uint ifdOffset = ReadUInt32(header.Slice(4, 4), littleEndian);
                if (ifdOffset < 8)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                List<BitmapFrame> portableFrames = new List<BitmapFrame>();
                HashSet<uint> seenIfdOffsets = new HashSet<uint>();
                uint currentIfdOffset = ifdOffset;
                while (currentIfdOffset != 0)
                {
                    if (currentIfdOffset < 8 || !seenIfdOffsets.Add(currentIfdOffset) || portableFrames.Count >= MaxPortableFrameCount)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    TiffDirectory directory = ReadDirectory(stream, startPosition, currentIfdOffset, littleEndian);
                    portableFrames.Add(CreateFrame(stream, startPosition, directory, littleEndian));
                    currentIfdOffset = directory.NextIfdOffset;
                }

                if (portableFrames.Count == 0)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                frames = new ReadOnlyCollection<BitmapFrame>(portableFrames);
                return true;
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
            frame = null;
            if (!TryCreatePortableFramesFromUri(uri, createOptions, cacheOption, out ReadOnlyCollection<BitmapFrame> frames))
            {
                return false;
            }

            frame = frames[0];
            return true;
        }

        internal static bool TryCreatePortableFramesFromUri(
            Uri uri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            out ReadOnlyCollection<BitmapFrame> frames)
        {
            return TryCreatePortableFramesFromUri(uri, createOptions, cacheOption, null, out frames);
        }

        internal static bool TryCreatePortableFramesFromUri(
            Uri uri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption,
            System.Net.Cache.RequestCachePolicy uriCachePolicy,
            out ReadOnlyCollection<BitmapFrame> frames)
        {
            frames = null;

            if (!BitmapDecoder.TryOpenPortableUriStream(uri, uriCachePolicy, out Stream stream))
            {
                return false;
            }

            using (stream)
            {
                return TryCreatePortableFrames(stream, createOptions, cacheOption, out frames);
            }
        }

        /// <summary>
        /// Returns whether metadata is fixed size or not.
        /// </summary>
        internal override bool IsMetadataFixedSize
        {
            get
            {
                return true;
            }
        }

        #region Internal Abstract

        /// Need to implement this to derive from the "sealed" object
        internal override void SealObject()
        {
            throw new NotImplementedException();
        }

        private static TiffDirectory ReadDirectory(Stream stream, long startPosition, uint ifdOffset, bool littleEndian)
        {
            SeekFromStart(stream, startPosition, ifdOffset);

            Span<byte> countBytes = stackalloc byte[2];
            ReadExactly(stream, countBytes);
            ushort entryCount = ReadUInt16(countBytes, littleEndian);
            if (entryCount == 0 || entryCount > 4096)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            TiffDirectory directory = new TiffDirectory();
            Span<byte> entryBytes = stackalloc byte[12];
            for (int i = 0; i < entryCount; i++)
            {
                ReadExactly(stream, entryBytes);
                TiffEntry entry = new TiffEntry(
                    ReadUInt16(entryBytes.Slice(0, 2), littleEndian),
                    ReadUInt16(entryBytes.Slice(2, 2), littleEndian),
                    ReadUInt32(entryBytes.Slice(4, 4), littleEndian),
                    ReadUInt32(entryBytes.Slice(8, 4), littleEndian));

                long nextEntryPosition = stream.Position;
                switch (entry.Tag)
                {
                    case ImageWidthTag:
                        directory.Width = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case ImageLengthTag:
                        directory.Height = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case BitsPerSampleTag:
                        directory.BitsPerSample = ReadUnsignedValues(stream, startPosition, entry, littleEndian);
                        break;
                    case CompressionTag:
                        directory.Compression = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case PhotometricInterpretationTag:
                        directory.PhotometricInterpretation = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case StripOffsetsTag:
                        directory.StripOffsets = ReadUnsignedValues(stream, startPosition, entry, littleEndian);
                        break;
                    case OrientationTag:
                        directory.Orientation = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        directory.HasOrientation = true;
                        break;
                    case SamplesPerPixelTag:
                        directory.SamplesPerPixel = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case RowsPerStripTag:
                        directory.RowsPerStrip = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case StripByteCountsTag:
                        directory.StripByteCounts = ReadUnsignedValues(stream, startPosition, entry, littleEndian);
                        break;
                    case PlanarConfigurationTag:
                        directory.PlanarConfiguration = ReadSingleUnsignedValue(stream, startPosition, entry, littleEndian);
                        break;
                    case ColorMapTag:
                        directory.ColorMap = ReadUnsignedValues(stream, startPosition, entry, littleEndian);
                        break;
                }

                stream.Position = nextEntryPosition;
            }

            Span<byte> nextIfdBytes = stackalloc byte[4];
            ReadExactly(stream, nextIfdBytes);
            directory.NextIfdOffset = ReadUInt32(nextIfdBytes, littleEndian);
            return directory;
        }

        private static BitmapFrame CreateFrame(Stream stream, long startPosition, TiffDirectory directory, bool littleEndian)
        {
            if (directory.Width == 0 || directory.Height == 0 ||
                directory.Width > int.MaxValue || directory.Height > int.MaxValue)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            if (directory.Compression != 1 || directory.PlanarConfiguration != 1)
            {
                throw new NotSupportedException("Portable TIFF decoding currently supports uncompressed chunky first-frame images.");
            }

            uint samplesPerPixel = directory.SamplesPerPixel;
            if (samplesPerPixel == 0)
            {
                samplesPerPixel = directory.PhotometricInterpretation == 2 ? 3u : 1u;
            }

            uint[] bitsPerSample = directory.BitsPerSample;
            if (bitsPerSample == null || bitsPerSample.Length == 0)
            {
                bitsPerSample = [1];
            }

            if (bitsPerSample.Length == 1 && samplesPerPixel > 1)
            {
                uint bitDepth = bitsPerSample[0];
                bitsPerSample = new uint[checked((int)samplesPerPixel)];
                Array.Fill(bitsPerSample, bitDepth);
            }

            if (bitsPerSample.Length < samplesPerPixel)
            {
                throw new NotSupportedException("Portable TIFF decoding requires BitsPerSample for each sample.");
            }

            bool paletteColor = directory.PhotometricInterpretation == 3;
            bool grayscale = directory.PhotometricInterpretation == 0 || directory.PhotometricInterpretation == 1;
            bool rgb = directory.PhotometricInterpretation == 2;
            if (paletteColor)
            {
                if (samplesPerPixel != 1)
                {
                    throw new NotSupportedException("Portable TIFF palette decoding currently supports one sample per pixel.");
                }

                uint paletteBitDepth = bitsPerSample[0];
                if (paletteBitDepth != 1 && paletteBitDepth != 2 && paletteBitDepth != 4 && paletteBitDepth != 8)
                {
                    throw new NotSupportedException("Portable TIFF palette decoding currently supports 1, 2, 4, and 8-bit indices.");
                }

                int colorCount = 1 << checked((int)paletteBitDepth);
                if (directory.ColorMap == null || directory.ColorMap.Length < checked(colorCount * 3))
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }
            }
            else
            {
                for (int i = 0; i < samplesPerPixel; i++)
                {
                    if (bitsPerSample[i] != 8)
                    {
                        throw new NotSupportedException("Portable TIFF decoding currently supports 8-bit samples.");
                    }
                }

                if ((!grayscale || samplesPerPixel != 1) && (!rgb || (samplesPerPixel != 3 && samplesPerPixel != 4)))
                {
                    throw new NotSupportedException("Portable TIFF decoding currently supports 8-bit grayscale, RGB, RGBA, and palette images.");
                }
            }

            if (directory.StripOffsets == null || directory.StripOffsets.Length == 0 ||
                directory.StripByteCounts == null || directory.StripByteCounts.Length == 0)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            int width = checked((int)directory.Width);
            int height = checked((int)directory.Height);
            int sourceStride = paletteColor
                ? checked(((width * (int)bitsPerSample[0]) + 7) / 8)
                : checked(width * (int)samplesPerPixel);
            int targetStride = checked(width * 4);
            byte[] pixels = new byte[checked(targetStride * height)];
            byte[] row = new byte[sourceStride];

            int y = 0;
            uint rowsPerStrip = directory.RowsPerStrip == 0 ? directory.Height : directory.RowsPerStrip;
            for (int strip = 0; strip < directory.StripOffsets.Length && y < height; strip++)
            {
                uint stripOffset = directory.StripOffsets[strip];
                uint byteCount = directory.StripByteCounts[Math.Min(strip, directory.StripByteCounts.Length - 1)];
                int rowsInStrip = (int)Math.Min(rowsPerStrip, (uint)(height - y));
                long stripEnd = checked((long)stripOffset + byteCount);
                SeekFromStart(stream, startPosition, stripOffset);

                for (int rowIndex = 0; rowIndex < rowsInStrip && y < height; rowIndex++, y++)
                {
                    long nextRowEnd = checked(stream.Position + sourceStride);
                    if (nextRowEnd > startPosition + stripEnd)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    ReadExactly(stream, row);
                    if (paletteColor)
                    {
                        CopyPaletteTiffRowToBgra(row, pixels, y * targetStride, width, (int)bitsPerSample[0], directory.ColorMap);
                    }
                    else
                    {
                        CopyTiffRowToBgra(row, pixels, y * targetStride, width, samplesPerPixel, directory.PhotometricInterpretation);
                    }
                }
            }

            if (y != height)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            BitmapSource source = BitmapSource.Create(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Bgra32,
                null,
                pixels,
                targetStride);

            BitmapMetadata metadata = CreateFrameMetadata(directory);
            BitmapFrame frame = BitmapFrame.Create(source, null, metadata, null);
            if (!frame.IsFrozen && frame.CanFreeze)
            {
                frame.Freeze();
            }

            return frame;
        }

        private static BitmapMetadata CreateFrameMetadata(TiffDirectory directory)
        {
            var queries = new Dictionary<string, object>(StringComparer.Ordinal);
            if (directory.HasOrientation)
            {
                queries["/ifd/{ushort=274}"] = ConvertTiffMetadataValue(directory.Orientation);
            }

            return new BitmapMetadata("tiff", MILGuidData.GUID_ContainerFormatTiff, queries);
        }

        private static object ConvertTiffMetadataValue(uint value)
        {
            return value <= ushort.MaxValue ? (object)(ushort)value : value;
        }

        private static void CopyPaletteTiffRowToBgra(
            byte[] source,
            byte[] target,
            int targetOffset,
            int width,
            int bitDepth,
            uint[] colorMap)
        {
            int colorCount = 1 << bitDepth;
            for (int x = 0; x < width; x++)
            {
                uint paletteIndex = ReadPackedSample(source, x, bitDepth);
                if (paletteIndex >= colorCount)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                int destinationOffset = targetOffset + (x * 4);
                int index = checked((int)paletteIndex);
                target[destinationOffset + 0] = TiffColorMapValueToByte(colorMap[(colorCount * 2) + index]);
                target[destinationOffset + 1] = TiffColorMapValueToByte(colorMap[colorCount + index]);
                target[destinationOffset + 2] = TiffColorMapValueToByte(colorMap[index]);
                target[destinationOffset + 3] = 255;
            }
        }

        private static uint ReadPackedSample(byte[] source, int sampleIndex, int bitDepth)
        {
            if (bitDepth == 8)
            {
                return source[sampleIndex];
            }

            int bitOffset = sampleIndex * bitDepth;
            int byteOffset = bitOffset / 8;
            int shift = 8 - bitDepth - (bitOffset % 8);
            return (uint)((source[byteOffset] >> shift) & ((1 << bitDepth) - 1));
        }

        private static byte TiffColorMapValueToByte(uint value)
        {
            return (byte)(Math.Min(value, 65535u) / 257u);
        }

        private static void CopyTiffRowToBgra(
            byte[] source,
            byte[] target,
            int targetOffset,
            int width,
            uint samplesPerPixel,
            uint photometricInterpretation)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceOffset = x * (int)samplesPerPixel;
                int destinationOffset = targetOffset + (x * 4);
                if (photometricInterpretation == 0 || photometricInterpretation == 1)
                {
                    byte value = source[sourceOffset];
                    if (photometricInterpretation == 0)
                    {
                        value = (byte)(255 - value);
                    }

                    target[destinationOffset + 0] = value;
                    target[destinationOffset + 1] = value;
                    target[destinationOffset + 2] = value;
                    target[destinationOffset + 3] = 255;
                }
                else
                {
                    target[destinationOffset + 0] = source[sourceOffset + 2];
                    target[destinationOffset + 1] = source[sourceOffset + 1];
                    target[destinationOffset + 2] = source[sourceOffset + 0];
                    target[destinationOffset + 3] = samplesPerPixel == 4 ? source[sourceOffset + 3] : (byte)255;
                }
            }
        }

        private static uint ReadSingleUnsignedValue(Stream stream, long startPosition, TiffEntry entry, bool littleEndian)
        {
            uint[] values = ReadUnsignedValues(stream, startPosition, entry, littleEndian);
            if (values.Length == 0)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            return values[0];
        }

        private static uint[] ReadUnsignedValues(Stream stream, long startPosition, TiffEntry entry, bool littleEndian)
        {
            int elementSize = GetTypeSize(entry.Type);
            if (elementSize == 0 || entry.Count == 0 || entry.Count > 1_000_000)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            long totalSize = checked((long)elementSize * entry.Count);
            byte[] data = new byte[checked((int)totalSize)];
            if (totalSize <= 4)
            {
                WriteInlineValueBytes(entry.ValueOrOffset, data, littleEndian);
            }
            else
            {
                SeekFromStart(stream, startPosition, entry.ValueOrOffset);
                ReadExactly(stream, data);
            }

            uint[] values = new uint[checked((int)entry.Count)];
            for (int i = 0; i < values.Length; i++)
            {
                ReadOnlySpan<byte> valueBytes = data.AsSpan(i * elementSize, elementSize);
                values[i] = entry.Type switch
                {
                    TiffTypeByte => valueBytes[0],
                    TiffTypeShort => ReadUInt16(valueBytes, littleEndian),
                    TiffTypeLong => ReadUInt32(valueBytes, littleEndian),
                    _ => throw new FileFormatException(null, SR.Image_CantDealWithStream),
                };
            }

            return values;
        }

        private static int GetTypeSize(ushort type)
        {
            return type switch
            {
                TiffTypeByte => 1,
                TiffTypeShort => 2,
                TiffTypeLong => 4,
                _ => 0,
            };
        }

        private static void WriteInlineValueBytes(uint value, byte[] destination, bool littleEndian)
        {
            Span<byte> valueBytes = stackalloc byte[4];
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(valueBytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(valueBytes, value);
            }

            valueBytes.Slice(0, destination.Length).CopyTo(destination);
        }

        private static void SeekFromStart(Stream stream, long startPosition, uint offset)
        {
            stream.Position = checked(startPosition + offset);
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool littleEndian)
        {
            return littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(source)
                : BinaryPrimitives.ReadUInt16BigEndian(source);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> source, bool littleEndian)
        {
            return littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(source)
                : BinaryPrimitives.ReadUInt32BigEndian(source);
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
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }
        }

        private readonly struct TiffEntry
        {
            public TiffEntry(ushort tag, ushort type, uint count, uint valueOrOffset)
            {
                Tag = tag;
                Type = type;
                Count = count;
                ValueOrOffset = valueOrOffset;
            }

            public ushort Tag { get; }

            public ushort Type { get; }

            public uint Count { get; }

            public uint ValueOrOffset { get; }
        }

        private sealed class TiffDirectory
        {
            public uint Width { get; set; }

            public uint Height { get; set; }

            public uint[] BitsPerSample { get; set; }

            public uint Compression { get; set; } = 1;

            public uint PhotometricInterpretation { get; set; } = 1;

            public uint[] StripOffsets { get; set; }

            public uint Orientation { get; set; } = 1;

            public bool HasOrientation { get; set; }

            public uint SamplesPerPixel { get; set; }

            public uint RowsPerStrip { get; set; }

            public uint[] StripByteCounts { get; set; }

            public uint PlanarConfiguration { get; set; } = 1;

            public uint[] ColorMap { get; set; }

            public uint NextIfdOffset { get; set; }
        }

        private const ushort ImageWidthTag = 256;
        private const ushort ImageLengthTag = 257;
        private const ushort BitsPerSampleTag = 258;
        private const ushort CompressionTag = 259;
        private const ushort PhotometricInterpretationTag = 262;
        private const ushort StripOffsetsTag = 273;
        private const ushort OrientationTag = 274;
        private const ushort SamplesPerPixelTag = 277;
        private const ushort RowsPerStripTag = 278;
        private const ushort StripByteCountsTag = 279;
        private const ushort PlanarConfigurationTag = 284;
        private const ushort ColorMapTag = 320;

        private const ushort TiffTypeByte = 1;
        private const ushort TiffTypeShort = 3;
        private const ushort TiffTypeLong = 4;

        private const int MaxPortableFrameCount = 1024;

        #endregion
    }

    #endregion
}
