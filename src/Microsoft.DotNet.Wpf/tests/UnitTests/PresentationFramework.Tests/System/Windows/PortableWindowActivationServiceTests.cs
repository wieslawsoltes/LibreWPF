// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableWindowActivationServiceTests
{
    [PortableInputFact]
    public void MenuEntryWithoutFocusUsesActiveVisibleUnblockedPortableWindow()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost firstHost = PortablePresentationSourceHost.Create();
            using IPortablePresentationSourceHost secondHost = PortablePresentationSourceHost.Create();
            var first = new Window { Width = 200, Height = 100 };
            var second = new Window { Width = 200, Height = 100 };
            PortableWindowActivationService.Register(activate: value => value,
                getHandle: value => ReferenceEquals(value, first) ? firstHost.Handle : secondHost.Handle);
            var firstScope = (IPortableAccessKeyScopeSource)first;
            var secondScope = (IPortableAccessKeyScopeSource)second;
            object? menuSource = null;
            KeyboardNavigation.EnterMenuModeEventHandler handler = (source, _) => { menuSource = source; return true; };
            KeyboardNavigation.Current.EnterMenuMode += handler;
            try
            {
                first.Show(); second.Show();
                firstHost.RootVisual = first; secondHost.RootVisual = second;
                firstHost.SetClientSize(200, 100); secondHost.SetClientSize(200, 100);
                PortableWindowActivationService.SetActivationState(second, true);
                firstScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                secondScope.IsPortableAccessKeyScopeActive.Should().BeTrue();
                Keyboard.ClearFocus();
                foreach (RoutedEvent routedEvent in new[] { Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice,
                        (PresentationSource)secondHost, 0, Key.F10) { RoutedEvent = routedEvent });
                menuSource.Should().BeSameAs(secondHost);
                using (PortableModalInputScope.Enter(first))
                    secondScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                second.Hide();
                secondScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                second.Show();
                PortableWindowActivationService.SetActivationState(second, false);
                secondScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
                PortableWindowActivationService.SetActivationState(first, true);
                firstScope.IsPortableAccessKeyScopeActive.Should().BeTrue();
                first.Close();
                firstScope.IsPortableAccessKeyScopeActive.Should().BeFalse();
            }
            finally
            {
                KeyboardNavigation.Current.EnterMenuMode -= handler;
                Keyboard.ClearFocus();
                firstHost.RootVisual = null; secondHost.RootVisual = null;
                if (!first.IsDisposed) first.Close();
                if (!second.IsDisposed) second.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    private const int MouseMoveInputKind = 3;
    private const int MouseDownInputKind = 4;
    private const int MouseUpInputKind = 5;
    private const int LeftMouseButton = 1;

    [PortableInputFact]
    public void FailedGateReleaseStillClosesAcceptedDialogAndDisposesItsHost()
    {
        RunInUiApartment(() =>
        {
            bool rejectEnable = false;
            int closes = 0, disposals = 0;
            using var gate = PortableModalInputScope.RegisterWindow(new object(), allowed =>
            {
                if (allowed && rejectEnable) throw new InvalidOperationException("Native gate release failed.");
            });
            var window = new Window { Width = 200, Height = 100 };
            PortableWindowActivationService.Register(activate: _ => window,
                getHandle: _ => new IntPtr(1234),
                close: _ => closes++, dispose: _ => disposals++,
                releaseDialog: (_, completed) => completed(),
                runDialog: (_, continuation) =>
                {
                    rejectEnable = true;
                    Action close = window.Close;
                    close.Should().Throw<AggregateException>();
                    window.IsDisposed.Should().BeTrue();
                    continuation().Should().BeFalse();
                    PortableModalInputScope.IsActive.Should().BeFalse();
                    PortableModalInputScope.IsNativeInputPolicySynchronized.Should().BeFalse();
                });
            try
            {
                window.ShowDialog().Should().BeFalse();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                rejectEnable = false;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
                using var restored = PortableModalInputScope.Enter(new object());
            }
        });
    }

    [PortableInputFact]
    public void DialogHideAndCloseKeepInputAndFocusBlockedUntilNativeCompletion()
    {
        RunInUiApartment(() =>
        {
            foreach (bool closeDialog in new[] { false, true })
            {
                using IPortablePresentationSourceHost ownerHost = PortablePresentationSourceHost.Create();
                var owner = new Window { Width = 200, Height = 100, Focusable = true };
                var dialog = new Window { Width = 100, Height = 100 };
                Action? completed = null;
                bool nativeEnded = false, ownerAllowed = true;
                int releaseRequests = 0, activationRequests = 0;
                using var gate = PortableModalInputScope.RegisterWindow(owner, allowed => ownerAllowed = allowed);
                PortableWindowActivationService.Register(activate: value => value,
                    getHandle: value => ReferenceEquals(value, owner) ? ownerHost.Handle : new IntPtr(5679),
                    requestActivation: value =>
                    {
                        value.Should().BeSameAs(owner);
                        nativeEnded.Should().BeTrue();
                        ownerAllowed.Should().BeTrue();
                        PortableModalInputScope.IsNativeInputPolicySynchronized.Should().BeTrue();
                        activationRequests++;
                        return true;
                    },
                    runDialog: (_, continuation) =>
                    {
                        PortableWindowActivationService.SetActivationState(owner, false);
                        if (closeDialog) dialog.Close(); else dialog.Hide();
                        continuation().Should().BeFalse();
                        ownerAllowed.Should().BeFalse();
                        activationRequests.Should().Be(0);
                        Keyboard.FocusedElement.Should().BeNull();
                    },
                    releaseDialog: (value, callback) =>
                    {
                        value.Should().BeSameAs(dialog);
                        releaseRequests++;
                        completed = callback;
                    });
                try
                {
                    owner.Show();
                    ownerHost.RootVisual = owner;
                    ownerHost.SetClientSize(200, 100);
                    Keyboard.Focus(owner).Should().BeSameAs(owner);
                    PortableWindowActivationService.SetActivationState(owner, true);
                    dialog.ShowDialog().Should().BeFalse();
                    releaseRequests.Should().Be(1); // Hide/Close plus finally transfer only once.
                    ownerAllowed.Should().BeFalse();
                    activationRequests.Should().Be(0);
                    dialog.IsDisposed.Should().Be(closeDialog);
                    if (!closeDialog)
                    {
                        Action reopen = () => dialog.ShowDialog();
                        reopen.Should().Throw<InvalidOperationException>().WithMessage("*still completing native release*");
                    }
                    nativeEnded = true;
                    completed.Should().NotBeNull();
                    completed!();
                    completed(); // An accidental duplicate cannot restore focus twice.
                    ownerAllowed.Should().BeTrue();
                    activationRequests.Should().Be(1);
                    Keyboard.FocusedElement.Should().BeSameAs(owner);
                    PortableModalInputScope.IsActive.Should().BeFalse();
                }
                finally
                {
                    nativeEnded = true;
                    completed?.Invoke();
                    Keyboard.ClearFocus();
                    if (!dialog.IsDisposed) dialog.Close();
                    ownerHost.RootVisual = null;
                    owner.Close();
                    PortableWindowActivationService.Clear();
                }
            }
        });
    }

    [PortableInputFact]
    public void DialogRestoresSourceFocusOnlyAfterInputAdmissionAndNativeActivation()
    {
        RunInUiApartment(() =>
        {
            foreach (bool accepted in new[] { true, false })
            {
                using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
                var window = new Window { Width = 200, Height = 100, Focusable = true };
                int requests = 0;
                PortableWindowActivationService.Register(activate: _ => window,
                    getHandle: _ => host.Handle,
                    requestActivation: value =>
                    {
                        value.Should().BeSameAs(window);
                        PortableModalInputScope.AllowsInput(window).Should().BeTrue();
                        PortableModalInputScope.IsNativeInputPolicySynchronized.Should().BeTrue();
                        requests++;
                        return accepted;
                    });
                try
                {
                    window.Show();
                    host.RootVisual = window;
                    host.SetClientSize(200, 100);
                    Keyboard.Focus(window).Should().BeSameAs(window);
                    PortableWindowActivationService.SetActivationState(window, true);
                    using (PortableWindowActivationService.CaptureModalInputRestoreState())
                    {
                        using (PortableModalInputScope.Enter(new object()))
                        {
                            PortableWindowActivationService.PrepareForModalInput();
                            PortableWindowActivationService.SetActivationState(window, false);
                            Keyboard.FocusedElement.Should().BeNull();
                            requests.Should().Be(0);
                        }
                    }
                    requests.Should().Be(1);
                    Keyboard.FocusedElement.Should().BeSameAs(accepted ? window : null);
                    window.IsActive.Should().BeFalse(); // Only actual host events publish IsActive.
                    PortableWindowActivationService.SetActivationState(window, true);
                    using (PortableWindowActivationService.CaptureModalInputRestoreState())
                    {
                        window.Hide();
                    }
                    requests.Should().Be(1); // Hidden windows are not reactivated.
                }
                finally
                {
                    Keyboard.ClearFocus();
                    host.RootVisual = null;
                    window.Close();
                    PortableWindowActivationService.Clear();
                }
            }
        });
    }

    [PortableInputFact]
    public void TypedOwnerChangesPreserveCollectionsWhenHostRejectsAndNeverUseOpaqueHandles()
    {
        RunInUiApartment(() =>
        {
            var owner = new Window();
            var secondOwner = new Window();
            var child = new Window { ShowInTaskbar = false };
            var updates = new List<Window?>();
            bool reject = false;
            PortableWindowActivationService.Register(
                activate: value => value, createHidden: value => value,
                getHandle: _ => new IntPtr(123),
                setOwner: (activation, value) =>
                {
                    activation.Should().BeSameAs(child);
                    if (reject) throw new PlatformNotSupportedException("Rejected owner.");
                    updates.Add((Window?)value);
                });
            try
            {
                new WindowInteropHelper(owner).EnsureHandle();
                new WindowInteropHelper(secondOwner).EnsureHandle();
                child.Owner = owner; // No native window or hidden taskbar-owner creation.
                updates.Should().BeEmpty();
                owner.OwnedWindows.Count.Should().Be(1);
                owner.OwnedWindows[0].Should().BeSameAs(child);
                new WindowInteropHelper(child).EnsureHandle();
                reject = true;
                Action replace = () => child.Owner = secondOwner;
                replace.Should().Throw<PlatformNotSupportedException>();
                child.Owner.Should().BeSameAs(owner);
                owner.OwnedWindows.Count.Should().Be(1);
                owner.OwnedWindows[0].Should().BeSameAs(child);
                secondOwner.OwnedWindows.Count.Should().Be(0);
                reject = false;
                child.Owner = secondOwner;
                updates.Should().ContainSingle().Which.Should().BeSameAs(secondOwner);
                Action rawOwner = () => new WindowInteropHelper(child).Owner = new IntPtr(456);
                rawOwner.Should().Throw<PlatformNotSupportedException>();
                child.Owner.Should().BeSameAs(secondOwner);
                child.Owner = null;
                updates.Should().HaveCount(2);
                updates[1].Should().BeNull();
                secondOwner.OwnedWindows.Count.Should().Be(0);
            }
            finally
            {
                child.Close(); secondOwner.Close(); owner.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    private sealed class PortableInputFactAttribute : FactAttribute
    {
        public PortableInputFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input-manager construction, including on Windows.";
        }
    }

    private sealed class WindowsMilFactAttribute : FactAttribute
    {
        public WindowsMilFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (!OperatingSystem.IsWindows() || PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.WindowsMil)
                Skip = "Requires the independently selected native Windows WPF lane.";
        }
    }

    [WindowsMilFact]
    public void NativeWindowSystemCommandsKeepPostedHwndMessages()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
            var window = new Window { Width = 200, Height = 100 };
            var commands = new List<int>();
            HwndSource? source = null;
            HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x0112) // WM_SYSCOMMAND
                {
                    commands.Add(wParam.ToInt32());
                    handled = true; // Observe dispatch without showing/minimizing the test window.
                }
                return IntPtr.Zero;
            };
            try
            {
                IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
                source = HwndSource.FromHwnd(handle);
                source.Should().NotBeNull();
                source!.AddHook(hook);
                SystemCommands.MaximizeWindow(window);
                SystemCommands.MinimizeWindow(window);
                SystemCommands.RestoreWindow(window);
                SystemCommands.CloseWindow(window);
                commands.Should().BeEmpty(); // These remain asynchronous Windows messages.
                window.WindowState.Should().Be(WindowState.Normal);
                window.IsDisposed.Should().BeFalse();
                window.Dispatcher.Invoke(() => { }, Threading.DispatcherPriority.ApplicationIdle);
                commands.Should().Equal(0xF030, 0xF020, 0xF120, 0xF060);
            }
            finally
            {
                source?.RemoveHook(hook);
                if (!window.IsDisposed) window.Close();
            }
        });
    }

    [PortableInputFact]
    public void PortableDeviceStateAndCommittedTextHaveOneOwnerOnEveryOs()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var source = (PresentationSource)host;
            var root = new HitTestElement { Focusable = true };
            host.RootVisual = root; host.SetClientSize(200, 100);
            Keyboard.Focus(root).Should().BeSameAs(root);
            int keyDowns = 0, keyUps = 0, texts = 0;
            root.KeyDown += (_, e) =>
            {
                e.Key.Should().Be(Key.A);
                e.InputSource.Should().BeSameAs(source);
                Keyboard.IsKeyDown(Key.A).Should().BeTrue();
                Keyboard.Modifiers.Should().Be(ModifierKeys.Shift);
                ++keyDowns;
            };
            root.KeyUp += (_, e) => { e.Key.Should().Be(Key.A); ++keyUps; };
            root.TextInput += (_, e) => { e.Text.Should().Be("A"); ++texts; };
            root.MouseDown += (_, e) => e.LeftButton.Should().Be(MouseButtonState.Pressed);
            try
            {
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.KeyDown, key: "A", modifiers: PortableInputModifiers.Shift));
                texts.Should().Be(0); // Key delivery must not fabricate a second text event.
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.TextInput, character: 'A', modifiers: PortableInputModifiers.Shift));
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.KeyUp, key: "A"));
                keyDowns.Should().Be(1); keyUps.Should().Be(1); texts.Should().Be(1);
                Keyboard.IsKeyDown(Key.A).Should().BeFalse(); Keyboard.Modifiers.Should().Be(ModifierKeys.None);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseDown, x: 10, y: 10, button: PortableMouseButton.Left));
                Mouse.LeftButton.Should().Be(MouseButtonState.Pressed);
                PortableWindowActivationService.ProcessInput(source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseUp, x: 10, y: 10, button: PortableMouseButton.Left));
                Mouse.LeftButton.Should().Be(MouseButtonState.Released);
            }
            finally { Keyboard.ClearFocus(); Mouse.Capture(null); }
        });
    }

    [PortableInputFact]
    public void SystemMenuUsesTypedRegistrationAndDesktopCoordinatesWithoutSourceHandleAccess()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            int handleQueries = 0;
            bool accepted = true;
            var positions = new List<Point>();
            PortableWindowActivationService.RegisterPortableInteropService();
            PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework, out var registrar).Should().BeTrue();
            registrar!.Register(new PortableWindowActivationCallbacks(_ => activation,
                getHandle: _ => { handleQueries++; return new IntPtr(5678); })
            {
                CreateHidden = _ => activation,
                ShowSystemMenu = (owner, x, y) =>
                {
                    owner.Should().BeSameAs(activation);
                    positions.Add(new Point(x, y));
                    return accepted;
                }
            });
            var window = new Window { Width = 200, Height = 100 };
            try
            {
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                int initialHandleQueries = handleQueries;
                SystemCommands.ShowSystemMenu(window, new Point(-1234.5, 67.25));
                SystemCommands.ShowSystemMenuPhysicalCoordinates(window, new Point(-900, 450));
                positions.Should().Equal(new Point(-1234.5, 67.25), new Point(-900, 450));
                accepted = false;
                Action rejected = () => SystemCommands.ShowSystemMenu(window, new Point(12, 24));
                rejected.Should().Throw<PlatformNotSupportedException>().WithMessage("*system menu*");
                Action invalid = () => SystemCommands.ShowSystemMenu(window, new Point(double.NaN, 0));
                invalid.Should().Throw<ArgumentException>();
                positions.Count.Should().Be(3);

                // Registering a legacy host must clear the optional capability,
                // not retain the previous host's system-menu callback.
                registrar.Register(new PortableWindowActivationCallbacks(_ => activation));
                rejected.Should().Throw<PlatformNotSupportedException>();
                positions.Count.Should().Be(3);
                var failure = new InvalidOperationException("Host menu failure.");
                registrar.Register(new PortableWindowActivationCallbacks(_ => activation)
                    { ShowSystemMenu = (_, _, _) => throw failure });
                rejected.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                handleQueries.Should().Be(initialHandleQueries);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void SystemWindowCommandsUsePortableOwnerStateAndCancelableCloseOnEveryOs()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var states = new List<WindowState>();
            int handleQueries = 0, closes = 0, disposals = 0;
            PortableWindowActivationService.Register(
                activate: _ => activation, createHidden: _ => activation,
                getHandle: owner =>
                {
                    owner.Should().BeSameAs(activation);
                    handleQueries++;
                    return new IntPtr(5678); // Deliberately not an HWND.
                },
                setWindowState: (owner, state) =>
                {
                    owner.Should().BeSameAs(activation);
                    states.Add((WindowState)state);
                },
                close: owner => { owner.Should().BeSameAs(activation); closes++; },
                dispose: owner => { owner.Should().BeSameAs(activation); disposals++; });
            var window = new Window { Width = 200, Height = 100 };
            bool cancelClose = true;
            int closing = 0, closed = 0;
            window.Closing += (_, e) => { closing++; e.Cancel = cancelClose; };
            window.Closed += (_, _) => closed++;
            try
            {
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                int initialHandleQueries = handleQueries;
                SystemCommands.MaximizeWindow(window);
                window.WindowState.Should().Be(WindowState.Maximized);
                SystemCommands.MinimizeWindow(window);
                window.WindowState.Should().Be(WindowState.Minimized);
                SystemCommands.RestoreWindow(window);
                window.WindowState.Should().Be(WindowState.Normal);
                states.Should().Equal(WindowState.Maximized, WindowState.Minimized, WindowState.Normal);

                SystemCommands.CloseWindow(window);
                window.IsDisposed.Should().BeFalse();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                closing.Should().Be(1); closed.Should().Be(0);
                closes.Should().Be(0); disposals.Should().Be(0);
                cancelClose = false;
                SystemCommands.CloseWindow(window);
                window.IsDisposed.Should().BeTrue();
                window.PortableWindowActivation.Should().BeNull();
                closing.Should().Be(2); closed.Should().Be(1);
                closes.Should().Be(1); disposals.Should().Be(1);
                handleQueries.Should().Be(initialHandleQueries);
            }
            finally
            {
                cancelClose = false;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomChromeUsesPortableOwnerBeforeAndAfterSourceCreation(bool attachBeforeSource)
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var borders = new List<WindowStyle>();
            PortableWindowActivationService.Register(
                activate: _ => activation, createHidden: _ => activation,
                getHandle: _ => new IntPtr(5678),
                setWindowBorder: (owner, _, style) =>
                {
                    owner.Should().BeSameAs(activation);
                    borders.Add((WindowStyle)style);
                });
            var window = new Window { Width = 200, Height = 100, WindowStyle = WindowStyle.SingleBorderWindow };
            var chrome = new Shell.WindowChrome { CaptionHeight = 32, GlassFrameThickness = new Thickness(0) };
            try
            {
                if (attachBeforeSource) Shell.WindowChrome.SetWindowChrome(window, chrome);
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                if (!attachBeforeSource) Shell.WindowChrome.SetWindowChrome(window, chrome);
                var source = (IPortableWindowStateSource)window;
                source.TryGetPortableWindowState(out var custom).Should().BeTrue();
                custom.WindowStyle.Should().Be((int)WindowStyle.None);
                chrome.CaptionHeight = 40;
                Shell.WindowChrome.SetWindowChrome(window, null);
                source.TryGetPortableWindowState(out var standard).Should().BeTrue();
                standard.WindowStyle.Should().Be((int)WindowStyle.SingleBorderWindow);
                borders.Should().Contain(WindowStyle.SingleBorderWindow);
                if (!attachBeforeSource) borders.Should().Contain(WindowStyle.None);
                window.WindowStyle.Should().Be(WindowStyle.SingleBorderWindow);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureHandleCreatesOneHiddenSourceAndReusesItForShow(bool showAfterCreation)
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            int hiddenCreates = 0, ordinaryCreates = 0, shows = 0, closes = 0, disposals = 0;
            PortableWindowActivationService.Register(
                activate: _ => { ordinaryCreates++; return new object(); },
                createHidden: _ => { hiddenCreates++; return activation; },
                getHandle: value => ReferenceEquals(value, activation) ? new IntPtr(5678) : IntPtr.Zero,
                show: value => { value.Should().BeSameAs(activation); shows++; },
                close: _ => closes++,
                dispose: _ => disposals++);
            var window = new Window { Width = 200, Height = 100 };
            var interop = new WindowInteropHelper(window);
            int initialized = 0;
            window.SourceInitialized += (_, _) =>
            {
                initialized++;
                interop.Handle.Should().Be(new IntPtr(5678));
                window.IsVisible.Should().BeFalse();
                // Event reentrancy must see the published identity, not create a second source.
                interop.EnsureHandle().Should().Be(interop.Handle);
            };
            try
            {
                interop.Handle.Should().Be(IntPtr.Zero);
                Visibility originalVisibility = window.Visibility;
                interop.EnsureHandle().Should().Be(new IntPtr(5678));
                interop.EnsureHandle().Should().Be(new IntPtr(5678));
                window.Visibility.Should().Be(originalVisibility);
                window.IsVisible.Should().BeFalse();
                window.IsActive.Should().BeFalse();
                shows.Should().Be(0);
                if (showAfterCreation)
                {
                    window.Show();
                    interop.EnsureHandle().Should().Be(new IntPtr(5678));
                    window.Hide();
                    window.Show();
                    shows.Should().Be(2);
                }
                hiddenCreates.Should().Be(1);
                ordinaryCreates.Should().Be(0);
                initialized.Should().Be(1);
                window.Close();
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(0)] // Legacy callback set has no hidden creation capability.
    [InlineData(1)] // Registered hidden factory rejects this window.
    [InlineData(2)] // Factory returns a source without a usable identity.
    public void EnsureHandleFailsClosedForUnavailableHiddenSources(int failure)
    {
        RunInUiApartment(() =>
        {
            int ordinaryCreates = 0, closes = 0, disposals = 0, initialized = 0;
            PortableWindowActivationService.Register(
                activate: _ => { ordinaryCreates++; return new object(); },
                createHidden: failure == 0 ? null : _ => failure == 1 ? null! : new object(),
                getHandle: _ => IntPtr.Zero,
                close: _ => closes++, dispose: _ => disposals++);
            var window = new Window();
            window.SourceInitialized += (_, _) => initialized++;
            var interop = new WindowInteropHelper(window);
            try
            {
                Action ensure = () => interop.EnsureHandle();
                if (failure == 0)
                    ensure.Should().Throw<PlatformNotSupportedException>().WithMessage("*hidden window sources*");
                else
                    ensure.Should().Throw<InvalidOperationException>();
                ordinaryCreates.Should().Be(0);
                initialized.Should().Be(0);
                interop.Handle.Should().Be(IntPtr.Zero);
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(failure == 2 ? 1 : 0);
                disposals.Should().Be(failure == 2 ? 1 : 0);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ExplicitRegistrationRoutesWindowLifecycleOnEveryPlatform()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Clear();
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
            var activation = new object();
            int creates = 0, shows = 0, hides = 0, requests = 0, runs = 0, closes = 0, disposals = 0;
            string? title = null;
            PortableWindowActivationService.Register(
                activate: _ => { creates++; return activation; },
                show: value => { value.Should().BeSameAs(activation); shows++; },
                hide: value => { value.Should().BeSameAs(activation); hides++; },
                setTitle: (_, value) => title = value,
                close: _ => closes++,
                run: _ => runs++,
                dispose: _ => disposals++,
                getHandle: _ => new IntPtr(1234),
                requestActivation: _ => { requests++; return true; });
            var window = new Window { Width = 200, Height = 100 };
            int initialized = 0;
            window.SourceInitialized += (_, _) => initialized++;
            try
            {
                PortableWindowActivationService.IsEnabled.Should().BeTrue();
                window.Show();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                window.Title = "Portable title";
                title.Should().Be("Portable title");
                window.Activate().Should().BeTrue();
                PortableWindowActivationService.GetHandle(activation).Should().Be(new IntPtr(1234));
                PortableWindowActivationService.TryRun(window).Should().BeTrue();
                window.Hide();
                window.Show();
                creates.Should().Be(1);
                initialized.Should().Be(1);
                shows.Should().Be(2);
                hides.Should().Be(1);
                requests.Should().Be(1);
                runs.Should().Be(1);
                window.Close();
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                if (!window.IsDisposed)
                {
                    window.Close();
                }
                PortableWindowActivationService.Clear();
            }
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
        });
    }

    [PortableInputFact]
    public void PortableDialogHideReturnsAndReusesSourceWithCancelableResult()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var window = new Window { Width = 200, Height = 100 };
            int creates = 0, runs = 0, shows = 0, hides = 0, closes = 0, disposals = 0;
            int closingCalls = 0;
            window.Closing += (_, e) => e.Cancel = ++closingCalls == 1;
            PortableWindowActivationService.Register(
                activate: _ => { creates++; return activation; },
                show: _ => shows++,
                hide: _ => { PortableModalInputScope.IsActive.Should().BeFalse(); hides++; },
                close: _ => { PortableModalInputScope.IsActive.Should().BeFalse(); closes++; },
                dispose: _ => disposals++,
                getHandle: _ => new IntPtr(5678),
                run: _ => throw new InvalidOperationException("Application loop must not run a dialog."),
                releaseDialog: (_, completed) => completed(),
                runDialog: (owner, continuation) =>
                {
                    owner.Should().BeSameAs(activation);
                    PortableModalInputScope.AllowsInput(window).Should().BeTrue();
                    PortableModalInputScope.AllowsInput(activation).Should().BeFalse();
                    ComponentDispatcher.IsThreadModal.Should().BeTrue();
                    continuation().Should().BeTrue();
                    runs++;
                    if (runs == 1)
                    {
                        window.Hide();
                        window.IsDisposed.Should().BeFalse();
                    }
                    else
                    {
                        window.DialogResult = true;
                        window.DialogResult.Should().BeNull();
                        continuation().Should().BeTrue(); // Canceled close keeps pumping.
                        window.DialogResult = true;
                    }
                    continuation().Should().BeFalse();
                });
            try
            {
                window.ShowDialog().Should().Be(false);
                PortableModalInputScope.IsActive.Should().BeFalse();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                closes.Should().Be(0); disposals.Should().Be(0);
                window.ShowDialog().Should().Be(true);
                creates.Should().Be(1); runs.Should().Be(2); shows.Should().Be(2); hides.Should().Be(1);
                closes.Should().Be(1); disposals.Should().Be(1);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void PortableDialogRequiresCapabilityBeforeShowAndRejectsPrematureReturn()
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var window = new Window { Width = 200, Height = 100 };
            int creates = 0, hides = 0;
            PortableWindowActivationService.Register(activate: _ => { creates++; return activation; });
            try
            {
                Action show = () => window.ShowDialog();
                show.Should().Throw<PlatformNotSupportedException>().WithMessage("*dialog run loop*");
                creates.Should().Be(0); window.IsVisible.Should().BeFalse();
                PortableWindowActivationService.Register(activate: _ => activation,
                    getHandle: _ => new IntPtr(5678), runDialog: (_, _) => { });
                show.Should().Throw<PlatformNotSupportedException>().WithMessage("*dialog release completion*");
                creates.Should().Be(0); window.IsVisible.Should().BeFalse();
                PortableWindowActivationService.Register(activate: _ => activation,
                    getHandle: _ => new IntPtr(5678), runDialog: (_, _) => { },
                    hide: _ => hides++,
                    releaseDialog: (_, completed) => completed());
                show.Should().Throw<InvalidOperationException>().WithMessage("*still open*");
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                window.IsVisible.Should().BeFalse();
                window.IsDisposed.Should().BeFalse();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                hides.Should().Be(1);
                var failure = new InvalidOperationException("Host event pump failure.");
                PortableWindowActivationService.Register(activate: _ => activation,
                    hide: _ => hides++,
                    runDialog: (_, _) => throw failure, releaseDialog: (_, completed) => completed());
                show.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                window.IsVisible.Should().BeFalse();
                window.IsDisposed.Should().BeFalse();
                hides.Should().Be(2);
                var hideFailure = new InvalidOperationException("Host hide failure.");
                PortableWindowActivationService.Register(activate: _ => activation,
                    hide: _ => throw hideFailure,
                    runDialog: (_, _) => throw failure, releaseDialog: (_, completed) => completed());
                var combined = show.Should().Throw<AggregateException>().Which;
                combined.InnerExceptions.Should().Contain(failure).And.Contain(hideFailure);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
                PortableModalInputScope.IsActive.Should().BeFalse();
                // Repair the deliberately rejected hide before the next case.
                PortableWindowActivationService.Register(activate: _ => activation, hide: _ => { });
                window.Hide();
                PortableWindowActivationService.Register(activate: _ => activation);
                show.Should().Throw<PlatformNotSupportedException>(); // Clear old optional callback.
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void NestedPortableDialogScopesRestoreTheOuterInvocation()
    {
        RunInUiApartment(() =>
        {
            var outer = new Window { Width = 200, Height = 100 };
            var inner = new Window { Width = 100, Height = 100 };
            int runs = 0;
            PortableWindowActivationService.Register(activate: window => window,
                getHandle: owner => ReferenceEquals(owner, outer) ? new IntPtr(5678) : new IntPtr(5679),
                releaseDialog: (_, completed) => completed(),
                runDialog: (owner, continuation) =>
                {
                    runs++;
                    ComponentDispatcher.IsThreadModal.Should().BeTrue();
                    continuation().Should().BeTrue();
                    if (ReferenceEquals(owner, outer))
                    {
                        inner.ShowDialog().Should().Be(false);
                        ComponentDispatcher.IsThreadModal.Should().BeTrue();
                        continuation().Should().BeTrue();
                    }
                    ((Window)owner).Hide();
                    continuation().Should().BeFalse();
                });
            try
            {
                outer.ShowDialog().Should().Be(false);
                runs.Should().Be(2);
                ComponentDispatcher.IsThreadModal.Should().BeFalse();
            }
            finally
            {
                inner.Close(); outer.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void ModalInputAdmissionPreservesEnabledStateAndRejectsOtherSourceReports()
    {
        RunInUiApartment(() =>
        {
            var owner = new Window { Width = 200, Height = 100, IsEnabled = false };
            var dialog = new Window { Width = 200, Height = 100 };
            using IPortablePresentationSourceHost ownerHost = PortablePresentationSourceHost.Create();
            ownerHost.RootVisual = owner;
            try
            {
                using (PortableModalInputScope.Enter(dialog))
                {
                    PortableWindowActivationService.IsModalInputAllowed(owner).Should().BeFalse();
                    PortableWindowActivationService.IsModalInputAllowed(dialog).Should().BeTrue();
                    PortableWindowActivationService.IsModalInputAllowed(new HitTestElement()).Should().BeFalse();
                    var input = new PortableInputEventArgs(PortableInputEventKind.MouseDown,
                        x: 10, y: 10, button: PortableMouseButton.Left);
                    PortableWindowActivationService.ProcessInput((PresentationSource)ownerHost, input);
                    input.Handled.Should().BeTrue();
                    owner.IsEnabled = true; // Application intent does not remove the modal restriction.
                    PortableWindowActivationService.IsModalInputAllowed(owner).Should().BeFalse();
                    PortableWindowActivationService.ProcessDragDropEvent(owner, 0,
                        Array.Empty<string>(), "blocked", 10, 10, 1, 1).Should().Be(0);
                }
                owner.IsEnabled.Should().BeTrue();
                PortableWindowActivationService.IsModalInputAllowed(owner).Should().BeTrue();
            }
            finally { ownerHost.RootVisual = null; owner.Close(); dialog.Close(); }
        });
    }

    [PortableInputFact]
    public void ModalEntryReleasesCaptureFromAnUnownedSource()
    {
        RunInUiApartment(() =>
        {
            using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
            var root = new HitTestElement { Focusable = true };
            host.RootVisual = root; host.SetClientSize(200, 100);
            Mouse.Capture(root, CaptureMode.Element).Should().BeTrue();
            using (PortableModalInputScope.Enter(new object()))
            {
                PortableWindowActivationService.PrepareForModalInput();
                Mouse.Captured.Should().BeNull();
            }
            Mouse.Capture(null);
        });
    }

    [PortableInputFact]
    public void ModalReportCannotBeRedirectedToBlockedCapture()
    {
        RunInUiApartment(() =>
        {
            using var capturedHost = PortablePresentationSourceHost.Create();
            using var dialogHost = PortablePresentationSourceHost.Create();
            var captured = new HitTestElement();
            var dialog = new Window { Width = 200, Height = 100 };
            capturedHost.RootVisual = captured; capturedHost.SetClientSize(200, 100);
            dialogHost.RootVisual = dialog;
            int moves = 0;
            captured.MouseMove += (_, _) => moves++;
            try
            {
                Mouse.Capture(captured, CaptureMode.Element).Should().BeTrue();
                using (PortableModalInputScope.Enter(dialog))
                {
                    var input = new PortableInputEventArgs(PortableInputEventKind.MouseMove, x: 10, y: 10);
                    PortableWindowActivationService.ProcessInput((PresentationSource)dialogHost, input);
                    input.Handled.Should().BeTrue();
                    moves.Should().Be(0);
                }
            }
            finally { Mouse.Capture(null); dialogHost.RootVisual = null; dialog.Close(); }
        });
    }

    [Fact]
    public void RegisteredHostRejectionDoesNotCreateAWindowsMilWindow()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(activate: _ => null!);
            var window = new Window();
            int initialized = 0;
            window.SourceInitialized += (_, _) => initialized++;
            try
            {
                Action show = window.Show;
                show.Should().Throw<InvalidOperationException>()
                    .WithMessage("*Falling back to Windows MIL is not permitted*");
                initialized.Should().Be(0);
                window.PortableWindowActivation.Should().BeNull();
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ActivePortableWindowRequiresItsHostRunLoop()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(activate: _ => new object());
            var window = new Window { Width = 200, Height = 100 };
            try
            {
                window.Show();
                Action run = () => PortableWindowActivationService.TryRun(window);
                run.Should().Throw<InvalidOperationException>()
                    .WithMessage("*no run-loop callback*");
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void RejectedActivationRequestDoesNotFabricateActiveState()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(
                activate: _ => new object(), requestActivation: _ => false);
            var window = new Window { Width = 200, Height = 100 };
            int activations = 0;
            window.Activated += (_, _) => activations++;
            try
            {
                window.Show();
                window.Activate().Should().BeFalse();
                window.IsActive.Should().BeFalse();
                activations.Should().Be(0);
                PortableWindowActivationService.SetActivationState(window, true);
                window.IsActive.Should().BeTrue();
                activations.Should().Be(1);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void CapturedElementReceivesMouseInputReportedByAnotherPresentationSource()
    {
        RunInUiApartment(VerifyCapturedElementReceivesMouseInputReportedByAnotherPresentationSource);
    }

    [Fact]
    public void SubtreeCapturePreservesReportedPresentationSourceAndCapture()
    {
        RunInUiApartment(VerifySubtreeCapturePreservesReportedPresentationSourceAndCapture);
    }

    private static void VerifyCapturedElementReceivesMouseInputReportedByAnotherPresentationSource()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();

        using IPortablePresentationSourceHost captureSourceHost = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost reportedSourceHost = PortablePresentationSourceHost.Create();
        var captureSource = (PresentationSource)captureSourceHost;
        var reportedSource = (PresentationSource)reportedSourceHost;
        var captureRoot = new HitTestElement();
        var reportedRoot = new HitTestElement();
        captureSourceHost.RootVisual = captureRoot;
        reportedSourceHost.RootVisual = reportedRoot;
        captureSourceHost.SetClientSize(500.0, 500.0);
        reportedSourceHost.SetClientSize(500.0, 500.0);
        captureSourceHost.SetClientOrigin(100.0, 200.0);
        reportedSourceHost.SetClientOrigin(400.0, 500.0);

        ProcessInput(
            activationService,
            captureSource,
            MouseDownInputKind,
            x: 20.0,
            y: 30.0,
            button: LeftMouseButton);
        captureRoot.CaptureMouse().Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(captureRoot);

        int capturedMoveCount = 0;
        int capturedUpCount = 0;
        int reportedMoveCount = 0;
        int reportedUpCount = 0;
        int lostCaptureCount = 0;
        Point capturedMovePoint = default;
        captureRoot.MouseMove += (_, e) =>
        {
            capturedMoveCount++;
            capturedMovePoint = e.GetPosition(captureRoot);
        };
        captureRoot.MouseUp += (_, _) => capturedUpCount++;
        captureRoot.LostMouseCapture += (_, _) => lostCaptureCount++;
        reportedRoot.MouseMove += (_, _) => reportedMoveCount++;
        reportedRoot.MouseUp += (_, _) => reportedUpCount++;
        try
        {
            ProcessInput(
                activationService,
                reportedSource,
                MouseMoveInputKind,
                x: 5.0,
                y: 7.0);
            ProcessInput(
                activationService,
                reportedSource,
                MouseUpInputKind,
                x: 5.0,
                y: 7.0,
                button: LeftMouseButton);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(captureSource);
            Mouse.Captured.Should().BeSameAs(captureRoot);
            capturedMoveCount.Should().Be(1);
            capturedUpCount.Should().Be(1);
            capturedMovePoint.X.Should().BeApproximately(305.0, 0.000001);
            capturedMovePoint.Y.Should().BeApproximately(307.0, 0.000001);
            reportedMoveCount.Should().Be(0);
            reportedUpCount.Should().Be(0);
            lostCaptureCount.Should().Be(0);
        }
        finally
        {
            captureRoot.ReleaseMouseCapture();
        }

        Mouse.Captured.Should().BeNull();
        lostCaptureCount.Should().Be(1);
    }

    private static void VerifySubtreeCapturePreservesReportedPresentationSourceAndCapture()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();

        using IPortablePresentationSourceHost captureSourceHost = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost reportedSourceHost = PortablePresentationSourceHost.Create();
        var captureSource = (PresentationSource)captureSourceHost;
        var reportedSource = (PresentationSource)reportedSourceHost;
        var captureRoot = new HitTestElement();
        var reportedRoot = new HitTestElement(captureRoot);
        captureSourceHost.RootVisual = captureRoot;
        reportedSourceHost.RootVisual = reportedRoot;
        captureSourceHost.SetClientSize(500.0, 500.0);
        reportedSourceHost.SetClientSize(500.0, 500.0);

        ProcessInput(
            activationService,
            captureSource,
            MouseMoveInputKind,
            x: 20.0,
            y: 30.0);
        Mouse.Capture(captureRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(captureRoot);

        int reportedMoveCount = 0;
        object? originalSource = null;
        reportedRoot.MouseMove += (_, e) =>
        {
            reportedMoveCount++;
            originalSource = e.OriginalSource;
        };
        try
        {
            ProcessInput(
                activationService,
                reportedSource,
                MouseMoveInputKind,
                x: 5.0,
                y: 7.0);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(reportedSource);
            Mouse.Captured.Should().BeSameAs(captureRoot);
            reportedMoveCount.Should().Be(1);
            originalSource.Should().BeSameAs(reportedRoot);
        }
        finally
        {
            Mouse.Capture(null);
        }

        Mouse.Captured.Should().BeNull();
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        })
        {
            IsBackground = true
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Portable WPF input test did not complete within 30 seconds.");
        }

        if (exception != null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static void ProcessInput(
        IPortableWindowActivationServiceRegistrar activationService,
        PresentationSource source,
        int kind,
        double x,
        double y,
        int button = 0)
    {
        activationService.TryProcessPresentationSourceInputEvent(
            source,
            new PortableWindowInputEvent(kind, x: x, y: y, button: button)).Should().BeTrue();
    }

    private sealed class HitTestElement : UIElement
    {
        private readonly DependencyObject? _uiParent;

        public HitTestElement(DependencyObject? uiParent = null)
        {
            _uiParent = uiParent;
        }

        protected override DependencyObject GetUIParentCore()
        {
            return _uiParent ?? base.GetUIParentCore();
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }
    }
}
