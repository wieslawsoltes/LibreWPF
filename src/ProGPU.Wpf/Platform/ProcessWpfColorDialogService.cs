using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfColorDialogPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public readonly record struct WpfColorDialogProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfColorDialogProcessResult> WpfColorDialogProcessRunner(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class ProcessWpfColorDialogService : IWpfColorDialogService
{
    private readonly Func<WpfColorDialogPlatform> _platformProvider;
    private readonly WpfColorDialogProcessRunner _processRunner;

    public ProcessWpfColorDialogService()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfColorDialogService(
        Func<WpfColorDialogPlatform> platformProvider,
        WpfColorDialogProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public int? Show(WpfColorDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var commands = CreateStartInfos(_platformProvider(), options);
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("Color dialog services are not available on this platform.");
        }

        var alpha = NormalizeAlpha(GetAlpha(options.InitialArgb));
        Exception? lastStartException = null;
        foreach (var command in commands)
        {
            try
            {
                var result = _processRunner(command, CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (result.ExitCode != 0)
                {
                    return null;
                }

                return TryParseColorOutput(result.StandardOutput, alpha, out var argb)
                    ? argb
                    : null;
            }
            catch (Win32Exception exception)
            {
                lastStartException = exception;
            }
            catch (InvalidOperationException exception)
            {
                lastStartException = exception;
            }
        }

        throw new InvalidOperationException("No color dialog command could be started.", lastStartException);
    }

    public static WpfColorDialogPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfColorDialogPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfColorDialogPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfColorDialogPlatform.Linux;
        }

        return WpfColorDialogPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateStartInfos(
        WpfColorDialogPlatform platform,
        WpfColorDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return platform switch
        {
            WpfColorDialogPlatform.Windows => new[] { CreateWindowsStartInfo(options) },
            WpfColorDialogPlatform.MacOS => new[] { CreateMacStartInfo(options) },
            WpfColorDialogPlatform.Linux => new[] { CreateLinuxZenityStartInfo(options), CreateLinuxKDialogStartInfo(options) },
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    public static bool TryParseColorOutput(string output, byte defaultAlpha, out int argb)
    {
        output = output.Trim();
        if (string.IsNullOrEmpty(output))
        {
            argb = 0;
            return false;
        }

        if (int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signedArgb))
        {
            argb = signedArgb;
            return true;
        }

        if (TryParseHexColor(output, defaultAlpha, out argb))
        {
            return true;
        }

        if (TryParseRgbFunction(output, defaultAlpha, out argb))
        {
            return true;
        }

        if (TryParseCommaColor(output, defaultAlpha, out argb))
        {
            return true;
        }

        argb = 0;
        return false;
    }

    private static ProcessStartInfo CreateWindowsStartInfo(WpfColorDialogOptions options)
    {
        var alpha = GetAlpha(options.InitialArgb);
        var command = "Add-Type -AssemblyName System.Windows.Forms; "
            + "Add-Type -AssemblyName System.Drawing; "
            + "$f = New-Object System.Windows.Forms.ColorDialog; "
            + "$f.FullOpen = $true; "
            + $"$f.Color = [System.Drawing.Color]::FromArgb({alpha}, {GetRed(options.InitialArgb)}, {GetGreen(options.InitialArgb)}, {GetBlue(options.InitialArgb)}); ";

        var customColors = CreateWindowsCustomColors(options.CustomColors);
        if (!string.IsNullOrEmpty(customColors))
        {
            command += $"$f.CustomColors = @({customColors}); ";
        }

        command += "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.Color.ToArgb()) }";
        return CreateStartInfo("powershell", "-NoProfile", "-NonInteractive", "-Command", command);
    }

    private static ProcessStartInfo CreateMacStartInfo(WpfColorDialogOptions options)
    {
        var red = GetRed(options.InitialArgb) * 257;
        var green = GetGreen(options.InitialArgb) * 257;
        var blue = GetBlue(options.InitialArgb) * 257;
        var chooseScript = $"set c to choose color default color {{{red}, {green}, {blue}}}";
        var returnScript = "return ((item 1 of c) div 257) & \",\" & ((item 2 of c) div 257) & \",\" & ((item 3 of c) div 257)";

        return CreateStartInfo("osascript", "-e", chooseScript, "-e", returnScript);
    }

    private static ProcessStartInfo CreateLinuxZenityStartInfo(WpfColorDialogOptions options)
    {
        return CreateStartInfo(
            "zenity",
            "--color-selection",
            "--show-palette",
            "--title=Select Color",
            $"--color={CreateHexRgb(options.InitialArgb)}");
    }

    private static ProcessStartInfo CreateLinuxKDialogStartInfo(WpfColorDialogOptions options)
    {
        return CreateStartInfo(
            "kdialog",
            "--title",
            "Select Color",
            "--getcolor",
            CreateHexRgb(options.InitialArgb));
    }

    private static string CreateWindowsCustomColors(IReadOnlyList<int>? customColors)
    {
        if (customColors == null || customColors.Count == 0)
        {
            return string.Empty;
        }

        var values = new string[customColors.Count];
        for (var i = 0; i < customColors.Count; i++)
        {
            values[i] = customColors[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(",", values);
    }

    private static bool TryParseHexColor(string output, byte defaultAlpha, out int argb)
    {
        if (!output.StartsWith('#'))
        {
            argb = 0;
            return false;
        }

        var hex = output[1..];
        if (hex.Length != 6 && hex.Length != 8)
        {
            argb = 0;
            return false;
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            argb = 0;
            return false;
        }

        if (hex.Length == 6)
        {
            value |= (uint)defaultAlpha << 24;
        }

        argb = unchecked((int)value);
        return true;
    }

    private static bool TryParseRgbFunction(string output, byte defaultAlpha, out int argb)
    {
        if (!output.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            argb = 0;
            return false;
        }

        var openIndex = output.IndexOf('(');
        var closeIndex = output.LastIndexOf(')');
        if (openIndex < 0 || closeIndex <= openIndex)
        {
            argb = 0;
            return false;
        }

        return TryParseCommaColor(output.Substring(openIndex + 1, closeIndex - openIndex - 1), defaultAlpha, out argb);
    }

    private static bool TryParseCommaColor(string output, byte defaultAlpha, out int argb)
    {
        var parts = output.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            argb = 0;
            return false;
        }

        if (!TryParseByte(parts[0], out var red) ||
            !TryParseByte(parts[1], out var green) ||
            !TryParseByte(parts[2], out var blue))
        {
            argb = 0;
            return false;
        }

        var alpha = defaultAlpha;
        if (parts.Length >= 4 && TryParseAlpha(parts[3], out var parsedAlpha))
        {
            alpha = parsedAlpha;
        }

        argb = CreateArgb(alpha, red, green, blue);
        return true;
    }

    private static bool TryParseByte(string value, out byte result)
    {
        if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var normalized) &&
            normalized >= 0 &&
            normalized <= 1)
        {
            result = (byte)Math.Round(normalized * byte.MaxValue);
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryParseAlpha(string value, out byte result)
    {
        return TryParseByte(value, out result);
    }

    private static int CreateArgb(byte alpha, byte red, byte green, byte blue)
    {
        return unchecked((int)(((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue));
    }

    private static byte GetAlpha(int argb)
    {
        return (byte)((uint)argb >> 24);
    }

    private static byte NormalizeAlpha(byte alpha)
    {
        return alpha == 0 ? byte.MaxValue : alpha;
    }

    private static byte GetRed(int argb)
    {
        return (byte)((uint)argb >> 16);
    }

    private static byte GetGreen(int argb)
    {
        return (byte)((uint)argb >> 8);
    }

    private static byte GetBlue(int argb)
    {
        return (byte)argb;
    }

    private static string CreateHexRgb(int argb)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{GetRed(argb):X2}{GetGreen(argb):X2}{GetBlue(argb):X2}");
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async ValueTask<WpfColorDialogProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start color dialog command '{startInfo.FileName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfColorDialogProcessResult(process.ExitCode, output, error);
    }
}
