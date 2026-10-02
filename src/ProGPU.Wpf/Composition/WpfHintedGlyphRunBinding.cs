using System.Numerics;
using ProGPU.Backend.Native;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using SceneDrawingContext = ProGPU.Scene.DrawingContext;

namespace System.Windows.Media.ProGPU.Composition;

/// <summary>Concrete transport of the original provider, never a design-font annotation.</summary>
internal class WpfHintedGlyphRunBinding : IPortableHintedGlyphRunBinding
{
    private readonly State _state;
    private readonly WpfHintedTextLifetime.Lease _use;
    private WpfHintedGlyphRunBinding(State state, WpfHintedTextLifetime.Lease use)
    { _state = state; _use = use; }

    internal static void Validate(NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices,
        float sourceEmSize, Vector2 origin)
    {
        if (indices.IsEmpty || indices.Length > ushort.MaxValue || !float.IsFinite(sourceEmSize) || sourceEmSize <= 0 ||
            !float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
            throw new ArgumentException("A hinted source run requires a finite original em/origin and a nonempty canonical-size selection.");
        uint? font = null;
        sbyte? level = null;
        foreach (int index in indices)
        {
            if ((uint)index >= (uint)read.Glyphs.Length) throw new ArgumentOutOfRangeException(nameof(indices));
            var glyph = read.Glyphs[index];
            var owner = read.PositionedOwners[index];
            var run = read.Runs[checked((int)owner.RunIndex)];
            var source = read.FontSources[checked((int)glyph.FontIndex)];
            if (glyph.GlyphId > ushort.MaxValue || (font.HasValue && font.Value != glyph.FontIndex) ||
                (level.HasValue && level.Value != read.BidiLevels[index]) ||
                sourceEmSize / source.UnitsPerEm != run.SourceScale)
                throw new NotSupportedException("A source GlyphRun must retain one exact original font, em size and bidi level.");
            font = glyph.FontIndex;
            level = read.BidiLevels[index];
        }
    }

    // Caller owns geometry and source run until successful return.
    internal static WpfHintedGlyphRunBinding Adopt(IPortableHintedTextGlyphRun owner,
        NativeHintedGlyphResource resource, HintedGlyphGeometry geometry, float em, Vector2 origin,
        NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices, NativeHintedSourceGlyphFrame? frame = null)
    {
        var state = new State(owner, resource, geometry, em, origin, read, indices, frame);
        return new(state, state.Lifetime.Acquire());
    }

    // Explicit source-double producer only. It remains a concrete hinted native
    // binding for the existing MIL ownership/import path, not a font annotation.
    internal static IPortableDisplayGlyphRunBinding AdoptSource(IPortableHintedTextGlyphRun owner,
        NativeHintedGlyphResource resource, HintedGlyphGeometry geometry, double em, double dpi,
        NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices, NativeHintedSourceRunFrame frame)
    {
        float rasterEm = (float)em;
        if (!float.IsFinite(rasterEm) || (double)rasterEm != em || (double)read.DpiScale != dpi)
            throw new NotSupportedException("The original source em/DPI is not representable by the raster binding ABI.");
        Validate(read, indices, rasterEm, frame.RasterParagraphOrigin);
        var source = new SourceIdentity(em, dpi, frame);
        var state = new State(owner, resource, geometry, rasterEm, frame.RasterParagraphOrigin, read, indices, null, source);
        return new SourceBinding(state, state.Lifetime.Acquire());
    }

    private T Read<T>(T value) { ObjectDisposedException.ThrowIf(IsDisposed, this); return value; }
    public bool IsDisposed => _use.IsDisposed;
    public float FontRenderingEmSize => Read(_state.Em);
    public float DpiScale => Read(_state.Geometry.DpiScale);
    // WPF shaped GlyphRuns publish direction; the retained state and selection
    // validation keep the complete native embedding level.
    public sbyte BidiLevel => (sbyte)(Read(_state.Level) & 1);
    public Vector2 Origin => Read(_state.Origin);
    public PortableHintedGlyphSourceFrame SourceFrame => Read(_state.SourceFrame)
        ?? throw new NotSupportedException("This binding has no validated original source line frame.");
    public ReadOnlyMemory<ushort> GlyphIndices => Read<ReadOnlyMemory<ushort>>(_state.Ids);
    public ReadOnlyMemory<Vector2> GlyphPositions => Read<ReadOnlyMemory<Vector2>>(_state.Positions);
    public PortableRect InkBounds => Read(_state.Ink);
    public PortableRect BaselineRelativeInkBounds => Read(_state.RelativeInk);
    internal NativeHintedGlyphResource NativeResource => Read(_state.Resource);
    internal HintedGlyphGeometry Geometry => Read(_state.Geometry);
    internal uint FontIndex => Read(_state.FontIndex);
    internal ReadOnlyMemory<uint> NativeIndices => Read<ReadOnlyMemory<uint>>(_state.Indices);
    public virtual IPortableHintedGlyphRunBinding Retain() => new WpfHintedGlyphRunBinding(_state, _use.Retain());
    public IPortableHintedTextGlyphRun AcquireGlyphRun()
    {
        using var hold = _use.Retain();
        return _state.Owner.Retain();
    }
    public void Dispose() => _use.Dispose();

    private readonly record struct SourceIdentity(double Em, double Dpi, NativeHintedSourceRunFrame Frame);

    private sealed class SourceBinding(State state, WpfHintedTextLifetime.Lease use)
        : WpfHintedGlyphRunBinding(state, use), IPortableDisplayGlyphRunBinding
    {
        public double SourceEmSize => Read(_state.Source!.Value.Em);
        public double SourcePixelsPerDip => Read(_state.Source!.Value.Dpi);
        public PortableDisplayGlyphSourceFrame DisplaySourceFrame
        {
            get
            {
                var frame = Read(_state.Source!.Value.Frame);
                return new(checked((int)frame.LineIndex), frame.ParagraphBaselineY,
                    new(frame.SourceBaselineOrigin.X, frame.SourceBaselineOrigin.Y));
            }
        }
        public override IPortableHintedGlyphRunBinding Retain() => new SourceBinding(_state, _use.Retain());
    }

    private sealed class State : IDisposable
    {
        internal readonly WpfHintedTextLifetime Lifetime;
        internal readonly IPortableHintedTextGlyphRun Owner;
        internal readonly NativeHintedGlyphResource Resource;
        internal readonly HintedGlyphGeometry Geometry;
        internal readonly float Em;
        internal readonly sbyte Level;
        internal readonly uint FontIndex;
        internal readonly Vector2 Origin;
        internal readonly PortableHintedGlyphSourceFrame? SourceFrame;
        internal readonly SourceIdentity? Source;
        internal readonly ushort[] Ids;
        internal readonly Vector2[] Positions;
        internal readonly uint[] Indices;
        internal readonly PortableRect Ink, RelativeInk;

        internal State(IPortableHintedTextGlyphRun owner, NativeHintedGlyphResource resource,
            HintedGlyphGeometry geometry, float em, Vector2 origin,
            NativeHintedGlyphResourceReadLease read, ReadOnlySpan<int> indices, NativeHintedSourceGlyphFrame? frame,
            SourceIdentity? source = null)
        {
            Vector2 drawOrigin = source?.Frame.RasterParagraphOrigin ?? frame?.ParagraphOrigin ?? origin;
            Vector2 relativeOrigin = frame?.BaselineRelativeOrigin ?? Vector2.Zero;
            if (!SceneDrawingContext.TryGetHintedGlyphInkBounds(geometry, drawOrigin, out var ink, out bool hasInk) ||
                !SceneDrawingContext.TryGetHintedGlyphInkBounds(geometry, relativeOrigin, out var relative, out bool hasRelativeInk))
                throw new ArgumentException("Original hinted ink is not representable in the source frame.");
            Ids = new ushort[indices.Length];
            Positions = new Vector2[indices.Length];
            Indices = new uint[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                var glyph = read.Glyphs[indices[i]];
                Ids[i] = checked((ushort)glyph.GlyphId);
                Positions[i] = new(glyph.X, glyph.Y);
                Indices[i] = checked((uint)indices[i]);
            }
            Em = em; Origin = drawOrigin;
            if (frame is { } sourceFrame)
                SourceFrame = new(checked((int)sourceFrame.LineIndex), sourceFrame.ParagraphBaselineY, sourceFrame.SourceBaselineOrigin);
            FontIndex = read.Glyphs[indices[0]].FontIndex;
            Level = read.BidiLevels[indices[0]];
            Ink = hasInk ? new(ink.X, ink.Y, ink.Width, ink.Height) : PortableRect.Empty;
            RelativeInk = hasRelativeInk ? new(relative.X, relative.Y, relative.Width, relative.Height) : PortableRect.Empty;
            if (source is { } original)
            {
                // Translate the actual retained raster ink into the original
                // double baseline frame; do not narrow a baseline or move glyphs.
                double relativeY = (double)relative.Y - original.Frame.ParagraphBaselineY;
                if (hasRelativeInk && (!double.IsFinite(relativeY) || !double.IsFinite(relativeY + relative.Height)))
                    throw new NotSupportedException("The source-relative native ink frame is not finite.");
                RelativeInk = hasRelativeInk ? new(relative.X, relativeY, relative.Width, relative.Height) : PortableRect.Empty;
                Source = original;
            }
            Lifetime = new(this);
            Owner = owner; Resource = resource; Geometry = geometry;
        }

        public void Dispose()
        {
            Exception? failure = null;
            try { Geometry.Dispose(); }
            catch (Exception error) { failure = error; }
            try { Owner.Dispose(); }
            catch (Exception error)
            {
                if (failure is null) failure = error;
                else try { failure.Data["HintedSourceOwnerCleanupFailure"] = error; } catch { }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
