using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.RealXamlCompilerHarness;

public partial class SmokeUserControl : UserControl
{
    public SmokeUserControl()
    {
        InitializeComponent();
    }

    public int ControlClickCount { get; private set; }

    public string? LastControlClickSenderName { get; private set; }

    public string? LastControlClickRoutedEventName { get; private set; }

    private void OnControlButtonClick(object sender, RoutedEventArgs e)
    {
        ControlClickCount++;
        LastControlClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastControlClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }
}
