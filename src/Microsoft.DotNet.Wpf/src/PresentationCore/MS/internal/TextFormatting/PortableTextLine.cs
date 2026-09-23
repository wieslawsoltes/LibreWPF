// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.IO;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Shaping;
using ProGPU.Wpf.Interop;
using Vector2 = System.Numerics.Vector2;

namespace MS.Internal.TextFormatting;

// Source-owned immutable request for one hard paragraph segment. Continuations
// retain the provider paragraph and do not query the source again.
internal sealed class PortableTextExclusionRequest
{
    internal PortableTextExclusionOptions Options { get; }
    internal ReadOnlyMemory<PortableTextExclusion> Exclusions { get; }
    internal double OriginY { get; private init; }
    internal PortableTextExclusionRequest At(double originY)
    {
        if (!double.IsFinite(originY) || originY < 0) throw new ArgumentOutOfRangeException(nameof(originY));
        return new(Options, Exclusions) { OriginY = originY };
    }
    internal PortableTextExclusionRequest(PortableTextExclusionOptions options, ReadOnlyMemory<PortableTextExclusion> exclusions)
    { Options = options; Exclusions = exclusions.ToArray(); }
}

internal interface IPortableExcludedTextSource
{
    PortableTextExclusionRequest GetExclusions(int firstSourceIndex);
}

// Original child ranges are relative to the requested hard segment, never glyph indices.
internal sealed class PortableTextFloatingRequest
{
    internal uint MaximumAttempts { get; }
    internal double OriginY { get; }
    internal ReadOnlyMemory<PortableTextSourceFloat> Children { get; }
    internal ReadOnlyMemory<PortableTextExclusion> Exclusions { get; }
    internal PortableTextFloatingRequest(uint maximumAttempts, double originY,
        ReadOnlySpan<PortableTextSourceFloat> children, ReadOnlySpan<PortableTextExclusion> exclusions)
    {
        MaximumAttempts = maximumAttempts; OriginY = originY;
        Children = children.ToArray(); Exclusions = exclusions.ToArray();
    }
}

internal interface IPortableFloatingTextSource
{
    PortableTextFloatingRequest GetFloats(int firstSourceIndex, int sourceLength);
}

/// <summary>Source-owned WPF line semantics over the typed ProGPU paragraph service.</summary>
internal sealed class PortableTextLine : TextLine
{
    private static readonly ConditionalWeakTable<GlyphTypeface, PortableTextFont> Fonts = new();
    internal sealed record Continuation(PortableTextLine Owner, int LineIndex, int NextSourceIndex);
    private readonly IPortableTextParagraph _paragraph;
    private readonly string _text;
    private readonly PortableTextSourceMap _sourceMap;
    private readonly TextModifierScope _endScope;
    private readonly TextRunProperties _properties;
    private readonly GlyphTypeface _face;
    private sealed record SourceStyle(int Start, int End, TextRunProperties Properties, TextRun Run, GlyphTypeface Face,
        PortableTextFont Font, double EmSize, double Baseline, double Height, uint Language,
        uint DigitZero, bool ContextualDigits);
    private readonly record struct SourceStyleRequest(int Start, int End, TextRunProperties Properties, TextRun Run,
        uint Language, CultureInfo DigitCulture, uint DigitZero, bool ContextualDigits);
    private readonly SourceStyle[] _styles;
    private sealed record SourceObject(int Position, TextEmbeddedObject Run, PortableTextInlineObject Metrics);
    private readonly SourceObject[] _objects;
    private readonly List<(int ObjectIndex, Point Origin)> _lineObjects;
    private readonly Dictionary<int, TextRunBounds> _objectBounds;
    private readonly List<(bool IsObject, int Index)> _drawingOrder;
    private readonly bool _fixedHeight;
    private readonly int _paragraphStart, _lineIndex, _newlines;
    private readonly bool _endsParagraph;
    private readonly double _paragraphWidth, _indent, _baseline, _height;
    private readonly bool _rightToLeft;
    private readonly List<IndexedGlyphRun> _glyphRuns = new();
    private readonly List<TextRunProperties> _glyphProperties = new();
    private readonly List<(int Start, int End, sbyte Level)> _selectionRuns = new();
    private readonly List<(Rect Bounds, Brush Brush)> _backgrounds = new();
    private List<(Rect Bounds, Brush Brush)> _underlines;
    private readonly List<TextSpan<TextRun>> _runs;
    private readonly Rect _ink;
    private readonly int _trailing;
    private readonly double _width;
    private bool _disposed;
    private readonly TextFormatterImp _formatter;
    private readonly IPortableTextFormatting _service;
    private readonly PortableTextLine _symbol;
    private readonly PortableTextLine _uncollapsed;
    private readonly List<IndexedGlyphRun> _collapsedGlyphRuns;
    private PortableTextLineInfo Info => _paragraph.Lines.Span[_lineIndex];
    internal PortableTextFragment? Fragment => (_paragraph as IPortableExcludedTextParagraph)?.Fragments.Span[_lineIndex];
    internal double FragmentContentHeight => (_paragraph as IPortableExcludedTextParagraph)?.ContentHeight ?? Height;
    internal bool IsLastFragment => _lineIndex + 1 == _paragraph.Lines.Length;
    internal double FragmentContentWidth => (_paragraph as IPortableExcludedTextParagraph)?.ContentWidth ?? Width;
    internal double FloatingContentHeight => (_paragraph as IPortableFloatingTextParagraph)?.OccupiedHeight ?? FragmentContentHeight;
    internal double FloatingContentWidth => (_paragraph as IPortableFloatingTextParagraph)?.OccupiedWidth ?? FragmentContentWidth;
    internal ReadOnlyMemory<PortableTextFloatPlacement> SourceFloats { get; private init; }
    private double NativeOrigin => Start - (Fragment?.Left ?? 0);
    // ProGPU interaction geometry is expressed in the paragraph's physical
    // left-to-right coordinate space. TextLine exposes the logical coordinate
    // space consumed by WPF's FlowDirection inversion, so an RTL paragraph
    // mirrors points and rectangles inside the retained native line width.
    // Fragment coordinates include their native left placement; normalize that
    // placement exactly once before applying the WPF line origin.
    private double ToLogicalPointX(double nativeX)
    {
        double fragmentLeft = Fragment?.Left ?? 0;
        double localX = nativeX - fragmentLeft;
        return Start + (_rightToLeft ? Info.Width - localX : localX);
    }

    private double ToLogicalRectangleX(double nativeX, double width)
    {
        double fragmentLeft = Fragment?.Left ?? 0;
        double localX = nativeX - fragmentLeft;
        return Start + (_rightToLeft ? Info.Width - localX - width : localX);
    }

    private float ToNativePointX(double logicalX)
    {
        double localX = logicalX - Start;
        double nativeLocalX = _rightToLeft ? Info.Width - localX : localX;
        return (float)(nativeLocalX + (Fragment?.Left ?? 0));
    }
    private int First => _paragraphStart + (_lineIndex == 0 && Info.InputStart == 0 ? 0 : _sourceMap.ToSource(Info.InputStart, true));
    private int End => _paragraphStart + _sourceMap.ToSource(Info.InputEnd, true);

    internal static TextLine Create(FormatSettings settings, int first, int idealWidth, double pixelsPerDip)
    {
        TextLine continuation = CreateContinuation(settings, first, idealWidth, pixelsPerDip);
        if (continuation != null) return continuation;
        if (!PortableWpfServiceRegistry.TryGetTextFormatting(out var service)) return null;
        return Create(settings, first, idealWidth, pixelsPerDip, service);
    }

    internal static TextLine CreateContinuation(FormatSettings settings, int first, int idealWidth, double pixelsPerDip)
    {
        if (settings.PreviousLineBreak?.PortableContinuation is not Continuation next) return null;
        double width = settings.Formatter.IdealToReal(idealWidth, pixelsPerDip);
        if (next.NextSourceIndex != first)
            throw Unsupported($"changed continuation width or source index " +
                $"(source={first}, expectedSource={next.NextSourceIndex}, " +
                $"width={width:R}, retainedWidth={next.Owner._paragraphWidth:R}, line={next.LineIndex})");
        if (next.Owner._paragraphWidth != width)
        {
            if (next.Owner._paragraph is not IPortableReflowTextParagraph reflow)
                throw Unsupported("the captured text paragraph does not expose native continuation reflow");
            int inputStart = next.Owner._paragraph.Lines.Span[next.LineIndex].InputStart;
            float maximumWidth = settings.Pap.Wrap && width > 0
                ? (float)Math.Max(float.Epsilon, width - next.Owner._indent) : 0;
            var paragraph = reflow.Reflow(inputStart, maximumWidth);
            if (paragraph == null || paragraph.Lines.IsEmpty || paragraph.Lines.Span[0].InputStart != inputStart)
                throw new InvalidOperationException("The native continuation lost its original input boundary.");
            return new PortableTextLine(next.Owner, 0, paragraph, width);
        }
        return new PortableTextLine(next.Owner, next.LineIndex);
    }

