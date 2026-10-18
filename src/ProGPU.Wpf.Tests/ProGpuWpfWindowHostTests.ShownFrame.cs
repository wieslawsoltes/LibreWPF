using System;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void NativeShowNotificationObservesCompletedVisibilityWrite()
    {
        using var fixture = new RenderCloseFixture();
        fixture.Probe.Visible = false;
        int notifications = 0;
        fixture.Host.NativeShowCompleted += (_, _) =>
        {
            Assert.True(fixture.Probe.Visible);
            Assert.Equal(1, fixture.Probe.VisibilityWrites);
            notifications++;
        };
        fixture.Host.Show();
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void FailedNativeShowDoesNotPublishFrameNotification()
    {
        using var fixture = new RenderCloseFixture();
        var failure = new InvalidOperationException("Native map failure");
        fixture.Probe.VisibilityWriteFailure = failure;
        int notifications = 0;
        fixture.Host.NativeShowCompleted += (_, _) => notifications++;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(fixture.Host.Show));
        Assert.Equal(0, notifications);
        fixture.Probe.VisibilityWriteFailure = null;
    }

    [Fact]
    public void NativeShowVisibilityReadCannotPublishAfterReentrantHide()
    {
        using var fixture = new RenderCloseFixture();
        int notifications = 0;
        fixture.Host.NativeShowCompleted += (_, _) => notifications++;
        fixture.Probe.VisibilityReadAction = () =>
        {
            fixture.Probe.VisibilityReadAction = null;
            fixture.Host.Hide();
        };
        fixture.Host.Show();
        Assert.False(fixture.Probe.Visible);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void NativeShowVisibilityReadCannotPublishForReplacementWindow()
    {
        using var fixture = new RenderCloseFixture();
        var (replacement, probe) = CreateRetirementWindow();
        int notifications = 0;
        fixture.Host.NativeShowCompleted += (_, _) => notifications++;
        fixture.Probe.VisibilityReadAction = () =>
        {
            fixture.Probe.VisibilityReadAction = null;
            SetPrivateField(fixture.Host, "_window", replacement);
        };
        try
        {
            fixture.Host.Show();
            Assert.Equal(0, notifications);
            Assert.Equal(0, probe.Accesses);
        }
        finally { SetPrivateField(fixture.Host, "_window", fixture.Window); }
    }
}
