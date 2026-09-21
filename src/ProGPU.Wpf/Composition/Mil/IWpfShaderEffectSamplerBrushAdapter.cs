using ProGPU.Scene;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public interface IWpfShaderEffectSamplerBrushAdapter
{
    bool TryAdaptShaderEffectSamplerBrush(
        object? brush,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler);
}
