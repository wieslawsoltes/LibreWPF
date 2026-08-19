using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;

internal static class Program
{
    private const string LibreWpfPackageVersion = "0.1.0-preview.42";
    private const string ProGpuPackageVersion = "0.1.0-preview.52";
    private const string PrepackagedProGpuDirectoryEnvironmentVariable = "PROGPU_WPF_PREPACKAGED_PROGPU_DIR";
    private const string SmokeTargetFramework = "net10.0-windows";
    private const string SmokeAssemblyName = "ProGPU.Wpf.SdkSwitchSmoke";
    private const string LibraryAssemblyName = "ProGPU.Wpf.SdkSwitchLibrary";
    private const string AppTypeName = "ProGPU.Wpf.SdkSwitchSmoke.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.SdkSwitchSmoke.MainWindow";
    private const string PortableMediaContextRenderServiceTypeName = "System.Windows.Media.PortableMediaContextRenderService";
    private const string PortableClipboardServiceTypeName = "System.Windows.PortableClipboardService";
    private const string PortableFileDialogServiceTypeName = "Microsoft.Win32.PortableFileDialogService";
    private const string PortableMessageBoxServiceTypeName = "System.Windows.PortableMessageBoxService";
    private const string PortablePresentationSourceTypeName = "System.Windows.PortablePresentationSource";
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private static readonly string[] RequiredWpfRuntimeAssemblies =
    [
        "WindowsBase",
        "System.Xaml",
        "PresentationCore",
        "PresentationFramework",
        "PresentationUI",
        "ReachFramework",
        "UIAutomationTypes",
        "UIAutomationProvider",
        "System.Windows.Input.Manipulations",
        "System.Windows.Primitives",
        "PresentationFramework.Aero",
        "PresentationFramework.Aero2",
        "PresentationFramework.AeroLite",
        "PresentationFramework.Classic",
        "PresentationFramework.Fluent",
        "PresentationFramework.Luna",
        "PresentationFramework.Royale",
        "System.Windows.Controls.Ribbon"
    ];
    private static readonly string[] ProGpuRuntimeAssemblies =
    [
        "ProGPU.Wpf",
        "ProGPU.Wpf.Interop",
        "ProGPU.Backend",
        "ProGPU.DirectX",
        "ProGPU.Scene",
        "ProGPU.Vector",
        "ProGPU.Text",
        "ProGPU.Compute",
        "ProGPU.Transpiler"
    ];
    private static readonly string[] SilkNetRuntimeAssemblies =
    [
        "Silk.NET.Core",
        "Silk.NET.GLFW",
        "Silk.NET.Input.Common",
        "Silk.NET.Input.Glfw",
        "Silk.NET.Maths",
        "Silk.NET.WebGPU",
        "Silk.NET.Windowing.Common",
        "Silk.NET.Windowing.Glfw"
    ];
    private static readonly string[] SupportPackageRuntimeAssemblies =
    [
        "System.Configuration.ConfigurationManager",
        "System.Diagnostics.EventLog",
        "System.Formats.Nrbf",
        "System.IO.Packaging",
        "System.Security.Cryptography.ProtectedData",
        "System.Private.Windows.Core",
        "System.Windows.Extensions",
        "OpenFontSharp"
    ];

    public sealed class PortableClipboardJsonPayload
    {
        public string? Message { get; set; }

        public int Count { get; set; }
    }

    [STAThread]
    private static int Main()
    {
        try
        {
            SmokeInputs inputs = ResolveSmokeInputs();
            ValidateProGpuHiDpiRenderSurface(inputs);
            RunObjectGraphSmoke(inputs);
            RunSdkPortableBootstrapSmoke(inputs);
            RunApplicationRunSmoke(inputs);

            Console.WriteLine("ProGPU WPF SDK switch runtime smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static SmokeInputs ResolveSmokeInputs()
    {
        string repoRoot = FindRepoRoot();
        string packageFeed = Path.Combine(repoRoot, "artifacts", "packages", "Release", "NonShipping");
        string appOutputRoot = Path.Combine(
            repoRoot,
            "artifacts",
            "bin",
            SmokeAssemblyName,
            "Debug",
            SmokeTargetFramework);
        string smokeAssemblyPath = Path.Combine(appOutputRoot, SmokeAssemblyName + ".dll");
        string releasePackagedWpfRoot = Path.Combine(
            repoRoot,
            "artifacts",
            "packaging",
            "Release",
            "LibreWPF.Transport",
            "lib",
            "net10.0");
        string debugPackagedWpfRoot = Path.Combine(
            repoRoot,
            "artifacts",
            "packaging",
            "Debug",
            "LibreWPF.Transport.Debug",
            "lib",
            "net10.0");
        string wpfRoot = Directory.Exists(releasePackagedWpfRoot)
            ? releasePackagedWpfRoot
            : Directory.Exists(debugPackagedWpfRoot)
                ? debugPackagedWpfRoot
            : Path.Combine(repoRoot, "artifacts", "progpu-wpf-sdk-smoke", "wpf");
        string proGpuRoot = Path.Combine(repoRoot, "artifacts", "progpu-wpf-sdk-smoke", "progpu");
        string? prepackagedProGpuDirectory =
            Environment.GetEnvironmentVariable(PrepackagedProGpuDirectoryEnvironmentVariable);

        RequireDirectory(packageFeed, "local package feed");
        RequireFile(smokeAssemblyPath, "SDK switch smoke assembly");
        RequireFile(
            Path.Combine(appOutputRoot, LibraryAssemblyName + ".dll"),
            "SDK switch library assembly");
        ValidateLocalProGpuPackageProvenance(
            repoRoot,
            packageFeed,
            prepackagedProGpuDirectory);
        ValidateLocalWpfPackageMatchesAvailableRepositoryBuilds(wpfRoot, packageFeed);
        RequireOutputRuntimeAssets(appOutputRoot, packageFeed);

        return new SmokeInputs(repoRoot, appOutputRoot, smokeAssemblyPath, wpfRoot, proGpuRoot);
    }

    private static void ValidateLocalProGpuPackageProvenance(
        string repoRoot,
        string packageFeed,
        string? prepackagedProGpuDirectory)
    {
        if (!string.IsNullOrWhiteSpace(prepackagedProGpuDirectory))
        {
            RequireDirectory(prepackagedProGpuDirectory, "exact prepackaged ProGPU package source");
        }

        foreach (string assemblyName in ProGpuRuntimeAssemblies)
        {
            if (!string.IsNullOrWhiteSpace(prepackagedProGpuDirectory) &&
                !string.Equals(assemblyName, "ProGPU.Wpf", StringComparison.Ordinal))
            {
                ValidateLocalPackageMatchesPrepackagedSource(
                    packageFeed,
                    prepackagedProGpuDirectory,
                    GetPackageIdForRuntimeAssembly(assemblyName));
                continue;
            }

            string repositoryAssemblyPath = GetRepositoryProGpuAssemblyPath(repoRoot, assemblyName);
            if (!File.Exists(repositoryAssemblyPath))
            {
                continue;
            }

            ValidateLocalPackageAssemblyMatchesFile(
                packageFeed,
                GetPackageIdForRuntimeAssembly(assemblyName),
                assemblyName,
                "net10.0",
                repositoryAssemblyPath,
                $"repository Release {assemblyName}.dll");
        }
    }

    private static void ValidateLocalPackageMatchesPrepackagedSource(
        string packageFeed,
        string prepackagedProGpuDirectory,
        string packageId)
    {
        string packageVersion = GetPackageVersion(packageId);
        string localPackagePath = Path.Combine(packageFeed, $"{packageId}.{packageVersion}.nupkg");
        string prepackagedSourcePath = Path.Combine(
            prepackagedProGpuDirectory,
            $"{packageId}.{packageVersion}.nupkg");

        RequireFile(localPackagePath, $"{packageId} local package");
        RequireFile(prepackagedSourcePath, $"{packageId} exact prepackaged source");
        AssertEqual(
            ComputeFileSha256(prepackagedSourcePath),
            ComputeFileSha256(localPackagePath),
            $"local {packageId} package matches exact prepackaged source");
    }

    private static void ValidateLocalWpfPackageMatchesAvailableRepositoryBuilds(string wpfRoot, string packageFeed)
    {
        if (!Directory.Exists(wpfRoot))
        {
            return;
        }

        foreach (string assemblyName in RequiredWpfRuntimeAssemblies)
        {
            string repositoryAssemblyPath = Path.Combine(wpfRoot, assemblyName + ".dll");
            if (!File.Exists(repositoryAssemblyPath))
            {
                continue;
            }

            ValidateLocalPackageAssemblyMatchesFile(
                packageFeed,
                "LibreWPF.Transport",
                assemblyName,
                "net10.0",
                repositoryAssemblyPath,
                $"repository WPF transport {assemblyName}.dll");
        }
    }

    private static string GetPackageIdForRuntimeAssembly(string assemblyName)
    {
        return assemblyName switch
        {
            "ProGPU.Wpf" => "LibreWPF.ProGPU",
            "ProGPU.Wpf.Interop" => "LibreWPF.Interop",
            _ => assemblyName
        };
    }

    private static string GetPackageVersion(string packageId)
    {
        return packageId is "LibreWPF.Transport" or "LibreWPF.ProGPU"
            ? LibreWpfPackageVersion
            : ProGpuPackageVersion;
    }

    private static void ValidateLocalPackageAssemblyMatchesFile(
        string packageFeed,
        string packageId,
        string assemblySimpleName,
        string targetFramework,
        string expectedAssemblyPath,
        string expectedAssemblyDescription)
    {
        string packageVersion = GetPackageVersion(packageId);
        string packagePath = Path.Combine(packageFeed, $"{packageId}.{packageVersion}.nupkg");
        string packageEntryName = $"lib/{targetFramework}/{assemblySimpleName}.dll";

        RequireFile(packagePath, $"{packageId} local package");

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = RequirePackageEntry(
            package,
            packageEntryName,
            $"{packageId}/{assemblySimpleName} runtime assembly");

        using Stream packageStream = entry.Open();
        string packageHash = ComputeStreamSha256(packageStream);
        string repositoryHash = ComputeFileSha256(expectedAssemblyPath);
        AssertEqual(
            repositoryHash,
            packageHash,
            $"local {packageId} package matches {expectedAssemblyDescription}");
    }

    private static string GetRepositoryProGpuAssemblyPath(string repoRoot, string assemblySimpleName)
    {
        if (string.Equals(assemblySimpleName, "ProGPU.Wpf", StringComparison.Ordinal))
        {
            return Path.Combine(
                repoRoot,
                "src",
                "ProGPU.Wpf",
                "bin",
                "Release",
                "net10.0",
                assemblySimpleName + ".dll");
        }

        return Path.Combine(
            repoRoot,
            "external",
            "ProGPU",
            "src",
            assemblySimpleName,
            "bin",
            "Release",
            "net10.0",
            assemblySimpleName + ".dll");
    }

    private static void RequireOutputRuntimeAssets(string appOutputRoot, string packageFeed)
    {
        foreach (string assemblyName in RequiredWpfRuntimeAssemblies.Concat(ProGpuRuntimeAssemblies).Concat(SilkNetRuntimeAssemblies).Concat(SupportPackageRuntimeAssemblies))
        {
            RequireFile(
                Path.Combine(appOutputRoot, assemblyName + ".dll"),
                $"SDK switch output runtime asset '{assemblyName}.dll'");
        }

        foreach (string assemblyName in ProGpuRuntimeAssemblies)
        {
            RequireOutputAssemblyMatchesLocalPackage(
                appOutputRoot,
                packageFeed,
                GetPackageIdForRuntimeAssembly(assemblyName),
                assemblyName,
                "net10.0");
        }

        foreach (string assemblyName in RequiredWpfRuntimeAssemblies)
        {
            RequireOutputAssemblyMatchesLocalPackage(
                appOutputRoot,
                packageFeed,
                "LibreWPF.Transport",
                assemblyName,
                "net10.0");
        }

        RequireAnyFile(
            appOutputRoot,
            GetNativeAssetCandidates("wgpu"),
            "SDK switch output native WebGPU runtime asset");
        RequireAnyFile(
            appOutputRoot,
            GetNativeAssetCandidates("glfw"),
            "SDK switch output native GLFW runtime asset");
    }

    private static void RequireOutputAssemblyMatchesLocalPackage(
        string appOutputRoot,
        string packageFeed,
        string packageId,
        string assemblySimpleName,
        string targetFramework)
    {
        string outputPath = Path.Combine(appOutputRoot, assemblySimpleName + ".dll");
        string packageVersion = GetPackageVersion(packageId);
        string packagePath = Path.Combine(packageFeed, $"{packageId}.{packageVersion}.nupkg");
        string packageEntryName = $"lib/{targetFramework}/{assemblySimpleName}.dll";

        RequireFile(outputPath, $"SDK switch output runtime asset '{assemblySimpleName}.dll'");
        RequireFile(packagePath, $"{packageId} local package");

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = RequirePackageEntry(
            package,
            packageEntryName,
            $"{packageId}/{assemblySimpleName} runtime assembly");

        using Stream packageStream = entry.Open();
        string packageHash = ComputeStreamSha256(packageStream);
        string outputHash = ComputeFileSha256(outputPath);
        AssertEqual(
            packageHash,
            outputHash,
            $"SDK switch output {assemblySimpleName}.dll matches local {packageId} package");
    }

    private static ZipArchiveEntry RequirePackageEntry(ZipArchive package, string entryName, string description)
    {
        ZipArchiveEntry? entry = package.GetEntry(entryName);
        if (entry is null)
        {
            throw new FileNotFoundException($"Missing {description} package entry: {entryName}", entryName);
        }

        return entry;
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return ComputeStreamSha256(stream);
    }

    private static string ComputeStreamSha256(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static string[] GetNativeAssetCandidates(string assetName)
    {
        return assetName switch
        {
            "wgpu" when OperatingSystem.IsWindows() => ["wgpu_native.dll"],
            "wgpu" when OperatingSystem.IsMacOS() => ["libwgpu_native.dylib"],
            "wgpu" => ["libwgpu_native.so"],
            "glfw" when OperatingSystem.IsWindows() => ["glfw3.dll"],
            "glfw" when OperatingSystem.IsMacOS() => ["libglfw.3.dylib"],
            "glfw" => ["libglfw.so.3"],
            _ => throw new ArgumentOutOfRangeException(nameof(assetName), assetName, null)
        };
    }

    private static IEnumerable<string> GetUnmanagedDllCandidates(string unmanagedDllName)
    {
        yield return Path.GetFileName(unmanagedDllName);

        if (unmanagedDllName.Contains("glfw", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string candidate in GetNativeAssetCandidates("glfw"))
            {
                yield return candidate;
            }
        }

        if (unmanagedDllName.Contains("wgpu", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string candidate in GetNativeAssetCandidates("wgpu"))
            {
                yield return candidate;
            }
        }

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(unmanagedDllName);
        if (OperatingSystem.IsWindows())
        {
            yield return nameWithoutExtension + ".dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "lib" + nameWithoutExtension + ".dylib";
        }
        else
        {
            yield return "lib" + nameWithoutExtension + ".so";
        }
    }

    private static void RunObjectGraphSmoke(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(inputs.SmokeAssemblyPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationCore"));
        Assembly presentationFramework = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationFramework"));
        PreloadSdkWindowingPlatform(loadContext, inputs.AppOutputRoot);
        RuntimeHelpers.RunModuleConstructor(smokeAssembly.ManifestModule.ModuleHandle);

        ValidateSdkLooseXamlReaderWriter(presentationFramework, presentationCore);
        ValidatePortableClipboard(presentationCore);
        ValidatePortableFileDialogs(presentationFramework);
        RegisterPortableMessageBox(presentationFramework);

        object app = Create(smokeAssembly, AppTypeName);
        try
        {
            InvokeVoid(app, "InitializeComponent");
            ValidateApp(app);
            ValidatePortableSystemParameters(presentationFramework, app);

            object window = Create(smokeAssembly, MainWindowTypeName);
            ValidateWindow(window, validateFrameContent: false, flushDispatcherOperations: null);
            ValidatePortableInputLanguageManager(presentationCore, window);
            ValidatePortableInputMethod(presentationCore, window);
            ValidatePortableWindowChrome(presentationFramework, window);
            ValidatePortableSystemCommands(presentationFramework, window);
            ValidatePortableMessageBox(presentationFramework, window);
        }
        finally
        {
            TryInvoke(app, "Shutdown");
            ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName);
            ClearPortableService(presentationCore, PortableClipboardServiceTypeName);
        }
    }

    private static void RunSdkPortableBootstrapSmoke(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(inputs.SmokeAssemblyPath);
        Type bootstrapType = smokeAssembly.GetType(
            "ProGPU.Wpf.Sdk.ProGpuWpfSdkPortableBootstrap",
            throwOnError: true)!;

        RuntimeHelpers.RunModuleConstructor(smokeAssembly.ManifestModule.ModuleHandle);

        Assembly presentationFramework = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationFramework"));
        Type activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        try
        {
            AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "SDK portable bootstrap activation enabled");
            Type messageBoxServiceType = GetRequiredType(presentationFramework, PortableMessageBoxServiceTypeName);
            AssertEqual(true, GetStaticProperty(messageBoxServiceType, "IsEnabled"), "SDK portable bootstrap MessageBox enabled");
            Type fileDialogServiceType = GetRequiredType(presentationFramework, PortableFileDialogServiceTypeName);
            AssertEqual(true, GetStaticProperty(fileDialogServiceType, "IsEnabled"), "SDK portable bootstrap file dialog enabled");
            AssertEqual(
                true,
                loadContext.Assemblies.Any(assembly => string.Equals(assembly.GetName().Name, "ProGPU.Wpf", StringComparison.Ordinal)),
                "SDK portable bootstrap loaded ProGPU.Wpf");
            AssertEqual("ProGPU.Wpf.Sdk", bootstrapType.Namespace ?? string.Empty, "SDK portable bootstrap namespace");
        }
        finally
        {
            ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName);
            ClearPortableService(presentationFramework, PortableFileDialogServiceTypeName);
            ClearPortableActivation(activationServiceType);
        }
    }

    private static void RunApplicationRunSmoke(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly smokeAssembly = loadContext.LoadFromAssemblyPath(inputs.SmokeAssemblyPath);
        Assembly presentationCore = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationCore"));
        Assembly presentationFramework = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationFramework"));
        PreloadSdkWindowingPlatform(loadContext, inputs.AppOutputRoot);
        RuntimeHelpers.RunModuleConstructor(smokeAssembly.ManifestModule.ModuleHandle);

        ValidateSdkLooseXamlReaderWriter(presentationFramework, presentationCore);

        object? app = null;
        SdkApplicationRunRecorder? recorder = null;
        Type? activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        bool runCompleted = false;

        try
        {
            ClearPortableActivation(activationServiceType);

            app = Create(smokeAssembly, AppTypeName);
            InvokeVoid(app, "InitializeComponent");
            ValidateApp(app);
            ValidatePortableSystemParameters(presentationFramework, app);
            ValidatePortableClipboard(presentationCore);
            ValidatePortableFileDialogs(presentationFramework);
            RegisterPortableMessageBox(presentationFramework);

            recorder = RegisterPortableActivation(
                presentationFramework,
                presentationCore,
                app,
                out activationServiceType);
            recorder.AssertRegistered();

            object exitCode = Invoke(app, "Run");
            runCompleted = true;
            AssertEqual(0, exitCode, "Application.Run exit code");
            ValidateApplicationRunLifetime(app);
            recorder.ValidateAfterRun();
        }
        finally
        {
            recorder?.Dispose();
            ClearPortableActivation(activationServiceType);
            ClearPortableService(presentationFramework, PortableMessageBoxServiceTypeName);
            ClearPortableService(presentationFramework, PortableFileDialogServiceTypeName);
            ClearPortableService(presentationCore, PortableClipboardServiceTypeName);

            if (!runCompleted && app is not null)
            {
                TryInvoke(app, "Shutdown");
            }
        }
    }

    private static SdkSmokeLoadContext CreateLoadContext(SmokeInputs inputs)
    {
        return new SdkSmokeLoadContext(
            inputs.RepoRoot,
            inputs.AppOutputRoot,
            inputs.SmokeAssemblyPath,
            inputs.WpfRoot,
            inputs.ProGpuRoot);
    }

    private static void ValidateProGpuHiDpiRenderSurface(SmokeInputs inputs)
    {
        using var loadContext = CreateLoadContext(inputs);
        Assembly proGpuWpf = loadContext.LoadFromAssemblyName(new AssemblyName("ProGPU.Wpf"));
        Assembly proGpuWpfInterop = loadContext.LoadFromAssemblyName(new AssemblyName("ProGPU.Wpf.Interop"));
        Assembly proGpuScene = loadContext.LoadFromAssemblyName(new AssemblyName("ProGPU.Scene"));
        Assembly proGpuBackend = loadContext.LoadFromAssemblyName(new AssemblyName("ProGPU.Backend"));
        Assembly proGpuVector = loadContext.LoadFromAssemblyName(new AssemblyName("ProGPU.Vector"));
        Assembly silkNetMaths = loadContext.LoadFromAssemblyName(new AssemblyName("Silk.NET.Maths"));
        Assembly silkNetWebGpu = loadContext.LoadFromAssemblyName(new AssemblyName("Silk.NET.WebGPU"));
        Assembly windowsBase = loadContext.LoadFromAssemblyName(new AssemblyName("WindowsBase"));
        Assembly presentationCore = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationCore"));
        Assembly presentationFramework = loadContext.LoadFromAssemblyName(new AssemblyName("PresentationFramework"));
        RegisterSdkNativeResolver(silkNetWebGpu, inputs.AppOutputRoot);
        RegisterSdkNativeResolver(proGpuBackend, inputs.AppOutputRoot);

        Type displayScaleResolverType = GetRequiredType(proGpuBackend, "ProGPU.Backend.DisplayScaleResolver");
        AssertDisplayScaleResolver(displayScaleResolverType, "SDK");

        Type windowHostType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfWindowHost");
        AssertPackagedRetinaStartupResizeKeepsLogicalSurface(
            windowHostType,
            displayScaleResolverType,
            silkNetMaths,
            "SDK");
        AssertPropertyType(windowHostType, "Width", typeof(int), "SDK ProGPU WPF host logical width property");
        AssertPropertyType(windowHostType, "Height", typeof(int), "SDK ProGPU WPF host logical height property");
        AssertPropertyType(windowHostType, "Left", typeof(int?), "SDK ProGPU WPF host left property");
        AssertPropertyType(windowHostType, "Top", typeof(int?), "SDK ProGPU WPF host top property");
        AssertPropertyType(windowHostType, "Topmost", typeof(bool), "SDK ProGPU WPF host topmost property");
        Type windowBorderType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfWindowBorder");
        AssertPropertyType(windowHostType, "WindowBorder", windowBorderType, "SDK ProGPU WPF host window border property");

        MethodInfo setClientSize = windowHostType.GetMethod(
            "SetClientSize",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(int), typeof(int)],
            modifiers: null)
            ?? throw new MissingMethodException(windowHostType.FullName, "SetClientSize");
        AssertEqual(2, setClientSize.GetParameters().Length, "SDK ProGPU WPF host client-size method parameter count");

        MethodInfo setPosition = windowHostType.GetMethod(
            "SetPosition",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(int), typeof(int)],
            modifiers: null)
            ?? throw new MissingMethodException(windowHostType.FullName, "SetPosition");
        AssertEqual(2, setPosition.GetParameters().Length, "SDK ProGPU WPF host position method parameter count");

        MethodInfo setTopmost = windowHostType.GetMethod(
            "SetTopmost",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(bool)],
            modifiers: null)
            ?? throw new MissingMethodException(windowHostType.FullName, "SetTopmost");
        AssertEqual(1, setTopmost.GetParameters().Length, "SDK ProGPU WPF host topmost method parameter count");

        MethodInfo setWindowBorder = windowHostType.GetMethod(
            "SetWindowBorder",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [windowBorderType],
            modifiers: null)
            ?? throw new MissingMethodException(windowHostType.FullName, "SetWindowBorder");
        AssertEqual(1, setWindowBorder.GetParameters().Length, "SDK ProGPU WPF host window border method parameter count");

