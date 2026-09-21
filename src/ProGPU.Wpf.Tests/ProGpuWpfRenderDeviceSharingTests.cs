using System;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuWpfRenderDeviceSharingTests
{
    [Theory]
    // Windows presents through D3D12, where a per-window device is the shipped configuration.
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void RenderDeviceSharingFollowsThePlatformAndTheOptOut(
        bool isWindows,
        bool explicitlyDisabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProGpuWpfRenderDeviceSharing.ShouldShareRenderDevice(isWindows, explicitlyDisabled));
    }

    [Fact]
    public void RegisteringADeviceOwnerRequiresAContext()
    {
        Assert.Throws<ArgumentNullException>(
            () => ProGpuWpfRenderDeviceSharing.RegisterDeviceOwnerContext(null!));
    }

    [Fact]
    public void RetiringAnAbsentDeviceOwnerIsANoOp()
    {
        ProGpuWpfRenderDeviceSharing.RetireDeviceOwnerContext(null);
    }

    [Fact]
    public void WindowHostsWithoutACompositionTargetReportNoSharedRenderDevice()
    {
        using var host = new ProGpuWpfWindowHost();

        Assert.False(host.UsesSharedRenderDevice);
    }
}
