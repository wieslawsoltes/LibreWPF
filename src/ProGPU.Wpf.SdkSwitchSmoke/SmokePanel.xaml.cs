using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class SmokePanel : UserControl
{
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption),
        typeof(string),
        typeof(SmokePanel),
        new PropertyMetadata("default caption"));

    public static readonly DependencyProperty PanelContentProperty = DependencyProperty.Register(
        nameof(PanelContent),
        typeof(object),
        typeof(SmokePanel),
        new PropertyMetadata(null));

    public SmokePanel()
    {
        InitializeComponent();
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public object? PanelContent
    {
        get => GetValue(PanelContentProperty);
        set => SetValue(PanelContentProperty, value);
    }
}
