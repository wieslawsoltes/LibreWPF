using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public class WpfPortableDocumentFlowTests
{
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
