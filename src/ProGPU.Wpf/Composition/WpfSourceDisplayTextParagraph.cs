using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using ProGPU.Backend.Native;
using ProGPU.Scene.Native;
using ProGPU.Text;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

// Explicit adoption of a complete native source generation. No ordinary
// formatter implements IPortableDisplayTextFormatting or selects device policy.
internal sealed class WpfSourceDisplayTextParagraph : IPortableDisplayTextParagraph
{
    private readonly object _gate = new();
    private readonly Generation _generation;
    private readonly WpfHintedTextLifetime.Lease _use;
    private WpfHintedTextRetirement _failedReflow;
    private bool _reflowing;

    private WpfSourceDisplayTextParagraph(Generation generation, WpfHintedTextLifetime.Lease use)
    { _generation = generation; _use = use; }

    // Source ownership transfers only on successful return. The resource is
    // always prepared from this exact source owner, never paired by equal arrays.
    internal static WpfSourceDisplayTextParagraph Adopt(NativeHintedSourceParagraph source, string text,
        NativeHintedProjectionPolicy projection, NativeHintedCoverage coverage)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(text);
        if (source.Options.MaximumWidth == 0.0)
            throw new NotSupportedException("Native unbounded width does not admit source zero-content-width Display.");
        ValidateSourceText(source.SourceScalars, text);
        NativeHintedGlyphResource? resource = null;
        try
        {
            resource = source.PrepareGlyphResourceWithNominalMetrics(projection, coverage);
            var generation = new Generation(source, resource, text);
            return Create(generation, generation.Lifetime.Acquire());
        }
        catch (Exception failure)
        {
            try { resource?.Dispose(); }
            catch (Exception cleanup) { Attach(failure, cleanup); }
            throw;
        }
    }

    internal static void ValidateSourceText(ReadOnlySpan<NativeTextScalar> scalars, string text)
    {
        int position = 0;
        foreach (var scalar in scalars)
        {
            if (scalar.InputIndex != position || Rune.DecodeFromUtf16(text.AsSpan(position), out Rune value, out int length) != OperationStatus.Done ||
                scalar.CodePoint != value.Value || scalar.InputLength != length)
                throw new ArgumentException("Source text differs from the original retained UTF-16 generation.", nameof(text));
            position = checked(position + length);
        }
        if (position != text.Length) throw new ArgumentException("Source text must cover the whole original generation.", nameof(text));
    }

    private static WpfSourceDisplayTextParagraph Create(Generation generation, WpfHintedTextLifetime.Lease use)
    {
        try { return new(generation, use); }
        catch (Exception failure) { try { use.Dispose(); } catch (Exception cleanup) { Attach(failure, cleanup); } throw; }
    }
    private static void Attach(Exception failure, Exception cleanup)
    { try { failure.Data["SourceDisplayRetirementFailure"] = cleanup; } catch { } }
    private T Read<T>(T value) { lock (_gate) { ObjectDisposedException.ThrowIf(IsDisposed, this); return value; } }
    public bool IsDisposed => _use.IsDisposed;
    public float DpiScale => Read(_generation.Raster.DpiScale);
    public ReadOnlyMemory<char> SourceText => Read(_generation.Text.AsMemory());
    public double SourceEmSize => Read(_generation.Source.Options.EmSize);
    public double SourcePixelsPerDip => Read(_generation.Source.Options.PixelsPerDip);
    public ReadOnlyMemory<PortableDisplayTextStyle> SourceStyles => Read<ReadOnlyMemory<PortableDisplayTextStyle>>(_generation.Styles);
    public ReadOnlyMemory<PortableDisplayTextGlyphMetrics> DisplayGlyphMetrics => Read<ReadOnlyMemory<PortableDisplayTextGlyphMetrics>>(_generation.Metrics);
    public ReadOnlyMemory<PortableDisplayTextLineMetrics> DisplayLineMetrics => Read<ReadOnlyMemory<PortableDisplayTextLineMetrics>>(_generation.LineMetrics);
    public PortableDisplayTextIntrinsicWidths? DisplayIntrinsicWidths => Read<PortableDisplayTextIntrinsicWidths?>(null);
    ReadOnlyMemory<PortableTextGlyph> IPortableTextParagraph.Glyphs => Read<ReadOnlyMemory<PortableTextGlyph>>(_generation.Glyphs);
    ReadOnlyMemory<PortableTextLineInfo> IPortableTextParagraph.Lines => Read<ReadOnlyMemory<PortableTextLineInfo>>(_generation.Lines);
    ReadOnlyMemory<PortableHintedTextGlyph> IPortableHintedTextParagraph.Glyphs => Read(_generation.Raster.Glyphs);
    ReadOnlyMemory<PortableHintedTextLine> IPortableHintedTextParagraph.Lines => Read(_generation.Raster.Lines);
    ReadOnlyMemory<PortableHintedTextClusterBox> IPortableHintedTextParagraph.Boxes => Read(ReadOnlyMemory<PortableHintedTextClusterBox>.Empty);
    ReadOnlyMemory<PortableHintedTextCaret> IPortableHintedTextParagraph.Carets => Read(ReadOnlyMemory<PortableHintedTextCaret>.Empty);
    public IPortableHintedTextParagraph Retain() { lock (_gate) return Create(_generation, _use.Retain()); }
    public PortableTextHit HitTest(int lineIndex, float distance) => throw new NotSupportedException("Source Display hit testing requires the double query.");
    public float GetCaretDistance(int lineIndex, PortableTextHit hit) => throw new NotSupportedException("Source Display caret distance requires the double query.");

    public PortableTextHit HitTestDisplay(int lineIndex, double distance)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var hit = _generation.Source.HitTestLine(lineIndex, distance);
            return new(hit.InputPosition, hit.Trailing != 0);
        }
    }
    public double GetDisplayCaretDistance(int lineIndex, PortableTextHit hit)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _generation.Source.GetLineCaret(lineIndex, hit.Position, hit.Trailing).X;
        }
    }
    public int GetNextLogicalCaret(int lineIndex, int position, bool previous)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if ((uint)lineIndex >= (uint)_generation.LogicalCarets.Length) throw new ArgumentOutOfRangeException(nameof(lineIndex));
            int[] stops = _generation.LogicalCarets[lineIndex];
            if (stops.Length == 0) throw new NotSupportedException("The original source line has no retained caret stops.");
            int index = Array.BinarySearch(stops, position);
            index = index >= 0 ? index + (previous ? -1 : 1) : ~index - (previous ? 1 : 0);
            return stops[Math.Clamp(index, 0, stops.Length - 1)];
        }
    }
    public int GetSelection(int lineIndex, int start, int end, Span<PortableRect> rectangles)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var temporary = ArrayPool<NativeHintedSourceRectangle>.Shared.Rent(rectangles.Length);
            try
            {
                int count = _generation.Source.GetLineSelection(lineIndex, start, end, temporary.AsSpan(0, rectangles.Length));
                for (int i = 0; i < count; ++i) { var r = temporary[i]; rectangles[i] = new(r.X, r.Y, r.Width, r.Height); }
                return count;
            }
            finally { ArrayPool<NativeHintedSourceRectangle>.Shared.Return(temporary); }
        }
    }
    public IPortableDisplayTextParagraph ReflowDisplay(int inputStart, double maximumWidth)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_reflowing) throw new InvalidOperationException("A source paragraph cannot recursively publish a continuation.");
            if (maximumWidth == 0.0) throw new NotSupportedException("Source zero content width remains unqualified.");
            _reflowing = true;
            NativeHintedSourceParagraph? candidate = null;
            try
            {
                _failedReflow.Dispose(); ObjectDisposedException.ThrowIf(IsDisposed, this);
                candidate = _generation.Source.Reflow(inputStart, maximumWidth);
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                var result = Adopt(candidate, _generation.Text, _generation.Resource.Projection, _generation.Resource.Coverage);
                candidate = null;
                return result;
            }
            catch (Exception failure)
            {
                if (candidate is not null) { _failedReflow.Capture(candidate); _failedReflow.DisposePreservingFailure(failure); }
                throw;
            }
            finally { _reflowing = false; }
        }
    }
    public IPortableHintedTextGlyphRun AcquireGlyphRun(ReadOnlySpan<int> positionedIndices)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            int[] indices = positionedIndices.ToArray();
            foreach (int index in indices) if ((uint)index >= (uint)_generation.Glyphs.Length) throw new ArgumentOutOfRangeException(nameof(positionedIndices));
            return GlyphRun.Create(_generation, _use.Retain(), indices);
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            try { _use.Dispose(); }
            catch (Exception failure) { _failedReflow.DisposePreservingFailure(failure); throw; }
            _failedReflow.Dispose();
        }
    }

    private sealed class Generation : IDisposable
    {
        private readonly object _geometryGate = new();
        private HintedGlyphGeometry? _geometry;
        internal readonly NativeHintedSourceParagraph Source;
        internal readonly NativeHintedGlyphResource Resource;
        internal readonly WpfHintedTextParagraph Raster;
        internal readonly WpfHintedTextLifetime Lifetime;
        internal readonly string Text;
        internal readonly PortableDisplayTextStyle[] Styles;
        internal readonly PortableDisplayTextGlyphMetrics[] Metrics;
        internal readonly PortableDisplayTextLineMetrics[] LineMetrics;
        internal readonly PortableTextGlyph[] Glyphs;
        internal readonly PortableTextLineInfo[] Lines;
        internal readonly int[][] LogicalCarets;
        internal Generation(NativeHintedSourceParagraph source, NativeHintedGlyphResource resource, string text)
        {
            using var read = resource.AcquireReadLease();
            if (!read.HasSourceMetrics || source.GlyphMetrics.Length != read.Glyphs.Length || source.LineMetrics.Length != read.Lines.Length)
                throw new InvalidOperationException("Source and raster arrays must belong to one complete retained generation.");
            Styles = new PortableDisplayTextStyle[source.SourceStyles.Length];
            for (int i = 0; i < Styles.Length; ++i) { var s = source.SourceStyles[i]; Styles[i] = new(s.EmSize, s.Ascent, s.Descent); }
            Metrics = new PortableDisplayTextGlyphMetrics[read.Glyphs.Length]; Glyphs = new PortableTextGlyph[Metrics.Length];
            for (int i = 0; i < Metrics.Length; ++i)
            {
                var m = source.GlyphMetrics[i]; var g = read.Glyphs[i];
                if (m.AdvanceY != 0.0 || m.Cluster != g.Cluster) throw new NotSupportedException("Source horizontal occurrence identity changed.");
                Metrics[i] = new(m.X, m.Y, m.AdvanceX);
                Glyphs[i] = new(g.GlyphId, g.Cluster, read.ClusterEnds[i], g.X, g.Y, g.AdvanceX, read.BidiLevels[i], g.FontIndex);
            }
            LineMetrics = new PortableDisplayTextLineMetrics[read.Lines.Length]; Lines = new PortableTextLineInfo[LineMetrics.Length];
            LogicalCarets = new int[Lines.Length][];
            for (int i = 0; i < Lines.Length; ++i)
            {
                var m = source.LineMetrics[i]; var l = read.Lines[i];
                if (m.GlyphStart != l.GlyphStart || m.GlyphCount != l.GlyphCount) throw new InvalidOperationException("Source line partition changed.");
                LineMetrics[i] = new(m.Width, m.Top, m.Height, m.BaselineOffset, m.BaselineY);
                Lines[i] = new(checked((int)l.GlyphStart), checked((int)l.GlyphCount), l.InputStart, l.InputEnd, l.Width, (float)m.Top, l.Height);
                var logical = new SortedSet<int>();
                foreach (var caret in source.Carets) if (caret.LineIndex == i) logical.Add(caret.InputPosition);
                LogicalCarets[i] = [.. logical];
            }
            Text = text; Source = source; Resource = resource; Lifetime = new(this);
            Raster = WpfHintedTextParagraph.Adopt(resource, text);
        }
        internal HintedGlyphGeometry SelectGeometry(ReadOnlySpan<int> indices)
        {
            lock (_geometryGate) { _geometry ??= NativeHintedGlyphGeometryFactory.Create(Resource); return _geometry.SelectOccurrences(indices); }
        }
        public void Dispose()
        {
            lock (_geometryGate)
            {
                Exception? failure = null;
                try { _geometry?.Dispose(); } catch (Exception error) { failure = error; }
                try { Raster.Dispose(); } catch (Exception error) { if (failure is null) failure = error; else Attach(failure, error); }
                try { Source.Dispose(); } catch (Exception error) { if (failure is null) failure = error; else Attach(failure, error); }
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    private sealed class GlyphRun : IPortableHintedTextGlyphRun, IPortableDisplayGlyphRunBindingFactory
    {
        private readonly object _gate = new();
        private readonly Generation _generation;
        private readonly WpfHintedTextLifetime.Lease _use;
        private readonly int[] _indices;
        private readonly uint[] _nativeIndices;
        private GlyphRun(Generation generation, WpfHintedTextLifetime.Lease use, int[] indices)
        {
            _generation = generation; _use = use; _indices = indices;
            _nativeIndices = new uint[indices.Length];
            for (int i = 0; i < indices.Length; ++i) _nativeIndices[i] = checked((uint)indices[i]);
        }
        internal static GlyphRun Create(Generation generation, WpfHintedTextLifetime.Lease use, int[] indices)
        {
            try { return new(generation, use, indices); }
            catch (Exception failure) { try { use.Dispose(); } catch (Exception cleanup) { Attach(failure, cleanup); } throw; }
        }
        public bool IsDisposed => _use.IsDisposed;
        public ReadOnlyMemory<int> PositionedGlyphIndices { get { lock (_gate) { ObjectDisposedException.ThrowIf(IsDisposed, this); return _indices; } } }
        public IPortableHintedTextGlyphRun Retain() { lock (_gate) return Create(_generation, _use.Retain(), _indices); }
        public IPortableHintedTextParagraph AcquireParagraph() { lock (_gate) return WpfSourceDisplayTextParagraph.Create(_generation, _use.Retain()); }
        public void CopyDisplaySourceMetrics(double sourceEmSize, double sourcePixelsPerDip, Span<double> sourceAdvances, Span<PortablePoint> sourceOffsets)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (sourceAdvances.Length < _indices.Length || sourceOffsets.Length < _indices.Length ||
                    MemoryMarshal.AsBytes(sourceAdvances).Overlaps(MemoryMarshal.AsBytes(sourceOffsets)))
                    throw new ArgumentException("Source metric outputs must cover the selection and have disjoint full capacities.");
                var advances = new double[_indices.Length]; var offsets = new NativeHintedSourceGlyphOffset[_indices.Length];
                using var read = _generation.Resource.AcquireReadLease();
                read.CopySourceMetrics(_nativeIndices, sourceEmSize, sourcePixelsPerDip, advances, offsets);
                advances.AsSpan().CopyTo(sourceAdvances);
                for (int i = 0; i < offsets.Length; ++i) sourceOffsets[i] = new(offsets[i].X, offsets[i].Y);
            }
        }
        public IPortableDisplayGlyphRunBinding BindDisplayGlyphRun(PortableTextFont sourceFont, double sourceEmSize,
            double sourcePixelsPerDip, PortablePoint sourceBaselineOrigin, ReadOnlySpan<double> sourceAdvances, ReadOnlySpan<PortablePoint> sourceOffsets)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this); ArgumentNullException.ThrowIfNull(sourceFont);
                if (_indices.Length == 0 || sourceAdvances.Length != _indices.Length || sourceOffsets.Length != _indices.Length)
                    throw new ArgumentException("Source metrics must cover the complete nonempty selected run.");
                using var read = _generation.Resource.AcquireReadLease();
                WpfHintedGlyphSourceIdentity.ValidateDefaultInstances(read.PositionedOwners, read.Runs, read.DeviceStyles, read.NormalizedCoordinates, _indices);
                WpfHintedGlyphSourceIdentity.ValidateFont(sourceFont, read.FontSources[checked((int)read.Glyphs[_indices[0]].FontIndex)], read.FontBytes);
                var offsets = new NativeHintedSourceGlyphOffset[_indices.Length];
                for (int i = 0; i < offsets.Length; ++i) offsets[i] = new() { X = sourceOffsets[i].X, Y = sourceOffsets[i].Y };
                var frame = read.ValidateSourceRun(_nativeIndices, sourceEmSize, sourcePixelsPerDip,
                    new() { X = sourceBaselineOrigin.X, Y = sourceBaselineOrigin.Y }, sourceAdvances, offsets);
                var owner = Create(_generation, _use.Retain(), _indices);
                HintedGlyphGeometry? geometry = null;
                try
                {
                    geometry = _generation.SelectGeometry(_indices);
                    return WpfHintedGlyphRunBinding.AdoptSource(owner, _generation.Resource, geometry,
                        sourceEmSize, sourcePixelsPerDip, read, _indices, frame);
                }
                catch (Exception failure)
                {
                    try { geometry?.Dispose(); } catch (Exception cleanup) { Attach(failure, cleanup); }
                    try { owner.Dispose(); } catch (Exception cleanup) { Attach(failure, cleanup); }
                    throw;
                }
            }
        }
        public void Dispose() { lock (_gate) _use.Dispose(); }
    }
}
