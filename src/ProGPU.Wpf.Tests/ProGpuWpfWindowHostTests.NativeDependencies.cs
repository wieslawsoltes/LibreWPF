using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void NativeRetirementDependenciesWaitForActualWindowDestruction()
    {
        var (window, _) = CreateRetirementWindow();
        var order = new List<string>();
        int attempts = 0;
        var retirement = new WpfNativeWindowRetirement(window, Environment.CurrentManagedThreadId,
            () => order.Add("renderer"), actual =>
            {
                Assert.Same(window, actual);
                order.Add("native-attempt");
                return ++attempts == 3;
            }, () => order.Add("native-owner"));
        Assert.False(retirement.TryComplete());
        Assert.False(retirement.TryComplete());
        Assert.DoesNotContain("native-owner", order);
        Assert.True(retirement.TryComplete());
        Assert.True(retirement.TryComplete());
        Assert.Equal(new[] { "renderer", "native-attempt", "native-attempt", "native-attempt", "native-owner" }, order);
    }

    [Fact]
    public void NativeRetirementDependencyFailureRetainsOnlyItsUnfinishedRelease()
    {
        var (window, _) = CreateRetirementWindow();
        int resources = 0, native = 0, dependency = 0;
        var original = new InvalidOperationException("hidden owner destruction failed");
        var retirement = new WpfNativeWindowRetirement(window, Environment.CurrentManagedThreadId,
            () => resources++, _ => { native++; return true; }, () =>
            {
                if (++dependency == 1) throw original;
            });
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => retirement.TryComplete()));
        Assert.False(retirement.IsComplete);
        Assert.Same(window, retirement.Window);
        Assert.True(retirement.TryComplete());
        Assert.True(retirement.TryComplete());
        Assert.Equal(1, resources);
        Assert.Equal(1, native);
        Assert.Equal(2, dependency);
    }

    [Fact]
    public void NativeRetirementDependencyCannotReenterOrMoveRetryToAnotherThread()
    {
        var (window, _) = CreateRetirementWindow();
        int releases = 0;
        WpfNativeWindowRetirement? retirement = null;
        retirement = new(window, Environment.CurrentManagedThreadId, () => { }, _ => true, () =>
        {
            releases++;
            Assert.False(retirement!.TryComplete());
            Assert.False(retirement.IsComplete);
        });
        Exception? wrongThread = null;
        var thread = new Thread(() => wrongThread = Record.Exception(() => retirement.TryComplete()));
        thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Equal(0, releases);
        Assert.True(retirement.TryComplete());
        Assert.Equal(1, releases);
    }

    [Fact]
    public void NativeRetirementDoesNotReleaseDependenciesAfterNativeException()
    {
        var (window, _) = CreateRetirementWindow();
        int releases = 0;
        var original = new InvalidOperationException("native child remains live");
        var retirement = new WpfNativeWindowRetirement(window, Environment.CurrentManagedThreadId,
            () => { }, _ => throw original, () => releases++);
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => retirement.TryComplete()));
        Assert.False(retirement.IsComplete);
        Assert.Equal(0, releases);
    }
}
