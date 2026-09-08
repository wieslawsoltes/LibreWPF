// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortablePopupOwnershipTests
{
    [Theory]
    [InlineData(1.0, 1.0, 2.0, 2.0)]
    [InlineData(2.0, 2.0, 2.0, 2.0)]
    [InlineData(1.5, 2.0, 2.0, 1.5)]
    public void PopupExtentsAndOffsetsUseDesktopScaleNotFramebufferDpi(double sx, double sy, double dpiX, double dpiY)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border();
            owner.RootVisual = target;
            var service = new PopupService(owner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(0, 0, target, true, null!, null!, null!);
                service.Source!.SetDeviceScale(dpiX, dpiY);
                ((IPortableDesktopGeometryHost)service.Source).SetDesktopTransform(
                    new PortableDesktopTransform(-1920, 24, sx, sy));
                var clientSize = new Size(100, 80);
                var screenSize = new Size(100 * sx, 80 * sy);
                helper.ClientSizeToScreen(clientSize).Should().Be(screenSize);
                helper.ScreenSizeToClient(screenSize).Should().Be(clientSize);
                helper.ClientOffsetToScreen(new Point(-5, 7)).Should().Be(new Point(-5 * sx, 7 * sy));

                service.Source.SetDeviceScale(3, 3);
                helper.ClientSizeToScreen(clientSize).Should().Be(screenSize);
                service.Source.SetClientOrigin(2560, -1440);
                helper.ClientSizeToScreen(clientSize).Should().Be(screenSize);
                helper.ScreenSizeToClient(screenSize).Should().Be(clientSize);
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableOwnerRoutesLifecycleIncludingAlreadyDisposedSources(bool disposeBeforeDestroy)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border { Width = 200, Height = 100 };
            owner.RootVisual = target;
            owner.SetClientSize(200, 100);
            var service = new PopupService(owner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                Popup.PopupSecurityHelper.RequiresPortableWindow(target).Should().BeTrue();
                PopupControlService.UsesNativeWindowing((PresentationSource)owner).Should().BeFalse();
                PopupControlService.HasNativeMouseCapture((PresentationSource)owner).Should().BeFalse();
                helper.BuildWindow(10, 20, target, true, null!, null!, null!);
                helper.IsPortable.Should().BeTrue();
                helper.IsWindowAlive().Should().BeTrue();
                service.Request!.OwnerHandle.Should().Be(owner.Handle);
                helper.SetWindowRootVisual(new Border { Width = 40, Height = 30 });
                helper.SetPopupPos(true, 25, 35, true, 50, 60);
                helper.ShowWindow();
                helper.SetHitTestable(false);
                helper.HideWindow();
                service.Positions.Should().Be(1);
                service.Sizes.Should().BeGreaterThan(0);
                service.Shows.Should().Be(1);
                service.Hides.Should().Be(1);
                service.HitTestChanges.Should().Be(1);
                if (disposeBeforeDestroy) service.Source!.Dispose();
                helper.CanDestroyWindow().Should().BeTrue();
                helper.DestroyWindow(null!, null!, null!);
                helper.HasWindowReference().Should().BeFalse();
                service.Destroys.Should().Be(1);
                ((PresentationSource)service.Source!).IsDisposed.Should().BeTrue();
                helper.DestroyWindow(null!, null!, null!);
                service.Destroys.Should().Be(1);
            }
            finally
            {
                helper.DestroyWindow(null!, null!, null!);
                service.Source?.Dispose();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedOrInvalidHostCannotCreateAnUnhostedPopup(bool invalidSource)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border();
            owner.RootVisual = target;
            var service = new PopupService(owner) { Reject = !invalidSource, InvalidSource = invalidSource };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            Action create = () => helper.BuildWindow(0, 0, target, true, null!, null!, null!);
            create.Should().Throw<PlatformNotSupportedException>().WithMessage("*No portable popup host accepted*");
            helper.IsWindowAlive().Should().BeFalse();
            helper.HasWindowReference().Should().BeFalse();
            service.Destroys.Should().Be(invalidSource ? 1 : 0);
        });
    }

    [Fact]
    public void DestroyFailureStillDisposesTheSourceAndReleasesHelperOwnership()
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border();
            owner.RootVisual = target;
            var service = new PopupService(owner) { ThrowOnDestroy = true };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            helper.BuildWindow(0, 0, target, true, null!, null!, null!);
            Action destroy = () => helper.DestroyWindow(null!, null!, null!);
            destroy.Should().Throw<InvalidOperationException>().WithMessage("Host destroy failure");
            helper.HasWindowReference().Should().BeFalse();
            ((PresentationSource)service.Source!).IsDisposed.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativePlacementUsesWorkAreaOnlyForPreferredAnchorsInsideIt(bool preferWorkArea, bool outsideWorkArea)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border { Width = 100, Height = 80 };
            owner.RootVisual = target;
            var screen = new PortableRect(-1920, 0, 1920, 1080);
            var work = new PortableRect(-1920, 24, 1920, 1056);
            var service = new PopupService(owner)
            {
                PlacementBounds = new(PortablePopupPlacementBoundsKind.NativeScreen, screen, work)
            };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(0, 0, target, true, null!, null!, null!);
                var query = new Rect(-30, 20, 20, 30);
                var result = helper.GetPortablePlacementBounds(query,
                    new Point(-10, outsideWorkArea ? 10 : 50), preferWorkArea);
                result.Should().Be(preferWorkArea && !outsideWorkArea
                    ? new Rect(-1920, 24, 1920, 1056) : new Rect(-1920, 0, 1920, 1080));
                service.LastPlacementTarget.Should().Be(new PortableRect(-30, 20, 20, 30));
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrInvalidNativePlacementFailsInsteadOfConfiningToOwner(bool invalid)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border();
            owner.RootVisual = target;
            var service = new PopupService(owner)
            {
                RejectBounds = !invalid,
                PlacementBounds = new(PortablePopupPlacementBoundsKind.NativeScreen,
                    new(0, 0, 100, 100), new(-1, 0, 100, 100))
            };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(0, 0, target, true, null!, null!, null!);
                Action query = () => helper.GetPortablePlacementBounds(new Rect(0, 0, 10, 10), new Point(), false);
                if (invalid) query.Should().Throw<InvalidOperationException>();
                else query.Should().Throw<PlatformNotSupportedException>();
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }

    [Fact]
    public void OwnerSurfacePlacementUsesTheRealLaidOutOwner()
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var target = new Border { Width = 200, Height = 100 };
            owner.RootVisual = target;
            owner.SetClientSize(200, 100);
            target.Measure(new Size(200, 100));
            target.Arrange(new Rect(0, 0, 200, 100));
            var service = new PopupService(owner)
            {
                PlacementBounds = new(PortablePopupPlacementBoundsKind.OwnerSurface, PortableRect.Empty, PortableRect.Empty)
            };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(0, 0, target, true, null!, null!, null!);
                helper.GetPortablePlacementBounds(new Rect(190, 90, 50, 50), new Point(), true)
                    .Should().Be(helper.GetParentWindowRect());
                helper.GetParentWindowRect().IsEmpty.Should().BeFalse();
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }

    private sealed class PopupService(IPortablePresentationSourceHost owner) : IPortablePopupServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;
        public PortablePopupCreateRequest? Request { get; private set; }
        public IPortablePresentationSourceHost? Source { get; private set; }
        public bool Reject { get; init; }
        public bool InvalidSource { get; init; }
        public bool ThrowOnDestroy { get; init; }
        public bool RejectBounds { get; init; }
        public PortablePopupPlacementBounds PlacementBounds { get; init; }
        public PortableRect LastPlacementTarget { get; private set; }
        public int Positions, Sizes, Shows, Hides, HitTestChanges, Destroys;
        private object? _identity;

        public bool TryGetPopupPlacementBounds(object source, PortableRect target, out PortablePopupPlacementBounds bounds)
        {
            LastPlacementTarget = target;
            bounds = PlacementBounds;
            return !RejectBounds && ReferenceEquals(source, _identity);
        }

        public bool TryCreatePopup(PortablePopupCreateRequest request, out object? source)
        {
            source = null;
            if (request.OwnerHandle != owner.Handle || Reject) return false;
            Request = request;
            source = InvalidSource ? new object() : Source = PortablePresentationSourceHost.Create();
            _identity = source;
            return true;
        }

        public bool TrySetPopupPosition(object source, int x, int y) { Positions++; return ReferenceEquals(source, _identity); }
        public bool TrySetPopupSize(object source, int width, int height) { Sizes++; return ReferenceEquals(source, _identity); }
        public bool TryShowPopup(object source) { Shows++; return ReferenceEquals(source, _identity); }
        public bool TryHidePopup(object source) { Hides++; return ReferenceEquals(source, _identity); }
        public bool TrySetPopupHitTestable(object source, bool value) { HitTestChanges++; return ReferenceEquals(source, _identity); }
        public bool TryDestroyPopup(object source)
        {
            if (!ReferenceEquals(source, _identity)) return false;
            Destroys++;
            if (ThrowOnDestroy) throw new InvalidOperationException("Host destroy failure");
            Source?.Dispose();
            return true;
        }
        public void Clear() => Source?.Dispose();
    }

    private static void RunInUiApartment(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Popup ownership fixture timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