        Type portablePresentationSourceType = GetRequiredType(presentationCore, "System.Windows.PortablePresentationSource");
        MethodInfo setPortableClientSize = portablePresentationSourceType.GetMethod(
            "SetClientSize",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(double), typeof(double)],
            modifiers: null)
            ?? throw new MissingMethodException(portablePresentationSourceType.FullName, "SetClientSize");
        AssertEqual(typeof(void), setPortableClientSize.ReturnType, "SDK portable presentation source client-size return type");

        Type portableActivationType = GetRequiredType(presentationFramework, "System.Windows.PortableWindowActivationService");
        MethodInfo setPortablePosition = portableActivationType.GetMethod(
            "SetPosition",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(object), typeof(double), typeof(double)],
            modifiers: null)
            ?? throw new MissingMethodException(portableActivationType.FullName, "SetPosition");
        AssertEqual(typeof(void), setPortablePosition.ReturnType, "SDK portable window activation position return type");

        MethodInfo setPortableTopmost = portableActivationType.GetMethod(
            "SetTopmost",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(object), typeof(bool)],
            modifiers: null)
            ?? throw new MissingMethodException(portableActivationType.FullName, "SetTopmost");
        AssertEqual(typeof(void), setPortableTopmost.ReturnType, "SDK portable window activation topmost return type");

        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type renderTargetViewportType = GetRequiredType(proGpuScene, "ProGPU.Scene.RenderTargetViewport");
        MethodInfo compositionRender = FindMethodByParameterNames(
            compositionTargetType,
            "Render",
            ["logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "dpiScale", "targetView"]);
        AssertParameterTypes(
            compositionRender,
            [typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(float)],
            "SDK ProGPU WPF composition render logical/physical surface");
        AssertEqual(true, compositionRender.GetParameters()[5].ParameterType.IsPointer, "SDK ProGPU WPF composition render target view pointer");
        MethodInfo compositionViewportRender = FindMethodByParameterNames(
            compositionTargetType,
            "Render",
            ["logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "renderTargetViewport", "dpiScale", "targetView"]);
        AssertParameterTypes(
            compositionViewportRender,
            [typeof(uint), typeof(uint), typeof(uint), typeof(uint), renderTargetViewportType, typeof(float)],
            "SDK ProGPU WPF composition render viewport surface");
        AssertEqual(true, compositionViewportRender.GetParameters()[6].ParameterType.IsPointer, "SDK ProGPU WPF composition render viewport target view pointer");
        MethodInfo hostPresent = FindMethodByParameterNames(
            windowHostType,
            "Present",
            ["logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "viewportX", "viewportY", "viewportWidth", "viewportHeight", "dpiScale"]);
        AssertMethodCallsSpecificMethod(
            hostPresent,
            compositionViewportRender,
            "SDK ProGPU WPF host present viewport render overload");
        MethodInfo synchronizeGeometry = FindMethodByParameterNames(
            windowHostType,
            "SynchronizePortablePresentationSourceGeometry",
            ["geometry"]);
        AssertMethodCallsMethod(
            synchronizeGeometry,
            windowHostType.FullName ?? string.Empty,
            "UpdatePortablePresentationSourceClientSize",
            "SDK ProGPU WPF host portable source logical-size synchronization");
        AssertMethodCallsMethod(
            synchronizeGeometry,
            windowHostType.FullName ?? string.Empty,
            "UpdatePortablePresentationSourceDpiScale",
            "SDK ProGPU WPF host portable source DPI synchronization");
        MethodInfo resolveCachedLogicalDimension = FindMethodByParameterNames(
            windowHostType,
            "ResolveCachedLogicalClientDimension",
            ["portablePresentationSourceDimension", "requestedLogicalDimension", "currentClientDimension"]);
        AssertEqual(
            typeof(int),
            resolveCachedLogicalDimension.ReturnType,
            "SDK ProGPU WPF host typed logical-size cache return type");
        MethodInfo resolveMonitorDpiScale = FindMethodByParameterNames(
            windowHostType,
            "ResolveMonitorDpiScaleWithPlatformFallback",
            ["monitorDpiScale", "platformDpiScaleProvider"]);
        AssertMethodCallsMethod(
            resolveMonitorDpiScale,
            displayScaleResolverType.FullName ?? string.Empty,
            "ResolveDisplayScaleWithPlatformFallback",
            "SDK ProGPU WPF host delegates display-scale fallback to ProGPU backend");

        Type compositorType = GetRequiredType(proGpuScene, "ProGPU.Scene.Compositor");
        Type visualType = GetRequiredType(proGpuScene, "ProGPU.Scene.Visual");
        MethodInfo compositorRenderScene = FindMethodByParameterNames(
            compositorType,
            "RenderScene",
            ["root", "logicalWidth", "logicalHeight", "renderTargetWidth", "renderTargetHeight", "dpiScale", "targetView"]);
        AssertParameterTypes(
            compositorRenderScene,
            [visualType, typeof(uint), typeof(uint), typeof(uint), typeof(uint), typeof(float)],
            "SDK ProGPU compositor render logical/physical surface");
        AssertEqual(true, compositorRenderScene.GetParameters()[6].ParameterType.IsPointer, "SDK ProGPU compositor render target view pointer");
        MethodInfo compositorViewportRenderScene = FindMethodByParameterNames(
            compositorType,
            "RenderScene",
            ["root", "logicalWidth", "logicalHeight", "renderTargetWidth", "renderTargetHeight", "renderTargetViewport", "dpiScale", "targetView"]);
        AssertParameterTypes(
            compositorViewportRenderScene,
            [visualType, typeof(uint), typeof(uint), typeof(uint), typeof(uint), renderTargetViewportType, typeof(float)],
            "SDK ProGPU compositor render viewport surface");
        AssertEqual(true, compositorViewportRenderScene.GetParameters()[7].ParameterType.IsPointer, "SDK ProGPU compositor render viewport target view pointer");
        AssertMethodCallsSpecificMethod(
            compositionRender,
            compositionViewportRender,
            "SDK ProGPU WPF composition render delegates to viewport render surface");
        AssertMethodCallsSpecificMethod(
            compositionViewportRender,
            compositorViewportRenderScene,
            "SDK ProGPU WPF composition target forwards viewport render surface");
        AssertPropertyGetterReferencesField(
            compositorType,
            "CurrentCanvasPixelX",
            "_explicitRenderTargetViewport",
            "SDK ProGPU compositor canvas pixel X viewport origin");
        AssertPropertyGetterReferencesField(
            compositorType,
            "CurrentCanvasPixelY",
            "_explicitRenderTargetViewport",
            "SDK ProGPU compositor canvas pixel Y viewport origin");
        AssertPropertyGetterReferencesField(
            compositorType,
            "CurrentCanvasPixelWidth",
            "_explicitRenderTargetWidth",
            "SDK ProGPU compositor canvas pixel width explicit render target");
        AssertPropertyGetterReferencesField(
            compositorType,
            "CurrentCanvasPixelHeight",
            "_explicitRenderTargetHeight",
            "SDK ProGPU compositor canvas pixel height explicit render target");
        MethodInfo compositorPhysicalRenderScene = FindMethodByParameterNames(
            compositorType,
            "RenderScene",
            ["root", "width", "height", "targetView"]);
        MethodInfo compositorPhysicalRenderSceneCore = FindMethodByParameterNames(
            compositorType,
            "RenderSceneCore",
            ["root", "width", "height", "targetView"]);
        MethodInfo applyRenderPassViewport = FindMethodByNameAndParameterCount(
            compositorType,
            "ApplyRenderPassViewport",
            4);
        AssertMethodCallsSpecificMethod(
            compositorPhysicalRenderScene,
            compositorPhysicalRenderSceneCore,
            "SDK ProGPU compositor physical render delegates to the retryable render core");
        AssertMethodCallsMethod(
            compositorPhysicalRenderSceneCore,
            compositorType.FullName ?? string.Empty,
            "ApplyRenderPassViewport",
            "SDK ProGPU compositor render pass viewport application");
        AssertMethodCallsMethod(
            applyRenderPassViewport,
            "ProGPU.Backend.IWebGpuApi",
            "RenderPassEncoderSetViewport",
            "SDK ProGPU compositor backend-independent physical render target viewport");
        AssertRetainedWpfLayerUsesLogicalBoundsAndIdentityScale(proGpuWpf, proGpuScene, "SDK");
        AssertPackagedHighDpiRetainedWpfPixelsFillPhysicalTarget(
            inputs.AppOutputRoot,
            proGpuWpf,
            proGpuBackend,
            presentationCore,
            presentationFramework,
            windowsBase,
            silkNetWebGpu,
            "SDK");
        AssertPackagedObjectRenderDataRectangleFillsPhysicalTarget(
            inputs.AppOutputRoot,
            proGpuWpf,
            proGpuWpfInterop,
            proGpuBackend,
            presentationCore,
            windowsBase,
            silkNetWebGpu,
            "SDK");
        AssertPackagedLegacyRenderOverloadFillsPhysicalTarget(
            inputs.AppOutputRoot,
            proGpuWpf,
            proGpuBackend,
            proGpuScene,
            proGpuVector,
            silkNetWebGpu,
            "SDK");
    }

    private static void AssertDisplayScaleResolver(Type displayScaleResolverType, string descriptionPrefix)
    {
        MethodInfo resolveWindowDisplayScale = FindMethodByParameterNames(
            displayScaleResolverType,
            "ResolveWindowDisplayScale",
            ["window", "monitorDpiScale"]);
        AssertEqual(typeof(double), resolveWindowDisplayScale.ReturnType, $"{descriptionPrefix} ProGPU backend window display-scale return type");
        AssertEqual(2, resolveWindowDisplayScale.GetParameters().Length, $"{descriptionPrefix} ProGPU backend window display-scale parameter count");

        MethodInfo normalizeDisplayScale = displayScaleResolverType.GetMethod(
            "NormalizeDisplayScale",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            [typeof(double)],
            modifiers: null)
            ?? throw new MissingMethodException(displayScaleResolverType.FullName, "NormalizeDisplayScale");
        AssertEqual(1.0, InvokeRequired(normalizeDisplayScale, [0.0]), $"{descriptionPrefix} ProGPU backend invalid display-scale normalization");
        AssertEqual(1.5, InvokeRequired(normalizeDisplayScale, [1.5]), $"{descriptionPrefix} ProGPU backend valid display-scale normalization");

        MethodInfo resolveDisplayScaleWithPlatformFallback = displayScaleResolverType.GetMethod(
            "ResolveDisplayScaleWithPlatformFallback",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            [typeof(double), typeof(Func<double?>)],
            modifiers: null)
            ?? throw new MissingMethodException(displayScaleResolverType.FullName, "ResolveDisplayScaleWithPlatformFallback");
        AssertEqual(
            2.0,
            InvokeRequired(resolveDisplayScaleWithPlatformFallback, [1.0, new Func<double?>(() => 2.0)]),
            $"{descriptionPrefix} ProGPU backend native display-scale fallback");
        AssertEqual(
            1.5,
            InvokeRequired(resolveDisplayScaleWithPlatformFallback, [1.5, new Func<double?>(() => 2.0)]),
            $"{descriptionPrefix} ProGPU backend monitor display-scale precedence");
    }

    private static object InvokeRequired(MethodInfo method, object?[] parameters)
    {
        return method.Invoke(null, parameters)
            ?? throw new InvalidOperationException($"Expected {method.DeclaringType?.FullName}.{method.Name} to return a value.");
    }

    private static void AssertPackagedRetinaStartupResizeKeepsLogicalSurface(
        Type windowHostType,
        Type displayScaleResolverType,
        Assembly silkNetMaths,
        string descriptionPrefix)
    {
        Type windowOptionsType = GetRequiredType(windowHostType.Assembly, "System.Windows.Media.ProGPU.ProGpuWpfWindowOptions");
        Type vector2DIntType = GetRequiredType(silkNetMaths, "Silk.NET.Maths.Vector2D`1").MakeGenericType(typeof(int));
        object options = Create(windowOptionsType);
        SetProperty(options, "Width", 420);
        SetProperty(options, "Height", 840);
        object host = Create(windowHostType, options);

        try
        {
            MethodInfo resolveDisplayScale = displayScaleResolverType.GetMethod(
                "ResolveDisplayScaleWithPlatformFallback",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                [typeof(double), typeof(Func<double?>)],
                modifiers: null)
                ?? throw new MissingMethodException(displayScaleResolverType.FullName, "ResolveDisplayScaleWithPlatformFallback");
            object dpiScale = InvokeRequired(resolveDisplayScale, [1.0, new Func<double?>(() => 2.0)]);
            AssertEqual(2.0, dpiScale, $"{descriptionPrefix} packaged Retina startup display scale");

            object nativeLogicalSize = Create(vector2DIntType, 420, 840);
            object retinaFramebufferSize = Create(vector2DIntType, 840, 1680);
            MethodInfo updateNativeResize = windowHostType.GetMethod(
                "UpdateClientSizeFromNativeResize",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                [vector2DIntType, vector2DIntType, typeof(double)],
                modifiers: null)
                ?? throw new MissingMethodException(windowHostType.FullName, "UpdateClientSizeFromNativeResize");
            InvokeMethod(updateNativeResize, host, nativeLogicalSize, retinaFramebufferSize, dpiScale);
            AssertEqual(420, GetProperty(host, "Width"), $"{descriptionPrefix} packaged Retina startup logical host width");
            AssertEqual(840, GetProperty(host, "Height"), $"{descriptionPrefix} packaged Retina startup logical host height");

            SetField(host, "_clientWidth", 840);
            SetField(host, "_clientHeight", 1680);
            SetField(host, "_requestedLogicalClientWidth", 840);
            SetField(host, "_requestedLogicalClientHeight", 1680);
            InvokeMethod(updateNativeResize, host, nativeLogicalSize, retinaFramebufferSize, dpiScale);
            AssertEqual(420, GetProperty(host, "Width"), $"{descriptionPrefix} packaged Retina polluted-cache logical host width");
            AssertEqual(840, GetProperty(host, "Height"), $"{descriptionPrefix} packaged Retina polluted-cache logical host height");

            MethodInfo resolveGeometry = windowHostType.GetMethod(
                "ResolveRenderSurfaceGeometry",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null,
                [typeof(int), typeof(int), vector2DIntType, typeof(double)],
                modifiers: null)
                ?? throw new MissingMethodException(windowHostType.FullName, "ResolveRenderSurfaceGeometry");
            object geometry = InvokeMethod(resolveGeometry, null, 420, 840, retinaFramebufferSize, dpiScale)
                ?? throw new InvalidOperationException($"{descriptionPrefix} packaged Retina render surface geometry was null.");
            AssertEqual(420u, GetProperty(geometry, "LogicalWidth"), $"{descriptionPrefix} packaged Retina render logical width");
            AssertEqual(840u, GetProperty(geometry, "LogicalHeight"), $"{descriptionPrefix} packaged Retina render logical height");
            AssertEqual(840u, GetProperty(geometry, "PixelWidth"), $"{descriptionPrefix} packaged Retina render pixel width");
            AssertEqual(1680u, GetProperty(geometry, "PixelHeight"), $"{descriptionPrefix} packaged Retina render pixel height");
            AssertEqual(0u, GetProperty(geometry, "ViewportX"), $"{descriptionPrefix} packaged Retina render viewport X");
            AssertEqual(0u, GetProperty(geometry, "ViewportY"), $"{descriptionPrefix} packaged Retina render viewport Y");
            AssertEqual(840u, GetProperty(geometry, "ViewportWidth"), $"{descriptionPrefix} packaged Retina render viewport width");
            AssertEqual(1680u, GetProperty(geometry, "ViewportHeight"), $"{descriptionPrefix} packaged Retina render viewport height");
            AssertEqual(2.0, GetProperty(geometry, "DpiScale"), $"{descriptionPrefix} packaged Retina render DPI scale");
        }
        finally
        {
            (host as IDisposable)?.Dispose();
        }
    }

    private static void AssertRetainedWpfLayerUsesLogicalBoundsAndIdentityScale(
        Assembly proGpuWpf,
        Assembly proGpuScene,
        string descriptionPrefix)
    {
        Type drawingFrameType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfDrawingFrame");
        Type containerVisualType = GetRequiredType(proGpuScene, "ProGPU.Scene.ContainerVisual");
        Type drawingVisualType = GetRequiredType(proGpuScene, "ProGPU.Scene.DrawingVisual");
        object sceneRoot = Create(containerVisualType);
        object retainedRoot = Create(containerVisualType);
        object flatRoot = Create(drawingVisualType);
        object frame = Create(
            drawingFrameType,
            sceneRoot,
            retainedRoot,
            flatRoot,
            840u,
            1680u,
            null,
            null,
            true,
            null,
            420u,
            840u,
            2.0,
            2.0,
            null);

        AssertEqual(420u, GetProperty(frame, "LogicalWidth"), $"{descriptionPrefix} ProGPU WPF drawing frame logical width");
        AssertEqual(840u, GetProperty(frame, "LogicalHeight"), $"{descriptionPrefix} ProGPU WPF drawing frame logical height");
        AssertEqual(840u, GetProperty(frame, "PixelWidth"), $"{descriptionPrefix} ProGPU WPF drawing frame pixel width");
        AssertEqual(1680u, GetProperty(frame, "PixelHeight"), $"{descriptionPrefix} ProGPU WPF drawing frame pixel height");
        AssertEqual(2.0, GetProperty(frame, "DpiScaleX"), $"{descriptionPrefix} ProGPU WPF drawing frame DPI scale X");
        AssertEqual(2.0, GetProperty(frame, "DpiScaleY"), $"{descriptionPrefix} ProGPU WPF drawing frame DPI scale Y");
        AssertEqual(new Vector2(420f, 840f), GetProperty(sceneRoot, "Size"), $"{descriptionPrefix} ProGPU scene root logical size");
        AssertEqual(new Vector2(420f, 840f), GetProperty(retainedRoot, "Size"), $"{descriptionPrefix} ProGPU retained WPF layer logical size");
        AssertEqual(new Vector2(420f, 840f), GetProperty(flatRoot, "Size"), $"{descriptionPrefix} ProGPU flat WPF layer logical size");
        AssertEqual(Vector3.One, GetProperty(retainedRoot, "Scale"), $"{descriptionPrefix} ProGPU retained WPF layer identity scale");
        AssertEqual(Vector2.Zero, GetProperty(retainedRoot, "RenderTransformOrigin"), $"{descriptionPrefix} ProGPU retained WPF layer transform origin");
    }

    private static void AssertPackagedHighDpiRetainedWpfPixelsFillPhysicalTarget(
        string nativeAssetRoot,
        Assembly proGpuWpf,
        Assembly proGpuBackend,
        Assembly presentationCore,
        Assembly presentationFramework,
        Assembly windowsBase,
        Assembly silkNetWebGpu,
        string descriptionPrefix)
    {
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type retainedSinkType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.Composition.ProGpuRetainedCompositionCommandSink");
        Type gpuTextureType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTexture");
        Type gpuTextureAlphaModeType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureAlphaMode");
        Type gpuTextureDimensionType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureDimension");
        Type textureFormatType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureFormat");
        Type textureUsageType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureUsage");
        object wpfVisual = CreateRedWpfVisual(presentationCore, presentationFramework, windowsBase);

        PreloadNativeAsset(nativeAssetRoot, "wgpu", $"{descriptionPrefix} WebGPU native runtime");
        using IDisposable currentDirectory = PushCurrentDirectory(nativeAssetRoot);
        object rgba8Unorm = Enum.Parse(textureFormatType, "Rgba8Unorm");
        object renderTargetUsage = CombineEnumFlags(
            textureUsageType,
            Enum.Parse(textureUsageType, "RenderAttachment"),
            Enum.Parse(textureUsageType, "CopySrc"));
        object straightAlphaMode = Enum.Parse(gpuTextureAlphaModeType, "Straight");
        object dimension2D = Enum.Parse(gpuTextureDimensionType, "Dimension2D");
        object target = InvokeStatic(compositionTargetType, "CreateHeadless", rgba8Unorm);
        object texture = Create(
            gpuTextureType,
            GetProperty(target, "Context"),
            840u,
            1680u,
            rgba8Unorm,
            renderTargetUsage,
            $"{descriptionPrefix} packaged HiDPI framebuffer target",
            1u,
            straightAlphaMode,
            1u,
            1u,
            dimension2D);

        try
        {
            object frame = Invoke(
                target,
                "BeginDrawingFrame",
                840u,
                1680u,
                true,
                420u,
                840u,
                2.0,
                2.0);
            object sink = Create(
                retainedSinkType,
                frame,
                GetProperty(target, "Context"),
                GetProperty(target, "Viewport3DTextureCache"));
            try
            {
                Invoke(target, "ReplayVisualSubtree", wpfVisual, sink, null, null);
            }
            finally
            {
                (sink as IDisposable)?.Dispose();
            }

            MethodInfo render = FindMethodByParameterNames(
                compositionTargetType,
                "Render",
                ["logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "dpiScale", "targetView"]);
            InvokeMethod(
                render,
                target,
                420u,
                840u,
                840u,
                1680u,
                2f,
                GetProperty(texture, "ViewPtr"));

            byte[] pixels = (byte[])Invoke(texture, "ReadPixels", 0u);
            AssertRgbaPixelIsRed(
                pixels,
                width: 840,
                x: 20,
                y: 20,
                $"{descriptionPrefix} packaged retained WPF HiDPI upper-left pixel");
            AssertRgbaPixelIsRed(
                pixels,
                width: 840,
                x: 780,
                y: 1560,
                $"{descriptionPrefix} packaged retained WPF HiDPI lower-right pixel");
        }
        finally
        {
            (texture as IDisposable)?.Dispose();
            (target as IDisposable)?.Dispose();
        }
    }

    private static void AssertPackagedObjectRenderDataRectangleFillsPhysicalTarget(
        string nativeAssetRoot,
        Assembly proGpuWpf,
        Assembly proGpuWpfInterop,
        Assembly proGpuBackend,
        Assembly presentationCore,
        Assembly windowsBase,
        Assembly silkNetWebGpu,
        string descriptionPrefix)
    {
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type gpuTextureType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTexture");
        Type gpuTextureAlphaModeType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureAlphaMode");
        Type gpuTextureDimensionType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureDimension");
        Type textureFormatType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureFormat");
        Type textureUsageType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureUsage");
        Type solidColorBrushType = GetRequiredType(presentationCore, "System.Windows.Media.SolidColorBrush");
        Type colorType = GetRequiredType(presentationCore, "System.Windows.Media.Color");
        Type portableRectType = GetRequiredType(proGpuWpfInterop, "ProGPU.Wpf.Interop.PortableRect");
        object red = InvokeStatic(colorType, "FromRgb", (byte)0xFF, (byte)0x00, (byte)0x00);
        object redBrush = Create(solidColorBrushType, red);
        object rectangle = Create(portableRectType, 0.0, 0.0, 420.0, 840.0, false);

        PreloadNativeAsset(nativeAssetRoot, "wgpu", $"{descriptionPrefix} WebGPU native runtime");
        using IDisposable currentDirectory = PushCurrentDirectory(nativeAssetRoot);
        object rgba8Unorm = Enum.Parse(textureFormatType, "Rgba8Unorm");
        object renderTargetUsage = CombineEnumFlags(
            textureUsageType,
            Enum.Parse(textureUsageType, "RenderAttachment"),
            Enum.Parse(textureUsageType, "CopySrc"));
        object straightAlphaMode = Enum.Parse(gpuTextureAlphaModeType, "Straight");
        object dimension2D = Enum.Parse(gpuTextureDimensionType, "Dimension2D");
        object target = InvokeStatic(compositionTargetType, "CreateHeadless", rgba8Unorm);
        object texture = Create(
            gpuTextureType,
            GetProperty(target, "Context"),
            840u,
            1680u,
            rgba8Unorm,
            renderTargetUsage,
            $"{descriptionPrefix} packaged object render-data HiDPI framebuffer target",
            1u,
            straightAlphaMode,
            1u,
            1u,
            dimension2D);

        try
        {
            object frame = Invoke(
                target,
                "BeginDrawingFrame",
                840u,
                1680u,
                true,
                420u,
                840u,
                2.0,
                2.0);
            object drawingContext = Invoke(frame, "OpenObjectRenderDataSinkContext", new object(), null);
            try
            {
                InvokeObjectDrawRectangle(drawingContext, redBrush, null, rectangle);
            }
            finally
            {
                (drawingContext as IDisposable)?.Dispose();
            }

            MethodInfo render = FindMethodByParameterNames(
                compositionTargetType,
                "Render",
                ["logicalWidth", "logicalHeight", "pixelWidth", "pixelHeight", "dpiScale", "targetView"]);
            InvokeMethod(
                render,
                target,
                420u,
                840u,
                840u,
                1680u,
                2f,
                GetProperty(texture, "ViewPtr"));

            byte[] pixels = (byte[])Invoke(texture, "ReadPixels", 0u);
            AssertRgbaPixelIsRed(
                pixels,
                width: 840,
                x: 20,
                y: 20,
                $"{descriptionPrefix} packaged object render-data WPF HiDPI upper-left pixel");
            AssertRgbaPixelIsRed(
                pixels,
                width: 840,
                x: 780,
                y: 1560,
                $"{descriptionPrefix} packaged object render-data WPF HiDPI lower-right pixel");
        }
        finally
        {
            (texture as IDisposable)?.Dispose();
            (target as IDisposable)?.Dispose();
        }
    }

    private static void InvokeObjectDrawRectangle(object drawingContext, object? brush, object? pen, object rectangle)
    {
        MethodInfo drawRectangle = drawingContext.GetType().GetMethod(
            "DrawRectangle",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [typeof(object), typeof(object), typeof(object)],
            modifiers: null)
            ?? throw new MissingMethodException(drawingContext.GetType().FullName, "DrawRectangle(object, object, object)");
        InvokeMethod(drawRectangle, drawingContext, brush, pen, rectangle);
    }

    private static object CreateRedWpfVisual(
        Assembly presentationCore,
        Assembly presentationFramework,
        Assembly windowsBase)
    {
        Type borderType = GetRequiredType(presentationFramework, "System.Windows.Controls.Border");
        Type colorType = GetRequiredType(presentationCore, "System.Windows.Media.Color");
        Type solidColorBrushType = GetRequiredType(presentationCore, "System.Windows.Media.SolidColorBrush");
        Type rectType = GetRequiredType(windowsBase, "System.Windows.Rect");
        Type sizeType = GetRequiredType(windowsBase, "System.Windows.Size");
        object red = InvokeStatic(colorType, "FromRgb", (byte)0xFF, (byte)0x00, (byte)0x00);
        object border = Create(borderType);
        SetProperty(border, "Width", 420.0);
        SetProperty(border, "Height", 840.0);
        SetProperty(border, "Background", Create(solidColorBrushType, red));
        InvokeVoid(border, "Measure", Create(sizeType, 420.0, 840.0));
        InvokeVoid(border, "Arrange", Create(rectType, 0.0, 0.0, 420.0, 840.0));
        InvokeVoid(border, "UpdateLayout");
        return border;
    }

    private static void AssertPackagedLegacyRenderOverloadFillsPhysicalTarget(
        string nativeAssetRoot,
        Assembly proGpuWpf,
        Assembly proGpuBackend,
        Assembly proGpuScene,
        Assembly proGpuVector,
        Assembly silkNetWebGpu,
        string descriptionPrefix)
    {
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type gpuTextureType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTexture");
        Type gpuTextureAlphaModeType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureAlphaMode");
        Type gpuTextureDimensionType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureDimension");
        Type rectType = GetRequiredType(proGpuScene, "ProGPU.Scene.Rect");
        Type solidColorBrushType = GetRequiredType(proGpuVector, "ProGPU.Vector.SolidColorBrush");
        Type textureFormatType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureFormat");
        Type textureUsageType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureUsage");

        PreloadNativeAsset(nativeAssetRoot, "wgpu", $"{descriptionPrefix} WebGPU native runtime");
        using IDisposable currentDirectory = PushCurrentDirectory(nativeAssetRoot);
        object rgba8Unorm = Enum.Parse(textureFormatType, "Rgba8Unorm");
        object renderTargetUsage = CombineEnumFlags(
            textureUsageType,
            Enum.Parse(textureUsageType, "RenderAttachment"),
            Enum.Parse(textureUsageType, "CopySrc"));
        object straightAlphaMode = Enum.Parse(gpuTextureAlphaModeType, "Straight");
        object dimension2D = Enum.Parse(gpuTextureDimensionType, "Dimension2D");
        object target = InvokeStatic(compositionTargetType, "CreateHeadless", rgba8Unorm);
        object texture = Create(
            gpuTextureType,
            GetProperty(target, "Context"),
            840u,
            1680u,
            rgba8Unorm,
            renderTargetUsage,
            $"{descriptionPrefix} packaged legacy HiDPI framebuffer target",
            1u,
            straightAlphaMode,
            1u,
            1u,
            dimension2D);

        try
        {
            object frame = Invoke(
                target,
                "BeginDrawingFrame",
                840u,
                1680u,
                true,
                420u,
                840u,
                2.0,
                2.0);
            object redBrush = Create(solidColorBrushType, 0xF02020FFu);
            object rectangle = Create(rectType, 0f, 0f, 420f, 840f);
            object drawingContext = GetProperty(GetProperty(target, "RootVisual"), "Context");
            InvokeVoid(drawingContext, "DrawRectangle", redBrush, null, rectangle);

            MethodInfo legacyRender = FindMethodByParameterNames(
                compositionTargetType,
                "Render",
                ["pixelWidth", "pixelHeight", "targetView"]);
            InvokeMethod(
                legacyRender,
                target,
                840u,
                1680u,
                GetProperty(texture, "ViewPtr"));

            byte[] pixels = (byte[])Invoke(texture, "ReadPixels", 0u);
            AssertRgbaPixelIsRed(
                pixels,
                width: 840,
                x: 780,
                y: 1560,
                $"{descriptionPrefix} packaged legacy WPF HiDPI lower-right pixel");
        }
        finally
        {
            (texture as IDisposable)?.Dispose();
            (target as IDisposable)?.Dispose();
        }
    }

    private static void PreloadNativeAsset(string root, string assetName, string description)
    {
        foreach (string candidate in GetNativeAssetCandidates(assetName))
        {
            string path = Path.Combine(root, candidate);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
            {
                return;
            }
        }

        throw new FileNotFoundException($"Could not load {description} from '{root}'.");
    }

    private static IDisposable PushCurrentDirectory(string path)
    {
        return new CurrentDirectoryScope(path);
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _originalDirectory;

        public CurrentDirectoryScope(string path)
        {
            _originalDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = path;
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = _originalDirectory;
        }
    }

    private static object CombineEnumFlags(Type enumType, params object[] values)
    {
        ulong combined = 0;
        foreach (object value in values)
        {
            combined |= Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }

        return Enum.ToObject(enumType, combined);
    }

    private static void AssertRgbaPixelIsRed(
        byte[] pixels,
        int width,
        int x,
        int y,
        string description)
    {
        int index = ((y * width) + x) * 4;
        if (index < 0 || index + 3 >= pixels.Length)
        {
            throw new InvalidOperationException($"Expected {description} pixel index to be inside the readback buffer.");
        }

        byte r = pixels[index];
        byte g = pixels[index + 1];
        byte b = pixels[index + 2];
        byte a = pixels[index + 3];
        if (r < 220 || g > 60 || b > 60 || a != 255)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be red, but found RGBA({r}, {g}, {b}, {a}).");
        }
    }

    private static void PreloadSdkWindowingPlatform(AssemblyLoadContext loadContext, string appOutputRoot)
    {
        Assembly glfwAssembly = loadContext.LoadFromAssemblyName(new AssemblyName("Silk.NET.GLFW"));
        RegisterSdkNativeResolver(glfwAssembly, appOutputRoot);
        Assembly webGpuAssembly = loadContext.LoadFromAssemblyName(new AssemblyName("Silk.NET.WebGPU"));
        RegisterSdkNativeResolver(webGpuAssembly, appOutputRoot);
        loadContext.LoadFromAssemblyName(new AssemblyName("Silk.NET.Windowing.Glfw"));
        loadContext.LoadFromAssemblyName(new AssemblyName("Silk.NET.Input.Glfw"));
    }

    private static void RegisterSdkNativeResolver(Assembly assembly, string appOutputRoot)
    {
        NativeLibrary.SetDllImportResolver(
            assembly,
            (libraryName, _, _) =>
            {
                foreach (string candidate in GetUnmanagedDllCandidates(libraryName))
                {
                    string path = Path.Combine(appOutputRoot, candidate);
                    if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                    {
                        return handle;
                    }
                }

                return IntPtr.Zero;
            });
    }

    private static void ValidateApp(object app)
    {
        AssertEqual("MainWindow.xaml", GetProperty(app, "StartupUri").ToString() ?? string.Empty, "startup URI");
        AssertEqual(0, GetProperty(app, "StartupEventCount"), "application startup event initial count");
        AssertEqual(0, GetProperty(app, "ExitEventCount"), "application exit event initial count");
        AssertEqual(-1, GetProperty(app, "LastExitCode"), "application exit code initial value");
        ValidateApplicationInitialLifetimeState(app);

        object resources = GetProperty(app, "Resources");
        object accentBrush = Invoke(app, "TryFindResource", "SmokeAccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "application accent brush");
        AssertEqual("#FF356D9E", GetProperty(accentBrush, "Color").ToString() ?? string.Empty, "application accent brush color");
        object mergedAccentBrush = Invoke(app, "TryFindResource", "MergedAccentBrush");
        AssertType(mergedAccentBrush, "System.Windows.Media.SolidColorBrush", "application merged accent brush");
        AssertEqual("#FF6B8F3A", GetProperty(mergedAccentBrush, "Color").ToString() ?? string.Empty, "application merged accent brush color");
        object libraryMergedAccentBrush = Invoke(app, "TryFindResource", "LibraryMergedAccentBrush");
        AssertType(libraryMergedAccentBrush, "System.Windows.Media.SolidColorBrush", "application referenced library merged accent brush");
        AssertEqual("#FF4F6F9D", GetProperty(libraryMergedAccentBrush, "Color").ToString() ?? string.Empty, "application referenced library merged accent brush color");
        AssertEqual("referenced library resource", Invoke(app, "TryFindResource", "LibraryMergedMessage"), "application referenced library merged string resource");
        object libraryMergedPadding = Invoke(app, "TryFindResource", "LibraryMergedPadding");
        AssertType(libraryMergedPadding, "System.Windows.Thickness", "application referenced library merged padding");
        AssertEqual(2.0, GetProperty(libraryMergedPadding, "Left"), "application referenced library merged padding left");
        AssertEqual(3.0, GetProperty(libraryMergedPadding, "Top"), "application referenced library merged padding top");
        AssertEqual(4.0, GetProperty(libraryMergedPadding, "Right"), "application referenced library merged padding right");
        AssertEqual(5.0, GetProperty(libraryMergedPadding, "Bottom"), "application referenced library merged padding bottom");
        object unsharedAccentBrush = Invoke(app, "TryFindResource", "UnsharedAccentBrush");
        object secondUnsharedAccentBrush = Invoke(app, "TryFindResource", "UnsharedAccentBrush");
        AssertType(unsharedAccentBrush, "System.Windows.Media.SolidColorBrush", "application unshared accent brush");
        AssertEqual("#FFC45A2B", GetProperty(unsharedAccentBrush, "Color").ToString() ?? string.Empty, "application unshared accent brush color");
        AssertNotSame(unsharedAccentBrush, secondUnsharedAccentBrush, "application x:Shared=false resource instance");
        object smokePanelMargin = Invoke(app, "TryFindResource", "SmokePanelMargin");
        AssertType(smokePanelMargin, "System.Windows.Thickness", "application merged panel margin");
        object providerGreeting = Invoke(app, "TryFindResource", "ProviderGreeting");
        AssertType(providerGreeting, "System.Windows.Data.ObjectDataProvider", "application object data provider");
        AssertEqual("provider:7", GetProperty(providerGreeting, "Data"), "application object data provider result");
        ValidateFreezableBrushResource(app);
        ValidateFreezableGradientBrushResource(app);
        AssertAtLeast(1, GetCount(GetProperty(resources, "Keys")), "application resource key count");
    }

    private static void ValidateApplicationRunLifetime(object app)
    {
        AssertEqual(true, GetProperty(app, "SdkOutputGuardChecked"), "application SDK output guard checked");
        AssertEqual(1, GetProperty(app, "StartupEventCount"), "application Startup event count");
        AssertEqual(0, GetProperty(app, "StartupArgsLength"), "application Startup args length");
        AssertEqual(1, GetProperty(app, "ExitEventCount"), "application Exit event count");
        AssertEqual(0, GetProperty(app, "LastExitCode"), "application Exit code");
        ValidateApplicationShutdownLifetimeState(app);
        object startupInjectedBrush = Invoke(app, "TryFindResource", "StartupInjectedBrush");
        AssertType(startupInjectedBrush, "System.Windows.Media.SolidColorBrush", "application Startup injected brush");
        AssertEqual("#FF7A4EB2", GetProperty(startupInjectedBrush, "Color").ToString() ?? string.Empty, "application Startup injected brush color");
        AssertEqual("startup resource value", Invoke(app, "TryFindResource", "StartupInjectedText"), "application Startup injected text resource");
    }

    private static void ValidateApplicationInitialLifetimeState(object app)
    {
        Type applicationType = GetRequiredType(GetAssemblyFromContext(app.GetType().Assembly, "PresentationFramework"), "System.Windows.Application");
        AssertSame(app, GetStaticProperty(applicationType, "Current"), "SDK Application.Current before run");
        AssertEqual("OnLastWindowClose", GetProperty(app, "ShutdownMode").ToString() ?? string.Empty, "SDK Application.ShutdownMode before run");
        AssertEqual(0, GetCount(GetProperty(app, "Windows")), "SDK Application.Windows before run");
        AssertNull(GetPropertyOrNull(app, "MainWindow"), "SDK Application.MainWindow before run");
    }

    private static void ValidateApplicationShutdownLifetimeState(object app)
    {
        Type applicationType = GetRequiredType(GetAssemblyFromContext(app.GetType().Assembly, "PresentationFramework"), "System.Windows.Application");
        AssertNull(GetStaticPropertyOrNull(applicationType, "Current"), "SDK Application.Current after shutdown");
        AssertNull(GetPropertyOrNull(app, "MainWindow"), "SDK Application.MainWindow after shutdown");
        AssertEqual(0, GetCount(GetProperty(app, "Windows")), "SDK Application.Windows after shutdown");
    }

    private static void ValidateFreezableBrushResource(object app)
    {
        object brush = Invoke(app, "TryFindResource", "FreezableAccentBrush");
        AssertType(brush, "System.Windows.Media.SolidColorBrush", "SDK Freezable brush resource");
        AssertEqual("#FF24507A", GetProperty(brush, "Color").ToString() ?? string.Empty, "SDK Freezable brush color");
        AssertClose(0.75, Convert.ToDouble(GetProperty(brush, "Opacity")), 0.0001, "SDK Freezable brush opacity");
        AssertEqual(true, GetProperty(brush, "CanFreeze"), "SDK Freezable brush can freeze");
        InvokeVoid(brush, "Freeze");
        AssertEqual(true, GetProperty(brush, "IsFrozen"), "SDK Freezable brush frozen");

        object clone = Invoke(brush, "Clone");
        AssertType(clone, "System.Windows.Media.SolidColorBrush", "SDK Freezable brush clone");
        AssertEqual(false, GetProperty(clone, "IsFrozen"), "SDK Freezable brush clone mutable");
        SetProperty(clone, "Opacity", 0.5);
        AssertClose(0.5, Convert.ToDouble(GetProperty(clone, "Opacity")), 0.0001, "SDK Freezable brush clone mutable opacity");

        object currentValueClone = Invoke(brush, "CloneCurrentValue");
        AssertType(currentValueClone, "System.Windows.Media.SolidColorBrush", "SDK Freezable brush current-value clone");
        AssertEqual(false, GetProperty(currentValueClone, "IsFrozen"), "SDK Freezable brush current-value clone mutable");
        AssertClose(0.75, Convert.ToDouble(GetProperty(currentValueClone, "Opacity")), 0.0001, "SDK Freezable current-value clone opacity");
    }

    private static void ValidateFreezableGradientBrushResource(object app)
    {
        object gradient = Invoke(app, "TryFindResource", "FreezableGradientBrush");
        AssertType(gradient, "System.Windows.Media.LinearGradientBrush", "SDK Freezable gradient brush resource");
        AssertClose(0.8, Convert.ToDouble(GetProperty(gradient, "Opacity")), 0.0001, "SDK Freezable gradient opacity");
        AssertEqual("Reflect", GetProperty(gradient, "SpreadMethod").ToString() ?? string.Empty, "SDK Freezable gradient spread method");
        object stops = GetProperty(gradient, "GradientStops");
        AssertEqual(3, GetCount(stops), "SDK Freezable gradient stop count");
        object firstStop = EnumerateObjects(stops).First();
        AssertType(firstStop, "System.Windows.Media.GradientStop", "SDK Freezable gradient first stop");
        AssertEqual("#FF2F6B54", GetProperty(firstStop, "Color").ToString() ?? string.Empty, "SDK Freezable gradient first stop color");
        AssertClose(0.0, Convert.ToDouble(GetProperty(firstStop, "Offset")), 0.0001, "SDK Freezable gradient first stop offset");
        AssertEqual(true, GetProperty(gradient, "CanFreeze"), "SDK Freezable gradient can freeze");
        InvokeVoid(gradient, "Freeze");
        AssertEqual(true, GetProperty(gradient, "IsFrozen"), "SDK Freezable gradient frozen");
        AssertEqual(true, GetProperty(stops, "IsFrozen"), "SDK Freezable gradient stop collection frozen");
        AssertEqual(true, GetProperty(firstStop, "IsFrozen"), "SDK Freezable gradient stop frozen");

        object clone = Invoke(gradient, "Clone");
        AssertType(clone, "System.Windows.Media.LinearGradientBrush", "SDK Freezable gradient clone");
        AssertEqual(false, GetProperty(clone, "IsFrozen"), "SDK Freezable gradient clone mutable");
        object cloneStops = GetProperty(clone, "GradientStops");
        AssertEqual(false, GetProperty(cloneStops, "IsFrozen"), "SDK Freezable gradient clone stop collection mutable");
        object cloneFirstStop = EnumerateObjects(cloneStops).First();
        AssertEqual(false, GetProperty(cloneFirstStop, "IsFrozen"), "SDK Freezable gradient clone stop mutable");
        SetProperty(cloneFirstStop, "Offset", 0.25);
        AssertClose(0.25, Convert.ToDouble(GetProperty(cloneFirstStop, "Offset")), 0.0001, "SDK Freezable gradient clone mutable stop offset");

        object currentValueClone = Invoke(gradient, "CloneCurrentValue");
        AssertType(currentValueClone, "System.Windows.Media.LinearGradientBrush", "SDK Freezable gradient current-value clone");
        AssertEqual(false, GetProperty(currentValueClone, "IsFrozen"), "SDK Freezable gradient current-value clone mutable");
        AssertClose(0.8, Convert.ToDouble(GetProperty(currentValueClone, "Opacity")), 0.0001, "SDK Freezable gradient current-value clone opacity");
        AssertEqual(3, GetCount(GetProperty(currentValueClone, "GradientStops")), "SDK Freezable gradient current-value clone stop collection");
    }

    private static void ValidatePortableSystemParameters(Assembly presentationFramework, object resourceOwner)
    {
        Type systemParametersType = GetRequiredType(presentationFramework, "System.Windows.SystemParameters");

        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "FocusBorderWidth", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "FocusBorderHeight", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "FocusHorizontalBorderHeight", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "FocusVerticalBorderWidth", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "PrimaryScreenWidth", 1024.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "PrimaryScreenHeight", 768.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "VerticalScrollBarWidth", 17.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "HorizontalScrollBarHeight", 17.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "CaretWidth", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "ThinHorizontalBorderHeight", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "ThinVerticalBorderWidth", 1.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "CursorWidth", 32.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "CursorHeight", 32.0);
        AssertPortableSystemParameterMetricValue(systemParametersType, "MinimumHorizontalDragDistance", 4.0);
        AssertPortableSystemParameterMetricValue(systemParametersType, "MinimumVerticalDragDistance", 4.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "IconWidth", 32.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "IconHeight", 32.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "IconGridWidth", 75.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "IconGridHeight", 75.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "MenuCheckmarkWidth", 13.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "MenuCheckmarkHeight", 13.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "MenuButtonWidth", 18.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "MenuButtonHeight", 18.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "WindowCaptionHeight", 23.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "MenuBarHeight", 19.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "VirtualScreenWidth", 1024.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "VirtualScreenHeight", 768.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "VirtualScreenLeft", 0.0);
        AssertPortableSystemParameterMetric(systemParametersType, resourceOwner, "VirtualScreenTop", 0.0);
        AssertPortableSystemParameterRect(systemParametersType, resourceOwner, "WorkArea", 0.0, 0.0, 1024.0, 768.0);
        AssertPortableSystemParameterThickness(systemParametersType, "WindowResizeBorderThickness", 8.0, 8.0, 8.0, 8.0);
        AssertPortableSystemParameterThickness(systemParametersType, "WindowNonClientFrameThickness", 8.0, 31.0, 8.0, 8.0);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "HighContrast", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "DropShadow", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "FlatMenu", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "MenuDropAlignment", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "MenuFade", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "MenuShowDelay", 400);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "ClientAreaAnimation", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "CursorShadow", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "GradientCaptions", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "HotTracking", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "ListBoxSmoothScrolling", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "SelectionFade", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "StylusHotTracking", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "UIEffects", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "MinimizeAnimation", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "Border", 1);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "DragFullWindows", true);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "ForegroundFlashCount", 7);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "WheelScrollLines", 3);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsImmEnabled", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsMediaCenter", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsMenuDropRightAligned", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsMiddleEastEnabled", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsMousePresent", true);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsMouseWheelPresent", true);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsPenWindows", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsRemotelyControlled", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsRemoteSession", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "ShowSounds", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsSlowMachine", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "SwapButtons", false);
        AssertPortableSystemParameterValue(systemParametersType, resourceOwner, "IsTabletPC", false);
        AssertPortableSystemParameterValue(
            systemParametersType,
            resourceOwner,
            "PowerLineStatus",
            Enum.Parse(GetRequiredType(presentationFramework, "System.Windows.PowerLineStatus"), "Unknown"));
    }

    private static void ValidatePortableInputLanguageManager(Assembly presentationCore, object target)
    {
        Type inputLanguageManagerType = GetRequiredType(presentationCore, "System.Windows.Input.InputLanguageManager");
        object manager = GetStaticProperty(inputLanguageManagerType, "Current");
        object currentLanguage = GetProperty(manager, "CurrentInputLanguage");
        AssertType(currentLanguage, "System.Globalization.CultureInfo", "SDK InputLanguageManager current language");

        object availableLanguages = GetProperty(manager, "AvailableInputLanguages");
        AssertAtLeast(1, EnumerateObjects(availableLanguages).Count(), "SDK InputLanguageManager available language count");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string currentName = GetProperty(currentLanguage, "Name").ToString() ?? string.Empty;
        AssertEqual(CultureInfo.CurrentCulture.Name, currentName, "portable SDK InputLanguageManager current culture");

        CultureInfo requestedLanguage = CultureInfo.GetCultureInfo("en-US");
        SetProperty(manager, "CurrentInputLanguage", requestedLanguage);
        AssertEqual(
            requestedLanguage.Name,
            GetProperty(GetProperty(manager, "CurrentInputLanguage"), "Name").ToString() ?? string.Empty,
            "portable SDK InputLanguageManager set current language");

        InvokeStaticVoid(inputLanguageManagerType, "SetInputLanguage", target, requestedLanguage);
        object attachedLanguage = InvokeStatic(inputLanguageManagerType, "GetInputLanguage", target);
        AssertEqual(
            requestedLanguage.Name,
            GetProperty(attachedLanguage, "Name").ToString() ?? string.Empty,
            "portable SDK InputLanguageManager attached language");

        SetProperty(manager, "CurrentInputLanguage", CultureInfo.CurrentCulture);
    }

    private static void ValidatePortableInputMethod(Assembly presentationCore, object target)
    {
        Type inputMethodType = GetRequiredType(presentationCore, "System.Windows.Input.InputMethod");
        object inputMethod = GetStaticProperty(inputMethodType, "Current");
        AssertType(inputMethod, "System.Windows.Input.InputMethod", "SDK InputMethod current instance");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Type inputMethodStateType = GetRequiredType(presentationCore, "System.Windows.Input.InputMethodState");
        Type conversionModeType = GetRequiredType(presentationCore, "System.Windows.Input.ImeConversionModeValues");
        Type sentenceModeType = GetRequiredType(presentationCore, "System.Windows.Input.ImeSentenceModeValues");
        Type speechModeType = GetRequiredType(presentationCore, "System.Windows.Input.SpeechMode");

        object offState = Enum.Parse(inputMethodStateType, "Off");
        object onState = Enum.Parse(inputMethodStateType, "On");
        object conversionMode = Enum.ToObject(
            conversionModeType,
            Convert.ToInt32(Enum.Parse(conversionModeType, "Native")) |
            Convert.ToInt32(Enum.Parse(conversionModeType, "FullShape")));
        object sentenceMode = Enum.Parse(sentenceModeType, "Automatic");
        object dictationMode = Enum.Parse(speechModeType, "Dictation");

        AssertEqual("Off", GetProperty(inputMethod, "ImeState").ToString() ?? string.Empty, "portable SDK InputMethod default IME state");
        AssertEqual("Alphanumeric", GetProperty(inputMethod, "ImeConversionMode").ToString() ?? string.Empty, "portable SDK InputMethod default conversion mode");
        AssertEqual("None", GetProperty(inputMethod, "ImeSentenceMode").ToString() ?? string.Empty, "portable SDK InputMethod default sentence mode");
        AssertEqual(false, GetProperty(inputMethod, "CanShowConfigurationUI"), "portable SDK InputMethod configure UI availability");
        AssertEqual(false, GetProperty(inputMethod, "CanShowRegisterWordUI"), "portable SDK InputMethod register-word UI availability");

        SetProperty(inputMethod, "ImeState", onState);
        SetProperty(inputMethod, "MicrophoneState", onState);
        SetProperty(inputMethod, "HandwritingState", onState);
        SetProperty(inputMethod, "SpeechMode", dictationMode);
        SetProperty(inputMethod, "ImeConversionMode", conversionMode);
        SetProperty(inputMethod, "ImeSentenceMode", sentenceMode);

        AssertEqual("On", GetProperty(inputMethod, "ImeState").ToString() ?? string.Empty, "portable SDK InputMethod set IME state");
        AssertEqual("On", GetProperty(inputMethod, "MicrophoneState").ToString() ?? string.Empty, "portable SDK InputMethod set microphone state");
        AssertEqual("On", GetProperty(inputMethod, "HandwritingState").ToString() ?? string.Empty, "portable SDK InputMethod set handwriting state");
        AssertEqual("Dictation", GetProperty(inputMethod, "SpeechMode").ToString() ?? string.Empty, "portable SDK InputMethod set speech mode");
        AssertEqual("Native, FullShape", GetProperty(inputMethod, "ImeConversionMode").ToString() ?? string.Empty, "portable SDK InputMethod set conversion mode");
        AssertEqual("Automatic", GetProperty(inputMethod, "ImeSentenceMode").ToString() ?? string.Empty, "portable SDK InputMethod set sentence mode");

        InvokeStaticVoid(inputMethodType, "SetPreferredImeState", target, onState);
        InvokeStaticVoid(inputMethodType, "SetPreferredImeConversionMode", target, conversionMode);
        InvokeStaticVoid(inputMethodType, "SetPreferredImeSentenceMode", target, sentenceMode);
        AssertEqual("On", InvokeStatic(inputMethodType, "GetPreferredImeState", target).ToString() ?? string.Empty, "portable SDK InputMethod attached preferred IME state");
        AssertEqual("Native, FullShape", InvokeStatic(inputMethodType, "GetPreferredImeConversionMode", target).ToString() ?? string.Empty, "portable SDK InputMethod attached preferred conversion mode");
        AssertEqual("Automatic", InvokeStatic(inputMethodType, "GetPreferredImeSentenceMode", target).ToString() ?? string.Empty, "portable SDK InputMethod attached preferred sentence mode");

        SetProperty(inputMethod, "ImeState", offState);
        SetProperty(inputMethod, "MicrophoneState", offState);
        SetProperty(inputMethod, "HandwritingState", offState);
        SetProperty(inputMethod, "ImeConversionMode", Enum.Parse(conversionModeType, "Alphanumeric"));
        SetProperty(inputMethod, "ImeSentenceMode", Enum.Parse(sentenceModeType, "None"));
    }

    private static void ValidatePortableWindowChrome(Assembly presentationFramework, object window)
    {
        Type windowChromeType = GetRequiredType(presentationFramework, "System.Windows.Shell.WindowChrome");
        Type nonClientFrameEdgesType = GetRequiredType(presentationFramework, "System.Windows.Shell.NonClientFrameEdges");
        Type thicknessType = windowChromeType.GetProperty(
                "ResizeBorderThickness",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.PropertyType
            ?? throw new MissingMemberException(windowChromeType.FullName, "ResizeBorderThickness");

        object chrome = Create(windowChromeType);
        SetProperty(chrome, "CaptionHeight", 32.0);
        SetProperty(chrome, "ResizeBorderThickness", Create(thicknessType, 6.0));
        SetProperty(chrome, "GlassFrameThickness", Create(thicknessType, 0.0));
        SetProperty(chrome, "NonClientFrameEdges", Enum.Parse(nonClientFrameEdgesType, "Top"));
        SetProperty(chrome, "UseAeroCaptionButtons", false);

        InvokeStaticVoid(windowChromeType, "SetWindowChrome", window, chrome);
        AssertSame(chrome, InvokeStatic(windowChromeType, "GetWindowChrome", window), "portable SDK WindowChrome attached value");
        AssertEqual(32.0, GetProperty(chrome, "CaptionHeight"), "portable SDK WindowChrome caption height");

        InvokeStaticVoid(windowChromeType, "SetIsHitTestVisibleInChrome", window, true);
        AssertEqual(
            true,
            InvokeStatic(windowChromeType, "GetIsHitTestVisibleInChrome", window),
            "portable SDK WindowChrome hit-test attached value");

        InvokeStaticVoid(windowChromeType, "SetWindowChrome", window, null);
        AssertNull(InvokeStaticOrNull(windowChromeType, "GetWindowChrome", window), "portable SDK WindowChrome cleared value");
    }

    private static void ValidatePortableSystemCommands(Assembly presentationFramework, object window)
    {
        Type systemCommandsType = GetRequiredType(presentationFramework, "System.Windows.SystemCommands");
        Type windowStateType = GetRequiredType(presentationFramework, "System.Windows.WindowState");
        Type pointType = systemCommandsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(method => string.Equals(method.Name, "ShowSystemMenu", StringComparison.Ordinal))
            .Select(method => method.GetParameters())
            .Where(parameters => parameters.Length == 2)
            .Select(parameters => parameters[1].ParameterType)
            .FirstOrDefault(type => string.Equals(type.FullName, "System.Windows.Point", StringComparison.Ordinal))
            ?? throw new TypeLoadException("System.Windows.Point");

        InvokeStaticVoid(systemCommandsType, "MaximizeWindow", window);
        AssertEqual(Enum.Parse(windowStateType, "Maximized"), GetProperty(window, "WindowState"), "portable SDK SystemCommands maximize state");

        InvokeStaticVoid(systemCommandsType, "MinimizeWindow", window);
        AssertEqual(Enum.Parse(windowStateType, "Minimized"), GetProperty(window, "WindowState"), "portable SDK SystemCommands minimize state");

        InvokeStaticVoid(systemCommandsType, "RestoreWindow", window);
        AssertEqual(Enum.Parse(windowStateType, "Normal"), GetProperty(window, "WindowState"), "portable SDK SystemCommands restore state");

        InvokeStaticVoid(systemCommandsType, "ShowSystemMenu", window, Create(pointType, 12.0, 24.0));
        AssertEqual(Enum.Parse(windowStateType, "Normal"), GetProperty(window, "WindowState"), "portable SDK SystemCommands show system menu no-op state");
    }

    private static void AssertPortableSystemParameterMetric(
        Type systemParametersType,
        object resourceOwner,
        string propertyName,
        double expectedNonWindowsValue)
    {
        double value = AssertPortableSystemParameterMetricValue(systemParametersType, propertyName, expectedNonWindowsValue);

        object resourceValue = ResolveSystemParameterResource(systemParametersType, resourceOwner, propertyName);
        AssertClose(value, Convert.ToDouble(resourceValue), 0.0001, $"portable SDK SystemParameters.{propertyName} resource");
    }

    private static double AssertPortableSystemParameterMetricValue(
        Type systemParametersType,
        string propertyName,
        double expectedNonWindowsValue)
    {
        double value = Convert.ToDouble(GetStaticProperty(systemParametersType, propertyName));
        if (OperatingSystem.IsWindows())
        {
            if (value < 0)
            {
                throw new InvalidOperationException($"Expected SystemParameters.{propertyName} to be non-negative, got '{value}'.");
            }
        }
        else
        {
            AssertClose(expectedNonWindowsValue, value, 0.0001, $"portable SDK SystemParameters.{propertyName}");
        }

        return value;
    }

    private static void AssertPortableSystemParameterThickness(
        Type systemParametersType,
        string propertyName,
        double expectedLeft,
        double expectedTop,
        double expectedRight,
        double expectedBottom)
    {
        object value = GetStaticProperty(systemParametersType, propertyName);
        if (OperatingSystem.IsWindows())
        {
            AssertType(value, "System.Windows.Thickness", $"SDK SystemParameters.{propertyName}");
            return;
        }

        AssertClose(expectedLeft, Convert.ToDouble(GetProperty(value, "Left")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Left");
        AssertClose(expectedTop, Convert.ToDouble(GetProperty(value, "Top")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Top");
        AssertClose(expectedRight, Convert.ToDouble(GetProperty(value, "Right")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Right");
        AssertClose(expectedBottom, Convert.ToDouble(GetProperty(value, "Bottom")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Bottom");
    }

    private static void AssertPortableSystemParameterRect(
        Type systemParametersType,
        object resourceOwner,
        string propertyName,
        double expectedX,
        double expectedY,
        double expectedWidth,
        double expectedHeight)
    {
        object value = GetStaticProperty(systemParametersType, propertyName);
        if (OperatingSystem.IsWindows())
        {
            AssertType(value, "System.Windows.Rect", $"SDK SystemParameters.{propertyName}");
        }
        else
        {
            AssertClose(expectedX, Convert.ToDouble(GetProperty(value, "X")), 0.0001, $"portable SDK SystemParameters.{propertyName}.X");
            AssertClose(expectedY, Convert.ToDouble(GetProperty(value, "Y")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Y");
            AssertClose(expectedWidth, Convert.ToDouble(GetProperty(value, "Width")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Width");
            AssertClose(expectedHeight, Convert.ToDouble(GetProperty(value, "Height")), 0.0001, $"portable SDK SystemParameters.{propertyName}.Height");
        }

        object resourceValue = ResolveSystemParameterResource(systemParametersType, resourceOwner, propertyName);
        AssertEqual(value, resourceValue, $"portable SDK SystemParameters.{propertyName} resource");
    }

    private static void AssertPortableSystemParameterValue(
        Type systemParametersType,
        object resourceOwner,
        string propertyName,
        object expectedNonWindowsValue)
    {
        object value = GetStaticProperty(systemParametersType, propertyName);
        if (!OperatingSystem.IsWindows())
        {
            AssertEqual(expectedNonWindowsValue, value, $"portable SDK SystemParameters.{propertyName}");
        }

        object resourceValue = ResolveSystemParameterResource(systemParametersType, resourceOwner, propertyName);
        AssertEqual(value, resourceValue, $"portable SDK SystemParameters.{propertyName} resource");
    }

    private static object ResolveSystemParameterResource(Type systemParametersType, object resourceOwner, string propertyName)
    {
        object key = GetStaticProperty(systemParametersType, propertyName + "Key");
        return Invoke(resourceOwner, "TryFindResource", key);
    }

    private static void ValidateSdkLooseXamlReaderWriter(Assembly presentationFramework, Assembly presentationCore)
    {
        ValidateSdkLooseXamlReader(presentationFramework, presentationCore);
        ValidateSdkLooseXamlWriterRoundTrip(presentationFramework);
    }

    private static void ValidateSdkLooseXamlReader(Assembly presentationFramework, Assembly presentationCore)
    {
        const string looseXaml = """
<StackPanel
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    x:Name="SdkLooseRoot">
    <StackPanel.Resources>
        <SolidColorBrush x:Key="SdkLooseAccentBrush" Color="#4F7CAC" />
        <Style x:Key="SdkLooseTextStyle" TargetType="{x:Type TextBlock}">
            <Setter Property="Tag" Value="SDK loose style tag" />
            <Setter Property="Foreground" Value="{StaticResource SdkLooseAccentBrush}" />
        </Style>
    </StackPanel.Resources>
    <TextBlock
        x:Name="SdkLooseText"
        Style="{StaticResource SdkLooseTextStyle}"
        Text="SDK loose xaml text" />
    <TextBox
        x:Name="SdkLooseTextBox"
        Tag="SDK loose binding text"
        Text="{Binding Tag, RelativeSource={RelativeSource Self}}" />
    <TextBox
        x:Name="SdkLooseInputScopeTextBox"
        Text="SDK loose input scope text">
        <InputMethod.InputScope>
            <InputScope
                RegularExpression="[a-z]+"
                SrgsMarkup="sdk-loose-input-scope">
                <InputScope.Names>
                    <InputScopeName>EmailUserName</InputScopeName>
                </InputScope.Names>
                <InputScope.PhraseList>
                    <InputScopePhrase>sdk loose phrase</InputScopePhrase>
                </InputScope.PhraseList>
            </InputScope>
        </InputMethod.InputScope>
    </TextBox>
</StackPanel>
""";

        object root = ParseLooseXaml(presentationFramework, looseXaml);
        AssertType(root, "System.Windows.Controls.StackPanel", "SDK loose XamlReader root");
        AssertEqual("SdkLooseRoot", GetProperty(root, "Name"), "SDK loose XamlReader root name");
        object children = GetProperty(root, "Children");
        AssertEqual(3, GetCount(children), "SDK loose XamlReader child count");

        object resources = GetProperty(root, "Resources");
        object accentBrush = GetDictionaryValue(resources, "SdkLooseAccentBrush");
        AssertType(accentBrush, "System.Windows.Media.SolidColorBrush", "SDK loose XamlReader brush resource");
        AssertEqual("#FF4F7CAC", GetProperty(accentBrush, "Color").ToString() ?? string.Empty, "SDK loose XamlReader brush color");
        object textStyle = GetDictionaryValue(resources, "SdkLooseTextStyle");
        AssertType(textStyle, "System.Windows.Style", "SDK loose XamlReader style resource");
        AssertEqual("System.Windows.Controls.TextBlock", GetProperty(textStyle, "TargetType").ToString() ?? string.Empty, "SDK loose XamlReader style target");

        object textBlock = Invoke(root, "FindName", "SdkLooseText");
        AssertType(textBlock, "System.Windows.Controls.TextBlock", "SDK loose XamlReader named TextBlock");
        AssertSame(GetCollectionItem(children, 0), textBlock, "SDK loose XamlReader TextBlock child");
        AssertSame(textStyle, GetProperty(textBlock, "Style"), "SDK loose XamlReader StaticResource style");
        AssertEqual("SDK loose xaml text", GetProperty(textBlock, "Text"), "SDK loose XamlReader text");
        AssertEqual("SDK loose style tag", GetProperty(textBlock, "Tag"), "SDK loose XamlReader style setter tag");
        AssertSame(accentBrush, GetProperty(textBlock, "Foreground"), "SDK loose XamlReader style StaticResource brush");

        object textBox = Invoke(root, "FindName", "SdkLooseTextBox");
        AssertType(textBox, "System.Windows.Controls.TextBox", "SDK loose XamlReader named TextBox");
        AssertSame(GetCollectionItem(children, 1), textBox, "SDK loose XamlReader TextBox child");
        AssertEqual("SDK loose binding text", GetProperty(textBox, "Tag"), "SDK loose XamlReader TextBox tag");
        AssertEqual("SDK loose binding text", GetProperty(textBox, "Text"), "SDK loose XamlReader RelativeSource binding text");
        AssertBindingPath(presentationFramework, textBox, "TextProperty", "Tag", "SDK loose XamlReader Binding path");

        object inputScopeTextBox = Invoke(root, "FindName", "SdkLooseInputScopeTextBox");
        AssertType(inputScopeTextBox, "System.Windows.Controls.TextBox", "SDK loose XamlReader InputScope TextBox");
        AssertSame(GetCollectionItem(children, 2), inputScopeTextBox, "SDK loose XamlReader InputScope TextBox child");
        AssertEqual("SDK loose input scope text", GetProperty(inputScopeTextBox, "Text"), "SDK loose XamlReader InputScope TextBox text");
        ValidateInputScope(
            presentationCore,
            inputScopeTextBox,
            "[a-z]+",
            "sdk-loose-input-scope",
            "EmailUserName",
            "sdk loose phrase",
            "SDK loose XamlReader");
    }

    private static void ValidateInputScope(
        Assembly presentationCore,
        object target,
        string expectedRegularExpression,
        string expectedSrgsMarkup,
        string expectedName,
        string expectedPhrase,
        string description)
    {
        Type inputMethodType = GetRequiredType(presentationCore, "System.Windows.Input.InputMethod");
        object inputScope = InvokeStatic(inputMethodType, "GetInputScope", target);
        AssertType(inputScope, "System.Windows.Input.InputScope", $"{description} InputScope attached value");
        AssertEqual(expectedRegularExpression, GetProperty(inputScope, "RegularExpression"), $"{description} InputScope regular expression");
        AssertEqual(expectedSrgsMarkup, GetProperty(inputScope, "SrgsMarkup"), $"{description} InputScope SRGS markup");

        object names = GetProperty(inputScope, "Names");
        AssertEqual(1, GetCount(names), $"{description} InputScope names");
        object scopeName = GetCollectionItem(names, 0);
        AssertType(scopeName, "System.Windows.Input.InputScopeName", $"{description} InputScopeName");
        AssertEqual(expectedName, GetProperty(scopeName, "NameValue").ToString() ?? string.Empty, $"{description} InputScopeName value");

        object phrases = GetProperty(inputScope, "PhraseList");
        AssertEqual(1, GetCount(phrases), $"{description} InputScope phrases");
        object phrase = GetCollectionItem(phrases, 0);
        AssertType(phrase, "System.Windows.Input.InputScopePhrase", $"{description} InputScopePhrase");
        AssertEqual(expectedPhrase, GetProperty(phrase, "Name"), $"{description} InputScopePhrase text");
    }

    private static void ValidateSdkLooseXamlWriterRoundTrip(Assembly presentationFramework)
    {
        const string writableXaml = """
<LinearGradientBrush
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    StartPoint="0,0"
    EndPoint="1,1"
    Opacity="0.625"
    SpreadMethod="Reflect">
    <GradientStop Color="#4F7CAC" Offset="0" />
    <GradientStop Color="#B15E3B" Offset="1" />
</LinearGradientBrush>
""";

        object brush = ParseLooseXaml(presentationFramework, writableXaml);
        string serialized = SaveLooseXaml(presentationFramework, brush);
        AssertContains("LinearGradientBrush", serialized, "SDK loose XamlWriter serialized brush");
        AssertContains("GradientStop", serialized, "SDK loose XamlWriter serialized GradientStop");

        object roundTrippedBrush = ParseLooseXaml(presentationFramework, serialized);
        AssertType(roundTrippedBrush, "System.Windows.Media.LinearGradientBrush", "SDK loose XamlWriter round-trip brush");
        AssertClose(0.625, Convert.ToDouble(GetProperty(roundTrippedBrush, "Opacity")), 0.0001, "SDK loose XamlWriter round-trip brush opacity");
        AssertEqual("Reflect", GetProperty(roundTrippedBrush, "SpreadMethod").ToString() ?? string.Empty, "SDK loose XamlWriter round-trip brush spread method");
        object roundTrippedStops = GetProperty(roundTrippedBrush, "GradientStops");
        AssertEqual(2, GetCount(roundTrippedStops), "SDK loose XamlWriter round-trip GradientStop count");
        ValidateSdkLooseGradientStop(GetCollectionItem(roundTrippedStops, 0), "#FF4F7CAC", 0.0, "first");
        ValidateSdkLooseGradientStop(GetCollectionItem(roundTrippedStops, 1), "#FFB15E3B", 1.0, "second");
    }

    private static void ValidateSdkLooseGradientStop(object stop, string expectedColor, double expectedOffset, string description)
    {
        AssertType(stop, "System.Windows.Media.GradientStop", $"SDK loose XamlWriter round-trip {description} stop");
        AssertEqual(expectedColor, GetProperty(stop, "Color").ToString() ?? string.Empty, $"SDK loose XamlWriter round-trip {description} stop color");
        AssertClose(expectedOffset, Convert.ToDouble(GetProperty(stop, "Offset")), 0.0001, $"SDK loose XamlWriter round-trip {description} stop offset");
    }

    private static object ParseLooseXaml(Assembly presentationFramework, string xaml)
    {
        Type xamlReaderType = GetRequiredType(presentationFramework, "System.Windows.Markup.XamlReader");
        return InvokeStatic(xamlReaderType, "Parse", xaml);
    }

    private static string SaveLooseXaml(Assembly presentationFramework, object value)
    {
        Type xamlWriterType = GetRequiredType(presentationFramework, "System.Windows.Markup.XamlWriter");
        return InvokeStatic(xamlWriterType, "Save", value).ToString()
            ?? throw new InvalidOperationException("Loose XamlWriter.Save returned null.");
    }

    private static object LoadApplicationComponent(object contextObject, string componentUri)
    {
        Assembly presentationFramework = GetAssemblyFromContext(contextObject.GetType().Assembly, "PresentationFramework");
        Type applicationType = GetRequiredType(presentationFramework, "System.Windows.Application");
        return InvokeStatic(applicationType, "LoadComponent", new Uri(componentUri, UriKind.Relative));
    }

    private static void ValidateWindow(
        object window,
        bool validateFrameContent,
        Action<object>? flushDispatcherOperations)
    {
        AssertAssignableTo(window, "System.Windows.Window", "SDK smoke main window");
        AssertEqual("ProGPU WPF SDK Smoke", GetProperty(window, "Title"), "window title");
        AssertEqual(420.0, GetProperty(window, "Width"), "window width");
        AssertEqual(840.0, GetProperty(window, "Height"), "window height");

        InvokeVoid(window, "UpdateLayout");

        object message = Invoke(window, "FindName", "Message");
        AssertType(message, "System.Windows.Controls.TextBlock", "message element");
        AssertEqual("ProGPU WPF SDK switch managed subsystem smoke", GetProperty(message, "Text"), "message text");
        object messageForeground = GetProperty(message, "Foreground");
        AssertType(messageForeground, "System.Windows.Media.SolidColorBrush", "message dynamic resource foreground");
        AssertEqual("#FF6B8F3A", GetProperty(messageForeground, "Color").ToString() ?? string.Empty, "message foreground color");
        object rootPanel = Invoke(window, "FindName", "RootPanel");
        AssertType(rootPanel, "System.Windows.Controls.StackPanel", "root panel element");
        object startupResourceText = Invoke(window, "FindName", "StartupResourceText");
        AssertType(startupResourceText, "System.Windows.Controls.TextBlock", "startup resource text element");
        if (validateFrameContent)
        {
            AssertEqual("startup resource value", GetProperty(startupResourceText, "Text"), "startup resource text value");
            object startupResourceForeground = GetProperty(startupResourceText, "Foreground");
            AssertType(startupResourceForeground, "System.Windows.Media.SolidColorBrush", "startup resource foreground");
            AssertEqual("#FF7A4EB2", GetProperty(startupResourceForeground, "Color").ToString() ?? string.Empty, "startup resource foreground color");
        }

        object actionButton = Invoke(window, "FindName", "ActionButton");
        AssertType(actionButton, "System.Windows.Controls.Button", "action button");
        AssertEqual("ProGPU WPF SDK switch managed subsystem smoke", GetProperty(actionButton, "Content"), "button bound content");
        AssertType(GetProperty(actionButton, "Style"), "System.Windows.Style", "action button explicit style");
        object actionButtonTemplate = GetProperty(actionButton, "Template");
        AssertType(actionButtonTemplate, "System.Windows.Controls.ControlTemplate", "action button control template");
        object actionButtonTemplateRoot = Invoke(actionButtonTemplate, "LoadContent");
        AssertType(actionButtonTemplateRoot, "System.Windows.Controls.Border", "action button control template root");
        Type visualStateManagerType = GetRequiredType(actionButtonTemplateRoot.GetType().Assembly, "System.Windows.VisualStateManager");
        object visualStateGroups = InvokeStatic(visualStateManagerType, "GetVisualStateGroups", actionButtonTemplateRoot);
        AssertAtLeast(1, GetCount(visualStateGroups), "action button visual state group count");
        object actionButtonBackground = GetProperty(actionButton, "Background");
        AssertType(actionButtonBackground, "System.Windows.Media.SolidColorBrush", "action button dynamic resource background");
        AssertEqual("#FF356D9E", GetProperty(actionButtonBackground, "Color").ToString() ?? string.Empty, "action button background color");

        object clickStatus = Invoke(window, "FindName", "ClickStatus");
        AssertType(clickStatus, "System.Windows.Controls.TextBlock", "click status element");
        object clickStatusText = GetProperty(clickStatus, "Text");
        if (object.Equals("not clicked", clickStatusText))
        {
            InvokeVoid(actionButton, "OnClick");
        }

        AssertEqual("clicked", GetProperty(clickStatus, "Text"), "click status after generated event");

        object commandBindings = GetProperty(window, "CommandBindings");
        object commandBinding = EnumerateObjects(commandBindings).FirstOrDefault()
            ?? throw new InvalidOperationException("Expected an SDK smoke Window.CommandBindings entry.");
        object commandBindingCommand = GetProperty(commandBinding, "Command");
        AssertType(commandBindingCommand, "System.Windows.Input.RoutedUICommand", "window command binding command");
        AssertEqual("SmokeCommand", GetProperty(commandBindingCommand, "Name"), "window command binding command name");

        object inputBindings = GetProperty(window, "InputBindings");
        object[] windowInputBindings = EnumerateObjects(inputBindings).ToArray();
        AssertEqual(2, windowInputBindings.Length, "window input binding count");
        object keyBinding = windowInputBindings[0];
        object keyBindingCommand = GetProperty(keyBinding, "Command");
        AssertType(keyBindingCommand, "System.Windows.Input.RoutedUICommand", "window key binding command");
        AssertEqual("SmokeCommand", GetProperty(keyBindingCommand, "Name"), "window key binding command name");
        AssertEqual("input binding payload", GetProperty(keyBinding, "CommandParameter"), "window key binding command parameter");
        AssertEqual("F6", GetProperty(keyBinding, "Key").ToString() ?? string.Empty, "window key binding key");
        AssertEqual("Control", GetProperty(keyBinding, "Modifiers").ToString() ?? string.Empty, "window key binding modifiers");
        object mouseBinding = windowInputBindings[1];
        object mouseBindingCommand = GetProperty(mouseBinding, "Command");
        AssertType(mouseBindingCommand, "System.Windows.Input.RoutedUICommand", "window mouse binding command");
        AssertEqual("SmokeCommand", GetProperty(mouseBindingCommand, "Name"), "window mouse binding command name");
        AssertEqual("mouse binding payload", GetProperty(mouseBinding, "CommandParameter"), "window mouse binding command parameter");
        object mouseGesture = GetProperty(mouseBinding, "Gesture");
        AssertType(mouseGesture, "System.Windows.Input.MouseGesture", "window mouse binding gesture");
        AssertEqual("LeftDoubleClick", GetProperty(mouseGesture, "MouseAction").ToString() ?? string.Empty, "window mouse binding gesture action");
        AssertEqual("None", GetProperty(mouseGesture, "Modifiers").ToString() ?? string.Empty, "window mouse binding gesture modifiers");

        object commandButton = Invoke(window, "FindName", "CommandButton");
        AssertType(commandButton, "System.Windows.Controls.Button", "command button");
        object commandButtonCommand = GetProperty(commandButton, "Command");
        AssertType(commandButtonCommand, "System.Windows.Input.RoutedUICommand", "command button command");
        AssertEqual("SmokeCommand", GetProperty(commandButtonCommand, "Name"), "command button command name");
        object commandButtonParameter = GetProperty(commandButton, "CommandParameter");
        AssertEqual("routed command payload", commandButtonParameter, "command button command parameter");
        AssertEqual(true, Invoke(commandButtonCommand, "CanExecute", commandButtonParameter, window), "command button routed command CanExecute");

        object commandStatus = Invoke(window, "FindName", "CommandStatus");
        AssertType(commandStatus, "System.Windows.Controls.TextBlock", "command status element");
        if (object.Equals("command not executed", GetProperty(commandStatus, "Text")))
        {
            InvokeVoid(commandButton, "OnClick");
            AssertEqual("routed command payload", GetProperty(window, "LastSmokeCommandParameter"), "window routed command executed parameter");
            AssertEqual("routed command payload", GetProperty(commandStatus, "Text"), "command status after routed command");
        }
        else
        {
            object lastCommandParameter = GetProperty(window, "LastSmokeCommandParameter");
            if (!object.Equals("routed command payload", lastCommandParameter)
                && !object.Equals("menu command payload", lastCommandParameter)
                && !object.Equals("context menu command payload", lastCommandParameter)
                && !object.Equals("toolbar command payload", lastCommandParameter)
                && !object.Equals("mouse binding payload", lastCommandParameter))
            {
                throw new InvalidOperationException($"Unexpected smoke command parameter '{lastCommandParameter}'.");
            }
        }

        AssertAtLeast(1, GetProperty(window, "SmokeCommandCanExecuteCount"), "window routed command CanExecute count");
        AssertAtLeast(1, GetProperty(window, "SmokeCommandExecutionCount"), "window routed command execution count");
        InvokeVoid(mouseBindingCommand, "Execute", GetProperty(mouseBinding, "CommandParameter"), window);
        AssertEqual("mouse binding payload", GetProperty(window, "LastSmokeCommandParameter"), "window mouse binding command executed parameter");
        AssertEqual("mouse binding payload", GetProperty(commandStatus, "Text"), "command status after mouse binding command");

        object requeryCommandButton = Invoke(window, "FindName", "RequeryCommandButton");
        AssertType(requeryCommandButton, "System.Windows.Controls.Button", "requery command button");
        AssertEqual("Run requery command", GetProperty(requeryCommandButton, "Content"), "requery command button content");
        object commandViewModel = GetProperty(window, "DataContext");
        object requeryCommand = GetProperty(commandViewModel, "RequeryCommand");
        if (!requeryCommand.GetType().GetInterfaces().Any(type => string.Equals(type.FullName, "System.Windows.Input.ICommand", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"requery command: expected ICommand implementation, actual '{requeryCommand.GetType().FullName}'.");
        }

        AssertSame(requeryCommand, GetProperty(requeryCommandButton, "Command"), "requery command button command binding");
        object requeryCommandParameter = GetProperty(requeryCommandButton, "CommandParameter");
        AssertEqual("requery command payload", requeryCommandParameter, "requery command button parameter");

        if (flushDispatcherOperations is not null)
        {
            Type commandManagerType = GetRequiredType(commandButtonCommand.GetType().Assembly, "System.Windows.Input.CommandManager");
            SetProperty(requeryCommand, "CanExecuteValue", false);
            InvokeStaticVoid(commandManagerType, "InvalidateRequerySuggested");
            flushDispatcherOperations(window);
            AssertEqual(false, GetProperty(requeryCommandButton, "IsEnabled"), "SDK requery command disabled state");

            int requeryProbeBefore = Convert.ToInt32(GetProperty(requeryCommand, "CanExecuteProbeCount"));
            SetProperty(requeryCommand, "CanExecuteValue", true);
            InvokeStaticVoid(commandManagerType, "InvalidateRequerySuggested");
            flushDispatcherOperations(window);
            AssertEqual(true, GetProperty(requeryCommandButton, "IsEnabled"), "SDK requery command enabled state");
            AssertAtLeast(
                requeryProbeBefore + 1,
                GetProperty(requeryCommand, "CanExecuteProbeCount"),
                "SDK requery command can-execute probe count");

            int requeryExecuteBefore = Convert.ToInt32(GetProperty(requeryCommand, "ExecuteCount"));
            InvokeVoid(requeryCommand, "Execute", requeryCommandParameter);
            AssertEqual(requeryExecuteBefore + 1, GetProperty(requeryCommand, "ExecuteCount"), "SDK requery command execute count");
            AssertEqual(requeryCommandParameter, GetProperty(requeryCommand, "LastParameter"), "SDK requery command last parameter");
        }

        object eventSetterButton = Invoke(window, "FindName", "EventSetterButton");
        AssertType(eventSetterButton, "System.Windows.Controls.Button", "event setter button");
        AssertEqual("Run event setter", GetProperty(eventSetterButton, "Content"), "event setter button content");
        AssertEqual("event setter styled", GetProperty(eventSetterButton, "Tag"), "event setter button tag");
        object eventSetterButtonStyle = GetProperty(eventSetterButton, "Style");
        AssertType(eventSetterButtonStyle, "System.Windows.Style", "event setter button style");
        AssertEqual("System.Windows.Controls.Button", GetProperty(eventSetterButtonStyle, "TargetType").ToString() ?? string.Empty, "event setter button style target type");
        object eventSetter = FindFirstByType(GetProperty(eventSetterButtonStyle, "Setters"), "System.Windows.EventSetter", "event setter button style event setter");
        AssertEqual("ButtonBase.Click", GetProperty(eventSetter, "Event").ToString() ?? string.Empty, "event setter button routed event");
        object eventSetterStatus = Invoke(window, "FindName", "EventSetterStatus");
        AssertType(eventSetterStatus, "System.Windows.Controls.TextBlock", "event setter status");
        int eventSetterClickCountBefore = Convert.ToInt32(GetProperty(window, "EventSetterClickCount"));
        InvokeVoid(eventSetterButton, "OnClick");
        AssertAtLeast(eventSetterClickCountBefore + 1, GetProperty(window, "EventSetterClickCount"), "event setter click count");
        AssertEqual("EventSetterButton", GetProperty(window, "LastEventSetterSenderName"), "event setter sender name");
        AssertEqual("Click", GetProperty(window, "LastEventSetterRoutedEventName"), "event setter routed event name");
        AssertEqual("event setter clicked", GetProperty(eventSetterStatus, "Text"), "event setter status text");

        object actionToolTip = GetProperty(actionButton, "ToolTip");
        AssertType(actionToolTip, "System.Windows.Controls.ToolTip", "action button tooltip");
        AssertEqual("Action tooltip content", GetProperty(actionToolTip, "Content"), "action tooltip content");
        AssertEqual("Right", GetProperty(actionToolTip, "Placement").ToString() ?? string.Empty, "action tooltip placement");
        if (flushDispatcherOperations != null)
        {
            SetProperty(actionToolTip, "PlacementTarget", actionButton);
            AssertEqual(false, GetProperty(actionToolTip, "IsOpen"), "action tooltip initial open state");
            SetProperty(actionToolTip, "IsOpen", true);
            flushDispatcherOperations(window);
            AssertEqual(true, GetProperty(actionToolTip, "IsOpen"), "action tooltip opened through portable popup");
            SetProperty(actionToolTip, "IsOpen", false);
            flushDispatcherOperations(window);
            AssertEqual(false, GetProperty(actionToolTip, "IsOpen"), "action tooltip closed through portable popup");
        }

        object actionContextMenu = GetProperty(actionButton, "ContextMenu");
        AssertType(actionContextMenu, "System.Windows.Controls.ContextMenu", "action button context menu");
        if (flushDispatcherOperations != null)
        {
            SetProperty(actionContextMenu, "PlacementTarget", actionButton);
            AssertEqual(false, GetProperty(actionContextMenu, "IsOpen"), "action context menu initial open state");
            SetProperty(actionContextMenu, "IsOpen", true);
            flushDispatcherOperations(window);
            AssertEqual(true, GetProperty(actionContextMenu, "IsOpen"), "action context menu opened through portable popup");
            SetProperty(actionContextMenu, "IsOpen", false);
            flushDispatcherOperations(window);
            AssertEqual(false, GetProperty(actionContextMenu, "IsOpen"), "action context menu closed through portable popup");
        }

        object[] actionContextMenuItems = EnumerateObjects(GetProperty(actionContextMenu, "Items")).ToArray();
        AssertAtLeast(3, actionContextMenuItems.Length, "action context menu item count");
        object contextCommandMenuItem = actionContextMenuItems[0];
        AssertType(contextCommandMenuItem, "System.Windows.Controls.MenuItem", "context command menu item");
        AssertEqual("_Context command", GetProperty(contextCommandMenuItem, "Header"), "context command menu item header");
        object contextCommand = GetProperty(contextCommandMenuItem, "Command");
        AssertType(contextCommand, "System.Windows.Input.RoutedUICommand", "context command menu item command");
        object contextCommandParameter = GetProperty(contextCommandMenuItem, "CommandParameter");
        AssertEqual("context menu command payload", contextCommandParameter, "context command menu item parameter");
        int commandExecutionCountBeforeContextMenu = Convert.ToInt32(GetProperty(window, "SmokeCommandExecutionCount"));
        InvokeVoid(contextCommand, "Execute", contextCommandParameter, window);
        AssertAtLeast(commandExecutionCountBeforeContextMenu + 1, GetProperty(window, "SmokeCommandExecutionCount"), "context menu command execution count");
        AssertEqual("context menu command payload", GetProperty(window, "LastSmokeCommandParameter"), "context menu command payload observed");
        AssertEqual("context menu command payload", GetProperty(commandStatus, "Text"), "command status after context menu command");
        AssertType(actionContextMenuItems[1], "System.Windows.Controls.Separator", "action context menu separator");
        object contextCheckableMenuItem = actionContextMenuItems[2];
        AssertType(contextCheckableMenuItem, "System.Windows.Controls.MenuItem", "context checkable menu item");
        AssertEqual(true, GetProperty(contextCheckableMenuItem, "IsCheckable"), "context checkable menu item IsCheckable");
        object contextCheckableMenuItemChecked = GetProperty(contextCheckableMenuItem, "IsChecked");
        if (!object.Equals(true, contextCheckableMenuItemChecked)
            && !object.Equals(false, contextCheckableMenuItemChecked))
        {
            throw new InvalidOperationException($"Unexpected context checkable menu item checked state '{contextCheckableMenuItemChecked}'.");
        }

        SetProperty(contextCheckableMenuItem, "IsChecked", true);
        AssertEqual(true, GetProperty(contextCheckableMenuItem, "IsChecked"), "context checkable menu item checked");
        SetProperty(contextCheckableMenuItem, "IsChecked", false);
        AssertEqual(false, GetProperty(contextCheckableMenuItem, "IsChecked"), "context checkable menu item unchecked");

        object smokeMenu = Invoke(window, "FindName", "SmokeMenu");
        AssertType(smokeMenu, "System.Windows.Controls.Menu", "smoke menu");
        object smokeRootMenuItem = EnumerateObjects(GetProperty(smokeMenu, "Items")).First();
        AssertType(smokeRootMenuItem, "System.Windows.Controls.MenuItem", "smoke root menu item");
        object[] smokeMenuItems = EnumerateObjects(GetProperty(smokeRootMenuItem, "Items")).ToArray();
        AssertAtLeast(4, smokeMenuItems.Length, "smoke root menu item count");
        AssertType(smokeMenuItems[2], "System.Windows.Controls.Separator", "smoke menu separator");

        object commandMenuItem = Invoke(window, "FindName", "CommandMenuItem");
        AssertType(commandMenuItem, "System.Windows.Controls.MenuItem", "command menu item");
        object commandMenuItemCommand = GetProperty(commandMenuItem, "Command");
        AssertType(commandMenuItemCommand, "System.Windows.Input.RoutedUICommand", "command menu item command");
        AssertEqual("SmokeCommand", GetProperty(commandMenuItemCommand, "Name"), "command menu item command name");
        object commandMenuItemParameter = GetProperty(commandMenuItem, "CommandParameter");
        AssertEqual("menu command payload", commandMenuItemParameter, "command menu item command parameter");
        AssertSame(window, GetProperty(commandMenuItem, "CommandTarget"), "command menu item command target");
        AssertEqual(true, Invoke(commandMenuItemCommand, "CanExecute", commandMenuItemParameter, window), "command menu routed command CanExecute");
        int commandExecutionCountBeforeMenu = Convert.ToInt32(GetProperty(window, "SmokeCommandExecutionCount"));
        InvokeVoid(commandMenuItemCommand, "Execute", commandMenuItemParameter, window);
        AssertAtLeast(commandExecutionCountBeforeMenu + 1, GetProperty(window, "SmokeCommandExecutionCount"), "window routed command execution count after menu");
        AssertEqual("menu command payload", GetProperty(window, "LastSmokeCommandParameter"), "window routed command menu parameter");
        AssertEqual("menu command payload", GetProperty(commandStatus, "Text"), "command status after menu routed command");

        object menuStatus = Invoke(window, "FindName", "MenuStatus");
        AssertType(menuStatus, "System.Windows.Controls.TextBlock", "menu status element");
        object clickMenuItem = Invoke(window, "FindName", "ClickMenuItem");
        AssertType(clickMenuItem, "System.Windows.Controls.MenuItem", "click menu item");
        RaiseRoutedEvent(clickMenuItem, "ClickEvent");
        AssertAtLeast(1, GetProperty(window, "MenuClickCount"), "window menu click count");
        AssertEqual("menu click", GetProperty(menuStatus, "Text"), "menu status after click item");

        object checkableMenuItem = Invoke(window, "FindName", "CheckableMenuItem");
        AssertType(checkableMenuItem, "System.Windows.Controls.MenuItem", "checkable menu item");
        AssertEqual(true, GetProperty(checkableMenuItem, "IsCheckable"), "checkable menu item IsCheckable");
        AssertEqual(true, GetProperty(checkableMenuItem, "IsChecked"), "checkable menu item initial IsChecked");
        SetProperty(checkableMenuItem, "IsChecked", false);
        AssertEqual(false, GetProperty(checkableMenuItem, "IsChecked"), "checkable menu item toggled unchecked");
        AssertAtLeast(1, GetProperty(window, "MenuUncheckedCount"), "window menu unchecked count");
        AssertEqual("menu unchecked", GetProperty(menuStatus, "Text"), "menu status after unchecked item");
        SetProperty(checkableMenuItem, "IsChecked", true);
        AssertEqual(true, GetProperty(checkableMenuItem, "IsChecked"), "checkable menu item toggled checked");
        AssertAtLeast(1, GetProperty(window, "MenuCheckedCount"), "window menu checked count");
        AssertEqual("menu checked", GetProperty(menuStatus, "Text"), "menu status after checked item");

        object checkChoicePanel = Invoke(window, "FindName", "CheckChoicePanel");
        AssertType(checkChoicePanel, "System.Windows.Controls.StackPanel", "check choice panel");
        AssertAtLeast(2, GetCount(GetProperty(checkChoicePanel, "Children")), "check choice panel child count");
        object checkChoiceStatus = Invoke(window, "FindName", "CheckChoiceStatus");
        AssertType(checkChoiceStatus, "System.Windows.Controls.TextBlock", "check choice status element");

        object managedCheckBox = Invoke(window, "FindName", "ManagedCheckBox");
        AssertType(managedCheckBox, "System.Windows.Controls.CheckBox", "managed check box");
        AssertEqual("Managed check", GetProperty(managedCheckBox, "Content"), "managed check box content");
        AssertEqual(true, GetProperty(managedCheckBox, "IsChecked"), "managed check box initial checked state");
        int managedCheckBoxUncheckedCountBefore = Convert.ToInt32(GetProperty(window, "ManagedCheckBoxUncheckedCount"));
        InvokeVoid(managedCheckBox, "OnClick");
        AssertEqual(false, GetProperty(managedCheckBox, "IsChecked"), "managed check box unchecked by click");
        AssertAtLeast(managedCheckBoxUncheckedCountBefore + 1, GetProperty(window, "ManagedCheckBoxUncheckedCount"), "managed check box unchecked count");
        AssertEqual("check unchecked", GetProperty(checkChoiceStatus, "Text"), "check choice status after check unchecked");
        int managedCheckBoxCheckedCountBefore = Convert.ToInt32(GetProperty(window, "ManagedCheckBoxCheckedCount"));
        InvokeVoid(managedCheckBox, "OnClick");
        AssertEqual(true, GetProperty(managedCheckBox, "IsChecked"), "managed check box checked by click");
        AssertAtLeast(managedCheckBoxCheckedCountBefore + 1, GetProperty(window, "ManagedCheckBoxCheckedCount"), "managed check box checked count");
        AssertEqual("check checked", GetProperty(checkChoiceStatus, "Text"), "check choice status after check checked");

        object managedRadioAlpha = Invoke(window, "FindName", "ManagedRadioAlpha");
        AssertType(managedRadioAlpha, "System.Windows.Controls.RadioButton", "managed radio alpha");
        AssertEqual("Alpha", GetProperty(managedRadioAlpha, "Content"), "managed radio alpha content");
        AssertEqual("ManagedRadioGroup", GetProperty(managedRadioAlpha, "GroupName"), "managed radio alpha group");
        object managedRadioBeta = Invoke(window, "FindName", "ManagedRadioBeta");
        AssertType(managedRadioBeta, "System.Windows.Controls.RadioButton", "managed radio beta");
        AssertEqual("Beta", GetProperty(managedRadioBeta, "Content"), "managed radio beta content");
        AssertEqual("ManagedRadioGroup", GetProperty(managedRadioBeta, "GroupName"), "managed radio beta group");
        AssertEqual(true, GetProperty(managedRadioAlpha, "IsChecked"), "managed radio alpha initial checked state");
        AssertEqual(false, GetProperty(managedRadioBeta, "IsChecked"), "managed radio beta initial checked state");
        int managedRadioCheckedCountBefore = Convert.ToInt32(GetProperty(window, "ManagedRadioCheckedCount"));
        int managedRadioUncheckedCountBefore = Convert.ToInt32(GetProperty(window, "ManagedRadioUncheckedCount"));
        InvokeVoid(managedRadioBeta, "OnClick");
        AssertEqual(false, GetProperty(managedRadioAlpha, "IsChecked"), "managed radio alpha unchecked after beta click");
        AssertEqual(true, GetProperty(managedRadioBeta, "IsChecked"), "managed radio beta checked by click");
        AssertAtLeast(managedRadioCheckedCountBefore + 1, GetProperty(window, "ManagedRadioCheckedCount"), "managed radio checked count after beta");
        AssertAtLeast(managedRadioUncheckedCountBefore + 1, GetProperty(window, "ManagedRadioUncheckedCount"), "managed radio unchecked count after beta");
        AssertEqual("ManagedRadioBeta", GetProperty(window, "LastManagedRadioCheckedName"), "managed radio beta last checked name");
        AssertEqual("radio checked: ManagedRadioBeta", GetProperty(checkChoiceStatus, "Text"), "check choice status after beta radio");
        InvokeVoid(managedRadioAlpha, "OnClick");
        AssertEqual(true, GetProperty(managedRadioAlpha, "IsChecked"), "managed radio alpha rechecked by click");
        AssertEqual(false, GetProperty(managedRadioBeta, "IsChecked"), "managed radio beta unchecked after alpha click");
        AssertEqual("ManagedRadioAlpha", GetProperty(window, "LastManagedRadioCheckedName"), "managed radio alpha last checked name");
        AssertEqual("radio checked: ManagedRadioAlpha", GetProperty(checkChoiceStatus, "Text"), "check choice status after alpha radio");

        object propertyTriggerStatus = Invoke(window, "FindName", "PropertyTriggerStatus");
        AssertType(propertyTriggerStatus, "System.Windows.Controls.TextBlock", "property trigger status element");
        AssertEqual("property trigger active", GetProperty(propertyTriggerStatus, "Text"), "property trigger text");
        object propertyTriggerForeground = GetProperty(propertyTriggerStatus, "Foreground");
        AssertType(propertyTriggerForeground, "System.Windows.Media.SolidColorBrush", "property trigger foreground");
        AssertEqual("#FF356D9E", GetProperty(propertyTriggerForeground, "Color").ToString() ?? string.Empty, "property trigger foreground color");

        object dataTriggerStatus = Invoke(window, "FindName", "DataTriggerStatus");
        AssertType(dataTriggerStatus, "System.Windows.Controls.TextBlock", "data trigger status element");
        AssertEqual("data trigger active", GetProperty(dataTriggerStatus, "Text"), "data trigger text");
        object dataTriggerForeground = GetProperty(dataTriggerStatus, "Foreground");
        AssertType(dataTriggerForeground, "System.Windows.Media.SolidColorBrush", "data trigger foreground");
        AssertEqual("#FF6B8F3A", GetProperty(dataTriggerForeground, "Color").ToString() ?? string.Empty, "data trigger foreground color");

        object multiTriggerStatus = Invoke(window, "FindName", "MultiTriggerStatus");
        AssertType(multiTriggerStatus, "System.Windows.Controls.TextBlock", "multi trigger status element");
        AssertEqual("multi trigger active", GetProperty(multiTriggerStatus, "Text"), "multi trigger text");
        object multiTriggerForeground = GetProperty(multiTriggerStatus, "Foreground");
        AssertType(multiTriggerForeground, "System.Windows.Media.SolidColorBrush", "multi trigger foreground");
        AssertEqual("#FF356D9E", GetProperty(multiTriggerForeground, "Color").ToString() ?? string.Empty, "multi trigger foreground color");

        object multiDataTriggerStatus = Invoke(window, "FindName", "MultiDataTriggerStatus");
        AssertType(multiDataTriggerStatus, "System.Windows.Controls.TextBlock", "multi data trigger status element");
        AssertEqual("multi data trigger active", GetProperty(multiDataTriggerStatus, "Text"), "multi data trigger text");
        object multiDataTriggerForeground = GetProperty(multiDataTriggerStatus, "Foreground");
        AssertType(multiDataTriggerForeground, "System.Windows.Media.SolidColorBrush", "multi data trigger foreground");
        AssertEqual("#FF6B8F3A", GetProperty(multiDataTriggerForeground, "Color").ToString() ?? string.Empty, "multi data trigger foreground color");

        object loadedStoryboardText = Invoke(window, "FindName", "LoadedStoryboardText");
        AssertType(loadedStoryboardText, "System.Windows.Controls.TextBlock", "loaded storyboard TextBlock");
        AssertEqual("loaded storyboard target", GetProperty(loadedStoryboardText, "Text"), "loaded storyboard text");
        object loadedStoryboardTriggers = GetProperty(loadedStoryboardText, "Triggers");
        AssertEqual(1, GetCount(loadedStoryboardTriggers), "loaded storyboard trigger count");
        object loadedStoryboardTrigger = FindFirstByType(loadedStoryboardTriggers, "System.Windows.EventTrigger", "loaded storyboard EventTrigger");
        AssertEqual("Loaded", GetProperty(GetProperty(loadedStoryboardTrigger, "RoutedEvent"), "Name"), "loaded storyboard routed event");
        object loadedStoryboardActions = GetProperty(loadedStoryboardTrigger, "Actions");
        AssertEqual(1, GetCount(loadedStoryboardActions), "loaded storyboard action count");
        object beginStoryboard = FindFirstByType(loadedStoryboardActions, "System.Windows.Media.Animation.BeginStoryboard", "loaded storyboard BeginStoryboard");
        object loadedStoryboard = GetProperty(beginStoryboard, "Storyboard");
        AssertType(loadedStoryboard, "System.Windows.Media.Animation.Storyboard", "loaded storyboard");
        object loadedStoryboardChildren = GetProperty(loadedStoryboard, "Children");
        AssertEqual(1, GetCount(loadedStoryboardChildren), "loaded storyboard child count");
        object doubleAnimation = FindFirstByType(loadedStoryboardChildren, "System.Windows.Media.Animation.DoubleAnimation", "loaded storyboard DoubleAnimation");
        AssertEqual(0.42, GetProperty(doubleAnimation, "To"), "loaded storyboard DoubleAnimation target value");
        AssertEqual("00:00:00", GetProperty(doubleAnimation, "Duration").ToString() ?? string.Empty, "loaded storyboard DoubleAnimation duration");
        AssertEqual("HoldEnd", GetProperty(doubleAnimation, "FillBehavior").ToString() ?? string.Empty, "loaded storyboard DoubleAnimation fill behavior");
        Type storyboardType = loadedStoryboard.GetType();
        AssertEqual("LoadedStoryboardText", InvokeStatic(storyboardType, "GetTargetName", doubleAnimation), "loaded storyboard target name");
        object loadedStoryboardTargetProperty = InvokeStatic(storyboardType, "GetTargetProperty", doubleAnimation);
        AssertType(loadedStoryboardTargetProperty, "System.Windows.PropertyPath", "loaded storyboard target property path");
        AssertEqual("Opacity", GetProperty(loadedStoryboardTargetProperty, "Path"), "loaded storyboard target property");
        if (validateFrameContent)
        {
            flushDispatcherOperations?.Invoke(window);
            AssertClose(0.42, Convert.ToDouble(GetProperty(loadedStoryboardText, "Opacity")), 0.0001, "loaded storyboard post-Loaded opacity");
            AssertAtLeast(1, GetProperty(window, "LoadedStoryboardTextLoadedCount"), "SDK loaded storyboard handler count");
            AssertEqual("Loaded", GetProperty(window, "LastLoadedStoryboardTextRoutedEventName"), "SDK loaded storyboard routed event name");
        }
        else
        {
            AssertEqual(1.0, GetProperty(loadedStoryboardText, "Opacity"), "loaded storyboard initial opacity");
            AssertEqual(0, GetProperty(window, "LoadedStoryboardTextLoadedCount"), "SDK loaded storyboard initial handler count");
        }

        object basedOnResourceText = Invoke(window, "FindName", "BasedOnResourceText");
        AssertType(basedOnResourceText, "System.Windows.Controls.TextBlock", "based-on resource text element");
        AssertEqual("based-on resource style", GetProperty(basedOnResourceText, "Text"), "based-on resource text");
        AssertEqual("SemiBold", GetProperty(basedOnResourceText, "FontWeight").ToString() ?? string.Empty, "based-on resource inherited font weight");
        object basedOnResourceForeground = GetProperty(basedOnResourceText, "Foreground");
        AssertType(basedOnResourceForeground, "System.Windows.Media.SolidColorBrush", "based-on resource foreground");
        AssertEqual("#FF356D9E", GetProperty(basedOnResourceForeground, "Color").ToString() ?? string.Empty, "based-on resource foreground color");

        object providerGreetingText = Invoke(window, "FindName", "ProviderGreetingText");
        AssertType(providerGreetingText, "System.Windows.Controls.TextBlock", "provider greeting text element");
        AssertEqual("provider:7", GetProperty(providerGreetingText, "Text"), "provider greeting text");
        object xmlSmokeData = Invoke(window, "FindResource", "XmlSmokeData");
        AssertType(xmlSmokeData, "System.Windows.Data.XmlDataProvider", "window XML data provider");
        object xmlProviderText = Invoke(window, "FindName", "XmlProviderText");
        AssertType(xmlProviderText, "System.Windows.Controls.TextBlock", "XML provider text element");
        AssertEqual("xml", GetProperty(xmlProviderText, "Text"), "XML provider XPath binding text");

        object unsharedBrushBorder = Invoke(window, "FindName", "UnsharedBrushBorder");
        AssertType(unsharedBrushBorder, "System.Windows.Controls.Border", "unshared brush border");
        object unsharedBorderBrush = GetProperty(unsharedBrushBorder, "Background");
        AssertType(unsharedBorderBrush, "System.Windows.Media.SolidColorBrush", "unshared border brush");
        AssertEqual("#FFC45A2B", GetProperty(unsharedBorderBrush, "Color").ToString() ?? string.Empty, "unshared border brush color");

        object inputBox = Invoke(window, "FindName", "InputBox");
        AssertType(inputBox, "System.Windows.Controls.TextBox", "input box");
        AssertEqual("editable package text", GetProperty(inputBox, "Text"), "input box bound text");
        Assembly presentationCore = GetAssemblyFromContext(window.GetType().Assembly, "PresentationCore");
        ValidateInputScope(
            presentationCore,
            inputBox,
            "[0-9a-z]+",
            "sdk-input-scope",
            "EmailSmtpAddress",
            "package phrase",
            "SDK compiled BAML");
        object accessKeyFocusPanel = Invoke(window, "FindName", "AccessKeyFocusPanel");
        AssertType(accessKeyFocusPanel, "System.Windows.Controls.StackPanel", "access key focus panel");
        Type focusManagerType = GetRequiredType(presentationCore, "System.Windows.Input.FocusManager");
        AssertEqual(true, InvokeStatic(focusManagerType, "GetIsFocusScope", accessKeyFocusPanel), "access key focus scope flag");
        AssertSame(inputBox, InvokeStatic(focusManagerType, "GetFocusedElement", accessKeyFocusPanel), "access key focus initial focused element");
        Assembly presentationFramework = GetAssemblyFromContext(window.GetType().Assembly, "PresentationFramework");
        Type keyboardNavigationType = GetRequiredType(presentationFramework, "System.Windows.Input.KeyboardNavigation");
        AssertEqual("Cycle", InvokeStatic(keyboardNavigationType, "GetTabNavigation", accessKeyFocusPanel).ToString() ?? string.Empty, "access key tab navigation mode");
        AssertEqual("Cycle", InvokeStatic(keyboardNavigationType, "GetControlTabNavigation", accessKeyFocusPanel).ToString() ?? string.Empty, "access key control tab navigation mode");
        AssertEqual("Contained", InvokeStatic(keyboardNavigationType, "GetDirectionalNavigation", accessKeyFocusPanel).ToString() ?? string.Empty, "access key directional navigation mode");
        object inputAccessLabel = Invoke(window, "FindName", "InputAccessLabel");
        AssertType(inputAccessLabel, "System.Windows.Controls.Label", "input access label");
        AssertEqual("_Input access", GetProperty(inputAccessLabel, "Content"), "input access label content");
        AssertSame(inputBox, GetProperty(inputAccessLabel, "Target"), "input access label target");
        object standaloneAccessText = Invoke(window, "FindName", "StandaloneAccessText");
        AssertType(standaloneAccessText, "System.Windows.Controls.AccessText", "standalone access text");
        AssertEqual("_Standalone access", GetProperty(standaloneAccessText, "Text"), "standalone access text");
        object ancestorBindingBorder = Invoke(window, "FindName", "AncestorBindingBorder");
        AssertType(ancestorBindingBorder, "System.Windows.Controls.Border", "ancestor binding border");
        AssertEqual("ancestor binding value", GetProperty(ancestorBindingBorder, "Tag"), "ancestor binding border tag");
        object ancestorBindingText = Invoke(window, "FindName", "AncestorBindingText");
        AssertType(ancestorBindingText, "System.Windows.Controls.TextBlock", "ancestor binding text");
        Type ancestorBindingOperationsType = GetRequiredType(presentationFramework, "System.Windows.Data.BindingOperations");
        object ancestorTextProperty = GetStaticField(ancestorBindingText.GetType(), "TextProperty");
        object ancestorBindingExpression = InvokeStatic(ancestorBindingOperationsType, "GetBindingExpression", ancestorBindingText, ancestorTextProperty);
        AssertType(ancestorBindingExpression, "System.Windows.Data.BindingExpression", "ancestor binding expression");
        object ancestorParentBinding = GetProperty(ancestorBindingExpression, "ParentBinding");
        AssertEqual("Tag", GetBindingPath(ancestorParentBinding), "ancestor binding path");
        object ancestorRelativeSource = GetProperty(ancestorParentBinding, "RelativeSource");
        AssertEqual("FindAncestor", GetProperty(ancestorRelativeSource, "Mode").ToString() ?? string.Empty, "ancestor binding relative-source mode");
        AssertEqual("System.Windows.Controls.Border", GetProperty(ancestorRelativeSource, "AncestorType").ToString() ?? string.Empty, "ancestor binding relative-source type");
        if (flushDispatcherOperations is not null)
        {
            flushDispatcherOperations(window);
            AssertEqual("ancestor binding value", GetProperty(ancestorBindingText, "Text"), "ancestor binding resolved text");
        }
        object mutableStatusText = Invoke(window, "FindName", "MutableStatusText");
        AssertType(mutableStatusText, "System.Windows.Controls.TextBlock", "mutable status text element");
        AssertEqual("initial binding status", GetProperty(mutableStatusText, "Text"), "mutable status initial binding text");
        object validatedInputBox = Invoke(window, "FindName", "ValidatedInputBox");
        AssertType(validatedInputBox, "System.Windows.Controls.TextBox", "validated input box");
        AssertEqual("valid package text", GetProperty(validatedInputBox, "Text"), "validated input box initial text");
        object validationStatus = Invoke(window, "FindName", "ValidationStatus");
        AssertType(validationStatus, "System.Windows.Controls.TextBlock", "validation status element");
        Type validationType = GetRequiredType(validatedInputBox.GetType().Assembly, "System.Windows.Controls.Validation");
        AssertEqual(false, InvokeStatic(validationType, "GetHasError", validatedInputBox), "validated input initial validation state");
        AssertEqual("validation has error: False", GetProperty(validationStatus, "Text"), "validation status initial text");
        if (validateFrameContent)
        {
            object viewModel = GetProperty(window, "DataContext");
            SetProperty(viewModel, "MutableStatus", "updated binding status");
            flushDispatcherOperations?.Invoke(window);
            AssertEqual("updated binding status", GetProperty(mutableStatusText, "Text"), "mutable status property changed binding text");
            SetProperty(validatedInputBox, "Text", string.Empty);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(true, InvokeStatic(validationType, "GetHasError", validatedInputBox), "validated input empty validation state");
            AssertEqual("validation has error: True", GetProperty(validationStatus, "Text"), "validation status empty text");
            AssertEqual("valid package text", GetProperty(viewModel, "ValidationText"), "validated input rejected source update");
            SetProperty(validatedInputBox, "Text", "corrected package text");
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(false, InvokeStatic(validationType, "GetHasError", validatedInputBox), "validated input corrected validation state");
            AssertEqual("validation has error: False", GetProperty(validationStatus, "Text"), "validation status corrected text");
            AssertEqual("corrected package text", GetProperty(viewModel, "ValidationText"), "validated input corrected source update");
        }

        object credentialBox = Invoke(window, "FindName", "CredentialBox");
        AssertType(credentialBox, "System.Windows.Controls.PasswordBox", "credential password box");
        AssertEqual(12, GetProperty(credentialBox, "MaxLength"), "credential password box max length");
        AssertEqual('#', GetProperty(credentialBox, "PasswordChar"), "credential password box password char");
        AssertEqual(string.Empty, GetProperty(credentialBox, "Password"), "credential password box initial password");
        object credentialSecurePassword = GetProperty(credentialBox, "SecurePassword");
        AssertEqual(0, GetProperty(credentialSecurePassword, "Length"), "credential password box initial secure password length");
        object passwordStatus = Invoke(window, "FindName", "PasswordStatus");
        AssertType(passwordStatus, "System.Windows.Controls.TextBlock", "password status element");
        if (validateFrameContent)
        {
            int passwordChangedCountBefore = Convert.ToInt32(GetProperty(window, "PasswordChangedCount"));
            SetProperty(credentialBox, "Password", "secret42");
            AssertEqual("secret42", GetProperty(credentialBox, "Password"), "credential password box updated password");
            credentialSecurePassword = GetProperty(credentialBox, "SecurePassword");
            AssertEqual(8, GetProperty(credentialSecurePassword, "Length"), "credential password box secure password length");
            AssertAtLeast(passwordChangedCountBefore + 1, GetProperty(window, "PasswordChangedCount"), "credential password box changed count");
            AssertEqual("CredentialBox", GetProperty(window, "LastPasswordChangedSenderName"), "credential password box changed sender");
            AssertEqual("PasswordChanged", GetProperty(window, "LastPasswordChangedRoutedEventName"), "credential password box routed event");
            AssertEqual("password changed", GetProperty(passwordStatus, "Text"), "password status after change");
            int passwordClearCountBefore = Convert.ToInt32(GetProperty(window, "PasswordChangedCount"));
            InvokeVoid(credentialBox, "Clear");
            AssertEqual(string.Empty, GetProperty(credentialBox, "Password"), "credential password box cleared password");
            credentialSecurePassword = GetProperty(credentialBox, "SecurePassword");
            AssertEqual(0, GetProperty(credentialSecurePassword, "Length"), "credential password box cleared secure password length");
            AssertAtLeast(passwordClearCountBefore + 1, GetProperty(window, "PasswordChangedCount"), "credential password box clear changed count");
        }

        object calendarSmoke = Invoke(window, "FindName", "CalendarSmoke");
        AssertType(calendarSmoke, "System.Windows.Controls.Calendar", "calendar smoke");
        AssertEqual("Month", GetProperty(calendarSmoke, "DisplayMode").ToString() ?? string.Empty, "calendar smoke display mode");
        AssertEqual("SingleDate", GetProperty(calendarSmoke, "SelectionMode").ToString() ?? string.Empty, "calendar smoke selection mode");
        AssertEqual("Monday", GetProperty(calendarSmoke, "FirstDayOfWeek").ToString() ?? string.Empty, "calendar smoke first day");
        AssertEqual(false, GetProperty(calendarSmoke, "IsTodayHighlighted"), "calendar smoke today highlight");
        AssertDate(GetProperty(calendarSmoke, "DisplayDateStart"), 2026, 1, 1, "calendar smoke display start");
        AssertDate(GetProperty(calendarSmoke, "DisplayDateEnd"), 2026, 12, 31, "calendar smoke display end");
        AssertDate(GetProperty(calendarSmoke, "DisplayDate"), 2026, 6, 1, "calendar smoke display date");
        AssertDate(GetProperty(calendarSmoke, "SelectedDate"), 2026, 6, 17, "calendar smoke selected date");
        object calendarSelectedDates = GetProperty(calendarSmoke, "SelectedDates");
        AssertEqual(1, GetCount(calendarSelectedDates), "calendar smoke selected date count");
        AssertDate(EnumerateObjects(calendarSelectedDates).First(), 2026, 6, 17, "calendar smoke selected date collection item");

        object datePickerSmoke = Invoke(window, "FindName", "DatePickerSmoke");
        AssertType(datePickerSmoke, "System.Windows.Controls.DatePicker", "date picker smoke");
        AssertEqual("Monday", GetProperty(datePickerSmoke, "FirstDayOfWeek").ToString() ?? string.Empty, "date picker smoke first day");
        AssertEqual(false, GetProperty(datePickerSmoke, "IsTodayHighlighted"), "date picker smoke today highlight");
        AssertEqual("Short", GetProperty(datePickerSmoke, "SelectedDateFormat").ToString() ?? string.Empty, "date picker smoke selected date format");
        AssertDate(GetProperty(datePickerSmoke, "SelectedDate"), 2026, 6, 18, "date picker smoke selected date");
        object dateStatus = Invoke(window, "FindName", "DateStatus");
        AssertType(dateStatus, "System.Windows.Controls.TextBlock", "date status element");
        if (validateFrameContent)
        {
            int dateSelectionChangedCountBefore = Convert.ToInt32(GetProperty(window, "DateSelectionChangedCount"));
            SetProperty(calendarSmoke, "SelectedDate", new DateTime(2026, 6, 21));
            AssertDate(GetProperty(calendarSmoke, "SelectedDate"), 2026, 6, 21, "calendar smoke updated selected date");
            AssertDate(EnumerateObjects(calendarSelectedDates).First(), 2026, 6, 21, "calendar smoke updated selected date collection item");
            AssertAtLeast(dateSelectionChangedCountBefore + 1, GetProperty(window, "DateSelectionChangedCount"), "calendar smoke selection changed count");
            AssertEqual("CalendarSmoke", GetProperty(window, "LastDateSelectionChangedSenderName"), "calendar smoke selection changed sender");
            AssertEqual("date changed: CalendarSmoke", GetProperty(dateStatus, "Text"), "date status after calendar change");
            dateSelectionChangedCountBefore = Convert.ToInt32(GetProperty(window, "DateSelectionChangedCount"));
            SetProperty(datePickerSmoke, "SelectedDate", new DateTime(2026, 6, 22));
            AssertDate(GetProperty(datePickerSmoke, "SelectedDate"), 2026, 6, 22, "date picker smoke updated selected date");
            AssertAtLeast(dateSelectionChangedCountBefore + 1, GetProperty(window, "DateSelectionChangedCount"), "date picker smoke selection changed count");
            AssertEqual("DatePickerSmoke", GetProperty(window, "LastDateSelectionChangedSenderName"), "date picker smoke selection changed sender");
            AssertEqual("date changed: DatePickerSmoke", GetProperty(dateStatus, "Text"), "date status after date picker change");
        }

        object routedEventSource = Invoke(window, "FindName", "RoutedEventSource");
        AssertType(routedEventSource, "ProGPU.Wpf.SdkSwitchSmoke.SmokeRoutedEventSource", "custom routed event source");
        AssertAssignableTo(routedEventSource, "System.Windows.FrameworkElement", "custom routed event source base type");
        object routedEventStatus = Invoke(window, "FindName", "RoutedEventStatus");
        AssertType(routedEventStatus, "System.Windows.Controls.TextBlock", "custom routed event status element");
        if (object.Equals("routed event not raised", GetProperty(routedEventStatus, "Text")))
        {
            InvokeVoid(routedEventSource, "RaiseSmokeBubbled");
        }

        AssertAtLeast(1, GetProperty(window, "SmokeRoutedEventCount"), "custom routed event count");
        AssertSame(rootPanel, GetProperty(window, "LastSmokeRoutedEventSender"), "custom routed event bubbled sender");
        AssertSame(routedEventSource, GetProperty(window, "LastSmokeRoutedEventSource"), "custom routed event original source");
        AssertEqual("SmokeBubbled", GetProperty(routedEventStatus, "Text"), "custom routed event status text");

        object itemsList = Invoke(window, "FindName", "ItemsList");
        AssertType(itemsList, "System.Windows.Controls.ListBox", "items list");
        AssertEqual(1, GetProperty(itemsList, "SelectedIndex"), "items list selected index");
        AssertType(GetProperty(itemsList, "ItemTemplate"), "System.Windows.DataTemplate", "items list item template");
        AssertAtLeast(3, GetCount(GetProperty(itemsList, "Items")), "items list count");
        object selectedItem = GetProperty(itemsList, "SelectedItem");
        AssertEqual("Scene", GetProperty(selectedItem, "Name"), "selected item name");
        AssertEqual("ProGPU", GetProperty(selectedItem, "Value"), "selected item value");
        object smokeStatusBar = Invoke(window, "FindName", "SmokeStatusBar");
        AssertType(smokeStatusBar, "System.Windows.Controls.Primitives.StatusBar", "smoke status bar");
        AssertAtLeast(3, GetCount(GetProperty(smokeStatusBar, "Items")), "smoke status bar item count");
        object statusReadyItem = Invoke(window, "FindName", "StatusReadyItem");
        AssertType(statusReadyItem, "System.Windows.Controls.Primitives.StatusBarItem", "status ready item");
        AssertEqual("Ready", GetProperty(statusReadyItem, "Content"), "status ready item content");
        object statusTextBlock = Invoke(window, "FindName", "StatusTextBlock");
        AssertType(statusTextBlock, "System.Windows.Controls.TextBlock", "status text block");
        AssertEqual("status text", GetProperty(statusTextBlock, "Tag"), "status text block tag");
        AssertEqual(GetProperty(selectedItem, "Name"), GetProperty(statusTextBlock, "Text"), "status selected item text");
        object itemsCountText = Invoke(window, "FindName", "ItemsCountText");
        AssertType(itemsCountText, "System.Windows.Controls.TextBlock", "items count text element");
        AssertEqual("items: 3", GetProperty(itemsCountText, "Text"), "initial items count binding text");
        object panelItemsControl = Invoke(window, "FindName", "PanelItemsControl");
        AssertType(panelItemsControl, "System.Windows.Controls.ItemsControl", "panel items control");
        AssertAtLeast(3, GetCount(GetProperty(panelItemsControl, "Items")), "panel items control item count");
        AssertEqual(3, GetProperty(panelItemsControl, "AlternationCount"), "panel items alternation count");
        AssertEqual("panel item: {0}", GetProperty(panelItemsControl, "ItemStringFormat"), "panel items string format");
        object panelItemContainerStyle = GetProperty(panelItemsControl, "ItemContainerStyle");
        AssertType(panelItemContainerStyle, "System.Windows.Style", "panel items container style");
        AssertAtLeast(1, GetCount(GetProperty(panelItemContainerStyle, "Setters")), "panel items container style setter count");
        object panelItemsPanelTemplate = GetProperty(panelItemsControl, "ItemsPanel");
        AssertType(panelItemsPanelTemplate, "System.Windows.Controls.ItemsPanelTemplate", "panel items panel template");
        object panelItemsPanelRoot = Invoke(panelItemsPanelTemplate, "LoadContent");
        AssertType(panelItemsPanelRoot, "System.Windows.Controls.WrapPanel", "panel items panel root");
        AssertEqual("Horizontal", GetProperty(panelItemsPanelRoot, "Orientation").ToString() ?? string.Empty, "panel items panel orientation");
        object smokeListView = Invoke(window, "FindName", "SmokeListView");
        AssertType(smokeListView, "System.Windows.Controls.ListView", "smoke list view");
        AssertAtLeast(3, GetCount(GetProperty(smokeListView, "Items")), "smoke list view item count");
        AssertEqual(2, GetProperty(smokeListView, "SelectedIndex"), "smoke list view initial selected index");
        object listViewSelectedItem = GetProperty(smokeListView, "SelectedItem");
        AssertEqual("XAML", GetProperty(listViewSelectedItem, "Name"), "smoke list view selected item name");
        AssertEqual("compiled", GetProperty(listViewSelectedItem, "Value"), "smoke list view selected item value");
        object smokeGridView = GetProperty(smokeListView, "View");
        AssertType(smokeGridView, "System.Windows.Controls.GridView", "smoke list view grid view");
        object[] gridViewColumns = EnumerateObjects(GetProperty(smokeGridView, "Columns")).ToArray();
        AssertEqual(2, gridViewColumns.Length, "smoke list view grid view column count");
        AssertType(gridViewColumns[0], "System.Windows.Controls.GridViewColumn", "smoke list view name column");
        AssertEqual("Name", GetProperty(gridViewColumns[0], "Header"), "smoke list view name column header");
        object listViewNameBinding = GetProperty(gridViewColumns[0], "DisplayMemberBinding");
        AssertType(listViewNameBinding, "System.Windows.Data.Binding", "smoke list view name binding");
        AssertEqual("Name", GetBindingPath(listViewNameBinding), "smoke list view name binding path");
        AssertType(gridViewColumns[1], "System.Windows.Controls.GridViewColumn", "smoke list view value column");
        AssertEqual("Value", GetProperty(gridViewColumns[1], "Header"), "smoke list view value column header");
        object listViewValueBinding = GetProperty(gridViewColumns[1], "DisplayMemberBinding");
        AssertType(listViewValueBinding, "System.Windows.Data.Binding", "smoke list view value binding");
        AssertEqual("Value", GetBindingPath(listViewValueBinding), "smoke list view value binding path");
        object listViewStatus = Invoke(window, "FindName", "ListViewStatus");
        AssertType(listViewStatus, "System.Windows.Controls.TextBlock", "list view status element");
        AssertEqual("list view: compiled", GetProperty(listViewStatus, "Text"), "list view initial selected text");
        if (validateFrameContent)
        {
            SetProperty(smokeListView, "SelectedIndex", 1);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(1, GetProperty(smokeListView, "SelectedIndex"), "smoke list view changed selected index");
            object changedListViewSelectedItem = GetProperty(smokeListView, "SelectedItem");
            AssertEqual("Scene", GetProperty(changedListViewSelectedItem, "Name"), "smoke list view changed selected item");
            AssertEqual("list view: ProGPU", GetProperty(listViewStatus, "Text"), "list view changed selected text");
            SetProperty(smokeListView, "SelectedIndex", 2);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual("list view: compiled", GetProperty(listViewStatus, "Text"), "list view restored selected text");
        }

        object multiBindingSummaryText = Invoke(window, "FindName", "MultiBindingSummaryText");
        AssertType(multiBindingSummaryText, "System.Windows.Controls.TextBlock", "multi binding summary text element");
        AssertEqual("Scene:ProGPU", GetProperty(multiBindingSummaryText, "Text"), "multi binding converter text");

        object priorityBindingText = Invoke(window, "FindName", "PriorityBindingText");
        AssertType(priorityBindingText, "System.Windows.Controls.TextBlock", "priority binding text");
        AssertEqual("ProGPU WPF SDK switch managed subsystem smoke", GetProperty(priorityBindingText, "Text"), "priority binding fallback text");
        Type bindingOperationsType = GetRequiredType(presentationFramework, "System.Windows.Data.BindingOperations");
        object textProperty = GetStaticField(priorityBindingText.GetType(), "TextProperty");
        object priorityBindingExpression = InvokeStatic(bindingOperationsType, "GetPriorityBindingExpression", priorityBindingText, textProperty);
        AssertType(priorityBindingExpression, "System.Windows.Data.PriorityBindingExpression", "priority binding expression");
        object priorityBindingExpressions = GetProperty(priorityBindingExpression, "BindingExpressions");
        AssertEqual(2, GetCount(priorityBindingExpressions), "priority binding expression child count");
        object parentPriorityBinding = GetProperty(priorityBindingExpression, "ParentPriorityBinding");
        AssertEqual(2, GetCount(GetProperty(parentPriorityBinding, "Bindings")), "priority binding child binding count");
        object activeBindingExpression = GetProperty(priorityBindingExpression, "ActiveBindingExpression");
        AssertType(activeBindingExpression, "System.Windows.Data.BindingExpression", "priority binding active expression");
        AssertEqual("Title", GetBindingPath(GetProperty(activeBindingExpression, "ParentBinding")), "priority binding active path");

        object selectedItemPresenter = Invoke(window, "FindName", "SelectedItemPresenter");
        AssertType(selectedItemPresenter, "System.Windows.Controls.ContentControl", "selected item presenter");
        AssertSame(selectedItem, GetProperty(selectedItemPresenter, "Content"), "selected item presenter content");
        AssertType(GetProperty(selectedItemPresenter, "ContentTemplate"), "System.Windows.DataTemplate", "selected item presenter template");
        Type dataTemplateKeyType = GetRequiredType(presentationFramework, "System.Windows.DataTemplateKey");
        object implicitTemplateKey = Create(dataTemplateKeyType, selectedItem.GetType());
        object implicitItemTemplate = Invoke(window, "FindResource", implicitTemplateKey);
        AssertType(implicitItemTemplate, "System.Windows.DataTemplate", "implicit item data template");
        object implicitTemplateRoot = Invoke(implicitItemTemplate, "LoadContent");
        AssertType(implicitTemplateRoot, "System.Windows.Controls.TextBlock", "implicit item template root");
        object implicitTemplateTextProperty = GetStaticField(implicitTemplateRoot.GetType(), "TextProperty");
        object implicitTemplateBindingExpression = InvokeStatic(bindingOperationsType, "GetBindingExpression", implicitTemplateRoot, implicitTemplateTextProperty);
        AssertType(implicitTemplateBindingExpression, "System.Windows.Data.BindingExpression", "implicit item template binding expression");
        AssertEqual("Name", GetBindingPath(GetProperty(implicitTemplateBindingExpression, "ParentBinding")), "implicit item template binding path");
        object implicitItemPresenter = Invoke(window, "FindName", "ImplicitItemPresenter");
        AssertType(implicitItemPresenter, "System.Windows.Controls.ContentPresenter", "implicit item presenter");
        AssertSame(selectedItem, GetProperty(implicitItemPresenter, "Content"), "implicit item presenter content");
        if (flushDispatcherOperations is not null)
        {
            SetProperty(implicitTemplateRoot, "DataContext", selectedItem);
            flushDispatcherOperations(window);
            AssertEqual("implicit: Scene", GetProperty(implicitTemplateRoot, "Text"), "implicit item template resolved text");
        }
        object implicitStylePanel = Invoke(window, "FindName", "ImplicitStylePanel");
        AssertType(implicitStylePanel, "System.Windows.Controls.StackPanel", "implicit style panel");
        object implicitStyledText = Invoke(window, "FindName", "ImplicitStyledText");
        AssertType(implicitStyledText, "System.Windows.Controls.TextBlock", "implicit styled text");
        AssertEqual("implicit style text", GetProperty(implicitStyledText, "Text"), "implicit styled text content");
        AssertEqual("implicit style active", GetProperty(implicitStyledText, "Tag"), "implicit styled text tag");
        object implicitTextStyle = GetProperty(implicitStyledText, "Style");
        AssertType(implicitTextStyle, "System.Windows.Style", "implicit text style");
        AssertEqual("System.Windows.Controls.TextBlock", GetProperty(implicitTextStyle, "TargetType").ToString() ?? string.Empty, "implicit text style target type");
        AssertAtLeast(2, GetCount(GetProperty(implicitTextStyle, "Setters")), "implicit text style setter count");
        object implicitStyledForeground = GetProperty(implicitStyledText, "Foreground");
        AssertType(implicitStyledForeground, "System.Windows.Media.SolidColorBrush", "implicit styled text foreground");
        AssertEqual("#FF356D9E", GetProperty(implicitStyledForeground, "Color").ToString() ?? string.Empty, "implicit styled text foreground color");

        object layoutGrid = Invoke(window, "FindName", "LayoutGrid");
        AssertType(layoutGrid, "System.Windows.Controls.Grid", "layout grid");
        AssertEqual(2, GetCount(GetProperty(layoutGrid, "RowDefinitions")), "layout grid row definition count");
        AssertEqual(2, GetCount(GetProperty(layoutGrid, "ColumnDefinitions")), "layout grid column definition count");
        Type gridType = layoutGrid.GetType();
        object layoutLabel = Invoke(window, "FindName", "LayoutLabel");
        AssertType(layoutLabel, "System.Windows.Controls.TextBlock", "layout label element");
        AssertEqual("Selected:", GetProperty(layoutLabel, "Text"), "layout label text");
        AssertEqual(0, InvokeStatic(gridType, "GetRow", layoutLabel), "layout label grid row");
        AssertEqual(0, InvokeStatic(gridType, "GetColumn", layoutLabel), "layout label grid column");
        object convertedSelectedItemText = Invoke(window, "FindName", "ConvertedSelectedItemText");
        AssertType(convertedSelectedItemText, "System.Windows.Controls.TextBlock", "converted selected item text element");
        AssertEqual("Scene=ProGPU/Rendering", GetProperty(convertedSelectedItemText, "Text"), "converted selected item text");
        AssertEqual(0, InvokeStatic(gridType, "GetRow", convertedSelectedItemText), "converted selected item grid row");
        AssertEqual(1, InvokeStatic(gridType, "GetColumn", convertedSelectedItemText), "converted selected item grid column");
        object formattedInputText = Invoke(window, "FindName", "FormattedInputText");
        AssertType(formattedInputText, "System.Windows.Controls.TextBlock", "formatted input text element");
        AssertEqual("Input: editable package text", GetProperty(formattedInputText, "Text"), "formatted input binding text");
        AssertEqual(1, InvokeStatic(gridType, "GetRow", formattedInputText), "formatted input grid row");
        AssertEqual(0, InvokeStatic(gridType, "GetColumn", formattedInputText), "formatted input grid column");
        AssertEqual(2, InvokeStatic(gridType, "GetColumnSpan", formattedInputText), "formatted input grid column span");

        object dockLayoutPanel = Invoke(window, "FindName", "DockLayoutPanel");
        AssertType(dockLayoutPanel, "System.Windows.Controls.DockPanel", "dock layout panel");
        AssertEqual(true, GetProperty(dockLayoutPanel, "LastChildFill"), "dock layout last child fill");
        AssertAtLeast(3, GetCount(GetProperty(dockLayoutPanel, "Children")), "dock layout child count");
        Type dockPanelType = dockLayoutPanel.GetType();
        object dockTopText = Invoke(window, "FindName", "DockTopText");
        AssertType(dockTopText, "System.Windows.Controls.TextBlock", "dock top text element");
        AssertEqual("dock top", GetProperty(dockTopText, "Text"), "dock top text");
        AssertEqual("Top", InvokeStatic(dockPanelType, "GetDock", dockTopText).ToString() ?? string.Empty, "dock layout top dock");
        object dockLeftText = Invoke(window, "FindName", "DockLeftText");
        AssertType(dockLeftText, "System.Windows.Controls.TextBlock", "dock left text element");
        AssertEqual("dock left", GetProperty(dockLeftText, "Text"), "dock left text");
        AssertEqual(72.0, GetProperty(dockLeftText, "Width"), "dock left width");
        AssertEqual("Left", InvokeStatic(dockPanelType, "GetDock", dockLeftText).ToString() ?? string.Empty, "dock layout left dock");
        object dockFillText = Invoke(window, "FindName", "DockFillText");
        AssertType(dockFillText, "System.Windows.Controls.TextBlock", "dock fill text element");
        AssertEqual("dock fill: Scene", GetProperty(dockFillText, "Text"), "dock fill binding text");

        object canvasLayoutPanel = Invoke(window, "FindName", "CanvasLayoutPanel");
        AssertType(canvasLayoutPanel, "System.Windows.Controls.Canvas", "canvas layout panel");
        AssertAtLeast(1, GetCount(GetProperty(canvasLayoutPanel, "Children")), "canvas layout child count");
        Type canvasType = canvasLayoutPanel.GetType();
        object canvasPositionedText = Invoke(window, "FindName", "CanvasPositionedText");
        AssertType(canvasPositionedText, "System.Windows.Controls.TextBlock", "canvas positioned text element");
        AssertEqual("canvas positioned", GetProperty(canvasPositionedText, "Text"), "canvas positioned text");
        AssertEqual(12.0, InvokeStatic(canvasType, "GetLeft", canvasPositionedText), "canvas positioned left");
        AssertEqual(8.0, InvokeStatic(canvasType, "GetTop", canvasPositionedText), "canvas positioned top");

        object uniformLayoutPanel = Invoke(window, "FindName", "UniformLayoutPanel");
        AssertType(uniformLayoutPanel, "System.Windows.Controls.Primitives.UniformGrid", "uniform layout panel");
        AssertEqual(1, GetProperty(uniformLayoutPanel, "Rows"), "uniform layout rows");
        AssertEqual(3, GetProperty(uniformLayoutPanel, "Columns"), "uniform layout columns");
        AssertEqual(3, GetCount(GetProperty(uniformLayoutPanel, "Children")), "uniform layout child count");
        object uniformCellOne = Invoke(window, "FindName", "UniformCellOne");
        AssertType(uniformCellOne, "System.Windows.Controls.TextBlock", "uniform cell one");
        AssertEqual("one", GetProperty(uniformCellOne, "Text"), "uniform cell one text");
        object uniformCellThree = Invoke(window, "FindName", "UniformCellThree");
        AssertType(uniformCellThree, "System.Windows.Controls.TextBlock", "uniform cell three");
        AssertEqual("three", GetProperty(uniformCellThree, "Text"), "uniform cell three text");

        object groupedItemsViewSource = Invoke(window, "FindResource", "GroupedItems");
        AssertType(groupedItemsViewSource, "System.Windows.Data.CollectionViewSource", "grouped items collection view source");
        AssertAtLeast(1, GetCount(GetProperty(groupedItemsViewSource, "SortDescriptions")), "grouped items sort description count");
        AssertAtLeast(1, GetCount(GetProperty(groupedItemsViewSource, "GroupDescriptions")), "grouped items group description count");
        object groupedItemsView = GetProperty(groupedItemsViewSource, "View");
        AssertAtLeast(1, GetCount(GetProperty(groupedItemsView, "Groups")), "grouped items view group count");

        object groupedItemsControl = Invoke(window, "FindName", "GroupedItemsControl");
        AssertType(groupedItemsControl, "System.Windows.Controls.ItemsControl", "grouped items control");
        AssertType(GetProperty(groupedItemsControl, "ItemTemplate"), "System.Windows.DataTemplate", "grouped items item template");
        object groupedItemsGroupStyle = EnumerateObjects(GetProperty(groupedItemsControl, "GroupStyle")).FirstOrDefault()
            ?? throw new InvalidOperationException("Expected a grouped items GroupStyle entry.");
        AssertType(groupedItemsGroupStyle, "System.Windows.Controls.GroupStyle", "grouped items group style");
        AssertType(GetProperty(groupedItemsGroupStyle, "HeaderTemplate"), "System.Windows.DataTemplate", "grouped items group header template");

        object selectorItemsControl = Invoke(window, "FindName", "SelectorItemsControl");
        AssertType(selectorItemsControl, "System.Windows.Controls.ItemsControl", "selector items control");
        AssertAtLeast(3, GetCount(GetProperty(selectorItemsControl, "Items")), "selector items control count");
        object itemTemplateSelector = GetProperty(selectorItemsControl, "ItemTemplateSelector");
        AssertType(itemTemplateSelector, "ProGPU.Wpf.SdkSwitchSmoke.SmokeItemTemplateSelector", "smoke item template selector");
        object frameworkItemTemplate = Invoke(window, "FindResource", "SmokeFrameworkItemTemplate");
        object renderingItemTemplate = Invoke(window, "FindResource", "SmokeRenderingItemTemplate");
        AssertType(frameworkItemTemplate, "System.Windows.DataTemplate", "framework item data template");
        AssertType(renderingItemTemplate, "System.Windows.DataTemplate", "rendering item data template");
        object firstItem = EnumerateObjects(GetProperty(itemsList, "Items")).First();
        AssertSame(frameworkItemTemplate, Invoke(itemTemplateSelector, "SelectTemplate", firstItem, selectorItemsControl), "framework item selected template");
        AssertSame(renderingItemTemplate, Invoke(itemTemplateSelector, "SelectTemplate", selectedItem, selectorItemsControl), "rendering item selected template");

        object smokeDataGrid = Invoke(window, "FindName", "SmokeDataGrid");
        AssertType(smokeDataGrid, "System.Windows.Controls.DataGrid", "smoke data grid");
        AssertEqual(false, GetProperty(smokeDataGrid, "AutoGenerateColumns"), "smoke data grid AutoGenerateColumns");
        AssertEqual(false, GetProperty(smokeDataGrid, "CanUserAddRows"), "smoke data grid CanUserAddRows");
        AssertAtLeast(3, GetCount(GetProperty(smokeDataGrid, "Items")), "smoke data grid item count");
        AssertEqual(1, GetProperty(smokeDataGrid, "SelectedIndex"), "smoke data grid initial selected index");
        object dataGridSelectedItem = GetProperty(smokeDataGrid, "SelectedItem");
        AssertEqual("Scene", GetProperty(dataGridSelectedItem, "Name"), "smoke data grid selected item name");
        AssertEqual(false, GetProperty(dataGridSelectedItem, "IsActive"), "smoke data grid selected item active");
        object[] dataGridColumns = EnumerateObjects(GetProperty(smokeDataGrid, "Columns")).ToArray();
        AssertEqual(3, dataGridColumns.Length, "smoke data grid column count");
        AssertType(dataGridColumns[0], "System.Windows.Controls.DataGridTextColumn", "smoke data grid name column");
        AssertEqual("Name", GetProperty(dataGridColumns[0], "Header"), "smoke data grid name column header");
        object dataGridNameBinding = GetProperty(dataGridColumns[0], "Binding");
        AssertType(dataGridNameBinding, "System.Windows.Data.Binding", "smoke data grid name binding");
        AssertEqual("Name", GetBindingPath(dataGridNameBinding), "smoke data grid name binding path");
        AssertType(dataGridColumns[1], "System.Windows.Controls.DataGridTextColumn", "smoke data grid category column");
        AssertEqual("Category", GetProperty(dataGridColumns[1], "Header"), "smoke data grid category column header");
        object dataGridCategoryBinding = GetProperty(dataGridColumns[1], "Binding");
        AssertType(dataGridCategoryBinding, "System.Windows.Data.Binding", "smoke data grid category binding");
        AssertEqual("Category", GetBindingPath(dataGridCategoryBinding), "smoke data grid category binding path");
        AssertType(dataGridColumns[2], "System.Windows.Controls.DataGridCheckBoxColumn", "smoke data grid active column");
        AssertEqual("Active", GetProperty(dataGridColumns[2], "Header"), "smoke data grid active column header");
        object dataGridActiveBinding = GetProperty(dataGridColumns[2], "Binding");
        AssertType(dataGridActiveBinding, "System.Windows.Data.Binding", "smoke data grid active binding");
        AssertEqual("IsActive", GetBindingPath(dataGridActiveBinding), "smoke data grid active binding path");
        object dataGridStatus = Invoke(window, "FindName", "DataGridStatus");
        AssertType(dataGridStatus, "System.Windows.Controls.TextBlock", "data grid status element");
        AssertEqual("data grid: Scene", GetProperty(dataGridStatus, "Text"), "data grid initial selected text");
        if (validateFrameContent)
        {
            SetProperty(smokeDataGrid, "SelectedIndex", 2);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(2, GetProperty(smokeDataGrid, "SelectedIndex"), "smoke data grid changed selected index");
            object changedDataGridSelectedItem = GetProperty(smokeDataGrid, "SelectedItem");
            AssertEqual("XAML", GetProperty(changedDataGridSelectedItem, "Name"), "smoke data grid changed selected item");
            AssertEqual(true, GetProperty(changedDataGridSelectedItem, "IsActive"), "smoke data grid changed selected active");
            AssertEqual("data grid: XAML", GetProperty(dataGridStatus, "Text"), "data grid changed selected text");
            SetProperty(smokeDataGrid, "SelectedIndex", 1);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual("data grid: Scene", GetProperty(dataGridStatus, "Text"), "data grid restored selected text");
        }

        object smokeComboBox = Invoke(window, "FindName", "SmokeComboBox");
        AssertType(smokeComboBox, "System.Windows.Controls.ComboBox", "smoke combo box");
        AssertAtLeast(3, GetCount(GetProperty(smokeComboBox, "Items")), "smoke combo box item count");
        AssertEqual("Name", GetProperty(smokeComboBox, "DisplayMemberPath"), "smoke combo box display member path");
        AssertEqual("Value", GetProperty(smokeComboBox, "SelectedValuePath"), "smoke combo box selected value path");
        AssertEqual(1, GetProperty(smokeComboBox, "SelectedIndex"), "smoke combo box initial selected index");
        AssertEqual("ProGPU", GetProperty(smokeComboBox, "SelectedValue"), "smoke combo box initial selected value");
        object comboSelectedItem = GetProperty(smokeComboBox, "SelectedItem");
        AssertEqual("Scene", GetProperty(comboSelectedItem, "Name"), "smoke combo box initial selected item name");
        object selectorStatus = Invoke(window, "FindName", "SelectorStatus");
        AssertType(selectorStatus, "System.Windows.Controls.TextBlock", "selector status element");
        if (validateFrameContent)
        {
            int selectorSelectionCountBefore = Convert.ToInt32(GetProperty(window, "SelectorSelectionChangedCount"));
            SetProperty(smokeComboBox, "SelectedIndex", 2);
            AssertEqual(2, GetProperty(smokeComboBox, "SelectedIndex"), "smoke combo box changed selected index");
            AssertEqual("compiled", GetProperty(smokeComboBox, "SelectedValue"), "smoke combo box changed selected value");
            object changedComboSelectedItem = GetProperty(smokeComboBox, "SelectedItem");
            AssertEqual("XAML", GetProperty(changedComboSelectedItem, "Name"), "smoke combo box changed selected item");
            AssertAtLeast(selectorSelectionCountBefore + 1, GetProperty(window, "SelectorSelectionChangedCount"), "selector selection changed count");
            AssertEqual("selector selected: compiled", GetProperty(selectorStatus, "Text"), "selector status after combo selection");
        }

        object smokeTabs = Invoke(window, "FindName", "SmokeTabs");
        AssertType(smokeTabs, "System.Windows.Controls.TabControl", "smoke tab control");
        AssertAtLeast(2, GetCount(GetProperty(smokeTabs, "Items")), "smoke tab item count");
        AssertEqual(1, GetProperty(smokeTabs, "SelectedIndex"), "smoke tab initial selected index");
        object frameworkTab = Invoke(window, "FindName", "FrameworkTab");
        AssertType(frameworkTab, "System.Windows.Controls.TabItem", "framework tab item");
        AssertEqual("Framework", GetProperty(frameworkTab, "Header"), "framework tab header");
        object renderingTab = Invoke(window, "FindName", "RenderingTab");
        AssertType(renderingTab, "System.Windows.Controls.TabItem", "rendering tab item");
        AssertEqual("Rendering", GetProperty(renderingTab, "Header"), "rendering tab header");
        AssertSame(renderingTab, GetProperty(smokeTabs, "SelectedItem"), "smoke tab initial selected item");
        object tabStatus = Invoke(window, "FindName", "TabStatus");
        AssertType(tabStatus, "System.Windows.Controls.TextBlock", "tab status element");
        if (validateFrameContent)
        {
            int tabSelectionCountBefore = Convert.ToInt32(GetProperty(window, "TabSelectionChangedCount"));
            SetProperty(smokeTabs, "SelectedIndex", 0);
            AssertEqual(0, GetProperty(smokeTabs, "SelectedIndex"), "smoke tab changed selected index");
            AssertSame(frameworkTab, GetProperty(smokeTabs, "SelectedItem"), "smoke tab changed selected item");
            AssertAtLeast(tabSelectionCountBefore + 1, GetProperty(window, "TabSelectionChangedCount"), "tab selection changed count");
            AssertEqual("tab selected: Framework", GetProperty(tabStatus, "Text"), "tab status after tab selection");
        }

        object smokeToolBarTray = Invoke(window, "FindName", "SmokeToolBarTray");
        AssertType(smokeToolBarTray, "System.Windows.Controls.ToolBarTray", "smoke toolbar tray");
        AssertAtLeast(1, GetCount(GetProperty(smokeToolBarTray, "ToolBars")), "smoke toolbar tray toolbar count");
        object smokeToolBar = Invoke(window, "FindName", "SmokeToolBar");
        AssertType(smokeToolBar, "System.Windows.Controls.ToolBar", "smoke toolbar");
        AssertEqual("Smoke tools", GetProperty(smokeToolBar, "Header"), "smoke toolbar header");
        AssertAtLeast(3, GetCount(GetProperty(smokeToolBar, "Items")), "smoke toolbar item count");

        object toolBarCommandButton = Invoke(window, "FindName", "ToolBarCommandButton");
        AssertType(toolBarCommandButton, "System.Windows.Controls.Button", "toolbar command button");
        object toolbarCommand = GetProperty(toolBarCommandButton, "Command");
        AssertType(toolbarCommand, "System.Windows.Input.RoutedUICommand", "toolbar command button command");
        object toolbarCommandParameter = GetProperty(toolBarCommandButton, "CommandParameter");
        AssertEqual("toolbar command payload", toolbarCommandParameter, "toolbar command parameter");
        AssertSame(window, GetProperty(toolBarCommandButton, "CommandTarget"), "toolbar command target");
        int commandExecutionCountBeforeToolbar = Convert.ToInt32(GetProperty(window, "SmokeCommandExecutionCount"));
        InvokeVoid(toolBarCommandButton, "OnClick");
        AssertAtLeast(commandExecutionCountBeforeToolbar + 1, GetProperty(window, "SmokeCommandExecutionCount"), "toolbar command execution count");
        AssertEqual("toolbar command payload", GetProperty(window, "LastSmokeCommandParameter"), "toolbar command payload observed");
        AssertEqual("toolbar command payload", GetProperty(commandStatus, "Text"), "command status after toolbar command");

        object toolBarSeparator = Invoke(window, "FindName", "ToolBarSeparator");
        AssertType(toolBarSeparator, "System.Windows.Controls.Separator", "toolbar separator");
        object toolBarToggle = Invoke(window, "FindName", "ToolBarToggle");
        AssertType(toolBarToggle, "System.Windows.Controls.Primitives.ToggleButton", "toolbar toggle");
        object toolBarToggleChecked = GetProperty(toolBarToggle, "IsChecked");
        if (!object.Equals(true, toolBarToggleChecked)
            && !object.Equals(false, toolBarToggleChecked))
        {
            throw new InvalidOperationException($"Unexpected toolbar toggle checked state '{toolBarToggleChecked}'.");
        }

        SetProperty(toolBarToggle, "IsChecked", true);
        AssertEqual(true, GetProperty(toolBarToggle, "IsChecked"), "toolbar toggle checked");
        SetProperty(toolBarToggle, "IsChecked", false);
        AssertEqual(false, GetProperty(toolBarToggle, "IsChecked"), "toolbar toggle unchecked");

        object smokeGroupBox = Invoke(window, "FindName", "SmokeGroupBox");
        AssertType(smokeGroupBox, "System.Windows.Controls.GroupBox", "smoke group box");
        AssertEqual("Managed range", GetProperty(smokeGroupBox, "Header"), "smoke group box header");
        AssertType(GetProperty(smokeGroupBox, "Content"), "System.Windows.Controls.StackPanel", "smoke group box content");

        object smokeExpander = Invoke(window, "FindName", "SmokeExpander");
        AssertType(smokeExpander, "System.Windows.Controls.Expander", "smoke expander");
        AssertEqual("Range details", GetProperty(smokeExpander, "Header"), "smoke expander header");
        object smokeExpanderIsExpanded = GetProperty(smokeExpander, "IsExpanded");
        if (!object.Equals(true, smokeExpanderIsExpanded)
            && !object.Equals(false, smokeExpanderIsExpanded))
        {
            throw new InvalidOperationException($"Unexpected smoke expander state '{smokeExpanderIsExpanded}'.");
        }

        object smokeScrollViewer = Invoke(window, "FindName", "SmokeScrollViewer");
        AssertType(smokeScrollViewer, "System.Windows.Controls.ScrollViewer", "smoke scroll viewer");
        AssertEqual("Auto", GetProperty(smokeScrollViewer, "VerticalScrollBarVisibility").ToString() ?? string.Empty, "smoke scroll viewer vertical visibility");
        AssertEqual("Disabled", GetProperty(smokeScrollViewer, "HorizontalScrollBarVisibility").ToString() ?? string.Empty, "smoke scroll viewer horizontal visibility");
        object scrollContentPanel = Invoke(window, "FindName", "ScrollContentPanel");
        AssertType(scrollContentPanel, "System.Windows.Controls.StackPanel", "scroll content panel");
        AssertAtLeast(3, GetCount(GetProperty(scrollContentPanel, "Children")), "scroll content child count");

        object rangeStatus = Invoke(window, "FindName", "RangeStatus");
        AssertType(rangeStatus, "System.Windows.Controls.TextBlock", "range status element");
        SetProperty(smokeExpander, "IsExpanded", true);
        int expanderCollapsedCountBefore = Convert.ToInt32(GetProperty(window, "ExpanderCollapsedCount"));
        SetProperty(smokeExpander, "IsExpanded", false);
        AssertEqual(false, GetProperty(smokeExpander, "IsExpanded"), "smoke expander collapsed state");
        AssertAtLeast(expanderCollapsedCountBefore + 1, GetProperty(window, "ExpanderCollapsedCount"), "smoke expander collapsed count");
        AssertEqual("range collapsed", GetProperty(rangeStatus, "Text"), "range status after collapse");
        int expanderExpandedCountBefore = Convert.ToInt32(GetProperty(window, "ExpanderExpandedCount"));
        SetProperty(smokeExpander, "IsExpanded", true);
        AssertEqual(true, GetProperty(smokeExpander, "IsExpanded"), "smoke expander expanded state");
        AssertAtLeast(expanderExpandedCountBefore + 1, GetProperty(window, "ExpanderExpandedCount"), "smoke expander expanded count");
        AssertEqual("range expanded", GetProperty(rangeStatus, "Text"), "range status after expand");

        object smokeSlider = Invoke(window, "FindName", "SmokeSlider");
        AssertType(smokeSlider, "System.Windows.Controls.Slider", "smoke slider");
        AssertEqual(0.0, GetProperty(smokeSlider, "Minimum"), "smoke slider minimum");
        AssertEqual(10.0, GetProperty(smokeSlider, "Maximum"), "smoke slider maximum");
        object smokeProgressBar = Invoke(window, "FindName", "SmokeProgressBar");
        AssertType(smokeProgressBar, "System.Windows.Controls.ProgressBar", "smoke progress bar");
        AssertEqual(0.0, GetProperty(smokeProgressBar, "Minimum"), "smoke progress bar minimum");
        AssertEqual(10.0, GetProperty(smokeProgressBar, "Maximum"), "smoke progress bar maximum");
        SetProperty(smokeSlider, "Value", 4.0);
        flushDispatcherOperations?.Invoke(window);
        AssertEqual(4.0, GetProperty(smokeSlider, "Value"), "smoke slider normalized value");
        AssertEqual(4.0, GetProperty(smokeProgressBar, "Value"), "smoke progress bound value");
        int rangeValueChangedCountBefore = Convert.ToInt32(GetProperty(window, "RangeValueChangedCount"));
        SetProperty(smokeSlider, "Value", 6.5);
        flushDispatcherOperations?.Invoke(window);
        AssertEqual(6.5, GetProperty(smokeSlider, "Value"), "smoke slider changed value");
        AssertEqual(6.5, GetProperty(smokeProgressBar, "Value"), "smoke progress changed bound value");
        AssertAtLeast(rangeValueChangedCountBefore + 1, GetProperty(window, "RangeValueChangedCount"), "range value changed count");
        AssertEqual("range value: 6.5", GetProperty(rangeStatus, "Text"), "range status after slider value");

        if (validateFrameContent)
        {
            object viewModel = GetProperty(window, "DataContext");
            object items = GetProperty(viewModel, "Items");
            object dynamicItem = Create(selectedItem.GetType(), "Binding", "dynamic", "Framework");
            InvokeVoid(items, "Add", dynamicItem);
            flushDispatcherOperations?.Invoke(window);
            AssertEqual(4, GetCount(GetProperty(itemsList, "Items")), "items list count after collection change");
            AssertEqual(4, GetCount(GetProperty(panelItemsControl, "Items")), "panel items count after collection change");
            AssertEqual(4, GetCount(GetProperty(smokeListView, "Items")), "list view count after collection change");
            AssertEqual(4, GetCount(GetProperty(selectorItemsControl, "Items")), "selector items count after collection change");
            AssertEqual(4, GetCount(GetProperty(smokeDataGrid, "Items")), "data grid items count after collection change");
            AssertEqual("items: 4", GetProperty(itemsCountText, "Text"), "items count binding text after collection change");
            AssertSame(frameworkItemTemplate, Invoke(itemTemplateSelector, "SelectTemplate", dynamicItem, selectorItemsControl), "dynamic framework item selected template");
        }
        object hierarchyTree = Invoke(window, "FindName", "HierarchyTree");
        AssertType(hierarchyTree, "System.Windows.Controls.TreeView", "hierarchy tree");
        AssertAtLeast(3, GetCount(GetProperty(hierarchyTree, "Items")), "hierarchy tree root item count");
        object hierarchyTemplate = GetProperty(hierarchyTree, "ItemTemplate");
        AssertType(hierarchyTemplate, "System.Windows.HierarchicalDataTemplate", "hierarchy tree item template");
        AssertType(GetProperty(hierarchyTemplate, "ItemsSource"), "System.Windows.Data.Binding", "hierarchy item source binding");
        object hierarchyTemplateRoot = Invoke(hierarchyTemplate, "LoadContent");
        AssertType(hierarchyTemplateRoot, "System.Windows.Controls.TextBlock", "hierarchy template root");
        object firstItemChildren = GetProperty(firstItem, "Children");
        AssertAtLeast(1, GetCount(firstItemChildren), "hierarchy first item child count");
        object firstChild = EnumerateObjects(firstItemChildren).First();
        AssertEqual("Startup", GetProperty(firstChild, "Name"), "hierarchy first child name");

        object compiledSmokePanel = Invoke(window, "FindName", "CompiledSmokePanel");
        AssertType(compiledSmokePanel, "ProGPU.Wpf.SdkSwitchSmoke.SmokePanel", "compiled user control");
        AssertAssignableTo(compiledSmokePanel, "System.Windows.Controls.UserControl", "compiled user control base type");
        AssertEqual("Compiled user control", GetProperty(compiledSmokePanel, "Caption"), "compiled user control dependency property");
        AssertEqual("ProGPU", GetProperty(compiledSmokePanel, "PanelContent"), "compiled user control bound dependency property");
        object panelCaption = Invoke(compiledSmokePanel, "FindName", "PanelCaption");
        AssertType(panelCaption, "System.Windows.Controls.TextBlock", "compiled user control caption element");
        AssertEqual("Compiled user control", GetProperty(panelCaption, "Text"), "compiled user control element-name binding");
        object panelRelativeCaption = Invoke(compiledSmokePanel, "FindName", "PanelRelativeCaption");
        AssertType(panelRelativeCaption, "System.Windows.Controls.TextBlock", "compiled user control relative-source element");
        AssertEqual("Compiled user control", GetProperty(panelRelativeCaption, "Text"), "compiled user control relative-source binding");
        object panelContentPresenter = Invoke(compiledSmokePanel, "FindName", "PanelContentPresenter");
        AssertType(panelContentPresenter, "System.Windows.Controls.ContentPresenter", "compiled user control content presenter");
        AssertEqual("ProGPU", GetProperty(panelContentPresenter, "Content"), "compiled user control content binding");

        object compiledLibraryPanel = Invoke(window, "FindName", "CompiledLibraryPanel");
        AssertType(compiledLibraryPanel, "ProGPU.Wpf.SdkSwitchLibrary.LibraryPanel", "compiled SDK library user control");
        AssertAssignableTo(compiledLibraryPanel, "System.Windows.Controls.UserControl", "compiled SDK library user control base type");
        AssertEqual("SDK library panel", GetProperty(compiledLibraryPanel, "Title"), "compiled SDK library user control dependency property");
        AssertEqual("library tag value", GetProperty(compiledLibraryPanel, "LibraryTag"), "compiled SDK library user control tag property");
        object libraryTitle = Invoke(compiledLibraryPanel, "FindName", "LibraryTitle");
        AssertType(libraryTitle, "System.Windows.Controls.TextBlock", "compiled SDK library title element");
        AssertEqual("SDK library panel", GetProperty(libraryTitle, "Text"), "compiled SDK library element-name title binding");
        object libraryMessage = Invoke(compiledLibraryPanel, "FindName", "LibraryMessage");
        AssertType(libraryMessage, "System.Windows.Controls.TextBlock", "compiled SDK library message element");
        AssertEqual("compiled library BAML", GetProperty(libraryMessage, "Text"), "compiled SDK library BAML text");
        object libraryRoot = Invoke(compiledLibraryPanel, "FindName", "LibraryRoot");
        AssertType(libraryRoot, "System.Windows.Controls.Border", "compiled SDK library root element");
        AssertEqual("library tag value", GetProperty(libraryRoot, "Tag"), "compiled SDK library element-name tag binding");
        object libraryBackground = GetProperty(libraryRoot, "Background");
        AssertType(libraryBackground, "System.Windows.Media.SolidColorBrush", "compiled SDK library resource brush");
        AssertEqual("#FF3E7B64", GetProperty(libraryBackground, "Color").ToString() ?? string.Empty, "compiled SDK library resource brush color");

        object loadedLibraryPanel = LoadApplicationComponent(window, "/ProGPU.Wpf.SdkSwitchLibrary;component/LibraryPanel.xaml");
        AssertType(loadedLibraryPanel, "ProGPU.Wpf.SdkSwitchLibrary.LibraryPanel", "Application.LoadComponent SDK library panel");
        AssertAssignableTo(loadedLibraryPanel, "System.Windows.Controls.UserControl", "Application.LoadComponent SDK library panel base type");
        SetProperty(loadedLibraryPanel, "Title", "SDK loaded library panel");
        SetProperty(loadedLibraryPanel, "LibraryTag", "loaded library tag value");
        InvokeVoid(loadedLibraryPanel, "UpdateLayout");
        object loadedLibraryTitle = Invoke(loadedLibraryPanel, "FindName", "LibraryTitle");
        AssertType(loadedLibraryTitle, "System.Windows.Controls.TextBlock", "Application.LoadComponent SDK library panel title element");
        AssertEqual("SDK loaded library panel", GetProperty(loadedLibraryTitle, "Text"), "Application.LoadComponent SDK library panel title binding");
        object loadedLibraryRoot = Invoke(loadedLibraryPanel, "FindName", "LibraryRoot");
        AssertType(loadedLibraryRoot, "System.Windows.Controls.Border", "Application.LoadComponent SDK library panel root");
        AssertEqual("loaded library tag value", GetProperty(loadedLibraryRoot, "Tag"), "Application.LoadComponent SDK library panel tag binding");

        object loadedLibraryPage = LoadApplicationComponent(window, "/ProGPU.Wpf.SdkSwitchLibrary;component/LibraryPage.xaml");
        AssertType(loadedLibraryPage, "ProGPU.Wpf.SdkSwitchLibrary.LibraryPage", "Application.LoadComponent SDK library page");
        AssertAssignableTo(loadedLibraryPage, "System.Windows.Controls.Page", "Application.LoadComponent SDK library page base type");
        AssertEqual("SDK Library Page", GetProperty(loadedLibraryPage, "Title"), "Application.LoadComponent SDK library page title");
        object loadedLibraryPageTitle = Invoke(loadedLibraryPage, "FindName", "LibraryPageTitle");
        AssertType(loadedLibraryPageTitle, "System.Windows.Controls.TextBlock", "Application.LoadComponent SDK library page title element");
        AssertEqual("SDK library page content", GetProperty(loadedLibraryPageTitle, "Text"), "Application.LoadComponent SDK library page title text");

        object libraryMergedResourceText = Invoke(window, "FindName", "LibraryMergedResourceText");
        AssertType(libraryMergedResourceText, "System.Windows.Controls.TextBlock", "referenced library merged resource text");
        AssertEqual("referenced library resource", GetProperty(libraryMergedResourceText, "Text"), "referenced library merged text resource");
        object libraryMergedResourceForeground = GetProperty(libraryMergedResourceText, "Foreground");
        AssertType(libraryMergedResourceForeground, "System.Windows.Media.SolidColorBrush", "referenced library merged text foreground");
        AssertEqual("#FF4F6F9D", GetProperty(libraryMergedResourceForeground, "Color").ToString() ?? string.Empty, "referenced library merged foreground color");
        object libraryMergedResourcePadding = GetProperty(libraryMergedResourceText, "Padding");
        AssertEqual(2.0, GetProperty(libraryMergedResourcePadding, "Left"), "referenced library merged padding left");
        AssertEqual(3.0, GetProperty(libraryMergedResourcePadding, "Top"), "referenced library merged padding top");
        AssertEqual(4.0, GetProperty(libraryMergedResourcePadding, "Right"), "referenced library merged padding right");
        AssertEqual(5.0, GetProperty(libraryMergedResourcePadding, "Bottom"), "referenced library merged padding bottom");

        object compiledLibraryThemedControl = Invoke(window, "FindName", "CompiledLibraryThemedControl");
        AssertType(compiledLibraryThemedControl, "ProGPU.Wpf.SdkSwitchLibrary.LibraryThemedControl", "compiled SDK library themed control");
        AssertAssignableTo(compiledLibraryThemedControl, "System.Windows.Controls.Control", "compiled SDK library themed control base type");
        AssertEqual("SDK library themed control", GetProperty(compiledLibraryThemedControl, "Text"), "compiled SDK library themed control dependency property");
        Invoke(compiledLibraryThemedControl, "ApplyTemplate");
        object libraryThemedTemplate = GetProperty(compiledLibraryThemedControl, "Template");
        AssertType(libraryThemedTemplate, "System.Windows.Controls.ControlTemplate", "compiled SDK library themed control default template");
        object libraryThemedText = Invoke(libraryThemedTemplate, "FindName", "LibraryThemeText", compiledLibraryThemedControl);
        AssertType(libraryThemedText, "System.Windows.Controls.TextBlock", "compiled SDK library themed control template text");
        AssertEqual("SDK library themed control", GetProperty(libraryThemedText, "Text"), "compiled SDK library themed control template binding");
        object libraryThemedForeground = GetProperty(libraryThemedText, "Foreground");
        AssertType(libraryThemedForeground, "System.Windows.Media.SolidColorBrush", "compiled SDK library themed control foreground");
        AssertEqual("#FF223344", GetProperty(libraryThemedForeground, "Color").ToString() ?? string.Empty, "compiled SDK library themed control foreground color");
        object libraryThemedRoot = Invoke(libraryThemedTemplate, "FindName", "LibraryThemeRoot", compiledLibraryThemedControl);
        AssertType(libraryThemedRoot, "System.Windows.Controls.Border", "compiled SDK library themed control template root");
        object libraryThemedBackground = GetProperty(libraryThemedRoot, "Background");
        AssertType(libraryThemedBackground, "System.Windows.Media.SolidColorBrush", "compiled SDK library themed control background");
        AssertEqual("#FFD9E6F2", GetProperty(libraryThemedBackground, "Color").ToString() ?? string.Empty, "compiled SDK library themed control background color");
        object libraryThemedBorderBrush = GetProperty(libraryThemedRoot, "BorderBrush");
        AssertType(libraryThemedBorderBrush, "System.Windows.Media.SolidColorBrush", "compiled SDK library themed control component resource brush");
        AssertEqual("#FFB25C3D", GetProperty(libraryThemedBorderBrush, "Color").ToString() ?? string.Empty, "compiled SDK library themed control component resource color");
        object libraryThemedBorderThickness = GetProperty(libraryThemedRoot, "BorderThickness");
        AssertEqual(2.0, GetProperty(libraryThemedBorderThickness, "Left"), "compiled SDK library themed control border thickness left");
        AssertEqual(2.0, GetProperty(libraryThemedBorderThickness, "Top"), "compiled SDK library themed control border thickness top");
        AssertEqual(2.0, GetProperty(libraryThemedBorderThickness, "Right"), "compiled SDK library themed control border thickness right");
        AssertEqual(2.0, GetProperty(libraryThemedBorderThickness, "Bottom"), "compiled SDK library themed control border thickness bottom");

        object libraryFrame = Invoke(window, "FindName", "LibraryFrame");
        AssertType(libraryFrame, "System.Windows.Controls.Frame", "compiled SDK library page frame");
        string libraryFrameSource = GetProperty(libraryFrame, "Source").ToString() ?? string.Empty;
        AssertEqual(true, libraryFrameSource.Contains("ProGPU.Wpf.SdkSwitchLibrary", StringComparison.Ordinal), "compiled SDK library frame source assembly");
        AssertEqual(true, libraryFrameSource.EndsWith("component/LibraryPage.xaml", StringComparison.Ordinal), "compiled SDK library frame source component path");
        if (validateFrameContent)
        {
            flushDispatcherOperations?.Invoke(window);
            object libraryFramePage = GetProperty(libraryFrame, "Content");
            AssertType(libraryFramePage, "ProGPU.Wpf.SdkSwitchLibrary.LibraryPage", "compiled SDK library frame page");
            AssertAssignableTo(libraryFramePage, "System.Windows.Controls.Page", "compiled SDK library frame page base type");
            AssertEqual("SDK Library Page", GetProperty(libraryFramePage, "Title"), "compiled SDK library frame page title");
            object libraryFramePageTitle = Invoke(libraryFramePage, "FindName", "LibraryPageTitle");
            AssertType(libraryFramePageTitle, "System.Windows.Controls.TextBlock", "compiled SDK library frame page title element");
            AssertEqual("SDK library page content", GetProperty(libraryFramePageTitle, "Text"), "compiled SDK library frame page title text");
            object libraryFramePageResourceText = Invoke(libraryFramePage, "FindName", "LibraryPageResourceText");
            AssertType(libraryFramePageResourceText, "System.Windows.Controls.TextBlock", "compiled SDK library frame page resource text");
            AssertEqual("referenced library resource", GetProperty(libraryFramePageResourceText, "Text"), "compiled SDK library frame page resource text");
            object libraryFramePageResourceForeground = GetProperty(libraryFramePageResourceText, "Foreground");
            AssertType(libraryFramePageResourceForeground, "System.Windows.Media.SolidColorBrush", "compiled SDK library frame page resource foreground");
            AssertEqual("#FF4F6F9D", GetProperty(libraryFramePageResourceForeground, "Color").ToString() ?? string.Empty, "compiled SDK library frame page resource foreground color");
        }

        object themedSmokeControl = Invoke(window, "FindName", "ThemedSmokeControl");
        AssertType(themedSmokeControl, "ProGPU.Wpf.SdkSwitchSmoke.SmokeThemedControl", "themed custom control");
        AssertAssignableTo(themedSmokeControl, "System.Windows.Controls.Control", "themed custom control base type");
        AssertEqual("Generic theme default style", GetProperty(themedSmokeControl, "Text"), "themed custom control dependency property");
        Invoke(themedSmokeControl, "ApplyTemplate");
        object themedControlTemplate = GetProperty(themedSmokeControl, "Template");
        AssertType(themedControlTemplate, "System.Windows.Controls.ControlTemplate", "themed custom control default template");
        object themedTemplateText = Invoke(themedControlTemplate, "FindName", "ThemeText", themedSmokeControl);
        AssertType(themedTemplateText, "System.Windows.Controls.TextBlock", "themed custom control template text");
        AssertEqual("Generic theme default style", GetProperty(themedTemplateText, "Text"), "themed custom control template binding");
        object themedTemplateForeground = GetProperty(themedTemplateText, "Foreground");
        AssertType(themedTemplateForeground, "System.Windows.Media.SolidColorBrush", "themed custom control foreground");
        AssertEqual("#FF356D9E", GetProperty(themedTemplateForeground, "Color").ToString() ?? string.Empty, "themed custom control foreground color");
        object themedTemplateRoot = Invoke(themedControlTemplate, "FindName", "ThemeRoot", themedSmokeControl);
        AssertType(themedTemplateRoot, "System.Windows.Controls.Border", "themed custom control template root");
        object themedTemplateBackground = GetProperty(themedTemplateRoot, "Background");
        AssertType(themedTemplateBackground, "System.Windows.Media.SolidColorBrush", "themed custom control background");
        AssertEqual("#FF6B8F3A", GetProperty(themedTemplateBackground, "Color").ToString() ?? string.Empty, "themed custom control background color");
        object themedTemplateBorderBrush = GetProperty(themedTemplateRoot, "BorderBrush");
        AssertType(themedTemplateBorderBrush, "System.Windows.Media.SolidColorBrush", "themed custom control component resource brush");
        AssertEqual("#FF7A4EB2", GetProperty(themedTemplateBorderBrush, "Color").ToString() ?? string.Empty, "themed custom control component resource color");
        object themedTemplateBorderThickness = GetProperty(themedTemplateRoot, "BorderThickness");
        AssertEqual(1.0, GetProperty(themedTemplateBorderThickness, "Left"), "themed custom control border thickness left");
        AssertEqual(1.0, GetProperty(themedTemplateBorderThickness, "Top"), "themed custom control border thickness top");
        AssertEqual(1.0, GetProperty(themedTemplateBorderThickness, "Right"), "themed custom control border thickness right");
        AssertEqual(1.0, GetProperty(themedTemplateBorderThickness, "Bottom"), "themed custom control border thickness bottom");

        object smokeFrame = Invoke(window, "FindName", "SmokeFrame");
        AssertType(smokeFrame, "System.Windows.Controls.Frame", "compiled page frame");
        string smokeFrameSource = GetProperty(smokeFrame, "Source").ToString() ?? string.Empty;
        AssertEqual(true, smokeFrameSource.Contains("ProGPU.Wpf.SdkSwitchSmoke", StringComparison.Ordinal), "compiled page frame source assembly");
        AssertEqual(true, smokeFrameSource.EndsWith("component/SmokePage.xaml", StringComparison.Ordinal), "compiled page frame source component path");
        if (validateFrameContent)
        {
            object smokePage = GetProperty(smokeFrame, "Content");
            AssertType(smokePage, "ProGPU.Wpf.SdkSwitchSmoke.SmokePage", "compiled frame page");
            AssertAssignableTo(smokePage, "System.Windows.Controls.Page", "compiled frame page base type");
            AssertEqual("Compiled Smoke Page", GetProperty(smokePage, "Title"), "compiled page title");
            object pageTitle = Invoke(smokePage, "FindName", "PageTitle");
            AssertType(pageTitle, "System.Windows.Controls.TextBlock", "compiled page title element");
            AssertEqual("Compiled page content", GetProperty(pageTitle, "Text"), "compiled page title text");
            object pageTitleForeground = GetProperty(pageTitle, "Foreground");
            AssertType(pageTitleForeground, "System.Windows.Media.SolidColorBrush", "compiled page dynamic resource foreground");
            AssertEqual("#FF356D9E", GetProperty(pageTitleForeground, "Color").ToString() ?? string.Empty, "compiled page dynamic resource foreground color");
            object pageSubtitle = Invoke(smokePage, "FindName", "PageSubtitle");
            AssertType(pageSubtitle, "System.Windows.Controls.TextBlock", "compiled page subtitle element");
            AssertEqual("Frame loaded SDK-built BAML", GetProperty(pageSubtitle, "Text"), "compiled page subtitle text");
            AssertAtLeast(1, GetProperty(window, "SmokeFrameNavigatingCount"), "compiled frame navigating count");
            AssertAtLeast(1, GetProperty(window, "SmokeFrameNavigatedCount"), "compiled frame navigated count");
            AssertAtLeast(1, GetProperty(window, "SmokeFrameLoadCompletedCount"), "compiled frame load completed count");
            AssertEqual(true, (GetProperty(window, "LastSmokeFrameNavigatingUri").ToString() ?? string.Empty).EndsWith("SmokePage.xaml", StringComparison.Ordinal), "compiled frame navigating URI");
            AssertEqual("New", GetProperty(window, "LastSmokeFrameNavigationMode"), "compiled frame navigation mode");
            AssertEqual(true, (GetProperty(window, "LastSmokeFrameNavigatedUri").ToString() ?? string.Empty).EndsWith("SmokePage.xaml", StringComparison.Ordinal), "compiled frame navigated URI");
            AssertEqual("ProGPU.Wpf.SdkSwitchSmoke.SmokePage", GetProperty(window, "LastSmokeFrameNavigatedContentType"), "compiled frame navigated content type");
            AssertEqual(true, (GetProperty(window, "LastSmokeFrameLoadCompletedUri").ToString() ?? string.Empty).EndsWith("SmokePage.xaml", StringComparison.Ordinal), "compiled frame load completed URI");
            int navigatingCountBeforeSecondPage = Convert.ToInt32(GetProperty(window, "SmokeFrameNavigatingCount"));
            int navigatedCountBeforeSecondPage = Convert.ToInt32(GetProperty(window, "SmokeFrameNavigatedCount"));
            int loadCompletedCountBeforeSecondPage = Convert.ToInt32(GetProperty(window, "SmokeFrameLoadCompletedCount"));
            AssertEqual(true, Invoke(smokeFrame, "Navigate", new Uri("/ProGPU.Wpf.SdkSwitchSmoke;component/SmokeSecondPage.xaml", UriKind.Relative)), "compiled frame second page navigate result");
            flushDispatcherOperations?.Invoke(window);
            object smokeSecondPage = GetProperty(smokeFrame, "Content");
            AssertType(smokeSecondPage, "ProGPU.Wpf.SdkSwitchSmoke.SmokeSecondPage", "compiled second frame page");
            AssertAssignableTo(smokeSecondPage, "System.Windows.Controls.Page", "compiled second frame page base type");
            AssertEqual("Compiled Second Page", GetProperty(smokeSecondPage, "Title"), "compiled second page title");
            object secondPageTitle = Invoke(smokeSecondPage, "FindName", "SecondPageTitle");
            AssertType(secondPageTitle, "System.Windows.Controls.TextBlock", "compiled second page title element");
            AssertEqual("Compiled second page content", GetProperty(secondPageTitle, "Text"), "compiled second page title text");
            object secondPageSubtitle = Invoke(smokeSecondPage, "FindName", "SecondPageSubtitle");
            AssertType(secondPageSubtitle, "System.Windows.Controls.TextBlock", "compiled second page subtitle element");
            AssertEqual("Frame navigated to SDK-built BAML", GetProperty(secondPageSubtitle, "Text"), "compiled second page subtitle text");
            AssertAtLeast(navigatingCountBeforeSecondPage + 1, GetProperty(window, "SmokeFrameNavigatingCount"), "compiled frame second page navigating count");
            AssertAtLeast(navigatedCountBeforeSecondPage + 1, GetProperty(window, "SmokeFrameNavigatedCount"), "compiled frame second page navigated count");
            AssertAtLeast(loadCompletedCountBeforeSecondPage + 1, GetProperty(window, "SmokeFrameLoadCompletedCount"), "compiled frame second page load completed count");
            AssertEqual("New", GetProperty(window, "LastSmokeFrameNavigationMode"), "compiled frame second page navigation mode");
            AssertEqual(true, (GetProperty(window, "LastSmokeFrameNavigatedUri").ToString() ?? string.Empty).EndsWith("SmokeSecondPage.xaml", StringComparison.Ordinal), "compiled frame second page navigated URI");
            AssertEqual("ProGPU.Wpf.SdkSwitchSmoke.SmokeSecondPage", GetProperty(window, "LastSmokeFrameNavigatedContentType"), "compiled frame second page content type");
            AssertEqual(true, GetProperty(smokeFrame, "CanGoBack"), "compiled frame journal can go back");
            int navigatingCountBeforeBack = Convert.ToInt32(GetProperty(window, "SmokeFrameNavigatingCount"));
            int navigatedCountBeforeBack = Convert.ToInt32(GetProperty(window, "SmokeFrameNavigatedCount"));
            InvokeVoid(smokeFrame, "GoBack");
            flushDispatcherOperations?.Invoke(window);
            object returnedSmokePage = GetProperty(smokeFrame, "Content");
            AssertType(returnedSmokePage, "ProGPU.Wpf.SdkSwitchSmoke.SmokePage", "compiled frame returned page");
            AssertAtLeast(navigatingCountBeforeBack + 1, GetProperty(window, "SmokeFrameNavigatingCount"), "compiled frame back navigating count");
            AssertAtLeast(navigatedCountBeforeBack + 1, GetProperty(window, "SmokeFrameNavigatedCount"), "compiled frame back navigated count");
            AssertEqual("Back", GetProperty(window, "LastSmokeFrameNavigationMode"), "compiled frame back navigation mode");
            AssertEqual("ProGPU.Wpf.SdkSwitchSmoke.SmokePage", GetProperty(window, "LastSmokeFrameNavigatedContentType"), "compiled frame back content type");
            object smokePageFunction = Create(window.GetType().Assembly, "ProGPU.Wpf.SdkSwitchSmoke.SmokePageFunction");
            int navigatingCountBeforePageFunction = Convert.ToInt32(GetProperty(window, "SmokeFrameNavigatingCount"));
            int navigatedCountBeforePageFunction = Convert.ToInt32(GetProperty(window, "SmokeFrameNavigatedCount"));
            AssertEqual(true, Invoke(smokeFrame, "Navigate", smokePageFunction), "compiled frame PageFunction navigate result");
            flushDispatcherOperations?.Invoke(window);
            object currentPageFunction = GetProperty(smokeFrame, "Content");
            AssertType(currentPageFunction, "ProGPU.Wpf.SdkSwitchSmoke.SmokePageFunction", "compiled frame PageFunction content");
            AssertAssignableTo(currentPageFunction, "System.Windows.Controls.Page", "compiled frame PageFunction page base type");
            AssertEqual("Compiled Smoke PageFunction", GetProperty(currentPageFunction, "Title"), "compiled frame PageFunction title");
            object pageFunctionPanel = GetProperty(currentPageFunction, "Content");
            AssertType(pageFunctionPanel, "System.Windows.Controls.StackPanel", "compiled frame PageFunction panel");
            object pageFunctionChildren = GetProperty(pageFunctionPanel, "Children");
            AssertEqual(2, GetCount(pageFunctionChildren), "compiled frame PageFunction child count");
            object firstPageFunctionChild = EnumerateObjects(pageFunctionChildren).First();
            AssertType(firstPageFunctionChild, "System.Windows.Controls.TextBlock", "compiled frame PageFunction first child");
            AssertEqual("Compiled page function content", GetProperty(firstPageFunctionChild, "Text"), "compiled frame PageFunction title text");
            AssertAtLeast(navigatingCountBeforePageFunction + 1, GetProperty(window, "SmokeFrameNavigatingCount"), "compiled frame PageFunction navigating count");
            AssertAtLeast(navigatedCountBeforePageFunction + 1, GetProperty(window, "SmokeFrameNavigatedCount"), "compiled frame PageFunction navigated count");
            AssertEqual("New", GetProperty(window, "LastSmokeFrameNavigationMode"), "compiled frame PageFunction navigation mode");
            AssertEqual("ProGPU.Wpf.SdkSwitchSmoke.SmokePageFunction", GetProperty(window, "LastSmokeFrameNavigatedContentType"), "compiled frame PageFunction content type");
            int pageFunctionReturnCountBefore = Convert.ToInt32(GetProperty(window, "SmokePageFunctionReturnCount"));
            Assembly pageFunctionPresentationFramework = GetAssemblyFromContext(currentPageFunction.GetType().Assembly, "PresentationFramework");
            Type returnEventArgsType = GetRequiredType(pageFunctionPresentationFramework, "System.Windows.Navigation.ReturnEventArgs`1").MakeGenericType(typeof(string));
            object returnEventArgs = Create(returnEventArgsType, "SDK PageFunction runtime result");
            MethodInfo onFinish = currentPageFunction.GetType()
                .BaseType?
                .BaseType?
                .GetMethod("_OnFinish", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(currentPageFunction.GetType().FullName, "_OnFinish");
            InvokeMethod(onFinish, currentPageFunction, returnEventArgs);
            flushDispatcherOperations?.Invoke(window);
            AssertAtLeast(pageFunctionReturnCountBefore + 1, GetProperty(window, "SmokePageFunctionReturnCount"), "compiled frame PageFunction return count");
            AssertEqual("SDK PageFunction runtime result", GetProperty(window, "LastSmokePageFunctionResult"), "compiled frame PageFunction return result");
        }

        object documentBox = Invoke(window, "FindName", "DocumentBox");
        AssertType(documentBox, "System.Windows.Controls.RichTextBox", "rich text box");
        object document = GetProperty(documentBox, "Document");
        AssertType(document, "System.Windows.Documents.FlowDocument", "rich text document");
        object documentBlocks = GetProperty(document, "Blocks");
        AssertAtLeast(5, GetCount(documentBlocks), "rich text block count");
        object firstParagraph = FindFirstByType(documentBlocks, "System.Windows.Documents.Paragraph", "rich text first paragraph");
        object paragraphInlines = GetProperty(firstParagraph, "Inlines");
        AssertAtLeast(6, GetCount(paragraphInlines), "rich text first paragraph inline count");
        object documentHyperlink = FindFirstByType(paragraphInlines, "System.Windows.Documents.Hyperlink", "rich text hyperlink");
        AssertEqual("https://example.com/progpu-wpf", GetProperty(documentHyperlink, "NavigateUri").ToString() ?? string.Empty, "rich text hyperlink URI");
        if (validateFrameContent)
        {
            int requestNavigateCountBefore = Convert.ToInt32(GetProperty(window, "DocumentLinkRequestNavigateCount"));
            InvokeVoid(documentHyperlink, "DoClick");
            AssertAtLeast(requestNavigateCountBefore + 1, GetProperty(window, "DocumentLinkRequestNavigateCount"), "SDK rich text hyperlink RequestNavigate count");
            AssertEqual("https://example.com/progpu-wpf", GetProperty(window, "LastDocumentLinkRequestNavigateUri"), "SDK rich text hyperlink RequestNavigate URI");
            AssertEqual("RequestNavigate", GetProperty(window, "LastDocumentLinkRequestNavigateRoutedEventName"), "SDK rich text hyperlink RequestNavigate routed event");
        }
        object inlineUiContainer = FindFirstByType(paragraphInlines, "System.Windows.Documents.InlineUIContainer", "rich text inline UI container");
        object inlineButton = GetProperty(inlineUiContainer, "Child");
        AssertType(inlineButton, "System.Windows.Controls.Button", "rich text inline UI button");
        AssertEqual("Inline document button", GetProperty(inlineButton, "Content"), "rich text inline UI button content");
        object documentSection = FindFirstByType(documentBlocks, "System.Windows.Documents.Section", "rich text section");
        AssertAtLeast(1, GetCount(GetProperty(documentSection, "Blocks")), "rich text section block count");
        object documentList = FindFirstByType(documentBlocks, "System.Windows.Documents.List", "rich text list");
        AssertEqual("Square", GetProperty(documentList, "MarkerStyle").ToString() ?? string.Empty, "rich text list marker style");
        AssertEqual(2, GetCount(GetProperty(documentList, "ListItems")), "rich text list item count");
        object documentTable = FindFirstByType(documentBlocks, "System.Windows.Documents.Table", "rich text table");
        AssertEqual(2, GetCount(GetProperty(documentTable, "Columns")), "rich text table column count");
        object tableRowGroups = GetProperty(documentTable, "RowGroups");
        AssertEqual(1, GetCount(tableRowGroups), "rich text table row group count");
        object firstRowGroup = EnumerateObjects(tableRowGroups).First();
        object tableRows = GetProperty(firstRowGroup, "Rows");
        AssertEqual(1, GetCount(tableRows), "rich text table row count");
        object firstTableRow = EnumerateObjects(tableRows).First();
        AssertEqual(2, GetCount(GetProperty(firstTableRow, "Cells")), "rich text table cell count");
        object blockUiContainer = FindFirstByType(documentBlocks, "System.Windows.Documents.BlockUIContainer", "rich text block UI container");
        object blockUiText = GetProperty(blockUiContainer, "Child");
        AssertType(blockUiText, "System.Windows.Controls.TextBlock", "rich text block UI text");
        AssertEqual("Block UI document content", GetProperty(blockUiText, "Text"), "rich text block UI text content");

        if (flushDispatcherOperations is not null)
        {
            ValidateApplicationDynamicResourceInvalidation(
                window,
                flushDispatcherOperations,
                message,
                actionButton);
        }
    }

    private static void ValidateApplicationDynamicResourceInvalidation(
        object window,
        Action<object> flushDispatcherOperations,
        object message,
        object actionButton)
    {
        Assembly presentationFramework = GetAssemblyFromContext(window.GetType().Assembly, "PresentationFramework");
        Type applicationType = GetRequiredType(presentationFramework, "System.Windows.Application");
        object application = GetStaticProperty(applicationType, "Current");
        object resources = GetProperty(application, "Resources");
        Assembly presentationCore = GetAssemblyFromContext(window.GetType().Assembly, "PresentationCore");

        InvokeVoid(resources, "set_Item", "SmokeAccentBrush", CreateSolidColorBrush(presentationCore, "#9E4A70"));
        InvokeVoid(resources, "set_Item", "MergedAccentBrush", CreateSolidColorBrush(presentationCore, "#234E7A"));
        flushDispatcherOperations(window);
        InvokeVoid(window, "UpdateLayout");

        object updatedMessageForeground = GetProperty(message, "Foreground");
        AssertType(updatedMessageForeground, "System.Windows.Media.SolidColorBrush", "message dynamic resource updated foreground");
        AssertEqual("#FF234E7A", GetProperty(updatedMessageForeground, "Color").ToString() ?? string.Empty, "message dynamic resource updated color");

        object updatedActionButtonBackground = GetProperty(actionButton, "Background");
        AssertType(updatedActionButtonBackground, "System.Windows.Media.SolidColorBrush", "action button dynamic resource updated background");
        AssertEqual("#FF9E4A70", GetProperty(updatedActionButtonBackground, "Color").ToString() ?? string.Empty, "action button dynamic resource updated color");
    }

    private static void ValidateSdkFocusAndAccessKeyAfterRun(Assembly presentationCore, object window)
    {
        object inputBox = Invoke(window, "FindName", "InputBox");
        object focusPanel = Invoke(window, "FindName", "AccessKeyFocusPanel");
        object accessLabel = Invoke(window, "FindName", "InputAccessLabel");
        AssertSame(inputBox, GetProperty(accessLabel, "Target"), "SDK access-key label target");

        Type keyboardType = GetRequiredType(presentationCore, "System.Windows.Input.Keyboard");
        Type focusManagerType = GetRequiredType(presentationCore, "System.Windows.Input.FocusManager");
        AssertSame(inputBox, InvokeStatic(keyboardType, "Focus", inputBox), "SDK TextBox Keyboard.Focus return value");
        AssertSame(inputBox, GetStaticProperty(keyboardType, "FocusedElement"), "SDK TextBox keyboard focused element");
        AssertSame(inputBox, InvokeStatic(focusManagerType, "GetFocusedElement", focusPanel), "SDK FocusManager live logical focus update");

        Type presentationSourceType = GetRequiredType(presentationCore, "System.Windows.PresentationSource");
        object presentationSource = InvokeStatic(presentationSourceType, "FromVisual", window);
        Type accessKeyManagerType = GetRequiredType(presentationCore, "System.Windows.Input.AccessKeyManager");
        AssertEqual(true, InvokeStatic(accessKeyManagerType, "IsKeyRegistered", presentationSource, "I"), "SDK access-key manager registered label key");

        InvokeStaticVoid(keyboardType, "ClearFocus");
        object? focusedAfterClear = GetStaticPropertyOrNull(keyboardType, "FocusedElement");
        AssertEqual(false, ReferenceEquals(inputBox, focusedAfterClear), "SDK keyboard focus cleared before access key");
        InvokeStatic(accessKeyManagerType, "ProcessKey", presentationSource, "I", false);
        AssertSame(inputBox, GetStaticProperty(keyboardType, "FocusedElement"), "SDK access-key manager focused label target");
        AssertSame(inputBox, InvokeStatic(focusManagerType, "GetFocusedElement", focusPanel), "SDK access-key manager restored logical focus");
        InvokeStaticVoid(keyboardType, "ClearFocus");
    }

    private static object CreateSolidColorBrush(Assembly presentationCore, string colorText)
    {
        Type colorConverterType = GetRequiredType(presentationCore, "System.Windows.Media.ColorConverter");
        object color = InvokeStatic(colorConverterType, "ConvertFromString", colorText);
        Type brushType = GetRequiredType(presentationCore, "System.Windows.Media.SolidColorBrush");
        return Create(brushType, color);
    }

    private static SdkApplicationRunRecorder RegisterPortableActivation(
        Assembly presentationFramework,
        Assembly presentationCore,
        object application,
        out Type activationServiceType)
    {
        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        MethodInfo register = activationServiceType.GetMethod(
            "Register",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "Register");

        var recorder = new SdkApplicationRunRecorder(
            presentationFramework,
            presentationCore,
            application,
            activationServiceType);
        recorder.RegisterMediaContextRenderService();

        register.Invoke(
            null,
            new object?[]
            {
                new Func<object, object>(recorder.Activate),
                new Action<object>(recorder.Show),
                new Action<object>(recorder.Hide),
                new Action<object, object>(recorder.SetWindowState),
                new Action<object, string>(recorder.SetTitle),
                new Action<object, double, double>(recorder.SetClientSize),
                new Action<object, double, double>(recorder.SetPosition),
                new Action<object, bool>(recorder.SetTopmost),
                new Action<object, object, object>(recorder.SetWindowBorder),
                new Action<object>(recorder.Close),
                new Action<object>(recorder.Run),
                new Action<object>(recorder.Dispose),
                new Func<object, bool>(_ => false),
                new Func<object, IntPtr>(recorder.GetHandle),
                null, // setWindowRegion
                null, // requestActivation
                null  // setIcon
            });

        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");
        recorder.AssertRegistered();
        return recorder;
    }

    private static void RegisterPortableMessageBox(Assembly presentationFramework)
    {
        Type serviceType = GetRequiredType(presentationFramework, PortableMessageBoxServiceTypeName);
        MethodInfo register = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Func<object, object>) },
                modifiers: null)
            ?? throw new MissingMethodException(serviceType.FullName, "Register");

        register.Invoke(
            null,
            new object[] { (Func<object, object>)ShowPortableMessageBox });
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable MessageBox service enabled");
    }

    private static object ShowPortableMessageBox(object request)
    {
        return GetProperty(request, "FallbackResult");
    }

    private static void ValidatePortableMessageBox(Assembly presentationFramework, object window)
    {
        Type serviceType = GetRequiredType(presentationFramework, PortableMessageBoxServiceTypeName);
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable MessageBox service enabled");

        Type messageBoxType = GetRequiredType(presentationFramework, "System.Windows.MessageBox");
        Type windowType = GetRequiredType(presentationFramework, "System.Windows.Window");
        Type buttonType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxButton");
        Type imageType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxImage");
        Type resultType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxResult");
        Type optionsType = GetRequiredType(presentationFramework, "System.Windows.MessageBoxOptions");

        object yesNoCancel = Enum.Parse(buttonType, "YesNoCancel");
        object okCancel = Enum.Parse(buttonType, "OKCancel");
        object warning = Enum.Parse(imageType, "Warning");
        object information = Enum.Parse(imageType, "Information");
        object noneResult = Enum.Parse(resultType, "None");
        object no = Enum.Parse(resultType, "No");
        object ok = Enum.Parse(resultType, "OK");
        object noneOptions = Enum.Parse(optionsType, "None");

        MethodInfo noOwnerShow = messageBoxType.GetMethod(
                "Show",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(string), typeof(string), buttonType, imageType, resultType, optionsType },
                modifiers: null)
            ?? throw new MissingMethodException(messageBoxType.FullName, "Show");
        object noOwnerResult = noOwnerShow.Invoke(
                null,
                new[] { "portable SDK message", "portable SDK caption", yesNoCancel, warning, no, noneOptions })
            ?? throw new InvalidOperationException("MessageBox.Show returned null.");
        AssertEqual(no, noOwnerResult, "portable MessageBox SDK no-owner default result");

        MethodInfo ownerShow = messageBoxType.GetMethod(
                "Show",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { windowType, typeof(string), typeof(string), buttonType, imageType, resultType, optionsType },
                modifiers: null)
            ?? throw new MissingMethodException(messageBoxType.FullName, "Show");
        object ownerResult = ownerShow.Invoke(
                null,
                new[] { window, "portable SDK owner message", "portable SDK owner caption", okCancel, information, noneResult, noneOptions })
            ?? throw new InvalidOperationException("MessageBox.Show returned null.");
        AssertEqual(ok, ownerResult, "portable MessageBox SDK owner fallback result");
    }

    private static void ValidatePortableClipboard(Assembly presentationCore)
    {
        Type serviceType = GetRequiredType(presentationCore, PortableClipboardServiceTypeName);
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable Clipboard service enabled");

        Type clipboardType = GetRequiredType(presentationCore, "System.Windows.Clipboard");
        Type dataFormatsType = GetRequiredType(presentationCore, "System.Windows.DataFormats");
        Type dataObjectInterfaceType = GetRequiredType(presentationCore, "System.Windows.IDataObject");
        object unicodeText = GetStaticField(dataFormatsType, "UnicodeText");

        InvokeStaticVoid(clipboardType, "Clear");
        AssertEqual(false, InvokeStatic(clipboardType, "ContainsText"), "portable Clipboard SDK initial text state");

        InvokeStaticVoid(clipboardType, "SetText", "portable SDK clipboard text");
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsText"), "portable Clipboard SDK text state after SetText");
        AssertEqual("portable SDK clipboard text", InvokeStatic(clipboardType, "GetText"), "portable Clipboard SDK GetText");

        object dataObject = InvokeStatic(clipboardType, "GetDataObject");
        AssertEqual(true, dataObjectInterfaceType.IsInstanceOfType(dataObject), "portable Clipboard SDK data object contract");
        AssertEqual(
            "portable SDK clipboard text",
            Invoke(dataObject, "GetData", unicodeText, false),
            "portable Clipboard SDK data object unicode text");
        AssertEqual(true, InvokeStatic(clipboardType, "IsCurrent", dataObject), "portable Clipboard SDK current data object");

        InvokeStaticVoid(clipboardType, "Flush");
        AssertEqual("portable SDK clipboard text", InvokeStatic(clipboardType, "GetText"), "portable Clipboard SDK flushed text");

        ValidatePortableRichClipboardFormats(presentationCore);
        ValidatePortableJsonDataObject(presentationCore);

        InvokeStaticVoid(clipboardType, "Clear");
        AssertEqual(false, InvokeStatic(clipboardType, "ContainsText"), "portable Clipboard SDK cleared text state");
        AssertEqual(string.Empty, InvokeStatic(clipboardType, "GetText"), "portable Clipboard SDK cleared text");
    }

    private static void ValidatePortableRichClipboardFormats(Assembly presentationCore)
    {
        Type clipboardType = GetRequiredType(presentationCore, "System.Windows.Clipboard");
        Type dataFormatsType = GetRequiredType(presentationCore, "System.Windows.DataFormats");

        string fileOne = Path.Combine(Path.GetTempPath(), "progpu-sdk-file-drop-one.txt");
        string fileTwo = Path.Combine(Path.GetTempPath(), "progpu-sdk-file-drop-two.txt");
        var fileDropList = new System.Collections.Specialized.StringCollection
        {
            fileOne,
            fileTwo
        };
        InvokeStaticVoid(clipboardType, "SetFileDropList", fileDropList);
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsFileDropList"), "portable Clipboard SDK file drop state");
        object roundTripFileDropList = InvokeStatic(clipboardType, "GetFileDropList");
        AssertEqual(2, GetCount(roundTripFileDropList), "portable Clipboard SDK file drop count");
        AssertEqual(fileOne, GetCollectionItem(roundTripFileDropList, 0), "portable Clipboard SDK file drop first path");
        AssertEqual(fileTwo, GetCollectionItem(roundTripFileDropList, 1), "portable Clipboard SDK file drop second path");

        const string customFormat = "PortableSdkCustomClipboardFormat";
        InvokeStaticVoid(clipboardType, "SetData", customFormat, "portable SDK custom data");
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsData", customFormat), "portable Clipboard SDK custom data state");
        AssertEqual("portable SDK custom data", InvokeStatic(clipboardType, "GetData", customFormat), "portable Clipboard SDK custom data value");

        byte[] audioBytes = [0x52, 0x49, 0x46, 0x46];
        InvokeStaticVoid(clipboardType, "SetAudio", audioBytes);
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsAudio"), "portable Clipboard SDK audio state");
        object audioStream = InvokeStatic(clipboardType, "GetAudioStream");
        AssertAssignableTo(audioStream, "System.IO.Stream", "portable Clipboard SDK audio stream");
        AssertEqual(4L, GetProperty(audioStream, "Length"), "portable Clipboard SDK audio stream length");

        object bitmapSource = CreatePortableBitmapSource(presentationCore);
        InvokeStaticVoid(clipboardType, "SetImage", bitmapSource);
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsImage"), "portable Clipboard SDK image state");
        object roundTripImage = InvokeStatic(clipboardType, "GetImage");
        AssertAssignableTo(roundTripImage, "System.Windows.Media.Imaging.BitmapSource", "portable Clipboard SDK image value");
        AssertEqual(2, GetProperty(roundTripImage, "PixelWidth"), "portable Clipboard SDK image width");
        AssertEqual(2, GetProperty(roundTripImage, "PixelHeight"), "portable Clipboard SDK image height");

        string bitmapFormat = GetStaticField(dataFormatsType, "Bitmap").ToString() ?? string.Empty;
        AssertEqual(true, InvokeStatic(clipboardType, "ContainsData", bitmapFormat), "portable Clipboard SDK image data format state");
    }

    private static object CreatePortableBitmapSource(Assembly presentationCore)
    {
        Type bitmapSourceType = GetRequiredType(presentationCore, "System.Windows.Media.Imaging.BitmapSource");
        Type pixelFormatsType = GetRequiredType(presentationCore, "System.Windows.Media.PixelFormats");
        object bgra32 = GetStaticProperty(pixelFormatsType, "Bgra32");
        byte[] pixels =
        [
            0x20, 0x40, 0x80, 0xFF,
            0x40, 0x80, 0x20, 0xFF,
            0x80, 0x20, 0x40, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        ];

        return InvokeStatic(
            bitmapSourceType,
            "Create",
            2,
            2,
            96.0,
            96.0,
            bgra32,
            null,
            pixels,
            8);
    }

    private static void ValidatePortableJsonDataObject(Assembly presentationCore)
    {
        const string dataObjectFormat = "PortableSdkJsonDataObjectFormat";
        const string clipboardFormat = "PortableSdkJsonClipboardFormat";

        Type dataObjectType = GetRequiredType(presentationCore, "System.Windows.DataObject");
        Type clipboardType = GetRequiredType(presentationCore, "System.Windows.Clipboard");
        var payload = new PortableClipboardJsonPayload
        {
            Message = "portable SDK JSON payload",
            Count = 42
        };

        object dataObject = Create(dataObjectType);
        MethodInfo dataObjectSetDataAsJson = GetGenericMethod(
            dataObjectType,
            "SetDataAsJson",
            isStatic: false,
            parameterCount: 2,
            parameters => parameters[0].ParameterType == typeof(string));
        InvokeMethod(
            dataObjectSetDataAsJson.MakeGenericMethod(typeof(PortableClipboardJsonPayload)),
            dataObject,
            dataObjectFormat,
            payload);
        AssertEqual(
            true,
            Invoke(dataObject, "GetDataPresent", dataObjectFormat, false),
            "portable Clipboard SDK JSON DataObject format present");

        MethodInfo dataObjectTryGetData = GetGenericMethod(
            dataObjectType,
            "TryGetData",
            isStatic: false,
            parameterCount: 3,
            parameters => parameters[0].ParameterType == typeof(string)
                && parameters[1].ParameterType == typeof(bool));
        object?[] dataObjectTryGetArgs = [dataObjectFormat, false, null];
        AssertEqual(
            true,
            InvokeMethod(
                dataObjectTryGetData.MakeGenericMethod(typeof(PortableClipboardJsonPayload)),
                dataObject,
                dataObjectTryGetArgs) is true,
            "portable Clipboard SDK JSON DataObject typed retrieval state");
        var dataObjectRoundTrip = (PortableClipboardJsonPayload?)dataObjectTryGetArgs[2]
            ?? throw new InvalidOperationException("Expected portable DataObject JSON payload.");
        AssertEqual("portable SDK JSON payload", dataObjectRoundTrip.Message ?? string.Empty, "portable Clipboard SDK JSON DataObject message");
        AssertEqual(42, dataObjectRoundTrip.Count, "portable Clipboard SDK JSON DataObject count");

        MethodInfo clipboardSetDataAsJson = GetGenericMethod(
            clipboardType,
            "SetDataAsJson",
            isStatic: true,
            parameterCount: 2,
            parameters => parameters[0].ParameterType == typeof(string));
        InvokeMethod(
            clipboardSetDataAsJson.MakeGenericMethod(typeof(PortableClipboardJsonPayload)),
            instance: null,
            clipboardFormat,
            payload);

        MethodInfo clipboardTryGetData = GetGenericMethod(
            clipboardType,
            "TryGetData",
            isStatic: true,
            parameterCount: 2,
            parameters => parameters[0].ParameterType == typeof(string));
        object?[] clipboardTryGetArgs = [clipboardFormat, null];
        AssertEqual(
            true,
            InvokeMethod(
                clipboardTryGetData.MakeGenericMethod(typeof(PortableClipboardJsonPayload)),
                instance: null,
                clipboardTryGetArgs) is true,
            "portable Clipboard SDK JSON clipboard typed retrieval state");
        var clipboardRoundTrip = (PortableClipboardJsonPayload?)clipboardTryGetArgs[1]
            ?? throw new InvalidOperationException("Expected portable Clipboard JSON payload.");
        AssertEqual("portable SDK JSON payload", clipboardRoundTrip.Message ?? string.Empty, "portable Clipboard SDK JSON clipboard message");
        AssertEqual(42, clipboardRoundTrip.Count, "portable Clipboard SDK JSON clipboard count");
    }

    private static void ValidatePortableFileDialogs(Assembly presentationFramework, object? owner = null)
    {
        Type serviceType = GetRequiredType(presentationFramework, PortableFileDialogServiceTypeName);
        AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable file dialog service enabled");

        MethodInfo registerMethod = serviceType.GetMethod(
                "Register",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Func<object, string?>) },
                modifiers: null)
            ?? throw new MissingMethodException(serviceType.FullName, "Register");

        string tempDirectory = Path.Combine(Path.GetTempPath(), "progpu-wpf-sdk-file-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string openPath = Path.Combine(tempDirectory, "open.txt");
        string savePathWithoutExtension = Path.Combine(tempDirectory, "saved");
        string savePath = savePathWithoutExtension + ".txt";
        File.WriteAllText(openPath, "portable SDK file dialog");

        int requestCount = 0;
        var seenKinds = new List<string>();
        Func<object, string?> handler = request =>
        {
            string kind = GetProperty(request, "Kind").ToString() ?? string.Empty;
            seenKinds.Add(kind);
            requestCount++;

            return kind switch
            {
                "SaveFile" => savePathWithoutExtension,
                "PickFolder" => tempDirectory,
                _ => openPath
            };
        };

        IDisposable? registration = null;
        try
        {
            registration = (IDisposable?)registerMethod.Invoke(null, new object[] { handler });

            string ownerPrefix = owner is null ? "no-owner" : "owner";
            object openDialog = Create(presentationFramework, "Microsoft.Win32.OpenFileDialog");
            SetProperty(openDialog, "Filter", "Text files (*.txt)|*.txt|All files (*.*)|*.*");
            AssertEqual(true, ShowDialog(openDialog, owner), $"portable SDK {ownerPrefix} OpenFileDialog result");
            AssertEqual(openPath, GetProperty(openDialog, "FileName"), $"portable SDK {ownerPrefix} OpenFileDialog FileName");
            AssertEqual("open.txt", GetProperty(openDialog, "SafeFileName"), $"portable SDK {ownerPrefix} OpenFileDialog SafeFileName");

            object saveDialog = Create(presentationFramework, "Microsoft.Win32.SaveFileDialog");
            SetProperty(saveDialog, "DefaultExt", "txt");
            SetProperty(saveDialog, "OverwritePrompt", false);
            AssertEqual(true, ShowDialog(saveDialog, owner), $"portable SDK {ownerPrefix} SaveFileDialog result");
            AssertEqual(savePath, GetProperty(saveDialog, "FileName"), $"portable SDK {ownerPrefix} SaveFileDialog FileName");
            AssertEqual("saved.txt", GetProperty(saveDialog, "SafeFileName"), $"portable SDK {ownerPrefix} SaveFileDialog SafeFileName");

            object folderDialog = Create(presentationFramework, "Microsoft.Win32.OpenFolderDialog");
            AssertEqual(true, ShowDialog(folderDialog, owner), $"portable SDK {ownerPrefix} OpenFolderDialog result");
            AssertEqual(tempDirectory, GetProperty(folderDialog, "FolderName"), $"portable SDK {ownerPrefix} OpenFolderDialog FolderName");
            AssertEqual(Path.GetFileName(tempDirectory), GetProperty(folderDialog, "SafeFolderName"), $"portable SDK {ownerPrefix} OpenFolderDialog SafeFolderName");

            AssertEqual(3, requestCount, $"portable SDK {ownerPrefix} file dialog request count");
            AssertEqual("OpenFile", seenKinds[0], $"portable SDK {ownerPrefix} file dialog open request kind");
            AssertEqual("SaveFile", seenKinds[1], $"portable SDK {ownerPrefix} file dialog save request kind");
            AssertEqual("PickFolder", seenKinds[2], $"portable SDK {ownerPrefix} file dialog folder request kind");
        }
        finally
        {
            registration?.Dispose();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static object ShowDialog(object dialog, object? owner)
    {
        return owner is null
            ? Invoke(dialog, "ShowDialog")
            : Invoke(dialog, "ShowDialog", owner);
    }

    private static void ClearPortableActivation(Type? activationServiceType)
    {
        activationServiceType?.GetMethod(
            "Clear",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
    }

    private static void ClearPortableService(Assembly assembly, string typeName)
    {
        assembly.GetType(typeName, throwOnError: false)?.GetMethod(
            "Clear",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
    }

    private static object Create(Assembly assembly, string typeName)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Could not create '{typeName}'.");
    }

    private static object Create(Type type, params object?[] args)
    {
        return Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args,
            culture: null)
            ?? throw new InvalidOperationException($"Could not create '{type.FullName}'.");
    }

    private static MethodInfo GetGenericMethod(
        Type type,
        string methodName,
        bool isStatic,
        int parameterCount,
        Func<ParameterInfo[], bool> parameterPredicate)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        return type.GetMethods(flags)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => method.IsGenericMethodDefinition)
            .Where(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == parameterCount && parameterPredicate(parameters);
            })
            .Single();
    }

    private static object? InvokeMethod(MethodInfo method, object? instance, params object?[] args)
    {
        try
        {
            return method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)!
            ?? throw new TypeLoadException(typeName);
    }

    private static Assembly GetAssemblyFromContext(Assembly contextAssembly, string assemblyName)
    {
        AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(contextAssembly)
            ?? throw new InvalidOperationException($"Assembly '{contextAssembly.FullName}' does not have a load context.");
        return loadContext.Assemblies.FirstOrDefault(
                assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Assembly '{assemblyName}' is not loaded in context '{loadContext.Name}'.");
    }

    private static object Invoke(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        try
        {
            return method.Invoke(instance, args)
                ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void InvokeVoid(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        try
        {
            method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void TryInvoke(object instance, string methodName)
    {
        MethodInfo? method = GetCompatibleMethod(instance.GetType(), methodName, Array.Empty<object?>());
        try
        {
            method?.Invoke(instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void RaiseRoutedEvent(object source, string routedEventFieldName)
    {
        FieldInfo field = source.GetType().GetField(
            routedEventFieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(source.GetType().FullName, routedEventFieldName);
        object routedEvent = field.GetValue(null)
            ?? throw new InvalidOperationException($"Routed event field '{routedEventFieldName}' returned null.");
        Type eventArgsType = GetRequiredType(routedEvent.GetType().Assembly, "System.Windows.RoutedEventArgs");
        object eventArgs = Activator.CreateInstance(eventArgsType, routedEvent, source)
            ?? throw new InvalidOperationException("Could not create RoutedEventArgs.");
        InvokeVoid(source, "RaiseEvent", eventArgs);
    }

    private static object InvokeStatic(Type type, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleStaticMethod(type, methodName, args)
            ?? throw new MissingMethodException(type.FullName, methodName);

        try
        {
            return method.Invoke(null, args)
                ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static object? InvokeStaticOrNull(Type type, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleStaticMethod(type, methodName, args)
            ?? throw new MissingMethodException(type.FullName, methodName);

        try
        {
            return method.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeStaticVoid(Type type, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleStaticMethod(type, methodName, args)
            ?? throw new MissingMethodException(type.FullName, methodName);

        try
        {
            method.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static MethodInfo? GetCompatibleMethod(Type type, string methodName, object?[] args)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => ParametersMatch(method.GetParameters(), args))
            .OrderBy(method => GetDeclaringTypeDistance(type, method.DeclaringType))
            .FirstOrDefault();
    }

    private static MethodInfo? GetCompatibleStaticMethod(Type type, string methodName, object?[] args)
    {
        return type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => ParametersMatch(method.GetParameters(), args))
            .FirstOrDefault();
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length)
        {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            object? arg = args[i];
            if (arg is null)
            {
                if (parameters[i].ParameterType.IsValueType &&
                    Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameters[i].ParameterType.IsAssignableFrom(arg.GetType()))
            {
                return false;
            }
        }

        return true;
    }

    private static int GetDeclaringTypeDistance(Type actualType, Type? declaringType)
    {
        int distance = 0;
        for (Type? type = actualType; type is not null; type = type.BaseType)
        {
            if (type == declaringType)
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
    }

    private static object GetProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        return property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null.");
    }

    private static object? GetPropertyOrNull(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        return property.GetValue(instance);
    }

    private static string GetBindingPath(object binding)
    {
        object propertyPath = GetProperty(binding, "Path");
        return GetProperty(propertyPath, "Path").ToString() ?? string.Empty;
    }

    private static void AssertBindingPath(
        Assembly presentationFramework,
        object target,
        string dependencyPropertyFieldName,
        string expectedPath,
        string description)
    {
        Type bindingOperationsType = GetRequiredType(presentationFramework, "System.Windows.Data.BindingOperations");
        object dependencyProperty = GetStaticField(target.GetType(), dependencyPropertyFieldName);
        object bindingExpression = InvokeStatic(bindingOperationsType, "GetBindingExpression", target, dependencyProperty);
        AssertType(bindingExpression, "System.Windows.Data.BindingExpression", $"{description} expression");
        AssertEqual(expectedPath, GetBindingPath(GetProperty(bindingExpression, "ParentBinding")), description);
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);
        return property.GetValue(null)
            ?? throw new InvalidOperationException($"Property '{propertyName}' returned null.");
    }

    private static object? GetStaticPropertyOrNull(Type type, string propertyName)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);
        return property.GetValue(null);
    }

    private static object GetStaticField(Type type, string fieldName)
    {
        FieldInfo field = type.GetField(
            fieldName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        return field.GetValue(null)
            ?? throw new InvalidOperationException($"Field '{fieldName}' returned null.");
    }

    private static void SetProperty(object instance, string propertyName, object? value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        FieldInfo field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
        field.SetValue(instance, value);
    }

    private static int GetCount(object collection)
    {
        if (collection is ICollection nonGenericCollection)
        {
            return nonGenericCollection.Count;
        }

        PropertyInfo? countProperty = collection.GetType().GetProperty(
            "Count",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (countProperty?.GetValue(collection) is object count)
        {
            return Convert.ToInt32(count);
        }

        throw new MissingMemberException(collection.GetType().FullName, "Count");
    }

    private static object GetDictionaryValue(object dictionary, object key)
    {
        if (dictionary is IDictionary nonGenericDictionary && nonGenericDictionary.Contains(key))
        {
            return nonGenericDictionary[key]
                ?? throw new InvalidOperationException($"Dictionary key '{key}' returned null.");
        }

        throw new InvalidOperationException($"Dictionary does not contain key '{key}'.");
    }

    private static object GetCollectionItem(object collection, int index)
    {
        return EnumerateObjects(collection).ElementAt(index);
    }

    private static IEnumerable<object> EnumerateObjects(object collection)
    {
        if (collection is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Object '{collection.GetType().FullName}' is not enumerable.");
        }

        foreach (object? item in enumerable)
        {
            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static object FindFirstByType(object collection, string typeName, string description)
    {
        foreach (object item in EnumerateObjects(collection))
        {
            if (string.Equals(item.GetType().FullName, typeName, StringComparison.Ordinal))
            {
                return item;
            }
        }

        throw new InvalidOperationException($"Expected {description} of type '{typeName}'.");
    }

    private static void AssertType(object value, string expectedTypeName, string description)
    {
        if (!string.Equals(value.GetType().FullName, expectedTypeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{description}: expected type '{expectedTypeName}', actual '{value.GetType().FullName}'.");
        }
    }

    private static void AssertPropertyType(Type type, string propertyName, Type expectedType, string description)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);

        if (property.PropertyType != expectedType)
        {
            throw new InvalidOperationException(
                $"{description}: expected property type '{expectedType.FullName}', actual '{property.PropertyType.FullName}'.");
        }
    }

    private static MethodInfo FindMethodByParameterNames(Type type, string methodName, string[] parameterNames)
    {
        MethodInfo? method = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == parameterNames.Length &&
                    parameters
                        .Select(parameter => parameter.Name ?? string.Empty)
                        .SequenceEqual(parameterNames, StringComparer.Ordinal);
            });

        return method ?? throw new MissingMethodException(
            type.FullName,
            $"{methodName}({string.Join(", ", parameterNames)})");
    }

    private static MethodInfo FindMethodByNameAndParameterCount(Type type, string methodName, int parameterCount)
    {
        MethodInfo? method = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .FirstOrDefault(method => method.GetParameters().Length == parameterCount);

        return method ?? throw new MissingMethodException(type.FullName, $"{methodName}/{parameterCount}");
    }

    private static void AssertParameterTypes(MethodInfo method, Type[] expectedParameterTypes, string description)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length < expectedParameterTypes.Length)
        {
            throw new InvalidOperationException(
                $"{description}: expected at least {expectedParameterTypes.Length} parameters, actual {parameters.Length}.");
        }

        for (int i = 0; i < expectedParameterTypes.Length; i++)
        {
            if (parameters[i].ParameterType != expectedParameterTypes[i])
            {
                throw new InvalidOperationException(
                    $"{description}: expected parameter '{parameters[i].Name}' type '{expectedParameterTypes[i].FullName}', actual '{parameters[i].ParameterType.FullName}'.");
            }
        }
    }

    private static void AssertPropertyGetterReferencesField(
        Type type,
        string propertyName,
        string fieldName,
        string description)
    {
        PropertyInfo property = type.GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(type.FullName, propertyName);
        MethodInfo getter = property.GetMethod
            ?? throw new MissingMethodException(type.FullName, propertyName + ".get");
        FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        byte[] il = getter.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"{description}: expected getter IL.");
        byte[] fieldToken = BitConverter.GetBytes(field.MetadataToken);

        for (int i = 0; i <= il.Length - fieldToken.Length; i++)
        {
            if (il.AsSpan(i, fieldToken.Length).SequenceEqual(fieldToken))
            {
                return;
            }
        }

        throw new InvalidOperationException($"{description}: expected getter to reference '{fieldName}'.");
    }

    private static void AssertMethodCallsMethod(
        MethodInfo method,
        string calledDeclaringTypeFullName,
        string calledMethodName,
        string description)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"{description}: expected method IL.");
        Type[] typeArguments = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        Type[] methodArguments = method.GetGenericArguments();

        for (int i = 0; i <= il.Length - 5; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? calledMethod;
            try
            {
                calledMethod = method.Module.ResolveMethod(token, typeArguments, methodArguments);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (calledMethod != null &&
                string.Equals(calledMethod.Name, calledMethodName, StringComparison.Ordinal) &&
                string.Equals(calledMethod.DeclaringType?.FullName, calledDeclaringTypeFullName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{description}: expected '{method.DeclaringType?.FullName}.{method.Name}' to call '{calledDeclaringTypeFullName}.{calledMethodName}'.");
    }

    private static void AssertMethodCallsSpecificMethod(
        MethodInfo method,
        MethodInfo calledMethod,
        string description)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"{description}: expected method IL.");
        Type[] typeArguments = method.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
        Type[] methodArguments = method.GetGenericArguments();

        for (int i = 0; i <= il.Length - 5; i++)
        {
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? resolvedMethod;
            try
            {
                resolvedMethod = method.Module.ResolveMethod(token, typeArguments, methodArguments);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (resolvedMethod is MethodInfo resolvedInfo &&
                resolvedInfo.Module == calledMethod.Module &&
                resolvedInfo.MetadataToken == calledMethod.MetadataToken)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{description}: expected '{method.DeclaringType?.FullName}.{method.Name}' to call '{calledMethod.DeclaringType?.FullName}.{calledMethod.Name}' with the validated overload.");
    }

    private static void AssertAssignableTo(object value, string expectedBaseTypeName, string description)
    {
        for (Type? type = value.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, expectedBaseTypeName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"{description}: expected assignable to '{expectedBaseTypeName}', actual '{value.GetType().FullName}'.");
    }

    private static void AssertEqual(object expected, object actual, string description)
    {
        if (!object.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertNull(object? actual, string description)
    {
        if (actual is not null)
        {
            throw new InvalidOperationException($"{description}: expected null, actual '{actual}'.");
        }
    }

    private static void AssertContains(string expected, string actual, string description)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{description}: expected text containing '{expected}'.");
        }
    }

    private static void AssertClose(double expected, double actual, double tolerance, string description)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"{description}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertDate(object value, int year, int month, int day, string description)
    {
        if (value is not DateTime date)
        {
            throw new InvalidOperationException($"{description}: expected DateTime, actual '{value.GetType().FullName}'.");
        }

        DateTime expected = new(year, month, day);
        if (date.Date != expected)
        {
            throw new InvalidOperationException($"{description}: expected '{expected:yyyy-MM-dd}', actual '{date:yyyy-MM-dd}'.");
        }
    }

    private static void AssertSame(object expected, object actual, string description)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"{description}: expected same instance.");
        }
    }

    private static void AssertNotSame(object unexpected, object actual, string description)
    {
        if (ReferenceEquals(unexpected, actual))
        {
            throw new InvalidOperationException($"{description}: expected different instances.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, object actualValue, string description)
    {
        int actual = Convert.ToInt32(actualValue);
        if (actual < expectedMinimum)
        {
            throw new InvalidOperationException($"{description}: expected at least {expectedMinimum}, actual {actual}.");
        }
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description} was not found.", path);
        }
    }

    private static void RequireAnyFile(string root, IReadOnlyList<string> fileNames, string description)
    {
        foreach (string fileName in fileNames)
        {
            if (Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).Any())
            {
                return;
            }
        }

        throw new FileNotFoundException(
            $"{description} was not found under '{root}'. Expected one of: {string.Join(", ", fileNames)}.");
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"{description} was not found: {path}");
        }
    }

    private static string FindRepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "global.json")) &&
                Directory.Exists(Path.Combine(directory, "src", "Microsoft.DotNet.Wpf")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the WPF repository root.");
    }

    private sealed record SmokeInputs(
        string RepoRoot,
        string AppOutputRoot,
        string SmokeAssemblyPath,
        string WpfRoot,
        string ProGpuRoot);

    private sealed class SdkApplicationRunRecorder : IDisposable
    {
        private readonly Assembly _presentationCore;
        private readonly Assembly _presentationFramework;
        private readonly object _application;
        private readonly Type _activationServiceType;
        private IDisposable? _mediaContextRenderRegistration;
        private RecordingActivation? _activation;

        public SdkApplicationRunRecorder(
            Assembly presentationFramework,
            Assembly presentationCore,
            object application,
            Type activationServiceType)
        {
            _presentationFramework = presentationFramework;
            _presentationCore = presentationCore;
            _application = application;
            _activationServiceType = activationServiceType;
        }

        public int ActivateCount { get; private set; }

        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public int RunCount { get; private set; }

        public int RenderRequestCount { get; private set; }

        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void RegisterMediaContextRenderService()
        {
            Type serviceType = GetRequiredType(_presentationCore, PortableMediaContextRenderServiceTypeName);
            object registration = InvokeStatic(serviceType, "Register", new Action<TimeSpan>(RequestRender));
            _mediaContextRenderRegistration = registration as IDisposable
                ?? throw new InvalidOperationException("PortableMediaContextRenderService.Register did not return IDisposable.");
            AssertEqual(true, GetStaticProperty(serviceType, "IsEnabled"), "portable MediaContext render service enabled");
        }

        public object Activate(object window)
        {
            try
            {
                return ActivateCore(window);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("SDK startup window activation callback failed.", ex);
            }
        }

        private object ActivateCore(object window)
        {
            if (ActivateCount != 0)
            {
                throw new InvalidOperationException("Expected exactly one SDK startup window activation.");
            }

            AssertType(window, MainWindowTypeName, "activated SDK startup window");
            ValidateWindow(window, validateFrameContent: false, flushDispatcherOperations: null);

            object presentationSource = CreatePortablePresentationSource(window);
            ActivateCount++;
            _activation = new RecordingActivation(window, presentationSource)
            {
                Title = GetProperty(window, "Title").ToString() ?? string.Empty,
                Width = Convert.ToDouble(GetProperty(window, "Width")),
                Height = Convert.ToDouble(GetProperty(window, "Height")),
                Left = Convert.ToDouble(GetProperty(window, "Left")),
                Top = Convert.ToDouble(GetProperty(window, "Top")),
                Topmost = Convert.ToBoolean(GetProperty(window, "Topmost")),
                ResizeMode = GetProperty(window, "ResizeMode"),
                WindowStyle = GetProperty(window, "WindowStyle")
            };
            return _activation;
        }

        public void Show(object activation)
        {
            try
            {
                ShowCore(activation);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("SDK startup window show callback failed.", ex);
            }
        }

        private void ShowCore(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            ShowCount++;
            typedActivation.IsVisible = true;
            FlushDispatcherOperations(typedActivation.Window, "Loaded", "Render");
        }

        public void Hide(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            HideCount++;
            typedActivation.IsVisible = false;
        }

        public void SetWindowState(object activation, object windowState)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.WindowState = windowState;
        }

        public void SetTitle(object activation, string title)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.Title = title;
        }

        public void SetClientSize(object activation, double width, double height)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.Width = width;
            typedActivation.Height = height;
        }

        public void SetPosition(object activation, double left, double top)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.Left = left;
            typedActivation.Top = top;
        }

        public void SetTopmost(object activation, bool topmost)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.Topmost = topmost;
        }

        public void SetWindowBorder(object activation, object resizeMode, object windowStyle)
        {
            var typedActivation = AssertSameActivation(activation);
            typedActivation.ResizeMode = resizeMode;
            typedActivation.WindowStyle = windowStyle;
        }

        public void Close(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            CloseCount++;
            typedActivation.IsClosed = true;
        }

        public void Run(object activation)
        {
            try
            {
                RunCore(activation);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("SDK startup window run callback failed.", ex);
            }
        }

        private void RunCore(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            RunCount++;
            AssertEqual(true, typedActivation.IsVisible, "SDK startup window visible before run");
            AssertEqual("ProGPU WPF SDK Smoke", typedActivation.Title, "activated SDK window title");
            AssertEqual(420.0, typedActivation.Width, "activated SDK window width");
            AssertEqual(840.0, typedActivation.Height, "activated SDK window height");
            AssertEqual(false, typedActivation.Topmost, "activated SDK window topmost");
            AssertSame(typedActivation.Window, GetProperty(_application, "MainWindow"), "SDK Application.MainWindow");
            Type resizeModeType = GetRequiredType(_presentationFramework, "System.Windows.ResizeMode");
            Type windowStyleType = GetRequiredType(_presentationFramework, "System.Windows.WindowStyle");
            SetProperty(typedActivation.Window, "ResizeMode", Enum.Parse(resizeModeType, "NoResize"));
            SetProperty(typedActivation.Window, "WindowStyle", Enum.Parse(windowStyleType, "None"));
            AssertEqual("NoResize", typedActivation.ResizeMode?.ToString() ?? string.Empty, "activated SDK window live resize mode");
            AssertEqual("None", typedActivation.WindowStyle?.ToString() ?? string.Empty, "activated SDK window live window style");
            Type applicationType = GetRequiredType(_presentationFramework, "System.Windows.Application");
            AssertSame(_application, GetStaticProperty(applicationType, "Current"), "SDK Application.Current during run");
            AssertEqual("OnLastWindowClose", GetProperty(_application, "ShutdownMode").ToString() ?? string.Empty, "SDK Application.ShutdownMode during run");
            object windows = GetProperty(_application, "Windows");
            AssertEqual(1, GetCount(windows), "SDK Application.Windows during run count");
            AssertSame(typedActivation.Window, GetCollectionItem(windows, 0), "SDK Application.Windows startup window");
            InvokeVoid(typedActivation.Window, "UpdateLayout");
            FlushDispatcherOperations(typedActivation.Window, "Loaded", "Render", "ApplicationIdle");
            ValidateWindow(
                typedActivation.Window,
                validateFrameContent: true,
                flushDispatcherOperations: window => FlushDispatcherOperations(window, "ApplicationIdle"));
            ValidateSdkFocusAndAccessKeyAfterRun(_presentationCore, typedActivation.Window);
            ValidatePortableMessageBox(_presentationFramework, typedActivation.Window);
            ValidatePortableFileDialogs(_presentationFramework, typedActivation.Window);
        }

        public void Dispose(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            DisposeCount++;
            typedActivation.DisposePresentationSource();
        }

        public IntPtr GetHandle(object activation)
        {
            var typedActivation = AssertSameActivation(activation);
            return (IntPtr)GetProperty(typedActivation.PresentationSource, "Handle");
        }

        public void ValidateAfterRun()
        {
            AssertEqual(1, ActivateCount, "SDK startup window activation count");
            AssertEqual(1, ShowCount, "SDK startup window show count");
            AssertEqual(1, RunCount, "SDK startup window run count");
            AssertEqual(true, RenderRequestCount > 0, "SDK portable MediaContext render request count");
            AssertEqual(1, CloseCount, "SDK startup window close count");
            AssertEqual(1, DisposeCount, "SDK startup window dispose count");

            if (_activation is null)
            {
                throw new InvalidOperationException("Application.Run did not create an SDK recording activation.");
            }

            AssertEqual(true, _activation.IsClosed, "SDK recording activation close state");
            AssertEqual(true, _activation.IsDisposed, "SDK recording activation dispose state");
            AssertEqual(0, HideCount, "SDK startup window hide count");
        }

        public void AssertRegistered()
        {
            AssertDelegateTarget("_activate", "SDK portable activation recorder activate target");
            AssertDelegateTarget("_show", "SDK portable activation recorder show target");
            AssertDelegateTarget("_setPosition", "SDK portable activation recorder position target");
            AssertDelegateTarget("_setTopmost", "SDK portable activation recorder topmost target");
            AssertDelegateTarget("_setWindowBorder", "SDK portable activation recorder window border target");
            AssertDelegateTarget("_run", "SDK portable activation recorder run target");
        }

        private void AssertDelegateTarget(string fieldName, string description)
        {
            object activationDelegate = GetStaticField(_activationServiceType, fieldName);
            AssertSame(this, GetProperty(activationDelegate, "Target"), description);
        }

        public void Dispose()
        {
            _mediaContextRenderRegistration?.Dispose();
            _mediaContextRenderRegistration = null;
            _activation?.DisposePresentationSource();
        }

        private void RequestRender(TimeSpan delay)
        {
            RenderRequestCount++;
        }

        private object CreatePortablePresentationSource(object window)
        {
            Type presentationSourceType = GetRequiredType(_presentationCore, PortablePresentationSourceTypeName);
            object presentationSource = Create(presentationSourceType);
            SetProperty(presentationSource, "RootVisual", window);
            return presentationSource;
        }

        private void FlushDispatcherOperations(object window, params string[] priorities)
        {
            MethodInfo method = _activationServiceType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate =>
                    string.Equals(candidate.Name, "FlushDispatcherOperations", StringComparison.Ordinal) &&
                    candidate.GetParameters().Length == 2);
            Type priorityType = method.GetParameters()[1].ParameterType;

            foreach (string priority in priorities)
            {
                object markerPriority = Enum.Parse(priorityType, priority);
                InvokeStaticVoid(_activationServiceType, "FlushDispatcherOperations", window, markerPriority);
            }
        }

        private RecordingActivation AssertSameActivation(object activation)
        {
            if (!ReferenceEquals(_activation, activation) || _activation is null)
            {
                throw new InvalidOperationException("Unexpected SDK portable window activation instance.");
            }

            return _activation;
        }
    }

    private sealed class RecordingActivation
    {
        public RecordingActivation(object window, object presentationSource)
        {
            Window = window;
            PresentationSource = presentationSource;
        }

        public object Window { get; }

        public object PresentationSource { get; }

        public bool IsVisible { get; set; }

        public bool IsClosed { get; set; }

        public bool IsDisposed { get; private set; }

        public object? WindowState { get; set; }

        public string Title { get; set; } = string.Empty;

        public double Width { get; set; }

        public double Height { get; set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public bool Topmost { get; set; }

        public object? ResizeMode { get; set; }

        public object? WindowStyle { get; set; }

        public void DisposePresentationSource()
        {
            if (IsDisposed)
            {
                return;
            }

            if (PresentationSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else
            {
                InvokeVoid(PresentationSource, "Dispose");
            }

            IsDisposed = true;
        }
    }

    private sealed class SdkSmokeLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly string _repoRoot;
        private readonly string _appOutputRoot;
        private readonly string _wpfRoot;
        private readonly string _proGpuRoot;
        private readonly string _smokeAssemblyPath;
        private readonly AssemblyDependencyResolver _resolver;

        public SdkSmokeLoadContext(
            string repoRoot,
            string appOutputRoot,
            string smokeAssemblyPath,
            string wpfRoot,
            string proGpuRoot)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _appOutputRoot = appOutputRoot;
            _smokeAssemblyPath = smokeAssemblyPath;
            _wpfRoot = wpfRoot;
            _proGpuRoot = proGpuRoot;
            _resolver = new AssemblyDependencyResolver(typeof(Program).Assembly.Location);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? assemblyPath = TryResolveAssemblyPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            foreach (string candidate in GetUnmanagedDllCandidates(unmanagedDllName))
            {
                string path = Path.Combine(_appOutputRoot, candidate);
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        private string? TryResolveAssemblyPath(AssemblyName assemblyName)
        {
            string fileName = assemblyName.Name + ".dll";
            string? path = assemblyName.Name switch
            {
                SmokeAssemblyName => _smokeAssemblyPath,
                "WindowsBase" or "System.Xaml" or "PresentationCore" or "PresentationFramework" or "PresentationUI" or "ReachFramework" or "System.Printing" or "UIAutomationTypes" or "UIAutomationProvider" or "System.Windows.Input.Manipulations" or "System.Windows.Primitives" or "PresentationFramework.Aero" or "PresentationFramework.Aero2" or "PresentationFramework.AeroLite" or "PresentationFramework.Classic" or "PresentationFramework.Fluent" or "PresentationFramework.Luna" or "PresentationFramework.Royale" or "System.Windows.Controls.Ribbon" =>
                    TryFindAssembly(_appOutputRoot, fileName) ?? TryFindAssembly(_wpfRoot, fileName),
                "ProGPU.Wpf" or "ProGPU.Wpf.Interop" or "ProGPU.Backend" or "ProGPU.DirectX" or "ProGPU.Scene" or "ProGPU.Vector" or "ProGPU.Text" or "ProGPU.Compute" or "ProGPU.Transpiler" =>
                    TryFindAssembly(_appOutputRoot, fileName) ?? TryFindAssembly(_proGpuRoot, fileName),
                _ => null
            };

            if (path is not null && File.Exists(path))
            {
                return path;
            }

            path = TryFindAssembly(_appOutputRoot, fileName)
                ?? TryFindArtifactAssembly(assemblyName.Name, "net10.0")
                ?? TryFindArtifactAssembly(assemblyName.Name, "net10.0")
                ?? _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null && File.Exists(path) ? path : null;
        }

        private static IEnumerable<string> GetUnmanagedDllCandidates(string unmanagedDllName)
        {
            yield return Path.GetFileName(unmanagedDllName);

            if (unmanagedDllName.Contains("glfw", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string candidate in GetNativeAssetCandidates("glfw"))
                {
                    yield return candidate;
                }
            }

            if (unmanagedDllName.Contains("wgpu", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string candidate in GetNativeAssetCandidates("wgpu"))
                {
                    yield return candidate;
                }
            }

            string nameWithoutExtension = Path.GetFileNameWithoutExtension(unmanagedDllName);
            if (OperatingSystem.IsWindows())
            {
                yield return nameWithoutExtension + ".dll";
            }
            else if (OperatingSystem.IsMacOS())
            {
                yield return "lib" + nameWithoutExtension + ".dylib";
            }
            else
            {
                yield return "lib" + nameWithoutExtension + ".so";
            }
        }

        private static string? TryFindAssembly(string root, string fileName)
        {
            string path = Path.Combine(root, fileName);
            return File.Exists(path) ? path : null;
        }

        private string? TryFindArtifactAssembly(string? assemblyName, string targetFramework)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return null;
            }

            string path = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName,
                "Debug",
                targetFramework,
                assemblyName + ".dll");
            return File.Exists(path) ? path : null;
        }

        public void Dispose()
        {
            Unload();
        }
    }
}
