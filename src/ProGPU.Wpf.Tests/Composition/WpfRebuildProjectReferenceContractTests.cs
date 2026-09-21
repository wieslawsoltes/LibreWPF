using System.Xml.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfRebuildProjectReferenceContractTests
{
    [Theory]
    [InlineData("ProjectReference")]
    [InlineData("ProjectReferenceWithConfiguration")]
    [InlineData("_ProjectReferenceWithConfiguration")]
    [InlineData("_MSBuildProjectReferenceNonExistent")]
    public void RebuildRemovesOnlyKnownMissingWpfProjects(string itemName)
    {
        XElement target = LoadReferenceTarget();
        XElement removal = Assert.Single(target.Descendants(itemName),
            item => item.Attribute("Remove") is not null);
        Assert.Equal("@(_wpfUnmatchedProjectReference)", removal.Attribute("Remove")?.Value);

        XElement missingFilter = Assert.Single(target.Elements("FilterItem1ByItem2"),
            item => item.Element("Output")?.Attribute("ItemName")?.Value == "_wpfUnmatchedProjectReference");
        Assert.Equal("@(_unmatchedProjectReference)", missingFilter.Attribute("Item1")?.Value);
        Assert.Equal("@(WpfProjectPath)", missingFilter.Attribute("Item2")?.Value);
        Assert.Equal("ProjectPath", missingFilter.Attribute("Metadata2")?.Value);
        Assert.Equal("true", missingFilter.Attribute("TreatItemsAsPaths")?.Value);
        Assert.Equal("Result", missingFilter.Element("Output")?.Attribute("TaskParameter")?.Value);
        Assert.True(XNode.DocumentOrderComparer.Compare(missingFilter, removal) < 0);

        XElement localFilter = Assert.Single(target.Elements("FilterItem1ByItem2"),
            item => item.Attribute("Item1")?.Value == "@(ProjectReference)");
        Assert.Equal("@(_wpfProjectPathAvailableLocally)", localFilter.Attribute("Item2")?.Value);
        Assert.Contains(localFilter.Elements("Output"), output =>
            output.Attribute("ItemName")?.Value == "_unmatchedProjectReference" &&
            output.Attribute("TaskParameter")?.Value == "UnmatchedReferences");
        Assert.True(XNode.DocumentOrderComparer.Compare(localFilter, missingFilter) < 0);
    }

    [Fact]
    public void ReferenceAssemblyDependencyStaysAnOrdinaryProjectReference()
    {
        var project = XDocument.Load(FindRepoPath("src", "Microsoft.DotNet.Wpf", "src",
            "WindowsBase", "ref", "WindowsBase-ref.csproj"));
        const string referencePath = @"$(WpfSourceDir)System.Xaml\ref\System.Xaml-ref.csproj";
        Assert.Contains(project.Descendants("ProjectReference"),
            item => item.Attribute("Include")?.Value == referencePath);
        XElement target = LoadReferenceTarget();
        Assert.DoesNotContain(target.Document!.Descendants("WpfProjectPath"),
            item => item.Attribute("ProjectPath")?.Value == referencePath);
        foreach (string itemName in new[] { "ProjectReferenceWithConfiguration", "_ProjectReferenceWithConfiguration" })
        {
            Assert.DoesNotContain(target.Descendants(itemName),
                item => item.Attribute("Include") is not null || item.Attribute("Update") is not null);
        }
        Assert.Contains(target.Descendants("MicrosoftDotNetWpfGitHubReference"),
            item => item.Attribute("Include")?.Value == "@(_microsoftDotNetWpfGitHubReference->'%(AssemblyName)')");
    }

    private static XElement LoadReferenceTarget() => Assert.Single(
        XDocument.Load(FindRepoPath("eng", "WpfArcadeSdk", "tools", "WpfProjectReference.targets"))
            .Descendants("Target"), target => target.Attribute("Name")?.Value == "EnsureWpfProjectReference");

    private static string FindRepoPath(params string[] segments)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
