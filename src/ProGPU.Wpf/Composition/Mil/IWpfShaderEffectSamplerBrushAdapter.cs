using ProGPU.Scene;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public readonly record struct WpfShaderEffectSamplerFrame(
    object Owner, global::ProGPU.Scene.Rect ContentBounds, float Padding)
{
    /// <summary>
    /// Original source bounds and four padding values. When present this is the
    /// authoritative frame; the legacy scalar fields are not capture inputs.
    /// </summary>
    public ShaderEffectSourceCapture? SourceCapture { get; private init; }

    public static WpfShaderEffectSamplerFrame FromSource(object owner, ShaderEffectSourceCapture source)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!source.IsValid) throw new ArgumentOutOfRangeException(nameof(source));
        return new(owner, default, 0) { SourceCapture = source };
    }

    /// <summary>The current receiving compositor DPI, not the source bitmap DPI.</summary>
    public float DpiScale { get; init; } = 1f;
}

public interface IWpfShaderEffectSamplerBrushAdapter
{
    /// <summary>
    /// Captures a source sampler using the exact original double bounds and
    /// four-edge padding. Legacy framed adapters cannot infer this capability.
    /// </summary>
    bool TryAdaptSourceShaderEffectSamplerBrush(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        WpfShaderEffectSamplerFrame frame,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        return false;
    }

    bool TryAdaptShaderEffectSamplerBrush(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler);

    /// <summary>Captures a complete source ImageBrush in the receiving effect's frame.</summary>
    bool TryAdaptShaderEffectSamplerBrush(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        WpfShaderEffectSamplerFrame frame,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        return false;
    }
}
