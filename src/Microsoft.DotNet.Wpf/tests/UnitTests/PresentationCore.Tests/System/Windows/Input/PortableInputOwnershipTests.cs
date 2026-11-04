// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Interop;
using ProGPU.Wpf.Interop;

namespace System.Windows.Input;

[Collection("Sequential")]
public sealed class PortableInputOwnershipTests
{
    private sealed class PortableInputFactAttribute : FactAttribute
    {
        public PortableInputFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input-manager construction, including on Windows.";
        }
    }

    private sealed class WindowsInputFactAttribute : FactAttribute
    {
        public WindowsInputFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (!OperatingSystem.IsWindows() || PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.WindowsMil)
                Skip = "Requires the independently selected native Windows WPF lane.";
        }
    }

    [WindowsInputFact]
    public void WindowsMilKeepsNativeInputDevices()
    {
        RunInUiApartment(() =>
        {
            Assert.False(InputManager.Current.UsesPortableInput);
            Assert.IsType<Win32KeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            Assert.IsType<Win32MouseDevice>(InputManager.Current.PrimaryMouseDevice);
        });
    }

    [PortableInputFact]
    public void FrozenPortableBackendSelectsHostOwnedDevices()
    {
        RunInUiApartment(() =>
        {
            var manager = InputManager.Current;
            Assert.True(manager.UsesPortableInput);
            var keyboard = Assert.IsType<PortableKeyboardDevice>(manager.PrimaryKeyboardDevice);
            var mouse = Assert.IsType<PortableMouseDevice>(manager.PrimaryMouseDevice);
            using var source = new PortablePresentationSource();
            keyboard.SetKeyStates(Key.A, KeyStates.Down);
            Assert.True(keyboard.IsKeyDown(Key.A));
            keyboard.SetKeyStates(Key.A, KeyStates.None);
            Assert.False(keyboard.IsKeyDown(Key.A));
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, source, source);
            Assert.Equal(MouseButtonState.Pressed, mouse.LeftButton);
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Released, source, source);
            Assert.Equal(MouseButtonState.Released, mouse.LeftButton);
            if (OperatingSystem.IsWindows())
                Assert.Throws<InvalidOperationException>(() => PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.WindowsMil));
            else
                Assert.Throws<PlatformNotSupportedException>(() => PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.WindowsMil));
        });
    }

    [PortableInputFact]
    public void PointerSourceButtonsRetainBothOriginAndCaptureRoute()
    {
        RunInUiApartment(() =>
        {
            var mouse = Assert.IsType<PortableMouseDevice>(InputManager.Current.PrimaryMouseDevice);
            using var origin = new PortablePresentationSource();
            using var routed = new PortablePresentationSource();
            using var other = new PortablePresentationSource();
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, origin, routed);
            mouse.SetButtonState(MouseButton.Right, MouseButtonState.Pressed, other, other);
            mouse.ReleaseSourceButtons(origin);
            Assert.Equal(MouseButtonState.Released, mouse.LeftButton);
            Assert.Equal(MouseButtonState.Pressed, mouse.RightButton);
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, origin, routed);
            mouse.ReleaseSourceButtons(routed);
            Assert.Equal(MouseButtonState.Released, mouse.LeftButton);
            Assert.Equal(MouseButtonState.Pressed, mouse.RightButton);

            foreach (MouseButton button in Enum.GetValues<MouseButton>())
                mouse.SetButtonState(button, MouseButtonState.Pressed, origin, routed);
            mouse.ReleaseSourceButtons(origin);
            foreach (MouseButton button in Enum.GetValues<MouseButton>())
                Assert.Equal(MouseButtonState.Released, mouse.GetButtonStateFromSystem(button));
        });
    }

    [PortableInputFact]
    public void PointerSourceCancellationDoesNotClearLaterPressButRealUpDoes()
    {
        RunInUiApartment(() =>
        {
            var mouse = Assert.IsType<PortableMouseDevice>(InputManager.Current.PrimaryMouseDevice);
            using var first = new PortablePresentationSource();
            using var second = new PortablePresentationSource();
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, first, first);
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, second, second);
            mouse.ReleaseSourceButtons(first);
            Assert.Equal(MouseButtonState.Pressed, mouse.LeftButton);
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Released, first, first);
            Assert.Equal(MouseButtonState.Released, mouse.LeftButton);
            // An unmatched real up is still delivered; it does not fabricate a down.
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Released, first, first);
            Assert.Equal(MouseButtonState.Released, mouse.LeftButton);
        });
    }

    [PortableInputFact]
    public void PointerSourceInvalidOrDisposedReportsCannotReplaceLivePresses()
    {
        RunInUiApartment(() =>
        {
            var mouse = Assert.IsType<PortableMouseDevice>(InputManager.Current.PrimaryMouseDevice);
            using var live = new PortablePresentationSource();
            using var retired = new PortablePresentationSource();
            retired.Dispose();
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, live, live);
            Assert.Throws<ObjectDisposedException>(() => mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, retired, live));
            Assert.Throws<ObjectDisposedException>(() => mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed, live, retired));
            Assert.Throws<ArgumentOutOfRangeException>(() => mouse.SetButtonState(MouseButton.Left, (MouseButtonState)2, live, live));
            Assert.Throws<ArgumentOutOfRangeException>(() => mouse.SetButtonState((MouseButton)5, MouseButtonState.Pressed, live, live));
            Assert.Equal(MouseButtonState.Pressed, mouse.LeftButton);
            mouse.ReleaseSourceButtons(retired);
            Assert.Equal(MouseButtonState.Pressed, mouse.LeftButton);
        });
    }

    [PortableInputFact]
    public void HwndEventModifierQueriesUseThePortableSnapshotAndRestorePhysicalState()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            keyboard.SetKeyStates(Key.RightCtrl, KeyStates.Down);
            Assert.Equal(ModifierKeys.Control, HwndKeyboardInputProvider.GetSystemModifierKeys());
            using (keyboard.PushEventModifiers(ModifierKeys.Shift | ModifierKeys.Alt))
            {
                Assert.Equal(ModifierKeys.Shift | ModifierKeys.Alt, HwndKeyboardInputProvider.GetSystemModifierKeys());
                using (keyboard.PushEventModifiers(ModifierKeys.None))
                {
                    keyboard.SetKeyStates(Key.RightCtrl, KeyStates.None);
                    Assert.Equal(ModifierKeys.None, HwndKeyboardInputProvider.GetSystemModifierKeys());
                }
                Assert.Equal(ModifierKeys.Shift | ModifierKeys.Alt, HwndKeyboardInputProvider.GetSystemModifierKeys());
            }
            Assert.Equal(ModifierKeys.None, HwndKeyboardInputProvider.GetSystemModifierKeys());
        });
    }

    [PortableInputFact]
    public void HwndEventModifiersClassifyControlTextWithoutWin32State()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            foreach (ModifierKeys modifiers in new[] { ModifierKeys.None, ModifierKeys.Shift,
                ModifierKeys.Control, ModifierKeys.Control | ModifierKeys.Shift, ModifierKeys.Alt,
                ModifierKeys.Control | ModifierKeys.Alt, ModifierKeys.Shift | ModifierKeys.Alt,
                ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt })
            {
                using var scope = keyboard.PushEventModifiers(modifiers);
                bool control = modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift);
                Assert.Equal(control, HwndKeyboardInputProvider.IsControlCharacter('\x03'));
                Assert.Equal(control, HwndKeyboardInputProvider.IsControlCharacter('\t'));
                Assert.Equal(control, HwndKeyboardInputProvider.IsControlCharacter('\u0085'));
                Assert.False(HwndKeyboardInputProvider.IsControlCharacter('a'));
                Assert.False(HwndKeyboardInputProvider.IsControlCharacter('\u03a9'));
            }
        });
    }

    [PortableInputFact]
    public void HwndEventModifiersKeepNestedTextAndExceptionScopesIndependent()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            keyboard.SetKeyStates(Key.LeftCtrl, KeyStates.Down);
            using (keyboard.PushEventModifiers(ModifierKeys.Alt))
            {
                Assert.False(HwndKeyboardInputProvider.IsControlCharacter('\x03'));
                Action nestedFailure = () =>
                {
                    using var scope = keyboard.PushEventModifiers(ModifierKeys.Control);
                    Assert.True(HwndKeyboardInputProvider.IsControlCharacter('\x03'));
                    keyboard.SetKeyStates(Key.LeftCtrl, KeyStates.None);
                    throw new InvalidOperationException("Nested input callback failed.");
                };
                Assert.Throws<InvalidOperationException>(nestedFailure);
                Assert.Equal(ModifierKeys.Alt, HwndKeyboardInputProvider.GetSystemModifierKeys());
                Assert.False(HwndKeyboardInputProvider.IsControlCharacter('\x03'));
            }
            Assert.Equal(ModifierKeys.None, HwndKeyboardInputProvider.GetSystemModifierKeys());
            Assert.False(HwndKeyboardInputProvider.IsControlCharacter('\x03'));
        });
    }

    [PortableInputFact]
    public void EventModifiersDoNotRewritePhysicalKeysOrToggleState()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            keyboard.SetKeyStates(Key.RightCtrl, KeyStates.Down);
            keyboard.SetKeyStates(Key.A, KeyStates.Down);
            keyboard.SetKeyStates(Key.CapsLock, KeyStates.Toggled);
            using (keyboard.PushEventModifiers(ModifierKeys.Shift | ModifierKeys.Alt))
            {
                Assert.Equal(ModifierKeys.Shift | ModifierKeys.Alt, Keyboard.Modifiers);
                Assert.True(keyboard.IsKeyDown(Key.RightCtrl));
                Assert.False(keyboard.IsKeyDown(Key.LeftShift)); // Aggregate input has no side identity.
                Assert.True(keyboard.IsKeyDown(Key.A));
                Assert.True(keyboard.IsKeyToggled(Key.CapsLock));
            }
            Assert.Equal(ModifierKeys.Control, Keyboard.Modifiers);
            Assert.True(keyboard.IsKeyToggled(Key.CapsLock));
        });
    }

    [PortableInputFact]
    public void NestedEventModifiersRestoreSnapshotsWithoutResurrectingReleasedKeys()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            keyboard.SetKeyStates(Key.LeftCtrl, KeyStates.Down);
            using (keyboard.PushEventModifiers(ModifierKeys.Shift))
            {
                using (keyboard.PushEventModifiers(ModifierKeys.None))
                {
                    keyboard.SetKeyStates(Key.LeftCtrl, KeyStates.None);
                    Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
                }
                Assert.Equal(ModifierKeys.Shift, Keyboard.Modifiers);
                Assert.False(keyboard.IsKeyDown(Key.LeftCtrl));
            }
            Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
        });
    }

    [PortableInputFact]
    public void EventModifierScopesRestoreOnFailureAndRejectUnknownFlagsAtomically()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            keyboard.SetKeyStates(Key.RightAlt, KeyStates.Down);
            Action fail = () =>
            {
                using var scope = keyboard.PushEventModifiers(ModifierKeys.Control);
                Assert.Throws<ArgumentOutOfRangeException>(() => keyboard.PushEventModifiers((ModifierKeys)16));
                Assert.Equal(ModifierKeys.Control, Keyboard.Modifiers);
                throw new InvalidOperationException("Source callback failed.");
            };
            Assert.Throws<InvalidOperationException>(fail);
            Assert.Equal(ModifierKeys.Alt, Keyboard.Modifiers);
        });
    }

    [PortableInputFact]
    public void EventModifierScopesRejectOutOfOrderAndStaleRelease()
    {
        RunInUiApartment(() =>
        {
            var keyboard = Assert.IsType<PortableKeyboardDevice>(InputManager.Current.PrimaryKeyboardDevice);
            var outer = keyboard.PushEventModifiers(ModifierKeys.Control);
            var stale = outer;
            var inner = keyboard.PushEventModifiers(ModifierKeys.Shift);
            try { outer.Dispose(); Assert.Fail("Out-of-order release was accepted."); }
            catch (InvalidOperationException) { }
            Assert.Equal(ModifierKeys.Shift, Keyboard.Modifiers);
            inner.Dispose();
            Assert.Equal(ModifierKeys.Control, Keyboard.Modifiers);
            outer.Dispose();
            outer.Dispose();
            using (keyboard.PushEventModifiers(ModifierKeys.Alt))
            {
                try { stale.Dispose(); Assert.Fail("Stale release was accepted."); }
                catch (InvalidOperationException) { }
                Assert.Equal(ModifierKeys.Alt, Keyboard.Modifiers);
            }
            Assert.Equal(ModifierKeys.None, Keyboard.Modifiers);
            PortableKeyboardDevice.EventModifierScope empty = default;
            empty.Dispose();
        });
    }

    [PortableInputFact]
    public void PortableFocusDoesNotAssociateWin32ContextsOrPretendToApplyPreferences()
    {
        RunInUiApartment(() =>
        {
            var method = InputMethod.Current;
            var element = new UIElement();
            InputManager.Current.PrimaryKeyboardDevice.TextServicesManager.Focus(element);
            method.EnableOrDisableInputMethod(false);
            method.EnableOrDisableInputMethod(true);
            method.GotKeyboardFocus(element);
            InputMethod.SetPreferredImeState(element, InputMethodState.On);
            Assert.Throws<PlatformNotSupportedException>(() => method.GotKeyboardFocus(element));
            InputMethod.SetPreferredImeState(element, InputMethodState.DoNotCare);
            InputMethod.SetPreferredImeConversionMode(element, ImeConversionModeValues.Native);
            Assert.Throws<PlatformNotSupportedException>(() => method.GotKeyboardFocus(element));
            InputMethod.SetPreferredImeConversionMode(element, ImeConversionModeValues.DoNotCare);
            InputMethod.SetPreferredImeSentenceMode(element, ImeSentenceModeValues.Conversation);
            Assert.Throws<PlatformNotSupportedException>(() => method.GotKeyboardFocus(element));
            InputMethod.SetPreferredImeSentenceMode(element, ImeSentenceModeValues.DoNotCare);
            method.GotKeyboardFocus(element);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRetainPacketAndFrameAcrossEverySplit()
    {
        RunInUiApartment(() =>
        {
            using var source = new PortablePresentationSource();
            var packet = new PortablePointerInput(PortablePointerEventKind.Down, 10.25, 20.75,
                1.25025, 0, 3, PortablePointerModifiers.Shift);
            var point = new Point(30.125, -4.875);
            RawMouseInputReport report = new PortableMouseInputReport(InputMode.Foreground, 1250, source,
                RawMouseActions.Activate | RawMouseActions.AbsoluteMove | RawMouseActions.Button1Press,
                30, -4, 0, (IntPtr)42, point, packet);
            foreach (RawMouseActions actions in new[] { RawMouseActions.Activate,
                RawMouseActions.AbsoluteMove | RawMouseActions.Button1Press,
                RawMouseActions.AbsoluteMove, RawMouseActions.Button1Press })
            {
                report = report.WithActions(actions, 0, 0, 0, IntPtr.Zero);
                Assert.IsType<PortableMouseInputReport>(report);
                Assert.Same(packet, report.NativePointer);
                Assert.Equal(point, report.ClientPoint);
                Assert.Equal(actions, report.Actions);
                Assert.Equal(1250, report.Timestamp);
                Assert.Same(source, report.InputSource);
                Assert.Equal(0, report.X);
                Assert.Equal(IntPtr.Zero, report.ExtraInformation);
            }
        });
    }

    [PortableInputFact]
    public void WheelEventStateRetainsEachConstructedDeltaAndTimestamp()
    {
        RunInUiApartment(() =>
        {
            int[] values = [int.MinValue, -240, -1, 0, 1, 120, int.MaxValue];
            var events = new MouseWheelEventArgs[values.Length];
            for (int i = 0; i < values.Length; i++)
                events[i] = new MouseWheelEventArgs(Mouse.PrimaryDevice, i + 10, values[i]);
            for (int i = 0; i < values.Length; i++)
            {
                Assert.Equal(values[i], events[i].Delta);
                Assert.Equal(i + 10, events[i].Timestamp);
                Assert.Same(Mouse.PrimaryDevice, events[i].MouseDevice);
            }
        });
    }

    [PortableInputFact]
    public void NativePointerReportExtensionKeepsLegacyStorageAndRejectsInvalidFrames()
    {
        RunInUiApartment(() =>
        {
            using var source = new PortablePresentationSource();
            var legacy = new RawMouseInputReport(InputMode.Foreground, 7, source,
                RawMouseActions.AbsoluteMove, -2, 3, 0, (IntPtr)42);
            var copy = legacy.WithActions(RawMouseActions.Button1Press, 0, 0, 0, IntPtr.Zero);
            Assert.IsType<RawMouseInputReport>(copy);
            Assert.Null(copy.NativePointer);
            Assert.Equal(new Point(-2, 3), legacy.ClientPoint);
            Assert.Equal(new Point(0, 0), copy.ClientPoint);
            Assert.Equal((IntPtr)42, legacy.ExtraInformation);
            var packet = new PortablePointerInput(PortablePointerEventKind.Move, 1, 2, 3, -1, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PortableMouseInputReport(InputMode.Foreground,
                3000, source, RawMouseActions.AbsoluteMove, 0, 0, 0, IntPtr.Zero, new Point(double.NaN, 0), packet));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PortableMouseInputReport(InputMode.Foreground,
                3000, source, RawMouseActions.AbsoluteMove, 0, 0, 0, IntPtr.Zero, new Point(0, double.PositiveInfinity), packet));
            Assert.IsType<MouseEventArgs>(PortableMouseEvents.Move(Mouse.PrimaryDevice, 7, null, null, Mouse.MouseMoveEvent));
            var button = PortableMouseEvents.Button(Mouse.PrimaryDevice, 7, MouseButton.Left, null, null, Mouse.MouseDownEvent);
            Assert.IsType<MouseButtonEventArgs>(button);
            Assert.Equal(1, button.ClickCount);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsDoNotRefreshAnObsoleteSourceGenerationWhenSplit()
    {
        RunInUiApartment(() =>
        {
            using var source = new PortablePresentationSource { RootVisual = new UIElement() };
            var packet = new PortablePointerInput(PortablePointerEventKind.Move, 1.25, 2.5, 3, -1, 0, 0);
            var report = new PortableMouseInputReport(InputMode.Foreground, 3000, source,
                RawMouseActions.AbsoluteMove, 1, 2, 0, IntPtr.Zero, new Point(1.25, 2.5), packet);
            Assert.True(report.IsCurrent);
            source.RootVisual = source.RootVisual;
            Assert.True(report.IsCurrent);
            source.RootVisual = new UIElement();
            Assert.False(report.IsCurrent);
            Assert.False(report.WithActions(RawMouseActions.AbsoluteMove, 1, 2, 0, IntPtr.Zero).IsCurrent);
            var current = new PortableMouseInputReport(InputMode.Foreground, 3000, source,
                RawMouseActions.AbsoluteMove, 1, 2, 0, IntPtr.Zero, new Point(1.25, 2.5), packet);
            Assert.True(current.IsCurrent);
            source.Dispose();
            Assert.False(current.IsCurrent);
        });
    }

    [PortableInputFact]
    public void NativePointerReportsRetainOriginAndDestinationGenerationsIndependently()
    {
        RunInUiApartment(() =>
        {
            using var origin = new PortablePresentationSource { RootVisual = new UIElement() };
            using var destination = new PortablePresentationSource { RootVisual = new UIElement() };
            var packet = new PortablePointerInput(PortablePointerEventKind.Down, 1.25, 2.5, 3, 0, 1, 0);
            var report = new PortableMouseInputReport(InputMode.Foreground, 3000, destination,
                RawMouseActions.AbsoluteMove | RawMouseActions.Button1Press, 1, 2, 0, IntPtr.Zero,
                new Point(1.25, 2.5), packet, origin);
            Assert.True(report.IsCurrent);
            origin.RootVisual = new UIElement();
            Assert.False(report.IsCurrent);
            Assert.False(report.WithActions(RawMouseActions.Button1Press, 0, 0, 0, IntPtr.Zero).IsCurrent);
            var current = new PortableMouseInputReport(InputMode.Foreground, 3000, destination,
                RawMouseActions.AbsoluteMove, 1, 2, 0, IntPtr.Zero, new Point(1.25, 2.5), packet, origin);
            Assert.True(current.IsCurrent);
            destination.RootVisual = new UIElement();
            Assert.False(current.IsCurrent);
            Assert.Same(packet, current.NativePointer);
        });
    }

    [PortableInputFact]
    public void WheelEventStateDoesNotBorrowAnotherDispatchersDelta()
    {
        RunInUiApartment(() =>
        {
            var first = new MouseWheelEventArgs(Mouse.PrimaryDevice, 10, 120);
            RunInUiApartment(() =>
            {
                var second = new MouseWheelEventArgs(Mouse.PrimaryDevice, 20, -240);
                Assert.Equal(-240, second.Delta);
                Assert.Equal(120, first.Delta);
            });
            Assert.Equal(120, first.Delta);
        });
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Portable input fixture timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
