using System;
using System.Runtime.CompilerServices;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using ProGPU.Backend;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfBitmapCacheBrushSourceLookup
{
    // All access, source events and recording disposal follow the rendering
    // thread contract. Empty entries are removed with the final command lease.
    [ThreadStatic] private static CachedPictureSourceCache<Key>? s_sources;

    internal static CachedPictureLease Acquire(IPortableBitmapCacheBrushSource source,
        WgpuContext? context, WpfViewport3DTextureCache? viewportCache,
        Func<object?, ImageSource?>? adapter)
    {
        var key = new Key(source, context, viewportCache, adapter);
        return (s_sources ??= new()).Acquire(key, key, static state =>
            new WpfBitmapCacheBrushPictureSource(state.Source, state.Context,
                state.ViewportCache, state.Adapter == null ? null : new ImageAdapter(state.Adapter)));
    }

    private sealed class ImageAdapter(Func<object?, ImageSource?> adapter) : IWpfImageSourceAdapter
    {
        public ImageSource? AdaptImageSource(object? source) => adapter(source);
    }

    private readonly record struct Key(IPortableBitmapCacheBrushSource Source,
        WgpuContext? Context, WpfViewport3DTextureCache? ViewportCache, Func<object?, ImageSource?>? Adapter)
    {
        public bool Equals(Key other) => ReferenceEquals(Source, other.Source)
            && ReferenceEquals(Context, other.Context) && ReferenceEquals(ViewportCache, other.ViewportCache)
            && Equals(Adapter, other.Adapter);
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Source),
            Context == null ? 0 : RuntimeHelpers.GetHashCode(Context),
            ViewportCache == null ? 0 : RuntimeHelpers.GetHashCode(ViewportCache),
            Adapter?.GetHashCode() ?? 0);
    }
}
