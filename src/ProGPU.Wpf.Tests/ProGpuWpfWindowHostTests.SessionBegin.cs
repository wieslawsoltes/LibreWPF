using System.IO;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void NativeDialogDefaultDoesNotBeginOrChangeOwnerLease()
    {
        using var fixture = new SessionCloseFixture();
        fixture.Host.NativeWindowSessionBeginOverride = _ => throw new Exception("unexpected Begin");
        fixture.Host.BeginNativeDialogSession();
        Assert.Equal(0, fixture.Lease.Requests);
        Assert.False(new ProGpuWpfWindowOptions().EnableNativeModalSessions);
    }

    [Theory]
    [InlineData("popup")]
    [InlineData("source")]
    [InlineData("hidden")]
    [InlineData("closing")]
    public void NativeDialogRequiresVisibleOriginalSourceTopLevel(string invalid)
    {
        using var fixture = new SessionCloseFixture();
        PrepareNativeDialog(fixture);
        var options = Assert.IsType<ProGpuWpfWindowOptions>(ReadRetirementField(fixture.Host, "_options"));
        if (invalid == "popup") options.IsPopupSurface = true;
        if (invalid == "source") SetPrivateField(fixture.Host, "_modalInputOwner", null);
        if (invalid == "hidden") fixture.Inner.Probe.Visible = false;
        if (invalid == "closing") SetPrivateField(fixture.Host, "_hasNativeWindowCloseStarted", true);
        int begins = 0;
        fixture.Host.NativeWindowSessionBeginOverride = _ => { begins++; return true; };
        Assert.Throws<InvalidOperationException>(fixture.Host.BeginNativeDialogSession);
        Assert.Equal(0, begins);
    }

    [Fact]
    public void NativeDialogReleasePrecedesSourceRestorationAndPermitsLaterDialog()
    {
        using var fixture = new SessionCloseFixture();
        PrepareNativeDialog(fixture);
        int begins = 0, restores = 0;
        fixture.Host.NativeWindowSessionBeginOverride = window =>
        {
            Assert.Same(fixture.Inner.Window, window);
            fixture.Lease.Retained = true;
            begins++;
            return true;
        };
        fixture.Host.BeginNativeDialogSession();
        Assert.Throws<InvalidOperationException>(fixture.Host.BeginNativeDialogSession);
        fixture.Host.ReleaseNativeDialog(() => restores++);
        Assert.Equal(0, restores);
        fixture.Lease.Complete();
        Assert.Equal(1, restores);
        fixture.Host.BeginNativeDialogSession();
        Assert.Equal(2, begins);
        fixture.Host.ReleaseNativeDialog(() => restores++);
        fixture.Lease.Complete();
        Assert.Equal(2, restores);
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("close")]
    [InlineData("dispose")]
    [InlineData("release")]
    public void NativeBeginCallbackDefersEndAndRetainsExactHost(string action)
    {
        using var fixture = new SessionCloseFixture();
        PrepareNativeDialog(fixture);
        int completed = 0;
        fixture.Host.NativeWindowSessionBeginOverride = window =>
        {
            fixture.Lease.Retained = true;
            if (action == "hide") fixture.Host.Hide();
            else if (action == "close") fixture.Host.Close();
            else if (action == "dispose") fixture.Host.Dispose();
            else fixture.Host.ReleaseNativeDialog(() => completed++);
            Assert.Same(window, fixture.Host.SilkWindow);
            Assert.Equal(0, fixture.Lease.Requests);
            Assert.Equal(0, fixture.Inner.Probe.Closes);
            Assert.Equal(0, fixture.Inner.Probe.Disposals);
            Assert.True(fixture.Inner.Probe.Visible);
            Assert.Equal(0, completed);
            return true;
        };
        fixture.Host.BeginNativeDialogSession();
        Assert.Equal(1, fixture.Lease.Requests);
        fixture.Lease.Complete();
        Assert.Equal(action == "release" ? 1 : 0, completed);
        Assert.Equal(action == "close" ? 1 : 0, fixture.Inner.Probe.Closes);
        Assert.Equal(action == "dispose" ? 1 : 0, fixture.Inner.Probe.Disposals);
        if (action == "hide") Assert.False(fixture.Inner.Probe.Visible);
    }

    [Fact]
    public void NativeBeginRejectsProviderFailureWithoutOrdinaryPollFallback()
    {
        using var fixture = new SessionCloseFixture();
        PrepareNativeDialog(fixture);
        fixture.Host.NativeWindowSessionBeginOverride = _ => false;
        Assert.Throws<PlatformNotSupportedException>(fixture.Host.BeginNativeDialogSession);
        Assert.Equal(false, ReadRetirementField(fixture.Host, "_ownsNativeDialog"));
        Assert.Equal(0, fixture.Inner.Probe.Closes);
    }

    [Fact]
    public void SourceDialogRunOwnsBeginAndExistingSourceCompletionOwnsRelease()
    {
        string host = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));
        Assert.Contains("if (continueRunning != null && _isHostVisible && continueRunning())\n            BeginNativeDialogSession();", host);
        string activation = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "WpfPortableWindowActivation.cs"));
        Assert.Contains("EnableNativeModalSessions = fallback.EnableNativeModalSessions", activation);
        Assert.Contains("Host.ReleaseNativeDialog(completed)", activation);
    }

    private static void PrepareNativeDialog(SessionCloseFixture fixture)
    {
        Assert.IsType<ProGpuWpfWindowOptions>(ReadRetirementField(fixture.Host, "_options")).EnableNativeModalSessions = true;
        SetPrivateField(fixture.Host, "_modalInputOwner", new object());
        SetPrivateField(fixture.Host, "_isHostVisible", true);
        fixture.Lease.Retained = false;
    }
}
