using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ProGPU.Wpf.TextLayoutParityApp;

public partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    public MainWindow()
    {
        InitializeComponent();
        TabsAndWhitespaceText.Text = "Alpha\tBeta trailing   \nNext\tcolumn";
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (Environment.GetEnvironmentVariable("PROGPU_WPF_TEXT_LAYOUT_REPORT") != "1")
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ReportLayout));
    }

    private void ReportLayout()
    {
        try
        {
#if PROGPU_WPF_NATIVE_MIL
            if (!string.Equals(AppContext.GetData("LibreWPF.RequestedRendererMode") as string,
                    "NativeMilWgpu", StringComparison.Ordinal) ||
                !global::System.Windows.Media.ProGPU.ProGpuWpfDiagnostics.TryGetWindowHost(this, out var nativeHost) ||
                nativeHost == null)
            {
                throw new InvalidOperationException("The text-layout fixture did not select a live native MIL host.");
            }
            Console.WriteLine("TEXT_RENDERER NativeMilWgpu");
            var nativeWindow = nativeHost.NativeWindowHandle;
            if (nativeWindow.Kind != global::ProGPU.Backend.NativeWindowKind.Win32 ||
                !nativeWindow.IsValid)
            {
                throw new InvalidOperationException("The native MIL fixture has no actual Win32 host window.");
            }
            IntPtr realWindow = nativeWindow.Handle;
#else
            IntPtr realWindow = new System.Windows.Interop.WindowInteropHelper(this).Handle;
#endif
            if (realWindow == IntPtr.Zero ||
                !GetWindowRect(realWindow, out var outer) ||
                !GetClientRect(realWindow, out var client))
            {
                throw new InvalidOperationException("The fixture could not read its actual native window rectangles.");
            }
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "WINDOW_GEOMETRY outer={0}x{1} client={2}x{3} dpi={4} source={5:F3}x{6:F3}",
                outer.Width, outer.Height, client.Width, client.Height,
                GetDpiForWindow(realWindow), Width, Height));
            ReportTextCase("wrapped-composite", WrappedCompositeText);
            ReportTextCase("mixed-runs", MixedRunsText);
            ReportTextCase("overflow-token", OverflowTokenText);
            ReportTextCase("tabs-whitespace", TabsAndWhitespaceText);
            ReportTextCase("explicit-line-height", ExplicitLineHeightText);
            ReportTextCase("bidirectional", BidirectionalText);
            Console.Out.Flush();
            if (Environment.GetEnvironmentVariable("PROGPU_WPF_TEXT_LAYOUT_EXIT_AFTER_REPORT") == "1")
            {
                Close();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("TEXT_LAYOUT_ERROR " + exception);
            Environment.ExitCode = 1;
            Close();
        }
    }

    private static void ReportTextCase(string name, TextBlock textBlock)
    {
        var lineTops = new List<double>();
        var lineHeights = new List<double>();
        var lineStarts = new List<int>();
        Rect finalCaret = Rect.Empty;
        TextPointer start = textBlock.ContentStart;
        TextPointer end = textBlock.ContentEnd;
        TextPointer finalPosition = end.GetInsertionPosition(LogicalDirection.Backward) ?? end;
        TextPointer firstPosition = start.GetInsertionPosition(LogicalDirection.Forward) ?? start;
        TextPointer linePosition = firstPosition.GetLineStartPosition(0) ?? firstPosition;
        for (int count = 0; linePosition.CompareTo(finalPosition) <= 0; count++)
        {
            if (count > 1000)
            {
                throw new InvalidOperationException($"Text lines for '{name}' did not terminate.");
            }

            Rect rectangle = linePosition.GetCharacterRect(LogicalDirection.Forward);
            if (rectangle.IsEmpty ||
                !double.IsFinite(rectangle.Y) ||
                !double.IsFinite(rectangle.Height))
            {
                throw new InvalidOperationException($"A line start for '{name}' has no caret rectangle.");
            }

            lineTops.Add(rectangle.Y);
            lineHeights.Add(rectangle.Height);
            lineStarts.Add(start.GetOffsetToPosition(linePosition));
            TextPointer? nextLine = linePosition.GetLineStartPosition(1, out int actualLineCount);
            if (nextLine == null ||
                actualLineCount == 0 ||
                nextLine.CompareTo(linePosition) <= 0 ||
                nextLine.CompareTo(finalPosition) > 0)
            {
                break;
            }
            linePosition = nextLine;
        }

        int insertionPositionCount = 0;
        TextPointer? position = firstPosition;
        for (int count = 0; position != null && position.CompareTo(finalPosition) <= 0; count++)
        {
            if (count > 10000)
            {
                throw new InvalidOperationException($"Text insertion positions for '{name}' did not terminate.");
            }

            Rect rectangle = position.GetCharacterRect(LogicalDirection.Forward);
            if (position.CompareTo(finalPosition) == 0)
            {
                finalCaret = rectangle;
            }
            insertionPositionCount++;
            position = position.GetNextInsertionPosition(LogicalDirection.Forward);
        }

        if (finalCaret.IsEmpty ||
            !double.IsFinite(finalCaret.X) ||
            !double.IsFinite(finalCaret.Y) ||
            !double.IsFinite(finalCaret.Height))
        {
            throw new InvalidOperationException($"The final text insertion position for '{name}' has no caret rectangle.");
        }

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "TEXT_CASE name={0} width={1:F3} height={2:F3} desiredWidth={3:F3} desiredHeight={4:F3} font={5:F3} lines={6} tops={7} heights={8} starts={9} positions={10} endOffset={11} caretX={12:F3} caretY={13:F3} caretHeight={14:F3}",
            name,
            textBlock.ActualWidth,
            textBlock.ActualHeight,
            textBlock.DesiredSize.Width,
            textBlock.DesiredSize.Height,
            textBlock.FontSize,
            lineTops.Count,
            string.Join(",", lineTops.ConvertAll(top => top.ToString("F3", CultureInfo.InvariantCulture))),
            string.Join(",", lineHeights.ConvertAll(height => height.ToString("F3", CultureInfo.InvariantCulture))),
            string.Join(",", lineStarts),
            insertionPositionCount,
            start.GetOffsetToPosition(finalPosition),
            finalCaret.X,
            finalCaret.Y,
            finalCaret.Height));
    }
}
