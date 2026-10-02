using System.IO;
using System.Reflection;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void FailedUnpublishedInputCleanupRetainsExactRetryOwner()
    {
        using var host = new ProGpuWpfWindowHost();
        var expected = new InvalidOperationException("input cleanup pending");
        var subscription = new PendingInputSubscription { Failure = expected };
        SetPrivateField(host, "_unpublishedInputSubscription", subscription);
        var retire = typeof(ProGpuWpfWindowHost)
            .GetMethod("RetireUnpublishedInputSubscription", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(host);
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(retire));
        Assert.Same(subscription, ReadRetirementField(host, "_unpublishedInputSubscription"));
        Assert.Equal(1, subscription.Attempts);
        subscription.Failure = null;
        retire();
        Assert.Null(ReadRetirementField(host, "_unpublishedInputSubscription"));
        Assert.Equal(2, subscription.Attempts);
        retire();
        Assert.Equal(2, subscription.Attempts);
    }

    [Fact]
    public void ReentrantUnpublishedInputCleanupDoesNotRepeatActiveAttempt()
    {
        using var host = new ProGpuWpfWindowHost();
        var subscription = new PendingInputSubscription();
        SetPrivateField(host, "_unpublishedInputSubscription", subscription);
        var retire = typeof(ProGpuWpfWindowHost)
            .GetMethod("RetireUnpublishedInputSubscription", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(host);
        subscription.OnDispose = () => Assert.Throws<InvalidOperationException>(retire);
        retire();
        Assert.Null(ReadRetirementField(host, "_unpublishedInputSubscription"));
        Assert.Equal(1, subscription.Attempts);
    }

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

    private sealed class PendingInputSubscription : IDisposable
    {
        internal int Attempts;
        internal Exception? Failure;
        internal Action? OnDispose;
        public void Dispose()
        {
            Attempts++;
            OnDispose?.Invoke();
            if (Failure != null) throw Failure;
        }
    }
}
