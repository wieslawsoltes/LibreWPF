namespace System.Windows.Media.ProGPU.Composition;

internal interface IWpfPointHitRegionCommandSink
{
    void PushPointHitRegion(WpfReplayRect rectangle);
    void PopPointHitRegion();
}
