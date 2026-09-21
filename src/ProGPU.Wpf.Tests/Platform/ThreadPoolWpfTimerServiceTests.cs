using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ThreadPoolWpfTimerServiceTests
{
    [Fact]
    public void CreateTimerRejectsInvalidArguments()
    {
        var service = new ThreadPoolWpfTimerService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateTimer(TimeSpan.Zero, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CreateTimer(TimeSpan.FromMilliseconds(-1), () => { }));
        Assert.Throws<ArgumentNullException>(() => service.CreateTimer(TimeSpan.FromMilliseconds(1), callback: null!));
    }

    [Fact]
    public void SingleShotTimerTicksOnceAndStops()
    {
        var service = new ThreadPoolWpfTimerService();
        using var ticked = new ManualResetEventSlim();
        var tickCount = 0;
        using var timer = service.CreateTimer(
            TimeSpan.FromMilliseconds(5),
            () =>
            {
                Interlocked.Increment(ref tickCount);
                ticked.Set();
            },
            isRepeating: false);

        timer.Start();

        Assert.True(ticked.Wait(TimeSpan.FromSeconds(2)));
        SpinWait.SpinUntil(() => !timer.IsEnabled, TimeSpan.FromSeconds(1));

        Assert.False(timer.IsEnabled);
        Assert.Equal(1, Volatile.Read(ref tickCount));
    }

    [Fact]
    public void StopPreventsFutureTicks()
    {
        var service = new ThreadPoolWpfTimerService();
        var tickCount = 0;
        using var timer = service.CreateTimer(
            TimeSpan.FromMilliseconds(200),
            () => Interlocked.Increment(ref tickCount));

        timer.Start();
        timer.Stop();
        Thread.Sleep(300);

        Assert.False(timer.IsEnabled);
        Assert.Equal(0, Volatile.Read(ref tickCount));
    }

    [Fact]
    public void DisposedTimerRejectsRestart()
    {
        var service = new ThreadPoolWpfTimerService();
        var timer = service.CreateTimer(TimeSpan.FromMilliseconds(10), () => { });

        timer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => timer.Start());
    }
}
