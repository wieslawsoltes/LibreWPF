using System.Windows.Media.ProGPU.Platform;
using Silk.NET.Input;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfCursorServiceTests
{
    [Theory]
    [InlineData(WpfCursor.Default, StandardCursor.Default)]
    [InlineData(WpfCursor.Arrow, StandardCursor.Arrow)]
    [InlineData(WpfCursor.IBeam, StandardCursor.IBeam)]
    [InlineData(WpfCursor.Crosshair, StandardCursor.Crosshair)]
    [InlineData(WpfCursor.Hand, StandardCursor.Hand)]
    [InlineData(WpfCursor.SizeWE, StandardCursor.HResize)]
    [InlineData(WpfCursor.SizeNS, StandardCursor.VResize)]
    [InlineData(WpfCursor.SizeNWSE, StandardCursor.NwseResize)]
    [InlineData(WpfCursor.SizeNESW, StandardCursor.NeswResize)]
    [InlineData(WpfCursor.SizeAll, StandardCursor.ResizeAll)]
    [InlineData(WpfCursor.No, StandardCursor.NotAllowed)]
    [InlineData(WpfCursor.Wait, StandardCursor.Wait)]
    [InlineData(WpfCursor.AppStarting, StandardCursor.WaitArrow)]
    public void TranslateCursorMapsWpfCursorToSilkCursor(WpfCursor cursor, StandardCursor expected)
    {
        Assert.Equal(expected, SilkNetWpfCursorService.TranslateCursor(cursor));
    }

    [Fact]
    public void SetCursorRejectsUnsupportedInputSource()
    {
        var service = new SilkNetWpfCursorService();

        Assert.Throws<ArgumentException>(() => service.SetCursor(new object(), WpfCursor.Arrow));
    }
}
