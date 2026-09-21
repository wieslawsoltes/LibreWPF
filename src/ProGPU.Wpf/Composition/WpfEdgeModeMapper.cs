using System;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfEdgeModeMapper
{
    public static bool HasExplicitValue(object? value)
    {
        var text = value?.ToString();
        return !string.IsNullOrWhiteSpace(text)
            && !string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "0", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupported(object? value)
    {
        return TryMapToAliased(value, out _);
    }

    public static bool TryMapToAliased(object? value, out bool isAliased)
    {
        isAliased = false;
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)
            || string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "Aliased", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
        {
            isAliased = true;
            return true;
        }

        return false;
    }
}
