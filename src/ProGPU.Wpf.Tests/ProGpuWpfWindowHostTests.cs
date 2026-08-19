using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Vector;
using ProGPU.Wpf.Interop;
using Silk.NET.Maths;
using Xunit;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using ProGpuDrawingContext = ProGPU.Scene.DrawingContext;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuWpfWindowHostTests
{
    [Fact]
    public void MemoryDiagnosticsSeparateManagedProcessAndTrackedGpuOwnership()
    {
        var snapshot = ProGpuWpfDiagnostics.CreateMemorySnapshot(
            new ProGPU.Scene.CompositorMetrics
            {
                SceneBufferBytes = 10,
                EffectParameterBufferBytes = 2,
                SceneUploadArenaBytes = 3,
                GlyphAtlasTextureBytes = 4,
                ColorGlyphAtlasTextureBytes = 5,
                PathAtlasTextureBytes = 6,
                GlyphOutlineGpuBytes = 7,
                TrackedIntermediateTextureBytes = 8
            },
            visualReplayCacheCapacity: 17,
            retainedVisualBranchSourceCount: 9,
            retainedVisualBranchCount: 10,
            viewport3DTextureSetCount: 1,
            viewport3DTextureBytes: 11,
            shaderSamplerTextureCount: 2,
            shaderSamplerTextureBytes: 13);

        Assert.True(snapshot.ManagedHeapBytes >= 0);
        Assert.True(snapshot.ManagedFragmentedBytes >= 0);
        Assert.True(snapshot.ProcessWorkingSetBytes > 0);
        Assert.Equal(17, snapshot.VisualReplayCacheCapacity);
        Assert.Equal(9, snapshot.RetainedVisualBranchSourceCount);
        Assert.Equal(10, snapshot.RetainedVisualBranchCount);
        Assert.Equal(1, snapshot.Viewport3DTextureSetCount);
        Assert.Equal(11UL, snapshot.Viewport3DTextureBytes);
        Assert.Equal(2, snapshot.ShaderSamplerTextureCount);
        Assert.Equal(13UL, snapshot.ShaderSamplerTextureBytes);
        Assert.Equal(15UL, snapshot.CompositorPersistentBufferBytes);
        Assert.Equal(15UL, snapshot.CompositorAtlasTextureBytes);
        Assert.Equal(7UL, snapshot.CompositorGlyphOutlineBytes);
        Assert.Equal(8UL, snapshot.CompositorIntermediateTextureBytes);
        Assert.Equal(69UL, snapshot.KnownWpfAndCompositorGpuBytes);
    }

    [Fact]
    public void PerformanceDiagnosticsExposeCpuSubmissionAndSceneMetrics()
    {
        var snapshot = ProGpuWpfDiagnostics.CreatePerformanceSnapshot(
            new ProGPU.Scene.CompositorMetrics
            {
                FrameTimeMs = 7.5,
                VisualTreeCompileTimeMs = 2.5,
                GpuUploadTimeMs = 1.25,
                RenderPassTimeMs = 3.75,
                DrawCallsCount = 11,
                RecordedCommandCount = 12,
                VectorVerticesCount = 13,
                TextVerticesCount = 14,
                SceneCacheHit = true,
                SceneCacheMissReason = "none",
                PathAtlasCachedCount = 15,
                PathAtlasGrowthCount = 16,
                GlyphOutlineCompiledCount = 17,
                GlyphRasterBatchSubmissions = 18
            },
            presentedFrameCount: 19);

        Assert.Equal(19, snapshot.PresentedFrameCount);
        Assert.Equal(7.5, snapshot.CompositorCpuFrameTimeMs);
        Assert.Equal(2.5, snapshot.VisualTreeCompileCpuTimeMs);
        Assert.Equal(1.25, snapshot.GpuUploadCpuTimeMs);
        Assert.Equal(3.75, snapshot.RenderPassEncodingCpuTimeMs);
        Assert.Equal(11, snapshot.DrawCallsCount);
        Assert.Equal(12, snapshot.RecordedCommandCount);
        Assert.Equal(13, snapshot.VectorVerticesCount);
        Assert.Equal(14, snapshot.TextVerticesCount);
        Assert.True(snapshot.SceneCacheHit);
        Assert.Equal("none", snapshot.SceneCacheMissReason);
        Assert.Equal(15, snapshot.PathAtlasCachedCount);
        Assert.Equal(16u, snapshot.PathAtlasGrowthCount);
        Assert.Equal(17, snapshot.GlyphOutlineCompiledCount);
        Assert.Equal(18UL, snapshot.GlyphRasterBatchSubmissions);
    }

    [Fact]
    public void SetTitleAndClientSizeUpdateCachedWindowStateBeforeNativeWindowExists()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = "Initial",
            Width = 640,
            Height = 480,
            Left = 12,
            Top = 24,
            Topmost = true,
            WindowBorder = ProGpuWpfWindowBorder.Hidden
        })
        {
            WpfRenderScheduler = scheduler
        };

        host.SetTitle("Updated");
        host.SetClientSize(321, 123);
        host.SetPosition(32, 48);
        host.SetTopmost(false);
        host.SetWindowBorder(ProGpuWpfWindowBorder.Fixed);

        Assert.Equal("Updated", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(123, host.Height);
        Assert.Equal(32, host.Left);
        Assert.Equal(48, host.Top);
        Assert.False(host.Topmost);
        Assert.Equal(ProGpuWpfWindowBorder.Fixed, host.WindowBorder);
        Assert.Equal(5, scheduler.RequestCount);
    }

    [Fact]
    public void SettingWpfRootVisualRequestsRender()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var root = new object();

        host.WpfRootVisual = root;

        Assert.Same(root, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
        Assert.True(scheduler.HasPendingRenderRequest);
    }

    [Fact]
    public void TryCreateWindowRegionClipBuildsExactDifferencePath()
    {
        var region = new PortableWindowRegion(
            new PortableRect(10, 20, 100, 50),
            new[]
            {
                new PortableRect(0, 30, 30, 20),
                new PortableRect(200, 30, 30, 20)
            });

        Assert.True(ProGpuWpfWindowHost.TryCreateWindowRegionClip(region, out var clip));
        Assert.NotNull(clip);
        Assert.True(clip!.IsCombined);
        Assert.Equal(0, clip.Op);
        Assert.NotNull(clip.PathA);
        Assert.NotNull(clip.PathB);
        Assert.True(clip.TryGetBounds(out var min, out var max));
        Assert.Equal(10f, min.X);
        Assert.Equal(20f, min.Y);
        Assert.Equal(110f, max.X);
        Assert.Equal(70f, max.Y);
        Assert.True(clip.PathB!.TryGetBounds(out var excludedMin, out var excludedMax));
        Assert.Equal(10f, excludedMin.X);
        Assert.Equal(30f, excludedMin.Y);
        Assert.Equal(30f, excludedMax.X);
        Assert.Equal(50f, excludedMax.Y);
    }

    [Fact]
    public void TryCreateWindowRegionClipFailsClosedForEmptyRegion()
    {
        Assert.False(ProGpuWpfWindowHost.TryCreateWindowRegionClip(null, out var nullClip));
        Assert.Null(nullClip);

        var region = new PortableWindowRegion(PortableRect.Empty);

        Assert.False(ProGpuWpfWindowHost.TryCreateWindowRegionClip(region, out var emptyClip));
        Assert.Null(emptyClip);
    }

    [Fact]
    public void SettingSameWpfRootVisualDoesNotRequestRenderAgain()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var root = new object();

        host.WpfRootVisual = root;
        host.WpfRootVisual = root;

        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void InvalidateWpfSourceForPortableRenderMarksSourceDirty()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var root = new object();
        var dirtySource = new object();
        var renderInvalidationCount = 0;
        typeof(ProGpuWpfWindowHost)
            .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(host, target);
        host.WpfRootVisual = root;
        target.WpfInvalidationTracker.Attach(root);
        target.WpfInvalidationTracker.ConsumeDirty();
        target.RenderInvalidated += (_, _) => renderInvalidationCount++;

        host.InvalidateWpfSourceForPortableRender(dirtySource);

        Assert.True(host.IsWpfRootVisualDirty);
        Assert.Same(dirtySource, target.LastDirtySource);
        Assert.Equal(1, target.DirtySourceCount);
        Assert.Equal(1, renderInvalidationCount);
    }

    [Fact]
    public void DefaultPlatformServicesUseCrossPlatformLauncherBoundary()
    {
        using var host = new ProGpuWpfWindowHost();

        var services = Assert.IsType<CrossPlatformWpfPlatformServices>(host.PlatformServices);
        Assert.IsType<ProcessWpfClipboard>(services.Clipboard);
        Assert.IsType<SilkNetWpfCursorService>(services.Cursors);
        Assert.IsType<QueuedWpfDispatcherService>(services.Dispatcher);
        Assert.IsType<SilkNetWpfDragDropService>(services.DragDrop);
        Assert.IsType<ProcessWpfFileDialogService>(services.FileDialogs);
        Assert.IsType<SilkNetWpfInputService>(services.Input);
        Assert.IsType<ProcessWpfLauncher>(services.Launcher);
        Assert.IsType<ProcessWpfMessageBoxService>(services.MessageBoxes);
        Assert.IsType<SilkNetWpfMonitorService>(services.Monitors);
        Assert.IsType<ThreadPoolWpfTimerService>(services.Timers);
        Assert.IsType<SilkNetWpfWindowDecorationService>(services.WindowDecorations);
        Assert.IsType<SilkNetWpfWindowEventService>(services.WindowEvents);
        Assert.IsType<DispatcherWpfRenderScheduler>(host.WpfRenderScheduler);
    }

    [Fact]
    public void DefaultWindowOptionsUseEventDrivenNativeLoop()
    {
        var options = new ProGpuWpfWindowOptions();

        Assert.True(options.IsEventDriven);
    }

    [Fact]
    public void NativeRunUsesOwnerDrivenPortableLoop()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs"));

        Assert.DoesNotContain("_window!.Run();", source, StringComparison.Ordinal);
        Assert.Contains("RunPortableNativeLoop();", source, StringComparison.Ordinal);
        Assert.Contains("private void RunPortableNativeLoop()", source, StringComparison.Ordinal);
        Assert.Contains("while (ShouldKeepPortableNativeRunLoopAlive())", source, StringComparison.Ordinal);
        Assert.Contains("DoEvents();", source, StringComparison.Ordinal);
        Assert.Contains("if (!EnsureCompositionTargetLoaded() || !ShouldKeepPortableNativeRunLoopAlive())", source, StringComparison.Ordinal);
        Assert.Contains("window.DoEvents();\n        }\n        finally\n        {\n            ProcessDeferredNativeWindowDisposals();", source, StringComparison.Ordinal);
        Assert.Contains("if (ShouldPumpNativeRender())", source, StringComparison.Ordinal);
        Assert.Contains("NativeRenderPumpCount++;\n            window.DoRender();", source, StringComparison.Ordinal);
        Assert.Contains("SkippedNativeRenderPumpCount++;", source, StringComparison.Ordinal);
        Assert.Contains("Thread.Sleep(hadPendingRender || WpfRenderScheduler.HasPendingRenderRequest", source, StringComparison.Ordinal);
        Assert.Contains("private bool ShouldKeepPortableNativeRunLoopAlive()", source, StringComparison.Ordinal);
        Assert.Contains("if (_isLoadingCompositionTarget)", source, StringComparison.Ordinal);
        Assert.Contains("composition target load deferred during reentrant initialization", source, StringComparison.Ordinal);
        Assert.Contains("_isLoadingCompositionTarget = true;", source, StringComparison.Ordinal);
        Assert.Contains("_isLoadingCompositionTarget = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (_window == null || _isDisposed || _hasNativeWindowCloseStarted)", source, StringComparison.Ordinal);
        Assert.Contains("input attach canceled after host close", source, StringComparison.Ordinal);
        Assert.Contains("!_hasNativeWindowCloseStarted", source, StringComparison.Ordinal);
        Assert.Contains("catch (ObjectDisposedException ex) when (!ShouldKeepPortableNativeRunLoopAlive())", source, StringComparison.Ordinal);
        Assert.Contains("owner loop unexpected ObjectDisposedException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (ObjectDisposedException)\n            {\n                return;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("!window.IsClosing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("windowClosing", source, StringComparison.Ordinal);
        Assert.Contains("if (!_disposeNativeWindowWhenLoopExits || _isNativeLoopRunning)", source, StringComparison.Ordinal);
        Assert.Contains("bool closeAlreadyStarted = _hasNativeWindowCloseStarted;", source, StringComparison.Ordinal);
        Assert.Contains("_hasNativeWindowCloseStarted = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (closeAlreadyStarted)\n        {\n            return;\n        }", source, StringComparison.Ordinal);
        Assert.Contains("window.Close();\n        TryRequestNativeLoopWakeup(window.ContinueEvents);", source, StringComparison.Ordinal);
        Assert.Contains("close request already pending", source, StringComparison.Ordinal);
        Assert.Contains("_hasNativeWindowCloseStarted = false;", source, StringComparison.Ordinal);
        Assert.Contains("QueueDeferredNativeWindowDisposal(this);", source, StringComparison.Ordinal);
        Assert.Contains("private static void ProcessDeferredNativeWindowDisposals()", source, StringComparison.Ordinal);
        Assert.Contains("host.DisposeDeferredNativeWindowIfNeeded();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeFramebufferResizeRendersInsideTheResizeCallback()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGpuWpfWindowHost.cs"));

        Assert.Contains("_window.FramebufferResize += OnFramebufferResize;", source, StringComparison.Ordinal);
        Assert.Contains("window.FramebufferResize -= OnFramebufferResize;", source, StringComparison.Ordinal);
        Assert.Contains("private void OnFramebufferResize(Vector2D<int> size)", source, StringComparison.Ordinal);
        Assert.Contains("OnResize(_window.Size);\n            OnRender(0d);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SetCursorReturnsFalseBeforeWindowIsCreated()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.SetCursor(WpfCursor.Hand));
    }

    [Fact]
    public void TryBeginDragMoveReturnsFalseBeforeWindowIsCreated()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.TryBeginDragMove());
    }

    [Fact]
    public void SettingPlatformServicesRebuildsDefaultRenderScheduler()
    {
        using var host = new ProGpuWpfWindowHost();
        var originalScheduler = host.WpfRenderScheduler;

        host.PlatformServices = new CrossPlatformWpfPlatformServices();

        Assert.IsType<DispatcherWpfRenderScheduler>(host.WpfRenderScheduler);
        Assert.NotSame(originalScheduler, host.WpfRenderScheduler);
    }

    [Fact]
    public void CustomRenderSchedulerIsPreservedWhenPlatformServicesChange()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };

        host.PlatformServices = new CrossPlatformWpfPlatformServices();

        Assert.Same(scheduler, host.WpfRenderScheduler);
    }

    [Fact]
    public void RenderSchedulerWakeupIsObservedByHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var wakeupCount = 0;
        host.RenderWakeupRequested += (_, _) => wakeupCount++;

        scheduler.RequestRender();

        Assert.Equal(1, host.RenderSchedulerWakeupCount);
        Assert.Equal(1, wakeupCount);
    }

    [Theory]
    [InlineData(false, true, false, false, false, false, true)]
    [InlineData(false, true, false, false, true, false, false)]
    [InlineData(false, true, false, false, false, true, false)]
    [InlineData(true, true, false, false, false, false, false)]
    [InlineData(false, false, false, false, false, false, false)]
    [InlineData(false, true, true, false, false, false, false)]
    [InlineData(false, true, false, true, false, false, false)]
    public void RenderSchedulerWakeupDoesNotRenderInlineInsideOwnerLoop(
        bool isDisposed,
        bool hasWindow,
        bool isRendering,
        bool isProcessingRenderSchedulerWakeup,
        bool isNativeLoopRunning,
        bool usesExternalNativeLoopPump,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProGpuWpfWindowHost.ShouldProcessRenderSchedulerWakeupInline(
                isDisposed,
                hasWindow,
                isRendering,
                isProcessingRenderSchedulerWakeup,
                isNativeLoopRunning,
                usesExternalNativeLoopPump));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ExternallyPumpedHostRendersPendingFrameBeforeNativeEvents(
        bool usesExternalNativeLoopPump,
        bool shouldPumpNativeRender,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProGpuWpfWindowHost.ShouldPumpExternalNativeRenderBeforeEvents(
                usesExternalNativeLoopPump,
                shouldPumpNativeRender));
    }

    [Theory]
    [InlineData(true, false, false, true, false, false, false, false, ProGpuWpfWindowingBackend.Win32, true, true, false, true)]
    [InlineData(false, true, false, false, true, false, false, false, ProGpuWpfWindowingBackend.Cocoa, true, true, true, false)]
    [InlineData(false, false, true, false, false, true, false, true, ProGpuWpfWindowingBackend.X11, true, true, true, false)]
    [InlineData(false, false, true, false, false, false, true, true, ProGpuWpfWindowingBackend.Wayland, false, false, false, true)]
    [InlineData(false, false, true, false, false, false, false, false, ProGpuWpfWindowingBackend.Unknown, false, false, false, false)]
    public void WindowingCapabilitiesDescribeTheActualNativeBackend(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        bool hasWin32,
        bool hasCocoa,
        bool hasX11,
        bool hasWayland,
        bool isWaylandDesktopSession,
        ProGpuWpfWindowingBackend expectedBackend,
        bool supportsGlobalPosition,
        bool supportsInteractiveMove,
        bool supportsNativePopupWindows,
        bool usesOwnerCompositedPopups)
    {
        var capabilities = ProGpuWpfDiagnostics.CreateWindowingCapabilitiesSnapshot(
            isWindows,
            isMacOS,
            isLinux,
            hasWin32,
            hasCocoa,
            hasX11,
            hasWayland,
            isWaylandDesktopSession);

        Assert.Equal(expectedBackend, capabilities.Backend);
        Assert.Equal(isWaylandDesktopSession, capabilities.IsWaylandDesktopSession);
        Assert.Equal(supportsGlobalPosition, capabilities.SupportsGlobalPosition);
        Assert.Equal(supportsInteractiveMove, capabilities.SupportsInteractiveMove);
        Assert.Equal(supportsNativePopupWindows, capabilities.SupportsNativePopupWindows);
        Assert.Equal(usesOwnerCompositedPopups, capabilities.UsesOwnerCompositedPopups);
    }

    [Theory]
    [InlineData("wayland", null, true)]
    [InlineData("WAYLAND", "", true)]
    [InlineData("x11", "wayland-0", true)]
    [InlineData("x11", null, false)]
    [InlineData(null, null, false)]
    public void WindowingCapabilitiesDetectWaylandDesktopSession(
        string? sessionType,
        string? waylandDisplay,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProGpuWpfDiagnostics.IsWaylandDesktopSession(sessionType, waylandDisplay));
    }

    [Fact]
    public void NativeLoopWakeupInvokesContinueEventsAndCountsSuccessfulRequests()
    {
        using var host = new ProGpuWpfWindowHost();
        var continueEventsCount = 0;

        Assert.True(host.TryRequestNativeLoopWakeup(() => continueEventsCount++));
        Assert.True(host.TryRequestNativeLoopWakeup(() => continueEventsCount++));

        Assert.Equal(2, continueEventsCount);
        Assert.Equal(2, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void NativeLoopWakeupReturnsFalseWhenContinueEventsFails()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.TryRequestNativeLoopWakeup(() => throw new InvalidOperationException()));

        Assert.Equal(0, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void NativeUpdateRaisesUpdateTick()
    {
        using var host = new ProGpuWpfWindowHost();
        var updateTickCount = 0;
        host.UpdateTick += (_, _) => updateTickCount++;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object[] { 0.0 });

        Assert.Equal(1, updateTickCount);
    }

    [Fact]
    public void ReplacingRenderSchedulerDisconnectsPreviousWakeupSource()
    {
        var firstScheduler = new TestRenderScheduler();
        var secondScheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = firstScheduler
        };

        host.WpfRenderScheduler = secondScheduler;

        firstScheduler.RequestRender();
        Assert.Equal(0, host.RenderSchedulerWakeupCount);

        secondScheduler.RequestRender();
        Assert.Equal(1, host.RenderSchedulerWakeupCount);
    }

    [Fact]
    public void DisposingHostDisconnectsRenderSchedulerWakeups()
    {
        var scheduler = new TestRenderScheduler();
        var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        host.Dispose();

        scheduler.RequestRender();

        Assert.Equal(0, host.RenderSchedulerWakeupCount);
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueBeforeAnyFrameIsPresented()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);

        Assert.True(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsFalseWhenPresentedFrameStateIsUnchanged()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);

        host.RecordPresentedFrame(frameState);

        Assert.True(host.HasPresentedFrame);
        Assert.Equal(frameState, host.LastPresentedFrameState);
        Assert.False(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenSchedulerHasPendingRequest()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        scheduler.RequestRender();

        Assert.True(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenNativeVersionChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        var changedFrameState = new ProGpuWpfFrameState(100, 50, 1, 4, 3);

        Assert.True(host.ShouldRenderFrame(changedFrameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenRetainedBranchTargetabilityChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        var changedFrameState = new ProGpuWpfFrameState(
            100,
            50,
            1,
            2,
            3,
            retainedBranchInvalidationCount: 1,
            retainedBranchDirtySourceCount: 1,
            retainedBranchMappedSourceCount: 1,
            retainedBranchUnmappedSourceCount: 0,
            retainedBranchSharedWithCleanSourceVisualCount: 1,
            retainedBranchReplayTargetConflictCount: 1,
            retainedBranchInvalidationUsedFallback: true);

        Assert.True(host.ShouldRenderFrame(changedFrameState));
        Assert.True(changedFrameState.RetainedBranchInvalidationUsedFallback);
        Assert.Equal(1, changedFrameState.RetainedBranchSharedWithCleanSourceVisualCount);
        Assert.Equal(1, changedFrameState.RetainedBranchReplayTargetConflictCount);
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenPixelSizeChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        var resizedFrameState = new ProGpuWpfFrameState(200, 100, 1, 2, 3);

        Assert.True(host.ShouldRenderFrame(resizedFrameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenLogicalSizeChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 100,
            logicalHeight: 50,
            dpiScale: 2.0);
        host.RecordPresentedFrame(frameState);

        var resizedFrameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 125,
            logicalHeight: 50,
            dpiScale: 2.0);

        Assert.True(host.ShouldRenderFrame(resizedFrameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenDpiScaleChanges()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 100,
            logicalHeight: 50,
            dpiScale: 2.0);
        host.RecordPresentedFrame(frameState);

        var scaledFrameState = new ProGpuWpfFrameState(
            200,
            100,
            1,
            2,
            3,
            logicalWidth: 100,
            logicalHeight: 50,
            dpiScale: 1.5);

        Assert.True(host.ShouldRenderFrame(scaledFrameState));
    }

    [Fact]
    public void RequestRenderAndWakeNativeLoopSchedulesRenderWithoutWindow()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };

        host.RequestRenderAndWakeNativeLoop();

        Assert.Equal(1, scheduler.RequestCount);
        Assert.Equal(0, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void RequestRenderAndWakeNativeLoopIgnoresDisposedRenderScheduler()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new DisposedRenderScheduler()
        };

        host.RequestRenderAndWakeNativeLoop();

        Assert.Equal(0, host.NativeLoopWakeupCount);
    }

    [Fact]
    public void LatePlatformInputAfterDisposeIsIgnored()
    {
        var scheduler = new TestRenderScheduler();
        var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var receivedCount = 0;
        host.InputReceived += (_, _) => receivedCount++;

        host.Dispose();
        RaisePlatformInput(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 10, y: 20));

        Assert.Equal(0, receivedCount);
        Assert.Equal(0, scheduler.RequestCount);
    }

    [Fact]
    public void GpuHitTestingFailsClosedAfterHostDisposal()
    {
        var host = new ProGpuWpfWindowHost();
        var target = ProGpuWpfCompositionTarget.CreateHeadless();
        SetPrivateField(host, "_target", target);

        host.Dispose();

        object?[] owners = new object?[4];
        object?[] candidates = new object?[4];

        Assert.False(host.HasGpuHitTestCache);
        Assert.False(host.TryHitTestOwner(1, 1, out var owner));
        Assert.Null(owner);
        Assert.False(host.TryHitTestOwners(1, 1, owners, out var ownerCount));
        Assert.Equal(0, ownerCount);
        Assert.False(host.TryQueryHitTestBoundsOwners(0, 0, 10, 10, owners, out var boundsOwnerCount));
        Assert.Equal(0, boundsOwnerCount);
        Assert.False(host.TryGetGpuHitTestCacheSnapshot(out _));
        Assert.False(host.TryQueryHitTestBoundsCandidates(0, 0, 10, 10, candidates, out var boundsCandidateCount));
        Assert.Equal(0, boundsCandidateCount);
        Assert.False(host.TryQueryHitTestEllipseCandidates(0, 0, 10, 10, candidates, out var ellipseCandidateCount));
        Assert.Equal(0, ellipseCandidateCount);
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenCoalescingIsDisabled()
    {
        using var host = new ProGpuWpfWindowHost
        {
            EnableFrameCoalescing = false
        };
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);

        Assert.True(host.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void ShouldRenderFrameReturnsTrueWhenExplicitFrameCallbacksAreRegistered()
    {
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        using var drawHost = new ProGpuWpfWindowHost();
        drawHost.RecordPresentedFrame(frameState);
        drawHost.Draw = (_, _) => { };

        using var wpfDrawHost = new ProGpuWpfWindowHost();
        wpfDrawHost.RecordPresentedFrame(frameState);
        wpfDrawHost.WpfDraw = (_, _) => { };

        using var renderHost = new ProGpuWpfWindowHost();
        renderHost.RecordPresentedFrame(frameState);
        renderHost.Render += (_, _) => { };

        Assert.True(drawHost.ShouldRenderFrame(frameState));
        Assert.True(wpfDrawHost.ShouldRenderFrame(frameState));
        Assert.True(renderHost.ShouldRenderFrame(frameState));
    }

    [Fact]
    public void NativeRenderPumpStopsAfterStaticFrameUntilRenderIsRequested()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        var pumpedFrames = 0;
        var skippedFrames = 0;

        for (var tick = 0; tick < 600; tick++)
        {
            if (host.ShouldPumpNativeRender())
            {
                pumpedFrames++;
                host.RecordPresentedFrame(frameState);
                scheduler.ConsumeRenderRequest();
            }
            else
            {
                skippedFrames++;
            }
        }

        Assert.Equal(1, pumpedFrames);
        Assert.Equal(599, skippedFrames);

        scheduler.RequestRender();

        Assert.True(host.ShouldPumpNativeRender());
    }

    [Fact]
    public void NativeRenderPumpRemainsContinuousForExplicitFrameCallbacks()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);
        host.RecordPresentedFrame(frameState);
        host.Render += (_, _) => { };

        Assert.True(host.ShouldPumpNativeRender());
    }

    [Fact]
    public void NativeRenderPumpStopsWhileWindowIsHiddenOrMinimized()
    {
        using var hiddenHost = new ProGpuWpfWindowHost();
        hiddenHost.Hide();

        using var minimizedHost = new ProGpuWpfWindowHost();
        minimizedHost.SetWindowState(ProGpuWpfWindowState.Minimized);

        Assert.False(hiddenHost.ShouldPumpNativeRender());
        Assert.False(minimizedHost.ShouldPumpNativeRender());
    }

    [Fact]
    public void PresentedFrameCountTracksActualPresentations()
    {
        using var host = new ProGpuWpfWindowHost();
        var frameState = new ProGpuWpfFrameState(100, 50, 1, 2, 3);

        Assert.Equal(0, host.PresentedFrameCount);

        host.RecordPresentedFrame(frameState);
        host.RecordPresentedFrame(frameState);

        Assert.Equal(2, host.PresentedFrameCount);
    }

    [Fact]
    public void NativeRenderPumpStopsAfterHostDisposal()
    {
        var host = new ProGpuWpfWindowHost();

        host.Dispose();

        Assert.False(host.ShouldPumpNativeRender());
    }

    [Fact]
    public void NativeRenderPumpIdlePredicateDoesNotAllocate()
    {
        using var host = new ProGpuWpfWindowHost();
        host.RecordPresentedFrame(new ProGpuWpfFrameState(100, 50, 1, 2, 3));
        for (var warmup = 0; warmup < 10_000; warmup++)
        {
            _ = host.ShouldPumpNativeRender();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var unexpectedPumpCount = 0;
        for (var iteration = 0; iteration < 1_000_000; iteration++)
        {
            if (host.ShouldPumpNativeRender())
            {
                unexpectedPumpCount++;
            }
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0, unexpectedPumpCount);
        Assert.Equal(0, allocatedBytes);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryTrustsReportedFramebufferOnHighDpiMonitor()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(420, 840),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(420u, geometry.PixelWidth);
        Assert.Equal(840u, geometry.PixelHeight);
        Assert.Equal(1.0, geometry.DpiScaleX);
        Assert.Equal(1.0, geometry.DpiScaleY);
        Assert.Equal(1.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveMonitorDpiScaleWithPlatformFallbackUsesNativeScaleWhenMonitorScaleIsUnavailable()
    {
        double dpiScale = ProGpuWpfWindowHost.ResolveMonitorDpiScaleWithPlatformFallback(
            monitorDpiScale: 1.0,
            platformDpiScaleProvider: () => 2.0);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(420, 840),
            monitorDpiScale: dpiScale);

        Assert.Equal(2.0, dpiScale);
        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(420u, geometry.PixelWidth);
        Assert.Equal(840u, geometry.PixelHeight);
        Assert.Equal(1.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryUsesMonitorScaleOnlyWhenFramebufferIsMissing()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(0, 0),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveMonitorDpiScaleWithPlatformFallbackKeepsUsableMonitorScale()
    {
        double dpiScale = ProGpuWpfWindowHost.ResolveMonitorDpiScaleWithPlatformFallback(
            monitorDpiScale: 1.5,
            platformDpiScaleProvider: () => 2.0);

        Assert.Equal(1.5, dpiScale);
    }

    [Fact]
    public void ResolveMonitorDpiScaleWithPlatformFallbackIgnoresInvalidNativeScale()
    {
        double dpiScale = ProGpuWpfWindowHost.ResolveMonitorDpiScaleWithPlatformFallback(
            monitorDpiScale: 1.0,
            platformDpiScaleProvider: () => 0.0);

        Assert.Equal(1.0, dpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryKeepsReportedPhysicalFramebuffer()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryUsesFullPhysicalViewportWhenFramebufferHasExtraPixels()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(840, 1736),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1736u, geometry.PixelHeight);
        Assert.Equal(0u, geometry.ViewportX);
        Assert.Equal(0u, geometry.ViewportY);
        Assert.Equal(840u, geometry.ViewportWidth);
        Assert.Equal(1736u, geometry.ViewportHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(1736.0 / 840.0, geometry.DpiScaleY);
        Assert.Equal((2.0 + (1736.0 / 840.0)) / 2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryKeepsFullViewportWhenOnlyFramebufferHeightGrows()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 420,
            clientHeight: 840,
            framebufferSize: new Vector2D<int>(420, 896),
            monitorDpiScale: 1.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(420u, geometry.PixelWidth);
        Assert.Equal(896u, geometry.PixelHeight);
        Assert.Equal(0u, geometry.ViewportX);
        Assert.Equal(0u, geometry.ViewportY);
        Assert.Equal(420u, geometry.ViewportWidth);
        Assert.Equal(896u, geometry.ViewportHeight);
        Assert.Equal(1.0, geometry.DpiScaleX);
        Assert.Equal(896.0 / 840.0, geometry.DpiScaleY);
        Assert.Equal((1.0 + (896.0 / 840.0)) / 2.0, geometry.DpiScale);
    }

    [Fact]
    public void ResolveRenderSurfaceGeometryUsesFullRetinaViewportForMvpWindow()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);

        Assert.Equal(760u, geometry.LogicalWidth);
        Assert.Equal(560u, geometry.LogicalHeight);
        Assert.Equal(1520u, geometry.PixelWidth);
        Assert.Equal(1120u, geometry.PixelHeight);
        Assert.Equal(0u, geometry.ViewportX);
        Assert.Equal(0u, geometry.ViewportY);
        Assert.Equal(1520u, geometry.ViewportWidth);
        Assert.Equal(1120u, geometry.ViewportHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryMapsPhysicalPointerCoordinatesToLogicalDips()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 1000,
            y: 700,
            button: WpfMouseButton.Left,
            modifiers: WpfInputModifiers.Control)
        {
            Handled = true
        };

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true);

        Assert.NotSame(input, normalized);
        Assert.Equal(WpfInputEventKind.MouseDown, normalized.Kind);
        Assert.Equal(500.0, normalized.X);
        Assert.Equal(350.0, normalized.Y);
        Assert.Equal(WpfMouseButton.Left, normalized.Button);
        Assert.Equal(WpfInputModifiers.Control, normalized.Modifiers);
        Assert.True(normalized.Handled);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryMapsUpperLeftPhysicalPointerCoordinatesToLogicalDips()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true);

        Assert.NotSame(input, normalized);
        Assert.Equal(160.0, normalized.X);
        Assert.Equal(90.0, normalized.Y);
        Assert.Equal(WpfMouseButton.Left, normalized.Button);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryKeepsLogicalPointerCoordinates()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseMove,
            x: 500,
            y: 300);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: false);

        Assert.Same(input, normalized);
        Assert.Equal(500.0, normalized.X);
        Assert.Equal(300.0, normalized.Y);
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryPreservesCocoaOwnerCoordinates()
    {
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseMove,
            x: 304,
            y: 192);
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 100,
            clientHeight: 60,
            framebufferSize: new Vector2D<int>(200, 120),
            monitorDpiScale: 2.0);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true,
            preserveNativePointerCoordinates: true);

        Assert.Same(input, normalized);
        Assert.Equal(304.0, normalized.X);
        Assert.Equal(192.0, normalized.Y);
    }

    [Fact]
    public void PointerInputCoordinateExceedsLogicalClientKeepsSilkLogicalRetinaCoordinates()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 500,
            y: 300,
            button: WpfMouseButton.Left);

        Assert.False(ProGpuWpfWindowHost.PointerInputCoordinateExceedsLogicalClient(input, geometry));
    }

    [Fact]
    public void PointerInputCoordinateExceedsLogicalClientDetectsFramebufferCoordinates()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 1000,
            y: 700,
            button: WpfMouseButton.Left);

        Assert.True(ProGpuWpfWindowHost.PointerInputCoordinateExceedsLogicalClient(input, geometry));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalKeepsRetinaPointerInputLogicalInsideClientBounds()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(760, 560),
                geometry,
                input));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalKeepsSilkLogicalCoordinatesWhenNativeWindowLooksPhysical()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(1520, 1120),
                geometry,
                input));
    }

    [Fact]
    public void NativeWindowSizeLooksPhysicalDetectsRetinaPhysicalNativeWindow()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);

        Assert.True(
            ProGpuWpfWindowHost.NativeWindowSizeLooksPhysical(
                new Vector2D<int>(1520, 1120),
                geometry));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalDetectsPointerCoordinatesOutsideLogicalBounds()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 1000,
            y: 700,
            button: WpfMouseButton.Left);

        Assert.True(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(760, 560),
                geometry,
                input));
    }

    [Fact]
    public void NativeInputCoordinatesLookPhysicalKeepsSingleScalePointerInputLogical()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(760, 560),
            monitorDpiScale: 1.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesLookPhysical(
                new Vector2D<int>(760, 560),
                geometry,
                input));
    }

    [Fact]
    public void NativeInputCoordinatesArePhysicalConvertsScaledX11PlatformInputInsideLogicalBounds()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.True(
            ProGpuWpfWindowHost.NativeInputCoordinatesArePhysical(
                isNativePlatformEvent: true,
                usesMonitorScaledWindowCoordinates: true,
                new Vector2D<int>(1520, 1120),
                geometry,
                input));
    }

    [Fact]
    public void NativeInputCoordinatesArePhysicalKeepsDiagnosticInputLogical()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 320,
            y: 180,
            button: WpfMouseButton.Left);

        Assert.False(
            ProGpuWpfWindowHost.NativeInputCoordinatesArePhysical(
                isNativePlatformEvent: false,
                usesMonitorScaledWindowCoordinates: true,
                new Vector2D<int>(1520, 1120),
                geometry,
                input));
    }

    [Fact]
    public void NormalizeInputEventForRenderSurfaceGeometryLeavesKeyboardInputUnchanged()
    {
        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            clientWidth: 760,
            clientHeight: 560,
            framebufferSize: new Vector2D<int>(1520, 1120),
            monitorDpiScale: 2.0);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.KeyDown,
            key: "A",
            scanCode: 1,
            modifiers: WpfInputModifiers.Shift);

        var normalized = ProGpuWpfWindowHost.NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            inputCoordinatesArePhysical: true);

        Assert.Same(input, normalized);
        Assert.Equal("A", normalized.Key);
        Assert.Equal(1, normalized.ScanCode);
        Assert.Equal(WpfInputModifiers.Shift, normalized.Modifiers);
    }

    [Fact]
    public void ResolveLogicalClientSizeTrustsSilkLogicalClientSize()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(840, 1680),
            framebufferSize: new Vector2D<int>(840, 1680),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(840, 1680), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeDoesNotOverrideNativeSizeWithStaleCache()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(840, 1680),
            framebufferSize: new Vector2D<int>(1680, 3360),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(840, 1680), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeUsesFramebufferFallbackWhenNativeSizeIsMissing()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(0, 0),
            framebufferSize: new Vector2D<int>(840, 1680),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeUsesCacheOnlyWhenNativeAndFramebufferAreMissing()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(0, 0),
            framebufferSize: new Vector2D<int>(0, 0),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeKeepsNativeSizeWhenSilkAlreadyReportsLogicalDips()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(420, 840),
            framebufferSize: new Vector2D<int>(840, 1680),
            cachedWidth: 420,
            cachedHeight: 840,
            monitorDpiScale: 2.0);

        Assert.Equal(new Vector2D<int>(420, 840), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeConvertsScaledX11CoordinatesToDips()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(1960, 1280),
            framebufferSize: new Vector2D<int>(1960, 1280),
            cachedWidth: 980,
            cachedHeight: 640,
            contentScale: new WpfDeviceScale(2.0, 2.0),
            windowSizeIsScaledByContentScale: true);

        Assert.Equal(new Vector2D<int>(980, 640), logicalSize);
    }

    [Fact]
    public void ResolveLogicalClientSizeKeepsWaylandAndMacOsCoordinatesInDips()
    {
        var logicalSize = ProGpuWpfWindowHost.ResolveLogicalClientSize(
            nativeSize: new Vector2D<int>(980, 640),
            framebufferSize: new Vector2D<int>(1960, 1280),
            cachedWidth: 980,
            cachedHeight: 640,
            contentScale: new WpfDeviceScale(2.0, 2.0),
            windowSizeIsScaledByContentScale: false);

        Assert.Equal(new Vector2D<int>(980, 640), logicalSize);
    }

    [Fact]
    public void ResolveNativeWindowSizeScalesLogicalDipsForScaledX11Coordinates()
    {
        var nativeSize = ProGpuWpfWindowHost.ResolveNativeWindowSizeForLogicalClientSize(
            new Vector2D<int>(900, 640),
            new WpfDeviceScale(2.0, 2.0),
            windowSizeIsScaledByContentScale: true);

        Assert.Equal(new Vector2D<int>(1800, 1280), nativeSize);
    }

    [Fact]
    public void ResolveNativeWindowSizeKeepsLogicalDipsForWaylandAndMacOs()
    {
        var nativeSize = ProGpuWpfWindowHost.ResolveNativeWindowSizeForLogicalClientSize(
            new Vector2D<int>(900, 640),
            new WpfDeviceScale(2.0, 2.0),
            windowSizeIsScaledByContentScale: false);

        Assert.Equal(new Vector2D<int>(900, 640), nativeSize);
    }

    [Fact]
    public void ResolveCachedLogicalClientDimensionPrefersLivePortableSource()
    {
        var dimension = ProGpuWpfWindowHost.ResolveCachedLogicalClientDimension(
            portablePresentationSourceDimension: 840,
            requestedLogicalDimension: 420,
            currentClientDimension: 420);

        Assert.Equal(840, dimension);
    }

    [Fact]
    public void ResolveCachedLogicalClientDimensionFallsBackToRequestedSize()
    {
        var dimension = ProGpuWpfWindowHost.ResolveCachedLogicalClientDimension(
            portablePresentationSourceDimension: 0,
            requestedLogicalDimension: 420,
            currentClientDimension: 840);

        Assert.Equal(420, dimension);
    }

    [Fact]
    public void NativeResizeCorrectsStalePhysicalClientSizeBeforeTargetLoad()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 840,
            Height = 1680
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(420, 840)));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            host.Width,
            host.Height,
            framebufferSize: new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0);
        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScaleX);
        Assert.Equal(2.0, geometry.DpiScaleY);
    }

    [Fact]
    public void NativeResizeTrustsSilkLogicalClientSizeOnHighDpiMonitor()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0));

        Assert.Equal(840, host.Width);
        Assert.Equal(1680, host.Height);
    }

    [Fact]
    public void NativeResizeTrustsSilkLogicalClientSizeWhenFramebufferReportIsMissing()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(0, 0),
            monitorDpiScale: 2.0));

        Assert.Equal(840, host.Width);
        Assert.Equal(1680, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsActualLogicalResizeWhenItIsNotDpiScaleMultiple()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(600, 900),
            new Vector2D<int>(1200, 1800),
            monitorDpiScale: 2.0));

        Assert.Equal(600, host.Width);
        Assert.Equal(900, host.Height);
    }

    [Fact]
    public void NativeResizeDoesNotTreatACommonDpiRatioAsPhysicalSize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 2.0));

        Assert.Equal(840, host.Width);
        Assert.Equal(1680, host.Height);
    }

    [Fact]
    public void NativeResizeDoesNotLetPortableSourceCacheOverrideSilkClientSize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));
        Assert.True(host.UpdatePortablePresentationSourceClientSize(840, 1680));

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0));

        Assert.Equal(840, host.Width);
        Assert.Equal(1680, host.Height);
    }

    [Fact]
    public void NativeResizeUsesSilkClientSizeWhenMonitorScaleIsUnavailable()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 1.0));

        Assert.Equal(840, host.Width);
        Assert.Equal(1680, host.Height);
    }

    [Fact]
    public void NativeResizeAppliesMaximizedWslgClientSizeInsteadOfStaleCache()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 1100,
            Height = 700
        });

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(1920, 1040),
            new Vector2D<int>(1920, 1040),
            monitorDpiScale: 2.0));

        Assert.Equal(1920, host.Width);
        Assert.Equal(1040, host.Height);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            host.Width,
            host.Height,
            framebufferSize: new Vector2D<int>(1920, 1040),
            monitorDpiScale: 2.0);

        Assert.Equal(1920u, geometry.LogicalWidth);
        Assert.Equal(1040u, geometry.LogicalHeight);
        Assert.Equal(1920u, geometry.PixelWidth);
        Assert.Equal(1040u, geometry.PixelHeight);
        Assert.Equal(1.0, geometry.DpiScale);
    }

    [Fact]
    public void NativeResizeDoesNotUseStaleRootRenderSizeForRealLogicalResize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });
        var root = new TestRootElement();
        root.SetRenderSize(420, 840);
        host.WpfRootVisual = root;

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(600, 900),
            new Vector2D<int>(1200, 1800),
            monitorDpiScale: 2.0));

        Assert.Equal(600, host.Width);
        Assert.Equal(900, host.Height);
    }

    [Fact]
    public void NativeResizeKeepsRealLogicalResizeWhenItMatchesPreviousDpiScaleMultiple()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 760,
            Height = 560
        });
        var root = new TestRootElement();
        root.SetRenderSize(760, 560);
        host.WpfRootVisual = root;
        host.RecordPresentedFrame(new ProGpuWpfFrameState(
            pixelWidth: 1520,
            pixelHeight: 1120,
            sceneChangeVersion: 1,
            retainedWpfChangeVersion: 1,
            flatDrawingChangeVersion: 0,
            logicalWidth: 760,
            logicalHeight: 560,
            dpiScale: 2.0));

        Assert.True(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(1520, 1120),
            new Vector2D<int>(3040, 2240),
            monitorDpiScale: 2.0));

        Assert.Equal(1520, host.Width);
        Assert.Equal(1120, host.Height);
    }

    [Fact]
    public void NativeResizeDoesNotLetPortablePresentationSourceOverrideSilkClientSize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 840,
            Height = 1680
        });
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));
        Assert.True(host.UpdatePortablePresentationSourceClientSize(420, 840));

        Assert.False(host.UpdateClientSizeFromNativeResize(
            new Vector2D<int>(840, 1680),
            new Vector2D<int>(1680, 3360),
            monitorDpiScale: 1.0));

        Assert.Equal(840, host.Width);
        Assert.Equal(1680, host.Height);
    }

    [Fact]
    public void NativeResizeIgnoresZeroSizeAndReturnsFalseForUnchangedClientSize()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 420,
            Height = 840
        });

        Assert.False(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(420, 840)));
        Assert.False(host.UpdateClientSizeFromNativeResize(new Vector2D<int>(0, -4)));

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
    }

    [Fact]
    public void ProcessDispatcherQueueRunsQueuedPlatformCallbacks()
    {
        var dispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: false);
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = CreatePlatformServices(dispatcher)
        };
        var ran = false;

        host.PlatformServices.Dispatcher.Post(() => ran = true);

        Assert.True(host.ProcessDispatcherQueue());
        Assert.True(ran);
    }

    [Fact]
    public void DispatcherWorkAvailableProcessesQueuedPlatformCallbacksOnOwnerThread()
    {
        var dispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: true);
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = CreatePlatformServices(dispatcher)
        };
        var ran = false;

        dispatcher.Post(() => ran = true, WpfDispatcherPriority.Render);

        Assert.True(ran);
        Assert.Equal(1, host.DispatcherWakeupCount);
        Assert.False(host.ProcessDispatcherQueue());
    }

    [Fact]
    public void DispatcherWorkAvailableFromWorkerThreadWaitsForOwnerThreadPump()
    {
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = new CrossPlatformWpfPlatformServices()
        };
        var dispatcher = Assert.IsType<QueuedWpfDispatcherService>(host.PlatformServices.Dispatcher);
        var ran = false;
        var worker = new Thread(() => dispatcher.Post(() => ran = true));

        worker.Start();
        worker.Join();

        Assert.False(ran);
        Assert.Equal(1, host.DispatcherWakeupCount);
        Assert.Equal(0, host.NativeLoopWakeupCount);
        Assert.True(host.ProcessDispatcherQueue());
        Assert.True(ran);
    }

    [Fact]
    public void ReplacingPlatformServicesDisconnectsPreviousDispatcherWakeupSource()
    {
        var firstDispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: true);
        var secondDispatcher = new TestDispatcherService(raiseWorkAvailableOnPost: true);
        using var host = new ProGpuWpfWindowHost
        {
            PlatformServices = CreatePlatformServices(firstDispatcher)
        };
        host.PlatformServices = CreatePlatformServices(secondDispatcher);
        var firstRan = false;
        var secondRan = false;

        firstDispatcher.Post(() => firstRan = true);
        secondDispatcher.Post(() => secondRan = true);

        Assert.False(firstRan);
        Assert.True(secondRan);
        Assert.Equal(1, host.DispatcherWakeupCount);
    }

    [Fact]
    public void InvokeSourceDrawRunsWpfDrawAndCapturesResult()
    {
        using var host = new ProGpuWpfWindowHost();
        var nativeContext = new ProGpuDrawingContext();
        using var mediaContext = new MediaDrawingContext(nativeContext);
        using var sourceContext = new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(mediaContext));
        var args = new ProGpuWpfFrameEventArgs(mediaContext, 100, 50, 0.016, 2);

        host.WpfDraw = (context, frame) =>
        {
            Assert.Same(args, frame);
            context.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
            context.PushOpacity(0.5);
        };

        host.InvokeSourceDraw(sourceContext, args);

        Assert.Equal(new WpfCompositionDrawingContextResult(3, 3, 0), host.LastSourceDrawingResult);
        Assert.Equal(new[]
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.PushOpacity,
            ProGpuRenderCommandType.PopOpacity
        }, nativeContext.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public void FrameEventArgsCanExposeActiveDrawingFrame()
    {
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);
        using var mediaContext = frame.OpenDrawingContext();

        var args = new ProGpuWpfFrameEventArgs(mediaContext, 100, 50, 0.016, 2, frame);

        Assert.Same(frame, args.DrawingFrame);
    }

    [Fact]
    public void InvokeSourceDrawResetsResultWhenNoSourceCallbackIsRegistered()
    {
        using var host = new ProGpuWpfWindowHost();
        var nativeContext = new ProGpuDrawingContext();
        using var mediaContext = new MediaDrawingContext(nativeContext);
        using var sourceContext = new WpfCompositionDrawingContext(
            new ProGpuCompositionCommandSink(mediaContext));
        var args = new ProGpuWpfFrameEventArgs(mediaContext, 100, 50, 0.016, 2);

        sourceContext.DrawVideo(new object(), new Rect(0, 0, 1, 1));
        host.InvokeSourceDraw(sourceContext, args);

        Assert.Equal(default, host.LastSourceDrawingResult);
        Assert.Empty(nativeContext.Commands);
    }

    [Fact]
    public void DefaultRenderDataSinkProviderRegistrationScopesTypedProvider()
    {
        using var host = new ProGpuWpfWindowHost();
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);

        using (IDisposable? registration = host.RegisterRenderDataSinkProvider(frame))
        {
            Assert.NotNull(registration);
            Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
        }

        Assert.Null(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
    }

    [Fact]
    public void RenderDataSinkProviderRegistrationFactoryCanBeScopedAndDisposed()
    {
        using var host = new ProGpuWpfWindowHost();
        var frame = new ProGpuWpfDrawingFrame(new ProGPU.Scene.DrawingVisual(), 100, 50);
        var registration = new TestRegistration();
        ProGpuWpfDrawingFrame? capturedFrame = null;
        host.RenderDataSinkProviderRegistrationFactory = (drawingFrame, _) =>
        {
            capturedFrame = drawingFrame;
            return registration;
        };

        using (host.RegisterRenderDataSinkProvider(frame))
        {
            Assert.Same(frame, capturedFrame);
            Assert.False(registration.IsDisposed);
        }

        Assert.True(registration.IsDisposed);
    }

    [Fact]
    public void TryBindPortablePresentationSourceMirrorsRootIntoHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };

        var bound = host.TryBindPortablePresentationSource(source);

        Assert.True(bound);
        Assert.Same(source, host.PortablePresentationSource);
        Assert.NotNull(host.PortablePresentationSourceBridge);
        Assert.Same(source.RootVisual, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void ReplacingPortablePresentationSourceUnsubscribesPreviousSource()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var first = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        var second = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };

        Assert.True(host.TryBindPortablePresentationSource(first));
        Assert.True(host.TryBindPortablePresentationSource(second));
        var requestCountAfterReplacement = scheduler.RequestCount;

        first.RootVisual = new object();

        Assert.Same(second, host.PortablePresentationSource);
        Assert.Same(second.RootVisual, host.WpfRootVisual);
        Assert.Equal(requestCountAfterReplacement, scheduler.RequestCount);
    }

    [Fact]
    public void PortablePopupHostCreatesAndControlsPopupForBoundOwner()
    {
        var scheduler = new TestRenderScheduler();
        var popupPresentationSource = new FakePortablePresentationSource();
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => popupPresentationSource);
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 24,
            y: 32,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.Equal(24, popupPresentationSource.ClientOriginX);
        Assert.Equal(32, popupPresentationSource.ClientOriginY);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 200, 80));
        Assert.True(host.TrySetPortablePopupPosition(popupSource!, 48, 64));
        Assert.Equal(48, popupPresentationSource.ClientOriginX);
        Assert.Equal(64, popupPresentationSource.ClientOriginY);
        Assert.True(host.TryShowPortablePopup(popupSource!));
        Assert.True(host.TrySetPortablePopupHitTestable(popupSource!, false));
        Assert.True(host.TryHidePortablePopup(popupSource!));
        Assert.True(host.TryDestroyPortablePopup(popupSource!));
        Assert.False(host.TryShowPortablePopup(popupSource!));
        Assert.True(scheduler.RequestCount > 1);
    }

    [Fact]
    public void PortablePopupUsesNativeHostLifecycleAndLocalInputWhenAvailable()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        var popupPresentationSource = new FakePortablePresentationSource();
        var nativeHost = new FakePortableNativePopupHost();
        var previousSourceFactory = WpfPortablePopupBridge.PortablePresentationSourceFactory;
        var previousNativeHostFactory = WpfPortablePopupBridge.NativePopupHostFactory;
        WpfPortablePopupBridge.PortablePresentationSourceFactory = (_, _) => popupPresentationSource;
        WpfPortablePopupBridge.NativePopupHostFactory = (_, _, _, _, _) => nativeHost;
        try
        {
            using var host = new ProGpuWpfWindowHost();
            var owner = new FakePortablePresentationSource { RootVisual = new object() };
            Assert.True(host.TryBindPortablePresentationSource(owner));
            var request = new PortablePopupCreateRequest(
                placementTarget: null,
                ownerPresentationSource: owner,
                ownerHandle: owner.Handle,
                popupScreenDeviceX: 20,
                popupScreenDeviceY: 30,
                ownerClientScreenDeviceX: 0,
                ownerClientScreenDeviceY: 0,
                isTransparent: false,
                isChildPopup: false);

            Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
            Assert.NotNull(nativeHost.InputHandler);
            Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
            Assert.True(host.TrySetPortablePopupPosition(popupSource!, 40, 50));
            Assert.True(host.TryShowPortablePopup(popupSource!));
            Assert.True(host.HasVisibleNativePortablePopup);
            host.GetPortablePopupDiagnostics(
                out int openCount,
                out int visibleCount,
                out int nativeWindowCount,
                out int presentedNativeWindowCount,
                out int nativeWindowGpuHitTestCount,
                out int nativeWindowGpuHitTestOwnerCount);
            Assert.Equal(1, openCount);
            Assert.Equal(1, visibleCount);
            Assert.Equal(1, nativeWindowCount);
            Assert.Equal(1, presentedNativeWindowCount);
            Assert.Equal(1, nativeWindowGpuHitTestCount);
            Assert.Equal(1, nativeWindowGpuHitTestOwnerCount);
            Assert.Equal((100, 80), nativeHost.Size);
            Assert.Equal((40, 50), nativeHost.Position);
            Assert.Equal(1, nativeHost.ShowCount);

            var transitionalOutsideInput = new WpfInputEventArgs(
                WpfInputEventKind.MouseMove,
                x: 7,
                y: -0.25);
            Assert.False(nativeHost.InputHandler!(transitionalOutsideInput));
            Assert.Equal(0, activationService.PresentationSourceInputCount);
            Assert.True(ProGpuWpfDiagnostics.TryRaiseTopmostNativePopupInput(
                host,
                transitionalOutsideInput));
            Assert.Equal(0, activationService.PresentationSourceInputCount);

            var input = new WpfInputEventArgs(
                WpfInputEventKind.MouseDown,
                x: 7,
                y: 9,
                button: WpfMouseButton.Left);
            Assert.True(ProGpuWpfDiagnostics.TryRaiseTopmostNativePopupLocalInput(host, input));
            Assert.Same(popupSource, activationService.LastPresentationSourceInputSource);
            Assert.Equal(7, activationService.LastPresentationSourceInput!.X);
            Assert.Equal(9, activationService.LastPresentationSourceInput.Y);

            Assert.True(host.TryHidePortablePopup(popupSource!));
            Assert.False(host.HasVisibleNativePortablePopup);
            Assert.Equal(1, nativeHost.HideCount);
            Assert.True(host.TryDestroyPortablePopup(popupSource!));
            Assert.True(nativeHost.IsDisposed);
        }
        finally
        {
            WpfPortablePopupBridge.PortablePresentationSourceFactory = previousSourceFactory;
            WpfPortablePopupBridge.NativePopupHostFactory = previousNativeHostFactory;
        }
    }

    [Fact]
    public void PortablePopupInputRoutesToPopupPresentationSourceWithLocalCoordinates()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => new FakePortablePresentationSource());
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 20,
            y: 30,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));

        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 25,
            y: 35,
            button: WpfMouseButton.Left);

        Assert.True(host.TryProcessPortablePopupInput(input));

        Assert.True(input.Handled);
        Assert.Equal(1, activationService.PresentationSourceInputCount);
        Assert.Same(popupSource, activationService.LastPresentationSourceInputSource);
        Assert.NotNull(activationService.LastPresentationSourceInput);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);

    }

    [Fact]
    public void PortablePopupGpuMissFallsBackToItsVisualTree()
    {
        var popupPresentationSource = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => popupPresentationSource);
        using var host = new ProGpuWpfWindowHost();
        var target = ProGpuWpfCompositionTarget.CreateHeadless();
        SetPrivateField(host, "_target", target);
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));

        object unrelatedOwner = new();
        int unrelatedOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(unrelatedOwner);
        var index = GpuHitTestIndex.Build(
        [
            GpuHitTestPrimitive.RectangleFill(
                unrelatedOwnerId,
                new System.Numerics.Vector2(20f, 30f),
                new System.Numerics.Vector2(100f, 80f),
                System.Numerics.Vector2.Zero,
                zIndex: 0f)
        ]);
        typeof(global::ProGPU.Scene.Compositor)
            .GetMethod("SetLastHitTestIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target.Compositor, new object[] { index });

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 20,
            y: 30,
            isTransparent: false,
            isChildPopup: false);
        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));

        Assert.Null(popupPresentationSource.HitTestOverride!(5, 5));
        Assert.False(popupPresentationSource.HitTestAllBufferOverride!(5, 5, new object?[4], out int ownerCount));
        Assert.Equal(0, ownerCount);
    }

    [Fact]
    public void PortablePopupInputSubtractsOwnerClientScreenOrigin()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        var popupPresentationSource = new FakePortablePresentationSource();
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => popupPresentationSource);
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        host.SetPosition(100, 200);
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            popupScreenDeviceX: 120,
            popupScreenDeviceY: 230,
            ownerClientScreenDeviceX: 100,
            ownerClientScreenDeviceY: 200,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));
        Assert.Equal(100, owner.ClientOriginX);
        Assert.Equal(200, owner.ClientOriginY);
        Assert.Equal(120, popupPresentationSource.ClientOriginX);
        Assert.Equal(230, popupPresentationSource.ClientOriginY);

        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 25,
            y: 35,
            button: WpfMouseButton.Left);

        Assert.True(host.TryProcessPortablePopupInput(input));
        Assert.NotNull(activationService.LastPresentationSourceInput);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);

        host.SetPosition(110, 210);
        Assert.Equal(110, owner.ClientOriginX);
        Assert.Equal(210, owner.ClientOriginY);
        Assert.Equal(130, popupPresentationSource.ClientOriginX);
        Assert.Equal(240, popupPresentationSource.ClientOriginY);
        var movedOwnerInput = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 25,
            y: 35,
            button: WpfMouseButton.Left);

        Assert.True(host.TryProcessPortablePopupInput(movedOwnerInput));
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);
    }

    [Fact]
    public void PortablePopupInputUsesLogicalCoordinatesAfterDpiScale()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        var popupPresentationSource = new FakePortablePresentationSource();
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => popupPresentationSource);
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(owner));
        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            x: 20,
            y: 30,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.NotNull(popupSource);
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));
        Assert.Equal(10, popupPresentationSource.ClientOriginX);
        Assert.Equal(15, popupPresentationSource.ClientOriginY);
        Assert.Equal(100, popupPresentationSource.ClientWidth);
        Assert.Equal(80, popupPresentationSource.ClientHeight);

        var outsideInput = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 9,
            y: 14,
            button: WpfMouseButton.Left);
        Assert.False(host.TryProcessPortablePopupInput(outsideInput));
        Assert.Equal(0, activationService.PresentationSourceInputCount);

        var insideInput = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 15,
            y: 20,
            button: WpfMouseButton.Left);
        Assert.True(host.TryProcessPortablePopupInput(insideInput));

        Assert.True(insideInput.Handled);
        Assert.Equal(1, activationService.PresentationSourceInputCount);
        Assert.Same(popupSource, activationService.LastPresentationSourceInputSource);
        Assert.NotNull(activationService.LastPresentationSourceInput);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);
    }

    [Fact]
    public void PortablePopupTracksOwnerDpiChangesAndPreservesLogicalOrigin()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        var popupPresentationSource = new FakePortablePresentationSource();
        using var popupSourceFactory = UsePortablePopupSourceFactory(() => popupPresentationSource);
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        host.SetPosition(100, 200);
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var request = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            popupScreenDeviceX: 120,
            popupScreenDeviceY: 230,
            ownerClientScreenDeviceX: 100,
            ownerClientScreenDeviceY: 200,
            isTransparent: false,
            isChildPopup: false);

        Assert.True(host.TryCreatePortablePopup(request, out object? popupSource));
        Assert.True(host.TrySetPortablePopupSize(popupSource!, 100, 80));
        Assert.True(host.TryShowPortablePopup(popupSource!));

        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));

        Assert.Equal(2.0, popupPresentationSource.DpiScaleX);
        Assert.Equal(2.0, popupPresentationSource.DpiScaleY);
        Assert.Equal(100, owner.ClientOriginX);
        Assert.Equal(200, owner.ClientOriginY);
        Assert.Equal(120, popupPresentationSource.ClientOriginX);
        Assert.Equal(230, popupPresentationSource.ClientOriginY);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 25,
            y: 35,
            button: WpfMouseButton.Left);
        Assert.True(host.TryProcessPortablePopupInput(input));
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(5, activationService.LastPresentationSourceInput.Y);
    }

    [Fact]
    public void NestedPortablePopupTracksOwnerDeviceOriginAndKeepsLocalInputCoordinates()
    {
        var activationService = new TestWindowActivationServiceRegistrar();
        using var activationRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(activationService);
        var parentPopupSource = new FakePortablePresentationSource();
        var nestedPopupSource = new FakePortablePresentationSource();
        int sourceIndex = 0;
        using var popupSourceFactory = UsePortablePopupSourceFactory(
            () => sourceIndex++ == 0 ? parentPopupSource : nestedPopupSource);
        using var host = new ProGpuWpfWindowHost();
        var owner = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        host.SetPosition(100, 200);
        Assert.True(host.TryBindPortablePresentationSource(owner));

        var parentRequest = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: owner,
            ownerHandle: owner.Handle,
            popupScreenDeviceX: 140,
            popupScreenDeviceY: 230,
            ownerClientScreenDeviceX: 100,
            ownerClientScreenDeviceY: 200,
            isTransparent: false,
            isChildPopup: false);
        Assert.True(host.TryCreatePortablePopup(parentRequest, out object? parentSource));
        Assert.True(host.TrySetPortablePopupSize(parentSource!, 120, 90));
        Assert.True(host.TryShowPortablePopup(parentSource!));

        var nestedRequest = new PortablePopupCreateRequest(
            placementTarget: null,
            ownerPresentationSource: parentSource,
            ownerHandle: IntPtr.Zero,
            popupScreenDeviceX: 230,
            popupScreenDeviceY: 270,
            ownerClientScreenDeviceX: 140,
            ownerClientScreenDeviceY: 230,
            isTransparent: false,
            isChildPopup: false);
        Assert.True(host.TryCreatePortablePopup(nestedRequest, out object? nestedSource));
        Assert.True(host.TrySetPortablePopupSize(nestedSource!, 80, 60));
        Assert.True(host.TryShowPortablePopup(nestedSource!));

        Assert.Equal(230, nestedPopupSource.ClientOriginX);
        Assert.Equal(270, nestedPopupSource.ClientOriginY);
        Assert.True(host.TrySetPortablePopupPosition(parentSource!, 160, 250));
        Assert.Equal(250, nestedPopupSource.ClientOriginX);
        Assert.Equal(290, nestedPopupSource.ClientOriginY);
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 155,
            y: 96,
            button: WpfMouseButton.Left);
        Assert.True(host.TryProcessPortablePopupInput(input));
        Assert.Same(nestedSource, activationService.LastPresentationSourceInputSource);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(6, activationService.LastPresentationSourceInput.Y);

        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
        Assert.Equal(160, parentPopupSource.ClientOriginX);
        Assert.Equal(250, parentPopupSource.ClientOriginY);
        Assert.Equal(250, nestedPopupSource.ClientOriginX);
        Assert.Equal(290, nestedPopupSource.ClientOriginY);
        var scaledInput = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 155,
            y: 96,
            button: WpfMouseButton.Left);
        Assert.True(host.TryProcessPortablePopupInput(scaledInput));
        Assert.Same(nestedSource, activationService.LastPresentationSourceInputSource);
        Assert.Equal(5, activationService.LastPresentationSourceInput!.X);
        Assert.Equal(6, activationService.LastPresentationSourceInput.Y);
    }

    [Fact]
    public void PortablePopupSinkDoesNotReplaceMainWindowInvalidationRoot()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var mainRoot = new object();
        target.WpfInvalidationTracker.Attach(mainRoot);
        target.WpfInvalidationTracker.ConsumeDirty();

        var frame = target.BeginDrawingFrame(
            pixelWidth: 200,
            pixelHeight: 100,
            clearRetainedWpfVisualRoot: false,
            logicalWidth: 200,
            logicalHeight: 100,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0);
        using var sink = new ProGpuRetainedCompositionCommandSink(
            frame,
            target.Context,
            target.Viewport3DTextureCache,
            ProGpuRetainedCompositionLayer.Popup);

        Assert.Same(mainRoot, target.WpfInvalidationTracker.Root);
        Assert.False(target.WpfInvalidationTracker.IsDirty);
        Assert.Empty(target.RetainedWpfVisualRoot.Children);
        Assert.Single(target.PopupRetainedWpfVisualRoot.Children);
    }

    [Fact]
    public void DrawingFrameKeepsPortablePopupLayerAboveMainWpfDrawingLayer()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();

        target.BeginDrawingFrame(
            pixelWidth: 200,
            pixelHeight: 100,
            clearRetainedWpfVisualRoot: true,
            logicalWidth: 200,
            logicalHeight: 100,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0);

        Assert.Equal(3, target.SceneRootVisual.Children.Count);
        Assert.Same(target.RetainedWpfVisualRoot, target.SceneRootVisual.Children[0]);
        Assert.Same(target.RootVisual, target.SceneRootVisual.Children[1]);
        Assert.Same(target.PopupRetainedWpfVisualRoot, target.SceneRootVisual.Children[2]);
    }

    [Fact]
    public void UpdatePortablePresentationSourceDpiScaleCoalescesUnchangedScale()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
        Assert.False(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.5, 2.0));

        Assert.Equal(2.5, source.DpiScaleX);
        Assert.Equal(2.0, source.DpiScaleY);
        Assert.Equal(2, source.DeviceScaleChangeCount);
        Assert.Equal(2, scheduler.RequestCount);
    }

    [Theory]
    [InlineData(628, 2.0, 314)]
    [InlineData(431, 2.0, 216)]
    [InlineData(-301, 1.5, -201)]
    [InlineData(40, 0.0, 40)]
    public void NativePopupConvertsDevicePixelsToLogicalScreenCoordinates(
        int deviceCoordinate,
        double deviceScale,
        int expectedLogicalCoordinate)
    {
        Assert.Equal(
            expectedLogicalCoordinate,
            WpfPortableNativePopupHost.ToNativeLogicalScreenCoordinate(deviceCoordinate, deviceScale));
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void NativePopupUsesTransientWindowWhenPositioningIsAvailable(
        bool isWindows,
        bool isMacOS,
        bool explicitlyDisabled,
        bool isWayland,
        bool expected)
    {
        Assert.Equal(
            expected,
            WpfPortableNativePopupHost.ShouldUseNativePopup(
                isWindows,
                isMacOS,
                explicitlyDisabled,
                isWayland));
    }

    [Theory]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, true, true, true, false)]
    public void NativePopupPumpsOnlyWhileVisible(
        bool isDisposed,
        bool isInitialized,
        bool isVisible,
        bool isPumping,
        bool expected)
    {
        Assert.Equal(
            expected,
            WpfPortableNativePopupHost.ShouldPumpEvents(
                isDisposed,
                isInitialized,
                isVisible,
                isPumping));
    }

    [Theory]
    [InlineData(true, 152, 96, 120, 80, 100, 60, 32, 16, true)]
    [InlineData(false, 32, 16, 120, 80, 100, 60, 32, 16, true)]
    [InlineData(true, 119.75, 96, 120, 80, 100, 60, -0.25, 16, false)]
    [InlineData(false, 32, 60.25, 120, 80, 100, 60, 32, 60.25, false)]
    [InlineData(true, double.NaN, 96, 120, 80, 100, 60, double.NaN, 16, false)]
    public void NativePopupNormalizesCocoaOwnerCoordinatesWithoutAllocating(
        bool coordinatesAreOwnerRelative,
        double inputX,
        double inputY,
        double popupOwnerX,
        double popupOwnerY,
        double popupWidth,
        double popupHeight,
        double expectedX,
        double expectedY,
        bool expected)
    {
        Assert.Equal(
            expected,
            WpfPortablePopupBridge.TryNormalizeNativePointerCoordinates(
                coordinatesAreOwnerRelative,
                inputX,
                inputY,
                popupOwnerX,
                popupOwnerY,
                popupWidth,
                popupHeight,
                out double localX,
                out double localY));
        Assert.Equal(expectedX, localX);
        Assert.Equal(expectedY, localY);
    }

    [Fact]
    public void NativePopupPointerCoordinateNormalizationDoesNotAllocate()
    {
        Assert.True(WpfPortablePopupBridge.TryNormalizeNativePointerCoordinates(
            coordinatesAreOwnerRelative: true,
            inputX: 152,
            inputY: 96,
            popupOwnerX: 120,
            popupOwnerY: 80,
            popupWidth: 100,
            popupHeight: 60,
            out _,
            out _));

        double checksum = 0;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000_000; i++)
        {
            bool accepted = WpfPortablePopupBridge.TryNormalizeNativePointerCoordinates(
                coordinatesAreOwnerRelative: true,
                inputX: 152,
                inputY: 96,
                popupOwnerX: 120,
                popupOwnerY: 80,
                popupWidth: 100,
                popupHeight: 60,
                out double localX,
                out double localY);
            checksum += accepted ? localX + localY : 0;
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(48_000_000, checksum);
        Assert.Equal(0, allocatedBytes);
    }

    [Fact]
    public void NativePopupLocalDiagnosticInputUsesPlatformCoordinateContract()
    {
        var input = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 18,
            y: 12,
            button: WpfMouseButton.Left,
            modifiers: WpfInputModifiers.Control);

        WpfInputEventArgs cocoa = WpfPortablePopupBridge.CreateNativeDiagnosticInput(
            coordinatesAreOwnerRelative: true,
            input,
            popupOwnerX: 54,
            popupOwnerY: 80);
        Assert.NotSame(input, cocoa);
        Assert.Equal(72, cocoa.X);
        Assert.Equal(92, cocoa.Y);
        Assert.Equal(input.Kind, cocoa.Kind);
        Assert.Equal(input.Button, cocoa.Button);
        Assert.Equal(input.Modifiers, cocoa.Modifiers);

        WpfInputEventArgs x11 = WpfPortablePopupBridge.CreateNativeDiagnosticInput(
            coordinatesAreOwnerRelative: false,
            input,
            popupOwnerX: 54,
            popupOwnerY: 80);
        Assert.Same(input, x11);
    }

    [Theory]
    [InlineData(314, 2.0, 628)]
    [InlineData(216, 2.0, 432)]
    [InlineData(-201, 1.5, -302)]
    [InlineData(40, double.NaN, 40)]
    public void OwnerWindowConvertsLogicalScreenCoordinatesToDevicePixels(
        int logicalCoordinate,
        double deviceScale,
        int expectedDeviceCoordinate)
    {
        Assert.Equal(
            expectedDeviceCoordinate,
            ProGpuWpfWindowHost.ToDeviceScreenCoordinate(logicalCoordinate, deviceScale));
    }

    [Fact]
    public void UpdatePortablePresentationSourceClientSizeCoalescesUnchangedLogicalSize()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.True(host.UpdatePortablePresentationSourceClientSize(420, 840));
        Assert.False(host.UpdatePortablePresentationSourceClientSize(420, 840));
        Assert.True(host.UpdatePortablePresentationSourceClientSize(640, 480));

        Assert.Equal(640, source.ClientWidth);
        Assert.Equal(480, source.ClientHeight);
        Assert.Equal(2, source.ClientSizeChangeCount);
        Assert.Equal(2, scheduler.RequestCount);
    }

    [Fact]
    public void SetClientSizeSynchronizesBoundPortablePresentationSourceImmediately()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        host.SetClientSize(640, 480);

        Assert.Equal(640, host.Width);
        Assert.Equal(480, host.Height);
        Assert.Equal(640, source.ClientWidth);
        Assert.Equal(480, source.ClientHeight);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.Equal(2, scheduler.RequestCount);
    }

    [Fact]
    public void SetInitialClientSizeCachesLogicalSizeWithoutPortableSourceRelayout()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Width = 1280,
            Height = 800
        })
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        host.SetInitialClientSize(420, 840);

        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
        Assert.Equal(0, source.ClientSizeChangeCount);
        Assert.Equal(1, scheduler.RequestCount);

        var geometry = ProGpuWpfWindowHost.ResolveRenderSurfaceGeometry(
            host.Width,
            host.Height,
            framebufferSize: new Vector2D<int>(840, 1680),
            monitorDpiScale: 2.0);

        Assert.Equal(420u, geometry.LogicalWidth);
        Assert.Equal(840u, geometry.LogicalHeight);
        Assert.Equal(840u, geometry.PixelWidth);
        Assert.Equal(1680u, geometry.PixelHeight);
        Assert.Equal(2.0, geometry.DpiScale);
    }

    [Fact]
    public void SynchronizePortablePresentationSourceGeometryCachesHighDpiSurfaceGeometry()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var geometry = new ProGpuWpfWindowHost.RenderSurfaceGeometry(
            LogicalWidth: 420,
            LogicalHeight: 840,
            PixelWidth: 840,
            PixelHeight: 1680,
            DpiScaleX: 2.0,
            DpiScaleY: 2.0,
            DpiScale: 2.0);

        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.True(host.SynchronizePortablePresentationSourceGeometry(geometry));

        Assert.Equal(geometry, host.LastResolvedRenderSurfaceGeometry);
        Assert.Equal(420, source.ClientWidth);
        Assert.Equal(840, source.ClientHeight);
        Assert.Equal(2.0, source.DpiScaleX);
        Assert.Equal(2.0, source.DpiScaleY);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.Equal(1, source.DeviceScaleChangeCount);
        Assert.Equal(new[] { "DeviceScale", "ClientSize" }, source.CallLog);
        Assert.Equal(2, scheduler.RequestCount);
        Assert.True(host.ForceFullWpfReplayForNextFrame);
    }

    [Fact]
    public void UpdatingPortablePresentationSourceClientSizeForcesFullWpfReplay()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.False(host.ForceFullWpfReplayForNextFrame);

        Assert.True(host.UpdatePortablePresentationSourceClientSize(420, 840));

        Assert.True(host.ForceFullWpfReplayForNextFrame);
    }

    [Fact]
    public void UpdatingPortablePresentationSourceDpiScaleForcesFullWpfReplay()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();
        Assert.True(host.TryBindPortablePresentationSource(source));

        Assert.False(host.ForceFullWpfReplayForNextFrame);

        Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));

        Assert.True(host.ForceFullWpfReplayForNextFrame);
    }

    [Fact]
    public void DisposingHostDetachesPortablePresentationSource()
    {
        var scheduler = new TestRenderScheduler();
        var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource
        {
            RootVisual = new object()
        };
        Assert.True(host.TryBindPortablePresentationSource(source));

        host.Dispose();
        var requestCountAfterDispose = scheduler.RequestCount;
        source.RootVisual = new object();

        Assert.Null(host.WpfRootVisual);
        Assert.Equal(requestCountAfterDispose, scheduler.RequestCount);
        Assert.False(source.IsDisposed);
    }

    private static CrossPlatformWpfPlatformServices CreatePlatformServices(IWpfDispatcherService dispatcher)
    {
        return new CrossPlatformWpfPlatformServices(
            new ProcessWpfLauncher(),
            new SilkNetWpfMonitorService(),
            new ProcessWpfClipboard(),
            new SilkNetWpfCursorService(),
            dispatcher,
            new ProcessWpfFileDialogService());
    }

    private sealed class TestDispatcherService : IWpfDispatcherService
    {
        private readonly Queue<TestDispatcherOperation> _operations = new();
        private readonly bool _raiseWorkAvailableOnPost;

        public TestDispatcherService(bool raiseWorkAvailableOnPost)
        {
            _raiseWorkAvailableOnPost = raiseWorkAvailableOnPost;
        }

        public event EventHandler? WorkAvailable;

        public bool CheckAccess()
        {
            return true;
        }

        public IWpfDispatcherOperation Post(Action callback, WpfDispatcherPriority priority = WpfDispatcherPriority.Normal)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var operation = new TestDispatcherOperation(callback, priority);
            _operations.Enqueue(operation);
            if (_raiseWorkAvailableOnPost)
            {
                WorkAvailable?.Invoke(this, EventArgs.Empty);
            }

            return operation;
        }

        public bool ProcessPending()
        {
            var processed = false;
            while (_operations.Count > 0)
            {
                var operation = _operations.Dequeue();
                if (operation.IsCanceled)
                {
                    continue;
                }

                operation.Invoke();
                operation.MarkCompleted();
                processed = true;
            }

            return processed;
        }
    }

    private sealed class TestDispatcherOperation : IWpfDispatcherOperation
    {
        private readonly Action _callback;

        public TestDispatcherOperation(Action callback, WpfDispatcherPriority priority)
        {
            _callback = callback;
            Priority = priority;
        }

        public WpfDispatcherPriority Priority { get; }

        public bool IsCanceled { get; private set; }

        public bool IsCompleted { get; private set; }

        public bool Cancel()
        {
            if (IsCanceled || IsCompleted)
            {
                return false;
            }

            IsCanceled = true;
            return true;
        }

        public void Dispose()
        {
            Cancel();
        }

        public void Invoke()
        {
            _callback();
        }

        public void MarkCompleted()
        {
            IsCompleted = true;
        }
    }

    private sealed class TestRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested;

        public bool HasPendingRenderRequest { get; private set; }

        public int RequestCount { get; private set; }

        public void RequestRender()
        {
            RequestCount++;
            HasPendingRenderRequest = true;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool ConsumeRenderRequest()
        {
            var hadPendingRequest = HasPendingRenderRequest;
            HasPendingRenderRequest = false;
            return hadPendingRequest;
        }

        public void Reset()
        {
            HasPendingRenderRequest = false;
        }
    }

    private sealed class DisposedRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested
        {
            add { }
            remove { }
        }

        public bool HasPendingRenderRequest => false;

        public void RequestRender()
        {
            throw new ObjectDisposedException(nameof(DisposedRenderScheduler));
        }

        public bool ConsumeRenderRequest()
        {
            return false;
        }

        public void Reset()
        {
        }
    }

    private sealed class TestRegistration : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TestRootElement : PortableVisualLayoutStateSource
    {
        public TestRenderSize RenderSize { get; private set; }

        public void SetRenderSize(double width, double height)
        {
            RenderSize = new TestRenderSize(width, height);
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new PortableSize(RenderSize.Width, RenderSize.Height)
            };
            return true;
        }
    }

    private readonly record struct TestRenderSize(double Width, double Height);

    private static IDisposable UsePortablePopupSourceFactory(Func<IPortablePresentationSourceHost> factory)
    {
        var previousFactory = WpfPortablePopupBridge.PortablePresentationSourceFactory;
        var previousNativeHostFactory = WpfPortablePopupBridge.NativePopupHostFactory;
        WpfPortablePopupBridge.PortablePresentationSourceFactory = (_, _) => factory();
        WpfPortablePopupBridge.NativePopupHostFactory = (_, _, _, _, _) => null;
        return new DelegateDisposable(() =>
        {
            WpfPortablePopupBridge.PortablePresentationSourceFactory = previousFactory;
            WpfPortablePopupBridge.NativePopupHostFactory = previousNativeHostFactory;
        });
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private Action? _dispose;

        public DelegateDisposable(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    private sealed class FakePortableNativePopupHost : IWpfPortableNativePopupHost
    {
        public bool HasPresentedFrame => true;

        public bool HasGpuHitTestCache => true;

        public bool TryGetGpuHitTestCacheSnapshot(out ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot snapshot)
        {
            snapshot = new ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot(
                HasIndex: true,
                HasDeviceIndex: true,
                PrimitiveCount: 1,
                NodeCount: 1,
                PrimitiveIndexCount: 1,
                PathSegmentCount: 0,
                OwnerCount: 1);
            return true;
        }

        public bool TryHitTestOwners(double x, double y, Span<object?> owners, out int ownerCount)
        {
            ownerCount = 0;
            return false;
        }

        public bool TryQueryHitTestBoundsOwners(
            double minX,
            double minY,
            double maxX,
            double maxY,
            Span<object?> owners,
            out int ownerCount)
        {
            ownerCount = 0;
            return false;
        }

        public Func<WpfInputEventArgs, bool>? InputHandler { get; private set; }

        public (int X, int Y) Position { get; private set; }

        public (int Width, int Height) Size { get; private set; }

        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void SetInputHandler(Func<WpfInputEventArgs, bool> inputHandler) => InputHandler = inputHandler;

        public void RaiseInputForDiagnostics(WpfInputEventArgs input) => InputHandler?.Invoke(input);

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
        }

        public void SetPosition(int x, int y) => Position = (x, y);

        public void SetSize(int width, int height) => Size = (width, height);

        public void Show() => ShowCount++;

        public void Hide() => HideCount++;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class TestWindowActivationServiceRegistrar : IPortableWindowActivationServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;

        public int PresentationSourceInputCount { get; private set; }

        public object? LastPresentationSourceInputSource { get; private set; }

        public PortableWindowInputEvent? LastPresentationSourceInput { get; private set; }

        public void Register(PortableWindowActivationCallbacks callbacks)
        {
        }

        public bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow)
        {
            isMainWindow = false;
            return false;
        }

        public bool TryCloseWindow(object window, out PortableWindowCloseResult result)
        {
            result = PortableWindowCloseResult.NotInvoked;
            return false;
        }

        public bool TrySetActivationState(object window, bool isActive)
        {
            return false;
        }

        public bool TryBeginInvokeInput(object window, Action callback)
        {
            return false;
        }

        public bool TryProcessInputEvent(object window, PortableWindowInputEvent input)
        {
            return false;
        }

        public bool TryProcessPresentationSourceInputEvent(object presentationSource, PortableWindowInputEvent input)
        {
            PresentationSourceInputCount++;
            LastPresentationSourceInputSource = presentationSource;
            LastPresentationSourceInput = input;
            input.Handled = true;
            return true;
        }

        public bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout)
        {
            return false;
        }

        public bool TryProcessDragDropEvent(
            object window,
            int dragDropEventKind,
            string[] files,
            string? text,
            double x,
            double y,
            int allowedEffects,
            int acceptedEffect,
            out int result)
        {
            result = 0;
            return false;
        }

        public void Clear()
        {
        }
    }

    private sealed class FakePortablePresentationSource : IPortablePresentationSourceHost
    {
        private object? _rootVisual;

        public event EventHandler? RenderRequested;

        event EventHandler? IPortablePresentationSourceHost.CursorRequested
        {
            add { }
            remove { }
        }

        public object CompositionTarget { get; } = new();

        public IntPtr Handle => IntPtr.Zero;

        public object? RequestedCursor => null;

        public string? RequestedCursorName => null;

        public Func<double, double, object?>? HitTestOverride { get; set; }

        public Func<double, double, object?[]?>? HitTestAllOverride { get; set; }

        public PortableHitTestAllBufferOverride? HitTestAllBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestBoundsBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestEllipseBoundsBufferOverride { get; set; }

        public object? RootVisual
        {
            get => _rootVisual;
            set
            {
                _rootVisual = value;
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public double DpiScaleX { get; private set; } = 1.0;

        public double DpiScaleY { get; private set; } = 1.0;

        public double ClientWidth { get; private set; }

        public double ClientHeight { get; private set; }

        public double ClientOriginX { get; private set; }

        public double ClientOriginY { get; private set; }

        public int DeviceScaleChangeCount { get; private set; }

        public int ClientSizeChangeCount { get; private set; }

        public System.Collections.Generic.List<string> CallLog { get; } = new();

        public bool IsDisposed { get; private set; }

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
            DeviceScaleChangeCount++;
            CallLog.Add("DeviceScale");
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            ClientWidth = width;
            ClientHeight = height;
            ClientSizeChangeCount++;
            CallLog.Add("ClientSize");
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientOrigin(double x, double y)
        {
            ClientOriginX = x;
            ClientOriginY = y;
            CallLog.Add("ClientOrigin");
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool TryUpdateRootVisualClientSize(out double width, out double height)
        {
            width = ClientWidth;
            height = ClientHeight;
            return false;
        }

        public bool DispatchHwndSourceHook(int message, IntPtr wParam, IntPtr lParam, out IntPtr result, out bool handled)
        {
            result = IntPtr.Zero;
            handled = false;
            return false;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private static void SetPrivateField<T>(object instance, string fieldName, T value)
    {
        typeof(ProGpuWpfWindowHost)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = directory.FullName;
            foreach (var segment in pathSegments)
            {
                candidate = Path.Combine(candidate, segment);
            }

            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository path '{Path.Combine(pathSegments)}'.");
    }

    private static void RaisePlatformInput(ProGpuWpfWindowHost host, WpfInputEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformInputReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
    }
}
