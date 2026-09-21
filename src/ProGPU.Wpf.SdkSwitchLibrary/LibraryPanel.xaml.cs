using System.Windows;
using System.Windows.Controls;

namespace ProGPU.Wpf.SdkSwitchLibrary;

public partial class LibraryPanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(LibraryPanel),
            new PropertyMetadata("Library panel"));

    public static readonly DependencyProperty LibraryTagProperty =
        DependencyProperty.Register(
            nameof(LibraryTag),
            typeof(string),
            typeof(LibraryPanel),
            new PropertyMetadata("library"));

    public LibraryPanel()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string LibraryTag
    {
        get => (string)GetValue(LibraryTagProperty);
        set => SetValue(LibraryTagProperty, value);
    }
}
