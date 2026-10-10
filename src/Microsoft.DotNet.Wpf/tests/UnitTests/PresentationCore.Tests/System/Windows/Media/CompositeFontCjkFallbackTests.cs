// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Globalization;
using System.Linq;
using MS.Internal.Shaping;
using System.Windows.Media.TextFormatting;

namespace System.Windows.Media;

// Locks the macOS CJK fallback contract of the four default composite fonts. The composite
// maps used to list only Windows families for most CJK ranges, so a request that reached
// them fell through to the base family and drew .notdef (tofu). This exercises the actual
// resolution and layout, not the map data: for each default family, language and
// representative character it requires a real physical face, a real glyph (never .notdef)
// and a non-zero measured extent. Runs only under the portable media backend because the
// Windows-MIL path resolves through the Windows font collection instead.
[Collection("Sequential")]
public class CompositeFontCjkFallbackTests
{
    // The four default (system composite) font families.
    private static readonly string[] s_defaultFamilies =
    {
        "Global User Interface", "Global Monospace", "Global Sans Serif", "Global Serif"
    };

    private static readonly string[] s_languages = { "zh-Hans", "zh-Hant" };

    // U+3002 the common Chinese full stop; U+FE10 a vertical form (was missing entirely);
    // U+FF61 halfwidth ideographic full stop (its Traditional map was mislabeled); U+6587 a
    // basic Han ideograph baseline.
    private static readonly string[] s_characters = { "\u3002", "\uFE10", "\uFF61", "\u6587" };

    public static IEnumerable<object[]> Cases()
    {
        foreach (string family in s_defaultFamilies)
            foreach (string language in s_languages)
                foreach (string text in s_characters)
                    yield return new object[] { family, language, text };
    }

    // The portable backend must be selected before WPF is constructed; a Windows-MIL
    // test process resolves through the real Windows font collection and must not run here.
    private sealed class PortableMediaTheoryAttribute : TheoryAttribute
    {
        public PortableMediaTheoryAttribute([System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0) : base(sourceFilePath, sourceLineNumber)
        {
            if (ProGPU.Wpf.Interop.PortableWpfRuntime.ConfiguredMediaBackend != ProGPU.Wpf.Interop.PortableWpfMediaBackend.Portable)
                Skip = "Requires a process initialized with portable media before WPF construction.";
        }
    }

    [PortableMediaTheory]
    [MemberData(nameof(Cases))]
    public void DefaultCompositeFontResolvesChineseFallbackToARealGlyph(string familyName, string language, string text)
    {
        var family = new FontFamily(familyName);
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var culture = CultureInfo.GetCultureInfo(language);

        // The face the composite fallback actually selects for this character.
        var cache = new GlyphingCache(16);
        var runs = new List<TextSpan<ScaledShapeTypeface>>();
        cache.GetPortableFontRuns(typeface, new CharacterBufferRange(text, 0, text.Length), culture, runs);
        var face = runs.Select(run => run.Value?.ShapeTypeface?.GlyphTypeface).FirstOrDefault(f => f != null);

        Assert.True(face != null, $"{familyName}/{language}: no physical face resolved for '{text}' (U+{(int)text[0]:X4}).");
        Assert.True(face!.HasCharacter(text[0]),
            $"{familyName}/{language}: '{face.FamilyNames.Values.First()}' has no glyph for U+{(int)text[0]:X4} (would draw .notdef).");

        // The real glyph also has a positive advance, i.e. the character has an actual ink
        // extent rather than a zero-advance .notdef placeholder.
        Assert.True(face.CharacterToGlyphMap.TryGetValue(text[0], out ushort glyph) && glyph != 0 &&
            face.AdvanceWidths.TryGetValue(glyph, out double advance) && advance > 0,
            $"{familyName}/{language}: '{text}' (U+{(int)text[0]:X4}) has no positive advance width.");

        // ...and the language selects the right script variant of the face: a zh-Hant run
        // must land on a Traditional-priority face (PingFang TC/Heiti TC/Songti TC), not a
        // Simplified one, and vice versa. Without this a shared range that lets Traditional
        // fall through to a Simplified-first generic map would still pass.
        string names = string.Join(" | ", face.FamilyNames.Values);
        if (language == "zh-Hant")
            Assert.True(names.Contains("TC", StringComparison.Ordinal),
                $"{familyName}/zh-Hant: '{text}' resolved to the non-Traditional face '{names}'.");
        else
            Assert.True(names.Contains("SC", StringComparison.Ordinal) || names.Contains("GB", StringComparison.Ordinal),
                $"{familyName}/zh-Hans: '{text}' resolved to the non-Simplified face '{names}'.");

        // A full control layout (TextBlock.DesiredSize.Width > 0) needs the real text-shaping
        // provider, which only a host process registers; the unit-test process has no such
        // provider, so the face+glyph assertions above are the meaningful in-process lock.
        // The end-to-end extent is covered by the host integration tests.
    }
}
