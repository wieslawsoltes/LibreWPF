using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Silk.NET.Input;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfInputServiceTests
{
    [Fact]
    public void CreateKeyEventNormalizesSilkKey()
    {
        var input = SilkNetWpfInputService.CreateKeyEvent(
            WpfInputEventKind.KeyDown,
            Key.A,
            scanCode: 38,
            WpfInputModifiers.Control | WpfInputModifiers.Shift);

        Assert.Equal(WpfInputEventKind.KeyDown, input.Kind);
        Assert.Equal("A", input.Key);
        Assert.Equal(38, input.ScanCode);
        Assert.Equal(WpfInputModifiers.Control | WpfInputModifiers.Shift, input.Modifiers);
    }

    [Theory]
    [InlineData(Key.Backspace, "Back")]
    [InlineData(Key.Enter, "Enter")]
    [InlineData(Key.Tab, "Tab")]
    [InlineData(Key.Escape, "Escape")]
    [InlineData(Key.Left, "Left")]
    [InlineData(Key.Right, "Right")]
    [InlineData(Key.Up, "Up")]
    [InlineData(Key.Down, "Down")]
    [InlineData(Key.F7, "F7")]
    [InlineData(Key.Number1, "D1")]
    [InlineData(Key.Keypad1, "NumPad1")]
    [InlineData(Key.ShiftLeft, "LeftShift")]
    [InlineData(Key.ShiftRight, "RightShift")]
    [InlineData(Key.ControlLeft, "LeftCtrl")]
    [InlineData(Key.ControlRight, "RightCtrl")]
    [InlineData(Key.AltLeft, "LeftAlt")]
    [InlineData(Key.AltRight, "RightAlt")]
    public void TranslateKeyMapsSilkNamesToPortableWpfKeyNames(Key silkKey, string expected)
    {
        Assert.Equal(expected, SilkNetWpfInputService.TranslateKey(silkKey));
    }

    [Theory]
    [InlineData(Key.SuperLeft, "LeftCtrl", "LWin")]
    [InlineData(Key.SuperRight, "RightCtrl", "RWin")]
    public void TranslateKeyMapsMacCommandToWpfControl(Key silkKey, string macExpected, string otherExpected)
    {
        Assert.Equal(
            OperatingSystem.IsMacOS() ? macExpected : otherExpected,
            SilkNetWpfInputService.TranslateKey(silkKey));
    }

    [Fact]
    public void NormalizeModifiersMapsMacCommandToWpfControl()
    {
        var normalized = SilkNetWpfInputService.NormalizeModifiersForCurrentPlatform(
            WpfInputModifiers.Super | WpfInputModifiers.Shift);

        Assert.Equal(
            OperatingSystem.IsMacOS()
                ? WpfInputModifiers.Control | WpfInputModifiers.Shift
                : WpfInputModifiers.Super | WpfInputModifiers.Shift,
            normalized);
    }

    [Fact]
    public void TranslateKeyMapsUnknownToNull()
    {
        Assert.Null(SilkNetWpfInputService.TranslateKey(Key.Unknown));
    }

    [Fact]
    public void CreateTextInputEventStoresCharacter()
    {
        var input = SilkNetWpfInputService.CreateTextInputEvent('x', WpfInputModifiers.Alt);

        Assert.Equal(WpfInputEventKind.TextInput, input.Kind);
        Assert.Equal('x', input.Character);
        Assert.Equal(WpfInputModifiers.Alt, input.Modifiers);
    }

    [Theory]
    [InlineData(MouseButton.Left, WpfMouseButton.Left)]
    [InlineData(MouseButton.Right, WpfMouseButton.Right)]
    [InlineData(MouseButton.Middle, WpfMouseButton.Middle)]
    [InlineData(MouseButton.Button4, WpfMouseButton.XButton1)]
    [InlineData(MouseButton.Button5, WpfMouseButton.XButton2)]
    [InlineData(MouseButton.Unknown, WpfMouseButton.Other)]
    public void TranslateMouseButtonMapsCommonButtons(MouseButton silkButton, WpfMouseButton expected)
    {
        Assert.Equal(expected, SilkNetWpfInputService.TranslateMouseButton(silkButton));
    }

    [Fact]
    public void CreateMouseButtonEventStoresPositionButtonAndModifiers()
    {
        var input = SilkNetWpfInputService.CreateMouseButtonEvent(
            WpfInputEventKind.MouseDown,
            MouseButton.Button4,
            new Vector2(12, 34),
            WpfInputModifiers.Super);

        Assert.Equal(WpfInputEventKind.MouseDown, input.Kind);
        Assert.Equal(WpfMouseButton.XButton1, input.Button);
        Assert.Equal(12, input.X);
        Assert.Equal(34, input.Y);
        Assert.Equal(
            OperatingSystem.IsMacOS() ? WpfInputModifiers.Control : WpfInputModifiers.Super,
            input.Modifiers);
    }

    [Fact]
    public void ResolveMousePositionPrefersLastMouseMoveWhenAvailable()
    {
        var position = SilkNetWpfInputService.ResolveMousePosition(
            currentPosition: Vector2.Zero,
            lastPosition: new Vector2(120, 80),
            hasLastPosition: true);

        Assert.Equal(new Vector2(120, 80), position);
    }

    [Fact]
    public void ResolveMousePositionUsesCurrentPositionBeforeFirstMouseMove()
    {
        var position = SilkNetWpfInputService.ResolveMousePosition(
            currentPosition: new Vector2(42, 24),
            lastPosition: Vector2.Zero,
            hasLastPosition: false);

        Assert.Equal(new Vector2(42, 24), position);
    }

    [Fact]
    public void ResolveMousePositionFallsBackToZeroForInvalidPositions()
    {
        var position = SilkNetWpfInputService.ResolveMousePosition(
            currentPosition: new Vector2(float.NaN, 24),
            lastPosition: new Vector2(12, float.PositiveInfinity),
            hasLastPosition: true);

        Assert.Equal(Vector2.Zero, position);
    }

    [Fact]
    public void CreateMouseWheelEventStoresPositionAndDeltas()
    {
        var input = SilkNetWpfInputService.CreateMouseWheelEvent(
            deltaX: 1,
            deltaY: -2,
            new Vector2(5, 6),
            WpfInputModifiers.None);

        Assert.Equal(WpfInputEventKind.MouseWheel, input.Kind);
        Assert.Equal(5, input.X);
        Assert.Equal(6, input.Y);
        Assert.Equal(1, input.DeltaX);
        Assert.Equal(-2, input.DeltaY);
    }

    [Fact]
    public void AttachStartsAndStopsKeyboardTextInput()
    {
        var keyboard = new FakeKeyboard();
        var context = new FakeInputContext();
        context.AddInitialKeyboard(keyboard);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);

        using (service.Attach(context))
        {
            Assert.Equal(1, keyboard.BeginInputCount);

            keyboard.RaiseKeyChar('x');

            var input = Assert.Single(received);
            Assert.Equal(WpfInputEventKind.TextInput, input.Kind);
            Assert.Equal('x', input.Character);
        }

        Assert.Equal(1, keyboard.EndInputCount);
        keyboard.RaiseKeyChar('y');
        Assert.Single(received);
        Assert.True(context.IsDisposed);
    }

    [Fact]
    public void AttachSubscribesDevicesConnectedAfterInitialAttach()
    {
        var context = new FakeInputContext();
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);

        using var subscription = service.Attach(context);
        var mouse = new FakeMouse { Position = new Vector2(42, 24) };
        var keyboard = new FakeKeyboard();

        context.Connect(mouse);
        context.Connect(keyboard);

        mouse.RaiseMouseDown(MouseButton.Left);
        keyboard.RaiseKeyDown(Key.R, scanCode: 15);

        Assert.Collection(
            received,
            first =>
            {
                Assert.Equal(WpfInputEventKind.MouseDown, first.Kind);
                Assert.Equal(WpfMouseButton.Left, first.Button);
                Assert.Equal(42, first.X);
                Assert.Equal(24, first.Y);
            },
            second =>
            {
                Assert.Equal(WpfInputEventKind.KeyDown, second.Kind);
                Assert.Equal("R", second.Key);
                Assert.Equal(15, second.ScanCode);
            });
    }

    [Fact]
    public void AttachSubscribesInitialDevicesEvenWhenConnectionFlagIsNotSet()
    {
        var mouse = new FakeMouse { Position = new Vector2(42, 24) };
        var keyboard = new FakeKeyboard();
        var context = new FakeInputContext();
        context.AddInitialMouse(mouse, isConnected: false);
        context.AddInitialKeyboard(keyboard, isConnected: false);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);

        using var subscription = service.Attach(context);

        keyboard.RaiseKeyDown(Key.ControlLeft, scanCode: 37);
        keyboard.RaiseKeyDown(Key.R, scanCode: 15);
        mouse.RaiseMouseDown(MouseButton.Left);

        Assert.Collection(
            received,
            first =>
            {
                Assert.Equal(WpfInputEventKind.KeyDown, first.Kind);
                Assert.Equal("LeftCtrl", first.Key);
                Assert.Equal(WpfInputModifiers.Control, first.Modifiers);
            },
            second =>
            {
                Assert.Equal(WpfInputEventKind.KeyDown, second.Kind);
                Assert.Equal("R", second.Key);
                Assert.Equal(WpfInputModifiers.Control, second.Modifiers);
            },
            third =>
            {
                Assert.Equal(WpfInputEventKind.MouseDown, third.Kind);
                Assert.Equal(WpfMouseButton.Left, third.Button);
                Assert.Equal(42, third.X);
                Assert.Equal(24, third.Y);
                Assert.Equal(WpfInputModifiers.Control, third.Modifiers);
            });
    }

    [Fact]
    public void AttachUsesCurrentMousePositionBeforeFirstMouseMove()
    {
        var mouse = new FakeMouse { Position = Vector2.Zero };
        var context = new FakeInputContext();
        context.AddInitialMouse(mouse);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);

        using var subscription = service.Attach(context);

        mouse.Position = new Vector2(120, 84);
        mouse.RaiseMouseDown(MouseButton.Left);

        var input = Assert.Single(received);
        Assert.Equal(WpfInputEventKind.MouseDown, input.Kind);
        Assert.Equal(120, input.X);
        Assert.Equal(84, input.Y);
    }

    [Fact]
    public void AttachForwardsMouseUpWithoutMatchingMouseDown()
    {
        var mouse = new FakeMouse { Position = new Vector2(12, 34) };
        var context = new FakeInputContext();
        context.AddInitialMouse(mouse);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);

        using var subscription = service.Attach(context);

        mouse.RaiseMouseUp(MouseButton.Left);
        mouse.RaiseMouseDown(MouseButton.Left);
        mouse.RaiseMouseUp(MouseButton.Left);

        Assert.Collection(
            received,
            first =>
            {
                Assert.Equal(WpfInputEventKind.MouseUp, first.Kind);
                Assert.Equal(WpfMouseButton.Left, first.Button);
                Assert.Equal(12, first.X);
                Assert.Equal(34, first.Y);
            },
            second =>
            {
                Assert.Equal(WpfInputEventKind.MouseDown, second.Kind);
                Assert.Equal(WpfMouseButton.Left, second.Button);
            },
            third =>
            {
                Assert.Equal(WpfInputEventKind.MouseUp, third.Kind);
                Assert.Equal(WpfMouseButton.Left, third.Button);
            });
    }

    [Theory]
    [InlineData(MouseButton.Left, 0)]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Middle, 2)]
    [InlineData(MouseButton.Button4, 3)]
    [InlineData(MouseButton.Button5, 4)]
    public void OriginalButtonPositionPrecedesDelayedMoveWithoutSamplingTheLaterCursor(MouseButton button, int nativeButton)
    {
        var mouse = new FakeMouse();
        var context = new FakeInputContext();
        context.AddInitialMouse(mouse);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);
        NativeMouseButtonPacket packet = default;
        int reads = 0;
        bool Read(out NativeMouseButtonPacket value) { reads++; value = packet; return true; }
        using var subscription = service.Attach(context, Read);

        mouse.RaiseMouseMove(new Vector2(110, 438));
        mouse.Position = new Vector2(999, 777); // Later live cursor is not either button's position.
        packet = new(NativePointerEventKind.Down, nativeButton, new(110, 153));
        mouse.RaiseMouseDown(button);
        packet = new(NativePointerEventKind.Up, nativeButton, new(-17, 29));
        mouse.RaiseMouseUp(button);
        mouse.RaiseMouseMove(new Vector2(110, 153));

        Assert.Equal(new[] { WpfInputEventKind.MouseMove, WpfInputEventKind.MouseDown,
            WpfInputEventKind.MouseUp, WpfInputEventKind.MouseMove }, received.Select(e => e.Kind));
        Assert.Equal((110d, 153d), (received[1].X, received[1].Y));
        Assert.Equal((-17d, 29d), (received[2].X, received[2].Y));
        Assert.Equal(0, mouse.PositionReadCount);
        Assert.Equal(2, reads);
        subscription.Dispose();
        mouse.RaiseMouseDown(button);
        Assert.Equal(2, reads);
        Assert.Equal(4, received.Count);
    }

    [Theory]
    [InlineData(false, NativePointerEventKind.Down, 0)]
    [InlineData(true, NativePointerEventKind.Up, 0)]
    [InlineData(true, NativePointerEventKind.Down, 1)]
    [InlineData(true, NativePointerEventKind.Move, 0)]
    public void UnavailableOrUnrelatedPacketsPreserveTheOriginalProviderPolicy(
        bool available, NativePointerEventKind kind, int button)
    {
        var mouse = new FakeMouse();
        var context = new FakeInputContext();
        context.AddInitialMouse(mouse);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);
        bool Read(out NativeMouseButtonPacket value)
        {
            value = new(kind, button, new(110, 153));
            return available;
        }
        using var subscription = service.Attach(context, Read);
        mouse.RaiseMouseMove(new Vector2(110, 438));
        mouse.Position = new Vector2(999, 777);
        mouse.RaiseMouseDown(MouseButton.Left);
        Assert.Equal((110d, 438d), (received[1].X, received[1].Y));
        Assert.Equal(1, mouse.PositionReadCount);
    }

    [Fact]
    public void AttachStopsForwardingDisconnectedDevices()
    {
        var mouse = new FakeMouse();
        var keyboard = new FakeKeyboard();
        var context = new FakeInputContext();
        context.AddInitialMouse(mouse);
        context.AddInitialKeyboard(keyboard);
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, e) => received.Add(e);

        using var subscription = service.Attach(context);

        context.Disconnect(mouse);
        context.Disconnect(keyboard);
        mouse.RaiseMouseDown(MouseButton.Left);
        keyboard.RaiseKeyDown(Key.A, scanCode: 1);

        Assert.Empty(received);
        Assert.Equal(1, keyboard.EndInputCount);
    }

    [Fact]
    public void NativePointerUsesOnlyOneStreamAndRetainsEventTimeModifiersAndPrecision()
    {
        var context = new FakeNativeInputContext();
        var mouse = new FakeMouse();
        context.AddInitialMouse(mouse);
        context.AddInitialKeyboard(new FakeKeyboard()); // No key is currently pressed.
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, input) => received.Add(input);
        using var subscription = service.Attach(context);
        const double x = 10000000.125, y = -123.123456789;
        context.RaiseNative(new NativePointerEvent(NativePointerEventKind.Down, x, y, 123.75,
            0, 2, NativePointerModifiers.Shift | NativePointerModifiers.CapsLock));
        mouse.RaiseMouseDown(MouseButton.Left);
        mouse.RaiseMouseMove(new Vector2(1, 2));
        context.Connect(new FakeMouse());
        var input = Assert.Single(received);
        Assert.Equal((x, y), (input.X, input.Y));
        Assert.Equal(WpfInputModifiers.Shift, input.Modifiers);
        Assert.Equal(PortablePointerModifiers.Shift | PortablePointerModifiers.CapsLock, input.NativePointer!.Modifiers);
        Assert.Equal((123.75, 2, 0), (input.NativePointer.Timestamp, input.NativePointer.ClickCount, input.NativePointer.Button));
    }

    [Fact]
    public void NativeScrollRetainsPointOrLineUnitsAndBothPhasesWithoutWheelConversion()
    {
        var context = new FakeNativeInputContext();
        var service = new SilkNetWpfInputService();
        var received = new List<WpfInputEventArgs>();
        service.InputReceived += (_, input) => received.Add(input);
        using var subscription = service.Attach(context);
        foreach (var unit in new[] { NativePointerScrollUnit.Points, NativePointerScrollUnit.Lines })
            context.RaiseNative(new NativePointerEvent(NativePointerEventKind.Scroll, 1.125, 2.25, 5,
                -1, 0, NativePointerModifiers.Super, 0.25, -0.5, unit, 4, 8)
                { ScrollProtocol = NativePointerScrollProtocol.AppKit });
        Assert.Equal(2, received.Count);
        for (int i = 0; i < received.Count; i++)
        {
            Assert.Equal((0.25, -0.5), (received[i].DeltaX, received[i].DeltaY));
            Assert.Equal(i == 0 ? PortablePointerScrollUnit.Points : PortablePointerScrollUnit.Lines, received[i].NativePointer!.ScrollUnit);
            Assert.Equal((4u, 8u), (received[i].NativePointer!.ScrollPhase, received[i].NativePointer!.MomentumPhase));
            Assert.Equal(PortablePointerScrollProtocol.AppKit, received[i].NativePointer!.ScrollProtocol);
            Assert.Equal(PortablePointerModifiers.Super, received[i].NativePointer!.Modifiers);
            Assert.Equal(OperatingSystem.IsMacOS() ? WpfInputModifiers.Control : WpfInputModifiers.Super, received[i].Modifiers);
        }
    }

    [Fact]
    public void NativeDisposalDeliversCancellationBeforeUnsubscribeAndAllowsReentrantDisposal()
    {
        var context = new FakeNativeInputContext();
        var service = new SilkNetWpfInputService();
        IDisposable? subscription = null;
        int cancellations = 0;
        service.InputReceived += (_, input) =>
        {
            if (input.Kind != WpfInputEventKind.MouseCancel) return;
            Assert.False(context.IsDisposed);
            Assert.Equal(PortablePointerEventKind.Cancel, input.NativePointer!.Kind);
            cancellations++;
            subscription!.Dispose();
        };
        subscription = service.Attach(context);
        context.RaiseNative(new NativePointerEvent(NativePointerEventKind.Down, 1, 2, 3, 0, 1, 0));
        subscription.Dispose();
        Assert.True(context.IsDisposed);
        Assert.Equal(1, cancellations);
        context.RaiseNative(new NativePointerEvent(NativePointerEventKind.Cancel, 1, 2, 3, -1, 0, 0));
        Assert.Equal(1, cancellations);
    }

    [Fact]
    public void ThrowingNativeCancellationStillDisposesAndUnsubscribes()
    {
        var context = new FakeNativeInputContext();
        var service = new SilkNetWpfInputService();
        var failure = new InvalidOperationException("Source cancellation failed.");
        int calls = 0;
        service.InputReceived += (_, input) =>
        {
            calls++;
            if (input.Kind == WpfInputEventKind.MouseCancel) throw failure;
        };
        var subscription = service.Attach(context);
        context.RaiseNative(new NativePointerEvent(NativePointerEventKind.Move, 1, 2, 3, -1, 0, 0));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(subscription.Dispose));
        Assert.True(context.IsDisposed);
        context.RaiseNative(new NativePointerEvent(NativePointerEventKind.Move, 1, 2, 3, -1, 0, 0));
        subscription.Dispose();
        Assert.Equal(2, calls);
    }

    private sealed class FakeNativeInputContext : FakeInputContext, INativePointerInputContext
    {
        private bool _observed;
        public NativePointerEvent? CurrentEvent { get; private set; }
        public ulong InputGeneration { get; private set; } = 1;
        public event Action<NativePointerEvent>? PointerEvent;
        public void RaiseNative(NativePointerEvent input)
        {
            _observed = input.Kind != NativePointerEventKind.Cancel;
            CurrentEvent = input;
            try { PointerEvent?.Invoke(input); }
            finally { CurrentEvent = null; }
        }
        public override void Dispose()
        {
            if (IsDisposed) return;
            InputGeneration++;
            try
            {
                if (_observed) RaiseNative(new NativePointerEvent(NativePointerEventKind.Cancel, 1, 2, 3, -1, 0, 0));
            }
            finally { base.Dispose(); }
        }
    }

    private class FakeInputContext : IInputContext
    {
        private readonly List<IGamepad> _gamepads = new();
        private readonly List<IJoystick> _joysticks = new();
        private readonly List<IKeyboard> _keyboards = new();
        private readonly List<IMouse> _mice = new();
        private readonly List<IInputDevice> _otherDevices = new();

        public event Action<IInputDevice, bool>? ConnectionChanged;

        public IntPtr Handle => IntPtr.Zero;

        public IReadOnlyList<IGamepad> Gamepads => _gamepads;

        public IReadOnlyList<IJoystick> Joysticks => _joysticks;

        public IReadOnlyList<IKeyboard> Keyboards => _keyboards;

        public IReadOnlyList<IMouse> Mice => _mice;

        public IReadOnlyList<IInputDevice> OtherDevices => _otherDevices;

        public bool IsDisposed { get; private set; }

        public void AddInitialMouse(FakeMouse mouse, bool isConnected = true)
        {
            mouse.IsConnected = isConnected;
            _mice.Add(mouse);
        }

        public void AddInitialKeyboard(FakeKeyboard keyboard, bool isConnected = true)
        {
            keyboard.IsConnected = isConnected;
            _keyboards.Add(keyboard);
        }

        public void Connect(FakeMouse mouse)
        {
            mouse.IsConnected = true;
            _mice.Add(mouse);
            ConnectionChanged?.Invoke(mouse, true);
        }

        public void Connect(FakeKeyboard keyboard)
        {
            keyboard.IsConnected = true;
            _keyboards.Add(keyboard);
            ConnectionChanged?.Invoke(keyboard, true);
        }

        public void Disconnect(FakeMouse mouse)
        {
            mouse.IsConnected = false;
            ConnectionChanged?.Invoke(mouse, false);
            _mice.Remove(mouse);
        }

        public void Disconnect(FakeKeyboard keyboard)
        {
            keyboard.IsConnected = false;
            ConnectionChanged?.Invoke(keyboard, false);
            _keyboards.Remove(keyboard);
        }

        public virtual void Dispose()
        {
            IsDisposed = true;
        }
    }

