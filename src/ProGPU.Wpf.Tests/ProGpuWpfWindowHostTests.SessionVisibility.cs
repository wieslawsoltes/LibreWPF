using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionCloseAndHideCallbacksCannotWriteToAlreadyClosingPopup(bool closeFirst)
    {
        using var fixture = new SessionCloseFixture();
        fixture.Inner.Probe.HideBeforeClose = true;
        fixture.Inner.Probe.RejectVisibilityAfterClose = true;
        if (closeFirst) { fixture.Host.Close(); fixture.Host.Hide(); }
        else { fixture.Host.Hide(); fixture.Host.Close(); }
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(0, fixture.Inner.Probe.VisibilityWrites);
        Assert.True(fixture.Inner.Probe.Visible);
        fixture.Lease.Complete();
        Assert.Equal(1, fixture.Inner.Probe.Closes);
        Assert.False(fixture.Inner.Probe.Visible);
        Assert.Equal(0, fixture.Inner.Probe.VisibilityWrites);
        Assert.Equal(false, ReadRetirementField(fixture.Host, "_nativeHidePending"));
        Assert.Equal(0, ReadRetirementField(fixture.Host, "_nativeSessionReleaseCount"));
    }

    [Fact]
    public void SessionOrdinaryPendingHideCanBeSupersededByShow()
    {
        using var fixture = new SessionCloseFixture();
        fixture.Host.Hide();
        fixture.Host.Show();
        int writesAfterShow = fixture.Inner.Probe.VisibilityWrites;
        Assert.True(writesAfterShow > 0);
        fixture.Lease.Complete();
        Assert.True(fixture.Inner.Probe.Visible);
        Assert.Equal(writesAfterShow, fixture.Inner.Probe.VisibilityWrites);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(false, ReadRetirementField(fixture.Host, "_nativeHidePending"));
    }

    [Theory]
    [InlineData("lease")]
    [InlineData("show")]
    [InlineData("close")]
    [InlineData("dispose")]
    public void SessionHideRechecksReentrantVisibilityRead(string action)
    {
        using var fixture = new SessionCloseFixture();
        fixture.Lease.Retained = false;
        fixture.Inner.Probe.HideBeforeClose = true;
        fixture.Inner.Probe.RejectVisibilityAfterClose = true;
        fixture.Inner.Probe.VisibilityReadAction = () =>
        {
            fixture.Inner.Probe.VisibilityReadAction = null;
            switch (action)
            {
                case "lease": fixture.Lease.Retained = true; break;
                case "show": fixture.Host.Show(); break;
                case "close": fixture.Host.Close(); break;
                case "dispose": fixture.Host.Dispose(); break;
            }
        };
        fixture.Host.Hide();
        if (action == "lease")
        {
            Assert.Equal(0, fixture.Inner.Probe.VisibilityWrites);
            Assert.True(fixture.Inner.Probe.Visible);
            Assert.NotNull(fixture.Lease.Completion);
            Assert.Equal(1, ReadRetirementField(fixture.Host, "_nativeSessionReleaseCount"));
            fixture.Lease.Complete();
            Assert.Equal(1, fixture.Inner.Probe.VisibilityWrites);
            Assert.False(fixture.Inner.Probe.Visible);
        }
        else if (action == "show")
        {
            Assert.Equal(1, fixture.Inner.Probe.VisibilityWrites);
            Assert.True(fixture.Inner.Probe.Visible);
        }
        else
        {
            Assert.Equal(0, fixture.Inner.Probe.VisibilityWrites);
            Assert.Equal(1, fixture.Inner.Probe.Closes);
            if (action == "dispose")
            {
                Assert.Equal(1, fixture.Inner.Probe.Disposals);
                Assert.Null(fixture.Host.SilkWindow);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SessionHideOrDialogUncertainFailureBlocksAllHostTransitions(bool dialog, bool identityDisappears)
    {
        using var fixture = new SessionCloseFixture();
        var failure = new RetirementFailureWithInaccessibleData();
        int completed = 0;
        fixture.Lease.Failure = failure;
        fixture.Lease.ReleaseIdentityBeforeFailure = identityDisappears;
        try
        {
            Action request = dialog ? () => fixture.Host.ReleaseNativeDialog(() => completed++) : fixture.Host.Hide;
            Assert.Same(failure, Record.Exception(request));
            Assert.Equal(!identityDisappears, fixture.Lease.Retained);
            fixture.Lease.Failure = null;
            Assert.Same(failure, Record.Exception(fixture.Host.Hide));
            Assert.Same(failure, Record.Exception(fixture.Host.Close));
            Assert.Same(failure, Record.Exception(() => fixture.Host.ReleaseNativeDialog(() => completed++)));
            Assert.Same(failure, Record.Exception(fixture.Host.Show));
            Assert.Same(failure, Record.Exception(() => fixture.Host.ShowWithoutActivation()));
            Assert.Same(failure, Record.Exception(fixture.Host.Dispose));
            Assert.Same(failure, Record.Exception(DrainRetirements));
            Assert.Equal(1, fixture.Lease.Requests);
            Assert.Equal(0, completed);
            Assert.Equal(0, fixture.Inner.Probe.Closes);
            Assert.Equal(0, fixture.Inner.Probe.VisibilityWrites);
            Assert.Equal(0, fixture.Inner.Probe.Disposals);
            Assert.Same(fixture.Inner.Window, ReadRetirementField(fixture.Host, "_nativeSessionReleaseWindow"));
            Assert.Same(fixture.Inner.Target, fixture.Host.CompositionTarget);
        }
        finally
        {
            // Test-only release of a fake token/headless owner. Product exposes
            // no reset for an uncertain native identity-release failure.
            SetPrivateField<object?>(fixture.Host, "_nativeSessionReleaseFailure", null);
            SetPrivateField<IWindow?>(fixture.Host, "_nativeSessionReleaseWindow", null);
            SetPrivateField<object?>(fixture.Host, "_nativeSessionReleaseCallbacks", null);
            SetPrivateField(fixture.Host, "_nativeSessionReleaseCount", 0);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SessionHideOrDialogDeliveredErrorsAreNotUncertainRelease(bool dialog, bool synchronous)
    {
        using var fixture = new SessionCloseFixture();
        var failure = new RetirementFailureWithInaccessibleData();
        int completed = 0;
        fixture.Lease.CompleteSynchronously = synchronous;
        if (!dialog) fixture.Inner.Probe.VisibilityWriteFailure = failure;
        Action request = dialog ? () => fixture.Host.ReleaseNativeDialog(() => { completed++; throw failure; }) : fixture.Host.Hide;
        try
        {
            if (synchronous) Assert.Same(failure, Record.Exception(request));
            else
            {
                request();
                Assert.Same(failure, Record.Exception(() => fixture.Lease.Complete()));
            }
            Assert.Null(ReadRetirementField(fixture.Host, "_nativeSessionReleaseFailure"));
            Assert.Null(ReadRetirementField(fixture.Host, "_nativeSessionReleaseWindow"));
            Assert.Equal(0, ReadRetirementField(fixture.Host, "_nativeSessionReleaseCount"));
            Assert.Equal(dialog ? 1 : 0, completed);
        }
        finally { fixture.Inner.Probe.VisibilityWriteFailure = null; }
        if (!dialog)
        {
            fixture.Host.Hide();
            Assert.False(fixture.Inner.Probe.Visible);
            Assert.Equal(2, fixture.Inner.Probe.VisibilityWrites);
        }
    }

    [Fact]
    public void SessionDialogCompletionRechecksNewLease()
    {
        using var fixture = new SessionCloseFixture();
        int completed = 0;
        fixture.Host.ReleaseNativeDialog(() => completed++);
        fixture.Lease.Complete(enterNewLease: true);
        Assert.Equal(0, completed);
        Assert.NotNull(fixture.Lease.Completion);
        fixture.Lease.Complete();
        Assert.Equal(1, completed);
        Assert.Equal(0, ReadRetirementField(fixture.Host, "_nativeSessionReleaseCount"));
    }

    [Fact]
    public void SessionDialogCallbackFailureDoesNotSuppressReadyHide()
    {
        using var fixture = new SessionCloseFixture();
        var failure = new RetirementFailureWithInaccessibleData();
        int completed = 0;
        fixture.Host.ReleaseNativeDialog(() => { completed++; throw failure; });
        fixture.Host.Hide();
        Assert.Equal(1, fixture.Lease.Requests);
        Assert.Same(failure, Record.Exception(() => fixture.Lease.Complete()));
        Assert.Equal(1, completed);
        Assert.False(fixture.Inner.Probe.Visible);
        Assert.Equal(1, fixture.Inner.Probe.VisibilityWrites);
        Assert.Null(ReadRetirementField(fixture.Host, "_nativeSessionReleaseFailure"));
        Assert.Equal(0, ReadRetirementField(fixture.Host, "_nativeSessionReleaseCount"));
    }

    [Fact]
    public void SessionUndeliveredCompletionCannotBeBypassedByAbsentNativeQuery()
    {
        using var fixture = new SessionCloseFixture();
        int completed = 0;
        fixture.Host.Hide();
        fixture.Lease.Retained = false;
        Assert.Throws<InvalidOperationException>(fixture.Host.Show);
        fixture.Host.Close();
        fixture.Host.ReleaseNativeDialog(() => completed++);
        fixture.Host.Dispose();
        Assert.Equal(1, fixture.Lease.Requests);
        Assert.Equal(0, completed);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(0, fixture.Inner.Probe.Disposals);
        Assert.Equal(0, fixture.Inner.Probe.VisibilityWrites);
        Assert.Same(fixture.Inner.Window, fixture.Host.SilkWindow);
        fixture.Lease.Complete();
        Assert.Equal(1, completed);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(1, fixture.Inner.Probe.Disposals);
        Assert.Null(fixture.Host.SilkWindow);
    }
}
