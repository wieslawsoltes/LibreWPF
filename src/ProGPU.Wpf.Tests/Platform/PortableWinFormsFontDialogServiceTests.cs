using ProGPU.Wpf.Interop;
using DrawFont = System.Drawing.Font;
using DrawFontStyle = System.Drawing.FontStyle;
using DrawGraphicsUnit = System.Drawing.GraphicsUnit;
using Forms = System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsFontDialogServiceTests
{
    [Fact]
    public void FontDialogUsesPortableFontDialogService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetFontDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        PortableFontDialogRequest? capturedRequest = null;
        using var registration = service.Register(request =>
        {
            capturedRequest = request;
            return new PortableFontDialogResult("Menlo", 12.5f, (int)(DrawFontStyle.Bold | DrawFontStyle.Italic), "Point");
        });

        using var initialFont = new DrawFont("Courier New", 9.5f, DrawFontStyle.Underline, DrawGraphicsUnit.Point, 0);
        var dialog = new Forms.FontDialog
        {
            Font = initialFont,
            MinSize = 6,
            MaxSize = 30,
            ShowEffects = true
        };

        Assert.Equal(Forms.DialogResult.OK, dialog.ShowDialog());
        Assert.NotNull(dialog.Font);
        Assert.Equal("Menlo", dialog.Font.Name);
        Assert.Equal(12.5f, dialog.Font.Size);
        Assert.True(dialog.Font.Bold);
        Assert.True(dialog.Font.Italic);
        Assert.False(dialog.Font.Underline);
        Assert.NotNull(capturedRequest);
        Assert.Equal("Courier New", capturedRequest.FamilyName);
        Assert.Equal(9.5f, capturedRequest.Size);
        Assert.Equal((int)DrawFontStyle.Underline, capturedRequest.Style);
        Assert.Equal("Point", capturedRequest.Unit);
        Assert.Equal(6, capturedRequest.MinSize);
        Assert.Equal(30, capturedRequest.MaxSize);
    }

    [Fact]
    public void FontDialogCancelKeepsExistingFont()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetFontDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        using var registration = service.Register(_ => null);
        using var originalFont = new DrawFont("Courier New", 10f, DrawFontStyle.Bold);
        var dialog = new Forms.FontDialog
        {
            Font = originalFont
        };

        Assert.Equal(Forms.DialogResult.Cancel, dialog.ShowDialog());
        Assert.Same(originalFont, dialog.Font);
    }

    [Fact]
    public void DrawingFontExposesSharpDevelopFontProperties()
    {
        using var font = new DrawFont("Courier New", 11f, DrawFontStyle.Bold | DrawFontStyle.Italic | DrawFontStyle.Underline, DrawGraphicsUnit.Point, 0);

        Assert.Equal("Courier New", font.Name);
        Assert.True(font.Bold);
        Assert.True(font.Italic);
        Assert.True(font.Underline);
        Assert.False(font.Strikeout);
        Assert.Equal(DrawGraphicsUnit.Point, font.OriginalUnit);
        Assert.Equal(11f, font.SizeInPoints);
    }
}
