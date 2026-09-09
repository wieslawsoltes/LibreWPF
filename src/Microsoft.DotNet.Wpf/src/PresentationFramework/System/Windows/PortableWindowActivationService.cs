// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Specialized;
using System.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MS.Internal;
using ProGPU.Wpf.Interop;

namespace System.Windows
{
    internal static class PortableWindowActivationService
    {
        private static readonly WindowActivationServiceRegistrar s_registrar = new WindowActivationServiceRegistrar();
        private static IDisposable s_registrarRegistration;
        private static Func<object, object> _activate;
        private static Func<object, object> _createHidden;
        private static Action<object> _show;
        private static Action<object> _hide;
        private static Action<object, object> _setWindowState;
        private static Action<object, string> _setTitle;
        private static Action<object, object> _setIcon;
        private static Action<object, double, double> _setClientSize;
        private static Action<object, double, double> _setPosition;
        private static Action<object, bool> _setTopmost;
        private static Action<object, object, object> _setWindowBorder;
        private static Action<object> _close;
        private static Action<object> _run;
        private static Action<object, Func<bool>> _runDialog;
        private static Action<object, Action> _releaseDialog;
        private static Action<object, object> _setOwner;
        private static Action<object> _dispose;
        private static Func<object, bool> _dragMove;
        private static Func<object, IntPtr> _getHandle;
        private static Func<IntPtr, PortableWindowRegion, bool> _setWindowRegion;
        private static Func<object, bool> _requestActivation;
        private static Func<object, double, double, bool> _showSystemMenu;

        internal static bool IsEnabled
        {
            get
            {
                // Registering a host is the explicit selection of portable windowing.
                // Ordinary Windows WPF has no registration and retains its native path.
                return Volatile.Read(ref _activate) != null;
            }
        }

        internal static void RegisterPortableInteropService()
        {
            s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterWindowActivationService(s_registrar);
        }

        internal static void Register(
            Func<object, object> activate,
            Action<object> show = null,
            Action<object> hide = null,
            Action<object, object> setWindowState = null,
            Action<object, string> setTitle = null,
            Action<object, double, double> setClientSize = null,
            Action<object, double, double> setPosition = null,
            Action<object, bool> setTopmost = null,
            Action<object, object, object> setWindowBorder = null,
            Action<object> close = null,
            Action<object> run = null,
            Action<object> dispose = null,
            Func<object, bool> dragMove = null,
            Func<object, IntPtr> getHandle = null,
            Func<IntPtr, PortableWindowRegion, bool> setWindowRegion = null,
            Func<object, bool> requestActivation = null,
            Action<object, object> setIcon = null,
            Func<object, object> createHidden = null,
            Func<object, double, double, bool> showSystemMenu = null,
            Action<object, Func<bool>> runDialog = null,
            Action<object, object> setOwner = null,
            Action<object, Action> releaseDialog = null)
        {
            ArgumentNullException.ThrowIfNull(activate);

            Volatile.Write(ref _show, show);
            Volatile.Write(ref _hide, hide);
            Volatile.Write(ref _setWindowState, setWindowState);
            Volatile.Write(ref _setTitle, setTitle);
            Volatile.Write(ref _setClientSize, setClientSize);
            Volatile.Write(ref _setPosition, setPosition);
            Volatile.Write(ref _setTopmost, setTopmost);
            Volatile.Write(ref _setWindowBorder, setWindowBorder);
            Volatile.Write(ref _close, close);
            Volatile.Write(ref _run, run);
            Volatile.Write(ref _runDialog, runDialog);
            Volatile.Write(ref _releaseDialog, releaseDialog);
            Volatile.Write(ref _setOwner, setOwner);
            Volatile.Write(ref _dispose, dispose);
            Volatile.Write(ref _dragMove, dragMove);
            Volatile.Write(ref _getHandle, getHandle);
            Volatile.Write(ref _setWindowRegion, setWindowRegion);
            Volatile.Write(ref _requestActivation, requestActivation);
            Volatile.Write(ref _setIcon, setIcon);
            Volatile.Write(ref _createHidden, createHidden);
            Volatile.Write(ref _showSystemMenu, showSystemMenu);
            Volatile.Write(ref _activate, activate);
        }

        internal static void Clear()
        {
            Volatile.Write(ref _activate, null);
            Volatile.Write(ref _createHidden, null);
            Volatile.Write(ref _showSystemMenu, null);
            Volatile.Write(ref _show, null);
            Volatile.Write(ref _hide, null);
            Volatile.Write(ref _setWindowState, null);
            Volatile.Write(ref _setTitle, null);
            Volatile.Write(ref _setClientSize, null);
            Volatile.Write(ref _setPosition, null);
            Volatile.Write(ref _setTopmost, null);
            Volatile.Write(ref _setWindowBorder, null);
            Volatile.Write(ref _close, null);
            Volatile.Write(ref _run, null);
            Volatile.Write(ref _runDialog, null);
            Volatile.Write(ref _releaseDialog, null);
            Volatile.Write(ref _setOwner, null);
            Volatile.Write(ref _dispose, null);
            Volatile.Write(ref _dragMove, null);
            Volatile.Write(ref _getHandle, null);
            Volatile.Write(ref _setWindowRegion, null);
            Volatile.Write(ref _requestActivation, null);
            Volatile.Write(ref _setIcon, null);
        }

