using System;
using Android.Content;
using Android.Views;
using Android.Runtime;
using ProGPU.Backend;

namespace ProGPU.Wpf.Android;

/// <summary>
/// A <see cref="SurfaceView"/> that hosts ProGPU WebGPU rendering on Android.
/// The view uses the underlying <c>ANativeWindow</c> handle (exposed via
/// <see cref="ISurfaceHolder"/>) to create a WebGPU surface backed by Vulkan.
/// </summary>
/// <remarks>
/// Usage in your Activity/Fragment:
/// <code>
///   var host = new WpfSurfaceView(this);
///   SetContentView(host);
/// </code>
/// </remarks>
public sealed class WpfSurfaceView : SurfaceView, ISurfaceHolderCallback
{
    private WgpuContext? _context;

    public WgpuContext? GpuContext => _context;

    public WpfSurfaceView(Context context) : base(context)
    {
        Holder?.AddCallback(this);
    }

    public WpfSurfaceView(Context context, global::Android.Util.IAttributeSet? attrs)
        : base(context, attrs)
    {
        Holder?.AddCallback(this);
    }

    // ISurfaceHolderCallback ---------------------------------------------------

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        // ANativeWindow_fromSurface is not directly exposed in .NET Android bindings.
        // We obtain the native window handle through the JNI handle of the Surface object.
        // WgpuContext.CreateSurfaceFromHandle wraps it via SurfaceDescriptorFromAndroidNativeWindow.
        var surface = holder.Surface;
        if (surface == null) return;

        IntPtr nativeWindowHandle = GetANativeWindowHandle(surface);
        uint w = (uint)(surface.Describe()?.Width ?? Width);
        uint h = (uint)(surface.Describe()?.Height ?? Height);

        _context = new WgpuContext();
        _context.InitializeFromHandle(nativeWindowHandle, w, h);
    }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
        _context?.ConfigureSwapChain((uint)width, (uint)height);
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _context?.Dispose();
        _context = null;
    }

    // Helpers ------------------------------------------------------------------

    /// <summary>
    /// Returns the <c>ANativeWindow*</c> for an Android <see cref="global::Android.Views.Surface"/>.
    /// Uses the JNI handle exposed via <see cref="Java.Lang.Object.Handle"/> as the bridge; the
    /// wgpu-android backend accepts either a JNI <c>jobject</c> or a real <c>ANativeWindow*</c>.
    /// </summary>
    private static IntPtr GetANativeWindowHandle(global::Android.Views.Surface surface) =>
        surface.Handle;  // wgpu-android bridge accepts the JNI jobject handle directly
}
