// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Specialized;

namespace System.Windows;

[Collection("Sequential")]
public class PortableClipboardServiceTests
{
    [Fact]
    public void ClearKeepsManagedStateAuthoritativeOverStaleNativeText()
    {
        if (!PortableClipboardService.IsEnabled)
        {
            return;
        }

        string? nativeText = "native text";
        using var registration = PortableClipboardService.Register(
            () => nativeText,
            text => nativeText = text);

        Clipboard.Clear();
        nativeText = "stale native text";

        PortableClipboardService.TryGetDataObject(out IDataObject? dataObject).Should().BeTrue();
        dataObject.Should().BeNull();
        Clipboard.ContainsText().Should().BeFalse();
        Clipboard.GetText().Should().BeEmpty();
    }

    [Fact]
    public void SetFileDropListClearsNativeTextMirror()
    {
        if (!PortableClipboardService.IsEnabled)
        {
            return;
        }

        string? nativeText = "native text";
        var writes = new List<string?>();
        using var registration = PortableClipboardService.Register(
            () => nativeText,
            text =>
            {
                writes.Add(text);
                nativeText = text;
            });
        var fileDropList = new StringCollection
        {
            "/tmp/portable-alpha.txt",
            "/tmp/portable-beta.txt"
        };

        PortableClipboardService.TrySetFileDropList(fileDropList).Should().BeTrue();

        writes.Should().ContainSingle().Which.Should().BeNull();
        Clipboard.ContainsText().Should().BeFalse();
        Clipboard.GetText().Should().BeEmpty();
        Clipboard.ContainsFileDropList().Should().BeTrue();
        var roundTripFileDropList = Clipboard.GetFileDropList();
        roundTripFileDropList.Count.Should().Be(2);
        roundTripFileDropList[0].Should().Be("/tmp/portable-alpha.txt");
        roundTripFileDropList[1].Should().Be("/tmp/portable-beta.txt");
    }
}
