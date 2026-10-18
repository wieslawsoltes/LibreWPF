namespace ProGPU.Wpf.Interop;

public readonly record struct PortableWpfServiceKey(string Name)
{
    public static PortableWpfServiceKey PresentationCore { get; } = new(nameof(PresentationCore));

    public static PortableWpfServiceKey PresentationFramework { get; } = new(nameof(PresentationFramework));

    public static PortableWpfServiceKey WinForms { get; } = new(nameof(WinForms));
}

public interface IPortableClipboardServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    IDisposable Register(Func<string?> getText, Action<string?> setText);

    void Clear();
}

public interface IPortableLauncherServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    IDisposable Register(Func<PortableLaunchRequest, bool> launch);

    void Clear();
}

public interface IPortableMessageBoxServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    IDisposable Register(Func<PortableMessageBoxRequest, string?> show);

    IDisposable RegisterFallback(Func<PortableMessageBoxRequest, string?> show)
    {
        return Register(show);
    }

    void Clear();
}

public interface IPortableFileDialogServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    IDisposable Register(Func<PortableFileDialogRequest, string?> showDialog);

    IDisposable RegisterResult(Func<PortableFileDialogRequest, PortableFileDialogResult?> showDialog)
    {
        ArgumentNullException.ThrowIfNull(showDialog);
        return Register(request => showDialog(request)?.SelectedPath);
    }

    void Clear();
}

public interface IPortableColorDialogServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    IDisposable Register(Func<PortableColorDialogRequest, int?> showDialog);

    void Clear();
}

public interface IPortableFontDialogServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    IDisposable Register(Func<PortableFontDialogRequest, PortableFontDialogResult?> showDialog);

    void Clear();
}

public interface IPortablePopupServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    bool TryCreatePopup(PortablePopupCreateRequest request, out object? presentationSource);

    bool TrySetPopupPosition(object presentationSource, int x, int y);

    bool TrySetPopupSize(object presentationSource, int width, int height);

    bool TryShowPopup(object presentationSource);

    bool TryHidePopup(object presentationSource);

    bool TrySetPopupHitTestable(object presentationSource, bool hitTestable);

    /// <summary>
    /// Queries placement limits for an already-owned popup, before or after Show.
    /// False means unavailable, not permission to constrain a native popup to its
    /// owner. Query and result use the host's desktop placement coordinates.
    /// </summary>
    bool TryGetPopupPlacementBounds(object presentationSource, PortableRect targetBounds,
        out PortablePopupPlacementBounds bounds)
    {
        bounds = default;
        return false;
    }

    bool TryDestroyPopup(object presentationSource);

    void Clear();
}

public sealed class PortableLaunchRequest
{
    public PortableLaunchRequest(Uri uri, string targetFrame, bool isTopLevel)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        TargetFrame = targetFrame;
        IsTopLevel = isTopLevel;
    }

    public Uri Uri { get; }

    public string TargetFrame { get; }

    public bool IsTopLevel { get; }
}

public sealed class PortableMessageBoxRequest
{
    public PortableMessageBoxRequest(
        string? messageBoxText,
        string? caption,
        string? button,
        string? icon,
        string? defaultResult,
        string? options,
        string? fallbackResult)
        : this(null, messageBoxText, caption, button, icon, defaultResult, options, fallbackResult)
    {
    }

    public PortableMessageBoxRequest(
        object? owner,
        string? messageBoxText,
        string? caption,
        string? button,
        string? icon,
        string? defaultResult,
        string? options,
        string? fallbackResult)
    {
        Owner = owner;
        MessageBoxText = messageBoxText ?? string.Empty;
        Caption = caption ?? string.Empty;
        Button = button ?? "OK";
        Icon = icon ?? "None";
        DefaultResult = defaultResult ?? "None";
        Options = options ?? "None";
        FallbackResult = fallbackResult ?? "OK";
    }

    public object? Owner { get; }

    public string MessageBoxText { get; }

    public string Caption { get; }

    public string Button { get; }

    public string Icon { get; }

    public string DefaultResult { get; }

    public string Options { get; }

    public string FallbackResult { get; }
}

public sealed class PortableFileDialogRequest
{
    public PortableFileDialogRequest(
        string? kind,
        string? title,
        string? initialDirectory,
        string? defaultDirectory,
        string? suggestedItemName,
        string? defaultExtension,
        string? filter,
        int filterIndex)
        : this(
            kind,
            title,
            initialDirectory,
            defaultDirectory,
            suggestedItemName,
            defaultExtension,
            filter,
            filterIndex,
            allowMultipleSelection: false)
    {
    }

    public PortableFileDialogRequest(
        string? kind,
        string? title,
        string? initialDirectory,
        string? defaultDirectory,
        string? suggestedItemName,
        string? defaultExtension,
        string? filter,
        int filterIndex,
        bool allowMultipleSelection)
    {
        Kind = kind ?? "OpenFile";
        Title = title ?? string.Empty;
        InitialDirectory = initialDirectory ?? string.Empty;
        DefaultDirectory = defaultDirectory ?? string.Empty;
        SuggestedItemName = suggestedItemName ?? string.Empty;
        DefaultExtension = defaultExtension ?? string.Empty;
        Filter = filter ?? string.Empty;
        FilterIndex = filterIndex;
        AllowMultipleSelection = allowMultipleSelection;
    }

