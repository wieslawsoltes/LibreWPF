// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal.FontCache;
using MS.Internal.Text.TextInterface;

namespace PresentationCore.Tests.MS.Internal.Text.TextInterface;

public class PortableFontDataTests
{
    [Fact]
    public void CatalogFacesReleaseFileBytesUntilGlyphDataIsUsed()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "LibreWPF.FluentSymbols.ttf");
        var uri = new Uri(path);
        var source = new FontSource(uri);

        IReadOnlyList<PortableFontData> faces = PortableFontData.LoadFaces(uri, source);

        PortableFontData face = Assert.Single(faces);
        Assert.False(face.IsFontDataResident);

        _ = face.GetGlyphIndex(0);

        Assert.True(face.IsFontDataResident);
    }

    [Fact]
    public void ExplicitFaceKeepsFileBytesForImmediateUse()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "LibreWPF.FluentSymbols.ttf");
        var uri = new Uri(path);
        var source = new FontSource(uri);

        PortableFontData face = PortableFontData.LoadFace(uri, source, faceIndex: 0);

        Assert.True(face.IsFontDataResident);
        _ = face.GetGlyphIndex(0);
    }

    [Fact]
    public void StreamedCatalogMetadataMatchesExplicitFaceMetadata()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "LibreWPF", "Fonts", "LibreWPF.FluentSymbols.ttf");
        var uri = new Uri(path);
        var source = new FontSource(uri);

        PortableFontData catalogFace = Assert.Single(PortableFontData.LoadFaces(uri, source));
        PortableFontData explicitFace = PortableFontData.LoadFace(uri, source, faceIndex: 0);

        Assert.Equal(explicitFace.FamilyName, catalogFace.FamilyName);
        Assert.Equal(explicitFace.FaceName, catalogFace.FaceName);
        Assert.Equal(explicitFace.FullName, catalogFace.FullName);
        Assert.Equal(explicitFace.Metrics.DesignUnitsPerEm, catalogFace.Metrics.DesignUnitsPerEm);
        Assert.Equal(explicitFace.Metrics.Ascent, catalogFace.Metrics.Ascent);
        Assert.Equal(explicitFace.Metrics.Descent, catalogFace.Metrics.Descent);
        Assert.Equal(explicitFace.Metrics.LineGap, catalogFace.Metrics.LineGap);
        Assert.Equal(explicitFace.Weight, catalogFace.Weight);
        Assert.Equal(explicitFace.Stretch, catalogFace.Stretch);
        Assert.Equal(explicitFace.Style, catalogFace.Style);
        Assert.Equal(explicitFace.FaceType, catalogFace.FaceType);
        Assert.Equal(explicitFace.GlyphCount, catalogFace.GlyphCount);
        Assert.Equal(explicitFace.IsSymbolFont, catalogFace.IsSymbolFont);
        Assert.Equal(explicitFace.Version, catalogFace.Version);
    }
}
