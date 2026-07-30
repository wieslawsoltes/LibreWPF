# LibreWPF ProGPU media presentation

Source-built LibreWPF keeps `PresentationCore` package-neutral. Its drawing
contexts publish `IPortableNativeDrawingContextStateSource`, which carries the
active backend context as `object` plus the current outer transform.

`LibreWPF.ProGPU` converts that state without reflection or wrapper allocation:

```csharp
var portableSource =
    (IPortableNativeDrawingContextStateSource)drawingContext;

if (WpfProGpuDrawingContextBridge.TryGetProGpuDrawingContextState(
        portableSource,
        out ProGpuDrawingContextState state))
{
    presenter.Record(
        in state,
        requiredContext,
        new ProGPU.Scene.Rect(0, 0, width, height));
}
```

`MediaGpuSurfacePresenter.Record(in ProGpuDrawingContextState, ...)` composes
the WPF outer transform exactly once after command-local transforms. The
conversion is O(1), does not retain the WPF drawing context, and performs no
managed allocation. A wrong native context or non-finite transform fails
closed.

The media presenter owns only its frame-notification subscription and
invalidation coalescing. The application still owns and disposes the
`MediaGpuSurface`, player/provider, and platform host in their normal lifetime
order.
