using System;
using System.Runtime.InteropServices;
using Metal;
using UIKit;
using ProGPU.Backend;

namespace ProGPU.Wpf.iOS;

/// <summary>
/// A UIView sub-class that hosts a CAMetalLayer and initialises ProGPU/WebGPU rendering
/// on iOS.  Embed this view inside your UIViewController hierarchy to get WPF content
/// rendered via Metal.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
///   var host = new WpfMetalView(CGRect.Empty);
///   host.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
///   View.AddSubview(host);
/// </code>
/// The view automatically (re)configures the swapchain when its bounds change.
/// </remarks>
public sealed class WpfMetalView : UIView
{
    private WgpuContext? _context;
    private CAMetalLayer? _metalLayer;

    public WgpuContext? GpuContext => _context;

    // Override the layer class so UIKit creates a CAMetalLayer
    [Export("layerClass")]
    public static Class LayerClass() => new Class(typeof(CAMetalLayer));

    public WpfMetalView(CoreGraphics.CGRect frame) : base(frame)
    {
        ContentScaleFactor = UIScreen.MainScreen.Scale;
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        // On first layout (or orientation change) wire up Metal surface
        _metalLayer = (CAMetalLayer)Layer;
        _metalLayer.Device = MTLDevice.SystemDefault;
        _metalLayer.PixelFormat = MTLPixelFormat.BGRA8Unorm;
        _metalLayer.FramebufferOnly = true;

        uint w = (uint)(Bounds.Width  * ContentScaleFactor);
        uint h = (uint)(Bounds.Height * ContentScaleFactor);
        _metalLayer.DrawableSize = new CoreGraphics.CGSize(w, h);

        if (_context == null)
        {
            _context = new WgpuContext();
            // Pass the CAMetalLayer pointer as the native surface handle.
            // WgpuContext.CreateSurfaceFromHandle wraps it in SurfaceDescriptorFromMetalLayer.
            _context.InitializeFromHandle(_metalLayer.Handle, w, h);
        }
        else
        {
            // On resize just reconfigure the swapchain
            _context.ConfigureSwapChain(w, h);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _context?.Dispose();
        base.Dispose(disposing);
    }
}
