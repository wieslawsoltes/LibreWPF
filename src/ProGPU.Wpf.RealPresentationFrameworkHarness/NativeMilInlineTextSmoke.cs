using ProGPU.Text;
using ProGPU.Wpf.Interop;

internal static class NativeMilInlineTextSmoke
{
    internal static void Run()
    {
        if (!PortableWpfServiceRegistry.TryGetTextFormatting(out var service) || service is not IPortableInlineTextFormatting inline)
            throw new InvalidOperationException("Native text provider must expose measured inline paragraphs.");
        var regular = new TtfFont(Path.Combine(AppContext.BaseDirectory, "CollapseFonts", "Inter-Medium.ttf"));
        var bold = new TtfFont(Path.Combine(AppContext.BaseDirectory, "CollapseFonts", "Inter-Bold.ttf"));
        var font = new PortableTextFont(regular.FontData, 0, checked((ushort)regular.UnitsPerEm));
        var second = new PortableTextFont(bold.FontData, 0, checked((ushort)bold.UnitsPerEm));
        foreach (bool rtl in new[] { false, true })
        foreach (bool multipleFaces in new[] { false, true })
        {
            var request = new PortableTextParagraphRequest("A\uFFFCB".AsMemory(), font, 16, 20, 31, rtl,
                PortableTextAlignment.Left, Styles: new PortableTextStyle[]
                {
                    new(0, 2, font, 16), new(2, 1, multipleFaces ? second : font, 16)
                }, MeasureIntrinsicWidths: true);
            var metrics = new PortableTextStyleMetrics[] { new(12, 4), new(12, 4) };
            var objects = new PortableTextInlineObject[] { new(1, 30.25f, 35, 7) };
            var paragraph = inline.FormatInline(request, metrics, objects);
            if (paragraph.Lines.Length != 3 || paragraph.InlineObjects.Length != 1 || paragraph.IntrinsicWidths == null)
                throw new InvalidOperationException("Inline object wrapping or intrinsic output is missing.");
            var placement = paragraph.InlineObjects.Span[0];
            if (placement != new PortableTextInlineObjectPlacement(1, 1, 1, 0, 20, 30.25f, 42))
                throw new InvalidOperationException($"Inline placement lost source/line ownership: {placement}.");
            var glyph = paragraph.Glyphs.Span[placement.GlyphIndex];
            if (!glyph.IsInlineObject || glyph.IsTab || glyph.IsCollapseSymbol || glyph.Cluster != 1 || glyph.ClusterEnd != 2)
                throw new InvalidOperationException("Inline object is not a distinct non-ink source cluster.");
            foreach (var textGlyph in paragraph.Glyphs.Span)
                if (!textGlyph.IsInlineObject && paragraph.GetNativeFont(textGlyph.FontIndex) == null)
                    throw new InvalidOperationException("Styled text lost its real render face.");
            float[] tops = [0, 20, 62], heights = [20, 42, 20], baselines = [12, 35, 12];
            for (int i = 0; i < 3; i++)
            {
                RequireNear(paragraph.Lines.Span[i].Y, tops[i], "line top");
                RequireNear(paragraph.Lines.Span[i].Height, heights[i], "line height");
                RequireNear(paragraph.GetBaselineOffset(i), baselines[i], "baseline offset");
            }
            var rectangles = new PortableRect[4];
            int count = paragraph.GetSelection(1, 1, 2, rectangles);
            if (count != 1) throw new InvalidOperationException("Inline selection lost the object cluster.");
            RequireNear((float)rectangles[0].Y, 0, "line-local selection Y");
            RequireNear((float)rectangles[0].Height, 42, "selection height");
            RequireNear((float)rectangles[0].Width, 30.25f, "selection width");
            RequireNear(Math.Abs(paragraph.GetCaretDistance(1, new(1, false)) -
                paragraph.GetCaretDistance(1, new(2, true))), 30.25f, "object caret width");
            if (paragraph.HitTest(1, 15).Position is not 1 and not 2 ||
                paragraph.GetNextLogicalCaret(1, 1, false) != 2)
                throw new InvalidOperationException("Inline hit or logical caret lost its source position.");
            objects[0] = new(1, 1, 1, 1);
            metrics[0] = new(1, 1);
            if (paragraph.InlineObjects.Span[0] != placement)
                throw new InvalidOperationException("Inline output borrowed mutable source metrics.");
            bool rejected = false;
            try { paragraph.Collapse(new(1, 10, 2, PortableTextTrimming.Character)); }
            catch (NotSupportedException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Measured collapse requires missing sign metrics.");

            var ordinary = service.Format(request with { Text = "AB".AsMemory(), Styles = default });
            if (ordinary is IPortableInlineTextParagraph || ordinary.Glyphs.Span[0].IsInlineObject)
                throw new InvalidOperationException("Ordinary text unexpectedly changed its output contract.");
        }
        Console.WriteLine("Native inline provider passed: styled/LTR/RTL wrapping, owned placements, measured baselines, selection and caret geometry.");
    }

    private static void RequireNear(float actual, float expected, string description)
    {
        if (!float.IsFinite(actual) || Math.Abs(actual - expected) > .001f)
            throw new InvalidOperationException($"Inline {description}: expected {expected}, got {actual}.");
    }
}
