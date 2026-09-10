// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Input;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortablePresentationSourceTests
{
    [PortableScopeFact]
    public void DefaultAccessKeyScopeRequiresActualActivePortableRoot()
    {
        using IPortablePresentationSourceHost first = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost second = PortablePresentationSourceHost.Create();
        var firstRoot = new AccessKeyRoot();
        var secondRoot = new AccessKeyRoot();
        first.RootVisual = firstRoot;
        second.RootVisual = secondRoot;
        first.SetClientSize(200, 100);
        second.SetClientSize(200, 100);
        AccessKeyManager.Register("Q", firstRoot);
        AccessKeyManager.Register("Q", secondRoot);
        try
        {
            Assert.Null(AccessKeyManager.GetActivePresentationSource());
            secondRoot.Active = true;
            Assert.Same(second, AccessKeyManager.GetActivePresentationSource());
            AccessKeyManager.ProcessKey(null, "Q", false);
            Assert.Equal(0, firstRoot.Invocations);
            Assert.Equal(1, secondRoot.Invocations);
            firstRoot.Active = true;
            Assert.Null(AccessKeyManager.GetActivePresentationSource()); // Ambiguous publication.
            secondRoot.Active = false;
            Assert.Same(first, AccessKeyManager.GetActivePresentationSource());
            first.RootVisual = null;
            Assert.Null(AccessKeyManager.GetActivePresentationSource());
            first.RootVisual = firstRoot;
            first.Dispose();
            Assert.Null(AccessKeyManager.GetActivePresentationSource());
            second.RootVisual = new HitTestElement(); // No typed active-scope capability.
            Assert.Null(AccessKeyManager.GetActivePresentationSource());
        }
        finally
        {
            AccessKeyManager.Unregister("Q", firstRoot);
            AccessKeyManager.Unregister("Q", secondRoot);
        }
    }

    private sealed class PortableScopeFactAttribute : StaFactAttribute
    {
        public PortableScopeFactAttribute()
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input initialization, including on Windows.";
        }
    }

    private sealed class AccessKeyRoot : UIElement, IPortableAccessKeyScopeSource
    {
        internal bool Active { get; set; }
        internal int Invocations { get; private set; }
        public bool IsPortableAccessKeyScopeActive => Active;
        internal AccessKeyRoot()
        {
            AccessKeyManager.AddAccessKeyPressedHandler(this, (_, e) => e.Target = this);
        }
        protected override void OnAccessKey(AccessKeyEventArgs e) => Invocations++;
    }

    [Fact]
    public void FromVisualExposesPortableHandleThroughWin32WindowContract()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var root = new DrawingVisual();
        source.RootVisual = root;

        PresentationSource presentationSource = PresentationSource.FromVisual(root);
        var window = presentationSource.Should().BeAssignableTo<Interop.IWin32Window>().Subject;

        window.Handle.Should().Be(source.Handle);
        window.Handle.Should().NotBe(IntPtr.Zero);
    }

    [Fact]
    public void InitialDeviceScaleIsAppliedToRootVisual()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create(2.0, 1.5);
        var root = new DpiTrackingElement();

        source.RootVisual = root;
        source.SetClientSize(400.0, 300.0);

        DpiScale dpi = VisualTreeHelper.GetDpi(root);
        dpi.DpiScaleX.Should().BeApproximately(2.0, 0.000001);
        dpi.DpiScaleY.Should().BeApproximately(1.5, 0.000001);
        root.DpiChangedCount.Should().Be(1);
        root.MeasureCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void InitialDpiCallbackCanResolvePortablePresentationSource()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create(2.0, 1.5);
        var root = new DpiTrackingElement();

        source.RootVisual = root;

        var window = root.PresentationSourceAtDpiChange.Should()
            .BeAssignableTo<Interop.IWin32Window>().Subject;
        window.Handle.Should().Be(source.Handle);
    }

    [Fact]
    public void DeviceScaleChangeUpdatesVisualDpiLayoutAndHwndSource()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var portableSource = (PortablePresentationSource)(PresentationSource)source;
        var root = new DpiTrackingElement();
        int hwndDpiChangedCount = 0;
        portableSource.HwndSource.DpiChanged += (_, e) =>
        {
            hwndDpiChangedCount++;
            e.OldDpi.DpiScaleX.Should().BeApproximately(1.0, 0.000001);
            e.NewDpi.DpiScaleX.Should().BeApproximately(2.0, 0.000001);
        };
        source.RootVisual = root;
        source.SetClientSize(400.0, 300.0);
        int measureCountBeforeDpiChange = root.MeasureCount;

        source.SetDeviceScale(2.0, 1.5);

        CompositionTarget compositionTarget = ((PresentationSource)source).CompositionTarget;
        compositionTarget.TransformToDevice.M11.Should().BeApproximately(2.0, 0.000001);
        compositionTarget.TransformToDevice.M22.Should().BeApproximately(1.5, 0.000001);
        VisualTreeHelper.GetDpi(root).DpiScaleX.Should().BeApproximately(2.0, 0.000001);
        VisualTreeHelper.GetDpi(root).DpiScaleY.Should().BeApproximately(1.5, 0.000001);
        root.DpiChangedCount.Should().Be(1);
        root.MeasureCount.Should().BeGreaterThan(measureCountBeforeDpiChange);
        hwndDpiChangedCount.Should().Be(1);
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 2.0)]
    public void ClientOriginParticipatesInScreenConversions(double dpiScaleX, double dpiScaleY)
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create(dpiScaleX, dpiScaleY);
        var root = new DrawingVisual();
        source.RootVisual = root;
        source.SetClientSize(400.0, 300.0);
        source.SetClientOrigin(120.0, 80.0);

        Point screenPoint = root.PointToScreen(new Point(5.0, 7.0));

        screenPoint.X.Should().BeApproximately(125.0, 0.000001);
        screenPoint.Y.Should().BeApproximately(87.0, 0.000001);
        Point clientPoint = root.PointFromScreen(screenPoint);
        clientPoint.X.Should().BeApproximately(5.0, 0.000001);
        clientPoint.Y.Should().BeApproximately(7.0, 0.000001);
        CompositionTarget compositionTarget = ((PresentationSource)source).CompositionTarget;
        compositionTarget.TransformToDevice.M11.Should().BeApproximately(dpiScaleX, 0.000001);
        compositionTarget.TransformToDevice.M22.Should().BeApproximately(dpiScaleY, 0.000001);
    }

    [Theory]
    [InlineData(1.0, 1.0, 2.0, 2.0)]
    [InlineData(2.0, 2.0, 2.0, 2.0)]
    [InlineData(1.5, 2.0, 2.0, 1.5)]
    public void DesktopMappingIsIndependentOfFramebufferDpi(double sx, double sy, double dpiX, double dpiY)
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create(dpiX, dpiY);
        var geometry = (IPortableDesktopGeometryHost)source;
        var root = new DrawingVisual();
        source.RootVisual = root;
        geometry.SetDesktopTransform(new PortableDesktopTransform(-1920, 24, sx, sy));
        var client = new Point(8, 12);
        var expected = new Point(-1920 + 8 * sx, 24 + 12 * sy);
        root.PointToScreen(client).Should().Be(expected);
        root.PointFromScreen(expected).Should().Be(client);
        var portable = (PortablePresentationSource)source;
        MS.Internal.PointUtil.ClientToScreen(client, portable.HwndSource).Should().Be(expected);
        MS.Internal.PointUtil.ScreenToClient(expected, portable.HwndSource).Should().Be(client);

        source.SetDeviceScale(3, 3);
        root.PointToScreen(client).Should().Be(expected);
        source.SetClientOrigin(-1600, -100);
        geometry.DesktopTransform.ScaleX.Should().Be(sx);
        geometry.DesktopTransform.ScaleY.Should().Be(sy);
        root.PointToScreen(client).Should().Be(new Point(-1600 + 8 * sx, -100 + 12 * sy));
    }

    [Fact]
    public void InvalidDesktopGeometryDoesNotReplaceSourceState()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var geometry = (IPortableDesktopGeometryHost)source;
        var expected = new PortableDesktopTransform(-800, 100, 2, 2);
        geometry.SetDesktopTransform(expected);
        Assert.Throws<ArgumentException>(() => geometry.SetDesktopTransform(default));
        geometry.DesktopTransform.Should().Be(expected);
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => geometry.SetDesktopTransform(expected));
    }

    [Fact]
    public void NonFiniteClientOriginFallsBackToZero()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var root = new DrawingVisual();
        source.RootVisual = root;
        source.SetClientSize(100.0, 100.0);
        source.SetClientOrigin(double.NaN, double.PositiveInfinity);

        root.PointToScreen(new Point()).Should().Be(new Point());
    }

    [Fact]
    public void PortableDragSourceDoesNotInvokeOleServices()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var root = new HitTestElement();
        source.RootVisual = root;
        source.SetClientSize(200.0, 100.0);

        int startedCount = 0;
        int completedCount = 0;
        root.AddHandler(DragDrop.DragDropStartedEvent, new RoutedEventHandler((_, _) => startedCount++));
        root.AddHandler(DragDrop.DragDropCompletedEvent, new RoutedEventHandler((_, _) => completedCount++));

        DragDropEffects result = DragDrop.DoDragDrop(
            root,
            "portable text",
            DragDropEffects.Copy | DragDropEffects.Move);

        result.Should().Be(DragDropEffects.None);
        startedCount.Should().Be(1);
        completedCount.Should().Be(1);
    }

    [StaFact]
    public void ReleaseMouseCaptureReportsOnlyCancelCaptureWithoutMovingThePointer()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var presentationSource = (PresentationSource)source;
        var root = new HitTestElement();
        source.RootVisual = root;
        source.SetClientSize(200.0, 100.0);

        ReportMouseInput(
            presentationSource,
            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
            x: 37,
            y: 41);

        root.CaptureMouse().Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(root);

        var releaseReports = new List<RawMouseInputReport>();
        int mouseMoveCount = 0;
        int lostMouseCaptureCount = 0;
        PreProcessInputEventHandler inputHandler = (_, e) =>
        {
            if (e.StagingItem.Input is InputReportEventArgs inputReport &&
                inputReport.Report is RawMouseInputReport mouseReport &&
                ReferenceEquals(mouseReport.InputSource, presentationSource))
            {
                releaseReports.Add(mouseReport);
            }
        };

        root.MouseMove += (_, _) => mouseMoveCount++;
        root.LostMouseCapture += (_, _) => lostMouseCaptureCount++;
        InputManager.Current.PreProcessInput += inputHandler;
        try
        {
            root.ReleaseMouseCapture();
        }
        finally
        {
            InputManager.Current.PreProcessInput -= inputHandler;
        }

        releaseReports.Should().Contain(report =>
            report.Actions == RawMouseActions.CancelCapture);
        releaseReports.Should().NotContain(report =>
            (report.Actions & RawMouseActions.Activate) != 0);
        releaseReports.Should().NotContain(report =>
            (report.Actions & RawMouseActions.AbsoluteMove) != 0 &&
            report.X == 0 &&
            report.Y == 0);
        mouseMoveCount.Should().Be(0);
        lostMouseCaptureCount.Should().Be(1);
        Mouse.Captured.Should().BeNull();
        root.IsMouseCaptured.Should().BeFalse();
    }

    [StaFact]
    public void DeactivatingPortableSourceClearsCaptureAfterActiveSourceChanges()
    {
        using IPortablePresentationSourceHost firstSource = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost secondSource = PortablePresentationSourceHost.Create();
        var firstPresentationSource = (PresentationSource)firstSource;
        var secondPresentationSource = (PresentationSource)secondSource;
        var firstRoot = new HitTestElement();
        var secondRoot = new HitTestElement();
        firstSource.RootVisual = firstRoot;
        secondSource.RootVisual = secondRoot;
        firstSource.SetClientSize(200.0, 100.0);
        secondSource.SetClientSize(200.0, 100.0);

        ReportMouseInput(
            firstPresentationSource,
            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
            x: 37,
            y: 41);
        Mouse.Capture(firstRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(firstRoot);

        var firstSourceReports = new List<RawMouseInputReport>();
        int lostMouseCaptureCount = 0;
        PreProcessInputEventHandler inputHandler = (_, e) =>
        {
            if (e.StagingItem.Input is InputReportEventArgs inputReport &&
                inputReport.Report is RawMouseInputReport mouseReport &&
                ReferenceEquals(mouseReport.InputSource, firstPresentationSource))
            {
                firstSourceReports.Add(mouseReport);
            }
        };
        firstRoot.LostMouseCapture += (_, _) => lostMouseCaptureCount++;
        InputManager.Current.PreProcessInput += inputHandler;
        try
        {
            ReportMouseInput(
                secondPresentationSource,
                RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
                x: 37,
                y: 41);
        }
        finally
        {
            InputManager.Current.PreProcessInput -= inputHandler;
        }

        Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(secondPresentationSource);
        Mouse.Captured.Should().BeNull();
        firstRoot.IsMouseCaptured.Should().BeFalse();
        lostMouseCaptureCount.Should().Be(1);
        firstSourceReports.Should().Contain(report => report.Actions == RawMouseActions.CancelCapture);
        firstSourceReports.Should().NotContain(report => (report.Actions & RawMouseActions.Activate) != 0);
    }

    [StaFact]
    public void CancelCaptureFromDifferentProviderDoesNotClearCapture()
    {
        using IPortablePresentationSourceHost firstSource = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost secondSource = PortablePresentationSourceHost.Create();
        var firstPresentationSource = (PresentationSource)firstSource;
        var secondPresentationSource = (PresentationSource)secondSource;
        var firstRoot = new HitTestElement();
        var secondRoot = new HitTestElement();
        firstSource.RootVisual = firstRoot;
        secondSource.RootVisual = secondRoot;
        firstSource.SetClientSize(200.0, 100.0);
        secondSource.SetClientSize(200.0, 100.0);

        ReportMouseInput(
            firstPresentationSource,
            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
            x: 37,
            y: 41);
        Mouse.Capture(firstRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(firstRoot);

        int lostMouseCaptureCount = 0;
        firstRoot.LostMouseCapture += (_, _) => lostMouseCaptureCount++;
        try
        {
            ReportMouseInput(
                secondPresentationSource,
                RawMouseActions.CancelCapture,
                x: 0,
                y: 0);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(firstPresentationSource);
            Mouse.Captured.Should().BeSameAs(firstRoot);
            firstRoot.IsMouseCaptured.Should().BeTrue();
            lostMouseCaptureCount.Should().Be(0);
        }
        finally
        {
            firstRoot.ReleaseMouseCapture();
        }
    }

    private static void ReportMouseInput(
        PresentationSource source,
        RawMouseActions actions,
        int x,
        int y)
    {
        var report = new RawMouseInputReport(
            InputMode.Foreground,
            Environment.TickCount,
            source,
            actions,
            x,
            y,
            wheel: 0,
            extraInformation: IntPtr.Zero);
        var input = new InputReportEventArgs(inputDevice: null, report: report)
        {
            RoutedEvent = InputManager.PreviewInputReportEvent
        };

        InputManager.Current.ProcessInput(input);
    }

    private sealed class HitTestElement : UIElement
    {
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }
    }

    private sealed class DpiTrackingElement : UIElement
    {
        internal int DpiChangedCount { get; private set; }

        internal int MeasureCount { get; private set; }

        internal PresentationSource PresentationSourceAtDpiChange { get; private set; }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            DpiChangedCount++;
            PresentationSourceAtDpiChange = PresentationSource.FromVisual(this);
            base.OnDpiChanged(oldDpi, newDpi);
        }

        protected override Size MeasureCore(Size availableSize)
        {
            MeasureCount++;
            return new Size(100.0, 50.0);
        }
    }
}
