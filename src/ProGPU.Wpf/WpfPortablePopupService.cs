using System;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU;

internal sealed class WpfPortablePopupService : IPortablePopupServiceRegistrar
{
    private readonly ProGpuWpfWindowHost _host;

    public WpfPortablePopupService(ProGpuWpfWindowHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;

    public bool TryCreatePopup(PortablePopupCreateRequest request, out object? presentationSource)
    {
        return _host.TryCreatePortablePopup(request, out presentationSource);
    }

    public bool TrySetPopupPosition(object presentationSource, int x, int y)
    {
        return _host.TrySetPortablePopupPosition(presentationSource, x, y);
    }

    public bool TrySetPopupSize(object presentationSource, int width, int height)
    {
        return _host.TrySetPortablePopupSize(presentationSource, width, height);
    }

    public bool TryShowPopup(object presentationSource)
    {
        return _host.TryShowPortablePopup(presentationSource);
    }

    public bool TryHidePopup(object presentationSource)
    {
        return _host.TryHidePortablePopup(presentationSource);
    }

    public bool TrySetPopupHitTestable(object presentationSource, bool hitTestable)
    {
        return _host.TrySetPortablePopupHitTestable(presentationSource, hitTestable);
    }

    public bool TryGetPopupPlacementBounds(object presentationSource, PortableRect targetBounds,
        out PortablePopupPlacementBounds bounds) =>
        _host.TryGetPortablePopupPlacementBounds(presentationSource, targetBounds, out bounds);

    public bool TryDestroyPopup(object presentationSource)
    {
        return _host.TryDestroyPortablePopup(presentationSource);
    }

    public void Clear()
    {
        _host.ClearPortablePopups();
    }
}
