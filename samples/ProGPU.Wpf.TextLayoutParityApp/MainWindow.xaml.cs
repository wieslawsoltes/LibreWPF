using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ProGPU.Wpf.TextLayoutParityApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
#endif
            var lineTops = new List<double>();
            var lineStarts = new List<int>();
            TextPointer end = WrappingText.ContentEnd;
            TextPointer? position = WrappingText.ContentStart;
            for (int count = 0; position != null && position.CompareTo(end) <= 0; count++)
            {
                if (count > 1000)
                {
                    throw new InvalidOperationException("Text insertion positions did not terminate.");
                }

                Rect rectangle = position.GetCharacterRect(LogicalDirection.Forward);
                if (position.CompareTo(end) == 0 &&
                    (rectangle.IsEmpty || !double.IsFinite(rectangle.X)))
                {
                    throw new InvalidOperationException("The final text insertion position has no caret rectangle.");
                }
                if (!rectangle.IsEmpty && double.IsFinite(rectangle.Y))
                {
                    bool newLine = true;
                    for (int i = 0; i < lineTops.Count; i++)
                    {
                        if (Math.Abs(lineTops[i] - rectangle.Y) < 0.25)
                        {
                            newLine = false;
                            break;
                        }
                    }

                    if (newLine)
                    {
                        lineTops.Add(rectangle.Y);
                        lineStarts.Add(WrappingText.ContentStart.GetOffsetToPosition(position));
                    }
                }

                position = position.GetNextInsertionPosition(LogicalDirection.Forward);
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "TEXT_LAYOUT width={0:F3} height={1:F3} font={2:F3} lines={3} tops={4} starts={5}",
                WrappingText.ActualWidth,
                WrappingText.ActualHeight,
                WrappingText.FontSize,
                lineTops.Count,
                string.Join(",", lineTops.ConvertAll(top => top.ToString("F3", CultureInfo.InvariantCulture))),
                string.Join(",", lineStarts)));
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
}
