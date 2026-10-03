using System;
using System.Windows.Media.Imaging;
using ProGPU.Backend;
using ProGPU.Scene;
using Silk.NET.WebGPU;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>An immutable GPU copy owned by one source recording generation.</summary>
internal sealed class WpfOwnedShaderImage : BitmapSource, IProGpuTextureLeaseSource, IDisposable
{
    private OwnedShaderEffectTexture? _owner;

    private WpfOwnedShaderImage(OwnedShaderEffectTexture owner) => _owner = owner;

    internal OwnedShaderEffectTexture Owner => _owner ?? throw new ObjectDisposedException(nameof(WpfOwnedShaderImage));
    public override GpuTexture GpuTexture => Owner.Texture;
    public override int PixelWidth => checked((int)GpuTexture.Width);
    public override int PixelHeight => checked((int)GpuTexture.Height);

    internal static WpfOwnedShaderImage Capture(ImageSource image, WgpuContext context)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (context.RenderLock)
        {
            if (!WpfBitmapSourceImageAdapter.TryGetGpuTexture(image, out var source) ||
                !ReferenceEquals(source.Context, context) || source.IsDisposed ||
                source.Dimension != GpuTextureDimension.Dimension2D || source.DepthOrArrayLayers != 1 ||
                source.SampleCount != 1 || (source.Usage & TextureUsage.CopySrc) == 0)
                throw new NotSupportedException("A retained shader image needs its actual copyable owned-device texture.");
            uint generation = source.Generation;
            var texture = new GpuTexture(context, source.Width, source.Height, source.Format,
                TextureUsage.CopyDst | TextureUsage.CopySrc | TextureUsage.TextureBinding | TextureUsage.RenderAttachment,
                "Retained source shader image", alphaMode: source.AlphaMode);
            try
            {
                texture.CopyBaseLevelFrom(source);
                if (source.IsDisposed || source.Generation != generation)
                    throw new InvalidOperationException("The original image changed during shader-source capture.");
                return new WpfOwnedShaderImage(new OwnedShaderEffectTexture(texture));
            }
            catch (Exception failure)
            {
                try { texture.Dispose(); }
                catch (Exception cleanup) { failure.Data["OwnedShaderImageCleanupFailure"] = cleanup; }
                throw;
            }
        }
    }

    bool IProGpuTextureSource.TryGetGpuTexture(out GpuTexture texture)
    {
        if (_owner is { } owner) return owner.TryGetGpuTexture(out texture);
        texture = null!;
        return false;
    }

    public bool TryAcquireGpuTextureLease(out IProGpuTextureLease lease)
    {
        if (_owner is { } owner) return owner.TryAcquireGpuTextureLease(out lease);
        lease = null!;
        return false;
    }

    public void Dispose() => System.Threading.Interlocked.Exchange(ref _owner, null)?.Dispose();
}
