using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using MediaBrush = System.Windows.Media.Brush;
using ProGpuVisual = global::ProGPU.Scene.Visual;

namespace System.Windows.Media.ProGPU.Composition;

public sealed class WpfRetainedVisualBranchMap
{
    private readonly Dictionary<object, VisualSet> _visualsBySource = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _sourcesByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _sourceOwnersByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProGpuVisual, ReferenceOwnerSet> _dependenciesByVisual = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _scratchDistinctSources = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProGpuVisual> _scratchVisitedVisuals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProGpuVisual> _scratchInvalidatedVisuals = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ProGpuVisual> _scratchTargetVisuals = new(ReferenceEqualityComparer.Instance);
    private readonly ReplayTargetList _scratchReplayTargets = new();
    private readonly ReplayTargetList _scratchTopLevelReplayTargets = new();
    private readonly SingleReplayTargetList _scratchSingleReplayTarget = new();

    public int SourceCount => _visualsBySource.Count;

    public int VisualCount { get; private set; }

    public object? LastSource { get; private set; }

    public ProGpuVisual? LastVisual { get; private set; }

    public IReadOnlyCollection<object> Sources => _visualsBySource.Keys;

    public void Clear()
    {
        _visualsBySource.Clear();
        _sourcesByVisual.Clear();
        _sourceOwnersByVisual.Clear();
        _dependenciesByVisual.Clear();
        _scratchDistinctSources.Clear();
        _scratchVisitedVisuals.Clear();
        _scratchInvalidatedVisuals.Clear();
        _scratchTargetVisuals.Clear();
        _scratchReplayTargets.Clear();
        _scratchTopLevelReplayTargets.Clear();
        _scratchSingleReplayTarget.Clear();
        VisualCount = 0;
        LastSource = null;
        LastVisual = null;
    }

    public void Register(object? source, ProGpuVisual? visual)
    {
        RegisterCore(source, visual, WpfRetainedVisualBranchOwnerKind.SourceOwner);
    }

    public void RegisterDependency(object? dependency, ProGpuVisual? visual)
    {
        RegisterCore(dependency, visual, WpfRetainedVisualBranchOwnerKind.Dependency);
    }

    private void RegisterCore(
        object? source,
        ProGpuVisual? visual,
        WpfRetainedVisualBranchOwnerKind ownerKind)
    {
        if (source == null || visual == null)
        {
            return;
        }

        ref var sources = ref CollectionsMarshal.GetValueRefOrAddDefault(
            _sourcesByVisual,
            visual,
            out var hasSources);
        if (hasSources && sources.Contains(source))
        {
            RegisterOwnerKind(source, visual, ownerKind);
            LastSource = source;
            LastVisual = visual;
            return;
        }

        ref var visuals = ref CollectionsMarshal.GetValueRefOrAddDefault(_visualsBySource, source, out _);
        visuals.Add(visual);
        sources.Add(source);
        RegisterOwnerKind(source, visual, ownerKind);
        VisualCount++;
        LastSource = source;
        LastVisual = visual;
    }

