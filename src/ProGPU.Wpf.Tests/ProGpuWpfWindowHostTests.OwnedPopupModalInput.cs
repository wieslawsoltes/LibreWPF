using System.IO;
using System.Reflection;
using System.Windows.Media.ProGPU;
using ProGPU.Wpf.Interop;
using Silk.NET.Windowing;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed partial class ProGpuWpfWindowHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedPopupNativeGateReceivesCurrentAndNestedSourcePolicy(bool ownerFirst)
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        object owner = new(), dialog = new();
        var values = new List<bool>();
        if (ownerFirst)
        {
            probe.Initialized = false;
            host.BindModalInputOwner(owner);
        }
        using (PortableModalInputScope.Enter(dialog))
        {
            host.BindOwnedPopupInputGate(window, allowed => { values.Add(allowed); return true; });
            if (!ownerFirst) host.BindModalInputOwner(owner);
            // On Windows the precreation registration caches its initial value;
            // source Load publishes that value to the newly initialized provider.
            if (ownerFirst)
            {
                probe.Initialized = true;
                ApplyOwnedModalPolicy(host, false);
            }
            Assert.Equal(new[] { false }, values);
            using (PortableModalInputScope.Enter(owner)) Assert.True(values[^1]);
            Assert.False(values[^1]);
        }
        Assert.Equal(new[] { false, true, false, true }, values);
        host.Dispose();
        Assert.Equal(4, values.Count); // Release cannot re-enable the retired view.
    }

    [Fact]
    public void OwnedPopupBeforeInitializationCachesBlockedPolicyUntilActualLoadAdmission()
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        probe.Initialized = false;
        object owner = new();
        var values = new List<bool>();
        host.BindOwnedPopupInputGate(window, allowed => { values.Add(allowed); return true; });
        using (PortableModalInputScope.Enter(new object()))
        {
            host.BindModalInputOwner(owner);
            Assert.Empty(values);
            Assert.Equal(false, ReadRetirementField(host, "_nativeInputAllowed"));
            probe.Initialized = true;
            ApplyOwnedModalPolicy(host, false);
            Assert.Equal(new[] { false }, values);
        }
        Assert.Equal(new[] { false, true }, values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedPopupGateFailurePrecedesOwnerShowAndNativeVisibility(bool throws)
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        var failure = new InvalidOperationException("original native input failure");
        bool reject = false;
        host.BindOwnedPopupInputGate(window, _ => !reject ? true : throws ? throw failure : false);
        host.BindModalInputOwner(new object());
        reject = true;
        int ownerShows = 0;
        Exception error = Assert.ThrowsAny<Exception>(() => host.ShowWithoutActivation(_ =>
        {
            ownerShows++;
            return true;
        }));
        if (throws) Assert.Same(failure, error);
        else Assert.IsType<PlatformNotSupportedException>(error);
        Assert.Equal(0, ownerShows);
        Assert.Equal(0, probe.VisibleWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedPopupGateCancellationCannotShowHiddenOrDisposedReplacement(bool dispose)
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        bool cancel = false;
        host.BindOwnedPopupInputGate(window, _ =>
        {
            if (cancel)
            {
                if (dispose) host.Dispose();
                else host.Hide();
            }
            return true;
        });
        host.BindModalInputOwner(new object());
        cancel = true;
        int shows = 0;
        Assert.Throws<InvalidOperationException>(() => host.ShowWithoutActivation(_ => { shows++; return true; }));
        Assert.Equal(0, shows);
        Assert.Equal(0, probe.VisibleTrueWrites);
    }

    [Fact]
    public void OwnedPopupInitialCancellationCannotPublishRegistrationAfterHostDisposal()
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        int calls = 0;
        host.BindOwnedPopupInputGate(window, _ => { calls++; host.Dispose(); return true; });
        Assert.Throws<InvalidOperationException>(() => host.BindModalInputOwner(new object()));
        Assert.Equal(1, calls);
        Assert.Equal(1, probe.Disposals);
        Assert.Null(host.SilkWindow);
        Assert.Null(ReadRetirementField(host, "_modalInputRegistration"));
        using (PortableModalInputScope.Enter(new object())) { }
        Assert.Equal(1, calls);
    }

    [Fact]
    public void OwnedPopupRegistrationKeepsFixedSourceOwnerAcrossReentrantPublication()
    {
        using var host = CreateOwnedModalPopup(out var window, out _);
        object owner = new();
        int calls = 0;
        host.BindOwnedPopupInputGate(window, _ =>
        {
            calls++;
            host.BindModalInputOwner(owner);
            Assert.Throws<InvalidOperationException>(() => host.BindModalInputOwner(new object()));
            Assert.Throws<InvalidOperationException>(() => host.BindOwnedPopupInputGate(window, _ => true));
            return true;
        });
        host.BindModalInputOwner(owner);
        Assert.Equal(1, calls);
        using (PortableModalInputScope.Enter(new object())) Assert.Equal(2, calls);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedPopupGateRejectsReplacementBeforeAndAfterNativeCallback(bool replaceInCallback)
    {
        using var host = CreateOwnedModalPopup(out var window, out _);
        var replacement = DispatchProxy.Create<IWindow, OwnedModalWindowProbe>();
        bool replace = false;
        int calls = 0;
        host.BindOwnedPopupInputGate(window, _ =>
        {
            calls++;
            if (replace) SetPrivateField(host, "_window", replacement);
            return true;
        });
        host.BindModalInputOwner(new object());
        if (replaceInCallback) replace = true;
        else SetPrivateField(host, "_window", replacement);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ApplyOwnedModalPolicy(host, false));
            Assert.Equal(replaceInCallback ? 2 : 1, calls);
            Assert.Equal(0, ((OwnedModalWindowProbe)(object)replacement).VisibleWrites);
        }
        finally { SetPrivateField(host, "_window", window); }
    }

    [Fact]
    public void OwnedPopupGateRechecksProviderAfterInitializationGetter()
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        var replacement = DispatchProxy.Create<IWindow, OwnedModalWindowProbe>();
        int calls = 0;
        host.BindOwnedPopupInputGate(window, _ => { calls++; return true; });
        host.BindModalInputOwner(new object());
        probe.ReadInitialized = () => SetPrivateField(host, "_window", replacement);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ApplyOwnedModalPolicy(host, false));
            Assert.Equal(1, calls);
        }
        finally { probe.ReadInitialized = null; SetPrivateField(host, "_window", window); }
    }

    [Fact]
    public void OwnedPopupSourceReleaseDoesNotEnablePendingNativeRetirement()
    {
        using var host = CreateOwnedModalPopup(out var window, out var probe);
        var values = new List<bool>();
        host.BindOwnedPopupInputGate(window, allowed => { values.Add(allowed); return true; });
        host.BindModalInputOwner(new object());
        using (PortableModalInputScope.Enter(new object()))
        {
            Assert.False(values[^1]);
            var failure = new InvalidOperationException("native retirement pending");
            probe.DisposalFailure = failure;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(host.Dispose));
            Assert.Same(window, host.SilkWindow);
            Assert.NotNull(ReadRetirementField(host, "_ownedPopupInputGate"));
            Assert.Null(ReadRetirementField(host, "_modalInputRegistration"));
            Assert.Equal(new[] { true, false }, values);
            probe.DisposalFailure = null;
            host.Dispose();
            Assert.Null(host.SilkWindow);
            Assert.Null(ReadRetirementField(host, "_ownedPopupInputGate"));
        }
        Assert.Equal(new[] { true, false }, values);
        Assert.Equal(2, probe.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedPopupBindingRejectsOrdinaryOrForeignWindow(bool popup)
    {
        using var host = new ProGpuWpfWindowHost(new() { IsPopupSurface = popup });
        var window = DispatchProxy.Create<IWindow, OwnedModalWindowProbe>();
        var foreign = DispatchProxy.Create<IWindow, OwnedModalWindowProbe>();
        SetPrivateField(host, "_window", window);
        SetPrivateField(host, "_nativeWindowThreadId", Environment.CurrentManagedThreadId);
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => host.BindOwnedPopupInputGate(
            popup ? foreign : window, _ => { calls++; return true; }));
        Assert.Equal(0, calls);
        Assert.Null(ReadRetirementField(host, "_ownedPopupInputGate"));
    }

    [Fact]
    public void OwnedPopupBindingRejectsWrongThreadBeforeRegistration()
    {
        using var host = CreateOwnedModalPopup(out var window, out _);
        Exception? error = null;
        var thread = new Thread(() => error = Record.Exception(() => host.BindOwnedPopupInputGate(window, _ => true)));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Null(ReadRetirementField(host, "_ownedPopupInputGate"));
    }

    [Fact]
    public void OwnedPopupFactoryGateUsesActualControllerAndPrecedesInitialization()
    {
        string source = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));
        int factory = source.IndexOf("private void EnsureWindow()", StringComparison.Ordinal);
        int load = source.IndexOf("private void OnLoad()", factory, StringComparison.Ordinal);
        string setup = source[factory..load];
        Assert.Contains("if (createdOwnedCocoa)\n            BindOwnedPopupInputGate(_window, _windowController.SetInputAllowed);", setup);
        Assert.Contains("NativePopupWindow.CreateOwnedCocoaWindow(", setup);
        Assert.DoesNotContain("NativeWindowModalSession.TryBegin", setup);
        Assert.Contains("if (!_options.EnableNativeModalSessions) return;", source);
        // The original ordinary-window guard is still independent of factory admission.
        Assert.Contains("else if (OperatingSystem.IsWindows() || NativeInputAllowedSetterOverride != null)", source);
    }

    private static ProGpuWpfWindowHost CreateOwnedModalPopup(out IWindow window, out OwnedModalWindowProbe probe)
    {
        var host = new ProGpuWpfWindowHost(new() { IsPopupSurface = true, IsVisible = false, EnablePortablePopupService = false });
        window = DispatchProxy.Create<IWindow, OwnedModalWindowProbe>();
        probe = (OwnedModalWindowProbe)(object)window;
        SetPrivateField(host, "_window", window);
        SetPrivateField(host, "_nativeWindowThreadId", Environment.CurrentManagedThreadId);
        return host;
    }

    private static void ApplyOwnedModalPolicy(ProGpuWpfWindowHost host, bool allowed) =>
        typeof(ProGpuWpfWindowHost).GetMethod("SetNativeInputAllowed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<bool>>(host)(allowed);

    public class OwnedModalWindowProbe : DispatchProxy
    {
        internal bool Initialized = true;
        internal bool Closing;
        internal int VisibleWrites, VisibleTrueWrites, Disposals;
        internal Action? ReadInitialized;
        internal Exception? DisposalFailure;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            switch (method!.Name)
            {
                case "get_IsInitialized": ReadInitialized?.Invoke(); return Initialized;
                case "get_IsClosing": return Closing;
                case "get_IsVisible": return false;
                case "set_IsVisible":
                    VisibleWrites++;
                    if ((bool)arguments![0]!) VisibleTrueWrites++;
                    return null;
                case "Close": Closing = true; return null;
                case "ContinueEvents": return null;
                case "Dispose":
                    Disposals++;
                    if (DisposalFailure != null) throw DisposalFailure;
                    Initialized = false;
                    return null;
                default:
                    if (method.Name.StartsWith("remove_", StringComparison.Ordinal)) return null;
                    throw new InvalidOperationException("Unexpected provider access: " + method.Name);
            }
        }
    }
}
