using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Numerics;
using ProGPU.Wpf.Interop;
using Xunit;
using GpuHitTestIndex = ProGPU.Vector.GpuHitTestIndex;
using GpuHitTestPrimitive = ProGPU.Vector.GpuHitTestPrimitive;
using ProGpuBlurEffect = ProGPU.Scene.BlurEffect;
using ProGpuContainerVisual = ProGPU.Scene.ContainerVisual;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;
using ProGpuTexture = ProGPU.Backend.GpuTexture;
using ProGpuRetainedDrawingVisual = System.Windows.Media.ProGPU.Composition.ProGpuRetainedDrawingVisual;
using ProGpuRenderCommandType = ProGPU.Scene.RenderCommandType;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;
using WgpuTextureUsage = Silk.NET.WebGPU.TextureUsage;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuWpfDrawingFrameTests
{
    [Fact]
    public void CompositionTargetDisposeReleasesRetainedSceneOwnership()
    {
        var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var source = new object();
        var retainedVisual = new ProGpuDrawingVisual();
        var popupVisual = new ProGpuDrawingVisual();

        target.RetainedWpfVisualRoot.AddChild(retainedVisual);
        target.PopupRetainedWpfVisualRoot.AddChild(popupVisual);
        target.RetainedVisualBranchMap.Register(source, retainedVisual);
        target.GpuHitTestOwnerMap.GetOrCreateId(source);
        target.RootVisual.Context.DrawRectangle(
            null,
            null,
            new ProGPU.Scene.Rect(1, 2, 3, 4));

        target.Dispose();
        target.Dispose();

        Assert.Empty(target.RootVisual.Context.Commands);
        Assert.Empty(target.RetainedWpfVisualRoot.Children);
        Assert.Empty(target.PopupRetainedWpfVisualRoot.Children);
        Assert.Empty(target.SceneRootVisual.Children);
        Assert.Equal(0, target.RetainedVisualBranchSourceCount);
        Assert.Equal(0, target.RetainedVisualBranchCount);
        Assert.Equal(0, target.GpuHitTestOwnerMap.Count);
    }

    [Fact]
    public void FrameImageSourceAdapterReuseDoesNotAllocate()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        IWpfImageSourceAdapter? first = target.CreateFrameImageSourceAdapter(null);
        for (var warmup = 0; warmup < 10_000; warmup++)
        {
            _ = target.CreateFrameImageSourceAdapter(null);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var changedReferenceCount = 0;
        for (var iteration = 0; iteration < 1_000_000; iteration++)
        {
            if (!ReferenceEquals(first, target.CreateFrameImageSourceAdapter(null)))
            {
                changedReferenceCount++;
            }
        }

        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0, changedReferenceCount);
        Assert.Equal(0, allocatedBytes);
    }

    [Fact]
    public void ConstructorClearsRootOnceAndSetsClampedPixelSize()
    {
        var root = new ProGpuDrawingVisual();
        root.Context.DrawRectangle(null, null, new ProGPU.Scene.Rect(1, 2, 3, 4));

        var frame = new ProGpuWpfDrawingFrame(root, 0, 0);

        Assert.Equal(1u, frame.PixelWidth);
        Assert.Equal(1u, frame.PixelHeight);
        Assert.Equal(new System.Numerics.Vector2(1, 1), root.Size);
        Assert.Empty(root.Context.Commands);
    }

    [Fact]
    public void ConstructorResetsSceneRootWithRetainedWpfLayerBeforeFlatLayer()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        retainedRoot.AddChild(new ProGpuDrawingVisual());
        flatRoot.Context.DrawRectangle(null, null, new ProGPU.Scene.Rect(1, 2, 3, 4));

        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);

        Assert.Equal(200u, frame.PixelWidth);
        Assert.Equal(100u, frame.PixelHeight);
        Assert.Empty(retainedRoot.Children);
        Assert.Empty(flatRoot.Context.Commands);
        Assert.Equal(new Vector2(200, 100), sceneRoot.Size);
        Assert.Equal(new Vector2(200, 100), retainedRoot.Size);
        Assert.Equal(new Vector2(200, 100), flatRoot.Size);
        Assert.Equal(new ProGPU.Scene.Visual[] { retainedRoot, flatRoot }, sceneRoot.Children.ToArray());
    }

    [Fact]
    public void ConstructorCanPreserveRetainedWpfLayerWhenReplayIsSkipped()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var retainedChild = new ProGpuDrawingVisual();
        retainedRoot.AddChild(retainedChild);
        flatRoot.Context.DrawRectangle(null, null, new ProGPU.Scene.Rect(1, 2, 3, 4));

        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            300,
            150,
            clearRetainedWpfVisualRoot: false);

        Assert.Equal(300u, frame.PixelWidth);
        Assert.Equal(150u, frame.PixelHeight);
        Assert.Same(retainedChild, Assert.Single(retainedRoot.Children));
        Assert.Empty(flatRoot.Context.Commands);
        Assert.Equal(new Vector2(300, 150), sceneRoot.Size);
        Assert.Equal(new Vector2(300, 150), retainedRoot.Size);
        Assert.Equal(new Vector2(300, 150), flatRoot.Size);
        Assert.Equal(new ProGPU.Scene.Visual[] { retainedRoot, flatRoot }, sceneRoot.Children.ToArray());
    }

    [Fact]
    public void ConstructorResetsPopupRetainedLayerAboveMainWpfAndFlatLayers()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var popupRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var retainedChild = new ProGpuDrawingVisual();
        var stalePopupChild = new ProGpuDrawingVisual();
        retainedRoot.AddChild(retainedChild);
        popupRoot.AddChild(stalePopupChild);

        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            popupRoot,
            flatRoot,
            320,
            180,
            clearRetainedWpfVisualRoot: false);

        Assert.Same(retainedChild, Assert.Single(retainedRoot.Children));
        Assert.Empty(popupRoot.Children);
        Assert.Equal(new Vector2(320, 180), popupRoot.Size);
        Assert.Equal(new ProGPU.Scene.Visual[] { retainedRoot, flatRoot, popupRoot }, sceneRoot.Children.ToArray());
    }

    [Fact]
    public void RetainedSinkCanTargetPopupLayerWithoutTouchingMainRetainedLayer()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var popupRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            popupRoot,
            flatRoot,
            320,
            180,
            clearRetainedWpfVisualRoot: false);

        using var sink = new ProGpuRetainedCompositionCommandSink(
            frame,
            context: null,
            viewport3DTextureCache: null,
            ProGpuRetainedCompositionLayer.Popup);

        Assert.Empty(retainedRoot.Children);
        var popupFrameRoot = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(popupRoot.Children));
        Assert.Empty(popupFrameRoot.Context.Commands);
    }

    [Fact]
    public void ConstructorKeepsWpfLayerBoundsAndTransformLogicalForHighDpiFrames()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();

        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            840,
            1680,
            logicalWidth: 420,
            logicalHeight: 840,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0);

        Assert.Equal(840u, frame.PixelWidth);
        Assert.Equal(1680u, frame.PixelHeight);
        Assert.Equal(420u, frame.LogicalWidth);
        Assert.Equal(840u, frame.LogicalHeight);
        Assert.Equal(2.0, frame.DpiScaleX);
        Assert.Equal(2.0, frame.DpiScaleY);
        Assert.Equal(new Vector2(420, 840), sceneRoot.Size);
        Assert.Equal(new Vector2(420, 840), retainedRoot.Size);
        Assert.Equal(new Vector2(420, 840), flatRoot.Size);
        Assert.Equal(Matrix4x4.Identity, retainedRoot.Transform);
        Assert.Equal(Vector3.One, retainedRoot.Scale);
        Assert.Equal(Vector2.Zero, retainedRoot.RenderTransformOrigin);

        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var retainedFrameRoot = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        Assert.Equal(new Vector2(420, 840), retainedFrameRoot.Size);
    }

    [Fact]
    public unsafe void HighDpiRetainedWpfLayerRendersAcrossPhysicalFramebuffer()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        using var texture = new ProGpuTexture(
            target.Context,
            840,
            1680,
            WgpuTextureFormat.Rgba8Unorm,
            WgpuTextureUsage.RenderAttachment | WgpuTextureUsage.CopySrc,
            "WPF retained HiDPI framebuffer target");

        var frame = target.BeginDrawingFrame(
            pixelWidth: 840,
            pixelHeight: 1680,
            clearRetainedWpfVisualRoot: true,
            logicalWidth: 420,
            logicalHeight: 840,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0);

        using (var sink = new ProGpuRetainedCompositionCommandSink(
                   frame,
                   target.Context,
                   target.Viewport3DTextureCache))
        {
            sink.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0xF0, 0x20, 0x20)),
                pen: null,
                new Rect(0, 0, 420, 840));
        }

        target.Render(
            logicalWidth: 420,
            logicalHeight: 840,
            pixelWidth: 840,
            pixelHeight: 1680,
            dpiScale: 2f,
            texture.ViewPtr);

        var pixels = texture.ReadPixels();
        var lowerRight = ReadPixel(pixels, texture.Width, x: 780, y: 1560);

        Assert.True(lowerRight.R >= 220, $"Expected retained WPF content to fill the physical framebuffer width, found {lowerRight}.");
        Assert.True(lowerRight.G <= 50, $"Expected retained WPF content green channel to stay low, found {lowerRight}.");
        Assert.True(lowerRight.B <= 50, $"Expected retained WPF content blue channel to stay low, found {lowerRight}.");
        Assert.Equal(255, lowerRight.A);
    }

    [Fact]
    public unsafe void HighDpiRetainedWpfLayerPreservesLogicalMarkerOrigin()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        using var texture = new ProGpuTexture(
            target.Context,
            840,
            1680,
            WgpuTextureFormat.Rgba8Unorm,
            WgpuTextureUsage.RenderAttachment | WgpuTextureUsage.CopySrc,
            "WPF retained HiDPI logical marker target");

        var frame = target.BeginDrawingFrame(
            pixelWidth: 840,
            pixelHeight: 1680,
            clearRetainedWpfVisualRoot: true,
            logicalWidth: 420,
            logicalHeight: 840,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0);

        using (var sink = new ProGpuRetainedCompositionCommandSink(
                   frame,
                   target.Context,
                   target.Viewport3DTextureCache))
        {
            sink.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0x10, 0x70, 0x20)),
                pen: null,
                new Rect(160, 320, 80, 80));
        }

        target.Render(
            logicalWidth: 420,
            logicalHeight: 840,
            pixelWidth: 840,
            pixelHeight: 1680,
            dpiScale: 2f,
            texture.ViewPtr);

        var pixels = texture.ReadPixels();
        var logicalMarker = ReadPixel(pixels, texture.Width, x: 340, y: 660);
        var doubleScaledMarker = ReadPixel(pixels, texture.Width, x: 660, y: 1300);

        Assert.True(logicalMarker.G >= 90, $"Expected retained WPF marker at logical-origin physical position, found {logicalMarker}.");
        Assert.True(logicalMarker.R <= 40, $"Expected retained WPF marker red channel to stay low, found {logicalMarker}.");
        Assert.True(logicalMarker.B <= 50, $"Expected retained WPF marker blue channel to stay low, found {logicalMarker}.");
        Assert.Equal(255, logicalMarker.A);
        Assert.True(
            doubleScaledMarker.G <= 40,
            $"Expected retained WPF marker not to be shifted by a second DPI scale, found {doubleScaledMarker}.");
    }

    [Fact]
    public unsafe void HighDpiSourceDrawingLayerRendersAcrossPhysicalFramebuffer()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        using var texture = new ProGpuTexture(
            target.Context,
            840,
            1680,
            WgpuTextureFormat.Rgba8Unorm,
            WgpuTextureUsage.RenderAttachment | WgpuTextureUsage.CopySrc,
            "WPF source HiDPI framebuffer target");

        var frame = target.BeginDrawingFrame(
            pixelWidth: 840,
            pixelHeight: 1680,
            clearRetainedWpfVisualRoot: true,
            logicalWidth: 420,
            logicalHeight: 840,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0);

        using (var context = frame.OpenCompositionDrawingContext())
        {
            context.DrawRectangle(
                Brushes.Red,
                pen: null,
                new Rect(0, 0, 420, 840));
        }

        target.Render(
            logicalWidth: 420,
            logicalHeight: 840,
            pixelWidth: 840,
            pixelHeight: 1680,
            dpiScale: 2f,
            texture.ViewPtr);

        var pixels = texture.ReadPixels();
        var lowerRight = ReadPixel(pixels, texture.Width, x: 780, y: 1560);

        Assert.True(lowerRight.R >= 220, $"Expected source WPF content to fill the physical framebuffer width, found {lowerRight}.");
        Assert.True(lowerRight.G <= 50, $"Expected source WPF content green channel to stay low, found {lowerRight}.");
        Assert.True(lowerRight.B <= 50, $"Expected source WPF content blue channel to stay low, found {lowerRight}.");
        Assert.Equal(255, lowerRight.A);
    }

    [Fact]
    public unsafe void LegacyRenderOverloadPreservesLogicalHighDpiFrameAcrossPhysicalFramebuffer()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        using var texture = new ProGpuTexture(
            target.Context,
            840,
            1680,
            WgpuTextureFormat.Rgba8Unorm,
            WgpuTextureUsage.RenderAttachment | WgpuTextureUsage.CopySrc,
            "WPF source legacy HiDPI framebuffer target");

        var frame = target.BeginDrawingFrame(
            pixelWidth: 840,
            pixelHeight: 1680,
            clearRetainedWpfVisualRoot: true,
            logicalWidth: 420,
            logicalHeight: 840,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0);

        using (var context = frame.OpenCompositionDrawingContext())
        {
            context.DrawRectangle(
                Brushes.Red,
                pen: null,
                new Rect(0, 0, 420, 840));
        }

        target.Render(
            pixelWidth: 840,
            pixelHeight: 1680,
            texture.ViewPtr);

        var pixels = texture.ReadPixels();
        var lowerRight = ReadPixel(pixels, texture.Width, x: 780, y: 1560);

        Assert.True(lowerRight.R >= 220, $"Expected legacy render overload to fill the physical framebuffer width, found {lowerRight}.");
        Assert.True(lowerRight.G <= 50, $"Expected legacy render overload green channel to stay low, found {lowerRight}.");
        Assert.True(lowerRight.B <= 50, $"Expected legacy render overload blue channel to stay low, found {lowerRight}.");
        Assert.Equal(255, lowerRight.A);
    }

    [Fact]
    public void ConstructorClearsRetainedVisualBranchMapWhenRetainedLayerIsCleared()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        branchMap.Register(source, new ProGpuDrawingVisual());

        _ = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            new ProGpuContainerVisual(),
            new ProGpuDrawingVisual(),
            200,
            100,
            retainedVisualBranchMap: branchMap);

        Assert.Equal(0, branchMap.SourceCount);
        Assert.Equal(0, branchMap.VisualCount);
        Assert.False(branchMap.TryGetVisuals(source, out _));
    }

    [Fact]
    public void RetainedSinkMapsSourceOwnerToCurrentNativeBranch()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: branchMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var source = new object();

        ((IWpfRetainedVisualBranchSink)sink).RegisterVisualOwner(source);

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        Assert.True(branchMap.TryGetVisuals(source, out var visuals));
        Assert.Same(retainedRootVisual, Assert.Single(visuals));
        Assert.Equal(1, branchMap.SourceCount);
        Assert.Equal(1, branchMap.VisualCount);
        Assert.Same(source, branchMap.LastSource);
        Assert.Same(retainedRootVisual, branchMap.LastVisual);
    }

    [Fact]
    public void RetainedSinkMapsDependencyToCurrentNativeBranch()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            new ProGpuDrawingVisual(),
            200,
            100,
            retainedVisualBranchMap: branchMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var branchSink = (IWpfRetainedVisualBranchSink)sink;
        var source = new object();
        var dependency = new object();

        branchSink.RegisterVisualOwner(source);
        branchSink.RegisterVisualDependency(dependency);

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var target = Assert.Single(branchMap.GetReplayTargetsForSources(new[] { dependency }));
        Assert.Same(source, target.Source);
        Assert.Same(retainedRootVisual, target.Visual);
    }

    [Fact]
    public void RetainedSinkPushesSourceOwnerVisualScope()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: branchMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var branchSink = (IWpfRetainedVisualBranchSink)sink;
        var parentSource = new object();
        var childSource = new object();

        Assert.True(branchSink.PushVisualOwner(parentSource));
        sink.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
        Assert.True(branchSink.PushVisualOwner(childSource));
        sink.DrawRectangle(Brushes.Blue, null, new Rect(5, 6, 7, 8));
        branchSink.PopVisualOwner();
        branchSink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var parentVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        var childVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(parentVisual.Children));
        Assert.Empty(retainedRootVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, Assert.Single(parentVisual.Context.Commands).Type);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, Assert.Single(childVisual.Context.Commands).Type);
        Assert.True(branchMap.TryGetVisuals(parentSource, out var parentVisuals));
        Assert.Same(parentVisual, Assert.Single(parentVisuals));
        Assert.True(branchMap.TryGetVisuals(childSource, out var childVisuals));
        Assert.Same(childVisual, Assert.Single(childVisuals));

        var result = branchMap.InvalidateVisualsForSources(new[] { childSource });
        Assert.Equal(1, result.DirtySourceCount);
        Assert.Equal(1, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.Equal(0, result.SharedWithCleanSourceVisualCount);
        Assert.Equal(0, result.ReplayTargetConflictCount);
        Assert.True(result.CanTargetAllDirtySources);
    }

    [Fact]
    public void RetainedSinkClosesOwnerLocalDrawingScopesOnOwnerPop()
    {
        var retainedRoot = new ProGpuContainerVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            new ProGpuDrawingVisual(),
            200,
            100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var branchSink = (IWpfRetainedVisualBranchSink)sink;

        Assert.True(branchSink.PushVisualOwner(new object()));
        sink.PushOpacity(0.5);
        branchSink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.Equal(
            new[] { ProGpuRenderCommandType.PushOpacity, ProGpuRenderCommandType.PopOpacity },
            ownerVisual.Context.Commands.Select(command => command.Type));
    }

    [Fact]
    public void RetainedSinkAppliesNativeVisualStateToCurrentOwnerScope()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            new ProGpuDrawingVisual(),
            200,
            100,
            retainedVisualBranchMap: branchMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var branchSink = (IWpfRetainedVisualBranchSink)sink;
        var stateSink = (IWpfRetainedVisualStateSink)sink;
        var source = new object();
        var transform = Matrix4x4.CreateTranslation(3, 4, 0);

        Assert.True(branchSink.PushVisualOwner(source));
        stateSink.ApplyVisualState(new WpfRetainedVisualState(
            new Vector2(10, 20),
            transform,
            0.5f,
            new WpfReplayRect(1, 2, 30, 40),
            outerClipBounds: new WpfReplayRect(4, 5, 60, 70)));
        branchSink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.Equal(new Vector2(10, 20), ownerVisual.Offset);
        Assert.Equal(transform, ownerVisual.Transform);
        Assert.Equal(0.5f, ownerVisual.Opacity);
        var clipBounds = Assert.NotNull(ownerVisual.ClipBounds);
        Assert.Equal(1, clipBounds.X);
        Assert.Equal(2, clipBounds.Y);
        Assert.Equal(30, clipBounds.Width);
        Assert.Equal(40, clipBounds.Height);
        var outerClipBounds = Assert.NotNull(ownerVisual.OuterClipBounds);
        Assert.Equal(4, outerClipBounds.X);
        Assert.Equal(5, outerClipBounds.Y);
        Assert.Equal(60, outerClipBounds.Width);
        Assert.Equal(70, outerClipBounds.Height);
        Assert.True(branchMap.TryGetVisuals(source, out var visuals));
        Assert.Same(ownerVisual, Assert.Single(visuals));
    }

    [Fact]
    public void NativeRectangleClipRecordsTransformedPushClipCommand()
    {
        var nativeContext = new ProGPU.Scene.DrawingContext();
        using var sink = new ProGpuCompositionCommandSink(nativeContext);
        var transformSink = (IWpfNativeTransformCommandSink)sink;
        var clipSink = (IWpfNativeClipCommandSink)sink;

        transformSink.PushNativeTransform(Matrix4x4.CreateTranslation(5, 7, 0));
        clipSink.PushNativeClip(new WpfReplayRect(1, 2, 30, 40));
        sink.Pop();
        sink.Pop();

        Assert.Collection(
            nativeContext.Commands,
            push =>
            {
                Assert.Equal(ProGpuRenderCommandType.PushClip, push.Type);
                Assert.Equal(1, push.Rect.X);
                Assert.Equal(2, push.Rect.Y);
                Assert.Equal(30, push.Rect.Width);
                Assert.Equal(40, push.Rect.Height);
                Assert.Equal(5, push.Transform.M41);
                Assert.Equal(7, push.Transform.M42);
            },
            pop => Assert.Equal(ProGpuRenderCommandType.PopClip, pop.Type));
    }

    [Fact]
    public void RetainedSinkAppliesNativeOpacityMaskStateToCurrentOwnerScope()
    {
        var retainedRoot = new ProGpuContainerVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            new ProGpuDrawingVisual(),
            200,
            100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var branchSink = (IWpfRetainedVisualBranchSink)sink;
        var stateSink = (IWpfRetainedVisualStateSink)sink;

        Assert.True(branchSink.PushVisualOwner(new object()));
        stateSink.ApplyVisualState(new WpfRetainedVisualState(
            Vector2.Zero,
            Matrix4x4.Identity,
            1f,
            clipBounds: null,
            opacityMask: Brushes.White,
            opacityMaskBounds: new WpfReplayRect(1, 2, 30, 40)));
        branchSink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.NotNull(ownerVisual.OpacityMask);
        var maskBounds = Assert.NotNull(ownerVisual.OpacityMaskBounds);
        Assert.Equal(1, maskBounds.X);
        Assert.Equal(2, maskBounds.Y);
        Assert.Equal(30, maskBounds.Width);
        Assert.Equal(40, maskBounds.Height);
    }

    [Fact]
    public void RetainedSinkAppliesNativeEffectAndCacheStateToCurrentOwnerScope()
    {
        var retainedRoot = new ProGpuContainerVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            new ProGpuDrawingVisual(),
            200,
            100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var branchSink = (IWpfRetainedVisualBranchSink)sink;
        var stateSink = (IWpfRetainedVisualStateSink)sink;
        var blur = new ProGpuBlurEffect(4);

        Assert.True(branchSink.PushVisualOwner(new object()));
        stateSink.ApplyVisualState(new WpfRetainedVisualState(
            new Vector2(5, 6),
            Matrix4x4.Identity,
            1f,
            clipBounds: null,
            new Vector2(70, 80),
            blur,
            cacheAsLayer: true,
            contentBounds: new WpfReplayRect(5, 6, 70, 80)));
        branchSink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.Equal(new Vector2(5, 6), ownerVisual.Offset);
        Assert.Equal(new Vector2(70, 80), ownerVisual.Size);
        Assert.Same(blur, ownerVisual.Effect);
        Assert.True(ownerVisual.CacheAsLayer);
    }

    [Fact]
    public void RetainedSinkCreatesBoundedNativeEffectVisual()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var blur = new ProGpuBlurEffect(6);

        Assert.True(sink.PushVisualEffect(blur, new Rect(10, 20, 30, 40)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(10, 20, 5, 6));
        sink.Pop();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var effectVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.Same(blur, effectVisual.Effect);
        Assert.Equal(new Vector2(10, 20), effectVisual.Offset);
        Assert.Equal(new Vector2(30, 40), effectVisual.Size);
        var command = Assert.Single(effectVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, command.Type);
        Assert.Equal(-10, command.Transform.M41);
        Assert.Equal(-20, command.Transform.M42);
    }

    [Fact]
    public void RetainedSinkMapsOwnerToNestedEffectBranchAfterEffectPush()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: branchMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var source = new object();

        Assert.True(sink.PushVisualEffect(new ProGpuBlurEffect(4), new Rect(1, 2, 30, 40)));
        ((IWpfRetainedVisualBranchSink)sink).RegisterVisualOwner(source);

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var effectVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.True(branchMap.TryGetVisuals(source, out var visuals));
        Assert.Same(effectVisual, Assert.Single(visuals));
    }

    [Fact]
    public void BranchMapInvalidatesMappedNativeBranch()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var parent = new ProGpuContainerVisual();
        var visual = new ProGpuDrawingVisual();
        parent.AddChild(visual);
        parent.IsDirty = false;
        visual.IsDirty = false;

        branchMap.Register(source, visual);

        Assert.Equal(1, branchMap.InvalidateVisuals(source));
        Assert.True(visual.IsDirty);
        Assert.True(parent.IsDirty);
    }

    [Fact]
    public void BranchMapInvalidatesUniqueNativeBranchesForDirtySources()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var firstSource = new object();
        var secondSource = new object();
        var visual = new ProGpuDrawingVisual
        {
            IsDirty = false
        };
        branchMap.Register(firstSource, visual);
        branchMap.Register(secondSource, visual);
        var cleanVersion = visual.ChangeVersion;

        Assert.Equal(1, branchMap.InvalidateVisuals(new[] { firstSource, secondSource }));
        Assert.True(visual.IsDirty);
        Assert.Equal(cleanVersion + 1, visual.ChangeVersion);

        var result = branchMap.InvalidateVisualsForSources(new[] { firstSource, secondSource });
        Assert.Equal(2, result.DirtySourceCount);
        Assert.Equal(2, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.Equal(0, result.SharedWithCleanSourceVisualCount);
        Assert.Equal(1, result.ReplayTargetConflictCount);
        Assert.False(result.CanTargetAllDirtySources);
    }

    [Fact]
    public void BranchMapConsumesReferenceDirtySourceSetWithoutChangingReplayOrInvalidation()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var firstSource = new object();
        var secondSource = new object();
        var firstVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        var secondVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        var dirtySources = new HashSet<object>(ReferenceEqualityComparer.Instance)
        {
            firstSource,
            secondSource
        };
        branchMap.Register(firstSource, firstVisual);
        branchMap.Register(secondSource, secondVisual);

        var result = branchMap.InvalidateVisualsForSources(dirtySources);
        var targets = branchMap.GetReplayTargetsForSources(dirtySources);

        Assert.Equal(2, result.DirtySourceCount);
        Assert.Equal(2, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(2, result.InvalidatedVisualCount);
        Assert.True(result.CanTargetAllDirtySources);
        Assert.True(firstVisual.IsDirty);
        Assert.True(secondVisual.IsDirty);
        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, target => ReferenceEquals(target.Source, firstSource) && ReferenceEquals(target.Visual, firstVisual));
        Assert.Contains(targets, target => ReferenceEquals(target.Source, secondSource) && ReferenceEquals(target.Visual, secondVisual));
    }

    [Fact]
    public void BranchMapConsumesSingleReferenceDirtySourceSetThroughSingleTargetPath()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var visual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        var dirtySources = new HashSet<object>(ReferenceEqualityComparer.Instance)
        {
            source
        };
        branchMap.Register(source, visual);

        var result = branchMap.InvalidateVisualsForSources(dirtySources);
        var firstTargets = branchMap.GetReplayTargetsForSources(dirtySources);
        var secondTargets = branchMap.GetReplayTargetsForSources(dirtySources);
        var target = Assert.Single(secondTargets);

        Assert.Same(firstTargets, secondTargets);
        Assert.Equal(1, result.DirtySourceCount);
        Assert.Equal(1, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.True(result.CanTargetAllDirtySources);
        Assert.True(visual.IsDirty);
        Assert.Same(source, target.Source);
        Assert.Same(visual, target.Visual);
    }

    [Fact]
    public void BranchMapUsesSingleReferenceDirtySourceHintForReplayAndInvalidation()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var wrongHint = new object();
        var visual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        var dirtySources = new HashSet<object>(ReferenceEqualityComparer.Instance)
        {
            source
        };
        branchMap.Register(source, visual);

        var result = branchMap.InvalidateVisualsForSources(dirtySources, source);
        var hintedTargets = branchMap.GetReplayTargetsForSources(dirtySources, source);
        var hintedTarget = Assert.Single(hintedTargets);
        var fallbackTargets = branchMap.GetReplayTargetsForSources(dirtySources, wrongHint);
        var fallbackTarget = Assert.Single(fallbackTargets);

        Assert.Same(hintedTargets, fallbackTargets);
        Assert.Equal(1, result.DirtySourceCount);
        Assert.Equal(1, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.True(result.CanTargetAllDirtySources);
        Assert.True(visual.IsDirty);
        Assert.Same(source, hintedTarget.Source);
        Assert.Same(visual, hintedTarget.Visual);
        Assert.Same(source, fallbackTarget.Source);
        Assert.Same(visual, fallbackTarget.Visual);
    }

    [Fact]
    public void BranchMapIndexesListLikeDirtySourcesWithoutEnumerator()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var firstSource = new object();
        var secondSource = new object();
        var firstVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        var secondVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        var multiSourceList = new ThrowingEnumeratorSourceList(firstSource, secondSource);
        branchMap.Register(firstSource, firstVisual);
        branchMap.Register(secondSource, secondVisual);

        var result = branchMap.InvalidateVisualsForSources(multiSourceList);
        var targets = branchMap.GetReplayTargetsForSources(multiSourceList);

        Assert.Equal(2, result.DirtySourceCount);
        Assert.Equal(2, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(2, result.InvalidatedVisualCount);
        Assert.True(result.CanTargetAllDirtySources);
        Assert.True(firstVisual.IsDirty);
        Assert.True(secondVisual.IsDirty);
        Assert.Equal(2, targets.Count);
        Assert.Contains(targets, target => ReferenceEquals(target.Source, firstSource) && ReferenceEquals(target.Visual, firstVisual));
        Assert.Contains(targets, target => ReferenceEquals(target.Source, secondSource) && ReferenceEquals(target.Visual, secondVisual));
        Assert.Equal(0, multiSourceList.EnumeratorRequestCount);
        Assert.True(multiSourceList.IndexerReadCount >= 4);

        var singleSourceList = new ThrowingEnumeratorSourceList(firstSource);
        var singleResult = branchMap.InvalidateVisualsForSources(singleSourceList);
        var singleTarget = Assert.Single(branchMap.GetReplayTargetsForSources(singleSourceList));

        Assert.Equal(1, singleResult.DirtySourceCount);
        Assert.Equal(1, singleResult.MappedSourceCount);
        Assert.Equal(0, singleResult.UnmappedSourceCount);
        Assert.True(singleResult.CanTargetAllDirtySources);
        Assert.Same(firstSource, singleTarget.Source);
        Assert.Same(firstVisual, singleTarget.Visual);
        Assert.Equal(0, singleSourceList.EnumeratorRequestCount);
        Assert.True(singleSourceList.IndexerReadCount >= 2);
    }

    [Fact]
    public void BranchMapReusesSingleReplayTargetList()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var visual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(source, visual);

        var firstTargets = branchMap.GetReplayTargetsForSources(new[] { source });
        var firstTarget = Assert.Single(firstTargets);
        var secondTargets = branchMap.GetReplayTargetsForSources(new[] { source });
        var secondTarget = Assert.Single(secondTargets);

        Assert.Same(firstTargets, secondTargets);
        Assert.Same(source, firstTarget.Source);
        Assert.Same(visual, firstTarget.Visual);
        Assert.Same(source, secondTarget.Source);
        Assert.Same(visual, secondTarget.Visual);

        branchMap.Clear();

        Assert.Empty(firstTargets);
    }

    [Fact]
    public void BranchMapReusesMultiReplayTargetList()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var firstSource = new object();
        var secondSource = new object();
        var firstVisual = new ProGpuRetainedDrawingVisual();
        var secondVisual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(firstSource, firstVisual);
        branchMap.Register(secondSource, secondVisual);

        var firstTargets = branchMap.GetReplayTargetsForSources(new[] { firstSource, secondSource });
        var secondTargets = branchMap.GetReplayTargetsForSources(new[] { firstSource, secondSource });

        Assert.Same(firstTargets, secondTargets);
        Assert.Equal(2, secondTargets.Count);
        Assert.Contains(secondTargets, target => ReferenceEquals(target.Source, firstSource) && ReferenceEquals(target.Visual, firstVisual));
        Assert.Contains(secondTargets, target => ReferenceEquals(target.Source, secondSource) && ReferenceEquals(target.Visual, secondVisual));

        branchMap.Clear();

        Assert.Empty(firstTargets);
    }

    [Fact]
    public void BranchMapReportsUnmappedDirtySourcesForFallbackDecisions()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var mappedSource = new object();
        var unmappedSource = new object();
        var visual = new ProGpuDrawingVisual
        {
            IsDirty = false
        };
        branchMap.Register(mappedSource, visual);

        var result = branchMap.InvalidateVisualsForSources(new[] { mappedSource, unmappedSource, mappedSource });

        Assert.Equal(2, result.DirtySourceCount);
        Assert.Equal(1, result.MappedSourceCount);
        Assert.Equal(1, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.Equal(0, result.SharedWithCleanSourceVisualCount);
        Assert.False(result.CanTargetAllDirtySources);
        Assert.True(visual.IsDirty);
    }

    [Fact]
    public void BranchMapReportsSharedCleanOwnersForCoarseNativeBranches()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var parentSource = new object();
        var childSource = new object();
        var visual = new ProGpuDrawingVisual
        {
            IsDirty = false
        };
        branchMap.Register(parentSource, visual);
        branchMap.Register(childSource, visual);

        var result = branchMap.InvalidateVisualsForSources(new[] { childSource });

        Assert.Equal(1, result.DirtySourceCount);
        Assert.Equal(1, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.Equal(1, result.SharedWithCleanSourceVisualCount);
        Assert.False(result.CanTargetAllDirtySources);
        Assert.True(visual.IsDirty);
    }

    [Fact]
    public void BranchMapHandlesInlineAndPromotedOwnerSets()
    {
        var sourceToVisualsMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var visuals = new ProGpuRetainedDrawingVisual[5];
        for (var i = 0; i < visuals.Length; i++)
        {
            visuals[i] = new ProGpuRetainedDrawingVisual
            {
                IsDirty = false
            };
            sourceToVisualsMap.Register(source, visuals[i]);
        }

        var multiVisualResult = sourceToVisualsMap.InvalidateVisualsForSources(new[] { source });
        var multiVisualTargets = sourceToVisualsMap.GetReplayTargetsForSources(new[] { source });

        Assert.Equal(1, multiVisualResult.DirtySourceCount);
        Assert.Equal(1, multiVisualResult.MappedSourceCount);
        Assert.Equal(5, multiVisualResult.InvalidatedVisualCount);
        Assert.True(multiVisualResult.CanTargetAllDirtySources);
        Assert.Equal(5, multiVisualTargets.Count);
        for (var i = 0; i < visuals.Length; i++)
        {
            Assert.True(visuals[i].IsDirty);
            Assert.Contains(multiVisualTargets, target => ReferenceEquals(target.Source, source) && ReferenceEquals(target.Visual, visuals[i]));
        }

        var ownersToVisualMap = new WpfRetainedVisualBranchMap();
        var owners = new object[5];
        var sharedVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        for (var i = 0; i < owners.Length; i++)
        {
            owners[i] = new object();
            ownersToVisualMap.Register(owners[i], sharedVisual);
        }

        var sharedOwnerResult = ownersToVisualMap.InvalidateVisualsForSources(new[] { owners[0] });

        Assert.Equal(1, sharedOwnerResult.DirtySourceCount);
        Assert.Equal(1, sharedOwnerResult.MappedSourceCount);
        Assert.Equal(1, sharedOwnerResult.InvalidatedVisualCount);
        Assert.Equal(1, sharedOwnerResult.SharedWithCleanSourceVisualCount);
        Assert.Equal(1, sharedOwnerResult.ReplayTargetConflictCount);
        Assert.False(sharedOwnerResult.CanTargetAllDirtySources);
        Assert.True(sharedVisual.IsDirty);
        Assert.Empty(ownersToVisualMap.GetReplayTargetsForSources(new[] { owners[0] }));
    }

    [Fact]
    public void BranchMapClassifiesPromotedOwnerSetsByScanningSmallerDirtyBatch()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var owners = new object[6];
        var sharedVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        for (var i = 0; i < owners.Length; i++)
        {
            owners[i] = new object();
            branchMap.Register(owners[i], sharedVisual);
        }

        var dirtySources = new HashSet<object>(ReferenceEqualityComparer.Instance)
        {
            owners[1],
            owners[3]
        };

        var result = branchMap.InvalidateVisualsForSources(dirtySources);

        Assert.Equal(2, result.DirtySourceCount);
        Assert.Equal(2, result.MappedSourceCount);
        Assert.Equal(0, result.UnmappedSourceCount);
        Assert.Equal(1, result.InvalidatedVisualCount);
        Assert.Equal(1, result.SharedWithCleanSourceVisualCount);
        Assert.Equal(1, result.ReplayTargetConflictCount);
        Assert.False(result.CanTargetAllDirtySources);
        Assert.True(sharedVisual.IsDirty);
        Assert.Empty(branchMap.GetReplayTargetsForSources(dirtySources));
    }

    [Fact]
    public void BranchMapReturnsTopLevelReplayTargetsForDirtySources()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var parentSource = new object();
        var childSource = new object();
        var parentVisual = new ProGpuRetainedDrawingVisual();
        var childVisual = new ProGpuRetainedDrawingVisual();
        parentVisual.AddChild(childVisual);
        branchMap.Register(parentSource, parentVisual);
        branchMap.Register(childSource, childVisual);

        var targets = branchMap.GetReplayTargetsForSources(new[] { parentSource, childSource });

        var target = Assert.Single(targets);
        Assert.Same(parentSource, target.Source);
        Assert.Same(parentVisual, target.Visual);
    }

    [Fact]
    public void BranchMapRejectsReplayTargetsWhenDirtySourceSharesBranchWithCleanOwner()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var dirtySource = new object();
        var cleanSource = new object();
        var visual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(dirtySource, visual);
        branchMap.Register(cleanSource, visual);

        Assert.Empty(branchMap.GetReplayTargetsForSources(new[] { dirtySource }));
    }

    [Fact]
    public void BranchMapPromotesSharedBranchToUniqueOwnerAncestor()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var ancestorSource = new object();
        var dirtySource = new object();
        var cleanSource = new object();
        var ancestorVisual = new ProGpuRetainedDrawingVisual();
        var sharedVisual = new ProGpuRetainedDrawingVisual
        {
            IsDirty = false
        };
        ancestorVisual.AddChild(sharedVisual);
        branchMap.Register(ancestorSource, ancestorVisual);
        branchMap.Register(dirtySource, sharedVisual);
        branchMap.Register(cleanSource, sharedVisual);

        var result = branchMap.InvalidateVisualsForSources(
            new[] { dirtySource });
        var target = Assert.Single(
            branchMap.GetReplayTargetsForSources(new[] { dirtySource }));

        Assert.Equal(1, result.SharedWithCleanSourceVisualCount);
        Assert.Equal(0, result.ReplayTargetConflictCount);
        Assert.True(result.CanTargetAllDirtySources);
        Assert.True(sharedVisual.IsDirty);
        Assert.Same(ancestorSource, target.Source);
        Assert.Same(ancestorVisual, target.Visual);
    }

    [Fact]
    public void BranchMapDeduplicatesBranchesPromotedToSameAncestor()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var ancestorSource = new object();
        var firstDirtySource = new object();
        var secondDirtySource = new object();
        var firstCleanSource = new object();
        var secondCleanSource = new object();
        var ancestorVisual = new ProGpuRetainedDrawingVisual();
        var firstSharedVisual = new ProGpuRetainedDrawingVisual();
        var secondSharedVisual = new ProGpuRetainedDrawingVisual();
        ancestorVisual.AddChild(firstSharedVisual);
        ancestorVisual.AddChild(secondSharedVisual);
        branchMap.Register(ancestorSource, ancestorVisual);
        branchMap.Register(firstDirtySource, firstSharedVisual);
        branchMap.Register(firstCleanSource, firstSharedVisual);
        branchMap.Register(secondDirtySource, secondSharedVisual);
        branchMap.Register(secondCleanSource, secondSharedVisual);

        var dirtySources = new[] { firstDirtySource, secondDirtySource };
        var result = branchMap.InvalidateVisualsForSources(dirtySources);
        var target = Assert.Single(
            branchMap.GetReplayTargetsForSources(dirtySources));

        Assert.Equal(2, result.SharedWithCleanSourceVisualCount);
        Assert.Equal(0, result.ReplayTargetConflictCount);
        Assert.True(result.CanTargetAllDirtySources);
        Assert.Same(ancestorSource, target.Source);
        Assert.Same(ancestorVisual, target.Visual);
    }

    [Fact]
    public void BranchMapReturnsSourceOwnerReplayTargetForDirtyDependency()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var dependency = new object();
        var visual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(source, visual);
        branchMap.RegisterDependency(dependency, visual);

        var result = branchMap.InvalidateVisualsForSources(new[] { dependency });
        var targets = branchMap.GetReplayTargetsForSources(new[] { dependency });

        Assert.Equal(1, result.DirtySourceCount);
        Assert.Equal(1, result.MappedSourceCount);
        Assert.Equal(0, result.SharedWithCleanSourceVisualCount);
        Assert.Equal(0, result.ReplayTargetConflictCount);
        Assert.True(result.CanTargetAllDirtySources);
        var target = Assert.Single(targets);
        Assert.Same(source, target.Source);
        Assert.Same(visual, target.Visual);
    }

    [Fact]
    public void BranchMapReturnsSourceOwnerReplayTargetForDirtyChildrenCollectionDependency()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var children = new object();
        var visual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(source, visual);
        branchMap.RegisterDependency(children, visual);

        var result = branchMap.InvalidateVisualsForSources(new[] { children });
        var targets = branchMap.GetReplayTargetsForSources(new[] { children });

        Assert.True(result.CanTargetAllDirtySources);
        var target = Assert.Single(targets);
        Assert.Same(source, target.Source);
        Assert.Same(visual, target.Visual);
    }

    [Fact]
    public void BranchMapReturnsSourceOwnerReplayTargetForDirtyRenderDataDependency()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var source = new object();
        var renderData = new object();
        var visual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(source, visual);
        branchMap.RegisterDependency(renderData, visual);

        var result = branchMap.InvalidateVisualsForSources(new[] { renderData });
        var targets = branchMap.GetReplayTargetsForSources(new[] { renderData });

        Assert.True(result.CanTargetAllDirtySources);
        var target = Assert.Single(targets);
        Assert.Same(source, target.Source);
        Assert.Same(visual, target.Visual);
    }

    [Fact]
    public void BranchMapKeepsChildSourceReplayTargetWhenParentChildrenCollectionIsTracked()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var parentSource = new object();
        var childSource = new object();
        var parentChildren = new object();
        var parentVisual = new ProGpuRetainedDrawingVisual();
        var childVisual = new ProGpuRetainedDrawingVisual();
        parentVisual.AddChild(childVisual);
        branchMap.Register(parentSource, parentVisual);
        branchMap.RegisterDependency(parentChildren, parentVisual);
        branchMap.Register(childSource, childVisual);

        var targets = branchMap.GetReplayTargetsForSources(new[] { childSource });

        var target = Assert.Single(targets);
        Assert.Same(childSource, target.Source);
        Assert.Same(childVisual, target.Visual);
    }

    [Fact]
    public void BranchMapRejectsDirtyDependencyWhenBranchHasMultipleSourceOwners()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var firstSource = new object();
        var secondSource = new object();
        var dependency = new object();
        var visual = new ProGpuRetainedDrawingVisual();
        branchMap.Register(firstSource, visual);
        branchMap.Register(secondSource, visual);
        branchMap.RegisterDependency(dependency, visual);

        var result = branchMap.InvalidateVisualsForSources(new[] { dependency });

        Assert.Empty(branchMap.GetReplayTargetsForSources(new[] { dependency }));
        Assert.Equal(1, result.ReplayTargetConflictCount);
        Assert.False(result.CanTargetAllDirtySources);
    }

    [Fact]
    public void BranchMapUnregistersVisualTreeMappings()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var parentSource = new object();
        var childSource = new object();
        var parentVisual = new ProGpuRetainedDrawingVisual();
        var childVisual = new ProGpuRetainedDrawingVisual();
        parentVisual.AddChild(childVisual);
        branchMap.Register(parentSource, parentVisual);
        branchMap.Register(childSource, childVisual);
        branchMap.RegisterDependency(new object(), childVisual);

        branchMap.UnregisterVisualTree(parentVisual);

        Assert.Equal(0, branchMap.SourceCount);
        Assert.Equal(0, branchMap.VisualCount);
        Assert.Null(branchMap.LastSource);
        Assert.Null(branchMap.LastVisual);
        Assert.False(branchMap.TryGetVisuals(parentSource, out _));
        Assert.False(branchMap.TryGetVisuals(childSource, out _));
    }

    [Fact]
    public void RetainedSinkStampsSourceOwnerHitTestIdOnCommands()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var ownerMap = new WpfGpuHitTestOwnerMap();
        var sourceOwner = new object();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: new WpfRetainedVisualBranchMap(),
            hitTestOwnerMap: ownerMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);

        Assert.True(sink.PushVisualOwner(sourceOwner));
        sink.DrawRectangle(Brushes.Red, null, new Rect(5, 6, 10, 11));
        sink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        var command = Assert.Single(ownerVisual.Context.Commands);
        Assert.Equal(1, ownerVisual.HitTestId);
        Assert.Equal(1, command.HitTestId);
        Assert.True(ownerMap.TryGetOwner(command.HitTestId, out object? mappedOwner));
        Assert.Same(sourceOwner, mappedOwner);
    }

    [Fact]
    public void RetainedSinkPropagatesSourceOwnerHitTestIdToCacheScopes()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var ownerMap = new WpfGpuHitTestOwnerMap();
        var sourceOwner = new object();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: new WpfRetainedVisualBranchMap(),
            hitTestOwnerMap: ownerMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);

        Assert.True(sink.PushVisualOwner(sourceOwner));
        Assert.True(sink.PushVisualCache(new Rect(5, 6, 70, 80)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(5, 6, 10, 11));
        sink.Pop();
        sink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        var cacheVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(ownerVisual.Children));
        var command = Assert.Single(cacheVisual.Context.Commands);
        Assert.Equal(1, ownerVisual.HitTestId);
        Assert.Equal(1, cacheVisual.HitTestId);
        Assert.Equal(1, command.HitTestId);
        Assert.True(ownerMap.TryGetOwner(cacheVisual.HitTestId, out object? mappedOwner));
        Assert.Same(sourceOwner, mappedOwner);
    }

    [Fact]
    public void RetainedSinkPropagatesSourceOwnerHitTestIdToEffectScopes()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var ownerMap = new WpfGpuHitTestOwnerMap();
        var sourceOwner = new object();
        var frame = new ProGpuWpfDrawingFrame(
            sceneRoot,
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: new WpfRetainedVisualBranchMap(),
            hitTestOwnerMap: ownerMap);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);

        Assert.True(sink.PushVisualOwner(sourceOwner));
        Assert.True(sink.PushVisualEffect(new ProGpuBlurEffect(4), new Rect(5, 6, 70, 80)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(5, 6, 10, 11));
        sink.Pop();
        sink.PopVisualOwner();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        var effectVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(ownerVisual.Children));
        var command = Assert.Single(effectVisual.Context.Commands);
        Assert.Equal(1, ownerVisual.HitTestId);
        Assert.Equal(1, effectVisual.HitTestId);
        Assert.Equal(1, command.HitTestId);
        Assert.True(ownerMap.TryGetOwner(effectVisual.HitTestId, out object? mappedOwner));
        Assert.Same(sourceOwner, mappedOwner);
    }

    [Fact]
    public void TryHitTestOwnerRetriesGpuResultsWhenUnmappedTopHitHidesMappedOwner()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        var owner = new object();
        int ownerId = target.GpuHitTestOwnerMap.GetOrCreateId(owner);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(ownerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 0f),
                GpuHitTestPrimitive.RectangleFill(999, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 1f)
            ],
            maxDepth: 2,
            maxPrimitivesPerNode: 1);
        InstallGpuHitTestCache(target, index);

        bool hit = target.TryHitTestOwner(new Vector2(10f, 10f), out object? resolvedOwner, out var result);

        Assert.True(hit);
        Assert.Same(owner, resolvedOwner);
        Assert.Equal(ownerId, result.Id);
    }

    [Fact]
    public void TryHitTestOwnersRetriesGpuResultsWhenUnmappedTopHitUnderfillsOwners()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        var firstOwner = new object();
        var secondOwner = new object();
        int firstOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(firstOwner);
        int secondOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(secondOwner);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(firstOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 1f),
                GpuHitTestPrimitive.RectangleFill(secondOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 0f),
                GpuHitTestPrimitive.RectangleFill(999, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 2f)
            ],
            maxDepth: 2,
            maxPrimitivesPerNode: 1);
        InstallGpuHitTestCache(target, index);
        object?[] owners = new object?[2];

        bool hit = target.TryHitTestOwners(new Vector2(10f, 10f), owners, out int ownerCount, out var summary);

        Assert.True(hit);
        Assert.Equal(2, ownerCount);
        Assert.Equal(3u, summary.Hit);
        Assert.Same(firstOwner, owners[0]);
        Assert.Same(secondOwner, owners[1]);
    }

    [Fact]
    public void TryHitTestOwnersDeduplicatesGpuPrimitivesAndRetriesForDistinctOwners()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        var firstOwner = new object();
        var secondOwner = new object();
        int firstOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(firstOwner);
        int secondOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(secondOwner);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(firstOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 2f),
                GpuHitTestPrimitive.RectangleFill(firstOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 1f),
                GpuHitTestPrimitive.RectangleFill(secondOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 0f)
            ],
            maxDepth: 2,
            maxPrimitivesPerNode: 1);
        InstallGpuHitTestCache(target, index);
        object?[] owners = new object?[2];

        bool hit = target.TryHitTestOwners(new Vector2(10f, 10f), owners, out int ownerCount, out var summary);

        Assert.True(hit);
        Assert.Equal(2, ownerCount);
        Assert.Equal(3u, summary.Hit);
        Assert.Same(firstOwner, owners[0]);
        Assert.Same(secondOwner, owners[1]);
    }

    [Fact]
    public void TryQueryHitTestBoundsCandidatesRetriesGpuResultsWhenUnmappedTopHitUnderfillsCandidates()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        var firstOwner = new object();
        var secondOwner = new object();
        int firstOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(firstOwner);
        int secondOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(secondOwner);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(firstOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 1f),
                GpuHitTestPrimitive.RectangleFill(secondOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 0f),
                GpuHitTestPrimitive.RectangleFill(999, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 2f)
            ],
            maxDepth: 2,
            maxPrimitivesPerNode: 1);
        InstallGpuHitTestCache(target, index);
        object?[] candidates = new object?[2];

        bool hit = target.TryQueryHitTestBoundsCandidates(
            new Vector2(5f, 5f),
            new Vector2(15f, 15f),
            candidates,
            out int candidateCount,
            out var summary);

        Assert.True(hit);
        Assert.Equal(2, candidateCount);
        Assert.Equal(3u, summary.Hit);
        Assert.Same(firstOwner, Assert.IsType<PortableGeometryHitTestCandidate>(candidates[0]).VisualHit);
        Assert.Same(secondOwner, Assert.IsType<PortableGeometryHitTestCandidate>(candidates[1]).VisualHit);
    }

    [Fact]
    public void TryQueryHitTestBoundsCandidatesDeduplicatesGpuPrimitivesAndRetriesForDistinctOwners()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless(WgpuTextureFormat.Rgba8Unorm);
        var firstOwner = new object();
        var secondOwner = new object();
        int firstOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(firstOwner);
        int secondOwnerId = target.GpuHitTestOwnerMap.GetOrCreateId(secondOwner);
        var index = GpuHitTestIndex.Build(
            [
                GpuHitTestPrimitive.RectangleFill(firstOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 2f),
                GpuHitTestPrimitive.RectangleFill(firstOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 1f),
                GpuHitTestPrimitive.RectangleFill(secondOwnerId, new Vector2(0f, 0f), new Vector2(20f, 20f), Vector2.Zero, zIndex: 0f)
            ],
            maxDepth: 2,
            maxPrimitivesPerNode: 1);
        InstallGpuHitTestCache(target, index);
        object?[] candidates = new object?[2];

        bool hit = target.TryQueryHitTestBoundsCandidates(
            new Vector2(5f, 5f),
            new Vector2(15f, 15f),
            candidates,
            out int candidateCount,
            out var summary);

        Assert.True(hit);
        Assert.Equal(2, candidateCount);
        Assert.Equal(3u, summary.Hit);
        Assert.Same(firstOwner, Assert.IsType<PortableGeometryHitTestCandidate>(candidates[0]).VisualHit);
        Assert.Same(secondOwner, Assert.IsType<PortableGeometryHitTestCandidate>(candidates[1]).VisualHit);
    }

    [Fact]
    public void RetainedSinkCreatesBoundedNativeCacheVisual()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);

        Assert.True(sink.PushVisualCache(new Rect(5, 6, 70, 80)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(5, 6, 10, 11));
        sink.Pop();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var cacheVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.True(cacheVisual.CacheAsLayer);
        Assert.Null(cacheVisual.Effect);
        Assert.Equal(new Vector2(5, 6), cacheVisual.Offset);
        Assert.Equal(new Vector2(70, 80), cacheVisual.Size);
        var command = Assert.Single(cacheVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, command.Type);
        Assert.Equal(-5, command.Transform.M41);
        Assert.Equal(-6, command.Transform.M42);
    }

    [Fact]
    public void RetainedSinkCreatesBoundedNativeDrawingCacheVisual()
    {
        var sceneRoot = new ProGpuContainerVisual();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(sceneRoot, retainedRoot, flatRoot, 200, 100);
        using var sink = new ProGpuRetainedCompositionCommandSink(frame, context: null, viewport3DTextureCache: null);
        var drawingCacheSink = (IWpfDrawingCacheCommandSink)sink;

        Assert.True(drawingCacheSink.PushDrawingCache(new Rect(12, 13, 40, 50)));
        sink.DrawRectangle(Brushes.Red, null, new Rect(12, 13, 14, 15));
        sink.Pop();

        var retainedRootVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var cacheVisual = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRootVisual.Children));
        Assert.True(cacheVisual.CacheAsLayer);
        Assert.Null(cacheVisual.Effect);
        Assert.Equal(new Vector2(12, 13), cacheVisual.Offset);
        Assert.Equal(new Vector2(40, 50), cacheVisual.Size);
        var command = Assert.Single(cacheVisual.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, command.Type);
        Assert.Equal(-12, command.Transform.M41);
        Assert.Equal(-13, command.Transform.M42);
    }

    [Fact]
    public void DrawingContextFactoryAppendsMultipleWrappersToSameFrameBuffer()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 200, 100);
        var factory = frame.CreateDrawingContextFactory();
        var ownerVisual = new object();

        using (var first = factory(null))
        {
            first.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
        }

        using (var second = factory(ownerVisual))
        {
            second.DrawLine(new Pen(Brushes.Black, 1), new Point(5, 6), new Point(7, 8));
        }

        Assert.Equal(2, frame.DrawingContextCount);
        Assert.Equal(0, frame.CompositionDrawingContextCount);
        Assert.Same(ownerVisual, frame.LastOwnerVisual);
        Assert.Equal(new[]
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawLine
        }, root.Context.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public void CompositionDrawingContextFactoryAppendsToSameFrameBuffer()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 200, 100);
        var factory = frame.CreateCompositionDrawingContextFactory();

        using (var first = factory(null))
        {
            first.DrawRectangle(Brushes.Red, null, new Rect(1, 2, 3, 4));
        }

        using (var second = factory(new object()))
        {
            second.DrawLine(new Pen(Brushes.Black, 1), new Point(5, 6), new Point(7, 8));
        }

        Assert.Equal(0, frame.DrawingContextCount);
        Assert.Equal(2, frame.CompositionDrawingContextCount);
        Assert.Equal(new[]
        {
            ProGpuRenderCommandType.DrawRect,
            ProGpuRenderCommandType.DrawLine
        }, root.Context.Commands.Select(command => command.Type).ToArray());
    }

    [Fact]
    public void CompositionDrawingContextFactoryMapsOwnerToRetainedBranchWhenLayerIsRebuilt()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: branchMap);
        var ownerVisual = new object();
        var brush = Brushes.Red;
        var factory = frame.CreateCompositionDrawingContextFactory();

        using (var context = factory(ownerVisual))
        {
            context.DrawRectangle(brush, null, new Rect(1, 2, 3, 4));
        }

        Assert.Equal(0, frame.DrawingContextCount);
        Assert.Equal(1, frame.CompositionDrawingContextCount);
        Assert.Same(ownerVisual, frame.LastOwnerVisual);
        Assert.Empty(flatRoot.Context.Commands);
        var retainedFrameRoot = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerBranch = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedFrameRoot.Children));
        Assert.Empty(retainedFrameRoot.Context.Commands);
        Assert.Equal(ProGpuRenderCommandType.DrawRect, Assert.Single(ownerBranch.Context.Commands).Type);
        Assert.True(branchMap.TryGetVisuals(ownerVisual, out var ownerVisuals));
        Assert.Same(ownerBranch, Assert.Single(ownerVisuals));

        var dependencyTarget = Assert.Single(branchMap.GetReplayTargetsForSources(new object[] { brush }));
        Assert.Same(ownerVisual, dependencyTarget.Source);
        Assert.Same(ownerBranch, dependencyTarget.Visual);
    }

    [Fact]
    public void CompositionDrawingContextFactoryFallsBackToFlatLayerWhenRetainedLayerIsPreserved()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var preservedBranch = new ProGpuDrawingVisual();
        retainedRoot.AddChild(preservedBranch);
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            flatRoot,
            200,
            100,
            clearRetainedWpfVisualRoot: false,
            retainedVisualBranchMap: branchMap);
        var ownerVisual = new object();
        var factory = frame.CreateCompositionDrawingContextFactory();

        using (var context = factory(ownerVisual))
        {
            context.DrawRectangle(Brushes.Blue, null, new Rect(5, 6, 7, 8));
        }

        Assert.Equal(0, frame.DrawingContextCount);
        Assert.Equal(1, frame.CompositionDrawingContextCount);
        Assert.Same(ownerVisual, frame.LastOwnerVisual);
        Assert.Same(preservedBranch, Assert.Single(retainedRoot.Children));
        Assert.Equal(ProGpuRenderCommandType.DrawRect, Assert.Single(flatRoot.Context.Commands).Type);
        Assert.False(branchMap.TryGetVisuals(ownerVisual, out _));
    }

    [Fact]
    public void ObjectRenderDataSinkContextMapsOwnerToRetainedBranchWhenLayerIsRebuilt()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: branchMap);
        var ownerVisual = new object();
        var brush = Brushes.Green;

        using (var context = frame.OpenObjectRenderDataSinkContext(ownerVisual))
        {
            context.DrawRectangle(brush, null, new PortableRect(9, 10, 11, 12));
        }

        Assert.Equal(0, frame.DrawingContextCount);
        Assert.Equal(0, frame.CompositionDrawingContextCount);
        Assert.Equal(1, frame.ObjectRenderDataSinkContextCount);
        Assert.Empty(flatRoot.Context.Commands);
        var retainedFrameRoot = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerBranch = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedFrameRoot.Children));
        Assert.Equal(ProGpuRenderCommandType.DrawRect, Assert.Single(ownerBranch.Context.Commands).Type);
        Assert.True(branchMap.TryGetVisuals(ownerVisual, out var ownerVisuals));
        Assert.Same(ownerBranch, Assert.Single(ownerVisuals));

        var dependencyTarget = Assert.Single(branchMap.GetReplayTargetsForSources(new object[] { brush }));
        Assert.Same(ownerVisual, dependencyTarget.Source);
        Assert.Same(ownerBranch, dependencyTarget.Visual);
    }

    [Fact]
    public void ObjectRenderDataSinkContextKeepsGradientStopGraphBehindBrushDependency()
    {
        var branchMap = new WpfRetainedVisualBranchMap();
        var retainedRoot = new ProGpuContainerVisual();
        var flatRoot = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(
            new ProGpuContainerVisual(),
            retainedRoot,
            flatRoot,
            200,
            100,
            retainedVisualBranchMap: branchMap);
        var ownerVisual = new object();
        var firstStop = new GradientStop(Colors.Red, 0);
        var secondStop = new GradientStop(Colors.Blue, 1);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                firstStop,
                secondStop
            }
        };

        using (var context = frame.OpenObjectRenderDataSinkContext(ownerVisual))
        {
            context.DrawRectangle(brush, null, new PortableRect(9, 10, 11, 12));
        }

        var retainedFrameRoot = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedRoot.Children));
        var ownerBranch = Assert.IsType<ProGpuRetainedDrawingVisual>(Assert.Single(retainedFrameRoot.Children));
        AssertGradientDependencyTargetsOwner(branchMap, brush, ownerVisual, ownerBranch);
        Assert.Empty(branchMap.GetReplayTargetsForSources(new object[] { brush.GradientStops }));
        Assert.Empty(branchMap.GetReplayTargetsForSources(new object[] { firstStop }));
        Assert.Empty(branchMap.GetReplayTargetsForSources(new object[] { secondStop }));
    }

    [Fact]
    public void TryRegisterRenderDataSinkProviderPushesPortableObjectSinkFactory()
    {
        var frame = new ProGpuWpfDrawingFrame(new ProGpuDrawingVisual(), 200, 100);

        var registered = frame.TryRegisterRenderDataSinkProvider(out var registration);

        Assert.True(registered);
        Assert.NotNull(registration);
        Assert.NotNull(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);

        var sink = Assert.IsType<WpfObjectRenderDataDrawingContext>(
            PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory!(new object()));
        sink.Close();

        registration.Dispose();
        Assert.Null(PortableRenderDataDrawingContextSinkProvider.ObjectSinkFactory);
    }

    private static void AssertGradientDependencyTargetsOwner(
        WpfRetainedVisualBranchMap branchMap,
        object dependency,
        object ownerVisual,
        ProGpuRetainedDrawingVisual ownerBranch)
    {
        var result = branchMap.InvalidateVisualsForSources(new[] { dependency });
        var target = Assert.Single(branchMap.GetReplayTargetsForSources(new[] { dependency }));

        Assert.True(result.CanTargetAllDirtySources);
        Assert.Same(ownerVisual, target.Source);
        Assert.Same(ownerBranch, target.Visual);
    }

    private static void InstallGpuHitTestCache(ProGpuWpfCompositionTarget target, GpuHitTestIndex index)
    {
        typeof(global::ProGPU.Scene.Compositor)
            .GetMethod("SetLastHitTestIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(target.Compositor, new object[] { index });
    }

    private static RgbaPixel ReadPixel(byte[] pixels, uint width, int x, int y)
    {
        var index = ((y * (int)width) + x) * 4;
        return new RgbaPixel(
            pixels[index + 0],
            pixels[index + 1],
            pixels[index + 2],
            pixels[index + 3]);
    }

    private readonly record struct RgbaPixel(byte R, byte G, byte B, byte A);

    private sealed class ThrowingEnumeratorSourceList : IReadOnlyList<object>
    {
        private readonly object[] _sources;

        public ThrowingEnumeratorSourceList(params object[] sources)
        {
            _sources = sources;
        }

        public int EnumeratorRequestCount { get; private set; }

        public int IndexerReadCount { get; private set; }

        public int Count => _sources.Length;

        public object this[int index]
        {
            get
            {
                IndexerReadCount++;
                return _sources[index];
            }
        }

        public IEnumerator<object> GetEnumerator()
        {
            EnumeratorRequestCount++;
            throw new InvalidOperationException("List-like retained branch-map traversal should use indexed access.");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
