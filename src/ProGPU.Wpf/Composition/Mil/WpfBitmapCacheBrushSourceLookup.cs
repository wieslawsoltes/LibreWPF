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
        if (!source.TryGetPortableBitmapCacheBrush(out var descriptor))
            throw new NotSupportedException("The typed cache-brush descriptor is unavailable.");
        if (adapter?.Target is IWpfShaderRecordingAdapterSource { RecordsOwnedShaderImages: true })
        {
            // An owned shader recipe cannot keep a live recapture callback:
            // record this exact nested cache generation while source ownership
            // and recursion guards are active. The ordinary live lookup below
            // remains unchanged for ordinary consumers.
            var snapshot = new RecordedSource(WpfBitmapCacheBrushCapture.Create(
                new PortableBitmapCacheBrushCaptureSource(descriptor.InternalTarget, descriptor.BitmapCache),
                context, viewportCache, new ImageAdapter(adapter)));
            try
            {
                using var recorded = new CachedPictureSourceCache<object>();
                return recorded.Acquire(new object(), snapshot, static value => value);
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }
        var key = new Key(descriptor.InternalTarget, descriptor.BitmapCache, context, viewportCache, adapter);
        return (s_sources ??= new()).Acquire(key, key, static state =>
            new WpfBitmapCacheBrushPictureSource(new PortableBitmapCacheBrushCaptureSource(state.Target, state.Cache), state.Context,
                state.ViewportCache, state.Adapter == null ? null : new ImageAdapter(state.Adapter)));
    }

    private sealed class ImageAdapter(Func<object?, ImageSource?> adapter)
        : IWpfImageSourceAdapter, IWpfShaderRecordingAdapterSource
    {
        public ImageSource? AdaptImageSource(object? source) => adapter(source);
        public bool RecordsOwnedShaderImages =>
            adapter.Target is IWpfShaderRecordingAdapterSource { RecordsOwnedShaderImages: true };
        public WpfShaderRecordingImageSourceAdapter? CreateShaderRecordingAdapter() =>
            (adapter.Target as IWpfShaderRecordingAdapterSource)?.CreateShaderRecordingAdapter();
    }

    private sealed class RecordedSource(WpfBitmapCacheBrushCapture capture) : ICachedPictureSource
    {
        private WpfBitmapCacheBrushCapture? _capture = capture;
        public event EventHandler? Invalidated { add { } remove { } }
        public CachedPictureSnapshot Capture()
        {
            var value = _capture ?? throw new ObjectDisposedException(nameof(RecordedSource));
            return new CachedPictureSnapshot(value.Picture.Clone(),
                new global::ProGPU.Scene.Rect((float)value.Bounds.X, (float)value.Bounds.Y,
                    (float)value.Bounds.Width, (float)value.Bounds.Height),
                (float)value.CachePolicy.RenderAtScale, value.CachePolicy.EnableClearType);
        }
        public void Dispose()
        {
            var value = _capture;
            _capture = null;
            value?.Dispose();
        }
    }

    private readonly record struct Key(object? Target, object? Cache,
        WgpuContext? Context, WpfViewport3DTextureCache? ViewportCache, Func<object?, ImageSource?>? Adapter)
    {
        public bool Equals(Key other) => ReferenceEquals(Target, other.Target) && ReferenceEquals(Cache, other.Cache)
            && ReferenceEquals(Context, other.Context) && ReferenceEquals(ViewportCache, other.ViewportCache)
            && Equals(Adapter, other.Adapter);
        public override int GetHashCode() => HashCode.Combine(Target == null ? 0 : RuntimeHelpers.GetHashCode(Target),
            Cache == null ? 0 : RuntimeHelpers.GetHashCode(Cache),
            Context == null ? 0 : RuntimeHelpers.GetHashCode(Context),
            ViewportCache == null ? 0 : RuntimeHelpers.GetHashCode(ViewportCache),
            Adapter?.GetHashCode() ?? 0);
    }
}
