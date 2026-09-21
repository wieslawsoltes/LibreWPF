using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.RealXamlCompilerHarness;

public partial class SmokePage : Page
{
    public SmokePage()
    {
        InitializeComponent();
    }

    public int PageClickCount { get; private set; }

    public string? LastPageClickSenderName { get; private set; }

    public string? LastPageClickRoutedEventName { get; private set; }

    private void OnPageButtonClick(object sender, RoutedEventArgs e)
    {
        PageClickCount++;
        LastPageClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastPageClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }
}