#pragma warning disable CS0067
    private sealed class FakeMouse : IMouse
    {
        public event Action<IMouse, MouseButton>? MouseDown;

        public event Action<IMouse, MouseButton>? MouseUp;

        public event Action<IMouse, MouseButton, Vector2>? Click;

        public event Action<IMouse, MouseButton, Vector2>? DoubleClick;

        public event Action<IMouse, Vector2>? MouseMove;

        public event Action<IMouse, ScrollWheel>? Scroll;

        public string Name => "Fake Mouse";

        public int Index => 0;

        public bool IsConnected { get; set; }

        public IReadOnlyList<MouseButton> SupportedButtons { get; } = Array.Empty<MouseButton>();

        public IReadOnlyList<ScrollWheel> ScrollWheels { get; } = Array.Empty<ScrollWheel>();

        private Vector2 _position;
        public int PositionReadCount { get; private set; }
        public Vector2 Position
        {
            get { PositionReadCount++; return _position; }
            set => _position = value;
        }

        public ICursor Cursor => null!;

        public int DoubleClickTime { get; set; }

        public int DoubleClickRange { get; set; }

        public bool IsButtonPressed(MouseButton btn)
        {
            return false;
        }

        public void RaiseMouseDown(MouseButton button)
        {
            MouseDown?.Invoke(this, button);
        }

        public void RaiseMouseUp(MouseButton button)
        {
            MouseUp?.Invoke(this, button);
        }

        public void RaiseMouseMove(Vector2 position)
        {
            Position = position;
            MouseMove?.Invoke(this, position);
        }

        public void RaiseScroll(ScrollWheel wheel)
        {
            Scroll?.Invoke(this, wheel);
        }
    }
