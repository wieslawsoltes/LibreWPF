using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WgpuSurfaceTransitionContractTests
{
    [Fact]
    public void WpfHostDefersResizeAndRenderingWhileSurfaceCapabilitiesAreUnavailable()
    {
        string host = File.ReadAllText(FindRepoPath("src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));

        Assert.Contains("if (!_target.Context.TryConfigureSwapChain(", host, StringComparison.Ordinal);
        Assert.Contains("if (!_target.Context.TryReconfigureIfNeeded(pixelWidth, pixelHeight))", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_target.Context.ConfigureSwapChain(\n            geometry.PixelWidth", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_target.Context.ReconfigureIfNeeded(pixelWidth, pixelHeight);", host, StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}
