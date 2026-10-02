using System.Runtime.ExceptionServices;
using ProGPU.Backend.Native;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>A batch's original producer owners, independent of the source GlyphRun and channel import.</summary>
internal sealed class WpfNativeHintedGlyphResources : IDisposable
{
    private readonly State _state;
    private readonly WpfHintedTextLifetime.Lease _use;
    private WpfNativeHintedGlyphResources(State state, WpfHintedTextLifetime.Lease use)
    { _state = state; _use = use; }

    // Retains, rather than steals, the builder's owners. Failed construction
    // leaves the builder authoritative for every acquisition already made.
    internal static WpfNativeHintedGlyphResources Create(
        IReadOnlyList<(uint Handle, WpfHintedGlyphRunBinding Owner)> sources)
    {
        var owners = new WpfHintedGlyphRunBinding[sources.Count];
        int acquired = 0;
        try
        {
            for (; acquired < owners.Length; acquired++)
                owners[acquired] = (WpfHintedGlyphRunBinding)sources[acquired].Owner.Retain();
            var state = new State(sources, owners);
            return new(state, state.Lifetime.Acquire());
        }
        catch (Exception failure)
        {
            for (int i = acquired - 1; i >= 0; i--)
                try { owners[i].Dispose(); }
                catch (Exception cleanup) { try { failure.Data[$"HintedBatchCleanup:{i}"] = cleanup; } catch { } }
            throw;
        }
    }

    internal WpfNativeHintedGlyphResources Retain() => new(_state, _use.Retain());
    internal int BindingCount { get { using var hold = _use.Retain(); return _state.Bindings.Length; } }
    internal bool HasSameProducerTopology(WpfNativeHintedGlyphResources other)
    {
        using var first = _use.Retain();
        using var second = other._use.Retain();
        if (_state.Bindings.Length != other._state.Bindings.Length) return false;
        for (int i = 0; i < _state.Bindings.Length; i++)
        {
            var left = _state.Bindings[i]; var right = other._state.Bindings[i];
            if (left.GlyphRunHandle != right.GlyphRunHandle || left.FontIndex != right.FontIndex ||
                !ReferenceEquals(_state.Resources[left.ResourceIndex], other._state.Resources[right.ResourceIndex])) return false;
        }
        return true;
    }
    internal NativeMilBatchMetrics Apply(NativeMilChannel channel, ReadOnlySpan<byte> bytes)
    {
        using var hold = _use.Retain();
        // One exact provider transaction, including mixed raw/source resources.
        // Missing source import is an error, never a retry through raw import.
        return _state.HasSourceGeometry
            ? channel.ApplyWithSourceGlyphResourcesWithMetrics(bytes, _state.Resources, _state.Bindings, _state.Indices)
            : channel.ApplyWithHintedGlyphResourcesWithMetrics(bytes, _state.Resources, _state.Bindings, _state.Indices);
    }
    public void Dispose() => _use.Dispose();

    private sealed class State : IDisposable
    {
        internal readonly WpfHintedTextLifetime Lifetime;
        internal readonly NativeHintedGlyphResource[] Resources;
        internal readonly NativeMilHintedGlyphBinding[] Bindings;
        internal readonly uint[] Indices;
        internal readonly bool HasSourceGeometry;
        private readonly WpfHintedGlyphRunBinding[] _owners;

        internal State(IReadOnlyList<(uint Handle, WpfHintedGlyphRunBinding Owner)> sources, WpfHintedGlyphRunBinding[] owners)
        {
            var resources = new List<NativeHintedGlyphResource>();
            var resourceIndices = new Dictionary<NativeHintedGlyphResource, uint>(ReferenceEqualityComparer.Instance);
            int count = 0;
            foreach (var owner in owners) count = checked(count + owner.NativeIndices.Length);
            Indices = new uint[count];
            Bindings = new NativeMilHintedGlyphBinding[owners.Length];
            int offset = 0;
            for (int i = 0; i < owners.Length; i++)
            {
                var owner = owners[i];
                HasSourceGeometry |= owner.NativeResource.HasSourceGeometry;
                if (!resourceIndices.TryGetValue(owner.NativeResource, out uint resourceIndex))
                {
                    resourceIndex = checked((uint)resources.Count);
                    resourceIndices.Add(owner.NativeResource, resourceIndex);
                    resources.Add(owner.NativeResource);
                }
                owner.NativeIndices.Span.CopyTo(Indices.AsSpan(offset));
                Bindings[i] = new()
                {
                    GlyphRunHandle = sources[i].Handle, ResourceIndex = resourceIndex, FontIndex = owner.FontIndex,
                    PositionedIndexStart = checked((uint)offset), PositionedIndexCount = checked((uint)owner.NativeIndices.Length),
                    LogicalOrigin = owner.Origin, Basis = System.Numerics.Matrix3x2.Identity
                };
                offset = checked(offset + owner.NativeIndices.Length);
            }
            Resources = resources.ToArray();
            Lifetime = new(this);
            _owners = owners;
        }

        public void Dispose()
        {
            Exception? failure = null;
            foreach (var owner in _owners)
                try { owner.Dispose(); }
                catch (Exception error) { failure ??= error; }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
