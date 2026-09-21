using System.Buffers.Binary;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

internal static class NativeMilImageBrushSmoke
{
    // Diagnostic public-API binding only: the existing host harness loads real
    // source WPF beside shim assemblies. Replace with direct references when
    // that dual-assembly loading is removed; product paths remain typed.
    internal static void RunSourceDrawingImage(Assembly presentationCore, Assembly windowsBase)
    {
        Type Required(string name) => presentationCore.GetType("System.Windows.Media." + name, true)!;
        Type rectType = windowsBase.GetType("System.Windows.Rect", true)!;
        object Rect(double x, double y, double width, double height) => Activator.CreateInstance(rectType, x, y, width, height)!;
        object group = Activator.CreateInstance(Required("DrawingGroup"))!;
        object children = group.GetType().GetProperty("Children")!.GetValue(group)!;
        MethodInfo add = children.GetType().GetMethod("Add", [Required("Drawing")])!;
        MethodInfo clear = children.GetType().GetMethod("Clear", Type.EmptyTypes)!;
        object geometry = Required("Geometry").GetMethod("Parse", [typeof(string)])!
            .Invoke(null, ["M 4,6 L 20,6 20,18 4,18 Z"])!;
        object red = Required("Brushes").GetProperty("Red")!.GetValue(null)!;
        object drawing = Activator.CreateInstance(Required("GeometryDrawing"), red, null, geometry)!;
        add.Invoke(children, [drawing]);
        object image = Activator.CreateInstance(Required("DrawingImage"), group)!;
        object brush = Activator.CreateInstance(Required("ImageBrush"), image)!;
        object visual = Activator.CreateInstance(Required("DrawingVisual"))!;
        object context = visual.GetType().GetMethod("RenderOpen")!.Invoke(visual, null)!;
        try
        {
            context.GetType().GetMethod("DrawImage", [Required("ImageSource"), rectType])!
                .Invoke(context, [image, Rect(0, 0, 24, 24)]);
            context.GetType().GetMethod("DrawRectangle", [Required("Brush"), Required("Pen"), rectType])!
                .Invoke(context, [brush, null, Rect(32, 0, 24, 24)]);
        }
        finally { ((IDisposable)context).Dispose(); }

        var bounds = (IPortableDrawingBoundsSource)group;
        var imageSource = (IPortableDrawingImageSource)image;
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(visual);
        tracker.ConsumeDirty();
        using var session = new WpfNativeMilCompilationSession();
        session.Update(visual, 64, 32);
        var populated = session.CompileFrame(1, 1, 0, 1).Scene;
        uint populatedDraws = NativeCompositor.ValidateScene(populated.Stream).DrawCount;
        if (populatedDraws == 0)
            throw new InvalidOperationException("Source DrawingImage/ImageBrush produced no native draws.");

        clear.Invoke(children, null);
        if (!bounds.TryGetPortableDrawingBounds(out var empty) || !empty.IsEmpty ||
            !imageSource.TryGetPortableDrawingImage(out object? content) || !ReferenceEquals(content, group))
            throw new InvalidOperationException("Cleared source drawing lost known-empty bounds or source identity.");
        if (!tracker.ConsumeDirty())
            throw new InvalidOperationException("Clearing source drawing content did not invalidate retained replay.");
        session.Update(visual, 64, 32);
        var cleared = session.CompileFrame(1, 1, 0, 2).Scene;
        if (NativeCompositor.ValidateScene(cleared.Stream).DrawCount != 0 || populated.Stream.AsSpan().SequenceEqual(cleared.Stream))
            throw new InvalidOperationException("Cleared source image retained native drawing content.");
        var unchanged = session.Update(visual, 64, 32);
        if (unchanged.RecreatedChannel || unchanged.AppliedSidebandCount != 0 ||
            !cleared.Stream.AsSpan().SequenceEqual(session.CompileFrame(1, 1, 0, 3).Scene.Stream))
            throw new InvalidOperationException("Stable empty source image did not reuse native state.");

        add.Invoke(children, [drawing]);
        if (!tracker.ConsumeDirty() || !bounds.TryGetPortableDrawingBounds(out var restoredBounds) || restoredBounds.IsEmpty)
            throw new InvalidOperationException("Refilling source drawing did not restore bounds and invalidation.");
        session.Update(visual, 64, 32);
        var restored = session.CompileFrame(1, 1, 0, 4).Scene;
        if (NativeCompositor.ValidateScene(restored.Stream).DrawCount != populatedDraws ||
            !restored.Stream.AsSpan().SequenceEqual(populated.Stream))
            throw new InvalidOperationException("Refilling source image did not restore its original native scene.");
        Console.WriteLine("Source DrawingImage/ImageBrush clear/refill smoke passed: identity, invalidation, empty reuse and native scene restoration.");
    }

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
