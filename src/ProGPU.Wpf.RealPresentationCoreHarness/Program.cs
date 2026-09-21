using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuRenderCommand = global::ProGPU.Scene.RenderCommand;
using ProGpuRenderCommandType = global::ProGPU.Scene.RenderCommandType;
using ProGpuVisual = global::ProGPU.Scene.Visual;

public static class Program
{
    private const string ProviderTypeName = "System.Windows.Media.RenderDataDrawingContextSinkProvider";
    private const string PortableProviderTypeName = "System.Windows.Media.PortableRenderDataDrawingContextSinkProvider";
    private const string PortableSinkInterfaceTypeName = "System.Windows.Media.IPortableRenderDataDrawingContextSink";
    private const string SinkInterfaceTypeName = "System.Windows.Media.IRenderDataDrawingContextSink";

    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationCorePath = FindRealPresentationCoreAssembly(repoRoot);
            RunHarness(repoRoot, presentationCorePath);
            Console.WriteLine("Real PresentationCore render-data provider registration succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHarness(string repoRoot, string presentationCorePath)
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var frame = target.BeginDrawingFrame(64, 32);

        var loadContext = new WpfAssemblyLoadContext(repoRoot, presentationCorePath);
        Assembly presentationCore = loadContext.LoadFromAssemblyPath(presentationCorePath);

        Type providerType = GetRequiredType(presentationCore, ProviderTypeName);
        Type drawingVisualType = GetRequiredType(presentationCore, "System.Windows.Media.DrawingVisual");
        Type sinkInterfaceType = GetRequiredType(presentationCore, SinkInterfaceTypeName);

        MethodInfo createSink = providerType.GetMethod(
            "CreateSink",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(providerType.FullName, "CreateSink");

        IDisposable registration = RegisterRealPortableObjectSinkProvider(
            presentationCore,
            frame,
            imageSourceAdapter: null);

        object ownerVisual = RuntimeHelpers.GetUninitializedObject(drawingVisualType);

        using (registration)
        {
            object sink = createSink.Invoke(null, new[] { ownerVisual })
                ?? throw new InvalidOperationException("Real PresentationCore provider returned a null sink.");

            if (frame.ObjectRenderDataSinkContextCount != 1 ||
                frame.DrawingContextCount != 0 ||
                !ReferenceEquals(frame.LastOwnerVisual, ownerVisual))
            {
                throw new InvalidOperationException(
                    $"Expected one object sink, zero drawing contexts, and the owner visual; got object sinks={frame.ObjectRenderDataSinkContextCount}, drawing contexts={frame.DrawingContextCount}, owner={frame.LastOwnerVisual}.");
            }

            MethodInfo pushOpacity = sinkInterfaceType.GetMethod(
                "PushOpacity",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(double) },
                modifiers: null)
                ?? throw new MissingMethodException(sinkInterfaceType.FullName, "PushOpacity");

            MethodInfo pop = sinkInterfaceType.GetMethod(
                "Pop",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
                ?? throw new MissingMethodException(sinkInterfaceType.FullName, "Pop");

            MethodInfo close = sinkInterfaceType.GetMethod(
                "Close",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null)
                ?? throw new MissingMethodException(sinkInterfaceType.FullName, "Close");

            pushOpacity.Invoke(sink, new object[] { 0.5 });
            pop.Invoke(sink, Array.Empty<object>());
            close.Invoke(sink, Array.Empty<object>());
        }

        if (target.RootVisual.Context.Commands.Count != 0)
        {
            throw new InvalidOperationException(
                $"Expected real provider RenderOpen commands to use the retained WPF owner branch, but the flat root received {target.RootVisual.Context.Commands.Count} commands.");
        }

        ProGpuContainerVisual retainedFrameRoot = GetSingleContainerChild(
            target.RetainedWpfVisualRoot,
            "retained WPF frame root");
        ProGpuVisual ownerBranch = GetSingleChild(
            retainedFrameRoot,
            "real provider owner branch");

        if (!target.RetainedVisualBranchMap.TryGetVisuals(ownerVisual, out IReadOnlyList<ProGpuVisual> ownerVisuals) ||
            ownerVisuals.Count != 1 ||
            !ReferenceEquals(ownerVisuals[0], ownerBranch))
        {
            throw new InvalidOperationException("Real PresentationCore owner visual was not mapped to the retained ProGPU owner branch.");
        }

        IReadOnlyList<ProGpuRenderCommand> commands = GetRetainedCommands(ownerBranch);
        if (commands.Count != 2 ||
            commands[0].Type != ProGpuRenderCommandType.PushOpacity ||
            commands[1].Type != ProGpuRenderCommandType.PopOpacity)
        {
            throw new InvalidOperationException(
                $"Expected retained owner branch PushOpacity/PopOpacity commands after real sink dispatch, got {commands.Count} commands.");
        }

        object restoredOwnerVisual = RuntimeHelpers.GetUninitializedObject(drawingVisualType);
        object? restoredSink = createSink.Invoke(null, new[] { restoredOwnerVisual });
        if (restoredSink != null)
        {
            throw new InvalidOperationException("Real PresentationCore sink provider did not restore to null after disposing registration.");
        }

        loadContext.Unload();
    }

    private static IDisposable RegisterRealPortableObjectSinkProvider(
        Assembly presentationCore,
        ProGpuWpfDrawingFrame frame,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        Type providerType = GetRequiredType(presentationCore, PortableProviderTypeName);
        Type portableSinkInterfaceType = GetRequiredType(presentationCore, PortableSinkInterfaceTypeName);
        Type proxyType = BuildPortableSinkProxyType(portableSinkInterfaceType);
        Type factoryType = BuildPortableSinkFactoryType(portableSinkInterfaceType, proxyType);

        object factory = Activator.CreateInstance(factoryType, frame, imageSourceAdapter)
            ?? throw new InvalidOperationException("Failed to create the portable sink factory proxy.");
        Type delegateType = typeof(Func<,>).MakeGenericType(typeof(object), portableSinkInterfaceType);
        MethodInfo createMethod = factoryType.GetMethod(
            "Create",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(factoryType.FullName, "Create");
        Delegate sinkFactory = Delegate.CreateDelegate(delegateType, factory, createMethod);

        MethodInfo pushMethod = providerType.GetMethod(
            "PushObjectSinkFactory",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types: new[] { delegateType },
            modifiers: null)
            ?? throw new MissingMethodException(providerType.FullName, "PushObjectSinkFactory");

        return (IDisposable)(pushMethod.Invoke(null, new object[] { sinkFactory })
            ?? throw new InvalidOperationException("Real PresentationCore portable provider returned null registration."));
    }

    private static Type BuildPortableSinkFactoryType(Type portableSinkInterfaceType, Type proxyType)
    {
        AssemblyName assemblyName = new("ProGpuWpfRealPortableSinkFactoryProxy");
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder typeBuilder = moduleBuilder.DefineType(
            "PortableSinkFactoryProxy",
            TypeAttributes.Public | TypeAttributes.Sealed);
        FieldBuilder frameField = typeBuilder.DefineField(
            "_frame",
            typeof(ProGpuWpfDrawingFrame),
            FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder imageSourceAdapterField = typeBuilder.DefineField(
            "_imageSourceAdapter",
            typeof(IWpfImageSourceAdapter),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        ConstructorBuilder constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(ProGpuWpfDrawingFrame), typeof(IWpfImageSourceAdapter) });
        ILGenerator ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, frameField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, imageSourceAdapterField);
        ctorIl.Emit(OpCodes.Ret);

        MethodInfo openSinkContext = typeof(ProGpuWpfDrawingFrame).GetMethod(
            nameof(ProGpuWpfDrawingFrame.OpenObjectRenderDataSinkContext),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(object), typeof(IWpfImageSourceAdapter) },
            modifiers: null)
            ?? throw new MissingMethodException(typeof(ProGpuWpfDrawingFrame).FullName, nameof(ProGpuWpfDrawingFrame.OpenObjectRenderDataSinkContext));
        ConstructorInfo proxyConstructor = proxyType.GetConstructor(new[] { typeof(object) })
            ?? throw new MissingMethodException(proxyType.FullName, ".ctor(object)");
        MethodBuilder createMethod = typeBuilder.DefineMethod(
            "Create",
            MethodAttributes.Public,
            portableSinkInterfaceType,
            new[] { typeof(object) });
        ILGenerator createIl = createMethod.GetILGenerator();
        createIl.Emit(OpCodes.Ldarg_0);
        createIl.Emit(OpCodes.Ldfld, frameField);
        createIl.Emit(OpCodes.Ldarg_1);
        createIl.Emit(OpCodes.Ldarg_0);
        createIl.Emit(OpCodes.Ldfld, imageSourceAdapterField);
        createIl.Emit(OpCodes.Callvirt, openSinkContext);
        createIl.Emit(OpCodes.Newobj, proxyConstructor);
        createIl.Emit(OpCodes.Castclass, portableSinkInterfaceType);
        createIl.Emit(OpCodes.Ret);

        return typeBuilder.CreateTypeInfo()!.AsType();
    }

