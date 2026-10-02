namespace ProGPU.Wpf.Sdk;

internal static class ProGpuWpfSdkPortableBootstrap
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        // Capture before source/media startup. Never reinterpret arguments when
        // another Window opens or use a capability query to enable this default.
        var startup = global::System.Windows.Media.ProGPU.ProGpuWpfStartupOptions.ParseArguments(
            global::System.MemoryExtensions.AsSpan(global::System.Environment.GetCommandLineArgs(), 1));
#if PROGPU_WPF_NATIVE_MIL
        // Native desktop packages provide x64/ARM64 backends. In particular,
        // the transport's win-x86 payload supports Windows MIL, not native MIL.
        // Reject an unsupported process before selecting media or creating WPF objects.
        var architecture = global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
        if (architecture != global::System.Runtime.InteropServices.Architecture.X64 &&
            architecture != global::System.Runtime.InteropServices.Architecture.Arm64)
        {
            throw new global::System.PlatformNotSupportedException(
                "LibreWPF NativeMilWgpu requires an x64 or ARM64 desktop process and its matching ProGPU native payload.");
        }
        // Install lazy text/geometry providers as well as transport selection:
        // application constructors can measure content before the first host exists.
        global::System.Windows.Media.ProGPU.ProGpuWpfNativeMediaServices.Initialize();
#endif
#if PROGPU_WPF_USE_LIBREWINFORMS
#if PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS
        if (startup.EnableNativeModalSessions)
            global::LibreWinForms.ProGPU.ProGpuPlatform.Register(enableNativeModalSessions: true);
        else
            global::LibreWinForms.ProGPU.ProGpuPlatform.Register();
#endif
        global::System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
#endif

#if !PROGPU_WPF_NATIVE_MIL
        // Ordinary managed Windows SDK applications keep Windows MIL. Explicit
        // native selection must instead register the portable source/window path
        // on Windows too, before the generated Application.Main constructs anything.
        if (global::System.OperatingSystem.IsWindows())
        {
            return;
        }
#endif

        global::System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(global::System.Windows.Application).Module.ModuleHandle);
        global::System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(global::System.Windows.Clipboard).Module.ModuleHandle);
#if PROGPU_WPF_NATIVE_MIL
        bool registered = global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
            window => new global::System.Windows.Media.ProGPU.ProGpuWpfWindowHost(
                global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.CreateHostOptions(
                    window,
                    new global::System.Windows.Media.ProGPU.ProGpuWpfWindowOptions
                    {
                        RendererMode = global::System.Windows.Media.ProGPU.ProGpuWpfRendererMode.NativeMilWgpu,
                        EnableNativeModalSessions = startup.EnableNativeModalSessions,
#if PROGPU_WPF_NATIVE_MIL_HIT_TESTING
                        EnableNativeMilHitTesting = true
#endif
                    })));
        if (!registered)
        {
            throw new global::System.InvalidOperationException(
                "LibreWPF NativeMilWgpu requires the source-built typed WPF activation service.");
        }
#else
        if (startup.EnableNativeModalSessions)
        {
            bool registered = global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
                window => new global::System.Windows.Media.ProGPU.ProGpuWpfWindowHost(
                    global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.CreateHostOptions(
                        window,
                        new global::System.Windows.Media.ProGPU.ProGpuWpfWindowOptions
                        {
                            EnableNativeModalSessions = startup.EnableNativeModalSessions
                        })));
            if (!registered)
                throw new global::System.InvalidOperationException(
                    "Explicit native modal startup requires the source-built typed WPF activation service.");
        }
        else
            global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation();
#endif
        global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService();
    }
}
