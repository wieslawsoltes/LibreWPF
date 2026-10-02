using System.IO;
using System.Reflection;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void SourceGateAloneCannotShowPopupWithoutItsRealAttachedInputContext()
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        host.BindOwnedPopupInputGate(window, _ => true);
        host.BindModalInputOwner(new object());
        int nativeShows = 0;
        Assert.Throws<PlatformNotSupportedException>(() => host.ShowWithoutActivation(_ =>
        {
            nativeShows++;
            return true;
        }));
        Assert.Equal(0, nativeShows);
        Assert.Equal(0, probe.VisibleWrites);
        Assert.Null(ReadRetirementField(host, "_ownedPopupInputContext"));
    }

    [Fact]
    public void OwnedInputCapabilityFailureCannotReplaceEarlierSourceGateFailure()
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        var expected = new InvalidOperationException("source native gate failed");
        bool fail = false;
        host.BindOwnedPopupInputGate(window, _ => fail ? throw expected : true);
        host.BindModalInputOwner(new object());
        fail = true;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => host.ShowWithoutActivation(_ =>
            throw new InvalidOperationException("No native show may execute."))));
        Assert.Equal(0, probe.VisibleWrites);
    }

    [Fact]
    public void RealSourceRouteRequiresExactProviderProofAfterAttachmentAndBeforeEveryShow()
    {
        string source = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));
        int attach = source.IndexOf("private void AttachInputService()", StringComparison.Ordinal);
        int detach = source.IndexOf("private void DetachInputService()", attach, StringComparison.Ordinal);
        string attachBody = source[attach..detach];
        Assert.True(attachBody.IndexOf("input.Attach(window)", StringComparison.Ordinal)
            < attachBody.IndexOf("RequireOwnedPopupInputContext(gate, input)", StringComparison.Ordinal));
        Assert.True(attachBody.IndexOf("RequireOwnedPopupInputContext(gate, input)", StringComparison.Ordinal)
            < attachBody.IndexOf("_inputSubscription = inputSubscription", StringComparison.Ordinal));
        Assert.Contains("_ownedPopupInputContext = ownedContext", attachBody);
        Assert.Contains("NativePopupWindow.SupportsModalInput(gate.Window, context)", source);
        Assert.Contains("ReferenceEquals(context, _ownedPopupInputContext)", source);
        Assert.Contains("ReferenceEquals(subscription, _inputSubscription)", source);
        string detachBody = source[detach..source.IndexOf("private void OnPlatformInputReceived", detach, StringComparison.Ordinal)];
        Assert.True(detachBody.IndexOf("_ownedPopupInputContext = null", StringComparison.Ordinal)
            < detachBody.IndexOf("_inputSubscription?.Dispose()", StringComparison.Ordinal));
        Assert.Contains("if (!_options.EnableNativeModalSessions) return;", source);
    }
}
