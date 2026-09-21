using ProGPU.Scene;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>
/// Converts the package-neutral drawing-context state published by
/// source-built LibreWPF into the typed ProGPU scene state used by reusable
/// GPU presenters.
/// </summary>
public static class WpfProGpuDrawingContextBridge
{
    /// <summary>
    /// Gets the active ProGPU drawing context and its outer transform without
    /// reflection, wrapper allocation, or a PresentationCore dependency on
    /// ProGPU.Scene.
    /// </summary>
    public static bool TryGetProGpuDrawingContextState(
        IPortableNativeDrawingContextStateSource source,
        out ProGpuDrawingContextState state)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.TryGetPortableNativeDrawingContextState(
                out PortableNativeDrawingContextState portableState))
        {
            state = default;
            return false;
        }

        return ProGpuDrawingContextState.TryCreate(
            portableState.NativeDrawingContext,
            portableState.Transform,
            out state);
    }
}
