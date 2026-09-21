using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfDrawingImageSourceContractTests
{
    [Fact]
    public void DrawingImageReplayUsesTypedInteropWithoutReflection()
    {
        var drawingImage = ReadRepoFile(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "System", "Windows", "Media", "DrawingImage.cs");
        var drawing = ReadRepoFile(
            "src", "Microsoft.DotNet.Wpf", "src", "PresentationCore", "System", "Windows", "Media", "Drawing.cs");
        var drawingBoundsContract = ReadRepoFile(
            "external", "ProGPU", "src", "ProGPU.Wpf.Interop", "PortableDrawingBounds.cs");
        var drawingReplay = ReadRepoFile(
            "src", "ProGPU.Wpf", "Composition", "Mil", "WpfDrawingReplay.cs");
        var objectReplay = ReadRepoFile(
            "src", "ProGPU.Wpf", "Composition", "WpfObjectRenderDataDrawingContext.cs");
        var milReplay = ReadRepoFile(
            "src", "ProGPU.Wpf", "Composition", "Mil", "WpfMilRenderDataDecoder.cs");

        Assert.Contains("DrawingImage : ImageSource, IPortableDrawingImageSource", drawingImage, StringComparison.Ordinal);
        Assert.Contains("TryGetPortableDrawingImage(out object drawing)", drawingImage, StringComparison.Ordinal);
        Assert.Contains("Drawing : Animatable, IDrawingContent, DUCE.IResource, IPortableDrawingBoundsSource", drawing, StringComparison.Ordinal);
        Assert.Contains("IPortableDrawingBoundsSource.TryGetPortableDrawingBounds(out PortableRect bounds)", drawing, StringComparison.Ordinal);
        Assert.Contains("interface IPortableDrawingBoundsSource", drawingBoundsContract, StringComparison.Ordinal);
        Assert.Contains("TryGetPortableDrawingBounds(out PortableRect bounds)", drawingBoundsContract, StringComparison.Ordinal);
        Assert.Contains("imageSource is not PortableDrawingImageSource drawingImageSource", drawingReplay, StringComparison.Ordinal);
        Assert.Contains("drawing is PortableDrawingBoundsSource drawingBoundsSource", drawingReplay, StringComparison.Ordinal);
        Assert.Contains("TryGetDrawingBounds(drawing, imageSourceAdapter, out var sourceBounds, out bool isEmpty)", drawingReplay, StringComparison.Ordinal);
        Assert.Contains("PushRectangleClip(sink, destinationBounds)", drawingReplay, StringComparison.Ordinal);
        Assert.Contains("portableBrush.Content is PortableDrawingImageSource drawingImageSource", drawingReplay, StringComparison.Ordinal);
        Assert.Contains("TryReplayPortableDrawingBrushFill(", drawingReplay, StringComparison.Ordinal);
        Assert.Contains("WpfDrawingReplay.TryReplayDrawingImage(", objectReplay, StringComparison.Ordinal);
        Assert.Contains("TryReplayDrawingImage(", milReplay, StringComparison.Ordinal);
        Assert.Contains("GetImageSourceAdapter(resources, imageSourceAdapter)", milReplay, StringComparison.Ordinal);

        foreach (var source in new[] { drawing, drawingReplay, objectReplay, milReplay })
        {
            Assert.DoesNotContain("using System.Reflection", source, StringComparison.Ordinal);
            Assert.DoesNotContain("BindingFlags", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetProperty(\"Drawing\")", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetProperty(\"Children\")", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Windows.Media.DrawingImage\"", source, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}
