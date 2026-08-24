using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortableWindowActivation : IDisposable
{
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_ACTIVATEAPP = 0x001C;
    private const int WM_SHOWWINDOW = 0x0018;
    private const int WM_MOVE = 0x0003;
    private const int WM_SIZE = 0x0005;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP = 0x00A2;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int WM_NCRBUTTONDOWN = 0x00A4;
    private const int WM_NCRBUTTONUP = 0x00A5;
    private const int WM_NCRBUTTONDBLCLK = 0x00A6;
    private const int WM_NCMBUTTONDOWN = 0x00A7;
    private const int WM_NCMBUTTONUP = 0x00A8;
    private const int WM_NCMBUTTONDBLCLK = 0x00A9;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int WA_INACTIVE = 0;
    private const int WA_ACTIVE = 1;
    private const int MA_ACTIVATEANDEAT = 2;
    private const int MA_NOACTIVATE = 3;
    private const int MA_NOACTIVATEANDEAT = 4;

    private static readonly ConditionalWeakTable<object, WpfPortableWindowActivation> s_activeActivations = new();
    private static readonly object s_activeActivationsByHandleLock = new();
    private static readonly Dictionary<IntPtr, WeakReference<WpfPortableWindowActivation>> s_activeActivationsByHandle = new();
    private static readonly object s_nonActivatingOwnedActivationsLock = new();
    private static readonly List<WeakReference<WpfPortableWindowActivation>> s_nonActivatingOwnedActivations = new();
    private static readonly TimeSpan ApplicationIdleFlushTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UpdateTickFlushTimeout = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan DispatcherTimerPumpInterval = TimeSpan.FromMilliseconds(16);
    private static readonly WpfPortableDisplayMetricsSource s_displayMetricsSource =
        new(() => CrossPlatformWpfPlatformServices.Instance.Monitors);
    private static IDisposable? s_displayMetricsRegistration;
    private bool _isDisposed;
    private bool _isClosingFromNative;
    private bool _isClosingFromWpf;
    private bool _isFlushingWpfDispatcher;
    private bool _isNativeRunStarted;
    private IDisposable? _mediaContextRenderRegistration;
    private IWpfTimer? _dispatcherTimerPump;
    private bool _showActivated = true;
    private bool _isRegisteredNonActivatingOwnedWindow;
    private object? _ownerWindow;
    private readonly HashSet<WpfMouseButton> _pressedMouseButtons = new();

    static WpfPortableWindowActivation()
    {
        PortableWpfServiceRegistry.ClipboardServiceRegistered += OnClipboardServiceRegistered;
        PortableWpfServiceRegistry.MessageBoxServiceRegistered += OnMessageBoxServiceRegistered;
        PortableWpfServiceRegistry.FileDialogServiceRegistered += OnFileDialogServiceRegistered;
        PortableWpfServiceRegistry.ColorDialogServiceRegistered += OnColorDialogServiceRegistered;
        PortableWpfServiceRegistry.FontDialogServiceRegistered += OnFontDialogServiceRegistered;
    }

    private WpfPortableWindowActivation(
        ProGpuWpfWindowHost host,
        object window,
        object rootVisual,
        object portablePresentationSource)
    {
        Host = host;
        Window = window;
        RootVisual = rootVisual;
        PortablePresentationSource = portablePresentationSource;
        Host.Closing += OnHostClosing;
        Host.InputReceived += OnHostInputReceived;
        Host.WindowEventReceived += OnHostWindowEventReceived;
        Host.DragDropReceived += OnHostDragDropReceived;
        Host.RenderWakeupRequested += OnHostRenderWakeupRequested;
        Host.UpdateTick += OnHostUpdateTick;
        RegisterActiveActivation(window, this);
        SynchronizeInitialWindowState(updatePortablePresentationSource: false);
    }

    public ProGpuWpfWindowHost Host { get; }

    public object Window { get; }

    public object RootVisual { get; }

    public object PortablePresentationSource { get; }

    public static bool TryRegisterPresentationFrameworkActivation(
        Func<object, ProGpuWpfWindowHost>? hostFactory = null)
    {
        if (PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out var activationService))
        {
            s_displayMetricsRegistration ??=
                PortableWpfServiceRegistry.RegisterDisplayMetricsSource(s_displayMetricsSource);
            activationService.Register(CreateWindowActivationCallbacks(hostFactory));
            TryRegisterPresentationFrameworkLauncherService();
            TryRegisterPresentationFrameworkMessageBoxService();
            TryRegisterPresentationFrameworkFileDialogService();
            TryRegisterWinFormsCompatClipboardService();
            TryRegisterWinFormsCompatMessageBoxService();
            TryRegisterWinFormsCompatFileDialogService();
            TryRegisterWinFormsCompatColorDialogService();
            TryRegisterWinFormsCompatFontDialogService();
            return true;
        }

        return false;
    }

    private static PortableWindowActivationCallbacks CreateWindowActivationCallbacks(
        Func<object, ProGpuWpfWindowHost>? hostFactory)
    {
        return new PortableWindowActivationCallbacks(
            activate: window =>
            {
                return TryCreateActivation(window, hostFactory, out var activation)
                    ? activation
                    : null;
            },
            show: activation => ((WpfPortableWindowActivation)activation).Show(),
            hide: activation => ((WpfPortableWindowActivation)activation).Hide(),
            setWindowState: (activation, windowState) =>
                ((WpfPortableWindowActivation)activation).SetWindowState(windowState),
            setTitle: (activation, title) =>
                ((WpfPortableWindowActivation)activation).SetTitle(title),
            setClientSize: (activation, width, height) =>
                ((WpfPortableWindowActivation)activation).SetClientSize(width, height),
            setPosition: (activation, left, top) =>
                ((WpfPortableWindowActivation)activation).SetPosition(left, top),
            setTopmost: (activation, topmost) =>
                ((WpfPortableWindowActivation)activation).SetTopmost(topmost),
            setWindowBorder: (activation, resizeMode, windowStyle) =>
                ((WpfPortableWindowActivation)activation).SetWindowBorder(resizeMode, windowStyle),
            close: activation => ((WpfPortableWindowActivation)activation).Close(),
            run: activation => ((WpfPortableWindowActivation)activation).Run(),
            dispose: activation => ((WpfPortableWindowActivation)activation).Dispose(),
            dragMove: activation => ((WpfPortableWindowActivation)activation).TryDragMove(),
            getHandle: activation =>
                ((WpfPortableWindowActivation)activation).Host.PortablePresentationSourceBridge?.Handle ?? IntPtr.Zero,
            setWindowRegion: TrySetWindowRegion,
            requestActivation: activation =>
                ((WpfPortableWindowActivation)activation).TryActivate(),
            setIcon: (activation, icon) =>
                ((WpfPortableWindowActivation)activation).SetIcon(icon));
    }

    public static bool TryRegisterPresentationCoreClipboardService()
    {
        if (PortableWpfServiceRegistry.TryGetClipboardService(
                PortableWpfServiceKey.PresentationCore,
                out var clipboardService))
        {
            clipboardService.Register(GetPortableClipboardText, SetPortableClipboardText);
            return true;
        }

        return false;
    }

    public static bool TryRegisterWinFormsCompatClipboardService()
    {
        if (PortableWpfServiceRegistry.TryGetClipboardService(
                PortableWpfServiceKey.WinForms,
                out var clipboardService))
        {
            clipboardService.Register(GetPortableClipboardText, SetPortableClipboardText);
            return true;
        }

        return false;
    }

    internal static bool TryGetActiveHost(object? window, out ProGpuWpfWindowHost? host)
    {
        if (window != null &&
            s_activeActivations.TryGetValue(window, out var activation) &&
            !activation._isDisposed)
        {
            host = activation.Host;
            return true;
        }

        host = null;
        return false;
    }

    /// <summary>
    /// Resolves the native OS window handle backing a WPF <see cref="Window"/> on this ProGPU/
    /// Silk.NET-hosted platform. Thin wrapper over the resolution that already lives on the
    /// window's <see cref="WpfPortablePresentationSourceBridge.TryGetNativeHandle"/> - see that
    /// member for what the handle actually is and why it exists alongside the portable
    /// <c>Handle</c> WPF's <c>HwndSource</c> compat shim already exposes.
    /// </summary>
    public static bool TryGetNativeWindowHandle(object? window, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!TryGetActiveHost(window, out var host) ||
            host?.PortablePresentationSourceBridge is not { } bridge)
        {
            return false;
        }

        return bridge.TryGetNativeHandle(out handle);
    }

    public static bool TryRegisterPresentationFrameworkLauncherService()
    {
        if (PortableWpfServiceRegistry.TryGetLauncherService(
                PortableWpfServiceKey.PresentationFramework,
                out var launcherService))
        {
            launcherService.Register(LaunchPortableUri);
            return true;
        }

        return false;
    }

    public static bool TryRegisterPresentationFrameworkMessageBoxService()
    {
        if (PortableWpfServiceRegistry.TryGetMessageBoxService(
                PortableWpfServiceKey.PresentationFramework,
                out var messageBoxService))
        {
            messageBoxService.RegisterFallback(ShowPortableMessageBox);
            return true;
        }

        return false;
    }

    public static bool TryRegisterWinFormsCompatMessageBoxService()
    {
        if (PortableWpfServiceRegistry.TryGetMessageBoxService(
                PortableWpfServiceKey.WinForms,
                out var messageBoxService))
        {
            messageBoxService.RegisterFallback(ShowPortableMessageBox);
            return true;
        }

        return false;
    }

    public static bool TryRegisterPresentationFrameworkFileDialogService()
    {
        if (PortableWpfServiceRegistry.TryGetFileDialogService(
                PortableWpfServiceKey.PresentationFramework,
                out var fileDialogService))
        {
            fileDialogService.RegisterResult(ShowPortableFileDialog);
            return true;
        }

        return false;
    }

    public static bool TryRegisterWinFormsCompatFileDialogService()
    {
        if (PortableWpfServiceRegistry.TryGetFileDialogService(
                PortableWpfServiceKey.WinForms,
                out var fileDialogService))
        {
            fileDialogService.RegisterResult(ShowPortableFileDialog);
            return true;
        }

        return false;
    }

    public static bool TryRegisterWinFormsCompatColorDialogService()
    {
        if (PortableWpfServiceRegistry.TryGetColorDialogService(
                PortableWpfServiceKey.WinForms,
                out var colorDialogService))
        {
            colorDialogService.Register(ShowPortableColorDialog);
            return true;
        }

        return false;
    }

    public static bool TryRegisterWinFormsCompatFontDialogService()
    {
        if (PortableWpfServiceRegistry.TryGetFontDialogService(
                PortableWpfServiceKey.WinForms,
                out var fontDialogService))
        {
            fontDialogService.Register(ShowPortableFontDialog);
            return true;
        }

        return false;
    }

    private static void OnClipboardServiceRegistered(IPortableClipboardServiceRegistrar service)
    {
        if (service.ServiceKey == PortableWpfServiceKey.PresentationCore ||
            service.ServiceKey == PortableWpfServiceKey.WinForms)
        {
            service.Register(GetPortableClipboardText, SetPortableClipboardText);
        }
    }

    private static void OnMessageBoxServiceRegistered(IPortableMessageBoxServiceRegistrar service)
    {
        if (service.ServiceKey == PortableWpfServiceKey.PresentationFramework ||
            service.ServiceKey == PortableWpfServiceKey.WinForms)
        {
            service.RegisterFallback(ShowPortableMessageBox);
        }
    }

    private static void OnFileDialogServiceRegistered(IPortableFileDialogServiceRegistrar service)
    {
        if (service.ServiceKey == PortableWpfServiceKey.PresentationFramework ||
            service.ServiceKey == PortableWpfServiceKey.WinForms)
        {
            service.RegisterResult(ShowPortableFileDialog);
        }
    }

    private static void OnColorDialogServiceRegistered(IPortableColorDialogServiceRegistrar service)
    {
        if (service.ServiceKey == PortableWpfServiceKey.WinForms)
        {
            service.Register(ShowPortableColorDialog);
        }
    }

    private static void OnFontDialogServiceRegistered(IPortableFontDialogServiceRegistrar service)
    {
        if (service.ServiceKey == PortableWpfServiceKey.WinForms)
        {
            service.Register(ShowPortableFontDialog);
        }
    }

    public void Show()
    {
        ThrowIfDisposed();
        SynchronizeInitialWindowState(updatePortablePresentationSource: true);
        if (ShouldDeferNativeShowUntilRun())
        {
            Host.DeferShowUntilRun();
            DispatchPortableShowWindowHook(isShown: true);
            return;
        }

        if (_showActivated)
        {
            Host.Show();
        }
        else
        {
            Host.ShowWithoutActivation();
        }

        DispatchPortableShowWindowHook(isShown: true);
        if (!_showActivated)
        {
            TrySetWindowActivationState(Window, isActive: false);
        }

        FlushWpfDispatcherOperations("Loaded", "Render");
    }

    internal bool TryActivate()
    {
        ThrowIfDisposed();
        return Host.TryActivate();
    }

    public void Hide()
    {
        ThrowIfDisposed();
        Host.Hide();
        DispatchPortableShowWindowHook(isShown: false);
    }

    public void SetWindowState(object? windowState)
    {
        ThrowIfDisposed();

        if (TryMapWindowState(windowState, out ProGpuWpfWindowState mappedWindowState))
        {
            Host.SetWindowState(mappedWindowState);
        }
    }

    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);

        Host.SetTitle(title);
    }

    public void SetIcon(object? icon)
    {
        ThrowIfDisposed();
        Host.SetIcon(icon);
    }

    public void SetClientSize(object? width, object? height)
    {
        ThrowIfDisposed();

        var clientWidth = TryMapPositiveDimension(width, out double mappedWidth)
            ? ToLogicalClientDimension(mappedWidth)
            : Host.Width;
        var clientHeight = TryMapPositiveDimension(height, out double mappedHeight)
            ? ToLogicalClientDimension(mappedHeight)
            : Host.Height;

        Host.SetClientSize(clientWidth, clientHeight);
    }

    public void SetPosition(object? left, object? top)
    {
        ThrowIfDisposed();

        var windowLeft = TryMapFiniteDimension(left, out double mappedLeft)
            ? ToLogicalPositionDimension(mappedLeft)
            : Host.Left;
        var windowTop = TryMapFiniteDimension(top, out double mappedTop)
            ? ToLogicalPositionDimension(mappedTop)
            : Host.Top;

        if (windowLeft.HasValue && windowTop.HasValue)
        {
            Host.SetPosition(windowLeft.Value, windowTop.Value);
        }
    }

    public void SetTopmost(bool topmost)
    {
        ThrowIfDisposed();

        Host.SetTopmost(topmost);
    }

    public void SetWindowBorder(object? resizeMode, object? windowStyle)
    {
        ThrowIfDisposed();

        Host.SetWindowBorder(ResolveWindowBorder(resizeMode, windowStyle, Host.WindowBorder));
    }

    public bool SetWindowRegion(PortableWindowRegion region)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(region);

        Host.SetWindowRegion(region);
        return true;
    }

    public void Close()
    {
        if (_isDisposed || _isClosingFromNative)
        {
            return;
        }

        _isClosingFromWpf = true;
        try
        {
            Host.Close();
        }
        finally
        {
            _isClosingFromWpf = false;
        }
    }

    public void Run()
    {
        ThrowIfDisposed();
        _isNativeRunStarted = true;
        SynchronizeInitialWindowState(updatePortablePresentationSource: true);
        if (_isDisposed)
        {
            return;
        }

        StartDispatcherTimerPump();
        try
        {
            Host.Run(_showActivated);
        }
        finally
        {
            StopDispatcherTimerPump();
        }
    }

    private bool ShouldDeferNativeShowUntilRun()
    {
        return !_isNativeRunStarted && IsCurrentApplicationMainWindow(Window);
    }

    public bool TryDragMove()
    {
        ThrowIfDisposed();
        return Host.TryBeginDragMove();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Host.Closing -= OnHostClosing;
        Host.InputReceived -= OnHostInputReceived;
        Host.WindowEventReceived -= OnHostWindowEventReceived;
        Host.DragDropReceived -= OnHostDragDropReceived;
        Host.RenderWakeupRequested -= OnHostRenderWakeupRequested;
        Host.UpdateTick -= OnHostUpdateTick;
        StopDispatcherTimerPump();
        _mediaContextRenderRegistration?.Dispose();
        _mediaContextRenderRegistration = null;
        _pressedMouseButtons.Clear();
        RemoveNonActivatingOwnedWindowRegistration();
        s_activeActivations.Remove(Window);
        UnregisterActiveActivationHandle(this);
        Host.Dispose();
        _isDisposed = true;
    }

    private static void RegisterActiveActivation(object window, WpfPortableWindowActivation activation)
    {
        s_activeActivations.Remove(window);
        s_activeActivations.Add(window, activation);
        RegisterActiveActivationHandle(activation);
    }

    private static void RegisterActiveActivationHandle(WpfPortableWindowActivation activation)
    {
        IntPtr handle = activation.Host.PortablePresentationSourceBridge?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        lock (s_activeActivationsByHandleLock)
        {
            s_activeActivationsByHandle[handle] = new WeakReference<WpfPortableWindowActivation>(activation);
        }
    }

    private static void UnregisterActiveActivationHandle(WpfPortableWindowActivation activation)
    {
        lock (s_activeActivationsByHandleLock)
        {
            foreach (var entry in s_activeActivationsByHandle.ToArray())
            {
                if (!entry.Value.TryGetTarget(out var registeredActivation) ||
                    ReferenceEquals(registeredActivation, activation))
                {
                    s_activeActivationsByHandle.Remove(entry.Key);
                }
            }
        }
    }

    private static bool TrySetWindowRegion(IntPtr handle, PortableWindowRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        lock (s_activeActivationsByHandleLock)
        {
            if (!s_activeActivationsByHandle.TryGetValue(handle, out var weakActivation))
            {
                return false;
            }

            if (!weakActivation.TryGetTarget(out var activation) || activation._isDisposed)
            {
                s_activeActivationsByHandle.Remove(handle);
                return false;
            }

            return activation.SetWindowRegion(region);
        }
    }

    private void UpdateNonActivatingOwnedWindowRegistration()
    {
        if (!_showActivated && _ownerWindow != null)
        {
            RegisterNonActivatingOwnedWindow();
            return;
        }

        RemoveNonActivatingOwnedWindowRegistration();
    }

    private void RegisterNonActivatingOwnedWindow()
    {
        if (_isRegisteredNonActivatingOwnedWindow)
        {
            return;
        }

        lock (s_nonActivatingOwnedActivationsLock)
        {
            CleanupNonActivatingOwnedWindowRegistrations();
            s_nonActivatingOwnedActivations.Add(new WeakReference<WpfPortableWindowActivation>(this));
            _isRegisteredNonActivatingOwnedWindow = true;
        }
    }

    private void RemoveNonActivatingOwnedWindowRegistration()
    {
        if (!_isRegisteredNonActivatingOwnedWindow)
        {
            return;
        }

        lock (s_nonActivatingOwnedActivationsLock)
        {
            for (int i = s_nonActivatingOwnedActivations.Count - 1; i >= 0; i--)
            {
                if (!s_nonActivatingOwnedActivations[i].TryGetTarget(out var activation) ||
                    ReferenceEquals(activation, this))
                {
                    s_nonActivatingOwnedActivations.RemoveAt(i);
                }
            }
        }

        _isRegisteredNonActivatingOwnedWindow = false;
    }

    private static bool HasVisibleNonActivatingOwnedWindow(object ownerWindow)
    {
        lock (s_nonActivatingOwnedActivationsLock)
        {
            var hasVisibleOwnedWindow = false;
            for (int i = s_nonActivatingOwnedActivations.Count - 1; i >= 0; i--)
            {
                if (!s_nonActivatingOwnedActivations[i].TryGetTarget(out var activation) ||
                    activation._isDisposed)
                {
                    s_nonActivatingOwnedActivations.RemoveAt(i);
                    continue;
                }

                if (!activation._showActivated &&
                    ReferenceEquals(activation._ownerWindow, ownerWindow) &&
                    activation.Host.IsVisible)
                {
                    hasVisibleOwnedWindow = true;
                }
            }

            return hasVisibleOwnedWindow;
        }
    }

    private static void CleanupNonActivatingOwnedWindowRegistrations()
    {
        for (int i = s_nonActivatingOwnedActivations.Count - 1; i >= 0; i--)
        {
            if (!s_nonActivatingOwnedActivations[i].TryGetTarget(out var activation) ||
                activation._isDisposed)
            {
                s_nonActivatingOwnedActivations.RemoveAt(i);
            }
        }
    }

    public static bool TryAttach(
        ProGpuWpfWindowHost host,
        object window,
        out WpfPortableWindowActivation? activation,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(window);

        activation = null;
        var rootVisual = ResolveRootVisual(window);
        if (!host.TryCreatePortablePresentationSource(
                rootVisual,
                dpiScaleX,
                dpiScaleY) ||
            host.PortablePresentationSource is not { } portablePresentationSource)
        {
            return false;
        }

        activation = new WpfPortableWindowActivation(host, window, rootVisual, portablePresentationSource);
        activation.TryRegisterMediaContextRenderService();
        return true;
    }

    public static bool TryAttach(
        ProGpuWpfWindowHost host,
        object window,
        object portablePresentationSource,
        out WpfPortableWindowActivation? activation)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(portablePresentationSource);

        activation = null;
        if (!host.TryBindPortablePresentationSource(portablePresentationSource) ||
            host.PortablePresentationSourceBridge is not { } bridge)
        {
            return false;
        }

        var rootVisual = ResolveRootVisual(window);
        bridge.RootVisual = rootVisual;
        activation = new WpfPortableWindowActivation(host, window, rootVisual, portablePresentationSource);
        activation.TryRegisterMediaContextRenderService();
        return true;
    }

    public static ProGpuWpfWindowOptions CreateHostOptions(
        object window,
        ProGpuWpfWindowOptions? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        fallback ??= new ProGpuWpfWindowOptions();
        var options = new ProGpuWpfWindowOptions
        {
            Title = fallback.Title,
            Width = fallback.Width,
            Height = fallback.Height,
            Left = fallback.Left,
            Top = fallback.Top,
            VSync = fallback.VSync,
            IsVisible = fallback.IsVisible,
            Topmost = fallback.Topmost,
            ShowActivated = fallback.ShowActivated,
            TransparentFramebuffer = fallback.TransparentFramebuffer,
            WindowBorder = fallback.WindowBorder,
            WindowState = fallback.WindowState
        };

        if (TryGetPortableWindowState(window, out var windowState))
        {
            ApplyPortableWindowState(windowState, options);
        }

        return options;
    }

    private void SynchronizeInitialWindowState(bool updatePortablePresentationSource)
    {
        if (TryGetPortableWindowState(Window, out var windowState))
        {
            SynchronizeInitialWindowState(windowState, updatePortablePresentationSource);
            return;
        }

        SetHostClientSize(Host.Width, Host.Height, updatePortablePresentationSource);
    }

    private static bool TryGetPortableWindowState(object window, out PortableWindowState state)
    {
        if (window is IPortableWindowStateSource stateSource &&
            stateSource.TryGetPortableWindowState(out state))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static void ApplyPortableWindowState(
        PortableWindowState state,
        ProGpuWpfWindowOptions options)
    {
        if (state.HasTitle)
        {
            options.Title = state.Title ?? string.Empty;
        }

        if (TryGetPositiveDimension(state.HasWidth, state.Width, out var width) ||
            TryGetPositiveDimension(state.HasActualWidth, state.ActualWidth, out width))
        {
            options.Width = ToLogicalClientDimension(width);
        }

        if (TryGetPositiveDimension(state.HasHeight, state.Height, out var height) ||
            TryGetPositiveDimension(state.HasActualHeight, state.ActualHeight, out height))
        {
            options.Height = ToLogicalClientDimension(height);
        }

        if (TryGetFiniteDimension(state.HasLeft, state.Left, out var left))
        {
            options.Left = ToLogicalPositionDimension(left);
        }

        if (TryGetFiniteDimension(state.HasTop, state.Top, out var top))
        {
            options.Top = ToLogicalPositionDimension(top);
        }

        if (TryMapPortableWindowState(state, out var mappedWindowState))
        {
            options.WindowState = mappedWindowState;
        }

        if (state.HasTopmost)
        {
            options.Topmost = state.Topmost;
        }

        if (state.HasShowActivated)
        {
            options.ShowActivated = state.ShowActivated;
        }

        if (state.HasAllowsTransparency)
        {
            options.TransparentFramebuffer = state.AllowsTransparency;
        }

        options.WindowBorder = ResolveWindowBorder(state, options.WindowBorder);
    }

    private void SynchronizeInitialWindowState(
        PortableWindowState state,
        bool updatePortablePresentationSource)
    {
        UpdatePortableActivationHints(state);

        if (state.HasTitle)
        {
            Host.SetTitle(state.Title ?? string.Empty);
        }

        if (state.HasIcon)
        {
            Host.SetIcon(state.Icon);
        }

        if (TryMapPortableWindowState(state, out var mappedWindowState))
        {
            Host.SetWindowState(mappedWindowState);
        }

        if (state.HasTopmost)
        {
            Host.SetTopmost(state.Topmost);
        }

        Host.SetWindowBorder(ResolveWindowBorder(state, Host.WindowBorder));

        var hasWidth =
            TryGetPositiveDimension(state.HasWidth, state.Width, out var width) ||
            TryGetPositiveDimension(state.HasActualWidth, state.ActualWidth, out width);
        var hasHeight =
            TryGetPositiveDimension(state.HasHeight, state.Height, out var height) ||
            TryGetPositiveDimension(state.HasActualHeight, state.ActualHeight, out height);

        if (hasWidth || hasHeight)
        {
            SetHostClientSize(
                hasWidth ? ToLogicalClientDimension(width) : Host.Width,
                hasHeight ? ToLogicalClientDimension(height) : Host.Height,
                updatePortablePresentationSource);
        }
        else
        {
            SetHostClientSize(Host.Width, Host.Height, updatePortablePresentationSource);
        }

        var hasLeft = TryGetFiniteDimension(state.HasLeft, state.Left, out var left);
        var hasTop = TryGetFiniteDimension(state.HasTop, state.Top, out var top);
        var windowLeft = hasLeft ? ToLogicalPositionDimension(left) : Host.Left;
        var windowTop = hasTop ? ToLogicalPositionDimension(top) : Host.Top;
        if (windowLeft.HasValue && windowTop.HasValue)
        {
            Host.SetPosition(windowLeft.Value, windowTop.Value);
        }
    }

    private void UpdatePortableActivationHints(PortableWindowState state)
    {
        _showActivated = !state.HasShowActivated || state.ShowActivated;
        _ownerWindow = state.HasOwner ? state.Owner : null;
        UpdateNonActivatingOwnedWindowRegistration();
    }

    private void SetHostClientSize(int width, int height, bool updatePortablePresentationSource)
    {
        if (updatePortablePresentationSource)
        {
            Host.SetClientSize(width, height);
        }
        else
        {
            Host.SetInitialClientSize(width, height);
        }
    }

    private static object ResolveRootVisual(object window)
    {
        return window;
    }

    private void StartDispatcherTimerPump()
    {
        if (_dispatcherTimerPump != null)
        {
            return;
        }

        _dispatcherTimerPump = Host.PlatformServices.Timers.CreateTimer(
            DispatcherTimerPumpInterval,
            OnDispatcherTimerPumpTick,
            isRepeating: true);
        _dispatcherTimerPump.Start();
    }

    private void StopDispatcherTimerPump()
    {
        _dispatcherTimerPump?.Dispose();
        _dispatcherTimerPump = null;
    }

    private void OnDispatcherTimerPumpTick()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            Host.TryRequestNativeLoopWakeup();
        }
        catch (ObjectDisposedException)
        {
            // A timer callback can race host teardown after a WPF close request.
        }
    }

    private void OnHostClosing(object? sender, ProGpuWpfWindowClosingEventArgs e)
    {
        if (_isDisposed || _isClosingFromWpf)
        {
            return;
        }

        _isClosingFromNative = true;
        try
        {
            if (TryInvokeWindowClose(Window) == WpfWindowCloseResult.Canceled)
            {
                e.Cancel = true;
            }
        }
        finally
        {
            _isClosingFromNative = false;
        }
    }

    private void OnHostWindowEventReceived(object? sender, WpfWindowEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        Host.TraceNativeActivation(
            $"event={e.Kind}, showActivated={_showActivated}, visible={Host.IsVisible}");

        switch (e.Kind)
        {
            case WpfWindowEventKind.Activated:
                DispatchPortableActivationHooks(isActive: true);
                TrySetWindowActivationStateForHostEvent(isActive: true);
                break;
            case WpfWindowEventKind.Deactivated:
                _pressedMouseButtons.Clear();
                DispatchPortableActivationHooks(isActive: false);
                TrySetWindowActivationStateForHostEvent(isActive: false);
                break;
            case WpfWindowEventKind.Shown:
                DispatchPortableShowWindowHook(isShown: true);
                break;
            case WpfWindowEventKind.Hidden:
                _pressedMouseButtons.Clear();
                DispatchPortableShowWindowHook(isShown: false);
                break;
            case WpfWindowEventKind.WindowPositionChanging:
                DispatchPortableWindowPositionChangingHook();
                break;
            case WpfWindowEventKind.WindowPositionChanged:
                DispatchPortableWindowPositionChangedHooks(e.Left, e.Top);
                break;
            case WpfWindowEventKind.WindowSizeChanged:
                DispatchPortableWindowSizeChangedHooks(e.Width, e.Height);
                break;
            case WpfWindowEventKind.NonClientMouseMove:
            case WpfWindowEventKind.NonClientMouseDown:
            case WpfWindowEventKind.NonClientMouseUp:
            case WpfWindowEventKind.NonClientMouseDoubleClick:
                DispatchPortableNonClientMouseHook(e);
                break;
        }
    }

    private void DispatchPortableActivationHooks(bool isActive)
    {
        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null)
        {
            return;
        }

        IntPtr activeWParam = new(isActive ? WA_ACTIVE : WA_INACTIVE);
        bridge.TryDispatchHwndSourceHook(WM_ACTIVATE, activeWParam, IntPtr.Zero, out _, out _);
        bridge.TryDispatchHwndSourceHook(WM_ACTIVATEAPP, isActive ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero, out _, out _);
    }

    private void DispatchPortableShowWindowHook(bool isShown)
    {
        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null)
        {
            return;
        }

        bridge.TryDispatchHwndSourceHook(WM_SHOWWINDOW, isShown ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero, out _, out _);
    }

    private void DispatchPortableWindowPositionChangingHook()
    {
        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null)
        {
            return;
        }

        bridge.TryDispatchHwndSourceHook(WM_WINDOWPOSCHANGING, IntPtr.Zero, IntPtr.Zero, out _, out _);
    }

    private void DispatchPortableWindowPositionChangedHooks(int? left, int? top)
    {
        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null)
        {
            return;
        }

        if (left.HasValue && top.HasValue)
        {
            Host.UpdatePortablePresentationSourceClientOrigin(left.Value, top.Value);
        }

        bridge.TryDispatchHwndSourceHook(WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero, out _, out _);
        if (left.HasValue && top.HasValue)
        {
            bridge.TryDispatchHwndSourceHook(WM_MOVE, IntPtr.Zero, PackSignedLowHigh(left.Value, top.Value), out _, out _);
        }
    }

    private void DispatchPortableWindowSizeChangedHooks(int? width, int? height)
    {
        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null)
        {
            return;
        }

        bridge.TryDispatchHwndSourceHook(WM_WINDOWPOSCHANGING, IntPtr.Zero, IntPtr.Zero, out _, out _);
        bridge.TryDispatchHwndSourceHook(WM_WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero, out _, out _);
        if (width.HasValue && height.HasValue)
        {
            bridge.TryDispatchHwndSourceHook(WM_SIZE, IntPtr.Zero, PackUnsignedLowHigh(width.Value, height.Value), out _, out _);
        }
    }

    private void DispatchPortableNonClientMouseHook(WpfWindowEventArgs e)
    {
        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null || !TryMapNonClientMouseMessage(e.Kind, e.Button, out int message))
        {
            return;
        }

        int hitTestCode = e.HitTestCode == 0 ? HTCAPTION : e.HitTestCode;
        IntPtr lParam = PackSignedLowHigh(e.ScreenX ?? 0, e.ScreenY ?? 0);
        bridge.TryDispatchHwndSourceHook(message, new IntPtr(hitTestCode), lParam, out _, out _);
    }

    private static bool TryMapNonClientMouseMessage(
        WpfWindowEventKind kind,
        WpfMouseButton button,
        out int message)
    {
        switch (kind)
        {
            case WpfWindowEventKind.NonClientMouseMove:
                message = WM_NCMOUSEMOVE;
                return true;
            case WpfWindowEventKind.NonClientMouseDown:
                return TryMapNonClientMouseButtonMessage(
                    button,
                    WM_NCLBUTTONDOWN,
                    WM_NCRBUTTONDOWN,
                    WM_NCMBUTTONDOWN,
                    out message);
            case WpfWindowEventKind.NonClientMouseUp:
                return TryMapNonClientMouseButtonMessage(
                    button,
                    WM_NCLBUTTONUP,
                    WM_NCRBUTTONUP,
                    WM_NCMBUTTONUP,
                    out message);
            case WpfWindowEventKind.NonClientMouseDoubleClick:
                return TryMapNonClientMouseButtonMessage(
                    button,
                    WM_NCLBUTTONDBLCLK,
                    WM_NCRBUTTONDBLCLK,
                    WM_NCMBUTTONDBLCLK,
                    out message);
            default:
                message = 0;
                return false;
        }
    }

    private static bool TryMapNonClientMouseButtonMessage(
        WpfMouseButton button,
        int leftMessage,
        int rightMessage,
        int middleMessage,
        out int message)
    {
        switch (button)
        {
            case WpfMouseButton.Left:
                message = leftMessage;
                return true;
            case WpfMouseButton.Right:
                message = rightMessage;
                return true;
            case WpfMouseButton.Middle:
                message = middleMessage;
                return true;
            default:
                message = 0;
                return false;
        }
    }

    private static IntPtr PackSignedLowHigh(int low, int high)
    {
        uint packed = (ushort)low | ((uint)(ushort)high << 16);
        return new IntPtr(unchecked((int)packed));
    }

    private static IntPtr PackUnsignedLowHigh(int low, int high)
    {
        uint packed = (uint)(ushort)Math.Max(0, low) | ((uint)(ushort)Math.Max(0, high) << 16);
        return new IntPtr(unchecked((int)packed));
    }

    private void TrySetWindowActivationStateForHostEvent(bool isActive)
    {
        if (ShouldSuppressHostActivationEvent(isActive))
        {
            return;
        }

        try
        {
            TrySetWindowActivationState(Window, isActive);
        }
        catch (Exception ex) when (!isActive && IsRecoverablePortableDeactivationException(ex))
        {
            // A native focus-loss callback can arrive while a third-party control still owns mouse capture.
            // Keep the portable host alive if that capture-cancel path rejects an intermediate layout state.
        }
    }

    private bool ShouldSuppressHostActivationEvent(bool isActive)
    {
        if (!_showActivated)
        {
            return true;
        }

        return !isActive &&
            !Host.HasRequestedNativeActivationForAnotherHost() &&
            (Host.HasVisibleNativePortablePopup || HasVisibleNonActivatingOwnedWindow(Window));
    }

    private static bool IsRecoverablePortableDeactivationException(Exception exception)
    {
        var baseException = exception.GetBaseException();
        return baseException is ArgumentException or InvalidOperationException;
    }

    private static bool IsRecoverableDispatcherFlushException(Exception exception)
    {
        var baseException = exception.GetBaseException();
        return baseException is InvalidOperationException invalidOperation &&
            invalidOperation.Message.IndexOf(
                "dispatcher processing is suspended",
                StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnHostRenderWakeupRequested(object? sender, EventArgs e)
    {
        if (_isDisposed || _isFlushingWpfDispatcher)
        {
            return;
        }

        TryPromoteDispatcherTimers(Window);
        if (TryCloseHostWhenWindowDisposed())
        {
            return;
        }

        FlushWpfDispatcherOperations("Input", "Render", "ApplicationIdle");
        TryCloseHostWhenWindowDisposed();
    }

    private void OnHostUpdateTick(object? sender, EventArgs e)
    {
        if (_isDisposed || _isFlushingWpfDispatcher)
        {
            return;
        }

        TryPromoteDispatcherTimers(Window);
        if (TryCloseHostWhenWindowDisposed())
        {
            return;
        }

        FlushWpfDispatcherOperation("Background", UpdateTickFlushTimeout);
        TryCloseHostWhenWindowDisposed();
    }

    private void FlushWpfDispatcherOperations(params string[] markerPriorityNames)
    {
        if (_isFlushingWpfDispatcher)
        {
            return;
        }

        _isFlushingWpfDispatcher = true;
        try
        {
            foreach (string markerPriorityName in markerPriorityNames)
            {
                TimeSpan? timeout = string.Equals(markerPriorityName, "ApplicationIdle", StringComparison.Ordinal)
                    ? ApplicationIdleFlushTimeout
                    : null;
                FlushWpfDispatcherOperation(markerPriorityName, timeout);
            }
        }
        finally
        {
            _isFlushingWpfDispatcher = false;
        }
    }

    private void FlushWpfDispatcherOperation(string markerPriorityName, TimeSpan? timeout)
    {
        TryFlushDispatcherOperations(Window, markerPriorityName, timeout);
    }

    private void OnHostInputReceived(object? sender, WpfInputEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        bool releaseButtonAfterDispatch = e.Kind == WpfInputEventKind.MouseUp &&
            e.Button != WpfMouseButton.None;
        if (e.Kind == WpfInputEventKind.MouseDown && e.Button != WpfMouseButton.None)
        {
            _pressedMouseButtons.Add(e.Button);
        }

        try
        {
            if (TryDispatchHostInputToWindowDispatcher(e))
            {
                return;
            }

            ProcessHostInputAndRequestRender(e);
        }
        catch (Exception exception)
        {
            if (!TryReportInputExceptionToWindowDispatcher(exception))
            {
                throw;
            }
        }
        finally
        {
            if (releaseButtonAfterDispatch)
            {
                _pressedMouseButtons.Remove(e.Button);
            }
        }
    }

    private bool TryReportInputExceptionToWindowDispatcher(Exception exception)
    {
        if (!TryGetWindowActivationService(out var activationService))
        {
            return false;
        }

        return activationService.TryBeginInvokeInput(
            Window,
            () => ExceptionDispatchInfo.Capture(exception).Throw());
    }

    private void ProcessHostInputAndRequestRender(WpfInputEventArgs e)
    {
        ProcessHostInput(e);
        RequestRenderFromMediaContext(RootVisual, TimeSpan.Zero);
    }

    private void ProcessHostInput(WpfInputEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        Host.TraceNativeActivation(
            $"input={e.Kind}, showActivated={_showActivated}, visible={Host.IsVisible}");

        bool suppressActivation = TryDispatchPortableMouseActivateHook(e, out bool eatInput);
        bool isActivationInput = e.Kind == WpfInputEventKind.MouseDown;
        if (isActivationInput && !suppressActivation && _showActivated)
        {
            TrySetWindowActivationState(Window, isActive: true);
        }
        else if (isActivationInput && !suppressActivation && _ownerWindow != null)
        {
            TrySetWindowActivationState(_ownerWindow, isActive: true);
        }

        if (eatInput)
        {
            e.Handled = true;
            return;
        }

        if (Host.TryProcessPortablePopupInput(e))
        {
            return;
        }

        TryForwardInputToWindow(Window, e);
    }

    private bool TryDispatchPortableMouseActivateHook(WpfInputEventArgs e, out bool eatInput)
    {
        eatInput = false;
        if (e.Kind != WpfInputEventKind.MouseDown)
        {
            return false;
        }

        WpfPortablePresentationSourceBridge? bridge = Host.PortablePresentationSourceBridge;
        if (bridge == null || !TryMapMouseDownMessage(e.Button, out int mouseMessage))
        {
            return false;
        }

        if (!bridge.TryDispatchHwndSourceHook(
                WM_MOUSEACTIVATE,
                bridge.Handle,
                PackUnsignedLowHigh(HTCLIENT, mouseMessage),
                out IntPtr result,
                out bool handled) ||
            !handled)
        {
            return false;
        }

        int mouseActivateResult = result.ToInt32();
        eatInput = mouseActivateResult is MA_ACTIVATEANDEAT or MA_NOACTIVATEANDEAT;
        return mouseActivateResult is MA_NOACTIVATE or MA_NOACTIVATEANDEAT;
    }

    private static bool TryMapMouseDownMessage(WpfMouseButton button, out int message)
    {
        switch (button)
        {
            case WpfMouseButton.Left:
                message = WM_LBUTTONDOWN;
                return true;
            case WpfMouseButton.Right:
                message = WM_RBUTTONDOWN;
                return true;
            case WpfMouseButton.Middle:
                message = WM_MBUTTONDOWN;
                return true;
            case WpfMouseButton.XButton1:
            case WpfMouseButton.XButton2:
                message = WM_XBUTTONDOWN;
                return true;
            default:
                message = 0;
                return false;
        }
    }

    private bool TryDispatchHostInputToWindowDispatcher(WpfInputEventArgs e)
    {
        var callback = new Action(() => ProcessHostInputAndRequestRender(e));
        if (TryGetWindowActivationService(out var activationService) &&
            activationService.TryBeginInvokeInput(Window, callback))
        {
            // Passive pointer movement only needs input dispatch here; the owner loop
            // consumes its render request once after the native event batch. During a
            // pressed-button interaction, preserve per-event layout because controls
            // such as Thumb calculate each delta from the preceding move's layout.
            if (e.Kind == WpfInputEventKind.MouseMove && _pressedMouseButtons.Count == 0)
            {
                FlushWpfDispatcherOperations("Input");
            }
            else
            {
                FlushWpfDispatcherOperations("Input", "Render");
            }

            Host.TryRequestNativeLoopWakeup();
            return true;
        }

        return false;
    }

    private static bool TryForwardInputToWindow(object window, WpfInputEventArgs e)
    {
        if (TryGetWindowActivationService(out var activationService))
        {
            var input = CreatePortableWindowInputEvent(e);
            if (activationService.TryProcessInputEvent(window, input))
            {
                e.Handled = input.Handled;
                return true;
            }
        }

        return false;
    }

    private static PortableWindowInputEvent CreatePortableWindowInputEvent(WpfInputEventArgs e)
    {
        return new PortableWindowInputEvent(
            (int)e.Kind,
            e.Key,
            e.ScanCode,
            e.Character,
            e.X,
            e.Y,
            e.DeltaX,
            e.DeltaY,
            (int)e.Button,
            (int)e.Modifiers);
    }

    private void OnHostDragDropReceived(object? sender, WpfDragDropEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        TryForwardDropToWindow(Window, e);
    }

    private static bool TryForwardDropToWindow(object window, WpfDragDropEventArgs e)
    {
        return TryProcessPortableDragDrop(window, e);
    }

    private static bool TryProcessPortableDragDrop(object window, WpfDragDropEventArgs e)
    {
        if (TryGetWindowActivationService(out var activationService) &&
            activationService.TryProcessDragDropEvent(
                window,
                (int)e.Kind,
                e.Data.Files.ToArray(),
                e.Data.Text,
                e.X,
                e.Y,
                (int)e.AllowedEffects,
                (int)e.AcceptedEffect,
                out int typedAcceptedEffect))
        {
            e.AcceptedEffect = (WpfDragDropEffects)typedAcceptedEffect;
            return true;
        }

        return false;
    }

    private static WpfWindowCloseResult TryInvokeWindowClose(object window)
    {
        if (TryGetWindowActivationService(out var activationService) &&
            activationService.TryCloseWindow(window, out var typedCloseResult))
        {
            return MapCloseResult(typedCloseResult);
        }

        return WpfWindowCloseResult.NotInvoked;
    }

    private bool TryCloseHostWhenWindowDisposed()
    {
        if (_isDisposed || _isClosingFromWpf)
        {
            return false;
        }

        if (!TryGetWindowActivationService(out var activationService) ||
            !activationService.TryIsWindowDisposed(Window, out bool isDisposed) ||
            !isDisposed)
        {
            return false;
        }

        Close();
        return true;
    }

    private static WpfWindowCloseResult MapCloseResult(PortableWindowCloseResult result)
    {
        return result switch
        {
            PortableWindowCloseResult.Closed => WpfWindowCloseResult.Closed,
            PortableWindowCloseResult.Canceled => WpfWindowCloseResult.Canceled,
            _ => WpfWindowCloseResult.NotInvoked
        };
    }

    private enum WpfWindowCloseResult
    {
        NotInvoked,
        Closed,
        Canceled
    }

    private static bool TrySetWindowActivationState(object window, bool isActive)
    {
        if (TryGetWindowActivationService(out var activationService) &&
            activationService.TrySetActivationState(window, isActive))
        {
            return true;
        }

        return false;
    }

    private bool TryRegisterMediaContextRenderService()
    {
        if (TryGetWindowActivationService(out var activationService) &&
            activationService.TryRegisterMediaContextRenderService(
                Window,
                RequestRenderFromMediaContext,
                out var registration) &&
            registration != null)
        {
            _mediaContextRenderRegistration?.Dispose();
            _mediaContextRenderRegistration = registration;
            return _mediaContextRenderRegistration != null;
        }

        return false;
    }

    private void RequestRenderFromMediaContext()
    {
        RequestRenderFromMediaContext(null, TimeSpan.Zero);
    }

    private void RequestRenderFromMediaContext(TimeSpan delay)
    {
        RequestRenderFromMediaContext(null, delay);
    }

    private void RequestRenderFromMediaContext(object? invalidatedSource, TimeSpan delay)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            Host.RequestMediaContextRenderAndWakeNativeLoop(invalidatedSource, delay);
        }
        catch (ObjectDisposedException)
        {
            // Host-first disposal can leave one stale MediaContext callback until activation cleanup.
        }
    }

    private static bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout = null)
    {
        if (TryGetWindowActivationService(out var activationService))
        {
            try
            {
                if (activationService.TryFlushDispatcherOperations(window, markerPriorityName, timeout))
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsRecoverableDispatcherFlushException(ex))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryPromoteDispatcherTimers(object window)
    {
        if (TryGetWindowActivationService(out var activationService))
        {
            return activationService.TryPromoteDispatcherTimers(window, Environment.TickCount);
        }

        return false;
    }

    private static bool TryGetWindowActivationService(
        out IPortableWindowActivationServiceRegistrar activationService)
    {
        if (PortableWpfServiceRegistry.TryGetWindowActivationService(
                PortableWpfServiceKey.PresentationFramework,
                out activationService))
        {
            return true;
        }

        activationService = null!;
        return false;
    }

    private static bool IsCurrentApplicationMainWindow(object window)
    {
        if (TryGetWindowActivationService(out var activationService) &&
            activationService.TryIsCurrentApplicationMainWindow(window, out bool isMainWindow))
        {
            return isMainWindow;
        }

        return false;
    }

    private static string? ShowPortableMessageBox(PortableMessageBoxRequest request)
    {
        var options = new WpfMessageBoxOptions
        {
            Owner = request.Owner,
            MessageBoxText = request.MessageBoxText,
            Caption = request.Caption,
            Button = request.Button,
            Icon = request.Icon,
            DefaultResult = request.DefaultResult,
            Options = request.Options,
            FallbackResult = request.FallbackResult
        };

        try
        {
            return CrossPlatformWpfPlatformServices.Instance.MessageBoxes.Show(options);
        }
        catch (PlatformNotSupportedException)
        {
            return options.FallbackResult;
        }
        catch (InvalidOperationException)
        {
            return options.FallbackResult;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return options.FallbackResult;
        }
    }

    private static bool LaunchPortableUri(PortableLaunchRequest request)
    {
        try
        {
            CrossPlatformWpfPlatformServices.Instance.Launcher
                .OpenUriAsync(request.Uri)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? GetPortableClipboardText()
    {
        try
        {
            string? text = CrossPlatformWpfPlatformServices.Instance.Clipboard
                .GetTextAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void SetPortableClipboardText(string? text)
    {
        try
        {
            CrossPlatformWpfPlatformServices.Instance.Clipboard
                .SetTextAsync(text)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static PortableFileDialogResult? ShowPortableFileDialog(PortableFileDialogRequest request)
    {
        string kind = request.Kind;
        var options = new WpfFileDialogOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedItemName,
            FileTypePatterns = ReadFileDialogPatterns(request.Filter),
            AllowMultipleSelection = request.AllowMultipleSelection
        };

        try
        {
            var fileDialogs = CrossPlatformWpfPlatformServices.Instance.FileDialogs;
            if (kind == "OpenFile" && request.AllowMultipleSelection)
            {
                string[]? selectedPaths = fileDialogs.OpenFilesAsync(options).AsTask().GetAwaiter().GetResult();
                return selectedPaths is { Length: > 0 }
                    ? new PortableFileDialogResult(selectedPaths)
                    : null;
            }


            if (kind == "PickFolder")
            {
                string[]? selectedPaths = fileDialogs.PickFoldersAsync(options).AsTask().GetAwaiter().GetResult();
                return selectedPaths is { Length: > 0 }
                    ? new PortableFileDialogResult(selectedPaths)
                    : null;
            }

            string? selectedPath = kind switch
            {
                "SaveFile" => fileDialogs.SaveFileAsync(options).AsTask().GetAwaiter().GetResult(),
                _ => fileDialogs.OpenFileAsync(options).AsTask().GetAwaiter().GetResult()
            };
            return selectedPath == null ? null : new PortableFileDialogResult(selectedPath);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int? ShowPortableColorDialog(PortableColorDialogRequest request)
    {
        var options = new WpfColorDialogOptions
        {
            InitialArgb = request.InitialArgb,
            CustomColors = request.CustomColors
        };

        try
        {
            return CrossPlatformWpfPlatformServices.Instance.ColorDialogs.Show(options);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static PortableFontDialogResult? ShowPortableFontDialog(PortableFontDialogRequest request)
    {
        var options = new WpfFontDialogOptions
        {
            FamilyName = request.FamilyName,
            Size = request.Size,
            Style = request.Style,
            Unit = request.Unit,
            ShowEffects = request.ShowEffects,
            ShowColor = request.ShowColor,
            MinSize = request.MinSize,
            MaxSize = request.MaxSize
        };

        try
        {
            WpfFontDialogResult? result = CrossPlatformWpfPlatformServices.Instance.FontDialogs.Show(options);
            return result == null
                ? null
                : new PortableFontDialogResult(result.FamilyName, result.Size, result.Style, result.Unit);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> ReadFileDialogPatterns(string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return Array.Empty<string>();
        }

        List<string>? patterns = null;
        int segmentStart = 0;
        int segmentIndex = 0;
        for (int i = 0; i <= filter.Length; i++)
        {
            if (i == filter.Length || filter[i] == '|')
            {
                if ((segmentIndex & 1) != 0)
                {
                    AddFileDialogPatterns(filter.AsSpan(segmentStart, i - segmentStart), ref patterns);
                }

                segmentStart = i + 1;
                segmentIndex++;
            }
        }

        return patterns is null ? Array.Empty<string>() : patterns;
    }

    private static void AddFileDialogPatterns(ReadOnlySpan<char> patternSegment, ref List<string>? patterns)
    {
        int patternStart = 0;
        for (int i = 0; i <= patternSegment.Length; i++)
        {
            if (i == patternSegment.Length || patternSegment[i] == ';')
            {
                ReadOnlySpan<char> pattern = patternSegment.Slice(patternStart, i - patternStart).Trim();
                if (!pattern.IsEmpty)
                {
                    patterns ??= new List<string>(4);
                    patterns.Add(pattern.ToString());
                }

                patternStart = i + 1;
            }
        }
    }

    private static bool TryMapWindowState(object? windowState, out ProGpuWpfWindowState mappedWindowState)
    {
        if (TryConvertEnumNumber(windowState, out var value) &&
            TryMapWindowStateValue(value, out mappedWindowState))
        {
            return true;
        }

        return TryMapWindowStateName(windowState?.ToString(), out mappedWindowState);
    }

    private static bool TryMapPortableWindowState(
        PortableWindowState state,
        out ProGpuWpfWindowState mappedWindowState)
    {
        if (!state.HasWindowState)
        {
            mappedWindowState = ProGpuWpfWindowState.Normal;
            return false;
        }

        return TryMapWindowStateValue(state.WindowState, out mappedWindowState);
    }

    private static bool TryMapWindowStateValue(
        int windowState,
        out ProGpuWpfWindowState mappedWindowState)
    {
        switch (windowState)
        {
            case 0:
                mappedWindowState = ProGpuWpfWindowState.Normal;
                return true;
            case 1:
                mappedWindowState = ProGpuWpfWindowState.Minimized;
                return true;
            case 2:
                mappedWindowState = ProGpuWpfWindowState.Maximized;
                return true;
            default:
                mappedWindowState = ProGpuWpfWindowState.Normal;
                return false;
        }
    }

    private static bool TryMapWindowStateName(
        string? windowState,
        out ProGpuWpfWindowState mappedWindowState)
    {
        switch (windowState)
        {
            case "Minimized":
                mappedWindowState = ProGpuWpfWindowState.Minimized;
                return true;
            case "Maximized":
                mappedWindowState = ProGpuWpfWindowState.Maximized;
                return true;
            case "Normal":
                mappedWindowState = ProGpuWpfWindowState.Normal;
                return true;
            default:
                mappedWindowState = ProGpuWpfWindowState.Normal;
                return false;
        }
    }

    private static ProGpuWpfWindowBorder ResolveWindowBorder(
        PortableWindowState state,
        ProGpuWpfWindowBorder fallback)
    {
        if (state.HasWindowStyle && state.WindowStyle == 0)
        {
            return state.HasResizeMode &&
                TryMapResizeModeValue(state.ResizeMode, out ProGpuWpfWindowBorder customChromeBorder) &&
                customChromeBorder == ProGpuWpfWindowBorder.Resizable
                    ? ProGpuWpfWindowBorder.HiddenResizable
                    : ProGpuWpfWindowBorder.Hidden;
        }

        return state.HasResizeMode &&
            TryMapResizeModeValue(state.ResizeMode, out ProGpuWpfWindowBorder mappedBorder)
                ? mappedBorder
                : fallback;
    }

    private static ProGpuWpfWindowBorder ResolveWindowBorder(
        object? resizeMode,
        object? windowStyle,
        ProGpuWpfWindowBorder fallback)
    {
        if (IsHiddenWindowStyle(windowStyle))
        {
            return TryMapResizeModeToWindowBorder(resizeMode, out ProGpuWpfWindowBorder customChromeBorder) &&
                customChromeBorder == ProGpuWpfWindowBorder.Resizable
                    ? ProGpuWpfWindowBorder.HiddenResizable
                    : ProGpuWpfWindowBorder.Hidden;
        }

        return TryMapResizeModeToWindowBorder(resizeMode, out ProGpuWpfWindowBorder mappedBorder)
            ? mappedBorder
            : fallback;
    }

    private static bool TryMapResizeModeToWindowBorder(
        object? resizeMode,
        out ProGpuWpfWindowBorder windowBorder)
    {
        windowBorder = ProGpuWpfWindowBorder.Resizable;
        if (resizeMode == null)
        {
            return false;
        }

        if (TryConvertEnumNumber(resizeMode, out var value))
        {
            return TryMapResizeModeValue(value, out windowBorder);
        }

        return TryMapResizeModeName(resizeMode.ToString(), out windowBorder);
    }

    private static bool TryMapResizeModeValue(
        int resizeMode,
        out ProGpuWpfWindowBorder windowBorder)
    {
        switch (resizeMode)
        {
            case 0:
            case 1:
                windowBorder = ProGpuWpfWindowBorder.Fixed;
                return true;
            case 2:
            case 3:
                windowBorder = ProGpuWpfWindowBorder.Resizable;
                return true;
            default:
                windowBorder = ProGpuWpfWindowBorder.Resizable;
                return false;
        }
    }

    private static bool TryMapResizeModeName(
        string? resizeMode,
        out ProGpuWpfWindowBorder windowBorder)
    {
        switch (resizeMode)
        {
            case "NoResize":
            case "CanMinimize":
                windowBorder = ProGpuWpfWindowBorder.Fixed;
                return true;
            case "CanResize":
            case "CanResizeWithGrip":
                windowBorder = ProGpuWpfWindowBorder.Resizable;
                return true;
            default:
                windowBorder = ProGpuWpfWindowBorder.Resizable;
                return false;
        }
    }

    private static bool IsHiddenWindowStyle(object? windowStyle)
    {
        if (TryConvertEnumNumber(windowStyle, out var value))
        {
            return value == 0;
        }

        return string.Equals(windowStyle?.ToString(), "None", StringComparison.Ordinal);
    }

    private static bool TryConvertEnumNumber(object? value, out int number)
    {
        number = 0;
        if (value == null || value is string)
        {
            return false;
        }

        try
        {
            number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private static bool TryCreateActivation(
        object window,
        Func<object, ProGpuWpfWindowHost>? hostFactory,
        out WpfPortableWindowActivation? activation)
    {
        activation = null;
        ProGpuWpfWindowHost host = hostFactory?.Invoke(window) ??
            new ProGpuWpfWindowHost(CreateHostOptions(window));
        if (TryAttach(host, window, out activation))
        {
            return true;
        }

        host.Dispose();
        return false;
    }

    private static bool TryMapPositiveDimension(object? value, out double mappedValue)
    {
        if (!TryMapFiniteDimension(value, out mappedValue))
        {
            return false;
        }

        return mappedValue > 0.0;
    }

    private static bool TryMapFiniteDimension(object? value, out double mappedValue)
    {
        mappedValue = 0.0;
        if (value == null)
        {
            return false;
        }

        try
        {
            mappedValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }

        return double.IsFinite(mappedValue);
    }

    private static bool TryGetPositiveDimension(bool hasValue, double value, out double mappedValue)
    {
        mappedValue = value;
        return hasValue && double.IsFinite(value) && value > 0.0;
    }

    private static bool TryGetFiniteDimension(bool hasValue, double value, out double mappedValue)
    {
        mappedValue = value;
        return hasValue && double.IsFinite(value);
    }

    private static int ToLogicalClientDimension(double value)
    {
        return Math.Max(1, (int)Math.Ceiling(value));
    }

    private static int ToLogicalPositionDimension(double value)
    {
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
