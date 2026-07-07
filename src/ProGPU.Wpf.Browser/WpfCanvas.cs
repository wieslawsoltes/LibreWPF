using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.AspNetCore.Components;
using ProGPU.Backend;

namespace ProGPU.Wpf.Browser;

/// <summary>
/// A Blazor component that binds a WebGPU rendering context to an HTML <c>&lt;canvas&gt;</c>
/// element and runs the WPF retained-scene compositor inside the browser.
/// </summary>
/// <remarks>
/// Add to your Blazor page:
/// <code>
///   &lt;WpfCanvas Id="progpu-canvas" Width="1280" Height="720" /&gt;
/// </code>
/// The component obtains a <c>GPUCanvasContext</c> via the browser WebGPU API and passes
/// a pinned UTF-8 CSS selector to <see cref="WgpuContext.InitializeFromHandle"/>.
/// </remarks>
public sealed class WpfCanvas : ComponentBase, IDisposable
{
    private WgpuContext? _context;
    private GCHandle _selectorHandle;

    [Parameter] public string Id      { get; set; } = "progpu-canvas";
    [Parameter] public int    Width   { get; set; } = 800;
    [Parameter] public int    Height  { get; set; } = 600;

    public WgpuContext? GpuContext => _context;

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender) return;
        InitializeWebGPU();
    }

    private unsafe void InitializeWebGPU()
    {
        // Build a CSS selector string "#<canvas-id>\0" and pin it for the lifetime
        // of the GPU context so the pointer remains valid.
        byte[] selectorUtf8 = System.Text.Encoding.UTF8.GetBytes($"#{Id}\0");
        _selectorHandle = GCHandle.Alloc(selectorUtf8, GCHandleType.Pinned);
        IntPtr selectorPtr = _selectorHandle.AddrOfPinnedObject();

        _context = new WgpuContext();
        // WgpuContext.CreateSurfaceFromHandle wraps selectorPtr in
        // SurfaceDescriptorFromCanvasHTMLSelector on the __WASM__ code path.
        _context.InitializeFromHandle(selectorPtr, (uint)Width, (uint)Height);
    }

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "canvas");
        builder.AddAttribute(1, "id",     Id);
        builder.AddAttribute(2, "width",  Width.ToString());
        builder.AddAttribute(3, "height", Height.ToString());
        builder.AddAttribute(4, "style",  "display:block;width:100%;height:100%;");
        builder.CloseElement();
    }

    public void Dispose()
    {
        _context?.Dispose();
        if (_selectorHandle.IsAllocated)
            _selectorHandle.Free();
    }
}
