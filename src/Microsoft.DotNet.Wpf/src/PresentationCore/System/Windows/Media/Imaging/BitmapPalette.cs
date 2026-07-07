// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal;
using MS.Win32.PresentationCore;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace System.Windows.Media.Imaging
{
    /// <summary>
    /// BitmapPalette class
    /// </summary>
    public sealed class BitmapPalette : DispatcherObject
    {
        #region Constructors

        /// <summary>
        /// No public default constructor
        /// </summary>
        private BitmapPalette()
        {
        }

        /// <summary>
        /// Create a palette from the list of colors.
        /// </summary>
        public BitmapPalette(IList<Color> colors)
        {
            ArgumentNullException.ThrowIfNull(colors);

            int count = colors.Count;

            if (count < 1 || count > 256)
            {
                throw new InvalidOperationException(SR.Format(SR.Image_PaletteZeroColors, null));
            }

            Color[] colorArray = new Color[count];

            for (int i = 0; i < count; ++i)
            {
                colorArray[i] = colors[i];
            }

            _colors = new ReadOnlyCollection<Color>(colorArray);

            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            _palette = CreateInternalPalette();

            UpdateUnmanaged();
        }

        /// <summary>
        /// Construct BitmapPalette from a BitmapSource.
        ///
        /// If the BitmapSource is already palettized, the corresponding
        /// palette is returned. Otherwise, a new palette is constructed from
        /// an analysis of the bitmap.
        /// </summary>
        /// <param name="bitmapSource">Bitmap to use for analysis</param>
        /// <param name="maxColorCount">Maximum number of colors</param>
        public BitmapPalette(BitmapSource bitmapSource, int maxColorCount)
        {
            // Note: we will never return a palette from BitmapPalettes.

            ArgumentNullException.ThrowIfNull(bitmapSource);

            if (!OperatingSystem.IsWindows())
            {
                InitializeManagedFromBitmapSource(bitmapSource, maxColorCount);
                return;
            }

            SafeMILHandle unmanagedBitmap = bitmapSource.WicSourceHandle;

            _palette = CreateInternalPalette();

            lock (bitmapSource.SyncObject)
            {
                HRESULT.Check(UnsafeNativeMethods.WICPalette.InitializeFromBitmap(
                            _palette,
                            unmanagedBitmap,
                            maxColorCount,
                            false));
            }

            UpdateManaged();
        }

        /// <summary>
        /// Constructs a bitmap from a known WICPaletteType (does not perform
        /// caching).
        ///
        /// Note: It is an error to modify the Color property of the
        /// constructed BitmapPalette.  Indeed, the returned BitmapPalette
        /// should probably be immediately frozen. Additionally, outside users
        /// will have no knowledge that this is a predefined palette (or which
        /// predefined palette it is). It is thus highly recommended that only
        /// the BitmapPalettes class use this constructor.
        /// </summary>
        internal BitmapPalette(WICPaletteType paletteType,
                bool addtransparentColor)
        {
            switch (paletteType)
            {
                case WICPaletteType.WICPaletteTypeFixedBW:
                case WICPaletteType.WICPaletteTypeFixedHalftone8:
                case WICPaletteType.WICPaletteTypeFixedHalftone27:
                case WICPaletteType.WICPaletteTypeFixedHalftone64:
                case WICPaletteType.WICPaletteTypeFixedHalftone125:
                case WICPaletteType.WICPaletteTypeFixedHalftone216:
                case WICPaletteType.WICPaletteTypeFixedHalftone252:
                case WICPaletteType.WICPaletteTypeFixedHalftone256:
                case WICPaletteType.WICPaletteTypeFixedGray4:
                case WICPaletteType.WICPaletteTypeFixedGray16:
                case WICPaletteType.WICPaletteTypeFixedGray256:
                    break;

                default:
                    throw new System.ArgumentException(SR.Format(SR.Image_PaletteFixedType, paletteType));
            }

            if (!OperatingSystem.IsWindows())
            {
                _colors = CreateManagedPredefinedColors(paletteType, addtransparentColor);
                return;
            }

            _palette = CreateInternalPalette();

            HRESULT.Check(UnsafeNativeMethods.WICPalette.InitializePredefined(
                        _palette,
                        paletteType,
                        addtransparentColor));

            // Fill in the Colors property.
            UpdateManaged();
        }

        internal BitmapPalette(SafeMILHandle unmanagedPalette)
        {
            _palette = unmanagedPalette;

            // Fill in the Colors property.
            UpdateManaged();
        }

        #endregion // Constructors

        #region Factory Methods

        /// <summary>
        /// Create a BitmapPalette from an unmanaged BitmapSource. If the
        /// bitmap is not paletteized, we return BitmapPalette.Empty. If the
        /// palette is of a known type, we will use BitmapPalettes.
        /// </summary>
        internal static BitmapPalette CreateFromBitmapSource(BitmapSource source)
        {
            Debug.Assert(source != null);

            if (!OperatingSystem.IsWindows())
            {
                return source._palette;
            }

            SafeMILHandle bitmapSource = source.WicSourceHandle;
            Debug.Assert(bitmapSource != null && !bitmapSource.IsInvalid);

            SafeMILHandle unmanagedPalette = CreateInternalPalette();

            BitmapPalette palette;

            // Don't throw on the HRESULT from this method.  If it returns failure,
            // that likely means that the source doesn't have a palette.
            lock (source.SyncObject)
            {
                int hr = UnsafeNativeMethods.WICBitmapSource.CopyPalette(
                            bitmapSource,
                            unmanagedPalette);

                if (hr != HRESULT.S_OK)
                {
                    return null;
                }
            }

            WICPaletteType paletteType;
            bool hasAlpha;

            HRESULT.Check(UnsafeNativeMethods.WICPalette.GetType(unmanagedPalette, out paletteType));
            HRESULT.Check(UnsafeNativeMethods.WICPalette.HasAlpha(unmanagedPalette, out hasAlpha));

            if (paletteType == WICPaletteType.WICPaletteTypeCustom ||
                paletteType == WICPaletteType.WICPaletteTypeOptimal)
            {
                palette = new BitmapPalette(unmanagedPalette);
            }
            else
            {
                palette = BitmapPalettes.FromMILPaletteType(paletteType, hasAlpha);
                Debug.Assert(palette != null);
            }

            return palette;
        }

        #endregion // Factory Methods

        #region Properties

        /// <summary>
        /// The contents of the palette.
        /// </summary>
        public IList<Color> Colors
        {
            get
            {
                return _colors;
            }
        }

        #endregion // Properties

        #region Internal Properties

        internal SafeMILHandle InternalPalette
        {
            get
            {
                if (_palette == null || _palette.IsInvalid)
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        throw new PlatformNotSupportedException("WIC palettes are not available outside Windows.");
                    }

                    _palette = CreateInternalPalette();
                    UpdateUnmanaged();
                }

                return _palette;
            }
        }

        #endregion // Internal Properties

        #region Static / Private Methods

        /// Returns if the Palette has any alpha within its colors
        internal static bool DoesPaletteHaveAlpha(BitmapPalette palette)
        {
            if (palette != null)
            {
                foreach (Color color in palette.Colors)
                {
                    if (color.A != 255)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static SafeMILHandle CreateInternalPalette()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("WIC palettes are not available outside Windows.");
            }

            SafeMILHandle palette = null;

            using (FactoryMaker myFactory = new FactoryMaker())
            {
                HRESULT.Check(UnsafeNativeMethods.WICImagingFactory.CreatePalette(
                            myFactory.ImagingFactoryPtr,
                            out palette));
                Debug.Assert(palette != null && !palette.IsInvalid);
            }

            return palette;
        }

        private static ReadOnlyCollection<Color> CreateManagedPredefinedColors(
            WICPaletteType paletteType,
            bool addtransparentColor)
        {
            List<Color> colors = new List<Color>(256);

            if (addtransparentColor)
            {
                colors.Add(Color.FromArgb(0, 0, 0, 0));
            }

            switch (paletteType)
            {
                case WICPaletteType.WICPaletteTypeFixedBW:
                    AddGrayRamp(colors, 2);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone8:
                    AddRgbCube(colors, 2);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone27:
                    AddRgbCube(colors, 3);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone64:
                    AddRgbCube(colors, 4);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone125:
                    AddRgbCube(colors, 5);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone216:
                    AddRgbCube(colors, 6);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone252:
                    AddRgbCube(colors, 6);
                    AddGrayRamp(colors, 36);
                    break;
                case WICPaletteType.WICPaletteTypeFixedHalftone256:
                    AddRgbCube(colors, 6);
                    AddGrayRamp(colors, 40);
                    break;
                case WICPaletteType.WICPaletteTypeFixedGray4:
                    AddGrayRamp(colors, 4);
                    break;
                case WICPaletteType.WICPaletteTypeFixedGray16:
                    AddGrayRamp(colors, 16);
                    break;
                case WICPaletteType.WICPaletteTypeFixedGray256:
                    AddGrayRamp(colors, 256);
                    break;
                default:
                    throw new System.ArgumentException(SR.Format(SR.Image_PaletteFixedType, paletteType));
            }

            if (colors.Count > 256)
            {
                colors.RemoveRange(256, colors.Count - 256);
            }

            return new ReadOnlyCollection<Color>(colors);
        }

        private static void AddRgbCube(List<Color> colors, int levels)
        {
            for (int red = 0; red < levels && colors.Count < 256; red++)
            {
                byte r = ScalePaletteChannel(red, levels);
                for (int green = 0; green < levels && colors.Count < 256; green++)
                {
                    byte g = ScalePaletteChannel(green, levels);
                    for (int blue = 0; blue < levels && colors.Count < 256; blue++)
                    {
                        byte b = ScalePaletteChannel(blue, levels);
                        colors.Add(Color.FromRgb(r, g, b));
                    }
                }
            }
        }

        private static void AddGrayRamp(List<Color> colors, int count)
        {
            for (int i = 0; i < count && colors.Count < 256; i++)
            {
                byte value = ScalePaletteChannel(i, count);
                colors.Add(Color.FromRgb(value, value, value));
            }
        }

        private static byte ScalePaletteChannel(int value, int count)
        {
            return count <= 1 ? (byte)0 : (byte)((value * 255) / (count - 1));
        }

        private void InitializeManagedFromBitmapSource(BitmapSource bitmapSource, int maxColorCount)
        {
            if (maxColorCount < 1 || maxColorCount > 256)
            {
                throw new InvalidOperationException(SR.Format(SR.Image_PaletteZeroColors, null));
            }

            BitmapPalette sourcePalette = bitmapSource.Palette;
            if (sourcePalette != null && sourcePalette.Colors.Count > 0)
            {
                int count = Math.Min(maxColorCount, sourcePalette.Colors.Count);
                Color[] colors = new Color[count];
                for (int i = 0; i < count; i++)
                {
                    colors[i] = sourcePalette.Colors[i];
                }

                _colors = new ReadOnlyCollection<Color>(colors);
                return;
            }

            List<Color> extractedColors = ExtractManagedColors(bitmapSource, maxColorCount);
            if (extractedColors.Count == 0)
            {
                throw new InvalidOperationException(SR.Format(SR.Image_PaletteZeroColors, null));
            }

            _colors = new ReadOnlyCollection<Color>(extractedColors);
        }

        private static List<Color> ExtractManagedColors(BitmapSource bitmapSource, int maxColorCount)
        {
            PixelFormat format = bitmapSource.Format;
            int bytesPerPixel;
            bool alpha;
            bool premultiplied;
            bool redFirst;

            switch (format.Format)
            {
                case PixelFormatEnum.Bgr24:
                    bytesPerPixel = 3;
                    alpha = false;
                    premultiplied = false;
                    redFirst = false;
                    break;
                case PixelFormatEnum.Rgb24:
                    bytesPerPixel = 3;
                    alpha = false;
                    premultiplied = false;
                    redFirst = true;
                    break;
                case PixelFormatEnum.Bgr32:
                    bytesPerPixel = 4;
                    alpha = false;
                    premultiplied = false;
                    redFirst = false;
                    break;
                case PixelFormatEnum.Bgra32:
                    bytesPerPixel = 4;
                    alpha = true;
                    premultiplied = false;
                    redFirst = false;
                    break;
                case PixelFormatEnum.Pbgra32:
                    bytesPerPixel = 4;
                    alpha = true;
                    premultiplied = true;
                    redFirst = false;
                    break;
                default:
                    return new List<Color>();
            }

            int width = bitmapSource.PixelWidth;
            int height = bitmapSource.PixelHeight;
            int stride = checked(width * bytesPerPixel);
            byte[] pixels = new byte[checked(stride * height)];
            bitmapSource.CopyPixels(pixels, stride, 0);

            List<Color> colors = new List<Color>(Math.Min(maxColorCount, 256));
            HashSet<uint> seen = new HashSet<uint>();
            for (int y = 0; y < height && colors.Count < maxColorCount; y++)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width && colors.Count < maxColorCount; x++)
                {
                    int offset = rowOffset + (x * bytesPerPixel);
                    byte a = alpha ? pixels[offset + 3] : (byte)255;
                    byte r = redFirst ? pixels[offset] : pixels[offset + 2];
                    byte g = pixels[offset + 1];
                    byte b = redFirst ? pixels[offset + 2] : pixels[offset];

                    if (premultiplied && a > 0 && a < 255)
                    {
                        r = Unpremultiply(r, a);
                        g = Unpremultiply(g, a);
                        b = Unpremultiply(b, a);
                    }

                    uint key = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                    if (seen.Add(key))
                    {
                        colors.Add(Color.FromArgb(a, r, g, b));
                    }
                }
            }

            return colors;
        }

        private static byte Unpremultiply(byte component, byte alpha)
        {
            return (byte)Math.Min(255, ((component * 255) + (alpha / 2)) / alpha);
        }

        /// <summary>
        /// Copy Colors down into the IMILPalette.
        /// </summary>
        /// Critical - is an unsafe method, calls into native code
        /// TreatAsSafe - No inputs are provided, no information is exposed.
        private unsafe void UpdateUnmanaged()
        {
            Debug.Assert(_palette != null && !_palette.IsInvalid);

            int numColors = Math.Min(256, _colors.Count);

            ImagePaletteColor[] paletteColorArray = new ImagePaletteColor[numColors];

            for (int i = 0; i < numColors; ++i)
            {
                Color color = _colors[i];
                paletteColorArray[i].B = color.B;
                paletteColorArray[i].G = color.G;
                paletteColorArray[i].R = color.R;
                paletteColorArray[i].A = color.A;
            }

            fixed (void* paletteColorArrayPinned = paletteColorArray)
            {
                HRESULT.Check(UnsafeNativeMethods.WICPalette.InitializeCustom(
                            _palette,
                            (IntPtr)paletteColorArrayPinned,
                            numColors));
            }
        }

        /// <summary>
        /// Copy the colors from IMILBitmapPalette into Colors.
        /// </summary>
        private void UpdateManaged()
        {
            Debug.Assert(_palette != null && !_palette.IsInvalid);

            int numColors = 0;
            int cActualColors = 0;
            HRESULT.Check(UnsafeNativeMethods.WICPalette.GetColorCount(_palette,
                        out numColors));

            List<Color> colors = new List<Color>();

            if (numColors < 1 || numColors > 256)
            {
                throw new InvalidOperationException(SR.Format(SR.Image_PaletteZeroColors, null));
            }
            else
            {
                ImagePaletteColor[] paletteColorArray = new ImagePaletteColor[numColors];
                unsafe
                {
                    fixed(void* paletteColorArrayPinned = paletteColorArray)
                    {
                        HRESULT.Check(UnsafeNativeMethods.WICPalette.GetColors(
                                    _palette,
                                    numColors,
                                    (IntPtr)paletteColorArrayPinned,
                                    out cActualColors));

                        Debug.Assert(cActualColors == numColors);
                    }
                }

                for (int i = 0; i < numColors; ++i)
                {
                    ImagePaletteColor c = paletteColorArray[i];

                    colors.Add(Color.FromArgb(c.A, c.R, c.G, c.B));
                }
            }

            _colors = new ReadOnlyCollection<Color>(colors);
        }

        #endregion // Private Methods

        /// <summary>
        /// ImagePaletteColor structure -- convenience for Interop
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ImagePaletteColor
        {
            /// <summary>
            /// blue channel: 0 - 255
            /// </summary>
            public byte B;
            /// <summary>
            /// green channel: 0 - 255
            /// </summary>
            public byte G;
            /// <summary>
            /// red channel: 0 - 255
            /// </summary>
            public byte R;
            /// <summary>
            /// alpha channel: 0 - 255
            /// </summary>
            public byte A;
        };

        // Note: We have a little trickery going on here. When a new BitmapPalette is
        // cloned, _palette isn't copied and so is reset to null. This means that the
        // next call to InternalPalette will create a new IWICPalette, which is exactly
        // the behavior that we want.
        private SafeMILHandle _palette = null; // IWICPalette*

        private IList<Color> _colors = ReadOnlyCollection<Color>.Empty;
    }
}