    public string Kind { get; }

    public string Title { get; }

    public string InitialDirectory { get; }

    public string DefaultDirectory { get; }

    public string SuggestedItemName { get; }

    public string DefaultExtension { get; }

    public string Filter { get; }

    public int FilterIndex { get; }

    public bool AllowMultipleSelection { get; }
}

public sealed class PortableFileDialogResult
{
    private readonly string[] _selectedPaths;

    public PortableFileDialogResult(string selectedPath)
    {
        ArgumentNullException.ThrowIfNull(selectedPath);
        _selectedPaths = [selectedPath];
    }

    public PortableFileDialogResult(ReadOnlySpan<string> selectedPaths)
    {
        _selectedPaths = selectedPaths.ToArray();
    }

    public int SelectedPathCount => _selectedPaths.Length;

    public string? SelectedPath => _selectedPaths.Length == 0 ? null : _selectedPaths[0];

    public ReadOnlySpan<string> SelectedPaths => _selectedPaths;

    public string GetSelectedPath(int index)
    {
        return _selectedPaths[index];
    }

    public string[] ToArray()
    {
        return (string[])_selectedPaths.Clone();
    }
}

public sealed class PortableColorDialogRequest
{
    private readonly int[] _customColors;

    public PortableColorDialogRequest(int initialArgb, IReadOnlyList<int>? customColors)
    {
        InitialArgb = initialArgb;

        if (customColors == null || customColors.Count == 0)
        {
            _customColors = Array.Empty<int>();
        }
        else
        {
            _customColors = new int[customColors.Count];
            for (var i = 0; i < customColors.Count; i++)
            {
                _customColors[i] = customColors[i];
            }
        }
    }

    public int InitialArgb { get; }

    public IReadOnlyList<int> CustomColors => _customColors;
}

public sealed class PortableFontDialogRequest
{
    public PortableFontDialogRequest(
        string? familyName,
        float size,
        int style,
        string? unit,
        bool showEffects,
        bool showColor,
        int minSize,
        int maxSize)
    {
        FamilyName = string.IsNullOrWhiteSpace(familyName) ? "Courier New" : familyName!;
        Size = size > 0 && float.IsFinite(size) ? size : 10f;
        Style = style;
        Unit = string.IsNullOrWhiteSpace(unit) ? "Point" : unit!;
        ShowEffects = showEffects;
        ShowColor = showColor;
        MinSize = minSize;
        MaxSize = maxSize;
    }

    public string FamilyName { get; }

    public float Size { get; }

    public int Style { get; }

    public string Unit { get; }

    public bool ShowEffects { get; }

    public bool ShowColor { get; }

    public int MinSize { get; }

    public int MaxSize { get; }
}

public sealed class PortableFontDialogResult
{
    public PortableFontDialogResult(string? familyName, float size, int style, string? unit)
    {
        FamilyName = string.IsNullOrWhiteSpace(familyName) ? "Courier New" : familyName!;
        Size = size > 0 && float.IsFinite(size) ? size : 10f;
        Style = style;
        Unit = string.IsNullOrWhiteSpace(unit) ? "Point" : unit!;
    }

    public string FamilyName { get; }

    public float Size { get; }

    public int Style { get; }

    public string Unit { get; }
}

/// <summary>
/// Describes a popup that a portable presentation host should composite into
/// an owner surface.
/// </summary>
public sealed class PortablePopupCreateRequest
{
    /// <summary>
    /// Creates a popup request using the legacy coordinate contract. The popup
    /// coordinates are absolute screen-device coordinates and the owner client
    /// origin defaults to the global screen-device coordinate origin (0, 0) so
    /// existing consumers keep their current positioning behavior.
    /// </summary>
    public PortablePopupCreateRequest(
        object? placementTarget,
        object? ownerPresentationSource,
        IntPtr ownerHandle,
        int x,
        int y,
        bool isTransparent,
        bool isChildPopup)
        : this(
            placementTarget,
            ownerPresentationSource,
            ownerHandle,
            x,
            y,
            ownerClientScreenDeviceX: 0,
            ownerClientScreenDeviceY: 0,
            isTransparent,
            isChildPopup)
    {
    }

    /// <summary>
    /// Creates a popup request with the popup position and owner client origin
    /// expressed in the same absolute screen-device coordinate space.
    /// </summary>
    public PortablePopupCreateRequest(
        object? placementTarget,
        object? ownerPresentationSource,
        IntPtr ownerHandle,
        int popupScreenDeviceX,
        int popupScreenDeviceY,
        int ownerClientScreenDeviceX,
        int ownerClientScreenDeviceY,
        bool isTransparent,
        bool isChildPopup)
    {
        PlacementTarget = placementTarget;
        OwnerPresentationSource = ownerPresentationSource;
        OwnerHandle = ownerHandle;
        PopupScreenDeviceX = popupScreenDeviceX;
        PopupScreenDeviceY = popupScreenDeviceY;
        OwnerClientScreenDeviceX = ownerClientScreenDeviceX;
        OwnerClientScreenDeviceY = ownerClientScreenDeviceY;
        IsTransparent = isTransparent;
        IsChildPopup = isChildPopup;
    }

