using System;
using System.Collections.Generic;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class SilkNetWpfDragDropService : IWpfDragDropService
{
    public event EventHandler<WpfDragDropEventArgs>? DragDropReceived;

    public IDisposable Attach(object window)
    {
        if (window is not IWindow silkWindow)
        {
            throw new ArgumentException("Silk.NET drag/drop services require a Silk.NET window instance.", nameof(window));
        }

        return Attach(silkWindow);
    }

    public IDisposable Attach(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Action<string[]> fileDrop = files => OnDragDropReceived(window, CreateFileDropEvent(files));
        window.FileDrop += fileDrop;

        return new DragDropSubscription(window, fileDrop);
    }

    public static WpfDragDropEventArgs CreateFileDropEvent(IReadOnlyList<string>? files)
    {
        return new WpfDragDropEventArgs(
            WpfDragDropEventKind.Drop,
            new WpfDragDropData(files ?? Array.Empty<string>()),
            WpfDragDropEffects.Copy,
            WpfDragDropEffects.Copy);
    }

    private void OnDragDropReceived(IWindow window, WpfDragDropEventArgs args)
    {
        DragDropReceived?.Invoke(window, args);
    }

    private sealed class DragDropSubscription : IDisposable
    {
        private readonly IWindow _window;
        private readonly Action<string[]> _fileDrop;
        private bool _isDisposed;

        public DragDropSubscription(IWindow window, Action<string[]> fileDrop)
        {
            _window = window;
            _fileDrop = fileDrop;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _window.FileDrop -= _fileDrop;
            _isDisposed = true;
        }
    }
}
