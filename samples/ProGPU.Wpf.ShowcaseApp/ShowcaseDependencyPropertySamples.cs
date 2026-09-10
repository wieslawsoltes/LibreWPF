using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProGPU.Wpf.ShowcaseApp;

public static class ShowcaseStateProperties
{
    public static readonly DependencyProperty SectionNameProperty =
        DependencyProperty.RegisterAttached(
            "SectionName",
            typeof(string),
            typeof(ShowcaseStateProperties),
            new FrameworkPropertyMetadata(
                "Unassigned section",
                FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ImportanceProperty =
        DependencyProperty.RegisterAttached(
            "Importance",
            typeof(double),
            typeof(ShowcaseStateProperties),
            new FrameworkPropertyMetadata(
                0d,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnImportanceChanged,
                CoerceImportance));

    public static int ImportanceChangedCount { get; private set; }

    public static string GetSectionName(DependencyObject element)
    {
        return (string)element.GetValue(SectionNameProperty);
    }

    public static void SetSectionName(DependencyObject element, string value)
    {
        element.SetValue(SectionNameProperty, value);
    }

    public static double GetImportance(DependencyObject element)
    {
        return (double)element.GetValue(ImportanceProperty);
    }

    public static void SetImportance(DependencyObject element, double value)
    {
        element.SetValue(ImportanceProperty, value);
    }

    private static void OnImportanceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        ImportanceChangedCount++;
    }

    private static object CoerceImportance(DependencyObject element, object baseValue)
    {
        return Math.Clamp((double)baseValue, 0d, 100d);
    }
}

public class ShowcaseHeaderTextBlock : TextBlock
{
    public static readonly DependencyProperty HeaderTextProperty =
        SummaryPanel.HeaderTextProperty.AddOwner(
            typeof(ShowcaseHeaderTextBlock),
            new FrameworkPropertyMetadata(
                "Header text",
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TypedOffsetProperty =
        DependencyProperty.Register(
            nameof(TypedOffset),
            typeof(ShowcaseTypedOffset),
            typeof(ShowcaseHeaderTextBlock),
            new FrameworkPropertyMetadata(
                default(ShowcaseTypedOffset),
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    static ShowcaseHeaderTextBlock()
    {
        FontWeightProperty.OverrideMetadata(
            typeof(ShowcaseHeaderTextBlock),
            new FrameworkPropertyMetadata(FontWeights.SemiBold));
        ForegroundProperty.OverrideMetadata(
            typeof(ShowcaseHeaderTextBlock),
            new FrameworkPropertyMetadata(Brushes.DarkSlateBlue));
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public ShowcaseTypedOffset TypedOffset
    {
        get => (ShowcaseTypedOffset)GetValue(TypedOffsetProperty);
        set => SetValue(TypedOffsetProperty, value);
    }
}

[TypeConverter(typeof(ShowcaseTypedOffsetConverter))]
public readonly record struct ShowcaseTypedOffset(double X, double Y);

public sealed class ShowcaseTypedOffsetConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string text)
        {
            string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                return new ShowcaseTypedOffset(x, y);
            }
        }

        return base.ConvertFrom(context, culture, value)!;
    }
}

public delegate void ShowcaseRoutedEventHandler(object sender, ShowcaseRoutedEventArgs e);

public sealed class ShowcaseRoutedEventArgs : RoutedEventArgs
{
    public ShowcaseRoutedEventArgs(RoutedEvent routedEvent, object source, string payload)
        : base(routedEvent, source)
    {
        Payload = payload;
    }

    public string Payload { get; }
}

public sealed class ShowcaseRoutedEventButton : Button
{
    public static readonly RoutedEvent ShowcaseActivatedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ShowcaseActivated),
            RoutingStrategy.Bubble,
            typeof(ShowcaseRoutedEventHandler),
            typeof(ShowcaseRoutedEventButton));

    static ShowcaseRoutedEventButton()
    {
        EventManager.RegisterClassHandler(
            typeof(ShowcaseRoutedEventButton),
            ShowcaseActivatedEvent,
            new ShowcaseRoutedEventHandler(OnShowcaseActivatedClassHandler),
            handledEventsToo: true);
    }

    public int ClassHandlerCount { get; private set; }

    public event ShowcaseRoutedEventHandler ShowcaseActivated
    {
        add => AddHandler(ShowcaseActivatedEvent, value);
        remove => RemoveHandler(ShowcaseActivatedEvent, value);
    }

    public ShowcaseRoutedEventArgs RaiseShowcaseActivated(string payload)
    {
        var args = new ShowcaseRoutedEventArgs(ShowcaseActivatedEvent, this, payload);
        RaiseEvent(args);
        return args;
    }

    private static void OnShowcaseActivatedClassHandler(object sender, ShowcaseRoutedEventArgs e)
    {
        if (sender is ShowcaseRoutedEventButton button)
        {
            button.ClassHandlerCount++;
        }
    }
}