        internal static bool TryActivate(Window window, out object activation, bool duringShow = true)
        {
            activation = null;

            Func<object, object> activate = Volatile.Read(ref _activate);
            if (activate == null)
            {
                return false;
            }

            if (!duringShow)
            {
                activate = Volatile.Read(ref _createHidden) ?? throw new PlatformNotSupportedException(
                    "The registered portable host does not support hidden window sources. Falling back to Windows MIL is not permitted.");
            }

            activation = activate(window) ?? throw new InvalidOperationException(
                "The registered portable window host could not activate this Window. Falling back to Windows MIL is not permitted.");
            return true;
        }

        internal static void Show(object activation)
        {
            Volatile.Read(ref _show)?.Invoke(activation);
        }

        internal static bool TryRequestActivation(object activation)
        {
            if (activation == null)
            {
                return false;
            }

            Func<object, bool> requestActivation = Volatile.Read(ref _requestActivation);
            return requestActivation != null && requestActivation(activation);
        }

        internal static void Hide(object activation)
        {
            Volatile.Read(ref _hide)?.Invoke(activation);
        }

        internal static void SetWindowState(object activation, WindowState windowState)
        {
            Volatile.Read(ref _setWindowState)?.Invoke(activation, windowState);
        }

        internal static void SetTitle(object activation, string title)
        {
            Volatile.Read(ref _setTitle)?.Invoke(activation, title);
        }

        internal static void SetIcon(object activation, object icon)
        {
            Volatile.Read(ref _setIcon)?.Invoke(activation, icon);
        }

        internal static void SetOwner(object activation, Window owner)
        {
            Action<object, object> callback = Volatile.Read(ref _setOwner) ?? throw new PlatformNotSupportedException(
                "The registered portable host does not support window ownership.");
            callback(activation, owner);
        }

        internal static void SetClientSize(object activation, double width, double height)
        {
            Volatile.Read(ref _setClientSize)?.Invoke(activation, width, height);
        }

        internal static void SetPosition(object activation, double left, double top)
        {
            Volatile.Read(ref _setPosition)?.Invoke(activation, left, top);
        }

        internal static void SetTopmost(object activation, bool topmost)
        {
            Volatile.Read(ref _setTopmost)?.Invoke(activation, topmost);
        }

        internal static void SetWindowBorder(object activation, ResizeMode resizeMode, WindowStyle windowStyle)
        {
            Volatile.Read(ref _setWindowBorder)?.Invoke(activation, resizeMode, windowStyle);
        }

        internal static bool TryDragMove(object activation)
        {
            if (activation == null)
            {
                return false;
            }

            Func<object, bool> dragMove = Volatile.Read(ref _dragMove);
            return dragMove != null && dragMove(activation);
        }

        internal static bool TryShowSystemMenu(object activation, double desktopX, double desktopY)
        {
            return activation != null && double.IsFinite(desktopX) && double.IsFinite(desktopY) &&
                Volatile.Read(ref _showSystemMenu)?.Invoke(activation, desktopX, desktopY) == true;
        }

        internal static IntPtr GetHandle(object activation)
        {
            if (activation == null)
            {
                return IntPtr.Zero;
            }

            Func<object, IntPtr> getHandle = Volatile.Read(ref _getHandle);
            return getHandle != null ? getHandle(activation) : IntPtr.Zero;
        }

        internal static bool TrySetWindowRegion(IntPtr handle, PortableWindowRegion region)
        {
            if (handle == IntPtr.Zero || region == null)
            {
                return false;
            }

            Func<IntPtr, PortableWindowRegion, bool> setWindowRegion = Volatile.Read(ref _setWindowRegion);
            return setWindowRegion != null && setWindowRegion(handle, region);
        }

        internal static void SetActivationState(Window window, bool isActive)
        {
            if (window == null)
            {
                return;
            }

            if (isActive && !PortableModalInputScope.AllowsInput(window))
                return;

            if (!isActive)
            {
                NotifyPortableInputProvidersDeactivated(window);
            }

            window.HandleActivate(isActive);
        }

        private static void NotifyPortableInputProvidersDeactivated(Window window)
        {
            PresentationSource source = PresentationSource.CriticalFromVisual(window);
            if (source == null)
            {
                return;
            }

            source.GetInputProvider(typeof(KeyboardDevice))?.NotifyDeactivate();
            source.GetInputProvider(typeof(MouseDevice))?.NotifyDeactivate();
        }

        internal static void ProcessInput(Window window, PortableInputEventArgs input)
        {
            if (window == null || input == null)
            {
                return;
            }

            PresentationSource source = PresentationSource.CriticalFromVisual(window);
            if (source == null)
            {
                return;
            }

            input.Handled = ProcessInput(source, window, input);
        }

        internal static void ProcessInput(PresentationSource source, PortableInputEventArgs input)
        {
            if (source == null || input == null)
            {
                return;
            }

            input.Handled = ProcessInput(source, source.RootVisual as UIElement, input);
        }

        internal static int ProcessDragDrop(
            Window window,
            string[] files,
            string text,
            double x,
            double y,
            int allowedEffects,
            int acceptedEffect)
        {
            return ProcessDragDropEvent(
                window,
                dragDropEventKind: 0,
                files,
                text,
                x,
                y,
                allowedEffects,
                acceptedEffect);
        }