    // The formatter captures one provider for the request. Concurrent override
    // disposal cannot turn an admitted request into the legacy missing-provider path.
    internal static TextLine Create(FormatSettings settings, int first, int idealWidth, double pixelsPerDip,
        IPortableTextFormatting service) => CreateCore(settings, first, idealWidth, pixelsPerDip, service, false, out _);

    private readonly record struct Measurement(int SourceLength, int Newlines, bool EndsParagraph,
        TextModifierScope EndScope, PortableTextIntrinsicWidths Widths, double Indent);

    private static TextLine CreateCore(FormatSettings settings, int first, int idealWidth, double pixelsPerDip,
        IPortableTextFormatting service, bool measureIntrinsicWidths, out Measurement measurement)
    {
        measurement = default;
        ArgumentNullException.ThrowIfNull(service);
        double width = settings.Formatter.IdealToReal(idealWidth, pixelsPerDip);
        TextLine continuation = CreateContinuation(settings, first, idealWidth, pixelsPerDip);
        if (continuation != null) return continuation;
        var pap = settings.Pap;
        PortableTextExclusionRequest exclusions = (settings.TextSource as IPortableExcludedTextSource)?.GetExclusions(first);
        if (exclusions != null && (measureIntrinsicWidths || service is not IPortableExcludedTextFormatting || width <= 0))
            throw Unsupported("excluded source formatting requires a bounded width and explicit native provider; intrinsic formatting remains separate");
        if (settings.IsSideways || settings.TextFormattingMode != TextFormattingMode.Ideal || pap.TextMarkerProperties != null ||
            (pap.TextDecorations?.Count ?? 0) != 0 ||
            pap.Tabs?.Count > 0)
            throw Unsupported($"display hinting, sideways text, markers, paragraph decorations or custom tabs " +
                $"(mode={settings.TextFormattingMode}, sideways={settings.IsSideways}, marker={pap.TextMarkerProperties != null}, " +
                $"decorations={pap.TextDecorations?.Count ?? 0}, justify={pap.Justify}, tabs={pap.Tabs?.Count ?? 0})");

        var builder = new StringBuilder();
        var runs = new List<TextSpan<TextRun>>();
        var styles = new List<SourceStyle>();
        var styleRequests = new List<SourceStyleRequest>();
        List<SourceObject> objects = null;
        double indent = settings.Formatter.IdealToReal(settings.TextIndent + pap.ParagraphIndent, pixelsPerDip);
        var mappedFonts = new List<TextSpan<ScaledShapeTypeface>>();
        var sourceRanges = new List<PortableTextSourceRange>();
        var scope = settings.PreviousLineBreak?.TextModifierScope;
        int scopeDepth = 0;
        for (var current = scope; current != null; current = current.ParentScope)
        {
            if (current.TextModifier.HasDirectionalEmbedding) throw Unsupported("directional modifier embedding");
            if (++scopeDepth > 128) throw Unsupported("modifier nesting budget");
        }
        TextRunProperties properties = null;
        bool hasTabs = false;
        bool endsParagraph = false;
        int cp = first, newlines = 0, sourceLength = 0;
        while (true)
        {
            var range = settings.FetchTextRun(cp, first, out TextRun run, out int length);
            if (length <= 0 || cp - first > (1 << 20) - length)
                throw Unsupported("invalid run length or paragraph input budget");
            if (run is TextEndOfLine)
            {
                if (run is TextEndOfParagraph) { scope = null; endsParagraph = true; }
                newlines = length; runs.Add(new(length, run)); break;
            }
            if (run is TextHidden or TextModifier or TextEndOfSegment)
            {
                if (run is TextModifier modifier)
                {
                    if (modifier.HasDirectionalEmbedding) throw Unsupported("directional modifier embedding");
                    if (++scopeDepth > 128) throw Unsupported("modifier nesting budget");
                    scope = new TextModifierScope(scope, modifier, cp);
                }
                else if (run is TextEndOfSegment)
                {
                    if (scope == null) throw Unsupported("unmatched modifier scope end");
                    scope = scope.ParentScope; scopeDepth--;
                }
                runs.Add(new(length, run)); cp = checked(cp + length); sourceLength = cp - first;
                continue;
            }
            if (run is not TextCharacters && run is not TextEmbeddedObject) throw Unsupported("embedded document objects");
            var p = scope == null ? run.Properties : scope.ModifyProperties(run.Properties);
            if (p == null || (p.TextEffects?.Count ?? 0) != 0 ||
                p.BaselineAlignment != BaselineAlignment.Baseline)
                throw Unsupported("run effects, baseline changes or custom typography");
            ValidateUnderlines(p.TextDecorations);
            var digits = settings.DigitState;
            digits.SetTextRunProperties(p);
            uint digitZero = digits.DigitCulture == null ? 0U : GetDigitZero(digits.DigitCulture.NumberFormat.NativeDigits);
            uint language = service.ResolveLanguage(
                CultureMapper.GetSpecificCulture(p.CultureInfo).IetfLanguageTag);
            properties ??= p;
            int start = builder.Length;
            if (run is TextEmbeddedObject embedded)
            {
                if (service is not IPortableInlineTextFormatting || length != 1 || !embedded.HasFixedSize ||
                    embedded.BreakBefore != LineBreakCondition.BreakDesired || embedded.BreakAfter != LineBreakCondition.BreakDesired ||
                    pap.LineHeight > 0 || (p.TextDecorations?.Count ?? 0) != 0)
                    throw Unsupported("variable-size, multi-symbol, decorated or fixed-line-height objects, custom object breaks or missing inline provider");
                var metrics = embedded.Format(pap.Wrap && width > 0 ? Math.Max(0, width - indent) : double.PositiveInfinity);
                if (metrics == null || !double.IsFinite(metrics.Width) || !double.IsFinite(metrics.Height) ||
                    !double.IsFinite(metrics.Baseline) || metrics.Width < 0 || metrics.Height < 0 ||
                    metrics.Baseline < 0 || metrics.Baseline > metrics.Height ||
                    metrics.Width > float.MaxValue || metrics.Height > float.MaxValue)
                    throw new InvalidOperationException("The source embedded object returned invalid native metrics.");
                styleRequests.Add(new(start, start + 1, p, run, language, null, 0, false));
                builder.Append('\uFFFC');
                sourceRanges.Add(new(cp - first, start, 1));
                (objects ??= new()).Add(new(start, embedded,
                    new(start, (float)metrics.Width, (float)metrics.Baseline, (float)(metrics.Height - metrics.Baseline))));
                runs.Add(new(1, run)); cp = checked(cp + 1); sourceLength = cp - first;
                continue;
            }
            range.CharacterBuffer.AppendToStringBuilder(builder, range.OffsetToFirstChar, length);
            int used = length;
            for (int i = start; i < builder.Length; i++)
            {
                char c = builder[i];
                if (c == '\t')
                {
                    if (pap.DefaultIncrementalTab <= 0) throw Unsupported("tabs with a disabled incremental grid");
                    hasTabs = true;
                }
                if (c is '\r' or '\n' or '\u2028' or '\u2029')
                {
                    if (c == '\u2029') { scope = null; endsParagraph = true; }
                    newlines = c == '\r' && i + 1 < builder.Length && builder[i + 1] == '\n' ? 2 : 1;
                    used = i - start + newlines;
                    builder.Length = i;
                    break;
                }
            }
            if (builder.Length > start)
            {
                sourceRanges.Add(new(cp - first, start, builder.Length - start));
                styleRequests.Add(new(start, builder.Length, p, run, language, digits.DigitCulture,
                    digitZero, digits.Contextual));
            }
            runs.Add(new(used, run)); cp = checked(cp + used); sourceLength = cp - first - newlines;
            if (newlines != 0) break;
        }
        string text = builder.ToString();
        ResolveSourceStyles(settings, first, pixelsPerDip, service, text, styleRequests, styles, mappedFonts);
        properties ??= pap.DefaultTextRunProperties;
        GlyphTypeface face;
        double primaryEmSize;
        if (styles.Count > 0) { face = styles[0].Face; primaryEmSize = styles[0].EmSize; }
        else
        {
            // An empty line still needs the source family's real font/metrics,
            // but this probe is not inserted into its text or rendered as a glyph.
            mappedFonts.Clear();
            settings.Formatter.GlyphingCache.GetPortableFontRuns(properties.Typeface,
                new CharacterBufferRange(" ", 0, 1), properties.CultureInfo, mappedFonts);
            if (mappedFonts.Count != 1) throw new InvalidOperationException("No default physical face for an empty line.");
            ValidateMappedFont(mappedFonts[0].Value, properties.Typeface);
            face = mappedFonts[0].Value.ShapeTypeface.GlyphTypeface;
            primaryEmSize = properties.FontRenderingEmSize * mappedFonts[0].Value.ScaleInEm;
        }
        var sourceMap = new PortableTextSourceMap(sourceLength, text.Length, CollectionsMarshal.AsSpan(sourceRanges));
        var floating = (settings.TextSource as IPortableFloatingTextSource)?.GetFloats(first, sourceLength);
        if (floating != null && (exclusions != null || measureIntrinsicWidths || width <= 0 ||
            service is not IPortableFloatingTextFormatting))
            throw Unsupported("floating source formatting requires one bounded native floating provider request");
        var floatEvents = floating == null ? null : sourceMap.MapFloatingRanges(floating.Children.Span);
        var font = styles.Count > 0 ? styles[0].Font : GetFont(face);
        double defaultHeight = properties.Typeface.LineSpacing(
            properties.FontRenderingEmSize, 1, pixelsPerDip, settings.TextFormattingMode);
        double height = pap.LineHeight > 0 ? settings.Formatter.IdealToReal(pap.LineHeight, pixelsPerDip) : defaultHeight;
        double baseline = properties.Typeface.Baseline(properties.FontRenderingEmSize, 1, pixelsPerDip, settings.TextFormattingMode);
        // Line Services preserves the default face's baseline ratio when a
        // client supplies an explicit line height. The run itself keeps its
        // source metrics inside that line box; scaling the run metrics would
        // make caret and selection rectangles as tall as the block line.
        if (pap.LineHeight > 0 && defaultHeight > 0)
            baseline = height * baseline / defaultHeight;
        var portableStyles = new PortableTextStyle[styles.Count];
        double layoutHeight = height;
        for (int i = 0; i < styles.Count; i++)
        {
            var style = styles[i];
            portableStyles[i] = new(style.Start, style.End - style.Start, style.Font,
                (float)style.EmSize, Features(style.Properties.TypographyProperties), style.Language,
                style.DigitZero, style.ContextualDigits, style.DigitZero != 0);
            layoutHeight = Math.Max(layoutHeight, style.Height);
        }
        var request = new PortableTextParagraphRequest(text.AsMemory(), font, (float)primaryEmSize,
            (float)(objects == null ? layoutHeight : height), pap.Wrap && width > 0 ? (float)Math.Max(float.Epsilon, width - indent) : 0,
            pap.RightToLeft, pap.Justify ? PortableTextAlignment.Justify : PortableTextAlignment.Left,
            Features(properties.TypographyProperties), portableStyles,
            hasTabs ? (float)pap.DefaultIncrementalTab : 0, (float)indent, measureIntrinsicWidths,
            pap.EmergencyWrap ? PortableTextWrapping.Emergency : PortableTextWrapping.WholeWord);
        IPortableTextParagraph paragraph;
        if (objects != null || exclusions != null || floating != null)
        {
            var metrics = new PortableTextStyleMetrics[styles.Count];
            for (int i = 0; i < metrics.Length; i++)
                metrics[i] = new((float)styles[i].Baseline, (float)(styles[i].Height - styles[i].Baseline));
            var items = new PortableTextInlineObject[objects?.Count ?? 0];
            for (int i = 0; i < items.Length; i++) items[i] = objects[i].Metrics;
            if (floating != null)
            {
                var options = new PortableTextFloatingOptions(floating.MaximumAttempts, floating.OriginY,
                    (float)baseline, (float)Math.Max(0, height - baseline));
                paragraph = ((IPortableFloatingTextFormatting)service).FormatFloating(in request, metrics, items,
                    in options, floatEvents, floating.Exclusions.Span);
            }
            else if (exclusions != null)
            {
                var options = exclusions.Options;
                if (exclusions.OriginY != 0)
                {
                    if (service is not IPortableSegmentedTextFormatting segmented)
                        throw Unsupported("the text provider does not support excluded hard-segment origins");
                    paragraph = segmented.FormatExcludedAt(in request, metrics, items, in options,
                        exclusions.Exclusions.Span, exclusions.OriginY);
                }
                else paragraph = ((IPortableExcludedTextFormatting)service).FormatExcluded(in request, metrics, items,
                    in options, exclusions.Exclusions.Span);
            }
            else paragraph = ((IPortableInlineTextFormatting)service).FormatInline(in request, metrics, items);
        }
        else paragraph = service.Format(in request);
        if (paragraph == null) throw new InvalidOperationException("The text provider returned no paragraph.");
        if (paragraph.Lines.Length == 0) throw new InvalidOperationException("The text provider returned no line.");
        if ((exclusions != null || floating != null) && (paragraph is not IPortableExcludedTextParagraph excluded ||
            excluded.Fragments.Length != paragraph.Lines.Length))
            throw new InvalidOperationException("The excluded text provider did not retain one frame per source fragment.");
        if (measureIntrinsicWidths)
        {
            var widths = paragraph.IntrinsicWidths ?? throw Unsupported("the text provider does not publish intrinsic paragraph widths");
            if (!float.IsFinite(widths.Minimum) || !float.IsFinite(widths.Maximum) ||
                widths.Minimum < 0 || widths.Maximum < widths.Minimum)
                throw new InvalidOperationException("The text provider returned invalid intrinsic paragraph widths.");
            measurement = new(sourceLength, newlines, endsParagraph, scope, widths, indent);
            return null; // Measurement never constructs source GlyphRuns or a drawing TextLine.
        }
        PortableTextFloatPlacement[] sourceFloats = [];
        if (floating != null)
        {
            if (paragraph is not IPortableFloatingTextParagraph floated || floated.Floats.Length != floatEvents.Length)
                throw new InvalidOperationException("The floating text provider lost source children.");
            sourceFloats = new PortableTextFloatPlacement[floatEvents.Length];
            for (int i = 0; i < sourceFloats.Length; i++)
            {
                var placement = floated.Floats.Span[i];
                if (placement.Position != floatEvents[i].Position)
                    throw new InvalidOperationException("The floating text provider changed source event order.");
                // Equal shaping boundaries may name distinct hidden children. Never reverse-map that ambiguity.
                sourceFloats[i] = placement with { Position = checked(first + floating.Children.Span[i].SourceStart) };
            }
        }
        return new PortableTextLine(paragraph, text, properties, face, first, 0, newlines, endsParagraph,
            width, indent, baseline, height, pap.RightToLeft, runs, pixelsPerDip, pap.Align, styles.ToArray(), pap.LineHeight > 0,
            sourceMap, scope,
            settings.Formatter, service, objects: objects?.ToArray()) { SourceFloats = sourceFloats };
    }

