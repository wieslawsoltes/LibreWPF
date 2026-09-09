using System.Buffers;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU;

/// <summary>Host-owned synchronous adapter; all geometry queries execute in ProGPU.</summary>
internal sealed class ProGpuWpfNativeHitTesting
{
    private const int StackResultLimit = 64;
    private NativeGpuHitTestOwnerSnapshot<object> _pendingOwners;
    internal NativeGpuHitTestRequestToken PendingRequest { get; private set; }
    internal NativeGpuHitTestResult LastSummary { get; private set; }

    internal bool Query(
        NativeGpuHitTestOwnerSnapshot<object> owners,
        NativeGpuHitTestQuery query,
        Span<object?> destination,
        bool geometryCandidates,
        out int count)
    {
        count = 0;
        if (destination.IsEmpty || !owners.IsValid) return false;
        if (PendingRequest.IsValid)
            throw new InvalidOperationException(
                "A failed native hit-test request still owns its readback. Recover or dispose its compositor before another query.");
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Synchronous WPF input requires desktop native query completion.");

        int capacity = Math.Min(destination.Length, NativeGpuHitTestQuery.MaximumResultCount);
        Span<NativeGpuHitTestResult> stack = stackalloc NativeGpuHitTestResult[Math.Min(capacity, StackResultLimit)];
        // The same bounded expansion rule as the managed owner adapter: a
        // missing owner may hide later results beyond the first result window.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            NativeGpuHitTestResult[]? rented = null;
            Span<NativeGpuHitTestResult> results = capacity <= stack.Length
                ? stack[..capacity]
                : (rented = ArrayPool<NativeGpuHitTestResult>.Shared.Rent(capacity)).AsSpan(0, capacity);
            try
            {
                query.Flags = (query.Flags & ~(uint)NativeGpuHitTestQueryFlags.ResultCapacityMask) | (uint)capacity;
                // Publish ownership before waiting. On an exception these
                // fields retain the original token/map until target disposal.
                NativeGpuHitTestRequestToken token = owners.BeginQuery(query);
                _pendingOwners = owners;
                PendingRequest = token;
                int hitCount = _pendingOwners.Wait(token, results, out var summary);
                PendingRequest = default;
                _pendingOwners = default;
                LastSummary = summary;
                count = geometryCandidates
                    ? CopyCandidates(owners, token, results[..hitCount], destination)
                    : owners.CopyOwners(token, results[..hitCount], destination);
                int expanded = GetExpandedCapacity(count, destination.Length, hitCount, summary.Hit, capacity);
                if (attempt != 0 || expanded == capacity) return true;
                // Release references copied by the first attempt before a
                // replacement result set (or exception) can leave them hidden.
                destination[..count].Clear();
                count = 0;
                capacity = expanded;
            }
            finally
            {
                if (rented is not null) ArrayPool<NativeGpuHitTestResult>.Shared.Return(rented);
            }
        }
        return true;
    }

    internal static int GetExpandedCapacity(
        int resolved, int requested, int hitCount, uint totalHits, int capacity)
        => resolved < requested && totalHits > (uint)hitCount
            ? (int)Math.Max((uint)capacity, Math.Min(totalHits, NativeGpuHitTestQuery.MaximumResultCount))
            : capacity;

    private static int CopyCandidates(
        NativeGpuHitTestOwnerSnapshot<object> owners, NativeGpuHitTestRequestToken token,
        ReadOnlySpan<NativeGpuHitTestResult> results, Span<object?> destination)
    {
        int count = 0;
        for (int i = 0; i < results.Length && count < destination.Length; i++)
            if (owners.TryGetOwner(token, results[i], out object? owner))
                destination[count++] = new PortableGeometryHitTestCandidate(owner, results[i].IntersectionDetail);
        return count;
    }

    // Only called after the owning native compositor has released its engine.
    internal void ResetAfterCompositorDisposal()
    {
        PendingRequest = default;
        _pendingOwners = default;
        LastSummary = default;
    }
}
