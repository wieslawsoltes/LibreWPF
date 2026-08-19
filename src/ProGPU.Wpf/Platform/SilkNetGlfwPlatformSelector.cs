using System;
using Silk.NET.GLFW;

namespace System.Windows.Media.ProGPU.Platform;

internal enum LinuxGlfwPlatformPreference
{
    Any,
    Wayland,
    X11
}

internal static class SilkNetGlfwPlatformSelector
{
    private const int GlfwPlatformHint = 0x00050003;
    private const int GlfwPlatformWayland = 0x00060003;
    private const int GlfwPlatformX11 = 0x00060004;
    private const string LinuxWindowingEnvironmentVariable = "PROGPU_WPF_LINUX_WINDOWING";
    private static readonly object s_gate = new();
    private static bool s_configurationAttempted;

    internal static bool ConfigureBeforeFirstGlfwUse()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        LinuxGlfwPlatformPreference preference = ResolvePreference(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            Environment.GetEnvironmentVariable(LinuxWindowingEnvironmentVariable));
        if (preference == LinuxGlfwPlatformPreference.Any)
        {
            return false;
        }

        lock (s_gate)
        {
            if (s_configurationAttempted || GlfwProvider.GLFW.IsValueCreated)
            {
                return false;
            }

            s_configurationAttempted = true;
            int platform = preference == LinuxGlfwPlatformPreference.X11
                ? GlfwPlatformX11
                : GlfwPlatformWayland;
            GlfwProvider.UninitializedGLFW.Value.InitHint(
                (InitHint)GlfwPlatformHint,
                platform);
            return true;
        }
    }

    internal static bool RequiresClientApiForTransparentFramebuffer(
        bool transparentFramebuffer)
    {
        return RequiresClientApiForTransparentFramebuffer(
            OperatingSystem.IsLinux(),
            transparentFramebuffer,
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            Environment.GetEnvironmentVariable(LinuxWindowingEnvironmentVariable));
    }

    internal static bool RequiresClientApiForTransparentFramebuffer(
        bool isLinux,
        bool transparentFramebuffer,
        string? sessionType,
        string? waylandDisplay,
        string? x11Display,
        string? configuredPreference)
    {
        if (!isLinux || !transparentFramebuffer)
        {
            return false;
        }

        LinuxGlfwPlatformPreference preference = ResolvePreference(
            sessionType,
            waylandDisplay,
            x11Display,
            configuredPreference);
        if (preference != LinuxGlfwPlatformPreference.Any)
        {
            return preference == LinuxGlfwPlatformPreference.X11;
        }

        return string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(waylandDisplay)
                && !string.IsNullOrWhiteSpace(x11Display));
    }

    internal static LinuxGlfwPlatformPreference ResolvePreference(
        string? sessionType,
        string? waylandDisplay,
        string? x11Display,
        string? configuredPreference)
    {
        if (string.Equals(configuredPreference, "x11", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxGlfwPlatformPreference.X11;
        }

        if (string.Equals(configuredPreference, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return LinuxGlfwPlatformPreference.Wayland;
        }

        bool isWaylandSession =
            string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(waylandDisplay);
        if (isWaylandSession && !string.IsNullOrWhiteSpace(x11Display))
        {
            // GLFW cannot position Wayland toplevels and does not expose the
            // xdg_toplevel + input serial needed by xdg_toplevel.move. Prefer
            // XWayland when it is available so WPF DragMove, floating docking
            // windows, native popup ownership, and screen coordinates retain
            // their desktop semantics. Set PROGPU_WPF_LINUX_WINDOWING=wayland
            // to opt into the native Wayland limitations explicitly.
            return LinuxGlfwPlatformPreference.X11;
        }

        return LinuxGlfwPlatformPreference.Any;
    }
}
