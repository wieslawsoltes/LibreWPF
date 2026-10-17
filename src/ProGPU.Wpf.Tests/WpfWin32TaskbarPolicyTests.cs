using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfWin32TaskbarPolicyTests
{
    [Fact]
    public void OriginalNineOwnerTransitionsPreserveChromeAndReuseHiddenOwner()
    {
        var native = new Native();
        var policy = new WpfWin32TaskbarPolicy(native);
        // Original Microsoft WPF taskbar reference, run39028587600. These are
        // policy controls; only paired native application execution is parity.
        (bool Show, nint SourceOwner, nint ExpectedOwner)[] transitions =
        [ (true, 0, 0), (false, 0, 700), (true, 700, 0),
          (false, 101, 101), (true, 101, 101), (false, 202, 202),
          (false, 0, 700), (true, 700, 0), (false, 0, 700) ];
        foreach (var (show, sourceOwner, expectedOwner) in transitions)
        {
            native.State = native.State with { Owner = sourceOwner };
            policy.Apply(show);
            Assert.Equal(new(expectedOwner, show ? 0x40100 : 0x100, true), native.State);
        }
        Assert.Equal(1, native.Created);
        Assert.Equal(0, native.Destroyed);
        Assert.Throws<InvalidOperationException>(policy.ReleaseAfterNativeWindow);
        Assert.Equal(0, native.Destroyed);
        native.ChildDestroyed = true;
        policy.ReleaseAfterNativeWindow();
        policy.ReleaseAfterNativeWindow();
        Assert.Equal(1, native.Destroyed);
        Assert.Throws<ObjectDisposedException>(() => policy.Apply(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialOwnedFalseCreatesDependencyAndPreservesEveryOtherStyleBit(bool visible)
    {
        var native = new Native { State = new(101, 0x10000080, visible) };
        var policy = new WpfWin32TaskbarPolicy(native);
        policy.Apply(false);
        Assert.Equal(1, native.Created);
        Assert.Equal(new(101, 0x10000080, visible), native.State);
        Assert.Empty(native.Mutations);
        policy.Apply(true);
        Assert.Equal(new(101, 0x10040080, visible), native.State);
        Assert.Equal(visible ? new[] { "visible:False", "style", "visible:True" } : new[] { "style" }, native.Mutations);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("style")]
    [InlineData("visible:True")]
    public void PublicationFailureRestoresOriginalStateAndRetainsHiddenDependency(string operation)
    {
        var native = new Native { FailOnce = operation };
        var original = native.State;
        var policy = new WpfWin32TaskbarPolicy(native);
        Assert.Same(native.Failure, Assert.Throws<InvalidOperationException>(() => policy.Apply(false)));
        Assert.Equal(original, native.State);
        Assert.Equal(1, native.Created);
        Assert.Equal(0, native.Destroyed);
        policy.Apply(false);
        Assert.Equal(new(700, 0x100, true), native.State);
        Assert.Equal(1, native.Created);
    }

    [Fact]
    public void FailedDestructionRetainsDependencyForCreatingThreadRetry()
    {
        var native = new Native();
        var policy = new WpfWin32TaskbarPolicy(native);
        policy.Apply(false);
        native.ChildDestroyed = true;
        native.FailOnce = "destroy";
        Assert.Same(native.Failure, Assert.Throws<InvalidOperationException>(policy.ReleaseAfterNativeWindow));
        Exception? wrongThread = null;
        var thread = new Thread(() => wrongThread = Record.Exception(policy.ReleaseAfterNativeWindow));
        thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Equal(0, native.Destroyed);
        policy.ReleaseAfterNativeWindow();
        Assert.Equal(1, native.Destroyed);
    }

    [Fact]
    public void ForeignThreadAndReentrantPublicationCannotMutateNativeState()
    {
        var native = new Native();
        var policy = new WpfWin32TaskbarPolicy(native);
        Exception? wrongThread = null;
        var thread = new Thread(() => wrongThread = Record.Exception(() => policy.Apply(false)));
        thread.Start(); thread.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Equal(0, native.Created);
        native.OnMutation = () =>
        {
            Assert.Throws<InvalidOperationException>(() => policy.Apply(true));
            Assert.Throws<InvalidOperationException>(policy.ReleaseAfterNativeWindow);
        };
        policy.Apply(false);
        Assert.Equal(new(700, 0x100, true), native.State);
    }

    private sealed class Native : IWpfWin32TaskbarOperations
    {
        public WpfWin32TaskbarSnapshot State = new(0, 0x40100, true);
        public int Created, Destroyed;
        public bool ChildDestroyed;
        public string? FailOnce;
        public Action? OnMutation;
        public readonly InvalidOperationException Failure = new("Original native failure");
        public readonly List<string> Mutations = [];
        public WpfWin32TaskbarSnapshot Read() => State;
        public nint CreateHiddenOwner() { Created++; return 700; }
        public void ValidateHiddenOwner(nint owner) { Assert.Equal(700, owner); Assert.Equal(0, Destroyed); }
        private void Mutate(string name)
        {
            Mutations.Add(name);
            OnMutation?.Invoke();
            if (FailOnce == name) { FailOnce = null; throw Failure; }
        }
        public void SetOwner(nint owner) { Mutate("owner"); State = State with { Owner = owner }; }
        public void SetExtendedStyle(long style) { Mutate("style"); State = State with { ExtendedStyle = style }; }
        public void SetVisibleWithoutActivation(bool visible) { Mutate("visible:" + visible); State = State with { Visible = visible }; }
        public void RequireChildDestroyed()
        {
            if (!ChildDestroyed) throw new InvalidOperationException("Child remains live");
        }
        public void DestroyHiddenOwner(nint owner) { ValidateHiddenOwner(owner); Mutate("destroy"); Destroyed++; }
    }
}
