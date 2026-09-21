using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class PortableFluentSymbolsTests
{
    private static readonly string s_repositoryRoot = FindRepositoryRoot();
    private static readonly string s_fontRoot = Path.Combine(
        s_repositoryRoot,
        "src",
        "Microsoft.DotNet.Wpf",
        "src",
        "PresentationCore",
        "Fonts",
        "LibreWPF.FluentSymbols");

    [Fact]
    public void GeneratedFontCoversEveryReviewedGalleryCodepoint()
    {
        string fontPath = Path.Combine(s_fontRoot, "LibreWPF.FluentSymbols.ttf");
        byte[] fontBytes = File.ReadAllBytes(fontPath);
        using JsonDocument mapping = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(s_fontRoot, "LegacyFluentGlyphMap.json")));
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(s_fontRoot, "SOURCE-MANIFEST.json")));

        JsonElement legacyGlyphs = mapping.RootElement.GetProperty("legacyGlyphs");
        JsonElement additions = mapping.RootElement.GetProperty("entries");
        Assert.Equal(1_475, legacyGlyphs.GetArrayLength());
        Assert.Equal(282, additions.GetArrayLength());

        string expectedHash = manifest.RootElement
            .GetProperty("generatedFont")
            .GetProperty("sha256")
            .GetString()!;
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(fontBytes)).ToLowerInvariant());

        SfntFont font = new(fontBytes);
        Assert.Equal("LibreWPF Fluent Symbols", font.FamilyName);
        Assert.Equal(0, font.EmbeddingFlags);

        HashSet<int> codepoints = new();
        foreach (JsonElement item in legacyGlyphs.EnumerateArray())
        {
            int codepoint = Convert.ToInt32(item.GetProperty("codepoint").GetString(), 16);
            Assert.True(codepoints.Add(codepoint), $"Duplicate U+{codepoint:X4} in the reviewed catalog.");
            Assert.NotEqual(0, font.GetGlyphIndex(codepoint));
        }

        foreach (int codepoint in new[]
                 {
                     0xE700, 0xE711, 0xE713, 0xE72B, 0xE73E, 0xE790, 0xE8C8, 0xEB51,
                     0xF08D, 0xF08E, 0xF08F, 0xF090, 0xF246, 0xF5F2, 0xF608, 0xF8CC,
                 })
        {
            ushort glyph = font.GetGlyphIndex(codepoint);
            Assert.NotEqual(0, glyph);
            Assert.True(font.HasGlyphOutline(glyph), $"U+{codepoint:X4} has no outline.");
        }
    }

    [Fact]
    public void GeneratedFontSourcesArePinnedAndRedistributable()
    {
        string notice = File.ReadAllText(Path.Combine(s_fontRoot, "NOTICE.md"));
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(s_fontRoot, "SOURCE-MANIFEST.json")));
        JsonElement[] sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToArray();

        AssertManifestFileHash(manifest.RootElement.GetProperty("mapping"));
        AssertManifestFileHash(manifest.RootElement.GetProperty("notice"));

        Assert.Contains("contains no outlines copied", notice, StringComparison.Ordinal);
        Assert.Contains("semantic substitutes", notice, StringComparison.Ordinal);
        Assert.Equal(3, sources.Length);
        Assert.Contains(sources, source =>
            source.GetProperty("repository").GetString() == "https://github.com/unoplatform/uno.fonts"
            && source.GetProperty("commit").GetString() == "ae06dc8d52ec90c4e050fd2f161711512deb0ba1"
            && source.GetProperty("license").GetString() == "Apache-2.0");
        Assert.Contains(sources, source =>
            source.GetProperty("repository").GetString() == "https://github.com/microsoft/fluentui-system-icons"
            && source.GetProperty("commit").GetString() == "32374ae9ccf107e026db0d9aa9c0d631328b8003"
            && source.GetProperty("license").GetString() == "MIT");
        Assert.All(sources, source =>
        {
            string licenseFile = source.GetProperty("licenseFile").GetString()!;
            Assert.True(File.Exists(Path.Combine(s_fontRoot, licenseFile)));
            Assert.Matches("^[0-9a-f]{64}$", source.GetProperty("sha256").GetString()!);
            Assert.Equal(
                source.GetProperty("licenseSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(s_fontRoot, licenseFile)))).ToLowerInvariant());
        });
    }

    private static void AssertManifestFileHash(JsonElement entry)
    {
        string path = entry.GetProperty("path").GetString()!;
        string expectedHash = entry.GetProperty("sha256").GetString()!;
        Assert.Equal(
            expectedHash,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(s_fontRoot, path)))).ToLowerInvariant());
    }

    [Fact]
    public void PortableDiscoveryAndTransportUseOneTypedLooseFontPath()
    {
        string portableText = File.ReadAllText(Path.Combine(
            s_repositoryRoot,
            "src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/Text/TextInterface/PortableTextInterface.cs"));
        string transportProject = File.ReadAllText(Path.Combine(
            s_repositoryRoot,
            "packaging/Microsoft.DotNet.Wpf.GitHub/Microsoft.DotNet.Wpf.GitHub.ArchNeutral.csproj"));
        string transportTargets = File.ReadAllText(Path.Combine(
            s_repositoryRoot,
            "packaging/Microsoft.DotNet.Wpf.GitHub/LibreWPF.Transport.targets"));
        string sdkTargets = File.ReadAllText(Path.Combine(
            s_repositoryRoot,
            "packaging/ProGPU.Wpf.Sdk/targets/ProGPU.Wpf.Sdk.targets"));
        string packageAudit = File.ReadAllText(Path.Combine(
            s_repositoryRoot,
            "eng/progpu-preview-package-audit.sh"));

        Assert.Contains("AppContext.BaseDirectory", portableText, StringComparison.Ordinal);
        Assert.Contains("\"LibreWPF\",", portableText, StringComparison.Ordinal);
        Assert.Contains("\"Fonts\",", portableText, StringComparison.Ordinal);
        Assert.Contains("\"LibreWPF.FluentSymbols.ttf\"", portableText, StringComparison.Ordinal);
        Assert.Contains("(\"Segoe Fluent Icons\", new[] { \"LibreWPF Fluent Symbols\" })", portableText, StringComparison.Ordinal);
        Assert.Contains("(\"Segoe MDL2 Assets\", new[] { \"LibreWPF Fluent Symbols\" })", portableText, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", portableText, StringComparison.Ordinal);

        Assert.Contains("PackagePath=\"buildTransitive/assets/LibreWPF/Fonts\"", transportProject, StringComparison.Ordinal);
        Assert.Contains("PackagePath=\"notices/LibreWPF.FluentSymbols\"", transportProject, StringComparison.Ordinal);
        Assert.Contains("assets/LibreWPF/Fonts/LibreWPF.FluentSymbols.ttf", transportTargets, StringComparison.Ordinal);
        Assert.Contains("Link=\"LibreWPF/Fonts/LibreWPF.FluentSymbols.ttf\"", transportTargets, StringComparison.Ordinal);
        Assert.Contains("LibreWPF/Notices/LibreWPF.FluentSymbols/NOTICE.md", transportTargets, StringComparison.Ordinal);
        Assert.Contains("LibreWPF/Notices/LibreWPF.FluentSymbols/LegacyFluentGlyphMap.json", transportTargets, StringComparison.Ordinal);
        Assert.Contains("LibreWPF/Notices/LibreWPF.FluentSymbols/licenses/%(Filename)%(Extension)", transportTargets, StringComparison.Ordinal);
        Assert.Contains("$(_ProGpuWpfManagedReferenceRoot)LibreWPF/Fonts/LibreWPF.FluentSymbols.ttf", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("require_entry_sha256 LibreWPF.Transport", packageAudit, StringComparison.Ordinal);
        Assert.Contains("value.generatedFont.sha256", packageAudit, StringComparison.Ordinal);
        Assert.Contains("source.licenseSha256", packageAudit, StringComparison.Ordinal);
        Assert.Contains("LibreWPF.FluentSymbols/LegacyFluentGlyphMap.json", packageAudit, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Segoe Fluent Icons")]
    [InlineData("Segoe MDL2 Assets")]
    public void ProGpuResolverAndRenderCommandUseAddedAliasGlyphs(string requestedFamily)
    {
        string fontPath = Path.Combine(s_fontRoot, "LibreWPF.FluentSymbols.ttf");
        TtfFont font = new(fontPath);
        uint[] codepoints = [0xF08D, 0xF08E, 0xF08F, 0xF090];
        ushort[] glyphs = codepoints.Select(font.GetGlyphIndex).ToArray();

        Assert.Equal("LibreWPF Fluent Symbols", font.FamilyName);
        Assert.All(glyphs, glyph => Assert.NotEqual(0, glyph));
        Assert.All(glyphs, glyph =>
        {
            var outline = font.GetGlyphOutline(glyph);
            Assert.NotNull(outline);
            Assert.True(outline!.Figures.Count > 0);
            Assert.True(outline.TryGetBounds(out var minimum, out var maximum));
            Assert.True(maximum.X > minimum.X);
            Assert.True(maximum.Y > minimum.Y);
        });

        PortableGlyphRun portable = new()
        {
            GlyphIndices = glyphs,
            AdvanceWidths = [20, 20, 20, 20],
            BaselineOrigin = new PortablePoint(2, 24),
            FontRenderingEmSize = 20,
            FontUri = fontPath,
            FontFamilyNames = [requestedFamily],
        };

        GlyphRun? resolved = WpfResourceResolver.AdaptGlyphRun(portable);
        Assert.NotNull(resolved);
        Assert.Equal("LibreWPF Fluent Symbols", resolved!.Font.FamilyName);
        Assert.Same(resolved.Font, portable.NativeFont);
        Assert.Equal(glyphs, resolved.GlyphIndices);

        ProGPU.Scene.DrawingContext commands = new();
        using ProGpuCompositionCommandSink sink = new(new DrawingContext(commands));
        sink.DrawGlyphRun(Brushes.Black, resolved);

        ProGPU.Scene.RenderCommand command = Assert.Single(commands.Commands);
        Assert.Equal(ProGPU.Scene.RenderCommandType.DrawGlyphRun, command.Type);
        Assert.Equal(glyphs, command.GlyphIndices);
        Assert.Same(resolved.Font, command.Font);
        Assert.True(command.Rect.Width > 0);
        Assert.True(command.Rect.Height > 0);
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

    private sealed class SfntFont
    {
        private readonly byte[] _bytes;
        private readonly Dictionary<string, (int Offset, int Length)> _tables;
        private readonly int _cmapOffset;
        private readonly int _glyfOffset;
        private readonly int _locaOffset;
        private readonly int _glyphCount;
        private readonly bool _longLocations;

        public SfntFont(byte[] bytes)
        {
            _bytes = bytes;
            int tableCount = ReadUInt16(4);
            _tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            for (int index = 0; index < tableCount; index++)
            {
                int record = 12 + (index * 16);
                string tag = Encoding.ASCII.GetString(bytes, record, 4);
                _tables[tag] = (ReadInt32(record + 8), ReadInt32(record + 12));
            }

            _cmapOffset = SelectCmapOffset();
            _glyfOffset = _tables["glyf"].Offset;
            _locaOffset = _tables["loca"].Offset;
            _glyphCount = ReadUInt16(_tables["maxp"].Offset + 4);
            _longLocations = ReadInt16(_tables["head"].Offset + 50) != 0;
            EmbeddingFlags = ReadUInt16(_tables["OS/2"].Offset + 8);
            FamilyName = ReadFamilyName();
        }

        public string FamilyName { get; }

        public ushort EmbeddingFlags { get; }

        public ushort GetGlyphIndex(int codepoint)
        {
            int format = ReadUInt16(_cmapOffset);
            return format switch
            {
                4 => ReadFormat4Glyph(codepoint),
                12 => ReadFormat12Glyph(codepoint),
                _ => throw new InvalidDataException($"Unsupported cmap format {format}."),
            };
        }

        public bool HasGlyphOutline(ushort glyph)
        {
            Assert.InRange(glyph, (ushort)1, checked((ushort)(_glyphCount - 1)));
            int start = ReadGlyphLocation(glyph);
            int end = ReadGlyphLocation(glyph + 1);
            return end > start && ReadInt16(_glyfOffset + start) != 0;
        }

        private int SelectCmapOffset()
        {
            int table = _tables["cmap"].Offset;
            int count = ReadUInt16(table + 2);
            int format4 = 0;
            for (int index = 0; index < count; index++)
            {
                int record = table + 4 + (index * 8);
                int subtable = table + ReadInt32(record + 4);
                int format = ReadUInt16(subtable);
                if (format == 12)
                {
                    return subtable;
                }

                if (format == 4)
                {
                    format4 = subtable;
                }
            }

            return format4 != 0 ? format4 : throw new InvalidDataException("No Unicode cmap was found.");
        }

        private ushort ReadFormat4Glyph(int codepoint)
        {
            if ((uint)codepoint > ushort.MaxValue)
            {
                return 0;
            }

            int segmentCount = ReadUInt16(_cmapOffset + 6) / 2;
            int endCodes = _cmapOffset + 14;
            int startCodes = endCodes + (segmentCount * 2) + 2;
            int deltas = startCodes + (segmentCount * 2);
            int rangeOffsets = deltas + (segmentCount * 2);
            for (int segment = 0; segment < segmentCount; segment++)
            {
                int end = ReadUInt16(endCodes + (segment * 2));
                if (codepoint > end)
                {
                    continue;
                }

                int start = ReadUInt16(startCodes + (segment * 2));
                if (codepoint < start)
                {
                    return 0;
                }

                int delta = ReadInt16(deltas + (segment * 2));
                int rangeOffsetAddress = rangeOffsets + (segment * 2);
                int rangeOffset = ReadUInt16(rangeOffsetAddress);
                if (rangeOffset == 0)
                {
                    return unchecked((ushort)(codepoint + delta));
                }

                ushort glyph = ReadUInt16(rangeOffsetAddress + rangeOffset + ((codepoint - start) * 2));
                return glyph == 0 ? (ushort)0 : unchecked((ushort)(glyph + delta));
            }

            return 0;
        }

        private ushort ReadFormat12Glyph(int codepoint)
        {
            int groups = ReadInt32(_cmapOffset + 12);
            int low = 0;
            int high = groups - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int group = _cmapOffset + 16 + (middle * 12);
                uint start = ReadUInt32(group);
                uint end = ReadUInt32(group + 4);
                if ((uint)codepoint < start)
                {
                    high = middle - 1;
                }
                else if ((uint)codepoint > end)
                {
                    low = middle + 1;
                }
                else
                {
                    return checked((ushort)(ReadUInt32(group + 8) + ((uint)codepoint - start)));
                }
            }

            return 0;
        }

        private int ReadGlyphLocation(int glyph)
        {
            return _longLocations
                ? ReadInt32(_locaOffset + (glyph * 4))
                : ReadUInt16(_locaOffset + (glyph * 2)) * 2;
        }

        private string ReadFamilyName()
        {
            int table = _tables["name"].Offset;
            int count = ReadUInt16(table + 2);
            int strings = table + ReadUInt16(table + 4);
            string? fallback = null;
            for (int index = 0; index < count; index++)
            {
                int record = table + 6 + (index * 12);
                int platform = ReadUInt16(record);
                int nameId = ReadUInt16(record + 6);
                if (nameId is not (1 or 16))
                {
                    continue;
                }

                int length = ReadUInt16(record + 8);
                int offset = strings + ReadUInt16(record + 10);
                string value = platform is 0 or 3
                    ? Encoding.BigEndianUnicode.GetString(_bytes, offset, length)
                    : Encoding.Latin1.GetString(_bytes, offset, length);
                if (nameId == 16)
                {
                    return value;
                }

                fallback ??= value;
            }

            return fallback ?? throw new InvalidDataException("The font has no family name.");
        }

        private short ReadInt16(int offset) => BinaryPrimitives.ReadInt16BigEndian(_bytes.AsSpan(offset, 2));
        private ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(offset, 2));
        private int ReadInt32(int offset) => BinaryPrimitives.ReadInt32BigEndian(_bytes.AsSpan(offset, 4));
        private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(offset, 4));
    }
}