    internal static MinMaxParagraphWidth MeasureIntrinsicWidths(FormatSettings settings, int first,
        double pixelsPerDip, IPortableTextFormatting service)
    {
        double minimum = 0, maximum = 0;
        int start = first;
        TextLineBreak previous = null;
        try
        {
            while (true)
            {
                _ = CreateCore(settings, first, 0, pixelsPerDip, service, true, out var measured);
                minimum = Math.Max(minimum, measured.Indent + measured.Widths.Minimum);
                maximum = Math.Max(maximum, measured.Indent + measured.Widths.Maximum);
                int consumed = checked(measured.SourceLength + measured.Newlines);
                if (consumed <= 0 || first - start > (1 << 20) - consumed)
                    throw Unsupported("intrinsic paragraph input budget or nonprogressing source");
                if (measured.EndsParagraph) return new MinMaxParagraphWidth(minimum, maximum);
                first = checked(first + consumed);
                previous?.Dispose();
                previous = measured.EndScope == null ? null : new TextLineBreak(measured.EndScope, IntPtr.Zero);
                settings.UpdateSettingsForCurrentLine(0, previous, false);
            }
        }
        finally { previous?.Dispose(); }
    }

    private static void ValidateMappedFont(ScaledShapeTypeface selected, Typeface requested)
    {
        if (selected == null || selected.NullShape || selected.ShapeTypeface?.GlyphTypeface == null ||
            selected.ShapeTypeface.DeviceFont != null)
            throw Unsupported($"unresolved null-shape or device-font mapping for '{requested.FontFamily?.Source}' " +
                $"(null shape: {selected?.NullShape}, physical face: {selected?.ShapeTypeface?.GlyphTypeface != null}, " +
                $"device font: {selected?.ShapeTypeface?.DeviceFont != null})");
        var simulations = selected.ShapeTypeface.GlyphTypeface.StyleSimulations;
        if (((int)simulations & ~(int)StyleSimulations.BoldItalicSimulation) != 0)
            throw Unsupported($"unknown synthetic font simulation for '{requested.FontFamily?.Source}': {simulations}");
        // Keep the source physical face, including its simulation flags. GlyphRun
        // exports those flags to both ProGPU renderers; its portable ink bounds
        // include the simulated stroke/shear without changing native advances.
    }

    private static PortableTextFont GetFont(GlyphTypeface face) => Fonts.GetValue(face, static source =>
    {
        using Stream stream = source.GetFontStream();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return new PortableTextFont(bytes.ToArray(), checked((uint)source.FaceIndex), source.DesignEmHeight);
    });

    private PortableTextLine(PortableTextLine owner, int index, IPortableTextParagraph paragraph = null, double? width = null) : this(paragraph ?? owner._paragraph, owner._text,
        owner._properties, owner._face, owner._paragraphStart, index, owner._newlines, owner._endsParagraph,
        width ?? owner._paragraphWidth, owner._indent, owner._baseline, owner._height, owner._rightToLeft,
        owner._runs, owner.PixelsPerDip, owner._alignment, owner._styles, owner._fixedHeight, owner._sourceMap, owner._endScope,
        owner._formatter, owner._service, objects: owner._objects) { SourceFloats = owner.SourceFloats; }

