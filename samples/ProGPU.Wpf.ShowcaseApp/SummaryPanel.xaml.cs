using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class SummaryPanel : UserControl
{
    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(
            nameof(HeaderText),
            typeof(string),
            typeof(SummaryPanel),
            new FrameworkPropertyMetadata(
                "Selection summary",
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public SummaryPanel()
    {
        InitializeComponent();
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }
}
