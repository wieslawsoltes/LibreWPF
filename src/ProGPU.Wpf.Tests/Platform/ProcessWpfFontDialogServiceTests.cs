using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media.ProGPU.Platform;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class ProcessWpfFontDialogServiceTests
{
    [Fact]
    public void CreateWindowsStartInfoUsesPowerShellFormsFontDialog()
    {
        var startInfo = Assert.Single(ProcessWpfFontDialogService.CreateStartInfos(
            WpfFontDialogPlatform.Windows,
            new WpfFontDialogOptions
            {
                FamilyName = "Courier New",
                Size = 9.5f,
                Style = 3,
                MinSize = 6,
                MaxSize = 30
            }));

        Assert.Equal("powershell", startInfo.FileName);
        Assert.Contains("-NoProfile", startInfo.ArgumentList);
        var command = startInfo.ArgumentList[^1];
        Assert.Contains("System.Windows.Forms.FontDialog", command);
        Assert.Contains("New-Object System.Drawing.Font('Courier New', 9.5", command);
        Assert.Contains("[System.Drawing.FontStyle]3", command);
        Assert.Contains("$f.MinSize = 6", command);
        Assert.Contains("$f.MaxSize = 30", command);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateMacStartInfoUsesOsascriptFontPickers()
    {
        var startInfo = Assert.Single(ProcessWpfFontDialogService.CreateStartInfos(
            WpfFontDialogPlatform.MacOS,
            new WpfFontDialogOptions
            {
                FamilyName = "Menlo",
                Size = 12,
                Style = 1
            }));

        Assert.Equal("osascript", startInfo.FileName);
        Assert.Equal(new[] { "-e" }, startInfo.ArgumentList.Take(1).ToArray());
        var script = startInfo.ArgumentList[1];
        Assert.Contains("choose from list fontFamilies", script);
        Assert.Contains("Font size", script);
        Assert.Contains("Bold Italic", script);
        Assert.Contains("Menlo", script);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void CreateLinuxStartInfosUseZenityAndKDialogFallbacks()
    {
        var startInfos = ProcessWpfFontDialogService.CreateStartInfos(
            WpfFontDialogPlatform.Linux,
            new WpfFontDialogOptions
            {
                FamilyName = "Courier New",
                Size = 9.5f,
                Style = 2
            });

        Assert.Equal(2, startInfos.Count);
        Assert.Equal("zenity", startInfos[0].FileName);
        Assert.Contains("--forms", startInfos[0].ArgumentList);
        Assert.Contains("--add-entry=Family", startInfos[0].ArgumentList);
        Assert.Contains("--add-combo=Style", startInfos[0].ArgumentList);
        Assert.False(startInfos[0].UseShellExecute);

        Assert.Equal("sh", startInfos[1].FileName);
        Assert.Contains("-c", startInfos[1].ArgumentList);
        Assert.Contains("kdialog", startInfos[1].ArgumentList[^1]);
        Assert.Contains("Courier New", startInfos[1].ArgumentList[^1]);
        Assert.False(startInfos[1].UseShellExecute);
    }

    [Theory]
    [InlineData("Menlo\t12.5\t3\tPoint", "Menlo", 12.5f, 3, "Point")]
    [InlineData("Courier New|9.5|Bold Italic|Point", "Courier New", 9.5f, 3, "Point")]
    [InlineData("Arial\t10\tUnderline Strikeout\tPixel", "Arial", 10f, 12, "Pixel")]
    public void TryParseFontOutputAcceptsNativeFormats(
        string output,
        string expectedFamily,
        float expectedSize,
        int expectedStyle,
        string expectedUnit)
    {
        Assert.True(ProcessWpfFontDialogService.TryParseFontOutput(
            output,
            new WpfFontDialogOptions(),
            out var result));

        Assert.Equal(expectedFamily, result.FamilyName);
        Assert.Equal(expectedSize, result.Size);
        Assert.Equal(expectedStyle, result.Style);
        Assert.Equal(expectedUnit, result.Unit);
    }

    [Fact]
    public void ShowReturnsParsedFont()
    {
        var service = new ProcessWpfFontDialogService(
            () => WpfFontDialogPlatform.MacOS,
            (_, _) => ValueTask.FromResult(new WpfFontDialogProcessResult(0, "Menlo\t12.5\tBold Italic\tPoint\n", string.Empty)));

        var result = service.Show(new WpfFontDialogOptions { FamilyName = "Courier New", Size = 9.5f });

        Assert.NotNull(result);
        Assert.Equal("Menlo", result.FamilyName);
        Assert.Equal(12.5f, result.Size);
        Assert.Equal(3, result.Style);
        Assert.Equal("Point", result.Unit);
    }

    [Fact]
    public void ShowFallsBackToKDialogWhenZenityCannotStart()
    {
        var calls = new List<string>();
        var service = new ProcessWpfFontDialogService(
            () => WpfFontDialogPlatform.Linux,
            (startInfo, _) =>
            {
                calls.Add(startInfo.FileName);
                if (startInfo.FileName == "zenity")
                {
                    throw new Win32Exception(2);
                }

                return ValueTask.FromResult(new WpfFontDialogProcessResult(0, "Menlo\t12\t1\tPoint", string.Empty));
            });

        var result = service.Show(new WpfFontDialogOptions());

        Assert.NotNull(result);
        Assert.Equal("Menlo", result.FamilyName);
        Assert.Equal(new[] { "zenity", "sh" }, calls);
    }

    [Fact]
    public void ShowReturnsNullWhenDialogIsCancelled()
    {
        var service = new ProcessWpfFontDialogService(
            () => WpfFontDialogPlatform.MacOS,
            (_, _) => ValueTask.FromResult(new WpfFontDialogProcessResult(1, string.Empty, "cancelled")));

        Assert.Null(service.Show(new WpfFontDialogOptions()));
    }

    [Fact]
    public void UnsupportedPlatformThrowsPlatformNotSupported()
    {
        var service = new ProcessWpfFontDialogService(
            () => WpfFontDialogPlatform.Unsupported,
            (_, _) => ValueTask.FromResult(new WpfFontDialogProcessResult(0, string.Empty, string.Empty)));

        Assert.Throws<PlatformNotSupportedException>(() => service.Show(new WpfFontDialogOptions()));
    }
}
