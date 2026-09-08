using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuCompositor = global::ProGPU.Scene.Compositor;
using ProGpuDrawingVisual = global::ProGPU.Scene.DrawingVisual;
using ProGpuHitTestDeviceIndex = global::ProGPU.Vector.GpuHitTestDeviceIndex;
using ProGpuHitTestIndex = global::ProGPU.Vector.GpuHitTestIndex;
using ProGpuHitTestResult = global::ProGPU.Vector.GpuHitTestResult;
using ProGpuRenderTargetViewport = global::ProGPU.Scene.RenderTargetViewport;
using ProGpuWgpuContext = global::ProGPU.Backend.WgpuContext;

namespace System.Windows.Media.ProGPU;

public unsafe sealed class ProGpuWpfCompositionTarget : IDisposable
{
    private const int HitTestStackResultLimit = 64;

    private readonly WpfVisualTreeRenderer _visualTreeRenderer = new();
    private readonly bool _ownsContext;
    private readonly bool _ownsCompositor;
    private readonly WpfShaderEffectSamplerTextureCache _shaderEffectSamplerTextureCache;
    private IWpfImageSourceAdapter? _frameImageSourceAdapterSource;
    private WpfShaderEffectSamplerImageSourceAdapter? _frameImageSourceAdapter;
    private bool _isDisposed;

    public ProGpuWgpuContext Context { get; }

    public ProGpuCompositor Compositor { get; }

    public ProGpuContainerVisual SceneRootVisual { get; } = new();

    public ProGpuContainerVisual RetainedWpfVisualRoot { get; } = new();

    public ProGpuContainerVisual PopupRetainedWpfVisualRoot { get; } = new();

    public ProGpuDrawingVisual RootVisual { get; } = new();

    public event EventHandler? RenderInvalidated;

    public IWpfImageSourceAdapter? WpfImageSourceAdapter { get; set; }

    public WpfVisualInvalidationTracker WpfInvalidationTracker { get; } = new();

    public WpfRetainedVisualBranchMap RetainedVisualBranchMap { get; } = new();

    public WpfGpuHitTestOwnerMap GpuHitTestOwnerMap { get; } = new();

    public long SceneChangeVersion => SceneRootVisual.ChangeVersion;

    public long RetainedWpfChangeVersion => RetainedWpfVisualRoot.ChangeVersion;

    public long FlatDrawingChangeVersion => RootVisual.ChangeVersion;

    public int DirtySourceCount => WpfInvalidationTracker.DirtySourceCount;

    public object? LastDirtySource => WpfInvalidationTracker.LastDirtySource;

    public int RetainedVisualBranchSourceCount => RetainedVisualBranchMap.SourceCount;

    public int RetainedVisualBranchCount => RetainedVisualBranchMap.VisualCount;

    public ProGpuHitTestIndex? LastGpuHitTestIndex => Compositor.LastHitTestIndex;
    public ProGpuHitTestDeviceIndex? LastGpuHitTestDeviceIndex => Compositor.LastHitTestDeviceIndex;

    public int LastRetainedBranchInvalidationCount { get; private set; }

    public int LastRetainedBranchDirtySourceCount { get; private set; }

    public int LastRetainedBranchMappedSourceCount { get; private set; }

    public int LastRetainedBranchUnmappedSourceCount { get; private set; }

    public int LastRetainedBranchSharedWithCleanSourceVisualCount { get; private set; }

    public int LastRetainedBranchReplayTargetConflictCount { get; private set; }

    public bool LastRetainedBranchInvalidationUsedFallback { get; private set; }

    internal WpfViewport3DTextureCache Viewport3DTextureCache { get; }

    internal void GetWpfTextureCacheMemoryDiagnostics(
        out int viewport3DTextureSetCount,
        out ulong viewport3DTextureBytes,
        out int shaderSamplerTextureCount,
        out ulong shaderSamplerTextureBytes)
    {
        Viewport3DTextureCache.GetMemoryDiagnostics(
            out viewport3DTextureSetCount,
            out viewport3DTextureBytes);
        _shaderEffectSamplerTextureCache.GetMemoryDiagnostics(
            out shaderSamplerTextureCount,
            out shaderSamplerTextureBytes);
    }

