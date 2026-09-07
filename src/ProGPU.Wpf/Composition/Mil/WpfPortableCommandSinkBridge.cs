using System.Runtime.CompilerServices;
using System.Windows.Media.ProGPU.Composition;
using System.Numerics;
using MediaBrush = System.Windows.Media.Brush;
using MediaTransform = System.Windows.Media.Transform;
using ProGpuEffectBase = global::ProGPU.Scene.EffectBase;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfPortableCommandSinkBridge
{
    public static bool TryPushOpacityMask(IWpfCompositionCommandSink sink, object? opacityMask,
        WpfReplayRect bounds, Func<object?, System.Windows.Media.ImageSource?>? imageSourceAdapter = null)
    {
        if (opacityMask is global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source)
            return sink is IWpfBitmapCacheBrushCommandSink cachedSink
                && cachedSink.PushBitmapCacheBrushOpacityMask(source, bounds, imageSourceAdapter);
        if (opacityMask == null) { sink.PushNoOpScope(); return true; }
        var brush = WpfResourceResolver.AdaptBrush(opacityMask);
        if (brush == null) return false;
        PushOpacityMask(sink, brush, bounds);
        return true;
    }

    public static void PushOpacityMask(
        IWpfCompositionCommandSink sink,
        MediaBrush? opacityMask,
        WpfReplayRect bounds)
    {
        if (sink is IWpfNativePrimitiveCommandSink nativeSink)
        {
            nativeSink.PushNativeOpacityMask(opacityMask, bounds);
            return;
        }

        WpfManagedCommandSinkBridge.PushOpacityMask(sink, opacityMask, bounds);
    }

    public static bool TryPushVisualEffect(
        IWpfCompositionCommandSink sink,
        ProGpuEffectBase effect,
        WpfReplayRect? bounds)
    {
        if (sink is IWpfNativeVisualEffectCommandSink nativeSink)
        {
            return nativeSink.PushNativeVisualEffect(effect, bounds);
        }

        if (sink is not IWpfVisualEffectCommandSink effectSink)
        {
            return false;
        }

        return bounds.HasValue
            ? WpfManagedCommandSinkBridge.PushVisualEffect(effectSink, effect, bounds.Value)
            : effectSink.PushVisualEffect(effect);
    }

    public static bool TryPushVisualCache(
        IWpfCompositionCommandSink sink,
        WpfReplayRect? bounds)
    {
        if (sink is IWpfNativeVisualCacheCommandSink nativeSink)
        {
            return nativeSink.PushNativeVisualCache(bounds);
        }

        return sink is IWpfVisualCacheCommandSink cacheSink
            && WpfManagedCommandSinkBridge.PushVisualCache(cacheSink, bounds);
    }

    public static bool TryPushDrawingCache(
        IWpfCompositionCommandSink sink,
        WpfReplayRect? bounds)
    {
        if (sink is IWpfNativeDrawingCacheCommandSink nativeSink)
        {
            return nativeSink.PushNativeDrawingCache(bounds);
        }

        return sink is IWpfDrawingCacheCommandSink cacheSink
            && WpfManagedCommandSinkBridge.PushDrawingCache(cacheSink, bounds);
    }

    public static void PushTransform(
        IWpfCompositionCommandSink sink,
        MediaTransform transform)
    {
        if (sink is IWpfNativeTransformCommandSink nativeSink
            && WpfResourceResolver.TryAdaptTransformMatrix(transform, out var nativeTransform))
        {
            nativeSink.PushNativeTransform(nativeTransform);
            return;
        }

        WpfManagedCommandSinkBridge.PushTransform(sink, transform);
    }

    public static void PushTransform(
        IWpfCompositionCommandSink sink,
        Matrix4x4 transform)
    {
        if (sink is IWpfNativeTransformCommandSink nativeSink)
        {
            nativeSink.PushNativeTransform(transform);
            return;
        }

        if (WpfResourceResolver.TryCreateManagedMatrixTransform(transform, out var mediaTransform))
        {
            WpfManagedCommandSinkBridge.PushTransform(sink, mediaTransform);
            return;
        }

        sink.PushNoOpScope();
    }
}

internal static class WpfManagedCommandSinkBridge
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PushOpacityMask(
        IWpfCompositionCommandSink sink,
        MediaBrush? opacityMask,
        WpfReplayRect bounds)
    {
        sink.PushOpacityMask(
            opacityMask,
            new System.Windows.Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool PushVisualEffect(
        IWpfVisualEffectCommandSink sink,
        ProGpuEffectBase effect,
        WpfReplayRect bounds)
    {
        return sink.PushVisualEffect(
            effect,
            new System.Windows.Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool PushVisualCache(
        IWpfVisualCacheCommandSink sink,
        WpfReplayRect? bounds)
    {
        return sink.PushVisualCache(bounds.HasValue
            ? new System.Windows.Rect(bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height)
            : null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool PushDrawingCache(
        IWpfDrawingCacheCommandSink sink,
        WpfReplayRect? bounds)
    {
        return sink.PushDrawingCache(bounds.HasValue
            ? new System.Windows.Rect(bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height)
            : null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PushTransform(
        IWpfCompositionCommandSink sink,
        MediaTransform transform)
    {
        sink.PushTransform(transform);
    }
}
