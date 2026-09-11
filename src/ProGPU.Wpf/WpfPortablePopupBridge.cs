using System;
using System.Buffers;
using System.Numerics;
using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

internal sealed class WpfPortablePopupBridge : IDisposable
{
    private const int HitTestOwnerBufferCapacity = 64;
    private const string TracePopupEnvironmentVariable = "PROGPU_WPF_TRACE_POPUP";
    private static readonly bool s_tracePopup = IsTraceEnabled(TracePopupEnvironmentVariable);

    private readonly ProGpuWpfWindowHost _host;
    private readonly IPortablePresentationSourceHost _source;
    private readonly object? _ownerPresentationSource;
    private readonly WpfPortablePopupBridge? _ownerPopup;
    private IWpfPortableNativePopupHost? _nativeHost;
    private double _dpiScaleX;
    private double _dpiScaleY;
    private int _ownerClientScreenDeviceX;
    private int _ownerClientScreenDeviceY;
    private double _localLogicalX;
    private double _localLogicalY;
    private PortableDesktopTransform _ownerDesktopTransform;
    private Func<double, double, object?>? _hitTestOverrideHandler;
    private Func<double, double, object?[]?>? _hitTestAllOverrideHandler;
    private PortableHitTestAllBufferOverride? _hitTestAllBufferOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestBoundsOverrideHandler;
    private PortableGeometryHitTestBufferOverride? _hitTestBoundsBufferOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestEllipseBoundsOverrideHandler;
    private PortableGeometryHitTestBufferOverride? _hitTestEllipseBoundsBufferOverrideHandler;
    private bool _isDisposed;

    private WpfPortablePopupBridge(
        ProGpuWpfWindowHost host,
        IPortablePresentationSourceHost source,
        object? ownerPresentationSource,
        WpfPortablePopupBridge? ownerPopup,
        int popupScreenDeviceX,
        int popupScreenDeviceY,
        int ownerClientScreenDeviceX,
        int ownerClientScreenDeviceY,
        double dpiScaleX,
        double dpiScaleY)
    {
        _host = host;
        _source = source;
        _ownerPresentationSource = ownerPresentationSource;
        _ownerPopup = ownerPopup;
        _dpiScaleX = dpiScaleX;
        _dpiScaleY = dpiScaleY;
        _ownerClientScreenDeviceX = ownerClientScreenDeviceX;
        _ownerClientScreenDeviceY = ownerClientScreenDeviceY;
        X = popupScreenDeviceX;
        Y = popupScreenDeviceY;
        _ownerDesktopTransform = ReadOwnerDesktopTransform();
        SetLocalPosition(popupScreenDeviceX, popupScreenDeviceY);
        Width = 1;
        Height = 1;
        IsHitTestable = true;
        SetSourceClientOrigin();
    }

    public object Source => _source;

    public object? RootVisual => _source.RootVisual;

    public IntPtr Handle => _source.Handle;

    public int X { get; private set; }

