using System;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class CrossPlatformWpfPlatformServices : IWpfPlatformServices
{
    public static CrossPlatformWpfPlatformServices Instance { get; } = new();

    public CrossPlatformWpfPlatformServices()
        : this(new ProcessWpfLauncher(), new SilkNetWpfMonitorService())
    {
    }

    public CrossPlatformWpfPlatformServices(IWpfLauncher launcher)
        : this(launcher, new SilkNetWpfMonitorService())
    {
    }

    public CrossPlatformWpfPlatformServices(IWpfLauncher launcher, IWpfMonitorService monitors)
        : this(
            launcher,
            monitors,
            new ProcessWpfClipboard(),
            new SilkNetWpfCursorService(),
            new QueuedWpfDispatcherService(),
            new ProcessWpfFileDialogService(),
            new SilkNetWpfInputService(),
            new ProcessWpfMessageBoxService(),
            new ThreadPoolWpfTimerService(),
            new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfFileDialogService fileDialogs)
        : this(launcher, monitors, clipboard, new SilkNetWpfCursorService(), new QueuedWpfDispatcherService(), fileDialogs, new SilkNetWpfInputService(), new ThreadPoolWpfTimerService(), new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfFileDialogService fileDialogs)
        : this(launcher, monitors, clipboard, cursors, new QueuedWpfDispatcherService(), fileDialogs, new SilkNetWpfInputService(), new ThreadPoolWpfTimerService(), new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs)
        : this(launcher, monitors, clipboard, cursors, dispatcher, fileDialogs, new SilkNetWpfInputService(), new ThreadPoolWpfTimerService(), new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input)
        : this(launcher, monitors, clipboard, new SilkNetWpfCursorService(), new QueuedWpfDispatcherService(), fileDialogs, input, new ThreadPoolWpfTimerService(), new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input)
        : this(launcher, monitors, clipboard, cursors, new QueuedWpfDispatcherService(), fileDialogs, input, new ThreadPoolWpfTimerService(), new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input)
        : this(launcher, monitors, clipboard, cursors, dispatcher, fileDialogs, input, new ThreadPoolWpfTimerService(), new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfTimerService timers)
        : this(launcher, monitors, clipboard, new SilkNetWpfCursorService(), new QueuedWpfDispatcherService(), fileDialogs, input, timers, new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfTimerService timers)
        : this(launcher, monitors, clipboard, cursors, new QueuedWpfDispatcherService(), fileDialogs, input, timers, new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfTimerService timers)
        : this(launcher, monitors, clipboard, cursors, dispatcher, fileDialogs, input, timers, new SilkNetWpfWindowEventService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfTimerService timers,
        IWpfWindowEventService windowEvents)
        : this(launcher, monitors, clipboard, cursors, dispatcher, fileDialogs, input, new ProcessWpfMessageBoxService(), timers, windowEvents, new SilkNetWpfWindowDecorationService(), new SilkNetWpfDragDropService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfMessageBoxService messageBoxes,
        IWpfTimerService timers,
        IWpfWindowEventService windowEvents)
        : this(launcher, monitors, clipboard, cursors, dispatcher, fileDialogs, input, messageBoxes, timers, windowEvents, new SilkNetWpfWindowDecorationService(), new SilkNetWpfDragDropService())
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfTimerService timers,
        IWpfWindowEventService windowEvents,
        IWpfDragDropService dragDrop)
        : this(launcher, monitors, clipboard, cursors, dispatcher, fileDialogs, input, new ProcessWpfMessageBoxService(), timers, windowEvents, new SilkNetWpfWindowDecorationService(), dragDrop)
    {
    }

    public CrossPlatformWpfPlatformServices(
        IWpfLauncher launcher,
        IWpfMonitorService monitors,
        IWpfClipboard clipboard,
        IWpfCursorService cursors,
        IWpfDispatcherService dispatcher,
        IWpfFileDialogService fileDialogs,
        IWpfInputService input,
        IWpfMessageBoxService messageBoxes,
        IWpfTimerService timers,
        IWpfWindowEventService windowEvents,
        IWpfWindowDecorationService windowDecorations,
        IWpfDragDropService dragDrop)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(cursors);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(fileDialogs);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(messageBoxes);
        ArgumentNullException.ThrowIfNull(timers);
        ArgumentNullException.ThrowIfNull(windowEvents);
        ArgumentNullException.ThrowIfNull(windowDecorations);
        ArgumentNullException.ThrowIfNull(dragDrop);

        Clipboard = clipboard;
        ColorDialogs = new ProcessWpfColorDialogService();
        Cursors = cursors;
        Dispatcher = dispatcher;
        DragDrop = dragDrop;
        FileDialogs = fileDialogs;
        FontDialogs = new ProcessWpfFontDialogService();
        Input = input;
        Launcher = launcher;
        MessageBoxes = messageBoxes;
        Monitors = monitors;
        Timers = timers;
        WindowDecorations = windowDecorations;
        WindowEvents = windowEvents;
    }

    public IWpfClipboard Clipboard { get; }

    public IWpfColorDialogService ColorDialogs { get; }

    public IWpfCursorService Cursors { get; }

    public IWpfDispatcherService Dispatcher { get; }

    public IWpfDragDropService DragDrop { get; }

    public IWpfFileDialogService FileDialogs { get; }

    public IWpfFontDialogService FontDialogs { get; }

    public IWpfInputService Input { get; }

    public IWpfLauncher Launcher { get; }

    public IWpfMessageBoxService MessageBoxes { get; }

    public IWpfMonitorService Monitors { get; }

    public IWpfTimerService Timers { get; }

    public IWpfWindowDecorationService WindowDecorations { get; }

    public IWpfWindowEventService WindowEvents { get; }
}
