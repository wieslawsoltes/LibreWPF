using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.ProGPU;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace LinuxMessageBoxCaption;

public partial class App : Application
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Window _owner = null!;
    private DispatcherTimer _timer = null!;
    private string _output = null!, _run = null!;
    private int _index = -1;
    private Window? _observed;
    private Exception? _failure;
    private readonly List<object> _results = new();
    private static readonly MessageBoxButton[] Buttons =
        { MessageBoxButton.OK, MessageBoxButton.OKCancel, MessageBoxButton.YesNo, MessageBoxButton.YesNoCancel };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!OperatingSystem.IsLinux() || e.Args.Length != 2 ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF" ||
            !Guid.TryParseExact(e.Args[1], "N", out _))
            throw new InvalidOperationException("Owned hosted Linux observation only.");
        _output = Path.GetFullPath(e.Args[0]); _run = e.Args[1];
        if (_output != e.Args[0] || Directory.Exists(_output) || File.Exists(_output) ||
            !Directory.Exists(Path.GetDirectoryName(_output)))
            throw new InvalidOperationException("Fresh absolute evidence directory required.");
        Directory.CreateDirectory(_output);
        _owner = new Window { Title = "Caption owner " + _run, Width = 760, Height = 520,
            Left = 80, Top = 80, Content = new TextBlock { Text = "Read-only MessageBox caption observation" } };
        MainWindow = _owner;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Observe();
        _owner.ContentRendered += (_, _) => Dispatcher.BeginInvoke(new Action(ObserveCaptions));
        _timer.Start();
        _owner.Show();
    }

    private void ObserveCaptions()
    {
        if (_index != -1) return;
        try
        {
            for (_index = 0; _index < Buttons.Length; ++_index)
            {
                _observed = null;
                MessageBoxResult result = MessageBox.Show(_owner,
                    "Actual source-created MessageBox. No synthetic keyboard or pointer input is sent.",
                    Caption, Buttons[_index], MessageBoxImage.None);
                if (_failure is not null) throw new InvalidOperationException("Caption observation failed.", _failure);
                if (_observed is null || _observed.IsVisible)
                    throw new InvalidOperationException("Observed dialog did not retire normally.");
                _results.Add(new { buttons = Buttons[_index].ToString(), result = result.ToString(),
                    ownerVisible = _owner.IsVisible, ownerEnabled = _owner.IsEnabled });
            }
            _timer.Stop();
            Write("complete.json", new { completed = true, qualified = false, pid = Environment.ProcessId,
                inputEventsSent = 0, elapsedSeconds = _clock.Elapsed.TotalSeconds, cases = _results,
                limitation = "Caption observation only. Programmatic dismissal is not native input/modality/taskbar qualification." });
            _owner.Close();
            Shutdown(0);
        }
        catch (Exception error)
        {
            _timer.Stop();
            Write("failure.json", new { error = error.ToString(), elapsedSeconds = _clock.Elapsed.TotalSeconds });
            Shutdown(1);
        }
    }

    private string Caption => "MessageBox " + _run + " " + Buttons[_index];

    private void Observe()
    {
        try
        {
            if (_clock.Elapsed.TotalSeconds > 55) throw new TimeoutException("55-second application budget exhausted.");
            if (_index < 0 || _index >= Buttons.Length) return;
            if (_observed is not null)
            {
                string acknowledgement = Path.Combine(_output, $"capture-{_index}.ack");
                if (File.Exists(acknowledgement))
                {
                    if (File.ReadAllText(acknowledgement) != _run)
                        throw new InvalidOperationException("Foreign capture acknowledgement.");
                    if (_observed.Owner != _owner || _observed.Title != Caption)
                        throw new InvalidOperationException("Dialog identity changed before normal source close.");
                    _observed.Close();
                }
                return;
            }
            Window[] candidates = Windows.Cast<Window>().Where(w => w.IsVisible && w.Title == Caption).ToArray();
            if (candidates.Length == 0) return;
            if (candidates.Length != 1 || candidates[0].Owner != _owner)
                throw new InvalidOperationException("Ambiguous source dialog ownership.");
            Window dialog = candidates[0];
            if (!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(dialog, out var snapshots) || snapshots.Length != 1)
                return;
            var value = snapshots[0];
            if (!value.IsVisible || value.PresentedFrameCount <= 0) return;
            if (value.RendererMode != ProGpuWpfRendererMode.NativeMilWgpu || !value.NativeMilHitTestingEnabled ||
                value.NativeWindow.Kind.ToString() != "X11" || value.NativeWindow.Handle == 0 ||
                dialog.ResizeMode != ResizeMode.NoResize)
                throw new InvalidOperationException("Unexpected actual source/native caption contract.");
            if (!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(_owner, out var ownerSnapshots) || ownerSnapshots.Length != 1)
                throw new InvalidOperationException("Original owner native identity unavailable.");
            _observed = dialog;
            Write($"ready-{_index}.json", new { pid = Environment.ProcessId, run = _run, index = _index,
                title = Caption, buttons = Buttons[_index].ToString(), nativeHandle = value.NativeWindow.Handle.ToInt64(),
                presentedFrames = value.PresentedFrameCount, resizeMode = dialog.ResizeMode.ToString(),
                showInTaskbar = dialog.ShowInTaskbar, owner = _owner.Title, ownerHandle = ownerSnapshots[0].NativeWindow.Handle.ToInt64(), ownerPresentedFrames = ownerSnapshots[0].PresentedFrameCount, sourceEnabled = dialog.IsEnabled,
                modalInputAllowed = PortableModalInputScope.AllowsInput(dialog),
                elapsedSeconds = _clock.Elapsed.TotalSeconds, inputEventsSent = 0 });
        }
        catch (Exception error)
        {
            _failure = error;
            _timer.Stop();
            Write("observer-failure.json", new { error = error.ToString(), elapsedSeconds = _clock.Elapsed.TotalSeconds });
            Shutdown(1);
        }
    }

    private void Write(string name, object value)
    {
        string destination = Path.Combine(_output, name);
        string pending = destination + ".pending";
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
        File.Move(pending, destination);
    }
}
