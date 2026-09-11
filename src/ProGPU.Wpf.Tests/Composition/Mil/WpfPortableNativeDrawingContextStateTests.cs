using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfPortableNativeDrawingContextStateTests
{
    [Fact]
    public void LibreWinFormsDirectPaintKeepsOuterAndClientTransformsInImmutableGraphicsBase()
    {
        string interop = File.ReadAllText(FindRepoPath(
            "external", "ProGPU", "src", "ProGPU.Wpf.Interop", "PortableNativeDrawingContext.cs"));
        string graphics = File.ReadAllText(FindRepoPath(
            "external", "ProGPU", "src", "System.Drawing.Common", "System", "Drawing", "Graphics.cs"));
        string host = File.ReadAllText(FindRepoPath(
            "external", "LibreWinForms", "src", "LibreWinForms.Portable",
            "LibreWinForms.WindowsFormsIntegration", "src", "WindowsFormsHost.cs"));
        string hostProject = File.ReadAllText(FindRepoPath(
            "external", "LibreWinForms", "src", "LibreWinForms.Portable",
            "LibreWinForms.WindowsFormsIntegration", "LibreWinForms.WindowsFormsIntegration.csproj"));

        Assert.Contains("public readonly struct PortableNativeDrawingContextState", interop, StringComparison.Ordinal);
        Assert.Contains("public Matrix4x4 Transform", interop, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", interop, StringComparison.Ordinal);

        Assert.Contains("private readonly Matrix3x2 _baseTransform;", graphics, StringComparison.Ordinal);
        Assert.Contains("private Matrix3x2 CombinedTransform =>", graphics, StringComparison.Ordinal);
        Assert.Contains("_transform.Value * GetPageTransform() * _containerTransform * _baseTransform;", graphics, StringComparison.Ordinal);
        Assert.Contains("public void ResetTransform()", graphics, StringComparison.Ordinal);
        Assert.Contains("_transform.Reset();", graphics, StringComparison.Ordinal);
        Assert.DoesNotContain("_baseTransform = Matrix3x2.Identity;", graphics, StringComparison.Ordinal);

        Assert.Contains("drawingContext is IPortableNativeDrawingContextStateSource", host, StringComparison.Ordinal);
        Assert.Contains("ProGPU.Scene.ProGpuDrawingContextState.TryCreate(", host, StringComparison.Ordinal);
        Assert.Contains("nativeContext = state.DrawingContext;", host, StringComparison.Ordinal);
        Assert.Contains("outerTransform = state.OuterTransform;", host, StringComparison.Ordinal);
        Assert.DoesNotContain("state.NativeDrawingContext is ProGPU.Scene.DrawingContext", host, StringComparison.Ordinal);
        Assert.Contains("Matrix4x4 clientTransform = Matrix4x4.CreateTranslation", host, StringComparison.Ordinal);
        Assert.Contains("* outerTransform;", host, StringComparison.Ordinal);
        Assert.Contains("FromProGpuDrawingContext(nativeContext, clientTransform)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("graphics.TranslateTransform((float)bounds.X", host, StringComparison.Ordinal);
        Assert.DoesNotContain("graphics.TranslateTransform((float)controlBounds.X", host, StringComparison.Ordinal);
        Assert.DoesNotContain("graphics.TranslateTransform((float)treeBounds.X", host, StringComparison.Ordinal);
        Assert.Contains("TryRenderListItemOwnerDraw(drawingContext, listBox, i, bounds, rowBounds)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", host, StringComparison.Ordinal);

        Assert.Contains("<PackageReference Include=\"LibreWPF.ProGPU\"", hostProject, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(LibreWinFormsReferenceMode)' != 'Project'\"", hostProject, StringComparison.Ordinal);
        Assert.Contains("..\\..\\..\\..\\..\\src\\ProGPU.Wpf\\ProGPU.Wpf.csproj", hostProject, StringComparison.Ordinal);
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
