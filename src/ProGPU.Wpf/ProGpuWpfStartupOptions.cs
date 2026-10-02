using System;

namespace System.Windows.Media.ProGPU;

/// <summary>Immutable, explicit source-host choices captured by the SDK before application startup.</summary>
public readonly record struct ProGpuWpfStartupOptions
{
    /// <summary>Requests the existing Cocoa native modal session; it does not qualify or enable automatic policy.</summary>
    public bool EnableNativeModalSessions { get; }

    private ProGpuWpfStartupOptions(bool enableNativeModalSessions)
        => EnableNativeModalSessions = enableNativeModalSessions;

    /// <summary>
    /// Reads application arguments without removing or changing them. The exact
    /// --libre-native-modal-sessions switch is startup-only; arguments after --
    /// belong entirely to the application. Other application options are untouched.
    /// </summary>
    public static ProGpuWpfStartupOptions ParseArguments(ReadOnlySpan<string> arguments)
        => ParseArguments(arguments, OperatingSystem.IsMacOS(), string.Equals(
            Environment.GetEnvironmentVariable("PROGPU_WPF_DISABLE_NATIVE_POPUPS"), "1", StringComparison.Ordinal));

    internal static ProGpuWpfStartupOptions ParseArguments(
        ReadOnlySpan<string> arguments, bool isMacOS, bool nativePopupsDisabled)
    {
        bool requested = false;
        foreach (string argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (argument == "--") break;
            if (!argument.StartsWith("--libre-native-modal", StringComparison.Ordinal)) continue;
            if (argument != "--libre-native-modal-sessions" || requested)
                throw new ArgumentException("Use --libre-native-modal-sessions exactly once, without a value.", nameof(arguments));
            requested = true;
        }

        if (requested && !isMacOS)
            throw new PlatformNotSupportedException("Explicit native modal startup requires the Cocoa session provider; Windows and Linux policies are unchanged.");
        if (requested && nativePopupsDisabled)
            throw new InvalidOperationException("Explicit native modal startup requires owned native popups; PROGPU_WPF_DISABLE_NATIVE_POPUPS=1 conflicts with this request.");
        return new(requested);
    }
}
