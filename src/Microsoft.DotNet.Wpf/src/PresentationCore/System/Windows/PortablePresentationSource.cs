// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal;
using ProGPU.Wpf.Interop;
using System.Buffers;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading;

namespace System.Windows
{
    /// <summary>
    /// Presentation source for non-HWND hosts.
    /// </summary>
    internal sealed class PortablePresentationSource : PresentationSource, IPortablePresentationSourceHost, IWin32Window, IDisposable
    {
        private readonly PortableCompositionTarget _compositionTarget;
        private readonly PortableKeyboardInputProvider _keyboardInputProvider;
        private readonly PortableMouseInputProvider _mouseInputProvider;
        private readonly HwndSource _portableHwndSource;
        private readonly IntPtr _handle;
        private const int HitTestOwnerBufferCapacity = 64;
        private Visual _rootVisual;
        private Size _clientSize;
        private Point _clientOrigin;
        private Func<double, double, object> _hostHitTestOverride;
        private Func<double, double, object[]> _hostHitTestAllOverride;
        private PortableHitTestAllBufferOverride _hostHitTestAllBufferOverride;
        private Func<double, double, double, double, object[]> _hostHitTestBoundsOverride;
        private PortableGeometryHitTestBufferOverride _hostHitTestBoundsBufferOverride;
        private Func<double, double, double, double, object[]> _hostHitTestEllipseBoundsOverride;
        private PortableGeometryHitTestBufferOverride _hostHitTestEllipseBoundsBufferOverride;
        private bool _hasClientSize;
        private bool _contentRenderedQueued;
        private bool _isDisposed;
        private static long s_nextPortableHandle = 0x505750460000;

        internal PortablePresentationSource()
            : this(1.0, 1.0)
        {
        }

        internal PortablePresentationSource(double dpiScaleX, double dpiScaleY)
        {
            _handle = new IntPtr(Interlocked.Increment(ref s_nextPortableHandle));
            _compositionTarget = new PortableCompositionTarget(dpiScaleX, dpiScaleY);
            _portableHwndSource = HwndSource.CreatePortable(this, _handle, dpiScaleX, dpiScaleY);
            _keyboardInputProvider = new PortableKeyboardInputProvider(this);
            _mouseInputProvider = new PortableMouseInputProvider(this);
            AddSource();
        }

        internal event EventHandler RenderRequested;

        internal event EventHandler Disposed;

        internal event EventHandler CursorRequested;

        internal IntPtr Handle
        {
            get { return _isDisposed ? IntPtr.Zero : _handle; }
        }

        internal Cursor RequestedCursor { get; private set; }

        internal HwndSource HwndSource
        {
            get { return _isDisposed ? null : _portableHwndSource; }
        }

        internal Func<Point, object> HitTestOverride { get; set; }

        internal Func<Point, object[]> HitTestAllOverride { get; set; }

        internal PortableHitTestAllBufferOverride HitTestAllBufferOverride { get; set; }

        internal Func<Point, Point, object[]> HitTestBoundsOverride { get; set; }

        internal PortableGeometryHitTestBufferOverride HitTestBoundsBufferOverride { get; set; }

        internal Func<Point, Point, object[]> HitTestEllipseBoundsOverride { get; set; }

        internal PortableGeometryHitTestBufferOverride HitTestEllipseBoundsBufferOverride { get; set; }

        event EventHandler IPortablePresentationSourceHost.RenderRequested
        {
            add { RenderRequested += value; }
            remove { RenderRequested -= value; }
        }

        event EventHandler IPortablePresentationSourceHost.CursorRequested
        {
            add { CursorRequested += value; }
            remove { CursorRequested -= value; }
        }

        object IPortablePresentationSourceHost.RootVisual
        {
            get { return RootVisual; }
            set
            {
                if (value != null && value is not Visual)
                {
                    throw new ArgumentException("Portable presentation source root must be a Visual.", nameof(value));
                }

                RootVisual = (Visual)value;
            }
        }

        object IPortablePresentationSourceHost.CompositionTarget
        {
            get { return _isDisposed ? null : _compositionTarget; }
        }

        IntPtr IPortablePresentationSourceHost.Handle
        {
            get { return _isDisposed ? IntPtr.Zero : _handle; }
        }

        IntPtr IWin32Window.Handle
        {
            get { return Handle; }
        }

        object IPortablePresentationSourceHost.RequestedCursor
        {
            get { return RequestedCursor; }
        }

        string IPortablePresentationSourceHost.RequestedCursorName
        {
            get { return RequestedCursor?.ToString(); }
        }

        Func<double, double, object> IPortablePresentationSourceHost.HitTestOverride
        {
            get { return _hostHitTestOverride; }
            set
            {
                _hostHitTestOverride = value;
                HitTestOverride = value == null ? null : (point) => value(point.X, point.Y);
            }
        }

