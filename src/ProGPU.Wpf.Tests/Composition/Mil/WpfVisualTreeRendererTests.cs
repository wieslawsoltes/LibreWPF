using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaBitmapSource = System.Windows.Media.Imaging.BitmapSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;
using ProGpuTexture = ProGPU.Backend.GpuTexture;
using ProGpuBlurEffect = ProGPU.Scene.BlurEffect;
using ProGpuDropShadowEffect = ProGPU.Scene.DropShadowEffect;
using ProGpuEffectBase = ProGPU.Scene.EffectBase;
using ProGpuWpfShaderEffect = ProGPU.Scene.WpfShaderEffect;
using ProGpuWpfShaderEffectSampler = ProGPU.Scene.WpfShaderEffectSampler;
using ProGpuTextureSamplingMode = ProGPU.Scene.TextureSamplingMode;
using PortableBitmapEffectInput = ProGPU.Wpf.Interop.PortableBitmapEffectInput;
using PortableBitmapEffectInputSource = ProGPU.Wpf.Interop.IPortableBitmapEffectInputSource;
using PortableColor = ProGPU.Wpf.Interop.PortableColor;
using PortableEffect = ProGPU.Wpf.Interop.PortableEffect;
using PortableEffectSource = ProGPU.Wpf.Interop.IPortableEffectSource;
using PortableDrawingGroupState = ProGPU.Wpf.Interop.PortableDrawingGroupState;
using PortableDrawingGroupStateSource = ProGPU.Wpf.Interop.IPortableDrawingGroupStateSource;
using PortableGeometryDrawingState = ProGPU.Wpf.Interop.PortableGeometryDrawingState;
using PortableGeometryDrawingStateSource = ProGPU.Wpf.Interop.IPortableGeometryDrawingStateSource;
using PortableGlyphRun = ProGPU.Wpf.Interop.PortableGlyphRun;
using PortableGlyphRunDrawingState = ProGPU.Wpf.Interop.PortableGlyphRunDrawingState;
using PortableGlyphRunDrawingStateSource = ProGPU.Wpf.Interop.IPortableGlyphRunDrawingStateSource;
using PortableGlyphRunSource = ProGPU.Wpf.Interop.IPortableGlyphRunSource;
using PortableImageDrawingState = ProGPU.Wpf.Interop.PortableImageDrawingState;
using PortableImageDrawingStateSource = ProGPU.Wpf.Interop.IPortableImageDrawingStateSource;
using PortableGeometryPath = ProGPU.Wpf.Interop.PortableGeometryPath;
using PortableGeometryPathKind = ProGPU.Wpf.Interop.PortableGeometryPathKind;
using PortableGeometryPathSource = ProGPU.Wpf.Interop.IPortableGeometryPathSource;
using PortablePathFigure = ProGPU.Wpf.Interop.PortablePathFigure;
using PortablePathSegment = ProGPU.Wpf.Interop.PortablePathSegment;
using PortablePixelShader = ProGPU.Wpf.Interop.PortablePixelShader;
using PortableShaderEffect = ProGPU.Wpf.Interop.PortableShaderEffect;
using PortableShaderEffectSource = ProGPU.Wpf.Interop.IPortableShaderEffectSource;
using PortableShaderSampler = ProGPU.Wpf.Interop.PortableShaderSampler;
using PortableShaderSamplingMode = ProGPU.Wpf.Interop.PortableShaderSamplingMode;
using PortableMatrix3x2 = ProGPU.Wpf.Interop.PortableMatrix3x2;
using PortablePoint = ProGPU.Wpf.Interop.PortablePoint;
using PortableRect = ProGPU.Wpf.Interop.PortableRect;
using PortableSize = ProGPU.Wpf.Interop.PortableSize;
using PortableSweepDirection = ProGPU.Wpf.Interop.PortableSweepDirection;
using PortableAlignmentX = ProGPU.Wpf.Interop.PortableAlignmentX;
using PortableAlignmentY = ProGPU.Wpf.Interop.PortableAlignmentY;
using PortableBrushMappingMode = ProGPU.Wpf.Interop.PortableBrushMappingMode;
using PortableStretch = ProGPU.Wpf.Interop.PortableStretch;
using PortableTileBrush = ProGPU.Wpf.Interop.PortableTileBrush;
using PortableTileBrushKind = ProGPU.Wpf.Interop.PortableTileBrushKind;
using PortableTileBrushSource = ProGPU.Wpf.Interop.IPortableTileBrushSource;
using PortableTileMode = ProGPU.Wpf.Interop.PortableTileMode;
using PortableTransformMatrixSource = ProGPU.Wpf.Interop.IPortableTransformMatrixSource;
using PortableVisualLayoutState = ProGPU.Wpf.Interop.PortableVisualLayoutState;
using PortableVisualLayoutStateSource = ProGPU.Wpf.Interop.IPortableVisualLayoutStateSource;
using PortableVisualChildrenSource = ProGPU.Wpf.Interop.IPortableVisualChildrenSource;
using PortableVisualBounds = ProGPU.Wpf.Interop.PortableVisualBounds;
using PortableVisualBoundsSource = ProGPU.Wpf.Interop.IPortableVisualBoundsSource;
using PortableVisualState = ProGPU.Wpf.Interop.PortableVisualState;
using PortableVisualStateSource = ProGPU.Wpf.Interop.IPortableVisualStateSource;
using PortableDrawingContentSource = ProGPU.Wpf.Interop.IPortableDrawingContentSource;
using PortableRenderDataSnapshot = ProGPU.Wpf.Interop.PortableRenderDataSnapshot;
using PortableRenderDataSource = ProGPU.Wpf.Interop.IPortableRenderDataSource;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfVisualTreeRendererTests
{
    private static PortableVisualState CreatePortableScrollableAreaClipState(
        double x,
        double y,
        double width,
        double height)
    {
        return new PortableVisualState
        {
            HasScrollableAreaClip = true,
            ScrollableAreaClip = new PortableRect(x, y, width, height)
        };
    }

    private static PortableVisualState CreatePortableOpacityMaskState(object opacityMask)
    {
        return new PortableVisualState
        {
            HasOpacityMask = true,
            OpacityMask = opacityMask
        };
    }

    private static PortableVisualState CreatePortableEffectState(object effect)
    {
        return new PortableVisualState
        {
            HasEffect = true,
            Effect = effect
        };
    }

    private static PortableVisualState CreatePortableBitmapEffectState(object bitmapEffect)
    {
        return new PortableVisualState
        {
            HasBitmapEffect = true,
            BitmapEffect = bitmapEffect
        };
    }

    private static PortableVisualState CreatePortableCacheModeState(object cacheMode)
    {
        return new PortableVisualState
        {
            HasCacheMode = true,
            CacheMode = cacheMode
        };
    }

    [Fact]
    public void ReplaySubtreeRecursesThroughChildren()
    {
        var parentBrush = Brushes.Red;
        var childBrush = Brushes.Blue;
        var parent = new FakeDrawingVisual(CreateRenderData(parentBrush));
        parent.Children.Add(new FakeDrawingVisual(CreateRenderData(childBrush)));
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(parent, sink);

        Assert.Equal(2, result.VisualCount);
        Assert.Equal(2, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(2, 2, 0, 0), result.RenderData);
        Assert.Equal(2, sink.DrawRectangles.Count);
        Assert.Same(parentBrush, sink.DrawRectangles[0].Brush);
        Assert.Same(childBrush, sink.DrawRectangles[1].Brush);
    }

    [Fact]
    public void ReplaySubtreeIgnoresNonPortableProtectedVisualChildren()
    {
        var root = new FakeVisualChildrenVisual();
        var childBrush = Brushes.Blue;
        root.AddChild(new FakeDrawingVisual(CreateRenderData(childBrush)));
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(0, result.ContentCount);
        Assert.Equal(0, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(default, result.RenderData);
        Assert.Empty(sink.DrawRectangles);
    }

    [Fact]
    public void ReplaySubtreeRecursesThroughPortableVisualChildren()
    {
        var root = new FakePortableVisualChildrenVisual();
        var childBrush = Brushes.Blue;
        root.AddChild(new FakeDrawingVisual(CreateRenderData(childBrush)));
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        var rectangle = Assert.Single(sink.DrawRectangles);
        Assert.Same(childBrush, rectangle.Brush);
    }

    [Fact]
    public void ReplaySubtreeBoundsThreadStaticPortableStateCache()
    {
        int childCount = WpfVisualTreeRenderer.VisualReplayCacheEntryLimit * 2;
        var visualState = new PortableVisualState();
        var layoutState = new PortableVisualLayoutState();
        var root = new FakePortableVisualStateAndLayoutVisual(visualState, layoutState);
        for (int index = 0; index < childCount; index++)
        {
            root.Children.Add(new FakePortableVisualStateAndLayoutVisual(visualState, layoutState));
        }

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, new TestSink());

        Assert.Equal(childCount + 1, result.VisualCount);
        Assert.InRange(
            WpfVisualTreeRenderer.VisualReplayCacheRetainedCapacity,
            WpfVisualTreeRenderer.VisualReplayCacheEntryLimit,
            (WpfVisualTreeRenderer.VisualReplayCacheEntryLimit * 2) + 1024);
        Assert.True(WpfVisualTreeRenderer.VisualReplayCacheRetainedCapacity < childCount);
    }

    [Fact]
    public void ReplaySubtreeReadsUiElementDrawingContent()
    {
        var brush = Brushes.Green;
        var visual = new FakeUiElementVisual(CreateRenderData(brush));
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(visual, sink);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Single(sink.DrawRectangles);
        Assert.Same(brush, sink.DrawRectangles[0].Brush);
    }

    [Fact]
    public void ReplaySubtreeRegistersSourceVisualOwnersWhenSinkSupportsBranchMap()
    {
        var parent = new FakeDrawingVisual(CreateRenderData(Brushes.Red));
        var child = new FakeDrawingVisual(CreateRenderData(Brushes.Blue));
        parent.Children.Add(child);
        var sink = new TestSink();

        _ = new WpfVisualTreeRenderer().ReplaySubtree(parent, sink);

        Assert.Equal(new object[] { parent, child }, sink.VisualOwners);
    }

    [Fact]
    public void ReplaySubtreeLowersNativeVisualStateIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasOpacity = true,
            Opacity = 0.5,
            HasClip = true,
            Clip = new PortableRectangleClipGeometry(0, 0, 100, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var rootState = sink.RetainedVisualStates[0];
        Assert.Equal(new Vector2(10, 20), rootState.Offset);
        Assert.Equal(0.5f, rootState.Opacity);
        Assert.Equal(3, rootState.Transform.M41);
        Assert.Equal(4, rootState.Transform.M42);
        AssertReplayRect(0, 0, 100, 50, rootState.ClipBounds);
        Assert.Null(rootState.OuterClipBounds);
        var childState = sink.RetainedVisualStates[1];
        Assert.Equal(Vector2.Zero, childState.Offset);
        Assert.Equal(1f, childState.Opacity);
        Assert.Equal(Matrix4x4.Identity, childState.Transform);
        Assert.Null(childState.ClipBounds);
        Assert.Null(childState.OuterClipBounds);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersPortableVisualClipIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = new PortableRectangleClipGeometry(5, 6, 70, 80)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(5, 6, 70, 80, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeUsesPortableGeometryPathForVisualClipWithoutReflection()
    {
        var clip = new PortableRectangleClipGeometry(5, 6, 70, 80);
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = clip
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(5, 6, 70, 80, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(0, clip.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void ReplaySubtreeDerivesPortableRectangleClipBoundsFromPathPoints()
    {
        var clip = new PortableRectangleClipGeometry(5, 6, 70, 80, new PortableRect(0, 0, 1, 1));
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = clip
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(5, 6, 70, 80, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersLayoutClipIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasLayoutClip = true,
            LayoutClip = new PortableRectangleClipGeometry(4, 5, 60, 70)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(4, 5, 60, 70, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeUsesPortableLayoutStateForLayoutClip()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasLayoutClip = true,
            LayoutClip = new PortableRectangleClipGeometry(7, 8, 90, 20),
            HasClipToBounds = true,
            ClipToBounds = false
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(7, 8, 90, 20, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void CanReplaySubtreeTreatsAbsentPortableVisualStateValuesAsAuthoritative()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            });

        Assert.True(new WpfVisualTreeRenderer().CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.Equal(0, root.ReflectedStateProbeCount);
    }

    [Fact]
    public void ReplaySubtreeRegistersPortableVisualStateResourcesAsRetainedDependencies()
    {
        var transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4));
        var clip = new PortableRectangleClipGeometry(0, 0, 100, 50);
        var opacityMask = Brushes.White;
        var effect = new FakeBlurEffect(3);
        var cacheMode = new object();
        var layoutClip = new PortableRectangleClipGeometry(1, 2, 30, 40);
        var root = new FakePortableVisualStateAndLayoutDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasTransform = true,
                Transform = transform,
                HasClip = true,
                Clip = clip,
                HasOpacity = true,
                Opacity = 1,
                HasOpacityMask = true,
                OpacityMask = opacityMask,
                HasEffect = true,
                Effect = effect,
                HasCacheMode = true,
                CacheMode = cacheMode
            },
            new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new ProGPU.Wpf.Interop.PortableSize(100, 50),
                HasLayoutClip = true,
                LayoutClip = layoutClip
            });
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Contains(transform, sink.VisualDependencies);
        Assert.Contains(clip, sink.VisualDependencies);
        Assert.Contains(opacityMask, sink.VisualDependencies);
        Assert.Contains(effect, sink.VisualDependencies);
        Assert.Contains(cacheMode, sink.VisualDependencies);
        Assert.Contains(layoutClip, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeDoesNotReflectAbsentPortableVisualStateDependencies()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            });
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersClipToBoundsRenderSizeIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new ProGPU.Wpf.Interop.PortableSize(80, 35),
            HasClipToBounds = true,
            ClipToBounds = true,
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(0, 0, 80, 35, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeFallsBackWhenRenderSizeExceedsPortableFloatRange()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new ProGPU.Wpf.Interop.PortableSize(80, double.MaxValue / 2)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Empty(sink.RetainedVisualStates);
        Assert.Single(sink.DrawRectangles);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeUsesPortableLayoutStateForClipToBoundsAndOpacityMaskBounds()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            CreatePortableOpacityMaskState(Brushes.White),
            new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new ProGPU.Wpf.Interop.PortableSize(42, 24),
                HasClipToBounds = true,
                ClipToBounds = true
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var state = sink.RetainedVisualStates[0];
        AssertReplayRect(0, 0, 42, 24, state.ClipBounds);
        AssertReplayRect(0, 0, 42, 24, state.OpacityMaskBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeSynthesizesCellClipFromPortableClipToBoundsState()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new ProGPU.Wpf.Interop.PortableSize(55, 18),
            HasClipToBounds = true,
            ClipToBounds = true
        });

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 55, 18, state.ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeIntersectsLayoutAndExplicitClipsForRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            new PortableVisualState
            {
                HasClip = true,
                Clip = new PortableRectangleClipGeometry(0, 0, 50, 50)
            },
            new PortableVisualLayoutState
            {
                HasLayoutClip = true,
                LayoutClip = new PortableRectangleClipGeometry(10, 12, 60, 70)
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        AssertReplayRect(10, 12, 40, 38, sink.RetainedVisualStates[0].ClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreePreservesIntersectedPortableVisualClipsAsNativeCombinedGeometryBeforeStaleBoundsMetadata()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            new PortableVisualState
            {
                HasClip = true,
                Clip = new PortableNonRectangleClipGeometry(0, 0, 100, 50, new PortableRect(-100, -100, 1, 1))
            },
            new PortableVisualLayoutState
            {
                HasLayoutClip = true,
                LayoutClip = new PortableRectangleClipGeometry(10, 12, 60, 70, new PortableRect(-200, -200, 1, 1))
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeGeometryClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.Clips);
        var clip = Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(PortableGeometryPathKind.Combined, clip.Kind);
        Assert.NotNull(clip.PathA);
        Assert.NotNull(clip.PathB);
        Assert.Equal(1, clip.CombineOperation);
        Assert.Equal(new PortableRect(10, 12, 60, 38), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreePreservesMixedPortableAndRectangleVisualClipsAsNativeCombinedGeometry()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            new PortableVisualState
            {
                HasClip = true,
                Clip = new PortableNonRectangleClipGeometry(0, 0, 100, 50, new PortableRect(-100, -100, 1, 1))
            },
            new PortableVisualLayoutState
            {
                HasLayoutClip = true,
                LayoutClip = new PortableRect(10, 12, 60, 70)
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeGeometryClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.Clips);
        var clip = Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(PortableGeometryPathKind.Combined, clip.Kind);
        Assert.NotNull(clip.PathA);
        Assert.NotNull(clip.PathB);
        Assert.Equal(1, clip.CombineOperation);
        Assert.Equal(new PortableRect(10, 12, 60, 38), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeUsesNativePortableClipBoundsBeforeStaleMetadataFallback()
    {
        var root = new FakePortableVisualStateAndLayoutVisual(
            new PortableVisualState
            {
                HasClip = true,
                Clip = new PortableUnfilledUnstrokedTriangleGeometry(0, 0, 100, 50, new PortableRect(-100, -100, 1, 1))
            },
            new PortableVisualLayoutState
            {
                HasLayoutClip = true,
                LayoutClip = new PortableRectangleClipGeometry(10, 12, 60, 70, new PortableRect(-200, -200, 1, 1))
            });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeGeometryClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.Clips);
        var clip = Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(PortableGeometryPathKind.Combined, clip.Kind);
        Assert.NotNull(clip.PathA);
        Assert.NotNull(clip.PathB);
        Assert.Equal(1, clip.CombineOperation);
        Assert.Equal(new PortableRect(10, 12, 60, 38), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersVisualScrollableAreaClipIntoRetainedOwnerScopes()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        Assert.Null(sink.RetainedVisualStates[0].ClipBounds);
        AssertReplayRect(2, 3, 40, 50, sink.RetainedVisualStates[0].OuterClipBounds);
        Assert.Null(sink.RetainedVisualStates[1].ClipBounds);
        Assert.Null(sink.RetainedVisualStates[1].OuterClipBounds);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersLocalAndScrollableClipsIntoSeparateRetainedState()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasClip = true,
            Clip = new PortableRectangleClipGeometry(1, 2, 30, 40),
            HasScrollableAreaClip = true,
            ScrollableAreaClip = new PortableRect(10, 20, 80, 90)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var rootState = sink.RetainedVisualStates[0];
        Assert.Equal(new Vector2(10, 20), rootState.Offset);
        AssertReplayRect(1, 2, 30, 40, rootState.ClipBounds);
        AssertReplayRect(10, 20, 80, 90, rootState.OuterClipBounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualUsesCurrentOwnerBranch()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasOpacity = true,
            Opacity = 0.75
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        Assert.Equal(new Vector2(10, 20), sink.RetainedVisualStates[0].Offset);
        Assert.Equal(0.75f, sink.RetainedVisualStates[0].Opacity);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersNativeEffectCacheAndOpacityIntoRetainedOwnerScope()
    {
        var effect = new FakeBlurEffect(4);
        var cacheMode = new object();
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasEffect = true,
                Effect = effect,
                HasCacheMode = true,
                CacheMode = cacheMode,
                HasOpacity = true,
                Opacity = 0.6,
                HasOffset = true,
                Offset = new PortablePoint(2, 3)
            })
        {
            Bounds = new FakeRect(10, 20, 30, 40)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        var blur = Assert.IsType<ProGpuBlurEffect>(state.Effect);
        Assert.Equal(4, blur.BlurRadius);
        Assert.True(state.CacheAsLayer);
        Assert.Equal(new Vector2(12, 23), state.Offset);
        Assert.Equal(new Vector2(30, 40), state.Size);
        Assert.Equal(0.6f, state.Opacity);
        AssertReplayRect(10, 20, 30, 40, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-10, transform.M41);
        Assert.Equal(-20, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeLowersOpacityMaskAndNativeEffectIntoRetainedOwnerScope()
    {
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            visualState)
        {
            Bounds = new FakeRect(10, 20, 30, 40)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.IsType<ProGpuBlurEffect>(state.Effect);
        Assert.Same(Brushes.White, state.OpacityMask);
        AssertReplayRect(0, 0, 30, 40, state.OpacityMaskBounds);
        AssertReplayRect(10, 20, 30, 40, state.ContentBounds);
        Assert.Empty(sink.OpacityMasks);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-10, transform.M41);
        Assert.Equal(-20, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPortableGeometryRenderDataWithoutManagedGeometry()
    {
        var geometry = new PortableNonRectangleClipGeometry(3, 4, 50, 20);
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 50, 20, state.OpacityMaskBounds);
        AssertReplayRect(3, 4, 50, 20, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-3, transform.M41);
        Assert.Equal(-4, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPortableRectangleGeometryPathPoints()
    {
        var geometry = new PortableRectangleClipGeometry(10, 20, 30, 40, new PortableRect(0, 0, 1, 1));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 30, 40, state.OpacityMaskBounds);
        AssertReplayRect(10, 20, 30, 40, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-10, transform.M41);
        Assert.Equal(-20, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPortableLineGeometryPathPoints()
    {
        var geometry = new PortableNonRectangleClipGeometry(3, 4, 50, 20, new PortableRect(0, 0, 1, 1));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 50, 20, state.OpacityMaskBounds);
        AssertReplayRect(3, 4, 50, 20, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-3, transform.M41);
        Assert.Equal(-4, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreePreservesRetainedMetadataOnlyPortableBoundsFallback()
    {
        var geometry = new PortableMetadataOnlyGeometry(new PortableRect(12, 14, 32, 24));
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            CreatePortableEffectState(new FakeBlurEffect(4)));
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(12, 14, 32, 24, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-12, transform.M41);
        Assert.Equal(-14, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeRejectsStaleRetainedMetadataWhenPortablePathDataCannotBeBound()
    {
        var geometry = new PortableInvalidPathGeometry(new PortableRect(12, 14, 32, 24));
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            CreatePortableEffectState(new FakeBlurEffect(4)));
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "DrawNativeGeometry", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.Null(state.ContentBounds);
        Assert.Null(state.Size);
        Assert.Empty(sink.NativeTransforms);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void MediaGeometryBoundsReaderUsesNativePortablePathBeforeGenericBoundsFallback()
    {
        var geometry = new PortableThrowingBoundsPathGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            Bounds = new PortableRect(0, 0, 1, 1),
            Transform = PortableMatrix3x2.Identity,
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(7, 9),
                    IsClosed = false,
                    IsFilled = false,
                    Segments =
                    [
                        PortablePathSegment.Line(
                            new PortablePoint(47, 9),
                            isSmoothJoin: false,
                            isStroked: false)
                    ]
                }
            ]
        });

        var hasBounds = WpfMediaGeometryBoundsReader.TryGetGeometryBounds(geometry, out var bounds);

        Assert.True(hasBounds);
        AssertReplayRect(7, 9, 40, 0, bounds);
    }

    [Fact]
    public void MediaGeometryBoundsReaderPreservesMetadataOnlyPortableBoundsFallback()
    {
        var geometry = new PortableThrowingBoundsPathGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            Bounds = new PortableRect(12, 14, 32, 24),
            Transform = PortableMatrix3x2.Identity,
            Figures = []
        });

        var hasBounds = WpfMediaGeometryBoundsReader.TryGetGeometryBounds(geometry, out var bounds);

        Assert.True(hasBounds);
        AssertReplayRect(12, 14, 32, 24, bounds);
    }

    [Fact]
    public void MediaGeometryBoundsReaderRejectsStalePortableMetadataWhenPathDataCannotBeBound()
    {
        var geometry = new PortableThrowingBoundsPathGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            Bounds = new PortableRect(12, 14, 32, 24),
            Transform = PortableMatrix3x2.Identity,
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(double.NaN, 9),
                    IsClosed = false,
                    IsFilled = false,
                    Segments =
                    [
                        PortablePathSegment.Line(
                            new PortablePoint(double.NaN, 9),
                            isSmoothJoin: false,
                            isStroked: false)
                    ]
                }
            ]
        });

        var hasBounds = WpfMediaGeometryBoundsReader.TryGetGeometryBounds(geometry, out var bounds);

        Assert.False(hasBounds);
        Assert.Equal(default, bounds);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromNativeMediaGeometryWithoutGenericBoundsFallback()
    {
        var geometry = CreateThrowingBoundsCurvedPathGeometry(new Rect(5, 6, 40, 30));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeMediaGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 40, 30, state.OpacityMaskBounds);
        AssertReplayRect(5, 6, 40, 30, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.NativeDrawGeometries);
        Assert.Same(geometry, Assert.Single(sink.NativeMediaDrawGeometries).Geometry);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPortableQuadraticGeometryPathPoints()
    {
        var geometry = new PortableQuadraticCurveGeometry(new PortableRect(0, 0, 1, 1));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 100, 50, state.OpacityMaskBounds);
        AssertReplayRect(3, 4, 100, 50, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-3, transform.M41);
        Assert.Equal(-4, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPortableArcGeometryPathPoints()
    {
        var geometry = new PortableArcCurveGeometry(new PortableRect(0, 0, 1, 1));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 100, 50, state.OpacityMaskBounds, precision: 4);
        AssertReplayRect(3, 4, 100, 50, state.ContentBounds, precision: 4);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-3, transform.M41, 4);
        Assert.Equal(-4, transform.M42, 4);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPortableCombinedGeometryOperands()
    {
        var geometry = new PortableCombinedGeometry(
            combineOperation: 2,
            new PortableQuadraticCurveGeometry(new PortableRect(0, 0, 1, 1)),
            new PortableRectangleClipGeometry(200, 10, 20, 30, new PortableRect(0, 0, 1, 1)),
            new PortableRect(0, 0, 1, 1));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateGeometryRenderData(geometry),
            visualState);
        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawNativeGeometry", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 217, 50, state.OpacityMaskBounds);
        AssertReplayRect(3, 4, 217, 50, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-3, transform.M41);
        Assert.Equal(-4, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromPrimitiveRenderDataThroughNativeBoundsSink()
    {
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            visualState);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 30, 40, state.OpacityMaskBounds);
        AssertReplayRect(1, 2, 30, 40, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-1, transform.M41);
        Assert.Equal(-2, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromLocalLineGeometryRenderDataWithoutGenericBoundsFallback()
    {
        var geometry = new LineGeometry(new Point(1, 2), new Point(31, 2));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateStrokedGeometryRenderData(geometry, new MediaPen(Brushes.Black, 4)),
            visualState);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "DrawLine", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 34, 4, state.OpacityMaskBounds);
        AssertReplayRect(-1, 0, 34, 4, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(1, transform.M41);
        Assert.Equal(0, transform.M42);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromNativeRectangleClipRenderData()
    {
        var clip = new PortableRectangleClipGeometry(10, 12, 8, 10);
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateClippedRenderData(clip, Brushes.Green),
            visualState);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "PushNativeClip", "DrawRectangle", "Pop", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 8, 10, state.OpacityMaskBounds);
        AssertReplayRect(10, 12, 8, 10, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-10, transform.M41);
        Assert.Equal(-12, transform.M42);
        AssertReplayRect(10, 12, 8, 10, Assert.Single(sink.NativeClips));
        Assert.Equal(0, clip.ReflectedGeometryProbeCount);
        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeInfersRetainedBoundsFromLocalPrimitiveClipRenderDataWithoutGenericBoundsFallback()
    {
        var clip = CreateThrowingBoundsClosedPolylinePathGeometry(
            new Point(5, 6),
            new Point(45, 6),
            new Point(20, 36));
        var visualState = CreatePortableOpacityMaskState(Brushes.White);
        visualState.HasEffect = true;
        visualState.Effect = new FakeBlurEffect(4);
        var root = new FakePortableVisualStateDrawingVisual(
            CreateClippedRenderData(clip, Brushes.Green),
            visualState);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushTransform", "PushClip", "DrawRectangle", "Pop", "Pop", "PopVisualOwner" },
            sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(0, 0, 26, 30, state.OpacityMaskBounds);
        AssertReplayRect(5, 6, 26, 30, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Same(clip, Assert.Single(sink.Clips));
        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result.RenderData);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeCacheState()
    {
        var cacheMode = new object();
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(2, 3),
                HasOpacity = true,
                Opacity = 0.35,
                HasClip = true,
                Clip = new PortableRectangleClipGeometry(10, 11, 20, 30),
                HasCacheMode = true,
                CacheMode = cacheMode
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.Null(state.Effect);
        Assert.True(state.CacheAsLayer);
        Assert.Equal(new Vector2(7, 9), state.Offset);
        Assert.Equal(new Vector2(70, 80), state.Size);
        Assert.Equal(0.35f, state.Opacity);
        AssertReplayRect(5, 5, 20, 30, state.ClipBounds);
        AssertReplayRect(5, 6, 70, 80, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeCacheVisualScrollableAreaClip()
    {
        var cacheMode = new object();
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(2, 3),
                HasCacheMode = true,
                CacheMode = cacheMode,
                HasScrollableAreaClip = true,
                ScrollableAreaClip = new PortableRect(10, 12, 20, 25)
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.True(state.CacheAsLayer);
        Assert.Equal(new Vector2(7, 9), state.Offset);
        Assert.Equal(new Vector2(70, 80), state.Size);
        Assert.Null(state.ClipBounds);
        AssertReplayRect(10, 12, 20, 25, state.OuterClipBounds);
        AssertReplayRect(5, 6, 70, 80, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualReappliesNativeEffectWithOuterTransform()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
                HasOffset = true,
                Offset = new PortablePoint(11, 13),
                HasClip = true,
                Clip = new PortableRectangleClipGeometry(10, 12, 20, 25),
                HasEffect = true,
                Effect = new FakeBlurEffect(4)
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new object[] { root }, sink.VisualOwners);
        var state = Assert.Single(sink.RetainedVisualStates);
        var blur = Assert.IsType<ProGpuBlurEffect>(state.Effect);
        Assert.Equal(4, blur.BlurRadius);
        Assert.False(state.CacheAsLayer);
        Assert.Equal(new Vector2(11, 13), state.Offset);
        Assert.Equal(8, state.Transform.M41);
        Assert.Equal(10, state.Transform.M42);
        AssertReplayRect(5, 6, 20, 25, state.ClipBounds);
        Assert.Equal(new Vector2(70, 80), state.Size);
        AssertReplayRect(5, 6, 70, 80, state.ContentBounds);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(-5, transform.M41);
        Assert.Equal(-6, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualRejectsMultipleNativeEffectSources()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeBlurEffect(4),
                HasBitmapEffect = true,
                BitmapEffect = new FakeBlurBitmapEffect(6)
            })
        {
            Bounds = new FakeRect(5, 6, 70, 80)
        };
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeRenderer();

        Assert.False(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.False(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(default, result);
        Assert.Empty(sink.Operations);
        Assert.Empty(sink.VisualOwners);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualPreservesOpacityMaskNativeState()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));
        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var renderer = new WpfVisualTreeRenderer();

        Assert.True(renderer.CanReplaySubtreeIntoCurrentRetainedVisual(root));
        Assert.True(renderer.TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new[] { "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var state = sink.RetainedVisualStates[0];
        Assert.Same(Brushes.White, state.OpacityMask);
        AssertReplayRect(1, 2, 100, 50, state.OpacityMaskBounds);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeRegistersRenderDataResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var renderData = CreateRenderData(brush);
        var root = new FakeDrawingVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new object[] { root }, sink.VisualOwners);
        Assert.DoesNotContain(root.Children, sink.VisualDependencies);
        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeUsesPortableVisualScrollableAreaClipWithoutPropertyProbe()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        AssertReplayRect(2, 3, 40, 50, state.OuterClipBounds);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeRegistersUiElementDrawingContentResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var renderData = CreateRenderData(brush);
        var root = new FakeUiElementVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new object[] { root }, sink.VisualOwners);
        Assert.DoesNotContain(root.Children, sink.VisualDependencies);
        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeRegistersNestedRenderDataResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var nestedBrush = new FakeResource();
        var nestedDrawing = new FakeDrawingResource
        {
            Brush = nestedBrush
        };
        var renderData = CreateRenderData(brush, nestedDrawing);
        var root = new FakeDrawingVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(nestedDrawing, sink.VisualDependencies);
        Assert.DoesNotContain(nestedBrush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeRegistersNestedVisualStateResourcesAsRetainedDependencies()
    {
        var shaderEffect = new FakeShaderEffect(new byte[] { 0, 3, 0, 0, 1, 2, 3, 4 });
        var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Contains(shaderEffect, sink.VisualDependencies);
        Assert.Contains(sink.VisualDependencies, dependency => dependency is PortablePixelShader);
        Assert.DoesNotContain(shaderEffect.PixelShader, sink.VisualDependencies);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeDoesNotRegisterVisualChildrenCollectionAsReflectedDependency()
    {
        var root = new FakeVisual();
        var child = new FakeDrawingVisual(CreateRenderData(Brushes.Green));
        root.Children.Add(child);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new object[] { root, child }, sink.VisualOwners);
        Assert.DoesNotContain(root.Children, sink.VisualDependencies);
        Assert.DoesNotContain(child, sink.VisualDependencies);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ChildEdgeCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void TryReplaySubtreeIntoCurrentRetainedVisualRegistersRenderDataResourcesAsRetainedDependencies()
    {
        var brush = Brushes.Green;
        var renderData = CreateRenderData(brush);
        var root = new FakeDrawingVisual(renderData);
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        Assert.True(new WpfVisualTreeRenderer().TryReplaySubtreeIntoCurrentRetainedVisual(
            root,
            sink,
            resources: null,
            imageSourceAdapter: null,
            out var result));

        Assert.Equal(new object[] { root }, sink.VisualOwners);
        Assert.DoesNotContain(root.Children, sink.VisualDependencies);
        Assert.Contains(renderData, sink.VisualDependencies);
        Assert.Contains(brush, sink.VisualDependencies);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeKeepsOpacityMaskAsNativeRetainedOwnerState()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushVisualOwner", "ApplyVisualState", "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner", "PopVisualOwner" },
            sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Equal(2, sink.RetainedVisualStates.Count);
        var state = sink.RetainedVisualStates[0];
        Assert.Same(Brushes.White, state.OpacityMask);
        AssertReplayRect(1, 2, 100, 50, state.OpacityMaskBounds);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeFallsBackWhenRetainedOpacityMaskCannotBeAdapted()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(new object()))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Equal(new object[] { root, root.Children[0] }, sink.VisualOwners);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeKeepsNonRectangleClipInCommandScopeForNativeOwnerSink()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = new PortableNonRectangleClipGeometry(0, 0, 100, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeUsesNativePortableGeometryClipForNonRectangleVisualClip()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = new PortableNonRectangleClipGeometry(0, 0, 100, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeGeometryClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.Clips);
        var clip = Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(PortableGeometryPathKind.Path, clip.Kind);
        Assert.Equal(new PortableRect(0, 0, 100, 50), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeKeepsRetracedPortablePathClipOutOfRetainedRectangleState()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = new PortableRetracedRectangleClipGeometry(0, 0, 100, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeGeometryClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.Clips);
        var clip = Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(PortableGeometryPathKind.Path, clip.Kind);
        Assert.Equal(new PortableRect(0, 0, 100, 50), clip.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeUsesNativeMediaGeometryClipForLocalNonRectangleVisualClip()
    {
        var clip = CreateTrianglePathGeometry();
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClip = true,
            Clip = clip
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new NativeGeometryTestSink { AcceptRetainedVisualOwners = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeMediaGeometryClip", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Empty(sink.RetainedVisualStates);
        Assert.Empty(sink.Clips);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Same(clip, Assert.Single(sink.NativeMediaGeometryClips));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesOffsetAndOpacityAroundContentAndChildren()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasOpacity = true,
            Opacity = 0.5
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "PushOpacity", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(10, transform.M41);
        Assert.Equal(20, transform.M42);
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesWpfVisualOffsetAroundContent()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(16, 24)
            });
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(16, transform.M41);
        Assert.Equal(24, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeLowersWpfVisualOffsetIntoRetainedOwnerState()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(16, 24)
            });
        var sink = new TestSink { AcceptRetainedVisualOwners = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualOwner", "ApplyVisualState", "DrawRectangle", "PopVisualOwner" }, sink.Operations);
        var state = Assert.Single(sink.RetainedVisualStates);
        Assert.Equal(new Vector2(16, 24), state.Offset);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesWpfVisualTransformAroundContent()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4))
            });
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "DrawRectangle", "Pop" }, sink.Operations);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(3, transform.M41);
        Assert.Equal(4, transform.M42);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsUnsupportedContentWithoutThrowing()
    {
        var root = new FakeDrawingVisual(new object());
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(0, result.ContentCount);
        Assert.Equal(1, result.UnsupportedContentCount);
        Assert.Empty(sink.Operations);
    }

    [Fact]
    public void ReplaySubtreeAdaptsWpfShapedTransformAndClip()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasClip = true,
            Clip = new PortableRectangleClipGeometry(0, 0, 100, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTransform", "PushNativeClip", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        Assert.Empty(sink.Transforms);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(3, transform.M41);
        Assert.Equal(4, transform.M42);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(0, 0, 100, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesOpacityMaskWhenBoundsAreAvailable()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White))
        {
            Bounds = new FakeRect(1, 2, 100, 50)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 100, 50), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeInfersOpacityMaskBoundsFromRenderDataContent()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            CreatePortableOpacityMaskState(Brushes.White));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 30, 40), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeInfersOpacityMaskBoundsFromTransformedRenderDataContent()
    {
        var root = new FakePortableVisualStateDrawingVisual(
            CreateTransformedRenderData(
                new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 7, 9)),
                Brushes.Green),
            CreatePortableOpacityMaskState(Brushes.White));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "PushTransform", "DrawRectangle", "Pop", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(8, 11, 30, 40), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(3, 3, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeInfersOpacityMaskBoundsFromChildRenderDataContent()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushOpacityMask", "DrawRectangle", "Pop" }, sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(1, 2, 30, 40), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeProjectsChildVisualStateWhenInferringOpacityMaskBounds()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White));
        root.Children.Add(new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 5, 7)),
                HasOffset = true,
                Offset = new PortablePoint(10, 20),
                HasClip = true,
                Clip = new PortableRectangleClipGeometry(5, 6, 10, 12)
            }));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushOpacityMask", "PushTransform", "PushTransform", "PushNativeClip", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" },
            sink.Operations);
        var mask = Assert.Single(sink.OpacityMasks);
        Assert.Same(Brushes.White, mask.OpacityMask);
        Assert.Equal(new Rect(20, 33, 10, 12), mask.Bounds);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeDoesNotInferOpacityMaskBoundsFromUnsupportedChildVisualState()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableOpacityMaskState(Brushes.White));
        root.Children.Add(new FakePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasTransform = true,
                Transform = new object()
            }));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.OpacityMasks);
        Assert.Equal(2, result.UnsupportedVisualStateCount);
        Assert.Equal(2, result.VisualCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesGuidelineCollectionsAsNoOpScope()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasSnappingGuidelinesX = true,
            SnappingGuidelinesX = new[] { 10d }
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesPortableVisualGuidelinesWithoutReflection()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1,
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = new[] { 10d },
                HasSnappingGuidelinesY = true,
                SnappingGuidelinesY = new[] { 20d, 21d }
            });

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushGuidelineSetObject", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeReusesPortableVisualGuidelineSetWrapperUntilArraysChange()
    {
        var guidelinesX = new[] { 10d };
        var guidelinesY = new[] { 20d };
        var root = new MutablePortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = guidelinesX,
                HasSnappingGuidelinesY = true,
                SnappingGuidelinesY = guidelinesY
            });
        var renderer = new WpfVisualTreeRenderer();

        var firstSink = new TestSink();
        renderer.ReplaySubtree(root, firstSink);
        var firstGuidelineSet = Assert.Single(firstSink.GuidelineSets);

        var secondSink = new TestSink();
        renderer.ReplaySubtree(root, secondSink);
        Assert.Same(firstGuidelineSet, Assert.Single(secondSink.GuidelineSets));

        guidelinesX[0] = 11d;
        var mutatedValueSink = new TestSink();
        renderer.ReplaySubtree(root, mutatedValueSink);
        Assert.Same(firstGuidelineSet, Assert.Single(mutatedValueSink.GuidelineSets));

        root.State = new PortableVisualState
        {
            HasSnappingGuidelinesX = true,
            SnappingGuidelinesX = new[] { 12d },
            HasSnappingGuidelinesY = true,
            SnappingGuidelinesY = guidelinesY
        };

        var changedArraySink = new TestSink();
        renderer.ReplaySubtree(root, changedArraySink);
        Assert.NotSame(firstGuidelineSet, Assert.Single(changedArraySink.GuidelineSets));
    }

    [Fact]
    public void ReplaySubtreeCachesPortableVisualAndLayoutStateDuringReplayPass()
    {
        var root = new CountingPortableVisualStateAndLayoutDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(3, 4),
                HasClip = true,
                Clip = new PortableRectangleClipGeometry(1, 2, 40, 50),
                HasScrollableAreaClip = true,
                ScrollableAreaClip = new PortableRect(0, 0, 80, 70),
                HasOpacity = true,
                Opacity = 0.8,
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = new[] { 10d },
                HasSnappingGuidelinesY = true,
                SnappingGuidelinesY = new[] { 20d }
            },
            new PortableVisualLayoutState
            {
                HasRenderSize = true,
                RenderSize = new PortableSize(100, 90),
                HasLayoutClip = true,
                LayoutClip = new PortableRectangleClipGeometry(0, 0, 60, 55)
            });
        var renderer = new WpfVisualTreeRenderer();

        var firstResult = renderer.ReplaySubtree(root, new TestSink());

        Assert.Equal(1, root.VisualStateQueryCount);
        Assert.Equal(1, root.VisualLayoutStateQueryCount);
        Assert.Equal(0, firstResult.UnsupportedVisualStateCount);

        var secondResult = renderer.ReplaySubtree(root, new TestSink());

        Assert.Equal(2, root.VisualStateQueryCount);
        Assert.Equal(2, root.VisualLayoutStateQueryCount);
        Assert.Equal(0, secondResult.UnsupportedVisualStateCount);
    }

    [Fact]
    public void ReplaySubtreeAppliesScrollableAreaClipAsRectangleClip()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesVisualScrollableAreaClipAsRectangleClip()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableScrollableAreaClipState(2, 3, 40, 50));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesLayoutClipAsRectangleClip()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasLayoutClip = true,
            LayoutClip = new PortableRectangleClipGeometry(2, 3, 40, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesClipToBoundsRenderSizeAsRectangleClip()
    {
        var root = new FakePortableVisualLayoutVisual(new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new ProGPU.Wpf.Interop.PortableSize(40, 50),
            HasClipToBounds = true,
            ClipToBounds = true,
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushNativeClip", "DrawRectangle", "Pop" }, sink.Operations);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(0, 0, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeProjectsScrollableAreaClipOutsideVisualOffsetForFallbackRendering()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(10, 20),
            HasScrollableAreaClip = true,
            ScrollableAreaClip = new PortableRect(2, 3, 40, 50)
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(
            new[] { "PushTransform", "PushTransform", "PushNativeClip", "PushTransform", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" },
            sink.Operations);
        Assert.Equal(3, sink.NativeTransforms.Count);
        Assert.Equal(10, sink.NativeTransforms[0].M41);
        Assert.Equal(20, sink.NativeTransforms[0].M42);
        Assert.Equal(-10, sink.NativeTransforms[1].M41);
        Assert.Equal(-20, sink.NativeTransforms[1].M42);
        Assert.Equal(10, sink.NativeTransforms[2].M41);
        Assert.Equal(20, sink.NativeTransforms[2].M42);
        var clip = Assert.Single(sink.NativeClips);
        AssertReplayRect(2, 3, 40, 50, clip);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsUnsupportedVisualEffectAndRenderingHintState()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasEffect = true,
            Effect = new FakeBlurEffect(8),
            HasBitmapEffect = true,
            BitmapEffect = new object(),
            HasCacheMode = true,
            CacheMode = new object(),
            HasEdgeMode = true,
            EdgeMode = new FakeRenderingHint("Aliased"),
            HasBitmapScalingMode = true,
            BitmapScalingMode = new FakeRenderingHint("NearestNeighbor"),
            HasClearTypeHint = true,
            ClearTypeHint = new FakeRenderingHint("Enabled"),
            HasTextRenderingMode = true,
            TextRenderingMode = new FakeRenderingHint("Aliased"),
            HasTextHintingMode = true,
            TextHintingMode = new FakeRenderingHint("Fixed")
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "PushEdgeMode", "PushTextRenderingMode", "PushTextHintingMode", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "NearestNeighbor" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Fixed" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(3, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesPortableVisualRenderingHintsWithoutReflection()
    {
        var root = new ThrowingPortableVisualStateDrawingVisual(
            CreateRenderData(Brushes.Green),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1,
                HasBitmapScalingMode = true,
                BitmapScalingMode = new FakeRenderingHint("NearestNeighbor"),
                HasEdgeMode = true,
                EdgeMode = new FakeRenderingHint("Aliased"),
                HasTextRenderingMode = true,
                TextRenderingMode = new FakeRenderingHint("ClearType"),
                HasTextHintingMode = true,
                TextHintingMode = new FakeRenderingHint("Fixed")
            });

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "PushEdgeMode", "PushTextRenderingMode", "PushTextHintingMode", "DrawRectangle", "Pop", "Pop", "Pop", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "NearestNeighbor" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Fixed" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, root.ReflectedStateProbeCount);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplayAppliesPortableGeometryDrawingStateWithoutReflection()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new RectangleGeometry(new Rect(1, 2, 10, 12)),
            HasBrush = true,
            Brush = Brushes.Green
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.DrawRectangles);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Equal(new Rect(1, 2, 10, 12), draw.Rectangle);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativePortableGeometryDrawingWhenAvailable()
    {
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeGeometry" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.NativeDrawGeometries);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(PortableGeometryPathKind.Path, draw.Geometry.Kind);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativeMediaGeometryDrawingForLocalNonPrimitiveGeometry()
    {
        var geometry = CreateTrianglePathGeometry();
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeMediaGeometry" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.NativeDrawGeometries);
        var draw = Assert.Single(sink.NativeMediaDrawGeometries);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Same(geometry, draw.Geometry);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsTypedRectGeometryStateAsNativeRectangleWithoutMediaGeometryFallback()
    {
        var pen = new MediaPen(Brushes.Black, 2);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new Rect(4, 5, 20, 30),
            HasBrush = true,
            Brush = Brushes.Green,
            HasPen = true,
            Pen = pen
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.NativeDrawRectangles);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Same(pen, draw.Pen);
        Assert.Equal(new WpfReplayRect(4, 5, 20, 30), draw.Rectangle);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsPortableRectGeometryStateAsRectangleWithoutMediaGeometryFallback()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new PortableRect(8, 9, 24, 34),
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.DrawRectangles);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new Rect(8, 9, 24, 34), draw.Rectangle);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsLocalRectangleGeometryStateAsNativeRectangleWithoutMediaGeometryFallback()
    {
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new RectangleGeometry(new Rect(4, 5, 20, 30));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green,
            HasPen = true,
            Pen = pen
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.DrawRectangles);
        var draw = Assert.Single(sink.NativeDrawRectangles);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Same(pen, draw.Pen);
        Assert.Equal(new WpfReplayRect(4, 5, 20, 30), draw.Rectangle);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsLocalRoundedRectangleGeometryStateAsNativeRoundedRectangle()
    {
        var geometry = new RectangleGeometry(new Rect(8, 9, 24, 34))
        {
            RadiusX = 6,
            RadiusY = 7
        };
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeRoundedRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.DrawRoundedRectangles);
        var draw = Assert.Single(sink.NativeDrawRoundedRectangles);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new WpfReplayRect(8, 9, 24, 34), draw.Rectangle);
        Assert.Equal(6, draw.RadiusX);
        Assert.Equal(7, draw.RadiusY);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsLocalRoundedRectangleGeometryStateAsRoundedRectangle()
    {
        var geometry = new RectangleGeometry(new Rect(8, 9, 24, 34))
        {
            RadiusX = 10,
            RadiusY = 11
        };
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawRoundedRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.DrawRoundedRectangles);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new Rect(8, 9, 24, 34), draw.Rectangle);
        Assert.Equal(10, draw.RadiusX);
        Assert.Equal(11, draw.RadiusY);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsTransformedLocalRectangleGeometryStateAsNativeRectangle()
    {
        var geometry = new RectangleGeometry(new Rect(8, 9, 24, 34))
        {
            Transform = new MatrixTransform(1, 0, 0, 1, 2, 3)
        };
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeRectangle" }, sink.Operations);
        Assert.Empty(sink.NativeDrawRoundedRectangles);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.NativeDrawRectangles);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new WpfReplayRect(10, 12, 24, 34), draw.Rectangle);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsLocalEllipseGeometryStateAsNativeEllipseWithoutMediaGeometryFallback()
    {
        var pen = new MediaPen(Brushes.Black, 2);
        var geometry = new EllipseGeometry(new Point(4, 5), 20, 30);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green,
            HasPen = true,
            Pen = pen
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeEllipse" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.DrawEllipses);
        var draw = Assert.Single(sink.NativeDrawEllipses);
        Assert.Same(Brushes.Green, draw.Brush);
        Assert.Same(pen, draw.Pen);
        Assert.Equal(new WpfReplayPoint(4, 5), draw.Center);
        Assert.Equal(20, draw.RadiusX);
        Assert.Equal(30, draw.RadiusY);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsLocalEllipseGeometryStateAsEllipseWithoutMediaGeometryFallback()
    {
        var geometry = new EllipseGeometry(new Point(8, 9), 24, 34);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawEllipse" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.DrawEllipses);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new Point(8, 9), draw.Center);
        Assert.Equal(24, draw.RadiusX);
        Assert.Equal(34, draw.RadiusY);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDrawsTransformedLocalEllipseGeometryStateAsNativeEllipse()
    {
        var geometry = new EllipseGeometry(new Point(8, 9), 24, 34)
        {
            Transform = new MatrixTransform(2, 0, 0, 3, 2, -1)
        };
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawNativeEllipse" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        var draw = Assert.Single(sink.NativeDrawEllipses);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Equal(new WpfReplayPoint(18, 26), draw.Center);
        Assert.Equal(48, draw.RadiusX);
        Assert.Equal(102, draw.RadiusY);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayKeepsSkewedLocalEllipseGeometryStateOnGenericGeometryPath()
    {
        var geometry = new EllipseGeometry(new Point(8, 9), 24, 34)
        {
            Transform = new MatrixTransform(1, 0.25, 0, 1, 2, 3)
        };
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Blue
        });
        var sink = new NativePrimitiveTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawGeometry" }, sink.Operations);
        Assert.Empty(sink.NativeDrawEllipses);
        var draw = Assert.Single(sink.DrawGeometries);
        Assert.Same(Brushes.Blue, draw.Brush);
        Assert.Null(draw.Pen);
        Assert.Same(geometry, draw.Geometry);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativeRectangleClipForPortableTileBrushFill()
    {
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.NativeGeometryClips);
        AssertReplayRect(1, 2, 10, 12, Assert.Single(sink.NativeClips));
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativePortableGeometryClipForNonRectangleTileBrushFill()
    {
        var geometry = new PortableNonRectangleClipGeometry(1, 2, 10, 12);
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeGeometryClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Single(sink.NativeGeometryClips);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesPortablePathBoundsForTileBrushFillBeforeStaleGeometryMetadata()
    {
        var geometry = new PortableQuadraticCurveGeometry(new PortableRect(0, 0, 1, 1));
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeGeometryClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Single(sink.NativeGeometryClips);
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(10, transform.M11, 4);
        Assert.Equal(50.0 / 12.0, transform.M22, 4);
        Assert.Equal(3, transform.M41, 4);
        Assert.Equal(4, transform.M42, 4);
        Assert.Same(Brushes.Red, Assert.Single(sink.DrawRectangles).Brush);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativeRectangleClipForMediaRectangleTileBrushFill()
    {
        var geometry = new RectangleGeometry(new Rect(1, 2, 10, 12));
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.Clips);
        Assert.Empty(sink.NativeGeometryClips);
        AssertReplayRect(1, 2, 10, 12, Assert.Single(sink.NativeClips));
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativeMediaGeometryClipForMediaNonRectangleTileBrushFill()
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(1, 2),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new LineSegment(new Point(11, 2), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(6, 14), isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeMediaGeometryClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Empty(sink.Clips);
        Assert.Same(geometry, Assert.Single(sink.NativeMediaGeometryClips));
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesLocalPolylineTileBrushFillBoundsWithoutMediaGeometryBoundsFallback()
    {
        var geometry = CreateThrowingBoundsClosedPolylinePathGeometry(
            new Point(1, 2),
            new Point(11, 2),
            new Point(6, 14));
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeMediaGeometryClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Empty(sink.Clips);
        Assert.Same(geometry, Assert.Single(sink.NativeMediaGeometryClips));
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayUsesNativeMediaGeometryBoundsForCurvedTileBrushFillWithoutGenericBoundsFallback()
    {
        var geometry = CreateThrowingBoundsCurvedPathGeometry(new Rect(5, 6, 40, 30));
        var nestedDrawing = new FakeGeometryDrawing(
            new RectangleGeometry(new Rect(0, 0, 10, 12)),
            Brushes.Red);
        var tileBrush = new FakeDrawingTileBrush(nestedDrawing);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Contains("PushNativeMediaGeometryClip", sink.Operations);
        Assert.DoesNotContain("PushClip", sink.Operations);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Empty(sink.Clips);
        Assert.Same(geometry, Assert.Single(sink.NativeMediaGeometryClips));
        var transform = Assert.Single(sink.NativeTransforms);
        Assert.Equal(4, transform.M11, 4);
        Assert.Equal(2.5, transform.M22, 4);
        Assert.Equal(5, transform.M41, 4);
        Assert.Equal(6, transform.M42, 4);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayReusesVisualBrushRendererForTiledPortableVisualBrushFill()
    {
        var visual = new FakeDrawingVisual(CreateRenderData(Brushes.Red))
        {
            Bounds = new PortableRect(0, 0, 10, 12)
        };
        var tileBrush = new FakeVisualTileBrush(
            visual,
            viewport: new PortableRect(0, 0, 0.5, 1),
            tileMode: PortableTileMode.Tile);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new PortableRectangleClipGeometry(0, 0, 20, 12),
            HasBrush = true,
            Brush = tileBrush
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        Assert.Equal(2, sink.DrawRectangles.Count);
        Assert.All(sink.DrawRectangles, draw => Assert.Same(Brushes.Red, draw.Brush));
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void ReplayAppliesPortableGeometryDrawingStateWithoutTypeNameShape()
    {
        var drawing = new PortableGeometryStateHost(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new RectangleGeometry(new Rect(1, 2, 10, 12)),
            HasBrush = true,
            Brush = Brushes.Green
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        Assert.Same(Brushes.Green, Assert.Single(sink.DrawRectangles).Brush);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDoesNotReflectAbsentPortableGeometryDrawingState()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new RectangleGeometry(new Rect(1, 2, 10, 12))
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
    }

    [Fact]
    public void ReplaySkipsUnavailablePortableGeometryDrawingStateWithoutReflectionFallback()
    {
        var drawing = new UnavailablePortableGeometryDrawing();
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
    }

    [Fact]
    public void ReplayAppliesPortableImageDrawingStateWithoutReflection()
    {
        var source = new object();
        var drawing = new ThrowingPortableImageDrawing(new PortableImageDrawingState
        {
            HasImageSource = true,
            ImageSource = source,
            HasRect = true,
            Rect = new PortableRect(1, 2, 10, 12)
        });
        var sink = new TestSink();
        var adapter = new FakeImageSourceAdapter();

        var status = WpfDrawingReplay.Replay(drawing, sink, adapter.AdaptImageSource);

        Assert.Equal(new[] { "DrawImage" }, sink.Operations);
        var image = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, image.ImageSource);
        Assert.Equal(new Rect(1, 2, 10, 12), image.Rectangle);
        Assert.Same(source, adapter.LastImageSource);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplayDoesNotReflectAbsentPortableImageDrawingState()
    {
        var drawing = new ThrowingPortableImageDrawing(new PortableImageDrawingState());
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Unsupported, status);
    }

    [Fact]
    public void ReplaySkipsUnavailablePortableImageDrawingStateWithoutReflectionFallback()
    {
        var drawing = new UnavailablePortableImageDrawing();
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
    }

    [Fact]
    public void ReplayDoesNotReflectAbsentPortableGlyphRunDrawingState()
    {
        var drawing = new ThrowingPortableGlyphRunDrawing(new PortableGlyphRunDrawingState
        {
            HasForegroundBrush = true,
            ForegroundBrush = Brushes.Green
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Unsupported, status);
    }

    [Fact]
    public void ReplayUsesNativeSinkForPortableGlyphRunDrawingState()
    {
        var glyphRun = new ThrowingPortableGlyphRunSource(new PortableGlyphRun
        {
            GlyphIndices = new ushort[] { 7 },
            BaselineOrigin = new PortablePoint(2, 3),
            FontRenderingEmSize = 14,
            FontFamilyNames = new[] { "Arial" }
        });
        var drawing = new ThrowingPortableGlyphRunDrawing(new PortableGlyphRunDrawingState
        {
            HasGlyphRun = true,
            GlyphRun = glyphRun,
            HasForegroundBrush = true,
            ForegroundBrush = Brushes.Green
        });
        var sink = new NativeGlyphRunTestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
        var nativeGlyphRun = Assert.Single(sink.NativeGlyphRuns);
        Assert.Same(Brushes.Green, nativeGlyphRun.ForegroundBrush);
        var adaptedGlyphRun = Assert.IsType<WpfNativeGlyphRun>(nativeGlyphRun.GlyphRun);
        Assert.Equal(new ushort[] { 7 }, adaptedGlyphRun.GlyphIndices);
        Assert.Equal(14, adaptedGlyphRun.FontSize);
        Assert.Equal(new Vector2(2, 3), adaptedGlyphRun.Position);
        Assert.Empty(sink.GlyphRuns);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(0, glyphRun.ReflectedGlyphRunProbeCount);
    }

    [Fact]
    public void ReplaySkipsUnavailablePortableGlyphRunDrawingStateWithoutReflectionFallback()
    {
        var drawing = new UnavailablePortableGlyphRunDrawing();
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(drawing, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableDrawingStateWithoutGenericBoundsFallback()
    {
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(1, 2, 10, 12), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesTypedRectGeometryStateWithoutMediaGeometryFallback()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new Rect(4, 5, 20, 30)
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(4, 5, 20, 30), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesReplayRectGeometryStateWithoutMediaGeometryFallback()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new WpfReplayRect(6, 7, 22, 32)
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(6, 7, 22, 32), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableRectGeometryStateWithoutMediaGeometryFallback()
    {
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = new PortableRect(8, 9, 24, 34)
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(8, 9, 24, 34), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesLocalLineGeometryStateWithoutMediaGeometryFallback()
    {
        var geometry = new LineGeometry(new Point(1, 2), new Point(31, 2));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(1, 2, 30, 0), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesLocalRectangleGeometryStateWithoutMediaGeometryFallback()
    {
        var geometry = new RectangleGeometry(new Rect(4, 5, 20, 30));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(4, 5, 20, 30), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesUnfilledRectanglePathGeometryStateWithoutMediaGeometryFallback()
    {
        var geometry = CreateRectanglePathGeometry(new Rect(4, 5, 20, 30), isFilled: false);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(4, 5, 20, 30), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesLocalEllipseGeometryStateWithoutMediaGeometryFallback()
    {
        var geometry = new EllipseGeometry(new Point(14, 25), 10, 20);
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(4, 5, 20, 40), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesTransformedLocalEllipseGeometryStateWithoutMediaGeometryFallback()
    {
        var geometry = new EllipseGeometry(new Point(14, 25), 10, 20)
        {
            Transform = new MatrixTransform(2, 0, 0, 3, 5, -1)
        };
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(13, 14, 40, 120), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesClosedPolylinePathGeometryStateWithoutMediaGeometryFallback()
    {
        var geometry = CreateClosedPolylinePathGeometry(
            new Point(1, 2),
            new Point(30, 40),
            new Point(50, 10));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(1, 2, 49, 38), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesNativeMediaGeometryPathWithoutGenericBoundsFallback()
    {
        var geometry = CreateThrowingBoundsCurvedPathGeometry(new Rect(5, 6, 40, 30));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(5, 6, 40, 30), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableLineGeometryPathPointsBeforeStaleBoundsMetadata()
    {
        var geometry = new PortableNonRectangleClipGeometry(3, 4, 50, 20, new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(3, 4, 50, 20), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesTransformedPortablePathBeforeStaleBoundsMetadata()
    {
        var geometry = new PortableNonRectangleClipGeometry(
            3,
            4,
            50,
            20,
            new PortableRect(0, 0, 1, 1),
            new PortableMatrix3x2(1, 0, 0, 1, 10, 20));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(13, 24, 50, 20), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsAppliesAxisAlignedPortableTransformToExactCurveBounds()
    {
        var geometry = new PortableQuadraticCurveGeometry(
            new PortableRect(0, 0, 1, 1),
            new PortableMatrix3x2(2, 0, 0, 3, 5, 7));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(11, 19, 200, 150), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsPreservesPortableHorizontalLineGeometryBounds()
    {
        var geometry = new PortableNonRectangleClipGeometry(3, 4, 50, 0, new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(3, 4, 50, 0), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableQuadraticGeometryPathPointsBeforeStaleBoundsMetadata()
    {
        var geometry = new PortableQuadraticCurveGeometry(new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(3, 4, 100, 50), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesNativePortablePathBoundsBeforeStaleMetadataFallback()
    {
        var geometry = new PortableUnfilledUnstrokedLineGeometry(new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(7, 9, 40, 0), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsPreservesMetadataOnlyPortableBoundsFallback()
    {
        var geometry = new PortableMetadataOnlyGeometry(new PortableRect(12, 14, 32, 24));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(12, 14, 32, 24), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsRejectsStalePortableMetadataWhenPathDataCannotBeBound()
    {
        var geometry = new PortableInvalidPathGeometry(new PortableRect(12, 14, 32, 24));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.False(hasBounds);
        Assert.Equal(default, bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableCubicGeometryPathPointsBeforeStaleBoundsMetadata()
    {
        var geometry = new PortableCubicCurveGeometry(new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(3, 4, 100, 75), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableArcGeometryPathPointsBeforeStaleBoundsMetadata()
    {
        var geometry = new PortableArcCurveGeometry(new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(3, bounds.X, 4);
        Assert.Equal(4, bounds.Y, 4);
        Assert.Equal(100, bounds.Width, 4);
        Assert.Equal(50, bounds.Height, 4);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableCombinedIntersectionOperandsBeforeStaleBoundsMetadata()
    {
        var geometry = new PortableCombinedGeometry(
            combineOperation: 1,
            new PortableRectangleClipGeometry(0, 0, 100, 100, new PortableRect(0, 0, 1, 1)),
            new PortableRectangleClipGeometry(20, 30, 50, 10, new PortableRect(0, 0, 1, 1)),
            new PortableRect(0, 0, 1, 1));
        var drawing = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(20, 30, 50, 10), bounds);
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableDrawingGroupClipBoundsWithoutGeometryFallback()
    {
        var geometry = new PortableRectangleClipGeometry(0, 0, 100, 100);
        var clip = new PortableRectangleClipGeometry(10, 20, 30, 40, new PortableRect(0, 0, 1, 1));
        var child = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasClipGeometry = true,
            ClipGeometry = clip,
            Children = [child]
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(group, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(10, 20, 30, 40), bounds);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(0, child.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
        Assert.Equal(0, clip.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesNativeMediaDrawingGroupClipBoundsWithoutGenericBoundsFallback()
    {
        var geometry = new PortableRectangleClipGeometry(0, 0, 100, 100);
        var clip = CreateThrowingBoundsCurvedPathGeometry(new Rect(10, 20, 30, 40));
        var child = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasClipGeometry = true,
            ClipGeometry = clip,
            Children = [child]
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(group, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(10, 20, 30, 40), bounds);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(0, child.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsUsesPortableDrawingGroupTransformMatrixWithoutMediaTransformFallback()
    {
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var child = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry
        });
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            Children = [child]
        });

        var hasBounds = WpfDrawingReplay.TryGetDrawingBounds(group, null, out var bounds);

        Assert.True(hasBounds);
        Assert.Equal(new Rect(4, 6, 10, 12), bounds);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(0, child.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsIgnoresNonPortableGenericBoundsShape()
    {
        var drawing = new ThrowingBoundsOnlyDrawing();

        Assert.False(WpfDrawingReplay.TryGetDrawingBounds(drawing, null, out _));
        Assert.Equal(0, drawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void TryGetDrawingBoundsDoesNotReflectUnavailablePortableDrawingState()
    {
        var geometryDrawing = new UnavailablePortableGeometryDrawing();
        var imageDrawing = new UnavailablePortableImageDrawing();
        var glyphRunDrawing = new UnavailablePortableGlyphRunDrawing();

        Assert.False(WpfDrawingReplay.TryGetDrawingBounds(geometryDrawing, null, out _));
        Assert.False(WpfDrawingReplay.TryGetDrawingBounds(imageDrawing, null, out _));
        Assert.False(WpfDrawingReplay.TryGetDrawingBounds(glyphRunDrawing, null, out _));

        Assert.Equal(0, geometryDrawing.ReflectedStateProbeCount);
        Assert.Equal(0, imageDrawing.ReflectedStateProbeCount);
        Assert.Equal(0, glyphRunDrawing.ReflectedStateProbeCount);
    }

    [Fact]
    public void ReplaySubtreeAppliesPortableDrawingGroupStateWithoutReflection()
    {
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasBounds = true,
            Bounds = new PortableRect(0, 0, 40, 30),
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasClipGeometry = true,
            ClipGeometry = new RectangleGeometry(new Rect(0, 0, 20, 20)),
            HasOpacity = true,
            Opacity = 0.5,
            HasGuidelineSet = true,
            GuidelineSet = new object(),
            HasBitmapScalingMode = true,
            BitmapScalingMode = new FakeRenderingHint("LowQuality"),
            HasEdgeMode = true,
            EdgeMode = new FakeRenderingHint("Aliased"),
            HasTextRenderingMode = true,
            TextRenderingMode = new FakeRenderingHint("ClearType"),
            HasTextHintingMode = true,
            TextHintingMode = new FakeRenderingHint("Fixed"),
            Children =
            [
                new FakeGeometryDrawing(
                    new RectangleGeometry(new Rect(1, 2, 10, 12)),
                    Brushes.Green)
            ]
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(group, sink);

        Assert.Equal(
            new[]
            {
                "PushTransform",
                "PushNativeClip",
                "PushOpacity",
                "PushGuidelineSetObject",
                "PushBitmapScalingMode",
                "PushEdgeMode",
                "PushTextRenderingMode",
                "PushTextHintingMode",
                "DrawRectangle",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(new Vector2(3, 4), new Vector2(sink.NativeTransforms[0].M41, sink.NativeTransforms[0].M42));
        Assert.Empty(sink.Clips);
        AssertReplayRect(0, 0, 20, 20, Assert.Single(sink.NativeClips));
        Assert.Equal(new[] { 0.5 }, sink.Opacities);
        Assert.Equal(new[] { "LowQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Aliased" }, sink.EdgeModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(new[] { "Fixed" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplaySubtreeUsesNativePortableDrawingGroupClipWhenAvailable()
    {
        var clip = new PortableRectangleClipGeometry(0, 0, 20, 20);
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var child = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green
        });
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasClipGeometry = true,
            ClipGeometry = clip,
            Children = [child]
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(group, sink);

        Assert.Equal(
            new[]
            {
                "PushTransform",
                "PushNativeClip",
                "DrawNativeGeometry",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Empty(sink.Clips);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.NativeGeometryClips);
        AssertReplayRect(0, 0, 20, 20, Assert.Single(sink.NativeClips));
        Assert.Single(sink.NativeDrawGeometries);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(0, child.ReflectedStateProbeCount);
        Assert.Equal(0, clip.ReflectedGeometryProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplaySubtreeUsesNativePortableDrawingGroupGeometryClipForNonRectangleClip()
    {
        var clip = new PortableNonRectangleClipGeometry(0, 0, 20, 20);
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var child = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green
        });
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasTransform = true,
            Transform = new FakeMatrixTransform(new FakeMatrix(1, 0, 0, 1, 3, 4)),
            HasClipGeometry = true,
            ClipGeometry = clip,
            Children = [child]
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(group, sink);

        Assert.Equal(
            new[]
            {
                "PushTransform",
                "PushNativeGeometryClip",
                "DrawNativeGeometry",
                "Pop",
                "Pop"
            },
            sink.Operations);
        Assert.Empty(sink.Clips);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.DrawGeometries);
        Assert.Single(sink.NativeGeometryClips);
        Assert.Single(sink.NativeDrawGeometries);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(0, child.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplaySubtreeUsesNativeMediaDrawingGroupGeometryClipForLocalNonRectangleClip()
    {
        var clip = CreateTrianglePathGeometry();
        var geometry = new PortableRectangleClipGeometry(1, 2, 10, 12);
        var child = new ThrowingPortableGeometryDrawing(new PortableGeometryDrawingState
        {
            HasGeometry = true,
            Geometry = geometry,
            HasBrush = true,
            Brush = Brushes.Green
        });
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasClipGeometry = true,
            ClipGeometry = clip,
            Children = [child]
        });
        var sink = new NativeGeometryTestSink();

        var status = WpfDrawingReplay.Replay(group, sink);

        Assert.Equal(
            new[]
            {
                "PushNativeMediaGeometryClip",
                "DrawNativeGeometry",
                "Pop"
            },
            sink.Operations);
        Assert.Empty(sink.Clips);
        Assert.Empty(sink.NativeClips);
        Assert.Empty(sink.DrawGeometries);
        Assert.Empty(sink.NativeGeometryClips);
        Assert.Same(clip, Assert.Single(sink.NativeMediaGeometryClips));
        Assert.Single(sink.NativeDrawGeometries);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(0, child.ReflectedStateProbeCount);
        Assert.Equal(0, geometry.ReflectedGeometryProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplaySubtreeDoesNotReflectAbsentPortableDrawingGroupState()
    {
        var group = new ThrowingPortableDrawingGroup(new PortableDrawingGroupState
        {
            HasOpacity = true,
            Opacity = 1,
            Children =
            [
                new FakeGeometryDrawing(
                    new RectangleGeometry(new Rect(1, 2, 10, 12)),
                    Brushes.Green)
            ]
        });
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(group, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.DrawGeometries);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Applied, status);
    }

    [Fact]
    public void ReplaySubtreeSkipsUnavailablePortableDrawingGroupStateWithoutReflectionFallback()
    {
        var group = new UnavailablePortableDrawingGroup();
        var sink = new TestSink();

        var status = WpfDrawingReplay.Replay(group, sink);

        Assert.Empty(sink.Operations);
        Assert.Equal(0, group.ReflectedStateProbeCount);
        Assert.Equal(WpfDrawingReplayStatus.Skipped, status);
    }

    [Fact]
    public void ReplaySubtreePushesNativeBlurEffectWhenSinkSupportsVisualEffects()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableEffectState(new FakeBlurEffect(12.5)));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(12.5f, effect.BlurRadius);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesPortableEffectWithoutReflectedTypeName()
    {
        var root = new FakePortableVisualStateVisual(
            CreatePortableEffectState(new FakePortableEffectSource(PortableEffect.Blur(9.5))));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(9.5f, effect.BlurRadius);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeCacheWhenSinkSupportsVisualCaches()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableCacheModeState(new object()))
        {
            Bounds = new FakeRect(10, 20, 30, 40)
        };
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualCaches = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualCache", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new Rect(10, 20, 30, 40), Assert.Single(sink.VisualCacheBounds));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeDropShadowEffectWhenSinkSupportsVisualEffects()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableEffectState(
            new FakeDropShadowEffect
            {
                BlurRadius = 7,
                ShadowDepth = 10,
                Direction = 315,
                Opacity = 0.5,
                Color = Color.FromArgb(128, 10, 20, 30)
            }));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuDropShadowEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(7f, effect.BlurRadius);
        Assert.InRange(effect.Offset.X, 7.06f, 7.08f);
        Assert.InRange(effect.Offset.Y, 7.06f, 7.08f);
        Assert.Equal(10f / 255f, effect.Color.X);
        Assert.Equal(20f / 255f, effect.Color.Y);
        Assert.Equal(30f / 255f, effect.Color.Z);
        Assert.InRange(effect.Color.W, 0.25f, 0.251f);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesNativeBitmapEffectWhenEmulationIsSupported()
    {
        var root = new FakePortableVisualStateVisual(
            CreatePortableBitmapEffectState(new FakeBlurBitmapEffect(6)));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
        var effect = Assert.IsType<ProGpuBlurEffect>(Assert.Single(sink.VisualEffects));
        Assert.Equal(6f, effect.BlurRadius);
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsSupportedEffectUnsupportedWhenSinkCannotApplyVisualEffects()
    {
        var root = new FakePortableVisualStateVisual(CreatePortableEffectState(new FakeBlurEffect(4)));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.VisualEffects);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePushesPortableShaderEffectWithoutReflectedPixelShaderShape()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 21, 34, 55, 89 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_portable_shader");

        var constants = new float[12];
        constants[8] = 0.125f;
        constants[9] = 0.25f;
        constants[10] = 0.5f;
        constants[11] = 1f;

        try
        {
            var shaderEffect = new FakePortableShaderEffectSource(new PortableShaderEffect(
                effectTypeFullName: null,
                effectTypeName: null,
                pixelShader: new PortablePixelShader(
                    uriSource: null,
                    absoluteUri: null,
                    bytecode,
                    majorVersion: 3,
                    minorVersion: 0),
                floatConstants: constants,
                samplers: new[]
                {
                    PortableShaderSampler.ImplicitInput(
                        1,
                        PortableShaderSamplingMode.NearestNeighbor)
                },
                intConstantCount: 0,
                boolConstantCount: 0,
                paddingTop: 1,
                paddingBottom: 2,
                paddingLeft: 3,
                paddingRight: 4,
                ddxUvDdyUvRegisterIndex: -1));

            var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            Assert.Equal(shaderSource, effect.Parameters.ShaderSource);
            Assert.Equal("registered_portable_shader", effect.Parameters.ShaderKey);
            Assert.Equal(1, effect.Parameters.SourceTextureRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, effect.Parameters.SamplingMode);
            Assert.Equal(4f, effect.Padding);
            Assert.Equal(0.125f, effect.Parameters.Constants[8]);
            Assert.Equal(0.25f, effect.Parameters.Constants[9]);
            Assert.Equal(0.5f, effect.Parameters.Constants[10]);
            Assert.Equal(1f, effect.Parameters.Constants[11]);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreePushesNativeShaderEffectWhenReplacementIsRegistered()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 1, 2, 3, 4 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_fake_shader");

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetFloatConstant(2, 0.25f, 0.5f, 0.75f, 1f);
            shaderEffect.SetImplicitInputSampler(1, FakeSamplingMode.NearestNeighbor);

            var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            Assert.Equal(shaderSource, effect.Parameters.ShaderSource);
            Assert.Equal("registered_fake_shader", effect.Parameters.ShaderKey);
            Assert.Equal(1, effect.Parameters.SourceTextureRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, effect.Parameters.SamplingMode);
            Assert.Equal(0.25f, effect.Parameters.Constants[8]);
            Assert.Equal(0.5f, effect.Parameters.Constants[9]);
            Assert.Equal(0.75f, effect.Parameters.Constants[10]);
            Assert.Equal(1f, effect.Parameters.Constants[11]);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreePushesNativeShaderEffectWithImageBrushSampler()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 2, 4, 6, 8 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_image_sampler_shader");
        var samplerTexture = (ProGpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(ProGpuTexture));
        var rawSamplerSource = new FakeBitmapSource();
        var imageAdapter = new FakeImageSourceAdapter(new FakeSamplerBitmapSource(samplerTexture));

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(0, FakeSamplingMode.Bilinear);
            shaderEffect.SetSampler(2, new FakeShaderImageBrush(rawSamplerSource), FakeSamplingMode.NearestNeighbor);

            var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeRenderer().ReplaySubtree(
                root,
                sink,
                imageSourceAdapter: imageAdapter);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            var sampler = Assert.Single(effect.Parameters.Samplers);
            Assert.Equal(2, sampler.RegisterIndex);
            Assert.Same(samplerTexture, sampler.Texture);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, sampler.SamplingMode);
            Assert.Equal(0, effect.Parameters.SourceTextureRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Linear, effect.Parameters.SamplingMode);
            Assert.Same(rawSamplerSource, imageAdapter.LastImageSource);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreePushesNativeShaderEffectWithAdapterRenderedBrushSampler()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 12, 14, 16, 18 };
        var shaderSource = "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }";
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            shaderSource,
            shaderKey: "registered_rendered_brush_sampler_shader");
        var samplerTexture = (ProGpuTexture)RuntimeHelpers.GetUninitializedObject(typeof(ProGpuTexture));
        var samplerBrush = new FakeShaderDrawingBrush();
        var imageAdapter = new FakeShaderSamplerBrushAdapter(samplerTexture);

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(0, FakeSamplingMode.Bilinear);
            shaderEffect.SetSampler(3, samplerBrush, FakeSamplingMode.NearestNeighbor);

            var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeRenderer().ReplaySubtree(
                root,
                sink,
                imageSourceAdapter: imageAdapter);

            Assert.Equal(new[] { "PushVisualEffect", "DrawRectangle", "Pop" }, sink.Operations);
            var effect = Assert.IsType<ProGpuWpfShaderEffect>(Assert.Single(sink.VisualEffects));
            var sampler = Assert.Single(effect.Parameters.Samplers);
            Assert.Equal(3, sampler.RegisterIndex);
            Assert.Same(samplerTexture, sampler.Texture);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, sampler.SamplingMode);
            Assert.Same(samplerBrush, imageAdapter.LastSamplerBrush);
            Assert.Equal(3, imageAdapter.LastSamplerRegisterIndex);
            Assert.Equal(ProGpuTextureSamplingMode.Nearest, imageAdapter.LastSamplerMode);
            Assert.Equal(0, result.UnsupportedVisualStateCount);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreeCountsShaderEffectUnsupportedForUnsupportedSamplerBrush()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 5, 7, 9, 11 };
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "registered_unsupported_sampler_shader");

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(0, FakeSamplingMode.Bilinear);
            shaderEffect.SetSampler(2, new FakeUnsupportedSamplerBrush(), FakeSamplingMode.NearestNeighbor);

            var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
            Assert.Empty(sink.VisualEffects);
            Assert.Equal(1, result.UnsupportedVisualStateCount);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreeCountsShaderEffectUnsupportedWhenReplacementIsMissing()
    {
        var root = new FakePortableVisualStateVisual(
            CreatePortableEffectState(new FakeShaderEffect(new byte[] { 0, 3, 0, 0, 9, 9, 9, 9 })));
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink { AcceptVisualEffects = true };
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
        Assert.Empty(sink.VisualEffects);
        Assert.Equal(1, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeCountsShaderEffectUnsupportedForOutOfRangeInputSampler()
    {
        var bytecode = new byte[] { 0, 3, 0, 0, 4, 4, 4, 4 };
        var replacementKey = WpfShaderEffectRegistry.RegisterPixelShaderBytecode(
            bytecode,
            "fn wpf_effect_main(uv: vec2<f32>, inputColor: vec4<f32>) -> vec4<f32> { return inputColor; }",
            shaderKey: "registered_out_of_range_sampler_shader");

        try
        {
            var shaderEffect = new FakeShaderEffect(bytecode);
            shaderEffect.SetImplicitInputSampler(16, FakeSamplingMode.NearestNeighbor);

            var root = new FakePortableVisualStateVisual(CreatePortableEffectState(shaderEffect));
            root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

            var sink = new TestSink { AcceptVisualEffects = true };
            var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

            Assert.Equal(new[] { "DrawRectangle" }, sink.Operations);
            Assert.Empty(sink.VisualEffects);
            Assert.Equal(1, result.UnsupportedVisualStateCount);
            Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        }
        finally
        {
            WpfShaderEffectRegistry.Unregister(replacementKey);
        }
    }

    [Fact]
    public void ReplaySubtreeAppliesLowQualityBitmapScalingAsLinearState()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasBitmapScalingMode = true,
            BitmapScalingMode = new FakeRenderingHint("LowQuality")
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "LowQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesHighQualityBitmapScalingAsCubicState()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasBitmapScalingMode = true,
            BitmapScalingMode = new FakeRenderingHint("HighQuality")
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushBitmapScalingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "HighQuality" }, sink.BitmapScalingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesClearTypeTextRenderingMode()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasTextRenderingMode = true,
            TextRenderingMode = new FakeRenderingHint("ClearType")
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTextRenderingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesClearTypeHintAsTextRenderingMode()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasClearTypeHint = true,
            ClearTypeHint = new FakeRenderingHint("Enabled")
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTextRenderingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "ClearType" }, sink.TextRenderingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreeAppliesAnimatedTextHintingMode()
    {
        var root = new FakePortableVisualStateVisual(new PortableVisualState
        {
            HasTextHintingMode = true,
            TextHintingMode = new FakeRenderingHint("Animated")
        });
        root.Children.Add(new FakeDrawingVisual(CreateRenderData(Brushes.Green)));

        var sink = new TestSink();
        var result = new WpfVisualTreeRenderer().ReplaySubtree(root, sink);

        Assert.Equal(new[] { "PushTextHintingMode", "DrawRectangle", "Pop" }, sink.Operations);
        Assert.Equal(new[] { "Animated" }, sink.TextHintingModes.Select(mode => mode?.ToString()));
        Assert.Equal(0, result.UnsupportedVisualStateCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
    }

    [Fact]
    public void ReplaySubtreePassesImageSourceAdapterToDefaultRenderDataResolver()
    {
        var source = new FakeBitmapSource();
        var adapter = new FakeImageSourceAdapter();
        var root = new FakeDrawingVisual(CreateImageRenderData(source));
        var sink = new TestSink();

        var result = new WpfVisualTreeRenderer().ReplaySubtree(
            root,
            sink,
            imageSourceAdapter: adapter);

        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(new WpfMilDecodeResult(1, 1, 0, 0), result.RenderData);
        Assert.Same(source, adapter.LastImageSource);
        var image = Assert.Single(sink.Images);
        Assert.Same(adapter.AdaptedImageSource, image.ImageSource);
        Assert.Equal(new Rect(1, 2, 30, 40), image.Rectangle);
    }

    private static FakeRenderData CreateRenderData(MediaBrush brush)
    {
        var record = CreateRectangleRecord(1, 0);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(brush));
    }

    private static FakeRenderData CreateRenderData(MediaBrush brush, object extraResource)
    {
        var record = CreateRectangleRecord(1, 0);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(brush, extraResource));
    }

    private static FakeRenderData CreateImageRenderData(object imageSource)
    {
        var record = CreateImageRecord(1);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(imageSource));
    }

    private static FakeRenderData CreateGeometryRenderData(object geometry)
    {
        var record = CreateGeometryRecord(1);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(geometry));
    }

    private static FakeRenderData CreateStrokedGeometryRenderData(object geometry, MediaPen pen)
    {
        var record = CreateGeometryRecord(0, 2, 3);
        return new FakeRenderData(record, record.Length, new FakeDependentResources(null, pen, geometry));
    }

    private static FakeRenderData CreateClippedRenderData(object clip, MediaBrush brush)
    {
        var pushClipPayload = new byte[8];
        WriteUInt32(pushClipPayload, 0, 1);

        var record = CreateRecord(WpfMilCommandId.PushClip, pushClipPayload)
            .Concat(CreateRectangleRecord(2, 0))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        return new FakeRenderData(record, record.Length, new FakeDependentResources(clip, brush));
    }

    private static FakeRenderData CreateTransformedRenderData(object transform, MediaBrush brush)
    {
        var pushTransformPayload = new byte[8];
        WriteUInt32(pushTransformPayload, 0, 1);

        var rectanglePayload = new byte[40];
        WriteRect(rectanglePayload, 0, 1, 2, 30, 40);
        WriteUInt32(rectanglePayload, 32, 2);

        var record = CreateRecord(WpfMilCommandId.PushTransform, pushTransformPayload)
            .Concat(CreateRecord(WpfMilCommandId.DrawRectangle, rectanglePayload))
            .Concat(CreateRecord(WpfMilCommandId.Pop, Array.Empty<byte>()))
            .ToArray();

        return new FakeRenderData(record, record.Length, new FakeDependentResources(transform, brush));
    }

    private static byte[] CreateRectangleRecord(uint brushToken, uint penToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, brushToken);
        WriteUInt32(payload, 36, penToken);
        return CreateRecord(WpfMilCommandId.DrawRectangle, payload);
    }

    private static byte[] CreateImageRecord(uint imageSourceToken)
    {
        var payload = new byte[40];
        WriteRect(payload, 0, 1, 2, 30, 40);
        WriteUInt32(payload, 32, imageSourceToken);
        return CreateRecord(WpfMilCommandId.DrawImage, payload);
    }

    private static byte[] CreateGeometryRecord(uint geometryToken)
    {
        return CreateGeometryRecord(0, 0, geometryToken);
    }

    private static byte[] CreateGeometryRecord(uint brushToken, uint penToken, uint geometryToken)
    {
        var payload = new byte[16];
        WriteUInt32(payload, 0, brushToken);
        WriteUInt32(payload, 4, penToken);
        WriteUInt32(payload, 8, geometryToken);
        return CreateRecord(WpfMilCommandId.DrawGeometry, payload);
    }

    private static PathGeometry CreateRectanglePathGeometry(Rect bounds, bool isFilled)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(bounds.X, bounds.Y),
            IsClosed = true,
            IsFilled = isFilled
        };
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width, bounds.Y), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height), isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Y + bounds.Height), isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateTrianglePathGeometry()
    {
        return CreateClosedPolylinePathGeometry(
            new Point(0, 0),
            new Point(20, 0),
            new Point(8, 16));
    }

    private static PathGeometry CreateClosedPolylinePathGeometry(params Point[] points)
    {
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = false
        };

        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], isStroked: true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateThrowingBoundsClosedPolylinePathGeometry(params Point[] points)
    {
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = true
        };

        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], isStroked: true));
        }

        var geometry = new ThrowingBoundsPathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateThrowingBoundsCurvedPathGeometry(Rect bounds)
    {
        var figure = new PathFigure
        {
            StartPoint = new Point(bounds.X, bounds.Y + bounds.Height),
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new BezierSegment(
            new Point(bounds.X, bounds.Y),
            new Point(bounds.X + bounds.Width, bounds.Y),
            new Point(bounds.X + bounds.Width, bounds.Y + bounds.Height),
            isStroked: true));
        figure.Segments.Add(new LineSegment(new Point(bounds.X, bounds.Y + bounds.Height), isStroked: true));

        var geometry = new ThrowingBoundsPathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static byte[] CreateRecord(WpfMilCommandId commandId, byte[] payload)
    {
        var record = new byte[payload.Length + 8];
        WriteInt32(record, 0, record.Length);
        WriteInt32(record, 4, (int)commandId);
        payload.CopyTo(record.AsSpan(8));
        return record;
    }

    private static void WriteRect(byte[] target, int offset, double x, double y, double width, double height)
    {
        WriteDouble(target, offset, x);
        WriteDouble(target, offset + 8, y);
        WriteDouble(target, offset + 16, width);
        WriteDouble(target, offset + 24, height);
    }

    private static void WriteInt32(byte[] target, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(offset, 4), value);
    }

    private static void WriteDouble(byte[] target, int offset, double value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(target.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(value));
    }

    private static void AssertReplayRect(double x, double y, double width, double height, WpfReplayRect? actual, int? precision = null)
    {
        var bounds = Assert.NotNull(actual);
        if (precision.HasValue)
        {
            Assert.Equal(x, bounds.X, precision.Value);
            Assert.Equal(y, bounds.Y, precision.Value);
            Assert.Equal(width, bounds.Width, precision.Value);
            Assert.Equal(height, bounds.Height, precision.Value);
            return;
        }

        Assert.Equal(x, bounds.X);
        Assert.Equal(y, bounds.Y);
        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);
    }

    private class FakeVisual : PortableVisualChildrenSource, PortableVisualBoundsSource
    {
        public FakeVisualCollection Children { get; } = new();

        public double Opacity { get; init; } = 1;

        public object? Transform { get; init; }

        public object? Clip { get; init; }

        public object? VisualClip { get; init; }

        public object? Bounds { get; init; }

        public object? OpacityMask { get; init; }

        public object? ScrollableAreaClip { get; init; }

        public object? VisualScrollableAreaClip { get; init; }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = Children.Count;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = Children[index];
            return true;
        }

        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            if (TryReadBounds(out var portableBounds))
            {
                bounds = new PortableVisualBounds
                {
                    HasContentBounds = true,
                    ContentBounds = portableBounds,
                    HasDescendantBounds = true,
                    DescendantBounds = portableBounds
                };
                return true;
            }

            bounds = null!;
            return false;
        }

        private bool TryReadBounds(out PortableRect bounds)
        {
            switch (Bounds)
            {
                case FakeRect rect:
                    bounds = new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
                    return true;
                case PortableRect rect:
                    bounds = rect;
                    return !rect.IsEmpty;
                default:
                    bounds = PortableRect.Empty;
                    return false;
            }
        }
    }

    private sealed class FakeDrawingVisual : FakeVisual, PortableDrawingContentSource
    {
        private readonly object? _content;

        public FakeDrawingVisual(object? content)
        {
            _content = content;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class FakeUiElementVisual : FakeVisual, PortableDrawingContentSource
    {
        private readonly object? _drawingContent;

        public FakeUiElementVisual(object? drawingContent)
        {
            _drawingContent = drawingContent;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _drawingContent;
            return true;
        }
    }

    private sealed class FakePortableVisualLayoutVisual : FakeVisual, PortableVisualLayoutStateSource
    {
        private readonly PortableVisualLayoutState _state;

        public FakePortableVisualLayoutVisual(PortableVisualLayoutState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakePortableVisualStateVisual : FakeVisual, PortableVisualStateSource
    {
        private readonly PortableVisualState _state;

        public FakePortableVisualStateVisual(PortableVisualState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakePortableVisualStateAndLayoutVisual :
        FakeVisual,
        PortableVisualStateSource,
        PortableVisualLayoutStateSource
    {
        private readonly PortableVisualState _visualState;
        private readonly PortableVisualLayoutState _layoutState;

        public FakePortableVisualStateAndLayoutVisual(
            PortableVisualState visualState,
            PortableVisualLayoutState layoutState)
        {
            _visualState = visualState;
            _layoutState = layoutState;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _visualState;
            return true;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _layoutState;
            return true;
        }
    }

    private sealed class FakePortableVisualStateDrawingVisual :
        FakeVisual,
        PortableVisualStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _state;

        public FakePortableVisualStateDrawingVisual(object? content, PortableVisualState state)
        {
            _content = content;
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class MutablePortableVisualStateDrawingVisual :
        FakeVisual,
        PortableVisualStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;

        public MutablePortableVisualStateDrawingVisual(object? content, PortableVisualState state)
        {
            _content = content;
            State = state;
        }

        public PortableVisualState State { get; set; }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = State;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class ThrowingPortableVisualStateDrawingVisual :
        PortableVisualStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _state;

        public ThrowingPortableVisualStateDrawingVisual(object? content, PortableVisualState state)
        {
            _content = content;
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Transform => ThrowReflectedStateProbe();

        public object? VisualTransform => ThrowReflectedStateProbe();

        public object? Clip => ThrowReflectedStateProbe();

        public object? VisualClip => ThrowReflectedStateProbe();

        public object? OpacityMask => ThrowReflectedStateProbe();

        public object? ScrollableAreaClip => ThrowReflectedStateProbe();

        public object? VisualScrollableAreaClip => ThrowReflectedStateProbe();

        public object? Effect => ThrowReflectedStateProbe();

        public object? BitmapEffect => ThrowReflectedStateProbe();

        public object? BitmapEffectInput => ThrowReflectedStateProbe();

        public object? CacheMode => ThrowReflectedStateProbe();

        public object? BitmapScalingMode => ThrowReflectedStateProbe();

        public object? EdgeMode => ThrowReflectedStateProbe();

        public object? ClearTypeHint => ThrowReflectedStateProbe();

        public object? TextRenderingMode => ThrowReflectedStateProbe();

        public object? TextHintingMode => ThrowReflectedStateProbe();

        public object? XSnappingGuidelines => ThrowReflectedStateProbe();

        public object? YSnappingGuidelines => ThrowReflectedStateProbe();

        public object? VisualXSnappingGuidelines => ThrowReflectedStateProbe();

        public object? VisualYSnappingGuidelines => ThrowReflectedStateProbe();

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected state property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableDrawingGroup : PortableDrawingGroupStateSource
    {
        private readonly PortableDrawingGroupState _state;

        public ThrowingPortableDrawingGroup(PortableDrawingGroupState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? Transform => ThrowReflectedStateProbe();

        public object? ClipGeometry => ThrowReflectedStateProbe();

        public object? Opacity => ThrowReflectedStateProbe();

        public object? OpacityMask => ThrowReflectedStateProbe();

        public object? GuidelineSet => ThrowReflectedStateProbe();

        public object? Effect => ThrowReflectedStateProbe();

        public object? BitmapEffect => ThrowReflectedStateProbe();

        public object? BitmapEffectInput => ThrowReflectedStateProbe();

        public object? CacheMode => ThrowReflectedStateProbe();

        public object? BitmapScalingMode => ThrowReflectedStateProbe();

        public object? EdgeMode => ThrowReflectedStateProbe();

        public object? ClearTypeHint => ThrowReflectedStateProbe();

        public object? TextRenderingMode => ThrowReflectedStateProbe();

        public object? TextHintingMode => ThrowReflectedStateProbe();

        public object? Children => ThrowReflectedStateProbe();

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected drawing group property '{propertyName}' should not be read.");
        }
    }

    private sealed class UnavailablePortableDrawingGroup : PortableDrawingGroupStateSource
    {
        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? Transform => ThrowReflectedStateProbe();

        public object? ClipGeometry => ThrowReflectedStateProbe();

        public object? Opacity => ThrowReflectedStateProbe();

        public object? OpacityMask => ThrowReflectedStateProbe();

        public object? GuidelineSet => ThrowReflectedStateProbe();

        public object? Effect => ThrowReflectedStateProbe();

        public object? BitmapEffect => ThrowReflectedStateProbe();

        public object? BitmapEffectInput => ThrowReflectedStateProbe();

        public object? CacheMode => ThrowReflectedStateProbe();

        public object? BitmapScalingMode => ThrowReflectedStateProbe();

        public object? EdgeMode => ThrowReflectedStateProbe();

        public object? ClearTypeHint => ThrowReflectedStateProbe();

        public object? TextRenderingMode => ThrowReflectedStateProbe();

        public object? TextHintingMode => ThrowReflectedStateProbe();

        public object? Children => ThrowReflectedStateProbe();

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = null!;
            return false;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected drawing group property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableGeometryDrawing : PortableGeometryDrawingStateSource
    {
        private readonly PortableGeometryDrawingState _state;

        public ThrowingPortableGeometryDrawing(PortableGeometryDrawingState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? Geometry => ThrowReflectedStateProbe();

        public object? Brush => ThrowReflectedStateProbe();

        public object? Pen => ThrowReflectedStateProbe();

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected geometry drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class PortableGeometryStateHost : PortableGeometryDrawingStateSource
    {
        private readonly PortableGeometryDrawingState _state;

        public PortableGeometryStateHost(PortableGeometryDrawingState state)
        {
            _state = state;
        }

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class ThrowingBoundsOnlyDrawing
    {
        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class UnavailablePortableGeometryDrawing : PortableGeometryDrawingStateSource
    {
        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? Geometry => ThrowReflectedStateProbe();

        public object? Brush => ThrowReflectedStateProbe();

        public object? Pen => ThrowReflectedStateProbe();

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = null!;
            return false;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected geometry drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableImageDrawing : PortableImageDrawingStateSource
    {
        private readonly PortableImageDrawingState _state;

        public ThrowingPortableImageDrawing(PortableImageDrawingState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? ImageSource => ThrowReflectedStateProbe();

        public object? Rect => ThrowReflectedStateProbe();

        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected image drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class UnavailablePortableImageDrawing : PortableImageDrawingStateSource
    {
        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? ImageSource => ThrowReflectedStateProbe();

        public object? Rect => ThrowReflectedStateProbe();

        public bool TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            state = null!;
            return false;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected image drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableGlyphRunDrawing : PortableGlyphRunDrawingStateSource
    {
        private readonly PortableGlyphRunDrawingState _state;

        public ThrowingPortableGlyphRunDrawing(PortableGlyphRunDrawingState state)
        {
            _state = state;
        }

        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? GlyphRun => ThrowReflectedStateProbe();

        public object? ForegroundBrush => ThrowReflectedStateProbe();

        public bool TryGetPortableGlyphRunDrawingState(out PortableGlyphRunDrawingState state)
        {
            state = _state;
            return true;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected glyph drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class UnavailablePortableGlyphRunDrawing : PortableGlyphRunDrawingStateSource
    {
        public int ReflectedStateProbeCount { get; private set; }

        public object? Bounds => ThrowReflectedStateProbe();

        public object? GlyphRun => ThrowReflectedStateProbe();

        public object? ForegroundBrush => ThrowReflectedStateProbe();

        public bool TryGetPortableGlyphRunDrawingState(out PortableGlyphRunDrawingState state)
        {
            state = null!;
            return false;
        }

        private object? ThrowReflectedStateProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedStateProbeCount++;
            throw new InvalidOperationException($"Reflected glyph drawing property '{propertyName}' should not be read.");
        }
    }

    private sealed class ThrowingPortableGlyphRunSource : PortableGlyphRunSource
    {
        private readonly PortableGlyphRun _glyphRun;

        public ThrowingPortableGlyphRunSource(PortableGlyphRun glyphRun)
        {
            _glyphRun = glyphRun;
        }

        public int ReflectedGlyphRunProbeCount { get; private set; }

        public object? GlyphIndices => ThrowReflectedGlyphRunProbe();

        public object? AdvanceWidths => ThrowReflectedGlyphRunProbe();

        public object? GlyphOffsets => ThrowReflectedGlyphRunProbe();

        public object? BaselineOrigin => ThrowReflectedGlyphRunProbe();

        public object? FontRenderingEmSize => ThrowReflectedGlyphRunProbe();

        public object? GlyphTypeface => ThrowReflectedGlyphRunProbe();

        public object? Font => ThrowReflectedGlyphRunProbe();

        public bool TryGetPortableGlyphRun(out PortableGlyphRun glyphRun)
        {
            glyphRun = _glyphRun;
            return true;
        }

        private object? ThrowReflectedGlyphRunProbe([CallerMemberName] string? propertyName = null)
        {
            ReflectedGlyphRunProbeCount++;
            throw new InvalidOperationException($"Reflected glyph-run property '{propertyName}' should not be read.");
        }
    }

    private sealed class NativeGlyphRunTestSink : IWpfCompositionCommandSink, IWpfNativePrimitiveCommandSink
    {
        public List<(MediaBrush? ForegroundBrush, MediaGlyphRun GlyphRun)> GlyphRuns { get; } = new();

        public List<(MediaBrush? ForegroundBrush, object GlyphRun)> NativeGlyphRuns { get; } = new();

        public MediaDrawingContext DrawingContext => null!;

        public void DrawLine(MediaPen? pen, Point point0, Point point1) { }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle) { }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY) { }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY) { }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry) { }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle) { }

        public void DrawText(FormattedText formattedText, Point origin) { }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
            GlyphRuns.Add((foregroundBrush, glyphRun));
        }

        public void PushClip(MediaGeometry clipGeometry) { }

        public void PushOpacity(double opacity) { }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds) { }

        public void PushTransform(MediaTransform transform) { }

        public void Pop() { }

        public void Close() { }

        public void Dispose() { }

        public void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1) { }

        public void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle) { }

        public void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY) { }

        public void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY) { }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle) { }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle) { }

        public void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRun)
        {
            NativeGlyphRuns.Add((foregroundBrush, glyphRun));
        }

        public void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds) { }
    }

    private sealed class FakePortableVisualStateAndLayoutDrawingVisual :
        PortableVisualStateSource,
        PortableVisualLayoutStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _visualState;
        private readonly PortableVisualLayoutState _layoutState;

        public FakePortableVisualStateAndLayoutDrawingVisual(
            object? content,
            PortableVisualState visualState,
            PortableVisualLayoutState layoutState)
        {
            _content = content;
            _visualState = visualState;
            _layoutState = layoutState;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _visualState;
            return true;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            state = _layoutState;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class CountingPortableVisualStateAndLayoutDrawingVisual :
        PortableVisualStateSource,
        PortableVisualLayoutStateSource,
        PortableDrawingContentSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _visualState;
        private readonly PortableVisualLayoutState _layoutState;

        public CountingPortableVisualStateAndLayoutDrawingVisual(
            object? content,
            PortableVisualState visualState,
            PortableVisualLayoutState layoutState)
        {
            _content = content;
            _visualState = visualState;
            _layoutState = layoutState;
        }

        public int VisualStateQueryCount { get; private set; }

        public int VisualLayoutStateQueryCount { get; private set; }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            VisualStateQueryCount++;
            state = _visualState;
            return true;
        }

        public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
        {
            VisualLayoutStateQueryCount++;
            state = _layoutState;
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }
    }

    private sealed class FakeVisualCollection
    {
        private readonly List<object> _children = new();

        public int Count => _children.Count;

        public object this[int index] => _children[index];

        public void Add(object child)
        {
            _children.Add(child);
        }
    }

    private abstract class FakeProtectedVisualChildrenBase
    {
        private readonly List<object> _children = new();

        protected int VisualChildrenCount => _children.Count;

        public void AddChild(object child)
        {
            _children.Add(child);
        }

        protected object GetVisualChild(int index)
        {
            return _children[index];
        }
    }

    private sealed class FakeVisualChildrenVisual : FakeProtectedVisualChildrenBase
    {
    }

    private sealed class FakePortableVisualChildrenVisual : PortableVisualChildrenSource
    {
        private readonly List<object> _children = new();

        public void AddChild(object child)
        {
            _children.Add(child);
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = _children.Count;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = index >= 0 && index < _children.Count
                ? _children[index]
                : null;
            return child != null;
        }
    }

    private sealed class FakeDrawingResource
    {
        public object? Brush { get; init; }
    }

    private sealed class FakeResource
    {
    }

    private sealed class FakeRenderingHint
    {
        private readonly string _name;

        public FakeRenderingHint(string name)
        {
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }
    }

    private sealed class FakeBlurEffect : PortableEffectSource
    {
        public FakeBlurEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.Blur(Radius);
            return true;
        }
    }

    private sealed class FakeDropShadowEffect : PortableEffectSource
    {
        public double BlurRadius { get; init; }

        public double ShadowDepth { get; init; }

        public double Direction { get; init; }

        public double Opacity { get; init; } = 1;

        public Color Color { get; init; } = Colors.Black;

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.DropShadow(
                BlurRadius,
                ShadowDepth,
                Direction,
                Opacity,
                new PortableColor(Color.A, Color.R, Color.G, Color.B));
            return true;
        }
    }

    private sealed class FakePortableEffectSource : PortableEffectSource
    {
        private readonly PortableEffect _effect;

        public FakePortableEffectSource(PortableEffect effect)
        {
            _effect = effect;
        }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = _effect;
            return true;
        }
    }

    private sealed class FakePortableShaderEffectSource : PortableShaderEffectSource
    {
        private readonly PortableShaderEffect _effect;

        public FakePortableShaderEffectSource(PortableShaderEffect effect)
        {
            _effect = effect;
        }

        public bool TryGetPortableShaderEffect(out PortableShaderEffect effect)
        {
            effect = _effect;
            return true;
        }
    }

    private sealed class FakeBlurBitmapEffect : PortableEffectSource
    {
        public FakeBlurBitmapEffect(double radius)
        {
            Radius = radius;
        }

        public double Radius { get; }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = PortableEffect.Blur(Radius);
            return true;
        }
    }

    private sealed class FakeContextBitmapEffectInput : PortableBitmapEffectInputSource
    {
        public bool TryGetPortableBitmapEffectInput(out PortableBitmapEffectInput input)
        {
            input = new PortableBitmapEffectInput(
                usesContextInput: true,
                hasDefaultAreaToApplyEffect: true);
            return true;
        }
    }

    private sealed class FakeShaderEffect : PortableShaderEffectSource
    {
        private readonly List<FakeFloatRegister?> _floatRegisters = new();
        private readonly List<FakeSamplerData?> _samplerData = new();

        public FakeShaderEffect(byte[] shaderBytecode)
        {
            PixelShader = new FakePixelShader(shaderBytecode);
        }

        public FakePixelShader PixelShader { get; }

        public void SetFloatConstant(int registerIndex, float r, float g, float b, float a)
        {
            while (_floatRegisters.Count <= registerIndex)
            {
                _floatRegisters.Add(null);
            }

            _floatRegisters[registerIndex] = new FakeFloatRegister(r, g, b, a);
        }

        public void SetImplicitInputSampler(int registerIndex, FakeSamplingMode samplingMode)
        {
            SetSampler(registerIndex, new FakeImplicitInputBrush(), samplingMode);
        }

        public void SetSampler(int registerIndex, object? brush, FakeSamplingMode samplingMode)
        {
            while (_samplerData.Count <= registerIndex)
            {
                _samplerData.Add(null);
            }

            _samplerData[registerIndex] = new FakeSamplerData(brush, samplingMode);
        }

        public bool TryGetPortableShaderEffect(out PortableShaderEffect effect)
        {
            effect = new PortableShaderEffect(
                GetType().FullName,
                GetType().Name,
                PixelShader.TryGetPortablePixelShader(),
                CreatePortableFloatConstants(),
                CreatePortableShaderSamplers(),
                intConstantCount: 0,
                boolConstantCount: 0,
                paddingTop: 0,
                paddingBottom: 0,
                paddingLeft: 0,
                paddingRight: 0,
                ddxUvDdyUvRegisterIndex: -1);
            return true;
        }

        private float[] CreatePortableFloatConstants()
        {
            if (_floatRegisters.Count == 0)
            {
                return Array.Empty<float>();
            }

            var constants = new float[_floatRegisters.Count * 4];
            var highestRegister = -1;

            for (var i = 0; i < _floatRegisters.Count; i++)
            {
                var register = _floatRegisters[i];
                if (!register.HasValue)
                {
                    continue;
                }

                var offset = i * 4;
                constants[offset] = register.Value.r;
                constants[offset + 1] = register.Value.g;
                constants[offset + 2] = register.Value.b;
                constants[offset + 3] = register.Value.a;
                highestRegister = i;
            }

            if (highestRegister < 0)
            {
                return Array.Empty<float>();
            }

            Array.Resize(ref constants, (highestRegister + 1) * 4);
            return constants;
        }

        private PortableShaderSampler[] CreatePortableShaderSamplers()
        {
            if (_samplerData.Count == 0)
            {
                return Array.Empty<PortableShaderSampler>();
            }

            var samplers = new List<PortableShaderSampler>(_samplerData.Count);
            for (var i = 0; i < _samplerData.Count; i++)
            {
                var sampler = _samplerData[i];
                if (!sampler.HasValue || sampler.Value._brush == null)
                {
                    continue;
                }

                var samplingMode = ConvertPortableSamplingMode(sampler.Value._samplingMode);
                if (sampler.Value._brush is FakeImplicitInputBrush)
                {
                    samplers.Add(PortableShaderSampler.ImplicitInput(i, samplingMode));
                }
                else if (sampler.Value._brush is FakeShaderImageBrush imageBrush)
                {
                    samplers.Add(PortableShaderSampler.Image(i, imageBrush.ImageSource, samplingMode));
                }
                else
                {
                    samplers.Add(new PortableShaderSampler(
                        i,
                        sampler.Value._brush,
                        samplingMode));
                }
            }

            return samplers.Count == 0 ? Array.Empty<PortableShaderSampler>() : samplers.ToArray();
        }

        private static PortableShaderSamplingMode ConvertPortableSamplingMode(object? samplingMode)
        {
            return samplingMode is FakeSamplingMode.NearestNeighbor
                ? PortableShaderSamplingMode.NearestNeighbor
                : samplingMode is FakeSamplingMode.Auto
                    ? PortableShaderSamplingMode.Auto
                    : PortableShaderSamplingMode.Bilinear;
        }
    }

    private sealed class FakePixelShader
    {
        private readonly byte[] _shaderBytecode;

        public FakePixelShader(byte[] shaderBytecode)
        {
            _shaderBytecode = shaderBytecode;
        }

        public Uri? UriSource { get; init; }

        public PortablePixelShader TryGetPortablePixelShader()
        {
            return new PortablePixelShader(
                UriSource?.ToString(),
                UriSource != null && UriSource.IsAbsoluteUri ? UriSource.AbsoluteUri : null,
                _shaderBytecode,
                _shaderBytecode.Length > 1 ? (short)_shaderBytecode[1] : (short)0,
                _shaderBytecode.Length > 0 ? (short)_shaderBytecode[0] : (short)0);
        }
    }

    private readonly record struct FakeFloatRegister(float r, float g, float b, float a);

    private readonly record struct FakeSamplerData(object? _brush, object? _samplingMode);

    private sealed class FakeImplicitInputBrush
    {
    }

    private sealed class FakeShaderImageBrush
    {
        public FakeShaderImageBrush(object? imageSource)
        {
            ImageSource = imageSource;
        }

        public object? ImageSource { get; }
    }

    private sealed class FakeShaderDrawingBrush
    {
    }

    private sealed class FakeUnsupportedSamplerBrush
    {
    }

    private sealed class FakeSamplerBitmapSource : MediaBitmapSource
    {
        private readonly ProGpuTexture _texture;

        public FakeSamplerBitmapSource(ProGpuTexture texture)
        {
            _texture = texture;
        }

        public override int PixelWidth => 1;

        public override int PixelHeight => 1;

        public override ProGpuTexture GpuTexture => _texture;
    }

    private enum FakeSamplingMode
    {
        NearestNeighbor = 0,
        Bilinear = 1,
        Auto = 2
    }

    private sealed class FakeRenderData : PortableRenderDataSource
    {
        private readonly byte[] _buffer;
        private readonly int _curOffset;
        private readonly FakeDependentResources _dependentResources;

        public FakeRenderData(byte[] buffer, int curOffset, FakeDependentResources dependentResources)
        {
            _buffer = buffer;
            _curOffset = curOffset;
            _dependentResources = dependentResources;
        }

        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        {
            snapshot = new PortableRenderDataSnapshot(
                _buffer.AsSpan(0, _curOffset).ToArray(),
                _dependentResources.Items);
            return true;
        }
    }

    private sealed class FakeDependentResources
    {
        private readonly object?[] _items;

        public FakeDependentResources(params object?[] items)
        {
            _items = items;
        }

        public IReadOnlyList<object?> Items => _items;

        public int Count => _items.Length;

        public object? this[int index] => _items[index];
    }

    private sealed class FakeGeometryDrawing : PortableGeometryDrawingStateSource
    {
        public FakeGeometryDrawing(object geometry, object? brush, object? pen = null)
        {
            Geometry = geometry;
            Brush = brush;
            Pen = pen;
        }

        public object Geometry { get; }

        public object? Brush { get; }

        public object? Pen { get; }

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasBrush = Brush != null,
                Brush = Brush,
                HasPen = Pen != null,
                Pen = Pen,
                HasGeometry = true,
                Geometry = Geometry
            };
            return true;
        }
    }

    private sealed class FakeDrawingTileBrush : PortableTileBrushSource
    {
        private readonly object? _drawing;

        public FakeDrawingTileBrush(object? drawing)
        {
            _drawing = drawing;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            if (_drawing == null)
            {
                brush = null!;
                return false;
            }

            brush = new PortableTileBrush(
                PortableTileBrushKind.Drawing,
                _drawing,
                opacity: 1,
                viewport: new PortableRect(0, 0, 1, 1),
                viewbox: new PortableRect(0, 0, 1, 1),
                viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                tileMode: PortableTileMode.None,
                stretch: PortableStretch.Fill,
                alignmentX: PortableAlignmentX.Center,
                alignmentY: PortableAlignmentY.Center,
                hasTransform: false,
                transform: PortableMatrix3x2.Identity,
                hasRelativeTransform: false,
                relativeTransform: PortableMatrix3x2.Identity);
            return true;
        }
    }

    private sealed class FakeVisualTileBrush : PortableTileBrushSource
    {
        private readonly object? _visual;
        private readonly PortableRect _viewport;
        private readonly PortableTileMode _tileMode;

        public FakeVisualTileBrush(object? visual, PortableRect viewport, PortableTileMode tileMode)
        {
            _visual = visual;
            _viewport = viewport;
            _tileMode = tileMode;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            if (_visual == null)
            {
                brush = null!;
                return false;
            }

            brush = new PortableTileBrush(
                PortableTileBrushKind.Visual,
                _visual,
                opacity: 1,
                viewport: _viewport,
                viewbox: new PortableRect(0, 0, 1, 1),
                viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox,
                tileMode: _tileMode,
                stretch: PortableStretch.Fill,
                alignmentX: PortableAlignmentX.Center,
                alignmentY: PortableAlignmentY.Center,
                hasTransform: false,
                transform: PortableMatrix3x2.Identity,
                hasRelativeTransform: false,
                relativeTransform: PortableMatrix3x2.Identity);
            return true;
        }
    }

    private sealed class ThrowingBoundsPathGeometry : PathGeometry
    {
        public override Rect Bounds => throw new InvalidOperationException("Generic path bounds should not be used.");
    }

    private sealed class PortableThrowingBoundsPathGeometry : PathGeometry, PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableThrowingBoundsPathGeometry(PortableGeometryPath path)
        {
            _path = path;
        }

        public override Rect Bounds => throw new InvalidOperationException("Generic path bounds should not be used.");

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class FakeMatrixTransform : PortableTransformMatrixSource
    {
        public FakeMatrixTransform(FakeMatrix value)
        {
            Value = value;
        }

        public FakeMatrix Value { get; }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = new PortableMatrix3x2(
                Value.M11,
                Value.M12,
                Value.M21,
                Value.M22,
                Value.OffsetX,
                Value.OffsetY);
            return true;
        }
    }

    private readonly record struct FakeMatrix(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY);

    private sealed class PortableRectangleClipGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;
        private readonly FakeRect _rect;

        public PortableRectangleClipGeometry(double x, double y, double width, double height)
            : this(x, y, width, height, new PortableRect(x, y, width, height))
        {
        }

        public PortableRectangleClipGeometry(double x, double y, double width, double height, PortableRect bounds)
        {
            _rect = new FakeRect(x, y, width, height);
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(x, y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(x + width, y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(x + width, y + height), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(x, y + height), isSmoothJoin: false, isStroked: true)
                        ]
                    }
                ]
            };
        }

        public int ReflectedGeometryProbeCount { get; private set; }

        public FakeRect Rect
        {
            get
            {
                ReflectedGeometryProbeCount++;
                return _rect;
            }
        }

        public double RadiusX
        {
            get
            {
                ReflectedGeometryProbeCount++;
                return 0;
            }
        }

        public double RadiusY
        {
            get
            {
                ReflectedGeometryProbeCount++;
                return 0;
            }
        }

        public object? Transform
        {
            get
            {
                ReflectedGeometryProbeCount++;
                return null;
            }
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class PortableNonRectangleClipGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableNonRectangleClipGeometry(double x, double y, double width, double height)
            : this(x, y, width, height, new PortableRect(x, y, width, height))
        {
        }

        public PortableNonRectangleClipGeometry(double x, double y, double width, double height, PortableRect bounds)
            : this(x, y, width, height, bounds, PortableMatrix3x2.Identity)
        {
        }

        public PortableNonRectangleClipGeometry(
            double x,
            double y,
            double width,
            double height,
            PortableRect bounds,
            PortableMatrix3x2 transform)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = transform,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(x, y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(x + width, y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(x + (width * 0.5), y + height), isSmoothJoin: false, isStroked: true)
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

    private sealed class PortableRetracedRectangleClipGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableRetracedRectangleClipGeometry(double x, double y, double width, double height)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = new PortableRect(x, y, width, height),
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(x, y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(x + width, y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(x, y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(x, y + height), isSmoothJoin: false, isStroked: true)
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

    private sealed class PortableQuadraticCurveGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableQuadraticCurveGeometry(PortableRect bounds)
            : this(bounds, PortableMatrix3x2.Identity)
        {
        }

        public PortableQuadraticCurveGeometry(PortableRect bounds, PortableMatrix3x2 transform)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = transform,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(3, 4),
                        IsClosed = false,
                        IsFilled = false,
                        Segments =
                        [
                            PortablePathSegment.QuadraticBezier(
                                new PortablePoint(53, 104),
                                new PortablePoint(103, 4),
                                isSmoothJoin: false,
                                isStroked: true)
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

    private sealed class PortableUnfilledUnstrokedTriangleGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableUnfilledUnstrokedTriangleGeometry(double x, double y, double width, double height, PortableRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(x, y),
                        IsClosed = true,
                        IsFilled = false,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(x + width, y), isSmoothJoin: false, isStroked: false),
                            PortablePathSegment.Line(new PortablePoint(x + (width * 0.5), y + height), isSmoothJoin: false, isStroked: false)
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

    private sealed class PortableUnfilledUnstrokedLineGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableUnfilledUnstrokedLineGeometry(PortableRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(7, 9),
                        IsClosed = false,
                        IsFilled = false,
                        Segments =
                        [
                            PortablePathSegment.Line(
                                new PortablePoint(47, 9),
                                isSmoothJoin: false,
                                isStroked: false)
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

    private sealed class PortableMetadataOnlyGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableMetadataOnlyGeometry(PortableRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures = []
            };
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class PortableInvalidPathGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableInvalidPathGeometry(PortableRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(double.NaN, 9),
                        IsClosed = false,
                        IsFilled = false,
                        Segments =
                        [
                            PortablePathSegment.Line(
                                new PortablePoint(double.NaN, 9),
                                isSmoothJoin: false,
                                isStroked: false)
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

    private sealed class PortableCubicCurveGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableCubicCurveGeometry(PortableRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(3, 4),
                        IsClosed = false,
                        IsFilled = false,
                        Segments =
                        [
                            PortablePathSegment.CubicBezier(
                                new PortablePoint(3, 104),
                                new PortablePoint(103, 104),
                                new PortablePoint(103, 4),
                                isSmoothJoin: false,
                                isStroked: true)
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

    private sealed class PortableArcCurveGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableArcCurveGeometry(PortableRect bounds)
        {
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(3, 54),
                        IsClosed = false,
                        IsFilled = false,
                        Segments =
                        [
                            PortablePathSegment.Arc(
                                new PortablePoint(103, 54),
                                new PortableSize(50, 50),
                                rotationAngle: 0,
                                isLargeArc: false,
                                PortableSweepDirection.Clockwise,
                                isSmoothJoin: false,
                                isStroked: true)
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

    private sealed class PortableCombinedGeometry : PortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        public PortableCombinedGeometry(
            int combineOperation,
            PortableGeometryPathSource first,
            PortableGeometryPathSource second,
            PortableRect bounds)
        {
            Assert.True(first.TryGetPortableGeometryPath(out var firstPath));
            Assert.True(second.TryGetPortableGeometryPath(out var secondPath));
            _path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Combined,
                Bounds = bounds,
                Transform = PortableMatrix3x2.Identity,
                PathA = firstPath,
                PathB = secondPath,
                CombineOperation = combineOperation
            };
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private sealed class FakeBitmapSource
    {
    }

    private sealed class FakeImageSource : MediaImageSource
    {
    }

    private sealed class FakeImageSourceAdapter : IWpfImageSourceAdapter
    {
        public FakeImageSourceAdapter(MediaImageSource? adaptedImageSource = null)
        {
            AdaptedImageSource = adaptedImageSource ?? new FakeImageSource();
        }

        public MediaImageSource AdaptedImageSource { get; }

        public object? LastImageSource { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            LastImageSource = imageSource;
            return AdaptedImageSource;
        }
    }

    private sealed class FakeShaderSamplerBrushAdapter :
        IWpfImageSourceAdapter,
        IWpfShaderEffectSamplerBrushAdapter
    {
        private readonly ProGpuTexture _texture;

        public FakeShaderSamplerBrushAdapter(ProGpuTexture texture)
        {
            _texture = texture;
        }

        public object? LastSamplerBrush { get; private set; }

        public int LastSamplerRegisterIndex { get; private set; }

        public ProGpuTextureSamplingMode LastSamplerMode { get; private set; }

        public MediaImageSource? AdaptImageSource(object? imageSource)
        {
            return null;
        }

        public bool TryAdaptShaderEffectSamplerBrush(
            object? brush,
            int registerIndex,
            ProGpuTextureSamplingMode samplingMode,
            out ProGpuWpfShaderEffectSampler sampler)
        {
            LastSamplerBrush = brush;
            LastSamplerRegisterIndex = registerIndex;
            LastSamplerMode = samplingMode;
            sampler = new ProGpuWpfShaderEffectSampler(registerIndex, _texture, samplingMode);
            return true;
        }
    }

    private class TestSink :
        IWpfCompositionCommandSink,
        IWpfVisualEffectCommandSink,
        IWpfVisualCacheCommandSink,
        IWpfRetainedVisualBranchSink,
        IWpfRetainedVisualStateSink,
        IWpfNativeTransformCommandSink,
        IWpfNativeClipCommandSink
    {
        public List<string> Operations { get; } = new();

        public List<object> VisualOwners { get; } = new();

        public List<object> VisualDependencies { get; } = new();

        public List<WpfRetainedVisualState> RetainedVisualStates { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle)> DrawRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Rect Rectangle, double RadiusX, double RadiusY)> DrawRoundedRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, Point Center, double RadiusX, double RadiusY)> DrawEllipses { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> DrawGeometries { get; } = new();

        public List<(MediaImageSource ImageSource, Rect Rectangle)> Images { get; } = new();

        public List<MediaTransform> Transforms { get; } = new();

        public List<Matrix4x4> NativeTransforms { get; } = new();

        public List<MediaGeometry> Clips { get; } = new();

        public List<WpfReplayRect> NativeClips { get; } = new();

        public List<double> Opacities { get; } = new();

        public List<(MediaBrush? OpacityMask, Rect Bounds)> OpacityMasks { get; } = new();

        public List<object> GuidelineSets { get; } = new();

        public List<object?> BitmapScalingModes { get; } = new();

        public List<object?> EdgeModes { get; } = new();

        public List<object?> TextRenderingModes { get; } = new();

        public List<object?> TextHintingModes { get; } = new();

        public List<ProGpuEffectBase> VisualEffects { get; } = new();

        public List<Rect?> VisualCacheBounds { get; } = new();

        public bool AcceptVisualEffects { get; init; }

        public bool AcceptVisualCaches { get; init; }

        public bool AcceptRetainedVisualOwners { get; init; }

        public MediaDrawingContext DrawingContext => null!;

        public void RegisterVisualOwner(object sourceVisual)
        {
            VisualOwners.Add(sourceVisual);
        }

        public void RegisterVisualDependency(object dependency)
        {
            VisualDependencies.Add(dependency);
        }

        public bool PushVisualOwner(object sourceVisual)
        {
            if (!AcceptRetainedVisualOwners)
            {
                return false;
            }

            Operations.Add("PushVisualOwner");
            VisualOwners.Add(sourceVisual);
            return true;
        }

        public void PopVisualOwner()
        {
            Operations.Add("PopVisualOwner");
        }

        public void ApplyVisualState(in WpfRetainedVisualState state)
        {
            Operations.Add("ApplyVisualState");
            RetainedVisualStates.Add(state);
        }

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
            Operations.Add("DrawLine");
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
            Operations.Add("DrawRectangle");
            DrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            Operations.Add("DrawRoundedRectangle");
            DrawRoundedRectangles.Add((brush, pen, rectangle, radiusX, radiusY));
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
            Operations.Add("DrawEllipse");
            DrawEllipses.Add((brush, pen, center, radiusX, radiusY));
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            Operations.Add("DrawGeometry");
            DrawGeometries.Add((brush, pen, geometry));
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
            Operations.Add("DrawImage");
            Images.Add((imageSource, rectangle));
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
            Operations.Add("PushClip");
            Clips.Add(clipGeometry);
        }

        public void PushOpacity(double opacity)
        {
            Operations.Add("PushOpacity");
            Opacities.Add(opacity);
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
            Operations.Add("PushOpacityMask");
            OpacityMasks.Add((opacityMask, bounds));
        }

        public void PushTransform(MediaTransform transform)
        {
            Operations.Add("PushTransform");
            Transforms.Add(transform);
        }

        public void PushNativeTransform(Matrix4x4 transform)
        {
            Operations.Add("PushTransform");
            NativeTransforms.Add(transform);
        }

        public void PushNativeClip(WpfReplayRect bounds)
        {
            Operations.Add("PushNativeClip");
            NativeClips.Add(bounds);
        }

        public void PushGuidelineSet()
        {
            Operations.Add("PushGuidelineSet");
        }

        public void PushGuidelineSet(object? guidelines)
        {
            Operations.Add("PushGuidelineSetObject");
            Assert.NotNull(guidelines);
            GuidelineSets.Add(guidelines);
        }

        public void PushBitmapScalingMode(object? bitmapScalingMode)
        {
            Operations.Add("PushBitmapScalingMode");
            BitmapScalingModes.Add(bitmapScalingMode);
        }

        public void PushEdgeMode(object? edgeMode)
        {
            Operations.Add("PushEdgeMode");
            EdgeModes.Add(edgeMode);
        }

        public void PushTextRenderingMode(object? textRenderingMode)
        {
            Operations.Add("PushTextRenderingMode");
            TextRenderingModes.Add(textRenderingMode);
        }

        public void PushTextHintingMode(object? textHintingMode)
        {
            Operations.Add("PushTextHintingMode");
            TextHintingModes.Add(textHintingMode);
        }

        public bool PushVisualEffect(ProGpuEffectBase effect)
        {
            if (!AcceptVisualEffects)
            {
                return false;
            }

            Operations.Add("PushVisualEffect");
            VisualEffects.Add(effect);
            return true;
        }

        public bool PushVisualCache(Rect? bounds = null)
        {
            if (!AcceptVisualCaches)
            {
                return false;
            }

            Operations.Add("PushVisualCache");
            VisualCacheBounds.Add(bounds);
            return true;
        }

        public void Pop()
        {
            Operations.Add("Pop");
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NativeGeometryTestSink : TestSink, IWpfNativeGeometryCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, PortableGeometryPath Geometry)> NativeDrawGeometries { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, MediaGeometry Geometry)> NativeMediaDrawGeometries { get; } = new();

        public List<PortableGeometryPath> NativeGeometryClips { get; } = new();

        public List<MediaGeometry> NativeMediaGeometryClips { get; } = new();

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, PortableGeometryPath geometry)
        {
            Operations.Add("DrawNativeGeometry");
            NativeDrawGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool DrawNativeGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
            Operations.Add("DrawNativeMediaGeometry");
            NativeMediaDrawGeometries.Add((brush, pen, geometry));
            return true;
        }

        public bool PushNativeGeometryClip(PortableGeometryPath clipGeometry)
        {
            Operations.Add("PushNativeGeometryClip");
            NativeGeometryClips.Add(clipGeometry);
            return true;
        }

        public bool PushNativeGeometryClip(MediaGeometry clipGeometry)
        {
            Operations.Add("PushNativeMediaGeometryClip");
            NativeMediaGeometryClips.Add(clipGeometry);
            return true;
        }
    }

    private sealed class NativePrimitiveTestSink : TestSink, IWpfNativePrimitiveCommandSink
    {
        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayRect Rectangle)> NativeDrawRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayRect Rectangle, double RadiusX, double RadiusY)> NativeDrawRoundedRectangles { get; } = new();

        public List<(MediaBrush? Brush, MediaPen? Pen, WpfReplayPoint Center, double RadiusX, double RadiusY)> NativeDrawEllipses { get; } = new();

        public void DrawNativeLine(MediaPen? pen, WpfReplayPoint point0, WpfReplayPoint point1)
        {
        }

        public void DrawNativeRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle)
        {
            Operations.Add("DrawNativeRectangle");
            NativeDrawRectangles.Add((brush, pen, rectangle));
        }

        public void DrawNativeRoundedRectangle(MediaBrush? brush, MediaPen? pen, WpfReplayRect rectangle, double radiusX, double radiusY)
        {
            Operations.Add("DrawNativeRoundedRectangle");
            NativeDrawRoundedRectangles.Add((brush, pen, rectangle, radiusX, radiusY));
        }

        public void DrawNativeEllipse(MediaBrush? brush, MediaPen? pen, WpfReplayPoint center, double radiusX, double radiusY)
        {
            Operations.Add("DrawNativeEllipse");
            NativeDrawEllipses.Add((brush, pen, center, radiusX, radiusY));
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle)
        {
        }

        public void DrawNativeImage(MediaImageSource imageSource, WpfReplayRect rectangle, WpfReplayRect sourceRectangle)
        {
        }

        public void DrawNativeGlyphRun(MediaBrush? foregroundBrush, object glyphRun)
        {
        }

        public void PushNativeOpacityMask(MediaBrush? opacityMask, WpfReplayRect bounds)
        {
        }
    }
}
