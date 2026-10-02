using System.Windows.Media.ProGPU;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionRetainedCloseCannotHideOrBeSupersededByShow(bool showAgain)
    {
        using var fixture = new SessionCloseFixture();
        fixture.Host.Close();
        fixture.Host.Close();
        if (showAgain) fixture.Host.Show();
        Assert.Equal(1, fixture.Lease.Requests);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.True(fixture.Inner.Probe.Visible);
        Assert.Same(fixture.Inner.Window, ReadRetirementField(fixture.Host, "_pendingNativeCloseWindow"));
        fixture.Lease.Complete();
        Assert.Equal(1, fixture.Inner.Probe.Closes);
        if (showAgain) Assert.True(fixture.Inner.Probe.Renders > 0);
        Assert.Null(ReadRetirementField(fixture.Host, "_pendingNativeCloseWindow"));
        Assert.Equal(0, fixture.Inner.Probe.Disposals);
    }

    [Fact]
    public void SessionCloseSynchronousReleasePublishesOnlyOneProviderClose()
    {
        using var fixture = new SessionCloseFixture();
        fixture.Lease.CompleteSynchronously = true;
        fixture.Host.Closing += (_, _) => fixture.Host.Close();
        fixture.Host.Close();
        Assert.Equal(2, fixture.Lease.Requests);
        Assert.Equal(1, fixture.Inner.Probe.Closes);
        Assert.Null(ReadRetirementField(fixture.Host, "_pendingNativeCloseWindow"));
        Assert.Equal(false, ReadRetirementField(fixture.Host, "_nativeCloseReleasePending"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionCloseRechecksNewLeaseBeforeProviderClose(bool dispose)
    {
        using var fixture = new SessionCloseFixture();
        fixture.Host.Close();
        if (dispose) fixture.Host.Dispose();
        fixture.Lease.Complete(enterNewLease: true);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(2, fixture.Lease.Requests);
        Assert.NotNull(fixture.Lease.Completion);
        Assert.Equal(0, fixture.Inner.Probe.Disposals);
        fixture.Lease.Complete();
        Assert.Equal(dispose ? 0 : 1, fixture.Inner.Probe.Closes);
        Assert.Equal(dispose ? 1 : 0, fixture.Inner.Probe.Disposals);
        Assert.Equal(3, fixture.Lease.Requests);
    }

    [Fact]
    public void SessionReleasedDisposalFailureRetainsItsQueuedOwnerWithoutClosing()
    {
        using var fixture = new SessionCloseFixture();
        var failure = new RetirementFailureWithInaccessibleData();
        bool fail = true;
        fixture.Host.WpfRenderScheduler = new RenderCloseScheduler(() =>
        {
            if (fail) throw failure;
        });
        fixture.Host.Close();
        fixture.Host.Dispose();
        try
        {
            Assert.Same(failure, Record.Exception(() => fixture.Lease.Complete()));
            Assert.Same(fixture.Inner.Window, fixture.Host.SilkWindow);
            Assert.Equal(0, fixture.Inner.Probe.Disposals);
            Assert.Equal(0, fixture.Inner.Probe.Closes);
        }
        finally { fail = false; }
        DrainRetirements();
        Assert.Null(fixture.Host.SilkWindow);
        Assert.Equal(1, fixture.Inner.Probe.Disposals);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
    }

    [Fact]
    public void SessionReleasedClosingCancellationKeepsLiveHostAndRenderer()
    {
        using var fixture = new SessionCloseFixture();
        fixture.Host.Closing += (_, args) => args.Cancel = true;
        fixture.Host.Close();
        fixture.Lease.Complete();
        Assert.False(fixture.Inner.Probe.Closing);
        Assert.Equal(false, ReadRetirementField(fixture.Host, "_hasNativeWindowCloseStarted"));
        Assert.Same(fixture.Inner.Target, fixture.Host.CompositionTarget);
        Assert.Same(fixture.Inner.Window, fixture.Host.SilkWindow);
        Assert.Null(ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
        fixture.Host.Close();
        Assert.Equal(2, fixture.Inner.Probe.Closes);
        Assert.True(fixture.Inner.Probe.Renders > 0);
        Assert.Equal(0, fixture.Inner.Probe.Disposals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("_isRendering")]
    [InlineData("_isNativeLoopRunning")]
    public void SessionCloseDisposalSupersedesCloseAndRetiresAfterAllOwners(string? activeOwner)
    {
        using var fixture = new SessionCloseFixture();
        fixture.Host.Close();
        if (activeOwner != null) SetPrivateField(fixture.Host, activeOwner, true);
        try
        {
            fixture.Host.Dispose();
            Assert.Equal(0, fixture.Inner.Probe.Disposals);
            Assert.Same(fixture.Inner.Target, fixture.Host.CompositionTarget);
            fixture.Lease.Complete();
            Assert.Equal(0, fixture.Inner.Probe.Closes);
            if (activeOwner != null)
            {
                Assert.Equal(0, fixture.Inner.Probe.Disposals);
                Assert.Same(fixture.Inner.Window, fixture.Host.SilkWindow);
            }
        }
        finally { if (activeOwner != null) SetPrivateField(fixture.Host, activeOwner, false); }
        DrainRetirements();
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(1, fixture.Inner.Probe.Disposals);
        Assert.Null(fixture.Host.SilkWindow);
        Assert.Null(fixture.Host.CompositionTarget);
    }

    [Fact]
    public void SessionRetainedDisposeInitiatedCloseAlsoWaitsBeforeTouchingProvider()
    {
        using var fixture = new SessionCloseFixture();
        SetPrivateField(fixture.Host, "_isRendering", true);
        try
        {
            fixture.Host.Dispose();
            Assert.Equal(1, fixture.Lease.Requests);
            Assert.Equal(0, fixture.Inner.Probe.Closes);
            Assert.Equal(0, fixture.Inner.Probe.Disposals);
        }
        finally { SetPrivateField(fixture.Host, "_isRendering", false); }
        DrainRetirements();
        Assert.Equal(0, fixture.Inner.Probe.Disposals);
        fixture.Lease.Complete();
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(1, fixture.Inner.Probe.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionReleaseFailureRetainsCloseIdentityAndOriginalException(bool identityDisappears)
    {
        using var fixture = new SessionCloseFixture();
        var failure = new RetirementFailureWithInaccessibleData();
        fixture.Lease.Failure = failure;
        fixture.Lease.ReleaseIdentityBeforeFailure = identityDisappears;
        try
        {
            Assert.Same(failure, Record.Exception(fixture.Host.Close));
            Assert.Same(fixture.Inner.Window, ReadRetirementField(fixture.Host, "_pendingNativeCloseWindow"));
            Assert.Equal(false, ReadRetirementField(fixture.Host, "_nativeCloseReleasePending"));
            Assert.Equal(!identityDisappears, fixture.Lease.Retained);
            fixture.Lease.Failure = null;
            Assert.Same(failure, Record.Exception(fixture.Host.Close));
            Assert.Same(failure, Record.Exception(fixture.Host.Dispose));
            Assert.Same(failure, Record.Exception(fixture.Host.Dispose));
            Assert.Same(failure, Record.Exception(DrainRetirements));
            Assert.Equal(1, fixture.Lease.Requests);
            Assert.Equal(0, fixture.Inner.Probe.Closes);
            Assert.Equal(0, fixture.Inner.Probe.Disposals);
            Assert.Same(fixture.Inner.Target, fixture.Host.CompositionTarget);
        }
        finally
        {
            // This fake lease has no native token. Release only the test's
            // headless resources; product code has no uncertain-release reset.
            SetPrivateField<object?>(fixture.Host, "_nativeSessionReleaseFailure", null);
            SetPrivateField<IWindow?>(fixture.Host, "_nativeSessionReleaseWindow", null);
            SetPrivateField<object?>(fixture.Host, "_nativeSessionReleaseCallbacks", null);
            SetPrivateField(fixture.Host, "_nativeSessionReleaseCount", 0);
            SetPrivateField<IWindow?>(fixture.Host, "_pendingNativeCloseWindow", null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionReleasedClosingFailureIsNotReplacedOrReplayed(bool synchronous)
    {
        using var fixture = new SessionCloseFixture();
        var failure = new RetirementFailureWithInaccessibleData();
        fixture.Host.Closing += (_, _) => throw failure;
        fixture.Lease.CompleteSynchronously = synchronous;
        if (synchronous) Assert.Same(failure, Record.Exception(fixture.Host.Close));
        else
        {
            fixture.Host.Close();
            Assert.Same(failure, Record.Exception(() => fixture.Lease.Complete()));
        }
        Assert.Equal(1, fixture.Inner.Probe.Closes);
        Assert.Null(ReadRetirementField(fixture.Host, "_pendingNativeCloseWindow"));
        Assert.Null(ReadRetirementField(fixture.Host, "_nativeSessionReleaseFailure"));
        fixture.Host.Close();
        Assert.Equal(1, fixture.Inner.Probe.Closes);
    }

    [Fact]
    public void SessionCloseRejectsWrongThreadBeforeReleaseOrProviderAccess()
    {
        using var fixture = new SessionCloseFixture();
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(fixture.Host.Close));
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, fixture.Lease.Requests);
        Assert.Equal(0, fixture.Inner.Probe.Closes);
        Assert.Equal(false, ReadRetirementField(fixture.Host, "_hasNativeWindowCloseStarted"));
    }

    [Fact]
    public void SessionCloseCannotApplyRetainedRequestToReplacementWindow()
    {
        using var fixture = new SessionCloseFixture();
        var (replacement, _) = CreateRetirementWindow();
        fixture.Host.Close();
        SetPrivateField(fixture.Host, "_window", replacement);
        try
        {
            Assert.Throws<InvalidOperationException>(() => fixture.Lease.Complete());
            Assert.Equal(0, fixture.Inner.Probe.Closes);
        }
        finally { SetPrivateField(fixture.Host, "_window", fixture.Inner.Window); }
        fixture.Host.Close();
        Assert.Equal(1, fixture.Inner.Probe.Closes);
    }

    private sealed class SessionCloseFixture : IDisposable
    {
        internal readonly RenderCloseFixture Inner = new();
        internal readonly SessionCloseLease Lease;
        internal ProGpuWpfWindowHost Host => Inner.Host;

        internal SessionCloseFixture()
        {
            Lease = new(Inner.Window);
            Host.NativeWindowSessionRelease = Lease.Request;
            Host.NativeWindowSessionRetains = Lease.Retains;
        }

        public void Dispose()
        {
            Lease.Failure = null;
            if (Lease.Completion != null) Lease.Complete();
            Inner.Dispose();
            DrainRetirements();
        }
    }

    private sealed class SessionCloseLease(IWindow window)
    {
        internal bool Retained = true, CompleteSynchronously;
        internal bool ReleaseIdentityBeforeFailure;
        internal int Requests;
        internal Action? Completion;
        internal Exception? Failure;

        internal bool Request(IWindow actual, Action completed)
        {
            Assert.Same(window, actual);
            Requests++;
            if (Failure != null)
            {
                if (ReleaseIdentityBeforeFailure) Retained = false;
                throw Failure;
            }
            if (!Retained) return false;
            Completion += completed;
            if (CompleteSynchronously) Complete();
            return true;
        }

        internal bool Retains(IWindow actual)
        {
            Assert.Same(window, actual);
            return Retained;
        }

        internal void Complete(bool enterNewLease = false)
        {
            Action completed = Assert.IsType<Action>(Completion);
            Completion = null;
            Retained = enterNewLease;
            completed();
        }
    }
}
