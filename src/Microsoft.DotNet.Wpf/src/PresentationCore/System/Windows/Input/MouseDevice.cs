// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Windows.Input.StylusPointer;
using System.Windows.Interop;
using System.Windows.Threading;
using MS.Internal;
using MS.Win32;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using ProGPU.Wpf.Interop;

// There's a choice of where to send MouseWheel events - to the element under
// the mouse (like IE does) or to the element with keyboard focus (like Win32
// does).  The latter choice lets you move the mouse away from the area you're
// scrolling and still use the wheel.  To get this effect, uncomment this line.
//#define SEND_WHEEL_EVENTS_TO_FOCUS

namespace System.Windows.Input
{
    /// <summary>
    ///     The MouseDevice class represents the mouse device to the
    ///     members of a context.
    /// </summary>
    public abstract class MouseDevice : InputDevice
    {
       internal MouseDevice(InputManager inputManager)
       {
            _inputManager = inputManager;
            _inputManager.PreProcessInput += new PreProcessInputEventHandler(PreProcessInput);
            _inputManager.PreNotifyInput += new NotifyInputEventHandler(PreNotifyInput);
            _inputManager.PostProcessInput += new ProcessInputEventHandler(PostProcessInput);

            // Get information about how far two clicks of a double click can be considered
            // to be in the "same place and time".
            //
            // The call here goes into the safe helper calls, more of a consistency in approach
            //
            if (OperatingSystem.IsWindows())
            {
                _doubleClickDeltaX = SafeSystemMetrics.DoubleClickDeltaX;
                _doubleClickDeltaY = SafeSystemMetrics.DoubleClickDeltaY;
                _doubleClickDeltaTime = SafeNativeMethods.GetDoubleClickTime();
            }
            else
            {
                _doubleClickDeltaX = 4;
                _doubleClickDeltaY = 4;
                _doubleClickDeltaTime = 500;
            }

            _overIsEnabledChangedEventHandler = new DependencyPropertyChangedEventHandler(OnOverIsEnabledChanged);
            _overIsVisibleChangedEventHandler = new DependencyPropertyChangedEventHandler(OnOverIsVisibleChanged);
            _overIsHitTestVisibleChangedEventHandler  = new DependencyPropertyChangedEventHandler(OnOverIsHitTestVisibleChanged);
            _reevaluateMouseOverDelegate = new DispatcherOperationCallback(ReevaluateMouseOverAsync);
            _reevaluateMouseOverOperation = null;

            _captureIsEnabledChangedEventHandler = new DependencyPropertyChangedEventHandler(OnCaptureIsEnabledChanged);
            _captureIsVisibleChangedEventHandler = new DependencyPropertyChangedEventHandler(OnCaptureIsVisibleChanged);
            _captureIsHitTestVisibleChangedEventHandler  = new DependencyPropertyChangedEventHandler(OnCaptureIsHitTestVisibleChanged);
            _reevaluateCaptureDelegate = new DispatcherOperationCallback(ReevaluateCaptureAsync);
            _reevaluateCaptureOperation = null;

            _inputManager.HitTestInvalidatedAsync += new EventHandler(OnHitTestInvalidatedAsync);
        }

        /// <summary>
        ///     Gets the current state of the specified button from the device from either the underlying system or the StylusDevice
        /// </summary>
        /// <param name="mouseButton">
        ///     The mouse button to get the state of
        /// </param>
        /// <returns>
        ///     The state of the specified mouse button
        /// </returns>
        protected MouseButtonState GetButtonState(MouseButton mouseButton)
        {
            // StylusDevice could have been disposed internally here.
            if ( _stylusDevice != null && _stylusDevice.IsValid)
                return _stylusDevice.GetMouseButtonState(mouseButton, this);
            else
                return GetButtonStateFromSystem(mouseButton);
        }

        /// <summary>
        ///     Gets the current position of the mouse in screen co-ords from either the underlying system or the StylusDevice
        /// </summary>
        /// <returns>
        ///     The current mouse location in screen co-ords
        /// </returns>
        protected Point GetScreenPosition()
        {
            if (_stylusDevice != null)
                return _stylusDevice.GetMouseScreenPosition(this);
            else
                return GetScreenPositionFromSystem();
        }

        /// <summary>
        ///     Gets the current state of the specified button from the device from the underlying system
        /// </summary>
        /// <param name="mouseButton">
        ///     The mouse button to get the state of
        /// </param>
        /// <returns>
        ///     The state of the specified mouse button
        /// </returns>
        internal abstract MouseButtonState GetButtonStateFromSystem(MouseButton mouseButton);

