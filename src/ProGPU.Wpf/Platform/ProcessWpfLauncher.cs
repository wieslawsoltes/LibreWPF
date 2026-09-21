using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Windows.Media.ProGPU.Platform;

public sealed class ProcessWpfLauncher : IWpfLauncher
{
    public ValueTask OpenUriAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        Start(CreateStartInfoForUri(uri));
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        Start(CreateStartInfoForFile(path));
        return ValueTask.CompletedTask;
    }

    public static ProcessStartInfo CreateStartInfoForUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return new ProcessStartInfo
        {
            FileName = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString(),
            UseShellExecute = true
        };
    }

    public static ProcessStartInfo CreateStartInfoForFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new ProcessStartInfo
        {
            FileName = Path.GetFullPath(path),
            UseShellExecute = true
        };
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        if (Process.Start(startInfo) == null)
        {
            throw new InvalidOperationException($"Unable to launch '{startInfo.FileName}'.");
        }
    }
}
