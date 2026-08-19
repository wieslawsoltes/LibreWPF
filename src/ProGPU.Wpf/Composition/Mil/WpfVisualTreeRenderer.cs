using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Wpf.Interop;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualBounds = ProGPU.Wpf.Interop.PortableVisualBounds;
using PortableVisualBoundsSource = ProGPU.Wpf.Interop.IPortableVisualBoundsSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortablePopupRootSource = ProGPU.Wpf.Interop.IPortablePopupRootSource;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualTreeRenderer
{
    private const string TraceRetainedVisualsEnvironmentVariable = "PROGPU_WPF_TRACE_RETAINED_VISUALS";
    internal const int VisualReplayCacheEntryLimit = 8192;

    private static readonly bool s_traceRetainedVisuals = IsRetainedVisualTraceEnabled();
    private static readonly ConditionalWeakTable<object, VisualGuidelineSetCache> s_visualGuidelineSetCache = new();

    [ThreadStatic]
    private static Dictionary<object, VisualReplayStateCacheEntry>? s_visualReplayCache;

    [ThreadStatic]
    private static int s_visualStateReplayCacheDepth;

    private enum RetainedOwnerScopeMode
    {
        None,
        LightweightOnly,
        Full
    }

    private readonly WpfRenderDataBridge _renderDataBridge;

    public WpfVisualTreeRenderer()
        : this(new WpfRenderDataBridge())
    {
    }

    public WpfVisualTreeRenderer(WpfRenderDataBridge renderDataBridge)
    {
        _renderDataBridge = renderDataBridge ?? throw new ArgumentNullException(nameof(renderDataBridge));
    }

    public WpfVisualReplayResult ReplaySubtree(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources = null,
        IWpfImageSourceAdapter? imageSourceAdapter = null,
        bool includePortablePopupRoots = false)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        using var visualStateCacheScope = BeginVisualStateReplayCache();
        var stats = new ReplayStats();
        ReplaySubtreeCore(rootVisual, sink, resources, imageSourceAdapter, stats, RetainedOwnerScopeMode.Full, includePortablePopupRoots);
        return stats.ToResult();
    }

    internal bool CanReplaySubtreeIntoCurrentRetainedVisual(
        object rootVisual,
        IWpfImageSourceAdapter? imageSourceAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        using var visualStateCacheScope = BeginVisualStateReplayCache();
        return TryCreateRetainedVisualState(rootVisual, imageSourceAdapter, out _);
    }

    internal bool TryReplaySubtreeIntoCurrentRetainedVisual(
        object rootVisual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfVisualReplayResult result)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        ArgumentNullException.ThrowIfNull(sink);

        using var visualStateCacheScope = BeginVisualStateReplayCache();
        var stats = new ReplayStats();
        if (!TryReplaySubtreeIntoCurrentRetainedVisualCore(rootVisual, sink, resources, imageSourceAdapter, stats, includePortablePopupRoots: false))
        {
            result = default;
            return false;
        }

        result = stats.ToResult();
        return true;
    }

    private void ReplaySubtreeCore(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats,
        RetainedOwnerScopeMode retainedOwnerScopeMode,
        bool includePortablePopupRoots)
    {
        if (ShouldSkipPortablePopupRoot(visual, includePortablePopupRoots))
        {
            return;
        }

        stats.VisualCount++;

        if (retainedOwnerScopeMode == RetainedOwnerScopeMode.Full
            && TryReplaySubtreeWithRetainedVisualOwner(visual, sink, resources, imageSourceAdapter, stats, includePortablePopupRoots))
        {
            return;
        }

        var hitTestOwnerPushed = retainedOwnerScopeMode != RetainedOwnerScopeMode.None &&
            TryPushHitTestOwner(visual, sink);
        try
        {
            var popCount = PushVisualState(visual, sink, imageSourceAdapter, stats);
            try
            {
                RegisterRetainedVisualOwner(visual, sink);
                RegisterRetainedVisualStateDependencies(visual, sink);

                if (!ReplayViewport3DVisual(visual, sink, stats))
                {
                    ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

                    var childScopeMode = hitTestOwnerPushed
                        ? RetainedOwnerScopeMode.LightweightOnly
                        : RetainedOwnerScopeMode.None;
                    ReplayVisualChildren(visual, sink, resources, imageSourceAdapter, stats, childScopeMode, includePortablePopupRoots);
                }
            }
            finally
            {
                for (var i = 0; i < popCount; i++)
                {
                    sink.Pop();
                }
            }
        }
        finally
        {
            if (hitTestOwnerPushed)
            {
                PopHitTestOwner(sink);
            }
        }
    }

    private static bool TryPushHitTestOwner(object visual, IWpfCompositionCommandSink sink)
    {
        return sink is IWpfHitTestOwnerScopeCommandSink hitTestOwnerScopeSink &&
            hitTestOwnerScopeSink.PushHitTestOwner(visual);
    }

    private static void PopHitTestOwner(IWpfCompositionCommandSink sink)
    {
        ((IWpfHitTestOwnerScopeCommandSink)sink).PopHitTestOwner();
    }

    private bool TryReplaySubtreeIntoCurrentRetainedVisualCore(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats,
        bool includePortablePopupRoots)
    {
        if (ShouldSkipPortablePopupRoot(visual, includePortablePopupRoots))
        {
            return false;
        }

        stats.VisualCount++;

        if (sink is not IWpfRetainedVisualBranchSink retainedVisualBranchSink
            || sink is not IWpfRetainedVisualStateSink retainedVisualStateSink
            || !TryCreateRetainedVisualState(visual, imageSourceAdapter, out var visualState))
        {
            return false;
        }

        retainedVisualBranchSink.RegisterVisualOwner(visual);
        RegisterRetainedVisualStateDependencies(visual, sink);
        retainedVisualStateSink.ApplyVisualState(visualState);

        var contentTransformPopCount = 0;
        try
        {
            contentTransformPopCount = PushRetainedVisualStateContentTransform(visualState, sink);

            if (!ReplayViewport3DVisual(visual, sink, stats))
            {
                ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

                ReplayVisualChildren(
                    visual,
                    sink,
                    resources,
                    imageSourceAdapter,
                    stats,
                    RetainedOwnerScopeMode.Full,
                    includePortablePopupRoots);
            }
        }
        finally
        {
            PopRetainedVisualStateContentTransform(contentTransformPopCount, sink);
        }

        return true;
    }

    private bool TryReplaySubtreeWithRetainedVisualOwner(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats,
        bool includePortablePopupRoots)
    {
        if (ShouldSkipPortablePopupRoot(visual, includePortablePopupRoots))
        {
            return false;
        }

        if (sink is not IWpfRetainedVisualBranchSink retainedVisualBranchSink
            || sink is not IWpfRetainedVisualStateSink retainedVisualStateSink
            || !TryCreateRetainedVisualState(visual, imageSourceAdapter, out var visualState))
        {
            TraceRetainedVisualOwnerState(visual, "unsupported");
            return false;
        }

        if (!retainedVisualBranchSink.PushVisualOwner(visual))
        {
            TraceRetainedVisualOwnerState(visual, "push-failed");
            return false;
        }

        TraceRetainedVisualOwnerState(visual, "retained");
        var replayed = false;
        try
        {
            RegisterRetainedVisualStateDependencies(visual, sink);
            retainedVisualStateSink.ApplyVisualState(visualState);

            var contentTransformPopCount = 0;
            try
            {
                contentTransformPopCount = PushRetainedVisualStateContentTransform(visualState, sink);

                if (!ReplayViewport3DVisual(visual, sink, stats))
                {
                    ReplayVisualContent(visual, sink, resources, imageSourceAdapter, stats);

                    ReplayVisualChildren(
                        visual,
                        sink,
                        resources,
                        imageSourceAdapter,
                        stats,
                        RetainedOwnerScopeMode.Full,
                        includePortablePopupRoots);
                }
            }
            finally
            {
                PopRetainedVisualStateContentTransform(contentTransformPopCount, sink);
            }

            replayed = true;
            return true;
        }
        finally
        {
            retainedVisualBranchSink.PopVisualOwner();
            if (!replayed)
            {
                stats.UnsupportedVisualStateCount++;
            }
        }
    }

    private void ReplayVisualChildren(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats,
        RetainedOwnerScopeMode retainedOwnerScopeMode,
        bool includePortablePopupRoots)
    {
        if (!TryGetPortableVisualChildren(visual, out var childrenSource, out var childCount))
        {
            return;
        }

        for (var i = 0; i < childCount; i++)
        {
            if (!childrenSource.TryGetPortableVisualChild(i, out var child) || child == null)
            {
                continue;
            }

            stats.ChildEdgeCount++;
            ReplaySubtreeCore(child, sink, resources, imageSourceAdapter, stats, retainedOwnerScopeMode, includePortablePopupRoots);
        }
    }

    private static bool ShouldSkipPortablePopupRoot(object visual, bool includePortablePopupRoots)
    {
        return !includePortablePopupRoots &&
            visual is PortablePopupRootSource popupRootSource &&
            popupRootSource.IsPortablePopupRoot;
    }

    private static void RegisterRetainedVisualOwner(object visual, IWpfCompositionCommandSink sink)
    {
        if (sink is IWpfRetainedVisualBranchSink retainedVisualBranchSink)
        {
            retainedVisualBranchSink.RegisterVisualOwner(visual);
        }
    }

    private static void RegisterRetainedVisualStateDependencies(object visual, IWpfCompositionCommandSink sink)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            if (visualState.HasTransform)
            {
                RegisterRetainedVisualDependency(visualState.Transform, sink);
            }

            if (visualState.HasClip)
            {
                RegisterRetainedVisualDependency(visualState.Clip, sink);
            }

            if (visualState.HasOpacityMask)
            {
                RegisterRetainedVisualDependency(visualState.OpacityMask, sink);
            }

            if (visualState.HasEffect)
            {
                RegisterRetainedVisualDependency(visualState.Effect, sink);
            }

            if (visualState.HasBitmapEffect)
            {
                RegisterRetainedVisualDependency(visualState.BitmapEffect, sink);
            }

            if (visualState.HasBitmapEffectInput)
            {
                RegisterRetainedVisualDependency(visualState.BitmapEffectInput, sink);
            }

            if (visualState.HasCacheMode)
            {
                RegisterRetainedVisualDependency(visualState.CacheMode, sink);
            }
        }

        if (TryGetPortableVisualLayoutState(visual, out var layoutState) && layoutState.HasLayoutClip)
        {
            RegisterRetainedVisualDependency(layoutState.LayoutClip, sink);
        }
    }

    private static void RegisterRetainedVisualDependencies(
        IReadOnlyList<object?> dependencies,
        IWpfCompositionCommandSink sink)
    {
        for (var i = 0; i < dependencies.Count; i++)
        {
            WpfRetainedVisualDependencyRegistrar.Register(sink, dependencies[i]);
        }
    }

    private static void RegisterRetainedVisualDependency(object? dependency, IWpfCompositionCommandSink sink)
    {
        WpfRetainedVisualDependencyRegistrar.Register(sink, dependency);
    }

    private static void RegisterRetainedVisualDirectDependency(object? dependency, IWpfCompositionCommandSink sink)
    {
        WpfRetainedVisualDependencyRegistrar.RegisterDirect(sink, dependency);
    }

    private static bool TryCreateRetainedVisualState(
        object visual,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfRetainedVisualState state)
    {
        state = default;
        var offset = Vector2.Zero;
        var transform = Matrix4x4.Identity;
        var opacity = 1f;
        WpfReplayRect? clipBounds = null;
        WpfReplayRect? outerClipBounds = null;
        if (!TryCreateRetainedOpacityMaskState(visual, out var opacityMask, out var opacityMaskBounds))
        {
            return false;
        }

        if (TryReadVisualTransform(visual, out var transformValue))
        {
            if (!WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
            {
                return false;
            }
        }

        if (TryReadOffset(visual, out var offsetX, out var offsetY))
        {
            offset = new Vector2((float)offsetX, (float)offsetY);
        }

        if (TryGetVisualClipBounds(visual, out var rectangleClipBounds))
        {
            if (!IsUsableBounds(rectangleClipBounds))
            {
                return false;
            }

            var combinedClipBounds = CombineClipBounds(clipBounds, rectangleClipBounds);
            if (!IsUsableBounds(combinedClipBounds))
            {
                return false;
            }

            clipBounds = combinedClipBounds;
        }
        else if (HasExplicitRetainedVisualClipState(visual))
        {
            return false;
        }

        if (TryGetScrollableAreaClipBounds(visual, out var scrollableClipBounds))
        {
            if (!IsUsableBounds(scrollableClipBounds))
            {
                return false;
            }

            outerClipBounds = scrollableClipBounds;
        }

        if (TryReadOpacity(visual, out var opacityDouble))
        {
            opacity = (float)opacityDouble;
        }

        if (HasUnsupportedRetainedVisualOwnerState(visual))
        {
            return false;
        }

        if (TryCreateSingleNativeRetainedVisualScopeState(
                visual,
                offset,
                transform,
                opacity,
                clipBounds,
                outerClipBounds,
                opacityMask,
                opacityMaskBounds,
                imageSourceAdapter,
                out state))
        {
            return true;
        }

        if (HasNativeRetainedVisualScopeState(visual))
        {
            return false;
        }

        Vector2? size = null;
        if (TryReadRenderSizeBounds(visual, out var bounds))
        {
            size = new Vector2((float)bounds.Width, (float)bounds.Height);
            // WPF uses very large finite doubles as an unbounded layout sentinel.
            // Do not narrow that sentinel to infinity: ProGPU retained visuals use
            // their float size to build local transforms, where infinity * 0
            // produces NaN and drops otherwise valid descendant drawing.
            if (!float.IsFinite(size.Value.X) || !float.IsFinite(size.Value.Y))
            {
                return false;
            }
        }

        state = new WpfRetainedVisualState(
            offset,
            transform,
            opacity,
            clipBounds,
            size,
            contentBounds: null,
            opacityMask: opacityMask,
            opacityMaskBounds: opacityMaskBounds,
            outerClipBounds: outerClipBounds);
        return true;
    }

    private static int PushRetainedVisualStateContentTransform(
        in WpfRetainedVisualState state,
        IWpfCompositionCommandSink sink)
    {
        if (!state.ContentBounds.HasValue)
        {
            return 0;
        }

        var bounds = state.ContentBounds.Value;
        if (bounds.X == 0 && bounds.Y == 0)
        {
            return 0;
        }

        if (sink is IWpfNativeTransformCommandSink nativeTransformSink)
        {
            nativeTransformSink.PushNativeTransform(Matrix4x4.CreateTranslation((float)-bounds.X, (float)-bounds.Y, 0f));
        }
        else
        {
            sink.PushNoOpScope();
        }

        return 1;
    }

    private static void PopRetainedVisualStateContentTransform(int popCount, IWpfCompositionCommandSink sink)
    {
        for (var i = 0; i < popCount; i++)
        {
            sink.Pop();
        }
    }

    private static bool HasUnsupportedRetainedVisualOwnerState(object visual)
    {
        return false;
    }

    private static bool HasNativeRetainedVisualScopeState(object visual)
    {
        return HasVisualEffect(visual)
            || HasVisualBitmapEffect(visual)
            || HasVisualBitmapEffectInput(visual)
            || HasVisualCacheMode(visual);
    }

    private static bool HasVisualEffect(object visual)
    {
        return TryGetVisualEffect(visual, out _);
    }

    private static bool TryGetVisualEffect(object visual, out object? effect)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            effect = visualState.Effect;
            return visualState.HasEffect && effect != null;
        }

        effect = null;
        return false;
    }

    private static bool HasVisualBitmapEffect(object visual)
    {
        return TryGetVisualBitmapEffect(visual, out _);
    }

    private static bool TryGetVisualBitmapEffect(object visual, out object? bitmapEffect)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            bitmapEffect = visualState.BitmapEffect;
            return visualState.HasBitmapEffect && bitmapEffect != null;
        }

        bitmapEffect = null;
        return false;
    }

    private static bool HasVisualBitmapEffectInput(object visual)
    {
        return TryGetVisualBitmapEffectInput(visual, out _);
    }

    private static bool TryGetVisualBitmapEffectInput(object visual, out object? bitmapEffectInput)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            bitmapEffectInput = visualState.BitmapEffectInput;
            return visualState.HasBitmapEffectInput && bitmapEffectInput != null;
        }

        bitmapEffectInput = null;
        return false;
    }

    private static bool HasVisualCacheMode(object visual)
    {
        return TryGetVisualCacheMode(visual, out _);
    }

    private static bool TryGetVisualCacheMode(object visual, out object? cacheMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            cacheMode = visualState.CacheMode;
            return visualState.HasCacheMode && cacheMode != null;
        }

        cacheMode = null;
        return false;
    }

    private static bool TryGetVisualBitmapScalingMode(object visual, out object? bitmapScalingMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            bitmapScalingMode = visualState.BitmapScalingMode;
            return visualState.HasBitmapScalingMode && bitmapScalingMode != null;
        }

        bitmapScalingMode = null;
        return false;
    }

    private static bool TryGetVisualEdgeMode(object visual, out object? edgeMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            edgeMode = visualState.EdgeMode;
            return visualState.HasEdgeMode && edgeMode != null;
        }

        edgeMode = null;
        return false;
    }

    private static bool TryGetVisualClearTypeHint(object visual, out object? clearTypeHint)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            clearTypeHint = visualState.ClearTypeHint;
            return visualState.HasClearTypeHint && clearTypeHint != null;
        }

        clearTypeHint = null;
        return false;
    }

    private static bool TryGetVisualTextRenderingMode(object visual, out object? textRenderingMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            textRenderingMode = visualState.TextRenderingMode;
            return visualState.HasTextRenderingMode && textRenderingMode != null;
        }

        textRenderingMode = null;
        return false;
    }

    private static bool TryGetVisualTextHintingMode(object visual, out object? textHintingMode)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            textHintingMode = visualState.TextHintingMode;
            return visualState.HasTextHintingMode && textHintingMode != null;
        }

        textHintingMode = null;
        return false;
    }

    private static bool TryCreateSingleNativeRetainedVisualScopeState(
        object visual,
        Vector2 offset,
        Matrix4x4 transform,
        float opacity,
        WpfReplayRect? clipBounds,
        WpfReplayRect? outerClipBounds,
        MediaBrush? opacityMask,
        WpfReplayRect? opacityMaskBounds,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out WpfRetainedVisualState state)
    {
        state = default;

        var effectStateCount = 0;
        global::ProGPU.Scene.EffectBase? effect = null;
        var cacheAsLayer = false;

        if (TryGetVisualEffect(visual, out var effectValue))
        {
            if (!WpfEffectMapper.TryCreateProGpuEffect(effectValue, out effect, imageSourceAdapter))
            {
                return false;
            }

            effectStateCount++;
        }

        if (TryGetVisualBitmapEffect(visual, out var bitmapEffect))
        {
            if (effectStateCount != 0)
            {
                return false;
            }

            TryGetVisualBitmapEffectInput(visual, out var bitmapEffectInput);
            if (!WpfEffectMapper.TryCreateProGpuPushEffect(bitmapEffect, bitmapEffectInput, out effect, imageSourceAdapter))
            {
                return false;
            }

            effectStateCount++;
        }
        else if (HasVisualBitmapEffectInput(visual))
        {
            return false;
        }

        if (HasVisualCacheMode(visual))
        {
            cacheAsLayer = true;
        }

        if (effectStateCount == 0 && !cacheAsLayer)
        {
            return false;
        }

        Vector2? size = null;
        WpfReplayRect? contentBounds = null;
        var retainedOffset = offset;
        var retainedTransform = transform;
        var retainedClipBounds = clipBounds;
        var retainedOpacityMaskBounds = opacityMaskBounds;
        if (TryReadRetainedVisualBounds(visual, out var bounds))
        {
            size = new Vector2((float)bounds.Width, (float)bounds.Height);
            contentBounds = bounds;
            retainedClipBounds = clipBounds.HasValue
                ? OffsetBounds(clipBounds.Value, -bounds.X, -bounds.Y)
                : null;
            retainedOpacityMaskBounds = opacityMaskBounds.HasValue
                ? OffsetBounds(opacityMaskBounds.Value, -bounds.X, -bounds.Y)
                : null;

            var boundsOffset = new Vector2((float)bounds.X, (float)bounds.Y);
            if (transform == Matrix4x4.Identity)
            {
                retainedOffset = offset + boundsOffset;
            }
            else
            {
                retainedTransform = Matrix4x4.CreateTranslation((float)bounds.X, (float)bounds.Y, 0f) * transform;
            }
        }

        state = new WpfRetainedVisualState(
            retainedOffset,
            retainedTransform,
            opacity,
            retainedClipBounds,
            size,
            effect,
            cacheAsLayer,
            contentBounds,
            opacityMask,
            retainedOpacityMaskBounds,
            outerClipBounds);
        return true;
    }

    private static bool TryCreateRetainedOpacityMaskState(
        object visual,
        out MediaBrush? opacityMask,
        out WpfReplayRect? opacityMaskBounds)
    {
        opacityMask = null;
        opacityMaskBounds = null;

        if (!TryGetOpacityMask(visual, out var opacityMaskValue) || opacityMaskValue == null)
        {
            return true;
        }

        opacityMask = WpfResourceResolver.AdaptBrush(opacityMaskValue);
        if (opacityMask == null || !TryReadOpacityMaskBounds(visual, out var bounds))
        {
            opacityMask = null;
            return false;
        }

        opacityMaskBounds = bounds;
        return true;
    }

    private static bool TryReadRectangleClipBounds(object clip, out WpfReplayRect bounds)
    {
        if (clip is WpfReplayRect replayRect)
        {
            bounds = replayRect;
            return IsUsableBounds(bounds);
        }

        if (clip is PortableRect portableRect)
        {
            return TryReadPortableRect(portableRect, out bounds);
        }

        if (clip is IPortableGeometryPathSource portableGeometry)
        {
            if (portableGeometry.TryGetPortableGeometryPath(out var portablePath)
                && WpfPortableRectangleClipReader.TryGetRectangleClipBounds(portablePath, out bounds))
            {
                return true;
            }

            bounds = default;
            return false;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadPortableGeometryBounds(PortableGeometryPath geometry, out WpfReplayRect bounds)
    {
        return WpfPortableGeometryBoundsReader.TryGetGeometryBounds(geometry, out bounds);
    }

    private static WpfReplayRect CombineClipBounds(WpfReplayRect? current, WpfReplayRect next)
    {
        if (!current.HasValue)
        {
            return next;
        }

        var x1 = Math.Max(current.Value.X, next.X);
        var y1 = Math.Max(current.Value.Y, next.Y);
        var x2 = Math.Min(current.Value.X + current.Value.Width, next.X + next.Width);
        var y2 = Math.Min(current.Value.Y + current.Value.Height, next.Y + next.Height);
        return x2 <= x1 || y2 <= y1
            ? WpfReplayRect.Empty
            : new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect OffsetBounds(WpfReplayRect bounds, double offsetX, double offsetY)
    {
        return new WpfReplayRect(bounds.X + offsetX, bounds.Y + offsetY, bounds.Width, bounds.Height);
    }

    private static Matrix4x4 ToMatrix4x4(MediaTransform transform)
    {
        return WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
            ? matrix
            : Matrix4x4.Identity;
    }

    private static bool ReplayViewport3DVisual(
        object visual,
        IWpfCompositionCommandSink sink,
        ReplayStats stats)
    {
        if (visual is not IPortableViewport3DSceneSource)
        {
            return false;
        }

        if (sink is IWpfViewport3DCommandSink viewport3DSink
            && viewport3DSink.DrawViewport3D(visual))
        {
            stats.ContentCount++;
        }
        else
        {
            stats.UnsupportedContentCount++;
        }

        return true;
    }

    private void ReplayVisualContent(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        if (!WpfVisualContentBridge.TryExtractContent(visual, out var content) || content == null)
        {
            return;
        }

        if (content is not PortableRenderDataSource)
        {
            stats.UnsupportedContentCount++;
            return;
        }

        stats.ContentCount++;
        var snapshot = WpfRenderDataBridge.Extract(content);
        RegisterRetainedVisualDirectDependency(content, sink);
        RegisterRetainedVisualDependencies(snapshot.DependentResources, sink);
        stats.AddRenderData(_renderDataBridge.Replay(snapshot, sink, resources, imageSourceAdapter));
    }

    private static int PushVisualState(
        object visual,
        IWpfCompositionCommandSink sink,
        IWpfImageSourceAdapter? imageSourceAdapter,
        ReplayStats stats)
    {
        var popCount = 0;
        var localVisualTransform = Matrix4x4.Identity;
        var canProjectScrollableClipToOuterSpace = true;
        var visualStateBoundsInitialized = false;
        var visualStateBoundsAvailable = false;
        var visualStateBounds = default(WpfReplayRect);

        bool TryGetVisualStateBounds(out WpfReplayRect bounds)
        {
            if (!visualStateBoundsInitialized)
            {
                visualStateBoundsAvailable = TryReadOpacityMaskBounds(visual, out visualStateBounds);
                visualStateBoundsInitialized = true;
            }

            bounds = visualStateBounds;
            return visualStateBoundsAvailable;
        }

        if (TryReadVisualTransform(visual, out var transform))
        {
            if (sink is IWpfNativeTransformCommandSink nativeTransformSink
                && WpfResourceResolver.TryAdaptTransformMatrix(transform, out var nativeTransform))
            {
                nativeTransformSink.PushNativeTransform(nativeTransform);
                localVisualTransform = nativeTransform * localVisualTransform;
                popCount++;
            }
            else if (WpfResourceResolver.AdaptTransform(transform) is { } mediaTransform)
            {
                sink.PushTransform(mediaTransform);
                canProjectScrollableClipToOuterSpace = false;
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryReadOffset(visual, out var offsetX, out var offsetY) && (offsetX != 0 || offsetY != 0))
        {
            if (sink is IWpfNativeTransformCommandSink nativeTransformSink)
            {
                var offsetTransform = Matrix4x4.CreateTranslation((float)offsetX, (float)offsetY, 0f);
                nativeTransformSink.PushNativeTransform(offsetTransform);
                localVisualTransform = offsetTransform * localVisualTransform;
            }
            else
            {
                sink.PushNoOpScope();
                canProjectScrollableClipToOuterSpace = false;
            }

            popCount++;
        }

        if (TryGetVisualClipBounds(visual, out var rectangleClipBounds))
        {
            PushRectangleClip(sink, rectangleClipBounds);
            popCount++;
        }
        else if (TryGetVisualClip(visual, out var clip) && clip != null)
        {
            if (TryPushNativeVisualClip(sink, clip))
            {
                popCount++;
            }
            else if (WpfResourceResolver.AdaptGeometry(clip) is { } clipGeometry)
            {
                if (!TryPushNativeMediaVisualClip(sink, clipGeometry))
                {
                    sink.PushClip(clipGeometry);
                }

                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetScrollableAreaClipBounds(visual, out var scrollableClipBounds))
        {
            if (IsUsableBounds(scrollableClipBounds))
            {
                if (!localVisualTransform.IsIdentity
                    && canProjectScrollableClipToOuterSpace
                    && sink is IWpfNativeTransformCommandSink nativeTransformSink
                    && Matrix4x4.Invert(localVisualTransform, out var inverseLocalTransform))
                {
                    nativeTransformSink.PushNativeTransform(inverseLocalTransform);
                    popCount++;
                    PushRectangleClip(sink, scrollableClipBounds);
                    popCount++;
                    nativeTransformSink.PushNativeTransform(localVisualTransform);
                    popCount++;
                }
                else
                {
                    PushRectangleClip(sink, scrollableClipBounds);
                    popCount++;
                }
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryReadOpacity(visual, out var opacity)
            && opacity != 1)
        {
            sink.PushOpacity(opacity);
            popCount++;
        }

        if (TryGetOpacityMask(visual, out var opacityMask) && opacityMask != null)
        {
            var mediaOpacityMask = WpfResourceResolver.AdaptBrush(opacityMask);
            if (mediaOpacityMask != null && TryGetVisualStateBounds(out var opacityMaskBounds))
            {
                WpfPortableCommandSinkBridge.PushOpacityMask(sink, mediaOpacityMask, opacityMaskBounds);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetVisualEffect(visual, out var effect))
        {
            if (WpfEffectMapper.TryCreateProGpuEffect(effect, out var proGpuEffect, imageSourceAdapter)
                && WpfPortableCommandSinkBridge.TryPushVisualEffect(
                    sink,
                    proGpuEffect,
                    TryGetVisualStateBounds(out var effectBounds) ? effectBounds : null))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetVisualBitmapEffect(visual, out var bitmapEffect))
        {
            TryGetVisualBitmapEffectInput(visual, out var bitmapEffectInput);
            if (WpfEffectMapper.TryCreateProGpuPushEffect(bitmapEffect, bitmapEffectInput, out var proGpuBitmapEffect, imageSourceAdapter)
                && WpfPortableCommandSinkBridge.TryPushVisualEffect(
                    sink,
                    proGpuBitmapEffect,
                    TryGetVisualStateBounds(out var bitmapEffectBounds) ? bitmapEffectBounds : null))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }
        else if (HasVisualBitmapEffectInput(visual))
        {
            stats.UnsupportedVisualStateCount++;
        }

        if (HasVisualCacheMode(visual))
        {
            if (WpfPortableCommandSinkBridge.TryPushVisualCache(
                sink,
                TryGetVisualStateBounds(out var cacheBounds) ? cacheBounds : null))
            {
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryCreateVisualGuidelineSet(visual, out var guidelineSet))
        {
            sink.PushGuidelineSet(guidelineSet);
            popCount++;
        }

        if (TryGetVisualBitmapScalingMode(visual, out var bitmapScalingMode))
        {
            if (WpfBitmapScalingModeMapper.IsSupported(bitmapScalingMode))
            {
                sink.PushBitmapScalingMode(bitmapScalingMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (TryGetVisualEdgeMode(visual, out var edgeMode))
        {
            if (WpfEdgeModeMapper.IsSupported(edgeMode))
            {
                sink.PushEdgeMode(edgeMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        var pushedTextRenderingMode = false;
        if (TryGetVisualTextRenderingMode(visual, out var textRenderingMode))
        {
            if (WpfTextRenderingModeMapper.IsSupported(textRenderingMode))
            {
                sink.PushTextRenderingMode(textRenderingMode);
                popCount++;
                pushedTextRenderingMode = true;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        if (!pushedTextRenderingMode
            && TryGetVisualClearTypeHint(visual, out var clearTypeHint)
            && WpfTextRenderingModeMapper.TryMapClearTypeHintToTextRenderingMode(clearTypeHint, out var clearTypeMode))
        {
            sink.PushTextRenderingMode(clearTypeMode);
            popCount++;
        }

        if (TryGetVisualTextHintingMode(visual, out var textHintingMode))
        {
            if (WpfTextRenderingModeMapper.IsSupportedTextHintingMode(textHintingMode))
            {
                sink.PushTextHintingMode(textHintingMode);
                popCount++;
            }
            else
            {
                stats.UnsupportedVisualStateCount++;
            }
        }

        stats.UnsupportedVisualStateCount += CountUnsupportedVisualState(visual);

        return popCount;
    }

    private static bool TryPushNativeVisualClip(IWpfCompositionCommandSink sink, object clip)
    {
        return sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && clip is IPortableGeometryPathSource portableGeometry
            && portableGeometry.TryGetPortableGeometryPath(out var portablePath)
            && nativeGeometrySink.PushNativeGeometryClip(portablePath);
    }

    private static bool TryPushNativeMediaVisualClip(
        IWpfCompositionCommandSink sink,
        MediaGeometry clipGeometry)
    {
        if (sink is IWpfNativeClipCommandSink nativeClipSink
            && WpfMediaRectangleClipReader.TryGetRectangleClipBounds(clipGeometry, out var clipBounds))
        {
            nativeClipSink.PushNativeClip(clipBounds);
            return true;
        }

        return sink is IWpfNativeGeometryCommandSink nativeGeometrySink
            && nativeGeometrySink.PushNativeGeometryClip(clipGeometry);
    }

    private static void PushRectangleClip(IWpfCompositionCommandSink sink, WpfReplayRect bounds)
    {
        if (sink is IWpfNativeClipCommandSink nativeClipSink)
        {
            nativeClipSink.PushNativeClip(bounds);
            return;
        }

        sink.PushClip(WpfResourceResolver.CreateRectanglePath(bounds));
    }

    private static int CountUnsupportedVisualState(object visual)
    {
        var count = 0;

        if (TryGetVisualClearTypeHint(visual, out var clearTypeHint)
            && !WpfTextRenderingModeMapper.IsSupportedClearTypeHint(clearTypeHint))
        {
            count++;
        }

        return count;
    }

    private static bool TryCreateVisualGuidelineSet(object visual, out object guidelineSet)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            if (!visualState.HasSnappingGuidelinesX && !visualState.HasSnappingGuidelinesY)
            {
                guidelineSet = null!;
                return false;
            }

            guidelineSet = s_visualGuidelineSetCache.GetOrCreateValue(visual).GetOrCreate(visualState);
            return true;
        }

        guidelineSet = null!;
        return false;
    }

    private static bool TryGetPortableVisualChildren(
        object visual,
        out PortableVisualChildrenSource childrenSource,
        out int count)
    {
        if (visual is PortableVisualChildrenSource visualChildrenSource
            && visualChildrenSource.TryGetPortableVisualChildCount(out count)
            && count > 0)
        {
            childrenSource = visualChildrenSource;
            return true;
        }

        childrenSource = null!;
        count = 0;
        return false;
    }

    private static bool TryGetPortableVisualState(object visual, out PortableVisualState state)
    {
        if (s_visualStateReplayCacheDepth > 0)
        {
            var cache = s_visualReplayCache ??=
                new Dictionary<object, VisualReplayStateCacheEntry>(ReferenceEqualityComparer.Instance);
            bool hasEntry = cache.TryGetValue(visual, out var entry);
            if (hasEntry && entry.HasVisualStateResult)
            {
                state = entry.VisualState!;
                return entry.VisualState != null;
            }

            bool hasState = TryReadPortableVisualStateUncached(visual, out state);
            if (hasEntry || cache.Count < VisualReplayCacheEntryLimit)
            {
                entry.HasVisualStateResult = true;
                entry.VisualState = hasState ? state : null;
                cache[visual] = entry;
            }

            return hasState;
        }

        return TryReadPortableVisualStateUncached(visual, out state);
    }

    private static bool TryReadPortableVisualStateUncached(object visual, out PortableVisualState state)
    {
        if (visual is PortableVisualStateSource visualStateSource
            && visualStateSource.TryGetPortableVisualState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static bool TryGetPortableVisualLayoutState(object visual, out PortableVisualLayoutState state)
    {
        if (s_visualStateReplayCacheDepth > 0)
        {
            var cache = s_visualReplayCache ??=
                new Dictionary<object, VisualReplayStateCacheEntry>(ReferenceEqualityComparer.Instance);
            bool hasEntry = cache.TryGetValue(visual, out var entry);
            if (hasEntry && entry.HasLayoutStateResult)
            {
                state = entry.LayoutState!;
                return entry.LayoutState != null;
            }

            bool hasState = TryReadPortableVisualLayoutStateUncached(visual, out state);
            if (hasEntry || cache.Count < VisualReplayCacheEntryLimit)
            {
                entry.HasLayoutStateResult = true;
                entry.LayoutState = hasState ? state : null;
                cache[visual] = entry;
            }

            return hasState;
        }

        return TryReadPortableVisualLayoutStateUncached(visual, out state);
    }

    private static bool TryReadPortableVisualLayoutStateUncached(object visual, out PortableVisualLayoutState state)
    {
        if (visual is PortableVisualLayoutStateSource visualLayoutSource
            && visualLayoutSource.TryGetPortableVisualLayoutState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static VisualStateReplayCacheScope BeginVisualStateReplayCache()
    {
        if (s_visualStateReplayCacheDepth == 0)
        {
            (s_visualReplayCache ??=
                new Dictionary<object, VisualReplayStateCacheEntry>(ReferenceEqualityComparer.Instance)).Clear();
        }

        s_visualStateReplayCacheDepth++;
        return default;
    }

    internal static int VisualReplayCacheRetainedCapacity =>
        s_visualReplayCache?.EnsureCapacity(0) ?? 0;

    private readonly struct VisualStateReplayCacheScope : IDisposable
    {
        public void Dispose()
        {
            if (s_visualStateReplayCacheDepth <= 0)
            {
                return;
            }

            s_visualStateReplayCacheDepth--;
            if (s_visualStateReplayCacheDepth == 0)
            {
                s_visualReplayCache?.Clear();
            }
        }
    }

    private struct VisualReplayStateCacheEntry
    {
        public bool HasVisualStateResult;

        public PortableVisualState? VisualState;

        public bool HasLayoutStateResult;

        public PortableVisualLayoutState? LayoutState;
    }

    private static bool TryReadOffset(object visual, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (TryGetPortableVisualState(visual, out var visualState) && visualState.HasOffset)
        {
            x = visualState.Offset.X;
            y = visualState.Offset.Y;
            return true;
        }

        return false;
    }

    private static bool TryReadVisualTransform(object visual, out object? transform)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            transform = visualState.Transform;
            return visualState.HasTransform && transform != null;
        }

        transform = null;
        return false;
    }

    private static bool TryReadOpacity(object visual, out double opacity)
    {
        if (TryGetPortableVisualState(visual, out var visualState) && visualState.HasOpacity)
        {
            opacity = visualState.Opacity;
            return true;
        }

        opacity = 1.0;
        return false;
    }

    private static bool TryGetOpacityMask(object visual, out object? opacityMask)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            opacityMask = visualState.OpacityMask;
            return visualState.HasOpacityMask && opacityMask != null;
        }

        opacityMask = null;
        return false;
    }

    private static bool TryReadOpacityMaskBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryGetPortableVisualBounds(visual, out var visualBounds)
            && TryReadPortableVisualBounds(visualBounds, out bounds))
        {
            return true;
        }

        if (TryReadRenderSizeBounds(visual, out bounds))
        {
            return true;
        }

        if (TryInferVisualContentBounds(visual, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryGetPortableVisualBounds(object visual, out PortableVisualBounds bounds)
    {
        if (visual is PortableVisualBoundsSource visualBoundsSource
            && visualBoundsSource.TryGetPortableVisualBounds(out bounds))
        {
            return true;
        }

        bounds = null!;
        return false;
    }

    private static bool TryReadPortableVisualBounds(PortableVisualBounds visualBounds, out WpfReplayRect bounds)
    {
        if (visualBounds.HasDescendantBounds
            && TryReadPortableRect(visualBounds.DescendantBounds, out bounds))
        {
            return true;
        }

        if (visualBounds.HasContentBounds
            && TryReadPortableRect(visualBounds.ContentBounds, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadPortableRect(PortableRect rect, out WpfReplayRect bounds)
    {
        if (!rect.IsEmpty)
        {
            bounds = new WpfReplayRect(rect.X, rect.Y, rect.Width, rect.Height);
            return IsUsableBounds(bounds);
        }

        bounds = default;
        return false;
    }

    private static bool TryReadRetainedVisualBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryReadRenderSizeBounds(visual, out bounds))
        {
            return true;
        }

        return TryReadOpacityMaskBounds(visual, out bounds);
    }

    private static bool TryReadRenderSizeBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryGetPortableVisualLayoutState(visual, out var layoutState)
            && TryReadPortableRenderSizeBounds(layoutState, out bounds))
        {
            return true;
        }

        bounds = default;
        return false;
    }

    private static bool TryReadPortableRenderSizeBounds(PortableVisualLayoutState state, out WpfReplayRect bounds)
    {
        if (state.HasRenderSize
            && state.RenderSize.Width > 0
            && state.RenderSize.Height > 0)
        {
            bounds = new WpfReplayRect(0, 0, state.RenderSize.Width, state.RenderSize.Height);
            return IsUsableBounds(bounds);
        }

        bounds = default;
        return false;
    }

    private static void TraceRetainedVisualOwnerState(object visual, string state)
    {
        if (!s_traceRetainedVisuals)
        {
            return;
        }

        Console.Error.WriteLine(
            $"ProGPU WPF retained visual {state}: {DescribeVisualForTrace(visual)}");
    }

    private static string DescribeVisualForTrace(object visual)
    {
        if (visual is PortableVisualStateSource)
        {
            return "PortableVisual";
        }

        if (visual is PortableRenderDataSource)
        {
            return "RenderDataVisual";
        }

        return "Owner";
    }

    private static bool IsRetainedVisualTraceEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(TraceRetainedVisualsEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryInferVisualContentBounds(object visual, out WpfReplayRect bounds)
    {
        bounds = default;
        var hasBounds = false;

        if (WpfVisualContentBridge.TryExtractContent(visual, out var content)
            && content != null
            && content is PortableRenderDataSource)
        {
            var snapshot = WpfRenderDataBridge.Extract(content);
            var resolver = WpfResourceResolver.FromDependentResources(snapshot.DependentResources);
            var sink = new BoundsAccumulatingSink();
            try
            {
                _ = new WpfMilRenderDataDecoder().Decode(snapshot.RenderData, sink, resolver);
            }
            catch (TypeLoadException)
            {
                return false;
            }

            if (sink.TryGetBounds(out var contentBounds))
            {
                bounds = contentBounds;
                hasBounds = true;
            }
        }

        if (!TryGetPortableVisualChildren(visual, out var childrenSource, out var childCount))
        {
            return hasBounds && IsUsableBounds(bounds);
        }

        for (var i = 0; i < childCount; i++)
        {
            if (!childrenSource.TryGetPortableVisualChild(i, out var child) || child == null)
            {
                continue;
            }

            if (!TryReadOpacityMaskBounds(child, out var childBounds))
            {
                continue;
            }

            if (!TryProjectChildBoundsIntoParent(child, childBounds, out var projectedChildBounds))
            {
                bounds = default;
                return false;
            }

            bounds = hasBounds ? UnionBounds(bounds, projectedChildBounds) : projectedChildBounds;
            hasBounds = true;
        }

        return hasBounds && IsUsableBounds(bounds);
    }

    private static bool TryProjectChildBoundsIntoParent(object child, WpfReplayRect childBounds, out WpfReplayRect parentBounds)
    {
        parentBounds = default;
        if (!TryClipChildBounds(child, childBounds, out var clippedBounds))
        {
            return false;
        }

        var transform = Matrix4x4.Identity;
        if (TryReadVisualTransform(child, out var transformValue))
        {
            if (!WpfResourceResolver.TryAdaptTransformMatrix(transformValue, out transform))
            {
                return false;
            }
        }

        if (TryReadOffset(child, out var offsetX, out var offsetY) && (offsetX != 0 || offsetY != 0))
        {
            transform = Matrix4x4.CreateTranslation((float)offsetX, (float)offsetY, 0f) * transform;
        }

        parentBounds = TransformBounds(clippedBounds, transform);
        if (TryGetScrollableAreaClipBounds(child, out var scrollableClipBounds))
        {
            if (!IsUsableBounds(scrollableClipBounds))
            {
                return false;
            }

            parentBounds = IntersectBounds(parentBounds, scrollableClipBounds);
        }

        return IsUsableBounds(parentBounds);
    }

    private static bool TryClipChildBounds(object child, WpfReplayRect childBounds, out WpfReplayRect clippedBounds)
    {
        clippedBounds = childBounds;
        if (!IsUsableBounds(clippedBounds))
        {
            return false;
        }

        WpfReplayRect? clipBounds = null;
        if (TryGetVisualClipBounds(child, out var childClipBounds))
        {
            clipBounds = childClipBounds;
        }

        if (!clipBounds.HasValue)
        {
            return true;
        }

        clippedBounds = IntersectBounds(clippedBounds, clipBounds.Value);
        return IsUsableBounds(clippedBounds);
    }

    private static WpfReplayRect ToReplayRect(PortableRect bounds)
    {
        return bounds.IsEmpty
            ? default
            : new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool TryGetScrollableAreaClipBounds(object visual, out WpfReplayRect bounds)
    {
        if (TryGetPortableVisualState(visual, out var visualState))
        {
            if (visualState.HasScrollableAreaClip)
            {
                bounds = ToReplayRect(visualState.ScrollableAreaClip);
                return true;
            }
        }

        bounds = default;
        return false;
    }

    private static bool TryGetVisualClipBounds(object visual, out WpfReplayRect bounds)
    {
        bounds = default;
        var hasBounds = false;

        if (TryGetPortableVisualState(visual, out var visualState) && visualState.HasClip)
        {
            if (visualState.Clip == null || !TryReadRectangleClipBounds(visualState.Clip, out bounds))
            {
                bounds = default;
                return false;
            }

            hasBounds = true;
        }

        if (TryGetPortableVisualLayoutState(visual, out var layoutState)
            && layoutState.HasLayoutClip
            && layoutState.LayoutClip != null)
        {
            if (!TryReadRectangleClipBounds(layoutState.LayoutClip, out var layoutClipBounds))
            {
                bounds = default;
                return false;
            }

            bounds = hasBounds ? CombineClipBounds(bounds, layoutClipBounds) : layoutClipBounds;
            hasBounds = true;
        }

        if (TryCreateClipToBoundsClipBounds(visual, out var clipToBoundsBounds))
        {
            bounds = hasBounds ? CombineClipBounds(bounds, clipToBoundsBounds) : clipToBoundsBounds;
            hasBounds = true;
        }

        if (!hasBounds || !IsUsableBounds(bounds))
        {
            bounds = default;
            return false;
        }

        return true;
    }

    private static bool HasExplicitRetainedVisualClipState(object visual)
    {
        if (TryGetPortableVisualState(visual, out var visualState)
            && visualState.HasClip
            && visualState.Clip != null)
        {
            return true;
        }

        return TryGetPortableVisualLayoutState(visual, out var layoutState)
            && layoutState.HasLayoutClip
            && layoutState.LayoutClip != null;
    }

    private static bool TryGetVisualClip(object visual, out object? clip)
    {
        var hasPortableVisualState = TryGetPortableVisualState(visual, out var visualState);
        object? currentClip = null;
        var hasCurrentClip = false;
        if (hasPortableVisualState && visualState.HasClip)
        {
            currentClip = visualState.Clip;
            hasCurrentClip = currentClip != null;
        }

        var hasPortableLayoutState = TryGetPortableVisualLayoutState(visual, out var layoutState);
        if (hasPortableLayoutState && layoutState.HasLayoutClip && layoutState.LayoutClip != null)
        {
            if (hasCurrentClip)
            {
                return TryCreateIntersectedClip(layoutState.LayoutClip, currentClip!, out clip);
            }

            clip = layoutState.LayoutClip;
            return true;
        }

        if (TryCreateClipToBoundsClip(visual, out var clipToBoundsClip))
        {
            if (hasCurrentClip)
            {
                return TryCreateIntersectedClip(clipToBoundsClip!, currentClip!, out clip);
            }

            clip = clipToBoundsClip;
            return true;
        }

        clip = currentClip;
        return hasCurrentClip;
    }

    private static bool TryCreateClipToBoundsClipBounds(object visual, out WpfReplayRect bounds)
    {
        bounds = default;
        if (TryGetPortableVisualLayoutState(visual, out var layoutState) && layoutState.HasClipToBounds)
        {
            return layoutState.ClipToBounds
                && TryReadPortableRenderSizeBounds(layoutState, out bounds);
        }

        return false;
    }

    private static bool TryCreateClipToBoundsClip(object visual, out object? clip)
    {
        clip = null;
        if (TryCreateClipToBoundsClipBounds(visual, out var bounds))
        {
            clip = CreateRectangleClipGeometry(bounds);
            return true;
        }

        return false;
    }

    private static bool TryCreateIntersectedClip(object first, object second, out object? clip)
    {
        if (TryReadRectangleClipBounds(first, out var firstBounds)
            && TryReadRectangleClipBounds(second, out var secondBounds))
        {
            var combined = CombineClipBounds(firstBounds, secondBounds);
            clip = CreateRectangleClipGeometry(combined);
            return true;
        }

        if (TryCreatePortableIntersectedClip(first, second, out clip))
        {
            return true;
        }

        var firstGeometry = WpfResourceResolver.AdaptGeometry(first);
        var secondGeometry = WpfResourceResolver.AdaptGeometry(second);
        if (firstGeometry == null || secondGeometry == null)
        {
            clip = null;
            return false;
        }

        clip = new System.Windows.Media.CombinedGeometry(
            System.Windows.Media.GeometryCombineMode.Intersect,
            firstGeometry,
            secondGeometry);
        return true;
    }

    private static bool TryCreatePortableIntersectedClip(object first, object second, out object? clip)
    {
        if (!TryReadPortableClipPath(first, out var firstPath)
            || !TryReadPortableClipPath(second, out var secondPath))
        {
            clip = null;
            return false;
        }

        clip = new PortableIntersectedClipGeometry(firstPath, secondPath);
        return true;
    }

    private static bool TryReadPortableClipPath(object clip, out PortableGeometryPath path)
    {
        if (clip is IPortableGeometryPathSource portable
            && portable.TryGetPortableGeometryPath(out path)
            && path != null)
        {
            return true;
        }

        if (TryReadRectangleClipBounds(clip, out var bounds))
        {
            return CreateRectangleClipGeometry(bounds).TryGetPortableGeometryPath(out path);
        }

        path = null!;
        return false;
    }

    private static PortableRectangleClipGeometry CreateRectangleClipGeometry(WpfReplayRect bounds)
    {
        return new PortableRectangleClipGeometry(bounds);
    }

    private static bool IsUsableBounds(WpfReplayRect bounds)
    {
        return double.IsFinite(bounds.X)
            && double.IsFinite(bounds.Y)
            && double.IsFinite(bounds.Width)
            && double.IsFinite(bounds.Height)
            && bounds.Width > 0
            && bounds.Height > 0;
    }

    private static WpfReplayRect UnionBounds(WpfReplayRect left, WpfReplayRect right)
    {
        var x1 = Math.Min(left.X, right.X);
        var y1 = Math.Min(left.Y, right.Y);
        var x2 = Math.Max(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Max(left.Y + left.Height, right.Y + right.Height);

        return new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect IntersectBounds(WpfReplayRect left, WpfReplayRect right)
    {
        var x1 = Math.Max(left.X, right.X);
        var y1 = Math.Max(left.Y, right.Y);
        var x2 = Math.Min(left.X + left.Width, right.X + right.Width);
        var y2 = Math.Min(left.Y + left.Height, right.Y + right.Height);

        return x2 <= x1 || y2 <= y1
            ? WpfReplayRect.Empty
            : new WpfReplayRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static WpfReplayRect TransformBounds(WpfReplayRect bounds, System.Numerics.Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return bounds;
        }

        var p1 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)bounds.Y), transform);
        var p2 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)bounds.Y), transform);
        var p3 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)bounds.X, (float)(bounds.Y + bounds.Height)), transform);
        var p4 = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height)), transform);

        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return new WpfReplayRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static WpfReplayRect? ApplyClip(WpfReplayRect bounds, WpfReplayRect? clip)
    {
        if (!IsUsableBounds(bounds))
        {
            return null;
        }

        if (!clip.HasValue)
        {
            return bounds;
        }

        var clipped = IntersectBounds(bounds, clip.Value);
        return IsUsableBounds(clipped) ? clipped : null;
    }

    private sealed class PortableRectangleClipGeometry : IPortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableRectangleClipGeometry(WpfReplayRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = new PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(bounds.X, bounds.Y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(bounds.X + bounds.Width, bounds.Y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(bounds.X + bounds.Width, bounds.Y + bounds.Height), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(bounds.X, bounds.Y + bounds.Height), isSmoothJoin: false, isStroked: true)
                        ]
                    }
                ]
            };
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class PortableIntersectedClipGeometry : IPortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableIntersectedClipGeometry(PortableGeometryPath first, PortableGeometryPath second)
        {
            var bounds = TryReadPortableGeometryBounds(first, out var firstBounds)
                && TryReadPortableGeometryBounds(second, out var secondBounds)
                ? CombineClipBounds(firstBounds, secondBounds)
                : default;

            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Combined,
                Bounds = IsUsableBounds(bounds)
                    ? new PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                    : PortableRect.Empty,
                Transform = PortableMatrix3x2.Identity,
                PathA = first,
                PathB = second,
                CombineOperation = 1
            };
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class BoundsAccumulatingSink :
        IWpfCompositionCommandSink,
        IWpfNativePrimitiveCommandSink,
        IWpfNativeTransformCommandSink,
        IWpfNativeClipCommandSink,
        IWpfNativeGeometryCommandSink
    {
        private enum PushKind
        {
            NoOp,
            Clip,
            Transform
        }

        private ProGpuCompositionCommandSink.SmallValueStack<PushKind> _pushStack;
        private ProGpuCompositionCommandSink.SmallValueStack<System.Numerics.Matrix4x4> _transformStack;
        private ProGpuCompositionCommandSink.SmallValueStack<WpfReplayRect?> _clipStack;
        private WpfReplayRect _bounds;
        private bool _hasBounds;

        public BoundsAccumulatingSink()
        {
            _transformStack.Push(System.Numerics.Matrix4x4.Identity);
            _clipStack.Push(null);
        }

        public MediaDrawingContext DrawingContext => null!;

        public bool TryGetBounds(out WpfReplayRect bounds)
        {
            bounds = _bounds;
            return _hasBounds && IsUsableBounds(_bounds);
        }

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            AddLineBounds(pen, point0.X, point0.Y, point1.X, point1.Y);
        }

        public void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
        {
            AddLineBounds(pen, point0.X, point0.Y, point1.X, point1.Y);
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            AddBounds(InflateForPen(FromMediaRect(rectangle), pen));
        }

        public void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
        {
            AddBounds(InflateForPen(rectangle, pen));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(FromMediaRect(rectangle), pen));
        }

        public void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(rectangle, pen));
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2), pen));
        }

        public void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
        {
            AddBounds(InflateForPen(new WpfReplayRect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2), pen));
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            if (TryGetPrimitiveMediaGeometryBounds(pen, geometry, out var bounds))
            {
                AddBounds(bounds);
                return;
            }

            if (WpfMediaGeometryBoundsReader.TryGetGeometryBounds(geometry, out var geometryBounds))
            {
                AddBounds(InflateForPen(geometryBounds, pen));
            }
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
        {
            if (!TryReadPortableGeometryBounds(geometry, out var bounds))
            {
                return false;
            }

            AddBounds(InflateForPen(bounds, pen));
            return true;
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            DrawGeometry(brush, pen, geometry);
            return true;
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            AddBounds(FromMediaRect(rectangle));
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
        {
            AddBounds(rectangle);
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
        {
            AddBounds(FromMediaRect(rectangle));
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
        {
            AddBounds(rectangle);
        }

        public void DrawText(MediaFormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            if (TryGetGlyphRunBounds(glyphRun, out var bounds))
            {
                AddBounds(bounds);
            }
        }

        public void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRun)
        {
            if (WpfResourceResolver.TryAdaptNativeGlyphRun(glyphRun, out var nativeGlyphRun)
                && TryGetGlyphRunBounds(nativeGlyphRun, out var bounds))
            {
                AddBounds(bounds);
            }
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            if (WpfMediaRectangleClipReader.TryGetRectangleClipBounds(clipGeometry, out var clipBounds))
            {
                PushClipCore(clipBounds);
                return;
            }

            if (TryGetPrimitiveMediaGeometryBounds(null, clipGeometry, out var primitiveClipBounds))
            {
                PushClipCore(primitiveClipBounds);
                return;
            }

            if (WpfMediaGeometryBoundsReader.TryGetGeometryBounds(clipGeometry, out var geometryClipBounds))
            {
                PushClipCore(geometryClipBounds);
                return;
            }

            _pushStack.Push(PushKind.NoOp);
        }

        public void PushNativeClip(WpfReplayRect bounds)
        {
            PushClipCore(bounds);
        }

        public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
        {
            if (!TryReadPortableGeometryBounds(clipGeometry, out var bounds))
            {
                return false;
            }

            PushClipCore(bounds);
            return true;
        }

        public bool PushNativeGeometryClip(MediaGeometry clipGeometry)
        {
            PushClip(clipGeometry);
            return true;
        }

        private void PushClipCore(WpfReplayRect bounds)
        {
            var clip = TransformBounds(bounds, _transformStack.Peek());
            var currentClip = _clipStack.Peek();
            _clipStack.Push(currentClip.HasValue ? IntersectBounds(currentClip.Value, clip) : clip);
            _pushStack.Push(PushKind.Clip);
        }

        public void PushOpacity(double opacity)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushTransform(MediaTransform transform)
        {
            var nativeTransform = WpfResourceResolver.TryAdaptTransformMatrix(transform, out var adaptedTransform)
                ? adaptedTransform
                : System.Numerics.Matrix4x4.Identity;
            PushTransformCore(nativeTransform);
        }

        public void PushNativeTransform(System.Numerics.Matrix4x4 transform)
        {
            PushTransformCore(transform);
        }

        private void PushTransformCore(System.Numerics.Matrix4x4 transform)
        {
            _transformStack.Push(transform * _transformStack.Peek());
            _pushStack.Push(PushKind.Transform);
        }

        public void PushNoOpScope()
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineSet()
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineY1(double coordinate)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            _pushStack.Push(PushKind.NoOp);
        }

        public void Pop()
        {
            if (_pushStack.Count == 0)
            {
                return;
            }

            var kind = _pushStack.Pop();
            if (kind == PushKind.Transform && _transformStack.Count > 1)
            {
                _transformStack.Pop();
            }
            else if (kind == PushKind.Clip && _clipStack.Count > 1)
            {
                _clipStack.Pop();
            }
        }

        public void Close()
        {
            _pushStack.Dispose();
            _transformStack.Dispose();
            _clipStack.Dispose();
        }

        public void Dispose()
        {
            Close();
        }

        private void AddBounds(WpfReplayRect bounds)
        {
            var transformed = TransformBounds(bounds, _transformStack.Peek());
            var clipped = ApplyClip(transformed, _clipStack.Peek());
            if (!clipped.HasValue)
            {
                return;
            }

            _bounds = _hasBounds ? UnionBounds(_bounds, clipped.Value) : clipped.Value;
            _hasBounds = true;
        }

        private void AddLineBounds(MediaPen? pen, double x0, double y0, double x1, double y1)
        {
            var thickness = Math.Max(1, pen?.Thickness ?? 1);
            var minX = Math.Min(x0, x1) - thickness / 2;
            var minY = Math.Min(y0, y1) - thickness / 2;
            var maxX = Math.Max(x0, x1) + thickness / 2;
            var maxY = Math.Max(y0, y1) + thickness / 2;
            AddBounds(new WpfReplayRect(minX, minY, maxX - minX, maxY - minY));
        }

        private static bool TryGetPrimitiveMediaGeometryBounds(
            MediaPen? pen,
            MediaGeometry geometry,
            out WpfReplayRect bounds)
        {
            if (WpfMediaRectangleClipReader.TryGetRectangleClipBounds(geometry, out var rectangleBounds)
                || WpfMediaRectangleClipReader.TryGetRectangleStrokeBounds(geometry, out rectangleBounds)
                || TryGetEllipseGeometryBounds(geometry, out rectangleBounds))
            {
                bounds = InflateForPen(rectangleBounds, pen);
                return true;
            }

            if (WpfMediaLineGeometryReader.TryGetPolylineSegments(geometry, out var segments))
            {
                if (TryGetLineSegmentBounds(segments, out bounds))
                {
                    bounds = InflateForLinePen(bounds, pen);
                    return true;
                }

                bounds = default;
                return false;
            }

            if (WpfMediaLineGeometryReader.TryGetLinePoints(geometry, out var startPoint, out var endPoint))
            {
                if (TryGetLineBounds(startPoint.X, startPoint.Y, endPoint.X, endPoint.Y, out bounds))
                {
                    bounds = InflateForLinePen(bounds, pen);
                    return true;
                }

                bounds = default;
                return false;
            }

            bounds = default;
            return false;
        }

        private static WpfReplayRect InflateForPen(WpfReplayRect bounds, MediaPen? pen)
        {
            if (pen == null || !IsUsableBounds(bounds))
            {
                return bounds;
            }

            var halfThickness = Math.Max(0, pen.Thickness) / 2;
            return new WpfReplayRect(
                bounds.X - halfThickness,
                bounds.Y - halfThickness,
                bounds.Width + halfThickness * 2,
                bounds.Height + halfThickness * 2);
        }

        private static WpfReplayRect InflateForLinePen(WpfReplayRect bounds, MediaPen? pen)
        {
            if (pen == null || !IsUsableBounds(bounds))
            {
                return bounds;
            }

            var halfThickness = Math.Max(1, pen.Thickness) / 2;
            return new WpfReplayRect(
                bounds.X - halfThickness,
                bounds.Y - halfThickness,
                bounds.Width + halfThickness * 2,
                bounds.Height + halfThickness * 2);
        }

        private static bool TryGetGlyphRunBounds(MediaGlyphRun glyphRun, out WpfReplayRect bounds)
        {
            bounds = default;

            if (glyphRun.FontSize <= 0)
            {
                return false;
            }

            var minX = glyphRun.Position.X;
            var minY = glyphRun.Position.Y - glyphRun.FontSize;
            var maxX = glyphRun.Position.X;
            var maxY = glyphRun.Position.Y;

            if (glyphRun.GlyphPositions.Length == 0)
            {
                maxX += glyphRun.FontSize;
            }
            else
            {
                var originX = glyphRun.Position.X;
                var originY = glyphRun.Position.Y;
                var fontSize = glyphRun.FontSize;
                var glyphPositions = glyphRun.GlyphPositions;
                for (var i = 0; i < glyphPositions.Length; i++)
                {
                    var position = glyphPositions[i];
                    var x = originX + position.X;
                    var y = originY + position.Y;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y - fontSize);
                    maxX = Math.Max(maxX, x + fontSize);
                    maxY = Math.Max(maxY, y);
                }
            }

            bounds = TransformBounds(
                new WpfReplayRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
                glyphRun.Transform);
            return IsUsableBounds(bounds);
        }

        private static bool TryGetGlyphRunBounds(WpfNativeGlyphRun glyphRun, out WpfReplayRect bounds)
        {
            bounds = default;
            if (glyphRun.HasBounds)
            {
                bounds = glyphRun.TransformedBounds;
                return IsUsableBounds(bounds);
            }

            if (glyphRun.FontSize <= 0)
            {
                return false;
            }

            var minX = glyphRun.Position.X;
            var minY = glyphRun.Position.Y - glyphRun.FontSize;
            var maxX = glyphRun.Position.X;
            var maxY = glyphRun.Position.Y;

            if (glyphRun.GlyphPositions.Length == 0)
            {
                maxX += glyphRun.FontSize;
            }
            else
            {
                var originX = glyphRun.Position.X;
                var originY = glyphRun.Position.Y;
                var fontSize = glyphRun.FontSize;
                var glyphPositions = glyphRun.GlyphPositions;
                for (var i = 0; i < glyphPositions.Length; i++)
                {
                    var position = glyphPositions[i];
                    var x = originX + position.X;
                    var y = originY + position.Y;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y - fontSize);
                    maxX = Math.Max(maxX, x + fontSize);
                    maxY = Math.Max(maxY, y);
                }
            }

            bounds = TransformBounds(
                new WpfReplayRect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
                glyphRun.Transform);
            return IsUsableBounds(bounds);
        }

        private static bool TryGetEllipseGeometryBounds(MediaGeometry geometry, out WpfReplayRect bounds)
        {
            return WpfMediaEllipseGeometryReader.TryGetEllipseBounds(geometry, out bounds);
        }

        private static bool TryGetLineSegmentBounds(
            IReadOnlyList<WpfReplayLineSegment> segments,
            out WpfReplayRect bounds)
        {
            bounds = default;
            if (segments.Count == 0)
            {
                return false;
            }

            var left = double.PositiveInfinity;
            var top = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            var bottom = double.NegativeInfinity;
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                IncludePoint(segment.StartPoint);
                IncludePoint(segment.EndPoint);
            }

            return TryCreateLineBounds(left, top, right, bottom, out bounds);

            void IncludePoint(WpfReplayPoint point)
            {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }
        }

        private static bool TryGetLineBounds(
            double x0,
            double y0,
            double x1,
            double y1,
            out WpfReplayRect bounds)
        {
            return TryCreateLineBounds(
                Math.Min(x0, x1),
                Math.Min(y0, y1),
                Math.Max(x0, x1),
                Math.Max(y0, y1),
                out bounds);
        }

        private static bool TryCreateLineBounds(
            double left,
            double top,
            double right,
            double bottom,
            out WpfReplayRect bounds)
        {
            var width = right - left;
            var height = bottom - top;
            if (!double.IsFinite(left)
                || !double.IsFinite(top)
                || !double.IsFinite(width)
                || !double.IsFinite(height)
                || (width == 0 && height == 0))
            {
                bounds = default;
                return false;
            }

            bounds = new WpfReplayRect(left, top, width, height);
            return IsUsableBounds(bounds);
        }

        private static bool HasIdentityGeometryTransform(MediaGeometry geometry)
        {
            var transform = geometry.Transform;
            return transform == null
                || (WpfResourceResolver.TryAdaptTransformMatrix(transform, out var matrix)
                    && WpfResourceResolver.IsIdentityMatrix(matrix));
        }

        private static WpfReplayRect FromMediaRect(Rect bounds)
        {
            return new WpfReplayRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

    }

    private sealed class VisualGuidelineSet : IPortableGuidelineSetSource
    {
        private readonly double[] _guidelinesX;
        private readonly double[] _guidelinesY;

        public VisualGuidelineSet(double[] guidelinesX, double[] guidelinesY)
        {
            _guidelinesX = guidelinesX;
            _guidelinesY = guidelinesY;
        }

        public bool TryGetPortableGuidelineSet(out PortableGuidelineSet guidelineSet)
        {
            guidelineSet = new PortableGuidelineSet(
                isFrozen: true,
                isDynamic: true,
                _guidelinesX,
                _guidelinesY);
            return true;
        }
    }

    private sealed class VisualGuidelineSetCache
    {
        private bool _hasGuidelinesX;
        private bool _hasGuidelinesY;
        private double[]? _guidelinesX;
        private double[]? _guidelinesY;
        private VisualGuidelineSet? _guidelineSet;

        public VisualGuidelineSet GetOrCreate(PortableVisualState visualState)
        {
            var hasGuidelinesX = visualState.HasSnappingGuidelinesX;
            var hasGuidelinesY = visualState.HasSnappingGuidelinesY;
            var guidelinesX = hasGuidelinesX ? visualState.SnappingGuidelinesX ?? Array.Empty<double>() : Array.Empty<double>();
            var guidelinesY = hasGuidelinesY ? visualState.SnappingGuidelinesY ?? Array.Empty<double>() : Array.Empty<double>();

            if (_guidelineSet == null
                || _hasGuidelinesX != hasGuidelinesX
                || _hasGuidelinesY != hasGuidelinesY
                || !ReferenceEquals(_guidelinesX, guidelinesX)
                || !ReferenceEquals(_guidelinesY, guidelinesY))
            {
                _hasGuidelinesX = hasGuidelinesX;
                _hasGuidelinesY = hasGuidelinesY;
                _guidelinesX = guidelinesX;
                _guidelinesY = guidelinesY;
                _guidelineSet = new VisualGuidelineSet(guidelinesX, guidelinesY);
            }

            return _guidelineSet;
        }
    }

    private sealed class ReplayStats
    {
        private int _renderDataRecordCount;
        private int _renderDataAppliedCount;
        private int _renderDataSkippedCount;
        private int _renderDataUnsupportedCount;

        public int VisualCount { get; set; }

        public int ContentCount { get; set; }

        public int ChildEdgeCount { get; set; }

        public int UnsupportedContentCount { get; set; }

        public int UnsupportedVisualStateCount { get; set; }

        public void AddRenderData(WpfMilDecodeResult result)
        {
            _renderDataRecordCount += result.RecordCount;
            _renderDataAppliedCount += result.AppliedCount;
            _renderDataSkippedCount += result.SkippedCount;
            _renderDataUnsupportedCount += result.UnsupportedCount;
        }

        public WpfVisualReplayResult ToResult()
        {
            return new WpfVisualReplayResult(
                VisualCount,
                ContentCount,
                ChildEdgeCount,
                UnsupportedContentCount,
                UnsupportedVisualStateCount,
                new WpfMilDecodeResult(
                    _renderDataRecordCount,
                    _renderDataAppliedCount,
                    _renderDataSkippedCount,
                    _renderDataUnsupportedCount));
        }
    }
}

public readonly record struct WpfVisualReplayResult(
    int VisualCount,
    int ContentCount,
    int ChildEdgeCount,
    int UnsupportedContentCount,
    int UnsupportedVisualStateCount,
    WpfMilDecodeResult RenderData);
