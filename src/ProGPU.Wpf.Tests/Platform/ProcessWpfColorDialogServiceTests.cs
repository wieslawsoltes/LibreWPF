using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ProcessWpfColorDialogServiceTests
{
    [Fact]
    public void CreateMacStartInfoUsesOsascriptChooseColor()
    {
        var startInfo = Assert.Single(ProcessWpfColorDialogService.CreateStartInfos(
            WpfColorDialogPlatform.MacOS,
            new WpfColorDialogOptions { InitialArgb = unchecked((int)0xFF0C2238) }));

        Assert.Equal("osascript", startInfo.FileName);
        Assert.Equal(new[] { "-e" }, startInfo.ArgumentList.Take(1).ToArray());
        Assert.Contains("choose color", startInfo.ArgumentList[1]);
        Assert.Contains("{3084, 8738, 14392}", startInfo.ArgumentList[1]);
        Assert.Contains("item 1 of c", startInfo.ArgumentList[3]);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateWindowsStartInfoUsesPowerShellFormsColorDialog()
    {
        var startInfo = Assert.Single(ProcessWpfColorDialogService.CreateStartInfos(
            WpfColorDialogPlatform.Windows,
            new WpfColorDialogOptions
            {
                InitialArgb = unchecked((int)0x80445566),
                CustomColors = new[] { unchecked((int)0xFF112233), unchecked((int)0xFF445566) }
            }));

        Assert.Equal("powershell", startInfo.FileName);
        Assert.Contains("-NoProfile", startInfo.ArgumentList);
        var command = startInfo.ArgumentList[^1];
        Assert.Contains("System.Windows.Forms.ColorDialog", command);
        Assert.Contains("FromArgb(128, 68, 85, 102)", command);
        Assert.Contains("$f.CustomColors", command);
        Assert.Contains(unchecked((int)0xFF112233).ToString(), command);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateLinuxStartInfosUseZenityAndKDialogColorPickers()
    {
        var startInfos = ProcessWpfColorDialogService.CreateStartInfos(
            WpfColorDialogPlatform.Linux,
            new WpfColorDialogOptions { InitialArgb = unchecked((int)0xFF0C2238) });

        Assert.Equal(2, startInfos.Count);
        Assert.Equal("zenity", startInfos[0].FileName);
        Assert.Contains("--color-selection", startInfos[0].ArgumentList);
        Assert.Contains("--show-palette", startInfos[0].ArgumentList);
        Assert.Contains("--color=#0C2238", startInfos[0].ArgumentList);
        Assert.False(startInfos[0].UseShellExecute);

        Assert.Equal("kdialog", startInfos[1].FileName);
        Assert.Contains("--getcolor", startInfos[1].ArgumentList);
        Assert.Contains("#0C2238", startInfos[1].ArgumentList);
        Assert.False(startInfos[1].UseShellExecute);
    }

    [Theory]
    [InlineData("#0C2238", unchecked((int)0xFF0C2238))]
    [InlineData("#800C2238", unchecked((int)0x800C2238))]
    [InlineData("rgb(12, 34, 56)", unchecked((int)0xFF0C2238))]
    [InlineData("12,34,56", unchecked((int)0xFF0C2238))]
    public void TryParseColorOutputAcceptsNativeFormats(string output, int expectedArgb)
    {
        Assert.True(ProcessWpfColorDialogService.TryParseColorOutput(output, 255, out var argb));
        Assert.Equal(expectedArgb, argb);
    }

    [Fact]
    public void ShowReturnsParsedColor()
    {
        var service = new ProcessWpfColorDialogService(
            () => WpfColorDialogPlatform.MacOS,
            (_, _) => ValueTask.FromResult(new WpfColorDialogProcessResult(0, "12,34,56\n", string.Empty)));

        var selectedColor = service.Show(new WpfColorDialogOptions { InitialArgb = unchecked((int)0x40000000) });

        Assert.Equal(unchecked((int)0x400C2238), selectedColor);
    }

    [Fact]
    public void ShowFallsBackToKDialogWhenZenityCannotStart()
    {
        var calls = new List<string>();
        var service = new ProcessWpfColorDialogService(
            () => WpfColorDialogPlatform.Linux,
            (startInfo, _) =>
            {
                calls.Add(startInfo.FileName);
                if (startInfo.FileName == "zenity")
                {
                    throw new Win32Exception(2);
                }

                return ValueTask.FromResult(new WpfColorDialogProcessResult(0, "#0C2238", string.Empty));
            });

        var selectedColor = service.Show(new WpfColorDialogOptions());

        Assert.Equal(unchecked((int)0xFF0C2238), selectedColor);
        Assert.Equal(new[] { "zenity", "kdialog" }, calls);
    }

    [Fact]
    public void ShowReturnsNullWhenDialogIsCancelled()
    {
        var service = new ProcessWpfColorDialogService(
            () => WpfColorDialogPlatform.MacOS,
            (_, _) => ValueTask.FromResult(new WpfColorDialogProcessResult(1, string.Empty, "cancelled")));

        Assert.Null(service.Show(new WpfColorDialogOptions()));
    }

    [Fact]
    public void UnsupportedPlatformThrowsPlatformNotSupported()
    {
        var service = new ProcessWpfColorDialogService(
            () => WpfColorDialogPlatform.Unsupported,
            (_, _) => ValueTask.FromResult(new WpfColorDialogProcessResult(0, string.Empty, string.Empty)));

        Assert.Throws<PlatformNotSupportedException>(() => service.Show(new WpfColorDialogOptions()));
    }
}
