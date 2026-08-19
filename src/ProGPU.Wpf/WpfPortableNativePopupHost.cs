using System;
using ProGPU.Wpf.Interop;
using ProGPU.Scene;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

internal interface IWpfPortableNativePopupHost : IDisposable
{
    bool HasPresentedFrame { get; }

    bool HasGpuHitTestCache { get; }

    bool TryGetGpuHitTestCacheSnapshot(out ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot snapshot);

    bool TryHitTestOwners(double x, double y, Span<object?> owners, out int ownerCount);

    bool TryQueryHitTestBoundsOwners(
        double minX,
        double minY,
        double maxX,
        double maxY,
        Span<object?> owners,
        out int ownerCount);

    void SetInputHandler(Func<WpfInputEventArgs, bool> inputHandler);

    void RaiseInputForDiagnostics(WpfInputEventArgs input);

    void SetDeviceScale(double dpiScaleX, double dpiScaleY);

    void SetPosition(int x, int y);

    void SetSize(int width, int height);

    void Show();

    void Hide();
}

internal sealed class WpfPortableNativePopupHost : IWpfPortableNativePopupHost
{
    private readonly ProGpuWpfWindowHost _ownerHost;
    private readonly ProGpuWpfWindowHost _popupHost;
    private Func<WpfInputEventArgs, bool>? _inputHandler;
    private double _dpiScaleX;
    private double _dpiScaleY;
    private int _nativeLogicalX;
    private int _nativeLogicalY;
    private bool _isInitialized;
    private bool _isVisible;
    private bool _isPumping;
    private bool _disposeWhenPumpCompletes;
    private bool _isDisposed;

    public bool HasPresentedFrame => !_isDisposed && _popupHost.HasPresentedFrame;

    public bool HasGpuHitTestCache =>
        TryGetGpuHitTestCacheSnapshot(out var snapshot) && snapshot.OwnerCount > 0;

    public bool TryGetGpuHitTestCacheSnapshot(out ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot snapshot)
    {
        snapshot = default;
        return !_isDisposed && _popupHost.TryGetGpuHitTestCacheSnapshot(out snapshot);
    }

    public bool TryHitTestOwners(double x, double y, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        return !_isDisposed && _popupHost.TryHitTestOwners(x, y, owners, out ownerCount);
    }

    public bool TryQueryHitTestBoundsOwners(
        double minX,
        double minY,
        double maxX,
        double maxY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        return !_isDisposed && _popupHost.TryQueryHitTestBoundsOwners(
            minX,
            minY,
            maxX,
            maxY,
            owners,
            out ownerCount);
    }

