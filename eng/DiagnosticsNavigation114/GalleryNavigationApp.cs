// Read-only observer around the original upstream Gallery page and handlers.
using System.IO;
using System.Text.Json;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Windows.Media.ProGPU;
using WPFGallery.Views;
using WPFGallery.ViewModels;

internal static class GalleryNavigationApp
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1) return 2;
        string output = Path.GetFullPath(args[0]);
        if (!Directory.Exists(output)) return 2;
        Application app = new();
        foreach (string resource in new[] {
            "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
            "Controls/ControlExample.xaml", "Controls/PageHeader.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(resource, UriKind.RelativeOrAbsolute) });
        NavigationWindowPage page = new(new NavigationWindowPageViewModel());
        Window root = new() { Title = "Original Gallery navigation acceptance", Width = 900, Height = 650, Content = page };
        Stopwatch clock = Stopwatch.StartNew();
        List<object> history = new();
        string previous = "";
        DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        void Save(string file, object value)
        {
            string target = Path.Combine(output, file);
            File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(target + ".tmp", target, true);
        }
        void Finish(Exception? error)
        {
            timer.Stop();
            Save("host-result.json", new { error = error?.ToString(), history, elapsed = clock.Elapsed.TotalSeconds, clipboardAccessed = false });
            foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
            app.Shutdown(error is null ? 0 : 1);
        }
        timer.Tick += (_, _) =>
        {
            try
            {
                if (clock.Elapsed.TotalSeconds > 180) throw new TimeoutException("Original-page observation ended at its 180-second bound.");
                var windows = app.Windows.Cast<Window>().Select(w =>
                {
                    ProGpuWpfDiagnostics.TryGetWindowHost(w, out var host);
                    var controls = Descendants(w).OfType<FrameworkElement>()
                        .Where(e => e.IsVisible && (e is Button || e is TextBlock))
                        .Select(e => {
                            Point a = e.PointToScreen(new Point());
                            Point b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
                            return new { type = e.GetType().Name, name = e.Name,
                                text = e is TextBlock t ? t.Text : (e as Button)?.Content?.ToString(),
                                command = (e as Button)?.Command?.ToString(), enabled = e.IsEnabled,
                                rect = new[] { a.X, a.Y, b.X-a.X, b.Y-a.Y } };
                        }).ToArray();
                    return new { title = w.Title, type = w.GetType().Name,
                        contentType = w.Content?.GetType().FullName,
                        source = (w as NavigationWindow)?.Source?.ToString(),
                        canGoBack = (w as NavigationWindow)?.CanGoBack,
                        canGoForward = (w as NavigationWindow)?.CanGoForward,
                        hwnd = host?.NativeWindowHandle.Handle.ToInt64(),
                        nativeKind = host?.NativeWindowHandle.Kind.ToString(),
                        presented = host?.HasPresentedFrame,
                        frames = host?.PresentedFrameCount, controls };
                }).ToArray();
                var state = new { pid = Environment.ProcessId, windows, elapsed = clock.Elapsed.TotalSeconds };
                Save("state.json", state);
                string key = string.Join("|", windows.Select(w => w.contentType + ":" + w.source + ":" + w.canGoBack + ":" + w.canGoForward));
                if (key != previous) { history.Add(state); Save("history.json", history); previous = key; }
                if (File.Exists(Path.Combine(output, "finish"))) Finish(null);
            }
            catch (Exception error) { Finish(error); }
        };
        root.Loaded += (_, _) => timer.Start();
        return app.Run(root);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
