// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Interop;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public partial class PortableWindowActivationServiceTests
{
    [PortableInputFact]
    public void SourceTaskbarStateAndChangesUseTypedHostWithoutInterpretingOpaqueHandle()
    {
        RunInUiApartment(() =>
        {
            var window = new Window { ShowInTaskbar = false };
            var updates = new List<bool>();
            PortableWindowActivationService.Register(activate: value => value,
                createHidden: value => value, getHandle: _ => new IntPtr(123),
                setWin32TaskbarVisibility: (source, visible) =>
                {
                    Assert.Same(window, source);
                    updates.Add(visible);
                });
            try
            {
                var source = (IPortableWindowStateSource)window;
                Assert.True(source.TryGetPortableWindowState(out var initial));
                Assert.True(initial.HasShowInTaskbar);
                Assert.False(initial.ShowInTaskbar);
                Assert.Equal(new IntPtr(123), new WindowInteropHelper(window).EnsureHandle());
                Assert.Empty(updates); // The host reads the initial typed snapshot.
                window.ShowInTaskbar = true;
                window.ShowInTaskbar = false;
                Assert.Equal(new[] { true, false }, updates);
                Assert.True(source.TryGetPortableWindowState(out var changed));
                Assert.True(changed.HasShowInTaskbar);
                Assert.False(changed.ShowInTaskbar);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }

    [PortableInputFact]
    public void ClearingTaskbarCallbackDoesNotRetainHostOrFallBackToOpaqueWin32Handle()
    {
        RunInUiApartment(() =>
        {
            var window = new Window();
            int calls = 0;
            PortableWindowActivationService.Register(activate: value => value,
                createHidden: value => value, getHandle: _ => new IntPtr(123),
                setWin32TaskbarVisibility: (_, _) => calls++);
            try
            {
                new WindowInteropHelper(window).EnsureHandle();
                window.ShowInTaskbar = false;
                Assert.Equal(1, calls);
                PortableWindowActivationService.Clear();
                // Legacy/non-Win32 hosts omit this optional Win32 capability.
                // Their existing taskbar behavior stays separately unqualified.
                PortableWindowActivationService.Register(activate: value => value,
                    createHidden: value => value, getHandle: _ => new IntPtr(123));
                window.ShowInTaskbar = true;
                Assert.Equal(1, calls);
            }
            finally
            {
                window.Close();
                PortableWindowActivationService.Clear();
            }
        });
    }
}
