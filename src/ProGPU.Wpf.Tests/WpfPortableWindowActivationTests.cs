using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

public sealed class WpfPortableWindowActivationTests
{
    [Fact]
    public void PresentationFrameworkActivationRegistrationUsesTypedInteropOnly()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);

        var registered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation();

        Assert.True(registered);
        Assert.Equal(1, service.RegisterCount);
        Assert.NotNull(service.Callbacks);
        Assert.NotNull(service.Callbacks.Activate);
        Assert.NotNull(service.Callbacks.Show);
        Assert.NotNull(service.Callbacks.Hide);
        Assert.NotNull(service.Callbacks.SetWindowState);
        Assert.NotNull(service.Callbacks.SetTitle);
        Assert.NotNull(service.Callbacks.SetIcon);
        Assert.NotNull(service.Callbacks.SetClientSize);
        Assert.NotNull(service.Callbacks.SetPosition);
        Assert.NotNull(service.Callbacks.SetTopmost);
        Assert.NotNull(service.Callbacks.SetWindowBorder);
        Assert.NotNull(service.Callbacks.Close);
        Assert.NotNull(service.Callbacks.Run);
        Assert.NotNull(service.Callbacks.Dispose);
        Assert.NotNull(service.Callbacks.DragMove);
        Assert.NotNull(service.Callbacks.GetHandle);
        Assert.NotNull(service.Callbacks.SetWindowRegion);
        Assert.NotNull(service.Callbacks.RequestActivation);
    }

    [Fact]
    public void ClipboardRegistrationUsesTypedInteropServiceOnly()
    {
        var service = new TestClipboardServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterClipboardService(service);
        var registerCountBefore = service.RegisterCount;

        var registered = WpfPortableWindowActivation.TryRegisterPresentationCoreClipboardService();

        Assert.True(registered);
        Assert.Equal(registerCountBefore + 1, service.RegisterCount);
        Assert.NotNull(service.GetText);
        Assert.NotNull(service.SetText);
    }

    [Fact]
    public void PresentationFrameworkServiceRegistrationUsesTypedInteropOnly()
    {
        var launcherService = new TestLauncherServiceRegistrar();
        var messageBoxService = new TestMessageBoxServiceRegistrar();
        var fileDialogService = new TestFileDialogServiceRegistrar();
        using var launcherRegistration = PortableWpfServiceRegistry.RegisterLauncherService(launcherService);
        using var messageBoxRegistration = PortableWpfServiceRegistry.RegisterMessageBoxService(messageBoxService);
        using var fileDialogRegistration = PortableWpfServiceRegistry.RegisterFileDialogService(fileDialogService);
        var launcherRegisterCountBefore = launcherService.RegisterCount;
        var messageBoxRegisterCountBefore = messageBoxService.RegisterCount;
        var fileDialogRegisterCountBefore = fileDialogService.RegisterCount;

        var launcherRegistered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkLauncherService();
        var messageBoxRegistered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkMessageBoxService();
        var fileDialogRegistered = WpfPortableWindowActivation.TryRegisterPresentationFrameworkFileDialogService();

        Assert.True(launcherRegistered);
        Assert.True(messageBoxRegistered);
        Assert.True(fileDialogRegistered);
        Assert.Equal(launcherRegisterCountBefore + 1, launcherService.RegisterCount);
        Assert.Equal(messageBoxRegisterCountBefore + 1, messageBoxService.RegisterCount);
        Assert.Equal(1, messageBoxService.FallbackRegisterCount);
        Assert.Equal(fileDialogRegisterCountBefore + 1, fileDialogService.RegisterCount);
        Assert.NotNull(launcherService.Launch);
        Assert.NotNull(messageBoxService.Show);
        Assert.NotNull(fileDialogService.ShowDialogResult);
    }

    [Fact]
    public void WinFormsMessageBoxRegistrationUsesTypedInteropServiceOnly()
    {
        var service = new TestMessageBoxServiceRegistrar(PortableWpfServiceKey.WinForms);
        using var registration = PortableWpfServiceRegistry.RegisterMessageBoxService(service);
        var registerCountBefore = service.RegisterCount;

        var registered = WpfPortableWindowActivation.TryRegisterWinFormsCompatMessageBoxService();

        Assert.True(registered);
        Assert.Equal(registerCountBefore + 1, service.RegisterCount);
        Assert.Equal(1, service.FallbackRegisterCount);
        Assert.NotNull(service.Show);
    }

    [Fact]
    public void LateWinFormsMessageBoxRegistrationIsBoundByTypedRegistryEvent()
    {
        RuntimeHelpers.RunClassConstructor(typeof(WpfPortableWindowActivation).TypeHandle);
        var service = new TestMessageBoxServiceRegistrar(PortableWpfServiceKey.WinForms);
        var observedRegistrationCount = 0;

        void ObserveRegistration(IPortableMessageBoxServiceRegistrar registeredService)
        {
            if (ReferenceEquals(registeredService, service))
            {
                observedRegistrationCount++;
            }
        }

        PortableWpfServiceRegistry.MessageBoxServiceRegistered += ObserveRegistration;
        try
        {
            using var registration = PortableWpfServiceRegistry.RegisterMessageBoxService(service);

            Assert.Equal(1, observedRegistrationCount);
            Assert.Equal(1, service.RegisterCount);
            Assert.Equal(1, service.FallbackRegisterCount);
            Assert.NotNull(service.Show);
        }
        finally
        {
            PortableWpfServiceRegistry.MessageBoxServiceRegistered -= ObserveRegistration;
        }
    }

    [Fact]
    public void FileDialogPatternParserKeepsWpfFilterSemantics()
    {
        var patterns = WpfPortableWindowActivation.ReadFileDialogPatterns(
            "Images| *.png ;*.jpg |Text|*.txt;; *.md |All files| *.* ");

        Assert.Equal(new[] { "*.png", "*.jpg", "*.txt", "*.md", "*.*" }, patterns);
        Assert.Empty(WpfPortableWindowActivation.ReadFileDialogPatterns(string.Empty));
        Assert.Empty(WpfPortableWindowActivation.ReadFileDialogPatterns("Description only"));
        Assert.Empty(WpfPortableWindowActivation.ReadFileDialogPatterns("Empty| ; ; |Description"));
    }

    [Fact]
    public void FileDialogResultRegistrationFallsBackToFirstPathForLegacyRegistrar()
    {
        var service = new TestLegacyFileDialogServiceRegistrar();
        IPortableFileDialogServiceRegistrar registrar = service;
        using IDisposable registration = registrar.RegisterResult(_ =>
            new PortableFileDialogResult(["/tmp/first.cs", "/tmp/second.cs"]));

        Assert.NotNull(service.ShowDialog);
        string? selectedPath = service.ShowDialog(new PortableFileDialogRequest(
            "OpenFile",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            1,
            allowMultipleSelection: true));

        Assert.Equal("/tmp/first.cs", selectedPath);
    }

    [Fact]
    public void AttachUsesTypedMediaContextRenderInteropServiceOnly()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Equal(1, service.MediaContextRenderRegisterCount);
        Assert.Same(window, service.LastMediaContextRenderWindow);
        Assert.NotNull(service.RequestRender);
        Assert.False(service.LastMediaContextRenderRegistration?.IsDisposed);

        activation.Dispose();

        Assert.True(service.LastMediaContextRenderRegistration?.IsDisposed);
    }

    [Fact]
    public void WindowRegionCallbackUsesTypedHandleMap()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource
        {
            Handle = new IntPtr(42)
        };
        var service = new TestWindowActivationServiceRegistrar();
        using var registration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);

        Assert.True(WpfPortableWindowActivation.TryRegisterPresentationFrameworkActivation());
        Assert.True(WpfPortableWindowActivation.TryAttach(host, window, source, out var activation));
        Assert.NotNull(activation);
        Assert.NotNull(service.Callbacks?.SetWindowRegion);

        var region = new PortableWindowRegion(
            new PortableRect(0, 0, 320, 240),
            new[]
            {
                new PortableRect(16, 24, 64, 32)
            });

        var applied = service.Callbacks.SetWindowRegion(source.Handle, region);

        Assert.True(applied);
        Assert.Same(region, host.WindowRegion);

        activation.Dispose();
        Assert.False(service.Callbacks.SetWindowRegion(source.Handle, region));
    }

    [Fact]
    public void DiagnosticsResolveActiveHostWithoutReflectionUntilActivationDisposes()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        Assert.False(ProGpuWpfDiagnostics.TryGetWindowHost(window, out var missingHost));
        Assert.Null(missingHost);
        Assert.False(ProGpuWpfDiagnostics.HasGpuHitTestCache(window));

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.True(ProGpuWpfDiagnostics.TryGetWindowHost(window, out var activeHost));
        Assert.Same(host, activeHost);
        Assert.False(ProGpuWpfDiagnostics.HasGpuHitTestCache(window));
        Assert.False(ProGpuWpfDiagnostics.TryHitTestOwner(window, 1, 1, out var owner));
        Assert.False(ProGpuWpfDiagnostics.TryHitTestInputOwner(window, 1, 1, out var inputOwner));
        Assert.Null(owner);
        Assert.False(ProGpuWpfDiagnostics.TryHitTestOwners(window, 1, 1, out var owners));
        Assert.Empty(owners);
        Assert.False(ProGpuWpfDiagnostics.TryQueryHitTestBoundsOwners(window, 0, 0, 10, 10, out var boundsOwners));
        Assert.Empty(boundsOwners);

        activation.Dispose();

        Assert.False(ProGpuWpfDiagnostics.TryGetWindowHost(window, out var disposedHost));
        Assert.Null(disposedHost);
    }

    [Fact]
    public void SetTitleAndClientSizeForwardWindowPropertyChangesToHost()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = "Initial",
            Width = 640,
            Height = 480
        })
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.SetTitle("Updated");
        activation.SetClientSize(320.2, double.NaN);
        activation.SetClientSize(double.NaN, 240.1);
        activation.SetPosition(31.4, 47.6);
        activation.SetTopmost(true);

        Assert.Equal("Updated", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(241, host.Height);
        Assert.Equal(31, host.Left);
        Assert.Equal(48, host.Top);
        Assert.True(host.Topmost);
        Assert.True(scheduler.RequestCount >= 6);
    }

    [Fact]
    public void TryAttachBindsExistingPortableSourceAndUsesWindowAsRootVisual()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Same(host, activation.Host);
        Assert.Same(window, activation.Window);
        Assert.Same(window, activation.RootVisual);
        Assert.Same(source, activation.PortablePresentationSource);
        Assert.Same(window, source.RootVisual);
        Assert.Same(window, host.WpfRootVisual);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void SetWindowBorderMapsLiveResizeModeAndWindowStyleChanges()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.SetWindowBorder(FakeResizeMode.NoResize, FakeWindowStyle.SingleBorderWindow);

        Assert.Equal(ProGpuWpfWindowBorder.Fixed, host.WindowBorder);

        activation.SetWindowBorder(FakeResizeMode.CanResizeWithGrip, FakeWindowStyle.None);

        Assert.Equal(ProGpuWpfWindowBorder.HiddenResizable, host.WindowBorder);

        activation.SetWindowBorder(FakeResizeMode.NoResize, FakeWindowStyle.None);

        Assert.Equal(ProGpuWpfWindowBorder.Hidden, host.WindowBorder);
        Assert.True(scheduler.RequestCount >= 3);
    }

    [Fact]
    public void TryAttachReturnsFalseWhenSourceShapeIsMissing()
    {
        using var host = new ProGpuWpfWindowHost();

        var attached = WpfPortableWindowActivation.TryAttach(
            host,
            new FakeWindow(),
            new object(),
            out var activation);

        Assert.False(attached);
        Assert.Null(activation);
        Assert.Null(host.PortablePresentationSource);
        Assert.Null(host.WpfRootVisual);
    }

    [Fact]
    public void NativeHostClosingDoesNotUseReflectedWindowCloseFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(0, window.CloseCount);
    }

    [Fact]
    public void NativeHostClosingUsesTypedCloseService()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            HandleCloseWindow = true,
            CloseWindowResult = PortableWindowCloseResult.Closed
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        bool? canceled = null;
        host.Closing += (_, args) => canceled = args.Cancel;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(1, service.CloseWindowCount);
        Assert.Same(window, service.LastCloseWindow);
        Assert.Equal(0, window.CloseCount);
        Assert.False(canceled.GetValueOrDefault());
    }

    [Fact]
    public void NativeHostClosingUsesTypedCloseCancellation()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            HandleCloseWindow = true,
            CloseWindowResult = PortableWindowCloseResult.Canceled
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        bool? canceled = null;
        host.Closing += (_, args) => canceled = args.Cancel;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(1, service.CloseWindowCount);
        Assert.Same(window, service.LastCloseWindow);
        Assert.Equal(0, window.CloseCount);
        Assert.True(canceled);
    }

    [Fact]
    public void NativeHostClosingDoesNotUseReflectedCloseCancellationFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow
        {
            CancelClose = true
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        bool? canceled = null;
        host.Closing += (_, args) => canceled = args.Cancel;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(0, window.CloseCount);
        Assert.False(window.IsClosed);
        Assert.False(canceled.GetValueOrDefault());
    }

    [Fact]
    public void NativeHostClosingDoesNotReadReflectedDisposedStateForCancellation()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeDisposedWindow
        {
            CancelClose = true
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        bool? canceled = null;
        host.Closing += (_, args) => canceled = args.Cancel;

        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, Array.Empty<object>());

        Assert.Equal(0, window.CloseCount);
        Assert.False(window.DisposedStateForTest);
        Assert.False(canceled.GetValueOrDefault());
    }

    [Fact]
    public void ShowUsesTypedMainWindowQueryService()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            HandleMainWindowQuery = true,
            IsMainWindow = true
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Show();

        Assert.Equal(1, service.MainWindowQueryCount);
        Assert.Same(window, service.LastMainWindowQueryWindow);
        Assert.Null(
            typeof(ProGpuWpfWindowHost)
                .GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host));
    }

    [Fact]
    public void CreateHostOptionsReadsFiniteWindowShape()
    {
        var fallback = new ProGpuWpfWindowOptions
        {
            Title = "Fallback",
            Width = 800,
            Height = 600,
            Left = 1,
            Top = 2,
            Topmost = false,
            WindowBorder = ProGpuWpfWindowBorder.Hidden,
            VSync = true
        };
        var window = new FakeWindow
        {
            Title = "Portable WPF",
            Width = 640.2,
            Height = double.NaN,
            ActualHeight = 480.1,
            Left = 10.4,
            Top = 20.6,
            Topmost = true,
            AllowsTransparency = true,
            WindowState = FakeWindowState.Minimized,
            ResizeMode = FakeResizeMode.CanResizeWithGrip
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window, fallback);

        Assert.Equal("Portable WPF", options.Title);
        Assert.Equal(641, options.Width);
        Assert.Equal(481, options.Height);
        Assert.Equal(10, options.Left);
        Assert.Equal(21, options.Top);
        Assert.True(options.Topmost);
        Assert.True(options.TransparentFramebuffer);
        Assert.True(options.VSync);
        Assert.Equal(ProGpuWpfWindowState.Minimized, options.WindowState);
        Assert.Equal(ProGpuWpfWindowBorder.Resizable, options.WindowBorder);
    }

    [Fact]
    public void CreateHostOptionsPreservesExplicitEmptyTitle()
    {
        var fallback = new ProGpuWpfWindowOptions
        {
            Title = "Fallback"
        };
        var window = new FakeWindow
        {
            Title = string.Empty
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window, fallback);

        Assert.Equal(string.Empty, options.Title);
    }

    [Fact]
    public void CreateHostOptionsDoesNotUseReflectedWindowShapeFallback()
    {
        var fallback = new ProGpuWpfWindowOptions
        {
            Title = "Fallback",
            Width = 800,
            Height = 600,
            Left = 1,
            Top = 2,
            Topmost = false,
            WindowBorder = ProGpuWpfWindowBorder.Fixed,
            WindowState = ProGpuWpfWindowState.Normal
        };
        var window = new FakeReflectedWindowShape
        {
            Title = "Ignored",
            Width = 640,
            Height = 480,
            Left = 10,
            Top = 20,
            Topmost = true,
            WindowState = FakeWindowState.Maximized,
            ResizeMode = FakeResizeMode.CanResizeWithGrip
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window, fallback);

        Assert.Equal("Fallback", options.Title);
        Assert.Equal(800, options.Width);
        Assert.Equal(600, options.Height);
        Assert.Equal(1, options.Left);
        Assert.Equal(2, options.Top);
        Assert.False(options.Topmost);
        Assert.Equal(ProGpuWpfWindowState.Normal, options.WindowState);
        Assert.Equal(ProGpuWpfWindowBorder.Fixed, options.WindowBorder);
    }

    [Fact]
    public void CreateHostOptionsMapsResizableCustomChromeToHiddenResizableBorder()
    {
        var window = new FakeWindow
        {
            ResizeMode = FakeResizeMode.CanResize,
            WindowStyle = FakeWindowStyle.None
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(window);

        Assert.Equal(ProGpuWpfWindowBorder.HiddenResizable, options.WindowBorder);
    }

    [Fact]
    public void TryAttachSynchronizesInitialWindowShapeBeforeFirstRender()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = "Fallback",
            Width = 1280,
            Height = 800
        })
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow
        {
            Title = "Portable WPF",
            Width = 420,
            Height = 840,
            Left = 32,
            Top = 48,
            Topmost = true,
            WindowState = FakeWindowState.Normal,
            ResizeMode = FakeResizeMode.NoResize
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Equal("Portable WPF", host.Title);
        Assert.Equal(420, host.Width);
        Assert.Equal(840, host.Height);
        Assert.Equal(32, host.Left);
        Assert.Equal(48, host.Top);
        Assert.True(host.Topmost);
        Assert.Equal(ProGpuWpfWindowBorder.Fixed, host.WindowBorder);
        Assert.Equal(0, source.ClientSizeChangeCount);

        activation.SetClientSize(window.Width, window.Height);

        Assert.Equal(420, source.ClientWidth);
        Assert.Equal(840, source.ClientHeight);
        Assert.Equal(1, source.ClientSizeChangeCount);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void TryAttachSynchronizesExplicitEmptyTitleBeforeFirstRender()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = "Fallback"
        });
        var window = new FakeWindow
        {
            Title = string.Empty
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Equal(string.Empty, host.Title);
    }

    [Fact]
    public void TryAttachSynchronizesInitialIconBeforeFirstRender()
    {
        using var host = new ProGpuWpfWindowHost();
        var icon = new FakePortableIcon();
        var window = new FakeWindow
        {
            Icon = icon
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.Equal(
            new byte[] { 255, 0, 0, 255 },
            typeof(ProGpuWpfWindowHost)
                .GetField("_windowIconPixels", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host));
    }

    [Fact]
    public void HideAndSetWindowStateUpdateHostWithoutNativeWindow()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Hide();
        activation.SetWindowState(FakeWindowState.Maximized);

        Assert.False(host.IsVisible);
        Assert.Equal(ProGpuWpfWindowState.Maximized, host.WindowState);
        Assert.True(scheduler.RequestCount >= 3);
    }

    [Fact]
    public void TryDragMoveReturnsFalseBeforeNativeWindowExists()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);
        Assert.False(activation.TryDragMove());
    }

    [Fact]
    public void HostActivationEventsDoNotUseReflectedHandleActivateFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.FilesDropped);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);

        Assert.False(window.IsActive);
        Assert.Equal(0, window.ActivatedCount);
        Assert.Equal(0, window.DeactivatedCount);
        Assert.Equal(
            new[] { 0x0006, 0x001C, 0x0006, 0x001C, 0x0006, 0x001C, 0x0006, 0x001C },
            source.DispatchedHwndSourceHooks.Select(hook => hook.Message).ToArray());
    }

    [Fact]
    public void HostWindowGeometryEventsDispatchLegacyHwndSourceHooks()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, new WpfWindowEventArgs(WpfWindowEventKind.WindowPositionChanging, left: -12, top: 34));
        RaiseHostWindowEvent(host, new WpfWindowEventArgs(WpfWindowEventKind.WindowPositionChanged, left: -12, top: 34));
        RaiseHostWindowEvent(host, new WpfWindowEventArgs(WpfWindowEventKind.WindowSizeChanged, width: 800, height: 600));

        Assert.Equal(
            new[] { 0x0046, 0x0047, 0x0003, 0x0046, 0x0047, 0x0005 },
            source.DispatchedHwndSourceHooks.Select(hook => hook.Message).ToArray());
        Assert.Equal(-12, source.ClientOriginX);
        Assert.Equal(34, source.ClientOriginY);
        Assert.Equal(PackSignedLowHigh(-12, 34), source.DispatchedHwndSourceHooks[2].LParam);
        Assert.Equal(PackUnsignedLowHigh(800, 600), source.DispatchedHwndSourceHooks[5].LParam);
    }

    [Fact]
    public void HostNonClientMouseEventsDispatchLegacyHwndSourceHooks()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(
            host,
            new WpfWindowEventArgs(
                WpfWindowEventKind.NonClientMouseDown,
                button: WpfMouseButton.Left,
                hitTestCode: 2,
                screenX: -7,
                screenY: 42));
        RaiseHostWindowEvent(
            host,
            new WpfWindowEventArgs(
                WpfWindowEventKind.NonClientMouseDoubleClick,
                button: WpfMouseButton.Left,
                hitTestCode: 2,
                screenX: -7,
                screenY: 42));
        RaiseHostWindowEvent(
            host,
            new WpfWindowEventArgs(
                WpfWindowEventKind.NonClientMouseDown,
                button: WpfMouseButton.Right,
                hitTestCode: 2,
                screenX: 300,
                screenY: -8));
        RaiseHostWindowEvent(
            host,
            new WpfWindowEventArgs(
                WpfWindowEventKind.NonClientMouseUp,
                button: WpfMouseButton.Right,
                hitTestCode: 2,
                screenX: 300,
                screenY: -8));

        Assert.Equal(
            new[] { 0x00A1, 0x00A3, 0x00A4, 0x00A5 },
            source.DispatchedHwndSourceHooks.Select(hook => hook.Message).ToArray());
        Assert.All(source.DispatchedHwndSourceHooks, hook => Assert.Equal(new IntPtr(2), hook.WParam));
        Assert.Equal(PackSignedLowHigh(-7, 42), source.DispatchedHwndSourceHooks[0].LParam);
        Assert.Equal(PackSignedLowHigh(-7, 42), source.DispatchedHwndSourceHooks[1].LParam);
        Assert.Equal(PackSignedLowHigh(300, -8), source.DispatchedHwndSourceHooks[2].LParam);
        Assert.Equal(PackSignedLowHigh(300, -8), source.DispatchedHwndSourceHooks[3].LParam);
    }

    [Fact]
    public void HostNonClientMouseMoveDefaultsToCaptionHitTest()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(
            host,
            new WpfWindowEventArgs(
                WpfWindowEventKind.NonClientMouseMove,
                screenX: 10,
                screenY: 20));

        var hook = Assert.Single(source.DispatchedHwndSourceHooks);
        Assert.Equal(0x00A0, hook.Message);
        Assert.Equal(new IntPtr(2), hook.WParam);
        Assert.Equal(PackSignedLowHigh(10, 20), hook.LParam);
    }

    [Fact]
    public void HostWindowVisibilityEventsDispatchLegacyShowWindowHooks()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, WpfWindowEventKind.Shown);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Hidden);

        Assert.Equal(new[] { 0x0018, 0x0018 }, source.DispatchedHwndSourceHooks.Select(hook => hook.Message).ToArray());
        Assert.Equal(new IntPtr(1), source.DispatchedHwndSourceHooks[0].WParam);
        Assert.Equal(IntPtr.Zero, source.DispatchedHwndSourceHooks[1].WParam);
    }

    [Fact]
    public void ShowHideDispatchLegacyShowWindowHooks()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            HandleMainWindowQuery = true,
            IsMainWindow = true
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Show();
        activation.Hide();

        Assert.Equal(new[] { 0x0018, 0x0018 }, source.DispatchedHwndSourceHooks.Select(hook => hook.Message).ToArray());
        Assert.Equal(new IntPtr(1), source.DispatchedHwndSourceHooks[0].WParam);
        Assert.Equal(IntPtr.Zero, source.DispatchedHwndSourceHooks[1].WParam);
    }

    [Fact]
    public void HostActivationEventsUseTypedWindowActivationService()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);

        Assert.Equal(2, service.SetActivationStateCount);
        Assert.Same(window, service.LastActivationStateWindow);
        Assert.False(service.LastActivationState);
        Assert.False(window.IsActive);
        Assert.Equal(0, window.ActivatedCount);
        Assert.Equal(0, window.DeactivatedCount);
    }

    [Fact]
    public void NonActivatingOwnedWindowDoesNotDeactivateOwner()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var ownerHost = new ProGpuWpfWindowHost();
        using var ownedHost = new ProGpuWpfWindowHost();
        var ownerWindow = new FakeWindow();
        var ownedWindow = new FakeWindow
        {
            Owner = ownerWindow,
            ShowActivated = false
        };
        var ownerSource = new FakePortablePresentationSource();
        var ownedSource = new FakePortablePresentationSource();

        Assert.True(WpfPortableWindowActivation.TryAttach(ownerHost, ownerWindow, ownerSource, out var ownerActivation));
        Assert.True(WpfPortableWindowActivation.TryAttach(ownedHost, ownedWindow, ownedSource, out var ownedActivation));
        Assert.NotNull(ownerActivation);
        Assert.NotNull(ownedActivation);

        RaiseHostWindowEvent(ownedHost, WpfWindowEventKind.Activated);
        RaiseHostWindowEvent(ownerHost, WpfWindowEventKind.Deactivated);

        Assert.Equal(0, service.SetActivationStateCount);

        ownedActivation.Hide();
        RaiseHostWindowEvent(ownerHost, WpfWindowEventKind.Deactivated);

        Assert.Equal(1, service.SetActivationStateCount);
        Assert.Same(ownerWindow, service.LastActivationStateWindow);
        Assert.False(service.LastActivationState);
    }

    [Fact]
    public void CreateHostOptionsUsesPortableShowActivatedState()
    {
        var ownerWindow = new FakeWindow();
        var ownedWindow = new FakeWindow
        {
            Owner = ownerWindow,
            ShowActivated = false
        };

        var options = WpfPortableWindowActivation.CreateHostOptions(ownedWindow);

        Assert.False(options.ShowActivated);
    }

    [Fact]
    public void HostDeactivationDoesNotBubblePortableCaptureCleanupFailure()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            ThrowOnDeactivate = true
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostWindowEvent(host, WpfWindowEventKind.Deactivated);

        Assert.Equal(1, service.SetActivationStateCount);
        Assert.Same(window, service.LastActivationStateWindow);
        Assert.False(service.LastActivationState);
    }

    [Fact]
    public void DisposingActivationStopsWindowEventForwarding()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new TestRenderScheduler()
        };
        var window = new FakeActivatableWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Dispose();
        RaiseHostWindowEvent(host, WpfWindowEventKind.Activated);

        Assert.False(window.IsActive);
        Assert.Equal(0, window.ActivatedCount);
    }

    [Fact]
    public void HostInputDoesNotUseReflectedPortableWindowInputHandler()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.KeyDown,
            key: "A",
            scanCode: 42,
            modifiers: WpfInputModifiers.Control);
        int requestCountBeforeInput = scheduler.RequestCount;
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, window.InputCount);
        Assert.Null(window.LastInputArgs);
        Assert.False(args.Handled);
        Assert.True(scheduler.RequestCount > requestCountBeforeInput);
    }

    [Fact]
    public void HostInputUsesTypedActivationService()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            key: "Ignored",
            scanCode: 42,
            x: 12,
            y: 24,
            button: WpfMouseButton.XButton1,
            modifiers: WpfInputModifiers.Shift | WpfInputModifiers.Alt);
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, window.InputCount);
        Assert.Equal(1, service.InputCount);
        Assert.Same(window, service.LastInputWindow);
        Assert.NotNull(service.LastInput);
        Assert.Equal((int)WpfInputEventKind.MouseDown, service.LastInput.Kind);
        Assert.Equal("Ignored", service.LastInput.Key);
        Assert.Equal(42, service.LastInput.ScanCode);
        Assert.Equal(12, service.LastInput.X);
        Assert.Equal(24, service.LastInput.Y);
        Assert.Equal((int)WpfMouseButton.XButton1, service.LastInput.Button);
        Assert.Equal((int)(WpfInputModifiers.Shift | WpfInputModifiers.Alt), service.LastInput.Modifiers);
        Assert.True(args.Handled);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void HostInputForNonActivatingOwnedWindowKeepsOwnerActive()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var ownerWindow = new FakeWindow();
        var ownedWindow = new FakeWindow
        {
            Owner = ownerWindow,
            ShowActivated = false
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, ownedWindow, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 12, y: 24, button: WpfMouseButton.Left);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, service.SetActivationStateCount);
        Assert.Same(ownerWindow, service.LastActivationStateWindow);
        Assert.True(service.LastActivationState);
        Assert.Equal(1, service.InputCount);
        Assert.Same(ownedWindow, service.LastInputWindow);
        Assert.True(args.Handled);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void PassivePointerInputForNonActivatingOwnedWindowPreservesCurrentActivation()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var ownerWindow = new FakeWindow();
        var ownedWindow = new FakeWindow
        {
            Owner = ownerWindow,
            ShowActivated = false
        };
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, ownedWindow, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 12, y: 24);
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, service.SetActivationStateCount);
        Assert.Equal(1, service.InputCount);
        Assert.Same(ownedWindow, service.LastInputWindow);
        Assert.True(args.Handled);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void HostMouseDownDispatchesMouseActivateHookAndCanSuppressActivation()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();
        source.HwndSourceHookResponses[0x0021] = (new IntPtr(3), true);

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 12, y: 24, button: WpfMouseButton.Left);
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, service.SetActivationStateCount);
        Assert.Equal(1, service.InputCount);
        Assert.True(args.Handled);
        Assert.Equal(0x0021, source.DispatchedHwndSourceHooks[0].Message);
        Assert.Equal(PackUnsignedLowHigh(1, 0x0201), source.DispatchedHwndSourceHooks[0].LParam);
    }

    [Fact]
    public void HostMouseActivateAndEatActivatesWindowAndStopsInputForwarding()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();
        source.HwndSourceHookResponses[0x0021] = (new IntPtr(2), true);

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 12, y: 24, button: WpfMouseButton.Left);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, service.SetActivationStateCount);
        Assert.Same(window, service.LastActivationStateWindow);
        Assert.True(service.LastActivationState);
        Assert.Equal(0, service.InputCount);
        Assert.True(args.Handled);
        Assert.Equal(0x0021, source.DispatchedHwndSourceHooks[0].Message);
    }

    [Fact]
    public void HostInputDoesNotUseReflectedDispatcherQueueFallback()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeDispatchingPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        int requestCountBeforeInput = scheduler.RequestCount;
        var args = new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 12, y: 24, button: WpfMouseButton.Left);
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, window.Dispatcher.BeginInvokeCount);
        Assert.Equal(0, window.Dispatcher.InvokeCount);
        Assert.Equal(0, window.InputCount);
        Assert.Null(window.LastInputArgs);
        Assert.DoesNotContain("Input", window.FlushedPriorities);
        Assert.DoesNotContain("Render", window.FlushedPriorities);
        Assert.True(scheduler.RequestCount > requestCountBeforeInput);
    }

    [Fact]
    public void HostInputUsesTypedDispatcherQueue()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            QueueInputCallbacks = true
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeDispatchingPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        int requestCountBeforeInput = scheduler.RequestCount;
        var args = new WpfInputEventArgs(WpfInputEventKind.MouseDown, x: 12, y: 24, button: WpfMouseButton.Left);
        RaiseHostInputEvent(host, args);

        Assert.Equal(1, service.BeginInvokeInputCount);
        Assert.Same(window, service.LastBeginInvokeInputWindow);
        Assert.NotNull(service.LastBeginInvokeInputCallback);
        Assert.Equal(0, window.Dispatcher.BeginInvokeCount);
        Assert.Equal(0, service.InputCount);
        Assert.Contains("Input", service.FlushedPriorities);
        Assert.Contains("Render", service.FlushedPriorities);
        Assert.Same(window, service.LastFlushWindow);

        service.LastBeginInvokeInputCallback.Invoke();

        Assert.Equal(1, service.InputCount);
        Assert.Same(window, service.LastInputWindow);
        Assert.NotNull(service.LastInput);
        Assert.Equal((int)WpfInputEventKind.MouseDown, service.LastInput.Kind);
        Assert.Equal(0, window.InputCount);
        Assert.True(scheduler.RequestCount > requestCountBeforeInput);
    }

    [Fact]
    public void QueuedNativeInputIsProcessedAndRenderedBeforeTheNextEvent()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            QueueInputCallbacks = true,
            RunQueuedInputOnInputFlush = true
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeDispatchingPortableInputWindow();
        var source = new FakePortablePresentationSource();

        Assert.True(WpfPortableWindowActivation.TryAttach(host, window, source, out var activation));
        Assert.NotNull(activation);

        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 10, y: 20));
        RaiseHostInputEvent(host, new WpfInputEventArgs(WpfInputEventKind.MouseMove, x: 30, y: 40));

        Assert.Equal(2, service.InputCount);
        Assert.Equal(
            new[]
            {
                "BeginInput",
                "Flush:Input",
                "ProcessInput:10",
                "Flush:Render",
                "BeginInput",
                "Flush:Input",
                "ProcessInput:30",
                "Flush:Render"
            },
            service.InputDispatchLog);
    }

    [Fact]
    public void HostInputDoesNotUseReflectedHandleActivateOrInputFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakeActivatablePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(WpfInputEventKind.KeyDown, key: "A", scanCode: 42);
        RaiseHostInputEvent(host, args);

        Assert.False(window.IsActive);
        Assert.Equal(0, window.ActivatedCount);
        Assert.Equal(0, window.InputCount);
    }

    [Fact]
    public void HostInputDoesNotUseReflectedPortableInputFallbackHandler()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableInputFallbackWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.MouseWheel,
            x: 12,
            y: 24,
            deltaY: -1);
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, window.InputCount);
        Assert.Null(window.LastInputArgs);
    }

    [Fact]
    public void HostInputDoesNotUseCompatiblePresentationFrameworkInputArgsFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePresentationFrameworkPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfInputEventArgs(
            WpfInputEventKind.MouseDown,
            x: 12,
            y: 24,
            button: WpfMouseButton.XButton1,
            modifiers: WpfInputModifiers.Shift | WpfInputModifiers.Alt);
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, window.InputCount);
        Assert.Null(window.LastInputArgs);
        Assert.False(args.Handled);
    }

    [Fact]
    public void RenderWakeupDoesNotUseReflectedDispatcherFlushForFallbackQueue()
    {
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeDispatchingPortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        window.FlushedPriorities.Clear();
        var args = new WpfInputEventArgs(WpfInputEventKind.TextInput, character: 'x');
        RaiseHostInputEvent(host, args);

        Assert.Equal(0, window.Dispatcher.BeginInvokeCount);
        Assert.Equal(0, window.InputCount);
        Assert.Null(window.LastInputArgs);
        Assert.Empty(window.FlushedPriorities);
        Assert.True(scheduler.RequestCount >= 1);
    }

    [Fact]
    public void RenderWakeupUsesTypedDispatcherFlushService()
    {
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        scheduler.RequestRender();

        Assert.Contains("Input", service.FlushedPriorities);
        Assert.Contains("Render", service.FlushedPriorities);
        Assert.Contains("ApplicationIdle", service.FlushedPriorities);
        Assert.Contains(service.FlushTimeouts, timeout => timeout.HasValue);
        Assert.Same(window, service.LastFlushWindow);
    }

    [Fact]
    public void RenderWakeupTreatsSuspendedTypedDispatcherFlushAsDeferred()
    {
        var service = new TestWindowActivationServiceRegistrar
        {
            ThrowOnDispatcherFlush = true
        };
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        var scheduler = new TestRenderScheduler();
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = scheduler
        };
        var window = new FakeWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        Exception? exception = Record.Exception(scheduler.RequestRender);

        Assert.Null(exception);
        Assert.NotEmpty(service.FlushedPriorities);
    }

    [Fact]
    public void DisposingActivationStopsInputForwarding()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new TestRenderScheduler()
        };
        var window = new FakePortableInputWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Dispose();
        RaiseHostInputEvent(
            host,
            new WpfInputEventArgs(WpfInputEventKind.TextInput, character: 'x'));

        Assert.Equal(0, window.InputCount);
    }

    [Fact]
    public void HostDragDropDoesNotUseReflectedPortableWindowDropHandler()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.Drop,
            new WpfDragDropData(new[] { "/tmp/a.txt", "/tmp/b.txt" }),
            WpfDragDropEffects.Copy,
            WpfDragDropEffects.None);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Null(window.LastDropArgs);
        Assert.Equal(WpfDragDropEffects.None, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragDropDoesNotUseReflectedPortableWindowActivationService()
    {
        System.Windows.PortableWindowActivationService.Reset();
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableServiceDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.Drop,
            new WpfDragDropData(new[] { "/tmp/a.txt" }, "portable text"),
            WpfDragDropEffects.Copy | WpfDragDropEffects.Move,
            WpfDragDropEffects.Copy,
            x: 12,
            y: 24);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Equal(0, System.Windows.PortableWindowActivationService.DropCount);
        Assert.Equal(WpfDragDropEffects.Copy, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragDropUsesTypedActivationService()
    {
        System.Windows.PortableWindowActivationService.Reset();
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.Drop,
            new WpfDragDropData(new[] { "/tmp/typed.txt" }, "typed text"),
            WpfDragDropEffects.Copy | WpfDragDropEffects.Move,
            WpfDragDropEffects.Copy,
            x: 42,
            y: 84);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Equal(0, System.Windows.PortableWindowActivationService.DropCount);
        Assert.Equal(1, service.DragDropCount);
        Assert.Same(window, service.LastDragDropWindow);
        Assert.Equal((int)WpfDragDropEventKind.Drop, service.LastDragDropKind);
        Assert.Equal(new[] { "/tmp/typed.txt" }, service.LastDragDropFiles);
        Assert.Equal("typed text", service.LastDragDropText);
        Assert.Equal(42, service.LastDragDropX);
        Assert.Equal(84, service.LastDragDropY);
        Assert.Equal((int)(WpfDragDropEffects.Copy | WpfDragDropEffects.Move), service.LastDragDropAllowedEffects);
        Assert.Equal((int)WpfDragDropEffects.Copy, service.LastDragDropAcceptedEffect);
        Assert.Equal(WpfDragDropEffects.Link, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragEnterDoesNotUseReflectedPortableWindowActivationService()
    {
        System.Windows.PortableWindowActivationService.Reset();
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableServiceDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.DragEnter,
            new WpfDragDropData(new[] { "/tmp/enter.txt" }, "enter text"),
            WpfDragDropEffects.Copy | WpfDragDropEffects.Move,
            WpfDragDropEffects.Copy,
            x: 7,
            y: 9);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Equal(0, System.Windows.PortableWindowActivationService.DropCount);
        Assert.Equal(WpfDragDropEffects.Copy, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragEnterUsesTypedActivationService()
    {
        System.Windows.PortableWindowActivationService.Reset();
        var service = new TestWindowActivationServiceRegistrar();
        using var serviceRegistration = PortableWpfServiceRegistry.RegisterWindowActivationService(service);
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        var args = new WpfDragDropEventArgs(
            WpfDragDropEventKind.DragEnter,
            new WpfDragDropData(new[] { "/tmp/typed-enter.txt" }, "typed enter"),
            WpfDragDropEffects.Copy | WpfDragDropEffects.Move,
            WpfDragDropEffects.Copy,
            x: 11,
            y: 13);
        RaiseHostDragDropEvent(host, args);

        Assert.Equal(0, window.DropCount);
        Assert.Equal(0, System.Windows.PortableWindowActivationService.DropCount);
        Assert.Equal(1, service.DragDropCount);
        Assert.Equal((int)WpfDragDropEventKind.DragEnter, service.LastDragDropKind);
        Assert.Equal(new[] { "/tmp/typed-enter.txt" }, service.LastDragDropFiles);
        Assert.Equal("typed enter", service.LastDragDropText);
        Assert.Equal(11, service.LastDragDropX);
        Assert.Equal(13, service.LastDragDropY);
        Assert.Equal(WpfDragDropEffects.Link, args.AcceptedEffect);
    }

    [Fact]
    public void HostDragDropDoesNotUseReflectedPortableFileDropFallback()
    {
        using var host = new ProGpuWpfWindowHost();
        var window = new FakePortableFileDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        RaiseHostDragDropEvent(
            host,
            new WpfDragDropEventArgs(
                WpfDragDropEventKind.Drop,
                new WpfDragDropData(new[] { "/tmp/document.txt" })));

        Assert.Equal(0, window.DropCount);
        Assert.Empty(window.LastFiles);
    }

    [Fact]
    public void DisposingActivationStopsDragDropForwarding()
    {
        using var host = new ProGpuWpfWindowHost
        {
            WpfRenderScheduler = new TestRenderScheduler()
        };
        var window = new FakePortableDropWindow();
        var source = new FakePortablePresentationSource();

        var attached = WpfPortableWindowActivation.TryAttach(host, window, source, out var activation);

        Assert.True(attached);
        Assert.NotNull(activation);

        activation.Dispose();
        RaiseHostDragDropEvent(
            host,
            new WpfDragDropEventArgs(
                WpfDragDropEventKind.Drop,
                new WpfDragDropData(new[] { "/tmp/ignored.txt" })));

        Assert.Equal(0, window.DropCount);
    }

    private static void RaiseHostWindowEvent(ProGpuWpfWindowHost host, WpfWindowEventKind kind)
    {
        RaiseHostWindowEvent(host, new WpfWindowEventArgs(kind));
    }

    private static void RaiseHostWindowEvent(ProGpuWpfWindowHost host, WpfWindowEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformWindowEventReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
    }

    private static IntPtr PackSignedLowHigh(int low, int high)
    {
        uint packed = (ushort)low | ((uint)(ushort)high << 16);
        return new IntPtr(unchecked((int)packed));
    }

    private static IntPtr PackUnsignedLowHigh(int low, int high)
    {
        uint packed = (uint)(ushort)Math.Max(0, low) | ((uint)(ushort)Math.Max(0, high) << 16);
        return new IntPtr(unchecked((int)packed));
    }

    private static void RaiseHostInputEvent(ProGpuWpfWindowHost host, WpfInputEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformInputReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
    }

    private static void RaiseHostDragDropEvent(ProGpuWpfWindowHost host, WpfDragDropEventArgs args)
    {
        typeof(ProGpuWpfWindowHost)
            .GetMethod("OnPlatformDragDropReceived", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object?[] { null, args });
    }

    private sealed class FakeWindow : IPortableWindowStateSource
    {
        public string? Title { get; set; }

        public object? Icon { get; set; }

        public double Width { get; set; } = double.NaN;

        public double Height { get; set; } = double.NaN;

        public double ActualWidth { get; set; }

        public double ActualHeight { get; set; }

        public double Left { get; set; } = double.NaN;

        public double Top { get; set; } = double.NaN;

        public bool Topmost { get; set; }

        public FakeWindowState WindowState { get; set; } = FakeWindowState.Normal;

        public FakeResizeMode ResizeMode { get; set; } = FakeResizeMode.CanResize;

        public FakeWindowStyle WindowStyle { get; set; } = FakeWindowStyle.SingleBorderWindow;

        public bool ShowActivated { get; set; } = true;

        public bool AllowsTransparency { get; set; }

        public object? Owner { get; set; }

        public bool CancelClose { get; set; }

        public bool IsClosed { get; private set; }

        public int CloseCount { get; private set; }

        public void Close()
        {
            CloseCount++;
            if (!CancelClose)
            {
                IsClosed = true;
            }
        }

        public bool TryGetPortableWindowState(out PortableWindowState state)
        {
            state = new PortableWindowState
            {
                HasTitle = true,
                Title = Title,
                HasIcon = Icon != null,
                Icon = Icon,
                HasWidth = true,
                Width = Width,
                HasHeight = true,
                Height = Height,
                HasActualWidth = true,
                ActualWidth = ActualWidth,
                HasActualHeight = true,
                ActualHeight = ActualHeight,
                HasLeft = true,
                Left = Left,
                HasTop = true,
                Top = Top,
                HasWindowState = true,
                WindowState = (int)WindowState,
                HasTopmost = true,
                Topmost = Topmost,
                HasResizeMode = true,
                ResizeMode = (int)ResizeMode,
                HasWindowStyle = true,
                WindowStyle = (int)WindowStyle,
                HasShowActivated = true,
                ShowActivated = ShowActivated,
                HasAllowsTransparency = true,
                AllowsTransparency = AllowsTransparency,
                HasOwner = Owner != null,
                Owner = Owner
            };
            return true;
        }
    }

    private sealed class FakePortableIcon : IPortableBitmapSourcePixelsSource
    {
        public bool TryGetPortableBitmapSourcePixels(out PortableBitmapSourcePixels pixels)
        {
            pixels = new PortableBitmapSourcePixels(
                width: 1,
                height: 1,
                dpiX: 96,
                dpiY: 96,
                stride: 4,
                format: PortablePixelDataFormat.Bgra32,
                pixels: new byte[] { 0, 0, 255, 255 });
            return true;
        }
    }

    private sealed class FakeDisposedWindow
    {
        private bool _disposed;

        public bool CancelClose { get; set; }

        public bool DisposedStateForTest => _disposed;

        public int CloseCount { get; private set; }

        public void Close()
        {
            CloseCount++;
            if (!CancelClose)
            {
                _disposed = true;
            }
        }
    }

    private sealed class FakeReflectedWindowShape
    {
        public string? Title { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public bool Topmost { get; set; }

        public FakeWindowState WindowState { get; set; }

        public FakeResizeMode ResizeMode { get; set; }
    }

    private sealed class FakeActivatableWindow
    {
        public bool IsActive { get; private set; }

        public int ActivatedCount { get; private set; }

        public int DeactivatedCount { get; private set; }

        internal void HandleActivate(bool isActive)
        {
            if (isActive && !IsActive)
            {
                IsActive = true;
                ActivatedCount++;
            }
            else if (!isActive && IsActive)
            {
                IsActive = false;
                DeactivatedCount++;
            }
        }
    }

    private sealed class FakePortableInputWindow
    {
        public int InputCount { get; private set; }

        public WpfInputEventArgs? LastInputArgs { get; private set; }

        private void OnPortableInput(WpfInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
            e.Handled = true;
        }
    }

    private sealed class FakePortableServiceActivationWindow :
        System.Windows.IPortableWindowActivationServiceTestTarget
    {
    }

    private sealed class FakeDispatchingPortableInputWindow :
        System.Windows.IPortableWindowActivationServiceTestTarget,
        System.Windows.IPortableDispatcherFlushTarget
    {
        public FakeDispatcher Dispatcher { get; } = new();

        public List<string> FlushedPriorities { get; } = new();

        public int InputCount { get; private set; }

        public WpfInputEventArgs? LastInputArgs { get; private set; }

        private void OnPortableInput(WpfInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
        }

        public void FlushDispatcherOperations(string priorityName)
        {
            FlushedPriorities.Add(priorityName);
            if (string.Equals(priorityName, "Input", StringComparison.Ordinal))
            {
                Dispatcher.TryInvokeQueuedCallback();
            }
        }

        public void FlushDispatcherOperations(string priorityName, TimeSpan timeout)
        {
            FlushedPriorities.Add(priorityName);
        }
    }

    private sealed class FakeSuspendedDispatcherFlushWindow :
        System.Windows.IPortableWindowActivationServiceTestTarget,
        System.Windows.IPortableDispatcherFlushTarget
    {
        public int FlushCount { get; private set; }

        public void FlushDispatcherOperations(string priorityName)
        {
            FlushCount++;
            throw new InvalidOperationException("Cannot perform this operation while dispatcher processing is suspended.");
        }

        public void FlushDispatcherOperations(string priorityName, TimeSpan timeout)
        {
            FlushCount++;
            throw new InvalidOperationException("Cannot perform this operation while dispatcher processing is suspended.");
        }
    }

    private sealed class FakeDispatcher
    {
        private Delegate? _queuedCallback;
        private object[] _queuedArgs = Array.Empty<object>();

        public int BeginInvokeCount { get; private set; }

        public int InvokeCount { get; private set; }

        public bool CheckAccess()
        {
            return false;
        }

        public object BeginInvoke(Delegate callback, object[] args)
        {
            BeginInvokeCount++;
            _queuedCallback = callback;
            _queuedArgs = args;
            return new object();
        }

        public object? Invoke(Action callback)
        {
            InvokeCount++;
            throw new InvalidOperationException("Input must be queued to the WPF dispatcher instead of invoked synchronously.");
        }

        public bool TryInvokeQueuedCallback()
        {
            if (_queuedCallback == null)
            {
                return false;
            }

            InvokeQueuedCallback();
            return true;
        }

        public void InvokeQueuedCallback()
        {
            var callback = _queuedCallback
                ?? throw new InvalidOperationException("Expected a dispatcher callback to be queued.");
            _queuedCallback = null;
            callback.DynamicInvoke(_queuedArgs);
        }
    }

    private sealed class FakePortableInputFallbackWindow
    {
        public int InputCount { get; private set; }

        public WpfInputEventArgs? LastInputArgs { get; private set; }

        internal void HandlePortableInput(WpfInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
        }
    }

    private sealed class FakeActivatablePortableInputWindow
    {
        public bool IsActive { get; private set; }

        public int ActivatedCount { get; private set; }

        public int InputCount { get; private set; }

        internal void HandleActivate(bool isActive)
        {
            if (!isActive || IsActive)
            {
                return;
            }

            IsActive = true;
            ActivatedCount++;
        }

        private void OnPortableInput(WpfInputEventArgs e)
        {
            InputCount++;
        }
    }

    private sealed class FakePresentationFrameworkPortableInputWindow
    {
        public int InputCount { get; private set; }

        public PortableInputEventArgs? LastInputArgs { get; private set; }

        internal void HandlePortableInput(PortableInputEventArgs e)
        {
            InputCount++;
            LastInputArgs = e;
            e.Handled = true;
        }
    }

    private enum PortableInputEventKind
    {
        KeyDown,
        KeyUp,
        TextInput,
        MouseMove,
        MouseDown,
        MouseUp,
        MouseWheel
    }

    private enum PortableMouseButton
    {
        None,
        Left,
        Right,
        Middle,
        XButton1,
        XButton2,
        Other
    }

    [Flags]
    private enum PortableInputModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Super = 8
    }

    private sealed class PortableInputEventArgs : EventArgs
    {
        public PortableInputEventArgs(
            PortableInputEventKind kind,
            string? key = null,
            int scanCode = 0,
            char? character = null,
            double x = 0,
            double y = 0,
            double deltaX = 0,
            double deltaY = 0,
            PortableMouseButton button = PortableMouseButton.None,
            PortableInputModifiers modifiers = PortableInputModifiers.None)
        {
            Kind = kind;
            Key = key;
            ScanCode = scanCode;
            Character = character;
            X = x;
            Y = y;
            DeltaX = deltaX;
            DeltaY = deltaY;
            Button = button;
            Modifiers = modifiers;
        }

        public PortableInputEventKind Kind { get; }

        public string? Key { get; }

        public int ScanCode { get; }

        public char? Character { get; }

        public double X { get; }

        public double Y { get; }

        public double DeltaX { get; }

        public double DeltaY { get; }

        public PortableMouseButton Button { get; }

        public PortableInputModifiers Modifiers { get; }

        public bool Handled { get; set; }
    }

    private sealed class FakePortableDropWindow
    {
        public int DropCount { get; private set; }

        public WpfDragDropEventArgs? LastDropArgs { get; private set; }

        private void OnPortableDrop(WpfDragDropEventArgs e)
        {
            DropCount++;
            LastDropArgs = e;
            e.AcceptedEffect = WpfDragDropEffects.Move;
        }
    }

    private sealed class FakePortableServiceDropWindow : System.Windows.IPortableWindowActivationServiceTestTarget
    {
        public int DropCount { get; private set; }

        private void OnPortableDrop(WpfDragDropEventArgs e)
        {
            DropCount++;
            e.AcceptedEffect = WpfDragDropEffects.Move;
        }
    }

    private sealed class FakePortableFileDropWindow
    {
        public int DropCount { get; private set; }

        public IReadOnlyList<string> LastFiles { get; private set; } = Array.Empty<string>();

        internal void OnPortableFileDrop(IReadOnlyList<string> files)
        {
            DropCount++;
            LastFiles = files;
        }
    }

    private sealed class TestClipboardServiceRegistrar : IPortableClipboardServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<string?>? GetText { get; private set; }

        public Action<string?>? SetText { get; private set; }

        public PortableWpfServiceKey ServiceKey
        {
            get
            {
                return PortableWpfServiceKey.PresentationCore;
            }
        }

        public IDisposable Register(Func<string?> getText, Action<string?> setText)
        {
            RegisterCount++;
            GetText = getText;
            SetText = setText;
            return new TestClipboardRegistration();
        }

        public void Clear()
        {
            GetText = null;
            SetText = null;
        }
    }

    private sealed class TestClipboardRegistration : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class TestLauncherServiceRegistrar : IPortableLauncherServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<PortableLaunchRequest, bool>? Launch { get; private set; }

        public PortableWpfServiceKey ServiceKey
        {
            get
            {
                return PortableWpfServiceKey.PresentationFramework;
            }
        }

        public IDisposable Register(Func<PortableLaunchRequest, bool> launch)
        {
            RegisterCount++;
            Launch = launch;
            return new TestPortableServiceRegistration();
        }

        public void Clear()
        {
            Launch = null;
        }
    }

    private sealed class TestMessageBoxServiceRegistrar : IPortableMessageBoxServiceRegistrar
    {
        private readonly PortableWpfServiceKey _serviceKey;

        public TestMessageBoxServiceRegistrar()
            : this(PortableWpfServiceKey.PresentationFramework)
        {
        }

        public TestMessageBoxServiceRegistrar(PortableWpfServiceKey serviceKey)
        {
            _serviceKey = serviceKey;
        }

        public int RegisterCount { get; private set; }

        public int FallbackRegisterCount { get; private set; }

        public Func<PortableMessageBoxRequest, string?>? Show { get; private set; }

        public PortableWpfServiceKey ServiceKey
        {
            get
            {
                return _serviceKey;
            }
        }

        public IDisposable Register(Func<PortableMessageBoxRequest, string?> show)
        {
            RegisterCount++;
            Show = show;
            return new TestPortableServiceRegistration();
        }

        public IDisposable RegisterFallback(Func<PortableMessageBoxRequest, string?> show)
        {
            FallbackRegisterCount++;
            return Register(show);
        }

        public void Clear()
        {
            Show = null;
        }
    }

    private sealed class TestFileDialogServiceRegistrar : IPortableFileDialogServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public Func<PortableFileDialogRequest, string?>? ShowDialog { get; private set; }

        public Func<PortableFileDialogRequest, PortableFileDialogResult?>? ShowDialogResult { get; private set; }

        public PortableWpfServiceKey ServiceKey
        {
            get
            {
                return PortableWpfServiceKey.PresentationFramework;
            }
        }

        public IDisposable Register(Func<PortableFileDialogRequest, string?> showDialog)
        {
            RegisterCount++;
            ShowDialog = showDialog;
            return new TestPortableServiceRegistration();
        }

        public IDisposable RegisterResult(Func<PortableFileDialogRequest, PortableFileDialogResult?> showDialog)
        {
            RegisterCount++;
            ShowDialogResult = showDialog;
            return new TestPortableServiceRegistration();
        }

        public void Clear()
        {
            ShowDialog = null;
            ShowDialogResult = null;
        }
    }

    private sealed class TestLegacyFileDialogServiceRegistrar : IPortableFileDialogServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;

        public Func<PortableFileDialogRequest, string?>? ShowDialog { get; private set; }

        public IDisposable Register(Func<PortableFileDialogRequest, string?> showDialog)
        {
            ShowDialog = showDialog;
            return new TestPortableServiceRegistration();
        }

        public void Clear()
        {
            ShowDialog = null;
        }
    }

    private sealed class TestWindowActivationServiceRegistrar : IPortableWindowActivationServiceRegistrar
    {
        public int RegisterCount { get; private set; }

        public PortableWindowActivationCallbacks? Callbacks { get; private set; }

        public int MediaContextRenderRegisterCount { get; private set; }

        public object? LastMediaContextRenderWindow { get; private set; }

        public Action<object?, TimeSpan>? RequestRender { get; private set; }

        public TestPortableServiceRegistration? LastMediaContextRenderRegistration { get; private set; }

        public int SetActivationStateCount { get; private set; }

        public object? LastActivationStateWindow { get; private set; }

        public bool LastActivationState { get; private set; }

        public bool ThrowOnDeactivate { get; set; }

        public bool QueueInputCallbacks { get; set; }

        public bool RunQueuedInputOnInputFlush { get; set; }

        public int BeginInvokeInputCount { get; private set; }

        public object? LastBeginInvokeInputWindow { get; private set; }

        public Action? LastBeginInvokeInputCallback { get; private set; }

        public List<string> InputDispatchLog { get; } = new List<string>();

        public int InputCount { get; private set; }

        public object? LastInputWindow { get; private set; }

        public PortableWindowInputEvent? LastInput { get; private set; }

        public object? LastFlushWindow { get; private set; }

        public List<string> FlushedPriorities { get; } = new List<string>();

        public List<TimeSpan?> FlushTimeouts { get; } = new List<TimeSpan?>();

        public bool ThrowOnDispatcherFlush { get; set; }

        public int DragDropCount { get; private set; }

        public object? LastDragDropWindow { get; private set; }

        public int LastDragDropKind { get; private set; }

        public string[] LastDragDropFiles { get; private set; } = Array.Empty<string>();

        public string? LastDragDropText { get; private set; }

        public double LastDragDropX { get; private set; }

        public double LastDragDropY { get; private set; }

        public int LastDragDropAllowedEffects { get; private set; }

        public int LastDragDropAcceptedEffect { get; private set; }

        public PortableWpfServiceKey ServiceKey
        {
            get
            {
                return PortableWpfServiceKey.PresentationFramework;
            }
        }

        public void Register(PortableWindowActivationCallbacks callbacks)
        {
            RegisterCount++;
            Callbacks = callbacks;
        }

        public bool TryRegisterMediaContextRenderService(
            object window,
            Action<object?, TimeSpan> requestRender,
            out IDisposable? registration)
        {
            MediaContextRenderRegisterCount++;
            LastMediaContextRenderWindow = window;
            RequestRender = requestRender;
            LastMediaContextRenderRegistration = new TestPortableServiceRegistration();
            registration = LastMediaContextRenderRegistration;
            return true;
        }

        public bool HandleMainWindowQuery { get; set; }

        public bool IsMainWindow { get; set; }

        public int MainWindowQueryCount { get; private set; }

        public object? LastMainWindowQueryWindow { get; private set; }

        public bool TryIsCurrentApplicationMainWindow(object window, out bool isMainWindow)
        {
            if (!HandleMainWindowQuery)
            {
                isMainWindow = false;
                return false;
            }

            MainWindowQueryCount++;
            LastMainWindowQueryWindow = window;
            isMainWindow = IsMainWindow;
            return true;
        }

        public bool HandleCloseWindow { get; set; }

        public PortableWindowCloseResult CloseWindowResult { get; set; }

        public int CloseWindowCount { get; private set; }

        public object? LastCloseWindow { get; private set; }

        public bool TryCloseWindow(object window, out PortableWindowCloseResult result)
        {
            if (!HandleCloseWindow)
            {
                result = PortableWindowCloseResult.NotInvoked;
                return false;
            }

            CloseWindowCount++;
            LastCloseWindow = window;
            result = CloseWindowResult;
            return true;
        }

        public bool TrySetActivationState(object window, bool isActive)
        {
            SetActivationStateCount++;
            LastActivationStateWindow = window;
            LastActivationState = isActive;
            if (!isActive && ThrowOnDeactivate)
            {
                throw new ArgumentException("Simulated capture-cancel layout failure.");
            }

            return true;
        }

        public bool TryBeginInvokeInput(object window, Action callback)
        {
            if (!QueueInputCallbacks)
            {
                return false;
            }

            BeginInvokeInputCount++;
            LastBeginInvokeInputWindow = window;
            LastBeginInvokeInputCallback = callback;
            InputDispatchLog.Add("BeginInput");
            return true;
        }

        public bool TryProcessInputEvent(object window, PortableWindowInputEvent input)
        {
            InputCount++;
            LastInputWindow = window;
            LastInput = input;
            InputDispatchLog.Add(
                "ProcessInput:" + input.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            input.Handled = true;
            return true;
        }

        public bool TryFlushDispatcherOperations(object window, string markerPriorityName, TimeSpan? timeout)
        {
            LastFlushWindow = window;
            FlushedPriorities.Add(markerPriorityName);
            InputDispatchLog.Add($"Flush:{markerPriorityName}");
            FlushTimeouts.Add(timeout);
            if (ThrowOnDispatcherFlush)
            {
                throw new InvalidOperationException("Cannot perform this operation while dispatcher processing is suspended.");
            }

            if (RunQueuedInputOnInputFlush &&
                string.Equals(markerPriorityName, "Input", StringComparison.Ordinal) &&
                LastBeginInvokeInputCallback is { } callback)
            {
                LastBeginInvokeInputCallback = null;
                callback();
            }

            return true;
        }

        public bool TryProcessDragDropEvent(
            object window,
            int dragDropEventKind,
            string[] files,
            string? text,
            double x,
            double y,
            int allowedEffects,
            int acceptedEffect,
            out int result)
        {
            DragDropCount++;
            LastDragDropWindow = window;
            LastDragDropKind = dragDropEventKind;
            LastDragDropFiles = files;
            LastDragDropText = text;
            LastDragDropX = x;
            LastDragDropY = y;
            LastDragDropAllowedEffects = allowedEffects;
            LastDragDropAcceptedEffect = acceptedEffect;
            result = (int)WpfDragDropEffects.Link;
            return true;
        }

        public void Clear()
        {
            Callbacks = null;
            LastMainWindowQueryWindow = null;
            LastCloseWindow = null;
            LastMediaContextRenderWindow = null;
            RequestRender = null;
            LastMediaContextRenderRegistration = null;
            LastActivationStateWindow = null;
            LastBeginInvokeInputWindow = null;
            LastBeginInvokeInputCallback = null;
            LastInputWindow = null;
            LastInput = null;
            LastFlushWindow = null;
            LastDragDropWindow = null;
            LastDragDropFiles = Array.Empty<string>();
            LastDragDropText = null;
            FlushedPriorities.Clear();
            FlushTimeouts.Clear();
            InputDispatchLog.Clear();
        }
    }

    private sealed class TestPortableServiceRegistration : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private enum FakeWindowState
    {
        Normal,
        Minimized,
        Maximized
    }

    private enum FakeResizeMode
    {
        NoResize,
        CanMinimize,
        CanResize,
        CanResizeWithGrip
    }

    private enum FakeWindowStyle
    {
        None,
        SingleBorderWindow,
        ThreeDBorderWindow,
        ToolWindow
    }

    private sealed class FakePortablePresentationSource : IPortablePresentationSourceHost
    {
        private object? _rootVisual;

        public event EventHandler? RenderRequested;

        event EventHandler? IPortablePresentationSourceHost.CursorRequested
        {
            add { }
            remove { }
        }

        public object CompositionTarget { get; } = new();

        public IntPtr Handle { get; set; }

        public object? RequestedCursor => null;

        public string? RequestedCursorName => null;

        public Func<double, double, object?>? HitTestOverride { get; set; }

        public Func<double, double, object?[]?>? HitTestAllOverride { get; set; }

        public PortableHitTestAllBufferOverride? HitTestAllBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestBoundsBufferOverride { get; set; }

        public Func<double, double, double, double, object?[]?>? HitTestEllipseBoundsOverride { get; set; }

        public PortableGeometryHitTestBufferOverride? HitTestEllipseBoundsBufferOverride { get; set; }

        public object? RootVisual
        {
            get => _rootVisual;
            set
            {
                _rootVisual = value;
                RenderRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        public double ClientWidth { get; private set; }

        public double ClientHeight { get; private set; }

        public int ClientSizeChangeCount { get; private set; }

        public double ClientOriginX { get; private set; }

        public double ClientOriginY { get; private set; }

        public List<(int Message, IntPtr WParam, IntPtr LParam)> DispatchedHwndSourceHooks { get; } = new();

        public Dictionary<int, (IntPtr Result, bool Handled)> HwndSourceHookResponses { get; } = new();

        public void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientSize(double width, double height)
        {
            ClientWidth = width;
            ClientHeight = height;
            ClientSizeChangeCount++;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetClientOrigin(double x, double y)
        {
            ClientOriginX = x;
            ClientOriginY = y;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool TryUpdateRootVisualClientSize(out double width, out double height)
        {
            width = ClientWidth;
            height = ClientHeight;
            return false;
        }

        public bool DispatchHwndSourceHook(int message, IntPtr wParam, IntPtr lParam, out IntPtr result, out bool handled)
        {
            DispatchedHwndSourceHooks.Add((message, wParam, lParam));
            if (HwndSourceHookResponses.TryGetValue(message, out var response))
            {
                result = response.Result;
                handled = response.Handled;
                return true;
            }

            result = IntPtr.Zero;
            handled = false;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestRenderScheduler : IWpfRenderScheduler
    {
        public event EventHandler? RenderRequested;

        public int RequestCount { get; private set; }

        public bool HasPendingRenderRequest { get; private set; }

        public void RequestRender()
        {
            RequestCount++;
            HasPendingRenderRequest = true;
            RenderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool ConsumeRenderRequest()
        {
            var hadPendingRequest = HasPendingRenderRequest;
            HasPendingRenderRequest = false;
            return hadPendingRequest;
        }

        public void Reset()
        {
            HasPendingRenderRequest = false;
        }
    }
}
