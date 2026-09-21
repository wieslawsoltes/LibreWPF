namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfDrawingCacheCommandSink
{
    bool PushDrawingCache(System.Windows.Rect? bounds = null);
}

internal interface IWpfNativeDrawingCacheCommandSink
{
    bool PushNativeDrawingCache(WpfReplayRect? bounds = null);
}
