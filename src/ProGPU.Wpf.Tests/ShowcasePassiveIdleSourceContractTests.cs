using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

// Source admission checks are not live native rendering qualification. The
// executable metric tests cover the shared BCL observer; the separate opt-in
// Showcase process must supply all four actual native receipts.
public class ShowcasePassiveIdleSourceContractTests
{
    [Fact]
    public void CompletionRetainsTheReceiptAndQueuesSourceShutdownOnTheOwnerThread()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        int receipt = source.IndexOf("JsonSerializer.Serialize(output, receipt", StringComparison.Ordinal);
        int flush = source.IndexOf("output.Flush(flushToDisk: true);", receipt, StringComparison.Ordinal);
        int completion = source.IndexOf("Console.WriteLine($\"Showcase passive layout clip validation", flush, StringComparison.Ordinal);
        int queue = source.IndexOf("Dispatcher.BeginInvoke(DispatcherPriority.Send,", completion, StringComparison.Ordinal);
        int shutdown = source.IndexOf("new Action(() => Application.Current.Shutdown(exitCode))", queue, StringComparison.Ordinal);
        Assert.True(receipt >= 0 && flush > receipt && completion > flush && queue > completion && shutdown > queue);
        Assert.DoesNotContain("Environment.Exit(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.FailFast(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizePreparationRequiresItsPresentedFrameWithinTheOriginalAttemptBudget()
    {
        string fixture = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        Assert.Equal(2, fixture.Split("previousPresentedFrameCount: resizeFrameBefore", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, fixture.Split("resizeFrameBefore = host.PresentedFrameCount;", StringSplitOptions.None).Length - 1);
        string shared = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs");
        int start = shared.IndexOf("private async Task<LiveLayoutSize> WaitForLiveNativeResizeAsync(", StringComparison.Ordinal);
        int end = shared.IndexOf("private async Task<string> ValidateLiveInputAsync(", start, StringComparison.Ordinal);
        string preparation = shared[start..end];
        Assert.Contains("attempt < LiveValidationMaxAttempts", preparation, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(LiveValidationRetryDelay)", preparation, StringComparison.Ordinal);
        Assert.Contains("ReadLivePresentedFrameState(liveHost)", preparation, StringComparison.Ordinal);
        Assert.Contains("NativeResizePresentation.IsReady(", preparation, StringComparison.Ordinal);
        Assert.Contains("geometryReady && layoutSizeReady && presentationReady", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("HasImmediatePresentationWork", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetRenderActivitySnapshot", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitIdle", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("Poll", preparation, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryMarkersCompleteBeforeTheUnchangedMeasuredInterval()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        int entered = source.IndexOf("Action? onBoundaryEntered = journal is null ? null : () => journal.Write(name + \"-boundary-entered\");", StringComparison.Ordinal);
        int captured = source.IndexOf("Action? onSourceCaptured = journal is null ? null : () => journal.Write(name + \"-boundary-source-captured\");", entered, StringComparison.Ordinal);
        int before = source.IndexOf("IdleSourceState before = await ReadIdleBoundaryAsync(host, fixture,", captured, StringComparison.Ordinal);
        int interval = source.IndexOf("PassiveIdleInterval.Result interval = await PassiveIdleInterval.ObserveAsync(process, readFrames,", before, StringComparison.Ordinal);
        int retained = source.IndexOf("receipt.Phases.Add(phase);", interval, StringComparison.Ordinal);
        Assert.True(entered >= 0 && captured > entered && before > captured && interval > before && retained > interval);
        Assert.DoesNotContain("journal", source[before..retained], StringComparison.Ordinal);
        Assert.Contains("onBoundaryEntered, onSourceCaptured);", source[before..interval], StringComparison.Ordinal);
        int boundary = source.IndexOf("private Task<IdleSourceState> ReadIdleBoundaryAsync(", StringComparison.Ordinal);
        int once = source.IndexOf("return PassiveIdleBoundary.ObserveOnceAsync(", boundary, StringComparison.Ordinal);
        int wake = source.IndexOf("() => WakeLiveNativeLoop(host)", once, StringComparison.Ordinal);
        int callback = source.IndexOf("onBoundaryEntered?.Invoke();", wake, StringComparison.Ordinal);
        int owner = source.IndexOf("if (!Dispatcher.CheckAccess()", callback, StringComparison.Ordinal);
        Assert.True(boundary >= 0 && once > boundary && wake > once && callback > wake && owner > callback);
        int read = source.IndexOf("IdleSourceState source = ReadIdleSourceState(host, fixture);", owner, StringComparison.Ordinal);
        int sourceCaptured = source.IndexOf("onSourceCaptured?.Invoke();", read, StringComparison.Ordinal);
        int revalidate = source.IndexOf("if (!ReferenceEquals(host.SilkWindow, window)", sourceCaptured, StringComparison.Ordinal);
        int publish = source.IndexOf("return source;", revalidate, StringComparison.Ordinal);
        Assert.True(read > owner && sourceCaptured > read && revalidate > sourceCaptured && publish > revalidate);
        Assert.Contains("IdleSourceState after = await ReadIdleBoundaryAsync(host, fixture);", source, StringComparison.Ordinal);
        int after = source.IndexOf("IdleSourceState after = await ReadIdleBoundaryAsync(host, fixture);", retained, StringComparison.Ordinal);
        int captureAfter = source.IndexOf("phase.After = after;", after, StringComparison.Ordinal);
        int comparison = source.IndexOf("phase.StableIdentity = before == after;", captureAfter, StringComparison.Ordinal);
        int reject = source.IndexOf("if (!phase.StableIdentity)", comparison, StringComparison.Ordinal);
        int requireIdle = source.IndexOf("interval.RequireIdle();", reject, StringComparison.Ordinal);
        Assert.True(after > retained && captureAfter > after && comparison > captureAfter && reject > comparison && requireIdle > reject);
        Assert.Contains("public IdleSourceState Before => state;", source, StringComparison.Ordinal);
        Assert.Contains("public IdleSourceState? After { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("[property: JsonIgnore] string Text", source, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(state.Text, after.Text, StringComparison.Ordinal)", source, StringComparison.Ordinal);
        foreach (string field in new[] { "Geometry", "Presented", "TextTop", "ContentWidth", "ContentHeight", "Clip", "Zero", "Recovery", "Commands", "Draws", "Submissions" })
            Assert.Contains(field, source[source.IndexOf("private readonly record struct IdleSourceState(", StringComparison.Ordinal)..source.IndexOf("private readonly record struct IdleRect(", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("IdleSettlingTime = TimeSpan.FromSeconds(1)", source, StringComparison.Ordinal);
        Assert.Contains("IdleObservationTime = TimeSpan.FromSeconds(2)", source, StringComparison.Ordinal);
        // Seventeen existing phase records plus two markers for each of four
        // initial boundaries remain inside the unchanged 32-record journal.
        Assert.Contains("++_count > 32", Read("samples/ProGPU.Wpf.ShowcaseApp/PassiveIdlePhaseJournal.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeDiagnosticsBracketTheActualOperationAndNeverObserveAnIdleInterval()
    {
        string fixture = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        int registration = fixture.IndexOf("using (var resizeDiagnostics = journal is null ? null :", StringComparison.Ordinal);
        int setter = fixture.IndexOf("host.SetClientSize(resizedWidth, resizedHeight);", registration, StringComparison.Ordinal);
        int check = fixture.IndexOf("resizeDiagnostics?.ThrowIfFailed();", setter, StringComparison.Ordinal);
        int disposed = fixture.IndexOf("}", check, StringComparison.Ordinal);
        int completed = fixture.IndexOf("journal?.Write(\"native-resize-setter-returned\")", disposed, StringComparison.Ordinal);
        int interval = fixture.IndexOf("var resized = await ObserveIdlePhaseAsync", completed, StringComparison.Ordinal);
        Assert.True(registration >= 0 && setter > registration && check > setter && disposed > check && completed > disposed && interval > completed);
        Assert.Contains("ProGpuWpfDiagnostics.ObserveNativeResize(host, journal.WriteResize)", fixture, StringComparison.Ordinal);
        string host = Read("src/ProGPU.Wpf/ProGpuWpfWindowHost.cs");
        string capture = host[host.IndexOf("private void TraceResizeCheckpoint(", StringComparison.Ordinal)..host.IndexOf("public void DoEvents()", StringComparison.Ordinal)];
        Assert.Contains("if (scope is null || !scope.CanRecord) return;", capture, StringComparison.Ordinal);
        Assert.Contains("catch (Exception error) { scope.Fail(error); }", capture, StringComparison.Ordinal);
        foreach (string forbidden in new[] { "RequestRender(", "ProcessPending(", "DoRender(", "Consume", "FramebufferSize", "NativeWindowHandle", "GetGpuMemory", "File.", "Console." })
            Assert.DoesNotContain(forbidden, capture, StringComparison.Ordinal);
        Assert.Contains("Resize diagnostics are already registered on this host.", host, StringComparison.Ordinal);
        Assert.Contains("if (ReferenceEquals(_resizeDiagnostics, scope)) _resizeDiagnostics = null;", host, StringComparison.Ordinal);
        foreach (string[] ordered in new[]
        {
            new[] { "ProGpuWpfResizeStage.ClientSizeEntered", "ProGpuWpfResizeStage.NativeSizeResolving", "ResolveNativeWindowSizeForLogicalClientSize(", "ProGpuWpfResizeStage.NativeSizeAssigning", "window.Size = nativeSize;", "ProGpuWpfResizeStage.NativeSizeAssigned" },
            new[] { "ProGpuWpfResizeStage.SwapChainConfigureEntering", "if (!_target.Context.TryReconfigureIfNeeded(pixelWidth, pixelHeight, waitForNativeCompletion: false))", "ProGpuWpfResizeStage.SwapChainConfigureRejected", "ProGpuWpfResizeStage.SwapChainConfigureReturned" },
            new[] { "ProGpuWpfResizeStage.FramebufferResizeEntered", "ProGpuWpfResizeStage.FramebufferResizeSkipped", "ProGpuWpfResizeStage.FramebufferSourceUnavailable", "ProGpuWpfResizeStage.FramebufferRenderEntering", "OnRender(0d);", "ProGpuWpfResizeStage.FramebufferRenderReturned" },
            new[] { "ProGpuWpfResizeStage.SourceLayoutEntering", "if (!_portablePresentationSourceBridge.TrySetClientSize(clientWidth, clientHeight))", "ProGpuWpfResizeStage.SourceLayoutRejected", "ProGpuWpfResizeStage.SourceLayoutReturned" }
        })
        {
            int previous = -1;
            foreach (string item in ordered)
            {
                int current = host.IndexOf(item, previous + 1, StringComparison.Ordinal);
                Assert.True(current > previous, $"Missing or reordered checkpoint: {item}");
                previous = current;
            }
        }
    }

    [Fact]
    public void IdleStartupWaitsForLoadedAndDoesNotRestart()
    {
        bool started = false;
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: false, ref started));
        Assert.False(started);
        Assert.True(PassiveIdleStartup.TryStart(isLoaded: true, ref started));
        Assert.True(started);
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: true, ref started));
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: false, ref started));
        Assert.False(PassiveIdleStartup.TryStart(isLoaded: true, ref started));
        Assert.True(started);
    }

    [Fact]
    public void ObserverHasNoDispatcherLayoutRenderQueryOrStatusSideEffects()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/PassiveIdleInterval.cs");
        foreach (string forbidden in new[] { "WakeLive", "UpdateLayout(", "Dispatcher.", "TryGetWindowHost(",
            "TryGetNativePerformanceSnapshot(", "TryPollNativeMemoryCheckpoint(", "GetGpuHitTest", "Console.", "File.", "GC.Collect(" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        Assert.Contains("await WaitForDurationAsync(before.Timestamp, duration, Stopwatch.GetTimestamp, Task.Delay).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("for (int attempt = 0; attempt < 4; ++attempt)", source, StringComparison.Ordinal);
        Assert.Contains("result.WallMilliseconds < duration.TotalMilliseconds", source, StringComparison.Ordinal);
        string wait = source[source.IndexOf("internal static async Task WaitForDurationAsync(", StringComparison.Ordinal)..source.IndexOf("private static Sample Capture(", StringComparison.Ordinal)];
        Assert.DoesNotContain("Capture(", wait, StringComparison.Ordinal);
        Assert.DoesNotContain("readPresentedFrames", wait, StringComparison.Ordinal);
        Assert.Contains("GC.GetTotalAllocatedBytes(precise: true)", source, StringComparison.Ordinal);
        Assert.Contains("ExtraPresentations != 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("while (", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ActualShowcaseFixtureIsSeparateAndPreservesRealNativeOwnership()
    {
        string source = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        int configuration = source.IndexOf("ValidateIdleLayoutClipConfiguration();", StringComparison.Ordinal);
        int startup = source.IndexOf("if (!PassiveIdleStartup.TryStart(IsLoaded, ref _liveValidationStarted)) return true;", StringComparison.Ordinal);
        int launch = source.IndexOf("_ = Task.Run(", StringComparison.Ordinal);
        Assert.True(configuration >= 0 && startup > configuration && launch > startup,
            "The actual Loaded state must admit the idle task after configuration validation.");
        Assert.Contains("TimeSpan.FromSeconds(30)", source, StringComparison.Ordinal);
        Assert.Contains("host.HasPresentedFrame", source, StringComparison.Ordinal);
        string window = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs");
        int constructor = window.IndexOf("public MainWindow()", StringComparison.Ordinal);
        int loaded = window.IndexOf("private void OnShowcaseWindowLoaded(", StringComparison.Ordinal);
        int afterLoaded = window.IndexOf("internal static IReadOnlyList<string> FrameworkThemeNames", StringComparison.Ordinal);
        Assert.Contains("StartLiveValidationIfRequired();", window[constructor..loaded], StringComparison.Ordinal);
        Assert.Contains("StartLiveValidationIfRequired();", window[loaded..afterLoaded], StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnShowcaseWindowLoaded\"", Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml"), StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.WpfRootVisual, this)", source, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.PortablePresentationSourceBridge?.RootVisual, this)", source, StringComparison.Ordinal);
        Assert.Contains("host.LastNativeMilSessionFrame is null", source, StringComparison.Ordinal);
        Assert.Contains("!PortableWpfRuntime.IsMediaBackendFrozen", source, StringComparison.Ordinal);
        Assert.Contains("native.DeviceRecoveryCount != fixture.OriginalRecovery", source, StringComparison.Ordinal);
        Assert.Contains("panel.Children.Insert(0, zero)", source, StringComparison.Ordinal);
        Assert.Contains("new Border { Width = 80, Height = 0, ClipToBounds = true", source, StringComparison.Ordinal);
        Assert.Contains("fixture.Viewer.ScrollToVerticalOffset(", source, StringComparison.Ordinal);
        int previousResizeCheckpoint = -1;
        foreach (string checkpoint in new[] { "journal?.Write(\"native-resize-request\")",
            "journal?.Write(\"native-resize-callback-entered\")", "host.SetClientSize(resizedWidth, resizedHeight);",
            "journal?.Write(\"native-resize-setter-returned\")", "WakeLiveRenderHost(host);",
            "journal?.Write(\"native-resize-wake-returned\")",
            "await WaitForLiveNativeResizeAsync(host, (uint)resizedWidth, (uint)resizedHeight",
            "journal?.Write(\"native-resize-geometry-observed\")",
            "var resized = await ObserveIdlePhaseAsync(" })
        {
            int current = source.IndexOf(checkpoint, previousResizeCheckpoint + 1, StringComparison.Ordinal);
            Assert.True(current > previousResizeCheckpoint, $"Missing or reordered resize checkpoint: {checkpoint}");
            previousResizeCheckpoint = current;
        }
        foreach (string phase in new[] { "initial", "scrolled", "native-resized", "restored" })
            Assert.Contains($"\"{phase}\", receipt, journal)", source, StringComparison.Ordinal);
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
        Assert.Contains("python3 ./eng/test-progpu-wpf-showcase-idle.py -v", Read(".github/workflows/progpu-wpf-sdk.yml"), StringComparison.Ordinal);
        Assert.Contains("python3 ./eng/test-showcase-idle-crash.py -v", Read(".github/workflows/progpu-wpf-sdk.yml"), StringComparison.Ordinal);
        string gate = Read("eng/progpu-wpf-layout-clip.sh");
        foreach (string name in new[] { "PassiveIdleIntervalTests", "ShowcasePassiveIdleSourceContractTests", "PassiveIdleBoundaryTests" })
        {
            Assert.Contains($"FullyQualifiedName~ProGPU.Wpf.Tests.{name}.", gate, StringComparison.Ordinal);
            Assert.Contains($"\"ProGPU.Wpf.Tests.{name}\": ", gate, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PassiveEndpointUsesOneNativeUpdateAndReadOnlyResizeObservations()
    {
        string fixture = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.IdleLayoutClip.cs");
        Assert.Equal(2, fixture.Split("requestRenderWhileObserving: false", StringSplitOptions.None).Length - 1);
        Assert.Contains("window.Update += update", fixture, StringComparison.Ordinal);
        Assert.Contains("window.Update -= update", fixture, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(host.SilkWindow, window)", fixture, StringComparison.Ordinal);
        Assert.Contains("activity.HasImmediatePresentationWork", fixture, StringComparison.Ordinal);
        Assert.Contains("after != activity", fixture, StringComparison.Ordinal);
        Assert.Contains("ReadIdleBoundaryAsync(host, fixture)", fixture, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(1)", fixture, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", fixture, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromTicks(LiveValidationRetryDelay.Ticks * LiveValidationMaxAttempts)", fixture, StringComparison.Ordinal);
        string shared = Read("samples/ProGPU.Wpf.ShowcaseApp/MainWindow.xaml.cs");
        Assert.Contains("bool requestRenderWhileObserving = true", shared, StringComparison.Ordinal);
        Assert.Contains(": await InvokeWithLiveNativeLoopWakeAsync(liveHost, ReadLayout, DispatcherPriority.Send)", shared, StringComparison.Ordinal);
        string host = Read("src/ProGPU.Wpf/ProGpuWpfWindowHost.cs");
        int reader = host.IndexOf("internal bool TryGetRenderActivitySnapshot(", StringComparison.Ordinal);
        int end = host.IndexOf("internal void RecordNativePerformanceSnapshot(", reader, StringComparison.Ordinal);
        string body = host[reader..end];
        Assert.Contains("PlatformServices.Dispatcher.CheckAccess()", body, StringComparison.Ordinal);
        Assert.Contains("_isRendering,", body, StringComparison.Ordinal);
        foreach (string forbidden in new[] { "ConsumeRenderRequest(", "RequestRender(", "Reset(", "ProcessPending(", "GetGpuMemorySnapshot(" })
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        // Original ordering explains why an ordinary dispatcher callback is not
        // an after-render boundary; the native update is outside that callback.
        int render = host.IndexOf("private void OnRender(double deltaSeconds)", StringComparison.Ordinal);
        Assert.True(host.IndexOf("_isRendering = true;", render, StringComparison.Ordinal) <
            host.IndexOf("ProcessDispatcherQueueCore();", render, StringComparison.Ordinal));
        Assert.Contains("TaskCreationOptions.RunContinuationsAsynchronously", Read("samples/ProGPU.Wpf.ShowcaseApp/PassiveIdleBoundary.cs"));
        int begin = fixture.IndexOf("IdleSourceState before = await ReadIdleBoundaryAsync(", StringComparison.Ordinal);
        int endInterval = fixture.IndexOf("receipt.Phases.Add(phase)", begin, StringComparison.Ordinal);
        Assert.DoesNotContain("journal", fixture[begin..endInterval], StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "progpu-wpf-sdk-ci.sh")))
                return File.ReadAllText(Path.Combine(directory.FullName, relative));
        throw new FileNotFoundException("Could not find the current LibreWPF source checkout.");
    }
}
