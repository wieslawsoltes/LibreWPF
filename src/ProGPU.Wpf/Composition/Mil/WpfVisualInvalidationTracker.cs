using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProGPU.Backend;
using PortableBitmapCacheBrushSource = ProGPU.Wpf.Interop.IPortableBitmapCacheBrushSource;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableDrawingGroupChildrenSource = ProGPU.Wpf.Interop.IPortableDrawingGroupChildrenSource;
using PortableDrawingImageSource = ProGPU.Wpf.Interop.IPortableDrawingImageSource;
using PortableDrawingGroupState = ProGPU.Wpf.Interop.PortableDrawingGroupState;
using PortableDrawingGroupStateSource = ProGPU.Wpf.Interop.IPortableDrawingGroupStateSource;
using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;
using PortableGlyphRunDrawingStateSource = ProGPU.Wpf.Interop.IPortableGlyphRunDrawingStateSource;
using PortableImageDrawingStateSource = ProGPU.Wpf.Interop.IPortableImageDrawingStateSource;
using PortableInvalidationSource = ProGPU.Wpf.Interop.IPortableInvalidationSource;
using PortableNativeImageSource = ProGPU.Wpf.Interop.IPortableNativeImageSource;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;
using PortableShaderEffectSource = ProGPU.Wpf.Interop.IPortableShaderEffectSource;
using PortableShaderSamplerKind = ProGPU.Wpf.Interop.PortableShaderSamplerKind;
using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfVisualInvalidationTracker : IDisposable
{
    [ThreadStatic]
    private static HashSet<object>? s_registerTrackedDependenciesVisited;

    [ThreadStatic]
    private static HashSet<object>? s_enumerateTrackedDependenciesVisited;

    private readonly List<InvalidationSubscription> _subscriptions = new();
    private readonly Dictionary<object, VisualStateSnapshot> _visualStateSnapshots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object?[]> _visualChildrenSnapshots = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _dirtySources = new(ReferenceEqualityComparer.Instance);
    private readonly List<object> _changedSources = new();
    private readonly HashSet<object> _visualStateTraversalVisited = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _visualChildrenCurrentSources = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _subscriptionTraversalVisited = new(ReferenceEqualityComparer.Instance);
    private object? _root;
    private object? _lastDirtySource;
    private bool _isDirty;
    private bool _isRefreshing;
    private bool _subscriptionsNeedRefresh;

    public event EventHandler? Invalidated;

    public object? Root => _root;

    public bool IsDirty => _isDirty;

    public int SubscriptionCount => _subscriptions.Count;

    public int VisualStateSnapshotCount => _visualStateSnapshots.Count;

    public int DirtySourceCount => _dirtySources.Count;

    public object? LastDirtySource => _lastDirtySource;

    public IReadOnlyCollection<object> DirtySources => _dirtySources;

    internal HashSet<object> DirtySourceSet => _dirtySources;

    internal static IReadOnlyList<object> EnumerateTrackedDependencies(object? source)
    {
        if (source == null)
        {
            return Array.Empty<object>();
        }

        var dependencies = new List<object>();
        var visited = s_enumerateTrackedDependenciesVisited;
        if (visited == null)
        {
            visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            s_enumerateTrackedDependenciesVisited = visited;
        }
        else if (visited.Count != 0)
        {
            var reentrantVisited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            try
            {
                CollectTrackedDependencies(source, dependencies, reentrantVisited);
                return dependencies;
            }
            finally
            {
                reentrantVisited.Clear();
            }
        }

        try
        {
            CollectTrackedDependencies(source, dependencies, visited);
            return dependencies;
        }
        finally
        {
            visited.Clear();
        }
    }

    internal static bool RegisterTrackedDependencies(
        IWpfRetainedVisualBranchSink sink,
        object? source)
    {
        if (source == null || IsTerminalValue(source))
        {
            return false;
        }

        var visited = s_registerTrackedDependenciesVisited;
        if (visited == null)
        {
            visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            s_registerTrackedDependenciesVisited = visited;
        }
        else if (visited.Count != 0)
        {
            var reentrantVisited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            try
            {
                return RegisterTrackedDependencies(sink, source, reentrantVisited);
            }
            finally
            {
                reentrantVisited.Clear();
            }
        }

        try
        {
            return RegisterTrackedDependencies(sink, source, visited);
        }
        finally
        {
            visited.Clear();
        }
    }

    public void AttachIfChanged(object? root)
    {
        if (!ReferenceEquals(_root, root))
        {
            Attach(root);
        }
    }

    public void Attach(object? root)
    {
        Detach();
        _root = root;

        if (root == null)
        {
            return;
        }

        SubscribeGraph(root);
        MarkDirty(root);
    }

    public bool ConsumeDirty()
    {
        var wasDirty = _isDirty;
        _isDirty = false;
        _dirtySources.Clear();
        _lastDirtySource = null;
        RefreshSubscriptionsIfNeeded();
        return wasDirty;
    }

    public bool DetectVersionChanges()
    {
        if (_root == null)
        {
            return false;
        }

        if (_isDirty)
        {
            return true;
        }

        _changedSources.Clear();
        _visualStateTraversalVisited.Clear();
        _visualChildrenCurrentSources.Clear();
        try
        {
            CollectVisualStateAndChildrenChanges(
                _root,
                _visualStateSnapshots,
                _visualChildrenSnapshots,
                _visualChildrenCurrentSources,
                _changedSources,
                _visualStateTraversalVisited);
            CollectRemovedVisualStateSources(
                _visualStateSnapshots,
                _visualStateTraversalVisited,
                _changedSources);
            CollectRemovedVisualChildrenSources(
                _visualChildrenSnapshots,
                _visualChildrenCurrentSources,
                _changedSources);

            if (_changedSources.Count == 0)
            {
                return false;
            }

            MarkDirtyListAndRefresh(_changedSources);
            return true;
        }
        finally
        {
            _changedSources.Clear();
            _visualStateTraversalVisited.Clear();
            _visualChildrenCurrentSources.Clear();
        }
    }

    public void MarkDirty()
    {
        MarkDirty(null);
    }

    public void MarkDirty(object? source)
    {
        var shouldRaiseInvalidated = MarkDirtyCore(source);
        RaiseInvalidatedIfNeeded(shouldRaiseInvalidated);
    }

    private bool MarkDirtyCore(object? source)
    {
        if (source != null)
        {
            _dirtySources.Add(source);
            _lastDirtySource = source;
        }

        if (_isDirty)
        {
            return false;
        }

        _isDirty = true;
        return true;
    }

    private void RaiseInvalidatedIfNeeded(bool shouldRaiseInvalidated)
    {
        if (shouldRaiseInvalidated)
        {
            Invalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Detach()
    {
        ClearSubscriptions();
        _visualStateSnapshots.Clear();
        _visualChildrenSnapshots.Clear();
        _dirtySources.Clear();
        _changedSources.Clear();
        _visualStateTraversalVisited.Clear();
        _visualChildrenCurrentSources.Clear();
        _subscriptionTraversalVisited.Clear();
        _root = null;
        _lastDirtySource = null;
        _isDirty = false;
        _subscriptionsNeedRefresh = false;
    }

    public void Dispose()
    {
        Detach();
    }

    private void MarkDirtyAndRefresh(object? source)
    {
        MarkDirty(source);
        RequestSubscriptionRefresh();
    }

    private void MarkDirtyListAndRefresh(IReadOnlyList<object> sources)
    {
        var shouldRaiseInvalidated = false;
        for (var i = 0; i < sources.Count; i++)
        {
            shouldRaiseInvalidated |= MarkDirtyCore(sources[i]);
        }

        RaiseInvalidatedIfNeeded(shouldRaiseInvalidated);
        RequestSubscriptionRefresh();
    }

    private void RequestSubscriptionRefresh()
    {
        if (_root != null)
        {
            _subscriptionsNeedRefresh = true;
        }
    }

    private void RefreshSubscriptionsIfNeeded()
    {
        if (_subscriptionsNeedRefresh)
        {
            RefreshSubscriptions();
        }
    }

    private void RefreshSubscriptions()
    {
        if (_root == null)
        {
            _subscriptionsNeedRefresh = false;
            return;
        }

        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            _subscriptionsNeedRefresh = false;
            ClearSubscriptions();
            _visualStateSnapshots.Clear();
            SubscribeGraph(_root);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void SubscribeGraph(object root)
    {
        _subscriptionTraversalVisited.Clear();
        _visualChildrenCurrentSources.Clear();
        try
        {
            SubscribeObject(root, _subscriptionTraversalVisited);
            RemoveStaleVisualChildrenSnapshots(_visualChildrenSnapshots, _visualChildrenCurrentSources);
        }
        finally
        {
            _subscriptionTraversalVisited.Clear();
            _visualChildrenCurrentSources.Clear();
        }
    }

    private void SubscribeObject(object? source, HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        SubscribeInvalidationEvents(source);
        CaptureVisualStateSnapshot(source);
        CaptureVisualChildrenSnapshot(source);

        if (source is INotifyCollectionChanged collectionChanged)
        {
            var handlerTarget = new CollectionChangedInvalidationHandler(this, source);
            NotifyCollectionChangedEventHandler handler = handlerTarget.OnCollectionChanged;
            if (TrySubscribeCollectionChanged(collectionChanged, handler))
            {
                _subscriptions.Add(InvalidationSubscription.ForCollectionChanged(collectionChanged, handler));
            }
        }

        if (source is IEnumerable collection)
        {
            var collectionState = new SubscribeDependencyState(this, visited);
            VisitCollectionItems(collection, ref collectionState, default(SubscribeDependencyVisitor));
        }

        var dependencyState = new SubscribeDependencyState(this, visited);
        VisitPortableDependencies(source, ref dependencyState, default(SubscribeDependencyVisitor));
    }

    private void CaptureVisualStateSnapshot(object source)
    {
        if (TryReadVisualStateSnapshot(source, out var snapshot))
        {
            _visualStateSnapshots[source] = snapshot;
        }
    }

    private void CaptureVisualChildrenSnapshot(object source)
    {
        if (TryReadPortableVisualChildrenSnapshot(
                source,
                _visualChildrenSnapshots,
                _visualChildrenCurrentSources,
                out var snapshot))
        {
            _visualChildrenSnapshots[source] = snapshot;
        }
    }

    private static void CollectVisualStateAndChildrenChanges(
        object root,
        Dictionary<object, VisualStateSnapshot> previousStates,
        Dictionary<object, object?[]> previousChildren,
        HashSet<object> currentChildrenSources,
        List<object> changedSources,
        HashSet<object> visited)
    {
        currentChildrenSources.Clear();
        visited.Clear();
        CaptureObjectVisualStateAndChildren(
            root,
            previousStates,
            previousChildren,
            currentChildrenSources,
            changedSources,
            visited);
    }

    private static void CaptureObjectVisualStateAndChildren(
        object? source,
        Dictionary<object, VisualStateSnapshot> previousStates,
        Dictionary<object, object?[]> previousChildren,
        HashSet<object> currentChildrenSources,
        List<object> changedSources,
        HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        if (TryReadVisualStateSnapshot(source, out var snapshot))
        {
            if (!previousStates.TryGetValue(source, out var previousSnapshot) ||
                !previousSnapshot.Equals(snapshot))
            {
                changedSources.Add(source);
            }
        }
        else if (previousStates.ContainsKey(source))
        {
            changedSources.Add(source);
        }

        if (TryGetPortableVisualChildrenSource(source, out var visualChildrenSource, out var count))
        {
            currentChildrenSources.Add(source);
            if (!previousChildren.TryGetValue(source, out var previousSnapshot) ||
                !VisualChildrenSnapshotEquals(visualChildrenSource, count, previousSnapshot))
            {
                changedSources.Add(source);
            }
        }

        if (source is IEnumerable collection)
        {
            var collectionState = new CaptureVisualStateAndChildrenDependencyState(
                previousStates,
                previousChildren,
                currentChildrenSources,
                changedSources,
                visited);
            VisitCollectionItems(collection, ref collectionState, default(CaptureVisualStateAndChildrenDependencyVisitor));
        }

        var dependencyState = new CaptureVisualStateAndChildrenDependencyState(
            previousStates,
            previousChildren,
            currentChildrenSources,
            changedSources,
            visited);
        VisitPortableDependencies(source, ref dependencyState, default(CaptureVisualStateAndChildrenDependencyVisitor));
    }

    private static void CollectTrackedDependencies(
        object? source,
        List<object> dependencies,
        HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return;
        }

        dependencies.Add(source);

        if (source is IEnumerable collection)
        {
            var collectionState = new CollectTrackedDependencyState(dependencies, visited);
            VisitCollectionItems(collection, ref collectionState, default(CollectTrackedDependencyVisitor));
        }

        var dependencyState = new CollectTrackedDependencyState(dependencies, visited);
        VisitPortableDependencies(source, ref dependencyState, default(CollectTrackedDependencyVisitor));
    }

    private static bool RegisterTrackedDependencies(
        IWpfRetainedVisualBranchSink sink,
        object? source,
        HashSet<object> visited)
    {
        if (source == null || IsTerminalValue(source) || !visited.Add(source))
        {
            return false;
        }

        sink.RegisterVisualDependency(source);
        var registered = true;

        if (source is IEnumerable collection)
        {
            var collectionState = new RegisterTrackedDependencyState(sink, visited);
            VisitCollectionItems(collection, ref collectionState, default(RegisterTrackedDependencyVisitor));
            registered |= collectionState.Registered;
        }

        var dependencyState = new RegisterTrackedDependencyState(sink, visited);
        VisitPortableDependencies(source, ref dependencyState, default(RegisterTrackedDependencyVisitor));
        registered |= dependencyState.Registered;

        return registered;
    }

    private static void CollectRemovedVisualStateSources(
        Dictionary<object, VisualStateSnapshot> previous,
        HashSet<object> visited,
        List<object> changedSources)
    {
        var previousStateEnumerator = previous.GetEnumerator();
        while (previousStateEnumerator.MoveNext())
        {
            var snapshot = previousStateEnumerator.Current;
            if (!visited.Contains(snapshot.Key))
            {
                changedSources.Add(snapshot.Key);
            }
        }
    }

    private static void CollectRemovedVisualChildrenSources(
        Dictionary<object, object?[]> previous,
        HashSet<object> currentSources,
        List<object> changedSources)
    {
        var previousChildrenEnumerator = previous.GetEnumerator();
        while (previousChildrenEnumerator.MoveNext())
        {
            var snapshot = previousChildrenEnumerator.Current;
            if (!currentSources.Contains(snapshot.Key))
            {
                changedSources.Add(snapshot.Key);
            }
        }
    }

    private static bool TryReadVisualStateSnapshot(object source, out VisualStateSnapshot snapshot)
    {
        var builder = new VisualStateSnapshotBuilder();
        var hasPortableVisualState = TryGetPortableVisualState(source, out var visualState);
        var hasPortableLayoutState = TryGetPortableVisualLayoutState(source, out var layoutState);

        if (hasPortableVisualState && visualState.HasOffset)
        {
            builder.SetOffset(visualState.Offset.X, visualState.Offset.Y);
        }

        if (hasPortableVisualState && visualState.HasClip)
        {
            builder.SetClip(visualState.Clip);
        }

        if (hasPortableLayoutState && layoutState.HasClipToBounds)
        {
            builder.SetClipToBounds(layoutState.ClipToBounds);
        }

        if (hasPortableLayoutState && layoutState.HasLayoutClip)
        {
            builder.SetLayoutClip(layoutState.LayoutClip);
        }

        if (hasPortableVisualState && visualState.HasTransform)
        {
            builder.SetTransform(visualState.Transform);
        }

        if (hasPortableVisualState && visualState.HasScrollableAreaClip)
        {
            var scrollClip = visualState.ScrollableAreaClip;
            builder.SetScrollableAreaClip(scrollClip.X, scrollClip.Y, scrollClip.Width, scrollClip.Height);
        }

        if (hasPortableVisualState && visualState.HasOpacity)
        {
            builder.SetOpacity(visualState.Opacity);
        }

        if (hasPortableVisualState && visualState.HasOpacityMask)
        {
            builder.SetOpacityMask(visualState.OpacityMask);
        }

        if (hasPortableVisualState)
        {
            if (visualState.HasEffect)
            {
                builder.SetEffect(visualState.Effect);
            }

            if (visualState.HasBitmapEffect)
            {
                builder.SetBitmapEffect(visualState.BitmapEffect);
            }

            if (visualState.HasBitmapEffectInput)
            {
                builder.SetBitmapEffectInput(visualState.BitmapEffectInput);
            }

            if (visualState.HasCacheMode)
            {
                builder.SetCacheMode(visualState.CacheMode);
            }

            if (visualState.HasBitmapScalingMode)
            {
                builder.SetBitmapScalingMode(visualState.BitmapScalingMode);
            }

            if (visualState.HasEdgeMode)
            {
                builder.SetEdgeMode(visualState.EdgeMode);
            }

            if (visualState.HasClearTypeHint)
            {
                builder.SetClearTypeHint(visualState.ClearTypeHint);
            }

            if (visualState.HasTextRenderingMode)
            {
                builder.SetTextRenderingMode(visualState.TextRenderingMode);
            }

            if (visualState.HasTextHintingMode)
            {
                builder.SetTextHintingMode(visualState.TextHintingMode);
            }

            if (visualState.HasSnappingGuidelinesX)
            {
                builder.SetSnappingGuidelinesX(visualState.SnappingGuidelinesX);
            }

            if (visualState.HasSnappingGuidelinesY)
            {
                builder.SetSnappingGuidelinesY(visualState.SnappingGuidelinesY);
            }
        }

        if (hasPortableLayoutState && TryReadPortableRenderSize(layoutState, out var width, out var height))
        {
            builder.SetRenderSize(width, height);
        }

        snapshot = builder.ToSnapshot();
        return builder.HasState;
    }

    private static bool TryGetPortableVisualState(object source, out PortableVisualState state)
    {
        if (source is PortableVisualStateSource visualStateSource
            && visualStateSource.TryGetPortableVisualState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static bool TryGetPortableVisualLayoutState(object source, out PortableVisualLayoutState state)
    {
        if (source is PortableVisualLayoutStateSource visualLayoutSource
            && visualLayoutSource.TryGetPortableVisualLayoutState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static bool TryReadPortableVisualChildrenSnapshot(
        object source,
        Dictionary<object, object?[]> previousSnapshots,
        HashSet<object> currentSources,
        out object?[] children)
    {
        children = Array.Empty<object?>();
        if (!TryGetPortableVisualChildrenSource(source, out var visualChildrenSource, out var count))
        {
            return false;
        }

        currentSources.Add(source);
        if (previousSnapshots.TryGetValue(source, out var previousSnapshot) &&
            VisualChildrenSnapshotEquals(visualChildrenSource, count, previousSnapshot))
        {
            children = previousSnapshot;
            return true;
        }

        if (count == 0)
        {
            return true;
        }

        children = new object?[count];
        for (var i = 0; i < count; i++)
        {
            children[i] = TryGetPortableVisualChild(visualChildrenSource, i);
        }

        return true;
    }

    private static void RemoveStaleVisualChildrenSnapshots(
        Dictionary<object, object?[]> snapshots,
        HashSet<object> currentSources)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        object[]? staleSources = null;
        var staleSourceCount = 0;
        try
        {
            var snapshotEnumerator = snapshots.GetEnumerator();
            while (snapshotEnumerator.MoveNext())
            {
                var snapshot = snapshotEnumerator.Current;
                if (!currentSources.Contains(snapshot.Key))
                {
                    WpfPooledRemovalBuffer.Add(
                        ref staleSources,
                        ref staleSourceCount,
                        snapshots.Count,
                        snapshot.Key);
                }
            }

            for (var i = 0; i < staleSourceCount; i++)
            {
                snapshots.Remove(staleSources![i]);
            }
        }
        finally
        {
            WpfPooledRemovalBuffer.Return(staleSources, staleSourceCount);
        }
    }

    private static bool TryGetPortableVisualChildrenSource(
        object source,
        out PortableVisualChildrenSource visualChildrenSource,
        out int count)
    {
        if (source is PortableVisualChildrenSource sourceVisualChildrenSource &&
            sourceVisualChildrenSource.TryGetPortableVisualChildCount(out count) &&
            count >= 0)
        {
            visualChildrenSource = sourceVisualChildrenSource;
            return true;
        }

        visualChildrenSource = null!;
        count = 0;
        return false;
    }

    private static object? TryGetPortableVisualChild(PortableVisualChildrenSource visualChildrenSource, int index)
    {
        try
        {
            return visualChildrenSource.TryGetPortableVisualChild(index, out var child) ? child : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool VisualChildrenSnapshotEquals(
        PortableVisualChildrenSource visualChildrenSource,
        int count,
        object?[] previous)
    {
        if (previous.Length != count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(previous[i], TryGetPortableVisualChild(visualChildrenSource, i)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadPortableRenderSize(
        PortableVisualLayoutState state,
        out double width,
        out double height)
    {
        if (state.HasRenderSize)
        {
            width = state.RenderSize.Width;
            height = state.RenderSize.Height;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    private void SubscribeInvalidationEvents(object source)
    {
        if (source is PortableInvalidationSource invalidationSource)
        {
            var handlerTarget = new SourceInvalidationHandler(this, source);
            EventHandler handler = handlerTarget.OnInvalidated;
            if (invalidationSource.TrySubscribeInvalidated(handler, out var subscription))
            {
                _subscriptions.Add(InvalidationSubscription.ForDisposable(subscription));
            }
        }
        else if (source is IProGpuInvalidatingTextureSource textureSource)
        {
            var handlerTarget = new SourceInvalidationHandler(this, source);
            EventHandler handler = handlerTarget.OnInvalidated;
            textureSource.TextureChanged += handler;
            _subscriptions.Add(InvalidationSubscription.ForDisposable(
                new ProGpuTextureInvalidationSubscription(
                    textureSource,
                    handler)));
        }

        if (source is INotifyPropertyChanged propertyChanged)
        {
            var handlerTarget = new PropertyChangedInvalidationHandler(this, source);
            PropertyChangedEventHandler handler = handlerTarget.OnPropertyChanged;
            if (TrySubscribePropertyChanged(propertyChanged, handler))
            {
                _subscriptions.Add(InvalidationSubscription.ForPropertyChanged(propertyChanged, handler));
            }
        }
    }

    private static bool TrySubscribePropertyChanged(
        INotifyPropertyChanged source,
        PropertyChangedEventHandler handler)
    {
        try
        {
            source.PropertyChanged += handler;
            return true;
        }
        catch (Exception exception) when (IsExpectedInvalidationSubscriptionException(exception))
        {
            return false;
        }
    }

    private static bool TryUnsubscribePropertyChanged(
        INotifyPropertyChanged source,
        PropertyChangedEventHandler handler)
    {
        try
        {
            source.PropertyChanged -= handler;
            return true;
        }
        catch (Exception exception) when (IsExpectedInvalidationSubscriptionException(exception))
        {
            return false;
        }
    }

    private static bool TrySubscribeCollectionChanged(
        INotifyCollectionChanged source,
        NotifyCollectionChangedEventHandler handler)
    {
        try
        {
            source.CollectionChanged += handler;
            return true;
        }
        catch (Exception exception) when (IsExpectedInvalidationSubscriptionException(exception))
        {
            return false;
        }
    }

    private static bool TryUnsubscribeCollectionChanged(
        INotifyCollectionChanged source,
        NotifyCollectionChangedEventHandler handler)
    {
        try
        {
            source.CollectionChanged -= handler;
            return true;
        }
        catch (Exception exception) when (IsExpectedInvalidationSubscriptionException(exception))
        {
            return false;
        }
    }

    private static bool TryDisposeInvalidationSubscription(IDisposable subscription)
    {
        try
        {
            subscription.Dispose();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (MethodAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return false;
    }

    private static bool IsExpectedInvalidationSubscriptionException(Exception exception)
    {
        return exception is InvalidOperationException
            or ArgumentException
            or MethodAccessException
            or NotSupportedException;
    }

    private static void VisitPortableDependencies<TState, TVisitor>(
        object source,
        ref TState state,
        TVisitor visitor)
        where TVisitor : struct, IPortableDependencyVisitor<TState>
    {
        if (source is PortableDrawingContentSource drawingContentSource
            && drawingContentSource.TryGetPortableDrawingContent(out var drawingContent)
            && drawingContent != null)
        {
            VisitPortableDependency(ref state, visitor, drawingContent);
        }

        if (source is PortableDrawingImageSource drawingImageSource
            && drawingImageSource.TryGetPortableDrawingImage(out var drawingImageContent)
            && drawingImageContent != null)
        {
            VisitPortableDependency(ref state, visitor, drawingImageContent);
        }

        if (source is PortableNativeImageSource nativeImageSource
            && nativeImageSource.TryGetPortableNativeImage(out var nativeImage)
            && nativeImage != null)
        {
            VisitPortableDependency(ref state, visitor, nativeImage);
        }

        if (source is PortableRenderDataSource renderDataSource
            && renderDataSource.TryGetPortableRenderDataSnapshot(out var renderDataSnapshot))
        {
            for (var i = 0; i < renderDataSnapshot.DependentResources.Count; i++)
            {
                VisitPortableDependency(ref state, visitor, renderDataSnapshot.DependentResources[i]);
            }
        }

        if (TryGetPortableVisualChildrenSource(source, out var visualChildrenSource, out var visualChildrenCount))
        {
            for (var i = 0; i < visualChildrenCount; i++)
            {
                VisitPortableDependency(ref state, visitor, TryGetPortableVisualChild(visualChildrenSource, i));
            }
        }

        if (source is PortableVisualStateSource visualStateSource
            && visualStateSource.TryGetPortableVisualState(out var visualState))
        {
            if (visualState.HasTransform)
            {
                VisitPortableDependency(ref state, visitor, visualState.Transform);
            }

            if (visualState.HasClip)
            {
                VisitPortableDependency(ref state, visitor, visualState.Clip);
            }

            if (visualState.HasOpacityMask)
            {
                VisitPortableDependency(ref state, visitor, visualState.OpacityMask);
            }

            if (visualState.HasEffect)
            {
                VisitPortableDependency(ref state, visitor, visualState.Effect);
            }

            if (visualState.HasBitmapEffect)
            {
                VisitPortableDependency(ref state, visitor, visualState.BitmapEffect);
            }

            if (visualState.HasBitmapEffectInput)
            {
                VisitPortableDependency(ref state, visitor, visualState.BitmapEffectInput);
            }

            if (visualState.HasCacheMode)
            {
                VisitPortableDependency(ref state, visitor, visualState.CacheMode);
            }
        }

        if (source is PortableVisualLayoutStateSource visualLayoutSource
            && visualLayoutSource.TryGetPortableVisualLayoutState(out var layoutState)
            && layoutState.HasLayoutClip)
        {
            VisitPortableDependency(ref state, visitor, layoutState.LayoutClip);
        }

        if (source is PortableGeometryDrawingStateSource geometryDrawingSource
            && geometryDrawingSource.TryGetPortableGeometryDrawingState(out var geometryDrawingState))
        {
            if (geometryDrawingState.HasGeometry)
            {
                VisitPortableDependency(ref state, visitor, geometryDrawingState.Geometry);
            }

            if (geometryDrawingState.HasBrush)
            {
                VisitPortableDependency(ref state, visitor, geometryDrawingState.Brush);
            }

            if (geometryDrawingState.HasPen)
            {
                VisitPortableDependency(ref state, visitor, geometryDrawingState.Pen);
            }
        }

        if (source is PortableImageDrawingStateSource imageDrawingSource
            && imageDrawingSource.TryGetPortableImageDrawingState(out var imageDrawingState)
            && imageDrawingState.HasImageSource)
        {
            VisitPortableDependency(ref state, visitor, imageDrawingState.ImageSource);
        }

        if (source is PortableGlyphRunDrawingStateSource glyphRunDrawingSource
            && glyphRunDrawingSource.TryGetPortableGlyphRunDrawingState(out var glyphRunDrawingState))
        {
            if (glyphRunDrawingState.HasGlyphRun)
            {
                VisitPortableDependency(ref state, visitor, glyphRunDrawingState.GlyphRun);
            }

            if (glyphRunDrawingState.HasForegroundBrush)
            {
                VisitPortableDependency(ref state, visitor, glyphRunDrawingState.ForegroundBrush);
            }
        }

        if (source is PortableDrawingGroupStateSource drawingGroupSource
            && drawingGroupSource.TryGetPortableDrawingGroupState(out var drawingGroupState))
        {
            if (drawingGroupState.HasTransform)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.Transform);
            }

            if (drawingGroupState.HasClipGeometry)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.ClipGeometry);
            }

            if (drawingGroupState.HasOpacityMask)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.OpacityMask);
            }

            if (drawingGroupState.HasGuidelineSet)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.GuidelineSet);
            }

            if (drawingGroupState.HasEffect)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.Effect);
            }

            if (drawingGroupState.HasBitmapEffect)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.BitmapEffect);
            }

            if (drawingGroupState.HasBitmapEffectInput)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.BitmapEffectInput);
            }

            if (drawingGroupState.HasCacheMode)
            {
                VisitPortableDependency(ref state, visitor, drawingGroupState.CacheMode);
            }

            VisitPortableDrawingGroupChildren(ref state, visitor, source, drawingGroupState);
        }

        if (source is PortableBitmapCacheBrushSource cacheBrushSource
            && cacheBrushSource.TryGetPortableBitmapCacheBrush(out var cacheBrush))
        {
            VisitPortableDependency(ref state, visitor, cacheBrush.InternalTarget);
            VisitPortableDependency(ref state, visitor, cacheBrush.BitmapCache);
        }

        if (source is PortableTileBrushSource tileBrushSource
            && tileBrushSource.TryGetPortableTileBrush(out var tileBrush))
        {
            VisitPortableDependency(ref state, visitor, tileBrush.Content);
        }

        if (source is PortableShaderEffectSource shaderEffectSource
            && shaderEffectSource.TryGetPortableShaderEffect(out var shaderEffect))
        {
            VisitPortableDependency(ref state, visitor, shaderEffect.PixelShader);
            var samplers = shaderEffect.Samplers;
            for (var i = 0; i < samplers.Length; i++)
            {
                var sampler = samplers[i];
                if (sampler.Kind == PortableShaderSamplerKind.Brush)
                {
                    VisitPortableDependency(ref state, visitor, sampler.Brush);
                }
                else if (sampler.Kind == PortableShaderSamplerKind.ImageSource)
                {
                    VisitPortableDependency(ref state, visitor, sampler.ImageSource);
                }
            }
        }
    }

    private static void VisitPortableDrawingGroupChildren<TState, TVisitor>(
        ref TState state,
        TVisitor visitor,
        object source,
        PortableDrawingGroupState drawingGroupState)
        where TVisitor : struct, IPortableDependencyVisitor<TState>
    {
        if (source is PortableDrawingGroupChildrenSource childrenSource
            && childrenSource.TryGetPortableDrawingGroupChildCount(out var count)
            && count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (childrenSource.TryGetPortableDrawingGroupChild(i, out var child))
                {
                    VisitPortableDependency(ref state, visitor, child);
                }
            }

            return;
        }

        var children = drawingGroupState.Children;
        for (var i = 0; i < children.Length; i++)
        {
            VisitPortableDependency(ref state, visitor, children[i]);
        }
    }

    private static void VisitPortableDependency<TState, TVisitor>(
        ref TState state,
        TVisitor visitor,
        object? dependency)
        where TVisitor : struct, IPortableDependencyVisitor<TState>
    {
        if (dependency != null)
        {
            visitor.Visit(ref state, dependency);
        }
    }

    private static void VisitCollectionItems<TState, TVisitor>(
        IEnumerable collection,
        ref TState state,
        TVisitor visitor)
        where TVisitor : struct, ICollectionItemVisitor<TState>
    {
        if (collection is IList list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                visitor.Visit(ref state, list[i]);
            }

            return;
        }

        if (collection is IReadOnlyList<object?> objectList)
        {
            for (var i = 0; i < objectList.Count; i++)
            {
                visitor.Visit(ref state, objectList[i]);
            }

            return;
        }

        var enumerator = collection.GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
                visitor.Visit(ref state, enumerator.Current);
            }
        }
        finally
        {
            if (enumerator is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private interface IPortableDependencyVisitor<TState>
    {
        void Visit(ref TState state, object? dependency);
    }

    private interface ICollectionItemVisitor<TState>
    {
        void Visit(ref TState state, object? item);
    }

    private readonly struct SubscribeDependencyState
    {
        public SubscribeDependencyState(WpfVisualInvalidationTracker tracker, HashSet<object> visited)
        {
            Tracker = tracker;
            Visited = visited;
        }

        public WpfVisualInvalidationTracker Tracker { get; }

        public HashSet<object> Visited { get; }
    }

    private readonly struct SubscribeDependencyVisitor :
        IPortableDependencyVisitor<SubscribeDependencyState>,
        ICollectionItemVisitor<SubscribeDependencyState>
    {
        public void Visit(ref SubscribeDependencyState state, object? dependency)
        {
            state.Tracker.SubscribeObject(dependency, state.Visited);
        }
    }

    private readonly struct CaptureVisualStateAndChildrenDependencyState
    {
        public CaptureVisualStateAndChildrenDependencyState(
            Dictionary<object, VisualStateSnapshot> snapshots,
            Dictionary<object, object?[]> previousChildren,
            HashSet<object> currentChildrenSources,
            List<object> changedSources,
            HashSet<object> visited)
        {
            Snapshots = snapshots;
            PreviousChildren = previousChildren;
            CurrentChildrenSources = currentChildrenSources;
            ChangedSources = changedSources;
            Visited = visited;
        }

        public Dictionary<object, VisualStateSnapshot> Snapshots { get; }

        public Dictionary<object, object?[]> PreviousChildren { get; }

        public HashSet<object> CurrentChildrenSources { get; }

        public List<object> ChangedSources { get; }

        public HashSet<object> Visited { get; }
    }

    private readonly struct CaptureVisualStateAndChildrenDependencyVisitor :
        IPortableDependencyVisitor<CaptureVisualStateAndChildrenDependencyState>,
        ICollectionItemVisitor<CaptureVisualStateAndChildrenDependencyState>
    {
        public void Visit(ref CaptureVisualStateAndChildrenDependencyState state, object? dependency)
        {
            CaptureObjectVisualStateAndChildren(
                dependency,
                state.Snapshots,
                state.PreviousChildren,
                state.CurrentChildrenSources,
                state.ChangedSources,
                state.Visited);
        }
    }

    private readonly struct CollectTrackedDependencyState
    {
        public CollectTrackedDependencyState(List<object> dependencies, HashSet<object> visited)
        {
            Dependencies = dependencies;
            Visited = visited;
        }

        public List<object> Dependencies { get; }

        public HashSet<object> Visited { get; }
    }

    private readonly struct CollectTrackedDependencyVisitor :
        IPortableDependencyVisitor<CollectTrackedDependencyState>,
        ICollectionItemVisitor<CollectTrackedDependencyState>
    {
        public void Visit(ref CollectTrackedDependencyState state, object? dependency)
        {
            CollectTrackedDependencies(dependency, state.Dependencies, state.Visited);
        }
    }

    private struct RegisterTrackedDependencyState
    {
        public RegisterTrackedDependencyState(IWpfRetainedVisualBranchSink sink, HashSet<object> visited)
        {
            Sink = sink;
            Visited = visited;
            Registered = false;
        }

        public IWpfRetainedVisualBranchSink Sink { get; }

        public HashSet<object> Visited { get; }

        public bool Registered { get; set; }
    }

    private readonly struct RegisterTrackedDependencyVisitor :
        IPortableDependencyVisitor<RegisterTrackedDependencyState>,
        ICollectionItemVisitor<RegisterTrackedDependencyState>
    {
        public void Visit(ref RegisterTrackedDependencyState state, object? dependency)
        {
            state.Registered |= RegisterTrackedDependencies(state.Sink, dependency, state.Visited);
        }
    }

    private static bool IsTerminalValue(object value)
    {
        return value is string
            or bool
            or byte
            or sbyte
            or short
            or ushort
            or int
            or uint
            or long
            or ulong
            or char
            or float
            or double
            or decimal
            or DateTime
            or TimeSpan
            or Guid
            or IntPtr
            or UIntPtr
            or Enum;
    }

    private readonly struct VisualStateSnapshot : IEquatable<VisualStateSnapshot>
    {
        public VisualStateSnapshot(
            bool hasOffset,
            double offsetX,
            double offsetY,
            bool hasClipProperty,
            object? clipReference,
            bool hasClipToBounds,
            bool clipToBounds,
            bool hasLayoutClipProperty,
            object? layoutClipReference,
            bool hasTransformProperty,
            object? transformReference,
            bool hasScrollableAreaClipProperty,
            bool hasScrollableAreaClipRect,
            double scrollClipX,
            double scrollClipY,
            double scrollClipWidth,
            double scrollClipHeight,
            bool hasOpacity,
            double opacity,
            bool hasOpacityMaskProperty,
            object? opacityMaskReference,
            bool hasEffectProperty,
            object? effectReference,
            bool hasBitmapEffectProperty,
            object? bitmapEffectReference,
            bool hasBitmapEffectInputProperty,
            object? bitmapEffectInputReference,
            bool hasCacheModeProperty,
            object? cacheModeReference,
            bool hasBitmapScalingMode,
            object? bitmapScalingMode,
            bool hasEdgeMode,
            object? edgeMode,
            bool hasClearTypeHint,
            object? clearTypeHint,
            bool hasTextRenderingMode,
            object? textRenderingMode,
            bool hasTextHintingMode,
            object? textHintingMode,
            bool hasSnappingGuidelinesX,
            double[]? snappingGuidelinesX,
            bool hasSnappingGuidelinesY,
            double[]? snappingGuidelinesY,
            bool hasRenderSize,
            double renderWidth,
            double renderHeight)
        {
            HasOffset = hasOffset;
            OffsetX = offsetX;
            OffsetY = offsetY;
            HasClipProperty = hasClipProperty;
            ClipReference = clipReference;
            HasClipToBounds = hasClipToBounds;
            ClipToBounds = clipToBounds;
            HasLayoutClipProperty = hasLayoutClipProperty;
            LayoutClipReference = layoutClipReference;
            HasTransformProperty = hasTransformProperty;
            TransformReference = transformReference;
            HasScrollableAreaClipProperty = hasScrollableAreaClipProperty;
            HasScrollableAreaClipRect = hasScrollableAreaClipRect;
            ScrollClipX = scrollClipX;
            ScrollClipY = scrollClipY;
            ScrollClipWidth = scrollClipWidth;
            ScrollClipHeight = scrollClipHeight;
            HasOpacity = hasOpacity;
            Opacity = opacity;
            HasOpacityMaskProperty = hasOpacityMaskProperty;
            OpacityMaskReference = opacityMaskReference;
            HasEffectProperty = hasEffectProperty;
            EffectReference = effectReference;
            HasBitmapEffectProperty = hasBitmapEffectProperty;
            BitmapEffectReference = bitmapEffectReference;
            HasBitmapEffectInputProperty = hasBitmapEffectInputProperty;
            BitmapEffectInputReference = bitmapEffectInputReference;
            HasCacheModeProperty = hasCacheModeProperty;
            CacheModeReference = cacheModeReference;
            HasBitmapScalingMode = hasBitmapScalingMode;
            BitmapScalingMode = bitmapScalingMode;
            HasEdgeMode = hasEdgeMode;
            EdgeMode = edgeMode;
            HasClearTypeHint = hasClearTypeHint;
            ClearTypeHint = clearTypeHint;
            HasTextRenderingMode = hasTextRenderingMode;
            TextRenderingMode = textRenderingMode;
            HasTextHintingMode = hasTextHintingMode;
            TextHintingMode = textHintingMode;
            HasSnappingGuidelinesX = hasSnappingGuidelinesX;
            SnappingGuidelinesX = snappingGuidelinesX ?? Array.Empty<double>();
            HasSnappingGuidelinesY = hasSnappingGuidelinesY;
            SnappingGuidelinesY = snappingGuidelinesY ?? Array.Empty<double>();
            HasRenderSize = hasRenderSize;
            RenderWidth = renderWidth;
            RenderHeight = renderHeight;
        }

        private bool HasOffset { get; }

        private double OffsetX { get; }

        private double OffsetY { get; }

        private bool HasClipProperty { get; }

        private object? ClipReference { get; }

        private bool HasClipToBounds { get; }

        private bool ClipToBounds { get; }

        private bool HasLayoutClipProperty { get; }

        private object? LayoutClipReference { get; }

        private bool HasTransformProperty { get; }

        private object? TransformReference { get; }

        private bool HasScrollableAreaClipProperty { get; }

        private bool HasScrollableAreaClipRect { get; }

        private double ScrollClipX { get; }

        private double ScrollClipY { get; }

        private double ScrollClipWidth { get; }

        private double ScrollClipHeight { get; }

        private bool HasOpacity { get; }

        private double Opacity { get; }

        private bool HasOpacityMaskProperty { get; }

        private object? OpacityMaskReference { get; }

        private bool HasEffectProperty { get; }

        private object? EffectReference { get; }

        private bool HasBitmapEffectProperty { get; }

        private object? BitmapEffectReference { get; }

        private bool HasBitmapEffectInputProperty { get; }

        private object? BitmapEffectInputReference { get; }

        private bool HasCacheModeProperty { get; }

        private object? CacheModeReference { get; }

        private bool HasBitmapScalingMode { get; }

        private object? BitmapScalingMode { get; }

        private bool HasEdgeMode { get; }

        private object? EdgeMode { get; }

        private bool HasClearTypeHint { get; }

        private object? ClearTypeHint { get; }

        private bool HasTextRenderingMode { get; }

        private object? TextRenderingMode { get; }

        private bool HasTextHintingMode { get; }

        private object? TextHintingMode { get; }

        private bool HasSnappingGuidelinesX { get; }

        private double[] SnappingGuidelinesX { get; }

        private bool HasSnappingGuidelinesY { get; }

        private double[] SnappingGuidelinesY { get; }

        private bool HasRenderSize { get; }

        private double RenderWidth { get; }

        private double RenderHeight { get; }

        public bool Equals(VisualStateSnapshot other)
        {
            return HasOffset == other.HasOffset &&
                OffsetX.Equals(other.OffsetX) &&
                OffsetY.Equals(other.OffsetY) &&
                HasClipProperty == other.HasClipProperty &&
                ReferenceEquals(ClipReference, other.ClipReference) &&
                HasClipToBounds == other.HasClipToBounds &&
                ClipToBounds == other.ClipToBounds &&
                HasLayoutClipProperty == other.HasLayoutClipProperty &&
                ReferenceEquals(LayoutClipReference, other.LayoutClipReference) &&
                HasTransformProperty == other.HasTransformProperty &&
                ReferenceEquals(TransformReference, other.TransformReference) &&
                HasScrollableAreaClipProperty == other.HasScrollableAreaClipProperty &&
                HasScrollableAreaClipRect == other.HasScrollableAreaClipRect &&
                ScrollClipX.Equals(other.ScrollClipX) &&
                ScrollClipY.Equals(other.ScrollClipY) &&
                ScrollClipWidth.Equals(other.ScrollClipWidth) &&
                ScrollClipHeight.Equals(other.ScrollClipHeight) &&
                HasOpacity == other.HasOpacity &&
                Opacity.Equals(other.Opacity) &&
                HasOpacityMaskProperty == other.HasOpacityMaskProperty &&
                ReferenceEquals(OpacityMaskReference, other.OpacityMaskReference) &&
                HasEffectProperty == other.HasEffectProperty &&
                ReferenceEquals(EffectReference, other.EffectReference) &&
                HasBitmapEffectProperty == other.HasBitmapEffectProperty &&
                ReferenceEquals(BitmapEffectReference, other.BitmapEffectReference) &&
                HasBitmapEffectInputProperty == other.HasBitmapEffectInputProperty &&
                ReferenceEquals(BitmapEffectInputReference, other.BitmapEffectInputReference) &&
                HasCacheModeProperty == other.HasCacheModeProperty &&
                ReferenceEquals(CacheModeReference, other.CacheModeReference) &&
                HasBitmapScalingMode == other.HasBitmapScalingMode &&
                Equals(BitmapScalingMode, other.BitmapScalingMode) &&
                HasEdgeMode == other.HasEdgeMode &&
                Equals(EdgeMode, other.EdgeMode) &&
                HasClearTypeHint == other.HasClearTypeHint &&
                Equals(ClearTypeHint, other.ClearTypeHint) &&
                HasTextRenderingMode == other.HasTextRenderingMode &&
                Equals(TextRenderingMode, other.TextRenderingMode) &&
                HasTextHintingMode == other.HasTextHintingMode &&
                Equals(TextHintingMode, other.TextHintingMode) &&
                HasSnappingGuidelinesX == other.HasSnappingGuidelinesX &&
                DoubleArraysEqual(SnappingGuidelinesX, other.SnappingGuidelinesX) &&
                HasSnappingGuidelinesY == other.HasSnappingGuidelinesY &&
                DoubleArraysEqual(SnappingGuidelinesY, other.SnappingGuidelinesY) &&
                HasRenderSize == other.HasRenderSize &&
                RenderWidth.Equals(other.RenderWidth) &&
                RenderHeight.Equals(other.RenderHeight);
        }

        public override bool Equals(object? obj)
        {
            return obj is VisualStateSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(HasOffset);
            hash.Add(OffsetX);
            hash.Add(OffsetY);
            hash.Add(HasClipProperty);
            hash.Add(GetReferenceHashCode(ClipReference));
            hash.Add(HasClipToBounds);
            hash.Add(ClipToBounds);
            hash.Add(HasLayoutClipProperty);
            hash.Add(GetReferenceHashCode(LayoutClipReference));
            hash.Add(HasTransformProperty);
            hash.Add(GetReferenceHashCode(TransformReference));
            hash.Add(HasScrollableAreaClipProperty);
            hash.Add(HasScrollableAreaClipRect);
            hash.Add(ScrollClipX);
            hash.Add(ScrollClipY);
            hash.Add(ScrollClipWidth);
            hash.Add(ScrollClipHeight);
            hash.Add(HasOpacity);
            hash.Add(Opacity);
            hash.Add(HasOpacityMaskProperty);
            hash.Add(GetReferenceHashCode(OpacityMaskReference));
            hash.Add(HasEffectProperty);
            hash.Add(GetReferenceHashCode(EffectReference));
            hash.Add(HasBitmapEffectProperty);
            hash.Add(GetReferenceHashCode(BitmapEffectReference));
            hash.Add(HasBitmapEffectInputProperty);
            hash.Add(GetReferenceHashCode(BitmapEffectInputReference));
            hash.Add(HasCacheModeProperty);
            hash.Add(GetReferenceHashCode(CacheModeReference));
            hash.Add(HasBitmapScalingMode);
            hash.Add(BitmapScalingMode);
            hash.Add(HasEdgeMode);
            hash.Add(EdgeMode);
            hash.Add(HasClearTypeHint);
            hash.Add(ClearTypeHint);
            hash.Add(HasTextRenderingMode);
            hash.Add(TextRenderingMode);
            hash.Add(HasTextHintingMode);
            hash.Add(TextHintingMode);
            hash.Add(HasSnappingGuidelinesX);
            AddDoubleArrayHash(ref hash, SnappingGuidelinesX);
            hash.Add(HasSnappingGuidelinesY);
            AddDoubleArrayHash(ref hash, SnappingGuidelinesY);
            hash.Add(HasRenderSize);
            hash.Add(RenderWidth);
            hash.Add(RenderHeight);
            return hash.ToHashCode();
        }

        private static bool DoubleArraysEqual(double[] left, double[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddDoubleArrayHash(ref HashCode hash, double[] values)
        {
            hash.Add(values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                hash.Add(values[i]);
            }
        }

        private static int GetReferenceHashCode(object? value)
        {
            return value == null ? 0 : RuntimeHelpers.GetHashCode(value);
        }
    }

    private struct VisualStateSnapshotBuilder
    {
        private bool _hasOffset;
        private double _offsetX;
        private double _offsetY;
        private bool _hasClipProperty;
        private object? _clipReference;
        private bool _hasClipToBounds;
        private bool _clipToBounds;
        private bool _hasLayoutClipProperty;
        private object? _layoutClipReference;
        private bool _hasTransformProperty;
        private object? _transformReference;
        private bool _hasScrollableAreaClipProperty;
        private bool _hasScrollableAreaClipRect;
        private double _scrollClipX;
        private double _scrollClipY;
        private double _scrollClipWidth;
        private double _scrollClipHeight;
        private bool _hasOpacity;
        private double _opacity;
        private bool _hasOpacityMaskProperty;
        private object? _opacityMaskReference;
        private bool _hasEffectProperty;
        private object? _effectReference;
        private bool _hasBitmapEffectProperty;
        private object? _bitmapEffectReference;
        private bool _hasBitmapEffectInputProperty;
        private object? _bitmapEffectInputReference;
        private bool _hasCacheModeProperty;
        private object? _cacheModeReference;
        private bool _hasBitmapScalingMode;
        private object? _bitmapScalingMode;
        private bool _hasEdgeMode;
        private object? _edgeMode;
        private bool _hasClearTypeHint;
        private object? _clearTypeHint;
        private bool _hasTextRenderingMode;
        private object? _textRenderingMode;
        private bool _hasTextHintingMode;
        private object? _textHintingMode;
        private bool _hasSnappingGuidelinesX;
        private double[]? _snappingGuidelinesX;
        private bool _hasSnappingGuidelinesY;
        private double[]? _snappingGuidelinesY;
        private bool _hasRenderSize;
        private double _renderWidth;
        private double _renderHeight;

        public bool HasState { get; private set; }

        public void SetOffset(double x, double y)
        {
            HasState = true;
            _hasOffset = true;
            _offsetX = x;
            _offsetY = y;
        }

        public void SetClip(object? clip)
        {
            HasState = true;
            _hasClipProperty = true;
            _clipReference = clip;
        }

        public void SetClipToBounds(bool clipToBounds)
        {
            HasState = true;
            _hasClipToBounds = true;
            _clipToBounds = clipToBounds;
        }

        public void SetLayoutClip(object? clip)
        {
            HasState = true;
            _hasLayoutClipProperty = true;
            _layoutClipReference = clip;
        }

        public void SetTransform(object? transform)
        {
            HasState = true;
            _hasTransformProperty = true;
            _transformReference = transform;
        }

        public void SetScrollableAreaClip(double x, double y, double width, double height)
        {
            HasState = true;
            _hasScrollableAreaClipProperty = true;
            _hasScrollableAreaClipRect = true;
            _scrollClipX = x;
            _scrollClipY = y;
            _scrollClipWidth = width;
            _scrollClipHeight = height;
        }

        public void SetOpacity(double opacity)
        {
            HasState = true;
            _hasOpacity = true;
            _opacity = opacity;
        }

        public void SetOpacityMask(object? opacityMask)
        {
            HasState = true;
            _hasOpacityMaskProperty = true;
            _opacityMaskReference = opacityMask;
        }

        public void SetEffect(object? effect)
        {
            HasState = true;
            _hasEffectProperty = true;
            _effectReference = effect;
        }

        public void SetBitmapEffect(object? bitmapEffect)
        {
            HasState = true;
            _hasBitmapEffectProperty = true;
            _bitmapEffectReference = bitmapEffect;
        }

        public void SetBitmapEffectInput(object? bitmapEffectInput)
        {
            HasState = true;
            _hasBitmapEffectInputProperty = true;
            _bitmapEffectInputReference = bitmapEffectInput;
        }

        public void SetCacheMode(object? cacheMode)
        {
            HasState = true;
            _hasCacheModeProperty = true;
            _cacheModeReference = cacheMode;
        }

        public void SetBitmapScalingMode(object? bitmapScalingMode)
        {
            HasState = true;
            _hasBitmapScalingMode = true;
            _bitmapScalingMode = bitmapScalingMode;
        }

        public void SetEdgeMode(object? edgeMode)
        {
            HasState = true;
            _hasEdgeMode = true;
            _edgeMode = edgeMode;
        }

        public void SetClearTypeHint(object? clearTypeHint)
        {
            HasState = true;
            _hasClearTypeHint = true;
            _clearTypeHint = clearTypeHint;
        }

        public void SetTextRenderingMode(object? textRenderingMode)
        {
            HasState = true;
            _hasTextRenderingMode = true;
            _textRenderingMode = textRenderingMode;
        }

        public void SetTextHintingMode(object? textHintingMode)
        {
            HasState = true;
            _hasTextHintingMode = true;
            _textHintingMode = textHintingMode;
        }

        public void SetSnappingGuidelinesX(double[]? guidelines)
        {
            HasState = true;
            _hasSnappingGuidelinesX = true;
            _snappingGuidelinesX = guidelines ?? Array.Empty<double>();
        }

        public void SetSnappingGuidelinesY(double[]? guidelines)
        {
            HasState = true;
            _hasSnappingGuidelinesY = true;
            _snappingGuidelinesY = guidelines ?? Array.Empty<double>();
        }

        public void SetRenderSize(double width, double height)
        {
            HasState = true;
            _hasRenderSize = true;
            _renderWidth = width;
            _renderHeight = height;
        }

        public readonly VisualStateSnapshot ToSnapshot()
        {
            return new VisualStateSnapshot(
                _hasOffset,
                _offsetX,
                _offsetY,
                _hasClipProperty,
                _clipReference,
                _hasClipToBounds,
                _clipToBounds,
                _hasLayoutClipProperty,
                _layoutClipReference,
                _hasTransformProperty,
                _transformReference,
                _hasScrollableAreaClipProperty,
                _hasScrollableAreaClipRect,
                _scrollClipX,
                _scrollClipY,
                _scrollClipWidth,
                _scrollClipHeight,
                _hasOpacity,
                _opacity,
                _hasOpacityMaskProperty,
                _opacityMaskReference,
                _hasEffectProperty,
                _effectReference,
                _hasBitmapEffectProperty,
                _bitmapEffectReference,
                _hasBitmapEffectInputProperty,
                _bitmapEffectInputReference,
                _hasCacheModeProperty,
                _cacheModeReference,
                _hasBitmapScalingMode,
                _bitmapScalingMode,
                _hasEdgeMode,
                _edgeMode,
                _hasClearTypeHint,
                _clearTypeHint,
                _hasTextRenderingMode,
                _textRenderingMode,
                _hasTextHintingMode,
                _textHintingMode,
                _hasSnappingGuidelinesX,
                _snappingGuidelinesX,
                _hasSnappingGuidelinesY,
                _snappingGuidelinesY,
                _hasRenderSize,
                _renderWidth,
                _renderHeight);
        }
    }

    private void ClearSubscriptions()
    {
        for (var i = _subscriptions.Count - 1; i >= 0; i--)
        {
            _subscriptions[i].Dispose();
        }

        _subscriptions.Clear();
    }

    private readonly struct InvalidationSubscription
    {
        private readonly IDisposable? _disposable;
        private readonly INotifyPropertyChanged? _propertyChanged;
        private readonly PropertyChangedEventHandler? _propertyChangedHandler;
        private readonly INotifyCollectionChanged? _collectionChanged;
        private readonly NotifyCollectionChangedEventHandler? _collectionChangedHandler;

        private InvalidationSubscription(
            IDisposable? disposable,
            INotifyPropertyChanged? propertyChanged,
            PropertyChangedEventHandler? propertyChangedHandler,
            INotifyCollectionChanged? collectionChanged,
            NotifyCollectionChangedEventHandler? collectionChangedHandler)
        {
            _disposable = disposable;
            _propertyChanged = propertyChanged;
            _propertyChangedHandler = propertyChangedHandler;
            _collectionChanged = collectionChanged;
            _collectionChangedHandler = collectionChangedHandler;
        }

        public static InvalidationSubscription ForDisposable(IDisposable disposable)
        {
            ArgumentNullException.ThrowIfNull(disposable);
            return new InvalidationSubscription(disposable, null, null, null, null);
        }

        public static InvalidationSubscription ForPropertyChanged(
            INotifyPropertyChanged source,
            PropertyChangedEventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(handler);
            return new InvalidationSubscription(null, source, handler, null, null);
        }

        public static InvalidationSubscription ForCollectionChanged(
            INotifyCollectionChanged source,
            NotifyCollectionChangedEventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(handler);
            return new InvalidationSubscription(null, null, null, source, handler);
        }

        public void Dispose()
        {
            if (_disposable != null)
            {
                TryDisposeInvalidationSubscription(_disposable);
                return;
            }

            if (_propertyChanged != null && _propertyChangedHandler != null)
            {
                TryUnsubscribePropertyChanged(_propertyChanged, _propertyChangedHandler);
                return;
            }

            if (_collectionChanged != null && _collectionChangedHandler != null)
            {
                TryUnsubscribeCollectionChanged(_collectionChanged, _collectionChangedHandler);
            }
        }
    }

    private sealed class SourceInvalidationHandler
    {
        private readonly WpfVisualInvalidationTracker _tracker;
        private readonly object _source;

        public SourceInvalidationHandler(WpfVisualInvalidationTracker tracker, object source)
        {
            _tracker = tracker;
            _source = source;
        }

        public void OnInvalidated(object? sender, EventArgs e)
        {
            _tracker.MarkDirtyAndRefresh(_source);
        }
    }

    private sealed class ProGpuTextureInvalidationSubscription : IDisposable
    {
        private IProGpuInvalidatingTextureSource? _source;
        private EventHandler? _handler;

        public ProGpuTextureInvalidationSubscription(
            IProGpuInvalidatingTextureSource source,
            EventHandler handler)
        {
            _source = source;
            _handler = handler;
        }

        public void Dispose()
        {
            var source = Interlocked.Exchange(ref _source, null);
            var handler = Interlocked.Exchange(ref _handler, null);
            if (source != null && handler != null)
            {
                source.TextureChanged -= handler;
            }
        }
    }

    private sealed class PropertyChangedInvalidationHandler
    {
        private readonly WpfVisualInvalidationTracker _tracker;
        private readonly object _source;

        public PropertyChangedInvalidationHandler(WpfVisualInvalidationTracker tracker, object source)
        {
            _tracker = tracker;
            _source = source;
        }

        public void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _tracker.MarkDirtyAndRefresh(_source);
        }
    }

    private sealed class CollectionChangedInvalidationHandler
    {
        private readonly WpfVisualInvalidationTracker _tracker;
        private readonly object _source;

        public CollectionChangedInvalidationHandler(WpfVisualInvalidationTracker tracker, object source)
        {
            _tracker = tracker;
            _source = source;
        }

        public void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _tracker.MarkDirtyAndRefresh(_source);
        }
    }
}