        internal static int ProcessDragDropEvent(
            Window window,
            int dragDropEventKind,
            string[] files,
            string text,
            double x,
            double y,
            int allowedEffects,
            int acceptedEffect)
        {
            if (window == null || !PortableModalInputScope.AllowsInput(window))
            {
                return (int)DragDropEffects.None;
            }

            DataObject dataObject = CreatePortableDragDropDataObject(files, text);
            if (dataObject == null)
            {
                return (int)DragDropEffects.None;
            }

            DragDropEffects mappedAllowedEffects = ToDragDropEffects(allowedEffects, DragDropEffects.Copy);
            DragDropEffects mappedAcceptedEffect = ToDragDropEffects(acceptedEffect, DragDropEffects.None);
            DragDropEffects result = DragDrop.ProcessPortableDragDrop(
                window,
                ToDragDropRoutedEvent(dragDropEventKind),
                dataObject,
                DragDropKeyStates.None,
                mappedAllowedEffects,
                mappedAcceptedEffect,
                new Point(ToInputCoordinate(x), ToInputCoordinate(y)));
            return (int)result;
        }

        private static bool ProcessInput(PresentationSource source, UIElement rootHitTestElement, PortableInputEventArgs input)
        {
            if (source.IsDisposed || !IsModalInputAllowed(rootHitTestElement))
                return true;

            InputManager inputManager = InputManager.UnsecureCurrent;
            if (IsMouseInputKind(input.Kind) && Mouse.Captured != null &&
                !IsModalInputElementAllowed(Mouse.Captured))
                return true;
            if (!IsMouseInputKind(input.Kind) && Keyboard.FocusedElement != null &&
                !IsModalInputElementAllowed(Keyboard.FocusedElement))
                return true;
            int timestamp = Environment.TickCount;
            PresentationSource mouseInputSource = source;
            UIElement mouseRootHitTestElement = rootHitTestElement;
            Point mouseRootPoint = new Point(input.X, input.Y);
            if (IsMouseInputKind(input.Kind))
            {
                ResolveCapturedMouseInputRoute(
                    inputManager,
                    source,
                    rootHitTestElement,
                    mouseRootPoint,
                    out mouseInputSource,
                    out mouseRootHitTestElement,
                    out mouseRootPoint);
            }
            RawMouseActions mouseActivation = GetMouseActivationAction(inputManager, mouseInputSource);

            // Capture can redirect an otherwise admitted event to another source.
            if (!IsModalInputAllowed(mouseRootHitTestElement))
                return true;

            switch (input.Kind)
            {
                case PortableInputEventKind.KeyDown:
                    return ProcessKeyboardInput(inputManager, source, input, timestamp, isDown: true);
                case PortableInputEventKind.KeyUp:
                    return ProcessKeyboardInput(inputManager, source, input, timestamp, isDown: false);
                case PortableInputEventKind.TextInput:
                    return ProcessTextInput(inputManager, source, input, timestamp);
                case PortableInputEventKind.MouseMove:
                    return ProcessMouseInput(inputManager, mouseInputSource, mouseRootHitTestElement, mouseRootPoint, input, timestamp, mouseActivation | RawMouseActions.AbsoluteMove);
                case PortableInputEventKind.MouseDown:
                    if (input.Button == PortableMouseButton.Left &&
                        rootHitTestElement is Window window &&
                        window.TryBeginPortableChromeDrag(mouseRootPoint))
                    {
                        return true;
                    }

                    return TryGetMouseButtonAction(input.Button, isDown: true, out RawMouseActions mouseDownAction)
                        && ProcessMouseInput(inputManager, mouseInputSource, mouseRootHitTestElement, mouseRootPoint, input, timestamp, mouseActivation | RawMouseActions.AbsoluteMove | mouseDownAction);
                case PortableInputEventKind.MouseUp:
                    return TryGetMouseButtonAction(input.Button, isDown: false, out RawMouseActions mouseUpAction)
                        && ProcessMouseInput(inputManager, mouseInputSource, mouseRootHitTestElement, mouseRootPoint, input, timestamp, mouseActivation | mouseUpAction);
                case PortableInputEventKind.MouseWheel:
                    int wheel = ToMouseWheelDelta(input.DeltaY);
                    return wheel != 0
                        && ProcessMouseInput(inputManager, mouseInputSource, mouseRootHitTestElement, mouseRootPoint, input, timestamp, mouseActivation | RawMouseActions.AbsoluteMove | RawMouseActions.VerticalWheelRotate, wheel);
                default:
                    return false;
            }
        }

        internal static bool IsModalInputAllowed(UIElement root)
        {
            if (!PortableModalInputScope.IsActive) return true;
            UIElement fast = root;
            while (root != null)
            {
                if (root is Window window) return !window.IsDisposed && PortableModalInputScope.AllowsInput(window);
                root = GetPopupInputOwnerRoot(root);
                fast = GetPopupInputOwnerRoot(GetPopupInputOwnerRoot(fast));
                // Reject malformed owner cycles without per-pointer scratch allocations.
                if (root != null && ReferenceEquals(root, fast)) return false;
            }
            return false;
        }

        private static UIElement GetPopupInputOwnerRoot(UIElement root)
        {
            PresentationSource owner = root is PopupRoot popupRoot && popupRoot.Parent is Popup popup
                ? popup.PortableInputOwnerSource : null;
            return owner != null && !owner.IsDisposed ? owner.RootVisual as UIElement : null;
        }

        private static bool IsModalInputElementAllowed(IInputElement element)
        {
            if (!PortableModalInputScope.IsActive) return true;
            DependencyObject visual = element is DependencyObject dependencyObject
                ? InputElement.GetContainingVisual(dependencyObject) : null;
            return visual != null && IsModalInputAllowed(
                PresentationSource.CriticalFromVisual(visual)?.RootVisual as UIElement);
        }

