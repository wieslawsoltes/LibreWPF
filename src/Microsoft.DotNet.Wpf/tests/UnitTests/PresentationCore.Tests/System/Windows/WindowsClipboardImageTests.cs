// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.ShowcaseApp;
using System.Windows.Media.Imaging;

namespace System.Windows;

// The clipboard is process-external state. These tests run only on Windows,
// sequentially, with the selected media backend frozen by the existing startup.
[Collection("Sequential")]
public sealed partial class WindowsClipboardImageTests
{
    private sealed class WindowsWpfFactAttribute : WpfFactAttribute
    {
        public WindowsWpfFactAttribute(
            [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires actual Windows OLE/GDI clipboard transport.";
        }
    }

    [WindowsWpfFact]
    public void OpaqueImageOutlivesFlushedOleMediumAndCanBeRepublished() => VerifyRoundTrip(indexed: false);

    [WindowsWpfFact]
    public void PartialPaletteImageUsesRealGdiColorConversion() => VerifyRoundTrip(indexed: true);

    private static void VerifyRoundTrip(bool indexed)
    {
        BitmapSource image = ShowcaseClipboardImage.ExerciseLifetime(indexed);
        ShowcaseClipboardImage.Verify(image);
        Assert.Equal(BitmapSource.UsesPortablePixelStorage, image._managedPixelBuffer is not null);
    }
}