    public ProGpuWpfCompositionTarget(
        ProGpuWgpuContext context,
        ProGpuCompositor compositor,
        bool ownsContext = false,
        bool ownsCompositor = false)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
        _ownsContext = ownsContext;
        _ownsCompositor = ownsCompositor;
        Viewport3DTextureCache = new WpfViewport3DTextureCache(Context);
        _shaderEffectSamplerTextureCache = new WpfShaderEffectSamplerTextureCache(
            Context,
            Compositor,
            Viewport3DTextureCache);
        WpfInvalidationTracker.Invalidated += OnWpfSourceInvalidated;
        ResetSceneRoot();
    }

    public static ProGpuWpfCompositionTarget CreateHeadless(TextureFormat renderFormat = TextureFormat.Rgba8Unorm)
    {
        var context = new ProGpuWgpuContext();
        context.Initialize(null);

        return new ProGpuWpfCompositionTarget(
            context,
            new ProGpuCompositor(context, renderFormat),
            ownsContext: true,
            ownsCompositor: true);
    }

    public static ProGpuWpfCompositionTarget CreateForWindow(IWindow window)
    {
        return CreateForWindow(window, sharedDeviceContext: null, compositorOptions: null);
    }

    internal static ProGpuWpfCompositionTarget CreateForWindow(
        IWindow window,
        ProGpuWgpuContext? sharedDeviceContext,
        global::ProGPU.Scene.CompositorOptions? compositorOptions)
    {
        ArgumentNullException.ThrowIfNull(window);

        var context = new ProGpuWgpuContext();
        ProGpuCompositor? compositor = null;
        try
        {
            if (sharedDeviceContext == null)
            {
                context.Initialize(window);
            }
            else
            {
                context.InitializeSharedDevice(window, sharedDeviceContext);
            }

            compositor = new ProGpuCompositor(
                context,
                context.SwapChainFormat,
                compositorOptions ?? global::ProGPU.Scene.CompositorOptions.Default);
            return new ProGpuWpfCompositionTarget(
                context, compositor, ownsContext: true, ownsCompositor: true);
        }
        catch
        {
            compositor?.Dispose();
            context.Dispose();
            throw;
        }
    }

    public MediaDrawingContext OpenDrawingContext(uint pixelWidth, uint pixelHeight)
    {
        ThrowIfDisposed();

        return BeginDrawingFrame(pixelWidth, pixelHeight).OpenDrawingContext();
    }

    public ProGpuWpfDrawingFrame BeginDrawingFrame(uint pixelWidth, uint pixelHeight)
    {
        return BeginDrawingFrame(pixelWidth, pixelHeight, clearRetainedWpfVisualRoot: true);
    }

    internal ProGpuWpfDrawingFrame BeginDrawingFrame(
        uint pixelWidth,
        uint pixelHeight,
        bool clearRetainedWpfVisualRoot)
    {
        return BeginDrawingFrame(
            pixelWidth,
            pixelHeight,
            clearRetainedWpfVisualRoot,
            logicalWidth: 0,
            logicalHeight: 0,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0);
    }

    internal ProGpuWpfDrawingFrame BeginDrawingFrame(
        uint pixelWidth,
        uint pixelHeight,
        bool clearRetainedWpfVisualRoot,
        uint logicalWidth,
        uint logicalHeight,
        double dpiScaleX,
        double dpiScaleY)
    {
        ThrowIfDisposed();
        if (clearRetainedWpfVisualRoot)
        {
            _shaderEffectSamplerTextureCache.Clear();
        }

        return new ProGpuWpfDrawingFrame(
            SceneRootVisual,
            RetainedWpfVisualRoot,
            PopupRetainedWpfVisualRoot,
            RootVisual,
            pixelWidth,
            pixelHeight,
            Context,
            Viewport3DTextureCache,
            clearRetainedWpfVisualRoot,
            RetainedVisualBranchMap,
            logicalWidth,
            logicalHeight,
            dpiScaleX,
            dpiScaleY,
            GpuHitTestOwnerMap);
    }

    public WpfCompositionDrawingContext OpenCompositionDrawingContext(uint pixelWidth, uint pixelHeight)
    {
        ThrowIfDisposed();
        return BeginDrawingFrame(pixelWidth, pixelHeight).OpenCompositionDrawingContext();
    }

    public WpfCompositionDrawingContext CreateCompositionDrawingContext(MediaDrawingContext drawingContext)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(drawingContext);
        return new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(drawingContext, Context, Viewport3DTextureCache));
    }

    public WpfVisualReplayResult ReplayVisualSubtree(
        object rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        WpfInvalidationTracker.AttachIfChanged(rootVisual);
        ProGpuWpfDrawingFrame drawingFrame = BeginDrawingFrame(pixelWidth, pixelHeight);
        IWpfImageSourceAdapter? activeImageSourceAdapter = CreateFrameImageSourceAdapter(
            imageSourceAdapter ?? WpfImageSourceAdapter);
        using IDisposable? renderDataSinkProviderRegistration = drawingFrame.TryRegisterRenderDataSinkProvider(activeImageSourceAdapter, out IDisposable? registration)
            ? registration
            : null;
        using var sink = drawingFrame.OpenCompositionCommandSink(null);
        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            activeImageSourceAdapter);
    }

    public WpfVisualReplayResult ReplayVisualSubtreeRetained(
        object rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        ProGpuWpfDrawingFrame drawingFrame = BeginDrawingFrame(pixelWidth, pixelHeight);
        IWpfImageSourceAdapter? activeImageSourceAdapter = CreateFrameImageSourceAdapter(
            imageSourceAdapter ?? WpfImageSourceAdapter);
        using IDisposable? renderDataSinkProviderRegistration = drawingFrame.TryRegisterRenderDataSinkProvider(activeImageSourceAdapter, out IDisposable? registration)
            ? registration
            : null;
        using var sink = new ProGpuRetainedCompositionCommandSink(
            drawingFrame,
            Context,
            Viewport3DTextureCache);
        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            activeImageSourceAdapter);
    }

    public WpfVisualReplayResult ReplayVisualSubtree(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            CreateFrameImageSourceAdapter(imageSourceAdapter ?? WpfImageSourceAdapter));
    }

    internal WpfVisualReplayResult ReplayVisualSubtreeUntracked(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null,
        bool includePortablePopupRoots = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            CreateFrameImageSourceAdapter(imageSourceAdapter ?? WpfImageSourceAdapter),
            trackInvalidationRoot: false,
            includePortablePopupRoots);
    }

    internal WpfVisualReplayResult ReplayVisualSubtreeTracked(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        bool includePortablePopupRoots)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        return ReplayVisualSubtreeCore(
            rootVisual,
            sink,
            resources,
            CreateFrameImageSourceAdapter(imageSourceAdapter ?? WpfImageSourceAdapter),
            trackInvalidationRoot: true,
            includePortablePopupRoots);
    }

    public void Render(uint pixelWidth, uint pixelHeight, TextureView* targetView)
    {
        ThrowIfDisposed();

        if (targetView == null)
        {
            throw new ArgumentNullException(nameof(targetView));
        }

        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        var logicalWidth = ResolveLogicalRenderDimension(SceneRootVisual.Size.X, RootVisual.Size.X, RetainedWpfVisualRoot.Size.X, pixelWidth);
        var logicalHeight = ResolveLogicalRenderDimension(SceneRootVisual.Size.Y, RootVisual.Size.Y, RetainedWpfVisualRoot.Size.Y, pixelHeight);
        var dpiScaleX = pixelWidth / (double)logicalWidth;
        var dpiScaleY = pixelHeight / (double)logicalHeight;
        var dpiScale = (float)((dpiScaleX + dpiScaleY) / 2.0);

        Render(logicalWidth, logicalHeight, pixelWidth, pixelHeight, dpiScale, targetView);
    }

    public void Render(
        uint logicalWidth,
        uint logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        float dpiScale,
        TextureView* targetView)
    {
        Render(
            logicalWidth,
            logicalHeight,
            pixelWidth,
            pixelHeight,
            ProGpuRenderTargetViewport.Full(pixelWidth, pixelHeight),
            dpiScale,
            targetView);
    }

    public void Render(
        uint logicalWidth,
        uint logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        ProGpuRenderTargetViewport renderTargetViewport,
        float dpiScale,
        TextureView* targetView)
    {
        ThrowIfDisposed();

        if (targetView == null)
        {
            throw new ArgumentNullException(nameof(targetView));
        }

        logicalWidth = Math.Max(1, logicalWidth);
        logicalHeight = Math.Max(1, logicalHeight);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        SceneRootVisual.Size = new Vector2(logicalWidth, logicalHeight);
        RetainedWpfVisualRoot.Size = new Vector2(logicalWidth, logicalHeight);
        PopupRetainedWpfVisualRoot.Size = new Vector2(logicalWidth, logicalHeight);
        RootVisual.Size = new Vector2(logicalWidth, logicalHeight);

        Compositor.RenderScene(
            SceneRootVisual,
            logicalWidth,
            logicalHeight,
            pixelWidth,
            pixelHeight,
            renderTargetViewport,
            dpiScale,
            targetView);
    }

    public bool TryHitTestPoint(Vector2 logicalPoint, out ProGpuHitTestResult result)
    {
        ThrowIfDisposed();
        return Compositor.TryHitTestPoint(logicalPoint, out result);
    }

    public bool TryHitTestOwner(Vector2 logicalPoint, out object? owner, out ProGpuHitTestResult result)
    {
        ThrowIfDisposed();
        Span<ProGpuHitTestResult> results = stackalloc ProGpuHitTestResult[1];
        if (!Compositor.TryHitTestPointAll(logicalPoint, results, out int hitCount, out var summary))
        {
            owner = null;
            result = default;
            return false;
        }

        if (TryResolveFirstHitTestOwner(results, hitCount, out owner, out result))
        {
            return true;
        }

        if (ShouldRetryHitTestOwnerResolution(
                resolvedCount: 0,
                requestedCount: 1,
                hitCount,
                summary,
                resultCapacity: 1))
        {
            int expandedCapacity = GetExpandedHitTestResultCapacity(summary, currentCapacity: 1);
            ProGpuHitTestResult[]? rentedExpandedResults = null;
            try
            {
                Span<ProGpuHitTestResult> expandedResults = RentHitTestResults(expandedCapacity, out rentedExpandedResults);
                if (Compositor.TryHitTestPointAll(logicalPoint, expandedResults, out int expandedHitCount, out var expandedSummary))
                {
                    if (TryResolveFirstHitTestOwner(expandedResults, expandedHitCount, out owner, out result))
                    {
                        return true;
                    }

                    summary = expandedSummary;
                }
            }
            finally
            {
                ReturnHitTestResults(rentedExpandedResults);
            }
        }

        owner = null;
        result = summary;
        return false;
    }

    public bool TryHitTestOwners(
        Vector2 logicalPoint,
        Span<object?> owners,
        out int ownerCount,
        out ProGpuHitTestResult summary)
    {
        ThrowIfDisposed();
        ownerCount = 0;
        summary = default;
        if (owners.IsEmpty)
        {
            return false;
        }

        int resultCapacity = GetHitTestResultCapacity(owners.Length);
        ProGpuHitTestResult[]? rentedResults = null;
        Span<ProGpuHitTestResult> results = resultCapacity <= HitTestStackResultLimit
            ? stackalloc ProGpuHitTestResult[resultCapacity]
            : RentHitTestResults(resultCapacity, out rentedResults);
        try
        {
            if (!Compositor.TryHitTestPointAll(logicalPoint, results, out int hitCount, out summary))
            {
                return false;
            }

            ownerCount = CopyHitTestOwners(results, hitCount, owners);
            if (ShouldRetryHitTestOwnerResolution(ownerCount, owners.Length, hitCount, summary, resultCapacity))
            {
                int expandedCapacity = GetExpandedHitTestResultCapacity(summary, resultCapacity);
                if (expandedCapacity > resultCapacity)
                {
                    ProGpuHitTestResult[]? rentedExpandedResults = null;
                    try
                    {
                        Span<ProGpuHitTestResult> expandedResults = RentHitTestResults(expandedCapacity, out rentedExpandedResults);
                        if (Compositor.TryHitTestPointAll(logicalPoint, expandedResults, out int expandedHitCount, out var expandedSummary))
                        {
                            ownerCount = CopyHitTestOwners(expandedResults, expandedHitCount, owners);
                            summary = expandedSummary;
                        }
                    }
                    finally
                    {
                        ReturnHitTestResults(rentedExpandedResults);
                    }
                }
            }

            return true;
        }
        finally
        {
            ReturnHitTestResults(rentedResults);
        }
    }

    public bool TryQueryHitTestBoundsOwners(
        Vector2 logicalMin,
        Vector2 logicalMax,
        Span<object?> owners,
        out int ownerCount,
        out ProGpuHitTestResult summary)
    {
        ThrowIfDisposed();
        ownerCount = 0;
        summary = default;
        if (owners.IsEmpty)
        {
            return false;
        }

        int resultCapacity = GetHitTestResultCapacity(owners.Length);
        ProGpuHitTestResult[]? rentedResults = null;
        Span<ProGpuHitTestResult> results = resultCapacity <= HitTestStackResultLimit
            ? stackalloc ProGpuHitTestResult[resultCapacity]
            : RentHitTestResults(resultCapacity, out rentedResults);
        try
        {
            if (!Compositor.TryQueryHitTestBoundsAll(logicalMin, logicalMax, results, out int hitCount, out summary))
            {
                return false;
            }

            ownerCount = CopyHitTestOwners(results, hitCount, owners);
            if (ShouldRetryHitTestOwnerResolution(ownerCount, owners.Length, hitCount, summary, resultCapacity))
            {
                int expandedCapacity = GetExpandedHitTestResultCapacity(summary, resultCapacity);
                if (expandedCapacity > resultCapacity)
                {
                    ProGpuHitTestResult[]? rentedExpandedResults = null;
                    try
                    {
                        Span<ProGpuHitTestResult> expandedResults = RentHitTestResults(expandedCapacity, out rentedExpandedResults);
                        if (Compositor.TryQueryHitTestBoundsAll(logicalMin, logicalMax, expandedResults, out int expandedHitCount, out var expandedSummary))
                        {
                            ownerCount = CopyHitTestOwners(expandedResults, expandedHitCount, owners);
                            summary = expandedSummary;
                        }
                    }
                    finally
                    {
                        ReturnHitTestResults(rentedExpandedResults);
                    }
                }
            }

            return true;
        }
        finally
        {
            ReturnHitTestResults(rentedResults);
        }
    }

    public bool TryQueryHitTestBoundsCandidates(
        Vector2 logicalMin,
        Vector2 logicalMax,
        Span<object?> candidates,
        out int candidateCount,
        out ProGpuHitTestResult summary)
    {
        ThrowIfDisposed();
        candidateCount = 0;
        summary = default;
        if (candidates.IsEmpty)
        {
            return false;
        }

        int resultCapacity = GetHitTestResultCapacity(candidates.Length);
        ProGpuHitTestResult[]? rentedResults = null;
        Span<ProGpuHitTestResult> results = resultCapacity <= HitTestStackResultLimit
            ? stackalloc ProGpuHitTestResult[resultCapacity]
            : RentHitTestResults(resultCapacity, out rentedResults);
        try
        {
            if (!Compositor.TryQueryHitTestBoundsAll(logicalMin, logicalMax, results, out int hitCount, out summary))
            {
                return false;
            }

            candidateCount = CopyGeometryHitTestCandidates(results, hitCount, candidates);
            if (ShouldRetryHitTestOwnerResolution(candidateCount, candidates.Length, hitCount, summary, resultCapacity))
            {
                int expandedCapacity = GetExpandedHitTestResultCapacity(summary, resultCapacity);
                if (expandedCapacity > resultCapacity)
                {
                    ProGpuHitTestResult[]? rentedExpandedResults = null;
                    try
                    {
                        Span<ProGpuHitTestResult> expandedResults = RentHitTestResults(expandedCapacity, out rentedExpandedResults);
                        if (Compositor.TryQueryHitTestBoundsAll(logicalMin, logicalMax, expandedResults, out int expandedHitCount, out var expandedSummary))
                        {
                            candidateCount = CopyGeometryHitTestCandidates(expandedResults, expandedHitCount, candidates);
                            summary = expandedSummary;
                        }
                    }
                    finally
                    {
                        ReturnHitTestResults(rentedExpandedResults);
                    }
                }
            }

            return true;
        }
        finally
        {
            ReturnHitTestResults(rentedResults);
        }
    }

    public bool TryQueryHitTestEllipseCandidates(
        Vector2 logicalMin,
        Vector2 logicalMax,
        Span<object?> candidates,
        out int candidateCount,
        out ProGpuHitTestResult summary)
    {
        ThrowIfDisposed();
        candidateCount = 0;
        summary = default;
        if (candidates.IsEmpty)
        {
            return false;
        }

        int resultCapacity = GetHitTestResultCapacity(candidates.Length);
        ProGpuHitTestResult[]? rentedResults = null;
        Span<ProGpuHitTestResult> results = resultCapacity <= HitTestStackResultLimit
            ? stackalloc ProGpuHitTestResult[resultCapacity]
            : RentHitTestResults(resultCapacity, out rentedResults);
        try
        {
            if (!Compositor.TryQueryHitTestEllipseAll(logicalMin, logicalMax, results, out int hitCount, out summary))
            {
                return false;
            }

            candidateCount = CopyGeometryHitTestCandidates(results, hitCount, candidates);
            if (ShouldRetryHitTestOwnerResolution(candidateCount, candidates.Length, hitCount, summary, resultCapacity))
            {
                int expandedCapacity = GetExpandedHitTestResultCapacity(summary, resultCapacity);
                if (expandedCapacity > resultCapacity)
                {
                    ProGpuHitTestResult[]? rentedExpandedResults = null;
                    try
                    {
                        Span<ProGpuHitTestResult> expandedResults = RentHitTestResults(expandedCapacity, out rentedExpandedResults);
                        if (Compositor.TryQueryHitTestEllipseAll(logicalMin, logicalMax, expandedResults, out int expandedHitCount, out var expandedSummary))
                        {
                            candidateCount = CopyGeometryHitTestCandidates(expandedResults, expandedHitCount, candidates);
                            summary = expandedSummary;
                        }
                    }
                    finally
                    {
                        ReturnHitTestResults(rentedExpandedResults);
                    }
                }
            }

            return true;
        }
        finally
        {
            ReturnHitTestResults(rentedResults);
        }
    }

    private int CopyHitTestOwners(
        ReadOnlySpan<ProGpuHitTestResult> results,
        int hitCount,
        Span<object?> owners)
    {
        int ownerCount = 0;
        int resultCount = Math.Min(hitCount, results.Length);
        for (int i = 0; i < resultCount && ownerCount < owners.Length; i++)
        {
            if (GpuHitTestOwnerMap.TryGetOwner(results[i].Id, out object? owner) &&
                owner != null)
            {
                owners[ownerCount++] = owner;
            }
        }

        return ownerCount;
    }

    private bool TryResolveFirstHitTestOwner(
        ReadOnlySpan<ProGpuHitTestResult> results,
        int hitCount,
        out object? owner,
        out ProGpuHitTestResult result)
    {
        int resultCount = Math.Min(hitCount, results.Length);
        for (int i = 0; i < resultCount; i++)
        {
            if (GpuHitTestOwnerMap.TryGetOwner(results[i].Id, out owner) &&
                owner != null)
            {
                result = results[i];
                return true;
            }
        }

        owner = null;
        result = default;
        return false;
    }

    private int CopyGeometryHitTestCandidates(
        ReadOnlySpan<ProGpuHitTestResult> results,
        int hitCount,
        Span<object?> candidates)
    {
        int candidateCount = 0;
        int resultCount = Math.Min(hitCount, results.Length);
        for (int i = 0; i < resultCount && candidateCount < candidates.Length; i++)
        {
            if (GpuHitTestOwnerMap.TryGetOwner(results[i].Id, out object? owner) &&
                owner != null)
            {
                candidates[candidateCount++] = new PortableGeometryHitTestCandidate(
                    owner,
                    results[i].IntersectionDetail);
            }
        }

        return candidateCount;
    }

    private static int GetHitTestResultCapacity(int requestedCount)
    {
        return Math.Min(Math.Max(requestedCount, 1), ProGpuHitTestDeviceIndex.MaxHitResultCount);
    }

    private static int GetExpandedHitTestResultCapacity(ProGpuHitTestResult summary, int currentCapacity)
    {
        uint boundedHitCount = Math.Min(summary.Hit, (uint)ProGpuHitTestDeviceIndex.MaxHitResultCount);
        return boundedHitCount > (uint)currentCapacity
            ? (int)boundedHitCount
            : currentCapacity;
    }

    private static bool ShouldRetryHitTestOwnerResolution(
        int resolvedCount,
        int requestedCount,
        int hitCount,
        ProGpuHitTestResult summary,
        int resultCapacity)
    {
        return resolvedCount < requestedCount &&
            summary.Hit > (uint)hitCount &&
            resultCapacity < ProGpuHitTestDeviceIndex.MaxHitResultCount;
    }

    private static Span<ProGpuHitTestResult> RentHitTestResults(
        int resultCapacity,
        out ProGpuHitTestResult[] rentedResults)
    {
        rentedResults = ArrayPool<ProGpuHitTestResult>.Shared.Rent(resultCapacity);
        return rentedResults.AsSpan(0, resultCapacity);
    }

    private static void ReturnHitTestResults(ProGpuHitTestResult[]? rentedResults)
    {
        if (rentedResults != null)
        {
            ArrayPool<ProGpuHitTestResult>.Shared.Return(rentedResults);
        }
    }

    private static uint ResolveLogicalRenderDimension(
        float sceneRootDimension,
        float flatRootDimension,
        float retainedRootDimension,
        uint pixelDimension)
    {
        if (TryUseLogicalRenderDimension(sceneRootDimension, pixelDimension, out var logicalDimension) ||
            TryUseLogicalRenderDimension(flatRootDimension, pixelDimension, out logicalDimension) ||
            TryUseLogicalRenderDimension(retainedRootDimension, pixelDimension, out logicalDimension))
        {
            return logicalDimension;
        }

        return Math.Max(1u, pixelDimension);
    }

    private static bool TryUseLogicalRenderDimension(float dimension, uint pixelDimension, out uint logicalDimension)
    {
        logicalDimension = 0;
        if (!float.IsFinite(dimension) || dimension <= 0f)
        {
            return false;
        }

        logicalDimension = Math.Max(1u, (uint)MathF.Round(dimension, MidpointRounding.AwayFromZero));
        return logicalDimension <= Math.Max(1u, pixelDimension);
    }

    public bool DetectWpfSourceChanges()
    {
        ThrowIfDisposed();
        return WpfInvalidationTracker.DetectVersionChanges();
    }

    public bool ShouldReplayVisualSubtree(object rootVisual)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        return !ReferenceEquals(WpfInvalidationTracker.Root, rootVisual) ||
               WpfInvalidationTracker.IsDirty;
    }

    internal bool TryPrepareDirtyRetainedVisualBranchReplayTargets(
        object rootVisual,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out IReadOnlyList<WpfRetainedVisualBranchReplayTarget> targets)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);

        if (!ReferenceEquals(WpfInvalidationTracker.Root, rootVisual) ||
            !WpfInvalidationTracker.IsDirty ||
            LastRetainedBranchDirtySourceCount == 0 ||
            LastRetainedBranchInvalidationUsedFallback)
        {
            targets = Array.Empty<WpfRetainedVisualBranchReplayTarget>();
            return false;
        }

        return TryGetDirtyRetainedVisualBranchReplayTargets(imageSourceAdapter, out targets);
    }

    internal bool TryReplayDirtyRetainedVisualBranches(
        object rootVisual,
        ProGpuWpfDrawingFrame drawingFrame,
        IReadOnlyList<WpfRetainedVisualBranchReplayTarget> targets,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfVisualReplayResult result)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(drawingFrame);

        result = default;
        if (!ReferenceEquals(WpfInvalidationTracker.Root, rootVisual) ||
            !WpfInvalidationTracker.IsDirty ||
            LastRetainedBranchDirtySourceCount == 0 ||
            LastRetainedBranchInvalidationUsedFallback ||
            targets.Count == 0)
        {
            return false;
        }

        IWpfImageSourceAdapter? activeImageSourceAdapter = imageSourceAdapter ?? WpfImageSourceAdapter;
        var replayResult = default(WpfVisualReplayResult);
        Viewport3DTextureCache.BeginFrame();

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var branchVisual = (ProGpuRetainedDrawingVisual)target.Visual;
                RetainedVisualBranchMap.UnregisterVisualTree(branchVisual);
                ResetRetainedDrawingVisualBranch(branchVisual, drawingFrame.LogicalWidth, drawingFrame.LogicalHeight);

                using var sink = new ProGpuRetainedCompositionCommandSink(
                    drawingFrame,
                    branchVisual,
                    Context,
                    Viewport3DTextureCache);
                if (!_visualTreeRenderer.TryReplaySubtreeIntoCurrentRetainedVisual(
                    target.Source,
                    sink,
                    resources,
                    activeImageSourceAdapter,
                    out var branchReplayResult))
                {
                    RetainedWpfVisualRoot.ClearChildren();
                    RetainedVisualBranchMap.Clear();
                    return false;
                }

                replayResult = AddReplayResults(replayResult, branchReplayResult);
            }

            WpfInvalidationTracker.ConsumeDirty();
            RootVisual.Invalidate();
            result = replayResult;
            return true;
        }
        finally
        {
            Viewport3DTextureCache.EndFrame();
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        RootVisual.Context.Clear();
        RetainedWpfVisualRoot.ClearChildren();
        PopupRetainedWpfVisualRoot.ClearChildren();
        RetainedVisualBranchMap.Clear();
        ResetSceneRoot();
        Viewport3DTextureCache.Clear();
        _shaderEffectSamplerTextureCache.Clear();
        SceneRootVisual.Invalidate();
        RootVisual.Invalidate();
        WpfInvalidationTracker.MarkDirty();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        WpfInvalidationTracker.Invalidated -= OnWpfSourceInvalidated;
        WpfInvalidationTracker.Dispose();
        RenderInvalidated = null;
        WpfImageSourceAdapter = null;
        _frameImageSourceAdapter = null;
        _frameImageSourceAdapterSource = null;

        RootVisual.Context.Clear();
        RetainedWpfVisualRoot.ClearChildren();
        PopupRetainedWpfVisualRoot.ClearChildren();
        SceneRootVisual.ClearChildren();
        RetainedVisualBranchMap.Clear();
        GpuHitTestOwnerMap.Clear();

        Viewport3DTextureCache.Dispose();
        _shaderEffectSamplerTextureCache.Dispose();

        if (_ownsCompositor)
        {
            Compositor.Dispose();
        }

        if (_ownsContext)
        {
            Context.Dispose();
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnWpfSourceInvalidated(object? sender, EventArgs e)
    {
        InvalidateRetainedWpfBranchesForDirtySources();
        RootVisual.Invalidate();
        RenderInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateRetainedWpfBranchesForDirtySources()
    {
        var result = RetainedVisualBranchMap.InvalidateVisualsForReferenceSources(
            WpfInvalidationTracker.DirtySourceSet,
            WpfInvalidationTracker.LastDirtySource);
        LastRetainedBranchInvalidationCount = result.InvalidatedVisualCount;
        LastRetainedBranchDirtySourceCount = result.DirtySourceCount;
        LastRetainedBranchMappedSourceCount = result.MappedSourceCount;
        LastRetainedBranchUnmappedSourceCount = result.UnmappedSourceCount;
        LastRetainedBranchSharedWithCleanSourceVisualCount = result.SharedWithCleanSourceVisualCount;
        LastRetainedBranchReplayTargetConflictCount = result.ReplayTargetConflictCount;
        LastRetainedBranchInvalidationUsedFallback = !result.CanTargetAllDirtySources;

        if (LastRetainedBranchInvalidationUsedFallback)
        {
            RetainedWpfVisualRoot.Invalidate();
        }
    }

    private WpfVisualReplayResult ReplayVisualSubtreeCore(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        bool trackInvalidationRoot = true,
        bool includePortablePopupRoots = false)
    {
        if (trackInvalidationRoot)
        {
            WpfInvalidationTracker.AttachIfChanged(rootVisual);
        }

        Viewport3DTextureCache.BeginFrame();

        try
        {
            var result = _visualTreeRenderer.ReplaySubtree(
                rootVisual,
                sink,
                resources,
                imageSourceAdapter,
                includePortablePopupRoots);
            if (trackInvalidationRoot)
            {
                WpfInvalidationTracker.ConsumeDirty();
            }

            RootVisual.Invalidate();
            return result;
        }
        finally
        {
            Viewport3DTextureCache.EndFrame();
        }
    }

    private bool TryGetDirtyRetainedVisualBranchReplayTargets(
        IWpfImageSourceAdapter? imageSourceAdapter,
        out IReadOnlyList<WpfRetainedVisualBranchReplayTarget> targets)
    {
        targets = RetainedVisualBranchMap.GetReplayTargetsForReferenceSources(
            WpfInvalidationTracker.DirtySourceSet,
            WpfInvalidationTracker.LastDirtySource);
        if (targets.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target.Visual is not ProGpuRetainedDrawingVisual branchVisual ||
                !_visualTreeRenderer.CanReplaySubtreeIntoCurrentRetainedVisual(
                    target.Source,
                    imageSourceAdapter))
            {
                targets = Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                return false;
            }
        }

        return true;
    }

    private static void ResetRetainedDrawingVisualBranch(
        ProGpuRetainedDrawingVisual visual,
        uint pixelWidth,
        uint pixelHeight)
    {
        visual.Context.Clear();
        visual.ClearChildren();
        visual.Offset = Vector2.Zero;
        visual.Size = new Vector2(pixelWidth, pixelHeight);
        visual.IsVisible = true;
        visual.Opacity = 1f;
        visual.Transform = Matrix4x4.Identity;
        visual.CacheAsLayer = false;
        visual.Scale = Vector3.One;
        visual.Rotation = 0f;
        visual.CenterPoint = Vector3.Zero;
        visual.RenderTransformOrigin = new Vector2(0.5f, 0.5f);
        visual.ClipBounds = null;
        visual.OuterClipBounds = null;
        visual.Effect = null;
    }

    internal IWpfImageSourceAdapter? CreateFrameImageSourceAdapter(IWpfImageSourceAdapter? imageSourceAdapter)
    {
        ThrowIfDisposed();
        if (_frameImageSourceAdapter == null ||
            !ReferenceEquals(_frameImageSourceAdapterSource, imageSourceAdapter))
        {
            _frameImageSourceAdapterSource = imageSourceAdapter;
            _frameImageSourceAdapter = new WpfShaderEffectSamplerImageSourceAdapter(
                imageSourceAdapter,
                _shaderEffectSamplerTextureCache);
        }

        return _frameImageSourceAdapter;
    }

    private static WpfVisualReplayResult AddReplayResults(
        WpfVisualReplayResult left,
        WpfVisualReplayResult right)
    {
        return new WpfVisualReplayResult(
            left.VisualCount + right.VisualCount,
            left.ContentCount + right.ContentCount,
            left.ChildEdgeCount + right.ChildEdgeCount,
            left.UnsupportedContentCount + right.UnsupportedContentCount,
            left.UnsupportedVisualStateCount + right.UnsupportedVisualStateCount,
            new WpfMilDecodeResult(
                left.RenderData.RecordCount + right.RenderData.RecordCount,
                left.RenderData.AppliedCount + right.RenderData.AppliedCount,
                left.RenderData.SkippedCount + right.RenderData.SkippedCount,
                left.RenderData.UnsupportedCount + right.RenderData.UnsupportedCount));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void ResetSceneRoot()
    {
        SceneRootVisual.ClearChildren();
        SceneRootVisual.AddChild(RetainedWpfVisualRoot);
        SceneRootVisual.AddChild(RootVisual);
        SceneRootVisual.AddChild(PopupRetainedWpfVisualRoot);
    }
}
