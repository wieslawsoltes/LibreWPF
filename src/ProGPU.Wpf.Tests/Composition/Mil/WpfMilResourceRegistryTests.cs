using System;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfMilResourceRegistryTests
{
    [Fact]
    public void RawLookupPreservesSourceIdentityAndRegisteredPrecedence()
    {
        var original = new object();
        var replacement = new object();
        var registry = WpfMilResourceRegistry.FromDependentResources([original, null]);
        var raw = Assert.IsAssignableFrom<IWpfRawMilResourceResolver>(registry);

        Assert.True(raw.TryResolveRawResource(1, out var resolved));
        Assert.Same(original, resolved);
        registry.Register(1, replacement);
        Assert.True(raw.TryResolveRawResource(1, out resolved));
        Assert.Same(replacement, resolved);
        Assert.Null(registry.ResolveBrush(1));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(uint.MaxValue)]
    public void RawLookupRejectsAbsentResources(uint token)
    {
        var registry = WpfMilResourceRegistry.FromDependentResources([new object(), null]);
        var raw = Assert.IsAssignableFrom<IWpfRawMilResourceResolver>(registry);

        Assert.False(raw.TryResolveRawResource(token, out var resolved));
        Assert.Null(resolved);
    }
}
