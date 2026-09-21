using System;
using System.Numerics;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaPortableRenderDataSink = System.Windows.Media.IPortableRenderDataDrawingContextSink;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingVisual = global::ProGPU.Scene.DrawingVisual;
using ProGpuVisual = global::ProGPU.Scene.Visual;
using ProGpuWgpuContext = global::ProGPU.Backend.WgpuContext;

namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfDrawingFrame
{
    private readonly ProGpuContainerVisual? _sceneRootVisual;
    private readonly ProGpuContainerVisual? _retainedWpfVisualRoot;
    private readonly ProGpuContainerVisual? _popupRetainedWpfVisualRoot;
    private readonly ProGpuDrawingVisual _rootVisual;
    private readonly ProGpuWgpuContext? _context;
    private readonly WpfViewport3DTextureCache? _viewport3DTextureCache;
    private readonly WpfRetainedVisualBranchMap? _retainedVisualBranchMap;
    private readonly WpfGpuHitTestOwnerMap? _hitTestOwnerMap;
    private readonly bool _allowRetainedOwnerContexts;

    internal ProGpuWpfDrawingFrame(
        ProGpuDrawingVisual rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuWgpuContext? context = null,
        WpfViewport3DTextureCache? viewport3DTextureCache = null,
        uint logicalWidth = 0,
        uint logicalHeight = 0,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0,
        WpfGpuHitTestOwnerMap? hitTestOwnerMap = null)
        : this(
            sceneRootVisual: null,
            retainedWpfVisualRoot: null,
            popupRetainedWpfVisualRoot: null,
            rootVisual,
            pixelWidth,
            pixelHeight,
            context,
            viewport3DTextureCache,
            logicalWidth: logicalWidth,
            logicalHeight: logicalHeight,
            dpiScaleX: dpiScaleX,
            dpiScaleY: dpiScaleY,
            hitTestOwnerMap: hitTestOwnerMap)
    {
    }

    internal ProGpuWpfDrawingFrame(
        ProGpuContainerVisual? sceneRootVisual,
        ProGpuContainerVisual? retainedWpfVisualRoot,
        ProGpuDrawingVisual rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuWgpuContext? context = null,
        WpfViewport3DTextureCache? viewport3DTextureCache = null,
        bool clearRetainedWpfVisualRoot = true,
        WpfRetainedVisualBranchMap? retainedVisualBranchMap = null,
        uint logicalWidth = 0,
        uint logicalHeight = 0,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0,
        WpfGpuHitTestOwnerMap? hitTestOwnerMap = null)
        : this(
            sceneRootVisual,
            retainedWpfVisualRoot,
            popupRetainedWpfVisualRoot: null,
            rootVisual,
            pixelWidth,
            pixelHeight,
            context,
            viewport3DTextureCache,
            clearRetainedWpfVisualRoot,
            retainedVisualBranchMap,
            logicalWidth,
            logicalHeight,
            dpiScaleX,
            dpiScaleY,
            hitTestOwnerMap)
    {
    }

    internal ProGpuWpfDrawingFrame(
        ProGpuContainerVisual? sceneRootVisual,
        ProGpuContainerVisual? retainedWpfVisualRoot,
        ProGpuContainerVisual? popupRetainedWpfVisualRoot,
        ProGpuDrawingVisual rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuWgpuContext? context = null,
        WpfViewport3DTextureCache? viewport3DTextureCache = null,
        bool clearRetainedWpfVisualRoot = true,
        WpfRetainedVisualBranchMap? retainedVisualBranchMap = null,
        uint logicalWidth = 0,
        uint logicalHeight = 0,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0,
        WpfGpuHitTestOwnerMap? hitTestOwnerMap = null)
    {
        _sceneRootVisual = sceneRootVisual;
        _retainedWpfVisualRoot = retainedWpfVisualRoot;
        _popupRetainedWpfVisualRoot = popupRetainedWpfVisualRoot;
        _rootVisual = rootVisual ?? throw new ArgumentNullException(nameof(rootVisual));
        _context = context;
        _viewport3DTextureCache = viewport3DTextureCache;
        _retainedVisualBranchMap = retainedVisualBranchMap;
        _hitTestOwnerMap = hitTestOwnerMap;
        _allowRetainedOwnerContexts = clearRetainedWpfVisualRoot;

        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);
        LogicalWidth = Math.Max(1, logicalWidth == 0 ? PixelWidth : logicalWidth);
        LogicalHeight = Math.Max(1, logicalHeight == 0 ? PixelHeight : logicalHeight);
        DpiScaleX = NormalizeDpiScale(dpiScaleX);
        DpiScaleY = NormalizeDpiScale(dpiScaleY);

        _rootVisual.Context.Clear();
        _rootVisual.Size = new Vector2(LogicalWidth, LogicalHeight);

        if (_retainedWpfVisualRoot != null)
        {
            if (clearRetainedWpfVisualRoot)
            {
                _retainedWpfVisualRoot.ClearChildren();
                _retainedVisualBranchMap?.Clear();
                _hitTestOwnerMap?.Clear();
            }

            _retainedWpfVisualRoot.Size = new Vector2(LogicalWidth, LogicalHeight);
            _retainedWpfVisualRoot.Offset = Vector2.Zero;
            _retainedWpfVisualRoot.Transform = Matrix4x4.Identity;
            _retainedWpfVisualRoot.Scale = Vector3.One;
            _retainedWpfVisualRoot.RenderTransformOrigin = Vector2.Zero;
        }

        if (_popupRetainedWpfVisualRoot != null)
        {
            _popupRetainedWpfVisualRoot.ClearChildren();
            _popupRetainedWpfVisualRoot.Size = new Vector2(LogicalWidth, LogicalHeight);
            _popupRetainedWpfVisualRoot.Offset = Vector2.Zero;
            _popupRetainedWpfVisualRoot.Transform = Matrix4x4.Identity;
            _popupRetainedWpfVisualRoot.Scale = Vector3.One;
            _popupRetainedWpfVisualRoot.RenderTransformOrigin = Vector2.Zero;
        }

        if (_sceneRootVisual != null)
        {
            _sceneRootVisual.ClearChildren();
            _sceneRootVisual.Size = new Vector2(LogicalWidth, LogicalHeight);
            if (_retainedWpfVisualRoot != null)
            {
                _sceneRootVisual.AddChild(_retainedWpfVisualRoot);
            }

            _sceneRootVisual.AddChild(_rootVisual);

            if (_popupRetainedWpfVisualRoot != null)
            {
                _sceneRootVisual.AddChild(_popupRetainedWpfVisualRoot);
            }
        }
    }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }

    public uint LogicalWidth { get; }

    public uint LogicalHeight { get; }

    public double DpiScaleX { get; }

    public double DpiScaleY { get; }

    public int DrawingContextCount { get; private set; }

    public int CompositionDrawingContextCount { get; private set; }

    public int ObjectRenderDataSinkContextCount { get; private set; }

    public object? LastOwnerVisual { get; private set; }

    public WpfRetainedVisualBranchMap? RetainedVisualBranchMap => _retainedVisualBranchMap;

    public WpfGpuHitTestOwnerMap? HitTestOwnerMap => _hitTestOwnerMap;

    internal bool AddRetainedWpfVisual(ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        if (_retainedWpfVisualRoot == null)
        {
            return false;
        }

        _retainedWpfVisualRoot.AddChild(visual);
        return true;
    }

    internal bool AddPopupRetainedWpfVisual(ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        if (_popupRetainedWpfVisualRoot == null)
        {
            return false;
        }

        _popupRetainedWpfVisualRoot.AddChild(visual);
        return true;
    }

    internal void RegisterRetainedWpfVisualOwner(object sourceVisual, ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(sourceVisual);
        ArgumentNullException.ThrowIfNull(visual);

        _retainedVisualBranchMap?.Register(sourceVisual, visual);
    }

    internal void RegisterRetainedWpfVisualDependency(object dependency, ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(visual);

        _retainedVisualBranchMap?.RegisterDependency(dependency, visual);
    }

    internal int GetOrCreateHitTestOwnerId(object ownerVisual)
    {
        return _hitTestOwnerMap?.GetOrCreateId(ownerVisual) ?? 0;
    }

    public MediaDrawingContext OpenDrawingContext()
    {
        return OpenDrawingContext(null);
    }

    public MediaDrawingContext OpenDrawingContext(object? ownerVisual)
    {
        DrawingContextCount++;
        LastOwnerVisual = ownerVisual;
        return new MediaDrawingContext(_rootVisual.Context);
    }

    public Func<object?, MediaDrawingContext> CreateDrawingContextFactory()
    {
        return OpenDrawingContext;
    }

    public bool TryRegisterRenderDataSinkProvider(out IDisposable? registration)
    {
        return TryRegisterRenderDataSinkProvider(null, out registration);
    }

    public bool TryRegisterRenderDataSinkProvider(
        IWpfImageSourceAdapter? imageSourceAdapter,
        out IDisposable? registration)
    {
        return WpfRenderDataSinkProviderBridge.TryRegisterRenderDataSinkProvider(
            this,
            imageSourceAdapter,
            out registration);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext()
    {
        return OpenCompositionDrawingContext(null);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext(IWpfImageSourceAdapter? imageSourceAdapter)
    {
        return OpenCompositionDrawingContext(null, imageSourceAdapter);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext(object? ownerVisual)
    {
        return OpenCompositionDrawingContext(ownerVisual, null);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext(
        object? ownerVisual,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        CompositionDrawingContextCount++;
        return new WpfCompositionDrawingContext(
            OpenCompositionCommandSink(ownerVisual),
            imageSourceAdapter);
    }

    public Func<object?, WpfCompositionDrawingContext> CreateCompositionDrawingContextFactory(
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        return ownerVisual => OpenCompositionDrawingContext(ownerVisual, imageSourceAdapter);
    }

    public WpfObjectRenderDataDrawingContext OpenObjectRenderDataSinkContext(
        object? ownerVisual,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ObjectRenderDataSinkContextCount++;
        return new WpfObjectRenderDataDrawingContext(
            OpenCompositionCommandSink(ownerVisual),
            imageSourceAdapter);
    }

    public Func<object?, MediaPortableRenderDataSink> CreateObjectRenderDataSinkFactory(
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        return ownerVisual => OpenObjectRenderDataSinkContext(ownerVisual, imageSourceAdapter);
    }

    internal IWpfCompositionCommandSink OpenCompositionCommandSink(object? ownerVisual)
    {
        LastOwnerVisual = ownerVisual;

        if (ownerVisual != null && _retainedWpfVisualRoot != null && _allowRetainedOwnerContexts)
        {
            var retainedSink = new ProGpuRetainedCompositionCommandSink(
                this,
                _context,
                _viewport3DTextureCache);
            var retainedBranchSink = (IWpfRetainedVisualBranchSink)retainedSink;
            if (retainedBranchSink.PushVisualOwner(ownerVisual))
            {
                return retainedSink;
            }

            retainedSink.Dispose();
        }

        return new ProGpuCompositionCommandSink(
            _rootVisual.Context,
            _context,
            _viewport3DTextureCache,
            hitTestId: ownerVisual != null ? GetOrCreateHitTestOwnerId(ownerVisual) : 0,
            hitTestOwnerMap: _hitTestOwnerMap);
    }

    private static double NormalizeDpiScale(double dpiScale)
    {
        return double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1.0;
    }
}
