using MediaBrush = System.Windows.Media.Brush;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public interface IWpfMilResourceResolver
{
    MediaBrush? ResolveBrush(uint resourceToken);

    MediaPen? ResolvePen(uint resourceToken);

    MediaGeometry? ResolveGeometry(uint resourceToken);

    MediaImageSource? ResolveImageSource(uint resourceToken);

    MediaGlyphRun? ResolveGlyphRun(uint resourceToken);

    MediaTransform? ResolveTransform(uint resourceToken);
}

public interface IWpfGuidelineSetResourceResolver
{
    object? ResolveGuidelineSet(uint resourceToken);
}

internal interface IWpfRawMilResourceResolver
{
    bool TryResolveRawResource(uint resourceToken, out object resource);
}
