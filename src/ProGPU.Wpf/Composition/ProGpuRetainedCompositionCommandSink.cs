using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableMediaPlayerFrame = ProGPU.Wpf.Interop.PortableMediaPlayerFrame;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuEffectBase = global::ProGPU.Scene.EffectBase;

namespace System.Windows.Media.ProGPU.Composition;

internal enum ProGpuRetainedCompositionLayer
{
    Main,
    Popup
}

internal sealed class ProGpuRetainedCompositionCommandSink :
    IWpfCompositionCommandSink,
    IWpfViewport3DCommandSink,
    IWpfVisualEffectCommandSink,
    IWpfVisualCacheCommandSink,
    IWpfDrawingCacheCommandSink,
    IWpfNativeVisualEffectCommandSink,
    IWpfNativeVisualCacheCommandSink,
    IWpfNativeDrawingCacheCommandSink,
    IWpfRetainedVisualBranchSink,
    IWpfRetainedVisualStateSink,
    IWpfNativeTransformCommandSink,
    IWpfNativePrimitiveCommandSink,
    IWpfNativeVideoCommandSink,
    IWpfNativeClipCommandSink,
    IWpfNativeGeometryCommandSink,
    IWpfHitTestOwnerScopeCommandSink,
    IWpfBitmapCacheBrushCommandSink,
    IWpfProGpuSceneDrawingContextSource
{
    private enum ScopeKind
    {
        Delegate,
        VisualEffect,
        VisualCache,
        DrawingCache
    }

    private enum VisualScopeKind
    {
        Root,
        SourceOwner,
        Effect,
        Cache
    }

    private ProGpuCompositionCommandSink.SmallValueStack<ScopeKind> _scopeStack;
    private ProGpuCompositionCommandSink.SmallValueStack<VisualScope> _visualScopes;
    private readonly ProGpuWpfDrawingFrame _drawingFrame;
    private bool _isClosed;

    public ProGpuRetainedCompositionCommandSink(
        ProGpuWpfDrawingFrame drawingFrame,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache)
        : this(
            drawingFrame,
            context,
            viewport3DTextureCache,
            ProGpuRetainedCompositionLayer.Main)
    {
    }

    internal ProGpuRetainedCompositionCommandSink(
        ProGpuWpfDrawingFrame drawingFrame,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache,
        ProGpuRetainedCompositionLayer layer)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);
        _drawingFrame = drawingFrame;

        RootVisual = new ProGpuRetainedDrawingVisual
        {
            Size = new Vector2(drawingFrame.LogicalWidth, drawingFrame.LogicalHeight)
        };

        bool added = layer == ProGpuRetainedCompositionLayer.Popup
            ? drawingFrame.AddPopupRetainedWpfVisual(RootVisual)
            : drawingFrame.AddRetainedWpfVisual(RootVisual);
        if (!added)
        {
            throw new InvalidOperationException("The drawing frame does not expose a retained WPF visual root.");
        }

        _visualScopes.Push(new VisualScope(drawingFrame, RootVisual, context, viewport3DTextureCache, VisualScopeKind.Root, 0));
    }

    internal ProGpuRetainedCompositionCommandSink(
        ProGpuWpfDrawingFrame drawingFrame,
        ProGpuRetainedDrawingVisual rootVisual,
        global::ProGPU.Backend.WgpuContext? context,
        WpfViewport3DTextureCache? viewport3DTextureCache)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);
        ArgumentNullException.ThrowIfNull(rootVisual);

        _drawingFrame = drawingFrame;
        RootVisual = rootVisual;
        _visualScopes.Push(new VisualScope(drawingFrame, RootVisual, context, viewport3DTextureCache, VisualScopeKind.Root, 0));
    }

    public MediaDrawingContext? DrawingContext => Current.DrawingContext;

    bool IWpfProGpuSceneDrawingContextSource.TryGetProGpuSceneDrawingContext(
        out global::ProGPU.Scene.DrawingContext? drawingContext)
    {
        return ((IWpfProGpuSceneDrawingContextSource)this)
            .TryGetProGpuSceneDrawingContextState(out drawingContext, out _);
    }

    bool IWpfProGpuSceneDrawingContextSource.TryGetProGpuSceneDrawingContextState(
        out global::ProGPU.Scene.DrawingContext? drawingContext,
        out Matrix4x4 transform)
    {
        if (_isClosed || _visualScopes.Count == 0)
        {
            drawingContext = null;
            transform = Matrix4x4.Identity;
            return false;
        }

        return ((IWpfProGpuSceneDrawingContextSource)_visualScopes.Peek().Sink)
            .TryGetProGpuSceneDrawingContextState(out drawingContext, out transform);
    }

    internal ProGpuRetainedDrawingVisual RootVisual { get; }

    public bool PushNativeEllipseClip(WpfReplayPoint center, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (!((IWpfNativeGeometryCommandSink)Current.Sink).PushNativeEllipseClip(center, radiusX, radiusY)) return false;
        _scopeStack.Push(ScopeKind.Delegate);
        return true;
    }

    public bool PushNativeRoundedRectangleClip(WpfReplayRect bounds, double radiusX, double radiusY)
    {
        ThrowIfClosed();
        if (!((IWpfNativeGeometryCommandSink)Current.Sink).PushNativeRoundedRectangleClip(bounds, radiusX, radiusY)) return false;
        _scopeStack.Push(ScopeKind.Delegate);
        return true;
    }

    void IWpfBitmapCacheBrushCommandSink.DrawBitmapCacheBrushSource(
        global::ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource source,
        Func<object?, MediaImageSource?>? imageSourceAdapter)
    {
        ThrowIfClosed();
        ((IWpfBitmapCacheBrushCommandSink)Current.Sink).DrawBitmapCacheBrushSource(source, imageSourceAdapter);
    }

    public void RegisterVisualOwner(object sourceVisual)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(sourceVisual);

        _drawingFrame.RegisterRetainedWpfVisualOwner(sourceVisual, Current.Visual);
    }

    public void RegisterVisualDependency(object dependency)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(dependency);

        _drawingFrame.RegisterRetainedWpfVisualDependency(dependency, Current.Visual);
    }

    public bool PushVisualOwner(object sourceVisual)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(sourceVisual);

        var ownerVisual = new ProGpuRetainedDrawingVisual();

        Current.Visual.AddChild(ownerVisual);
        int hitTestOwnerId = _drawingFrame.GetOrCreateHitTestOwnerId(sourceVisual);
        ownerVisual.HitTestId = hitTestOwnerId;
        _visualScopes.Push(new VisualScope(
            _drawingFrame,
            ownerVisual,
            Current.Context,
            Current.Viewport3DTextureCache,
            VisualScopeKind.SourceOwner,
            _scopeStack.Count,
            hitTestOwnerId));
        _drawingFrame.RegisterRetainedWpfVisualOwner(sourceVisual, ownerVisual);
        return true;
    }

    public void PopVisualOwner()
    {
        ThrowIfClosed();

        if (_visualScopes.Count <= 1)
        {
            throw new InvalidOperationException("There is no retained source owner visual scope to pop.");
        }

        var current = Current;
        if (current.ScopeKind != VisualScopeKind.SourceOwner)
        {
            throw new InvalidOperationException("The current retained visual scope is not a source owner scope.");
        }

        if (_scopeStack.Count < current.ScopeStackDepth)
        {
            throw new InvalidOperationException("Cannot pop a retained source owner visual scope after its parent drawing scopes changed.");
        }

        while (_scopeStack.Count > current.ScopeStackDepth)
        {
            Pop();
        }

        PopVisualScope();
    }

    bool IWpfHitTestOwnerScopeCommandSink.PushHitTestOwner(object sourceVisual)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(sourceVisual);

        _drawingFrame.RegisterRetainedWpfVisualOwner(sourceVisual, Current.Visual);
        return ((IWpfHitTestOwnerScopeCommandSink)Current.Sink).PushHitTestOwner(sourceVisual);
    }

    void IWpfHitTestOwnerScopeCommandSink.PopHitTestOwner()
    {
        ThrowIfClosed();
        ((IWpfHitTestOwnerScopeCommandSink)Current.Sink).PopHitTestOwner();
    }

    public void ApplyVisualState(in WpfRetainedVisualState state)
    {
        ThrowIfClosed();

        var visual = Current.Visual;
        visual.Offset = state.Offset;
        if (state.Size.HasValue)
        {
            visual.Size = state.Size.Value;
        }

        visual.Transform = state.Transform;
        visual.Opacity = state.Opacity;
        visual.ClipBounds = state.ClipBounds.HasValue
            ? ToNativeRect(state.ClipBounds.Value)
            : null;
        visual.OuterClipBounds = state.OuterClipBounds.HasValue
            ? ToNativeRect(state.OuterClipBounds.Value)
            : null;
        visual.OpacityMask = state.OpacityMask != null && state.OpacityMaskBounds.HasValue
            ? ToNativeBrush(state.OpacityMask, state.OpacityMaskBounds.Value)
            : null;
        visual.OpacityMaskBounds = state.OpacityMask != null && state.OpacityMaskBounds.HasValue
            ? ToNativeRect(state.OpacityMaskBounds.Value)
            : null;
        visual.Effect = state.Effect;
        visual.CacheAsLayer = state.CacheAsLayer;
    }

    private static global::ProGPU.Vector.Brush? ToNativeBrush(MediaBrush brush, WpfReplayRect bounds)
    {
        return WpfResourceResolver.AdaptNativeBrush(brush, bounds, out _);
    }

    private static global::ProGPU.Scene.Rect ToNativeRect(WpfReplayRect bounds)
    {
        return new global::ProGPU.Scene.Rect(
            (float)bounds.X,
            (float)bounds.Y,
            (float)bounds.Width,
            (float)bounds.Height);
    }

    private VisualScope Current
    {
        get
        {
            ThrowIfClosed();
            return _visualScopes.Peek();
        }
    }

    public void DrawLine(MediaPen? pen, Point point0, Point point1)
    {
        Current.Sink.DrawLine(pen, point0, point1);
    }

    public void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeLine(pen, point0, point1);
    }

    public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
    {
        Current.Sink.DrawRectangle(brush, pen, rectangle);
    }

    public void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeRectangle(brush, pen, rectangle);
    }

    public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        Current.Sink.DrawRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
    }

    public void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeRoundedRectangle(brush, pen, rectangle, radiusX, radiusY);
    }

    public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
    {
        Current.Sink.DrawEllipse(brush, pen, center, radiusX, radiusY);
    }

    public void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeEllipse(brush, pen, center, radiusX, radiusY);
    }

    public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        Current.Sink.DrawGeometry(brush, pen, geometry);
    }

    public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
    {
        return Current.Sink is IWpfNativeGeometryCommandSink nativeSink
            && nativeSink.DrawNativeGeometry(brush, pen, geometry);
    }

    public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
    {
        return Current.Sink is IWpfNativeGeometryCommandSink nativeSink
            && nativeSink.DrawNativeGeometry(brush, pen, geometry);
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle)
    {
        Current.Sink.DrawImage(imageSource, rectangle);
    }

    public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeImage(imageSource, rectangle);
    }

    public void DrawImage(MediaImageSource imageSource, Rect rectangle, Rect sourceRectangle)
    {
        Current.Sink.DrawImage(imageSource, rectangle, sourceRectangle);
    }

    public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeImage(imageSource, rectangle, sourceRectangle);
    }

    public bool DrawNativeVideo(
        PortableMediaPlayerFrame frame,
        WpfReplayRect rectangle)
    {
        return Current.Sink is IWpfNativeVideoCommandSink videoSink &&
            videoSink.DrawNativeVideo(frame, rectangle);
    }

    public void DrawText(MediaFormattedText formattedText, Point origin)
    {
        Current.Sink.DrawText(formattedText, origin);
    }

    public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
    {
        Current.Sink.DrawGlyphRun(foregroundBrush, glyphRun);
    }

    public void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRun)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).DrawNativeGlyphRun(foregroundBrush, glyphRun);
    }

    public bool DrawViewport3D(object viewportVisual)
    {
        return Current.Sink.DrawViewport3D(viewportVisual);
    }

    public void PushClip(MediaGeometry clipGeometry)
    {
        Current.Sink.PushClip(clipGeometry);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
    {
        if (Current.Sink is not IWpfNativeGeometryCommandSink nativeSink
            || !nativeSink.PushNativeGeometryClip(clipGeometry))
        {
            return false;
        }

        _scopeStack.Push(ScopeKind.Delegate);
        return true;
    }

    public bool PushNativeGeometryClip(MediaGeometry clipGeometry)
    {
        if (Current.Sink is not IWpfNativeGeometryCommandSink nativeSink
            || !nativeSink.PushNativeGeometryClip(clipGeometry))
        {
            return false;
        }

        _scopeStack.Push(ScopeKind.Delegate);
        return true;
    }

    public void PushOpacity(double opacity)
    {
        Current.Sink.PushOpacity(opacity);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
    {
        Current.Sink.PushOpacityMask(opacityMask, bounds);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
    {
        ((IWpfNativePrimitiveCommandSink)Current.Sink).PushNativeOpacityMask(opacityMask, bounds);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushNativeClip(WpfReplayRect bounds)
    {
        ((IWpfNativeClipCommandSink)Current.Sink).PushNativeClip(bounds);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushTransform(MediaTransform transform)
    {
        Current.Sink.PushTransform(transform);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushNativeTransform(Matrix4x4 transform)
    {
        Current.Sink.PushNativeTransform(transform);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushNoOpScope()
    {
        Current.Sink.PushNoOpScope();
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineSet()
    {
        Current.Sink.PushGuidelineSet();
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineSet(object? guidelines)
    {
        Current.Sink.PushGuidelineSet(guidelines);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineY1(double coordinate)
    {
        Current.Sink.PushGuidelineY1(coordinate);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        Current.Sink.PushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushBitmapScalingMode(object? bitmapScalingMode)
    {
        Current.Sink.PushBitmapScalingMode(bitmapScalingMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushEdgeMode(object? edgeMode)
    {
        Current.Sink.PushEdgeMode(edgeMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushTextRenderingMode(object? textRenderingMode)
    {
        Current.Sink.PushTextRenderingMode(textRenderingMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public void PushTextHintingMode(object? textHintingMode)
    {
        Current.Sink.PushTextHintingMode(textHintingMode);
        _scopeStack.Push(ScopeKind.Delegate);
    }

    public bool PushVisualEffect(ProGpuEffectBase effect)
    {
        return PushNativeVisualEffect(effect, bounds: null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool PushVisualEffect(ProGpuEffectBase effect, Rect? bounds)
    {
        return PushNativeVisualEffect(effect, ToReplayRect(bounds));
    }

    public bool PushNativeVisualEffect(ProGpuEffectBase effect, WpfReplayRect? bounds)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(effect);

        var effectBounds = NormalizeBounds(bounds);
        var effectVisual = new ProGpuRetainedDrawingVisual
        {
            Effect = effect,
            Offset = new Vector2((float)effectBounds.X, (float)effectBounds.Y),
            Size = new Vector2((float)effectBounds.Width, (float)effectBounds.Height)
        };

        PushVisualScope(effectVisual, effectBounds, ScopeKind.VisualEffect);
        return true;
    }

    public bool PushVisualCache(Rect? bounds = null)
    {
        return PushNativeCacheVisual(ToReplayRect(bounds), ScopeKind.VisualCache);
    }

    public bool PushDrawingCache(Rect? bounds = null)
    {
        return PushNativeCacheVisual(ToReplayRect(bounds), ScopeKind.DrawingCache);
    }

    public bool PushNativeVisualCache(WpfReplayRect? bounds = null)
    {
        return PushNativeCacheVisual(bounds, ScopeKind.VisualCache);
    }

    public bool PushNativeDrawingCache(WpfReplayRect? bounds = null)
    {
        return PushNativeCacheVisual(bounds, ScopeKind.DrawingCache);
    }

    private bool PushNativeCacheVisual(WpfReplayRect? bounds, ScopeKind scopeKind)
    {
        ThrowIfClosed();
        var cacheBounds = NormalizeBounds(bounds);
        var cacheVisual = new ProGpuRetainedDrawingVisual
        {
            CacheAsLayer = true,
            Offset = new Vector2((float)cacheBounds.X, (float)cacheBounds.Y),
            Size = new Vector2((float)cacheBounds.Width, (float)cacheBounds.Height)
        };

        PushVisualScope(cacheVisual, cacheBounds, scopeKind);
        return true;
    }

    public void Pop()
    {
        ThrowIfClosed();

        if (_scopeStack.Count == 0)
        {
            Current.Sink.Pop();
            return;
        }

        var scopeKind = _scopeStack.Pop();
        if (scopeKind == ScopeKind.Delegate)
        {
            Current.Sink.Pop();
            return;
        }

        PopVisualScope();
    }

    public void Close()
    {
        if (_isClosed)
        {
            return;
        }

        while (_scopeStack.Count > 0)
        {
            Pop();
        }

        while (_visualScopes.Count > 0)
        {
            _visualScopes.Pop().Dispose();
        }

        _scopeStack.Dispose();
        _visualScopes.Dispose();
        _isClosed = true;
    }

    public void Dispose()
    {
        Close();
    }

    private WpfReplayRect NormalizeBounds(WpfReplayRect? bounds)
    {
        if (bounds.HasValue
            && double.IsFinite(bounds.Value.X)
            && double.IsFinite(bounds.Value.Y)
            && double.IsFinite(bounds.Value.Width)
            && double.IsFinite(bounds.Value.Height)
            && bounds.Value.Width > 0
            && bounds.Value.Height > 0)
        {
            return bounds.Value;
        }

        var rootSize = _visualScopes.Count > 0
            ? _visualScopes.Peek().Visual.Size
            : Vector2.One;
        return new WpfReplayRect(0, 0, Math.Max(1, rootSize.X), Math.Max(1, rootSize.Y));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WpfReplayRect? ToReplayRect(Rect? bounds)
    {
        if (!bounds.HasValue)
        {
            return null;
        }

        var value = bounds.Value;
        return new WpfReplayRect(value.X, value.Y, value.Width, value.Height);
    }

    private void PushVisualScope(ProGpuRetainedDrawingVisual visual, WpfReplayRect bounds, ScopeKind scopeKind)
    {
        Current.Visual.AddChild(visual);

        var visualScopeKind = scopeKind == ScopeKind.VisualEffect ? VisualScopeKind.Effect : VisualScopeKind.Cache;
        var hitTestId = Current.HitTestId;
        visual.HitTestId = hitTestId;
        var scope = new VisualScope(_drawingFrame, visual, Current.Context, Current.Viewport3DTextureCache, visualScopeKind, _scopeStack.Count, hitTestId);
        if (bounds.X != 0 || bounds.Y != 0)
        {
            scope.Sink.PushNativeTransform(Matrix4x4.CreateTranslation((float)-bounds.X, (float)-bounds.Y, 0f));
            scope.HasBoundsTransform = true;
        }

        _visualScopes.Push(scope);
        _scopeStack.Push(scopeKind);
    }

    private void PopVisualScope()
    {
        if (_visualScopes.Count <= 1)
        {
            return;
        }

        var scope = _visualScopes.Pop();
        if (scope.HasBoundsTransform)
        {
            scope.Sink.Pop();
        }

        scope.Dispose();
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new ObjectDisposedException(nameof(ProGpuRetainedCompositionCommandSink));
        }
    }

    private sealed class VisualScope : IDisposable
    {
        public VisualScope(
            ProGpuWpfDrawingFrame drawingFrame,
            ProGpuRetainedDrawingVisual visual,
            global::ProGPU.Backend.WgpuContext? context,
            WpfViewport3DTextureCache? viewport3DTextureCache,
            VisualScopeKind scopeKind,
            int scopeStackDepth,
            int hitTestId = 0)
        {
            ArgumentNullException.ThrowIfNull(drawingFrame);
            Visual = visual;
            Context = context;
            Viewport3DTextureCache = viewport3DTextureCache;
            ScopeKind = scopeKind;
            ScopeStackDepth = scopeStackDepth;
            HitTestId = hitTestId;
            Sink = new ProGpuCompositionCommandSink(
                visual.Context,
                context,
                viewport3DTextureCache,
                hitTestId: hitTestId,
                hitTestOwnerMap: drawingFrame.HitTestOwnerMap);
        }

        public ProGpuRetainedDrawingVisual Visual { get; }

        public global::ProGPU.Backend.WgpuContext? Context { get; }

        public WpfViewport3DTextureCache? Viewport3DTextureCache { get; }

        public VisualScopeKind ScopeKind { get; }

        public int ScopeStackDepth { get; }

        public int HitTestId { get; }

        public MediaDrawingContext? DrawingContext => Sink.DrawingContext;

        public ProGpuCompositionCommandSink Sink { get; }

        public bool HasBoundsTransform { get; set; }

        public void Dispose()
        {
            Sink.Dispose();
        }
    }
}

internal sealed class ProGpuRetainedDrawingVisual : ProGpuContainerVisual
{
    public ProGpuDrawingContext Context { get; } = new();

    public override void OnRender(ProGpuDrawingContext context)
    {
        context.Append(Context);
    }
}
