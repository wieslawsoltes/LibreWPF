using System;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfTextRenderingModeMapper
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
        return TryMapToTextRenderingMode(value, out _);
    }

    public static bool HasExplicitTextHintingMode(object? value)
    {
        var text = value?.ToString();
        return !string.IsNullOrWhiteSpace(text)
            && !string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "0", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedTextHintingMode(object? value)
    {
        return TryMapToTextHintingMode(value, out _);
    }

    public static bool HasExplicitClearTypeHint(object? value)
    {
        var text = value?.ToString();
        return !string.IsNullOrWhiteSpace(text)
            && !string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "0", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedClearTypeHint(object? value)
    {
        return TryMapClearTypeHintToTextRenderingMode(value, out _);
    }

    public static bool TryMapClearTypeHintToTextRenderingMode(
        object? value,
        out global::ProGPU.Scene.TextRenderingMode mode)
    {
        mode = global::ProGPU.Scene.TextRenderingMode.Grayscale;
        var text = value?.ToString();
        if (string.Equals(text, "Enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
        {
            mode = global::ProGPU.Scene.TextRenderingMode.ClearType;
            return true;
        }

        return false;
    }

    public static bool TryMapToAliased(object? value, out bool isAliased)
    {
        var mapped = TryMapToTextRenderingMode(value, out var mode);
        isAliased = mode == global::ProGPU.Scene.TextRenderingMode.Aliased;
        return mapped;
    }

    public static bool TryMapToTextRenderingMode(
        object? value,
        out global::ProGPU.Scene.TextRenderingMode mode)
    {
        mode = global::ProGPU.Scene.TextRenderingMode.Grayscale;
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
            mode = global::ProGPU.Scene.TextRenderingMode.Aliased;
            return true;
        }

        if (string.Equals(text, "Grayscale", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "2", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "ClearType", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "3", StringComparison.OrdinalIgnoreCase))
        {
            mode = global::ProGPU.Scene.TextRenderingMode.ClearType;
            return true;
        }

        return false;
    }

    public static bool TryMapToTextHintingMode(
        object? value,
        out global::ProGPU.Scene.TextHintingMode mode)
    {
        mode = global::ProGPU.Scene.TextHintingMode.Auto;
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)
            || string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "Fixed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
        {
            mode = global::ProGPU.Scene.TextHintingMode.Fixed;
            return true;
        }

        if (string.Equals(text, "Animated", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "2", StringComparison.OrdinalIgnoreCase))
        {
            mode = global::ProGPU.Scene.TextHintingMode.Animated;
            return true;
        }

        return false;
    }
}
