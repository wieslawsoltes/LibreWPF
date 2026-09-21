namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfRetainedVisualDependencyRegistrar
{
    public static void Register(IWpfCompositionCommandSink sink, object? dependency)
    {
        if (dependency == null || sink is not IWpfRetainedVisualBranchSink retainedVisualBranchSink)
        {
            return;
        }

        if (!WpfVisualInvalidationTracker.RegisterTrackedDependencies(retainedVisualBranchSink, dependency))
        {
            retainedVisualBranchSink.RegisterVisualDependency(dependency);
        }
    }

    public static void RegisterDirect(IWpfCompositionCommandSink sink, object? dependency)
    {
        if (dependency != null && sink is IWpfRetainedVisualBranchSink retainedVisualBranchSink)
        {
            retainedVisualBranchSink.RegisterVisualDependency(dependency);
        }
    }
}
