using System.Numerics;
using System.Reflection;
using System.Windows.Media.ProGPU;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Silk.NET.WebGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuWpfNativeHitTestingTests
{
    [Fact]
    public void NativeInputAdmissionIsFrozenAndRequiresTheNativeRenderer()
    {
        Assert.False(new ProGpuWpfWindowOptions().EnableNativeMilHitTesting);
        Assert.Throws<ArgumentException>(() => new ProGpuWpfWindowHost(
            new ProGpuWpfWindowOptions { EnableNativeMilHitTesting = true }));
        var options = new ProGpuWpfWindowOptions
            { RendererMode = ProGpuWpfRendererMode.NativeMilWgpu, EnableNativeMilHitTesting = true };
        using var host = new ProGpuWpfWindowHost(options);
        options.EnableNativeMilHitTesting = false;
        options.RendererMode = ProGpuWpfRendererMode.ManagedPortable;
        Assert.True(host.NativeMilHitTestingEnabled);
        Assert.Equal(ProGpuWpfRendererMode.NativeMilWgpu, host.RendererMode);
    }

    [Theory]
    [InlineData(1, 2, 2, 3U, 2, 3)]
    [InlineData(2, 2, 2, 100U, 2, 2)]
    [InlineData(0, 1, 1, 900U, 1, 256)]
    [InlineData(0, 4, 3, 3U, 4, 4)]
    public void OwnerExpansionKeepsTheManagedCapacityContract(
        int resolved, int requested, int hitCount, uint total, int capacity, int expected)
        => Assert.Equal(expected, ProGpuWpfNativeHitTesting.GetExpandedCapacity(
            resolved, requested, hitCount, total, capacity));

    [Fact]
    public void HostQueriesUsePresentedNativeOwnersAndNativeDiagnostics()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
            { RendererMode = ProGpuWpfRendererMode.NativeMilWgpu, EnableNativeMilHitTesting = true });
        var target = ProGpuWpfCompositionTarget.CreateHeadless();
        SetField(host, "_target", target);
        var compositor = new NativeCompositor(target.Context, TextureFormat.Rgba8Unorm);
        SetField(host, "_nativeMilCompositor", compositor);
        // Same presented-snapshot path used while forwarding a native input
        // event; no dispatcher render is manufactured by this routing fixture.
        SetField(host, "_isForwardingPlatformInput", true);
        object first = new(), second = new();
        InstallIndex(compositor);
        var owners = compositor.BindGpuHitTestOwners(
            new NativeGpuHitTestOwnerMap<object>([new(42, first), new(43, second)]), 919, 1);
        typeof(ProGpuWpfWindowHost).GetProperty("NativeMilHitTestOwners",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, owners);

        Assert.Null(target.LastGpuHitTestIndex);
        Assert.True(host.HasGpuHitTestCache);
        Assert.True(host.TryGetGpuHitTestCacheSnapshot(out var before));
        Assert.False(before.HasDeviceIndex);
        Assert.Equal(3, before.PrimitiveCount);
        Assert.Equal(2, before.OwnerCount);
        Assert.True(host.TryHitTestOwner(5, 5, out object? hit));
        Assert.Same(second, hit); // Topmost unknown owner must trigger expansion.
        object?[] output = new object?[2];
        Assert.True(host.TryHitTestOwners(5, 5, output, out int count));
        Assert.Equal(2, count);
        Assert.Same(second, output[0]);
        Assert.Same(first, output[1]);
        Assert.True(host.TryQueryHitTestBoundsOwners(-1, -1, 21, 11, output, out count));
        Assert.Equal(2, count);
        Assert.True(host.TryQueryHitTestBoundsCandidates(-1, -1, 21, 11, output, out count));
        Assert.Equal(2, count);
        Assert.Same(second, Assert.IsType<PortableGeometryHitTestCandidate>(output[0]).VisualHit);
        Assert.True(host.TryQueryHitTestEllipseCandidates(-20, -20, 40, 30, output, out count));
        Assert.Equal(2, count);
        Assert.True(host.TryHitTestOwners(100, 100, output, out count));
        Assert.Equal(0, count);
        Assert.True(host.TryGetGpuHitTestCacheSnapshot(out var after));
        Assert.True(after.HasDeviceIndex);
        Assert.Equal(before.PrimitiveCount, after.PrimitiveCount);
        Assert.Null(target.LastGpuHitTestIndex);

        host.Dispose();
        Assert.False(host.TryHitTestOwners(5, 5, output, out count));
        Assert.Equal(0, count);
        Assert.False(host.HasGpuHitTestCache);
    }

    [Fact]
    public void NativeHostWithoutAdmissionCannotReadAManagedIndex()
    {
        using var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
            { RendererMode = ProGpuWpfRendererMode.NativeMilWgpu });
        SetField(host, "_target", ProGpuWpfCompositionTarget.CreateHeadless());
        SetField(host, "_isForwardingPlatformInput", true);
        Assert.Throws<NotSupportedException>(() => host.TryHitTestOwner(5, 5, out _));
        Assert.False(host.HasGpuHitTestCache);
    }

    private static void InstallIndex(NativeCompositor compositor)
    {
        // Original ProGPU package-consumer index contract, expanded with an
        // unknown top owner to exercise the real host's bounded retry path.
        Span<byte> bytes = stackalloc byte[4096];
        var builder = new NativeSceneStreamBuilder(bytes, 919, 1, 0, 1);
        Span<NativeGpuHitTestPrimitive> primitives = stackalloc NativeGpuHitTestPrimitive[3];
        for (int i = 0; i < primitives.Length; i++)
            primitives[i] = new()
            {
                BoundsMin = Vector2.Zero, BoundsMax = new(20, 10),
                Data0 = new NativeFloat4 { Z = 20, W = 10 },
                InverseTransform0 = new NativeFloat4 { X = 1 },
                InverseTransform1 = new NativeFloat4 { Y = 1 },
                Kind = (uint)NativeGpuHitTestPrimitiveKind.RectangleFill,
                Flags = (uint)(NativeGpuHitTestPrimitiveFlags.Visible | NativeGpuHitTestPrimitiveFlags.HitTestVisible),
                Id = i == 2 ? 999 : 42 + i, ZIndex = i + 1
            };
        NativeGpuHitTestNode node = new()
            { BoundsMin = Vector2.Zero, BoundsMax = new(20, 10), PrimitiveCount = 3 };
        Assert.True(builder.TryAddHitTestIndexResource(1, 1, primitives, [node], [0U, 1U, 2U], [], out _));
        Assert.True(builder.TryBuild(out ReadOnlySpan<byte> stream));
        compositor.UpdateScene(stream);
    }

    // Test-only known-field fixture injection follows existing host tests.
    private static void SetField(ProGpuWpfWindowHost host, string name, object value)
        => typeof(ProGpuWpfWindowHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, value);
}
