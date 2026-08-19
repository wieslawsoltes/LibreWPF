using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using ProGPU.Backend;
using ProGPU.DirectX;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using ProGpuPathGeometry = global::ProGPU.Vector.PathGeometry;
using ProGpuPrimitivePathGeometry = global::ProGPU.Vector.PrimitivePathGeometry;
using ProGpuRect = global::ProGPU.Scene.Rect;
using ProGpuRenderTargetViewport = global::ProGPU.Scene.RenderTargetViewport;
using SilkWindowBorder = Silk.NET.Windowing.WindowBorder;
using SilkWindowState = Silk.NET.Windowing.WindowState;

namespace System.Windows.Media.ProGPU;

public unsafe sealed class ProGpuWpfWindowHost : IDisposable
{
    private const string TraceRenderSurfaceEnvironmentVariable = "PROGPU_WPF_TRACE_RENDER_SURFACE";
    private const string TraceInputEnvironmentVariable = "PROGPU_WPF_TRACE_INPUT";
    private const string TraceNativeLoopEnvironmentVariable = "PROGPU_WPF_TRACE_NATIVE_LOOP";
    private const int HitTestOwnerBufferCapacity = 64;
    // A 256-square _NET_WM_ICON plus its CARDINAL header exceeds the legacy
    // X11 core request payload. Keep the portable native icon below that limit.
    private const int MaxWindowIconDimension = 128;
    private static readonly TimeSpan PortableNativeLoopActiveDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan PortableNativeLoopIdleDelay = TimeSpan.FromMilliseconds(16);

    private static readonly bool s_traceRenderSurface = IsTraceEnabled(TraceRenderSurfaceEnvironmentVariable);
    private static readonly bool s_traceInput = IsTraceEnabled(TraceInputEnvironmentVariable);
    private static readonly bool s_traceNativeLoop = IsTraceEnabled(TraceNativeLoopEnvironmentVariable);
    private static readonly object s_nativeActivationGate = new();
    private static readonly object s_deferredNativeWindowDisposalGate = new();
    private static readonly HashSet<ProGpuWpfWindowHost> s_deferredNativeWindowDisposals = new();
    private static WeakReference<ProGpuWpfWindowHost>? s_pendingNativeActivation;
    private static WeakReference<ProGpuWpfWindowHost>? s_requestedNativeActivation;

    private readonly ProGpuWpfWindowOptions _options;
    private IWindow? _window;
    private ProGpuWpfCompositionTarget? _target;
    private ProGpuDirectXDevice? _directXDevice;
    private IDisposable? _inputSubscription;
    private IWpfInputService? _attachedInputService;
    private IDisposable? _dragDropSubscription;
    private IWpfDragDropService? _attachedDragDropService;
    private IDisposable? _windowEventSubscription;
    private IWpfWindowEventService? _attachedWindowEventService;
    private IDisposable? _nativeDpiSubscription;
    private IWpfDispatcherService? _attachedDispatcherService;
    private IWpfPlatformServices _platformServices = CrossPlatformWpfPlatformServices.Instance;
    private IWpfRenderScheduler _wpfRenderScheduler;
    private WpfPortablePresentationSourceBridge? _portablePresentationSourceBridge;
    private readonly List<WpfPortablePopupBridge> _portablePopupBridges = new();
    private readonly WpfPortablePopupService? _portablePopupService;
    private readonly IDisposable? _portablePopupServiceRegistration;
    private object? _wpfRootVisual;
    private double _portablePresentationSourceDpiScaleX = double.NaN;
    private double _portablePresentationSourceDpiScaleY = double.NaN;
    private int _portablePresentationSourceClientWidth = -1;
    private int _portablePresentationSourceClientHeight = -1;
    private int _portablePresentationSourceClientOriginX;
    private int _portablePresentationSourceClientOriginY;
    private bool _hasPortablePresentationSourceClientOrigin;
    private bool _isDisposed;
    private bool _isNativeLoopRunning;
    private bool _usesExternalNativeLoopPump;
    private bool _isLoadingCompositionTarget;
    private bool _disposeNativeWindowWhenLoopExits;
    private bool _hasPresentedFrame;
    private long _presentedFrameCount;
    private bool _ownsRenderScheduler;
    private bool _isRendering;
    private bool _isRenderingLiveResize;
    private bool _isInNativeWindowCloseCallback;
    private bool _isForwardingPlatformInput;
    private bool _isProcessingRenderSchedulerWakeup;
    private bool _isProcessingDispatcherWorkWakeup;
    private bool _forceFullWpfReplay;
    private bool _isHostVisible;
    private bool _hasNativeWindowCloseStarted;
    private bool _dpiWindowHintsConfigured;
    private bool _hasPendingNativeDpiChange;
    private double _nativeWindowContentScaleX = double.NaN;
    private double _nativeWindowContentScaleY = double.NaN;
    private ProGpuWpfWindowState _windowState;
    private string _windowTitle;
    private int _clientWidth;
    private int _clientHeight;
    private int _requestedLogicalClientWidth = -1;
    private int _requestedLogicalClientHeight = -1;
    private int _declaredLogicalClientWidth = -1;
    private int _declaredLogicalClientHeight = -1;
    private int? _windowLeft;
    private int? _windowTop;
    private bool _windowTopmost;
    private ProGpuWpfWindowBorder _windowBorder;
    private byte[]? _windowIconPixels;
    private int _windowIconWidth;
    private int _windowIconHeight;
    private SilkWindowController? _windowController;
    private PortableWindowRegion? _windowRegion;

    internal readonly record struct RenderSurfaceGeometry(
        uint LogicalWidth,
        uint LogicalHeight,
        uint PixelWidth,
        uint PixelHeight,
        double DpiScaleX,
        double DpiScaleY,
        double DpiScale,
        uint ViewportX = 0,
        uint ViewportY = 0,
        uint ViewportWidth = 0,
        uint ViewportHeight = 0);

    public ProGpuWpfWindowHost(ProGpuWpfWindowOptions? options = null)
    {
        _options = options ?? new ProGpuWpfWindowOptions();
        _isHostVisible = _options.IsVisible;
        _windowState = _options.WindowState;
        _windowTitle = _options.Title;
        _clientWidth = Math.Max(1, _options.Width);
        _clientHeight = Math.Max(1, _options.Height);
        _requestedLogicalClientWidth = _clientWidth;
        _requestedLogicalClientHeight = _clientHeight;
        _declaredLogicalClientWidth = _clientWidth;
        _declaredLogicalClientHeight = _clientHeight;
        _windowLeft = _options.Left;
        _windowTop = _options.Top;
        _windowTopmost = _options.Topmost;
        _windowBorder = _options.WindowBorder;
        _wpfRenderScheduler = CreateDefaultRenderScheduler(_platformServices, out _ownsRenderScheduler);
        AttachDispatcherService(_platformServices.Dispatcher);
        AttachRenderScheduler(_wpfRenderScheduler);
        if (!OperatingSystem.IsWindows() && _options.EnablePortablePopupService)
        {
            _portablePopupService = new WpfPortablePopupService(this);
            _portablePopupServiceRegistration = PortableWpfServiceRegistry.RegisterPopupService(_portablePopupService);
        }
    }

    public event EventHandler<ProGpuWpfFrameEventArgs>? Render;

    internal event EventHandler? RenderWakeupRequested;

    internal event EventHandler? UpdateTick;

    public event EventHandler<WpfInputEventArgs>? InputReceived;

    public event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    public event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    public event EventHandler<ProGpuWpfWindowClosingEventArgs>? Closing;

    public IWindow? SilkWindow => _window;

    public ProGpuWpfCompositionTarget? CompositionTarget => _target;

    public ProGpuDirectXDevice? DirectXDevice
    {
        get
        {
            ThrowIfDisposed();
            if (_target == null)
            {
                return null;
            }

            if (_directXDevice is { Context: var context } && ReferenceEquals(context, _target.Context))
            {
                return _directXDevice;
            }

            _directXDevice?.Dispose();
            _directXDevice = ProGpuDirectXDevice.FromContext(
                _target.Context,
                new ProGpuDirectXDeviceOptions
                {
                    Label = "ProGPU WPF DirectX Device",
                    MinimumFeatureLevel = DxFeatureLevel.Direct3D9_3
                });
            return _directXDevice;
        }
    }

    public bool IsVisible => _isHostVisible || (_window?.IsVisible ?? false);

    public ProGpuWpfWindowState WindowState => _windowState;

    public string Title => _window?.Title ?? _windowTitle;

    public int Width => _clientWidth;

    public int Height => _clientHeight;

    public int? Left => _window?.Position.X ?? _windowLeft;

    public int? Top => _window?.Position.Y ?? _windowTop;

    public bool Topmost => _window?.TopMost ?? _windowTopmost;

    public ProGpuWpfWindowBorder WindowBorder => _windowBorder;

    public PortableWindowRegion? WindowRegion => _windowRegion;

    public object? PortablePresentationSource => _portablePresentationSourceBridge?.Source;

    public WpfPortablePresentationSourceBridge? PortablePresentationSourceBridge => _portablePresentationSourceBridge;

    internal WpfCursor? LastPortableCursor { get; private set; }

