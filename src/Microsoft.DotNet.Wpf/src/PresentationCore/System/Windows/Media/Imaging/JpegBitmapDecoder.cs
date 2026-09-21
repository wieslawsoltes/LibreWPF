// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

using System.IO;
using System.Collections.ObjectModel;
using Microsoft.Win32.SafeHandles;
using MS.Internal;
using System.Windows.Media;
using StbImageSharp;

namespace System.Windows.Media.Imaging
{
    #region JpegBitmapDecoder

    /// <summary>
    /// The built-in Microsoft Jpeg (Bitmap) Decoder.
    /// </summary>
    public sealed class JpegBitmapDecoder : BitmapDecoder
    {
        /// <summary>
        /// Don't allow construction of a decoder with no params
        /// </summary>
        private JpegBitmapDecoder()
        {
        }

        /// <summary>
        /// Create a JpegBitmapDecoder given the Uri
        /// </summary>
        /// <param name="bitmapUri">Uri to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public JpegBitmapDecoder(
            Uri bitmapUri,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapUri, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatJpeg)
        {
        }

        /// <summary>
        /// If this decoder cannot handle the bitmap stream, it will throw an exception.
        /// </summary>
        /// <param name="bitmapStream">Stream to decode</param>
        /// <param name="createOptions">Bitmap Create Options</param>
        /// <param name="cacheOption">Bitmap Caching Option</param>
        public JpegBitmapDecoder(
            Stream bitmapStream,
            BitmapCreateOptions createOptions,
            BitmapCacheOption cacheOption
            ) : base(bitmapStream, createOptions, cacheOption, MILGuidData.GUID_ContainerFormatJpeg)
        {
        }

        internal JpegBitmapDecoder(
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
        internal JpegBitmapDecoder(
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

            if (stream == null || !stream.CanSeek)
            {
                return false;
            }

            long startPosition = stream.Position;

            try
            {
                Span<byte> signature = stackalloc byte[3];
                if (!TryReadExactly(stream, signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                if (!IsJpegSignature(signature))
                {
                    stream.Position = startPosition;
                    return false;
                }

                stream.Position = startPosition;
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                int stride = checked(image.Width * 4);
                byte[] pixels = new byte[checked(stride * image.Height)];
                ConvertRgbaToBgra(image.Data, pixels);

                BitmapSource source = BitmapSource.Create(
                    image.Width,
                    image.Height,
                    96.0,
                    96.0,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);

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

        private static void ConvertRgbaToBgra(byte[] source, byte[] target)
        {
            for (int sourceOffset = 0; sourceOffset < source.Length; sourceOffset += 4)
            {
                target[sourceOffset + 0] = source[sourceOffset + 2];
                target[sourceOffset + 1] = source[sourceOffset + 1];
                target[sourceOffset + 2] = source[sourceOffset + 0];
                target[sourceOffset + 3] = source[sourceOffset + 3];
            }
        }

        private static bool IsJpegSignature(ReadOnlySpan<byte> signature)
        {
            return signature.Length >= 3 &&
                signature[0] == 0xFF &&
                signature[1] == 0xD8 &&
                signature[2] == 0xFF;
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
