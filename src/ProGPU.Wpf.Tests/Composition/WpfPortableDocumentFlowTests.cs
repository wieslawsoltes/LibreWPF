using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public class WpfPortableDocumentFlowTests
{
    [Fact]
    public void AnchorAdapterRetainsNativeLayoutsAndAtomicBatches()
    {
        static void SameLayout<TSource, TNative>() where TSource : struct where TNative : struct
        {
            Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf<TNative>(), System.Runtime.InteropServices.Marshal.SizeOf<TSource>());
            foreach (var field in typeof(TSource).GetFields())
                Assert.Equal(System.Runtime.InteropServices.Marshal.OffsetOf<TNative>(field.Name),
                    System.Runtime.InteropServices.Marshal.OffsetOf<TSource>(field.Name));
        }
        SameLayout<PortableDocumentAnchorWidthRequest, ProGPU.Backend.Native.NativeDocumentAnchorWidthRequest>();
        SameLayout<PortableDocumentAnchorWidthResult, ProGPU.Backend.Native.NativeDocumentAnchorWidthResult>();
        SameLayout<PortableDocumentAnchorRequest, ProGPU.Backend.Native.NativeDocumentAnchorRequest>();
        SameLayout<PortableDocumentAnchorRectangle, ProGPU.Backend.Native.NativeDocumentAnchorRectangle>();
        IPortableAnchoredDocumentFlow provider = new WpfPortableDocumentFlow();
        PortableDocumentAnchorWidthRequest[] widths = [new() { AvailableWidth = 100, HorizontalInsets = 12,
            MeasuredWidth = 37, HasMeasurement = 1, Mode = PortableDocumentAnchorWidthMode.FitContent }];
        PortableDocumentAnchorWidthResult[] resolved = new PortableDocumentAnchorWidthResult[1];
        provider.ResolveAnchorWidths(widths, resolved);
        Assert.Equal(37, resolved[0].ContentWidth); Assert.Equal(49, resolved[0].OuterWidth);
        Assert.Equal(1U, resolved[0].RequiresRemeasure);
        PortableDocumentAnchorRequest[] requests = [
            new() { Right = 100, Bottom = 100, Width = 30, Height = 10, Alignment = PortableDocumentAnchorAlignment.Right, AllowDelay = 1, MaximumAttempts = 8 },
            new() { Right = 100, Bottom = 100, Width = 30, Height = 15, Alignment = PortableDocumentAnchorAlignment.Right, AllowDelay = 1, MaximumAttempts = 8 }];
        PortableDocumentAnchorRectangle[] placed = new PortableDocumentAnchorRectangle[2];
        provider.PlaceAnchors(requests, [new() { Left = 70, Right = 100, Bottom = 20 }], placed);
        Assert.Equal(70, placed[0].Left); Assert.Equal(20, placed[0].Top); Assert.Equal(30, placed[1].Top);
        requests[0].Alignment = PortableDocumentAnchorAlignment.Left;
        requests[1].Reserved = 1;
        Assert.Throws<ProGPU.Backend.Native.NativeRendererException>(() => provider.PlaceAnchors(requests, [], placed));
        Assert.Equal(70, placed[0].Left); Assert.Equal(30, placed[1].Top);
    }

    [Fact]
    public void RowAdapterRetainsSharedColumnConstraintsAndSourceLineOrder()
    {
        IPortableDocumentFlow provider = new WpfPortableDocumentFlow();
        PortableDocumentBlock[] blocks = [
            new() { ParentIndex = uint.MaxValue, SubtreeEnd = 3 },
            new() { ParentIndex = 0, SubtreeEnd = 2, LineCount = 2 },
            new() { ParentIndex = 0, SubtreeEnd = 3, LineStart = 2, LineCount = 1 }];
        PortableDocumentRow[] rows = [new() { BlockIndex = 0, ColumnCount = 2, CellSpacing = 2 }];
        PortableDocumentCell[] cells = [
            new() { BlockIndex = 1, ColumnCount = 1 },
            new() { BlockIndex = 2, ColumnStart = 1, ColumnCount = 1 }];
        double[] columns = [80, 120];
        PortableDocumentBox[] boxes = new PortableDocumentBox[3];
        provider.ResolveWidthsWithRows(blocks, 100, rows, columns, cells, boxes);
        Assert.Equal(80, boxes[1].Width); Assert.Equal(120, boxes[2].Width);
        PortableDocumentLine[] lines = [new() { Width = 40, Height = 12 },
            new() { Width = 30, Height = 12 }, new() { Width = 60, Height = 16 }];
        PortableDocumentLinePosition[] positions = new PortableDocumentLinePosition[3];
        Assert.Equal(new PortableDocumentExtent(204, 26),
            provider.ArrangeWithRows(blocks, 100, lines, [], rows, columns, cells, boxes, positions));
        Assert.Equal(1, positions[0].X); Assert.Equal(83, positions[2].X);
        Assert.Equal(1, positions[0].Y); Assert.Equal(13, positions[1].Y); Assert.Equal(1, positions[2].Y);
        Assert.Equal(24, boxes[1].Height); Assert.Equal(24, boxes[2].Height);
    }

    [Fact]
    public void TypedAdapterPreservesNativeAdmissionWithoutLoadingADevice()
    {
        IPortableDocumentFlow provider = new WpfPortableDocumentFlow();
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.ResolveWidths([], double.NaN, []));
        Assert.Throws<ArgumentException>(() => provider.ResolveWidths([new()], 100, []));
        Assert.Throws<ArgumentException>(() => provider.Arrange([], 100, [new()], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.ArrangeWithObjects([], double.NaN, [], [], [], []));
        Assert.Throws<ArgumentException>(() => provider.ArrangeWithObjects([new()], 100, [], [new()], [], []));
        Assert.Throws<ArgumentException>(() => provider.ArrangeWithObjects([], 100, [new()], [], [], []));
    }

    [Fact]
    public void MeasuredBlockObjectUsesNativePlacementWithoutFabricatedTextLines()
    {
        IPortableDocumentFlow provider = new WpfPortableDocumentFlow();
        PortableDocumentBlock[] blocks = [new() {
            ParentIndex = PortableDocumentBlock.NoParent, SubtreeEnd = 1,
            InsetLeft = 4, InsetTop = 3, InsetRight = 6, InsetBottom = 5 }];
        PortableDocumentBox[] boxes = new PortableDocumentBox[1];
        provider.ResolveWidths(blocks, 100, boxes);
        Assert.Equal(90, boxes[0].Width);
        PortableDocumentObject[] objects = [new() { BlockIndex = 0, Width = 40, Height = 32 }];
        var extent = provider.ArrangeWithObjects(blocks, 100, [], objects, boxes, []);
        Assert.Equal(new PortableDocumentExtent(100, 40), extent);
        Assert.Equal(4, boxes[0].X); Assert.Equal(3, boxes[0].Y);
        Assert.Equal(90, boxes[0].Width); Assert.Equal(32, boxes[0].Height);
        objects[0].Height = 48;
        Assert.Equal(new PortableDocumentExtent(100, 56),
            provider.ArrangeWithObjects(blocks, 100, [], objects, boxes, []));
        Assert.Equal(48, boxes[0].Height);
    }
}