        Func<double, double, object[]> IPortablePresentationSourceHost.HitTestAllOverride
        {
            get { return _hostHitTestAllOverride; }
            set
            {
                _hostHitTestAllOverride = value;
                HitTestAllOverride = value == null ? null : (point) => value(point.X, point.Y);
            }
        }

        PortableHitTestAllBufferOverride IPortablePresentationSourceHost.HitTestAllBufferOverride
        {
            get { return _hostHitTestAllBufferOverride; }
            set
            {
                _hostHitTestAllBufferOverride = value;
                HitTestAllBufferOverride = value == null ? null : (double x, double y, Span<object> results, out int resultCount) => value(x, y, results, out resultCount);
            }
        }

        Func<double, double, double, double, object[]> IPortablePresentationSourceHost.HitTestBoundsOverride
        {
            get { return _hostHitTestBoundsOverride; }
            set
            {
                _hostHitTestBoundsOverride = value;
                HitTestBoundsOverride = value == null
                    ? null
                    : (min, max) => value(min.X, min.Y, max.X, max.Y);
            }
        }

        PortableGeometryHitTestBufferOverride IPortablePresentationSourceHost.HitTestBoundsBufferOverride
        {
            get { return _hostHitTestBoundsBufferOverride; }
            set
            {
                _hostHitTestBoundsBufferOverride = value;
                HitTestBoundsBufferOverride = value == null
                    ? null
                    : (double minX, double minY, double maxX, double maxY, Span<object> results, out int resultCount) => value(minX, minY, maxX, maxY, results, out resultCount);
            }
        }

        Func<double, double, double, double, object[]> IPortablePresentationSourceHost.HitTestEllipseBoundsOverride
        {
            get { return _hostHitTestEllipseBoundsOverride; }
            set
            {
                _hostHitTestEllipseBoundsOverride = value;
                HitTestEllipseBoundsOverride = value == null
                    ? null
                    : (min, max) => value(min.X, min.Y, max.X, max.Y);
            }
        }

        PortableGeometryHitTestBufferOverride IPortablePresentationSourceHost.HitTestEllipseBoundsBufferOverride
        {
            get { return _hostHitTestEllipseBoundsBufferOverride; }
            set
            {
                _hostHitTestEllipseBoundsBufferOverride = value;
                HitTestEllipseBoundsBufferOverride = value == null
                    ? null
                    : (double minX, double minY, double maxX, double maxY, Span<object> results, out int resultCount) => value(minX, minY, maxX, maxY, results, out resultCount);
            }
        }

        void IPortablePresentationSourceHost.SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            SetDeviceScale(dpiScaleX, dpiScaleY);
        }

        void IPortablePresentationSourceHost.SetClientSize(double width, double height)
        {
            SetClientSize(width, height);
        }

        void IPortablePresentationSourceHost.SetClientOrigin(double x, double y)
        {
            SetClientOrigin(x, y);
        }

        bool IPortablePresentationSourceHost.TryUpdateRootVisualClientSize(out double width, out double height)
        {
            return TryUpdateRootVisualClientSize(out width, out height);
        }

        bool IPortablePresentationSourceHost.DispatchHwndSourceHook(int message, IntPtr wParam, IntPtr lParam, out IntPtr result, out bool handled)
        {
            if (_isDisposed || _portableHwndSource == null)
            {
                result = IntPtr.Zero;
                handled = false;
                return false;
            }

            return _portableHwndSource.DispatchPortableHwndSourceHook(message, wParam, lParam, out result, out handled);
        }

        public override bool IsDisposed
        {
            get { return _isDisposed; }
        }

        public override Visual RootVisual
        {
            get
            {
                if (_isDisposed)
                {
                    return null;
                }

                return _rootVisual;
            }
            set
            {
                VerifyNotDisposed();
                SetRootVisual(value);
            }
        }

        internal PortableCompositionTarget PortableCompositionTarget
        {
            get { return _compositionTarget; }
        }

        internal void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            VerifyNotDisposed();
            Matrix currentTransform = _compositionTarget.TransformToDevice;
            if (currentTransform.M11 == dpiScaleX && currentTransform.M22 == dpiScaleY)
            {
                return;
            }

            if (!_portableHwndSource.SetPortableDeviceScale(dpiScaleX, dpiScaleY))
            {
                return;
            }

