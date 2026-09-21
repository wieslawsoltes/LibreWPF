using Microsoft.Win32;
using ProGPU.Wpf.Interop;
using System.Windows;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class PortableSystemEventsTests
{
    [Fact]
    public void EventArgumentsPreserveDesktopContractValues()
    {
        Assert.Equal(PowerModes.Suspend, new PowerModeChangedEventArgs(PowerModes.Suspend).Mode);
        Assert.Equal(SessionEndReasons.Logoff, new SessionEndedEventArgs(SessionEndReasons.Logoff).Reason);

        var ending = new SessionEndingEventArgs(SessionEndReasons.SystemShutdown)
        {
            Cancel = true,
        };
        Assert.True(ending.Cancel);
        Assert.Equal(SessionEndReasons.SystemShutdown, ending.Reason);

        Assert.Equal(SessionSwitchReason.SessionUnlock, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock).Reason);
        Assert.Equal((nint)42, new TimerElapsedEventArgs(42).TimerId);
        Assert.Equal(UserPreferenceCategory.VisualStyle, new UserPreferenceChangedEventArgs(UserPreferenceCategory.VisualStyle).Category);
        Assert.Equal(UserPreferenceCategory.Locale, new UserPreferenceChangingEventArgs(UserPreferenceCategory.Locale).Category);
    }

    [Fact]
    public void TypedPublisherRaisesAndUnsubscribesPreferenceEvents()
    {
        UserPreferenceCategory? observed = null;
        UserPreferenceChangedEventHandler handler = (_, args) => observed = args.Category;

        SystemEvents.UserPreferenceChanged += handler;
        try
        {
            PortableSystemEvents.NotifyUserPreferenceChanged(UserPreferenceCategory.Color);
            Assert.Equal(UserPreferenceCategory.Color, observed);
        }
        finally
        {
            SystemEvents.UserPreferenceChanged -= handler;
        }

        observed = null;
        PortableSystemEvents.NotifyUserPreferenceChanged(UserPreferenceCategory.Window);
        Assert.Null(observed);
    }

    [Fact]
    public void SessionEndingPublisherReturnsCancellationState()
    {
        SessionEndingEventHandler handler = (_, args) => args.Cancel = true;
        SystemEvents.SessionEnding += handler;
        try
        {
            Assert.False(PortableSystemEvents.NotifySessionEnding(SessionEndReasons.Logoff));
        }
        finally
        {
            SystemEvents.SessionEnding -= handler;
        }

        Assert.True(PortableSystemEvents.NotifySessionEnding(SessionEndReasons.Logoff));
    }

    [Fact]
    public async Task TimerAndEventThreadApisArePortable()
    {
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SystemEvents.InvokeOnEventsThread((Action)(() => callback.TrySetResult()));
        await callback.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var timerElapsed = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        TimerElapsedEventHandler handler = (_, args) => timerElapsed.TrySetResult(args.TimerId);
        SystemEvents.TimerElapsed += handler;
        nint timerId = SystemEvents.CreateTimer(10);
        try
        {
            Assert.Equal(timerId, await timerElapsed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            SystemEvents.KillTimer(timerId);
            SystemEvents.TimerElapsed -= handler;
        }
    }

    [Fact]
    public async Task EventHandlerInvocationUsesDesktopNullSenderContract()
    {
        var callback = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        SystemEvents.InvokeOnEventsThread((EventHandler)((sender, _) => callback.TrySetResult(sender)));
        Assert.Null(await callback.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ThrowingSubscriberIsRemovedAfterFirstPublication()
    {
        int invocationCount = 0;
        EventHandler handler = (_, _) =>
        {
            invocationCount++;
            throw new InvalidOperationException("Expected subscriber failure.");
        };

        SystemEvents.DisplaySettingsChanged += handler;
        PortableSystemEvents.NotifyDisplaySettingsChanged();
        PortableSystemEvents.NotifyDisplaySettingsChanged();

        Assert.Equal(1, invocationCount);
        SystemEvents.DisplaySettingsChanged -= handler;
    }

    [Fact]
    public void TimerRejectsNonPositiveIntervals()
    {
        Assert.Throws<ArgumentException>(() => SystemEvents.CreateTimer(0));
        Assert.Throws<ArgumentException>(() => SystemEvents.CreateTimer(-1));
    }

    [Fact]
    public void SilkHostDoesNotSynthesizeGlobalEventsFromWindowGeometry()
    {
        var notifications = new List<string>();
        EventHandler changing = (_, _) => notifications.Add("Changing");
        EventHandler changed = (_, _) => notifications.Add("Changed");
        EventHandler fontsChanged = (_, _) => notifications.Add("FontsChanged");

        SystemEvents.DisplaySettingsChanging += changing;
        SystemEvents.DisplaySettingsChanged += changed;
        SystemEvents.InstalledFontsChanged += fontsChanged;
        try
        {
            using var host = new ProGpuWpfWindowHost();
            var source = new FakePortablePresentationSource();

            Assert.True(host.TryBindPortablePresentationSource(source));
            Assert.Empty(notifications);

            Assert.True(host.UpdatePortablePresentationSourceDpiScale(1.0, 1.0));
            Assert.Empty(notifications);

            Assert.False(host.UpdatePortablePresentationSourceDpiScale(double.NaN, 1.0));
            Assert.False(host.UpdatePortablePresentationSourceDpiScale(1.0, 0.0));
            Assert.Empty(notifications);

            Assert.True(host.UpdatePortablePresentationSourceClientSize(640, 480));
            Assert.Empty(notifications);

            Assert.False(host.UpdatePortablePresentationSourceDpiScale(1.0, 1.0));
            Assert.Empty(notifications);

            Assert.True(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
            Assert.Empty(notifications);

            Assert.False(host.UpdatePortablePresentationSourceDpiScale(2.0, 2.0));
            Assert.True(host.UpdatePortablePresentationSourceClientSize(800, 600));
            Assert.Empty(notifications);
        }
        finally
        {
            SystemEvents.DisplaySettingsChanging -= changing;
            SystemEvents.DisplaySettingsChanged -= changed;
            SystemEvents.InstalledFontsChanged -= fontsChanged;
        }
    }

    [Fact]
    public void TypedSystemThemeSourceRegistrationRelaysStateAndStopsAfterDisposal()
    {
        var serviceKey = new PortableWpfServiceKey("PortableSystemEventsTests.Theme");
        var source = new FakePortableSystemThemeSource(serviceKey, PortableSystemTheme.Dark);
        int changeCount = 0;
        EventHandler changed = (sender, _) =>
        {
            if (ReferenceEquals(sender, source))
            {
                changeCount++;
            }
        };

        PortableWpfServiceRegistry.SystemThemeChanged += changed;
        IDisposable? registration = null;
        try
        {
            registration = PortableWpfServiceRegistry.RegisterSystemThemeSource(source);
            Assert.Equal(1, changeCount);
            Assert.True(PortableWpfServiceRegistry.TryGetSystemThemeSource(serviceKey, out IPortableSystemThemeSource registered));
            Assert.Same(source, registered);
            Assert.True(registered.TryGetSystemTheme(out PortableSystemTheme theme));
            Assert.Equal(PortableSystemTheme.Dark, theme);

            source.SetTheme(PortableSystemTheme.Light);
            Assert.Equal(2, changeCount);
            Assert.True(registered.TryGetSystemTheme(out theme));
            Assert.Equal(PortableSystemTheme.Light, theme);

            registration.Dispose();
            registration = null;
            Assert.Equal(3, changeCount);
            Assert.False(PortableWpfServiceRegistry.TryGetSystemThemeSource(serviceKey, out _));

            source.SetTheme(PortableSystemTheme.Dark);
            Assert.Equal(3, changeCount);
        }
        finally
        {
            registration?.Dispose();
            PortableWpfServiceRegistry.SystemThemeChanged -= changed;
        }
    }

    private sealed class FakePortablePresentationSource : IPortablePresentationSourceHost
    {
        public event EventHandler? RenderRequested;

        public event EventHandler? CursorRequested
        {
            add { }
            remove { }
        }

        public object? RootVisual { get; set; }

        public object CompositionTarget { get; } = new();

        public nint Handle => nint.Zero;

        public object? RequestedCursor => null;

        public string? RequestedCursorName => null;

        public Func<double, double, object?>? HitTestOverride { get; set; }

        public Func<double, double, object?[]?>? HitTestAllOverride { get; set; }

        public PortableHitTestAllBufferOverride? HitTestAllBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestBoundsBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestEllipseBoundsBufferOverride { get; set; }

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool TryUpdateRootVisualClientSize(out double width, out double height)
        {
            width = 0;
            height = 0;
            return false;
        }

        public bool DispatchHwndSourceHook(int message, nint wParam, nint lParam, out nint result, out bool handled)
        {
            result = nint.Zero;
            handled = false;
            return false;
        }

        public void Dispose()
        {
            RenderRequested = null;
        }
    }

    private sealed class FakePortableSystemThemeSource : IPortableSystemThemeSource
    {
        private PortableSystemTheme _theme;

        public FakePortableSystemThemeSource(PortableWpfServiceKey serviceKey, PortableSystemTheme theme)
        {
            ServiceKey = serviceKey;
            _theme = theme;
        }

        public PortableWpfServiceKey ServiceKey { get; }

        public event EventHandler? SystemThemeChanged;

        public bool TryGetSystemTheme(out PortableSystemTheme theme)
        {
            theme = _theme;
            return true;
        }

        public void SetTheme(PortableSystemTheme theme)
        {
            _theme = theme;
            SystemThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
