using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProGPU.Backend;

namespace System.Windows.Media.ProGPU.Platform;

public interface IWpfPlatformServices
{
    IWpfClipboard Clipboard { get; }

    IWpfColorDialogService ColorDialogs { get; }

    IWpfCursorService Cursors { get; }

    IWpfDispatcherService Dispatcher { get; }

    IWpfDragDropService DragDrop { get; }

    IWpfFileDialogService FileDialogs { get; }

    IWpfFontDialogService FontDialogs { get; }

    IWpfInputService Input { get; }

    IWpfLauncher Launcher { get; }

    IWpfMessageBoxService MessageBoxes { get; }

    IWpfMonitorService Monitors { get; }

    IWpfTimerService Timers { get; }

    IWpfWindowDecorationService WindowDecorations { get; }

    IWpfWindowEventService WindowEvents { get; }
}

public interface IWpfClipboard
{
    ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default);

    ValueTask SetTextAsync(string? text, CancellationToken cancellationToken = default);
}

public interface IWpfColorDialogService
{
    int? Show(WpfColorDialogOptions options);
}

public interface IWpfCursorService
{
    bool SetCursor(object inputSource, WpfCursor cursor);
}

public interface IWpfDispatcherService
{
    event EventHandler? WorkAvailable;

    bool CheckAccess();

    IWpfDispatcherOperation Post(Action callback, WpfDispatcherPriority priority = WpfDispatcherPriority.Normal);

    bool ProcessPending();
}

public interface IWpfDispatcherOperation : IDisposable
{
    WpfDispatcherPriority Priority { get; }

    bool IsCanceled { get; }

    bool IsCompleted { get; }

    bool Cancel();
}

public interface IWpfDragDropService
{
    event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    IDisposable Attach(object window);
}

public interface IWpfFileDialogService
{
    ValueTask<string?> OpenFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default);

    async ValueTask<string[]?> OpenFilesAsync(
        WpfFileDialogOptions options,
        CancellationToken cancellationToken = default)
    {
        string? selectedPath = await OpenFileAsync(options, cancellationToken).ConfigureAwait(false);
        return selectedPath == null ? null : [selectedPath];
    }

    ValueTask<string?> SaveFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default);

    ValueTask<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    async ValueTask<string[]?> PickFoldersAsync(
        WpfFileDialogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? selectedPath = await PickFolderAsync(cancellationToken).ConfigureAwait(false);
        return selectedPath == null ? null : [selectedPath];
    }
}

public interface IWpfFontDialogService
{
    WpfFontDialogResult? Show(WpfFontDialogOptions options);
}

public interface IWpfLauncher
{
    ValueTask OpenUriAsync(Uri uri, CancellationToken cancellationToken = default);

    ValueTask OpenFileAsync(string path, CancellationToken cancellationToken = default);
}

public interface IWpfMessageBoxService
{
    string Show(WpfMessageBoxOptions options);
}

public interface IWpfInputService
{
    event EventHandler<WpfInputEventArgs>? InputReceived;

    IDisposable Attach(object window);
}

public interface IWpfMonitorService
{
    IReadOnlyList<WpfMonitorInfo> GetMonitors();
}

public interface IWpfTimerService
{
    IWpfTimer CreateTimer(TimeSpan interval, Action callback, bool isRepeating = true);
}

public interface IWpfTimer : IDisposable
{
    TimeSpan Interval { get; }

    bool IsEnabled { get; }

    void Start();

    void Stop();
}

public interface IWpfWindowDecorationService
{
    bool TryBeginDragMove(object window);

    void TrackDragMoveInput(object window, WpfInputEventArgs input)
    {
    }

    bool TryContinueDragMove(object window, WpfInputEventArgs input)
    {
        return false;
    }

    void EndDragMove(object window)
    {
    }

    bool TryActivate(object window)
    {
        return false;
    }

    bool TryShowWithoutActivation(object window)
    {
        return false;
    }