        internal static void PrepareForModalInput()
        {
            InputManager manager = InputManager.UnsecureCurrent;
            PresentationSource keyboardSource = manager.PrimaryKeyboardDevice.ActiveSource;
            if (keyboardSource != null && !IsModalInputAllowed(keyboardSource.RootVisual as UIElement))
                keyboardSource.GetInputProvider(typeof(KeyboardDevice))?.NotifyDeactivate();
            PresentationSource mouseSource = manager.PrimaryMouseDevice.ActiveSource;
            if (mouseSource != null && !IsModalInputAllowed(mouseSource.RootVisual as UIElement))
                mouseSource.GetInputProvider(typeof(MouseDevice))?.NotifyDeactivate();
            if (Mouse.Captured != null && !IsModalInputElementAllowed(Mouse.Captured))
                Mouse.Capture(null);
            if (Keyboard.FocusedElement != null && !IsModalInputElementAllowed(Keyboard.FocusedElement))
                Keyboard.ClearFocus();
        }

        internal static IDisposable CaptureModalInputRestoreState()
        {
            IInputElement focus = Keyboard.FocusedElement;
            DependencyObject visual = focus is DependencyObject element
                ? InputElement.GetContainingVisual(element) : null;
            PresentationSource source = visual != null ? PresentationSource.CriticalFromVisual(visual) : null;
            Window window = GetModalInputOwnerWindow(source?.RootVisual as UIElement);
            if (window != null && (window.IsDisposed || !window.IsActive)) window = null;
            if (window == null && Application.Current is Application application && application.Dispatcher.CheckAccess())
            {
                WindowCollection windows = application.Windows;
                for (int i = 0; i < windows.Count; i++)
                {
                    Window candidate = windows[i];
                    if (candidate.Dispatcher.CheckAccess() && !candidate.IsDisposed && candidate.IsActive)
                    {
                        window = candidate;
                        break;
                    }
                }
            }
            return new ModalInputRestoreState(window, source, focus);
        }

        // Drop targets in separately surfaced popups activate their real owner, not a
        // placement target or an HWND inferred from the portable presentation identity.
        internal static bool TryActivateInputOwner(PresentationSource source)
        {
            if (source == null || !source.CheckAccess() || source.IsDisposed ||
                !PointUtil.IsPortablePresentationSource(source)) return false;
            Window window = GetModalInputOwnerWindow(source.RootVisual as UIElement);
            return window != null && window.Dispatcher.CheckAccess() && !window.IsDisposed &&
                window.PortableWindowActivation != null && window.IsVisible && window.IsEnabled &&
                PortableModalInputScope.AllowsInput(window) && window.Activate();
        }

        private static Window GetModalInputOwnerWindow(UIElement root)
        {
            UIElement fast = root;
            while (root != null && root is not Window)
            {
                root = GetPopupInputOwnerRoot(root);
                fast = GetPopupInputOwnerRoot(GetPopupInputOwnerRoot(fast));
                if (root != null && ReferenceEquals(root, fast)) { root = null; break; }
            }
            return root as Window;
        }

        private sealed class ModalInputRestoreState : IDisposable
        {
            private readonly int _threadId = Environment.CurrentManagedThreadId;
            private Window _window;
            private PresentationSource _source;
            private IInputElement _focus;

            internal ModalInputRestoreState(Window window, PresentationSource source, IInputElement focus)
            {
                _window = window;
                _source = source;
                _focus = focus;
            }

            public void Dispose()
            {
                if (_threadId != Environment.CurrentManagedThreadId)
                    throw new InvalidOperationException("Dialog focus restoration requires its source thread.");
                Window window = _window;
                PresentationSource source = _source;
                IInputElement focus = _focus;
                _window = null;
                _source = null;
                _focus = null;
                if (window == null || window.IsDisposed || !window.IsVisible || !window.IsEnabled ||
                    window.PortableWindowActivation == null || !PortableModalInputScope.AllowsInput(window) ||
                    !PortableModalInputScope.IsNativeInputPolicySynchronized) return;
                // Native admission is required; never manufacture IsActive or
                // restore a detached/moved element into another source.
                if (!TryRequestActivation(window.PortableWindowActivation) || window.IsDisposed ||
                    !window.IsVisible || !PortableModalInputScope.AllowsInput(window)) return;
                if (source == null || source.IsDisposed || focus is not DependencyObject element ||
                    !ReferenceEquals(GetModalInputOwnerWindow(source.RootVisual as UIElement), window) ||
                    !IsModalInputElementAllowed(focus)) return;
                DependencyObject visual = InputElement.GetContainingVisual(element);
                if (visual != null && ReferenceEquals(PresentationSource.CriticalFromVisual(visual), source))
                    Keyboard.Focus(focus);
            }
        }

        private static bool IsMouseInputKind(PortableInputEventKind kind)
        {
            return kind == PortableInputEventKind.MouseMove ||
                kind == PortableInputEventKind.MouseDown ||
                kind == PortableInputEventKind.MouseUp ||
                kind == PortableInputEventKind.MouseWheel;
        }

