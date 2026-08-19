// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace MS.Internal.Text.TextInterface.Tests;

public sealed class PortableFontCollectionTests
{
    [Fact]
    public void ArialAliasResolvesPortableSansFamilyWhenArialIsMissing()
    {
        LocalizedStrings names = new()
        {
            [CultureInfo.GetCultureInfo("en-US")] = "DejaVu Sans"
        };
        FontFamily portableSans = new("DejaVu Sans", names, Array.Empty<Font>());
        FontCollection collection = new(new[] { portableSans });

        Assert.True(collection.FindFamilyName("Arial", out uint index));
        Assert.Same(portableSans, collection[index]);
    }

    [Fact]
    public void ArialAliasPrefersActualArialWhenInstalled()
    {
        FontFamily arial = CreateFamily("Arial");
        FontFamily portableSans = CreateFamily("DejaVu Sans");
        FontCollection collection = new(new[] { arial, portableSans });

        Assert.True(collection.FindFamilyName("Arial", out uint index));
        Assert.Same(arial, collection[index]);
    }

    [Fact]
    public void ComicSansAliasResolvesPortableScriptFamilyWhenComicSansIsMissing()
    {
        FontFamily portableScript = CreateFamily("Z003");
        FontFamily portableSans = CreateFamily("DejaVu Sans");
        FontCollection collection = new(new[] { portableSans, portableScript });

        Assert.True(collection.FindFamilyName("Comic Sans MS", out uint index));
        Assert.Same(portableScript, collection[index]);
    }

    private static FontFamily CreateFamily(string name)
    {
        LocalizedStrings names = new()
        {
            [CultureInfo.GetCultureInfo("en-US")] = name
        };
        return new FontFamily(name, names, Array.Empty<Font>());
    }
}
