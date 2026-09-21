// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Windows.Media.Composition;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    /// <summary>
    /// Creates presentation image sources backed by portable native image providers.
    /// </summary>
    public static class PortableNativeImageSourceFactory
    {
        public static ImageSource Create(IPortableNativeImageSource nativeImageSource)
        {
            if (nativeImageSource == null)
            {
                throw new ArgumentNullException(nameof(nativeImageSource));
            }

            int pixelWidth = nativeImageSource.PixelWidth;
            int pixelHeight = nativeImageSource.PixelHeight;
            if (pixelWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nativeImageSource), "The native image width must be positive.");
            }

            if (pixelHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nativeImageSource), "The native image height must be positive.");
            }

            double dpiX = nativeImageSource.DpiX;
            double dpiY = nativeImageSource.DpiY;
            if (!double.IsFinite(dpiX) || !double.IsFinite(dpiY) || dpiX <= 0 || dpiY <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nativeImageSource), "The native image DPI must be finite and positive.");
            }

            return new PortableNativeImageSource(nativeImageSource, pixelWidth, pixelHeight, dpiX, dpiY);
        }

        private sealed class PortableNativeImageSource : ImageSource, IPortableNativeImageSource
        {
            private readonly IPortableNativeImageSource _nativeImageSource;
            private readonly int _pixelWidth;
            private readonly int _pixelHeight;
            private readonly double _dpiX;
            private readonly double _dpiY;

            internal PortableNativeImageSource(
                IPortableNativeImageSource nativeImageSource,
                int pixelWidth,
                int pixelHeight,
                double dpiX,
                double dpiY)
            {
                _nativeImageSource = nativeImageSource;
                _pixelWidth = pixelWidth;
                _pixelHeight = pixelHeight;
                _dpiX = dpiX;
                _dpiY = dpiY;
            }

            public override double Width
            {
                get
                {
                    ReadPreamble();
                    return PixelsToDIPs(_dpiX, _pixelWidth);
                }
            }

            public override double Height
            {
                get
                {
                    ReadPreamble();
                    return PixelsToDIPs(_dpiY, _pixelHeight);
                }
            }

            public override ImageMetadata Metadata
            {
                get
                {
                    ReadPreamble();
                    return null;
                }
            }

            int IPortableNativeImageSource.PixelWidth => _pixelWidth;

            int IPortableNativeImageSource.PixelHeight => _pixelHeight;

            double IPortableNativeImageSource.DpiX => _dpiX;

            double IPortableNativeImageSource.DpiY => _dpiY;

            bool IPortableNativeImageSource.TryGetPortableNativeImage(out object nativeImage)
            {
                ReadPreamble();
                return _nativeImageSource.TryGetPortableNativeImage(out nativeImage);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new PortableNativeImageSource(_nativeImageSource, _pixelWidth, _pixelHeight, _dpiX, _dpiY);
            }

            protected override bool FreezeCore(bool isChecking)
            {
                // The provider owns a live backend resource whose mutable state is
                // outside the WPF property system, so this carrier cannot be frozen.
                return false;
            }

            internal override DUCE.ResourceHandle AddRefOnChannelCore(DUCE.Channel channel)
            {
                // Portable native images are consumed by the managed replay sink.
                // They deliberately have no legacy MIL resource handle.
                return DUCE.ResourceHandle.Null;
            }

            internal override void ReleaseOnChannelCore(DUCE.Channel channel)
            {
            }

            internal override DUCE.ResourceHandle GetHandleCore(DUCE.Channel channel)
            {
                return DUCE.ResourceHandle.Null;
            }

            internal override int GetChannelCountCore()
            {
                return 0;
            }

            internal override DUCE.Channel GetChannelCore(int index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
