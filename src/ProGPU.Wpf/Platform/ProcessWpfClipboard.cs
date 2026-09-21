using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfClipboardPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public readonly record struct WpfClipboardProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfClipboardProcessResult> WpfClipboardProcessRunner(
    ProcessStartInfo startInfo,
    string? standardInput,
    CancellationToken cancellationToken);

public sealed class ProcessWpfClipboard : IWpfClipboard
{
    private readonly Func<WpfClipboardPlatform> _platformProvider;
    private readonly WpfClipboardProcessRunner _processRunner;

    public ProcessWpfClipboard()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfClipboard(
        Func<WpfClipboardPlatform> platformProvider,
        WpfClipboardProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        var commands = CreateGetTextStartInfos(_platformProvider());
        var result = await RunFirstSuccessfulCommandAsync(commands, standardInput: null, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput;
    }

    public async ValueTask SetTextAsync(string? text, CancellationToken cancellationToken = default)
    {
        var commands = CreateSetTextStartInfos(_platformProvider());
        await RunFirstSuccessfulCommandAsync(commands, text ?? string.Empty, cancellationToken).ConfigureAwait(false);
    }

    public static WpfClipboardPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfClipboardPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfClipboardPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfClipboardPlatform.Linux;
        }

        return WpfClipboardPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateSetTextStartInfos(WpfClipboardPlatform platform)
    {
        return platform switch
        {
            WpfClipboardPlatform.Windows => new[]
            {
                CreateStartInfo("powershell", "-NoProfile -NonInteractive -Command \"Set-Clipboard -Value ([Console]::In.ReadToEnd())\"", redirectInput: true)
            },
            WpfClipboardPlatform.MacOS => new[]
            {
                CreateStartInfo("pbcopy", redirectInput: true)
            },
            WpfClipboardPlatform.Linux => new[]
            {
                CreateStartInfo("wl-copy", redirectInput: true),
                CreateStartInfo("xclip", "-selection clipboard", redirectInput: true),
                CreateStartInfo("xsel", "--clipboard --input", redirectInput: true)
            },
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    public static IReadOnlyList<ProcessStartInfo> CreateGetTextStartInfos(WpfClipboardPlatform platform)
    {
        return platform switch
        {
            WpfClipboardPlatform.Windows => new[]
            {
                CreateStartInfo("powershell", "-NoProfile -NonInteractive -Command \"Get-Clipboard -Raw\"", redirectOutput: true)
            },
            WpfClipboardPlatform.MacOS => new[]
            {
                CreateStartInfo("pbpaste", redirectOutput: true)
            },
            WpfClipboardPlatform.Linux => new[]
            {
                CreateStartInfo("wl-paste", "--no-newline", redirectOutput: true),
                CreateStartInfo("xclip", "-selection clipboard -out", redirectOutput: true),
                CreateStartInfo("xsel", "--clipboard --output", redirectOutput: true)
            },
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    private static ProcessStartInfo CreateStartInfo(
        string fileName,
        string? arguments = null,
        bool redirectInput = false,
        bool redirectOutput = false)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    private async ValueTask<WpfClipboardProcessResult> RunFirstSuccessfulCommandAsync(
        IReadOnlyList<ProcessStartInfo> commands,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("Clipboard services are not available on this platform.");
        }

        Exception? lastException = null;
        WpfClipboardProcessResult? lastFailedResult = null;
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _processRunner(command, standardInput, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return result;
                }

                lastFailedResult = result;
            }
            catch (Win32Exception exception)
            {
                lastException = exception;
            }
            catch (InvalidOperationException exception)
            {
                lastException = exception;
            }
        }

        if (lastFailedResult is { } failed)
        {
            throw new InvalidOperationException(
                $"Clipboard command failed with exit code {failed.ExitCode}. {failed.StandardError}");
        }

        throw new InvalidOperationException("No clipboard command could be started.", lastException);
    }

    private static async ValueTask<WpfClipboardProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start clipboard command '{startInfo.FileName}'.");
        }

        var outputTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);
        var errorTask = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : Task.FromResult(string.Empty);

        if (standardInput != null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfClipboardProcessResult(process.ExitCode, output, error);
    }
}
