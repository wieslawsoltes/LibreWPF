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

    private sealed class PopupService(IPortablePresentationSourceHost owner) : IPortablePopupServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.PresentationFramework;
        public PortablePopupCreateRequest? Request { get; private set; }
        public IPortablePresentationSourceHost? Source { get; private set; }
        public bool Reject { get; init; }
        public bool InvalidSource { get; init; }
        public bool ThrowOnDestroy { get; init; }
        public int Positions, Sizes, Shows, Hides, HitTestChanges, Destroys;
        private object? _identity;

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
