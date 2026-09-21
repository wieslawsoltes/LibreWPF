namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfVisualCacheCommandSink
{
    bool PushVisualCache(System.Windows.Rect? bounds = null);
}

internal interface IWpfNativeVisualCacheCommandSink
{
    bool PushNativeVisualCache(WpfReplayRect? bounds = null);
}
