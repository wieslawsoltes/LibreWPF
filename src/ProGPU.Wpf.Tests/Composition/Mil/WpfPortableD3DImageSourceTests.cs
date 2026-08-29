using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfPortableD3DImageSourceTests
{
    [Fact]
    public void D3DImageUsesTypedPointerFreeCanonicalMilSeam()
    {
        string interop = File.ReadAllText(FindRepoPath(
            "external", "ProGPU", "src", "ProGPU.Wpf.Interop",
            "PortableD3DImageFrame.cs"));
        string image = File.ReadAllText(FindRepoPath(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore",
            "System", "Windows", "InterOp", "D3DImage.cs"));
        string factory = File.ReadAllText(FindRepoPath(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore",
            "System", "Windows", "InterOp",
            "PortableD3DImageSourceFactory.cs"));
        string compiler = File.ReadAllText(FindRepoPath(
            "src", "ProGPU.Wpf", "Composition", "Mil",
            "WpfNativeMilSceneCompiler.cs"));
        string host = File.ReadAllText(FindRepoPath(
            "src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));

        Assert.Contains("public interface IPortableD3DImageSource", interop,
            StringComparison.Ordinal);
        Assert.Contains("ulong ContentVersion", interop,
            StringComparison.Ordinal);
        Assert.Contains("IPortableD3DImageSource, IPortableInvalidationSource",
            image, StringComparison.Ordinal);
        Assert.Contains("PortableD3DImageSourceFactory", factory,
            StringComparison.Ordinal);
        Assert.Contains("NativeMilResourceType.D3DImage", compiler,
            StringComparison.Ordinal);
        Assert.Contains("Batch.SetD3DImage(d3dImageHandle)", compiler,
            StringComparison.Ordinal);
        Assert.Contains("Batch.PresentD3DImage(d3dImageHandle)", compiler,
            StringComparison.Ordinal);
        Assert.Contains("SetD3DImageExternalImage(", compiler,
            StringComparison.Ordinal);
        Assert.Contains("frame.D3DImageSources", host,
            StringComparison.Ordinal);
        Assert.Contains("TryAcquireGpuTextureLease(", host,
            StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", image,
            StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", compiler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("pInteropDeviceBitmap", compiler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("hEvent", compiler,
            StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(
                new[] { current.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }
        throw new FileNotFoundException(
            $"Unable to locate repository file '{Path.Combine(pathSegments)}'.");
    }
}
