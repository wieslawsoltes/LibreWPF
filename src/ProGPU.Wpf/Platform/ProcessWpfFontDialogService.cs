using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfFontDialogPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public readonly record struct WpfFontDialogProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfFontDialogProcessResult> WpfFontDialogProcessRunner(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class ProcessWpfFontDialogService : IWpfFontDialogService
{
    private readonly Func<WpfFontDialogPlatform> _platformProvider;
    private readonly WpfFontDialogProcessRunner _processRunner;

    public ProcessWpfFontDialogService()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfFontDialogService(
        Func<WpfFontDialogPlatform> platformProvider,
        WpfFontDialogProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public WpfFontDialogResult? Show(WpfFontDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = NormalizeOptions(options);
        var commands = CreateStartInfos(_platformProvider(), normalized);
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("Font dialog services are not available on this platform.");
        }

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

                return TryParseFontOutput(result.StandardOutput, normalized, out var selectedFont)
                    ? selectedFont
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

        throw new InvalidOperationException("No font dialog command could be started.", lastStartException);
    }

    public static WpfFontDialogPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfFontDialogPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfFontDialogPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfFontDialogPlatform.Linux;
        }

        return WpfFontDialogPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateStartInfos(
        WpfFontDialogPlatform platform,
        WpfFontDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options = NormalizeOptions(options);
        return platform switch
        {
            WpfFontDialogPlatform.Windows => new[] { CreateWindowsStartInfo(options) },
            WpfFontDialogPlatform.MacOS => new[] { CreateMacStartInfo(options) },
            WpfFontDialogPlatform.Linux => new[] { CreateLinuxZenityStartInfo(options), CreateLinuxKDialogStartInfo(options) },
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    public static bool TryParseFontOutput(
        string output,
        WpfFontDialogOptions defaults,
        out WpfFontDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        var trimmed = output.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            result = CreateResult(defaults);
            return false;
        }

        var parts = trimmed.Split('\t', StringSplitOptions.TrimEntries);
        if (parts.Length < 4)
        {
            parts = trimmed.Split('|', StringSplitOptions.TrimEntries);
        }

        if (parts.Length < 1 || string.IsNullOrWhiteSpace(parts[0]))
        {
            result = CreateResult(defaults);
            return false;
        }

        var familyName = parts[0].Trim();
        var size = defaults.Size;
        if (parts.Length > 1 &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize) &&
            parsedSize > 0 &&
            float.IsFinite(parsedSize))
        {
            size = parsedSize;
        }

        var style = defaults.Style;
        if (parts.Length > 2 && TryParseStyle(parts[2], out var parsedStyle))
        {
            style = parsedStyle;
        }

        var unit = defaults.Unit;
        if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
        {
            unit = NormalizeUnit(parts[3]);
        }

        result = new WpfFontDialogResult(familyName, size, style, unit);
        return true;
    }

    private static ProcessStartInfo CreateWindowsStartInfo(WpfFontDialogOptions options)
    {
        var command = "Add-Type -AssemblyName System.Windows.Forms; "
            + "Add-Type -AssemblyName System.Drawing; "
            + "$f = New-Object System.Windows.Forms.FontDialog; "
            + $"$f.ShowEffects = ${options.ShowEffects.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}; "
            + $"$f.ShowColor = ${options.ShowColor.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}; ";

        if (options.MinSize > 0)
        {
            command += $"$f.MinSize = {options.MinSize}; ";
        }

        if (options.MaxSize > 0)
        {
            command += $"$f.MaxSize = {options.MaxSize}; ";
        }

        command += $"$f.Font = New-Object System.Drawing.Font('{EscapePowerShellString(options.FamilyName)}', "
            + $"{options.Size.ToString(CultureInfo.InvariantCulture)}, "
            + $"[System.Drawing.FontStyle]{options.Style}, "
            + $"[System.Drawing.GraphicsUnit]::{NormalizeUnit(options.Unit)}); "
            + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { "
            + "[Console]::Write($f.Font.Name + [char]9 + "
            + "$f.Font.Size.ToString([System.Globalization.CultureInfo]::InvariantCulture) + [char]9 + "
            + "[int]$f.Font.Style + [char]9 + $f.Font.Unit.ToString()) }";

        return CreateStartInfo("powershell", "-NoProfile", "-NonInteractive", "-Command", command);
    }

    private static ProcessStartInfo CreateMacStartInfo(WpfFontDialogOptions options)
    {
        var familyList = CreateAppleScriptFamilyList(options.FamilyName);
        var defaultStyle = StyleNameFromFlags(options.Style);
        var script = "set fontFamilies to {" + familyList + "}\n"
            + "set chosenFamily to choose from list fontFamilies with title \"Select Font\" with prompt \"Font family\" default items {\""
            + EscapeAppleScriptString(options.FamilyName) + "\"}\n"
            + "if chosenFamily is false then error number -128\n"
            + "set chosenSize to text returned of (display dialog \"Font size\" default answer \""
            + options.Size.ToString(CultureInfo.InvariantCulture) + "\")\n"
            + "set chosenStyle to choose from list {\"Regular\", \"Bold\", \"Italic\", \"Bold Italic\"} with title \"Select Font\" with prompt \"Font style\" default items {\""
            + EscapeAppleScriptString(defaultStyle) + "\"}\n"
            + "if chosenStyle is false then error number -128\n"
            + "return (item 1 of chosenFamily) & tab & chosenSize & tab & (item 1 of chosenStyle) & tab & \"Point\"";

        return CreateStartInfo("osascript", "-e", script);
    }

    private static ProcessStartInfo CreateLinuxZenityStartInfo(WpfFontDialogOptions options)
    {
        return CreateStartInfo(
            "zenity",
            "--forms",
            "--title=Select Font",
            "--separator=\t",
            "--add-entry=Family",
            "--add-entry=Size",
            "--add-combo=Style",
            "--combo-values=Regular|Bold|Italic|Bold Italic");
    }

    private static ProcessStartInfo CreateLinuxKDialogStartInfo(WpfFontDialogOptions options)
    {
        var styleName = StyleNameFromFlags(options.Style);
        var script = "family=$(kdialog --title 'Select Font' --inputbox 'Font family' "
            + EscapeShellSingleQuotedString(options.FamilyName) + ") || exit 1; "
            + "size=$(kdialog --title 'Select Font' --inputbox 'Font size' "
            + EscapeShellSingleQuotedString(options.Size.ToString(CultureInfo.InvariantCulture)) + ") || exit 1; "
            + "style=$(kdialog --title 'Select Font' --combobox 'Font style' 'Regular' 'Bold' 'Italic' 'Bold Italic' --default "
            + EscapeShellSingleQuotedString(styleName) + ") || exit 1; "
            + "printf '%s\\t%s\\t%s\\tPoint' \"$family\" \"$size\" \"$style\"";

        return CreateStartInfo("sh", "-c", script);
    }

    private static WpfFontDialogOptions NormalizeOptions(WpfFontDialogOptions options)
    {
        return new WpfFontDialogOptions
        {
            FamilyName = string.IsNullOrWhiteSpace(options.FamilyName) ? "Courier New" : options.FamilyName,
            Size = options.Size > 0 && float.IsFinite(options.Size) ? options.Size : 10f,
            Style = options.Style,
            Unit = NormalizeUnit(options.Unit),
            ShowEffects = options.ShowEffects,
            ShowColor = options.ShowColor,
            MinSize = Math.Max(0, options.MinSize),
            MaxSize = Math.Max(0, options.MaxSize)
        };
    }

    private static WpfFontDialogResult CreateResult(WpfFontDialogOptions options)
    {
        return new WpfFontDialogResult(options.FamilyName, options.Size, options.Style, options.Unit);
    }

    private static string NormalizeUnit(string? unit)
    {
        return unit switch
        {
            "World" => "World",
            "Display" => "Display",
            "Pixel" => "Pixel",
            "Inch" => "Inch",
            "Document" => "Document",
            "Millimeter" => "Millimeter",
            _ => "Point"
        };
    }

    private static bool TryParseStyle(string value, out int style)
    {
        value = value.Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out style))
        {
            return true;
        }

        style = 0;
        foreach (var token in value.Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(token, "Regular", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(token, "Bold", StringComparison.OrdinalIgnoreCase))
            {
                style |= 1;
            }
            else if (string.Equals(token, "Italic", StringComparison.OrdinalIgnoreCase))
            {
                style |= 2;
            }
            else if (string.Equals(token, "Underline", StringComparison.OrdinalIgnoreCase))
            {
                style |= 4;
            }
            else if (string.Equals(token, "Strikeout", StringComparison.OrdinalIgnoreCase))
            {
                style |= 8;
            }
        }

        return true;
    }

    private static string StyleNameFromFlags(int style)
    {
        var bold = (style & 1) != 0;
        var italic = (style & 2) != 0;
        return (bold, italic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular"
        };
    }

    private static string CreateAppleScriptFamilyList(string initialFamilyName)
    {
        var families = new[]
        {
            initialFamilyName,
            "Menlo",
            "Monaco",
            "Courier New",
            "Consolas",
            "Helvetica",
            "Arial",
            "Times New Roman",
            "Verdana"
        }
        .Where(static family => !string.IsNullOrWhiteSpace(family))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(family => "\"" + EscapeAppleScriptString(family) + "\"");

        return string.Join(", ", families);
    }

    private static string EscapePowerShellString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string EscapeShellSingleQuotedString(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
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

    private static async ValueTask<WpfFontDialogProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start font dialog command '{startInfo.FileName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfFontDialogProcessResult(process.ExitCode, output, error);
    }
}
