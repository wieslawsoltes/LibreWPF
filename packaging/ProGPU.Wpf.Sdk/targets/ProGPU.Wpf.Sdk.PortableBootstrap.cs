namespace ProGPU.Wpf.Sdk;

internal static class ProGpuWpfSdkPortableBootstrap
{
    [global::System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        // An executable can also be a project reference of a server or test
        // host. Only the actual entry assembly owns automatic desktop startup.
        // This identity query does not inspect or invoke application members.
        if (global::System.Reflection.Assembly.GetEntryAssembly() != typeof(ProGpuWpfSdkPortableBootstrap).Assembly)
            return;

#if PROGPU_WPF_BOOTSTRAP_WPF
        bool enableNativeModalSessions = ReadWpfStartupOptions();
#if !PROGPU_WPF_NATIVE_MIL
        if (global::System.OperatingSystem.IsWindows())
        {
            InitializeWindowsForms(enableNativeModalSessions);
            return;
        }
#endif
        InitializeWpf(enableNativeModalSessions);
#elif PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS
        InitializeFormsOnly();
#endif
    }

#if PROGPU_WPF_USE_LIBREWINFORMS || PROGPU_WPF_BOOTSTRAP_WPF
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void InitializeWindowsForms(bool enableNativeModalSessions)
    {
#if PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS
        if (enableNativeModalSessions)
            global::LibreWinForms.ProGPU.ProGpuPlatform.Register(enableNativeModalSessions: true);
        else
            global::LibreWinForms.ProGPU.ProGpuPlatform.Register();
#endif
#if PROGPU_WPF_BOOTSTRAP_WPF && PROGPU_WPF_USE_LIBREWINFORMS
        EnableWindowsFormsInterop();
#endif
    }
#endif

#if PROGPU_WPF_USE_CANONICAL_LIBREWINFORMS && !PROGPU_WPF_BOOTSTRAP_WPF
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void InitializeFormsOnly()
    {
        var startup = global::LibreWinForms.ProGPU.ProGpuStartupOptions.ParseArguments(
            global::System.MemoryExtensions.AsSpan(global::System.Environment.GetCommandLineArgs(), 1));
        InitializeWindowsForms(startup.EnableNativeModalSessions);
    }
#endif

#if PROGPU_WPF_BOOTSTRAP_WPF
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool ReadWpfStartupOptions()
        => global::System.Windows.Media.ProGPU.ProGpuWpfStartupOptions.ParseArguments(
            global::System.MemoryExtensions.AsSpan(global::System.Environment.GetCommandLineArgs(), 1)).EnableNativeModalSessions;

    // Keep all WPF type references behind the entry-assembly and Windows
    // checks. The JIT must not resolve these dependencies on the early return.
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void InitializeWpf(bool enableNativeModalSessions)
    {
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
        InitializeWindowsForms(enableNativeModalSessions);
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
                        EnableNativeModalSessions = enableNativeModalSessions,
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
        if (enableNativeModalSessions)
        {
            bool registered = global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation(
                window => new global::System.Windows.Media.ProGPU.ProGpuWpfWindowHost(
                    global::System.Windows.Media.ProGPU.WpfPortableWindowActivation.CreateHostOptions(
                        window,
                        new global::System.Windows.Media.ProGPU.ProGpuWpfWindowOptions
                        {
                            EnableNativeModalSessions = enableNativeModalSessions
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

#if PROGPU_WPF_USE_LIBREWINFORMS
    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void EnableWindowsFormsInterop()
        => global::System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
#endif
#endif
}
