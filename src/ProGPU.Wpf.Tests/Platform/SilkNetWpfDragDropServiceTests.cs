using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetWpfDragDropServiceTests
{
    [Fact]
    public void CreateFileDropEventStoresDroppedFiles()
    {
        var args = SilkNetWpfDragDropService.CreateFileDropEvent(new[] { "/tmp/a.txt", "/tmp/b.txt" });

        Assert.Equal(WpfDragDropEventKind.Drop, args.Kind);
        Assert.Equal(WpfDragDropEffects.Copy, args.AllowedEffects);
        Assert.Equal(WpfDragDropEffects.Copy, args.AcceptedEffect);
        Assert.True(args.Data.ContainsFiles);
        Assert.Equal(new[] { "/tmp/a.txt", "/tmp/b.txt" }, args.Data.Files);
    }

    [Fact]
    public void CreateFileDropEventHandlesNullFileList()
    {
        var args = SilkNetWpfDragDropService.CreateFileDropEvent(null);

        Assert.Equal(WpfDragDropEventKind.Drop, args.Kind);
        Assert.False(args.Data.ContainsFiles);
        Assert.Empty(args.Data.Files);
    }

    [Fact]
    public void AttachRejectsNonSilkWindow()
    {
        var service = new SilkNetWpfDragDropService();

        Assert.Throws<ArgumentException>(() => service.Attach(new object()));
    }
}