        private static void ResolveCapturedMouseInputRoute(
            InputManager inputManager,
            PresentationSource reportedSource,
            UIElement reportedRootHitTestElement,
            Point reportedRootPoint,
            out PresentationSource inputSource,
            out UIElement rootHitTestElement,
            out Point rootPoint)
        {
            inputSource = reportedSource;
            rootHitTestElement = reportedRootHitTestElement;
            rootPoint = reportedRootPoint;

            MouseDevice mouseDevice = inputManager.PrimaryMouseDevice;
            if (mouseDevice?.CapturedMode != CaptureMode.Element ||
                mouseDevice.Captured is not DependencyObject capturedElement)
            {
                return;
            }

            DependencyObject capturedVisual = InputElement.GetContainingVisual(capturedElement);
            PresentationSource capturedSource = capturedVisual != null
                ? PresentationSource.CriticalFromVisual(capturedVisual)
                : null;
            if (capturedSource == null ||
                capturedSource.RootVisual is not UIElement capturedRootHitTestElement ||
                ReferenceEquals(capturedSource, reportedSource))
            {
                return;
            }

            Point reportedClientPoint = PointUtil.RootToClient(reportedRootPoint, reportedSource);
            Point screenPoint = PointUtil.ClientToScreen(reportedClientPoint, reportedSource);
            Point capturedClientPoint = PointUtil.ScreenToClient(screenPoint, capturedSource);
            Point capturedRootPoint = PointUtil.TryClientToRoot(
                capturedClientPoint,
                capturedSource,
                throwOnError: false,
                out bool success);
            if (!success)
            {
                return;
            }

            inputSource = capturedSource;
            rootHitTestElement = capturedRootHitTestElement;
            rootPoint = capturedRootPoint;
        }

        private static RawMouseActions GetMouseActivationAction(InputManager inputManager, PresentationSource source)
        {
            return ReferenceEquals(inputManager.PrimaryMouseDevice?.ActiveSource, source)
                ? RawMouseActions.None
                : RawMouseActions.Activate;
        }

        private static bool ProcessKeyboardInput(
            InputManager inputManager,
            PresentationSource source,
            PortableInputEventArgs input,
            int timestamp,
            bool isDown)
        {
            if (!TryGetKey(input.Key, out Key key) || key == Key.None)
            {
                return false;
            }

            if (inputManager.PrimaryKeyboardDevice is PortableKeyboardDevice keyboardDevice)
            {
                UpdateModifierKeyStates(keyboardDevice, input.Modifiers);
                keyboardDevice.SetKeyStates(key, isDown ? KeyStates.Down : KeyStates.None);
            }

            RawKeyboardInputReport report = new RawKeyboardInputReport(
                source,
                InputMode.Foreground,
                timestamp,
                RawKeyboardActions.Activate | (isDown ? RawKeyboardActions.KeyDown : RawKeyboardActions.KeyUp),
                input.ScanCode,
                IsExtendedKey(key),
                IsSystemKey(key, input.Modifiers),
                KeyInterop.VirtualKeyFromKey(key),
                IntPtr.Zero);

            return ProcessInputReport(inputManager, report);
        }

        private static bool ProcessTextInput(
            InputManager inputManager,
            PresentationSource source,
            PortableInputEventArgs input,
            int timestamp)
        {
            if (input.Character is not char character)
            {
                return false;
            }

            RawTextInputReport report = new RawTextInputReport(
                source,
                InputMode.Foreground,
                timestamp,
                isDeadCharacter: false,
                isSystemCharacter: (input.Modifiers & PortableInputModifiers.Alt) == PortableInputModifiers.Alt,
                isControlCharacter: char.IsControl(character),
                character);

            return ProcessInputReport(inputManager, report);
        }

        private static bool ProcessMouseInput(
            InputManager inputManager,
            PresentationSource source,
            UIElement rootHitTestElement,
            Point rootPoint,
            PortableInputEventArgs input,
            int timestamp,
            RawMouseActions actions,
            int wheel = 0)
        {
            if (inputManager.PrimaryMouseDevice is PortableMouseDevice mouseDevice &&
                TryGetMouseButton(input.Button, out MouseButton mouseButton))
            {
                if ((actions & GetMouseButtonPressAction(mouseButton)) != 0)
                {
                    mouseDevice.SetButtonState(mouseButton, MouseButtonState.Pressed);
                }
                else if ((actions & GetMouseButtonReleaseAction(mouseButton)) != 0)
                {
                    mouseDevice.SetButtonState(mouseButton, MouseButtonState.Released);
                }
            }

            Point clientPoint = ToMouseClientPoint(source, rootHitTestElement, rootPoint);
            RawMouseInputReport report = new RawMouseInputReport(
                InputMode.Foreground,
                timestamp,
                source,
                actions,
                ToInputCoordinate(clientPoint.X),
                ToInputCoordinate(clientPoint.Y),
                wheel,
                IntPtr.Zero);

            return ProcessInputReport(inputManager, report);
        }

        private static Point ToMouseClientPoint(PresentationSource source, UIElement rootHitTestElement, Point rootPoint)
        {
            if (source?.CompositionTarget == null)
            {
                return rootPoint;
            }

            Point clientPoint = PointUtil.RootToClient(rootPoint, source);
            bool rootHit = (clientPoint.X < 0.0 || clientPoint.Y < 0.0) &&
                IsHitTestableRootPoint(rootHitTestElement, source, rootPoint);
            if ((clientPoint.X < 0.0 || clientPoint.Y < 0.0) &&
                rootHit)
            {
                clientPoint = new Point(
                    Math.Max(0.0, clientPoint.X),
                    Math.Max(0.0, clientPoint.Y));
            }

            return clientPoint;
        }

