using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProGPU.Wpf.SdkSwitchSmoke;

public partial class App : Application
{
    private const string LibreWpfPackageVersion = "0.1.0-preview.42";
    private const string ProGpuPackageVersion = "0.1.0-preview.52";

    public int StartupEventCount { get; private set; }

    public int StartupArgsLength { get; private set; } = -1;

    public bool SdkOutputGuardChecked { get; private set; }

    public int ExitEventCount { get; private set; }

    public int LastExitCode { get; private set; } = -1;

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        ValidateSdkRenderSurfaceOutput();
        SdkOutputGuardChecked = true;
        StartupEventCount++;
        StartupArgsLength = e.Args.Length;
        Resources["StartupInjectedBrush"] = new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0xB2));
        Resources["StartupInjectedText"] = "startup resource value";
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
        LastExitCode = e.ApplicationExitCode;
    }

    private static void ValidateSdkRenderSurfaceOutput()
    {
        Assembly proGpuWpf = LoadRequiredAssembly("ProGPU.Wpf");
        Assembly proGpuScene = LoadRequiredAssembly("ProGPU.Scene");
        Assembly proGpuBackend = LoadRequiredAssembly("ProGPU.Backend");
        Assembly proGpuDirectX = LoadRequiredAssembly("ProGPU.DirectX");
        Assembly silkNetMaths = LoadRequiredAssembly("Silk.NET.Maths");
        Assembly silkNetWebGpu = LoadRequiredAssembly("Silk.NET.WebGPU");

        Type hostType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfWindowHost");
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type directXDeviceType = GetRequiredType(proGpuDirectX, "ProGPU.DirectX.ProGpuDirectXDevice");
        Type compositorType = GetRequiredType(proGpuScene, "ProGPU.Scene.Compositor");
        Type renderTargetViewportType = GetRequiredType(proGpuScene, "ProGPU.Scene.RenderTargetViewport");
        Type displayScaleResolverType = GetRequiredType(proGpuBackend, "ProGPU.Backend.DisplayScaleResolver");
        Type vector2DIntType = GetRequiredType(silkNetMaths, "Silk.NET.Maths.Vector2D`1").MakeGenericType(typeof(int));

        RequireMethodByParameterNames(displayScaleResolverType, "ResolveWindowDisplayScale", "window", "monitorDpiScale");
        MethodInfo resolveDisplayScale = RequireMethodByParameterNames(
            displayScaleResolverType,
            "ResolveDisplayScaleWithPlatformFallback",
            "monitorDpiScale",
            "platformDpiScaleProvider");
        MethodInfo resolveGeometry = RequireMethodByParameterNames(
            hostType,
            "ResolveRenderSurfaceGeometry",
            "clientWidth",
            "clientHeight",
            "framebufferSize",
            "monitorDpiScale");

        RequireMethodByParameterNames(
            hostType,
            "Present",
            "logicalWidth",
            "logicalHeight",
            "pixelWidth",
            "pixelHeight",
            "viewportX",
            "viewportY",
            "viewportWidth",
            "viewportHeight",
            "dpiScale");
        RequireMethodByParameterNames(hostType, "SynchronizePortablePresentationSourceGeometry", "geometry");
        RequireMethodByParameterNames(
            compositionTargetType,
            "Render",
            "logicalWidth",
            "logicalHeight",
            "pixelWidth",
            "pixelHeight",
            "dpiScale",
            "targetView");
        MethodInfo compositionViewportRender = RequireMethodByParameterNames(
            compositionTargetType,
            "Render",
            "logicalWidth",
            "logicalHeight",
            "pixelWidth",
            "pixelHeight",
            "renderTargetViewport",
            "dpiScale",
            "targetView");
        AssertEqual(
            renderTargetViewportType,
            compositionViewportRender.GetParameters()[4].ParameterType,
            "SDK smoke ProGPU WPF viewport render parameter type");
        RequireMethodByParameterNames(
            compositorType,
            "RenderScene",
            "root",
            "logicalWidth",
            "logicalHeight",
            "renderTargetWidth",
            "renderTargetHeight",
            "dpiScale",
            "targetView");
        MethodInfo compositorViewportRender = RequireMethodByParameterNames(
            compositorType,
            "RenderScene",
            "root",
            "logicalWidth",
            "logicalHeight",
            "renderTargetWidth",
            "renderTargetHeight",
            "renderTargetViewport",
            "dpiScale",
            "targetView");
        AssertEqual(
            renderTargetViewportType,
            compositorViewportRender.GetParameters()[5].ParameterType,
            "SDK smoke ProGPU compositor viewport render parameter type");

        double dpiScale = Convert.ToDouble(
            resolveDisplayScale.Invoke(null, new object?[] { 1.0, new Func<double?>(() => 2.0) }));
        AssertEqual(2.0, dpiScale, "SDK smoke Retina display-scale fallback");

        object framebufferSize = Activator.CreateInstance(vector2DIntType, 840, 1680)
            ?? throw new InvalidOperationException("Could not create Silk.NET Retina framebuffer size.");
        object geometry = resolveGeometry.Invoke(null, new object?[] { 420, 840, framebufferSize, dpiScale })
            ?? throw new InvalidOperationException("SDK render-surface geometry returned null.");

        AssertEqual(420u, GetProperty(geometry, "LogicalWidth"), "SDK smoke Retina logical width");
        AssertEqual(840u, GetProperty(geometry, "LogicalHeight"), "SDK smoke Retina logical height");
        AssertEqual(840u, GetProperty(geometry, "PixelWidth"), "SDK smoke Retina physical width");
        AssertEqual(1680u, GetProperty(geometry, "PixelHeight"), "SDK smoke Retina physical height");
        AssertEqual(0u, GetProperty(geometry, "ViewportX"), "SDK smoke Retina viewport X");
        AssertEqual(0u, GetProperty(geometry, "ViewportY"), "SDK smoke Retina viewport Y");
        AssertEqual(840u, GetProperty(geometry, "ViewportWidth"), "SDK smoke Retina viewport width");
        AssertEqual(1680u, GetProperty(geometry, "ViewportHeight"), "SDK smoke Retina viewport height");
        AssertEqual(2.0, GetProperty(geometry, "DpiScale"), "SDK smoke Retina DPI scale");
        ValidateHostUsesSilkLogicalClientSize(hostType, vector2DIntType, dpiScale);

        ValidateRetainedOwnerBranchFillsPhysicalTarget(proGpuWpf, proGpuBackend, silkNetWebGpu);
        ValidateRetainedOwnerBranchPreservesLogicalMarkerOrigin(proGpuWpf, proGpuBackend, silkNetWebGpu);
        RequireProperty(hostType, "DirectXDevice", directXDeviceType);

        ValidateRuntimeAssetMatchesLocalPackage(proGpuWpf, "LibreWPF.ProGPU", "ProGPU.Wpf", "net10.0");
        ValidateRuntimeAssetMatchesLocalPackage(proGpuDirectX, "ProGPU.DirectX", "ProGPU.DirectX", "net10.0");
        ValidateRuntimeAssetMatchesLocalPackage(proGpuScene, "ProGPU.Scene", "ProGPU.Scene", "net10.0");
        ValidateRuntimeAssetMatchesLocalPackage(proGpuBackend, "ProGPU.Backend", "ProGPU.Backend", "net10.0");
    }

    private static void ValidateHostUsesSilkLogicalClientSize(
        Type hostType,
        Type vector2DIntType,
        double dpiScale)
    {
        Type optionsType = GetRequiredType(hostType.Assembly, "System.Windows.Media.ProGPU.ProGpuWpfWindowOptions");
        object options = Activator.CreateInstance(optionsType)
            ?? throw new InvalidOperationException("Could not create ProGPU WPF window options.");
        SetProperty(options, "Width", 420);
        SetProperty(options, "Height", 840);

        object host = Activator.CreateInstance(hostType, options)
            ?? throw new InvalidOperationException("Could not create ProGPU WPF window host.");
        try
        {
            SetField(host, "_clientWidth", 840);
            SetField(host, "_clientHeight", 1680);
            SetField(host, "_requestedLogicalClientWidth", 840);
            SetField(host, "_requestedLogicalClientHeight", 1680);

            object nativeLogicalSize = Activator.CreateInstance(vector2DIntType, 420, 840)
                ?? throw new InvalidOperationException("Could not create Silk.NET logical client size.");
            object retinaFramebufferSize = Activator.CreateInstance(vector2DIntType, 840, 1680)
                ?? throw new InvalidOperationException("Could not create Silk.NET Retina framebuffer size.");
            MethodInfo updateNativeResize = RequireMethodByParameterNames(
                hostType,
                "UpdateClientSizeFromNativeResize",
                "size",
                "framebufferSize",
                "monitorDpiScale");
            updateNativeResize.Invoke(host, new[] { nativeLogicalSize, retinaFramebufferSize, dpiScale });

            AssertEqual(420, GetProperty(host, "Width"), "SDK smoke live host uses Silk logical width");
            AssertEqual(840, GetProperty(host, "Height"), "SDK smoke live host uses Silk logical height");
        }
        finally
        {
            (host as IDisposable)?.Dispose();
        }
    }

    private static Assembly LoadRequiredAssembly(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"SDK smoke output is missing required assembly '{assemblyName}'. Rebuild the package-mode smoke output.",
                ex);
        }
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load type '{typeName}' from '{assembly.FullName}'.");
    }

    private static MethodInfo RequireMethodByParameterNames(Type type, string methodName, params string[] parameterNames)
    {
        MethodInfo? method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                candidate.GetParameters()
                    .Select(parameter => parameter.Name ?? string.Empty)
                    .SequenceEqual(parameterNames));

        return method
            ?? throw new MissingMethodException(
                type.FullName,
                $"{methodName}({string.Join(", ", parameterNames)})");
    }

    private static void RequireProperty(Type type, string propertyName, Type propertyType)
    {
        PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new MissingMemberException(type.FullName, propertyName);

        if (property.PropertyType != propertyType)
        {
            throw new InvalidOperationException(
                $"{type.FullName}.{propertyName} expected type '{propertyType.FullName}' but got '{property.PropertyType.FullName}'. Rebuild the package-mode SDK smoke output.");
        }
    }

    private static object? GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static void SetField(object instance, string fieldName, object value)
    {
        FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(instance.GetType().FullName, fieldName);
        field.SetValue(instance, value);
    }

    private static void ValidateRetainedOwnerBranchFillsPhysicalTarget(
        Assembly proGpuWpf,
        Assembly proGpuBackend,
        Assembly silkNetWebGpu)
    {
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type retainedSinkType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.Composition.ProGpuRetainedCompositionCommandSink");
        Type gpuTextureType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTexture");
        Type gpuTextureAlphaModeType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureAlphaMode");
        Type gpuTextureDimensionType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureDimension");
        Type textureFormatType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureFormat");
        Type textureUsageType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureUsage");
        object wpfVisual = CreateRedWpfVisual();
        string runtimeRoot = Path.GetDirectoryName(proGpuWpf.Location) ?? AppContext.BaseDirectory;

        PreloadWebGpuNativeRuntime(runtimeRoot);
        using IDisposable currentDirectory = new CurrentDirectoryScope(runtimeRoot);
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
            "SDK smoke retained-owner HiDPI framebuffer target",
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

            MethodInfo render = RequireMethodByParameterNames(
                compositionTargetType,
                "Render",
                "logicalWidth",
                "logicalHeight",
                "pixelWidth",
                "pixelHeight",
                "dpiScale",
                "targetView");
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
                "SDK smoke retained-owner WPF HiDPI upper-left pixel");
            AssertRgbaPixelIsRed(
                pixels,
                width: 840,
                x: 780,
                y: 1560,
                "SDK smoke retained-owner WPF HiDPI lower-right pixel");
        }
        finally
        {
            (texture as IDisposable)?.Dispose();
            (target as IDisposable)?.Dispose();
        }
    }

    private static void ValidateRetainedOwnerBranchPreservesLogicalMarkerOrigin(
        Assembly proGpuWpf,
        Assembly proGpuBackend,
        Assembly silkNetWebGpu)
    {
        Type compositionTargetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        Type retainedSinkType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.Composition.ProGpuRetainedCompositionCommandSink");
        Type gpuTextureType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTexture");
        Type gpuTextureAlphaModeType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureAlphaMode");
        Type gpuTextureDimensionType = GetRequiredType(proGpuBackend, "ProGPU.Backend.GpuTextureDimension");
        Type textureFormatType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureFormat");
        Type textureUsageType = GetRequiredType(silkNetWebGpu, "Silk.NET.WebGPU.TextureUsage");
        object wpfVisual = CreateLogicalMarkerWpfVisual();
        string runtimeRoot = Path.GetDirectoryName(proGpuWpf.Location) ?? AppContext.BaseDirectory;

        PreloadWebGpuNativeRuntime(runtimeRoot);
        using IDisposable currentDirectory = new CurrentDirectoryScope(runtimeRoot);
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
            "SDK smoke retained-owner logical marker HiDPI target",
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

            MethodInfo render = RequireMethodByParameterNames(
                compositionTargetType,
                "Render",
                "logicalWidth",
                "logicalHeight",
                "pixelWidth",
                "pixelHeight",
                "dpiScale",
                "targetView");
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
            AssertRgbaPixelIsGreen(
                pixels,
                width: 840,
                x: 340,
                y: 660,
                "SDK smoke retained-owner WPF logical marker pixel");
            AssertRgbaPixelIsNotGreen(
                pixels,
                width: 840,
                x: 660,
                y: 1300,
                "SDK smoke retained-owner WPF double-scaled marker pixel");
        }
        finally
        {
            (texture as IDisposable)?.Dispose();
            (target as IDisposable)?.Dispose();
        }
    }

    private static Border CreateRedWpfVisual()
    {
        var border = new Border
        {
            Width = 420.0,
            Height = 840.0,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00))
        };
        border.Measure(new Size(420.0, 840.0));
        border.Arrange(new Rect(0.0, 0.0, 420.0, 840.0));
        border.UpdateLayout();
        return border;
    }

    private static DrawingVisual CreateLogicalMarkerWpfVisual()
    {
        var visual = new DrawingVisual();
        using DrawingContext context = visual.RenderOpen();
        context.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(0x10, 0x70, 0x20)),
            pen: null,
            new Rect(160, 320, 80, 80));
        return visual;
    }

    private static object Create(Type type, params object?[] args)
    {
        return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args,
                culture: null)
            ?? throw new InvalidOperationException($"Could not create instance of '{type.FullName}'.");
    }

    private static object Invoke(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        return InvokeMethod(method, instance, args)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
    }

    private static void InvokeVoid(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleMethod(instance.GetType(), methodName, args)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        InvokeMethod(method, instance, args);
    }

    private static object InvokeStatic(Type type, string methodName, params object?[] args)
    {
        MethodInfo method = GetCompatibleStaticMethod(type, methodName, args)
            ?? throw new MissingMethodException(type.FullName, methodName);

        return InvokeMethod(method, null, args)
            ?? throw new InvalidOperationException($"Method '{methodName}' returned null.");
    }

    private static object? InvokeMethod(MethodInfo method, object? instance, params object?[] args)
    {
        try
        {
            return method.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo? GetCompatibleMethod(Type type, string methodName, object?[] args)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
            .Where(method => ParametersMatch(method.GetParameters(), args))
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

    private static object CombineEnumFlags(Type enumType, params object[] values)
    {
        ulong combined = 0;
        foreach (object value in values)
        {
            combined |= Convert.ToUInt64(value);
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
                $"Expected {description} to be red, but found RGBA({r}, {g}, {b}, {a}). Rebuild the package-mode SDK smoke output.");
        }
    }

    private static void AssertRgbaPixelIsGreen(
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
        if (r > 60 || g < 90 || b > 70 || a != 255)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be green, but found RGBA({r}, {g}, {b}, {a}). Rebuild the package-mode SDK smoke output.");
        }
    }

    private static void AssertRgbaPixelIsNotGreen(
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
        if (r <= 60 && g >= 90 && b <= 70 && a == 255)
        {
            throw new InvalidOperationException(
                $"Expected {description} not to contain the logical marker, but found RGBA({r}, {g}, {b}, {a}). Rebuild the package-mode SDK smoke output.");
        }
    }

    private static void ValidateRuntimeAssetMatchesLocalPackage(
        Assembly assembly,
        string packageId,
        string assemblySimpleName,
        string targetFramework)
    {
        if (!TryFindLocalPackageFeed(out string packageFeed))
        {
            return;
        }

        string runtimeAssemblyPath = assembly.Location;
        if (string.IsNullOrWhiteSpace(runtimeAssemblyPath) || !File.Exists(runtimeAssemblyPath))
        {
            throw new InvalidOperationException(
                $"SDK smoke output could not locate loaded assembly '{assemblySimpleName}'. Rebuild the package-mode SDK smoke output.");
        }

        string packageVersion = packageId == "LibreWPF.ProGPU"
            ? LibreWpfPackageVersion
            : ProGpuPackageVersion;
        string packagePath = Path.Combine(packageFeed, $"{packageId}.{packageVersion}.nupkg");
        if (!File.Exists(packagePath))
        {
            throw new InvalidOperationException(
                $"SDK smoke output could not find local package '{packagePath}'. Repack the local SDK feed.");
        }

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string entryName = $"lib/{targetFramework}/{assemblySimpleName}.dll";
        ZipArchiveEntry entry = package.GetEntry(entryName)
            ?? throw new InvalidOperationException(
                $"SDK smoke local package '{packageId}' is missing '{entryName}'. Repack the local SDK feed.");

        using Stream entryStream = entry.Open();
        string packageHash = ComputeStreamSha256(entryStream);
        string runtimeHash = ComputeFileSha256(runtimeAssemblyPath);
        if (!string.Equals(packageHash, runtimeHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SDK smoke loaded '{assemblySimpleName}.dll' does not match '{packageId}.{packageVersion}.nupkg'. Rebuild the package-mode SDK smoke output.");
        }
    }

    private static bool TryFindLocalPackageFeed(out string packageFeed)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "artifacts",
                "packages",
                "Release",
                "NonShipping");
            if (Directory.Exists(candidate))
            {
                packageFeed = candidate;
                return true;
            }
        }

        packageFeed = string.Empty;
        return false;
    }

    private static void PreloadWebGpuNativeRuntime(string runtimeRoot)
    {
        foreach (string candidate in GetWebGpuNativeRuntimeCandidates())
        {
            string path = Path.Combine(runtimeRoot, candidate);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
            {
                return;
            }
        }

        throw new FileNotFoundException(
            $"SDK smoke output could not load the WebGPU native runtime from '{runtimeRoot}'. Rebuild the package-mode SDK smoke output.");
    }

    private static string[] GetWebGpuNativeRuntimeCandidates()
    {
        return
        [
            "libwgpu_native.dylib",
            "wgpu_native.dll",
            "libwgpu_native.so",
            "wgpu.dll",
            "libwgpu.so"
        ];
    }

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return ComputeStreamSha256(stream);
    }

    private static string ComputeStreamSha256(Stream stream)
    {
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void AssertEqual(object expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{description} expected '{expected}' but got '{actual}'. Rebuild the package-mode SDK smoke output.");
        }
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
}
