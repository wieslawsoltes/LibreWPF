using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProGPU.Backend;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfMessageBoxPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public readonly record struct WpfMessageBoxProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfMessageBoxProcessResult> WpfMessageBoxProcessRunner(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class ProcessWpfMessageBoxService : IWpfMessageBoxService
{
    private readonly Func<WpfMessageBoxPlatform> _platformProvider;
    private readonly WpfMessageBoxProcessRunner _processRunner;

    public ProcessWpfMessageBoxService()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfMessageBoxService(
        Func<WpfMessageBoxPlatform> platformProvider,
        WpfMessageBoxProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public string Show(WpfMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        WpfMessageBoxPlatform platform = _platformProvider();
        var commands = CreateStartInfos(platform, options);
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("Message box services are not available on this platform.");
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
                return MapProcessResult(platform, options, result);
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

        throw new InvalidOperationException("No message box command could be started.", lastStartException);
    }

    public static WpfMessageBoxPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfMessageBoxPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfMessageBoxPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfMessageBoxPlatform.Linux;
        }

        return WpfMessageBoxPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateStartInfos(
        WpfMessageBoxPlatform platform,
        WpfMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return platform switch
        {
            WpfMessageBoxPlatform.Windows => new[] { CreateWindowsStartInfo(options) },
            WpfMessageBoxPlatform.MacOS => new[] { CreateMacStartInfo(options) },
            WpfMessageBoxPlatform.Linux => new[] { CreateLinuxZenityStartInfo(options), CreateLinuxKDialogStartInfo(options) },
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    private static ProcessStartInfo CreateWindowsStartInfo(WpfMessageBoxOptions options)
    {
        int messageBoxOptions = MapWindowsOptions(options.Options);
        NativeWindowHandle owner = ResolveOwnerNativeHandle(options);
        bool useOwner = owner.Kind == NativeWindowKind.Win32
            && owner.IsValid
            && CanUseWindowsOwner(messageBoxOptions);

        var command = "Add-Type -AssemblyName System.Windows.Forms; ";
        if (useOwner)
        {
            command += "Add-Type -TypeDefinition 'using System; using System.Windows.Forms; "
                + "public sealed class LibreWpfMessageBoxOwner : IWin32Window { "
                + "private readonly IntPtr handle; "
                + "public LibreWpfMessageBoxOwner(long value) { handle = new IntPtr(value); } "
                + "public IntPtr Handle { get { return handle; } } }' -ReferencedAssemblies System.Windows.Forms; "
                + $"$owner = [LibreWpfMessageBoxOwner]::new({owner.Handle.ToInt64()}); ";
        }

        command += "[Console]::Write([System.Windows.Forms.MessageBox]::Show("
            + (useOwner ? "$owner, " : string.Empty)
            + $"'{EscapePowerShellString(options.MessageBoxText)}', "
            + $"'{EscapePowerShellString(options.Caption)}', "
            + $"[System.Windows.Forms.MessageBoxButtons]::{MapWindowsButtons(options.Button)}, "
            + $"[System.Windows.Forms.MessageBoxIcon]::{MapWindowsIcon(options.Icon)}, "
            + $"[System.Windows.Forms.MessageBoxDefaultButton]::{MapWindowsDefaultButton(options)}, "
            + $"([System.Windows.Forms.MessageBoxOptions]{messageBoxOptions})))";

        return CreateStartInfo("powershell", "-NoProfile", "-NonInteractive", "-Command", command);
    }

    private static ProcessStartInfo CreateMacStartInfo(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var buttons = CreateAppleScriptButtonList(buttonSet.Labels);
        var script = $"display dialog \"{EscapeAppleScriptString(options.MessageBoxText)}\" "
            + $"with title \"{EscapeAppleScriptString(options.Caption)}\" "
            + $"buttons {{{buttons}}} "
            + $"default button \"{EscapeAppleScriptString(GetDefaultButtonLabel(options, buttonSet))}\"";

        if (!string.IsNullOrEmpty(buttonSet.CancelLabel))
        {
            script += $" cancel button \"{EscapeAppleScriptString(buttonSet.CancelLabel)}\"";
        }

        var icon = MapMacIcon(options.Icon);
        if (!string.IsNullOrEmpty(icon))
        {
            script += $" with icon {icon}";
        }

        return CreateStartInfo("osascript", "-e", script);
    }

    private static ProcessStartInfo CreateLinuxZenityStartInfo(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var startInfo = CreateStartInfo(
            "zenity",
            MapZenityDialogKind(options),
            $"--title={options.Caption}",
            $"--text={options.MessageBoxText}",
            $"--ok-label={buttonSet.PrimaryLabel}");

        if (TryGetX11OwnerId(options, out string ownerId))
        {
            startInfo.ArgumentList.Add($"--attach={ownerId}");
        }

        if (!string.Equals(MapZenityDialogKind(options), "--info", StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add($"--cancel-label={buttonSet.SecondaryLabel}");
        }

        foreach (var extra in buttonSet.ExtraLabels)
        {
            startInfo.ArgumentList.Add($"--extra-button={extra}");
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateLinuxKDialogStartInfo(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var startInfo = CreateStartInfo("kdialog");
        if (TryGetX11OwnerId(options, out string ownerId))
        {
            startInfo.ArgumentList.Add("--attach");
            startInfo.ArgumentList.Add(ownerId);
        }

        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add(options.Caption);
        startInfo.ArgumentList.Add(GetKDialogKind(options));
        startInfo.ArgumentList.Add(options.MessageBoxText);

        if (string.Equals(options.Button, "OK", StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add("--ok-label");
            startInfo.ArgumentList.Add(buttonSet.PrimaryLabel);
        }
        else
        {
            startInfo.ArgumentList.Add("--yes-label");
            startInfo.ArgumentList.Add(buttonSet.PrimaryLabel);
            startInfo.ArgumentList.Add("--no-label");
            startInfo.ArgumentList.Add(buttonSet.SecondaryLabel);

            if (buttonSet.ExtraLabels.Count > 0)
            {
                startInfo.ArgumentList.Add("--cancel-label");
                startInfo.ArgumentList.Add(buttonSet.ExtraLabels[0]);
            }
        }

        return startInfo;
    }

    private static string MapProcessResult(
        WpfMessageBoxPlatform platform,
        WpfMessageBoxOptions options,
        WpfMessageBoxProcessResult result)
    {
        var buttonSet = GetButtonSet(options.Button);
        var output = result.StandardOutput.Trim();

        if (TryParseResultName(output, buttonSet, out var parsed))
        {
            return parsed;
        }

        if (output.StartsWith("button returned:", StringComparison.OrdinalIgnoreCase) &&
            TryParseResultName(output["button returned:".Length..].Trim(), buttonSet, out parsed))
        {
            return parsed;
        }

        if (platform == WpfMessageBoxPlatform.MacOS
            && result.ExitCode != 0
            && buttonSet.CancelLabel is not null
            && (result.StandardError.Contains("(-128)", StringComparison.Ordinal)
                || result.StandardError.Contains("User canceled", StringComparison.OrdinalIgnoreCase)))
        {
            return buttonSet.ResultByLabel[buttonSet.CancelLabel];
        }

        return result.ExitCode switch
        {
            0 => buttonSet.PrimaryResult,
            1 => buttonSet.SecondaryResult,
            2 when buttonSet.ExtraResults.Count > 0 => buttonSet.ExtraResults[0],
            _ => GetFallbackResult(options)
        };
    }

    private static bool TryParseResultName(string output, ButtonSet buttonSet, out string result)
    {
        if (buttonSet.ResultByLabel.TryGetValue(output, out var mappedResult))
        {
            result = mappedResult;
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static ButtonSet GetButtonSet(string? button)
    {
        return button switch
        {
            "OKCancel" => ButtonSet.Create("OK", "Cancel"),
            "YesNo" => ButtonSet.Create("Yes", "No"),
            "YesNoCancel" => ButtonSet.Create("Yes", "No", "Cancel"),
            "RetryCancel" => ButtonSet.Create("Retry", "Cancel"),
            "AbortRetryIgnore" => ButtonSet.Create("Abort", "Retry", "Ignore"),
            "CancelTryContinue" => ButtonSet.Create("Cancel", "TryAgain", "Continue"),
            _ => ButtonSet.Create("OK")
        };
    }

    private static string GetDefaultButtonLabel(WpfMessageBoxOptions options, ButtonSet buttonSet)
    {
        var fallback = GetFallbackResult(options);
        foreach (var mapping in buttonSet.ResultByLabel)
        {
            if (string.Equals(mapping.Value, fallback, StringComparison.Ordinal))
            {
                return mapping.Key;
            }
        }

        return buttonSet.PrimaryLabel;
    }

    private static string GetFallbackResult(WpfMessageBoxOptions options)
    {
        return string.IsNullOrWhiteSpace(options.FallbackResult)
            ? GetButtonSet(options.Button).PrimaryResult
            : options.FallbackResult;
    }

    private static string MapWindowsButtons(string? button)
    {
        return button switch
        {
            "OKCancel" => "OKCancel",
            "YesNo" => "YesNo",
            "YesNoCancel" => "YesNoCancel",
            "RetryCancel" => "RetryCancel",
            "AbortRetryIgnore" => "AbortRetryIgnore",
            "CancelTryContinue" => "CancelTryContinue",
            _ => "OK"
        };
    }

    private static string MapWindowsIcon(string? icon)
    {
        return icon switch
        {
            "Asterisk" or "Information" => "Information",
            "Error" or "Hand" or "Stop" => "Error",
            "Exclamation" or "Warning" => "Warning",
            "Question" => "Question",
            _ => "None"
        };
    }

    private static string MapWindowsDefaultButton(WpfMessageBoxOptions options)
    {
        var buttonSet = GetButtonSet(options.Button);
        var fallback = GetFallbackResult(options);
        for (var index = 0; index < buttonSet.Results.Count; index++)
        {
            if (string.Equals(buttonSet.Results[index], fallback, StringComparison.Ordinal))
            {
                return $"Button{Math.Min(index + 1, 3)}";
            }
        }

        return "Button1";
    }

    private static int MapWindowsOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options)
            || string.Equals(options, "None", StringComparison.Ordinal))
        {
            return 0;
        }

        int result = 0;
        foreach (string option in options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= option switch
            {
                "DefaultDesktopOnly" => 0x00020000,
                "RightAlign" => 0x00080000,
                "RtlReading" => 0x00100000,
                "ServiceNotification" => 0x00200000,
                _ => 0
            };
        }

        return result;
    }

    private static bool CanUseWindowsOwner(int messageBoxOptions)
    {
        const int desktopOrServiceNotification = 0x00020000 | 0x00200000;
        return (messageBoxOptions & desktopOrServiceNotification) == 0;
    }

    private static bool TryGetX11OwnerId(WpfMessageBoxOptions options, out string ownerId)
    {
        NativeWindowHandle owner = ResolveOwnerNativeHandle(options);
        if (owner.Kind == NativeWindowKind.X11 && owner.IsValid)
        {
            ownerId = unchecked((nuint)owner.Handle).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        ownerId = string.Empty;
        return false;
    }

    private static NativeWindowHandle ResolveOwnerNativeHandle(WpfMessageBoxOptions options)
    {
        if (options.OwnerNativeHandle.IsValid)
        {
            return options.OwnerNativeHandle;
        }

        if (options.Owner is NativeWindowHandle nativeHandle && nativeHandle.IsValid)
        {
            return nativeHandle;
        }

        if (options.Owner is ProGpuWpfWindowHost host)
        {
            return ResolveHostNativeHandle(host);
        }

        if (options.Owner is IWindow window)
        {
            return ResolveWindowNativeHandle(window);
        }

        return WpfPortableWindowActivation.TryGetActiveHost(options.Owner, out ProGpuWpfWindowHost? activeHost)
            ? ResolveHostNativeHandle(activeHost!)
            : NativeWindowHandle.Empty;
    }

    private static NativeWindowHandle ResolveHostNativeHandle(ProGpuWpfWindowHost host)
    {
        return ResolveWindowNativeHandle(host.SilkWindow);
    }

    private static NativeWindowHandle ResolveWindowNativeHandle(IWindow? window)
    {
        if (window == null)
        {
            return NativeWindowHandle.Empty;
        }

        using var controller = new SilkWindowController(window);
        return controller.Handle;
    }

    private static string MapMacIcon(string? icon)
    {
        return icon switch
        {
            "Error" or "Hand" or "Stop" => "stop",
            "Exclamation" or "Warning" => "caution",
            "Asterisk" or "Information" or "Question" => "note",
            _ => string.Empty
        };
    }

    private static string MapZenityDialogKind(WpfMessageBoxOptions options)
    {
        if (string.Equals(options.Button, "OK", StringComparison.Ordinal))
        {
            return options.Icon switch
            {
                "Error" or "Hand" or "Stop" => "--error",
                "Exclamation" or "Warning" => "--warning",
                _ => "--info"
            };
        }

        return "--question";
    }

    private static string GetKDialogKind(WpfMessageBoxOptions options)
    {
        if (string.Equals(options.Button, "OK", StringComparison.Ordinal))
        {
            return options.Icon switch
            {
                "Error" or "Hand" or "Stop" => "--error",
                "Exclamation" or "Warning" => "--sorry",
                _ => "--msgbox"
            };
        }

        return GetButtonSet(options.Button).ExtraLabels.Count > 0 ? "--yesnocancel" : "--yesno";
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

    private static string CreateAppleScriptButtonList(IReadOnlyList<string> labels)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < labels.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder
                .Append('"')
                .Append(EscapeAppleScriptString(labels[i]))
                .Append('"');
        }

        return builder.ToString();
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

    private static async ValueTask<WpfMessageBoxProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start message box command '{startInfo.FileName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfMessageBoxProcessResult(process.ExitCode, output, error);
    }

    private sealed class ButtonSet
    {
        private ButtonSet(
            IReadOnlyList<string> labels,
            IReadOnlyList<string> results,
            IReadOnlyList<string> extraLabels,
            IReadOnlyList<string> extraResults,
            string? cancelLabel,
            IReadOnlyDictionary<string, string> resultByLabel)
        {
            Labels = labels;
            Results = results;
            ExtraLabels = extraLabels;
            ExtraResults = extraResults;
            CancelLabel = cancelLabel;
            ResultByLabel = resultByLabel;
        }

        public IReadOnlyList<string> Labels { get; }

        public IReadOnlyList<string> Results { get; }

        public IReadOnlyDictionary<string, string> ResultByLabel { get; }

        public string PrimaryLabel => Labels[0];

        public string SecondaryLabel => Labels.Count > 1 ? Labels[1] : Labels[0];

        public string? CancelLabel { get; }

        public IReadOnlyList<string> ExtraLabels { get; }

        public string PrimaryResult => Results[0];

        public string SecondaryResult => Results.Count > 1 ? Results[1] : Results[0];

        public IReadOnlyList<string> ExtraResults { get; }

        public static ButtonSet Create(params string[] labels)
        {
            var results = new string[labels.Length];
            var resultByLabel = new Dictionary<string, string>(labels.Length, StringComparer.OrdinalIgnoreCase);
            string? cancelLabel = null;
            for (var i = 0; i < labels.Length; i++)
            {
                string label = labels[i];
                string result = label == "TryAgain" ? "TryAgain" : label;
                results[i] = result;
                resultByLabel[label] = result;
                if (string.Equals(label, "Cancel", StringComparison.Ordinal))
                {
                    cancelLabel = label;
                }
            }

            return new ButtonSet(
                labels,
                results,
                CreateTail(labels),
                CreateTail(results),
                cancelLabel,
                resultByLabel);
        }

        private static IReadOnlyList<string> CreateTail(IReadOnlyList<string> values)
        {
            if (values.Count <= 2)
            {
                return Array.Empty<string>();
            }

            var tail = new string[values.Count - 2];
            for (var i = 0; i < tail.Length; i++)
            {
                tail[i] = values[i + 2];
            }

            return tail;
        }
    }
}