    public int Y { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsVisible { get; private set; }

    public bool IsHitTestable { get; private set; }

    internal bool IsVisibleNativeWindow => IsVisible && _nativeHost != null;

    internal bool TryGetPlacementBounds(PortableRect targetBounds, out PortablePopupPlacementBounds bounds)
    {
        bounds = default;
        if (_isDisposed || !PortablePopupMonitorSelection.IsFiniteRectangle(targetBounds, allowZeroSize: true))
            return false;
        if (_nativeHost == null)
        {
            bounds = new(PortablePopupPlacementBoundsKind.OwnerSurface, PortableRect.Empty, PortableRect.Empty);
            return true;
        }

        // Placement and native window positions share platform screen coordinates.
        // Per-monitor framebuffer/content scales must not rescale desktop origins.
        var selection = new PortablePopupMonitorSelection(targetBounds);
        var monitors = _host.PlatformServices.Monitors.GetMonitors();
        for (int i = 0; i < monitors.Count; i++)
        {
            WpfMonitorInfo monitor = monitors[i];
            selection.Consider(
                new PortableRect(monitor.X, monitor.Y, monitor.Width, monitor.Height),
                new PortableRect(monitor.WorkAreaX, monitor.WorkAreaY, monitor.WorkAreaWidth, monitor.WorkAreaHeight),
                monitor.IsPrimary);
        }
        return selection.TryGetBounds(out bounds);
    }

    internal bool TryGetNativeMilOverlay(out WpfNativeMilVisualOverlay overlay)
    {
        overlay = default;
        if (_isDisposed || _nativeHost != null || !IsVisible || RootVisual is not { } root)
            return false;
        EnsureRootLayout(root);
        // Layout can synchronously close the popup or replace its source root.
        if (_isDisposed || !IsVisible || RootVisual is not { } currentRoot)
            return false;
        overlay = new(currentRoot, LogicalX, LogicalY, Width, Height);
        return true;
    }

    internal bool HasPresentedNativeFrame => IsVisibleNativeWindow && _nativeHost!.HasPresentedFrame;

    internal bool HasNativeGpuHitTestCache => IsVisibleNativeWindow && _nativeHost!.HasGpuHitTestCache;

    internal int NativeGpuHitTestOwnerCount =>
        IsVisibleNativeWindow &&
        _nativeHost!.TryGetGpuHitTestCacheSnapshot(out var snapshot)
            ? snapshot.OwnerCount
            : 0;

    internal bool TryHitTestNativeOwners(
        double screenDeviceX,
        double screenDeviceY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!IsVisibleNativeWindow)
        {
            return false;
        }
        var local = ScreenDeviceToPopupClient(screenDeviceX, screenDeviceY);
        if (local.X < 0 || local.Y < 0 || local.X > Width || local.Y > Height) return false;
        return _nativeHost!.TryHitTestOwners(local.X, local.Y, owners, out ownerCount);
    }

    internal bool TryQueryNativeHitTestBoundsOwners(
        double screenDeviceMinX,
        double screenDeviceMinY,
        double screenDeviceMaxX,
        double screenDeviceMaxY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        if (!IsVisibleNativeWindow)
        {
            return false;
        }

        var min = ScreenDeviceToPopupClient(screenDeviceMinX, screenDeviceMinY);
        var max = ScreenDeviceToPopupClient(screenDeviceMaxX, screenDeviceMaxY);
        if (max.X < 0 || max.Y < 0 || min.X > Width || min.Y > Height) return false;
        return _nativeHost!.TryQueryHitTestBoundsOwners(
            min.X,
            min.Y,
            max.X,
            max.Y,
            owners,
            out ownerCount);
    }

    private PortablePoint ScreenDeviceToPopupClient(double x, double y)
    {
        var desktop = _source is IPortableDesktopGeometryHost geometry ? geometry.DesktopTransform :
            new PortableDesktopTransform(X / _dpiScaleX, Y / _dpiScaleY, 1, 1);
        return desktop.DesktopToClient(new PortablePoint(x / _dpiScaleX, y / _dpiScaleY));
    }

    internal bool TryQueryAllNativeHitTestOwners(Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        return IsVisibleNativeWindow && _nativeHost!.TryQueryHitTestBoundsOwners(
            0,
            0,
            Width,
            Height,
            owners,
            out ownerCount);
    }

    internal bool TryRaiseNativeInputForDiagnostics(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsVisibleNativeWindow)
        {
            return false;
        }