        /// <summary>
        ///     Gets the current position of the mouse in screen co-ords from the underlying system
        /// </summary>
        /// <returns>
        ///     The current mouse location in screen co-ords
        /// </returns>
        internal Point GetScreenPositionFromSystem()
        {
            // Win32 has issues reliably returning where the mouse is.  Until we figure
            // out a better way, just return the last mouse position in screen coordinates.

            Point ptScreen = new Point(0, 0);

            // Security Mitigation: do not give out input state if the device is not active.
            if (IsActive)
            {
                try
                {
                    PresentationSource activeSource = CriticalActiveSource;
                    if (activeSource != null)
                    {
                        ptScreen = PointUtil.ClientToScreen(_lastPosition, activeSource);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // The window could be shutting down, so just return (0,0).
                    ptScreen = new Point(0, 0);
                }
            }

            return ptScreen;
        }

        /// <summary>
        ///     Gets the current position of the mouse in client co-ords of the current PresentationSource
        /// </summary>
        /// <returns>
        ///     The current mouse position in client co-ords
        /// </returns>
        protected Point GetClientPosition()
        {
            Point ptClient = new Point(0, 0);
            try
            {
                PresentationSource activeSource = CriticalActiveSource;
                if (activeSource != null)
                {
                    ptClient = GetClientPosition(activeSource);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The window could be shutting down, so just return (0,0).
                ptClient = new Point(0, 0);
            }

            return ptClient;
        }

        /// <summary>
        ///     Gets the current position of the mouse in client co-ords of the specified PresentationSource
        /// </summary>
        /// <returns>
        ///     The current mouse position in client co-ords
        /// </returns>
        protected Point GetClientPosition(PresentationSource presentationSource)
        {
            Point ptScreen = GetScreenPosition();
            Point ptClient = PointUtil.ScreenToClient(ptScreen, presentationSource);

            return ptClient;
        }

        /// <summary>
        ///     Returns the element that input from this device is sent to.
        /// </summary>
        public override IInputElement Target
        {
            get
            {
//                 VerifyAccess();

                // Return the element that the mouse is over.  If the mouse
                // has been captured, the mouse will be considered "over"
                // the capture point if the mouse is outside of the
                // captured element (or subtree).
                return _mouseOver;
            }
        }

        /// <summary>
        ///     Returns the PresentationSource that is reporting input for this device.
        /// </summary>
        /// <remarks>
        ///     Callers must have UIPermission(UIPermissionWindow.AllWindows) to call this API.
        /// </remarks>

        public override PresentationSource ActiveSource => _inputSource;

        /// <summary>
        ///     Returns the PresentationSource that is reporting input for this device.
        /// </summary>
        internal PresentationSource CriticalActiveSource => _inputSource;

        /// <summary>
        ///     Returns the element that the mouse is over.
        /// </summary>
        /// <remarks>
        ///     The mouse is considered directly over an element if the mouse
        ///     has been captured to that element.
        /// </remarks>
        public IInputElement DirectlyOver
        {
            get
            {
                return _mouseOver;
            }
        }

        /// <summary>
        ///     Returns the element that the mouse is over regardless of
        ///     its IsEnabled state.
        /// </summary>
        internal IInputElement RawDirectlyOver
        {
            get
            {
                if (_rawMouseOver != null)
                {
                    IInputElement rawMouseOver = (IInputElement)_rawMouseOver.Target;
                    if (rawMouseOver != null)
                    {
                        return rawMouseOver;
                    }
                }

                return DirectlyOver;
            }
        }

        /// <summary>
        ///     Returns the element that has captured the mouse.
        /// </summary>
        public IInputElement Captured
        {
            get
            {
//                 VerifyAccess();
                return (!_isCaptureMouseInProgress) ? _mouseCapture : null;
            }
        }

        /// <summary>
        ///     Returns the element that has captured the mouse.
        /// </summary>
        internal CaptureMode CapturedMode
        {
            get
            {
                return _captureMode;
            }
        }

        /// <summary>
        ///     Captures the mouse to a particular element.
        /// </summary>
        public bool Capture(IInputElement element)
        {
            return Capture(element, CaptureMode.Element);
        }

        /// <summary>
        ///     Captures the mouse to a particular element.
        /// </summary>
        public bool Capture(IInputElement element, CaptureMode captureMode)
        {
            int timeStamp = Environment.TickCount;
//             VerifyAccess();

            if (!(captureMode == CaptureMode.None || captureMode == CaptureMode.Element || captureMode == CaptureMode.SubTree))
            {
                throw new System.ComponentModel.InvalidEnumArgumentException("captureMode", (int)captureMode, typeof(CaptureMode));
            }

            if (element == null)
            {
                captureMode = CaptureMode.None;
            }

            if (captureMode == CaptureMode.None)
            {
                element = null;
            }

            // Validate that elt is either a UIElement, a ContentElement or a UIElement3D.
            DependencyObject eltDO = element as DependencyObject;
            if (eltDO != null && !InputElement.IsValid(element))
            {
                throw new InvalidOperationException(SR.Format(SR.Invalid_IInputElement, eltDO.GetType()));
            }

            bool success = false;

            // The element we are capturing to must be both enabled and visible.
            if (element is UIElement)
            {
                UIElement e = element as UIElement;

                if(e.IsVisible && e.IsEnabled)
                {
                    success = true;
                }
            }
            else if (element is ContentElement)
            {
                ContentElement ce = element as ContentElement;

                if(ce.IsEnabled) // There is no IsVisible property for ContentElement
                {
                    success = true;
                }
            }
            else if (element is UIElement3D)
            {
                UIElement3D e = element as UIElement3D;

                if(e.IsVisible && e.IsEnabled)
                {
                    success = true;
                }
            }
            else
            {
                // Setting capture to null.
                success = true;
            }

            if(success)
            {
                success = false;

                // Find a mouse input provider that provides input for either
                // the new element (if taking capture) or the existing capture
                // element (if releasing capture).
                IMouseInputProvider mouseInputProvider = null;
                if (element != null)
                {
                    DependencyObject containingVisual = InputElement.GetContainingVisual(eltDO);
                    if (containingVisual != null)
                    {
                        PresentationSource captureSource = PresentationSource.CriticalFromVisual(containingVisual);
                        if (captureSource != null)
                        {
                            mouseInputProvider = captureSource.GetInputProvider(typeof(MouseDevice)) as IMouseInputProvider;
                        }
                    }
                }
                else if (_mouseCapture != null)
                {
                    mouseInputProvider = _providerCapture;
                }

                // If we found a mouse input provider, ask it to either capture
                // or release the mouse for us.
                if(mouseInputProvider != null)
                {
                    if (element != null)
                    {
                        // CaptureMouse can raise a MouseMove event in some cases
                        // and listeners that query Mouse.Captured should not see the old
                        // value that's being replaced.   We'll expose 'null' instead.
                        // [This situation arises in a ComboBox that has a TextBox in
                        // its dropdown window]
                        bool savedIsCaptureMouseInProgress = _isCaptureMouseInProgress;
                        _isCaptureMouseInProgress = true;

                        success = mouseInputProvider.CaptureMouse();

                        _isCaptureMouseInProgress = savedIsCaptureMouseInProgress;

                        if (success)
                        {
                            ChangeMouseCapture(element, mouseInputProvider, captureMode, timeStamp);
                        }
                    }
                    else
                    {
                        mouseInputProvider.ReleaseMouseCapture();

                        // If we had capture, the input provider will release it.  That will
                        // cause a RawMouseAction.CancelCapture to be processed, which will
                        // update our internal states.
                        success = true;
                    }
                }
            }

            return success;
        }

        //
        // Find an IMouseInputProvider on which the cursor can be set
        private IMouseInputProvider FindMouseInputProviderForCursor( )
        {
            // The shape of this API goes on the assumption that, like Win32, the cursor
            // is set for the whole desktop, not just a particular element or a particular
            // root visual.  So instead of trying to find the IMouseInputProvider
            // that covers a particular element, we just find any IMouseInputProvider
            // and set the cursor on it.

            IMouseInputProvider mouseInputProvider = null;

            IEnumerator inputProviders = _inputManager.UnsecureInputProviders.GetEnumerator();

            while (inputProviders.MoveNext())
            {
                IMouseInputProvider provider = inputProviders.Current as IMouseInputProvider;
                if (provider != null )
                {
                    mouseInputProvider = provider;
                    break;
                }
            }

            return mouseInputProvider;
}

        /// <summary>
        /// The override cursor
        /// </summary>
        public Cursor OverrideCursor
        {
            get
            {
//                 VerifyAccess();

                return _overrideCursor;
            }

            set
            {
//                 VerifyAccess();

                _overrideCursor = value;
                UpdateCursorPrivate();
            }
        }

        /// <summary>
        /// Set the cursor
        /// </summary>
        /// <param ref="cursor">The new cursor</param>
        /// <remarks>Note that this cursor doesn't apply any particular UIElement, it applies
        ///          to the whole desktop.
        /// </remarks>
        public bool SetCursor(Cursor cursor)
        {
//             VerifyAccess();

            // Override the cursor if one is set.
            if (_overrideCursor != null)
            {
                cursor = _overrideCursor;
            }

            if (cursor == null)
            {
                cursor = Cursors.None;
            }
            // Get a mouse provider
            IMouseInputProvider mouseInputProvider = FindMouseInputProviderForCursor();

            // If we found one, set the cursor
            if (mouseInputProvider != null)
                return mouseInputProvider.SetCursor(cursor);
            else
                return false;
}

        /// <summary>
        ///     The state of the left button.
        /// </summary>
        public MouseButtonState LeftButton
        {
            get
            {
                return GetButtonState(MouseButton.Left);
            }
        }

        /// <summary>
        ///     The state of the right button.
        /// </summary>
        public MouseButtonState RightButton
        {
            get
            {
                return GetButtonState(MouseButton.Right);
            }
        }

        /// <summary>
        ///     The state of the middle button.
        /// </summary>
        public MouseButtonState MiddleButton
        {
            get
            {
                return GetButtonState(MouseButton.Middle);
            }
        }

        /// <summary>
        ///     The state of the first extended button.
        /// </summary>
        public MouseButtonState XButton1
        {
            get
            {
                return GetButtonState(MouseButton.XButton1);
            }
        }

        /// <summary>
        ///     The state of the second extended button.
        /// </summary>
        public MouseButtonState XButton2
        {
            get
            {
                return GetButtonState(MouseButton.XButton2);
            }
        }

        /// <summary>
        ///     Calculates the position of the mouse relative to
        ///     a particular element.
        /// </summary>
        public Point GetPosition(IInputElement relativeTo)
        {
//             VerifyAccess();

            // Validate that relativeTo is either a UIElement, a ContentElement or a UIElement3D.
            if (relativeTo != null && !InputElement.IsValid(relativeTo))
            {
                throw new InvalidOperationException(SR.Format(SR.Invalid_IInputElement, relativeTo.GetType()));
            }

            PresentationSource relativePresentationSource = null;

            if (relativeTo != null)
            {
                DependencyObject dependencyObject = relativeTo as  DependencyObject;
                DependencyObject containingVisual = InputElement.GetContainingVisual(dependencyObject);

                if (containingVisual != null)
                {
                    relativePresentationSource = PresentationSource.CriticalFromVisual(containingVisual);
                }
            }
            else
            {
                if (_inputSource is not null)
                {
                    relativePresentationSource = _inputSource;
                }
            }


            // Verify that we have a valid PresentationSource with a valid RootVisual
            // - if we don't we won't be able to invoke ClientToRoot or TranslatePoint and
            //   we will just return 0,0
            if (relativePresentationSource == null || relativePresentationSource.RootVisual == null)
            {
                return new Point(0, 0);
            }

            Point ptClient;
            Point ptRoot;
            bool success;
            Point ptRelative;

            ptClient    = GetClientPosition(relativePresentationSource);
            ptRoot      = PointUtil.TryClientToRoot(ptClient, relativePresentationSource, false, out success);
            if (!success)
            {
                // ClientToRoot failed, usually because the client area is degenerate.
                // Just return 0,0
                return new Point(0, 0);
            }
            ptRelative  = InputElement.TranslatePoint(ptRoot, relativePresentationSource.RootVisual, (DependencyObject)relativeTo);

            return ptRelative;
        }

        /// <summary>
        /// </summary>
        internal void ReevaluateMouseOver(DependencyObject element, DependencyObject oldParent, bool isCoreParent)
        {
            if (element != null)
            {
                if (isCoreParent)
                {
                    MouseOverTreeState.SetCoreParent(element, oldParent);
                }
                else
                {
                    MouseOverTreeState.SetLogicalParent(element, oldParent);
                }
            }

            // It would be best to re-evaluate anything dependent on the hit-test results
            // immediately after layout & rendering are complete.  Unfortunately this can
            // lead to an infinite loop.  Consider the following scenario:
            //
            // If the mouse is over an element, hide it.
            //
            // This never resolves to a "correct" state.  When the mouse moves over the
            // element, the element is hidden, so the mouse is no longer over it, so the
            // element is shown, but that means the mouse is over it again.  Repeat.
            //
            // We push our re-evaluation to a priority lower than input processing so that
            // the user can change the input device to avoid the infinite loops, or close
            // the app if nothing else works.
            //
            if (_reevaluateMouseOverOperation == null)
            {
                _reevaluateMouseOverOperation = Dispatcher.BeginInvoke(DispatcherPriority.Input, _reevaluateMouseOverDelegate, null);
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private object ReevaluateMouseOverAsync(object arg)
        {
            _reevaluateMouseOverOperation = null;

            Synchronize();

            // Refresh MouseOverProperty so that ReverseInherited Flags are updated.
            //
            // We only need to do this is there is any information about the old
            // tree state.  This is because it is possible (even likely) that
            // Synchronize() would have already done this if we hit-tested to a
            // different element.
            if (_mouseOverTreeState != null && !_mouseOverTreeState.IsEmpty)
            {
                UIElement.MouseOverProperty.OnOriginValueChanged(_mouseOver as DependencyObject, _mouseOver as DependencyObject, ref _mouseOverTreeState);
            }

            return null;
        }

        /// <summary>
        /// </summary>
        internal void ReevaluateCapture(DependencyObject element, DependencyObject oldParent, bool isCoreParent)
        {
            if (element != null)
            {
                if (isCoreParent)
                {
                    MouseCaptureWithinTreeState.SetCoreParent(element, oldParent);
                }
                else
                {
                    MouseCaptureWithinTreeState.SetLogicalParent(element, oldParent);
                }
            }

            // We re-evaluate the captured element to be consistent with how
            // we re-evaluate the element the mouse is over.
            //
            // See ReevaluateMouseOver for details.
            //
            if (_reevaluateCaptureOperation == null)
            {
                _reevaluateCaptureOperation = Dispatcher.BeginInvoke(DispatcherPriority.Input, _reevaluateCaptureDelegate, null);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private object ReevaluateCaptureAsync(object arg)
        {
            _reevaluateCaptureOperation = null;

            if (_mouseCapture == null )
                return null;

            bool killCapture = false;

            DependencyObject dependencyObject = _mouseCapture as DependencyObject;

            //
            // First, check things like IsEnabled, IsVisible, etc. on a
            // UIElement vs. ContentElement basis.
            //
            if (dependencyObject is UIElement uie)
            {
                killCapture = !ValidateUIElementForCapture(uie);
            }
            else if (dependencyObject is ContentElement ce)
            {
                killCapture = !ValidateContentElementForCapture(ce);
            }
            else if (dependencyObject is UIElement3D uie3D)
            {
                killCapture = !ValidateUIElement3DForCapture(uie3D);
            }

            //
            // Second, if we still haven't thought of a reason to kill capture, validate
            // it on a Visual basis for things like still being in the right tree.
            //
            if (!killCapture)
            {
                DependencyObject containingVisual = InputElement.GetContainingVisual(dependencyObject);
                killCapture = !ValidateVisualForCapture(containingVisual);
            }

            //
            // Lastly, if we found any reason above, kill capture.
            //
            if (killCapture)
            {
                Capture(null);
            }

            // Refresh MouseCaptureWithinProperty so that ReverseInherited flags are updated.
            //
            // We only need to do this is there is any information about the old
            // tree state.  This is because it is possible (even likely) that
            // we would have already killed capture if the capture criteria was
            // no longer met.
            if (_mouseCaptureWithinTreeState != null && !_mouseCaptureWithinTreeState.IsEmpty)
            {
                UIElement.MouseCaptureWithinProperty.OnOriginValueChanged(_mouseCapture as DependencyObject, _mouseCapture as DependencyObject, ref _mouseCaptureWithinTreeState);
            }

            return null;
        }

        private bool ValidateUIElementForCapture(UIElement element)
        {
            if (!element.IsEnabled)
                return false;

            if (!element.IsVisible)
                return false;

            if (!element.IsHitTestVisible)
                return false;

            return true;
        }

        private bool ValidateUIElement3DForCapture(UIElement3D element)
        {
            if (!element.IsEnabled)
                return false;

            if (!element.IsVisible)
                return false;

            if (!element.IsHitTestVisible)
                return false;

            return true;
        }


        private bool ValidateContentElementForCapture(ContentElement element)
        {
            if (!element.IsEnabled)
                return false;

            // NOTE: there are no IsVisible or IsHitTestVisible properties for ContentElements.

            return true;
        }

        private bool ValidateVisualForCapture(DependencyObject visual)
        {
            if (visual == null)
                return false;

            PresentationSource presentationSource = PresentationSource.CriticalFromVisual(visual);

            if (presentationSource == null)
                return false;

            if (presentationSource != CriticalActiveSource)
                return false;

            return true;
        }

        private void OnOverIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The element that the mouse is over just became disabled.
            //
            // We need to resynchronize the mouse so that we can figure out who
            // the mouse is over now.

            ReevaluateMouseOver(null, null, true);
        }

        private void OnOverIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The element that the mouse is over just became non-visible (collapsed or hidden).
            //
            // We need to resynchronize the mouse so that we can figure out who
            // the mouse is over now.

            ReevaluateMouseOver(null, null, true);
        }

        private void OnOverIsHitTestVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The element that the mouse is over was affected by a change in hit-test visibility.
            //
            // We need to resynchronize the mouse so that we can figure out who
            // the mouse is over now.

            ReevaluateMouseOver(null, null, true);
        }

        private void OnCaptureIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The element that the mouse is captured to just became disabled.
            //
            // We need to re-evaluate the element that has mouse capture since
            // we can't allow the mouse to remain captured by a disabled element.

            ReevaluateCapture(null, null, true);
        }

        private void OnCaptureIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The element that the mouse is captured to just became non-visible (collapsed or hidden).
            //
            // We need to re-evaluate the element that has mouse capture since
            // we can't allow the mouse to remain captured by a non-visible element.

            ReevaluateCapture(null, null, true);
        }

        private void OnCaptureIsHitTestVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The element that the mouse is captured to was affected by a change in hit-test visibility.
            //
            // We need to re-evaluate the element that has mouse capture since
            // we can't allow the mouse to remain captured by a non-hittest-visible element.

            ReevaluateCapture(null, null, true);
        }