    public object? PlacementTarget { get; }

    public object? OwnerPresentationSource { get; }

    public IntPtr OwnerHandle { get; }

    /// <summary>
    /// Gets the popup's absolute horizontal screen-device coordinate.
    /// </summary>
    public int PopupScreenDeviceX { get; }

    /// <summary>
    /// Gets the popup's absolute vertical screen-device coordinate.
    /// </summary>
    public int PopupScreenDeviceY { get; }

    /// <summary>
    /// Gets the absolute horizontal screen-device coordinate of the owner
    /// presentation source's client origin.
    /// </summary>
    public int OwnerClientScreenDeviceX { get; }

    /// <summary>
    /// Gets the absolute vertical screen-device coordinate of the owner
    /// presentation source's client origin.
    /// </summary>
    public int OwnerClientScreenDeviceY { get; }

    /// <summary>
    /// Gets the popup's absolute horizontal screen-device coordinate.
    /// This is the compatibility alias for <see cref="PopupScreenDeviceX"/>.
    /// </summary>
    public int X => PopupScreenDeviceX;

    /// <summary>
    /// Gets the popup's absolute vertical screen-device coordinate.
    /// This is the compatibility alias for <see cref="PopupScreenDeviceY"/>.
    /// </summary>
    public int Y => PopupScreenDeviceY;

    public bool IsTransparent { get; }

    public bool IsChildPopup { get; }
}

public sealed class PortableWindowRegion
{
    private readonly PortableRect[] _excludedRects;

    public PortableWindowRegion(PortableRect bounds, IReadOnlyList<PortableRect>? excludedRects = null)
    {
        Bounds = bounds;
        // Capture once: a caller changing its list must not mutate a region
        // already installed in a retained managed or native scene.
        _excludedRects = excludedRects is null ? [] : excludedRects.ToArray();
        ExcludedRects = Array.AsReadOnly(_excludedRects);
    }

    public PortableRect Bounds { get; }

    public IReadOnlyList<PortableRect> ExcludedRects { get; }

    public ReadOnlySpan<PortableRect> ExcludedRectSpan => _excludedRects;

    public bool IsEmpty => Bounds.IsEmpty || Bounds.Width <= 0 || Bounds.Height <= 0;
}

public sealed class PortableWindowActivationCallbacks
{
    public PortableWindowActivationCallbacks(
        Func<object, object?> activate,
        Action<object>? show = null,
        Action<object>? hide = null,
        Action<object, object>? setWindowState = null,
        Action<object, string>? setTitle = null,
        Action<object, double, double>? setClientSize = null,
        Action<object, double, double>? setPosition = null,
        Action<object, bool>? setTopmost = null,
        Action<object, object, object>? setWindowBorder = null,
        Action<object>? close = null,
        Action<object>? run = null,
        Action<object>? dispose = null,
        Func<object, bool>? dragMove = null,
        Func<object, IntPtr>? getHandle = null,
        Func<IntPtr, PortableWindowRegion, bool>? setWindowRegion = null,
        Func<object, bool>? requestActivation = null,
        Action<object, object?>? setIcon = null)
    {
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
        Show = show;
        Hide = hide;
        SetWindowState = setWindowState;
        SetTitle = setTitle;
        SetClientSize = setClientSize;
        SetPosition = setPosition;
        SetTopmost = setTopmost;
        SetWindowBorder = setWindowBorder;
        Close = close;
        Run = run;
        Dispose = dispose;
        DragMove = dragMove;
        GetHandle = getHandle;
        SetWindowRegion = setWindowRegion;
        RequestActivation = requestActivation;
        SetIcon = setIcon;
    }

    public Func<object, object?> Activate { get; }

    /// <summary>
    /// Creates an owned, hidden window source without attaching the visual tree,
    /// showing the window, or requesting foreground activation. The ordinary
    /// Show callback attaches the tree later and reuses this activation object.
    /// </summary>
    /// <remarks>
    /// Optional for existing hosts. A consumer must reject hidden-source creation
    /// when absent, not fall back to Activate or a different window backend.
    /// GetHandle must return the stable nonzero portable source identity before
    /// this callback returns. That identity is not necessarily a native HWND.
    /// A failing factory owns cleanup of every resource it allocated.
    /// </remarks>
    public Func<object, object?>? CreateHidden { get; init; }

    /// <summary>
    /// Displays the existing activation's system menu at absolute native desktop
    /// coordinates. Source handles are opaque; only the host resolves native
    /// ownership. Return false when unsupported/rejected, not silent success.
    /// May run a modal platform loop and reenter source callbacks.
    /// </summary>
    public Func<object, double, double, bool>? ShowSystemMenu { get; init; }

    public Action<object>? Show { get; }

    public Action<object>? Hide { get; }

    public Action<object, object>? SetWindowState { get; }

    public Action<object, string>? SetTitle { get; }

    public Action<object, double, double>? SetClientSize { get; }

