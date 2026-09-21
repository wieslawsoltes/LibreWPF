using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public interface IWpfImageSourceAdapter
{
    MediaImageSource? AdaptImageSource(object? imageSource);
}
