// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Text;
using MS.Internal.TextFormatting;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Documents;

// Source adapter only: every index remains an offset in the original container.
// TextFormatter/PortableTextLine owns modifier evaluation and native shaping.
internal sealed class PortableDocumentParagraphSource : TextSource, IPortableExcludedTextSource, IPortableFloatingTextSource
{
    private readonly Paragraph _paragraph;
    private readonly ITextContainer _container;
    private readonly double _paragraphWidth;
    private readonly PortableTextExclusionRequest _exclusions;
    private readonly PortableDocumentAnchorLayout _anchors;
    private PortableTextExclusionRequest _segmentExclusions;
    private int _segmentStart;
    private readonly bool _floating;
    private double _floatingOrigin;
    private readonly List<PortableTextExclusion> _floatExclusions;
    internal bool HasInlineObjects { get; private set; }
    internal int Start { get; }
    internal int End { get; }

    internal PortableDocumentParagraphSource(Paragraph paragraph, double pixelsPerDip, double paragraphWidth = double.NaN)
        : this(paragraph, pixelsPerDip, paragraphWidth, null) { }

    internal PortableDocumentParagraphSource(Paragraph paragraph, double pixelsPerDip, double paragraphWidth,
        PortableTextExclusionRequest exclusions)
        : this(paragraph, pixelsPerDip, paragraphWidth, exclusions, null) { }

    internal PortableDocumentParagraphSource(Paragraph paragraph, double pixelsPerDip, double paragraphWidth,
        PortableTextExclusionRequest exclusions, PortableDocumentAnchorLayout anchors)
        : this(paragraph, pixelsPerDip, paragraphWidth, exclusions, anchors, false) { }

    internal PortableDocumentParagraphSource(Paragraph paragraph, double pixelsPerDip, double paragraphWidth,
        PortableTextExclusionRequest exclusions, PortableDocumentAnchorLayout anchors, bool floating)
    {
        anchors?.ValidateFor(paragraph);
        _paragraph = paragraph;
        _container = paragraph.TextContainer;
        _paragraphWidth = paragraphWidth;
        _exclusions = exclusions;
        _anchors = anchors;
        if (floating && (anchors == null || exclusions != null))
            throw new ArgumentException("Floating source requires owned anchors and no fixed exclusions.");
        _floating = floating;
        if (floating) _floatExclusions = new();
        Start = paragraph.ElementStart.Offset;
        _segmentStart = Start;
        _segmentExclusions = exclusions;
        End = paragraph.ElementEnd.Offset;
        PixelsPerDip = pixelsPerDip;
    }

    PortableTextExclusionRequest IPortableExcludedTextSource.GetExclusions(int firstSourceIndex)
    {
        _anchors?.ValidateFor(_paragraph);
        if (_exclusions != null && firstSourceIndex != _segmentStart)
            throw new PlatformNotSupportedException("Excluded hard-line continuation requires a source-resolved segment origin.");
        return _segmentExclusions;
    }

    internal void AdvanceExcludedSegment(int start, double nativeBottom)
    {
        if (_exclusions == null || start <= _segmentStart || start >= End)
            throw new InvalidOperationException("Invalid excluded source segment transition.");
        var next = _exclusions.At(nativeBottom);
        _segmentStart = start;
        _segmentExclusions = next;
    }

    PortableTextFloatingRequest IPortableFloatingTextSource.GetFloats(int firstSourceIndex, int sourceLength)
    {
        if (!_floating) return null;
        _anchors.ValidateFor(_paragraph);
        if (firstSourceIndex != _segmentStart) throw new InvalidOperationException("Floating source segment origin was not advanced.");
        return new(256, _floatingOrigin, _anchors.GetFloatingChildren(firstSourceIndex, sourceLength), _floatExclusions.ToArray());
    }

    internal void AdvanceFloatingSegment(int start, double nativeBottom, ReadOnlySpan<PortableTextFloatPlacement> placements)
    {
        if (!_floating || start <= _segmentStart || start > End || !double.IsFinite(nativeBottom) || nativeBottom < _floatingOrigin)
            throw new InvalidOperationException("Invalid floating hard-segment transition.");
        foreach (var p in placements) _floatExclusions.Add(new(p.Left, p.Top, p.Right, p.Bottom));
        _segmentStart = start;
        _floatingOrigin = nativeBottom;
    }

