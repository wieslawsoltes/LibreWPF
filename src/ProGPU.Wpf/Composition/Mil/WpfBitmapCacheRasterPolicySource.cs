using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using System.Windows.Media.ProGPU.Platform;

namespace System.Windows.Media.ProGPU.Composition.Mil;

// Owns source and device identity locally. Only copied numeric policy crosses
// MIL; monitor/window/device addresses are never used as provider evidence.
internal sealed class WpfBitmapCacheRasterPolicySource
{
    private readonly WgpuContext _context;
    private readonly Func<IWpfMonitorService> _getMonitors;
    private readonly Func<(uint Width, uint Height)>? _getNativeLimits;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private IPortablePrimaryDisplayRasterScaleSource? _provider;
    private PortablePrimaryDisplayRasterScale _source;
    private WgpuDeviceIdentity? _device;
    private PortableBitmapCacheRasterPolicy _policy;
    private PortableBitmapCacheRasterPolicy? _framePolicy;
    private bool _capturing;

    internal WpfBitmapCacheRasterPolicySource(WgpuContext context,
        Func<IWpfMonitorService> getMonitors, Func<(uint Width, uint Height)>? getNativeLimits = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _getMonitors = getMonitors ?? throw new ArgumentNullException(nameof(getMonitors));
        _getNativeLimits = getNativeLimits;
    }

    internal void BeginFrame()
    {
        VerifyOwner();
        if (_capturing) throw new InvalidOperationException("Cache raster source capture cannot reenter.");
        _framePolicy = null;
    }

    internal PortableBitmapCacheRasterPolicy CaptureFrame()
    {
        VerifyOwner();
        if (_framePolicy is { } policy)
        {
            if (!ReferenceEquals(_device, _context.DeviceIdentity))
                throw new InvalidOperationException("The cache frame lost its actual device identity.");
            return policy;
        }
        return (_framePolicy = Capture()).Value;
    }

    internal PortableBitmapCacheRasterPolicy Capture()
    {
        VerifyOwner();
        if (_capturing) throw new InvalidOperationException("Cache raster source capture cannot reenter.");
        _capturing = true;
        try
        {
            WgpuDeviceIdentity device = _context.DeviceIdentity;
            IPortablePrimaryDisplayRasterScaleSource? provider;
            PortablePrimaryDisplayRasterPolicy expected;
            if (OperatingSystem.IsWindows())
            {
                PortableWpfServiceRegistry.TryGetWindowActivationService(
                    PortableWpfServiceKey.PresentationFramework, out var registrar);
                provider = registrar as IPortablePrimaryDisplayRasterScaleSource;
                expected = PortablePrimaryDisplayRasterPolicy.WindowsSystemDpi;
            }
            else
            {
                provider = _getMonitors() as IPortablePrimaryDisplayRasterScaleSource;
                expected = PortablePrimaryDisplayRasterPolicy.PrimaryMonitorContentScale;
            }
            if (provider is null || !provider.TryGetPrimaryDisplayRasterScale(out var source) ||
                !source.IsValid || source.Policy != expected)
                throw new NotSupportedException("The actual source primary-display raster policy is unavailable.");
            uint width, height;
            if (_getNativeLimits is { } native)
                (width, height) = native();
            else if (!_context.TryGetCacheRasterLimits(out width, out height))
                throw new NotSupportedException("The live renderer has no owned cache raster limits.");
            VerifyOwner();
            if (!ReferenceEquals(device, _context.DeviceIdentity) || width == 0 || height == 0)
                throw new InvalidOperationException("Cache raster capture lost its original device or limits.");

            if (!ReferenceEquals(provider, _provider) || !source.Equals(_source) ||
                !ReferenceEquals(device, _device) || width != _policy.MaximumTextureWidth ||
                height != _policy.MaximumTextureHeight)
            {
                var policy = new PortableBitmapCacheRasterPolicy(source.ScaleX, source.ScaleY,
                    width, height, checked(_policy.SourceRevision + 1));
                _provider = provider;
                _source = source;
                _device = device;
                _policy = policy;
            }
            return _policy;
        }
        finally { _capturing = false; }
    }

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Cache raster policy belongs to its creating source thread.");
        if (_context.IsDisposed || !_context.IsInitialized || _context.IsDeviceLost)
            throw new ObjectDisposedException(nameof(WgpuContext), "The original cache raster device is unavailable.");
    }
}
