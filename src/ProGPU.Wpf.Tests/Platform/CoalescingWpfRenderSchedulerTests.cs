using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class CoalescingWpfRenderSchedulerTests
{
    [Fact]
    public void RequestRenderCoalescesPendingRequests()
    {
        var scheduler = new CoalescingWpfRenderScheduler();
        var requestCount = 0;
        scheduler.RenderRequested += (_, _) => requestCount++;

        scheduler.RequestRender();
        scheduler.RequestRender();

        Assert.True(scheduler.HasPendingRenderRequest);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void ConsumeRenderRequestClearsPendingRequest()
    {
        var scheduler = new CoalescingWpfRenderScheduler();
        scheduler.RequestRender();

        Assert.True(scheduler.ConsumeRenderRequest());
        Assert.False(scheduler.HasPendingRenderRequest);
        Assert.False(scheduler.ConsumeRenderRequest());
    }

    [Fact]
    public void ResetClearsPendingRequest()
    {
        var scheduler = new CoalescingWpfRenderScheduler();
        scheduler.RequestRender();

        scheduler.Reset();

        Assert.False(scheduler.HasPendingRenderRequest);
    }
}
