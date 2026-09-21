using System.Xml.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class RealApplicationRunHarnessRuntimeClosureTests
{
    [Fact]
    public void IncludesTypedWinRtRuntimeDependency()
    {
        var project = XDocument.Load(FindRepoPath(
            "src",
            "ProGPU.Wpf.RealApplicationRunHarness",
            "ProGPU.Wpf.RealApplicationRunHarness.csproj"));

        var reference = Assert.Single(
            project.Descendants("ProjectReference"),
            item => ((string?)item.Attribute("Include"))?
                .Replace('/', '\\')
                .EndsWith(
                    @"external\ProGPU\src\ProGPU.WinRT\ProGPU.WinRT.csproj",
                    StringComparison.OrdinalIgnoreCase) == true);

        Assert.Equal("all", (string?)reference.Attribute("PrivateAssets"));
        Assert.Null(reference.Attribute("ReferenceOutputAssembly"));
    }

    private static string FindRepoPath(params string[] components)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine([directory.FullName, .. components]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(components)}'.");
    }
}
