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
        RunExcluded(service, font);
        Console.WriteLine("Native inline provider passed: styled/LTR/RTL wrapping, owned placements, measured baselines, selection and caret geometry.");
    }

    private static void RunExcluded(IPortableTextFormatting service, PortableTextFont font)
    {
        if (service is not IPortableExcludedTextFormatting excluded)
            throw new InvalidOperationException("Native text provider must expose explicit exclusion formatting.");
        foreach (bool rtl in new[] { false, true })
        {
            var request = new PortableTextParagraphRequest("A\ufffcB".AsMemory(), font, 16, 20, 31, rtl,
                PortableTextAlignment.Left, Styles: new PortableTextStyle[] { new(0, 3, font, 16) },
                MeasureIntrinsicWidths: true);
            PortableTextStyleMetrics[] metrics = [new(12, 4)];
            PortableTextInlineObject[] objects = [new(1, 30.25f, 35, 7)];
            var options = new PortableTextExclusionOptions(128);
            PortableTextExclusion[] rectangles = [new(0, 0, 31, 20)];
            var paragraph = excluded.FormatExcluded(request, metrics, objects, options, rectangles);
            if (paragraph.Fragments.Length != 3 || paragraph.ContentHeight != 102 || paragraph.MeasuredWidth != 31 ||
                paragraph.Lines.Span[1].Y != 40 || paragraph.Fragments.Span[2].Top != 82 ||
                paragraph.InlineObjects.Span[0].Y != 40 || paragraph.GetBaselineOffset(1) != 35)
                throw new InvalidOperationException("Excluded provider lost native fragment tops or inline baselines.");
            var selection = new PortableRect[4];
            if (paragraph.GetSelection(1, 1, 2, selection) != 1 || selection[0].Y != 0 ||
                selection[0].Height != 42 || paragraph.HitTest(1, 15).Position is not 1 and not 2)
                throw new InvalidOperationException("Excluded provider lost line-local source interaction.");
            int below = paragraph.MoveCaret(0, PortableTextCaretMovement.Down, 0);
            if (paragraph.Carets.Span[below].Y != 40)
                throw new InvalidOperationException("Excluded provider navigation lost the cleared row.");
            rectangles[0] = new(0, 0, 31, 100);
            if (paragraph.ContentHeight != 102 || paragraph.Fragments.Span[0].Top != 20)
                throw new InvalidOperationException("Excluded provider borrowed source rectangles.");

            request = request with { Text = "A A".AsMemory(), MaximumWidth = 40 };
            var split = excluded.FormatExcluded(request, metrics, [], options, [new(16, 0, 24, 20)]);
            if (split.Fragments.Length != 2 || split.ContentHeight != 20 ||
                split.Lines.Span[0].Y != 0 || split.Lines.Span[1].Y != 0 ||
                split.Fragments.Span[0].RowIndex != split.Fragments.Span[1].RowIndex)
                throw new InvalidOperationException("Excluded provider stacked same-row fragments.");
            int leftFragment = rtl ? 1 : 0, current = -1;
            for (int i = 0; i < split.Carets.Length; i++)
                if (split.Carets.Span[i].FragmentIndex == leftFragment &&
                    (current < 0 || split.Carets.Span[i].X >= split.Carets.Span[current].X)) current = i;
            int next = split.MoveCaret(current, PortableTextCaretMovement.Right, 24);
            if (split.Carets.Span[next].FragmentIndex == leftFragment || split.Carets.Span[next].Y != 0)
                throw new InvalidOperationException("Excluded provider did not cross the same-row gap.");
        }
        Console.WriteLine("Native excluded provider passed: retained frames, inline source identity, selection and physical caret movement.");
    }

    private static void RequireNear(float actual, float expected, string description)
    {
        if (!float.IsFinite(actual) || Math.Abs(actual - expected) > .001f)
            throw new InvalidOperationException($"Inline {description}: expected {expected}, got {actual}.");
    }
}
