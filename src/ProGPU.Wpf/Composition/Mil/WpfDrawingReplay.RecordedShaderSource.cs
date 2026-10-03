using System;
using System.Numerics;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static partial class WpfDrawingReplay
{
    // This is an owned display-list snapshot, not a substitute source Visual or
    // DrawingImage. The original brush descriptor and source identity remain
    // separate; only playback uses these already captured commands.
    internal sealed class RecordedTileContent(GpuPicture picture, Rect bounds, bool isEmpty) : IDisposable
    {
        private GpuPicture? _picture = picture;
        internal bool IsEmpty { get; } = isEmpty;

        internal bool TryGetBounds(out Rect value, out bool empty)
        {
            ObjectDisposedException.ThrowIf(_picture is null, this);
            value = bounds;
            empty = IsEmpty;
            return true;
        }

        internal WpfDrawingReplayStatus Replay(IWpfCompositionCommandSink sink)
        {
            GpuPicture owned = _picture ?? throw new ObjectDisposedException(nameof(RecordedTileContent));
            if (sink is not IWpfProGpuSceneDrawingContextSource source ||
                !source.TryGetProGpuSceneDrawingContextState(out var context, out var transform) || context is null)
                return WpfDrawingReplayStatus.Unsupported;
            context.DrawPicture(owned, transform);
            return IsEmpty ? WpfDrawingReplayStatus.Skipped : WpfDrawingReplayStatus.Applied;
        }

        public void Dispose() => System.Threading.Interlocked.Exchange(ref _picture, null)?.Dispose();
    }

    internal static WpfDrawingReplayStatus ReplayRecordedShaderTile(
        PortableTileBrush brush, RecordedTileContent? content, MediaImageSource? image,
        Rect destination, IWpfCompositionCommandSink sink, WpfImageSourceFrame? imageFrame = null)
    {
        var geometry = new TileBrushFillGeometry(null, destination, null, null, IsRectangle: true);
        if (content is { IsEmpty: true } || (brush.Kind == PortableTileBrushKind.Visual && brush.Content is null))
            return WpfDrawingReplayStatus.Skipped;
        if (brush.Kind == PortableTileBrushKind.Visual)
        {
            if (content is null) return WpfDrawingReplayStatus.Unsupported;
            return TryReplayPortableVisualBrushFill(brush, geometry, sink, null, out var status, content)
                ? status : WpfDrawingReplayStatus.Unsupported;
        }
        if (brush.Kind != PortableTileBrushKind.Image) return WpfDrawingReplayStatus.Unsupported;
        if (content is not null)
            return TryReplayPortableDrawingBrushFill(brush, null, geometry, sink, null, out var status, content)
                ? status : WpfDrawingReplayStatus.Unsupported;
        return image is not null && TryReplayPortableImageBrushFill(brush, geometry, sink, null, image, imageFrame)
            ? WpfDrawingReplayStatus.Applied : WpfDrawingReplayStatus.Unsupported;
    }
}