#pragma warning restore CS0067

    private sealed class FakeKeyboard : IKeyboard
    {
        private readonly HashSet<Key> _pressedKeys = new();

        public event Action<IKeyboard, Key, int>? KeyDown;

        public event Action<IKeyboard, Key, int>? KeyUp;

        public event Action<IKeyboard, char>? KeyChar;

        public string Name => "Fake Keyboard";

        public int Index => 0;

        public bool IsConnected { get; set; }

        public IReadOnlyList<Key> SupportedKeys { get; } = Array.Empty<Key>();

        public string ClipboardText { get; set; } = string.Empty;

        public int BeginInputCount { get; private set; }

        public int EndInputCount { get; private set; }

        public bool IsKeyPressed(Key key)
        {
            return _pressedKeys.Contains(key);
        }

        public bool IsScancodePressed(int scancode)
        {
            return false;
        }

        public void BeginInput()
        {
            BeginInputCount++;
        }

        public void EndInput()
        {
            EndInputCount++;
        }

        public void RaiseKeyDown(Key key, int scanCode)
        {
            _pressedKeys.Add(key);
            KeyDown?.Invoke(this, key, scanCode);
        }

        public void RaiseKeyUp(Key key, int scanCode)
        {
            _pressedKeys.Remove(key);
            KeyUp?.Invoke(this, key, scanCode);
        }

        public void RaiseKeyChar(char character)
        {
            KeyChar?.Invoke(this, character);
        }
    }
}
