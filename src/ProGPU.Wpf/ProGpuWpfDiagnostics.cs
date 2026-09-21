namespace System.Windows.Media.ProGPU;

public enum ProGpuWpfWindowingBackend
{
    Unknown,
    Win32,
    Cocoa,
    X11,
    Wayland
}

public static class ProGpuWpfDiagnostics
{
    private const int HitTestOwnerBufferCapacity = 64;

    public readonly record struct RenderSurfaceGeometrySnapshot(
        uint LogicalWidth,
        uint LogicalHeight,
        uint PixelWidth,
        uint PixelHeight,
        double DpiScaleX,
        double DpiScaleY,
        double DpiScale,
        uint ViewportX,
        uint ViewportY,
        uint ViewportWidth,
        uint ViewportHeight);

    public readonly record struct GpuHitTestCacheSnapshot(
        bool HasIndex,
        bool HasDeviceIndex,
        int PrimitiveCount,
        int NodeCount,
        int PrimitiveIndexCount,
        int PathSegmentCount,
        int OwnerCount);

    public readonly record struct CompositionLayerSnapshot(
        bool HasCompositionTarget,
        int SceneRootChildCount,
        int RetainedLayerIndex,
        int FlatLayerIndex,
        int PopupLayerIndex,
        int RetainedLayerChildCount,
        int PopupLayerChildCount);

    public readonly record struct PortablePopupSnapshot(
        int OpenCount,
        int VisibleCount,
        int NativeWindowCount,
        int PresentedNativeWindowCount,
        int NativeWindowGpuHitTestCount,
        int NativeWindowGpuHitTestOwnerCount);

    public readonly record struct WindowingCapabilitiesSnapshot(
        ProGpuWpfWindowingBackend Backend,
        bool IsWaylandDesktopSession,
        bool SupportsGlobalPosition,
        bool SupportsInteractiveMove,
        bool SupportsNativePopupWindows,
        bool UsesOwnerCompositedPopups);

    public readonly record struct MemorySnapshot(
        long ManagedHeapBytes,
        long ManagedFragmentedBytes,
        long ProcessWorkingSetBytes,
        int VisualReplayCacheCapacity,
        int RetainedVisualBranchSourceCount,
        int RetainedVisualBranchCount,
        int Viewport3DTextureSetCount,
        ulong Viewport3DTextureBytes,
        int ShaderSamplerTextureCount,
        ulong ShaderSamplerTextureBytes,
        ulong CompositorPersistentBufferBytes,
        ulong CompositorAtlasTextureBytes,
        ulong CompositorGlyphOutlineBytes,
        ulong CompositorIntermediateTextureBytes,
        ulong KnownWpfAndCompositorGpuBytes);

    public readonly record struct PerformanceSnapshot(
        long PresentedFrameCount,
        double CompositorCpuFrameTimeMs,
        double VisualTreeCompileCpuTimeMs,
        double GpuUploadCpuTimeMs,
        double RenderPassEncodingCpuTimeMs,
        int DrawCallsCount,
        int RecordedCommandCount,
        int VectorVerticesCount,
        int TextVerticesCount,
        bool SceneCacheHit,
        string? SceneCacheMissReason,
        int PathAtlasCachedCount,
        uint PathAtlasGrowthCount,
        int GlyphOutlineCompiledCount,
        ulong GlyphRasterBatchSubmissions);

    // CPU wall times around real host/native stages, not GPU execution times.
    // The native submission call includes uploads and encoding; they cannot
    // be split using the managed compositor's unrelated timing counters.
    public readonly record struct NativePerformanceSnapshot(
        long PresentedFrameCount,
        double CpuFrameTimeMs,
        double SourceUpdateCpuTimeMs,
        double SceneCompileCpuTimeMs,
        double SceneInstallCpuTimeMs,
        double SurfaceAcquireCpuTimeMs,
        double SubmissionCpuTimeMs,
        double PresentCpuTimeMs,
        bool SourceUpdated,
        global::ProGPU.Backend.Native.NativeSceneUpdateMetrics SceneUpdate,
        global::ProGPU.Backend.Native.NativeSceneFrameMetrics Frame)
    {
        public long DeviceRecoveryCount { get; init; }
        // Present only when the host explicitly enables inventory capture.
        // Logical engine-owned storage, not physical or whole-device residency.
        public global::ProGPU.Backend.Native.NativeGpuMemorySnapshot? GpuMemory { get; init; }
        public double MemoryInventoryCpuTimeMs { get; init; }
    }