    /// <summary>
    /// Resolves the live native frame around the source Window's client in
    /// desktop logical units. Null means the host is not yet initialized or
    /// cannot report authoritative frame metrics; zero is a known borderless
    /// frame. Consumers must not use framebuffer scale or invent title-bar
    /// dimensions when this capability is unavailable.
    /// </summary>
    public Func<object, PortableWindowFrameInsets?>? GetFrameInsets { get; init; }

    /// <summary>
    /// Reports whether this live host needs a source-owned caption for ordinary
    /// NoResize windows. The source still owns style/custom-chrome selection.
    /// This is decoration policy, not a native frame measurement or input gate.
    /// </summary>
    public Func<object, bool>? RequiresSourceFixedCaption { get; init; }

    /// <summary>Changes enabled state of an existing close control, independently
    /// of its visibility or programmatic closing. False means unsupported or
    /// not yet applied; an advisory native hint is not desktop qualification.</summary>
    public Func<object, bool, bool>? SetCloseButtonEnabled { get; init; }

    public Action<object, double, double>? SetPosition { get; }

    public Action<object, bool>? SetTopmost { get; }

    /// <summary>Optional source Win32 taskbar policy; other native providers retain their separate admission.</summary>
    public Action<object, bool>? SetWin32TaskbarVisibility { get; init; }

    public Action<object, object, object>? SetWindowBorder { get; }

    public Action<object>? Close { get; }

    /// <summary>
    /// Pumps one live activation on its source thread until that host retires or
    /// the application requests shutdown. Returning does not request application
    /// shutdown. The source may then select another existing host without showing
    /// or activating it again, or wait without windows under explicit lifetime.
    /// A premature return while both host and application are live is an error.
    /// </summary>
    public Action<object>? Run { get; }

    /// <summary>
    /// Pumps an already shown dialog synchronously while the source-owned
    /// continuation returns true. Check it between event/render iterations and
    /// return without closing or disposing a hidden window. Invoke and borrow the
    /// continuation only on the host thread for the duration of this call; do not
    /// retain it. Exceptions propagate. This is distinct from the application Run
    /// lifetime and does not itself implement owner disabling or native ownership.
    /// </summary>
    public Action<object, Func<bool>>? RunDialog { get; init; }

    /// <summary>
    /// Ends native modality for this activation, then invokes completion exactly
    /// once on its host thread (synchronously if no native session owns it).
    /// May retain completion until native event callbacks and nested sessions
    /// unwind. A failed release must throw, not report success. Dialog consumers
    /// require this capability before Show, together with RunDialog; input gates
    /// and focus restoration belong after completion, not after requesting it.
    /// </summary>
    public Action<object, Action>? ReleaseDialog { get; init; }

    /// <summary>
    /// Sets the source Window owner (null clears it). The host resolves its own
    /// live native identity, never an opaque presentation-source handle. Reject
    /// unsupported ownership explicitly. This does not establish input modality.
    /// </summary>
    public Action<object, object?>? SetOwner { get; init; }

    public Action<object>? Dispose { get; }

    public Func<object, bool>? DragMove { get; }

    public Func<object, IntPtr>? GetHandle { get; }

    public Func<IntPtr, PortableWindowRegion, bool>? SetWindowRegion { get; }

    /// <summary>
    /// Requests native foreground activation for an existing portable window host.
    /// </summary>
    /// <remarks>
    /// This callback is distinct from <see cref="Activate"/>, which creates or resolves
    /// the portable activation object. Hosts should return <see langword="true"/> only
    /// when the native platform accepted the foreground request.
    /// </remarks>
    public Func<object, bool>? RequestActivation { get; }

    /// <summary>
    /// Updates the framework-owned icon source for an existing portable window host.
    /// A <see langword="null"/> source clears the native window icon.
    /// </summary>
    public Action<object, object?>? SetIcon { get; }
}

public sealed class PortableWindowInputEvent
{
    public PortableWindowInputEvent(
        int kind,
        string? key = null,
        int scanCode = 0,
        char? character = null,
        double x = 0,
        double y = 0,
        double deltaX = 0,
        double deltaY = 0,
        int button = 0,
        int modifiers = 0)
    {
        Kind = kind;
        Key = key;
        ScanCode = scanCode;
        Character = character;
        X = x;
        Y = y;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Button = button;
        Modifiers = modifiers;
    }

    public int Kind { get; }

    public string? Key { get; }

    public int ScanCode { get; }

    public char? Character { get; }

    public double X { get; }

    public double Y { get; }

    public double DeltaX { get; }

    public double DeltaY { get; }

    public int Button { get; }

    public int Modifiers { get; }

    public bool Handled { get; set; }
}

public enum PortableWindowCloseResult
{
    NotInvoked = 0,
    Closed = 1,
    Canceled = 2
}

public interface IPortableWindowActivationServiceRegistrar
{
    PortableWpfServiceKey ServiceKey { get; }

    void Register(PortableWindowActivationCallbacks callbacks);

    bool TryRegisterMediaContextRenderService(
        object window,
        Action<object?, TimeSpan> requestRender,
        out IDisposable? registration)
    {
        registration = null;
        return false;
    }

    bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow);

    bool TryCloseWindow(object window, out PortableWindowCloseResult result);

    bool TryIsWindowDisposed(object window, out bool isDisposed)
    {
        isDisposed = false;
        return false;
    }

    bool TrySetActivationState(object window, bool isActive);

    bool TryBeginInvokeInput(object window, Action callback);

    bool TryProcessInputEvent(object window, PortableWindowInputEvent input);

    bool TryProcessPresentationSourceInputEvent(object presentationSource, PortableWindowInputEvent input)
    {
        return false;
    }

    bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout);

    /// <summary>
    /// Registers a lightweight notification that is raised whenever idle-priority work is
    /// posted to the dispatcher's queue. Hosts use the notification to wake their native event
    /// loop without polling lower-priority dispatcher work on every frame.
    /// </summary>
    bool TryRegisterDispatcherIdleWorkNotification(
        object window,
        Action workPosted,
        out IDisposable? registration)
    {
        registration = null;
        return false;
    }

    bool TryPromoteDispatcherTimers(object window, int currentTimeInTicks)
    {
        return false;
    }

    bool TrySetWindowRegion(IntPtr handle, PortableWindowRegion region)
    {
        return false;
    }

    bool TryProcessDragDropEvent(
        object window,
        int dragDropEventKind,
        string[] files,
        string? text,
        double x,
        double y,
        int allowedEffects,
        int acceptedEffect,
        out int result);

    void Clear();
}

