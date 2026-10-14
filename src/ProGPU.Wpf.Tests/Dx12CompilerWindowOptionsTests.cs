using ProGPU.Backend;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class Dx12CompilerWindowOptionsTests
{
    [Fact]
    public void UnspecifiedOwnedWindowPreservesTheCoreStartupPolicy()
    {
        Assert.Null(new ProGpuWpfWindowOptions().Dx12CompilerOptions);
        using var context = ProGpuWpfCompositionTarget.CreateDeviceContext(null, null);
        Assert.Equal(WgpuDx12CompilerOptions.FromEnvironment(), context.Dx12CompilerOptions);
    }

    [Theory]
    [InlineData(ProGpuWpfRendererMode.ManagedPortable)]
    [InlineData(ProGpuWpfRendererMode.NativeMilWgpu)]
    public void SourceActivationAndOwnedContextKeepTheExactCompilerDescriptor(ProGpuWpfRendererMode renderer)
    {
        var requested = new WgpuDx12CompilerOptions(WgpuDx12ShaderCompiler.Dxc, Path.GetFullPath("compiler"));
        var fallback = new ProGpuWpfWindowOptions { RendererMode = renderer, Dx12CompilerOptions = requested };
        var actual = WpfPortableWindowActivation.CreateHostOptions(new object(), fallback);
        Assert.Same(requested, actual.Dx12CompilerOptions);
        Assert.Equal(renderer, actual.RendererMode);
        using var context = ProGpuWpfCompositionTarget.CreateDeviceContext(null, null, actual.Dx12CompilerOptions);
        Assert.Same(requested, context.Dx12CompilerOptions);
    }

    [Theory]
    [InlineData(WgpuDx12ShaderCompiler.Fxc)]
    [InlineData(WgpuDx12ShaderCompiler.Dxc)]
    public void PopupUsesTheLiveOwnersCompilerInsteadOfTheProcessDefault(WgpuDx12ShaderCompiler compiler)
    {
        var requested = new WgpuDx12CompilerOptions(compiler);
        using var owner = new WgpuContext { Dx12CompilerOptions = requested };
        using var popup = ProGpuWpfCompositionTarget.CreateDeviceContext(null, owner);
        Assert.Same(requested, popup.Dx12CompilerOptions);
    }

    [Fact]
    public void RecoveryReadsTheReplacementOwnerAndRetainsConflictingExplicitRequestsForCoreValidation()
    {
        var originalCompiler = new WgpuDx12CompilerOptions(WgpuDx12ShaderCompiler.Fxc);
        var replacementCompiler = new WgpuDx12CompilerOptions(WgpuDx12ShaderCompiler.Dxc, Path.GetFullPath("replacement-compiler"));
        using var original = new WgpuContext { Dx12CompilerOptions = originalCompiler };
        using var replacement = new WgpuContext { Dx12CompilerOptions = replacementCompiler };
        using var first = ProGpuWpfCompositionTarget.CreateDeviceContext(null, original);
        using var recovered = ProGpuWpfCompositionTarget.CreateDeviceContext(null, replacement);
        using var conflicting = ProGpuWpfCompositionTarget.CreateDeviceContext(null, replacement, originalCompiler);
        Assert.Same(originalCompiler, first.Dx12CompilerOptions);
        Assert.Same(replacementCompiler, recovered.Dx12CompilerOptions);
        Assert.Same(originalCompiler, conflicting.Dx12CompilerOptions);
        Assert.Same(replacementCompiler, replacement.Dx12CompilerOptions);
    }
}
