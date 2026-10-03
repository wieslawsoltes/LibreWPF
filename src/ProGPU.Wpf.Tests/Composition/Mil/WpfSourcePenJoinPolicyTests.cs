using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;
using VectorPen = ProGPU.Vector.Pen;
using VectorJoin = ProGPU.Vector.PenLineJoin;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfSourcePenJoinPolicyTests
{
    private static readonly WpfReplayRect Bounds = new(5, 7, 20, 30);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CachedLocalPenRetainsPolicyAcrossReuseAndSourceChanges(int join)
    {
        var source = new Pen(Brushes.Black, 4)
        {
            LineJoin = (PenLineJoin)join,
            MiterLimit = 1,
            DashStyle = new DashStyle([2, 3], 0.5)
        };
        VectorPen first = Adapt(source);
        AssertPolicy(first, join);
        Assert.Same(first, Adapt(source));

        source.LineJoin = (PenLineJoin)((join + 1) % 3);
        source.DashStyle.Dashes = [5, 7];
        VectorPen changed = Adapt(source);
        Assert.NotSame(first, changed);
        AssertPolicy(changed, (join + 1) % 3);
        Assert.Equal(new double[] { 5, 7 }, changed.DashArray);
        AssertPolicy(first, join);
        Assert.Equal(new double[] { 2, 3 }, first.DashArray);
        Assert.Equal(0.5, first.DashOffset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TypedPortablePenUsesSamePolicyWithoutLocalShimIdentity(int join)
    {
        var source = new PenSource(new PortablePen(
            PortableBrush.SolidColor(new PortableColor(255, 10, 20, 30)),
            4, PortablePenLineCap.Square, PortablePenLineCap.Triangle,
            PortablePenLineCap.Round, (PortablePenLineJoin)join, 1, [2, 3], 0.5));
        VectorPen adapted = Adapt(source);
        AssertPolicy(adapted, join);
        Assert.Equal(ProGPU.Vector.PenLineCap.Square, adapted.StartLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Triangle, adapted.EndLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Round, adapted.DashCap);
        Assert.Equal(new double[] { 2, 3 }, adapted.DashArray);
        Assert.Equal(0.5, adapted.DashOffset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RawCachedStrokeStateKeepsSourceBrushSeparateFromCoveragePen(int join)
    {
        var sourceBrush = new object();
        var state = new PortablePenState(sourceBrush, 4, PortablePenLineCap.Square,
            PortablePenLineCap.Triangle, PortablePenLineCap.Round,
            (PortablePenLineJoin)join, 1, new double[] { 2, 3 }, 0.5);
        Assert.True(WpfResourceResolver.TryAdaptNativeStrokePen(state, out var adapted));
        AssertPolicy(adapted, join);
        Assert.Same(sourceBrush, state.Brush);
        Assert.Equal(Vector4.One, Assert.IsType<ProGPU.Vector.SolidColorBrush>(adapted.Brush).Color);
        Assert.Equal(ProGPU.Vector.PenLineCap.Square, adapted.StartLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Triangle, adapted.EndLineCap);
        Assert.Equal(ProGPU.Vector.PenLineCap.Round, adapted.DashCap);
        adapted.DashArray = [11];
        Assert.Equal(new double[] { 2, 3 }, state.Dashes.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecordedNativePathKeepsOriginalSmoothFlagAndSourcePolicy(int join)
    {
        var source = new Pen(Brushes.Black, 4) { LineJoin = (PenLineJoin)join, MiterLimit = 1 };
        var geometry = new PortableGeometryPath
        {
            Figures = [new PortablePathFigure
            {
                StartPoint = new(4, 20), IsFilled = false, IsClosed = false,
                Segments = [PortablePathSegment.Line(new(20, 20), false, true),
                    PortablePathSegment.Line(new(20, 4), true, true)]
            }]
        };
        var commands = new ProGPU.Scene.DrawingContext();
        try
        {
            using var sink = new ProGpuCompositionCommandSink(commands);
            Assert.True(sink.DrawNativeGeometry(null, source, geometry));
            var command = Assert.Single(commands.Commands);
            Assert.Equal(ProGPU.Scene.RenderCommandType.DrawPath, command.Type);
            AssertPolicy(Assert.IsType<VectorPen>(command.Pen), join);
            var figure = Assert.Single(command.Path!.Figures);
            Assert.False(figure.Segments[0].IsSmoothJoin);
            Assert.True(figure.Segments[1].IsSmoothJoin);
            Assert.True(figure.Segments[1].IsStroked);
            Assert.Equal(Matrix4x4.Identity, command.Transform);

            source.LineJoin = (PenLineJoin)((join + 1) % 3);
            AssertPolicy(command.Pen!, join);
        }
        finally { commands.Clear(); }
    }

    [Fact]
    public void UnsupportedRawSourceJoinRemainsAtomicAndGenericPenDefaultStaysUnchanged()
    {
        var source = new PortablePenState(new object(), 4, 0, 0, 0,
            (PortablePenLineJoin)3, 1, default, 0);
        Assert.False(WpfResourceResolver.TryAdaptNativeStrokePen(source, out var rejected));
        Assert.Null(rejected);
        var generic = new VectorPen(new ProGPU.Vector.SolidColorBrush(Vector4.One), 4);
        Assert.False(generic.UseWpfJoinSemantics);
    }

    private static VectorPen Adapt(object source)
    {
        var pen = WpfResourceResolver.AdaptNativePen(source, Bounds, out int unsupported);
        Assert.Equal(0, unsupported);
        return Assert.IsType<VectorPen>(pen);
    }

    private static void AssertPolicy(VectorPen pen, int join)
    {
        Assert.True(pen.UseWpfJoinSemantics);
        Assert.False(pen.ClipMiterAtLimit);
        Assert.Equal((VectorJoin)join, pen.LineJoin);
        Assert.Equal(ProGPU.Vector.PenStrokeTransformMode.Normal, pen.StrokeTransformMode);
        Assert.False(pen.IsHairline);
        Assert.Equal(4f, pen.Thickness);
        Assert.Equal(1f, pen.MiterLimit);
    }

    private sealed class PenSource(PortablePen snapshot) : IPortablePenSource
    {
        public bool TryGetPortablePen(out PortablePen pen) { pen = snapshot; return true; }
    }
}