            _compositionTarget.SetDeviceScale(dpiScaleX, dpiScaleY);
            ApplyRootVisualDpi(dpiScaleX, dpiScaleY);
            ApplyRootVisualLayout();
            RequestRender();
        }

        internal void SetClientSize(double width, double height)
        {
            VerifyNotDisposed();

            Size clientSize = new Size(
                ToPositiveFiniteClientSize(width),
                ToPositiveFiniteClientSize(height));
            if (_hasClientSize &&
                _clientSize.Width == clientSize.Width &&
                _clientSize.Height == clientSize.Height)
            {
                return;
            }

            _clientSize = clientSize;
            _hasClientSize = true;
            ApplyRootVisualLayout();
            RequestRender();
        }

        internal Point ClientOrigin
        {
            get { return _clientOrigin; }
        }

        internal void SetClientOrigin(double x, double y)
        {
            VerifyNotDisposed();

            Point origin = new Point(
                ToFiniteClientOrigin(x),
                ToFiniteClientOrigin(y));
            if (_clientOrigin == origin)
            {
                return;
            }

            _clientOrigin = origin;
            RequestRender();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                VerifyAccess();
                SetRootVisual(null);
                RemoveSource();
                _portableHwndSource.Dispose();
                _mouseInputProvider.Dispose();
                _keyboardInputProvider.Dispose();
                _compositionTarget.Dispose();
                ClearContentRenderedListeners();
                Disposed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                RenderRequested = null;
                CursorRequested = null;
                Disposed = null;
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        protected override CompositionTarget GetCompositionTargetCore()
        {
            return _isDisposed ? null : _compositionTarget;
        }

        internal override IInputProvider GetInputProvider(Type inputDevice)
        {
            if (inputDevice == typeof(MouseDevice))
            {
                return _mouseInputProvider;
            }

            if (inputDevice == typeof(KeyboardDevice))
            {
                return _keyboardInputProvider;
            }

            return null;
        }

        private void SetRootVisual(Visual rootVisual)
        {
            if (_rootVisual == rootVisual)
            {
                return;
            }

            Visual oldRootVisual = _rootVisual;
            if (oldRootVisual is UIElement oldRootUIElement)
            {
                oldRootUIElement.LayoutUpdated -= OnLayoutUpdated;
            }

            if (rootVisual != null)
            {
                _rootVisual = rootVisual;
                if (rootVisual is UIElement newRootUIElement)
                {
                    newRootUIElement.LayoutUpdated += OnLayoutUpdated;
                }

                _compositionTarget.RootVisual = rootVisual;
                // Publish the presentation source before portable DPI and layout work can
                // reenter user code through Loaded, layout, or DPI callbacks.
                RootChanged(oldRootVisual, _rootVisual);
                Matrix transformToDevice = _compositionTarget.TransformToDevice;
                ApplyRootVisualDpi(transformToDevice.M11, transformToDevice.M22);
                UIElement.PropagateResumeLayout(null, rootVisual);
            }
            else
            {
                _rootVisual = null;
                _compositionTarget.RootVisual = null;
            }

            if (oldRootVisual != null)
            {
                UIElement.PropagateSuspendLayout(oldRootVisual);
            }

            if (rootVisual == null)
            {
                RootChanged(oldRootVisual, _rootVisual);
            }

            _keyboardInputProvider.OnRootChanged(oldRootVisual, _rootVisual);
            if (rootVisual != null)
            {
                ApplyRootVisualLayout();
            }
            QueueContentRendered();
            RequestRender();
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
        {
            QueueContentRendered();
            RequestRender();
        }

        private void ApplyRootVisualLayout()
        {
            if (!_hasClientSize || _rootVisual is not UIElement rootUIElement)
            {
                return;
            }

            rootUIElement.InvalidateMeasure();
            rootUIElement.Measure(_clientSize);
            rootUIElement.Arrange(new Rect(new Point(), _clientSize));
            rootUIElement.UpdateLayout();
        }

        private void ApplyRootVisualDpi(double dpiScaleX, double dpiScaleY)
        {
            if (_rootVisual == null)
            {
                return;
            }

            DpiScale newDpi = new DpiScale(dpiScaleX, dpiScaleY);
            if (VisualTreeHelper.GetDpi(_rootVisual).Equals(newDpi))
            {
                return;
            }

            VisualTreeHelper.SetRootDpi(_rootVisual, newDpi);
            InvalidateDpiMeasure(_rootVisual);
        }

        private static void InvalidateDpiMeasure(DependencyObject dependencyObject)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(dependencyObject, i);
                if (child != null)
                {
                    InvalidateDpiMeasure(child);
                }
            }

            if (dependencyObject is UIElement element)
            {
                element.InvalidateMeasure();
            }
        }

        private bool TryUpdateRootVisualClientSize(out double width, out double height)
        {
            width = 0.0;
            height = 0.0;
            VerifyNotDisposed();

            if (_rootVisual is not UIElement rootUIElement)
            {
                return false;
            }

            Size desiredSize = MeasureRootVisual(rootUIElement, new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (IsClientSizeEmpty(desiredSize))
            {
                desiredSize = MeasureRootVisual(rootUIElement, new Size(4096.0, 4096.0));
            }

            width = ToPositiveFiniteClientSize(desiredSize.Width);
            height = ToPositiveFiniteClientSize(desiredSize.Height);
            SetClientSize(width, height);
            return true;
        }

        private static Size MeasureRootVisual(UIElement rootUIElement, Size constraint)
        {
            rootUIElement.InvalidateMeasure();
            rootUIElement.Measure(constraint);
            return rootUIElement.DesiredSize;
        }

        private static bool IsClientSizeEmpty(Size size)
        {
            return !double.IsFinite(size.Width) ||
                !double.IsFinite(size.Height) ||
                size.Width <= 1.0 ||
                size.Height <= 1.0;
        }

        private static double ToPositiveFiniteClientSize(double value)
        {
            return double.IsFinite(value) && value > 0.0 ? value : 1.0;
        }

        private static double ToFiniteClientOrigin(double value)
        {
            return double.IsFinite(value) ? value : 0.0;
        }

        private void QueueContentRendered()
        {
            if (_rootVisual == null || _contentRenderedQueued || _isDisposed)
            {
                return;
            }

            _contentRenderedQueued = true;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new DispatcherOperationCallback(FireContentRenderedCallback),
                this);
        }

        private object FireContentRenderedCallback(object arg)
        {
            if (_isDisposed)
            {
                return null;
            }

            _contentRenderedQueued = false;
            return FireContentRendered(arg);
        }

        private void RequestRender()
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool RequestCursor(Cursor cursor)
        {
            if (_isDisposed || !HasRootVisual)
            {
                return false;
            }

            RequestedCursor = cursor ?? Cursors.None;
            CursorRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void VerifyNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            VerifyAccess();
        }

        private bool HasRootVisual
        {
            get { return !_isDisposed && _rootVisual != null; }
        }

        private bool ProvidesInputForRootVisual(Visual visual)
        {
            return !_isDisposed && _rootVisual == visual;
        }

        internal bool TryHitTestOverride(Point rootPoint, out IInputElement enabledHit, out IInputElement originalHit)
        {
            enabledHit = originalHit = null;
            if (_isDisposed || HitTestOverride == null)
            {
                return false;
            }

            object hitTestResult = HitTestOverride(rootPoint);
            if (ReferenceEquals(hitTestResult, this))
            {
                // The portable host returns the source itself to mark a GPU-cache miss
                // that should not fall back to the CPU drawing-content hit-test walk.
                return true;
            }

            if (hitTestResult is not DependencyObject candidate)
            {
                return false;
            }

            originalHit = candidate as IInputElement;
            while (candidate != null)
            {
                if (candidate is UIElement element)
                {
                    originalHit ??= element;
                    if (element.IsEnabled)
                    {
                        enabledHit = element;
                        return true;
                    }
                }
                else if (candidate is UIElement3D element3D)
                {
                    originalHit ??= element3D;
                    if (element3D.IsEnabled)
                    {
                        enabledHit = element3D;
                        return true;
                    }
                }

                if (candidate == _rootVisual)
                {
                    break;
                }

                candidate = VisualTreeHelper.GetParentInternal(candidate);
            }

            return originalHit != null;
        }

        internal bool TryPointHitTestOverride(Visual reference, Point referencePoint, bool include2DOn3D, out HitTestResult hitTestResult)
        {
            hitTestResult = null;
            if (!include2DOn3D ||
                _isDisposed ||
                HitTestOverride == null ||
                reference == null ||
                _rootVisual == null)
            {
                return false;
            }

            if (!TryTransformPoint(reference, _rootVisual, referencePoint, out Point rootPoint))
            {
                return false;
            }

            object hitTestResultObject = HitTestOverride(rootPoint);
            if (ReferenceEquals(hitTestResultObject, this))
            {
                return true;
            }

            if (hitTestResultObject is not Visual visualHit)
            {
                return false;
            }

            if (!IsVisualDescendantOf(visualHit, reference))
            {
                return true;
            }

            if (!TryTransformPoint(_rootVisual, visualHit, rootPoint, out Point pointHit))
            {
                return true;
            }

            hitTestResult = new PointHitTestResult(visualHit, pointHit);
            return true;
        }

        internal bool TryPointHitTestOverride(Visual reference, Point referencePoint, HitTestFilterCallback filterCallback, HitTestResultCallback resultCallback, out HitTestResultBehavior result)
        {
            result = HitTestResultBehavior.Continue;
            if (_isDisposed ||
                (HitTestAllBufferOverride == null && HitTestAllOverride == null) ||
                reference == null ||
                resultCallback == null ||
                _rootVisual == null)
            {
                return false;
            }

            if (!TryTransformPoint(reference, _rootVisual, referencePoint, out Point rootPoint))
            {
                return false;
            }

            if (!TryGetHitTestAllResults(rootPoint, out object[] hitTestResults, out int hitTestResultCount, out bool shouldReturnHitTestResults))
            {
                return false;
            }

            try
            {
                Dictionary<Visual, HitTestFilterBehavior> filterResults = filterCallback == null
                    ? null
                    : new Dictionary<Visual, HitTestFilterBehavior>();

                for (int i = 0; i < hitTestResultCount; i++)
                {
                    if (hitTestResults[i] is not Visual visualHit ||
                        !IsVisualDescendantOf(visualHit, reference))
                    {
                        continue;
                    }

                    if (!IsPointHitVisibleByFilter(
                            reference,
                            visualHit,
                            filterCallback,
                            filterResults,
                            out bool stopFilter))
                    {
                        if (stopFilter)
                        {
                            result = HitTestResultBehavior.Stop;
                            return true;
                        }

                        continue;
                    }

                    if (!TryTransformPoint(_rootVisual, visualHit, rootPoint, out Point pointHit))
                    {
                        continue;
                    }

                    result = resultCallback(new PointHitTestResult(visualHit, pointHit));
                    if (result == HitTestResultBehavior.Stop)
                    {
                        return true;
                    }
                }
            }
            finally
            {
                ReturnHitTestAllResults(hitTestResults, shouldReturnHitTestResults);
            }

            return true;
        }

        internal bool TryGeometryHitTestOverride(Visual reference, GeometryHitTestParameters geometryParams, HitTestFilterCallback filterCallback, HitTestResultCallback resultCallback, out HitTestResultBehavior result)
        {
            result = HitTestResultBehavior.Continue;
            if (_isDisposed ||
                (HitTestBoundsBufferOverride == null &&
                 HitTestEllipseBoundsBufferOverride == null &&
                 HitTestBoundsOverride == null &&
                 HitTestEllipseBoundsOverride == null) ||
                reference == null ||
                geometryParams == null ||
                resultCallback == null ||
                _rootVisual == null)
            {
                return false;
            }

            Rect bounds = geometryParams.Bounds;
            if (bounds.IsEmpty ||
                !TryTransformBounds(reference, _rootVisual, bounds, out Rect rootBounds, out bool preservesAxisAlignedBounds) ||
                rootBounds.IsEmpty)
            {
                return false;
            }

            if (!TryGetGeometryHitTestResults(
                    rootBounds,
                    geometryParams.PortableHitTestGeometryKind == PortableHitTestGeometryKind.AxisAlignedEllipse && preservesAxisAlignedBounds,
                    out object[] hitTestResults,
                    out int hitTestResultCount,
                    out bool shouldReturnHitTestResults))
            {
                return false;
            }

            try
            {
                Dictionary<Visual, HitTestFilterBehavior> filterResults = filterCallback == null
                    ? null
                    : new Dictionary<Visual, HitTestFilterBehavior>();

                for (int i = 0; i < hitTestResultCount; i++)
                {
                    if (!TryGetPortableGeometryHitCandidate(hitTestResults[i], out Visual visualHit, out IntersectionDetail intersectionDetail) ||
                        !IsVisualDescendantOf(visualHit, reference))
                    {
                        continue;
                    }

                    if (!IsPointHitVisibleByFilter(
                            reference,
                            visualHit,
                            filterCallback,
                            filterResults,
                            out bool stopFilter))
                    {
                        if (stopFilter)
                        {
                            result = HitTestResultBehavior.Stop;
                            return true;
                        }

                        continue;
                    }

                    result = resultCallback(new GeometryHitTestResult(visualHit, intersectionDetail));
                    if (result == HitTestResultBehavior.Stop)
                    {
                        return true;
                    }
                }
            }
            finally
            {
                ReturnHitTestAllResults(hitTestResults, shouldReturnHitTestResults);
            }

            return true;
        }

        private bool TryGetGeometryHitTestResults(
            Rect rootBounds,
            bool preferEllipse,
            out object[] hitTestResults,
            out int hitTestResultCount,
            out bool shouldReturnHitTestResults)
        {
            if (preferEllipse &&
                TryGetGeometryHitTestBufferResults(HitTestEllipseBoundsBufferOverride, rootBounds, out hitTestResults, out hitTestResultCount))
            {
                shouldReturnHitTestResults = true;
                return true;
            }

            if (preferEllipse &&
                HitTestEllipseBoundsOverride != null)
            {
                hitTestResults = HitTestEllipseBoundsOverride(rootBounds.TopLeft, rootBounds.BottomRight);
                if (hitTestResults != null)
                {
                    hitTestResultCount = hitTestResults.Length;
                    shouldReturnHitTestResults = false;
                    return true;
                }
            }

            if (TryGetGeometryHitTestBufferResults(HitTestBoundsBufferOverride, rootBounds, out hitTestResults, out hitTestResultCount))
            {
                shouldReturnHitTestResults = true;
                return true;
            }

            if (HitTestBoundsOverride != null)
            {
                hitTestResults = HitTestBoundsOverride(rootBounds.TopLeft, rootBounds.BottomRight);
                if (hitTestResults != null)
                {
                    hitTestResultCount = hitTestResults.Length;
                    shouldReturnHitTestResults = false;
                    return true;
                }
            }

            hitTestResults = null;
            hitTestResultCount = 0;
            shouldReturnHitTestResults = false;
            return false;
        }

        private static bool TryGetGeometryHitTestBufferResults(
            PortableGeometryHitTestBufferOverride hitTestOverride,
            Rect rootBounds,
            out object[] hitTestResults,
            out int hitTestResultCount)
        {
            hitTestResults = null;
            hitTestResultCount = 0;
            if (hitTestOverride == null)
            {
                return false;
            }

            object[] rentedResults = ArrayPool<object>.Shared.Rent(HitTestOwnerBufferCapacity);
            if (!hitTestOverride(
                    rootBounds.Left,
                    rootBounds.Top,
                    rootBounds.Right,
                    rootBounds.Bottom,
                    rentedResults,
                    out hitTestResultCount) ||
                hitTestResultCount < 0 ||
                hitTestResultCount > rentedResults.Length)
            {
                ArrayPool<object>.Shared.Return(rentedResults, clearArray: true);
                hitTestResultCount = 0;
                return false;
            }

            hitTestResults = rentedResults;
            return true;
        }

        private static bool TryGetPortableGeometryHitCandidate(object candidate, out Visual visualHit, out IntersectionDetail intersectionDetail)
        {
            visualHit = null;
            intersectionDetail = IntersectionDetail.Intersects;
            if (candidate is not PortableGeometryHitTestCandidate portableCandidate)
            {
                return false;
            }

            if (portableCandidate.VisualHit is not Visual portableVisualHit)
            {
                return false;
            }

            visualHit = portableVisualHit;
            intersectionDetail = ToIntersectionDetail(portableCandidate.IntersectionDetail);
            return true;
        }

        private static IntersectionDetail ToIntersectionDetail(uint detail)
        {
            return detail switch
            {
                2u => IntersectionDetail.FullyInside,
                3u => IntersectionDetail.FullyContains,
                4u => IntersectionDetail.Intersects,
                _ => IntersectionDetail.Intersects
            };
        }

        private static bool IsPointHitVisibleByFilter(
            Visual reference,
            Visual visualHit,
            HitTestFilterCallback filterCallback,
            Dictionary<Visual, HitTestFilterBehavior> filterResults,
            out bool stop)
        {
            stop = false;
            if (filterCallback == null)
            {
                return true;
            }

            Visual[] path = ArrayPool<Visual>.Shared.Rent(16);
            int pathCount = 0;
            try
            {
                DependencyObject current = visualHit;
                while (current != null)
                {
                    if (current is Visual currentVisual)
                    {
                        if (pathCount == path.Length)
                        {
                            Visual[] expandedPath = ArrayPool<Visual>.Shared.Rent(path.Length * 2);
                            Array.Copy(path, expandedPath, pathCount);
                            ArrayPool<Visual>.Shared.Return(path, clearArray: true);
                            path = expandedPath;
                        }

                        path[pathCount++] = currentVisual;
                    }

                    if (current == reference)
                    {
                        break;
                    }

                    current = VisualTreeHelper.GetParentInternal(current);
                }

                if (pathCount == 0 || path[pathCount - 1] != reference)
                {
                    return false;
                }

                for (int i = pathCount - 1; i >= 0; i--)
                {
                    Visual currentVisual = path[i];
                    if (!filterResults.TryGetValue(currentVisual, out HitTestFilterBehavior filter))
                    {
                        filter = filterCallback(currentVisual);
                        filterResults.Add(currentVisual, filter);
                    }

                    if (filter == HitTestFilterBehavior.Stop)
                    {
                        stop = true;
                        return false;
                    }

                    if (filter == HitTestFilterBehavior.ContinueSkipSelfAndChildren)
                    {
                        return false;
                    }

                    if (filter == HitTestFilterBehavior.ContinueSkipChildren &&
                        currentVisual != visualHit)
                    {
                        return false;
                    }

                    if (filter == HitTestFilterBehavior.ContinueSkipSelf &&
                        currentVisual == visualHit)
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                ArrayPool<Visual>.Shared.Return(path, clearArray: true);
            }
        }

        internal bool TryInputHitTestOverride(UIElement reference, Point referencePoint, out DependencyObject candidate, out HitTestResult hitTestResult)
        {
            candidate = null;
            hitTestResult = null;
            if (_isDisposed ||
                (HitTestAllBufferOverride == null && HitTestAllOverride == null) ||
                reference == null ||
                _rootVisual == null)
            {
                return false;
            }

            if (!TryTransformPoint(reference, _rootVisual, referencePoint, out Point rootPoint))
            {
                return false;
            }

            if (!TryGetHitTestAllResults(rootPoint, out object[] hitTestResults, out int hitTestResultCount, out bool shouldReturnHitTestResults))
            {
                return false;
            }

            try
            {
                for (int i = 0; i < hitTestResultCount; i++)
                {
                    if (hitTestResults[i] is not Visual visualHit ||
                        !IsInputHitTestVisibleDescendantOf(visualHit, reference) ||
                        !TryTransformPoint(_rootVisual, visualHit, rootPoint, out Point pointHit))
                    {
                        continue;
                    }

                    candidate = visualHit;
                    hitTestResult = new PointHitTestResult(visualHit, pointHit);
                    return true;
                }
            }
            finally
            {
                ReturnHitTestAllResults(hitTestResults, shouldReturnHitTestResults);
            }

            return true;
        }

        private bool TryGetHitTestAllResults(Point rootPoint, out object[] hitTestResults, out int hitTestResultCount, out bool shouldReturnHitTestResults)
        {
            if (HitTestAllBufferOverride != null)
            {
                object[] rentedResults = ArrayPool<object>.Shared.Rent(HitTestOwnerBufferCapacity);
                if (!HitTestAllBufferOverride(rootPoint.X, rootPoint.Y, rentedResults, out hitTestResultCount) ||
                    hitTestResultCount < 0 ||
                    hitTestResultCount > rentedResults.Length)
                {
                    ArrayPool<object>.Shared.Return(rentedResults, clearArray: true);
                    hitTestResults = null;
                    hitTestResultCount = 0;
                    shouldReturnHitTestResults = false;
                    return false;
                }

                hitTestResults = rentedResults;
                shouldReturnHitTestResults = true;
                return true;
            }

            if (HitTestAllOverride == null)
            {
                hitTestResults = null;
                hitTestResultCount = 0;
                shouldReturnHitTestResults = false;
                return false;
            }

            hitTestResults = HitTestAllOverride(rootPoint);
            if (hitTestResults == null)
            {
                hitTestResultCount = 0;
                shouldReturnHitTestResults = false;
                return false;
            }

            hitTestResultCount = hitTestResults.Length;
            shouldReturnHitTestResults = false;
            return true;
        }

        private static void ReturnHitTestAllResults(object[] hitTestResults, bool shouldReturnHitTestResults)
        {
            if (shouldReturnHitTestResults)
            {
                ArrayPool<object>.Shared.Return(hitTestResults, clearArray: true);
            }
        }

        private static bool TryTransformPoint(Visual fromVisual, Visual toVisual, Point point, out Point transformedPoint)
        {
            transformedPoint = point;
            if (fromVisual == toVisual)
            {
                return true;
            }

            try
            {
                GeneralTransform transform = fromVisual.TransformToVisual(toVisual);
                return transform != null && transform.TryTransform(point, out transformedPoint);
            }
            catch (InvalidOperationException)
            {
                transformedPoint = default;
                return false;
            }
        }

        private static bool TryTransformBounds(Visual fromVisual, Visual toVisual, Rect bounds, out Rect transformedBounds)
        {
            return TryTransformBounds(fromVisual, toVisual, bounds, out transformedBounds, out _);
        }

        private static bool TryTransformBounds(Visual fromVisual, Visual toVisual, Rect bounds, out Rect transformedBounds, out bool preservesAxisAlignedBounds)
        {
            transformedBounds = Rect.Empty;
            preservesAxisAlignedBounds = false;
            if (bounds.IsEmpty)
            {
                return false;
            }

            if (!TryTransformPoint(fromVisual, toVisual, bounds.TopLeft, out Point topLeft) ||
                !TryTransformPoint(fromVisual, toVisual, bounds.TopRight, out Point topRight) ||
                !TryTransformPoint(fromVisual, toVisual, bounds.BottomRight, out Point bottomRight) ||
                !TryTransformPoint(fromVisual, toVisual, bounds.BottomLeft, out Point bottomLeft))
            {
                return false;
            }

            transformedBounds = new Rect(topLeft, topLeft);
            transformedBounds.Union(topRight);
            transformedBounds.Union(bottomRight);
            transformedBounds.Union(bottomLeft);
            preservesAxisAlignedBounds = IsAxisAlignedRectangle(topLeft, topRight, bottomRight, bottomLeft);
            return true;
        }

        private static bool IsAxisAlignedRectangle(Point topLeft, Point topRight, Point bottomRight, Point bottomLeft)
        {
            const double epsilon = 0.000001;
            return AreClose(topLeft.Y, topRight.Y, epsilon) &&
                AreClose(bottomLeft.Y, bottomRight.Y, epsilon) &&
                AreClose(topLeft.X, bottomLeft.X, epsilon) &&
                AreClose(topRight.X, bottomRight.X, epsilon);
        }

        private static bool AreClose(double left, double right, double epsilon)
        {
            return Math.Abs(left - right) <= epsilon;
        }

        private static bool IsVisualDescendantOf(Visual visual, Visual ancestor)
        {
            DependencyObject current = visual;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParentInternal(current);
            }

            return false;
        }

        private static bool IsInputHitTestVisibleDescendantOf(Visual visual, Visual ancestor)
        {
            DependencyObject current = visual;
            while (current != null)
            {
                if (UIElementHelper.IsUIElementOrUIElement3D(current) &&
                    (!UIElementHelper.IsVisible(current) || !UIElementHelper.IsHitTestVisible(current)))
                {
                    return false;
                }

                if (current == ancestor)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParentInternal(current);
            }

            return false;
        }

        private sealed class PortableKeyboardInputProvider : IKeyboardInputProvider, IDisposable
        {
            private readonly PortablePresentationSource _source;
            private InputProviderSite _site;
            private bool _active;

            internal PortableKeyboardInputProvider(PortablePresentationSource source)
            {
                _source = source;
                _site = InputManager.Current.RegisterInputProvider(this);
            }

            public void Dispose()
            {
                _active = false;
                _site?.Dispose();
                _site = null;
            }

            internal void OnRootChanged(Visual oldRoot, Visual newRoot)
            {
                if (_active && newRoot != null)
                {
                    Keyboard.Focus(null);
                }
            }

            bool IInputProvider.ProvidesInputForRootVisual(Visual v)
            {
                return _source.ProvidesInputForRootVisual(v);
            }

            void IInputProvider.NotifyDeactivate()
            {
                _active = false;
            }

            bool IKeyboardInputProvider.AcquireFocus(bool checkOnly)
            {
                bool acquired = _source.HasRootVisual;
                if (acquired && !checkOnly)
                {
                    _active = true;
                }

                return acquired;
            }
        }

        private sealed class PortableMouseInputProvider : IMouseInputProvider, IDisposable
        {
            private readonly PortablePresentationSource _source;
            private InputProviderSite _site;
            private bool _haveCapture;

            internal PortableMouseInputProvider(PortablePresentationSource source)
            {
                _source = source;
                _site = InputManager.Current.RegisterInputProvider(this);
            }

            public void Dispose()
            {
                ReleaseMouseCapture(reportInput: true);
                _site?.Dispose();
                _site = null;
            }

            bool IInputProvider.ProvidesInputForRootVisual(Visual v)
            {
                return _source.ProvidesInputForRootVisual(v);
            }

            void IInputProvider.NotifyDeactivate()
            {
                // Changing the active presentation source is not the same as
                // cancelling mouse capture. A captured subtree can span the
                // owner and popup presentation sources, so releasing here
                // closes menus and ComboBox popups as the pointer enters their
                // native child window. The HWND provider likewise only stops
                // source-local tracking when it is deactivated; capture is
                // released explicitly by WPF or when this provider is disposed.
            }

            bool IMouseInputProvider.SetCursor(Cursor cursor)
            {
                return _source.RequestCursor(cursor);
            }

            bool IMouseInputProvider.CaptureMouse()
            {
                if (!_source.HasRootVisual)
                {
                    return false;
                }

                _haveCapture = true;
                return true;
            }

            void IMouseInputProvider.ReleaseMouseCapture()
            {
                ReleaseMouseCapture(reportInput: true);
            }

            int IMouseInputProvider.GetIntermediatePoints(IInputElement relativeTo, Point[] points)
            {
                return -1;
            }

            private void ReleaseMouseCapture(bool reportInput)
            {
                if (!_haveCapture)
                {
                    return;
                }

                _haveCapture = false;

                if (reportInput && _site != null && !_site.IsDisposed)
                {
                    RawMouseInputReport report = new RawMouseInputReport(
                        InputMode.Foreground,
                        Environment.TickCount,
                        _source,
                        RawMouseActions.CancelCapture,
                        0,
                        0,
                        0,
                        IntPtr.Zero);

                    _site.ReportInput(report);
                }
            }
        }
    }
}
