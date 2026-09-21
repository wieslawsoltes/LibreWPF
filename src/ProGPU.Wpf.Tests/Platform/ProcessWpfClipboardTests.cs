using System.Diagnostics;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ProcessWpfClipboardTests
{
    [Fact]
    public void CreateStartInfosForMacOsUsesPbcopyAndPbpaste()
    {
        var setCommand = Assert.Single(ProcessWpfClipboard.CreateSetTextStartInfos(WpfClipboardPlatform.MacOS));
        var getCommand = Assert.Single(ProcessWpfClipboard.CreateGetTextStartInfos(WpfClipboardPlatform.MacOS));

        Assert.Equal("pbcopy", setCommand.FileName);
        Assert.True(setCommand.RedirectStandardInput);
        Assert.False(setCommand.UseShellExecute);
        Assert.Equal("pbpaste", getCommand.FileName);
        Assert.True(getCommand.RedirectStandardOutput);
        Assert.False(getCommand.UseShellExecute);
    }

    [Fact]
    public void CreateStartInfosForWindowsUsesPowerShellClipboardCommands()
    {
        var setCommand = Assert.Single(ProcessWpfClipboard.CreateSetTextStartInfos(WpfClipboardPlatform.Windows));
        var getCommand = Assert.Single(ProcessWpfClipboard.CreateGetTextStartInfos(WpfClipboardPlatform.Windows));

        Assert.Equal("powershell", setCommand.FileName);
        Assert.Contains("Set-Clipboard", setCommand.Arguments);
        Assert.True(setCommand.RedirectStandardInput);
        Assert.Equal("powershell", getCommand.FileName);
        Assert.Contains("Get-Clipboard", getCommand.Arguments);
        Assert.True(getCommand.RedirectStandardOutput);
    }

    [Fact]
    public void CreateStartInfosForLinuxProvidesWaylandAndX11Fallbacks()
    {
        var setCommands = ProcessWpfClipboard.CreateSetTextStartInfos(WpfClipboardPlatform.Linux);
        var getCommands = ProcessWpfClipboard.CreateGetTextStartInfos(WpfClipboardPlatform.Linux);

        Assert.Equal(new[] { "wl-copy", "xclip", "xsel" }, setCommands.Select(command => command.FileName).ToArray());
        Assert.Equal(new[] { "wl-paste", "xclip", "xsel" }, getCommands.Select(command => command.FileName).ToArray());
        Assert.All(setCommands, command => Assert.True(command.RedirectStandardInput));
        Assert.All(getCommands, command => Assert.True(command.RedirectStandardOutput));
    }

    [Fact]
    public async Task SetTextAsyncFallsBackToNextLinuxCommandWhenFirstCommandFails()
    {
        var calls = new List<(ProcessStartInfo StartInfo, string? Input)>();
        var clipboard = new ProcessWpfClipboard(
            () => WpfClipboardPlatform.Linux,
            (startInfo, standardInput, _) =>
            {
                calls.Add((startInfo, standardInput));
                return ValueTask.FromResult(startInfo.FileName == "wl-copy"
                    ? new WpfClipboardProcessResult(1, string.Empty, "missing display")
                    : new WpfClipboardProcessResult(0, string.Empty, string.Empty));
            });

        await clipboard.SetTextAsync("hello");

        Assert.Equal(new[] { "wl-copy", "xclip" }, calls.Select(call => call.StartInfo.FileName).ToArray());
        Assert.All(calls, call => Assert.Equal("hello", call.Input));
    }

    [Fact]
    public async Task GetTextAsyncReturnsOutputFromFirstSuccessfulCommand()
    {
        var calls = new List<string>();
        var clipboard = new ProcessWpfClipboard(
            () => WpfClipboardPlatform.Linux,
            (startInfo, _, _) =>
            {
                calls.Add(startInfo.FileName);
                return ValueTask.FromResult(startInfo.FileName == "wl-paste"
                    ? new WpfClipboardProcessResult(1, string.Empty, "missing display")
                    : new WpfClipboardProcessResult(0, "clipboard text", string.Empty));
            });

        var text = await clipboard.GetTextAsync();

        Assert.Equal("clipboard text", text);
        Assert.Equal(new[] { "wl-paste", "xclip" }, calls);
    }

    [Fact]
    public async Task UnsupportedPlatformThrowsPlatformNotSupported()
    {
        var clipboard = new ProcessWpfClipboard(
            () => WpfClipboardPlatform.Unsupported,
            (_, _, _) => ValueTask.FromResult(new WpfClipboardProcessResult(0, string.Empty, string.Empty)));

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await clipboard.GetTextAsync());
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await clipboard.SetTextAsync("text"));
    }
}
