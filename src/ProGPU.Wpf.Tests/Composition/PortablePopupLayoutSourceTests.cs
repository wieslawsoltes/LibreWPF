using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class PortablePopupLayoutSourceTests
{
    [Fact]
    public void PortablePopupRootMeasurementDoesNotInvalidateLayout()
    {
        var popupPath = FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "Primitives",
            "Popup.cs");
        var popup = File.ReadAllText(popupPath);

        Assert.Contains("if (!rootElement.IsMeasureValid)", popup, StringComparison.Ordinal);
        Assert.Contains("rootElement.Measure(constraint);", popup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "rootElement.InvalidateMeasure();\n                rootElement.Measure(constraint);",
            popup,
            StringComparison.Ordinal);
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

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
