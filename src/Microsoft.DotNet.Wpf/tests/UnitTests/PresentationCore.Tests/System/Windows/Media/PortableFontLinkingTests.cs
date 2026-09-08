// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Globalization;
using System.Linq;
using MS.Internal.Shaping;
using System.Windows.Media.TextFormatting;

namespace System.Windows.Media;

[Collection("Sequential")]
public class PortableFontLinkingTests
{
    private static (GlyphTypeface Face, string Target) Load(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", file);
        var face = new GlyphTypeface(new Uri(path));
        return (face, path + "#" + face.FamilyNames.Values.First());
    }

    [Fact]
    public void SourceFamilyFallbackReturnsPhysicalFacesWithoutTextItemization()
    {
        var latin = Load("Inter-Medium.ttf");
        var symbols = Load("LibreWPF.FluentSymbols.ttf");
        int symbol = symbols.Face.CharacterToGlyphMap.First(pair => pair.Key >= 0xe000 && pair.Key <= 0xf8ff &&
            pair.Value != 0 && !latin.Face.HasCharacter((uint)pair.Key)).Key;
        var typeface = new Typeface(new FontFamily(latin.Target), FontStyles.Normal, FontWeights.Normal,
            FontStretches.Normal, new FontFamily(symbols.Target));
        string text = "a" + (char)symbol + "b";
        var cache = new GlyphingCache(16);
        var first = new List<TextSpan<ScaledShapeTypeface>>();
        cache.GetPortableFontRuns(typeface, new CharacterBufferRange(text, 0, text.Length), CultureInfo.InvariantCulture, first);
        Assert.Equal(3, first.Count);
        Assert.Equal(latin.Face.FontUri, first[0].Value.ShapeTypeface.GlyphTypeface.FontUri);
        Assert.Equal(symbols.Face.FontUri, first[1].Value.ShapeTypeface.GlyphTypeface.FontUri);
        Assert.Equal(latin.Face.FontUri, first[2].Value.ShapeTypeface.GlyphTypeface.FontUri);
        Assert.All(first, item => { Assert.Equal(1, item.Length); Assert.False(item.Value.NullShape); Assert.Equal(1, item.Value.ScaleInEm); });
        var repeated = new List<TextSpan<ScaledShapeTypeface>>();
        cache.GetPortableFontRuns(typeface, new CharacterBufferRange(text, 0, text.Length), CultureInfo.InvariantCulture, repeated);
        Assert.Equal(first.Count, repeated.Count);
        for (int i = 0; i < first.Count; i++) Assert.Same(first[i].Value, repeated[i].Value);
    }

    [Fact]
    public void CompositeRangesKeepLanguageSelectionScaleAndCompleteUtf16Coverage()
    {
        var latin = Load("Inter-Medium.ttf");
        var family = new FontFamily();
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-10FFFF", Language = System.Windows.Markup.XmlLanguage.GetLanguage("en"), Target = latin.Target, Scale = .8 });
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-10FFFF", Target = latin.Target, Scale = 1.2 });
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal, null!);
        var cache = new GlyphingCache(16);
        var runs = new List<TextSpan<ScaledShapeTypeface>>();
        cache.GetPortableFontRuns(typeface, new CharacterBufferRange("abc", 0, 3), CultureInfo.GetCultureInfo("en-US"), runs);
        Assert.Equal(.8, Assert.Single(runs).Value.ScaleInEm);
        runs.Clear();
        cache.GetPortableFontRuns(typeface, new CharacterBufferRange("abc", 0, 3), CultureInfo.GetCultureInfo("fr-FR"), runs);
        Assert.Equal(1.2, Assert.Single(runs).Value.ScaleInEm);
        const string text = "a\U0001f642b";
        runs.Clear();
        cache.GetPortableFontRuns(typeface, new CharacterBufferRange(text, 0, text.Length), CultureInfo.GetCultureInfo("en-US"), runs);
        int end = 0;
        foreach (var run in runs)
        {
            Assert.True(run.Length > 0);
            end += run.Length;
            Assert.False(end < text.Length && char.IsLowSurrogate(text[end]));
        }
        Assert.Equal(text.Length, end);
    }
}