    public static bool TryGetNativePerformanceSnapshot(
        object? window, out NativePerformanceSnapshot snapshot)
    {
        snapshot = default;
        return TryGetWindowHost(window, out var host) && host is not null &&
            host.TryGetNativePerformanceSnapshot(out snapshot);
    }

    public readonly record struct NativeMemoryCheckpoint(
        NativePerformanceSnapshot PresentedFrame,
        global::ProGPU.Backend.Native.NativeGpuMemorySnapshot CompletedMemory);

    /// <summary>
    /// Explicitly polls actual GPU completion on the native render owner thread.
    /// Unlike snapshot reads, this may retire completed submission resources.
    /// Returns false while work remains pending or no matching captured frame exists.
    /// Does not wait, request a frame, purge caches, or change published frame data.
    /// </summary>
    public static bool TryPollNativeMemoryCheckpoint(
        object? window, out NativeMemoryCheckpoint checkpoint)
    {
        checkpoint = default;
        return TryGetWindowHost(window, out var host) && host is not null &&
            host.TryPollNativeMemoryCheckpoint(out checkpoint);
    }

    internal static bool TryCreateNativeMemoryCheckpoint(
        NativePerformanceSnapshot frame,
        global::ProGPU.Backend.Native.NativeGpuMemorySnapshot completed,
        long recoveryCount,
        out NativeMemoryCheckpoint checkpoint)
    {
        checkpoint = default;
        if (frame.PresentedFrameCount == 0 || frame.DeviceRecoveryCount != recoveryCount ||
            frame.GpuMemory is not { } submitted || submitted.EngineId == 0 ||
            submitted.EngineId != completed.EngineId ||
            submitted.SceneId != frame.SceneUpdate.SceneId ||
            submitted.SceneGeneration != frame.SceneUpdate.Generation ||
            completed.SceneId != submitted.SceneId ||
            completed.SceneGeneration != submitted.SceneGeneration ||
            completed.RetainedSubmissionBatchCount != 0)
            return false;
        checkpoint = new NativeMemoryCheckpoint(frame, completed);
        return true;
    }

    public static bool TryGetWindowHost(object? window, out ProGpuWpfWindowHost? host)
    {
        if (window is ProGpuWpfWindowHost directHost)
        {
            host = directHost;
            return true;
        }

        return WpfPortableWindowActivation.TryGetActiveHost(window, out host);
    }

    public static bool TryGetWindowingCapabilities(
        object? window,
        out WindowingCapabilitiesSnapshot capabilities)
    {
        capabilities = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        var native = host.SilkWindow?.Native;
        capabilities = CreateWindowingCapabilitiesSnapshot(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux(),
            native?.Win32 is not null,
            native?.Cocoa is not null,
            native?.X11 is not null,
            native?.Wayland is not null,
            IsWaylandDesktopSession(
                Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));
        return native != null;
    }

    internal static WindowingCapabilitiesSnapshot CreateWindowingCapabilitiesSnapshot(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        bool hasWin32,
        bool hasCocoa,
        bool hasX11,
        bool hasWayland,
        bool isWaylandDesktopSession)
    {
        ProGpuWpfWindowingBackend backend =
            isWindows && hasWin32 ? ProGpuWpfWindowingBackend.Win32 :
            isMacOS && hasCocoa ? ProGpuWpfWindowingBackend.Cocoa :
            isLinux && hasX11 ? ProGpuWpfWindowingBackend.X11 :
            isLinux && hasWayland ? ProGpuWpfWindowingBackend.Wayland :
            ProGpuWpfWindowingBackend.Unknown;
        bool supportsDesktopPositioning =
            backend is ProGpuWpfWindowingBackend.Win32
                or ProGpuWpfWindowingBackend.Cocoa
                or ProGpuWpfWindowingBackend.X11;
        bool supportsNativePopups =
            backend is ProGpuWpfWindowingBackend.Cocoa
                or ProGpuWpfWindowingBackend.X11;

        return new WindowingCapabilitiesSnapshot(
            backend,
            isWaylandDesktopSession,
            SupportsGlobalPosition: supportsDesktopPositioning,
            SupportsInteractiveMove: supportsDesktopPositioning,
            SupportsNativePopupWindows: supportsNativePopups,
            UsesOwnerCompositedPopups:
                backend is ProGpuWpfWindowingBackend.Win32
                    or ProGpuWpfWindowingBackend.Wayland);
    }

