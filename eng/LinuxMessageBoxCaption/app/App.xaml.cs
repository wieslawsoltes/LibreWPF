using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace LinuxMessageBoxCaption;

// Separate input observer. The original four-caption, no-input source remains immutable.
public partial class App : Application
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<object> _results = new();
    private readonly MessageBoxButton[] _buttons = { MessageBoxButton.OK, MessageBoxButton.OKCancel,
        MessageBoxButton.YesNo, MessageBoxButton.YesNoCancel };
    private readonly string[] _expected = { "OK", "Cancel", "No", "Cancel" };
    private Window _owner = null!;
    private Button _guard = null!;
    private DispatcherTimer _timer = null!;
    private string _output = null!, _run = null!, _renderer = null!;
    private string _phase = "owner";
    private int _index = -1, _sequence, _guardClicks, _ownerDown, _ownerUp, _ownerKeys;
    private Window? _dialog;
    private bool _opening;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!OperatingSystem.IsLinux() || e.Args.Length != 3 ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF" ||
            !Guid.TryParseExact(e.Args[1], "N", out _) ||
            (e.Args[2] != "NativeMilWgpu" && e.Args[2] != "ManagedPortable"))
            throw new InvalidOperationException("Owned hosted Linux observer only.");
        _output = Path.GetFullPath(e.Args[0]); _run = e.Args[1]; _renderer = e.Args[2];
        if (_output != e.Args[0] || Directory.Exists(_output) || File.Exists(_output) ||
            !Directory.Exists(Path.GetDirectoryName(_output)))
            throw new InvalidOperationException("Fresh absolute evidence directory required.");
        Directory.CreateDirectory(_output);
        _guard = new Button { Content = "Owner input guard", Width = 160, Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 48, 0, 0) };
        _guard.Click += (_, _) =>
        {
            if (_phase != "owner" || _opening) throw new InvalidOperationException("Modal owner accepted a click.");
            _guardClicks++;
            if (_index == 3) { Finish(); return; }
            _opening = true;
            Dispatcher.BeginInvoke(new Action(OpenNext));
        };
        _owner = new Window { Title = "Caption owner " + _run, Width = 760, Height = 520,
            Left = 80, Top = 80, Content = _guard };
        _owner.PreviewMouseDown += (_, _) => _ownerDown++;
        _owner.PreviewMouseUp += (_, _) => _ownerUp++;
        _owner.PreviewKeyDown += (_, _) => _ownerKeys++;
        MainWindow = _owner;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Observe();
        _timer.Start(); _owner.Show();
    }

    private string Caption => "MessageBox " + _run + " " + _buttons[_index];

    private void OpenNext()
    {
        try
        {
            ++_index; _phase = "modal"; _dialog = null;
            MessageBoxResult result = MessageBox.Show(_owner,
                "Actual source-created MessageBox. Bounded native caption and owner-input acceptance.",
                Caption, _buttons[_index], MessageBoxImage.None);
            if (_dialog is null || _dialog.IsVisible || result.ToString() != _expected[_index])
                throw new InvalidOperationException("Original response or dialog retirement failed.");
            if (!_owner.IsVisible || !_owner.IsEnabled || !PortableModalInputScope.AllowsInput(_owner))
                throw new InvalidOperationException("Original owner source admission was not restored.");
            _results.Add(new { index = _index, buttons = _buttons[_index].ToString(), result = result.ToString(),
                ownerVisible = _owner.IsVisible, ownerEnabled = _owner.IsEnabled,
                ownerInputAllowed = PortableModalInputScope.AllowsInput(_owner) });
            _phase = "owner"; _opening = false; Observe();
        }
        catch (Exception error) { Fail(error); }
    }

    private static object Bounds(FrameworkElement visual) => Bounds(visual, visual.ActualWidth, visual.ActualHeight);

    private static object Bounds(FrameworkElement visual, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new InvalidOperationException("Invalid original client extent.");
        Point a = visual.PointToScreen(new Point());
        Point b = visual.PointToScreen(new Point(width, height));
        if (!double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(b.X) || !double.IsFinite(b.Y) ||
            b.X <= a.X || b.Y <= a.Y) throw new InvalidOperationException("Invalid original target bounds.");
        return new { left = a.X, top = a.Y, right = b.X, bottom = b.Y };
    }

    private object Surface(Window window)
    {
        if (!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(window, out var values) || values.Length != 1)
            throw new InvalidOperationException("Actual native owner snapshot unavailable.");
        var value = values[0];
        if (!value.IsVisible || value.PresentedFrameCount <= 0 || value.NativeWindow.Kind.ToString() != "X11" ||
            value.NativeWindow.Handle == 0 || value.RendererMode.ToString() != _renderer ||
            (_renderer == "NativeMilWgpu" && !value.NativeMilHitTestingEnabled))
            throw new InvalidOperationException("Source/native renderer identity is unproven.");
        var dpi = VisualTreeHelper.GetDpi(window);
        if (!ProGpuWpfDiagnostics.TryGetRenderSurfaceGeometry(window, out var geometry) ||
            geometry.LogicalWidth == 0 || geometry.LogicalHeight == 0 ||
            geometry.DpiScaleX != dpi.DpiScaleX || geometry.DpiScaleY != dpi.DpiScaleY)
            throw new InvalidOperationException("Actual source client geometry is unavailable or unsynchronized.");
        // Window.ActualWidth/ActualHeight include the native frame. Keep that
        // observation separately; the source renderer owns the client extent.
        return new { nativeHandle = value.NativeWindow.Handle.ToInt64(), title = window.Title,
            presentedFrames = value.PresentedFrameCount, renderer = value.RendererMode.ToString(),
            sourceEnabled = window.IsEnabled, sourceActive = window.IsActive,
            inputAllowed = PortableModalInputScope.AllowsInput(window),
            sourceWidth = window.Width, sourceHeight = window.Height,
            outerVisualWidth = window.ActualWidth, outerVisualHeight = window.ActualHeight,
            outerVisualBounds = Bounds(window), sourceClientGeometry = geometry,
            clientWidth = geometry.LogicalWidth, clientHeight = geometry.LogicalHeight,
            dpiX = dpi.DpiScaleX, dpiY = dpi.DpiScaleY,
            client = Bounds(window, geometry.LogicalWidth, geometry.LogicalHeight) };
    }

    private void Observe()
    {
        try
        {
            if (_clock.Elapsed.TotalSeconds > 55) throw new TimeoutException("55-second source deadline exhausted.");
            if (!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(_owner, out var ownerValues) ||
                ownerValues.Length != 1 || ownerValues[0].PresentedFrameCount <= 0) return;
            object? dialog = null;
            if (_phase == "modal")
            {
                var candidates = Windows.Cast<Window>().Where(w => w.IsVisible && w.Title == Caption).ToArray();
                if (candidates.Length == 0) return;
                if (candidates.Length != 1 || candidates[0].Owner != _owner)
                    throw new InvalidOperationException("Source dialog ownership changed.");
                _dialog = candidates[0];
                if (!ProGpuWpfDiagnostics.TryGetDesktopWindowSnapshots(_dialog, out var values) ||
                    values.Length != 1 || values[0].PresentedFrameCount <= 0) return;
                var captions = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(_dialog))
                    .Select(i => VisualTreeHelper.GetChild(_dialog, i))
                    .Where(v => v.GetType().FullName == "System.Windows.PortableFixedWindowCaption").Cast<Grid>().ToArray();
                if (captions.Length != 1 || _dialog.ResizeMode != ResizeMode.NoResize)
                    throw new InvalidOperationException("Original fixed source caption missing.");
                var caption = captions[0];
                var close = caption.Children.OfType<Button>().Single();
                var title = caption.Children.OfType<TextBlock>().Single();
                if (caption.Children.Count != 2 || title.Text != Caption || !caption.IsVisible ||
                    AutomationProperties.GetAutomationId(close) != "PortableWindowClose" ||
                    close.IsEnabled != (_buttons[_index] != MessageBoxButton.YesNo))
                    throw new InvalidOperationException("Actual caption response/title contract changed.");
                var responses = (StackPanel)((Grid)_dialog.Content).Children[1];
                dialog = new { surface = Surface(_dialog), buttons = _buttons[_index].ToString(),
                    caption = Bounds(caption), dragTarget = Bounds(title), closeTarget = Bounds(close),
                    closeEnabled = close.IsEnabled, responseTargets = responses.Children.OfType<Button>()
                        .Select(b => new { content = b.Content?.ToString(), bounds = Bounds(b) }).ToArray() };
            }
            var snapshot = new { schema = "caption-native-input-observer-v1", pid = Environment.ProcessId,
                run = _run, sequence = ++_sequence, phase = _phase, index = _index,
                elapsedSeconds = _clock.Elapsed.TotalSeconds, owner = Surface(_owner), dialog,
                ownerInput = new { clicks = _guardClicks, down = _ownerDown, up = _ownerUp, keys = _ownerKeys },
                ownerTarget = Bounds(_guard) };
            Write($"snapshot-{_sequence:D6}.json", snapshot);
            string pending = Path.Combine(_output, "latest.pending");
            File.WriteAllText(pending, JsonSerializer.Serialize(snapshot));
            File.Move(pending, Path.Combine(_output, "latest.json"), true);
        }
        catch (Exception error) { Fail(error); }
    }

    private void Finish()
    {
        _timer.Stop();
        if (_clock.Elapsed.TotalSeconds > 55) { Fail(new TimeoutException("55-second source deadline exhausted at completion.")); return; }
        if (_results.Count != 4 || _guardClicks != 5) { Fail(new InvalidOperationException("Incomplete source interactions.")); return; }
        Write("complete.json", new { completed = true, qualified = false, pid = Environment.ProcessId,
            run = _run, elapsedSeconds = _clock.Elapsed.TotalSeconds, cases = _results,
            ownerClicks = _guardClicks, limitation = "Native controller, captures and package identity remain independent required gates." });
        _owner.Close(); Shutdown(0);
    }

    private void Fail(Exception error)
    {
        _timer?.Stop();
        string path = Path.Combine(_output, "failure.json");
        if (!File.Exists(path)) Write("failure.json", new { error = error.ToString(), elapsedSeconds = _clock.Elapsed.TotalSeconds });
        Shutdown(1);
    }

    private void Write(string name, object value)
    {
        using var stream = new FileStream(Path.Combine(_output, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }
}
