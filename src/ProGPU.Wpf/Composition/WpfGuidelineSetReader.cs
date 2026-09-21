using System;
using System.Collections.Generic;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

internal static class WpfGuidelineSetReader
{
    public static bool TryReadDynamicGuidelineY1(object? guidelines, out double coordinate)
    {
        coordinate = default;
        if (TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY)
            && guidelinesX.Length == 0
            && guidelinesY.Length == 1)
        {
            coordinate = guidelinesY[0];
            return true;
        }

        return false;
    }

    public static bool TryReadDynamicGuidelineYPair(
        object? guidelines,
        out double leadingCoordinate,
        out double drivenCoordinate)
    {
        leadingCoordinate = default;
        drivenCoordinate = default;

        if (!TryReadDynamicGuidelineSet(guidelines, out var guidelinesX, out var guidelinesY)
            || guidelinesX.Length != 0
            || guidelinesY.Length != 2)
        {
            return false;
        }

        leadingCoordinate = guidelinesY[0];
        drivenCoordinate = guidelinesY[1];
        return true;
    }

    public static bool TryReadDynamicGuidelineSet(
        object? guidelines,
        out double[] guidelinesX,
        out double[] guidelinesY)
    {
        guidelinesX = Array.Empty<double>();
        guidelinesY = Array.Empty<double>();

        if (guidelines is not IPortableGuidelineSetSource guidelineSource
            || !guidelineSource.TryGetPortableGuidelineSet(out var portableGuidelines)
            || !portableGuidelines.IsFrozen
            || !portableGuidelines.IsDynamic)
        {
            return false;
        }

        guidelinesX = portableGuidelines.GuidelinesX;
        guidelinesY = portableGuidelines.GuidelinesY;
        return true;
    }

    public static bool TryReadDoubleCollection(object? collection, out double[] values)
    {
        values = Array.Empty<double>();

        if (collection is not IList<double> typedValues)
        {
            return false;
        }

        if (typedValues.Count == 0)
        {
            return true;
        }

        values = new double[typedValues.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = typedValues[i];
        }

        return true;
    }
}