        _nativeHost!.RaiseInputForDiagnostics(input);
        return true;
    }

    internal bool TryRaiseNativeLocalInputForDiagnostics(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsVisibleNativeWindow)
        {
            return false;
        }

        WpfInputEventArgs nativeInput = CreateNativeDiagnosticInput(
            OperatingSystem.IsMacOS(),
            input,
            LogicalX,
            LogicalY);
        _nativeHost!.RaiseInputForDiagnostics(nativeInput);
        input.Handled = nativeInput.Handled;
        return true;
    }

    internal static WpfInputEventArgs CreateNativeDiagnosticInput(
        bool coordinatesAreOwnerRelative,
        WpfInputEventArgs input,
        double popupOwnerX,
        double popupOwnerY)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!coordinatesAreOwnerRelative || !IsPointerInput(input.Kind))
        {
            return input;
        }

        return new WpfInputEventArgs(
            input.Kind,
            input.Key,
            input.ScanCode,
            input.Character,
            input.X + popupOwnerX,
            input.Y + popupOwnerY,
            input.DeltaX,
            input.DeltaY,
            input.Button,
            input.Modifiers);
    }

    internal static Func<double, double, IPortablePresentationSourceHost> PortablePresentationSourceFactory { get; set; } =
        PortablePresentationSourceHost.Create;

    internal static Func<
        ProGpuWpfWindowHost,
        IPortablePresentationSourceHost,
        PortablePopupCreateRequest,
        double,
        double,
        IWpfPortableNativePopupHost?> NativePopupHostFactory { get; set; } =
        WpfPortableNativePopupHost.TryCreate;

    private double LogicalX =>
        (_ownerPopup?.LogicalX ?? 0.0) +
        _localLogicalX;

    private double LogicalY =>
        (_ownerPopup?.LogicalY ?? 0.0) +
        _localLogicalY;

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        PortablePopupCreateRequest request,
        WpfPortablePopupBridge? ownerPopup,
        out WpfPortablePopupBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);

        double dpiScaleX = host.CurrentDpiScaleX;
        double dpiScaleY = host.CurrentDpiScaleY;
        IPortablePresentationSourceHost source;
        try
        {
            source = PortablePresentationSourceFactory(
                dpiScaleX,
                dpiScaleY);
        }
        catch (PlatformNotSupportedException)
        {
            bridge = null;
            return false;
        }

        try
        {
            bridge = new WpfPortablePopupBridge(
                host,
                source,
                request.OwnerPresentationSource,
                ownerPopup,
                request.PopupScreenDeviceX,
                request.PopupScreenDeviceY,
                request.OwnerClientScreenDeviceX,
                request.OwnerClientScreenDeviceY,
                dpiScaleX,
                dpiScaleY);
        }
        catch
        {
            // Construction has not transferred ownership or installed callbacks.
            source.Dispose();
            throw;
        }
        try
        {
            bridge.SubscribeToSource();
            bridge.InstallHitTestOverrides();
            // Null is the factory's explicit owner-surface selection. Once it
            // selects a native host, a failure must not change surface kind.
            bridge._nativeHost = NativePopupHostFactory(host, source, request, dpiScaleX, dpiScaleY);
            bridge._nativeHost?.SetInputHandler(bridge.TryProcessNativeInput);
        }
        catch
        {
            bridge.Dispose();
            bridge = null;
            throw;
        }
        Trace(
            "create " +
            $"screen=({request.PopupScreenDeviceX},{request.PopupScreenDeviceY}) " +
            $"owner=({request.OwnerClientScreenDeviceX},{request.OwnerClientScreenDeviceY}) " +
            $"transparent={request.IsTransparent} child={request.IsChildPopup}");
        return true;
    }

    public bool TrySetDeviceScale(double dpiScaleX, double dpiScaleY)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(dpiScaleX) || dpiScaleX <= 0.0 ||
            !double.IsFinite(dpiScaleY) || dpiScaleY <= 0.0)
        {
            return false;
        }

        if (_dpiScaleX == dpiScaleX && _dpiScaleY == dpiScaleY)
        {
            return false;
        }

        // Unpositioned owners have no native origin to republish. Preserve the
        // origin carried by the create request in the new device-coordinate
        // frame; nested owners have already published their new device origin.
        return TrySetOwnerGeometry(
            _ownerPopup?.X ?? ToScreenDeviceCoordinate(0, _ownerClientScreenDeviceX / _dpiScaleX, dpiScaleX),
            _ownerPopup?.Y ?? ToScreenDeviceCoordinate(0, _ownerClientScreenDeviceY / _dpiScaleY, dpiScaleY),
            dpiScaleX,
            dpiScaleY);
    }

    public bool TrySetOwnerClientGeometry(
        object? ownerPresentationSource,
        int x,
        int y,
        WpfDeviceScale? deviceScale = null)
    {
        ThrowIfDisposed();
        return ReferenceEquals(_ownerPresentationSource, ownerPresentationSource) &&
            TrySetOwnerGeometry(x, y, deviceScale?.X ?? _dpiScaleX, deviceScale?.Y ?? _dpiScaleY);
    }

    private bool TrySetOwnerGeometry(int x, int y, double dpiScaleX, double dpiScaleY)
    {
        if (!double.IsFinite(dpiScaleX) || dpiScaleX <= 0.0 ||
            !double.IsFinite(dpiScaleY) || dpiScaleY <= 0.0)
        {
            return false;
        }

        var desktopTransform = ReadOwnerDesktopTransform();
        bool desktopScaleChanged = _ownerDesktopTransform.ScaleX != desktopTransform.ScaleX ||
            _ownerDesktopTransform.ScaleY != desktopTransform.ScaleY;
        bool scaleChanged = _dpiScaleX != dpiScaleX || _dpiScaleY != dpiScaleY;
        if (!desktopScaleChanged && !scaleChanged && _ownerClientScreenDeviceX == x && _ownerClientScreenDeviceY == y)
        {
            return false;
        }

        // Publish one coherent device frame before notifying either source or
        // native host. Moving with the new origin and old scale first produces
        // a real, incorrect native-window move during an owner DPI transition.
        _ownerClientScreenDeviceX = x;
        _ownerClientScreenDeviceY = y;
        _ownerDesktopTransform = desktopTransform;
        var desktopOffset = desktopTransform.ClientVectorToDesktop(new PortablePoint(_localLogicalX, _localLogicalY));
        X = ToScreenDeviceCoordinate(x, desktopOffset.X, dpiScaleX);
        Y = ToScreenDeviceCoordinate(y, desktopOffset.Y, dpiScaleY);
        _dpiScaleX = dpiScaleX;
        _dpiScaleY = dpiScaleY;
        SetSourceClientOrigin();
        if (scaleChanged)
        {
            if (_nativeHost is { } nativeHost)
                nativeHost.SetOwnerTransportScale(dpiScaleX, dpiScaleY);
            else
                _source.SetDeviceScale(dpiScaleX, dpiScaleY);
        }
        _nativeHost?.SetPosition(X, Y);
        Trace($"owner origin=({x},{y}) scale=({dpiScaleX:0.###},{dpiScaleY:0.###}) popup=({X},{Y})");
        RequestRender();
        return true;
    }

    public bool TrySetPosition(int x, int y)
    {
        ThrowIfDisposed();
        if (X == x && Y == y)
        {
            return false;
        }

        X = x;
        Y = y;
        SetLocalPosition(x, y);
        SetSourceClientOrigin();
        _nativeHost?.SetPosition(x, y);
        Trace($"position x={x} y={y}");
        RequestRender();
        return true;
    }

    private void SetSourceClientOrigin()
    {
        double x = ToLogicalScreenCoordinate(X, _dpiScaleX);
        double y = ToLogicalScreenCoordinate(Y, _dpiScaleY);
        // An independently surfaced popup owns its client scale after native
        // initialization. Owner-surface popups share the owner's DIP frame.
        var scale = _nativeHost != null && _source is IPortableDesktopGeometryHost nativeGeometry
            ? nativeGeometry.DesktopTransform : _ownerDesktopTransform;
        if (_source is IPortableDesktopGeometryHost geometry)
            geometry.SetDesktopTransform(new PortableDesktopTransform(x, y, scale.ScaleX, scale.ScaleY));
        else if (scale.ScaleX == 1 && scale.ScaleY == 1)
            _source.SetClientOrigin(x, y);
        else
            throw new PlatformNotSupportedException("Scaled popup coordinates require the typed portable desktop geometry capability.");
    }

    private PortableDesktopTransform ReadOwnerDesktopTransform() =>
        _ownerPresentationSource is IPortableDesktopGeometryHost geometry
            ? geometry.DesktopTransform : PortableDesktopTransform.Identity;

    private void SetLocalPosition(int x, int y)
    {
        // Legacy transport is desktop * framebuffer DPI, not owner-client DIPs.
        var local = _ownerDesktopTransform.DesktopVectorToClient(new PortablePoint(
            ((double)x - _ownerClientScreenDeviceX) / _dpiScaleX,
            ((double)y - _ownerClientScreenDeviceY) / _dpiScaleY));
        _localLogicalX = local.X;
        _localLogicalY = local.Y;
    }

    internal bool RefreshOwnerDesktopGeometry() => TrySetOwnerGeometry(
        _ownerClientScreenDeviceX, _ownerClientScreenDeviceY, _dpiScaleX, _dpiScaleY);

    private static double ToLogicalScreenCoordinate(int deviceCoordinate, double deviceScale)
    {
        double normalizedScale = double.IsFinite(deviceScale) && deviceScale > 0.0
            ? deviceScale
            : 1.0;
        return deviceCoordinate / normalizedScale;
    }

    public bool TrySetSize(int width, int height)
    {
        ThrowIfDisposed();
        int normalizedWidth = Math.Max(1, width);
        int normalizedHeight = Math.Max(1, height);
        if (Width == normalizedWidth && Height == normalizedHeight)
        {
            return false;
        }

        Width = normalizedWidth;
        Height = normalizedHeight;
        _source.SetClientSize(normalizedWidth, normalizedHeight);
        _nativeHost?.SetSize(normalizedWidth, normalizedHeight);
        Trace($"size width={normalizedWidth} height={normalizedHeight}");
        RequestRender();
        return true;
    }

    public bool TryShow()
    {
        ThrowIfDisposed();
        if (IsVisible)
        {
            return false;
        }

        IsVisible = true;
        if (RootVisual is { } rootVisual)
        {
            EnsureRootLayout(rootVisual);
            _nativeHost?.SetSize(Width, Height);
        }
        _nativeHost?.Show();
        Trace($"show width={Width} height={Height} root={(RootVisual is null ? "<null>" : "set")}");
        RequestRender();
        return true;
    }

    public bool TryHide()
    {
        ThrowIfDisposed();
        if (!IsVisible)
        {
            return false;
        }

        IsVisible = false;
        _nativeHost?.Hide();
        Trace("hide");
        RequestRender();
        return true;
    }

    public bool TrySetHitTestable(bool hitTestable)
    {
        ThrowIfDisposed();
        if (IsHitTestable == hitTestable)
        {
            return false;
        }

        IsHitTestable = hitTestable;
        RequestRender();
        return true;
    }

    public WpfVisualReplayResult Replay(
        ProGpuWpfCompositionTarget target,
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfMilResourceResolver? resources,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(drawingFrame);

        object? rootVisual = RootVisual;
        if (_nativeHost != null || !IsVisible || rootVisual == null)
        {
            return default;
        }

        EnsureRootLayout(rootVisual);

        using var sink = new ProGpuRetainedCompositionCommandSink(
            drawingFrame,
            target.Context,
            target.Viewport3DTextureCache,
            ProGpuRetainedCompositionLayer.Popup);

        PositionPopupRoot(sink.RootVisual);

        WpfVisualReplayResult result = target.ReplayVisualSubtreeUntracked(
            rootVisual,
            sink,
            resources,
            imageSourceAdapter,
            includePortablePopupRoots: true);
        Trace(FormattableString.Invariant($"replay visible={IsVisible} logical=({LogicalX:0.###},{LogicalY:0.###}) size={Width}x{Height} root=set visuals={result.VisualCount} content={result.ContentCount} renderData={result.RenderData.AppliedCount}/{result.RenderData.RecordCount}"));
        return result;
    }

    public bool ContainsHostPoint(double x, double y)
    {
        if (!IsVisible || !IsHitTestable)
        {
            return false;
        }

        double logicalX = LogicalX;
        double logicalY = LogicalY;
        return x >= logicalX &&
            y >= logicalY &&
            x <= logicalX + Width &&
            y <= logicalY + Height;
    }

    public bool TryProcessInput(WpfInputEventArgs input)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);

        if (!IsVisible || !IsHitTestable)
        {
            return false;
        }

        if (IsPointerInput(input.Kind) && !ContainsHostPoint(input.X, input.Y))
        {
            return false;
        }

        if (!PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out var activationService))
        {
            return false;
        }

        var portableInput = CreatePortableWindowInputEvent(input);
        if (!activationService.TryProcessPresentationSourceInputEvent(Source, portableInput))
        {
            return false;
        }

        input.Handled = portableInput.Handled;
        return true;
    }

    private bool TryProcessNativeInput(WpfInputEventArgs input)
    {
        if (_isDisposed || !IsVisible || !IsHitTestable)
        {
            return false;
        }

        double localX = input.X;
        double localY = input.Y;
        if (IsPointerInput(input.Kind) &&
            !TryNormalizeNativePointerCoordinates(
                OperatingSystem.IsMacOS(),
                input.X,
                input.Y,
                LogicalX,
                LogicalY,
                Width,
                Height,
                out localX,
                out localY))
        {
            Trace(FormattableString.Invariant(
                $"ignore native pointer outside local bounds point=({input.X:0.###},{input.Y:0.###}) origin=({LogicalX:0.###},{LogicalY:0.###}) size={Width}x{Height}"));
            return false;
        }

        return TryRouteInputToPresentationSource(input, localX, localY);
    }

    internal static bool TryNormalizeNativePointerCoordinates(
        bool coordinatesAreOwnerRelative,
        double inputX,
        double inputY,
        double popupOwnerX,
        double popupOwnerY,
        double popupWidth,
        double popupHeight,
        out double localX,
        out double localY)
    {
        // Cocoa/GLFW reports transient child-window pointer coordinates in the
        // owner client's logical coordinate space. X11 reports popup-local points.
        // Keep the conversion primitive and allocation-free because every popup
        // pointer event passes through it.
        localX = coordinatesAreOwnerRelative
            ? inputX - popupOwnerX
            : inputX;
        localY = coordinatesAreOwnerRelative
            ? inputY - popupOwnerY
            : inputY;

        return double.IsFinite(localX) &&
            double.IsFinite(localY) &&
            localX >= 0.0 &&
            localY >= 0.0 &&
            localX <= popupWidth &&
            localY <= popupHeight;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _nativeHost?.Dispose();
        }
        finally
        {
            try
            {
                DetachSourceCallbacks();
            }
            finally
            {
                // Unlike a borrowed main-window binding, this source was
                // created by our factory. Release it even if WPF never accepted
                // the popup or the owning window closes before Popup cleanup.
                _source.Dispose();
            }
        }
        GC.SuppressFinalize(this);
    }

    private void DetachSourceCallbacks()
    {
        _source.RenderRequested -= OnSourceRenderRequested;
        _source.CursorRequested -= OnSourceCursorRequested;

        if (_hitTestOverrideHandler != null &&
            ReferenceEquals(_source.HitTestOverride, _hitTestOverrideHandler))
        {
            _source.HitTestOverride = null;
        }

        if (_hitTestAllOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllOverride, _hitTestAllOverrideHandler))
        {
            _source.HitTestAllOverride = null;
        }

        if (_hitTestAllBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllBufferOverride, _hitTestAllBufferOverrideHandler))
        {
            _source.HitTestAllBufferOverride = null;
        }

        if (_hitTestBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsOverride, _hitTestBoundsOverrideHandler))
        {
            _source.HitTestBoundsOverride = null;
        }

        if (_hitTestBoundsBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsBufferOverride, _hitTestBoundsBufferOverrideHandler))
        {
            _source.HitTestBoundsBufferOverride = null;
        }

        if (_hitTestEllipseBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsOverride, _hitTestEllipseBoundsOverrideHandler))
        {
            _source.HitTestEllipseBoundsOverride = null;
        }

        if (_hitTestEllipseBoundsBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsBufferOverride, _hitTestEllipseBoundsBufferOverrideHandler))
        {
            _source.HitTestEllipseBoundsBufferOverride = null;
        }
    }

    private void SubscribeToSource()
    {
        _source.RenderRequested += OnSourceRenderRequested;
        _source.CursorRequested += OnSourceCursorRequested;
    }

    private void InstallHitTestOverrides()
    {
        _hitTestOverrideHandler = TryHitTestOwner;
        _hitTestAllOverrideHandler = HitTestOwners;
        _hitTestAllBufferOverrideHandler = HitTestOwners;
        _hitTestBoundsOverrideHandler = HitTestBoundsOwners;
        _hitTestBoundsBufferOverrideHandler = HitTestBoundsOwners;
        _hitTestEllipseBoundsOverrideHandler = HitTestEllipseBoundsOwners;
        _hitTestEllipseBoundsBufferOverrideHandler = HitTestEllipseBoundsOwners;

        _source.HitTestOverride = _hitTestOverrideHandler;
        _source.HitTestAllOverride = _hitTestAllOverrideHandler;
        _source.HitTestAllBufferOverride = _hitTestAllBufferOverrideHandler;
        _source.HitTestBoundsOverride = _hitTestBoundsOverrideHandler;
        _source.HitTestBoundsBufferOverride = _hitTestBoundsBufferOverrideHandler;
        _source.HitTestEllipseBoundsOverride = _hitTestEllipseBoundsOverrideHandler;
        _source.HitTestEllipseBoundsBufferOverride = _hitTestEllipseBoundsBufferOverrideHandler;
    }

    private void OnSourceRenderRequested(object? sender, EventArgs e)
    {
        if (!_isDisposed)
        {
            RequestRender();
        }
    }

    private void OnSourceCursorRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _host.ApplyPortableCursor(WpfPortablePresentationSourceBridge.ToWpfCursor(
            _source.RequestedCursorName ?? _source.RequestedCursor?.ToString()));
    }

    private object? TryHitTestOwner(double rootX, double rootY)
    {
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (HitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount))
            {
                ReadOnlySpan<object?> owners = ownerBuffer.AsSpan(0, ownerCount);
                if (WpfPortablePresentationSourceBridge.TrySelectPointerInputOwner(owners, out object? selectedOwner))
                {
                    return selectedOwner;
                }
            }

            // The shared cache can still describe only the owner window while a
            // newly opened popup is waiting for its first retained frame. Let the
            // popup presentation source use its typed visual-tree hit test then;
            // a handled GPU miss would make WPF treat an inside click as outside.
            return null;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    private object?[]? HitTestOwners(double rootX, double rootY)
    {
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!HitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount))
            {
                return null;
            }

            if (ownerCount == 0)
            {
                return Array.Empty<object?>();
            }

            var owners = new object?[ownerCount];
            ownerBuffer.AsSpan(0, ownerCount).CopyTo(owners);
            return owners;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    private bool HitTestOwners(double rootX, double rootY, Span<object?> owners, out int ownerCount)
    {
        if (!IsVisible ||
            !_host.TryHitTestOwners(LogicalX + rootX, LogicalY + rootY, owners, out ownerCount))
        {
            ownerCount = 0;
            return false;
        }

        ownerCount = WpfPortablePresentationSourceBridge.FilterVisualOwnerSubtree(
            owners[..ownerCount],
            RootVisual);
        ownerCount = WpfPortablePresentationSourceBridge.FilterTransparentPointerOverlays(owners[..ownerCount]);
        return ownerCount > 0;
    }

    private object?[]? HitTestBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        return HitTestGeometryOwners(minX, minY, maxX, maxY, isEllipse: false);
    }

    private bool HitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        if (_host.TryQueryHitTestBoundsCandidates(
            LogicalX + minX,
            LogicalY + minY,
            LogicalX + maxX,
            LogicalY + maxY,
            candidates,
            out candidateCount))
        {
            candidateCount = WpfPortablePresentationSourceBridge.FilterVisualOwnerSubtree(
                candidates[..candidateCount],
                RootVisual);
            return candidateCount > 0;
        }

        candidateCount = 0;
        return false;
    }

    private object?[]? HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        return HitTestGeometryOwners(minX, minY, maxX, maxY, isEllipse: true);
    }

    private bool HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        if (_host.TryQueryHitTestEllipseCandidates(
            LogicalX + minX,
            LogicalY + minY,
            LogicalX + maxX,
            LogicalY + maxY,
            candidates,
            out candidateCount))
        {
            candidateCount = WpfPortablePresentationSourceBridge.FilterVisualOwnerSubtree(
                candidates[..candidateCount],
                RootVisual);
            return candidateCount > 0;
        }

        candidateCount = 0;
        return false;
    }

    private object?[]? HitTestGeometryOwners(double minX, double minY, double maxX, double maxY, bool isEllipse)
    {
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            bool hit = isEllipse
                ? HitTestEllipseBoundsOwners(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount)
                : HitTestBoundsOwners(minX, minY, maxX, maxY, candidateBuffer, out candidateCount);
            if (!hit)
            {
                return null;
            }

            if (candidateCount == 0)
            {
                return Array.Empty<object>();
            }

            var candidates = new object?[candidateCount];
            candidateBuffer.AsSpan(0, candidateCount).CopyTo(candidates);
            return candidates;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    private PortableWindowInputEvent CreatePortableWindowInputEvent(WpfInputEventArgs input)
    {
        return new PortableWindowInputEvent(
            (int)input.Kind,
            input.Key,
            input.ScanCode,
            input.Character,
            input.X - LogicalX,
            input.Y - LogicalY,
            input.DeltaX,
            input.DeltaY,
            (int)input.Button,
            (int)input.Modifiers);
    }

    private bool TryRouteInputToPresentationSource(WpfInputEventArgs input, double localX, double localY)
    {
        if (!PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out var activationService))
        {
            return false;
        }

        var portableInput = new PortableWindowInputEvent(
            (int)input.Kind,
            input.Key,
            input.ScanCode,
            input.Character,
            localX,
            localY,
            input.DeltaX,
            input.DeltaY,
            (int)input.Button,
            (int)input.Modifiers);
        if (!activationService.TryProcessPresentationSourceInputEvent(Source, portableInput))
        {
            return false;
        }

        input.Handled = portableInput.Handled;
        return true;
    }

    private void RequestRender()
    {
        if (_nativeHost == null)
            _host.InvalidateNativeMilPopups();
        _host.RequestRenderAndWakeNativeLoop();
    }

    private void EnsureRootLayout(object rootVisual)
    {
        if (Width > 1 &&
            Height > 1)
        {
            return;
        }

        if (!_source.TryUpdateRootVisualClientSize(out double width, out double height))
        {
            return;
        }

        if (width <= 1.0 ||
            height <= 1.0)
        {
            return;
        }

        int clientWidth = (int)Math.Ceiling(width);
        int clientHeight = (int)Math.Ceiling(height);
        Width = clientWidth;
        Height = clientHeight;
        _source.SetClientSize(clientWidth, clientHeight);
        _nativeHost?.SetSize(clientWidth, clientHeight);
        Trace($"layout width={clientWidth} height={clientHeight}");
    }

    private void PositionPopupRoot(ProGpuRetainedDrawingVisual rootVisual)
    {
        rootVisual.Offset = new Vector2((float)LogicalX, (float)LogicalY);
        rootVisual.Size = new Vector2(Width, Height);
        rootVisual.Transform = Matrix4x4.Identity;
        rootVisual.Scale = Vector3.One;
        rootVisual.RenderTransformOrigin = Vector2.Zero;
    }

    private static void Trace(string message)
    {
        if (!s_tracePopup)
        {
            return;
        }

        Console.WriteLine($"ProGPU WPF popup: {message}");
    }

    private static bool IsTraceEnabled(string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return value != null &&
            (value.Length == 0 ||
             string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPointerInput(WpfInputEventKind kind)
    {
        return kind is WpfInputEventKind.MouseMove or
            WpfInputEventKind.MouseDown or
            WpfInputEventKind.MouseUp or
            WpfInputEventKind.MouseWheel;
    }

    private static int ToScreenDeviceCoordinate(int ownerCoordinate, double logicalOffset, double scale)
    {
        double value = ownerCoordinate + (logicalOffset * scale);
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
