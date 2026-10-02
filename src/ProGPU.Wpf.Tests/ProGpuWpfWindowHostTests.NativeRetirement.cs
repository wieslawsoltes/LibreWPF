using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Fact]
    public void NativeRetirementKeepsExactWindowUntilProviderCompletes()
    {
        var (window, _) = CreateRetirementWindow();
        int resources = 0, attempts = 0;
        var retirement = new WpfNativeWindowRetirement(window, Environment.CurrentManagedThreadId,
            () => resources++, actual =>
            {
                Assert.Same(window, actual);
                Assert.Equal(1, resources);
                return ++attempts == 3;
            });
        Assert.False(retirement.TryComplete());
        Assert.False(retirement.TryComplete());
        Assert.Same(window, retirement.Window);
        Assert.False(retirement.IsComplete);
        Assert.True(retirement.TryComplete());
        Assert.True(retirement.TryComplete());
        Assert.Equal(1, resources);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void NativeRetirementDefaultDisposesForeignProviderAfterResourcesOnce()
    {
        var (window, probe) = CreateRetirementWindow();
        var order = new List<string>();
        probe.DisposeAction = () => order.Add("native");
        var retirement = new WpfNativeWindowRetirement(window, Environment.CurrentManagedThreadId,
            () => order.Add("resources"));
        Assert.True(retirement.TryComplete());
        Assert.True(retirement.TryComplete());
        Assert.Equal(new[] { "resources", "native" }, order);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeRetirementFailureKeepsItsOriginalCleanupOwner(bool resourceFailure)
    {
        var (window, _) = CreateRetirementWindow();
        var failure = new InvalidOperationException("retirement failure");
        bool fail = true;
        int resources = 0, native = 0;
        var retirement = new WpfNativeWindowRetirement(window, Environment.CurrentManagedThreadId, () =>
        {
            resources++;
            if (fail && resourceFailure) throw failure;
        }, actual =>
        {
            Assert.Same(window, actual);
            native++;
            if (fail) throw failure;
            return true;
        });
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => retirement.TryComplete()));
        Assert.False(retirement.IsComplete);
        Assert.Equal(resourceFailure ? 0 : 1, native);
        fail = false;
        Assert.True(retirement.TryComplete());
        Assert.Equal(resourceFailure ? 2 : 1, resources);
        Assert.Equal(resourceFailure ? 1 : 2, native);
    }

    [Fact]
    public void NativeRetirementRejectsReentryAndWrongThreadBeforeCallbacks()
    {
        var (window, _) = CreateRetirementWindow();
        int resources = 0, native = 0;
        WpfNativeWindowRetirement? retirement = null;
        retirement = new(window, Environment.CurrentManagedThreadId, () =>
        {
            resources++;
            Assert.False(retirement!.TryComplete());
        }, _ =>
        {
            native++;
            Assert.False(retirement!.TryComplete());
            return true;
        });
        Exception? wrongThread = null;
        var thread = new Thread(() => wrongThread = Record.Exception(() => retirement.TryComplete()));
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Equal(0, resources);
        Assert.Equal(0, native);
        Assert.True(retirement.TryComplete());
        Assert.Equal(1, resources);
        Assert.Equal(1, native);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WindowlessHostCleanupCanReenterAndRetryWithoutLosingRegistration(bool failFirst, bool resourceStage)
    {
        using var host = new ProGpuWpfWindowHost();
        int releases = 0;
        var failure = new InvalidOperationException("source registration cleanup");
        var registration = new RetirementCallback(() =>
        {
            releases++;
            host.Dispose();
            if (failFirst && releases == 1) throw failure;
        });
        if (resourceStage)
        {
            host.WpfRenderScheduler = new RetirementScheduler(registration.Dispose);
            SetPrivateField(host, "_ownsRenderScheduler", true);
        }
        else SetPrivateField(host, "_modalInputRegistration", registration);
        if (failFirst)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(host.Dispose));
            if (resourceStage) Assert.Equal(true, ReadRetirementField(host, "_ownsRenderScheduler"));
            else Assert.Same(registration, ReadRetirementField(host, "_modalInputRegistration"));
        }
        host.Dispose();
        host.Dispose();
        Assert.Equal(failFirst ? 2 : 1, releases);
        Assert.Null(ReadRetirementField(host, "_modalInputRegistration"));
        Assert.Equal(false, ReadRetirementField(host, "_ownsRenderScheduler"));
    }

    [Theory]
    [InlineData("_isRendering")]
    [InlineData("_isInNativeWindowCloseCallback")]
    public void HostRetirementWaitsForItsActiveCallback(string activeField)
    {
        int resources = 0, native = 0;
        using var host = CreateRetiringHost(() => resources++, _ => { native++; return true; }, out var probe);
        SetPrivateField(host, activeField, true);
        try
        {
            InvokeRetirement(host);
            Assert.Equal(0, probe.EventRemovals);
            Assert.Equal(0, resources);
            Assert.Equal(0, native);
            Assert.NotNull(ReadRetirementField(host, "_window"));
        }
        finally { SetPrivateField(host, activeField, false); }
        InvokeRetirement(host);
        Assert.Equal(6, probe.EventRemovals);
        Assert.Equal(1, resources);
        Assert.Equal(1, native);
        Assert.Null(ReadRetirementField(host, "_window"));
    }

    [Fact]
    public void HostRetirementRejectsWrongThreadBeforeUnsubscriptionAndDpiRelease()
    {
        int resources = 0, native = 0, dpi = 0;
        using var host = CreateRetiringHost(() => resources++, _ => { native++; return true; }, out var probe);
        SetPrivateField(host, "_nativeDpiSubscription", new RetirementCallback(() => dpi++));
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(() => InvokeRetirement(host)));
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, probe.EventRemovals);
        Assert.Equal(0, dpi);
        Assert.Equal(0, resources);
        Assert.Equal(0, native);
        InvokeRetirement(host);
        Assert.Equal(1, dpi);
        Assert.Equal(1, resources);
        Assert.Equal(1, native);
    }

    [Fact]
    public void HostRetirementUnsubscriptionReentryCannotRepeatProviderRelease()
    {
        int resources = 0, native = 0;
        using var host = CreateRetiringHost(() => resources++, _ => { native++; return true; }, out var probe);
        probe.RemoveAction = host.Dispose;
        InvokeRetirement(host);
        Assert.Equal(6, probe.EventRemovals);
        Assert.Equal(1, resources);
        Assert.Equal(1, native);
        Assert.Null(ReadRetirementField(host, "_window"));
    }

    [Fact]
    public void ClosingCallbackDisposalKeepsRendererUntilDeferredHostRetires()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        using var host = new ProGpuWpfWindowHost();
        var (window, probe) = CreateRetirementWindow();
        SetPrivateField(host, "_window", window);
        SetPrivateField(host, "_nativeWindowThreadId", Environment.CurrentManagedThreadId);
        SetPrivateField(host, "_target", target);
        int closing = 0, nativeDisposals = 0;
        probe.DisposeAction = () =>
        {
            Assert.Null(ReadRetirementField(host, "_target"));
            nativeDisposals++;
        };
        host.Closing += (_, _) =>
        {
            closing++;
            host.Dispose();
            Assert.Same(target, ReadRetirementField(host, "_target"));
            Assert.Equal(0, nativeDisposals);
        };
        typeof(ProGpuWpfWindowHost).GetMethod("OnClosing",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action>(host)();
        Assert.Equal(1, closing);
        Assert.Same(target, ReadRetirementField(host, "_target"));
        Assert.Same(window, ReadRetirementField(host, "_window"));
        Assert.Equal(0, nativeDisposals);
        DrainRetirements();
        Assert.Null(ReadRetirementField(host, "_target"));
        Assert.Null(ReadRetirementField(host, "_window"));
        Assert.Equal(1, nativeDisposals);
    }

    [Fact]
    public void PreEventRenderRetirementStopsBeforeAnyFurtherProviderRead()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        RetirementWindowProbe? probe = null;
        using var host = CreateRetiringHost(target.Dispose, _ =>
        {
            probe!.Released = true;
            return true;
        }, out var identity);
        probe = identity;
        SetPrivateField(host, "_target", target);
        SetPrivateField(host, "_isDisposed", false);
        probe.RenderAction = () =>
        {
            // Model the source close acknowledged during this provider's render
            // callback; the real host drain still owns resource/native release.
            SetPrivateField(host, "_isDisposed", true);
            QueueRetirement(host);
        };
        host.DoEvents();
        Assert.True(probe.Released);
        Assert.Equal(1, probe.RenderCalls);
        Assert.Equal(0, probe.EventDrivenReads);
        Assert.Null(ReadRetirementField(host, "_window"));
    }

    [Fact]
    public void HostDrainRetainsFailedAndDeferredWindowsWhileCompletingPeers()
    {
        bool fail = true;
        int failedAttempts = 0, completedAttempts = 0, deferredAttempts = 0;
        var failure = new RetirementFailureWithInaccessibleData();
        var secondFailure = new RetirementFailureWithInaccessibleData();
        Exception? firstObservedFailure = null;
        using var failed = CreateRetiringHost(() => { }, _ =>
        {
            failedAttempts++;
            DrainRetirements(); // The existing outer drain owns this attempt.
            if (fail)
            {
                firstObservedFailure ??= failure;
                throw failure;
            }
            return true;
        }, out _);
        using var secondFailed = CreateRetiringHost(() => { }, _ =>
        {
            if (fail)
            {
                firstObservedFailure ??= secondFailure;
                throw secondFailure;
            }
            return true;
        }, out _);
        using var completed = CreateRetiringHost(() => { }, _ => { completedAttempts++; return true; }, out _);
        using var deferred = CreateRetiringHost(() => { }, _ => ++deferredAttempts > 1, out _);
        QueueRetirement(failed);
        QueueRetirement(secondFailed);
        QueueRetirement(completed);
        QueueRetirement(deferred);
        try
        {
            var actual = Assert.Throws<RetirementFailureWithInaccessibleData>(DrainRetirements);
            Assert.Same(firstObservedFailure, actual);
            Assert.Equal(1, failedAttempts);
            Assert.Equal(1, completedAttempts);
            Assert.Equal(1, deferredAttempts);
            Assert.NotNull(ReadRetirementField(failed, "_window"));
            Assert.NotNull(ReadRetirementField(secondFailed, "_window"));
            Assert.Null(ReadRetirementField(completed, "_window"));
            Assert.NotNull(ReadRetirementField(deferred, "_window"));
        }
        finally { fail = false; }
        DrainRetirements();
        Assert.Null(ReadRetirementField(failed, "_window"));
        Assert.Null(ReadRetirementField(secondFailed, "_window"));
        Assert.Null(ReadRetirementField(deferred, "_window"));
        Assert.Equal(1, completedAttempts);
    }

    [Fact]
    public void ExistingDispatchFailureIsNotReplacedByQueuedRetirementFailure()
    {
        bool fail = true;
        var original = new RetirementFailureWithInaccessibleData();
        using var host = CreateRetiringHost(() => { }, _ =>
        {
            if (fail) throw new InvalidOperationException("native retirement");
            return true;
        }, out _);
        QueueRetirement(host);
        try
        {
            var drain = typeof(ProGpuWpfWindowHost).GetMethod("ProcessDeferredNativeWindowDisposalsPreservingFailure",
                BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Action<Exception?>>();
            drain(original);
            Assert.NotNull(ReadRetirementField(host, "_window"));
        }
        finally { fail = false; }
        DrainRetirements();
        Assert.Null(ReadRetirementField(host, "_window"));
    }

    [Fact]
    public void HostRetirementKeepsRendererOwnerUntilTargetDisposalSucceeds()
    {
        string source = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));
        int start = source.IndexOf("private void DisposeTarget()", StringComparison.Ordinal);
        int end = source.IndexOf("private void ReplaceRenderScheduler", start, StringComparison.Ordinal);
        string disposal = source[start..end];
        int dispose = disposal.IndexOf("target.Dispose();", StringComparison.Ordinal);
        int release = disposal.IndexOf("_target = null;", StringComparison.Ordinal);
        Assert.True(dispose >= 0 && release > dispose);
        Assert.True(disposal.IndexOf("_directXDevice?.Dispose();", StringComparison.Ordinal) < dispose);
        Assert.DoesNotContain("s_deferredNativeWindowDisposals.Clear()", source);
        Assert.Contains("_nativeWindowThreadId = Environment.CurrentManagedThreadId;", source);
    }

    private static ProGpuWpfWindowHost CreateRetiringHost(Action resources, Func<IWindow, bool> dispose,
        out RetirementWindowProbe probe)
    {
        var (window, identity) = CreateRetirementWindow();
        probe = identity;
        var host = new ProGpuWpfWindowHost();
        SetPrivateField(host, "_window", window);
        SetPrivateField(host, "_nativeWindowThreadId", Environment.CurrentManagedThreadId);
        SetPrivateField(host, "_isDisposed", true);
        SetPrivateField(host, "_disposeNativeWindowWhenLoopExits", true);
        SetPrivateField(host, "_nativeWindowRetirement", new WpfNativeWindowRetirement(window,
            Environment.CurrentManagedThreadId, resources, dispose));
        return host;
    }

    private static object? ReadRetirementField(ProGpuWpfWindowHost host, string name) =>
        typeof(ProGpuWpfWindowHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host);

    private static void InvokeRetirement(ProGpuWpfWindowHost host) =>
        typeof(ProGpuWpfWindowHost).GetMethod("DisposeDeferredNativeWindowIfNeeded",
            BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action>(host)();

    private static void QueueRetirement(ProGpuWpfWindowHost host) =>
        typeof(ProGpuWpfWindowHost).GetMethod("QueueDeferredNativeWindowDisposal",
            BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Action<ProGpuWpfWindowHost>>()(host);

    private static void DrainRetirements() =>
        typeof(ProGpuWpfWindowHost).GetMethod("ProcessDeferredNativeWindowDisposals",
            BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Action>()();

    private static (IWindow Window, RetirementWindowProbe Probe) CreateRetirementWindow()
    {
        var window = DispatchProxy.Create<IWindow, RetirementWindowProbe>();
        return (window, (RetirementWindowProbe)(object)window);
    }

    private sealed class RetirementCallback(Action callback) : IDisposable
    {
        public void Dispose() => callback();
    }

    private sealed class RetirementScheduler(Action dispose) : IWpfRenderScheduler, IDisposable
    {
        public event EventHandler? RenderRequested { add { } remove { } }
        public bool HasPendingRenderRequest => false;
        public void RequestRender() { }
        public bool ConsumeRenderRequest() => false;
        public void Reset() { }
        public void Dispose() => dispose();
    }

    private sealed class RetirementFailureWithInaccessibleData : Exception
    {
        public override IDictionary Data => throw new InvalidOperationException("Do not annotate callback exceptions.");
    }

    public class RetirementWindowProbe : DispatchProxy
    {
        internal Action? DisposeAction, RemoveAction, RenderAction;
        internal int EventRemovals, RenderCalls, EventDrivenReads, Accesses;
        internal bool Released;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Accesses++;
            if (Released) throw new InvalidOperationException("The retired provider must not be read again.");
            switch (method!.Name)
            {
                case "get_IsInitialized": return true;
                case "get_WindowState": return WindowState.Normal;
                case "get_IsEventDriven": EventDrivenReads++; return false;
                case "DoRender": RenderCalls++; RenderAction?.Invoke(); return null;
            }
            if (method.Name.StartsWith("remove_", StringComparison.Ordinal))
            {
                EventRemovals++;
                RemoveAction?.Invoke();
                return null;
            }
            if (method.Name == nameof(IDisposable.Dispose))
            {
                DisposeAction?.Invoke();
                return null;
            }
            throw new InvalidOperationException("Unexpected native provider access: " + method.Name);
        }
    }
}
