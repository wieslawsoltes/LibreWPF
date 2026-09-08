// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableWindowActivationServiceTests
{
    private const int MouseMoveInputKind = 3;
    private const int MouseDownInputKind = 4;
    private const int MouseUpInputKind = 5;
    private const int LeftMouseButton = 1;

    [Fact]
    public void CapturedElementReceivesMouseInputReportedByAnotherPresentationSource()
    {
        RunInUiApartment(VerifyCapturedElementReceivesMouseInputReportedByAnotherPresentationSource);
    }

    [Fact]
    public void SubtreeCapturePreservesReportedPresentationSourceAndCapture()
    {
        RunInUiApartment(VerifySubtreeCapturePreservesReportedPresentationSourceAndCapture);
    }

    [Fact]
    public void DispatcherIdleWorkNotificationExcludesRegularlyPumpedPrioritiesAndUnsubscribes()
    {
        RunInUiApartment(VerifyDispatcherIdleWorkNotification);
    }

    private static void VerifyDispatcherIdleWorkNotification()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();
        var window = new Window();
        int notificationCount = 0;

        activationService.TryRegisterDispatcherIdleWorkNotification(
            window,
            () => notificationCount++,
            out IDisposable? registration).Should().BeTrue();
        registration.Should().NotBeNull();

        DispatcherOperation background = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            static () => { });
        DispatcherOperation idle = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            static () => { });

        notificationCount.Should().Be(1);
        registration!.Dispose();
        DispatcherOperation afterDispose = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            static () => { });
        notificationCount.Should().Be(1);

        background.Abort();
        idle.Abort();
        afterDispose.Abort();
    }

    private static void VerifyCapturedElementReceivesMouseInputReportedByAnotherPresentationSource()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();

        using IPortablePresentationSourceHost captureSourceHost = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost reportedSourceHost = PortablePresentationSourceHost.Create();
        var captureSource = (PresentationSource)captureSourceHost;
        var reportedSource = (PresentationSource)reportedSourceHost;
        var captureRoot = new HitTestElement();
        var reportedRoot = new HitTestElement();
        captureSourceHost.RootVisual = captureRoot;
        reportedSourceHost.RootVisual = reportedRoot;
        captureSourceHost.SetClientSize(500.0, 500.0);
        reportedSourceHost.SetClientSize(500.0, 500.0);
        captureSourceHost.SetClientOrigin(100.0, 200.0);
        reportedSourceHost.SetClientOrigin(400.0, 500.0);

        ProcessInput(
            activationService,
            captureSource,
            MouseDownInputKind,
            x: 20.0,
            y: 30.0,
            button: LeftMouseButton);
        captureRoot.CaptureMouse().Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(captureRoot);

        int capturedMoveCount = 0;
        int capturedUpCount = 0;
        int reportedMoveCount = 0;
        int reportedUpCount = 0;
        int lostCaptureCount = 0;
        Point capturedMovePoint = default;
        captureRoot.MouseMove += (_, e) =>
        {
            capturedMoveCount++;
            capturedMovePoint = e.GetPosition(captureRoot);
        };
        captureRoot.MouseUp += (_, _) => capturedUpCount++;
        captureRoot.LostMouseCapture += (_, _) => lostCaptureCount++;
        reportedRoot.MouseMove += (_, _) => reportedMoveCount++;
        reportedRoot.MouseUp += (_, _) => reportedUpCount++;
        try
        {
            ProcessInput(
                activationService,
                reportedSource,
                MouseMoveInputKind,
                x: 5.0,
                y: 7.0);
            ProcessInput(
                activationService,
                reportedSource,
                MouseUpInputKind,
                x: 5.0,
                y: 7.0,
                button: LeftMouseButton);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(captureSource);
            Mouse.Captured.Should().BeSameAs(captureRoot);
            capturedMoveCount.Should().Be(1);
            capturedUpCount.Should().Be(1);
            capturedMovePoint.X.Should().BeApproximately(305.0, 0.000001);
            capturedMovePoint.Y.Should().BeApproximately(307.0, 0.000001);
            reportedMoveCount.Should().Be(0);
            reportedUpCount.Should().Be(0);
            lostCaptureCount.Should().Be(0);
        }
        finally
        {
            captureRoot.ReleaseMouseCapture();
        }

        Mouse.Captured.Should().BeNull();
        lostCaptureCount.Should().Be(1);
    }

    private static void VerifySubtreeCapturePreservesReportedPresentationSourceAndCapture()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Application).Module.ModuleHandle);
        PortableWpfServiceRegistry.TryGetWindowActivationService(
            PortableWpfServiceKey.PresentationFramework,
            out IPortableWindowActivationServiceRegistrar activationService).Should().BeTrue();

        using IPortablePresentationSourceHost captureSourceHost = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost reportedSourceHost = PortablePresentationSourceHost.Create();
        var captureSource = (PresentationSource)captureSourceHost;
        var reportedSource = (PresentationSource)reportedSourceHost;
        var captureRoot = new HitTestElement();
        var reportedRoot = new HitTestElement(captureRoot);
        captureSourceHost.RootVisual = captureRoot;
        reportedSourceHost.RootVisual = reportedRoot;
        captureSourceHost.SetClientSize(500.0, 500.0);
        reportedSourceHost.SetClientSize(500.0, 500.0);

        ProcessInput(
            activationService,
            captureSource,
            MouseMoveInputKind,
            x: 20.0,
            y: 30.0);
        Mouse.Capture(captureRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(captureRoot);

        int reportedMoveCount = 0;
        object? originalSource = null;
        reportedRoot.MouseMove += (_, e) =>
        {
            reportedMoveCount++;
            originalSource = e.OriginalSource;
        };
        try
        {
            ProcessInput(
                activationService,
                reportedSource,
                MouseMoveInputKind,
                x: 5.0,
                y: 7.0);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(reportedSource);
            Mouse.Captured.Should().BeSameAs(captureRoot);
            reportedMoveCount.Should().Be(1);
            originalSource.Should().BeSameAs(reportedRoot);
        }
        finally
        {
            Mouse.Capture(null);
        }

        Mouse.Captured.Should().BeNull();
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        })
        {
            IsBackground = true
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Portable WPF input test did not complete within 30 seconds.");
        }

        if (exception != null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static void ProcessInput(
        IPortableWindowActivationServiceRegistrar activationService,
        PresentationSource source,
        int kind,
        double x,
        double y,
        int button = 0)
    {
        activationService.TryProcessPresentationSourceInputEvent(
            source,
            new PortableWindowInputEvent(kind, x: x, y: y, button: button)).Should().BeTrue();
    }

    private sealed class HitTestElement : UIElement
    {
        private readonly DependencyObject? _uiParent;

        public HitTestElement(DependencyObject? uiParent = null)
        {
            _uiParent = uiParent;
        }

        protected override DependencyObject GetUIParentCore()
        {
            return _uiParent ?? base.GetUIParentCore();
        }

        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }
    }
}
