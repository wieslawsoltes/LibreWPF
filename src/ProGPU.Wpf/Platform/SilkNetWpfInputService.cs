using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.ExceptionServices;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Silk.NET.Windowing;
using SilkInput = Silk.NET.Input;

namespace System.Windows.Media.ProGPU.Platform;

internal interface ISilkNetWpfInputContextProvider
{
    bool TryGetInputContext(object window, out SilkInput.IInputContext inputContext);
}

public sealed class SilkNetWpfInputService : IWpfInputService, ISilkNetWpfInputContextProvider
{
    internal delegate bool MouseButtonPacketReader(out NativeMouseButtonPacket packet);
    private readonly object _sync = new();
    private readonly Dictionary<IView, SilkInput.IInputContext> _inputContexts = new();

    public event EventHandler<WpfInputEventArgs>? InputReceived;

    public IDisposable Attach(object window)
        => AttachWindow(window, readMouseButtonPacket: null);

    internal IDisposable Attach(IView window, SilkWindowController controller)
        => AttachWindow(window, controller.TryGetCurrentMouseButtonPacket);

    private IDisposable AttachWindow(object window, MouseButtonPacketReader? readMouseButtonPacket)
    {
        if (window is not IView silkView)
        {
            throw new ArgumentException("Silk.NET input services require a Silk.NET view instance.", nameof(window));
        }

        var inputContext = NativeWindowInput.CreateInput(silkView);
        try
        {
            IDisposable subscription = Attach(
                inputContext,
                silkView,
                () =>
                {
                    lock (_sync)
                    {
                        if (_inputContexts.TryGetValue(silkView, out var registeredContext) &&
                            ReferenceEquals(registeredContext, inputContext))
                        {
                            _inputContexts.Remove(silkView);
                        }
                    }
                },
                readMouseButtonPacket);
            lock (_sync)
            {
                _inputContexts[silkView] = inputContext;
            }

            return subscription;
        }
        catch
        {
            lock (_sync)
            {
                if (_inputContexts.TryGetValue(silkView, out var registeredContext) &&
                    ReferenceEquals(registeredContext, inputContext))
                {
                    _inputContexts.Remove(silkView);
                }
            }

            inputContext.Dispose();
            throw;
        }
    }

    public IDisposable Attach(SilkInput.IInputContext inputContext)
    {
        return Attach(inputContext, inputContext, onDispose: null);
    }

    internal IDisposable Attach(SilkInput.IInputContext inputContext, MouseButtonPacketReader readMouseButtonPacket)
        => Attach(inputContext, inputContext, onDispose: null, readMouseButtonPacket);

    bool ISilkNetWpfInputContextProvider.TryGetInputContext(object window, out SilkInput.IInputContext inputContext)
    {
        if (window is IView silkView)
        {
            lock (_sync)
            {
                if (_inputContexts.TryGetValue(silkView, out inputContext!))
                {
                    return true;
                }
            }
        }

        inputContext = null!;
        return false;
    }

