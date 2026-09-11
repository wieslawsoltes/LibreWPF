using System;
using System.Numerics;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal readonly record struct WpfViewport3DMaterialPass(
    PortableViewport3DMaterialKind Kind,
    Vector4 Color,
    Vector3 SpecularColor,
    float Shininess,
    Vector3 AmbientColor,
    float Opacity,
    bool IsUnlit)
{
    public global::ProGPU.Vector.Brush? MaterialBrush { get; init; }
}

internal static class WpfViewport3DMaterialMapper
{
    internal static bool TryMapManaged(
        PortableViewport3DMaterial? material,
        out WpfViewport3DMaterialPass pass)
        => TryMapSolid(material, out pass) ||
            TryMapGradient(material, allowSpecular: true, out pass);

    internal static bool TryMapNative(
        PortableViewport3DMaterial? material,
        out WpfViewport3DMaterialPass pass)
        => TryMapSolid(material, out pass) ||
            TryMapGradient(material, allowSpecular: true, out pass);

    private static bool TryMapGradient(
        PortableViewport3DMaterial? material,
        bool allowSpecular,
        out WpfViewport3DMaterialPass pass)
    {
        if (material is null ||
            material.Brush is null ||
            material.TileBrush is not null ||
            material.Brush.Kind is not (
                PortableBrushKind.LinearGradient or
                PortableBrushKind.RadialGradient) ||
            !TryToColor(material.Color, out Vector4 materialColor))
        {
            pass = default;
            return false;
        }

        global::ProGPU.Vector.Brush? nativeBrush =
            WpfResourceResolver.AdaptNativePortableBrush(
                material.Brush,
                new WpfReplayRect(0, 0, 1, 1),
                out int unsupportedStateCount);
        if (nativeBrush is null || unsupportedStateCount != 0)
        {
            pass = default;
            return false;
        }

        if (material.Kind == PortableViewport3DMaterialKind.Specular)
        {
            if (!allowSpecular ||
                !TryToFiniteFloat(
                    material.SpecularPower,
                    out float shininess))
            {
                pass = default;
                return false;
            }
            pass = new WpfViewport3DMaterialPass(
                material.Kind,
                new Vector4(0, 0, 0, 1),
                new Vector3(
                    materialColor.X,
                    materialColor.Y,
                    materialColor.Z),
                MathF.Max(shininess, 0.001f),
                Vector3.Zero,
                materialColor.W,
                IsUnlit: false)
            {
                MaterialBrush = nativeBrush
            };
            return true;
        }

        bool isUnlit =
            material.Kind == PortableViewport3DMaterialKind.Emissive;
        if (!isUnlit &&
            material.Kind != PortableViewport3DMaterialKind.Diffuse)
        {
            pass = default;
            return false;
        }
        Vector3 ambient = Vector3.Zero;
        if (!isUnlit &&
            !TryToColor(material.AmbientColor, out ambient))
        {
            pass = default;
            return false;
        }

        pass = new WpfViewport3DMaterialPass(
            material.Kind,
            new Vector4(
                materialColor.X,
                materialColor.Y,
                materialColor.Z,
                1.0f),
            Vector3.Zero,
            1.0f,
            isUnlit ? Vector3.Zero : ambient,
            materialColor.W,
            isUnlit)
        {
            MaterialBrush = nativeBrush
        };
        return true;
    }

    internal static bool TryMapSolid(
        PortableViewport3DMaterial? material,
        out WpfViewport3DMaterialPass pass)
    {
        pass = default;
        if (material is null
            || material.Brush is null
            || material.TileBrush is not null
            || material.Brush.Kind != PortableBrushKind.SolidColor
            || !TryToUnitFloat(material.Brush.Opacity, out float brushOpacity)
            || !TryToColor(material.Color, out Vector4 materialColor)
            || !TryToColor(material.Brush.Color, out Vector4 brushColor))
        {
            return false;
        }

        Vector3 rgb = new(
            brushColor.X * materialColor.X,
            brushColor.Y * materialColor.Y,
            brushColor.Z * materialColor.Z);
        float opacity =
            brushColor.W * brushOpacity * materialColor.W;

        switch (material.Kind)
        {
            case PortableViewport3DMaterialKind.Diffuse:
                if (!TryToColor(material.AmbientColor, out Vector3 ambient))
                {
                    return false;
                }
                pass = new WpfViewport3DMaterialPass(
                    material.Kind,
                    new Vector4(rgb, 1.0f),
                    Vector3.Zero,
                    1.0f,
                    ambient,
                    opacity,
                    IsUnlit: false);
                return true;
            case PortableViewport3DMaterialKind.Specular:
                if (!TryToFiniteFloat(
                        material.SpecularPower,
                        out float shininess))
                {
                    return false;
                }
                pass = new WpfViewport3DMaterialPass(
                    material.Kind,
                    new Vector4(0, 0, 0, 1),
                    rgb,
                    MathF.Max(shininess, 0.001f),
                    Vector3.Zero,
                    opacity,
                    IsUnlit: false);
                return true;
            case PortableViewport3DMaterialKind.Emissive:
                pass = new WpfViewport3DMaterialPass(
                    material.Kind,
                    new Vector4(rgb, 1.0f),
                    Vector3.Zero,
                    1.0f,
                    Vector3.Zero,
                    opacity,
                    IsUnlit: true);
                return true;
            default:
                return false;
        }
    }

    private static bool TryToColor(
        PortableColor value,
        out Vector4 color)
    {
        const float scale = 1.0f / 255.0f;
        color = new Vector4(
            value.R * scale,
            value.G * scale,
            value.B * scale,
            value.A * scale);
        return true;
    }

    private static bool TryToColor(
        PortableColor4 value,
        out Vector4 color)
    {
        if (!TryToUnitFloat(value.R, out float r)
            || !TryToUnitFloat(value.G, out float g)
            || !TryToUnitFloat(value.B, out float b)
            || !TryToUnitFloat(value.A, out float a))
        {
            color = default;
            return false;
        }
        color = new Vector4(r, g, b, a);
        return true;
    }

    private static bool TryToColor(
        PortableVector3 value,
        out Vector3 color)
    {
        if (!TryToUnitFloat(value.X, out float x)
            || !TryToUnitFloat(value.Y, out float y)
            || !TryToUnitFloat(value.Z, out float z))
        {
            color = default;
            return false;
        }
        color = new Vector3(x, y, z);
        return true;
    }

    private static bool TryToUnitFloat(double value, out float result)
    {
        return TryToRangeFloat(value, 0.0f, 1.0f, out result);
    }

    private static bool TryToFiniteFloat(double value, out float result)
    {
        if (!double.IsFinite(value)
            || value < -float.MaxValue
            || value > float.MaxValue)
        {
            result = default;
            return false;
        }
        result = (float)value;
        return float.IsFinite(result);
    }

    private static bool TryToRangeFloat(
        double value,
        float minimum,
        float maximum,
        out float result)
    {
        if (!double.IsFinite(value)
            || value < minimum
            || value > maximum)
        {
            result = default;
            return false;
        }
        result = (float)value;
        return float.IsFinite(result);
    }
}
