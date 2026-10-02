// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace MS.Internal.TextFormatting;

internal sealed partial class PortableTextLine
{
    private readonly record struct SourceLineInfo(int GlyphStart, int GlyphCount, int InputStart, int InputEnd,
        double Width, double Y, double Height);
    private readonly record struct SourceIntrinsicWidths(double Minimum, double Maximum);

    private static SourceLineInfo GetSourceLineInfo(IPortableTextParagraph paragraph, int index)
    {
        var line = paragraph.Lines.Span[index];
        if (paragraph is IPortableDisplayTextParagraph display)
        {
            var metrics = display.DisplayLineMetrics.Span[index];
            return new(line.GlyphStart, line.GlyphCount, line.InputStart, line.InputEnd,
                metrics.Width, metrics.Top, metrics.Height);
        }
        return new(line.GlyphStart, line.GlyphCount, line.InputStart, line.InputEnd, line.Width, line.Y, line.Height);
    }

    private double GetSourceAdvance(int index) => _paragraph is IPortableDisplayTextParagraph display
        ? display.DisplayGlyphMetrics.Span[index].Advance : _paragraph.Glyphs.Span[index].Advance;

    private static SourceIntrinsicWidths GetSourceIntrinsicWidths(IPortableTextParagraph paragraph)
    {
        if (paragraph is IPortableDisplayTextParagraph display)
        {
            var widths = display.DisplayIntrinsicWidths ?? throw Unsupported("the Display provider does not publish original double intrinsic widths");
            return new(widths.Minimum, widths.Maximum);
        }
        var ordinary = paragraph.IntrinsicWidths ?? throw Unsupported("the text provider does not publish intrinsic paragraph widths");
        return new(ordinary.Minimum, ordinary.Maximum);
    }

    private static double GetDisplayMaximumWidth(bool wrap, double width, double indent)
    {
        if (!wrap || width <= 0) return 0;
        double available = width - indent;
        if (!double.IsFinite(available) || available <= 0)
            throw Unsupported("source Display exhausted content width requires an explicit zero-available-width contract");
        return available;
    }

    private static PortableDisplayTextStyle[] GetDisplayStyles(IReadOnlyList<SourceStyle> styles)
    {
        var result = new PortableDisplayTextStyle[styles.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = new(styles[i].EmSize, styles[i].Baseline, styles[i].Height - styles[i].Baseline);
        return result;
    }

    private void ValidateDisplayParagraph(IPortableTextParagraph paragraph) => ValidateDisplayParagraph(paragraph,
        _formatter.TextFormattingMode == TextFormattingMode.Display, _text, _generationPixelsPerDip,
        _styles.Length == 0 ? 0 : _styles[0].EmSize,
        _formatter.TextFormattingMode == TextFormattingMode.Display ? GetDisplayStyles(_styles) : []);

    private static void ValidateDisplayParagraph(IPortableTextParagraph paragraph, bool displayMode, string text,
        double pixelsPerDip, double emSize, ReadOnlySpan<PortableDisplayTextStyle> styles)
    {
        if (!displayMode)
        {
            if (paragraph is IPortableDisplayTextParagraph)
                throw new InvalidOperationException("An Ideal request cannot acquire a Display generation.");
            return;
        }
        if (paragraph is not IPortableDisplayTextParagraph display || display.IsDisposed ||
            display.SourcePixelsPerDip != pixelsPerDip || display.SourceEmSize != emSize ||
            !display.SourceText.Span.SequenceEqual(text.AsSpan()) || !display.SourceStyles.Span.SequenceEqual(styles))
            throw new InvalidOperationException("The Display paragraph lost its original source text, em/DPI or style identity.");
        // The current GlyphRun/raster projection stores device scale as float.
        // Preserve the original double and reject, never relabel it after narrowing.
        if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0 || (float)pixelsPerDip != pixelsPerDip)
            throw Unsupported("the original Display device scale cannot be represented by the current render frame");
        if (display.DisplayGlyphMetrics.Length != paragraph.Glyphs.Length ||
            display.DisplayLineMetrics.Length != paragraph.Lines.Length)
            throw new InvalidOperationException("The Display paragraph lost its original glyph or writer metric coverage.");
        foreach (var glyph in display.DisplayGlyphMetrics.Span)
            if (!double.IsFinite(glyph.X) || !double.IsFinite(glyph.Y) || !double.IsFinite(glyph.Advance))
                throw new InvalidOperationException("The Display paragraph contains nonfinite source glyph metrics.");
        foreach (var line in display.DisplayLineMetrics.Span)
            if (!double.IsFinite(line.Width) || line.Width < 0 || !double.IsFinite(line.Top) ||
                !double.IsFinite(line.Height) || line.Height <= 0 || !double.IsFinite(line.BaselineOffset) ||
                !double.IsFinite(line.BaselineY))
                throw new InvalidOperationException("The Display paragraph contains invalid source writer frames.");
    }
}

// Snapshot identity before Retain can call a provider. Failed publication retains
// the exact returned owner for the existing retryable retirement path.
internal sealed class PortableTextDisplayIdentity
{
    private readonly string _text;
    private readonly double _em, _dpi;
    private readonly PortableDisplayTextStyle[] _styles;
    internal PortableTextDisplayIdentity(IPortableDisplayTextParagraph paragraph)
    {
        _text = paragraph.SourceText.ToString();
        _em = paragraph.SourceEmSize; _dpi = paragraph.SourcePixelsPerDip;
        _styles = paragraph.SourceStyles.ToArray();
    }
    internal void Validate(IPortableTextParagraph paragraph)
    {
        if (paragraph is not IPortableDisplayTextParagraph display || display.IsDisposed ||
            display.SourceEmSize != _em || display.SourcePixelsPerDip != _dpi ||
            !display.SourceText.Span.SequenceEqual(_text.AsSpan()) || !display.SourceStyles.Span.SequenceEqual(_styles))
            throw new InvalidOperationException("Retain changed the original Display generation's source identity.");
    }
}
