using System.Reflection;

internal static class NativeMilGeometryRelationSmoke
{
    // Diagnostic-only public API binding: this harness deliberately loads real
    // WPF beside shim assemblies. Replace this binding with direct references
    // when the harness no longer needs that dual-assembly load context. No
    // product geometry or visual adapter uses reflection.
    internal static void Run(Assembly presentationCore, object drawingVisual)
    {
        Type Required(string name) => presentationCore.GetType("System.Windows.Media." + name, true)!;
        Type geometry = Required("Geometry");
        MethodInfo parse = geometry.GetMethod("Parse", [typeof(string)])!;
        object Shape(string path) => parse.Invoke(null, [path])!;
        MethodInfo compare = geometry.GetMethod("FillContainsWithDetail", [geometry])!;
        object outer = Shape("M 0,0 L 100,0 100,100 0,100 Z");
        object inner = Shape("M 10,10 L 20,10 20,20 10,20 Z");
        if (compare.Invoke(outer, [inner])!.ToString() != "FullyContains" ||
            compare.Invoke(inner, [outer])!.ToString() != "FullyInside")
            throw new InvalidOperationException("Source WPF geometry selection reversed containment.");

        Type resultType = Required("HitTestResult");
        Type behaviorType = Required("HitTestResultBehavior");
        Type callbackType = Required("HitTestResultCallback");
        Type collectorType = typeof(Collector<,>).MakeGenericType(resultType, behaviorType);
        var collector = (CollectorBase)Activator.CreateInstance(collectorType)!;
        Delegate callback = Delegate.CreateDelegate(callbackType, collector, collectorType.GetMethod("Collect")!);
        Type parametersType = Required("GeometryHitTestParameters");
        MethodInfo hitTest = Required("VisualTreeHelper").GetMethod("HitTest",
            [Required("Visual"), Required("HitTestFilterCallback"), callbackType, Required("HitTestParameters")])!;
        PropertyInfo clip = drawingVisual.GetType().GetProperty("Clip")!;
        object? previousClip = clip.GetValue(drawingVisual);
        try
        {
            // The host's existing visual paints [8,8]-[152,88]. Selection must
            // obey its actual clip, not merely the painted content envelope.
            clip.SetValue(drawingVisual, Shape("M 8,8 L 88,8 8,88 Z"));
            Select("M 16,16 L 24,16 24,24 16,24 Z", expectedHit: true);
            Select("M 120,16 L 128,16 128,24 120,24 Z", expectedHit: false);
        }
        finally { clip.SetValue(drawingVisual, previousClip); }
        Console.WriteLine("Source-built native geometry selection smoke passed: containment direction and clipped visual traversal.");

        void Select(string path, bool expectedHit)
        {
            collector.Results.Clear();
            object parameters = Activator.CreateInstance(parametersType, Shape(path))!;
            hitTest.Invoke(null, [drawingVisual, null, callback, parameters]);
            bool found = false;
            foreach (object result in collector.Results)
                found |= ReferenceEquals(resultType.GetProperty("VisualHit")!.GetValue(result), drawingVisual);
            if (found != expectedHit)
                throw new InvalidOperationException("Source WPF geometry selection did not preserve the visual clip.");
        }
    }

    private abstract class CollectorBase
    {
        public List<object> Results { get; } = [];
    }

    private sealed class Collector<TArgument, TResult> : CollectorBase
    {
        public Collector() { }
        private static readonly TResult Continue = (TResult)Enum.Parse(typeof(TResult), "Continue");
        public TResult Collect(TArgument result)
        {
            Results.Add(result!);
            return Continue;
        }
    }
}
