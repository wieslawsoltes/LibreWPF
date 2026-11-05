// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public partial class PortableWindowActivationServiceTests
{
    [PortableInputFact]
    public void SourceTaskbarChangesUseActualActivationWithoutChangingSourcePolicy()
    {
        RunInUiApartment(() =>
        {
            var values = new List<(Window Window, bool Visible)>();
            WithCaptionWindow(false, (window, _, _) =>
            {
                window.ShowInTaskbar = false;
                window.ShowInTaskbar = true;
                Assert.Equal(new[] { false, true }, values.Select(value => value.Visible));
                Assert.All(values, value => Assert.Same(window, value.Window));
                Assert.True(window.ShowInTaskbar);
                Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
            }, taskbarPolicy: (window, visible) => { values.Add((window, visible)); return false; });
        });
    }

    [PortableInputFact]
    public void TaskbarRegistrationKeepsLegacyWin32AndClearsX11Capability()
    {
        RunInUiApartment(() =>
        {
            object activation = new();
            int x11 = 0, win32 = 0;
            try
            {
                PortableWindowActivationService.RegisterCallbacks(new PortableWindowActivationCallbacks(value => value)
                {
                    SetX11TaskbarVisibility = (owner, value) =>
                    {
                        Assert.Same(activation, owner); Assert.False(value); x11++; return false;
                    }
                });
                Assert.False(PortableWindowActivationService.SetTaskbarVisibility(activation, false));
                Assert.False(PortableWindowActivationService.SetTaskbarVisibility(null, false));
                Assert.Equal(1, x11);
                PortableWindowActivationService.Register(value => value,
                    setWin32TaskbarVisibility: (owner, value) =>
                    {
                        Assert.Same(activation, owner); Assert.True(value); win32++;
                    });
                Assert.True(PortableWindowActivationService.SetTaskbarVisibility(activation, true));
                Assert.Equal(1, x11);
                Assert.Equal(1, win32);
                PortableWindowActivationService.Clear();
                Assert.False(PortableWindowActivationService.SetTaskbarVisibility(activation, true));
                Assert.Equal(1, win32);
                PortableWindowActivationService.RegisterCallbacks(new PortableWindowActivationCallbacks(value => value)
                {
                    SetX11TaskbarVisibility = (_, _) => { PortableWindowActivationService.Clear(); return true; }
                });
                Assert.False(PortableWindowActivationService.SetTaskbarVisibility(activation, true));
            }
            finally { PortableWindowActivationService.Clear(); }
        });
    }
}
