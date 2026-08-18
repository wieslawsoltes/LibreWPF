using System;
using System.Buffers;
using System.Text;
using System.Windows;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU;

public sealed class WpfPortablePresentationSourceBridge : IDisposable
{
    private const string TraceHitTestEnvironmentVariable = "PROGPU_WPF_TRACE_HIT_TEST";
    private const int HitTestOwnerBufferCapacity = 64;

    private static readonly bool s_traceHitTest = IsHitTestTraceEnabled();

    private readonly ProGpuWpfWindowHost _host;
    private readonly IPortablePresentationSourceHost _source;
    private readonly bool _ownsSource;
    private Func<double, double, object?>? _hitTestOverrideHandler;
    private Func<double, double, object?[]?>? _hitTestAllOverrideHandler;
    private PortableHitTestAllBufferOverride? _hitTestAllBufferOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestBoundsOverrideHandler;
    private PortableGeometryHitTestBufferOverride? _hitTestBoundsBufferOverrideHandler;
    private Func<double, double, double, double, object?[]?>? _hitTestEllipseBoundsOverrideHandler;
    private PortableGeometryHitTestBufferOverride? _hitTestEllipseBoundsBufferOverrideHandler;
    private bool _isDisposed;

    private WpfPortablePresentationSourceBridge(
        ProGpuWpfWindowHost host,
        IPortablePresentationSourceHost source,
        bool ownsSource)
    {
        _host = host;
        _source = source;
        _ownsSource = ownsSource;
    }

    public object Source => _source;

    public object? CompositionTarget => _source.CompositionTarget;

    public IntPtr Handle => _source.Handle;

