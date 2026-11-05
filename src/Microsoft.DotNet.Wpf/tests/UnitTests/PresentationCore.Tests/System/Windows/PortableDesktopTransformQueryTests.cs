// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public sealed class PortableDesktopTransformQueryTests
{
    [Theory]
    [InlineData(1.0, 1.0, 2.0, 2.0)]
    [InlineData(1.5, 2.0, 2.0, 1.25)]
    public void PublicFacadeReportsActualDesktopMappingIndependentlyOfFramebufferDpi(
        double scaleX, double scaleY, double dpiX, double dpiY)
    {
        using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create(dpiX, dpiY);
        var geometry = (IPortableDesktopGeometryHost)host;
        var root = new DrawingVisual();
        host.RootVisual = root;
        var expected = new PortableDesktopTransform(-1920, 24, scaleX, scaleY);
        geometry.SetDesktopTransform(expected);
        PresentationSource facade = PresentationSource.FromVisual(root);
        Assert.IsType<HwndSource>(facade);

        Assert.True(PortablePresentationSourceHost.TryGetDesktopTransform(facade, out var snapshot));
        Assert.Equal(expected, snapshot);
        Assert.True(PortablePresentationSourceHost.TryGetDesktopTransform((PresentationSource)host, out var direct));
        Assert.Equal(snapshot, direct);
        var client = new Point(8, 12);
        var desktop = new Point(-1920 + 8 * scaleX, 24 + 12 * scaleY);
        Assert.Equal(desktop, root.PointToScreen(client));
        Assert.Equal(client, root.PointFromScreen(desktop));

        host.SetDeviceScale(3, 4);
        Assert.True(PortablePresentationSourceHost.TryGetDesktopTransform(facade, out var afterDpi));
        Assert.Equal(snapshot, afterDpi);
        Assert.Equal(desktop, root.PointToScreen(client));
        host.SetClientOrigin(200, -100);
        Assert.True(PortablePresentationSourceHost.TryGetDesktopTransform(facade, out var moved));
        Assert.Equal(new PortableDesktopTransform(200, -100, scaleX, scaleY), moved);
        Assert.Equal(expected, snapshot); // A returned snapshot never changes with its host.
    }

    [Fact]
    public void MissingSourceIsRejectedWithoutInventingAMapping()
    {
        Assert.Null(PresentationSource.FromVisual(new DrawingVisual()));
        Assert.Throws<ArgumentNullException>("source", () =>
            PortablePresentationSourceHost.TryGetDesktopTransform(null!, out _));
    }

    [Fact]
    public void UnknownCustomSourceDoesNotPublishOrInvokeItsGeometryContract()
    {
        // PointToScreen does not adopt arbitrary custom geometry-host interfaces.
        // The public observation must use that same source-selection contract.
        var source = new CustomSource();
        Assert.False(PortablePresentationSourceHost.TryGetDesktopTransform(source, out var transform));
        Assert.False(transform.IsValid);
    }

    [Fact]
    public void DisposedFacadeAndOwnerDoNotReturnTheirOldMapping()
    {
        IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create(2, 2);
        var root = new DrawingVisual();
        host.RootVisual = root;
        PresentationSource facade = PresentationSource.FromVisual(root);
        host.SetClientOrigin(200, 150);
        host.Dispose();

        Assert.False(PortablePresentationSourceHost.TryGetDesktopTransform(facade, out var fromFacade));
        Assert.False(fromFacade.IsValid);
        Assert.False(PortablePresentationSourceHost.TryGetDesktopTransform((PresentationSource)host, out var fromOwner));
        Assert.False(fromOwner.IsValid);
    }

    [Fact]
    public void RetiredFacadeDoesNotPublishThroughTheOwnersDisposalCallback()
    {
        using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
        var owner = (PortablePresentationSource)host;
        bool observed = false;
        owner.Disposed += (_, _) =>
        {
            Assert.False(owner.IsDisposed); // The event precedes final owner teardown.
            Assert.True(owner.HwndSource.IsDisposed);
            Assert.False(PortablePresentationSourceHost.TryGetDesktopTransform(owner, out var transform));
            Assert.False(transform.IsValid);
            observed = true;
        };
        host.Dispose();
        Assert.True(observed);
    }

    [Fact]
    public void ForeignThreadCannotReadSourceGeometry()
    {
        using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
        var root = new DrawingVisual();
        host.RootVisual = root;
        PresentationSource facade = PresentationSource.FromVisual(root);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { PortablePresentationSourceHost.TryGetDesktopTransform(facade, out _); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.True(PortablePresentationSourceHost.TryGetDesktopTransform(facade, out _));
    }

    private sealed class CustomSource : PresentationSource, IPortableDesktopGeometryHost
    {
        public override Visual RootVisual { get => throw new InvalidOperationException(); set => throw new InvalidOperationException(); }
        public override bool IsDisposed => throw new InvalidOperationException();
        protected override CompositionTarget GetCompositionTargetCore() => throw new InvalidOperationException();
        public PortableDesktopTransform DesktopTransform => throw new InvalidOperationException();
        public void SetDesktopTransform(in PortableDesktopTransform transform) => throw new InvalidOperationException();
    }
}

public sealed partial class NativeDesktopTransformQueryTests
{
    private sealed class WindowsStaFactAttribute : StaFactAttribute
    {
        public WindowsStaFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires the original native HWND source on Windows.";
        }
    }

    [WindowsStaFact]
    public void NativeHwndKeepsItsExistingScreenCoordinatesAndHasNoPortableMapping()
    {
        Assert.Equal(PortableWpfMediaBackend.WindowsMil, PortableWpfRuntime.ConfiguredMediaBackend);
        string? expectedCore = Environment.GetEnvironmentVariable("LIBREWPF_DESKTOP_QUERY_EXPECTED_CORE");
        if (expectedCore != null)
        {
            Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
            Assert.Equal(Path.GetFullPath(expectedCore),
                Path.GetFullPath(typeof(PresentationSource).Assembly.Location), StringComparer.OrdinalIgnoreCase);
        }
        using var source = new HwndSource(new HwndSourceParameters("Desktop mapping query")
        {
            WindowStyle = unchecked((int)0x80000000), // Hidden native popup; no input or activation.
            Width = 16,
            Height = 16,
            PositionX = 11,
            PositionY = 13
        });
        Assert.False(source.IsPortable);
        Assert.NotEqual(0, IsWindow(source.Handle));
        Assert.NotEqual(0u, GetWindowThreadProcessId(source.Handle, out uint process));
        Assert.Equal((uint)Environment.ProcessId, process);
        var root = new DrawingVisual();
        source.RootVisual = root;
        var point = new Point(5, 7);
        Point before = root.PointToScreen(point);

        Assert.False(PortablePresentationSourceHost.TryGetDesktopTransform(source, out var transform));
        Assert.False(transform.IsValid);
        Assert.Equal(before, root.PointToScreen(point));
        if (expectedCore != null)
        {
            string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(expectedCore))!;
            bool observedMil = false;
            using Process current = Process.GetCurrentProcess();
            foreach (ProcessModule module in current.Modules)
            {
                string name = Path.GetFileName(module.FileName);
                if (!name.Equals("wpfgfx_cor3.dll", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("PresentationNative_cor3.dll", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("PenImc_cor3.dll", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("DirectWriteForwarder.dll", StringComparison.OrdinalIgnoreCase)) continue;
                Assert.Equal(Path.Combine(expectedDirectory, name), Path.GetFullPath(module.FileName), StringComparer.OrdinalIgnoreCase);
                observedMil |= name.Equals("wpfgfx_cor3.dll", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"Native desktop query loaded: {module.FileName}");
            }
            Assert.True(observedMil, "The actual Windows MIL native producer must be loaded from the verified x64 test closure.");
        }
        Console.WriteLine($"Native desktop query: architecture={RuntimeInformation.ProcessArchitecture}; source={typeof(PresentationSource).Assembly.Location}; media={PortableWpfRuntime.ConfiguredMediaBackend}; ownedHwnd=0x{source.Handle:X}; inputActions=0");
    }

    [LibraryImport("user32.dll")]
    private static partial int IsWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
