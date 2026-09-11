using System;
using System.Collections.Generic;
using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfMilResourceRegistry : IWpfMilResourceResolver, IWpfGuidelineSetResourceResolver,
    IWpfRawMilResourceResolver
{
    private readonly IReadOnlyList<object?>? _dependentResources;
    private Dictionary<uint, object>? _resources;

    public WpfMilResourceRegistry()
    {
    }

    private WpfMilResourceRegistry(IReadOnlyList<object?> dependentResources)
    {
        _dependentResources = dependentResources;
    }

    public static WpfMilResourceRegistry FromDependentResources(IReadOnlyList<object?> dependentResources)
    {
        ArgumentNullException.ThrowIfNull(dependentResources);

        return new WpfMilResourceRegistry(dependentResources);
    }

    public void Register(uint resourceToken, object resource)
    {
        if (resourceToken == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(resourceToken), "WPF MIL dependent resource tokens are one-based.");
        }

        ArgumentNullException.ThrowIfNull(resource);
        (_resources ??= new Dictionary<uint, object>())[resourceToken] = resource;
    }

    public MediaBrush? ResolveBrush(uint resourceToken)
    {
        return Resolve<MediaBrush>(resourceToken);
    }

    public MediaPen? ResolvePen(uint resourceToken)
    {
        return Resolve<MediaPen>(resourceToken);
    }

    public MediaGeometry? ResolveGeometry(uint resourceToken)
    {
        return Resolve<MediaGeometry>(resourceToken);
    }

    public MediaImageSource? ResolveImageSource(uint resourceToken)
    {
        return Resolve<MediaImageSource>(resourceToken);
    }

    public MediaGlyphRun? ResolveGlyphRun(uint resourceToken)
    {
        return Resolve<MediaGlyphRun>(resourceToken);
    }

    public MediaTransform? ResolveTransform(uint resourceToken)
    {
        return Resolve<MediaTransform>(resourceToken);
    }

    public object? ResolveGuidelineSet(uint resourceToken)
    {
        return TryResolveResource(resourceToken, out var resource) ? resource : null;
    }

    bool IWpfRawMilResourceResolver.TryResolveRawResource(uint resourceToken, out object resource)
    {
        if (TryResolveResource(resourceToken, out var resolved) && resolved != null)
        {
            resource = resolved;
            return true;
        }

        resource = null!;
        return false;
    }

    private T? Resolve<T>(uint resourceToken) where T : class
    {
        return TryResolveResource(resourceToken, out var resource)
            ? resource as T
            : null;
    }

    private bool TryResolveResource(uint resourceToken, out object? resource)
    {
        if (resourceToken == 0)
        {
            resource = null;
            return false;
        }

        if (_resources != null && _resources.TryGetValue(resourceToken, out var registeredResource))
        {
            resource = registeredResource;
            return true;
        }

        if (_dependentResources != null)
        {
            var index = resourceToken - 1;
            if (index < (uint)_dependentResources.Count)
            {
                resource = _dependentResources[(int)index];
                return resource != null;
            }
        }

        resource = null;
        return false;
    }
}
