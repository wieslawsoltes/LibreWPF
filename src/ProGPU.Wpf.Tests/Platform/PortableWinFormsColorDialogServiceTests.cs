using ProGPU.Wpf.Interop;
using DrawColor = System.Drawing.Color;
using Forms = System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsColorDialogServiceTests
{
    [Fact]
    public void ColorDialogUsesPortableColorDialogService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetColorDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        PortableColorDialogRequest? capturedRequest = null;
        using var registration = service.Register(request =>
        {
            capturedRequest = request;
            return DrawColor.FromArgb(255, 12, 34, 56).ToArgb();
        });

        var dialog = new Forms.ColorDialog
        {
            Color = DrawColor.FromArgb(255, 1, 2, 3),
            CustomColors = new[] { DrawColor.Red.ToArgb(), DrawColor.Blue.ToArgb() }
        };

        Assert.Equal(Forms.DialogResult.OK, dialog.ShowDialog());
        Assert.Equal(DrawColor.FromArgb(255, 12, 34, 56).ToArgb(), dialog.Color.ToArgb());
        Assert.NotNull(capturedRequest);
        Assert.Equal(DrawColor.FromArgb(255, 1, 2, 3).ToArgb(), capturedRequest.InitialArgb);
        Assert.Equal(new[] { DrawColor.Red.ToArgb(), DrawColor.Blue.ToArgb() }, capturedRequest.CustomColors);
    }

    [Fact]
    public void ColorDialogCancelKeepsExistingColor()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetColorDialogService(
            PortableWpfServiceKey.WinForms,
            out var service));

        using var registration = service.Register(_ => null);
        var originalColor = DrawColor.FromArgb(255, 64, 80, 96);
        var dialog = new Forms.ColorDialog
        {
            Color = originalColor
        };

        Assert.Equal(Forms.DialogResult.Cancel, dialog.ShowDialog());
        Assert.Equal(originalColor.ToArgb(), dialog.Color.ToArgb());
    }
}
