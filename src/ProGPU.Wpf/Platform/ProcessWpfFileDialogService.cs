using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public enum WpfFileDialogPlatform
{
    Windows,
    MacOS,
    Linux,
    Unsupported
}

public enum WpfFileDialogKind
{
    OpenFile,
    SaveFile,
    PickFolder
}

public readonly record struct WpfFileDialogProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public delegate ValueTask<WpfFileDialogProcessResult> WpfFileDialogProcessRunner(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class ProcessWpfFileDialogService : IWpfFileDialogService
{
    private const char MultiplePathSeparator = '\u001E';
    private readonly Func<WpfFileDialogPlatform> _platformProvider;
    private readonly WpfFileDialogProcessRunner _processRunner;

    public ProcessWpfFileDialogService()
        : this(DetectPlatform, RunProcessAsync)
    {
    }

    public ProcessWpfFileDialogService(
        Func<WpfFileDialogPlatform> platformProvider,
        WpfFileDialogProcessRunner processRunner)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public ValueTask<string?> OpenFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunDialogAsync(WpfFileDialogKind.OpenFile, options, cancellationToken);
    }

    public ValueTask<string[]?> OpenFilesAsync(
        WpfFileDialogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunDialogsAsync(WpfFileDialogKind.OpenFile, options, cancellationToken);
    }

    public ValueTask<string?> SaveFileAsync(WpfFileDialogOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunDialogAsync(WpfFileDialogKind.SaveFile, options, cancellationToken);
    }

    public ValueTask<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        return RunDialogAsync(WpfFileDialogKind.PickFolder, new WpfFileDialogOptions(), cancellationToken);
    }

    public ValueTask<string[]?> PickFoldersAsync(
        WpfFileDialogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RunDialogsAsync(WpfFileDialogKind.PickFolder, options, cancellationToken);
    }

    public static WpfFileDialogPlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WpfFileDialogPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WpfFileDialogPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return WpfFileDialogPlatform.Linux;
        }

        return WpfFileDialogPlatform.Unsupported;
    }

    public static IReadOnlyList<ProcessStartInfo> CreateStartInfos(
        WpfFileDialogPlatform platform,
        WpfFileDialogKind kind,
        WpfFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return platform switch
        {
            WpfFileDialogPlatform.Windows => new[] { CreateWindowsStartInfo(kind, options) },
            WpfFileDialogPlatform.MacOS => new[] { CreateMacStartInfo(kind, options) },
            WpfFileDialogPlatform.Linux => CreateLinuxStartInfos(kind, options),
            _ => Array.Empty<ProcessStartInfo>()
        };
    }

    private async ValueTask<string?> RunDialogAsync(
        WpfFileDialogKind kind,
        WpfFileDialogOptions options,
        CancellationToken cancellationToken)
    {
        string[]? selectedPaths = await RunDialogsAsync(kind, options, cancellationToken).ConfigureAwait(false);
        return selectedPaths is { Length: > 0 } ? selectedPaths[0] : null;
    }

    private async ValueTask<string[]?> RunDialogsAsync(
        WpfFileDialogKind kind,
        WpfFileDialogOptions options,
        CancellationToken cancellationToken)
    {
        var commands = CreateStartInfos(_platformProvider(), kind, options);
        if (commands.Count == 0)
        {
            throw new PlatformNotSupportedException("File dialog services are not available on this platform.");
        }

        Exception? lastStartException = null;
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _processRunner(command, cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return ParseSelectedPaths(result.StandardOutput, options.AllowMultipleSelection);
                }

                return null;
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

        throw new InvalidOperationException("No file dialog command could be started.", lastStartException);
    }

    private static ProcessStartInfo CreateWindowsStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var command = kind switch
        {
            WpfFileDialogKind.OpenFile => CreateWindowsOpenCommand(options),
            WpfFileDialogKind.SaveFile => CreateWindowsSaveCommand(options),
            WpfFileDialogKind.PickFolder => CreateWindowsFolderCommand(options),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return CreateStartInfo("powershell", "-NoProfile", "-NonInteractive", "-Command", command);
    }

    private static string CreateWindowsOpenCommand(WpfFileDialogOptions options)
    {
        var filter = CreateWindowsFilter(options.FileTypePatterns);
        var command = "Add-Type -AssemblyName System.Windows.Forms; "
            + "$f = New-Object System.Windows.Forms.OpenFileDialog; "
            + $"$f.Title = '{EscapePowerShellString(options.Title ?? "Open File")}'; "
            + $"$f.Filter = '{EscapePowerShellString(filter)}'; ";

        if (options.AllowMultipleSelection)
        {
            command += "$f.Multiselect = $true; "
                + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) "
                + "{ [Console]::Write([string]::Join([char]30, $f.FileNames)) }";
        }
        else
        {
            command += "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) "
                + "{ [Console]::Write($f.FileName) }";
        }

        return command;
    }

    private static string CreateWindowsSaveCommand(WpfFileDialogOptions options)
    {
        var filter = CreateWindowsFilter(options.FileTypePatterns);
        var command = "Add-Type -AssemblyName System.Windows.Forms; "
            + "$f = New-Object System.Windows.Forms.SaveFileDialog; "
            + $"$f.Title = '{EscapePowerShellString(options.Title ?? "Save File")}'; "
            + $"$f.Filter = '{EscapePowerShellString(filter)}'; ";

        if (!string.IsNullOrWhiteSpace(options.SuggestedFileName))
        {
            command += $"$f.FileName = '{EscapePowerShellString(options.SuggestedFileName!)}'; ";
        }

        return command + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.FileName) }";
    }

    private static string CreateWindowsFolderCommand(WpfFileDialogOptions options)
    {
        if (options.AllowMultipleSelection)
        {
            return CreateWindowsMultiFolderCommand(options);
        }

        return "Add-Type -AssemblyName System.Windows.Forms; "
            + "$f = New-Object System.Windows.Forms.FolderBrowserDialog; "
            + $"$f.Description = '{EscapePowerShellString(options.Title ?? "Select Folder")}'; "
            + "if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Write($f.SelectedPath) }";
    }

    private static string CreateWindowsMultiFolderCommand(WpfFileDialogOptions options)
    {
        string title = EscapePowerShellString(options.Title ?? "Select Folders");
        return "$source = @'\n"
            + WindowsMultiFolderPickerSource
            + "\n'@\nAdd-Type -TypeDefinition $source; "
            + $"$paths = [ProGpuPortableFolderPicker]::Pick('{title}'); "
            + "if ($null -ne $paths -and $paths.Length -gt 0) "
            + "{ [Console]::Write([string]::Join([char]30, $paths)) }";
    }

    private const string WindowsMultiFolderPickerSource = """
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class ProGpuPortableFolderPicker
{
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosAllowMultiSelect = 0x00000200;
    private const uint FosPathMustExist = 0x00000800;
    private const uint SigdnFileSystemPath = 0x80058000;
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    public static string[] Pick(string title)
    {
        IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialogCom();
        IShellItemArray results = null;
        try
        {
            dialog.SetOptions(FosPickFolders | FosForceFileSystem | FosAllowMultiSelect | FosPathMustExist);
            dialog.SetTitle(title);
            int result = dialog.Show(IntPtr.Zero);
            if (result == ErrorCancelled)
            {
                return Array.Empty<string>();
            }

            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            dialog.GetResults(out results);
            uint count;
            results.GetCount(out count);
            var paths = new List<string>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IShellItem item = null;
                try
                {
                    results.GetItemAt(index, out item);
                    string path;
                    item.GetDisplayName(SigdnFileSystemPath, out path);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    Release(item);
                }
            }

            return paths.ToArray();
        }
        finally
        {
            Release(results);
            Release(dialog);
        }
    }

    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private sealed class FileOpenDialogCom
    {
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr filterSpec);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int errorCode);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr filter);
        void GetResults(out IShellItemArray items);
        void GetSelectedItems(out IShellItemArray items);
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetPropertyStore(int flags, ref Guid iid, out IntPtr propertyStore);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid iid, out IntPtr descriptionList);
        void GetAttributes(uint flags, uint mask, out uint attributes);
        void GetCount(out uint count);
        void GetItemAt(uint index, out IShellItem item);
        void EnumItems(out IntPtr enumerator);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint displayName, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem item, uint hint, out int order);
    }
}
""";

    private static string CreateWindowsFilter(IReadOnlyList<string> patterns)
    {
        var normalized = NormalizeFileTypePatterns(patterns);
        if (normalized.Count == 0)
        {
            return "All Files (*.*)|*.*";
        }

        var joined = string.Join(";", normalized);
        return $"Selected Files ({joined})|{joined}|All Files (*.*)|*.*";
    }

    private static ProcessStartInfo CreateMacStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var prompt = EscapeAppleScriptString(options.Title ?? DefaultTitle(kind));
        var script = kind switch
        {
            WpfFileDialogKind.OpenFile when options.AllowMultipleSelection =>
                $"set selectedFiles to choose file {CreateMacTypeClause(options.FileTypePatterns)}with prompt \"{prompt}\" with multiple selections allowed\n"
                + "set selectedPaths to \"\"\n"
                + "repeat with selectedFile in selectedFiles\n"
                + "if selectedPaths is not \"\" then set selectedPaths to selectedPaths & ASCII character 30\n"
                + "set selectedPaths to selectedPaths & POSIX path of selectedFile\n"
                + "end repeat\n"
                + "return selectedPaths",
            WpfFileDialogKind.OpenFile => $"POSIX path of (choose file {CreateMacTypeClause(options.FileTypePatterns)}with prompt \"{prompt}\")",
            WpfFileDialogKind.SaveFile => $"POSIX path of (choose file name default name \"{EscapeAppleScriptString(options.SuggestedFileName ?? "untitled")}\" with prompt \"{prompt}\")",
            WpfFileDialogKind.PickFolder when options.AllowMultipleSelection =>
                $"set selectedFolders to choose folder with prompt \"{prompt}\" with multiple selections allowed\n"
                + "set selectedPaths to \"\"\n"
                + "repeat with selectedFolder in selectedFolders\n"
                + "if selectedPaths is not \"\" then set selectedPaths to selectedPaths & ASCII character 30\n"
                + "set selectedPaths to selectedPaths & POSIX path of selectedFolder\n"
                + "end repeat\n"
                + "return selectedPaths",
            WpfFileDialogKind.PickFolder => $"POSIX path of (choose folder with prompt \"{prompt}\")",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return CreateStartInfo("osascript", "-e", script);
    }

    private static string CreateMacTypeClause(IReadOnlyList<string> patterns)
    {
        var normalized = NormalizeFileTypePatterns(patterns);
        if (normalized.Count == 0)
        {
            return string.Empty;
        }

        var extensions = new List<string>(normalized.Count);
        foreach (var pattern in normalized)
        {
            var extension = pattern.TrimStart('*', '.');
            if (!string.IsNullOrWhiteSpace(extension) &&
                !extension.Contains('*', StringComparison.Ordinal))
            {
                extensions.Add($"\"{EscapeAppleScriptString(extension)}\"");
            }
        }

        return extensions.Count == 0 ? string.Empty : $"of type {{{string.Join(", ", extensions)}}} ";
    }

    private static IReadOnlyList<ProcessStartInfo> CreateLinuxStartInfos(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        if (kind == WpfFileDialogKind.PickFolder && options.AllowMultipleSelection)
        {
            // Zenity explicitly supports combining directory selection with a
            // multiple-result separator. KDialog's existing-directory command
            // is single-result, so do not silently truncate a multi-folder
            // request by using it as a fallback.
            return new[] { CreateLinuxZenityStartInfo(kind, options) };
        }

        return new[]
        {
            CreateLinuxZenityStartInfo(kind, options),
            CreateLinuxKDialogStartInfo(kind, options)
        };
    }

    private static ProcessStartInfo CreateLinuxZenityStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var title = options.Title ?? DefaultTitle(kind);
        var startInfo = CreateStartInfo("zenity", "--file-selection", $"--title={title}");

        if (kind == WpfFileDialogKind.SaveFile)
        {
            startInfo.ArgumentList.Add("--save");
            startInfo.ArgumentList.Add("--confirm-overwrite");
            if (!string.IsNullOrWhiteSpace(options.SuggestedFileName))
            {
                startInfo.ArgumentList.Add($"--filename={options.SuggestedFileName}");
            }
        }
        else if (kind == WpfFileDialogKind.PickFolder)
        {
            startInfo.ArgumentList.Add("--directory");
        }

        if ((kind == WpfFileDialogKind.OpenFile || kind == WpfFileDialogKind.PickFolder)
            && options.AllowMultipleSelection)
        {
            startInfo.ArgumentList.Add("--multiple");
            startInfo.ArgumentList.Add($"--separator={MultiplePathSeparator}");
        }

        var normalized = NormalizeFileTypePatterns(options.FileTypePatterns);
        if (normalized.Count > 0 && kind != WpfFileDialogKind.PickFolder)
        {
            startInfo.ArgumentList.Add($"--file-filter=Selected Files | {string.Join(' ', normalized)}");
            startInfo.ArgumentList.Add("--file-filter=All Files | *");
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateLinuxKDialogStartInfo(WpfFileDialogKind kind, WpfFileDialogOptions options)
    {
        var title = options.Title ?? DefaultTitle(kind);
        var startInfo = CreateStartInfo("kdialog", "--title", title);

        if (kind == WpfFileDialogKind.OpenFile)
        {
            if (options.AllowMultipleSelection)
            {
                startInfo.ArgumentList.Add("--multiple");
                startInfo.ArgumentList.Add("--separate-output");
            }

            startInfo.ArgumentList.Add("--getopenfilename");
            startInfo.ArgumentList.Add(".");
            AddKDialogFileFilter(startInfo, options.FileTypePatterns);
        }
        else if (kind == WpfFileDialogKind.SaveFile)
        {
            startInfo.ArgumentList.Add("--getsavefilename");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(options.SuggestedFileName) ? "." : options.SuggestedFileName!);
            AddKDialogFileFilter(startInfo, options.FileTypePatterns);
        }
        else if (kind == WpfFileDialogKind.PickFolder)
        {
            startInfo.ArgumentList.Add("--getexistingdirectory");
            startInfo.ArgumentList.Add(".");
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return startInfo;
    }

    private static void AddKDialogFileFilter(ProcessStartInfo startInfo, IReadOnlyList<string> patterns)
    {
        var normalized = NormalizeFileTypePatterns(patterns);
        if (normalized.Count == 0)
        {
            return;
        }

        startInfo.ArgumentList.Add($"{string.Join(' ', normalized)}|Selected Files\n*|All Files");
    }

    private static string DefaultTitle(WpfFileDialogKind kind)
    {
        return kind switch
        {
            WpfFileDialogKind.OpenFile => "Open File",
            WpfFileDialogKind.SaveFile => "Save File",
            WpfFileDialogKind.PickFolder => "Select Folder",
            _ => string.Empty
        };
    }

    internal static string[]? ParseSelectedPaths(string standardOutput, bool allowMultipleSelection)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        if (!allowMultipleSelection)
        {
            string selectedPath = standardOutput.Trim();
            return selectedPath.Length == 0 ? null : [selectedPath];
        }

        char[] separators = standardOutput.IndexOf(MultiplePathSeparator) >= 0
            ? [MultiplePathSeparator]
            : ['\r', '\n'];
        string[] selectedPaths = standardOutput.Split(
            separators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return selectedPaths.Length == 0 ? null : selectedPaths;
    }

    private static IReadOnlyList<string> NormalizeFileTypePatterns(IReadOnlyList<string>? patterns)
    {
        if (patterns == null)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(patterns.Count);
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var trimmed = pattern.Trim();
            if (trimmed == "*" || trimmed == "*.*")
            {
                continue;
            }

            normalized.Add(trimmed.StartsWith('.') ? $"*{trimmed}" : trimmed);
        }

        return normalized.Count == 0 ? Array.Empty<string>() : normalized;
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

    private static async ValueTask<WpfFileDialogProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start file dialog command '{startInfo.FileName}'.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        return new WpfFileDialogProcessResult(process.ExitCode, output, error);
    }
}
