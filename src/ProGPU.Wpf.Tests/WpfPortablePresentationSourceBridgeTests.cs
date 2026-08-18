using System.Windows;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Vector;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortablePresentationSourceBridgeTests
{
    [Fact]
    public void TryBindMirrorsBridgeRootVisualIntoHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        bridge.RootVisual = root;

        Assert.Same(root, source.RootVisual);
        Assert.Same(root, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SourceRenderRequestSynchronizesHostRootVisual()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);

        source.RootVisual = root;

        Assert.Same(root, bridge!.RootVisual);
        Assert.Same(root, host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SourceDeviceScaleRequestSchedulesRenderWhenRootIsUnchanged()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);

        Assert.True(bridge!.TrySetDeviceScale(2.0, 1.5));

        Assert.Equal(2.0, source.DpiScaleX);
        Assert.Equal(1.5, source.DpiScaleY);
        Assert.Null(host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void SourceClientSizeRequestSchedulesRenderWhenRootIsUnchanged()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);

        Assert.True(bridge!.TrySetClientSize(420, 840));

        Assert.Equal(420, source.ClientWidth);
        Assert.Equal(840, source.ClientHeight);
        Assert.Null(host.WpfRootVisual);
        Assert.Equal(1, scheduler.RequestCount);
    }

    [Fact]
    public void TryBindExposesPortableSourceHandle()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource
        {
            Handle = new IntPtr(0x50575046)
        };

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.Equal(source.Handle, bridge.Handle);
    }

    [Fact]
    public void DisposeUnsubscribesFromSourceRenderRequests()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakePortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        source.RootVisual = root;
        Assert.Same(root, host.WpfRootVisual);

        bridge!.Dispose();
        source.RootVisual = new object();

        Assert.Null(host.WpfRootVisual);
        Assert.Equal(2, scheduler.RequestCount);
        Assert.False(source.IsDisposed);
    }

    [Fact]
    public void SourceCursorRequestForwardsWpfCursorToHost()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        Assert.NotNull(bridge);

        source.RequestCursor(new FakeCursor("Hand"));

        Assert.Equal(WpfCursor.Hand, host.LastPortableCursor);
    }

    [Fact]
    public void DisposeUnsubscribesFromSourceCursorRequests()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        source.RequestCursor(new FakeCursor("IBeam"));
        Assert.Equal(WpfCursor.IBeam, host.LastPortableCursor);

        bridge!.Dispose();
        source.RequestCursor(new FakeCursor("Hand"));

        Assert.Equal(WpfCursor.IBeam, host.LastPortableCursor);
    }

    [Fact]
    public void TryBindInstallsGpuHitTestOverrideWhenSourceExposesHook()
    {
        using var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.NotNull(source.HitTestOverride);
        Assert.NotNull(source.HitTestAllOverride);
        Assert.NotNull(source.HitTestAllBufferOverride);
        Assert.NotNull(source.HitTestBoundsOverride);
        Assert.NotNull(source.HitTestBoundsBufferOverride);
        Assert.NotNull(source.HitTestEllipseBoundsOverride);
        Assert.NotNull(source.HitTestEllipseBoundsBufferOverride);
        Assert.Null(source.HitTestOverride(12, 24));
        Assert.Null(source.HitTestAllOverride(12, 24));
        Assert.False(source.HitTestAllBufferOverride!(12, 24, new object?[4], out var bufferCount));
        Assert.Equal(0, bufferCount);
        Assert.Null(source.HitTestBoundsOverride(0, 0, 12, 24));
        Assert.False(source.HitTestBoundsBufferOverride!(0, 0, 12, 24, new object?[4], out var boundsBufferCount));
        Assert.Equal(0, boundsBufferCount);
        Assert.Null(source.HitTestEllipseBoundsOverride(0, 0, 12, 24));
        Assert.False(source.HitTestEllipseBoundsBufferOverride!(0, 0, 12, 24, new object?[4], out var ellipseBufferCount));
        Assert.Equal(0, ellipseBufferCount);

        bridge!.Dispose();

        Assert.Null(source.HitTestOverride);
        Assert.Null(source.HitTestAllOverride);
        Assert.Null(source.HitTestAllBufferOverride);
        Assert.Null(source.HitTestBoundsOverride);
        Assert.Null(source.HitTestBoundsBufferOverride);
        Assert.Null(source.HitTestEllipseBoundsOverride);
        Assert.Null(source.HitTestEllipseBoundsBufferOverride);
    }

    [Fact]
    public void GpuHitTestOverridesReturnHandledMissWhenCacheExists()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var source = new FakePortablePresentationSource();
        InstallCompositionTarget(host, target);
        InstallEmptyGpuHitTestCache(target);

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.NotNull(source.HitTestOverride);
        Assert.NotNull(source.HitTestAllOverride);
        Assert.NotNull(source.HitTestAllBufferOverride);
        Assert.NotNull(source.HitTestBoundsOverride);
        Assert.NotNull(source.HitTestBoundsBufferOverride);
        Assert.NotNull(source.HitTestEllipseBoundsOverride);
        Assert.NotNull(source.HitTestEllipseBoundsBufferOverride);
        Assert.Same(source, source.HitTestOverride(12, 24));
        Assert.Empty(source.HitTestAllOverride(12, 24)!);
        Assert.True(source.HitTestAllBufferOverride!(12, 24, new object?[4], out var bufferCount));
        Assert.Equal(0, bufferCount);
        Assert.Empty(source.HitTestBoundsOverride(0, 0, 12, 24)!);
        Assert.True(source.HitTestBoundsBufferOverride!(0, 0, 12, 24, new object?[4], out var boundsBufferCount));
        Assert.Equal(0, boundsBufferCount);
        Assert.Empty(source.HitTestEllipseBoundsOverride(0, 0, 12, 24)!);
        Assert.True(source.HitTestEllipseBoundsBufferOverride!(0, 0, 12, 24, new object?[4], out var ellipseBufferCount));
        Assert.Equal(0, ellipseBufferCount);
    }

    [Fact]
    public void GpuHitTestPointOverrideTreatsTransparentGpuOwnerAsHandledMissWithoutSingleOwnerRetry()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var source = new FakePortablePresentationSource();
        var transparentOwner = new FakeVisualOwner(
            PortableVisualOwnerKind.TransparentPointerOverlay);
        int transparentOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(transparentOwner);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(
                    transparentOwnerId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 0f)
            ]);
        InstallCompositionTarget(host, target);
        InstallGpuHitTestCache(target, index);

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.NotNull(source.HitTestOverride);
        Assert.Same(source, source.HitTestOverride(10, 10));
        Assert.Empty(source.HitTestAllOverride!(10, 10)!);
        Assert.True(source.HitTestAllBufferOverride!(10, 10, new object?[4], out var bufferCount));
        Assert.Equal(0, bufferCount);
    }

    [Fact]
    public void GpuHitTestPointOverridePreservesTopmostSiblingOrderOverVisualDepth()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var source = new FakePortablePresentationSource();
        var window = new FakeVisualOwner(PortableVisualOwnerKind.Window);
        var comboBox = new FakeVisualOwner(PortableVisualOwnerKind.Content, window);
        var layout = new FakeVisualOwner(PortableVisualOwnerKind.PointerInfrastructure, comboBox);
        var toggleButton = new FakeVisualOwner(PortableVisualOwnerKind.Content, layout);
        var editableTextBox = new FakeVisualOwner(PortableVisualOwnerKind.Content, layout);
        var textView = new FakeVisualOwner(PortableVisualOwnerKind.Content, editableTextBox);
        int toggleButtonId = target.GpuHitTestOwnerMap.GetOrCreateId(toggleButton);
        int textViewId = target.GpuHitTestOwnerMap.GetOrCreateId(textView);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(
                    toggleButtonId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 2f),
                GpuHitTestPrimitive.RectangleFill(
                    textViewId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 1f)
            ]);
        InstallCompositionTarget(host, target);
        InstallGpuHitTestCache(target, index);

        Assert.True(WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge));
        Assert.NotNull(bridge);

        Assert.Same(toggleButton, source.HitTestOverride!(10, 10));
        Assert.Equal([toggleButton, textView], source.HitTestAllOverride!(10, 10));
    }

    [Fact]
    public void GpuHitTestPointOverridePrefersDescendantOverEarlierAncestorHits()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var source = new FakePortablePresentationSource();
        var window = new FakeVisualOwner(PortableVisualOwnerKind.Window);
        var dockManager = new FakeVisualOwner(PortableVisualOwnerKind.Content, window);
        var autoHideArea = new FakeVisualOwner(PortableVisualOwnerKind.PointerInfrastructure, dockManager);
        var filterTextBox = new FakeVisualOwner(PortableVisualOwnerKind.Content, dockManager);
        var textView = new FakeVisualOwner(PortableVisualOwnerKind.Content, filterTextBox);
        int dockManagerId = target.GpuHitTestOwnerMap.GetOrCreateId(dockManager);
        int autoHideAreaId = target.GpuHitTestOwnerMap.GetOrCreateId(autoHideArea);
        int textViewId = target.GpuHitTestOwnerMap.GetOrCreateId(textView);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(
                    dockManagerId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 3f),
                GpuHitTestPrimitive.RectangleFill(
                    autoHideAreaId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 2f),
                GpuHitTestPrimitive.RectangleFill(
                    textViewId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 1f)
            ]);
        InstallCompositionTarget(host, target);
        InstallGpuHitTestCache(target, index);

        Assert.True(WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge));
        Assert.NotNull(bridge);

        Assert.Same(textView, source.HitTestOverride!(10, 10));
        Assert.Equal(
            [dockManager, autoHideArea, textView],
            source.HitTestAllOverride!(10, 10));
    }

    [Fact]
    public void GpuHitTestPointOverrideSkipsInputDisabledOverlayBeforeTopmostEnabledSibling()
    {
        using var host = new ProGpuWpfWindowHost();
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var source = new FakePortablePresentationSource();
        var window = new FakeVisualOwner(PortableVisualOwnerKind.Window);
        var comboBox = new FakeVisualOwner(PortableVisualOwnerKind.Content, window);
        var layout = new FakeVisualOwner(PortableVisualOwnerKind.PointerInfrastructure, comboBox);
        var disabledChevron = new FakeVisualOwner(
            PortableVisualOwnerKind.Content,
            layout,
            isInputEnabled: false);
        var toggleButton = new FakeVisualOwner(PortableVisualOwnerKind.Content, layout);
        var editableTextBox = new FakeVisualOwner(PortableVisualOwnerKind.Content, layout);
        var textView = new FakeVisualOwner(PortableVisualOwnerKind.Content, editableTextBox);
        int disabledChevronId = target.GpuHitTestOwnerMap.GetOrCreateId(disabledChevron);
        int toggleButtonId = target.GpuHitTestOwnerMap.GetOrCreateId(toggleButton);
        int textViewId = target.GpuHitTestOwnerMap.GetOrCreateId(textView);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(
                    disabledChevronId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 3f),
                GpuHitTestPrimitive.RectangleFill(
                    toggleButtonId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 2f),
                GpuHitTestPrimitive.RectangleFill(
                    textViewId,
                    new System.Numerics.Vector2(0f, 0f),
                    new System.Numerics.Vector2(20f, 20f),
                    System.Numerics.Vector2.Zero,
                    zIndex: 1f)
            ]);
        InstallCompositionTarget(host, target);
        InstallGpuHitTestCache(target, index);

        Assert.True(WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge));
        Assert.NotNull(bridge);

        Assert.Same(toggleButton, source.HitTestOverride!(10, 10));
        Assert.Equal(
            [disabledChevron, toggleButton, textView],
            source.HitTestAllOverride!(10, 10));
    }

    [Fact]
    public void PopupScopeFilterKeepsOnlyVisualOwnersFromItsPresentationSourceRoot()
    {
        var root = new FakeVisualOwner(PortableVisualOwnerKind.PointerInfrastructure);
        var child = new FakeVisualOwner(PortableVisualOwnerKind.Content, root);
        var grandchild = new FakeVisualOwner(PortableVisualOwnerKind.Content, child);
        var unrelatedRoot = new FakeVisualOwner(PortableVisualOwnerKind.PointerInfrastructure);
        var unrelatedChild = new FakeVisualOwner(PortableVisualOwnerKind.Content, unrelatedRoot);
        object?[] candidates =
        [
            unrelatedChild,
            child,
            new PortableGeometryHitTestCandidate(grandchild, intersectionDetail: 2),
            null
        ];

        int count = WpfPortablePresentationSourceBridge.FilterVisualOwnerSubtree(candidates, root);

        Assert.Equal(2, count);
        Assert.Same(child, candidates[0]);
        var geometryCandidate = Assert.IsType<PortableGeometryHitTestCandidate>(candidates[1]);
        Assert.Same(grandchild, geometryCandidate.VisualHit);
        Assert.Null(candidates[2]);
        Assert.Null(candidates[3]);
    }

    [Fact]
    public void CapturedGpuHitTestCallbacksFailClosedAfterHostDisposal()
    {
        var host = new ProGpuWpfWindowHost();
        var source = new FakePortablePresentationSource();
        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);
        Assert.True(bound);
        Assert.NotNull(bridge);

        Func<double, double, object?> hitTest = source.HitTestOverride!;
        Func<double, double, object?[]?> hitTestAll = source.HitTestAllOverride!;
        PortableHitTestAllBufferOverride hitTestAllBuffer = source.HitTestAllBufferOverride!;
        Func<double, double, double, double, object?[]?> hitTestBounds = source.HitTestBoundsOverride!;
        PortableGeometryHitTestBufferOverride hitTestBoundsBuffer = source.HitTestBoundsBufferOverride!;
        Func<double, double, double, double, object?[]?> hitTestEllipse = source.HitTestEllipseBoundsOverride!;
        PortableGeometryHitTestBufferOverride hitTestEllipseBuffer = source.HitTestEllipseBoundsBufferOverride!;

        host.Dispose();

        Assert.Null(hitTest(12, 24));
        Assert.Null(hitTestAll(12, 24));
        Assert.False(hitTestAllBuffer(12, 24, new object?[4], out var allBufferCount));
        Assert.Equal(0, allBufferCount);
        Assert.Null(hitTestBounds(0, 0, 12, 24));
        Assert.False(hitTestBoundsBuffer(0, 0, 12, 24, new object?[4], out var boundsBufferCount));
        Assert.Equal(0, boundsBufferCount);
        Assert.Null(hitTestEllipse(0, 0, 12, 24));
        Assert.False(hitTestEllipseBuffer(0, 0, 12, 24, new object?[4], out var ellipseBufferCount));
        Assert.Equal(0, ellipseBufferCount);
    }

    [Fact]
    public void TryBindReturnsFalseWhenSourceShapeIsMissing()
    {
        using var host = new ProGpuWpfWindowHost();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, new object(), out var bridge);

        Assert.False(bound);
        Assert.Null(bridge);
    }

    [Fact]
    public void TryBindUsesTypedPortableSourceContractWithoutReflectiveShape()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var source = new FakeTypedPortablePresentationSource();
        var root = new object();

        var bound = WpfPortablePresentationSourceBridge.TryBind(host, source, out var bridge);

        Assert.True(bound);
        Assert.NotNull(bridge);
        Assert.Same(source.CompositionTargetValue, bridge!.CompositionTarget);
        Assert.Equal(source.HandleValue, bridge.Handle);
        Assert.NotNull(source.HitTestOverrideValue);
        Assert.NotNull(source.HitTestAllOverrideValue);
        Assert.NotNull(source.HitTestAllBufferOverrideValue);
        Assert.NotNull(source.HitTestBoundsOverrideValue);
        Assert.NotNull(source.HitTestBoundsBufferOverrideValue);
        Assert.NotNull(source.HitTestEllipseBoundsOverrideValue);
        Assert.NotNull(source.HitTestEllipseBoundsBufferOverrideValue);
        Assert.Null(source.HitTestOverrideValue!(12, 24));
        Assert.Null(source.HitTestAllOverrideValue!(12, 24));
        Assert.False(source.HitTestAllBufferOverrideValue!(12, 24, new object?[4], out var bufferCount));
        Assert.Equal(0, bufferCount);
        Assert.Null(source.HitTestBoundsOverrideValue!(0, 0, 12, 24));
        Assert.False(source.HitTestBoundsBufferOverrideValue!(0, 0, 12, 24, new object?[4], out var boundsBufferCount));
        Assert.Equal(0, boundsBufferCount);
        Assert.Null(source.HitTestEllipseBoundsOverrideValue!(0, 0, 12, 24));
        Assert.False(source.HitTestEllipseBoundsBufferOverrideValue!(0, 0, 12, 24, new object?[4], out var ellipseBufferCount));
        Assert.Equal(0, ellipseBufferCount);

        bridge.RootVisual = root;
        Assert.Same(root, source.RootVisualValue);
        Assert.Same(root, host.WpfRootVisual);
        Assert.True(bridge.TrySetDeviceScale(2.0, 1.5));
        Assert.True(bridge.TrySetClientSize(420, 840));
        Assert.Equal((2.0, 1.5), source.DeviceScale);
        Assert.Equal((420d, 840d), source.ClientSize);

        source.RequestCursor(new FakeCursor("Wait"));
        Assert.Equal(WpfCursor.Wait, host.LastPortableCursor);

        bridge.Dispose();

        Assert.Null(source.HitTestOverrideValue);
        Assert.Null(source.HitTestAllOverrideValue);
        Assert.Null(source.HitTestAllBufferOverrideValue);
        Assert.Null(source.HitTestBoundsOverrideValue);
        Assert.Null(source.HitTestBoundsBufferOverrideValue);
        Assert.Null(source.HitTestEllipseBoundsOverrideValue);
        Assert.Null(source.HitTestEllipseBoundsBufferOverrideValue);
        Assert.False(source.IsDisposed);
    }

    private static void InstallCompositionTarget(ProGpuWpfWindowHost host, ProGpuWpfCompositionTarget target)
    {
        typeof(ProGpuWpfWindowHost)
            .GetField("_target", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(host, target);
    }

    private static void InstallEmptyGpuHitTestCache(ProGpuWpfCompositionTarget target)
    {
        var index = GpuHitTestIndex.Build(Array.Empty<GpuHitTestPrimitive>());
        InstallGpuHitTestCache(target, index);
    }

    private static void InstallGpuHitTestCache(ProGpuWpfCompositionTarget target, GpuHitTestIndex index)
    {
        typeof(global::ProGPU.Scene.Compositor)
            .GetMethod("SetLastHitTestIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(target.Compositor, new object[] { index });
    }

    private sealed class FakePortablePresentationSource : IPortablePresentationSourceHost
    {
        private object? _rootVisual;

        public event EventHandler? RenderRequested;

        public event EventHandler? CursorRequested;

        public object CompositionTarget { get; } = new();

        public IntPtr Handle { get; init; }

        public object? RequestedCursor { get; private set; }

        public string? RequestedCursorName => RequestedCursor?.ToString();

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

        public bool IsDisposed { get; private set; }

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            DpiScaleX = dpiScaleX;
            DpiScaleY = dpiScaleY;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            ClientWidth = width;
            ClientHeight = height;
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

        internal void RequestCursor(object cursor)
        {
            RequestedCursor = cursor;
            CursorRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeVisualOwner(
        PortableVisualOwnerKind ownerKind,
        object? parent = null,
        bool isInputEnabled = true) : IPortableVisualOwnerHost
    {
        public object PortableVisualParent { get; } = parent ?? null!;

        public bool IsPortableInputEnabled { get; } = isInputEnabled;

        public PortableVisualOwnerKind PortableVisualOwnerKind { get; } = ownerKind;
    }

    private sealed class FakeCursor
    {
        public FakeCursor(string cursorType)
        {
            CursorType = cursorType;
        }

        public string CursorType { get; }

        public override string ToString()
        {
            return CursorType;
        }
    }

    private sealed class FakeTypedPortablePresentationSource : IPortablePresentationSourceHost
    {
        private object? _rootVisual;
        private object? _requestedCursor;
        private Func<double, double, object?>? _hitTestOverride;
        private Func<double, double, object?[]?>? _hitTestAllOverride;
        private PortableHitTestAllBufferOverride? _hitTestAllBufferOverride;
        private Func<double, double, double, double, object?[]?>? _hitTestBoundsOverride;
        private PortableGeometryHitTestBufferOverride? _hitTestBoundsBufferOverride;
        private Func<double, double, double, double, object?[]?>? _hitTestEllipseBoundsOverride;
        private PortableGeometryHitTestBufferOverride? _hitTestEllipseBoundsBufferOverride;

        public event EventHandler? RenderRequested;

        public event EventHandler? CursorRequested;

        public object CompositionTargetValue { get; } = new();

        public IntPtr HandleValue { get; } = new(0x50575047);

        public object? RootVisualValue => _rootVisual;

        public (double X, double Y) DeviceScale { get; private set; } = (1.0, 1.0);

        public (double Width, double Height) ClientSize { get; private set; }

        public Func<double, double, object?>? HitTestOverrideValue => _hitTestOverride;

        public Func<double, double, object?[]?>? HitTestAllOverrideValue => _hitTestAllOverride;

        public PortableHitTestAllBufferOverride? HitTestAllBufferOverrideValue => _hitTestAllBufferOverride;

        public Func<double, double, double, double, object?[]?>? HitTestBoundsOverrideValue => _hitTestBoundsOverride;

        public PortableGeometryHitTestBufferOverride? HitTestBoundsBufferOverrideValue => _hitTestBoundsBufferOverride;

        public Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverrideValue => _hitTestEllipseBoundsOverride;

        public PortableGeometryHitTestBufferOverride? HitTestEllipseBoundsBufferOverrideValue => _hitTestEllipseBoundsBufferOverride;

        public bool IsDisposed { get; private set; }

        object? IPortablePresentationSourceHost.RootVisual
        {
            get => _rootVisual;
            set
            {
                _rootVisual = value;
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        object? IPortablePresentationSourceHost.CompositionTarget => CompositionTargetValue;

        IntPtr IPortablePresentationSourceHost.Handle => HandleValue;

        object? IPortablePresentationSourceHost.RequestedCursor => _requestedCursor;

        string? IPortablePresentationSourceHost.RequestedCursorName => _requestedCursor?.ToString();

        Func<double, double, object?>? IPortablePresentationSourceHost.HitTestOverride
        {
            get => _hitTestOverride;
            set => _hitTestOverride = value;
        }

        Func<double, double, object?[]?>? IPortablePresentationSourceHost.HitTestAllOverride
        {
            get => _hitTestAllOverride;
            set => _hitTestAllOverride = value;
        }

        PortableHitTestAllBufferOverride? IPortablePresentationSourceHost.HitTestAllBufferOverride
        {
            get => _hitTestAllBufferOverride;
            set => _hitTestAllBufferOverride = value;
        }

        Func<double, double, double, double, object?[]?>? IPortablePresentationSourceHost.HitTestBoundsOverride
        {
            get => _hitTestBoundsOverride;
            set => _hitTestBoundsOverride = value;
        }

        PortableGeometryHitTestBufferOverride? IPortablePresentationSourceHost.HitTestBoundsBufferOverride
        {
            get => _hitTestBoundsBufferOverride;
            set => _hitTestBoundsBufferOverride = value;
        }

        Func<double, double, double, double, object?[]?>? IPortablePresentationSourceHost.HitTestEllipseBoundsOverride
        {
            get => _hitTestEllipseBoundsOverride;
            set => _hitTestEllipseBoundsOverride = value;
        }

        PortableGeometryHitTestBufferOverride? IPortablePresentationSourceHost.HitTestEllipseBoundsBufferOverride
        {
            get => _hitTestEllipseBoundsBufferOverride;
            set => _hitTestEllipseBoundsBufferOverride = value;
        }

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            DeviceScale = (dpiScaleX, dpiScaleY);
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            ClientSize = (width, height);
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        bool IPortablePresentationSourceHost.TryUpdateRootVisualClientSize(out double width, out double height)
        {
            width = ClientSize.Width;
            height = ClientSize.Height;
            return false;
        }

        bool IPortablePresentationSourceHost.DispatchHwndSourceHook(int message, IntPtr wParam, IntPtr lParam, out IntPtr result, out bool handled)
        {
            result = IntPtr.Zero;
            handled = false;
            return false;
        }

        public void RequestCursor(object cursor)
        {
            _requestedCursor = cursor;
            CursorRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TestRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested;

        public int RequestCount { get; private set; }

        public bool HasPendingRenderRequest { get; private set; }

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
}
