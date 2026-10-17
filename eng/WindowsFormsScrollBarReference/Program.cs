using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private sealed class ReferenceForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    [STAThread]
    private static int Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var clock = Stopwatch.StartNew();
        var records = new List<object>();
        using var form = new ReferenceForm
        {
            Text = "Owned original scrollbar reference", ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(800, 750),
            StartPosition = FormStartPosition.Manual, Location = new Point(80, 80)
        };
        int exit = 1;
        form.Shown += (_, _) =>
        {
            try
            {
                var cases = new (int Length, int Min, int Max, int Page, int Value)[]
                {
                    (671, 0, 685, 630, 0), (671, 0, 685, 630, 20),
                    (671, 0, 685, 630, 56), (671, 0, 685, 630, 685),
                    (100, 0, 100, 0, 50), (100, 0, 100, 101, 0),
                    (100, 20, 20, 1, 20), (101, -20, 20, 10, -7),
                    (16, 0, 100, 10, 45), (1, 0, 100, 10, 45),
                    (35, 0, 100, 10, 45), (100, 0, 100000, 1, 50000)
                };
                foreach (bool vertical in new[] { false, true })
                foreach (bool rtl in new[] { false, true })
                foreach (bool enabled in new[] { true, false })
                foreach (var item in cases)
                {
                    if (clock.Elapsed.TotalSeconds >= 20) throw new TimeoutException("Original 20-second source reference bound expired");
                    using ScrollBar bar = vertical ? new VScrollBar() : new HScrollBar();
                    bar.Bounds = vertical ? new Rectangle(20, 20, 17, item.Length) : new Rectangle(20, 20, item.Length, 17);
                    bar.Minimum = item.Min;
                    bar.Maximum = item.Max;
                    bar.LargeChange = item.Page;
                    bar.Value = item.Value;
                    bar.RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
                    form.Controls.Add(bar);
                    bar.CreateControl();
                    bar.Enabled = enabled;
                    nint handle = bar.Handle;
                    var info = new ScrollInfo { Size = (uint)Marshal.SizeOf<ScrollInfo>(), Mask = 0x17 };
                    var geometry = new ScrollBarInfo { Size = (uint)Marshal.SizeOf<ScrollBarInfo>(), States = new uint[6] };
                    if (!GetScrollInfo(handle, 2, ref info) || !GetScrollBarInfo(handle, -4, ref geometry))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
                    if (GetWindowThreadProcessId(handle, out uint pid) != GetCurrentThreadId() || pid != Environment.ProcessId)
                        throw new InvalidOperationException("Original source scrollbar ownership changed");
                    records.Add(new
                    {
                        vertical, rtl, enabled, item.Length, minimum = bar.Minimum, maximum = bar.Maximum,
                        page = bar.LargeChange, value = bar.Value, dpi = bar.DeviceDpi,
                        client = new[] { bar.ClientSize.Width, bar.ClientSize.Height },
                        nativeRange = new[] { info.Minimum, info.Maximum, info.Position, info.TrackPosition },
                        nativePage = info.Page, line = geometry.Line, thumbStart = geometry.ThumbStart,
                        thumbEnd = geometry.ThumbEnd, states = geometry.States,
                        nativeRect = new[] { geometry.Rect.Left, geometry.Rect.Top, geometry.Rect.Right, geometry.Rect.Bottom },
                        systemArrow = vertical ? SystemInformation.VerticalScrollBarArrowHeight : SystemInformation.HorizontalScrollBarArrowWidth,
                        systemThumb = vertical ? SystemInformation.VerticalScrollBarThumbHeight : SystemInformation.HorizontalScrollBarThumbWidth
                    });
                    form.Controls.Remove(bar);
                }
                if (records.Count != 96) throw new InvalidOperationException("Original reference inventory changed");
                var theme = new StringBuilder(1024);
                var color = new StringBuilder(128);
                var size = new StringBuilder(128);
                int themeResult = GetCurrentThemeName(theme, theme.Capacity, color, color.Capacity, size, size.Capacity);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    diagnosticOnly = true, qualified = false, inputInjected = false,
                    elapsedSeconds = clock.Elapsed.TotalSeconds, cases = records,
                    themeResult, themeFile = theme.ToString(), color = color.ToString(), size = size.ToString(),
                    sourceAssembly = typeof(ScrollBar).Assembly.Location,
                    sourceAssemblyVersion = typeof(ScrollBar).Assembly.GetName().Version?.ToString()
                }));
                exit = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { form.Close(); }
        };
        Application.Run(form);
        return exit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollBarInfo
    {
        public uint Size;
        public Rect Rect;
        public int Line, ThumbStart, ThumbEnd, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] States;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public uint Size, Mask;
        public int Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollInfo(nint window, int bar, ref ScrollInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetScrollBarInfo(nint window, int id, ref ScrollBarInfo info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentThemeName(StringBuilder theme, int themeLength, StringBuilder color, int colorLength, StringBuilder size, int sizeLength);
}
