using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

// Independent Microsoft WPF reference, not the original modal acceptance app.
// No input injection, forced activation, or external-window mutation is used.
internal static class TaskbarOwnershipDiagnostic
{
    private const uint GwOwner = 4;
    private const int GwlStyle = -16, GwlExStyle = -20;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<object> Records = [];

    [STAThread]
    private static int Main()
    {
        Window? first = null, second = null, subject = null;
        Exception? failure = null;
        long subjectHandle = 0;
        try
        {
            first = MakeWindow("Taskbar reference owner A");
            second = MakeWindow("Taskbar reference owner B");
            subject = MakeWindow("Taskbar reference subject");
            first.Show(); second.Show(); subject.Show();
            subjectHandle = new WindowInteropHelper(subject).Handle;
            Observe("unowned-default", subject);
            subject.ShowInTaskbar = false;
            Observe("unowned-false", subject);
            subject.ShowInTaskbar = true;
            Observe("unowned-true-again", subject);
            subject.ShowInTaskbar = false;
            Observe("unowned-false-again", subject);
            subject.Owner = first;
            Observe("owned-A-false", subject);
            subject.ShowInTaskbar = true;
            Observe("owned-A-true", subject);
            subject.ShowInTaskbar = false;
            subject.Owner = second;
            Observe("owned-B-false", subject);
            subject.Owner = null;
            Observe("owner-cleared-false", subject);
            subject.ShowInTaskbar = true;
            Observe("owner-cleared-true", subject);
            subject.Close();
            subject = null;
            Drain();
            Records.Add(new { phase = "subject-closed", elapsedMs = Clock.Elapsed.TotalMilliseconds,
                originalHandle = subjectHandle, originalStillExists = IsWindow((nint)subjectHandle),
                inventory = Inventory() });
        }
        catch (Exception error) { failure = error; }
        finally
        {
            foreach (Window? window in new[] { subject, second, first })
            {
                if (window == null) continue;
                try { window.Close(); }
                catch (Exception error) { failure ??= error; }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            diagnosticOnly = true, qualified = false,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            sourceAssembly = typeof(Window).Assembly.FullName,
            elapsedMs = Clock.Elapsed.TotalMilliseconds,
            records = Records, finalInventory = Inventory(), failure = failure?.ToString()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failure == null && Records.Count == 10 && subjectHandle != 0 && !IsWindow((nint)subjectHandle) ? 0 : 1;
    }

    private static Window MakeWindow(string title) => new()
    {
        Title = title, Width = 260, Height = 150, ShowActivated = false,
        ResizeMode = ResizeMode.NoResize, Content = "Original Microsoft WPF taskbar ownership reference"
    };

    private static void Drain()
    {
        if (Clock.Elapsed >= TimeSpan.FromSeconds(20)) throw new TimeoutException("Original 20-second in-process bound exceeded.");
        // Flush existing source work; no input or application-property changes.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (Clock.Elapsed >= TimeSpan.FromSeconds(20)) throw new TimeoutException("Original 20-second in-process bound exceeded.");
    }

    private static void Observe(string phase, Window subject)
    {
        Drain();
        nint handle = new WindowInteropHelper(subject).Handle;
        if (handle == 0 || !IsWindow(handle)) throw new InvalidOperationException("Original source window has no live native identity.");
        Records.Add(new { phase, elapsedMs = Clock.Elapsed.TotalMilliseconds,
            sourceShowInTaskbar = subject.ShowInTaskbar, sourceVisible = subject.IsVisible,
            sourceOwner = subject.Owner == null ? 0L : (long)new WindowInteropHelper(subject.Owner).Handle,
            native = Read(handle), inventory = Inventory() });
    }

    private static object Read(nint handle)
    {
        uint thread = GetWindowThreadProcessId(handle, out uint process);
        if (process != Environment.ProcessId) throw new InvalidOperationException("Foreign native window rejected.");
        long style = GetWindowLongPtrW(handle, GwlStyle).ToInt64();
        long extended = GetWindowLongPtrW(handle, GwlExStyle).ToInt64();
        return new { handle = (long)handle, thread, process, owner = (long)GetWindow(handle, GwOwner),
            style = $"0x{style:X}", extendedStyle = $"0x{extended:X}",
            appWindow = (extended & 0x40000) != 0, toolWindow = (extended & 0x80) != 0,
            visible = IsWindowVisible(handle), enabled = IsWindowEnabled(handle) };
    }

    private static List<object> Inventory()
    {
        var values = new List<object>();
        Exception? failure = null;
        EnumWindowsProc callback = (handle, _) =>
        {
            try
            {
                GetWindowThreadProcessId(handle, out uint process);
                if (process == Environment.ProcessId)
                {
                    if (values.Count == 32) throw new InvalidOperationException("Owned window inventory exceeded 32 records.");
                    values.Add(Read(handle));
                }
                return true;
            }
            catch (Exception error) { failure = error; return false; }
        };
        if (!EnumWindows(callback, 0) && failure == null) failure = new InvalidOperationException("Native inventory failed.");
        GC.KeepAlive(callback);
        if (failure != null) throw failure;
        return values;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
}
