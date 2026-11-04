using System.Reflection;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingCallbackCanOwnConfirmationBeforeItsCloseDecision(bool cancel)
    {
        using var owner = new ProGpuWpfWindowHost();
        using var child = new ProGpuWpfWindowHost();
        int assignments = 0, callbacks = 0;
        child.NativeOwnerSetterOverride = candidate =>
        {
            Assert.Same(owner, candidate);
            assignments++;
            return true;
        };
        owner.Closing += (_, args) =>
        {
            callbacks++;
            Assert.True(child.TrySetNativeOwner(owner));
            args.Cancel = cancel;
        };

        InvokeOwnerClosing(owner);

        Assert.Equal(1, callbacks);
        Assert.Equal(cancel, child.TrySetNativeOwner(owner));
        Assert.Equal(cancel ? 2 : 1, assignments);
    }

    [Fact]
    public void ClosingOwnerDoesNotTurnNativeRefusalIntoManagedSuccess()
    {
        using var owner = new ProGpuWpfWindowHost();
        using var child = new ProGpuWpfWindowHost();
        int assignments = 0;
        child.NativeOwnerSetterOverride = candidate =>
        {
            Assert.Same(owner, candidate);
            assignments++;
            return false;
        };
        owner.Closing += (_, _) => Assert.False(child.TrySetNativeOwner(owner));

        InvokeOwnerClosing(owner);

        Assert.Equal(1, assignments);
    }

    [Fact]
    public void DisposedOwnerIsRejectedEvenInsideItsClosingCallback()
    {
        using var owner = new ProGpuWpfWindowHost();
        using var child = new ProGpuWpfWindowHost();
        child.NativeOwnerSetterOverride = _ => throw new InvalidOperationException("Retired owner reached native setter.");
        owner.Closing += (_, _) =>
        {
            owner.Dispose();
            Assert.False(child.TrySetNativeOwner(owner));
        };

        InvokeOwnerClosing(owner);
    }

    [Fact]
    public void ClosingChildCannotAssignOrClearItsOwner()
    {
        using var owner = new ProGpuWpfWindowHost();
        using var child = new ProGpuWpfWindowHost();
        child.NativeOwnerSetterOverride = _ => throw new InvalidOperationException("Closing child reached native setter.");
        child.Closing += (_, _) =>
        {
            Assert.False(child.TrySetNativeOwner(owner));
            Assert.False(child.TrySetNativeOwner(null));
        };

        InvokeOwnerClosing(child);
    }

    private static void InvokeOwnerClosing(ProGpuWpfWindowHost host) =>
        typeof(ProGpuWpfWindowHost).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(host)();
}
