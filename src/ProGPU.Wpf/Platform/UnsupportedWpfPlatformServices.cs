using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class UnsupportedWpfPlatformServices : IWpfPlatformServices
{
    public static UnsupportedWpfPlatformServices Instance { get; } = new();

    public IWpfClipboard Clipboard { get; } = new UnsupportedClipboard();

    public IWpfColorDialogService ColorDialogs { get; } = new UnsupportedColorDialogService();

    public IWpfCursorService Cursors { get; } = new UnsupportedCursorService();

    public IWpfDispatcherService Dispatcher { get; } = new UnsupportedDispatcherService();

    public IWpfDragDropService DragDrop { get; } = new UnsupportedDragDropService();

    public IWpfFileDialogService FileDialogs { get; } = new UnsupportedFileDialogService();

    public IWpfFontDialogService FontDialogs { get; } = new UnsupportedFontDialogService();

    public IWpfInputService Input { get; } = new UnsupportedInputService();

    public IWpfLauncher Launcher { get; } = new UnsupportedLauncher();

    public IWpfMessageBoxService MessageBoxes { get; } = new UnsupportedMessageBoxService();

    public IWpfMonitorService Monitors { get; } = new UnsupportedMonitorService();

    public IWpfTimerService Timers { get; } = new UnsupportedTimerService();

    public IWpfWindowDecorationService WindowDecorations { get; } = new UnsupportedWindowDecorationService();

    public IWpfWindowEventService WindowEvents { get; } = new UnsupportedWindowEventService();

    private sealed class UnsupportedClipboard : IWpfClipboard
    {
        public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Clipboard services are not configured for this WPF ProGPU host.");
        }

        public ValueTask SetTextAsync(string? text, CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Clipboard services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedFileDialogService : IWpfFileDialogService
    {
        public ValueTask<string?> OpenFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("File dialog services are not configured for this WPF ProGPU host.");
        }

        public ValueTask<string?> SaveFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("File dialog services are not configured for this WPF ProGPU host.");
        }

        public ValueTask<string?> PickFolderAsync(CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Folder picker services are not configured for this WPF ProGPU host.");
        }

        public ValueTask<string[]?> PickFoldersAsync(
            WpfFileDialogOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Folder picker services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedColorDialogService : IWpfColorDialogService
    {
        public int? Show(WpfColorDialogOptions options)
        {
            throw new PlatformNotSupportedException("Color dialog services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedFontDialogService : IWpfFontDialogService
    {
        public WpfFontDialogResult? Show(WpfFontDialogOptions options)
        {
            throw new PlatformNotSupportedException("Font dialog services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedCursorService : IWpfCursorService
    {
        public bool SetCursor(object inputSource, WpfCursor cursor)
        {
            throw new PlatformNotSupportedException("Cursor services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedDispatcherService : IWpfDispatcherService
    {
        public event EventHandler? WorkAvailable
        {
            add { }
            remove { }
        }

        public bool CheckAccess()
        {
            throw new PlatformNotSupportedException("Dispatcher services are not configured for this WPF ProGPU host.");
        }

        public IWpfDispatcherOperation Post(Action callback, WpfDispatcherPriority priority = WpfDispatcherPriority.Normal)
        {
            throw new PlatformNotSupportedException("Dispatcher services are not configured for this WPF ProGPU host.");
        }

        public bool ProcessPending()
        {
            throw new PlatformNotSupportedException("Dispatcher services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedDragDropService : IWpfDragDropService
    {
        public event EventHandler<WpfDragDropEventArgs>? DragDropReceived
        {
            add { }
            remove { }
        }

        public IDisposable Attach(object window)
        {
            throw new PlatformNotSupportedException("Drag/drop services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedLauncher : IWpfLauncher
    {
        public ValueTask OpenUriAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Launcher services are not configured for this WPF ProGPU host.");
        }

        public ValueTask OpenFileAsync(string path, CancellationToken cancellationToken = default)
        {
            throw new PlatformNotSupportedException("Launcher services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedMessageBoxService : IWpfMessageBoxService
    {
        public string Show(WpfMessageBoxOptions options)
        {
            throw new PlatformNotSupportedException("Message box services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedInputService : IWpfInputService
    {
        public event EventHandler<WpfInputEventArgs>? InputReceived
        {
            add { }
            remove { }
        }

        public IDisposable Attach(object window)
        {
            throw new PlatformNotSupportedException("Input services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedMonitorService : IWpfMonitorService
    {
        public IReadOnlyList<WpfMonitorInfo> GetMonitors()
        {
            throw new PlatformNotSupportedException("Monitor services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedTimerService : IWpfTimerService
    {
        public IWpfTimer CreateTimer(TimeSpan interval, Action callback, bool isRepeating = true)
        {
            throw new PlatformNotSupportedException("Timer services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedWindowDecorationService : IWpfWindowDecorationService
    {
        public bool TryBeginDragMove(object window)
        {
            throw new PlatformNotSupportedException("Window decoration services are not configured for this WPF ProGPU host.");
        }
    }

    private sealed class UnsupportedWindowEventService : IWpfWindowEventService
    {
        public event EventHandler<WpfWindowEventArgs>? WindowEventReceived
        {
            add { }
            remove { }
        }

        public IDisposable Attach(object window)
        {
            throw new PlatformNotSupportedException("Window event services are not configured for this WPF ProGPU host.");
        }
    }
}
