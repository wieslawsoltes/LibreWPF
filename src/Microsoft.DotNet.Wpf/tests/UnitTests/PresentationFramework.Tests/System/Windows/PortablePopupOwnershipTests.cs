// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortablePopupOwnershipTests
{
    [PortablePopupFact]
    public void OwnerlessPopupRetainsTheActualActiveOwnerWithoutChangingPlacementTarget()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create(2, 1.5);
            var firstRoot = new ActivePopupRoot();
            var secondRoot = new ActivePopupRoot { Active = true };
            first.RootVisual = firstRoot;
            second.RootVisual = secondRoot;
            second.SetClientOrigin(120, 80);
            var service = new PopupService(second);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(400, 300, null!, true, null!, null!, null!);
                helper.PortableInputOwnerSource.Should().BeSameAs(second);
                service.Request!.PlacementTarget.Should().BeNull();
                service.Request.OwnerPresentationSource.Should().BeSameAs(second);
                service.Request.OwnerHandle.Should().Be(second.Handle);
                service.Request.PopupScreenDeviceX.Should().Be(800);
                service.Request.PopupScreenDeviceY.Should().Be(450);
                service.Request.OwnerClientScreenDeviceX.Should().Be(240);
                service.Request.OwnerClientScreenDeviceY.Should().Be(120);

                firstRoot.Active = true;
                secondRoot.Active = false;
                helper.PortableInputOwnerSource.Should().BeSameAs(second);
                helper.DestroyWindow(null!, null!, null!);
                helper.PortableInputOwnerSource.Should().BeNull();
                service.Destroys.Should().Be(1);

                var nextService = new PopupService(first);
                using var nextRegistration = PortableWpfServiceRegistry.RegisterPopupService(nextService);
                helper.BuildWindow(400, 300, null!, true, null!, null!, null!);
                helper.PortableInputOwnerSource.Should().BeSameAs(first);
                nextService.Request!.PlacementTarget.Should().BeNull();
                helper.DestroyWindow(null!, null!, null!);
                nextService.Destroys.Should().Be(1);
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }

    [PortablePopupTheory]
    [InlineData(PlacementMode.Bottom)]
    [InlineData(PlacementMode.Absolute)]
    public void UnattachedPublicPopupUsesActiveWindowAndPreservesScreenOffsets(PlacementMode placement)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var window = new Window { Width = 800, Height = 600 };
            PortableWindowActivationService.Register(activate: value => value, getHandle: _ => owner.Handle);
            var service = new PopupService(owner)
            {
                PlacementBounds = new(PortablePopupPlacementBoundsKind.NativeScreen,
                    new(0, 0, 1920, 1080), new(0, 0, 1920, 1080))
            };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var popup = new Popup
            {
                Child = new Border { Width = 100, Height = 40 },
                HorizontalOffset = 400,
                VerticalOffset = 300,
                Placement = placement
            };
            try
            {
                window.Show();
                owner.RootVisual = window;
                owner.SetClientSize(800, 600);
                owner.SetClientOrigin(120, 80);
                PortableWindowActivationService.SetActivationState(window, true);

                popup.IsOpen = true;

                popup.IsOpen.Should().BeTrue();
                popup.PlacementTarget.Should().BeNull();
                service.Request!.PlacementTarget.Should().BeNull();
                service.Request.OwnerPresentationSource.Should().BeSameAs(owner);
                service.Shows.Should().Be(1);
                service.LastPosition.Should().Be(new Point(400, 300));
                popup.Child.RenderSize.Should().Be(new Size(100, 40));
            }
            finally
            {
                popup.IsOpen = false;
                // Closing a public Popup schedules source destruction at Input priority.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                service.Source?.Dispose();
                owner.RootVisual = null;
                window.Close();
                PortableWindowActivationService.Clear();
            }
            service.Destroys.Should().Be(1);
        });
    }

    [PortablePopupTheory]
    [InlineData("inactive")]
    [InlineData("hidden")]
    [InlineData("modal-blocked")]
    [InlineData("detached")]
    [InlineData("disposed-source")]
    [InlineData("closed-window")]
    public void OwnerlessPopupRejectsUnavailableOrIneligibleWindows(string state)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var window = new Window { Width = 200, Height = 100 };
            PortableWindowActivationService.Register(activate: value => value, getHandle: _ => owner.Handle);
            var service = new PopupService(owner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            IDisposable? modalScope = null;
            try
            {
                window.Show();
                owner.RootVisual = window;
                owner.SetClientSize(200, 100);
                PortableWindowActivationService.SetActivationState(window, state != "inactive");
                switch (state)
                {
                    case "hidden": window.Hide(); break;
                    case "modal-blocked": modalScope = PortableModalInputScope.Enter(new object()); break;
                    case "detached": owner.RootVisual = null; break;
                    case "disposed-source": owner.Dispose(); break;
                    case "closed-window": window.Close(); break;
                }

                AssertOwnerlessCreationRejected(helper, service);
            }
            finally
            {
                modalScope?.Dispose();
                helper.DestroyWindow(null!, null!, null!);
                if (!((PresentationSource)owner).IsDisposed) owner.RootVisual = null;
                if (!window.IsDisposed) window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortablePopupFact]
    public void OwnerlessPopupRejectsAmbiguousActiveSourcesWithoutCallingHost()
    {
        RunInUiApartment(() =>
        {
            using var first = PortablePresentationSourceHost.Create();
            using var second = PortablePresentationSourceHost.Create();
            first.RootVisual = new ActivePopupRoot { Active = true };
            second.RootVisual = new ActivePopupRoot { Active = true };
            var service = new PopupService(first);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            AssertOwnerlessCreationRejected(new Popup.PopupSecurityHelper(), service);
        });
    }

    [PortablePopupFact]
    public void OwnerlessPopupCannotBorrowAnActiveSourceFromAnotherDispatcher()
    {
        RunInUiApartment(() =>
        {
            using var foreignOwner = PortablePresentationSourceHost.Create();
            foreignOwner.RootVisual = new ActivePopupRoot { Active = true };
            // Keep the foreign source live while another dispatcher attempts creation.
            RunInUiApartment(() =>
            {
                using var localOwner = PortablePresentationSourceHost.Create();
                localOwner.RootVisual = new Border(); // No active-source capability.
                var service = new PopupService(localOwner);
                using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
                AssertOwnerlessCreationRejected(new Popup.PopupSecurityHelper(), service);
            });
        });
    }

    [PortablePopupFact]
    public void ExplicitPlacementTargetRemainsAuthoritativeOverActiveOwner()
    {
        RunInUiApartment(() =>
        {
            using var explicitOwner = PortablePresentationSourceHost.Create();
            using var activeOwner = PortablePresentationSourceHost.Create();
            var target = new Border();
            explicitOwner.RootVisual = target;
            activeOwner.RootVisual = new ActivePopupRoot { Active = true };
            var service = new PopupService(explicitOwner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(400, 300, target, true, null!, null!, null!);
                service.Request!.PlacementTarget.Should().BeSameAs(target);
                helper.PortableInputOwnerSource.Should().BeSameAs(explicitOwner);
                helper.DestroyWindow(null!, null!, null!);

                Action create = () => helper.BuildWindow(400, 300, new Border(), true, null!, null!, null!);
                create.Should().Throw<PlatformNotSupportedException>().WithMessage("*No portable popup host accepted*");
                helper.PortableInputOwnerSource.Should().BeNull();
                helper.HasWindowReference().Should().BeFalse();
                service.Creates.Should().Be(1);
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }

    [PortablePopupTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerlessPopupDoesNotBypassRejectedOrInvalidHost(bool invalidSource)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            owner.RootVisual = new ActivePopupRoot { Active = true };
            var service = new PopupService(owner) { Reject = !invalidSource, InvalidSource = invalidSource };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            Action create = () => helper.BuildWindow(400, 300, null!, true, null!, null!, null!);
            create.Should().Throw<PlatformNotSupportedException>().WithMessage("*No portable popup host accepted*");
            helper.PortableInputOwnerSource.Should().BeNull();
            helper.HasWindowReference().Should().BeFalse();
            service.Creates.Should().Be(1);
            service.Destroys.Should().Be(invalidSource ? 1 : 0);
        });
    }

    private static void AssertOwnerlessCreationRejected(Popup.PopupSecurityHelper helper, PopupService service)
    {
        Action create = () => helper.BuildWindow(400, 300, null!, true, null!, null!, null!);
        create.Should().Throw<PlatformNotSupportedException>().WithMessage("*No portable popup host accepted*");
        helper.PortableInputOwnerSource.Should().BeNull();
        helper.HasWindowReference().Should().BeFalse();
        service.Creates.Should().Be(0);
    }

    private sealed class ActivePopupRoot : Border, IPortableAccessKeyScopeSource
    {
        internal bool Active { get; set; }
        public bool IsPortableAccessKeyScopeActive => Active;
    }

    private sealed class PortablePopupFactAttribute : FactAttribute
    {
        public PortablePopupFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input initialization, including on Windows.";
        }
    }

    private sealed class PortablePopupTheoryAttribute : TheoryAttribute
    {
        public PortablePopupTheoryAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Requires portable media selected before input initialization, including on Windows.";
        }
    }

    [Fact]
    public void PopupModalAdmissionUsesItsActualOwnerSourceAndReleasesItOnDestroy()
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var dialog = new Window { Width = 200, Height = 100 };
            owner.RootVisual = dialog;
            var service = new PopupService(owner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            try
            {
                helper.BuildWindow(0, 0, dialog, true, null!, null!, null!);
                helper.PortableInputOwnerSource.Should().BeSameAs(owner);
                using (PortableModalInputScope.Enter(dialog))
                {
                    PortableWindowActivationService.IsModalInputAllowed(
                        helper.PortableInputOwnerSource.RootVisual as UIElement).Should().BeTrue();
                    using (PortableModalInputScope.Enter(new object()))
                        PortableWindowActivationService.IsModalInputAllowed(
                            helper.PortableInputOwnerSource.RootVisual as UIElement).Should().BeFalse();
                }
                helper.DestroyWindow(null!, null!, null!);
                helper.PortableInputOwnerSource.Should().BeNull();
            }
            finally
            {
                helper.DestroyWindow(null!, null!, null!);
                owner.RootVisual = null;
                dialog.Close();
            }
        });
    }

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
        public int Creates, Positions, Sizes, Shows, Hides, HitTestChanges, Destroys;
        public Point LastPosition { get; private set; }
        private object? _identity;

        public bool TryGetPopupPlacementBounds(object source, PortableRect target, out PortablePopupPlacementBounds bounds)
        {
            LastPlacementTarget = target;
            bounds = PlacementBounds;
            return !RejectBounds && ReferenceEquals(source, _identity);
        }

        public bool TryCreatePopup(PortablePopupCreateRequest request, out object? source)
        {
            Creates++;
            source = null;
            if (request.OwnerHandle != owner.Handle || Reject) return false;
            Request = request;
            source = InvalidSource ? new object() : Source = PortablePresentationSourceHost.Create();
            _identity = source;
            return true;
        }

        public bool TrySetPopupPosition(object source, int x, int y) { Positions++; LastPosition = new Point(x, y); return ReferenceEquals(source, _identity); }
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
