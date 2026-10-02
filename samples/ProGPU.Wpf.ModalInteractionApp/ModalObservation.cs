// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
#if LIBREWPF_MODAL_PORTABLE
using System.Windows.Media.ProGPU;
#endif

namespace ProGPU.Wpf.ModalInteractionApp;

internal sealed class ModalObservation : IDisposable
{
    private readonly ModalWindow _owner;
    private readonly string _directory;
    private readonly string _run;
    private readonly bool _nativeModal;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private int _sequence;
    private bool _capturing;

    internal ModalObservation(ModalWindow owner, string directory, string run, bool nativeModal)
    {
        _owner = owner; _run = run; _nativeModal = nativeModal;
        _directory = Path.GetFullPath(directory);
        if (_directory != directory || Directory.Exists(directory) || File.Exists(directory))
            throw new ArgumentException("Evidence must be a new absolute owned directory.");
        string parent = Path.GetDirectoryName(directory) ?? throw new ArgumentException("Missing evidence parent.");
        for (var entry = new DirectoryInfo(parent); entry != null; entry = entry.Parent)
            if (!entry.Exists || (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Evidence parent must exist without symbolic links/reparse points.");
        Directory.CreateDirectory(directory);
        owner.Observer = this;
        _timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Snapshot();
        _timer.Start();
    }

    internal void Snapshot()
    {
        if (_capturing) throw new InvalidOperationException("Recursive modal observation.");
        _capturing = true;
        try
        {
            if (++_sequence > 650 || _clock.Elapsed > TimeSpan.FromSeconds(65))
                throw new TimeoutException("Original modal observer budget exhausted.");
            var targets = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in _owner.Targets)
                targets.Add(item.Key, item.Value.IsVisible ? Bounds(item.Value) : null);
            var windows = new List<object>();
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible) continue;
                var native = new List<object>();
#if LIBREWPF_MODAL_PORTABLE
                if (!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(window, out var snapshots))
                    throw new InvalidOperationException("Existing source/native desktop identities are unavailable.");
                foreach (var value in snapshots)
                {
                    if (value.RootVisual is not FrameworkElement root)
                        throw new InvalidOperationException("Desktop source root has no original layout frame.");
                    native.Add(new { sourceHandle = value.SourceHandle.ToInt64(),
                        nativeKind = value.NativeWindow.Kind.ToString(), nativeHandle = value.NativeWindow.Handle.ToInt64(),
                        visible = value.IsVisible, inputEnabled = value.InputEnabled,
                        presentedFrames = value.PresentedFrameCount,
                        client = Bounds(root is Window sourceWindow && sourceWindow.Content is FrameworkElement content ? content : root),
                        nativeGeometry = value.NativeGeometry is { } geometry ? new
                        {
                            contentView = geometry.ContentView.ToInt64(), windowNumber = geometry.CocoaWindowNumber,
                            content = new { x = geometry.ContentBounds.X, y = geometry.ContentBounds.Y,
                                width = geometry.ContentBounds.Width, height = geometry.ContentBounds.Height },
                            frame = new { x = geometry.FrameBounds.X, y = geometry.FrameBounds.Y,
                                width = geometry.FrameBounds.Width, height = geometry.FrameBounds.Height },
                            backingScale = geometry.BackingScale
                        } : null });
                }
#else
                nint handle = new WindowInteropHelper(window).Handle;
                if (handle == 0) throw new InvalidOperationException("Original visible window lacks an existing HWND.");
                native.Add(new { sourceHandle = handle.ToInt64(), nativeKind = "Win32", nativeHandle = handle.ToInt64(),
                    visible = window.IsVisible, inputEnabled = (bool?)null, presentedFrames = (long?)null,
                    client = Bounds((FrameworkElement)window.Content), nativeGeometry = (object?)null });
#endif
                windows.Add(new { title = window.Title, name = window.Name, visible = window.IsVisible,
                    active = window.IsActive, sourceIsEnabled = window.IsEnabled, surfaces = native });
            }
            Write(new { schema = "wpf-modal-desktop-v1", qualified = false, pid = Environment.ProcessId,
                run = _run, title = _owner.Title, sequence = _sequence, elapsedMs = _clock.ElapsedMilliseconds,
                nativeModalRequested = _nativeModal, phase = _owner.Phase, windows, targets,
                focus = ModalWindow.FocusName(), priorFocus = _owner.PriorFocus, returnedFocus = _owner.ReturnedFocus,
                ownerText = _owner.Editor.Text, comboSelection = _owner.Combo.SelectedIndex,
                popupOpen = _owner.Popup.IsOpen, contextOpen = _owner.Menu.IsOpen, comboOpen = _owner.Combo.IsDropDownOpen,
                tooltipOpen = (_owner.Targets["tooltip"].ToolTip as ToolTip)?.IsOpen ?? false,
                messageResult = _owner.MessageResult, dialogResult = _owner.DialogResultObserved,
                counts = new Dictionary<string, int>(_owner.Counts, StringComparer.Ordinal) });
        }
        finally { _capturing = false; }
    }

    private void Write(object value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 256 * 1024) throw new InvalidOperationException("Modal observation exceeds 256 KiB.");
        string pending = Path.Combine(_directory, "snapshot.pending");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
        File.Move(pending, Path.Combine(_directory, $"snapshot-{_sequence:D8}.json"));
    }

    private static object Bounds(FrameworkElement element)
    {
        Point first = element.PointToScreen(new Point());
        Point last = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        if (!double.IsFinite(first.X) || !double.IsFinite(first.Y) || !double.IsFinite(last.X) ||
            !double.IsFinite(last.Y) || last.X <= first.X || last.Y <= first.Y)
            throw new InvalidOperationException("Original source screen bounds unavailable.");
        return new { x = first.X, y = first.Y, width = last.X - first.X, height = last.Y - first.Y };
    }

    public void Dispose() => _timer.Stop();
}
