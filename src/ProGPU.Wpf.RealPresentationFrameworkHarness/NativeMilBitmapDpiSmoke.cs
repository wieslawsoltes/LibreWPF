using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

internal static class NativeMilBitmapDpiSmoke
{
    // Diagnostic public-API binding only, as in RunFactory. Direct source-WPF
    // references replace it when this harness no longer loads both assemblies.
    internal static object CreateWriteableBitmap(Assembly presentationCore, Assembly windowsBase)
    {
        Type Required(string name) => presentationCore.GetType("System.Windows.Media." + name, true)!;
        Type type = Required("Imaging.WriteableBitmap");
        Type bitmapSource = Required("Imaging.BitmapSource");
        Type formatType = Required("PixelFormat");
        Type paletteType = Required("Imaging.BitmapPalette");
        Type rectType = windowsBase.GetType("System.Windows.Int32Rect", true)!;
        object format = Required("PixelFormats").GetProperty("Pbgra32")!.GetValue(null)!;
        byte[] original = [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255];
        object bitmap = Activator.CreateInstance(type, 2, 2, 144.0, 192.0, format, null)!;
        object full = Activator.CreateInstance(rectType, 0, 0, 2, 2)!;
        MethodInfo write = type.GetMethod("WritePixels", [rectType, typeof(Array), typeof(int), typeof(int)])!;
        write.Invoke(bitmap, [full, original, 8, 0]);
        object clone = type.GetMethod("Clone", Type.EmptyTypes)!.Invoke(bitmap, null)!;
        object cached = bitmapSource.GetMethod("Create", [typeof(int), typeof(int), typeof(double), typeof(double),
            formatType, paletteType, typeof(Array), typeof(int)])!.Invoke(null, [2, 2, 144.0, 192.0, format, null, original, 8])!;
        object copy = Activator.CreateInstance(type, cached)!;

        int changed = 0;
        EventHandler handler = (_, _) => changed++;
        EventInfo changedEvent = type.GetEvent("Changed")!;
        MethodInfo acquire = type.GetMethod("Lock", Type.EmptyTypes)!;
        MethodInfo release = type.GetMethod("Unlock", Type.EmptyTypes)!;
        changedEvent.AddEventHandler(bitmap, handler);
        try
        {
            acquire.Invoke(bitmap, null);
            try
            {
                acquire.Invoke(bitmap, null);
                try
                {
                    nint address = (nint)type.GetProperty("BackBuffer")!.GetValue(bitmap)!;
                    if (address == 0) throw new InvalidOperationException("WriteableBitmap has no locked storage.");
                    Marshal.WriteByte(address, 99);
                    type.GetMethod("AddDirtyRect", [rectType])!.Invoke(bitmap, [full]);
                }
                finally { release.Invoke(bitmap, null); }
                if (changed != 0) throw new InvalidOperationException("A nested bitmap unlock published prematurely.");
            }
            finally { release.Invoke(bitmap, null); }
            if (changed != 1) throw new InvalidOperationException("Bitmap dirty publication was not coalesced.");
        }
        finally { changedEvent.RemoveEventHandler(bitmap, handler); }
        if (Snapshot(bitmap).Pixels[0] != 99)
            throw new InvalidOperationException("Locked bitmap writes did not reach the typed pixel source.");
        foreach (object unchanged in new[] { cached, copy, clone })
            if (!Snapshot(unchanged).Pixels.AsSpan().SequenceEqual(original))
                throw new InvalidOperationException("Bitmap copy/clone storage was not independent.");
        type.GetMethod("Freeze", Type.EmptyTypes)!.Invoke(clone, null);
        if (!(bool)type.GetProperty("IsFrozen")!.GetValue(clone)!)
            throw new InvalidOperationException("The bitmap did not freeze before native replay.");
        Console.WriteLine("Source-built WriteableBitmap smoke passed: memory source, copy/clone, lock/dirty publication, freeze and typed pixels.");
        return clone;

        static PortableBitmapSourcePixels Snapshot(object value)
        {
            if (value is not IPortableBitmapSourcePixelsSource source ||
                !source.TryGetPortableBitmapSourcePixels(out var pixels) || pixels.Width != 2 || pixels.Height != 2 ||
                pixels.Stride != 8 || pixels.DpiX != 144 || pixels.DpiY != 192 || pixels.Format != PortablePixelDataFormat.Pbgra32)
                throw new InvalidOperationException("WriteableBitmap lost its portable pixel/DPI contract.");
            return pixels;
        }
    }

