using ProGPU.Text;
using ProGPU.Wpf.Interop;

internal static class NativeMilTextJustificationSmoke
{
    internal static void Run()
    {
        if (!PortableWpfServiceRegistry.TryGetTextFormatting(out var service))
            throw new InvalidOperationException("Native text service must precede application media.");
        var regular = new TtfFont(Path.Combine(AppContext.BaseDirectory, "CollapseFonts", "Inter-Medium.ttf"));
        var bold = new TtfFont(Path.Combine(AppContext.BaseDirectory, "CollapseFonts", "Inter-Bold.ttf"));
        var font = new PortableTextFont(regular.FontData, 0, checked((ushort)regular.UnitsPerEm));
        var second = new PortableTextFont(bold.FontData, 0, checked((ushort)bold.UnitsPerEm));
        const string text = "office a\u0301 M M M M";
        foreach (bool rtl in new[] { false, true })
        {
            var request = new PortableTextParagraphRequest(text.AsMemory(), font, 16, 24, 0, rtl,
                PortableTextAlignment.Left, Styles: new PortableTextStyle[]
                {
                    new(0, 10, font, 16), new(10, text.Length - 10, second, 18)
                });
            var unbounded = service.Format(request);
            float width = 1;
            foreach (var glyph in unbounded.Glyphs.Span)
                if (glyph.Cluster < 12) width += glyph.Advance;
            request = request with { MaximumWidth = width };
            var left = service.Format(request);
            var justified = service.Format(request with { Alignment = PortableTextAlignment.Justify });
            if (justified.Lines.Length != 2 || justified.Lines.Span[0].InputEnd != 12)
                throw new InvalidOperationException("Justification changed source wrap boundaries.");
            var line = justified.Lines.Span[0];
            float trailingWidth = 0;
            for (int i = 0; i < justified.Glyphs.Length; ++i)
            {
                var a = left.Glyphs.Span[i]; var b = justified.Glyphs.Span[i];
                if (a.Cluster != b.Cluster || a.ClusterEnd != b.ClusterEnd || a.GlyphId != b.GlyphId ||
                    a.FontIndex != b.FontIndex || a.BidiLevel != b.BidiLevel)
                    throw new InvalidOperationException("Justification changed shaped source ownership.");
                if (b.Cluster == 11) trailingWidth += b.Advance;
                if (b.Cluster >= 12 && (Math.Abs(a.Advance - b.Advance) > .001 || Math.Abs(a.X - b.X) > .001))
                    throw new InvalidOperationException("Justification changed the final line.");
                if (b.Cluster != 6) continue;
                if (b.Advance <= a.Advance)
                    throw new InvalidOperationException("Interior source space was not expanded.");
                var rectangles = new PortableRect[line.GlyphCount];
                int count = justified.GetSelection(0, 6, 7, rectangles);
                if (count != 1 || Math.Abs(rectangles[0].X - b.X) > .001 ||
                    Math.Abs(rectangles[0].Width - b.Advance) > .001)
                    throw new InvalidOperationException("Expanded space selection differs from native glyph geometry.");
                float leading = justified.GetCaretDistance(0, new(6, false));
                float trailing = justified.GetCaretDistance(0, new(7, true));
                if (Math.Abs(Math.Abs(trailing - leading) - b.Advance) > .001)
                    throw new InvalidOperationException("Expanded space caret stops retained stale advances.");
                var hit = justified.HitTest(0, b.X + b.Advance * .75F);
                if (hit.Position != 6 && hit.Position != 7)
                    throw new InvalidOperationException("Expanded space hit lost its source cluster.");
            }
            if (Math.Abs(line.Width - trailingWidth - width) > .001 ||
                justified.GetNextLogicalCaret(0, 7, false) != 9)
                throw new InvalidOperationException("Justification lost visible width or split the combining cluster.");
        }
        Console.WriteLine("Native word-space justification passed: styled/RTL wrapping, source clusters, selection, caret and hit geometry.");
    }
}