public static partial class PortableWpfServiceRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableWindowActivationServiceRegistrar> WindowActivationServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableClipboardServiceRegistrar> ClipboardServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableLauncherServiceRegistrar> LauncherServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableMessageBoxServiceRegistrar> MessageBoxServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableFileDialogServiceRegistrar> FileDialogServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableColorDialogServiceRegistrar> ColorDialogServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableFontDialogServiceRegistrar> FontDialogServices = new();
    private static readonly Dictionary<PortableWpfServiceKey, PopupServiceRouter> PopupServiceRouters = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableSystemThemeSource> SystemThemeSources = new();
    private static readonly Dictionary<PortableWpfServiceKey, IPortableDisplayMetricsSource> DisplayMetricsSources = new();

    public static event Action<IPortableClipboardServiceRegistrar>? ClipboardServiceRegistered;

    public static event Action<IPortableMessageBoxServiceRegistrar>? MessageBoxServiceRegistered;

    public static event Action<IPortableFileDialogServiceRegistrar>? FileDialogServiceRegistered;

    public static event Action<IPortableColorDialogServiceRegistrar>? ColorDialogServiceRegistered;

    public static event Action<IPortableFontDialogServiceRegistrar>? FontDialogServiceRegistered;

    /// <summary>
    /// Raised when a registered platform theme source reports a state change,
    /// or when the active source for a service key is replaced or removed.
    /// </summary>
    public static event EventHandler? SystemThemeChanged;

    /// <summary>
    /// Raised when a registered platform display source reports a geometry change,
    /// or when the active source for a service key is replaced or removed.
    /// </summary>
    public static event EventHandler? DisplayMetricsChanged;

    private static Action? s_nativeInputPump;

    /// <summary>
    /// Pumps every active native window's event queue once. Set by the portable windowing layer
    /// (ProGPU.Wpf), consumed by whoever owns the real WPF Dispatcher (PresentationFramework wires
    /// it into System.Windows.Threading.Dispatcher.NativeInputPump) - neither can reference the
    /// other directly (ProGPU.Wpf compiles against a WPF-shaped compile-time stub, not the real
    /// WindowsBase it runs against), so this registry is the seam between them.
    ///
    /// Without it a NESTED Dispatcher.PushFrame (Window.ShowDialog, DragDrop's portable
    /// drag-source loop) exits as soon as the managed queue drains, because the only thing that
    /// can produce the input it is waiting for is a pump call like this. Measured before this was
    /// wired up: a portable drag-and-drop's nested frame returned after ~7ms having processed zero
    /// mouse updates, so no drop target ever saw DragOver/Drop and no drag cursor ever appeared.
    /// </summary>
    public static Action? NativeInputPump
    {
        get
        {
            lock (SyncRoot)
            {
                return s_nativeInputPump;
            }
        }
        set
        {
            lock (SyncRoot)
            {
                if (ReferenceEquals(s_nativeInputPump, value))
                {
                    return;
                }

                s_nativeInputPump = value;
            }

            NativeInputPumpChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised when <see cref="NativeInputPump"/> is set or cleared, so a consumer that initializes
    /// before the windowing layer does not have to poll for it.
    /// </summary>
    public static event EventHandler? NativeInputPumpChanged;

    public static IDisposable RegisterWindowActivationService(IPortableWindowActivationServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            WindowActivationServices[service.ServiceKey] = service;
        }

        return new Registration<IPortableWindowActivationServiceRegistrar>(service, WindowActivationServices);
    }

    public static bool TryGetWindowActivationService(
        PortableWpfServiceKey serviceKey,
        out IPortableWindowActivationServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return WindowActivationServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterClipboardService(IPortableClipboardServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            ClipboardServices[service.ServiceKey] = service;
        }

        ClipboardServiceRegistered?.Invoke(service);
        return new Registration<IPortableClipboardServiceRegistrar>(service, ClipboardServices);
    }

    public static bool TryGetClipboardService(
        PortableWpfServiceKey serviceKey,
        out IPortableClipboardServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return ClipboardServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterLauncherService(IPortableLauncherServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            LauncherServices[service.ServiceKey] = service;
        }

        return new Registration<IPortableLauncherServiceRegistrar>(service, LauncherServices);
    }

    public static bool TryGetLauncherService(
        PortableWpfServiceKey serviceKey,
        out IPortableLauncherServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return LauncherServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterMessageBoxService(IPortableMessageBoxServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            MessageBoxServices[service.ServiceKey] = service;
        }

        MessageBoxServiceRegistered?.Invoke(service);
        return new Registration<IPortableMessageBoxServiceRegistrar>(service, MessageBoxServices);
    }

    public static bool TryGetMessageBoxService(
        PortableWpfServiceKey serviceKey,
        out IPortableMessageBoxServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return MessageBoxServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterFileDialogService(IPortableFileDialogServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            FileDialogServices[service.ServiceKey] = service;
        }

        FileDialogServiceRegistered?.Invoke(service);
        return new Registration<IPortableFileDialogServiceRegistrar>(service, FileDialogServices);
    }

    public static bool TryGetFileDialogService(
        PortableWpfServiceKey serviceKey,
        out IPortableFileDialogServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return FileDialogServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterColorDialogService(IPortableColorDialogServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            ColorDialogServices[service.ServiceKey] = service;
        }

        ColorDialogServiceRegistered?.Invoke(service);
        return new Registration<IPortableColorDialogServiceRegistrar>(service, ColorDialogServices);
    }

    public static bool TryGetColorDialogService(
        PortableWpfServiceKey serviceKey,
        out IPortableColorDialogServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return ColorDialogServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterFontDialogService(IPortableFontDialogServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        lock (SyncRoot)
        {
            FontDialogServices[service.ServiceKey] = service;
        }

        FontDialogServiceRegistered?.Invoke(service);
        return new Registration<IPortableFontDialogServiceRegistrar>(service, FontDialogServices);
    }

    public static bool TryGetFontDialogService(
        PortableWpfServiceKey serviceKey,
        out IPortableFontDialogServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return FontDialogServices.TryGetValue(serviceKey, out service!);
        }
    }

    public static IDisposable RegisterPopupService(IPortablePopupServiceRegistrar service)
    {
        ArgumentNullException.ThrowIfNull(service);
        ValidateServiceKey(service.ServiceKey, nameof(service));

        PopupServiceRouter router;
        lock (SyncRoot)
        {
            if (!PopupServiceRouters.TryGetValue(service.ServiceKey, out router!))
            {
                router = new PopupServiceRouter(service.ServiceKey);
                PopupServiceRouters.Add(service.ServiceKey, router);
            }

            router.Add(service);
        }

        return new PopupServiceRegistration(router, service);
    }

    public static bool TryGetPopupService(
        PortableWpfServiceKey serviceKey,
        out IPortablePopupServiceRegistrar service)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            if (PopupServiceRouters.TryGetValue(serviceKey, out PopupServiceRouter? router) &&
                router.HasServices)
            {
                service = router;
                return true;
            }

            service = null!;
            return false;
        }
    }

    public static IDisposable RegisterSystemThemeSource(IPortableSystemThemeSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateServiceKey(source.ServiceKey, nameof(source));

        IPortableSystemThemeSource? replacedSource = null;
        lock (SyncRoot)
        {
            if (SystemThemeSources.TryGetValue(source.ServiceKey, out replacedSource))
            {
                replacedSource.SystemThemeChanged -= OnSystemThemeSourceChanged;
            }

            SystemThemeSources[source.ServiceKey] = source;
            source.SystemThemeChanged += OnSystemThemeSourceChanged;
        }

        SystemThemeChanged?.Invoke(source, EventArgs.Empty);
        return new SystemThemeSourceRegistration(source);
    }

    public static bool TryGetSystemThemeSource(
        PortableWpfServiceKey serviceKey,
        out IPortableSystemThemeSource source)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return SystemThemeSources.TryGetValue(serviceKey, out source!);
        }
    }

    private static void OnSystemThemeSourceChanged(object? sender, EventArgs e)
    {
        SystemThemeChanged?.Invoke(sender, e);
    }

    public static IDisposable RegisterDisplayMetricsSource(IPortableDisplayMetricsSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateServiceKey(source.ServiceKey, nameof(source));

        IPortableDisplayMetricsSource? replacedSource = null;
        lock (SyncRoot)
        {
            if (DisplayMetricsSources.TryGetValue(source.ServiceKey, out replacedSource))
            {
                replacedSource.DisplayMetricsChanged -= OnDisplayMetricsSourceChanged;
            }

            DisplayMetricsSources[source.ServiceKey] = source;
            source.DisplayMetricsChanged += OnDisplayMetricsSourceChanged;
        }

        DisplayMetricsChanged?.Invoke(source, EventArgs.Empty);
        return new DisplayMetricsSourceRegistration(source);
    }

    public static bool TryGetDisplayMetricsSource(
        PortableWpfServiceKey serviceKey,
        out IPortableDisplayMetricsSource source)
    {
        ValidateServiceKey(serviceKey, nameof(serviceKey));

        lock (SyncRoot)
        {
            return DisplayMetricsSources.TryGetValue(serviceKey, out source!);
        }
    }

    private static void OnDisplayMetricsSourceChanged(object? sender, EventArgs e)
    {
        DisplayMetricsChanged?.Invoke(sender, e);
    }

    private static void ValidateServiceKey(PortableWpfServiceKey serviceKey, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(serviceKey.Name))
        {
            throw new ArgumentException("Portable WPF service keys must have a non-empty name.", parameterName);
        }
    }

    private sealed class Registration<TService> : IDisposable
        where TService : class
    {
        private readonly Dictionary<PortableWpfServiceKey, TService> _services;
        private TService? _service;

        public Registration(TService service, Dictionary<PortableWpfServiceKey, TService> services)
        {
            _service = service;
            _services = services;
        }

        public void Dispose()
        {
            var service = _service;
            if (service == null)
            {
                return;
            }

            _service = null;

            lock (SyncRoot)
            {
                var serviceKey = GetServiceKey(service);
                if (_services.TryGetValue(serviceKey, out var current) &&
                    ReferenceEquals(current, service))
                {
                    _services.Remove(serviceKey);
                }
            }
        }

        private static PortableWpfServiceKey GetServiceKey(TService service)
        {
            return service switch
            {
                IPortableWindowActivationServiceRegistrar windowActivationService => windowActivationService.ServiceKey,
                IPortableClipboardServiceRegistrar clipboardService => clipboardService.ServiceKey,
                IPortableLauncherServiceRegistrar launcherService => launcherService.ServiceKey,
                IPortableMessageBoxServiceRegistrar messageBoxService => messageBoxService.ServiceKey,
                IPortableFileDialogServiceRegistrar fileDialogService => fileDialogService.ServiceKey,
                IPortableColorDialogServiceRegistrar colorDialogService => colorDialogService.ServiceKey,
                IPortableFontDialogServiceRegistrar fontDialogService => fontDialogService.ServiceKey,
                IPortablePopupServiceRegistrar popupService => popupService.ServiceKey,
                _ => throw new InvalidOperationException("Unsupported portable WPF service registrar.")
            };
        }
    }

    private sealed class PopupServiceRegistration : IDisposable
    {
        private PopupServiceRouter? _router;
        private IPortablePopupServiceRegistrar? _service;

        public PopupServiceRegistration(
            PopupServiceRouter router,
            IPortablePopupServiceRegistrar service)
        {
            _router = router;
            _service = service;
        }

        public void Dispose()
        {
            IPortablePopupServiceRegistrar? service = Interlocked.Exchange(ref _service, null);
            PopupServiceRouter? router = Interlocked.Exchange(ref _router, null);
            if (service != null && router != null)
            {
                router.Remove(service);
            }
        }
    }

    private sealed class PopupServiceRouter : IPortablePopupServiceRegistrar
    {
        private readonly object _gate = new();
        private readonly List<IPortablePopupServiceRegistrar> _services = new();
        private readonly Dictionary<object, IPortablePopupServiceRegistrar> _popupOwners =
            new(ReferenceEqualityComparer.Instance);

        public PopupServiceRouter(PortableWpfServiceKey serviceKey)
        {
            ServiceKey = serviceKey;
        }

        public PortableWpfServiceKey ServiceKey { get; }

        public bool HasServices
        {
            get
            {
                lock (_gate)
                {
                    return _services.Count != 0;
                }
            }
        }

        public void Add(IPortablePopupServiceRegistrar service)
        {
            lock (_gate)
            {
                _services.Add(service);
            }
        }

        public void Remove(IPortablePopupServiceRegistrar service)
        {
            lock (_gate)
            {
                for (int index = _services.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(_services[index], service))
                    {
                        _services.RemoveAt(index);
                        break;
                    }
                }

                if (_popupOwners.Count == 0)
                {
                    return;
                }

                object[] ownedPopups = _popupOwners
                    .Where(pair => ReferenceEquals(pair.Value, service))
                    .Select(static pair => pair.Key)
                    .ToArray();
                foreach (object popup in ownedPopups)
                {
                    _popupOwners.Remove(popup);
                }
            }
        }

        public bool TryCreatePopup(PortablePopupCreateRequest request, out object? presentationSource)
        {
            ArgumentNullException.ThrowIfNull(request);

            IPortablePopupServiceRegistrar[] services = GetServicesNewestFirst();
            foreach (IPortablePopupServiceRegistrar service in services)
            {
                if (!service.TryCreatePopup(request, out presentationSource))
                {
                    continue;
                }

                if (presentationSource != null)
                {
                    lock (_gate)
                    {
                        _popupOwners[presentationSource] = service;
                    }
                }

                return true;
            }

            presentationSource = null;
            return false;
        }

        public bool TrySetPopupPosition(object presentationSource, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);
            return TryRoute(
                presentationSource,
                service => service.TrySetPopupPosition(presentationSource, x, y));
        }

        public bool TrySetPopupSize(object presentationSource, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);
            return TryRoute(
                presentationSource,
                service => service.TrySetPopupSize(presentationSource, width, height));
        }

        public bool TryShowPopup(object presentationSource)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);
            return TryRoute(presentationSource, service => service.TryShowPopup(presentationSource));
        }

        public bool TryHidePopup(object presentationSource)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);
            return TryRoute(presentationSource, service => service.TryHidePopup(presentationSource));
        }

        public bool TrySetPopupHitTestable(object presentationSource, bool hitTestable)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);
            return TryRoute(
                presentationSource,
                service => service.TrySetPopupHitTestable(presentationSource, hitTestable));
        }

        public bool TryDestroyPopup(object presentationSource)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);

            IPortablePopupServiceRegistrar? owner = GetPopupOwner(presentationSource);
            if (owner != null)
            {
                bool destroyed = owner.TryDestroyPopup(presentationSource);
                if (destroyed)
                {
                    lock (_gate)
                    {
                        _popupOwners.Remove(presentationSource);
                    }
                }

                return destroyed;
            }

            foreach (IPortablePopupServiceRegistrar service in GetServicesNewestFirst())
            {
                if (service.TryDestroyPopup(presentationSource))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetPopupPlacementBounds(object presentationSource, PortableRect targetBounds,
            out PortablePopupPlacementBounds bounds)
        {
            ArgumentNullException.ThrowIfNull(presentationSource);
            // Unlike compatibility operation routing, an ownership query must
            // never let another window's service guess bounds for this popup.
            IPortablePopupServiceRegistrar? owner = GetPopupOwner(presentationSource);
            if (owner != null)
                return owner.TryGetPopupPlacementBounds(presentationSource, targetBounds, out bounds);
            bounds = default;
            return false;
        }

        public void Clear()
        {
            IPortablePopupServiceRegistrar[] services = GetServicesNewestFirst();
            lock (_gate)
            {
                _popupOwners.Clear();
            }

            foreach (IPortablePopupServiceRegistrar service in services)
            {
                service.Clear();
            }
        }

        private bool TryRoute(
            object presentationSource,
            Func<IPortablePopupServiceRegistrar, bool> operation)
        {
            IPortablePopupServiceRegistrar? owner = GetPopupOwner(presentationSource);
            if (owner != null)
            {
                return operation(owner);
            }

            foreach (IPortablePopupServiceRegistrar service in GetServicesNewestFirst())
            {
                if (operation(service))
                {
                    lock (_gate)
                    {
                        _popupOwners[presentationSource] = service;
                    }

                    return true;
                }
            }

            return false;
        }

        private IPortablePopupServiceRegistrar? GetPopupOwner(object presentationSource)
        {
            lock (_gate)
            {
                return _popupOwners.TryGetValue(presentationSource, out IPortablePopupServiceRegistrar? owner)
                    ? owner
                    : null;
            }
        }

        private IPortablePopupServiceRegistrar[] GetServicesNewestFirst()
        {
            lock (_gate)
            {
                IPortablePopupServiceRegistrar[] services = _services.ToArray();
                Array.Reverse(services);
                return services;
            }
        }
    }

    private sealed class SystemThemeSourceRegistration : IDisposable
    {
        private IPortableSystemThemeSource? _source;

        public SystemThemeSourceRegistration(IPortableSystemThemeSource source)
        {
            _source = source;
        }

        public void Dispose()
        {
            IPortableSystemThemeSource? source = Interlocked.Exchange(ref _source, null);
            if (source is null)
            {
                return;
            }

            bool removed = false;
            lock (SyncRoot)
            {
                if (SystemThemeSources.TryGetValue(source.ServiceKey, out IPortableSystemThemeSource? current) &&
                    ReferenceEquals(current, source))
                {
                    source.SystemThemeChanged -= OnSystemThemeSourceChanged;
                    SystemThemeSources.Remove(source.ServiceKey);
                    removed = true;
                }
            }

            if (removed)
            {
                SystemThemeChanged?.Invoke(source, EventArgs.Empty);
            }
        }
    }

    private sealed class DisplayMetricsSourceRegistration : IDisposable
    {
        private IPortableDisplayMetricsSource? _source;

        public DisplayMetricsSourceRegistration(IPortableDisplayMetricsSource source)
        {
            _source = source;
        }

        public void Dispose()
        {
            IPortableDisplayMetricsSource? source = Interlocked.Exchange(ref _source, null);
            if (source is null)
            {
                return;
            }

            bool removed = false;
            lock (SyncRoot)
            {
                if (DisplayMetricsSources.TryGetValue(source.ServiceKey, out IPortableDisplayMetricsSource? current) &&
                    ReferenceEquals(current, source))
                {
                    source.DisplayMetricsChanged -= OnDisplayMetricsSourceChanged;
                    DisplayMetricsSources.Remove(source.ServiceKey);
                    removed = true;
                }
            }

            if (removed)
            {
                DisplayMetricsChanged?.Invoke(source, EventArgs.Empty);
            }
        }
    }
}
