using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void X11TaskbarIntentWaitsForActualShowAndSurvivesHideShow()
    {
        using var fixture = new RenderCloseFixture();
        fixture.Probe.Visible = false;
        var values = new List<bool>();
        fixture.Host.X11TaskbarVisibilitySetterOverride = (window, value) =>
        {
            Assert.Same(fixture.Window, window);
            Assert.True(fixture.Probe.Visible);
            values.Add(value);
            return true;
        };
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        Assert.Empty(values);
        fixture.Host.Show();
        Assert.Equal(new[] { false }, values);
        Assert.True(fixture.Host.SetX11TaskbarVisibility(true));
        fixture.Host.Hide();
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        fixture.Host.Show();
        Assert.Equal(new[] { false, true, false }, values);
    }

    [Fact]
    public void X11TaskbarUnselectedHostDoesNotSendAndNativeFailureStaysFalse()
    {
        using var fixture = new RenderCloseFixture();
        int sends = 0;
        fixture.Host.X11TaskbarVisibilitySetterOverride = (_, _) => { sends++; return false; };
        fixture.Host.Show();
        Assert.Equal(0, sends);
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        Assert.Equal(1, sends);
    }

    [Fact]
    public void X11TaskbarVisibilityReadCannotSendAfterSourceIntentChanges()
    {
        using var fixture = new RenderCloseFixture();
        var values = new List<bool>();
        fixture.Host.X11TaskbarVisibilitySetterOverride = (_, value) => { values.Add(value); return true; };
        fixture.Probe.VisibilityReadAction = () =>
        {
            fixture.Probe.VisibilityReadAction = null;
            Assert.True(fixture.Host.SetX11TaskbarVisibility(true));
        };
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        Assert.Equal(new[] { true }, values);
    }

    [Fact]
    public void X11TaskbarVisibilityReadCannotSendAfterHide()
    {
        using var fixture = new RenderCloseFixture();
        int sends = 0;
        fixture.Host.X11TaskbarVisibilitySetterOverride = (_, _) => { sends++; return true; };
        fixture.Probe.VisibilityReadAction = () =>
        {
            fixture.Probe.VisibilityReadAction = null;
            fixture.Host.Hide();
        };
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        Assert.Equal(0, sends);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void X11TaskbarSendCannotReportSuccessAfterReentrantRetirement(bool replaceCallback)
    {
        using var fixture = new RenderCloseFixture();
        fixture.Host.X11TaskbarVisibilitySetterOverride = (_, _) =>
        {
            if (replaceCallback) fixture.Host.X11TaskbarVisibilitySetterOverride = (_, _) => true;
            else SetPrivateField(fixture.Host, "_hasNativeWindowCloseStarted", true);
            return true;
        };
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        SetPrivateField(fixture.Host, "_hasNativeWindowCloseStarted", false);
    }

    [Fact]
    public void X11TaskbarForeignThreadCannotReplaceOwnedIntent()
    {
        using var fixture = new RenderCloseFixture();
        fixture.Probe.Visible = false;
        Assert.False(fixture.Host.SetX11TaskbarVisibility(false));
        Assert.False(Task.Run(() => fixture.Host.SetX11TaskbarVisibility(true)).GetAwaiter().GetResult());
        fixture.Host.X11TaskbarVisibilitySetterOverride = (_, value) => { Assert.False(value); return true; };
        fixture.Host.Show();
    }
}