    private static Type BuildPortableSinkProxyType(Type portableSinkInterfaceType)
    {
        AssemblyName assemblyName = new("ProGpuWpfRealPortableSinkProxy");
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder typeBuilder = moduleBuilder.DefineType(
            "PortableSinkProxy",
            TypeAttributes.Public | TypeAttributes.Sealed);
        typeBuilder.AddInterfaceImplementation(portableSinkInterfaceType);
        FieldBuilder innerField = typeBuilder.DefineField(
            "_inner",
            typeof(object),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        ConstructorBuilder constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(object) });
        ILGenerator ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, innerField);
        ctorIl.Emit(OpCodes.Ret);

        MethodInfo forwardMethod = typeof(Program).GetMethod(
            nameof(ForwardPortableSinkCall),
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new MissingMethodException(typeof(Program).FullName, nameof(ForwardPortableSinkCall));

        foreach (MethodInfo interfaceMethod in portableSinkInterfaceType.GetMethods())
        {
            if (interfaceMethod.ReturnType != typeof(void))
            {
                throw new NotSupportedException($"Portable sink method '{interfaceMethod.Name}' must return void.");
            }

            ParameterInfo[] parameters = interfaceMethod.GetParameters();
            Type[] parameterTypes = parameters.Select(parameter => parameter.ParameterType).ToArray();
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                interfaceMethod.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                typeof(void),
                parameterTypes);
            ILGenerator il = methodBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, innerField);
            il.Emit(OpCodes.Ldstr, interfaceMethod.Name);
            il.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
            il.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i + 1);
                if (parameterTypes[i].IsValueType)
                {
                    il.Emit(OpCodes.Box, parameterTypes[i]);
                }

                il.Emit(OpCodes.Stelem_Ref);
            }

            il.Emit(OpCodes.Call, forwardMethod);
            il.Emit(OpCodes.Ret);
            typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);
        }

        return typeBuilder.CreateTypeInfo()!.AsType();
    }

    public static void ForwardPortableSinkCall(
        object sink,
        string methodName,
        int parameterCount,
        object?[] arguments)
    {
        MethodInfo method = sink.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate =>
                candidate.Name == methodName &&
                candidate.GetParameters().Length == parameterCount)
            ?? throw new MissingMethodException(sink.GetType().FullName, $"{methodName}({parameterCount} args)");

        method.Invoke(sink, arguments);
    }

    private static ProGpuContainerVisual GetSingleContainerChild(ProGpuContainerVisual parent, string description)
    {
        ProGpuVisual visual = GetSingleChild(parent, description);
        return visual as ProGpuContainerVisual
            ?? throw new InvalidOperationException($"Expected {description} to be a container visual, got {visual.GetType().FullName}.");
    }

    private static ProGpuVisual GetSingleChild(ProGpuContainerVisual parent, string description)
    {
        if (parent.Children.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {description}, got {parent.Children.Count} children.");
        }

        return parent.Children[0];
    }

    private static IReadOnlyList<ProGpuRenderCommand> GetRetainedCommands(ProGpuVisual visual)
    {
        PropertyInfo contextProperty = visual.GetType().GetProperty(
            "Context",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' does not expose a drawing context.");

        ProGpuDrawingContext context = contextProperty.GetValue(visual) as ProGpuDrawingContext
            ?? throw new InvalidOperationException(
                $"Retained owner branch type '{visual.GetType().FullName}' exposed an unexpected context value.");

        return context.Commands;
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static string FindRealPresentationCoreAssembly(string repoRoot)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", "PresentationCore");
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Real PresentationCore artifacts directory was not found: {artifactsRoot}");
        }

        string[] candidates = Directory.GetFiles(
            artifactsRoot,
            "PresentationCore.dll",
            SearchOption.AllDirectories);

        string? selected = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return selected
            ?? throw new FileNotFoundException("Could not locate a net10.0 real PresentationCore.dll artifact.", artifactsRoot);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string marker = Path.Combine(
                directory.FullName,
                "src",
                "Microsoft.DotNet.Wpf",
                "src",
                "PresentationCore",
                "PresentationCore.csproj");

            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WPF repository root.");
    }

    private sealed class WpfAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _repoRoot;
        private readonly string _presentationCorePath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(string repoRoot, string presentationCorePath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationCorePath = presentationCorePath;
            _resolver = new AssemblyDependencyResolver(presentationCorePath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string artifactAssemblyPath = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                assemblyName.Name ?? string.Empty,
                "Debug",
                "net10.0",
                $"{assemblyName.Name}.dll");

            if (File.Exists(artifactAssemblyPath))
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}
