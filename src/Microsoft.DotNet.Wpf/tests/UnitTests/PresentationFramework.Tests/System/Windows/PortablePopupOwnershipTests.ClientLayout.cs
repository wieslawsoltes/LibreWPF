// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public partial class PortablePopupOwnershipTests
{
    [PortablePopupTheory]
    [InlineData(174.8984375, 175)]
    [InlineData(175.0, 175)]
    [InlineData(174.125, 174)]
    [InlineData(174.5, 175)]
    [InlineData(94.25, 94)]
    public void PopupClientLayoutRetainsTheHostExtentAcrossRepeatedSourceUpdates(double desiredWidth, int hostWidth)
    {
        RunInUiApartment(() =>
        {
            using var owner = PortablePresentationSourceHost.Create();
            owner.RootVisual = new ActivePopupRoot { Active = true };
            var service = new PopupService(owner);
            using var registration = PortableWpfServiceRegistry.RegisterPopupService(service);
            var helper = new Popup.PopupSecurityHelper();
            var child = new Border { Width = desiredWidth, Height = 33 };
            var root = new PopupRoot { Child = child };
            try
            {
                helper.BuildWindow(100, 100, null!, true, null!, null!, null!);
                helper.SetWindowRootVisual(root);
                for (int index = 0; index < 3; index++)
                {
                    helper.TryUpdatePortablePopupRootClientSize(root, out Size client).Should().BeTrue();
                    client.Should().Be(new Size(hostWidth, 33));
                    LayoutInformation.GetLayoutSlot(root).Size.Should().Be(client);
                    LayoutInformation.GetLayoutSlot(root).Location.Should().Be(new Point());
                    Media.VisualTreeHelper.GetOffset(root).Should().Be(new Vector());
                    Media.VisualTreeHelper.GetParent(root).Should().BeNull();
                    PresentationSource.FromVisual(root)!.RootVisual.Should().BeSameAs(root);
                    root.RenderSize.Should().Be(new Size(Math.Max(desiredWidth, hostWidth), 33));
                    root.DesiredSize.Should().Be(new Size(desiredWidth, 33));
                    child.Width.Should().Be(desiredWidth);
                    child.RenderSize.Should().Be(new Size(desiredWidth, 33));
                }
            }
            finally { helper.DestroyWindow(null!, null!, null!); }
        });
    }
}