    public IWpfPlatformServices PlatformServices
    {
        get => _platformServices;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            DetachDispatcherService();
            _platformServices = value;
            AttachDispatcherService(_platformServices.Dispatcher);
            if (_ownsRenderScheduler)
            {
                ReplaceRenderScheduler(
                    CreateDefaultRenderScheduler(_platformServices, out var ownsScheduler),
                    ownsScheduler);
            }
        }
    }

    public object? WpfRootVisual
    {
        get => _wpfRootVisual;
        set
        {
            if (ReferenceEquals(_wpfRootVisual, value))
            {
                return;
            }

            _wpfRootVisual = value;
            InvalidateWpfRootVisualForPresentationSourceGeometryChange();
            RequestRenderAndWakeNativeLoop();
        }
    }

    public IWpfMilResourceResolver? WpfResourceResolver { get; set; }

    public IWpfImageSourceAdapter? WpfImageSourceAdapter { get; set; } = new WpfBitmapSourceImageAdapter();

    public IWpfRenderScheduler WpfRenderScheduler
    {
        get => _wpfRenderScheduler;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ReplaceRenderScheduler(value, ownsScheduler: false);
        }
    }

    public WpfVisualReplayResult LastVisualReplayResult { get; private set; }

    public WpfCompositionDrawingContextResult LastSourceDrawingResult { get; private set; }

    public bool IsWpfRootVisualDirty => _target?.WpfInvalidationTracker.IsDirty ?? false;

    public bool EnableFrameCoalescing { get; set; } = true;

    public bool HasPresentedFrame => Volatile.Read(ref _hasPresentedFrame);

    public long PresentedFrameCount => Interlocked.Read(ref _presentedFrameCount);

    public ProGpuWpfFrameState LastPresentedFrameState { get; private set; }

    internal RenderSurfaceGeometry LastResolvedRenderSurfaceGeometry { get; private set; }

    internal double CurrentDpiScaleX => ResolveCurrentPortableDpiScale(
        LastResolvedRenderSurfaceGeometry.DpiScaleX,
        _portablePresentationSourceDpiScaleX);

    internal double CurrentDpiScaleY => ResolveCurrentPortableDpiScale(
        LastResolvedRenderSurfaceGeometry.DpiScaleY,
        _portablePresentationSourceDpiScaleY);

    public long SkippedFrameCount { get; private set; }

    public long RetainedWpfReplaySkipCount { get; private set; }

    public long RetainedWpfBranchReplayCount { get; private set; }

    internal bool ForceFullWpfReplayForNextFrame => _forceFullWpfReplay;

    internal long RenderSchedulerWakeupCount { get; private set; }

    internal long DispatcherWakeupCount { get; private set; }

    internal long NativeLoopWakeupCount { get; private set; }

    internal long NativeLoopOwnerActivationCount { get; private set; }

    internal long NativeLoopOwnerIterationCount { get; private set; }

    internal long NativeLoopOwnerDoEventsCallCount { get; private set; }

    internal long NativeRenderPumpCount { get; private set; }

    internal long SkippedNativeRenderPumpCount { get; private set; }

    internal bool HasGpuHitTestCache => !_isDisposed && _target?.LastGpuHitTestIndex != null;

    internal bool HasVisibleNativePortablePopup
    {
        get
        {
            for (int i = 0; i < _portablePopupBridges.Count; i++)
            {
                if (_portablePopupBridges[i].IsVisibleNativeWindow)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal void GetPortablePopupDiagnostics(
        out int openCount,
        out int visibleCount,
        out int nativeWindowCount,
        out int presentedNativeWindowCount,
        out int nativeWindowGpuHitTestCount,
        out int nativeWindowGpuHitTestOwnerCount)
    {
        openCount = _portablePopupBridges.Count;
        visibleCount = 0;
        nativeWindowCount = 0;
        presentedNativeWindowCount = 0;
        nativeWindowGpuHitTestCount = 0;
        nativeWindowGpuHitTestOwnerCount = 0;

        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            var popup = _portablePopupBridges[i];
            if (!popup.IsVisible)
            {
                continue;
            }

            visibleCount++;
            if (!popup.IsVisibleNativeWindow)
            {
                continue;
            }

            nativeWindowCount++;
            if (popup.HasPresentedNativeFrame)
            {
                presentedNativeWindowCount++;
            }

            if (popup.HasNativeGpuHitTestCache)
            {
                nativeWindowGpuHitTestCount++;
            }

            nativeWindowGpuHitTestOwnerCount += popup.NativeGpuHitTestOwnerCount;
        }
    }

    internal bool TryHitTestNativePortablePopupOwners(
        double screenDeviceX,
        double screenDeviceY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryHitTestNativeOwners(
                    screenDeviceX,
                    screenDeviceY,
                    owners,
                    out ownerCount))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryQueryNativePortablePopupHitTestBoundsOwners(
        double screenDeviceMinX,
        double screenDeviceMinY,
        double screenDeviceMaxX,
        double screenDeviceMaxY,
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryQueryNativeHitTestBoundsOwners(
                    screenDeviceMinX,
                    screenDeviceMinY,
                    screenDeviceMaxX,
                    screenDeviceMaxY,
                    owners,
                    out ownerCount))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryQueryNativePortablePopupOwners(
        Span<object?> owners,
        out int ownerCount)
    {
        ownerCount = 0;
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryQueryAllNativeHitTestOwners(owners, out ownerCount) &&
                ownerCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryRaiseTopmostNativePortablePopupInputForDiagnostics(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryRaiseNativeInputForDiagnostics(input))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryRaiseTopmostNativePortablePopupLocalInputForDiagnostics(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryRaiseNativeLocalInputForDiagnostics(input))
            {
                return true;
            }
        }

        return false;
    }

    public Action<MediaDrawingContext, ProGpuWpfFrameEventArgs>? Draw { get; set; }

    public Action<WpfCompositionDrawingContext, ProGpuWpfFrameEventArgs>? WpfDraw { get; set; }

    internal Func<ProGpuWpfDrawingFrame, IWpfImageSourceAdapter?, IDisposable?> RenderDataSinkProviderRegistrationFactory { get; set; } = RegisterDefaultRenderDataSinkProvider;

    public void Run()
    {
        Run(_options.ShowActivated);
    }

    internal void Run(bool showActivated)
    {
        ThrowIfDisposed();
        // Nonactivating native windows must be created hidden. Otherwise the
        // Cocoa/GLFW window can take focus before the platform show policy runs.
        _isHostVisible = showActivated;
        EnsureWindow();
        if (!_window!.IsInitialized)
        {
            _window.Initialize();
        }

        _isHostVisible = true;
        ShowNativeWindow(showActivated);
        _isNativeLoopRunning = true;
        TraceNativeLoop("run entering: " + CreateNativeLoopTraceState());
        try
        {
            RunPortableNativeLoop();
        }
        catch (Exception ex)
        {
            TraceNativeLoop("run failed: " + ex);
            throw;
        }
        finally
        {
            _isNativeLoopRunning = false;
            DisposeDeferredNativeWindowIfNeeded();
            TraceNativeLoop("run leaving: " + CreateNativeLoopTraceState());
        }
    }

    private void RunPortableNativeLoop()
    {
        if (!ShouldKeepPortableNativeRunLoopAlive())
        {
            TraceNativeLoop("owner loop skipped: " + CreateNativeLoopTraceState());
            return;
        }

        NativeLoopOwnerActivationCount++;
        TraceNativeLoop("owner loop entering: " + CreateNativeLoopTraceState());
        while (ShouldKeepPortableNativeRunLoopAlive())
        {
            var hadPendingRender = WpfRenderScheduler.HasPendingRenderRequest;
            NativeLoopOwnerDoEventsCallCount++;
            try
            {
                ApplyPendingNativeActivation(consume: false);
                DoEvents();
                ApplyPendingNativeActivation(consume: true);
            }
            catch (ObjectDisposedException ex) when (!ShouldKeepPortableNativeRunLoopAlive())
            {
                TraceNativeLoop("owner loop close/dispose exit after ObjectDisposedException: " + ex.ObjectName);
                return;
            }
            catch (ObjectDisposedException ex)
            {
                TraceNativeLoop("owner loop unexpected ObjectDisposedException: " + ex);
                throw;
            }
            catch (InvalidOperationException ex) when (!ShouldKeepPortableNativeRunLoopAlive())
            {
                TraceNativeLoop("owner loop close/dispose exit after InvalidOperationException: " + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                TraceNativeLoop("owner loop unexpected exception: " + ex);
                throw;
            }

            NativeLoopOwnerIterationCount++;
            if (!ShouldKeepPortableNativeRunLoopAlive())
            {
                TraceNativeLoop("owner loop stopping after DoEvents: " + CreateNativeLoopTraceState());
                return;
            }

            Thread.Sleep(hadPendingRender || WpfRenderScheduler.HasPendingRenderRequest
                ? PortableNativeLoopActiveDelay
                : PortableNativeLoopIdleDelay);
        }

        TraceNativeLoop("owner loop leaving: " + CreateNativeLoopTraceState());
    }

    private bool ShouldKeepPortableNativeRunLoopAlive()
    {
        var window = _window;
        return !_isDisposed &&
            !_hasNativeWindowCloseStarted &&
            window != null;
    }

    public void Initialize()
    {
        ThrowIfDisposed();
        ShowCore(requestRenderWhenInitialized: false);
    }

    internal void InitializeHidden()
    {
        ThrowIfDisposed();
        _isHostVisible = false;
        EnsureWindow();
        _window!.IsVisible = false;
        if (!_window.IsInitialized)
        {
            _window.Initialize();
        }
    }

    public void Show()
    {
        ThrowIfDisposed();
        ShowCore(requestRenderWhenInitialized: true);
    }

    internal bool TryActivate()
    {
        ThrowIfDisposed();
        EnsureWindow();
        bool activated = PlatformServices.WindowDecorations.TryActivate(_window!);
        if (activated)
        {
            QueuePendingNativeActivation(this);
            RequestRenderAndWakeNativeLoop();
        }

        TraceNativeLoop("native activation requested: accepted=" + activated + ", " + CreateNativeLoopTraceState());
        return activated;
    }

    private static void QueuePendingNativeActivation(ProGpuWpfWindowHost host)
    {
        lock (s_nativeActivationGate)
        {
            var request = new WeakReference<ProGpuWpfWindowHost>(host);
            s_pendingNativeActivation = request;
            s_requestedNativeActivation = request;
        }
    }

    internal bool HasRequestedNativeActivationForAnotherHost()
    {
        lock (s_nativeActivationGate)
        {
            return s_requestedNativeActivation != null &&
                s_requestedNativeActivation.TryGetTarget(out ProGpuWpfWindowHost? requestedHost) &&
                !ReferenceEquals(requestedHost, this) &&
                !requestedHost._isDisposed &&
                !requestedHost._hasNativeWindowCloseStarted;
        }
    }

    private static void ApplyPendingNativeActivation(bool consume)
    {
        WeakReference<ProGpuWpfWindowHost>? pending;
        lock (s_nativeActivationGate)
        {
            pending = s_pendingNativeActivation;
            if (consume)
            {
                s_pendingNativeActivation = null;
            }
        }

        if (pending == null ||
            !pending.TryGetTarget(out ProGpuWpfWindowHost? host) ||
            host._isDisposed ||
            host._hasNativeWindowCloseStarted ||
            !host._isHostVisible ||
            host._window == null)
        {
            if (consume && pending != null)
            {
                ClearRequestedNativeActivation(pending);
            }

            return;
        }

        bool activated = host.PlatformServices.WindowDecorations.TryActivate(host._window);
        if (activated &&
            !host._isDisposed &&
            !host._hasNativeWindowCloseStarted)
        {
            // The shared GLFW poll can still contain the owner's delayed first-show
            // activation. Drain the requested window's native focus event before the
            // WPF dispatcher resumes and observes IsActive.
            try
            {
                host._window.DoEvents();
            }
            finally
            {
                ProcessDeferredNativeWindowDisposals();
            }
        }

        host.TraceNativeLoop(
            "deferred native activation requested: accepted=" + activated + ", " +
            host.CreateNativeLoopTraceState());
        if (consume)
        {
            ClearRequestedNativeActivation(pending);
        }
    }

    private static void ClearRequestedNativeActivation(
        WeakReference<ProGpuWpfWindowHost> request)
    {
        lock (s_nativeActivationGate)
        {
            if (ReferenceEquals(s_requestedNativeActivation, request))
            {
                s_requestedNativeActivation = null;
            }
        }
    }

    private static void ClearNativeActivationForHost(ProGpuWpfWindowHost host)
    {
        lock (s_nativeActivationGate)
        {
            if (s_pendingNativeActivation != null &&
                s_pendingNativeActivation.TryGetTarget(out ProGpuWpfWindowHost? pendingHost) &&
                ReferenceEquals(pendingHost, host))
            {
                s_pendingNativeActivation = null;
            }

            if (s_requestedNativeActivation != null &&
                s_requestedNativeActivation.TryGetTarget(out ProGpuWpfWindowHost? requestedHost) &&
                ReferenceEquals(requestedHost, host))
            {
                s_requestedNativeActivation = null;
            }
        }
    }

    internal void ShowWithoutActivation()
    {
        ThrowIfDisposed();
        // Keep WindowOptions.IsVisible false through native creation, then let
        // the platform service update Silk's visibility state with focus-on-show disabled.
        _isHostVisible = false;
        EnsureWindow();
        if (!_window!.IsInitialized)
        {
            _window.Initialize();
        }

        _isHostVisible = true;
        ShowNativeWindow(showActivated: false);

        RequestRenderAndWakeNativeLoop();
    }

    private void ShowNativeWindow(bool showActivated)
    {
        if (showActivated ||
            !PlatformServices.WindowDecorations.TryShowWithoutActivation(_window!))
        {
            _window!.IsVisible = true;
        }
    }

    internal void DeferShowUntilRun()
    {
        ThrowIfDisposed();
        _isHostVisible = true;
        if (_window != null)
        {
            _window.IsVisible = false;
        }
    }

    public void Hide()
    {
        ThrowIfDisposed();

        _isHostVisible = false;
        if (_window != null)
        {
            _window.IsVisible = false;
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetWindowState(ProGpuWpfWindowState windowState)
    {
        ThrowIfDisposed();

        _windowState = windowState;
        if (_window != null)
        {
            _window.WindowState = ToSilkWindowState(windowState);
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetTitle(string title)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(title);

        _windowTitle = title;
        if (_window != null)
        {
            _window.Title = _windowTitle;
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetIcon(object? iconSource)
    {
        ThrowIfDisposed();

        if (iconSource == null)
        {
            _windowIconPixels = null;
            _windowIconWidth = 0;
            _windowIconHeight = 0;
            ApplyWindowIcon();
            return;
        }

        if (!WpfBitmapSourceImageAdapter.TryCopyPixelsAsRgba32(
                iconSource,
                MaxWindowIconDimension,
                out var pixels,
                out var width,
                out var height))
        {
            return;
        }

        _windowIconPixels = pixels;
        _windowIconWidth = width;
        _windowIconHeight = height;
        ApplyWindowIcon();
    }

    private void ApplyWindowIcon()
    {
        if (_window?.IsInitialized != true)
        {
            return;
        }

        if (_windowIconPixels != null && _windowIconWidth > 0 && _windowIconHeight > 0)
        {
            _window.SetWindowIcon([
                new RawImage(_windowIconWidth, _windowIconHeight, _windowIconPixels)
            ]);
        }
        else
        {
            _window.SetWindowIcon([]);
        }
    }

    public void SetClientSize(int width, int height)
    {
        ThrowIfDisposed();
        SetClientSizeCore(width, height, updatePortablePresentationSource: true);
    }

    public void SetPosition(int left, int top)
    {
        ThrowIfDisposed();

        _windowLeft = left;
        _windowTop = top;
        if (_window != null)
        {
            _window.Position = new Vector2D<int>(left, top);
        }

        UpdatePortablePresentationSourceClientOrigin(left, top);

        RequestRenderAndWakeNativeLoop();
    }

    public void SetTopmost(bool topmost)
    {
        ThrowIfDisposed();

        _windowTopmost = topmost;
        if (_window != null)
        {
            _window.TopMost = topmost;
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void SetWindowBorder(ProGpuWpfWindowBorder windowBorder)
    {
        ThrowIfDisposed();

        _windowBorder = windowBorder;
        if (_window != null)
        {
            _window.WindowBorder = ToSilkWindowBorder(windowBorder);
        }

        ApplyWindowBorderToController();

        RequestRenderAndWakeNativeLoop();
    }

    public void SetWindowRegion(PortableWindowRegion region)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(region);

        _windowRegion = region.IsEmpty ? null : region;
        ApplyWindowRegionToCompositionTarget();
        RequestRenderAndWakeNativeLoop();
    }

    private void ApplyWindowRegionToCompositionTarget()
    {
        if (_target == null)
        {
            return;
        }

        _target.SceneRootVisual.GeometryClip = TryCreateWindowRegionClip(_windowRegion, out var clip)
            ? clip
            : null;
    }

    internal static bool TryCreateWindowRegionClip(
        PortableWindowRegion? region,
        out ProGpuPathGeometry? clip)
    {
        clip = null;
        if (region == null || region.IsEmpty || !TryToSceneRect(region.Bounds, out var bounds))
        {
            return false;
        }

        clip = ProGpuPrimitivePathGeometry.CreateRectangle(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);

        var excludedRects = region.ExcludedRects;
        for (int i = 0; i < excludedRects.Count; i++)
        {
            if (!TryToSceneRect(excludedRects[i], out var excluded) ||
                !TryIntersect(bounds, excluded, out var clippedExcluded))
            {
                continue;
            }

            clip = new ProGpuPathGeometry
            {
                IsCombined = true,
                PathA = clip,
                PathB = ProGpuPrimitivePathGeometry.CreateRectangle(
                    clippedExcluded.X,
                    clippedExcluded.Y,
                    clippedExcluded.Width,
                    clippedExcluded.Height),
                Op = 0
            };
        }

        return true;
    }

    private static bool TryToSceneRect(PortableRect rect, out ProGpuRect sceneRect)
    {
        if (rect.IsEmpty ||
            !double.IsFinite(rect.X) ||
            !double.IsFinite(rect.Y) ||
            !double.IsFinite(rect.Width) ||
            !double.IsFinite(rect.Height) ||
            rect.Width <= 0 ||
            rect.Height <= 0)
        {
            sceneRect = default;
            return false;
        }

        sceneRect = new ProGpuRect(
            (float)rect.X,
            (float)rect.Y,
            (float)rect.Width,
            (float)rect.Height);
        return float.IsFinite(sceneRect.X) &&
               float.IsFinite(sceneRect.Y) &&
               float.IsFinite(sceneRect.Width) &&
               float.IsFinite(sceneRect.Height) &&
               sceneRect.Width > 0 &&
               sceneRect.Height > 0;
    }

    private static bool TryIntersect(ProGpuRect left, ProGpuRect right, out ProGpuRect intersection)
    {
        float x1 = Math.Max(left.X, right.X);
        float y1 = Math.Max(left.Y, right.Y);
        float x2 = Math.Min(left.Right, right.Right);
        float y2 = Math.Min(left.Bottom, right.Bottom);
        if (x2 <= x1 || y2 <= y1)
        {
            intersection = default;
            return false;
        }

        intersection = new ProGpuRect(x1, y1, x2 - x1, y2 - y1);
        return true;
    }

    internal void SetInitialClientSize(int width, int height)
    {
        ThrowIfDisposed();
        SetClientSizeCore(width, height, updatePortablePresentationSource: false);
    }

    private void SetClientSizeCore(int width, int height, bool updatePortablePresentationSource)
    {
        _clientWidth = Math.Max(1, width);
        _clientHeight = Math.Max(1, height);
        _requestedLogicalClientWidth = _clientWidth;
        _requestedLogicalClientHeight = _clientHeight;
        _declaredLogicalClientWidth = _clientWidth;
        _declaredLogicalClientHeight = _clientHeight;
        if (_window != null)
        {
            _window.Size = ResolveNativeWindowSizeForLogicalClientSize(
                new Vector2D<int>(_clientWidth, _clientHeight),
                ResolveCurrentWindowContentScale(),
                UsesMonitorScaledWindowCoordinates());
        }

        if (updatePortablePresentationSource)
        {
            UpdatePortablePresentationSourceClientSize((uint)_clientWidth, (uint)_clientHeight);
        }

        RequestRenderAndWakeNativeLoop();
    }

    public void DoEvents()
    {
        ThrowIfDisposed();
        ProcessDispatcherQueueCore();
        EnsureWindow();
        IWindow window = _window!;
        if (!window.IsInitialized)
        {
            window.Initialize();
        }

        if (!ShouldKeepPortableNativeRunLoopAlive())
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        if (!EnsureCompositionTargetLoaded() || !ShouldKeepPortableNativeRunLoopAlive())
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        if (ShouldPumpExternalNativeRenderBeforeEvents(
                _usesExternalNativeLoopPump,
                ShouldPumpNativeRender()))
        {
            NativeRenderPumpCount++;
            window.DoRender();
        }

        try
        {
            window.DoEvents();
        }
        finally
        {
            ProcessDeferredNativeWindowDisposals();
        }

        if (!ShouldKeepPortableNativeRunLoopAlive())
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        window.DoUpdate();
        EnsureCompositionTargetLoaded();
        if (ShouldPumpNativeRender())
        {
            NativeRenderPumpCount++;
            window.DoRender();
        }
        else
        {
            SkippedNativeRenderPumpCount++;
        }

        DisposeDeferredNativeWindowIfNeeded();
        if (_isDisposed)
        {
            return;
        }

        ProcessDispatcherQueueCore();
    }

    public void Close()
    {
        if (_window == null)
        {
            return;
        }

        RequestNativeWindowClose(_window);
    }

    public bool SetCursor(WpfCursor cursor)
    {
        ThrowIfDisposed();

        return SetCursorCore(cursor);
    }

    internal bool ApplyPortableCursor(WpfCursor cursor)
    {
        ThrowIfDisposed();

        LastPortableCursor = cursor;
        return SetCursorCore(cursor);
    }

    private bool SetCursorCore(WpfCursor cursor)
    {
        if (_window == null)
        {
            return false;
        }

        if (_attachedInputService is ISilkNetWpfInputContextProvider inputContextProvider &&
            inputContextProvider.TryGetInputContext(_window, out var inputContext))
        {
            return PlatformServices.Cursors.SetCursor(inputContext, cursor);
        }

        return PlatformServices.Cursors.SetCursor(_window, cursor);
    }

    public bool TryBeginDragMove()
    {
        ThrowIfDisposed();

        return _window != null && PlatformServices.WindowDecorations.TryBeginDragMove(_window);
    }

    public bool ProcessDispatcherQueue()
    {
        ThrowIfDisposed();
        return ProcessDispatcherQueueCore();
    }

    public bool TryCreatePortablePresentationSource(
        object? rootVisual = null,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0)
    {
        ThrowIfDisposed();

        if (!WpfPortablePresentationSourceBridge.TryCreate(
                this,
                dpiScaleX,
                dpiScaleY,
                out WpfPortablePresentationSourceBridge? bridge))
        {
            return false;
        }

        AttachPortablePresentationSourceBridge(bridge!, dpiScaleX, dpiScaleY);
        if (rootVisual != null)
        {
            bridge!.RootVisual = rootVisual;
        }

        return true;
    }

    public bool TryBindPortablePresentationSource(object presentationSource)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(presentationSource);

        if (!WpfPortablePresentationSourceBridge.TryBind(
                this,
                presentationSource,
                out WpfPortablePresentationSourceBridge? bridge))
        {
            return false;
        }

        AttachPortablePresentationSourceBridge(bridge!, double.NaN, double.NaN);
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ClearNativeActivationForHost(this);

        IWindow? window = _window;
        bool deferNativeWindowDispose = window != null &&
            (_isNativeLoopRunning ||
                _isRendering ||
                _isProcessingDispatcherWorkWakeup ||
                _isInNativeWindowCloseCallback);
        bool disposeNativeWindow = window != null && !deferNativeWindowDispose;

        if (window != null && !deferNativeWindowDispose)
        {
            DetachNativeDpiService();
            window.Load -= OnLoad;
            window.Update -= OnUpdate;
            window.Render -= OnRender;
            window.Resize -= OnResize;
            window.FramebufferResize -= OnFramebufferResize;
            window.Closing -= OnClosing;
        }
        else if (deferNativeWindowDispose)
        {
            _disposeNativeWindowWhenLoopExits = true;
            RequestNativeWindowClose(window!);
            if (_isInNativeWindowCloseCallback)
            {
                QueueDeferredNativeWindowDisposal(this);
            }
        }

        DetachInputService();
        DetachDragDropService();
        DetachWindowEventService();
        DetachDispatcherService();
        DisposePortablePopupService();
        DisposePortablePresentationSourceBridge();
        DisposeTarget();
        _windowController?.Dispose();
        _windowController = null;
        if (disposeNativeWindow)
        {
            window!.Dispose();
        }

        DetachRenderScheduler(_wpfRenderScheduler);
        DisposeOwnedRenderScheduler();

        _target = null;
        if (!deferNativeWindowDispose)
        {
            _window = null;
        }
    }

    private void DisposeDeferredNativeWindowIfNeeded()
    {
        if (!_disposeNativeWindowWhenLoopExits || _isNativeLoopRunning)
        {
            return;
        }

        _disposeNativeWindowWhenLoopExits = false;
        IWindow? window = _window;
        if (window == null)
        {
            return;
        }

        window.Load -= OnLoad;
        window.Update -= OnUpdate;
        window.Render -= OnRender;
        window.Resize -= OnResize;
        window.FramebufferResize -= OnFramebufferResize;
        window.Closing -= OnClosing;
        DetachNativeDpiService();
        window.Dispose();
        _window = null;
    }

    private static void QueueDeferredNativeWindowDisposal(ProGpuWpfWindowHost host)
    {
        lock (s_deferredNativeWindowDisposalGate)
        {
            s_deferredNativeWindowDisposals.Add(host);
        }
    }

    private static void ProcessDeferredNativeWindowDisposals()
    {
        ProGpuWpfWindowHost[] pending;
        lock (s_deferredNativeWindowDisposalGate)
        {
            if (s_deferredNativeWindowDisposals.Count == 0)
            {
                return;
            }

            pending = [.. s_deferredNativeWindowDisposals];
            s_deferredNativeWindowDisposals.Clear();
        }

        foreach (ProGpuWpfWindowHost host in pending)
        {
            host.DisposeDeferredNativeWindowIfNeeded();
        }
    }

    private void RequestNativeWindowClose(IWindow window)
    {
        bool closeAlreadyStarted = _hasNativeWindowCloseStarted;
        _hasNativeWindowCloseStarted = true;
        TraceNativeLoop((closeAlreadyStarted ? "close request already pending: " : "close requested: ") + CreateNativeLoopTraceState());
        if (closeAlreadyStarted)
        {
            return;
        }

        window.Close();
        TryRequestNativeLoopWakeup(window.ContinueEvents);
    }

    private void EnsureWindow()
    {
        if (_window != null)
        {
            return;
        }

        SilkNetGlfwPlatformSelector.ConfigureBeforeFirstGlfwUse();
        var windowOptions = WindowOptions.Default;
        // GLFW's X11 backend always selects the default (usually opaque)
        // visual for GLFW_NO_API windows. Requesting a client API makes GLFW
        // choose an XRender/GLX framebuffer configuration with an alpha
        // channel when TransparentFramebuffer is enabled. WebGPU still owns
        // presentation; the unused client context is never swapped.
        windowOptions.API =
            SilkNetGlfwPlatformSelector.RequiresClientApiForTransparentFramebuffer(
                _options.TransparentFramebuffer)
                ? GraphicsAPI.Default
                : GraphicsAPI.None;
        windowOptions.ShouldSwapAutomatically = false;
        windowOptions.Size = new Vector2D<int>(_clientWidth, _clientHeight);
        windowOptions.Title = _windowTitle;
        windowOptions.VSync = _options.VSync;
        windowOptions.IsEventDriven = _options.IsEventDriven;
        windowOptions.IsVisible = _isHostVisible;
        windowOptions.WindowState = ToSilkWindowState(_windowState);
        windowOptions.TopMost = _windowTopmost;
        windowOptions.WindowBorder = ToSilkWindowBorder(_windowBorder);
        windowOptions.TransparentFramebuffer = _options.TransparentFramebuffer;
        if (_windowLeft.HasValue && _windowTop.HasValue)
        {
            windowOptions.Position = new Vector2D<int>(_windowLeft.Value, _windowTop.Value);
        }

        _window = Window.Create(windowOptions);
        _dpiWindowHintsConfigured = SilkNetGlfwDpiService.TryConfigureDpiWindowHints();
        _windowController = new SilkWindowController(_window);
        ApplyWindowBorderToController();
        _hasNativeWindowCloseStarted = false;
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
    }

    private void OnLoad()
    {
        AttachNativeDpiService();
        _windowController?.Attach();
        ApplyWindowIcon();
        EnsureCompositionTargetLoaded();
    }

    private bool EnsureCompositionTargetLoaded()
    {
        if (_isDisposed || _hasNativeWindowCloseStarted)
        {
            return false;
        }

        if (_isLoadingCompositionTarget)
        {
            TraceNativeLoop("composition target load deferred during reentrant initialization");
            return false;
        }

        if (_target != null)
        {
            return true;
        }

        if (_window == null)
        {
            return false;
        }

        if (!CanCreateNativeRenderSurface(_window))
        {
            return false;
        }

        _isLoadingCompositionTarget = true;
        try
        {
            IWindow window = _window;
            ProGpuWpfCompositionTarget target = ProGpuWpfCompositionTarget.CreateForWindow(
                window,
                _options.SharedRenderDeviceContext,
                _options.CompositorOptions);
            if (_options.TransparentFramebuffer)
            {
                target.Compositor.ClearColor = System.Numerics.Vector4.Zero;
            }

            if (_isDisposed || _hasNativeWindowCloseStarted || !ReferenceEquals(window, _window))
            {
                target.Dispose();
                return false;
            }

            _target = target;
            target.RenderInvalidated += OnCompositionTargetRenderInvalidated;
            target.Context.VSync = _options.VSync;
            ApplyWindowRegionToCompositionTarget();
            if (!CanFinishCompositionTargetLoad(target, window))
            {
                DisposeTarget();
                return false;
            }

            AttachInputService();
            if (!CanFinishCompositionTargetLoad(target, window))
            {
                DisposeTarget();
                return false;
            }

            AttachDragDropService();
            AttachWindowEventService();
            if (!CanFinishCompositionTargetLoad(target, window))
            {
                DisposeTarget();
                return false;
            }

            SynchronizePortablePresentationSourceGeometry();
            if (Left is int nativeLogicalLeft && Top is int nativeLogicalTop)
            {
                UpdatePortablePresentationSourceClientOrigin(nativeLogicalLeft, nativeLogicalTop);
            }
            RequestRenderAndWakeNativeLoop();
            return true;
        }
        catch
        {
            DisposeTarget();
            throw;
        }
        finally
        {
            _isLoadingCompositionTarget = false;
        }
    }

    private bool CanFinishCompositionTargetLoad(ProGpuWpfCompositionTarget target, IWindow window)
    {
        return !_isDisposed &&
            !_hasNativeWindowCloseStarted &&
            ReferenceEquals(window, _window) &&
            ReferenceEquals(target, _target);
    }

    private static bool CanCreateNativeRenderSurface(IWindow window)
    {
        if (window is not IView view || view.Handle == IntPtr.Zero)
        {
            return false;
        }

        return window is INativeWindowSource { Native: not null };
    }

    private void OnResize(Vector2D<int> size)
    {
        if (_window == null)
        {
            UpdateClientSizeFromNativeResize(size);
        }
        else
        {
            var framebufferSize = _window.FramebufferSize;
            WpfDeviceScale contentScale = ResolveCurrentWindowContentScale();
            UpdateClientSizeFromNativeResize(
                size,
                framebufferSize,
                contentScale,
                UsesMonitorScaledWindowCoordinates());
        }

        if (_target == null || _window == null)
        {
            RequestRenderAndWakeNativeLoop();
            return;
        }

        var geometry = ResolveCurrentRenderSurfaceGeometry();
        SynchronizePortablePresentationSourceGeometry(geometry);
        if (!_target.Context.TryConfigureSwapChain(
                geometry.PixelWidth,
                geometry.PixelHeight))
        {
            RequestRenderAndWakeNativeLoop();
            return;
        }
        _target.SceneRootVisual.Invalidate();
        _target.RootVisual.Invalidate();
        RequestRenderAndWakeNativeLoop();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (size.X <= 0 || size.Y <= 0 || _window == null || _isDisposed || _isRenderingLiveResize)
        {
            return;
        }

        _isRenderingLiveResize = true;
        try
        {
            // Native resize tracking can keep Silk.NET's owner event loop inside
            // DoEvents until the drag completes. Render synchronously from the
            // framebuffer callback so layout and the swap chain follow every step.
            OnResize(_window.Size);
            OnRender(0d);
        }
        finally
        {
            _isRenderingLiveResize = false;
        }
    }

    private void AttachNativeDpiService()
    {
        DetachNativeDpiService();
        if (_window == null || _isDisposed)
        {
            return;
        }

        if (SilkNetGlfwDpiService.TryGetWindowContentScale(_window, out WpfDeviceScale scale))
        {
            CacheNativeWindowContentScale(scale);
        }

        _nativeDpiSubscription = SilkNetGlfwDpiService.TrySubscribeToWindowContentScale(
            _window,
            OnNativeWindowContentScaleChanged);
    }

    private void DetachNativeDpiService()
    {
        _nativeDpiSubscription?.Dispose();
        _nativeDpiSubscription = null;
        _hasPendingNativeDpiChange = false;
        _nativeWindowContentScaleX = double.NaN;
        _nativeWindowContentScaleY = double.NaN;
    }

    private void OnNativeWindowContentScaleChanged(WpfDeviceScale scale)
    {
        if (_isDisposed)
        {
            return;
        }

        CacheNativeWindowContentScale(scale);
        _hasPendingNativeDpiChange = true;
        RequestRenderAndWakeNativeLoop();
    }

    private void CacheNativeWindowContentScale(WpfDeviceScale scale)
    {
        _nativeWindowContentScaleX = scale.X;
        _nativeWindowContentScaleY = scale.Y;
    }

    private void ProcessPendingNativeDpiChange()
    {
        if (!_hasPendingNativeDpiChange || _window == null || _isDisposed)
        {
            return;
        }

        _hasPendingNativeDpiChange = false;
        OnResize(_window.Size);
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_isDisposed)
        {
            DisposeDeferredNativeWindowIfNeeded();
            return;
        }

        ProcessPendingNativeDpiChange();
        TryProcessDispatcherWorkWakeup();
        UpdateTick?.Invoke(this, EventArgs.Empty);
        DisposeDeferredNativeWindowIfNeeded();
    }

    private void OnRender(double deltaSeconds)
    {
        if (_isRendering)
        {
            return;
        }

        _isRendering = true;
        try
        {
            if (_isDisposed)
            {
                return;
            }

            if (_target == null || _window == null || _target.Context.Surface == null)
            {
                ProcessDispatcherQueueCore();
                return;
            }

            var geometry = ResolveCurrentRenderSurfaceGeometry();
            SynchronizePortablePresentationSourceGeometry(geometry);
            ProcessDispatcherQueueCore();

            if (_target == null || _window == null || _target.Context.Surface == null)
            {
                return;
            }

            geometry = ResolveCurrentRenderSurfaceGeometry();
            SynchronizePortablePresentationSourceGeometry(geometry);
            var pixelWidth = geometry.PixelWidth;
            var pixelHeight = geometry.PixelHeight;
            var logicalWidth = geometry.LogicalWidth;
            var logicalHeight = geometry.LogicalHeight;
            var dpiScaleX = geometry.DpiScaleX;
            var dpiScaleY = geometry.DpiScaleY;
            var dpiScale = geometry.DpiScale;
            var viewportX = geometry.ViewportX;
            var viewportY = geometry.ViewportY;
            var viewportWidth = ResolveGeometryViewportDimension(geometry.ViewportWidth, pixelWidth);
            var viewportHeight = ResolveGeometryViewportDimension(geometry.ViewportHeight, pixelHeight);
            _target.DetectWpfSourceChanges();
            var frameState = CaptureFrameState(
                _target,
                logicalWidth,
                logicalHeight,
                pixelWidth,
                pixelHeight,
                dpiScale);

            if (!ShouldRenderFrame(frameState))
            {
                SkippedFrameCount++;
                return;
            }

            if (!_target.Context.TryReconfigureIfNeeded(pixelWidth, pixelHeight))
            {
                RequestRenderAndWakeNativeLoop();
                return;
            }

            object? wpfRootVisual = _wpfRootVisual;
            var forceFullWpfReplay = _forceFullWpfReplay;
            var shouldReplayWpfRootVisual = wpfRootVisual != null &&
                (forceFullWpfReplay || _target.ShouldReplayVisualSubtree(wpfRootVisual));
            var activeWpfImageSourceAdapter = _target.CreateFrameImageSourceAdapter(WpfImageSourceAdapter);
            IReadOnlyList<WpfRetainedVisualBranchReplayTarget> dirtyBranchReplayTargets = Array.Empty<WpfRetainedVisualBranchReplayTarget>();
            var canReplayDirtyWpfBranches = wpfRootVisual != null &&
                shouldReplayWpfRootVisual &&
                !_options.IncludePortablePopupRootsInWpfReplay &&
                !forceFullWpfReplay &&
                _target.TryPrepareDirtyRetainedVisualBranchReplayTargets(
                    wpfRootVisual,
                    activeWpfImageSourceAdapter,
                    out dirtyBranchReplayTargets);
            var clearRetainedWpfVisualRoot = wpfRootVisual == null ||
                (shouldReplayWpfRootVisual && !canReplayDirtyWpfBranches);
            var drawingFrame = _target.BeginDrawingFrame(
                viewportWidth,
                viewportHeight,
                clearRetainedWpfVisualRoot,
                logicalWidth,
                logicalHeight,
                dpiScaleX,
                dpiScaleY);

            using (IDisposable? renderDataSinkProviderRegistration = RegisterRenderDataSinkProvider(drawingFrame, activeWpfImageSourceAdapter))
            {
                var args = new ProGpuWpfFrameEventArgs(
                    drawingContext: null,
                    pixelWidth,
                    pixelHeight,
                    deltaSeconds,
                    dpiScale,
                    drawingFrame);

                if (wpfRootVisual != null)
                {
                    if (shouldReplayWpfRootVisual)
                    {
                        if (canReplayDirtyWpfBranches &&
                            _target.TryReplayDirtyRetainedVisualBranches(
                                wpfRootVisual,
                                drawingFrame,
                                dirtyBranchReplayTargets,
                                WpfResourceResolver,
                                activeWpfImageSourceAdapter,
                                out var branchReplayResult))
                        {
                            LastVisualReplayResult = branchReplayResult;
                            RetainedWpfBranchReplayCount++;
                        }
                        else
                        {
                            using var sink = new ProGpuRetainedCompositionCommandSink(
                                drawingFrame,
                                _target.Context,
                                _target.Viewport3DTextureCache);
                            LastVisualReplayResult = _target.ReplayVisualSubtreeTracked(
                                wpfRootVisual,
                                sink,
                                WpfResourceResolver,
                                activeWpfImageSourceAdapter,
                                _options.IncludePortablePopupRootsInWpfReplay);
                        }
                    }
                    else
                    {
                        RetainedWpfReplaySkipCount++;
                    }

                    _forceFullWpfReplay = false;
                }
                else
                {
                    _target.WpfInvalidationTracker.Detach();
                    LastVisualReplayResult = default;
                    _forceFullWpfReplay = false;
                }

                if (WpfDraw != null)
                {
                    using var sourceDrawingContext = drawingFrame.OpenCompositionDrawingContext(activeWpfImageSourceAdapter);
                    InvokeSourceDraw(sourceDrawingContext, args);
                }
                else
                {
                    LastSourceDrawingResult = default;
                }

                if (_portablePopupBridges.Count > 0)
                {
                    LastVisualReplayResult = AddWpfVisualReplayResults(
                        LastVisualReplayResult,
                        ReplayPortablePopups(
                            _target,
                            drawingFrame,
                            activeWpfImageSourceAdapter));
                }

                if (Draw != null)
                {
                    using var drawingContext = drawingFrame.OpenDrawingContext();
                    var drawArgs = new ProGpuWpfFrameEventArgs(
                        drawingContext,
                        pixelWidth,
                        pixelHeight,
                        deltaSeconds,
                        dpiScale,
                        drawingFrame);
                    Draw.Invoke(drawingContext, drawArgs);
                    Render?.Invoke(this, drawArgs);
                }
                else
                {
                    Render?.Invoke(this, args);
                }

                WpfRenderScheduler.ConsumeRenderRequest();
            }

            if (Present(
                    logicalWidth,
                    logicalHeight,
                    pixelWidth,
                    pixelHeight,
                    viewportX,
                    viewportY,
                    viewportWidth,
                    viewportHeight,
                    dpiScale))
            {
                RecordPresentedFrame(CaptureFrameState(
                    _target,
                    logicalWidth,
                    logicalHeight,
                    pixelWidth,
                    pixelHeight,
                    dpiScale));
                TraceRenderSurfaceGeometryIfRequested(geometry);
            }
        }
        finally
        {
            _isRendering = false;
        }
    }

    private bool Present(
        uint logicalWidth,
        uint logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        uint viewportX,
        uint viewportY,
        uint viewportWidth,
        uint viewportHeight,
        double dpiScale)
    {
        if (_target == null)
        {
            return false;
        }

        var surfaceTexture = new SurfaceTexture();
        _target.Context.Wgpu.SurfaceGetCurrentTexture(_target.Context.Surface, &surfaceTexture);

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
        {
            return false;
        }

        var viewDescriptor = new TextureViewDescriptor
        {
            Format = _target.Context.SwapChainFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        var targetView = _target.Context.Wgpu.TextureCreateView(surfaceTexture.Texture, &viewDescriptor);
        try
        {
            _target.Render(
                logicalWidth,
                logicalHeight,
                pixelWidth,
                pixelHeight,
                new ProGpuRenderTargetViewport(
                    viewportX,
                    viewportY,
                    ResolveGeometryViewportDimension(viewportWidth, pixelWidth),
                    ResolveGeometryViewportDimension(viewportHeight, pixelHeight)),
                (float)dpiScale,
                targetView);
            _target.Context.Wgpu.SurfacePresent(_target.Context.Surface);
            return true;
        }
        finally
        {
            if (targetView != null)
            {
                _target.Context.Wgpu.TextureViewRelease(targetView);
            }
        }
    }

    private static void TraceRenderSurfaceGeometryIfRequested(RenderSurfaceGeometry geometry)
    {
        if (!s_traceRenderSurface)
        {
            return;
        }

        Console.WriteLine(
            "ProGPU WPF render surface: " +
            $"logical {geometry.LogicalWidth}x{geometry.LogicalHeight}, " +
            $"pixels {geometry.PixelWidth}x{geometry.PixelHeight}, " +
            $"viewport {ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth)}x{ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight)}@{geometry.ViewportX},{geometry.ViewportY}, " +
            $"dpi {geometry.DpiScale:0.###}");
    }

    private void TraceNativeLoop(string message)
    {
        if (!s_traceNativeLoop)
        {
            return;
        }

        Console.WriteLine("ProGPU WPF native loop: " + message);
    }

    internal void TraceNativeActivation(string message)
    {
        TraceNativeLoop($"window={Title}, {message}");
    }

    private string CreateNativeLoopTraceState()
    {
        return $"host={GetHashCode():x}, disposed={_isDisposed}, closeStarted={_hasNativeWindowCloseStarted}, " +
            $"hostVisible={_isHostVisible}, hasWindow={_window != null}, " +
            $"ownerActivations={NativeLoopOwnerActivationCount}, ownerDoEvents={NativeLoopOwnerDoEventsCallCount}, " +
            $"ownerIterations={NativeLoopOwnerIterationCount}";
    }

    private WpfVisualReplayResult ReplayPortablePopups(
        ProGpuWpfCompositionTarget target,
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? activeWpfImageSourceAdapter)
    {
        var result = default(WpfVisualReplayResult);
        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            result = AddWpfVisualReplayResults(
                result,
                _portablePopupBridges[i].Replay(
                    target,
                    drawingFrame,
                    WpfResourceResolver,
                    activeWpfImageSourceAdapter));
        }

        return result;
    }

    private static WpfVisualReplayResult AddWpfVisualReplayResults(
        WpfVisualReplayResult left,
        WpfVisualReplayResult right)
    {
        return new WpfVisualReplayResult(
            left.VisualCount + right.VisualCount,
            left.ContentCount + right.ContentCount,
            left.ChildEdgeCount + right.ChildEdgeCount,
            left.UnsupportedContentCount + right.UnsupportedContentCount,
            left.UnsupportedVisualStateCount + right.UnsupportedVisualStateCount,
            new WpfMilDecodeResult(
                left.RenderData.RecordCount + right.RenderData.RecordCount,
                left.RenderData.AppliedCount + right.RenderData.AppliedCount,
                left.RenderData.SkippedCount + right.RenderData.SkippedCount,
                left.RenderData.UnsupportedCount + right.RenderData.UnsupportedCount));
    }

    internal static RenderSurfaceGeometry ResolveRenderSurfaceGeometry(
        int clientWidth,
        int clientHeight,
        Vector2D<int> framebufferSize,
        double monitorDpiScale)
    {
        return ResolveRenderSurfaceGeometry(
            clientWidth,
            clientHeight,
            framebufferSize,
            new WpfDeviceScale(monitorDpiScale, monitorDpiScale));
    }

    private static RenderSurfaceGeometry ResolveRenderSurfaceGeometry(
        int clientWidth,
        int clientHeight,
        Vector2D<int> framebufferSize,
        WpfDeviceScale contentScale)
    {
        var logicalWidth = (uint)Math.Max(1, clientWidth);
        var logicalHeight = (uint)Math.Max(1, clientHeight);
        var fallbackScaleX = NormalizeMonitorDpiScale(contentScale.X);
        var fallbackScaleY = NormalizeMonitorDpiScale(contentScale.Y);
        var pixelWidth = framebufferSize.X > 0
            ? (uint)framebufferSize.X
            : (uint)Math.Max(1, (int)Math.Ceiling(logicalWidth * fallbackScaleX));
        var pixelHeight = framebufferSize.Y > 0
            ? (uint)framebufferSize.Y
            : (uint)Math.Max(1, (int)Math.Ceiling(logicalHeight * fallbackScaleY));

        var dpiScaleX = pixelWidth / (double)logicalWidth;
        var dpiScaleY = pixelHeight / (double)logicalHeight;

        return new RenderSurfaceGeometry(
            logicalWidth,
            logicalHeight,
            pixelWidth,
            pixelHeight,
            dpiScaleX,
            dpiScaleY,
            (dpiScaleX + dpiScaleY) / 2.0,
            ViewportWidth: pixelWidth,
            ViewportHeight: pixelHeight);
    }

    private RenderSurfaceGeometry ResolveCurrentRenderSurfaceGeometry()
    {
        RenderSurfaceGeometry geometry;
        var cachedLogicalClientWidth = GetCachedLogicalClientWidth();
        var cachedLogicalClientHeight = GetCachedLogicalClientHeight();
        if (_window == null)
        {
            geometry = ResolveRenderSurfaceGeometry(
                cachedLogicalClientWidth,
                cachedLogicalClientHeight,
                new Vector2D<int>(cachedLogicalClientWidth, cachedLogicalClientHeight),
                1.0);
            LastResolvedRenderSurfaceGeometry = geometry;
            return geometry;
        }

        var clientSize = _window.Size;
        var framebufferSize = _window.FramebufferSize;
        WpfDeviceScale contentScale = ResolveCurrentWindowContentScale();
        var logicalSize = ResolveLogicalClientSize(
            clientSize,
            framebufferSize,
            cachedLogicalClientWidth,
            cachedLogicalClientHeight,
            contentScale,
            UsesMonitorScaledWindowCoordinates());
        geometry = ResolveRenderSurfaceGeometry(
            logicalSize.X,
            logicalSize.Y,
            framebufferSize,
            contentScale);
        LastResolvedRenderSurfaceGeometry = geometry;
        return geometry;
    }

    internal RenderSurfaceGeometry ResolveCurrentRenderSurfaceGeometryForDiagnostics()
    {
        return ResolveCurrentRenderSurfaceGeometry();
    }

    private static uint ResolveGeometryViewportDimension(uint viewportDimension, uint fallbackPixelDimension)
    {
        return viewportDimension > 0u
            ? viewportDimension
            : Math.Max(1u, fallbackPixelDimension);
    }

    internal bool SynchronizePortablePresentationSourceDpiScale(RenderSurfaceGeometry geometry)
    {
        LastResolvedRenderSurfaceGeometry = geometry;
        return UpdatePortablePresentationSourceDpiScale(geometry.DpiScaleX, geometry.DpiScaleY);
    }

    private bool SynchronizePortablePresentationSourceDpiScale()
    {
        var geometry = ResolveCurrentRenderSurfaceGeometry();
        return SynchronizePortablePresentationSourceDpiScale(geometry);
    }

    internal bool SynchronizePortablePresentationSourceGeometry(RenderSurfaceGeometry geometry)
    {
        LastResolvedRenderSurfaceGeometry = geometry;
        bool dpiScaleChanged = UpdatePortablePresentationSourceDpiScale(geometry.DpiScaleX, geometry.DpiScaleY);
        bool clientSizeChanged = UpdatePortablePresentationSourceClientSize(geometry.LogicalWidth, geometry.LogicalHeight);
        return clientSizeChanged || dpiScaleChanged;
    }

    private bool SynchronizePortablePresentationSourceGeometry()
    {
        var geometry = ResolveCurrentRenderSurfaceGeometry();
        return SynchronizePortablePresentationSourceGeometry(geometry);
    }

    internal bool UpdateClientSizeFromNativeResize(Vector2D<int> size)
    {
        return UpdateClientSizeFromNativeResize(size, size, 1.0);
    }

    internal bool UpdateClientSizeFromNativeResize(
        Vector2D<int> size,
        Vector2D<int> framebufferSize,
        double monitorDpiScale)
    {
        return UpdateClientSizeFromNativeResize(
            size,
            framebufferSize,
            new WpfDeviceScale(monitorDpiScale, monitorDpiScale),
            windowSizeIsScaledByContentScale: false);
    }

    private bool UpdateClientSizeFromNativeResize(
        Vector2D<int> size,
        Vector2D<int> framebufferSize,
        WpfDeviceScale contentScale,
        bool windowSizeIsScaledByContentScale)
    {
        var logicalSize = ResolveLogicalClientSize(
            size,
            framebufferSize,
            GetCachedLogicalClientWidth(),
            GetCachedLogicalClientHeight(),
            contentScale,
            windowSizeIsScaledByContentScale);
        var clientWidth = logicalSize.X;
        var clientHeight = logicalSize.Y;
        if (_clientWidth == clientWidth && _clientHeight == clientHeight)
        {
            return false;
        }

        _clientWidth = clientWidth;
        _clientHeight = clientHeight;
        return true;
    }

    private int GetCachedLogicalClientWidth()
    {
        return ResolveCachedLogicalClientDimension(
            _portablePresentationSourceClientWidth,
            _requestedLogicalClientWidth,
            _declaredLogicalClientWidth,
            _clientWidth);
    }

    private int GetCachedLogicalClientHeight()
    {
        return ResolveCachedLogicalClientDimension(
            _portablePresentationSourceClientHeight,
            _requestedLogicalClientHeight,
            _declaredLogicalClientHeight,
            _clientHeight);
    }

    internal static int ResolveCachedLogicalClientDimension(
        int portablePresentationSourceDimension,
        int requestedLogicalDimension,
        int currentClientDimension)
    {
        return portablePresentationSourceDimension > 0
            ? portablePresentationSourceDimension
            : requestedLogicalDimension > 0
                ? requestedLogicalDimension
                : currentClientDimension;
    }

    private static int ResolveCachedLogicalClientDimension(
        int portablePresentationSourceDimension,
        int requestedLogicalDimension,
        int declaredLogicalDimension,
        int currentClientDimension)
    {
        return portablePresentationSourceDimension > 0
            ? portablePresentationSourceDimension
            : requestedLogicalDimension > 0
                ? requestedLogicalDimension
                : declaredLogicalDimension > 0
                    ? declaredLogicalDimension
                    : currentClientDimension;
    }

    internal static Vector2D<int> ResolveLogicalClientSize(
        Vector2D<int> nativeSize,
        Vector2D<int> framebufferSize,
        int cachedWidth,
        int cachedHeight,
        double monitorDpiScale)
    {
        return ResolveLogicalClientSize(
            nativeSize,
            framebufferSize,
            cachedWidth,
            cachedHeight,
            new WpfDeviceScale(monitorDpiScale, monitorDpiScale),
            windowSizeIsScaledByContentScale: false);
    }

    internal static Vector2D<int> ResolveLogicalClientSize(
        Vector2D<int> nativeSize,
        Vector2D<int> framebufferSize,
        int cachedWidth,
        int cachedHeight,
        WpfDeviceScale contentScale,
        bool windowSizeIsScaledByContentScale)
    {
        return new Vector2D<int>(
            ResolveLogicalClientDimension(
                nativeSize.X,
                framebufferSize.X,
                cachedWidth,
                contentScale.X,
                windowSizeIsScaledByContentScale),
            ResolveLogicalClientDimension(
                nativeSize.Y,
                framebufferSize.Y,
                cachedHeight,
                contentScale.Y,
                windowSizeIsScaledByContentScale));
    }

    internal static Vector2D<int> ResolveNativeWindowSizeForLogicalClientSize(
        Vector2D<int> logicalSize,
        WpfDeviceScale contentScale,
        bool windowSizeIsScaledByContentScale)
    {
        if (!windowSizeIsScaledByContentScale)
        {
            return logicalSize;
        }

        return new Vector2D<int>(
            ScaleLogicalClientDimension(logicalSize.X, contentScale.X),
            ScaleLogicalClientDimension(logicalSize.Y, contentScale.Y));
    }

    private static int ScaleLogicalClientDimension(int logicalDimension, double contentScale)
    {
        double scaledDimension = Math.Max(1, logicalDimension) * NormalizeMonitorDpiScale(contentScale);
        return (int)Math.Clamp(
            Math.Round(scaledDimension, MidpointRounding.AwayFromZero),
            1.0,
            int.MaxValue);
    }

    private static int ResolveLogicalClientDimension(
        int nativeDimension,
        int framebufferDimension,
        int cachedDimension,
        double contentScale,
        bool windowSizeIsScaledByContentScale)
    {
        // Silk.NET normally exposes logical window coordinates independently
        // from framebuffer pixels. GLFW_SCALE_TO_MONITOR is different on X11
        // and Win32: GLFW enlarges the native content area because those
        // platforms map window coordinates to pixels 1:1. Only divide by the
        // authoritative GLFW content scale when that hint is active on one of
        // those backends; ordinary Wayland/macOS and unscaled WSLg sizes remain
        // logical and must not be inferred from a coincidental size ratio.
        if (nativeDimension > 0)
        {
            if (windowSizeIsScaledByContentScale)
            {
                return Math.Max(
                    1,
                    (int)Math.Round(
                        nativeDimension / NormalizeMonitorDpiScale(contentScale),
                        MidpointRounding.AwayFromZero));
            }

            return nativeDimension;
        }

        if (framebufferDimension > 0)
        {
            double dpiScale = NormalizeMonitorDpiScale(contentScale);
            return Math.Max(
                1,
                (int)Math.Round(
                    framebufferDimension / dpiScale,
                    MidpointRounding.AwayFromZero));
        }

        return Math.Max(1, cachedDimension);
    }

    private WpfDeviceScale ResolveCurrentWindowContentScale()
    {
        if (SilkNetGlfwDpiService.TryGetWindowContentScale(_window, out WpfDeviceScale nativeScale))
        {
            CacheNativeWindowContentScale(nativeScale);
            return nativeScale;
        }

        if (SilkNetGlfwDpiService.TryNormalizeContentScale(
                _nativeWindowContentScaleX,
                _nativeWindowContentScaleY,
                out WpfDeviceScale cachedScale))
        {
            return cachedScale;
        }

        double fallbackScale = DisplayScaleResolver.ResolveWindowDisplayScale(
            _window,
            ResolveCurrentMonitorDpiScaleFromPlatformServices());
        return new WpfDeviceScale(fallbackScale, fallbackScale);
    }

    private bool UsesMonitorScaledWindowCoordinates()
    {
        return SilkNetGlfwDpiService.UsesMonitorScaledWindowCoordinates(
            _dpiWindowHintsConfigured,
            _window?.Native?.X11 is not null,
            _window?.Native?.Win32 is not null);
    }

    private double ResolveCurrentMonitorDpiScaleFromPlatformServices()
    {
        try
        {
            var monitors = PlatformServices.Monitors.GetMonitors();
            if (monitors.Count == 0)
            {
                return 1.0;
            }

            foreach (var monitor in monitors)
            {
                if (monitor.IsPrimary)
                {
                    return NormalizeMonitorDpiScale(monitor.DpiScale);
                }
            }

            return NormalizeMonitorDpiScale(monitors[0].DpiScale);
        }
        catch
        {
            return 1.0;
        }
    }

    internal static double ResolveMonitorDpiScaleWithPlatformFallback(
        double monitorDpiScale,
        Func<double?> platformDpiScaleProvider)
    {
        return DisplayScaleResolver.ResolveDisplayScaleWithPlatformFallback(
            monitorDpiScale,
            platformDpiScaleProvider);
    }

    private static double NormalizeMonitorDpiScale(double dpiScale)
    {
        return DisplayScaleResolver.NormalizeDisplayScale(dpiScale);
    }

    private static double ResolveCurrentPortableDpiScale(double geometryDpiScale, double cachedDpiScale)
    {
        if (double.IsFinite(geometryDpiScale) && geometryDpiScale > 0.0)
        {
            return NormalizeMonitorDpiScale(geometryDpiScale);
        }

        if (double.IsFinite(cachedDpiScale) && cachedDpiScale > 0.0)
        {
            return NormalizeMonitorDpiScale(cachedDpiScale);
        }

        return 1.0;
    }

    private void OnClosing()
    {
        _isInNativeWindowCloseCallback = true;
        try
        {
            _hasNativeWindowCloseStarted = true;
            TraceNativeLoop("closing event entering: " + CreateNativeLoopTraceState());
            var args = new ProGpuWpfWindowClosingEventArgs();
            Closing?.Invoke(this, args);
            if (args.Cancel)
            {
                if (_window != null)
                {
                    _window.IsClosing = false;
                }

                _hasNativeWindowCloseStarted = false;
                _isHostVisible = true;
                TraceNativeLoop("closing event canceled: " + CreateNativeLoopTraceState());
                RequestRenderAndWakeNativeLoop();
                return;
            }

            _isHostVisible = false;
            DisposeTarget();
            TraceNativeLoop("closing event accepted: " + CreateNativeLoopTraceState());
        }
        finally
        {
            _isInNativeWindowCloseCallback = false;
        }
    }

    private void OnCompositionTargetRenderInvalidated(object? sender, EventArgs e)
    {
        RequestRenderAndWakeNativeLoop();
    }

    internal bool ShouldRenderFrame(ProGpuWpfFrameState frameState)
    {
        if (!EnableFrameCoalescing)
        {
            return true;
        }

        if (HasExplicitFrameCallbacks)
        {
            return true;
        }

        if (WpfRenderScheduler.HasPendingRenderRequest)
        {
            return true;
        }

        return !HasPresentedFrame || LastPresentedFrameState != frameState;
    }

    internal bool ShouldPumpNativeRender()
    {
        if (_isDisposed ||
            _hasNativeWindowCloseStarted ||
            !_isHostVisible ||
            _windowState == ProGpuWpfWindowState.Minimized ||
            _window is { WindowState: SilkWindowState.Minimized })
        {
            return false;
        }

        return !EnableFrameCoalescing ||
            HasExplicitFrameCallbacks ||
            !HasPresentedFrame ||
            WpfRenderScheduler.HasPendingRenderRequest;
    }

    internal void RecordPresentedFrame(ProGpuWpfFrameState frameState)
    {
        LastPresentedFrameState = frameState;
        Interlocked.Increment(ref _presentedFrameCount);
        Volatile.Write(ref _hasPresentedFrame, true);
    }

    private bool HasExplicitFrameCallbacks => Draw != null || WpfDraw != null || Render != null;

    private static ProGpuWpfFrameState CaptureFrameState(
        ProGpuWpfCompositionTarget target,
        uint logicalWidth,
        uint logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        double dpiScale)
    {
        return new ProGpuWpfFrameState(
            pixelWidth,
            pixelHeight,
            target.SceneChangeVersion,
            target.RetainedWpfChangeVersion,
            target.FlatDrawingChangeVersion,
            target.LastRetainedBranchInvalidationCount,
            target.LastRetainedBranchDirtySourceCount,
            target.LastRetainedBranchMappedSourceCount,
            target.LastRetainedBranchUnmappedSourceCount,
            target.LastRetainedBranchSharedWithCleanSourceVisualCount,
            target.LastRetainedBranchReplayTargetConflictCount,
            target.LastRetainedBranchInvalidationUsedFallback,
            logicalWidth: logicalWidth,
            logicalHeight: logicalHeight,
            dpiScale: dpiScale);
    }

    internal IDisposable? RegisterRenderDataSinkProvider(ProGpuWpfDrawingFrame drawingFrame)
    {
        return RegisterRenderDataSinkProvider(drawingFrame, WpfImageSourceAdapter);
    }

    internal IDisposable? RegisterRenderDataSinkProvider(
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);

        return RenderDataSinkProviderRegistrationFactory(drawingFrame, imageSourceAdapter);
    }

    internal void InvokeSourceDraw(
        WpfCompositionDrawingContext sourceDrawingContext,
        ProGpuWpfFrameEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(sourceDrawingContext);
        ArgumentNullException.ThrowIfNull(args);

        if (WpfDraw == null)
        {
            LastSourceDrawingResult = default;
            return;
        }

        try
        {
            WpfDraw(sourceDrawingContext, args);
        }
        finally
        {
            sourceDrawingContext.Close();
            LastSourceDrawingResult = sourceDrawingContext.Result;
        }
    }

    internal bool TryHitTestOwner(double x, double y, out object? owner)
    {
        owner = null;
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            x < float.MinValue ||
            x > float.MaxValue ||
            y < float.MinValue ||
            y > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryHitTestOwner(
            new System.Numerics.Vector2((float)x, (float)y),
            out owner,
            out _);
    }

    internal bool TryHitTestOwners(double x, double y, out object?[] owners)
    {
        owners = Array.Empty<object?>();
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryHitTestOwners(x, y, ownerBuffer, out int ownerCount))
            {
                return false;
            }

            if (ownerCount == 0)
            {
                return true;
            }

            owners = CopyHitTestResults(ownerBuffer.AsSpan(0, ownerCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    internal bool TryHitTestOwners(double x, double y, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!double.IsFinite(x) ||
            !double.IsFinite(y) ||
            x < float.MinValue ||
            x > float.MaxValue ||
            y < float.MinValue ||
            y > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryHitTestOwners(
            new System.Numerics.Vector2((float)x, (float)y),
            owners,
            out ownerCount,
            out _);
    }

    private ProGpuWpfCompositionTarget? GetGpuHitTestTargetAfterRefresh()
    {
        ProGpuWpfCompositionTarget? target = _target;
        if (_isDisposed || target == null)
        {
            return null;
        }

        if (!_isRendering &&
            !_isForwardingPlatformInput &&
            (target.DetectWpfSourceChanges() ||
                target.WpfInvalidationTracker.IsDirty ||
                WpfRenderScheduler.HasPendingRenderRequest))
        {
            TryProcessRenderSchedulerWakeup();
        }

        target = _target;
        return _isDisposed ? null : target;
    }

    internal bool TryQueryHitTestBoundsOwners(double minX, double minY, double maxX, double maxY, out object?[] owners)
    {
        owners = Array.Empty<object?>();
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryQueryHitTestBoundsOwners(minX, minY, maxX, maxY, ownerBuffer, out int ownerCount))
            {
                return false;
            }

            if (ownerCount == 0)
            {
                return true;
            }

            owners = CopyHitTestResults(ownerBuffer.AsSpan(0, ownerCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    internal bool TryQueryHitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY) ||
            minX < float.MinValue ||
            minX > float.MaxValue ||
            minY < float.MinValue ||
            minY > float.MaxValue ||
            maxX < float.MinValue ||
            maxX > float.MaxValue ||
            maxY < float.MinValue ||
            maxY > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryQueryHitTestBoundsOwners(
            new System.Numerics.Vector2((float)minX, (float)minY),
            new System.Numerics.Vector2((float)maxX, (float)maxY),
            owners,
            out ownerCount,
            out _);
    }

    internal bool TryGetGpuHitTestCacheSnapshot(out ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot snapshot)
    {
        snapshot = default;
        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        var index = target.LastGpuHitTestIndex;
        snapshot = new ProGpuWpfDiagnostics.GpuHitTestCacheSnapshot(
            index is not null,
            target.LastGpuHitTestDeviceIndex is not null,
            index?.Primitives.Count ?? 0,
            index?.Nodes.Count ?? 0,
            index?.PrimitiveIndices.Count ?? 0,
            index?.PathSegments.Count ?? 0,
            target.GpuHitTestOwnerMap.Count);
        return true;
    }

    private static object?[] CopyHitTestResults(ReadOnlySpan<object?> results)
    {
        if (results.IsEmpty)
        {
            return Array.Empty<object?>();
        }

        var copy = new object?[results.Length];
        results.CopyTo(copy);
        return copy;
    }

    internal bool TryQueryHitTestBoundsCandidates(double minX, double minY, double maxX, double maxY, out object?[] candidates)
    {
        candidates = Array.Empty<object?>();
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryQueryHitTestBoundsCandidates(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount))
            {
                return false;
            }

            if (candidateCount == 0)
            {
                return true;
            }

            candidates = CopyHitTestResults(candidateBuffer.AsSpan(0, candidateCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    internal bool TryQueryHitTestBoundsCandidates(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        candidateCount = 0;
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY) ||
            minX < float.MinValue ||
            minX > float.MaxValue ||
            minY < float.MinValue ||
            minY > float.MaxValue ||
            maxX < float.MinValue ||
            maxX > float.MaxValue ||
            maxY < float.MinValue ||
            maxY > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryQueryHitTestBoundsCandidates(
            new System.Numerics.Vector2((float)minX, (float)minY),
            new System.Numerics.Vector2((float)maxX, (float)maxY),
            candidates,
            out candidateCount,
            out _);
    }

    internal bool TryQueryHitTestEllipseCandidates(double minX, double minY, double maxX, double maxY, out object?[] candidates)
    {
        candidates = Array.Empty<object?>();
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!TryQueryHitTestEllipseCandidates(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount))
            {
                return false;
            }

            if (candidateCount == 0)
            {
                return true;
            }

            candidates = CopyHitTestResults(candidateBuffer.AsSpan(0, candidateCount));
            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    internal bool TryQueryHitTestEllipseCandidates(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        candidateCount = 0;
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY) ||
            minX < float.MinValue ||
            minX > float.MaxValue ||
            minY < float.MinValue ||
            minY > float.MaxValue ||
            maxX < float.MinValue ||
            maxX > float.MaxValue ||
            maxY < float.MinValue ||
            maxY > float.MaxValue)
        {
            return false;
        }

        ProGpuWpfCompositionTarget? target = GetGpuHitTestTargetAfterRefresh();
        if (target == null)
        {
            return false;
        }

        return target.TryQueryHitTestEllipseCandidates(
            new System.Numerics.Vector2((float)minX, (float)minY),
            new System.Numerics.Vector2((float)maxX, (float)maxY),
            candidates,
            out candidateCount,
            out _);
    }

    private void AttachInputService()
    {
        if (_window == null || _isDisposed || _hasNativeWindowCloseStarted)
        {
            return;
        }

        IWindow window = _window;
        TraceNativeLoop(
            $"input attach entering: host={GetHashCode():x}, handle={window.Handle}, " +
            $"hadSubscription={_inputSubscription != null}");
        DetachInputService();

        var input = PlatformServices.Input;
        try
        {
            input.InputReceived += OnPlatformInputReceived;
            IDisposable inputSubscription = input.Attach(window);
            if (_isDisposed ||
                _hasNativeWindowCloseStarted ||
                !ReferenceEquals(window, _window))
            {
                inputSubscription.Dispose();
                input.InputReceived -= OnPlatformInputReceived;
                TraceNativeLoop($"input attach canceled after host close: host={GetHashCode():x}, handle={window.Handle}");
                return;
            }

            _inputSubscription = inputSubscription;
            _attachedInputService = input;
            TraceNativeLoop($"input attached: host={GetHashCode():x}, handle={window.Handle}");
        }
        catch (PlatformNotSupportedException)
        {
            input.InputReceived -= OnPlatformInputReceived;
            _inputSubscription = null;
            _attachedInputService = null;
        }
        catch
        {
            input.InputReceived -= OnPlatformInputReceived;
            throw;
        }
    }

    private void DetachInputService()
    {
        IWindow? window = _window;
        bool hadSubscription = _inputSubscription != null;
        if (hadSubscription)
        {
            TraceNativeLoop(
                $"input detach entering: host={GetHashCode():x}, handle={window?.Handle ?? IntPtr.Zero}");
        }

        _inputSubscription?.Dispose();
        _inputSubscription = null;

        if (_attachedInputService != null)
        {
            _attachedInputService.InputReceived -= OnPlatformInputReceived;
            _attachedInputService = null;
        }

        if (hadSubscription && window != null)
        {
            TraceNativeLoop($"input detached: host={GetHashCode():x}, handle={window.Handle}");
        }
    }

    private void OnPlatformInputReceived(object? sender, WpfInputEventArgs e)
    {
        if (_isDisposed ||
            _isInNativeWindowCloseCallback ||
            !IsPlatformEventForCurrentWindow(sender))
        {
            return;
        }

        TraceInputEvent("native", e);
        var input = NormalizeInputEventForCurrentRenderSurface(e, sender != null);
        TraceInputEvent("wpf", input);
        _isForwardingPlatformInput = true;
        try
        {
            InputReceived?.Invoke(this, input);
            if (!ReferenceEquals(input, e))
            {
                e.Handled = input.Handled;
            }
        }
        finally
        {
            _isForwardingPlatformInput = false;
        }

        RequestRenderAndWakeNativeLoop();
    }

    internal bool TryProcessPortablePopupInput(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);

        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            if (_portablePopupBridges[i].TryProcessInput(input))
            {
                return true;
            }
        }

        return false;
    }

    internal void RaiseInputForDiagnostics(WpfInputEventArgs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        OnPlatformInputReceived(null, input);
    }

    private static void TraceInputEvent(string stage, WpfInputEventArgs input)
    {
        if (!s_traceInput)
        {
            return;
        }

        string character = input.Character.HasValue
            ? input.Character.Value.ToString()
            : string.Empty;
        Console.WriteLine(
            "ProGPU WPF input " +
            $"{stage}: {input.Kind}, " +
            $"key '{input.Key ?? string.Empty}', " +
            $"scan {input.ScanCode}, " +
            $"char '{character}', " +
            $"x {input.X:0.###}, y {input.Y:0.###}, " +
            $"delta {input.DeltaX:0.###},{input.DeltaY:0.###}, " +
            $"button {input.Button}, modifiers {input.Modifiers}, handled {input.Handled}");
    }

    private static bool IsTraceEnabled(string environmentVariable)
    {
        return Environment.GetEnvironmentVariable(environmentVariable) == "1";
    }

    private WpfInputEventArgs NormalizeInputEventForCurrentRenderSurface(
        WpfInputEventArgs input,
        bool isNativePlatformEvent)
    {
        if (!IsPointerInput(input.Kind) || _window == null)
        {
            return input;
        }

        var geometry = ResolveCurrentRenderSurfaceGeometry();
        return NormalizeInputEventForRenderSurfaceGeometry(
            input,
            geometry,
            NativeInputCoordinatesArePhysical(
                isNativePlatformEvent,
                UsesMonitorScaledWindowCoordinates(),
                _window.Size,
                geometry,
                input),
            _options.NativePointerCoordinatesAreOwnerRelative);
    }

    internal static bool NativeInputCoordinatesArePhysical(
        bool isNativePlatformEvent,
        bool usesMonitorScaledWindowCoordinates,
        Vector2D<int> nativeSize,
        RenderSurfaceGeometry geometry,
        WpfInputEventArgs input)
    {
        return (isNativePlatformEvent &&
                usesMonitorScaledWindowCoordinates &&
                geometry.DpiScale > 1.0 + double.Epsilon &&
                IsPointerInput(input.Kind)) ||
            NativeInputCoordinatesLookPhysical(nativeSize, geometry, input);
    }

    internal static WpfInputEventArgs NormalizeInputEventForRenderSurfaceGeometry(
        WpfInputEventArgs input,
        RenderSurfaceGeometry geometry,
        bool inputCoordinatesArePhysical,
        bool preserveNativePointerCoordinates = false)
    {
        if (preserveNativePointerCoordinates ||
            !inputCoordinatesArePhysical ||
            !IsPointerInput(input.Kind))
        {
            return input;
        }

        var viewportWidth = ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth);
        var viewportHeight = ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight);
        var scaleX = viewportWidth / (double)Math.Max(1u, geometry.LogicalWidth);
        var scaleY = viewportHeight / (double)Math.Max(1u, geometry.LogicalHeight);
        var normalized = new WpfInputEventArgs(
            input.Kind,
            input.Key,
            input.ScanCode,
            input.Character,
            NormalizeInputCoordinate(input.X, geometry.ViewportX, scaleX),
            NormalizeInputCoordinate(input.Y, geometry.ViewportY, scaleY),
            input.DeltaX,
            input.DeltaY,
            input.Button,
            input.Modifiers)
        {
            Handled = input.Handled
        };
        return normalized;
    }

    internal static bool NativeInputCoordinatesLookPhysical(
        Vector2D<int> nativeSize,
        RenderSurfaceGeometry geometry,
        WpfInputEventArgs input)
    {
        if (!IsPointerInput(input.Kind))
        {
            return false;
        }

        return PointerInputCoordinateExceedsLogicalClient(input, geometry);
    }

    internal static bool NativeWindowSizeLooksPhysical(
        Vector2D<int> nativeSize,
        RenderSurfaceGeometry geometry)
    {
        if (geometry.DpiScale <= 1.0 + double.Epsilon)
        {
            return false;
        }

        var viewportWidth = ResolveGeometryViewportDimension(geometry.ViewportWidth, geometry.PixelWidth);
        var viewportHeight = ResolveGeometryViewportDimension(geometry.ViewportHeight, geometry.PixelHeight);
        var nativeWidth = Math.Abs(nativeSize.X);
        var nativeHeight = Math.Abs(nativeSize.Y);
        if (nativeWidth <= 0 || nativeHeight <= 0)
        {
            return false;
        }

        return NativeDimensionLooksPhysical(nativeWidth, geometry.LogicalWidth, geometry.PixelWidth, viewportWidth) &&
            NativeDimensionLooksPhysical(nativeHeight, geometry.LogicalHeight, geometry.PixelHeight, viewportHeight);
    }

    private static bool NativeDimensionLooksPhysical(
        int nativeDimension,
        uint logicalDimension,
        uint pixelDimension,
        uint viewportDimension)
    {
        if (logicalDimension == 0u ||
            NativeDimensionMatches(nativeDimension, logicalDimension))
        {
            return false;
        }

        return NativeDimensionMatches(nativeDimension, pixelDimension) ||
            NativeDimensionMatches(nativeDimension, viewportDimension);
    }

    private static bool NativeDimensionMatches(int nativeDimension, uint targetDimension)
    {
        return targetDimension > 0u &&
            Math.Abs(nativeDimension - (int)targetDimension) <= 1;
    }

    internal static bool PointerInputCoordinateExceedsLogicalClient(
        WpfInputEventArgs input,
        RenderSurfaceGeometry geometry)
    {
        if (!IsPointerInput(input.Kind))
        {
            return false;
        }

        return PointerCoordinateExceedsLogicalClient(input.X, geometry.LogicalWidth) ||
            PointerCoordinateExceedsLogicalClient(input.Y, geometry.LogicalHeight);
    }

    private static bool PointerCoordinateExceedsLogicalClient(double coordinate, uint logicalDimension)
    {
        if (!double.IsFinite(coordinate) || coordinate < 0.0 || logicalDimension == 0u)
        {
            return false;
        }

        return coordinate > logicalDimension + 1.0;
    }

    private static bool IsPointerInput(WpfInputEventKind kind)
    {
        return kind is WpfInputEventKind.MouseMove or
            WpfInputEventKind.MouseDown or
            WpfInputEventKind.MouseUp or
            WpfInputEventKind.MouseWheel;
    }

    private static double NormalizeInputCoordinate(double coordinate, uint viewportOffset, double scale)
    {
        if (!double.IsFinite(coordinate) || !double.IsFinite(scale) || scale <= 0.0)
        {
            return 0.0;
        }

        return (coordinate - viewportOffset) / scale;
    }

    private void AttachDragDropService()
    {
        if (_window == null)
        {
            return;
        }

        DetachDragDropService();

        var dragDrop = PlatformServices.DragDrop;
        try
        {
            dragDrop.DragDropReceived += OnPlatformDragDropReceived;
            _dragDropSubscription = dragDrop.Attach(_window);
            _attachedDragDropService = dragDrop;
        }
        catch (PlatformNotSupportedException)
        {
            dragDrop.DragDropReceived -= OnPlatformDragDropReceived;
            _dragDropSubscription = null;
            _attachedDragDropService = null;
        }
    }

    private void DetachDragDropService()
    {
        _dragDropSubscription?.Dispose();
        _dragDropSubscription = null;

        if (_attachedDragDropService != null)
        {
            _attachedDragDropService.DragDropReceived -= OnPlatformDragDropReceived;
            _attachedDragDropService = null;
        }
    }

    private void OnPlatformDragDropReceived(object? sender, WpfDragDropEventArgs e)
    {
        if (!IsPlatformEventForCurrentWindow(sender))
        {
            return;
        }

        DragDropReceived?.Invoke(this, e);
        RequestRenderAndWakeNativeLoop();
    }

    private void AttachWindowEventService()
    {
        if (_window == null)
        {
            return;
        }

        DetachWindowEventService();

        var windowEvents = PlatformServices.WindowEvents;
        try
        {
            windowEvents.WindowEventReceived += OnPlatformWindowEventReceived;
            _windowEventSubscription = windowEvents.Attach(_window);
            _attachedWindowEventService = windowEvents;
        }
        catch (PlatformNotSupportedException)
        {
            windowEvents.WindowEventReceived -= OnPlatformWindowEventReceived;
            _windowEventSubscription = null;
            _attachedWindowEventService = null;
        }
    }

    private void DetachWindowEventService()
    {
        _windowEventSubscription?.Dispose();
        _windowEventSubscription = null;

        if (_attachedWindowEventService != null)
        {
            _attachedWindowEventService.WindowEventReceived -= OnPlatformWindowEventReceived;
            _attachedWindowEventService = null;
        }
    }

    private void OnPlatformWindowEventReceived(object? sender, WpfWindowEventArgs e)
    {
        if (!IsPlatformEventForCurrentWindow(sender))
        {
            return;
        }

        WindowEventReceived?.Invoke(this, e);
        RequestRenderAndWakeNativeLoop();
    }

    private bool IsPlatformEventForCurrentWindow(object? sender)
    {
        return sender is not IView || ReferenceEquals(sender, _window);
    }

    private bool ProcessDispatcherQueueCore()
    {
        try
        {
            return PlatformServices.Dispatcher.ProcessPending();
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private void AttachDispatcherService(IWpfDispatcherService dispatcher)
    {
        DetachDispatcherService();
        dispatcher.WorkAvailable += OnDispatcherWorkAvailable;
        _attachedDispatcherService = dispatcher;
    }

    private void DetachDispatcherService()
    {
        if (_attachedDispatcherService != null)
        {
            _attachedDispatcherService.WorkAvailable -= OnDispatcherWorkAvailable;
            _attachedDispatcherService = null;
        }
    }

    private void OnDispatcherWorkAvailable(object? sender, EventArgs e)
    {
        DispatcherWakeupCount++;
        if (!TryProcessDispatcherWorkWakeup())
        {
            TryRequestNativeLoopWakeup();
        }
    }

    internal bool TryProcessDispatcherWorkWakeup()
    {
        if (_isRendering || _isProcessingDispatcherWorkWakeup)
        {
            return false;
        }

        try
        {
            if (!PlatformServices.Dispatcher.CheckAccess())
            {
                return false;
            }
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        _isProcessingDispatcherWorkWakeup = true;
        try
        {
            return ProcessDispatcherQueueCore();
        }
        finally
        {
            _isProcessingDispatcherWorkWakeup = false;
        }
    }

    private void DisposeTarget()
    {
        DetachInputService();
        DetachDragDropService();
        DetachWindowEventService();

        if (_target == null)
        {
            return;
        }

        ProGpuWpfCompositionTarget target = _target;
        _target = null;
        _directXDevice?.Dispose();
        _directXDevice = null;
        target.RenderInvalidated -= OnCompositionTargetRenderInvalidated;
        target.Dispose();
        WpfRenderScheduler.Reset();
        LastPresentedFrameState = default;
        Interlocked.Exchange(ref _presentedFrameCount, 0);
        Volatile.Write(ref _hasPresentedFrame, false);
        SkippedFrameCount = 0;
        RetainedWpfReplaySkipCount = 0;
        RetainedWpfBranchReplayCount = 0;
    }

    private void ReplaceRenderScheduler(IWpfRenderScheduler scheduler, bool ownsScheduler)
    {
        if (ReferenceEquals(_wpfRenderScheduler, scheduler))
        {
            _ownsRenderScheduler = ownsScheduler;
            return;
        }

        DetachRenderScheduler(_wpfRenderScheduler);
        DisposeOwnedRenderScheduler();
        _wpfRenderScheduler = scheduler;
        _ownsRenderScheduler = ownsScheduler;
        AttachRenderScheduler(_wpfRenderScheduler);
    }

    private void AttachRenderScheduler(IWpfRenderScheduler scheduler)
    {
        scheduler.RenderRequested += OnRenderSchedulerRenderRequested;
    }

    private void DetachRenderScheduler(IWpfRenderScheduler scheduler)
    {
        scheduler.RenderRequested -= OnRenderSchedulerRenderRequested;
    }

    private void OnRenderSchedulerRenderRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        RenderSchedulerWakeupCount++;
        RenderWakeupRequested?.Invoke(this, EventArgs.Empty);
        if (!TryProcessRenderSchedulerWakeup())
        {
            TryRequestNativeLoopWakeup();
        }
    }

    internal bool TryRequestNativeLoopWakeup()
    {
        var window = _window;
        return window != null && TryRequestNativeLoopWakeup(window.ContinueEvents);
    }

    internal void RequestRenderAndWakeNativeLoop()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            WpfRenderScheduler.RequestRender();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        TryRequestNativeLoopWakeup();
    }

    internal void InvalidateWpfSourceForPortableRender(object? source)
    {
        if (_target == null)
        {
            return;
        }

        object? dirtySource = source ?? _wpfRootVisual;
        if (dirtySource != null)
        {
            _target.WpfInvalidationTracker.MarkDirty(dirtySource);
            return;
        }

        _target.WpfInvalidationTracker.MarkDirty();
    }

    internal bool TryRequestNativeLoopWakeup(Action continueEvents)
    {
        ArgumentNullException.ThrowIfNull(continueEvents);

        try
        {
            continueEvents();
            NativeLoopWakeupCount++;
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal bool TryProcessRenderSchedulerWakeup()
    {
        var window = _window;
        if (!ShouldProcessRenderSchedulerWakeupInline(
                _isDisposed,
                window != null,
                _isRendering,
                _isProcessingRenderSchedulerWakeup,
                _isNativeLoopRunning,
                _usesExternalNativeLoopPump))
        {
            return false;
        }

        try
        {
            if (!PlatformServices.Dispatcher.CheckAccess())
            {
                return false;
            }
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        _isProcessingRenderSchedulerWakeup = true;
        try
        {
            try
            {
                window!.DoRender();
            }
            catch (Exception ex) when (IsRecoverableDispatcherRenderException(ex))
            {
                RequestRenderAndWakeNativeLoop();
                return false;
            }

            return true;
        }
        finally
        {
            _isProcessingRenderSchedulerWakeup = false;
            DisposeDeferredNativeWindowIfNeeded();
        }
    }

    internal static bool ShouldProcessRenderSchedulerWakeupInline(
        bool isDisposed,
        bool hasWindow,
        bool isRendering,
        bool isProcessingRenderSchedulerWakeup,
        bool isNativeLoopRunning,
        bool usesExternalNativeLoopPump)
    {
        // A running owner loop, and an externally pumped popup loop, each guarantee
        // their own render opportunity. Rendering inline from a Dispatcher/MediaContext
        // callback can recursively enter SurfacePresent and indefinitely starve the WPF
        // dispatcher during native pointer drags. Let the applicable loop render after
        // dispatcher/input processing returns instead.
        return !isDisposed
            && hasWindow
            && !isRendering
            && !isProcessingRenderSchedulerWakeup
            && !isNativeLoopRunning
            && !usesExternalNativeLoopPump;
    }

    internal void UseExternalNativeLoopPump()
    {
        _usesExternalNativeLoopPump = true;
    }

    internal static bool ShouldPumpExternalNativeRenderBeforeEvents(
        bool usesExternalNativeLoopPump,
        bool shouldPumpNativeRender) =>
        usesExternalNativeLoopPump && shouldPumpNativeRender;

    private static bool IsRecoverableDispatcherRenderException(Exception exception)
    {
        var baseException = exception.GetBaseException();
        return baseException is InvalidOperationException invalidOperation &&
            invalidOperation.Message.IndexOf(
                "dispatcher processing is suspended",
                StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal bool UpdatePortablePresentationSourceDpiScale(double dpiScaleX, double dpiScaleY)
    {
        if (_portablePresentationSourceBridge == null)
        {
            return false;
        }

        if (!double.IsFinite(dpiScaleX) || dpiScaleX <= 0.0 ||
            !double.IsFinite(dpiScaleY) || dpiScaleY <= 0.0)
        {
            return false;
        }

        if (double.IsFinite(_portablePresentationSourceDpiScaleX) &&
            double.IsFinite(_portablePresentationSourceDpiScaleY) &&
            Math.Abs(_portablePresentationSourceDpiScaleX - dpiScaleX) < double.Epsilon &&
            Math.Abs(_portablePresentationSourceDpiScaleY - dpiScaleY) < double.Epsilon)
        {
            return false;
        }

        if (!_portablePresentationSourceBridge.TrySetDeviceScale(dpiScaleX, dpiScaleY))
        {
            return false;
        }

        _portablePresentationSourceDpiScaleX = dpiScaleX;
        _portablePresentationSourceDpiScaleY = dpiScaleY;
        if (Left is int nativeLogicalLeft && Top is int nativeLogicalTop)
        {
            UpdatePortablePresentationSourceClientOrigin(nativeLogicalLeft, nativeLogicalTop);
        }

        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            _portablePopupBridges[i].TrySetDeviceScale(dpiScaleX, dpiScaleY);
        }

        InvalidateWpfRootVisualForPresentationSourceGeometryChange();
        return true;
    }

    internal bool UpdatePortablePresentationSourceClientSize(uint logicalWidth, uint logicalHeight)
    {
        if (_portablePresentationSourceBridge == null)
        {
            return false;
        }

        var clientWidth = (int)Math.Min((uint)int.MaxValue, Math.Max(1u, logicalWidth));
        var clientHeight = (int)Math.Min((uint)int.MaxValue, Math.Max(1u, logicalHeight));
        if (_portablePresentationSourceClientWidth == clientWidth &&
            _portablePresentationSourceClientHeight == clientHeight)
        {
            return false;
        }

        if (!_portablePresentationSourceBridge.TrySetClientSize(clientWidth, clientHeight))
        {
            return false;
        }

        _portablePresentationSourceClientWidth = clientWidth;
        _portablePresentationSourceClientHeight = clientHeight;
        InvalidateWpfRootVisualForPresentationSourceGeometryChange();
        return true;
    }

    internal bool UpdatePortablePresentationSourceClientOrigin(int x, int y)
    {
        WpfPortablePresentationSourceBridge? bridge = _portablePresentationSourceBridge;
        if (bridge == null)
        {
            return false;
        }

        bool originChanged = !_hasPortablePresentationSourceClientOrigin ||
            _portablePresentationSourceClientOriginX != x ||
            _portablePresentationSourceClientOriginY != y;
        if (originChanged && !bridge.TrySetClientOrigin(x, y))
        {
            return false;
        }

        int deviceX = ToDeviceScreenCoordinate(x, _portablePresentationSourceDpiScaleX);
        int deviceY = ToDeviceScreenCoordinate(y, _portablePresentationSourceDpiScaleY);
        UpdatePortablePopupOwnerOrigins(bridge.Source, deviceX, deviceY);

        _portablePresentationSourceClientOriginX = x;
        _portablePresentationSourceClientOriginY = y;
        _hasPortablePresentationSourceClientOrigin = true;
        if (originChanged)
        {
            RequestRenderAndWakeNativeLoop();
        }

        return originChanged;
    }

    internal static int ToDeviceScreenCoordinate(int nativeLogicalCoordinate, double deviceScale)
    {
        double normalizedScale = double.IsFinite(deviceScale) && deviceScale > 0.0
            ? deviceScale
            : 1.0;
        double value = nativeLogicalCoordinate * normalizedScale;
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

    private void InvalidateWpfRootVisualForPresentationSourceGeometryChange()
    {
        _forceFullWpfReplay = true;

        if (_target == null)
        {
            return;
        }

        _target.SceneRootVisual.Invalidate();
        _target.RetainedWpfVisualRoot.Invalidate();
        _target.RootVisual.Invalidate();

        if (_wpfRootVisual != null)
        {
            _target.WpfInvalidationTracker.MarkDirty(_wpfRootVisual);
        }
    }

    private void AttachPortablePresentationSourceBridge(
        WpfPortablePresentationSourceBridge bridge,
        double dpiScaleX,
        double dpiScaleY)
    {
        DisposePortablePresentationSourceBridge();
        _portablePresentationSourceBridge = bridge;
        _portablePresentationSourceDpiScaleX = dpiScaleX;
        _portablePresentationSourceDpiScaleY = dpiScaleY;
        _portablePresentationSourceClientWidth = -1;
        _portablePresentationSourceClientHeight = -1;
        _hasPortablePresentationSourceClientOrigin = false;
        bridge.SyncHostRootVisual();

        int? clientOriginX = Left;
        int? clientOriginY = Top;
        if (clientOriginX.HasValue && clientOriginY.HasValue)
        {
            UpdatePortablePresentationSourceClientOrigin(
                clientOriginX.Value,
                clientOriginY.Value);
        }
    }

    private void DisposePortablePresentationSourceBridge()
    {
        _portablePresentationSourceBridge?.Dispose();
        _portablePresentationSourceBridge = null;
        _portablePresentationSourceDpiScaleX = double.NaN;
        _portablePresentationSourceDpiScaleY = double.NaN;
        _portablePresentationSourceClientWidth = -1;
        _portablePresentationSourceClientHeight = -1;
        _hasPortablePresentationSourceClientOrigin = false;
    }

    internal bool TryCreatePortablePopup(
        PortablePopupCreateRequest request,
        out object? presentationSource)
    {
        presentationSource = null;
        if (_isDisposed ||
            request == null ||
            !OwnsPortablePopupOwner(request.OwnerPresentationSource, request.OwnerHandle))
        {
            return false;
        }

        WpfPortablePopupBridge? ownerPopup = null;
        if (request.OwnerPresentationSource != null)
        {
            TryFindPortablePopup(request.OwnerPresentationSource, out ownerPopup);
        }

        if (!WpfPortablePopupBridge.TryCreate(this, request, ownerPopup, out var bridge))
        {
            return false;
        }

        _portablePopupBridges.Add(bridge!);
        presentationSource = bridge!.Source;
        RequestRenderAndWakeNativeLoop();
        return true;
    }

    internal bool TrySetPortablePopupPosition(object presentationSource, int x, int y)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TrySetPosition(x, y);
        UpdatePortablePopupOwnerOrigins(presentationSource, x, y);
        return true;
    }

    private void UpdatePortablePopupOwnerOrigins(
        object ownerPresentationSource,
        int ownerClientScreenDeviceX,
        int ownerClientScreenDeviceY)
    {
        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            WpfPortablePopupBridge popup = _portablePopupBridges[i];
            if (!popup.TrySetOwnerClientScreenOrigin(
                    ownerPresentationSource,
                    ownerClientScreenDeviceX,
                    ownerClientScreenDeviceY))
            {
                continue;
            }

            UpdatePortablePopupOwnerOrigins(popup.Source, popup.X, popup.Y);
        }
    }

    internal bool TrySetPortablePopupSize(object presentationSource, int width, int height)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TrySetSize(width, height);
        return true;
    }

    internal bool TryShowPortablePopup(object presentationSource)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TryShow();
        return true;
    }

    internal bool TryHidePortablePopup(object presentationSource)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TryHide();
        return true;
    }

    internal bool TrySetPortablePopupHitTestable(object presentationSource, bool hitTestable)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        popup.TrySetHitTestable(hitTestable);
        return true;
    }

    internal bool TryDestroyPortablePopup(object presentationSource)
    {
        if (!TryFindPortablePopup(presentationSource, out var popup))
        {
            return false;
        }

        _portablePopupBridges.Remove(popup);
        popup.Dispose();
        RequestRenderAndWakeNativeLoop();
        return true;
    }

    internal void ClearPortablePopups()
    {
        if (_portablePopupBridges.Count == 0)
        {
            return;
        }

        DisposePortablePopupBridges();
        RequestRenderAndWakeNativeLoop();
    }

    private bool OwnsPortablePopupOwner(object? ownerPresentationSource, IntPtr ownerHandle)
    {
        var rootBridge = _portablePresentationSourceBridge;
        if (rootBridge != null &&
            (ReferenceEquals(ownerPresentationSource, rootBridge.Source) ||
             (ownerHandle != IntPtr.Zero && ownerHandle == rootBridge.Handle)))
        {
            return true;
        }

        for (int i = 0; i < _portablePopupBridges.Count; i++)
        {
            var popup = _portablePopupBridges[i];
            if (ReferenceEquals(ownerPresentationSource, popup.Source) ||
                (ownerHandle != IntPtr.Zero && ownerHandle == popup.Handle))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryFindPortablePopup(object presentationSource, out WpfPortablePopupBridge popup)
    {
        if (presentationSource != null)
        {
            for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
            {
                popup = _portablePopupBridges[i];
                if (ReferenceEquals(presentationSource, popup.Source))
                {
                    return true;
                }
            }
        }

        popup = null!;
        return false;
    }

    private void DisposePortablePopupService()
    {
        _portablePopupServiceRegistration?.Dispose();
        DisposePortablePopupBridges();
    }

    private void DisposePortablePopupBridges()
    {
        for (int i = _portablePopupBridges.Count - 1; i >= 0; i--)
        {
            _portablePopupBridges[i].Dispose();
        }

        _portablePopupBridges.Clear();
    }

    private void DisposeOwnedRenderScheduler()
    {
        if (_ownsRenderScheduler && _wpfRenderScheduler is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _ownsRenderScheduler = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void ShowCore(bool requestRenderWhenInitialized)
    {
        _isHostVisible = true;
        EnsureWindow();
        _window!.IsVisible = true;

        if (!_window.IsInitialized)
        {
            _window.Initialize();
        }
        else if (requestRenderWhenInitialized)
        {
            RequestRenderAndWakeNativeLoop();
        }
    }

    private static IDisposable? RegisterDefaultRenderDataSinkProvider(
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        return drawingFrame.TryRegisterRenderDataSinkProvider(imageSourceAdapter, out IDisposable? registration)
            ? registration
            : null;
    }

    private static IWpfRenderScheduler CreateDefaultRenderScheduler(
        IWpfPlatformServices platformServices,
        out bool ownsScheduler)
    {
        try
        {
            ownsScheduler = true;
            return new DispatcherWpfRenderScheduler(
                platformServices.Dispatcher,
                platformServices.Timers);
        }
        catch (PlatformNotSupportedException)
        {
            ownsScheduler = false;
            return new CoalescingWpfRenderScheduler();
        }
    }

    private static SilkWindowState ToSilkWindowState(ProGpuWpfWindowState windowState)
    {
        return windowState switch
        {
            ProGpuWpfWindowState.Minimized => SilkWindowState.Minimized,
            ProGpuWpfWindowState.Maximized => SilkWindowState.Maximized,
            _ => SilkWindowState.Normal
        };
    }

    private static SilkWindowBorder ToSilkWindowBorder(ProGpuWpfWindowBorder windowBorder)
    {
        return windowBorder switch
        {
            ProGpuWpfWindowBorder.Fixed => SilkWindowBorder.Fixed,
            ProGpuWpfWindowBorder.Hidden => SilkWindowBorder.Hidden,
            ProGpuWpfWindowBorder.HiddenResizable => SilkWindowBorder.Hidden,
            _ => SilkWindowBorder.Resizable
        };
    }

    private void ApplyWindowBorderToController()
    {
        if (_windowController == null)
        {
            return;
        }

        bool hidden = _windowBorder is
            ProGpuWpfWindowBorder.Hidden or
            ProGpuWpfWindowBorder.HiddenResizable;
        bool resizable = _windowBorder is
            ProGpuWpfWindowBorder.Resizable or
            ProGpuWpfWindowBorder.HiddenResizable;
        _windowController.SetDecorations(
            hidden ? NativeWindowDecorations.None : NativeWindowDecorations.Full);
        _windowController.SetCanResize(resizable);
    }
}
