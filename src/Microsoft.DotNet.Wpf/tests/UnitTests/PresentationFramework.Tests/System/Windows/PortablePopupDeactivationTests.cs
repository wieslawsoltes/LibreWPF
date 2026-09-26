// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace System.Windows;

public partial class PortablePopupOwnershipTests
{
    [PortablePopupTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerDeactivationClosesAutoClosePopupAndReleasesCapture(bool ownerless)
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            var popup = CreateDeactivationPopup(ownerless ? null : window);
            int couldClose = 0;
            popup.PopupCouldClose += (_, _) => couldClose++;
            try
            {
                popup.IsOpen = true;
                Mouse.Captured.Should().BeSameAs(service.Source!.RootVisual);
                PortableWindowActivationService.SetActivationState(window, false);
                // The native hook also schedules dismissal at Normal priority.
                popup.IsOpen.Should().BeTrue();
                DrainPopupDispatcher();
                popup.IsOpen.Should().BeFalse();
                Mouse.Captured.Should().BeNull();
                couldClose.Should().Be(1);
                service.Hides.Should().Be(1);
            }
            finally { CloseDeactivationPopup(popup); }
        });
    }

    [PortablePopupTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerDeactivationNotificationPreservesStaysOpenPolicy(bool staysOpen)
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            var popup = CreateDeactivationPopup(window, staysOpen);
            int couldClose = 0;
            popup.PopupCouldClose += (_, _) => couldClose++;
            try
            {
                popup.IsOpen = true;
                PortableWindowActivationService.SetActivationState(window, false);
                DrainPopupDispatcher();
                popup.IsOpen.Should().Be(staysOpen);
                couldClose.Should().Be(1);
                PortableWindowActivationService.SetActivationState(window, false);
                DrainPopupDispatcher();
                couldClose.Should().Be(1); // No new activation edge.
            }
            finally { CloseDeactivationPopup(popup); }
        });
    }

    [PortablePopupFact]
    public void OwnerDeactivationDoesNotConfusePopupFocusOrInputWithWindowActivation()
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            var popup = CreateDeactivationPopup(window);
            try
            {
                popup.IsOpen = true;
                var child = (Border)popup.Child;
                service.Source!.HitTestOverride = (_, _) => child;
                Keyboard.Focus(child).Should().BeSameAs(child);
                PortableWindowActivationService.ProcessInput((PresentationSource)service.Source,
                    new PortableInputEventArgs(PortableInputEventKind.MouseMove, x: 10, y: 10));
                DrainPopupDispatcher();
                window.IsActive.Should().BeTrue();
                popup.IsOpen.Should().BeTrue();
                Mouse.Captured.Should().BeSameAs(service.Source.RootVisual);
            }
            finally { CloseDeactivationPopup(popup); }
        });
    }

    [PortablePopupTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerDeactivationQueuedBeforeCloseCannotDismissReopenedPopup(bool destroySource)
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            var popup = CreateDeactivationPopup(window);
            int couldClose = 0;
            popup.PopupCouldClose += (_, _) => couldClose++;
            try
            {
                popup.IsOpen = true;
                PortableWindowActivationService.SetActivationState(window, false);
                popup.IsOpen = false;
                if (destroySource) popup.ForceClose();
                PortableWindowActivationService.SetActivationState(window, true);
                popup.IsOpen = true;
                DrainPopupDispatcher();
                popup.IsOpen.Should().BeTrue();
                couldClose.Should().Be(0);
                // The new opening still subscribes exactly once.
                PortableWindowActivationService.SetActivationState(window, false);
                DrainPopupDispatcher();
                popup.IsOpen.Should().BeFalse();
                couldClose.Should().Be(1);
            }
            finally { CloseDeactivationPopup(popup); }
        });
    }

    [PortablePopupFact]
    public void OwnerDeactivationUsesRetainedOwnerRatherThanChangedPlacementTarget()
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            using var otherSource = PortablePresentationSourceHost.Create();
            var other = new Window { Width = 200, Height = 100 };
            otherSource.RootVisual = other;
            otherSource.SetClientSize(200, 100);
            var popup = CreateDeactivationPopup(window);
            try
            {
                popup.IsOpen = true;
                popup.PlacementTarget = other;
                PortableWindowActivationService.SetActivationState(other, true);
                PortableWindowActivationService.SetActivationState(other, false);
                DrainPopupDispatcher();
                popup.IsOpen.Should().BeTrue();
                popup.PortableInputOwnerSource.Should().BeSameAs(owner);
                PortableWindowActivationService.SetActivationState(window, false);
                DrainPopupDispatcher();
                popup.IsOpen.Should().BeFalse();
            }
            finally
            {
                CloseDeactivationPopup(popup);
                otherSource.RootVisual = null;
                other.Close();
            }
        });
    }

    [PortablePopupFact]
    public void OwnerDeactivationDismissesNestedPopupsWithoutLeavingParentCapture()
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            var parent = CreateDeactivationPopup(window);
            var child = CreateDeactivationPopup(parent.Child);
            try
            {
                parent.IsOpen = true;
                var childService = CreateDeactivationService(owner);
                using var childRegistration = PortableWpfServiceRegistry.RegisterPopupService(childService);
                try
                {
                    child.IsOpen = true;
                    child.PortableInputOwnerSource.Should().BeSameAs(owner);
                    Mouse.Captured.Should().BeSameAs(childService.Source!.RootVisual);
                    PortableWindowActivationService.SetActivationState(window, false);
                    DrainPopupDispatcher();
                    child.IsOpen.Should().BeFalse();
                    parent.IsOpen.Should().BeFalse();
                    Mouse.Captured.Should().BeNull();
                }
                finally { CloseDeactivationPopup(child); }
            }
            finally { CloseDeactivationPopup(parent); }
        });
    }

    [PortablePopupTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerDeactivationReachesRealContextMenuAndToolTip(bool toolTip)
    {
        WithActivePopupOwner((window, owner, service) =>
        {
            if (toolTip)
            {
                var control = new ToolTip
                {
                    PlacementTarget = window, Placement = PlacementMode.Bottom,
                    Content = new Border { Width = 40, Height = 20 }
                };
                try
                {
                    control.IsOpen = true;
                    service.Shows.Should().Be(1);
                    PortableWindowActivationService.SetActivationState(window, false);
                    DrainPopupDispatcher();
                    control.IsOpen.Should().BeFalse();
                }
                finally { control.IsOpen = false; DrainPopupDispatcher(); }
            }
            else
            {
                var control = new ContextMenu { PlacementTarget = window, Placement = PlacementMode.Bottom };
                control.Items.Add(new Border { Width = 40, Height = 20 });
                try
                {
                    control.IsOpen = true;
                    service.Shows.Should().Be(1);
                    PortableWindowActivationService.SetActivationState(window, false);
                    DrainPopupDispatcher();
                    control.IsOpen.Should().BeFalse();
                }
                finally { control.IsOpen = false; DrainPopupDispatcher(); }
            }
            Mouse.Captured.Should().BeNull();
        });
    }

    [PortablePopupFact]
    public void OwnerDeactivationDoesNotRetainFailedShowSubscription()
    {
        WithActivePopupOwner((window, owner, _) =>
        {
            var rejected = new PopupService(owner) { ThrowOnShow = true,
                PlacementBounds = new(PortablePopupPlacementBoundsKind.NativeScreen,
                    new(0, 0, 1920, 1080), new(0, 0, 1920, 1080)) };
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(rejected);
            var popup = CreateDeactivationPopup(window, staysOpen: true);
            int couldClose = 0;
            popup.PopupCouldClose += (_, _) => couldClose++;
            try
            {
                Action show = () => popup.IsOpen = true;
                show.Should().Throw<InvalidOperationException>().WithMessage("Host show failure");
                PortableWindowActivationService.SetActivationState(window, false);
                DrainPopupDispatcher();
                couldClose.Should().Be(0);
            }
            finally { CloseDeactivationPopup(popup); }
        });
    }

    private static Popup CreateDeactivationPopup(UIElement? target, bool staysOpen = false) => new()
    {
        PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = staysOpen,
        Child = new Border { Width = 100, Height = 40, Focusable = true }
    };

    private static PopupService CreateDeactivationService(IPortablePresentationSourceHost owner) => new(owner)
    {
        PlacementBounds = new(PortablePopupPlacementBoundsKind.NativeScreen,
            new(0, 0, 1920, 1080), new(0, 0, 1920, 1080))
    };

    private static void WithActivePopupOwner(Action<Window, IPortablePresentationSourceHost, PopupService> action)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            var window = new Window { Width = 800, Height = 600 };
            PortableWindowActivationService.Register(activate: value => value, getHandle: _ => owner.Handle);
            var service = CreateDeactivationService(owner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            try
            {
                window.Show();
                owner.RootVisual = window;
                owner.SetClientSize(800, 600);
                PortableWindowActivationService.SetActivationState(window, true);
                action(window, owner, service);
            }
            finally
            {
                Keyboard.ClearFocus();
                Mouse.Capture(null);
                service.Source?.Dispose();
                owner.RootVisual = null;
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    private static void DrainPopupDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void CloseDeactivationPopup(Popup popup)
    {
        popup.IsOpen = false;
        popup.ForceClose();
        DrainPopupDispatcher();
    }
}
