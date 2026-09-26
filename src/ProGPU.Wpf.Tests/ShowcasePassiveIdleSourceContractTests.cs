using Xunit;

namespace ProGPU.Wpf.Tests;

// Source admission checks are not live native rendering qualification. The
// executable metric tests cover the shared BCL observer; the separate opt-in
// Showcase process must supply all four actual native receipts.
public class ShowcasePassiveIdleSourceContractTests
{
    [Fact]
    public void ObserverHasNoDispatcherLayoutRenderQueryOrStatusSideEffects()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/PassiveIdleInterval.cs");
        foreach (string forbidden in new[] { "WakeLive", "UpdateLayout(", "Dispatcher.", "TryGetWindowHost(",
            "TryGetNativePerformanceSnapshot(", "TryPollNativeMemoryCheckpoint(", "GetGpuHitTest", "Console.", "File.", "GC.Collect(" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(duration).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("GC.GetTotalAllocatedBytes(precise: true)", source, StringComparison.Ordinal);
        Assert.Contains("ExtraPresentations != 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("while (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActualShowcaseFixtureIsSeparateAndPreservesRealNativeOwnership()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        Assert.Contains("ReferenceEquals(host.WpfRootVisual, this)", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.PortablePresentationSourceBridge?.RootVisual, this)", source, StringComparison.Ordinal);
        Assert.Contains("host.LastNativeMilSessionFrame is null", source, StringComparison.Ordinal);
        Assert.Contains("!PortableWpfRuntime.IsMediaBackendFrozen", source, StringComparison.Ordinal);
        Assert.Contains("native.DeviceRecoveryCount != fixture.OriginalRecovery", source, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Insert(0, zero)", source, StringComparison.Ordinal);
        Assert.Contains("new Border { Width = 80, Height = 0, ClipToBounds = true", source, StringComparison.Ordinal);
        Assert.Contains("fixture.Viewer.ScrollToVerticalOffset(", source, StringComparison.Ordinal);
        Assert.Contains("SetLiveNativeWindowSize(host, resizedWidth, resizedHeight)", source, StringComparison.Ordinal);
        foreach (string phase in new[] { "initial", "scrolled", "native-resized", "restored" })
            Assert.Contains($"\"{phase}\", receipt)", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("fixture.Panel.Children.Remove(fixture.Zero)", source, StringComparison.Ordinal);
        Assert.Contains("receipt.UiRestored = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableFrameCoalescing =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableNativeMemoryDiagnostics =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryPollNativeMemoryCheckpoint(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetGpuHitTest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.Collect(", source, StringComparison.Ordinal);
        Assert.Contains("MainWindow.ValidateIdleLayoutClipConfiguration();", Read("samples/ProGPU.Wpf.ShowcaseApp/App.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("StartIdleLayoutClipValidationIfRequested()", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs"), StringComparison.Ordinal);
        // Keep forced-frame performance validation separate, not relabelled idle.
        Assert.Contains("PresentNativePerformanceFrameAsync(host)", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.NativePerformance.cs"), StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "progpu-wpf-sdk-ci.sh")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative));
        throw new FileNotFoundException("Could not find the current LibreWPF source checkout.");
    }
}
