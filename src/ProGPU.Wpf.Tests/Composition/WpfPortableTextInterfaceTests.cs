using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfPortableTextInterfaceTests
{
    [Fact]
    public void PortableTextInterfaceContainsManagedSfntFontFaceBoundary()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "internal",
            "Text",
            "TextInterface",
            "PortableTextInterface.cs"));

        Assert.Contains("TTO_GSUB = 0x47535542", source, StringComparison.Ordinal);
        Assert.Contains("TTO_GPOS = 0x47504F53", source, StringComparison.Ordinal);
        Assert.Contains("TTO_GDEF = 0x47444546", source, StringComparison.Ordinal);
        Assert.Contains("FontCollection.FromFontSources(fontSources)", source, StringComparison.Ordinal);
        Assert.Contains("s_portableFamilyAliases", source, StringComparison.Ordinal);
        Assert.Contains("(\"Calibri\", new[]", source, StringComparison.Ordinal);
        Assert.Contains("(\"Comic Sans MS\", new[]", source, StringComparison.Ordinal);
        Assert.Contains("(\"Segoe UI\", new[]", source, StringComparison.Ordinal);
        Assert.Contains("(\"Consolas\", new[]", source, StringComparison.Ordinal);
        Assert.Contains("TryFindFamilyName(candidate, out index)", source, StringComparison.Ordinal);
        Assert.Contains("internal sealed class PortableFontData", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntFontFace = ProGPU.Text.SfntFontFace;", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntGlyphBounds = ProGPU.Text.SfntGlyphBounds;", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntFontSubsetter = ProGPU.Text.SfntFontSubsetter;", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntHorizontalGlyphMetrics = ProGPU.Text.SfntHorizontalGlyphMetrics;", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntSimpleGlyphMetrics = ProGPU.Text.SfntSimpleGlyphMetrics;", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntSimpleGlyphRun = ProGPU.Text.SfntSimpleGlyphRun;", source, StringComparison.Ordinal);
        Assert.Contains("using ProGpuSfntSimpleGlyphShaper = ProGPU.Text.SfntSimpleGlyphShaper;", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<ProGpuSfntFontFace> faces = ProGpuSfntFontFace.LoadFaces(data);", source, StringComparison.Ordinal);
        Assert.Contains("GetSfntFace().TryGetTable(TagToString(tag), out ReadOnlyMemory<byte> tableDataMemory)", source, StringComparison.Ordinal);
        Assert.Contains("_sfntFace.TryGetGlyphCount(out ushort glyphCount)", source, StringComparison.Ordinal);
        Assert.Contains("GetSfntFace().TryGetGlyphIndex(codePoint, out ushort glyphIndex)", source, StringComparison.Ordinal);
        Assert.Contains("sfntFace.TryGetHorizontalGlyphMetrics(glyphIndex, out ProGpuSfntHorizontalGlyphMetrics metrics)", source, StringComparison.Ordinal);
        Assert.Contains("sfntFace.TryGetGlyphBounds(glyphIndex, out ProGpuSfntGlyphBounds glyphBounds)", source, StringComparison.Ordinal);
        Assert.Contains("return GetSfntFace().TryGetEmbeddingRights(out fsType);", source, StringComparison.Ordinal);
        Assert.Contains("internal ushort GetGlyphIndex(uint codePoint)", source, StringComparison.Ordinal);
        Assert.Contains("internal GlyphMetrics GetGlyphMetrics(ushort glyphIndex)", source, StringComparison.Ordinal);
        Assert.Contains("internal bool TryGetTable(uint tag, out byte[] tableData)", source, StringComparison.Ordinal);
        Assert.Contains("return _fontData.TryGetTable((uint)openTypeTableTag, out tableData);", source, StringComparison.Ordinal);
        Assert.Contains("return _fontData.TryGetEmbeddingRights(out fsType);", source, StringComparison.Ordinal);
        Assert.Contains("ProGpuSfntSimpleGlyphRun glyphRun = ProGpuSfntSimpleGlyphShaper.CreateGlyphRun(", source, StringComparison.Ordinal);
        Assert.Contains("ProGpuSfntSimpleGlyphShaper.FillGlyphAdvances(", source, StringComparison.Ordinal);
        Assert.Contains("return new ProGpuSfntSimpleGlyphMetrics(metrics.AdvanceWidth, metrics.AdvanceHeight);", source, StringComparison.Ordinal);
        Assert.Contains("private static void FillGlyphPlacements(", source, StringComparison.Ordinal);
        Assert.Contains("Marshal.Copy((IntPtr)fontData, fontCopy, 0, fileSize);", source, StringComparison.Ordinal);
        Assert.Contains("ProGpuSfntFontSubsetter.TryCreateGlyphIdPreservingSubset(", source, StringComparison.Ordinal);

        Assert.DoesNotContain("The portable WPF font face is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF font object is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF font collection is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF text analyzer is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The portable WPF TrueType subsetter is not yet backed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private CmapData ParseCmap()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private ushort GetAdvanceWidth(ushort glyphIndex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private short GetLeftSideBearing(ushort glyphIndex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static SimpleGlyphRun CreateSimpleGlyphRun", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ushort GetSimpleGlyphIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool IsControlGlyph", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static uint ReadCodePoint(char* textString", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly struct SimpleGlyphRun", source, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
