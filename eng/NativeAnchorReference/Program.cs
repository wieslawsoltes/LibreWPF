using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

// Deliberately uses the installed Microsoft WindowsDesktop runtime, not any
// source-built LibreWPF assembly. This records reference behavior, not parity.
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var cases = new[]
            {
                Measure("left-at-start", "", 100, HorizontalAlignment.Left),
                Measure("left-after-short-prefix", "prefix ", 100, HorizontalAlignment.Left),
                Measure("right-after-short-prefix", "prefix ", 100, HorizontalAlignment.Right),
                Measure("left-after-wrapped-prefix", string.Concat(Enumerable.Repeat("prefix ", 12)), 100, HorizontalAlignment.Left),
                Measure("full-width-after-short-prefix", "prefix ", 320, HorizontalAlignment.Left),
                Measure("figure-after-short-prefix", "prefix ", 100, HorizontalAlignment.Left, true),
                Measure("auto-left-after-short-prefix", "prefix ", double.NaN, HorizontalAlignment.Left),
                Measure("auto-stretch-after-short-prefix", "prefix ", double.NaN, HorizontalAlignment.Stretch),
                Measure("auto-figure-after-short-prefix", "prefix ", double.NaN, HorizontalAlignment.Left, true),
                Measure("sibling-anchors", "prefix ", 100, HorizontalAlignment.Left, false, true),
                Measure("right-sibling-anchors", "prefix ", 100, HorizontalAlignment.Right, false, true),
                Measure("center-sibling-anchors", "prefix ", 100, HorizontalAlignment.Center, false, true),
                Measure("anchor-only", "", 100, HorizontalAlignment.Left, false, false, ""),
            };
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                runtime = Environment.Version.ToString(),
                presentationFramework = typeof(Window).Assembly.FullName,
                presentationFrameworkLocation = typeof(Window).Assembly.Location,
                os = Environment.OSVersion.VersionString,
                cases,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static object Point(TextPointer position)
    {
        Rect rect = position.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty) throw new InvalidOperationException("Native WPF did not publish character geometry.");
        return new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height };
    }

    private static object Measure(string name, string prefix, double width,
        HorizontalAlignment alignment, bool figure = false, bool sibling = false,
        string suffix = "suffix suffix suffix suffix suffix suffix suffix")
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var before = new Run(prefix);
        var after = new Run(suffix);
        var childRun = new Run("anchor text");
        var childParagraph = new Paragraph(childRun) { Margin = new Thickness(0) };
        AnchoredBlock anchor = figure
            ? new Figure(childParagraph) { Width = double.IsNaN(width) ? new FigureLength() : new FigureLength(width), HorizontalAnchor = FigureHorizontalAnchor.ColumnLeft }
            : new Floater(childParagraph) { Width = width, HorizontalAlignment = alignment };
        anchor.Margin = anchor.Padding = anchor.BorderThickness = new Thickness(0);
        paragraph.Inlines.Add(before);
        paragraph.Inlines.Add(anchor);
        Run? siblingRun = null;
        if (sibling)
        {
            siblingRun = new Run("second anchor");
            paragraph.Inlines.Add(new Floater(new Paragraph(siblingRun) { Margin = new Thickness(0) })
            {
                Width = width, HorizontalAlignment = alignment,
                Margin = new Thickness(0), Padding = new Thickness(0), BorderThickness = new Thickness(0),
            });
        }
        paragraph.Inlines.Add(after);
        document.Blocks.Add(paragraph);
        var viewer = new FlowDocumentScrollViewer
        {
            IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = 320,
            Height = 240,
            Document = document,
        };
        var window = new Window
        {
            Title = "ProGPU native WPF anchor reference",
            ShowInTaskbar = false,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = viewer,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.UpdateLayout();
            return new
            {
                name, prefix, requestedWidth = double.IsNaN(width) ? (double?)null : width, viewerWidth = viewer.ActualWidth,
                alignment = alignment.ToString(), figure,
                prefixFirst = prefix.Length > 0 ? Point(before.ContentStart) : null,
                prefixLast = prefix.Length > 0 ? Point(before.ContentEnd.GetPositionAtOffset(-1)!) : null,
                anchorFirst = Point(childRun.ContentStart),
                siblingFirst = siblingRun is null ? null : Point(siblingRun.ContentStart),
                suffixFirst = suffix.Length > 0 ? Point(after.ContentStart) : null,
                suffixRows = Rows(after),
            };
        }
        finally { window.Close(); }
    }

    private static object[] Rows(Run run)
    {
        var rows = new List<object>();
        double previousY = double.NaN;
        for (int i = 0; i < run.Text.Length; ++i)
        {
            TextPointer position = run.ContentStart.GetPositionAtOffset(i)!;
            Rect rect = position.GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) throw new InvalidOperationException("Native WPF did not publish a suffix row.");
            if (rect.Y != previousY)
            {
                rows.Add(new { offset = i, x = rect.X, y = rect.Y, height = rect.Height });
                previousY = rect.Y;
            }
        }
        return rows.ToArray();
    }
}
