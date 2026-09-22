using System.Windows.Media.ProGPU.Composition;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfPortableTextFormattingTests
{
    [Fact]
    public void DigitContextAdapterPreservesNativeStrongContextAndGraphemeBoundaries()
    {
        IPortableTextDigitContext provider = new WpfPortableTextFormatting();
        char[] source = "\u06271\u064E2 A3".ToCharArray();
        byte[] context = new byte[source.Length];
        byte[] withGraphemes = new byte[source.Length];
        byte[] graphemes = new byte[source.Length];

        Assert.False(provider.ResolveDigitContext(source, false, context));
        Assert.False(provider.ResolveDigitContext(source, false, withGraphemes, graphemes));

        Assert.Equal(new byte[] { 1, 1, 1, 1, 1, 0, 0 }, context);
        Assert.Equal(context, withGraphemes);
        Assert.Equal(new byte[] { 1, 1, 0, 1, 1, 1, 1 }, graphemes);
        Assert.Equal("\u06271\u064E2 A3", new string(source));

        // The returned context also connects a preceding source chunk to the
        // current hard segment before WPF resolves physical font ranges.
        byte[] prefixContext = new byte[1];
        bool preceding = provider.ResolveDigitContext("\u0627", false, prefixContext);
        byte[] digits = new byte[3];
        Assert.True(provider.ResolveDigitContext("123", preceding, digits));
        Assert.Equal(new byte[] { 1, 1, 1 }, digits);

        Array.Fill(withGraphemes, (byte)0xA5);
        Assert.Throws<ArgumentException>(() => provider.ResolveDigitContext(
            source, false, withGraphemes, new byte[source.Length - 1]));
        Assert.All(withGraphemes, value => Assert.Equal((byte)0xA5, value));
        Assert.Equal("\u06271\u064E2 A3", new string(source));
    }

    [Fact]
    public void StyledDigitAdapterMatchesDirectNativeArabicGlyphsAndSourcePositions()
    {
        byte[] data = ReadArabicFont();
        var face = new TtfFont(data);
        var font = new PortableTextFont(data, 0, face.UnitsPerEm);
        var provider = new WpfPortableTextFormatting();
        char[] source = ['1', '2', '3'];
        var original = new PortableTextParagraphRequest(source, font, 24, 32, 1000,
            false, PortableTextAlignment.Left,
            Styles: new PortableTextStyle[] { new(0, source.Length, font, 24, DigitZero: 0x0660) });
        var direct = original with
        {
            Text = "\u0661\u0662\u0663".AsMemory(),
            Styles = new PortableTextStyle[] { new(0, source.Length, font, 24) }
        };

        IPortableTextParagraph substituted = provider.Format(original);
        IPortableTextParagraph expected = provider.Format(direct);

        Assert.Equal(new[] { '1', '2', '3' }, source);
        Assert.Equal(expected.Lines.ToArray(), substituted.Lines.ToArray());
        Assert.Equal(expected.Glyphs.ToArray(), substituted.Glyphs.ToArray());
        var line = Assert.Single(substituted.Lines.ToArray());
        Assert.Equal((0, 3), (line.InputStart, line.InputEnd));
        Assert.True(line.Width > 0);
        var glyphs = substituted.Glyphs.ToArray();
        Assert.Equal(3, glyphs.Length);
        Assert.Equal(new[] { 0, 1, 2 }, glyphs.Select(glyph => glyph.Cluster).Order().ToArray());
        foreach (var glyph in glyphs)
        {
            uint expectedGlyph = face.GetGlyphIndex(0x0660U + (uint)(source[glyph.Cluster] - '0'));
            Assert.NotEqual(0U, expectedGlyph);
            Assert.Equal(expectedGlyph, glyph.GlyphId);
            Assert.Equal(glyph.Cluster + 1, glyph.ClusterEnd);
            Assert.Equal(0U, glyph.FontIndex);
        }
        Assert.Same(expected.GetNativeFont(0), substituted.GetNativeFont(0));
        Assert.NotNull(substituted.GetNativeFont(0));
        for (int position = 0; position <= source.Length; position++)
        {
            var hit = new PortableTextHit(position, false);
            Assert.Equal(expected.GetCaretDistance(0, hit), substituted.GetCaretDistance(0, hit));
        }
    }

    private static byte[] ReadArabicFont()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "Microsoft.DotNet.Wpf", "src",
                "PresentationCore", "Fonts", "trado.ttf");
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        throw new InvalidOperationException("The repository Traditional Arabic font fixture is required.");
    }
}
