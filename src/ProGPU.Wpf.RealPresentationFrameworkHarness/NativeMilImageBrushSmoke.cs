using System.Buffers.Binary;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;

internal static class NativeMilImageBrushSmoke
{
    internal static void Run()
    {
        var image = new Bitmap();
        var brush = new Brush(image);
        var visual = new Visual(new Content(brush));
        using var session = new WpfNativeMilCompilationSession();
        session.Update(visual, 64, 64);
        byte[] first = session.CompileFrame(1, 1, 0, 1).Scene.Stream;
        if (first.Length == 0) throw new InvalidOperationException("Native ImageBrush scene is empty.");
        var unchanged = session.Update(visual, 64, 64);
        if (unchanged.AppliedSidebandCount != 0 ||
            !first.AsSpan().SequenceEqual(session.CompileFrame(1, 1, 0, 2).Scene.Stream))
            throw new InvalidOperationException("Native ImageBrush unchanged reuse failed.");
        brush.Stretch = PortableStretch.None;
        session.Update(visual, 64, 64);
        byte[] changed = session.CompileFrame(1, 1, 0, 3).Scene.Stream;
        if (first.AsSpan().SequenceEqual(changed))
            throw new InvalidOperationException("Native ImageBrush stretch did not invalidate mapping.");
        image.DpiX = 12;
        if (session.Update(visual, 64, 64).AppliedSidebandCount != 1 ||
            changed.AsSpan().SequenceEqual(session.CompileFrame(1, 1, 0, 4).Scene.Stream))
            throw new InvalidOperationException("Native ImageBrush source DPI did not invalidate mapping.");
        Console.WriteLine("Native MIL ImageBrush smoke passed: typed producer, stable replay, stretch and DPI invalidation.");
    }

    private sealed class Bitmap : IPortableBitmapSourcePixelsSource
    {
        private readonly byte[] _pixels = [0, 0, 255, 255, 255, 0, 0, 255];
        public double DpiX { get; set; } = 6;
        public bool TryGetPortableBitmapSourcePixels(out PortableBitmapSourcePixels pixels)
        { pixels = new(2, 1, DpiX, 6, 8, PortablePixelDataFormat.Pbgra32, _pixels); return true; }
    }

    private sealed class Brush(Bitmap image) : IPortableTileBrushSource
    {
        internal PortableStretch Stretch { get; set; } = PortableStretch.Fill;
        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = new(PortableTileBrushKind.Image, image, 1, new(0, 0, 1, 1), new(0, 0, 1, 1),
                PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
                PortableTileMode.None, Stretch, PortableAlignmentX.Center, PortableAlignmentY.Center,
                false, PortableMatrix3x2.Identity, false, PortableMatrix3x2.Identity);
            return true;
        }
    }

    private sealed class Content : IPortableRenderDataSource
    {
        private readonly PortableRenderDataSnapshot _snapshot;
        internal Content(Brush brush)
        {
            byte[] bytes = new byte[48];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 48);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0x40);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(8), 8);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16), 8);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(24), 48);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(32), 48);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
            _snapshot = new(bytes, [brush]);
        }
        public bool TryGetPortableRenderDataSnapshot(out PortableRenderDataSnapshot snapshot)
        { snapshot = _snapshot; return true; }
    }

    private sealed class Visual(Content content) : IPortableVisualStateSource,
        IPortableVisualChildrenSource, IPortableDrawingContentSource, IPortableVisualBoundsSource
    {
        public bool TryGetPortableVisualState(out PortableVisualState state)
        { state = new() { HasOpacity = true, Opacity = 1 }; return true; }
        public bool TryGetPortableVisualChildCount(out int count) { count = 0; return true; }
        public bool TryGetPortableVisualChild(int index, out object? child) { child = null; return false; }
        public bool TryGetPortableDrawingContent(out object? value) { value = content; return true; }
        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        { bounds = new() { HasDescendantBounds = true, DescendantBounds = new(8, 8, 48, 48) }; return true; }
    }
}
