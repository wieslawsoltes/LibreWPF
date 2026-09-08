// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableWindowActivationServiceTests
{
    private const int MouseMoveInputKind = 3;
    private const int MouseDownInputKind = 4;
    private const int MouseUpInputKind = 5;
    private const int LeftMouseButton = 1;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomChromeUsesPortableOwnerBeforeAndAfterSourceCreation(bool attachBeforeSource)
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            var borders = new List<WindowStyle>();
            PortableWindowActivationService.Register(
                activate: _ => activation, createHidden: _ => activation,
                getHandle: _ => new IntPtr(5678),
                setWindowBorder: (owner, _, style) =>
                {
                    owner.Should().BeSameAs(activation);
                    borders.Add((WindowStyle)style);
                });
            var window = new Window { Width = 200, Height = 100, WindowStyle = WindowStyle.SingleBorderWindow };
            var chrome = new Shell.WindowChrome { CaptionHeight = 32, GlassFrameThickness = new Thickness(0) };
            try
            {
                if (attachBeforeSource) Shell.WindowChrome.SetWindowChrome(window, chrome);
                new WindowInteropHelper(window).EnsureHandle().Should().Be(new IntPtr(5678));
                if (!attachBeforeSource) Shell.WindowChrome.SetWindowChrome(window, chrome);
                var source = (IPortableWindowStateSource)window;
                source.TryGetPortableWindowState(out var custom).Should().BeTrue();
                custom.WindowStyle.Should().Be((int)WindowStyle.None);
                chrome.CaptionHeight = 40;
                Shell.WindowChrome.SetWindowChrome(window, null);
                source.TryGetPortableWindowState(out var standard).Should().BeTrue();
                standard.WindowStyle.Should().Be((int)WindowStyle.SingleBorderWindow);
                borders.Should().Contain(WindowStyle.SingleBorderWindow);
                if (!attachBeforeSource) borders.Should().Contain(WindowStyle.None);
                window.WindowStyle.Should().Be(WindowStyle.SingleBorderWindow);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureHandleCreatesOneHiddenSourceAndReusesItForShow(bool showAfterCreation)
    {
        RunInUiApartment(() =>
        {
            var activation = new object();
            int hiddenCreates = 0, ordinaryCreates = 0, shows = 0, closes = 0, disposals = 0;
            PortableWindowActivationService.Register(
                activate: _ => { ordinaryCreates++; return new object(); },
                createHidden: _ => { hiddenCreates++; return activation; },
                getHandle: value => ReferenceEquals(value, activation) ? new IntPtr(5678) : IntPtr.Zero,
                show: value => { value.Should().BeSameAs(activation); shows++; },
                close: _ => closes++,
                dispose: _ => disposals++);
            var window = new Window { Width = 200, Height = 100 };
            var interop = new WindowInteropHelper(window);
            int initialized = 0;
            window.SourceInitialized += (_, _) =>
            {
                initialized++;
                interop.Handle.Should().Be(new IntPtr(5678));
                window.IsVisible.Should().BeFalse();
                // Event reentrancy must see the published identity, not create a second source.
                interop.EnsureHandle().Should().Be(interop.Handle);
            };
            try
            {
                interop.Handle.Should().Be(IntPtr.Zero);
                Visibility originalVisibility = window.Visibility;
                interop.EnsureHandle().Should().Be(new IntPtr(5678));
                interop.EnsureHandle().Should().Be(new IntPtr(5678));
                window.Visibility.Should().Be(originalVisibility);
                window.IsVisible.Should().BeFalse();
                window.IsActive.Should().BeFalse();
                shows.Should().Be(0);
                if (showAfterCreation)
                {
                    window.Show();
                    interop.EnsureHandle().Should().Be(new IntPtr(5678));
                    window.Hide();
                    window.Show();
                    shows.Should().Be(2);
                }
                hiddenCreates.Should().Be(1);
                ordinaryCreates.Should().Be(0);
                initialized.Should().Be(1);
                window.Close();
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Theory]
    [InlineData(0)] // Legacy callback set has no hidden creation capability.
    [InlineData(1)] // Registered hidden factory rejects this window.
    [InlineData(2)] // Factory returns a source without a usable identity.
    public void EnsureHandleFailsClosedForUnavailableHiddenSources(int failure)
    {
        RunInUiApartment(() =>
        {
            int ordinaryCreates = 0, closes = 0, disposals = 0, initialized = 0;
            PortableWindowActivationService.Register(
                activate: _ => { ordinaryCreates++; return new object(); },
                createHidden: failure == 0 ? null : _ => failure == 1 ? null! : new object(),
                getHandle: _ => IntPtr.Zero,
                close: _ => closes++, dispose: _ => disposals++);
            var window = new Window();
            window.SourceInitialized += (_, _) => initialized++;
            var interop = new WindowInteropHelper(window);
            try
            {
                Action ensure = () => interop.EnsureHandle();
                if (failure == 0)
                    ensure.Should().Throw<PlatformNotSupportedException>().WithMessage("*hidden window sources*");
                else
                    ensure.Should().Throw<InvalidOperationException>();
                ordinaryCreates.Should().Be(0);
                initialized.Should().Be(0);
                interop.Handle.Should().Be(IntPtr.Zero);
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(failure == 2 ? 1 : 0);
                disposals.Should().Be(failure == 2 ? 1 : 0);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ExplicitRegistrationRoutesWindowLifecycleOnEveryPlatform()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Clear();
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
            var activation = new object();
            int creates = 0, shows = 0, hides = 0, requests = 0, runs = 0, closes = 0, disposals = 0;
            string? title = null;
            PortableWindowActivationService.Register(
                activate: _ => { creates++; return activation; },
                show: value => { value.Should().BeSameAs(activation); shows++; },
                hide: value => { value.Should().BeSameAs(activation); hides++; },
                setTitle: (_, value) => title = value,
                close: _ => closes++,
                run: _ => runs++,
                dispose: _ => disposals++,
                getHandle: _ => new IntPtr(1234),
                requestActivation: _ => { requests++; return true; });
            var window = new Window { Width = 200, Height = 100 };
            int initialized = 0;
            window.SourceInitialized += (_, _) => initialized++;
            try
            {
                PortableWindowActivationService.IsEnabled.Should().BeTrue();
                window.Show();
                window.PortableWindowActivation.Should().BeSameAs(activation);
                window.Title = "Portable title";
                title.Should().Be("Portable title");
                window.Activate().Should().BeTrue();
                PortableWindowActivationService.GetHandle(activation).Should().Be(new IntPtr(1234));
                PortableWindowActivationService.TryRun(window).Should().BeTrue();
                window.Hide();
                window.Show();
                creates.Should().Be(1);
                initialized.Should().Be(1);
                shows.Should().Be(2);
                hides.Should().Be(1);
                requests.Should().Be(1);
                runs.Should().Be(1);
                window.Close();
                window.PortableWindowActivation.Should().BeNull();
                closes.Should().Be(1);
                disposals.Should().Be(1);
            }
            finally
            {
                if (!window.IsDisposed)
                {
                    window.Close();
                }
                PortableWindowActivationService.Clear();
            }
            PortableWindowActivationService.IsEnabled.Should().BeFalse();
        });
    }

    [Fact]
    public void RegisteredHostRejectionDoesNotCreateAWindowsMilWindow()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(activate: _ => null!);
            var window = new Window();
            int initialized = 0;
            window.SourceInitialized += (_, _) => initialized++;
            try
            {
                Action show = window.Show;
                show.Should().Throw<InvalidOperationException>()
                    .WithMessage("*Falling back to Windows MIL is not permitted*");
                initialized.Should().Be(0);
                window.PortableWindowActivation.Should().BeNull();
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void ActivePortableWindowRequiresItsHostRunLoop()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(activate: _ => new object());
            var window = new Window { Width = 200, Height = 100 };
            try
            {
                window.Show();
                Action run = () => PortableWindowActivationService.TryRun(window);
                run.Should().Throw<InvalidOperationException>()
                    .WithMessage("*no run-loop callback*");
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [Fact]
    public void RejectedActivationRequestDoesNotFabricateActiveState()
    {
        RunInUiApartment(() =>
        {
            PortableWindowActivationService.Register(
                activate: _ => new object(), requestActivation: _ => false);
            var window = new Window { Width = 200, Height = 100 };
            int activations = 0;
            window.Activated += (_, _) => activations++;
            try
            {
                window.Show();
                window.Activate().Should().BeFalse();
                window.IsActive.Should().BeFalse();
                activations.Should().Be(0);
                PortableWindowActivationService.SetActivationState(window, true);
                window.IsActive.Should().BeTrue();
                activations.Should().Be(1);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

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
