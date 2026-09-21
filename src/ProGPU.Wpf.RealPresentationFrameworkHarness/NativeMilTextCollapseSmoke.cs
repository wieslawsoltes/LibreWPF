using ProGPU.Text;
using ProGPU.Wpf.Interop;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition.Mil;

internal static class NativeMilTextCollapseSmoke
{
    // Diagnostic-only public binding for the harness's side-by-side real/shim
    // assemblies. Replace with direct calls when that dual-load harness retires.
    internal static void RunSourceHeader(Assembly framework, Assembly core, Assembly windowsBase)
    {
        const string text = "Extended WPF Toolkit and AvalonDock through ProGPU.Wpf.Sdk";
        var type = framework.GetType("System.Windows.Controls.TextBlock", true)!;
        object header = Activator.CreateInstance(type)!;
        type.GetProperty("Text")!.SetValue(header, text);
        type.GetProperty("FontSize")!.SetValue(header, 16.0);
        var trimming = type.GetProperty("TextTrimming")!;
        trimming.SetValue(header, Enum.Parse(trimming.PropertyType, "CharacterEllipsis"));
        type.GetProperty("FontWeight")!.SetValue(header,
            core.GetType("System.Windows.FontWeights", true)!.GetProperty("SemiBold")!.GetValue(null));
        var size = windowsBase.GetType("System.Windows.Size", true)!;
        var rect = windowsBase.GetType("System.Windows.Rect", true)!;
        foreach (double width in new[] { 96.0, 160.0, 96.0 })
        {
            type.GetMethod("Measure", [size])!.Invoke(header, [Activator.CreateInstance(size, width, 48.0)]);
            type.GetMethod("Arrange", [rect])!.Invoke(header, [Activator.CreateInstance(rect, 0.0, 0.0, width, 48.0)]);
            type.GetMethod("UpdateLayout", Type.EmptyTypes)!.Invoke(header, null);
            object desired = type.GetProperty("DesiredSize")!.GetValue(header)!;
            double actualWidth = (double)size.GetProperty("Width")!.GetValue(desired)!;
            if (actualWidth > width + .01 || (string)type.GetProperty("Text")!.GetValue(header)! != text)
                throw new InvalidOperationException("Source Toolkit header failed width/source-text preservation.");
            if (new WpfNativeMilSceneCompiler().BuildBatch(header, checked((uint)width), 48).GlyphRunFonts is not { Count: > 0 })
                throw new InvalidOperationException("Collapsed source header did not reach native MIL glyph resources.");
        }
        Console.WriteLine("Source Toolkit header collapse smoke passed: narrow/wide/narrow layout and native MIL glyph export.");
    }

    // Runs only in the existing native host qualification lane. This is real
    // native paragraph/interaction coverage, not a replacement for Toolkit UI fidelity.
    internal static void Run()
    {
        if (!PortableWpfServiceRegistry.TryGetTextFormatting(out var service))
            throw new InvalidOperationException("Native text service must be registered before host construction.");
        var regular = new TtfFont(Path.Combine(AppContext.BaseDirectory, "CollapseFonts", "Inter-Medium.ttf"));
        var bold = new TtfFont(Path.Combine(AppContext.BaseDirectory, "CollapseFonts", "Inter-Bold.ttf"));
        var font = new PortableTextFont(regular.FontData, 0, checked((ushort)regular.UnitsPerEm));
        var second = new PortableTextFont(bold.FontData, 0, checked((ushort)bold.UnitsPerEm));
        const string text = "office a\u0301 paragraph header with long content and a tab\tend";
        foreach (bool rtl in new[] { false, true })
        foreach (float wrapWidth in new[] { 0F, 75F })
        {
            var request = new PortableTextParagraphRequest(text.AsMemory(), font, 16, 24, wrapWidth, rtl,
                PortableTextAlignment.Left, Styles: new PortableTextStyle[]
                {
                    new(0, 10, font, 16), new(10, text.Length - 10, second, 18)
                }, IncrementalTab: 32, TabOrigin: 7);
            var original = service.Format(request);
            int lineIndex = wrapWidth == 0 ? 0 : 1;
            if (original.Lines.Length <= lineIndex) throw new InvalidOperationException("Expected wrapped source lines.");
            var sourceLine = original.Lines.Span[lineIndex];
            var originalGlyphs = original.Glyphs.ToArray();
            foreach (var mode in new[] { PortableTextTrimming.Character, PortableTextTrimming.Word })
            foreach (float width in new[] { sourceLine.Width / 2, 0F })
            {
                var collapse = new PortableTextCollapseRequest(lineIndex, width, 3, mode);
                var result = original.Collapse(collapse);
                if (!ReferenceEquals(result, original.Collapse(collapse)))
                    throw new InvalidOperationException("Repeated collapse must reuse its retained view.");
                var hidden = result.CollapsedRange ?? throw new InvalidOperationException("Missing hidden source range.");
                var line = result.Lines.Span[lineIndex];
                var sign = result.Glyphs.Span[hidden.SymbolGlyphIndex];
                if (line.InputStart != sourceLine.InputStart || line.InputEnd != sourceLine.InputEnd ||
                    hidden.Start >= hidden.End || hidden.End != sourceLine.InputEnd || !sign.IsCollapseSymbol || sign.IsTab)
                    throw new InvalidOperationException("Collapse lost source identity or sign metadata.");
                if (rtl && Math.Abs(sign.X) > .001F)
                    throw new InvalidOperationException("RTL collapse sign must precede retained visual content.");
                if (result.GetNextLogicalCaret(lineIndex, hidden.Start, false) != hidden.End)
                    throw new InvalidOperationException("A hidden glyph cluster became a caret stop.");
                var hit = result.HitTest(lineIndex, sign.X + sign.Advance * .75F);
                if (hit.Position != hidden.Start && hit.Position != hidden.End)
                    throw new InvalidOperationException("Collapsed sign hit did not resolve to hidden-range affinity.");
                var rectangles = new PortableRect[Math.Max(1, line.GlyphCount)];
                int count = result.GetSelection(lineIndex, hidden.Start, hidden.End, rectangles);
                if (count != 1 || Math.Abs(rectangles[0].X - sign.X) > .001 || Math.Abs(rectangles[0].Width - sign.Advance) > .001)
                    throw new InvalidOperationException("Hidden-range selection differs from sign geometry.");
                for (int i = 0; i < lineIndex; ++i)
                    if (result.Lines.Span[i] != original.Lines.Span[i])
                        throw new InvalidOperationException("Collapse changed a preceding wrapped line.");
            }
            if (!original.Glyphs.Span.SequenceEqual(originalGlyphs) || original.CollapsedRange != null)
                throw new InvalidOperationException("Collapse mutated the original paragraph.");
        }
        Console.WriteLine("Native text collapse smoke passed: styled/tabbed source ranges, wrapping, RTL, symbol hit/selection and reuse.");
    }
}
