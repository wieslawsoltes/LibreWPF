namespace ProGPU.Wpf.Sdk;

internal static class ProGpuWpfSdkPortableBootstrap
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
#if PROGPU_WPF_NATIVE_MIL
        // Typed Application/Window activation accepts Windows hosts, but the
        // source-built MIL startup and popup paths still select by OS.
        // Do not let an explicit native SDK selection run Windows MIL instead.
        if (global::System.OperatingSystem.IsWindows())
        {
            throw new global::System.PlatformNotSupportedException(
                "LibreWPF NativeMilWgpu SDK activation on Windows requires explicit portable MIL startup and popup routing, which are not connected yet. The direct native host gate remains available.");
        }
#endif
#if PROGPU_WPF_USE_LIBREWINFORMS
        global::System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
#endif

        if (global::System.OperatingSystem.IsWindows())
        {
            return;
        }

        global::System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(global::System.Windows.Application).Module.ModuleHandle);
        global::System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(global::System.Windows.Clipboard).Module.ModuleHandle);
#if PROGPU_WPF_NATIVE_MIL
        bool registered = global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
            static window => new global::System.Windows.Media.ProGPU.ProGpuWpfWindowHost(
                global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.CreateHostOptions(
                    window,
                    new global::System.Windows.Media.ProGPU.ProGpuWpfWindowOptions
                    {
                        RendererMode = global::System.Windows.Media.ProGPU.ProGpuWpfRendererMode.NativeMilWgpu
                    })));
        if (!registered)
        {
            throw new global::System.InvalidOperationException(
                "LibreWPF NativeMilWgpu requires the source-built typed WPF activation service.");
        }
#else
        global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation();
#endif
        global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService();
    }
}