    internal static bool IsWaylandDesktopSession(string? sessionType, string? waylandDisplay)
    {
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(waylandDisplay);
    }

    public static bool TryRequestRender(object? window)
    {
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        host.RequestRenderAndWakeNativeLoop();
        return true;
    }

    public static bool TryWakeNativeLoop(object? window)
    {
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryRequestNativeLoopWakeup();
    }

    public static bool TryGetRenderSchedulerWakeupCount(object? window, out long wakeupCount)
    {
        wakeupCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        wakeupCount = host.RenderSchedulerWakeupCount;
        return true;
    }

    public static bool TryGetRenderSurfaceGeometry(object? window, out RenderSurfaceGeometrySnapshot geometry)
    {
        geometry = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        geometry = CreateSnapshot(host.ResolveCurrentRenderSurfaceGeometryForDiagnostics());
        return true;
    }

    public static bool TryGetCompositionLayerSnapshot(object? window, out CompositionLayerSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        var target = host.CompositionTarget;
        if (target == null)
        {
            return false;
        }

        var sceneChildren = target.SceneRootVisual.Children;
        snapshot = new CompositionLayerSnapshot(
            HasCompositionTarget: true,
            SceneRootChildCount: sceneChildren.Count,
            RetainedLayerIndex: IndexOfChild(sceneChildren, target.RetainedWpfVisualRoot),
            FlatLayerIndex: IndexOfChild(sceneChildren, target.RootVisual),
            PopupLayerIndex: IndexOfChild(sceneChildren, target.PopupRetainedWpfVisualRoot),
            RetainedLayerChildCount: target.RetainedWpfVisualRoot.Children.Count,
            PopupLayerChildCount: target.PopupRetainedWpfVisualRoot.Children.Count);
        return true;
    }

    public static bool TryGetMemorySnapshot(object? window, out MemorySnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        // Managed cache sizes do not inventory resources owned by C++.
        if (host.RendererMode == ProGpuWpfRendererMode.NativeMilWgpu)
        {
            return false;
        }
        var target = host.CompositionTarget;
        if (target == null)
        {
            return false;
        }

        snapshot = CreateMemorySnapshot(target);
        return true;
    }

    public static bool TryGetPerformanceSnapshot(object? window, out PerformanceSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        if (host.RendererMode == ProGpuWpfRendererMode.NativeMilWgpu)
        {
            return false;
        }
        var target = host.CompositionTarget;
        if (target == null)
        {
            return false;
        }

        snapshot = CreatePerformanceSnapshot(target.Compositor.Metrics, host.PresentedFrameCount);
        return true;
    }

    internal static MemorySnapshot CreateMemorySnapshot(ProGpuWpfCompositionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.GetWpfTextureCacheMemoryDiagnostics(
            out int viewport3DTextureSetCount,
            out ulong viewport3DTextureBytes,
            out int shaderSamplerTextureCount,
            out ulong shaderSamplerTextureBytes);

        return CreateMemorySnapshot(
            target.Compositor.Metrics,
            global::System.Windows.Media.ProGPU.Composition.Mil.WpfVisualTreeRenderer.VisualReplayCacheRetainedCapacity,
            target.RetainedVisualBranchSourceCount,
            target.RetainedVisualBranchCount,
            viewport3DTextureSetCount,
            viewport3DTextureBytes,
            shaderSamplerTextureCount,
            shaderSamplerTextureBytes);
    }

