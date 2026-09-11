// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
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
            keyboard.SetKeyStates(Key.A, KeyStates.Down);
            Assert.True(keyboard.IsKeyDown(Key.A));
            keyboard.SetKeyStates(Key.A, KeyStates.None);
            Assert.False(keyboard.IsKeyDown(Key.A));
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Pressed);
            Assert.Equal(MouseButtonState.Pressed, mouse.LeftButton);
            mouse.SetButtonState(MouseButton.Left, MouseButtonState.Released);
            Assert.Equal(MouseButtonState.Released, mouse.LeftButton);
            if (OperatingSystem.IsWindows())
                Assert.Throws<InvalidOperationException>(() => PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.WindowsMil));
            else
                Assert.Throws<PlatformNotSupportedException>(() => PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.WindowsMil));
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
