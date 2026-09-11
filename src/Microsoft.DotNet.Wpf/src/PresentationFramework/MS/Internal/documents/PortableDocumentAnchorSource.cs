// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows.Documents;

namespace MS.Internal.Documents;

// Original document identity and symbol ownership, not flattened shaping text.
// Callers retain these descriptors only for their source layout generation.
internal sealed record PortableDocumentAnchorSource(Paragraph Paragraph, AnchoredBlock Anchor, int Start, int End)
{
    internal static IReadOnlyList<PortableDocumentAnchorSource> Collect(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        paragraph.Dispatcher.VerifyAccess();
        List<PortableDocumentAnchorSource> result = null;
        int visited = 0;
        void Visit(InlineCollection inlines, int depth)
        {
            if (depth > 256) throw new PlatformNotSupportedException("Portable anchored inline nesting budget exceeded.");
            foreach (Inline inline in inlines)
            {
                if (++visited > 1 << 20) throw new PlatformNotSupportedException("Portable anchored inline budget exceeded.");
                if (!ReferenceEquals(inline.TextContainer, paragraph.TextContainer))
                    throw new InvalidOperationException("Anchored inline belongs to another source document.");
                if (inline is AnchoredBlock anchor)
                {
                    int start = anchor.ElementStart.Offset, end = anchor.ElementEnd.Offset;
                    if (start < paragraph.ContentStart.Offset || end > paragraph.ContentEnd.Offset || end <= start ||
                        (result is { Count: > 0 } && result[^1].End > start))
                        throw new InvalidOperationException("Anchored source ranges are not ordered within their paragraph.");
                    (result ??= new()).Add(new(paragraph, anchor, start, end));
                    // Its BlockCollection is formatted independently from the
                    // original source. Do not visit child paragraph text here.
                }
                else if (inline is System.Windows.Documents.Span span) Visit(span.Inlines, depth + 1);
            }
        }
        Visit(paragraph.Inlines, 0);
        return result == null ? Array.Empty<PortableDocumentAnchorSource>() : result.AsReadOnly();
    }
}
