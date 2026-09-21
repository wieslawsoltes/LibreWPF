using System;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class CoalescingWpfRenderScheduler : IWpfDelayedRenderScheduler
{
    private bool _hasPendingRenderRequest;

    public event EventHandler? RenderRequested;

    public bool HasPendingRenderRequest => _hasPendingRenderRequest;

    public void RequestRender()
    {
        if (_hasPendingRenderRequest)
        {
            return;
        }

        _hasPendingRenderRequest = true;
        RenderRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRender(TimeSpan delay)
    {
        RequestRender();
    }

    public bool ConsumeRenderRequest()
    {
        var hadPendingRequest = _hasPendingRenderRequest;
        _hasPendingRenderRequest = false;
        return hadPendingRequest;
    }

    public void Reset()
    {
        _hasPendingRenderRequest = false;
    }
}