    bool TryConfigurePopupOwner(object ownerWindow, object popupWindow)
    {
        return false;
    }

    // Applied after the popup is ordered on screen: AppKit recomputes a window's
    // shadow when it is shown, so clearing it at creation time does not stick.
    bool TryDisablePopupShadow(object popupWindow)
    {
        return false;
    }

    // A transparent framebuffer only gives the drawing surface an alpha channel; the native
    // window still composites its own opaque backdrop underneath, so fully transparent pixels
    // reveal that backdrop instead of what is behind the window. Call this for windows created
    // with TransparentFramebuffer to clear the native backdrop as well.
    bool TryEnableTransparentBackground(object window)
    {
        return false;
    }
}

public interface IWpfWindowEventService
{
    event EventHandler<WpfWindowEventArgs>? WindowEventReceived;

    IDisposable Attach(object window);
}

public sealed class WpfFileDialogOptions
{
    public string? Title { get; set; }

    public string? SuggestedFileName { get; set; }

    public IReadOnlyList<string> FileTypePatterns { get; set; } = Array.Empty<string>();

    public bool AllowMultipleSelection { get; set; }
}

public sealed class WpfMessageBoxOptions
{
    public object? Owner { get; set; }

    /// <summary>
    /// Gets or sets an explicit native owner when the managed <see cref="Owner"/> cannot be
    /// resolved to an active ProGPU window. Win32 and X11 process-backed dialogs can use this
    /// handle for native modal ownership; Cocoa and Wayland process-backed dialogs currently
    /// leave the dialog unparented because their native handles cannot be safely passed to a
    /// separate dialog process.
    /// </summary>
    public NativeWindowHandle OwnerNativeHandle { get; set; } = NativeWindowHandle.Empty;

    public string MessageBoxText { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;

    public string Button { get; set; } = "OK";

    public string Icon { get; set; } = "None";

    public string DefaultResult { get; set; } = "None";

    public string Options { get; set; } = "None";

    public string FallbackResult { get; set; } = "OK";
}

public sealed class WpfColorDialogOptions
{
    public int InitialArgb { get; set; } = unchecked((int)0xFF000000);

    public IReadOnlyList<int> CustomColors { get; set; } = Array.Empty<int>();
}

public sealed class WpfFontDialogOptions
{
    public string FamilyName { get; set; } = "Courier New";

    public float Size { get; set; } = 10f;

    public int Style { get; set; }

    public string Unit { get; set; } = "Point";

    public bool ShowEffects { get; set; } = true;

    public bool ShowColor { get; set; }

    public int MinSize { get; set; }

    public int MaxSize { get; set; }
}

public sealed class WpfFontDialogResult
{
    public WpfFontDialogResult(string familyName, float size, int style, string unit)
    {
        FamilyName = familyName;
        Size = size;
        Style = style;
        Unit = unit;
    }

    public string FamilyName { get; }

    public float Size { get; }

    public int Style { get; }

    public string Unit { get; }
}

public enum WpfCursor
{
    Default,
    Arrow,
    IBeam,
    Crosshair,
    Hand,
    SizeWE,
    SizeNS,
    SizeNWSE,
    SizeNESW,
    SizeAll,
    No,
    Wait,
    AppStarting
}

public enum WpfDispatcherPriority
{
    Inactive = 0,
    SystemIdle = 1,
    ApplicationIdle = 2,
    ContextIdle = 3,
    Background = 4,
    Input = 5,
    Loaded = 6,
    Render = 7,
    DataBind = 8,
    Normal = 9,
    Send = 10
}

[Flags]
public enum WpfDragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4
}

public enum WpfDragDropEventKind
{
    Drop = 0,
    DragEnter = 1,
    DragOver = 2,
    DragLeave = 3
}

public sealed class WpfDragDropData
{
    public WpfDragDropData(IReadOnlyList<string>? files = null, string? text = null)
    {
        Files = files ?? Array.Empty<string>();
        Text = text;
    }

