using System;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfBitmapSourceImageAdapter : IWpfImageSourceAdapter
{
    private static readonly ConditionalWeakTable<MediaImageSource, AdaptedTextureCache> s_adaptedTextures = new();

    public MediaImageSource? AdaptImageSource(object? imageSource)
    {
        if (imageSource == null)
        {
            return null;
        }

        if (imageSource is MediaImageSource mediaImageSource
            && CanProvideGpuTexture(mediaImageSource))
        {
            return mediaImageSource;
        }

        if (!TryGetPortableBitmapSourcePixels(
                imageSource,
                out var portablePixels,
                out var formatKind,
                out var cacheKey))
        {
            return null;
        }

        var width = portablePixels.Width;
        var height = portablePixels.Height;
        var context = ResolveGpuContext();
        if (imageSource is not MediaImageSource mediaSource)
        {
            return null;
        }

        if (s_adaptedTextures.TryGetValue(mediaSource, out var adapted)
            && adapted.TryGet(context, cacheKey, out _))
        {
            return mediaSource;
        }

        if (!TryConvertPortableBitmapSourceAsPbgra32Buffer(portablePixels, formatKind, out var pixelBuffer))
        {
            return null;
        }

        if (TryCreateGpuTexture(context, width, height, pixelBuffer, out var adaptedTexture))
        {
            s_adaptedTextures.GetValue(mediaSource, static _ => new AdaptedTextureCache())
                .Set(context, cacheKey, adaptedTexture);
            return mediaSource;
        }

        return null;
    }

    internal static bool CanProvideGpuTexture(object imageSource)
    {
        return imageSource is IProGpuTextureSource
            || imageSource is IPortableNativeImageSource;
    }

    internal static bool TryGetGpuTexture(MediaImageSource imageSource, out GpuTexture texture)
    {
        texture = null!;
        var currentContext = ResolveCurrentGpuContext();

        if (imageSource is IProGpuTextureSource textureSource)
        {
            try
            {
                if (textureSource.TryGetGpuTexture(out var resolvedTexture)
                    && IsUsableInContext(resolvedTexture, currentContext))
                {
                    texture = resolvedTexture;
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (imageSource is IPortableNativeImageSource nativeImageSource)
        {
            try
            {
                if (nativeImageSource.TryGetPortableNativeImage(out object? nativeImage)
                    && nativeImage is GpuTexture resolvedTexture
                    && IsUsableInContext(resolvedTexture, currentContext))
                {
                    texture = resolvedTexture;
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (s_adaptedTextures.TryGetValue(imageSource, out var adapted)
            && adapted.TryGet(currentContext, out texture))
        {
            return true;
        }

        return false;
    }

    private static bool TryCreateGpuTexture(
        WgpuContext context,
        int width,
        int height,
        Pbgra32PixelBuffer pixelBuffer,
        out GpuTexture texture)
    {
        texture = null!;

        try
        {
            texture = new GpuTexture(
                context,
                (uint)width,
                (uint)height,
                TextureFormat.Bgra8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst | TextureUsage.TextureBinding,
                "WPF BitmapSource Adapter Texture",
                alphaMode: GpuTextureAlphaMode.Premultiplied);
            texture.WritePbgra32(pixelBuffer);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        texture?.Dispose();
        texture = null!;
        return false;
    }

    private static WgpuContext? ResolveCurrentGpuContext()
    {
        var current = WgpuContext.Current;
        if (current != null && !current.IsDisposed)
        {
            return current;
        }

        if (WgpuContext.TryGetFirstActiveContext(out var active))
        {
            return active;
        }

        return null;
    }

    private static WgpuContext ResolveGpuContext()
    {
        var current = WgpuContext.Current;
        if (current != null && !current.IsDisposed)
        {
            return current;
        }

        if (WgpuContext.TryGetFirstActiveContext(out var active))
        {
            return active;
        }

        var context = new WgpuContext();
        context.Initialize(null);
        return context;
    }

    private static bool IsUsableInContext(GpuTexture texture, WgpuContext? context)
    {
        var textureContext = texture.Context;
        if (textureContext == null)
        {
            return !texture.IsDisposed;
        }

        return !texture.IsDisposed
            && !textureContext.IsDisposed
            && (context == null || ReferenceEquals(textureContext, context));
    }

    private sealed class AdaptedTextureCache
    {
        private readonly object _gate = new();
        private readonly ConditionalWeakTable<WgpuContext, AdaptedTextureEntry> _texturesByContext = new();

        public void Set(WgpuContext context, BitmapSourceTextureCacheKey cacheKey, GpuTexture texture)
        {
            GpuTexture? replacedTexture = null;
            lock (_gate)
            {
                if (_texturesByContext.TryGetValue(context, out var existing)
                    && !ReferenceEquals(existing.Texture, texture))
                {
                    replacedTexture = existing.Texture;
                }

                _texturesByContext.Remove(context);
                _texturesByContext.Add(context, new AdaptedTextureEntry(cacheKey, texture));
            }

            replacedTexture?.Dispose();
        }

        public bool TryGet(WgpuContext context, BitmapSourceTextureCacheKey cacheKey, out GpuTexture texture)
        {
            lock (_gate)
            {
                if (_texturesByContext.TryGetValue(context, out var entry)
                    && entry.CacheKey.Equals(cacheKey)
                    && IsUsableInContext(entry.Texture, context))
                {
                    texture = entry.Texture;
                    return true;
                }
            }

            texture = null!;
            return false;
        }

        public bool TryGet(WgpuContext? context, out GpuTexture texture)
        {
            if (context == null)
            {
                texture = null!;
                return false;
            }

            lock (_gate)
            {
                if (_texturesByContext.TryGetValue(context, out var entry)
                    && IsUsableInContext(entry.Texture, context))
                {
                    texture = entry.Texture;
                    return true;
                }
            }

            texture = null!;
            return false;
        }
    }

    private sealed record AdaptedTextureEntry(
        BitmapSourceTextureCacheKey CacheKey,
        GpuTexture Texture);

    internal readonly record struct BitmapSourceTextureCacheKey(
        int Width,
        int Height,
        int Stride,
        PortablePixelDataFormat Format,
        int PaletteLength,
        ulong PaletteHash,
        ulong PixelHash);

    internal static bool TryCopyPixelsAsPbgra32(
        object imageSource,
        int width,
        int height,
        out byte[] pixels,
        out int stride)
    {
        pixels = Array.Empty<byte>();
        stride = 0;

        if (!TryCopyPixelsAsPbgra32Buffer(imageSource, width, height, out var pixelBuffer))
        {
            return false;
        }

        pixels = pixelBuffer.Pixels;
        stride = pixelBuffer.Stride;
        return true;
    }

    internal static bool TryCopyPixelsAsPbgra32Buffer(
        object imageSource,
        int width,
        int height,
        out Pbgra32PixelBuffer pixelBuffer)
    {
        pixelBuffer = default;

        if (imageSource is not IPortableBitmapSourcePixelsSource portableSource
            || !TryGetPortableBitmapSourcePixels(
                portableSource,
                out var portablePixels,
                out var formatKind,
                out _))
        {
            return false;
        }

        if (!TryConvertPortableBitmapSourceAsPbgra32Buffer(portablePixels, formatKind, out pixelBuffer))
        {
            return false;
        }

        if (portablePixels.Width == width && portablePixels.Height == height)
        {
            return true;
        }

        pixelBuffer = default;
        return false;
    }

    internal static bool TryCopyPixelsAsRgba32(
        object imageSource,
        out byte[] pixels,
        out int width,
        out int height)
    {
        return TryCopyPixelsAsRgba32(
            imageSource,
            int.MaxValue,
            out pixels,
            out width,
            out height);
    }

    internal static bool TryCopyPixelsAsRgba32(
        object imageSource,
        int maxDimension,
        out byte[] pixels,
        out int width,
        out int height)
    {
        pixels = Array.Empty<byte>();
        width = 0;
        height = 0;

        if (maxDimension <= 0)
        {
            return false;
        }

        if (!TryGetPortableBitmapSourcePixels(
                imageSource,
                out var portablePixels,
                out var formatKind,
                out _) ||
            !TryConvertPortableBitmapSourceAsPbgra32Buffer(
                portablePixels,
                formatKind,
                out var pixelBuffer) ||
            !pixelBuffer.IsValid)
        {
            return false;
        }

        var sourceWidth = pixelBuffer.Width;
        var sourceHeight = pixelBuffer.Height;
        var sourceMaxDimension = Math.Max(sourceWidth, sourceHeight);
        if (sourceMaxDimension > maxDimension)
        {
            width = Math.Max(1, (int)Math.Round(sourceWidth * (double)maxDimension / sourceMaxDimension));
            height = Math.Max(1, (int)Math.Round(sourceHeight * (double)maxDimension / sourceMaxDimension));
        }
        else
        {
            width = sourceWidth;
            height = sourceHeight;
        }

        pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sourceY = y * sourceHeight / height;
            var sourceRow = sourceY * pixelBuffer.Stride;
            var destinationRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var sourceX = x * sourceWidth / width;
                var sourceOffset = sourceRow + sourceX * 4;
                var destinationOffset = destinationRow + x * 4;
                var alpha = pixelBuffer.Pixels[sourceOffset + 3];
                pixels[destinationOffset] = Unpremultiply(pixelBuffer.Pixels[sourceOffset + 2], alpha);
                pixels[destinationOffset + 1] = Unpremultiply(pixelBuffer.Pixels[sourceOffset + 1], alpha);
                pixels[destinationOffset + 2] = Unpremultiply(pixelBuffer.Pixels[sourceOffset], alpha);
                pixels[destinationOffset + 3] = alpha;
            }
        }

        return true;
    }

    private static byte Unpremultiply(byte component, byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        return (byte)Math.Min(255, (component * 255 + alpha / 2) / alpha);
    }

    private static bool TryGetPortableBitmapSourcePixels(
        object imageSource,
        out PortableBitmapSourcePixels portablePixels,
        out PixelDataFormat formatKind,
        out BitmapSourceTextureCacheKey cacheKey)
    {
        if (imageSource is IPortableBitmapSourcePixelsSource portableSource)
        {
            return TryGetPortableBitmapSourcePixels(
                portableSource,
                out portablePixels,
                out formatKind,
                out cacheKey);
        }

        portablePixels = null!;
        formatKind = default;
        cacheKey = default;
        return false;
    }

    private static bool TryGetPortableBitmapSourcePixels(
        IPortableBitmapSourcePixelsSource portableSource,
        out PortableBitmapSourcePixels portablePixels,
        out PixelDataFormat formatKind,
        out BitmapSourceTextureCacheKey cacheKey)
    {
        formatKind = default;
        cacheKey = default;

        if (!portableSource.TryGetPortableBitmapSourcePixels(out portablePixels)
            || portablePixels == null
            || portablePixels.Width <= 0
            || portablePixels.Height <= 0
            || portablePixels.Stride <= 0
            || portablePixels.Pixels == null
            || !TryMapPixelDataFormat(portablePixels.Format, out formatKind)
            || !TryCreateBitmapSourceTextureCacheKey(portablePixels, out cacheKey))
        {
            return false;
        }

        return true;
    }

    private static bool TryConvertPortableBitmapSourceAsPbgra32Buffer(
        PortableBitmapSourcePixels portablePixels,
        PixelDataFormat formatKind,
        out Pbgra32PixelBuffer pixelBuffer)
    {
        pixelBuffer = default;

        var palette = CreatePalette(portablePixels.Palette);
        if (PixelDataConverter.RequiresPalette(formatKind) && palette.Length == 0)
        {
            return false;
        }

        var sourceBuffer = new PixelDataBuffer(
            portablePixels.Width,
            portablePixels.Height,
            portablePixels.Stride,
            formatKind,
            portablePixels.Pixels,
            palette);
        if (!sourceBuffer.TryConvertToPbgra32(out var pbgra32Buffer))
        {
            return false;
        }

        pixelBuffer = pbgra32Buffer;
        return true;
    }

    internal static bool TryCreateBitmapSourceTextureCacheKey(
        PortableBitmapSourcePixels portablePixels,
        out BitmapSourceTextureCacheKey cacheKey)
    {
        cacheKey = default;

        if (portablePixels == null
            || portablePixels.Width <= 0
            || portablePixels.Height <= 0
            || portablePixels.Stride <= 0
            || portablePixels.Pixels == null)
        {
            return false;
        }

        var palette = portablePixels.Palette ?? Array.Empty<PortablePbgra32Color>();
        cacheKey = new BitmapSourceTextureCacheKey(
            portablePixels.Width,
            portablePixels.Height,
            portablePixels.Stride,
            portablePixels.Format,
            palette.Length,
            HashPalette(palette),
            HashBytes(portablePixels.Pixels));
        return true;
    }

    private static ulong HashPalette(ReadOnlySpan<PortablePbgra32Color> palette)
    {
        ulong hash = 14695981039346656037UL;
        for (var i = 0; i < palette.Length; i++)
        {
            var color = palette[i];
            hash = AddHashByte(hash, color.B);
            hash = AddHashByte(hash, color.G);
            hash = AddHashByte(hash, color.R);
            hash = AddHashByte(hash, color.A);
        }

        return hash;
    }

    private static ulong HashBytes(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037UL;
        for (var i = 0; i < bytes.Length; i++)
        {
            hash = AddHashByte(hash, bytes[i]);
        }

        return hash;
    }

    private static ulong AddHashByte(ulong hash, byte value)
    {
        return (hash ^ value) * 1099511628211UL;
    }

    private static bool TryMapPixelDataFormat(
        PortablePixelDataFormat portableFormat,
        out PixelDataFormat format)
    {
        switch (portableFormat)
        {
            case PortablePixelDataFormat.Pbgra32:
                format = PixelDataFormat.Pbgra32;
                return true;
            case PortablePixelDataFormat.Bgra32:
                format = PixelDataFormat.Bgra32;
                return true;
            case PortablePixelDataFormat.Bgr32:
                format = PixelDataFormat.Bgr32;
                return true;
            case PortablePixelDataFormat.Bgr101010:
                format = PixelDataFormat.Bgr101010;
                return true;
            case PortablePixelDataFormat.Bgr24:
                format = PixelDataFormat.Bgr24;
                return true;
            case PortablePixelDataFormat.Rgb24:
                format = PixelDataFormat.Rgb24;
                return true;
            case PortablePixelDataFormat.BlackWhite:
                format = PixelDataFormat.BlackWhite;
                return true;
            case PortablePixelDataFormat.Gray2:
                format = PixelDataFormat.Gray2;
                return true;
            case PortablePixelDataFormat.Gray4:
                format = PixelDataFormat.Gray4;
                return true;
            case PortablePixelDataFormat.Gray8:
                format = PixelDataFormat.Gray8;
                return true;
            case PortablePixelDataFormat.Gray16:
                format = PixelDataFormat.Gray16;
                return true;
            case PortablePixelDataFormat.Bgr555:
                format = PixelDataFormat.Bgr555;
                return true;
            case PortablePixelDataFormat.Bgr565:
                format = PixelDataFormat.Bgr565;
                return true;
            case PortablePixelDataFormat.Rgb48:
                format = PixelDataFormat.Rgb48;
                return true;
            case PortablePixelDataFormat.Rgba64:
                format = PixelDataFormat.Rgba64;
                return true;
            case PortablePixelDataFormat.Prgba64:
                format = PixelDataFormat.Prgba64;
                return true;
            case PortablePixelDataFormat.Cmyk32:
                format = PixelDataFormat.Cmyk32;
                return true;
            case PortablePixelDataFormat.Gray32Float:
                format = PixelDataFormat.Gray32Float;
                return true;
            case PortablePixelDataFormat.Rgb128Float:
                format = PixelDataFormat.Rgb128Float;
                return true;
            case PortablePixelDataFormat.Rgba128Float:
                format = PixelDataFormat.Rgba128Float;
                return true;
            case PortablePixelDataFormat.Prgba128Float:
                format = PixelDataFormat.Prgba128Float;
                return true;
            case PortablePixelDataFormat.Indexed1:
                format = PixelDataFormat.Indexed1;
                return true;
            case PortablePixelDataFormat.Indexed2:
                format = PixelDataFormat.Indexed2;
                return true;
            case PortablePixelDataFormat.Indexed4:
                format = PixelDataFormat.Indexed4;
                return true;
            case PortablePixelDataFormat.Indexed8:
                format = PixelDataFormat.Indexed8;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static Pbgra32Color[] CreatePalette(PortablePbgra32Color[]? portablePalette)
    {
        if (portablePalette == null || portablePalette.Length == 0)
        {
            return Array.Empty<Pbgra32Color>();
        }

        int count = Math.Min(256, portablePalette.Length);
        var palette = new Pbgra32Color[count];
        for (var i = 0; i < count; i++)
        {
            var color = portablePalette[i];
            palette[i] = new Pbgra32Color(color.B, color.G, color.R, color.A);
        }

        return palette;
    }

}