        private void OnHitTestInvalidatedAsync(object sender, EventArgs e)
        {
            // The hit-test result may have changed.
            Synchronize();
        }

        /// <summary>
        ///     Forces the mouse to resynchronize.
        /// </summary>
        public void Synchronize()
        {
            // System.Console.WriteLine("Synchronize");
//             VerifyAccess();

            // Simulate a mouse move
            PresentationSource activeSource = CriticalActiveSource;
            // No OS position is available to re-hit-test after a native leave.
            // A real native/legacy position report, not layout, ends this state.
            if (this is PortableMouseDevice { NativePointerOutside: true })
                return;
            // A portable element-captured drag has no trustworthy OS cursor position when
            // a transient window switches the active source. Do not synthesize a move in
            // that narrow case. Subtree capture (used by menus and popups) still needs the
            // normal re-hit-test when the pointer enters its separate native surface.
            // Win32 also retains its original synchronization path.
            if (Captured != null &&
                _captureMode == CaptureMode.Element &&
                activeSource is PortablePresentationSource &&
                (LeftButton == MouseButtonState.Pressed ||
                 MiddleButton == MouseButtonState.Pressed ||
                 RightButton == MouseButtonState.Pressed))
            {
                return;
            }

            if (activeSource != null && activeSource.CompositionTarget != null && !activeSource.CompositionTarget.IsDisposed)
            {
                int timeStamp = Environment.TickCount;
                Point ptClient = GetClientPosition();

                RawMouseInputReport report = activeSource is PortablePresentationSource portableSource
                    ? PortableMouseInputReport.Synchronize(timeStamp, portableSource, ptClient)
                    : new RawMouseInputReport(InputMode.Foreground,
                                                                     timeStamp,
                                                                     activeSource,
                                                                     RawMouseActions.AbsoluteMove,
                                                                     (int)ptClient.X,
                                                                     (int)ptClient.Y,
                                                                     0,
                                                                     IntPtr.Zero)
                {
                    _isSynchronize = true
                };

                InputReportEventArgs inputReportEventArgs;
                if (_stylusDevice != null)
                {
                    // if we have a current stylusdevice .. use it
                    inputReportEventArgs = new InputReportEventArgs(_stylusDevice, report);
                }
                else
                {
                    inputReportEventArgs = new InputReportEventArgs(this, report);
                }

                inputReportEventArgs.RoutedEvent=InputManager.PreviewInputReportEvent;

                //ProcessInput has a linkdemand
                _inputManager.ProcessInput(inputReportEventArgs);
                return;
            }

            // There is no usable active source to re-hit-test against - which is exactly the
            // window a teardown opens up (a rapid source/design/close switch: the element that
            // was under the cursor is leaving the tree, and this queued re-evaluation is left
            // with nothing to hit-test against). This method used to return silently here,
            // leaving that element stuck with IsMouseOver==true and - the part that actually
            // hurts - never raising MouseLeave. Any hover timer armed against it (AvalonEdit's
            // MouseHoverLogic, tooltips, ...) then stayed live and fired later against a
            // disposed element. Nothing can be mouse-over when there is no active source, so
            // clear it through the ordinary path and let MouseLeave stop those timers.
            if (_mouseOver != null)
            {
                ChangeMouseOver(null, Environment.TickCount);
            }
        }

        /// <summary>
        ///     Forces the mouse cursor to be updated.
        /// </summary>
        public void UpdateCursor()
        {
            // Call Forwarded
            UpdateCursorPrivate();
        }

        /// <summary>
        ///     Forces the mouse cursor to be updated.
        /// </summary>
        /// <remarks>
        ///     This method has been added just because changing the public
        ///     API UpdateCursor will be a breaking change
        /// </remarks>
        private bool UpdateCursorPrivate()
        {
            int timeStamp = Environment.TickCount;
            QueryCursorEventArgs queryCursor = new QueryCursorEventArgs(this, timeStamp)
            {
                Cursor = Cursors.Arrow,
                RoutedEvent = Mouse.QueryCursorEvent
            };
            //ProcessInput has a linkdemand
            _inputManager.ProcessInput(queryCursor);
            return queryCursor.Handled;
        }

        internal PortableMouseInputReport NativeMouseOverReport { get; private set; }
        private ulong _nativeMouseOverRevision;
        internal bool IsNativeMouseOverNotificationCurrent => NativeMouseOverReport == null ||
            (NativeMouseOverReport.IsCurrent && this is PortableMouseDevice mouse &&
                mouse.NativePointerRevision == _nativeMouseOverRevision);

        private void ChangeMouseOver(IInputElement mouseOver, int timestamp, PortableMouseInputReport nativeReport = null)
        {
            PortableMouseInputReport previous = NativeMouseOverReport;
            ulong previousRevision = _nativeMouseOverRevision;
            NativeMouseOverReport = nativeReport;
            _nativeMouseOverRevision = (this as PortableMouseDevice)?.NativePointerRevision ?? 0;
            try { ChangeMouseOverCore(mouseOver, timestamp); }
            finally
            {
                NativeMouseOverReport = previous;
                _nativeMouseOverRevision = previousRevision;
            }
        }

