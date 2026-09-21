using System.Globalization;
using System.Text.Json;

namespace System.Windows.Media.Tests;

public sealed class PortableFluentSymbolsTests
{
    [Theory]
    [InlineData("Segoe Fluent Icons")]
    [InlineData("Segoe MDL2 Assets")]
    [InlineData("Segoe Fluent Icons, Segoe MDL2 Assets")]
    public void LegacySymbolAliasesResolveRenderableAddedGlyphs(string familyName)
    {
        Typeface typeface = new(new FontFamily(familyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface));
        Assert.NotNull(glyphTypeface);

        foreach (int codepoint in new[] { 0xF08D, 0xF08E, 0xF08F, 0xF090 })
        {
            Assert.True(glyphTypeface.CharacterToGlyphMap.TryGetValue(codepoint, out ushort glyph));
            Assert.NotEqual(0, glyph);

            Geometry outline = glyphTypeface.GetGlyphOutline(glyph, 20, 20);
            Assert.False(outline.Bounds.IsEmpty);
            Assert.True(outline.Bounds.Width > 0);
            Assert.True(outline.Bounds.Height > 0);
        }
    }

    [Fact]
    public void PortableCompatibilityFamilyResolvesCompleteReviewedPrivateUseRange()
    {
        Typeface typeface = new(
            new FontFamily("LibreWPF Fluent Symbols"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface));
        Assert.NotNull(glyphTypeface);
        Assert.Equal(
            "LibreWPF Fluent Symbols",
            glyphTypeface.FamilyNames[CultureInfo.GetCultureInfo("en-US")]);

        string mapPath = Path.Combine(
            FindRepositoryRoot(),
            "src/Microsoft.DotNet.Wpf/src/PresentationCore/Fonts/LibreWPF.FluentSymbols/LegacyFluentGlyphMap.json");
        using JsonDocument mapping = JsonDocument.Parse(File.ReadAllText(mapPath));
        foreach (JsonElement item in mapping.RootElement.GetProperty("legacyGlyphs").EnumerateArray())
        {
            int codepoint = Convert.ToInt32(item.GetProperty("codepoint").GetString(), 16);
            Assert.True(glyphTypeface.CharacterToGlyphMap.TryGetValue(codepoint, out ushort glyph));
            Assert.NotEqual(0, glyph);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Microsoft.Dotnet.Wpf.sln"))
                && Directory.Exists(Path.Combine(directory.FullName, "src", "Microsoft.DotNet.Wpf")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the WPF repository root.");
    }
}