    private WpfPortableNativePopupHost(
        ProGpuWpfWindowHost ownerHost,
        IPortablePresentationSourceHost source,
        PortablePopupCreateRequest request,
        double dpiScaleX,
        double dpiScaleY)
    {
        _ownerHost = ownerHost;
        _dpiScaleX = NormalizeDeviceScale(dpiScaleX);
        _dpiScaleY = NormalizeDeviceScale(dpiScaleY);
        _nativeLogicalX = ToNativeLogicalScreenCoordinate(request.PopupScreenDeviceX, _dpiScaleX);
        _nativeLogicalY = ToNativeLogicalScreenCoordinate(request.PopupScreenDeviceY, _dpiScaleY);
        _popupHost = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = string.Empty,
            Width = 1,
            Height = 1,
            // WPF placement uses device-screen pixels. Silk/GLFW window positions use
            // native logical screen coordinates, including on Retina displays.
            Left = _nativeLogicalX,
            Top = _nativeLogicalY,
            IsVisible = false,
            IsEventDriven = false,
            // Native transient ownership keeps the popup above its owner. A global topmost
            // level would incorrectly float it above other applications on macOS/X11.
            Topmost = false,
            ShowActivated = false,
            TransparentFramebuffer = request.IsTransparent,
            WindowBorder = ProGpuWpfWindowBorder.Hidden,
            EnablePortablePopupService = false,
            IncludePortablePopupRootsInWpfReplay = true,
            NativePointerCoordinatesAreOwnerRelative = OperatingSystem.IsMacOS(),
            SharedRenderDeviceContext = ownerHost.CompositionTarget?.Context,
            CompositorOptions = new CompositorOptions
            {
                GlyphAtlasSize = 1024,
                PathAtlasSize = 1024,
                InitialVertexCount = 4096,
                InitialIndexCount = 6144,
                EnableGpuHitTesting = true,
                EnableCompiledSceneCache = true,
                PrimarySampleCount = 4
            }
        })
        {
            // Input and native-window events are per surface. Reusing the process-wide default
            // service instance would broadcast the popup's move/focus callbacks to the owner.
            PlatformServices = new CrossPlatformWpfPlatformServices(),
            WpfResourceResolver = ownerHost.WpfResourceResolver,
            WpfImageSourceAdapter = ownerHost.WpfImageSourceAdapter
        };
        _popupHost.UseExternalNativeLoopPump();

        if (!_popupHost.TryBindPortablePresentationSource(source))
        {
            _popupHost.Dispose();
            throw new PlatformNotSupportedException("The popup presentation source cannot be bound to a native ProGPU host.");
        }

        _popupHost.UpdatePortablePresentationSourceDpiScale(dpiScaleX, dpiScaleY);
        _popupHost.InputReceived += OnPopupInputReceived;
        _ownerHost.UpdateTick += OnOwnerUpdateTick;
    }

    public static IWpfPortableNativePopupHost? TryCreate(
        ProGpuWpfWindowHost ownerHost,
        IPortablePresentationSourceHost source,
        PortablePopupCreateRequest request,
        double dpiScaleX,
        double dpiScaleY)
    {
        ArgumentNullException.ThrowIfNull(ownerHost);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);

        bool explicitlyDisabled = string.Equals(
            Environment.GetEnvironmentVariable("PROGPU_WPF_DISABLE_NATIVE_POPUPS"),
            "1",
            StringComparison.Ordinal);
        bool isWayland = ownerHost.SilkWindow?.Native?.Wayland is not null;
        if (!ShouldUseNativePopup(
                OperatingSystem.IsWindows(),
                OperatingSystem.IsMacOS(),
                explicitlyDisabled,
                isWayland))
        {
            return null;
        }

        return new WpfPortableNativePopupHost(ownerHost, source, request, dpiScaleX, dpiScaleY);
    }

    internal static bool ShouldUseNativePopup(
        bool isWindows,
        bool isMacOS,
        bool explicitlyDisabled,
        bool isWayland)
    {
        // GLFW exposes Wayland popup surfaces as ordinary xdg_toplevel windows and
        // cannot position them. Cocoa transient child windows are positionable and
        // their owner-relative pointer coordinates are normalized by the popup bridge.
        // Windows continues to use WPF's native HWND popup path. X11 and Cocoa use
        // native transient popup windows.
        _ = isMacOS;
        return !isWindows && !explicitlyDisabled && !isWayland;
    }

    public void SetInputHandler(Func<WpfInputEventArgs, bool> inputHandler)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _inputHandler = inputHandler ?? throw new ArgumentNullException(nameof(inputHandler));
    }

    public void RaiseInputForDiagnostics(WpfInputEventArgs input)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(input);
        // Native input is dispatched by DoEvents after its pre-event render has
        // refreshed the GPU hit-test cache. Preserve that ordering for injected
        // input so diagnostics exercise the same popup state as the native path.
        PumpEventsIfNeeded();
        _popupHost.RaiseInputForDiagnostics(input);
    }

    public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _dpiScaleX = NormalizeDeviceScale(dpiScaleX);
        _dpiScaleY = NormalizeDeviceScale(dpiScaleY);
        _popupHost.UpdatePortablePresentationSourceDpiScale(dpiScaleX, dpiScaleY);
    }

    public void SetPosition(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _nativeLogicalX = ToNativeLogicalScreenCoordinate(x, _dpiScaleX);
        _nativeLogicalY = ToNativeLogicalScreenCoordinate(y, _dpiScaleY);
        _popupHost.SetPosition(_nativeLogicalX, _nativeLogicalY);
    }

    internal static int ToNativeLogicalScreenCoordinate(int deviceCoordinate, double deviceScale)
    {
        double normalizedScale = NormalizeDeviceScale(deviceScale);
        double value = deviceCoordinate / normalizedScale;
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

    private static double NormalizeDeviceScale(double deviceScale) =>
        double.IsFinite(deviceScale) && deviceScale > 0.0
            ? deviceScale
            : 1.0;

    public void SetSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _popupHost.SetClientSize(Math.Max(1, width), Math.Max(1, height));
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        EnsureInitialized();
        // X11 window managers are allowed to ignore a position supplied before a
        // transient window is mapped. Reapply the settled WPF placement after the
        // owner/type hints exist and immediately before the nonactivating map.
        _popupHost.SetPosition(_nativeLogicalX, _nativeLogicalY);
        _isVisible = true;
        try
        {
            _popupHost.ShowWithoutActivation();
        }
        catch
        {
            _isVisible = false;
            throw;
        }
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isVisible = false;
        _popupHost.Hide();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _ownerHost.UpdateTick -= OnOwnerUpdateTick;
        _popupHost.InputReceived -= OnPopupInputReceived;
        _inputHandler = null;
        if (_isPumping)
        {
            _disposeWhenPumpCompletes = true;
            return;
        }

        _popupHost.Dispose();
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _popupHost.InitializeHidden();
        if (_ownerHost.SilkWindow is { } ownerWindow && _popupHost.SilkWindow is { } popupWindow)
        {
            _ownerHost.PlatformServices.WindowDecorations.TryConfigurePopupOwner(ownerWindow, popupWindow);
        }

        _isInitialized = true;
    }

    private void OnOwnerUpdateTick(object? sender, EventArgs e)
    {
        PumpEventsIfNeeded();
    }

    private void PumpEventsIfNeeded()
    {
        if (!ShouldPumpEvents(_isDisposed, _isInitialized, _isVisible, _isPumping))
        {
            return;
        }

        _isPumping = true;
        try
        {
            _popupHost.DoEvents();
        }
        catch (ObjectDisposedException) when (_isDisposed)
        {
        }
        finally
        {
            _isPumping = false;
            if (_disposeWhenPumpCompletes)
            {
                _disposeWhenPumpCompletes = false;
                _popupHost.Dispose();
            }
        }
    }

    internal static bool ShouldPumpEvents(
        bool isDisposed,
        bool isInitialized,
        bool isVisible,
        bool isPumping) =>
        !isDisposed && isInitialized && isVisible && !isPumping;

    private void OnPopupInputReceived(object? sender, WpfInputEventArgs e)
    {
        if (!_isDisposed && _inputHandler?.Invoke(e) == true)
        {
            e.Handled = true;
        }
    }
}
