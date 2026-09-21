using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.SdkSwitchLibrary;

public sealed class LibraryThemedControl : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(LibraryThemedControl),
        new FrameworkPropertyMetadata(string.Empty));

    static LibraryThemedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LibraryThemedControl),
            new FrameworkPropertyMetadata(typeof(LibraryThemedControl)));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
