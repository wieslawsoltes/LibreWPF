using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfBitmapCacheSourceContractTests
{
    [Fact]
    public void NativeMilBitmapCacheUsesTypedSourceContractWithoutReflection()
    {
        string contract = ReadRepoFile(
            "external", "ProGPU", "src", "ProGPU.Wpf.Interop",
            "PortableBitmapCache.cs");
        string bitmapCache = ReadRepoFile(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore",
            "System", "Windows", "Media", "BitmapCache.cs");
        string presentationCoreRef = ReadRepoFile(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore",
            "ref", "PresentationCore.cs");
        string compiler = ReadRepoFile(
            "src", "ProGPU.Wpf", "Composition", "Mil",
            "WpfNativeMilSceneCompiler.cs");
        string builder = ReadRepoFile(
            "external", "ProGPU", "src", "ProGPU.Backend.Native",
            "NativeMilBatchBuilder.cs");

        Assert.Contains(
            "public interface IPortableBitmapCacheSource",
            contract,
            StringComparison.Ordinal);
        Assert.Contains(
            "public readonly record struct PortableBitmapCache",
            contract,
            StringComparison.Ordinal);
        Assert.Contains(
            "BitmapCache : CacheMode, IPortableBitmapCacheSource",
            bitmapCache,
            StringComparison.Ordinal);
        Assert.Contains(
            "IPortableBitmapCacheSource.TryGetPortableBitmapCache",
            bitmapCache,
            StringComparison.Ordinal);
        Assert.Contains(
            "ProGPU.Wpf.Interop.IPortableBitmapCacheSource",
            presentationCoreRef,
            StringComparison.Ordinal);
        Assert.Contains(
            "resource is not IPortableBitmapCacheSource source",
            compiler,
            StringComparison.Ordinal);
        Assert.Contains(
            "Batch.SetVisualCacheMode(",
            compiler,
            StringComparison.Ordinal);
        Assert.Contains(
            "Batch.SetBitmapCache(",
            compiler,
            StringComparison.Ordinal);
        Assert.Contains(
            "NativeMilCommand.VisualSetCacheMode, 12",
            builder,
            StringComparison.Ordinal);
        Assert.Contains(
            "NativeMilCommand.BitmapCache, 28",
            builder,
            StringComparison.Ordinal);

        foreach (string source in new[] { bitmapCache, compiler })
        {
            Assert.DoesNotContain(
                "System.Reflection", source, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "BindingFlags", source, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "GetProperty(\"RenderAtScale\")",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "GetProperty(\"SnapsToDevicePixels\")",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "GetProperty(\"EnableClearType\")",
                source,
                StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(
                new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
