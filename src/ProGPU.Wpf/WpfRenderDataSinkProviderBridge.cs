using System;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaRenderDataSink = System.Windows.Media.IPortableRenderDataDrawingContextSink;
using MediaRenderDataSinkProvider = System.Windows.Media.PortableRenderDataDrawingContextSinkProvider;

namespace System.Windows.Media.ProGPU;

public static class WpfRenderDataSinkProviderBridge
{
    public static bool TryRegisterRenderDataSinkProvider(
        ProGpuWpfDrawingFrame drawingFrame,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);

        return TryRegisterObjectSinkFactory(
            drawingFrame.CreateObjectRenderDataSinkFactory(imageSourceAdapter),
            out registration);
    }

    public static bool TryRegisterDrawingContextFactory(
        ProGpuWpfDrawingFrame drawingFrame,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);

        return TryRegisterDrawingContextFactory(
            drawingFrame.CreateDrawingContextFactory(),
            out registration);
    }

    public static bool TryRegisterDrawingContextFactory(
        Func<object?, MediaDrawingContext> drawingContextFactory,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(drawingContextFactory);

        registration = MediaRenderDataSinkProvider.PushDrawingContextFactory(drawingContextFactory);
        return true;
    }

    public static bool TryRegisterObjectSinkFactory(
        Func<object?, MediaRenderDataSink> objectSinkFactory,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(objectSinkFactory);

        registration = MediaRenderDataSinkProvider.PushObjectSinkFactory(objectSinkFactory);
        return true;
    }
}
