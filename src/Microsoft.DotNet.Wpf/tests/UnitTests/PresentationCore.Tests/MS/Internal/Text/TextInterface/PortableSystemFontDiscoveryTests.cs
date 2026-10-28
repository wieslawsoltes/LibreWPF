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
        string path = Path.Combine(child, "two-faces.ttc");
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
