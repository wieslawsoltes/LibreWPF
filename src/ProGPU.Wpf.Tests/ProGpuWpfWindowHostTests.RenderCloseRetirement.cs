using System.IO;
using System.Reflection;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void AcceptedCloseRetainsTargetUntilActualRenderCallbackUnwinds()
    {
        using var fixture = new RenderCloseFixture();
        bool scopeReleased = false;
        fixture.Dispatcher.Post(() =>
        {
            using var scope = new RetirementCallback(() =>
            {
                Assert.Same(fixture.Target, fixture.Host.CompositionTarget);
                scopeReleased = true;
            });
            fixture.Host.Close();
            Assert.Equal(true, ReadRetirementField(fixture.Host, "_isRendering"));
            Assert.Same(fixture.Target, ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
            Assert.Same(fixture.Target, fixture.Host.CompositionTarget);
            Assert.False(fixture.CanContinue());
        });
        fixture.Render();
        Assert.True(scopeReleased);
        Assert.Null(fixture.Host.CompositionTarget);
        Assert.Null(ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
        Assert.Same(fixture.Window, fixture.Host.SilkWindow);
        Assert.Equal(0, fixture.Probe.Disposals);
        Assert.Equal(1, fixture.Probe.Closes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelOrHideShowDuringRenderDoesNotRetireTarget(bool cancelClose)
    {
        using var fixture = new RenderCloseFixture();
        bool callbackReached = false;
        fixture.Host.Closing += (_, args) => args.Cancel = true;
        fixture.Dispatcher.Post(() =>
        {
            callbackReached = true;
            Assert.Equal(true, ReadRetirementField(fixture.Host, "_isRendering"));
            if (cancelClose) fixture.Host.Close();
            else fixture.Host.Hide();
            fixture.Host.Show();
            Assert.Null(ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
            Assert.True(fixture.CanContinue());
        });
        fixture.Render();
        Assert.True(callbackReached);
        Assert.Equal(cancelClose ? 1 : 0, fixture.Probe.Closes);
        Assert.Same(fixture.Target, fixture.Host.CompositionTarget);
        Assert.Equal(0, fixture.Probe.Disposals);
        Assert.False(fixture.Probe.Closing);
        Assert.True(fixture.Probe.Visible);
    }

    [Fact]
    public void AcceptedTargetCannotBeRevivedByNestedShowAndCanceledLaterClose()
    {
        using var fixture = new RenderCloseFixture();
        bool cancel = false;
        fixture.Host.Closing += (_, args) => args.Cancel = cancel;
        fixture.Dispatcher.Post(() =>
        {
            fixture.Host.Close();
            fixture.Host.Show();
            // Independently exercise the accepted-target lease, not merely the
            // mutable close flag: a later provider/source generation may reopen.
            SetPrivateField(fixture.Host, "_hasNativeWindowCloseStarted", false);
            cancel = true;
            fixture.Host.Close();
            Assert.Equal(false, ReadRetirementField(fixture.Host, "_hasNativeWindowCloseStarted"));
            Assert.False(fixture.CanContinue());
            Assert.Same(fixture.Target, ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
        });
        fixture.Render();
        Assert.Null(fixture.Host.CompositionTarget);
        Assert.Equal(0, fixture.Probe.Disposals);
        Assert.Equal(2, fixture.Probe.Closes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCloseCleanupRetainsOwnerAndPreservesFrameFailure(bool failFrame)
    {
        using var fixture = new RenderCloseFixture();
        var frameFailure = new RetirementFailureWithInaccessibleData();
        var cleanupFailure = new InvalidOperationException("scheduler reset");
        bool fail = true;
        int resets = 0;
        fixture.Host.WpfRenderScheduler = new RenderCloseScheduler(() =>
        {
            resets++;
            // Target.Dispose has returned, but cleanup is not complete. Neither
            // Show nor a fresh render/load attempt may reuse this target.
            SetPrivateField(fixture.Host, "_hasNativeWindowCloseStarted", false);
            fixture.Host.Show();
            Assert.False(InvokeHostMethod<Func<bool>>(fixture.Host, "EnsureCompositionTargetLoaded")());
            fixture.Render();
            Assert.Same(fixture.Target, fixture.Host.CompositionTarget);
            if (fail) throw cleanupFailure;
        });
        fixture.Dispatcher.Post(() =>
        {
            fixture.Host.Close();
            if (failFrame) throw frameFailure;
        });
        try
        {
            Exception? actual = Record.Exception(fixture.Render);
            Assert.Same(failFrame ? frameFailure : cleanupFailure, actual);
            Assert.Same(fixture.Target, fixture.Host.CompositionTarget);
            Assert.Same(fixture.Target, ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
            Assert.Equal(1, resets);
            Assert.Equal(0, fixture.Probe.Disposals);
        }
        finally { fail = false; }
        DrainRetirements();
        Assert.Equal(2, resets);
        Assert.Null(fixture.Host.CompositionTarget);
        Assert.Null(ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
        Assert.Equal(0, fixture.Probe.Disposals);
    }

    [Fact]
    public void CloseCleanupReentrantHostDisposalKeepsNativeRetirementOwner()
    {
        using var fixture = new RenderCloseFixture();
        int resets = 0;
        fixture.Host.WpfRenderScheduler = new RenderCloseScheduler(() =>
        {
            resets++;
            fixture.Host.Dispose();
            Assert.Equal(0, fixture.Probe.Disposals);
            Assert.Same(fixture.Target, fixture.Host.CompositionTarget);
        });
        fixture.Dispatcher.Post(fixture.Host.Close);
        fixture.Render();
        Assert.Equal(1, resets);
        Assert.Null(fixture.Host.CompositionTarget);
        Assert.Equal(0, fixture.Probe.Disposals);
        DrainRetirements();
        Assert.Equal(1, fixture.Probe.Disposals);
        Assert.Null(fixture.Host.SilkWindow);
    }

    [Fact]
    public void ClosedFrameCannotDisposeAReplacementTarget()
    {
        using var fixture = new RenderCloseFixture();
        using var replacement = ProGpuWpfCompositionTarget.CreateHeadless();
        fixture.Dispatcher.Post(() =>
        {
            fixture.Host.Close();
            SetPrivateField(fixture.Host, "_target", replacement);
            Assert.False(fixture.CanContinue());
        });
        try
        {
            var failure = Assert.Throws<InvalidOperationException>(fixture.Render);
            Assert.Contains("replacement target", failure.Message);
            Assert.Same(replacement, fixture.Host.CompositionTarget);
            Assert.Same(fixture.Target, ReadRetirementField(fixture.Host, "_acceptedCloseTarget"));
            Assert.NotNull(replacement.BeginDrawingFrame(1, 1));
            Assert.NotNull(fixture.Target.BeginDrawingFrame(1, 1));
        }
        finally { SetPrivateField(fixture.Host, "_target", fixture.Target); }
        DrainRetirements();
        Assert.Null(fixture.Host.CompositionTarget);
        Assert.NotNull(replacement.BeginDrawingFrame(1, 1));
    }

    [Fact]
    public void RenderCloseBoundaryRemainsOutsideDrawingAndTextureScopes()
    {
        string source = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));
        int start = source.IndexOf("private void OnRender(double deltaSeconds)", StringComparison.Ordinal);
        int end = source.IndexOf("private bool CanContinueRenderFrame", start, StringComparison.Ordinal);
        string render = source[start..end];
        Assert.Contains("Draw.Invoke(drawingContext, drawArgs);\n                    if (!CanContinueRenderFrame", render);
        Assert.Contains("if (!CanContinueRenderFrame(frameTarget, frameWindow)) return;\n            if (Present(", render);
        Assert.Contains("finally\n        {\n            _isRendering = false;\n            try { DisposeAcceptedCloseTargetIfNeeded(); }", render);
        Assert.Contains("catch (Exception) when (renderFailure != null)", render);
        Assert.True(render.IndexOf("if (Present(", StringComparison.Ordinal) <
            render.IndexOf("try { DisposeAcceptedCloseTargetIfNeeded(); }", StringComparison.Ordinal));
        Assert.Contains("_target.Context.Wgpu.TextureViewRelease(targetView);", source);
        Assert.Contains("_target.Context.Wgpu.TextureRelease(surfaceTexture.Texture);", source);
    }

    private static T InvokeHostMethod<T>(ProGpuWpfWindowHost host, string name) where T : Delegate =>
        typeof(ProGpuWpfWindowHost).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<T>(host);

    private sealed class RenderCloseFixture : IDisposable
    {
        internal readonly TestDispatcherService Dispatcher = new(false);
        internal readonly ProGpuWpfWindowHost Host;
        internal readonly ProGpuWpfCompositionTarget Target;
        internal readonly IWindow Window;
        internal readonly RenderCloseWindowProbe Probe;

        internal RenderCloseFixture()
        {
            Host = new ProGpuWpfWindowHost
            {
                PlatformServices = CreatePlatformServices(Dispatcher),
                WpfRenderScheduler = new TestRenderScheduler()
            };
            Target = ProGpuWpfCompositionTarget.CreateHeadless();
            Window = DispatchProxy.Create<IWindow, RenderCloseWindowProbe>();
            Probe = (RenderCloseWindowProbe)(object)Window;
            Probe.CloseAction = InvokeHostMethod<Action>(Host, "OnClosing");
            Probe.RenderAction = Render;
            SetPrivateField(Host, "_window", Window);
            SetPrivateField(Host, "_nativeWindowThreadId", Environment.CurrentManagedThreadId);
            SetPrivateField(Host, "_target", Target);
        }

        internal void Render() => InvokeHostMethod<Action<double>>(Host, "OnRender")(0d);
        internal bool CanContinue() => InvokeHostMethod<Func<ProGpuWpfCompositionTarget, IWindow?, bool>>(
            Host, "CanContinueRenderFrame")(Target, Window);

        public void Dispose()
        {
            Host.Dispose();
            Target.Dispose();
        }
    }

    private sealed class RenderCloseScheduler(Action reset) : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested { add { } remove { } }
        public bool HasPendingRenderRequest => false;
        public void RequestRender() { }
        public bool ConsumeRenderRequest() => false;
        public void Reset() => reset();
    }

    public class RenderCloseWindowProbe : DispatchProxy
    {
        internal Action? CloseAction, RenderAction, VisibilityReadAction;
        internal Exception? VisibilityWriteFailure;
        internal bool Closing, Visible = true, HideBeforeClose, RejectVisibilityAfterClose;
        internal int Closes, Disposals, Renders, VisibilityWrites;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "get_IsInitialized": return true;
                case "get_IsClosing": return Closing;
                case "set_IsClosing": Closing = (bool)args![0]!; return null;
                case "set_IsVisible":
                    if (RejectVisibilityAfterClose && Closing)
                        throw new InvalidOperationException("The closing owned popup rejects visibility setters.");
                    VisibilityWrites++;
                    if (VisibilityWriteFailure != null) throw VisibilityWriteFailure;
                    Visible = (bool)args![0]!;
                    return null;
                case "get_IsVisible": VisibilityReadAction?.Invoke(); return Visible;
                case "ContinueEvents": return null;
                case "DoRender": Renders++; RenderAction?.Invoke(); return null;
                case "Close":
                    Closing = true;
                    if (HideBeforeClose) Visible = false;
                    Closes++;
                    CloseAction?.Invoke();
                    return null;
                case "Dispose": Disposals++; return null;
            }
            if (method.Name.StartsWith("remove_", StringComparison.Ordinal)) return null;
            throw new InvalidOperationException("Unexpected provider access: " + method.Name);
        }
    }
}
