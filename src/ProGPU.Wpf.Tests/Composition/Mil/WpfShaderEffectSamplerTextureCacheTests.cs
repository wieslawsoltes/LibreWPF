using System.Windows;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfShaderEffectSamplerTextureCacheTests
{
    [Fact]
    public void TryGetBrushSourceBoundsRejectsNonPortableDrawingBrushShape()
    {
        var brush = new FakeDrawingBrush(new FakeDrawing(new Rect(10, 20, 200, 100)))
        {
            Viewbox = new Rect(0.25, 0.2, 0.5, 0.4),
            ViewboxUnits = "RelativeToBoundingBox"
        };

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.False(resolved);
        Assert.Equal(default, bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsRejectsNonPortableVisualBrushShape()
    {
        var brush = new FakeVisualBrush(new FakeVisual(new Rect(4, 8, 80, 40)))
        {
            Viewbox = new Rect(0.5, 0.25, 0.25, 0.5),
            ViewboxUnits = "RelativeToBoundingBox"
        };

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.False(resolved);
        Assert.Equal(default, bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsResolvesPortableDrawingBrushRelativeViewbox()
    {
        var brush = CreatePortableTileBrushSource(
            PortableTileBrushKind.Drawing,
            new FakeDrawing(new Rect(10, 20, 200, 100)),
            viewbox: new PortableRect(0.25, 0.2, 0.5, 0.4),
            viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox);

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(60, 40, 100, 40), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsResolvesPortableVisualBrushRelativeViewbox()
    {
        var brush = CreatePortableTileBrushSource(
            PortableTileBrushKind.Visual,
            new FakeVisual(new Rect(4, 8, 80, 40)),
            viewbox: new PortableRect(0.5, 0.25, 0.25, 0.5),
            viewboxUnits: PortableBrushMappingMode.RelativeToBoundingBox);

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(44, 18, 20, 20), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsKeepsAbsoluteViewboxPrecedence()
    {
        var brush = CreatePortableTileBrushSource(
            PortableTileBrushKind.Drawing,
            new FakeDrawing(new Rect(10, 20, 200, 100)),
            viewbox: new PortableRect(2, 3, 4, 5),
            viewboxUnits: PortableBrushMappingMode.Absolute);

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(2, 3, 4, 5), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsUsesGeometryDrawingBoundsWhenDrawingBoundsAreAbsent()
    {
        var brush = CreatePortableTileBrushSource(
            PortableTileBrushKind.Drawing,
            new FakeGeometryDrawing(
                new FakeRectangleGeometry(new FakeRect(5, 6, 70, 80))));

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(5, 6, 70, 80), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsInfersDrawingGroupBoundsFromChildren()
    {
        var brush = CreatePortableTileBrushSource(
            PortableTileBrushKind.Drawing,
            new FakeDrawingGroup(
                new FakeGeometryDrawing(
                    new FakeRectangleGeometry(new FakeRect(2, 3, 10, 20))),
                new FakeGeometryDrawing(
                    new FakeRectangleGeometry(new FakeRect(20, 7, 5, 8)))));

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(2, 3, 23, 20), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsUsesPortableVisualDescendantBoundsBeforeRenderSizeFallback()
    {
        var visual = new FakeVisualWithDescendantBounds(
            new Rect(3, 4, 30, 40),
            new FakeSize(300, 200));
        var brush = CreatePortableTileBrushSource(PortableTileBrushKind.Visual, visual);

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.True(resolved);
        Assert.Equal(new Rect(3, 4, 30, 40), bounds);
    }

    [Fact]
    public void TryGetBrushSourceBoundsRejectsPortableVisualWithoutReplayBounds()
    {
        var brush = CreatePortableTileBrushSource(
            PortableTileBrushKind.Visual,
            new FakeDesiredSizeVisual(new FakeSize(64, 32)));

        var resolved = WpfShaderEffectSamplerTextureCache.TryGetBrushSourceBounds(brush, out var bounds);

        Assert.False(resolved);
        Assert.Equal(default, bounds);
    }

    private static FakePortableTileBrushSource CreatePortableTileBrushSource(
        PortableTileBrushKind kind,
        object content)
    {
        return CreatePortableTileBrushSource(
            kind,
            content,
            new PortableRect(0, 0, 1, 1),
            PortableBrushMappingMode.RelativeToBoundingBox);
    }

    private static FakePortableTileBrushSource CreatePortableTileBrushSource(
        PortableTileBrushKind kind,
        object content,
        PortableRect viewbox,
        PortableBrushMappingMode viewboxUnits)
    {
        return new FakePortableTileBrushSource(new PortableTileBrush(
            kind,
            content,
            opacity: 1,
            viewport: new PortableRect(0, 0, 1, 1),
            viewbox: viewbox,
            viewportUnits: PortableBrushMappingMode.RelativeToBoundingBox,
            viewboxUnits: viewboxUnits,
            tileMode: PortableTileMode.None,
            stretch: PortableStretch.Fill,
            alignmentX: PortableAlignmentX.Center,
            alignmentY: PortableAlignmentY.Center,
            hasTransform: false,
            transform: PortableMatrix3x2.Identity,
            hasRelativeTransform: false,
            relativeTransform: PortableMatrix3x2.Identity));
    }

    private sealed class FakeDrawingBrush
    {
        public FakeDrawingBrush(object? drawing)
        {
            Drawing = drawing;
        }

        public object? Drawing { get; }

        public Rect Viewbox { get; init; }

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";
    }

    private sealed class FakeVisualBrush
    {
        public FakeVisualBrush(object? visual)
        {
            Visual = visual;
        }

        public object? Visual { get; }

        public Rect Viewbox { get; init; }

        public string ViewboxUnits { get; init; } = "RelativeToBoundingBox";
    }

    private sealed class FakePortableTileBrushSource : IPortableTileBrushSource
    {
        private readonly PortableTileBrush _brush;

        public FakePortableTileBrushSource(PortableTileBrush brush)
        {
            _brush = brush;
        }

        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = _brush;
            return true;
        }
    }

    private sealed class FakeDrawing : IPortableDrawingGroupStateSource
    {
        public FakeDrawing(Rect bounds)
        {
            Bounds = bounds;
        }

        public Rect Bounds { get; }

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = new PortableDrawingGroupState
            {
                HasBounds = true,
                Bounds = ToPortableRect(Bounds)
            };
            return true;
        }
    }

    private sealed class FakeGeometryDrawing : IPortableGeometryDrawingStateSource
    {
        public FakeGeometryDrawing(object? geometry)
        {
            Geometry = geometry;
        }

        public object? Geometry { get; }

        public bool TryGetPortableGeometryDrawingState(out PortableGeometryDrawingState state)
        {
            state = new PortableGeometryDrawingState
            {
                HasGeometry = Geometry != null,
                Geometry = Geometry
            };
            return true;
        }
    }

    private sealed class FakeDrawingGroup : IPortableDrawingGroupStateSource
    {
        private readonly object[] _children;

        public FakeDrawingGroup(params object[] children)
        {
            _children = children;
            Children = new FakeDrawingCollection(children);
        }

        public FakeDrawingCollection Children { get; }

        public bool TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            state = new PortableDrawingGroupState
            {
                Children = _children
            };
            return true;
        }
    }

    private sealed class FakeDrawingCollection
    {
        private readonly object[] _items;

        public FakeDrawingCollection(object[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public object this[int index] => _items[index];
    }

    private sealed class FakeRectangleGeometry : IPortableGeometryPathSource
    {
        public FakeRectangleGeometry(FakeRect rect)
        {
            Rect = rect;
        }

        public FakeRect Rect { get; }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                FillRule = PortableFillRule.Nonzero,
                Figures =
                [
                    new PortablePathFigure
                    {
                        StartPoint = new PortablePoint(Rect.X, Rect.Y),
                        IsClosed = true,
                        IsFilled = true,
                        Segments =
                        [
                            PortablePathSegment.Line(new PortablePoint(Rect.X + Rect.Width, Rect.Y), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(Rect.X + Rect.Width, Rect.Y + Rect.Height), isSmoothJoin: false, isStroked: true),
                            PortablePathSegment.Line(new PortablePoint(Rect.X, Rect.Y + Rect.Height), isSmoothJoin: false, isStroked: true)
                        ]
                    }
                ]
            };
            return true;
        }
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private sealed class FakeVisual : IPortableVisualBoundsSource
    {
        public FakeVisual(Rect contentBounds)
        {
            ContentBounds = contentBounds;
        }

        public Rect ContentBounds { get; }

        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new PortableVisualBounds
            {
                HasContentBounds = true,
                ContentBounds = ToPortableRect(ContentBounds)
            };
            return true;
        }
    }

    private sealed class FakeVisualWithDescendantBounds : IPortableVisualBoundsSource, IPortableVisualLayoutStateSource
    {
        public FakeVisualWithDescendantBounds(Rect descendantBounds, FakeSize renderSize)
        {
            DescendantBounds = descendantBounds;
            RenderSize = renderSize;
        }

        public Rect DescendantBounds { get; }

        public FakeSize RenderSize { get; }

        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new PortableVisualBounds
            {
                HasDescendantBounds = true,
                DescendantBounds = ToPortableRect(DescendantBounds)
            };
            return true;
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

    private sealed class FakeDesiredSizeVisual
    {
        public FakeDesiredSizeVisual(FakeSize desiredSize)
        {
            DesiredSize = desiredSize;
        }

        public FakeSize DesiredSize { get; }
    }

    private readonly record struct FakeSize(double Width, double Height);

    private static PortableRect ToPortableRect(Rect rect)
    {
        return rect.IsEmpty
            ? PortableRect.Empty
            : new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