        private void ChangeMouseOverCore(IInputElement mouseOver, int timestamp)
        {
            DependencyObject o = null;

            if (_mouseOver != mouseOver)
            {
                // Console.WriteLine("ChangeMouseOver(" + mouseOver + ")");

                // Update the critical piece of data.
                IInputElement oldMouseOver = _mouseOver;
                _mouseOver = mouseOver;

                using(Dispatcher.DisableProcessing()) // Disable reentrancy due to locks taken
                {
                    // Adjust the handlers we use to track everything.
                    if(oldMouseOver != null)
                    {
                        o = oldMouseOver as DependencyObject;
                        if (o is UIElement uie)
                        {
                            uie.IsEnabledChanged -= _overIsEnabledChangedEventHandler;
                            uie.IsVisibleChanged -= _overIsVisibleChangedEventHandler;
                            uie.IsHitTestVisibleChanged -= _overIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is ContentElement ce)
                        {
                            ce.IsEnabledChanged -= _overIsEnabledChangedEventHandler;

                            // NOTE: there are no IsVisible or IsHitTestVisible properties for ContentElements.
                            //
                            // ce.IsVisibleChanged -= _overIsVisibleChangedEventHandler;
                            // ce.IsHitTestVisibleChanged -= _overIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is UIElement3D uie3D)
                        {
                            uie3D.IsEnabledChanged -= _overIsEnabledChangedEventHandler;
                            uie3D.IsVisibleChanged -= _overIsVisibleChangedEventHandler;
                            uie3D.IsHitTestVisibleChanged -= _overIsHitTestVisibleChangedEventHandler;
                        }
                    }
                    if(_mouseOver != null)
                    {
                        o = _mouseOver as DependencyObject;
                        if (o is UIElement uie)
                        {
                            uie.IsEnabledChanged += _overIsEnabledChangedEventHandler;
                            uie.IsVisibleChanged += _overIsVisibleChangedEventHandler;
                            uie.IsHitTestVisibleChanged += _overIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is ContentElement ce)
                        {
                            ce.IsEnabledChanged += _overIsEnabledChangedEventHandler;

                            // NOTE: there are no IsVisible or IsHitTestVisible properties for ContentElements.
                            //
                            // ce.IsVisibleChanged += _overIsVisibleChangedEventHandler;
                            // ce.IsHitTestVisibleChanged += _overIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is UIElement3D uie3D)
                        {
                            uie3D.IsEnabledChanged += _overIsEnabledChangedEventHandler;
                            uie3D.IsVisibleChanged += _overIsVisibleChangedEventHandler;
                            uie3D.IsHitTestVisibleChanged += _overIsHitTestVisibleChangedEventHandler;
                        }
                    }
                }

                // Oddly enough, update the IsMouseOver property first.  This is
                // so any callbacks will see the more-common IsMouseOver property
                // set correctly.
                if (this is PortableMouseDevice)
                {
                    try
                    {
                        UIElement.MouseOverProperty.OnOriginValueChanged(oldMouseOver as DependencyObject,
                            _mouseOver as DependencyObject, ref _mouseOverTreeState);
                    }
                    finally { UpdateMouseDirectlyOver(oldMouseOver); }
                }
                else
                {
                    UIElement.MouseOverProperty.OnOriginValueChanged(oldMouseOver as DependencyObject,
                        _mouseOver as DependencyObject, ref _mouseOverTreeState);
                    UpdateMouseDirectlyOver(oldMouseOver);
                }
            }
        }

        private void UpdateMouseDirectlyOver(IInputElement oldMouseOver)
        {
            if (oldMouseOver is DependencyObject oldElement)
                oldElement.SetValue(UIElement.IsMouseDirectlyOverPropertyKey,
                    this is PortableMouseDevice && ReferenceEquals(oldMouseOver, _mouseOver));
            if (_mouseOver is DependencyObject currentElement)
                currentElement.SetValue(UIElement.IsMouseDirectlyOverPropertyKey, true);
        }
        internal void LeavePortableSource(PortablePresentationSource source, int timestamp, PortablePointerInput input)
        {
            VerifyAccess();
            if (this is not PortableMouseDevice mouse ||
                (!mouse.IsNativePointerOrigin(source) &&
                    !(input.Kind == PortablePointerEventKind.Cancel && ReferenceEquals(_inputSource, source))))
                return;

            bool cancellation = input.Kind == PortablePointerEventKind.Cancel;
            if (!cancellation && mouse.NativePointerOutside) return;
            PresentationSource frame = _inputSource;
            Point point = _lastPosition;
            if (!cancellation && frame is PortablePresentationSource && !frame.IsDisposed && source.RootVisual != null)
            {
                point = PointUtil.RootToClient(new Point(input.X, input.Y), source);
                if (!ReferenceEquals(frame, source))
                    point = PointUtil.ScreenToClient(PointUtil.ClientToScreen(point, source), frame);
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                    throw new ArgumentOutOfRangeException(nameof(input));
            }
            PresentationSource reportSource = frame is PortablePresentationSource && !frame.IsDisposed ? frame : source;
            var report = new PortableMouseInputReport(InputMode.Foreground, timestamp, reportSource,
                RawMouseActions.Deactivate, 0, 0, 0, IntPtr.Zero, point, input, source);
            mouse.RecordPointerLeave();
            _lastPosition = point;
            _isPhysicallyOver = false;
            _rawMouseOver = null;
            _forceUpdateLastPosition = true;
            if (cancellation && ReferenceEquals(_inputSource, source)) _inputSource = null;
            // Capture governs WPF's logical hover even outside the physical
            // source. Neither a leave nor a foreign source cancel releases it.
            ChangeMouseOver(_mouseCapture, timestamp, report);
        }

        internal void CancelPortableSourceCapture(PortablePresentationSource source, int timestamp, PortablePointerInput input)
        {
            VerifyAccess();
            if (this is not PortableMouseDevice mouse) return;
            bool ownsCapture = _providerCapture != null &&
                ReferenceEquals(source.GetInputProvider(typeof(MouseDevice)), _providerCapture);
            if (mouse.IsNativePointerOrigin(source) || ReferenceEquals(_inputSource, source))
                mouse.RecordPointerLeave(); // Block stale synchronization during capture callbacks.
            ulong pointerRevision = mouse.NativePointerRevision;
            ulong finalCaptureGeneration = unchecked(_captureGeneration + (ownsCapture ? 1UL : 0UL));
            // Lifecycle cancellation is not suppressible ordinary input. Clear
            // capture before any routed callback; the report only carries the
            // native event identity, and never changes the cursor position.
            var report = new PortableMouseInputReport(InputMode.Foreground, timestamp, source,
                RawMouseActions.CancelCapture, 0, 0, 0, IntPtr.Zero, default, input);
            ExceptionDispatchInfo failure = null;
            try
            {
                if (ownsCapture) ChangeMouseCapture(null, null, CaptureMode.None, timestamp, report);
            }
            catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
            try
            {
                // A callback may have entered another source or taken new
                // capture. Only retire hover belonging to this invocation.
                if (report.IsCurrent && pointerRevision == mouse.NativePointerRevision &&
                    finalCaptureGeneration == _captureGeneration)
                    LeavePortableSource(source, timestamp, input);
            }
            catch (Exception exception)
            {
                if (failure != null) throw new AggregateException(failure.SourceException, exception);
                throw;
            }
            failure?.Throw();
        }

        private void ChangeMouseCapture(IInputElement mouseCapture, IMouseInputProvider providerCapture, CaptureMode captureMode, int timestamp,
            PortableMouseInputReport nativeCancellation = null)
        {
            DependencyObject o = null;

            if(mouseCapture != _mouseCapture)
            {
                // Console.WriteLine("ChangeMouseCapture(" + mouseCapture + ")");

                // Update the critical pieces of data.
                IInputElement oldMouseCapture = _mouseCapture;
                _mouseCapture = mouseCapture;
                bool portableCapture = this is PortableMouseDevice;
                ExceptionDispatchInfo failure = null;
                ulong captureGeneration = unchecked(++_captureGeneration);
                if (_mouseCapture != null)
                {
                    _providerCapture = providerCapture;
                }
                else
                {
                    _providerCapture = null;
                }
                _captureMode = captureMode;

                using (Dispatcher.DisableProcessing()) // Disable reentrancy due to locks taken
                {
                    // Adjust the handlers we use to track everything.
                    if (oldMouseCapture != null)
                    {
                        o = oldMouseCapture as DependencyObject;
                        if (o is UIElement uie)
                        {
                            uie.IsEnabledChanged -= _captureIsEnabledChangedEventHandler;
                            uie.IsVisibleChanged -= _captureIsVisibleChangedEventHandler;
                            uie.IsHitTestVisibleChanged -= _captureIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is ContentElement ce)
                        {
                            ce.IsEnabledChanged -= _captureIsEnabledChangedEventHandler;

                            // NOTE: there are no IsVisible or IsHitTestVisible properties for ContentElements.
                            //
                            // ce.IsVisibleChanged -= _captureIsVisibleChangedEventHandler;
                            // ce.IsHitTestVisibleChanged -= _captureIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is UIElement3D uie3D)
                        {
                            uie3D.IsEnabledChanged -= _captureIsEnabledChangedEventHandler;
                            uie3D.IsVisibleChanged -= _captureIsVisibleChangedEventHandler;
                            uie3D.IsHitTestVisibleChanged -= _captureIsHitTestVisibleChangedEventHandler;
                        }
                    }
                    if (_mouseCapture != null)
                    {
                        o = _mouseCapture as DependencyObject;
                        if (o is UIElement uie)
                        {
                            uie.IsEnabledChanged += _captureIsEnabledChangedEventHandler;
                            uie.IsVisibleChanged += _captureIsVisibleChangedEventHandler;
                            uie.IsHitTestVisibleChanged += _captureIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is ContentElement ce)
                        {
                            ce.IsEnabledChanged += _captureIsEnabledChangedEventHandler;

                            // NOTE: there are no IsVisible or IsHitTestVisible properties for ContentElements.
                            //
                            // ce.IsVisibleChanged += _captureIsVisibleChangedEventHandler;
                            // ce.IsHitTestVisibleChanged += _captureIsHitTestVisibleChangedEventHandler;
                        }
                        else if (o is UIElement3D uie3D)
                        {
                            uie3D.IsEnabledChanged += _captureIsEnabledChangedEventHandler;
                            uie3D.IsVisibleChanged += _captureIsVisibleChangedEventHandler;
                            uie3D.IsHitTestVisibleChanged += _captureIsHitTestVisibleChangedEventHandler;
                        }
                    }
                }

                // Oddly enough, update the IsMouseCaptureWithin property first.  This is
                // so any callbacks will see the more-common IsMouseCaptureWithin property
                // set correctly.
                try
                {
                    UIElement.MouseCaptureWithinProperty.OnOriginValueChanged(oldMouseCapture as DependencyObject,
                        _mouseCapture as DependencyObject, ref _mouseCaptureWithinTreeState, portableCapture);
                }
                catch (Exception exception) when (portableCapture) { RecordCaptureFailure(ref failure, exception); }

                // Invalidate the IsMouseCaptured properties.
                if (oldMouseCapture != null)
                {
                    o = oldMouseCapture as DependencyObject;
                    // A portable property callback may already have recaptured
                    // the old element. Do not overwrite that newer state.
                    try
                    {
                        o.SetValue(UIElement.IsMouseCapturedPropertyKey,
                            portableCapture && ReferenceEquals(oldMouseCapture, _mouseCapture));
                    }
                    catch (Exception exception) when (portableCapture) { RecordCaptureFailure(ref failure, exception); }
                }
                if (_mouseCapture != null)
                {
                    o = _mouseCapture as DependencyObject;
                    try { o.SetValue(UIElement.IsMouseCapturedPropertyKey, true); } // Same property for ContentElements
                    catch (Exception exception) when (portableCapture) { RecordCaptureFailure(ref failure, exception); }
                }

                // Send the LostMouseCapture and GotMouseCapture events.
                if (oldMouseCapture != null &&
                    (this is not PortableMouseDevice || !ReferenceEquals(oldMouseCapture, _mouseCapture)))
                {
                    MouseEventArgs lostCapture = PortableMouseEvents.Move(this, timestamp, _stylusDevice,
                        nativeCancellation, Mouse.LostMouseCaptureEvent);
                    lostCapture.Source = oldMouseCapture;
                    //ProcessInput has a linkdemand
                    try { _inputManager.ProcessInput(lostCapture); }
                    catch (Exception exception) when (portableCapture) { RecordCaptureFailure(ref failure, exception); }
                }
                // A LostMouseCapture/property callback can acquire a new capture,
                // including on this same provider. It owns its own Got event and
                // synchronization; the retired transition must not repeat them.
                if (this is PortableMouseDevice && captureGeneration != _captureGeneration)
                {
                    failure?.Throw();
                    return;
                }
                if (_mouseCapture != null)
                {
                    MouseEventArgs gotCapture = new MouseEventArgs(this, timestamp, _stylusDevice)
                    {
                        RoutedEvent = Mouse.GotMouseCaptureEvent,
                        Source = _mouseCapture
                    };
                    //ProcessInput has a linkdemand
                    try { _inputManager.ProcessInput(gotCapture); }
                    catch (Exception exception) when (portableCapture) { RecordCaptureFailure(ref failure, exception); }
                }

                // Force a mouse move so we can update the mouse over.
                if (nativeCancellation == null && (!portableCapture || captureGeneration == _captureGeneration))
                {
                    try
                    {
                        if (this is PortableMouseDevice { NativePointerOutside: true })
                            ChangeMouseOver(_mouseCapture, timestamp);
                        else
                            Synchronize();
                    }
                    catch (Exception exception) when (portableCapture) { RecordCaptureFailure(ref failure, exception); }
                }
                failure?.Throw();
            }
        }

        private static void RecordCaptureFailure(ref ExceptionDispatchInfo failure, Exception exception) =>
            failure = ExceptionDispatchInfo.Capture(failure == null ? exception :
                new AggregateException(failure.SourceException, exception));

        private bool IsActiveSourceOrCapturedProviderCancel(RawMouseInputReport rawMouseInputReport)
        {
            if (rawMouseInputReport.InputSource is PortablePresentationSource &&
                rawMouseInputReport.Actions == RawMouseActions.CancelCapture)
            {
                // A retired provider can still remember having capture after
                // another provider acquired it. Being active is not ownership.
                return _providerCapture != null && ReferenceEquals(_providerCapture,
                    rawMouseInputReport.InputSource.GetInputProvider(typeof(MouseDevice)));
            }
            if ((_inputSource is not null) && (rawMouseInputReport.InputSource == _inputSource))
            {
                return true;
            }

            if (rawMouseInputReport.Actions != RawMouseActions.CancelCapture ||
                _providerCapture == null ||
                rawMouseInputReport.InputSource == null)
            {
                return false;
            }

            IMouseInputProvider inputProvider = rawMouseInputReport.InputSource.GetInputProvider(typeof(MouseDevice)) as IMouseInputProvider;
            return ReferenceEquals(inputProvider, _providerCapture);
        }

        private void PreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            if (e.StagingItem.Input is MouseEventArgs nativeEvent &&
                PortableMouseEvents.GetNativeReport(nativeEvent) is { IsCurrent: false })
            {
                e.Cancel();
                return;
            }
            if (e.StagingItem.Input.RoutedEvent == InputManager.PreviewInputReportEvent)
            {
                InputReportEventArgs inputReportEventArgs = e.StagingItem.Input as InputReportEventArgs;

                if (!inputReportEventArgs.Handled && inputReportEventArgs.Report.Type == InputType.Mouse)
                {
                    RawMouseInputReport rawMouseInputReport = (RawMouseInputReport)inputReportEventArgs.Report;

                    if (!rawMouseInputReport.IsCurrent) { e.Cancel(); return; }


                    // Normally we only process mouse input that is from our
                    // active visual manager.  Activate reports change the visual
                    // manager that is active, and an exact-provider capture
                    // cancellation can arrive while that source is being replaced.
                    if ((rawMouseInputReport.Actions & RawMouseActions.Activate) == RawMouseActions.Activate)
                    {
                        // Console.WriteLine("RawMouseActions.Activate");

                        // If other actions are being reported besides the
                        // activate, separate them into different events.
                        if ((rawMouseInputReport.Actions & ~RawMouseActions.Activate) != 0)
                        {
                            // Cancel this event.  We'll push a new event for the activate.
                            e.Cancel();

                            // Push a new RawMouseInputReport for the non-activate actions.
                            RawMouseInputReport reportActions = rawMouseInputReport.WithActions(
                                                                                        rawMouseInputReport.Actions & ~RawMouseActions.Activate,
                                                                                        rawMouseInputReport.X,
                                                                                        rawMouseInputReport.Y,
                                                                                        rawMouseInputReport.Wheel,
                                                                                        rawMouseInputReport.ExtraInformation);
                            InputReportEventArgs actionsArgs = new InputReportEventArgs(inputReportEventArgs.Device, reportActions)
                            {
                                RoutedEvent = InputManager.PreviewInputReportEvent
                            };
                            e.PushInput(actionsArgs, null);

                            PushActivateInputReport(e, inputReportEventArgs, rawMouseInputReport, clearExtraInformation:false);
                        }
                    }
                    // Process mouse input from our active PresentationSource, plus a pure
                    // capture cancellation from the provider that currently owns capture.
                    else if (IsActiveSourceOrCapturedProviderCancel(rawMouseInputReport))
                    {
                        // We need to remember the StylusDevice that generated this input.  Use the _tagStylusDevice
                        // to store this in before we take over the inputReport Device and loose it.  Any
                        // input reports we re-push need to preserve this too.  This is used to set the StylusDevice
                        // property on MouseEventArgs.
                        InputDevice inputDevice = e.StagingItem.GetData(_tagStylusDevice) as StylusDevice;

                        if (inputDevice == null)
                        {
                            if (StylusLogic.IsPointerStackEnabled
                                && StylusLogic.IsPromotedMouseEvent(rawMouseInputReport))
                            {
                                
                                // Due to the WPF pointer stack not promoting mouse internally, we must first
                                // detect a promoted mouse messages and then fill the stylus device
                                // from the promoted device used.  This comes from the extra information
                                // on the mouse message.
                                uint cursorId = StylusLogic.GetCursorIdFromMouseEvent(rawMouseInputReport);
                                var tablets = Tablet.TabletDevices.As<PointerTabletDeviceCollection>();

                                inputDevice = tablets.GetStylusDeviceByCursorId(cursorId)?.StylusDevice;
                            }
                            else
                            {
                                inputDevice = inputReportEventArgs.Device as StylusDevice;
                            }

                            if (inputDevice != null)
                            {
                                e.StagingItem.SetData(_tagStylusDevice, inputDevice);
                            }
                        }

                        // Claim the input for the mouse.
                        inputReportEventArgs.Device = this;

                        // If the input is reporting mouse deactivation, we need
                        // to ensure that the element receives a final leave.
                        // Note that activation could have been moved to another
                        // visual manager in our app, which means that the leave
                        // was already sent.  So only do this if the deactivate
                        // event is from the visual manager that we think is active.
                        if ((rawMouseInputReport.Actions & RawMouseActions.Deactivate) == RawMouseActions.Deactivate)
                        {
                            if (_mouseOver != null)
                            {
                                // Push back this event, and cancel the current processing.
                                e.PushInput(e.StagingItem);
                                e.Cancel();
                                _isPhysicallyOver = false;
                                ChangeMouseOver(null, e.StagingItem.Input.Timestamp);
                            }
                        }

                        // If the input is reporting mouse movement, we need to check
                        // if we need to update our sense of "mouse over".
                        // RelativeMove, VirtualDesktopMove have not been handled yet
                        if ((rawMouseInputReport.Actions & RawMouseActions.AbsoluteMove) == RawMouseActions.AbsoluteMove)
                        {
                            // If other actions are being reported besides the
                            // move, separate them into different events.
                            if ((rawMouseInputReport.Actions & ~(RawMouseActions.AbsoluteMove | RawMouseActions.QueryCursor)) != 0)
                            {
                                // Cancel this event.  We'll push a new event for the move.
                                e.Cancel();

                                // Push a new RawMouseInputReport for the non-move actions.
                                RawMouseInputReport reportActions = rawMouseInputReport.WithActions(
                                                                                            rawMouseInputReport.Actions & ~(RawMouseActions.AbsoluteMove | RawMouseActions.QueryCursor),
                                                                                            0,
                                                                                            0,
                                                                                            rawMouseInputReport.Wheel,
                                                                                            rawMouseInputReport.ExtraInformation);
                                InputReportEventArgs actionsArgs = new InputReportEventArgs(inputDevice, reportActions)
                                {
                                    RoutedEvent = InputManager.PreviewInputReportEvent
                                };
                                e.PushInput(actionsArgs, null);

                                // Push a new RawMouseInputReport for the AbsoluteMove.
                                RawMouseInputReport reportMove = rawMouseInputReport.WithActions(
                                                                                         rawMouseInputReport.Actions & (RawMouseActions.AbsoluteMove | RawMouseActions.QueryCursor),
                                                                                         rawMouseInputReport.X,
                                                                                         rawMouseInputReport.Y,
                                                                                         0,
                                                                                         IntPtr.Zero);
                                InputReportEventArgs moveArgs = new InputReportEventArgs(inputDevice, reportMove)
                                {
                                    RoutedEvent = InputManager.PreviewInputReportEvent
                                };
                                e.PushInput(moveArgs, null);
                            }
                            else
                            {
                                // Convert the point from client coordinates into "root" coordinates.
                                // We do this in the pre-process stage because it is possible that
                                // this conversion will fail, in which case we want to cancel the
                                // mouse move event.
                                bool success = true;
                                Point ptClient = rawMouseInputReport.ClientPoint;
                                Point ptRoot = PointUtil.TryClientToRoot(ptClient, rawMouseInputReport.InputSource, false, out success);
                                if(success)
                                {
                                    e.StagingItem.SetData(_tagRootPoint, ptRoot);
                                }
                                else
                                {
                                    e.Cancel();
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // All mouse event processing should only happen if we still have an active input source.

                if (_inputSource != null)
                {
                    if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseDownEvent)
                    {
                        MouseButtonEventArgs mouseButtonEventArgs = e.StagingItem.Input as MouseButtonEventArgs;

                        if (_mouseCapture != null && !_isPhysicallyOver)
                        {
                            // The mouse is not physically over the capture point (or
                            // subtree), so raise the PreviewMouseDownOutsideCapturedElement
                            // event first.
                            MouseButtonEventArgs clickThrough = new MouseButtonEventArgs(this, mouseButtonEventArgs.Timestamp, mouseButtonEventArgs.ChangedButton, GetStylusDevice(e.StagingItem))
                            {
                                RoutedEvent = Mouse.PreviewMouseDownOutsideCapturedElementEvent
                            };
                            //ProcessInput has a linkdemand
                            _inputManager.ProcessInput(clickThrough);
                        }
                    }

                    else if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseUpEvent)
                    {
                        MouseButtonEventArgs mouseButtonEventArgs = e.StagingItem.Input as MouseButtonEventArgs;

                        if (_mouseCapture != null && !_isPhysicallyOver)
                        {
                            // The mouse is not physically over the capture point (or
                            // subtree), so raise the PreviewMouseUpOutsideCapturedElement
                            // event first.
                            MouseButtonEventArgs clickThrough = new MouseButtonEventArgs(this, mouseButtonEventArgs.Timestamp, mouseButtonEventArgs.ChangedButton, GetStylusDevice(e.StagingItem))
                            {
                                RoutedEvent = Mouse.PreviewMouseUpOutsideCapturedElementEvent
                            };
                            //ProcessInput has a linkdemand
                            _inputManager.ProcessInput(clickThrough);
                        }
                    }
                }
            }
}

        /// <summary>
        /// Push an Activate input report, on behalf of the given RawMouseInputReport.
        /// Common logic used by MouseDevice.PreProcessInput and PointerDevice.PreProcessMouseInput
        /// </summary>
        internal static void PushActivateInputReport(PreProcessInputEventArgs e, InputReportEventArgs inputReportEventArgs, RawMouseInputReport rawMouseInputReport, bool clearExtraInformation)
        {
            IntPtr extraInformation = clearExtraInformation ? IntPtr.Zero : rawMouseInputReport.ExtraInformation;

            // Create a new RawMouseInputReport for the activate.
            RawMouseInputReport reportActivate = rawMouseInputReport.WithActions(
                                                                         RawMouseActions.Activate,
                                                                         rawMouseInputReport.X,
                                                                         rawMouseInputReport.Y,
                                                                         rawMouseInputReport.Wheel,
                                                                         extraInformation);

            // Push a new RawMouseInputReport for the activate.
            InputReportEventArgs activateArgs = new InputReportEventArgs(inputReportEventArgs.Device, reportActivate)
            {
                RoutedEvent = InputManager.PreviewInputReportEvent
            };
            e.PushInput(activateArgs, null);
        }

        private void PreNotifyInput(object sender, NotifyInputEventArgs e)
        {
            if (e.StagingItem.Input is MouseEventArgs nativeEvent &&
                PortableMouseEvents.GetNativeReport(nativeEvent) is { IsCurrent: false })
            {
                nativeEvent.Handled = true;
                return;
            }
            if ( e.StagingItem.Input.RoutedEvent == InputManager.PreviewInputReportEvent )
            {
                InputReportEventArgs inputReportEventArgs = e.StagingItem.Input as InputReportEventArgs;

                if (!inputReportEventArgs.Handled && inputReportEventArgs.Report.Type == InputType.Mouse)
                {
                    RawMouseInputReport rawMouseInputReport = (RawMouseInputReport) inputReportEventArgs.Report;

                    if (!rawMouseInputReport.IsCurrent) { inputReportEventArgs.Handled = true; return; }

                    // Generally, we need to check against redundant actions.
                    // We never prevent the raw event from going through, but we
                    // will only generate the high-level events for non-redundant
                    // actions.  We store the set of non-redundant actions in
                    // the dictionary of this event.

                    // Get the current Non-Redundant Actions for this event and
                    // make a copy.  We will compare the original value against the copy
                    // at the end of this function and write it back in if changed.

                    RawMouseActions actions = GetNonRedundantActions(e);
                    RawMouseActions originalActions = actions;

                    _stylusDevice = GetStylusDevice(e.StagingItem);

                    // Normally we only process mouse input that is from our
                    // active presentation source.  Activate reports change the visual
                    // manager that is active, and an exact-provider capture
                    // cancellation can arrive while that source is being replaced.
                    if ((rawMouseInputReport.Actions & RawMouseActions.Activate) == RawMouseActions.Activate)
                    {
                        // System.Console.WriteLine("Initializing the mouse state.");

                        actions |= RawMouseActions.Activate;

                        _positionRelativeToOver.X = 0;
                        _positionRelativeToOver.Y = 0;

                        _lastPosition = rawMouseInputReport.ClientPoint;
                        _forceUpdateLastPosition = true;

                        _stylusDevice = inputReportEventArgs.Device as StylusDevice;

                        // if the existing source is null, no need to do any special-case handling
                        if (_inputSource == null)
                        {
                            _inputSource = rawMouseInputReport.InputSource;
                        }
                        // if the new source is the same as the old source, don't bother doing anything
                        else if (_inputSource != rawMouseInputReport.InputSource)
                        {
                            IMouseInputProvider toDeactivate = _inputSource.GetInputProvider(typeof(MouseDevice)) as IMouseInputProvider;

                            // All mouse information is now restricted to this presentation source.
                            _inputSource = rawMouseInputReport.InputSource;

                            toDeactivate?.NotifyDeactivate();
                        }
                    }

                    // Process mouse input from our active presentation source, plus a pure
                    // capture cancellation from the provider that currently owns capture.
                    if (IsActiveSourceOrCapturedProviderCancel(rawMouseInputReport))
                    {
                        // If the input is reporting mouse deactivation, we need
                        // to break any capture we may have.  Note that we only do
                        // this if the presentation source associated with this event
                        // is the same presentation source we are already over.
                        if ((rawMouseInputReport.Actions & RawMouseActions.Deactivate) == RawMouseActions.Deactivate)
                        {
                            // Console.WriteLine("RawMouseActions.Deactivate");
                            Debug.Assert(_mouseOver == null, "_mouseOver should be null because we have called ChangeMouseOver(null) already.");
                            _inputSource = null;

                            ChangeMouseCapture(null, null, CaptureMode.None, e.StagingItem.Input.Timestamp);
                        }

                        if ((rawMouseInputReport.Actions & RawMouseActions.CancelCapture) == RawMouseActions.CancelCapture)
                        {
                            // Console.WriteLine("RawMouseActions.CancelCapture");
                            ChangeMouseCapture(null, null, CaptureMode.None, e.StagingItem.Input.Timestamp);
                        }

                        // If the input is reporting mouse movement, only update the
                        // set of non-redundant actions if the position changed.
                        // RelativeMove, VirtualDesktopMove have not been handled yet
                        if ((rawMouseInputReport.Actions & RawMouseActions.AbsoluteMove) == RawMouseActions.AbsoluteMove)
                        {
                            //Console.WriteLine("RawMouseActions.AbsoluteMove: X=" + rawMouseInputReport.X + " Y=" + rawMouseInputReport.Y );

                            PortableMouseDevice portableMouse = this as PortableMouseDevice;
                            bool nativeReentry = portableMouse?.NativePointerOutside == true &&
                                (rawMouseInputReport.NativePointer != null || !rawMouseInputReport._isSynchronize);
                            portableMouse?.RecordPointerPosition(rawMouseInputReport);
                            ulong positionRevision = portableMouse?.NativePointerRevision ?? 0;

                            // Translate the mouse coordinates to both root relative and "mouseOver" relate.
                            // - Note: "mouseOver" in this case is the element the mouse "was" over before this move.
                            bool mouseOverAvailable = false;
                            Point ptClient = rawMouseInputReport.ClientPoint;
                            Point ptRoot = (Point) e.StagingItem.GetData(_tagRootPoint);
                            Point ptRelativeToOver = InputElement.TranslatePoint(ptRoot, rawMouseInputReport.InputSource.RootVisual, (DependencyObject)_mouseOver, out mouseOverAvailable);

                            IInputElement mouseOver = _mouseOver; // assume mouse is still over whatever it was before
                            IInputElement rawMouseOver = (_rawMouseOver != null) ? (IInputElement)_rawMouseOver.Target : null;
                            bool isPhysicallyOver = _isPhysicallyOver;
                            bool isGlobalChange = !ArePointsClose(ptClient, _lastPosition);  // determine if the mouse actually physically moved

                            // Invoke Hit Test logic to determine what element the mouse will be over AFTER the move is processed.
                            // - Only do this if:
                            //      - The mouse physcially moved (isGlobalChange)
                            //      - We are simulating a mouse move (_isSynchronize)
                            //      - mouseOver isn't availabe (!mouseOverAvailable)  Could be caused by a degenerate transform.
                            // - This is to mitigate the redundant AbsoluteMove notifications associated with QueryCursor
                            if (isGlobalChange || rawMouseInputReport._isSynchronize || !mouseOverAvailable || nativeReentry ||
                                rawMouseInputReport.NativePointer?.Kind == PortablePointerEventKind.Enter)
                            {
                                isPhysicallyOver = true;  // assume mouse is physical over element, we'll set it false if it's due to capture

                                switch (_captureMode)
                                {
                                    // In this case there is no capture, so a simple hit test will determine which element becomes "mouseOver"
                                    case CaptureMode.None:
                                        {
                                            if (rawMouseInputReport._isSynchronize)
                                            {
                                                GlobalHitTest(true, ptClient, _inputSource, out mouseOver, out rawMouseOver);
                                            }
                                            else
                                            {
                                                LocalHitTest(true, ptClient, _inputSource, out mouseOver, out rawMouseOver);
                                            }

                                            if (mouseOver == rawMouseOver)
                                            {
                                                // Since they are the same, there is no reason to process rawMouseOver
                                                rawMouseOver = null;
                                            }

                                            // We understand UIElements and ContentElements.
                                            // If we are over something else (like a raw visual)
                                            // find the containing element.
                                            if (!InputElement.IsValid(mouseOver))
                                            {
                                                mouseOver = InputElement.GetContainingInputElement(mouseOver as DependencyObject);
                                            }
                                            if ((rawMouseOver != null) && !InputElement.IsValid(rawMouseOver))
                                            {
                                                rawMouseOver = InputElement.GetContainingInputElement(rawMouseOver as DependencyObject);
                                            }
                                        }
                                        break;

                                    // In this case, capture is to a specific element, so it will ALWAYS become "mouseOver"
                                    // - however, we do a hit test to see if the mouse is actually physically over the element,
                                    // - if it is not, we toggle isPhysicallyOver
                                    case CaptureMode.Element:
                                        if (rawMouseInputReport._isSynchronize)
                                        {
                                            mouseOver = GlobalHitTest(true, ptClient, _inputSource);
                                        }
                                        else
                                        {
                                            mouseOver = LocalHitTest(true, ptClient, _inputSource);
                                        }

                                        // There is no reason to process rawMouseOver when
                                        // the element should always be the one with mouse capture.
                                        rawMouseOver = null;

                                        if (mouseOver != _mouseCapture)
                                        {
                                            // Always consider the mouse over the capture point.
                                            mouseOver = _mouseCapture;
                                            isPhysicallyOver = false;
                                        }
                                        break;

                                    // In this case, capture is set to an entire subtree.  We use simple hit testing to determine
                                    // which, if any element in the subtree it is over, and set "mouseOver to that element
                                    // If it is not over any specific subtree element, "mouseOver" is set to the root of the subtree.
                                    // - Note: a subtree can span multiple HWNDs
                                    case CaptureMode.SubTree:
                                        {
                                            IInputElement mouseCapture = InputElement.GetContainingInputElement(_mouseCapture as DependencyObject);
                                            if (mouseCapture != null)
                                            {
                                                // We need to re-hit-test to get the "real" UIElement we are over.
                                                // This allows us to have our capture-to-subtree span multiple windows.

                                                // GlobalHitTest always returns an IInputElement, so we are sure to have one.
                                                GlobalHitTest(true, ptClient, _inputSource, out mouseOver, out rawMouseOver);
                                            }

                                            if (mouseOver != null && !InputElement.IsValid(mouseOver) )
                                                mouseOver = InputElement.GetContainingInputElement(mouseOver as DependencyObject);

                                            // Make sure that the element we hit is acutally underneath
                                            // our captured element.  Because we did a global hit test, we
                                            // could have hit an element in a completely different window.
                                            //
                                            // Note that we support the child being in a completely different window.
                                            // So we use the GetUIParent method instead of just looking at
                                            // visual/content parents.
                                            if (mouseOver != null)
                                            {
                                                IInputElement ieTest = mouseOver;
                                                UIElement eTest = null;
                                                ContentElement ceTest = null;
                                                UIElement3D e3DTest = null;

                                                while (ieTest != null && ieTest != mouseCapture)
                                                {
                                                    eTest = ieTest as UIElement;

                                                    if (eTest != null)
                                                    {
                                                        ieTest = InputElement.GetContainingInputElement(eTest.GetUIParent(true));
                                                    }
                                                    else
                                                    {
                                                        ceTest = ieTest as ContentElement;

                                                        if (ceTest != null)
                                                        {
                                                            ieTest = InputElement.GetContainingInputElement(ceTest.GetUIParent(true));
                                                        }
                                                        else
                                                        {
                                                            e3DTest = ieTest as UIElement3D; // Should never fail.

                                                            ieTest = InputElement.GetContainingInputElement(e3DTest.GetUIParent(true));
                                                        }
                                                    }
                                                }

                                                // If we missed the capture point, we didn't hit anything.
                                                if (ieTest != mouseCapture)
                                                {
                                                    mouseOver = _mouseCapture;
                                                    isPhysicallyOver = false;

                                                    // Since they are the same, there is no reason to process rawMouseOver
                                                    rawMouseOver = null;
                                                }
                                            }
                                            else
                                            {
                                                // We didn't hit anything.  Consider the mouse over the capture point.
                                                mouseOver = _mouseCapture;
                                                isPhysicallyOver = false;

                                                // Since they are the same, there is no reason to process rawMouseOver
                                                rawMouseOver = null;
                                            }

                                            if (rawMouseOver != null)
                                            {
                                                if (mouseOver == rawMouseOver)
                                                {
                                                    // Since they are the same, there is no reason to process rawMouseOver
                                                    rawMouseOver = null;
                                                }
                                                else if (!InputElement.IsValid(rawMouseOver))
                                                {
                                                    rawMouseOver = InputElement.GetContainingInputElement(rawMouseOver as DependencyObject);
                                                }
                                            }
                                        }
                                        break;
                                }
                            }

                            if (!rawMouseInputReport.IsCurrent ||
                                (portableMouse != null && positionRevision != portableMouse.NativePointerRevision))
                            {
                                inputReportEventArgs.Handled = true;
                                return;
                            }
                            _isPhysicallyOver = mouseOver == null ? false : isPhysicallyOver;

                            // Now that we've determine what element the mouse is over now (mouseOver)
                            // - we need to check if it's changed

                            bool isMouseOverChange = mouseOver != _mouseOver;

                            // If mouseOver changed, we need to recalculate the ptRelativeToOver, because "Over" changed!

                            if (isMouseOverChange)
                            {
                                ptRelativeToOver = InputElement.TranslatePoint(ptRoot, rawMouseInputReport.InputSource.RootVisual, (DependencyObject)mouseOver);
                            }

                            //Console.WriteLine("RawMouseActions.AbsoluteMove: mouse moved over " + (isMouseOverChange ? "same" : "different") + " element.  old=" + _mouseOver + " new=" + mouseOver);
                            //Console.WriteLine("RawMouseActions.AbsoluteMove: capture=" + _mouseCapture);

                            // Check to see if the local mouse position changed.  This can be
                            // caused by a change to the geometry of the
                            // element we are over or a change in which element
                            // we are over.
                            //
                            bool isLocalChange = isMouseOverChange || !ArePointsClose(ptRelativeToOver, _positionRelativeToOver);

                            // Console.WriteLine("RawMouseActions.AbsoluteMove: isGlobalChange=" + isGlobalChange + " isLocalChange=" + isLocalChange);

                            // We only update our cached position (_lastPosition & _positionRelativeToOver )
                            // if we have moved "far enough" allowing small incrementaly moves to accumulate

                            if (isGlobalChange || isLocalChange || _forceUpdateLastPosition)
                            {
                                _forceUpdateLastPosition = false;

                                _lastPosition = ptClient;
                                _positionRelativeToOver = ptRelativeToOver;

                                if (isMouseOverChange)
                                {
                                    ChangeMouseOver(mouseOver, e.StagingItem.Input.Timestamp,
                                        rawMouseInputReport as PortableMouseInputReport);
                                }
                                if (!rawMouseInputReport.IsCurrent ||
                                    (portableMouse != null && positionRevision != portableMouse.NativePointerRevision))
                                {
                                    inputReportEventArgs.Handled = true;
                                    return;
                                }

                                if ((_rawMouseOver == null) && (rawMouseOver != null))
                                {
                                    _rawMouseOver = new WeakReference(rawMouseOver);
                                }
                                else
                                {
                                    _rawMouseOver?.Target = rawMouseOver;
                                }

                                // Console.WriteLine("RawMouseActions.AbsoluteMove: ptRoot=" + ptRoot);
                                // RelativeMove, VirtualDesktopMove have not been handled yet
                                actions |= RawMouseActions.AbsoluteMove;

                                // In most cases the sequence of messages received from the system are HitTest, SetCursor & MouseMove.
                                // The SetCursor message in this case will be traslated into an Avalon MouseMove & QueryCursor.
                                // The MouseMove message to follow is redundant and is thrown away.
                                // But imagine a case where Capture is taken. Here the system produces only two messages HitTest & MouseMove.
                                // Hence we translate the MouseMove into an Avalon MouseMove & QueryCursor.
                                // Logically MouseMove and QueryCursor go as a pair.
                                actions |= RawMouseActions.QueryCursor;
                            }
                        }

                        // Mouse wheel rotate events are never considered redundant.
                        if ((rawMouseInputReport.Actions & RawMouseActions.VerticalWheelRotate) == RawMouseActions.VerticalWheelRotate)
                        {
                            // Console.WriteLine("RawMouseActions.VerticalWheelRotate");

                            actions |= RawMouseActions.VerticalWheelRotate;

                            // Tell the InputManager that the MostRecentDevice is us.
                            _inputManager.MostRecentInputDevice = this;
                        }

                        // Mouse query cursor events are never considered redundant.
                        if ((rawMouseInputReport.Actions & RawMouseActions.QueryCursor) == RawMouseActions.QueryCursor)
                        {
                            // Console.WriteLine("RawMouseActions.QueryCursor");

                            actions |= RawMouseActions.QueryCursor;
                        }

                        ReadOnlySpan<RawMouseActions> ButtonPressActions = stackalloc RawMouseActions[5]
                        {
                            RawMouseActions.Button1Press,
                            RawMouseActions.Button2Press,
                            RawMouseActions.Button3Press,
                            RawMouseActions.Button4Press,
                            RawMouseActions.Button5Press
                        };

                        ReadOnlySpan<RawMouseActions> ButtonReleaseActions = stackalloc RawMouseActions[5]
                        {
                            RawMouseActions.Button1Release,
                            RawMouseActions.Button2Release,
                            RawMouseActions.Button3Release,
                            RawMouseActions.Button4Release,
                            RawMouseActions.Button5Release
                        };

                        for (int iButton = 0; iButton < 5; iButton++)
                        {
                            if ((rawMouseInputReport.Actions & ButtonPressActions[iButton]) == ButtonPressActions[iButton])
                            {
                                actions |= ButtonPressActions[iButton];

                                // Tell the InputManager that the MostRecentDevice is us.
                                _inputManager.MostRecentInputDevice = this;
                            }

                            if ((rawMouseInputReport.Actions & ButtonReleaseActions[iButton]) == ButtonReleaseActions[iButton])
                            {
                                actions |= ButtonReleaseActions[iButton];

                                // Tell the InputManager that the MostRecentDevice is us.
                                _inputManager.MostRecentInputDevice = this;
                            }
                        }
                    }

                    if (actions != originalActions)
                    {
                        e.StagingItem.SetData(_tagNonRedundantActions, actions);
                    }
                }
            }
            else
            {
                // All mouse event processing should only happen if we still have an active input source.

                if (_inputSource != null)
                {
                    // During the PreviewMouseDown event, we update the click count, if there are
                    // multiple "quick" clicks in approximately the "same" location (as defined
                    // by the hosting environment, aka the registry).
                    if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseDownEvent)
                    {
                        MouseButtonEventArgs mouseButtonArgs = e.StagingItem.Input as MouseButtonEventArgs;
                        StylusDevice stylusDevice = GetStylusDevice(e.StagingItem);
                        Point ptClient = GetClientPosition();

                        _clickCount = PortableMouseEvents.GetNativePointer(mouseButtonArgs)?.ClickCount ??
                            CalculateClickCount(mouseButtonArgs.ChangedButton, mouseButtonArgs.Timestamp, stylusDevice, ptClient);
                        if (_clickCount == 1)
                        {
                            // we need to reset out data, since this is the start of the click count process...
                            _lastClick = ptClient;
                            _lastButton = mouseButtonArgs.ChangedButton;
                            _lastClickTime = mouseButtonArgs.Timestamp;
                        }
                        // Put the updated count into the args.
                        mouseButtonArgs.ClickCount = _clickCount;
                    }
                }
            }
        }

        // Due to the inexactness of math calculations of
        // floating-point numbers, we use the AreClose method
        // to determine if the coordinates are close enough
        // to consider the same.

        private bool ArePointsClose(Point A, Point B)
        {
            return MS.Internal.DoubleUtil.AreClose(A.X, B.X) && MS.Internal.DoubleUtil.AreClose(A.Y, B.Y);
        }

        private void PostProcessInput(object sender, ProcessInputEventArgs e)
        {
            if (e.StagingItem.Input is MouseEventArgs nativeEvent &&
                PortableMouseEvents.GetNativeReport(nativeEvent) is { IsCurrent: false })
            {
                nativeEvent.Handled = true;
                return;
            }
            // PreviewMouseWheel --> MouseWheel
            if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseWheelEvent)
            {
                if (!e.StagingItem.Input.Handled)
                {
                    MouseWheelEventArgs previewWheel = (MouseWheelEventArgs) e.StagingItem.Input;
                    MouseWheelEventArgs wheel = new MouseWheelEventArgs(this, previewWheel.Timestamp, previewWheel.Delta)
                    {
                        RoutedEvent = Mouse.MouseWheelEvent
                    };

#if SEND_WHEEL_EVENTS_TO_FOCUS
                    // wheel events are treated as if they came from the
                    // element with keyboard focus
                    wheel.Source = previewWheel.Source;
#endif

                    e.PushInput(wheel, e.StagingItem);
                }
            }

            // PreviewMouseDown --> MouseDown
            if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseDownEvent)
            {
                if (!e.StagingItem.Input.Handled)
                {
                    MouseButtonEventArgs previewDown = (MouseButtonEventArgs) e.StagingItem.Input;
                    MouseButtonEventArgs down = PortableMouseEvents.Button(this, previewDown.Timestamp,
                        previewDown.ChangedButton, GetStylusDevice(e.StagingItem),
                        PortableMouseEvents.GetNativeReport(previewDown), Mouse.MouseDownEvent, previewDown.ClickCount);
                    e.PushInput(down, e.StagingItem);
                }
            }

            // PreviewMouseUp --> MouseUp
            if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseUpEvent)
            {
                if (!e.StagingItem.Input.Handled)
                {
                    MouseButtonEventArgs previewUp = (MouseButtonEventArgs) e.StagingItem.Input;
                    MouseButtonEventArgs up = PortableMouseEvents.Button(this, previewUp.Timestamp,
                        previewUp.ChangedButton, GetStylusDevice(e.StagingItem),
                        PortableMouseEvents.GetNativeReport(previewUp), Mouse.MouseUpEvent);
                    e.PushInput(up, e.StagingItem);
                }
            }

            // PreviewMouseMove --> MouseMove
            if (e.StagingItem.Input.RoutedEvent == Mouse.PreviewMouseMoveEvent)
            {
                if (!e.StagingItem.Input.Handled)
                {
                    MouseEventArgs previewMove = (MouseEventArgs) e.StagingItem.Input;
                    MouseEventArgs move = PortableMouseEvents.Move(this, previewMove.Timestamp,
                        GetStylusDevice(e.StagingItem), PortableMouseEvents.GetNativeReport(previewMove), Mouse.MouseMoveEvent);
                    e.PushInput(move, e.StagingItem);
                }
            }

            // We are finished processing the QueryCursor event.  Just update the
            // mouse cursor to be what was decided during the event route.
            if (e.StagingItem.Input.RoutedEvent == Mouse.QueryCursorEvent)
            {
                QueryCursorEventArgs queryCursor = (QueryCursorEventArgs)e.StagingItem.Input;


                SetCursor(queryCursor.Cursor);
}

            if (e.StagingItem.Input.RoutedEvent == InputManager.InputReportEvent)
            {
                InputReportEventArgs inputReportEventArgs = e.StagingItem.Input as InputReportEventArgs;

                if (!inputReportEventArgs.Handled && inputReportEventArgs.Report.Type == InputType.Mouse)
                {
                    RawMouseInputReport rawMouseInputReport = (RawMouseInputReport) inputReportEventArgs.Report;

                    if (!rawMouseInputReport.IsCurrent) { inputReportEventArgs.Handled = true; return; }

                    // Only process mouse input that is from our active visual manager.
                    if ((_inputSource is not null) && (rawMouseInputReport.InputSource == _inputSource))
                    {
                        // In general, this is where we promote the non-redundant
                        // reported actions to our premier events.
                        RawMouseActions actions = GetNonRedundantActions(e);

                        // Raw Activate --> Raw MouseMove
                        // Whenever the mouse device is activated we need to
                        // cause a mouse move so that elements realize that
                        // the mouse is over them again.  In most cases, the
                        // action that caused the mouse to activate is a move,
                        // but this is to guard against any other cases.
                        // Native pointer activation always precedes its exact
                        // AbsoluteMove. Do not inject an integer-rounded legacy
                        // synchronization event before that native position.
                        if ((actions & RawMouseActions.Activate) == RawMouseActions.Activate &&
                            rawMouseInputReport.NativePointer == null)
                        {
                            Synchronize();
                        }

                        // Raw --> PreviewMouseWheel
                        // HorizontalWheelRotate hasn't been handled yet
                        if ((actions & RawMouseActions.VerticalWheelRotate) == RawMouseActions.VerticalWheelRotate)
                        {
                            MouseWheelEventArgs previewWheel = new MouseWheelEventArgs(this, rawMouseInputReport.Timestamp, rawMouseInputReport.Wheel)
                            {
                                RoutedEvent = Mouse.PreviewMouseWheelEvent
                            };

#if SEND_WHEEL_EVENTS_TO_FOCUS
                            // wheel events are treated as if they came from the
                            // element with keyboard focus
                            DependencyObject focus = Keyboard.FocusedElement as DependencyObject;
                            if (focus != null)
                            {
                                previewWheel.Source = focus;
                            }
#endif

                            e.PushInput(previewWheel, e.StagingItem);
                        }

                        // Raw --> PreviewMouseDown
                        if ((actions & RawMouseActions.Button1Press) == RawMouseActions.Button1Press)
                        {
                            MouseButtonEventArgs previewDown = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.Left, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseDownEvent);
                            e.PushInput(previewDown, e.StagingItem);
                        }

                        // Raw --> PreviewMouseUp
                        if ((actions & RawMouseActions.Button1Release) == RawMouseActions.Button1Release)
                        {
                            MouseButtonEventArgs previewUp = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.Left, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseUpEvent);
                            e.PushInput(previewUp, e.StagingItem);
                        }

                        // Raw --> PreviewMouseDown
                        if ((actions & RawMouseActions.Button2Press) == RawMouseActions.Button2Press)
                        {
                            MouseButtonEventArgs previewDown = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.Right, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseDownEvent);
                            e.PushInput(previewDown, e.StagingItem);
                        }

                        // Raw --> PreviewMouseUp
                        if ((actions & RawMouseActions.Button2Release) == RawMouseActions.Button2Release)
                        {
                            MouseButtonEventArgs previewUp = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.Right, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseUpEvent);
                            e.PushInput(previewUp, e.StagingItem);
                        }

                        // Raw --> PreviewMouseDown
                        if ((actions & RawMouseActions.Button3Press) == RawMouseActions.Button3Press)
                        {
                            MouseButtonEventArgs previewDown = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.Middle, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseDownEvent);
                            e.PushInput(previewDown, e.StagingItem);
                        }