    internal static void RequireSourceBitmapBinding(object drawingVisual)
    {
        var batch = new WpfNativeMilSceneCompiler().BuildBatch(drawingVisual, 160, 96);
        if (batch.BitmapSources is not { Count: 1 })
            throw new InvalidOperationException("Native MIL did not bind the source-built WriteableBitmap.");
        var bitmap = batch.BitmapSources[0];
        byte[] rgba = [3, 2, 1, 255, 6, 5, 4, 255, 9, 8, 7, 255, 12, 11, 10, 255];
        if (bitmap.Width != 2 || bitmap.Height != 2 || bitmap.RowBytes != 8 || bitmap.DpiX != 144 || bitmap.DpiY != 192 ||
            !bitmap.Rgba8Pixels.AsSpan().SequenceEqual(rgba))
            throw new InvalidOperationException("Native MIL bitmap sideband lost source DPI or RGBA channel order.");
    }

    internal static void RunChannel()
    {
        using var channel = new NativeMilChannel();
        var batch = new NativeMilBatchBuilder();
        batch.CreateResource(1, NativeMilResourceType.BitmapSource);
        batch.CreateResource(2, NativeMilResourceType.DoubleBufferedBitmap);
        channel.Apply(batch.ToArray());
        byte[] pixels = [1, 2, 3, 255];
        channel.SetBitmapSourceRgba8(1, 1, 1, 4, pixels, 144, 192);
        channel.SetDoubleBufferedBitmapRgba8(2, 1, 1, 4, pixels, 192, 144);
        foreach (uint handle in new uint[] { 1, 2 })
        {
            ulong generation = channel.GetResourceGeneration(handle);
            try
            {
                if (handle == 1) channel.SetBitmapSourceExternalImage(handle, 1, 1, double.NaN, 96);
                else channel.SetDoubleBufferedBitmapExternalImage(handle, 1, 1, 96, double.NaN);
                throw new InvalidOperationException("Invalid bitmap DPI was accepted.");
            }
            catch (NativeMilException error) when (error.Status == NativeMilStatus.InvalidArgument) { }
            if (channel.GetResourceGeneration(handle) != generation)
                throw new InvalidOperationException("Rejected bitmap DPI changed retained state.");
        }
        channel.SetBitmapSourceExternalImage(1, 1, 1, 72, 120);
        channel.SetDoubleBufferedBitmapExternalImage(2, 1, 1, 120, 72);
        channel.SetBitmapSourceRgba8(1, 1, 1, 4, pixels);
        channel.SetDoubleBufferedBitmapRgba8(2, 1, 1, 4, pixels);
        Console.WriteLine("Native MIL bitmap DPI smoke passed: four typed bindings, invalid DPI rollback, legacy overloads.");
    }

    // This diagnostic harness loads source-built WPF beside its shim assembly.
    // Reflection invokes only public WPF test APIs; no product adapter uses it.
    internal static void RunFactory(Assembly presentationCore)
    {
        Type factory = presentationCore.GetType("System.Windows.Media.PortableNativeImageSourceFactory", true)!;
        MethodInfo create = factory.GetMethod("Create", [typeof(IPortableNativeImageSource)])!;
        var provider = new ImageProvider();
        object image = create.Invoke(null, [provider])!;
        Validate(image);
        object clone = image.GetType().GetMethod("Clone", Type.EmptyTypes)!.Invoke(image, null)!;
        Validate(clone);
        try
        {
            create.Invoke(null, [new ImageProvider { DpiX = double.NaN }]);
            throw new InvalidOperationException("The public image factory accepted invalid DPI.");
        }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentOutOfRangeException) { }
        Console.WriteLine("Source-built native image DPI smoke passed: natural size, typed forwarding, clone, invalid DPI.");

        static void Validate(object value)
        {
            var source = (IPortableNativeImageSource)value;
            double width = (double)value.GetType().GetProperty("Width")!.GetValue(value)!;
            double height = (double)value.GetType().GetProperty("Height")!.GetValue(value)!;
            if (source.DpiX != 144 || source.DpiY != 192 || source.PixelWidth != 180 ||
                source.PixelHeight != 120 || width != 120 || height != 60)
                throw new InvalidOperationException("Source-built native image resolution was not preserved.");
        }
    }

    private sealed class ImageProvider : IPortableNativeImageSource
    {
        public int PixelWidth => 180;
        public int PixelHeight => 120;
        public double DpiX { get; init; } = 144;
        public double DpiY => 192;
        public bool TryGetPortableNativeImage(out object? nativeImage)
        { nativeImage = null; return false; }
    }
}
