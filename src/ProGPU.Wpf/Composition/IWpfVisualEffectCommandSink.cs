namespace System.Windows.Media.ProGPU.Composition;

public interface IWpfVisualEffectCommandSink
{
    bool PushVisualEffect(global::ProGPU.Scene.EffectBase effect);

    bool PushVisualEffect(global::ProGPU.Scene.EffectBase effect, System.Windows.Rect? bounds)
    {
        return PushVisualEffect(effect);
    }
}

internal interface IWpfNativeVisualEffectCommandSink
{
    bool PushNativeVisualEffect(
        global::ProGPU.Scene.EffectBase effect,
        WpfReplayRect? bounds);
}
