using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.ShowcaseApp;

public sealed class ShowcaseThemedControl : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(ShowcaseThemedControl),
        new FrameworkPropertyMetadata(string.Empty));

    static ShowcaseThemedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ShowcaseThemedControl),
            new FrameworkPropertyMetadata(typeof(ShowcaseThemedControl)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
