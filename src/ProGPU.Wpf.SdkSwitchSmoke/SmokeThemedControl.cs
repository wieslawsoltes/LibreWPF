using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public sealed class SmokeThemedControl : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SmokeThemedControl),
        new FrameworkPropertyMetadata(string.Empty));

    static SmokeThemedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SmokeThemedControl),
            new FrameworkPropertyMetadata(typeof(SmokeThemedControl)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