                        // Raw --> PreviewMouseUp
                        if ((actions & RawMouseActions.Button3Release) == RawMouseActions.Button3Release)
                        {
                            MouseButtonEventArgs previewUp = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.Middle, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseUpEvent);
                            e.PushInput(previewUp, e.StagingItem);
                        }

                        // Raw --> PreviewMouseDown
                        if ((actions & RawMouseActions.Button4Press) == RawMouseActions.Button4Press)
                        {
                            MouseButtonEventArgs previewDown = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.XButton1, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseDownEvent);
                            e.PushInput(previewDown, e.StagingItem);
                        }

                        // Raw --> PreviewMouseUp
                        if ((actions & RawMouseActions.Button4Release) == RawMouseActions.Button4Release)
                        {
                            MouseButtonEventArgs previewUp = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.XButton1, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseUpEvent);
                            e.PushInput(previewUp, e.StagingItem);
                        }

                        // Raw --> PreviewMouseDown
                        if ((actions & RawMouseActions.Button5Press) == RawMouseActions.Button5Press)
                        {
                            MouseButtonEventArgs previewDown = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.XButton2, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseDownEvent);
                            e.PushInput(previewDown, e.StagingItem);
                        }

                        // Raw --> PreviewMouseUp
                        if ((actions & RawMouseActions.Button5Release) == RawMouseActions.Button5Release)
                        {
                            MouseButtonEventArgs previewUp = PortableMouseEvents.Button(this,
                                rawMouseInputReport.Timestamp, MouseButton.XButton2, GetStylusDevice(e.StagingItem),
                                rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseUpEvent);
                            e.PushInput(previewUp, e.StagingItem);
                        }

                        // Raw --> PreviewMouseMove
                        // RelativeMove, VirtualDesktopMove haven't been handled yet
                        if ((actions & RawMouseActions.AbsoluteMove) == RawMouseActions.AbsoluteMove)
                        {
                            MouseEventArgs previewMove = PortableMouseEvents.Move(this, rawMouseInputReport.Timestamp,
                                GetStylusDevice(e.StagingItem), rawMouseInputReport as PortableMouseInputReport, Mouse.PreviewMouseMoveEvent);
                            e.PushInput(previewMove, e.StagingItem);
                        }

                        // Raw --> QueryCursor
                        if ((actions & RawMouseActions.QueryCursor) == RawMouseActions.QueryCursor)
                        {
                            inputReportEventArgs.Handled = UpdateCursorPrivate();
                        }
                    }
                }
            }
        }

        private RawMouseActions GetNonRedundantActions(NotifyInputEventArgs e)
        {
            RawMouseActions actions = new RawMouseActions();

            // The CLR throws a null-ref exception if it tries to unbox a
            // null.  So we have to special case that.
            object o = e.StagingItem.GetData(_tagNonRedundantActions);
            if (o != null)
            {
                actions = (RawMouseActions) o;
            }

            return actions;
        }

        internal static IInputElement GlobalHitTest(bool clientUnits, Point pt, PresentationSource inputSource)
        {
            IInputElement enabledHit;
            IInputElement originalHit;
            GlobalHitTest(clientUnits, pt, inputSource, out enabledHit, out originalHit);

            return enabledHit;
        }

        internal static IInputElement GlobalHitTest(Point ptClient, PresentationSource inputSource)
        {
            return GlobalHitTest(true, ptClient, inputSource);
        }

        // Take a point relative the the specified visual manager, and translate
        // up to the screen, hit-test to a window, and then hit-test down to an
        // element.
        private static void GlobalHitTest(bool clientUnits, Point pt, PresentationSource inputSource, out IInputElement enabledHit, out IInputElement originalHit)
        {
            enabledHit = originalHit = null;

            Point ptClient = clientUnits ? pt : PointUtil.RootToClient(pt, inputSource);

            // Note: this only works for HWNDs for now.
            HwndSource source = inputSource as HwndSource;
            if (OperatingSystem.IsWindows() && source != null && source.CompositionTarget != null && !source.IsHandleNull)
            {
                Point ptScreen = PointUtil.ClientToScreen(ptClient, source);
                IntPtr hwndHit = IntPtr.Zero ;
                HwndSource sourceHit = null ;

                // Find the HWND under the point.
                hwndHit = UnsafeNativeMethods.WindowFromPoint((int)ptScreen.X, (int)ptScreen.Y);

                // Make sure the window is enabled!
                if (!SafeNativeMethods.IsWindowEnabled(new HandleRef(null, hwndHit)))
                {
                    hwndHit = IntPtr.Zero;
                }

                if (hwndHit != IntPtr.Zero)
                {
                    // See if this is one of our windows.
                    sourceHit = HwndSource.CriticalFromHwnd(hwndHit);
                }
                if (sourceHit != null && sourceHit.Dispatcher == inputSource.CompositionTarget.Dispatcher)
                {
                    Point ptClientHit = PointUtil.ScreenToClient(ptScreen, sourceHit);

                    // Perform a local hit-test within this visual manager.
                    LocalHitTest(true, ptClientHit, sourceHit, out enabledHit, out originalHit);
                }
            }
            else if (inputSource?.CompositionTarget != null && !inputSource.CompositionTarget.IsDisposed)
            {
                LocalHitTest(clientUnits, pt, inputSource, out enabledHit, out originalHit);
            }
        }

        internal static IInputElement LocalHitTest(bool clientUnits, Point pt, PresentationSource inputSource)
        {
            IInputElement enabledHit;
            IInputElement originalHit;
            LocalHitTest(clientUnits, pt, inputSource, out enabledHit, out originalHit);

            return enabledHit;
        }

        internal static IInputElement LocalHitTest(Point ptClient, PresentationSource inputSource)
        {
            return LocalHitTest(true, ptClient, inputSource);
        }

        // Take a point relative the the specified visual manager and hit-test
        // down to an element.
        private static void LocalHitTest(bool clientUnits, Point pt, PresentationSource inputSource, out IInputElement enabledHit, out IInputElement originalHit)
        {
            enabledHit = originalHit = null;

            // Hit-test starting from the root UIElement.
            // Note: this restricts us to windows with UIElement as the root (not just visuals).
            if (inputSource != null)
            {
                UIElement root = inputSource.RootVisual as UIElement;
                if(root != null)
                {
                    Point rootPt = clientUnits ? PointUtil.ClientToRoot(pt, inputSource) : pt;
                    if (inputSource is PortablePresentationSource portableSource &&
                        portableSource.TryHitTestOverride(rootPt, out enabledHit, out originalHit))
                    {
                        return;
                    }

                    root.InputHitTest(rootPt, out enabledHit, out originalHit);
                }
            }
        }

        internal bool IsSameSpot(Point newPosition, StylusDevice stylusDevice)
        {
            int doubleClickDeltaX = (stylusDevice != null)?stylusDevice.DoubleTapDeltaX:_doubleClickDeltaX;
            int doubleClickDeltaY = (stylusDevice != null)?stylusDevice.DoubleTapDeltaY:_doubleClickDeltaY;

            // Is the delta coordinates of this click close enough to the last click?
            return (Math.Abs(newPosition.X - _lastClick.X) < doubleClickDeltaX) &&
                   (Math.Abs(newPosition.Y - _lastClick.Y) < doubleClickDeltaY);
        }

        internal int CalculateClickCount(MouseButton button, int timeStamp, StylusDevice stylusDevice, Point downPt)
        {
            // How long since the last click?
            int timeSpan = timeStamp - _lastClickTime;

            int doubleClickDeltaTime = (stylusDevice != null)?stylusDevice.DoubleTapDeltaTime:_doubleClickDeltaTime;

            // Is the delta coordinates of this click close enough to the last click?
            bool isSameSpot = IsSameSpot(downPt, stylusDevice);

            // Is this the same mouse button as the last click?
            bool isSameButton = (_lastButton == button);

            // Now check everything to see if this is a multi-click.
            if (timeSpan < doubleClickDeltaTime
                  && isSameSpot
                  && isSameButton)
            {
                // Yes, increment the count
                return _clickCount +1;
            }
            else
            {
                // No, not a multi-click.
                return 1;
            }
        }

        internal Point PositionRelativeToOver
        {
            get
            {
                return _positionRelativeToOver;
            }
        }

        internal Point NonRelativePosition
        {
            get
            {
                return _lastPosition;
            }
        }

        internal bool IsActive
        {
            get
            {
                return _inputSource != null;
            }
        }

        // Helper to access the StylusDevice property.
        private StylusDevice GetStylusDevice(StagingAreaInputItem stagingItem)
        {
            return stagingItem.GetData(_tagStylusDevice) as StylusDevice;
        }

        internal StylusDevice StylusDevice
        {
            get
            {
                return _stylusDevice;
            }
        }

        private DeferredElementTreeState MouseOverTreeState
        {
            get
            {
                if (_mouseOverTreeState == null)
                {
                    _mouseOverTreeState = new DeferredElementTreeState();
                }

                return _mouseOverTreeState;
            }
        }

        private DeferredElementTreeState MouseCaptureWithinTreeState
        {
            get
            {
                if (_mouseCaptureWithinTreeState == null)
                {
                    _mouseCaptureWithinTreeState = new DeferredElementTreeState();
                }

                return _mouseCaptureWithinTreeState;
            }
        }

        private PresentationSource _inputSource;

        private InputManager _inputManager;

        private IInputElement _mouseOver;
        private DeferredElementTreeState _mouseOverTreeState;
        private bool _isPhysicallyOver;
        private WeakReference _rawMouseOver;

        private IInputElement _mouseCapture;
        private ulong _captureGeneration;
        private DeferredElementTreeState _mouseCaptureWithinTreeState;
        private IMouseInputProvider _providerCapture;
        private CaptureMode _captureMode;
        private bool _isCaptureMouseInProgress;

        private DependencyPropertyChangedEventHandler _overIsEnabledChangedEventHandler;
        private DependencyPropertyChangedEventHandler _overIsVisibleChangedEventHandler;
        private DependencyPropertyChangedEventHandler _overIsHitTestVisibleChangedEventHandler;
        private DispatcherOperationCallback _reevaluateMouseOverDelegate;
        private DispatcherOperation _reevaluateMouseOverOperation;

        private DependencyPropertyChangedEventHandler _captureIsEnabledChangedEventHandler;
        private DependencyPropertyChangedEventHandler _captureIsVisibleChangedEventHandler;
        private DependencyPropertyChangedEventHandler _captureIsHitTestVisibleChangedEventHandler;
        private DispatcherOperationCallback _reevaluateCaptureDelegate;
        private DispatcherOperation _reevaluateCaptureOperation;

        // Device state we track
        private Point _positionRelativeToOver = new Point();
        private Point _lastPosition = new Point();
        private bool _forceUpdateLastPosition = false;

        // Data tags for information we pass around the staging area.
        private object _tagNonRedundantActions = new object();
        private object _tagStylusDevice = new object();
        private object _tagRootPoint = new object();

        // Information used to distinguish double-clicks (actually, multi clicks) from
        // multiple independent clicks.
        private Point _lastClick = new Point();
        private MouseButton _lastButton;
        private int _clickCount;
        private int _lastClickTime;
        private int _doubleClickDeltaTime;
        private int _doubleClickDeltaX;
        private int _doubleClickDeltaY;

        private Cursor _overrideCursor;

        // Reference to StylusDevice to defer to for physical mouse state (position/button state)
        private StylusDevice _stylusDevice = null;
    }
}