        private static bool IsHitTestableRootPoint(UIElement rootHitTestElement, PresentationSource source, Point rootPoint)
        {
            if (rootHitTestElement?.InputHitTest(rootPoint) != null)
            {
                return true;
            }

            return source?.RootVisual is UIElement root &&
                !ReferenceEquals(root, rootHitTestElement) &&
                root.InputHitTest(rootPoint) != null;
        }

        private static bool ProcessInputReport(InputManager inputManager, InputReport report)
        {
            InputReportEventArgs input = new InputReportEventArgs(null, report)
            {
                RoutedEvent = InputManager.PreviewInputReportEvent
            };

            return inputManager.ProcessInput(input);
        }

        private static DataObject CreatePortableDragDropDataObject(string[] files, string text)
        {
            var dataObject = new DataObject();
            bool hasData = false;

            if (files != null && files.Length > 0)
            {
                var fileDropList = new StringCollection();
                fileDropList.AddRange(files);
                dataObject.SetFileDropList(fileDropList);
                hasData = true;
            }

            if (text != null)
            {
                dataObject.SetText(text);
                hasData = true;
            }

            return hasData ? dataObject : null;
        }

        private static DragDropEffects ToDragDropEffects(int value, DragDropEffects fallback)
        {
            var effects = (DragDropEffects)value;
            return DragDrop.IsValidDragDropEffects(effects) ? effects : fallback;
        }

        private static RoutedEvent ToDragDropRoutedEvent(int dragDropEventKind)
        {
            return dragDropEventKind switch
            {
                1 => DragDrop.DragEnterEvent,
                2 => DragDrop.DragOverEvent,
                3 => DragDrop.DragLeaveEvent,
                _ => DragDrop.DropEvent
            };
        }

        private static void UpdateModifierKeyStates(PortableKeyboardDevice keyboardDevice, PortableInputModifiers modifiers)
        {
            SetModifierKeyState(keyboardDevice, Key.LeftShift, modifiers, PortableInputModifiers.Shift);
            SetModifierKeyState(keyboardDevice, Key.RightShift, modifiers, PortableInputModifiers.Shift);
            SetModifierKeyState(keyboardDevice, Key.LeftCtrl, modifiers, PortableInputModifiers.Control);
            SetModifierKeyState(keyboardDevice, Key.RightCtrl, modifiers, PortableInputModifiers.Control);
            SetModifierKeyState(keyboardDevice, Key.LeftAlt, modifiers, PortableInputModifiers.Alt);
            SetModifierKeyState(keyboardDevice, Key.RightAlt, modifiers, PortableInputModifiers.Alt);
            SetModifierKeyState(keyboardDevice, Key.LWin, modifiers, PortableInputModifiers.Super);
            SetModifierKeyState(keyboardDevice, Key.RWin, modifiers, PortableInputModifiers.Super);
        }

        private static void SetModifierKeyState(
            PortableKeyboardDevice keyboardDevice,
            Key key,
            PortableInputModifiers modifiers,
            PortableInputModifiers modifier)
        {
            keyboardDevice.SetKeyStates(
                key,
                (modifiers & modifier) == modifier ? KeyStates.Down : KeyStates.None);
        }

        private static bool TryGetKey(string keyName, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            if (Enum.TryParse(keyName, ignoreCase: false, out key))
            {
                return true;
            }

            switch (keyName)
            {
                case "Backspace":
                    key = Key.Back;
                    return true;
                case "ShiftLeft":
                    key = Key.LeftShift;
                    return true;
                case "ShiftRight":
                    key = Key.RightShift;
                    return true;
                case "ControlLeft":
                    key = Key.LeftCtrl;
                    return true;
                case "ControlRight":
                    key = Key.RightCtrl;
                    return true;
                case "AltLeft":
                    key = Key.LeftAlt;
                    return true;
                case "AltRight":
                    key = Key.RightAlt;
                    return true;
                case "SuperLeft":
                    key = Key.LWin;
                    return true;
                case "SuperRight":
                    key = Key.RWin;
                    return true;
            }

            if (keyName.Length == 7 &&
                keyName.StartsWith("Number", StringComparison.Ordinal) &&
                char.IsDigit(keyName[6]))
            {
                return Enum.TryParse("D" + keyName[6], ignoreCase: false, out key);
            }

            if (keyName.Length == 7 &&
                keyName.StartsWith("Keypad", StringComparison.Ordinal) &&
                char.IsDigit(keyName[6]))
            {
                return Enum.TryParse("NumPad" + keyName[6], ignoreCase: false, out key);
            }

            return false;
        }

        private static bool TryGetMouseButtonAction(
            PortableMouseButton portableButton,
            bool isDown,
            out RawMouseActions action)
        {
            action = RawMouseActions.None;
            if (!TryGetMouseButton(portableButton, out MouseButton button))
            {
                return false;
            }

            action = isDown ? GetMouseButtonPressAction(button) : GetMouseButtonReleaseAction(button);
            return action != RawMouseActions.None;
        }

        private static bool TryGetMouseButton(PortableMouseButton portableButton, out MouseButton button)
        {
            switch (portableButton)
            {
                case PortableMouseButton.Left:
                    button = MouseButton.Left;
                    return true;
                case PortableMouseButton.Right:
                    button = MouseButton.Right;
                    return true;
                case PortableMouseButton.Middle:
                    button = MouseButton.Middle;
                    return true;
                case PortableMouseButton.XButton1:
                    button = MouseButton.XButton1;
                    return true;
                case PortableMouseButton.XButton2:
                    button = MouseButton.XButton2;
                    return true;
                default:
                    button = MouseButton.Left;
                    return false;
            }
        }

