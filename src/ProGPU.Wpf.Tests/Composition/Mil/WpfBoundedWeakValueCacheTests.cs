using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Windows.Media.ProGPU.Composition.Mil;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfBoundedWeakValueCacheTests
{
    [Fact]
    public void RecentlyUsedValuesSurviveCapacityTrimming()
    {
        var cache = new WpfBoundedWeakValueCache<string, object>(
            capacity: 3,
            StringComparer.Ordinal);
        var first = cache.GetOrAdd("first", static _ => new object());
        var second = cache.GetOrAdd("second", static _ => new object());
        var third = cache.GetOrAdd("third", static _ => new object());

        Assert.Same(first, cache.GetOrAdd("first", static _ => new object()));
        var fourth = cache.GetOrAdd("fourth", static _ => new object());

        Assert.Equal(cache.Capacity, cache.Count);
        Assert.True(cache.TryGetValue("first", out var retainedFirst));
        Assert.Same(first, retainedFirst);
        Assert.False(cache.TryGetValue("second", out _));
        Assert.True(cache.TryGetValue("third", out var retainedThird));
        Assert.Same(third, retainedThird);
        Assert.True(cache.TryGetValue("fourth", out var retainedFourth));
        Assert.Same(fourth, retainedFourth);
        GC.KeepAlive(second);
    }

    [Fact]
    public void ConcurrentUniqueKeysRemainBounded()
    {
        const int capacity = 16;
        var cache = new WpfBoundedWeakValueCache<int, object>(capacity);
        var retained = new ConcurrentBag<object>();

        Parallel.For(
            0,
            512,
            key => retained.Add(cache.GetOrAdd(key, static _ => new object())));

        Assert.InRange(cache.Count, 1, capacity);
        GC.KeepAlive(retained);
    }
}
