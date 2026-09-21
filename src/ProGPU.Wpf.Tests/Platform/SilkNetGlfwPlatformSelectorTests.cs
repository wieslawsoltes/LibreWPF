using System.Windows.Media.ProGPU.Platform;
using ProGPU.Backend;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class SilkNetGlfwPlatformSelectorTests
{
    [Theory]
    [InlineData(true, false, false, null, null, null, null, (int)NativeWindowKind.Win32)]
    [InlineData(false, true, false, null, null, null, null, (int)NativeWindowKind.Cocoa)]
    [InlineData(false, false, true, "x11", null, ":0", null, (int)NativeWindowKind.X11)]
    [InlineData(false, false, true, "wayland", "wayland-0", ":0", null, (int)NativeWindowKind.X11)]
    [InlineData(false, false, true, "wayland", "wayland-0", ":0", "wayland", (int)NativeWindowKind.Wayland)]
    [InlineData(false, false, true, "wayland", "wayland-0", null, null, (int)NativeWindowKind.Wayland)]
    [InlineData(false, false, false, null, null, null, null, (int)NativeWindowKind.Unknown)]
    public void DesktopPointerProviderMatchesSelectedWindowSystem(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        string? sessionType,
        string? waylandDisplay,
        string? x11Display,
        string? configuredPreference,
        int expected)
    {
        Assert.Equal(
            (NativeWindowKind)expected,
            SilkNetWpfMonitorService.ResolveDesktopPointerPlatformKind(
                isWindows,
                isMacOS,
                isLinux,
                sessionType,
                waylandDisplay,
                x11Display,
                configuredPreference));
    }

    [Theory]
    [InlineData("wayland", "wayland-0", ":0", null, (int)LinuxGlfwPlatformPreference.X11)]
    [InlineData("wayland", "wayland-0", null, null, (int)LinuxGlfwPlatformPreference.Any)]
    [InlineData("x11", null, ":0", null, (int)LinuxGlfwPlatformPreference.Any)]
    [InlineData(null, null, ":0", null, (int)LinuxGlfwPlatformPreference.Any)]
    [InlineData("wayland", "wayland-0", ":0", "wayland", (int)LinuxGlfwPlatformPreference.Wayland)]
    [InlineData("wayland", "wayland-0", null, "x11", (int)LinuxGlfwPlatformPreference.X11)]
    [InlineData("x11", null, ":0", "WAYLAND", (int)LinuxGlfwPlatformPreference.Wayland)]
    public void ResolvePreferencePreservesDesktopWindowSemanticsWhenXWaylandIsAvailable(
        string? sessionType,
        string? waylandDisplay,
        string? x11Display,
        string? configuredPreference,
        int expected)
    {
        Assert.Equal(
            (LinuxGlfwPlatformPreference)expected,
            SilkNetGlfwPlatformSelector.ResolvePreference(
                sessionType,
                waylandDisplay,
                x11Display,
                configuredPreference));
    }

    [Theory]
    [InlineData(false, true, "x11", null, ":0", null, false)]
    [InlineData(true, false, "x11", null, ":0", null, false)]
    [InlineData(true, true, "x11", null, ":0", null, true)]
    [InlineData(true, true, null, null, ":0", null, true)]
    [InlineData(true, true, "wayland", "wayland-0", ":0", null, true)]
    [InlineData(true, true, "wayland", "wayland-0", ":0", "wayland", false)]
    [InlineData(true, true, "wayland", "wayland-0", null, null, false)]
    [InlineData(true, true, "x11", null, ":0", "wayland", false)]
    [InlineData(true, true, "wayland", "wayland-0", null, "x11", true)]
    public void TransparentX11WindowsRequestAnAlphaCapableClientVisual(
        bool isLinux,
        bool transparentFramebuffer,
        string? sessionType,
        string? waylandDisplay,
        string? x11Display,
        string? configuredPreference,
        bool expected)
    {
        Assert.Equal(
            expected,
            SilkNetGlfwPlatformSelector.RequiresClientApiForTransparentFramebuffer(
                isLinux,
                transparentFramebuffer,
                sessionType,
                waylandDisplay,
                x11Display,
                configuredPreference));
    }
}
