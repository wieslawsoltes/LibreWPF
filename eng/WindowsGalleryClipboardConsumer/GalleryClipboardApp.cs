// Root-only diagnostic host. Original Gallery sources/resources remain unchanged.
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows.Media.ProGPU;
using WPFGallery.Views;
using WPFGallery.ViewModels;

internal static class GalleryClipboardApp
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern int CountClipboardFormats();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);

    [STAThread]
    private static int Main(string[] args)
    {
        Stopwatch clock = Stopwatch.StartNew();
        if (!OperatingSystem.IsWindows() || args.Length != 2 ||
            (args[0] != "--allow-replace-clipboard" && args[0] != "--require-empty-ci-clipboard"))
            throw new InvalidOperationException("Actual Windows and explicit permission to replace existing clipboard contents are required; there is no all-format restore claim.");
        bool requireEmptyCi = args[0] == "--require-empty-ci-clipboard";
        if (requireEmptyCi && (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            Environment.GetEnvironmentVariable("RUNNER_OS") != "Windows" ||
            Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF"))
            throw new InvalidOperationException("Empty CI clipboard mode requires the LibreWPF GitHub-hosted Windows runner.");
        string output = Path.GetFullPath(args[1]);
        if (!Directory.Exists(output)) throw new DirectoryNotFoundException(output);
        if (!OpenClipboard(0)) throw new InvalidOperationException("Clipboard unavailable; existing contents were not inspected.");
        bool previouslyNonempty;
        uint initialSequence;
        try
        {
            Marshal.SetLastPInvokeError(0);
            int formats = CountClipboardFormats();
            if (formats == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new InvalidOperationException("Clipboard format-count query failed.");
            previouslyNonempty = formats != 0;
            initialSequence = GetClipboardSequenceNumber();
            if (requireEmptyCi && previouslyNonempty)
                throw new InvalidOperationException("CI clipboard is nonempty; no contents were read or replaced.");
        }
        finally { if (!CloseClipboard()) throw new InvalidOperationException("Clipboard preflight release failed."); }

        Application application = new();
        foreach (string resource in new[] {
            "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
            "Controls/ControlExample.xaml", "Controls/PageHeader.xaml" })
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(resource, UriKind.RelativeOrAbsolute) });
        ClipboardPage page = new(new ClipboardPageViewModel());
        Window window = new() { Title = "Gallery clipboard image qualification", Width = 900, Height = 900, Content = page };
        int phase = 0;
        long afterFrame = -1;
        long beforeScene = -1, beforeRetained = -1, beforeFlat = -1;
        uint ownedSequence = 0;
        BitmapSource? retained = null;
        byte[]? expected = null;
        bool completed = false;
        bool activationTargetPublished = false;
        List<string> errors = new();
        DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };

        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void PrepareForeground(nint hwnd)
        {
            Require(GetWindowThreadProcessId(hwnd, out uint owner) != 0 && owner == Environment.ProcessId,
                "Foreground target does not belong to the original Gallery process.");
            Require(IsWindowVisible(hwnd) && !IsIconic(hwnd), "Foreground target is hidden or minimized.");
            Require(clock.Elapsed.TotalSeconds < 55, "Original host budget exhausted before foreground setup.");
            nint before = GetForegroundWindow();
            // The disposable CI driver clicks the actual native caption first.
            // A background application cannot grant itself foreground rights.
            bool requested = before != hwnd && !requireEmptyCi;
            bool accepted = before == hwnd || (requested && SetForegroundWindow(hwnd));
            nint after = GetForegroundWindow();
            GetWindowThreadProcessId(after, out uint foregroundPid);
            Save(output, "activation.json", new {
                pid = Environment.ProcessId, hwnd = hwnd.ToInt64(), before = before.ToInt64(), requested,
                accepted, after = after.ToInt64(), foregroundPid, elapsed = clock.Elapsed.TotalSeconds,
                caller = "owned Gallery application thread", requestCount = requested ? 1 : 0 });
            Require(accepted, "Windows rejected the Gallery application's foreground request.");
            Require(after == hwnd, "The Gallery application did not become foreground.");
        }
        Image NamedImage(string name) => page.FindName(name) as Image ?? throw new InvalidOperationException("Missing original image " + name);
        void Verify(BitmapSource image)
        {
            Require(image.PixelWidth == 72 && image.PixelHeight == 73, "Original PNG extent mismatch.");
            Require(expected is not null && expected.SequenceEqual(Rgb(image)), "Complete opaque CF_BITMAP RGB differs from the original PNG's Bgr32 conversion.");
        }
        void Owned() => Require(ownedSequence != 0 && GetClipboardSequenceNumber() == ownedSequence, "Clipboard sequence changed externally; do not overwrite or clear it.");
        void End(Exception? failure)
        {
            timer.Stop();
            if (failure is not null) errors.Add(failure.ToString());
            if (ownedSequence != 0)
            {
                try { Owned(); Clipboard.Clear(); ownedSequence = 0; }
                catch (Exception cleanup) { errors.Add("clipboard cleanup: " + cleanup); }
            }
            try { window.Close(); }
            catch (Exception cleanup) { errors.Add("window cleanup: " + cleanup); }
            Save(output, "host-result.json", new { completed, errors, previouslyNonempty, requireEmptyCi, initialSequence, elapsed = clock.Elapsed.TotalSeconds, clipboardRestored = false, qualification = "original Gallery action/source ownership; native captures admitted separately" });
            application.Shutdown(completed && errors.Count == 0 ? 0 : 1);
        }
        timer.Tick += (_, _) =>
        {
            try
            {
                Require(clock.Elapsed < TimeSpan.FromSeconds(87), "Approved host reserve reached inside outer90-second deadline.");
                if (!ProGpuWpfDiagnostics.TryGetWindowHost(window, out var host) || host is null || !host.HasPresentedFrame) return;
                Require(host.NativeWindowHandle.Kind == global::ProGPU.Backend.NativeWindowKind.Win32 && host.NativeWindowHandle.IsValid, "No real owned Win32 window.");
                if (phase == 0)
                {
                    if (requireEmptyCi)
                    {
                        if (!activationTargetPublished)
                        {
                            Save(output, "activation-target.json", new {
                                pid = Environment.ProcessId, hwnd = host.NativeWindowHandle.Handle.ToInt64(),
                                frame = host.PresentedFrameCount, elapsed = clock.Elapsed.TotalSeconds });
                            activationTargetPublished = true;
                        }
                        if (!File.Exists(Path.Combine(output, "activation-target.accepted"))) return;
                    }
                    PrepareForeground(host.NativeWindowHandle.Handle);
                    var initialFrame = host.LastPresentedFrameState;
                    beforeScene = initialFrame.SceneChangeVersion;
                    beforeRetained = initialFrame.RetainedWpfChangeVersion;
                    beforeFlat = initialFrame.FlatDrawingChangeVersion;
                    BitmapSource source = NamedImage("SourceImage").Source as BitmapSource ?? throw new InvalidOperationException("Original pack-resource image did not decode.");
                    expected = Rgb(source);
                    Require(source.PixelWidth == 72 && source.PixelHeight == 73, "Original PNG did not load.");
                    Require(!requireEmptyCi || GetClipboardSequenceNumber() == initialSequence,
                        "CI clipboard changed externally after the empty preflight; do not replace it.");
                    OriginalButton(page, "Copy Image to Clipboard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ownedSequence = GetClipboardSequenceNumber();
                    Owned();
                    Require(page.ViewModel.CopyImageStatus == "Image copied to clipboard!", "Original copy action failed.");
                    OriginalButton(page, "Paste Image from Clipboard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Image image = NamedImage("PastedImage");
                    retained = image.Source as BitmapSource ?? throw new InvalidOperationException("Original paste returned no bitmap.");
                    Verify(retained);
                    Require(image.Visibility == Visibility.Visible && page.ViewModel.PasteImageStatus == "Image pasted! Size: 72x73", "Original paste visibility/status failed.");
                    image.BringIntoView();
                    afterFrame = host.PresentedFrameCount;
                    phase = 1;
                }
                else if (phase == 1 || phase == 3)
                {
                    if (host.PresentedFrameCount <= afterFrame && phase == 1) return;
                    var presented = host.LastPresentedFrameState;
                    if (phase == 1 && presented.SceneChangeVersion <= beforeScene && presented.RetainedWpfChangeVersion <= beforeRetained && presented.FlatDrawingChangeVersion <= beforeFlat) return;
                    // A submitted frame can still be drawing. Keep the original
                    // deadline and capture exactly once after real GPU completion.
                    if (!ProGpuWpfDiagnostics.TryPollNativePresentationCheckpoint(window, out var completion)) return;
                    Require(completion.PresentedFrame.PresentedFrameCount == host.PresentedFrameCount,
                        "GPU completion belongs to another submitted frame.");
                    Image image = NamedImage("PastedImage");
                    Require(image.IsVisible && image.ActualWidth > 0 && image.ActualHeight > 0, "Pasted image is not visibly arranged.");
                    Point a = image.PointToScreen(new Point());
                    Point b = image.PointToScreen(new Point(image.ActualWidth, image.ActualHeight));
                    Point clientA = page.PointToScreen(new Point());
                    Point clientB = page.PointToScreen(new Point(page.ActualWidth, page.ActualHeight));
                    Save(output, phase == 1 ? "pasted.json" : "retained.json", new {
                        pid = Environment.ProcessId, hwnd = host.NativeWindowHandle.Handle.ToInt64(),
                        frame = host.PresentedFrameCount, image = new[] { a.X, a.Y, b.X - a.X, b.Y - a.Y },
                        gpuCompletion = new { frame = completion.PresentedFrame.PresentedFrameCount,
                            submission = completion.Submission.Value,
                            deviceRecovery = completion.PresentedFrame.DeviceRecoveryCount },
                        scene = presented.SceneChangeVersion, retained = presented.RetainedWpfChangeVersion, flat = presented.FlatDrawingChangeVersion,
                        client = new[] { clientA.X, clientA.Y, clientB.X - clientA.X, clientB.Y - clientA.Y },
                        rgbSha256 = Convert.ToHexString(SHA256.HashData(Rgb(retained!))),
                        sourceFormat = NamedImage("SourceImage").Source is BitmapSource s ? s.Format.ToString() : "missing",
                        elapsed = clock.Elapsed.TotalSeconds });
                    phase++;
                }
                else if (phase == 2 && File.Exists(Path.Combine(output, "pasted.capture-accepted")))
                {
                    Owned(); Clipboard.Clear(); ownedSequence = GetClipboardSequenceNumber();
                    Verify(retained!); retained!.Freeze();
                    phase = 3;
                }
                else if (phase == 4 && File.Exists(Path.Combine(output, "retained.capture-accepted")))
                {
                    Owned(); Clipboard.SetImage(retained!); ownedSequence = GetClipboardSequenceNumber();
                    OriginalButton(page, "Paste Image from Clipboard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Verify((BitmapSource)NamedImage("PastedImage").Source);
                    completed = true;
                    End(null);
                }
            }
            catch (Exception error) { End(error); }
        };
        window.Loaded += (_, _) => timer.Start();
        return application.Run(window);
    }

    private static byte[] Rgb(BitmapSource source)
    {
        FormatConvertedBitmap converted = new(source, PixelFormats.Bgr32, null, 0);
        byte[] bytes = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
        converted.CopyPixels(bytes, checked(source.PixelWidth * 4), 0);
        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 0; // CF_BITMAP has no alpha promise.
        return bytes;
    }

    private static Button OriginalButton(DependencyObject root, string content)
    {
        List<Button> matches = new();
        void Visit(DependencyObject node)
        {
            if (node is Button button && Equals(button.Content, content)) matches.Add(button);
            foreach (object child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject element) Visit(element);
            if (node is WPFGallery.Controls.ControlExample example && example.ExampleContent is DependencyObject exampleContent)
                Visit(exampleContent);
        }
        Visit(root);
        return matches.Distinct().Single();
    }

    private static void Save(string directory, string name, object value)
    {
        string path = Path.Combine(directory, name);
        using (FileStream file = new(path + ".pending", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
        File.Move(path + ".pending", path); // Publish complete receipt, never overwrite.
    }
}
