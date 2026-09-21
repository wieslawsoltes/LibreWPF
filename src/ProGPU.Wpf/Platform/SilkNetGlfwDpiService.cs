using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.Windowing;

namespace System.Windows.Media.ProGPU.Platform;

internal readonly record struct WpfDeviceScale(double X, double Y)
{
    internal double Average => (X + Y) / 2.0;
}

internal static class SilkNetGlfwDpiService
{
    // These GLFW 3.4 hints are newer than the enums in Silk.NET 2.23.
    private const int GlfwScaleToMonitor = 0x0002200C;
    private const int GlfwScaleFramebuffer = 0x0002200D;

    private static readonly object s_exportsGate = new();
    private static GlfwDpiExports? s_exports;
    private static bool s_exportsResolved;

    internal static bool TryConfigureDpiWindowHints()
    {
        try
        {
            Glfw glfw = GlfwProvider.GLFW.Value;
            glfw.WindowHint((WindowHintBool)GlfwScaleToMonitor, true);
            glfw.WindowHint((WindowHintBool)GlfwScaleFramebuffer, true);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (GlfwException)
        {
            return false;
        }
    }

    internal static bool TryGetWindowContentScale(IWindow? window, out WpfDeviceScale scale)
    {
        scale = default;
        if (!TryGetGlfwWindowHandle(window, out nint glfwWindow) ||
            !TryGetExports(out GlfwDpiExports? exports))
        {
            return false;
        }

        try
        {
            exports!.GetWindowContentScale(glfwWindow, out float scaleX, out float scaleY);
            return TryNormalizeContentScale(scaleX, scaleY, out scale);
        }
        catch (AccessViolationException)
        {
            scale = default;
            return false;
        }
    }

    internal static IDisposable? TrySubscribeToWindowContentScale(
        IWindow? window,
        Action<WpfDeviceScale> contentScaleChanged)
    {
        ArgumentNullException.ThrowIfNull(contentScaleChanged);

        if (!TryGetGlfwWindowHandle(window, out nint glfwWindow) ||
            !TryGetExports(out GlfwDpiExports? exports))
        {
            return null;
        }

        GlfwWindowContentScaleCallback callback = (_, scaleX, scaleY) =>
        {
            if (TryNormalizeContentScale(scaleX, scaleY, out WpfDeviceScale scale))
            {
                contentScaleChanged(scale);
            }
        };

        nint callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
        nint previousCallback = exports!.SetWindowContentScaleCallback(glfwWindow, callbackPointer);
        return new ContentScaleSubscription(
            glfwWindow,
            exports.SetWindowContentScaleCallback,
            previousCallback,
            callback);
    }

    internal static bool UsesMonitorScaledWindowCoordinates(
        bool dpiWindowHintsConfigured,
        bool hasX11Window,
        bool hasWin32Window)
    {
        return dpiWindowHintsConfigured && (hasX11Window || hasWin32Window);
    }

    internal static bool TryNormalizeContentScale(
        double scaleX,
        double scaleY,
        out WpfDeviceScale scale)
    {
        scale = default;
        if (!IsUsableScale(scaleX) || !IsUsableScale(scaleY))
        {
            return false;
        }

        scale = new WpfDeviceScale(
            NormalizeScale(scaleX),
            NormalizeScale(scaleY));
        return true;
    }

    private static bool TryGetGlfwWindowHandle(IWindow? window, out nint glfwWindow)
    {
        glfwWindow = 0;
        if (window is not INativeWindowSource nativeWindowSource)
        {
            return false;
        }

        IntPtr? handle = nativeWindowSource.Native?.Glfw;
        if (!handle.HasValue || handle.Value == IntPtr.Zero)
        {
            return false;
        }

        glfwWindow = handle.Value;
        return true;
    }

    private static bool TryGetExports(out GlfwDpiExports? exports)
    {
        lock (s_exportsGate)
        {
            if (!s_exportsResolved)
            {
                s_exports = TryLoadExports();
                s_exportsResolved = true;
            }

            exports = s_exports;
            return exports != null;
        }
    }

    private static GlfwDpiExports? TryLoadExports()
    {
        string[] libraryNames = OperatingSystem.IsWindows()
            ? new[] { "glfw3.dll", "glfw3" }
            : OperatingSystem.IsMacOS()
                ? new[] { "libglfw.3.dylib", "libglfw.dylib", "glfw3" }
                : new[] { "libglfw.so.3", "libglfw.so", "glfw3" };

        foreach (string libraryName in libraryNames)
        {
            if (!NativeLibrary.TryLoad(libraryName, out nint libraryHandle))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(libraryHandle, "glfwGetWindowContentScale", out nint getScale) &&
                NativeLibrary.TryGetExport(libraryHandle, "glfwSetWindowContentScaleCallback", out nint setCallback))
            {
                return new GlfwDpiExports(
                    libraryHandle,
                    Marshal.GetDelegateForFunctionPointer<GlfwGetWindowContentScale>(getScale),
                    Marshal.GetDelegateForFunctionPointer<GlfwSetWindowContentScaleCallback>(setCallback));
            }

            NativeLibrary.Free(libraryHandle);
        }

        return null;
    }

    private static bool IsUsableScale(double scale)
    {
        return double.IsFinite(scale) && scale > 0.0 && scale <= 8.0;
    }

    private static double NormalizeScale(double scale)
    {
        return Math.Round(scale, 4, MidpointRounding.AwayFromZero);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlfwGetWindowContentScale(
        nint window,
        out float scaleX,
        out float scaleY);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GlfwSetWindowContentScaleCallback(
        nint window,
        nint callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlfwWindowContentScaleCallback(
        nint window,
        float scaleX,
        float scaleY);

    private sealed record GlfwDpiExports(
        nint LibraryHandle,
        GlfwGetWindowContentScale GetWindowContentScale,
        GlfwSetWindowContentScaleCallback SetWindowContentScaleCallback);

    private sealed class ContentScaleSubscription : IDisposable
    {
        private readonly nint _window;
        private readonly GlfwSetWindowContentScaleCallback _setCallback;
        private readonly nint _previousCallback;
        private GlfwWindowContentScaleCallback? _callback;

        internal ContentScaleSubscription(
            nint window,
            GlfwSetWindowContentScaleCallback setCallback,
            nint previousCallback,
            GlfwWindowContentScaleCallback callback)
        {
            _window = window;
            _setCallback = setCallback;
            _previousCallback = previousCallback;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_callback == null)
            {
                return;
            }

            _setCallback(_window, _previousCallback);
            _callback = null;
        }
    }
}
