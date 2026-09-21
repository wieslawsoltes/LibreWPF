using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class ProGpuPackDependencyVersionTests
{
    [Fact]
    public void SourceBuiltTextAndVectorDependenciesUseTheImmutableRuntimeVersion()
    {
        var project = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "ProGPU.Wpf.csproj"));

        Assert.Contains(
            "'%(_ProjectReferencesWithVersions.Filename)' == 'ProGPU.Text'",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "'%(_ProjectReferencesWithVersions.Filename)' == 'ProGPU.Vector'",
            project,
            StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repository path '{Path.Combine(pathSegments)}'.");
    }
}
