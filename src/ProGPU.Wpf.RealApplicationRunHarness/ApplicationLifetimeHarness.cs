using System.Reflection;
using System.Runtime.CompilerServices;
using ProGPU.Wpf.Interop;

internal static partial class Program
{
    // Diagnostic-only public source API reflection, required by the isolated WPF
    // assembly loader. Replace with direct source references when this harness can
    // bind those assemblies normally. Product lifetime and registrar paths are typed.
    private static void RunApplicationLifetimeHarness(string repoRoot, string frameworkPath,
        string corePath, string compilerPath, string scenario)
    {
        if (scenario is not ("last-window" or "main-window" or "explicit"))
            throw new ArgumentException("Expected last-window, main-window, or explicit lifetime scenario.");

        PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.Portable);
        var context = new WpfAssemblyLoadContext(repoRoot, frameworkPath, corePath, compilerPath);
        object? application = null;
        IPortableWindowActivationServiceRegistrar? registrar = null;
        var activations = new List<RecordingActivation>();
        int runs = 0, closed = 0, disposed = 0, exits = 0, postedReopens = 0;
        try
        {
            Assembly core = context.LoadFromAssemblyPath(corePath);
            Assembly framework = context.LoadFromAssemblyPath(frameworkPath);
            RuntimeHelpers.RunModuleConstructor(core.ManifestModule.ModuleHandle);
            RuntimeHelpers.RunModuleConstructor(framework.ManifestModule.ModuleHandle);
            if (!PortableWpfServiceRegistry.TryGetWindowActivationService(PortableWpfServiceKey.PresentationFramework, out registrar))
                throw new InvalidOperationException("Source WPF did not publish its typed window registrar.");

            Type appType = GetRequiredType(framework, "System.Windows.Application");
            Type shutdownMode = GetRequiredType(framework, "System.Windows.ShutdownMode");
            Type sourceFactory = GetRequiredType(core, "System.Windows.PortablePresentationSourceHost");
            MethodInfo createSource = sourceFactory.GetMethod("Create", [typeof(double), typeof(double)])!;
            Type sourceHostType = GetRequiredType(core, "System.Windows.IPortablePresentationSourceHost");
            PropertyInfo sourceRoot = sourceHostType.GetProperty("RootVisual")!;
            PropertyInfo sourceHandle = sourceHostType.GetProperty("Handle")!;
            MethodInfo sourceSize = sourceHostType.GetMethod("SetClientSize")!;
            application = Create(framework, "System.Windows.Application");
            AssertEqual("OnLastWindowClose", GetProperty(application, "ShutdownMode").ToString(), "default shutdown mode");
            if (scenario == "main-window")
                SetProperty(application, "ShutdownMode", Enum.Parse(shutdownMode, "OnMainWindowClose"));

            EventInfo exitEvent = appType.GetEvent("Exit")!;
            Action<object?, EventArgs> onExit = (_, _) => exits++;
            exitEvent.AddEventHandler(application,
                Delegate.CreateDelegate(exitEvent.EventHandlerType!, onExit.Target, onExit.Method));

            object NewWindow(string title)
            {
                object window = Create(framework, "System.Windows.Window");
                SetProperty(window, "Title", title);
                SetProperty(window, "Width", 320.0);
                SetProperty(window, "Height", 200.0);
                window.GetType().GetEvent("Closed")!.AddEventHandler(window, (EventHandler)((_, _) => closed++));
                return window;
            }

            registrar.Register(new PortableWindowActivationCallbacks(
                activate: window =>
                {
                    object source = createSource.Invoke(null, [1.0, 1.0])!;
                    var activation = new RecordingActivation(window, source);
                    activations.Add(activation);
                    sourceRoot.SetValue(source, window);
                    return activation;
                },
                show: value => ((RecordingActivation)value).IsVisible = true,
                hide: value => ((RecordingActivation)value).IsVisible = false,
                setClientSize: (value, width, height) =>
                    sourceSize.Invoke(((RecordingActivation)value).PresentationSource, [width, height]),
                getHandle: value => (IntPtr)sourceHandle.GetValue(((RecordingActivation)value).PresentationSource)!,
                close: value => ((RecordingActivation)value).IsClosed = true,
                dispose: value =>
                {
                    var activation = (RecordingActivation)value;
                    AssertEqual(false, activation.IsDisposed, "single host disposal");
                    AssertEqual(true, activation.IsClosed, "host close before disposal");
                    activation.DisposePresentationSource();
                    activation.IsDisposed = true;
                    disposed++;
                },
                run: value =>
                {
                    var activation = (RecordingActivation)value;
                    runs++;
                    AssertSame(activations[runs - 1], activation, "source host handoff order");
                    AssertEqual(true, activation.IsVisible, "selected host preserves visibility");
                    AssertEqual(0, exits, "application remains live before requested shutdown");
                    AssertSame(application, GetStaticProperty(appType, "Current"), "application identity across handoff");
                    if (runs == 1)
                    {
                        AssertCollectionCount(GetProperty(application, "Windows"), 2, "two unowned source windows");
                        Invoke(activation.Window, "Close");
                        AssertEqual(1, closed, "first window closed");
                        AssertEqual(null, appType.GetProperty("MainWindow")!.GetValue(application), "source clears closed main window");
                        return;
                    }

                    if (runs == 2)
                    {
                        AssertEqual(false, activations[1].IsDisposed, "remaining host survives first close");
                        if (scenario == "explicit")
                            SetProperty(application, "ShutdownMode", Enum.Parse(shutdownMode, "OnExplicitShutdown"));
                        Invoke(activation.Window, "Close");
                        AssertCollectionCount(GetProperty(application, "Windows"), 0, "last window removed");
                        if (scenario == "explicit")
                        {
                            // Only the application's hostless dispatcher can execute
                            // this work after the last host callback has returned.
                            object dispatcher = GetProperty(application, "Dispatcher");
                            Action reopen = () =>
                            {
                                AssertEqual(0, exits, "explicit lifetime survives no windows");
                                postedReopens++;
                                Invoke(NewWindow("Reopened after windowless wait"), "Show");
                            };
                            dispatcher.GetType().GetMethod("BeginInvoke", [typeof(Delegate), typeof(object[])])!
                                .Invoke(dispatcher, [reopen, Array.Empty<object>()]);
                        }
                        return;
                    }

                    AssertEqual("explicit", scenario, "only explicit lifetime enters reopened host");
                    AssertEqual(1, postedReopens, "hostless dispatcher delivered reopen once");
                    try
                    {
                        Invoke(application, "Run");
                        throw new InvalidOperationException("Nested Application.Run was accepted.");
                    }
                    catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { }
                    appType.GetMethod("Shutdown", [typeof(int)])!.Invoke(application, [23]);
                }));

            object first = NewWindow("First native polling owner");
            object second = NewWindow("Independent remaining window");
            AssertSame(first, GetProperty(application, "MainWindow"), "first source main window");
            Invoke(first, "Show");
            Invoke(second, "Show");
            AssertEqual(scenario == "explicit" ? 23 : 0, Invoke(application, "Run"), "source exit code");
            int expectedRuns = scenario == "main-window" ? 1 : scenario == "last-window" ? 2 : 3;
            AssertEqual(expectedRuns, runs, "native host loop entries");
            AssertEqual(scenario == "explicit" ? 3 : 2, closed, "all source windows closed exactly once");
            AssertEqual(closed, disposed, "all native activations disposed exactly once");
            AssertEqual(1, exits, "single application Exit event");
            AssertEqual(scenario == "explicit" ? 1 : 0, postedReopens, "deferred reopen count");
        }
        finally
        {
            if (application != null) TryInvoke(application, "Shutdown");
            registrar?.Clear();
            foreach (var activation in activations)
                if (!activation.IsDisposed) activation.DisposePresentationSource();
            context.Unload();
        }
    }
}
