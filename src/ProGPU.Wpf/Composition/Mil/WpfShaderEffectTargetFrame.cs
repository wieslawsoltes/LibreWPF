using System.Numerics;
using ProGPU.Scene;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>
/// Actual receiving target inputs captured before source replay. Projection and
/// viewport arithmetic belong to Scene, not the source adapter. Semantic DPI is
/// deliberately independent of this physical mapping.
/// </summary>
public readonly record struct WpfShaderEffectTargetFrame(
    uint LogicalWidth,
    uint LogicalHeight,
    uint TargetWidth,
    uint TargetHeight,
    RenderTargetViewport Viewport)
{
    public bool TryGetPixelsPerUnit(out Vector2 pixelsPerUnit) =>
        EffectCaptureFrame.TryResolveSourcePixelsPerUnit(LogicalWidth, LogicalHeight,
            TargetWidth, TargetHeight, Viewport, out pixelsPerUnit);
}
