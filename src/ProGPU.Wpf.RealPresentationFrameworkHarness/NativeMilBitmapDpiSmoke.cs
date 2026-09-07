using System.Reflection;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

internal static class NativeMilBitmapDpiSmoke
{
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
