using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public sealed class SmokeItemDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is SmokeItem item
            ? $"{item.Name}={item.Value}/{item.Category}"
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public sealed class SmokeItemSummaryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string name = GetText(values, 0);
        string value = GetText(values, 1);
        return $"{name}:{value}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return Array.ConvertAll(targetTypes, _ => Binding.DoNothing);
    }

    private static string GetText(object[] values, int index)
    {
        if (index >= values.Length ||
            values[index] is null ||
            ReferenceEquals(values[index], DependencyProperty.UnsetValue))
        {
            return string.Empty;
        }

        return values[index].ToString() ?? string.Empty;
    }
}