    public bool TryGetVisuals(object source, out IReadOnlyList<ProGpuVisual> visuals)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_visualsBySource.TryGetValue(source, out var mappedVisuals) &&
            mappedVisuals.Count > 0)
        {
            visuals = mappedVisuals;
            return true;
        }

        visuals = Array.Empty<ProGpuVisual>();
        return false;
    }

    public int InvalidateVisuals(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            return 0;
        }

        var count = visuals.Count;
        for (var i = 0; i < count; i++)
        {
            visuals[i].Invalidate();
        }

        return count;
    }

    public int InvalidateVisuals(IEnumerable<object> sources)
    {
        return InvalidateVisualsForSources(sources, singleSourceHint: null).InvalidatedVisualCount;
    }

    public IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSources(IEnumerable<object> sources)
    {
        return GetReplayTargetsForSources(sources, singleSourceHint: null);
    }

    public IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSources(
        IEnumerable<object> sources,
        object? singleSourceHint)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (TryGetReferenceEqualityHashSet(sources, out var referenceDirtySources))
        {
            return GetReplayTargetsForReferenceSourceSet(referenceDirtySources, singleSourceHint);
        }

        if (sources is IReadOnlyCollection<object> sourceCollection)
        {
            if (sourceCollection.Count == 0)
            {
                return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
            }

            if (sourceCollection.Count == 1 &&
                TryGetSingleSource(sourceCollection, out var singleSource))
            {
                return GetReplayTargetsForSingleSource(singleSource);
            }
        }

        _scratchDistinctSources.Clear();
        try
        {
            AddDistinctSources(sources, _scratchDistinctSources);
            return GetReplayTargetsForDistinctSourceSet(_scratchDistinctSources);
        }
        finally
        {
            _scratchDistinctSources.Clear();
        }
    }

    internal IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForReferenceSources(
        HashSet<object> sources,
        object? singleSourceHint)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return GetReplayTargetsForReferenceSourceSet(sources, singleSourceHint);
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForReferenceSourceSet(
        HashSet<object> dirtySources,
        object? singleSourceHint)
    {
        if (dirtySources.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        if (dirtySources.Count == 1 &&
            TryGetSingleSource(dirtySources, singleSourceHint, out var singleSource))
        {
            return GetReplayTargetsForSingleSource(singleSource);
        }

        return GetReplayTargetsForDistinctSourceSet(dirtySources);
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForDistinctSourceSet(
        HashSet<object> dirtySources)
    {
        if (dirtySources.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        _scratchReplayTargets.Clear();
        _scratchTopLevelReplayTargets.Clear();
        _scratchVisitedVisuals.Clear();
        try
        {
            var dirtySourceEnumerator = dirtySources.GetEnumerator();
            while (dirtySourceEnumerator.MoveNext())
            {
                var source = dirtySourceEnumerator.Current;
                if (!_visualsBySource.TryGetValue(source, out var visuals))
                {
                    _scratchReplayTargets.Clear();
                    return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                }

                var visualCount = visuals.Count;
                for (var i = 0; i < visualCount; i++)
                {
                    var visual = visuals[i];
                    if (!TryResolveReplayTargetForVisual(
                            visual,
                            out var replaySource,
                            out var replayVisual))
                    {
                        _scratchReplayTargets.Clear();
                        return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                    }

                    if (_scratchVisitedVisuals.Add(replayVisual))
                    {
                        _scratchReplayTargets.Add(
                            new WpfRetainedVisualBranchReplayTarget(
                                replaySource,
                                replayVisual));
                    }
                }
            }

            return _scratchReplayTargets.Count <= 1
                ? ReturnReplayTargets(_scratchReplayTargets)
                : SelectTopLevelReplayTargets(_scratchReplayTargets);
        }
        finally
        {
            _scratchVisitedVisuals.Clear();
        }
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> GetReplayTargetsForSingleSource(object source)
    {
        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        if (visuals.Count == 1)
        {
            var visual = visuals[0];
            return TryResolveReplayTargetForVisual(
                    visual,
                    out var replaySource,
                    out var replayVisual)
                ? CreateSingleReplayTarget(replaySource, replayVisual)
                : Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        _scratchReplayTargets.Clear();
        _scratchTopLevelReplayTargets.Clear();
        _scratchVisitedVisuals.Clear();
        try
        {
            var visualCount = visuals.Count;
            for (var i = 0; i < visualCount; i++)
            {
                var visual = visuals[i];
                if (!TryResolveReplayTargetForVisual(
                        visual,
                        out var replaySource,
                        out var replayVisual))
                {
                    _scratchReplayTargets.Clear();
                    return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
                }

                if (_scratchVisitedVisuals.Add(replayVisual))
                {
                    _scratchReplayTargets.Add(
                        new WpfRetainedVisualBranchReplayTarget(
                            replaySource,
                            replayVisual));
                }
            }

            return _scratchReplayTargets.Count <= 1
                ? ReturnReplayTargets(_scratchReplayTargets)
                : SelectTopLevelReplayTargets(_scratchReplayTargets);
        }
        finally
        {
            _scratchVisitedVisuals.Clear();
        }
    }

    private static bool TryGetSingleSource(
        IReadOnlyCollection<object> sources,
        out object source)
    {
        if (sources is IList<object> list)
        {
            if (list.Count == 0)
            {
                source = null!;
                return false;
            }

            source = list[0];
            return true;
        }

        if (sources is IReadOnlyList<object> readOnlyList)
        {
            if (readOnlyList.Count == 0)
            {
                source = null!;
                return false;
            }

            source = readOnlyList[0];
            return true;
        }

        using var sourceEnumerator = sources.GetEnumerator();
        if (sourceEnumerator.MoveNext())
        {
            source = sourceEnumerator.Current;
            return true;
        }

        source = null!;
        return false;
    }

    private static bool TryGetSingleSource(
        HashSet<object> sources,
        object? singleSourceHint,
        out object source)
    {
        if (singleSourceHint != null && sources.Contains(singleSourceHint))
        {
            source = singleSourceHint;
            return true;
        }

        return TryGetSingleSource(sources, out source);
    }

    private static bool TryGetSingleSource(
        HashSet<object> sources,
        out object source)
    {
        var sourceEnumerator = sources.GetEnumerator();
        if (sourceEnumerator.MoveNext())
        {
            source = sourceEnumerator.Current;
            return true;
        }

        source = null!;
        return false;
    }

    private static void AddDistinctSources(
        IEnumerable<object> sources,
        HashSet<object> distinctSources)
    {
        if (sources is IList<object> list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                distinctSources.Add(list[i]);
            }

            return;
        }

        if (sources is IReadOnlyList<object> readOnlyList)
        {
            for (var i = 0; i < readOnlyList.Count; i++)
            {
                distinctSources.Add(readOnlyList[i]);
            }

            return;
        }

        var sourceEnumerator = sources.GetEnumerator();
        while (sourceEnumerator.MoveNext())
        {
            var source = sourceEnumerator.Current;
            distinctSources.Add(source);
        }
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> CreateSingleReplayTarget(
        object source,
        ProGpuVisual visual)
    {
        _scratchSingleReplayTarget.Set(new WpfRetainedVisualBranchReplayTarget(source, visual));
        return _scratchSingleReplayTarget;
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> ReturnReplayTargets(
        ReplayTargetList targets)
    {
        if (targets.Count == 0)
        {
            return Array.Empty<WpfRetainedVisualBranchReplayTarget>();
        }

        if (targets.Count == 1)
        {
            _scratchSingleReplayTarget.Set(targets[0]);
            targets.Clear();
            return _scratchSingleReplayTarget;
        }

        return targets;
    }

    private IReadOnlyList<WpfRetainedVisualBranchReplayTarget> SelectTopLevelReplayTargets(
        ReplayTargetList targets)
    {
        _scratchTopLevelReplayTargets.Clear();
        _scratchTargetVisuals.Clear();
        try
        {
            var targetCount = targets.Count;
            for (var i = 0; i < targetCount; i++)
            {
                var target = targets[i];
                _scratchTargetVisuals.Add(target.Visual);
            }

            for (var i = 0; i < targetCount; i++)
            {
                var target = targets[i];
                if (!IsCoveredByTargetAncestor(target.Visual, _scratchTargetVisuals))
                {
                    _scratchTopLevelReplayTargets.Add(target);
                }
            }

            var result = ReturnReplayTargets(_scratchTopLevelReplayTargets);
            _scratchReplayTargets.Clear();
            return result;
        }
        finally
        {
            _scratchTargetVisuals.Clear();
        }
    }

    public void UnregisterVisualTree(ProGpuVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        UnregisterVisualTreeCore(visual);
        LastSource = null;
        LastVisual = null;
    }

    public WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSources(IEnumerable<object> sources)
    {
        return InvalidateVisualsForSources(sources, singleSourceHint: null);
    }

    public WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSources(
        IEnumerable<object> sources,
        object? singleSourceHint)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (TryGetReferenceEqualityHashSet(sources, out var referenceVisitedSources))
        {
            return InvalidateVisualsForReferenceSourceSet(referenceVisitedSources, singleSourceHint);
        }

        if (sources is IReadOnlyCollection<object> sourceCollection)
        {
            if (sourceCollection.Count == 0)
            {
                return new WpfRetainedVisualBranchInvalidationResult(0, 0, 0);
            }

            if (sourceCollection.Count == 1 &&
                TryGetSingleSource(sourceCollection, out var singleSource))
            {
                return InvalidateVisualsForSingleSource(singleSource);
            }
        }

        _scratchDistinctSources.Clear();
        try
        {
            AddDistinctSources(sources, _scratchDistinctSources);
            return InvalidateVisualsForDistinctSourceSet(_scratchDistinctSources);
        }
        finally
        {
            _scratchDistinctSources.Clear();
        }
    }

    internal WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForReferenceSources(
        HashSet<object> sources,
        object? singleSourceHint)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return InvalidateVisualsForReferenceSourceSet(sources, singleSourceHint);
    }

    private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForReferenceSourceSet(
        HashSet<object> visitedSources,
        object? singleSourceHint)
    {
        if (visitedSources.Count == 0)
        {
            return new WpfRetainedVisualBranchInvalidationResult(0, 0, 0);
        }

        if (visitedSources.Count == 1 &&
            TryGetSingleSource(visitedSources, singleSourceHint, out var singleSource))
        {
            return InvalidateVisualsForSingleSource(singleSource);
        }

        return InvalidateVisualsForDistinctSourceSet(visitedSources);
    }

    private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForSingleSource(object source)
    {
        if (!_visualsBySource.TryGetValue(source, out var visuals))
        {
            return new WpfRetainedVisualBranchInvalidationResult(1, 0, 0);
        }

        var invalidatedVisualCount = 0;
        var sharedWithCleanSourceVisualCount = 0;
        var replayTargetConflictCount = 0;

        var visualCount = visuals.Count;
        for (var i = 0; i < visualCount; i++)
        {
            var visual = visuals[i];
            visual.Invalidate();
            invalidatedVisualCount++;

            if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners))
            {
                if (!TryResolveReplayTargetForVisual(visual, out _, out _))
                {
                    replayTargetConflictCount++;
                }

                continue;
            }

            if (sourceOwners.Count == 1)
            {
                continue;
            }

            if (!TryResolveReplayTargetForVisual(visual, out _, out _))
            {
                replayTargetConflictCount++;
            }

            sourceOwners.ClassifyAgainst(
                source,
                out var hasDirtySourceOwner,
                out var hasCleanSourceOwner);

            if (hasDirtySourceOwner && hasCleanSourceOwner)
            {
                sharedWithCleanSourceVisualCount++;
            }
        }

        return new WpfRetainedVisualBranchInvalidationResult(
            1,
            1,
            invalidatedVisualCount,
            sharedWithCleanSourceVisualCount,
            replayTargetConflictCount);
    }

    private WpfRetainedVisualBranchInvalidationResult InvalidateVisualsForDistinctSourceSet(
        HashSet<object> visitedSources)
    {
        if (visitedSources.Count == 0)
        {
            return new WpfRetainedVisualBranchInvalidationResult(0, 0, 0);
        }

        _scratchInvalidatedVisuals.Clear();
        var mappedSourceCount = 0;
        var sharedWithCleanSourceVisualCount = 0;
        var replayTargetConflictCount = 0;
        try
        {
            var visitedSourceEnumerator = visitedSources.GetEnumerator();
            while (visitedSourceEnumerator.MoveNext())
            {
                var source = visitedSourceEnumerator.Current;
                if (!_visualsBySource.TryGetValue(source, out var visuals))
                {
                    continue;
                }

                mappedSourceCount++;

                var visualCount = visuals.Count;
                for (var i = 0; i < visualCount; i++)
                {
                    var visual = visuals[i];
                    if (_scratchInvalidatedVisuals.Add(visual))
                    {
                        visual.Invalidate();
                    }
                }
            }

            var invalidatedVisualEnumerator = _scratchInvalidatedVisuals.GetEnumerator();
            while (invalidatedVisualEnumerator.MoveNext())
            {
                var visual = invalidatedVisualEnumerator.Current;
                if (!_sourceOwnersByVisual.TryGetValue(visual, out var sourceOwners))
                {
                    if (!TryResolveReplayTargetForVisual(visual, out _, out _))
                    {
                        replayTargetConflictCount++;
                    }

                    continue;
                }

                if (sourceOwners.Count == 1)
                {
                    continue;
                }

                if (!TryResolveReplayTargetForVisual(visual, out _, out _))
                {
                    replayTargetConflictCount++;
                }

                sourceOwners.ClassifyAgainst(
                    visitedSources,
                    out var hasDirtySourceOwner,
                    out var hasCleanSourceOwner);
                if (!hasDirtySourceOwner)
                {
                    continue;
                }

                if (hasCleanSourceOwner)
                {
                    sharedWithCleanSourceVisualCount++;
                }
            }

            return new WpfRetainedVisualBranchInvalidationResult(
                visitedSources.Count,
                mappedSourceCount,
                _scratchInvalidatedVisuals.Count,
                sharedWithCleanSourceVisualCount,
                replayTargetConflictCount);
        }
        finally
        {
            _scratchInvalidatedVisuals.Clear();
        }
    }

    private static bool TryGetReferenceEqualityHashSet(
        IEnumerable<object> sources,
        out HashSet<object> sourceSet)
    {
        if (sources is HashSet<object> candidate &&
            ReferenceEquals(candidate.Comparer, ReferenceEqualityComparer.Instance))
        {
            sourceSet = candidate;
            return true;
        }

        sourceSet = null!;
        return false;
    }

    private void UnregisterVisualTreeCore(ProGpuVisual visual)
    {
        if (_sourcesByVisual.Remove(visual, out var sources))
        {
            var sourceEnumerator = sources.GetEnumerator();
            while (sourceEnumerator.MoveNext())
            {
                var source = sourceEnumerator.Current;
                if (_visualsBySource.TryGetValue(source, out var visuals)
                    && RemoveVisualForSource(source, visual, visuals))
                {
                    VisualCount--;
                }
            }
        }

        _sourceOwnersByVisual.Remove(visual);
        _dependenciesByVisual.Remove(visual);

        if (visual is global::ProGPU.Scene.ContainerVisual containerVisual)
        {
            var children = containerVisual.Children;
            for (var i = 0; i < children.Count; i++)
            {
                UnregisterVisualTreeCore(children[i]);
            }
        }
    }

    private bool RemoveVisualForSource(
        object source,
        ProGpuVisual visual,
        VisualSet visuals)
    {
        if (visuals.Count == 1)
        {
            if (!ReferenceEquals(visuals[0], visual))
            {
                return false;
            }

            _visualsBySource.Remove(source);
            return true;
        }

        if (!visuals.Remove(visual))
        {
            return false;
        }

        if (visuals.Count == 0)
        {
            _visualsBySource.Remove(source);
            return true;
        }

        _visualsBySource[source] = visuals;
        return true;
    }

    private void RegisterOwnerKind(
        object source,
        ProGpuVisual visual,
        WpfRetainedVisualBranchOwnerKind ownerKind)
    {
        var ownersByVisual = ownerKind == WpfRetainedVisualBranchOwnerKind.SourceOwner
            ? _sourceOwnersByVisual
            : _dependenciesByVisual;
        ref var owners = ref CollectionsMarshal.GetValueRefOrAddDefault(ownersByVisual, visual, out _);
        owners.Add(source);
    }

    private bool TryResolveReplayTargetForVisual(
        ProGpuVisual visual,
        out object replaySource,
        out ProGpuVisual replayVisual)
    {
        replaySource = null!;
        replayVisual = null!;

        for (ProGpuVisual? current = visual;
             current != null;
             current = current.Parent)
        {
            if (_sourceOwnersByVisual.TryGetValue(current, out var sourceOwners) &&
                sourceOwners.TryGetSingle(out replaySource))
            {
                replayVisual = current;
                return true;
            }
        }

        return false;
    }

    private static bool IsCoveredByTargetAncestor(
        ProGpuVisual visual,
        HashSet<ProGpuVisual> targetVisuals)
    {
        for (var current = visual.Parent; current != null; current = current.Parent)
        {
            if (targetVisuals.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class SingleReplayTargetList : IReadOnlyList<WpfRetainedVisualBranchReplayTarget>
    {
        private WpfRetainedVisualBranchReplayTarget _target;
        private bool _hasTarget;

        public int Count => _hasTarget ? 1 : 0;

        public WpfRetainedVisualBranchReplayTarget this[int index]
        {
            get
            {
                if (!_hasTarget || index != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _target;
            }
        }

        public void Set(WpfRetainedVisualBranchReplayTarget target)
        {
            _target = target;
            _hasTarget = true;
        }

        public void Clear()
        {
            _target = default;
            _hasTarget = false;
        }

        public IEnumerator<WpfRetainedVisualBranchReplayTarget> GetEnumerator()
        {
            if (_hasTarget)
            {
                yield return _target;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private sealed class ReplayTargetList : IReadOnlyList<WpfRetainedVisualBranchReplayTarget>
    {
        private readonly List<WpfRetainedVisualBranchReplayTarget> _targets = new();

        public int Count => _targets.Count;

        public WpfRetainedVisualBranchReplayTarget this[int index] => _targets[index];

        public void Add(WpfRetainedVisualBranchReplayTarget target)
        {
            _targets.Add(target);
        }

        public void Clear()
        {
            _targets.Clear();
        }

        public IEnumerator<WpfRetainedVisualBranchReplayTarget> GetEnumerator()
        {
            return _targets.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private struct VisualSet : IReadOnlyList<ProGpuVisual>
    {
        private const int InlineCapacity = 4;

        private ProGpuVisual? _first;
        private ProGpuVisual? _second;
        private ProGpuVisual? _third;
        private ProGpuVisual? _fourth;
        private int _inlineCount;
        private List<ProGpuVisual>? _many;

        public int Count => _many?.Count ?? _inlineCount;

        public ProGpuVisual this[int index]
        {
            get
            {
                if (_many != null)
                {
                    return _many[index];
                }

                if ((uint)index >= (uint)_inlineCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return index switch
                {
                    0 => _first!,
                    1 => _second!,
                    2 => _third!,
                    _ => _fourth!
                };
            }
        }

        public void Add(ProGpuVisual visual)
        {
            ArgumentNullException.ThrowIfNull(visual);

            if (_many != null)
            {
                _many.Add(visual);
                return;
            }

            if (_inlineCount < InlineCapacity)
            {
                SetInline(_inlineCount++, visual);
                return;
            }

            _many = new List<ProGpuVisual>(InlineCapacity + 1);
            AddInlineTo(_many);
            _many.Add(visual);
            ClearInline();
        }

        public bool Remove(ProGpuVisual visual)
        {
            ArgumentNullException.ThrowIfNull(visual);

            if (_many != null)
            {
                if (!_many.Remove(visual))
                {
                    return false;
                }

                if (_many.Count <= InlineCapacity)
                {
                    SetInlineFrom(_many);
                    _many = null;
                }

                return true;
            }

            for (var i = 0; i < _inlineCount; i++)
            {
                if (!ReferenceEquals(GetInline(i), visual))
                {
                    continue;
                }

                RemoveInlineAt(i);
                return true;
            }

            return false;
        }

        private readonly ProGpuVisual? GetInline(int index)
        {
            return index switch
            {
                0 => _first,
                1 => _second,
                2 => _third,
                _ => _fourth
            };
        }

        private void SetInline(int index, ProGpuVisual? visual)
        {
            switch (index)
            {
                case 0:
                    _first = visual;
                    break;
                case 1:
                    _second = visual;
                    break;
                case 2:
                    _third = visual;
                    break;
                default:
                    _fourth = visual;
                    break;
            }
        }

        private void RemoveInlineAt(int index)
        {
            for (var i = index; i < _inlineCount - 1; i++)
            {
                SetInline(i, GetInline(i + 1));
            }

            _inlineCount--;
            SetInline(_inlineCount, null);
        }

        private readonly void AddInlineTo(List<ProGpuVisual> visuals)
        {
            for (var i = 0; i < _inlineCount; i++)
            {
                visuals.Add(GetInline(i)!);
            }
        }

        private void SetInlineFrom(List<ProGpuVisual> visuals)
        {
            ClearInline();
            for (var i = 0; i < visuals.Count; i++)
            {
                SetInline(_inlineCount++, visuals[i]);
            }
        }

        private void ClearInline()
        {
            _first = null;
            _second = null;
            _third = null;
            _fourth = null;
            _inlineCount = 0;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_first, _second, _third, _fourth, _inlineCount, _many);
        }

        IEnumerator<ProGpuVisual> IEnumerable<ProGpuVisual>.GetEnumerator()
        {
            return GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<ProGpuVisual>
        {
            private readonly ProGpuVisual? _first;
            private readonly ProGpuVisual? _second;
            private readonly ProGpuVisual? _third;
            private readonly ProGpuVisual? _fourth;
            private readonly int _inlineCount;
            private List<ProGpuVisual>.Enumerator _manyEnumerator;
            private readonly bool _hasMany;
            private int _inlineIndex;

            internal Enumerator(
                ProGpuVisual? first,
                ProGpuVisual? second,
                ProGpuVisual? third,
                ProGpuVisual? fourth,
                int inlineCount,
                List<ProGpuVisual>? many)
            {
                _first = first;
                _second = second;
                _third = third;
                _fourth = fourth;
                _inlineCount = inlineCount;
                if (many != null)
                {
                    _manyEnumerator = many.GetEnumerator();
                    _hasMany = true;
                }
                else
                {
                    _manyEnumerator = default;
                    _hasMany = false;
                }

                _inlineIndex = 0;
                Current = null!;
            }

            public ProGpuVisual Current { get; private set; }

            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_hasMany)
                {
                    if (!_manyEnumerator.MoveNext())
                    {
                        return false;
                    }

                    Current = _manyEnumerator.Current;
                    return true;
                }

                if (_inlineIndex >= _inlineCount)
                {
                    return false;
                }

                Current = _inlineIndex switch
                {
                    0 => _first!,
                    1 => _second!,
                    2 => _third!,
                    _ => _fourth!
                };
                _inlineIndex++;
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }

    private struct ReferenceOwnerSet
    {
        private const int InlineCapacity = 4;

        private object? _first;
        private object? _second;
        private object? _third;
        private object? _fourth;
        private int _inlineCount;
        private HashSet<object>? _many;

        public int Count => _many?.Count ?? _inlineCount;

        public bool Add(object source)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (_many != null)
            {
                return _many.Add(source);
            }

            if (ContainsInline(source))
            {
                return false;
            }

            if (_inlineCount < InlineCapacity)
            {
                SetInline(_inlineCount++, source);
                return true;
            }

            _many = new HashSet<object>(InlineCapacity + 1, ReferenceEqualityComparer.Instance);
            AddInlineTo(_many);
            _many.Add(source);
            ClearInline();
            return true;
        }

        public bool Contains(object source)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (_many != null)
            {
                return _many.Contains(source);
            }

            return ContainsInline(source);
        }

        public bool TryGetSingle(out object source)
        {
            if (_many == null && _inlineCount == 1)
            {
                source = _first!;
                return true;
            }

            source = null!;
            return false;
        }

        public void ClassifyAgainst(
            object dirtySource,
            out bool hasDirtySourceOwner,
            out bool hasCleanSourceOwner)
        {
            ArgumentNullException.ThrowIfNull(dirtySource);

            hasDirtySourceOwner = false;
            hasCleanSourceOwner = false;
            if (_many != null)
            {
                hasDirtySourceOwner = _many.Contains(dirtySource);
                hasCleanSourceOwner = _many.Count > (hasDirtySourceOwner ? 1 : 0);
                return;
            }

            for (var i = 0; i < _inlineCount; i++)
            {
                if (ReferenceEquals(GetInline(i), dirtySource))
                {
                    hasDirtySourceOwner = true;
                }
                else
                {
                    hasCleanSourceOwner = true;
                }

                if (hasDirtySourceOwner && hasCleanSourceOwner)
                {
                    return;
                }
            }
        }

        public void ClassifyAgainst(
            HashSet<object> dirtySources,
            out bool hasDirtySourceOwner,
            out bool hasCleanSourceOwner)
        {
            ArgumentNullException.ThrowIfNull(dirtySources);

            hasDirtySourceOwner = false;
            hasCleanSourceOwner = false;
            if (_many != null)
            {
                ClassifyPromotedOwnersAgainstDirtySources(
                    _many,
                    dirtySources,
                    out hasDirtySourceOwner,
                    out hasCleanSourceOwner);
                return;
            }

            for (var i = 0; i < _inlineCount; i++)
            {
                if (dirtySources.Contains(GetInline(i)!))
                {
                    hasDirtySourceOwner = true;
                }
                else
                {
                    hasCleanSourceOwner = true;
                }

                if (hasDirtySourceOwner && hasCleanSourceOwner)
                {
                    return;
                }
            }
        }

        private static void ClassifyPromotedOwnersAgainstDirtySources(
            HashSet<object> sourceOwners,
            HashSet<object> dirtySources,
            out bool hasDirtySourceOwner,
            out bool hasCleanSourceOwner)
        {
            hasDirtySourceOwner = false;
            hasCleanSourceOwner = false;
            if (dirtySources.Count < sourceOwners.Count)
            {
                hasCleanSourceOwner = true;
                var dirtySourceEnumerator = dirtySources.GetEnumerator();
                while (dirtySourceEnumerator.MoveNext())
                {
                    var dirtySource = dirtySourceEnumerator.Current;
                    if (sourceOwners.Contains(dirtySource))
                    {
                        hasDirtySourceOwner = true;
                        return;
                    }
                }

                return;
            }

            var sourceOwnerEnumerator = sourceOwners.GetEnumerator();
            while (sourceOwnerEnumerator.MoveNext())
            {
                var sourceOwner = sourceOwnerEnumerator.Current;
                if (dirtySources.Contains(sourceOwner))
                {
                    hasDirtySourceOwner = true;
                }
                else
                {
                    hasCleanSourceOwner = true;
                }

                if (hasDirtySourceOwner && hasCleanSourceOwner)
                {
                    return;
                }
            }
        }

        private readonly object? GetInline(int index)
        {
            return index switch
            {
                0 => _first,
                1 => _second,
                2 => _third,
                _ => _fourth
            };
        }

        private void SetInline(int index, object? source)
        {
            switch (index)
            {
                case 0:
                    _first = source;
                    break;
                case 1:
                    _second = source;
                    break;
                case 2:
                    _third = source;
                    break;
                default:
                    _fourth = source;
                    break;
            }
        }

        private readonly bool ContainsInline(object source)
        {
            for (var i = 0; i < _inlineCount; i++)
            {
                if (ReferenceEquals(GetInline(i), source))
                {
                    return true;
                }
            }

            return false;
        }

        private readonly void AddInlineTo(HashSet<object> owners)
        {
            for (var i = 0; i < _inlineCount; i++)
            {
                owners.Add(GetInline(i)!);
            }
        }

        private void ClearInline()
        {
            _first = null;
            _second = null;
            _third = null;
            _fourth = null;
            _inlineCount = 0;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_first, _second, _third, _fourth, _inlineCount, _many);
        }

        public struct Enumerator
        {
            private readonly object? _first;
            private readonly object? _second;
            private readonly object? _third;
            private readonly object? _fourth;
            private readonly int _inlineCount;
            private HashSet<object>.Enumerator _manyEnumerator;
            private readonly bool _hasMany;
            private int _inlineIndex;

            internal Enumerator(
                object? first,
                object? second,
                object? third,
                object? fourth,
                int inlineCount,
                HashSet<object>? many)
            {
                _first = first;
                _second = second;
                _third = third;
                _fourth = fourth;
                _inlineCount = inlineCount;
                if (many != null)
                {
                    _manyEnumerator = many.GetEnumerator();
                    _hasMany = true;
                }
                else
                {
                    _manyEnumerator = default;
                    _hasMany = false;
                }

                _inlineIndex = 0;
                Current = null!;
            }

            public object Current { get; private set; }

            public bool MoveNext()
            {
                if (_hasMany)
                {
                    if (!_manyEnumerator.MoveNext())
                    {
                        return false;
                    }

                    Current = _manyEnumerator.Current;
                    return true;
                }

                if (_inlineIndex >= _inlineCount)
                {
                    return false;
                }

                Current = _inlineIndex switch
                {
                    0 => _first!,
                    1 => _second!,
                    2 => _third!,
                    _ => _fourth!
                };
                _inlineIndex++;
                return true;
            }
        }
    }
}

public readonly record struct WpfRetainedVisualBranchReplayTarget(
    object Source,
    ProGpuVisual Visual);

internal enum WpfRetainedVisualBranchOwnerKind
{
    SourceOwner,
    Dependency
}

public readonly struct WpfRetainedVisualBranchInvalidationResult
{
    public WpfRetainedVisualBranchInvalidationResult(
        int dirtySourceCount,
        int mappedSourceCount,
        int invalidatedVisualCount,
        int sharedWithCleanSourceVisualCount = 0,
        int replayTargetConflictCount = 0)
    {
        DirtySourceCount = dirtySourceCount;
        MappedSourceCount = mappedSourceCount;
        InvalidatedVisualCount = invalidatedVisualCount;
        SharedWithCleanSourceVisualCount = sharedWithCleanSourceVisualCount;
        ReplayTargetConflictCount = replayTargetConflictCount;
    }

    public int DirtySourceCount { get; }

    public int MappedSourceCount { get; }

    public int UnmappedSourceCount => DirtySourceCount - MappedSourceCount;

    public int InvalidatedVisualCount { get; }

    public int SharedWithCleanSourceVisualCount { get; }

    public int ReplayTargetConflictCount { get; }

    public bool CanTargetAllDirtySources =>
        DirtySourceCount > 0 &&
        UnmappedSourceCount == 0 &&
        InvalidatedVisualCount > 0 &&
        ReplayTargetConflictCount == 0;
}

internal interface IWpfRetainedVisualBranchSink
{
    void RegisterVisualOwner(object sourceVisual);

    void RegisterVisualDependency(object dependency);

    bool PushVisualOwner(object sourceVisual);

    void PopVisualOwner();
}

internal readonly struct WpfReplayRect : IEquatable<WpfReplayRect>
{
    public WpfReplayRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static WpfReplayRect Empty { get; } = new(0, 0, 0, 0);

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool Equals(WpfReplayRect other)
    {
        return X.Equals(other.X)
            && Y.Equals(other.Y)
            && Width.Equals(other.Width)
            && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is WpfReplayRect other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }
}

internal readonly struct WpfReplayPoint
{
    public WpfReplayPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

internal readonly struct WpfRetainedVisualState
{
    public WpfRetainedVisualState(
        Vector2 offset,
        Matrix4x4 transform,
        float opacity,
        WpfReplayRect? clipBounds,
        Vector2? size = null,
        global::ProGPU.Scene.EffectBase? effect = null,
        bool cacheAsLayer = false,
        WpfReplayRect? contentBounds = null,
        MediaBrush? opacityMask = null,
        WpfReplayRect? opacityMaskBounds = null,
        WpfReplayRect? outerClipBounds = null)
    {
        Offset = offset;
        Transform = transform;
        Opacity = opacity;
        ClipBounds = clipBounds;
        OuterClipBounds = outerClipBounds;
        Size = size;
        Effect = effect;
        CacheAsLayer = cacheAsLayer;
        ContentBounds = contentBounds;
        OpacityMask = opacityMask;
        OpacityMaskBounds = opacityMaskBounds;
    }

    public Vector2 Offset { get; }

    public Vector2? Size { get; }

    public Matrix4x4 Transform { get; }

    public float Opacity { get; }

    public WpfReplayRect? ClipBounds { get; }

    public WpfReplayRect? OuterClipBounds { get; }

    public global::ProGPU.Scene.EffectBase? Effect { get; }

    public bool CacheAsLayer { get; }

    public WpfReplayRect? ContentBounds { get; }

    public MediaBrush? OpacityMask { get; }

    public WpfReplayRect? OpacityMaskBounds { get; }
}

internal interface IWpfRetainedVisualStateSink
{
    void ApplyVisualState(in WpfRetainedVisualState state);
}