    public object? RootVisual
    {
        get => _source.RootVisual;
        set
        {
            ThrowIfDisposed();
            _source.RootVisual = value;
            SyncHostRootVisual();
        }
    }

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        return TryCreate(host, 1.0, 1.0, out bridge);
    }

    public static bool TryCreate(
        ProGpuWpfWindowHost host,
        double dpiScaleX,
        double dpiScaleY,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);

        IPortablePresentationSourceHost source;
        try
        {
            source = PortablePresentationSourceHost.Create(dpiScaleX, dpiScaleY);
        }
        catch (PlatformNotSupportedException)
        {
            bridge = null;
            return false;
        }

        return TryBind(host, source, ownsSource: true, out bridge);
    }

    public static bool TryBind(
        ProGpuWpfWindowHost host,
        object presentationSource,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        return TryBind(host, presentationSource, ownsSource: false, out bridge);
    }

    public bool TrySetDeviceScale(double dpiScaleX, double dpiScaleY)
    {
        ThrowIfDisposed();
        _source.SetDeviceScale(dpiScaleX, dpiScaleY);
        return true;
    }

    public bool TrySetClientSize(double width, double height)
    {
        ThrowIfDisposed();
        _source.SetClientSize(width, height);
        return true;
    }

    public bool TrySetClientOrigin(double x, double y)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        _source.SetClientOrigin(x, y);
        return true;
    }

    public bool TryDispatchHwndSourceHook(int message, IntPtr wParam, IntPtr lParam, out IntPtr result, out bool handled)
    {
        ThrowIfDisposed();
        return _source.DispatchHwndSourceHook(message, wParam, lParam, out result, out handled);
    }

    public bool SyncHostRootVisual()
    {
        ThrowIfDisposed();

        object? rootVisual = RootVisual;
        if (ReferenceEquals(_host.WpfRootVisual, rootVisual))
        {
            return false;
        }

        _host.WpfRootVisual = rootVisual;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _source.RenderRequested -= OnSourceRenderRequested;
        _source.CursorRequested -= OnSourceCursorRequested;

        if (_hitTestOverrideHandler != null &&
            ReferenceEquals(_source.HitTestOverride, _hitTestOverrideHandler))
        {
            _source.HitTestOverride = null;
        }

        if (_hitTestAllOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllOverride, _hitTestAllOverrideHandler))
        {
            _source.HitTestAllOverride = null;
        }

        if (_hitTestAllBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestAllBufferOverride, _hitTestAllBufferOverrideHandler))
        {
            _source.HitTestAllBufferOverride = null;
        }

        if (_hitTestBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsOverride, _hitTestBoundsOverrideHandler))
        {
            _source.HitTestBoundsOverride = null;
        }

        if (_hitTestBoundsBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestBoundsBufferOverride, _hitTestBoundsBufferOverrideHandler))
        {
            _source.HitTestBoundsBufferOverride = null;
        }

        if (_hitTestEllipseBoundsOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsOverride, _hitTestEllipseBoundsOverrideHandler))
        {
            _source.HitTestEllipseBoundsOverride = null;
        }

        if (_hitTestEllipseBoundsBufferOverrideHandler != null &&
            ReferenceEquals(_source.HitTestEllipseBoundsBufferOverride, _hitTestEllipseBoundsBufferOverrideHandler))
        {
            _source.HitTestEllipseBoundsBufferOverride = null;
        }

        object? rootVisual = _source.RootVisual;
        if (ReferenceEquals(_host.WpfRootVisual, rootVisual))
        {
            _host.WpfRootVisual = null;
        }

        if (_ownsSource)
        {
            _source.Dispose();
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private static bool TryBind(
        ProGpuWpfWindowHost host,
        object presentationSource,
        bool ownsSource,
        out WpfPortablePresentationSourceBridge? bridge)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(presentationSource);

        if (presentationSource is not IPortablePresentationSourceHost source)
        {
            bridge = null;
            return false;
        }

        bridge = new WpfPortablePresentationSourceBridge(host, source, ownsSource);
        bridge.SubscribeToSource();
        bridge.InstallHitTestOverrides();
        bridge.SyncHostRootVisual();
        return true;
    }

    private void SubscribeToSource()
    {
        _source.RenderRequested += OnSourceRenderRequested;
        _source.CursorRequested += OnSourceCursorRequested;
    }

    private void InstallHitTestOverrides()
    {
        _hitTestOverrideHandler = TryHitTestOwner;
        _hitTestAllOverrideHandler = HitTestOwners;
        _hitTestAllBufferOverrideHandler = HitTestOwners;
        _hitTestBoundsOverrideHandler = HitTestBoundsOwners;
        _hitTestBoundsBufferOverrideHandler = HitTestBoundsOwners;
        _hitTestEllipseBoundsOverrideHandler = HitTestEllipseBoundsOwners;
        _hitTestEllipseBoundsBufferOverrideHandler = HitTestEllipseBoundsOwners;

        _source.HitTestOverride = _hitTestOverrideHandler;
        _source.HitTestAllOverride = _hitTestAllOverrideHandler;
        _source.HitTestAllBufferOverride = _hitTestAllBufferOverrideHandler;
        _source.HitTestBoundsOverride = _hitTestBoundsOverrideHandler;
        _source.HitTestBoundsBufferOverride = _hitTestBoundsBufferOverrideHandler;
        _source.HitTestEllipseBoundsOverride = _hitTestEllipseBoundsOverrideHandler;
        _source.HitTestEllipseBoundsBufferOverride = _hitTestEllipseBoundsBufferOverrideHandler;
    }

    private void OnSourceRenderRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!SyncHostRootVisual())
        {
            _host.RequestRenderAndWakeNativeLoop();
        }
    }

    private void OnSourceCursorRequested(object? sender, EventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _host.ApplyPortableCursor(ToWpfCursor(_source.RequestedCursorName ?? _source.RequestedCursor?.ToString()));
    }

    private object? TryHitTestOwner(double rootX, double rootY)
    {
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (_host.TryHitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount))
            {
                ReadOnlySpan<object?> owners = ownerBuffer.AsSpan(0, ownerCount);
                if (TrySelectPointerInputOwner(owners, out object? selectedOwner))
                {
                    TraceHitTestOwners(rootX, rootY, owners, selectedOwner);
                    return selectedOwner;
                }

                object? handledMiss = _host.HasGpuHitTestCache ? Source : null;
                TraceHitTestOwners(rootX, rootY, owners, handledMiss);
                return handledMiss;
            }

            object? fallbackOwner = _host.HasGpuHitTestCache ? Source : null;
            TraceHitTestOwners(rootX, rootY, ReadOnlySpan<object?>.Empty, fallbackOwner, hasOwners: false);
            return fallbackOwner;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    internal static bool TrySelectPointerInputOwner(ReadOnlySpan<object?> owners, out object? selectedOwner)
    {
        selectedOwner = null;

        for (int i = 0; i < owners.Length; i++)
        {
            object? owner = owners[i];
            if (owner == null)
            {
                continue;
            }

            if (!TryNormalizePointerInputOwner(owner, out object? normalizedOwner) ||
                normalizedOwner == null)
            {
                continue;
            }

            bool hasMoreSpecificDescendant = false;
            for (int j = 0; j < owners.Length; j++)
            {
                if (j == i || owners[j] == null ||
                    !TryNormalizePointerInputOwner(owners[j]!, out object? otherOwner) ||
                    otherOwner == null ||
                    ReferenceEquals(normalizedOwner, otherOwner))
                {
                    continue;
                }

                if (IsVisualOwnerDescendantOrSelf(otherOwner, normalizedOwner))
                {
                    hasMoreSpecificDescendant = true;
                    break;
                }
            }

            if (hasMoreSpecificDescendant)
            {
                continue;
            }

            // Broad container and pointer-infrastructure primitives may precede a
            // descendant hit. After removing those ancestors, preserve ProGPU's
            // descending Z order so a ComboBox toggle still wins over the editor in
            // its underlying sibling subtree.
            selectedOwner = normalizedOwner;
            return true;
        }

        return false;
    }

    private static bool TryNormalizePointerInputOwner(object owner, out object? normalizedOwner)
    {
        normalizedOwner = null;
        if (IsTransparentPointerOverlay(owner))
        {
            return false;
        }

        object? firstEnabledOwner = null;
        object? current = owner;
        for (int depth = 0; current != null && depth < 128; depth++)
        {
            // An input-disabled or explicitly transparent branch should expose the
            // next lower Z-order hit, not promote an enabled ancestor over it.
            if (IsTransparentPointerOverlay(current) || !IsEnabledInputOwner(current))
            {
                return false;
            }

            firstEnabledOwner ??= current;
            if (IsWindowOwner(current))
            {
                normalizedOwner = firstEnabledOwner;
                return normalizedOwner != null;
            }

            if (!IsPointerInputInfrastructure(current))
            {
                normalizedOwner = current;
                return true;
            }

            current = TryGetVisualParent(current);
        }

        normalizedOwner = firstEnabledOwner;
        return normalizedOwner != null;
    }

    private static bool IsEnabledInputOwner(object owner)
    {
        return owner is not IPortableVisualOwnerHost host || host.IsPortableInputEnabled;
    }

    private static bool IsTransparentPointerOverlay(object owner)
    {
        return owner is IPortableVisualOwnerHost
        {
            PortableVisualOwnerKind: PortableVisualOwnerKind.TransparentPointerOverlay
        };
    }

    private static bool IsPointerInputInfrastructure(object owner)
    {
        return owner is IPortableVisualOwnerHost
        {
            PortableVisualOwnerKind: PortableVisualOwnerKind.PointerInfrastructure
        };
    }

    private static bool IsWindowOwner(object owner)
    {
        return owner is IPortableVisualOwnerHost
        {
            PortableVisualOwnerKind: PortableVisualOwnerKind.Window
        };
    }

    private static object? TryGetVisualParent(object current)
    {
        return current is IPortableVisualOwnerHost host ? host.PortableVisualParent : null;
    }

    private static void TraceHitTestOwners(
        double rootX,
        double rootY,
        ReadOnlySpan<object?> owners,
        object? selectedOwner,
        bool hasOwners = true)
    {
        if (!s_traceHitTest)
        {
            return;
        }

        string ownerList = !hasOwners
            ? "<none>"
            : DescribeHitTestOwners(owners);
        Console.Error.WriteLine(
            $"ProGPU WPF GPU hit-test ({rootX:0.###},{rootY:0.###}) owners=[{ownerList}] selected={DescribeHitTestOwner(selectedOwner)}");
    }

    private static string DescribeHitTestOwners(ReadOnlySpan<object?> owners)
    {
        if (owners.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < owners.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(DescribeHitTestOwner(owners[i]));
        }

        return builder.ToString();
    }

    private static bool IsHitTestTraceEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(TraceHitTestEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeHitTestOwner(object? owner)
    {
        if (owner == null)
        {
            return "<null>";
        }

        if (owner is IPortableVisualOwnerHost)
        {
            return "PortableVisualOwnerHost";
        }

        return owner is string label && !string.IsNullOrEmpty(label)
            ? label
            : "Owner";
    }

    private object?[]? HitTestOwners(double rootX, double rootY)
    {
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            if (!HitTestOwners(rootX, rootY, ownerBuffer, out int ownerCount))
            {
                return _host.HasGpuHitTestCache ? Array.Empty<object>() : null;
            }

            if (ownerCount == 0)
            {
                return Array.Empty<object?>();
            }

            var owners = new object?[ownerCount];
            ownerBuffer.AsSpan(0, ownerCount).CopyTo(owners);
            return owners;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    private bool HitTestOwners(double rootX, double rootY, Span<object?> owners, out int ownerCount)
    {
        if (!_host.TryHitTestOwners(rootX, rootY, owners, out ownerCount))
        {
            ownerCount = 0;
            return _host.HasGpuHitTestCache;
        }

        ownerCount = FilterTransparentPointerOverlays(owners[..ownerCount]);
        return true;
    }

    internal static int FilterTransparentPointerOverlays(Span<object?> owners)
    {
        int writeIndex = 0;
        for (int i = 0; i < owners.Length; i++)
        {
            object? owner = owners[i];
            if (owner != null && IsTransparentPointerOverlay(owner))
            {
                continue;
            }

            owners[writeIndex++] = owner;
        }

        for (int i = writeIndex; i < owners.Length; i++)
        {
            owners[i] = null;
        }

        return writeIndex;
    }

    internal static int FilterVisualOwnerSubtree(Span<object?> candidates, object? rootVisual)
    {
        if (rootVisual == null)
        {
            candidates.Clear();
            return 0;
        }

        int writeIndex = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            object? candidate = candidates[i];
            object? owner = candidate is PortableGeometryHitTestCandidate geometryCandidate
                ? geometryCandidate.VisualHit
                : candidate;
            if (owner == null || !IsVisualOwnerDescendantOrSelf(owner, rootVisual))
            {
                continue;
            }

            candidates[writeIndex++] = candidate;
        }

        for (int i = writeIndex; i < candidates.Length; i++)
        {
            candidates[i] = null;
        }

        return writeIndex;
    }

    private static bool IsVisualOwnerDescendantOrSelf(object owner, object rootVisual)
    {
        object? current = owner;
        for (int depth = 0; current != null && depth < 128; depth++)
        {
            if (ReferenceEquals(current, rootVisual))
            {
                return true;
            }

            current = TryGetVisualParent(current);
        }

        return false;
    }

    private object?[]? HitTestBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        return HitTestGeometryOwners(minX, minY, maxX, maxY, isEllipse: false);
    }

    private bool HitTestBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        if (_host.TryQueryHitTestBoundsCandidates(minX, minY, maxX, maxY, candidates, out candidateCount))
        {
            return true;
        }

        candidateCount = 0;
        return _host.HasGpuHitTestCache;
    }

    private object?[]? HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY)
    {
        return HitTestGeometryOwners(minX, minY, maxX, maxY, isEllipse: true);
    }

    private bool HitTestEllipseBoundsOwners(double minX, double minY, double maxX, double maxY, Span<object?> candidates, out int candidateCount)
    {
        if (_host.TryQueryHitTestEllipseCandidates(minX, minY, maxX, maxY, candidates, out candidateCount))
        {
            return true;
        }

        candidateCount = 0;
        return _host.HasGpuHitTestCache;
    }

    private object?[]? HitTestGeometryOwners(double minX, double minY, double maxX, double maxY, bool isEllipse)
    {
        object?[] candidateBuffer = ArrayPool<object?>.Shared.Rent(HitTestOwnerBufferCapacity);
        try
        {
            bool hit = isEllipse
                ? HitTestEllipseBoundsOwners(minX, minY, maxX, maxY, candidateBuffer, out int candidateCount)
                : HitTestBoundsOwners(minX, minY, maxX, maxY, candidateBuffer, out candidateCount);
            if (!hit)
            {
                return null;
            }

            if (candidateCount == 0)
            {
                return Array.Empty<object>();
            }

            var candidates = new object?[candidateCount];
            candidateBuffer.AsSpan(0, candidateCount).CopyTo(candidates);
            return candidates;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(candidateBuffer, clearArray: true);
        }
    }

    internal static WpfCursor ToWpfCursor(string? cursorName)
    {
        return cursorName switch
        {
            "No" => WpfCursor.No,
            "Arrow" => WpfCursor.Arrow,
            "AppStarting" => WpfCursor.AppStarting,
            "Cross" => WpfCursor.Crosshair,
            "IBeam" => WpfCursor.IBeam,
            "SizeAll" => WpfCursor.SizeAll,
            "SizeNESW" => WpfCursor.SizeNESW,
            "SizeNS" => WpfCursor.SizeNS,
            "SizeNWSE" => WpfCursor.SizeNWSE,
            "SizeWE" => WpfCursor.SizeWE,
            "Wait" => WpfCursor.Wait,
            "Hand" => WpfCursor.Hand,
            _ => WpfCursor.Default
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
