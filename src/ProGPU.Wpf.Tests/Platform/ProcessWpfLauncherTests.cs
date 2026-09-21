using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ProcessWpfLauncherTests
{
    [Fact]
    public void CreateStartInfoForUriUsesShellExecute()
    {
        var uri = new Uri("https://example.com/path?q=1");

        var startInfo = ProcessWpfLauncher.CreateStartInfoForUri(uri);

        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("https://example.com/path?q=1", startInfo.FileName);
    }

    [Fact]
    public void CreateStartInfoForFileUsesFullPathAndShellExecute()
    {
        var startInfo = ProcessWpfLauncher.CreateStartInfoForFile("relative-file.txt");

        Assert.True(startInfo.UseShellExecute);
        Assert.EndsWith("relative-file.txt", startInfo.FileName);
        Assert.True(Path.IsPathFullyQualified(startInfo.FileName));
    }

    [Fact]
    public void CrossPlatformPlatformServicesProvidesProcessLauncher()
    {
        var services = new CrossPlatformWpfPlatformServices();

        Assert.IsType<ProcessWpfClipboard>(services.Clipboard);
        Assert.IsType<SilkNetWpfCursorService>(services.Cursors);
        Assert.IsType<QueuedWpfDispatcherService>(services.Dispatcher);
        Assert.IsType<SilkNetWpfDragDropService>(services.DragDrop);
        Assert.IsType<ProcessWpfFileDialogService>(services.FileDialogs);
        Assert.IsType<ProcessWpfLauncher>(services.Launcher);
        Assert.IsType<ProcessWpfMessageBoxService>(services.MessageBoxes);
        Assert.IsType<SilkNetWpfMonitorService>(services.Monitors);
        Assert.IsType<ThreadPoolWpfTimerService>(services.Timers);
    }
}