    private IDisposable Attach(
        SilkInput.IInputContext inputContext,
        object eventSource,
        Action? onDispose,
        MouseButtonPacketReader? readMouseButtonPacket = null)
    {
        ArgumentNullException.ThrowIfNull(inputContext);

        var mouseSubscriptions = new Dictionary<SilkInput.IMouse, Action>();
        var keyboardSubscriptions = new Dictionary<SilkInput.IKeyboard, Action>();
        var nativePointer = inputContext as INativePointerInputContext;

        void AttachMouse(SilkInput.IMouse mouse)
        {
            if (nativePointer != null || mouseSubscriptions.ContainsKey(mouse))
            {
                return;
            }

            Vector2 lastPosition = default;
            bool hasLastPosition = false;
            var pressedButtons = new HashSet<SilkInput.MouseButton>();
            Action<SilkInput.IMouse, Vector2> mouseMove = (_, position) =>
            {
                lastPosition = position;
                hasLastPosition = IsFinite(position);
                OnInputReceived(eventSource, CreateMouseMoveEvent(position, ReadModifiers(inputContext)));
            };
            Action<SilkInput.IMouse, SilkInput.MouseButton> mouseDown = (_, button) =>
            {
                var position = ResolveButtonPosition(NativePointerEventKind.Down, button);
                if (IsFinite(position))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }

                pressedButtons.Add(button);
                OnInputReceived(eventSource, CreateMouseButtonEvent(WpfInputEventKind.MouseDown, button, position, ReadModifiers(inputContext)));
            };
            Action<SilkInput.IMouse, SilkInput.MouseButton> mouseUp = (_, button) =>
            {
                // The down/up sequence must be forwarded verbatim: synthetic input (OS-level
                // automation such as cliclick) can deliver a mouse-up without a matching down (the
                // injected down may have been dropped while the window was being activated, or the
                // press started before the input context was fully attached). Suppressing the up
                // leaves WPF's Mouse.LeftButton stuck in the Pressed state for the rest of the app
                // lifetime, which corrupts every later drag. Forward it unconditionally; WPF keeps
                // its own button-state bookkeeping.
                pressedButtons.Remove(button);
                var position = ResolveButtonPosition(NativePointerEventKind.Up, button);
                if (IsFinite(position))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }

                OnInputReceived(eventSource, CreateMouseButtonEvent(WpfInputEventKind.MouseUp, button, position, ReadModifiers(inputContext)));
            };
            Action<SilkInput.IMouse, SilkInput.ScrollWheel> scroll = (_, wheel) =>
            {
                var position = ResolveMousePosition(mouse.Position, lastPosition, hasLastPosition);
                if (IsFinite(position))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }

