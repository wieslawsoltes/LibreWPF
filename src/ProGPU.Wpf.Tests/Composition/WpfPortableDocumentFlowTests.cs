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
    }
}
