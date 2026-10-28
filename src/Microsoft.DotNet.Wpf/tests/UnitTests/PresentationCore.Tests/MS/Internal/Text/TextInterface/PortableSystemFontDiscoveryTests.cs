// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Linq;
using MS.Internal.FontCache;
using MS.Internal.Text.TextInterface;
using FontStyle = MS.Internal.Text.TextInterface.FontStyle;

namespace PresentationCore.Tests.MS.Internal.Text.TextInterface;

public sealed class PortableSystemFontDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("librewpf-font-discovery-").FullName;

    [Fact]
    public void OverlappingRootsOpenEachSourceOnceInFirstSeenOrder()
    {
        string child = Directory.CreateDirectory(Path.Combine(_root, "Supplemental")).FullName;
        string nested = CopyFont(child, "nested.ttf");
        string other = CopyFont(_root, "other.ttf");
        var factory = new CountingFontSourceFactory();

        FontCollection collection = FontCollection.FromUris(
            Factory.EnumerateSystemFontUris(Path.Combine(_root, "missing.ttf"), new[] { child, _root, child }), factory);

        Assert.Equal(new[] { new Uri(nested).AbsoluteUri, new Uri(other).AbsoluteUri }, factory.Sources);
        Assert.All(factory.Threads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
        Assert.Equal(1u, collection.FamilyCount);
        Assert.Equal(2u, collection[0].Count); // Same family in distinct files remains distinct.
        Assert.Equal(new[] { nested, other }, collection[0].Select(font => font.FontData.SourceUri.LocalPath));
        Assert.All(collection[0], font => Assert.False(font.FontData.IsFontDataResident));
        Assert.Equal(nested, collection[0].GetFirstMatchingFont(FontWeight.Normal, FontStretch.Normal, FontStyle.Normal).FontData.SourceUri.LocalPath);
    }

    [Fact]
    public void BundledSourceKeepsPriorityWhenAlsoDiscoveredUnderARoot()
    {
        string bundled = CopyFont(_root, "bundled.ttf");
        string child = Directory.CreateDirectory(Path.Combine(_root, "Supplemental")).FullName;
        string nested = CopyFont(child, "nested.ttf");
        var factory = new CountingFontSourceFactory();

        FontCollection collection = FontCollection.FromUris(
            Factory.EnumerateSystemFontUris(bundled, new[] { child, _root }), factory);

        Assert.Equal(new[] { new Uri(bundled).AbsoluteUri, new Uri(nested).AbsoluteUri }, factory.Sources);
        Assert.Equal(2u, collection[0].Count);
        Assert.Equal(bundled, collection[0].GetFirstMatchingFont(FontWeight.Normal, FontStretch.Normal, FontStyle.Normal).FontData.SourceUri.LocalPath);
    }

    [Fact]
    public void RepeatedSystemCollectionFileRetainsEveryOriginalFaceSlot()
    {
        string child = Directory.CreateDirectory(Path.Combine(_root, "Supplemental")).FullName;
        string path = CreateTwoFaceCollection(child);
        var factory = new CountingFontSourceFactory();

        FontCollection collection = FontCollection.FromUris(
            Factory.EnumerateSystemFontUris(Path.Combine(_root, "missing.ttf"), new[] { child, _root }), factory);

        Assert.Equal(new Uri(path).AbsoluteUri, Assert.Single(factory.Sources));
        Assert.Equal(1u, collection.FamilyCount);
        Assert.Equal(new uint[] { 0, 1 }, collection[0].Select(font => font.FontData.FaceIndex));
        Assert.All(collection[0], font =>
        {
            Assert.Equal(FontFaceType.TrueTypeCollection, font.FontData.FaceType);
            Assert.False(font.FontData.IsFontDataResident);
        });
    }

    private static string CreateTwoFaceCollection(string directory)
    {
        string path = Path.Combine(directory, "two-faces.ttc");
        byte[] original = File.ReadAllBytes(FontPath);
        // Two valid collection face slots alias the same unchanged SFNT tables.
        // Keep their separate original indexes; discovery deduplicates files only.
        const int headerSize = 20;
        byte[] collectionBytes = new byte[headerSize + original.Length];
        original.CopyTo(collectionBytes, headerSize);
        BinaryPrimitives.WriteUInt32BigEndian(collectionBytes.AsSpan(0), 0x74746366);
        BinaryPrimitives.WriteUInt32BigEndian(collectionBytes.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(collectionBytes.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32BigEndian(collectionBytes.AsSpan(12), headerSize);
        BinaryPrimitives.WriteUInt32BigEndian(collectionBytes.AsSpan(16), headerSize);
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(4));
        for (int index = 0; index < tableCount; index++)
        {
            Span<byte> offset = collectionBytes.AsSpan(headerSize + 12 + index * 16 + 8, 4);
            BinaryPrimitives.WriteUInt32BigEndian(offset, checked(BinaryPrimitives.ReadUInt32BigEndian(offset) + headerSize));
        }
        File.WriteAllBytes(path, collectionBytes);
        return path;
    }

    [Fact]
    public void ExplicitCustomCollectionRetainsRepeatedSources()
    {
        string path = CopyFont(_root, "custom.ttf");
        var uri = new Uri(path);
        var factory = new CountingFontSourceFactory();

        FontCollection collection = FontCollection.FromUris(new[] { uri, uri }, factory);

        Assert.Equal(new[] { uri.AbsoluteUri, uri.AbsoluteUri }, factory.Sources);
        Assert.Equal(2u, collection[0].Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(40)]
    public void OwnedSystemCatalogPreservesOriginalMetadataAndLazyFaceOwnership(int fileCount)
    {
        string otherFont = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "NotoSansSymbols2-Regular.ttf");
        var uris = new List<Uri>();
        for (int index = fileCount - 1; index >= 0; index--)
        {
            string path = Path.Combine(_root, $"font-{index:D2}.ttf");
            File.Copy(index % 2 == 0 ? FontPath : otherFont, path);
            uris.Add(new Uri(path));
        }
        string collectionPath = CreateTwoFaceCollection(_root);
        uris.Insert(1, new Uri(collectionPath));
        string malformed = Path.Combine(_root, "malformed.ttf");
        File.WriteAllBytes(malformed, new byte[] { 1, 2, 3 });
        uris.Insert(2, new Uri(malformed));
        uris.Add(new Uri(Path.Combine(_root, "missing.ttf")));
        var factory = new FontSourceFactory();

        FontCollection original = FontCollection.FromUris(uris, factory);
        FontCollection current = FontCollection.FromSystemUris(uris, factory);

        Assert.Equal(original.FamilyCount, current.FamilyCount);
        for (uint family = 0; family < original.FamilyCount; family++)
        {
            Assert.Equal(original[family].Count, current[family].Count);
            var expectedFaces = original[family].Select(font => font.FontData).ToArray();
            var actualFaces = current[family].Select(font => font.FontData).ToArray();
            for (int index = 0; index < expectedFaces.Length; index++)
            {
                PortableFontData expected = expectedFaces[index], actual = actualFaces[index];
                Assert.Equal(expected.SourceUri.AbsoluteUri, actual.SourceUri.AbsoluteUri);
                Assert.Equal(expected.FaceIndex, actual.FaceIndex);
                Assert.Equal(expected.FamilyName, actual.FamilyName);
                Assert.Equal(expected.FaceName, actual.FaceName);
                Assert.Equal(expected.FullName, actual.FullName);
                Assert.Equal((expected.Metrics.DesignUnitsPerEm, expected.Metrics.Ascent, expected.Metrics.Descent,
                    expected.Metrics.LineGap, expected.Metrics.CapHeight, expected.Metrics.XHeight,
                    expected.Metrics.UnderlinePosition, expected.Metrics.UnderlineThickness,
                    expected.Metrics.StrikethroughPosition, expected.Metrics.StrikethroughThickness),
                    (actual.Metrics.DesignUnitsPerEm, actual.Metrics.Ascent, actual.Metrics.Descent,
                    actual.Metrics.LineGap, actual.Metrics.CapHeight, actual.Metrics.XHeight,
                    actual.Metrics.UnderlinePosition, actual.Metrics.UnderlineThickness,
                    actual.Metrics.StrikethroughPosition, actual.Metrics.StrikethroughThickness));
                Assert.Equal(expected.Weight, actual.Weight);
                Assert.Equal(expected.Stretch, actual.Stretch);
                Assert.Equal(expected.Style, actual.Style);
                Assert.Equal(expected.FaceType, actual.FaceType);
                Assert.Equal(expected.GlyphCount, actual.GlyphCount);
                Assert.Equal(expected.IsSymbolFont, actual.IsSymbolFont);
                Assert.Equal(expected.Version, actual.Version);
                for (ushort name = 0; name <= 19; name++)
                    Assert.Equal(expected.GetNameStrings(name, name, "missing").Select(pair => (pair.Key.Name, pair.Value)).ToArray(),
                        actual.GetNameStrings(name, name, "missing").Select(pair => (pair.Key.Name, pair.Value)).ToArray());
                Assert.False(expected.IsFontDataResident);
                Assert.False(actual.IsFontDataResident);
            }
        }
        PortableFontData selected = current[0].First().FontData;
        _ = selected.GetGlyphIndex(0);
        Assert.True(selected.IsFontDataResident);
        Assert.All(current[0].Skip(1), font => Assert.False(font.FontData.IsFontDataResident));
    }

    [Fact]
    public void CustomSystemFactoryKeepsSequentialCallerCallbacksAndDuplicates()
    {
        var uri = new Uri(CopyFont(_root, "custom.ttf"));
        var factory = new CountingFontSourceFactory();
        FontCollection collection = FontCollection.FromSystemUris(Enumerable.Repeat(uri, 40), factory);
        Assert.Equal(40, factory.Sources.Count);
        Assert.All(factory.Threads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
        Assert.Equal(40u, collection[0].Count);
    }

    [Fact]
    public void CustomSystemFactoryFatalErrorKeepsOriginalExceptionAndCallbackBoundary()
    {
        var uri = new Uri(CopyFont(_root, "custom.ttf"));
        var error = new InvalidOperationException("Original source failure");
        var factory = new FailingFontSourceFactory(error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            FontCollection.FromSystemUris(Enumerable.Repeat(uri, 40), factory)));
        Assert.Equal(13, factory.Calls);
    }

    private sealed class FailingFontSourceFactory(Exception error) : IFontSourceFactory
    {
        internal int Calls { get; private set; }
        public IFontSource Create(string uriString)
        {
            if (++Calls == 13) throw error;
            return new FontSource(new Uri(uriString));
        }
    }

    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "LibreWPF.FluentSymbols.ttf");

    private static string CopyFont(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        File.Copy(FontPath, path);
        return path;
    }

    private sealed class CountingFontSourceFactory : IFontSourceFactory
    {
        internal List<string> Sources { get; } = new();
        internal List<int> Threads { get; } = new();

        public IFontSource Create(string uriString)
        {
            Sources.Add(uriString);
            Threads.Add(Environment.CurrentManagedThreadId);
            return new FontSource(new Uri(uriString));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