                OnInputReceived(eventSource, CreateMouseWheelEvent(wheel.X, wheel.Y, position, ReadModifiers(inputContext)));
            };

            Vector2 ResolveButtonPosition(NativePointerEventKind kind, SilkInput.MouseButton button)
            {
                if (readMouseButtonPacket != null && readMouseButtonPacket(out var packet) &&
                    packet.Kind == kind && MatchesNativeButton(button, packet.Button))
                    return new Vector2(packet.Position.X, packet.Position.Y);

                // Other providers and synthetic provider releases retain their
                // existing policy. A later global cursor query is not this packet.
                return ResolveMousePosition(mouse.Position, lastPosition, hasLastPosition);
            }

            mouse.MouseMove += mouseMove;
            mouse.MouseDown += mouseDown;
            mouse.MouseUp += mouseUp;
            mouse.Scroll += scroll;

            void Unsubscribe()
            {
                mouse.MouseMove -= mouseMove;
                mouse.MouseDown -= mouseDown;
                mouse.MouseUp -= mouseUp;
                mouse.Scroll -= scroll;
            }

            mouseSubscriptions.Add(mouse, Unsubscribe);
        }

        void DetachMouse(SilkInput.IMouse mouse)
        {
            if (!mouseSubscriptions.Remove(mouse, out var unsubscribe))
            {
                return;
            }

            unsubscribe();
        }

        void AttachKeyboard(SilkInput.IKeyboard keyboard)
        {
            if (keyboardSubscriptions.ContainsKey(keyboard))
            {
                return;
            }

            Action<SilkInput.IKeyboard, SilkInput.Key, int> keyDown = (_, key, scanCode) =>
                OnInputReceived(eventSource, CreateKeyEvent(WpfInputEventKind.KeyDown, key, scanCode, ReadModifiers(inputContext)));
            Action<SilkInput.IKeyboard, SilkInput.Key, int> keyUp = (_, key, scanCode) =>
                OnInputReceived(eventSource, CreateKeyEvent(WpfInputEventKind.KeyUp, key, scanCode, ReadModifiers(inputContext)));
            Action<SilkInput.IKeyboard, char> keyChar = (_, character) =>
                OnInputReceived(eventSource, CreateTextInputEvent(character, ReadModifiers(inputContext)));

            keyboard.BeginInput();
            keyboard.KeyDown += keyDown;
            keyboard.KeyUp += keyUp;
            keyboard.KeyChar += keyChar;

            void Unsubscribe()
            {
                keyboard.KeyDown -= keyDown;
                keyboard.KeyUp -= keyUp;
                keyboard.KeyChar -= keyChar;
                keyboard.EndInput();
            }

            keyboardSubscriptions.Add(keyboard, Unsubscribe);
        }

        void DetachKeyboard(SilkInput.IKeyboard keyboard)
        {
            if (!keyboardSubscriptions.Remove(keyboard, out var unsubscribe))
            {
                return;
            }

            unsubscribe();
        }

        var mice = inputContext.Mice;
        for (var i = 0; i < mice.Count; i++)
        {
            AttachMouse(mice[i]);
        }

        var keyboards = inputContext.Keyboards;
        for (var i = 0; i < keyboards.Count; i++)
        {
            AttachKeyboard(keyboards[i]);
        }

        void ConnectionChanged(SilkInput.IInputDevice device, bool connected)
        {
            switch (device)
            {
                case SilkInput.IMouse mouse when connected:
                    AttachMouse(mouse);
                    break;
                case SilkInput.IMouse mouse:
                    DetachMouse(mouse);
                    break;
                case SilkInput.IKeyboard keyboard when connected:
                    AttachKeyboard(keyboard);
                    break;
                case SilkInput.IKeyboard keyboard:
                    DetachKeyboard(keyboard);
                    break;
            }
        }

        inputContext.ConnectionChanged += ConnectionChanged;
        void NativePointerReceived(NativePointerEvent input) =>
            OnInputReceived(eventSource, CreateNativePointerEvent(input));
        if (nativePointer != null) nativePointer.PointerEvent += NativePointerReceived;

        return new InputSubscription(
            inputContext,
            mouseSubscriptions,
            keyboardSubscriptions,
            () => inputContext.ConnectionChanged -= ConnectionChanged,
            onDispose,
            nativePointer == null ? null : () => nativePointer.PointerEvent -= NativePointerReceived);
    }

    public static WpfInputEventArgs CreateNativePointerEvent(NativePointerEvent input)
    {
        var packet = new PortablePointerInput((PortablePointerEventKind)input.Kind,
            (PortablePointerScrollProtocol)input.ScrollProtocol,
            input.X, input.Y, input.Timestamp, input.Button, input.ClickCount,
            (PortablePointerModifiers)input.Modifiers, input.ScrollX, input.ScrollY,
            (PortablePointerScrollUnit)input.ScrollUnit, input.ScrollPhase, input.MomentumPhase);
        var shortcuts = NormalizeModifiersForCurrentPlatform((WpfInputModifiers)((int)input.Modifiers & 15));
        return new WpfInputEventArgs(packet, shortcuts);
    }

    public static WpfInputEventArgs CreateKeyEvent(
        WpfInputEventKind kind,
        SilkInput.Key key,
        int scanCode,
        WpfInputModifiers modifiers)
    {
        if (kind != WpfInputEventKind.KeyDown && kind != WpfInputEventKind.KeyUp)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Key events must be KeyDown or KeyUp.");
        }

        return new WpfInputEventArgs(
            kind,
            key: TranslateKey(key),
            scanCode: scanCode,
            modifiers: NormalizeModifiersForCurrentPlatform(modifiers));
    }

    public static WpfInputEventArgs CreateTextInputEvent(char character, WpfInputModifiers modifiers)
    {
        return new WpfInputEventArgs(
            WpfInputEventKind.TextInput,
            character: character,
            modifiers: NormalizeModifiersForCurrentPlatform(modifiers));
    }

    public static WpfInputEventArgs CreateMouseMoveEvent(Vector2 position, WpfInputModifiers modifiers)
    {
        return new WpfInputEventArgs(
            WpfInputEventKind.MouseMove,
            x: position.X,
            y: position.Y,
            modifiers: NormalizeModifiersForCurrentPlatform(modifiers));
    }

    public static WpfInputEventArgs CreateMouseButtonEvent(
        WpfInputEventKind kind,
        SilkInput.MouseButton button,
        Vector2 position,
        WpfInputModifiers modifiers)
    {
        if (kind != WpfInputEventKind.MouseDown && kind != WpfInputEventKind.MouseUp)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Mouse button events must be MouseDown or MouseUp.");
        }

        return new WpfInputEventArgs(
            kind,
            x: position.X,
            y: position.Y,
            button: TranslateMouseButton(button),
            modifiers: NormalizeModifiersForCurrentPlatform(modifiers));
    }

    public static WpfInputEventArgs CreateMouseWheelEvent(
        double deltaX,
        double deltaY,
        Vector2 position,
        WpfInputModifiers modifiers)
    {
        return new WpfInputEventArgs(
            WpfInputEventKind.MouseWheel,
            x: position.X,
            y: position.Y,
            deltaX: deltaX,
            deltaY: deltaY,
            modifiers: NormalizeModifiersForCurrentPlatform(modifiers));
    }

    public static WpfMouseButton TranslateMouseButton(SilkInput.MouseButton button)
    {
        return button switch
        {
            SilkInput.MouseButton.Left => WpfMouseButton.Left,
            SilkInput.MouseButton.Right => WpfMouseButton.Right,
            SilkInput.MouseButton.Middle => WpfMouseButton.Middle,
            SilkInput.MouseButton.Button4 => WpfMouseButton.XButton1,
            SilkInput.MouseButton.Button5 => WpfMouseButton.XButton2,
            _ => WpfMouseButton.Other
        };
    }

    public static string? TranslateKey(SilkInput.Key key)
    {
        if (key == SilkInput.Key.Unknown)
        {
            return null;
        }

        return key switch
        {
            SilkInput.Key.Backspace => "Back",
            SilkInput.Key.ShiftLeft => "LeftShift",
            SilkInput.Key.ShiftRight => "RightShift",
            SilkInput.Key.ControlLeft => "LeftCtrl",
            SilkInput.Key.ControlRight => "RightCtrl",
            SilkInput.Key.AltLeft => "LeftAlt",
            SilkInput.Key.AltRight => "RightAlt",
            SilkInput.Key.SuperLeft => OperatingSystem.IsMacOS() ? "LeftCtrl" : "LWin",
            SilkInput.Key.SuperRight => OperatingSystem.IsMacOS() ? "RightCtrl" : "RWin",
            SilkInput.Key.Number0 => "D0",
            SilkInput.Key.Number1 => "D1",
            SilkInput.Key.Number2 => "D2",
            SilkInput.Key.Number3 => "D3",
            SilkInput.Key.Number4 => "D4",
            SilkInput.Key.Number5 => "D5",
            SilkInput.Key.Number6 => "D6",
            SilkInput.Key.Number7 => "D7",
            SilkInput.Key.Number8 => "D8",
            SilkInput.Key.Number9 => "D9",
            SilkInput.Key.Keypad0 => "NumPad0",
            SilkInput.Key.Keypad1 => "NumPad1",
            SilkInput.Key.Keypad2 => "NumPad2",
            SilkInput.Key.Keypad3 => "NumPad3",
            SilkInput.Key.Keypad4 => "NumPad4",
            SilkInput.Key.Keypad5 => "NumPad5",
            SilkInput.Key.Keypad6 => "NumPad6",
            SilkInput.Key.Keypad7 => "NumPad7",
            SilkInput.Key.Keypad8 => "NumPad8",
            SilkInput.Key.Keypad9 => "NumPad9",
            _ => key.ToString()
        };
    }

    internal static WpfInputModifiers NormalizeModifiersForCurrentPlatform(WpfInputModifiers modifiers)
    {
        if (!OperatingSystem.IsMacOS() || (modifiers & WpfInputModifiers.Super) == 0)
        {
            return modifiers;
        }

        return (modifiers & ~WpfInputModifiers.Super) | WpfInputModifiers.Control;
    }

    private static WpfInputModifiers ReadModifiers(SilkInput.IInputContext inputContext)
    {
        var modifiers = WpfInputModifiers.None;

        var keyboards = inputContext.Keyboards;
        for (var i = 0; i < keyboards.Count; i++)
        {
            var keyboard = keyboards[i];
            if (IsKeyPressed(keyboard, SilkInput.Key.ShiftLeft) || IsKeyPressed(keyboard, SilkInput.Key.ShiftRight))
            {
                modifiers |= WpfInputModifiers.Shift;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.ControlLeft) || IsKeyPressed(keyboard, SilkInput.Key.ControlRight))
            {
                modifiers |= WpfInputModifiers.Control;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.AltLeft) || IsKeyPressed(keyboard, SilkInput.Key.AltRight))
            {
                modifiers |= WpfInputModifiers.Alt;
            }

            if (IsKeyPressed(keyboard, SilkInput.Key.SuperLeft) || IsKeyPressed(keyboard, SilkInput.Key.SuperRight))
            {
                modifiers |= WpfInputModifiers.Super;
            }
        }

        return modifiers;
    }

    private static bool IsKeyPressed(SilkInput.IKeyboard keyboard, SilkInput.Key key)
    {
        try
        {
            return keyboard.IsKeyPressed(key);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnInputReceived(object eventSource, WpfInputEventArgs args)
    {
        InputReceived?.Invoke(eventSource, args);
    }

    internal static Vector2 ResolveMousePosition(
        Vector2 currentPosition,
        Vector2 lastPosition,
        bool hasLastPosition)
    {
        if (hasLastPosition && IsFinite(lastPosition))
        {
            return lastPosition;
        }

        return IsFinite(currentPosition)
            ? currentPosition
            : Vector2.Zero;
    }

    private static bool MatchesNativeButton(SilkInput.MouseButton button, int nativeButton) => button switch
    {
        SilkInput.MouseButton.Left => nativeButton == 0,
        SilkInput.MouseButton.Right => nativeButton == 1,
        SilkInput.MouseButton.Middle => nativeButton == 2,
        SilkInput.MouseButton.Button4 => nativeButton == 3,
        SilkInput.MouseButton.Button5 => nativeButton == 4,
        _ => false
    };

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private sealed class InputSubscription : IDisposable
    {
        private readonly SilkInput.IInputContext _inputContext;
        private readonly Dictionary<SilkInput.IMouse, Action> _mouseSubscriptions;
        private readonly Dictionary<SilkInput.IKeyboard, Action> _keyboardSubscriptions;
        private readonly Action _unsubscribeConnectionChanged;
        private readonly Action? _onDispose;
        private readonly Action? _unsubscribeNativePointer;
        private bool _isDisposed;

        public InputSubscription(
            SilkInput.IInputContext inputContext,
            Dictionary<SilkInput.IMouse, Action> mouseSubscriptions,
            Dictionary<SilkInput.IKeyboard, Action> keyboardSubscriptions,
            Action unsubscribeConnectionChanged,
            Action? onDispose,
            Action? unsubscribeNativePointer)
        {
            _inputContext = inputContext;
            _mouseSubscriptions = mouseSubscriptions;
            _keyboardSubscriptions = keyboardSubscriptions;
            _unsubscribeConnectionChanged = unsubscribeConnectionChanged;
            _onDispose = onDispose;
            _unsubscribeNativePointer = unsubscribeNativePointer;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            Exception? failure = null;
            void Release(Action action)
            {
                try { action(); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
            }
            Release(_unsubscribeConnectionChanged);
            DisposeSubscriptions(_mouseSubscriptions, Release);
            DisposeSubscriptions(_keyboardSubscriptions, Release);
            if (_unsubscribeNativePointer == null && _onDispose != null) Release(_onDispose);
            // Keep the native subscription alive for the provider's cancellation.
            // Reentrant disposal is guarded, and a throwing source handler must
            // not prevent detachment or removal from the registered context map.
            Release(_inputContext.Dispose);
            if (_unsubscribeNativePointer != null)
            {
                Release(_unsubscribeNativePointer);
                if (_onDispose != null) Release(_onDispose);
            }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void DisposeSubscriptions<TDevice>(Dictionary<TDevice, Action> subscriptions, Action<Action> release)
            where TDevice : notnull
        {
            while (TryTakeFirstSubscription(subscriptions, out var device, out var unsubscribe))
            {
                subscriptions.Remove(device);
                release(unsubscribe);
            }
        }

        private static bool TryTakeFirstSubscription<TDevice>(
            Dictionary<TDevice, Action> subscriptions,
            out TDevice device,
            out Action unsubscribe)
            where TDevice : notnull
        {
            var subscriptionEnumerator = subscriptions.GetEnumerator();
            if (subscriptionEnumerator.MoveNext())
            {
                var subscription = subscriptionEnumerator.Current;
                device = subscription.Key;
                unsubscribe = subscription.Value;
                return true;
            }

            device = default!;
            unsubscribe = null!;
            return false;
        }
    }
}