    public override TextRun GetTextRun(int dcp)
    {
        if (dcp < Start || dcp >= End) throw new ArgumentOutOfRangeException(nameof(dcp));
        if (_anchors != null && _anchors.TryGetSourceRange(dcp, out _, out int anchorEnd))
            return new TextHidden(anchorEnd - dcp);
        // The paragraph closing edge is a real source symbol, not an extra
        // synthetic position after its document range.
        if (dcp == End - 1) return new TextEndOfParagraph(1);
        StaticTextPointer position = _container.CreateStaticPointerAtOffset(dcp);
        TextRun run;
        switch (position.GetPointerContext(LogicalDirection.Forward))
        {
            case TextPointerContext.Text:
                int limit = Math.Min(End - 1 - dcp,
                    position.GetOffsetToPosition(_container.Highlights.GetNextPropertyChangePosition(position, LogicalDirection.Forward)));
                // One lookahead unit protects the bounded copy from splitting a
                // scalar. Highlight/style boundaries remain real source ranges.
                char[] buffer = new char[Math.Min(4097, limit)];
                int copied = position.GetTextInRun(LogicalDirection.Forward, buffer, 0, buffer.Length);
                int length = Math.Min(copied, 4096);
                if (copied > length && char.IsHighSurrogate(buffer[length - 1]) && char.IsLowSurrogate(buffer[length])) --length;
                if (length == 0) throw new InvalidOperationException("A document text run did not advance its source position.");
                run = new TextCharacters(buffer, 0, length,
                    new TextProperties(position.Parent ?? _paragraph, position, false, true, PixelsPerDip));
                break;
            case TextPointerContext.ElementStart:
                var opening = (TextElement)position.GetAdjacentElement(LogicalDirection.Forward);
                if (ReferenceEquals(opening, _paragraph)) return new TextHidden(1);
                Inline inline = RequireInline(opening);
                if (inline is LineBreak) return new TextEndOfLine(2);
                TextDecorationCollection decorations = DynamicPropertyReader.GetTextDecorations(inline);
                if (HasDirectionalScope(inline))
                    run = new TextSpanModifier(1, decorations, inline.Foreground, inline.FlowDirection);
                else if (decorations is { Count: > 0 })
                    run = new TextSpanModifier(1, decorations, inline.Foreground);
                else
                    run = new TextHidden(1);
                break;
            case TextPointerContext.ElementEnd:
                Inline closing = RequireInline((TextElement)position.GetAdjacentElement(LogicalDirection.Forward));
                run = HasDirectionalScope(closing) || DynamicPropertyReader.GetTextDecorations(closing) is { Count: > 0 }
                    ? new TextEndOfSegment(1) : new TextHidden(1);
                break;
            case TextPointerContext.EmbeddedElement:
                if (position.Parent is not InlineUIContainer owner ||
                    position.GetAdjacentElement(LogicalDirection.Forward) is not UIElement child || !ReferenceEquals(owner.Child, child))
                    throw new PlatformNotSupportedException("Portable document objects require an actual InlineUIContainer child.");
                HasInlineObjects = true;
                run = new PortableDocumentInlineObject(owner, child,
                    new TextProperties(owner, position, false, true, PixelsPerDip), _paragraphWidth);
                break;
            default:
                throw new InvalidOperationException("The source paragraph ended before its closing edge.");
        }
        run.Properties?.PixelsPerDip = PixelsPerDip;
        return run;
    }

    private static Inline RequireInline(TextElement element)
    {
        if (element is not Inline inline || inline is AnchoredBlock)
            throw new PlatformNotSupportedException("Portable paragraph content requires an explicit inline-object or anchored-block contract.");
        // Empty styled elements have no shaping text. Their separate line-metric
        // contribution cannot be manufactured with zero-width-space glyphs.
        if (inline.IsEmpty && inline is not LineBreak && inline.Parent is TextElement parent &&
            (inline.FontSize != parent.FontSize || !Equals(inline.FontFamily, parent.FontFamily) ||
             inline.FontWeight != parent.FontWeight || inline.FontStyle != parent.FontStyle ||
             inline.FontStretch != parent.FontStretch || inline.BaselineAlignment != BaselineAlignment.Baseline))
            throw new PlatformNotSupportedException("Empty styled inline metrics require the native non-ink metric contract.");
        return inline;
    }

    private static bool HasDirectionalScope(Inline inline) => inline.Parent is DependencyObject parent &&
        inline.FlowDirection != (FlowDirection)parent.GetValue(FrameworkElement.FlowDirectionProperty);

    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int dcp)
    {
        _anchors?.ValidateFor(_paragraph);
        if (dcp < Start || dcp > End) throw new ArgumentOutOfRangeException(nameof(dcp));
        ITextPointer position = _container.CreatePointerAtOffset(dcp, LogicalDirection.Backward);
        int hidden = 0;
        while (position.Offset > Start)
        {
            if (_anchors != null && _anchors.TryGetSourceRange(position.Offset - 1, out int anchorStart, out _))
            {
                int skipped = position.Offset - anchorStart;
                position.MoveByOffset(-skipped);
                hidden += skipped;
                continue;
            }
            if (position.GetPointerContext(LogicalDirection.Backward) == TextPointerContext.Text) break;
            position.MoveByOffset(-1);
            ++hidden;
        }
        int limit = Math.Min(4097, Math.Min(position.Offset - Start, position.GetTextRunLength(LogicalDirection.Backward)));
        char[] buffer = new char[limit];
        int copied = position.GetTextInRun(LogicalDirection.Backward, buffer, 0, limit);
        int first = Math.Max(0, copied - 4096);
        if (first > 0 && char.IsLowSurrogate(buffer[first]) && char.IsHighSurrogate(buffer[first - 1])) ++first;
        int length = copied - first;
        CultureInfo culture = DynamicPropertyReader.GetCultureInfo(position.CreateStaticPointer().Parent ?? _paragraph);
        return new(hidden + length, new(culture, new CharacterBufferRange(buffer, first, length)));
    }

    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
        => textSourceCharacterIndex;
}
