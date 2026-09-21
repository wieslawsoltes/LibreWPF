using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WindowsNativeGlyphRunContractTests
{
    [Fact]
    public void ManagedMilMarshallingSkipsPortableGlyphRunsBeforeNativeDirectWriteAccess()
    {
        var portableTextPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "internal",
            "Text",
            "TextInterface",
            "PortableTextInterface.cs");
        var glyphRunPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "GlyphRun.cs");
        var renderDataPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "RenderData.cs");
        var generatedRenderDataPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "System",
            "Windows",
            "Media",
            "Generated",
            "RenderData.cs");
        var renderDataGeneratorPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "WpfGfx",
            "codegen",
            "mcg",
            "generators",
            "renderdata.cs");
        var nativeFontHeaderPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "DirectWriteForwarder",
            "CPP",
            "DWriteWrapper",
            "Font.h");
        var nativeFontSourcePath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "DirectWriteForwarder",
            "CPP",
            "DWriteWrapper",
            "Font.cpp");
        var typefaceMapPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationCore",
            "MS",
            "internal",
            "Shaping",
            "TypefaceMap.cs");

        var portableText = File.ReadAllText(portableTextPath);
        var glyphRun = File.ReadAllText(glyphRunPath);
        var renderData = File.ReadAllText(renderDataPath);
        var generatedRenderData = File.ReadAllText(generatedRenderDataPath);
        var renderDataGenerator = File.ReadAllText(renderDataGeneratorPath);
        var nativeFontHeader = File.ReadAllText(nativeFontHeaderPath);
        var nativeFontSource = File.ReadAllText(nativeFontSourcePath);
        var typefaceMap = File.ReadAllText(typefaceMapPath);

        Assert.Contains("internal bool HasDWriteFont => false;", portableText, StringComparison.Ordinal);
        Assert.Contains("return _glyphTypeface.HasDWriteFont;", glyphRun, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(renderData, "resource is GlyphRun glyphRun && !glyphRun.HasDWriteFont"));
        Assert.Contains("data.hGlyphRun = glyphRun.HasDWriteFont", generatedRenderData, StringComparison.Ordinal);
        Assert.Contains(": 0;", generatedRenderData, StringComparison.Ordinal);
        Assert.Contains("instruction.Name == \"DrawGlyphRun\" && field.PropertyName == \"GlyphRun\"", renderDataGenerator, StringComparison.Ordinal);
        Assert.Contains("[[handleName]] = glyphRun.HasDWriteFont", renderDataGenerator, StringComparison.Ordinal);
        Assert.Contains("property bool HasDWriteFont", nativeFontHeader, StringComparison.Ordinal);
        Assert.Contains("bool Font::HasDWriteFont::get()", nativeFontSource, StringComparison.Ordinal);
        Assert.Contains("spans = new List<Span>(analyzedSpans.Count);", typefaceMap, StringComparison.Ordinal);
        Assert.Contains("new Span(analyzedSpan.element, analyzedSpan.length)", typefaceMap, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;

        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
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
