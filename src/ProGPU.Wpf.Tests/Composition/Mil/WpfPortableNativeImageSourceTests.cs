using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfPortableNativeImageSourceTests
{
    [Fact]
    public void LibreWinFormsStableImagesUseTypedGpuDirectCarrierBeforePixelFallback()
    {
        string interop = File.ReadAllText(FindRepoPath(
            "external", "ProGPU", "src", "ProGPU.Wpf.Interop", "PortableNativeImageSource.cs"));
        string factory = File.ReadAllText(FindRepoPath(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "System", "Windows", "Media",
            "PortableNativeImageSourceFactory.cs"));
        string adapter = File.ReadAllText(FindRepoPath(
            "src", "ProGPU.Wpf", "Composition", "Mil", "WpfBitmapSourceImageAdapter.cs"));
        string commandSink = File.ReadAllText(FindRepoPath(
            "src", "ProGPU.Wpf", "Composition", "ProGpuCompositionCommandSink.cs"));
        string invalidationTracker = File.ReadAllText(FindRepoPath(
            "src", "ProGPU.Wpf", "Composition", "Mil", "WpfVisualInvalidationTracker.cs"));
        string host = File.ReadAllText(FindRepoPath(
            "external", "LibreWinForms", "src", "LibreWinForms.Portable",
            "LibreWinForms.WindowsFormsIntegration", "src", "WindowsFormsHost.cs"));

        Assert.Contains("public interface IPortableNativeImageSource", interop, StringComparison.Ordinal);
        Assert.Contains("TryGetPortableNativeImage(out object? nativeImage)", interop, StringComparison.Ordinal);
        Assert.Contains("public static ImageSource Create(IPortableNativeImageSource nativeImageSource)", factory, StringComparison.Ordinal);
        Assert.Contains("private sealed class PortableNativeImageSource : ImageSource, IPortableNativeImageSource", factory, StringComparison.Ordinal);
        Assert.Contains("return _nativeImageSource.TryGetPortableNativeImage(out nativeImage);", factory, StringComparison.Ordinal);

        Assert.Contains("imageSource is IPortableNativeImageSource", adapter, StringComparison.Ordinal);
        Assert.Contains("TryGetNativeGpuTexture(nativeImage, out var resolvedTexture)", adapter, StringComparison.Ordinal);
        Assert.Contains("nativeImage is IProGpuTextureLeaseSource nativeTextureSource", adapter, StringComparison.Ordinal);
        Assert.Contains("drawingContext.TryRetainTexture(", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", adapter, StringComparison.Ordinal);
        Assert.Contains("TryRetainGpuTexture(", commandSink, StringComparison.Ordinal);
        Assert.DoesNotContain("imageSource is MediaBitmapSource bitmapSource", commandSink, StringComparison.Ordinal);
        Assert.Contains("source is PortableNativeImageSource nativeImageSource", invalidationTracker, StringComparison.Ordinal);
        Assert.Contains("source is IProGpuInvalidatingTextureSource textureSource", invalidationTracker, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", invalidationTracker, StringComparison.Ordinal);

        int typedPath = host.IndexOf("if (image is IProGpuTextureSource textureSource)", StringComparison.Ordinal);
        int pixelFallback = host.IndexOf("return CreatePixelImageSource(image);", StringComparison.Ordinal);
        Assert.True(typedPath >= 0);
        Assert.True(pixelFallback > typedPath);
        Assert.Contains("PortableNativeImageSourceFactory.Create(", host, StringComparison.Ordinal);
        Assert.Contains("private sealed class ProGpuDrawingImageSource : IPortableNativeImageSource", host, StringComparison.Ordinal);
        Assert.Contains("_textureSource.TryGetGpuTexture(out GpuTexture texture)", host, StringComparison.Ordinal);
        Assert.Contains("private static WriteableBitmap? CreatePixelImageSource(DrawingImage image)", host, StringComparison.Ordinal);
        Assert.Contains("bitmap.LockBits(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadPixels", host, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            string candidate = Path.Combine(new[] { current.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Unable to locate repository file '{Path.Combine(pathSegments)}'.");
    }
}
