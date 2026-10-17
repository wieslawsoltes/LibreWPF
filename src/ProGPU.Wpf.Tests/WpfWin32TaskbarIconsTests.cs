using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfWin32TaskbarIconsTests
{
    [Fact]
    public void ReplacementAndClearReleaseCopiesWithoutDestroyingProviderHandles()
    {
        var native = new Native();
        var icons = new WpfWin32TaskbarIcons(native);
        icons.Synchronize(700);
        Assert.Equal(new(101, 102), native.Target);
        Assert.Empty(native.Destroyed);
        // Provider may reuse the same numeric source handles for new icon data.
        icons.Synchronize(700);
        Assert.Equal(new(103, 104), native.Target);
        Assert.Equal(new nint[] { 101, 102 }, native.Destroyed);
        native.Source = default;
        icons.Synchronize(700);
        Assert.Equal(default, native.Target);
        Assert.Equal(new nint[] { 101, 102, 103, 104 }, native.Destroyed);
        icons.ReleaseAfterWindows();
        Assert.DoesNotContain((nint)11, native.Destroyed);
        Assert.DoesNotContain((nint)12, native.Destroyed);
    }

    [Fact]
    public void SecondCopyFailureReleasesOnlyUnpublishedCandidate()
    {
        var native = new Native();
        var icons = new WpfWin32TaskbarIcons(native);
        icons.Synchronize(700);
        native.FailCopy = 12;
        Assert.Same(native.Failure, Assert.Throws<InvalidOperationException>(() => icons.Synchronize(700)));
        Assert.Equal(new(101, 102), native.Target);
        Assert.Equal(new nint[] { 103 }, native.Destroyed);
    }

    [Fact]
    public void PartialPublicationRestoresPairBeforeReleasingCandidate()
    {
        var native = new Native();
        var icons = new WpfWin32TaskbarIcons(native);
        icons.Synchronize(700);
        native.FailWrites = 1;
        Assert.Same(native.Failure, Assert.Throws<InvalidOperationException>(() => icons.Synchronize(700)));
        Assert.Equal(new(101, 102), native.Target);
        Assert.Equal(new nint[] { 103, 104 }, native.Destroyed);
    }

    [Fact]
    public void FailedRollbackRetainsActuallyBoundMixedPair()
    {
        var native = new Native();
        var icons = new WpfWin32TaskbarIcons(native);
        icons.Synchronize(700);
        native.FailWrites = 2;
        native.FailBeforeRollback = true;
        Assert.Throws<AggregateException>(() => icons.Synchronize(700));
        Assert.Equal(new(103, 102), native.Target);
        Assert.Equal(new nint[] { 101, 104 }, native.Destroyed);
        native.Target = default; // Both native windows have been destroyed.
        icons.ReleaseAfterWindows();
        Assert.Equal(4, native.Destroyed.Count);
    }

    [Fact]
    public void FailedReleaseKeepsCopiesForRetryWithoutRepeatingSuccessfulDestruction()
    {
        var native = new Native();
        var icons = new WpfWin32TaskbarIcons(native);
        icons.Synchronize(700);
        native.Target = default;
        native.FailDestroy = 102;
        Assert.Same(native.Failure, Assert.Throws<InvalidOperationException>(icons.ReleaseAfterWindows));
        Assert.Equal(new nint[] { 101 }, native.Destroyed);
        icons.ReleaseAfterWindows();
        icons.ReleaseAfterWindows();
        Assert.Equal(new nint[] { 101, 102 }, native.Destroyed);
    }

    private sealed class Native : IWpfWin32TaskbarIconOperations
    {
        public WpfWin32IconPair Source = new(11, 12), Target;
        public nint FailCopy, FailDestroy;
        public int FailWrites;
        public bool FailBeforeRollback;
        public List<nint> Destroyed = [];
        public readonly InvalidOperationException Failure = new("Original icon failure");
        private int _next = 100;
        public WpfWin32IconPair ReadSourceIcons() => Source;
        public WpfWin32IconPair ReadTargetIcons(nint owner) { Assert.Equal(700, owner); return Target; }
        public void WriteTargetIcons(nint owner, WpfWin32IconPair pair)
        {
            Assert.Equal(700, owner);
            if (FailBeforeRollback && FailWrites == 1) { FailWrites--; throw Failure; }
            Target = Target with { Large = pair.Large };
            if (FailWrites > 0) { FailWrites--; throw Failure; }
            Target = pair;
        }
        public nint CopyIcon(nint icon)
        {
            if (icon == FailCopy) throw Failure;
            Assert.True(icon is 11 or 12);
            return ++_next;
        }
        public void DestroyIcon(nint icon)
        {
            Assert.NotEqual(Target.Large, icon);
            Assert.NotEqual(Target.Small, icon);
            Assert.DoesNotContain(icon, Destroyed);
            if (icon == FailDestroy) { FailDestroy = 0; throw Failure; }
            Destroyed.Add(icon);
        }
    }
}
