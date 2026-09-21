using System.Windows.Media.ProGPU.Composition;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public interface IWpfDrawingResourceResolver
{
    bool TryReplayDrawing(uint resourceToken, IWpfCompositionCommandSink sink);

    WpfDrawingReplayStatus ReplayDrawing(uint resourceToken, IWpfCompositionCommandSink sink)
    {
        return TryReplayDrawing(resourceToken, sink)
            ? WpfDrawingReplayStatus.Applied
            : WpfDrawingReplayStatus.Skipped;
    }
}
