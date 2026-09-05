using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Xunit;
using PortableAlignmentX = ProGPU.Wpf.Interop.PortableAlignmentX;
using PortableAlignmentY = ProGPU.Wpf.Interop.PortableAlignmentY;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableGeometryDrawingState = ProGPU.Wpf.Interop.PortableGeometryDrawingState;
using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;
using PortableGlyphRunDrawingState = ProGPU.Wpf.Interop.PortableGlyphRunDrawingState;
using PortableGlyphRunDrawingStateSource = ProGPU.Wpf.Interop.IPortableGlyphRunDrawingStateSource;
using PortableMatrix3x2 = ProGPU.Wpf.Interop.PortableMatrix3x2;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableRenderDataSnapshot = ProGPU.Wpf.Interop.PortableRenderDataSnapshot;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableInvalidationSource = ProGPU.Wpf.Interop.IPortableInvalidationSource;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;
using PortableStretch = ProGPU.Wpf.Interop.PortableStretch;
using PortableTileBrush = ProGPU.Wpf.Interop.PortableTileBrush;
using PortableTileBrushKind = ProGPU.Wpf.Interop.PortableTileBrushKind;
using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;
using PortableTileMode = ProGPU.Wpf.Interop.PortableTileMode;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfVisualInvalidationTrackerTests
{
    [Fact]
    public void AttachMarksRootDirtyAndConsumeClearsDirtyState()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        var invalidationCount = 0;
        tracker.Invalidated += (_, _) => invalidationCount++;

        tracker.Attach(root);

        Assert.Same(root, tracker.Root);
        Assert.True(tracker.IsDirty);
        Assert.True(tracker.SubscriptionCount > 0);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Equal(1, tracker.DirtySourceCount);
        Assert.Contains(root, tracker.DirtySources);
        Assert.Equal(1, invalidationCount);
        Assert.True(tracker.ConsumeDirty());
        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.Equal(0, tracker.DirtySourceCount);
    }

    [Fact]
    public void NonPortableChangedEventDoesNotMarkTrackerDirty()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.RaiseChanged();

        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.Empty(tracker.DirtySources);
    }

    [Fact]
    public void PortableInvalidationSourceMarksTrackerDirtyWithoutReflectedEvent()
    {
        var root = new FakePortableInvalidationResource();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
        Assert.True(root.PortableSubscriptionCount > 0);
        Assert.Equal(0, root.ReflectedChangedSubscriptionCount);
        Assert.Equal(0, root.ReflectedVersionProbeCount);
    }

    [Fact]
    public void PortableNativeImageTracksTypedGpuTextureInvalidation()
    {
        var textureSource = new FakeInvalidatingTextureSource();
        var root = new FakePortableNativeImageSource(textureSource);
        var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        textureSource.RaiseTextureChanged();

        Assert.True(tracker.IsDirty);
        Assert.Same(textureSource, tracker.LastDirtySource);
        Assert.Contains(textureSource, tracker.DirtySources);
        Assert.Equal(1, textureSource.SubscriptionCount);

        tracker.Dispose();
        Assert.Equal(0, textureSource.SubscriptionCount);
    }

    [Fact]
    public void PortableInvalidationSourceDoesNotProbeReflectedVersionProperties()
    {
        var root = new FakePortableInvalidationResource();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        Assert.False(tracker.DetectVersionChanges());

        root.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Equal(0, root.ReflectedVersionProbeCount);
    }

    [Fact]
    public void PropertyChangedMarksTrackerDirty()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.RaisePropertyChanged(nameof(FakeVisual.Opacity));

        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
    }

    [Fact]
    public void NonPortablePublicVersionIsNotSnapshotted()
    {
        var root = new FakeVisual
        {
            Brush = new FakePublicVersionResource()
        };
        using var tracker = new WpfVisualInvalidationTracker();

        tracker.Attach(root);
        tracker.ConsumeDirty();

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void NonPortablePublicVersionChangeDoesNotMarkTrackerDirty()
    {
        var brush = new FakePublicVersionResource();
        var root = new FakeVisual
        {
            Brush = brush
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.IncrementVersion();

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.DoesNotContain(brush, tracker.DirtySources);
    }

    [Fact]
    public void NonPortableVisualStatePropertyChangesDoNotMarkTrackerDirty()
    {
        var root = new FakeVisual
        {
            VisualOffset = new System.Windows.Vector(0, 0),
            VisualScrollableAreaClip = new Rect(0, 0, 100, 40),
            VisualClip = new Rect(0, 0, 100, 40),
            LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 40)),
            ClipToBounds = false
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        root.VisualOffset = new System.Windows.Vector(0, -120);
        root.VisualScrollableAreaClip = new Rect(0, 0, 100, 56);
        root.VisualClip = new Rect(0, 0, 100, 56);
        root.LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 56));
        root.ClipToBounds = true;

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.DoesNotContain(root, tracker.DirtySources);
    }

    [Fact]
    public void PortableLayoutStateChangeMarksTrackerDirtyWithoutEvent()
    {
        var state = new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new PortableSize(40, 20),
            HasClipToBounds = true,
            ClipToBounds = false
        };
        var root = new FakePortableLayoutVisual(state);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        state.RenderSize = new PortableSize(41, 20);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        state.HasLayoutClip = true;
        state.LayoutClip = new RectangleGeometry(new Rect(0, 0, 100, 40));

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.ClipToBounds = true;

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
    }

    [Fact]
    public void PortableVisualStateChangeMarksTrackerDirtyWithoutEvent()
    {
        var state = new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(0, 0),
            HasOpacity = true,
            Opacity = 1.0
        };
        var root = new FakePortableStateVisual(state);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        state.Offset = new PortablePoint(0, -120);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);

        tracker.ConsumeDirty();
        state.HasScrollableAreaClip = true;
        state.ScrollableAreaClip = new PortableRect(0, 0, 100, 40);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasOpacityMask = true;
        state.OpacityMask = new FakeResource();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasEffect = true;
        state.Effect = new FakeResource();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasCacheMode = true;
        state.CacheMode = new FakeResource();

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasBitmapScalingMode = true;
        state.BitmapScalingMode = "NearestNeighbor";

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasTextRenderingMode = true;
        state.TextRenderingMode = "ClearType";

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);

        tracker.ConsumeDirty();
        state.HasSnappingGuidelinesX = true;
        state.SnappingGuidelinesX = new[] { 10d, 20d };

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
    }

    [Fact]
    public void PortableVisualStateRemovalMarksVisitedSourceDirty()
    {
        var child = new MutablePortableStateVisual(new PortableVisualState
        {
            HasOpacity = true,
            Opacity = 1.0
        });
        var root = new FakePortableVisualChildrenOnly();
        root.AddChild(child);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        child.PublishState = false;

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(child, tracker.LastDirtySource);
        Assert.Contains(child, tracker.DirtySources);
    }

    [Fact]
    public void PortableVisualSourceDoesNotProbeReflectedReferenceProperties()
    {
        var effect = new FakeResource();
        var root = new ThrowingPortableStateVisual(new PortableVisualState
        {
            HasEffect = true,
            Effect = effect
        });
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        effect.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(effect, tracker.LastDirtySource);
        Assert.Equal(0, root.ReflectedPropertyProbeCount);
        Assert.Equal(0, root.ReflectedVersionProbeCount);
    }

    [Fact]
    public void PortableVisualSourceDoesNotProbeReflectedVersionProperties()
    {
        var state = new PortableVisualState
        {
            HasOpacity = true,
            Opacity = 1.0
        };
        var root = new ThrowingPortableStateVisual(state);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        Assert.False(tracker.DetectVersionChanges());

        state.Opacity = 0.5;

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Equal(0, root.ReflectedVersionProbeCount);
    }

    [Fact]
    public void PrivateVersionFieldChangeDoesNotMarkTrackerDirty()
    {
        var brush = new FakePrivateVersionResource();
        var root = new FakeVisual
        {
            Brush = brush
        };
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.IncrementVersion();

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.DoesNotContain(brush, tracker.DirtySources);
    }

    [Fact]
    public void NonPortableChildrenCollectionChangeDoesNotMarkTrackerDirty()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var child = new FakeVisual();
        root.Children.Add(child);

        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);

        child.RaisePropertyChanged(nameof(FakeVisual.Opacity));

        Assert.False(tracker.IsDirty);
        Assert.DoesNotContain(child, tracker.DirtySources);
    }

    [Fact]
    public void PortableVisualChildrenChangeMarksTrackerDirtyWithoutReflectedChildrenCollection()
    {
        var root = new FakePortableVisualChildrenOnly();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var child = new FakeVisual();
        root.AddChild(child);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Same(root, tracker.LastDirtySource);
        Assert.Contains(root, tracker.DirtySources);
        tracker.ConsumeDirty();

        child.RaisePropertyChanged(nameof(FakeVisual.Opacity));

        Assert.True(tracker.IsDirty);
        Assert.Same(child, tracker.LastDirtySource);
    }

    [Fact]
    public void PortableVisualChildrenSubscriptionRefreshIsDeferredUntilDirtyConsumed()
    {
        var root = new FakePortableVisualChildrenOnly();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();
        var initialSubscriptionCount = tracker.SubscriptionCount;

        var child = new FakeVisual();
        root.AddChild(child);

        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Equal(initialSubscriptionCount, tracker.SubscriptionCount);

        tracker.ConsumeDirty();

        Assert.True(tracker.SubscriptionCount > initialSubscriptionCount);
        child.RaisePropertyChanged(nameof(FakeVisual.Opacity));
        Assert.True(tracker.IsDirty);
        Assert.Same(child, tracker.LastDirtySource);
    }

    [Fact]
    public void PortableVisualChildrenSubscriptionRefreshPrunesRemovedChildSnapshots()
    {
        var root = new FakePortableVisualChildrenOnly();
        var childGroup = new FakePortableVisualChildrenOnly();
        var leaf = new FakeVisual();
        childGroup.AddChild(leaf);
        root.AddChild(childGroup);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        Assert.True(root.RemoveChild(childGroup));
        Assert.True(tracker.DetectVersionChanges());
        Assert.True(tracker.IsDirty);
        Assert.Contains(root, tracker.DirtySources);
        Assert.Contains(childGroup, tracker.DirtySources);

        tracker.ConsumeDirty();

        Assert.False(tracker.DetectVersionChanges());
        Assert.False(tracker.IsDirty);
        Assert.DoesNotContain(childGroup, tracker.DirtySources);
    }

    [Fact]
    public void DetectVersionChangesRaisesInvalidatedAfterCompleteDirtySourceBatch()
    {
        var firstState = new PortableVisualState
        {
            HasOpacity = true,
            Opacity = 1.0
        };
        var secondState = new PortableVisualState
        {
            HasOpacity = true,
            Opacity = 1.0
        };
        var firstChild = new FakePortableStateVisual(firstState);
        var secondChild = new FakePortableStateVisual(secondState);
        var root = new FakePortableVisualChildrenOnly();
        root.AddChild(firstChild);
        root.AddChild(secondChild);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var invalidationCount = 0;
        var observedDirtySourceCount = 0;
        var observedFirstChildDirty = false;
        var observedSecondChildDirty = false;
        tracker.Invalidated += (_, _) =>
        {
            invalidationCount++;
            observedDirtySourceCount = tracker.DirtySourceCount;
            observedFirstChildDirty = tracker.DirtySources.Contains(firstChild);
            observedSecondChildDirty = tracker.DirtySources.Contains(secondChild);
        };

        firstState.Opacity = 0.5;
        secondState.Opacity = 0.25;

        Assert.True(tracker.DetectVersionChanges());
        Assert.Equal(1, invalidationCount);
        Assert.Equal(2, observedDirtySourceCount);
        Assert.True(observedFirstChildDirty);
        Assert.True(observedSecondChildDirty);
        Assert.Equal(2, tracker.DirtySourceCount);
        Assert.Contains(firstChild, tracker.DirtySources);
        Assert.Contains(secondChild, tracker.DirtySources);
    }

    [Fact]
    public void DrawingForegroundBrushChangeMarksTrackerDirty()
    {
        var brush = new FakeResource();
        var root = new FakePortableDrawingVisual(new FakeGlyphRunDrawing(brush));
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
    }

    [Fact]
    public void VisualBrushVisualChangeMarksTrackerDirty()
    {
        var brushVisual = new FakeVisual();
        var root = new FakePortableDrawingVisual(new FakeVisualBrush(brushVisual));
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brushVisual.RaisePropertyChanged(nameof(FakeVisual.Opacity));

        Assert.True(tracker.IsDirty);
        Assert.Same(brushVisual, tracker.LastDirtySource);
        Assert.Contains(brushVisual, tracker.DirtySources);
    }

    [Fact]
    public void VisualEffectChangeMarksTrackerDirty()
    {
        var effect = new FakeResource();
        var root = new FakePortableStateVisual(new PortableVisualState
        {
            HasEffect = true,
            Effect = effect
        });
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        effect.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(effect, tracker.LastDirtySource);
        Assert.Contains(effect, tracker.DirtySources);
    }

    [Fact]
    public void EnumerateTrackedDependenciesIncludesNestedResourceGraph()
    {
        var brush = new FakeResource();
        var drawing = new FakeGlyphRunDrawing(brush);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(drawing);

        Assert.Equal(new object[] { drawing, brush }, dependencies);
    }

    [Fact]
    public void EnumerateTrackedDependenciesIgnoresNonPortablePrivateDrawingContentGraph()
    {
        var brush = new FakeResource();
        var content = new FakeRenderContent
        {
            Brush = brush
        };
        var root = new FakeUiElementVisual(content);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.DoesNotContain(content, dependencies);
        Assert.DoesNotContain(brush, dependencies);
    }

    [Fact]
    public void EnumerateTrackedDependenciesUsesPortableDrawingAndRenderDataSources()
    {
        var brush = new FakeResource();
        var renderData = new FakePortableRenderDataSource(new object?[] { brush });
        var root = new FakePortableDrawingVisual(renderData);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.Contains(renderData, dependencies);
        Assert.Contains(brush, dependencies);
        Assert.Equal(1, root.ContentReadCount);
        Assert.Equal(1, renderData.SnapshotReadCount);
    }

    [Fact]
    public void EnumerateTrackedDependenciesUsesPortableVisualStateResources()
    {
        var transform = new FakeResource();
        var clip = new FakeResource();
        var effect = new FakeResource();
        var cacheMode = new FakeResource();
        var root = new FakePortableStateVisual(new PortableVisualState
        {
            HasTransform = true,
            Transform = transform,
            HasClip = true,
            Clip = clip,
            HasEffect = true,
            Effect = effect,
            HasCacheMode = true,
            CacheMode = cacheMode
        });

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);

        Assert.Contains(root, dependencies);
        Assert.Contains(transform, dependencies);
        Assert.Contains(clip, dependencies);
        Assert.Contains(effect, dependencies);
        Assert.Contains(cacheMode, dependencies);
    }

    [Fact]
    public void ListLikeTrackedDependencyTraversalUsesIndexerWithoutEnumerator()
    {
        var brush = new FakeResource();
        var root = new ThrowingEnumeratorList(brush);
        using var tracker = new WpfVisualInvalidationTracker();

        var exception = Record.Exception(() => tracker.Attach(root));
        Assert.Null(exception);
        tracker.ConsumeDirty();

        exception = Record.Exception(() => tracker.DetectVersionChanges());
        Assert.Null(exception);

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(root);
        var sink = new TestRetainedBranchSink();

        Assert.True(WpfVisualInvalidationTracker.RegisterTrackedDependencies(sink, root));
        Assert.Contains(root, dependencies);
        Assert.Contains(brush, dependencies);
        Assert.Contains(root, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(0, root.EnumeratorRequestCount);
        Assert.True(root.IndexerReadCount > 0);
    }

    [Fact]
    public void NonPortablePrivateDrawingContentChangeDoesNotMarkTrackerDirty()
    {
        var brush = new FakeResource();
        var root = new FakeUiElementVisual(new FakeRenderContent
        {
            Brush = brush
        });
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.RaisePortableInvalidated();

        Assert.False(tracker.IsDirty);
        Assert.Null(tracker.LastDirtySource);
        Assert.DoesNotContain(brush, tracker.DirtySources);
    }

    [Fact]
    public void PortableDrawingRenderDataDependencyChangeMarksTrackerDirty()
    {
        var brush = new FakeResource();
        var renderData = new FakePortableRenderDataSource(new object?[] { brush });
        var root = new FakePortableDrawingVisual(renderData);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        brush.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
    }

    [Fact]
    public void EnumerateTrackedDependenciesDoesNotExpandGradientStopGraphByReflection()
    {
        var firstStop = new GradientStop(Colors.Red, 0);
        var secondStop = new GradientStop(Colors.Blue, 1);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                firstStop,
                secondStop
            }
        };

        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(brush);

        Assert.Contains(brush, dependencies);
        Assert.DoesNotContain(brush.GradientStops, dependencies);
        Assert.DoesNotContain(firstStop, dependencies);
        Assert.DoesNotContain(secondStop, dependencies);
    }

    [Fact]
    public void GradientStopChangeInvalidatesTrackedPortableBrush()
    {
        var stop = new GradientStop(Colors.Red, 0);
        var brush = new LinearGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                stop,
                new GradientStop(Colors.Blue, 1)
            }
        };
        var root = new FakePortableDrawingVisual(new FakeGeometryDrawing(brush));
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        stop.Offset = 0.25;

        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
    }

    [Fact]
    public void AttachSkipsFrozenFreezableChangedSubscription()
    {
        var transform = new FakeFrozenFreezableLikeResource();
        using var tracker = new WpfVisualInvalidationTracker();

        var exception = Record.Exception(() => tracker.Attach(transform));

        Assert.Null(exception);
        Assert.Same(transform, tracker.Root);
        Assert.True(tracker.IsDirty);
        Assert.Equal(0, tracker.SubscriptionCount);
    }

    [Fact]
    public void GradientStopCollectionChangeInvalidatesTrackedPortableBrush()
    {
        var brush = new LinearGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Colors.Red, 0)
            }
        };
        var root = new FakePortableDrawingVisual(new FakeGeometryDrawing(brush));
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        var addedStop = new GradientStop(Colors.Green, 0.5);
        brush.GradientStops.Add(addedStop);

        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
        tracker.ConsumeDirty();

        addedStop.Color = Colors.Yellow;

        Assert.True(tracker.IsDirty);
        Assert.Same(brush, tracker.LastDirtySource);
        Assert.Contains(brush, tracker.DirtySources);
    }

    [Fact]
    public void GeometryDrawingGeometryChangeMarksTrackerDirty()
    {
        var geometry = new FakeResource();
        var root = new FakePortableDrawingVisual(new FakeGeometryDrawing(geometry: geometry));
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        geometry.RaisePortableInvalidated();

        Assert.True(tracker.IsDirty);
        Assert.Same(geometry, tracker.LastDirtySource);
        Assert.Contains(geometry, tracker.DirtySources);
    }

    [Fact]
    public void DetachUnsubscribesTrackedSources()
    {
        var root = new FakeVisual();
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(root);
        tracker.ConsumeDirty();

        tracker.Detach();
        root.RaisePropertyChanged(nameof(FakeVisual.Opacity));

        Assert.Null(tracker.Root);
        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.SubscriptionCount);
    }

    private sealed class FakeVisual : INotifyPropertyChanged
    {
        public event EventHandler? Changed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public FakeVisualCollection Children { get; } = new();

        public object? Drawing { get; init; }

        public object? Brush { get; init; }

        public object? Clip { get; init; }

        public object? VisualClip { get; set; }

        public object? LayoutClip { get; set; }

        public bool ClipToBounds { get; set; }

        public object? Effect { get; init; }

        public double Opacity { get; set; } = 1;

        public System.Windows.Vector VisualOffset { get; set; }

        public Rect? VisualScrollableAreaClip { get; set; }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class FakePortableLayoutVisual : PortableVisualLayoutStateSource
    {
        private readonly PortableVisualLayoutState _state;

        public FakePortableLayoutVisual(PortableVisualLayoutState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakePortableStateVisual : PortableVisualStateSource
    {
        private readonly PortableVisualState _state;

        public FakePortableStateVisual(PortableVisualState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class MutablePortableStateVisual : PortableVisualStateSource
    {
        private readonly PortableVisualState _state;

        public MutablePortableStateVisual(PortableVisualState state)
        {
            _state = state;
        }

        public bool PublishState { get; set; } = true;

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return PublishState;
        }
    }

    private sealed class ThrowingPortableStateVisual : PortableVisualStateSource
    {
        private readonly PortableVisualState _state;

        public ThrowingPortableStateVisual(PortableVisualState state)
        {
            _state = state;
        }

        public int ReflectedPropertyProbeCount { get; private set; }

        public int ReflectedVersionProbeCount { get; private set; }

        public int Version => ThrowReflectedVersionProbe();

        public object? Children => ThrowReflectedPropertyProbe();

        public object? Clip => ThrowReflectedPropertyProbe();

        public object? Effect => ThrowReflectedPropertyProbe();

        public object? OpacityMask => ThrowReflectedPropertyProbe();

        public object? Transform => ThrowReflectedPropertyProbe();

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedPropertyProbe()
        {
            ReflectedPropertyProbeCount++;
            throw new InvalidOperationException("Reflected property probe should not be used for portable visual sources.");
        }

        private int ThrowReflectedVersionProbe()
        {
            ReflectedVersionProbeCount++;
            throw new InvalidOperationException("Reflected version probe should not be used for portable visual sources.");
        }
    }

    private sealed class FakeGeometryDrawing : PortableGeometryDrawingStateSource
    {
        private readonly object? _geometry;
        private readonly object? _brush;

        public FakeGeometryDrawing(object? brush = null, object? geometry = null)
        {
            _geometry = geometry;
            _brush = brush;
        }

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasGeometry = _geometry != null,
                Geometry = _geometry,
                HasBrush = _brush != null,
                Brush = _brush
            };
            return true;
        }
    }

    private sealed class FakeGlyphRunDrawing : PortableGlyphRunDrawingStateSource
    {
        private readonly object? _foregroundBrush;

        public FakeGlyphRunDrawing(object? foregroundBrush)
        {
            _foregroundBrush = foregroundBrush;
        }

        public bool TryGetPortableGlyphRunDrawingState(out PortableGlyphRunDrawingState state)
        {
            state = new PortableGlyphRunDrawingState
            {
                HasForegroundBrush = true,
                ForegroundBrush = _foregroundBrush
            };
            return true;
        }
    }

    private sealed class FakeVisualBrush : PortableTileBrushSource
    {
        private readonly object _visual;

        public FakeVisualBrush(object visual)
        {
            _visual = visual;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = new PortableTileBrush(
                PortableTileBrushKind.Visual,
                _visual,
                opacity: 1.0,
                viewport: new PortableRect(0, 0, 1, 1),
                viewbox: new PortableRect(0, 0, 1, 1),
                viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                tileMode: PortableTileMode.None,
                stretch: PortableStretch.Fill,
                alignmentX: PortableAlignmentX.Center,
                alignmentY: PortableAlignmentY.Center,
                hasTransform: false,
                PortableMatrix3x2.Identity,
                hasRelativeTransform: false,
                PortableMatrix3x2.Identity);
            return true;
        }
    }

    private sealed class FakePortableVisualChildrenOnly : PortableVisualChildrenSource
    {
        private readonly List<object> _children = new();

        public void AddChild(object child)
        {
            _children.Add(child);
        }

        public bool RemoveChild(object child)
        {
            return _children.Remove(child);
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = _children.Count;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = _children[index];
            return true;
        }
    }

    private sealed class FakeUiElementVisual
    {
        private readonly object? _drawingContent;

        public FakeUiElementVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }
    }

    private sealed class FakePortableDrawingVisual : PortableDrawingContentSource
    {
        private readonly object? _drawingContent;

        public FakePortableDrawingVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }

        public int ContentReadCount { get; private set; }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            ContentReadCount++;
            content = _drawingContent;
            return true;
        }
    }

    private sealed class FakePortableRenderDataSource : PortableRenderDataSource
    {
        private readonly IReadOnlyList<object?> _dependentResources;

        public FakePortableRenderDataSource(IReadOnlyList<object?> dependentResources)
        {
            _dependentResources = dependentResources;
        }

        public int SnapshotReadCount { get; private set; }

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            SnapshotReadCount++;
            snapshot = new PortableRenderDataSnapshot(Array.Empty<byte>(), _dependentResources);
            return true;
        }
    }

    private sealed class FakeRenderContent
    {
        public object? Brush { get; init; }
    }

    private sealed class FakeResource : PortableInvalidationSource
    {
        private EventHandler? _portableInvalidated;

        public void RaisePortableInvalidated()
        {
            _portableInvalidated?.Invoke(this, EventArgs.Empty);
        }

        public bool TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)
        {
            _portableInvalidated += handler;
            subscription = new Subscription(() => _portableInvalidated -= handler);
            return true;
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _unsubscribe;

            public Subscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                var unsubscribe = _unsubscribe;
                _unsubscribe = null;
                unsubscribe?.Invoke();
            }
        }
    }

    private sealed class FakePortableInvalidationResource : PortableInvalidationSource
    {
        private EventHandler? _portableInvalidated;

        public event EventHandler? Changed
        {
            add => ReflectedChangedSubscriptionCount++;
            remove => ReflectedChangedUnsubscriptionCount++;
        }

        public int PortableSubscriptionCount { get; private set; }

        public int ReflectedChangedSubscriptionCount { get; private set; }

        public int ReflectedChangedUnsubscriptionCount { get; private set; }

        public int ReflectedVersionProbeCount { get; private set; }

        public int Version
        {
            get
            {
                ReflectedVersionProbeCount++;
                throw new InvalidOperationException("Reflected version probe should not be used for portable invalidation sources.");
            }
        }

        public bool TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)
        {
            PortableSubscriptionCount++;
            _portableInvalidated += handler;
            subscription = new Subscription(() => _portableInvalidated -= handler);
            return true;
        }

        public void RaisePortableInvalidated()
        {
            _portableInvalidated?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _unsubscribe;

            public Subscription(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                var unsubscribe = _unsubscribe;
                _unsubscribe = null;
                unsubscribe?.Invoke();
            }
        }
    }

    private sealed class FakePortableNativeImageSource : IPortableNativeImageSource
    {
        private readonly object _nativeImage;

        public FakePortableNativeImageSource(object nativeImage)
        {
            _nativeImage = nativeImage;
        }

        public int PixelWidth => 1;

        public int PixelHeight => 1;

        public bool TryGetPortableNativeImage(out object? nativeImage)
        {
            nativeImage = _nativeImage;
            return true;
        }
    }

    private sealed class FakeInvalidatingTextureSource :
        IProGpuInvalidatingTextureSource
    {
        private EventHandler? _textureChanged;

        public event EventHandler? TextureChanged
        {
            add
            {
                _textureChanged += value;
                SubscriptionCount++;
            }
            remove
            {
                _textureChanged -= value;
                SubscriptionCount--;
            }
        }

        public int SubscriptionCount { get; private set; }

        public bool TryGetGpuTexture(out GpuTexture texture)
        {
            texture = null!;
            return false;
        }

        public bool TryAcquireGpuTextureLease(out IProGpuTextureLease lease)
        {
            lease = null!;
            return false;
        }

        public void RaiseTextureChanged()
        {
            _textureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeFrozenFreezableLikeResource
    {
        public event EventHandler? Changed
        {
            add => throw new InvalidOperationException("Specified value must have IsFrozen set to false to modify.");
            remove => throw new InvalidOperationException("Specified value must have IsFrozen set to false to modify.");
        }
    }

    private sealed class FakePublicVersionResource
    {
        public int Version { get; private set; }

        public void IncrementVersion()
        {
            Version++;
        }
    }

    private sealed class FakePrivateVersionResource
    {
        private uint _version;

        public void IncrementVersion()
        {
            _version++;
        }
    }

    private sealed class FakeVisualCollection : INotifyCollectionChanged
    {
        private readonly List<object> _items = new();

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public int Count => _items.Count;

        public object this[int index] => _items[index];

        public void Add(object item)
        {
            _items.Add(item);
            CollectionChanged?.Invoke(
                this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
        }
    }

    private sealed class ThrowingEnumeratorList : IList
    {
        private readonly object?[] _items;

        public ThrowingEnumeratorList(params object?[] items)
        {
            _items = items;
        }

        public int EnumeratorRequestCount { get; private set; }

        public int IndexerReadCount { get; private set; }

        public int Count => _items.Length;

        public bool IsFixedSize => true;

        public bool IsReadOnly => true;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object? this[int index]
        {
            get
            {
                IndexerReadCount++;
                return _items[index];
            }
            set => throw new NotSupportedException();
        }

        public int Add(object? value)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            throw new NotSupportedException();
        }

        public bool Contains(object? value)
        {
            return Array.IndexOf(_items, value) >= 0;
        }

        public void CopyTo(Array array, int index)
        {
            _items.CopyTo(array, index);
        }

        public IEnumerator GetEnumerator()
        {
            EnumeratorRequestCount++;
            throw new InvalidOperationException("List-like invalidation traversal should use indexed access.");
        }

        public int IndexOf(object? value)
        {
            return Array.IndexOf(_items, value);
        }

        public void Insert(int index, object? value)
        {
            throw new NotSupportedException();
        }

        public void Remove(object? value)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestRetainedBranchSink : IWpfRetainedVisualBranchSink
    {
        public List<object> VisualDependencies { get; } = new();

        public void RegisterVisualOwner(object sourceVisual)
        {
        }

        public void RegisterVisualDependency(object dependency)
        {
            VisualDependencies.Add(dependency);
        }

        public bool PushVisualOwner(object sourceVisual)
        {
            return true;
        }

        public void PopVisualOwner()
        {
        }
    }
}
