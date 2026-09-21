// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using MS.Internal;
using Microsoft.Win32.SafeHandles;

namespace System.Windows.Media.Imaging
{
    #region GifBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Gif (Bitmap) Decoder.
    /// </summary>
    public sealed class GifBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private GifBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a GifBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public GifBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatGif)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public GifBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatGif)
        {
        }

        internal GifBitmapDecoder(
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

        internal GifBitmapDecoder(
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
        internal GifBitmapDecoder(
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
            UnmanagedMemoryStream unmanagedMemoryStream ,
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
                Span<byte> signature = stackalloc byte[6];
                if (!TryReadExactly(stream, signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (!IsGifSignature(signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                stream.Position = startPosition;
                byte[] data = ReadRemainingBytes(stream);
                frames = DecodePortableFrames(data);
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

        private static ReadOnlyCollection<BitmapFrame> DecodePortableFrames(byte[] data)
        {
            var reader = new GifReader(data);
            reader.Skip(6);

            int canvasWidth = reader.ReadUInt16();
            int canvasHeight = reader.ReadUInt16();
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            byte packed = reader.ReadByte();
            int backgroundColorIndex = reader.ReadByte();
            reader.Skip(1);

            byte[] globalColorTable = null;
            if ((packed & 0x80) != 0)
            {
                globalColorTable = reader.ReadColorTable(1 << ((packed & 0x07) + 1));
            }

            int stride = checked(canvasWidth * 4);
            byte[] canvas = new byte[checked(stride * canvasHeight)];
            FillBackground(canvas, globalColorTable, backgroundColorIndex);

            List<BitmapFrame> portableFrames = new List<BitmapFrame>();
            GraphicControl graphicControl = default;
            PendingDisposal pendingDisposal = default;

            while (!reader.EndOfData)
            {
                byte introducer = reader.ReadByte();
                if (introducer == 0x3B)
                {
                    break;
                }

                if (introducer == 0x21)
                {
                    byte label = reader.ReadByte();
                    if (label == 0xF9)
                    {
                        graphicControl = reader.ReadGraphicControl();
                    }
                    else
                    {
                        reader.SkipSubBlocks();
                    }

                    continue;
                }

                if (introducer != 0x2C)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                ApplyPendingDisposal(canvas, stride, pendingDisposal, globalColorTable, backgroundColorIndex);
                pendingDisposal = default;

                ImageDescriptor descriptor = reader.ReadImageDescriptor();
                ValidateFrameBounds(canvasWidth, canvasHeight, descriptor);

                byte[] activeColorTable = descriptor.LocalColorTable ?? globalColorTable;
                if (activeColorTable == null)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                byte[] savedCanvas = graphicControl.DisposalMethod == 3 ? (byte[])canvas.Clone() : null;
                byte[] indices = DecodeLzwIndices(
                    reader.ReadByte(),
                    reader.ReadSubBlocks(),
                    checked(descriptor.Width * descriptor.Height));
                if (descriptor.Interlaced)
                {
                    indices = Deinterlace(indices, descriptor.Width, descriptor.Height);
                }

                DrawIndexedFrame(canvas, stride, descriptor, activeColorTable, graphicControl, indices);
                portableFrames.Add(CreateFrame(canvas, canvasWidth, canvasHeight, stride, graphicControl, descriptor));

                pendingDisposal = new PendingDisposal(
                    graphicControl.DisposalMethod,
                    descriptor.Left,
                    descriptor.Top,
                    descriptor.Width,
                    descriptor.Height,
                    savedCanvas);
                graphicControl = default;
            }

            if (portableFrames.Count == 0)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            return new ReadOnlyCollection<BitmapFrame>(portableFrames);
        }

        private static void FillBackground(byte[] canvas, byte[] globalColorTable, int backgroundColorIndex)
        {
            if (globalColorTable == null)
            {
                return;
            }

            int colorOffset = backgroundColorIndex * 3;
            if (colorOffset < 0 || colorOffset + 2 >= globalColorTable.Length)
            {
                return;
            }

            for (int offset = 0; offset < canvas.Length; offset += 4)
            {
                canvas[offset + 0] = globalColorTable[colorOffset + 2];
                canvas[offset + 1] = globalColorTable[colorOffset + 1];
                canvas[offset + 2] = globalColorTable[colorOffset + 0];
                canvas[offset + 3] = 0xFF;
            }
        }

        private static void ApplyPendingDisposal(
            byte[] canvas,
            int stride,
            PendingDisposal pendingDisposal,
            byte[] globalColorTable,
            int backgroundColorIndex)
        {
            if (pendingDisposal.Method == 3 && pendingDisposal.SavedCanvas != null)
            {
                Buffer.BlockCopy(pendingDisposal.SavedCanvas, 0, canvas, 0, canvas.Length);
                return;
            }

            if (pendingDisposal.Method != 2)
            {
                return;
            }

            byte b = 0;
            byte g = 0;
            byte r = 0;
            byte a = 0;
            if (globalColorTable != null)
            {
                int colorOffset = backgroundColorIndex * 3;
                if (colorOffset >= 0 && colorOffset + 2 < globalColorTable.Length)
                {
                    b = globalColorTable[colorOffset + 2];
                    g = globalColorTable[colorOffset + 1];
                    r = globalColorTable[colorOffset + 0];
                    a = 0xFF;
                }
            }

            for (int y = 0; y < pendingDisposal.Height; y++)
            {
                int rowOffset = checked((pendingDisposal.Top + y) * stride + pendingDisposal.Left * 4);
                for (int x = 0; x < pendingDisposal.Width; x++)
                {
                    int offset = rowOffset + x * 4;
                    canvas[offset + 0] = b;
                    canvas[offset + 1] = g;
                    canvas[offset + 2] = r;
                    canvas[offset + 3] = a;
                }
            }
        }

        private static void ValidateFrameBounds(int canvasWidth, int canvasHeight, ImageDescriptor descriptor)
        {
            if (descriptor.Width <= 0 ||
                descriptor.Height <= 0 ||
                descriptor.Left < 0 ||
                descriptor.Top < 0 ||
                descriptor.Left + descriptor.Width > canvasWidth ||
                descriptor.Top + descriptor.Height > canvasHeight)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }
        }

        private static void DrawIndexedFrame(
            byte[] canvas,
            int stride,
            ImageDescriptor descriptor,
            byte[] colorTable,
            GraphicControl graphicControl,
            byte[] indices)
        {
            for (int y = 0; y < descriptor.Height; y++)
            {
                int rowOffset = checked((descriptor.Top + y) * stride + descriptor.Left * 4);
                int sourceRowOffset = checked(y * descriptor.Width);
                for (int x = 0; x < descriptor.Width; x++)
                {
                    int colorIndex = indices[sourceRowOffset + x];
                    if (graphicControl.HasTransparentColor &&
                        colorIndex == graphicControl.TransparentColorIndex)
                    {
                        continue;
                    }

                    int colorOffset = colorIndex * 3;
                    if (colorOffset < 0 || colorOffset + 2 >= colorTable.Length)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    int targetOffset = rowOffset + x * 4;
                    canvas[targetOffset + 0] = colorTable[colorOffset + 2];
                    canvas[targetOffset + 1] = colorTable[colorOffset + 1];
                    canvas[targetOffset + 2] = colorTable[colorOffset + 0];
                    canvas[targetOffset + 3] = 0xFF;
                }
            }
        }

        private static BitmapFrame CreateFrame(
            byte[] canvas,
            int width,
            int height,
            int stride,
            GraphicControl graphicControl,
            ImageDescriptor descriptor)
        {
            byte[] pixels = (byte[])canvas.Clone();
            BitmapSource source = BitmapSource.Create(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);

            BitmapMetadata metadata = CreateFrameMetadata(graphicControl, descriptor);
            BitmapFrame frame = BitmapFrame.Create(source, null, metadata, null);
            if (!frame.IsFrozen && frame.CanFreeze)
            {
                frame.Freeze();
            }

            return frame;
        }

        private static BitmapMetadata CreateFrameMetadata(GraphicControl graphicControl, ImageDescriptor descriptor)
        {
            var queries = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["/grctlext/Disposal"] = (byte)graphicControl.DisposalMethod,
                ["/grctlext/Delay"] = (ushort)graphicControl.Delay,
                ["/grctlext/TransparencyFlag"] = graphicControl.HasTransparentColor,
                ["/grctlext/TransparentColorIndex"] = (byte)graphicControl.TransparentColorIndex,
                ["/imgdesc/Left"] = (ushort)descriptor.Left,
                ["/imgdesc/Top"] = (ushort)descriptor.Top,
                ["/imgdesc/Width"] = (ushort)descriptor.Width,
                ["/imgdesc/Height"] = (ushort)descriptor.Height,
                ["/imgdesc/InterlaceFlag"] = descriptor.Interlaced,
            };

            return new BitmapMetadata("gif", MILGuidData.GUID_ContainerFormatGif, queries);
        }

        private static byte[] DecodeLzwIndices(byte minimumCodeSize, byte[] compressedData, int expectedPixelCount)
        {
            if (minimumCodeSize < 1 || minimumCodeSize > 8)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            int clearCode = 1 << minimumCodeSize;
            int endCode = clearCode + 1;
            int nextCode = endCode + 1;
            int codeSize = minimumCodeSize + 1;
            var dictionary = CreateInitialDictionary(clearCode);
            var reader = new GifBitReader(compressedData);
            List<byte> output = new List<byte>(expectedPixelCount);
            byte[] previous = null;

            while (output.Count < expectedPixelCount)
            {
                int code = reader.ReadCode(codeSize);
                if (code < 0)
                {
                    break;
                }

                if (code == clearCode)
                {
                    dictionary = CreateInitialDictionary(clearCode);
                    nextCode = endCode + 1;
                    codeSize = minimumCodeSize + 1;
                    previous = null;
                    continue;
                }

                if (code == endCode)
                {
                    break;
                }

                byte[] entry;
                if (code < dictionary.Count && dictionary[code] != null)
                {
                    entry = dictionary[code];
                }
                else if (code == nextCode && previous != null)
                {
                    entry = AppendByte(previous, previous[0]);
                }
                else
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                AddDecodedBytes(output, entry, expectedPixelCount);
                if (previous != null && nextCode < 4096)
                {
                    dictionary.Add(AppendByte(previous, entry[0]));
                    nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }
                }

                previous = entry;
            }

            if (output.Count != expectedPixelCount)
            {
                throw new FileFormatException(null, SR.Image_CantDealWithStream);
            }

            return output.ToArray();
        }

        private static List<byte[]> CreateInitialDictionary(int clearCode)
        {
            List<byte[]> dictionary = new List<byte[]>(4096);
            for (int i = 0; i < clearCode; i++)
            {
                dictionary.Add(new[] { (byte)i });
            }

            dictionary.Add(null);
            dictionary.Add(null);
            return dictionary;
        }

        private static byte[] AppendByte(byte[] data, byte value)
        {
            byte[] result = new byte[data.Length + 1];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            result[data.Length] = value;
            return result;
        }

        private static void AddDecodedBytes(List<byte> output, byte[] entry, int expectedPixelCount)
        {
            for (int i = 0; i < entry.Length && output.Count < expectedPixelCount; i++)
            {
                output.Add(entry[i]);
            }
        }

        private static byte[] Deinterlace(byte[] source, int width, int height)
        {
            byte[] target = new byte[source.Length];
            int sourceOffset = 0;
            CopyInterlacePass(source, target, width, height, 0, 8, ref sourceOffset);
            CopyInterlacePass(source, target, width, height, 4, 8, ref sourceOffset);
            CopyInterlacePass(source, target, width, height, 2, 4, ref sourceOffset);
            CopyInterlacePass(source, target, width, height, 1, 2, ref sourceOffset);
            return target;
        }

        private static void CopyInterlacePass(
            byte[] source,
            byte[] target,
            int width,
            int height,
            int startRow,
            int rowStep,
            ref int sourceOffset)
        {
            for (int y = startRow; y < height; y += rowStep)
            {
                Buffer.BlockCopy(source, sourceOffset, target, y * width, width);
                sourceOffset += width;
            }
        }

        private static byte[] ReadRemainingBytes(Stream stream)
        {
            using MemoryStream memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static bool IsGifSignature(ReadOnlySpan<byte> signature)
        {
            return signature.Length >= 6 &&
                signature[0] == (byte)'G' &&
                signature[1] == (byte)'I' &&
                signature[2] == (byte)'F' &&
                signature[3] == (byte)'8' &&
                (signature[4] == (byte)'7' || signature[4] == (byte)'9') &&
                signature[5] == (byte)'a';
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

        private readonly struct GraphicControl
        {
            public GraphicControl(int disposalMethod, int delay, bool hasTransparentColor, int transparentColorIndex)
            {
                DisposalMethod = disposalMethod;
                Delay = delay;
                HasTransparentColor = hasTransparentColor;
                TransparentColorIndex = transparentColorIndex;
            }

            public int DisposalMethod { get; }

            public int Delay { get; }

            public bool HasTransparentColor { get; }

            public int TransparentColorIndex { get; }
        }

        private readonly struct ImageDescriptor
        {
            public ImageDescriptor(
                int left,
                int top,
                int width,
                int height,
                bool interlaced,
                byte[] localColorTable)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                Interlaced = interlaced;
                LocalColorTable = localColorTable;
            }

            public int Left { get; }

            public int Top { get; }

            public int Width { get; }

            public int Height { get; }

            public bool Interlaced { get; }

            public byte[] LocalColorTable { get; }
        }

        private readonly struct PendingDisposal
        {
            public PendingDisposal(
                int method,
                int left,
                int top,
                int width,
                int height,
                byte[] savedCanvas)
            {
                Method = method;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
                SavedCanvas = savedCanvas;
            }

            public int Method { get; }

            public int Left { get; }

            public int Top { get; }

            public int Width { get; }

            public int Height { get; }

            public byte[] SavedCanvas { get; }
        }

        private ref struct GifReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _offset;

            public GifReader(byte[] data)
            {
                _data = data;
                _offset = 0;
            }

            public bool EndOfData => _offset >= _data.Length;

            public byte ReadByte()
            {
                if (_offset >= _data.Length)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                return _data[_offset++];
            }

            public int ReadUInt16()
            {
                int lo = ReadByte();
                int hi = ReadByte();
                return lo | (hi << 8);
            }

            public void Skip(int count)
            {
                if (count < 0 || _offset + count > _data.Length)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                _offset += count;
            }

            public byte[] ReadColorTable(int colorCount)
            {
                int byteCount = checked(colorCount * 3);
                if (_offset + byteCount > _data.Length)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                byte[] colorTable = _data.Slice(_offset, byteCount).ToArray();
                _offset += byteCount;
                return colorTable;
            }

            public GraphicControl ReadGraphicControl()
            {
                byte blockSize = ReadByte();
                if (blockSize != 4)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                byte packed = ReadByte();
                int delay = ReadUInt16();
                int transparentColorIndex = ReadByte();
                byte terminator = ReadByte();
                if (terminator != 0)
                {
                    throw new FileFormatException(null, SR.Image_CantDealWithStream);
                }

                return new GraphicControl(
                    (packed >> 2) & 0x07,
                    delay,
                    (packed & 0x01) != 0,
                    transparentColorIndex);
            }

            public ImageDescriptor ReadImageDescriptor()
            {
                int left = ReadUInt16();
                int top = ReadUInt16();
                int width = ReadUInt16();
                int height = ReadUInt16();
                byte packed = ReadByte();
                bool interlaced = (packed & 0x40) != 0;
                byte[] localColorTable = null;
                if ((packed & 0x80) != 0)
                {
                    localColorTable = ReadColorTable(1 << ((packed & 0x07) + 1));
                }

                return new ImageDescriptor(left, top, width, height, interlaced, localColorTable);
            }

            public byte[] ReadSubBlocks()
            {
                using MemoryStream memory = new MemoryStream();
                while (true)
                {
                    byte blockSize = ReadByte();
                    if (blockSize == 0)
                    {
                        return memory.ToArray();
                    }

                    if (_offset + blockSize > _data.Length)
                    {
                        throw new FileFormatException(null, SR.Image_CantDealWithStream);
                    }

                    memory.Write(_data.Slice(_offset, blockSize));
                    _offset += blockSize;
                }
            }

            public void SkipSubBlocks()
            {
                while (true)
                {
                    byte blockSize = ReadByte();
                    if (blockSize == 0)
                    {
                        return;
                    }

                    Skip(blockSize);
                }
            }
        }

        private ref struct GifBitReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _bitOffset;

            public GifBitReader(byte[] data)
            {
                _data = data;
                _bitOffset = 0;
            }

            public int ReadCode(int codeSize)
            {
                if (codeSize <= 0 || codeSize > 12 || _bitOffset + codeSize > _data.Length * 8)
                {
                    return -1;
                }

                int code = 0;
                for (int bit = 0; bit < codeSize; bit++)
                {
                    int absoluteBit = _bitOffset + bit;
                    int value = (_data[absoluteBit / 8] >> (absoluteBit % 8)) & 1;
                    code |= value << bit;
                }

                _bitOffset += codeSize;
                return code;
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