    private static void ResolveSourceStyles(FormatSettings settings, int first, double pixelsPerDip,
        IPortableTextFormatting service, string text, List<SourceStyleRequest> requests,
        List<SourceStyle> styles, List<TextSpan<ScaledShapeTypeface>> mappedFonts)
    {
        bool contextual = false;
        for (int i = 0; i < requests.Count; i++)
            contextual |= requests[i].DigitZero != 0 && requests[i].ContextualDigits;
        byte[] contexts = null;
        try
        {
            if (contextual)
            {
                if (service is not IPortableTextDigitContext digitContext)
                    throw Unsupported("native digit-context resolution before source font selection");
                bool initial = GetInitialDigitContext(settings, first, digitContext);
                contexts = ArrayPool<byte>.Shared.Rent(Math.Max(1, checked(text.Length * 2)));
                var starts = contexts.AsSpan(text.Length, text.Length);
                digitContext.ResolveDigitContext(text.AsSpan(), initial, contexts.AsSpan(0, text.Length), starts);
                for (int i = 0; i < requests.Count; i++)
                    ValidateNumberSymbols(requests[i], text, contexts);
                PreserveDigitClusters(text, contexts.AsSpan(0, text.Length), starts);
            }
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (!contextual) ValidateNumberSymbols(request, text, null);
                int position = request.Start;
                while (position < request.End)
                {
                    bool substitute = UsesDigitCulture(request, contexts, position);
                    int end = request.DigitZero == 0 ? request.End : position + 1;
                    while (end < request.End && UsesDigitCulture(request, contexts, end) == substitute)
                        end++;
                    var p = request.Properties;
                    // Embedded objects retain source metrics, but never enter font linking.
                    var range = request.Run is TextEmbeddedObject
                        ? new CharacterBufferRange(" ", 0, 1)
                        : new CharacterBufferRange(text, position, end - position);
                    mappedFonts.Clear();
                    settings.Formatter.GlyphingCache.GetPortableFontRuns(p.Typeface, range,
                        p.CultureInfo, mappedFonts, substitute ? request.DigitCulture : null);
                    int mappedStart = position;
                    for (int j = 0; j < mappedFonts.Count; j++)
                    {
                        var mapped = mappedFonts[j];
                        var selected = mapped.Value;
                        ValidateMappedFont(selected, p.Typeface);
                        var face = selected.ShapeTypeface.GlyphTypeface;
                        if (substitute)
                            ValidateDigitGlyphs(face, text.AsSpan(mappedStart, mapped.Length), request.DigitZero);
                        double emSize = p.FontRenderingEmSize * selected.ScaleInEm;
                        if (!double.IsFinite(emSize) || emSize <= 0) throw Unsupported("invalid composite-font scale");
                        styles.Add(new(mappedStart, checked(mappedStart + mapped.Length), p, request.Run,
                            face, GetFont(face), emSize,
                            p.Typeface.Baseline(p.FontRenderingEmSize, 1, pixelsPerDip, settings.TextFormattingMode),
                            p.Typeface.LineSpacing(p.FontRenderingEmSize, 1, pixelsPerDip, settings.TextFormattingMode),
                            request.Language, substitute ? request.DigitZero : 0, false));
                        mappedStart += mapped.Length;
                    }
                    if (mappedStart != end) throw new InvalidOperationException("Source font ranges do not cover the styled run.");
                    position = end;
                }
            }
        }
        finally
        {
            if (contexts != null) ArrayPool<byte>.Shared.Return(contexts);
        }
    }

    private static bool UsesDigitCulture(SourceStyleRequest request, byte[] contexts, int position)
        => request.DigitZero != 0 && (!request.ContextualDigits || contexts[position] != 0);

    private static void ValidateNumberSymbols(SourceStyleRequest request, string text, byte[] contexts)
    {
        if (request.DigitZero == 0) return;
        var map = new DigitMap(request.DigitCulture);
        int position = request.Start;
        while (position < request.End)
        {
            int index = text.AsSpan(position, request.End - position).IndexOfAny('%', ',', '.');
            if (index < 0) break;
            position += index;
            char character = text[position];
            if (UsesDigitCulture(request, contexts, position) && map[character] != character)
                throw Unsupported("culture-specific number symbols require a native symbol-substitution contract");
            position++;
        }
    }

    private static void PreserveDigitClusters(string text, Span<byte> contexts, ReadOnlySpan<byte> starts)
    {
        int start = 0;
        while (start < text.Length)
        {
            int next = starts[(start + 1)..].IndexOf((byte)1);
            int end = next < 0 ? text.Length : start + 1 + next;
            int digit = text.AsSpan(start, end - start).IndexOfAnyInRange('0', '9');
            // Only digit decisions affect shaping. Keep the entire native grapheme
            // with its digit so font linking never separates marks/joiners from it.
            byte context = contexts[digit < 0 ? start : start + digit];
            contexts[start..end].Fill(context);
            start = end;
        }
    }

    private static void ValidateDigitGlyphs(GlyphTypeface face, ReadOnlySpan<char> text, uint digitZero)
    {
        uint seen = 0;
        while (!text.IsEmpty)
        {
            int index = text.IndexOfAnyInRange('0', '9');
            if (index < 0) break;
            int digit = text[index] - '0';
            uint bit = 1U << digit;
            if ((seen & bit) == 0)
            {
                int scalar = checked((int)digitZero + digit);
                if (!face.CharacterToGlyphMap.TryGetValue(scalar, out ushort glyph) || glyph == 0)
                    throw Unsupported("a substituted digit is missing from the mapped physical face; alternate-character fallback requires native support");
                seen |= bit;
                if (seen == 0x3ff) break;
            }
            text = text[(index + 1)..];
        }
    }

    private static bool GetInitialDigitContext(FormatSettings settings, int first, IPortableTextDigitContext service)
    {
        bool context = settings.Pap.RightToLeft;
        if (first == 0) return context;
        var chunks = new List<string>();
        int remaining = first, total = 0, sourceLength = 0;
        while (remaining > 0)
        {
            var preceding = settings.GetPrecedingText(remaining, out bool endsAtHardBreak);
            if (endsAtHardBreak) break;
            if (preceding.Length <= 0) break;
            int consumed = Math.Min(remaining, preceding.Length);
            if (consumed > (1 << 20) - sourceLength) throw Unsupported("preceding digit-context source budget");
            sourceLength += consumed;
            var range = preceding.Value.CharacterBufferRange;
            bool hardBreak = false;
            if (!range.IsEmpty)
            {
                int start = Math.Max(0, range.Length - remaining);
                for (int i = range.Length - 1; i >= start; i--)
                {
                    if (range.CharacterBuffer[range.OffsetToFirstChar + i] is '\r' or '\n' or '\u2028' or '\u2029')
                    {
                        start = i + 1; hardBreak = true; break;
                    }
                }
                int length = range.Length - start;
                if (length > (1 << 20) - total) throw Unsupported("preceding digit-context input budget");
                total += length;
                if (length > 0)
                {
                    var chunk = new StringBuilder(length);
                    range.CharacterBuffer.AppendToStringBuilder(chunk, range.OffsetToFirstChar + start, length);
                    chunks.Add(chunk.ToString());
                }
            }
            if (hardBreak) break;
            remaining -= consumed;
        }
        if (total == 0) return context;
        // Source spans are fetched backwards. Join them before native decoding so
        // a source span boundary cannot split a supplementary scalar.
        var precedingText = new StringBuilder(total);
        for (int i = chunks.Count - 1; i >= 0; i--)
            precedingText.Append(chunks[i]);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(total);
        try { return service.ResolveDigitContext(precedingText.ToString().AsSpan(), context, buffer.AsSpan(0, total)); }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static uint GetDigitZero(string[] digits)
    {
        if (digits == null || digits.Length != 10)
            throw Unsupported("number culture without ten native digits");
        uint zero = 0;
        for (int index = 0; index < digits.Length; index++)
        {
            string digit = digits[index];
            int scalar;
            if (digit is { Length: 1 } && !char.IsSurrogate(digit[0]))
                scalar = digit[0];
            else if (digit is { Length: 2 } && char.IsSurrogatePair(digit, 0))
                scalar = char.ConvertToUtf32(digit, 0);
            else
                throw Unsupported("multi-scalar or invalid native digit");
            if (index == 0)
                zero = checked((uint)scalar);
            else if ((uint)scalar != zero + (uint)index)
                throw Unsupported("non-contiguous native digit sequence");
        }
        return zero;
    }

    private static PortableTextFeature[] Features(TextRunTypographyProperties p)
    {
        if (p == null) return [];
        if (p.Variants != FontVariants.Normal || p.Capitals != FontCapitals.Normal || p.Fraction != FontFraction.Normal ||
            p.NumeralStyle != FontNumeralStyle.Normal || p.NumeralAlignment != FontNumeralAlignment.Normal ||
            p.EastAsianWidths != FontEastAsianWidths.Normal || p.EastAsianLanguage != FontEastAsianLanguage.Normal ||
            p.StandardSwashes != 0 || p.ContextualSwashes != 0 || p.StylisticAlternates != 0 || p.AnnotationAlternates != 0)
            throw Unsupported("enumerated typography alternates");
        static PortableTextFeature F(string tag, bool enabled) => new(
            (uint)tag[0] << 24 | (uint)tag[1] << 16 | (uint)tag[2] << 8 | tag[3], enabled ? 1U : 0U);
        return [F("liga", p.StandardLigatures), F("clig", p.ContextualLigatures), F("dlig", p.DiscretionaryLigatures),
            F("hlig", p.HistoricalLigatures), F("calt", p.ContextualAlternates), F("hist", p.HistoricalForms),
            F("kern", p.Kerning), F("cpsp", p.CapitalSpacing), F("case", p.CaseSensitiveForms),
            F("ss01", p.StylisticSet1), F("ss02", p.StylisticSet2), F("ss03", p.StylisticSet3), F("ss04", p.StylisticSet4),
            F("ss05", p.StylisticSet5), F("ss06", p.StylisticSet6), F("ss07", p.StylisticSet7), F("ss08", p.StylisticSet8),
            F("ss09", p.StylisticSet9), F("ss10", p.StylisticSet10), F("ss11", p.StylisticSet11), F("ss12", p.StylisticSet12),
            F("ss13", p.StylisticSet13), F("ss14", p.StylisticSet14), F("ss15", p.StylisticSet15), F("ss16", p.StylisticSet16),
            F("ss17", p.StylisticSet17), F("ss18", p.StylisticSet18), F("ss19", p.StylisticSet19), F("ss20", p.StylisticSet20),
            F("zero", p.SlashedZero), F("mgrk", p.MathematicalGreek), F("expt", p.EastAsianExpertForms)];
    }

    private readonly TextAlignment _alignment;
    private PortableTextLine(IPortableTextParagraph paragraph, string text, TextRunProperties properties,
        GlyphTypeface face, int paragraphStart, int lineIndex, int newlines, bool endsParagraph, double width, double indent,
        double baseline, double height, bool rtl, List<TextSpan<TextRun>> runs, double pixelsPerDip, TextAlignment alignment,
        SourceStyle[] styles, bool fixedHeight, PortableTextSourceMap sourceMap, TextModifierScope endScope,
        TextFormatterImp formatter, IPortableTextFormatting service, PortableTextLine symbol = null,
        PortableTextLine uncollapsed = null, double? baselineOverride = null, SourceObject[] objects = null)
        : base(pixelsPerDip)
    {
        _paragraph = paragraph; _text = text; _properties = properties; _face = face;
        _sourceMap = sourceMap; _endScope = endScope;
        _paragraphStart = paragraphStart; _lineIndex = lineIndex; _newlines = newlines;
        _endsParagraph = endsParagraph;
        _paragraphWidth = width; _indent = indent; _baseline = baseline; _height = height;
        _rightToLeft = rtl; _runs = runs; _alignment = alignment;
        _styles = styles; _fixedHeight = fixedHeight;
        _objects = objects ?? [];
        if (_objects.Length != 0)
        {
            _lineObjects = new();
            _objectBounds = new();
            _drawingOrder = new();
        }
        _formatter = formatter; _service = service; _uncollapsed = uncollapsed;
        double ascent = 0, descent = 0;
        foreach (var style in styles)
        {
            if (style.Start >= Info.InputEnd || style.End <= Info.InputStart) continue;
            ascent = Math.Max(ascent, style.Baseline);
            descent = Math.Max(descent, style.Height - style.Baseline);
        }
        if (!fixedHeight && ascent + descent > 0)
        {
            _baseline = ascent;
            _height = ascent + descent;
        }
        if (paragraph is IPortableInlineTextParagraph measured)
        {
            _baseline = measured.GetBaselineOffset(lineIndex);
            _height = Info.Height;
        }
        // Collapse keeps original line metrics; a taller sign contributes ink
        // overhang, not a new advance that would move following source lines.
        if (baselineOverride is { } forcedBaseline) _baseline = forcedBaseline;
        int visibleEnd = paragraph.CollapsedRange is { } collapsed ? collapsed.Start : Info.InputEnd;
        while (visibleEnd > Info.InputStart && char.IsWhiteSpace(text[visibleEnd - 1])) visibleEnd--;
        _trailing = paragraph.CollapsedRange != null ? 0 :
            End - _paragraphStart - Math.Max(First - _paragraphStart, _sourceMap.ToSource(visibleEnd, false));
        double trailingWidth = 0;
        if (paragraph.CollapsedRange == null)
            foreach (var glyph in paragraph.Glyphs.Span.Slice(Info.GlyphStart, Info.GlyphCount))
                if (glyph.Cluster >= visibleEnd) trailingWidth += glyph.Advance;
        _width = Math.Max(0, Info.Width - trailingWidth);
        Rect ink = CreateGlyphRuns();
        CacheUnderlines(visibleEnd, ref ink);
        if (symbol != null && paragraph.CollapsedRange is { } range)
        {
            var placement = paragraph.Glyphs.Span[range.SymbolGlyphIndex];
            _symbol = new PortableTextLine(symbol._paragraph, symbol._text, symbol._properties, symbol._face,
                0, 0, 0, symbol._endsParagraph, 0, Start + placement.X, symbol._baseline, symbol._height, symbol._rightToLeft,
                symbol._runs, PixelsPerDip, TextAlignment.Left, symbol._styles, symbol._fixedHeight,
                symbol._sourceMap, null, symbol._formatter, symbol._service, baselineOverride: Baseline);
            ink.Union(_symbol._ink);
            int sourceStart = _paragraphStart + _sourceMap.ToSource(range.Start, true);
            int sourceEnd = _paragraphStart + _sourceMap.ToSource(range.End, true);
            _collapsedGlyphRuns = new(_glyphRuns.Count + _symbol._glyphRuns.Count);
            _collapsedGlyphRuns.AddRange(_glyphRuns);
            for (int i = 0; i < _symbol._glyphRuns.Count; ++i)
                _collapsedGlyphRuns.Add(new(sourceStart, sourceEnd - sourceStart, _symbol._glyphRuns[i].GlyphRun));
        }
        _ink = ink;
    }

    private static void ValidateUnderlines(TextDecorationCollection decorations)
    {
        if (decorations == null) return;
        foreach (TextDecoration decoration in decorations)
            if (!decoration.CanFreeze || decoration.Location != TextDecorationLocation.Underline ||
                decoration.Pen != null || decoration.PenOffset != 0 ||
                decoration.PenOffsetUnit != TextDecorationUnit.FontRecommended ||
                decoration.PenThicknessUnit != TextDecorationUnit.FontRecommended)
                throw Unsupported("custom, animated or non-underline run decorations");
    }

    private void CacheUnderlines(int visibleEnd, ref Rect ink)
    {
        PortableRect[] rented = null;
        Span<PortableRect> rectangles = stackalloc PortableRect[64];
        try
        {
            SourceStyle previous = null;
            foreach (SourceStyle style in _styles)
            {
                int start = Math.Max(style.Start, Info.InputStart);
                int end = Math.Min(style.End, visibleEnd);
                if (start >= end || (style.Properties.TextDecorations?.Count ?? 0) == 0)
                { previous = null; continue; }

                // LineServices averages metrics for a continuous mixed-font
                // underline. Do not substitute disconnected per-face heights.
                if (previous != null && previous.End == style.Start &&
                    (previous.Face.UnderlineThickness * previous.EmSize != style.Face.UnderlineThickness * style.EmSize ||
                     previous.Face.UnderlinePosition * previous.EmSize != style.Face.UnderlinePosition * style.EmSize))
                    throw Unsupported("continuous mixed-font underline metric averaging");
                previous = style;

                double thickness = style.Face.UnderlineThickness * style.EmSize;
                double center = Baseline - style.Face.UnderlinePosition * style.EmSize;
                if (!double.IsFinite(thickness) || thickness <= 0 || !double.IsFinite(center))
                    throw Unsupported("invalid font underline metrics");
                if (style.Properties.ForegroundBrush == null) continue;
                if (Info.GlyphCount > rectangles.Length)
                    rectangles = rented = ArrayPool<PortableRect>.Shared.Rent(Info.GlyphCount);
                int count = _paragraph.GetSelection(_lineIndex, start, end, rectangles);
                if ((uint)count > (uint)rectangles.Length)
                    throw new InvalidOperationException("The text provider returned an invalid decoration range count.");
                _underlines ??= new();
                for (int i = 0; i < count; i++)
                {
                    var range = rectangles[i];
                    if (!double.IsFinite(range.X) || !double.IsFinite(range.Width) || range.Width < 0)
                        throw new InvalidOperationException("The text provider returned invalid decoration geometry.");
                    if (range.Width == 0) continue;
                    var bounds = new Rect(NativeOrigin + range.X, center - thickness / 2, range.Width, thickness);
                    foreach (TextDecoration decoration in style.Properties.TextDecorations)
                        _underlines.Add((bounds, style.Properties.ForegroundBrush));
                    ink.Union(bounds);
                }
            }
        }
        finally { if (rented != null) ArrayPool<PortableRect>.Shared.Return(rented); }
    }

    private Rect CreateGlyphRuns()
    {
        Rect ink = Rect.Empty;
        var glyphs = _paragraph.Glyphs.Span;
        double nativeBaseline = Info.Y + (_paragraph is IPortableInlineTextParagraph ? Baseline : 0);
        int end = Info.GlyphStart + Info.GlyphCount;
        for (int first = Info.GlyphStart; first < end;)
        {
            int stop = first + 1;
            sbyte level = glyphs[first].BidiLevel;
            if (glyphs[first].IsCollapseSymbol)
            {
                _selectionRuns.Add((glyphs[first].Cluster, glyphs[first].ClusterEnd, level));
                first = stop;
                continue; // Its independently formatted source symbol owns ink/style.
            }
            int styleIndex = StyleIndex(glyphs[first].Cluster);
            var style = _styles[styleIndex];
            var face = style.Face;
            var properties = style.Properties;
            uint fontIndex = glyphs[first].FontIndex;
            if (glyphs[first].IsInlineObject)
            {
                var item = glyphs[first];
                int lo = 0, hi = _objects.Length;
                while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_objects[mid].Position < item.Cluster) lo = mid + 1; else hi = mid; }
                if (lo == _objects.Length || _objects[lo].Position != item.Cluster || item.ClusterEnd != item.Cluster + 1 ||
                    _paragraph is not IPortableInlineTextParagraph measured || measured.InlineObjects.Length != _objects.Length)
                    throw new InvalidOperationException("Native inline object has no retained source owner.");
                var placement = measured.InlineObjects.Span[lo];
                if (placement.InputPosition != item.Cluster || placement.LineIndex != _lineIndex || placement.GlyphIndex != first)
                    throw new InvalidOperationException("Native inline placement changed source or line ownership.");
                var origin = new Point(NativeOrigin + placement.X, Baseline);
                var objectLayoutBounds = new Rect(NativeOrigin + placement.X, placement.Y - Info.Y, placement.Width, placement.Height);
                _drawingOrder.Add((true, _lineObjects.Count));
                _lineObjects.Add((lo, origin));
                int objectSourceStart = _paragraphStart + _sourceMap.ToSource(item.Cluster, true);
                _objectBounds.Add(item.Cluster, new(objectLayoutBounds, objectSourceStart, objectSourceStart + 1, _objects[lo].Run));
                var objectInk = _objects[lo].Run.ComputeBoundingBox(_rightToLeft, false);
                if (!objectInk.IsEmpty) { objectInk.Offset(origin.X, origin.Y); ink.Union(objectInk); }
                _selectionRuns.Add((item.Cluster, item.ClusterEnd, level));
                CacheBackground(properties.BackgroundBrush, item.Cluster, item.ClusterEnd);
                first = stop;
                continue;
            }
            if (glyphs[first].IsTab)
            {
                var tab = glyphs[first];
                if (tab.ClusterEnd != tab.Cluster + 1 || _text[tab.Cluster] != '\t' || tab.ClusterEnd > style.End)
                    throw new InvalidOperationException("Native tab does not match a source tab cluster.");
                _selectionRuns.Add((tab.Cluster, tab.ClusterEnd, level));
                CacheBackground(properties.BackgroundBrush, tab.Cluster, tab.ClusterEnd);
                first = stop;
                continue;
            }
            while (stop < end && !glyphs[stop].IsTab && !glyphs[stop].IsInlineObject && !glyphs[stop].IsCollapseSymbol && glyphs[stop].BidiLevel == level && glyphs[stop].FontIndex == fontIndex &&
                StyleIndex(glyphs[stop].Cluster) == styleIndex)
            {
                var previous = glyphs[stop - 1]; var next = glyphs[stop];
                if (previous.Cluster != next.Cluster && ((level & 1) == 0 ?
                    previous.ClusterEnd != next.Cluster : next.ClusterEnd != previous.Cluster)) break;
                stop++;
            }
            var indices = new int[stop - first];
            for (int i = 0; i < indices.Length; i++) indices[i] = first + i;
            if ((level & 1) != 0)
                Array.Sort(indices, (a, b) =>
                {
                    int compare = _paragraph.Glyphs.Span[a].Cluster.CompareTo(_paragraph.Glyphs.Span[b].Cluster);
                    return compare == 0 ? a.CompareTo(b) : compare;
                });
            int cpStart = int.MaxValue, cpEnd = 0;
            foreach (int index in indices) { cpStart = Math.Min(cpStart, glyphs[index].Cluster); cpEnd = Math.Max(cpEnd, glyphs[index].ClusterEnd); }
            var ids = new ushort[indices.Length]; var advances = new double[indices.Length];
            var offsets = new Point[indices.Length]; var positions = new Vector2[indices.Length];
            var clusters = new ushort[cpEnd - cpStart]; var carets = new bool[clusters.Length + 1];
            double advance = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                var g = glyphs[indices[i]];
                if (g.Cluster < style.Start || g.ClusterEnd > style.End)
                    throw new InvalidOperationException("A native cluster crosses its source style domain.");
                ids[i] = checked((ushort)g.GlyphId); advances[i] = g.Advance;
                positions[i] = new(g.X, (float)(g.Y - nativeBaseline));
                double offset = (level & 1) == 0 ? g.X - advance :
                    -advance - face.AdvanceWidths[ids[i]] * style.EmSize - g.X;
                offsets[i] = new(offset, -(g.Y - nativeBaseline));
                advance += g.Advance;
                if (i == 0 || g.Cluster != glyphs[indices[i - 1]].Cluster)
                {
                    clusters.AsSpan(g.Cluster - cpStart, g.ClusterEnd - g.Cluster).Fill(checked((ushort)i));
                    carets[g.Cluster - cpStart] = true; carets[g.ClusterEnd - cpStart] = true;
                }
            }
            var run = new GlyphRun(face, level, false, style.EmSize, (float)PixelsPerDip,
                ids, new Point(NativeOrigin, Baseline), advances, offsets, _text.AsSpan(cpStart, cpEnd - cpStart).ToArray(),
                null, clusters, carets, XmlLanguage.GetLanguage(properties.CultureInfo.IetfLanguageTag));
            run.InitializePortableGlyphPositions(positions, _paragraph.GetNativeFont(fontIndex));
            int sourceStart = _sourceMap.ToSource(cpStart, true), sourceEnd = _sourceMap.ToSource(cpEnd, false);
            if (sourceEnd - sourceStart != cpEnd - cpStart)
                throw new InvalidOperationException("A glyph run crosses hidden source content.");
            _drawingOrder?.Add((false, _glyphRuns.Count));
            _glyphRuns.Add(new(_paragraphStart + sourceStart, sourceEnd - sourceStart, run));
            _glyphProperties.Add(properties);
            _selectionRuns.Add((cpStart, cpEnd, level));
            CacheBackground(properties.BackgroundBrush, cpStart, cpEnd);
            Rect bounds = run.ComputePortableInkBoundingBox();
            if (!bounds.IsEmpty) { bounds.Offset(run.BaselineOrigin.X, run.BaselineOrigin.Y); ink.Union(bounds); }
            first = stop;
        }
        return ink;
    }

    private void CacheBackground(Brush brush, int start, int end)
    {
        if (brush == null) return;
        var rectangles = new PortableRect[Math.Max(1, Info.GlyphCount)];
        int count = _paragraph.GetSelection(_lineIndex, start, end, rectangles);
        for (int i = 0; i < count; i++)
            _backgrounds.Add((new Rect(NativeOrigin + rectangles[i].X, 0, rectangles[i].Width, Height), brush));
    }

    private int StyleIndex(int position)
    {
        int lo = 0, hi = _styles.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_styles[mid].End <= position) lo = mid + 1; else hi = mid; }
        if (lo == _styles.Length || position < _styles[lo].Start)
            throw new InvalidOperationException("A positioned glyph has no source style.");
        return lo;
    }

    public override void Dispose() => _disposed = true;
    private void CheckAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static PlatformNotSupportedException Unsupported(string detail) => new("Native WPF text integration does not yet preserve " + detail + ".");
    public override void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion)
    {
        CheckAlive();
        ArgumentNullException.ThrowIfNull(drawingContext);
        var antiInversion = TextFormatterImp.CreateAntiInversionTransform(inversion, _paragraphWidth, _height);
        if (antiInversion != null) drawingContext.PushTransform(antiInversion);
        drawingContext.PushTransform(new TranslateTransform(origin.X, origin.Y));
        try
        {
            foreach (var background in _backgrounds) drawingContext.DrawRectangle(background.Brush, null, background.Bounds);
            if (_drawingOrder == null)
                for (int i = 0; i < _glyphRuns.Count; i++) drawingContext.DrawGlyphRun(_glyphProperties[i].ForegroundBrush, _glyphRuns[i].GlyphRun);
            else foreach (var draw in _drawingOrder)
            {
                if (!draw.IsObject) drawingContext.DrawGlyphRun(_glyphProperties[draw.Index].ForegroundBrush, _glyphRuns[draw.Index].GlyphRun);
                else
                {
                    var item = _lineObjects[draw.Index];
                    _objects[item.ObjectIndex].Run.Draw(drawingContext, item.Origin, _rightToLeft, false);
                }
            }
            _symbol?.Draw(drawingContext, new Point(), InvertAxes.None);
            if (_underlines != null)
                foreach (var underline in _underlines)
                {
                    // Match the existing source SimpleTextLine baseline/top-edge
                    // pairing. Both ProGPU renderers consume the typed rectangle.
                    drawingContext.PushGuidelineY2(Baseline, underline.Bounds.Top - Baseline);
                    try { drawingContext.DrawRectangle(underline.Brush, null, underline.Bounds); }
                    finally { drawingContext.Pop(); }
                }
        }
        finally { drawingContext.Pop(); if (antiInversion != null) drawingContext.Pop(); }
    }

    public override TextLine Collapse(params TextCollapsingProperties[] properties)
    {
        CheckAlive();
        return CollapseCore(properties);
    }

    private TextLine CollapseCore(TextCollapsingProperties[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Length == 0) return this;
        var collapsing = properties[0] ?? throw new ArgumentException("Collapsing properties cannot be null.", nameof(properties));
        if (!double.IsFinite(collapsing.Width) || collapsing.Width < 0 || collapsing.Width > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(properties));
        // The immutable original paragraph remains usable through this view
        // even when its original TextLine wrapper has been disposed.
        if (_uncollapsed != null)
        {
            var result = _uncollapsed.CollapseCore(properties);
            return ReferenceEquals(result, _uncollapsed) ? new PortableTextLine(_uncollapsed, _lineIndex) : result;
        }
        if (Width + _indent <= collapsing.Width) return this;
        if (collapsing.Symbol is not TextCharacters characters || characters.Properties == null || characters.Length <= 0)
            throw Unsupported("non-text collapsing symbols");
        var source = new CollapsingSymbolSource(characters, PixelsPerDip);
        source.Initialize();
        var paragraphProperties = new CollapsingSymbolProperties(characters.Properties, _rightToLeft);
        var settings = new FormatSettings(_formatter, source, new TextRunCacheImp(),
            new ParaProp(_formatter, paragraphProperties, false), null, true, TextFormattingMode.Ideal, false);
        using var symbol = (PortableTextLine)Create(settings, 0, 0, PixelsPerDip, _service);
        if (symbol._paragraph.Lines.Length != 1 || symbol._text.Length != characters.Length)
            throw Unsupported("multiline collapsing symbols");
        var request = new PortableTextCollapseRequest(_lineIndex, (float)Math.Max(0, collapsing.Width - _indent),
            (float)symbol.WidthIncludingTrailingWhitespace, collapsing.Style switch
            {
                TextCollapsingStyle.TrailingCharacter => PortableTextTrimming.Character,
                TextCollapsingStyle.TrailingWord => PortableTextTrimming.Word,
                _ => throw Unsupported("unknown collapsing granularity")
            });
        var collapsed = _paragraph.Collapse(request) ?? throw new InvalidOperationException("The provider returned no collapsed paragraph.");
        if (collapsed.CollapsedRange is not { } range || range.LineIndex != _lineIndex ||
            range.Start < Info.InputStart || range.Start >= range.End || range.End != Info.InputEnd ||
            (uint)range.SymbolGlyphIndex >= collapsed.Glyphs.Length || !collapsed.Glyphs.Span[range.SymbolGlyphIndex].IsCollapseSymbol)
            throw new InvalidOperationException("The provider returned invalid collapsed source ranges.");
        return new PortableTextLine(collapsed, _text, _properties, _face, _paragraphStart, _lineIndex, _newlines, _endsParagraph,
            _paragraphWidth, _indent, _baseline, _height, _rightToLeft, _runs, PixelsPerDip, _alignment,
            _styles, _fixedHeight, _sourceMap, _endScope, _formatter, _service, symbol, this);
    }

    public override IList<TextCollapsedRange> GetTextCollapsedRanges()
    {
        CheckAlive();
        if (_paragraph.CollapsedRange is not { } c) return null;
        int first = _paragraphStart + _sourceMap.ToSource(c.Start, true);
        int end = _paragraphStart + _sourceMap.ToSource(c.End, true);
        double sign = _paragraph.Glyphs.Span[c.SymbolGlyphIndex].Advance;
        return new[] { new TextCollapsedRange(first, end - first,
            Math.Max(0, _uncollapsed.WidthIncludingTrailingWhitespace - WidthIncludingTrailingWhitespace + sign)) };
    }

    private sealed class CollapsingSymbolSource(TextCharacters characters, double pixelsPerDip) : TextSource
    {
        public override TextRun GetTextRun(int index) => index == 0 ? characters : new TextEndOfParagraph(1);
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int index)
            => new(0, new(characters.Properties.CultureInfo, new CharacterBufferRange(string.Empty, 0, 0)));
        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index) => index;
        internal void Initialize() => PixelsPerDip = pixelsPerDip;
    }

    private sealed class CollapsingSymbolProperties(TextRunProperties properties, bool rtl) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => 0;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => properties;
        public override TextWrapping TextWrapping => TextWrapping.NoWrap;
        public override TextMarkerProperties TextMarkerProperties => null;
        public override double Indent => 0;
    }
    public override CharacterHit GetCharacterHitFromDistance(double distance)
    {
        CheckAlive(); var hit = _paragraph.HitTest(_lineIndex, ToNativePointX(distance));
        if (!hit.Trailing) return new(_paragraphStart + _sourceMap.ToSource(hit.Position, true), 0);
        int before = _paragraph.GetNextLogicalCaret(_lineIndex, hit.Position, true);
        int sourceStart = _sourceMap.ToSource(before, true), sourceEnd = _sourceMap.ToSource(hit.Position, false);
        if (sourceEnd <= sourceStart) return new(_paragraphStart + sourceStart, 0);
        return new(_paragraphStart + sourceStart, sourceEnd - sourceStart);
    }
    public override double GetDistanceFromCharacterHit(CharacterHit hit)
    {
        CheckAlive();
        int position = _sourceMap.ToText(Math.Clamp(checked(hit.FirstCharacterIndex + hit.TrailingLength) - _paragraphStart, 0, _sourceMap.SourceLength));
        if (_paragraph.CollapsedRange is { } c && position > c.Start && position < c.End)
            position = hit.TrailingLength != 0 ? c.End : c.Start;
        return ToLogicalPointX(_paragraph.GetCaretDistance(_lineIndex, new(position, hit.TrailingLength != 0)));
    }
    public override CharacterHit GetNextCaretCharacterHit(CharacterHit hit) => Move(hit, false);
    // Physical fragment movement stays in the retained native paragraph. Return
    // source offsets (including hidden document edges), never native UTF-16 indices.
    internal bool TryMoveFragmentCaret(int sourcePosition, bool trailing, bool down, double preferredX,
        out int targetPosition, out bool targetTrailing, out int fragmentDelta)
    {
        CheckAlive();
        targetPosition = sourcePosition; targetTrailing = trailing; fragmentDelta = 0;
        if (_paragraph is not IPortableExcludedTextParagraph excluded) return false;
        if (!double.IsFinite(preferredX))
            throw new ArgumentOutOfRangeException(nameof(preferredX));
        float nativePreferredX = ToNativePointX(preferredX);
        if (!float.IsFinite(nativePreferredX))
            throw new ArgumentOutOfRangeException(nameof(preferredX));
        if (sourcePosition < First || sourcePosition > First + Length)
            throw new ArgumentOutOfRangeException(nameof(sourcePosition));
        int position = _sourceMap.ToText(Math.Clamp(sourcePosition - _paragraphStart, 0, _sourceMap.SourceLength));
        var carets = excluded.Carets.Span;
        int selected = -1;
        for (int index = 0; index < carets.Length; ++index)
        {
            var caret = carets[index];
            if (caret.FragmentIndex != _lineIndex || caret.Position != position) continue;
            if (selected < 0) selected = index;
            if (caret.Trailing == trailing) { selected = index; break; }
        }
        if (selected < 0) throw new InvalidOperationException("Source position has no retained native fragment caret.");
        int moved = excluded.MoveCaret(selected, down ? PortableTextCaretMovement.Down : PortableTextCaretMovement.Up,
            nativePreferredX);
        if ((uint)moved >= (uint)carets.Length)
            throw new InvalidOperationException("Native fragment movement returned an invalid caret.");
        var target = carets[moved];
        targetPosition = _paragraphStart + _sourceMap.ToSource(target.Position, !target.Trailing);
        targetTrailing = target.Trailing;
        fragmentDelta = target.FragmentIndex - _lineIndex;
        return true;
    }
    public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit hit) => Move(hit, true);
    public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit hit) => Move(hit, true);
    internal override bool IsAtCaretCharacterHit(CharacterHit hit, int cpFirst)
    {
        CheckAlive();
        int position = checked(hit.FirstCharacterIndex + hit.TrailingLength);
        if (position < First || position > First + Length) return false;
        // Logical native moves return canonical leading hits. CharacterHit object
        // equality would incorrectly reject every equivalent trailing affinity.
        // Hidden formatting edges map to the same shaping boundary, while CR/LF
        // and LineBreak source interiors are not additional caret stops.
        if (position >= End) return position == End || position == First + Length;
        int textPosition = _sourceMap.ToText(position - _paragraphStart);
        if (textPosition == Info.InputStart || textPosition == Info.InputEnd) return true;
        int previous = _paragraph.GetNextLogicalCaret(_lineIndex, textPosition, true);
        return _paragraph.GetNextLogicalCaret(_lineIndex, previous, false) == textPosition;
    }
    private CharacterHit Move(CharacterHit hit, bool previous)
    {
        CheckAlive(); int position = hit.FirstCharacterIndex + hit.TrailingLength;
        if (position >= End && !previous) return new(First + Length, 0);
        int textPosition = _sourceMap.ToText(Math.Clamp(position - _paragraphStart, 0, _sourceMap.SourceLength));
        return new(_paragraphStart + _sourceMap.ToSource(_paragraph.GetNextLogicalCaret(_lineIndex, textPosition, previous), true), 0);
    }
    public override IList<TextBounds> GetTextBounds(int first, int length)
    {
        CheckAlive();
        if (length < 0) { first += length; length = -length; }
        int start = _sourceMap.ToText(Math.Clamp(first, First, End) - _paragraphStart);
        int end = _sourceMap.ToText(Math.Clamp(checked(first + length), First, End) - _paragraphStart);
        // A source terminator needs a caret box despite having no ink cluster.
        // Hidden formatting edges retain caret navigation, but are not selection
        // rectangles. Only a range intersecting the actual newline gets this box.
        if (length > 0 && start == end && NewlineLength > 0 &&
            first >= First && first < First + Length && checked(first + length) > End)
            return new[] { new TextBounds(new Rect(GetNonInkCaretX(first),
                0, 0, Height), _rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, null) };
        var rectangles = new PortableRect[Math.Max(1, Info.GlyphCount)];
        var result = new List<TextBounds>();
        foreach (var run in _selectionRuns)
        {
            int from = Math.Max(start, run.Start);
            int to = Math.Min(end, run.End);
            if (from >= to) continue;
            int count = _paragraph.GetSelection(_lineIndex, from, to, rectangles);
            for (int i = 0; i < count; i++)
            {
                IList<TextRunBounds> runBounds;
                if (_objectBounds != null && _objectBounds.TryGetValue(run.Start, out var objectBound))
                    runBounds = new[] { objectBound };
                else
                {
                    SourceStyle style = _styles[StyleIndex(from)];
                    int sourceFirst = _paragraphStart + _sourceMap.ToSource(from, true);
                    int sourceEnd = _paragraphStart + _sourceMap.ToSource(to, false);
                    var r = rectangles[i];
                    runBounds = new[]
                    {
                        new TextRunBounds(
                            new Rect(ToLogicalRectangleX(r.X, r.Width), Baseline - style.Baseline, r.Width, style.Height),
                            sourceFirst, sourceEnd, style.Run)
                    };
                }
                var rectangle = rectangles[i];
                result.Add(new(new Rect(ToLogicalRectangleX(rectangle.X, rectangle.Width), 0, rectangle.Width, Height),
                    (run.Level & 1) != 0 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, runBounds));
            }
        }
        return result;
    }
    // Source formatting edges have caret positions but no selection geometry.
    // Keep GetTextBounds empty for those edges; TextBlock's point-caret query
    // consumes this separate rectangle instead of treating the edge as ink.
    internal bool TryGetNonInkCaretBounds(int sourcePosition, out Rect rectangle, out FlowDirection flowDirection)
        => TryGetNonInkCaretBounds(sourcePosition, false, out rectangle, out flowDirection);
    internal bool TryGetNonInkCaretBounds(int sourcePosition, bool isTerminalInsertion,
        out Rect rectangle, out FlowDirection flowDirection)
    {
        CheckAlive();
        rectangle = Rect.Empty;
        flowDirection = _rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (sourcePosition < First || sourcePosition > First + Length ||
            (sourcePosition == First + Length && _lineIndex + 1 != _paragraph.Lines.Length))
            return false;
        int start = _sourceMap.ToText(Math.Clamp(sourcePosition, First, End) - _paragraphStart);
        int end = _sourceMap.ToText(Math.Clamp(checked(sourcePosition + 1), First, End) - _paragraphStart);
        if (start != end) return false;
        // The native caret owns the actual bidi affinity at the source edge.
        // In an RTL paragraph ending with an LTR run, WPF places the terminal
        // insertion at that run's trailing edge, not the paragraph's left edge.
        // The source host still identifies terminal positions (which may be
        // before hidden closing edges), but that identity does not override
        // native affinity for this non-ink caret box.
        double x = GetNonInkCaretX(sourcePosition);
        rectangle = new Rect(x, 0, 0, Height);
        return true;
    }

    private double GetNonInkCaretX(int sourcePosition)
    {
        // WPF places an explicit hard-break symbol at the complete source line's
        // trailing edge, including trailing whitespace. The shaped paragraph's
        // logical caret at that UTF-16 offset can instead belong to the final
        // bidi run (notably contextual Arabic digits). EndOfParagraph retains
        // native caret affinity; only the actual hard break uses this edge.
        if (sourcePosition == End && NewlineLength > 0 && !_endsParagraph)
            return Start + WidthIncludingTrailingWhitespace;
        return GetDistanceFromCharacterHit(new CharacterHit(sourcePosition, 0));
    }
    public override IList<TextSpan<TextRun>> GetTextRunSpans()
    {
        var result = new List<TextSpan<TextRun>>(); int cp = _paragraphStart;
        foreach (var run in _runs)
        {
            int overlap = Math.Min(cp + run.Length, First + Length) - Math.Max(cp, First);
            if (overlap > 0) result.Add(new(overlap, run.Value)); cp += run.Length;
        }
        return result;
    }
    public override IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns()
    {
        CheckAlive();
        return _collapsedGlyphRuns ?? _glyphRuns;
    }
    public override TextLineBreak GetTextLineBreak() => _uncollapsed != null ? _uncollapsed.GetTextLineBreak() :
        _lineIndex + 1 < _paragraph.Lines.Length ?
        new TextLineBreak(null, IntPtr.Zero) { PortableContinuation = new(this, _lineIndex + 1, End) } :
        _endScope == null ? null : new TextLineBreak(_endScope, IntPtr.Zero);
    // FormatLine floors its requested width to WPF's 1/300-DIP ideal grid.
    // Native advances may be a fraction of one ideal unit larger than that
    // floor while still fitting the caller's real width. Do not report that
    // quantization alone as overflow: TextBlock would request an ellipsis at
    // its original width, where Collapse correctly returns the uncollapsed line.
    public override bool HasOverflowed => _paragraphWidth > 0 &&
        Start + Width > _paragraphWidth + TextFormatterImp.IdealToRealWithNoRounding(1);
    public override bool HasCollapsed => _paragraph.CollapsedRange != null;
    public override int Length => End - First + NewlineLength;
    public override int NewlineLength => _uncollapsed != null ? _uncollapsed.NewlineLength :
        _lineIndex + 1 == _paragraph.Lines.Length ? _newlines : 0;
    public override int TrailingWhitespaceLength => _trailing + NewlineLength;
    public override int DependentLength => _sourceMap.SourceLength - (End - _paragraphStart);
    public override double Start => Fragment is { } fragment
        ? _indent + fragment.Left + (_alignment switch
            { TextAlignment.Right => fragment.Width - _width, TextAlignment.Center => (fragment.Width - _width) / 2, _ => 0 })
        : _indent + (_paragraphWidth <= 0 ? 0 : _alignment switch
    { TextAlignment.Right => _paragraphWidth - _indent - _width, TextAlignment.Center => (_paragraphWidth - _indent - _width) / 2, _ => 0 });
    public override double Width => _width;
    public override double WidthIncludingTrailingWhitespace => Info.Width;
    public override double Height => _height;
    public override double TextHeight => _height;
    public override double Baseline => _baseline;
    public override double TextBaseline => _baseline;
    public override double MarkerBaseline => _baseline;
    public override double MarkerHeight => _height;
    public override double Extent => _ink.IsEmpty ? 0 : _ink.Height;
    public override double OverhangLeading => _ink.IsEmpty ? 0 : Start - _ink.Left;
    public override double OverhangTrailing => _ink.IsEmpty ? 0 : _ink.Right - Start - Width;
    public override double OverhangAfter => _ink.IsEmpty ? 0 : _ink.Bottom - Height;
}
