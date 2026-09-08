// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using MS.Internal.Text;
using MS.Internal.TextFormatting;

namespace MS.Internal.Documents;

// Source adapter only: every index remains an offset in the original container.
// TextFormatter/PortableTextLine owns modifier evaluation and native shaping.
internal sealed class PortableDocumentParagraphSource : TextSource
{
    private readonly Paragraph _paragraph;
    private readonly ITextContainer _container;
    internal int Start { get; }
    internal int End { get; }

    internal PortableDocumentParagraphSource(Paragraph paragraph, double pixelsPerDip)
    {
        _paragraph = paragraph;
        _container = paragraph.TextContainer;
        Start = paragraph.ElementStart.Offset;
        End = paragraph.ElementEnd.Offset;
        PixelsPerDip = pixelsPerDip;
    }

    public override TextRun GetTextRun(int dcp)
    {
        if (dcp < Start || dcp >= End) throw new ArgumentOutOfRangeException(nameof(dcp));
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
                throw new PlatformNotSupportedException("Portable document embedded objects require the native inline-object contract.");
            default:
                throw new InvalidOperationException("The source paragraph ended before its closing edge.");
        }
        run.Properties?.PixelsPerDip = PixelsPerDip;
        return run;
    }

    private static Inline RequireInline(TextElement element)
    {
        if (element is not Inline inline || inline is InlineUIContainer || inline is AnchoredBlock)
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
        if (dcp < Start || dcp > End) throw new ArgumentOutOfRangeException(nameof(dcp));
        ITextPointer position = _container.CreatePointerAtOffset(dcp, LogicalDirection.Backward);
        int hidden = 0;
        while (position.Offset > Start && position.GetPointerContext(LogicalDirection.Backward) != TextPointerContext.Text)
        {
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