    public IReadOnlyList<string> Files { get; }

    public string? Text { get; }

    public bool ContainsFiles => Files.Count > 0;

    public bool ContainsText => !string.IsNullOrEmpty(Text);
}

public sealed class WpfDragDropEventArgs : EventArgs
{
    public WpfDragDropEventArgs(
        WpfDragDropEventKind kind,
        WpfDragDropData data,
        WpfDragDropEffects allowedEffects = WpfDragDropEffects.Copy,
        WpfDragDropEffects acceptedEffect = WpfDragDropEffects.None,
        double x = 0,
        double y = 0)
    {
        ArgumentNullException.ThrowIfNull(data);

        Kind = kind;
        Data = data;
        AllowedEffects = allowedEffects;
        AcceptedEffect = acceptedEffect;
        X = x;
        Y = y;
    }

    public WpfDragDropEventKind Kind { get; }

    public WpfDragDropData Data { get; }

    public WpfDragDropEffects AllowedEffects { get; }

    public WpfDragDropEffects AcceptedEffect { get; set; }

    public double X { get; }

    public double Y { get; }
}

public readonly record struct WpfMonitorInfo(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    double DpiScale,
    bool IsPrimary)
{
    public int WorkAreaX { get; init; } = X;

    public int WorkAreaY { get; init; } = Y;

    public int WorkAreaWidth { get; init; } = Width;

    public int WorkAreaHeight { get; init; } = Height;

    public bool UsesLogicalCoordinates { get; init; }
}

public enum WpfWindowEventKind
{
    Activated,
    Deactivated,
    FilesDropped,
    Shown,
    Hidden,
    WindowPositionChanging,
    WindowPositionChanged,
    WindowSizeChanged,
    NonClientMouseMove,
    NonClientMouseDown,
    NonClientMouseUp,
    NonClientMouseDoubleClick
}

public sealed class WpfWindowEventArgs : EventArgs
{
    public WpfWindowEventArgs(
        WpfWindowEventKind kind,
        IReadOnlyList<string>? files = null,
        int? left = null,
        int? top = null,
        int? width = null,
        int? height = null,
        WpfMouseButton button = WpfMouseButton.None,
        int hitTestCode = 0,
        int? screenX = null,
        int? screenY = null)
    {
        Kind = kind;
        Files = files ?? Array.Empty<string>();
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Button = button;
        HitTestCode = hitTestCode;
        ScreenX = screenX;
        ScreenY = screenY;
    }

    public WpfWindowEventKind Kind { get; }

    public IReadOnlyList<string> Files { get; }

    public int? Left { get; }

    public int? Top { get; }

    public int? Width { get; }

    public int? Height { get; }

    public WpfMouseButton Button { get; }

    public int HitTestCode { get; }

    public int? ScreenX { get; }

    public int? ScreenY { get; }
}

public enum WpfInputEventKind
{
    KeyDown,
    KeyUp,
    TextInput,
    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel
}

public enum WpfMouseButton
{
    None,
    Left,
    Right,
    Middle,
    XButton1,
    XButton2,
    Other
}

[Flags]
public enum WpfInputModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Super = 8
}

public sealed class WpfInputEventArgs : EventArgs
{
    public WpfInputEventArgs(
        WpfInputEventKind kind,
        string? key = null,
        int scanCode = 0,
        char? character = null,
        double x = 0,
        double y = 0,
        double deltaX = 0,
        double deltaY = 0,
        WpfMouseButton button = WpfMouseButton.None,
        WpfInputModifiers modifiers = WpfInputModifiers.None)
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

    public WpfInputEventKind Kind { get; }

    public string? Key { get; }

    public int ScanCode { get; }

    public char? Character { get; }

    public double X { get; }

    public double Y { get; }

    public double DeltaX { get; }

    public double DeltaY { get; }

    public WpfMouseButton Button { get; }

    public WpfInputModifiers Modifiers { get; }

    public bool Handled { get; set; }
}