        private static RawMouseActions GetMouseButtonPressAction(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => RawMouseActions.Button1Press,
                MouseButton.Right => RawMouseActions.Button2Press,
                MouseButton.Middle => RawMouseActions.Button3Press,
                MouseButton.XButton1 => RawMouseActions.Button4Press,
                MouseButton.XButton2 => RawMouseActions.Button5Press,
                _ => RawMouseActions.None
            };
        }

        private static RawMouseActions GetMouseButtonReleaseAction(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => RawMouseActions.Button1Release,
                MouseButton.Right => RawMouseActions.Button2Release,
                MouseButton.Middle => RawMouseActions.Button3Release,
                MouseButton.XButton1 => RawMouseActions.Button4Release,
                MouseButton.XButton2 => RawMouseActions.Button5Release,
                _ => RawMouseActions.None
            };
        }

        private static bool IsExtendedKey(Key key)
        {
            return key == Key.RightAlt ||
                key == Key.RightCtrl ||
                key == Key.Insert ||
                key == Key.Delete ||
                key == Key.Home ||
                key == Key.End ||
                key == Key.Prior ||
                key == Key.Next ||
                key == Key.Left ||
                key == Key.Right ||
                key == Key.Up ||
                key == Key.Down;
        }

        private static bool IsSystemKey(Key key, PortableInputModifiers modifiers)
        {
            return key == Key.LeftAlt ||
                key == Key.RightAlt ||
                (modifiers & PortableInputModifiers.Alt) == PortableInputModifiers.Alt;
        }

        private static int ToMouseWheelDelta(double delta)
        {
            return ToInputCoordinate(delta * Mouse.MouseWheelDeltaForOneLine);
        }

        private static int ToInputCoordinate(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (value <= int.MinValue)
            {
                return int.MinValue;
            }

            return (int)Math.Round(value);
        }

        internal static void Close(object activation)
        {
            Volatile.Read(ref _close)?.Invoke(activation);
        }

        internal static bool TryRun(Window window)
        {
            if (window == null)
            {
                return false;
            }

            object activation = window.PortableWindowActivation;
            if (activation == null)
            {
                return false;
            }

            Action<object> run = Volatile.Read(ref _run);
            if (run == null)
            {
                throw new InvalidOperationException(
                    "The active portable window host has no run-loop callback. Falling back to the Windows application loop is not permitted.");
            }

            run(activation);
            return true;
        }

        internal static Action<object, Func<bool>> GetDialogRunCallback()
        {
            return Volatile.Read(ref _runDialog) ?? throw new PlatformNotSupportedException(
                "The portable window host does not support a source-controlled dialog run loop.");
        }

        internal static Action<object, Action> GetDialogReleaseCallback()
        {
            return Volatile.Read(ref _releaseDialog) ?? throw new PlatformNotSupportedException(
                "The portable window host does not support native dialog release completion.");
        }

        internal static void FlushDispatcherOperations(object window, DispatcherPriority markerPriority)
        {
            FlushDispatcherOperations(window, markerPriority, Timeout.InfiniteTimeSpan);
        }

        internal static bool FlushDispatcherOperations(object window, DispatcherPriority markerPriority, TimeSpan timeout)
        {
            if (window is not Window typedWindow ||
                typedWindow.Dispatcher == null ||
                typedWindow.Dispatcher.HasShutdownStarted ||
                typedWindow.Dispatcher.HasShutdownFinished)
            {
                return false;
            }

            bool markerReached = false;
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherOperation markerOperation = typedWindow.Dispatcher.BeginInvoke(
                markerPriority,
                (DispatcherOperationCallback)delegate(object state)
                {
                    markerReached = true;
                    ((DispatcherFrame)state).Continue = false;
                    return null;
                },
                frame);
            DispatcherTimer timer = null;
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                timer = new DispatcherTimer(DispatcherPriority.Send, typedWindow.Dispatcher)
                {
                    Interval = timeout
                };
                timer.Tick += delegate
                {
                    timer.Stop();
                    frame.Continue = false;
                };
                timer.Start();
            }

            Dispatcher.PushFrame(frame);
            timer?.Stop();

            if (!markerReached && markerOperation.Status == DispatcherOperationStatus.Pending)
            {
                markerOperation.Abort();
            }

            return markerReached;
        }

        internal static bool PromoteDispatcherTimers(object window, int currentTimeInTicks)
        {
            if (window is not Window typedWindow ||
                typedWindow.Dispatcher == null ||
                typedWindow.Dispatcher.HasShutdownStarted ||
                typedWindow.Dispatcher.HasShutdownFinished)
            {
                return false;
            }

            typedWindow.Dispatcher.PromoteTimers(currentTimeInTicks);
            return true;
        }

        internal static void Dispose(object activation)
        {
            Action<object> dispose = Volatile.Read(ref _dispose);
            if (dispose != null)
            {
                dispose(activation);
            }
            else if (activation is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private sealed class WindowActivationServiceRegistrar : IPortableWindowActivationServiceRegistrar
        {
            public PortableWpfServiceKey ServiceKey
            {
                get
                {
                    return PortableWpfServiceKey.PresentationFramework;
                }
            }

            public void Register(PortableWindowActivationCallbacks callbacks)
            {
                ArgumentNullException.ThrowIfNull(callbacks);

                PortableWindowActivationService.Register(
                    callbacks.Activate,
                    callbacks.Show,
                    callbacks.Hide,
                    callbacks.SetWindowState,
                    callbacks.SetTitle,
                    callbacks.SetClientSize,
                    callbacks.SetPosition,
                    callbacks.SetTopmost,
                    callbacks.SetWindowBorder,
                    callbacks.Close,
                    callbacks.Run,
                    callbacks.Dispose,
                    callbacks.DragMove,
                    callbacks.GetHandle,
                    callbacks.SetWindowRegion,
                    callbacks.RequestActivation,
                    callbacks.SetIcon,
                    callbacks.CreateHidden,
                    callbacks.ShowSystemMenu,
                    callbacks.RunDialog,
                    callbacks.SetOwner,
                    callbacks.ReleaseDialog);
            }

            public bool TryRegisterMediaContextRenderService(
                object window,
                Action<object, TimeSpan> requestRender,
                out IDisposable registration)
            {
                registration = null;
                if (window is not Window)
                {
                    return false;
                }

                registration = Media.PortableMediaContextRenderService.Register(
                    (invalidatedSource, delay) => requestRender(invalidatedSource, delay));
                return true;
            }

            public bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow)
            {
                isMainWindow = false;
                if (window is not Window typedWindow)
                {
                    return false;
                }

                Application application = Application.Current;
                if (application == null
                    || !ReferenceEquals(application.Dispatcher, typedWindow.Dispatcher))
                {
                    return true;
                }

                if (!application.Dispatcher.CheckAccess())
                {
                    return false;
                }

                isMainWindow = ReferenceEquals(application.MainWindow, typedWindow);
                return true;
            }

            public bool TryCloseWindow(object window, out PortableWindowCloseResult result)
            {
                result = PortableWindowCloseResult.NotInvoked;
                if (window is not Window typedWindow)
                {
                    return false;
                }

                typedWindow.Close();
                result = typedWindow.IsDisposed
                    ? PortableWindowCloseResult.Closed
                    : PortableWindowCloseResult.Canceled;
                return true;
            }

            public bool TryIsWindowDisposed(object window, out bool isDisposed)
            {
                isDisposed = false;
                if (window is not Window typedWindow)
                {
                    return false;
                }

                isDisposed = typedWindow.IsDisposed;
                return true;
            }

            public bool TrySetActivationState(object window, bool isActive)
            {
                if (window is not Window typedWindow)
                {
                    return false;
                }

                PortableWindowActivationService.SetActivationState(typedWindow, isActive);
                return true;
            }

            public bool TryBeginInvokeInput(object window, Action callback)
            {
                if (window is not Window typedWindow ||
                    callback == null ||
                    typedWindow.Dispatcher == null ||
                    typedWindow.Dispatcher.CheckAccess())
                {
                    return false;
                }

                typedWindow.Dispatcher.BeginInvoke(DispatcherPriority.Input, callback);
                return true;
            }

            public bool TryProcessInputEvent(object window, PortableWindowInputEvent input)
            {
                if (window is not Window typedWindow || input == null)
                {
                    return false;
                }

                var mappedInput = CreatePortableInputEvent(input);
                PortableWindowActivationService.ProcessInput(typedWindow, mappedInput);
                input.Handled = mappedInput.Handled;
                return true;
            }

            public bool TryProcessPresentationSourceInputEvent(object presentationSource, PortableWindowInputEvent input)
            {
                if (presentationSource is not PresentationSource typedSource || input == null)
                {
                    return false;
                }

                var mappedInput = CreatePortableInputEvent(input);
                PortableWindowActivationService.ProcessInput(typedSource, mappedInput);
                input.Handled = mappedInput.Handled;
                return true;
            }

            private static PortableInputEventArgs CreatePortableInputEvent(PortableWindowInputEvent input)
            {
                return new PortableInputEventArgs(
                    (PortableInputEventKind)input.Kind,
                    input.Key,
                    input.ScanCode,
                    input.Character,
                    input.X,
                    input.Y,
                    input.DeltaX,
                    input.DeltaY,
                    (PortableMouseButton)input.Button,
                    (PortableInputModifiers)input.Modifiers);
            }

            public bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout)
            {
                if (!Enum.TryParse(markerPriorityName, ignoreCase: false, out DispatcherPriority markerPriority))
                {
                    return false;
                }

                if (timeout.HasValue)
                {
                    return PortableWindowActivationService.FlushDispatcherOperations(
                        window,
                        markerPriority,
                        timeout.Value);
                }

                PortableWindowActivationService.FlushDispatcherOperations(window, markerPriority);
                return true;
            }

            public bool TryPromoteDispatcherTimers(object window, int currentTimeInTicks)
            {
                return PortableWindowActivationService.PromoteDispatcherTimers(window, currentTimeInTicks);
            }

            public bool TrySetWindowRegion(IntPtr handle, PortableWindowRegion region)
            {
                return PortableWindowActivationService.TrySetWindowRegion(handle, region);
            }

            public bool TryProcessDragDropEvent(
                object window,
                int dragDropEventKind,
                string[] files,
                string text,
                double x,
                double y,
                int allowedEffects,
                int acceptedEffect,
                out int result)
            {
                if (window is not Window typedWindow)
                {
                    result = (int)DragDropEffects.None;
                    return false;
                }

                result = PortableWindowActivationService.ProcessDragDropEvent(
                    typedWindow,
                    dragDropEventKind,
                    files,
                    text,
                    x,
                    y,
                    allowedEffects,
                    acceptedEffect);
                return true;
            }

            public void Clear()
            {
                PortableWindowActivationService.Clear();
            }
        }
    }
}