    internal static MemorySnapshot CreateMemorySnapshot(
        global::ProGPU.Scene.CompositorMetrics metrics,
        int visualReplayCacheCapacity,
        int retainedVisualBranchSourceCount,
        int retainedVisualBranchCount,
        int viewport3DTextureSetCount,
        ulong viewport3DTextureBytes,
        int shaderSamplerTextureCount,
        ulong shaderSamplerTextureBytes)
    {
        ulong persistentBufferBytes =
            metrics.SceneBufferBytes +
            metrics.EffectParameterBufferBytes +
            metrics.SceneUploadArenaBytes;
        ulong atlasTextureBytes =
            metrics.GlyphAtlasTextureBytes +
            metrics.ColorGlyphAtlasTextureBytes +
            metrics.PathAtlasTextureBytes;
        ulong knownGpuBytes =
            persistentBufferBytes +
            atlasTextureBytes +
            metrics.GlyphOutlineGpuBytes +
            metrics.TrackedIntermediateTextureBytes +
            viewport3DTextureBytes +
            shaderSamplerTextureBytes;
        GCMemoryInfo gcMemory = GC.GetGCMemoryInfo();

        return new MemorySnapshot(
            ManagedHeapBytes: gcMemory.HeapSizeBytes,
            ManagedFragmentedBytes: gcMemory.FragmentedBytes,
            ProcessWorkingSetBytes: Environment.WorkingSet,
            VisualReplayCacheCapacity: visualReplayCacheCapacity,
            RetainedVisualBranchSourceCount: retainedVisualBranchSourceCount,
            RetainedVisualBranchCount: retainedVisualBranchCount,
            Viewport3DTextureSetCount: viewport3DTextureSetCount,
            Viewport3DTextureBytes: viewport3DTextureBytes,
            ShaderSamplerTextureCount: shaderSamplerTextureCount,
            ShaderSamplerTextureBytes: shaderSamplerTextureBytes,
            CompositorPersistentBufferBytes: persistentBufferBytes,
            CompositorAtlasTextureBytes: atlasTextureBytes,
            CompositorGlyphOutlineBytes: metrics.GlyphOutlineGpuBytes,
            CompositorIntermediateTextureBytes: metrics.TrackedIntermediateTextureBytes,
            KnownWpfAndCompositorGpuBytes: knownGpuBytes);
    }

    internal static PerformanceSnapshot CreatePerformanceSnapshot(
        global::ProGPU.Scene.CompositorMetrics metrics,
        long presentedFrameCount)
    {
        return new PerformanceSnapshot(
            PresentedFrameCount: presentedFrameCount,
            CompositorCpuFrameTimeMs: metrics.FrameTimeMs,
            VisualTreeCompileCpuTimeMs: metrics.VisualTreeCompileTimeMs,
            GpuUploadCpuTimeMs: metrics.GpuUploadTimeMs,
            RenderPassEncodingCpuTimeMs: metrics.RenderPassTimeMs,
            DrawCallsCount: metrics.DrawCallsCount,
            RecordedCommandCount: metrics.RecordedCommandCount,
            VectorVerticesCount: metrics.VectorVerticesCount,
            TextVerticesCount: metrics.TextVerticesCount,
            SceneCacheHit: metrics.SceneCacheHit,
            SceneCacheMissReason: metrics.SceneCacheMissReason,
            PathAtlasCachedCount: metrics.PathAtlasCachedCount,
            PathAtlasGrowthCount: metrics.PathAtlasGrowthCount,
            GlyphOutlineCompiledCount: metrics.GlyphOutlineCompiledCount,
            GlyphRasterBatchSubmissions: metrics.GlyphRasterBatchSubmissions);
    }

    public static bool TryGetPortablePopupSnapshot(object? window, out PortablePopupSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        host.GetPortablePopupDiagnostics(
            out int openCount,
            out int visibleCount,
            out int nativeWindowCount,
            out int presentedNativeWindowCount,
            out int nativeWindowGpuHitTestCount,
            out int nativeWindowGpuHitTestOwnerCount);
        snapshot = new PortablePopupSnapshot(
            openCount,
            visibleCount,
            nativeWindowCount,
            presentedNativeWindowCount,
            nativeWindowGpuHitTestCount,
            nativeWindowGpuHitTestOwnerCount);
        return true;
    }

