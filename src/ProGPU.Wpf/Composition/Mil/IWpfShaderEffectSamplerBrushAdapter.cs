using ProGPU.Scene;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public readonly record struct WpfShaderEffectSamplerFrame(
    object Owner, global::ProGPU.Scene.Rect ContentBounds, float Padding);

public interface IWpfShaderEffectSamplerBrushAdapter
{
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
