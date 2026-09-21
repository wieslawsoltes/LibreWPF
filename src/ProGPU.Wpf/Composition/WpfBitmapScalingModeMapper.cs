using System;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfBitmapScalingModeMapper
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
        return TryMapToTextureSamplingMode(value, out _);
    }

    public static bool TryMapToTextureSamplingMode(
        object? value,
        out global::ProGPU.Scene.TextureSamplingMode samplingMode)
    {
        samplingMode = global::ProGPU.Scene.TextureSamplingMode.Linear;
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)
            || string.Equals(text, "Unspecified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "NearestNeighbor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "3", StringComparison.OrdinalIgnoreCase))
        {
            samplingMode = global::ProGPU.Scene.TextureSamplingMode.Nearest;
            return true;
        }

        if (string.Equals(text, "HighQuality", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Fant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "2", StringComparison.OrdinalIgnoreCase))
        {
            samplingMode = global::ProGPU.Scene.TextureSamplingMode.Cubic;
            return true;
        }

        return string.Equals(text, "Linear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "LowQuality", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "1", StringComparison.OrdinalIgnoreCase);
    }
}