    public static bool TryHitTestNativePopupOwners(
        object? window,
        double screenDeviceX,
        double screenDeviceY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestNativePortablePopupOwners(
            screenDeviceX,
            screenDeviceY,
            owners,
            out ownerCount);
    }

    public static bool TryQueryNativePopupHitTestBoundsOwners(
        object? window,
        double screenDeviceMinX,
        double screenDeviceMinY,
        double screenDeviceMaxX,
        double screenDeviceMaxY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryQueryNativePortablePopupHitTestBoundsOwners(
            screenDeviceMinX,
            screenDeviceMinY,
            screenDeviceMaxX,
            screenDeviceMaxY,
            owners,
            out ownerCount);
    }

    public static bool TryQueryNativePopupOwners(
        object? window,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryQueryNativePortablePopupOwners(owners, out ownerCount);
    }

    public static bool TryRaiseInput(
        object? window,
        Platform.WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        host.RaiseInputForDiagnostics(input);
        return true;
    }

    public static bool TryRaiseTopmostNativePopupInput(
        object? window,
        Platform.WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryRaiseTopmostNativePortablePopupInputForDiagnostics(input);
    }

    public static bool TryRaiseTopmostNativePopupLocalInput(
        object? window,
        Platform.WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryRaiseTopmostNativePortablePopupLocalInputForDiagnostics(input);
    }

    public static bool HasGpuHitTestCache(object? window)
    {
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.HasGpuHitTestCache;
    }

    public static bool TryGetGpuHitTestCacheSnapshot(object? window, out GpuHitTestCacheSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryGetGpuHitTestCacheSnapshot(out snapshot);
    }

    public static bool TryHitTestOwner(object? window, double x, double y, out object? owner)
    {
        owner = null;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestOwner(x, y, out owner);
    }

    public static bool TryHitTestInputOwner(object? window, double x, double y, out object? owner)
    {
        owner = null;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        object?[] ownerBuffer = System.Buffers.ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            return host.TryHitTestOwners(x, y, ownerBuffer, out int ownerCount)
                && WpfPortablePresentationSourceBridge.TrySelectPointerInputOwner(
                    ownerBuffer.AsSpan(0, ownerCount),
                    out owner);
        }
        finally
        {
            System.Buffers.ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    public static bool TryHitTestOwners(object? window, double x, double y, out object?[] owners)
    {
        owners = Array.Empty<object?>();
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestOwners(x, y, out owners);
    }

    public static bool TryHitTestOwners(object? window, double x, double y, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryHitTestOwners(x, y, owners, out ownerCount);
    }

    public static bool TryQueryHitTestBoundsOwners(
        object? window,
        double minX,
        double minY,
        double maxX,
        double maxY,
        out object?[] owners)
    {
        owners = Array.Empty<object?>();
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryQueryHitTestBoundsOwners(minX, minY, maxX, maxY, out owners);
    }

    public static bool TryQueryHitTestBoundsOwners(
        object? window,
        double minX,
        double minY,
        double maxX,
        double maxY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!TryGetWindowHost(window, out var host))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(host);
        return host.TryQueryHitTestBoundsOwners(minX, minY, maxX, maxY, owners, out ownerCount);
    }

    private static RenderSurfaceGeometrySnapshot CreateSnapshot(
        ProGpuWpfWindowHost.RenderSurfaceGeometry geometry)
    {
        return new RenderSurfaceGeometrySnapshot(
            geometry.LogicalWidth,
            geometry.LogicalHeight,
            geometry.PixelWidth,
            geometry.PixelHeight,
            geometry.DpiScaleX,
            geometry.DpiScaleY,
            geometry.DpiScale,
            geometry.ViewportX,
            geometry.ViewportY,
            geometry.ViewportWidth,
            geometry.ViewportHeight);
    }

    private static int IndexOfChild(
        System.Collections.Generic.IReadOnlyList<global::ProGPU.Scene.Visual> children,
        global::ProGPU.Scene.Visual target)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], target))
            {
                return i;
            }
        }

        return -1;
    }
}
